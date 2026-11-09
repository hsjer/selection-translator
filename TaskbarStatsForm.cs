using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SelectionTranslator
{
    /// <summary>
    /// 贴在任务栏托盘区左侧的实时状态条（上传/下载、CPU、内存）。
    /// 用 WS_EX_LAYERED + UpdateLayeredWindow 做每像素透明，只画文字、
    /// 不铺底色，所以浅色/深色/亚克力任务栏上都不会出现一块色块。
    /// </summary>
    internal sealed class TaskbarStatsForm : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;
        private const int ULW_ALPHA = 2;
        private const int ABM_GETTASKBARPOS = 0x00000005;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(
            string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(
            IntPtr parent, IntPtr after,
            string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(
            IntPtr hWnd, out RECT rect);

        [DllImport("shell32.dll")]
        private static extern IntPtr SHAppBarMessage(
            int message, ref APPBARDATA data);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            int crKey,
            ref BLENDFUNCTION blend,
            int flags);

        private readonly Timer _timer;
        private readonly float _scale;
        private long _lastReceived;
        private long _lastSent;
        private ulong _lastIdle;
        private ulong _lastTotal;
        private bool _hasBaseline;

        public TaskbarStatsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            AutoScaleMode = AutoScaleMode.None;

            float scale = 1f;
            try
            {
                using (Graphics graphics = CreateGraphics())
                {
                    scale = graphics.DpiX / 96f;
                }
            }
            catch
            {
                scale = 1f;
            }
            _scale = Math.Max(1f, Math.Min(2.5f, scale));

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_LAYERED |
                    WS_EX_TRANSPARENT |
                    WS_EX_TOOLWINDOW |
                    WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
            RefreshNow();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                if (!_timer.Enabled)
                {
                    _timer.Start();
                }
                RefreshNow();
            }
            else
            {
                _timer.Stop();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }

        public void RefreshNow()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            long received;
            long sent;
            TaskbarStats.SampleTraffic(out received, out sent);
            ulong idle;
            ulong total;
            TaskbarStats.SampleCpu(out idle, out total);
            int memory = TaskbarStats.MemoryLoad();

            double upload = 0;
            double download = 0;
            int cpu = 0;
            if (_hasBaseline)
            {
                double seconds = _timer.Interval / 1000.0;
                download = Math.Max(0, received - _lastReceived) /
                    seconds;
                upload = Math.Max(0, sent - _lastSent) / seconds;
                cpu = TaskbarStats.ComputeCpuPercent(
                    idle - _lastIdle, total - _lastTotal);
            }
            _lastReceived = received;
            _lastSent = sent;
            _lastIdle = idle;
            _lastTotal = total;
            _hasBaseline = true;

            string up = TaskbarStats.FormatSpeed(upload);
            string down = TaskbarStats.FormatSpeed(download);
            using (Bitmap bitmap = RenderBitmap(
                up, down, cpu, memory, _scale))
            {
                DrawLayered(bitmap);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            RefreshNow();
        }

        /// <summary>
        /// 生成只有文字、背景全透明的位图。预览和自检也走这里，
        /// 保证屏幕效果与导出图一致。
        /// </summary>
        public static Bitmap RenderBitmap(
            string upload,
            string download,
            int cpuPercent,
            int memoryPercent,
            float scale)
        {
            return RenderBitmap(
                upload, download, cpuPercent, memoryPercent,
                scale, TextColor());
        }

        public static Bitmap RenderBitmap(
            string upload,
            string download,
            int cpuPercent,
            int memoryPercent,
            float scale,
            Color textColor)
        {
            if (scale < 1f)
            {
                scale = 1f;
            }
            if (scale > 2.5f)
            {
                scale = 2.5f;
            }

            string first;
            string second;
            TaskbarStats.FormatLines(
                upload, download, cpuPercent, memoryPercent,
                out first, out second);

            using (Font font = new Font(
                "Microsoft YaHei UI",
                12f * scale,
                FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(textColor))
            using (Bitmap probe = new Bitmap(1, 1,
                PixelFormat.Format32bppArgb))
            using (Graphics measure = Graphics.FromImage(probe))
            {
                StringFormat format = StringFormat.GenericTypographic;
                SizeF firstSize = measure.MeasureString(
                    first, font, 4096, format);
                SizeF secondSize = measure.MeasureString(
                    second, font, 4096, format);
                int width = (int)Math.Ceiling(
                    Math.Max(firstSize.Width, secondSize.Width)) +
                    (int)Math.Round(8 * scale);
                int lineHeight = (int)Math.Ceiling(14 * scale);
                int height = lineHeight * 2 +
                    (int)Math.Round(4 * scale);
                if (width < 1)
                {
                    width = 1;
                }
                if (height < 1)
                {
                    height = 1;
                }

                Bitmap bitmap = new Bitmap(
                    width, height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TextRenderingHint =
                        TextRenderingHint.AntiAliasGridFit;
                    graphics.DrawString(
                        first, font, brush,
                        2 * scale, 0, format);
                    graphics.DrawString(
                        second, font, brush,
                        2 * scale, lineHeight, format);
                }
                return bitmap;
            }
        }

        /// <summary>任务栏是浅色时用深字，深色时用浅字。</summary>
        public static Color TextColor()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion" +
                    "\\Themes\\Personalize"))
                {
                    object value = key == null
                        ? null : key.GetValue("SystemUsesLightTheme");
                    bool light = value == null ||
                        Convert.ToInt32(value) != 0;
                    return light
                        ? Color.FromArgb(255, 26, 28, 32)
                        : Color.FromArgb(255, 240, 242, 246);
                }
            }
            catch
            {
                return Color.FromArgb(255, 26, 28, 32);
            }
        }

        private void DrawLayered(Bitmap bitmap)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                bitmapHandle = bitmap.GetHbitmap(
                    Color.FromArgb(0));
                oldBitmap = SelectObject(
                    memoryDc, bitmapHandle);

                Point position = ComputePosition(bitmap.Size);
                POINT destination = new POINT();
                destination.X = position.X;
                destination.Y = position.Y;
                POINT source = new POINT();
                SIZE size = new SIZE();
                size.cx = bitmap.Width;
                size.cy = bitmap.Height;
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AC_SRC_ALPHA;

                UpdateLayeredWindow(
                    Handle,
                    IntPtr.Zero,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                }
                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>每次刷新都重算位置：托盘图标增减、任务栏换边、
        /// 分辨率/DPI 变化都会自动跟上，不需要重启。</summary>
        public Point ComputePosition(Size content)
        {
            Rectangle taskbar = GetTaskbarRect();
            Rectangle notify = GetTrayNotifyRect();
            int margin = (int)Math.Round(8 * _scale);
            return TaskbarStats.ComputePosition(
                taskbar, notify, content, margin);
        }

        public static Rectangle GetTaskbarRect()
        {
            APPBARDATA data = new APPBARDATA();
            data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            IntPtr result = SHAppBarMessage(
                ABM_GETTASKBARPOS, ref data);
            if (result != IntPtr.Zero &&
                data.rc.Right > data.rc.Left &&
                data.rc.Bottom > data.rc.Top)
            {
                return Rectangle.FromLTRB(
                    data.rc.Left, data.rc.Top,
                    data.rc.Right, data.rc.Bottom);
            }

            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            RECT rect;
            if (tray != IntPtr.Zero &&
                GetWindowRect(tray, out rect))
            {
                return Rectangle.FromLTRB(
                    rect.Left, rect.Top,
                    rect.Right, rect.Bottom);
            }

            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            return new Rectangle(
                bounds.Left, working.Bottom,
                bounds.Width,
                Math.Max(1, bounds.Bottom - working.Bottom));
        }

        public static Rectangle GetTrayNotifyRect()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero)
            {
                return Rectangle.Empty;
            }
            IntPtr notify = FindWindowEx(
                tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notify == IntPtr.Zero)
            {
                return Rectangle.Empty;
            }
            RECT rect;
            if (!GetWindowRect(notify, out rect))
            {
                return Rectangle.Empty;
            }
            return Rectangle.FromLTRB(
                rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }
}
