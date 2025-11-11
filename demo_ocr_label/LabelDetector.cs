using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace demo_ocr_label
{
    public class LabelDetector
    {
        public LabelDetector()
        {
            // Không cần tham số nữa
        }
        /// <summary>
        /// Detect label similar to the provided Python implementation.
        /// Input: Bitmap (BGR)
        /// Output: (rotatedRect, boxPoints, qrText, qrPoints) or (null, null, null, null) if not found
        /// </summary>
        public static (RotatedRect? rect, OpenCvSharp.Point[]? box, string? qrText, Point2f[]? qrPoints)
            DetectLabelRegion(Bitmap inputBmp, int thresholdValue = 150)
        {
            if (inputBmp == null)
                return (null, null, null, null);

            // Convert Bitmap -> Mat (BGR)
            Mat src;
            using (var ms = new MemoryStream())
            {
                inputBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                src = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
            }

            try
            {
                // 1️⃣ To grayscale
                using var gray = new Mat();
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

                // 2️⃣ Làm mượt ảnh — loại bỏ noise cao tần
                // GaussianBlur giúp làm mềm biên, tránh nhiễu trắng đen lẻ
                Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

                // 3️⃣ Làm nổi bật cạnh (tùy chọn, tăng tương phản nếu label sáng không đều)
                // Uncomment nếu cần tăng độ nét vùng sáng
                // Cv2.Laplacian(gray, gray, MatType.CV_8U, 3);

                // 3 Binary threshold (label trắng nên threshold cao)
                using var binary = new Mat();
                //Debug.WriteLine("Ngưỡng sáng nhận diện label: " + thresholdValue);
                Cv2.Threshold(gray, binary, thresholdValue, 255, ThresholdTypes.Binary);

                // 4 Morphological operations để loại bỏ nhiễu & làm nét vùng label
                using var morph = new Mat();
                Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));

                // Mở (open): xóa điểm nhiễu nhỏ
                Cv2.MorphologyEx(binary, morph, MorphTypes.Open, kernel, iterations: 1);
                // Đóng (close): làm vùng label kín, liền mạch
                Cv2.MorphologyEx(morph, morph, MorphTypes.Close, kernel, iterations: 2);


                // 3️⃣ Find contours (external)
                Cv2.FindContours(binary, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                if (contours == null || contours.Length == 0)
                    return (null, null, null, null);

                // 4️⃣ Chọn contour lớn nhất
                OpenCvSharp.Point[] biggest = null!;
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

                //if (biggest == null || maxArea < 1000)
                 if (biggest == null)
                     return (null, null, null, null);

                // 5️⃣ Lấy MinAreaRect và box
                var rect = Cv2.MinAreaRect(biggest);
                var ptsF = rect.Points();
                var box = ptsF.Select(p => new OpenCvSharp.Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();

                // 6️⃣ Crop vùng label theo bounding box (để kiểm tra QR)
                var bound = Cv2.BoundingRect(biggest);
                bound.X = Math.Max(0, bound.X);
                bound.Y = Math.Max(0, bound.Y);
                bound.Width = Math.Min(src.Width - bound.X, bound.Width);
                bound.Height = Math.Min(src.Height - bound.Y, bound.Height);

                using var labelRoi = new Mat(src, bound);

                // 7️⃣ Dò QR code trong vùng label
                string qrText = "";
                Point2f[] qrPoints = null!; // Đổi tên để rõ nghĩa
                try
                {
                    using var qr = new QRCodeDetector();
                    using var straight = new Mat();

                    qrText = qr.DetectAndDecode(labelRoi, out qrPoints, straight);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QR ERROR] {ex.Message}");
                }

                // 8️⃣ Chỉ trả về nếu có QR thật
                if (!string.IsNullOrEmpty(qrText))
                    return (rect, box, qrText, qrPoints);

                return (null, null, null, null);
            }
            finally
            {
                src.Dispose();
            }
        }


        /// <summary>
        /// Xoay và cắt label theo tọa độ rect trong ROI.
        /// Nhận vào: ROI bitmap, rect, box, qrPoints → trả về ảnh label đã xoay thẳng.
        /// </summary>
        public Bitmap CropAndAlignLabel(Bitmap roi, RotatedRect rect, OpenCvSharp.Point[] box, Point2f[] qrPoints)
        {
            try
            {
                if (roi == null)
                    throw new ArgumentNullException(nameof(roi));

                using var src = BitmapToMat(roi);

                // 🔹 1) Lấy kích thước label (theo chiều dài & rộng của rect)
                int labelWidth = (int)rect.Size.Width;
                int labelHeight = (int)rect.Size.Height;

                if (labelWidth <= 0 || labelHeight <= 0)
                    return null;

                // 🔹 2) Xác định góc xoay (normalize cho đúng hướng)
                float angle = rect.Angle;
                if (rect.Size.Width < rect.Size.Height)
                {
                    angle += 90;
                    //Debug.WriteLine($"Xoay +90, Angel = {angle}");
                }

                if (angle >= 135 && angle <= 180)
                {
                    angle -= 180;
                    labelWidth = (int)rect.Size.Height; 
                    labelHeight = (int)rect.Size.Width;
                    //Debug.WriteLine($"Xoay -180, Angel = {angle}");
                }
                
                if (angle > 90 && angle <= 135)
                {
                    labelWidth = (int)rect.Size.Height;
                    labelHeight = (int)rect.Size.Width;
                }

                //Debug.WriteLine($"angel sau xy ly: {angle}");
                float labelAngle = angle; // Đổi tên để phân biệt với qrAngle

                // ========== CODE MỚI: Tính góc QR Code ==========
                // Vector cạnh trên của QR (từ Top-Left → Top-Right)
                Point2f vec_QR_Top = qrPoints[1] - qrPoints[0];

                // Tính góc "sự thật" từ vector này
                float qrAngle = (float)(Math.Atan2(vec_QR_Top.Y, vec_QR_Top.X) * (180.0 / Math.PI));
                // =================================================

                // ========== CODE MỚI: So sánh góc ==========
                float deltaAngle = labelAngle - qrAngle;

                // Chuẩn hóa delta về [-180, 180]
                while (deltaAngle <= -180) deltaAngle += 360;
                while (deltaAngle > 180) deltaAngle -= 360;

                // Quyết định: Nếu chênh lệch > 90 độ → ngược nhau 180°
                bool needs180Flip = Math.Abs(deltaAngle) > 90;

                Debug.WriteLine($"🧭 Label={labelAngle:F1}°, QR={qrAngle:F1}°, Δ={deltaAngle:F1}° → Flip180={needs180Flip}");
                // ============================================

                // 🔹 3) Ma trận xoay quanh tâm label trong ROI
                Mat rotationMatrix = Cv2.GetRotationMatrix2D(rect.Center, labelAngle, 1.0);

                // 🔹 4) Tạo ảnh xoay có cùng kích thước như ROI
                Mat rotated = new Mat();
                Cv2.WarpAffine(src, rotated, rotationMatrix, src.Size(),
                    InterpolationFlags.Linear, BorderTypes.Replicate);

                // 🔹 5) Cắt đúng vùng label (theo kích thước rect)
                // (chuyển tâm về hệ toạ độ sau xoay)
                OpenCvSharp.Point2f center = rect.Center;
                int x = (int)(center.X - labelWidth / 2.0f);
                int y = (int)(center.Y - labelHeight / 2.0f);

                // Clamp cho an toàn
                x = Math.Max(0, Math.Min(x, rotated.Width - 1));
                y = Math.Max(0, Math.Min(y, rotated.Height - 1));
                labelWidth = Math.Min(labelWidth, rotated.Width - x);
                labelHeight = Math.Min(labelHeight, rotated.Height - y);

                OpenCvSharp.Rect cropRect = new(x, y, labelWidth, labelHeight);
                Mat cropped = new Mat(rotated, cropRect);

                // 🔹 6) Kiểm tra và xoay 180° nếu cần (dựa trên QR geometry)
                if (needs180Flip)
                {
                    Cv2.Rotate(cropped, cropped, RotateFlags.Rotate180);
                    Debug.WriteLine("🔄 Đã xoay lại 180° (dựa trên QR geometry).");
                }

                // 🔹 7) Trả kết quả Bitmap
                return MatToBitmap(cropped);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CropAndAlignLabel ERROR] {ex.Message}");
                return null;
            }
        }

        // === Helper ===
        private static Mat BitmapToMat(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
        }

        private static Bitmap MatToBitmap(Mat mat)
        {
            Cv2.ImEncode(".png", mat, out var buf);
            using var ms = new MemoryStream(buf);
            using var tmp = new Bitmap(ms);
            return new Bitmap(tmp);
        }
    }
}
