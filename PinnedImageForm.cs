using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 「钉在桌面 / 贴图」窗口：把一张截图以无边框、置顶、可拖动的小窗
    /// 固定在桌面上，类似 QQ 截图的贴图功能。窗口自己持有并释放位图，
    /// 调用方构造后不要再 Dispose 传进来的 Bitmap。
    ///
    /// 交互：左键拖动移动；Ctrl + 滚轮缩放；右键弹出菜单（复制 / 保存 /
    /// 置顶 / 透明度 / 缩放 / 关闭）；Esc 或双击关闭。
    /// </summary>
    internal sealed class PinnedImageForm : Form
    {
        private static readonly List<PinnedImageForm> OpenForms =
            new List<PinnedImageForm>();

        /// <summary>贴图数量变化（用于托盘菜单的「关闭所有贴图」状态）。</summary>
        public static event Action<int> CountChanged;

        public static int OpenCount
        {
            get { return OpenForms.Count; }
        }

        /// <summary>截图期间临时隐藏所有贴图，返回被隐藏的数量。</summary>
        public static int HideAllForCapture()
        {
            int hidden = 0;
            PinnedImageForm[] snapshot = OpenForms.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].IsDisposed && snapshot[i].Visible)
                {
                    snapshot[i].Hide();
                    hidden++;
                }
            }
            return hidden;
        }

        public static void ShowAllAfterCapture()
        {
            PinnedImageForm[] snapshot = OpenForms.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].IsDisposed && !snapshot[i].Visible)
                {
                    snapshot[i].Show();
                }
            }
        }

        public static void CloseAll()
        {
            PinnedImageForm[] snapshot = OpenForms.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].IsDisposed)
                {
                    snapshot[i].Close();
                }
            }
        }

        private static void NotifyCount()
        {
            Action<int> handler = CountChanged;
            if (handler != null)
            {
                handler(OpenForms.Count);
            }
        }

        /// <summary>合并所有显示器，避免使用部分运行库缺失的 VirtualScreen。</summary>
        private static Rectangle VirtualBounds()
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

        private readonly Bitmap _image;
        private float _scale = 1f;
        private bool _dragging;
        private bool _hover;
        private Point _dragStart;
        private Point _dragOrigin;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _topItem;

        public PinnedImageForm(Bitmap image, Point screenLocation)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }
            _image = image; // 所有权转移到本窗口

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.White;
            Cursor = Cursors.Arrow;
            KeyPreview = true;
            AllowDrop = false;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            UpdateStyles();

            ApplySize();

            Rectangle area = VirtualBounds();
            int x = Math.Max(area.Left, Math.Min(
                screenLocation.X, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(
                screenLocation.Y, area.Bottom - Height));
            Location = new Point(x, y);

            BuildMenu();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 阻止被 Alt+Tab / 任务栏切换抢焦点，保持「贴在桌面上」的观感。
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        private void ApplySize()
        {
            int w = Math.Max(32,
                (int)Math.Round(_image.Width * _scale)) + 2;
            int h = Math.Max(32,
                (int)Math.Round(_image.Height * _scale)) + 2;
            Rectangle area = VirtualBounds();
            w = Math.Min(w, area.Width);
            h = Math.Min(h, area.Height);
            ClientSize = new Size(w, h);
            Invalidate();
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;

            ToolStripMenuItem copy = new ToolStripMenuItem("复制图片 (Ctrl+C)");
            copy.Click += delegate { CopyToClipboard(); };
            _menu.Items.Add(copy);

            ToolStripMenuItem save = new ToolStripMenuItem("保存为 PNG (Ctrl+S)");
            save.Click += delegate { SaveToPng(); };
            _menu.Items.Add(save);

            _menu.Items.Add(new ToolStripSeparator());

            _topItem = new ToolStripMenuItem("始终置顶");
            _topItem.CheckOnClick = true;
            _topItem.Checked = true;
            _topItem.Click += delegate { TopMost = _topItem.Checked; };
            _menu.Items.Add(_topItem);

            ToolStripMenuItem opacity =
                new ToolStripMenuItem("透明度");
            AddOpacityItem(opacity, "不透明", 1.0);
            AddOpacityItem(opacity, "90%", 0.9);
            AddOpacityItem(opacity, "75%", 0.75);
            AddOpacityItem(opacity, "50%", 0.5);
            _menu.Items.Add(opacity);

            ToolStripMenuItem zoom = new ToolStripMenuItem("缩放");
            AddZoomItem(zoom, "放大 (Ctrl + 滚轮上)", 1.25f);
            AddZoomItem(zoom, "缩小 (Ctrl + 滚轮下)", 0.8f);
            ToolStripMenuItem reset = new ToolStripMenuItem("原始大小");
            reset.Click += delegate { _scale = 1f; ApplySize(); };
            zoom.DropDownItems.Add(reset);
            _menu.Items.Add(zoom);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem close = new ToolStripMenuItem("关闭贴图 (Esc)");
            close.Click += delegate { Close(); };
            _menu.Items.Add(close);

            ToolStripMenuItem closeAll =
                new ToolStripMenuItem("关闭所有贴图");
            closeAll.Click += delegate { CloseAll(); };
            _menu.Items.Add(closeAll);

            ContextMenuStrip = _menu;
        }

        private void AddOpacityItem(
            ToolStripMenuItem parent, string label, double value)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            double captured = value;
            item.Click += delegate { Opacity = captured; };
            parent.DropDownItems.Add(item);
        }

        private void AddZoomItem(
            ToolStripMenuItem parent, string label, float factor)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            float captured = factor;
            item.Click += delegate
            {
                _scale = Math.Max(0.25f,
                    Math.Min(4f, _scale * captured));
                ApplySize();
            };
            parent.DropDownItems.Add(item);
        }

        private Rectangle CloseButtonRect()
        {
            return new Rectangle(Width - 21, 3, 17, 17);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 双缓冲下不擦背景，避免拖动时闪白。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.White);
            g.InterpolationMode = _scale >= 1f
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image,
                new Rectangle(1, 1, Width - 2, Height - 2));

            Color border = _hover
                ? Color.FromArgb(0, 122, 255)
                : Color.FromArgb(178, 190, 208);
            using (Pen pen = new Pen(border, 1f))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            if (_hover)
            {
                // 右上角悬浮关闭按钮。
                Rectangle r = CloseButtonRect();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush back = new SolidBrush(
                    Color.FromArgb(215, 32, 42, 58)))
                {
                    g.FillEllipse(back, r);
                }
                using (Pen cross = new Pen(Color.White, 1.7f))
                {
                    cross.StartCap = LineCap.Round;
                    cross.EndCap = LineCap.Round;
                    g.DrawLine(cross,
                        r.Left + 5, r.Top + 5,
                        r.Right - 5, r.Bottom - 5);
                    g.DrawLine(cross,
                        r.Right - 5, r.Top + 5,
                        r.Left + 5, r.Bottom - 5);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_dragging)
            {
                _hover = false;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left &&
                _hover && CloseButtonRect().Contains(e.Location))
            {
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            _dragging = true;
            _dragStart = Cursor.Position;
            _dragOrigin = Location;
            Cursor = Cursors.SizeAll;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                return;
            }
            Point now = Cursor.Position;
            Location = new Point(
                _dragOrigin.X + now.X - _dragStart.X,
                _dragOrigin.Y + now.Y - _dragStart.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            Capture = false;
            Cursor = Cursors.Arrow;
            if (!ClientRectangle.Contains(e.Location))
            {
                _hover = false;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Ctrl + 滚轮缩放，避免普通滚轮误触。
            if ((ModifierKeys & Keys.Control) != Keys.Control)
            {
                return;
            }
            float factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
            _scale = Math.Max(0.25f, Math.Min(4f, _scale * factor));
            ApplySize();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopyToClipboard();
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                SaveToPng();
            }
        }

        private void CopyToClipboard()
        {
            try
            {
                // 同样带上文件列表，粘到桌面就是一个 png 文件。
                ClipboardPasteFiles.WriteBitmap(
                    _image, ClipboardWriteMode.Auto);
            }
            catch (Exception ex)
            {
                Log.Error("贴图复制失败", ex);
            }
        }

        private void SaveToPng()
        {
            try
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存贴图";
                    dialog.Filter = "PNG 图片 (*.png)|*.png";
                    dialog.DefaultExt = "png";
                    dialog.FileName = "贴图_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        _image.Save(dialog.FileName, ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("贴图保存失败", ex);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!OpenForms.Contains(this))
            {
                OpenForms.Add(this);
                NotifyCount();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            Capture = false;
            if (OpenForms.Contains(this))
            {
                OpenForms.Remove(this);
                NotifyCount();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_menu != null)
            {
                ContextMenuStrip = null;
                _menu.Dispose();
                _menu = null;
            }
            _image.Dispose();
        }
    }
}
