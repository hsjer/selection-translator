using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        // 状态条是鼠标穿透窗口，收不到 MouseEnter，只能轮询光标位置。
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        private readonly Timer _timer;
        private readonly Timer _hoverTimer;
        private readonly float _scale;
        // 定位用的固定参考宽度：按「最长速度文本」渲染一次得到。
        // 位图本身会随内容变窄，但左边缘始终按它计算，不会左右抖。
        private readonly int _referenceWidth;
        private readonly TrafficDayTracker _trafficDay;
        private readonly GpuSampler _gpuSampler;
        private long _lastReceived;
        private long _lastSent;
        private ulong _lastIdle;
        private ulong _lastTotal;
        private bool _hasBaseline;

        private double _lastUpload;
        private double _lastDownload;
        private int _lastCpu;
        private int[] _lastGpuLoads = new int[0];
        private DateTime _lastTrafficSave = DateTime.MinValue;
        private DateTime _lastDetailPaint = DateTime.MinValue;
        private Rectangle _barBounds = Rectangle.Empty;
        private Rectangle _detailBounds = Rectangle.Empty;
        // 上一次把状态条顶回 topmost 层的时间，用于节流（见 TryKeepTopmost）。
        private DateTime _lastTopmostReclaim = DateTime.MinValue;
        // 避让后的落点锚：一旦为别人让开就钉在那儿，避免被对方的宽度
        // 抖动拽着左右蹭（见 ComputePosition）。Empty 表示没在避让状态。
        private Point _avoidAnchor = Point.Empty;
        private TaskbarStatsDetailForm _detailForm;

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

            // 用最长的一组数据渲染一帧，拿它当定位参考宽度。
            // 这里只需要尺寸，不需要真正显示。
            try
            {
                using (Bitmap reference = RenderBitmap(
                    "999.99 MB/s", "999.99 MB/s",
                    100, 100, _scale, Color.Transparent))
                {
                    _referenceWidth = reference.Width;
                }
            }
            catch
            {
                _referenceWidth = (int)Math.Round(180 * _scale);
            }

            _trafficDay = TrafficDayTracker.Load(
                Path.Combine(
                    ConfigStore.DataDirectory, "traffic.json"));
            _gpuSampler = new GpuSampler();

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTick;

            _hoverTimer = new Timer();
            _hoverTimer.Interval = 150;
            _hoverTimer.Tick += OnHoverTick;
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
            _hoverTimer.Start();
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
                if (!_hoverTimer.Enabled)
                {
                    _hoverTimer.Start();
                }
                RefreshNow();
            }
            else
            {
                _timer.Stop();
                _hoverTimer.Stop();
                HideDetail();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_hoverTimer != null)
                {
                    _hoverTimer.Stop();
                    _hoverTimer.Dispose();
                }
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }
                if (_detailForm != null)
                {
                    if (!_detailForm.IsDisposed)
                    {
                        _detailForm.Dispose();
                    }
                    _detailForm = null;
                }
                try
                {
                    _trafficDay.Save();
                }
                catch
                {
                }
            }
            base.Dispose(disposing);
        }

        public void RefreshNow()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            Dictionary<string, long> receivedByNic;
            Dictionary<string, long> sentByNic;
            long received;
            long sent;
            TaskbarStats.SampleTrafficByNic(
                out receivedByNic, out sentByNic,
                out received, out sent);
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
                if (seconds <= 0)
                {
                    seconds = 1.0;
                }
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
            _lastUpload = upload;
            _lastDownload = download;
            _lastCpu = cpu;

            // 当日累计：按网卡记底数，避免开机以来的累计被算进今天。
            DateTime now = DateTime.Now;
            bool dayChanged = _trafficDay.Date !=
                now.ToString(
                    "yyyy-MM-dd", CultureInfo.InvariantCulture);
            _trafficDay.Update(now, receivedByNic, sentByNic);
            if (dayChanged ||
                (now - _lastTrafficSave).TotalSeconds >= 30)
            {
                _trafficDay.Save();
                _lastTrafficSave = now;
            }

            // GPU Engine 是累计时间计数器，必须两次采样求差；
            // 首次返回 null，对应行自动隐藏，下一秒就有值。
            _lastGpuLoads = TaskbarStats.AggregateGpuLoad(
                _gpuSampler.Sample());

            // 状态条统一用 MB/s，长度稳定；详情卡片里仍显示精确值。
            string up = TaskbarStats.FormatSpeedCompact(upload);
            string down = TaskbarStats.FormatSpeedCompact(download);
            using (Bitmap bitmap = RenderBitmap(
                up, down, cpu, memory, _scale))
            {
                Point position = DrawLayered(bitmap);
                _barBounds = new Rectangle(position, bitmap.Size);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            RefreshNow();
        }

        /// <summary>诊断实拍时挂起自动收起，否则光标不在状态条上会被收掉。</summary>
        public bool SuspendHover { get; set; }

        /// <summary>当前状态条屏幕矩形（诊断用）。</summary>
        public Rectangle BarBounds
        {
            get { return _barBounds; }
        }

        /// <summary>当前详情卡片屏幕矩形（诊断用）。</summary>
        public Rectangle DetailBounds
        {
            get { return _detailBounds; }
        }

        /// <summary>详情卡片当前是否可见（悬停探针用）。</summary>
        public bool IsDetailVisible
        {
            get
            {
                return _detailForm != null &&
                    !_detailForm.IsDisposed &&
                    _detailForm.Visible;
            }
        }

        /// <summary>
        /// 悬停定时器与每帧重绘共用的 Z 序维护：
        /// 状态条必须始终可见，所以它常驻 topmost 层，**绝不主动取消置顶**
        /// ———— 之前「被挡就 ClearTopMost 让位」的策略会把状态条压到
        /// 全屏应用（浏览器 / Electron 应用）底下，用户看到的现象就是
        /// 「直接不显示了」。
        /// 现在只在一种情况下调用 SetWindowPos：真的被排在自己上方的
        /// 可见窗口盖住（IsBarObscured 沿 Z 序链检查）。正常桌面下这个
        /// 判据恒为 false，一次 SetWindowPos 都不发，因此不会出现
        /// 「鼠标点一下来回跳动」；只有点击任务栏、Explorer 把
        /// Shell_TrayWnd 提到我们之上时才会顶回来一次。
        /// </summary>
        private void TryKeepTopmost()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }
            if (_barBounds.IsEmpty)
            {
                // 还没画过内容，位置未知，无从判断是否被盖住。
                return;
            }
            // 被持续压住时（例如某个常驻浮窗就压在条上），最多每
            // 半秒顶一次，避免 150ms 一轮把 SetWindowPos 刷成风暴。
            if ((DateTime.UtcNow - _lastTopmostReclaim)
                    .TotalMilliseconds < 500)
            {
                return;
            }
            bool obscured;
            try
            {
                obscured = TaskbarStats.IsBarObscured(
                    _barBounds, Handle);
            }
            catch
            {
                // 检测失败就当作没被盖住，保持现状，不影响显示。
                obscured = false;
            }
            if (obscured)
            {
                LayeredWindow.KeepTopMost(Handle);
                _lastTopmostReclaim = DateTime.UtcNow;
            }
        }

        /// <summary>悬停检测：光标在状态条或详情卡片内就展开，移开即收起。</summary>
        private void OnHoverTick(object sender, EventArgs e)
        {
            if (SuspendHover || !Visible || IsDisposed)
            {
                return;
            }
            POINT native;
            if (!GetCursorPos(out native))
            {
                return;
            }
            Point cursor = new Point(native.X, native.Y);

            // 光标在任务栏上时快速恢复 Z 序（点一下任务栏就会被 Explorer
            // 压下去），不必等下一秒的采样刷新；鼠标不在任务栏上（例如
            // 全屏游戏）就不额外抢顶层，避免打扰其它窗口。
            // 只压在没人的区域才置顶，绝不跟挡在画面上的别人抢。
            Rectangle taskbar = GetTaskbarRect();
            if (!taskbar.IsEmpty && taskbar.Contains(cursor))
            {
                TryKeepTopmost();
            }

            bool keepOpen = TaskbarStats.ShouldShowDetail(
                cursor,
                _barBounds,
                _detailBounds,
                _detailForm != null &&
                    !_detailForm.IsDisposed &&
                    _detailForm.Visible);

            if (keepOpen)
            {
                EnsureDetail();
                // 刚展开时立刻画一次，之后约每秒跟随数据刷新。
                if (!_detailForm.Visible ||
                    (DateTime.UtcNow - _lastDetailPaint)
                        .TotalMilliseconds >= 900)
                {
                    RefreshDetail();
                    _lastDetailPaint = DateTime.UtcNow;
                }
            }
            else
            {
                HideDetail();
            }
        }

        private void EnsureDetail()
        {
            if (_detailForm == null || _detailForm.IsDisposed)
            {
                _detailForm = new TaskbarStatsDetailForm();
            }
        }

        private void HideDetail()
        {
            if (_detailForm != null &&
                !_detailForm.IsDisposed &&
                _detailForm.Visible)
            {
                _detailForm.Hide();
            }
            _detailBounds = Rectangle.Empty;
        }

        /// <summary>用当前缓存 + 即时采样组装一帧详情。</summary>
        private TaskbarStats.DetailSample CaptureDetail()
        {
            TaskbarStats.DetailSample sample =
                new TaskbarStats.DetailSample();
            sample.DayReceived = _trafficDay.DayReceived;
            sample.DaySent = _trafficDay.DaySent;
            sample.UploadBps = _lastUpload;
            sample.DownloadBps = _lastDownload;
            sample.CpuPercent = _lastCpu;
            sample.CpuMhz = TaskbarStats.CpuFrequencyMhz();

            ulong memoryUsed;
            ulong memoryTotal;
            TaskbarStats.MemoryBytes(
                out memoryUsed, out memoryTotal);
            sample.MemoryUsed = memoryUsed >
                (ulong)long.MaxValue
                    ? long.MaxValue : (long)memoryUsed;
            sample.MemoryTotal = memoryTotal >
                (ulong)long.MaxValue
                    ? long.MaxValue : (long)memoryTotal;

            long diskFree;
            long diskTotal;
            TaskbarStats.DiskSpace(out diskFree, out diskTotal);
            sample.DiskFree = diskFree;
            sample.DiskTotal = diskTotal;

            bool hasBattery;
            int batteryPercent;
            bool onAc;
            int remainSeconds;
            TaskbarStats.Battery(
                out hasBattery, out batteryPercent,
                out onAc, out remainSeconds);
            sample.HasBattery = hasBattery;
            sample.BatteryPercent = batteryPercent;
            sample.BatteryOnAc = onAc;
            sample.BatteryRemainSeconds = remainSeconds;

            sample.GpuLoads = _lastGpuLoads;
            return sample;
        }

        private void RefreshDetail()
        {
            if (_detailForm == null || _detailForm.IsDisposed)
            {
                return;
            }
            List<TaskbarStats.DetailRow> rows =
                TaskbarStats.FormatDetailRows(CaptureDetail());
            _detailBounds = _detailForm.ShowAt(
                _barBounds,
                GetTaskbarRect(),
                rows,
                IsLightTheme());
        }

        /// <summary>诊断用：不依赖鼠标，直接把详情卡片显示出来。</summary>
        public void ShowDetailForProbe()
        {
            EnsureDetail();
            RefreshNow();
            RefreshDetail();
        }

        /// <summary>预览与自检共用的假数据行。</summary>
        public static List<TaskbarStats.DetailRow> BuildPreviewRows()
        {
            TaskbarStats.DetailSample sample =
                new TaskbarStats.DetailSample();
            sample.DayReceived = 1834L * 1024 * 1024;
            sample.DaySent = 246L * 1024 * 1024;
            sample.UploadBps = 6.6 * 1024;
            sample.DownloadBps = 5.04 * 1024;
            sample.CpuPercent = 28;
            sample.CpuMhz = 3370;
            sample.MemoryUsed = 9L * 1024 * 1024 * 1024;
            sample.MemoryTotal = 16L * 1024 * 1024 * 1024;
            sample.DiskFree = 128L * 1024 * 1024 * 1024;
            sample.DiskTotal = 512L * 1024 * 1024 * 1024;
            sample.GpuLoads = new int[] { 17 };
            sample.HasBattery = true;
            sample.BatteryPercent = 82;
            sample.BatteryOnAc = false;
            sample.BatteryRemainSeconds = 8100;
            return TaskbarStats.FormatDetailRows(sample);
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

            TaskbarStats.BarLine firstLine;
            TaskbarStats.BarLine secondLine;
            TaskbarStats.FormatLineParts(
                upload, download, cpuPercent, memoryPercent,
                out firstLine, out secondLine);

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

                // 箭头 / 指标名的列宽按固定文本测；速度列宽取两行当前
                // 速度的较宽者，量化到「一个数字」的粒度。两行共用同一
                // 个值，所以始终上下对称；位图紧贴内容，不会再出现
                // 「网速和 CPU 之间空一大截」。
                float arrowW = Math.Max(
                    measure.MeasureString(
                        "↑", font, 4096, format).Width,
                    measure.MeasureString(
                        "↓", font, 4096, format).Width);
                float maxSpeedW = measure.MeasureString(
                    "999.99 MB/s", font, 4096, format).Width;
                float upWidth = measure.MeasureString(
                    firstLine.Speed, font, 4096, format).Width;
                float downWidth = measure.MeasureString(
                    secondLine.Speed, font, 4096, format).Width;
                float labelW = Math.Max(
                    measure.MeasureString(
                        "CPU", font, 4096, format).Width,
                    measure.MeasureString(
                        "内存", font, 4096, format).Width);
                float pctW = Math.Max(
                    measure.MeasureString(
                        firstLine.Percent, font, 4096, format).Width,
                    measure.MeasureString(
                        secondLine.Percent, font, 4096, format).Width);

                float speedW = TaskbarStats.QuantizeColumn(
                    Math.Max(upWidth, downWidth),
                    8f * scale, maxSpeedW);

                int leftPad = (int)Math.Round(2 * scale);
                int rightPad = (int)Math.Round(6 * scale);
                int gapArrow = (int)Math.Round(4 * scale);
                int gapSpeed = (int)Math.Round(6 * scale);
                int gapLabel = (int)Math.Round(4 * scale);

                int arrowX = leftPad;
                int speedX = leftPad +
                    (int)Math.Ceiling(arrowW) + gapArrow;
                int labelX = speedX +
                    (int)Math.Ceiling(speedW) + gapSpeed;
                int pctX = labelX +
                    (int)Math.Ceiling(labelW) + gapLabel;
                int width = pctX +
                    (int)Math.Ceiling(pctW) + rightPad;
                // 行高留足，避免两行字被压在一起显得上下不齐。
                int lineHeight = (int)Math.Ceiling(16 * scale);
                if (width < 1)
                {
                    width = 1;
                }
                // 先按「行框」给足上下空白画出来，再按实际墨迹裁一刀。
                // DrawString 的 y 是行框顶，数字字形只占行框上部——直接
                // 拿行框高度当位图高度、把两行分别画在 0 / lineHeight，
                // 整块文字就整体偏上（和任务栏里别的工具并排一眼可见）。
                // 裁到墨迹后再对称补 padding，位图中心 == 文字中心，
                // 交给 ComputePosition 做垂直居中就是真的居中。
                int padY = (int)Math.Ceiling(10 * scale);
                int layoutHeight = lineHeight * 2 + padY * 2;

                Bitmap layout = new Bitmap(
                    width, layoutHeight, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(layout))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TextRenderingHint =
                        TextRenderingHint.AntiAliasGridFit;
                    DrawBarLine(
                        graphics, font, brush, format,
                        firstLine, padY,
                        arrowX, speedX, labelX, pctX);
                    DrawBarLine(
                        graphics, font, brush, format,
                        secondLine, padY + lineHeight,
                        arrowX, speedX, labelX, pctX);
                }
                Bitmap bitmap = CropToInk(
                    layout, (int)Math.Round(2 * scale));
                layout.Dispose();
                return bitmap;
            }
        }

        /// <summary>
        /// 把位图按实际墨迹（alpha &gt; 8 的行）裁掉上下空白，再补回对称的
        /// padding。文字位图的「行框顶」比字形顶高一截，若直接拿行框高度
        /// 当最终尺寸，整块字就会偏上；裁到墨迹后上下留白相等，才谈得上
        /// 垂直居中。
        /// </summary>
        private static Bitmap CropToInk(Bitmap source, int padding)
        {
            int width = source.Width;
            int height = source.Height;
            int top = -1;
            int bottom = -1;
            BitmapData data = source.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                int[] pixels = new int[width * height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    bool any = false;
                    for (int x = 0; x < width; x++)
                    {
                        if ((pixels[row + x] >> 24) > 8)
                        {
                            any = true;
                            break;
                        }
                    }
                    if (any)
                    {
                        if (top < 0)
                        {
                            top = y;
                        }
                        bottom = y;
                    }
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
            if (top < 0 || bottom < top)
            {
                top = 0;
                bottom = height - 1;
            }
            if (padding < 0)
            {
                padding = 0;
            }
            int inkHeight = bottom - top + 1;
            Bitmap result = new Bitmap(
                width, inkHeight + padding * 2,
                PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(
                    source,
                    new Rectangle(0, padding - top, width, height),
                    new Rectangle(0, 0, width, height),
                    GraphicsUnit.Pixel);
            }
            return result;
        }

        /// <summary>
        /// 一行四段全部左对齐到各自列起点。四列的起点两行完全共用，
        /// 因此网速再怎么跳也不会上下不对称；各列之间只留很小的固定
        /// 间隙，位图右端紧贴最后一列，不留多余空白。
        /// </summary>
        private static void DrawBarLine(
            Graphics graphics,
            Font font,
            Brush brush,
            StringFormat format,
            TaskbarStats.BarLine line,
            int y,
            int arrowX,
            int speedX,
            int labelX,
            int pctX)
        {
            graphics.DrawString(
                line.Arrow, font, brush, arrowX, y, format);
            graphics.DrawString(
                line.Speed, font, brush, speedX, y, format);
            graphics.DrawString(
                line.Label, font, brush, labelX, y, format);
            graphics.DrawString(
                line.Percent, font, brush, pctX, y, format);
        }

        /// <summary>系统是否使用浅色任务栏（详情卡片配色也跟着走）。</summary>
        public static bool IsLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion" +
                    "\\Themes\\Personalize"))
                {
                    object value = key == null
                        ? null : key.GetValue("SystemUsesLightTheme");
                    return value == null ||
                        Convert.ToInt32(value) != 0;
                }
            }
            catch
            {
                return true;
            }
        }

        /// <summary>任务栏是浅色时用深字，深色时用浅字。</summary>
        public static Color TextColor()
        {
            return IsLightTheme()
                ? Color.FromArgb(255, 26, 28, 32)
                : Color.FromArgb(255, 240, 242, 246);
        }

        private Point DrawLayered(Bitmap bitmap)
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
                // 每一帧重绘后维护 Z 序：状态条必须始终可见。
                // 绝不再主动取消置顶（旧策略会把状态条压到全屏应用之下，
                // 用户看到的是「直接不显示」）；只有真的被排在上面的
                // 窗口盖住时才顶回来一次，正常桌面下恒不触发，
                // 所以不会出现「点一下来回跳」。节流与悬停轮询共用。
                if ((DateTime.UtcNow - _lastTopmostReclaim)
                        .TotalMilliseconds >= 500)
                {
                    bool obscuredNow;
                    try
                    {
                        obscuredNow = TaskbarStats.IsBarObscured(
                            new Rectangle(position, bitmap.Size),
                            Handle);
                    }
                    catch
                    {
                        obscuredNow = false;
                    }
                    if (obscuredNow)
                    {
                        LayeredWindow.KeepTopMost(Handle);
                        _lastTopmostReclaim = DateTime.UtcNow;
                    }
                }
                return position;
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
        /// 分辨率/DPI 变化都会自动跟上，不需要重启。
        /// 定位宽度用固定参考值，位图变窄时左边缘也纹丝不动。</summary>
        public Point ComputePosition(Size content)
        {
            Rectangle taskbar = GetTaskbarRect();
            Rectangle notify = GetTrayNotifyRect();
            int margin = (int)Math.Round(8 * _scale);
            Size reference = new Size(
                Math.Max(_referenceWidth, content.Width),
                content.Height);
            Point basePos = TaskbarStats.ComputePosition(
                taskbar, notify, reference, margin);
            // 落位前先看一眼任务栏带上是不是被别的窗口占了：占了就自动
            // 挪到空档，别把文字叠在别人内容上。任何异常都退回默认位置，
            // 绝不能因为避让逻辑把状态条搞没了。
            try
            {
                List<Rectangle> occupiers =
                    TaskbarStats.FindOccupiers(taskbar, Handle);
                // 默认位置本来是空的：回默认位置（别的工具关掉后自动归位）。
                if (occupiers.Count == 0 ||
                    !TaskbarStats.OverlapsAny(
                        new Rectangle(basePos, reference), occupiers))
                {
                    _avoidAnchor = Point.Empty;
                    return basePos;
                }
                Point avoided = TaskbarStats.AvoidOccupiers(
                    basePos, taskbar, notify, reference, margin,
                    occupiers);
                // 位置锚定：让开一次之后就待在那儿。别人的小部件宽度会
                // 随数字增减左右漂（TrafficMonitor 实测能漂 40px），若每
                // 帧都重新求「最靠近默认位的空档」，状态条会被拽着来回
                // 蹭。只有当当前锚点真的被压住（对方涨过来）才再让一次。
                if (!_avoidAnchor.IsEmpty &&
                    _avoidAnchor.Y == avoided.Y &&
                    !TaskbarStats.OverlapsAny(
                        new Rectangle(_avoidAnchor, reference),
                        occupiers))
                {
                    return _avoidAnchor;
                }
                _avoidAnchor = avoided;
                return avoided;
            }
            catch
            {
                return basePos;
            }
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
