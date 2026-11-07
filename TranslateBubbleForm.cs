using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class TranslateBubbleForm : Form
    {
        private readonly string _text;
        private readonly Action<string, Point> _onClick;
        private readonly Timer _hideTimer;
        private readonly ToolTip _toolTip;

        public string SelectedText
        {
            get { return _text; }
        }

        public TranslateBubbleForm(
            string text,
            Point location,
            Action<string, Point> onClick)
        {
            _text = text;
            _onClick = onClick;

            Text = "翻译";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(38, 38);
            BackColor = Color.FromArgb(45, 110, 220);
            Opacity = 0.96;
            Cursor = Cursors.Hand;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                Region = new Region(path);
            }

            Label label = new Label();
            label.Text = "译";
            label.ForeColor = Color.White;
            label.Font = new Font(
                "Microsoft YaHei UI",
                12F,
                FontStyle.Bold,
                GraphicsUnit.Point);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Cursor = Cursors.Hand;
            label.Click += OnBubbleClick;
            Controls.Add(label);

            Click += OnBubbleClick;

            _toolTip = new ToolTip();
            _toolTip.SetToolTip(label, "翻译选中的文本");
            _toolTip.SetToolTip(this, "翻译选中的文本");

            _hideTimer = new Timer();
            _hideTimer.Interval = 4500;
            _hideTimer.Tick += OnHideTimerTick;

            PlaceNear(location);
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
                parameters.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;
                parameters.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                parameters.ExStyle |= NativeMethods.WS_EX_TOPMOST;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _hideTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _hideTimer.Stop();
            _hideTimer.Dispose();
            _toolTip.Dispose();
            base.OnFormClosed(e);
        }

        private void PlaceNear(Point cursor)
        {
            Rectangle workingArea = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X + 14;
            int y = cursor.Y - Height - 12;

            if (y < workingArea.Top)
            {
                y = cursor.Y + 18;
            }
            if (x + Width > workingArea.Right)
            {
                x = cursor.X - Width - 14;
            }
            if (x < workingArea.Left)
            {
                x = workingArea.Left;
            }
            if (y + Height > workingArea.Bottom)
            {
                y = workingArea.Bottom - Height;
            }

            Location = new Point(x, y);
        }

        private void OnHideTimerTick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            Close();
        }

        private void OnBubbleClick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            Hide();
            if (_onClick != null)
            {
                _onClick(_text, Location);
            }
            Close();
        }
    }
}
