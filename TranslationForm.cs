using System;
using System.Drawing;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class TranslationForm : Form
    {
        private readonly AppConfig _config;
        private readonly Action _openSettings;
        private readonly TextBox _sourceBox;
        private readonly TextBox _resultBox;
        private readonly Label _statusLabel;
        private readonly Button _retryButton;
        private readonly Button _copyButton;
        private readonly Button _copySourceButton;
        private readonly LinkLabel _settingsLink;
        private readonly ComboBox _fromBox;
        private readonly ComboBox _toBox;
        private readonly Button _swapButton;
        private string _translatedText = "";
        private bool _busy;
        private bool _manualInput;

        public TranslationForm(
            AppConfig config,
            string text,
            Point near,
            Action openSettings,
            bool manualInput = false)
        {
            _manualInput = manualInput;
            _config = config;
            _openSettings = openSettings;

            Text = _manualInput ? "手动翻译" : "百度翻译";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = _manualInput
                ? FormStartPosition.CenterScreen
                : FormStartPosition.Manual;
            ClientSize = new Size(580, 470);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);

            // ---- 语言选择：源语言 → 目标语言 ----
            Label fromLabel = new Label();
            fromLabel.Text = "源语言";
            fromLabel.AutoSize = true;
            fromLabel.Location = new Point(14, 18);
            fromLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(fromLabel);

            _fromBox = new ComboBox();
            _fromBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _fromBox.Location = new Point(66, 13);
            _fromBox.Size = new Size(128, 24);
            _fromBox.FlatStyle = FlatStyle.System;
            Controls.Add(_fromBox);

            Label arrowLabel = new Label();
            arrowLabel.Text = "→";
            arrowLabel.AutoSize = true;
            arrowLabel.Location = new Point(200, 18);
            arrowLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(arrowLabel);

            Label toLabel = new Label();
            toLabel.Text = "目标语言";
            toLabel.AutoSize = true;
            toLabel.Location = new Point(222, 18);
            toLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(toLabel);

            _toBox = new ComboBox();
            _toBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _toBox.Location = new Point(282, 13);
            _toBox.Size = new Size(190, 24);
            _toBox.FlatStyle = FlatStyle.System;
            Controls.Add(_toBox);

            _swapButton = new Button();
            _swapButton.Text = "⇄";
            _swapButton.Location = new Point(482, 12);
            _swapButton.Size = new Size(38, 26);
            _swapButton.Click += OnSwapLanguages;
            _swapButton.Visible = false;
            Controls.Add(_swapButton);

            FillLanguages();
            _fromBox.SelectedIndexChanged += OnLanguageChanged;
            _toBox.SelectedIndexChanged += OnLanguageChanged;

            Label sourceLabel = new Label();
            sourceLabel.Text = "原文";
            sourceLabel.AutoSize = true;
            sourceLabel.Location = new Point(14, 48);
            sourceLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(sourceLabel);

            _sourceBox = new TextBox();
            _sourceBox.Multiline = true;
            _sourceBox.ScrollBars = ScrollBars.Vertical;
            _sourceBox.BackColor = Color.White;
            _sourceBox.BorderStyle = BorderStyle.FixedSingle;
            _sourceBox.Location = new Point(14, 70);
            _sourceBox.Size = new Size(552, 76);
            _sourceBox.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                AnchorStyles.Right;
            _sourceBox.Text = _manualInput ? "" : (text ?? "");
            _sourceBox.TextChanged += OnSourceTextChanged;
            Controls.Add(_sourceBox);

            Label resultLabel = new Label();
            resultLabel.Text = "译文";
            resultLabel.AutoSize = true;
            resultLabel.Location = new Point(14, 158);
            resultLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(resultLabel);

            _resultBox = new TextBox();
            _resultBox.Multiline = true;
            _resultBox.ReadOnly = true;
            _resultBox.ScrollBars = ScrollBars.Vertical;
            _resultBox.BackColor = Color.White;
            _resultBox.BorderStyle = BorderStyle.FixedSingle;
            _resultBox.Location = new Point(14, 180);
            _resultBox.Size = new Size(552, 210);
            _resultBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_resultBox);

            _statusLabel = new Label();
            _statusLabel.Text = "";
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Location = new Point(14, 402);
            _statusLabel.Size = new Size(360, 22);
            _statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left |
                AnchorStyles.Right;
            _statusLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(_statusLabel);

            _settingsLink = new LinkLabel();
            _settingsLink.Text = "打开设置";
            _settingsLink.AutoSize = true;
            _settingsLink.Location = new Point(14, 430);
            _settingsLink.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _settingsLink.Visible = false;
            _settingsLink.LinkClicked += OnSettingsLinkClicked;
            Controls.Add(_settingsLink);

            _retryButton = new Button();
            _retryButton.Text = "翻译";
            _retryButton.Location = new Point(414, 426);
            _retryButton.Size = new Size(72, 28);
            _retryButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _retryButton.Click += OnRetryClicked;
            Controls.Add(_retryButton);

            _copyButton = new Button();
            _copyButton.Text = "复制译文";
            _copyButton.Location = new Point(334, 426);
            _copyButton.Size = new Size(72, 28);
            _copyButton.Anchor = AnchorStyles.Bottom;
            _copyButton.Enabled = false;
            _copyButton.Click += OnCopyClicked;
            Controls.Add(_copyButton);

            _copySourceButton = new Button();
            _copySourceButton.Text = "复制原文";
            _copySourceButton.Location = new Point(254, 426);
            _copySourceButton.Size = new Size(72, 28);
            _copySourceButton.Anchor = AnchorStyles.Bottom;
            _copySourceButton.Click += OnCopySourceClicked;
            Controls.Add(_copySourceButton);

            Button closeButton = new Button();
            closeButton.Text = "关闭";
            closeButton.Location = new Point(494, 426);
            closeButton.Size = new Size(72, 28);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            // 用 Esc 关闭窗口。
            KeyDown += OnFormKeyDown;

            if (!_manualInput)
            {
                ShowNear(near);
            }
        }

        /// <summary>下拉框里的一项语言：Code 是百度接口用的语种代码。</summary>
        private sealed class LanguageOption
        {
            public readonly string Code;
            public readonly string Name;

            public LanguageOption(string code, string name)
            {
                Code = code;
                Name = name;
            }

            public override string ToString()
            {
                return Name;
            }
        }

        /// <summary>
        /// 百度翻译支持的常用语种。代码用接口认的那一套：日语 jp、
        /// 韩语 kor、法语 fra、西班牙语 spa、阿拉伯语 ara、越南语 vie。
        /// </summary>
        private static readonly LanguageOption[] Languages =
            new LanguageOption[]
            {
                new LanguageOption("auto", "自动检测"),
                new LanguageOption("zh", "中文"),
                new LanguageOption("en", "英语"),
                new LanguageOption("jp", "日语"),
                new LanguageOption("kor", "韩语"),
                new LanguageOption("fra", "法语"),
                new LanguageOption("spa", "西班牙语"),
                new LanguageOption("de", "德语"),
                new LanguageOption("ru", "俄语"),
                new LanguageOption("pt", "葡萄牙语"),
                new LanguageOption("it", "意大利语"),
                new LanguageOption("ar", "阿拉伯语"),
                new LanguageOption("th", "泰语"),
                new LanguageOption("vie", "越南语"),
                new LanguageOption("cht", "繁体中文"),
            };

        /// <summary>把接口返回的语种代码翻成中文名，状态栏别只显示 zh → en。</summary>
        private static string DescribeCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return "自动";
            }
            for (int i = 0; i < Languages.Length; i++)
            {
                if (string.Equals(
                    Languages[i].Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return Languages[i].Name;
                }
            }
            return code;
        }

        private void FillLanguages()
        {
            string from = _config == null ? "" : _config.TranslateFromLang;
            if (string.IsNullOrEmpty(from))
            {
                from = "auto";
            }
            string to = _config == null ? "" : _config.TranslateToLang;
            if (string.IsNullOrEmpty(to))
            {
                // 没单独选过就跟随「设置」里的目标语言。
                to = _config == null ? "" : _config.TargetLanguage;
            }
            if (string.IsNullOrEmpty(to))
            {
                to = "auto";
            }

            _fromBox.BeginUpdate();
            _toBox.BeginUpdate();
            for (int i = 0; i < Languages.Length; i++)
            {
                LanguageOption option = Languages[i];
                _fromBox.Items.Add(option);
                if (option.Code != "auto")
                {
                    _toBox.Items.Add(option);
                }
            }
            // 目标语言的「自动」= 原来的中英互译规则。
            _toBox.Items.Insert(
                0, new LanguageOption("auto", "自动（中→英，其他→中）"));
            _fromBox.EndUpdate();
            _toBox.EndUpdate();

            SelectCode(_fromBox, from);
            SelectCode(_toBox, to);
            UpdateSwapVisibility();
        }

        private static void SelectCode(ComboBox box, string code)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                LanguageOption option = box.Items[i] as LanguageOption;
                if (option != null && string.Equals(
                    option.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            if (box.Items.Count > 0)
            {
                box.SelectedIndex = 0;
            }
        }

        private string CurrentFrom
        {
            get
            {
                LanguageOption option =
                    _fromBox.SelectedItem as LanguageOption;
                return option == null ? "auto" : option.Code;
            }
        }

        private string CurrentTo
        {
            get
            {
                LanguageOption option = _toBox.SelectedItem as LanguageOption;
                return option == null ? "" : option.Code;
            }
        }

        /// <summary>只有两边都选了具体语种才需要「对调」。</summary>
        private void UpdateSwapVisibility()
        {
            _swapButton.Visible = _fromBox.SelectedIndex >= 0 &&
                _toBox.SelectedIndex >= 0 &&
                CurrentFrom != "auto" && CurrentTo != "auto";
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            UpdateSwapVisibility();
            if (_busy || string.IsNullOrEmpty(_translatedText))
            {
                return;
            }
            _translatedText = "";
            _resultBox.Text = "";
            _copyButton.Enabled = false;
            _statusLabel.Text = "语言已更改，点击「翻译」重新翻译";
        }

        private void OnSwapLanguages(object sender, EventArgs e)
        {
            string from = CurrentFrom;
            string to = CurrentTo;
            if (from == "auto" || to == "auto")
            {
                return;
            }
            SelectCode(_fromBox, to);
            SelectCode(_toBox, from);
            _statusLabel.Text = "已对调语言方向，点击「翻译」";
        }

        private void SaveLanguageChoice(string from, string to)
        {
            if (_config == null)
            {
                return;
            }
            if (string.Equals(_config.TranslateFromLang, from,
                    StringComparison.Ordinal) &&
                string.Equals(_config.TranslateToLang, to,
                    StringComparison.Ordinal))
            {
                return;
            }
            try
            {
                AppConfig saved = ConfigStore.Load() ?? _config;
                saved.TranslateFromLang = from;
                saved.TranslateToLang = to;
                ConfigStore.Save(saved);
                _config.TranslateFromLang = from;
                _config.TranslateToLang = to;
            }
            catch (Exception ex)
            {
                Log.Error("保存翻译语言失败", ex);
            }
        }

        /// <summary>自检用：把语言选择那一行的实际排版打印出来。</summary>
        internal string DescribeLanguageRow()
        {
            return string.Format(
                "from=[{0}] {1} to=[{2}] {3} swap={4}@{5}",
                _fromBox.Text, _fromBox.Bounds,
                _toBox.Text, _toBox.Bounds,
                _swapButton.Visible ? "on" : "off",
                _swapButton.Bounds);
        }

        public void ShowNear(Point point)
        {
            Rectangle workingArea = Screen.FromPoint(point).WorkingArea;
            int x = point.X + 18;
            int y = point.Y + 18;
            if (x + Width > workingArea.Right)
            {
                x = point.X - Width - 18;
            }
            if (y + Height > workingArea.Bottom)
            {
                y = point.Y - Height - 18;
            }
            if (x < workingArea.Left)
            {
                x = workingArea.Left + 8;
            }
            if (y < workingArea.Top)
            {
                y = workingArea.Top + 8;
            }
            Location = new Point(x, y);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_manualInput)
            {
                _sourceBox.Focus();
                return;
            }
            BeginTranslate();
        }

        private async void BeginTranslate()
        {
            if (_busy)
            {
                return;
            }

            string from = CurrentFrom;
            string to = CurrentTo;
            // 中文→中文这种组合百度会直接报错，先在界面上拦下来。
            if (from != "auto" && to != "auto" && from == to)
            {
                _translatedText = "";
                _resultBox.Text = "";
                _copyButton.Enabled = false;
                _statusLabel.Text = "源语言和目标语言相同，请换一个目标语言";
                return;
            }

            _busy = true;
            _settingsLink.Visible = false;
            _retryButton.Enabled = false;
            _copyButton.Enabled = false;
            _resultBox.Text = "翻译中…";
            _statusLabel.Text = "正在请求百度翻译…";
            SaveLanguageChoice(from, to);

            try
            {
                TranslationResult result = await BaiduTranslator.TranslateAsync(
                    _config,
                    _sourceBox.Text,
                    from,
                    to);
                _translatedText = result.Text;
                _resultBox.Text = result.Text;
                _statusLabel.Text = string.Format(
                    "{0} → {1}",
                    DescribeCode(result.From),
                    DescribeCode(result.To));
                _copyButton.Enabled = true;
            }
            catch (TranslationException ex)
            {
                _translatedText = "";
                _resultBox.Text = ex.Message;
                _statusLabel.Text = "翻译失败";
                if (ex.Code == "NO_CREDENTIALS")
                {
                    _settingsLink.Visible = true;
                }
                Log.Info("翻译失败：" + ex.Code + " " + ex.Message);
            }
            catch (Exception ex)
            {
                _translatedText = "";
                _resultBox.Text = "翻译失败：" + ex.Message;
                _statusLabel.Text = "翻译失败";
                Log.Error("翻译时发生未处理错误", ex);
            }
            finally
            {
                _busy = false;
                _retryButton.Enabled = true;
            }
        }

        private void OnRetryClicked(object sender, EventArgs e)
        {
            BeginTranslate();
        }

        private void OnCopyClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_translatedText))
            {
                return;
            }

            ClipboardHelper.SetText(_translatedText);
            _statusLabel.Text = "译文已复制到剪贴板";
        }

        private void OnCopySourceClicked(object sender, EventArgs e)
        {
            string source = _sourceBox.Text ?? "";
            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            ClipboardHelper.SetText(source);
            _statusLabel.Text = "原文已复制到剪贴板";
        }

        /// <summary>
        /// 用户修改原文后，旧译文失效，禁用复制并提示重新翻译。
        /// </summary>
        private void OnSourceTextChanged(object sender, EventArgs e)
        {
            if (_busy || string.IsNullOrEmpty(_translatedText))
            {
                return;
            }

            _translatedText = "";
            _resultBox.Text = "";
            _copyButton.Enabled = false;
            _statusLabel.Text = "原文已修改，点击「翻译」重新翻译";
        }

        private void OnSettingsLinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e)
        {
            Close();
            if (_openSettings != null)
            {
                _openSettings();
            }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
            else if (_manualInput && e.KeyCode == Keys.Enter)
            {
                BeginTranslate();
            }
        }
    }
}
