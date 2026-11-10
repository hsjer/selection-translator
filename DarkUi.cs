using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 图片工具（查看器 / 对话框）共用的深色外观：调色板、字体、圆角路径。
    /// 全部控件自绘，不依赖任何第三方 UI 库，保证 exe 依旧只有一个文件。
    /// </summary>
    internal static class DarkUi
    {
        public static readonly Color Window = Color.FromArgb(22, 23, 26);
        public static readonly Color Surface = Color.FromArgb(29, 31, 35);
        public static readonly Color Card = Color.FromArgb(36, 39, 44);
        public static readonly Color CardHover = Color.FromArgb(50, 54, 61);
        public static readonly Color Sunken = Color.FromArgb(24, 26, 30);
        public static readonly Color Border = Color.FromArgb(57, 61, 69);
        public static readonly Color BorderSoft = Color.FromArgb(44, 47, 54);
        public static readonly Color Text = Color.FromArgb(235, 237, 241);
        public static readonly Color TextDim = Color.FromArgb(151, 157, 167);
        public static readonly Color TextFaint = Color.FromArgb(103, 109, 119);
        public static readonly Color Accent = Color.FromArgb(82, 145, 255);
        public static readonly Color AccentHover = Color.FromArgb(110, 165, 255);
        public static readonly Color AccentSoft = Color.FromArgb(33, 50, 78);
        public static readonly Color Danger = Color.FromArgb(240, 106, 106);

        public static readonly Font FontTitle =
            new Font("Microsoft YaHei UI", 15f, FontStyle.Bold,
                GraphicsUnit.Pixel);
        public static readonly Font FontStrong =
            new Font("Microsoft YaHei UI", 13f, FontStyle.Bold,
                GraphicsUnit.Pixel);
        public static readonly Font FontBody =
            new Font("Microsoft YaHei UI", 13f, FontStyle.Regular,
                GraphicsUnit.Pixel);
        public static readonly Font FontSmall =
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Regular,
                GraphicsUnit.Pixel);
        public static readonly Font FontTiny =
            new Font("Microsoft YaHei UI", 11f, FontStyle.Regular,
                GraphicsUnit.Pixel);
        public static readonly Font FontNumber =
            new Font("Segoe UI", 12f, FontStyle.Regular,
                GraphicsUnit.Pixel);
        public static readonly Font FontHuge =
            new Font("Microsoft YaHei UI", 21f, FontStyle.Bold,
                GraphicsUnit.Pixel);

        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > rect.Width)
            {
                d = rect.Width;
            }
            if (d > rect.Height)
            {
                d = rect.Height;
            }
            if (d <= 0f)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }
            path.AddArc(rect.Left, rect.Top, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void Text2(
            Graphics g, string text, Font font, Color color,
            Rectangle bounds, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, flags);
        }

        public const TextFormatFlags LeftMiddle =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        public const TextFormatFlags LeftMiddleClip =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis;

        public const TextFormatFlags CenterMiddle =
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        public const TextFormatFlags RightMiddle =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        public const TextFormatFlags RightMiddleClip =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis;

        private static PointF P(float x, float y)
        {
            return new PointF(x, y);
        }

        private static PointF Polar(
            float cx, float cy, float radius, float degrees)
        {
            double rad = degrees * Math.PI / 180.0;
            return new PointF(
                cx + (float)(radius * Math.Cos(rad)),
                cy + (float)(radius * Math.Sin(rad)));
        }

        /// <summary>画一段带箭头的圆弧，用于旋转 / 撤销这类「循环」图标。</summary>
        private static void DrawArcArrow(
            Graphics g, Pen pen, RectangleF rect,
            float startAngle, float sweep, float headLength)
        {
            g.DrawArc(pen, rect, startAngle, sweep);

            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float radius = rect.Width / 2f;
            float endAngle = startAngle + sweep;
            PointF end = Polar(cx, cy, radius, endAngle);
            // 切线方向：GDI+ 角度按顺时针增大，逆时针绘制时切线要反过来。
            float tangent = endAngle + (sweep >= 0f ? 90f : -90f);
            PointF a = Polar(
                end.X, end.Y, headLength, tangent + 180f + 42f);
            PointF b = Polar(
                end.X, end.Y, headLength, tangent + 180f - 42f);
            g.DrawLine(pen, end, a);
            g.DrawLine(pen, end, b);
        }

        /// <summary>
        /// 所有图标都画在 18×18 的逻辑网格里，再整体缩放到按钮中心，
        /// 这样线宽、留白、圆角在各按钮之间保持一致。
        /// </summary>
        public static void DrawGlyph(
            Graphics g, GlyphKind kind, Rectangle box,
            Color ink, float stroke)
        {
            float size = Math.Min(box.Width, box.Height) - 12f;
            if (size < 8f)
            {
                size = 8f;
            }
            if (size > 26f)
            {
                size = 26f;
            }
            float scale = size / 18f;
            float ox = box.X + (box.Width - size) / 2f;
            float oy = box.Y + (box.Height - size) / 2f;

            GraphicsState state = g.Save();
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (Pen pen = new Pen(ink, Math.Max(0.6f, stroke / scale)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (kind)
                {
                    case GlyphKind.Fit:
                        // 四角向外的括号 = 适应窗口。
                        g.DrawLines(pen, new PointF[]
                        {
                            P(2.2f, 6.6f), P(2.2f, 2.2f), P(6.6f, 2.2f)
                        });
                        g.DrawLines(pen, new PointF[]
                        {
                            P(15.8f, 6.6f), P(15.8f, 2.2f), P(11.4f, 2.2f)
                        });
                        g.DrawLines(pen, new PointF[]
                        {
                            P(2.2f, 11.4f), P(2.2f, 15.8f), P(6.6f, 15.8f)
                        });
                        g.DrawLines(pen, new PointF[]
                        {
                            P(15.8f, 11.4f), P(15.8f, 15.8f), P(11.4f, 15.8f)
                        });
                        break;

                    case GlyphKind.Actual:
                    {
                        using (GraphicsPath path = Rounded(
                            new RectangleF(3.6f, 3.6f, 10.8f, 10.8f), 2.2f))
                        {
                            g.DrawPath(pen, path);
                        }
                        using (SolidBrush brush = new SolidBrush(ink))
                        {
                            g.FillEllipse(brush, 7.4f, 7.4f, 3.2f, 3.2f);
                        }
                        break;
                    }

                    case GlyphKind.ZoomIn:
                        g.DrawEllipse(pen, 2.6f, 2.6f, 10.4f, 10.4f);
                        g.DrawLine(pen, 11.9f, 11.9f, 15.6f, 15.6f);
                        g.DrawLine(pen, 5.3f, 7.8f, 10.3f, 7.8f);
                        g.DrawLine(pen, 7.8f, 5.3f, 7.8f, 10.3f);
                        break;

                    case GlyphKind.ZoomOut:
                        g.DrawEllipse(pen, 2.6f, 2.6f, 10.4f, 10.4f);
                        g.DrawLine(pen, 11.9f, 11.9f, 15.6f, 15.6f);
                        g.DrawLine(pen, 5.3f, 7.8f, 10.3f, 7.8f);
                        break;

                    case GlyphKind.RotateLeft:
                        DrawArcArrow(g, pen,
                            new RectangleF(3f, 3f, 12f, 12f),
                            250f, -260f, 3.6f);
                        break;

                    case GlyphKind.RotateRight:
                        DrawArcArrow(g, pen,
                            new RectangleF(3f, 3f, 12f, 12f),
                            290f, 260f, 3.6f);
                        break;

                    case GlyphKind.Undo:
                        DrawArcArrow(g, pen,
                            new RectangleF(3f, 3f, 12f, 12f),
                            200f, -250f, 3.6f);
                        break;

                    case GlyphKind.Crop:
                        g.DrawLine(pen, 5.6f, 2.2f, 5.6f, 12.4f);
                        g.DrawLine(pen, 5.6f, 12.4f, 15.8f, 12.4f);
                        g.DrawLine(pen, 2.2f, 5.6f, 12.4f, 5.6f);
                        g.DrawLine(pen, 12.4f, 5.6f, 12.4f, 15.8f);
                        break;

                    case GlyphKind.Copy:
                    {
                        using (GraphicsPath back = Rounded(
                            new RectangleF(2.4f, 2.4f, 9.4f, 9.4f), 1.8f))
                        {
                            g.DrawPath(pen, back);
                        }
                        using (GraphicsPath front = Rounded(
                            new RectangleF(6.4f, 6.4f, 9.2f, 9.2f), 1.8f))
                        {
                            g.DrawPath(pen, front);
                        }
                        break;
                    }

                    case GlyphKind.Save:
                        g.DrawLine(pen, 9f, 2.4f, 9f, 11.2f);
                        g.DrawLines(pen, new PointF[]
                        {
                            P(5.8f, 8.2f), P(9f, 11.4f), P(12.2f, 8.2f)
                        });
                        g.DrawLine(pen, 3.4f, 15.2f, 14.6f, 15.2f);
                        break;

                    case GlyphKind.Prev:
                        g.DrawLine(pen, 14.2f, 9f, 4.2f, 9f);
                        g.DrawLines(pen, new PointF[]
                        {
                            P(7.6f, 5.4f), P(4f, 9f), P(7.6f, 12.6f)
                        });
                        break;

                    case GlyphKind.Next:
                        g.DrawLine(pen, 3.8f, 9f, 13.8f, 9f);
                        g.DrawLines(pen, new PointF[]
                        {
                            P(10.4f, 5.4f), P(14f, 9f), P(10.4f, 12.6f)
                        });
                        break;

                    case GlyphKind.Open:
                        g.DrawLines(pen, new PointF[]
                        {
                            P(2.2f, 14.4f), P(2.2f, 4.2f), P(7f, 4.2f),
                            P(8.6f, 6.4f), P(15.8f, 6.4f), P(15.8f, 14.4f)
                        });
                        g.DrawLine(pen, 2.2f, 14.4f, 15.8f, 14.4f);
                        break;

                    case GlyphKind.Minimize:
                        g.DrawLine(pen, 4.5f, 9f, 13.5f, 9f);
                        break;

                    case GlyphKind.Maximize:
                    {
                        using (GraphicsPath path = Rounded(
                            new RectangleF(4.4f, 4.4f, 9.2f, 9.2f), 1.4f))
                        {
                            g.DrawPath(pen, path);
                        }
                        break;
                    }

                    case GlyphKind.Restore:
                    {
                        g.DrawLine(pen, 7.2f, 4.4f, 13.6f, 4.4f);
                        g.DrawLine(pen, 13.6f, 4.4f, 13.6f, 10.8f);
                        using (GraphicsPath path = Rounded(
                            new RectangleF(4.4f, 6.8f, 7.8f, 7.8f), 1.4f))
                        {
                            g.DrawPath(pen, path);
                        }
                        break;
                    }

                    case GlyphKind.Close:
                        g.DrawLine(pen, 4.8f, 4.8f, 13.2f, 13.2f);
                        g.DrawLine(pen, 13.2f, 4.8f, 4.8f, 13.2f);
                        break;
                }
            }

            g.Restore(state);
        }
    }

    /// <summary>
    /// 圆角按钮：用于对话框底部操作、分段选择（Checked）与信息面板里的小按钮。
    /// </summary>
    internal class FlatButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private bool _primary;
        private bool _checked;
        private bool _danger;
        private Color _surface = DarkUi.Card;
        private int _radius = 8;

        public FlatButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Size = new Size(88, 32);
            Cursor = Cursors.Hand;
            Font = DarkUi.FontSmall;
            Invalidate();
        }

        /// <summary>控件底色 = 父容器底色，自绘时先铺满它避免闪白。</summary>
        public Color SurfaceColor
        {
            get { return _surface; }
            set
            {
                if (_surface != value)
                {
                    _surface = value;
                    Invalidate();
                }
            }
        }

        public bool Primary
        {
            get { return _primary; }
            set { _primary = value; Invalidate(); }
        }

        public bool Danger
        {
            get { return _danger; }
            set { _danger = value; Invalidate(); }
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

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
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
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(_surface);

            RectangleF box = new RectangleF(
                0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill;
            Color border = Color.Empty;
            Color ink;

            if (!Enabled)
            {
                fill = _primary ? Color.FromArgb(44, 55, 72) : DarkUi.Surface;
                ink = DarkUi.TextFaint;
            }
            else if (_primary)
            {
                fill = _hover ? DarkUi.AccentHover : DarkUi.Accent;
                if (_pressed)
                {
                    fill = Color.FromArgb(66, 124, 226);
                }
                ink = Color.White;
            }
            else if (_danger && _hover)
            {
                fill = Color.FromArgb(64, 38, 40);
                border = Color.FromArgb(140, 66, 68);
                ink = DarkUi.Danger;
            }
            else if (_checked)
            {
                fill = DarkUi.AccentSoft;
                border = Color.FromArgb(72, 116, 184);
                ink = Color.FromArgb(226, 236, 252);
            }
            else
            {
                fill = _hover ? DarkUi.CardHover : DarkUi.Card;
                border = DarkUi.Border;
                ink = DarkUi.Text;
            }

            using (GraphicsPath path = DarkUi.Rounded(box, _radius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (border != Color.Empty)
                {
                    using (Pen pen = new Pen(border, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            if (!string.IsNullOrEmpty(Text))
            {
                // 在按钮的 3px 内边距里居中绘制，避免中文贴边。
                Rectangle textBox = new Rectangle(
                    3, 0, Math.Max(1, Width - 6), Height);
                DarkUi.Text2(g, Text, Font, ink, textBox,
                    DarkUi.CenterMiddle | TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal enum GlyphKind
    {
        Fit,
        Actual,
        ZoomIn,
        ZoomOut,
        RotateLeft,
        RotateRight,
        Crop,
        Copy,
        Save,
        Prev,
        Next,
        Open,
        Undo,
        Minimize,
        Maximize,
        Restore,
        Close
    }

    /// <summary>只画图标的按钮：底部悬浮工具条与标题栏窗口按钮共用。</summary>
    internal sealed class GlyphButton : Control
    {
        private GlyphKind _kind;
        private Color _surface;
        private bool _hover;
        private bool _pressed;
        private bool _dangerHover;
        private Color _ink = DarkUi.TextDim;
        private float _iconScale = 1f;

        public GlyphButton(GlyphKind kind, Color surface, int width, int height)
        {
            _kind = kind;
            _surface = surface;
            Size = new Size(width, height);
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        public GlyphKind Kind
        {
            get { return _kind; }
            set
            {
                if (_kind != value)
                {
                    _kind = value;
                    Invalidate();
                }
            }
        }

        /// <summary>控件底色，需与所在的胶囊/标题栏保持一致。</summary>
        public Color SurfaceColor
        {
            get { return _surface; }
            set
            {
                if (_surface != value)
                {
                    _surface = value;
                    Invalidate();
                }
            }
        }

        /// <summary>悬停时是否用红色高亮（窗口关闭按钮）。</summary>
        public bool DangerHover
        {
            get { return _dangerHover; }
            set { _dangerHover = value; }
        }

        public Color Ink
        {
            get { return _ink; }
            set
            {
                if (_ink != value)
                {
                    _ink = value;
                    Invalidate();
                }
            }
        }

        public float IconScale
        {
            get { return _iconScale; }
            set { _iconScale = value; Invalidate(); }
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
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(_surface);

            RectangleF box = new RectangleF(
                1.5f, 1.5f, Width - 3f, Height - 3f);
            Color ink = Enabled ? _ink : DarkUi.TextFaint;
            if (_hover && Enabled)
            {
                Color fill = _dangerHover
                    ? Color.FromArgb(196, 64, 64)
                    : Color.FromArgb(52, 56, 64);
                using (GraphicsPath path = DarkUi.Rounded(box, 7f))
                using (SolidBrush brush = new SolidBrush(
                    _pressed ? Color.FromArgb(fill.R, fill.G, fill.B) : fill))
                {
                    g.FillPath(brush, path);
                }
                ink = _dangerHover ? Color.White : DarkUi.Text;
            }

            Rectangle iconBox = new Rectangle(0, 0, Width, Height);
            DarkUi.DrawGlyph(g, _kind, iconBox, ink,
                1.45f * Math.Max(0.8f, _iconScale));
        }
    }

    /// <summary>深色输入框：圆角边框 + 内嵌无边框 TextBox，聚焦时描边变蓝。</summary>
    internal sealed class FieldBox : Control
    {
        private readonly TextBox _box;
        private bool _focused;

        public FieldBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = DarkUi.Window;

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = DarkUi.Sunken;
            _box.ForeColor = DarkUi.Text;
            _box.Font = DarkUi.FontBody;
            _box.TextChanged += delegate
            {
                OnTextChanged(EventArgs.Empty);
            };
            _box.GotFocus += delegate
            {
                _focused = true;
                Invalidate();
            };
            _box.LostFocus += delegate
            {
                _focused = false;
                Invalidate();
            };
            Controls.Add(_box);
            Size = new Size(120, 34);
        }

        public TextBox Inner
        {
            get { return _box; }
        }

        public bool ReadOnlyField
        {
            get { return _box.ReadOnly; }
            set { _box.ReadOnly = value; }
        }

        public override string Text
        {
            get { return _box.Text; }
            set { _box.Text = value; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_box == null)
            {
                return;
            }
            int height = _box.PreferredHeight > 0
                ? _box.PreferredHeight : 18;
            _box.SetBounds(
                11,
                Math.Max(2, (Height - height) / 2),
                Math.Max(12, Width - 22),
                height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            RectangleF box = new RectangleF(
                0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = DarkUi.Rounded(box, 8f))
            using (SolidBrush brush = new SolidBrush(DarkUi.Sunken))
            {
                g.FillPath(brush, path);
                using (Pen pen = new Pen(
                    _focused ? DarkUi.Accent : DarkUi.Border, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }
        }
    }

    /// <summary>深色模态对话框基类：无边框 + 自绘标题行 + 可拖动 + Esc 取消。</summary>
    internal class DarkDialog : Form
    {
        private const int HeaderHeight = 46;

        private readonly string _caption;
        private readonly GlyphButton _closeButton;
        private bool _dragging;
        private Point _dragOrigin;

        protected DarkDialog(string caption, Size clientSize)
        {
            _caption = caption;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = DarkUi.Window;
            ClientSize = clientSize;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Font = DarkUi.FontBody;
            MaximizeBox = false;
            MinimizeBox = false;
            Text = caption;

            _closeButton = new GlyphButton(
                GlyphKind.Close, DarkUi.Window, 32, 28);
            _closeButton.DangerHover = true;
            _closeButton.IconScale = 0.85f;
            _closeButton.Location = new Point(
                ClientSize.Width - 42, 9);
            _closeButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(_closeButton);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        /// <summary>相对客户区顶部的 y 坐标，供子类排版内容。</summary>
        protected static int ContentTop
        {
            get { return HeaderHeight + 14; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(DarkUi.Window);

            RectangleF frame = new RectangleF(
                0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
            using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
            {
                g.DrawRectangle(pen, frame.X, frame.Y,
                    frame.Width, frame.Height);
            }

            DarkUi.Text2(g, _caption, DarkUi.FontTitle, DarkUi.Text,
                new Rectangle(20, 13, ClientSize.Width - 70, 24),
                DarkUi.LeftMiddleClip);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < HeaderHeight)
            {
                _dragging = true;
                _dragOrigin = Cursor.Position;
                _dragOrigin.Offset(-Left, -Top);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point now = Cursor.Position;
                Location = new Point(
                    now.X - _dragOrigin.X, now.Y - _dragOrigin.Y);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>把按钮按「取消在左、主操作在右」排在标题下方最底部一行。</summary>
        protected void PlaceFooter(params FlatButton[] buttons)
        {
            int right = ClientSize.Width - 20;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                FlatButton button = buttons[i];
                button.Location = new Point(right - button.Width, 0);
                button.Top = ClientSize.Height - button.Height - 20;
                button.SurfaceColor = DarkUi.Window;
                button.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                right = button.Left - 10;
                Controls.Add(button);
            }
        }

        /// <summary>小标题（如「目标格式」），统一 11px 灰色。</summary>
        protected static void DrawSectionLabel(
            Graphics g, string text, int x, int y)
        {
            DarkUi.Text2(g, text, DarkUi.FontSmall, DarkUi.TextDim,
                new Rectangle(x, y, 360, 16), DarkUi.LeftMiddle);
        }

        /// <summary>一行说明文字（当前状态、预计结果）。</summary>
        protected static void DrawHint(
            Graphics g, string text, int x, int y, Color color)
        {
            DarkUi.Text2(g, text, DarkUi.FontSmall, color,
                new Rectangle(x, y, 400, 18), DarkUi.LeftMiddleClip);
        }
    }

    /// <summary>
    /// 无边框主窗口基类：自带 6px 热区缩放、拖动、最大化/还原与阴影。
    /// 顶部标题行由子类自绘（见 <see cref="HeaderHeight"/>）。
    /// </summary>
    internal class ChromeForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private Rectangle _restoreBounds;
        private bool _maximized;

        protected ChromeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = DarkUi.Window;
            Font = DarkUi.FontBody;
            KeyPreview = true;
            DoubleBuffered = true;
            MinimumSize = new Size(720, 480);
            StartPosition = FormStartPosition.CenterScreen;
        }

        /// <summary>标题行高度，命中这里可以拖动窗口。</summary>
        protected virtual int HeaderHeight
        {
            get { return 44; }
        }

        protected bool IsMaximized
        {
            get { return _maximized; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.EnableRoundedCorners(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT && !_maximized)
                {
                    Point screen = new Point(
                        unchecked((short)(long)m.LParam),
                        unchecked((short)((long)m.LParam >> 16)));
                    Point p = PointToClient(screen);
                    int edge = 6;
                    bool left = p.X <= edge;
                    bool right = p.X >= ClientSize.Width - edge - 1;
                    bool top = p.Y <= edge;
                    bool bottom = p.Y >= ClientSize.Height - edge - 1;
                    if (left && top)
                    {
                        m.Result = (IntPtr)HTTOPLEFT;
                    }
                    else if (right && top)
                    {
                        m.Result = (IntPtr)HTTOPRIGHT;
                    }
                    else if (left && bottom)
                    {
                        m.Result = (IntPtr)HTBOTTOMLEFT;
                    }
                    else if (right && bottom)
                    {
                        m.Result = (IntPtr)HTBOTTOMRIGHT;
                    }
                    else if (left)
                    {
                        m.Result = (IntPtr)HTLEFT;
                    }
                    else if (right)
                    {
                        m.Result = (IntPtr)HTRIGHT;
                    }
                    else if (top)
                    {
                        m.Result = (IntPtr)HTTOP;
                    }
                    else if (bottom)
                    {
                        m.Result = (IntPtr)HTBOTTOM;
                    }
                }
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>在标题行上按下左键时调用，交给系统完成拖动。</summary>
        protected void BeginWindowDrag()
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(
                Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        protected void ToggleMaximize()
        {
            if (_maximized)
            {
                _maximized = false;
                if (_restoreBounds.Width > 0)
                {
                    Bounds = _restoreBounds;
                }
            }
            else
            {
                _restoreBounds = Bounds;
                _maximized = true;
                Rectangle work = Screen.FromControl(this).WorkingArea;
                Bounds = work;
            }
            OnMaximizedChanged();
        }

        protected virtual void OnMaximizedChanged()
        {
        }
    }
}
