using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 图片显示区：负责缩放、拖动平移、透明棋盘底、裁剪选框与操作提示条。
    /// 位图的所有权在窗体，画布只负责画，不 Dispose 传进来的图片。
    /// </summary>
    internal sealed class ImageCanvas : Control
    {
        private static readonly Color CanvasBack = Color.FromArgb(17, 18, 20);
        private static Bitmap _checker;
        private static Bitmap CheckerPattern()
        {
            if (_checker == null)
            {
                Bitmap tile = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(tile))
                {
                    g.Clear(Color.FromArgb(29, 30, 34));
                    using (SolidBrush brush =
                        new SolidBrush(Color.FromArgb(36, 38, 43)))
                    {
                        g.FillRectangle(brush, 0, 0, 8, 8);
                        g.FillRectangle(brush, 8, 8, 8, 8);
                    }
                }
                _checker = tile;
            }
            return _checker;
        }

        private readonly System.Windows.Forms.Timer _toastTimer;
        private Bitmap _image;
        private float _zoom = 1f;
        private float _offsetX;
        private float _offsetY;
        private bool _panning;
        private Point _panStart;
        private float _panOriginX;
        private float _panOriginY;
        private bool _cropMode;
        private bool _cropDragging;
        private Point _cropStart;
        private Rectangle _cropRect;
        private string _toast;
        private DateTime _toastUntil;

        public ImageCanvas()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = CanvasBack;
            AllowDrop = true;
            TabStop = true;

            _toastTimer = new System.Windows.Forms.Timer();
            _toastTimer.Interval = 120;
            _toastTimer.Tick += delegate
            {
                if (DateTime.UtcNow >= _toastUntil)
                {
                    _toast = null;
                    _toastTimer.Stop();
                }
                Invalidate();
            };
        }

        /// <summary>缩放比例变化（工具栏与页脚需要同步百分比）。</summary>
        public event EventHandler ViewChanged;

        /// <summary>裁剪框被确认，参数是图片像素坐标。</summary>
        public event Action<Rectangle> CropConfirmed;

        /// <summary>拖入文件（窗体统一处理打开逻辑）。</summary>
        public event Action<string> FileDropped;

        public Bitmap Image
        {
            get { return _image; }
        }

        public float Zoom
        {
            get { return _zoom; }
        }

        public int ZoomPercent
        {
            get { return (int)Math.Round(_zoom * 100f); }
        }

        public bool IsCropMode
        {
            get { return _cropMode; }
        }

        public void SetImage(Bitmap image, bool resetView)
        {
            _image = image;
            if (image == null)
            {
                _zoom = 1f;
                _offsetX = 0f;
                _offsetY = 0f;
                Invalidate();
                RaiseViewChanged();
                return;
            }
            if (resetView)
            {
                FitToWindow();
            }
            else
            {
                CenterImage();
                Invalidate();
                RaiseViewChanged();
            }
        }

        public void FitToWindow()
        {
            if (_image == null || ClientSize.Width < 20 ||
                ClientSize.Height < 20)
            {
                return;
            }
            float availableW = ClientSize.Width - 56f;
            float availableH = ClientSize.Height - 56f;
            float scale = Math.Min(
                availableW / _image.Width, availableH / _image.Height);
            if (scale > 1f)
            {
                // 小图不放大，保持 100% 更清晰。
                scale = 1f;
            }
            _zoom = ClampZoom(scale);
            CenterImage();
            RaiseViewChanged();
        }

        public void ActualSize()
        {
            if (_image == null)
            {
                return;
            }
            _zoom = 1f;
            CenterImage();
            RaiseViewChanged();
        }

        public void ZoomBy(float factor)
        {
            if (_image == null)
            {
                return;
            }
            ZoomTo(_zoom * factor,
                new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        public void ZoomTo(float zoom, Point anchor)
        {
            if (_image == null)
            {
                return;
            }
            float next = ClampZoom(zoom);
            if (Math.Abs(next - _zoom) < 0.0001f)
            {
                return;
            }
            float ratio = next / _zoom;
            _offsetX = anchor.X - (anchor.X - _offsetX) * ratio;
            _offsetY = anchor.Y - (anchor.Y - _offsetY) * ratio;
            _zoom = next;
            ClampOffset();
            Invalidate();
            RaiseViewChanged();
        }

        public void BeginCrop()
        {
            if (_image == null)
            {
                return;
            }
            _cropMode = true;
            _cropRect = Rectangle.Empty;
            Cursor = Cursors.Cross;
            Invalidate();
        }

        public void CancelCrop()
        {
            _cropMode = false;
            _cropDragging = false;
            _cropRect = Rectangle.Empty;
            Cursor = Cursors.Default;
            Invalidate();
        }

        /// <summary>当前裁剪框换算成图片像素坐标。</summary>
        public Rectangle CropSelection
        {
            get
            {
                if (_image == null || _cropRect.Width < 2 ||
                    _cropRect.Height < 2 || _zoom <= 0f)
                {
                    return Rectangle.Empty;
                }
                int x = (int)Math.Round((_cropRect.X - _offsetX) / _zoom);
                int y = (int)Math.Round((_cropRect.Y - _offsetY) / _zoom);
                int w = (int)Math.Round(_cropRect.Width / _zoom);
                int h = (int)Math.Round(_cropRect.Height / _zoom);
                Rectangle result = new Rectangle(x, y, w, h);
                return Rectangle.Intersect(result,
                    new Rectangle(0, 0, _image.Width, _image.Height));
            }
        }

        public void ConfirmCrop()
        {
            Rectangle selection = CropSelection;
            if (selection.Width < 2 || selection.Height < 2)
            {
                ShowToast("请先拖出要保留的区域");
                return;
            }
            Action<Rectangle> handler = CropConfirmed;
            if (handler != null)
            {
                handler(selection);
            }
        }

        public void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = DateTime.UtcNow.AddMilliseconds(2600);
            _toastTimer.Start();
            Invalidate();
        }

        private static float ClampZoom(float zoom)
        {
            if (zoom < 0.02f)
            {
                return 0.02f;
            }
            if (zoom > 32f)
            {
                return 32f;
            }
            return zoom;
        }

        private RectangleF ImageRect()
        {
            if (_image == null)
            {
                return RectangleF.Empty;
            }
            return new RectangleF(_offsetX, _offsetY,
                _image.Width * _zoom, _image.Height * _zoom);
        }

        private void CenterImage()
        {
            if (_image == null)
            {
                return;
            }
            _offsetX = (ClientSize.Width - _image.Width * _zoom) / 2f;
            _offsetY = (ClientSize.Height - _image.Height * _zoom) / 2f;
        }

        /// <summary>放大后不要让人把图片整张拖出视野。</summary>
        private void ClampOffset()
        {
            if (_image == null)
            {
                return;
            }
            float width = _image.Width * _zoom;
            float height = _image.Height * _zoom;
            if (width <= ClientSize.Width)
            {
                _offsetX = (ClientSize.Width - width) / 2f;
            }
            else
            {
                float minX = ClientSize.Width - width - 40f;
                float maxX = 40f;
                if (_offsetX < minX)
                {
                    _offsetX = minX;
                }
                if (_offsetX > maxX)
                {
                    _offsetX = maxX;
                }
            }
            if (height <= ClientSize.Height)
            {
                _offsetY = (ClientSize.Height - height) / 2f;
            }
            else
            {
                float minY = ClientSize.Height - height - 40f;
                float maxY = 40f;
                if (_offsetY < minY)
                {
                    _offsetY = minY;
                }
                if (_offsetY > maxY)
                {
                    _offsetY = maxY;
                }
            }
        }

        private void RaiseViewChanged()
        {
            EventHandler handler = ViewChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_image != null)
            {
                ClampOffset();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(CanvasBack);

            if (_image == null)
            {
                DarkUi.Text2(g, "拖一张图片进来，或点击右上角「打开图片」",
                    DarkUi.FontStrong, DarkUi.TextFaint,
                    new Rectangle(0, ClientSize.Height / 2 - 22,
                        ClientSize.Width, 22),
                    DarkUi.CenterMiddle);
                DarkUi.Text2(g,
                    "支持 PNG · JPG · BMP · GIF · TIFF",
                    DarkUi.FontSmall, DarkUi.TextFaint,
                    new Rectangle(0, ClientSize.Height / 2 + 4,
                        ClientSize.Width, 20),
                    DarkUi.CenterMiddle);
                return;
            }

            RectangleF rect = ImageRect();

            // 透明区域用棋盘格垫底，一眼能看出哪里是透明的。
            using (TextureBrush brush = new TextureBrush(CheckerPattern()))
            {
                brush.TranslateTransform(rect.X, rect.Y);
                g.FillRectangle(brush, rect);
            }

            g.InterpolationMode = _zoom >= 2.5f
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, rect);

            using (Pen pen = new Pen(Color.FromArgb(56, 255, 255, 255), 1f))
            {
                g.DrawRectangle(pen, rect.X, rect.Y,
                    rect.Width - 1f, rect.Height - 1f);
            }

            if (_cropMode)
            {
                DrawCropMask(g, rect);
            }

            DrawToast(g);
        }

        private void DrawCropMask(Graphics g, RectangleF imageRect)
        {
            RectangleF selection = _cropRect;
            if (selection.Width > 0 && selection.Height > 0)
            {
                using (SolidBrush mask =
                    new SolidBrush(Color.FromArgb(150, 8, 9, 11)))
                {
                    g.FillRectangle(mask, 0f, 0f,
                        ClientSize.Width, selection.Top);
                    g.FillRectangle(mask, 0f, selection.Bottom,
                        ClientSize.Width,
                        Math.Max(0f, ClientSize.Height - selection.Bottom));
                    g.FillRectangle(mask, 0f, selection.Top,
                        selection.Left, selection.Height);
                    g.FillRectangle(mask, selection.Right, selection.Top,
                        Math.Max(0f, ClientSize.Width - selection.Right),
                        selection.Height);
                }

                using (Pen pen = new Pen(DarkUi.Accent, 1.5f))
                {
                    g.DrawRectangle(pen, selection.X, selection.Y,
                        selection.Width, selection.Height);
                }

                Rectangle crop = CropSelection;
                if (crop.Width > 0 && crop.Height > 0)
                {
                    string label = crop.Width + " × " + crop.Height;
                    Size size = TextRenderer.MeasureText(
                        label, DarkUi.FontSmall);
                    float labelW = size.Width + 16f;
                    float labelH = 22f;
                    float labelX = selection.X;
                    float labelY = selection.Y - labelH - 6f;
                    if (labelY < 4f)
                    {
                        labelY = selection.Y + 6f;
                    }
                    RectangleF chip = new RectangleF(
                        labelX, labelY, labelW, labelH);
                    using (GraphicsPath path = DarkUi.Rounded(chip, 6f))
                    using (SolidBrush brush = new SolidBrush(DarkUi.Accent))
                    {
                        g.FillPath(brush, path);
                    }
                    DarkUi.Text2(g, label, DarkUi.FontSmall, Color.White,
                        Rectangle.Round(chip), DarkUi.CenterMiddle);
                }
            }
            else
            {
                using (SolidBrush mask =
                    new SolidBrush(Color.FromArgb(90, 8, 9, 11)))
                {
                    g.FillRectangle(mask, 0f, 0f,
                        ClientSize.Width, ClientSize.Height);
                }
                DarkUi.Text2(g, "拖出要保留的区域，Enter 确认，Esc 取消",
                    DarkUi.FontStrong, DarkUi.Text,
                    new Rectangle(0, 24, ClientSize.Width, 22),
                    DarkUi.CenterMiddle);
                if (imageRect.Width > 0)
                {
                    // 保持 imageRect 参与绘制，避免高 DPI 下裁剪框错位。
                    using (Pen pen = new Pen(
                        Color.FromArgb(90, DarkUi.Accent), 1f))
                    {
                        g.DrawRectangle(pen, imageRect.X, imageRect.Y,
                            imageRect.Width - 1f, imageRect.Height - 1f);
                    }
                }
            }
        }

        private void DrawToast(Graphics g)
        {
            if (string.IsNullOrEmpty(_toast))
            {
                return;
            }
            Size size = TextRenderer.MeasureText(_toast, DarkUi.FontSmall);
            RectangleF box = new RectangleF(
                18f, ClientSize.Height - 54f,
                size.Width + 30f, 34f);
            using (GraphicsPath path = DarkUi.Rounded(box, 9f))
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(242, 40, 44, 50)))
            {
                g.FillPath(brush, path);
                using (Pen pen = new Pen(DarkUi.Border, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }
            using (SolidBrush dot = new SolidBrush(DarkUi.Accent))
            {
                g.FillEllipse(dot, box.X + 12f, box.Y + 14f, 6f, 6f);
            }
            DarkUi.Text2(g, _toast, DarkUi.FontSmall, DarkUi.Text,
                new Rectangle((int)box.X + 24, (int)box.Y,
                    (int)box.Width - 30, (int)box.Height),
                DarkUi.LeftMiddle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (CanFocus)
            {
                // 拿到焦点后滚轮才是自己的，否则消息会跑去别的控件。
                Focus();
            }
            if (_image == null || e.Button != MouseButtons.Left)
            {
                return;
            }
            if (_cropMode)
            {
                _cropDragging = true;
                _cropStart = e.Location;
                _cropRect = new Rectangle(e.Location, Size.Empty);
                Invalidate();
                return;
            }
            _panning = true;
            _panStart = e.Location;
            _panOriginX = _offsetX;
            _panOriginY = _offsetY;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_cropDragging)
            {
                int x = Math.Min(_cropStart.X, e.X);
                int y = Math.Min(_cropStart.Y, e.Y);
                int w = Math.Abs(e.X - _cropStart.X);
                int h = Math.Abs(e.Y - _cropStart.Y);
                _cropRect = new Rectangle(x, y, w, h);
                Invalidate();
                return;
            }
            if (_panning)
            {
                _offsetX = _panOriginX + (e.X - _panStart.X);
                _offsetY = _panOriginY + (e.Y - _panStart.Y);
                ClampOffset();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_panning)
            {
                _panning = false;
                Cursor = _cropMode ? Cursors.Cross : Cursors.Default;
            }
            _cropDragging = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_image == null || _cropMode)
            {
                return;
            }
            if (_zoom > 0.999f && _zoom < 1.001f)
            {
                FitToWindow();
            }
            else
            {
                ActualSize();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_image == null)
            {
                return;
            }
            float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            ZoomTo(_zoom * factor, e.Location);
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = HasImageFile(e) ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] files = e.Data.GetData(DataFormats.FileDrop)
                as string[];
            if (files == null || files.Length == 0)
            {
                return;
            }
            Action<string> handler = FileDropped;
            if (handler != null && ImageToolKit.IsImagePath(files[0]))
            {
                handler(files[0]);
            }
        }

        private static bool HasImageFile(DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop)
                as string[];
            return files != null && files.Length > 0 &&
                ImageToolKit.IsImagePath(files[0]);
        }
    }

    /// <summary>
    /// 右侧信息面板：图片名称 / 类型 / 大小 / 尺寸 / 位置 / 修改时间，
    /// 每一行右侧带一个就地操作按钮（重命名、转格式、压缩、改尺寸、打开）。
    /// 全部自绘，避免十几种控件在深色背景上出现白底。
    /// </summary>
    internal sealed class ImageInfoPanel : Control
    {
        private const int CardLeft = 14;
        private const int CardTop = 14;
        private const int RowHeight = 50;
        private const int ButtonWidth = 62;
        private const int ButtonHeight = 26;

        private readonly List<Rectangle> _actionRects =
            new List<Rectangle>();
        private readonly List<string> _actionKeys = new List<string>();
        private readonly ToolTip _tips;

        private ImageFileInfo _info;
        private bool _dirty;
        private int _hoverIndex = -1;

        public ImageInfoPanel(ToolTip tips)
        {
            _tips = tips;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = DarkUi.Window;
            AllowDrop = true;
            TabStop = false;
        }

        public event Action<string> ActionClicked;
        public event Action<string> FileDropped;

        public void SetInfo(ImageFileInfo info, bool dirty)
        {
            _info = info;
            _dirty = dirty;
            BuildHitRects();
            Invalidate();
        }

        private int CardWidth
        {
            get { return Math.Max(160, Width - CardLeft * 2); }
        }

        private int CardHeight
        {
            get
            {
                if (_info == null)
                {
                    return 94;
                }
                return 52 + RowHeight * 6 + 12;
            }
        }

        private void BuildHitRects()
        {
            _actionRects.Clear();
            _actionKeys.Clear();
            string[] keys = new string[]
            {
                "rename", "convert", "compress", "resize", "folder", ""
            };
            int right = CardLeft + CardWidth - 14 - ButtonWidth;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].Length == 0)
                {
                    continue;
                }
                int top = CardTop + 52 + i * RowHeight +
                    (RowHeight - ButtonHeight) / 2 - 2;
                _actionRects.Add(new Rectangle(
                    right, top, ButtonWidth, ButtonHeight));
                _actionKeys.Add(keys[i]);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            BuildHitRects();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(DarkUi.Window);

            RectangleF card = new RectangleF(
                CardLeft, CardTop, CardWidth, CardHeight);
            using (GraphicsPath path = DarkUi.Rounded(card, 12f))
            using (SolidBrush brush = new SolidBrush(DarkUi.Card))
            {
                g.FillPath(brush, path);
                using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            DarkUi.Text2(g, "图片信息", DarkUi.FontStrong, DarkUi.Text,
                new Rectangle(CardLeft + 14, CardTop + 14, 160, 20),
                DarkUi.LeftMiddle);

            if (_info == null)
            {
                DarkUi.Text2(g, "还没有打开图片", DarkUi.FontSmall,
                    DarkUi.TextFaint,
                    new Rectangle(CardLeft + 14, CardTop + 56, 200, 20),
                    DarkUi.LeftMiddle);
                return;
            }

            string[] values = new string[]
            {
                _info.Name,
                _info.FormatName + " · " + _info.Bits,
                ImageToolKit.FormatSize(_info.SizeBytes) +
                    (_dirty ? "（未保存）" : ""),
                _info.Width + " × " + _info.Height,
                ImageToolKit.ShortenPath(_info.Folder, 24),
                ImageToolKit.FormatDateTime(_info.Modified)
            };
            string[] labels = new string[]
            {
                "名称", "类型", "文件大小", "尺寸", "位置", "修改时间"
            };
            string[] actions = new string[]
            {
                "重命名", "转格式", "压缩", "改尺寸", "打开"
            };

            int labelX = CardLeft + 14;
            int buttonLeft = CardLeft + CardWidth - 14 - ButtonWidth;
            int valueWidth = Math.Max(40,
                buttonLeft - 10 - labelX);

            for (int i = 0; i < labels.Length; i++)
            {
                int top = CardTop + 52 + i * RowHeight;
                DarkUi.Text2(g, labels[i], DarkUi.FontSmall,
                    DarkUi.TextFaint,
                    new Rectangle(labelX, top + 2, valueWidth, 16),
                    DarkUi.LeftMiddle);
                DarkUi.Text2(g, values[i], DarkUi.FontBody, DarkUi.Text,
                    new Rectangle(labelX, top + 18,
                        valueWidth + ButtonWidth + 10, 20),
                    DarkUi.LeftMiddleClip);
            }

            for (int i = 0; i < _actionRects.Count; i++)
            {
                bool hover = _hoverIndex == i;
                Rectangle box = _actionRects[i];
                RectangleF shape = new RectangleF(
                    box.X + 0.5f, box.Y + 0.5f,
                    box.Width - 1f, box.Height - 1f);
                using (GraphicsPath path = DarkUi.Rounded(shape, 7f))
                using (SolidBrush brush = new SolidBrush(
                    hover ? DarkUi.CardHover : DarkUi.Surface))
                {
                    g.FillPath(brush, path);
                    using (Pen pen = new Pen(
                        hover ? DarkUi.Accent : DarkUi.Border, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
                DarkUi.Text2(g, actions[i], DarkUi.FontSmall,
                    hover ? DarkUi.Text : DarkUi.TextDim,
                    box, DarkUi.CenterMiddle);
            }

            int hintTop = CardTop + CardHeight + 18;
            string[] hints = new string[]
            {
                "拖动平移 · 滚轮缩放",
                "← → 切换同目录图片",
                "Ctrl+Z 撤销 · Ctrl+S 另存为",
                "双击画面在适应窗口与 100% 之间切换"
            };
            for (int i = 0; i < hints.Length; i++)
            {
                DarkUi.Text2(g, hints[i], DarkUi.FontTiny,
                    DarkUi.TextFaint,
                    new Rectangle(CardLeft + 4, hintTop + i * 20,
                        CardWidth - 8, 18),
                    DarkUi.LeftMiddleClip);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitIndex(e.Location);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                if (index >= 0 && _tips != null)
                {
                    string[] tips = new string[]
                    {
                        "重命名这个文件",
                        "转换图片格式（PNG / JPG / BMP / GIF / TIFF）",
                        "压小文件体积",
                        "修改图片尺寸",
                        "在资源管理器中打开所在文件夹"
                    };
                    if (index < tips.Length)
                    {
                        _tips.Show(tips[index], this,
                            e.X, e.Y + 18, 3000);
                    }
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int index = HitIndex(e.Location);
            if (index >= 0 && index < _actionKeys.Count)
            {
                Action<string> handler = ActionClicked;
                if (handler != null)
                {
                    handler(_actionKeys[index]);
                }
            }
        }

        private int HitIndex(Point point)
        {
            for (int i = 0; i < _actionRects.Count; i++)
            {
                if (_actionRects[i].Contains(point))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            string[] files = e.Data.GetData(DataFormats.FileDrop)
                as string[];
            e.Effect = files != null && files.Length > 0 &&
                ImageToolKit.IsImagePath(files[0])
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] files = e.Data.GetData(DataFormats.FileDrop)
                as string[];
            if (files == null || files.Length == 0)
            {
                return;
            }
            Action<string> handler = FileDropped;
            if (handler != null && ImageToolKit.IsImagePath(files[0]))
            {
                handler(files[0]);
            }
        }
    }

    /// <summary>底部居中的胶囊工具条：图标按钮 + 分组竖线。</summary>
    internal sealed class PillBar : Control
    {
        private readonly List<GlyphButton> _buttons =
            new List<GlyphButton>();
        private readonly List<int> _separators = new List<int>();
        private readonly List<int> _separatorAfter =
            new List<int>();

        public PillBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = DarkUi.Window;
            Height = 46;
            TabStop = false;
        }

        public void AddButton(GlyphButton button)
        {
            button.SurfaceColor = DarkUi.Surface;
            _buttons.Add(button);
            Controls.Add(button);
        }

        public void AddSeparator()
        {
            _separatorAfter.Add(_buttons.Count);
        }

        /// <summary>按内容宽度重新排版并居中到父容器。</summary>
        public void LayoutItems()
        {
            _separators.Clear();
            int x = 13;
            int lastIndex = 0;
            for (int i = 0; i < _buttons.Count; i++)
            {
                GlyphButton button = _buttons[i];
                button.Location = new Point(x, (Height - 34) / 2);
                x += 34 + 2;
                lastIndex = i + 1;
                for (int s = 0; s < _separatorAfter.Count; s++)
                {
                    if (_separatorAfter[s] == lastIndex)
                    {
                        _separators.Add(x + 5);
                        x += 12;
                    }
                }
            }
            Width = x + 9;
        }
    }
}
