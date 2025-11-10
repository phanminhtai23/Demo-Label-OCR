using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace demo_ocr_label   // <<< trùng với namespace của Form1
{
    public class OverlayPictureBox : PictureBox   // <<< phải là public
    {
        public Rectangle GuideBox { get; set; } = Rectangle.Empty;
        //public Color BoxColor { get; set; } = Color.Red;
        public bool IsObjectDetected { get; set; } = false;
        // khi tim thay object
        //cameraBox.IsObjectDetected = true;
        //cameraBox.Invalidate();

        // khi khong detec nua
        //cameraBox.IsObjectDetected = false;
        //cameraBox.Invalidate();


        private bool dragging, resizing;
        private int handle = 8;
        private Point dragStart;
        private Rectangle originalBox;
        private ResizeDir dir = ResizeDir.None;

        public OverlayPictureBox()
        {
            this.DoubleBuffered = true;
            this.MouseDown += OnDown;
            this.MouseMove += OnMove;
            this.MouseUp += OnUp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (GuideBox == Rectangle.Empty) return;

            using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            using var region = new Region(new Rectangle(Point.Empty, ClientSize));
            region.Exclude(GuideBox);
            e.Graphics.FillRegion(dim, region);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // 🔴 Mặc định đỏ — chuyển xanh nếu có object detected
            var color = IsObjectDetected ? Color.Lime : Color.Red;
            using var pen = new Pen(color, 3);

            e.Graphics.DrawRectangle(pen, GuideBox);

            foreach (var r in Handles()) { e.Graphics.FillRectangle(Brushes.White, r); e.Graphics.DrawRectangle(Pens.Black, r); }
        }

        // --- mouse + helpers (giữ đơn giản: 4 cạnh) ---
        private void OnDown(object? s, MouseEventArgs e)
        {
            dir = HitHandle(e.Location);
            if (dir != ResizeDir.None) { resizing = true; dragStart = e.Location; originalBox = GuideBox; return; }
            if (GuideBox.Contains(e.Location)) { dragging = true; dragStart = e.Location; originalBox = GuideBox; }
        }
        private void OnMove(object? s, MouseEventArgs e)
        {
            if (GuideBox == Rectangle.Empty) return;
            if (resizing)
            {
                int dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
                var b = originalBox;
                if (dir == ResizeDir.Left) { b.X += dx; b.Width -= dx; }
                if (dir == ResizeDir.Right) { b.Width += dx; }
                if (dir == ResizeDir.Top) { b.Y += dy; b.Height -= dy; }
                if (dir == ResizeDir.Bottom) { b.Height += dy; }
                GuideBox = Clamp(b); Invalidate(); return;
            }
            if (dragging)
            {
                int dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
                GuideBox = Clamp(new Rectangle(originalBox.X + dx, originalBox.Y + dy, originalBox.Width, originalBox.Height));
                Invalidate(); return;
            }
            dir = HitHandle(e.Location);
            Cursor = dir switch
            {
                ResizeDir.Left or ResizeDir.Right => Cursors.SizeWE,
                ResizeDir.Top or ResizeDir.Bottom => Cursors.SizeNS,
                _ => GuideBox.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default
            };
        }
        private void OnUp(object? s, MouseEventArgs e) { dragging = false; resizing = false; dir = ResizeDir.None; }

        private Rectangle Clamp(Rectangle r)
        { if (r.X < 0) r.X = 0; if (r.Y < 0) r.Y = 0; if (r.Right > Width) r.Width = Width - r.X; if (r.Bottom > Height) r.Height = Height - r.Y; return r; }
        private Rectangle[] Handles()
        {
            int w = handle;
            return new[]
            {
                new Rectangle(GuideBox.Left, GuideBox.Top + GuideBox.Height/2 - w/2, w, w),      // L
                new Rectangle(GuideBox.Right - w, GuideBox.Top + GuideBox.Height/2 - w/2, w, w), // R
                new Rectangle(GuideBox.Left + GuideBox.Width/2 - w/2, GuideBox.Top, w, w),       // T
                new Rectangle(GuideBox.Left + GuideBox.Width/2 - w/2, GuideBox.Bottom - w, w, w) // B
            };
        }
        private ResizeDir HitHandle(Point p)
        {
            var hs = Handles();
            if (hs[0].Contains(p)) return ResizeDir.Left;
            if (hs[1].Contains(p)) return ResizeDir.Right;
            if (hs[2].Contains(p)) return ResizeDir.Top;
            if (hs[3].Contains(p)) return ResizeDir.Bottom;
            return ResizeDir.None;
        }
        private enum ResizeDir { None, Left, Right, Top, Bottom }
    }
}
