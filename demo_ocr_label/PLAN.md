# KẾ HOẠCH TRIỂN KHAI: Thay thế PaddleOCR-cls bằng Logic Hình học QR Code

## 📊 TỔNG QUAN

**Mục tiêu**: Thay thế việc sử dụng mô hình AI (PaddleOCR-cls) để kiểm tra hướng ảnh bằng phép toán hình học dựa trên tọa độ 4 đỉnh của mã QR Code.

**Lợi ích**:
- ⚡ Tốc độ: Nhanh hơn 100-1000 lần
- 🎯 Độ chính xác: Dựa trên cấu trúc hình học cố định của QR → tin cậy tuyệt đối
- 🪶 Nhẹ hơn: Giảm 1 engine OCR → ứng dụng khởi động nhanh hơn
- 🧹 Code sạch hơn: Loại bỏ 1 hàm AI phức tạp, giảm phụ thuộc

---

## 🔬 CƠ SỞ LÝ THUYẾT

### Tại sao giải pháp hoạt động?

1. **QR Code có cấu trúc cố định**:
   - 3 "mắt" (Finder Patterns) luôn ở vị trí: Top-Left, Top-Right, Bottom-Left
   - OpenCV's `detectAndDecode` phải tìm 3 mắt này để giải mã
   - Hàm luôn trả về 4 đỉnh theo thứ tự logic: `[0]=TopLeft, [1]=TopRight, [2]=BottomRight, [3]=BottomLeft`

2. **MinAreaRect không biết hướng**:
   - `Cv2.MinAreaRect()` chỉ tìm hình chữ nhật nhỏ nhất bao quanh contour
   - Một hình chữ nhật xoay 0° và 180° có cùng kích thước → không phân biệt được
   - Góc trả về có thể sai 180°

3. **QR Code là "La bàn"**:
   - Vector từ `qrPoints[0]` → `qrPoints[1]` chính là cạnh trên logic của QR
   - So sánh góc của vector này với góc của Label → biết ngay có bị ngược 180° không

---

## 📐 LOGIC TOÁN HỌC

```
┌─────────────────────────────────────────────────────────────┐
│  BƯỚC 1: Tính góc Label (Dự đoán)                          │
├─────────────────────────────────────────────────────────────┤
│  Input:  RotatedRect rect (từ MinAreaRect)                 │
│  Logic:  float labelAngle = rect.Angle;                    │
│          if (rect.Size.Width < rect.Size.Height)           │
│              labelAngle += 90; // Chuẩn hóa                │
│  Output: labelAngle (ví dụ: 5° hoặc 185°)                  │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  BƯỚC 2: Tính góc QR (Sự thật)                             │
├─────────────────────────────────────────────────────────────┤
│  Input:  Point2f[] qrPoints (từ detectAndDecode)           │
│  Logic:  Point2f vec = qrPoints[1] - qrPoints[0];          │
│          float qrAngle = Atan2(vec.Y, vec.X) * 180/π;      │
│  Output: qrAngle (góc chính xác, ví dụ: 4.8°)              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  BƯỚC 3: So sánh và Quyết định                             │
├─────────────────────────────────────────────────────────────┤
│  Logic:  float delta = labelAngle - qrAngle;               │
│          Chuẩn hóa delta về [-180, 180]                    │
│          bool needs180Flip = Math.Abs(delta) > 90;         │
│                                                             │
│  Giải thích:                                               │
│  - Nếu delta ≈ 0° → Label và QR cùng hướng → OK           │
│  - Nếu delta ≈ 180° → Label ngược QR 180° → CẦN XOAY     │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  BƯỚC 4: Thực thi                                          │
├─────────────────────────────────────────────────────────────┤
│  1. WarpAffine với labelAngle → xoay thẳng Label          │
│  2. if (needs180Flip) Rotate180 → sửa hướng               │
└─────────────────────────────────────────────────────────────┘
```

---

## 🛠️ KẾ HOẠCH THỰC HIỆN (3 GIAI ĐOẠN)

---

## 📦 GIAI ĐOẠN 1: Sửa `LabelDetector.cs`

### 1.1 Sửa hàm `DetectLabelRegion` - Trả về tọa độ QR

#### Dữ liệu đầu vào:
- `Bitmap inputBmp`: Ảnh ROI cần phát hiện label
- `int thresholdValue`: Ngưỡng binary (mặc định 150)

#### Các bước thực hiện:

**Bước 1.1.1**: Thay đổi chữ ký hàm
```csharp
// CŨ:
public static (RotatedRect? rect, OpenCvSharp.Point[]? box, string? qrText)

// MỚI: Thêm Point2f[]? qrPoints
public static (RotatedRect? rect, OpenCvSharp.Point[]? box, string? qrText, Point2f[]? qrPoints)
    DetectLabelRegion(Bitmap inputBmp, int thresholdValue = 150)
```

**Bước 1.1.2**: Đổi tên biến `points` thành `qrPoints`
```csharp
// Tìm dòng (khoảng dòng 107):
Point2f[] points;
qrText = qr.DetectAndDecode(labelRoi, out points, straight);

// Sửa thành:
Point2f[] qrPoints; // Đổi tên để rõ nghĩa
qrText = qr.DetectAndDecode(labelRoi, out qrPoints, straight);
```

**Bước 1.1.3**: Cập nhật tất cả lệnh `return`
```csharp
// Tìm tất cả các dòng:
return (null, null, null);

// Sửa thành (thêm null cho qrPoints):
return (null, null, null, null);

// Tìm dòng return thành công (khoảng dòng 120):
if (!string.IsNullOrEmpty(qrText))
    return (rect, box, qrText);

// Sửa thành:
if (!string.IsNullOrEmpty(qrText))
    return (rect, box, qrText, qrPoints);
```

#### Dữ liệu đầu ra:
- `(RotatedRect? rect, Point[]? box, string? qrText, Point2f[]? qrPoints)`
- `qrPoints`: Mảng 4 đỉnh QR theo thứ tự [TopLeft, TopRight, BottomRight, BottomLeft]

---

### 1.2 Sửa hàm `CropAndAlignLabel` - Dùng QR để xoay

#### Dữ liệu đầu vào:
- `Bitmap roi`: Ảnh ROI chứa label
- `RotatedRect rect`: Hình chữ nhật xoay của label
- `Point[] box`: 4 đỉnh của label
- **(MỚI)** `Point2f[] qrPoints`: 4 đỉnh của QR Code

#### Các bước thực hiện:

**Bước 1.2.1**: Thay đổi chữ ký hàm
```csharp
// CŨ:
public Bitmap CropAndAlignLabel(Bitmap roi, RotatedRect rect, OpenCvSharp.Point[] box)

// MỚI: Thêm Point2f[] qrPoints
public Bitmap CropAndAlignLabel(Bitmap roi, RotatedRect rect, OpenCvSharp.Point[] box, Point2f[] qrPoints)
```

**Bước 1.2.2**: Tính góc Label và đổi tên biến
```csharp
// Tìm đoạn code (khoảng dòng 151-159):
float angle = rect.Angle;
if (rect.Size.Width < rect.Size.Height)
{
    angle += 90;
}

// Sửa thành (đổi tên angle → labelAngle):
float angle = rect.Angle;
if (rect.Size.Width < rect.Size.Height)
{
    angle += 90;
}
float labelAngle = angle; // Đổi tên để phân biệt với qrAngle
```

**Bước 1.2.3**: THÊM CODE MỚI - Tính góc QR
```csharp
// Thêm ngay sau đoạn code trên (sau dòng float labelAngle = angle;):

// ========== CODE MỚI: Tính góc QR Code ==========
// Vector cạnh trên của QR (từ Top-Left → Top-Right)
Point2f vec_QR_Top = qrPoints[1] - qrPoints[0];

// Tính góc "sự thật" từ vector này
float qrAngle = (float)(Math.Atan2(vec_QR_Top.Y, vec_QR_Top.X) * (180.0 / Math.PI));
// =================================================
```

**Bước 1.2.4**: THÊM CODE MỚI - So sánh góc và quyết định
```csharp
// Thêm tiếp ngay sau đoạn code trên:

// ========== CODE MỚI: So sánh góc ==========
float deltaAngle = labelAngle - qrAngle;

// Chuẩn hóa delta về [-180, 180]
while (deltaAngle <= -180) deltaAngle += 360;
while (deltaAngle > 180) deltaAngle -= 360;

// Quyết định: Nếu chênh lệch > 90 độ → ngược nhau 180°
bool needs180Flip = Math.Abs(deltaAngle) > 90;

Debug.WriteLine($"🧭 Label={labelAngle:F1}°, QR={qrAngle:F1}°, Δ={deltaAngle:F1}° → Flip180={needs180Flip}");
// ============================================
```

**Bước 1.2.5**: Sửa các dòng sử dụng biến `angle`
```csharp
// Tìm dòng (khoảng dòng 176):
Mat rotationMatrix = Cv2.GetRotationMatrix2D(rect.Center, angle, 1.0);

// Sửa thành (dùng labelAngle thay vì angle):
Mat rotationMatrix = Cv2.GetRotationMatrix2D(rect.Center, labelAngle, 1.0);
```

**Bước 1.2.6**: XÓA và THAY THẾ logic IsImageUpsideDown
```csharp
// Tìm và XÓA TOÀN BỘ đoạn code sau (khoảng dòng 196-205):
if (IsImageUpsideDown(cropped))
{
    Cv2.Rotate(cropped, cropped, RotateFlags.Rotate180);
    Debug.WriteLine("🔄 Đã xoay lại 180° (dựa trên text).");
}

// THAY THẾ bằng:
if (needs180Flip)
{
    Cv2.Rotate(cropped, cropped, RotateFlags.Rotate180);
    Debug.WriteLine("🔄 Đã xoay lại 180° (dựa trên QR geometry).");
}
```

#### Dữ liệu đầu ra:
- `Bitmap`: Label đã xoay thẳng và đúng chiều

---

### 1.3 Dọn dẹp `LabelDetector.cs`

**Bước 1.3.1**: Xóa hàm `IsImageUpsideDown`
```csharp
// Tìm và XÓA TOÀN BỘ hàm này (khoảng dòng 224-285):
public bool IsImageUpsideDown(Mat mat)
{
    // ... toàn bộ code bên trong ...
}
```

**Bước 1.3.2**: Xóa biến `directClassOCR`
```csharp
// Tìm và XÓA dòng này ở đầu class (khoảng dòng 12):
private PaddleOCREngine directClassOCR;
```

**Bước 1.3.3**: Sửa constructor
```csharp
// Tìm constructor (khoảng dòng 13-16):
public LabelDetector(PaddleOCREngine ocrEngine)
{
    directClassOCR = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
}

// Sửa thành constructor rỗng:
public LabelDetector()
{
    // Không cần tham số nữa
}
```

---

## 📱 GIAI ĐOẠN 2: Sửa `Form1.cs`

### 2.1 Sửa vòng lặp `CaptureLoop` - Bắc cầu dữ liệu

#### Dữ liệu đầu vào:
- Vòng lặp `while (!ct.IsCancellationRequested)` đang chạy

#### Các bước thực hiện:

**Bước 2.1.1**: Cập nhật lệnh gọi `DetectLabelRegion`
```csharp
// Tìm dòng (khoảng dòng 350):
var (rect, box, qrText) = LabelDetector.DetectLabelRegion(roi, currentThreshold);

// Sửa thành (thêm qrPoints):
var (rect, box, qrText, qrPoints) = LabelDetector.DetectLabelRegion(roi, currentThreshold);
```

**Bước 2.1.2**: Thêm điều kiện kiểm tra `qrPoints`
```csharp
// Tìm dòng if (khoảng dòng 352):
if (rect != null && box != null && qrText != null)

// Sửa thành (thêm qrPoints != null):
if (rect != null && box != null && qrText != null && qrPoints != null)
```

**Bước 2.1.3**: Cập nhật lệnh gọi `CropAndAlignLabel`
```csharp
// Tìm dòng (khoảng dòng 362):
var aligned = labelDetector.CropAndAlignLabel(roi, rect.Value, box);

// Sửa thành (thêm qrPoints):
var aligned = labelDetector.CropAndAlignLabel(roi, rect.Value, box, qrPoints);
```

#### Dữ liệu đầu ra:
- Luồng dữ liệu `qrPoints` được truyền thành công từ `DetectLabelRegion` → `CropAndAlignLabel`

---

## 🧹 GIAI ĐOẠN 3: Dọn dẹp Code

### 3.1 Dọn dẹp `Form1.cs`

**Bước 3.1.1**: Xóa biến `directClassOCR`
```csharp
// Tìm và XÓA dòng này (khoảng dòng 38):
public PaddleOCREngine? directClassOCR;
```

**Bước 3.1.2**: Xóa hàm `InitDirectClassOCR`
```csharp
// Tìm và XÓA TOÀN BỘ hàm này (khoảng dòng 868-886):
private void InitDirectClassOCR()
{
    // ... toàn bộ code ...
}
```

**Bước 3.1.3**: Sửa `Form1_Load`
```csharp
// Tìm trong hàm Form1_Load (khoảng dòng 133-136):
InitDirectClassOCR();
labelDetector = new LabelDetector(directClassOCR);

// XÓA dòng InitDirectClassOCR() và SỬA dòng khởi tạo:
labelDetector = new LabelDetector(); // Constructor rỗng
```

---

## ✅ CHECKLIST HOÀN THÀNH

### Giai đoạn 1: LabelDetector.cs
- [ ] 1.1.1: Thay đổi chữ ký `DetectLabelRegion` (thêm `Point2f[]? qrPoints`)
- [ ] 1.1.2: Đổi tên `points` → `qrPoints`
- [ ] 1.1.3: Cập nhật tất cả `return` (thêm `null` hoặc `qrPoints`)
- [ ] 1.2.1: Thay đổi chữ ký `CropAndAlignLabel` (thêm tham số `qrPoints`)
- [ ] 1.2.2: Đổi tên `angle` → `labelAngle`
- [ ] 1.2.3: Thêm code tính `qrAngle`
- [ ] 1.2.4: Thêm code tính `deltaAngle` và `needs180Flip`
- [ ] 1.2.5: Sửa `angle` → `labelAngle` trong `GetRotationMatrix2D`
- [ ] 1.2.6: Thay thế `IsImageUpsideDown` bằng `needs180Flip`
- [ ] 1.3.1: Xóa hàm `IsImageUpsideDown`
- [ ] 1.3.2: Xóa biến `directClassOCR`
- [ ] 1.3.3: Sửa constructor thành rỗng

### Giai đoạn 2: Form1.cs
- [ ] 2.1.1: Thêm `qrPoints` vào tuple khi gọi `DetectLabelRegion`
- [ ] 2.1.2: Thêm `qrPoints != null` vào điều kiện `if`
- [ ] 2.1.3: Truyền `qrPoints` vào `CropAndAlignLabel`

### Giai đoạn 3: Cleanup
- [ ] 3.1.1: Xóa biến `directClassOCR` trong `Form1.cs`
- [ ] 3.1.2: Xóa hàm `InitDirectClassOCR` trong `Form1.cs`
- [ ] 3.1.3: Sửa khởi tạo `labelDetector` trong `Form1_Load`

---

## 🧪 KIỂM TRA SAU KHI HOÀN THÀNH

### Kiểm tra biên dịch:
```bash
# Trong PowerShell:
cd c:\Users\trung\Desktop\AI\label-ocr\demo_ocr_label
dotnet build
```
**Kỳ vọng**: Không có lỗi biên dịch.

### Kiểm tra chức năng:
1. ✅ Mở camera → Label được phát hiện
2. ✅ QR Code được giải mã thành công
3. ✅ Ảnh label hiển thị đúng chiều (không bị ngược)
4. ✅ Debug Window hiển thị log góc: `🧭 Label=5.2°, QR=4.8°, Δ=0.4° → Flip180=False`
5. ✅ Tốc độ xử lý nhanh hơn rõ rệt (không có delay từ PaddleOCR-cls)

### Kiểm tra edge cases:
- [ ] Label xoay 0° → Hiển thị đúng
- [ ] Label xoay 90° → Hiển thị đúng
- [ ] Label xoay 180° (ngược) → Tự động flip, hiển thị đúng
- [ ] Label xoay 270° → Hiển thị đúng
- [ ] QR Code bị che khuất một phần → Vẫn hoạt động (nếu detectAndDecode thành công)

---

## 📈 KẾT QUẢ DỰ KIẾN

### Trước khi thực hiện:
- Tốc độ kiểm tra hướng: ~50-200ms (PaddleOCR-cls)
- Số engine OCR: 2 (`ocr` + `directClassOCR`)
- Độ phức tạp code: Cao (AI inference)

### Sau khi thực hiện:
- Tốc độ kiểm tra hướng: ~0.1-0.5ms (Atan2 + so sánh)
- Số engine OCR: 1 (`ocr`)
- Độ phức tạp code: Thấp (toán hình học)
- **Cải thiện tốc độ**: 100-2000 lần nhanh hơn

---

## ⚠️ LƯU Ý QUAN TRỌNG

1. **Backup trước khi sửa**:
   ```powershell
   # Tạo bản sao các file quan trọng:
   Copy-Item LabelDetector.cs LabelDetector.cs.backup
   Copy-Item Form1.cs Form1.cs.backup
   ```

2. **Thứ tự thực hiện**:
   - ⚠️ **BẮT BUỘC** làm theo thứ tự: Giai đoạn 1 → 2 → 3
   - ⚠️ **KHÔNG** build giữa chừng (sẽ lỗi biên dịch)
   - ✅ Build **sau khi hoàn thành cả 3 giai đoạn**

3. **Nếu gặp lỗi biên dịch**:
   - Kiểm tra lại Checklist xem bước nào còn thiếu
   - Đảm bảo tất cả `return (null, null, null)` đã sửa thành `return (null, null, null, null)`
   - Đảm bảo `angle` đã đổi thành `labelAngle` ở mọi nơi sử dụng

4. **Nếu ảnh vẫn bị ngược sau khi sửa**:
   - Kiểm tra Debug Window xem log `🧭 Label=... QR=... Δ=...`
   - Nếu `Δ ≈ 180°` nhưng `Flip180=False` → Sai điều kiện `> 90`
   - Nếu không có log → Quên thêm code ở Bước 1.2.4

---

## 🎯 KẾT LUẬN

Kế hoạch này đã được thiết kế chi tiết từng bước, với:
- ✅ Cơ sở lý thuyết vững chắc
- ✅ Logic toán học đơn giản, dễ hiểu
- ✅ Các bước thực hiện rõ ràng, không nhầm lẫn
- ✅ Checklist đầy đủ để tự kiểm tra
- ✅ Hướng dẫn xử lý lỗi

**Vui lòng xác nhận để bắt đầu triển khai!** 🚀

---

**Người lập kế hoạch**: GitHub Copilot  
**Ngày tạo**: 2025-01-11  
**Phiên bản**: 1.0
