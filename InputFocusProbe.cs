using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SelectionTranslator
{
    internal enum VoiceInsertOutcome
    {
        Inserted,
        /// <summary>说话期间用户切走了窗口，为安全起见不往新窗口打字。</summary>
        WindowChanged,
        Failed
    }

    /// <summary>
    /// 按下语音键那一刻的焦点快照。
    /// 判定采用「默认放行」：任何控件都当输入框处理，只有明确的桌面 /
    /// 菜单 / 任务栏 / 无窗口才标记为不适合直接键入。
    /// </summary>
    internal sealed class FocusTarget
    {
        public IntPtr ForegroundWindow;
        public IntPtr FocusControl;
        public uint ThreadId;
        public uint ProcessId;
        public string ClassName;
        public string Reason;
        public bool HasCaret;
        /// <summary>是否允许往这个焦点送字（默认 true，不再靠白名单）。</summary>
        public bool IsEditable;
        /// <summary>明确不是输入场景，模拟键入会被系统吞掉或误触发快捷键。</summary>
        public bool IsLikelyNonInput;

        public string Describe()
        {
            return "foreground=0x" +
                ForegroundWindow.ToInt64().ToString("X") +
                " focus=0x" + FocusControl.ToInt64().ToString("X") +
                " class=" + (ClassName ?? "") +
                " thread=" + ThreadId +
                " pid=" + ProcessId +
                " caret=" + HasCaret +
                " editable=" + IsEditable +
                " nonInput=" + IsLikelyNonInput +
                " reason=" + (Reason ?? "");
        }
    }

    /// <summary>
    /// 微信输入法式的语音上屏：不限制输入框类型，识别完成后把文字
    /// 直接送到当时的光标处，全程不弹出任何需要操作的窗口。
    /// </summary>
    internal static class InputFocusProbe
    {
        public const string MethodType = "type";
        public const string MethodPaste = "paste";

        /// <summary>
        /// 已知输入控件类名，只用于日志里说明「这是个什么控件」，
        /// 不再作为是否允许语音输入的准入条件。
        /// </summary>
        private static readonly string[] EditableTokens = new string[]
        {
            "edit",             // Edit / RichEdit20W / RichEdit50W / TEdit / ATL:Edit
            "scintilla",        // Notepad++ / SciTE
            "textbox",          // WinForms / WPF / UWP
            "tmemo",            // Delphi / C++Builder
            "ttntmemo",
            "qtextedit",
            "qlineedit",
            "qplaintextedit",
            "gtktextview",
            "notepad"
        };

        /// <summary>
        /// 常见自绘输入框的宿主窗口，同样只用于日志说明。
        /// 未知类名一律放行，避免漏判把用户挡在外面。
        /// </summary>
        private static readonly string[] HostTokens = new string[]
        {
            "chrome_renderwidgethosthwnd",
            "chrome_widgetwin",
            "mozillawindowclass",
            "mozilla",
            "windows.ui.core.corewindow",
            "applicationframewindow",
            "_wwg",             // Word 文档区
            "excel7",           // Excel 单元格
            "qt",
            "qwidget",
            "wechat",
            "weixin",
            "electron",
            "tframeclass",
            "tfmxform",
            "sunawtframe",
            "netuijuggler"
        };

        /// <summary>
        /// 唯一的排除清单：这些是桌面 / 任务栏 / 菜单等系统窗口，
        /// 往里模拟键入没有任何意义，还可能触发系统快捷键。
        /// 除此之外一律不限制。
        /// </summary>
        private static readonly string[] ExcludedTokens = new string[]
        {
            "progman",
            "workerw",
            "shell_traywnd",
            "shell_secondarytraywnd",
            "#32768",           // 弹出菜单
            "dv2controlhost",
            "windows.ui.core.coredispatcher",
            "foregroudstaging",
            "tooltips_class32"
        };

        public static string NormalizeMethod(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            return v == MethodPaste ? MethodPaste : MethodType;
        }

        public static bool IsEditableClass(string className)
        {
            string c = (className ?? "").Trim().ToLowerInvariant();
            if (c.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < EditableTokens.Length; i++)
            {
                if (c.IndexOf(EditableTokens[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsHostClass(string className)
        {
            string c = (className ?? "").Trim().ToLowerInvariant();
            if (c.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < HostTokens.Length; i++)
            {
                if (c.IndexOf(HostTokens[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsExcludedClass(string className)
        {
            string c = (className ?? "").Trim().ToLowerInvariant();
            if (c.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < ExcludedTokens.Length; i++)
            {
                if (c.IndexOf(ExcludedTokens[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 纯逻辑分类：默认放行，只有明确的非输入场景才拦截。
        /// 抽成独立函数，便于自检覆盖任意未知控件类名。
        /// </summary>
        public static void ClassifyFocus(
            string className,
            bool hasCaret,
            bool menuMode,
            bool windowOk,
            out bool nonInput,
            out string reason)
        {
            nonInput = false;
            reason = "";
            if (!windowOk)
            {
                nonInput = true;
                reason = "没有可用的前台窗口";
                return;
            }
            if (IsExcludedClass(className))
            {
                nonInput = true;
                reason = "桌面 / 菜单 / 任务栏";
                return;
            }
            if (menuMode)
            {
                nonInput = true;
                reason = "菜单正在弹出";
                return;
            }

            // 下面只决定日志里怎么写，不影响是否放行。
            if (IsEditableClass(className))
            {
                reason = "输入控件";
            }
            else if (hasCaret)
            {
                reason = "检测到文本光标";
            }
            else if (IsHostClass(className))
            {
                reason = "输入宿主窗口";
            }
            else
            {
                reason = "未知控件（默认按输入框处理）";
            }
        }

        /// <summary>是否允许把文字模拟键入到这个焦点。</summary>
        public static bool CanInsertInto(FocusTarget target, string text)
        {
            return target != null &&
                !target.IsLikelyNonInput &&
                target.ForegroundWindow != IntPtr.Zero &&
                !string.IsNullOrEmpty(text);
        }

        public static FocusTarget Probe()
        {
            FocusTarget target = new FocusTarget();
            target.ForegroundWindow = NativeMethods.GetForegroundWindow();
            target.ClassName = "";
            target.IsEditable = true;
            target.Reason = "默认放行";

            NativeMethods.GUITHREADINFO info =
                new NativeMethods.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(
                typeof(NativeMethods.GUITHREADINFO));
            bool gotInfo = NativeMethods.GetGUIThreadInfo(0, ref info);

            IntPtr focus = IntPtr.Zero;
            if (gotInfo)
            {
                focus = info.hwndFocus != IntPtr.Zero
                    ? info.hwndFocus : info.hwndActive;
                target.HasCaret =
                    info.hwndCaret != IntPtr.Zero ||
                    (info.flags & NativeMethods.GUI_CARETBLINKING) != 0;
            }
            if (focus == IntPtr.Zero)
            {
                focus = target.ForegroundWindow;
            }
            target.FocusControl = focus;

            if (focus == IntPtr.Zero)
            {
                target.IsEditable = false;
                target.IsLikelyNonInput = true;
                target.Reason = "没有前台窗口";
                return target;
            }

            uint pid;
            target.ThreadId = NativeMethods.GetWindowThreadProcessId(
                focus, out pid);
            target.ProcessId = pid;

            StringBuilder name = new StringBuilder(256);
            int length = NativeMethods.GetClassName(focus, name, name.Capacity);
            target.ClassName = length > 0 ? name.ToString() : "";

            bool menuMode = gotInfo &&
                (info.flags & (
                    NativeMethods.GUI_INMENUMODE |
                    NativeMethods.GUI_POPUPMENUMODE |
                    NativeMethods.GUI_SYSTEMMENUMODE)) != 0;
            bool windowOk = NativeMethods.IsWindow(focus) &&
                NativeMethods.IsWindowVisible(focus) &&
                NativeMethods.IsWindowEnabled(focus);

            bool nonInput;
            string reason;
            ClassifyFocus(
                target.ClassName,
                target.HasCaret,
                menuMode,
                windowOk,
                out nonInput,
                out reason);
            target.IsLikelyNonInput = nonInput;
            target.IsEditable = !nonInput;
            target.Reason = reason;
            return target;
        }

        /// <summary>
        /// 把识别文本整理成适合逐字符注入的一行纯文本：
        /// 换行/制表符换成空格，避免 \r 被输入框当成「提交」。
        /// </summary>
        public static string NormalizeForTyping(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\r' || ch == '\n' || ch == '\t')
                {
                    if (ch == '\r' && i + 1 < text.Length &&
                        text[i + 1] == '\n')
                    {
                        i++;
                    }
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    {
                        sb.Append(' ');
                    }
                }
                else if (ch >= 0x20 && ch != 0x7F)
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 构造 KEYEVENTF_UNICODE 输入序列：每个 UTF-16 码元一对按下/抬起。
        /// 中文、emoji 代理对都能原样送进输入框，且完全不碰剪贴板。
        /// </summary>
        public static NativeMethods.INPUT[] BuildUnicodeInputs(string text)
        {
            string clean = NormalizeForTyping(text);
            NativeMethods.INPUT[] inputs =
                new NativeMethods.INPUT[clean.Length * 2];
            for (int i = 0; i < clean.Length; i++)
            {
                ushort scan = (ushort)clean[i];

                inputs[i * 2].type = NativeMethods.INPUT_KEYBOARD;
                inputs[i * 2].U.ki.wVk = 0;
                inputs[i * 2].U.ki.wScan = scan;
                inputs[i * 2].U.ki.dwFlags =
                    NativeMethods.KEYEVENTF_UNICODE;

                inputs[i * 2 + 1].type = NativeMethods.INPUT_KEYBOARD;
                inputs[i * 2 + 1].U.ki.wVk = 0;
                inputs[i * 2 + 1].U.ki.wScan = scan;
                inputs[i * 2 + 1].U.ki.dwFlags =
                    NativeMethods.KEYEVENTF_UNICODE |
                    NativeMethods.KEYEVENTF_KEYUP;
            }
            return inputs;
        }

        /// <summary>
        /// 把文字送回按下语音键时那个窗口的光标处。
        /// 不做输入框类型限制；仅当焦点明确不是输入场景（桌面/菜单）时
        /// 放弃模拟键入，让调用方把结果放进剪贴板兜底。
        /// 期间只要前台窗口变了就拒绝写入，避免把字打进别的程序。
        /// </summary>
        public static async Task<VoiceInsertOutcome> InsertAsync(
            FocusTarget target,
            string text,
            string method,
            bool keepClipboardText)
        {
            if (!CanInsertInto(target, text))
            {
                return VoiceInsertOutcome.Failed;
            }

            IntPtr now = NativeMethods.GetForegroundWindow();
            if (now != target.ForegroundWindow)
            {
                Log.Info("语音输入：前台窗口已变化，放弃写入。" +
                    " now=0x" + now.ToInt64().ToString("X") +
                    " expected=0x" +
                    target.ForegroundWindow.ToInt64().ToString("X"));
                return VoiceInsertOutcome.WindowChanged;
            }

            // 语音键是 Ctrl+Alt+V：修饰键没松开时注入字符会变成快捷键。
            await WaitModifiersReleasedAsync();

            if (NativeMethods.GetForegroundWindow() !=
                target.ForegroundWindow)
            {
                return VoiceInsertOutcome.WindowChanged;
            }

            if (NormalizeMethod(method) == MethodPaste)
            {
                return await PasteAsync(text, keepClipboardText);
            }
            return await TypeAsync(text);
        }

        private static async Task<VoiceInsertOutcome> TypeAsync(string text)
        {
            // 先整体归一化再一次构造，避免分批时把词中间的空格 Trim 掉。
            NativeMethods.INPUT[] all = BuildUnicodeInputs(text);
            if (all.Length == 0)
            {
                return VoiceInsertOutcome.Failed;
            }

            int size = Marshal.SizeOf(typeof(NativeMethods.INPUT));
            const int inputsPerBatch = 96;   // 48 个 UTF-16 码元
            for (int offset = 0; offset < all.Length;
                offset += inputsPerBatch)
            {
                int count = Math.Min(
                    inputsPerBatch, all.Length - offset);
                NativeMethods.INPUT[] batch =
                    new NativeMethods.INPUT[count];
                Array.Copy(all, offset, batch, 0, count);
                uint sent = NativeMethods.SendInput(
                    (uint)batch.Length, batch, size);
                if (sent != batch.Length)
                {
                    Log.Error("SendInput 注入文字失败，Win32 错误码：" +
                        Marshal.GetLastWin32Error(), null);
                    return VoiceInsertOutcome.Failed;
                }
                if (offset + count < all.Length)
                {
                    await Task.Delay(12);
                }
            }
            Log.Info("语音输入：已模拟键入 " +
                (all.Length / 2) + " 字");
            return VoiceInsertOutcome.Inserted;
        }

        /// <summary>
        /// 兼容通道：某些程序（终端、游戏、自绘控件）不收 Unicode 注入，
        /// 改用剪贴板 + Ctrl+V，并在粘贴完成后恢复用户原来的剪贴板。
        /// </summary>
        private static async Task<VoiceInsertOutcome> PasteAsync(
            string text, bool keepClipboardText)
        {
            ClipboardSnapshot snapshot = ClipboardHelper.CaptureSnapshot();
            ClipboardHelper.SetText(text);
            if (!string.Equals(
                ClipboardHelper.GetText(), text, StringComparison.Ordinal))
            {
                Log.Error("语音输入：写入剪贴板失败", null);
                return VoiceInsertOutcome.Failed;
            }

            await Task.Delay(40);
            ClipboardHelper.SendPaste();
            await Task.Delay(160);

            if (!keepClipboardText)
            {
                // 有些程序是异步读剪贴板的，晚一点再还回去。
                await Task.Delay(400);
                snapshot.Restore();
            }
            Log.Info("语音输入：已通过剪贴板粘贴 " + text.Length + " 字");
            return VoiceInsertOutcome.Inserted;
        }

        private static async Task WaitModifiersReleasedAsync()
        {
            for (int i = 0; i < 20; i++)
            {
                if (!AnyModifierDown())
                {
                    return;
                }
                await Task.Delay(40);
            }
            ReleaseModifiers();
            await Task.Delay(30);
        }

        private static bool AnyModifierDown()
        {
            return IsDown(NativeMethods.VK_CONTROL) ||
                IsDown(NativeMethods.VK_MENU) ||
                IsDown(NativeMethods.VK_SHIFT);
        }

        private static bool IsDown(int key)
        {
            return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        }

        /// <summary>
        /// 用户一直按着 Ctrl/Alt 时主动补一个抬起事件，
        /// 否则注入的字符会被当成 Ctrl+字母快捷键。
        /// </summary>
        private static void ReleaseModifiers()
        {
            int[] keys = new int[]
            {
                NativeMethods.VK_CONTROL,
                NativeMethods.VK_MENU,
                NativeMethods.VK_SHIFT
            };
            NativeMethods.INPUT[] inputs =
                new NativeMethods.INPUT[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                inputs[i].type = NativeMethods.INPUT_KEYBOARD;
                inputs[i].U.ki.wVk = (ushort)keys[i];
                inputs[i].U.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
            }
            NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }
}
