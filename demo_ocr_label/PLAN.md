# 📋 Kế hoạch nâng cấp hệ thống nhận diện nhãn

## 🎯 Mục tiêu

Xây dựng thuật toán tự động, mạnh mẽ có khả năng phát hiện nhãn trên **mọi màu áo** (tối, màu, sáng, trắng) bằng cách tự động chọn chiến lược xử lý tối ưu.

---

## 📊 Kiến trúc tổng thể: Hệ thống 3 tầng

```
Ảnh đầu vào (Guide Box ROI)
         ↓
┌─────────────────────────────────┐
│  TẦNG 1: Phân tích tự động      │
│  Tính 3 Metrics → Final Score   │
└─────────────────────────────────┘
         ↓
    Phân loại theo Score
         ↓
   ┌─────┴──────┬──────────┐
   ↓            ↓          ↓
HIGH (>0.6)  MEDIUM    LOW (<0.3)
            (0.3-0.6)
   ↓            ↓          ↓
┌────────┐  ┌────────┐  ┌────────┐
│Binary  │  │Canny   │  │QR-First│
│Thresh  │  │Edge    │  │Geometry│
└────────┘  └────────┘  └────────┘
   ↓            ↓          ↓
   └─────┬──────┴──────────┘
         ↓
  Kết quả: (Rect, Box, QR)
```

---

## 📌 TẦNG 1: Phân tích độ tương phản

### Mục tiêu
Đánh giá độ khó của ảnh bằng 3 metrics, tính điểm tổng hợp để quyết định chiến lược.

### Metric 1: Separation (Độ tách biệt Histogram)

**Khái niệm:** Đo khoảng cách giữa 2 đỉnh chính trong histogram (nền áo vs nhãn trắng).

**Mã giả:**
```
1. Tính histogram ảnh xám (256 bins)
2. Làm mượt histogram (moving average 5 bins)
3. Tìm 2 local maxima lớn nhất (cách nhau >30 bins)
4. separation = |peak1_pos - peak2_pos| / 255.0
```

**Đánh giá:**
- `> 0.6`: HIGH (áo tối vs nhãn trắng)
- `0.3-0.6`: MEDIUM (áo màu nhạt)
- `< 0.3`: LOW (áo trắng)

---

### Metric 2: Edge Strength (Độ mạnh biên)

**Khái niệm:** Tỷ lệ % pixel được phát hiện là biên (cạnh).

**Mã giả:**
```
1. edges = Canny(gray, threshold1=50, threshold2=150)
2. edge_pixels = count_white_pixels(edges)
3. total_pixels = gray.width × gray.height
4. edge_strength = edge_pixels / total_pixels
```

**Đánh giá:**
- `> 0.05` (5%): HIGH - Biên rõ ràng
- `0.02-0.05`: MEDIUM - Biên yếu
- `< 0.02`: LOW - Biên mờ

---

### Metric 3: Contrast Ratio (Độ tương phản)

**Khái niệm:** Dùng độ lệch chuẩn để đo sự phân tán cường độ sáng.

**Mã giả:**
```
1. mean, stddev = calculate_statistics(gray)
2. contrast_ratio = stddev / 128.0  // Normalize
```

**Đánh giá:**
- `> 0.625`: HIGH - Pixel rải đều tối-sáng
- `0.3-0.625`: MEDIUM - Tương phản vừa
- `< 0.3`: LOW - Pixel đồng đều

---

### 🎯 Tính Final Score

**Công thức:**
```
edge_strength_norm = min(edge_strength / 0.1, 1.0)

final_score = (separation × 0.4) 
            + (edge_strength_norm × 0.3) 
            + (contrast_ratio × 0.3)
```

**Trọng số:**
- Separation: 40% (quan trọng nhất)
- Edge Strength: 30%
- Contrast Ratio: 30%

**Phân loại:**
```
if final_score > 0.6:
    return HIGH_CONTRAST
elif final_score > 0.3:
    return MEDIUM_CONTRAST
else:
    return LOW_CONTRAST
```

---

## 📌 TẦNG 2: Chiến lược xử lý

### ✅ Chiến lược A: HIGH CONTRAST
**Áp dụng:** 80% trường hợp (áo tối/màu đậm)  
**Thời gian:** 5-10ms  
**Phương pháp:** Binary Threshold + Morphology

**Các bước:**
```
1. binary = Threshold(gray, 150, 255, BINARY)
2. morph = Open(binary, kernel 3×3, 1 iter)  // Xóa nhiễu
3. morph = Close(morph, kernel 3×3, 2 iter)  // Nối nhãn
4. contours = FindContours(morph, EXTERNAL)
5. biggest = find_largest_contour(contours)
6. rect, box = MinAreaRect(biggest)
7. qr_text, qr_points = VerifyQR(rect)
8. if qr_text exists: return (rect, box, qr_text, qr_points)
9. else: return null
```

---

### ✅ Chiến lược B: MEDIUM CONTRAST
**Áp dụng:** 15% trường hợp (áo màu nhạt/xám)  
**Thời gian:** 15-25ms  
**Phương pháp:** Canny Edge Detection + Strong Morphology

**Các bước:**
```
1. edges = Canny(gray, 30, 100)  // Threshold thấp để bắt biên yếu
2. kernel = GetStructuringElement(7×7)
3. morph = Close(edges, kernel, 3 iter)  // Nối biên đứt gãy
4. morph = Dilate(morph, kernel, 1 iter)  // Làm dày
5. contours = FindContours(morph, EXTERNAL)

6. // Lọc contours - CHỈ theo diện tích
   roi_area = gray.width × gray.height
   candidates = []
   for c in contours:
       area = ContourArea(c)
       area_ratio = area / roi_area
       if 0.05 < area_ratio < 0.80:  // Chỉ lọc quá nhỏ/lớn
           candidates.add(c)
   
7. Sort(candidates, by=area, descending)  // Lớn nhất trước

8. // Lặp để verify QR (Early Exit)
   for c in candidates:
       rect, box = MinAreaRect(c)
       qr_text, qr_points = VerifyQR(rect)
       if qr_text exists:
           return (rect, box, qr_text, qr_points)  // Tìm thấy!
   
9. return null  // Không tìm thấy
```

**Lưu ý quan trọng:**
- ❌ KHÔNG lọc aspect ratio (nhãn có thể bị gấp, perspective)
- ❌ KHÔNG lọc solidity (nhãn có QR/text tạo "lỗ")
- ✅ CHỈ lọc area ratio (5-80%)
- ✅ Ưu tiên lớn nhất + verify bằng QR

---

### ✅ Chiến lược C: LOW CONTRAST (QR-First Geometry)
**Áp dụng:** 5% trường hợp (áo trắng/kem)  
**Thời gian:** 5-10ms  
**Phương pháp:** Detect QR → Suy luận hình học nhãn

**Ý tưởng cốt lõi:**
Không tìm biên (vì không có biên rõ). Thay vào đó, tìm QR code trước, sau đó dùng quy tắc nghiệp vụ (layout nhãn) để suy ra vị trí nhãn.

**Kiến trúc nhãn:**
```
┌─────────────────────────────────────┐
│         LABEL (Nhãn)                │
│  ┌──────┐          ┌────┐           │
│  │TEXT  │          │ QR │           │
│  │AREA  │          │CODE│           │
│  └──────┘          └────┘           │
│  (2/3 width)      (1/3 width)       │
└─────────────────────────────────────┘
    ↑                    ↑
  Label width = QR width × 3
```

**Các bước:**
```
1. // Detect QR trước
   qr_text, qr_points = DetectAndDecode(src)
   if qr_points is null:
       return null
   
2. // Tính geometry QR
   p0 = qr_points[0]  // top-left
   p1 = qr_points[1]  // top-right
   p3 = qr_points[3]  // bottom-left
   
   top_vec = p1 - p0      // Vector cạnh trên (→)
   left_vec = p3 - p0     // Vector cạnh trái (↓)
   
   qr_width = length(top_vec)
   qr_height = length(left_vec)

3. // Suy luận nhãn (GIẢ ĐỊNH: QR chiếm 1/3 nhãn)
   LABEL_WIDTH_RATIO = 3.0  // Có thể cấu hình
   
   label_width = qr_width × LABEL_WIDTH_RATIO
   label_height = qr_height

4. // Tính vector đơn vị
   dir_right = normalize(top_vec)
   dir_down = normalize(left_vec)
   dir_left = -dir_right

5. // Tính 4 góc nhãn (mở rộng từ QR sang trái)
   label_top_right = p0  // = QR top-left
   
   label_top_left = label_top_right 
                  + dir_left × (label_width - qr_width)
   
   label_bottom_right = label_top_right 
                      + dir_down × label_height
   
   label_bottom_left = label_top_left 
                     + dir_down × label_height

6. // Tạo RotatedRect từ 4 góc
   center = (sum of 4 corners) / 4
   angle = atan2(top_vec.y, top_vec.x) × 180/π
   
   rect = RotatedRect(center, (label_width, label_height), angle)
   box = [label_top_left, label_top_right, 
          label_bottom_right, label_bottom_left]

7. return (rect, box, qr_text, qr_points)
```

**Ưu điểm:**
- ✅ Không phụ thuộc vào biên (biên có thể không tồn tại)
- ✅ Cực nhanh (~5ms, chỉ detect QR + tính toán)
- ✅ Chính xác 100% nếu QR được phát hiện
- ✅ Xử lý được trường hợp nghiêng (dùng vector)

**Tham số điều chỉnh:**
```
LABEL_WIDTH_RATIO = 3.0  // QR chiếm 1/3 nhãn
                         // Kiểm tra thiết kế nhãn thực tế
                         // Có thể là 2.5 - 3.5
```

---

## 🚀 Tích hợp và Fallback

### Luồng chính
```
function DetectLabelRegion(image, threshold):
    // Preprocessing
    gray = convert_to_gray(image)
    gray = GaussianBlur(gray, kernel=5×5)
    
    // TẦNG 1: Phân tích
    analysis = AnalyzeFrame(gray)
    
    // TẦNG 2: Thực thi chiến lược
    result = null
    
    switch (analysis.level):
        case HIGH:
            result = DetectWithHighContrast(image, gray, threshold)
            
        case MEDIUM:
            result = DetectWithMediumContrast(image, gray)
            if result is null:  // Fallback
                result = DetectWithHighContrast(image, gray, threshold)
                
        case LOW:
            result = DetectWithLowContrast(image, gray)
            if result is null:  // Fallback chain
                result = DetectWithMediumContrast(image, gray)
            if result is null:
                result = DetectWithHighContrast(image, gray, threshold)
    
    return result
```

### Chiến lược Fallback
```
LOW → MEDIUM → HIGH
     (fail)    (fail)

Lý do: 
- LOW thất bại → không tìm thấy QR hoặc QR bị hỏng
- MEDIUM thất bại → biên quá yếu
- HIGH luôn chạy được (worst case)
```

---

## 📈 Kỳ vọng kết quả

### Độ chính xác

| Màu áo | Trước (Binary only) | Sau (3-tier) | Cải thiện |
|--------|---------------------|--------------|-----------|
| Đen/Đậm | 95% | 95% | - |
| Màu sắc | 90% | 92% | +2% |
| Màu nhạt | 60% | 85% | **+25%** ⭐ |
| Trắng/Kem | 20% | 75% | **+55%** ⭐⭐ |

### Tốc độ xử lý

| Strategy | Tỷ lệ | Thời gian | Trung bình |
|----------|-------|-----------|------------|
| HIGH | 80% | 5-10ms | ~7ms |
| MEDIUM | 15% | 15-25ms | ~20ms |
| LOW | 5% | 5-10ms | ~7ms |
| **Tổng** | 100% | - | **~10ms** ⚡ |

→ Đạt **>100 FPS**, đủ cho realtime camera

---

## 🎯 Lộ trình triển khai

### Phase 1: Hệ thống phân tích (3-4h)
- [ ] Tạo enum `ContrastLevel` (High/Medium/Low)
- [ ] Tạo struct `ContrastAnalysisResult` (chứa 3 metrics + final score)
- [ ] Implement `AnalyzeHistogram()` - tìm 2 peaks
- [ ] Implement `AnalyzeEdges()` - Canny + đếm pixels
- [ ] Implement `AnalyzeContrast()` - tính stddev
- [ ] Implement `AnalyzeFrame()` - tổng hợp 3 metrics
- [ ] Test với 10 ảnh mẫu, log metrics

### Phase 2: Strategy HIGH (0.5h)
- [ ] Refactor code cũ thành `DetectWithHighContrast()`
- [ ] Thêm logging
- [ ] Test không làm hỏng tính năng cũ

### Phase 3: Strategy MEDIUM (2-3h)
- [ ] Implement `DetectWithMediumContrast()`
- [ ] Canny + Morphology mạnh (7×7, 3 iters)
- [ ] Filter CHỈ theo area (5-80%)
- [ ] Sort + QR verification loop
- [ ] Test với 5 ảnh áo màu nhạt

### Phase 4: Strategy LOW (2-3h)
- [ ] Implement `DetectWithLowContrast()`
- [ ] Detect QR trước
- [ ] Tính geometry QR (vectors, width, height)
- [ ] Suy luận 4 góc nhãn (label_width = qr_width × 3)
- [ ] Tạo RotatedRect từ 4 góc
- [ ] Test với 5 ảnh áo trắng/kem

### Phase 5: Tích hợp (1h)
- [ ] Tích hợp 3 strategies vào `DetectLabelRegion()`
- [ ] Implement routing logic (switch-case)
- [ ] Implement fallback chain (Low→Medium→High)
- [ ] Thêm comprehensive logging

### Phase 6: Testing & Tuning (2-3h)
- [ ] Tạo test set 30 ảnh (10 mỗi loại)
- [ ] Chạy batch test, đo accuracy
- [ ] Fine-tune ngưỡng (0.6, 0.3, Canny thresholds...)
- [ ] Kiểm tra `LABEL_WIDTH_RATIO` với nhãn thực tế
- [ ] Stress test với camera realtime

**Tổng thời gian:** 11-15 giờ

---

## 🔧 Các tham số cần điều chỉnh

### Metrics (Phase 1)
```
// Final Score thresholds
HIGH_THRESHOLD = 0.6   // Có thể 0.55 - 0.65
MEDIUM_THRESHOLD = 0.3 // Có thể 0.25 - 0.35

// Edge Strength normalization
EDGE_MAX = 0.1  // Giá trị max expected (10%)
```

### Strategy HIGH (Phase 2)
```
BINARY_THRESHOLD = 150  // Có thể 140 - 160
MORPH_KERNEL_SIZE = 3   // 3×3 hoặc 5×5
```

### Strategy MEDIUM (Phase 3)
```
CANNY_LOW = 30          // 20 - 40
CANNY_HIGH = 100        // 80 - 120
MORPH_KERNEL_SIZE = 7   // 5×5 hoặc 7×7
MORPH_ITERATIONS = 3    // 2 - 4

AREA_MIN_RATIO = 0.05   // 5%
AREA_MAX_RATIO = 0.80   // 80%
```

### Strategy LOW (Phase 4)
```
LABEL_WIDTH_RATIO = 3.0  // 2.5 - 3.5 (kiểm tra nhãn thực tế!)
                         // QR chiếm 1/3 nhãn
```

---

## 🐛 Troubleshooting

### Vấn đề: Metrics không ổn định giữa các frame
**Nguyên nhân:** Nhiễu, dao động ánh sáng  
**Giải pháp:** 
- Smooth metrics bằng moving average 3-5 frames
- Tăng GaussianBlur kernel 5×5 → 7×7

### Vấn đề: Strategy MEDIUM bắt nhiều false positive
**Nguyên nhân:** Canny quá nhạy  
**Giải pháp:**
- Tăng CANNY_LOW từ 30 → 40
- CHỈ filter area, KHÔNG dùng aspect ratio/solidity

### Vấn đề: Strategy LOW sai kích thước
**Nguyên nhân:** `LABEL_WIDTH_RATIO` không đúng  
**Giải pháp:**
- Đo thực tế: qr_width / label_width
- Điều chỉnh ratio (2.5 - 3.5)

---

## 📚 Tham khảo

### OpenCV Concepts
- **Histogram:** Phân bố cường độ sáng 0-255
- **Canny Edge:** Phát hiện gradient cao
- **Standard Deviation:** Đo độ phân tán pixel
- **Morphology:** Open (xóa nhiễu), Close (nối liền)

### Papers
- Otsu's Binarization (1979)
- Canny Edge Detector (1986)

---

**Phiên bản:** 2.0 (High-Level)  
**Ngày:** November 12, 2025  
**Trạng thái:** Ready for Implementation
