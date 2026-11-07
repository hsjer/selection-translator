using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 截图编辑器：截屏选定区域后进入，可标注（矩形/椭圆/箭头/直线/
    /// 自由画/文字/高亮/马赛克）、裁剪、撤销、保存、复制，并调用百度
    /// 图片翻译接口翻译截图中的文字，展示分段结果与整图贴合译文图。
    ///
    /// 坐标约定：所有形状保存在「图片坐标系」（原图像素），绘制时按
    /// 缩放系数换算到画布；鼠标事件先把画布坐标换算回图片坐标再生成形状。
    /// </summary>
    internal sealed class ScreenshotEditorForm : Form
    {
        private enum Tool
        {
            None,
            Rectangle,
            Ellipse,
            Arrow,
            Line,
            Pen,
            Text,
            Highlight,
            Mosaic,
            Crop
        }

        private sealed class CanvasPanel : Panel
        {
            public CanvasPanel()
            {
                DoubleBuffered = true;
            }
        }

        private abstract class Shape
        {
            public abstract void Draw(
                Graphics g, float zoom, Bitmap image);
            public abstract Shape Clone();
            public virtual void Scale(
                float sx, float sy, float dx, float dy)
            {
            }
            public virtual RectangleF RectBounds()
            {
                return new RectangleF(0, 0, 0, 0);
            }
        }

        private sealed class RectShape : Shape
        {
            public RectangleF Rect;
            public Color Color;
            public float Width;

            public override void Draw(
                Graphics g, float zoom, Bitmap image)
            {
                using (Pen pen = new Pen(Color, Width))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawRectangle(
                        pen, Rect.X, Rect.Y, Rect.Width, Rect.Height);
                }
            }

            public override Shape Clone()
            {
                return new RectShape
                {
                    Rect = Rect,
                    Color = Color,
                    Width = Width
                };
            }

            public override void Scale(
                float sx, float sy, float dx, float dy)
            {
                Rect = new RectangleF(
                    Rect.X * sx + dx,
                    Rect.Y * sy + dy,
                    Rect.Width * sx,
                    Rect.Height * sy);
                Width *= (sx + sy) * 0.5f;
            }

            public override RectangleF RectBounds()
            {
                return Rect;
            }
        }

        private sealed class EllipseShape : Shape
        {
            public RectangleF Rect;
            public Color Color;
            public float Width;

            public override void Draw(
                Graphics g, float zoom, Bitmap image)
            {
                using (Pen pen = new Pen(Color, Width))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawEllipse(pen, Rect);
                }
            }

            public override Shape Clone()
            {
                return new EllipseShape
                {
                    Rect = Rect,
                    Color = Color,
                    Width = Width
                };
            }

            public override void Scale(
                float sx, float sy, float dx, float dy)
            {
                Rect = new RectangleF(
                    Rect.X * sx + dx,
                    Rect.Y * sy + dy,
                    Rect.Width * sx,
                    Rect.Height * sy);
                Width *= (sx + sy) * 0.5f;
            }

            public override RectangleF RectBounds()
            {
                return Rect;
            }
        }

        private sealed class HighlightShape : Shape
        {
            public RectangleF Rect;
            public Color Color;

            public override void Draw(
                Graphics g, float zoom, Bitmap image)
            {
                using (SolidBrush brush = new SolidBrush(Color))
                {
                    g.FillRectangle(brush, Rect);
                }
            }

            public override Shape Clone()
            {
                return new HighlightShape
                {
                    Rect = Rect,
                    Color = Color
                };
            }

            public override void Scale(
                float sx, float sy, float dx, float dy)
            {
                Rect = new RectangleF(
                    Rect.X * sx + dx,
                    Rect.Y * sy + dy,
                    Rect.Width * sx,
                    Rect.Height * sy);
            }

            public override RectangleF RectBounds()
            {
                return Rect;
            }
        }

        private sealed class MosaicShape : Shape
        {
            public RectangleF Rect;
            public int BlockScreenPx;

            public override void Draw(
                Graphics g, float zoom, Bitmap image)
            {
                if (image == null)
                {
                    return;
                }

                int x = (int)Math.Ceiling(Rect.X);
                int y = (int)Math.Ceiling(Rect.Y);
                int w = Math.Min((int)Rect.Width, image.Width - x);
                int h = Math.Min((int)Rect.Height, image.Height - y);
                if (w <= 0 || h <= 0)
                {
                    return;
                }

                // 形状坐标是「图片像素」，块大小也按图片像素固定，
                // 这样画布缩放与导出原图时的马赛克效果完全一致。
                int block = Math.Max(6, BlockScreenPx);

                // 先把整个区域缩小到 1 像素/块，每块取到的就是区域平均色。
                int tw = (w + block - 1) / block;
                int th = (h + block - 1) / block;
                Bitmap tiny;
                try
                {
                    tiny = new Bitmap(tw, th);
                    using (Graphics gx = Graphics.FromImage(tiny))
                    {
                        gx.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        gx.SmoothingMode = SmoothingMode.AntiAlias;
                        gx.DrawImage(
                            image,
                            new Rectangle(0, 0, tw, th),
                            new Rectangle(x, y, w, h),
                            GraphicsUnit.Pixel);
                    }
                }
                catch
                {
                    tiny = null;
                }

                GraphicsState state = g.Save();
                try
                {
                    g.SetClip(new RectangleF(x, y, w, h));

                    if (tiny != null)
                    {
                        try
                        {
                            for (int by = 0; by < h; by += block)
                            {
                                for (int bx = 0; bx < w; bx += block)
                                {
                                    int bw = Math.Min(block, w - bx);
                                    int bh = Math.Min(block, h - by);
                                    Color color;
                                    try
                                    {
                                        color = tiny.GetPixel(
                                            bx / block, by / block);
                                    }
                                    catch
                                    {
                                        color = Color.Black;
                                    }
                                    using (SolidBrush brush =
                                        new SolidBrush(color))
                                    {
                                        g.FillRectangle(
                                            brush, x + bx, y + by,
                                            bw, bh);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            tiny.Dispose();
                        }
                    }
                    else
                    {
                        using (SolidBrush brush = new SolidBrush(Color.Black))
                        {
                            g.FillRectangle(brush, x, y, w, h);
                        }
                    }
                }
                finally
                {
                    g.Restore(state);
                }
            }

            public override Shape Clone()
            {
                return new MosaicShape
                {
                    Rect = Rect,
                    BlockScreenPx = BlockScreenPx
                };
            }

            public override void Scale(
                float sx, float sy, float dx, float dy)
            {
                Rect = new RectangleF(
                    Rect.X * sx + dx,
                    Rect.Y * sy + dy,
                    Rect.Width * sx,
                    Rect.Height * sy);
            }

            public override RectangleF RectBounds()
            {
                return Rect;
            }
        }

        private sealed class MultiShape : Shape
        {
            public List<PointF> Points = new List<PointF>();
            public Color Color;
            public float Width;
            public bool Arrow;

            public override void Draw(
                Graphics g, float zoom, Bitmap image)
            {
                if (Points.Count < 2)
                {
                    return;
                }

                using (Pen pen = new Pen(Color, Width))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, Points.ToArray());

                    if (Arrow)
                    {
                        DrawArrowHead(g, pen.Width);
                    }
                }
            }

            private void DrawArrowHead(
                Graphics g, float penWidth)
            {
                PointF from = Points[Points.Count - 2];
                PointF to = Points[Points.Count - 1];
                float angle = (float)Math.Atan2(
                    to.Y - from.Y, to.X - from.X);
                float length = penWidth * 4;
                float width = penWidth * 2.5f;

                using (SolidBrush brush = new SolidBrush(Color))
                {
                    PointF[] head = new PointF[]
                    {
                        to,
                        new PointF(
                            to.X - length * (float)Math.Cos(angle - 0.4f),
                            to.Y - length * (float)Math.Sin(angle - 0.4f)),
                        new PointF(
                            to.X - length * (float)Math.Cos(angle + 0.4f),
                            to.Y - length * (float)Math.Sin(angle + 0.4f))
                    };
                    g.FillPolygon(brush, head);
                }
            }

            public override Shape Clone()
            {
                MultiShape copy = new MultiShape
                {
                    Color = Color,
                    Width = Width,
                    Arrow = Arrow
                };
                for (int i = 0; i < Points.Count; i++)
                {
                    copy.Points.Add(Points[i]);
                }
                return copy;
            }

            public override void Scale(
                float sx, float sy, float dx, float dy)
            {
                for (int i = 0; i < Points.Count; i++)
                {
                    Points[i] = new PointF(
                        Points[i].X * sx + dx,
                        Points[i].Y * sy + dy);
                }
                Width *= (sx + sy) * 0.5f;
            }

            public override RectangleF RectBounds()
            {
                if (Points.Count == 0)
                {
                    return new RectangleF(0, 0, 0, 0);
                }

                float x0 = Points[0].X;
                float y0 = Points[0].Y;
                float x1 = Points[0].X;
                float y1 = Points[0].Y;
                for (int i = 0; i < Points.Count; i++)
                {
                    x0 = Math.Min(x0, Points[i].X);
                    y0 = Math.Min(y0, Points[i].Y);
                    x1 = Math.Max(x1, Points[i].X);
                    y1 = Math.Max(y1, Points[i].Y);
                }
                return new RectangleF(x0, y0, x1 - x0, y1 - y0);
            }
        }

        private sealed class TextShape : Shape
        {
            public PointF Position;
            public string Text;
            public float FontSize;
            public Color Color;

            public override void Draw(
                Graphics g, float zoom, Bitmap image)
            {
                if (string.IsNullOrEmpty(Text))
                {
                    return;
                }

                float size = Math.Max(FontSize, 10f);
                using (Font font = new Font(
                    "Microsoft YaHei UI", size, FontStyle.Bold))
                {
                    using (SolidBrush outline = new SolidBrush(
                        Color.FromArgb(200, 0, 0, 0)))
                    using (SolidBrush fore = new SolidBrush(Color))
                    {
                        // 深色描边保证在浅色背景上可读。
                        g.DrawString(Text, font, outline, Position);
                        g.DrawString(Text, font, fore, Position);
                    }
                }
            }

            public override Shape Clone()
            {
                return new TextShape
                {
                    Position = Position,
                    Text = Text,
                    FontSize = FontSize,
                    Color = Color
                };
            }

            public override void Scale(
                float sx, float sy, float dx, float dy)
            {
                Position = new PointF(
                    Position.X * sx + dx,
                    Position.Y * sy + dy);
                FontSize *= Math.Max(sx, sy);
            }

            public override RectangleF RectBounds()
            {
                return new RectangleF(
                    Position.X, Position.Y, 120, FontSize);
            }
        }

        private sealed class CropPreview : Shape
        {
            public RectangleF Rect;

            public override void Draw(
                Graphics g, float zoom, Bitmap image)
            {
                using (Pen pen = new Pen(
                    Color.FromArgb(255, 255, 60, 110),
                    1.5f))
                {
                    g.DrawRectangle(
                        pen, Rect.X, Rect.Y, Rect.Width, Rect.Height);
                }
            }

            public override Shape Clone()
            {
                return new CropPreview { Rect = Rect };
            }

            public override RectangleF RectBounds()
            {
                return Rect;
            }
        }

        private sealed class CroppedSnapshot
        {
            public Bitmap Image;
            public List<Shape> Shapes;
        }

        private readonly Func<AppConfig> _configProvider;
        private Bitmap _image;

        /// <summary>始终取托盘当前配置，避免编辑器打开期间改设置后用旧密钥。</summary>
        private AppConfig Config
        {
            get
            {
                AppConfig config = _configProvider == null
                    ? null : _configProvider();
                return config ?? new AppConfig();
            }
        }
        private readonly List<Shape> _shapes = new List<Shape>();
        private Stack<object> _undo = new Stack<object>();
        private Shape _preview;

        private Tool _tool = Tool.Rectangle;
        private Color _color = Color.FromArgb(255, 230, 30, 30);
        private float _width = 2.5f;
        private float _zoom = 1f;
        // 图片左上角在画布中的像素偏移（用于居中，替代 AutoScroll）。
        private PointF _imageOffset = PointF.Empty;

        private PointF _startPoint;
        private bool _drawing;
        private TextBox _textInput;
        private bool _translating;

        // 这些控件在 Build* 辅助方法中创建（非构造函数），
        // 因此不能声明为 readonly。
        private CanvasPanel _canvas;
        private ToolStrip _toolbar;
        private Panel _actionBar;
        private Panel _resultPanel;
        private Label _statusLabel;
        private readonly List<Control> _rightButtons =
            new List<Control>();

        private ToolStripButton _translateButton;
        private ToolStripButton _undoButton;
        private ToolStripButton _clearButton;
        private ToolStripDropDownButton _colorButton;
        private ToolStripDropDownButton _widthButton;
        private ToolStripButton _copyTextButton;
        private ToolStripButton _copyPasteButton;
        private ListBox _segmentList;
        private TextBox _sumBox;
        private TextBox _srcBox;
        private PictureBox _pasteBox;

        private ImageTranslationResult _lastResult;

        /// <summary>当前编辑的截图位图（供外部清理）。</summary>
        public Bitmap Image
        {
            get { return _image; }
        }

        public ScreenshotEditorForm(
            Func<AppConfig> configProvider, Bitmap image)
        {
            _configProvider = configProvider;
            _image = image;

            Text = "截图编辑器（Esc 取消 / Enter 确认裁剪 / Ctrl+Z 撤销）";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);

            BuildToolbar();
            BuildCanvas();
            BuildResultPanel();
            BuildActionBar();

            // Docking 顺序：先加的后停靠。最终布局：
            // 上=工具栏，中=画布，下=操作栏，最底=翻译结果面板。
            Controls.Add(_canvas);          // Fill
            Controls.Add(_actionBar);       // Bottom
            Controls.Add(_resultPanel);     // Bottom
            Controls.Add(_toolbar);         // Top

            _canvas.Paint += OnCanvasPaint;
            _canvas.MouseDown += OnCanvasMouseDown;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseUp += OnCanvasMouseUp;
            _canvas.DoubleClick += OnCanvasDoubleClick;
            KeyDown += OnEditorKeyDown;
            Resize += OnEditorResize;

            SetTool(Tool.Rectangle);
        }

        // ---------------- 界面构建 ----------------

        private void BuildToolbar()
        {
            _toolbar = new ToolStrip
            {
                Margin = new Padding(8, 4, 8, 2),
                GripStyle = ToolStripGripStyle.Hidden
            };

            AddToolButton("矩形",
                (s, e) => SetTool(Tool.Rectangle));
            AddToolButton("椭圆",
                (s, e) => SetTool(Tool.Ellipse));
            AddToolButton("箭头",
                (s, e) => SetTool(Tool.Arrow));
            AddToolButton("直线",
                (s, e) => SetTool(Tool.Line));
            AddToolButton("画笔",
                (s, e) => SetTool(Tool.Pen));
            AddToolButton("高亮",
                (s, e) => SetTool(Tool.Highlight));
            AddToolButton("马赛克",
                (s, e) => SetTool(Tool.Mosaic));
            AddToolButton("文字",
                (s, e) => SetTool(Tool.Text));
            AddToolButton("裁剪",
                (s, e) => SetTool(Tool.Crop));

            _toolbar.Items.Add(new ToolStripSeparator());

            _undoButton = NewToolButton("撤销");
            _undoButton.Click += OnUndoClicked;
            _toolbar.Items.Add(_undoButton);

            _clearButton = NewToolButton("清空标注");
            _clearButton.Click += OnClearClicked;
            _toolbar.Items.Add(_clearButton);

            _toolbar.Items.Add(new ToolStripSeparator());

            _colorButton = new ToolStripDropDownButton();
            _colorButton.Text = "颜色";
            _colorButton.DropDownItems.Add(
                MakeColorItem("红", Color.FromArgb(255, 230, 30, 30)));
            _colorButton.DropDownItems.Add(
                MakeColorItem("绿", Color.FromArgb(255, 30, 160, 60)));
            _colorButton.DropDownItems.Add(
                MakeColorItem("蓝", Color.FromArgb(255, 40, 90, 220)));
            _colorButton.DropDownItems.Add(
                MakeColorItem("黄", Color.FromArgb(255, 235, 150, 0)));
            _colorButton.DropDownItems.Add(
                MakeColorItem("白", Color.White));
            _colorButton.DropDownItems.Add(
                MakeColorItem("黑", Color.Black));
            _toolbar.Items.Add(_colorButton);

            _widthButton = new ToolStripDropDownButton();
            _widthButton.Text = "线宽";
            _widthButton.DropDownItems.Add(
                MakeWidthItem("细", 2));
            _widthButton.DropDownItems.Add(
                MakeWidthItem("中", 4));
            _widthButton.DropDownItems.Add(
                MakeWidthItem("粗", 8));
            _toolbar.Items.Add(_widthButton);

            _toolbar.Items.Add(new ToolStripSeparator());

            _saveButton = NewToolButton("保存图片");
            _saveButton.Click += OnSaveClicked;
            _toolbar.Items.Add(_saveButton);

            ToolStripButton copyButton = NewToolButton("复制结果");
            copyButton.Click += OnCopyClicked;
            _toolbar.Items.Add(copyButton);

            _translateButton = NewToolButton("翻译图片");
            _translateButton.Click += OnTranslateClicked;
            _toolbar.Items.Add(_translateButton);

            _toolbar.Items.Add(new ToolStripSeparator());

            _copyTextButton = NewToolButton("复制译文");
            _copyTextButton.Click += OnCopyTextClicked;
            _toolbar.Items.Add(_copyTextButton);

            _copyPasteButton = NewToolButton("复制译文图");
            _copyPasteButton.Enabled = false;
            _copyPasteButton.Click += OnCopyPasteClicked;
            _toolbar.Items.Add(_copyPasteButton);
        }

        private ToolStripButton _saveButton;

        private void AddToolButton(
            string text, EventHandler handler)
        {
            ToolStripButton button = NewToolButton(text);
            button.Click += handler;
            _toolbar.Items.Add(button);
        }

        private static ToolStripButton NewToolButton(string text)
        {
            return new ToolStripButton(text);
        }

        private ToolStripMenuItem MakeColorItem(
            string text, Color color)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += (s, e) =>
            {
                _color = color;
            };
            return item;
        }

        private ToolStripMenuItem MakeWidthItem(string text, int px)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += (s, e) =>
            {
                _width = px;
                _widthButton.Text = text + " (" + px + "px)";
            };
            return item;
        }

        private void BuildCanvas()
        {
            _canvas = new CanvasPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(218, 222, 228)
            };
        }

        private void BuildResultPanel()
        {
            _resultPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 246,
                Visible = false,
                BackColor = Color.White
            };

            Label title = new Label
            {
                Text = "图片翻译结果",
                AutoSize = true,
                Location = new Point(12, 8),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            };
            _resultPanel.Controls.Add(title);

            Label srcLabel = new Label
            {
                Text = "原文：",
                Bounds = new Rectangle(12, 32, 60, 20),
                AutoSize = true
            };
            _resultPanel.Controls.Add(srcLabel);

            _srcBox = new TextBox
            {
                Bounds = new Rectangle(52, 32, 408, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                ReadOnly = true,
                WordWrap = false
            };
            _srcBox.Text = "（识别中…）";
            _resultPanel.Controls.Add(_srcBox);

            _segmentList = new ListBox
            {
                Bounds = new Rectangle(12, 58, 448, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                HorizontalScrollbar = true
            };
            _resultPanel.Controls.Add(_segmentList);

            Label sumLabel = new Label
            {
                Text = "译文：",
                Bounds = new Rectangle(12, 184, 60, 20),
                AutoSize = true
            };
            _resultPanel.Controls.Add(sumLabel);

            _sumBox = new TextBox
            {
                Bounds = new Rectangle(52, 182, 408, 56),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false
            };
            _sumBox.Text = "译文：";
            _resultPanel.Controls.Add(_sumBox);

            Label pasteLabel = new Label
            {
                Text = "整图贴合译文：",
                Bounds = new Rectangle(472, 32, 120, 20),
                AutoSize = true
            };
            _resultPanel.Controls.Add(pasteLabel);

            _pasteBox = new PictureBox
            {
                Bounds = new Rectangle(472, 54, 352, 184),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(248, 249, 251)
            };
            _resultPanel.Controls.Add(_pasteBox);

            Button closeResult = new Button
            {
                Text = "收起结果",
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Location = new Point(752, 16)
            };
            closeResult.Click += (s, e) =>
            {
                _resultPanel.Visible = false;
                RefreshLayoutAfterPanelChange();
            };
            _resultPanel.Controls.Add(closeResult);
        }

        private void BuildActionBar()
        {
            _actionBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                BackColor = Color.FromArgb(238, 240, 244)
            };

            _statusLabel = new Label
            {
                Text = "拖拽鼠标绘制；文字工具点击后输入文字",
                AutoSize = false,
                Bounds = new Rectangle(12, 14, 420, 22),
                ForeColor = Color.FromArgb(90, 98, 112)
            };
            _actionBar.Controls.Add(_statusLabel);

            Button doneButton = new Button
            {
                Text = "完成",
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            doneButton.Click += (s, e) => Close();
            _actionBar.Controls.Add(doneButton);
            _rightButtons.Add(doneButton);

            Button translateButton = new Button
            {
                Text = "翻译图片",
                Size = new Size(96, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            translateButton.Click += OnTranslateClicked;
            _actionBar.Controls.Add(translateButton);
            _rightButtons.Add(translateButton);

            Button copyButton = new Button
            {
                Text = "复制",
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            copyButton.Click += OnCopyClicked;
            _actionBar.Controls.Add(copyButton);
            _rightButtons.Add(copyButton);

            Button saveButton = new Button
            {
                Text = "保存",
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            saveButton.Click += OnSaveClicked;
            _actionBar.Controls.Add(saveButton);
            _rightButtons.Add(saveButton);
        }

        /// <summary>
        /// 结果面板显隐会改变画布 ClientSize，等布局结束后再重算缩放与居中。
        /// </summary>
        private void RefreshLayoutAfterPanelChange()
        {
            BeginInvoke(new MethodInvoker(delegate
            {
                if (!IsDisposed && !_canvas.IsDisposed)
                {
                    ComputeZoom();
                    CenterImage();
                    RepositionRightButtons();
                }
            }));
        }

        private void RepositionRightButtons()
        {
            int x = _actionBar.Width - 12;
            int y = Math.Max(0, (_actionBar.Height - 30) / 2);
            for (int i = _rightButtons.Count - 1; i >= 0; i--)
            {
                Control control = _rightButtons[i];
                x -= control.Width;
                control.Left = x;
                control.Top = y;
                x -= 8;
            }
        }

        // ---------------- 坐标与缩放 ----------------

        private void ComputeZoom()
        {
            if (_image == null ||
                _image.Width <= 0 || _image.Height <= 0)
            {
                return;
            }

            int availW = Math.Max(100, _canvas.ClientSize.Width - 32);
            int availH = Math.Max(100, _canvas.ClientSize.Height - 32);
            float zx = availW / (float)_image.Width;
            float zy = availH / (float)_image.Height;
            // 取适配值，保证整图始终可见（因此无需滚动）。
            // 小图最多放大 4 倍，避免像素块过大。
            _zoom = Math.Min(zx, zy);
            if (_zoom > 4f)
            {
                _zoom = 4f;
            }

            SetStatusText();
        }

        private PointF GetImagePoint(MouseEventArgs e)
        {
            float x = (e.X - _imageOffset.X) / _zoom;
            float y = (e.Y - _imageOffset.Y) / _zoom;
            return new PointF(x, y);
        }

        private void CenterImage()
        {
            if (_image == null)
            {
                return;
            }

            float w = _image.Width * _zoom;
            float h = _image.Height * _zoom;
            _imageOffset = new PointF(
                Math.Max(0, (_canvas.ClientSize.Width - w) / 2f),
                Math.Max(0, (_canvas.ClientSize.Height - h) / 2f));
            InvalidateCanvas();
        }

        private void SetStatusText()
        {
            if (_statusLabel == null)
            {
                return;
            }

            string toolName;
            string hint;
            switch (_tool)
            {
                case Tool.Rectangle:
                    toolName = "矩形";
                    hint = "拖拽绘制矩形";
                    break;
                case Tool.Ellipse:
                    toolName = "椭圆";
                    hint = "拖拽绘制椭圆";
                    break;
                case Tool.Arrow:
                    toolName = "箭头";
                    hint = "从起点拖到终点";
                    break;
                case Tool.Line:
                    toolName = "直线";
                    hint = "从起点拖到终点";
                    break;
                case Tool.Pen:
                    toolName = "画笔";
                    hint = "按住拖动自由绘制";
                    break;
                case Tool.Text:
                    toolName = "文字";
                    hint = "点击位置输入文字，Enter 确认";
                    break;
                case Tool.Highlight:
                    toolName = "高亮";
                    hint = "拖拽覆盖高亮区域";
                    break;
                case Tool.Mosaic:
                    toolName = "马赛克";
                    hint = "拖拽添加马赛克（仅作用截图）";
                    break;
                case Tool.Crop:
                    toolName = "裁剪";
                    hint = "框选区域后回车/双击裁剪";
                    break;
                default:
                    toolName = "";
                    hint = "";
                    break;
            }

            string size = _image == null
                ? "无图片"
                : _image.Width + "×" + _image.Height +
                  "  缩放 " + (_zoom * 100).ToString("0") + "%";
            _statusLabel.Text = size + "    |    工具：" +
                toolName + "　" + hint;
        }

        private void SetTool(Tool tool)
        {
            _tool = tool;
            _preview = null;

            if (_tool != Tool.Text && _textInput != null)
            {
                CommitText();
            }

            _canvas.Cursor = tool == Tool.Text
                ? Cursors.IBeam
                : Cursors.Cross;
            SetStatusText();
        }

        // ---------------- 绘制 ----------------

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            using (SolidBrush bg = new SolidBrush(
                Color.FromArgb(218, 222, 228)))
            {
                g.FillRectangle(bg, e.ClipRectangle);
            }

            g.TranslateTransform(
                _imageOffset.X,
                _imageOffset.Y);
            g.ScaleTransform(_zoom, _zoom);

            if (_image != null)
            {
                g.DrawImage(_image, 0, 0);
            }

            for (int i = 0; i < _shapes.Count; i++)
            {
                _shapes[i].Draw(g, _zoom, _image);
            }

            if (_preview != null)
            {
                _preview.Draw(g, _zoom, _image);
            }
        }

        // ---------------- 鼠标事件 ----------------

        private void OnCanvasMouseDown(
            object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_tool == Tool.Text)
            {
                ShowTextInput(e);
                return;
            }

            _startPoint = GetImagePoint(e);
            _canvas.Capture = true;
            _drawing = true;
            _preview = CreatePreview(_startPoint);
            InvalidateCanvas();
        }

        private Shape CreatePreview(PointF p)
        {
            switch (_tool)
            {
                case Tool.Rectangle:
                    return new RectShape
                    {
                        Rect = new RectangleF(p.X, p.Y, 1, 1),
                        Color = _color, Width = _width
                    };
                case Tool.Ellipse:
                    return new EllipseShape
                    {
                        Rect = new RectangleF(p.X, p.Y, 1, 1),
                        Color = _color, Width = _width
                    };
                case Tool.Arrow:
                    return new MultiShape
                    {
                        Points = new List<PointF> { p, p },
                        Color = _color, Width = _width, Arrow = true
                    };
                case Tool.Line:
                    return new MultiShape
                    {
                        Points = new List<PointF> { p, p },
                        Color = _color, Width = _width
                    };
                case Tool.Pen:
                    return new MultiShape
                    {
                        Points = new List<PointF> { p },
                        Color = _color, Width = _width
                    };
                case Tool.Highlight:
                    return new HighlightShape
                    {
                        Rect = new RectangleF(p.X, p.Y, 1, 1),
                        Color = Color.FromArgb(110, _color)
                    };
                case Tool.Mosaic:
                    return new MosaicShape
                    {
                        Rect = new RectangleF(p.X, p.Y, 1, 1),
                        BlockScreenPx = 12
                    };
                case Tool.Crop:
                    return new CropPreview
                    {
                        Rect = new RectangleF(p.X, p.Y, 1, 1)
                    };
                default:
                    return null;
            }
        }

        private void OnCanvasMouseMove(
            object sender, MouseEventArgs e)
        {
            if (!_drawing || _preview == null)
            {
                return;
            }

            PointF p = GetImagePoint(e);
            UpdatePreview(p);
            InvalidateCanvas();
        }

        private void UpdatePreview(PointF p)
        {
            RectangleF rect = NormalizeRect(_startPoint, p);

            if (_preview is RectShape)
            {
                ((RectShape)_preview).Rect = rect;
            }
            else if (_preview is EllipseShape)
            {
                ((EllipseShape)_preview).Rect = rect;
            }
            else if (_preview is HighlightShape)
            {
                ((HighlightShape)_preview).Rect = rect;
            }
            else if (_preview is MosaicShape)
            {
                ((MosaicShape)_preview).Rect = rect;
            }
            else if (_preview is CropPreview)
            {
                ((CropPreview)_preview).Rect = rect;
            }
            else if (_preview is MultiShape)
            {
                MultiShape shape = (MultiShape)_preview;
                if (_tool == Tool.Pen)
                {
                    shape.Points.Add(p);
                }
                else
                {
                    // 直线/箭头：仅更新终点。
                    shape.Points[shape.Points.Count - 1] = p;
                }
            }
        }

        private static RectangleF NormalizeRect(
            PointF a, PointF b)
        {
            float left = Math.Min(a.X, b.X);
            float top = Math.Min(a.Y, b.Y);
            return new RectangleF(
                left, top,
                Math.Abs(b.X - a.X),
                Math.Abs(b.Y - a.Y));
        }

        private void OnCanvasMouseUp(
            object sender, MouseEventArgs e)
        {
            if (!_drawing)
            {
                return;
            }

            _drawing = false;
            _canvas.Capture = false;

            if (_preview == null)
            {
                return;
            }

            RectangleF bounds = GetBounds(_preview);
            if (bounds.Width < 3 || bounds.Height < 3)
            {
                // 太小的框忽略；裁剪模式下也忽略，避免误触发。
                if (_tool != Tool.Crop)
                {
                    DiscardPreview();
                }
                return;
            }

            if (_tool == Tool.Crop)
            {
                // 裁剪预览保留，等待 Enter/双击确认。
                return;
            }

            CommitShape(_preview);
        }

        private void DiscardPreview()
        {
            _preview = null;
            InvalidateCanvas();
        }

        private static RectangleF GetBounds(Shape shape)
        {
            return shape.RectBounds();
        }

        private void CommitShape(Shape shape)
        {
            _undo.Push(CloneShapes(_shapes));
            _shapes.Add(shape);
            _preview = null;
            InvalidateCanvas();
            updateUndoButtons();
        }

        private List<Shape> CloneShapes(List<Shape> source)
        {
            List<Shape> copy = new List<Shape>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                copy.Add(source[i].Clone());
            }
            return copy;
        }

        private void OnCanvasDoubleClick(
            object sender, EventArgs e)
        {
            if (_tool == Tool.Crop)
            {
                ApplyCrop();
            }
        }

        private void InvalidateCanvas()
        {
            _canvas.Invalidate(true);
        }

        // ---------------- 文字输入 ----------------

        private void ShowTextInput(MouseEventArgs e)
        {
            _textInput = new TextBox
            {
                Bounds = new Rectangle(e.X, e.Y, 260, 28),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(
                    "Microsoft YaHei UI",
                    Math.Max(9f, 20f * _zoom),
                    FontStyle.Bold)
            };
            _textInput.KeyDown += OnTextInputKeyDown;
            _canvas.Controls.Add(_textInput);
            _textInput.Focus();
        }

        private void OnTextInputKeyDown(
            object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitText();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CancelTextInput();
                e.SuppressKeyPress = true;
            }
        }

        private void CommitText()
        {
            if (_textInput == null)
            {
                return;
            }

            string text = _textInput.Text;
            Rectangle textBoxRect = _textInput.Bounds;
            RemoveTextInput();

            if (!string.IsNullOrEmpty(text))
            {
                CommitShape(new TextShape
                {
                    Position = new PointF(
                        (textBoxRect.X - _imageOffset.X) / _zoom,
                        (textBoxRect.Y - _imageOffset.Y) / _zoom),
                    Text = text,
                    FontSize = 20,
                    Color = _color
                });
            }
        }

        private void CancelTextInput()
        {
            RemoveTextInput();
        }

        private void RemoveTextInput()
        {
            if (_textInput == null)
            {
                return;
            }
            _canvas.Controls.Remove(_textInput);
            _textInput.Dispose();
            _textInput = null;
        }

        // ---------------- 裁剪 ----------------

        private void ApplyCrop()
        {
            if (!(_preview is CropPreview))
            {
                return;
            }

            RectangleF cropRect = ((CropPreview)_preview).Rect;
            if (cropRect.Width < 20 || cropRect.Height < 20)
            {
                MessageBox.Show(
                    this,
                    "裁剪区域太小（至少 20×20 像素）。",
                    "截图编辑器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                DiscardPreview();
                SetTool(Tool.Rectangle);
                return;
            }

            // 约束在图片范围内。
            RectangleF intersect = RectangleF.Intersect(
                cropRect,
                new RectangleF(0, 0, _image.Width, _image.Height));
            if (intersect.Width < 20 || intersect.Height < 20)
            {
                MessageBox.Show(
                    this,
                    "裁剪区域太小或超出图片范围。",
                    "截图编辑器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                DiscardPreview();
                SetTool(Tool.Rectangle);
                return;
            }

            int x = (int)Math.Round(intersect.X);
            int y = (int)Math.Round(intersect.Y);
            int w = (int)Math.Round(intersect.Width);
            int h = (int)Math.Round(intersect.Height);

            // 记入撤销栈（保存原图用于恢复）。
            _undo.Push(new CroppedSnapshot
            {
                Image = _image,
                Shapes = CloneShapes(_shapes)
            });

            Bitmap croppedImage = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(croppedImage))
            {
                g.DrawImage(
                    _image,
                    new Rectangle(0, 0, w, h),
                    x, y, w, h,
                    GraphicsUnit.Pixel);
            }

            List<Shape> kept = new List<Shape>();
            float sx = w / intersect.Width;
            float sy = h / intersect.Height;
            float dx = -intersect.X;
            float dy = -intersect.Y;

            for (int i = 0; i < _shapes.Count; i++)
            {
                Shape shape = _shapes[i].Clone();
                shape.Scale(sx, sy, dx, dy);
                RectangleF b = GetBounds(shape);
                if (b.X < w && b.Y < h &&
                    b.X + b.Width > 0 && b.Y + b.Height > 0)
                {
                    kept.Add(shape);
                }
            }

            // 原图交给撤销栈持有，不能在此释放。
            _image = croppedImage;
            _shapes.Clear();
            for (int i = 0; i < kept.Count; i++)
            {
                _shapes.Add(kept[i]);
            }

            DiscardPreview();
            _imageOffset = PointF.Empty;
            ComputeZoom();
            CenterImage();
            SetStatusText();
            updateUndoButtons();
        }

        // ---------------- 保存 / 复制 ----------------

        private Bitmap CreateCompositeWithShapes()
        {
            if (_image == null)
            {
                return null;
            }

            Bitmap composite = new Bitmap(
                _image.Width, _image.Height,
                PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(composite))
            {
                g.DrawImage(_image, 0, 0);
                for (int i = 0; i < _shapes.Count; i++)
                {
                    _shapes[i].Draw(g, 1f, _image);
                }
            }
            return composite;
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            if (_image == null)
            {
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "保存截图",
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|位图|*.bmp"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Bitmap composite = CreateCompositeWithShapes();
            try
            {
                string ext = Path.GetExtension(dialog.FileName);
                if (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    composite.Save(dialog.FileName, ImageFormat.Jpeg);
                }
                else
                {
                    composite.Save(dialog.FileName, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                Log.Error("保存截图失败", ex);
                MessageBox.Show(
                    this,
                    "保存失败：" + ex.Message,
                    "截图编辑器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                composite.Dispose();
            }
        }

        private void OnCopyClicked(object sender, EventArgs e)
        {
            if (_image == null)
            {
                return;
            }

            Bitmap composite = CreateCompositeWithShapes();
            try
            {
                Clipboard.SetImage(composite);
            }
            catch (Exception ex)
            {
                Log.Error("复制截图到剪贴板失败", ex);
                MessageBox.Show(
                    this,
                    "复制失败：" + ex.Message,
                    "截图编辑器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                composite.Dispose();
            }
        }

        // ---------------- 翻译 ----------------

        private async void OnTranslateClicked(
            object sender, EventArgs e)
        {
            if (_translating || _image == null)
            {
                return;
            }

            if (!HasCredentials())
            {
                MessageBox.Show(
                    this,
                    "未配置百度翻译 AppID 或密钥。\r\n" +
                    "请右键托盘图标打开「设置」填写。",
                    "截图翻译",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _translating = true;
            SetTranslateBusy(true);
            _resultPanel.Visible = true;
            RefreshLayoutAfterPanelChange();
            _lastResult = null;
            _segmentList.Items.Clear();
            _srcBox.Text = "（翻译中…）";
            _sumBox.Text = "翻译中…（图片识别可能需要几秒）";
            _pasteBox.Image = null;

            try
            {
                // 翻译原图（不含标注），避免标注干扰 OCR。
                string note;
                byte[] bytes =
                    BaiduImageTranslator.PrepareImage(
                        _image, out note);

                AppConfig config = Config;
                ImageTranslationResult result =
                    await BaiduImageTranslator.TranslateImageAsync(
                        config, bytes,
                        config.ImageTargetLanguage);

                if (IsDisposed)
                {
                    return;
                }

                _lastResult = result;
                ShowResult(result);
            }
            catch (TranslationException ex)
            {
                _segmentList.Items.Clear();
                _srcBox.Text = "（无）";
                _sumBox.Text = ex.Message;
                _pasteBox.Image = null;
            }
            catch (Exception ex)
            {
                Log.Error("截图翻译失败", ex);
                _segmentList.Items.Clear();
                _srcBox.Text = "（无）";
                _sumBox.Text = "翻译失败：" + ex.Message;
                _pasteBox.Image = null;
            }
            finally
            {
                _translating = false;
                SetTranslateBusy(false);
            }
        }

        private bool HasCredentials()
        {
            AppConfig config = Config;
            return !string.IsNullOrEmpty((config.AppId ?? "").Trim()) &&
                !string.IsNullOrEmpty(config.GetSecret());
        }

        private void SetTranslateBusy(bool busy)
        {
            if (_translateButton != null)
            {
                _translateButton.Enabled = !busy;
            }
        }

        private void ShowResult(ImageTranslationResult result)
        {
            _segmentList.Items.Clear();
            if (result.Segments != null)
            {
                for (int i = 0; i < result.Segments.Count; i++)
                {
                    ImageSegmentInfo seg = result.Segments[i];
                    _segmentList.Items.Add(
                        (i + 1) + ". " + seg.Src + "  =>  " + seg.Dst);
                }
            }

            _srcBox.Text = string.IsNullOrEmpty(result.SumSrc)
                ? "（未识别到文字）"
                : result.SumSrc;
            _sumBox.Text = "译文（" + result.To + "）：\r\n" +
                (string.IsNullOrEmpty(result.SumDst)
                    ? "（无）" : result.SumDst);

            if (result.PastedImage != null &&
                result.PastedImage.Length > 0)
            {
                Bitmap pasted;
                try
                {
                    using (MemoryStream stream =
                        new MemoryStream(result.PastedImage))
                    {
                        pasted = new Bitmap(stream);
                    }
                }
                catch
                {
                    pasted = null;
                }

                if (pasted != null)
                {
                    _pasteBox.Image = pasted;
                    _copyPasteButton.Enabled = true;
                }
            }

            if (_pasteBox.Image == null)
            {
                _copyPasteButton.Enabled = false;
            }
        }

        private void OnCopyTextClicked(object sender, EventArgs e)
        {
            if (_lastResult == null)
            {
                return;
            }

            try
            {
                Clipboard.SetText(_lastResult.SumDst ?? "");
            }
            catch (Exception ex)
            {
                Log.Error("复制译文失败", ex);
            }
        }

        private void OnCopyPasteClicked(object sender, EventArgs e)
        {
            if (_lastResult == null ||
                _lastResult.PastedImage == null)
            {
                return;
            }

            try
            {
                using (MemoryStream stream =
                    new MemoryStream(_lastResult.PastedImage))
                {
                    using (Bitmap image = new Bitmap(stream))
                    {
                        Clipboard.SetImage(image);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("复制译文图失败", ex);
            }
        }

        // ---------------- 撤销 / 清空 ----------------

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (_undo.Count == 0)
            {
                return;
            }

            object snapshot = _undo.Pop();

            if (snapshot is CroppedSnapshot)
            {
                CroppedSnapshot restored = snapshot as CroppedSnapshot;
                Bitmap old = _image;
                _image = restored.Image;
                _shapes.Clear();
                for (int i = 0; i < restored.Shapes.Count; i++)
                {
                    _shapes.Add(restored.Shapes[i]);
                }
                if (old != null)
                {
                    old.Dispose();
                }
                DiscardPreview();
                _imageOffset = PointF.Empty;
                ComputeZoom();
                CenterImage();
                SetStatusText();
            }
            else if (snapshot is List<Shape>)
            {
                _shapes.Clear();
                List<Shape> shapes = snapshot as List<Shape>;
                for (int i = 0; i < shapes.Count; i++)
                {
                    _shapes.Add(shapes[i]);
                }
                DiscardPreview();
                ComputeZoom();
                CenterImage();
                SetStatusText();
            }

            updateUndoButtons();
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            if (_shapes.Count == 0)
            {
                return;
            }

            _undo.Push(CloneShapes(_shapes));
            _shapes.Clear();
            DiscardPreview();
            InvalidateCanvas();
            updateUndoButtons();
        }

        private void updateUndoButtons()
        {
            if (_undoButton != null)
            {
                _undoButton.Enabled = _undo.Count > 0;
            }
            if (_clearButton != null)
            {
                _clearButton.Enabled = _shapes.Count > 0;
            }
        }

        // ---------------- 键盘 ----------------

        private void OnEditorKeyDown(
            object sender, KeyEventArgs e)
        {
            if (_textInput != null)
            {
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                if (_tool == Tool.Crop && _preview != null)
                {
                    DiscardPreview();
                    SetTool(Tool.Rectangle);
                    e.SuppressKeyPress = true;
                    return;
                }

                DialogResult = DialogResult.Cancel;
                Close();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Enter &&
                !e.Control && !e.Shift)
            {
                if (_tool == Tool.Crop)
                {
                    ApplyCrop();
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (e.Control && e.KeyCode == Keys.Z)
            {
                OnUndoClicked(this, EventArgs.Empty);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.C)
            {
                OnCopyClicked(this, EventArgs.Empty);
                e.SuppressKeyPress = true;
            }
        }

        // ---------------- 生命周期 ----------------

        private void OnEditorResize(
            object sender, EventArgs e)
        {
            ComputeZoom();
            CenterImage();
            RepositionRightButtons();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ComputeZoom();
            CenterImage();
            RepositionRightButtons();
            updateUndoButtons();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (_pasteBox.Image != null)
            {
                _pasteBox.Image.Dispose();
                _pasteBox.Image = null;
            }
            foreach (object entry in _undo)
            {
                CroppedSnapshot snapshot = entry as CroppedSnapshot;
                if (snapshot != null && snapshot.Image != null &&
                    snapshot.Image != _image)
                {
                    snapshot.Image.Dispose();
                }
            }
            _undo.Clear();
            if (_image != null)
            {
                _image.Dispose();
                _image = null;
            }
            RemoveTextInput();
        }
    }
}
