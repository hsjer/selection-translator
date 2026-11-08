using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SelectionTranslator
{
    /// <summary>鼠标底下的那个顶层窗口。</summary>
    internal sealed class WindowTarget
    {
        public IntPtr Handle { get; set; }

        /// <summary>屏幕坐标下的窗口外框（已用 DWM 真实边框修正）。</summary>
        public Rectangle Bounds { get; set; }

        public string Title { get; set; }

        public string ClassName { get; set; }

        public string Describe()
        {
            return string.Format(
                "hwnd=0x{0:X} class={1} size={2}x{3} title={4}",
                Handle.ToInt64(),
                ClassName,
                Bounds.Width,
                Bounds.Height,
                Title);
        }
    }

    /// <summary>
    /// 截图时「直接点一个窗口」用的拾取器：按 Z 序找鼠标下第一个
    /// 属于别的进程的可见顶层窗口。不需要钩子，也不改任何窗口状态。
    /// </summary>
    internal static class WindowPicker
    {
        /// <summary>小于这个尺寸的窗口不值得整窗截取（多半是提示条、阴影层）。</summary>
        public const int MinSide = 24;

        private static readonly string[] ShellSurfaces = new string[]
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
        };

        /// <summary>
        /// 这些类名通常是「看得见但不算窗口」的幽灵层：Windows 输入体验
        /// 的宿主、UWP 影子窗口。它们盖在任务栏上，会把真正的目标挡掉。
        /// </summary>
        private static readonly string[] GhostClasses = new string[]
        {
            "Windows.UI.Core.CoreWindow"
        };

        public static WindowTarget PickAt(Point screenPoint)
        {
            int processId = Process.GetCurrentProcess().Id;
            WindowTarget result = null;

            try
            {
                NativeMethods.EnumWindows(
                    delegate(IntPtr hwnd, IntPtr unused)
                    {
                        if (!NativeMethods.IsWindowVisible(hwnd) ||
                            NativeMethods.IsIconic(hwnd))
                        {
                            return true;
                        }

                        uint owner;
                        NativeMethods.GetWindowThreadProcessId(
                            hwnd, out owner);
                        if (owner == (uint)processId)
                        {
                            return true;
                        }

                        string className = ClassOf(hwnd);
                        if (IsShellSurface(className) ||
                            IsGhost(className) ||
                            IsCloaked(hwnd))
                        {
                            return true;
                        }

                        // 工具窗口（输入法候选、浮动提示等）不算用户的「窗口」。
                        int exStyle = NativeMethods.GetWindowLong(
                            hwnd, NativeMethods.GWL_EXSTYLE);
                        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                        {
                            return true;
                        }

                        Rectangle bounds = BoundsOf(hwnd);
                        if (bounds.Width < MinSide ||
                            bounds.Height < MinSide)
                        {
                            return true;
                        }
                        if (!bounds.Contains(screenPoint))
                        {
                            return true;
                        }

                        result = new WindowTarget();
                        result.Handle = hwnd;
                        result.Bounds = bounds;
                        result.ClassName = className;
                        result.Title = TitleOf(hwnd);
                        return false;
                    },
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Error("拾取鼠标下的窗口失败", ex);
            }

            return result;
        }

        /// <summary>按 Z 序列出可见的顶层窗口（诊断用，不会过滤桌面和任务栏）。</summary>
        public static System.Collections.Generic.List<WindowTarget> ListTopLevel(
            int limit)
        {
            System.Collections.Generic.List<WindowTarget> items =
                new System.Collections.Generic.List<WindowTarget>();
            int processId = Process.GetCurrentProcess().Id;

            try
            {
                NativeMethods.EnumWindows(
                    delegate(IntPtr hwnd, IntPtr unused)
                    {
                        if (items.Count >= limit)
                        {
                            return false;
                        }
                        if (!NativeMethods.IsWindowVisible(hwnd))
                        {
                            return true;
                        }

                        uint owner;
                        NativeMethods.GetWindowThreadProcessId(
                            hwnd, out owner);
                        if (owner == (uint)processId)
                        {
                            return true;
                        }

                        Rectangle bounds = BoundsOf(hwnd);
                        if (bounds.Width < 1 || bounds.Height < 1)
                        {
                            return true;
                        }

                        WindowTarget target = new WindowTarget();
                        target.Handle = hwnd;
                        target.Bounds = bounds;
                        target.ClassName = ClassOf(hwnd);
                        target.Title = TitleOf(hwnd);
                        items.Add(target);
                        return true;
                    },
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Error("枚举窗口失败", ex);
            }

            return items;
        }

        public static bool IsShellSurface(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            for (int i = 0; i < ShellSurfaces.Length; i++)
            {
                if (string.Equals(
                    ShellSurfaces[i],
                    className,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>DWM 隐藏（cloaked）的窗口：UWP 后台、幽灵层，肉眼看不见。</summary>
        public static bool IsCloaked(IntPtr hwnd)
        {
            try
            {
                int cloaked;
                int hr = NativeMethods.DwmGetWindowAttributeInt(
                    hwnd, 14 /* DWMWA_CLOAKED */, out cloaked, sizeof(int));
                return hr == 0 && cloaked != 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsGhost(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            for (int i = 0; i < GhostClasses.Length; i++)
            {
                if (string.Equals(
                    GhostClasses[i],
                    className,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 优先用 DWM 的 extended frame bounds：Windows 10 起
        /// GetWindowRect 会把一圈看不见的缩放手柄也算进来，
        /// 直接截会多出一圈背景色。
        /// </summary>
        public static Rectangle BoundsOf(IntPtr hwnd)
        {
            NativeMethods.RECT rect;
            try
            {
                int hr = NativeMethods.DwmGetWindowAttribute(
                    hwnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out rect,
                    Marshal.SizeOf(typeof(NativeMethods.RECT)));
                if (hr == 0)
                {
                    Rectangle dwm = Rectangle.FromLTRB(
                        rect.Left, rect.Top, rect.Right, rect.Bottom);
                    if (dwm.Width >= 1 && dwm.Height >= 1)
                    {
                        return dwm;
                    }
                }
            }
            catch
            {
                // 没有 DWM（远程桌面 / 老旧系统）时退回普通外框。
            }

            if (NativeMethods.GetWindowRect(hwnd, out rect))
            {
                return Rectangle.FromLTRB(
                    rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
            return Rectangle.Empty;
        }

        /// <summary>窗口外框是否值得整窗选中。</summary>
        public static bool IsUsable(Rectangle bounds)
        {
            return bounds.Width >= MinSide && bounds.Height >= MinSide;
        }

        /// <summary>把窗口外框夹到指定范围内（屏幕外的部分不要）。</summary>
        public static Rectangle Clamp(Rectangle bounds, Rectangle limits)
        {
            Rectangle clamped = Rectangle.Intersect(bounds, limits);
            if (clamped.Width < 1 || clamped.Height < 1)
            {
                return Rectangle.Empty;
            }
            return clamped;
        }

        private static string ClassOf(IntPtr hwnd)
        {
            StringBuilder name = new StringBuilder(256);
            int length = NativeMethods.GetClassName(hwnd, name, name.Capacity);
            return length > 0 ? name.ToString() : "";
        }

        private static string TitleOf(IntPtr hwnd)
        {
            StringBuilder text = new StringBuilder(512);
            int length = NativeMethods.GetWindowText(
                hwnd, text, text.Capacity);
            return length > 0 ? text.ToString() : "";
        }
    }
}
