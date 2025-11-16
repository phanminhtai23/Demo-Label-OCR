using OpenCvSharp; // để dùng Point2f
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace demo_ocr_label
{
    public static class ImageDebugSaver
    {
        // BẬT / TẮT lưu (mặc định bật)
        public static bool Enabled { get; set; } = true;

        // Thư mục gốc mặc định (có thể cấu hình trước khi gọi Save)
        // Ví dụ: ImageDebugSaver.ConfigureRoot("D:\\LabelDebug", disableDateSubFolder:true);
        public static string? RootDirectory { get; private set; } = null;

        // Nếu = true thì KHÔNG tạo subfolder theo ngày (yyyyMMdd)
        public static bool DisableDateSubFolder { get; private set; } = false;

        // Thiết lập thư mục lưu & tuỳ chọn có dùng folder ngày hay không
        public static void ConfigureRoot(string rootPath, bool disableDateSubFolder = false)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                Debug.WriteLine("[ImageDebugSaver] rootPath rỗng, bỏ qua thiết lập.");
                return;
            }
            RootDirectory = rootPath;
            DisableDateSubFolder = disableDateSubFolder;
            Debug.WriteLine($"[ImageDebugSaver] Set RootDirectory = {RootDirectory}, DisableDate = {DisableDateSubFolder}");
        }

        // Thêm / chỉnh sửa vào class ImageDebugSaver

        // Helper tạo tên file với thứ tự bước: 0_,1_,2_,3_,4_,5_,6_
        private static string BuildStepFilePath(int stepIndex, string baseRoot, string? tag = null, string? ext = ".png")
        {
            string timePart = DateTime.Now.ToString("HHmmss_fff"); // giờ phút giây mili
            string safeTag = string.IsNullOrWhiteSpace(tag) ? "" : "_" + Sanitize(tag);
            string fileName = $"{stepIndex}_{timePart}{safeTag}{ext}";
            return Path.Combine(baseRoot, fileName);
        }

        // Lấy thư mục gốc (đã gồm subfolder ngày nếu bật)
        private static string EnsureBaseRoot(string? overrideDir)
        {
            string baseRoot = overrideDir
                              ?? RootDirectory
                              ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugImages");

            if (!DisableDateSubFolder)
            {
                string dayFolder = DateTime.Now.ToString("yyyyMMdd");
                baseRoot = Path.Combine(baseRoot, dayFolder);
            }
            Directory.CreateDirectory(baseRoot);
            return baseRoot;
        }

        // 0: Ảnh full frame trước bước 1
        public static string? SaveStep0RawFrame(Bitmap bmp, string? tag = null, string? overrideDir = null)
        {
            try
            {
                if (!Enabled || bmp == null || bmp.Width == 0) return null;
                string root = EnsureBaseRoot(overrideDir);
                string path = BuildStepFilePath(0, root, tag);
                using var safe = Clone24bpp(bmp);
                safe.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveStep0RawFrame ERROR] {ex.Message}");
                return null;
            }
        }

        // 1: ROI có QR (đã vẽ polygon)
        public static string? SaveStep1FindQr(Bitmap roi, OpenCvSharp.Point2f[]? qrPoints, string? qrText, string? overrideDir = null)
        {
            try
            {
                if (!Enabled || roi == null || roi.Width == 0) return null;
                using var work = (Bitmap)roi.Clone();
                if (qrPoints != null && qrPoints.Length == 4)
                {
                    using var g = Graphics.FromImage(work);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var pts = qrPoints.Select(p => new System.Drawing.PointF(p.X, p.Y)).ToArray();
                    using var pen = new Pen(Color.Lime, 3);
                    g.DrawPolygon(pen, pts);
                    using var cornerPen = new Pen(Color.Red, 2);
                    foreach (var p in pts)
                        g.DrawEllipse(cornerPen, p.X - 3, p.Y - 3, 6, 6);

                    if (!string.IsNullOrWhiteSpace(qrText))
                    {
                        using var font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold);
                        using var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                        using var fg = new SolidBrush(Color.Yellow);
                        var sz = g.MeasureString(qrText, font);
                        float tx = Math.Max(0, pts[0].X);
                        float ty = Math.Max(0, pts[0].Y - sz.Height - 6);
                        g.FillRectangle(bg, tx, ty, sz.Width + 8, sz.Height + 4);
                        g.DrawString(qrText, font, fg, tx + 4, ty + 2);
                    }
                }
                string root = EnsureBaseRoot(overrideDir);
                string path = BuildStepFilePath(1, root, qrText);
                using var safe = Clone24bpp(work);
                safe.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveStep1FindQr ERROR] {ex.Message}");
                return null;
            }
        }

        // 2: Ảnh debug vùng Label (debugBmp1)
        public static string? SaveStep2RectAroundLabel(Bitmap bmp, string? tag = null, string? overrideDir = null)
        {
            try
            {
                if (!Enabled || bmp == null || bmp.Width == 0) return null;
                string root = EnsureBaseRoot(overrideDir);
                string path = BuildStepFilePath(2, root, tag);
                using var safe = Clone24bpp(bmp);
                safe.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveStep2RectAroundLabel ERROR] {ex.Message}");
                return null;
            }
        }

        // 3: Label đã cắt & xoay
        public static string? SaveStep3AlignedLabel(Bitmap aligned, string? overrideDir = null)
        {
            try
            {
                if (!Enabled || aligned == null || aligned.Width == 0) return null;
                string root = EnsureBaseRoot(overrideDir);
                string path = BuildStepFilePath(3, root);
                using var safe = Clone24bpp(aligned);
                safe.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveStep3AlignedLabel ERROR] {ex.Message}");
                return null;
            }
        }

        // 4: Ảnh ghép 2 vùng OCR (mergedCrop)
        public static string? SaveStep4MergedCrop(Bitmap merged, string? overrideDir = null)
        {
            try
            {
                if (!Enabled || merged == null || merged.Width == 0) return null;
                string root = EnsureBaseRoot(overrideDir);
                string path = BuildStepFilePath(4, root);
                using var safe = Clone24bpp(merged);
                safe.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveStep4MergedCrop ERROR] {ex.Message}");
                return null;
            }
        }

        // 5: Ảnh input OCR (nếu muốn lưu thêm)
        public static string? SaveStep5OcrInput(Bitmap merged, double minScore, string? overrideDir = null)
        {
            try
            {
                if (!Enabled || merged == null || merged.Width == 0) return null;
                string root = EnsureBaseRoot(overrideDir);
                string tag = $"score_{minScore:0.00}";
                string path = BuildStepFilePath(5, root, tag);
                using var safe = Clone24bpp(merged);
                safe.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveStep5OcrInput ERROR] {ex.Message}");
                return null;
            }
        }

        // 6: Ảnh nền trắng hiển thị kết quả hậu xử lý (text mỗi dòng)
        public static string? SaveStep6PostProcessText(string donHang, string maAo, string size, string color, string? overrideDir = null)
        {
            try
            {
                if (!Enabled) return null;
                string root = EnsureBaseRoot(overrideDir);

                string[] lines =
                {
            $"donHang: {donHang}",
            $"maAo: {maAo}",
            $"size: {size}",
            $"color: {color}"
        };

                using var font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold);
                int padding = 20;
                int lineSpacing = 6;
                int maxW = 0;
                int totalH = padding;

                using (var tmp = new Bitmap(1, 1))
                using (var gM = Graphics.FromImage(tmp))
                {
                    foreach (var ln in lines)
                    {
                        var sz = gM.MeasureString(ln, font);
                        if (sz.Width > maxW) maxW = (int)Math.Ceiling(sz.Width);
                        totalH += (int)Math.Ceiling(sz.Height) + lineSpacing;
                    }
                }
                totalH += padding;
                int finalW = maxW + padding * 2;

                using var outBmp = new Bitmap(finalW, totalH, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(outBmp))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    int y = padding;
                    using var brush = new SolidBrush(Color.Black);
                    foreach (var ln in lines)
                    {
                        g.DrawString(ln, font, brush, new PointF(padding, y));
                        var sz = g.MeasureString(ln, font);
                        y += (int)Math.Ceiling(sz.Height) + lineSpacing;
                    }
                }

                string path = BuildStepFilePath(6, root);
                outBmp.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveStep6PostProcessText ERROR] {ex.Message}");
                return null;
            }
        }

        // ... bên trong namespace demo_ocr_label, class ImageDebugSaver

        public static string? SaveResultJson(double runMilliseconds, string donHang, string qrText, string maAo, string size, string color)
        {
            try
            {
                if (!Enabled) return null;

                // Nếu truyền rỗng -> dùng cấu hình sẵn của ImageDebugSaver (RootDirectory + tùy DateSubFolder)
                string directory = "D:\\Project\\WinForm\\demo_ocr_label\\debug_jsons";

                // Tên file theo giờ_phút_giây_ngày_tháng_năm
                string fileName = $"{DateTime.Now:HHmmss_ddMMyyyy}.json";
                string fullPath = Path.Combine(directory, fileName);

                var payload = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    runMs = runMilliseconds,
                    donHang,
                    qrText,
                    maAo,
                    size,
                    color
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fullPath, json);
                Debug.WriteLine($"[ImageDebugSaver] Saved JSON: {fullPath}");
                return fullPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageDebugSaver ERROR SaveResultJson] {ex.Message}");
                return null;
            }
        }
        private static Bitmap Clone24bpp(Bitmap src)
        {
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            return src.Clone(rect, PixelFormat.Format24bppRgb);
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}