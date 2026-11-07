using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 截屏选区窗口：全屏覆盖，鼠标拖拽框选区域，
    /// Enter 或双击确认，Esc 取消。确认后返回裁剪好的截图位图。
    ///
    /// 实现要点：
    /// - 进入时抓取整屏快照作为背景；
    /// - 选区外叠半透明黑色遮罩，选区内部恢复原图亮度，方便看清取景范围；
    /// - 鼠标捕获（MouseCapture）保证拖拽移出窗口外仍能持续框选。
    /// </summary>
    internal sealed class ScreenshotCaptureForm : Form
    {
        private Bitmap _screen;
        private Rectangle _selection = Rectangle.Empty;
        private Point _startPoint;
        private bool _dragging;

        public ScreenshotCaptureForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = GetVirtualBounds();
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
        }

        /// <summary>合并所有显示器的边界，得到虚拟屏幕矩形。</summary>
        private static Rectangle GetVirtualBounds()
        {
            Screen[] screens = Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                return Screen.PrimaryScreen.Bounds;
            }

            Rectangle result = screens[0].Bounds;
            for (int i = 1; i < screens.Length; i++)
            {
                result = Rectangle.Union(result, screens[i].Bounds);
            }
            return result;
        }

        /// <summary>选区截图（原尺寸，未缩放）。取消时为 null。</summary>
        public Bitmap Captured { get; private set; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CaptureFullScreen();
        }

        private void CaptureFullScreen()
        {
            Rectangle bounds = GetVirtualBounds();
            _screen = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(_screen))
            {
                graphics.CopyFromScreen(
                    bounds.Left, bounds.Top, 0, 0, _screen.Size);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_screen != null)
            {
                // 原图打底。
                graphics.DrawImage(_screen, 0, 0);

                // 半透明遮罩。
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    graphics.FillRectangle(brush, ClientRectangle);
                }

                // 选区内部恢复原图（通过裁剪区域重绘整屏快照）。
                if (_selection.Width > 1 && _selection.Height > 1)
                {
                    GraphicsState state = graphics.Save();
                    using (GraphicsPath clip = new GraphicsPath())
                    {
                        clip.AddRectangle(new RectangleF(
                            _selection.X, _selection.Y,
                            _selection.Width, _selection.Height));
                        graphics.SetClip(clip, CombineMode.Replace);
                        graphics.DrawImage(_screen, 0, 0);
                    }
                    graphics.Restore(state);

                    // 选区边框。
                    using (Pen pen = new Pen(Color.FromArgb(255, 60, 110, 220), 1.5f))
                    {
                        graphics.DrawRectangle(pen, _selection);
                    }
                }

                // 尺寸提示。
                if (_selection.Width > 1 && _selection.Height > 1)
                {
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(40, 46, 58)))
                    using (StringFormat format = new StringFormat())
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        string text = _selection.Width + " × " + _selection.Height;
                        SizeF size = graphics.MeasureString(
                            text, SystemFonts.MessageBoxFont);
                        RectangleF labelRect = new RectangleF(
                            _selection.Left,
                            _selection.Top - size.Height - 6,
                            Math.Max(size.Width, 40),
                            size.Height + 10);
                        if (labelRect.Top < 0)
                        {
                            labelRect.Y = _selection.Bottom + 4;
                        }
                        if (labelRect.Right > ClientSize.Width)
                        {
                            labelRect.X = ClientSize.Width - labelRect.Width;
                        }
                        graphics.FillRectangle(
                            brush, labelRect);
                        graphics.DrawString(
                            text, SystemFonts.MessageBoxFont, textBrush,
                            labelRect, format);
                    }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _startPoint = new Point(e.X, e.Y);
            _selection = new Rectangle(e.X, e.Y, 1, 1);
            _dragging = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                return;
            }

            Point current = new Point(e.X, e.Y);
            _selection = Normalize(_startPoint, current);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _dragging = false;
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            ConfirmSelection();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Enter)
            {
                ConfirmSelection();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void ConfirmSelection()
        {
            if (_selection.Width < 10 || _selection.Height < 10)
            {
                return;
            }

            if (_screen != null)
            {
                Captured = CloneRegion(_screen, _selection);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return new Rectangle(left, top, right - left, bottom - top);
        }

        private static Bitmap CloneRegion(Bitmap source, Rectangle region)
        {
            // 裁剪到源图范围内，避免越界。
            region = Rectangle.Intersect(
                region,
                new Rectangle(0, 0, source.Width, source.Height));
            if (region.Width < 1 || region.Height < 1)
            {
                return null;
            }

            Bitmap result = new Bitmap(region.Width, region.Height);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.DrawImage(
                    source,
                    new RectangleF(0, 0, region.Width, region.Height),
                    new RectangleF(region.X, region.Y,
                        region.Width, region.Height),
                    GraphicsUnit.Pixel);
            }
            return result;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_screen != null)
            {
                _screen.Dispose();
                _screen = null;
            }
            base.OnFormClosing(e);
        }
    }
}
