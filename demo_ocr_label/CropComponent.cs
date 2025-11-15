using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Drawing;

namespace demo_ocr_label
{
    public static class CropComponent
    {
        // Cắt 2 vùng (góc dưới bên trái + vùng phía trên QR) rồi ghép ảnh lại (KHÔNG OCR)
        public static Bitmap CropAndMergeBottomLeftAndAboveQr(Bitmap aligned, OpenCvSharp.Point[] qrBox)
        {
            Bitmap bottomLeftCrop = null;
            Bitmap aboveQrCrop = null;
            Bitmap mergedCrop = null;

            try
            {
                if (aligned == null || qrBox == null || qrBox.Length != 4)
                    return null;

                int width = aligned.Width;
                int height = aligned.Height;

                // Clone để tránh lỗi GDI+
                Bitmap safeAligned = aligned.Clone(
                    new Rectangle(0, 0, aligned.Width, aligned.Height),
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                using var mat = LabelDetector.BitmapToMat(safeAligned);

                // === 1) Vùng góc dưới bên trái ===
                Rectangle roiBottomLeft = new Rectangle(
                    0,
                    (int)(height * (1 - utils.fileConfig.bottomLeftComponent.height)),
                    (int)(width * utils.fileConfig.bottomLeftComponent.width),
                    (int)(height * utils.fileConfig.bottomLeftComponent.height)
                );
                roiBottomLeft.Intersect(new Rectangle(0, 0, width, height));
                if (roiBottomLeft.Width <= 0 || roiBottomLeft.Height <= 0)
                {
                    Debug.WriteLine("[⚠️] ROI BottomLeft invalid: " + roiBottomLeft);
                    safeAligned.Dispose();
                    return null;
                }
                bottomLeftCrop = safeAligned.Clone(roiBottomLeft, safeAligned.PixelFormat);

                // === 2) Vùng phía trên cạnh QR ===
                var p0 = qrBox[0]; // top-left
                var p1 = qrBox[1]; // top-right
                var p2 = qrBox[2]; // bottom-right

                var topVec = new OpenCvSharp.Point2f(p1.X - p0.X, p1.Y - p0.Y);
                var rightVec = new OpenCvSharp.Point2f(p2.X - p1.X, p2.Y - p1.Y);
                float qrWidth = (float)Math.Sqrt(topVec.X * topVec.X + topVec.Y * topVec.Y);
                float qrHeight = (float)Math.Sqrt(rightVec.X * rightVec.X + rightVec.Y * rightVec.Y);

                var normal = new OpenCvSharp.Point2f(topVec.Y, -topVec.X);
                float len = (float)Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
                if (len != 0) { normal.X /= len; normal.Y /= len; }

                float offset = (float)(utils.fileConfig.aboveQrComponent.doiTamLenTren * qrWidth);
                float widthAbove = (float)(utils.fileConfig.aboveQrComponent.width * qrWidth);
                float heightAbove = (float)(utils.fileConfig.aboveQrComponent.height * qrHeight);

                var dir = new OpenCvSharp.Point2f(topVec.X / qrWidth, topVec.Y / qrWidth);
                float shiftDist = utils.fileConfig.aboveQrComponent.doiTamSangPhai * qrWidth;

                var baseTopRight = new OpenCvSharp.Point2f(
                    p1.X + normal.X * offset + dir.X * shiftDist,
                    p1.Y + normal.Y * offset + dir.Y * shiftDist);

                var rectTopRight = baseTopRight;
                var rectTopLeft = new OpenCvSharp.Point2f(
                    rectTopRight.X - dir.X * widthAbove,
                    rectTopRight.Y - dir.Y * widthAbove);

                var rectBottomRight = new OpenCvSharp.Point2f(
                    rectTopRight.X + normal.X * heightAbove,
                    rectTopRight.Y + normal.Y * heightAbove);

                var rectBottomLeft = new OpenCvSharp.Point2f(
                    rectTopLeft.X + normal.X * heightAbove,
                    rectTopLeft.Y + normal.Y * heightAbove);

                OpenCvSharp.Point2f[] srcQuad =
                {
                    rectTopLeft,
                    rectTopRight,
                    rectBottomRight,
                    rectBottomLeft
                };
                OpenCvSharp.Point2f[] dstQuad =
                {
                    new(0, heightAbove),
                    new(widthAbove, heightAbove),
                    new(widthAbove, 0),
                    new(0, 0)
                };

                var M = Cv2.GetPerspectiveTransform(srcQuad, dstQuad);
                using var croppedTopRight = new Mat();
                Cv2.WarpPerspective(mat, croppedTopRight, M, new OpenCvSharp.Size(widthAbove, heightAbove),
                    InterpolationFlags.Linear, BorderTypes.Replicate);

                // Convert sang Bitmap
                aboveQrCrop = LabelDetector.MatToBitmap(croppedTopRight);

                // === 3) Ghép ảnh ===
                int mergedWidth = Math.Max(aboveQrCrop.Width, bottomLeftCrop.Width);
                int mergedHeight = aboveQrCrop.Height + bottomLeftCrop.Height;

                mergedCrop = new Bitmap(mergedWidth, mergedHeight);
                using (Graphics g = Graphics.FromImage(mergedCrop))
                {
                    g.Clear(System.Drawing.Color.Black);
                    using (Bitmap topClone = (Bitmap)aboveQrCrop.Clone())
                    using (Bitmap bottomClone = (Bitmap)bottomLeftCrop.Clone())
                    {
                        g.DrawImage(topClone, (mergedWidth - topClone.Width) / 2, 0);
                        g.DrawImage(bottomClone, (mergedWidth - bottomClone.Width) / 2, topClone.Height);
                    }
                }

                // Cleanup tạm
                aboveQrCrop?.Dispose();
                bottomLeftCrop?.Dispose();
                safeAligned.Dispose();

                return mergedCrop;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[❌ CropComponent ERROR] {ex.Message}");
                try { aboveQrCrop?.Dispose(); bottomLeftCrop?.Dispose(); mergedCrop?.Dispose(); } catch { }
                return null;
            }
        }
    }
}