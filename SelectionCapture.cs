using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace SelectionTranslator
{
    /// <summary>
    /// 右键按下时先同步记录鼠标位置处的 UIA 元素，再异步读取选区。
    /// 这样即使原生右键菜单随后弹出，读取的仍然是文本控件而不是菜单。
    /// </summary>
    internal sealed class SelectionProbe
    {
        public AutomationElement Element { get; set; }
        public AutomationElement FocusedElement { get; set; }
    }

    internal static class SelectionCapture
    {
        /// <summary>
        /// 通过 Windows UI Automation 读取鼠标位置处的选中文本。
        /// 不修改剪贴板，适合右键悬浮按钮场景。
        /// </summary>
        public static Task<string> GetSelectedTextAsync(
            int x,
            int y,
            int maxLength)
        {
            return Task.Run<string>(delegate
            {
                return GetSelectedText(x, y, maxLength);
            });
        }

        /// <summary>
        /// 在后台线程完成“定位元素 + 读取选区”。不能在低级鼠标钩子
        /// 回调里直接调用 UIA，否则可能触发 RPC_E_CANTCALLOUT_ININPUTSYNCCALL。
        /// </summary>
        public static Task<string> GetSelectedTextViaProbeAsync(
            int x,
            int y,
            int maxLength)
        {
            return Task.Run<string>(delegate
            {
                SelectionProbe probe = CaptureProbe(x, y);
                string text = ReadFromElement(probe.Element, maxLength);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
                return ReadFromElement(probe.FocusedElement, maxLength);
            });
        }

        /// <summary>
        /// 在右键按下的钩子回调中同步调用，只做一次快速的元素定位，
        /// 避免等原生右键菜单弹出后再查询。
        /// </summary>
        public static SelectionProbe CaptureProbe(int x, int y)
        {
            SelectionProbe probe = new SelectionProbe();

            try
            {
                probe.Element = AutomationElement.FromPoint(
                    new System.Windows.Point(x, y));
            }
            catch (Exception ex)
            {
                Log.Info("UIA 定位鼠标位置元素失败：" + ex.Message);
            }

            try
            {
                probe.FocusedElement = AutomationElement.FocusedElement;
            }
            catch (Exception ex)
            {
                Log.Info("UIA 读取焦点元素失败：" + ex.Message);
            }

            return probe;
        }

        /// <summary>
        /// 读取当前焦点元素的选中文本。供轮询检测选区使用，
        /// 必须在后台线程调用。
        /// </summary>
        public static string GetFocusedSelection(int maxLength)
        {
            try
            {
                return ReadFromElement(
                    AutomationElement.FocusedElement,
                    maxLength);
            }
            catch (Exception ex)
            {
                Log.Info("UIA 读取焦点选区失败：" + ex.Message);
                return "";
            }
        }

        public static Task<string> ReadSelectedTextAsync(
            SelectionProbe probe,
            int maxLength)
        {
            return Task.Run<string>(delegate
            {
                if (probe == null)
                {
                    return "";
                }

                string text = ReadFromElement(probe.Element, maxLength);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }

                return ReadFromElement(probe.FocusedElement, maxLength);
            });
        }

        private static string GetSelectedText(int x, int y, int maxLength)
        {
            try
            {
                AutomationElement element = AutomationElement.FromPoint(
                    new System.Windows.Point(x, y));
                string text = ReadFromElement(element, maxLength);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }

                // 某些应用在右键时改变了 UIA 树，退回到当前焦点元素再试一次。
                AutomationElement focused = AutomationElement.FocusedElement;
                text = ReadFromElement(focused, maxLength);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
            catch (Exception ex)
            {
                Log.Error("UIA 读取选中文本失败", ex);
            }
            return "";
        }

        private static string ReadFromElement(
            AutomationElement element,
            int maxLength)
        {
            for (int depth = 0; depth < 5 && element != null; depth++)
            {
                try
                {
                    object patternObject;
                    if (element.TryGetCurrentPattern(
                        TextPattern.Pattern,
                        out patternObject))
                    {
                        TextPattern pattern = patternObject as TextPattern;
                        if (pattern != null)
                        {
                            TextPatternRange[] ranges = pattern.GetSelection();
                            if (ranges != null && ranges.Length > 0)
                            {
                                StringBuilder builder = new StringBuilder();
                                foreach (TextPatternRange range in ranges)
                                {
                                    string part = range.GetText(
                                        maxLength <= 0 ? -1 : maxLength);
                                    if (string.IsNullOrEmpty(part))
                                    {
                                        continue;
                                    }
                                    if (builder.Length > 0)
                                    {
                                        builder.AppendLine();
                                    }
                                    builder.Append(part);
                                }

                                string result = builder.ToString().Trim();
                                if (result.Length > 0)
                                {
                                    return result;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 当前元素不支持 TextPattern，继续向父级查找。
                }

                try
                {
                    element = TreeWalker.ControlViewWalker.GetParent(element);
                }
                catch
                {
                    element = null;
                }
            }
            return "";
        }
    }
}
