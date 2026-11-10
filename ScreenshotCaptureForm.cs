using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 一体化截屏浮层（微信/QQ 截图风格）：
    /// 全屏冻结画面 → 拖拽框选（左键或右键都可以）→ 选区出现 8 个
    /// 缩放手柄与尺寸标签，并在下方浮出图标工具栏 → 就地标注 →
    /// 保存 / 取消 / 完成（打开编辑器）/ 翻译（打开编辑器并自动翻译）。
    ///
    /// 与旧版的区别：
    /// - 抓取屏幕放在构造函数里（窗口尚未创建），避免把自己拍进去；
    /// - 遮罩预先合成为一张位图，且开启双缓冲，拖拽时不再闪烁；
    /// - 首次框选左右键一视同仁；松开后锁定选区（一次快捷键一张图），
    ///   光标由十字变回箭头，右键只用于退出当前工具；
    /// - 所有标注工具直接浮在选区旁边，不再依赖第二个窗口才出现。
    /// </summary>
    internal sealed class ScreenshotCaptureForm : Form
    {
        public enum CaptureResult
        {
            None,
            /// <summary>「对号」：把合成图复制到剪贴板后直接结束，不打开任何窗口。</summary>
            Copy,
            Translate,
            /// <summary>「取字」：只做 OCR，全文复制到剪贴板，不弹任何窗口。</summary>
            Ocr,
            Save,
            Pin
        }

        private enum Tool
        {
            None,
            Rect,
            Ellipse,
            Arrow,
            Pen,
            Text,
            Number,
            Highlight,
            Mosaic,
            Crop
        }

        private enum DragMode
        {
            None,
            Select,
            Move,
            Resize,
            Draw
        }

        private enum HandleKind
        {
            None,
            TopLeft,
            Top,
            TopRight,
            Right,
            BottomRight,
            Bottom,
            BottomLeft,
            Left
        }

        private enum IconKind
        {
            Rect,
            Ellipse,
            Arrow,
            Pen,
            Text,
            Number,
            Highlight,
            Mosaic,
            Undo,
            Crop,
            Scroll,
            Color,
            Width,
            Save,
            Pin,
            Translate,
            Ocr,
            Cancel,
            Done
        }

        // ------------------------------------------------------------------
        // 形状：全部保存在「虚拟屏幕坐标」（与窗体客户区 1:1），绘制和
        // 导出都使用同一套坐标，导出时只做一次平移，避免缩放带来的偏差。
        // ------------------------------------------------------------------

        private abstract class Shape
        {
            public Color Color;
            public float Width;

            public abstract void Draw(Graphics g, Bitmap screen);
            public abstract void Shift(int dx, int dy);
            public abstract Shape Clone();
        }

        private sealed class FrameShape : Shape
        {
            public RectangleF Rect;
            public bool Ellipse;

            public override void Draw(Graphics g, Bitmap screen)
            {
                RectangleF r = NormalizeRect(Rect);
                if (r.Width < 1 || r.Height < 1)
                {
                    return;
                }

                using (Pen pen = new Pen(Color, Math.Max(1f, Width)))
                {
                    pen.LineJoin = LineJoin.Round;
                    if (Ellipse)
                    {
                        g.DrawEllipse(pen, r);
                    }
                    else
                    {
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    }
                }
            }

            public override void Shift(int dx, int dy)
            {
                Rect = new RectangleF(Rect.X + dx, Rect.Y + dy,
                    Rect.Width, Rect.Height);
            }

            public override Shape Clone()
            {
                return (FrameShape)MemberwiseClone();
            }
        }

        private sealed class StrokeShape : Shape
        {
            public List<PointF> Points = new List<PointF>();
            public bool Arrow;

            public override void Draw(Graphics g, Bitmap screen)
            {
                if (Points.Count < 2)
                {
                    return;
                }

                using (Pen pen = new Pen(Color, Math.Max(1f, Width)))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, Points.ToArray());

                    if (Arrow)
                    {
                        PointF from = Points[Points.Count - 2];
                        PointF to = Points[Points.Count - 1];
                        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
                        float length = Math.Max(10f, pen.Width * 4f);
                        float spread = 0.42f;
                        PointF p1 = new PointF(
                            to.X - (float)(length * Math.Cos(angle - spread)),
                            to.Y - (float)(length * Math.Sin(angle - spread)));
                        PointF p2 = new PointF(
                            to.X - (float)(length * Math.Cos(angle + spread)),
                            to.Y - (float)(length * Math.Sin(angle + spread)));
                        using (SolidBrush brush = new SolidBrush(Color))
                        {
                            g.FillPolygon(brush,
                                new PointF[] { to, p1, p2 });
                        }
                    }
                }
            }

            public override void Shift(int dx, int dy)
            {
                for (int i = 0; i < Points.Count; i++)
                {
                    Points[i] = new PointF(
                        Points[i].X + dx, Points[i].Y + dy);
                }
            }

            public override Shape Clone()
            {
                StrokeShape copy = (StrokeShape)MemberwiseClone();
                copy.Points = new List<PointF>(Points);
                return copy;
            }
        }

        private sealed class FillShape : Shape
        {
            public RectangleF Rect;
            public bool Mosaic;

            public override void Draw(Graphics g, Bitmap screen)
            {
                RectangleF rf = NormalizeRect(Rect);
                Rectangle r = new Rectangle(
                    (int)Math.Floor(rf.X), (int)Math.Floor(rf.Y),
                    (int)Math.Ceiling(rf.Width), (int)Math.Ceiling(rf.Height));
                if (r.Width < 1 || r.Height < 1)
                {
                    return;
                }

                if (!Mosaic)
                {
                    using (SolidBrush brush = new SolidBrush(
                        Color.FromArgb(105, Color)))
                    {
                        g.FillRectangle(brush, r);
                    }
                    return;
                }

                DrawMosaic(g, screen, r);
            }

            private static void DrawMosaic(
                Graphics g, Bitmap screen, Rectangle r)
            {
                if (screen == null)
                {
                    return;
                }

                Rectangle visible = Rectangle.Intersect(
                    r, new Rectangle(0, 0, screen.Width, screen.Height));
                if (visible.Width < 1 || visible.Height < 1)
                {
                    return;
                }

                const int block = 10;
                int tw = Math.Max(1, visible.Width / block);
                int th = Math.Max(1, visible.Height / block);
                using (Bitmap tiny = new Bitmap(tw, th))
                {
                    using (Graphics tg = Graphics.FromImage(tiny))
                    {
                        tg.InterpolationMode =
                            InterpolationMode.NearestNeighbor;
                        tg.PixelOffsetMode = PixelOffsetMode.Half;
                        tg.DrawImage(screen,
                            new Rectangle(0, 0, tw, th),
                            visible, GraphicsUnit.Pixel);
                    }

                    for (int by = 0; by < visible.Height; by += block)
                    {
                        for (int bx = 0; bx < visible.Width; bx += block)
                        {
                            int sx = Math.Min(tw - 1, bx / block);
                            int sy = Math.Min(th - 1, by / block);
                            Color c = tiny.GetPixel(sx, sy);
                            using (SolidBrush brush = new SolidBrush(c))
                            {
                                g.FillRectangle(brush,
                                    visible.Left + bx, visible.Top + by,
                                    Math.Min(block, visible.Width - bx),
                                    Math.Min(block, visible.Height - by));
                            }
                        }
                    }
                }
            }

            public override void Shift(int dx, int dy)
            {
                Rect = new RectangleF(Rect.X + dx, Rect.Y + dy,
                    Rect.Width, Rect.Height);
            }

            public override Shape Clone()
            {
                return (FillShape)MemberwiseClone();
            }
        }

        private sealed class TextShape : Shape
        {
            public PointF Position;
            public string Text;
            public float Size;

            public override void Draw(Graphics g, Bitmap screen)
            {
                if (string.IsNullOrEmpty(Text))
                {
                    return;
                }

                using (Font font = new Font(
                    "Microsoft YaHei UI", Math.Max(12f, Size),
                    FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush shadow = new SolidBrush(
                    Color.FromArgb(190, 0, 0, 0)))
                using (SolidBrush brush = new SolidBrush(Color))
                {
                    g.DrawString(Text, font, shadow,
                        Position.X + 1.5f, Position.Y + 1.5f);
                    g.DrawString(Text, font, brush, Position);
                }
            }

            public override void Shift(int dx, int dy)
            {
                Position = new PointF(Position.X + dx, Position.Y + dy);
            }

            public override Shape Clone()
            {
                return (TextShape)MemberwiseClone();
            }
        }

        private sealed class NumberShape : Shape
        {
            public PointF Position;
            public int Number;

            public override void Draw(Graphics g, Bitmap screen)
            {
                const float radius = 13f;
                RectangleF circle = new RectangleF(
                    Position.X - radius, Position.Y - radius,
                    radius * 2, radius * 2);
                using (SolidBrush brush = new SolidBrush(Color))
                using (Pen pen = new Pen(Color.White, 2f))
                using (Font font = new Font(
                    "Microsoft YaHei UI", 14f, FontStyle.Bold,
                    GraphicsUnit.Pixel))
                using (SolidBrush textBrush =
                    new SolidBrush(Color.White))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    g.FillEllipse(brush, circle);
                    g.DrawEllipse(pen, circle);
                    g.DrawString(Number.ToString(), font, textBrush,
                        circle, format);
                }
            }

            public override void Shift(int dx, int dy)
            {
                Position = new PointF(Position.X + dx, Position.Y + dy);
            }

            public override Shape Clone()
            {
                return (NumberShape)MemberwiseClone();
            }
        }

        // ------------------------------------------------------------------
        // 工具栏按钮：统一 18×18 图标网格、圆角悬停态、抗锯齿线性图标。
        // ------------------------------------------------------------------

        private sealed class IconButton : Control
        {
            private readonly IconKind _kind;
            private bool _hover;
            private bool _checked;
            private Color _swatch;
            private float _stroke = 2f;

            public IconButton(IconKind kind, string toolTip, ToolTip tips)
            {
                _kind = kind;
                Size = new Size(34, 34);
                BackColor = ToolbarBack;
                TabStop = false;
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint, true);
                // 不让按钮抢走窗体焦点，否则 Esc / Ctrl+Z / 数字键
                // 快捷键在点过工具栏之后会失效。
                SetStyle(ControlStyles.Selectable, false);
                tips.SetToolTip(this, toolTip);
            }

            public IconKind Kind
            {
                get { return _kind; }
            }

            public bool Checked
            {
                get { return _checked; }
                set
                {
                    if (_checked != value)
                    {
                        _checked = value;
                        Invalidate();
                    }
                }
            }

            public Color Swatch
            {
                get { return _swatch; }
                set { _swatch = value; Invalidate(); }
            }

            public float Stroke
            {
                get { return _stroke; }
                set { _stroke = value; Invalidate(); }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hover = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hover = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(ToolbarBack);

                bool primary = _kind == IconKind.Done;
                bool danger = _kind == IconKind.Cancel;
                RectangleF inner = new RectangleF(
                    2f, 2f, Width - 4f, Height - 4f);
                Color fill = Color.Empty;

                if (primary)
                {
                    fill = _hover
                        ? Color.FromArgb(0, 104, 220)
                        : AccentColor;
                }
                else if (danger)
                {
                    fill = _hover
                        ? Color.FromArgb(255, 223, 227)
                        : Color.FromArgb(255, 239, 241);
                }
                else if (_checked)
                {
                    fill = Color.FromArgb(225, 238, 255);
                }
                else if (_hover)
                {
                    fill = Color.FromArgb(239, 243, 249);
                }

                if (fill != Color.Empty && Enabled)
                {
                    using (GraphicsPath path = MakeRoundedRect(inner, 8f))
                    using (SolidBrush brush = new SolidBrush(fill))
                    {
                        g.FillPath(brush, path);
                    }
                }

                Color ink;
                if (primary)
                {
                    ink = Color.White;
                }
                else if (danger)
                {
                    ink = Color.FromArgb(211, 60, 68);
                }
                else if (_checked)
                {
                    ink = AccentColor;
                }
                else
                {
                    ink = IconInk;
                }
                if (_kind == IconKind.Color)
                {
                    ink = _swatch;
                }
                // 不可用的按钮（比如长图状态下的「滚动截图」）画淡一点。
                if (!Enabled)
                {
                    ink = Color.FromArgb(186, 192, 202);
                }

                DrawIcon(g, _kind,
                    new Rectangle(0, 0, Width, Height), ink, _stroke);
            }
        }

        private sealed class ToolbarPanel : Panel
        {
            public ToolbarPanel()
            {
                BackColor = ToolbarBack;
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ApplyShape();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                ApplyShape();
            }

            /// <summary>圆角外形，比直角工具条更接近主流截图工具。</summary>
            private void ApplyShape()
            {
                if (Width < 2 || Height < 2)
                {
                    return;
                }
                Region previous = Region;
                using (GraphicsPath path = MakeRoundedRect(
                    new RectangleF(0f, 0f, Width, Height), 9f))
                {
                    Region = new Region(path);
                }
                if (previous != null)
                {
                    previous.Dispose();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(ToolbarBack);
                RectangleF frame = new RectangleF(
                    0.5f, 0.5f, Width - 1f, Height - 1f);
                using (GraphicsPath path = MakeRoundedRect(frame, 9f))
                using (Pen pen = new Pen(
                    Color.FromArgb(214, 222, 234), 1f))
                {
                    g.DrawPath(pen, path);
                }
                base.OnPaint(e);
            }
        }

        private static readonly Color AccentColor =
            Color.FromArgb(0, 122, 255);
        private static readonly Color ToolbarBack =
            Color.FromArgb(255, 255, 255);
        private static readonly Color IconInk =
            Color.FromArgb(72, 84, 102);
        private static readonly Color SeparatorColor =
            Color.FromArgb(224, 230, 239);
        private static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(240, 62, 62),
            Color.FromArgb(247, 148, 29),
            Color.FromArgb(247, 199, 44),
            Color.FromArgb(45, 180, 96),
            Color.FromArgb(0, 122, 255),
            Color.FromArgb(38, 44, 56)
        };
        private static readonly float[] Widths = new float[] { 2f, 4f, 8f };

        private Bitmap _screen;
        private Bitmap _dimmed;
        private Bitmap _captured;
        private Bitmap _raw;
        private Rectangle _selection = Rectangle.Empty;
        private List<Shape> _shapes = new List<Shape>();
        private readonly Stack<List<Shape>> _undo =
            new Stack<List<Shape>>();

        private DragMode _dragMode = DragMode.None;
        private HandleKind _handle = HandleKind.None;
        private MouseButtons _dragButton = MouseButtons.None;
        private Point _dragStart;
        private Point _lastPoint;
        // 标注起点：屏幕坐标下等于 _dragStart，长图模式下是「长图坐标」。
        private Point _drawStart;
        private Rectangle _dragOrigin;
        private Shape _preview;
        private Tool _tool = Tool.None;
        private Color _color = Palette[0];
        private float _stroke = Widths[0];
        private int _numberCounter = 1;
        private bool _finished;
        // 选区一旦框定就锁定：同一次快捷键会话内不允许再框选第二次。
        // 想重新截图需要再按一次快捷键（或 Esc 取消后重按）。
        private bool _selectionLocked;

        // 滚动截图期间会隐藏浮层，防止重复进入。
        private bool _scrollInProgress;

        // ---- 长图状态 ----
        // 滚动截图拼好之后不另开窗口，而是把长图直接当成这张「截图」：
        // 浮层原地回来，选区变成 1:1 的取景框，滚轮上下翻看，
        // 标注 / 保存 / 复制 / 贴图 / 翻译全都和普通截图一模一样。
        private Bitmap _longImage;
        private int _longScroll;
        // 截图开始前的前台窗口：滚动截图时要把焦点还给它，
        // 否则滚轮会滚到我们自己的（已隐藏的）窗口上。
        private readonly IntPtr _foregroundWindow;

        // ---- 窗口直接选取 ----
        // 鼠标悬停在一个窗口上时把整窗高亮出来，单击即截这个窗口，
        // 不用先把整个窗口小心翼翼地圈一遍。
        private Rectangle _hoverRect = Rectangle.Empty;
        private WindowTarget _hoverWindow;
        private Point _hoverPoint = new Point(int.MinValue, int.MinValue);

        private readonly ToolbarPanel _toolbar;
        private readonly List<IconButton> _buttons =
            new List<IconButton>();
        private readonly ToolTip _tips;
        private TextBox _textInput;
        // 颜色 / 线宽下拉菜单复用同一个实例。以前每次点击都新建、
        // 并在 Closed 事件里立即 Dispose，会与 WinForms 的
        // ModalMenuFilter 冲突，抛 ObjectDisposedException 中断截图。
        private ContextMenuStrip _colorMenu;
        private ContextMenuStrip _widthMenu;

        /// <summary>
        /// 父窗口绘制时裁掉子控件区域，否则全屏位图会把浮动工具栏盖住。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        public ScreenshotCaptureForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            KeyPreview = false;

            Bounds = GetVirtualBounds();

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            UpdateStyles();

            // 必须在窗口创建 / 显示之前抓屏，否则会把自己的黑色窗体
            // 一起拍进去，看上去就像「截图没内容」。
            _foregroundWindow = NativeMethods.GetForegroundWindow();
            CaptureFullScreen();

            _tips = new ToolTip();
            _tips.ShowAlways = true;
            _toolbar = BuildToolbar();
            Controls.Add(_toolbar);
            _toolbar.Visible = false;
        }

        /// <summary>最终合成好的截图（含标注）。调用方负责释放。</summary>
        public Bitmap Captured
        {
            get { return _captured; }
        }

        /// <summary>
        /// 同一选区但不含任何标注的原图，专供 OCR 识别使用，
        /// 避免矩形框 / 马赛克 / 文字被当成待翻译内容。
        /// 调用方负责释放。
        /// </summary>
        public Bitmap RawCaptured
        {
            get { return _raw; }
        }

        /// <summary>用户选择的结束方式，决定托盘侧的后续动作。</summary>
        public CaptureResult Result { get; private set; }

        /// <summary>选区左上角对应的屏幕坐标（贴图窗口用它定位）。</summary>
        public Point CaptureLocation { get; private set; }

        /// <summary>自检 / 探针用：直接指定选区（客户区坐标，已锁定）。</summary>
        internal void DebugSelect(Rectangle clientRect)
        {
            _selection = Rectangle.Intersect(
                clientRect,
                new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            if (!HasSelection())
            {
                return;
            }
            _selectionLocked = true;
            PlaceToolbar();
            Invalidate();
        }

        /// <summary>自检 / 探针用：触发工具栏上的「滚动截图」。</summary>
        internal void DebugStartScroll()
        {
            OnScrollClicked(this, EventArgs.Empty);
        }

        /// <summary>当前是不是「滚动截图拼好的长图」在编辑中。</summary>
        internal bool InLongMode
        {
            get { return _longImage != null; }
        }

        /// <summary>自检 / 探针用：长图尺寸（非长图状态返回空）。</summary>
        internal Size LongImageSize
        {
            get
            {
                return _longImage == null
                    ? Size.Empty : _longImage.Size;
            }
        }

        /// <summary>自检 / 探针用：当前纵向滚动偏移。</summary>
        internal int LongScroll
        {
            get { return _longScroll; }
        }

        /// <summary>
        /// 接管一张拼好的长图：浮层保持打开，选区改成 1:1 的取景框，
        /// 之后的一切（标注、保存、复制、贴图、翻译、✓ 完成）都和普通截图一致。
        /// 位图所有权转移给本窗口。
        /// </summary>
        internal void EnterLongMode(Bitmap image)
        {
            if (image == null)
            {
                return;
            }
            if (_longImage != null && !ReferenceEquals(_longImage, image))
            {
                _longImage.Dispose();
            }

            _longImage = image;
            _longScroll = 0;
            _shapes.Clear();
            _undo.Clear();
            _numberCounter = 1;
            _tool = Tool.None;
            _preview = null;
            _selectionLocked = true;

            LayoutLongViewport();

            // 长图已经是最终结果，不允许再滚一次（会截到浮层自己）。
            IconButton scroll = FindButton(IconKind.Scroll);
            if (scroll != null)
            {
                scroll.Enabled = false;
                _tips.SetToolTip(scroll, "长图已拼接完成，可以直接标注或点 ✓ 完成");
            }

            SyncToolButtons();
            PlaceToolbar();
            Invalidate();
        }

        /// <summary>
        /// 给长图安排取景框：宽度 1:1，高度铺满可用区域，
        /// 超出的部分靠滚轮上下翻。这样标注精度和真实像素完全一致，
        /// 也不会把几千像素的长图压成一条细线。
        /// </summary>
        private void LayoutLongViewport()
        {
            if (_longImage == null)
            {
                return;
            }

            int marginX = 48;
            int topMargin = 46;   // 上方留给「尺寸」标签
            int bottomReserve = 92;  // 下方留给工具栏

            int maxWidth = Math.Max(120, ClientSize.Width - marginX * 2);
            int maxHeight = Math.Max(
                160, ClientSize.Height - topMargin - bottomReserve);

            int width = Math.Min(_longImage.Width, maxWidth);
            int height = Math.Min(_longImage.Height, maxHeight);

            int left = (ClientSize.Width - width) / 2;
            int top = topMargin + Math.Max(0, (maxHeight - height) / 2);
            _selection = new Rectangle(left, top, width, height);

            int maxScroll = MaxLongScroll;
            if (_longScroll > maxScroll)
            {
                _longScroll = maxScroll;
            }
        }

        private int MaxLongScroll
        {
            get
            {
                if (_longImage == null || _selection.Height <= 0)
                {
                    return 0;
                }
                return Math.Max(0, _longImage.Height - _selection.Height);
            }
        }

        /// <summary>自检 / 探针用：纵向最多能滚多少像素。</summary>
        internal int LongScrollRange
        {
            get { return MaxLongScroll; }
        }

        /// <summary>滚长图（像素）。返回是否真的动了，供探针断言。</summary>
        internal bool ScrollLongBy(int delta)
        {
            if (_longImage == null)
            {
                return false;
            }
            int next = _longScroll + delta;
            next = Math.Max(0, Math.Min(MaxLongScroll, next));
            if (next == _longScroll)
            {
                return false;
            }
            _longScroll = next;
            Invalidate();
            return true;
        }

        /// <summary>自检 / 探针用：按当前标注导出一张成品（调用方释放）。</summary>
        internal Bitmap DebugBuildResult()
        {
            return BuildResult();
        }

        /// <summary>自检 / 探针用：模拟一格滚轮（正数向上）。</summary>
        internal void DebugWheel(int delta)
        {
            OnMouseWheel(new MouseEventArgs(
                MouseButtons.None, 0, 0, 0, delta));
        }

        /// <summary>自检 / 探针用：按「长图坐标」加一个矩形标注。</summary>
        internal void DebugAddFrame(Rectangle imageRect, Color color)
        {
            FrameShape frame = new FrameShape();
            frame.Rect = imageRect;
            frame.Ellipse = false;
            frame.Color = color;
            frame.Width = 4f;
            _shapes.Add(frame);
            Invalidate();
        }

        /// <summary>
        /// 客户区坐标 → 图像坐标。普通截图两者相同；长图模式下
        /// 还要扣掉取景框原点、加上纵向滚动量。
        /// </summary>
        private Point ToImagePoint(Point client)
        {
            if (_longImage == null)
            {
                return client;
            }
            return new Point(
                client.X - _selection.Left,
                client.Y - _selection.Top + _longScroll);
        }

        /// <summary>图像坐标 → 客户区坐标（文本框这类真实控件要用）。</summary>
        private Point ToClientPoint(Point image)
        {
            if (_longImage == null)
            {
                return image;
            }
            return new Point(
                image.X + _selection.Left,
                image.Y + _selection.Top - _longScroll);
        }

        /// <summary>自检 / 探针用：把工具栏所有图标放大画成一张对照图。</summary>
        internal static Bitmap RenderIconStripForTest(int scale)
        {
            IconKind[] kinds = (IconKind[])Enum.GetValues(
                typeof(IconKind));
            if (scale < 1)
            {
                scale = 1;
            }
            int cell = 34 * scale;
            int perRow = 9;
            int rows = (kinds.Length + perRow - 1) / perRow;
            Bitmap strip = new Bitmap(
                perRow * cell + 8, rows * (cell + 18) + 8);
            using (Graphics g = Graphics.FromImage(strip))
            using (Font font = new Font(
                "Microsoft YaHei UI", 9f, FontStyle.Regular,
                GraphicsUnit.Pixel))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < kinds.Length; i++)
                {
                    Rectangle box = new Rectangle(
                        4 + (i % perRow) * cell,
                        4 + (i / perRow) * (cell + 18),
                        cell, cell);
                    try
                    {
                        using (Bitmap one = new Bitmap(cell, cell))
                        {
                            using (Graphics og = Graphics.FromImage(one))
                            {
                                og.Clear(ToolbarBack);
                                DrawIcon(og, kinds[i],
                                    new Rectangle(0, 0, cell, cell),
                                    IconInk, 2f);
                            }
                            g.DrawImage(one, box);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("渲染图标失败：" + kinds[i], ex);
                    }
                    TextRenderer.DrawText(
                        g, kinds[i].ToString(), font,
                        new Point(box.X, box.Bottom + 2),
                        Color.Black);
                }
            }
            return strip;
        }

        /// <summary>自检 / 探针用：当前选区对应的屏幕矩形。</summary>
        internal Rectangle CaptureArea
        {
            get
            {
                if (!HasSelection())
                {
                    return Rectangle.Empty;
                }
                return new Rectangle(
                    PointToScreen(_selection.Location), _selection.Size);
            }
        }

        /// <summary>
        /// Finish() 中构建结果位图 + 关闭浮层的耗时（毫秒），
        /// 供托盘侧统计「点按钮 → 剪贴板可用」的端到端耗时。
        /// </summary>
        public long FinishElapsedMs { get; private set; }

        // ------------------------------------------------------------------
        // 初始化
        // ------------------------------------------------------------------

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

        private void CaptureFullScreen()
        {
            Rectangle bounds = GetVirtualBounds();
            _screen = new Bitmap(bounds.Width, bounds.Height,
                PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_screen))
            {
                g.CopyFromScreen(
                    bounds.Left, bounds.Top, 0, 0, _screen.Size);
            }

            // 遮罩预合成为一张图，拖拽时只需 DrawImage 一次，
            // 不再每帧重画半透明层（旧版闪烁的主要来源）。
            _dimmed = new Bitmap(_screen.Width, _screen.Height,
                PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_dimmed))
            {
                g.DrawImage(_screen, 0, 0);
                using (SolidBrush brush = new SolidBrush(
                    Color.FromArgb(105, 12, 16, 24)))
                {
                    g.FillRectangle(brush,
                        0, 0, _dimmed.Width, _dimmed.Height);
                }
            }
        }

        private ToolbarPanel BuildToolbar()
        {
            ToolbarPanel panel = new ToolbarPanel();
            panel.Height = 42;
            panel.Padding = new Padding(4);

            int x = 5;
            AddToolButton(panel, ref x, IconKind.Rect,
                "矩形框", Tool.Rect);
            AddToolButton(panel, ref x, IconKind.Ellipse,
                "椭圆框", Tool.Ellipse);
            AddToolButton(panel, ref x, IconKind.Arrow,
                "箭头", Tool.Arrow);
            AddToolButton(panel, ref x, IconKind.Pen,
                "画笔", Tool.Pen);
            AddToolButton(panel, ref x, IconKind.Text,
                "文字（在选区内单击后输入）", Tool.Text);
            AddToolButton(panel, ref x, IconKind.Number,
                "序号标记", Tool.Number);
            AddToolButton(panel, ref x, IconKind.Highlight,
                "高亮", Tool.Highlight);
            AddToolButton(panel, ref x, IconKind.Mosaic,
                "马赛克", Tool.Mosaic);
            x = AddSeparator(panel, x);

            AddActionButton(panel, ref x, IconKind.Undo,
                "撤销 (Ctrl+Z)", OnUndoClicked);
            AddActionButton(panel, ref x, IconKind.Crop,
                "裁剪选区（在选区内拖出新范围）", OnCropClicked);
            AddActionButton(panel, ref x, IconKind.Scroll,
                "滚动截图（长截图）：点一下，然后拨鼠标滚轮滚动页面",
                OnScrollClicked);
            x = AddSeparator(panel, x);

            AddActionButton(panel, ref x, IconKind.Color,
                "标注颜色", OnColorClicked);
            AddActionButton(panel, ref x, IconKind.Width,
                "线条粗细", OnWidthClicked);
            x = AddSeparator(panel, x);

            AddActionButton(panel, ref x, IconKind.Save,
                "保存为 PNG", OnSaveClicked);
            AddActionButton(panel, ref x, IconKind.Pin,
                "钉在桌面（贴图）", OnPinClicked);
            AddActionButton(panel, ref x, IconKind.Translate,
                "翻译图片（译文直接显示在图上，可选中复制）", OnTranslateClicked);
            AddActionButton(panel, ref x, IconKind.Ocr,
                "只识别文字（不翻译，识别后可在图上自己选字复制）", OnOcrClicked);
            AddActionButton(panel, ref x, IconKind.Cancel,
                "取消 (Esc)", OnCancelClicked);
            AddActionButton(panel, ref x, IconKind.Done,
                "完成（复制图片到剪贴板，Esc 取消）", OnDoneClicked);

            panel.Width = x + 5;
            return panel;
        }

        private void AddToolButton(
            ToolbarPanel panel, ref int x,
            IconKind kind, string tip, Tool tool)
        {
            IconButton button = new IconButton(kind, tip, _tips);
            button.Location = new Point(x, 4);
            button.Tag = tool;
            button.Click += OnToolButtonClicked;
            panel.Controls.Add(button);
            _buttons.Add(button);
            x += button.Width + 1;
        }

        private void AddActionButton(
            ToolbarPanel panel, ref int x,
            IconKind kind, string tip, EventHandler handler)
        {
            IconButton button = new IconButton(kind, tip, _tips);
            button.Location = new Point(x, 4);
            button.Click += handler;
            panel.Controls.Add(button);
            _buttons.Add(button);
            if (kind == IconKind.Color)
            {
                button.Swatch = _color;
            }
            else if (kind == IconKind.Width)
            {
                button.Stroke = _stroke;
            }
            x += button.Width + 1;
        }

        private static int AddSeparator(ToolbarPanel panel, int x)
        {
            Panel line = new Panel();
            line.BackColor = SeparatorColor;
            line.Size = new Size(1, 22);
            line.Location = new Point(x + 4, 10);
            panel.Controls.Add(line);
            return x + 11;
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 双缓冲下禁止擦背景，避免闪白。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (_dimmed == null)
            {
                g.Clear(Color.Black);
                return;
            }

            g.DrawImage(_dimmed, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool hasSelection = _selection.Width > 1 &&
                _selection.Height > 1;

            if (hasSelection)
            {
                Rectangle visible = Rectangle.Intersect(
                    _selection,
                    new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));

                // 选区内恢复原图亮度，并把标注裁剪在选区内。
                GraphicsState state = g.Save();
                g.SetClip(visible, CombineMode.Replace);
                if (_longImage != null)
                {
                    DrawLongImage(g);
                }
                else
                {
                    g.DrawImage(_screen, 0, 0);
                    for (int i = 0; i < _shapes.Count; i++)
                    {
                        _shapes[i].Draw(g, _screen);
                    }
                    if (_preview != null)
                    {
                        _preview.Draw(g, _screen);
                    }
                }
                g.Restore(state);

                DrawSelectionChrome(g);
            }
            else if (!_hoverRect.IsEmpty)
            {
                DrawHoverWindow(g);
            }
            else
            {
                DrawHint(g);
            }
        }

        /// <summary>
        /// 画长图的当前可见片段（1:1），并把原点平移到「长图坐标」，
        /// 后面的标注就是按真实像素落笔，导出时不需要任何缩放。
        /// </summary>
        private void DrawLongImage(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(
                _longImage,
                _selection,
                0,
                _longScroll,
                _selection.Width,
                _selection.Height,
                GraphicsUnit.Pixel);

            g.TranslateTransform(
                _selection.Left, _selection.Top - _longScroll);
            for (int i = 0; i < _shapes.Count; i++)
            {
                _shapes[i].Draw(g, _longImage);
            }
            if (_preview != null)
            {
                _preview.Draw(g, _longImage);
            }
            g.TranslateTransform(
                -_selection.Left, -(_selection.Top - _longScroll));
        }

        /// <summary>
        /// 悬停高亮：把鼠标底下那个窗口按原样画亮，描一圈蓝边，
        /// 并在左上角标出尺寸和「单击截取此窗口」。
        /// </summary>
        private void DrawHoverWindow(Graphics g)
        {
            Rectangle rect = _hoverRect;
            if (rect.Width < 2 || rect.Height < 2)
            {
                return;
            }

            Rectangle visible = Rectangle.Intersect(
                rect, new Rectangle(Point.Empty, ClientSize));
            if (visible.Width < 1 || visible.Height < 1)
            {
                return;
            }

            GraphicsState state = g.Save();
            g.SetClip(visible, CombineMode.Replace);
            g.DrawImage(_screen, 0, 0);
            g.Restore(state);

            using (Pen pen = new Pen(AccentColor, 2f))
            {
                g.DrawRectangle(pen, rect.X, rect.Y,
                    rect.Width - 1, rect.Height - 1);
            }

            string size = rect.Width + " × " + rect.Height;
            string tip = "单击截取此窗口";
            using (Font font = new Font(
                "Microsoft YaHei UI", 11f, FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat())
            using (SolidBrush back = new SolidBrush(
                Color.FromArgb(235, 26, 32, 44)))
            using (SolidBrush fore = new SolidBrush(Color.White))
            using (SolidBrush dim = new SolidBrush(
                Color.FromArgb(180, 255, 255, 255)))
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                SizeF sizeText = g.MeasureString(size, font);
                SizeF tipText = g.MeasureString(tip, font);
                float w = sizeText.Width + tipText.Width + 28f;
                float h = 24f;
                float x = rect.X;
                float y = rect.Y - h - 6f;
                if (y < 2f)
                {
                    y = rect.Y + 6f;
                }
                x = Math.Max(2f, Math.Min(x, ClientSize.Width - w - 2f));
                RectangleF label = new RectangleF(x, y, w, h);
                g.FillRectangle(back, label);
                g.DrawString(size, font, fore,
                    new RectangleF(label.X + 9f, label.Y,
                        sizeText.Width + 4f, h), format);
                g.DrawString(tip, font, dim,
                    new RectangleF(label.X + 9f + sizeText.Width + 6f,
                        label.Y, tipText.Width + 4f, h), format);
            }
        }

        private void DrawSelectionChrome(Graphics g)
        {
            using (Pen pen = new Pen(AccentColor, 1.5f))
            {
                g.DrawRectangle(pen, _selection);
            }

            // 8 个白色方块手柄，蓝色描边。长图模式下取景框大小由画面决定，
            // 不给手柄，免得用户以为能像裁剪那样改尺寸。
            if (_longImage == null)
            {
                Point[] handles = HandlePoints(_selection);
                using (SolidBrush fill = new SolidBrush(Color.White))
                using (Pen border = new Pen(AccentColor, 1.4f))
                {
                    for (int i = 0; i < handles.Length; i++)
                    {
                        Rectangle box = new Rectangle(
                            handles[i].X - 4, handles[i].Y - 4, 8, 8);
                        g.FillRectangle(fill, box);
                        g.DrawRectangle(border, box);
                    }
                }
            }

            // 尺寸标签：深色底 + 白字，位于选区左上角上方。
            // 长图模式显示的是长图真实尺寸（不是取景框尺寸）。
            string text = _longImage == null
                ? _selection.Width + " × " + _selection.Height
                : _longImage.Width + " × " + _longImage.Height +
                    " 长图" +
                    (MaxLongScroll > 0 ? " · 滚轮上下看" : "");
            using (Font font = new Font(
                "Microsoft YaHei UI", 11f, FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat())
            using (SolidBrush back = new SolidBrush(
                Color.FromArgb(225, 32, 38, 50)))
            using (SolidBrush fore = new SolidBrush(Color.White))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                SizeF measured = g.MeasureString(text, font);
                float w = Math.Max(58f, measured.Width + 14f);
                float h = measured.Height + 8f;
                float x = _selection.Left;
                float y = _selection.Top - h - 8f;
                if (y < 2)
                {
                    y = _selection.Top + 8f;
                }
                x = Math.Max(2f,
                    Math.Min(x, ClientSize.Width - w - 2f));
                RectangleF label = new RectangleF(x, y, w, h);
                g.FillRectangle(back, label);
                g.DrawString(text, font, fore, label, format);
            }

            // 裁剪模式提示：不用弹窗打断操作，直接画在选区下方。
            if (_tool == Tool.Crop)
            {
                const string cropTip =
                    "裁剪模式：在选区内拖出新范围 · 再次点击裁剪按钮退出";
                using (Font font = new Font(
                    "Microsoft YaHei UI", 12f, FontStyle.Regular,
                    GraphicsUnit.Pixel))
                using (SolidBrush back = new SolidBrush(
                    Color.FromArgb(215, 32, 38, 50)))
                using (SolidBrush fore = new SolidBrush(Color.White))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    SizeF measured = g.MeasureString(cropTip, font);
                    float w = measured.Width + 20f;
                    float h = measured.Height + 10f;
                    float x = Math.Max(2f, Math.Min(
                        _selection.Left, ClientSize.Width - w - 2f));
                    float y = _selection.Bottom + 58f;
                    if (y + h > ClientSize.Height - 2)
                    {
                        y = _selection.Top - h - 6f;
                    }
                    RectangleF banner = new RectangleF(x, y, w, h);
                    g.FillRectangle(back, banner);
                    g.DrawString(cropTip, font, fore, banner, format);
                }
            }

            // 长图模式：右内侧一条细滚动条，提示「上面 / 下面还有内容」。
            if (_longImage != null && MaxLongScroll > 0)
            {
                DrawLongScrollBar(g);
            }
        }

        private void DrawLongScrollBar(Graphics g)
        {
            const int trackWidth = 6;
            Rectangle track = new Rectangle(
                _selection.Right - trackWidth - 3,
                _selection.Top + 4,
                trackWidth,
                Math.Max(12, _selection.Height - 8));
            using (SolidBrush back = new SolidBrush(
                Color.FromArgb(105, 0, 0, 0)))
            {
                g.FillRectangle(back, track);
            }

            float ratio = (float)_selection.Height / _longImage.Height;
            int thumbHeight = (int)Math.Round(track.Height * ratio);
            if (thumbHeight < 28)
            {
                thumbHeight = 28;
            }
            if (thumbHeight > track.Height)
            {
                thumbHeight = track.Height;
            }
            int travel = track.Height - thumbHeight;
            int top = travel <= 0 ? track.Top : track.Top +
                (int)Math.Round(
                    travel * (double)_longScroll / MaxLongScroll);
            using (SolidBrush fore = new SolidBrush(
                Color.FromArgb(205, 255, 255, 255)))
            {
                g.FillRectangle(
                    fore, new Rectangle(track.X, top, track.Width, thumbHeight));
            }
        }

        private void DrawHint(Graphics g)
        {
            const string line1 = "拖动框选区域，或直接单击一个窗口截整窗";
            const string line2 = "左键 / 右键均可 · Esc 取消";
            using (Font font = new Font(
                "Microsoft YaHei UI", 15f, FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (Font small = new Font(
                "Microsoft YaHei UI", 12f, FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (SolidBrush back = new SolidBrush(
                Color.FromArgb(205, 24, 30, 42)))
            using (SolidBrush fore = new SolidBrush(Color.White))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                SizeF s1 = g.MeasureString(line1, font);
                SizeF s2 = g.MeasureString(line2, small);
                float w = Math.Max(s1.Width, s2.Width) + 52f;
                float h = s1.Height + s2.Height + 40f;
                RectangleF box = new RectangleF(
                    (ClientSize.Width - w) / 2f,
                    (ClientSize.Height - h) / 2f, w, h);
                g.FillRectangle(back, box);
                g.DrawString(line1, font, fore,
                    new RectangleF(box.X, box.Y + 12, box.Width, s1.Height),
                    format);
                g.DrawString(line2, small, fore,
                    new RectangleF(box.X, box.Y + 18 + s1.Height,
                        box.Width, s2.Height), format);
            }
        }

        private static Point[] HandlePoints(Rectangle r)
        {
            int cx = r.Left + r.Width / 2;
            int cy = r.Top + r.Height / 2;
            return new Point[]
            {
                new Point(r.Left, r.Top),
                new Point(cx, r.Top),
                new Point(r.Right, r.Top),
                new Point(r.Right, cy),
                new Point(r.Right, r.Bottom),
                new Point(cx, r.Bottom),
                new Point(r.Left, r.Bottom),
                new Point(r.Left, cy)
            };
        }

        // ------------------------------------------------------------------
        // 鼠标交互
        // ------------------------------------------------------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left &&
                e.Button != MouseButtons.Right)
            {
                return;
            }

            RemoveTextInput(true);

            // 选区已锁定后，右键不再「重新框选」，只退出当前标注工具。
            // 这是用户反馈的核心问题：一次快捷键只应该产出一张截图。
            if (_selectionLocked && e.Button == MouseButtons.Right)
            {
                if (_tool != Tool.None)
                {
                    _tool = Tool.None;
                    SyncToolButtons();
                }
                UpdateCursor(new Point(e.X, e.Y));
                return;
            }

            _dragButton = e.Button;
            _dragStart = new Point(e.X, e.Y);
            _lastPoint = _dragStart;
            _dragOrigin = _selection;
            _handle = _longImage == null
                ? HitHandle(_dragStart) : HandleKind.None;

            // 首次框选时左右键都可以；锁定后右键已在上面拦截，
            // 左键只允许改手柄 / 移动 / 标注 / 裁剪，不允许开新选区。
            if (_handle != HandleKind.None)
            {
                _dragMode = DragMode.Resize;
                PushUndo();
            }
            else if (_tool == Tool.Crop && HasSelection() &&
                _longImage == null)
            {
                _dragMode = DragMode.Select;
                PushUndo();
            }
            else if (HasSelection() && _tool != Tool.None &&
                _selection.Contains(_dragStart))
            {
                BeginDraw(ToImagePoint(_dragStart));
            }
            else if (_tool == Tool.None && HasSelection() &&
                _longImage == null && _selection.Contains(_dragStart))
            {
                _dragMode = DragMode.Move;
                PushUndo();
            }
            else if (_selectionLocked)
            {
                // 选区已定：点选区外不再触发第二次框选。
                _dragMode = DragMode.None;
                _dragButton = MouseButtons.None;
                _handle = HandleKind.None;
                return;
            }
            else
            {
                _dragMode = DragMode.Select;
                _shapes.Clear();
                _numberCounter = 1;
                _selection = new Rectangle(e.X, e.Y, 1, 1);
                _toolbar.Visible = false;
                // 开始拖框后就不再显示窗口高亮，避免两层框叠在一起。
                _hoverWindow = null;
                _hoverRect = Rectangle.Empty;
                _hoverPoint = new Point(int.MinValue, int.MinValue);
            }

            // 文字 / 序号是「单击即完成」，不需要鼠标捕获；否则
            // MouseUp 里会提前 return，导致 Capture 一直不释放。
            if (_dragMode != DragMode.None)
            {
                Capture = true;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            Point current = new Point(
                Math.Max(0, Math.Min(ClientSize.Width - 1, e.X)),
                Math.Max(0, Math.Min(ClientSize.Height - 1, e.Y)));

            if (_dragMode == DragMode.None)
            {
                // 长图模式下画面上没有「窗口」可选，别再做窗口高亮。
                if (_longImage == null)
                {
                    UpdateHoverWindow(current);
                }
                UpdateCursor(current);
                return;
            }

            switch (_dragMode)
            {
                case DragMode.Select:
                    _selection = Normalize(_dragStart, current);
                    break;

                case DragMode.Move:
                    MoveSelection(current);
                    break;

                case DragMode.Resize:
                    ResizeSelection(current);
                    break;

                case DragMode.Draw:
                    UpdateDraw(ToImagePoint(current));
                    break;
            }

            _lastPoint = current;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_dragMode == DragMode.None)
            {
                return;
            }

            Capture = false;

            if (_dragMode == DragMode.Draw && _preview != null)
            {
                // 太小的拖拽（单击）不生成形状；画笔至少留两个点。
                if (IsUsable(_preview))
                {
                    _shapes.Add(_preview);
                }
                _preview = null;
            }

            if (_dragMode == DragMode.Select)
            {
                if (_selection.Width < 10 || _selection.Height < 10)
                {
                    if (_dragButton == MouseButtons.Right &&
                        _dragOrigin.Width >= 10 &&
                        _dragOrigin.Height >= 10)
                    {
                        // 右键只是点一下、没有拖动：保留原选区与标注，
                        // 仅退出当前工具（常见截图工具的右键语义）。
                        _selection = _dragOrigin;
                        if (_undo.Count > 0)
                        {
                            _shapes = _undo.Pop();
                        }
                        _tool = Tool.None;
                        SyncToolButtons();
                    }
                    else if (_tool == Tool.None && TryPickHoveredWindow())
                    {
                        // 单击某个窗口 → 直接把整个窗口作为选区，
                        // 不用再沿着窗口边缘一点点圈。
                    }
                    else
                    {
                        _selection = Rectangle.Empty;
                    }
                }
                else if (_tool == Tool.Crop)
                {
                    // 裁剪：新选区取拖拽框与旧选区的交集。
                    _selection = Rectangle.Intersect(
                        _selection, _dragOrigin);
                    _tool = Tool.None;
                    SyncToolButtons();
                }
                // 松开鼠标即锁定选区：本次会话不再接受新的框选。
                if (HasSelection())
                {
                    _selectionLocked = true;
                }
                PlaceToolbar();
            }
            else if (_dragMode == DragMode.Move ||
                _dragMode == DragMode.Resize)
            {
                PlaceToolbar();
            }

            _dragMode = DragMode.None;
            _handle = HandleKind.None;
            _dragButton = MouseButtons.None;
            // 立刻刷新光标，不等下一次 MouseMove：松开右键后就该变回箭头。
            UpdateCursor(PointToClient(MousePosition));
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (HasSelection())
            {
                Finish(CaptureResult.Copy);
            }
        }

        /// <summary>
        /// 长图模式下滚轮就是「上下翻长图」：一屏装不下的部分靠它看，
        /// Shift 按住滚得更快。普通截图不响应滚轮（免得误操作）。
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (_longImage == null || MaxLongScroll <= 0)
            {
                return;
            }

            int step = (ModifierKeys & Keys.Shift) != 0 ? 288 : 96;
            int notches = e.Delta / 120;
            if (notches == 0)
            {
                notches = e.Delta > 0 ? 1 : -1;
            }
            ScrollLongBy(-notches * step);
        }

        private bool HasSelection()
        {
            return _selection.Width >= 10 && _selection.Height >= 10;
        }

        /// <summary>
        /// 刷新「鼠标底下的窗口」高亮。只在还没框选、也没在标注时生效，
        /// 每次鼠标移动最多重新枚举一次窗口（同一点直接跳过）。
        /// </summary>
        private void UpdateHoverWindow(Point client)
        {
            if (_hoverPoint == client)
            {
                return;
            }
            _hoverPoint = client;

            if (_selectionLocked || _tool != Tool.None)
            {
                ClearHoverWindow();
                return;
            }

            WindowTarget target = WindowPicker.PickAt(PointToScreen(client));
            Rectangle rect = Rectangle.Empty;
            if (target != null)
            {
                Point origin = PointToClient(
                    new Point(target.Bounds.Left, target.Bounds.Top));
                rect = WindowPicker.Clamp(
                    new Rectangle(origin, target.Bounds.Size),
                    new Rectangle(Point.Empty, ClientSize));
            }

            bool sameTarget = target == null
                ? _hoverWindow == null
                : (_hoverWindow != null &&
                    _hoverWindow.Handle == target.Handle);
            if (sameTarget && rect == _hoverRect)
            {
                return;
            }

            _hoverWindow = target;
            _hoverRect = rect;
            Invalidate();
        }

        private void ClearHoverWindow()
        {
            _hoverPoint = new Point(int.MinValue, int.MinValue);
            if (_hoverWindow == null && _hoverRect.IsEmpty)
            {
                return;
            }
            _hoverWindow = null;
            _hoverRect = Rectangle.Empty;
            Invalidate();
        }

        /// <summary>鼠标底下的窗口是否可以直接整窗截取。</summary>
        private Rectangle HoverWindowRect()
        {
            if (_hoverWindow == null || _hoverRect.IsEmpty)
            {
                return Rectangle.Empty;
            }
            return WindowPicker.IsUsable(_hoverRect)
                ? _hoverRect : Rectangle.Empty;
        }

        /// <summary>单击命中窗口时，把选区设成整窗。返回是否命中。</summary>
        private bool TryPickHoveredWindow()
        {
            WindowTarget target = WindowPicker.PickAt(
                PointToScreen(_dragStart));
            if (target == null)
            {
                return false;
            }

            Point origin = PointToClient(
                new Point(target.Bounds.Left, target.Bounds.Top));
            Rectangle rect = WindowPicker.Clamp(
                new Rectangle(origin, target.Bounds.Size),
                new Rectangle(Point.Empty, ClientSize));
            if (!WindowPicker.IsUsable(rect))
            {
                return false;
            }

            _selection = rect;
            _hoverWindow = null;
            _hoverRect = Rectangle.Empty;
            _hoverPoint = new Point(int.MinValue, int.MinValue);
            return true;
        }

        private void MoveSelection(Point current)
        {
            int dx = current.X - _lastPoint.X;
            int dy = current.Y - _lastPoint.Y;
            Rectangle moved = _selection;
            moved.Offset(dx, dy);
            moved.X = Math.Max(0,
                Math.Min(moved.X, ClientSize.Width - moved.Width));
            moved.Y = Math.Max(0,
                Math.Min(moved.Y, ClientSize.Height - moved.Height));
            int appliedX = moved.X - _selection.X;
            int appliedY = moved.Y - _selection.Y;
            _selection = moved;
            if (appliedX != 0 || appliedY != 0)
            {
                for (int i = 0; i < _shapes.Count; i++)
                {
                    _shapes[i].Shift(appliedX, appliedY);
                }
            }
        }

        private void ResizeSelection(Point current)
        {
            int left = _dragOrigin.Left;
            int top = _dragOrigin.Top;
            int right = _dragOrigin.Right;
            int bottom = _dragOrigin.Bottom;

            switch (_handle)
            {
                case HandleKind.TopLeft:
                    left = current.X; top = current.Y; break;
                case HandleKind.Top:
                    top = current.Y; break;
                case HandleKind.TopRight:
                    right = current.X; top = current.Y; break;
                case HandleKind.Right:
                    right = current.X; break;
                case HandleKind.BottomRight:
                    right = current.X; bottom = current.Y; break;
                case HandleKind.Bottom:
                    bottom = current.Y; break;
                case HandleKind.BottomLeft:
                    left = current.X; bottom = current.Y; break;
                case HandleKind.Left:
                    left = current.X; break;
            }

            Rectangle r = Normalize(
                new Point(left, top), new Point(right, bottom));
            r = Rectangle.Intersect(r,
                new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            if (r.Width < 10)
            {
                r.Width = 10;
            }
            if (r.Height < 10)
            {
                r.Height = 10;
            }
            _selection = r;
        }

        private HandleKind HitHandle(Point p)
        {
            if (!HasSelection())
            {
                return HandleKind.None;
            }

            Point[] points = HandlePoints(_selection);
            HandleKind[] kinds = new HandleKind[]
            {
                HandleKind.TopLeft, HandleKind.Top,
                HandleKind.TopRight, HandleKind.Right,
                HandleKind.BottomRight, HandleKind.Bottom,
                HandleKind.BottomLeft, HandleKind.Left
            };
            for (int i = 0; i < points.Length; i++)
            {
                if (Math.Abs(p.X - points[i].X) <= 7 &&
                    Math.Abs(p.Y - points[i].Y) <= 7)
                {
                    return kinds[i];
                }
            }
            return HandleKind.None;
        }

        private void UpdateCursor(Point p)
        {
            if (!HasSelection())
            {
                Cursor = Cursors.Cross;
                return;
            }

            // 长图模式没有手柄，别显示缩放光标。
            switch (_longImage == null ? HitHandle(p) : HandleKind.None)
            {
                case HandleKind.TopLeft:
                case HandleKind.BottomRight:
                    Cursor = Cursors.SizeNWSE; return;
                case HandleKind.TopRight:
                case HandleKind.BottomLeft:
                    Cursor = Cursors.SizeNESW; return;
                case HandleKind.Left:
                case HandleKind.Right:
                    Cursor = Cursors.SizeWE; return;
                case HandleKind.Top:
                case HandleKind.Bottom:
                    Cursor = Cursors.SizeNS; return;
            }

            // 只有正在标注时才用十字 / 工字光标；选区确定后回归普通
            // 鼠标箭头（手柄上仍是缩放光标）。选区内拖动依旧可移动选区。
            if (_tool != Tool.None)
            {
                Cursor = _tool == Tool.Text
                    ? Cursors.IBeam : Cursors.Cross;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }

        // ------------------------------------------------------------------
        // 形状绘制
        // ------------------------------------------------------------------

        private void BeginDraw(Point p)
        {
            PushUndo();
            _drawStart = p;

            if (_tool == Tool.Text)
            {
                ShowTextInput(p);
                _dragMode = DragMode.None;
                return;
            }

            if (_tool == Tool.Number)
            {
                NumberShape number = new NumberShape();
                number.Position = new PointF(p.X, p.Y);
                number.Number = _numberCounter++;
                number.Color = _color;
                number.Width = _stroke;
                _shapes.Add(number);
                _dragMode = DragMode.None;
                Invalidate();
                return;
            }

            _dragMode = DragMode.Draw;
            switch (_tool)
            {
                case Tool.Rect:
                case Tool.Ellipse:
                    FrameShape frame = new FrameShape();
                    frame.Rect = new RectangleF(p.X, p.Y, 0, 0);
                    frame.Ellipse = _tool == Tool.Ellipse;
                    frame.Color = _color;
                    frame.Width = _stroke;
                    _preview = frame;
                    break;

                case Tool.Highlight:
                case Tool.Mosaic:
                    FillShape fill = new FillShape();
                    fill.Rect = new RectangleF(p.X, p.Y, 0, 0);
                    fill.Mosaic = _tool == Tool.Mosaic;
                    fill.Color = _color;
                    fill.Width = _stroke;
                    _preview = fill;
                    break;

                default:
                    StrokeShape stroke = new StrokeShape();
                    stroke.Points.Add(new PointF(p.X, p.Y));
                    stroke.Points.Add(new PointF(p.X, p.Y));
                    stroke.Arrow = _tool == Tool.Arrow;
                    stroke.Color = _color;
                    stroke.Width = _tool == Tool.Pen
                        ? Math.Max(2f, _stroke - 1f) : _stroke;
                    _preview = stroke;
                    break;
            }
        }

        private void UpdateDraw(Point p)
        {
            if (_preview == null)
            {
                return;
            }

            if (_preview is FrameShape)
            {
                FrameShape frame = (FrameShape)_preview;
                frame.Rect = new RectangleF(
                    _drawStart.X, _drawStart.Y,
                    p.X - _drawStart.X, p.Y - _drawStart.Y);
            }
            else if (_preview is FillShape)
            {
                FillShape fill = (FillShape)_preview;
                fill.Rect = new RectangleF(
                    _drawStart.X, _drawStart.Y,
                    p.X - _drawStart.X, p.Y - _drawStart.Y);
            }
            else if (_preview is StrokeShape)
            {
                StrokeShape stroke = (StrokeShape)_preview;
                if (_tool == Tool.Pen)
                {
                    stroke.Points.Add(new PointF(p.X, p.Y));
                }
                else
                {
                    stroke.Points[stroke.Points.Count - 1] =
                        new PointF(p.X, p.Y);
                }
            }
        }

        private static bool IsUsable(Shape shape)
        {
            if (shape is FrameShape)
            {
                RectangleF r = ((FrameShape)shape).Rect;
                return Math.Abs(r.Width) > 3 && Math.Abs(r.Height) > 3;
            }
            if (shape is FillShape)
            {
                RectangleF r = ((FillShape)shape).Rect;
                return Math.Abs(r.Width) > 3 && Math.Abs(r.Height) > 3;
            }
            if (shape is StrokeShape)
            {
                StrokeShape s = (StrokeShape)shape;
                if (s.Points.Count < 2)
                {
                    return false;
                }
                PointF a = s.Points[0];
                PointF b = s.Points[s.Points.Count - 1];
                return Math.Abs(a.X - b.X) > 3 ||
                    Math.Abs(a.Y - b.Y) > 3 ||
                    s.Points.Count > 4;
            }
            return true;
        }

        private void ShowTextInput(Point p)
        {
            RemoveTextInput(false);
            _textInput = new TextBox();
            _textInput.BorderStyle = BorderStyle.FixedSingle;
            _textInput.BackColor = Color.White;
            _textInput.ForeColor = _color;
            _textInput.Font = new Font(
                "Microsoft YaHei UI", Math.Max(13f, _stroke * 3f + 8f),
                FontStyle.Bold, GraphicsUnit.Pixel);
            _textInput.Width = 250;
            Point where = ToClientPoint(p);
            _textInput.Location = new Point(
                Math.Max(0, Math.Min(
                    ClientSize.Width - 250, where.X)),
                Math.Max(0, Math.Min(
                    ClientSize.Height - 30, where.Y - 12)));
            _textInput.KeyDown += OnTextKeyDown;
            _textInput.Leave += OnTextLeave;
            Controls.Add(_textInput);
            _textInput.BringToFront();
            _textInput.Focus();
        }

        private void OnTextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                RemoveTextInput(true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                RemoveTextInput(false);
            }
        }

        private void OnTextLeave(object sender, EventArgs e)
        {
            RemoveTextInput(true);
        }

        private void RemoveTextInput(bool commit)
        {
            if (_textInput == null)
            {
                return;
            }

            TextBox box = _textInput;
            string text = box.Text == null ? "" : box.Text.Trim();
            float size = box.Font.Size;
            Point location = box.Location;
            _textInput = null;

            box.KeyDown -= OnTextKeyDown;
            box.Leave -= OnTextLeave;
            Controls.Remove(box);
            box.Dispose();

            if (commit && text.Length > 0)
            {
                PushUndo();
                TextShape shape = new TextShape();
                shape.Text = text;
                Point imageLocation = ToImagePoint(location);
                shape.Position = new PointF(
                    imageLocation.X, imageLocation.Y);
                shape.Size = size;
                shape.Color = _color;
                shape.Width = _stroke;
                _shapes.Add(shape);
            }
            Invalidate();
        }

        // ------------------------------------------------------------------
        // 撤销 / 工具栏动作
        // ------------------------------------------------------------------

        private void PushUndo()
        {
            List<Shape> snapshot = new List<Shape>();
            for (int i = 0; i < _shapes.Count; i++)
            {
                snapshot.Add(_shapes[i].Clone());
            }
            _undo.Push(snapshot);
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (_undo.Count == 0)
            {
                return;
            }
            _shapes = _undo.Pop();
            _preview = null;
            Invalidate();
        }

        private void OnToolButtonClicked(object sender, EventArgs e)
        {
            IconButton clicked = (IconButton)sender;
            Tool tool = (Tool)clicked.Tag;
            _tool = _tool == tool ? Tool.None : tool;
            SyncToolButtons();
            UpdateCursor(PointToClient(MousePosition));
        }

        private void SyncToolButtons()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Tag is Tool)
                {
                    _buttons[i].Checked =
                        (Tool)_buttons[i].Tag == _tool;
                }
            }
        }

        private void OnCropClicked(object sender, EventArgs e)
        {
            _tool = _tool == Tool.Crop ? Tool.None : Tool.Crop;
            SyncToolButtons();
            Invalidate();
        }

        // ------------------------------------------------------------------
        // 滚动截图（长截图）
        // ------------------------------------------------------------------

        /// <summary>
        /// 点工具栏的「滚动截图」后：浮层先藏起来（否则拍到的是自己的
        /// 冻结画面），把焦点还给原来的窗口，然后定时抓选区画面，
        /// 用「画布底部重叠比对」把新滚出来的内容接在下面。
        ///
        /// 期间用一个只画在选区外侧的边框窗提示「正在截哪一块」；
        /// 结束后浮层原地回来，长图直接当作本次截图继续编辑，
        /// 和普通截图一样点 ✓ 完成（或先标注再完成）。
        /// </summary>
        private void OnScrollClicked(object sender, EventArgs e)
        {
            if (_scrollInProgress || !HasSelection())
            {
                return;
            }

            if (_longImage != null)
            {
                // 长图已经是结果了，再滚一次只会截到浮层自己。
                return;
            }

            Point origin = PointToScreen(_selection.Location);
            Rectangle screenRect = ScrollCaptureSession.ClampToScreen(
                new Rectangle(origin, _selection.Size));
            if (screenRect.Width < 32 || screenRect.Height < 32)
            {
                MessageBox.Show(
                    this,
                    "选区太小了。先框选一块页面内容（比如正文区域），再点滚动截图。",
                    "滚动截图",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _scrollInProgress = true;
            RemoveTextInput(false);
            _tool = Tool.None;
            SyncToolButtons();
            _toolbar.Visible = false;

            ScrollCaptureSession session = null;
            ScrollCaptureBar bar = null;
            ScrollCaptureFrame frame = null;
            ScrollKeyWatcher keys = null;
            Bitmap result = null;
            try
            {
                // 浮层藏起来之后被它盖住的页面才会重画，抓到的才是真内容。
                Hide();
                Application.DoEvents();
                System.Threading.Thread.Sleep(220);
                Application.DoEvents();

                if (_foregroundWindow != IntPtr.Zero &&
                    NativeMethods.IsWindow(_foregroundWindow))
                {
                    NativeMethods.SetForegroundWindow(_foregroundWindow);
                }

                session = new ScrollCaptureSession(screenRect);
                bar = new ScrollCaptureBar(
                    screenRect, session.StitchedHeight);
                // 选区边框：画在选区外侧 3px，所以不会把自己拍进去，
                // 但用户始终能看清「现在截的是这一块」。
                frame = new ScrollCaptureFrame(session.Area);
                frame.Show();
                keys = new ScrollKeyWatcher();

                ScrollCaptureSession active = session;
                ScrollCaptureBar strip = bar;
                session.Changed += delegate
                {
                    strip.Status = active.ReachedLimit
                        ? "已拼接 " + active.StitchedHeight +
                            " px（已到长度上限）"
                        : "已拼接 " + active.StitchedHeight + " px";
                };
                keys.FinishRequested += delegate
                {
                    strip.RequestFinish();
                };
                keys.CancelRequested += delegate
                {
                    strip.RequestCancel();
                };

                if (!session.Start())
                {
                    if (frame != null)
                    {
                        frame.Hide();
                    }
                    Show();
                    PlaceToolbar();
                    _scrollInProgress = false;
                    return;
                }

                if (!bar.PlaceOutsideCapture())
                {
                    Log.Info("滚动截图：选区几乎占满屏幕，" +
                        "悬浮条可能被拍进长图。");
                }

                keys.Start();
                DialogResult choice = bar.ShowDialog();

                session.Stop();
                if (choice == DialogResult.OK)
                {
                    result = session.Finish();
                }

                if (result == null ||
                    result.Height <= screenRect.Height)
                {
                    if (result != null)
                    {
                        result.Dispose();
                        result = null;
                    }
                    // 没拼出东西：把浮层还原，用户可以重新框选或换个窗口。
                    Show();
                    PlaceToolbar();
                    Invalidate();
                    _scrollInProgress = false;
                    MessageBox.Show(
                        this,
                        "这次没有拼接出新内容。\r\n" +
                        "滚动截图需要鼠标滚轮把页面滚起来，" +
                        "并且选区里最好是页面正文，别框到固定不动的区域。",
                        "滚动截图",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("滚动截图失败", ex);
                if (result != null)
                {
                    result.Dispose();
                    result = null;
                }
                if (!IsDisposed && !Visible)
                {
                    Show();
                    PlaceToolbar();
                }
            }
            finally
            {
                if (keys != null)
                {
                    keys.Dispose();
                }
                if (frame != null)
                {
                    frame.Hide();
                    frame.Dispose();
                }
                if (bar != null)
                {
                    bar.Dispose();
                }
                if (session != null)
                {
                    session.Dispose();
                }
                _scrollInProgress = false;
            }

            if (result == null)
            {
                Invalidate();
                return;
            }

            // 长图拼好了：浮层原地回来，把长图当成本次截图继续编辑。
            // 之后的标注 / 保存 / 复制 / 贴图 / 翻译 / ✓ 完成全都是
            // 普通截图那套逻辑，用户不用学第二套界面。
            EnterLongMode(result);

            if (!IsDisposed && !Visible)
            {
                Show();
            }
            Activate();
            Focus();
            PlaceToolbar();
            Invalidate();
        }

        private IconButton FindButton(IconKind kind)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Kind == kind)
                {
                    return _buttons[i];
                }
            }
            return null;
        }

        private void OnColorClicked(object sender, EventArgs e)
        {
            IconButton owner = (IconButton)sender;
            if (_colorMenu == null)
            {
                _colorMenu = new ContextMenuStrip();
                string[] names = new string[]
                {
                    "红色", "橙色", "黄色", "绿色", "蓝色", "深灰"
                };
                for (int i = 0; i < Palette.Length; i++)
                {
                    Color c = Palette[i];
                    Bitmap swatch = new Bitmap(16, 16);
                    using (Graphics g = Graphics.FromImage(swatch))
                    {
                        g.Clear(c);
                        g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
                    }
                    ToolStripMenuItem item = new ToolStripMenuItem(
                        names[i], swatch, OnColorChosen);
                    item.Tag = c;
                    _colorMenu.Items.Add(item);
                }
            }
            _colorMenu.Show(owner, new Point(0, owner.Height + 2));
        }

        private void OnColorChosen(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            _color = (Color)item.Tag;
            IconButton button = FindButton(IconKind.Color);
            if (button != null)
            {
                button.Swatch = _color;
            }
        }

        private void OnWidthClicked(object sender, EventArgs e)
        {
            IconButton owner = (IconButton)sender;
            if (_widthMenu == null)
            {
                _widthMenu = new ContextMenuStrip();
                string[] names =
                    new string[] { "细 (2px)", "中 (4px)", "粗 (8px)" };
                for (int i = 0; i < Widths.Length; i++)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(
                        names[i], null, OnWidthChosen);
                    item.Tag = Widths[i];
                    _widthMenu.Items.Add(item);
                }
            }
            _widthMenu.Show(owner, new Point(0, owner.Height + 2));
        }

        private void OnWidthChosen(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            _stroke = (float)item.Tag;
            IconButton button = FindButton(IconKind.Width);
            if (button != null)
            {
                button.Stroke = _stroke;
            }
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            Finish(CaptureResult.Save);
        }

        private void OnPinClicked(object sender, EventArgs e)
        {
            Finish(CaptureResult.Pin);
        }

        private void OnTranslateClicked(object sender, EventArgs e)
        {
            Finish(CaptureResult.Translate);
        }

        private void OnOcrClicked(object sender, EventArgs e)
        {
            Finish(CaptureResult.Ocr);
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            Finish(CaptureResult.None);
        }

        private void OnDoneClicked(object sender, EventArgs e)
        {
            Finish(CaptureResult.Copy);
        }

        // ------------------------------------------------------------------
        // 键盘
        // ------------------------------------------------------------------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape)
            {
                if (_textInput != null)
                {
                    RemoveTextInput(false);
                    return;
                }
                Finish(CaptureResult.None);
            }
            else if (e.KeyCode == Keys.Enter && _textInput == null)
            {
                if (HasSelection())
                {
                    Finish(CaptureResult.Copy);
                }
            }
            else if (e.Control && e.KeyCode == Keys.Z)
            {
                OnUndoClicked(this, EventArgs.Empty);
            }
            else if (!e.Control && !e.Alt && !e.Shift)
            {
                // 数字键 1-8 快速切换标注工具。
                if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D8)
                {
                    _tool = (Tool)(e.KeyCode - Keys.D1 + 1);
                    SyncToolButtons();
                }
                else if (_longImage != null)
                {
                    // 长图模式：键盘也能翻页，PgDn 直达底部更顺手。
                    if (e.KeyCode == Keys.PageDown)
                    {
                        ScrollLongBy(_selection.Height - 40);
                    }
                    else if (e.KeyCode == Keys.PageUp)
                    {
                        ScrollLongBy(-(_selection.Height - 40));
                    }
                    else if (e.KeyCode == Keys.End)
                    {
                        ScrollLongBy(MaxLongScroll);
                    }
                    else if (e.KeyCode == Keys.Home)
                    {
                        ScrollLongBy(-MaxLongScroll);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 完成 / 导出
        // ------------------------------------------------------------------

        private void PlaceToolbar()
        {
            if (!HasSelection())
            {
                _toolbar.Visible = false;
                return;
            }

            _toolbar.Visible = true;
            int x = Math.Max(4, Math.Min(
                _selection.Left,
                ClientSize.Width - _toolbar.Width - 4));
            int y = _selection.Bottom + 10;
            if (y + _toolbar.Height > ClientSize.Height - 4)
            {
                y = _selection.Top - _toolbar.Height - 10;
            }
            if (y < 4)
            {
                y = Math.Max(4,
                    ClientSize.Height - _toolbar.Height - 4);
            }
            _toolbar.Location = new Point(x, y);
            _toolbar.BringToFront();
        }

        private void Finish(CaptureResult result)
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            Stopwatch finishWatch = Stopwatch.StartNew();

            // 用户可能刚输入完文字就直接点「完成」，此时把文字落图；
            // 取消则不保留。
            RemoveTextInput(result != CaptureResult.None);
            _preview = null;
            _toolbar.Visible = false;

            if (result == CaptureResult.None || !HasSelection())
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            _captured = BuildResult();
            _raw = BuildRawResult();
            if (_captured == null || _raw == null)
            {
                if (_captured != null)
                {
                    _captured.Dispose();
                    _captured = null;
                }
                if (_raw != null)
                {
                    _raw.Dispose();
                    _raw = null;
                }
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            Result = result;
            // 窗体关闭后 PointToScreen 不可用，先把屏幕坐标记下来。
            CaptureLocation = PointToScreen(_selection.Location);

            if (result == CaptureResult.Save)
            {
                SaveToFile(_captured, this);
                _captured.Dispose();
                _captured = null;
                _raw.Dispose();
                _raw = null;
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (result == CaptureResult.Copy)
            {
                TryCopyToClipboard(_captured);
            }

            finishWatch.Stop();
            FinishElapsedMs = finishWatch.ElapsedMilliseconds;

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>只裁切原屏快照，不带任何标注，供图片翻译使用。</summary>
        private Bitmap BuildRawResult()
        {
            if (_longImage != null)
            {
                Bitmap copy = new Bitmap(
                    _longImage.Width, _longImage.Height,
                    PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(copy))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(_longImage, 0, 0);
                }
                return copy;
            }

            Rectangle r = Rectangle.Intersect(
                _selection,
                new Rectangle(0, 0, _screen.Width, _screen.Height));
            if (r.Width < 10 || r.Height < 10)
            {
                return null;
            }

            Bitmap result = new Bitmap(
                r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_screen,
                    new Rectangle(0, 0, r.Width, r.Height),
                    r, GraphicsUnit.Pixel);
            }
            return result;
        }

        private Bitmap BuildResult()
        {
            if (_longImage != null)
            {
                Bitmap longShot = new Bitmap(
                    _longImage.Width, _longImage.Height,
                    PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(longShot))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(_longImage, 0, 0);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // 标注本来就存在长图坐标里，这里不需要任何平移。
                    for (int i = 0; i < _shapes.Count; i++)
                    {
                        _shapes[i].Draw(g, _longImage);
                    }
                }
                return longShot;
            }

            Rectangle r = Rectangle.Intersect(
                _selection,
                new Rectangle(0, 0, _screen.Width, _screen.Height));
            if (r.Width < 10 || r.Height < 10)
            {
                return null;
            }

            Bitmap result = new Bitmap(
                r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_screen,
                    new Rectangle(0, 0, r.Width, r.Height),
                    r, GraphicsUnit.Pixel);

                // Clip 在世界变换为单位矩阵时设置为设备坐标，
                // 之后平移只影响绘制，不影响裁剪范围。
                g.SetClip(
                    new Rectangle(0, 0, r.Width, r.Height),
                    CombineMode.Replace);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-r.Left, -r.Top);
                for (int i = 0; i < _shapes.Count; i++)
                {
                    _shapes[i].Draw(g, _screen);
                }
            }
            return result;
        }

        private static void SaveToFile(
            Bitmap image, IWin32Window owner)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "保存截图";
                dialog.Filter = "PNG 图片 (*.png)|*.png";
                dialog.DefaultExt = "png";
                dialog.FileName = "截图_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    image.Save(dialog.FileName, ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "保存失败：" + ex.Message,
                        "保存截图",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private static void TryCopyToClipboard(Bitmap image)
        {
            try
            {
                // 同时放位图和一份 PNG 文件：聊天框里粘成图，
                // 桌面 / 文件夹里 Ctrl+V 粘成「图片-时间戳.png」。
                ClipboardPasteFiles.WriteBitmap(
                    image, ClipboardWriteMode.Auto);
            }
            catch
            {
                // 剪贴板被其它程序占用时忽略，用户仍可用工具条的「保存」按钮导出。
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            Capture = false;
            if (_textInput != null)
            {
                RemoveTextInput(false);
            }
            // 非「完成/翻译」路径（Esc、Alt+F4、异常关闭）由浮层自己
            // 释放；成功路径的位图所有权交给托盘编辑器。
            if (DialogResult != DialogResult.OK)
            {
                if (_captured != null)
                {
                    _captured.Dispose();
                    _captured = null;
                }
                if (_raw != null)
                {
                    _raw.Dispose();
                    _raw = null;
                }
            }
            // 下拉菜单与 ToolTip 只在窗体真正关闭时统一释放，避免在
            // 菜单 Closed 事件里 Dispose 触发 ModalMenuFilter 异常。
            DisposePopupMenu(ref _colorMenu);
            DisposePopupMenu(ref _widthMenu);
            if (_tips != null)
            {
                _tips.Dispose();
            }
            if (_dimmed != null)
            {
                _dimmed.Dispose();
                _dimmed = null;
            }
            if (_longImage != null)
            {
                _longImage.Dispose();
                _longImage = null;
            }
            if (_screen != null)
            {
                _screen.Dispose();
                _screen = null;
            }
        }

        // ------------------------------------------------------------------
        // 工具方法
        // ------------------------------------------------------------------

        private static void DisposePopupMenu(ref ContextMenuStrip menu)
        {
            if (menu == null)
            {
                return;
            }
            ContextMenuStrip target = menu;
            menu = null;
            try
            {
                target.Hide();
                foreach (ToolStripItem item in target.Items)
                {
                    if (item.Image != null)
                    {
                        item.Image.Dispose();
                    }
                }
                target.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("释放工具栏下拉菜单失败", ex);
            }
        }

        private static RectangleF NormalizeRect(RectangleF r)
        {
            float x = Math.Min(r.X, r.X + r.Width);
            float y = Math.Min(r.Y, r.Y + r.Height);
            return new RectangleF(
                x, y, Math.Abs(r.Width), Math.Abs(r.Height));
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return new Rectangle(
                left, top, right - left, bottom - top);
        }

        // ------------------------------------------------------------------
        // 图标绘制
        // ------------------------------------------------------------------

        /// <summary>
        /// 所有图标都在同一个 18×18 逻辑网格内绘制，线宽 / 留白 / 圆角
        /// 完全一致；再整体缩放贴到按钮中心，视觉上比逐图标手写坐标整齐。
        /// </summary>
        private static void DrawIcon(
            Graphics g, IconKind kind, Rectangle box,
            Color ink, float stroke)
        {
            float size = Math.Min(box.Width, box.Height) - 16f;
            if (size < 8f)
            {
                size = 8f;
            }
            float scale = size / 18f;
            float ox = box.X + (box.Width - size) / 2f;
            float oy = box.Y + (box.Height - size) / 2f;

            GraphicsState state = g.Save();
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (Pen pen = new Pen(ink, 1.55f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (kind)
                {
                    case IconKind.Rect:
                        using (GraphicsPath path = MakeRoundedRect(
                            new RectangleF(2f, 3.5f, 14f, 11f), 1.4f))
                        {
                            g.DrawPath(pen, path);
                        }
                        break;

                    case IconKind.Ellipse:
                        g.DrawEllipse(pen, 2f, 3.5f, 14f, 11f);
                        break;

                    case IconKind.Arrow:
                        g.DrawLine(pen, 2.5f, 15.5f, 15f, 3f);
                        g.DrawLine(pen, 15f, 3f, 8.2f, 3f);
                        g.DrawLine(pen, 15f, 3f, 15f, 9.8f);
                        break;

                    case IconKind.Pen:
                        g.DrawBezier(pen,
                            U(1.8f, 13.5f), U(5f, 2f),
                            U(9.5f, 16f), U(16.2f, 4.5f));
                        using (SolidBrush brush = new SolidBrush(ink))
                        {
                            g.FillEllipse(brush, 14.4f, 2.6f, 3.2f, 3.2f);
                        }
                        break;

                    case IconKind.Text:
                        g.DrawLine(pen, 3f, 3.5f, 15f, 3.5f);
                        g.DrawLine(pen, 9f, 3.5f, 9f, 15f);
                        g.DrawLine(pen, 6f, 15f, 12f, 15f);
                        break;

                    case IconKind.Number:
                        using (SolidBrush brush = new SolidBrush(ink))
                        {
                            g.FillEllipse(brush, 1.5f, 1.5f, 15f, 15f);
                        }
                        DrawUnitGlyph(g, "1", Color.White, 11f,
                            "Segoe UI");
                        break;

                    case IconKind.Highlight:
                        using (GraphicsPath path = MakeRoundedRect(
                            new RectangleF(2f, 2.5f, 14f, 8f), 2f))
                        {
                            g.DrawPath(pen, path);
                        }
                        g.DrawLine(pen, 4f, 11f, 14f, 11f);
                        using (SolidBrush brush = new SolidBrush(
                            Color.FromArgb(150, ink)))
                        {
                            g.FillRectangle(brush, 1.5f, 13.5f, 15f, 3.5f);
                        }
                        break;

                    case IconKind.Mosaic:
                        using (GraphicsPath path = MakeRoundedRect(
                            new RectangleF(1.5f, 1.5f, 15f, 15f), 1.8f))
                        {
                            g.DrawPath(pen, path);
                        }
                        g.DrawLine(pen, 6.5f, 1.5f, 6.5f, 16.5f);
                        g.DrawLine(pen, 11.5f, 1.5f, 11.5f, 16.5f);
                        g.DrawLine(pen, 1.5f, 6.5f, 16.5f, 6.5f);
                        g.DrawLine(pen, 1.5f, 11.5f, 16.5f, 11.5f);
                        using (SolidBrush brush = new SolidBrush(
                            Color.FromArgb(115, ink)))
                        {
                            g.FillRectangle(brush, 2f, 2f, 4f, 4f);
                            g.FillRectangle(brush, 7f, 7f, 4f, 4f);
                            g.FillRectangle(brush, 12f, 12f, 4f, 4f);
                        }
                        break;

                    case IconKind.Undo:
                        g.DrawArc(pen,
                            new RectangleF(2f, 3f, 14f, 12f), 180f, 180f);
                        g.DrawLine(pen, 2.3f, 9f, 7f, 5.5f);
                        g.DrawLine(pen, 2.3f, 9f, 7f, 12.5f);
                        g.DrawLine(pen, 16f, 9f, 16f, 13f);
                        break;

                    case IconKind.Crop:
                        g.DrawLines(pen, new PointF[]
                        {
                            U(5f, 1.5f), U(5f, 13f), U(16.5f, 13f)
                        });
                        g.DrawLines(pen, new PointF[]
                        {
                            U(1.5f, 5f), U(13f, 5f), U(13f, 16.5f)
                        });
                        break;

                    case IconKind.Scroll:
                        // 左边一张「纸」，右边上下双向箭头 = 滚动长图。
                        using (GraphicsPath page = MakeRoundedRect(
                            new RectangleF(1.4f, 1.8f, 9f, 14.4f), 2f))
                        using (SolidBrush fill = new SolidBrush(
                            Color.FromArgb(22, ink)))
                        {
                            g.FillPath(fill, page);
                            g.DrawPath(pen, page);
                        }
                        g.DrawLine(pen, 3.6f, 5.4f, 8.2f, 5.4f);
                        g.DrawLine(pen, 3.6f, 8.4f, 8.2f, 8.4f);
                        g.DrawLine(pen, 3.6f, 11.4f, 6.8f, 11.4f);
                        // 箭头画窄一点、陡一点，1:1 时才不会糊成一团。
                        g.DrawLine(pen, 14.8f, 4.4f, 14.8f, 13.6f);
                        g.DrawLine(pen, 13.2f, 6.6f, 14.8f, 4.4f);
                        g.DrawLine(pen, 16.4f, 6.6f, 14.8f, 4.4f);
                        g.DrawLine(pen, 13.2f, 11.4f, 14.8f, 13.6f);
                        g.DrawLine(pen, 16.4f, 11.4f, 14.8f, 13.6f);
                        break;

                    case IconKind.Color:
                        using (SolidBrush brush = new SolidBrush(ink))
                        using (Pen ring = new Pen(
                            Color.FromArgb(186, 197, 212), 1.1f))
                        {
                            g.FillEllipse(brush, 2f, 2f, 14f, 14f);
                            g.DrawEllipse(ring, 2f, 2f, 14f, 14f);
                        }
                        using (SolidBrush gloss = new SolidBrush(
                            Color.FromArgb(80, 255, 255, 255)))
                        {
                            g.FillEllipse(gloss, 4.6f, 4.4f, 4f, 3f);
                        }
                        break;

                    case IconKind.Width:
                    {
                        float[] widths = new float[] { 1.2f, 2.2f, 3.4f };
                        int active = Array.IndexOf(Widths, stroke);
                        if (active < 0)
                        {
                            active = 0;
                        }
                        for (int i = 0; i < widths.Length; i++)
                        {
                            using (Pen line = new Pen(
                                i == active ? AccentColor : ink,
                                widths[i]))
                            {
                                line.StartCap = LineCap.Round;
                                line.EndCap = LineCap.Round;
                                float y = 4f + i * 5f;
                                g.DrawLine(line, 2f, y, 16f, y);
                            }
                        }
                        break;
                    }

                    case IconKind.Save:
                        g.DrawLine(pen, 9f, 2f, 9f, 11.5f);
                        g.DrawLine(pen, 5f, 8f, 9f, 12f);
                        g.DrawLine(pen, 9f, 12f, 13f, 8f);
                        g.DrawLines(pen, new PointF[]
                        {
                            U(3f, 12f), U(3f, 16f),
                            U(15f, 16f), U(15f, 12f)
                        });
                        break;

                    case IconKind.Pin:
                    {
                        // 倾斜的图钉（通用「钉住」符号），比正立的针身更易辨认。
                        GraphicsState pinState = g.Save();
                        g.TranslateTransform(9f, 9f);
                        g.RotateTransform(38f);
                        g.TranslateTransform(-9f, -9f);

                        using (GraphicsPath cap = MakeRoundedRect(
                            new RectangleF(3f, 1.8f, 12f, 4f), 2f))
                        using (SolidBrush brush = new SolidBrush(ink))
                        {
                            g.FillPath(brush, cap);
                        }
                        g.DrawLine(pen, 9f, 5.8f, 9f, 15f);
                        using (SolidBrush brush = new SolidBrush(ink))
                        {
                            g.FillPolygon(brush, new PointF[]
                            {
                                U(7.7f, 13.5f), U(10.3f, 13.5f),
                                U(9f, 16.6f)
                            });
                        }
                        g.Restore(pinState);
                        break;
                    }

                    case IconKind.Translate:
                        using (GraphicsPath path = MakeRoundedRect(
                            new RectangleF(0.8f, 1.8f, 16.4f, 14.4f), 3.5f))
                        using (SolidBrush brush = new SolidBrush(
                            Color.FromArgb(22, ink)))
                        {
                            g.FillPath(brush, path);
                            g.DrawPath(pen, path);
                        }
                        DrawUnitGlyph(g, "译", ink, 10.5f,
                            "Microsoft YaHei UI");
                        break;

                    case IconKind.Ocr:
                        // 一页带横线的文档，表示「只把文字取出来」。
                        using (GraphicsPath path = MakeRoundedRect(
                            new RectangleF(2f, 1.6f, 14f, 14.8f), 2.4f))
                        using (SolidBrush brush = new SolidBrush(
                            Color.FromArgb(22, ink)))
                        {
                            g.FillPath(brush, path);
                            g.DrawPath(pen, path);
                        }
                        g.DrawLine(pen, 4.8f, 5.4f, 13.2f, 5.4f);
                        g.DrawLine(pen, 4.8f, 8.6f, 13.2f, 8.6f);
                        g.DrawLine(pen, 4.8f, 11.8f, 10.6f, 11.8f);
                        break;

                    case IconKind.Cancel:
                        using (Pen thick = new Pen(ink, 1.9f))
                        {
                            thick.StartCap = LineCap.Round;
                            thick.EndCap = LineCap.Round;
                            g.DrawLine(thick, 6f, 6f, 12f, 12f);
                            g.DrawLine(thick, 12f, 6f, 6f, 12f);
                        }
                        break;

                    case IconKind.Done:
                        using (Pen thick = new Pen(ink, 2.1f))
                        {
                            thick.StartCap = LineCap.Round;
                            thick.EndCap = LineCap.Round;
                            thick.LineJoin = LineJoin.Round;
                            g.DrawLines(thick, new PointF[]
                            {
                                U(4f, 9.2f), U(7.6f, 12.8f),
                                U(14f, 5.2f)
                            });
                        }
                        break;
                }
            }

            g.Restore(state);
        }

        private static PointF U(float x, float y)
        {
            return new PointF(x, y);
        }

        /// <summary>在 18×18 图标网格中居中绘制一个字符。</summary>
        private static void DrawUnitGlyph(
            Graphics g, string text, Color ink,
            float size, string family)
        {
            using (Font font = new Font(
                family, size, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(ink))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(text, font, brush,
                    new RectangleF(-1f, -1f, 20f, 20f), format);
            }
        }

        private static GraphicsPath MakeRoundedRect(
            RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width)
            {
                d = r.Width;
            }
            if (d > r.Height)
            {
                d = r.Height;
            }
            path.AddArc(r.Left, r.Top, d, d, 180f, 90f);
            path.AddArc(r.Right - d, r.Top, d, d, 270f, 90f);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}
