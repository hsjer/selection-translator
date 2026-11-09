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

            // 修饰键本身不能当主键。KeyDown 里拿到的是 ControlKey /
            // ShiftKey / Menu 这类「键码」，而 Keys.Control / Keys.Alt /
            // Keys.Shift 是修饰符标志，两者都要挡住，否则会录入出
            // 「Ctrl+ControlKey」这种根本注册不了的组合。
            if (key == Keys.None ||
                IsModifierKeyCode(key) ||
                key == Keys.Control ||
                key == Keys.Alt ||
                key == Keys.Shift)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 把 KeyDown 的「键码」翻译成 MOD_* 位。
        /// Keys.ControlKey(17) 和 Keys.Control(0x20000) 不是一个东西：
        /// 早先只判断后者，导致单独按下 Ctrl 就被当成了主键。
        /// </summary>
        public static int ModifierFlagFromKeyCode(Keys key)
        {
            switch (key)
            {
                case Keys.ControlKey:
                    return NativeMethods.MOD_CONTROL;
                case Keys.ShiftKey:
                    return NativeMethods.MOD_SHIFT;
                case Keys.Menu:
                    return NativeMethods.MOD_ALT;
                case Keys.LWin:
                case Keys.RWin:
                    return NativeMethods.MOD_WIN;
                default:
                    return 0;
            }
        }

        public static bool IsModifierKeyCode(Keys key)
        {
            return ModifierFlagFromKeyCode(key) != 0;
        }

        /// <summary>不带修饰键时，允许单独注册成全局热键的功能键。</summary>
        public static bool IsStandaloneKey(Keys key)
        {
            if (key >= Keys.F1 && key <= Keys.F12)
            {
                return true;
            }
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return true;
            }
            switch (key)
            {
                case Keys.PrintScreen:
                case Keys.Pause:
                case Keys.Insert:
                case Keys.Delete:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>录入过程中显示的修饰键前缀，例如 "Ctrl+Alt+"。</summary>
        public static string FormatModifiers(int modifiers)
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
            if (mods.Count == 0)
            {
                return "";
            }
            return string.Join("+", mods.ToArray()) + "+";
        }

        public static string Format(int modifiers, Keys key)
        {
            return FormatModifiers(modifiers) + key.ToString();
        }

        /// <summary>
        /// 一次录入结束时，按「累积的修饰键 + 主键」生成可注册的快捷键。
        /// 无修饰键时只放行功能键，避免全局拦截普通打字。
        /// </summary>
        public static bool TryCompose(
            int modifiers,
            Keys key,
            out string hotkey)
        {
            hotkey = "";
            if (key == Keys.None || IsModifierKeyCode(key))
            {
                return false;
            }
            if (modifiers == 0 && !IsStandaloneKey(key))
            {
                return false;
            }

            string candidate = Format(modifiers, key);
            int parsedModifiers;
            Keys parsedKey;
            if (!TryParse(
                candidate, out parsedModifiers, out parsedKey))
            {
                return false;
            }
            hotkey = candidate;
            return true;
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

            int flag = ModifierFlagFromKeyCode(modifierKey);
            if (flag != 0)
            {
                modifiers |= flag;
            }

            // 主键本身也是修饰键 → 还没组合完，继续等。
            if (mainKey == Keys.None || IsModifierKeyCode(mainKey) ||
                mainKey == Keys.Control || mainKey == Keys.Alt ||
                mainKey == Keys.Shift)
            {
                return false;
            }

            key = mainKey;
            return true;
        }
    }
}
