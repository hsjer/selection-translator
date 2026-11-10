using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 一帧画面（32bppArgb 的裸像素）。滚动截图里所有的比对都在这个
    /// 结构上做，避免反复 GetPixel 走 GDI+ 的慢路径。
    /// </summary>
    internal sealed class ScrollFrame
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int[] _pixels;

        private ScrollFrame(int width, int height, int[] pixels)
        {
            _width = width;
            _height = height;
            _pixels = pixels;
        }

        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        public int[] Pixels
        {
            get { return _pixels; }
        }

        public static ScrollFrame FromBitmap(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 1 || bitmap.Height < 1)
            {
                return null;
            }

            int w = bitmap.Width;
            int h = bitmap.Height;
            int[] pixels = new int[w * h];
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(
                        IntPtr.Add(data.Scan0, y * data.Stride),
                        pixels,
                        y * w,
                        w);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return new ScrollFrame(w, h, pixels);
        }

        /// <summary>取底部若干行（画布底部的「参考带」就是这么来的）。</summary>
        public ScrollFrame Tail(int rows)
        {
            if (rows < 1)
            {
                return null;
            }
            if (rows > _height)
            {
                rows = _height;
            }
            return Slice(_height - rows, rows);
        }

        public ScrollFrame Slice(int fromRow, int rows)
        {
            if (fromRow < 0 || rows < 1 ||
                fromRow + rows > _height)
            {
                return null;
            }
            int[] copy = new int[rows * _width];
            Array.Copy(
                _pixels, fromRow * _width, copy, 0, rows * _width);
            return new ScrollFrame(_width, rows, copy);
        }

        /// <summary>取灰度（0.299R + 0.587G + 0.114B 的整数近似）。</summary>
        public int Gray(int x, int y)
        {
            int pixel = _pixels[y * _width + x];
            return (((pixel >> 16) & 0xFF) * 77 +
                ((pixel >> 8) & 0xFF) * 151 +
                (pixel & 0xFF) * 28) >> 8;
        }
    }

    /// <summary>
    /// 滚动截图的纯计算部分：在下一帧里找到「画布底部参考带」出现的位置。
    /// 不碰屏幕、不碰窗口，自检可以直接喂两帧假数据验证。
    ///
    /// 判据是采样点的平均灰度差，不是逐像素相等——滚动截图里经常
    /// 混着光标闪烁、滚动条变化和轻微的抗锯齿差异，逐像素比对会误判。
    /// </summary>
    internal static class ScrollMath
    {
        /// <summary>水平采样列数上限。</summary>
        public const int SampleCols = 24;

        /// <summary>垂直采样行数上限。</summary>
        public const int SampleRows = 24;

        /// <summary>平均灰度差超过这个值就不认这个位置。</summary>
        public const int DefaultTolerance = 16;

        /// <summary>
        /// 返回 reference 顶部在 frame 中的行号；-1 表示找不到可信匹配。
        /// 命中 y 之后，frame 中 [y, y + reference.Height) 就是画布底部
        /// 那段内容，再往下的行才是新出现的部分。
        /// </summary>
        public static int MatchOffset(
            ScrollFrame reference,
            ScrollFrame frame,
            int tolerance)
        {
            int diff;
            return MatchOffset(reference, frame, tolerance, out diff);
        }

        public static int MatchOffset(
            ScrollFrame reference,
            ScrollFrame frame,
            int tolerance,
            out int bestDiff)
        {
            bestDiff = int.MaxValue;
            if (reference == null || frame == null)
            {
                return -1;
            }
            if (reference.Width != frame.Width ||
                reference.Height < 8 ||
                reference.Height > frame.Height)
            {
                return -1;
            }

            int maxStart = frame.Height - reference.Height;
            int rowStep = Math.Max(1, reference.Height / SampleRows);
            int colStep = Math.Max(1, reference.Width / SampleCols);

            int best = -1;
            for (int y = 0; y <= maxStart; y++)
            {
                long sum = 0;
                int count = 0;
                for (int r = 0; r < reference.Height; r += rowStep)
                {
                    int frameY = y + r;
                    for (int x = 0; x < reference.Width; x += colStep)
                    {
                        sum += Math.Abs(
                            reference.Gray(x, r) - frame.Gray(x, frameY));
                        count++;
                    }
                    // 当前行的平均值已经比历史最优还差，没必要算完。
                    if (bestDiff != int.MaxValue && count > 0 &&
                        sum / count > bestDiff)
                    {
                        break;
                    }
                }
                if (count == 0)
                {
                    continue;
                }
                int average = (int)(sum / count);
                if (average < bestDiff)
                {
                    bestDiff = average;
                    best = y;
                    if (average == 0)
                    {
                        break;
                    }
                }
            }

            return bestDiff <= tolerance ? best : -1;
        }

        /// <summary>
        /// 两帧看起来是不是同一幅画面（用户还没拨滚轮）。
        /// 只采样十几个点，够用而且几乎不耗时。
        /// </summary>
        public static bool LooksSame(
            ScrollFrame a, ScrollFrame b, int tolerance)
        {
            if (a == null || b == null ||
                a.Width != b.Width || a.Height != b.Height)
            {
                return false;
            }

            int colStep = Math.Max(1, a.Width / 16);
            int rowStep = Math.Max(1, a.Height / 16);
            long sum = 0;
            int count = 0;
            for (int y = 0; y < a.Height; y += rowStep)
            {
                for (int x = 0; x < a.Width; x += colStep)
                {
                    sum += Math.Abs(a.Gray(x, y) - b.Gray(x, y));
                    count++;
                }
            }
            if (count == 0)
            {
                return true;
            }
            return sum / count <= tolerance;
        }
    }

    /// <summary>
    /// 一次滚动截图会话：定时抓取选区画面，和已经拼好的长图底部做
    /// 重叠匹配，把新出现的行接到下面。
    ///
    /// 参考带永远取「画布最底部」，所以不会因为逐帧累加误差而越拼越歪；
    /// 用户往回滚时匹配自然失败，也不会重复拼接同一段内容。
    /// </summary>
    internal sealed class ScrollCaptureSession : IDisposable
    {
        /// <summary>参考带高度：越大越准，但允许的单帧滚动距离越小。</summary>
        private const int ReferenceRows = 240;

        /// <summary>匹配失败时逐步缩短参考带，容忍「一帧滚很多」。</summary>
        private const int MinReferenceRows = 64;

        /// <summary>长图高度上限，防止无限滚动把内存吃光。</summary>
        public const int MaxCanvasHeight = 16000;

        private const int FrameInterval = 130;

        private readonly Rectangle _area;
        private readonly System.Windows.Forms.Timer _timer;
        private Bitmap _canvas;
        private ScrollFrame _reference;
        private ScrollFrame _lastFrame;
        private bool _started;
        private bool _disposed;

        public event EventHandler Changed;

        public ScrollCaptureSession(Rectangle area)
        {
            _area = ScrollCaptureSession.ClampToScreen(area);
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = FrameInterval;
            _timer.Tick += OnTick;
        }

        public Rectangle Area
        {
            get { return _area; }
        }

        public Bitmap Canvas
        {
            get { return _canvas; }
        }

        /// <summary>已经拼出来的长图高度。</summary>
        public int StitchedHeight
        {
            get { return _canvas == null ? 0 : _canvas.Height; }
        }

        /// <summary>是否已经到达高度上限（再滚也不会变长了）。</summary>
        public bool ReachedLimit
        {
            get
            {
                return _canvas != null &&
                    _canvas.Height >= MaxCanvasHeight;
            }
        }

        public static Rectangle ClampToScreen(Rectangle rect)
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            Rectangle clamped = Rectangle.Intersect(
                rect, virtualScreen);
            if (clamped.Width < 1)
            {
                clamped.Width = 1;
            }
            if (clamped.Height < 1)
            {
                clamped.Height = 1;
            }
            return clamped;
        }

        /// <summary>抓第一帧；调用前浮层必须已经隐藏。</summary>
        public bool Start()
        {
            if (_started || _disposed)
            {
                return false;
            }
            _started = true;

            _canvas = Capture(_area);
            if (_canvas == null)
            {
                return false;
            }

            _lastFrame = ScrollFrame.FromBitmap(_canvas);
            if (_lastFrame != null)
            {
                _reference = _lastFrame.Tail(
                    Math.Min(ReferenceRows, _lastFrame.Height));
            }
            _timer.Start();
            FireChanged();
            return true;
        }

        /// <summary>结束会话并把长图交出去（调用方负责释放）。</summary>
        public Bitmap Finish()
        {
            Stop();
            Bitmap result = _canvas;
            _canvas = null;
            return result;
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }
            try
            {
                CaptureTick();
            }
            catch (Exception ex)
            {
                Log.Error("滚动截图抓帧失败", ex);
            }
        }

        private void CaptureTick()
        {
            if (_reference == null || _canvas == null ||
                _area.Width < 8 || _area.Height < 8)
            {
                return;
            }

            Bitmap shot = Capture(_area);
            if (shot == null)
            {
                return;
            }

            ScrollFrame frame;
            try
            {
                frame = ScrollFrame.FromBitmap(shot);
            }
            finally
            {
                shot.Dispose();
            }
            if (frame == null)
            {
                return;
            }

            if (_lastFrame != null &&
                ScrollMath.LooksSame(_lastFrame, frame, 2))
            {
                _lastFrame = frame;
                return;
            }

            ScrollFrame reference = _reference;
            int y = ScrollMath.MatchOffset(
                reference, frame, ScrollMath.DefaultTolerance);

            // 一帧滚得比较多时参考带顶部会滚出画面，换短一点的参考带再试。
            int rows = Math.Max(MinReferenceRows, reference.Height / 2);
            while (y < 0 && rows >= MinReferenceRows)
            {
                ScrollFrame shorter = reference.Tail(rows);
                if (shorter == null || shorter.Height == reference.Height)
                {
                    break;
                }
                reference = shorter;
                y = ScrollMath.MatchOffset(
                    reference, frame, ScrollMath.DefaultTolerance);
                rows /= 2;
            }

            _lastFrame = frame;
            if (y < 0)
            {
                // 没匹配上：可能是往回滚，也可能是页面切换。保持画布不动，
                // 下一帧再试，绝不乱接一段内容上去。
                return;
            }

            int addedRows = frame.Height - (y + reference.Height);
            if (addedRows > 0 &&
                _canvas.Height + addedRows <= MaxCanvasHeight)
            {
                Append(frame, y + reference.Height, addedRows);
                // 追加之后画布底部正好等于这一帧的底部。
                _reference = frame.Tail(
                    Math.Min(ReferenceRows, frame.Height));
                FireChanged();
            }
        }

        private void Append(ScrollFrame frame, int fromRow, int rows)
        {
            int width = _canvas.Width;
            int newHeight = _canvas.Height + rows;
            Bitmap bigger = new Bitmap(
                width, newHeight, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bigger))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(_canvas, 0, 0);
                }

                BitmapData data = bigger.LockBits(
                    new Rectangle(0, _canvas.Height, width, rows),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    for (int i = 0; i < rows; i++)
                    {
                        Marshal.Copy(
                            frame.Pixels,
                            (fromRow + i) * width,
                            IntPtr.Add(data.Scan0, i * data.Stride),
                            width);
                    }
                }
                finally
                {
                    bigger.UnlockBits(data);
                }
            }
            catch
            {
                bigger.Dispose();
                throw;
            }

            _canvas.Dispose();
            _canvas = bigger;
        }

        private static Bitmap Capture(Rectangle area)
        {
            Rectangle rect = ClampToScreen(area);
            try
            {
                Bitmap bitmap = new Bitmap(
                    rect.Width, rect.Height,
                    PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(
                        rect.Left, rect.Top, 0, 0,
                        rect.Size, CopyPixelOperation.SourceCopy);
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Error("滚动截图取屏失败", ex);
                return null;
            }
        }

        private void FireChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
            if (_canvas != null)
            {
                _canvas.Dispose();
                _canvas = null;
            }
            _reference = null;
            _lastFrame = null;
        }
    }

    /// <summary>
    /// 滚动截图期间的小悬浮条：显示已拼接高度，提供「完成 / 取消」。
    /// 用 WS_EX_NOACTIVATE 显示，绝不抢目标窗口的焦点，否则滚轮就
    /// 滚不动底下的页面了。
    /// </summary>
    internal sealed class ScrollCaptureBar : Form
    {
        private readonly Rectangle _avoid;
        private readonly FlatButton _doneButton;
        private readonly FlatButton _cancelButton;
        private string _status;

        public ScrollCaptureBar(Rectangle avoid, int startHeight)
        {
            _avoid = avoid;
            _status = "已拼接 " + startHeight + " px";

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = DarkUi.Card;
            ClientSize = new Size(420, 74);
            Font = DarkUi.FontSmall;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);

            _doneButton = new FlatButton();
            _doneButton.Text = "完成";
            _doneButton.Size = new Size(62, 30);
            _doneButton.Primary = true;
            _doneButton.SurfaceColor = DarkUi.Card;
            _doneButton.Location = new Point(ClientSize.Width - 142, 21);
            _doneButton.Click += delegate
            {
                RequestFinish();
            };
            Controls.Add(_doneButton);

            _cancelButton = new FlatButton();
            _cancelButton.Text = "取消";
            _cancelButton.Size = new Size(62, 30);
            _cancelButton.SurfaceColor = DarkUi.Card;
            _cancelButton.Location = new Point(ClientSize.Width - 74, 21);
            _cancelButton.Click += delegate
            {
                RequestCancel();
            };
            Controls.Add(_cancelButton);
        }

        /// <summary>Enter / 「完成」按钮都走这里。</summary>
        public void RequestFinish()
        {
            if (IsDisposed || !Visible)
            {
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Esc / 「取消」按钮都走这里。</summary>
        public void RequestCancel()
        {
            if (IsDisposed || !Visible)
            {
                return;
            }
            DialogResult = DialogResult.Cancel;
            Close();
        }

        public string Status
        {
            get { return _status; }
            set
            {
                _status = value ?? "";
                Invalidate();
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE |
                    NativeMethods.WS_EX_TOOLWINDOW |
                    NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        /// <summary>
        /// 摆在选区外面，避免悬浮条自己被拍进长图。放不下时返回 false，
        /// 由调用方决定是否仍然显示（此时只能靠 Esc / Enter 结束）。
        /// </summary>
        public bool PlaceOutsideCapture()
        {
            Rectangle work = Screen.FromRectangle(_avoid).WorkingArea;
            const int gap = 10;

            Point[] candidates = new Point[]
            {
                new Point(_avoid.Right + gap, _avoid.Bottom - Height),
                new Point(_avoid.Left - Width - gap,
                    _avoid.Bottom - Height),
                new Point(_avoid.Left, _avoid.Bottom + gap),
                new Point(_avoid.Left, _avoid.Top - Height - gap),
                new Point(_avoid.Right - Width, _avoid.Top - Height - gap)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                Rectangle target = new Rectangle(
                    candidates[i], Size);
                target = ClampInto(target, work);
                if (!target.IntersectsWith(_avoid) &&
                    work.Contains(target))
                {
                    Location = target.Location;
                    return true;
                }
            }

            // 实在放不下（选区几乎占满屏幕）：贴在工作区右下角。
            Rectangle fallback = new Rectangle(
                work.Right - Width - 8,
                work.Bottom - Height - 8,
                Width, Height);
            Location = fallback.Location;
            return !fallback.IntersectsWith(_avoid);
        }

        private static Rectangle ClampInto(
            Rectangle rect, Rectangle bounds)
        {
            int x = Math.Max(bounds.Left,
                Math.Min(rect.Left, bounds.Right - rect.Width));
            int y = Math.Max(bounds.Top,
                Math.Min(rect.Top, bounds.Bottom - rect.Height));
            return new Rectangle(x, y, rect.Width, rect.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(DarkUi.Card);

            RectangleF frame = new RectangleF(
                0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = RoundedRect(frame, 10f))
            using (Pen pen = new Pen(DarkUi.Border, 1f))
            {
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                g,
                "滚轮滚动页面，自动拼接成长图",
                DarkUi.FontSmall,
                new Point(16, 14),
                DarkUi.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                g,
                _status + " · Esc 取消 / Enter 完成",
                DarkUi.FontTiny,
                new Point(16, 40),
                DarkUi.TextDim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        internal static GraphicsPath RoundedRect(
            RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > rect.Width)
            {
                d = rect.Width;
            }
            if (d > rect.Height)
            {
                d = rect.Height;
            }
            path.AddArc(rect.Left, rect.Top, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270f, 90f);
            path.AddArc(
                rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// 滚动期间监听 Esc / Enter。悬浮条是「不激活」窗口拿不到键盘焦点，
    /// 而目标窗口正被用户滚动，不能抢它的焦点，所以只能走低级键盘钩子。
    /// </summary>
    internal sealed class ScrollKeyWatcher : IDisposable
    {
        private readonly NativeMethods.LowLevelKeyboardProc _callback;
        private readonly SynchronizationContext _uiContext;
        private IntPtr _hook;
        private bool _disposed;

        public event EventHandler FinishRequested;
        public event EventHandler CancelRequested;

        /// <summary>
        /// 默认忽略程序注入的按键；端到端探针用 SendInput 造按键时临时关掉。
        /// </summary>
        public bool IgnoreInjectedKeys { get; set; }

        public ScrollKeyWatcher()
        {
            _uiContext = SynchronizationContext.Current;
            _callback = HookCallback;
            IgnoreInjectedKeys = true;
        }

        public bool IsRunning
        {
            get { return _hook != IntPtr.Zero; }
        }

        public void Start()
        {
            if (_disposed || _hook != IntPtr.Zero)
            {
                return;
            }
            IntPtr module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookExKeyboard(
                NativeMethods.WH_KEYBOARD_LL,
                _callback,
                module,
                0);
            if (_hook == IntPtr.Zero)
            {
                Log.Error("滚动截图键盘钩子安装失败，Win32 错误码：" +
                    Marshal.GetLastWin32Error(), null);
            }
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero)
            {
                return;
            }
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr HookCallback(
            int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && _hook != IntPtr.Zero)
                {
                    NativeMethods.KBDLLHOOKSTRUCT info =
                        (NativeMethods.KBDLLHOOKSTRUCT)
                        Marshal.PtrToStructure(
                            lParam,
                            typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    if (IgnoreInjectedKeys &&
                        (info.flags & (NativeMethods.LLKHF_INJECTED |
                        NativeMethods.LLKHF_LOWER_IL_INJECTED)) != 0)
                    {
                        return NativeMethods.CallNextHookEx(
                            _hook, nCode, wParam, lParam);
                    }

                    uint message = (uint)wParam.ToInt32();
                    bool isDown =
                        message == NativeMethods.WM_KEYDOWN ||
                        message == NativeMethods.WM_SYSKEYDOWN;
                    if (isDown)
                    {
                        EventHandler handler = null;
                        if (info.vkCode == NativeMethods.VK_ESCAPE)
                        {
                            handler = CancelRequested;
                        }
                        else if (info.vkCode == NativeMethods.VK_RETURN)
                        {
                            handler = FinishRequested;
                        }
                        if (handler != null)
                        {
                            EventHandler target = handler;
                            if (_uiContext != null)
                            {
                                _uiContext.Post(
                                    delegate { Fire(target); }, null);
                            }
                            else
                            {
                                Fire(target);
                            }
                            // 吞掉按键，别让底下的页面也跟着响应。
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("处理滚动截图键盘钩子失败", ex);
            }
            return NativeMethods.CallNextHookEx(
                _hook, nCode, wParam, lParam);
        }

        private static void Fire(EventHandler handler)
        {
            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error("处理滚动截图按键失败", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Stop();
        }
    }

    /// <summary>
    /// 长截图预览：滚轮上下看整张图，右键/按钮保存或复制。
    /// 滚动截图结束后由浮层打开，位图所有权归本窗口。
    /// </summary>
    internal sealed class LongShotForm : ChromeForm
    {
        private readonly Bitmap _image;
        private readonly LongShotCanvas _canvas;
        private readonly FlatButton _saveButton;
        private readonly FlatButton _copyButton;
        private readonly FlatButton _pinButton;
        private readonly FlatButton _closeButton;
        private readonly GlyphButton _closeGlyph;
        private readonly string _title;

        public LongShotForm(Bitmap image)
        {
            _image = image;
            _title = "长截图 · " + image.Width + " × " + image.Height;

            Text = "长截图 - " + AppInfo.Name;
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(520, 360);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            _canvas = new LongShotCanvas();
            _canvas.SetImage(image);
            Controls.Add(_canvas);

            _saveButton = MakeButton("保存 PNG", OnSave);
            _copyButton = MakeButton("复制", OnCopy);
            _pinButton = MakeButton("贴到桌面", OnPin);
            _closeButton = MakeButton("关闭", delegate
            {
                Close();
            });

            _closeGlyph = new GlyphButton(
                GlyphKind.Close, DarkUi.Window, 40, 30);
            _closeGlyph.Click += delegate { Close(); };
            Controls.Add(_closeGlyph);

            LayoutChildren();
        }

        protected override int HeaderHeight
        {
            get { return 46; }
        }

        private FlatButton MakeButton(
            string text, EventHandler onClick)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Size = new Size(96, 34);
            button.SurfaceColor = DarkUi.Surface;
            button.Click += onClick;
            Controls.Add(button);
            return button;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            // 基类构造函数里设置 ClientSize / MinimumSize 就会触发 OnResize，
            // 那时候子控件还没建出来，直接返回。
            if (_canvas == null || _saveButton == null ||
                _closeGlyph == null)
            {
                return;
            }

            const int footer = 54;
            _canvas.SetBounds(
                0, HeaderHeight, ClientSize.Width,
                Math.Max(40, ClientSize.Height - HeaderHeight - footer));

            int y = ClientSize.Height - footer + 10;
            _saveButton.Location = new Point(16, y);
            _copyButton.Location = new Point(118, y);
            _pinButton.Location = new Point(220, y);
            _closeButton.Location = new Point(
                Math.Max(16, ClientSize.Width - 112), y);
            _closeGlyph.Location = new Point(
                ClientSize.Width - 44, 8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(DarkUi.Window);

            using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
            {
                g.DrawLine(pen, 0, HeaderHeight - 1,
                    ClientSize.Width, HeaderHeight - 1);
                g.DrawLine(pen, 0,
                    ClientSize.Height - 54, ClientSize.Width,
                    ClientSize.Height - 54);
            }

            TextRenderer.DrawText(
                g, _title, DarkUi.FontTitle, new Point(16, 12),
                DarkUi.Text, TextFormatFlags.Left |
                    TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                g,
                "滚轮上下查看 · Ctrl+滚轮缩放 · Esc 关闭",
                DarkUi.FontTiny,
                new Point(ClientSize.Width - 400, 18),
                DarkUi.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < HeaderHeight)
            {
                BeginWindowDrag();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                float factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
                _canvas.Zoom(factor);
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                return;
            }
            if (e.Control && e.KeyCode == Keys.S)
            {
                OnSave(this, EventArgs.Empty);
                return;
            }
            if (e.Control && e.KeyCode == Keys.C)
            {
                OnCopy(this, EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }

        private void OnSave(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "保存长截图";
                dialog.Filter = "PNG 图片 (*.png)|*.png";
                dialog.DefaultExt = "png";
                dialog.FileName = "长截图_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    _image.Save(dialog.FileName, ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "保存失败：" + ex.Message,
                        "保存长截图",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void OnCopy(object sender, EventArgs e)
        {
            try
            {
                ClipboardPasteFiles.WriteBitmap(
                    _image, ClipboardWriteMode.Auto);
            }
            catch (Exception ex)
            {
                Log.Error("复制长截图失败", ex);
            }
        }

        private void OnPin(object sender, EventArgs e)
        {
            try
            {
                Bitmap copy = new Bitmap(_image);
                PinnedImageForm pin = new PinnedImageForm(
                    copy, Point.Empty);
                pin.Show();
                Close();
            }
            catch (Exception ex)
            {
                Log.Error("贴长截图失败", ex);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel)
            {
                return;
            }
            _canvas.SetImage(null);
            _image.Dispose();
        }

        /// <summary>
        /// 预览画布：按宽度自适应缩放，纵向用 ScrollableControl 自带滚动。
        /// </summary>
        private sealed class LongShotCanvas : ScrollableControl
        {
            private Bitmap _image;
            private float _scale = 1f;
            // 默认按窗口宽度自适应；用户手动缩放后就不再跟着窗口变。
            private bool _autoFit = true;

            public LongShotCanvas()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint |
                    ControlStyles.ResizeRedraw, true);
                BackColor = DarkUi.Sunken;
                AutoScroll = true;
            }

            public void SetImage(Bitmap image)
            {
                _image = image;
                _autoFit = true;
                FitWidth();
            }

            public void FitWidth()
            {
                if (_image == null)
                {
                    AutoScrollMinSize = Size.Empty;
                    Invalidate();
                    return;
                }
                float width = Math.Max(80f, ClientSize.Width - 32f);
                float scale = width / _image.Width;
                if (scale > 1f)
                {
                    scale = 1f;
                }
                if (scale < 0.02f)
                {
                    scale = 0.02f;
                }
                _scale = scale;
                ApplySize();
            }

            public void Zoom(float factor)
            {
                if (_image == null)
                {
                    return;
                }
                _autoFit = false;
                float next = _scale * factor;
                if (next < 0.05f)
                {
                    next = 0.05f;
                }
                if (next > 4f)
                {
                    next = 4f;
                }
                _scale = next;
                ApplySize();
            }

            private void ApplySize()
            {
                int w = (int)Math.Round(_image.Width * _scale);
                int h = (int)Math.Round(_image.Height * _scale);
                AutoScrollMinSize = new Size(w + 32, h + 32);
                Invalidate();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                // 没手动缩放过就一直跟着窗口宽度走。
                if (_image != null && _autoFit)
                {
                    FitWidth();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(DarkUi.Sunken);
                if (_image == null)
                {
                    return;
                }

                g.InterpolationMode = _scale < 1f
                    ? InterpolationMode.HighQualityBicubic
                    : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int w = (int)Math.Round(_image.Width * _scale);
                int h = (int)Math.Round(_image.Height * _scale);
                int x = Math.Max(16,
                    (ClientSize.Width - w) / 2);
                int y = 16 + AutoScrollPosition.Y;

                Rectangle target = new Rectangle(x, y, w, h);
                using (SolidBrush shadow = new SolidBrush(
                    Color.FromArgb(90, 0, 0, 0)))
                {
                    g.FillRectangle(shadow,
                        target.X + 4, target.Y + 4,
                        target.Width, target.Height);
                }
                g.DrawImage(_image, target);
            }
        }
    }
}
