using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 语音识别结果小窗口。
    ///
    /// 文本是可编辑的：识别难免有同音错字，用户可以直接改完再复制。
    /// 另外提供「粘贴到原窗口」——把文字送回说话前光标所在的那个
    /// 输入框，省掉一次手动 Ctrl+V。
    /// </summary>
    internal sealed class VoiceResultForm : Form
    {
        private readonly TextBox _textBox;
        private readonly Label _statusLabel;
        private readonly Func<AppConfig> _configProvider;
        private readonly Action _openSettings;
        private readonly IntPtr _targetWindow;
        private readonly bool _autoCopy;
        private readonly string _engineText;
        private readonly AsrResult _info;
        private readonly int _elapsedMs;

        public event EventHandler RecordAgainRequested;

        public VoiceResultForm(
            string text,
            AsrResult info,
            int elapsedMs,
            string engineText,
            IntPtr targetWindow,
            bool autoCopy,
            Func<AppConfig> configProvider,
            Action openSettings)
        {
            _info = info ?? new AsrResult();
            _elapsedMs = elapsedMs;
            _engineText = string.IsNullOrEmpty(engineText)
                ? TencentAsr.DescribeEngine(null) : engineText;
            _targetWindow = targetWindow;
            _autoCopy = autoCopy;
            _configProvider = configProvider;
            _openSettings = openSettings;

            Text = "语音识别";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 378);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);

            Label title = new Label();
            title.Text = "语音识别结果";
            title.AutoSize = true;
            title.Location = new Point(14, 12);
            title.Font = new Font(
                "Microsoft YaHei UI", 11F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(40, 46, 58);
            Controls.Add(title);

            Label engineLabel = new Label();
            engineLabel.Text = _engineText;
            engineLabel.AutoSize = false;
            engineLabel.TextAlign = ContentAlignment.MiddleRight;
            engineLabel.Location = new Point(260, 14);
            engineLabel.Size = new Size(306, 20);
            engineLabel.ForeColor = Color.FromArgb(120, 128, 142);
            Controls.Add(engineLabel);

            _textBox = new TextBox();
            _textBox.Multiline = true;
            _textBox.ScrollBars = ScrollBars.Vertical;
            _textBox.BackColor = Color.White;
            _textBox.BorderStyle = BorderStyle.FixedSingle;
            _textBox.Location = new Point(14, 42);
            _textBox.Size = new Size(552, 212);
            _textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                AnchorStyles.Right | AnchorStyles.Bottom;
            _textBox.Text = text ?? "";
            _textBox.KeyDown += OnTextBoxKeyDown;
            Controls.Add(_textBox);

            _statusLabel = new Label();
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Location = new Point(14, 260);
            _statusLabel.Size = new Size(552, 22);
            _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right |
                AnchorStyles.Bottom;
            _statusLabel.ForeColor = Color.FromArgb(110, 118, 132);
            _statusLabel.Text = BuildStatus(text);
            Controls.Add(_statusLabel);

            Button againButton = new Button();
            againButton.Text = "重新录音";
            againButton.Location = new Point(14, 336);
            againButton.Size = new Size(88, 30);
            againButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            againButton.Click += OnAgainClicked;
            Controls.Add(againButton);

            Button translateButton = new Button();
            translateButton.Text = "翻译";
            translateButton.Location = new Point(110, 336);
            translateButton.Size = new Size(72, 30);
            translateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            translateButton.Click += OnTranslateClicked;
            Controls.Add(translateButton);

            Button pasteButton = new Button();
            pasteButton.Text = "粘贴到原窗口";
            pasteButton.Location = new Point(190, 336);
            pasteButton.Size = new Size(116, 30);
            pasteButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            pasteButton.Click += OnPasteClicked;
            Controls.Add(pasteButton);

            Button copyButton = new Button();
            copyButton.Text = "复制";
            copyButton.Location = new Point(320, 336);
            copyButton.Size = new Size(72, 30);
            copyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            copyButton.Click += OnCopyClicked;
            Controls.Add(copyButton);

            Button copyCloseButton = new Button();
            copyCloseButton.Text = "复制并关闭";
            copyCloseButton.Location = new Point(398, 336);
            copyCloseButton.Size = new Size(96, 30);
            copyCloseButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            copyCloseButton.Click += OnCopyAndCloseClicked;
            Controls.Add(copyCloseButton);

            Button closeButton = new Button();
            closeButton.Text = "关闭";
            closeButton.Location = new Point(500, 336);
            closeButton.Size = new Size(66, 30);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            AcceptButton = copyCloseButton;
            CancelButton = closeButton;
        }

        private string BuildStatus(string text)
        {
            string body = string.Format(
                "音频 {0:0.0} 秒 · 识别 {1} ms · {2} 字",
                (_info.AudioDurationMs > 0
                    ? _info.AudioDurationMs : 0) / 1000.0,
                _elapsedMs,
                (text ?? "").Length);
            if (_autoCopy)
            {
                body += " · 已复制到剪贴板";
            }
            return body;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_autoCopy)
            {
                ClipboardHelper.SetText(_textBox.Text);
            }

            _textBox.Focus();
            _textBox.SelectAll();
        }

        private string CurrentText
        {
            get { return (_textBox.Text ?? "").Trim(); }
        }

        private void OnCopyClicked(object sender, EventArgs e)
        {
            string text = CurrentText;
            if (text.Length == 0)
            {
                return;
            }
            ClipboardHelper.SetText(text);
            _statusLabel.Text = "已复制到剪贴板";
        }

        private void OnCopyAndCloseClicked(object sender, EventArgs e)
        {
            string text = CurrentText;
            if (text.Length > 0)
            {
                ClipboardHelper.SetText(text);
            }
            Close();
        }

        private void OnPasteClicked(object sender, EventArgs e)
        {
            string text = CurrentText;
            if (text.Length == 0)
            {
                return;
            }

            ClipboardHelper.SetText(text);

            IntPtr target = _targetWindow;
            if (target == IntPtr.Zero)
            {
                _statusLabel.Text = "已复制到剪贴板，请切换窗口后按 Ctrl+V";
                return;
            }

            // 先让出前台，再把焦点还给说话前的那个窗口。
            Hide();
            Application.DoEvents();
            if (!NativeMethods.SetForegroundWindow(target))
            {
                Show();
                _statusLabel.Text =
                    "已复制到剪贴板，但无法切回原窗口，请手动 Ctrl+V";
                return;
            }

            Thread.Sleep(80);
            ClipboardHelper.SendPaste();
            Close();
        }

        private void OnTranslateClicked(object sender, EventArgs e)
        {
            string text = CurrentText;
            if (text.Length == 0)
            {
                return;
            }

            AppConfig config = _configProvider == null
                ? null : _configProvider();
            if (config == null)
            {
                return;
            }

            Close();
            TranslationForm form = new TranslationForm(
                config,
                text,
                Cursor.Position,
                _openSettings);
            form.Show();
            form.Activate();
        }

        private void OnAgainClicked(object sender, EventArgs e)
        {
            EventHandler handler = RecordAgainRequested;
            Close();
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Enter &&
                (e.Control || e.Shift))
            {
                OnCopyAndCloseClicked(sender, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
    }
}
