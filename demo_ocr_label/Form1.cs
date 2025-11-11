using ClosedXML.Excel;
using demo_ocr_label;
using DirectShowLib;
using DocumentFormat.OpenXml.Drawing.Charts;
using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace demo_ocr_label
{
    public partial class Form1 : Form
    {

        // detec realtime
        private System.Windows.Forms.Timer detectTimer;
        private bool isOCRBusy = false;


        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _cameraTask;
        private Bitmap? _currentFrame;

        private OverlayPictureBox cameraBox;

        //private OverlayPictureBox cameraBox;
        private Label statusLabel;


        private PaddleOCREngine? ocr;
        private LabelDetector labelDetector;
        //OCRModelConfig config = new OCRModelConfig();
        //config.det_infer = @"models\ch_PP-OCRv3_det_infer";
        //config.rec_infer = @"models\ch_PP-OCRv3_rec_infer";
        //config.cls_infer = @"models\ch_ppocr_mobile_v2.0_cls_infer";
        //config.keys = @"models\ppocr_keys.txt";

        private List<string> models = new List<string>();
        private List<string> sizes = new List<string>();
        private List<string> colors = new List<string>();

        private int pauseTime = 0; // seconds
        public Form1()
        {
            InitializeComponent();
            
        }



        // handle button click to open/close camera
        private async void button1_Click(object sender, EventArgs e)
        {
            if (comboBoxCameraList.SelectedItem?.ToString() == "Không phát hiện camera nào.")
            {
                MessageBox.Show("Không phát hiện camera nào!", "Cảnh báo",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            button1.Enabled = false;
            int camera_index = comboBoxCameraList.SelectedIndex;
            //Debug.WriteLine($"Selected camera index: {camera_index}");
            try
            {
                if (_capture != null && _capture.IsOpened())
                {
                    statusLabel.Text = "Camera đang đóng...";
                    statusLabel.Visible = true;
                    await StopCameraAsync();
                    comboBoxCameraList.Enabled = true;
                    statusLabel.Visible = false;
                    button1.Text = "Mở Camera";
                    button1.BackColor = Color.LightGreen;
                }
                else
                {
                    statusLabel.Text = "Camera đang mở...";
                    statusLabel.Visible = true;
                    await StartCameraAsync(camera_index);
                    comboBoxCameraList.Enabled = false;
                    statusLabel.Visible = false;
                    button1.Text = "Đóng Camera";
                    button1.BackColor = Color.Red;
                }
            }
            finally
            {
                button1.Enabled = true;
            }
        }


        // load camera
        private void LoadCameraList()
        {
            DsDevice[] captureDevices = DsDevice.GetDevicesOfCat(DirectShowLib.FilterCategory.VideoInputDevice);

            comboBoxCameraList.Items.Clear();

            if (captureDevices.Length == 0)
            {
                comboBoxCameraList.Items.Add("Không phát hiện camera nào.");
                comboBoxCameraList.Enabled = false;
                return;
            }

            Console.WriteLine("Available Cameras:");
            for (int i = 0; i < captureDevices.Length; i++)
            {
                comboBoxCameraList.Items.Add($"{i}. {captureDevices[i].Name}");
                Debug.WriteLine($"Index {i}: {captureDevices[i].Name}");
            }
            comboBoxCameraList.SelectedIndex = 0; // chọn camera đầu
        }
        private void Form1_Load(object sender, EventArgs e)
        {

            LoadExcelData("data.xlsx");
            LoadCameraList();
            InitOCR();

            labelDetector = new LabelDetector();

            pauseTime = (int)numericUpDown1.Value;

            cameraBox = overlayPictureBox1;
            //cameraBox.Dock = DockStyle.Fill;
            cameraBox.BackColor = Color.Black;
            cameraBox.SizeMode = PictureBoxSizeMode.Zoom;

            cameraBox.GuideBox = new Rectangle(
                cameraBox.Width / 4,
                cameraBox.Height / 4,
                cameraBox.Width / 2,
                cameraBox.Height / 2
            );

            statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold),
                Visible = false
            };
            cameraBox.Controls.Add(statusLabel);

            button1.Text = "Mở Camera";
        }


        // đọc dữ liệu từ file Excel
        private void LoadExcelData(string excelFileName)
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, excelFileName);
            Debug.WriteLine("Đường dẫn file Excel: " + filePath);
            // ⚠️ Kiểm tra file tồn tại
            if (!File.Exists(filePath))
            {
                ShowWarningBox("Không tìm thấy file Excel! \rVui lòng đặt file dữ liệu excel tên \"data.xlsx\" nằm cùng thư mục ứng dụng!");
                return;
            }

            try
            {
                using (var workbook = new XLWorkbook(filePath))
                {
                    var worksheet = workbook.Worksheet(1);
                    var range = worksheet.RangeUsed();

                    if (range == null)
                    {
                        ShowWarningBox("File Excel rỗng hoặc không có dữ liệu! Vui lòng điền thông tin size áo và màu áo và file data.xlsx");
                        return;
                    }


                    models.Clear();
                    sizes.Clear();
                    colors.Clear();

                    var rows = range.RowsUsed().Skip(1); // bỏ dòng tiêu đề

                    foreach (var row in rows)
                    {
                        string model = row.Cell(1).GetValue<string>().Trim().ToLower();
                        string size = row.Cell(2).GetValue<string>().Trim().ToLower();
                        string color = row.Cell(3).GetValue<string>().Trim().ToLower();

                        if (!string.IsNullOrWhiteSpace(model))
                        {
                            model = Regex.Replace(model.Trim().ToLower(), @"\s+", ""); // bỏ hết khoảng trắng
                            if (!models.Contains(model))
                                models.Add(model);
                        }

                        if (!string.IsNullOrWhiteSpace(size))
                        {
                            size = Regex.Replace(size.Trim().ToLower(), @"\s+", "");
                            if (!sizes.Contains(size))
                                sizes.Add(size);
                        }

                        if (!string.IsNullOrWhiteSpace(color))
                        {
                            color = Regex.Replace(color.Trim().ToLower(), @"\s+", "");
                            if (!colors.Contains(color))
                                colors.Add(color);
                        }
                    }
                }

                if (sizes.Count == 0 && colors.Count == 0)
                {
                    ShowWarningBox("Không có dữ liệu hợp lệ trong file Excel!");
                    return;
                }

                // ✅ Thành công
                Debug.WriteLine("Đọc dữ liệu thành công từ file excel!");

                Debug.WriteLine("Total of Models: " + string.Join(", ", models));
                Debug.WriteLine("Total of Sizes: " + string.Join(", ", sizes));
                Debug.WriteLine("Total of Colors: " + string.Join(", ", colors));
            }
            catch (Exception ex)
            {
                ShowWarningBox("Lỗi khi đọc file Excel:\n" + ex.Message);
            }
        }

        // Hộp cảnh báo có 2 lựa chọn: Tải lại / Đóng
        private void ShowWarningBox(string message)
        {
            var result = MessageBox.Show(
                message + "\n\nBạn có muốn tải lại ứng dụng để kiểm tra lại không?",
                "⚠️ Cảnh báo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result == DialogResult.Yes)
            {
                // Tải lại ứng dụng
                Application.Restart();
            }
            else
            {
                // Đóng ứng dụng
                Application.Exit();
            }
        }

        private async Task StartCameraAsync(int camIndex = 0)
        {

            //Debug.WriteLine("Cam nhận được: " + camIndex);
            _capture = new VideoCapture(camIndex, VideoCaptureAPIs.MSMF);
            if (!_capture.IsOpened())
            {
                MessageBox.Show("Cannot open camera");
                return;
            }

            _capture.FrameWidth = 1280;
            _capture.FrameHeight = 720;

            _cts = new CancellationTokenSource();
            _cameraTask = Task.Run(() => CaptureLoop(_cts.Token));

            await Task.CompletedTask;
        }

        private async Task StopCameraAsync()
        {
            //detectTimer.Stop();
            _cts?.Cancel();
            await Task.Delay(200);

            _capture?.Release();
            _capture?.Dispose();
            _capture = null;

            cameraBox.Image?.Dispose();
            cameraBox.Image = null;
            _currentFrame?.Dispose();
            _currentFrame = null;
            statusLabel.Visible = false;
        }

        // cách 200ms mới detect một lần => tránh gây giật khung hình
        private void DetectTimer_Tick(object sender, EventArgs e)
        {
            //Stopwatch sw = Stopwatch.StartNew();
            //Debug.WriteLine("detecting");
            ////DetectInsideGuideBox();


            //var roi = GetGuideBoxRoi(_currentFrame, cameraBox.GuideBox, cameraBox.Size);


            //DetectFromGuildBox(roi);
            //sw.Stop();
            //Debug.WriteLine($"OCR time: {sw.ElapsedMilliseconds} ms");
        }

        private async void CaptureLoop(CancellationToken ct)
        {

            //var sw = Stopwatch.StartNew();  
            //sw.Stop();                       // dừng đếm
            //double ms = sw.ElapsedMilliseconds;


            using var frame = new Mat();
            int currentThreshold = 180; // giá trị mặc định

            while (!ct.IsCancellationRequested)
            {
                var detectLabelQrTime = Stopwatch.StartNew();

                //Debug.WriteLine("vô đây");
                var sw = Stopwatch.StartNew();  // bắt đầu đo thời gian


                if (!_capture.Read(frame) || frame.Empty())
                    continue;

                // Giữ bản copy frame gốc để hiển thị
                var bmpFull = MatToBitmap(frame);

                // Lấy ROI (guide box) từ frame gốc
                var roiResult = GetGuideBoxRoi(bmpFull, cameraBox.GuideBox, cameraBox);
                var roi = roiResult.Image;
                var mapped = roiResult.Mapped;
                if (roi == null)
                {
                    // Không có ROI hợp lệ → hiển thị ảnh gốc
                    cameraBox.BeginInvoke(new Action(() =>
                    {
                        var old = cameraBox.Image;
                        cameraBox.Image = bmpFull;
                        old?.Dispose();
                    }));
                    continue;
                }

                //// Tính offset (tọa độ ROI trên full frame)
                //float scaleX = (float)bmpFull.Width / cameraBox.Width;
                //float scaleY = (float)bmpFull.Height / cameraBox.Height;

                //var guideRect = cameraBox.GuideBox;
                //int offsetX = (int)(guideRect.X * scaleX);
                //int offsetY = (int)(guideRect.Y * scaleY);

                try
                {
                    
                    currentThreshold = (int)numericThreshold.Value;
                    // 1️⃣ Detect label trong vùng ROI
                    var (rect, box, qrText, qrPoints) = LabelDetector.DetectLabelRegion(roi, currentThreshold);

                    using var mat = frame.Clone(); // frame gốc để vẽ overlay


                    detectLabelQrTime.Stop();                       // dừng đếm
                    double ms1 = detectLabelQrTime.ElapsedMilliseconds;
                    Debug.WriteLine($"Detect Label + QR time: {ms1:F2} ms");

                    // tìm thấy label
                    if (rect != null && box != null && qrText != null && qrPoints != null)
                    {
                        var CatXoayLabelTime = Stopwatch.StartNew();


                        // 2️⃣ Chuyển tọa độ box trong ROI -> tọa độ full ảnh
                        var fullBox = box.Select(p =>
                            new OpenCvSharp.Point(p.X + mapped.X, p.Y + mapped.Y)
                        ).ToArray();

                        // 3️⃣ Vẽ khung label và tâm trên frame full
                        Cv2.Polylines(mat, new[] { fullBox }, true, Scalar.Lime, 2);
                        //Cv2.Circle(mat,
                        //    new OpenCvSharp.Point(
                        //        (int)(rect.Value.Center.X + mapped.X),
                        //        (int)(rect.Value.Center.Y + mapped.Y)),
                        //    4, Scalar.Red, -1);

                        Cv2.PutText(mat, $"Angle={rect.Value.Angle:F1}",
                            new OpenCvSharp.Point(mapped.X, Math.Max(0, mapped.Y - 10)),
                            HersheyFonts.HersheySimplex, 0.7, Scalar.Yellow, 2);

                        // 1 (Tùy chọn) Vẽ khung ROI (để thấy vùng detect)
                        Cv2.Rectangle(mat,
                            new OpenCvSharp.Point(mapped.X, mapped.Y),
                            new OpenCvSharp.Point(mapped.X + roi.Width, mapped.Y + roi.Height),
                            Scalar.Blue, 2);

                        // 2 Hiển thị frame kết quả
                        var debugBmp = MatToBitmap(mat);
                        cameraBox.BeginInvoke(new Action(() =>
                        {
                            var old = cameraBox.Image;
                            cameraBox.Image = debugBmp;
                            old?.Dispose();
                            //sw.Stop();                       // dừng đếm
                            //double ms = sw.ElapsedMilliseconds;
                            //double fps = (ms > 0) ? 1000.0 / ms : 0;
                            ////Debug.WriteLine($"⏱ Time per frame: {ms:F1} ms  →  FPS: {fps:F1}");
                            ////Debug.WriteLine($"DetectLabelRegion time: {sw.ElapsedMilliseconds} ms");
                            //label6.Text = $"FPS: {fps:F1}";

                        }));

                        var aligned = labelDetector.CropAndAlignLabel(roi, rect.Value, box, qrPoints);

                        CatXoayLabelTime.Stop();                       // dừng đếm
                        double ms2 = CatXoayLabelTime.ElapsedMilliseconds;
                        Debug.WriteLine($"Cắt, xoay Label Time: {ms2:F2} ms");

                        ////3 DEBUG: luôn hiển thị ảnh cắt label
                        //pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // co ảnh cho vừa khung
                        //pictureBox1.Image = aligned;

                        if (aligned != null)
                        //if (false)
                        {
                            //var ocrTime = Stopwatch.StartNew();
                            // 1️⃣ Gọi OCR trên vùng dưới bên trái
                            var (croppedImg, ocrTexts, minScore, text) = RunOcrOnBottomLeftQuarter(ocr, aligned);

                            //ocrTime.Stop(); // dừng đo
                            //double ocrTimeMs = ocrTime.Elapsed.TotalMilliseconds;
                            //Debug.WriteLine($"Extract Text Time: {ocrTimeMs:F2} ms");
                            var HauXuLy = Stopwatch.StartNew();
                            var (maAo, size, color) = HandleOcrTexts(ocrTexts);

                            HauXuLy.Stop();                       // dừng đếm
                            double ms6 = HauXuLy.Elapsed.TotalMilliseconds;
                            Debug.WriteLine($"Hậu xử lý Time: {ms6:F2} ms");


                            // extract được text và text hợp lệ
                            if (maAo != "" && size != "" && color != "")
                            //if (true)
                            {

                                //// 1 (Tùy chọn) Vẽ khung ROI (để thấy vùng detect)
                                //Cv2.Rectangle(mat,
                                //    new OpenCvSharp.Point(mapped.X, mapped.Y),
                                //    new OpenCvSharp.Point(mapped.X + roi.Width, mapped.Y + roi.Height),
                                //    Scalar.Blue, 2);

                                //// 2 Hiển thị frame kết quả
                                //var debugBmp = MatToBitmap(mat);
                                cameraBox.BeginInvoke(new Action(() =>
                                {
                                    //var old = cameraBox.Image;
                                    //cameraBox.Image = debugBmp;
                                    //old?.Dispose();
                                    sw.Stop();                       // dừng đếm
                                    double ms = sw.ElapsedMilliseconds;
                                    double fps = (ms > 0) ? 1000.0 / ms : 0;
                                    //Debug.WriteLine($"⏱ Time per frame: {ms:F1} ms  →  FPS: {fps:F1}");
                                    Debug.WriteLine($"Total of full pipeline time extract sucessfully (detect-cắt Label, xoay180, OCR, hậu xử lý): {sw.ElapsedMilliseconds} ms");
                                    label6.Text = $"FPS: {fps:F1}";

                                }));

                                // 3 hiển thị ảnh debug label đã xoay và cắt
                                pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // co ảnh cho vừa khung
                                pictureBox1.Image = aligned;

                                cameraBox.BeginInvoke(new Action(() =>
                                {
                                    cameraBox.IsObjectDetected = true;   // ✅ đổi sang khung xanh
                                    cameraBox.Invalidate();
                                    label7.Text = $"Accuracy: {minScore * 100:F2}%";
                                    label7.Visible = true; // hiện accuracy
                                    label8.Visible = true; // hiện thông báo đã detect
                                }));

                                // 4 hiển thị ảnh cắt 1/4
                                if (croppedImg != null)
                                {
                                    pictureBox2.BeginInvoke(new Action(() =>
                                    {
                                        pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
                                        pictureBox2.Image?.Dispose();
                                        if (croppedImg != null)
                                        {
                                            pictureBox2.BeginInvoke(new Action(() =>
                                            {
                                                pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
                                                pictureBox2.Image?.Dispose();
                                                pictureBox2.Image = croppedImg; // không cần clone nữa
                                            }));
                                        }
                                    }));
                                }

                                //string combined = string.Join(" | ", ocrTexts.Select(tb => $"{tb.Text}: {tb.Score:F2}"));

                                // 5 Hiển thị text lên textbox
                                textBox1.BeginInvoke(new Action(() =>
                                {
                                    textBox1.Multiline = true;
                                    textBox1.AutoSize = false;
                                    textBox1.ScrollBars = ScrollBars.Vertical;

                                    textBox1.Text =
                                        $"QR: \"{qrText}\"\r\n" +
                                        $"Mã áo: \"{maAo}\"\r\n" +
                                        $"Size áo: \"{size}\"\r\n" +
                                        $"Màu áo: \"{color}\"";

                                    //textBox1.Text = text;
                                }));
                                await Task.Delay(pauseTime * 1000); // dừng pauseTime giây trước khi detect tiếp
                                //MessageBox.Show("⏸️ Đang tạm dừng...\nNhấn OK để tiếp tục", "Tạm dừng test");

                            }
                            else // text không đúng định dạng
                            {
                                Debug.WriteLine("text result k đúng định dạng");

                            }
                        }
                    }
                    else // không detec thấy label
                    {
                        cameraBox.BeginInvoke(new Action(() =>
                        {
                            label7.Visible = false;
                            label8.Visible = false;
                            cameraBox.IsObjectDetected = false;  // 🔴 trở lại khung đỏ
                            cameraBox.Invalidate();

                        }));


                        //// 1 (Tùy chọn) Vẽ khung ROI (để thấy vùng detect)
                        //Cv2.Rectangle(mat,
                        //    new OpenCvSharp.Point(mapped.X, mapped.Y),
                        //    new OpenCvSharp.Point(mapped.X + roi.Width, mapped.Y + roi.Height),
                        //    Scalar.Blue, 2);

                        // 2 Hiển thị frame kết quả
                        var debugBmp = MatToBitmap(mat);
                        cameraBox.BeginInvoke(new Action(() =>
                        {
                            var old = cameraBox.Image;
                            cameraBox.Image = debugBmp;
                            old?.Dispose();
                            sw.Stop();                       // dừng đếm
                            double ms = sw.ElapsedMilliseconds;
                            double fps = (ms > 0) ? 1000.0 / ms : 0;
                            //Debug.WriteLine($"⏱ Time per frame: {ms:F1} ms  →  FPS: {fps:F1}");
                            //Debug.WriteLine($"full pipeline không trích xuấ time: {sw.ElapsedMilliseconds} ms");
                            label6.Text = $"FPS: {fps:F1}";

                        }));


                        // 3 hiển thị ảnh debug label đã xoay và cắt
                        pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // co ảnh cho vừa khung
                        pictureBox1.Image = null;


                        // 4 hiển thị ảnh cắt 1/4
                        pictureBox2.BeginInvoke(new Action(() =>
                        {
                            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
                            pictureBox2.Image?.Dispose();
                            pictureBox2.Image = null; // không cần clone nữa
                        }));

                        // 5 Hiển thị text lên textbox
                        textBox1.BeginInvoke(new Action(() =>
                        {
                            textBox1.Multiline = true;
                            textBox1.AutoSize = false;
                            textBox1.ScrollBars = ScrollBars.Vertical;

                            textBox1.Text = "";
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Detect ERROR] {ex.Message}");
                }

                // Free bitmap ROI tránh leak bộ nhớ
                roi.Dispose();

                Thread.Sleep(1); // tránh CPU 100%
            }
        }

        // sử dụng Paddle OCR ở 1/4 góc dưới bên trái
        public (Bitmap cropped, List<string> texts, float minScore, string DebugText) RunOcrOnBottomLeftQuarter(PaddleOCREngine ocr, Bitmap aligned, PictureBox pictureBox2 = null)
        {
            Bitmap cropped = null;
            var texts = new List<string>();
            var DebugText = "";


            try
            {
                if (ocr == null || aligned == null)
                    return (null, texts, -999, DebugText);

                var Crop1_4LlabelTime = Stopwatch.StartNew();

                int width = aligned.Width;
                int height = aligned.Height;

                // 1️⃣ Xác định vùng ROI (1/2 chiều rộng, 1/4 chiều cao ở dưới)
                Rectangle roiRect = new Rectangle(
                    0,
                    (int)(height * 0.6), 
                    (int)(width * 0.8), // chiều rộng lấy tính từ goc dưới bên trái
                    (int)(height * 0.4) // chiều cao lấy tính từ goc dưới bên trái
                );

                // Giới hạn trong ảnh
                roiRect.Intersect(new Rectangle(0, 0, width, height));
                if (roiRect.Width <= 0 || roiRect.Height <= 0)
                {
                    Debug.WriteLine("[OCR] ROI invalid — skip frame");
                    return (null, texts, -999, DebugText);
                }

                // 2️⃣ Cắt vùng ROI
                cropped = aligned.Clone(roiRect, aligned.PixelFormat);

                Crop1_4LlabelTime.Stop();                       // dừng đếm
                double ms4 = Crop1_4LlabelTime.ElapsedMilliseconds;
                Debug.WriteLine($"Thời gian cắt 1/4 Label: {ms4:F2} ms");


                var PaddleOCRTime = Stopwatch.StartNew();

                // 3 Gọi OCR (thread-safe)
                OCRResult result;
                lock (ocr)
                {
                    result = ocr.DetectText(cropped);
                }




                //Debug.WriteLine("result_OCR", result);
                // 4 Trích xuất text
                float minScore = 999;
                if (result?.TextBlocks?.Count > 0)
                {
                    texts = result.TextBlocks
                        .Where(tb => !string.IsNullOrWhiteSpace(tb.Text))
                        .Select(tb => tb.Text.Trim())
                        .ToList();

                    //Debug.WriteLine("[OCR RESULT]");
                    foreach (var tb in result.TextBlocks)
                    {
                        if (tb.Score < minScore)
                        {
                            minScore = tb.Score;
                        }
                        var txt = tb.Text?.Trim() ?? "";
                        //Debug.WriteLine($" → {txt,-20} | Score: {tb.Score * 100:F2}%");
                        DebugText += $"{txt} | Score: {tb.Score * 100:F2}%\r\n";
                    }
                }


                PaddleOCRTime.Stop();                       // dừng đếm
                double ms5 = PaddleOCRTime.ElapsedMilliseconds;
                Debug.WriteLine($"Regconize Text Time: {ms5:F2} ms");

                //cropped.Dispose(); // giải phóng vùng tạm sau khi clone cho UI
                return (cropped, texts, minScore, DebugText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR ERROR] {ex.Message}");
                return (null, texts, -999, DebugText);
            }
        }

        // xử lý đầu ra của OCR
        private (string MaAo, string Size, string Other) HandleOcrTexts(List<string> ocrTexts)
        {
            if (ocrTexts == null || ocrTexts.Count < 3)
            {
                Debug.WriteLine($"[HandleOcrTexts] Warning: OCR texts count is lower than 3, ocrTexts.Count = {ocrTexts.Count}");
                return ("", "", "");
            }

            string maAo = "";
            string size = "";
            string color = "";

            //foreach (var text in ocrTexts)
            //{
            //    var t = (string)text.Trim();  // ✅ chuẩn hóa trước khi kiểm tra

            //    var t_lower = t.ToLower();
            //    Debug.WriteLine($"trước Trim {text}, sau Trim: {t_lower}");

            //    // 1️⃣ Kiểm tra mã áo (thường là số hoặc có chữ ngắn, như 3000, 500A, A12)
            //    if (string.IsNullOrEmpty(maAo) &&
            //        models.Contains(t_lower))
            //    {
            //        maAo = t;
            //    }

            //    // 2️⃣ Kiểm tra size
            //    else if (string.IsNullOrEmpty(size) &&
            //        sizes.Contains(t_lower))
            //    {
            //        size = t;
            //    }

            //    // 3️⃣ Còn lại gom vào "other"
            //    else if (string.IsNullOrEmpty(color) &&
            //        colors.Contains(t_lower))
            //    {
            //        color = t;
            //    }
            //}
            //var t = (string)text.Trim();  // ✅ chuẩn hóa trước khi kiểm tra

            //var t_lower = t.ToLower();
            //Debug.WriteLine($"trước Trim {text}, sau Trim: {t_lower}");

            // 1️⃣ Kiểm tra mã áo (thường là số hoặc có chữ ngắn, như 3000, 500A, A12)
            if (string.IsNullOrEmpty(maAo) &&
                models.Contains(ocrTexts[0].Trim().ToLower()))
            {
                maAo = ocrTexts[0].Trim();
            }

            // 2️⃣ Kiểm tra size
            if (string.IsNullOrEmpty(size) &&
                sizes.Contains(ocrTexts[1].Trim().ToLower()))
            {
                size = ocrTexts[1].Trim();
            }

            //  > 3️⃣ Còn lại gom vào "other"
            if (ocrTexts.Count() > 3)
            {
                var color_trim = "";
                for (int i = 2; i < ocrTexts.Count(); i++)
                {
                    color_trim += ocrTexts[i].Trim();
                }
                if (string.IsNullOrEmpty(color) &&
                    colors.Contains(color_trim.ToLower()))
                {
                    color = color_trim;
                }

            }
            else // =3
            {
                if (string.IsNullOrEmpty(color) &&
                      colors.Contains(ocrTexts[2].Trim().ToLower()))
                {
                    color = ocrTexts[2].Trim();
                }
            }

                return (maAo, size, color);
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            base.OnFormClosing(e);
        }

        private Bitmap MatToBitmap(Mat mat)
        {
            int w = mat.Width;
            int h = mat.Height;
            int channels = mat.Channels(); // 3 = BGR

            Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            var rect = new Rectangle(0, 0, w, h);
            var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);

            int stride = bmpData.Stride;
            int rowLength = w * channels;

            byte[] buffer = new byte[rowLength];

            for (int y = 0; y < h; y++)
            {
                // src: pointer vào dữ liệu Mat
                IntPtr src = mat.Data + y * (int)mat.Step();
                // copy từ Mat vào byte[]
                System.Runtime.InteropServices.Marshal.Copy(src, buffer, 0, rowLength);

                // dst: con trỏ đến bitmap row
                IntPtr dst = bmpData.Scan0 + y * stride;
                // copy byte[] vào Bitmap
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, dst, rowLength);
            }

            bmp.UnlockBits(bmpData);
            return bmp;
        }

        private static Mat BitmapToMat(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
        }

        private void overlayPictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void InitOCR()
        {

            OCRModelConfig config = null;   // model tích hợp
            OCRParameter param = new OCRParameter
            {

                cpu_math_library_num_threads = 6,
                enable_mkldnn = true,
                det = true,
                cls = false,
                //recf = true
                det_db_score_mode = true

            };
            //param.ort = false;
            ocr = new PaddleOCREngine(config, param);
        }

        // lấy vị trí guild box
        private RoiResult GetGuideBoxRoi(Bitmap frame, Rectangle guideBox, OverlayPictureBox cameraBox)
        {
            RoiResult result = new RoiResult();

            if (frame == null || frame.Width == 0 || frame.Height == 0)
                return result;
            if (cameraBox == null || cameraBox.ClientSize.Width == 0 || cameraBox.ClientSize.Height == 0)
                return result;

            float imgW = frame.Width;
            float imgH = frame.Height;
            float boxW = cameraBox.ClientSize.Width;
            float boxH = cameraBox.ClientSize.Height;

            float scale = Math.Min(boxW / imgW, boxH / imgH);
            float drawW = imgW * scale;
            float drawH = imgH * scale;
            float offsetX = (boxW - drawW) / 2f;
            float offsetY = (boxH - drawH) / 2f;

            float x = (guideBox.X - offsetX) / scale;
            float y = (guideBox.Y - offsetY) / scale;
            float w = guideBox.Width / scale;
            float h = guideBox.Height / scale;

            x = Math.Max(0, x);
            y = Math.Max(0, y);
            w = Math.Min(imgW - x, w);
            h = Math.Min(imgH - y, h);

            var mapped = new Rectangle((int)x, (int)y, (int)w, (int)h);
            mapped.Intersect(new Rectangle(0, 0, (int)imgW, (int)imgH));

            if (mapped.Width > 0 && mapped.Height > 0)
                result.Image = frame.Clone(mapped, frame.PixelFormat);

            result.Mapped = mapped;
            result.Scale = scale;
            result.OffsetX = offsetX;
            result.OffsetY = offsetY;

            return result;
        }

        //private void DetectInsideGuideBox()
        //{
        //    if (isOCRBusy) return;

        //    if (_currentFrame == null || ocr == null)
        //        return;

        //    // Nếu cameraBox chưa layout xong
        //    if (cameraBox.Width <= 0 || cameraBox.Height <= 0)
        //        return;

        //    isOCRBusy = true;

        //    var roi = GetGuideBoxRoi(_currentFrame, cameraBox.GuideBox, cameraBox.Size);


        //    var res = ocr.DetectText(roi);

        //    cameraBox.IsObjectDetected = res.TextBlocks.Count > 0;
        //    cameraBox.Invalidate();

        //    textBox1.Text = string.Join(Environment.NewLine,
        //        res.TextBlocks.Select(tb => tb.Text));

        //    isOCRBusy = false;
        //}


        // show anh private void ShowBitmap(Bitmap bmp)
        private void ShowBitmap(Bitmap bmp)
        {
            Form f = new Form();
            f.Text = "ROI";
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Size = new System.Drawing.Size(bmp.Width + 20, bmp.Height + 40);

            PictureBox pb = new PictureBox();
            pb.Dock = DockStyle.Fill;
            pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.Image = (Bitmap)bmp.Clone();

            f.Controls.Add(pb);
            f.Show();
        }


        private void button2_Click(object sender, EventArgs e)
        {
            //DetectInsideGuideBox();
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void numericUpDown1_ValueChanged_1(object sender, EventArgs e)
        {

        }
    }
}

