using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal static class HotkeyParser
    {
        public const int None = 0;

        public static bool TryParse(
            string text,
            out int modifiers,
            out Keys key)
        {
            modifiers = None;
            key = Keys.None;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Split('+');
            int i = 0;
            for (; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeMethods.MOD_CONTROL;
                }
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeMethods.MOD_ALT;
                }
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeMethods.MOD_SHIFT;
                }
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeMethods.MOD_WIN;
                }
                else
                {
                    break;
                }
            }

            if (i >= parts.Length)
            {
                return false;
            }

            string keyStr = parts[i].Trim();
            try
            {
                key = (Keys)Enum.Parse(typeof(Keys), keyStr, true);
            }
            catch
            {
                return false;
            }

            // 只允许一个非修饰键。
            if (i + 1 < parts.Length)
            {
                return false;
            }

            // 不能只用修饰键。
            if (key == Keys.Control ||
                key == Keys.Alt ||
                key == Keys.Shift ||
                key == Keys.LWin ||
                key == Keys.RWin)
            {
                return false;
            }

            return true;
        }

        public static string Format(int modifiers, Keys key)
        {
            List<string> mods = new List<string>();
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0)
            {
                mods.Add("Ctrl");
            }
            if ((modifiers & NativeMethods.MOD_ALT) != 0)
            {
                mods.Add("Alt");
            }
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0)
            {
                mods.Add("Shift");
            }
            if ((modifiers & NativeMethods.MOD_WIN) != 0)
            {
                mods.Add("Win");
            }
            mods.Add(key.ToString());
            return string.Join("+", mods.ToArray());
        }

        /// <summary>
        /// 从 KeyDown 事件中提取快捷键组合。返回 false 表示按下的是
        /// 纯修饰键（还需继续等待非修饰键）。
        /// </summary>
        public static bool TryFromKeys(
            Keys modifierKey,
            Keys mainKey,
            out int modifiers,
            out Keys key)
        {
            modifiers = None;
            key = Keys.None;

            if (modifierKey == Keys.Control)
            {
                modifiers |= NativeMethods.MOD_CONTROL;
            }
            else if (modifierKey == Keys.Alt)
            {
                modifiers |= NativeMethods.MOD_ALT;
            }
            else if (modifierKey == Keys.Shift)
            {
                modifiers |= NativeMethods.MOD_SHIFT;
            }
            else if (modifierKey == Keys.LWin || modifierKey == Keys.RWin)
            {
                modifiers |= NativeMethods.MOD_WIN;
            }

            // 主键本身也是修饰键 → 还没组合完，继续等。
            if (mainKey == Keys.Control ||
                mainKey == Keys.Alt ||
                mainKey == Keys.Shift ||
                mainKey == Keys.LWin ||
                mainKey == Keys.RWin)
            {
                return false;
            }

            // Win 键不能和 LWin/RWin 同时作为修饰符（系统限制）。
            if (mainKey == Keys.LWin || mainKey == Keys.RWin)
            {
                return false;
            }

            key = mainKey;
            return true;
        }
    }
}
