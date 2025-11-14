using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace demo_ocr_label
{
    public class RectangleAroundQR
    {
        public static Bitmap DrawDebugRectangle(Bitmap inputBmp, Point2f[] qrPoints, Point2f[] rectPoints)
        {
            if (inputBmp == null)
                return null;

            using var mat = LabelDetector.BitmapToMat(inputBmp);
            Mat debugMat = mat.Clone();

            // Vẽ hình chữ nhật (màu xanh lá)
            if (rectPoints != null && rectPoints.Length == 4)
            {
                OpenCvSharp.Point[] rectPts = rectPoints.Select(p => new OpenCvSharp.Point((int)p.X, (int)p.Y)).ToArray();
                Cv2.Polylines(debugMat, new[] { rectPts }, true, new Scalar(0, 255, 0), 3);

                // Vẽ các đỉnh và nhãn
                for (int i = 0; i < rectPts.Length; i++)
                {
                    Cv2.Circle(debugMat, rectPts[i], 5, new Scalar(0, 255, 0), -1);
                    Cv2.PutText(debugMat, $"R{i}", new OpenCvSharp.Point(rectPts[i].X + 8, rectPts[i].Y - 8),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);
                }
            }

            // Vẽ QR code (màu đỏ)
            if (qrPoints != null && qrPoints.Length == 4)
            {
                OpenCvSharp.Point[] qrPts = qrPoints.Select(p => new OpenCvSharp.Point((int)p.X, (int)p.Y)).ToArray();
                Cv2.Polylines(debugMat, new[] { qrPts }, true, new Scalar(0, 0, 255), 2);

                // Vẽ các điểm QR và nhãn
                for (int i = 0; i < qrPts.Length; i++)
                {
                    Cv2.Circle(debugMat, qrPts[i], 4, new Scalar(0, 0, 255), -1);
                    Cv2.PutText(debugMat, $"Q{i}", new OpenCvSharp.Point(qrPts[i].X + 8, qrPts[i].Y + 8),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 255), 1);
                }
            }

            return LabelDetector.MatToBitmap(debugMat);
        }

        /// <summary>
        /// Tính 4 góc hình chữ nhật dựa trên QR đã xoay thẳng với 3 điểm chuẩn.
        /// </summary>
        /// <param name="qrPoints">4 điểm QR code (Q0, Q1, Q2, Q3)</param>
        /// <returns>4 đỉnh hình chữ nhật (R0, R1, R2, R3)</returns>
        public static Point2f[] GetRectangleAroundQR(Point2f[] qrPoints, float offsetX, float offsetY, float widthScale, float heightScale)
        {
            if (qrPoints == null || qrPoints.Length != 4)
                return null;

            // Tính vector hướng của 2 cạnh QR
            Point2f vecHorizontal = qrPoints[1] - qrPoints[0]; // Q0 -> Q1: hướng ngang
            Point2f vecVertical = qrPoints[3] - qrPoints[0];   // Q0 -> Q3: hướng dọc
                                                               // Tính độ dài cạnh QR
            float qrSideLength = (float)Math.Sqrt(vecHorizontal.X * vecHorizontal.X + vecHorizontal.Y * vecHorizontal.Y);

            // Chuẩn hóa vector thành vector đơn vị
            float lenH = (float)Math.Sqrt(vecHorizontal.X * vecHorizontal.X + vecHorizontal.Y * vecHorizontal.Y);
            float lenV = (float)Math.Sqrt(vecVertical.X * vecVertical.X + vecVertical.Y * vecVertical.Y);
            Point2f unitH = lenH > 0 ? new Point2f(vecHorizontal.X / lenH, vecHorizontal.Y / lenH) : new Point2f(1, 0);
            Point2f unitV = lenV > 0 ? new Point2f(vecVertical.X / lenV, vecVertical.Y / lenV) : new Point2f(0, 1);

            // R0: Từ Q0, đi lên (ngược hướng Q0->Q3) 1qr, rồi dịch sang trái 2.5qr
            Point2f R0 = qrPoints[0] - unitV * (1.2f * qrSideLength) - unitH * (3.8f * qrSideLength);

            // R1: Từ Q0, đi lên (ngược hướng Q0->Q3) 1qr, rồi dịch sang phải 0.5qr
            Point2f R1 = qrPoints[0] - unitV * (1.2f * qrSideLength) + unitH * (1.4f * qrSideLength);

            // R2: Từ Q0, đi xuống (cùng hướng Q0->Q3) 2qr, rồi dịch sang phải 1qr
            Point2f R2 = qrPoints[0] + unitV * (2.4f * qrSideLength) + unitH * (1.4f * qrSideLength);

            // R3: Từ R0, đi xuống 2qr, rồi dịch sang trái 2.5qr    
            Point2f R3 = qrPoints[0] + unitV * (2.4f * qrSideLength) - unitH * (3.8f * qrSideLength);

            Point2f[] rectPoints = new Point2f[4];
            rectPoints[0] = R0;
            rectPoints[1] = R1;
            rectPoints[2] = R2;
            rectPoints[3] = R3;

            return rectPoints;
        }
    }
}
