using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class SettingsForm : Form
    {
        private sealed class LanguageOption
        {
            public string Code { get; set; }
            public string Name { get; set; }

            public override string ToString()
            {
                return Name;
            }
        }

        private readonly TextBox _appIdBox;
        private readonly TextBox _secretBox;
        private readonly CheckBox _showSecretBox;
        private readonly ComboBox _languageBox;
        private readonly CheckBox _rightClickBox;
        private readonly CheckBox _fallbackBox;
        private readonly CheckBox _showBubbleBox;
        private readonly CheckBox _startupBox;
        private readonly NumericUpDown _maxTextBox;
        private readonly TextBox _hotkeyTranslateField;
        private readonly TextBox _hotkeyManualField;
        private readonly TextBox _hotkeyScreenshotField;
        private readonly ComboBox _imageLangBox;
        private readonly TextBox _tencentIdBox;
        private readonly TextBox _tencentKeyBox;
        private readonly CheckBox _tencentShowKeyBox;
        private readonly CheckBox _useTencentBox;
        private readonly ComboBox _tencentRegionBox;
        private readonly ComboBox _tencentConfigBox;
        private readonly Button _ocrTestButton;
        private readonly CheckBox _voiceEnableBox;
        private readonly TextBox _hotkeyVoiceField;
        private readonly ComboBox _voiceEngineBox;
        private readonly ComboBox _voiceMethodBox;
        private readonly CheckBox _voiceAutoCopyBox;
        private TextBox _capturingField;
        private string _capturingFieldOriginal;
        private readonly Button _testButton;

        public AppConfig Config { get; private set; }

        public SettingsForm(AppConfig config)
        {
            Config = config;

            Text = "设置 - 选中文本翻译";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            // 内容总高约 900；屏幕不够高时允许滚动，避免按钮被裁掉。
            int availableHeight =
                Screen.PrimaryScreen.WorkingArea.Height - 60;
            ClientSize = new Size(
                480, Math.Min(938, Math.Max(560, availableHeight)));
            AutoScroll = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);

            Label appIdLabel = new Label();
            appIdLabel.Text = "百度翻译 AppID";
            appIdLabel.AutoSize = true;
            appIdLabel.Location = new Point(16, 16);
            Controls.Add(appIdLabel);

            _appIdBox = new TextBox();
            _appIdBox.Location = new Point(16, 38);
            _appIdBox.Size = new Size(432, 24);
            _appIdBox.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                AnchorStyles.Right;
            _appIdBox.Text = config.AppId ?? "";
            Controls.Add(_appIdBox);

            LinkLabel platformLink = new LinkLabel();
            platformLink.Text = "打开百度翻译开放平台，查看 AppID 和密钥";
            platformLink.AutoSize = true;
            platformLink.Location = new Point(16, 68);
            platformLink.LinkClicked += OnPlatformLinkClicked;
            Controls.Add(platformLink);

            Label secretLabel = new Label();
            secretLabel.Text = "密钥";
            secretLabel.AutoSize = true;
            secretLabel.Location = new Point(16, 96);
            Controls.Add(secretLabel);

            _secretBox = new TextBox();
            _secretBox.Location = new Point(16, 118);
            _secretBox.Size = new Size(432, 24);
            _secretBox.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                AnchorStyles.Right;
            _secretBox.UseSystemPasswordChar = true;
            _secretBox.Text = config.GetSecret();
            Controls.Add(_secretBox);

            _showSecretBox = new CheckBox();
            _showSecretBox.Text = "显示密钥";
            _showSecretBox.AutoSize = true;
            _showSecretBox.Location = new Point(16, 148);
            _showSecretBox.CheckedChanged += OnShowSecretChanged;
            Controls.Add(_showSecretBox);

            Label languageLabel = new Label();
            languageLabel.Text = "目标语言";
            languageLabel.AutoSize = true;
            languageLabel.Location = new Point(16, 180);
            Controls.Add(languageLabel);

            _languageBox = new ComboBox();
            _languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _languageBox.Location = new Point(16, 202);
            _languageBox.Size = new Size(220, 24);
            AddLanguage("auto", "自动（中文→英文，其他→中文）");
            AddLanguage("zh", "中文");
            AddLanguage("en", "英语");
            AddLanguage("ja", "日语");
            AddLanguage("ko", "韩语");
            AddLanguage("fr", "法语");
            AddLanguage("de", "德语");
            AddLanguage("ru", "俄语");
            AddLanguage("es", "西班牙语");
            AddLanguage("pt", "葡萄牙语");
            AddLanguage("it", "意大利语");
            AddLanguage("ar", "阿拉伯语");
            AddLanguage("th", "泰语");
            AddLanguage("vi", "越南语");
            SelectLanguage(config.TargetLanguage);
            Controls.Add(_languageBox);

            Label imageLangLabel = new Label();
            imageLangLabel.Text = "图片文字 → 译文目标语言";
            imageLangLabel.AutoSize = true;
            imageLangLabel.Location = new Point(248, 180);
            Controls.Add(imageLangLabel);

            _imageLangBox = new ComboBox();
            _imageLangBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _imageLangBox.Location = new Point(248, 202);
            _imageLangBox.Size = new Size(216, 24);
            // 新管道走百度「文本翻译」，这里用文本翻译的语种代码；
            // 回退到百度图片翻译时由 PicturePipeline 负责映射。
            AddImageLang("auto", "自动");
            AddImageLang("zh", "中文");
            AddImageLang("en", "英语");
            AddImageLang("ja", "日语");
            AddImageLang("ko", "韩语");
            AddImageLang("fr", "法语");
            AddImageLang("es", "西班牙语");
            AddImageLang("ru", "俄语");
            AddImageLang("pt", "葡萄牙语");
            AddImageLang("de", "德语");
            AddImageLang("it", "意大利语");
            AddImageLang("ar", "阿拉伯语");
            AddImageLang("th", "泰语");
            AddImageLang("vi", "越南语");
            AddImageLang("nl", "荷兰语");
            AddImageLang("pl", "波兰语");
            AddImageLang("tr", "土耳其语");
            AddImageLang("el", "希腊语");
            AddImageLang("sv", "瑞典语");
            AddImageLang("da", "丹麦语");
            AddImageLang("id", "印尼语");
            SelectImageLang(config.ImageTargetLanguage);
            Controls.Add(_imageLangBox);

            _rightClickBox = new CheckBox();
            _rightClickBox.Text = "选中文本后右键显示「译」悬浮按钮";
            _rightClickBox.AutoSize = true;
            _rightClickBox.Location = new Point(16, 240);
            _rightClickBox.Checked = config.EnableRightClick;
            Controls.Add(_rightClickBox);

            _fallbackBox = new CheckBox();
            _fallbackBox.Text = "读不到选区时自动用 Ctrl+C 兜底（兼容 IDEA 等应用）";
            _fallbackBox.AutoSize = true;
            _fallbackBox.Location = new Point(16, 266);
            _fallbackBox.Checked = config.EnableClipboardFallback;
            Controls.Add(_fallbackBox);

            _showBubbleBox = new CheckBox();
            _showBubbleBox.Text = "选中文本后自动显示「译」悬浮按钮";
            _showBubbleBox.AutoSize = true;
            _showBubbleBox.Location = new Point(16, 292);
            _showBubbleBox.Checked = config.ShowBubbleOnSelection;
            Controls.Add(_showBubbleBox);

            _startupBox = new CheckBox();
            _startupBox.Text = "开机自动启动";
            _startupBox.AutoSize = true;
            _startupBox.Location = new Point(16, 318);
            _startupBox.Checked = config.StartWithWindows;
            Controls.Add(_startupBox);

            Label maxTextLabel = new Label();
            maxTextLabel.Text = "最大文本长度（字符）";
            maxTextLabel.AutoSize = true;
            maxTextLabel.Location = new Point(16, 344);
            Controls.Add(maxTextLabel);

            _maxTextBox = new NumericUpDown();
            _maxTextBox.Location = new Point(16, 366);
            _maxTextBox.Size = new Size(220, 24);
            _maxTextBox.Minimum = 100;
            _maxTextBox.Maximum = 20000;
            _maxTextBox.Increment = 100;
            int initialMax = config.MaxTextLength;
            if (initialMax < 100)
            {
                initialMax = 3000;
            }
            _maxTextBox.Value =
                (int)Math.Max(100, Math.Min(20000, initialMax));
            Controls.Add(_maxTextBox);

            Label hotkeyTranslateLabel = new Label();
            hotkeyTranslateLabel.Text = "翻译选中文本快捷键";
            hotkeyTranslateLabel.AutoSize = true;
            hotkeyTranslateLabel.Location = new Point(16, 402);
            Controls.Add(hotkeyTranslateLabel);

            _hotkeyTranslateField = new TextBox();
            _hotkeyTranslateField.ReadOnly = true;
            _hotkeyTranslateField.TextAlign = HorizontalAlignment.Center;
            _hotkeyTranslateField.Location = new Point(180, 402);
            _hotkeyTranslateField.Size = new Size(120, 24);
            _hotkeyTranslateField.Text = config.HotkeyTranslateSelection;
            _hotkeyTranslateField.Click += OnHotkeyFieldClicked;
            _hotkeyTranslateField.Leave += OnHotkeyFieldLeave;
            Controls.Add(_hotkeyTranslateField);

            Label hotkeyManualLabel = new Label();
            hotkeyManualLabel.Text = "手动翻译快捷键";
            hotkeyManualLabel.AutoSize = true;
            hotkeyManualLabel.Location = new Point(16, 436);
            Controls.Add(hotkeyManualLabel);

            _hotkeyManualField = new TextBox();
            _hotkeyManualField.ReadOnly = true;
            _hotkeyManualField.TextAlign = HorizontalAlignment.Center;
            _hotkeyManualField.Location = new Point(180, 436);
            _hotkeyManualField.Size = new Size(120, 24);
            _hotkeyManualField.Text = config.HotkeyManualInput;
            _hotkeyManualField.Click += OnHotkeyFieldClicked;
            _hotkeyManualField.Leave += OnHotkeyFieldLeave;
            Controls.Add(_hotkeyManualField);

            Label hotkeyScreenshotLabel = new Label();
            hotkeyScreenshotLabel.Text = "截图翻译快捷键";
            hotkeyScreenshotLabel.AutoSize = true;
            hotkeyScreenshotLabel.Location = new Point(16, 470);
            Controls.Add(hotkeyScreenshotLabel);

            _hotkeyScreenshotField = new TextBox();
            _hotkeyScreenshotField.ReadOnly = true;
            _hotkeyScreenshotField.TextAlign = HorizontalAlignment.Center;
            _hotkeyScreenshotField.Location = new Point(180, 470);
            _hotkeyScreenshotField.Size = new Size(120, 24);
            _hotkeyScreenshotField.Text = config.HotkeyScreenshot;
            _hotkeyScreenshotField.Click += OnHotkeyFieldClicked;
            _hotkeyScreenshotField.Leave += OnHotkeyFieldLeave;
            Controls.Add(_hotkeyScreenshotField);

            KeyDown += OnSettingsKeyDown;

            Label hintLabel = new Label();
            hintLabel.Text =
                "点击快捷键输入框后按下组合键即可自定义。\r\n" +
                "四个快捷键不能相同。超长文本会自动分段翻译。";
            hintLabel.Location = new Point(16, 504);
            hintLabel.Size = new Size(440, 46);
            hintLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(hintLabel);

            // --------------------------------------------------------------
            // 文字识别（腾讯云 OCR）
            // --------------------------------------------------------------
            GroupBox tencentGroup = new GroupBox();
            tencentGroup.Text = "文字识别（腾讯云 OCR · GeneralAccurateOCR）";
            tencentGroup.Location = new Point(12, 552);
            tencentGroup.Size = new Size(448, 178);
            tencentGroup.Font = Font;
            Controls.Add(tencentGroup);

            _useTencentBox = new CheckBox();
            _useTencentBox.Text = "用腾讯云识别图中文字（推荐，取字更快）";
            _useTencentBox.AutoSize = true;
            _useTencentBox.Location = new Point(12, 22);
            _useTencentBox.Checked = config.UseTencentOcr;
            tencentGroup.Controls.Add(_useTencentBox);

            LinkLabel tencentLink = new LinkLabel();
            tencentLink.Text = "如何获取密钥？";
            tencentLink.AutoSize = true;
            tencentLink.Location = new Point(316, 24);
            tencentLink.LinkClicked += OnTencentLinkClicked;
            tencentGroup.Controls.Add(tencentLink);

            Label tencentIdLabel = new Label();
            tencentIdLabel.Text = "SecretId";
            tencentIdLabel.AutoSize = true;
            tencentIdLabel.Location = new Point(12, 52);
            tencentGroup.Controls.Add(tencentIdLabel);

            _tencentIdBox = new TextBox();
            _tencentIdBox.Location = new Point(96, 48);
            _tencentIdBox.Size = new Size(344, 23);
            _tencentIdBox.Text = config.TencentSecretId ?? "";
            tencentGroup.Controls.Add(_tencentIdBox);

            Label tencentKeyLabel = new Label();
            tencentKeyLabel.Text = "SecretKey";
            tencentKeyLabel.AutoSize = true;
            tencentKeyLabel.Location = new Point(12, 80);
            tencentGroup.Controls.Add(tencentKeyLabel);

            _tencentKeyBox = new TextBox();
            _tencentKeyBox.Location = new Point(96, 76);
            _tencentKeyBox.Size = new Size(240, 23);
            _tencentKeyBox.UseSystemPasswordChar = true;
            _tencentKeyBox.Text = config.GetTencentSecretKey();
            tencentGroup.Controls.Add(_tencentKeyBox);

            _tencentShowKeyBox = new CheckBox();
            _tencentShowKeyBox.Text = "显示";
            _tencentShowKeyBox.AutoSize = true;
            _tencentShowKeyBox.Location = new Point(342, 79);
            _tencentShowKeyBox.CheckedChanged +=
                OnTencentShowKeyChanged;
            tencentGroup.Controls.Add(_tencentShowKeyBox);

            Label regionLabel = new Label();
            regionLabel.Text = "地域";
            regionLabel.AutoSize = true;
            regionLabel.Location = new Point(12, 108);
            tencentGroup.Controls.Add(regionLabel);

            _tencentRegionBox = new ComboBox();
            _tencentRegionBox.DropDownStyle =
                ComboBoxStyle.DropDownList;
            _tencentRegionBox.Location = new Point(60, 104);
            _tencentRegionBox.Size = new Size(140, 23);
            _tencentRegionBox.Items.AddRange(new object[]
            {
                "ap-guangzhou", "ap-shanghai", "ap-beijing",
                "ap-hongkong", "ap-singapore"
            });
            SelectCombo(_tencentRegionBox,
                string.IsNullOrEmpty(config.TencentRegion)
                    ? TencentOcr.DefaultRegion
                    : config.TencentRegion);
            tencentGroup.Controls.Add(_tencentRegionBox);

            Label configLabel = new Label();
            configLabel.Text = "识别模式";
            configLabel.AutoSize = true;
            configLabel.Location = new Point(210, 108);
            tencentGroup.Controls.Add(configLabel);

            _tencentConfigBox = new ComboBox();
            _tencentConfigBox.DropDownStyle =
                ComboBoxStyle.DropDownList;
            _tencentConfigBox.Location = new Point(272, 104);
            _tencentConfigBox.Size = new Size(168, 23);
            _tencentConfigBox.Items.AddRange(new object[]
            {
                "MulOCR（多语种）", "OCR（通用）"
            });
            _tencentConfigBox.SelectedIndex =
                string.Equals(config.TencentOcrConfigId, "OCR",
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            tencentGroup.Controls.Add(_tencentConfigBox);

            Label tencentHint = new Label();
            tencentHint.Text =
                "识别用腾讯云，翻译仍用百度文本翻译；关闭开关则回退百度图片翻译。\r\n" +
                "SecretKey 用 Windows DPAPI 加密保存在本机，不会随 exe 拷走。";
            tencentHint.Location = new Point(12, 136);
            tencentHint.Size = new Size(424, 36);
            tencentHint.ForeColor = Color.FromArgb(90, 98, 112);
            tencentGroup.Controls.Add(tencentHint);

            // --------------------------------------------------------------
            // 语音识别（腾讯云一句话识别）
            // --------------------------------------------------------------
            GroupBox voiceGroup = new GroupBox();
            voiceGroup.Text = "语音识别（腾讯云一句话识别 · 按住说话）";
            voiceGroup.Location = new Point(12, 738);
            voiceGroup.Size = new Size(448, 156);
            voiceGroup.Font = Font;
            Controls.Add(voiceGroup);

            _voiceEnableBox = new CheckBox();
            _voiceEnableBox.Text = "启用语音识别（按住快捷键说话，松开即识别）";
            _voiceEnableBox.AutoSize = true;
            _voiceEnableBox.Location = new Point(12, 22);
            _voiceEnableBox.Checked = config.EnableVoiceInput;
            voiceGroup.Controls.Add(_voiceEnableBox);

            Label voiceHotkeyLabel = new Label();
            voiceHotkeyLabel.Text = "语音快捷键";
            voiceHotkeyLabel.AutoSize = true;
            voiceHotkeyLabel.Location = new Point(12, 52);
            voiceGroup.Controls.Add(voiceHotkeyLabel);

            _hotkeyVoiceField = new TextBox();
            _hotkeyVoiceField.ReadOnly = true;
            _hotkeyVoiceField.TextAlign = HorizontalAlignment.Center;
            _hotkeyVoiceField.Location = new Point(96, 48);
            _hotkeyVoiceField.Size = new Size(110, 23);
            _hotkeyVoiceField.Text = config.HotkeyVoiceInput;
            _hotkeyVoiceField.Click += OnHotkeyFieldClicked;
            _hotkeyVoiceField.Leave += OnHotkeyFieldLeave;
            voiceGroup.Controls.Add(_hotkeyVoiceField);

            Label voiceEngineLabel = new Label();
            voiceEngineLabel.Text = "引擎";
            voiceEngineLabel.AutoSize = true;
            voiceEngineLabel.Location = new Point(220, 52);
            voiceGroup.Controls.Add(voiceEngineLabel);

            _voiceEngineBox = new ComboBox();
            _voiceEngineBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _voiceEngineBox.Location = new Point(256, 48);
            _voiceEngineBox.Size = new Size(180, 23);
            System.Collections.Generic.List<AsrEngineOption> engines =
                TencentAsr.BuildEngineOptions();
            for (int i = 0; i < engines.Count; i++)
            {
                _voiceEngineBox.Items.Add(engines[i]);
            }
            SelectVoiceEngine(config.VoiceEngineType);
            voiceGroup.Controls.Add(_voiceEngineBox);

            Label voiceMethodLabel = new Label();
            voiceMethodLabel.Text = "上屏方式";
            voiceMethodLabel.AutoSize = true;
            voiceMethodLabel.Location = new Point(12, 82);
            voiceGroup.Controls.Add(voiceMethodLabel);

            _voiceMethodBox = new ComboBox();
            _voiceMethodBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _voiceMethodBox.Location = new Point(80, 78);
            _voiceMethodBox.Size = new Size(160, 23);
            _voiceMethodBox.Items.Add("模拟键入（推荐）");
            _voiceMethodBox.Items.Add("剪贴板粘贴（兼容）");
            _voiceMethodBox.SelectedIndex =
                InputFocusProbe.NormalizeMethod(config.VoiceInputMethod) ==
                    InputFocusProbe.MethodPaste ? 1 : 0;
            voiceGroup.Controls.Add(_voiceMethodBox);

            _voiceAutoCopyBox = new CheckBox();
            _voiceAutoCopyBox.Text = "同时复制到剪贴板";
            _voiceAutoCopyBox.AutoSize = true;
            _voiceAutoCopyBox.Location = new Point(252, 80);
            _voiceAutoCopyBox.Checked = config.VoiceAutoCopy;
            voiceGroup.Controls.Add(_voiceAutoCopyBox);

            Label voiceHint = new Label();
            voiceHint.Text =
                "任意输入框都能用，不限控件类型，松开即直接上屏；\r\n" +
                "个别软件打不出字时改用「剪贴板粘贴」。";
            voiceHint.Location = new Point(12, 104);
            voiceHint.Size = new Size(424, 40);
            voiceHint.ForeColor = Color.FromArgb(90, 98, 112);
            voiceGroup.Controls.Add(voiceHint);

            // --------------------------------------------------------------
            // 底部按钮
            // --------------------------------------------------------------
            _ocrTestButton = new Button();
            _ocrTestButton.Text = "测试 OCR";
            _ocrTestButton.Location = new Point(16, 900);
            _ocrTestButton.Size = new Size(88, 28);
            _ocrTestButton.Click += OnOcrTestClicked;
            Controls.Add(_ocrTestButton);

            _testButton = new Button();
            _testButton.Text = "测试翻译";
            _testButton.Location = new Point(112, 900);
            _testButton.Size = new Size(88, 28);
            _testButton.Click += OnTestClicked;
            Controls.Add(_testButton);

            Button micTestButton = new Button();
            micTestButton.Text = "测试麦克风";
            micTestButton.Location = new Point(208, 900);
            micTestButton.Size = new Size(94, 28);
            micTestButton.Click += OnMicTestClicked;
            Controls.Add(micTestButton);

            Button saveButton = new Button();
            saveButton.Text = "保存";
            saveButton.Location = new Point(310, 900);
            saveButton.Size = new Size(68, 28);
            saveButton.Click += OnSaveClicked;
            Controls.Add(saveButton);

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.Location = new Point(384, 900);
            cancelButton.Size = new Size(64, 28);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        private void AddLanguage(string code, string name)
        {
            LanguageOption option = new LanguageOption();
            option.Code = code;
            option.Name = name;
            _languageBox.Items.Add(option);
        }

        private void AddImageLang(string code, string name)
        {
            LanguageOption option = new LanguageOption();
            option.Code = code;
            option.Name = name;
            _imageLangBox.Items.Add(option);
        }

        private void SelectImageLang(string code)
        {
            // 兼容旧配置里的图片翻译语种（jp/kor/fra/spa…）。
            string normalized = PicturePipeline.MapTextTarget(code);
            for (int i = 0; i < _imageLangBox.Items.Count; i++)
            {
                LanguageOption option =
                    _imageLangBox.Items[i] as LanguageOption;
                if (option != null &&
                    string.Equals(
                        option.Code,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _imageLangBox.SelectedIndex = i;
                    return;
                }
            }
            _imageLangBox.SelectedIndex = 0;
        }

        private string GetSelectedImageLang()
        {
            LanguageOption option =
                _imageLangBox.SelectedItem as LanguageOption;
            return option == null ? "auto" : option.Code;
        }

        private void SelectLanguage(string code)
        {
            for (int i = 0; i < _languageBox.Items.Count; i++)
            {
                LanguageOption option =
                    _languageBox.Items[i] as LanguageOption;
                if (option != null &&
                    string.Equals(
                        option.Code,
                        code,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _languageBox.SelectedIndex = i;
                    return;
                }
            }
            _languageBox.SelectedIndex = 0;
        }

        private string GetSelectedLanguage()
        {
            LanguageOption option =
                _languageBox.SelectedItem as LanguageOption;
            return option == null ? "auto" : option.Code;
        }

        private AppConfig BuildEditedConfig()
        {
            AppConfig edited = new AppConfig();
            edited.AppId = _appIdBox.Text.Trim();
            edited.SetSecret(_secretBox.Text.Trim());
            edited.TargetLanguage = GetSelectedLanguage();
            edited.EnableRightClick = _rightClickBox.Checked;
            edited.EnableClipboardFallback = _fallbackBox.Checked;
            edited.ShowBubbleOnSelection = _showBubbleBox.Checked;
            edited.StartWithWindows = _startupBox.Checked;
            edited.MaxTextLength = (int)_maxTextBox.Value;
            edited.HotkeyTranslateSelection =
                _hotkeyTranslateField.Text.Trim();
            edited.HotkeyManualInput =
                _hotkeyManualField.Text.Trim();
            edited.HotkeyScreenshot =
                _hotkeyScreenshotField.Text.Trim();
            edited.ImageTargetLanguage = GetSelectedImageLang();
            edited.TencentSecretId = _tencentIdBox.Text.Trim();
            edited.SetTencentSecretKey(_tencentKeyBox.Text.Trim());
            edited.TencentRegion = GetSelectedTencentRegion();
            edited.TencentOcrConfigId =
                _tencentConfigBox.SelectedIndex == 1
                    ? "OCR" : "MulOCR";
            edited.UseTencentOcr = _useTencentBox.Checked;
            edited.EnableVoiceInput = _voiceEnableBox.Checked;
            edited.HotkeyVoiceInput = _hotkeyVoiceField.Text.Trim();
            edited.VoiceEngineType = GetSelectedVoiceEngine();
            edited.VoiceInputMethod = GetSelectedVoiceMethod();
            edited.VoiceAutoCopy = _voiceAutoCopyBox.Checked;
            return edited;
        }

        private static void SelectCombo(ComboBox combo, string value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(
                    Convert.ToString(combo.Items[i]),
                    value,
                    StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private string GetSelectedTencentRegion()
        {
            string value = Convert.ToString(
                _tencentRegionBox.SelectedItem);
            return string.IsNullOrEmpty(value)
                ? TencentOcr.DefaultRegion : value;
        }

        private void SelectVoiceEngine(string code)
        {
            string normalized = TencentAsr.NormalizeEngine(code);
            for (int i = 0; i < _voiceEngineBox.Items.Count; i++)
            {
                AsrEngineOption option =
                    _voiceEngineBox.Items[i] as AsrEngineOption;
                if (option != null &&
                    string.Equals(
                        option.Code,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _voiceEngineBox.SelectedIndex = i;
                    return;
                }
            }
            _voiceEngineBox.SelectedIndex = 0;
        }

        private string GetSelectedVoiceEngine()
        {
            AsrEngineOption option =
                _voiceEngineBox.SelectedItem as AsrEngineOption;
            return option == null
                ? TencentAsr.DefaultEngine : option.Code;
        }

        private string GetSelectedVoiceMethod()
        {
            return _voiceMethodBox.SelectedIndex == 1
                ? InputFocusProbe.MethodPaste
                : InputFocusProbe.MethodType;
        }

        private void OnShowSecretChanged(object sender, EventArgs e)
        {
            _secretBox.UseSystemPasswordChar = !_showSecretBox.Checked;
        }

        private void OnTencentShowKeyChanged(object sender, EventArgs e)
        {
            _tencentKeyBox.UseSystemPasswordChar =
                !_tencentShowKeyBox.Checked;
        }

        private void OnTencentLinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://console.cloud.tencent.com/cam/capi")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "无法打开浏览器：" + ex.Message,
                    "选中文本翻译",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 用一张程序生成的含文字小图验证腾讯云凭证与签名。
        /// 只要不是鉴权类错误就认为凭证有效（测试图可能识别不出文字）。
        /// </summary>
        private async void OnOcrTestClicked(object sender, EventArgs e)
        {
            AppConfig edited = BuildEditedConfig();
            if (!edited.HasTencentCredentials())
            {
                MessageBox.Show(
                    this,
                    "请先填写腾讯云 SecretId 和 SecretKey。",
                    "测试 OCR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _ocrTestButton.Enabled = false;
            _ocrTestButton.Text = "测试中…";
            try
            {
                byte[] bytes;
                Size size = new Size(520, 160);
                using (Bitmap bitmap = new Bitmap(
                    size.Width, size.Height))
                {
                    using (Graphics graphics =
                        Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.White);
                        using (Font font = new Font(
                            "Microsoft YaHei UI", 34F,
                            FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush brush =
                            new SolidBrush(Color.Black))
                        {
                            graphics.DrawString(
                                "OCR TEST 123", font, brush, 18f, 50f);
                        }
                    }
                    string note;
                    Size sentSize;
                    bytes = TencentOcr.PrepareImage(
                        bitmap, out note, out sentSize);
                    size = sentSize;
                }

                OcrResult result =
                    await TencentOcr.RecognizeAsync(
                        edited, bytes, size);
                MessageBox.Show(
                    this,
                    "腾讯云凭证有效，签名通过。\r\n\r\n" +
                    "识别行数：" + result.Lines.Count +
                    "\r\nRequestId：" + result.RequestId +
                    (result.SumText.Length > 0
                        ? "\r\n识别内容：" + result.SumText
                        : ""),
                    "测试 OCR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (TranslationException ex)
            {
                // 只要不是鉴权 / 网络类错误，就说明凭证与签名已被服务端接受；
                // 测试小图识别不出文字（ImageNoText）不影响这个结论。
                bool credentialsAccepted =
                    !ex.Code.StartsWith("AuthFailure") &&
                    ex.Code != "NETWORK" &&
                    ex.Code != "TIMEOUT" &&
                    !ex.Code.StartsWith("HTTP_") &&
                    ex.Code != "NO_TENCENT_CREDENTIALS";
                MessageBox.Show(
                    this,
                    credentialsAccepted
                        ? "腾讯云凭证有效，签名通过。\r\n\r\n" +
                          "服务端已受理请求，返回业务结果：" + ex.Message
                        : ex.Message,
                    credentialsAccepted
                        ? "测试 OCR"
                        : "测试 OCR 失败",
                    MessageBoxButtons.OK,
                    credentialsAccepted
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("测试腾讯云 OCR 失败", ex);
                MessageBox.Show(
                    this,
                    "测试失败：" + ex.Message,
                    "测试 OCR 失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                _ocrTestButton.Enabled = true;
                _ocrTestButton.Text = "测试 OCR";
            }
        }

        /// <summary>
        /// 录 3 秒麦克风并真实调用一句话识别，验证麦克风、网络、凭证和引擎。
        /// </summary>
        private async void OnMicTestClicked(object sender, EventArgs e)
        {
            AppConfig edited = BuildEditedConfig();
            if (!edited.HasTencentCredentials())
            {
                MessageBox.Show(
                    this,
                    "请先填写腾讯云 SecretId 和 SecretKey。",
                    "测试麦克风",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            if (VoiceRecorder.DeviceCount() <= 0)
            {
                MessageBox.Show(
                    this,
                    "没有检测到麦克风设备。",
                    "测试麦克风",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Button button = sender as Button;
            if (button != null)
            {
                button.Enabled = false;
            }

            try
            {
                if (button != null)
                {
                    button.Text = "录音 3 秒…";
                }

                byte[] wav;
                using (VoiceRecorder recorder = new VoiceRecorder())
                {
                    recorder.Start();
                    for (int i = 0; i < 15; i++)
                    {
                        await Task.Delay(200);
                        if (button != null && !button.IsDisposed)
                        {
                            button.Text = "录音 " +
                                ((15 - i) * 0.2).ToString("0.0") + " 秒";
                        }
                    }
                    wav = recorder.Stop();
                }

                int durationMs = VoiceRecorder.DurationMs(wav.Length);
                if (button != null && !button.IsDisposed)
                {
                    button.Text = "识别中…";
                }

                AsrResult result = await TencentAsr.RecognizeAsync(
                    edited, wav, durationMs);
                MessageBox.Show(
                    this,
                    "录音 " + (durationMs / 1000.0).ToString("0.0") +
                    " 秒，识别成功。\r\n\r\n" +
                    (result.Text.Length > 0
                        ? "识别内容：" + result.Text
                        : "服务端返回了空文本，请说得大声一点再试。") +
                    "\r\n\r\nRequestId：" + result.RequestId,
                    "测试麦克风",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (TranslationException ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "测试麦克风失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("测试语音识别失败", ex);
                MessageBox.Show(
                    this,
                    "测试失败：" + ex.Message,
                    "测试麦克风失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (button != null && !button.IsDisposed)
                {
                    button.Enabled = true;
                    button.Text = "测试麦克风";
                }
            }
        }

        /// <summary>
        /// 点击快捷键输入框进入捕获模式。
        /// </summary>
        private void OnHotkeyFieldClicked(object sender, EventArgs e)
        {
            TextBox field = sender as TextBox;
            if (field == null)
            {
                return;
            }

            _capturingFieldOriginal = field.Text;
            field.Text = "请按下快捷键…";
            _capturingField = field;
        }

        /// <summary>
        /// 快捷键输入框失去焦点时取消捕获并恢复原文。
        /// </summary>
        private void OnHotkeyFieldLeave(object sender, EventArgs e)
        {
            if (_capturingField == sender)
            {
                _capturingField.Text = _capturingFieldOriginal;
                _capturingField = null;
                _capturingFieldOriginal = "";
            }
        }

        /// <summary>
        /// 表单级 KeyDown：捕获快捷键组合。
        /// </summary>
        private void OnSettingsKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturingField == null ||
                !_capturingField.ContainsFocus)
            {
                return;
            }

            // Esc 取消捕获。
            if (e.KeyCode == Keys.Escape)
            {
                _capturingField.Text = _capturingFieldOriginal;
                _capturingField = null;
                _capturingFieldOriginal = "";
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            // 纯修饰键：等待最终按键。
            if (e.KeyCode == Keys.Control ||
                e.KeyCode == Keys.Alt ||
                e.KeyCode == Keys.Shift ||
                e.KeyCode == Keys.LWin ||
                e.KeyCode == Keys.RWin)
            {
                return;
            }

            // 组合键：把 e.Modifiers 转为 MOD_* 位掩码。
            int modifiers = 0;
            if ((e.Modifiers & Keys.Control) != 0)
            {
                modifiers |= NativeMethods.MOD_CONTROL;
            }
            if ((e.Modifiers & Keys.Alt) != 0)
            {
                modifiers |= NativeMethods.MOD_ALT;
            }
            if ((e.Modifiers & Keys.Shift) != 0)
            {
                modifiers |= NativeMethods.MOD_SHIFT;
            }
            if ((e.Modifiers & Keys.LWin) != 0 ||
                (e.Modifiers & Keys.RWin) != 0)
            {
                modifiers |= NativeMethods.MOD_WIN;
            }

            string hotkey = HotkeyParser.Format(
                modifiers,
                e.KeyCode);
            _capturingField.Text = hotkey;
            _capturingField = null;
            _capturingFieldOriginal = "";
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void OnPlatformLinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://fanyi-api.baidu.com/manage/developer")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "无法打开浏览器：" + ex.Message,
                    "选中文本翻译",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async void OnTestClicked(object sender, EventArgs e)
        {
            AppConfig edited = BuildEditedConfig();
            if (edited.AppId.Length == 0 ||
                edited.GetSecret().Length == 0)
            {
                MessageBox.Show(
                    this,
                    "请先填写 AppID 和密钥。",
                    "测试连接",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _testButton.Enabled = false;
            _testButton.Text = "测试中…";
            try
            {
                TranslationResult result =
                    await BaiduTranslator.TranslateAsync(
                        edited,
                        "Hello, world!");
                MessageBox.Show(
                    this,
                    "连接成功。\r\n\r\nHello, world! → " + result.Text,
                    "测试连接",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (TranslationException ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "测试连接失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("测试百度翻译失败", ex);
                MessageBox.Show(
                    this,
                    "测试失败：" + ex.Message,
                    "测试连接失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                _testButton.Enabled = true;
                _testButton.Text = "测试连接";
            }
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            AppConfig edited = BuildEditedConfig();
            if (edited.AppId.Length == 0 ||
                edited.GetSecret().Length == 0)
            {
                MessageBox.Show(
                    this,
                    "AppID 和密钥不能为空。\r\n\r\n请先在百度翻译开放平台创建应用并开通通用翻译服务。",
                    "保存设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // 校验快捷键格式。
            int modifiers;
            Keys key;
            if (!HotkeyParser.TryParse(
                edited.HotkeyTranslateSelection,
                out modifiers,
                out key))
            {
                MessageBox.Show(
                    this,
                    "「翻译选中文本快捷键」格式无效。\r\n" +
                    "请点击输入框后重新按下组合键。",
                    "保存设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!HotkeyParser.TryParse(
                edited.HotkeyManualInput,
                out modifiers,
                out key))
            {
                MessageBox.Show(
                    this,
                    "「手动翻译快捷键」格式无效。\r\n" +
                    "请点击输入框后重新按下组合键。",
                    "保存设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!HotkeyParser.TryParse(
                edited.HotkeyScreenshot,
                out modifiers,
                out key))
            {
                MessageBox.Show(
                    this,
                    "「截图翻译快捷键」格式无效。\r\n" +
                    "请点击输入框后重新按下组合键。",
                    "保存设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!HotkeyParser.TryParse(
                edited.HotkeyVoiceInput,
                out modifiers,
                out key))
            {
                MessageBox.Show(
                    this,
                    "「语音识别快捷键」格式无效。\r\n" +
                    "请点击输入框后重新按下组合键。",
                    "保存设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // 四个快捷键不能相同。
            string[] hotkeys = new string[]
            {
                edited.HotkeyTranslateSelection,
                edited.HotkeyManualInput,
                edited.HotkeyScreenshot,
                edited.HotkeyVoiceInput
            };
            string[] hotkeyNames = new string[]
            {
                "翻译选中文本",
                "手动翻译",
                "截图翻译",
                "语音识别"
            };
            for (int i = 0; i < hotkeys.Length; i++)
            {
                for (int j = i + 1; j < hotkeys.Length; j++)
                {
                    if (hotkeys[i].Equals(
                                hotkeys[j],
                                StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            this,
                            "「" + hotkeyNames[i] + "」与「" +
                                  hotkeyNames[j] + "」快捷键不能相同。\r\n" +
                            "请修改其中一个。",
                            "保存设置",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            Config = edited;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
