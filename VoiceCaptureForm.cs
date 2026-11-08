using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 录音时贴在屏幕底部的小浮条。
    ///
    /// 它必须「抢焦点之外的一切」：不激活、不进任务栏、永远置顶，
    /// 这样用户按住快捷键说话时，光标还留在原来的输入框里，
    /// 识别完成后可以直接把文字粘回去。
    /// </summary>
    internal sealed class VoiceCaptureForm : Form
    {
        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;

        private enum BarState
        {
            Recording,
            Recognizing,
            Done,
            Failed
        }

        private readonly string _hotkeyText;
        private readonly Timer _animationTimer;

        private BarState _state;
        private TimeSpan _duration;
        private float _level;
        private int _phase;
        private string _statusText;
        private bool _closeButtonHot;

        public event EventHandler StopRequested;
        public event EventHandler CancelRequested;

        public VoiceCaptureForm(string hotkeyText)
        {
            _hotkeyText = string.IsNullOrEmpty(hotkeyText)
                ? "快捷键" : hotkeyText;
            _state = BarState.Recording;
            _statusText = "";

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(348, 96);
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);

            _animationTimer = new Timer();
            _animationTimer.Interval = 60;
            _animationTimer.Tick += OnAnimationTick;

            LocateBottomCenter();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |=
                    NativeMethods.WS_EX_NOACTIVATE |
                    NativeMethods.WS_EX_TOOLWINDOW |
                    NativeMethods.WS_EX_TOPMOST;
                return parameters;
            }
        }

        private void LocateBottomCenter()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle area = screen.WorkingArea;
            int x = area.Left + (area.Width - Width) / 2;
            int y = area.Bottom - Height - 72;
            Location = new Point(x, y);
        }

        // ------------------------------------------------------------------
        // 外部驱动
        // ------------------------------------------------------------------

        public void UpdateRecording(TimeSpan duration, float level)
        {
            bool needsRedraw = _state != BarState.Recording;
            _state = BarState.Recording;
            _duration = duration;
            _level = level;
            if (needsRedraw)
            {
                Invalidate();
            }
        }

        public void ShowRecognizing(string note)
        {
            _state = BarState.Recognizing;
            _statusText = note ?? "正在识别…";
            Invalidate();
            if (!_animationTimer.Enabled)
            {
                _animationTimer.Start();
            }
        }

        /// <summary>文字已经直接写进输入框，闪一下就收起。</summary>
        public void ShowDone(string message)
        {
            _state = BarState.Done;
            _statusText = message ?? "已输入";
            Invalidate();
        }

        public void ShowFailed(string message)
        {
            _state = BarState.Failed;
            _statusText = message ?? "识别失败";
            Invalidate();
        }

        public void StartAnimation()
        {
            if (!_animationTimer.Enabled)
            {
                _animationTimer.Start();
            }
        }

        public void StopAnimation()
        {
            _animationTimer.Stop();
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            _phase = (_phase + 1) % 360;
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartAnimation();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _animationTimer.Stop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animationTimer.Stop();
                _animationTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // 鼠标：整块点击 = 结束录音，右上角 × = 取消
        // ------------------------------------------------------------------

        protected override void WndProc(ref Message message)
        {
            // 不激活窗口，但仍要能收到点击。
            if (message.Msg == WmMouseActivate)
            {
                message.Result = new IntPtr(MaNoActivate);
                return;
            }
            base.WndProc(ref message);
        }

        private Rectangle CloseButtonBounds
        {
            get { return new Rectangle(Width - 34, 8, 24, 24); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = CloseButtonBounds.Contains(e.Location);
            if (hot != _closeButtonHot)
            {
                _closeButtonHot = hot;
                Cursor = hot ? Cursors.Hand : Cursors.Default;
                Invalidate(CloseButtonBounds);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_closeButtonHot)
            {
                _closeButtonHot = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // 识别中不响应任何点击：这时候再取消已经来不及了，
            // 请求还在路上，点了也只会让人以为没反应。
            if (_state != BarState.Recording)
            {
                return;
            }

            if (CloseButtonBounds.Contains(e.Location))
            {
                EventHandler cancel = CancelRequested;
                if (cancel != null)
                {
                    cancel(this, EventArgs.Empty);
                }
                return;
            }

            EventHandler stop = StopRequested;
            if (stop != null)
            {
                stop(this, EventArgs.Empty);
            }
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint =
                System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle bounds = new Rectangle(0, 0, Width, Height);
            bounds.Width -= 1;
            bounds.Height -= 1;

            Color panel = Color.FromArgb(30, 34, 42);
            Color border = Color.FromArgb(64, 72, 88);

            using (GraphicsPath path = RoundedRect(bounds, 12))
            {
                using (SolidBrush brush = new SolidBrush(panel))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(border, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            DrawStatusDot(g);
            DrawTitle(g);
            DrawLevels(g);
            DrawHint(g);
            DrawCloseButton(g);
        }

        private void DrawStatusDot(Graphics g)
        {
            float cx = 26f;
            float cy = 32f;

            Color dotColor;
            float radius = 7f;

            if (_state == BarState.Recording)
            {
                dotColor = Color.FromArgb(235, 74, 74);
                // 呼吸脉动
                double pulse = (Math.Sin(_phase * Math.PI / 180.0) + 1) / 2;
                radius = 6f + (float)(pulse * 2.2);
                int alpha = (int)(60 + pulse * 90);
                using (SolidBrush halo = new SolidBrush(
                    Color.FromArgb(alpha, 235, 74, 74)))
                {
                    g.FillEllipse(halo, cx - 13f, cy - 13f, 26f, 26f);
                }
            }
            else if (_state == BarState.Recognizing)
            {
                dotColor = Color.FromArgb(64, 156, 255);
            }
            else if (_state == BarState.Done)
            {
                dotColor = Color.FromArgb(66, 200, 130);
            }
            else
            {
                dotColor = Color.FromArgb(240, 178, 60);
            }

            using (SolidBrush brush = new SolidBrush(dotColor))
            {
                g.FillEllipse(
                    brush, cx - radius, cy - radius,
                    radius * 2f, radius * 2f);
            }
        }

        private void DrawTitle(Graphics g)
        {
            string title;
            Color color;
            if (_state == BarState.Recording)
            {
                title = "正在听… " + FormatDuration(_duration);
                color = Color.FromArgb(240, 243, 248);
            }
            else if (_state == BarState.Recognizing)
            {
                int dots = (_phase / 12) % 4;
                title = "识别中" + new string('.', dots);
                color = Color.FromArgb(160, 205, 255);
            }
            else if (_state == BarState.Done)
            {
                title = "✓ " + _statusText;
                color = Color.FromArgb(150, 235, 185);
            }
            else
            {
                title = _statusText;
                color = Color.FromArgb(255, 200, 120);
            }

            using (Font font = new Font(
                "Microsoft YaHei UI", 11F, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.DrawString(title, font, brush, 46f, 20f);
            }
        }

        private void DrawLevels(Graphics g)
        {
            const int barCount = 28;
            float left = 46f;
            float width = 250f;
            float top = 48f;
            float height = 20f;

            int lit;
            if (_state == BarState.Done)
            {
                lit = barCount;
            }
            else if (_state == BarState.Recording)
            {
                float scaled = _level * 3.2f;
                if (scaled > 1f)
                {
                    scaled = 1f;
                }
                lit = (int)Math.Round(scaled * barCount);
            }
            else
            {
                // 识别中给一个来回扫动的假进度条，表示还在等结果。
                lit = (int)(Math.Abs(
                    Math.Sin(_phase * Math.PI / 180.0)) * barCount);
            }

            Color litColor = _state == BarState.Done
                ? Color.FromArgb(80, 200, 140)
                : Color.FromArgb(110, 170, 255);

            float step = width / barCount;
            float barWidth = step * 0.55f;

            for (int i = 0; i < barCount; i++)
            {
                bool on = i < lit;
                Color color = on
                    ? litColor
                    : Color.FromArgb(58, 64, 78);
                float barHeight = height;
                if (i % 2 == 1)
                {
                    barHeight = height * 0.7f;
                }
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillRectangle(
                        brush,
                        left + i * step,
                        top + (height - barHeight) / 2f,
                        barWidth,
                        barHeight);
                }
            }
        }

        private void DrawHint(Graphics g)
        {
            string hint;
            if (_state == BarState.Recording)
            {
                hint = "松开 " + _hotkeyText + " 结束 · 点这里也可以";
            }
            else if (_state == BarState.Recognizing)
            {
                hint = "正在上传到腾讯云语音识别…";
            }
            else if (_state == BarState.Done)
            {
                hint = "文字已直接出现在光标处";
            }
            else
            {
                hint = "稍后自动关闭";
            }

            using (Font font = new Font("Microsoft YaHei UI", 8.5F))
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(150, 160, 176)))
            {
                g.DrawString(hint, font, brush, 46f, 70f);
            }
        }

        private void DrawCloseButton(Graphics g)
        {
            Rectangle bounds = CloseButtonBounds;
            if (_closeButtonHot)
            {
                using (SolidBrush brush = new SolidBrush(
                    Color.FromArgb(70, 78, 94)))
                {
                    g.FillEllipse(brush, bounds);
                }
            }

            using (Pen pen = new Pen(Color.FromArgb(180, 190, 205), 1.6f))
            {
                int pad = 8;
                g.DrawLine(pen,
                    bounds.Left + pad, bounds.Top + pad,
                    bounds.Right - pad - 1, bounds.Bottom - pad - 1);
                g.DrawLine(pen,
                    bounds.Right - pad - 1, bounds.Top + pad,
                    bounds.Left + pad, bounds.Bottom - pad - 1);
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            int totalSeconds = (int)duration.TotalSeconds;
            if (totalSeconds < 60)
            {
                return totalSeconds.ToString("0.0") + " 秒";
            }
            return totalSeconds / 60 + ":" +
                (totalSeconds % 60).ToString("00");
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top,
                diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter,
                diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter,
                diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
