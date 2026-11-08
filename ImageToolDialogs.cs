using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>深色提示框 / 确认框，避免在深色界面上弹出刺眼的白底 MessageBox。</summary>
    internal sealed class SimpleMessageDialog : DarkDialog
    {
        private readonly FlatButton _okButton;
        private readonly FlatButton _cancelButton;

        public SimpleMessageDialog(
            string caption, string message, bool withCancel)
            : base(caption, new Size(440, 196))
        {
            Label text = new Label();
            text.Text = message;
            text.ForeColor = DarkUi.TextDim;
            text.BackColor = DarkUi.Window;
            text.Font = DarkUi.FontBody;
            text.SetBounds(20, 58, 400, 74);
            text.AutoSize = false;
            Controls.Add(text);

            _okButton = new FlatButton();
            _okButton.Text = withCancel ? "确定" : "知道了";
            _okButton.Primary = true;
            _okButton.Size = new Size(96, 34);
            _okButton.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            if (withCancel)
            {
                _cancelButton = new FlatButton();
                _cancelButton.Text = "取消";
                _cancelButton.Size = new Size(82, 34);
                _cancelButton.Click += delegate
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
                PlaceFooter(_cancelButton, _okButton);
            }
            else
            {
                PlaceFooter(_okButton);
            }
        }

        public void SetOkText(string text)
        {
            _okButton.Text = text;
        }
    }

    internal static class ImageMessage
    {
        public static void Info(
            IWin32Window owner, string caption, string message)
        {
            using (SimpleMessageDialog dialog =
                new SimpleMessageDialog(caption, message, false))
            {
                dialog.ShowDialog(owner);
            }
        }

        public static bool Ask(
            IWin32Window owner, string caption,
            string message, string okText)
        {
            using (SimpleMessageDialog dialog =
                new SimpleMessageDialog(caption, message, true))
            {
                dialog.SetOkText(okText);
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }

    /// <summary>格式转换：选目标格式、JPEG 质量与输出位置，并实时预估体积。</summary>
    internal sealed class ImageConvertDialog : DarkDialog
    {
        private readonly Bitmap _image;
        private readonly ImageFileInfo _info;
        private readonly FlatButton[] _formatButtons;
        private readonly FlatButton[] _qualityButtons;
        private readonly FlatButton[] _outputButtons;

        private int _formatIndex;
        private int _quality = 92;
        private bool _saveAs;
        private string _saveAsPath;
        private string _hintText = "";
        private long _estimate;

        public ImageConvertDialog(ImageFileInfo info, Bitmap image)
            : base("转换格式", new Size(440, 404))
        {
            _info = info;
            _image = image;

            ImageFormatOption current = ImageFormatOption.FromExtension(
                Path.GetExtension(info == null ? "" : info.FilePath));
            for (int i = 0; i < ImageFormatOption.All.Length; i++)
            {
                if (ImageFormatOption.All[i].Key == current.Key)
                {
                    _formatIndex = i;
                }
            }

            _formatButtons = new FlatButton[ImageFormatOption.All.Length];
            for (int i = 0; i < _formatButtons.Length; i++)
            {
                int index = i;
                FlatButton button = MakeChip(
                    ImageFormatOption.All[i].Label, 72, false);
                button.Location = new Point(20 + i * 80, 110);
                button.Click += delegate
                {
                    _formatIndex = index;
                    SyncChips();
                    Recalculate();
                };
                _formatButtons[i] = button;
                Controls.Add(button);
            }

            int[] qualities = new int[] { 85, 92, 75 };
            string[] qualityLabels = new string[]
            {
                "85 标准", "92 推荐", "75 更小"
            };
            _qualityButtons = new FlatButton[3];
            for (int i = 0; i < 3; i++)
            {
                int value = qualities[i];
                FlatButton button = MakeChip(
                    qualityLabels[i], 88, false);
                button.Location = new Point(20 + i * 96, 178);
                button.Click += delegate
                {
                    _quality = value;
                    SyncChips();
                    Recalculate();
                };
                _qualityButtons[i] = button;
                Controls.Add(button);
            }

            _outputButtons = new FlatButton[2];
            string[] outputLabels = new string[] { "保存到同目录", "另存为…" };
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                FlatButton button = MakeChip(outputLabels[i], 120, false);
                button.Location = new Point(20 + i * 128, 246);
                button.Click += delegate
                {
                    if (index == 1)
                    {
                        if (!PickSaveAs())
                        {
                            return;
                        }
                    }
                    else
                    {
                        _saveAs = false;
                        _saveAsPath = null;
                    }
                    SyncChips();
                    Recalculate();
                };
                _outputButtons[i] = button;
                Controls.Add(button);
            }

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Size = new Size(82, 34);
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            FlatButton confirm = new FlatButton();
            confirm.Text = "开始转换";
            confirm.Primary = true;
            confirm.Size = new Size(104, 34);
            confirm.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            PlaceFooter(cancel, confirm);

            SyncChips();
            Recalculate();
        }

        public string TargetFormatKey
        {
            get { return ImageFormatOption.All[_formatIndex].Key; }
        }

        public int Quality
        {
            get { return _quality; }
        }

        /// <summary>转换后要写出的完整路径（不会覆盖已有文件）。</summary>
        public string SavePath
        {
            get
            {
                if (_saveAs && !string.IsNullOrEmpty(_saveAsPath))
                {
                    return _saveAsPath;
                }
                string folder = _info == null
                    ? "" : _info.Folder;
                string name = _info == null ? "image"
                    : Path.GetFileNameWithoutExtension(_info.Name);
                string target = Path.Combine(folder,
                    name + ImageFormatOption.All[_formatIndex].Extension);
                return ImageToolKit.UniquePath(target);
            }
        }

        private FlatButton MakeChip(string text, int width, bool primary)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Size = new Size(width, 32);
            button.SurfaceColor = DarkUi.Window;
            button.Primary = primary;
            return button;
        }

        private void SyncChips()
        {
            for (int i = 0; i < _formatButtons.Length; i++)
            {
                _formatButtons[i].Checked = i == _formatIndex;
            }
            bool jpeg = ImageFormatOption.All[_formatIndex].Lossy;
            for (int i = 0; i < _qualityButtons.Length; i++)
            {
                int[] qualities = new int[] { 85, 92, 75 };
                _qualityButtons[i].Checked =
                    jpeg && qualities[i] == _quality;
                _qualityButtons[i].Enabled = jpeg;
            }
            _outputButtons[0].Checked = !_saveAs;
            _outputButtons[1].Checked = _saveAs;
        }

        private bool PickSaveAs()
        {
            ImageFormatOption option =
                ImageFormatOption.All[_formatIndex];
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "转换后保存为";
                dialog.Filter = option.Label + "|*" +
                    option.Extension + "|所有文件|*.*";
                dialog.FileName = Path.GetFileNameWithoutExtension(
                    _info == null ? "image" : _info.Name) +
                    option.Extension;
                string folder = _info == null ? "" : _info.Folder;
                if (!string.IsNullOrEmpty(folder))
                {
                    dialog.InitialDirectory = folder;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }
                _saveAs = true;
                _saveAsPath = dialog.FileName;
                return true;
            }
        }

        private void Recalculate()
        {
            _estimate = 0;
            if (_image != null)
            {
                string error;
                byte[] bytes = ImageToolKit.Encode(
                    _image, TargetFormatKey, _quality, out error);
                if (bytes != null)
                {
                    _estimate = bytes.Length;
                }
            }
            _hintText = "将保存为 " + Path.GetFileName(SavePath) +
                (_estimate > 0
                    ? " · 约 " + ImageToolKit.FormatSize(_estimate)
                    : "");
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            string summary = _info == null
                ? ""
                : _info.FormatName + " · " + _info.Bits + " · " +
                    _info.Width + " × " + _info.Height + " · " +
                    ImageToolKit.FormatSize(_info.SizeBytes);
            DrawHint(g, "当前：" + summary, 20, 58, DarkUi.TextDim);

            DrawSectionLabel(g, "目标格式", 20, 90);
            DrawSectionLabel(g, "质量（仅 JPEG）", 20, 158);
            DrawSectionLabel(g, "输出位置", 20, 226);

            DrawHint(g, _hintText, 20, 292, DarkUi.Text);
            DrawHint(g, "不会覆盖原始文件", 20, 314, DarkUi.TextFaint);
        }
    }

    /// <summary>压缩：JPEG 质量 + 可选的长边上限，并实时预估体积。</summary>
    internal sealed class ImageCompressDialog : DarkDialog
    {
        private readonly Bitmap _image;
        private readonly ImageFileInfo _info;
        private readonly FlatButton[] _qualityButtons;
        private readonly FlatButton[] _sideButtons;

        private int _quality = 75;
        private int _maxSide;

        private long _estimate;
        private int _targetWidth;
        private int _targetHeight;

        public ImageCompressDialog(ImageFileInfo info, Bitmap image)
            : base("压缩图片", new Size(440, 400))
        {
            _info = info;
            _image = image;

            int[] qualities = new int[] { 75, 60, 45 };
            string[] labels = new string[]
            {
                "75 推荐", "60 更小", "45 最小"
            };
            _qualityButtons = new FlatButton[3];
            for (int i = 0; i < 3; i++)
            {
                int value = qualities[i];
                FlatButton button = new FlatButton();
                button.Text = labels[i];
                button.Size = new Size(88, 32);
                button.SurfaceColor = DarkUi.Window;
                button.Location = new Point(20 + i * 96, 110);
                button.Click += delegate
                {
                    _quality = value;
                    SyncChips();
                    Recalculate();
                };
                _qualityButtons[i] = button;
                Controls.Add(button);
            }

            int[] sides = new int[] { 0, 1920, 1280, 960 };
            string[] sideLabels = new string[]
            {
                "原尺寸", "1920 px", "1280 px", "960 px"
            };
            _sideButtons = new FlatButton[4];
            for (int i = 0; i < 4; i++)
            {
                int value = sides[i];
                FlatButton button = new FlatButton();
                button.Text = sideLabels[i];
                button.Size = new Size(92, 32);
                button.SurfaceColor = DarkUi.Window;
                button.Location = new Point(20 + i * 100, 178);
                button.Click += delegate
                {
                    _maxSide = value;
                    SyncChips();
                    Recalculate();
                };
                _sideButtons[i] = button;
                Controls.Add(button);
            }

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Size = new Size(82, 34);
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            FlatButton confirm = new FlatButton();
            confirm.Text = "压缩保存";
            confirm.Primary = true;
            confirm.Size = new Size(104, 34);
            confirm.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            PlaceFooter(cancel, confirm);

            SyncChips();
            Recalculate();
        }

        public int Quality
        {
            get { return _quality; }
        }

        public int MaxSide
        {
            get { return _maxSide; }
        }

        /// <summary>压缩结果固定输出 JPEG（这是体积收益最大的格式）。</summary>
        public string SavePath
        {
            get
            {
                if (_info == null)
                {
                    return "compressed.jpg";
                }
                return ImageToolKit.WithSuffix(
                    _info.FilePath, "_compressed", ".jpg");
            }
        }

        private void SyncChips()
        {
            int[] qualities = new int[] { 75, 60, 45 };
            for (int i = 0; i < _qualityButtons.Length; i++)
            {
                _qualityButtons[i].Checked = qualities[i] == _quality;
            }
            int[] sides = new int[] { 0, 1920, 1280, 960 };
            for (int i = 0; i < _sideButtons.Length; i++)
            {
                _sideButtons[i].Checked = sides[i] == _maxSide;
            }
        }

        private void Recalculate()
        {
            _estimate = 0;
            _targetWidth = 0;
            _targetHeight = 0;
            if (_image == null)
            {
                Invalidate();
                return;
            }

            Bitmap work = _image;
            bool temporary = false;
            int longest = Math.Max(_image.Width, _image.Height);
            if (_maxSide > 0 && longest > _maxSide)
            {
                float scale = (float)_maxSide / longest;
                int width = Math.Max(1, (int)Math.Round(
                    _image.Width * scale));
                int height = Math.Max(1, (int)Math.Round(
                    _image.Height * scale));
                work = ImageToolKit.Resize(_image, width, height);
                temporary = true;
            }
            _targetWidth = work.Width;
            _targetHeight = work.Height;

            string error;
            byte[] bytes = ImageToolKit.Encode(work, "jpg", _quality,
                out error);
            if (bytes != null)
            {
                _estimate = bytes.Length;
            }
            if (temporary)
            {
                work.Dispose();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            string summary = _info == null
                ? ""
                : ImageToolKit.FormatSize(_info.SizeBytes) + " · " +
                    _info.Width + " × " + _info.Height;
            DrawHint(g, "当前：" + summary, 20, 58, DarkUi.TextDim);

            DrawSectionLabel(g, "JPEG 质量", 20, 90);
            DrawSectionLabel(g, "最长边上限", 20, 158);

            long original = _info == null ? 0 : _info.SizeBytes;
            string estimate = _estimate > 0
                ? "约 " + ImageToolKit.FormatSize(_estimate)
                : "—";
            DarkUi.Text2(g, estimate, DarkUi.FontHuge, DarkUi.Text,
                new Rectangle(20, 226, 260, 30), DarkUi.LeftMiddle);

            string compare = "";
            if (_estimate > 0 && original > 0)
            {
                int percent = (int)Math.Round(
                    100.0 * _estimate / original);
                compare = "原图 " + ImageToolKit.FormatSize(original) +
                    " → " + percent + "%" +
                    (_targetWidth > 0
                        ? " · " + _targetWidth + " × " + _targetHeight
                        : "");
            }
            DrawHint(g, compare, 20, 258, DarkUi.TextDim);
            DrawHint(g, "输出：" + Path.GetFileName(SavePath),
                20, 288, DarkUi.TextFaint);

            if (_estimate > 0 && original > 0 && _estimate >= original)
            {
                DrawHint(g, "原图已经很小了，再压可能反而变大",
                    20, 310, DarkUi.Danger);
            }
        }
    }

    /// <summary>改尺寸：像素输入 + 比例快捷 + 输出方式。</summary>
    internal sealed class ImageResizeDialog : DarkDialog
    {
        private readonly FieldBox _widthField;
        private readonly FieldBox _heightField;
        private readonly FlatButton _lockButton;
        private readonly FlatButton[] _percentButtons;
        private readonly FlatButton[] _outputButtons;

        private readonly int _sourceWidth;
        private readonly int _sourceHeight;
        private bool _updating;
        private bool _lock = true;
        private int _output;
        private string _error;

        public ImageResizeDialog(int width, int height)
            : base("修改尺寸", new Size(440, 424))
        {
            _sourceWidth = Math.Max(1, width);
            _sourceHeight = Math.Max(1, height);

            _widthField = new FieldBox();
            _widthField.SetBounds(20, 110, 92, 34);
            _widthField.Text = _sourceWidth.ToString();
            _widthField.TextChanged += delegate
            {
                if (_updating)
                {
                    return;
                }
                int value;
                if (_lock && int.TryParse(_widthField.Text, out value) &&
                    value > 0)
                {
                    _updating = true;
                    _heightField.Text = Math.Max(1, (int)Math.Round(
                        value * (double)_sourceHeight / _sourceWidth))
                        .ToString();
                    _updating = false;
                }
                Invalidate();
            };
            Controls.Add(_widthField);

            _heightField = new FieldBox();
            _heightField.SetBounds(142, 110, 92, 34);
            _heightField.Text = _sourceHeight.ToString();
            _heightField.TextChanged += delegate
            {
                if (_updating)
                {
                    return;
                }
                int value;
                if (_lock && int.TryParse(_heightField.Text, out value) &&
                    value > 0)
                {
                    _updating = true;
                    _widthField.Text = Math.Max(1, (int)Math.Round(
                        value * (double)_sourceWidth / _sourceHeight))
                        .ToString();
                    _updating = false;
                }
                Invalidate();
            };
            Controls.Add(_heightField);

            _lockButton = new FlatButton();
            _lockButton.Text = "锁定比例";
            _lockButton.Checked = true;
            _lockButton.Size = new Size(104, 34);
            _lockButton.SurfaceColor = DarkUi.Window;
            _lockButton.Location = new Point(246, 110);
            _lockButton.Click += delegate
            {
                _lock = !_lock;
                _lockButton.Checked = _lock;
            };
            Controls.Add(_lockButton);

            int[] percents = new int[] { 25, 50, 75, 100, 200 };
            _percentButtons = new FlatButton[percents.Length];
            for (int i = 0; i < percents.Length; i++)
            {
                int percent = percents[i];
                FlatButton button = new FlatButton();
                button.Text = percent + "%";
                button.Size = new Size(72, 32);
                button.SurfaceColor = DarkUi.Window;
                button.Location = new Point(20 + i * 80, 182);
                button.Click += delegate
                {
                    _updating = true;
                    _widthField.Text = Math.Max(1, (int)Math.Round(
                        _sourceWidth * percent / 100.0)).ToString();
                    _heightField.Text = Math.Max(1, (int)Math.Round(
                        _sourceHeight * percent / 100.0)).ToString();
                    _updating = false;
                    for (int k = 0; k < _percentButtons.Length; k++)
                    {
                        _percentButtons[k].Checked = k == i;
                    }
                    Invalidate();
                };
                if (percent == 100)
                {
                    button.Checked = true;
                }
                _percentButtons[i] = button;
                Controls.Add(button);
            }

            string[] outputs = new string[]
            {
                "仅查看", "另存新文件", "覆盖原文件"
            };
            _outputButtons = new FlatButton[outputs.Length];
            for (int i = 0; i < outputs.Length; i++)
            {
                int index = i;
                FlatButton button = new FlatButton();
                button.Text = outputs[i];
                button.Size = new Size(126, 32);
                button.SurfaceColor = DarkUi.Window;
                button.Location = new Point(20 + i * 134, 250);
                button.Click += delegate
                {
                    _output = index;
                    SyncOutput();
                    Invalidate();
                };
                _outputButtons[i] = button;
                Controls.Add(button);
            }
            _outputButtons[0].Checked = true;

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Size = new Size(82, 34);
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            FlatButton confirm = new FlatButton();
            confirm.Text = "应用";
            confirm.Primary = true;
            confirm.Size = new Size(96, 34);
            confirm.Click += delegate
            {
                if (!ValidateInput())
                {
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            PlaceFooter(cancel, confirm);
        }

        public int TargetWidth
        {
            get
            {
                int value;
                return int.TryParse(_widthField.Text, out value)
                    ? Math.Max(1, value) : _sourceWidth;
            }
        }

        public int TargetHeight
        {
            get
            {
                int value;
                return int.TryParse(_heightField.Text, out value)
                    ? Math.Max(1, value) : _sourceHeight;
            }
        }

        /// <summary>0 仅查看，1 另存新文件，2 覆盖原文件。</summary>
        public int OutputMode
        {
            get { return _output; }
        }

        private void SyncOutput()
        {
            for (int i = 0; i < _outputButtons.Length; i++)
            {
                _outputButtons[i].Checked = i == _output;
            }
        }

        private bool ValidateInput()
        {
            int width = TargetWidth;
            int height = TargetHeight;
            if (width < 1 || height < 1)
            {
                _error = "宽度和高度都必须是正整数";
                Invalidate();
                return false;
            }
            if (width > 20000 || height > 20000)
            {
                _error = "单边最大支持 20000 像素";
                Invalidate();
                return false;
            }
            _error = null;
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            DrawHint(g, "当前尺寸：" + _sourceWidth + " × " +
                _sourceHeight, 20, 58, DarkUi.TextDim);
            DrawSectionLabel(g, "宽度（像素）", 20, 90);
            DrawSectionLabel(g, "高度（像素）", 142, 90);
            DarkUi.Text2(g, "×", DarkUi.FontBody, DarkUi.TextFaint,
                new Rectangle(118, 110, 18, 34), DarkUi.CenterMiddle);

            DrawSectionLabel(g, "按比例", 20, 162);
            DrawSectionLabel(g, "输出方式", 20, 230);

            if (_error != null)
            {
                DrawHint(g, _error, 20, 296, DarkUi.Danger);
            }
            else
            {
                DrawHint(g, "新尺寸：" + TargetWidth + " × " +
                    TargetHeight, 20, 296, DarkUi.Text);
                string note = _output == 2
                    ? "会直接覆盖原文件，建议先另存一份"
                    : (_output == 1
                        ? "同目录生成新文件，原图保留"
                        : "只在查看器里调整，不写文件");
                DrawHint(g, note, 20, 318,
                    _output == 2 ? DarkUi.Danger : DarkUi.TextFaint);
            }
        }
    }

    /// <summary>重命名：只改主文件名，扩展名固定。</summary>
    internal sealed class ImageRenameDialog : DarkDialog
    {
        private readonly FieldBox _field;
        private readonly string _extension;
        private string _error;

        public ImageRenameDialog(string currentName)
            : base("重命名", new Size(420, 252))
        {
            string name = currentName ?? "";
            _extension = Path.GetExtension(name);
            string baseName = Path.GetFileNameWithoutExtension(name);

            _field = new FieldBox();
            _field.SetBounds(20, 104, 380, 34);
            _field.Text = baseName;
            _field.TextChanged += delegate
            {
                _error = null;
                Invalidate();
            };
            Controls.Add(_field);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Size = new Size(82, 34);
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            FlatButton confirm = new FlatButton();
            confirm.Text = "重命名";
            confirm.Primary = true;
            confirm.Size = new Size(96, 34);
            confirm.Click += delegate
            {
                string value = (_field.Text ?? "").Trim();
                if (value.Length == 0)
                {
                    _error = "名称不能为空";
                    Invalidate();
                    return;
                }
                if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    _error = "名称不能包含 \\ / : * ? \" < > |";
                    Invalidate();
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            PlaceFooter(cancel, confirm);
        }

        public string NewBaseName
        {
            get { return (_field.Text ?? "").Trim(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            DrawSectionLabel(g, "新名称", 20, 80);
            DrawHint(g, "扩展名保持不变：" + _extension, 20, 146,
                DarkUi.TextFaint);

            if (_error != null)
            {
                DrawHint(g, _error, 20, 168, DarkUi.Danger);
            }
            else
            {
                DrawHint(g, "只改文件名，图片内容不受影响",
                    20, 168, DarkUi.TextFaint);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _field.Inner.Focus();
            _field.Inner.SelectAll();
        }
    }
}
