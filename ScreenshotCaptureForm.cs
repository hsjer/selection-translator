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

                if (fill != Color.Empty)
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
                    new Rectangle(0, 0, _screen.Width, _screen.Height));

                // 选区内恢复原图亮度，并把标注裁剪在选区内。
                GraphicsState state = g.Save();
                g.SetClip(visible, CombineMode.Replace);
                g.DrawImage(_screen, 0, 0);
                for (int i = 0; i < _shapes.Count; i++)
                {
                    _shapes[i].Draw(g, _screen);
                }
                if (_preview != null)
                {
                    _preview.Draw(g, _screen);
                }
                g.Restore(state);

                DrawSelectionChrome(g);
            }
            else
            {
                DrawHint(g);
            }
        }

        private void DrawSelectionChrome(Graphics g)
        {
            using (Pen pen = new Pen(AccentColor, 1.5f))
            {
                g.DrawRectangle(pen, _selection);
            }

            // 8 个白色方块手柄，蓝色描边。
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

            // 尺寸标签：深色底 + 白字，位于选区左上角上方。
            string text = _selection.Width + " × " + _selection.Height;
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
        }

        private void DrawHint(Graphics g)
        {
            const string line1 = "拖动鼠标框选要识别的区域";
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
            _handle = HitHandle(_dragStart);

            // 首次框选时左右键都可以；锁定后右键已在上面拦截，
            // 左键只允许改手柄 / 移动 / 标注 / 裁剪，不允许开新选区。
            if (_handle != HandleKind.None)
            {
                _dragMode = DragMode.Resize;
                PushUndo();
            }
            else if (_tool == Tool.Crop && HasSelection())
            {
                _dragMode = DragMode.Select;
                PushUndo();
            }
            else if (HasSelection() && _tool != Tool.None &&
                _selection.Contains(_dragStart))
            {
                BeginDraw(_dragStart);
            }
            else if (_tool == Tool.None && HasSelection() &&
                _selection.Contains(_dragStart))
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
                    UpdateDraw(current);
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

        private bool HasSelection()
        {
            return _selection.Width >= 10 && _selection.Height >= 10;
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

            switch (HitHandle(p))
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
                    _dragStart.X, _dragStart.Y,
                    p.X - _dragStart.X, p.Y - _dragStart.Y);
            }
            else if (_preview is FillShape)
            {
                FillShape fill = (FillShape)_preview;
                fill.Rect = new RectangleF(
                    _dragStart.X, _dragStart.Y,
                    p.X - _dragStart.X, p.Y - _dragStart.Y);
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
            _textInput.Location = new Point(
                Math.Max(0, Math.Min(
                    ClientSize.Width - 250, p.X)),
                Math.Max(0, Math.Min(
                    ClientSize.Height - 30, p.Y - 12)));
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
                shape.Position = new PointF(location.X, location.Y);
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
                Clipboard.SetImage(image);
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
