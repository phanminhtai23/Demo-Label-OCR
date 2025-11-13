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
        private const double HIGH_THRESHOLD = 0.45;      // Nới rộng: 0.6 → 0.45 (để áo đen vào HIGH)
        private const double MEDIUM_THRESHOLD = 0.25;    // Thu hẹp: 0.3 → 0.25
        private const double EDGE_MAX = 0.1;
        
        // Label expansion ratios
        private const float LABEL_WIDTH_RATIO = 4.0f;    // Giảm: 9.0 → 4.0 (frame không quá dài)
        private const float LABEL_HEIGHT_RATIO = 3.0f;   // Giữ nguyên 3× cho chiều cao

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
        /// - strategyUsed: Tên strategy đã sử dụng thành công (để hiển thị trên UI)
        /// </returns>
        public static (RotatedRect? rect, OpenCvSharp.Point[]? box, string? qrText,
                       Point2f[]? qrPoints180, Point2f[]? qrPoints, string? strategyUsed)
            DetectLabelRegion(Mat src, int thresholdValue = 150)
        {
            if (src == null || src.Empty())
                return (null, null, null, null, null, null);

            try
            {
                // PREPROCESSING
                using var gray = new Mat();
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

                // TẦNG 1: Phân tích độ tương phản
                var analysis = AnalyzeFrame(gray);

                Debug.WriteLine("╔════════════════════════════════════════════════════════════════╗");
                Debug.WriteLine("║           FRAME ANALYSIS - AUTO CONTRAST DETECTION            ║");
                Debug.WriteLine("╠════════════════════════════════════════════════════════════════╣");
                Debug.WriteLine($"║  📊 Final Score:     {analysis.FinalScore,6:F3}                              ║");
                Debug.WriteLine($"║  🎯 Strategy Level:  {analysis.Level,-10}                        ║");
                Debug.WriteLine("╠════════════════════════════════════════════════════════════════╣");
                Debug.WriteLine("║  METRICS BREAKDOWN:                                            ║");
                Debug.WriteLine($"║    • Separation:     {analysis.Separation,6:F3}  (peaks: {analysis.Peak1Position,3}, {analysis.Peak2Position,3})       ║");
                Debug.WriteLine($"║    • Edge Strength:  {analysis.EdgeStrength,6:F3}  ({analysis.EdgePixelCount,5} pixels)         ║");
                Debug.WriteLine($"║    • Contrast Ratio: {analysis.ContrastRatio,6:F3}  (σ={analysis.StdDevIntensity,6:F1})           ║");
                Debug.WriteLine("╚════════════════════════════════════════════════════════════════╝");

                // TẦNG 2: Chọn và thực thi chiến lược
                (RotatedRect?, OpenCvSharp.Point[]?, string?, Point2f[]?, Point2f[]?) result;
                string strategyUsed = "";

                switch (analysis.Level)
                {
                    case ContrastLevel.High:
                        Debug.WriteLine("🟢 Executing Strategy: HIGH CONTRAST");
                        result = DetectWithHighContrast(src, gray, thresholdValue);
                        Debug.WriteLine($"   Result: {(result.Item1 != null ? "✅ SUCCESS" : "❌ FAILED")}");
                        if (result.Item1 != null) strategyUsed = "HIGH";
                        break;

                    case ContrastLevel.Medium:
                        Debug.WriteLine("🟡 Executing Strategy: MEDIUM CONTRAST");
                        result = DetectWithMediumContrast(src, gray);
                        Debug.WriteLine($"   Result: {(result.Item1 != null ? "✅ SUCCESS" : "❌ FAILED")}");
                        
                        if (result.Item1 != null)
                        {
                            strategyUsed = "MEDIUM";
                        }
                        // Fallback to High nếu thất bại
                        else
                        {
                            Debug.WriteLine("⚠️  MEDIUM failed, falling back to HIGH strategy...");
                            result = DetectWithHighContrast(src, gray, thresholdValue);
                            Debug.WriteLine($"   Fallback Result: {(result.Item1 != null ? "✅ SUCCESS" : "❌ FAILED")}");
                            if (result.Item1 != null) strategyUsed = "MEDIUM→HIGH";
                        }
                        break;

                    case ContrastLevel.Low:
                        Debug.WriteLine("🔴 Executing Strategy: LOW CONTRAST (QR-First)");
                        
                        // Áp dụng CLAHE preprocessing để cải thiện contrast cục bộ
                        // Hoạt động tốt với mọi màu áo (đen, trắng, xám...)
                        using (var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new OpenCvSharp.Size(8, 8)))
                        {
                            clahe.Apply(gray, gray);
                            Debug.WriteLine("  → Applied CLAHE preprocessing (adaptive contrast enhancement)");
                        }
                        
                        result = DetectWithLowContrast(src, gray);
                        Debug.WriteLine($"   Result: {(result.Item1 != null ? "✅ SUCCESS" : "❌ FAILED")}");
                        
                        if (result.Item1 != null)
                        {
                            strategyUsed = "LOW";
                        }
                        // Fallback to Medium nếu thất bại
                        else
                        {
                            Debug.WriteLine("⚠️  LOW failed, falling back to MEDIUM strategy...");
                            result = DetectWithMediumContrast(src, gray);
                            Debug.WriteLine($"   Fallback Result: {(result.Item1 != null ? "✅ SUCCESS" : "❌ FAILED")}");
                            
                            if (result.Item1 != null)
                            {
                                strategyUsed = "LOW→MEDIUM";
                            }
                            // Fallback to High nếu vẫn thất bại
                            else
                            {
                                Debug.WriteLine("⚠️  MEDIUM failed, falling back to HIGH strategy...");
                                result = DetectWithHighContrast(src, gray, thresholdValue);
                                Debug.WriteLine($"   Final Fallback Result: {(result.Item1 != null ? "✅ SUCCESS" : "❌ FAILED")}");
                                if (result.Item1 != null) strategyUsed = "LOW→MEDIUM→HIGH";
                            }
                        }
                        break;

                    default:
                        Debug.WriteLine("❌ ERROR: Unknown contrast level");
                        result = (null, null, null, null, null);
                        strategyUsed = "ERROR";
                        break;
                }

                if (result.Item1 != null)
                {
                    Debug.WriteLine($"✅ FINAL RESULT: Label detected | QR: {result.Item3 ?? "N/A"} | Strategy: {strategyUsed}");
                }
                else
                {
                    Debug.WriteLine("❌ FINAL RESULT: Label NOT detected");
                }
                Debug.WriteLine("");

                return (result.Item1, result.Item2, result.Item3, result.Item4, result.Item5, strategyUsed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DetectLabelRegion ERROR] {ex.Message}");
                return (null, null, null, null, null, null);
            }
        }

        #region TẦNG 1: Analysis Methods

        /// <summary>
        /// Phân tích frame để tính final score và xác định contrast level
        /// </summary>
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

        /// <summary>
        /// Phân tích histogram để tìm 2 peaks và tính separation
        /// </summary>
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
            Array.Copy(histData, smoothed, 256);
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

        /// <summary>
        /// Phân tích edges bằng Canny để tính edge strength
        /// </summary>
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

        /// <summary>
        /// Phân tích contrast bằng standard deviation
        /// </summary>
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

        #endregion

        #region TẦNG 2: Strategy Methods

        /// <summary>
        /// Strategy HIGH: Binary Threshold + Morphology (cho áo tối/màu đậm)
        /// </summary>
        private static (RotatedRect?, OpenCvSharp.Point[]?, string?, Point2f[]?, Point2f[]?)
            DetectWithHighContrast(Mat src, Mat gray, int thresholdValue)
        {
            Debug.WriteLine("  → Method: Binary Threshold + Morphology");
            
            // Sử dụng Otsu's method để tự động tính threshold tối ưu
            // Thay vì dùng hardcoded value (180), Otsu tự động thích ứng với mọi màu áo
            using var binary = new Mat();
            double otsuThreshold = Cv2.Threshold(gray, binary, 0, 255, 
                ThresholdTypes.Binary | ThresholdTypes.Otsu);
            
            Debug.WriteLine($"  → Otsu adaptive threshold: {otsuThreshold:F1} (auto-calculated for any background color)");

            using var morph = new Mat();
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.MorphologyEx(binary, morph, MorphTypes.Open, kernel, iterations: 1);
            Cv2.MorphologyEx(morph, morph, MorphTypes.Close, kernel, iterations: 2);

            Cv2.FindContours(morph, out OpenCvSharp.Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
            {
                Debug.WriteLine("  ✗ No contours found");
                return (null, null, null, null, null);
            }

            Debug.WriteLine($"  → Found {contours.Length} contours");

            // Chọn contour lớn nhất
            OpenCvSharp.Point[] biggest = null;
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
            {
                Debug.WriteLine("  ✗ No valid contour found");
                return (null, null, null, null, null);
            }

            Debug.WriteLine($"  → Largest contour area: {maxArea:F0} pixels");

            // MinAreaRect
            var rect = Cv2.MinAreaRect(biggest);
            var box = rect.Points().Select(p =>
                new OpenCvSharp.Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();

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
                Debug.WriteLine($"  ✗ QR detection error: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(qrText))
            {
                Debug.WriteLine($"  ✓ QR detected: {qrText}");
                return (rect, box, qrText, qrPoints180, qrPoints);
            }

            Debug.WriteLine("  ✗ No QR code found in label region");
            return (null, null, null, null, null);
        }

        /// <summary>
        /// Strategy MEDIUM: Canny Edge Detection + Strong Morphology (cho áo màu nhạt)
        /// </summary>
        private static (RotatedRect?, OpenCvSharp.Point[]?, string?, Point2f[]?, Point2f[]?)
            DetectWithMediumContrast(Mat src, Mat gray)
        {
            Debug.WriteLine("  → Method: Canny Edge + Strong Morphology");

            // Giảm threshold để nhạy hơn với edge trên nền tối/sáng
            // threshold1: 30 (cũ: 50), threshold2: 100 (cũ: 150)
            using var edges = new Mat();
            Cv2.Canny(gray, edges, threshold1: 30, threshold2: 100);
            Debug.WriteLine("  → Lower Canny thresholds (30/100) for better edge detection on all backgrounds");

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
            Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel, iterations: 3);
            Cv2.MorphologyEx(edges, edges, MorphTypes.Dilate, kernel, iterations: 1);

            Cv2.FindContours(edges, out OpenCvSharp.Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
            {
                Debug.WriteLine("  ✗ No contours found");
                return (null, null, null, null, null);
            }

            Debug.WriteLine($"  → Found {contours.Length} contours");

            // Lọc và sort contours
            var candidates = new List<(OpenCvSharp.Point[] contour, RotatedRect rect, double area)>();
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

            Debug.WriteLine($"  → {candidates.Count} candidates after filtering (area 5-80%)");

            // Lặp và verify QR
            foreach (var (contour, rect, area) in candidates)
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
                        new OpenCvSharp.Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
                    Debug.WriteLine($"  ✓ QR detected in candidate (area={area:F0}): {qrText}");
                    return (rect, box, qrText, qrPoints180, qrPoints);
                }
            }

            Debug.WriteLine("  ✗ No QR found in any candidate");
            return (null, null, null, null, null);
        }

        /// <summary>
        /// Strategy LOW: QR-First Geometry (cho áo trắng/kem)
        /// Tìm QR trước, sau đó suy luận hình học để tìm nhãn
        /// </summary>
        private static (RotatedRect?, OpenCvSharp.Point[]?, string?, Point2f[]?, Point2f[]?)
            DetectWithLowContrast(Mat src, Mat gray)
        {
            Debug.WriteLine("  → Method: QR-First + Geometry Inference");

            // 1. Enhance contrast trước khi detect QR để tăng robustness
            using var enhanced = new Mat();
            Cv2.EqualizeHist(gray, enhanced);
            Debug.WriteLine("  → Applied histogram equalization for QR detection robustness");

            // 2. Detect QR trên enhanced image
            string qrText = "";
            Point2f[] qrPoints = null;

            try
            {
                using var qr = new QRCodeDetector();
                using var straight = new Mat();
                
                // Thử detect trên enhanced image trước
                qrText = qr.DetectAndDecode(enhanced, out qrPoints, straight);
                
                // Nếu fail, thử lại trên original
                if (string.IsNullOrEmpty(qrText))
                {
                    qrText = qr.DetectAndDecode(src, out qrPoints, straight);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ✗ QR detection failed: {ex.Message}");
                return (null, null, null, null, null);
            }

            if (string.IsNullOrEmpty(qrText) || qrPoints == null || qrPoints.Length < 4)
            {
                Debug.WriteLine("  ✗ No QR code detected");
                return (null, null, null, null, null);
            }

            Debug.WriteLine($"  ✓ QR detected: {qrText}");

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

            double angleRad = Math.Atan2(topVec.Y, topVec.X);
            double angleDeg = angleRad * 180.0 / Math.PI;
            Debug.WriteLine($"  → QR geometry: {qrWidth:F1}×{qrHeight:F1} px, angle={angleDeg:F1}°");

            // 4. Suy luận nhãn với expansion (bao gồm cả background context)
            float labelWidth = qrWidth * LABEL_WIDTH_RATIO;    // Width: 4× QR
            float labelHeight = qrHeight * LABEL_HEIGHT_RATIO; // Height: 3× QR

            Debug.WriteLine($"  → Predicted label: {labelWidth:F1}×{labelHeight:F1} px");
            Debug.WriteLine($"  → Expansion: width={LABEL_WIDTH_RATIO}×QR, height={LABEL_HEIGHT_RATIO}×QR");

            // 5. Tính 4 góc nhãn
            // QR nằm ở góc TRÁI DƯỚI của nhãn → cần mở rộng sang PHẢI và LÊN TRÊN
            var qrTopLeft = p0;
            
            // Label top-left: đi lên trên từ QR top-left
            var labelTopLeft = new Point2f(
                qrTopLeft.X - dirDown.X * (labelHeight - qrHeight),
                qrTopLeft.Y - dirDown.Y * (labelHeight - qrHeight)
            );

            // Label top-right: từ top-left đi sang phải
            var labelTopRight = new Point2f(
                labelTopLeft.X + dirRight.X * labelWidth,
                labelTopLeft.Y + dirRight.Y * labelWidth
            );

            // Label bottom-left: từ top-left đi xuống
            var labelBottomLeft = new Point2f(
                labelTopLeft.X + dirDown.X * labelHeight,
                labelTopLeft.Y + dirDown.Y * labelHeight
            );

            // Label bottom-right: từ bottom-left đi sang phải
            var labelBottomRight = new Point2f(
                labelBottomLeft.X + dirRight.X * labelWidth,
                labelBottomLeft.Y + dirRight.Y * labelWidth
            );

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
                .Select(p => new OpenCvSharp.Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))
                .ToArray();

            Debug.WriteLine($"  ✓ Label constructed: center=({labelCenter.X:F1},{labelCenter.Y:F1}), angle={angle:F1}°");

            // qrPoints180 = null vì không có ROI cục bộ (detect trên toàn ảnh)
            return (labelRect, labelBox, qrText, null, qrPoints);
        }

        #endregion
    }
}
