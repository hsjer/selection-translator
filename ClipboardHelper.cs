using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class ClipboardSnapshot
    {
        private readonly DataObject _data;
        private readonly bool _hadData;
        private readonly bool _cannotRestore;

        public ClipboardSnapshot(DataObject data, bool hadData, bool cannotRestore)
        {
            _data = data;
            _hadData = hadData;
            _cannotRestore = cannotRestore;
        }

        /// <summary>
        /// 剪贴板里只有无法备份的数据（例如图片），此时不要发送 Ctrl+C，
        /// 否则会覆盖掉用户原来的内容且无法恢复。
        /// </summary>
        public bool CannotRestore
        {
            get { return _cannotRestore; }
        }

        public void Restore()
        {
            try
            {
                if (_hadData && _data != null)
                {
                    Clipboard.SetDataObject(_data, true);
                }
                else
                {
                    Clipboard.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Error("恢复剪贴板失败", ex);
            }
        }
    }

    internal static class ClipboardHelper
    {
        public static ClipboardSnapshot CaptureSnapshot()
        {
            try
            {
                IDataObject original = Clipboard.GetDataObject();
                if (original == null)
                {
                    return new ClipboardSnapshot(null, false, false);
                }

                // 只备份常见文本格式，避免在右键按下时遍历大图片等
                // 延迟渲染格式导致鼠标钩子阻塞。
                DataObject copy = new DataObject();
                bool hasData = false;
                string[] formats = new string[]
                {
                    DataFormats.UnicodeText,
                    DataFormats.Text,
                    DataFormats.Html,
                    DataFormats.Rtf
                };
                foreach (string format in formats)
                {
                    try
                    {
                        if (original.GetDataPresent(format, false))
                        {
                            object value = original.GetData(format, false);
                            if (value != null)
                            {
                                copy.SetData(format, value);
                                hasData = true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    if (Clipboard.ContainsFileDropList())
                    {
                        copy.SetFileDropList(Clipboard.GetFileDropList());
                        hasData = true;
                    }
                }
                catch
                {
                }

                // 只枚举格式名，不读取内容，代价很低。
                bool hasOtherFormats = false;
                try
                {
                    foreach (string format in original.GetFormats(false))
                    {
                        if (Array.IndexOf(formats, format) < 0 &&
                            !string.Equals(
                                format,
                                DataFormats.FileDrop,
                                StringComparison.Ordinal))
                        {
                            hasOtherFormats = true;
                            break;
                        }
                    }
                }
                catch
                {
                }

                return new ClipboardSnapshot(
                    hasData ? copy : null,
                    hasData,
                    hasOtherFormats && !hasData);
            }
            catch (Exception ex)
            {
                Log.Error("备份剪贴板失败", ex);
                return new ClipboardSnapshot(null, false, false);
            }
        }

        public static string GetText()
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    return Clipboard.GetText(TextDataFormat.UnicodeText);
                }
                if (Clipboard.ContainsText())
                {
                    return Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取剪贴板失败", ex);
            }
            return "";
        }

        public static void SetText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? "");
            }
            catch (Exception ex)
            {
                Log.Error("写入剪贴板失败", ex);
            }
        }

        /// <summary>
        /// 向当前前台窗口发送 Ctrl+C，读取选中文本，然后尽量恢复原剪贴板。
        /// 这是全局快捷键的兜底方案，适用于不支持 UI Automation 的应用。
        /// </summary>
        public static async Task<string> CopySelectionAsync()
        {
            ClipboardSnapshot snapshot = CaptureSnapshot();

            // 剪贴板里只有无法备份的内容（例如图片），不要发 Ctrl+C。
            if (snapshot.CannotRestore)
            {
                return "";
            }

            // 全局快捷键触发时，Ctrl/Alt 通常还处于按下状态。
            // 先等用户松开，否则再发 Ctrl+C 会变成 Ctrl+Alt+C 之类的组合键。
            await WaitForModifiersReleasedAsync();

            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            SendCtrlC();

            string text = "";
            bool changed = false;
            int waited = 0;
            while (waited < 400)
            {
                await Task.Delay(40);
                waited += 40;
                if (NativeMethods.GetClipboardSequenceNumber() != sequence)
                {
                    changed = true;
                    text = GetText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        break;
                    }
                }
            }

            // 只有剪贴板真的被改动过才恢复，避免无选区时反复写剪贴板。
            if (changed)
            {
                await Task.Delay(60);
                snapshot.Restore();
            }
            return (text ?? "").Trim();
        }

        private static async Task WaitForModifiersReleasedAsync()
        {
            for (int i = 0; i < 20; i++)
            {
                bool controlDown =
                    (NativeMethods.GetAsyncKeyState(
                        NativeMethods.VK_CONTROL) & 0x8000) != 0;
                bool shiftDown =
                    (NativeMethods.GetAsyncKeyState(
                        NativeMethods.VK_SHIFT) & 0x8000) != 0;
                bool altDown =
                    (NativeMethods.GetAsyncKeyState(
                        NativeMethods.VK_MENU) & 0x8000) != 0;

                if (!controlDown && !shiftDown && !altDown)
                {
                    return;
                }
                await Task.Delay(50);
            }
        }

        private static void SendCtrlC()
        {
            SendCtrlKey(NativeMethods.VK_C, "Ctrl+C");
        }

        /// <summary>向前台窗口发送 Ctrl+V，把剪贴板文字贴进输入框。</summary>
        public static void SendPaste()
        {
            SendCtrlKey(NativeMethods.VK_V, "Ctrl+V");
        }

        private static void SendCtrlKey(int virtualKey, string name)
        {
            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[4];

            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = NativeMethods.VK_CONTROL;

            inputs[1].type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = (ushort)virtualKey;

            inputs[2].type = NativeMethods.INPUT_KEYBOARD;
            inputs[2].U.ki.wVk = (ushort)virtualKey;
            inputs[2].U.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

            inputs[3].type = NativeMethods.INPUT_KEYBOARD;
            inputs[3].U.ki.wVk = NativeMethods.VK_CONTROL;
            inputs[3].U.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

            int size = Marshal.SizeOf(typeof(NativeMethods.INPUT));
            uint sent = NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                size);
            if (sent != inputs.Length)
            {
                Log.Error(
                    "SendInput 发送 " + name + " 失败，Win32 错误码：" +
                    Marshal.GetLastWin32Error(),
                    null);
            }
        }
    }
}
