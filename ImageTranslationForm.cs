using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 图片翻译结果窗口。
    ///
    /// 与旧的「截图编辑器」的区别：这里没有任何矩形 / 画笔 / 马赛克 /
    /// 裁剪等编辑工具。翻译完成后译文直接显示在图片上（优先使用百度
    /// 返回的整图贴合译文图），每个 OCR 分段是一块可点选、可框选的
    /// 热区，选中即自动复制到剪贴板。
    ///
    /// 坐标约定：_textBlocks 保存 OCR 源图坐标；MapOcrRect 把它映射到
    /// 当前显示底图的像素坐标；绘制时再做一次 Translate + Scale 变换。
    /// </summary>
    internal sealed class ImageTranslationForm : Form
    {
        // ------------------------------------------------------------------
        // 文字块模型（SelfTest 通过反射验证 rect 解析与阅读顺序拼接）
        // ------------------------------------------------------------------

        private sealed class TextBlock
        {
            public RectangleF Rect;
            public string Src;
            public string Dst;
            public int Index;
        }

        private sealed class ResultCanvas : Panel
        {
            public ResultCanvas()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint |
                    ControlStyles.ResizeRedraw,
                    true);
                DoubleBuffered = true;
                UpdateStyles();
                BackColor = Color.FromArgb(236, 239, 244);
                Cursor = Cursors.Arrow;
            }
        }

        // ------------------------------------------------------------------
        // 字段
        // ------------------------------------------------------------------

        private readonly Func<AppConfig> _configProvider;
        private Bitmap _image;          // 带标注的截图
        private Bitmap _sourceImage;    // 未标注截图，送 OCR
        private Bitmap _pastedImage;    // 百度返回的整图贴合译文图
        private ImageTranslationResult _lastResult;

        private readonly List<TextBlock> _textBlocks =
            new List<TextBlock>();
        private readonly List<int> _selectedBlocks = new List<int>();
        private Size _ocrSize = Size.Empty;
        private bool _copySourceText = true;   // 默认复制原文，翻译完成后切到译文
        private PointF _textSelectStart;
        private RectangleF _textSelectRect;
        private bool _textSelecting;
        private int _hoverBlock = -1;
        private string _copiedHint = "";

        private bool _viewPasted = true;       // true=贴合译文图，false=原图对照
        private bool _translating;
        private bool _autoFit = true;
        private string _status = "准备中…";
        private string _error = "";

        private float _zoom = 1f;
        private PointF _viewOffset = PointF.Empty;
        private bool _panning;
        private Point _panStart;
        private PointF _panOrigin;
        private Point _dragStartScreen;

        private ResultCanvas _canvas;
        private ToolStrip _toolbar;
        private Label _statusLabel;
        private ToolStripButton _viewButton;
        private ToolStripButton _contentButton;
        private ToolStripButton _copySelectedButton;
        private ToolStripButton _copyAllButton;
        private ToolStripButton _copyImageButton;
        private ToolStripButton _pinButton;
        private ToolStripButton _saveButton;
        private ToolStripButton _retryButton;
        private ToolStripButton _fitButton;

        private AppConfig Config
        {
            get
            {
                AppConfig config = _configProvider == null
                    ? null : _configProvider();
                return config ?? new AppConfig();
            }
        }

        /// <summary>当前显示的底图。可能为贴合译文图或原始截图。</summary>
        private Image CurrentImage
        {
            get
            {
                if (_viewPasted && _pastedImage != null)
                {
                    return _pastedImage;
                }
                return _image;
            }
        }

        /// <summary>没有贴合图时，需要在原图文字块上自己画译文。</summary>
        private bool NeedDrawTranslation
        {
            get { return !(_viewPasted && _pastedImage != null); }
        }

        public ImageTranslationForm(
            Func<AppConfig> configProvider, Bitmap image)
            : this(configProvider, image, null)
        {
        }

        /// <param name="image">带标注的截图，作为显示底图与兜底 OCR 源。</param>
        /// <param name="sourceImage">不含标注的原图，送 OCR 以避免标注干扰识别。</param>
        public ImageTranslationForm(
            Func<AppConfig> configProvider,
            Bitmap image,
            Bitmap sourceImage)
        {
            _configProvider = configProvider;
            _image = image;
            _sourceImage = sourceImage;

            Text = "图片翻译 — 直接在图上点选 / 框选文字复制";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = true;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);

            Size initial = InitialSize();
            ClientSize = initial;
            MinimumSize = new Size(560, 380);

            BuildToolbar();
            BuildCanvas();
            BuildStatusBar();

            Controls.Add(_canvas);      // Fill
            Controls.Add(_statusBar);   // Bottom（字段名见 BuildStatusBar）
            Controls.Add(_toolbar);     // Top

            Shown += delegate
            {
                FitToWindow();
                InvalidateCanvas();
            };
            Resize += delegate
            {
                if (_autoFit)
                {
                    FitToWindow();
                }
            };
        }

        private Panel _statusBar;

        private Size InitialSize()
        {
            int width = 900;
            int height = 640;
            try
            {
                Screen screen = Screen.PrimaryScreen;
                if (screen != null)
                {
                    Rectangle area = screen.WorkingArea;
                    width = Math.Min(width, Math.Max(560, area.Width - 80));
                    height = Math.Min(height, Math.Max(380, area.Height - 80));
                    if (_image != null)
                    {
                        width = Math.Min(width,
                            Math.Max(560, _image.Width + 60));
                        height = Math.Min(height,
                            Math.Max(380, _image.Height + 150));
                    }
                }
            }
            catch
            {
                // 无显示器环境（自检）下使用默认尺寸。
            }
            return new Size(width, height);
        }

        // ------------------------------------------------------------------
        // 界面
        // ------------------------------------------------------------------

        private void BuildToolbar()
        {
            _toolbar = new ToolStrip();
            _toolbar.Dock = DockStyle.Top;
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;
            _toolbar.LayoutStyle = ToolStripLayoutStyle.Flow;
            _toolbar.Padding = new Padding(6, 4, 6, 4);
            _toolbar.AutoSize = true;
            _toolbar.BackColor = Color.FromArgb(250, 251, 253);

            _viewButton = NewButton("显示：译文图", OnViewClicked);
            _contentButton = NewButton("选字复制：原文", OnContentClicked);
            _copySelectedButton = NewButton("复制选中 (Ctrl+C)", OnCopySelectedClicked);
            _copyAllButton = NewButton("复制全部译文", OnCopyAllClicked);
            _copyImageButton = NewButton("复制图片", OnCopyImageClicked);
            _toolbar.Items.Add(new ToolStripSeparator());
            _pinButton = NewButton("钉在桌面", OnPinClicked);
            _saveButton = NewButton("保存 PNG", OnSaveClicked);
            _toolbar.Items.Add(new ToolStripSeparator());
            _retryButton = NewButton("重新翻译", OnRetryClicked);
            _fitButton = NewButton("适应窗口", OnFitClicked);
            NewButton("关闭 (Esc)", OnCloseClicked);

            UpdateUi();
        }

        private ToolStripButton NewButton(string text, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.AutoSize = true;
            button.Click += handler;
            _toolbar.Items.Add(button);
            return button;
        }

        private void BuildCanvas()
        {
            _canvas = new ResultCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Paint += OnCanvasPaint;
            _canvas.MouseDown += OnCanvasMouseDown;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseUp += OnCanvasMouseUp;
            _canvas.MouseWheel += OnCanvasMouseWheel;
            _canvas.MouseLeave += delegate
            {
                if (_hoverBlock != -1)
                {
                    _hoverBlock = -1;
                    _canvas.Cursor = Cursors.Arrow;
                    InvalidateCanvas();
                }
            };
        }

        private void BuildStatusBar()
        {
            _statusBar = new Panel();
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 30;
            _statusBar.BackColor = Color.FromArgb(245, 247, 250);
            _statusBar.Padding = new Padding(10, 0, 10, 0);

            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.ForeColor = Color.FromArgb(60, 68, 80);
            _statusLabel.Text = _status;
            _statusBar.Controls.Add(_statusLabel);
        }

        private void UpdateUi()
        {
            bool hasResult = _lastResult != null;
            bool hasBlocks = _textBlocks.Count > 0;
            if (_viewButton != null)
            {
                _viewButton.Text = _viewPasted
                    ? "显示：译文图" : "显示：原图对照";
                _viewButton.Enabled = _pastedImage != null;
            }
            if (_contentButton != null)
            {
                _contentButton.Text = _copySourceText
                    ? "选字复制：原文" : "选字复制：译文";
                _contentButton.Enabled = hasBlocks;
            }
            if (_copySelectedButton != null)
            {
                _copySelectedButton.Enabled = hasBlocks;
            }
            if (_copyAllButton != null)
            {
                _copyAllButton.Enabled = hasResult;
            }
            if (_copyImageButton != null)
            {
                _copyImageButton.Enabled = CurrentImage != null;
            }
            if (_pinButton != null)
            {
                _pinButton.Enabled = CurrentImage != null;
            }
            if (_saveButton != null)
            {
                _saveButton.Enabled = CurrentImage != null;
            }
            if (_retryButton != null)
            {
                _retryButton.Enabled = !_translating;
            }
        }

        private void SetStatus(string text, bool isError)
        {
            _status = text;
            _error = isError ? text : "";
            if (_statusLabel != null && !_statusLabel.IsDisposed)
            {
                _statusLabel.ForeColor = isError
                    ? Color.FromArgb(190, 50, 50)
                    : Color.FromArgb(60, 68, 80);
                _statusLabel.Text = text;
            }
            UpdateUi();
            InvalidateCanvas();
        }

        private void InvalidateCanvas()
        {
            if (_canvas != null && !_canvas.IsDisposed)
            {
                _canvas.Invalidate();
            }
        }

        // ------------------------------------------------------------------
        // 翻译
        // ------------------------------------------------------------------

        /// <summary>
        /// 由托盘在 Show() 之前调用：窗口显示后自动发起一次图片翻译。
        /// 未配置密钥时不打断用户，只在状态栏给出提示。
        /// </summary>
        public void BeginTranslation()
        {
            EventHandler handler = null;
            handler = delegate
            {
                Shown -= handler;
                StartTranslation();
            };
            Shown += handler;
        }

        private void OnRetryClicked(object sender, EventArgs e)
        {
            StartTranslation();
        }

        private bool HasCredentials()
        {
            AppConfig config = Config;
            return !string.IsNullOrEmpty((config.AppId ?? "").Trim()) &&
                !string.IsNullOrEmpty(config.GetSecret());
        }

        private async void StartTranslation()
        {
            if (_translating || IsDisposed)
            {
                return;
            }
            if (_sourceImage == null && _image == null)
            {
                SetStatus("没有可翻译的图片", true);
                return;
            }
            if (!HasCredentials())
            {
                SetStatus(
                    "未配置百度翻译 AppID 或密钥，请右键托盘图标打开「设置」填写",
                    true);
                return;
            }

            _translating = true;
            _error = "";
            SetStatus("正在识别并翻译图片…", false);

            try
            {
                Bitmap ocrSource = _sourceImage != null
                    ? _sourceImage : _image;
                string note;
                byte[] bytes = BaiduImageTranslator.PrepareImage(
                    ocrSource, out note);

                // rect 坐标是「实际上传的那张图」的像素坐标。
                // PrepareImage 可能缩放/重编码，所以从最终字节流取尺寸，
                // 而不是直接用 ocrSource.Size，避免文字块整体偏移。
                Size sentSize;
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Image sent = Image.FromStream(stream))
                {
                    sentSize = sent.Size;
                }

                AppConfig config = Config;
                ImageTranslationResult result =
                    await BaiduImageTranslator.TranslateImageAsync(
                        config, bytes, config.ImageTargetLanguage);

                if (IsDisposed)
                {
                    return;
                }

                ApplyResult(result, sentSize);
            }
            catch (TranslationException ex)
            {
                if (!IsDisposed)
                {
                    SetStatus("翻译失败：" + ex.Message, true);
                }
            }
            catch (Exception ex)
            {
                Log.Error("图片翻译结果窗口翻译失败", ex);
                if (!IsDisposed)
                {
                    SetStatus("翻译失败：" + ex.Message, true);
                }
            }
            finally
            {
                _translating = false;
                if (!IsDisposed)
                {
                    UpdateUi();
                }
            }
        }

        private void ApplyResult(
            ImageTranslationResult result, Size ocrSize)
        {
            _lastResult = result;
            _textBlocks.Clear();
            _selectedBlocks.Clear();
            _hoverBlock = -1;
            _copiedHint = "";
            _ocrSize = ocrSize;

            Bitmap pasted = DecodePastedImage(result);
            if (_pastedImage != null)
            {
                _pastedImage.Dispose();
            }
            _pastedImage = pasted;
            _viewPasted = _pastedImage != null;

            BuildTextBlocks(result);

            // 运行时默认复制译文；自检（不走翻译路径）保持默认原文，
            // 两条路径的期望值在 SelfTest 中分别断言。
            _copySourceText = false;

            _autoFit = true;
            FitToWindow();

            int count = _textBlocks.Count;
            string tip = _pastedImage != null
                ? "译文已贴合到图片上" : "译文已叠加到图片上";
            SetStatus(
                tip + "，共识别 " + count +
                " 段。点选或框选文字块即可复制" +
                (_copySourceText ? "原文" : "译文") +
                "；Ctrl+A 全选，Esc 关闭。",
                false);
        }

        private static Bitmap DecodePastedImage(
            ImageTranslationResult result)
        {
            if (result == null || result.PastedImage == null ||
                result.PastedImage.Length == 0)
            {
                return null;
            }
            try
            {
                using (MemoryStream stream =
                    new MemoryStream(result.PastedImage))
                {
                    // Bitmap 会持有 stream，必须复制一份脱离流生命周期。
                    using (Bitmap loaded = new Bitmap(stream))
                    {
                        return new Bitmap(loaded);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("贴合译文图解码失败", ex);
                return null;
            }
        }

        private void BuildTextBlocks(ImageTranslationResult result)
        {
            if (result == null || result.Segments == null)
            {
                return;
            }
            for (int i = 0; i < result.Segments.Count; i++)
            {
                ImageSegmentInfo seg = result.Segments[i];
                RectangleF rect;
                if (!TryParseOcrRect(seg.Rect, out rect))
                {
                    continue;
                }
                TextBlock block = new TextBlock();
                block.Rect = rect;
                block.Src = (seg.Src ?? "").Trim();
                block.Dst = (seg.Dst ?? "").Trim();
                block.Index = i;
                _textBlocks.Add(block);
            }
        }

        /// <summary>rect 格式为 "left top width height"，空格或逗号分隔。</summary>
        private static bool TryParseOcrRect(
            string text, out RectangleF rect)
        {
            rect = RectangleF.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string[] parts = text.Split(
                new char[] { ' ', ',', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                return false;
            }

            float[] values = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(
                    parts[i],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[i]))
                {
                    return false;
                }
            }
            if (values[2] <= 0 || values[3] <= 0)
            {
                return false;
            }
            rect = new RectangleF(
                values[0], values[1], values[2], values[3]);
            return true;
        }

        /// <summary>OCR 坐标按「识别时图片尺寸 → 当前显示底图尺寸」映射。</summary>
        private RectangleF MapOcrRect(RectangleF r)
        {
            Image display = CurrentImage;
            if (display == null ||
                _ocrSize.Width <= 0 || _ocrSize.Height <= 0)
            {
                return r;
            }
            float sx = display.Width / (float)_ocrSize.Width;
            float sy = display.Height / (float)_ocrSize.Height;
            return new RectangleF(
                r.X * sx, r.Y * sy, r.Width * sx, r.Height * sy);
        }

        // ------------------------------------------------------------------
        // 视图 / 绘制
        // ------------------------------------------------------------------

        private void FitToWindow()
        {
            Image display = CurrentImage;
            if (display == null || _canvas == null)
            {
                return;
            }
            float availableWidth = Math.Max(
                40f, _canvas.ClientSize.Width - 24f);
            float availableHeight = Math.Max(
                40f, _canvas.ClientSize.Height - 24f);
            _zoom = Math.Min(
                availableWidth / display.Width,
                availableHeight / display.Height);
            _zoom = Math.Max(0.02f, Math.Min(_zoom, 4f));
            CenterImage();
            InvalidateCanvas();
        }

        private void CenterImage()
        {
            Image display = CurrentImage;
            if (display == null || _canvas == null)
            {
                return;
            }
            float width = display.Width * _zoom;
            float height = display.Height * _zoom;
            _viewOffset = new PointF(
                (_canvas.ClientSize.Width - width) / 2f,
                (_canvas.ClientSize.Height - height) / 2f);
        }

        private PointF ToImage(Point point)
        {
            float zoom = Math.Max(_zoom, 0.01f);
            return new PointF(
                (point.X - _viewOffset.X) / zoom,
                (point.Y - _viewOffset.Y) / zoom);
        }

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_canvas.BackColor);

            Image display = CurrentImage;
            if (display == null)
            {
                DrawMessage(g);
                return;
            }

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            GraphicsState state = g.Save();
            g.TranslateTransform(_viewOffset.X, _viewOffset.Y);
            g.ScaleTransform(_zoom, _zoom);
            g.DrawImage(display, 0, 0, display.Width, display.Height);
            DrawTextOverlay(g);
            g.Restore(state);

            DrawMessage(g);
        }

        /// <summary>
        /// 文字块热区：平时只画极淡的边框，不遮挡贴合图上的译文；
        /// 悬停 / 选中时高亮。没有贴合图时直接在块内画译文文字。
        /// </summary>
        private void DrawTextOverlay(Graphics g)
        {
            if (_textBlocks.Count == 0)
            {
                return;
            }
            float line = 1.4f / Math.Max(_zoom, 0.01f);
            for (int i = 0; i < _textBlocks.Count; i++)
            {
                TextBlock block = _textBlocks[i];
                RectangleF r = MapOcrRect(block.Rect);
                bool selected = _selectedBlocks.Contains(i);
                bool hovered = i == _hoverBlock;

                if (NeedDrawTranslation)
                {
                    string overlay = string.IsNullOrEmpty(block.Dst)
                        ? block.Src : block.Dst;
                    DrawTranslationInRect(g, overlay, r);
                }

                Color fill = selected
                    ? Color.FromArgb(88, 0, 122, 255)
                    : hovered
                        ? Color.FromArgb(32, 0, 122, 255)
                        : Color.FromArgb(0, 0, 122, 255);
                Color border = selected
                    ? Color.FromArgb(255, 0, 90, 210)
                    : hovered
                        ? Color.FromArgb(210, 0, 122, 255)
                        : Color.FromArgb(95, 0, 122, 255);

                if (fill.A > 0)
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                    {
                        g.FillRectangle(brush, r);
                    }
                }
                using (Pen pen = new Pen(border, line))
                {
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                }
            }

            if (_textSelecting)
            {
                RectangleF r = _textSelectRect;
                using (Pen pen = new Pen(
                    Color.FromArgb(0, 122, 255), line * 1.8f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                }
            }
        }

        /// <summary>降级路径：接口没给贴合图时，把译文画在原图文字块里。</summary>
        private void DrawTranslationInRect(
            Graphics g, string text, RectangleF r)
        {
            if (string.IsNullOrEmpty(text) || r.Width < 6f || r.Height < 6f)
            {
                return;
            }
            using (SolidBrush background = new SolidBrush(
                Color.FromArgb(198, 18, 38, 76)))
            {
                g.FillRectangle(background, r);
            }

            float size = Math.Max(9f, Math.Min(r.Height * 0.8f, 42f));
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    using (Font font = new Font(
                        "Microsoft YaHei UI", size,
                        FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        SizeF measured = g.MeasureString(
                            text, font,
                            (int)Math.Max(1f, r.Width), format);
                        if (measured.Width <= r.Width + 1f || size <= 9f)
                        {
                            using (SolidBrush brush =
                                new SolidBrush(Color.White))
                            {
                                g.DrawString(text, font, brush, r, format);
                            }
                            break;
                        }
                    }
                    size = Math.Max(9f, size * 0.82f);
                }
            }
        }

        private void DrawMessage(Graphics g)
        {
            string message = "";
            if (!string.IsNullOrEmpty(_error))
            {
                message = _error;
            }
            else if (_translating)
            {
                message = "正在识别并翻译图片…";
            }
            else if (CurrentImage == null)
            {
                message = "没有可显示的图片";
            }
            if (message.Length == 0)
            {
                return;
            }

            int maxWidth = Math.Max(220, _canvas.ClientSize.Width - 80);
            SizeF size = g.MeasureString(message, Font, maxWidth);
            float width = Math.Min(maxWidth, size.Width + 32f);
            float height = size.Height + 22f;
            float x = (_canvas.ClientSize.Width - width) / 2f;
            float y = 14f;
            using (GraphicsPath path = RoundedRect(x, y, width, height, 8f))
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(238, 255, 255, 255)))
            using (Pen pen = new Pen(Color.FromArgb(60, 0, 0, 0)))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            using (SolidBrush textBrush = new SolidBrush(
                string.IsNullOrEmpty(_error)
                    ? Color.FromArgb(40, 48, 60)
                    : Color.FromArgb(180, 45, 45)))
            {
                g.DrawString(
                    message, Font, textBrush,
                    new RectangleF(x + 16f, y + 11f,
                        width - 32f, height - 22f));
            }
        }

        private static GraphicsPath RoundedRect(
            float x, float y, float width, float height, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(x, y, d, d, 180f, 90f);
            path.AddArc(x + width - d, y, d, d, 270f, 90f);
            path.AddArc(x + width - d, y + height - d, d, d, 0f, 90f);
            path.AddArc(x, y + height - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        // ------------------------------------------------------------------
        // 鼠标：左键点选 / 框选文字，中键或右键拖动平移
        // ------------------------------------------------------------------

        private int HitBlock(PointF point)
        {
            for (int i = 0; i < _textBlocks.Count; i++)
            {
                if (MapOcrRect(_textBlocks[i].Rect).Contains(point))
                {
                    return i;
                }
            }
            return -1;
        }

        private void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (CurrentImage == null)
            {
                return;
            }
            if (e.Button == MouseButtons.Middle ||
                e.Button == MouseButtons.Right)
            {
                _panning = true;
                _panStart = e.Location;
                _panOrigin = _viewOffset;
                _canvas.Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            PointF point = ToImage(e.Location);
            _textSelecting = true;
            _textSelectStart = point;
            _textSelectRect = new RectangleF(point.X, point.Y, 0f, 0f);
            _dragStartScreen = e.Location;
            InvalidateCanvas();
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_panning)
            {
                _viewOffset = new PointF(
                    _panOrigin.X + (e.X - _panStart.X),
                    _panOrigin.Y + (e.Y - _panStart.Y));
                InvalidateCanvas();
                return;
            }

            PointF point = ToImage(e.Location);
            if (_textSelecting)
            {
                _textSelectRect = RectangleF.FromLTRB(
                    Math.Min(_textSelectStart.X, point.X),
                    Math.Min(_textSelectStart.Y, point.Y),
                    Math.Max(_textSelectStart.X, point.X),
                    Math.Max(_textSelectStart.Y, point.Y));
                InvalidateCanvas();
                return;
            }

            int hover = HitBlock(point);
            if (hover != _hoverBlock)
            {
                _hoverBlock = hover;
                _canvas.Cursor = hover >= 0
                    ? Cursors.Hand : Cursors.Arrow;
                InvalidateCanvas();
            }
        }

        private void OnCanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                _canvas.Cursor = Cursors.Arrow;
                return;
            }
            if (!_textSelecting)
            {
                return;
            }
            _textSelecting = false;

            bool dragged =
                Math.Abs(e.X - _dragStartScreen.X) > 4 ||
                Math.Abs(e.Y - _dragStartScreen.Y) > 4;
            ApplyTextSelection(!dragged);
            CopySelectedText();
            InvalidateCanvas();
        }

        private void OnCanvasMouseWheel(object sender, MouseEventArgs e)
        {
            if (CurrentImage == null ||
                (Control.ModifierKeys & Keys.Control) != Keys.Control)
            {
                return;
            }
            _autoFit = false;
            PointF anchor = ToImage(e.Location);
            float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            float next = Math.Max(0.02f,
                Math.Min(8f, _zoom * factor));
            if (Math.Abs(next - _zoom) < 0.0001f)
            {
                return;
            }
            _zoom = next;
            _viewOffset = new PointF(
                e.X - anchor.X * _zoom,
                e.Y - anchor.Y * _zoom);
            InvalidateCanvas();
        }

        // ------------------------------------------------------------------
        // 选字复制
        // ------------------------------------------------------------------

        private void ApplyTextSelection(bool clicked)
        {
            _selectedBlocks.Clear();
            for (int i = 0; i < _textBlocks.Count; i++)
            {
                RectangleF r = MapOcrRect(_textBlocks[i].Rect);
                bool hit = clicked
                    ? r.Contains(_textSelectStart)
                    : r.IntersectsWith(_textSelectRect);
                if (hit)
                {
                    _selectedBlocks.Add(i);
                }
            }
            SortSelectedBlocks();
        }

        /// <summary>按阅读顺序排序：Top 容差内视为同一行，行内按 Left。</summary>
        private void SortSelectedBlocks()
        {
            _selectedBlocks.Sort(delegate(int a, int b)
            {
                RectangleF ra = MapOcrRect(_textBlocks[a].Rect);
                RectangleF rb = MapOcrRect(_textBlocks[b].Rect);
                float tolerance =
                    Math.Max(ra.Height, rb.Height) * 0.6f;
                if (Math.Abs(ra.Top - rb.Top) > tolerance)
                {
                    return ra.Top.CompareTo(rb.Top);
                }
                return ra.Left.CompareTo(rb.Left);
            });
        }

        private string BuildSelectedText()
        {
            StringBuilder builder = new StringBuilder();
            float previousTop = float.NaN;
            float previousHeight = 0f;
            for (int i = 0; i < _selectedBlocks.Count; i++)
            {
                TextBlock block = _textBlocks[_selectedBlocks[i]];
                string text = _copySourceText
                    ? block.Src : block.Dst;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }
                RectangleF r = MapOcrRect(block.Rect);
                if (builder.Length > 0)
                {
                    bool sameLine = !float.IsNaN(previousTop) &&
                        Math.Abs(r.Top - previousTop) <=
                        Math.Max(r.Height, previousHeight) * 0.6f;
                    builder.Append(sameLine ? " " : "\r\n");
                }
                builder.Append(text);
                previousTop = r.Top;
                previousHeight = r.Height;
            }
            return builder.ToString();
        }

        private void CopySelectedText()
        {
            string text = BuildSelectedText();
            if (text.Length == 0)
            {
                _copiedHint = "选区内没有可复制的文字，请重新框选";
                SetStatus(_copiedHint, false);
                return;
            }
            TrySetClipboardText(
                text,
                "已复制 " + _selectedBlocks.Count + " 段" +
                (_copySourceText ? "原文" : "译文"));
        }

        private void SelectAllTextBlocks()
        {
            _selectedBlocks.Clear();
            for (int i = 0; i < _textBlocks.Count; i++)
            {
                _selectedBlocks.Add(i);
            }
            SortSelectedBlocks();
            CopySelectedText();
            InvalidateCanvas();
        }

        private string BuildAllText(bool source)
        {
            bool original = _copySourceText;
            List<int> originalSelection =
                new List<int>(_selectedBlocks);
            try
            {
                _copySourceText = source;
                _selectedBlocks.Clear();
                for (int i = 0; i < _textBlocks.Count; i++)
                {
                    _selectedBlocks.Add(i);
                }
                SortSelectedBlocks();
                return BuildSelectedText();
            }
            finally
            {
                _copySourceText = original;
                _selectedBlocks.Clear();
                _selectedBlocks.AddRange(originalSelection);
            }
        }

        private void TrySetClipboardText(string text, string successPrefix)
        {
            if (string.IsNullOrEmpty(text))
            {
                SetStatus("没有可复制的文字", false);
                return;
            }
            try
            {
                Clipboard.SetText(text);
                string preview = text
                    .Replace("\r\n", " ").Replace("\n", " ");
                if (preview.Length > 46)
                {
                    preview = preview.Substring(0, 46) + "…";
                }
                _copiedHint = successPrefix + "：" + preview;
                SetStatus(_copiedHint, false);
            }
            catch (Exception ex)
            {
                Log.Error("复制图片选中文字失败", ex);
                SetStatus("复制失败，剪贴板可能被其它程序占用", true);
            }
        }

        // ------------------------------------------------------------------
        // 工具条动作
        // ------------------------------------------------------------------

        private void OnViewClicked(object sender, EventArgs e)
        {
            _viewPasted = !_viewPasted;
            _autoFit = true;
            FitToWindow();
            SetStatus(
                _viewPasted
                    ? "已切换到译文贴合图，文字块仍可选中复制"
                    : "已切换到原图对照，点选文字块可复制译文",
                false);
        }

        private void OnContentClicked(object sender, EventArgs e)
        {
            _copySourceText = !_copySourceText;
            SetStatus(
                "选字复制已切换为" +
                (_copySourceText ? "原文" : "译文") +
                "，可重新点选文字块复制",
                false);
            if (_selectedBlocks.Count > 0)
            {
                CopySelectedText();
            }
        }

        private void OnCopySelectedClicked(object sender, EventArgs e)
        {
            if (_selectedBlocks.Count == 0)
            {
                SelectAllTextBlocks();
                return;
            }
            CopySelectedText();
        }

        private void OnCopyAllClicked(object sender, EventArgs e)
        {
            if (_lastResult == null)
            {
                return;
            }
            string text = _lastResult.SumDst;
            if (string.IsNullOrEmpty(text))
            {
                text = BuildAllText(false);
            }
            TrySetClipboardText(text, "已复制全部译文");
        }

        private void OnCopyImageClicked(object sender, EventArgs e)
        {
            Image display = CurrentImage;
            if (display == null)
            {
                return;
            }
            try
            {
                Clipboard.SetImage(new Bitmap(display));
                SetStatus("图片已复制到剪贴板", false);
            }
            catch (Exception ex)
            {
                Log.Error("复制图片失败", ex);
                SetStatus("复制图片失败：" + ex.Message, true);
            }
        }

        private void OnPinClicked(object sender, EventArgs e)
        {
            Image display = CurrentImage;
            if (display == null)
            {
                return;
            }
            try
            {
                Bitmap copy = new Bitmap(display);
                Point location = new Point(Left + 40, Top + 40);
                PinnedImageForm pin =
                    new PinnedImageForm(copy, location);
                copy = null;
                pin.Show();
                SetStatus("已钉在桌面", false);
            }
            catch (Exception ex)
            {
                Log.Error("贴图失败", ex);
                SetStatus("贴图失败：" + ex.Message, true);
            }
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            Image display = CurrentImage;
            if (display == null)
            {
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "保存翻译结果图片";
                dialog.Filter = "PNG 图片 (*.png)|*.png";
                dialog.DefaultExt = "png";
                dialog.FileName = "翻译截图_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    using (Bitmap copy = new Bitmap(display))
                    {
                        copy.Save(dialog.FileName, ImageFormat.Png);
                    }
                    SetStatus("已保存：" + dialog.FileName, false);
                }
                catch (Exception ex)
                {
                    Log.Error("保存翻译结果失败", ex);
                    SetStatus("保存失败：" + ex.Message, true);
                }
            }
        }

        private void OnFitClicked(object sender, EventArgs e)
        {
            _autoFit = true;
            FitToWindow();
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            Close();
        }

        // ------------------------------------------------------------------
        // 键盘 / 生命周期
        // ------------------------------------------------------------------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAllTextBlocks();
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedText();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (_image != null)
            {
                _image.Dispose();
                _image = null;
            }
            if (_sourceImage != null)
            {
                _sourceImage.Dispose();
                _sourceImage = null;
            }
            if (_pastedImage != null)
            {
                _pastedImage.Dispose();
                _pastedImage = null;
            }
        }
    }
}
