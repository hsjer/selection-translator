using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 全局界面配色中心。以前每个窗口都写死深色（<see cref="DarkUi"/> 里的
    /// 常量），用户想换个颜色只能改代码；现在所有窗口统一从这里取色，
    /// 「界面外观」里选深色 / 浅色 / 任意自定义背景色，改完立即套用到
    /// 已经打开的窗口。
    ///
    /// 配色只由一个「底色」推出来：底亮就整体走浅色（文字转深色、
    /// 分隔线转暗），底暗就走深色。强调色独立可调。
    /// </summary>
    internal static class UiTheme
    {
        public static readonly Color DefaultBase =
            Color.FromArgb(22, 23, 26);
        public static readonly Color DefaultAccent =
            Color.FromArgb(82, 145, 255);

        private static Color _baseColor = DefaultBase;
        private static Color _accentColor = DefaultAccent;
        private static bool _light;

        private static Color _window = DefaultBase;
        private static Color _surface = Color.FromArgb(29, 31, 35);
        private static Color _card = Color.FromArgb(36, 39, 44);
        private static Color _cardHover = Color.FromArgb(50, 54, 61);
        private static Color _sunken = Color.FromArgb(24, 26, 30);
        private static Color _border = Color.FromArgb(57, 61, 69);
        private static Color _borderSoft = Color.FromArgb(44, 47, 54);
        private static Color _text = Color.FromArgb(235, 237, 241);
        private static Color _textDim = Color.FromArgb(151, 157, 167);
        private static Color _textFaint = Color.FromArgb(103, 109, 119);
        private static Color _accent = DefaultAccent;
        private static Color _accentHover = Color.FromArgb(110, 165, 255);
        private static Color _accentPressed = Color.FromArgb(66, 124, 226);
        private static Color _accentSoft = Color.FromArgb(33, 50, 78);
        private static Color _accentBorder = Color.FromArgb(72, 116, 184);
        private static Color _accentInk = Color.FromArgb(226, 236, 252);
        private static Color _onAccent = Color.White;
        private static Color _danger = Color.FromArgb(240, 106, 106);
        private static Color _dangerSoft = Color.FromArgb(64, 38, 40);
        private static Color _dangerBorder = Color.FromArgb(140, 66, 68);
        private static Color _disabledFill = Color.FromArgb(44, 55, 72);

        // 旧色 -> 新色。改配色后用它把已经打开的窗口里那些「创建时
        // 一次性赋值」的 BackColor / ForeColor 平移到新配色，不用逐个
        // 窗口写刷新逻辑。
        private static Dictionary<int, Color> _remap =
            new Dictionary<int, Color>();

        /// <summary>配色变化后触发（参数无意义），窗口可据此重绘。</summary>
        public static event EventHandler Changed;

        public static Color BaseColor { get { return _baseColor; } }
        public static Color AccentColor { get { return _accentColor; } }
        public static bool IsLight { get { return _light; } }

        public static Color Window { get { return _window; } }
        public static Color Surface { get { return _surface; } }
        public static Color Card { get { return _card; } }
        public static Color CardHover { get { return _cardHover; } }
        public static Color Sunken { get { return _sunken; } }
        public static Color Border { get { return _border; } }
        public static Color BorderSoft { get { return _borderSoft; } }
        public static Color Text { get { return _text; } }
        public static Color TextDim { get { return _textDim; } }
        public static Color TextFaint { get { return _textFaint; } }
        public static Color Accent { get { return _accent; } }
        public static Color AccentHover { get { return _accentHover; } }
        public static Color AccentPressed { get { return _accentPressed; } }
        public static Color AccentSoft { get { return _accentSoft; } }
        public static Color AccentBorder { get { return _accentBorder; } }
        public static Color AccentInk { get { return _accentInk; } }
        public static Color OnAccent { get { return _onAccent; } }
        public static Color Danger { get { return _danger; } }
        public static Color DangerSoft { get { return _dangerSoft; } }
        public static Color DangerBorder { get { return _dangerBorder; } }
        public static Color DisabledFill { get { return _disabledFill; } }

        // ---- 颜色工具 ----

        public static int Clamp255(int value)
        {
            if (value < 0)
            {
                return 0;
            }
            if (value > 255)
            {
                return 255;
            }
            return value;
        }

        public static Color Shift(Color color, int delta)
        {
            return Color.FromArgb(
                color.A,
                Clamp255(color.R + delta),
                Clamp255(color.G + delta),
                Clamp255(color.B + delta));
        }

        /// <summary>t = 0 取 a，t = 1 取 b。</summary>
        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0.0)
            {
                t = 0.0;
            }
            if (t > 1.0)
            {
                t = 1.0;
            }
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static double Luminance(Color color)
        {
            return (0.2126 * color.R +
                0.7152 * color.G +
                0.0722 * color.B) / 255.0;
        }

        /// <summary>WCAG 对比度，1 ~ 21。用于自检「文字一定看得清」。</summary>
        public static double Contrast(Color a, Color b)
        {
            double la = Relative(a);
            double lb = Relative(b);
            double hi = Math.Max(la, lb);
            double lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        private static double Relative(Color c)
        {
            return 0.2126 * Channel(c.R) +
                0.7152 * Channel(c.G) +
                0.0722 * Channel(c.B);
        }

        private static double Channel(int value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        // ---- 配置 ----

        /// <summary>从配置里取颜色；空 / 非法值退回默认深色。</summary>
        public static void ConfigureFromConfig(AppConfig config)
        {
            Color baseColor = ParseColor(
                config == null ? null : config.ThemeBaseColor,
                DefaultBase);
            Color accent = ParseColor(
                config == null ? null : config.ThemeAccentColor,
                DefaultAccent);
            Configure(baseColor, accent, false);
        }

        public static Color ParseColor(string text, Color fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }
            string value = text.Trim();
            try
            {
                if (value.StartsWith("#", StringComparison.Ordinal))
                {
                    value = value.Substring(1);
                }
                if (value.Length == 6)
                {
                    int rgb = Convert.ToInt32(value, 16);
                    return Color.FromArgb(
                        (rgb >> 16) & 0xFF,
                        (rgb >> 8) & 0xFF,
                        rgb & 0xFF);
                }
                if (value.Length == 8)
                {
                    int argb = Convert.ToInt32(value, 16);
                    return Color.FromArgb(
                        (argb >> 24) & 0xFF,
                        (argb >> 16) & 0xFF,
                        (argb >> 8) & 0xFF,
                        argb & 0xFF);
                }
            }
            catch
            {
            }
            return fallback;
        }

        public static string ToHex(Color color)
        {
            return string.Format(
                "#{0:X2}{1:X2}{2:X2}",
                color.R, color.G, color.B);
        }

        public static bool IsDefault(Color baseColor, Color accent)
        {
            return baseColor.ToArgb() == DefaultBase.ToArgb() &&
                accent.ToArgb() == DefaultAccent.ToArgb();
        }

        // ---- 套用 ----

        /// <summary>
        /// 换一套配色。notify = true 时把新色套到所有已打开的窗口上
        /// （逐个控件把旧色平移成新色），并触发 <see cref="Changed"/>。
        /// </summary>
        public static void Configure(
            Color baseColor, Color accentColor, bool notify)
        {
            if (baseColor.IsEmpty)
            {
                baseColor = DefaultBase;
            }
            if (accentColor.IsEmpty)
            {
                accentColor = DefaultAccent;
            }

            // 先把「旧色 -> 新色」的映射表算出来，再改调色板；顺序反了
            // 就变成新旧同一个颜色，已打开的窗口不会变。
            Dictionary<int, Color> remap = BuildRemap(baseColor, accentColor);
            ComputePalette(baseColor, accentColor);
            _remap = remap;

            if (!notify)
            {
                return;
            }

            ApplyToOpenForms();
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        /// <summary>按底层亮度推导整套调色板。</summary>
        private static void ComputePalette(Color baseColor, Color accent)
        {
            _baseColor = baseColor;
            _accentColor = accent;
            _light = Luminance(baseColor) > 0.55;
            int dir = _light ? -1 : 1;

            _window = baseColor;
            _surface = Shift(baseColor, dir * 9);
            _card = Shift(baseColor, dir * 16);
            _cardHover = Shift(baseColor, dir * 30);
            _sunken = Shift(baseColor, dir * -6);
            _border = Shift(baseColor, dir * 40);
            _borderSoft = Shift(baseColor, dir * 22);

            _text = _light
                ? Color.FromArgb(28, 30, 34)
                : Color.FromArgb(236, 238, 242);
            _textDim = Mix(_text, _window, 0.42);
            _textFaint = Mix(_text, _window, 0.62);

            _accent = accent;
            _accentHover = _light ? Shift(accent, -18) : Shift(accent, 20);
            _accentPressed = Shift(accent, -30);
            _accentSoft = Mix(accent, _window, _light ? 0.84 : 0.80);
            _accentBorder = Mix(accent, _window, 0.45);
            _accentInk = Mix(accent, _text, 0.42);
            _onAccent = Luminance(accent) > 0.62
                ? Color.FromArgb(22, 23, 26)
                : Color.White;

            _danger = _light
                ? Color.FromArgb(196, 52, 58)
                : Color.FromArgb(240, 106, 106);
            _dangerSoft = Mix(_danger, _window, 0.82);
            _dangerBorder = Mix(_danger, _window, 0.5);
            _disabledFill = Mix(_accent, _window, _light ? 0.85 : 0.72);
        }

        private static Dictionary<int, Color> BuildRemap(
            Color baseColor, Color accent)
        {
            // 用「当前」调色板当旧值，新值单独算一份，避免中途改到全局字段。
            Color oldWindow = _window;
            Color oldSurface = _surface;
            Color oldCard = _card;
            Color oldCardHover = _cardHover;
            Color oldSunken = _sunken;
            Color oldBorder = _border;
            Color oldBorderSoft = _borderSoft;
            Color oldText = _text;
            Color oldTextDim = _textDim;
            Color oldTextFaint = _textFaint;
            Color oldAccent = _accent;
            Color oldAccentHover = _accentHover;
            Color oldAccentPressed = _accentPressed;
            Color oldAccentSoft = _accentSoft;
            Color oldAccentBorder = _accentBorder;
            Color oldAccentInk = _accentInk;
            Color oldDanger = _danger;
            Color oldDangerSoft = _dangerSoft;
            Color oldDangerBorder = _dangerBorder;
            Color oldDisabledFill = _disabledFill;

            ComputePalette(baseColor, accent);

            Dictionary<int, Color> map = new Dictionary<int, Color>();
            Add(map, oldWindow, _window);
            Add(map, oldSurface, _surface);
            Add(map, oldCard, _card);
            Add(map, oldCardHover, _cardHover);
            Add(map, oldSunken, _sunken);
            Add(map, oldBorder, _border);
            Add(map, oldBorderSoft, _borderSoft);
            Add(map, oldText, _text);
            Add(map, oldTextDim, _textDim);
            Add(map, oldTextFaint, _textFaint);
            Add(map, oldAccent, _accent);
            Add(map, oldAccentHover, _accentHover);
            Add(map, oldAccentPressed, _accentPressed);
            Add(map, oldAccentSoft, _accentSoft);
            Add(map, oldAccentBorder, _accentBorder);
            Add(map, oldAccentInk, _accentInk);
            Add(map, oldDanger, _danger);
            Add(map, oldDangerSoft, _dangerSoft);
            Add(map, oldDangerBorder, _dangerBorder);
            Add(map, oldDisabledFill, _disabledFill);
            return map;
        }

        private static void Add(
            Dictionary<int, Color> map, Color from, Color to)
        {
            int key = from.ToArgb();
            if (key == to.ToArgb())
            {
                return;
            }
            // 同一旧色被两个槽位用到时以先登记的为准，后到的忽略。
            if (!map.ContainsKey(key))
            {
                map[key] = to;
            }
        }

        public static void ApplyToOpenForms()
        {
            FormCollection forms;
            try
            {
                forms = Application.OpenForms;
            }
            catch
            {
                return;
            }
            for (int i = 0; i < forms.Count; i++)
            {
                Restyle(forms[i]);
            }
        }

        /// <summary>
        /// 把一个控件树里的旧配色平移成新配色。标准控件的 BackColor /
        /// ForeColor 是创建时一次性赋值的，只重绘不会变色；自绘控件
        /// （FlatButton / GlyphButton）另有 SurfaceColor 属性，用反射一并改。
        /// </summary>
        public static void Restyle(Control root)
        {
            if (root == null)
            {
                return;
            }
            RestyleChildren(root);
            ApplyColors(root);
            try
            {
                root.Invalidate(true);
            }
            catch
            {
            }
        }

        private static void RestyleChildren(Control parent)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control child = parent.Controls[i];
                RestyleChildren(child);
                ApplyColors(child);
                child.Invalidate();
            }
        }

        private static void ApplyColors(Control control)
        {
            Color mapped;
            if (_remap.TryGetValue(control.BackColor.ToArgb(), out mapped))
            {
                control.BackColor = mapped;
            }
            if (_remap.TryGetValue(control.ForeColor.ToArgb(), out mapped))
            {
                control.ForeColor = mapped;
            }

            // 自绘按钮的底色没有走 BackColor（为了不闪白，自己铺），
            // 用反射把 SurfaceColor 也平移过去。
            PropertyInfo surface = control.GetType().GetProperty(
                "SurfaceColor",
                BindingFlags.Public | BindingFlags.Instance);
            if (surface == null ||
                surface.PropertyType != typeof(Color) ||
                !surface.CanRead || !surface.CanWrite)
            {
                return;
            }
            try
            {
                Color current = (Color)surface.GetValue(control, null);
                if (_remap.TryGetValue(current.ToArgb(), out mapped))
                {
                    surface.SetValue(control, mapped, null);
                }
            }
            catch
            {
            }
        }
    }
}
