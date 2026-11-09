using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SelectionTranslator
{
    /// <summary>
    /// 「连按两下 Ctrl」的纯状态机，不碰 Win32，方便自检和探针直接喂按键序列。
    /// 触发条件：两次短促的 Ctrl 按下→松开，两次间隔不超过 intervalMs，
    /// 且中间不能夹任何别的键（否则 Ctrl+C、Ctrl+V 会误触发）。
    /// 左右 Ctrl 由调用方统一当成同一种键送进来。
    /// </summary>
    internal sealed class DoubleCtrlDetector
    {
        public const int DefaultIntervalMs = 400;
        public const int DefaultMaxHoldMs = 300;

        private readonly int _intervalMs;
        private readonly int _maxHoldMs;
        private int _downTime = -1;
        private int _lastTapTime = -1;
        private bool _hasTap;

        public DoubleCtrlDetector()
            : this(DefaultIntervalMs, DefaultMaxHoldMs)
        {
        }

        public DoubleCtrlDetector(int intervalMs, int maxHoldMs)
        {
            _intervalMs = Math.Max(80, Math.Min(2000, intervalMs));
            _maxHoldMs = Math.Max(50, Math.Min(2000, maxHoldMs));
        }

        public int IntervalMs
        {
            get { return _intervalMs; }
        }

        public int MaxHoldMs
        {
            get { return _maxHoldMs; }
        }

        public void Reset()
        {
            _downTime = -1;
            _lastTapTime = -1;
            _hasTap = false;
        }

        /// <summary>Ctrl 按下；返回是否触发（触发判定放在抬起，故恒为 false）。</summary>
        public bool OnCtrlDown(int now)
        {
            // 按住不放时系统会重复投递 WM_KEYDOWN，不能当成第二次轻点。
            if (_downTime >= 0)
            {
                return false;
            }
            if (_hasTap && Elapsed(_lastTapTime, now) > _intervalMs)
            {
                Reset();
            }
            _downTime = now;
            return false;
        }

        /// <summary>Ctrl 抬起；返回 true 表示识别到双击。</summary>
        public bool OnCtrlUp(int now)
        {
            if (_downTime < 0)
            {
                return false;
            }
            int hold = Elapsed(_downTime, now);
            _downTime = -1;
            if (hold < 0 || hold > _maxHoldMs)
            {
                // 长按（比如按住 Ctrl 选文件）不算轻点。
                Reset();
                return false;
            }

            if (_hasTap && Elapsed(_lastTapTime, now) <= _intervalMs)
            {
                // 触发后清空，三连击不会连着弹两次窗口。
                Reset();
                return true;
            }

            _lastTapTime = now;
            _hasTap = true;
            return false;
        }

        /// <summary>两次轻点之间按下任何别的键都取消本轮（Ctrl+C 等）。</summary>
        public void OnOtherKeyDown()
        {
            Reset();
        }

        /// <summary>识别左右 Ctrl 的虚拟键码；低级钩子给的是具体左右键。</summary>
        public static bool IsCtrlKey(uint virtualKey)
        {
            return virtualKey == (uint)NativeMethods.VK_CONTROL ||
                virtualKey == (uint)NativeMethods.VK_LCONTROL ||
                virtualKey == (uint)NativeMethods.VK_RCONTROL;
        }

        /// <summary>注入的按键（程序自己模拟的粘贴 / 上屏）不算用户操作。</summary>
        public static bool IsInjected(uint flags)
        {
            return (flags & (NativeMethods.LLKHF_INJECTED |
                NativeMethods.LLKHF_LOWER_IL_INJECTED)) != 0;
        }

        /// <summary>
        /// TickCount 会回绕，用 unchecked 差值；负数说明时间倒流，
        /// 由调用方按「长按 / 超时」保守处理。
        /// </summary>
        private static int Elapsed(int from, int to)
        {
            return unchecked(to - from);
        }
    }

    /// <summary>
    /// WH_KEYBOARD_LL 全局钩子。RegisterHotKey 不支持只用一个修饰键，
    /// 所以「双击 Ctrl」只能靠低级键盘钩子识别；回调里只做状态判断，
    /// 真正打开窗口通过 SynchronizationContext.Post 投递回 UI 线程。
    /// </summary>
    internal sealed class DoubleCtrlWatcher : IDisposable
    {
        private readonly NativeMethods.LowLevelKeyboardProc _callback;
        private readonly SynchronizationContext _uiContext;
        private readonly DoubleCtrlDetector _detector =
            new DoubleCtrlDetector();
        private IntPtr _hook;
        private bool _disposed;

        public event EventHandler DoubleCtrlPressed;

        /// <summary>
        /// 默认忽略程序注入的按键，避免本工具模拟键入 / 粘贴时自己触发自己。
        /// 端到端探针用 SendInput 造按键，会临时关掉这个开关。
        /// </summary>
        public bool IgnoreInjectedKeys { get; set; }

        public DoubleCtrlWatcher(SynchronizationContext uiContext)
            : this(uiContext, null)
        {
        }

        public DoubleCtrlWatcher(
            SynchronizationContext uiContext,
            DoubleCtrlDetector detector)
        {
            if (uiContext == null)
            {
                throw new ArgumentNullException("uiContext");
            }
            if (detector != null)
            {
                _detector = detector;
            }
            _uiContext = uiContext;
            _callback = HookCallback;
            IgnoreInjectedKeys = true;
        }

        public bool IsRunning
        {
            get { return _hook != IntPtr.Zero; }
        }

        public DoubleCtrlDetector Detector
        {
            get { return _detector; }
        }

        public void Start()
        {
            if (_disposed || _hook != IntPtr.Zero)
            {
                return;
            }
            _detector.Reset();
            IntPtr module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookExKeyboard(
                NativeMethods.WH_KEYBOARD_LL,
                _callback,
                module,
                0);
            if (_hook == IntPtr.Zero)
            {
                Log.Error(
                    "安装全局键盘钩子失败，Win32 错误码：" +
                    Marshal.GetLastWin32Error(),
                    null);
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
            _detector.Reset();
        }

        private IntPtr HookCallback(
            int nCode,
            IntPtr wParam,
            IntPtr lParam)
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
                        DoubleCtrlDetector.IsInjected(info.flags))
                    {
                        return NativeMethods.CallNextHookEx(
                            _hook, nCode, wParam, lParam);
                    }

                    uint message = (uint)wParam.ToInt32();
                    bool isDown =
                        message == NativeMethods.WM_KEYDOWN ||
                        message == NativeMethods.WM_SYSKEYDOWN;
                    bool isUp =
                        message == NativeMethods.WM_KEYUP ||
                        message == NativeMethods.WM_SYSKEYUP;
                    if (isDown || isUp)
                    {
                        bool fired = false;
                        if (DoubleCtrlDetector.IsCtrlKey(info.vkCode))
                        {
                            int now = Environment.TickCount;
                            if (isDown)
                            {
                                _detector.OnCtrlDown(now);
                            }
                            else
                            {
                                fired = _detector.OnCtrlUp(now);
                            }
                        }
                        else if (isDown)
                        {
                            _detector.OnOtherKeyDown();
                        }

                        if (fired)
                        {
                            _uiContext.Post(
                                delegate { FirePressed(); },
                                null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("处理双击 Ctrl 钩子失败", ex);
            }
            return NativeMethods.CallNextHookEx(
                _hook, nCode, wParam, lParam);
        }

        private void FirePressed()
        {
            if (_disposed)
            {
                return;
            }
            EventHandler handler = DoubleCtrlPressed;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error("处理双击 Ctrl 失败", ex);
                }
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
}
