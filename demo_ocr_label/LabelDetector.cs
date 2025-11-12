using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Diagnostics;
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
        public static (RotatedRect? rect, OpenCvSharp.Point[]? box, string? qrText, Point2f[]? qrPoints180, Point2f[]? qrPoints)
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
                    return (null, null, null, null, null);

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
                    return (null, null, null, null, null);

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
                Point2f[] qrPoints180 = null!; // Đổi tên để rõ nghĩa
                Point2f[] qrPoints = null!;
                try
                {
                    using var qr = new QRCodeDetector();
                    using var straight = new Mat();

                    qrText = qr.DetectAndDecode(labelRoi, out qrPoints180, straight);

                    if (qrPoints180 != null)
                    {
                        qrPoints = qrPoints180;
                        for (int i = 0; i < qrPoints.Length; i++)
                        {
                            qrPoints[i].X += bound.X;
                            qrPoints[i].Y += bound.Y;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QR ERROR] {ex.Message}");
                }

                // 8️⃣ Chỉ trả về nếu có QR thật
                if (!string.IsNullOrEmpty(qrText))
                    return (rect, box, qrText, qrPoints180, qrPoints);

                return (null, null, null, null, null);
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
        public Bitmap CropAndAlignLabel(Bitmap roi, RotatedRect rect, OpenCvSharp.Point[] box,
                                Point2f[] qrPoints180, Point2f[] qrPoints)
        {
            try
            {
                if (roi == null)
                    throw new ArgumentNullException(nameof(roi));

                using var src = BitmapToMat(roi);

                // 🔹 1) Kích thước label
                int labelWidth = (int)rect.Size.Width;
                int labelHeight = (int)rect.Size.Height;
                if (labelWidth <= 0 || labelHeight <= 0)
                    return null;

                // 🔹 2) Chuẩn hóa góc xoay
                float angle = rect.Angle;
                if (rect.Size.Width < rect.Size.Height)
                    angle += 90;

                if (angle >= 135 && angle <= 180)
                {
                    angle -= 180;
                    labelWidth = (int)rect.Size.Height;
                    labelHeight = (int)rect.Size.Width;
                }
                if (angle > 90 && angle <= 135)
                {
                    labelWidth = (int)rect.Size.Height;
                    labelHeight = (int)rect.Size.Width;
                }

                float labelAngle = angle;

                // 🔹 3) Tính góc QR Code từ qrPoints180 (dùng để xác định ngược)
                Point2f vec_QR_Top = qrPoints180[1] - qrPoints180[0];
                float qrAngle = (float)(Math.Atan2(vec_QR_Top.Y, vec_QR_Top.X) * (180.0 / Math.PI));

                // 🔹 4) So sánh góc
                float deltaAngle = labelAngle - qrAngle;
                while (deltaAngle <= -180) deltaAngle += 360;
                while (deltaAngle > 180) deltaAngle -= 360;
                bool needs180Flip = Math.Abs(deltaAngle) > 90;

                Debug.WriteLine($"🧭 Label={labelAngle:F1}°, QR={qrAngle:F1}°, Δ={deltaAngle:F1}° → Flip180={needs180Flip}");

                // 🔹 5) Ma trận xoay quanh tâm label
                Mat rotationMatrix = Cv2.GetRotationMatrix2D(rect.Center, labelAngle, 1.0);

                // 🔹 6) Xoay ROI
                Mat rotated = new Mat();
                Cv2.WarpAffine(src, rotated, rotationMatrix, src.Size(),
                    InterpolationFlags.Linear, BorderTypes.Replicate);

                // 🔹 7) Cắt vùng label
                OpenCvSharp.Point2f center = rect.Center;
                int x = (int)(center.X - labelWidth / 2.0f);
                int y = (int)(center.Y - labelHeight / 2.0f);

                x = Math.Max(0, Math.Min(x, rotated.Width - 1));
                y = Math.Max(0, Math.Min(y, rotated.Height - 1));
                labelWidth = Math.Min(labelWidth, rotated.Width - x);
                labelHeight = Math.Min(labelHeight, rotated.Height - y);

                OpenCvSharp.Rect cropRect = new(x, y, labelWidth, labelHeight);
                Mat cropped = new Mat(rotated, cropRect);

                // 🔹 8) Tính lại tọa độ QR trong ảnh đã xoay & cắt (dựa trên qrPoints)
                Point2f[] rotatedQRPoints = new Point2f[qrPoints.Length];

                // --- affine chuẩn ---
                Mat affine33 = Mat.Eye(3, 3, MatType.CV_64F);
                rotationMatrix.CopyTo(affine33[new OpenCvSharp.Rect(0, 0, 3, 2)]);

                Mat cropTranslate = Mat.Eye(3, 3, MatType.CV_64F);
                cropTranslate.At<double>(0, 2) = -x;
                cropTranslate.At<double>(1, 2) = -y;

                Mat finalTransform = cropTranslate * affine33;

                for (int i = 0; i < qrPoints.Length; i++)
                {
                    double px = qrPoints[i].X;
                    double py = qrPoints[i].Y;

                    double X = finalTransform.At<double>(0, 0) * px +
                               finalTransform.At<double>(0, 1) * py +
                               finalTransform.At<double>(0, 2);
                    double Y = finalTransform.At<double>(1, 0) * px +
                               finalTransform.At<double>(1, 1) * py +
                               finalTransform.At<double>(1, 2);

                    rotatedQRPoints[i] = new Point2f((float)X, (float)Y);
                }

                // 🔹 9) Lật 180° nếu cần
                if (needs180Flip)
                {
                    Cv2.Rotate(cropped, cropped, RotateFlags.Rotate180);
                    for (int i = 0; i < rotatedQRPoints.Length; i++)
                    {
                        rotatedQRPoints[i].X = labelWidth - rotatedQRPoints[i].X;
                        rotatedQRPoints[i].Y = labelHeight - rotatedQRPoints[i].Y;
                    }
                    Debug.WriteLine("🔄 Đã xoay lại 180° (dựa trên QR geometry).");
                }

                // 🔹 10) Ảnh phải có 3 kênh
                if (cropped.Channels() == 1)
                    Cv2.CvtColor(cropped, cropped, ColorConversionCodes.GRAY2BGR);

                // 🔹 11) Debug log
                Debug.WriteLine($"Cropped size: {cropped.Width}x{cropped.Height}");
                for (int i = 0; i < rotatedQRPoints.Length; i++)
                    Debug.WriteLine($"   ⮑ QR[{i}] after transform: ({rotatedQRPoints[i].X:F1}, {rotatedQRPoints[i].Y:F1})");

                // 🔹 12) Vẽ QR box
                OpenCvSharp.Point[] qrBox = rotatedQRPoints
                    .Select(p => new OpenCvSharp.Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))
                    .ToArray();

                for (int i = 0; i < qrBox.Length; i++)
                {
                    qrBox[i].X = Math.Max(0, Math.Min(qrBox[i].X, cropped.Width - 1));
                    qrBox[i].Y = Math.Max(0, Math.Min(qrBox[i].Y, cropped.Height - 1));
                }

                Cv2.Polylines(cropped, new[] { qrBox }, true, new Scalar(0, 0, 255), 2);
                for (int i = 0; i < qrBox.Length; i++)
                    Cv2.Circle(cropped, qrBox[i], 4, new Scalar(0, 255, 0), -1);

                // 🔹 13) Trả kết quả
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