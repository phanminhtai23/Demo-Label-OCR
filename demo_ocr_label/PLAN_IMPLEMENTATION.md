# 📋 Kế hoạch triển khai chi tiết - Label Detection System

## 🎯 Mục tiêu

Tạo lớp `LabelRegionExtractor` riêng biệt để xử lý logic phát hiện nhãn (label detection), tách biệt khỏi `LabelDetector` hiện tại. Điều này giúp:
- ✅ Dễ bảo trì và mở rộng
- ✅ Tách biệt trách nhiệm (Separation of Concerns)
- ✅ Dễ test từng phần
- ✅ Tương thích ngược với code hiện tại

---

## 📐 Kiến trúc hệ thống mới

```
┌─────────────────────────────────────────────┐
│           LabelDetector.cs                  │
│  (Class cũ - giữ nguyên interface)          │
│                                             │
│  + DetectLabelRegion()  ──────────┐        │
│       └─ Gọi extractor            │        │
│                                    │        │
│  + CropAndAlignLabel()             │        │
│  + Helper methods                  │        │
└────────────────────────────────────┼────────┘
                                     │
                                     ↓
┌─────────────────────────────────────────────┐
│      LabelRegionExtractor.cs (MỚI)         │
│  (Class mới - chứa toàn bộ logic detect)   │
│                                             │
│  + DetectLabelRegion() ← Hàm chính         │
│       │                                     │
│       ├─ AnalyzeFrame()                    │
│       │    ├─ AnalyzeHistogram()           │
│       │    ├─ AnalyzeEdges()               │
│       │    └─ AnalyzeContrast()            │
│       │                                     │
│       ├─ DetectWithHighContrast()          │
│       ├─ DetectWithMediumContrast()        │
│       └─ DetectWithLowContrast()           │
│                                             │
│  + Struct: ContrastAnalysisResult          │
│  + Enum: ContrastLevel                     │
└─────────────────────────────────────────────┘
```

---

## 📦 Cấu trúc file mới

### File: `LabelRegionExtractor.cs`

```
LabelRegionExtractor.cs
│
├─ Enum: ContrastLevel
├─ Struct: ContrastAnalysisResult
│
├─ Class: LabelRegionExtractor
│   │
│   ├─ Public Methods
│   │   └─ DetectLabelRegion(Mat src, int thresholdValue = 150)
│   │       → (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?)
│   │
│   ├─ Private: Analysis Methods (TẦNG 1)
│   │   ├─ AnalyzeFrame(Mat gray)
│   │   ├─ AnalyzeHistogram(Mat gray)
│   │   ├─ AnalyzeEdges(Mat gray)
│   │   └─ AnalyzeContrast(Mat gray)
│   │
│   └─ Private: Strategy Methods (TẦNG 2)
│       ├─ DetectWithHighContrast(Mat src, Mat gray, int threshold)
│       ├─ DetectWithMediumContrast(Mat src, Mat gray)
│       └─ DetectWithLowContrast(Mat src, Mat gray)
```

---

## 🔧 Chi tiết triển khai

### Phase 1: Tạo cấu trúc cơ bản (30 phút)

#### 1.1. Tạo file `LabelRegionExtractor.cs`

```csharp
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace demo_ocr_label
{
    /// <summary>
    /// Mức độ tương phản của ảnh
    /// </summary>
    public enum ContrastLevel
    {
        High,      // Score > 0.6 → Binary Threshold
        Medium,    // Score 0.3-0.6 → Edge Detection
        Low        // Score < 0.3 → QR-First
    }

    /// <summary>
    /// Kết quả phân tích độ tương phản
    /// </summary>
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

    /// <summary>
    /// Class chịu trách nhiệm phát hiện vùng nhãn (label region) trong ảnh
    /// Sử dụng hệ thống 3 tầng: Analysis → Strategy Selection → Detection
    /// </summary>
    public class LabelRegionExtractor
    {
        // Constants
        private const double HIGH_THRESHOLD = 0.6;
        private const double MEDIUM_THRESHOLD = 0.3;
        private const double EDGE_MAX = 0.1;

        public LabelRegionExtractor()
        {
        }

        /// <summary>
        /// Phát hiện vùng nhãn trong ảnh
        /// </summary>
        /// <param name="src">Ảnh nguồn (BGR, đã là Mat)</param>
        /// <param name="thresholdValue">Ngưỡng cho Binary Threshold (dùng cho Strategy HIGH)</param>
        /// <returns>
        /// Tuple gồm:
        /// - rect: RotatedRect của nhãn
        /// - box: 4 góc của nhãn
        /// - qrText: Nội dung QR code
        /// - qrPoints180: Tọa độ QR trong ROI cục bộ
        /// - qrPoints: Tọa độ QR trong ảnh gốc
        /// </returns>
        public static (RotatedRect? rect, Point[]? box, string? qrText, 
                       Point2f[]? qrPoints180, Point2f[]? qrPoints)
            DetectLabelRegion(Mat src, int thresholdValue = 150)
        {
            if (src == null || src.Empty())
                return (null, null, null, null, null);

            try
            {
                // PREPROCESSING
                using var gray = new Mat();
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);

                // TẦNG 1: Phân tích độ tương phản
                var analysis = AnalyzeFrame(gray);

                Debug.WriteLine($"📊 Frame Analysis:");
                Debug.WriteLine($"   Final Score: {analysis.FinalScore:F3}");
                Debug.WriteLine($"   Level: {analysis.Level}");
                Debug.WriteLine($"   Separation: {analysis.Separation:F3} (peaks: {analysis.Peak1Position}, {analysis.Peak2Position})");
                Debug.WriteLine($"   Edge Strength: {analysis.EdgeStrength:F3} ({analysis.EdgePixelCount} pixels)");
                Debug.WriteLine($"   Contrast: {analysis.ContrastRatio:F3} (stddev: {analysis.StdDevIntensity:F1})");

                // TẦNG 2: Chọn và thực thi chiến lược
                (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?) result;

                switch (analysis.Level)
                {
                    case ContrastLevel.High:
                        result = DetectWithHighContrast(src, gray, thresholdValue);
                        break;

                    case ContrastLevel.Medium:
                        result = DetectWithMediumContrast(src, gray);
                        // Fallback to High nếu thất bại
                        if (result.Item1 == null)
                        {
                            Debug.WriteLine("⚠️ Medium failed, fallback to High");
                            result = DetectWithHighContrast(src, gray, thresholdValue);
                        }
                        break;

                    case ContrastLevel.Low:
                        result = DetectWithLowContrast(src, gray);
                        // Fallback to Medium nếu thất bại
                        if (result.Item1 == null)
                        {
                            Debug.WriteLine("⚠️ Low failed, fallback to Medium");
                            result = DetectWithMediumContrast(src, gray);
                        }
                        // Fallback to High nếu vẫn thất bại
                        if (result.Item1 == null)
                        {
                            Debug.WriteLine("⚠️ Medium failed, fallback to High");
                            result = DetectWithHighContrast(src, gray, thresholdValue);
                        }
                        break;

                    default:
                        result = (null, null, null, null, null);
                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DetectLabelRegion ERROR] {ex.Message}");
                return (null, null, null, null, null);
            }
        }

        #region TẦNG 1: Analysis Methods

        private static ContrastAnalysisResult AnalyzeFrame(Mat gray)
        {
            // TODO: Implement Phase 1
            throw new NotImplementedException();
        }

        private static (int peak1, int peak2, double separation) AnalyzeHistogram(Mat gray)
        {
            // TODO: Implement Phase 1
            throw new NotImplementedException();
        }

        private static (int edgePixels, double edgeStrength) AnalyzeEdges(Mat gray)
        {
            // TODO: Implement Phase 1
            throw new NotImplementedException();
        }

        private static (double mean, double stddev, double ratio) AnalyzeContrast(Mat gray)
        {
            // TODO: Implement Phase 1
            throw new NotImplementedException();
        }

        #endregion

        #region TẦNG 2: Strategy Methods

        private static (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?)
            DetectWithHighContrast(Mat src, Mat gray, int thresholdValue)
        {
            // TODO: Implement Phase 2
            throw new NotImplementedException();
        }

        private static (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?)
            DetectWithMediumContrast(Mat src, Mat gray)
        {
            // TODO: Implement Phase 3
            throw new NotImplementedException();
        }

        private static (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?)
            DetectWithLowContrast(Mat src, Mat gray)
        {
            // TODO: Implement Phase 4
            throw new NotImplementedException();
        }

        #endregion
    }
}
```

---

### Phase 2: Cập nhật `LabelDetector.cs` (15 phút)

#### 2.1. Refactor hàm `DetectLabelRegion` để sử dụng extractor mới

```csharp
// File: LabelDetector.cs

public static (RotatedRect? rect, Point[]? box, string? qrText, 
               Point2f[]? qrPoints180, Point2f[]? qrPoints)
    DetectLabelRegion(Bitmap inputBmp, int thresholdValue = 150)
{
    if (inputBmp == null)
        return (null, null, null, null, null);

    // Convert Bitmap -> Mat (BGR)
    Mat src;
    using (var ms = new MemoryStream())
    {
        inputBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        src = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
    }

    try
    {
        // 🔹 GỌI CLASS MỚI - LabelRegionExtractor
        return LabelRegionExtractor.DetectLabelRegion(src, thresholdValue);
    }
    finally
    {
        src?.Dispose();
    }
}
```

**Lợi ích:**
- ✅ Interface không đổi → Code gọi hàm không cần sửa
- ✅ Logic phức tạp được tách ra class riêng
- ✅ `LabelDetector` chỉ còn lo chuyển đổi Bitmap → Mat

---

### Phase 3: Implement Analysis Methods (3-4 giờ)

#### 3.1. `AnalyzeHistogram()` - Tìm 2 peaks

```csharp
private static (int peak1, int peak2, double separation) AnalyzeHistogram(Mat gray)
{
    // Lấy vùng center (giả sử nhãn ở giữa)
    int sampleSize = Math.Min(gray.Width, gray.Height) / 3;
    var centerRegion = new Rect(
        gray.Width / 2 - sampleSize / 2,
        gray.Height / 2 - sampleSize / 2,
        sampleSize, sampleSize
    );
    using var centerRoi = new Mat(gray, centerRegion);

    // Tính histogram
    using var hist = new Mat();
    Cv2.CalcHist(
        new[] { centerRoi },
        new[] { 0 },
        null,
        hist,
        1,
        new[] { 256 },
        new[] { new Rangef(0, 256) }
    );

    // Smooth histogram (moving average)
    float[] histData = new float[256];
    hist.GetArray(out histData);

    float[] smoothed = new float[256];
    for (int i = 2; i < 254; i++)
    {
        smoothed[i] = (histData[i - 2] + histData[i - 1] + histData[i] +
                       histData[i + 1] + histData[i + 2]) / 5.0f;
    }

    // Tìm 2 local maxima
    var peaks = new List<(int pos, float val)>();
    float avgHeight = smoothed.Average();
    float threshold = avgHeight * 0.5f;

    for (int i = 10; i < 246; i++)
    {
        bool isLocalMax = smoothed[i] > smoothed[i - 1] &&
                          smoothed[i] > smoothed[i + 1] &&
                          smoothed[i] > threshold;

        if (isLocalMax)
        {
            bool tooCloseToExisting = peaks.Any(p => Math.Abs(p.pos - i) < 30);
            if (!tooCloseToExisting)
            {
                peaks.Add((i, smoothed[i]));
            }
        }
    }

    // Lấy 2 peak cao nhất
    peaks = peaks.OrderByDescending(p => p.val).Take(2).ToList();

    if (peaks.Count < 2)
    {
        return (0, 255, 0.0);
    }

    // Sắp xếp theo position
    peaks = peaks.OrderBy(p => p.pos).ToList();

    int peak1 = peaks[0].pos;
    int peak2 = peaks[1].pos;
    double separation = Math.Abs(peak2 - peak1) / 255.0;

    return (peak1, peak2, separation);
}
```

#### 3.2. `AnalyzeEdges()` - Đếm edge pixels

```csharp
private static (int edgePixels, double edgeStrength) AnalyzeEdges(Mat gray)
{
    int sampleSize = Math.Min(gray.Width, gray.Height) / 3;
    var centerRegion = new Rect(
        gray.Width / 2 - sampleSize / 2,
        gray.Height / 2 - sampleSize / 2,
        sampleSize, sampleSize
    );
    using var centerRoi = new Mat(gray, centerRegion);

    // Canny Edge Detection
    using var edges = new Mat();
    Cv2.Canny(centerRoi, edges, threshold1: 50, threshold2: 150);

    // Đếm edge pixels
    int edgePixels = Cv2.CountNonZero(edges);
    int totalPixels = centerRoi.Width * centerRoi.Height;

    double edgeStrength = (double)edgePixels / totalPixels;

    return (edgePixels, edgeStrength);
}
```

#### 3.3. `AnalyzeContrast()` - Tính stddev

```csharp
private static (double mean, double stddev, double ratio) AnalyzeContrast(Mat gray)
{
    int sampleSize = Math.Min(gray.Width, gray.Height) / 3;
    var centerRegion = new Rect(
        gray.Width / 2 - sampleSize / 2,
        gray.Height / 2 - sampleSize / 2,
        sampleSize, sampleSize
    );
    using var centerRoi = new Mat(gray, centerRegion);

    // Tính mean và stddev
    Cv2.MeanStdDev(centerRoi, out Scalar meanScalar, out Scalar stddevScalar);

    double mean = meanScalar.Val0;
    double stddev = stddevScalar.Val0;
    double ratio = stddev / 128.0;

    return (mean, stddev, ratio);
}
```

#### 3.4. `AnalyzeFrame()` - Tổng hợp

```csharp
private static ContrastAnalysisResult AnalyzeFrame(Mat gray)
{
    // 1. Phân tích histogram
    var (peak1, peak2, separation) = AnalyzeHistogram(gray);

    // 2. Phân tích edges
    var (edgePixels, edgeStrength) = AnalyzeEdges(gray);

    // 3. Phân tích contrast
    var (mean, stddev, contrastRatio) = AnalyzeContrast(gray);

    // 4. Tính Final Score
    double edgeStrengthNorm = Math.Min(edgeStrength / EDGE_MAX, 1.0);
    double finalScore =
        separation * 0.4 +
        edgeStrengthNorm * 0.3 +
        contrastRatio * 0.3;

    // 5. Xác định level
    ContrastLevel level;
    if (finalScore > HIGH_THRESHOLD)
        level = ContrastLevel.High;
    else if (finalScore > MEDIUM_THRESHOLD)
        level = ContrastLevel.Medium;
    else
        level = ContrastLevel.Low;

    // 6. Trả về kết quả
    return new ContrastAnalysisResult
    {
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

### Phase 4: Implement Strategy HIGH (0.5 giờ)

```csharp
private static (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?)
    DetectWithHighContrast(Mat src, Mat gray, int thresholdValue)
{
    Debug.WriteLine("🟢 Strategy: HIGH CONTRAST - Binary Threshold");

    using var binary = new Mat();
    Cv2.Threshold(gray, binary, thresholdValue, 255, ThresholdTypes.Binary);

    using var morph = new Mat();
    using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
    Cv2.MorphologyEx(binary, morph, MorphTypes.Open, kernel, iterations: 1);
    Cv2.MorphologyEx(morph, morph, MorphTypes.Close, kernel, iterations: 2);

    Cv2.FindContours(morph, out Point[][] contours, out _,
        RetrievalModes.External, ContourApproximationModes.ApproxSimple);

    if (contours == null || contours.Length == 0)
        return (null, null, null, null, null);

    // Chọn contour lớn nhất
    Point[] biggest = null;
    double maxArea = 0;
    foreach (var c in contours)
    {
        double area = Cv2.ContourArea(c);
        if (area > maxArea)
        {
            maxArea = area;
            biggest = c;
        }
    }

    if (biggest == null)
        return (null, null, null, null, null);

    // MinAreaRect
    var rect = Cv2.MinAreaRect(biggest);
    var box = rect.Points().Select(p =>
        new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();

    // Crop và verify QR
    var bound = Cv2.BoundingRect(biggest);
    bound.X = Math.Max(0, bound.X);
    bound.Y = Math.Max(0, bound.Y);
    bound.Width = Math.Min(src.Width - bound.X, bound.Width);
    bound.Height = Math.Min(src.Height - bound.Y, bound.Height);

    using var labelRoi = new Mat(src, bound);

    string qrText = "";
    Point2f[] qrPoints180 = null;
    Point2f[] qrPoints = null;

    try
    {
        using var qr = new QRCodeDetector();
        using var straight = new Mat();
        qrText = qr.DetectAndDecode(labelRoi, out qrPoints180, straight);

        if (qrPoints180 != null)
        {
            qrPoints = new Point2f[qrPoints180.Length];
            for (int i = 0; i < qrPoints180.Length; i++)
            {
                qrPoints[i] = new Point2f(
                    qrPoints180[i].X + bound.X,
                    qrPoints180[i].Y + bound.Y
                );
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[QR ERROR] {ex.Message}");
    }

    if (!string.IsNullOrEmpty(qrText))
        return (rect, box, qrText, qrPoints180, qrPoints);

    return (null, null, null, null, null);
}
```

---

### Phase 5: Implement Strategy MEDIUM (2-3 giờ)

```csharp
private static (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?)
    DetectWithMediumContrast(Mat src, Mat gray)
{
    Debug.WriteLine("🟡 Strategy: MEDIUM CONTRAST - Edge Detection");

    using var edges = new Mat();
    Cv2.Canny(gray, edges, threshold1: 30, threshold2: 100);

    using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
    Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel, iterations: 3);
    Cv2.MorphologyEx(edges, edges, MorphTypes.Dilate, kernel, iterations: 1);

    Cv2.FindContours(edges, out Point[][] contours, out _,
        RetrievalModes.External, ContourApproximationModes.ApproxSimple);

    if (contours == null || contours.Length == 0)
        return (null, null, null, null, null);

    // Lọc và sort contours
    var candidates = new List<(Point[] contour, RotatedRect rect, double area)>();
    double roiArea = gray.Width * gray.Height;

    foreach (var c in contours)
    {
        double area = Cv2.ContourArea(c);
        double areaRatio = area / roiArea;

        // CHỈ lọc theo area
        if (areaRatio < 0.05 || areaRatio > 0.80) continue;

        var rect = Cv2.MinAreaRect(c);
        candidates.Add((c, rect, area));
    }

    // Sắp xếp theo area (lớn nhất trước)
    candidates = candidates.OrderByDescending(x => x.area).ToList();

    // Lặp và verify QR
    foreach (var (contour, rect, _) in candidates)
    {
        var bound = Cv2.BoundingRect(contour);
        bound.X = Math.Max(0, bound.X);
        bound.Y = Math.Max(0, bound.Y);
        bound.Width = Math.Min(src.Width - bound.X, bound.Width);
        bound.Height = Math.Min(src.Height - bound.Y, bound.Height);

        if (bound.Width <= 0 || bound.Height <= 0) continue;

        using var labelRoi = new Mat(src, bound);

        string qrText = "";
        Point2f[] qrPoints180 = null;
        Point2f[] qrPoints = null;

        try
        {
            using var qr = new QRCodeDetector();
            using var straight = new Mat();
            qrText = qr.DetectAndDecode(labelRoi, out qrPoints180, straight);

            if (qrPoints180 != null)
            {
                qrPoints = new Point2f[qrPoints180.Length];
                for (int i = 0; i < qrPoints180.Length; i++)
                {
                    qrPoints[i] = new Point2f(
                        qrPoints180[i].X + bound.X,
                        qrPoints180[i].Y + bound.Y
                    );
                }
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(qrText))
        {
            var box = rect.Points().Select(p =>
                new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            return (rect, box, qrText, qrPoints180, qrPoints);
        }
    }

    return (null, null, null, null, null);
}
```

---

### Phase 6: Implement Strategy LOW (2-3 giờ)

```csharp
private static (RotatedRect?, Point[]?, string?, Point2f[]?, Point2f[]?)
    DetectWithLowContrast(Mat src, Mat gray)
{
    Debug.WriteLine("🔴 Strategy: LOW CONTRAST - QR-First Geometry");

    // 1. Detect QR trước
    string qrText = "";
    Point2f[] qrPoints = null;

    try
    {
        using var qr = new QRCodeDetector();
        using var straight = new Mat();
        qrText = qr.DetectAndDecode(src, out qrPoints, straight);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[QR Detection Failed] {ex.Message}");
        return (null, null, null, null, null);
    }

    if (string.IsNullOrEmpty(qrText) || qrPoints == null || qrPoints.Length < 4)
    {
        Debug.WriteLine("❌ No QR code found");
        return (null, null, null, null, null);
    }

    Debug.WriteLine($"✅ QR found: {qrText}");

    // 2. Tính geometry QR
    var p0 = qrPoints[0]; // top-left
    var p1 = qrPoints[1]; // top-right
    var p3 = qrPoints[3]; // bottom-left

    var topVec = new Point2f(p1.X - p0.X, p1.Y - p0.Y);
    var leftVec = new Point2f(p3.X - p0.X, p3.Y - p0.Y);

    float qrWidth = (float)Math.Sqrt(topVec.X * topVec.X + topVec.Y * topVec.Y);
    float qrHeight = (float)Math.Sqrt(leftVec.X * leftVec.X + leftVec.Y * leftVec.Y);

    // 3. Vector đơn vị
    var dirRight = new Point2f(topVec.X / qrWidth, topVec.Y / qrWidth);
    var dirDown = new Point2f(leftVec.X / qrHeight, leftVec.Y / qrHeight);
    var dirLeft = new Point2f(-dirRight.X, -dirRight.Y);

    Debug.WriteLine($"📐 QR geometry: width={qrWidth:F1}, height={qrHeight:F1}");

    // 4. Suy luận nhãn (QR chiếm 1/3)
    const float LABEL_WIDTH_RATIO = 3.0f;
    float labelWidth = qrWidth * LABEL_WIDTH_RATIO;
    float labelHeight = qrHeight;

    // 5. Tính 4 góc nhãn
    var labelTopRight = p0;

    var labelTopLeft = new Point2f(
        labelTopRight.X + dirLeft.X * (labelWidth - qrWidth),
        labelTopRight.Y + dirLeft.Y * (labelWidth - qrWidth)
    );

    var labelBottomRight = new Point2f(
        labelTopRight.X + dirDown.X * labelHeight,
        labelTopRight.Y + dirDown.Y * labelHeight
    );

    var labelBottomLeft = new Point2f(
        labelTopLeft.X + dirDown.X * labelHeight,
        labelTopLeft.Y + dirDown.Y * labelHeight
    );

    Debug.WriteLine($"📐 Predicted label: width={labelWidth:F1}, height={labelHeight:F1}");

    // 6. Tạo RotatedRect
    var labelCenter = new Point2f(
        (labelTopLeft.X + labelTopRight.X + labelBottomRight.X + labelBottomLeft.X) / 4.0f,
        (labelTopLeft.Y + labelTopRight.Y + labelBottomRight.Y + labelBottomLeft.Y) / 4.0f
    );

    float angle = (float)(Math.Atan2(topVec.Y, topVec.X) * (180.0 / Math.PI));

    var labelRect = new RotatedRect(
        center: labelCenter,
        size: new Size2f(labelWidth, labelHeight),
        angle: angle
    );

    var labelBox = new[] { labelTopLeft, labelTopRight, labelBottomRight, labelBottomLeft }
        .Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))
        .ToArray();

    // qrPoints180 = null vì không có ROI cục bộ (detect trên toàn ảnh)
    return (labelRect, labelBox, qrText, null, qrPoints);
}
```

---

## 🧪 Testing Strategy

### Test 1: Tương thích ngược (Regression Test)
```csharp
// Chạy với ảnh test cũ, đảm bảo kết quả không đổi
var oldResult = OldDetectLabelRegion(testImage);
var newResult = LabelRegionExtractor.DetectLabelRegion(testImageMat);

Assert.AreEqual(oldResult.qrText, newResult.qrText);
```

### Test 2: Test từng strategy
```csharp
// HIGH: Áo đen
// MEDIUM: Áo xám
// LOW: Áo trắng
```

### Test 3: Stress test với camera realtime
```csharp
// Đo FPS, memory usage
```

---

## 📊 Checklist triển khai

### Phase 1: Cấu trúc cơ bản ✅
- [ ] Tạo file `LabelRegionExtractor.cs`
- [ ] Tạo enum `ContrastLevel`
- [ ] Tạo struct `ContrastAnalysisResult`
- [ ] Tạo skeleton class với hàm `DetectLabelRegion`
- [ ] Tạo các method stubs (NotImplementedException)

### Phase 2: Cập nhật LabelDetector ✅
- [ ] Refactor `DetectLabelRegion` để gọi extractor
- [ ] Test không làm hỏng code hiện tại
- [ ] Đảm bảo interface không đổi

### Phase 3: Analysis Methods ✅
- [ ] Implement `AnalyzeHistogram()`
- [ ] Implement `AnalyzeEdges()`
- [ ] Implement `AnalyzeContrast()`
- [ ] Implement `AnalyzeFrame()`
- [ ] Test với 10 ảnh mẫu, log metrics

### Phase 4: Strategy HIGH ✅
- [ ] Implement `DetectWithHighContrast()`
- [ ] Test với ảnh áo tối
- [ ] Verify QR detection

### Phase 5: Strategy MEDIUM ✅
- [ ] Implement `DetectWithMediumContrast()`
- [ ] Test với ảnh áo màu nhạt
- [ ] Verify fallback logic

### Phase 6: Strategy LOW ✅
- [ ] Implement `DetectWithLowContrast()`
- [ ] Test với ảnh áo trắng
- [ ] Verify geometry calculation

### Phase 7: Integration Testing ✅
- [ ] Test toàn bộ pipeline
- [ ] Test fallback chain
- [ ] Regression test với code cũ
- [ ] Stress test với camera

---

## ⏱️ Timeline

| Phase | Nhiệm vụ | Thời gian |
|-------|----------|-----------|
| 1 | Cấu trúc cơ bản | 0.5h |
| 2 | Cập nhật LabelDetector | 0.25h |
| 3 | Analysis Methods | 3-4h |
| 4 | Strategy HIGH | 0.5h |
| 5 | Strategy MEDIUM | 2-3h |
| 6 | Strategy LOW | 2-3h |
| 7 | Testing & Integration | 2h |
| **Tổng** | | **10-13h** |

---

## 🎯 Kết quả mong đợi

✅ **Sau khi hoàn thành:**
- Code cũ vẫn hoạt động bình thường (100% backward compatible)
- Logic phức tạp được tách riêng, dễ maintain
- Dễ dàng thêm strategy mới trong tương lai
- Performance không thay đổi (hoặc tốt hơn)
- Code dễ test và debug hơn

---

**Phiên bản:** 1.0  
**Ngày:** November 12, 2025  
**Trạng thái:** Ready to Implement
