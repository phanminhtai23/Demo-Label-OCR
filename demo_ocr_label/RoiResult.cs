using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace demo_ocr_label
{
    public class RoiResult
    {
        public Bitmap? Image { get; set; }
        public Rectangle Mapped { get; set; }  // vùng cắt trong ảnh gốc
        public float Scale { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
    }
}
