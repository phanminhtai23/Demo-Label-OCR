using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace demo_ocr_label
{
    public class Config
    {
        public Component bottomLeftComponent { get; set; }
        public Component aboveQrComponent { get; set; }
        public PaddleOCRParams modelParams { get; set; }
    }

    public class Component
    {
        public double width { get; set; }
        public double height { get; set; }
        public double khoangCach { get; set; }   // có thể null, không sao
    }

    public class PaddleOCRParams
    {
        // 🔹 Có nhận diện chữ (Detection)
        public bool det { get; set; } = true;

        // 🔹 Có nhận diện hướng chữ (Classification)
        public bool cls { get; set; } = false;

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
}