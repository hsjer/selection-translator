using System;
using System.Diagnostics;
using System.Drawing;
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
            ClientSize = new Size(480, 558);
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

            KeyDown += OnSettingsKeyDown;

            Label hintLabel = new Label();
            hintLabel.Text =
                "点击快捷键输入框后按下组合键即可自定义。\r\n" +
                "超长文本会自动分段翻译。";
            hintLabel.Location = new Point(16, 478);
            hintLabel.Size = new Size(448, 46);
            hintLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(hintLabel);

            _testButton = new Button();
            _testButton.Text = "测试连接";
            _testButton.Location = new Point(220, 528);
            _testButton.Size = new Size(84, 28);
            _testButton.Click += OnTestClicked;
            Controls.Add(_testButton);

            Button saveButton = new Button();
            saveButton.Text = "保存";
            saveButton.Location = new Point(310, 528);
            saveButton.Size = new Size(68, 28);
            saveButton.Click += OnSaveClicked;
            Controls.Add(saveButton);

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.Location = new Point(384, 528);
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
            return edited;
        }

        private void OnShowSecretChanged(object sender, EventArgs e)
        {
            _secretBox.UseSystemPasswordChar = !_showSecretBox.Checked;
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

            // 两个快捷键不能相同。
            if (edited.HotkeyTranslateSelection.Equals(
                edited.HotkeyManualInput,
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    "两个快捷键不能相同。\r\n" +
                    "请修改其中一个。",
                    "保存设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Config = edited;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
