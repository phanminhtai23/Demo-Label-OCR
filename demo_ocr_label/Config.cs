using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace demo_ocr_label
{
    public class Config
    {

        // vùng chứa chứa 3 thông tin: mã áo, size áo và màu áo
        public Component bottomLeftComponent { get; set; }
        // vùng chứa thông tin số lượng đơn hàng và thứ tự đơn hàng
        public Component aboveQrComponent { get; set; }
        // các tham số của mô hình PadlleOCR
        public PaddleOCRParams modelParams { get; set; }

        public SystemArivables systemArivable { get; set; }

        public LabelRectangle labelRectangle { get; set; }
    }

        
    // mô tả một vùng cắt thông tin số lượng đơn hàng - nằm phía trên qr code. Độ lớn tính tương đối % so sánh với độ dài cạnh của qr code
    public class Component
    {
        public float doiTamSangPhai { get; set; }   // dời vị trí cắt sang phải, tính từ góc trên bên phải của qr code
        public float doiTamLenTren { get; set; }   // dời vị trí cắt lên trên, tính từ góc trên bên phải của qr code
        public float width { get; set; } // chiều rộng vùng cắt, tính từ vị trí cắt sang trái
        public float height { get; set; } // chiều cao vùng cắt, tính từ vị trí cắt lên trên

    }

    public class PaddleOCRParams
    {
        // 🔹 Có nhận diện chữ (Detection)
        public bool det { get; set; } = true;

        // 🔹 Có nhận diện hướng chữ (Classification)
        public bool cls { get; set; } = false;

        // 🔹 Sử dụng bộ phân loại hướng chữ (Angle Classifier)
        public bool use_angle_cls { get; set; }

        // 🔹 Có nhận diện nội dung chữ (Recognition)
        public bool rec { get; set; } = true;

        // 🔹 Ngưỡng nhị phân hóa trong DB Detector (0.0–1.0)
        public float det_db_thresh { get; set; } = 0.3f;

        // 🔹 Ngưỡng confidence để giữ lại box (0.0–1.0)
        public float det_db_box_thresh { get; set; } = 0.5f;

        // 🔹 Ngưỡng confidence khi kiểm tra hướng chữ (classification)
        public float cls_thresh { get; set; } = 0.9f;

        // 🔹 Bật tăng tốc tính toán bằng Intel MKL-DNN (oneDNN)
        public bool enable_mkldnn { get; set; } = true;

        // 🔹 Số luồng CPU song song được dùng
        public int cpu_math_library_num_threads { get; set; } = 6;

        // tính score dựa trên đa giác, chính xách hơn nhưng chậm hơn xíu
        public bool det_db_score_mode { get; set; } = false; 

    }
    public class SystemArivables
    {
        public bool debugMode { get; set; } = true;
        public bool showTime { get; set; } = false;
    }

    public class LabelRectangle
    {
        public float up { get; set; }
        public float down { get; set; }
        public float left { get; set; }
        public float right { get; set; }
    }
}