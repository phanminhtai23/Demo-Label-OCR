using ClosedXML.Excel;
using demo_ocr_label;
using DirectShowLib;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Spreadsheet;
using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Text.Json;
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
        private bool debugMode = false;
        private bool showTime = false;
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
                    button1.BackColor = System.Drawing.Color.LightGreen;
                }
                else
                {
                    statusLabel.Text = "Camera đang mở...";
                    statusLabel.Visible = true;
                    await StartCameraAsync(camera_index);
                    comboBoxCameraList.Enabled = false;
                    statusLabel.Visible = false;
                    button1.Text = "Đóng Camera";
                    button1.BackColor = System.Drawing.Color.Red;
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

            // Chỗ lưu ảnh debug
            ImageDebugSaver.ConfigureRoot("D:\\Project\\WinForm\\demo_ocr_label\\debug_imgs", disableDateSubFolder: false);

            LoadExcelData("data.xlsx");
            //until.LoadConfigFile("config.json");

            if (utils.fileConfig != null)
            {
                string json = JsonSerializer.Serialize(utils.fileConfig, new JsonSerializerOptions
                {
                    WriteIndented = true // in gọn 1 dòng
                });
                Debug.WriteLine($"[CONFIG] {json}");
            }
            debugMode = utils.fileConfig.systemArivable.debugMode;
            showTime = utils.fileConfig.systemArivable.showTime;


            LoadCameraList();
            InitOCR();

            labelDetector = new LabelDetector();

            pauseTime = (int)numericUpDown1.Value;
            cameraBox = overlayPictureBox1;
            //cameraBox.Dock = DockStyle.Fill;
            cameraBox.BackColor = System.Drawing.Color.Black;
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
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.Transparent,
                Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 14, FontStyle.Bold),
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

        // Logic chính, vòng lặp từng frame ảnh
        private async void CaptureLoop(CancellationToken ct)
        {

            int currentThreshold = 180; // giá trị mặc định
            using var frame = new Mat();

            while (!ct.IsCancellationRequested)
            {


                // Đo toàn bộ thời gian xử lý 1 frame
                var sw = Stopwatch.StartNew();

                if (!_capture.Read(frame) || frame.Empty())
                    continue;

                // Giữ bản copy frame gốc để hiển thị
                var bmpFull = MatToBitmap(frame);

                // DEBUG: Lưu ảnh full frame
                if (debugMode) ImageDebugSaver.SaveStep0RawFrame(bmpFull);

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
                        // CLONE để tránh GDI+ "Object is currently in use elsewhere"
                        cameraBox.Image = (Bitmap)bmpFull.Clone();
                        old?.Dispose();
                    }));
                    // Giải phóng bản gốc sau khi đã clone cho UI
                    bmpFull.Dispose();
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
                    
                    using var mat = frame.Clone();

                    currentThreshold = (int)numericThreshold.Value;



                    // Đo thời gian detect QR
                    var thoiGianTimQR = Stopwatch.StartNew();

                    // 1. Kiểm tra QR có trong Guild Box không?
                    var (qrPoints, qrText) = LabelDetector.DetectQRCode(roi);
 


                    if (showTime)
                    {
                        thoiGianTimQR.Stop();                       // dừng đếm
                        double ms1 = thoiGianTimQR.Elapsed.TotalMilliseconds;
                        Debug.WriteLine($"1. Thời gian tìm QR trong Guild Box: {ms1:F2} ms");
                    }

                    // Trong frame ảnh có mã QR
                    if (qrPoints != null)
                    {
                        // DEBUG: lưu ảnh roi có QR
                        if (debugMode) ImageDebugSaver.SaveStep1FindQr(roi, qrPoints, qrText);

                        // Đo thời gian xác định tọa độ HCN quanh Label
                        var thoiGianXacDinhToaDoLabel = Stopwatch.StartNew();

                        // 2. Xác định tọa độ HCN quanh Label
                        var (rect, rectPoints, debugBmp1, rectInGuildlBox) = LabelDetector.DetectLabelRegionWithQrCode(roi, qrPoints);



                        if (showTime)
                        {
                            thoiGianXacDinhToaDoLabel.Stop();                       // dừng đếm
                            double ms2 = thoiGianXacDinhToaDoLabel.Elapsed.TotalMilliseconds;
                            Debug.WriteLine($"2. Thời gian xác định tọa độ HCN quanh Label: {ms2:F2} ms");
                        }

                        // Nếu HCN nằm trong Guild Box
                        if (rectPoints != null && rectInGuildlBox) 
                        {
                            // DEBUG: Label nằm trong Guild Box
                            if (debugMode) ImageDebugSaver.SaveStep2RectAroundLabel(debugBmp1);
                            // Chuyển tọa độ box trong ROI -> tọa độ full ảnh
                            var qrBox = qrPoints.Select(p =>
                                new OpenCvSharp.Point(p.X + mapped.X, p.Y + mapped.Y)
                            ).ToArray();

                            // chuyển tọa độ hình chữ nhật quanh Label -> tọa độ full ảnh
                            var qrRectangle = rectPoints.Select(p =>
                                new OpenCvSharp.Point(p.X + mapped.X, p.Y + mapped.Y)
                            ).ToArray();

                            // Vẽ khung label và tâm trên frame full
                            Cv2.Polylines(mat, new[] { qrBox }, true, Scalar.Lime, 2);

                            // Vẽ khung HCN lên frame full
                            Cv2.Polylines(mat, new[] { qrRectangle }, true, Scalar.Lime, 2);

                            var debugBmp = MatToBitmap(mat);


                            // Hiển thị ảnh ra liền
                            cameraBox.BeginInvoke(new Action(() =>
                            {
                                var old = cameraBox.Image;
                                cameraBox.Image = (Bitmap)debugBmp.Clone();
                                old?.Dispose();

                            }));


                            // DEBUG: show ảnh debug HCN trong Guild Box
                            //ShowBitmapCoDung(debugBmp1);


                            // Đo thời gian cắt và xoay thẳng Label
                            var thoiGianCatXoayLabel = Stopwatch.StartNew();

                            // 3. Cắt và xoay thẳng Label
                            var (alignedLabel, qrBoxScale) = labelDetector.CropAndAlignLabel(roi, rect.Value, rectPoints, rectPoints, qrPoints);
                            if (debugMode) ImageDebugSaver.SaveStep3AlignedLabel(alignedLabel);

                            if (showTime)
                            {
                                thoiGianCatXoayLabel.Stop();                       // dừng đếm
                                double ms3 = thoiGianCatXoayLabel.Elapsed.TotalMilliseconds;
                                Debug.WriteLine($"3. Thời gian cắt và xoay thẳng Label: {ms3:F2} ms");
                            }

                            ////3 DEBUG: luôn hiển thị ảnh cắt label
                            // Cập nhật UI phải qua Invoke và CLONE bitmap
                            //pictureBox1.BeginInvoke(new Action(() =>
                            //{
                            //    pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // co ảnh cho vừa khung
                            //    var old = pictureBox1.Image;
                            //    pictureBox1.Image = aligned != null ? (Bitmap)aligned.Clone() : null;
                            //    old?.Dispose();
                            //}));

                            if (alignedLabel != null)
                            {
                                //this.Invoke((Action)(() => ShowBitmap(aligned)));
                                //var ocrTime = Stopwatch.StartNew();
                                // 1️⃣ Gọi OCR trên vùng dưới bên trái

                                //ShowQrBox(aligned, qrBox);


                                var thoiGianCatVaGop2Vung = Stopwatch.StartNew();
                                //4. Cắt 2 vùng cần regconize
                                var mergedCrop = CropComponent.CropAndMergeBottomLeftAndAboveQr(alignedLabel, qrBoxScale);

                                if (debugMode) ImageDebugSaver.SaveStep4MergedCrop(mergedCrop);
                                if (showTime)
                                {
                                    thoiGianCatVaGop2Vung.Stop();                       // dừng đếm
                                    double ms4 = thoiGianCatVaGop2Vung.Elapsed.TotalMilliseconds;
                                    Debug.WriteLine($"4. Thời gian cắt và gộp 2 vùng cần OCR: {ms4:F2} ms");
                                }

                                var thoiGianOCR = Stopwatch.StartNew();

                                // 5. Gọi OCR trên mergedCrop
                                var (ocrTexts, minScore, debugText) = ExtractTextsFromMergedCrop(ocr, mergedCrop);
                                //if (debugMode) ImageDebugSaver.SaveStep5OcrInput(mergedCrop, minScore);

                                if (showTime)
                                {
                                    thoiGianOCR.Stop();                       // dừng đếm
                                    double ms5 = thoiGianOCR.Elapsed.TotalMilliseconds;
                                    Debug.WriteLine($"5. Thời gian OCR trên mergedCrop: {ms5:F2} ms");
                                }

                                //var (mergedCrop, ocrTexts, minScore, debugText) = RunOcrOnMergedBottomLeftAndAboveQr(ocr, alignedLabel, qrBoxScale);

                                var HauXuLy = Stopwatch.StartNew();
                                // 6. Hậu xử lý text trích được có nằm trong từ điển ? index đơn hàng trong label có khớp trong Qr?
                                var (donHang, maAo, size, color) = HandleOcrTexts(ocrTexts, qrText);
                               // DEBUG: hiển thị thời gian hậu xử lý
                                if (showTime)
                                {
                                    HauXuLy.Stop();                       // dừng đếm
                                    double ms6 = HauXuLy.Elapsed.TotalMilliseconds;
                                    Debug.WriteLine($"6. Thời gian hậu xử lý text trích được: {ms6:F2} ms");
                                }


                                // extract được text và text hợp lệ
                                if (donHang != "" && maAo != "" && size != "" && color != "")
                                //if (true)
                                {
                                    // DEBUG LƯU KẾT QUẢ
                                    if (debugMode) ImageDebugSaver.SaveStep6PostProcessText(donHang, maAo, size, color);
                                    //// 1 (Tùy chọn) Vẽ khung ROI (để thấy vùng detect)
                                    //Cv2.Rectangle(mat,
                                    //    new OpenCvSharp.Point(mapped.X, mapped.Y),
                                    //    new OpenCvSharp.Point(mapped.X + roi.Width, mapped.Y + roi.Height),
                                    //    Scalar.Blue, 2);

                                    Bitmap aligned_clone = (Bitmap)alignedLabel.Clone();

                                    // DEBUG: hiển thị ảnh cắt label
                                    pictureBox1.BeginInvoke(new Action(() =>
                                    {
                                        pictureBox1.SizeMode = PictureBoxSizeMode.Zoom; // co ảnh cho vừa khung
                                        var old = pictureBox1.Image;
                                        pictureBox1.Image = aligned_clone;
                                        old?.Dispose();
                                    }));

                                    cameraBox.BeginInvoke(new Action(() =>
                                    {
                                        cameraBox.IsObjectDetected = true;   // ✅ đổi sang khung xanh
                                        cameraBox.Invalidate();
                                        label7.Text = $"Accuracy: {minScore * 100:F2}%";
                                        label7.Visible = true; // hiện accuracy
                                        label8.Visible = true; // hiện thông báo đã detect

                                        sw.Stop();
                                        double ms = sw.ElapsedMilliseconds;
                                        double fps = (ms > 0) ? 1000.0 / ms : 0;
                                        // DEBUG: show time
                                        if (showTime) Debug.WriteLine($"TỔNG THỜI GIAN (FULL PIPELINE): {sw.ElapsedMilliseconds} ms");
                                        label6.Text = $"FPS: {fps:F1}";
                                    }));

                                    // 4 hiển thị ảnh cắt 1/4
                                    if (mergedCrop != null)
                                    {
                                        Bitmap mergedCrop_clone = (Bitmap)mergedCrop.Clone();
                                        pictureBox2.BeginInvoke(new Action(() =>
                                        {
                                            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
                                            var old = pictureBox2.Image;
                                            pictureBox2.Image = mergedCrop_clone;
                                            old?.Dispose();
                                        }));
                                        mergedCrop.Dispose();
                                    }

                                    //string combined = string.Join(" | ", ocrTexts.Select(tb => $"{tb.Text}: {tb.Score:F2}"));

                                    // 5 Hiển thị text lên textbox
                                    textBox1.BeginInvoke(new Action(() =>
                                    {
                                        textBox1.Multiline = true;
                                        textBox1.AutoSize = false;
                                        textBox1.ScrollBars = ScrollBars.Vertical;

                                        textBox1.Text =
                                        $"Đơn hàng: \"{donHang}\"\r\n" +
                                            $"QR: \"{qrText}\"\r\n" +
                                            $"Mã áo: \"{maAo}\"\r\n" +
                                            $"Size áo: \"{size}\"\r\n" +
                                            $"Màu áo: \"{color}\""; 

                                        //textBox1.Text = $"{qrText} | \r\n{debugText}";
                                    }));
                                    await Task.Delay(pauseTime * 1000); // dừng pauseTime giây trước khi detect tiếp
                                    alignedLabel.Dispose();
                                }
                                else // text không đúng định dạng
                                {
                                    Debug.WriteLine("text result k đúng định dạng");

                                    Bitmap bitMap_clonee = (Bitmap)bmpFull.Clone();
                                    cameraBox.BeginInvoke(new Action(() =>
                                    {
                                        var old = cameraBox.Image;
                                        cameraBox.Image = bitMap_clonee;
                                        old?.Dispose();
                                        sw.Stop();                       // dừng đếm
                                        double ms = sw.ElapsedMilliseconds;
                                        double fps = (ms > 0) ? 1000.0 / ms : 0;
                                        //Debug.WriteLine($"⏱ Time per frame: {ms:F1} ms  →  FPS: {fps:F1}");
                                        //Debug.WriteLine($"DetectLabelRegion time: {sw.ElapsedMilliseconds} ms");
                                        label6.Text = $"FPS: {fps:F1}";

                                    }));

                                }
                            }
                        }
                        else // HCN quanh label nằm ngoài Guild Box, hiển thị khung Guild Box màu đỏ, và vẽ khung HCN màu đỏ
                        {

                            cameraBox.BeginInvoke(new Action(() =>
                            {
                                label7.Visible = false;
                                label8.Visible = false;
                                cameraBox.IsObjectDetected = false;  // 🔴 trở lại khung đỏ
                                cameraBox.Invalidate();

                            }));

                            // 2️⃣ Chuyển tọa độ box trong ROI -> tọa độ full ảnh
                            var qrBox = qrPoints.Select(p =>
                                new OpenCvSharp.Point(p.X + mapped.X, p.Y + mapped.Y)
                            ).ToArray();


                            // chuyển tọa độ hình chữ nhật quanh Label -> tọa độ full ảnh
                            var qrRectangle = rectPoints.Select(p =>
                                new OpenCvSharp.Point(p.X + mapped.X, p.Y + mapped.Y)
                            ).ToArray();

                            // 3️⃣ Vẽ khung label và tâm trên frame full
                            Cv2.Polylines(mat, new[] { qrBox }, true, Scalar.Lime, 2);

                            // 3️⃣ Vẽ khung HCN lên frame full
                            Cv2.Polylines(mat, new[] { qrRectangle }, true, Scalar.Red, 2);

                            //Cv2.Circle(mat,
                            //  new OpenCvSharp.Point(
                            //      (int)(rectPoints[0].X + mapped.X),
                            //      (int)(rectPoints[0].Y + mapped.Y)),
                            //  4, Scalar.Red, -1);
                            var debugBmp = MatToBitmap(mat);
                            Bitmap debugBmp_clone = (Bitmap)debugBmp.Clone();
                            Debug.WriteLine("Thấy QR nhưng Label nằm ngoài Guild Box");

                            cameraBox.BeginInvoke(new Action(() =>
                            {
                                var old = cameraBox.Image;
                                cameraBox.Image = debugBmp_clone;
                                old?.Dispose();
                                sw.Stop();                       // dừng đếm
                                double ms = sw.ElapsedMilliseconds;
                                double fps = (ms > 0) ? 1000.0 / ms : 0;
                                //Debug.WriteLine($"⏱ Time per frame: {ms:F1} ms  →  FPS: {fps:F1}");
                                //Debug.WriteLine($"DetectLabelRegion time: {sw.ElapsedMilliseconds} ms");
                                label6.Text = $"FPS: {fps:F1}";

                            }));
                            debugBmp.Dispose();

                            //ShowBitmapCoDung(debugBmp1);
                        }



                    }
                    else // không thấy qr: hiển thị frame bình thường
                    {

                        cameraBox.BeginInvoke(new Action(() =>
                        {
                            label7.Visible = false;
                            label8.Visible = false;
                            cameraBox.IsObjectDetected = false;  // 🔴 trở lại khung đỏ
                            cameraBox.Invalidate();

                        }));
                        Debug.WriteLine("Không phát hiện được QR trong Guild Box!");
                        // Hiển thị ảnh gốc

                        Bitmap bitMat = MatToBitmap(mat);
                        cameraBox.BeginInvoke(new Action(() =>
                        {
                            var old = cameraBox.Image;
                            cameraBox.Image = bitMat;
                            old?.Dispose();
                            sw.Stop();                       // dừng đếm
                            double ms = sw.ElapsedMilliseconds;
                            double fps = (ms > 0) ? 1000.0 / ms : 0;
                            //Debug.WriteLine($"⏱ Time per frame: {ms:F1} ms  →  FPS: {fps:F1}");
                            //Debug.WriteLine($"DetectLabelRegion time: {sw.ElapsedMilliseconds} ms");
                            label6.Text = $"FPS: {fps:F1}";

                        }));

                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Detect ERROR] {ex.Message}");
                }

                // Free bitmap ROI tránh leak bộ nhớ
                roi.Dispose();
                // bmpFull đã được clone khi đưa vào UI → có thể giải phóng bản gốc ở đây
                bmpFull.Dispose();

                Thread.Sleep(1); // tránh CPU 100%
            }
        }

        // Sử dụng Paddle OCR ở 1/4 góc dưới bên trái
        public (Bitmap mergedCrop, List<string> texts, float minScore, string DebugText)
        RunOcrOnMergedBottomLeftAndAboveQr(PaddleOCREngine ocr, Bitmap aligned, OpenCvSharp.Point[] qrBox)
        {
            Bitmap bottomLeftCrop = null;
            Bitmap aboveQrCrop = null;
            Bitmap mergedCrop = null;
            var texts = new List<string>();
            string DebugText = "";

            try
            {
                if (ocr == null || aligned == null)
                    return (null, texts, -999, "[❌] Input null");

                int width = aligned.Width;
                int height = aligned.Height;


                //// Clone để tránh lỗi GDI+ "object in use elsewhere"
                Bitmap safeAligned = aligned.Clone(
                    new Rectangle(0, 0, aligned.Width, aligned.Height),
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                using var mat = BitmapToMat(safeAligned);

                //// === 2️⃣ Detect QR code trên ảnh grayscale ===
                //var qrDetector = new QRCodeDetector();
                //string qrData = qrDetector.DetectAndDecode(mat, out OpenCvSharp.Point2f[] points);

                //if (string.IsNullOrEmpty(qrData) || points == null || points.Length < 4)
                //{
                //    Debug.WriteLine("[⚠️] Không phát hiện được QR code trong ảnh grayscale.");
                //    safeAligned.Dispose();
                //    return (null, texts, -999, "[⚠️] No QR detected in grayscale image.");
                //}

                // === 3️⃣ Vùng góc dưới bên trái ===
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
                    //safeAligned.Dispose();
                    return (null, texts, -999, "ROI BottomLeft invalid");
                }
                bottomLeftCrop = safeAligned.Clone(roiBottomLeft, safeAligned.PixelFormat);

                // === 4️⃣ Cắt vùng "phía trên cạnh nối giữa points[0] & points[1]" ===
                var p0 = qrBox[0]; // top-left
                var p1 = qrBox[1]; // top-right
                var p2 = qrBox[2]; // bottom-right
                var p3 = qrBox[3]; // bottom-left

                // Vector cạnh trên & cạnh phải của QR
                var topVec = new OpenCvSharp.Point2f(p1.X - p0.X, p1.Y - p0.Y);
                var rightVec = new OpenCvSharp.Point2f(p2.X - p1.X, p2.Y - p1.Y);
                float qrWidth = (float)Math.Sqrt(topVec.X * topVec.X + topVec.Y * topVec.Y);
                float qrHeight = (float)Math.Sqrt(rightVec.X * rightVec.X + rightVec.Y * rightVec.Y);

                // Vector pháp tuyến hướng lên (vuông góc cạnh trên)
                var normal = new OpenCvSharp.Point2f(topVec.Y, -topVec.X);
                float len = (float)Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
                normal.X /= len;
                normal.Y /= len;

                // Các thông số vùng cắt
                float offset = (float)(utils.fileConfig.aboveQrComponent.doiTamLenTren * qrWidth);  // khoảng cách lên trên
                float widthAbove = (float)(utils.fileConfig.aboveQrComponent.width * qrWidth);   // rộng sang trái
                float heightAbove = (float)(utils.fileConfig.aboveQrComponent.height * qrHeight); // dài lên trên

                // Vector đơn vị theo hướng cạnh trên (trái → phải)
                var dir = new OpenCvSharp.Point2f(topVec.X / qrWidth, topVec.Y / qrWidth);

                // dời sang phải
                float shiftDist = utils.fileConfig.aboveQrComponent.doiTamSangPhai * qrWidth;


                // Gốc bắt đầu từ góc phải trên QR (p1)
                var baseTopRight = new OpenCvSharp.Point2f(
                    p1.X + normal.X * offset + dir.X * shiftDist,
                    p1.Y + normal.Y * offset + dir.Y * shiftDist);

                // Vùng cắt sẽ kéo lên trên (normal hướng lên) và sang trái (ngược hướng dir)
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

                // Warp Perspective để cắt vùng
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
                var croppedTopRight = new Mat();
                Cv2.WarpPerspective(mat, croppedTopRight, M, new OpenCvSharp.Size(widthAbove, heightAbove),
                    InterpolationFlags.Linear, BorderTypes.Replicate);

                // Convert sang Bitmap
                aboveQrCrop = MatToBitmap(croppedTopRight);

                // === 5️⃣ Ghép ảnh ===
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

                //Debug.WriteLine($"[✅] Merged crop size: {mergedWidth}x{mergedHeight}");

                // === 6️⃣ OCR ===
                OCRResult result;
                lock (ocr)
                {
                    result = ocr.DetectText(mergedCrop);
                }

                // === 7️⃣ Trích xuất text ===
                float minScore = 999;
                if (result?.TextBlocks?.Count > 0)
                {
                    texts = result.TextBlocks
                        .Where(tb => !string.IsNullOrWhiteSpace(tb.Text))
                        .Select(tb => tb.Text.Trim())
                        .ToList();

                    foreach (var tb in result.TextBlocks)
                    {
                        if (tb.Score < minScore)
                            minScore = tb.Score;
                        DebugText += $"{tb.Text?.Trim()} | Score: {tb.Score * 100:F2}%\r\n";
                    }
                }

                // === 8️⃣ Giải phóng ===
                aboveQrCrop?.Dispose();
                bottomLeftCrop?.Dispose();
                safeAligned.Dispose();

                return (mergedCrop, texts, minScore, DebugText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[❌ OCR ERROR] {ex.Message}");
                return (null, texts, -999, DebugText);
            }
        }


        // Dùng ảnh ghép để OCR extract chữ
        public (List<string> texts, float minScore, string DebugText) ExtractTextsFromMergedCrop(PaddleOCREngine ocr, Bitmap mergedCrop)
        {
            var texts = new List<string>();
            string DebugText = "";
            float minScore = 999;

            try
            {
                if (ocr == null || mergedCrop == null)
                    return (texts, -999, "[❌] Input null");

                OCRResult result;
                lock (ocr)
                {
                    result = ocr.DetectText(mergedCrop);
                }

                if (result?.TextBlocks?.Count > 0)
                {
                    texts = result.TextBlocks
                        .Where(tb => !string.IsNullOrWhiteSpace(tb.Text))
                        .Select(tb => tb.Text.Trim())
                        .ToList();

                    foreach (var tb in result.TextBlocks)
                    {
                        if (tb.Score < minScore)
                            minScore = tb.Score;
                        DebugText += $"{tb.Text?.Trim()} | Score: {tb.Score * 100:F2}%\r\n";
                    }
                }

                return (texts, minScore, DebugText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[❌ OCR ONLY ERROR] {ex.Message}");
                return (texts, -999, DebugText);
            }
        }
        // xử lý đầu ra của OCR
        private (string donHang, string maAo, string size, string color) HandleOcrTexts(List<string> ocrTexts, string qrText)
        {
            if (ocrTexts == null || ocrTexts.Count < 4)
            {
                Debug.WriteLine($"[HandleOcrTexts] Warning: OCR texts count is lower than 3, ocrTexts.Count = {ocrTexts.Count}");
                return ("", "", "", "");
            }

            string donHang = "";
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

            //Debug.WriteLine("=== OCR Texts after Trim ===");
            //for (int i = 0; i < ocrTexts.Count(); i++)
            //{
            //    Debug.WriteLine($"{ocrTexts[i]}");
            //}


            // Hậu xử lý text chỗ đơn hàng, số lượng ký tự phải bằng 3.
            if (string.IsNullOrEmpty(maAo) &&
                ocrTexts[0].Length == 3)
            {

                char idDonHang = ocrTexts[0][0];
                char tongSoDonHang = ocrTexts[0][ocrTexts[0].Length - 1];

                string[] qrTextSplit = qrText.Split('-');
                //Debug.WriteLine($"qrTextSplit last: {qrTextSplit}");
                //Debug.WriteLine($"idDonHang: {idDonHang}");
                //Debug.WriteLine($"ocrTexts[0]: {ocrTexts[0]}");



                // trường hợp có / ở cuối qrText
                if (qrTextSplit[qrTextSplit.Length - 1].Contains('/'))
                {

                    string stringLastQRText = qrTextSplit[qrTextSplit.Length - 1]; // ví dụ "1/5"
                    char qrTextSplit_last = stringLastQRText[0]; // Lấy 5
                    if (qrTextSplit_last != idDonHang)
                    {
                        Debug.WriteLine("=== QR Text có / ===");
                        Debug.WriteLine($"Chỉ số đơn hàng trong QR != Chỉ số đơn hàng trên Label - {qrTextSplit_last} != {idDonHang}");
                        return (donHang, maAo, size, color);
                    } else
                    {
                        donHang = idDonHang.ToString() + "/" + tongSoDonHang;
                    }
                }
                else // trường hợp KHÔNG có / ở cuối qrText
                {
                    string stringLastQRText = qrTextSplit[qrTextSplit.Length - 1]; // ví dụ "1/5"
                    char qrTextSplit_last = stringLastQRText[stringLastQRText.Length - 1]; // Lấy 5
                    if (qrTextSplit_last != idDonHang)
                    {
                        Debug.WriteLine("=== QR Text không có / ===");
                        Debug.WriteLine($"Chỉ số đơn hàng trong QR !=  Chỉ số đơn hàng trên Label - {qrTextSplit_last} != {idDonHang}");
                        return (donHang, maAo, size, color);
                    }else
                    {
                        donHang = idDonHang.ToString() + "/" + tongSoDonHang;
                    }
                }

                // 1️⃣ Kiểm tra mã áo (thường là số hoặc có chữ ngắn, như 3000, 500A, A12)
                if (string.IsNullOrEmpty(maAo) &&
                    models.Contains(ocrTexts[1].Trim().ToLower()))
                {
                    maAo = ocrTexts[1].Trim();
                }

                // 2️⃣ Kiểm tra size
                if (string.IsNullOrEmpty(size) &&
                    sizes.Contains(ocrTexts[2].Trim().ToLower()))
                {
                    size = ocrTexts[2].Trim();
                }

                //  > 4 chữ
                if (ocrTexts.Count() > 4)
                {
                    var color_trim = "";
                    for (int i = 3; i < ocrTexts.Count(); i++)
                    {
                        color_trim += ocrTexts[i].Trim();
                    }
                    if (string.IsNullOrEmpty(color) &&
                        colors.Contains(color_trim.ToLower()))
                    {
                        color = color_trim;
                    }

                }
                else // = 4
                {
                    if (string.IsNullOrEmpty(color) &&
                          colors.Contains(ocrTexts[3].Trim().ToLower()))
                    {
                        color = ocrTexts[3].Trim();
                    }
                }

                //Debug.WriteLine($"Đơn hàng: {donHang}, Mã áo: {maAo}, size {size}, color: {color}");
                return (donHang, maAo, size, color);
            }
            return (donHang, maAo, size, color);
        }



        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            base.OnFormClosing(e);
        }

        // Replace your current MatToBitmap with this safe version
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

        /// <summary>
        /// Khởi tạo OCR engine với cấu hình từ fileConfig.
        /// Tạo OCRParameter từ modelParams và khởi tạo PaddleOCREngine.
        /// </summary>
        private void InitOCR()
        {

            OCRModelConfig config = null;   // model tích hợp
            OCRParameter param = new OCRParameter
            {

                det = utils.fileConfig.modelParams.det,
                cls = utils.fileConfig.modelParams.cls,
                use_angle_cls = utils.fileConfig.modelParams.use_angle_cls,
                rec = utils.fileConfig.modelParams.rec,
                det_db_thresh = utils.fileConfig.modelParams.det_db_thresh,
                det_db_box_thresh = utils.fileConfig.modelParams.det_db_box_thresh,
                cls_thresh = utils.fileConfig.modelParams.cls_thresh,
                enable_mkldnn = utils.fileConfig.modelParams.enable_mkldnn,
                cpu_math_library_num_threads = utils.fileConfig.modelParams.cpu_math_library_num_threads,
                det_db_score_mode = utils.fileConfig.modelParams.det_db_score_mode
            };
            ocr = new PaddleOCREngine(config, param);
        }

        // Lấy vùng ROI tương ứng với guide box trên ảnh gốc
        // Chuyển đổi tọa độ từ overlay (đã scale) về tọa độ ảnh gốc
        // Input: frame - ảnh gốc, guideBox - vùng chọn trên overlay, cameraBox - PictureBox hiển thị
        // Output: RoiResult chứa ảnh cắt, vùng mapping và tham số scale/offset
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


        // show anh
        public void ShowBitmapCoDung(Bitmap bmp)
        {
            if (bmp == null) return;

            // Marshal to UI thread and block until the dialog is closed
            if (InvokeRequired)
            {
                Invoke(new Action<Bitmap>(ShowBitmapCoDung), bmp);
                return;
            }

            Form f = new Form
            {
                Text = "ROI - Press Enter to continue",
                StartPosition = FormStartPosition.CenterScreen,
                KeyPreview = true,
                // Fit into the screen in case the image is large
                Size = new System.Drawing.Size(
                    Math.Min(bmp.Width + 20, Screen.PrimaryScreen.WorkingArea.Width - 40),
                    Math.Min(bmp.Height + 60, Screen.PrimaryScreen.WorkingArea.Height - 80))
            };

            PictureBox pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = (Bitmap)bmp.Clone() // avoid GDI+ "in use" issues
            };

            f.Controls.Add(pb);

            // Close on Enter
            f.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
                {
                    e.Handled = true;
                    f.Close();
                }
            };

            // Optional: also close on double click
            pb.DoubleClick += (s, e) => f.Close();

            // Ensure image resources are freed
            f.FormClosed += (s, e) =>
            {
                pb.Image?.Dispose();
                pb.Dispose();
            };

            // Modal dialog blocks until user closes (or presses Enter)
            f.ShowDialog(this);
        }
        public static void ShowQrBox(Bitmap srcBmp, OpenCvSharp.Point2f[] qrBox, string windowName = "QR Crop Debug")
        {
            if (srcBmp == null)
            {
                Debug.WriteLine("[⚠️] Ảnh Bitmap rỗng, không thể hiển thị");
                return;
            }

            if (qrBox == null || qrBox.Length != 4)
            {
                Debug.WriteLine("[⚠️] Không có QR box hợp lệ để crop");
                return;
            }

            // ⚙️ Clone để tránh GDI+ lock
            Bitmap safeBmp = (Bitmap)srcBmp.Clone();

            try
            {
                // Chuyển Bitmap → Mat
                using var ms = new MemoryStream();
                safeBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                using var src = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);

                // === 1️⃣ Tạo mask và crop vùng QR theo polygon ===
                var pts = qrBox.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                var mask = Mat.Zeros(src.Size(), MatType.CV_8UC1);
                Cv2.FillPoly(mask, new[] { pts }, Scalar.White);

                // Tạo bounding box để giới hạn crop
                var rect = Cv2.BoundingRect(pts);

                // Áp mask và cắt vùng QR
                var cropped = new Mat();
                src.CopyTo(cropped, mask);
                Mat qrOnly = new Mat(cropped, rect);

                Debug.WriteLine("vao vẽ######################################");

                // === 2️⃣ Hiển thị ===
                Cv2.ImShow(windowName, qrOnly);
                Cv2.WaitKey(0);
                Cv2.DestroyWindow(windowName);

                qrOnly.Dispose();
                mask.Dispose();
                cropped.Dispose();
            }
            finally
            {
                safeBmp.Dispose();
            }
        }




        /// <summary>
        /// Overload: dùng cho Bitmap
        /// </summary>

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

