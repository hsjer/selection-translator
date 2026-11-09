using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 轻量图片查看器：无边框深色窗口 + 自绘标题栏 + 悬浮图标工具条，
    /// 右侧是「图片信息」卡片（重命名 / 转格式 / 压缩 / 改尺寸 / 打开目录）。
    /// 所有编辑都在内存里进行，只有明确保存时才写文件。
    /// </summary>
    internal sealed class ImageViewerForm : ChromeForm
    {
        private const int HeaderHeightValue = 46;
        private const int InfoWidth = 308;
        private const int FooterHeight = 64;

        /// <summary>记住上次打开图片的目录，下次文件框直接从这里开始。</summary>
        private static string _lastFolder;

        private readonly ImageCanvas _canvas;
        private readonly ImageInfoPanel _info;
        private readonly PillBar _pill;
        private readonly ToolTip _tips;
        private readonly GlyphButton _openButton;
        private readonly GlyphButton _minimizeButton;
        private readonly GlyphButton _maximizeButton;
        private readonly GlyphButton _closeButton;
        private GlyphButton _cropButton;
        private readonly List<Bitmap> _undo = new List<Bitmap>();

        private string _path;
        private List<string> _siblings = new List<string>();
        private ImageFileInfo _fileInfo;
        private bool _cropMode;

        public ImageViewerForm()
        {
            Text = "图片查看器 - " + AppInfo.Name;
            ClientSize = new Size(1080, 720);
            MinimumSize = new Size(760, 520);
            KeyPreview = true;

            _tips = new ToolTip();
            _tips.InitialDelay = 420;
            _tips.ReshowDelay = 120;

            _canvas = new ImageCanvas();
            _canvas.ViewChanged += delegate { Invalidate(); };
            _canvas.CropConfirmed += ApplyCrop;
            _canvas.FileDropped += delegate(string file)
            {
                OpenPath(file);
            };
            Controls.Add(_canvas);

            _info = new ImageInfoPanel(_tips);
            _info.ActionClicked += OnInfoAction;
            _info.FileDropped += delegate(string file)
            {
                OpenPath(file);
            };
            Controls.Add(_info);

            _pill = new PillBar();
            Controls.Add(_pill);

            _openButton = new GlyphButton(
                GlyphKind.Open, DarkUi.Window, 34, 30);
            _openButton.DangerHover = false;
            _openButton.Click += delegate { OpenImageDialog(); };
            _tips.SetToolTip(_openButton, "打开图片 (Ctrl+O)");
            Controls.Add(_openButton);

            _minimizeButton = new GlyphButton(
                GlyphKind.Minimize, DarkUi.Window, 38, 30);
            _minimizeButton.IconScale = 0.85f;
            _minimizeButton.Click += delegate
            {
                WindowState = FormWindowState.Minimized;
            };
            _tips.SetToolTip(_minimizeButton, "最小化");
            Controls.Add(_minimizeButton);

            _maximizeButton = new GlyphButton(
                GlyphKind.Maximize, DarkUi.Window, 38, 30);
            _maximizeButton.IconScale = 0.85f;
            _maximizeButton.Click += delegate { ToggleMaximize(); };
            _tips.SetToolTip(_maximizeButton, "最大化");
            Controls.Add(_maximizeButton);

            _closeButton = new GlyphButton(
                GlyphKind.Close, DarkUi.Window, 38, 30);
            _closeButton.DangerHover = true;
            _closeButton.IconScale = 0.85f;
            _closeButton.Click += delegate { Close(); };
            _tips.SetToolTip(_closeButton, "关闭");
            Controls.Add(_closeButton);

            BuildToolbar();

            AllowDrop = true;
            DragEnter += OnViewerDragEnter;
            DragDrop += OnViewerDragDrop;

            UpdateTitle();
            DoLayout();
        }

        protected override int HeaderHeight
        {
            get { return HeaderHeightValue; }
        }

        private bool IsDirty
        {
            get { return _undo.Count > 0; }
        }

        // ------------------------------------------------------------------
        // 对外入口
        // ------------------------------------------------------------------

        /// <summary>用独立窗口打开一张图片（托盘菜单、命令行、拖拽共用）。</summary>
        public static void ShowPath(string path)
        {
            if (!ImageToolKit.IsImagePath(path))
            {
                ImageMessage.Info(null, "打开图片",
                    "这个文件不是支持的图片格式。\r\n支持 PNG / JPG / BMP / GIF / TIFF。");
                return;
            }
            ImageViewerForm form = new ImageViewerForm();
            form.OpenPath(path);
            if (!form.HasImage)
            {
                form.Dispose();
                return;
            }
            form.Show();
            form.Activate();
        }

        /// <summary>弹文件选择框再打开。</summary>
        public static void ShowOpenDialog(IWin32Window owner)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "打开图片";
                dialog.Filter = ImageToolKit.OpenFilter;
                dialog.CheckFileExists = true;
                string folder = Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures);
                if (!string.IsNullOrEmpty(_lastFolder) &&
                    Directory.Exists(_lastFolder))
                {
                    folder = _lastFolder;
                }
                if (Directory.Exists(folder))
                {
                    dialog.InitialDirectory = folder;
                }
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                {
                    ShowPath(dialog.FileName);
                }
            }
        }

        public void OpenPath(string path)
        {
            ShowFile(path, true);
        }

        /// <summary>是否已经成功载入一张图（命令行打开时用来判断要不要显示窗口）。</summary>
        public bool HasImage
        {
            get { return _canvas.Image != null; }
        }

        // ------------------------------------------------------------------
        // 布局与绘制
        // ------------------------------------------------------------------

        /// <summary>标题行左侧的小「照片」标记，纯手绘，不依赖图标资源。</summary>
        private static void DrawPhotoMark(
            Graphics g, RectangleF box, Color ink)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(ink, 1.2f))
            {
                pen.LineJoin = LineJoin.Round;
                using (GraphicsPath frame = DarkUi.Rounded(
                    new RectangleF(box.X + 0.5f, box.Y + 1f,
                        box.Width - 1f, box.Height - 2f), 3f))
                {
                    g.DrawPath(pen, frame);
                }
                g.DrawEllipse(pen, box.X + 3.6f, box.Y + 4.6f, 2.8f, 2.8f);
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(box.X + 2f, box.Bottom - 3.6f),
                    new PointF(box.X + 6.2f, box.Y + 8.2f),
                    new PointF(box.X + 8.6f, box.Y + 10.6f),
                    new PointF(box.X + 11f, box.Y + 7.6f),
                    new PointF(box.Right - 2f, box.Bottom - 3.6f)
                });
            }
        }

        private void BuildToolbar()
        {
            AddTool(GlyphKind.Prev, "上一张（←）", delegate { Navigate(-1); });
            AddTool(GlyphKind.Next, "下一张（→）", delegate { Navigate(1); });
            _pill.AddSeparator();
            AddTool(GlyphKind.Fit, "适应窗口（Ctrl+0）", delegate
            {
                _canvas.FitToWindow();
            });
            AddTool(GlyphKind.Actual, "实际大小 100%（Ctrl+1）", delegate
            {
                _canvas.ActualSize();
            });
            AddTool(GlyphKind.ZoomOut, "缩小（-）", delegate
            {
                _canvas.ZoomBy(1f / 1.25f);
            });
            AddTool(GlyphKind.ZoomIn, "放大（+）", delegate
            {
                _canvas.ZoomBy(1.25f);
            });
            _pill.AddSeparator();
            AddTool(GlyphKind.RotateLeft, "向左旋转 90°", delegate
            {
                Rotate(false);
            });
            AddTool(GlyphKind.RotateRight, "向右旋转 90°", delegate
            {
                Rotate(true);
            });
            _cropButton = AddTool(GlyphKind.Crop, "裁剪（C）", delegate
            {
                ToggleCrop();
            });
            _pill.AddSeparator();
            AddTool(GlyphKind.Copy, "复制图片（Ctrl+C）", delegate
            {
                CopyImage();
            });
            AddTool(GlyphKind.Save, "另存为（Ctrl+S）", delegate
            {
                SaveAs();
            });
        }

        private GlyphButton AddTool(
            GlyphKind kind, string tip, EventHandler click)
        {
            GlyphButton button = new GlyphButton(
                kind, DarkUi.Surface, 34, 34);
            button.IconScale = 0.95f;
            button.Click += click;
            _tips.SetToolTip(button, tip);
            _pill.AddButton(button);
            return button;
        }

        private void DoLayout()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            int canvasWidth = Math.Max(200, width - InfoWidth);
            int canvasHeight = Math.Max(120,
                height - HeaderHeightValue - FooterHeight);

            _canvas.SetBounds(0, HeaderHeightValue,
                canvasWidth, canvasHeight);
            _info.SetBounds(width - InfoWidth, HeaderHeightValue,
                InfoWidth, Math.Max(120, height - HeaderHeightValue));

            _pill.LayoutItems();
            _pill.Location = new Point(
                Math.Max(16, (canvasWidth - _pill.Width) / 2),
                height - FooterHeight +
                    (FooterHeight - _pill.Height) / 2);

            int right = width - 12;
            _closeButton.Location = new Point(right - 38, 8);
            _maximizeButton.Location = new Point(right - 76, 8);
            _minimizeButton.Location = new Point(right - 114, 8);
            _openButton.Location = new Point(right - 162, 8);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_canvas != null)
            {
                DoLayout();
            }
        }

        protected override void OnMaximizedChanged()
        {
            if (_maximizeButton != null)
            {
                _maximizeButton.Kind = IsMaximized
                    ? GlyphKind.Restore : GlyphKind.Maximize;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(DarkUi.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
            {
                g.DrawLine(pen, 0, HeaderHeightValue - 1,
                    ClientSize.Width, HeaderHeightValue - 1);
                g.DrawLine(pen, 0, ClientSize.Height - FooterHeight,
                    ClientSize.Width - InfoWidth,
                    ClientSize.Height - FooterHeight);
            }

            int nameRight = _openButton.Left - 12;
            int navWidth = _fileInfo != null && _fileInfo.Total > 1
                ? 78 : 0;

            // 左上角一个小标记，让标题行不至于只有一行字。
            DrawPhotoMark(g, new RectangleF(16f, 15f, 16f, 16f),
                _path == null ? DarkUi.TextFaint : DarkUi.Accent);

            Rectangle nameRect = new Rectangle(
                40, 12, Math.Max(80, nameRight - 40 - navWidth - 8), 22);
            string title = _path == null
                ? "图片查看器"
                : (IsDirty ? "• " : "") + Path.GetFileName(_path);
            DarkUi.Text2(g, title, DarkUi.FontStrong,
                _path == null ? DarkUi.TextDim : DarkUi.Text,
                nameRect, DarkUi.LeftMiddleClip);

            if (navWidth > 0)
            {
                DarkUi.Text2(g,
                    _fileInfo.Index + " / " + _fileInfo.Total,
                    DarkUi.FontNumber, DarkUi.TextFaint,
                    new Rectangle(nameRight - navWidth, 12, navWidth, 22),
                    DarkUi.LeftMiddle);
            }

            if (_canvas.Image != null)
            {
                string zoom = _canvas.ZoomPercent + "%";
                DarkUi.Text2(g, zoom, DarkUi.FontNumber, DarkUi.TextDim,
                    new Rectangle(20, ClientSize.Height - FooterHeight,
                        60, FooterHeight),
                    DarkUi.LeftMiddle);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < HeaderHeightValue)
            {
                BeginWindowDrag();
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Y < HeaderHeightValue)
            {
                ToggleMaximize();
                return;
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_canvas.Image == null)
            {
                return;
            }
            Point local = _canvas.PointToClient(PointToScreen(e.Location));
            if (!_canvas.ClientRectangle.Contains(local))
            {
                return;
            }
            float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            _canvas.ZoomTo(_canvas.Zoom * factor, local);
        }

        // ------------------------------------------------------------------
        // 文件读写
        // ------------------------------------------------------------------

        private void ShowFile(string path, bool resetView)
        {
            string error;
            Bitmap image = ImageToolKit.Load(path, out error);
            if (image == null)
            {
                // 还没显示窗口时（命令行 / 拖放打开失败）不要把提示框
                // 挂在一个看不见的窗体上，否则它会跑到屏幕左上角。
                IWin32Window owner = Visible ? (IWin32Window)this : null;
                ImageMessage.Info(owner, "打开图片",
                    error ?? "无法打开这张图片。");
                return;
            }

            if (_cropMode)
            {
                ExitCrop();
            }

            Bitmap previous = _canvas.Image;
            _canvas.SetImage(image, resetView);
            if (previous != null && !ReferenceEquals(previous, image))
            {
                previous.Dispose();
            }

            ClearUndo();
            _path = path;
            _lastFolder = Path.GetDirectoryName(path);
            _siblings = ImageToolKit.SiblingImages(path);
            RefreshInfo();
            UpdateTitle();
            Invalidate();
        }

        private void UpdateTitle()
        {
            Text = _path == null
                ? "图片查看器 - " + AppInfo.Name
                : Path.GetFileName(_path) + " - " + AppInfo.Name;
        }

        private void RefreshInfo()
        {
            if (_path == null || _canvas.Image == null)
            {
                _fileInfo = null;
                _info.SetInfo(null, false);
                return;
            }
            _fileInfo = ImageToolKit.Describe(
                _path, _canvas.Image, _siblings);
            _info.SetInfo(_fileInfo, IsDirty);
        }

        private void OpenImageDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "打开图片";
                dialog.Filter = ImageToolKit.OpenFilter;
                dialog.CheckFileExists = true;
                string folder = _path == null
                    ? null : Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(folder) ||
                    !Directory.Exists(folder))
                {
                    folder = _lastFolder;
                }
                if (string.IsNullOrEmpty(folder) ||
                    !Directory.Exists(folder))
                {
                    folder = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyPictures);
                }
                if (Directory.Exists(folder))
                {
                    dialog.InitialDirectory = folder;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    OpenPath(dialog.FileName);
                }
            }
        }

        private void SaveAs()
        {
            if (_canvas.Image == null)
            {
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "另存为";
                dialog.Filter =
                    "PNG|*.png|JPEG|*.jpg|BMP|*.bmp|GIF|*.gif|TIFF|*.tif";
                string folder = _path == null
                    ? null : Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(folder) &&
                    Directory.Exists(folder))
                {
                    dialog.InitialDirectory = folder;
                }
                string name = _path == null
                    ? "image" : Path.GetFileNameWithoutExtension(_path);
                string ext = _path == null
                    ? ".png" : Path.GetExtension(_path);
                dialog.FileName = name + "_edit" + ext;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string key = ImageFormatOption.FromExtension(
                    Path.GetExtension(dialog.FileName)).Key;
                string error;
                if (!ImageToolKit.Save(_canvas.Image, dialog.FileName,
                    key, 92, out error))
                {
                    ImageMessage.Info(this, "另存为",
                        error ?? "保存失败。");
                    return;
                }
                ShowFile(dialog.FileName, false);
                _canvas.ShowToast("已保存 " +
                    Path.GetFileName(dialog.FileName));
            }
        }

        private void CopyImage()
        {
            if (_canvas.Image == null)
            {
                return;
            }
            try
            {
                ClipboardPasteFiles.WriteBitmap(
                    _canvas.Image, ClipboardWriteMode.Auto);
                _canvas.ShowToast("图片已复制，可直接粘贴到桌面");
            }
            catch (Exception ex)
            {
                _canvas.ShowToast("复制失败：" +
                    ImageToolKit.ShortError(ex));
            }
        }

        private void OpenFolder()
        {
            if (_path == null)
            {
                return;
            }
            try
            {
                Process.Start("explorer.exe",
                    "/select,\"" + _path + "\"");
            }
            catch (Exception ex)
            {
                _canvas.ShowToast("打开文件夹失败：" +
                    ImageToolKit.ShortError(ex));
            }
        }

        // ------------------------------------------------------------------
        // 编辑操作
        // ------------------------------------------------------------------

        private void PushUndo()
        {
            if (_canvas.Image == null)
            {
                return;
            }
            _undo.Add((Bitmap)_canvas.Image.Clone());
            while (_undo.Count > 8)
            {
                _undo[0].Dispose();
                _undo.RemoveAt(0);
            }
        }

        private void ClearUndo()
        {
            for (int i = 0; i < _undo.Count; i++)
            {
                _undo[i].Dispose();
            }
            _undo.Clear();
        }

        private void ReplaceImage(Bitmap next, string toast)
        {
            if (next == null)
            {
                return;
            }
            PushUndo();
            Bitmap previous = _canvas.Image;
            _canvas.SetImage(next, false);
            if (previous != null && !ReferenceEquals(previous, next))
            {
                previous.Dispose();
            }
            RefreshInfo();
            Invalidate();
            if (!string.IsNullOrEmpty(toast))
            {
                _canvas.ShowToast(toast);
            }
        }

        private void Undo()
        {
            if (_undo.Count == 0)
            {
                _canvas.ShowToast("没有可撤销的操作");
                return;
            }
            Bitmap restore = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Bitmap previous = _canvas.Image;
            _canvas.SetImage(restore, false);
            if (previous != null && !ReferenceEquals(previous, restore))
            {
                previous.Dispose();
            }
            RefreshInfo();
            Invalidate();
            _canvas.ShowToast("已撤销上一步");
        }

        private void Rotate(bool clockwise)
        {
            if (_canvas.Image == null)
            {
                return;
            }
            Bitmap rotated = ImageToolKit.Rotate(_canvas.Image, clockwise);
            ReplaceImage(rotated, clockwise
                ? "向右旋转 90°，Ctrl+Z 可撤销"
                : "向左旋转 90°，Ctrl+Z 可撤销");
        }

        private void ToggleCrop()
        {
            if (_canvas.Image == null)
            {
                return;
            }
            if (_cropMode)
            {
                ExitCrop();
                return;
            }
            _cropMode = true;
            _canvas.BeginCrop();
            _cropButton.Ink = DarkUi.Accent;
            _canvas.ShowToast("拖出要保留的区域，Enter 确认");
        }

        private void ExitCrop()
        {
            _cropMode = false;
            _canvas.CancelCrop();
            _cropButton.Ink = DarkUi.TextDim;
        }

        private void ApplyCrop(Rectangle selection)
        {
            if (_canvas.Image == null)
            {
                return;
            }
            Bitmap cropped = ImageToolKit.Crop(_canvas.Image, selection);
            if (cropped == null)
            {
                _canvas.ShowToast("选区太小了，重新拖一次");
                return;
            }
            ExitCrop();
            ReplaceImage(cropped, "已裁剪为 " + cropped.Width +
                " × " + cropped.Height + "，Ctrl+Z 可撤销");
        }

        // ------------------------------------------------------------------
        // 信息面板动作
        // ------------------------------------------------------------------

        private void OnInfoAction(string action)
        {
            switch (action)
            {
                case "rename":
                    RenameImage();
                    break;
                case "convert":
                    ConvertImage();
                    break;
                case "compress":
                    CompressImage();
                    break;
                case "resize":
                    ResizeImage();
                    break;
                case "folder":
                    OpenFolder();
                    break;
            }
        }

        private void RenameImage()
        {
            if (_path == null || _fileInfo == null)
            {
                return;
            }
            using (ImageRenameDialog dialog =
                new ImageRenameDialog(_fileInfo.Name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string newPath;
                string error;
                if (!ImageToolKit.TryRename(
                    _path, dialog.NewBaseName, out newPath, out error))
                {
                    ImageMessage.Info(this, "重命名",
                        error ?? "重命名失败。");
                    return;
                }
                _path = newPath;
                _siblings = ImageToolKit.SiblingImages(_path);
                RefreshInfo();
                UpdateTitle();
                Invalidate();
                _canvas.ShowToast("已重命名为 " +
                    Path.GetFileName(_path));
            }
        }

        private void ConvertImage()
        {
            if (_canvas.Image == null || _fileInfo == null)
            {
                return;
            }
            using (ImageConvertDialog dialog =
                new ImageConvertDialog(_fileInfo, _canvas.Image))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string target = dialog.SavePath;
                string error;
                if (!ImageToolKit.Save(_canvas.Image, target,
                    dialog.TargetFormatKey, dialog.Quality, out error))
                {
                    ImageMessage.Info(this, "转换格式",
                        error ?? "保存失败。");
                    return;
                }
                ShowFile(target, false);
                _canvas.ShowToast("已转换并保存 " +
                    Path.GetFileName(target));
            }
        }

        private void CompressImage()
        {
            if (_canvas.Image == null || _fileInfo == null)
            {
                return;
            }
            using (ImageCompressDialog dialog =
                new ImageCompressDialog(_fileInfo, _canvas.Image))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                Bitmap work = _canvas.Image;
                bool temporary = false;
                int longest = Math.Max(work.Width, work.Height);
                if (dialog.MaxSide > 0 && longest > dialog.MaxSide)
                {
                    float scale = (float)dialog.MaxSide / longest;
                    int width = Math.Max(1,
                        (int)Math.Round(work.Width * scale));
                    int height = Math.Max(1,
                        (int)Math.Round(work.Height * scale));
                    work = ImageToolKit.Resize(work, width, height);
                    temporary = true;
                }

                string target = dialog.SavePath;
                string error;
                bool ok = ImageToolKit.Save(work, target, "jpg",
                    dialog.Quality, out error);
                if (temporary)
                {
                    work.Dispose();
                }
                if (!ok)
                {
                    ImageMessage.Info(this, "压缩图片",
                        error ?? "保存失败。");
                    return;
                }

                long size = 0;
                try
                {
                    size = new FileInfo(target).Length;
                }
                catch
                {
                    size = 0;
                }
                ShowFile(target, false);
                _canvas.ShowToast(size > 0
                    ? "已压缩到 " + ImageToolKit.FormatSize(size)
                    : "已保存压缩结果");
            }
        }

        private void ResizeImage()
        {
            if (_canvas.Image == null || _path == null)
            {
                return;
            }
            using (ImageResizeDialog dialog = new ImageResizeDialog(
                _canvas.Image.Width, _canvas.Image.Height))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                int width = dialog.TargetWidth;
                int height = dialog.TargetHeight;
                Bitmap resized = ImageToolKit.Resize(
                    _canvas.Image, width, height);
                if (resized == null)
                {
                    return;
                }

                if (dialog.OutputMode == 0)
                {
                    ReplaceImage(resized, "已在查看器中调整为 " +
                        width + " × " + height + "（未写入文件）");
                    return;
                }

                string key = ImageFormatOption.FromExtension(
                    Path.GetExtension(_path)).Key;
                string target = dialog.OutputMode == 2
                    ? _path
                    : ImageToolKit.WithSuffix(_path,
                        "_" + width + "x" + height,
                        Path.GetExtension(_path));
                string error;
                bool ok = ImageToolKit.Save(resized, target, key, 92,
                    out error);
                resized.Dispose();
                if (!ok)
                {
                    ImageMessage.Info(this, "修改尺寸",
                        error ?? "保存失败。");
                    return;
                }
                ShowFile(target, false);
                _canvas.ShowToast(dialog.OutputMode == 2
                    ? "已覆盖保存原文件"
                    : "已另存为 " + Path.GetFileName(target));
            }
        }

        // ------------------------------------------------------------------
        // 同目录切换
        // ------------------------------------------------------------------

        private int CurrentIndex()
        {
            if (_siblings == null || _path == null)
            {
                return -1;
            }
            for (int i = 0; i < _siblings.Count; i++)
            {
                if (string.Equals(_siblings[i], _path,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private void Navigate(int delta)
        {
            if (_siblings == null || _siblings.Count < 2)
            {
                _canvas.ShowToast("这个文件夹里只有这一张图片");
                return;
            }
            int index = CurrentIndex();
            if (index < 0)
            {
                index = 0;
            }
            int next = index + delta;
            if (next < 0)
            {
                next = _siblings.Count - 1;
            }
            if (next >= _siblings.Count)
            {
                next = 0;
            }
            if (next == index)
            {
                return;
            }
            if (IsDirty && !ImageMessage.Ask(this, "切换图片",
                "当前的修改还没有保存，仍要切换到下一张吗？", "仍然切换"))
            {
                return;
            }
            ShowFile(_siblings[next], true);
        }

        // ------------------------------------------------------------------
        // 拖放与快捷键
        // ------------------------------------------------------------------

        private void OnViewerDragEnter(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop)
                as string[];
            e.Effect = files != null && files.Length > 0 &&
                ImageToolKit.IsImagePath(files[0])
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnViewerDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop)
                as string[];
            if (files == null || files.Length == 0)
            {
                return;
            }
            if (ImageToolKit.IsImagePath(files[0]))
            {
                OpenPath(files[0]);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (_cropMode)
                {
                    ExitCrop();
                }
                else
                {
                    Close();
                }
                return true;
            }
            if (keyData == Keys.Enter && _cropMode)
            {
                _canvas.ConfirmCrop();
                return true;
            }
            if (keyData == Keys.Left)
            {
                Navigate(-1);
                return true;
            }
            if (keyData == Keys.Right)
            {
                Navigate(1);
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                OpenImageDialog();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveAs();
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
            {
                CopyImage();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Z))
            {
                Undo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D0) ||
                keyData == (Keys.Control | Keys.NumPad0))
            {
                _canvas.FitToWindow();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D1) ||
                keyData == (Keys.Control | Keys.NumPad1))
            {
                _canvas.ActualSize();
                return true;
            }
            if (keyData == Keys.Add ||
                keyData == (Keys.Control | Keys.Oemplus))
            {
                _canvas.ZoomBy(1.25f);
                return true;
            }
            if (keyData == Keys.Subtract ||
                keyData == (Keys.Control | Keys.OemMinus))
            {
                _canvas.ZoomBy(1f / 1.25f);
                return true;
            }
            if (keyData == Keys.C)
            {
                ToggleCrop();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && IsDirty &&
                !ImageMessage.Ask(this, "关闭图片",
                    "当前的修改还没有保存，确定要关闭吗？", "关闭"))
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClearUndo();
            Bitmap image = _canvas.Image;
            if (image != null)
            {
                _canvas.SetImage(null, false);
                image.Dispose();
            }
            base.OnFormClosed(e);
        }
    }
}
