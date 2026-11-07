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
            ClientSize = new Size(580, 430);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);

            Label sourceLabel = new Label();
            sourceLabel.Text = "原文";
            sourceLabel.AutoSize = true;
            sourceLabel.Location = new Point(14, 12);
            sourceLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(sourceLabel);

            _sourceBox = new TextBox();
            _sourceBox.Multiline = true;
            _sourceBox.ScrollBars = ScrollBars.Vertical;
            _sourceBox.BackColor = Color.White;
            _sourceBox.BorderStyle = BorderStyle.FixedSingle;
            _sourceBox.Location = new Point(14, 34);
            _sourceBox.Size = new Size(552, 76);
            _sourceBox.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                AnchorStyles.Right;
            _sourceBox.Text = _manualInput ? "" : (text ?? "");
            _sourceBox.TextChanged += OnSourceTextChanged;
            Controls.Add(_sourceBox);

            Label resultLabel = new Label();
            resultLabel.Text = "译文";
            resultLabel.AutoSize = true;
            resultLabel.Location = new Point(14, 122);
            resultLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(resultLabel);

            _resultBox = new TextBox();
            _resultBox.Multiline = true;
            _resultBox.ReadOnly = true;
            _resultBox.ScrollBars = ScrollBars.Vertical;
            _resultBox.BackColor = Color.White;
            _resultBox.BorderStyle = BorderStyle.FixedSingle;
            _resultBox.Location = new Point(14, 144);
            _resultBox.Size = new Size(552, 210);
            _resultBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_resultBox);

            _statusLabel = new Label();
            _statusLabel.Text = "";
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Location = new Point(14, 366);
            _statusLabel.Size = new Size(360, 22);
            _statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left |
                AnchorStyles.Right;
            _statusLabel.ForeColor = Color.FromArgb(90, 98, 112);
            Controls.Add(_statusLabel);

            _settingsLink = new LinkLabel();
            _settingsLink.Text = "打开设置";
            _settingsLink.AutoSize = true;
            _settingsLink.Location = new Point(14, 394);
            _settingsLink.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _settingsLink.Visible = false;
            _settingsLink.LinkClicked += OnSettingsLinkClicked;
            Controls.Add(_settingsLink);

            _retryButton = new Button();
            _retryButton.Text = "翻译";
            _retryButton.Location = new Point(414, 390);
            _retryButton.Size = new Size(72, 28);
            _retryButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _retryButton.Click += OnRetryClicked;
            Controls.Add(_retryButton);

            _copyButton = new Button();
            _copyButton.Text = "复制译文";
            _copyButton.Location = new Point(334, 390);
            _copyButton.Size = new Size(72, 28);
            _copyButton.Anchor = AnchorStyles.Bottom;
            _copyButton.Enabled = false;
            _copyButton.Click += OnCopyClicked;
            Controls.Add(_copyButton);

            _copySourceButton = new Button();
            _copySourceButton.Text = "复制原文";
            _copySourceButton.Location = new Point(254, 390);
            _copySourceButton.Size = new Size(72, 28);
            _copySourceButton.Anchor = AnchorStyles.Bottom;
            _copySourceButton.Click += OnCopySourceClicked;
            Controls.Add(_copySourceButton);

            Button closeButton = new Button();
            closeButton.Text = "关闭";
            closeButton.Location = new Point(494, 390);
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

            _busy = true;
            _settingsLink.Visible = false;
            _retryButton.Enabled = false;
            _copyButton.Enabled = false;
            _resultBox.Text = "翻译中…";
            _statusLabel.Text = "正在请求百度翻译…";

            try
            {
                TranslationResult result = await BaiduTranslator.TranslateAsync(
                    _config,
                    _sourceBox.Text);
                _translatedText = result.Text;
                _resultBox.Text = result.Text;
                _statusLabel.Text = string.Format(
                    "{0} → {1}",
                    string.IsNullOrEmpty(result.From) ? "auto" : result.From,
                    result.To);
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
