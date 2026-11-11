using System;
using System.Drawing;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 界面外观（配色总开关）。以前所有窗口都是写死的深色，这里给出
    /// 一个统一的入口：选深色 / 浅色 / 任意自定义底色 + 强调色，改完
    /// 立刻套到**所有已经打开的窗口**（桌面整理、剪贴板历史、文件秒搜、
    /// 软件下载、图片查看器…），下次启动沿用。
    /// </summary>
    internal sealed class AppearanceForm : Form
    {
        private const int TitleHeight = 38;

        private static AppearanceForm _instance;

        private readonly Panel _body;
        private readonly GlyphButton _closeGlyph;
        private bool _dragging;
        private Point _dragStart;
        private Point _dragOrigin;

        private readonly Panel _baseSwatch;
        private readonly Panel _accentSwatch;
        private readonly Label _baseValue;
        private readonly Label _accentValue;
        private readonly CheckBox _residentBox;
        private readonly Label _tip;
        private readonly FlatButton _darkButton;
        private readonly FlatButton _lightButton;
        private readonly FlatButton _basePickButton;
        private readonly FlatButton _accentPickButton;
        private readonly FlatButton _resetButton;
        private readonly FlatButton _closeButton;

        public AppearanceForm()
        {
            Text = "界面外观 - " + AppInfo.Name;
            // 自绘标题栏：系统浅蓝标题栏跟深色内容放一起太割裂。
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = DarkUi.FontBody;
            BackColor = DarkUi.Window;
            ForeColor = DarkUi.Text;
            ClientSize = new Size(468, 410 + TitleHeight);

            _body = new Panel();
            _body.Location = new Point(0, TitleHeight);
            _body.Size = new Size(468, 410);
            _body.BackColor = DarkUi.Surface;
            Controls.Add(_body);

            _closeGlyph = new GlyphButton(
                GlyphKind.Close, DarkUi.Window, 34, 26);
            _closeGlyph.TabStop = false;
            _closeGlyph.DangerHover = true;
            _closeGlyph.Location = new Point(468 - 42, 6);
            _closeGlyph.Click += delegate { Close(); };
            Controls.Add(_closeGlyph);

            Label title = new Label();
            title.Text = "界面配色";
            title.Font = DarkUi.FontTitle;
            title.ForeColor = DarkUi.Text;
            title.AutoSize = true;
            title.Location = new Point(18, 16);
            _body.Controls.Add(title);

            Label intro = new Label();
            intro.Text = "一个开关管所有窗口：底色亮就自动转浅色文字方案，\n" +
                "底色暗就走深色方案，改完立即生效。";
            intro.ForeColor = DarkUi.TextDim;
            intro.Font = DarkUi.FontSmall;
            intro.AutoSize = true;
            intro.Location = new Point(20, 46);
            _body.Controls.Add(intro);

            // ---- 底色 ----
            Label baseLabel = new Label();
            baseLabel.Text = "界面底色";
            baseLabel.ForeColor = DarkUi.TextDim;
            baseLabel.Font = DarkUi.FontSmall;
            baseLabel.AutoSize = true;
            baseLabel.Location = new Point(20, 96);
            _body.Controls.Add(baseLabel);

            _baseSwatch = new Panel();
            // 自绘描边：浅色主题下是白底白块，系统边框几乎看不见。
            _baseSwatch.BorderStyle = BorderStyle.None;
            _baseSwatch.Paint += OnSwatchPaint;
            _baseSwatch.Size = new Size(58, 26);
            _baseSwatch.Location = new Point(104, 91);
            _body.Controls.Add(_baseSwatch);

            _baseValue = new Label();
            _baseValue.ForeColor = DarkUi.TextFaint;
            _baseValue.Font = DarkUi.FontTiny;
            _baseValue.AutoSize = true;
            _baseValue.Location = new Point(170, 98);
            _body.Controls.Add(_baseValue);

            _basePickButton = new FlatButton();
            _basePickButton.Text = "选择颜色…";
            _basePickButton.Size = new Size(88, 28);
            _basePickButton.SurfaceColor = DarkUi.Surface;
            _basePickButton.Radius = 7;
            _basePickButton.Click += delegate { PickBase(); };
            _basePickButton.Location = new Point(360, 90);
            _body.Controls.Add(_basePickButton);

            // ---- 强调色 ----
            Label accentLabel = new Label();
            accentLabel.Text = "强调色";
            accentLabel.ForeColor = DarkUi.TextDim;
            accentLabel.Font = DarkUi.FontSmall;
            accentLabel.AutoSize = true;
            accentLabel.Location = new Point(20, 138);
            _body.Controls.Add(accentLabel);

            _accentSwatch = new Panel();
            _accentSwatch.BorderStyle = BorderStyle.None;
            _accentSwatch.Paint += OnSwatchPaint;
            _accentSwatch.Size = new Size(58, 26);
            _accentSwatch.Location = new Point(104, 133);
            _body.Controls.Add(_accentSwatch);

            _accentValue = new Label();
            _accentValue.ForeColor = DarkUi.TextFaint;
            _accentValue.Font = DarkUi.FontTiny;
            _accentValue.AutoSize = true;
            _accentValue.Location = new Point(170, 140);
            _body.Controls.Add(_accentValue);

            _accentPickButton = new FlatButton();
            _accentPickButton.Text = "选择颜色…";
            _accentPickButton.Size = new Size(88, 28);
            _accentPickButton.SurfaceColor = DarkUi.Surface;
            _accentPickButton.Radius = 7;
            _accentPickButton.Click += delegate { PickAccent(); };
            _accentPickButton.Location = new Point(360, 132);
            _body.Controls.Add(_accentPickButton);

            // ---- 预设 ----
            Label presetLabel = new Label();
            presetLabel.Text = "快速预设";
            presetLabel.ForeColor = DarkUi.TextDim;
            presetLabel.Font = DarkUi.FontSmall;
            presetLabel.AutoSize = true;
            presetLabel.Location = new Point(20, 180);
            _body.Controls.Add(presetLabel);

            _darkButton = MakeButton("深色", 104, 174, 76);
            _darkButton.Click += delegate
            {
                Apply(UiTheme.DefaultBase, UiTheme.AccentColor);
            };
            _body.Controls.Add(_darkButton);

            _lightButton = MakeButton("浅色", 188, 174, 76);
            _lightButton.Click += delegate
            {
                Apply(Color.FromArgb(246, 247, 250), UiTheme.AccentColor);
            };
            _body.Controls.Add(_lightButton);

            FlatButton themeWarm = MakeButton("暖纸", 272, 174, 76);
            themeWarm.Click += delegate
            {
                Apply(Color.FromArgb(43, 37, 32), Color.FromArgb(224, 149, 74));
            };
            _body.Controls.Add(themeWarm);

            FlatButton themeGreen = MakeButton("墨绿", 356, 174, 76);
            themeGreen.Click += delegate
            {
                Apply(Color.FromArgb(20, 32, 29), Color.FromArgb(72, 190, 150));
            };
            _body.Controls.Add(themeGreen);

            // ---- 预览 ----
            Panel preview = new Panel();
            preview.BackColor = DarkUi.Window;
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.Location = new Point(20, 216);
            preview.Size = new Size(424, 76);
            _body.Controls.Add(preview);

            Label previewTitle = new Label();
            previewTitle.Text = "示例文字 · 正文";
            previewTitle.ForeColor = DarkUi.Text;
            previewTitle.Font = DarkUi.FontStrong;
            previewTitle.AutoSize = true;
            previewTitle.Location = new Point(12, 10);
            preview.Controls.Add(previewTitle);

            Label previewDim = new Label();
            previewDim.Text = "次要说明文字 128 KB · 09:24";
            previewDim.ForeColor = DarkUi.TextDim;
            previewDim.Font = DarkUi.FontSmall;
            previewDim.AutoSize = true;
            previewDim.Location = new Point(12, 34);
            preview.Controls.Add(previewDim);

            FlatButton previewButton = new FlatButton();
            previewButton.Text = "强调按钮";
            previewButton.Primary = true;
            previewButton.Size = new Size(84, 26);
            previewButton.Location = new Point(324, 24);
            previewButton.SurfaceColor = DarkUi.Window;
            previewButton.Radius = 7;
            preview.Controls.Add(previewButton);

            // ---- 常驻桌面 ----
            _residentBox = new CheckBox();
            _residentBox.Text = "桌面助手常驻桌面（收起后缩成一条标题栏）";
            _residentBox.ForeColor = DarkUi.Text;
            _residentBox.Font = DarkUi.FontSmall;
            _residentBox.AutoSize = true;
            _residentBox.Location = new Point(20, 304);
            _residentBox.CheckedChanged += delegate
            {
                DesktopManagerForm.ApplyResidentSetting(_residentBox.Checked);
            };
            _body.Controls.Add(_residentBox);

            _tip = new Label();
            _tip.Text = "「桌面整理」面板上点标题栏或右上角箭头都能收起 / 展开。";
            _tip.ForeColor = DarkUi.TextFaint;
            _tip.Font = DarkUi.FontTiny;
            _tip.AutoSize = true;
            _tip.Location = new Point(20, 330);
            _body.Controls.Add(_tip);

            _resetButton = MakeButton("恢复默认", 20, 372, 88);
            _resetButton.Click += delegate
            {
                Apply(UiTheme.DefaultBase, UiTheme.DefaultAccent);
            };
            _body.Controls.Add(_resetButton);

            _closeButton = MakeButton("关闭", 380, 372, 64);
            _closeButton.Primary = true;
            _closeButton.Click += delegate { Close(); };
            _body.Controls.Add(_closeButton);

            LoadFromConfig();
            ApplyTitleBarTheme();
            UiTheme.Changed += OnThemeChanged;
            FormClosed += delegate { UiTheme.Changed -= OnThemeChanged; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (SolidBrush brush = new SolidBrush(DarkUi.Window))
            {
                e.Graphics.FillRectangle(brush, 0, 0, Width, TitleHeight);
            }
            DarkUi.Text2(e.Graphics, "界面外观", DarkUi.FontStrong,
                DarkUi.Text, new Rectangle(18, 0, Width - 70, TitleHeight),
                DarkUi.LeftMiddle);
            using (Pen pen = new Pen(DarkUi.BorderSoft))
            {
                e.Graphics.DrawLine(pen, 0, TitleHeight - 1,
                    Width, TitleHeight - 1);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < TitleHeight)
            {
                _dragging = true;
                _dragStart = Cursor.Position;
                _dragOrigin = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Point now = Cursor.Position;
                Location = new Point(
                    _dragOrigin.X + now.X - _dragStart.X,
                    _dragOrigin.Y + now.Y - _dragStart.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            // 主题可能被别处改（比如托盘里切换后回到这个窗口），
            // 色块、色值和标题栏都要跟上。
            SyncSwatches();
            ApplyTitleBarTheme();
            Invalidate(true);
        }

        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeOld = 19;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hWnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBarTheme();
        }

        /// <summary>深色主题下把系统标题栏也刷成深色，不然一条亮白标题很割裂。</summary>
        private void ApplyTitleBarTheme()
        {
            if (!IsHandleCreated)
            {
                return;
            }
            try
            {
                int dark = UiTheme.IsLight ? 0 : 1;
                // Win11 用 20，Win10 1903+ 用 19，失败的返回值忽略即可。
                if (DwmSetWindowAttribute(
                    Handle, DwmUseImmersiveDarkMode, ref dark,
                    sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(
                        Handle, DwmUseImmersiveDarkModeOld, ref dark,
                        sizeof(int));
                }
            }
            catch
            {
            }
        }

        private FlatButton MakeButton(string text, int x, int y, int width)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Size = new Size(width, 28);
            button.Location = new Point(x, y);
            button.SurfaceColor = DarkUi.Surface;
            button.Radius = 7;
            return button;
        }

        public static AppearanceForm ShowWindow()
        {
            if (_instance != null && !_instance.IsDisposed)
            {
                _instance.Activate();
                return _instance;
            }
            AppearanceForm form = new AppearanceForm();
            _instance = form;
            form.FormClosed += delegate { _instance = null; };
            form.Show();
            form.Activate();
            return form;
        }

        private void LoadFromConfig()
        {
            AppConfig config = null;
            try
            {
                config = ConfigStore.Load();
            }
            catch (Exception ex)
            {
                Log.Error("读取界面配色失败", ex);
            }
            Color baseColor = config == null
                ? UiTheme.DefaultBase
                : UiTheme.ParseColor(config.ThemeBaseColor, UiTheme.DefaultBase);
            Color accent = config == null
                ? UiTheme.DefaultAccent
                : UiTheme.ParseColor(
                    config.ThemeAccentColor, UiTheme.DefaultAccent);
            UiTheme.Configure(baseColor, accent, false);
            SyncSwatches();
            _residentBox.Checked = config != null &&
                config.DesktopPanelResident;
        }

        /// <summary>色块自带一圈描边，浅色主题下白底白块才看得见边界。</summary>
        private void OnSwatchPaint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            if (panel == null || panel.ClientSize.Width < 2)
            {
                return;
            }
            using (Pen pen = new Pen(DarkUi.Border))
            {
                e.Graphics.DrawRectangle(
                    pen, 0, 0,
                    panel.ClientSize.Width - 1,
                    panel.ClientSize.Height - 1);
            }
        }

        private void SyncSwatches()
        {
            _baseSwatch.BackColor = UiTheme.BaseColor;
            _accentSwatch.BackColor = UiTheme.AccentColor;
            _baseValue.Text = UiTheme.ToHex(UiTheme.BaseColor);
            _accentValue.Text = UiTheme.ToHex(UiTheme.AccentColor);
            _darkButton.Checked =
                UiTheme.IsLight == false &&
                UiTheme.BaseColor.ToArgb() == UiTheme.DefaultBase.ToArgb();
            _lightButton.Checked = UiTheme.IsLight;
        }

        private void PickBase()
        {
            Color picked = PickColor(UiTheme.BaseColor);
            if (picked.IsEmpty)
            {
                return;
            }
            Apply(picked, UiTheme.AccentColor);
        }

        private void PickAccent()
        {
            Color picked = PickColor(UiTheme.AccentColor);
            if (picked.IsEmpty)
            {
                return;
            }
            Apply(UiTheme.BaseColor, picked);
        }

        private Color PickColor(Color current)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                dialog.Color = current;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return Color.Empty;
                }
                return Color.FromArgb(
                    dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }
        }

        /// <summary>改配色：立即套到所有窗口 + 落配置。</summary>
        internal void Apply(Color baseColor, Color accent)
        {
            UiTheme.Configure(baseColor, accent, true);
            SyncSwatches();
            Invalidate(true);
            try
            {
                AppConfig config = ConfigStore.Load();
                config.ThemeBaseColor = UiTheme.IsDefault(
                    baseColor, accent) ? "" : UiTheme.ToHex(baseColor);
                config.ThemeAccentColor = accent.ToArgb() ==
                    UiTheme.DefaultAccent.ToArgb() ? "" : UiTheme.ToHex(accent);
                ConfigStore.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error("保存界面配色失败", ex);
            }
        }
    }
}
