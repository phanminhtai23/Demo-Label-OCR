# 📋 Kế hoạch nâng cấp hệ thống nhận diện nhãn (Label Detection)

## 🎯 Mục tiêu

Nâng cấp thuật toán nhận diện nhãn để xử lý **TẤT CẢ màu áo** (đỏ, xanh, trắng, xám, be, hồng...) một cách tự động và nhanh chóng trong môi trường nhà máy với ánh sáng ổn định.

---

## 📊 Phân tích bài toán

### Các trường hợp thực tế

| Nhóm màu áo | Tương phản với nhãn trắng | Tỷ lệ xuất hiện | Độ khó | Phương pháp phù hợp |
|-------------|---------------------------|-----------------|---------|---------------------|
| **Đen, Navy, Nâu đậm** | Cực cao (>150 intensity) | ~20% | ⭐ Rất dễ | Binary Threshold |
| **Đỏ, Xanh, Vàng, Cam** | Cao (100-150 intensity) | ~40% | ⭐⭐ Dễ | Binary Threshold |
| **Hồng nhạt, Be, Xám nhạt** | Trung bình (50-100) | ~25% | ⭐⭐⭐ Trung bình | Edge Detection |
| **Trắng, Kem, Xám rất nhạt** | Thấp (<50) | ~15% | ⭐⭐⭐⭐⭐ Cực khó | QR-First |

### Thách thức hiện tại

- ❌ Binary Threshold cố định không hoạt động tốt với áo màu sáng (trắng, kem, xám)
- ❌ Không thể định nghĩa threshold cho từng màu (quá nhiều biến thể)
- ✅ Cần giải pháp tự động phát hiện và chọn strategy phù hợp
- ✅ Phải đảm bảo tốc độ xử lý realtime (< 100ms/frame)

---

## 🔍 Giải pháp: Hệ thống phát hiện tự động 3 tầng

### Kiến trúc tổng thể

```
Input Frame (Camera)
    ↓
┌─────────────────────────────────────────────────┐
│  PREPROCESSING (1-2ms)                          │
│  • Resize về 640px nếu quá lớn (tăng tốc)       │
│  • GaussianBlur (giảm nhiễu)                    │
│  • Chỉ xử lý vùng Guide Box (tối ưu)            │
└─────────────────────────────────────────────────┘
    ↓
┌─────────────────────────────────────────────────┐
│  AUTO DETECTION: Phân tích độ tương phản (3-5ms)│
│  • Tính 3 metrics: Separation, Edge, Contrast   │
│  • Tính Final Score = weighted average          │
│  • Chọn Strategy tự động (High/Medium/Low)      │
└─────────────────────────────────────────────────┘
    ↓
    ├─ HIGH CONTRAST (Score > 0.6) ────────────────┐
    │   Strategy: FAST PATH (5-10ms)               │
    │   ✅ Áp dụng: 80% trường hợp                 │
    │   • Binary Threshold (code hiện tại)         │
    │   • Morphology đơn giản (3x3 kernel)         │
    │   • FindContours → Chọn contour lớn nhất     │
    └──────────────────────────────────────────────┘
    │
    ├─ MEDIUM CONTRAST (Score 0.3-0.6) ────────────┐
    │   Strategy: EDGE-BASED (15-25ms)             │
    │   ✅ Áp dụng: 15% trường hợp                 │
    │   • Canny Edge Detection (threshold thấp)    │
    │   • Morphology mạnh (7x7 kernel, 3 iters)    │
    │   • Contour filtering (aspect ratio, area)   │
    │   • QR Verification                          │
    └──────────────────────────────────────────────┘
    │
    └─ LOW CONTRAST (Score < 0.3) ─────────────────┐
        Strategy: QR-FIRST (50-100ms)              │
        ✅ Áp dụng: 5% trường hợp                  │
        • Detect QR Code trước trong toàn ảnh      │
        • Mở rộng vùng QR (2x mỗi chiều)           │
        • Adaptive Threshold cục bộ (blockSize=15) │
        • FindContours chứa QR                     │
        └──────────────────────────────────────────┘
    ↓
✅ Label Detected → QR Verification → Crop & Align → OCR
```

---

## 📐 Chi tiết 3 Metrics để phát hiện độ tương phản

### 1️⃣ **Separation (Histogram) - Độ tách histogram**

#### Khái niệm
Histogram là biểu đồ phân bố cường độ sáng (0-255) của các pixel trong ảnh. Khi có nhãn trắng trên nền tối/màu, histogram sẽ có **2 đỉnh rõ ràng**:
- **Đỉnh 1**: Vùng tối (nền áo) → Tập trung ở intensity thấp (0-100)
- **Đỉnh 2**: Vùng sáng (nhãn trắng) → Tập trung ở intensity cao (180-255)

#### Công thức tính
```
Separation = |Peak1_Position - Peak2_Position| / 255

Ví dụ:
- Áo đen (peak1=30) vs Nhãn trắng (peak2=240)
  → Separation = |30 - 240| / 255 = 0.824 ✅ CAO
  
- Áo xám nhạt (peak1=180) vs Nhãn trắng (peak2=230)
  → Separation = |180 - 230| / 255 = 0.196 ❌ THẤP
```

#### Triển khai
```csharp
// Bước 1: Lấy vùng mẫu (center của frame - nơi có nhãn)
Rect centerRegion = new Rect(
    width/2 - sampleSize/2,
    height/2 - sampleSize/2,
    sampleSize, sampleSize
);
Mat centerRoi = new Mat(gray, centerRegion);

// Bước 2: Tính histogram (256 bins, range 0-255)
Mat hist = new Mat();
Cv2.CalcHist(
    new[] { centerRoi },
    new[] { 0 },           // Kênh 0 (grayscale)
    null,                  // Không mask
    hist,
    1,                     // 1 chiều
    new[] { 256 },         // 256 bins
    new[] { new Rangef(0, 256) }
);

// Bước 3: Tìm 2 đỉnh lớn nhất
float[] histData = new float[256];
hist.GetArray(out histData);

// Smooth histogram để tránh nhiễu (moving average 5 bins)
for (int i = 2; i < 254; i++) {
    histData[i] = (histData[i-2] + histData[i-1] + histData[i] + 
                   histData[i+1] + histData[i+2]) / 5.0f;
}

// Tìm 2 local maxima cách nhau tối thiểu 50 bins
List<(int pos, float value)> peaks = new List<(int, float)>();
for (int i = 10; i < 246; i++) {
    if (histData[i] > histData[i-1] && 
        histData[i] > histData[i+1] &&
        histData[i] > threshold_min) {
        peaks.Add((i, histData[i]));
    }
}
peaks = peaks.OrderByDescending(p => p.value).Take(2).ToList();

// Bước 4: Tính separation
double separation = Math.Abs(peaks[0].pos - peaks[1].pos) / 255.0;
```

#### Ngưỡng đánh giá
- **> 0.6** (>150/255): HIGH - Áo tối/màu đậm vs nhãn trắng
- **0.3 - 0.6** (80-150/255): MEDIUM - Áo màu nhạt vs nhãn trắng
- **< 0.3** (<80/255): LOW - Áo trắng/xám nhạt vs nhãn trắng

---

### 2️⃣ **Edge Strength (Canny) - Độ mạnh viền**

#### Khái niệm
Canny Edge Detection tìm các pixel có gradient (độ biến thiên cường độ sáng) cao. Khi nhãn và nền có tương phận tốt, viền biên sẽ rõ ràng → nhiều edge pixels.

Edge Strength đo **tỷ lệ % pixel là biên** trong vùng mẫu.

#### Công thức tính
```
Edge Strength = (Số pixel edge) / (Tổng số pixel vùng mẫu)

Ví dụ:
- Vùng mẫu 200x200 = 40,000 pixels
- Canny phát hiện 2,500 edge pixels
  → Edge Strength = 2,500 / 40,000 = 0.0625 ✅ CAO
  
- Canny chỉ phát hiện 400 edge pixels
  → Edge Strength = 400 / 40,000 = 0.01 ❌ THẤP
```

#### Triển khai
```csharp
// Bước 1: Áp dụng Canny trên vùng mẫu
Mat edges = new Mat();
Cv2.Canny(
    centerRoi,
    edges,
    threshold1: 50,   // Threshold thấp (phát hiện edge yếu)
    threshold2: 150,  // Threshold cao
    apertureSize: 3   // Sobel kernel size
);

// Bước 2: Đếm số pixel trắng (edge)
int edgePixels = Cv2.CountNonZero(edges);
int totalPixels = centerRoi.Width * centerRoi.Height;

// Bước 3: Tính tỷ lệ
double edgeStrength = (double)edgePixels / totalPixels;
```

#### Giải thích tham số Canny
- **threshold1 (50)**: Gradient < 50 → loại bỏ (không phải edge)
- **threshold2 (150)**: Gradient > 150 → chắc chắn là edge
- **50-150**: Vùng mơ hồ, chỉ giữ nếu liền với edge mạnh (hysteresis)

#### Ngưỡng đánh giá
- **> 0.05** (5%): HIGH - Biên rõ ràng, dễ phát hiện
- **0.02 - 0.05** (2-5%): MEDIUM - Biên yếu, cần morphology
- **< 0.02** (<2%): LOW - Biên mờ, cần adaptive threshold

---

### 3️⃣ **Contrast Ratio (Standard Deviation) - Độ tương phản**

#### Khái niệm
Standard Deviation (độ lệch chuẩn) đo **độ phân tán** của cường độ sáng. Khi có cả vùng tối (nền) và vùng sáng (nhãn), stddev sẽ cao.

- **StdDev cao**: Pixel có cường độ sáng rất khác nhau → tương phản cao
- **StdDev thấp**: Pixel có cường độ sáng giống nhau → tương phản thấp

#### Công thức toán học
```
Mean (μ) = Σ(pixel_value) / N

StdDev (σ) = sqrt(Σ(pixel_value - μ)² / N)

Contrast Ratio = σ / 128  (normalize về 0-1)

Ví dụ:
- Ảnh có pixels [10, 15, 12, 240, 250, 245]
  Mean = 128.67
  StdDev = 116.2 → Contrast = 116.2/128 = 0.908 ✅ CAO
  
- Ảnh có pixels [200, 210, 205, 215, 220, 218]
  Mean = 211.33
  StdDev = 7.5 → Contrast = 7.5/128 = 0.059 ❌ THẤP
```

#### Triển khai
```csharp
// Bước 1: Tính mean và stddev của vùng mẫu
Scalar mean, stddev;
Cv2.MeanStdDev(centerRoi, out mean, out stddev);

// Bước 2: Normalize stddev (0-128 → 0-1)
double contrastRatio = stddev.Val0 / 128.0;

// Note: Val0 vì grayscale chỉ có 1 kênh
```

#### Ý nghĩa vật lý
- **mean**: Độ sáng trung bình (0-255)
- **stddev**: Độ "dao động" của pixel quanh mean
  - StdDev = 0: Tất cả pixel giống nhau (ảnh đơn sắc)
  - StdDev = 128: Pixel rải đều từ 0-255 (tương phản cực cao)

#### Ngưỡng đánh giá
- **> 0.625** (>80/128): HIGH - Có cả vùng tối và sáng rõ ràng
- **0.3 - 0.625** (40-80/128): MEDIUM - Tương phản vừa phải
- **< 0.3** (<40/128): LOW - Pixel đều nhau, khó phân biệt

---

### 🎯 **Công thức Final Score (Tổng hợp)**

```csharp
// Weighted Average của 3 metrics
double finalScore = 
    (separation / 1.0) * 0.4 +      // 40% trọng số (quan trọng nhất)
    (edgeStrength / 0.1) * 0.3 +    // 30% trọng số (normalize: 0.1 = max)
    (contrastRatio / 1.0) * 0.3;    // 30% trọng số

// Quyết định strategy
if (finalScore > 0.6) return ContrastLevel.High;
if (finalScore > 0.3) return ContrastLevel.Medium;
return ContrastLevel.Low;
```

#### Giải thích trọng số
- **Separation (40%)**: Quan trọng nhất vì phản ánh trực tiếp độ khác biệt giữa 2 vùng
- **Edge Strength (30%)**: Quan trọng thứ 2, đánh giá khả năng tìm biên
- **Contrast Ratio (30%)**: Hỗ trợ, phát hiện trường hợp histogram không rõ ràng

#### Ví dụ tính toán

**Case 1: Áo đen vs Nhãn trắng**
```
Separation = 0.82 → 0.82 * 0.4 = 0.328
Edge Strength = 0.08 → (0.08/0.1) * 0.3 = 0.240
Contrast Ratio = 0.90 → 0.90 * 0.3 = 0.270
Final Score = 0.838 → HIGH ✅
```

**Case 2: Áo xám nhạt vs Nhãn trắng**
```
Separation = 0.25 → 0.25 * 0.4 = 0.100
Edge Strength = 0.03 → (0.03/0.1) * 0.3 = 0.090
Contrast Ratio = 0.45 → 0.45 * 0.3 = 0.135
Final Score = 0.325 → MEDIUM ⚠️
```

**Case 3: Áo trắng vs Nhãn trắng**
```
Separation = 0.15 → 0.15 * 0.4 = 0.060
Edge Strength = 0.015 → (0.015/0.1) * 0.3 = 0.045
Contrast Ratio = 0.20 → 0.20 * 0.3 = 0.060
Final Score = 0.165 → LOW ❌
```

---

## 🚀 Các giai đoạn triển khai

### **Phase 1: Xây dựng hệ thống phân tích tự động (3-4 giờ)**

#### 1.1. Tạo enum và struct (30 phút)
```csharp
// File: LabelDetector.cs

public enum ContrastLevel 
{
    High,      // Score > 0.6 → Binary Threshold
    Medium,    // Score 0.3-0.6 → Edge Detection
    Low        // Score < 0.3 → QR-First
}

public struct ContrastAnalysisResult 
{
    public ContrastLevel Level;
    public double FinalScore;
    
    // Chi tiết 3 metrics
    public double Separation;
    public double EdgeStrength;
    public double ContrastRatio;
    
    // Debug info
    public int Peak1Position;
    public int Peak2Position;
    public int EdgePixelCount;
    public double MeanIntensity;
    public double StdDevIntensity;
}
```

#### 1.2. Implement hàm phân tích histogram (1 giờ)
```csharp
private static (int peak1, int peak2, double separation) AnalyzeHistogram(Mat gray)
{
    // 1. Lấy vùng center (giả sử nhãn ở giữa)
    int sampleSize = Math.Min(gray.Width, gray.Height) / 3;
    Rect centerRegion = new Rect(
        gray.Width/2 - sampleSize/2,
        gray.Height/2 - sampleSize/2,
        sampleSize, sampleSize
    );
    Mat centerRoi = new Mat(gray, centerRegion);
    
    // 2. Tính histogram
    Mat hist = new Mat();
    Cv2.CalcHist(
        new[] { centerRoi },
        new[] { 0 },
        null,
        hist,
        1,
        new[] { 256 },
        new[] { new Rangef(0, 256) }
    );
    
    // 3. Smooth histogram (moving average)
    float[] histData = new float[256];
    hist.GetArray(out histData);
    
    float[] smoothed = new float[256];
    for (int i = 2; i < 254; i++) {
        smoothed[i] = (histData[i-2] + histData[i-1] + histData[i] + 
                       histData[i+1] + histData[i+2]) / 5.0f;
    }
    
    // 4. Tìm 2 local maxima
    List<(int pos, float val)> peaks = new List<(int, float)>();
    float avgHeight = smoothed.Average();
    float threshold = avgHeight * 0.5f; // Chỉ xét peak > 50% mean
    
    for (int i = 10; i < 246; i++) {
        bool isLocalMax = smoothed[i] > smoothed[i-1] && 
                          smoothed[i] > smoothed[i+1] &&
                          smoothed[i] > threshold;
        
        // Kiểm tra không bị nhiễu (peak phải "độc lập")
        if (isLocalMax) {
            bool tooCloseToExisting = peaks.Any(p => Math.Abs(p.pos - i) < 30);
            if (!tooCloseToExisting) {
                peaks.Add((i, smoothed[i]));
            }
        }
    }
    
    // 5. Lấy 2 peak cao nhất
    peaks = peaks.OrderByDescending(p => p.val).Take(2).ToList();
    
    if (peaks.Count < 2) {
        // Không tìm thấy 2 peak → tương phản thấp
        return (0, 255, 0.0);
    }
    
    // Sắp xếp theo position (peak1 = thấp, peak2 = cao)
    peaks = peaks.OrderBy(p => p.pos).ToList();
    
    int peak1 = peaks[0].pos;
    int peak2 = peaks[1].pos;
    double separation = Math.Abs(peak2 - peak1) / 255.0;
    
    return (peak1, peak2, separation);
}
```

#### 1.3. Implement hàm tính Edge Strength (30 phút)
```csharp
private static (int edgePixels, double edgeStrength) AnalyzeEdges(Mat gray)
{
    // Lấy vùng center
    int sampleSize = Math.Min(gray.Width, gray.Height) / 3;
    Rect centerRegion = new Rect(
        gray.Width/2 - sampleSize/2,
        gray.Height/2 - sampleSize/2,
        sampleSize, sampleSize
    );
    Mat centerRoi = new Mat(gray, centerRegion);
    
    // Canny Edge Detection
    Mat edges = new Mat();
    Cv2.Canny(centerRoi, edges, threshold1: 50, threshold2: 150);
    
    // Đếm edge pixels
    int edgePixels = Cv2.CountNonZero(edges);
    int totalPixels = centerRoi.Width * centerRoi.Height;
    
    double edgeStrength = (double)edgePixels / totalPixels;
    
    edges.Dispose();
    centerRoi.Dispose();
    
    return (edgePixels, edgeStrength);
}
```

#### 1.4. Implement hàm tính Contrast Ratio (20 phút)
```csharp
private static (double mean, double stddev, double ratio) AnalyzeContrast(Mat gray)
{
    // Lấy vùng center
    int sampleSize = Math.Min(gray.Width, gray.Height) / 3;
    Rect centerRegion = new Rect(
        gray.Width/2 - sampleSize/2,
        gray.Height/2 - sampleSize/2,
        sampleSize, sampleSize
    );
    Mat centerRoi = new Mat(gray, centerRegion);
    
    // Tính mean và stddev
    Scalar meanScalar, stddevScalar;
    Cv2.MeanStdDev(centerRoi, out meanScalar, out stddevScalar);
    
    double mean = meanScalar.Val0;
    double stddev = stddevScalar.Val0;
    double ratio = stddev / 128.0; // Normalize
    
    centerRoi.Dispose();
    
    return (mean, stddev, ratio);
}
```

#### 1.5. Tổng hợp hàm AnalyzeFrame (1 giờ)
```csharp
private static ContrastAnalysisResult AnalyzeFrame(Mat gray)
{
    // 1. Phân tích histogram
    var (peak1, peak2, separation) = AnalyzeHistogram(gray);
    
    // 2. Phân tích edges
    var (edgePixels, edgeStrength) = AnalyzeEdges(gray);
    
    // 3. Phân tích contrast
    var (mean, stddev, contrastRatio) = AnalyzeContrast(gray);
    
    // 4. Tính Final Score (weighted average)
    double finalScore = 
        separation * 0.4 +
        (edgeStrength / 0.1) * 0.3 +  // Normalize: 0.1 = max expected
        contrastRatio * 0.3;
    
    // 5. Xác định level
    ContrastLevel level;
    if (finalScore > 0.6) level = ContrastLevel.High;
    else if (finalScore > 0.3) level = ContrastLevel.Medium;
    else level = ContrastLevel.Low;
    
    // 6. Trả về kết quả đầy đủ
    return new ContrastAnalysisResult {
        Level = level,
        FinalScore = finalScore,
        Separation = separation,
        EdgeStrength = edgeStrength,
        ContrastRatio = contrastRatio,
        Peak1Position = peak1,
        Peak2Position = peak2,
        EdgePixelCount = edgePixels,
        MeanIntensity = mean,
        StdDevIntensity = stddev
    };
}
```

---

### **Phase 2: Implement Strategy HIGH (giữ nguyên code cũ) (30 phút)**

```csharp
private static (RotatedRect?, Point[]?, string?, Point2f[]?) 
    DetectWithHighContrast(Mat src, Mat gray, int thresholdValue)
{
    Debug.WriteLine("🟢 Strategy: HIGH CONTRAST - Binary Threshold");
    
    // GIỮ NGUYÊN CODE HIỆN TẠI (dòng 54-88 trong LabelDetector.cs)
    using var binary = new Mat();
    Cv2.Threshold(gray, binary, thresholdValue, 255, ThresholdTypes.Binary);
    
    using var morph = new Mat();
    Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
    Cv2.MorphologyEx(binary, morph, MorphTypes.Open, kernel, iterations: 1);
    Cv2.MorphologyEx(morph, morph, MorphTypes.Close, kernel, iterations: 2);
    
    Cv2.FindContours(morph, out Point[][] contours, out _, 
        RetrievalModes.External, ContourApproximationModes.ApproxSimple);
    
    if (contours == null || contours.Length == 0)
        return (null, null, null, null);
    
    // Chọn contour lớn nhất
    Point[] biggest = null;
    double maxArea = 0;
    foreach (var c in contours) {
        double area = Cv2.ContourArea(c);
        if (area > maxArea) {
            maxArea = area;
            biggest = c;
        }
    }
    
    if (biggest == null)
        return (null, null, null, null);
    
    // GIỮ NGUYÊN PHẦN QR DETECTION (dòng 89-116)
    var rect = Cv2.MinAreaRect(biggest);
    var box = rect.Points().Select(p => 
        new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
    
    var bound = Cv2.BoundingRect(biggest);
    bound.X = Math.Max(0, bound.X);
    bound.Y = Math.Max(0, bound.Y);
    bound.Width = Math.Min(src.Width - bound.X, bound.Width);
    bound.Height = Math.Min(src.Height - bound.Y, bound.Height);
    
    using var labelRoi = new Mat(src, bound);
    
    string qrText = "";
    Point2f[] qrPoints = null;
    try {
        using var qr = new QRCodeDetector();
        qrText = qr.DetectAndDecode(labelRoi, out qrPoints);
    }
    catch (Exception ex) {
        Debug.WriteLine($"[QR ERROR] {ex.Message}");
    }
    
    if (!string.IsNullOrEmpty(qrText))
        return (rect, box, qrText, qrPoints);
    
    return (null, null, null, null);
}
```

---

### **Phase 3: Implement Strategy MEDIUM (Edge-based) (2-3 giờ)**

```csharp
private static (RotatedRect?, Point[]?, string?, Point2f[]?) 
    DetectWithMediumContrast(Mat src, Mat gray)
{
    Debug.WriteLine("🟡 Strategy: MEDIUM CONTRAST - Edge Detection");
    
    // 1. Canny Edge Detection (threshold thấp hơn để bắt edge yếu)
    Mat edges = new Mat();
    Cv2.Canny(gray, edges, threshold1: 30, threshold2: 100);
    
    // 2. Morphology mạnh hơn để nối các edge rời rạc
    Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
    
    // Close: Nối các gap nhỏ
    Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel, iterations: 3);
    
    // Dilate: Làm dày edge
    Cv2.MorphologyEx(edges, edges, MorphTypes.Dilate, kernel, iterations: 1);
    
    // 3. Find contours
    Cv2.FindContours(edges, out Point[][] contours, out _,
        RetrievalModes.External, ContourApproximationModes.ApproxSimple);
    
    if (contours == null || contours.Length == 0) {
        edges.Dispose();
        return (null, null, null, null);
    }
    
    // 4. Lọc contours theo shape và size
    List<(Point[] contour, RotatedRect rect, double score)> candidates = 
        new List<(Point[], RotatedRect, double)>();
    
    double frameArea = src.Width * src.Height;
    
    foreach (var c in contours) {
        double area = Cv2.ContourArea(c);
        
        // Loại bỏ contour quá nhỏ hoặc quá lớn
        double areaRatio = area / frameArea;
        if (areaRatio < 0.05 || areaRatio > 0.5) continue;
        
        var rect = Cv2.MinAreaRect(c);
        
        // Kiểm tra aspect ratio (nhãn thường 1.5:1 đến 3:1)
        double aspectRatio = Math.Max(rect.Size.Width, rect.Size.Height) /
                             Math.Min(rect.Size.Width, rect.Size.Height);
        
        if (aspectRatio < 1.2 || aspectRatio > 3.5) continue;
        
        // Kiểm tra độ "chữ nhật" (solidity)
        double rectArea = rect.Size.Width * rect.Size.Height;
        double solidity = area / rectArea;
        
        if (solidity < 0.7) continue; // Nhãn phải gần hình chữ nhật
        
        // Tính điểm cho candidate
        double score = areaRatio * 0.4 +           // Diện tích vừa phải
                       (1.0 / aspectRatio) * 0.3 + // Gần vuông (aspect=2) tốt hơn
                       solidity * 0.3;             // Hình chữ nhật
        
        candidates.Add((c, rect, score));
    }
    
    // 5. Sắp xếp theo score và verify bằng QR
    candidates = candidates.OrderByDescending(x => x.score).ToList();
    
    foreach (var (contour, rect, score) in candidates) {
        var bound = Cv2.BoundingRect(contour);
        bound.X = Math.Max(0, bound.X);
        bound.Y = Math.Max(0, bound.Y);
        bound.Width = Math.Min(src.Width - bound.X, bound.Width);
        bound.Height = Math.Min(src.Height - bound.Y, bound.Height);
        
        if (bound.Width <= 0 || bound.Height <= 0) continue;
        
        using var labelRoi = new Mat(src, bound);
        
        // Thử detect QR
        string qrText = "";
        Point2f[] qrPoints = null;
        try {
            using var qr = new QRCodeDetector();
            qrText = qr.DetectAndDecode(labelRoi, out qrPoints);
        }
        catch { }
        
        if (!string.IsNullOrEmpty(qrText)) {
            // Tìm thấy QR → đây là nhãn đúng
            var box = rect.Points().Select(p =>
                new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            
            edges.Dispose();
            return (rect, box, qrText, qrPoints);
        }
    }
    
    edges.Dispose();
    return (null, null, null, null);
}
```

#### Chi tiết các bộ lọc contour

**1. Area Ratio Filter**
```
Mục đích: Loại bỏ contour quá nhỏ (nhiễu) hoặc quá lớn (cả khung hình)
Ngưỡng: 5% < area < 50% của frame
Lý do: Nhãn thường chiếm 10-30% trong guide box
```

**2. Aspect Ratio Filter**
```
Mục đích: Nhãn có tỷ lệ dài/rộng cố định
Ngưỡng: 1.2 < aspect < 3.5
Lý do: Nhãn thường hình chữ nhật (ví dụ: 10cm x 5cm = aspect 2.0)
```

**3. Solidity Filter**
```
Mục đích: Nhãn phải gần hình chữ nhật (không lỗ thủng, không lõm)
Công thức: Solidity = ContourArea / BoundingRectArea
Ngưỡng: > 0.7 (70%)
Lý do: Nhãn có biên thẳng, ít lỗ → solidity cao
```

---

### **Phase 4: Implement Strategy LOW (QR-First) (2-3 giờ)**

```csharp
private static (RotatedRect?, Point[]?, string?, Point2f[]?) 
    DetectWithLowContrast(Mat src, Mat gray)
{
    Debug.WriteLine("🔴 Strategy: LOW CONTRAST - QR-First");
    
    // 1. Detect QR code trước trong toàn ảnh
    string qrText = "";
    Point2f[] qrPoints = null;
    
    try {
        using var qr = new QRCodeDetector();
        qrText = qr.DetectAndDecode(src, out qrPoints);
    }
    catch (Exception ex) {
        Debug.WriteLine($"[QR Detection Failed] {ex.Message}");
        return (null, null, null, null);
    }
    
    if (string.IsNullOrEmpty(qrText) || qrPoints == null || qrPoints.Length < 4) {
        Debug.WriteLine("❌ No QR code found");
        return (null, null, null, null);
    }
    
    Debug.WriteLine($"✅ QR found: {qrText}");
    
    // 2. Mở rộng vùng QR để tìm toàn bộ nhãn
    // Giả định: QR chiếm 1/4 diện tích nhãn → mở rộng 2x mỗi chiều
    var qrBound = Cv2.BoundingRect(
        qrPoints.Select(p => new Point((int)p.X, (int)p.Y)).ToArray()
    );
    
    int expandX = (int)(qrBound.Width * 1.2); // +120% theo X
    int expandY = (int)(qrBound.Height * 1.2); // +120% theo Y
    
    Rect searchRegion = new Rect(
        Math.Max(0, qrBound.X - expandX),
        Math.Max(0, qrBound.Y - expandY),
        Math.Min(src.Width - (qrBound.X - expandX), qrBound.Width + 2 * expandX),
        Math.Min(src.Height - (qrBound.Y - expandY), qrBound.Height + 2 * expandY)
    );
    
    // 3. Crop vùng tìm kiếm
    Mat searchRoi = new Mat(src, searchRegion);
    Mat grayRoi = new Mat(gray, searchRegion);
    
    // 4. Adaptive Threshold (tính threshold riêng cho từng vùng nhỏ)
    Mat adaptive = new Mat();
    Cv2.AdaptiveThreshold(
        grayRoi,
        adaptive,
        maxValue: 255,
        adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
        thresholdType: ThresholdTypes.Binary,
        blockSize: 15,  // Cửa sổ 15x15 (phải là số lẻ)
        C: 2            // Hằng số trừ từ mean
    );
    
    // 5. Morphology để làm sạch
    Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
    Cv2.MorphologyEx(adaptive, adaptive, MorphTypes.Close, kernel, iterations: 2);
    
    // 6. Find contours trong vùng này
    Cv2.FindContours(adaptive, out Point[][] contours, out _,
        RetrievalModes.External, ContourApproximationModes.ApproxSimple);
    
    if (contours == null || contours.Length == 0) {
        searchRoi.Dispose();
        grayRoi.Dispose();
        adaptive.Dispose();
        return (null, null, null, null);
    }
    
    // 7. Tìm contour chứa QR code
    // Convert QR points sang tọa độ trong searchRoi
    Point2f[] qrPointsLocal = qrPoints.Select(p => 
        new Point2f(p.X - searchRegion.X, p.Y - searchRegion.Y)
    ).ToArray();
    
    Point2f qrCenter = new Point2f(
        qrPointsLocal.Average(p => p.X),
        qrPointsLocal.Average(p => p.Y)
    );
    
    foreach (var c in contours) {
        // Kiểm tra contour có chứa tâm QR không
        double result = Cv2.PointPolygonTest(c, qrCenter, measureDist: false);
        
        if (result >= 0) { // Inside or on edge
            // Đây là contour nhãn
            var rect = Cv2.MinAreaRect(c);
            
            // Convert rect về tọa độ gốc (src)
            rect.Center = new Point2f(
                rect.Center.X + searchRegion.X,
                rect.Center.Y + searchRegion.Y
            );
            
            var box = rect.Points().Select(p =>
                new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            
            searchRoi.Dispose();
            grayRoi.Dispose();
            adaptive.Dispose();
            
            return (rect, box, qrText, qrPoints);
        }
    }
    
    // 8. Không tìm thấy contour chứa QR → fallback: dùng expanded QR bound
    Debug.WriteLine("⚠️ Fallback: Using expanded QR bound as label");
    
    // Tạo RotatedRect từ searchRegion
    RotatedRect fallbackRect = new RotatedRect(
        center: new Point2f(
            searchRegion.X + searchRegion.Width / 2.0f,
            searchRegion.Y + searchRegion.Height / 2.0f
        ),
        size: new Size2f(searchRegion.Width, searchRegion.Height),
        angle: 0
    );
    
    var fallbackBox = fallbackRect.Points().Select(p =>
        new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
    
    searchRoi.Dispose();
    grayRoi.Dispose();
    adaptive.Dispose();
    
    return (fallbackRect, fallbackBox, qrText, qrPoints);
}
```

#### Giải thích Adaptive Threshold

**So với Binary Threshold thông thường:**
```
Binary Threshold:
  pixel > GLOBAL_THRESHOLD → 255 (trắng)
  pixel <= GLOBAL_THRESHOLD → 0 (đen)
  → Chỉ 1 threshold cho cả ảnh

Adaptive Threshold:
  Chia ảnh thành các block nhỏ (15x15)
  Mỗi block có threshold riêng = mean(block) - C
  → Thích ứng với ánh sáng cục bộ
```

**Tham số:**
- **blockSize = 15**: Kích thước cửa sổ (phải lẻ), càng lớn càng smooth
- **C = 2**: Hằng số điều chỉnh, threshold = mean - C
- **GaussianC**: Dùng trọng số Gaussian (center pixels quan trọng hơn)

---

### **Phase 5: Tích hợp vào hàm DetectLabelRegion chính (1 giờ)**

```csharp
public static (RotatedRect? rect, Point[]? box, string? qrText, Point2f[]? qrPoints)
    DetectLabelRegion(Bitmap inputBmp, int thresholdValue = 150)
{
    if (inputBmp == null)
        return (null, null, null, null);

    // Convert Bitmap -> Mat (BGR)
    Mat src;
    using (var ms = new MemoryStream()) {
        inputBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        src = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
    }

    try {
        // PREPROCESSING
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);
        
        // AUTO DETECTION: Phân tích độ tương phản
        var analysis = AnalyzeFrame(gray);
        
        Debug.WriteLine($"📊 Frame Analysis:");
        Debug.WriteLine($"   Final Score: {analysis.FinalScore:F3}");
        Debug.WriteLine($"   Level: {analysis.Level}");
        Debug.WriteLine($"   Separation: {analysis.Separation:F3} (peaks: {analysis.Peak1Position}, {analysis.Peak2Position})");
        Debug.WriteLine($"   Edge Strength: {analysis.EdgeStrength:F3} ({analysis.EdgePixelCount} pixels)");
        Debug.WriteLine($"   Contrast: {analysis.ContrastRatio:F3} (stddev: {analysis.StdDevIntensity:F1})");
        
        // STRATEGY SELECTION & EXECUTION
        (RotatedRect?, Point[]?, string?, Point2f[]?) result = (null, null, null, null);
        
        switch (analysis.Level) {
            case ContrastLevel.High:
                result = DetectWithHighContrast(src, gray, thresholdValue);
                break;
                
            case ContrastLevel.Medium:
                result = DetectWithMediumContrast(src, gray);
                // Fallback to High nếu thất bại
                if (result.Item1 == null) {
                    Debug.WriteLine("⚠️ Medium failed, fallback to High");
                    result = DetectWithHighContrast(src, gray, thresholdValue);
                }
                break;
                
            case ContrastLevel.Low:
                result = DetectWithLowContrast(src, gray);
                // Fallback to Medium nếu thất bại
                if (result.Item1 == null) {
                    Debug.WriteLine("⚠️ Low failed, fallback to Medium");
                    result = DetectWithMediumContrast(src, gray);
                }
                // Fallback to High nếu vẫn thất bại
                if (result.Item1 == null) {
                    Debug.WriteLine("⚠️ Medium failed, fallback to High");
                    result = DetectWithHighContrast(src, gray, thresholdValue);
                }
                break;
        }
        
        return result;
    }
    finally {
        src.Dispose();
    }
}
```

---

### **Phase 6: Testing & Fine-tuning (2-3 giờ)**

#### 6.1. Tạo test cases
```csharp
// Danh sách ảnh test
var testImages = new[] {
    // HIGH contrast
    "test_images/black_shirt.jpg",
    "test_images/red_shirt.jpg",
    "test_images/blue_shirt.jpg",
    
    // MEDIUM contrast
    "test_images/pink_shirt.jpg",
    "test_images/gray_shirt.jpg",
    "test_images/beige_shirt.jpg",
    
    // LOW contrast
    "test_images/white_shirt.jpg",
    "test_images/cream_shirt.jpg",
    "test_images/light_gray_shirt.jpg"
};

foreach (var imgPath in testImages) {
    Bitmap bmp = new Bitmap(imgPath);
    var result = LabelDetector.DetectLabelRegion(bmp, 180);
    
    Console.WriteLine($"\n{Path.GetFileName(imgPath)}:");
    Console.WriteLine($"  Detected: {result.rect != null}");
    Console.WriteLine($"  QR: {result.qrText}");
}
```

#### 6.2. Điều chỉnh thresholds
Dựa trên kết quả test, fine-tune các ngưỡng:

```csharp
// Có thể cần điều chỉnh:
// - Final Score thresholds (0.6, 0.3)
// - Canny thresholds (30, 100)
// - Morphology iterations
// - Area ratio thresholds (0.05, 0.5)
// - Aspect ratio thresholds (1.2, 3.5)
// - Adaptive blockSize (15)
```

#### 6.3. Thêm telemetry
```csharp
// Trong Form1.cs, hiển thị metrics lên UI
label_analysis.Text = $"Score: {analysis.FinalScore:F2} | " +
                      $"Level: {analysis.Level} | " +
                      $"Sep: {analysis.Separation:F2}";
```

---

## 📊 Bảng tổng kết thời gian & độ ưu tiên

| Phase | Nhiệm vụ | Thời gian | Độ ưu tiên | Output |
|-------|----------|-----------|------------|--------|
| **1** | Hệ thống phân tích tự động | 3-4h | 🔴 Cao nhất | `AnalyzeFrame()`, metrics |
| **2** | Strategy HIGH (giữ nguyên) | 0.5h | 🟢 Thấp | Refactor code cũ |
| **3** | Strategy MEDIUM (Edge) | 2-3h | 🟡 Trung bình | `DetectWithMediumContrast()` |
| **4** | Strategy LOW (QR-First) | 2-3h | 🟠 Cao | `DetectWithLowContrast()` |
| **5** | Tích hợp & routing | 1h | 🔴 Cao nhất | `DetectLabelRegion()` hoàn chỉnh |
| **6** | Testing & fine-tuning | 2-3h | 🔴 Cao nhất | Điều chỉnh thresholds |

**Tổng thời gian ước tính: 11-15 giờ**

---

## 🎯 Lộ trình triển khai khuyến nghị

### **Sprint 1 (1 tuần): Core Foundation**
- [ ] Phase 1: Xây dựng hệ thống phân tích (3 metrics)
- [ ] Phase 2: Refactor code HIGH contrast
- [ ] Phase 5: Tích hợp routing cơ bản (chỉ HIGH)
- [ ] Test với áo tối/màu đậm → Đảm bảo không làm hỏng tính năng cũ

### **Sprint 2 (1 tuần): Medium Contrast**
- [ ] Phase 3: Implement Edge Detection strategy
- [ ] Tích hợp vào routing (HIGH + MEDIUM)
- [ ] Test với áo màu nhạt (hồng, be, xám)

### **Sprint 3 (1 tuần): Low Contrast & Polish**
- [ ] Phase 4: Implement QR-First strategy
- [ ] Tích hợp full 3 strategies với fallback
- [ ] Phase 6: Testing toàn diện + fine-tuning
- [ ] Thêm logging, UI feedback

---

## 🔧 Tối ưu hóa hiệu năng

### 1. Early Exit
```csharp
// Dừng ngay khi tìm thấy
if (highResult.Item1 != null) return highResult;
```

### 2. ROI Processing
```csharp
// Chỉ analyze vùng guide box thay vì toàn frame
Mat roi = new Mat(src, guideBoxRegion);
var analysis = AnalyzeFrame(roi);
```

### 3. Resize Input
```csharp
// Downscale cho analysis (tăng tốc 4x)
if (gray.Width > 640) {
    Mat small = new Mat();
    double scale = 640.0 / gray.Width;
    Cv2.Resize(gray, small, Size.Zero, scale, scale);
    var analysis = AnalyzeFrame(small);
    small.Dispose();
}
```

### 4. Cache QR Detection
```csharp
// Cache QR trong 10 frames (QR không đổi nhanh)
private static Dictionary<int, (string, Point2f[], DateTime)> _qrCache;
private static int _frameCounter = 0;

private static (string?, Point2f[]?) GetCachedQR(Mat src) {
    _frameCounter++;
    if (_qrCache.TryGetValue(_frameCounter / 10, out var cached)) {
        if ((DateTime.Now - cached.Item3).TotalMilliseconds < 500) {
            return (cached.Item1, cached.Item2);
        }
    }
    
    // Detect mới
    using var qr = new QRCodeDetector();
    string text = qr.DetectAndDecode(src, out Point2f[] points);
    
    if (!string.IsNullOrEmpty(text)) {
        _qrCache[_frameCounter / 10] = (text, points, DateTime.Now);
    }
    
    return (text, points);
}
```

---

## 📈 Kỳ vọng kết quả

### Độ chính xác

| Nhóm màu | Trước (Binary only) | Sau (3-tier) | Cải thiện |
|----------|---------------------|--------------|-----------|
| Đen, Đậm | 95% | 95% | - |
| Màu (Đỏ, Xanh) | 90% | 92% | +2% |
| Màu nhạt | 60% | 85% | +25% ⭐ |
| Trắng, Kem | 20% | 75% | +55% ⭐⭐ |

### Tốc độ xử lý

| Strategy | Tỷ lệ sử dụng | Thời gian | Tốc độ trung bình |
|----------|---------------|-----------|-------------------|
| HIGH | 80% | 5-10ms | ~7ms |
| MEDIUM | 15% | 15-25ms | ~20ms |
| LOW | 5% | 50-100ms | ~70ms |
| **Weighted Avg** | 100% | - | **~12ms** ⚡ |

→ Vẫn đạt **>80 FPS**, đủ cho realtime camera

---

## 🐛 Troubleshooting

### Vấn đề: Strategy HIGH không hoạt động với áo đen
**Nguyên nhân:** Threshold quá cao (180)
**Giải pháp:** Giảm xuống 150-160, hoặc dùng Otsu auto threshold

### Vấn đề: Strategy MEDIUM bắt nhiều false positive
**Nguyên nhân:** Canny quá nhạy, bắt cả nếp gấp áo
**Giải pháp:** 
- Tăng Canny threshold1 từ 30 → 40
- Tăng area ratio filter từ 0.05 → 0.08
- Tăng solidity threshold từ 0.7 → 0.75

### Vấn đề: Strategy LOW quá chậm
**Nguyên nhân:** QR detection trên full image tốn thời gian
**Giải pháp:**
- Resize image xuống 640px trước khi detect QR
- Chỉ detect trong vùng guide box
- Cache QR result cho 5-10 frames

### Vấn đề: Metrics không ổn định giữa các frame
**Nguyên nhân:** Nhiễu, dao động ánh sáng
**Giải pháp:**
- Smooth metrics bằng moving average 3-5 frames
- Tăng GaussianBlur kernel từ 5x5 → 7x7

---

## 📚 Tài liệu tham khảo

### OpenCV Documentation
- [Histogram Calculation](https://docs.opencv.org/4.x/d8/dbc/tutorial_histogram_calculation.html)
- [Canny Edge Detection](https://docs.opencv.org/4.x/da/d22/tutorial_py_canny.html)
- [Adaptive Thresholding](https://docs.opencv.org/4.x/d7/d4d/tutorial_py_thresholding.html)
- [Morphological Transformations](https://docs.opencv.org/4.x/d9/d61/tutorial_py_morphological_ops.html)

### Papers & Articles
- Otsu's Binarization: "A Threshold Selection Method from Gray-Level Histograms" (1979)
- Canny Edge Detector: "A Computational Approach to Edge Detection" (1986)
- Adaptive Thresholding: OpenCV documentation

---

## ✅ Checklist triển khai

### Phase 1: Analysis System
- [ ] Tạo enum `ContrastLevel`
- [ ] Tạo struct `ContrastAnalysisResult`
- [ ] Implement `AnalyzeHistogram()` với peak finding
- [ ] Implement `AnalyzeEdges()` với Canny
- [ ] Implement `AnalyzeContrast()` với stddev
- [ ] Implement `AnalyzeFrame()` tổng hợp
- [ ] Test metrics với 10 ảnh mẫu
- [ ] Log metrics ra Debug console

### Phase 2: Strategy HIGH
- [ ] Refactor code cũ thành `DetectWithHighContrast()`
- [ ] Thêm debug logging
- [ ] Test không làm hỏng tính năng cũ

### Phase 3: Strategy MEDIUM
- [ ] Implement `DetectWithMediumContrast()`
- [ ] Implement Canny detection
- [ ] Implement morphology pipeline
- [ ] Implement contour filtering (area, aspect, solidity)
- [ ] Implement QR verification loop
- [ ] Test với 5 ảnh áo màu nhạt

### Phase 4: Strategy LOW
- [ ] Implement `DetectWithLowContrast()`
- [ ] Implement QR-first detection
- [ ] Implement region expansion logic
- [ ] Implement Adaptive Threshold
- [ ] Implement contour-contains-QR check
- [ ] Implement fallback mechanism
- [ ] Test với 5 ảnh áo trắng/kem

### Phase 5: Integration
- [ ] Tích hợp 3 strategies vào `DetectLabelRegion()`
- [ ] Implement strategy routing logic
- [ ] Implement fallback chain (Low→Medium→High)
- [ ] Thêm comprehensive logging
- [ ] Test toàn bộ pipeline

### Phase 6: Testing & Tuning
- [ ] Tạo test set 30 ảnh (10 mỗi loại)
- [ ] Chạy batch test, đo accuracy
- [ ] Fine-tune thresholds dựa trên kết quả
- [ ] Tối ưu tốc độ (resize, ROI, cache)
- [ ] Stress test với camera realtime
- [ ] Document các thresholds cuối cùng

---

**Tác giả:** AI Assistant  
**Ngày tạo:** November 12, 2025  
**Phiên bản:** 1.0  
**Trạng thái:** Ready for Implementation
