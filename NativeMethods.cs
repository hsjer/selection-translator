using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SelectionTranslator
{
    internal static class NativeMethods
    {
        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_HOTKEY = 0x0312;
        public const uint WM_CANCELMODE = 0x001F;
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const uint WM_SYSKEYDOWN = 0x0104;
        public const uint WM_SYSKEYUP = 0x0105;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_RETURN = 0x0D;
        /// <summary>KBDLLHOOKSTRUCT.flags：程序注入的按键。</summary>
        public const uint LLKHF_INJECTED = 0x10;
        public const uint LLKHF_LOWER_IL_INJECTED = 0x02;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_WIN = 0x0008;
        public const int MOD_NOREPEAT = 0x4000;

        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;

        public const int VK_CONTROL = 0x11;
        public const int VK_SHIFT = 0x10;
        public const int VK_MENU = 0x12;
        public const int VK_C = 0x43;
        public const int VK_V = 0x56;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;

        public const int INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        /// <summary>wScan 直接放 UTF-16 码元，绕过键盘布局与输入法。</summary>
        public const uint KEYEVENTF_UNICODE = 0x0004;

        public const int GUI_CARETBLINKING = 0x0001;
        public const int GUI_INMENUMODE = 0x0004;
        public const int GUI_SYSTEMMENUMODE = 0x0008;
        public const int GUI_POPUPMENUMODE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>WH_KEYBOARD_LL 回调的 lParam。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;

            [FieldOffset(0)]
            public KEYBDINPUT ki;

            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// 取前台线程焦点信息。idThread 传 0 即「当前前台线程」，
        /// 因此能拿到别的进程里真正在接收键盘输入的那个控件。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        public delegate IntPtr LowLevelMouseProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        public delegate IntPtr LowLevelKeyboardProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelMouseProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        // 同一个 API 再导出一份键盘回调签名；WH_KEYBOARD_LL 的回调
        // 在线程级钩子里执行，必须和鼠标钩子分开声明委托类型。
        [DllImport(
            "user32.dll",
            EntryPoint = "SetWindowsHookExW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        public static extern IntPtr SetWindowsHookExKeyboard(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            int fsModifiers,
            int vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint nInputs,
            INPUT[] pInputs,
            int cbSize);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(
            string lpClassName,
            string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        /// <summary>
        /// 关闭当前弹出的原生右键菜单。返回是否找到并发送了关闭消息。
        /// </summary>
        public static bool CloseActiveMenu()
        {
            IntPtr menu = FindWindow("#32768", null);
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            PostMessage(
                menu,
                WM_KEYDOWN,
                new IntPtr(VK_ESCAPE),
                IntPtr.Zero);
            PostMessage(
                menu,
                WM_KEYUP,
                new IntPtr(VK_ESCAPE),
                IntPtr.Zero);
            return true;
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetGUIThreadInfo(
            uint idThread,
            ref GUITHREADINFO lpgui);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetClassName(
            IntPtr hWnd,
            StringBuilder lpClassName,
            int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int size);

        // ---- 窗口拾取（截图时直接点一个窗口）----

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(
            EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(
            IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(
            IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        /// <summary>DWM 的真实外框（不含 Win10 那圈看不见的缩放手柄）。</summary>
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            out RECT value,
            int size);

        /// <summary>同上的 int 版，用来查 DWMWA_CLOAKED。</summary>
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmGetWindowAttributeInt(
            IntPtr hwnd,
            int attribute,
            out int value,
            int size);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(
            int eventId,
            uint flags,
            IntPtr item1,
            IntPtr item2);

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int AssocQueryString(
            uint flags,
            uint str,
            string assoc,
            string extra,
            StringBuilder output,
            ref uint size);

        /// <summary>
        /// 让 Shell 自己解析一个 ProgID，用来验证「打开方式」注册是否真的
        /// 被 Windows 认出来（比直接读注册表可靠）。
        /// </summary>
        public static string QueryAssociation(uint str, string assoc)
        {
            try
            {
                uint size = 1024;
                StringBuilder buffer = new StringBuilder((int)size);
                int hr = AssocQueryString(
                    0, str, assoc, "open", buffer, ref size);
                if (hr != 0)
                {
                    return "HRESULT 0x" + hr.ToString("X8");
                }
                return buffer.ToString();
            }
            catch (Exception ex)
            {
                return "ERROR " + ex.Message;
            }
        }

        /// <summary>
        /// 通知资源管理器「文件关联变了」。写 OpenWithProgids 之后不调这个，
        /// 「打开方式」列表往往要等重启 Explorer 才刷新。
        /// </summary>
        public static void NotifyAssociationChanged()
        {
            try
            {
                SHChangeNotify(
                    SHCNE_ASSOCCHANGED,
                    SHCNF_IDLIST,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            catch
            {
                // 老系统上没有这个导出时忽略，重启 Explorer 也能生效。
            }
        }

        /// <summary>
        /// 让无边框窗口在 Windows 11 上使用系统圆角（Windows 10 会直接
        /// 返回错误码，忽略即可），同时把弹窗的标题栏设成深色。
        /// </summary>
        public static void EnableRoundedCorners(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
                int dark = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            }
            catch
            {
                // 老系统上没有 dwmapi 的这两个属性，保持默认外观。
            }
        }
    }
}
