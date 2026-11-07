using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace SelectionTranslator
{
    internal sealed class RightClickEventArgs : EventArgs
    {
        public Point Location { get; private set; }
        public bool Handled { get; set; }

        public RightClickEventArgs(Point location)
        {
            Location = location;
        }
    }

    internal sealed class RightClickWatcher : IDisposable
    {
        private readonly NativeMethods.LowLevelMouseProc _callback;
        private readonly SynchronizationContext _uiContext;
        private IntPtr _hook;
        private bool _rightButtonDown;
        private bool _suppressLeftUp;
        private NativeMethods.POINT _downPoint;

        public event EventHandler<RightClickEventArgs> RightButtonDown;
        public event EventHandler<RightClickEventArgs> RightClicked;
        public event EventHandler<RightClickEventArgs> LeftButtonDown;

        public RightClickWatcher(SynchronizationContext uiContext)
        {
            if (uiContext == null)
            {
                throw new ArgumentNullException("uiContext");
            }
            _uiContext = uiContext;
            _callback = HookCallback;
        }

        public bool IsRunning
        {
            get { return _hook != IntPtr.Zero; }
        }

        public void Start()
        {
            if (_hook != IntPtr.Zero)
            {
                return;
            }

            IntPtr module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _callback,
                module,
                0);

            if (_hook == IntPtr.Zero)
            {
                Log.Error(
                    "安装全局鼠标钩子失败，Win32 错误码：" +
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
        }

        private IntPtr HookCallback(
            int nCode,
            IntPtr wParam,
            IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int message = wParam.ToInt32();
                    if (message == NativeMethods.WM_RBUTTONDOWN)
                    {
                        NativeMethods.MSLLHOOKSTRUCT info =
                            (NativeMethods.MSLLHOOKSTRUCT)
                            Marshal.PtrToStructure(
                                lParam,
                                typeof(NativeMethods.MSLLHOOKSTRUCT));
                        _rightButtonDown = true;
                        _downPoint = info.pt;
                        // 右键按下必须在原生右键菜单弹出前同步处理，
                        // 否则 UIA 在鼠标位置读到的会是菜单窗口。
                        InvokeEvent(
                            RightButtonDown,
                            new Point(info.pt.X, info.pt.Y));
                    }
                    else if (message == NativeMethods.WM_RBUTTONUP)
                    {
                        NativeMethods.MSLLHOOKSTRUCT info =
                            (NativeMethods.MSLLHOOKSTRUCT)
                            Marshal.PtrToStructure(
                                lParam,
                                typeof(NativeMethods.MSLLHOOKSTRUCT));

                        bool isClick = _rightButtonDown &&
                            Math.Abs(info.pt.X - _downPoint.X) < 6 &&
                            Math.Abs(info.pt.Y - _downPoint.Y) < 6;
                        _rightButtonDown = false;

                        if (isClick)
                        {
                            PostEvent(
                                RightClicked,
                                new Point(info.pt.X, info.pt.Y));
                        }
                    }
                    else if (message == NativeMethods.WM_LBUTTONDOWN)
                    {
                        NativeMethods.MSLLHOOKSTRUCT info =
                            (NativeMethods.MSLLHOOKSTRUCT)
                            Marshal.PtrToStructure(
                                lParam,
                                typeof(NativeMethods.MSLLHOOKSTRUCT));

                        if (InvokeLeftButtonDown(
                            new Point(info.pt.X, info.pt.Y)))
                        {
                            _suppressLeftUp = true;
                            return (IntPtr)1;
                        }
                    }
                    else if (message == NativeMethods.WM_LBUTTONUP)
                    {
                        if (_suppressLeftUp)
                        {
                            _suppressLeftUp = false;
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("鼠标钩子回调失败", ex);
            }

            return NativeMethods.CallNextHookEx(
                _hook,
                nCode,
                wParam,
                lParam);
        }

        private bool InvokeLeftButtonDown(Point location)
        {
            EventHandler<RightClickEventArgs> handler = LeftButtonDown;
            if (handler == null)
            {
                return false;
            }

            RightClickEventArgs args = new RightClickEventArgs(location);
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                Log.Error("处理左键按下事件失败", ex);
            }
            return args.Handled;
        }

        private void InvokeEvent(
            EventHandler<RightClickEventArgs> handler,
            Point location)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new RightClickEventArgs(location));
            }
            catch (Exception ex)
            {
                Log.Error("处理右键按下事件失败", ex);
            }
        }

        private void PostEvent(
            EventHandler<RightClickEventArgs> handler,
            Point location)
        {
            if (handler == null)
            {
                return;
            }

            _uiContext.Post(delegate
            {
                try
                {
                    handler(this, new RightClickEventArgs(location));
                }
                catch (Exception ex)
                {
                    Log.Error("处理右键事件失败", ex);
                }
            }, null);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
