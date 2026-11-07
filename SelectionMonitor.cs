using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace SelectionTranslator
{
    internal sealed class SelectionChangedEventArgs : EventArgs
    {
        public string Text { get; private set; }
        public Point Location { get; private set; }

        public SelectionChangedEventArgs(string text, Point location)
        {
            Text = text;
            Location = location;
        }
    }

    /// <summary>
    /// 轮询检测当前焦点元素的选中文本；当选区稳定（停止变化约 350ms）
    /// 后触发事件，用于实现“选中即显示翻译图标”。
    /// UI Automation 调用与状态读写都放在后台线程，事件通过
    /// SynchronizationContext 投递到 UI 线程。
    /// </summary>
    internal sealed class SelectionMonitor : IDisposable
    {
        private readonly object _sync = new object();
        private readonly SynchronizationContext _uiContext;
        private readonly int _maxLength;

        private Timer _pollTimer;
        private Timer _debounceTimer;
        private string _pendingText = "";
        private string _lastEmitted = "";
        private Point _pendingPoint;
        private bool _running;

        public event EventHandler<SelectionChangedEventArgs> SelectionChanged;

        public SelectionMonitor(SynchronizationContext uiContext, int maxLength)
        {
            if (uiContext == null)
            {
                throw new ArgumentNullException("uiContext");
            }
            _uiContext = uiContext;
            _maxLength = maxLength > 0 ? maxLength : 3000;
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_running)
                {
                    return;
                }
                _running = true;
                _lastEmitted = "";
                _pollTimer = new Timer(OnPoll, null, 800, 450);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_running)
                {
                    return;
                }
                _running = false;
                DisposeTimer(ref _pollTimer);
                DisposeTimer(ref _debounceTimer);
                _pendingText = "";
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnPoll(object state)
        {
            // 忽略本应用自身的窗口，避免在翻译/设置窗口上弹出气泡。
            if (IsOwnForeground())
            {
                return;
            }

            // UIA 调用不放在锁内，避免阻塞其他回调。
            string text = SelectionCapture.GetFocusedSelection(_maxLength);
            bool scheduleDebounce = false;

            lock (_sync)
            {
                if (!_running)
                {
                    return;
                }

                if (text.Length == 0)
                {
                    _pendingText = "";
                    DisposeTimer(ref _debounceTimer);
                    return;
                }

                if (text == _pendingText)
                {
                    return;
                }

                _pendingText = text;
                NativeMethods.POINT pt;
                NativeMethods.GetCursorPos(out pt);
                _pendingPoint = new Point(pt.X, pt.Y);
                DisposeTimer(ref _debounceTimer);
                scheduleDebounce = true;
            }

            if (scheduleDebounce)
            {
                _debounceTimer = new Timer(OnDebounce, null, 350, Timeout.Infinite);
            }
        }

        private void OnDebounce(object state)
        {
            string text;
            Point location = Point.Empty;
            bool shouldEmit = false;

            lock (_sync)
            {
                if (!_running)
                {
                    return;
                }

                text = _pendingText;
                if (text.Length == 0)
                {
                    return;
                }

                // 选区清空后允许下次选中相同文本时再次触发。
                if (_lastEmitted.Length == 0 || text != _lastEmitted)
                {
                    _lastEmitted = text;
                    location = _pendingPoint;
                    shouldEmit = true;
                }
                else
                {
                    shouldEmit = false;
                }
            }

            if (shouldEmit)
            {
                PostSelectionChanged(text, location);
            }
        }

        private void PostSelectionChanged(string text, Point location)
        {
            SynchronizationContext context = _uiContext;
            context.Post(delegate
            {
                EventHandler<SelectionChangedEventArgs> handler =
                    SelectionChanged;
                if (handler != null)
                {
                    try
                    {
                        handler(this, new SelectionChangedEventArgs(
                            text,
                            location));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("处理选区变化事件失败", ex);
                    }
                }
            }, null);
        }

        private static void DisposeTimer(ref Timer timer)
        {
            Timer old = timer;
            timer = null;
            if (old != null)
            {
                old.Dispose();
            }
        }

        private bool IsOwnForeground()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    return true;
                }
                uint processId;
                NativeMethods.GetWindowThreadProcessId(
                    foreground,
                    out processId);
                if (processId == 0)
                {
                    return true;
                }
                return string.Equals(
                    Process.GetProcessById((int)processId).ProcessName,
                    "SelectionTranslator",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}
