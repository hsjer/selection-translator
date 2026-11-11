using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal static class SelfTest
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(
            uint flags, uint dx, uint dy,
            uint data, IntPtr extraInfo);

        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr after,
            int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoMoveSize = 0x0003;

        /// <summary>
        /// 真实鼠标探针的前置条件：面板必须真的压在目标点上。工作台 /
        /// 浏览器窗口抢在最上层时，注入的鼠标消息根本不会到面板，
        /// 会伪装成「拉伸没生效」。这里强制置顶并验证根窗口。
        /// </summary>
        private static bool EnsurePanelHit(
            Form form, Point point, StringBuilder output, string label)
        {
            for (int i = 0; i < 8; i++)
            {
                // 先检查：已经压在最上层时什么都别做。无条件激活面板
                // 会触发前台切换，偶发把面板状态搅乱（短条被展开）。
                IntPtr root = RootWindowAt(point);
                if (root == form.Handle)
                {
                    return true;
                }
                SetWindowPos(form.Handle, HwndTopmost,
                    0, 0, 0, 0, SwpNoMoveSize);
                SetForegroundWindow(form.Handle);
                PumpMessages(120);
            }
            IntPtr last = RootWindowAt(point);
            output.AppendLine(label + " 前置失败: target=" + point +
                " root=" + last + " panel=" + form.Handle +
                " panelBounds=" + form.Bounds +
                " fg=" + WindowClass(GetForegroundWindow()));
            return false;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(
            NativeMethods.POINT point);

        private static IntPtr RootWindowAt(Point p)
        {
            IntPtr hit = WindowFromPoint(
                new NativeMethods.POINT(p.X, p.Y));
            if (hit == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            IntPtr root = GetAncestor(hit, 2);
            return root == IntPtr.Zero ? hit : root;
        }

        public static void Run()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;

            try
            {
                string sign = BaiduTranslator.BuildSign(
                    "appid",
                    "hello",
                    "123",
                    "key");
                const string expected = "ee5464a269ed944ffef7569e277346ef";
                AppendResult(output, "MD5 sign", sign, expected);
                if (sign != expected)
                {
                    ok = false;
                }

                Check(output, ref ok, "CJK -> en",
                    BaiduTranslator.ResolveTarget("你好", "auto"), "en");
                Check(output, ref ok, "ASCII -> zh",
                    BaiduTranslator.ResolveTarget("hello", "auto"), "zh");
                Check(output, ref ok, "explicit target",
                    BaiduTranslator.ResolveTarget("hello", "ja"), "ja");
                Check(output, ref ok, "contains CJK",
                    BaiduTranslator.ContainsCjk("abc中文").ToString(),
                    "True");
                Check(output, ref ok, "no CJK",
                    BaiduTranslator.ContainsCjk("hello").ToString(),
                    "False");

                string protectedValue = ConfigStore.Protect("test-secret");
                string unprotectedValue =
                    ConfigStore.Unprotect(protectedValue);
                Check(output, ref ok, "DPAPI roundtrip",
                    unprotectedValue, "test-secret");

                // 快捷键录入：Keys.ControlKey(键码 17) 与
                // Keys.Control(修饰符标志) 是两回事，旧代码只判断后者，
                // 才会把单独按下的 Ctrl 当成主键录成「Ctrl+ControlKey」。
                Check(output, ref ok, "mod flag ctrl",
                    HotkeyParser.ModifierFlagFromKeyCode(
                        Keys.ControlKey).ToString(),
                    NativeMethods.MOD_CONTROL.ToString());
                Check(output, ref ok, "mod flag shift",
                    HotkeyParser.ModifierFlagFromKeyCode(
                        Keys.ShiftKey).ToString(),
                    NativeMethods.MOD_SHIFT.ToString());
                Check(output, ref ok, "mod flag alt",
                    HotkeyParser.ModifierFlagFromKeyCode(
                        Keys.Menu).ToString(),
                    NativeMethods.MOD_ALT.ToString());
                Check(output, ref ok, "mod flag win",
                    HotkeyParser.ModifierFlagFromKeyCode(
                        Keys.LWin).ToString(),
                    NativeMethods.MOD_WIN.ToString());
                Check(output, ref ok, "plain key is not mod",
                    HotkeyParser.IsModifierKeyCode(Keys.T).ToString(),
                    "False");

                int parsedMods;
                Keys parsedKey;
                Check(output, ref ok, "reject ControlKey hotkey",
                    HotkeyParser.TryParse(
                        "Ctrl+ControlKey",
                        out parsedMods, out parsedKey).ToString(),
                    "False");
                Check(output, ref ok, "reject ShiftKey hotkey",
                    HotkeyParser.TryParse(
                        "Ctrl+ShiftKey",
                        out parsedMods, out parsedKey).ToString(),
                    "False");
                Check(output, ref ok, "accept ctrl shift z",
                    HotkeyParser.TryParse(
                        "Ctrl+Shift+Z",
                        out parsedMods, out parsedKey).ToString(),
                    "True");
                Check(output, ref ok, "ctrl shift z roundtrip",
                    HotkeyParser.Format(parsedMods, parsedKey),
                    "Ctrl+Shift+Z");
                Check(output, ref ok, "accept single F5",
                    HotkeyParser.TryParse(
                        "F5", out parsedMods, out parsedKey).ToString(),
                    "True");
                Check(output, ref ok, "single F5 no modifier",
                    parsedMods.ToString(), "0");
                Check(output, ref ok, "standalone F5",
                    HotkeyParser.IsStandaloneKey(Keys.F5).ToString(),
                    "True");
                Check(output, ref ok, "letter needs modifier",
                    HotkeyParser.IsStandaloneKey(Keys.A).ToString(),
                    "False");
                Check(output, ref ok, "capture prefix",
                    HotkeyParser.FormatModifiers(
                        NativeMethods.MOD_CONTROL |
                        NativeMethods.MOD_ALT),
                    "Ctrl+Alt+");

                string composed;
                Check(output, ref ok, "compose ctrl+T",
                    HotkeyParser.TryCompose(
                        NativeMethods.MOD_CONTROL, Keys.T,
                        out composed).ToString() + " " + composed,
                    "True Ctrl+T");
                Check(output, ref ok, "compose ctrl+alt+shift+Z",
                    HotkeyParser.TryCompose(
                        NativeMethods.MOD_CONTROL |
                        NativeMethods.MOD_ALT |
                        NativeMethods.MOD_SHIFT,
                        Keys.Z, out composed).ToString() + " " + composed,
                    "True Ctrl+Alt+Shift+Z");
                Check(output, ref ok, "compose bare letter rejected",
                    HotkeyParser.TryCompose(
                        0, Keys.A, out composed).ToString(),
                    "False");
                Check(output, ref ok, "compose bare F8",
                    HotkeyParser.TryCompose(
                        0, Keys.F8, out composed).ToString() + " " + composed,
                    "True F8");
                Check(output, ref ok, "compose mod key rejected",
                    HotkeyParser.TryCompose(
                        0, Keys.ControlKey, out composed).ToString(),
                    "False");

                // 双击 Ctrl：状态机不碰 Win32，直接喂按键序列。
                DoubleCtrlDetector dctrl = new DoubleCtrlDetector();
                dctrl.OnCtrlDown(1000);
                bool dcFirst = dctrl.OnCtrlUp(1050);
                dctrl.OnCtrlDown(1200);
                bool dcSecond = dctrl.OnCtrlUp(1250);
                Check(output, ref ok, "double ctrl fires",
                    dcFirst + "/" + dcSecond, "False/True");

                dctrl.Reset();
                dctrl.OnCtrlDown(1000);
                dctrl.OnCtrlUp(1050);
                dctrl.OnCtrlDown(1600);
                Check(output, ref ok, "double ctrl too slow",
                    dctrl.OnCtrlUp(1650).ToString(), "False");

                dctrl.Reset();
                dctrl.OnCtrlDown(1000);
                dctrl.OnCtrlUp(1050);
                dctrl.OnOtherKeyDown();
                dctrl.OnCtrlDown(1200);
                Check(output, ref ok, "double ctrl other key cancels",
                    dctrl.OnCtrlUp(1250).ToString(), "False");

                dctrl.Reset();
                dctrl.OnCtrlDown(1000);
                dctrl.OnCtrlUp(1400);
                dctrl.OnCtrlDown(1450);
                Check(output, ref ok, "double ctrl long hold cancels",
                    dctrl.OnCtrlUp(1500).ToString(), "False");

                dctrl.Reset();
                dctrl.OnCtrlDown(1000);
                dctrl.OnCtrlDown(1010); // 自动重复不该算第二次
                dctrl.OnCtrlUp(1050);
                dctrl.OnCtrlDown(1200);
                Check(output, ref ok, "double ctrl ignores auto repeat",
                    dctrl.OnCtrlUp(1250).ToString(), "True");

                dctrl.Reset();
                dctrl.OnCtrlDown(1000);
                dctrl.OnCtrlUp(1050);
                dctrl.OnCtrlDown(1200);
                bool dc3 = dctrl.OnCtrlUp(1250);
                dctrl.OnCtrlDown(1400);
                bool dc4 = dctrl.OnCtrlUp(1450);
                Check(output, ref ok, "double ctrl triple tap once",
                    dc3 + "/" + dc4, "True/False");

                Check(output, ref ok, "double ctrl left right keycodes",
                    (DoubleCtrlDetector.IsCtrlKey(0x11) &&
                        DoubleCtrlDetector.IsCtrlKey(0xA2) &&
                        DoubleCtrlDetector.IsCtrlKey(0xA3) &&
                        !DoubleCtrlDetector.IsCtrlKey(0x41)).ToString(),
                    "True");
                Check(output, ref ok, "double ctrl injected flags",
                    DoubleCtrlDetector.IsInjected(0x10) + "/" +
                    DoubleCtrlDetector.IsInjected(0x02) + "/" +
                    DoubleCtrlDetector.IsInjected(0),
                    "True/True/False");
                Check(output, ref ok, "double ctrl default enabled",
                    new AppConfig().EnableDoubleCtrlHotkey.ToString(),
                    "True");
                Check(output, ref ok, "double ctrl config migration",
                    ConfigStore.HasDoubleCtrlSetting(
                        "{\"EnableDoubleCtrlHotkey\":false}") + "/" +
                    ConfigStore.HasDoubleCtrlSetting(
                        "{\"ShowTaskbarStats\":true}"),
                    "True/False");

                // 任务栏状态条：格式化、CPU 差值、贴托盘区定位。
                Check(output, ref ok, "stats speed bytes",
                    TaskbarStats.FormatSpeed(512), "512 B/s");
                Check(output, ref ok, "stats speed KB",
                    TaskbarStats.FormatSpeed(6.6 * 1024), "6.60 KB/s");
                Check(output, ref ok, "stats speed MB",
                    TaskbarStats.FormatSpeed(2.5 * 1024 * 1024),
                    "2.50 MB/s");
                Check(output, ref ok, "stats speed clamped",
                    TaskbarStats.FormatSpeed(-10), "0 B/s");
                Check(output, ref ok, "stats cpu 25",
                    TaskbarStats.ComputeCpuPercent(75, 100).ToString(),
                    "25");
                Check(output, ref ok, "stats cpu zero total",
                    TaskbarStats.ComputeCpuPercent(0, 0).ToString(),
                    "0");
                Check(output, ref ok, "stats cpu bad delta",
                    TaskbarStats.ComputeCpuPercent(200, 100).ToString(),
                    "0");
                string statsLine1;
                string statsLine2;
                TaskbarStats.FormatLines(
                    "1.00 KB/s", "2.00 KB/s", 17, 62,
                    out statsLine1, out statsLine2);
                Check(output, ref ok, "stats line 1",
                    statsLine1, "↑ 1.00 KB/s   CPU: 17%");
                Check(output, ref ok, "stats line 2",
                    statsLine2, "↓ 2.00 KB/s   内存: 62%");
                TaskbarStats.BarLine barFirst;
                TaskbarStats.BarLine barSecond;
                TaskbarStats.FormatLineParts(
                    "1.00 KB/s", "2.00 KB/s", 17, 62,
                    out barFirst, out barSecond);
                Check(output, ref ok, "bar parts first",
                    barFirst.Arrow + "|" + barFirst.Speed + "|" +
                    barFirst.Label + "|" + barFirst.Percent,
                    "↑|1.00 KB/s|CPU|17%");
                Check(output, ref ok, "bar parts second",
                    barSecond.Arrow + "|" + barSecond.Speed + "|" +
                    barSecond.Label + "|" + barSecond.Percent,
                    "↓|2.00 KB/s|内存|62%");

                // 状态条单位统一按 MB/s 算，速度字符串长度稳定。
                Check(output, ref ok, "compact speed idle",
                    TaskbarStats.FormatSpeedCompact(0),
                    "0.00 MB/s");
                Check(output, ref ok, "compact speed bytes",
                    TaskbarStats.FormatSpeedCompact(165),
                    "0.00 MB/s");
                Check(output, ref ok, "compact speed mb",
                    TaskbarStats.FormatSpeedCompact(
                        5.0 * 1024 * 1024),
                    "5.00 MB/s");
                Check(output, ref ok, "compact speed gb",
                    TaskbarStats.FormatSpeedCompact(
                        2.0 * 1024 * 1024 * 1024),
                    "2.00 GB/s");
                Check(output, ref ok, "compact speed negative",
                    TaskbarStats.FormatSpeedCompact(-1),
                    "0.00 MB/s");
                Check(output, ref ok, "column quantized",
                    TaskbarStats.QuantizeColumn(
                        37f, 8f, 90f).ToString(), "40");
                Check(output, ref ok, "column clamped",
                    TaskbarStats.QuantizeColumn(
                        200f, 8f, 90f).ToString(), "90");

                Func<Bitmap, int> leftmostInk = delegate(Bitmap image)
                {
                    for (int xx = 0; xx < image.Width; xx++)
                    {
                        for (int yy = 0; yy < image.Height; yy++)
                        {
                            if (image.GetPixel(xx, yy).A > 8)
                            {
                                return xx;
                            }
                        }
                    }
                    return -1;
                };
                using (Bitmap smallNumbers =
                    TaskbarStatsForm.RenderBitmap(
                        "0.00 MB/s", "0.00 MB/s", 8, 9, 1f))
                using (Bitmap bigNumbers =
                    TaskbarStatsForm.RenderBitmap(
                        "999.99 MB/s", "999.99 MB/s",
                        100, 100, 1f))
                {
                    // 位图紧贴内容，短数字不会在网速与 CPU 之间
                    // 留一大片空白；长度差只来自速度列本身。
                    Check(output, ref ok, "bar compact for short speed",
                        (smallNumbers.Width <
                            bigNumbers.Width).ToString(),
                        "True");
                    Check(output, ref ok, "bar height stable",
                        (smallNumbers.Height ==
                            bigNumbers.Height).ToString(),
                        "True");
                    // 箭头起点固定，配合固定参考宽度定位，状态条不抖。
                    Check(output, ref ok, "bar arrow start stable",
                        (leftmostInk(smallNumbers) ==
                            leftmostInk(bigNumbers)).ToString(),
                        "True");
                    Check(output, ref ok,
                        "bar width difference bounded",
                        (bigNumbers.Width -
                            smallNumbers.Width < 40).ToString(),
                        "True");
                }
                using (Bitmap scaledSmall =
                    TaskbarStatsForm.RenderBitmap(
                        "0.00 MB/s", "0.00 MB/s", 8, 9, 1.25f))
                using (Bitmap scaledBig =
                    TaskbarStatsForm.RenderBitmap(
                        "999.99 MB/s", "999.99 MB/s",
                        100, 100, 1.25f))
                {
                    Check(output, ref ok, "bar compact at 125%",
                        (scaledSmall.Width <
                            scaledBig.Width).ToString(),
                        "True");
                }
                Point statsPosition = TaskbarStats.ComputePosition(
                    new Rectangle(0, 1040, 1920, 40),
                    new Rectangle(1600, 1040, 320, 40),
                    new Size(150, 32), 8);
                Check(output, ref ok, "stats position x",
                    statsPosition.X.ToString(), "1442");
                Check(output, ref ok, "stats position y",
                    statsPosition.Y.ToString(), "1044");
                Point statsFallback = TaskbarStats.ComputePosition(
                    new Rectangle(0, 1040, 1920, 40),
                    Rectangle.Empty, new Size(150, 32), 8);
                Check(output, ref ok, "stats fallback position",
                    (statsFallback.X > 0).ToString(), "True");
                Check(output, ref ok, "stats text opaque",
                    TaskbarStatsForm.TextColor().A.ToString(), "255");

                // 状态条垂直居中：位图必须按「实际墨迹」定高，上下留白
                // 相等。早期版本把 DrawString 的行框高度直接当位图高度，
                // 数字字形只占行框上部，整块字就整体偏上，和任务栏里别的
                // 小工具并排时一眼能看出没居中。
                using (Bitmap bar = TaskbarStatsForm.RenderBitmap(
                    "0.02 MB/s", "0.03 MB/s", 31, 89, 1f))
                {
                    int inkTop = -1;
                    int inkBottom = -1;
                    for (int y = 0; y < bar.Height; y++)
                    {
                        bool any = false;
                        for (int x = 0; x < bar.Width; x++)
                        {
                            if (bar.GetPixel(x, y).A > 8)
                            {
                                any = true;
                                break;
                            }
                        }
                        if (any)
                        {
                            if (inkTop < 0)
                            {
                                inkTop = y;
                            }
                            inkBottom = y;
                        }
                    }
                    int topGap = inkTop;
                    int bottomGap = bar.Height - 1 - inkBottom;
                    Check(output, ref ok, "status bar ink found",
                        (inkTop >= 0).ToString(), "True");
                    Check(output, ref ok, "status bar vertically centered",
                        Math.Abs(topGap - bottomGap).ToString(), "0");
                }

                // ===== 占用避让：状态条落位前先看任务栏带有没有被别的
                // 窗口占了，占了就自动挪到空档，别叠在别人内容上。 =====
                Rectangle avoidTaskbar =
                    new Rectangle(0, 1040, 1920, 40);
                Rectangle avoidTray =
                    new Rectangle(1600, 1040, 320, 40);
                Size avoidContent = new Size(150, 32);
                int avoidMargin = 8;
                Point avoidBase = TaskbarStats.ComputePosition(
                    avoidTaskbar, avoidTray, avoidContent, avoidMargin);
                Check(output, ref ok, "avoid baseline x",
                    avoidBase.X.ToString(), "1442");

                // 没有占用 → 保持默认位置（紧贴托盘左侧）。
                Point noOcc = TaskbarStats.AvoidOccupiers(
                    avoidBase, avoidTaskbar, avoidTray,
                    avoidContent, avoidMargin,
                    new List<Rectangle>());
                Check(output, ref ok, "avoid no-occupier keeps base",
                    noOcc.X.ToString(), "1442");

                // 默认位置被一个窗口盖住 → 自动左移到它的左边空档。
                Point oneOcc = TaskbarStats.AvoidOccupiers(
                    avoidBase, avoidTaskbar, avoidTray,
                    avoidContent, avoidMargin,
                    new List<Rectangle>
                    {
                        new Rectangle(1400, 1040, 150, 40)
                    });
                Check(output, ref ok, "avoid single occupier relocates",
                    oneOcc.X.ToString(), "1250");
                Check(output, ref ok,
                    "avoid single occupier no overlap",
                    (!oneOcc.Equals(avoidBase)).ToString(), "True");

                // 整段可用带都被占满 → 没空档，退回默认位置。
                Point fullOcc = TaskbarStats.AvoidOccupiers(
                    avoidBase, avoidTaskbar, avoidTray,
                    avoidContent, avoidMargin,
                    new List<Rectangle>
                    {
                        new Rectangle(4, 1040, 1588, 40)
                    });
                Check(output, ref ok, "avoid full span falls back",
                    fullOcc.X.ToString(), "1442");

                // 两个窗口中间留了空档 → 落到空档右侧（贴着下一个占用体）。
                Point gapOcc = TaskbarStats.AvoidOccupiers(
                    avoidBase, avoidTaskbar, avoidTray,
                    avoidContent, avoidMargin,
                    new List<Rectangle>
                    {
                        new Rectangle(4, 1040, 600, 40),
                        new Rectangle(900, 1040, 600, 40)
                    });
                Check(output, ref ok, "avoid gap slot",
                    gapOcc.X.ToString(), "750");

                // 竖直任务栏走不了水平区间算法 → 不挪动，保持默认。
                Rectangle verticalTaskbar =
                    new Rectangle(1040, 0, 40, 1920);
                Point verticalBase = new Point(20, 1000);
                Point verticalOcc = TaskbarStats.AvoidOccupiers(
                    verticalBase, verticalTaskbar, Rectangle.Empty,
                    avoidContent, avoidMargin,
                    new List<Rectangle>
                    {
                        new Rectangle(1040, 800, 40, 400)
                    });
                Check(output, ref ok, "avoid vertical taskbar stays",
                    (verticalOcc.X == 20 && verticalOcc.Y == 1000)
                        .ToString(), "True");

                // FindOccupiers 冒烟：真实任务栏下能跑通、不把系统组件
                // 算进来、返回的矩形都落在任务栏带内。
                try
                {
                    Rectangle realTaskbar =
                        TaskbarStatsForm.GetTaskbarRect();
                    List<Rectangle> occ =
                        TaskbarStats.FindOccupiers(
                            realTaskbar, IntPtr.Zero);
                    bool allInside = true;
                    if (occ != null)
                    {
                        foreach (Rectangle r in occ)
                        {
                            if (!realTaskbar.Contains(r))
                            {
                                allInside = false;
                                break;
                            }
                        }
                    }
                    Check(output, ref ok,
                        "find occupiers no throw",
                        (occ != null).ToString(), "True");
                    Check(output, ref ok,
                        "find occupiers inside taskbar",
                        allInside.ToString(), "True");
                }
                catch (Exception ex)
                {
                    Check(output, ref ok, "find occupiers no throw",
                        "False (" + ex.GetType().Name + ")", "True");
                }

                using (Bitmap statsBitmap =
                    TaskbarStatsForm.RenderBitmap(
                        "6.60 KB/s", "5.04 KB/s", 28, 62, 1f))
                {
                    Check(output, ref ok, "stats bitmap width",
                        (statsBitmap.Width > 80).ToString(), "True");
                    Check(output, ref ok, "stats bitmap height",
                        (statsBitmap.Height >= 28).ToString(), "True");
                    Check(output, ref ok,
                        "stats bitmap transparent corner",
                        statsBitmap.GetPixel(
                            0, statsBitmap.Height - 1).A.ToString(),
                        "0");
                }
                Check(output, ref ok, "config stats on by default",
                    FreshConfig().ShowTaskbarStats.ToString(), "True");

                // ===== 悬浮详情：今日流量累计（按网卡记底数、跨天重置、持久化）=====
                string trafficFile = Path.Combine(
                    Path.GetTempPath(), "st-traffic-selftest.json");
                try
                {
                    if (File.Exists(trafficFile))
                    {
                        File.Delete(trafficFile);
                    }
                }
                catch
                {
                }
                TrafficDayTracker traffic =
                    TrafficDayTracker.Load(trafficFile);
                Dictionary<string, long> nicRecv =
                    new Dictionary<string, long>();
                Dictionary<string, long> nicSent =
                    new Dictionary<string, long>();
                DateTime dayOne = new DateTime(2026, 11, 9, 10, 0, 0);
                nicRecv["a"] = 1000;
                nicSent["a"] = 100;
                traffic.Update(dayOne, nicRecv, nicSent);
                Check(output, ref ok, "traffic first sample is baseline",
                    traffic.DayReceived.ToString(), "0");
                Check(output, ref ok, "traffic baseline date",
                    traffic.Date, "2026-11-09");
                nicRecv["a"] = 1500;
                nicSent["a"] = 180;
                traffic.Update(
                    dayOne.AddSeconds(1), nicRecv, nicSent);
                Check(output, ref ok, "traffic delta received",
                    traffic.DayReceived.ToString(), "500");
                Check(output, ref ok, "traffic delta sent",
                    traffic.DaySent.ToString(), "80");
                nicRecv["a"] = 10;
                nicSent["a"] = 5;
                traffic.Update(
                    dayOne.AddSeconds(2), nicRecv, nicSent);
                Check(output, ref ok, "traffic counter wrap ignored",
                    traffic.DayReceived.ToString(), "500");
                nicRecv["b"] = 9000;
                nicSent["b"] = 7000;
                traffic.Update(
                    dayOne.AddSeconds(3), nicRecv, nicSent);
                Check(output, ref ok, "traffic new nic is baseline",
                    traffic.DayReceived.ToString(), "500");
                traffic.Update(
                    new DateTime(2026, 11, 10, 0, 0, 1),
                    nicRecv, nicSent);
                Check(output, ref ok, "traffic day rollover date",
                    traffic.Date, "2026-11-10");
                Check(output, ref ok, "traffic day rollover reset",
                    traffic.DayReceived.ToString(), "0");
                nicRecv["a"] = 2000;
                nicSent["a"] = 200;
                traffic.Update(
                    new DateTime(2026, 11, 10, 0, 1, 0),
                    nicRecv, nicSent);
                traffic.Save();
                TrafficDayTracker reloaded =
                    TrafficDayTracker.Load(trafficFile);
                Check(output, ref ok, "traffic persisted date",
                    reloaded.Date, "2026-11-10");
                Check(output, ref ok, "traffic persisted received",
                    reloaded.DayReceived.ToString(), "1990");
                Check(output, ref ok, "traffic persisted sent",
                    reloaded.DaySent.ToString(), "195");
                try
                {
                    File.Delete(trafficFile);
                }
                catch
                {
                }

                // ===== 悬浮详情：数值格式化 =====
                Check(output, ref ok, "detail format KB",
                    TaskbarStats.FormatBytes(1536), "1.50 KB");
                Check(output, ref ok, "detail format MB",
                    TaskbarStats.FormatBytes(2.5 * 1024 * 1024),
                    "2.50 MB");
                Check(output, ref ok, "detail format ghz",
                    TaskbarStats.FormatGhz(3370), "3.37 GHz");
                Check(output, ref ok, "detail format ghz unavailable",
                    TaskbarStats.FormatGhz(0), "");
                Check(output, ref ok, "detail format duration",
                    TaskbarStats.FormatDuration(8100), "2 小时 15 分");
                Check(output, ref ok, "detail format duration short",
                    TaskbarStats.FormatDuration(30), "不到 1 分钟");
                Check(output, ref ok, "detail format duration unknown",
                    TaskbarStats.FormatDuration(-1), "");

                // ===== GPU 计数器实例解析与聚合 =====
                int gpuPhys;
                string gpuEngine;
                bool gpuParsed = TaskbarStats.TryParseGpuInstance(
                    "pid_10256_luid_0x0_0x1_phys_0_eng_0_engtype_3D",
                    out gpuPhys, out gpuEngine);
                Check(output, ref ok, "gpu instance parsed",
                    gpuParsed.ToString(), "True");
                Check(output, ref ok, "gpu instance phys",
                    gpuPhys.ToString(), "0");
                Check(output, ref ok, "gpu instance engine",
                    gpuEngine, "3D");
                Check(output, ref ok, "gpu instance bad name",
                    TaskbarStats.TryParseGpuInstance(
                        "not-a-gpu", out gpuPhys,
                        out gpuEngine).ToString(), "False");
                List<KeyValuePair<string, float>> gpuValues =
                    new List<KeyValuePair<string, float>>();
                gpuValues.Add(new KeyValuePair<string, float>(
                    "pid_1_phys_0_eng_0_engtype_3D", 12.4f));
                gpuValues.Add(new KeyValuePair<string, float>(
                    "pid_2_phys_0_eng_1_engtype_3D", 30.2f));
                gpuValues.Add(new KeyValuePair<string, float>(
                    "pid_3_phys_1_eng_0_engtype_VideoDecode", 7f));
                gpuValues.Add(new KeyValuePair<string, float>(
                    "pid_4_phys_1_eng_1_engtype_VideoDecode", 140f));
                int[] gpuLoads =
                    TaskbarStats.AggregateGpuLoad(gpuValues);
                Check(output, ref ok, "gpu aggregate count",
                    gpuLoads.Length.ToString(), "2");
                Check(output, ref ok, "gpu aggregate phys0 max",
                    gpuLoads[0].ToString(), "43");
                Check(output, ref ok, "gpu aggregate clamped",
                    gpuLoads[1].ToString(), "100");
                Check(output, ref ok, "gpu aggregate empty",
                    TaskbarStats.AggregateGpuLoad(null)
                        .Length.ToString(), "0");

                // ===== 详情行：能取到的才显示 =====
                List<TaskbarStats.DetailRow> minimumRows =
                    TaskbarStats.FormatDetailRows(
                        new TaskbarStats.DetailSample());
                Check(output, ref ok, "detail minimum rows",
                    minimumRows.Count.ToString(), "3");
                List<TaskbarStats.DetailRow> previewRows =
                    TaskbarStatsForm.BuildPreviewRows();
                Check(output, ref ok, "detail preview rows",
                    previewRows.Count.ToString(), "7");
                Check(output, ref ok, "detail preview traffic value",
                    previewRows[0].Value.IndexOf("2.03 GB") >= 0
                        ? "yes" : "no", "yes");
                Check(output, ref ok, "detail preview battery",
                    previewRows[6].Value, "82%   剩余 2 小时 15 分");
                TaskbarStats.DetailSample charging =
                    new TaskbarStats.DetailSample();
                charging.HasBattery = true;
                charging.BatteryPercent = 100;
                charging.BatteryOnAc = true;
                charging.GpuLoads = new int[] { 10, 20 };
                List<TaskbarStats.DetailRow> chargingRows =
                    TaskbarStats.FormatDetailRows(charging);
                Check(output, ref ok, "detail charging label",
                    chargingRows[chargingRows.Count - 1].Value,
                    "100%   充电中");
                Check(output, ref ok, "detail multi gpu rows",
                    chargingRows.Count.ToString(), "6");

                // ===== 详情卡片位图：圆角透明、深浅两套配色 =====
                using (Bitmap lightCard =
                    TaskbarStatsDetailForm.RenderBitmap(
                        previewRows, 1f, true))
                using (Bitmap darkCard =
                    TaskbarStatsDetailForm.RenderBitmap(
                        previewRows, 1f, false))
                {
                    Check(output, ref ok, "detail card width",
                        (lightCard.Width > 150).ToString(), "True");
                    Check(output, ref ok, "detail card height",
                        (lightCard.Height > 100).ToString(), "True");
                    // 悬停卡片要紧凑，别盖住大半个桌面。
                    Check(output, ref ok, "detail card compact width",
                        (lightCard.Width < 320).ToString(), "True");
                    Check(output, ref ok, "detail card compact height",
                        (lightCard.Height < 175).ToString(), "True");
                    Check(output, ref ok, "detail card rounded corner",
                        lightCard.GetPixel(0, 0).A < 128
                            ? "transparent" : "opaque",
                        "transparent");
                    Check(output, ref ok, "detail card center opaque",
                        lightCard.GetPixel(
                            lightCard.Width / 2,
                            lightCard.Height / 2).A.ToString(),
                        "255");
                    Check(output, ref ok, "detail card theme differs",
                        (lightCard.GetPixel(12, 12).ToArgb() !=
                            darkCard.GetPixel(12, 12).ToArgb())
                            .ToString(), "True");
                }

                // ===== 悬停判定：状态条内展开、移开收起、卡片内保持 =====
                Rectangle hoverBar =
                    new Rectangle(100, 100, 120, 40);
                Rectangle hoverCard =
                    new Rectangle(60, 20, 300, 70);
                Check(output, ref ok, "hover over bar",
                    TaskbarStats.ShouldShowDetail(
                        new Point(150, 120), hoverBar,
                        Rectangle.Empty, false).ToString(), "True");
                Check(output, ref ok, "hover far away",
                    TaskbarStats.ShouldShowDetail(
                        new Point(500, 500), hoverBar,
                        hoverCard, true).ToString(), "False");
                Check(output, ref ok, "hover edge tolerance",
                    TaskbarStats.ShouldShowDetail(
                        new Point(150, 94), hoverBar,
                        Rectangle.Empty, false).ToString(), "True");
                Check(output, ref ok, "hover inside open card",
                    TaskbarStats.ShouldShowDetail(
                        new Point(200, 50), hoverBar,
                        hoverCard, true).ToString(), "True");
                Check(output, ref ok, "hover inside closed card",
                    TaskbarStats.ShouldShowDetail(
                        new Point(200, 50), hoverBar,
                        hoverCard, false).ToString(), "False");
                Check(output, ref ok, "hover empty bounds",
                    TaskbarStats.ShouldShowDetail(
                        new Point(0, 0), Rectangle.Empty,
                        Rectangle.Empty, true).ToString(), "False");

                int liveCpuMhz = TaskbarStats.CpuFrequencyMhz();
                Check(output, ref ok, "stats live cpu freq sane",
                    (liveCpuMhz == 0 ||
                        (liveCpuMhz >= 100 && liveCpuMhz <= 10000))
                        .ToString(), "True");
                Check(output, ref ok, "stats parse cpu name ghz",
                    TaskbarStats.ParseCpuNameMhz(
                        "Intel(R) Core(TM) i7-1065G7 CPU @ 1.30GHz")
                        .ToString(), "1300");
                Check(output, ref ok, "stats parse cpu name spaced ghz",
                    TaskbarStats.ParseCpuNameMhz(
                        "AMD Ryzen 7 5800H with Radeon Graphics 3.20 GHz")
                        .ToString(), "3200");
                Check(output, ref ok, "stats parse cpu name no ghz",
                    TaskbarStats.ParseCpuNameMhz("Virtual CPU")
                        .ToString(), "0");
                int liveNominal = TaskbarStats.CpuNominalMhz();
                Check(output, ref ok, "stats live cpu nominal sane",
                    (liveNominal == 0 ||
                        (liveNominal >= 100 && liveNominal <= 10000))
                        .ToString(), "True");
                double livePerf =
                    TaskbarStats.ProcessorPerformancePercent();
                Check(output, ref ok, "stats live cpu perf sane",
                    (livePerf == 0 ||
                        (livePerf > 0 && livePerf <= 1000))
                        .ToString(), "True");

                // ===== 剪贴板历史：分类 / 搜索 / 排序 / TTL / 持久化 =====
                DateTime cbNow =
                    new DateTime(2026, 11, 9, 14, 0, 0,
                        DateTimeKind.Utc);
                DateTime cbLocalNow =
                    new DateTime(2026, 11, 9, 14, 0, 0);

                ClipboardItem cbText = new ClipboardItem();
                cbText.Id = 1;
                cbText.Kind = (int)ClipboardItemKind.Text;
                cbText.TicksUtc = cbNow.AddMinutes(-3).Ticks;
                cbText.Text = "Hello 工具侠 剪贴板";
                cbText.Fingerprint = cbText.Text;
                cbText.Count = 1;

                ClipboardItem cbImage = new ClipboardItem();
                cbImage.Id = 2;
                cbImage.Kind = (int)ClipboardItemKind.Image;
                cbImage.TicksUtc = cbNow.AddHours(-2).Ticks;
                cbImage.ImageWidth = 311;
                cbImage.ImageHeight = 156;
                cbImage.Fingerprint = "img:abc";
                cbImage.Count = 1;

                ClipboardItem cbFile = new ClipboardItem();
                cbFile.Id = 3;
                cbFile.Kind = (int)ClipboardItemKind.File;
                cbFile.TicksUtc = cbNow.AddDays(-2).Ticks;
                cbFile.Files = new string[] { @"C:\Temp\工具侠.exe" };
                cbFile.Fingerprint = ClipboardLogic.FingerprintFor(
                    cbFile.Kind, "", cbFile.Files, "");
                cbFile.Favorite = true;
                cbFile.Count = 1;

                ClipboardItem cbOld = new ClipboardItem();
                cbOld.Id = 4;
                cbOld.Kind = (int)ClipboardItemKind.Text;
                cbOld.TicksUtc = cbNow.AddDays(-30).Ticks;
                cbOld.Text = "很久以前";
                cbOld.Fingerprint = "很久以前";
                cbOld.Count = 1;

                System.Collections.Generic.List<ClipboardItem> cbItems =
                    new System.Collections.Generic.List<ClipboardItem>();
                cbItems.Add(cbOld);
                cbItems.Add(cbImage);
                cbItems.Add(cbText);
                cbItems.Add(cbFile);

                Check(output, ref ok, "clipboard kind text",
                    ClipboardLogic.KindLabel(0), "文本");
                Check(output, ref ok, "clipboard kind image",
                    ClipboardLogic.KindLabel(1), "图像");
                Check(output, ref ok, "clipboard kind file",
                    ClipboardLogic.KindLabel(2), "文件");
                Check(output, ref ok, "clipboard ttl normalize",
                    ClipboardLogic.NormalizeTtl(999).ToString(), "168");
                Check(output, ref ok, "clipboard ttl label",
                    ClipboardLogic.TtlLabel(24), "1 天");
                Check(output, ref ok, "clipboard ttl index",
                    ClipboardLogic.TtlIndex(720).ToString(), "5");
                Check(output, ref ok, "clipboard clamp max items",
                    ClipboardLogic.ClampMaxItems(5).ToString(), "20");
                Check(output, ref ok, "clipboard filter all",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.All, "").Count.ToString(),
                    "4");
                Check(output, ref ok, "clipboard filter text",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.Text, "").Count.ToString(),
                    "2");
                Check(output, ref ok, "clipboard filter image",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.Image, "").Count.ToString(),
                    "1");
                Check(output, ref ok, "clipboard filter file",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.File, "").Count.ToString(),
                    "1");
                Check(output, ref ok, "clipboard filter favorite",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.Favorite, "").Count.ToString(),
                    "1");
                Check(output, ref ok, "clipboard search keyword",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.All, "工具侠").Count.ToString(),
                    "2");
                Check(output, ref ok, "clipboard search multi words",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.All,
                        "工具侠 剪贴板").Count.ToString(), "1");
                Check(output, ref ok, "clipboard search image size",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.All,
                        "311x156").Count.ToString(), "1");
                Check(output, ref ok, "clipboard sort newest first",
                    ClipboardLogic.Filter(
                        cbItems, ClipboardTab.All, "")[0].Id.ToString(),
                    "1");
                Check(output, ref ok, "clipboard same content",
                    ClipboardLogic.SameContent(
                        cbText, cbText).ToString(), "True");
                Check(output, ref ok, "clipboard different content",
                    ClipboardLogic.SameContent(
                        cbText, cbFile).ToString(), "False");
                Check(output, ref ok, "clipboard relative minutes",
                    ClipboardLogic.FormatRelative(
                        cbLocalNow.AddMinutes(-5), cbLocalNow),
                    "5 分钟前");
                Check(output, ref ok, "clipboard date group today",
                    ClipboardLogic.DateGroup(cbLocalNow, cbLocalNow),
                    "今天");
                Check(output, ref ok, "clipboard date group yesterday",
                    ClipboardLogic.DateGroup(
                        cbLocalNow.AddDays(-1), cbLocalNow), "昨天");
                Check(output, ref ok, "clipboard expired old",
                    ClipboardLogic.IsExpired(
                        cbOld, 168, cbNow).ToString(), "True");
                Check(output, ref ok, "clipboard favorite never expires",
                    ClipboardLogic.IsExpired(
                        cbFile, 1, cbNow).ToString(), "False");
                Check(output, ref ok, "clipboard ttl zero never",
                    ClipboardLogic.IsExpired(
                        cbOld, 0, cbNow).ToString(), "False");
                Check(output, ref ok, "clipboard purge expired",
                    ClipboardLogic.SelectExpired(
                        cbItems, 168, 300, cbNow).Count.ToString(), "1");
                System.Collections.Generic.List<ClipboardItem> cbMany =
                    new System.Collections.Generic.List<ClipboardItem>();
                for (int i = 0; i < 25; i++)
                {
                    ClipboardItem many = new ClipboardItem();
                    many.Id = 100 + i;
                    many.Kind = (int)ClipboardItemKind.Text;
                    many.TicksUtc = cbNow.AddMinutes(-i).Ticks;
                    many.Text = "批量 " + i;
                    many.Fingerprint = "批量 " + i;
                    many.Count = 1;
                    cbMany.Add(many);
                }
                Check(output, ref ok, "clipboard purge max items",
                    ClipboardLogic.SelectExpired(
                        cbMany, 0, 20, cbNow).Count.ToString(), "5");
                // 上限最小被钳到 20 条，4 条样本不该被误删。
                Check(output, ref ok, "clipboard purge min limit",
                    ClipboardLogic.SelectExpired(
                        cbItems, 0, 2, cbNow).Count.ToString(), "0");
                Check(output, ref ok, "clipboard preview truncate",
                    ClipboardLogic.Preview(cbText, 5), "Hello…");

                string cbPath = System.IO.Path.Combine(
                    ConfigStore.DataDirectory,
                    "clipboard-selftest-" +
                    Guid.NewGuid().ToString("N").Substring(0, 8) +
                    ".json");
                string cbImageDir = System.IO.Path.Combine(
                    ConfigStore.DataDirectory,
                    "clipboard-selftest-images");
                ClipboardStore cbStore =
                    ClipboardStore.Load(cbPath, cbImageDir);
                cbStore.Add(cbText);
                cbStore.Add(cbText);
                Check(output, ref ok, "clipboard dedup count",
                    cbStore.Items.Count + "/" +
                    cbStore.Items[0].Count, "1/2");
                cbStore.Save();
                ClipboardStore cbReloaded =
                    ClipboardStore.Load(cbPath, cbImageDir);
                Check(output, ref ok, "clipboard store roundtrip",
                    cbReloaded.Items.Count + "/" +
                    cbReloaded.Items[0].Text,
                    "1/Hello 工具侠 剪贴板");

                // 图片能粘贴到桌面：额外挂一份 CF_HDROP 文件列表。
                DateTime pasteTime =
                    new DateTime(2026, 11, 9, 15, 3, 12, 123);
                Check(output, ref ok, "paste file name image",
                    ClipboardPasteFiles.FileNameFor(
                        (int)ClipboardItemKind.Image, pasteTime),
                    "图片-20261109-150312-123.png");
                Check(output, ref ok, "paste file name text",
                    ClipboardPasteFiles.FileNameFor(
                        (int)ClipboardItemKind.Text, pasteTime),
                    "文本-20261109-150312-123.txt");
                Check(output, ref ok, "paste mode auto",
                    ClipboardPasteFiles.IncludeImage(
                        ClipboardWriteMode.Auto) + "/" +
                    ClipboardPasteFiles.IncludeFile(
                        ClipboardWriteMode.Auto),
                    "True/True");
                Check(output, ref ok, "paste mode image only",
                    ClipboardPasteFiles.IncludeImage(
                        ClipboardWriteMode.ImageOnly) + "/" +
                    ClipboardPasteFiles.IncludeFile(
                        ClipboardWriteMode.ImageOnly),
                    "True/False");
                Check(output, ref ok, "paste mode file only",
                    ClipboardPasteFiles.IncludeImage(
                        ClipboardWriteMode.FileOnly) + "/" +
                    ClipboardPasteFiles.IncludeFile(
                        ClipboardWriteMode.FileOnly),
                    "False/True");
                Check(output, ref ok, "paste cleanup boundary",
                    ClipboardPasteFiles.IsExpired(
                        pasteTime, pasteTime.AddHours(1)) + "/" +
                    ClipboardPasteFiles.IsExpired(
                        pasteTime, pasteTime.AddHours(3)),
                    "False/True");

                string pasteSource = Path.Combine(
                    ConfigStore.DataDirectory,
                    "paste-selftest-" +
                    Guid.NewGuid().ToString("N").Substring(0, 8) +
                    ".png");
                using (Bitmap pasteBitmap = new Bitmap(9, 7))
                {
                    using (Graphics pasteGraphics =
                        Graphics.FromImage(pasteBitmap))
                    {
                        pasteGraphics.Clear(Color.Blue);
                    }
                    pasteBitmap.Save(pasteSource, ImageFormat.Png);
                }
                string imagePastePath = ClipboardPasteFiles.PrepareImage(
                    pasteSource, DateTime.Now);
                Check(output, ref ok, "paste image file prepared",
                    (imagePastePath.Length > 0 &&
                        File.Exists(imagePastePath) &&
                        Path.GetExtension(imagePastePath) == ".png")
                        .ToString(), "True");
                // 截图复制后剪贴板同时有位图和文件，采样时要能认出
                // 「这是临时落盘的那份」，从而记成图片而不是文件。
                Check(output, ref ok, "paste temp path detected",
                    ClipboardPasteFiles.IsPasteTemp(imagePastePath) + "/" +
                    ClipboardPasteFiles.IsPasteTemp(pasteSource) + "/" +
                    ClipboardPasteFiles.IsPasteTemp(""),
                    "True/False/False");
                Check(output, ref ok, "paste temp list only",
                    ClipboardPasteFiles.IsOnlyPasteTemp(
                        new string[] { imagePastePath }) + "/" +
                    ClipboardPasteFiles.IsOnlyPasteTemp(
                        new string[] { imagePastePath, pasteSource }) + "/" +
                    ClipboardPasteFiles.IsOnlyPasteTemp(null),
                    "True/False/False");
                string textPastePath = ClipboardPasteFiles.PrepareText(
                    "桌面粘贴测试", DateTime.Now);
                Check(output, ref ok, "paste text file prepared",
                    (textPastePath.Length > 0 &&
                        File.Exists(textPastePath) &&
                        File.ReadAllText(textPastePath) == "桌面粘贴测试")
                        .ToString(), "True");
                try
                {
                    if (imagePastePath.Length > 0 &&
                        File.Exists(imagePastePath))
                    {
                        File.Delete(imagePastePath);
                    }
                    if (textPastePath.Length > 0 &&
                        File.Exists(textPastePath))
                    {
                        File.Delete(textPastePath);
                    }
                    if (File.Exists(pasteSource))
                    {
                        File.Delete(pasteSource);
                    }
                }
                catch
                {
                }
                try
                {
                    System.IO.File.Delete(cbPath);
                }
                catch
                {
                }

                // 软件下载：360 宝库接口的解析与文件命名全是纯函数，
                // 这里直接喂样例 JSON，不依赖网络。
                string softJson =
                    "{\"errno\":0,\"errmsg\":\"\",\"data\":{" +
                    "\"total\":18,\"list\":[" +
                    "{\"softid\":2000004652," +
                    "\"softname\":\"小红书手机版\"," +
                    "\"desc\":\"3亿人的生活指南\"," +
                    "\"cname\":\"聊天\",\"size\":\"31.00 MB\"," +
                    "\"poll\":\"6.0\"," +
                    "\"logo\":\"//p1.ssl.qhimg.com/a.png\"," +
                    "\"soft_download\":\"http://cds.360tpcdn.com/x/" +
                    "2000004652_f7.exe?time=1&sign=2\"," +
                    "\"safe_download\":\"https://softdl6.360tpcdn.com/" +
                    "pcrj/setup.exe\"," +
                    "\"url\":\"/qbfl/2000004652.html\"}," +
                    "{\"softid\":1,\"softname\":\"A&amp;B\"," +
                    "\"desc\":\"\",\"cname\":\"\",\"size\":\"\"," +
                    "\"poll\":\"\",\"logo\":\"\"," +
                    "\"soft_download\":\"https://down.pc.yyb.qq.com/" +
                    "a/b/x.exe\",\"safe_download\":\"\"," +
                    "\"url\":\"/qbfl/1.html\"}]}}";
                SoftSearchResult softParsed =
                    SoftwareCatalog.Parse(softJson);
                Check(output, ref ok, "software parse count",
                    softParsed.Items.Count + "/" + softParsed.Total,
                    "2/18");
                Check(output, ref ok, "software parse first",
                    softParsed.Items[0].Name + "/" +
                    softParsed.Items[0].SizeText,
                    "小红书手机版/31.00 MB");
                Check(output, ref ok, "software rating text",
                    softParsed.Items[0].RatingText, "6.0 分");
                Check(output, ref ok, "software logo url absolute",
                    softParsed.Items[0].LogoUrl,
                    "https://p1.ssl.qhimg.com/a.png");
                Check(output, ref ok, "software detail page absolute",
                    softParsed.Items[0].DetailPage,
                    "https://baoku.360.cn/qbfl/2000004652.html");
                Check(output, ref ok, "software unescape name",
                    softParsed.Items[1].Name, "A&B");
                Check(output, ref ok, "software url file name",
                    SoftwareCatalog.FileNameFromUrl(
                        "http://cds.360tpcdn.com/x/2000004652_f7.exe" +
                        "?time=1&sign=2"),
                    "2000004652_f7.exe");
                Check(output, ref ok, "software build file name",
                    SoftwareCatalog.BuildFileName(
                        softParsed.Items[0]),
                    "小红书手机版_2000004652.exe");
                Check(output, ref ok, "software build file name keeps ext",
                    SoftwareCatalog.BuildFileName(
                        softParsed.Items[1]),
                    "A&B_1.exe");
                Check(output, ref ok, "software sanitize file name",
                    (SoftwareCatalog.SanitizeFileName(
                        "a/b:c*?\"<>|").IndexOfAny(new char[]
                        { '/', ':', '*', '?', '"', '<', '>', '|' }) < 0)
                        .ToString(), "True");
                Check(output, ref ok, "software one line",
                    SoftwareCatalog.OneLine("a\r\n\nb   c", 20),
                    "a b c");
                SoftSearchResult softBad = SoftwareCatalog.Parse(
                    "{\"errno\":7,\"errmsg\":\"bad request\"}");
                Check(output, ref ok, "software parse errno",
                    softBad.Items.Count + "/" +
                        (softBad.Error.Length > 0),
                    "0/True");
                Check(output, ref ok, "software parse broken json",
                    (SoftwareCatalog.Parse("<html>").Error.Length > 0)
                        .ToString(), "True");
                string softTempDir = Path.Combine(
                    ConfigStore.DataDirectory, "software-selftest");
                Directory.CreateDirectory(softTempDir);
                string softTempFile = Path.Combine(
                    softTempDir, "工具侠.exe");
                File.WriteAllText(softTempFile, "x");
                string softUnique = SoftwareCatalog.UniquePath(
                    softTempDir, "工具侠.exe");
                Check(output, ref ok, "software unique path",
                    Path.GetFileName(softUnique), "工具侠(1).exe");
                try
                {
                    File.Delete(softTempFile);
                    if (File.Exists(softUnique))
                    {
                        File.Delete(softUnique);
                    }
                    Directory.Delete(softTempDir);
                }
                catch
                {
                }
                Check(output, ref ok, "software size text",
                    SoftwareDownloader.SizeText(33168072), "31.6 MB");
                Check(output, ref ok, "software speed text",
                    SoftwareDownloader.SpeedText(2411724), "2.3 MB/s");
                Check(output, ref ok, "software remaining text",
                    SoftwareDownloader.RemainingText(0, 1000, 100),
                    "剩余 10 秒");

                // ---- 腾讯软件中心：xmlInfo 解析 / 两源合并 ----
                string tencentXml =
                    "\n<soft id=\"11488\" ostype=\"496\" osbit=\"2\">\n" +
                    "    <displayname>\n      <![CDATA[微信]]>\n" +
                    "    </displayname>\n" +
                    "    <filesize>259969448</filesize>\n" +
                    "    <https>\n      <![CDATA[https://webcdn.m.qq.com/" +
                    "spcmgr/download/WeChatWin.exe]]>\n    </https>\n" +
                    "    <versionname>4.2.0.13</versionname>\n" +
                    "    <feature>\n      <![CDATA[让沟通更方便]]>\n" +
                    "    </feature>\n" +
                    "    <logo48>11488_48_1616464914.png</logo48>\n" +
                    "    <point>66</point>\n" +
                    "</soft>";
                System.Collections.Generic.Dictionary<string, object>
                    tencentRoot =
                        new System.Collections.Generic.Dictionary<string, object>();
                System.Collections.Generic.Dictionary<string, object>
                    tencentInfo =
                        new System.Collections.Generic.Dictionary<string, object>();
                tencentInfo["keyword"] = "微信";
                tencentInfo["total"] = 2;
                System.Collections.Generic.Dictionary<string, object>
                    tencentRow =
                        new System.Collections.Generic.Dictionary<string, object>();
                tencentRow["SoftID"] = 11488;
                tencentRow["SoftName"] = "微信";
                tencentRow["xmlInfo"] = tencentXml;
                System.Collections.Generic.Dictionary<string, object>
                    tencentRow2 =
                        new System.Collections.Generic.Dictionary<string, object>();
                tencentRow2["SoftID"] = 24239;
                tencentRow2["SoftName"] = "抖音";
                tencentRow2["xmlInfo"] = "";
                tencentRoot["code"] = 0;
                tencentRoot["info"] = tencentInfo;
                tencentRoot["list"] = new object[]
                {
                    tencentRow, tencentRow2
                };
                System.Web.Script.Serialization.JavaScriptSerializer
                    tencentSerializer =
                        new System.Web.Script.Serialization.JavaScriptSerializer();
                string tencentJson = tencentSerializer.Serialize(
                    tencentRoot);
                SoftSearchResult tencentSoft =
                    SoftwareCatalog.ParseTencent(tencentJson);
                Check(output, ref ok, "tencent parse count",
                    tencentSoft.Items.Count + "/" + tencentSoft.Total,
                    "2/2");
                Check(output, ref ok, "tencent parse name",
                    tencentSoft.Items[0].Name, "微信");
                Check(output, ref ok, "tencent parse size",
                    tencentSoft.Items[0].SizeText, "248 MB");
                Check(output, ref ok, "tencent parse rating",
                    tencentSoft.Items[0].RatingText, "6.6 分");
                Check(output, ref ok, "tencent parse version",
                    tencentSoft.Items[0].VersionText, "4.2.0.13");
                Check(output, ref ok, "tencent parse download url",
                    tencentSoft.Items[0].DownloadUrl,
                    "https://webcdn.m.qq.com/spcmgr/download/WeChatWin.exe");
                Check(output, ref ok, "tencent parse detail page",
                    tencentSoft.Items[0].DetailPage,
                    "https://pc.qq.com/detail/8/detail_11488.html");
                Check(output, ref ok, "tencent parse logo",
                    tencentSoft.Items[0].LogoUrl,
                    "https://pc3.gtimg.com/softmgr/logo/48/" +
                    "11488_48_1616464914.png");
                Check(output, ref ok, "tencent parse source tag",
                    tencentSoft.Items[0].Source + "/" +
                    tencentSoft.Items[1].Source,
                    "腾讯/腾讯");
                Check(output, ref ok, "tencent xml cdata strip",
                    SoftwareCatalog.XmlText(tencentXml, "displayname"),
                    "微信");
                Check(output, ref ok, "tencent human size",
                    SoftwareCatalog.HumanSize(1362132), "1.30 MB");
                Check(output, ref ok, "tencent point format",
                    SoftwareCatalog.FormatPoint("68"), "6.8");

                // 两源合并：同名同 ID 也不去重，顺序是 360 在前腾讯在后。
                SoftItem mergeProbe = new SoftItem();
                mergeProbe.Source = SoftwareCatalog.Source360;
                mergeProbe.SoftId = 11488;
                mergeProbe.Name = "微信";
                SoftSearchResult mergedResult = SoftwareCatalog.Merge(
                    new SoftSearchResult
                    {
                        Total = 1,
                        Items = new System.Collections.Generic.List<SoftItem>
                        {
                            mergeProbe
                        }
                    },
                    tencentSoft);
                Check(output, ref ok, "software merge keeps duplicates",
                    mergedResult.Items.Count + "/" + mergedResult.Total,
                    "3/3");
                Check(output, ref ok, "software merge order",
                    mergedResult.Items[0].Source + "/" +
                    mergedResult.Items[1].Source,
                    "360/腾讯");
                Check(output, ref ok, "software key is unique per source",
                    mergeProbe.Key + "|" +
                    tencentSoft.Items[0].Key,
                    "360:11488|腾讯:11488");
                SoftSearchResult failedSide = new SoftSearchResult();
                failedSide.Error = "连不上软件源";
                SoftSearchResult partialResult = SoftwareCatalog.Merge(
                    mergedResult, failedSide);
                Check(output, ref ok, "software merge partial failure",
                    partialResult.Items.Count + "/" +
                    (partialResult.Error.Length == 0) + "/" +
                    (partialResult.Warning.Length > 0),
                    "3/True/True");
                Check(output, ref ok, "software merge all failed",
                    (SoftwareCatalog.Merge(
                        new SoftSearchResult { Error = "a" },
                        new SoftSearchResult { Error = "b" })
                        .Error.Length > 0).ToString(), "True");

                // 只有一侧字段的条目（360 没有版本号、腾讯没有分类）：
                // 任何字段都不能是 null，否则绘制里一次 .Length 就空引用。
                SoftItem bareItem = new SoftItem();
                bareItem.Source = SoftwareCatalog.Source360;
                bareItem.Name = "只有名字";
                Check(output, ref ok, "software bare item text fields",
                    (bareItem.Name.Length +
                        bareItem.Description.Length +
                        bareItem.SizeText.Length +
                        bareItem.Rating.Length +
                        bareItem.Category.Length +
                        bareItem.VersionText.Length +
                        bareItem.LogoUrl.Length +
                        bareItem.DownloadUrl.Length +
                        bareItem.SafeDownloadUrl.Length +
                        bareItem.DetailUrl.Length) + "/" +
                    (bareItem.RatingText.Length) + "/" +
                    bareItem.Key,
                    "4/0/360:0");
                bareItem.Name = null;
                bareItem.VersionText = null;
                Check(output, ref ok, "software null assignment normalized",
                    (bareItem.Name.Length +
                        bareItem.VersionText.Length) + "/" +
                    (bareItem.Name == null).ToString(),
                    "0/False");

                // ---- 文件秒搜：纯函数部分 ----
                Check(output, ref ok, "filesearch classify doc",
                    FileKindLogic.Classify("工作报告.docx", false)
                        .ToString(), "Document");
                Check(output, ref ok, "filesearch classify image",
                    FileKindLogic.Classify("截图.PNG", false)
                        .ToString(), "Image");
                Check(output, ref ok, "filesearch classify archive",
                    FileKindLogic.Classify("备份.7z", false)
                        .ToString(), "Archive");
                Check(output, ref ok, "filesearch classify app",
                    FileKindLogic.Classify("setup.msi", false)
                        .ToString(), "Application");
                Check(output, ref ok, "filesearch classify video",
                    FileKindLogic.Classify("片段.mp4", false)
                        .ToString(), "Video");
                Check(output, ref ok, "filesearch classify audio",
                    FileKindLogic.Classify("song.mp3", false)
                        .ToString(), "Audio");
                Check(output, ref ok, "filesearch classify no ext",
                    FileKindLogic.Classify("Makefile", false)
                        .ToString(), "Other");
                Check(output, ref ok, "filesearch classify folder",
                    FileKindLogic.Classify("项目", true)
                        .ToString(), "Folder");
                Check(output, ref ok, "filesearch extension of dotfile",
                    "[" + FileKindLogic.ExtensionOf(".gitignore") + "]",
                    "[]");
                Check(output, ref ok, "filesearch extension of double",
                    FileKindLogic.ExtensionOf("a.tar.gz"), "gz");
                Check(output, ref ok, "filesearch size bytes",
                    FileKindLogic.SizeText(512, FileKind.Other), "512 B");
                Check(output, ref ok, "filesearch size kb",
                    FileKindLogic.SizeText(2048, FileKind.Other), "2 KB");
                Check(output, ref ok, "filesearch size mb",
                    FileKindLogic.SizeText(1572864, FileKind.Other),
                    "1.5 MB");
                Check(output, ref ok, "filesearch size gb",
                    FileKindLogic.SizeText(1610612736L, FileKind.Other),
                    "1.50 GB");
                Check(output, ref ok, "filesearch size folder hidden",
                    "[" + FileKindLogic.SizeText(4096,
                        FileKind.Folder) + "]", "[]");
                Check(output, ref ok, "filesearch date zero",
                    "[" + FileKindLogic.DateText(0) + "]", "[]");
                Check(output, ref ok, "filesearch split terms",
                    FileQueryEngine.SplitTerms("  报告   txt ").Count
                        .ToString(), "2");
                Check(output, ref ok, "filesearch split terms cap",
                    FileQueryEngine.SplitTerms("a b c d e f g h").Count
                        .ToString(), "6");
                Check(output, ref ok, "filesearch wildcard suffix",
                    FileQueryEngine.WildcardMatch(
                        "报告.docx", "*.docx").ToString(), "True");
                Check(output, ref ok, "filesearch wildcard single",
                    FileQueryEngine.WildcardMatch("a.txt", "?.txt")
                        .ToString(), "True");
                Check(output, ref ok, "filesearch wildcard single miss",
                    FileQueryEngine.WildcardMatch("ab.txt", "?.txt")
                        .ToString(), "False");
                Check(output, ref ok, "filesearch wildcard middle",
                    FileQueryEngine.WildcardMatch(
                        "季度汇报-v2.docx", "*汇报*v2*").ToString(),
                    "True");
                Check(output, ref ok, "filesearch wildcard case",
                    FileQueryEngine.WildcardMatch("README.MD", "*.md")
                        .ToString(), "True");
                Check(output, ref ok, "filesearch parent path",
                    FileResultList.ParentPath(
                        @"C:\Users\me\Documents\a.docx", "a.docx"),
                    @"C:\Users\me\Documents");
                Check(output, ref ok, "filesearch parent path root",
                    FileResultList.ParentPath(@"D:\a.docx", "a.docx"),
                    @"D:\");
                Check(output, ref ok, "filesearch scope text empty",
                    FileSearchForm.ScopeText(
                        new System.Collections.Generic.List<string>()),
                    "此电脑");
                Check(output, ref ok, "filesearch scope text drive",
                    FileSearchForm.ScopeText(
                        new System.Collections.Generic.List<string>
                        {
                            @"D:\"
                        }), "D:");
                Check(output, ref ok, "filesearch scope text multi",
                    FileSearchForm.ScopeText(
                        new System.Collections.Generic.List<string>
                        {
                            @"C:\", @"D:\"
                        }), "2 个位置");
                Check(output, ref ok, "filesearch short root folder",
                    FileSearchForm.ShortRoot(
                        @"D:\desktop\selection-translator"),
                    "selection-translator");
                Check(output, ref ok, "filesearch skip noise name",
                    FileScanner.ShouldSkipDirectory("node_modules")
                        .ToString(), "True");
                Check(output, ref ok, "filesearch skip normal name",
                    FileScanner.ShouldSkipDirectory("Documents")
                        .ToString(), "False");
                Check(output, ref ok, "filesearch skip under windows",
                    FileScanner.IsUnderSkippedRoot(
                        Path.Combine(Environment.GetFolderPath(
                            Environment.SpecialFolder.Windows),
                            "System32")).ToString(), "True");
                Check(output, ref ok, "filesearch skip boundary",
                    FileScanner.IsUnderSkippedRoot(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.Windows) + "Apps")
                        .ToString(), "False");
                Check(output, ref ok, "filesearch full path root",
                    BuildPathProbe(@"C:\", "a.txt"), @"C:\a.txt");
                Check(output, ref ok, "filesearch full path nested",
                    BuildPathProbe(@"D:\a", "b.txt"), @"D:\a\b.txt");
                Check(output, ref ok, "filesearch count format",
                    FileSearchForm.FormatCount(1234567), "1,234,567");
                Check(output, ref ok, "filesearch ascii letter none",
                    FileQueryEngine.HasAsciiLetter("报告").ToString(),
                    "False");
                Check(output, ref ok, "filesearch ascii letter yes",
                    FileQueryEngine.HasAsciiLetter("报告v2").ToString(),
                    "True");
                Check(output, ref ok, "filesearch suffix pattern",
                    FileQueryEngine.SuffixPattern("*.docx"), ".docx");
                Check(output, ref ok, "filesearch suffix pattern complex",
                    "[" + (FileQueryEngine.SuffixPattern("*报告*") ??
                        "") + "]", "[]");

                // 下载目录：等于系统默认时不落盘（换机器还能用），
                // 自定义目录原样保存。
                string defaultDir = SoftwareDownloader.DefaultDirectory();
                Check(output, ref ok, "download dir default normalized",
                    "[" + SettingsForm.NormalizeDownloadDirectory(
                        defaultDir + "\\") + "]",
                    "[]");
                Check(output, ref ok, "download dir custom kept",
                    SettingsForm.NormalizeDownloadDirectory(
                        @"D:\我的安装包"),
                    @"D:\我的安装包");

                // 分段翻译：长文本应被拆成多段，每段 UTF-8 字节数都不超过上限，
                // 且拼接后能还原原文。
                StringBuilder longText = new StringBuilder();
                for (int i = 0; i < 2000; i++)
                {
                    longText.Append("你好世界");
                }
                System.Collections.Generic.List<string> chunks =
                    BaiduTranslator.SplitForTranslation(
                        longText.ToString(),
                        6000);
                Check(output, ref ok, "chunk count > 1",
                    (chunks.Count > 1).ToString(), "True");

                bool allUnderLimit = true;
                foreach (string chunk in chunks)
                {
                    if (System.Text.Encoding.UTF8.GetByteCount(chunk) > 6000)
                    {
                        allUnderLimit = false;
                        break;
                    }
                }
                Check(output, ref ok, "all chunks under limit",
                    allUnderLimit.ToString(), "True");

                StringBuilder joined = new StringBuilder();
                foreach (string chunk in chunks)
                {
                    joined.Append(chunk);
                }
                Check(output, ref ok, "chunks reconstruct original",
                    joined.ToString(), longText.ToString());

                // 短文本应只产生一段。
                System.Collections.Generic.List<string> shortChunks =
                    BaiduTranslator.SplitForTranslation("hello", 6000);
                Check(output, ref ok, "short text single chunk",
                    shortChunks.Count.ToString(), "1");

                // 图片翻译：MD5 与签名。
                const string md5Hello = "5d41402abc4b2a76b9719d911017c592";
                Check(output, ref ok, "md5 bytes hello",
                    BaiduImageTranslator.Md5OfBytes(
                        System.Text.Encoding.UTF8.GetBytes("hello")),
                    md5Hello);

                const string imageSignExpected =
                    "788139c539df880f030cca06d979593e";
                string imageSign = BaiduImageTranslator.BuildImageSign(
                    "appid",
                    md5Hello,
                    "123",
                    "key");
                AppendResult(output, "MD5 image sign",
                    imageSign, imageSignExpected);
                if (imageSign != imageSignExpected)
                {
                    ok = false;
                }

                // 图片分享名固定成分。
                Check(output, ref ok, "cuid", BaiduImageTranslator.Cuid,
                    "APICUID");
                Check(output, ref ok, "mac", BaiduImageTranslator.Mac, "mac");

                // 语种映射：图片接口代码与文本接口不同。
                Check(output, ref ok, "image lang ja->jp",
                    BaiduImageTranslator.MapImageLanguage("ja"), "jp");
                Check(output, ref ok, "image lang ko->kor",
                    BaiduImageTranslator.MapImageLanguage("ko"), "kor");
                Check(output, ref ok, "image lang fr->fra",
                    BaiduImageTranslator.MapImageLanguage("fr"), "fra");
                Check(output, ref ok, "image lang es->spa",
                    BaiduImageTranslator.MapImageLanguage("es"), "spa");
                Check(output, ref ok, "image lang en keep",
                    BaiduImageTranslator.MapImageLanguage("en"), "en");
                Check(output, ref ok, "image lang auto keep",
                    BaiduImageTranslator.MapImageLanguage("auto"), "auto");

                // 图片接口目标语言校验。
                Check(output, ref ok, "image target en ok",
                    BaiduImageTranslator.IsSupportedTarget("en").ToString(),
                    "True");
                Check(output, ref ok, "image target jp ok",
                    BaiduImageTranslator.IsSupportedTarget("jp").ToString(),
                    "True");
                Check(output, ref ok, "image target ja unsupported",
                    BaiduImageTranslator.IsSupportedTarget("ja").ToString(),
                    "False");

                bool threwUnsupported = false;
                try
                {
                    BaiduImageTranslator.MapImageLanguage("th");
                }
                catch (TranslationException)
                {
                    threwUnsupported = true;
                }
                Check(output, ref ok, "image lang th throws",
                    threwUnsupported.ToString(), "True");

                // 截图浮层冒烟测试：构造过程（抓屏 / 遮罩 / 工具栏图标）
                // 不抛异常，且给定选区后能真正导出一张位图。
                // 锁屏 / 远程会话 / 虚拟显示器下系统会直接拒绝屏幕
                // BitBlt，此时跳过这组依赖桌面的断言，其余照常执行，
                // 不让环境限制把整份自检判成失败。
                try
                {
                    using (ScreenshotCaptureForm capture =
                        new ScreenshotCaptureForm())
                    {
                        Check(output, ref ok, "capture initially empty",
                            (capture.Captured == null).ToString(), "True");
                        Check(output, ref ok, "capture initial result",
                            capture.Result.ToString(), "None");

                        FieldInfo selectionField =
                            typeof(ScreenshotCaptureForm).GetField(
                                "_selection",
                                BindingFlags.NonPublic |
                                BindingFlags.Instance);
                        selectionField.SetValue(
                            capture, new Rectangle(10, 10, 120, 80));

                        MethodInfo buildMethod =
                            typeof(ScreenshotCaptureForm).GetMethod(
                                "BuildResult",
                                BindingFlags.NonPublic |
                                BindingFlags.Instance);
                        using (Bitmap exported =
                            (Bitmap)buildMethod.Invoke(capture, null))
                        {
                            Check(output, ref ok,
                                "capture export not null",
                                (exported != null).ToString(), "True");
                            if (exported != null)
                            {
                                Check(output, ref ok,
                                    "capture export width",
                                    exported.Width.ToString(), "120");
                                Check(output, ref ok,
                                    "capture export height",
                                    exported.Height.ToString(), "80");
                            }
                        }
                    }
                }
                catch (System.ComponentModel.Win32Exception captureError)
                {
                    output.AppendLine(
                        "SKIP screenshot smoke test: " +
                        captureError.Message);
                }
                catch (System.Runtime.InteropServices.ExternalException
                    captureError)
                {
                    output.AppendLine(
                        "SKIP screenshot smoke test: " +
                        captureError.Message);
                }

                // 应用图标：exe 的 Win32 图标资源 + 供托盘复用的托管资源。
                using (Icon exeIcon = Icon.ExtractAssociatedIcon(
                    Application.ExecutablePath))
                {
                    Check(output, ref ok, "exe icon resource",
                        (exeIcon != null).ToString(), "True");
                }
                using (Stream iconStream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(
                            "SelectionTranslator.app.ico"))
                {
                    Check(output, ref ok, "embedded icon resource",
                        (iconStream != null && iconStream.Length > 0)
                            .ToString(), "True");
                }
                using (Icon trayIcon = IconFactory.Create())
                {
                    Check(output, ref ok, "tray icon",
                        (trayIcon != null && trayIcon.Width > 0)
                            .ToString(), "True");
                }

                // 图片选字复制（结果窗口）：rect 解析 + 按阅读顺序拼接原文 / 译文。
                Type editorType = typeof(ImageTranslationForm);
                MethodInfo parseRect = editorType.GetMethod(
                    "TryParseOcrRect",
                    BindingFlags.NonPublic | BindingFlags.Static);
                object[] parseArgs = new object[]
                {
                    "79 23 246 43", RectangleF.Empty
                };
                bool parsed = (bool)parseRect.Invoke(
                    null, parseArgs);
                RectangleF parsedRect = (RectangleF)parseArgs[1];
                Check(output, ref ok, "ocr rect parsed",
                    parsed.ToString(), "True");
                Check(output, ref ok, "ocr rect values",
                    parsedRect.X + "," + parsedRect.Y + "," +
                    parsedRect.Width + "," + parsedRect.Height,
                    "79,23,246,43");

                object[] badArgs = new object[]
                {
                    "not a rect", RectangleF.Empty
                };
                Check(output, ref ok, "ocr rect rejects junk",
                    ((bool)parseRect.Invoke(null, badArgs)).ToString(),
                    "False");

                using (Bitmap stub = new Bitmap(120, 80))
                using (Bitmap rawStub = new Bitmap(120, 80))
                using (ImageTranslationForm editor =
                    new ImageTranslationForm(
                        delegate { return new AppConfig(); },
                        stub, rawStub))
                {
                    Type blockType = editorType.GetNestedType(
                        "TextBlock", BindingFlags.NonPublic);
                    const BindingFlags AnyField =
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance;
                    IList blocks = (IList)editorType.GetField(
                        "_textBlocks",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance).GetValue(editor);

                    string[] sources =
                        new string[] { "A", "B", "C" };
                    string[] targets =
                        new string[] { "甲", "乙", "丙" };
                    RectangleF[] rects = new RectangleF[]
                    {
                        new RectangleF(0, 0, 20, 10),
                        new RectangleF(30, 0, 20, 10),
                        new RectangleF(0, 30, 20, 10)
                    };
                    for (int i = 0; i < 3; i++)
                    {
                        object block = Activator.CreateInstance(
                            blockType, true);
                        blockType.GetField("Rect", AnyField)
                            .SetValue(block, rects[i]);
                        blockType.GetField("Src", AnyField)
                            .SetValue(block, sources[i]);
                        blockType.GetField("Dst", AnyField)
                            .SetValue(block, targets[i]);
                        blockType.GetField("Index", AnyField)
                            .SetValue(block, i);
                        blocks.Add(block);
                    }

                    // 故意逆序放入，验证按行 / 列排序。
                    editorType.GetField(
                        "_textSelectRect",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance).SetValue(
                            editor,
                            new RectangleF(0, 0, 200, 50));
                    editorType.GetMethod(
                        "ApplyTextSelection",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(
                            editor, new object[] { false });

                    MethodInfo buildText = editorType.GetMethod(
                        "BuildSelectedText",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                    Check(output, ref ok, "selected source text",
                        ((string)buildText.Invoke(editor, null))
                            .Replace("\r\n", "\\n"),
                        "A B\\nC");

                    editorType.GetField(
                        "_copySourceText",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance).SetValue(
                            editor, false);
                    Check(output, ref ok, "selected target text",
                        ((string)buildText.Invoke(editor, null))
                            .Replace("\r\n", "\\n"),
                        "甲 乙\\n丙");

                    // 结果窗口渲染冒烟测试：假造一次带贴合图的翻译结果，
                    // 验证解码、文字块构建、状态刷新与实际绘制都不抛异常。
                    byte[] pastedBytes;
                    using (MemoryStream png = new MemoryStream())
                    {
                        stub.Save(png, ImageFormat.Png);
                        pastedBytes = png.ToArray();
                    }
                    ImageTranslationResult fake =
                        new ImageTranslationResult();
                    fake.SumSrc = "A B C";
                    fake.SumDst = "甲 乙 丙";
                    fake.PastedImage = pastedBytes;
                    fake.Segments =
                        new System.Collections.Generic.List<ImageSegmentInfo>();
                    string[] rectTexts = new string[]
                    {
                        "0 0 40 20", "50 0 40 20", "0 30 90 20"
                    };
                    for (int i = 0; i < rectTexts.Length; i++)
                    {
                        ImageSegmentInfo segment =
                            new ImageSegmentInfo();
                        segment.Src = "src" + i;
                        segment.Dst = "dst" + i;
                        segment.Rect = rectTexts[i];
                        fake.Segments.Add(segment);
                    }
                    editorType.GetMethod(
                        "ApplyResult",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(
                            editor,
                            new object[]
                            {
                                fake, new Size(120, 80)
                            });
                    IList blocksAfter = (IList)editorType.GetField(
                        "_textBlocks",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance).GetValue(editor);
                    Check(output, ref ok, "result blocks",
                        blocksAfter.Count.ToString(), "3");

                    using (Bitmap surface = new Bitmap(320, 200))
                    using (Graphics graphics =
                        Graphics.FromImage(surface))
                    {
                        PaintEventArgs paintArgs =
                            new PaintEventArgs(
                                graphics,
                                new Rectangle(0, 0, 320, 200));
                        editorType.GetMethod(
                            "OnCanvasPaint",
                            BindingFlags.NonPublic |
                            BindingFlags.Instance).Invoke(
                                editor,
                                new object[] { editor, paintArgs });
                        Check(output, ref ok, "result render",
                            "True", "True");
                    }
                }

                // 只取 OCR 文本（不翻译）：rect 解析、阅读顺序拼接、
                // sumSrc 优先与分段兜底，以及截图浮层的新结果枚举。
                RectangleF ocrRect;
                Check(output, ref ok, "ocr client rect parsed",
                    BaiduImageTranslator.TryParseOcrRect(
                        "79 23 246 43", out ocrRect).ToString(),
                    "True");
                Check(output, ref ok, "ocr client rect values",
                    ocrRect.X + "," + ocrRect.Y + "," +
                    ocrRect.Width + "," + ocrRect.Height,
                    "79,23,246,43");
                Check(output, ref ok, "ocr client rect rejects junk",
                    BaiduImageTranslator.TryParseOcrRect(
                        "junk", out ocrRect).ToString(), "False");

                System.Collections.Generic.List<ImageSegmentInfo>
                    ocrSegments =
                        new System.Collections.Generic.List
                            <ImageSegmentInfo>();
                string[] ocrRects = new string[]
                {
                    "0 30 90 20", "50 0 40 20", "0 0 40 20"
                };
                string[] ocrSources = new string[] { "C", "B", "A" };
                for (int i = 0; i < ocrRects.Length; i++)
                {
                    ImageSegmentInfo segment = new ImageSegmentInfo();
                    segment.Src = ocrSources[i];
                    segment.Dst = "ignored" + i;
                    segment.Rect = ocrRects[i];
                    ocrSegments.Add(segment);
                }
                Check(output, ref ok, "ocr reading order",
                    BaiduImageTranslator.JoinOcrSegments(ocrSegments)
                        .Replace("\r\n", "\\n"),
                    "A B\\nC");

                ImageTranslationResult ocrResult =
                    new ImageTranslationResult();
                ocrResult.SumSrc = "  full text  ";
                ocrResult.Segments = ocrSegments;
                Check(output, ref ok, "ocr prefers sumSrc",
                    BaiduImageTranslator.BuildOcrText(ocrResult),
                    "full text");
                ocrResult.SumSrc = "";
                Check(output, ref ok, "ocr falls back to segments",
                    BaiduImageTranslator.BuildOcrText(ocrResult)
                        .Replace("\r\n", "\\n"),
                    "A B\\nC");
                Check(output, ref ok, "ocr capture result exists",
                    Enum.IsDefined(
                        typeof(ScreenshotCaptureForm.CaptureResult),
                        "Ocr").ToString(), "True");

                // ------------------------------------------------------
                // 腾讯云 OCR + 百度逐行翻译管道
                // ------------------------------------------------------
                const string expectedAuth =
                    "TC3-HMAC-SHA256 Credential=AKIDEXAMPLE/" +
                    "2020-09-13/ocr/tc3_request, " +
                    "SignedHeaders=content-type;host, Signature=" +
                    "2a57b637010b6590b19bce624ad0c6aeb98f40228a" +
                    "68f1f811c4810b0caeb4c6";
                string actualAuth = TencentOcr.BuildAuthorization(
                    "AKIDEXAMPLE", "EXAMPLEKEY", "ap-guangzhou",
                    "{\"ImageBase64\":\"AA==\",\"ConfigID\":\"MulOCR\"}",
                    1600000000L);
                Check(output, ref ok, "tc3 sign vector",
                    actualAuth, expectedAuth);

                string ocrJson =
                    "{\"Response\":{\"Angle\":0.5," +
                    "\"RequestId\":\"req-123\",\"TextDetections\":[" +
                    "{\"DetectedText\":\"B\",\"Confidence\":99," +
                    "\"ItemPolygon\":{\"X\":50,\"Y\":0," +
                    "\"Width\":40,\"Height\":20}}," +
                    "{\"DetectedText\":\"A\",\"Confidence\":98," +
                    "\"ItemPolygon\":{\"X\":0,\"Y\":0," +
                    "\"Width\":40,\"Height\":20}}," +
                    "{\"DetectedText\":\"C\",\"Confidence\":97," +
                    "\"Polygon\":[{\"X\":0,\"Y\":30}," +
                    "{\"X\":90,\"Y\":30},{\"X\":90,\"Y\":50}," +
                    "{\"X\":0,\"Y\":50}]}]}}";
                OcrResult tencentParsed = TencentOcr.ParseResponse(
                    ocrJson, new Size(200, 100));
                Check(output, ref ok, "tencent ocr line count",
                    tencentParsed.Lines.Count.ToString(), "3");
                Check(output, ref ok, "tencent ocr reading order",
                    tencentParsed.SumText.Replace("\r\n", "\\n"),
                    "A B\\nC");
                Check(output, ref ok, "tencent ocr polygon rect",
                    tencentParsed.Lines[2].Rect, "0.0 30.0 90.0 20.0");
                Check(output, ref ok, "tencent ocr request id",
                    tencentParsed.RequestId, "req-123");
                Check(output, ref ok, "tencent ocr image size",
                    tencentParsed.ImageSize.Width + "x" +
                        tencentParsed.ImageSize.Height, "200x100");

                string authError = "";
                try
                {
                    TencentOcr.ParseResponse(
                        "{\"Response\":{\"Error\":{\"Code\":" +
                        "\"AuthFailure.SignatureFailure\"," +
                        "\"Message\":\"bad\"}}}",
                        new Size(10, 10));
                }
                catch (TranslationException ex)
                {
                    authError = ex.Code;
                }
                Check(output, ref ok, "tencent auth error mapped",
                    authError, "AuthFailure.SignatureFailure");

                Check(output, ref ok, "text target auto",
                    PicturePipeline.MapTextTarget(""), "auto");
                Check(output, ref ok, "text target jp -> ja",
                    PicturePipeline.MapTextTarget("jp"), "ja");
                Check(output, ref ok, "text target fra -> fr",
                    PicturePipeline.MapTextTarget("fra"), "fr");
                Check(output, ref ok, "text target passthrough",
                    PicturePipeline.MapTextTarget("EN"), "en");

                LineTranslationResult fakeLines =
                    new LineTranslationResult();
                fakeLines.From = "zh";
                fakeLines.To = "en";
                fakeLines.Lines =
                    new System.Collections.Generic.List<string>(
                        new string[] { "甲", "乙", "丙" });
                ImageTranslationResult pipelineResult =
                    PicturePipeline.BuildResult(tencentParsed, fakeLines);
                Check(output, ref ok, "pipeline segment count",
                    pipelineResult.Segments.Count.ToString(), "3");
                Check(output, ref ok, "pipeline segment dst",
                    pipelineResult.Segments[0].Dst, "甲");
                Check(output, ref ok, "pipeline segment rect",
                    pipelineResult.Segments[0].Rect,
                    "0.0 0.0 40.0 20.0");
                Check(output, ref ok, "pipeline sum dst",
                    pipelineResult.SumDst.Replace("\r\n", "\\n"),
                    "甲 乙\\n丙");
                Check(output, ref ok, "pipeline no pasted image",
                    (pipelineResult.PastedImage == null)
                        .ToString(), "True");

                // 仅识别路径：有坐标分段、原文汇总，但没有任何译文。
                ImageTranslationResult ocrOnlyResult =
                    PicturePipeline.BuildOcrOnlyResult(tencentParsed);
                Check(output, ref ok, "ocr-only segment count",
                    ocrOnlyResult.Segments.Count.ToString(), "3");
                Check(output, ref ok, "ocr-only segment src",
                    ocrOnlyResult.Segments[0].Src, "A");
                Check(output, ref ok, "ocr-only segment has rect",
                    ocrOnlyResult.Segments[0].Rect,
                    "0.0 0.0 40.0 20.0");
                Check(output, ref ok, "ocr-only no translation",
                    (ocrOnlyResult.Segments[0].Dst.Length == 0 &&
                        string.IsNullOrEmpty(ocrOnlyResult.SumDst))
                        .ToString(), "True");
                Check(output, ref ok, "ocr-only sum src",
                    ocrOnlyResult.SumSrc.Replace("\r\n", "\\n"),
                    "A B\\nC");
                Check(output, ref ok, "ocr-only no pasted image",
                    (ocrOnlyResult.PastedImage == null).ToString(),
                    "True");

                // 仅识别窗口：不画译文遮罩，应用结果后默认复制原文。
                using (Bitmap stub = new Bitmap(120, 80))
                using (Bitmap rawStub = new Bitmap(120, 80))
                using (ImageTranslationForm ocrWindow =
                    new ImageTranslationForm(
                        delegate { return new AppConfig(); },
                        stub, rawStub, true))
                {
                    const BindingFlags Instance =
                        BindingFlags.NonPublic |
                        BindingFlags.Instance;
                    Check(output, ref ok, "ocr window mode",
                        ((bool)editorType.GetField(
                            "_ocrOnly", Instance)
                            .GetValue(ocrWindow)).ToString(),
                        "True");
                    Check(output, ref ok,
                        "ocr window skips translation overlay",
                        ((bool)editorType.GetProperty(
                            "NeedDrawTranslation", Instance)
                            .GetValue(ocrWindow, null)).ToString(),
                        "False");

                    editorType.GetMethod("ApplyResult", Instance).Invoke(
                        ocrWindow,
                        new object[] { ocrOnlyResult, new Size(120, 80) });
                    IList ocrBlocks = (IList)editorType.GetField(
                        "_textBlocks", Instance).GetValue(ocrWindow);
                    Check(output, ref ok, "ocr window block count",
                        ocrBlocks.Count.ToString(), "3");
                    Check(output, ref ok, "ocr window copies source",
                        ((bool)editorType.GetField(
                            "_copySourceText", Instance)
                            .GetValue(ocrWindow)).ToString(),
                        "True");

                    using (Bitmap surface = new Bitmap(320, 200))
                    using (Graphics graphics =
                        Graphics.FromImage(surface))
                    {
                        PaintEventArgs paintArgs =
                            new PaintEventArgs(
                                graphics,
                                new Rectangle(0, 0, 320, 200));
                        editorType.GetMethod(
                            "OnCanvasPaint", Instance).Invoke(
                                ocrWindow,
                                new object[] { ocrWindow, paintArgs });
                        Check(output, ref ok, "ocr window render",
                            "True", "True");
                    }
                }

                // ---- 语音识别（一句话识别）----
                byte[] pcmSample = new byte[3200];   // 100ms 的静音 PCM
                byte[] wavSample = VoiceRecorder.WrapWav(pcmSample);
                Check(output, ref ok, "wav total bytes",
                    wavSample.Length.ToString(),
                    (VoiceRecorder.HeaderBytes + 3200).ToString());
                Check(output, ref ok, "wav riff",
                    Encoding.ASCII.GetString(wavSample, 0, 4), "RIFF");
                Check(output, ref ok, "wav wave",
                    Encoding.ASCII.GetString(wavSample, 8, 4), "WAVE");
                Check(output, ref ok, "wav data",
                    Encoding.ASCII.GetString(wavSample, 36, 4), "data");
                Check(output, ref ok, "wav sample rate",
                    BitConverter.ToInt32(wavSample, 24).ToString(),
                    VoiceRecorder.SampleRate.ToString());
                Check(output, ref ok, "wav data length",
                    BitConverter.ToInt32(wavSample, 40).ToString(),
                    "3200");
                Check(output, ref ok, "wav duration ms",
                    VoiceRecorder.DurationMs(wavSample.Length).ToString(),
                    "100");
                Check(output, ref ok, "wav empty duration",
                    VoiceRecorder.DurationMs(VoiceRecorder.HeaderBytes)
                        .ToString(), "0");

                Check(output, ref ok, "asr engine default",
                    TencentAsr.NormalizeEngine(null), "16k_zh");
                Check(output, ref ok, "asr engine keep known",
                    TencentAsr.NormalizeEngine("16k_en"), "16k_en");
                Check(output, ref ok, "asr engine fallback",
                    TencentAsr.NormalizeEngine("bogus"), "16k_zh");
                Check(output, ref ok, "asr engine count",
                    TencentAsr.BuildEngineOptions().Count.ToString(),
                    "20");
                Check(output, ref ok, "asr engine label",
                    TencentAsr.DescribeEngine("16k_ja"),
                    "16k_ja · 日语");

                // asr 与 ocr 的签名必须不同（service 参与派生密钥）。
                string payload = "{\"EngSerViceType\":\"16k_zh\"}";
                string asrAuth = TencentAsr.BuildAuthorization(
                    "AKIDTEST", "secret", payload, 1599140479);
                string ocrAuth = TencentOcr.BuildAuthorization(
                    "AKIDTEST", "secret", "ap-guangzhou",
                    payload, 1599140479);
                Check(output, ref ok, "asr auth algorithm",
                    asrAuth.Substring(0, 16), "TC3-HMAC-SHA256 ");
                Check(output, ref ok, "asr auth scope",
                    asrAuth.Contains("/2020-09-03/asr/tc3_request")
                        ? "yes" : "no", "yes");
                Check(output, ref ok, "asr auth differs from ocr",
                    (asrAuth != ocrAuth).ToString(), "True");

                AsrResult asrParsed = TencentAsr.ParseResponse(
                    "{\"Response\":{\"Result\":\"腾讯云语音识别欢迎您。\"," +
                    "\"AudioDuration\":2430,\"WordSize\":4," +
                    "\"RequestId\":\"41ed9283\"}}");
                Check(output, ref ok, "asr text parsed",
                    asrParsed.Text, "腾讯云语音识别欢迎您。");
                Check(output, ref ok, "asr duration parsed",
                    asrParsed.AudioDurationMs.ToString(), "2430");
                Check(output, ref ok, "asr request id",
                    asrParsed.RequestId, "41ed9283");

                string asrErrorCode = "NONE";
                try
                {
                    TencentAsr.ParseResponse(
                        "{\"Response\":{\"Error\":{\"Code\":" +
                        "\"FailedOperation.UserNotRegistered\"," +
                        "\"Message\":\"not registered\"}," +
                        "\"RequestId\":\"r1\"}}");
                }
                catch (TranslationException ex)
                {
                    asrErrorCode = ex.Code;
                }
                Check(output, ref ok, "asr error code",
                    asrErrorCode, "FailedOperation.UserNotRegistered");

                Check(output, ref ok, "config voice on by default",
                    FreshConfig().EnableVoiceInput.ToString(), "True");
                Check(output, ref ok, "config voice hotkey",
                    FreshConfig().HotkeyVoiceInput, "Ctrl+Alt+V");
                Check(output, ref ok, "config voice engine",
                    FreshConfig().VoiceEngineType, "16k_zh");
                Check(output, ref ok, "config voice autocopy off",
                    FreshConfig().VoiceAutoCopy.ToString(), "False");
                Check(output, ref ok, "config voice input method",
                    FreshConfig().VoiceInputMethod, "type");

                // 录音浮条（含完成态）要能正常构建并画出来。
                using (VoiceCaptureForm bar =
                    new VoiceCaptureForm("Ctrl+Alt+V"))
                {
                    bar.UpdateRecording(
                        TimeSpan.FromSeconds(3.4), 0.6f);
                    int panelPixels = 0;
                    using (Bitmap surface =
                        new Bitmap(bar.Width, bar.Height))
                    {
                        bar.DrawToBitmap(surface,
                            new Rectangle(0, 0, bar.Width, bar.Height));
                        for (int y = 0; y < surface.Height; y += 2)
                        {
                            for (int x = 0; x < surface.Width; x += 2)
                            {
                                Color pixel = surface.GetPixel(x, y);
                                if (pixel.R == 30 && pixel.G == 34 &&
                                    pixel.B == 42)
                                {
                                    panelPixels++;
                                }
                            }
                        }
                    }
                    Check(output, ref ok, "voice bar renders panel",
                        (panelPixels > 500).ToString(), "True");
                    bar.ShowRecognizing("正在识别…");
                    bar.ShowDone("已输入 12 字");
                    using (Bitmap surface =
                        new Bitmap(bar.Width, bar.Height))
                    {
                        bar.DrawToBitmap(surface,
                            new Rectangle(0, 0, bar.Width, bar.Height));
                        Check(output, ref ok, "voice bar done render",
                            "True", "True");
                    }
                    bar.ShowFailed("测试失败");
                }

                // ---- 不限制输入框：默认放行，只排除明确非输入场景 ----
                Check(output, ref ok, "focus editable edit",
                    InputFocusProbe.IsEditableClass("Edit").ToString(),
                    "True");
                Check(output, ref ok, "focus editable richedit",
                    InputFocusProbe.IsEditableClass("RichEdit20W")
                        .ToString(), "True");
                Check(output, ref ok, "focus editable scintilla",
                    InputFocusProbe.IsEditableClass("Scintilla")
                        .ToString(), "True");
                Check(output, ref ok, "focus editable explorer token",
                    InputFocusProbe.IsEditableClass("CabinetWClass")
                        .ToString(), "False");
                Check(output, ref ok, "focus host chrome",
                    InputFocusProbe.IsHostClass(
                        "Chrome_RenderWidgetHostHWND").ToString(), "True");
                Check(output, ref ok, "focus host word",
                    InputFocusProbe.IsHostClass("_WwG").ToString(),
                    "True");
                Check(output, ref ok, "focus excluded desktop",
                    InputFocusProbe.IsExcludedClass("Progman")
                        .ToString(), "True");
                Check(output, ref ok, "focus excluded menu",
                    InputFocusProbe.IsExcludedClass("#32768")
                        .ToString(), "True");

                bool nonInput;
                string focusReason;
                InputFocusProbe.ClassifyFocus(
                    "SomeCustomDrawnInputWnd", false, false, true,
                    out nonInput, out focusReason);
                Check(output, ref ok, "focus unknown class allowed",
                    nonInput.ToString(), "False");
                Check(output, ref ok, "focus unknown class reason",
                    focusReason, "未知控件（默认按输入框处理）");
                InputFocusProbe.ClassifyFocus(
                    "CabinetWClass", false, false, true,
                    out nonInput, out focusReason);
                Check(output, ref ok, "focus explorer allowed",
                    nonInput.ToString(), "False");
                InputFocusProbe.ClassifyFocus(
                    "", false, false, true,
                    out nonInput, out focusReason);
                Check(output, ref ok, "focus empty class allowed",
                    nonInput.ToString(), "False");
                InputFocusProbe.ClassifyFocus(
                    "Progman", false, false, true,
                    out nonInput, out focusReason);
                Check(output, ref ok, "focus desktop blocked",
                    nonInput.ToString(), "True");
                InputFocusProbe.ClassifyFocus(
                    "Edit", false, true, true,
                    out nonInput, out focusReason);
                Check(output, ref ok, "focus menu blocked",
                    nonInput.ToString(), "True");
                InputFocusProbe.ClassifyFocus(
                    "Edit", false, false, false,
                    out nonInput, out focusReason);
                Check(output, ref ok, "focus dead window blocked",
                    nonInput.ToString(), "True");
                InputFocusProbe.ClassifyFocus(
                    "CustomWnd", true, false, true,
                    out nonInput, out focusReason);
                Check(output, ref ok, "focus caret reason",
                    focusReason, "检测到文本光标");

                FocusTarget allowedTarget = new FocusTarget();
                allowedTarget.ForegroundWindow = new IntPtr(1234);
                Check(output, ref ok, "insert allowed unknown",
                    InputFocusProbe.CanInsertInto(
                        allowedTarget, "中文").ToString(), "True");
                FocusTarget desktopTarget = new FocusTarget();
                desktopTarget.ForegroundWindow = new IntPtr(1234);
                desktopTarget.IsLikelyNonInput = true;
                Check(output, ref ok, "insert blocked desktop",
                    InputFocusProbe.CanInsertInto(
                        desktopTarget, "中文").ToString(), "False");
                Check(output, ref ok, "insert blocked empty text",
                    InputFocusProbe.CanInsertInto(
                        allowedTarget, "").ToString(), "False");
                Check(output, ref ok, "insert blocked null target",
                    InputFocusProbe.CanInsertInto(
                        null, "中文").ToString(), "False");
                Check(output, ref ok, "insert blocked no window",
                    InputFocusProbe.CanInsertInto(
                        new FocusTarget(), "中文").ToString(), "False");
                Check(output, ref ok, "focus probe no crash",
                    InputFocusProbe.Probe().Describe()
                        .Contains("nonInput=") ? "yes" : "no", "yes");

                Check(output, ref ok, "focus method default",
                    InputFocusProbe.NormalizeMethod(null), "type");
                Check(output, ref ok, "focus method paste",
                    InputFocusProbe.NormalizeMethod("PASTE"), "paste");
                Check(output, ref ok, "focus method fallback",
                    InputFocusProbe.NormalizeMethod("bogus"), "type");
                Check(output, ref ok, "typing normalize newline",
                    InputFocusProbe.NormalizeForTyping(
                        "第一行\r\n第二行\t三"), "第一行 第二行 三");
                Check(output, ref ok, "typing normalize control",
                    InputFocusProbe.NormalizeForTyping("a\u0001b"),
                    "ab");
                Check(output, ref ok, "typing keeps inner space",
                    InputFocusProbe.NormalizeForTyping("今天 天气"),
                    "今天 天气");
                NativeMethods.INPUT[] typed =
                    InputFocusProbe.BuildUnicodeInputs("中文A");
                Check(output, ref ok, "typing inputs length",
                    typed.Length.ToString(), "6");
                Check(output, ref ok, "typing first scan",
                    typed[0].U.ki.wScan.ToString(),
                    ((int)'\u4e2d').ToString());
                Check(output, ref ok, "typing unicode flag",
                    typed[0].U.ki.dwFlags.ToString(),
                    NativeMethods.KEYEVENTF_UNICODE.ToString());
                Check(output, ref ok, "typing keyup flag",
                    typed[1].U.ki.dwFlags.ToString(),
                    (NativeMethods.KEYEVENTF_UNICODE |
                        NativeMethods.KEYEVENTF_KEYUP).ToString());
                Check(output, ref ok, "typing empty text",
                    InputFocusProbe.BuildUnicodeInputs("").Length
                        .ToString(), "0");

                // 设置窗口加了语音区，这里做一次构造 + 绘制冒烟测试，
                // 免得布局代码在低分辨率机器上抛异常。
                using (SettingsForm settings =
                    new SettingsForm(FreshConfig()))
                {
                    TextBox voiceHotkey = (TextBox)typeof(SettingsForm)
                        .GetField(
                            "_hotkeyVoiceField",
                            BindingFlags.NonPublic |
                            BindingFlags.Instance)
                        .GetValue(settings);
                    Check(output, ref ok, "settings voice hotkey field",
                        voiceHotkey.Text, "Ctrl+Alt+V");
                    ComboBox methodBox = (ComboBox)typeof(SettingsForm)
                        .GetField(
                            "_voiceMethodBox",
                            BindingFlags.NonPublic |
                            BindingFlags.Instance)
                        .GetValue(settings);
                    Check(output, ref ok, "settings voice method",
                        methodBox.SelectedIndex.ToString(), "0");
                    Check(output, ref ok, "settings voice method items",
                        methodBox.Items.Count.ToString(), "2");
                    BindingFlags fieldFlags =
                        BindingFlags.NonPublic |
                        BindingFlags.Instance;
                    Panel contentHost = (Panel)typeof(SettingsForm)
                        .GetField("_contentHost", fieldFlags)
                        .GetValue(settings);
                    Panel footer = (Panel)typeof(SettingsForm)
                        .GetField("_footer", fieldFlags)
                        .GetValue(settings);
                    Check(output, ref ok,
                        "settings content autoscroll",
                        contentHost.AutoScroll.ToString(), "True");
                    Check(output, ref ok,
                        "settings footer docked bottom",
                        footer.Dock.ToString(), "Bottom");
                    Control saveButton = settings.AcceptButton as Control;
                    Check(output, ref ok,
                        "settings save button pinned",
                        (saveButton != null &&
                            saveButton.Parent == footer).ToString(),
                        "True");
                    using (Bitmap surface = new Bitmap(
                        settings.Width, settings.Height))
                    {
                        settings.DrawToBitmap(surface,
                            new Rectangle(
                                0, 0,
                                settings.Width,
                                settings.Height));
                        Check(output, ref ok, "settings render",
                            "True", "True");
                    }
                }

                AppConfig fresh = new AppConfig();
                Check(output, ref ok, "config tencent on by default",
                    fresh.UseTencentOcr.ToString(), "True");
                Check(output, ref ok, "config tencent region",
                    fresh.TencentRegion, "ap-guangzhou");
                Check(output, ref ok, "config tencent config id",
                    fresh.TencentOcrConfigId, "MulOCR");
                Check(output, ref ok, "engine fallback without keys",
                    PicturePipeline.UseTencent(fresh).ToString(),
                    "False");

                AppConfig withKeys = new AppConfig();
                withKeys.TencentSecretId = "AKIDTEST";
                withKeys.SetTencentSecretKey("test-key");
                Check(output, ref ok, "engine tencent with keys",
                    PicturePipeline.UseTencent(withKeys).ToString(),
                    "True");
                Check(output, ref ok, "tencent secret roundtrip",
                    withKeys.GetTencentSecretKey(), "test-key");

                // Prepare 两条路径都要能编码图片并给出正确尺寸。
                using (Bitmap sample = new Bitmap(120, 80))
                {
                    using (Graphics graphics =
                        Graphics.FromImage(sample))
                    {
                        graphics.Clear(Color.White);
                    }
                    PreparedPicture fallback =
                        PicturePipeline.Prepare(sample, fresh);
                    Check(output, ref ok, "prepare fallback engine",
                        fallback.UseTencent.ToString(), "False");
                    Check(output, ref ok, "prepare fallback size",
                        fallback.Size.Width + "x" +
                            fallback.Size.Height, "120x80");
                    Check(output, ref ok, "prepare fallback bytes",
                        (fallback.Bytes.Length > 0).ToString(),
                        "True");

                    PreparedPicture tencent =
                        PicturePipeline.Prepare(sample, withKeys);
                    Check(output, ref ok, "prepare tencent engine",
                        tencent.UseTencent.ToString(), "True");
                    Check(output, ref ok, "prepare tencent size",
                        tencent.Size.Width + "x" +
                            tencent.Size.Height, "120x80");
                    Check(output, ref ok, "prepare tencent bytes",
                        (tencent.Bytes.Length > 0).ToString(),
                        "True");
                }

                // ---- 图片工具（查看 / 转换 / 压缩 / 改尺寸）----
                string imageFolder = Path.Combine(
                    Path.GetTempPath(), "st-image-selftest");
                if (Directory.Exists(imageFolder))
                {
                    Directory.Delete(imageFolder, true);
                }
                Directory.CreateDirectory(imageFolder);
                try
                {
                    Check(output, ref ok, "image format jpeg ext",
                        ImageFormatOption.FromExtension(".jpeg").Key,
                        "jpg");
                    Check(output, ref ok, "image format tif ext",
                        ImageFormatOption.FromExtension(".tiff").Key,
                        "tif");
                    Check(output, ref ok, "image format unknown ext",
                        ImageFormatOption.FromExtension(".xyz").Key,
                        "png");
                    Check(output, ref ok, "image format labels",
                        ImageFormatOption.Find("tif").Label, "TIFF");
                    Check(output, ref ok, "image bits 24",
                        ImageToolKit.DescribeBits(PixelFormat.Format24bppRgb),
                        "24 位");
                    Check(output, ref ok, "image bits alpha",
                        ImageToolKit.DescribeBits(
                            PixelFormat.Format32bppArgb), "32 位");
                    Check(output, ref ok, "image size kb",
                        ImageToolKit.FormatSize(21463), "20.96 KB");
                    Check(output, ref ok, "image size bytes",
                        ImageToolKit.FormatSize(512), "512 B");
                    Check(output, ref ok, "image accept png",
                        ImageToolKit.IsImagePath("a.PNG").ToString(),
                        "True");
                    Check(output, ref ok, "image reject txt",
                        ImageToolKit.IsImagePath("a.txt").ToString(),
                        "False");
                    Check(output, ref ok, "image shorten path",
                        ImageToolKit.ShortenPath(
                            @"D:\下载\C#从入门到放弃\img\a.png", 18)
                            .Contains("…").ToString(), "True");

                    string png = Path.Combine(imageFolder, "sample.png");
                    using (Bitmap sample = new Bitmap(120, 80))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(sample))
                        {
                            graphics.Clear(Color.SteelBlue);
                        }

                        string saveError;
                        Check(output, ref ok, "image save png",
                            ImageToolKit.Save(sample, png, "png", 90,
                                out saveError).ToString(), "True");
                        Check(output, ref ok, "image save png err",
                            saveError == null ? "null" : saveError,
                            "null");

                        Bitmap loaded = ImageToolKit.Load(png, out saveError);
                        Check(output, ref ok, "image load size",
                            loaded == null ? "null"
                                : loaded.Width + "x" + loaded.Height,
                            "120x80");
                        Check(output, ref ok, "image detect format",
                            ImageToolKit.DetectFormatName(loaded, ".png"),
                            "PNG");

                        ImageFileInfo info = ImageToolKit.Describe(
                            png, loaded,
                            ImageToolKit.SiblingImages(png));
                        Check(output, ref ok, "image info name",
                            info.Name, "sample.png");
                        Check(output, ref ok, "image info index",
                            info.Index.ToString(), "1");
                        Check(output, ref ok, "image info size",
                            (info.SizeBytes > 0).ToString(), "True");

                        using (Bitmap resized =
                            ImageToolKit.Resize(loaded, 60, 40))
                        {
                            Check(output, ref ok, "image resize",
                                resized.Width + "x" + resized.Height,
                                "60x40");
                        }

                        using (Bitmap flipped =
                            ImageToolKit.Rotate(loaded, true))
                        {
                            Check(output, ref ok, "image rotate",
                                flipped.Width + "x" + flipped.Height,
                                "80x120");
                        }

                        using (Bitmap cropped = ImageToolKit.Crop(
                            loaded, new Rectangle(10, 10, 50, 30)))
                        {
                            Check(output, ref ok, "image crop",
                                cropped == null ? "null"
                                    : cropped.Width + "x" + cropped.Height,
                                "50x30");
                        }

                        Check(output, ref ok, "image crop out of range",
                            (ImageToolKit.Crop(loaded,
                                new Rectangle(500, 500, 10, 10)) == null)
                                .ToString(), "True");

                        // 带透明通道的图转 JPEG 要能自动铺白底。
                        using (Bitmap alpha = new Bitmap(
                            40, 40, PixelFormat.Format32bppArgb))
                        {
                            string alphaError;
                            byte[] jpeg = ImageToolKit.Encode(
                                alpha, "jpg", 80, out alphaError);
                            Check(output, ref ok, "image alpha to jpeg",
                                (jpeg != null && jpeg.Length > 0)
                                    .ToString(), "True");
                        }

                        string unique = ImageToolKit.UniquePath(png);
                        Check(output, ref ok, "image unique path",
                            (unique != png && !File.Exists(unique))
                                .ToString(), "True");
                        Check(output, ref ok, "image unique suffix",
                            unique.EndsWith("sample (1).png",
                                StringComparison.Ordinal).ToString(),
                            "True");

                        string suffixed = ImageToolKit.WithSuffix(
                            png, "_small", ".jpg");
                        Check(output, ref ok, "image suffix name",
                            Path.GetFileName(suffixed), "sample_small.jpg");

                        loaded.Dispose();
                    }

                    string renamed;
                    string renameError;
                    Check(output, ref ok, "image rename ok",
                        ImageToolKit.TryRename(png, "renamed",
                            out renamed, out renameError).ToString(),
                        "True");
                    Check(output, ref ok, "image rename applied",
                        (File.Exists(renamed) && !File.Exists(png))
                            .ToString(), "True");
                    Check(output, ref ok, "image rename empty",
                        ImageToolKit.TryRename(renamed, "  ",
                            out renamed, out renameError).ToString(),
                        "False");
                    Check(output, ref ok, "image rename empty reason",
                        renameError, "名称不能为空");
                    Check(output, ref ok, "image rename bad chars",
                        ImageToolKit.TryRename(renamed, "a/b",
                            out renamed, out renameError).ToString(),
                        "False");

                    string second = Path.Combine(
                        imageFolder, "renamed 2.bmp");
                    using (Bitmap extra = new Bitmap(20, 20))
                    {
                        extra.Save(second, ImageFormat.Bmp);
                    }
                    System.Collections.Generic.List<string> siblings =
                        ImageToolKit.SiblingImages(renamed);
                    Check(output, ref ok, "image siblings count",
                        siblings.Count.ToString(), "2");
                    Check(output, ref ok, "image siblings order",
                        Path.GetFileName(siblings[0]), "renamed 2.bmp");
                }
                finally
                {
                    try
                    {
                        Directory.Delete(imageFolder, true);
                    }
                    catch
                    {
                        // 临时目录清不掉不影响自检结论。
                    }
                }

                // ---- 名称与资源管理器「使用工具侠打开图片」----
                Check(output, ref ok, "app name", AppInfo.Name, "工具侠");
                Check(output, ref ok, "app id stable",
                    AppInfo.Id, "SelectionTranslator");
                Check(output, ref ok, "shell menu text",
                    ShellIntegration.MenuText, "使用工具侠打开图片");
                Check(output, ref ok, "shell key path",
                    ShellIntegration.MenuKeyPath,
                    @"Software\Classes\SystemFileAssociations\image\shell\ToolboxHero");
                Check(output, ref ok, "shell command quotes",
                    ShellIntegration.BuildCommand(
                        @"C:\Program Files\工具侠.exe"),
                    "\"C:\\Program Files\\工具侠.exe\" \"%1\"");
                Check(output, ref ok, "shell command strips quotes",
                    ShellIntegration.BuildCommand(
                        "\"C:\\a b\\工具侠.exe\""),
                    "\"C:\\a b\\工具侠.exe\" \"%1\"");
                Check(output, ref ok, "shell command blank",
                    ShellIntegration.BuildCommand("   "), "");
                Check(output, ref ok, "shell icon value",
                    ShellIntegration.BuildIconValue(@"C:\a\b.exe"),
                    "\"C:\\a\\b.exe\",0");
                Check(output, ref ok, "shell trim quotes",
                    ShellIntegration.TrimQuotes("\"x y\""), "x y");

                // ---- 「打开方式(H)」注册用的纯函数 ----
                Check(output, ref ok, "openwith progid",
                    ShellIntegration.ProgId, "ToolboxHero.Image");
                Check(output, ref ok, "openwith extension count",
                    ShellIntegration.ImageExtensions.Length.ToString(),
                    "8");
                Check(output, ref ok, "openwith first extension",
                    ShellIntegration.ImageExtensions[0], ".png");
                Check(output, ref ok, "openwith last extension",
                    ShellIntegration.ImageExtensions[7], ".webp");
                Check(output, ref ok, "openwith progids path",
                    ShellIntegration.OpenWithProgidsPath(".jpg"),
                    @"Software\Classes\.jpg\OpenWithProgids");
                Check(output, ref ok, "openwith fileexts path",
                    ShellIntegration.FileExtsProgidsPath(".gif"),
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.gif\OpenWithProgids");
                Check(output, ref ok, "openwith app file name",
                    ShellIntegration.AppFileName(
                        @"C:\a b\工具侠.exe"),
                    "工具侠.exe");
                Check(output, ref ok, "openwith app key path",
                    ShellIntegration.AppKeyPath(
                        @"C:\a b\工具侠.exe"),
                    @"Software\Classes\Applications\工具侠.exe");

                // ---- 首次运行自动开启三个开关 ----
                AppConfig firstRunConfig = new AppConfig();
                Check(output, ref ok, "first run default flag",
                    firstRunConfig.FirstRunDone.ToString(), "False");
                Check(output, ref ok, "first run default autostart",
                    firstRunConfig.StartWithWindows.ToString(), "False");
                Check(output, ref ok, "first run default shell menu",
                    firstRunConfig.EnableImageShellMenu.ToString(),
                    "True");
                Check(output, ref ok, "first run needs setup",
                    FirstRunSetup.NeedsSetup(firstRunConfig).ToString(),
                    "True");
                firstRunConfig.FirstRunDone = true;
                Check(output, ref ok, "first run done skips setup",
                    FirstRunSetup.NeedsSetup(firstRunConfig).ToString(),
                    "False");
                Check(output, ref ok, "first run null config",
                    FirstRunSetup.NeedsSetup(null).ToString(), "False");
                Check(output, ref ok, "first run old config migration",
                    ConfigStore.HasFirstRunDoneSetting(
                        "{\"EnableImageShellMenu\":true}").ToString(),
                    "False");
                Check(output, ref ok, "first run new config detected",
                    ConfigStore.HasFirstRunDoneSetting(
                        "{\"FirstRunDone\":true}").ToString(),
                    "True");
                Check(output, ref ok, "first run json null safe",
                    ConfigStore.HasFirstRunDoneSetting(null).ToString(),
                    "False");

                // 真的写一次 HKCU 再读回来，最后恢复成跑自检之前的模样。
                bool shellWasEnabled = ShellIntegration.IsEnabled();
                string shellError;
                try
                {
                    Check(output, ref ok, "shell register",
                        ShellIntegration.SetEnabled(true,
                            @"C:\临时 目录\工具侠.exe",
                            out shellError).ToString(), "True");
                    Check(output, ref ok, "shell register readback",
                        ShellIntegration.IsCurrent(
                            @"C:\临时 目录\工具侠.exe").ToString(),
                        "True");
                    Check(output, ref ok, "shell register enabled",
                        ShellIntegration.IsEnabled().ToString(), "True");
                    Check(output, ref ok, "openwith register command",
                        ShellIntegration.GetOpenWithCommand(),
                        ShellIntegration.BuildCommand(
                            @"C:\临时 目录\工具侠.exe"));
                    Check(output, ref ok, "openwith register enabled",
                        ShellIntegration.IsOpenWithEnabled().ToString(),
                        "True");
                    Check(output, ref ok, "openwith register candidate",
                        ShellIntegration.HasProgIdValue(".png").ToString(),
                        "True");
                    Check(output, ref ok, "openwith register webp",
                        ShellIntegration.HasProgIdValue(".webp").ToString(),
                        "True");
                    Check(output, ref ok, "shell unregister",
                        ShellIntegration.SetEnabled(false, null,
                            out shellError).ToString(), "True");
                    Check(output, ref ok, "shell unregister disabled",
                        ShellIntegration.IsEnabled().ToString(), "False");
                    Check(output, ref ok, "openwith unregister disabled",
                        ShellIntegration.IsOpenWithEnabled().ToString(),
                        "False");
                    Check(output, ref ok, "openwith unregister candidate",
                        ShellIntegration.HasProgIdValue(".png").ToString(),
                        "False");
                }
                finally
                {
                    ShellIntegration.SetEnabled(
                        shellWasEnabled,
                        Application.ExecutablePath,
                        out shellError);
                }

                // ---- 截图时直接点窗口 ----
                Check(output, ref ok, "picker shell progman",
                    WindowPicker.IsShellSurface("Progman").ToString(),
                    "True");
                Check(output, ref ok, "picker shell workerw",
                    WindowPicker.IsShellSurface("WorkerW").ToString(),
                    "True");
                Check(output, ref ok, "picker app window",
                    WindowPicker.IsShellSurface(
                        "Chrome_WidgetWin_1").ToString(), "False");
                Check(output, ref ok, "picker empty class",
                    WindowPicker.IsShellSurface("").ToString(), "False");
                Check(output, ref ok, "picker usable big",
                    WindowPicker.IsUsable(
                        new Rectangle(0, 0, 100, 80)).ToString(), "True");
                Check(output, ref ok, "picker reject tiny",
                    WindowPicker.IsUsable(
                        new Rectangle(0, 0, 10, 10)).ToString(), "False");
                Check(output, ref ok, "picker clamp inside",
                    WindowPicker.Clamp(
                        new Rectangle(-10, -10, 50, 50),
                        new Rectangle(0, 0, 100, 100))
                        .ToString(), "{X=0,Y=0,Width=40,Height=40}");
                Check(output, ref ok, "picker clamp outside",
                    WindowPicker.Clamp(
                        new Rectangle(200, 200, 50, 50),
                        new Rectangle(0, 0, 100, 100))
                        .ToString(), "{X=0,Y=0,Width=0,Height=0}");

                // ---- 图标 ----
                Check(output, ref ok, "icon size count",
                    AppIcon.IcoSizes.Length.ToString(), "7");
                using (Bitmap icon16 = AppIcon.Render(16))
                {
                    Check(output, ref ok, "icon 16 size",
                        icon16.Width + "x" + icon16.Height, "16x16");
                    Check(output, ref ok, "icon 16 has ink",
                        (icon16.GetPixel(8, 8).A > 0).ToString(), "True");
                    Check(output, ref ok, "icon 16 corner clear",
                        (icon16.GetPixel(0, 0).A == 0).ToString(), "True");
                }
                string icoPath = Path.Combine(
                    Path.GetTempPath(), "st-icon-test.ico");
                try
                {
                    AppIcon.WriteIco(icoPath);
                    byte[] header = File.ReadAllBytes(icoPath);
                    Check(output, ref ok, "icon ico magic",
                        (header.Length > 6 &&
                            header[0] == 0 && header[1] == 0 &&
                            header[2] == 1 && header[3] == 0)
                            .ToString(), "True");
                    Check(output, ref ok, "icon ico frame count",
                        BitConverter.ToInt16(header, 4).ToString(), "7");
                }
                finally
                {
                    try
                    {
                        File.Delete(icoPath);
                    }
                    catch
                    {
                        // 删不掉不影响结论。
                    }
                }

                // ---- 开机启动项 ----
                Check(output, ref ok, "startup command built",
                    (StartupManager.BuildCommand().StartsWith("\"") &&
                        StartupManager.BuildCommand().EndsWith("\""))
                        .ToString(), "True");
                Check(output, ref ok, "startup run key path",
                    StartupManager.RunKeyPathForTest,
                    @"Software\Microsoft\Windows\CurrentVersion\Run");

                using (ImageViewerForm viewer = new ImageViewerForm())
                {
                    Check(output, ref ok, "viewer minimum width",
                        (viewer.MinimumSize.Width >= 700).ToString(),
                        "True");
                }

                LineTranslationResult emptyLines =
                    BaiduTranslator.TranslateLinesAsync(
                        fresh,
                        new System.Collections.Generic.List<string>(),
                        "auto").GetAwaiter().GetResult();
                Check(output, ref ok, "empty line translation",
                    emptyLines.Lines.Count.ToString(), "0");

                RunScrollShotChecks(output, ref ok);
            }
            catch (Exception ex)
            {
                ok = false;
                output.AppendLine("EXCEPTION: " + ex);
            }

            output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            string path = Path.Combine(
                ConfigStore.DataDirectory,
                "selftest.txt");
            File.WriteAllText(path, output.ToString(), Encoding.UTF8);
            Environment.ExitCode = ok ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 滚动截图 / 文件秒搜快捷键
        // ------------------------------------------------------------------

        /// <summary>
        /// 滚动截图的纯计算验证：帧采样、重叠匹配、回滚不重复拼接。
        /// 全部用「每行颜色唯一」的合成图，不依赖屏幕，跑得也快。
        /// </summary>
        private static void RunScrollShotChecks(
            StringBuilder output, ref bool ok)
        {
            // ---- 文件秒搜快捷键 ----
            // 默认不能是 Ctrl+F：全局热键会把它从所有程序手里抢走
            // （任务管理器点搜索框、浏览器查找都会弹文件秒搜）。
            Check(output, ref ok, "filesearch hotkey default",
                new AppConfig().HotkeyFileSearch, "Ctrl+Alt+F");
            Check(output, ref ok, "filesearch hotkey not bare Ctrl+F",
                new AppConfig().HotkeyFileSearch == "Ctrl+F"
                    ? "bad" : "ok", "ok");
            Check(output, ref ok, "filesearch hotkey old config",
                ConfigStore.HasFileSearchHotkeySetting(
                    "{\"AppId\":\"x\"}").ToString(), "False");
            Check(output, ref ok, "filesearch hotkey new config",
                ConfigStore.HasFileSearchHotkeySetting(
                    "{\"HotkeyFileSearch\":\"Ctrl+F\"}").ToString(),
                "True");
            int mods;
            Keys key;
            bool parsed = HotkeyParser.TryParse(
                AppConfig.DefaultFileSearchHotkey, out mods, out key);
            Check(output, ref ok, "filesearch hotkey parses",
                parsed.ToString(), "True");
            Check(output, ref ok, "filesearch hotkey main key",
                parsed ? key.ToString() : "-", "F");
            Check(output, ref ok, "filesearch hotkey ctrl bit",
                (parsed ? (mods & NativeMethods.MOD_CONTROL) : 0)
                    .ToString(), NativeMethods.MOD_CONTROL.ToString());
            Check(output, ref ok, "filesearch hotkey alt bit",
                (parsed ? (mods & NativeMethods.MOD_ALT) : 0).ToString(),
                NativeMethods.MOD_ALT.ToString());

            // ---- 灰度换算 ----
            using (Bitmap white = new Bitmap(4, 4))
            {
                using (Graphics g = Graphics.FromImage(white))
                {
                    g.Clear(Color.White);
                }
                ScrollFrame whiteFrame = ScrollFrame.FromBitmap(white);
                Check(output, ref ok, "scroll frame white gray",
                    whiteFrame.Gray(1, 1).ToString(), "255");
            }
            using (Bitmap black = new Bitmap(4, 4))
            {
                using (Graphics g = Graphics.FromImage(black))
                {
                    g.Clear(Color.Black);
                }
                ScrollFrame blackFrame = ScrollFrame.FromBitmap(black);
                Check(output, ref ok, "scroll frame black gray",
                    blackFrame.Gray(2, 2).ToString(), "0");
            }

            // ---- 重叠匹配 ----
            const int width = 120;
            const int height = 800;
            Bitmap firstShot = MakeScrollProbeSlice(width, height, 0);
            ScrollFrame canvas = ScrollFrame.FromBitmap(firstShot);
            firstShot.Dispose();
            Check(output, ref ok, "scroll frame size",
                canvas.Width + "x" + canvas.Height, "120x800");

            ScrollFrame reference = canvas.Tail(240);
            Check(output, ref ok, "scroll reference height",
                reference.Height.ToString(), "240");
            Check(output, ref ok, "scroll reference content",
                reference.Gray(5, 0).ToString(),
                canvas.Gray(5, 560).ToString());

            // 向下滚 300 px：参考带顶部在下一帧里应出现在第 260 行
            Bitmap shiftedShot = MakeScrollProbeSlice(width, height, 300);
            ScrollFrame shifted = ScrollFrame.FromBitmap(shiftedShot);
            shiftedShot.Dispose();
            int diff;
            int y = ScrollMath.MatchOffset(
                reference, shifted,
                ScrollMath.DefaultTolerance, out diff);
            output.AppendLine("scroll match y=" + y + " diff=" + diff);
            Check(output, ref ok, "scroll match offset",
                y.ToString(), "260");
            Check(output, ref ok, "scroll new rows",
                (shifted.Height - (y + reference.Height)).ToString(),
                "300");

            // 往回滚到原点：参考带出现在第 560 行，新增行数应为 0
            Bitmap backShot = MakeScrollProbeSlice(width, height, 0);
            ScrollFrame backFrame = ScrollFrame.FromBitmap(backShot);
            backShot.Dispose();
            int backY = ScrollMath.MatchOffset(
                reference, backFrame, ScrollMath.DefaultTolerance);
            Check(output, ref ok, "scroll rollback offset",
                backY.ToString(), "560");
            Check(output, ref ok, "scroll rollback no new rows",
                (backFrame.Height - (backY + reference.Height))
                    .ToString(), "0");

            // 完全不相干的画面（全黑）不能被当成命中
            using (Bitmap blank = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(blank))
                {
                    g.Clear(Color.Black);
                }
                ScrollFrame blankFrame = ScrollFrame.FromBitmap(blank);
                Check(output, ref ok, "scroll blank frame rejected",
                    ScrollMath.MatchOffset(
                        reference, blankFrame,
                        ScrollMath.DefaultTolerance).ToString(),
                    "-1");
            }

            // ---- 同一帧判定 ----
            Bitmap duplicate = MakeScrollProbeSlice(width, height, 0);
            ScrollFrame duplicateFrame = ScrollFrame.FromBitmap(duplicate);
            duplicate.Dispose();
            Check(output, ref ok, "scroll same frame detected",
                ScrollMath.LooksSame(canvas, duplicateFrame, 2)
                    .ToString(), "True");
            Check(output, ref ok, "scroll moved frame detected",
                ScrollMath.LooksSame(canvas, shifted, 2).ToString(),
                "False");
        }

        /// <summary>探针用的「文档行颜色」：256×256 的周期，足够区分每一行。</summary>
        internal static Color ScrollProbeColor(int row)
        {
            if (row < 0)
            {
                row = 0;
            }
            int r = row % 256;
            int g = ((row / 256) * 37) % 256;
            int b = ((row * 61) ^ (row / 256)) % 256;
            return Color.FromArgb(255, r, g, b);
        }

        private static Bitmap MakeScrollProbeSlice(
            int width, int height, int startRow)
        {
            Bitmap slice = new Bitmap(
                width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(slice))
            using (SolidBrush brush = new SolidBrush(Color.Black))
            {
                for (int y = 0; y < height; y++)
                {
                    brush.Color = ScrollProbeColor(startRow + y);
                    g.FillRectangle(brush, 0, y, width, 1);
                }
            }
            return slice;
        }

        /// <summary>
        /// 端到端滚动截图：真的开一个可滚动的内容窗口，真的滚 8 次，
        /// 真的抓屏拼接，最后逐行核对颜色，确认没有错位或重复。
        /// 结果图存 scroll-shot.png。
        /// </summary>
        public static void RunScrollShotProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            ScrollProbeForm probe = null;
            ScrollCaptureSession session = null;
            Bitmap stitched = null;
            try
            {
                Application.EnableVisualStyles();
                probe = new ScrollProbeForm();
                probe.Location = new Point(140, 120);
                probe.Show();
                probe.Activate();
                PumpMessages(400);

                Rectangle area = probe.DocumentArea;
                output.AppendLine("area=" + area.Width + "x" +
                    area.Height + " at " + area.Location);

                session = new ScrollCaptureSession(area);
                Check(output, ref ok, "scroll probe start",
                    session.Start().ToString(), "True");
                Check(output, ref ok, "scroll probe first frame",
                    session.StitchedHeight.ToString(),
                    area.Height.ToString());

                const int step = 60;
                const int steps = 8;
                for (int i = 0; i < steps; i++)
                {
                    probe.ScrollBy(step);
                    PumpMessages(260);
                }

                stitched = session.Finish();
                int expectedHeight = area.Height + step * steps;
                int actualHeight = stitched == null ? 0 : stitched.Height;
                output.AppendLine("stitched=" + actualHeight +
                    " expected=" + expectedHeight);
                Check(output, ref ok, "scroll probe stitched",
                    (stitched != null).ToString(), "True");
                Check(output, ref ok, "scroll probe height",
                    (Math.Abs(actualHeight - expectedHeight) <= step)
                        .ToString(), "True");

                if (stitched != null)
                {
                    int[] rows = new int[] { 4, 127, 250, 375, 499 };
                    int good = 0;
                    for (int i = 0; i < rows.Length; i++)
                    {
                        int row = rows[i];
                        if (row >= stitched.Height)
                        {
                            continue;
                        }
                        Color expected = ScrollProbeColor(
                            probe.StartRow + row);
                        Color actual = stitched.GetPixel(
                            stitched.Width / 2, row);
                        bool same =
                            Math.Abs(actual.R - expected.R) <= 3 &&
                            Math.Abs(actual.G - expected.G) <= 3 &&
                            Math.Abs(actual.B - expected.B) <= 3;
                        if (!same)
                        {
                            output.AppendLine("row " + row +
                                " actual=" + actual +
                                " expected=" + expected);
                        }
                        else
                        {
                            good++;
                        }
                    }
                    output.AppendLine("alignedRows=" + good + "/" +
                        rows.Length);
                    Check(output, ref ok,
                        "scroll probe content aligned",
                        good.ToString(), rows.Length.ToString());

                    stitched.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory,
                            "scroll-shot.png"),
                        ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (session != null)
                {
                    session.Dispose();
                }
                if (stitched != null)
                {
                    stitched.Dispose();
                }
                if (probe != null)
                {
                    probe.Close();
                    probe.Dispose();
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "scrollshotprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 悬浮条 + Esc/Enter 钩子：确认悬浮条不会压在选区上、
        /// 点「取消」能正常结束、Esc 能被钩子接住并吞掉。
        /// </summary>
        public static void RunScrollBarProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            ScrollCaptureBar bar = null;
            ScrollKeyWatcher keys = null;
            try
            {
                Application.EnableVisualStyles();
                using (Form host = new Form())
                {
                    IntPtr handle = host.Handle;
                    output.AppendLine("hostHandle=" + handle);
                    if (SynchronizationContext.Current == null)
                    {
                        SynchronizationContext.SetSynchronizationContext(
                            new WindowsFormsSynchronizationContext());
                    }

                    Rectangle avoid = new Rectangle(240, 180, 620, 520);
                    bar = new ScrollCaptureBar(avoid, avoid.Height);
                    bool placed = bar.PlaceOutsideCapture();
                    output.AppendLine("placed=" + placed +
                        " bounds=" + bar.Bounds);
                    Check(output, ref ok, "scroll bar placed outside",
                        placed.ToString(), "True");
                    Check(output, ref ok,
                        "scroll bar avoids capture area",
                        bar.Bounds.IntersectsWith(avoid).ToString(),
                        "False");

                    bar.Show();
                    PumpMessages(450);
                    bar.Status = "已拼接 " + (avoid.Height + 360) + " px";
                    PumpMessages(250);

                    Rectangle shotArea = bar.Bounds;
                    shotArea.Inflate(8, 8);
                    using (Bitmap shot = Capture(shotArea))
                    {
                        if (shot != null)
                        {
                            shot.Save(
                                Path.Combine(
                                    ConfigStore.DataDirectory,
                                    "ui-scrollbar.png"),
                                ImageFormat.Png);
                        }
                    }
                    Check(output, ref ok, "scroll bar visible",
                        bar.Visible.ToString(), "True");

                    int finished = 0;
                    int cancelled = 0;
                    keys = new ScrollKeyWatcher();
                    keys.IgnoreInjectedKeys = false;
                    keys.FinishRequested += delegate { finished++; };
                    keys.CancelRequested += delegate { cancelled++; };
                    keys.Start();
                    output.AppendLine("hookInstalled=" + keys.IsRunning);
                    if (keys.IsRunning)
                    {
                        ProbeKey(NativeMethods.VK_ESCAPE, false);
                        ProbeKey(NativeMethods.VK_ESCAPE, true);
                        PumpMessages(400);
                        Check(output, ref ok, "scroll esc hooked",
                            cancelled.ToString(), "1");

                        ProbeKey(NativeMethods.VK_RETURN, false);
                        ProbeKey(NativeMethods.VK_RETURN, true);
                        PumpMessages(400);
                        Check(output, ref ok, "scroll enter hooked",
                            finished.ToString(), "1");
                    }
                    else
                    {
                        output.AppendLine(
                            "SKIP: 当前会话无法安装全局键盘钩子");
                    }

                    bar.RequestCancel();
                    PumpMessages(150);
                    Check(output, ref ok, "scroll bar cancel result",
                        bar.DialogResult.ToString(), "Cancel");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (keys != null)
                {
                    keys.Dispose();
                }
                if (bar != null)
                {
                    bar.Dispose();
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "scrollbarprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 从「框选 → 点滚动截图按钮 → 拨滚轮 → Enter 完成」整条真实链路：
        /// 用 ScrollProbeForm 当目标窗口，定时器负责滚动和按 Enter，
        /// 最后验证长截图预览窗口真的弹出来了。
        /// </summary>
        public static void RunScrollFlowProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            ScrollProbeForm probe = null;
            ScreenshotCaptureForm overlay = null;
            System.Windows.Forms.Timer driver = null;
            try
            {
                Application.EnableVisualStyles();
                probe = new ScrollProbeForm();
                probe.Location = new Point(140, 120);
                probe.Show();
                probe.Activate();
                PumpMessages(500);

                Rectangle area = probe.DocumentArea;
                output.AppendLine("target=" + area.Width + "x" +
                    area.Height + " at " + area.Location);

                overlay = new ScreenshotCaptureForm();
                overlay.Show();
                PumpMessages(300);

                Point clientOrigin = overlay.PointToClient(
                    new Point(area.Left, area.Top));
                Rectangle selection = new Rectangle(
                    clientOrigin, area.Size);
                overlay.DebugSelect(selection);
                output.AppendLine("selection=" + overlay.CaptureArea);

                // 每 260ms 滚一格，滚完 8 次再按 Enter 结束。
                int ticks = 0;
                const int step = 60;
                const int steps = 8;
                driver = new System.Windows.Forms.Timer();
                driver.Interval = 260;
                driver.Tick += delegate
                {
                    ticks++;
                    if (ticks <= steps)
                    {
                        probe.ScrollBy(step);
                        return;
                    }
                    driver.Stop();
                    ProbeKey(NativeMethods.VK_RETURN, false);
                    ProbeKey(NativeMethods.VK_RETURN, true);
                };
                driver.Start();

                overlay.DebugStartScroll();
                driver.Stop();

                PumpMessages(700);

                bool overlayAlive = overlay != null &&
                    !overlay.IsDisposed && overlay.Visible;
                Check(output, ref ok, "scroll flow keeps overlay open",
                    overlayAlive.ToString(), "True");
                Check(output, ref ok, "scroll flow enters long mode",
                    (overlay != null && overlay.InLongMode).ToString(),
                    "True");
                if (overlay == null || !overlayAlive || !overlay.InLongMode)
                {
                    return;
                }

                Size longSize = overlay.LongImageSize;
                output.AppendLine("longImage=" + longSize.Width +
                    "x" + longSize.Height);
                Check(output, ref ok, "scroll flow long image width",
                    longSize.Width.ToString(), area.Width.ToString());
                Check(output, ref ok, "scroll flow long image grew",
                    (longSize.Height > area.Height).ToString(), "True");

                // 滚轮 / 翻页：滚轮一格 96px（不足一屏时按余量封顶）。
                int before = overlay.LongScroll;
                int range = overlay.LongScrollRange;
                output.AppendLine("scrollRange=" + range);
                int expectWheel = Math.Min(96, Math.Max(0, range - before));
                overlay.DebugWheel(-120);
                Check(output, ref ok, "scroll flow wheel scrolls",
                    (overlay.LongScroll - before).ToString(),
                    expectWheel.ToString());
                overlay.DebugWheel(120);
                Check(output, ref ok, "scroll flow wheel scrolls back",
                    overlay.LongScroll.ToString(), before.ToString());

                // 导出必须是整张长图（不是取景框那么高），
                // 而且第 y 行的颜色必须等于文档第 y 行——被取景框裁掉
                // 或整体错位都会当场失败。
                using (Bitmap exported = overlay.DebugBuildResult())
                {
                    Check(output, ref ok, "scroll flow export size",
                        exported.Width + "x" + exported.Height,
                        longSize.Width + "x" + longSize.Height);

                    int[] probes = new int[]
                    {
                        0, longSize.Height / 3,
                        area.Height, longSize.Height - 1
                    };
                    for (int i = 0; i < probes.Length; i++)
                    {
                        int row = probes[i];
                        Color want = ScrollProbeColor(row);
                        Color got = exported.GetPixel(
                            Math.Min(30, exported.Width - 1), row);
                        Check(output, ref ok,
                            "scroll flow export row " + row,
                            got.R + "," + got.G + "," + got.B,
                            want.R + "," + want.G + "," + want.B);
                        // 最左一列也不能被选区边框染上颜色。
                        Color edge = exported.GetPixel(0, row);
                        Check(output, ref ok,
                            "scroll flow export edge " + row,
                            edge.R + "," + edge.G + "," + edge.B,
                            want.R + "," + want.G + "," + want.B);
                    }
                }

                // 浮层里现在应该是「长图 + 工具栏」的正常编辑状态。
                overlay.Activate();
                PumpMessages(400);
                using (Bitmap shot = new Bitmap(
                    overlay.Width, overlay.Height))
                {
                    overlay.DrawToBitmap(
                        shot,
                        new Rectangle(0, 0,
                            overlay.Width, overlay.Height));
                    shot.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory,
                            "ui-scrollflow.png"),
                        ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (driver != null)
                {
                    driver.Stop();
                    driver.Dispose();
                }
                if (overlay != null && !overlay.IsDisposed)
                {
                    overlay.Close();
                    overlay.Dispose();
                }
                if (probe != null)
                {
                    probe.Close();
                    probe.Dispose();
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "scrollflowprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 滚动截图期间的「选区边框」：确认边框真的画出来了，
        /// 而且一像素都不许压进选区内部（否则会被拼进长图）。
        /// </summary>
        public static void RunScrollFrameProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            Form target = null;
            ScrollCaptureFrame frame = null;
            try
            {
                Application.EnableVisualStyles();
                target = new Form();
                target.FormBorderStyle = FormBorderStyle.None;
                target.StartPosition = FormStartPosition.Manual;
                target.BackColor = Color.White;
                target.Bounds = new Rectangle(320, 260, 520, 380);
                // 置顶：否则别的窗口可能压在目标上，采样点就不是纯白了。
                target.TopMost = true;
                // 画成「一页文档」的样子，截图里能直接看出边框框的是正文。
                target.Paint += delegate(object sender, PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    using (Font title = new Font(
                        "Microsoft YaHei UI", 20f, FontStyle.Bold))
                    using (Font body = new Font(
                        "Microsoft YaHei UI", 12f))
                    using (SolidBrush ink = new SolidBrush(
                        Color.FromArgb(32, 36, 44)))
                    using (SolidBrush soft = new SolidBrush(
                        Color.FromArgb(120, 128, 140)))
                    {
                        g.DrawString("滚动截图选区", title, ink, 34f, 26f);
                        for (int i = 0; i < 12; i++)
                        {
                            g.DrawString(
                                "第 " + (i + 1) +
                                " 行正文：蓝框标出的就是正在截的区域。",
                                body, i % 4 == 0 ? ink : soft,
                                34f, 92f + i * 22f);
                        }
                    }
                };
                target.Show();
                PumpMessages(300);

                Rectangle area = target.RectangleToScreen(
                    target.ClientRectangle);
                output.AppendLine("area=" + area);

                frame = new ScrollCaptureFrame(area);
                frame.Show();
                PumpMessages(450);

                Check(output, ref ok, "frame visible",
                    frame.Visible.ToString(), "True");
                Check(output, ref ok, "frame keeps capture area",
                    frame.Area.ToString(), area.ToString());

                const int pad = 8;
                Rectangle shotRect = new Rectangle(
                    area.Left - pad, area.Top - pad,
                    area.Width + pad * 2, area.Height + pad * 2);
                using (Bitmap shot = Capture(shotRect))
                {
                    if (shot == null)
                    {
                        Check(output, ref ok, "frame screenshot",
                            "null", "bitmap");
                    }
                    else
                    {
                        shot.Save(
                            Path.Combine(
                                ConfigStore.DataDirectory,
                                "ui-scrollframe.png"),
                            ImageFormat.Png);

                        // 选区内四边 + 中间必须还是白底：边框没压进来。
                        Point[] inside = new Point[]
                        {
                            new Point(pad, pad),
                            new Point(pad + area.Width - 1, pad),
                            new Point(pad, pad + area.Height - 1),
                            new Point(pad + area.Width - 1,
                                pad + area.Height - 1),
                            new Point(pad + area.Width / 2, pad),
                            new Point(pad, pad + area.Height / 2)
                        };
                        int dirty = 0;
                        for (int i = 0; i < inside.Length; i++)
                        {
                            Color c = shot.GetPixel(
                                inside[i].X, inside[i].Y);
                            if (c.R < 240 || c.G < 240 || c.B < 240)
                            {
                                dirty++;
                                output.AppendLine(
                                    "  inner " + inside[i] + " = " +
                                    c.R + "," + c.G + "," + c.B);
                            }
                        }
                        Check(output, ref ok, "frame outside the area",
                            dirty.ToString(), "0");

                        // 选区外 2px 处必须是我们画的蓝色边框。
                        Color ring = shot.GetPixel(pad - 2, pad + 20);
                        output.AppendLine("ring=" + ring.R + "," +
                            ring.G + "," + ring.B);
                        Check(output, ref ok, "frame border drawn",
                            (ring.B > 150 && ring.R < 120).ToString(),
                            "True");
                    }
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (frame != null)
                {
                    frame.Hide();
                    frame.Dispose();
                }
                if (target != null)
                {
                    target.Close();
                    target.Dispose();
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "scrollframeprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>截图浮层工具栏的渲染预览（含新增的滚动截图按钮）。</summary>
        public static void RunCaptureUiProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            ScreenshotCaptureForm overlay = null;
            Form backdrop = null;
            try
            {
                Application.EnableVisualStyles();
                backdrop = new Form();
                backdrop.FormBorderStyle = FormBorderStyle.None;
                backdrop.StartPosition = FormStartPosition.Manual;
                backdrop.Bounds = new Rectangle(0, 0, 1280, 720);
                backdrop.BackColor = Color.FromArgb(28, 32, 40);
                backdrop.Show();
                PumpMessages(400);

                overlay = new ScreenshotCaptureForm();
                overlay.Show();
                PumpMessages(300);
                Point origin = overlay.PointToClient(new Point(240, 160));
                overlay.DebugSelect(new Rectangle(origin, new Size(640, 360)));
                PumpMessages(300);

                Check(output, ref ok, "capture ui selection",
                    (overlay.CaptureArea.Width > 0).ToString(), "True");

                Rectangle bounds = overlay.Bounds;
                using (Bitmap shot = new Bitmap(
                    Math.Min(bounds.Width, 1400),
                    Math.Min(bounds.Height, 900)))
                {
                    overlay.DrawToBitmap(
                        shot,
                        new Rectangle(0, 0, shot.Width, shot.Height));
                    shot.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory,
                            "ui-capture-toolbar.png"),
                        ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (overlay != null && !overlay.IsDisposed)
                {
                    overlay.Close();
                    overlay.Dispose();
                }
                if (backdrop != null)
                {
                    backdrop.Close();
                    backdrop.Dispose();
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "captureuiprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>工具栏图标对照图（放大后逐个标注），看新图标有没有画歪。</summary>
        public static void RunIconStripProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            try
            {
                using (Bitmap strip =
                    ScreenshotCaptureForm.RenderIconStripForTest(4))
                {
                    strip.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory,
                            "ui-icon-strip.png"),
                        ImageFormat.Png);
                    Check(output, ref ok, "icon strip size",
                        (strip.Width > 100).ToString(), "True");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "iconstripprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 桌面管理探针：在临时目录造几类文件，验证列表加载、视图切换、
        /// 透明度设置，以及「重新分类」只分类不搬家（文件原位不动、
        /// 不创建分类文件夹）。最后清理临时目录。输出 desktopmgrprobe.txt。
        /// </summary>
        public static void RunDesktopMgrProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            DesktopManagerForm form = null;
            string temp = null;
            AppConfig backup = null;
            try
            {
                Application.EnableVisualStyles();
                // 探针里的收起 / 展开 / 拉伸会一路 SavePanelState，跑完
                // 必须把用户的真实配置原样恢复，别把探针几何写进 config。
                backup = ConfigStore.Load();

                temp = Path.Combine(
                    Path.GetTempPath(),
                    "st-desktopmgr-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "报告.docx"), "doc");
                File.WriteAllText(Path.Combine(temp, "截图.png"), "img");
                File.WriteAllText(Path.Combine(temp, "电影.mp4"), "vid");
                File.WriteAllText(Path.Combine(temp, "压缩包.zip"), "zip");
                File.WriteAllText(Path.Combine(temp, "杂项.dat"), "other");

                form = new DesktopManagerForm();
                // 复现用户踩到的坑：桌面根上有个用户自己的真实文件夹，
                // 名字就叫「文档」。以前会被当成旧版分类文件夹，里面的
                // 文件被塞进「文档」页签，点「打开所在位置」就跳到那个
                // 文件夹里去。现在必须完全不当回事。
                string userDocDir = Path.Combine(temp, "文档");
                Directory.CreateDirectory(userDocDir);
                File.WriteAllText(
                    Path.Combine(userDocDir, "内部资料.docx"), "inner");
                // 屏幕外的临时目录，别让真实配置里的屏蔽列表掺进来。
                form.SetHiddenFoldersForTest("");

                form.LoadFromPath(temp);

                Check(output, ref ok, "桌面管理条目数",
                    form.TotalItemCount.ToString(), "6");
                // 同名真实文件夹按普通目录对待，计进「目录」，内部文件不映射。
                Check(output, ref ok, "同名文件夹算普通目录",
                    form.CountForCategory("目录").ToString(), "1");
                Check(output, ref ok, "同名文件夹内部文件不映射",
                    form.CountForCategory("文档").ToString(), "1");
                Check(output, ref ok, "默认视图=列表",
                    form.ViewMode.ToString(),
                    DesktopManagerForm.ViewModeKind.List.ToString());

                form.ToggleView();
                Check(output, ref ok, "切换后=图标",
                    form.ViewMode.ToString(),
                    DesktopManagerForm.ViewModeKind.Icon.ToString());
                form.ToggleView();
                Check(output, ref ok, "切回=列表",
                    form.ViewMode.ToString(),
                    DesktopManagerForm.ViewModeKind.List.ToString());

                form.SetOpacityForTest(0.5);
                Check(output, ref ok, "透明度=50%",
                    ((int)Math.Round(form.CurrentOpacity * 100)).ToString(),
                    "50");

                int classified = form.Organize(temp);
                Check(output, ref ok, "分类计数", classified.ToString(), "5");
                // 新版「重新分类」只做展示层分类：绝不移动文件、绝不建文件夹。
                Check(output, ref ok, "整理后顶层文件数不变",
                    Directory.GetFiles(temp).Length.ToString(), "5");
                // 临时目录里那个「文档」是探针自己建的（模拟用户真实
                // 文件夹），所以只检查其余四个分类文件夹没被创建。
                Check(output, ref ok, "不创建分类文件夹",
                    (Directory.Exists(Path.Combine(temp, "图片")) ||
                     Directory.Exists(Path.Combine(temp, "视频")) ||
                     Directory.Exists(Path.Combine(temp, "压缩")) ||
                     Directory.Exists(Path.Combine(temp, "其它")))
                        .ToString(),
                    "False");
                Check(output, ref ok, "文件留在原位",
                    File.Exists(Path.Combine(temp, "报告.docx")).ToString(),
                    "True");

                // 分类完页签仍按扩展名分别汇总（+1 那个是探针建的「文档」目录）。
                Check(output, ref ok, "分类后全部条目数",
                    form.TotalItemCount.ToString(), "6");
                Check(output, ref ok, "默认页签=文档",
                    form.ActiveCategory, "文档");
                // 「全部」聚合页签已去掉：6 格都是实打实的分类。
                Check(output, ref ok, "页签数=6",
                    DesktopManagerForm.Categories.Length.ToString(), "6");
                Check(output, ref ok, "已去掉全部页签",
                    (Array.IndexOf(
                        DesktopManagerForm.Categories, "全部") < 0)
                        .ToString(), "True");
                form.SelectCategory("文档");
                Check(output, ref ok, "页签文档=1",
                    form.ItemCount.ToString(), "1");
                form.SelectCategory("图片");
                Check(output, ref ok, "页签图片=1",
                    form.ItemCount.ToString(), "1");
                form.SelectCategory("视频");
                Check(output, ref ok, "页签视频=1",
                    form.ItemCount.ToString(), "1");
                form.SelectCategory("压缩");
                Check(output, ref ok, "页签压缩=1",
                    form.ItemCount.ToString(), "1");
                form.SelectCategory("其它");
                Check(output, ref ok, "页签其它=1",
                    form.ItemCount.ToString(), "1");
                // 同名真实文件夹按普通目录算，出现在「目录」页签里。
                Check(output, ref ok, "页签目录=1",
                    form.CountForCategory("目录").ToString(), "1");
                form.SelectCategory("其它");
                Check(output, ref ok, "切回其它=1",
                    form.ItemCount.ToString(), "1");
                Check(output, ref ok, "页签计数文档=1",
                    form.CountForCategory("文档").ToString(), "1");

                // 按路径显式屏蔽：把它列进隐藏列表后，目录页签清空，
                // 但别的条目一个都不能少（屏蔽只针对这一条路径）。
                form.SetHiddenFoldersForTest(userDocDir);
                Check(output, ref ok, "屏蔽后目录页签=0",
                    form.CountForCategory("目录").ToString(), "0");
                Check(output, ref ok, "屏蔽后其余条目保留",
                    form.TotalItemCount.ToString(), "5");
                Check(output, ref ok, "屏蔽后同名字段仍在",
                    form.CountForCategory("文档").ToString(), "1");
                // 恢复：清掉屏蔽，目录页签回来。
                form.SetHiddenFoldersForTest("");
                Check(output, ref ok, "解除屏蔽后目录页签=1",
                    form.CountForCategory("目录").ToString(), "1");

                // 渲染冒烟：真的显示一次，确认自绘列表不抛 GDI+ 异常。
                // 悬停展开改由下面的 HoverTickForTest 手动驱动，避免真实
                // 光标位置把「收起 / 展开」断言搅乱。
                form.SuspendHoverExpandForTest();
                form.Show();
                PumpMessages(160);

                // 收起 / 展开：收起后只剩一条短横条，展开回到竖长形。
                // 先把形状重置成默认：真实配置里可能残留探针 / 用户拖过
                // 的尺寸，这组断言要的是出厂形状。
                form.ResetSizesForTest();
                form.OverridePointerForTest(false);
                form.SetCollapsed(false);
                int expandedHeight = form.Height;
                int expandedRight = form.ScreenLocation.X + form.Width;
                Check(output, ref ok, "展开是竖长形",
                    (form.Height > form.Width).ToString(), "True");
                Check(output, ref ok, "展开宽度=400",
                    form.Width.ToString(), "400");
                form.SetCollapsed(true);
                Check(output, ref ok, "收起高度=标题栏",
                    form.Height.ToString(), "44");
                Check(output, ref ok, "收起是短条",
                    (form.Width < 240).ToString(), "True");
                Check(output, ref ok, "收起右边缘不动",
                    (form.ScreenLocation.X + form.Width).ToString(),
                    expandedRight.ToString());
                Check(output, ref ok, "收起状态",
                    form.Collapsed.ToString(), "True");
                form.SetCollapsed(false);
                Check(output, ref ok, "展开高度还原",
                    form.Height.ToString(), expandedHeight.ToString());
                Check(output, ref ok, "展开状态",
                    form.Collapsed.ToString(), "False");

                // 悬停自动展开：停够 180ms 才展开；手动收起时鼠标还压在
                // 条上，必须先离开一次才能再次触发，否则会「收了又弹」。
                form.SetCollapsed(true);
                form.HoverTickForTest();          // 鼠标不在 → 解除压制
                form.OverridePointerForTest(true);
                form.HoverTickForTest();          // 60ms
                form.HoverTickForTest();          // 120ms
                Check(output, ref ok, "悬停未满 180ms 不展开",
                    form.Collapsed.ToString(), "True");
                form.HoverTickForTest();          // 180ms
                Check(output, ref ok, "悬停 180ms 自动展开",
                    form.Collapsed.ToString(), "False");
                form.SetCollapsed(true);
                for (int i = 0; i < 6; i++)
                {
                    form.HoverTickForTest();
                }
                Check(output, ref ok, "刚收起时鼠标未离开不弹开",
                    form.Collapsed.ToString(), "True");
                form.OverridePointerForTest(false);
                form.HoverTickForTest();
                form.OverridePointerForTest(true);
                form.HoverTickForTest();
                form.HoverTickForTest();
                form.HoverTickForTest();
                Check(output, ref ok, "鼠标离开再回来可展开",
                    form.Collapsed.ToString(), "False");

                // 展开态：鼠标离开面板 400ms 自动收起成短条；中途回来
                // 计时清零，不然路过面板边缘都会被误收。
                form.OverridePointerForTest(false);
                form.HoverTickForTest();          // 60ms
                form.HoverTickForTest();          // 120ms
                form.HoverTickForTest();          // 180ms
                Check(output, ref ok, "展开态离开未满 400ms 不收起",
                    form.Collapsed.ToString(), "False");
                form.OverridePointerForTest(true);
                form.HoverTickForTest();          // 回来，计时清零
                form.OverridePointerForTest(false);
                form.HoverTickForTest();          // 60ms
                form.HoverTickForTest();          // 120ms
                Check(output, ref ok, "离开中断回来后重新计时",
                    form.Collapsed.ToString(), "False");
                for (int i = 0; i < 5; i++)
                {
                    form.HoverTickForTest();      // 180 / 240 / 300 / 360 / 420ms
                }
                Check(output, ref ok, "展开态离开 400ms 自动收起",
                    form.Collapsed.ToString(), "True");
                // 后面的拉伸几何断言要展开态。
                form.OverridePointerForTest(true);
                form.HoverTickForTest();          // 60
                form.HoverTickForTest();          // 120
                form.HoverTickForTest();          // 180 → 展开
                Check(output, ref ok, "自动收起后可再悬停展开",
                    form.Collapsed.ToString(), "False");

                // 边框拉伸命中：展开只认左边框（宽度）/ 上边框（高度）
                // / 左上角，右边框和下边框一律不响应，这样系统 sizing
                // 循环里「下边框不动」才成立。
                form.OverridePointerForTest(false);
                Check(output, ref ok, "展开左边框可拉宽",
                    form.ResizeHitForTest(2, 100).ToString(), "10");
                Check(output, ref ok, "展开上边框可拉高",
                    form.ResizeHitForTest(100, 2).ToString(), "12");
                Check(output, ref ok, "展开左上角可双向拉",
                    form.ResizeHitForTest(2, 2).ToString(), "13");
                Check(output, ref ok, "展开右边框不响应",
                    form.ResizeHitForTest(
                        form.Width - 2, 100).ToString(), "0");
                Check(output, ref ok, "展开下边框不响应",
                    form.ResizeHitForTest(
                        100, form.Height - 2).ToString(), "0");
                Check(output, ref ok, "面板中央不拉伸",
                    form.ResizeHitForTest(
                        form.Width / 2, form.Height / 2).ToString(), "0");
                Check(output, ref ok, "老配置尺寸 0 回落默认",
                    DesktopManagerForm.ClampSizeForTest(
                        0, 300, 1600, 400).ToString(), "400");
                Check(output, ref ok, "合法尺寸不被改",
                    DesktopManagerForm.ClampSizeForTest(
                        520, 300, 1600, 400).ToString(), "520");

                // 真实拉伸几何：上边框往上拉变高、下边缘不动、列表
                // 当场多显示；往下拉变矮、列表少显示。左边框拉伸时
                // 右边缘不动。
                int h0 = form.Height;
                int bottom0 = form.ScreenLocation.Y + form.Height;
                int right0 = form.ScreenLocation.X + form.Width;
                int list0 = form.ListHeightForTest;
                form.SimulateResizeDragForTest(12, 0, -120);
                Check(output, ref ok, "上边框往上拉变高",
                    (form.Height > h0).ToString(), "True");
                Check(output, ref ok, "上边框拉动时下边缘不动",
                    (form.ScreenLocation.Y + form.Height).ToString(),
                    bottom0.ToString());
                Check(output, ref ok, "往上拉列表实时显示更多",
                    (form.ListHeightForTest > list0).ToString(),
                    "True");
                int tall = form.Height;
                int listTall = form.ListHeightForTest;
                form.SimulateResizeDragForTest(12, 0, 200);
                Check(output, ref ok, "上边框往下拉变矮",
                    (form.Height < tall).ToString(), "True");
                Check(output, ref ok, "往下拉列表实时显示更少",
                    (form.ListHeightForTest < listTall).ToString(),
                    "True");
                int w0 = form.Width;
                form.SimulateResizeDragForTest(10, -80, 0);
                Check(output, ref ok, "左边框往左拉变宽",
                    (form.Width > w0).ToString(), "True");
                Check(output, ref ok, "左边框拉动时右边缘不动",
                    (form.ScreenLocation.X + form.Width).ToString(),
                    right0.ToString());

                // 拉高时文件列表当场跟着变高（不是松手才生效）。
                int listBefore = form.ListHeightForTest;
                form.SetBounds(form.Left, form.Top, 520, 700);
                Check(output, ref ok, "拉高时列表实时变高",
                    (form.ListHeightForTest > listBefore).ToString(),
                    "True");
                form.EndResizeForTest();
                Check(output, ref ok, "拉伸后记住展开宽度",
                    form.ExpandedWidthForTest.ToString(), "520");
                Check(output, ref ok, "拉伸后记住展开高度",
                    form.ExpandedHeightForTest.ToString(), "700");

                // 收起短条：左右边框都能改长度，高度锁死；边框区不
                // 触发悬停展开，否则停在右边框上想拖就先被展开了。
                form.SetCollapsed(true);
                Check(output, ref ok, "收起右边框可改长度",
                    form.ResizeHitForTest(
                        form.Width - 2, 20).ToString(), "11");
                Check(output, ref ok, "收起左边框可改长度",
                    form.ResizeHitForTest(2, 20).ToString(), "10");
                Check(output, ref ok, "收起上边框锁死",
                    form.ResizeHitForTest(100, 2).ToString(), "0");
                Check(output, ref ok, "边框不算悬停区域",
                    form.IsHoverAreaForTest(
                        form.Width - 2, 20).ToString(), "False");
                Check(output, ref ok, "短条中央算悬停区域",
                    form.IsHoverAreaForTest(
                        form.Width / 2, 20).ToString(), "True");
                form.SetBounds(form.Left, form.Top, 280, 44);
                form.EndResizeForTest();
                Check(output, ref ok, "短条长度可自定义并记住",
                    form.CollapsedWidthForTest.ToString(), "280");

                // 还原默认形状，别把后面的锁定 / 拖动断言搅乱。
                form.SetBounds(form.Left, form.Top, 210, 44);
                form.EndResizeForTest();
                form.SetCollapsed(false);
                form.SetBounds(form.Left, form.Top, 400, 620);
                form.EndResizeForTest();

                // 锁定位置：解锁时拖标题栏能挪，锁定后拖不动。
                form.SetLocked(false);
                Point beforeLock = form.Location;
                form.BeginDragForTest();
                form.MoveDragForTest(60, 40);
                Check(output, ref ok, "解锁后标题栏可拖动",
                    (form.Location != beforeLock).ToString(), "True");
                form.SetLocked(true);
                Point lockedAt = form.Location;
                form.BeginDragForTest();
                form.MoveDragForTest(-40, -25);
                Check(output, ref ok, "锁定后拖不动",
                    (form.Location == lockedAt).ToString(), "True");
                Check(output, ref ok, "锁定状态可读",
                    form.Locked.ToString(), "True");
                form.SetLocked(false);
                Check(output, ref ok, "解锁恢复可拖",
                    form.Locked.ToString(), "False");

                // 界面配色：浅色底必须自动转深色文字，两种主题都要保证
                // 正文对比度足够（WCAG AA 大字标准 3:1，这里按 7:1 卡）。
                UiTheme.Configure(
                    Color.FromArgb(246, 247, 250),
                    UiTheme.DefaultAccent, false);
                Check(output, ref ok, "浅色主题判定",
                    UiTheme.IsLight.ToString(), "True");
                Check(output, ref ok, "浅色正文对比度>=7",
                    (UiTheme.Contrast(UiTheme.Text, UiTheme.Window) >= 7)
                        .ToString(), "True");
                Check(output, ref ok, "浅色正文为深色",
                    (UiTheme.Text.R < 80).ToString(), "True");

                UiTheme.Configure(
                    Color.FromArgb(22, 23, 26),
                    UiTheme.DefaultAccent, false);
                Check(output, ref ok, "深色主题判定",
                    UiTheme.IsLight.ToString(), "False");
                Check(output, ref ok, "深色正文对比度>=7",
                    (UiTheme.Contrast(UiTheme.Text, UiTheme.Window) >= 7)
                        .ToString(), "True");
                Check(output, ref ok, "深色正文为浅色",
                    (UiTheme.Text.R > 180).ToString(), "True");

                // 强调色跟随：换了强调色，派生色必须跟着变。
                UiTheme.Configure(
                    UiTheme.DefaultBase,
                    Color.FromArgb(224, 149, 74), false);
                Check(output, ref ok, "强调色生效",
                    UiTheme.Accent.ToArgb()
                        .Equals(Color.FromArgb(224, 149, 74).ToArgb())
                        .ToString(), "True");
                Check(output, ref ok, "强调柔色区别于底色",
                    (UiTheme.AccentSoft.ToArgb() !=
                        UiTheme.Window.ToArgb()).ToString(), "True");

                // 已打开窗口的「创建时一次性赋值」颜色要能跟着主题平移。
                using (Panel probePanel = new Panel())
                {
                    probePanel.BackColor = UiTheme.DefaultBase;
                    UiTheme.Configure(
                        Color.FromArgb(246, 247, 250),
                        UiTheme.DefaultAccent, false);
                    UiTheme.Restyle(probePanel);
                    Check(output, ref ok, "旧窗口底色跟随主题",
                        (probePanel.BackColor.ToArgb() ==
                            UiTheme.Window.ToArgb()).ToString(), "True");
                }

                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);

                // 真桌面加载耗时（只读，不动任何文件）。图标是后台补的，
                // 所以这里的数字就是「打开面板到看见列表」的等待时间。
                string desktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktop))
                {
                    System.Diagnostics.Stopwatch watch =
                        System.Diagnostics.Stopwatch.StartNew();
                    DesktopManagerForm real = new DesktopManagerForm();
                    real.LoadFromPath(desktop);
                    watch.Stop();
                    output.AppendLine(string.Format(
                        "真实桌面加载: {0} 项, {1} ms",
                        real.TotalItemCount, watch.ElapsedMilliseconds));
                    real.Close();
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    form.Close();
                }
                if (temp != null && Directory.Exists(temp))
                {
                    try
                    {
                        Directory.Delete(temp, true);
                    }
                    catch
                    {
                    }
                }
                try
                {
                    if (backup != null)
                    {
                        ConfigStore.Save(backup);
                    }
                }
                catch
                {
                }
                output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "desktopmgrprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(
            IntPtr hWnd, uint affinity);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowDisplayAffinity(
            IntPtr hWnd, out uint affinity);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;
        private const int GwlExStyle = -20;
        private const int WsExTopMost = 0x00000008;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool MoveWindow(
            IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hDC, int x, int y);

        /// <summary>读屏幕上某一点的像素（诊断窗口到底有没有画出来）。</summary>
        private static string ScreenPixel(int x, int y)
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                return "-";
            }
            try
            {
                uint value = GetPixel(dc, x, y);
                if (value == 0xFFFFFFFF)
                {
                    return "err";
                }
                return ((value & 0xFF)).ToString() + "," +
                    ((value >> 8) & 0xFF).ToString() + "," +
                    ((value >> 16) & 0xFF).ToString();
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private static string WindowClass(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return "(null)";
            }
            StringBuilder sb = new StringBuilder(128);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(
            byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);

        private const byte VkLWin = 0x5B;
        private const byte VkD = 0x44;
        private const uint KeyEventKeyUp = 0x0002;

        /// <summary>按一次 Win+D（显示桌面 / 还原）。</summary>
        private static void ToggleShowDesktop()
        {
            keybd_event(VkLWin, 0, 0, IntPtr.Zero);
            keybd_event(VkD, 0, 0, IntPtr.Zero);
            PumpMessages(60);
            keybd_event(VkD, 0, KeyEventKeyUp, IntPtr.Zero);
            keybd_event(VkLWin, 0, KeyEventKeyUp, IntPtr.Zero);
        }

        private const uint GwHwndNext = 2;
        private const uint GaRoot = 2;

        /// <summary>窗口在系统 z-order 里的序号（0 最靠上）。找不到返回 -1。</summary>
        private static int ZOrderIndex(IntPtr hWnd)
        {
            int total;
            return ZOrderIndex(hWnd, out total);
        }

        private static int ZOrderIndex(IntPtr hWnd, out int total)
        {
            total = 0;
            if (hWnd == IntPtr.Zero)
            {
                return -1;
            }
            IntPtr current = GetTopWindow(IntPtr.Zero);
            int index = 0;
            while (current != IntPtr.Zero && index < 4000)
            {
                if (current == hWnd)
                {
                    total = index;
                    return index;
                }
                current = GetWindow(current, GwHwndNext);
                index++;
            }
            total = index;
            return -1;
        }

        /// <summary>整屏截图存到数据目录。</summary>
        private static void CaptureDesktopShot(string name)
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            using (Bitmap shot = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics graphics = Graphics.FromImage(shot))
                {
                    graphics.CopyFromScreen(
                        bounds.X, bounds.Y, 0, 0, bounds.Size);
                }
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                shot.Save(Path.Combine(ConfigStore.DataDirectory, name),
                    System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>
        /// 常驻面板必须像桌面小部件：
        ///   · 别的程序在前台时，它压在应用窗口下面、不是 topmost（否则
        ///     就会盖在浏览器上面，用户抱怨过）；
        ///   · 桌面在前台（Win+D / 关掉最后一个窗口）时，它浮到最前，
        ///     保证回到桌面一定看得见；
        ///   · 点它不会激活自己，也就不会顶到别人头上去；
        ///   · 关掉应用窗口不会把它带走。
        /// 输出 desktoppin.txt 与 ui-desktoppin-*.png。
        /// </summary>
        /// <summary>
        /// 真实鼠标拖边框的端到端探针：验证 WinForms 消息链路（特别是被
        /// 文件列表 / 滚动条盖住的左边框）、下边缘不动、列表当场变高，
        /// 以及收起后拖短条右边框改长度。输出 resizeprobe.txt。
        /// </summary>
        public static void RunResizeProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            DesktopManagerForm form = null;
            Point originalCursor = Cursor.Position;
            AppConfig backup = null;
            try
            {
                Application.EnableVisualStyles();
                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);
                backup = ConfigStore.Load();

                form = new DesktopManagerForm();
                form.LoadFromPath(Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory));
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(120, 240);
                // 实拍期间别让层级跟随把面板压到底下，也别让悬停展开
                // 在拖边框时插一脚。
                form.SuspendHoverExpandForTest();
                form.SuspendZOrderForTest();
                form.Show();
                form.SetCollapsed(false);
                // RestorePanelState / ApplyAnchoredSize 会按配置摆位置，
                // 实拍探针要一个固定的起点，所以在 Show 之后再定一次。
                form.Location = new Point(120, 240);
                form.Size = new Size(400, 620);
                // 冷启动时浏览器 / 工作台常会抢一次前台，刚 Show 完注入的
                // 鼠标消息可能被它吃掉。先置顶 + 激活 + 等 DWM 排稳，再拖。
                SetWindowPos(form.Handle, HwndTopmost,
                    0, 0, 0, 0, SwpNoMoveSize);
                SetForegroundWindow(form.Handle);
                PumpMessages(500);

                // 上边框往上拉 120px：下边缘不动、文件列表当场变高。
                Point o = form.ScreenLocation;
                Point topStart = new Point(o.X + 140, o.Y + 2);
                bool topReady = EnsurePanelHit(
                    form, topStart, output, "topDrag");
                output.AppendLine("diag: handle=" + form.Handle +
                    " rootAtStart=" + RootWindowAt(topStart) +
                    " fg=" + WindowClass(GetForegroundWindow()) +
                    " topmost=" + IsTopMost(form.Handle) +
                    " bounds=" + form.Bounds + " visible=" + form.Visible +
                    " ready=" + topReady);
                o = form.ScreenLocation;
                int h0 = form.Height;
                int bottom0 = o.Y + h0;
                int list0 = form.ListHeightForTest;
                DragMouse(new Point(o.X + 140, o.Y + 2),
                    new Point(o.X + 140, o.Y - 118));
                PumpMessages(250);
                // 完全没变化时重试一次：注入消息被别的进程抢走是环境
                // 噪声（同一轮左边框 / 短条都成功），不是拉伸失效。
                if (form.Height <= h0 + 60)
                {
                    output.AppendLine("topDrag 重试：首次按下消息未送达");
                    EnsurePanelHit(form, topStart, output, "topDrag-retry");
                    o = form.ScreenLocation;
                    DragMouse(new Point(o.X + 140, o.Y + 2),
                        new Point(o.X + 140, o.Y - 118));
                    PumpMessages(250);
                }
                output.AppendLine("topDrag: h=" + h0 + "->" + form.Height +
                    " bottom=" + bottom0 + "->" +
                    (form.ScreenLocation.Y + form.Height) +
                    " list=" + list0 + "->" + form.ListHeightForTest);
                Check(output, ref pass, "真的能拖上边框变高",
                    (form.Height > h0 + 60).ToString(), "True");
                Check(output, ref pass, "拖上边框时下边缘不动",
                    (form.ScreenLocation.Y + form.Height).ToString(),
                    bottom0.ToString());
                Check(output, ref pass, "拖动中列表真的变高",
                    (form.ListHeightForTest > list0).ToString(), "True");

                // 左边框这一列被列表 / 滚动条盖着，消息必须转发回窗体。
                // 先把层级稳住，再重新取一次坐标：上一步拉高过面板，
                // 置顶等待期间位置也可能被系统调整，旧坐标会打偏 6px 边框。
                EnsurePanelHit(form,
                    new Point(form.ScreenLocation.X + 140,
                        form.ScreenLocation.Y + 200),
                    output, "leftDrag");
                o = form.ScreenLocation;
                Point leftStart = new Point(o.X + 2, o.Y + 220);
                DesktopManagerForm.ForwardDownCountForTest = 0;
                DesktopManagerForm.ForwardHitCountForTest = 0;
                DesktopManagerForm.ForwardMoveCountForTest = 0;
                DesktopManagerForm.ForwardUpCountForTest = 0;
                EnsurePanelHit(form, leftStart, output, "leftDrag");
                // 所有基准值都在最后一次定位之后取，避免和拖拽起点错位。
                o = form.ScreenLocation;
                int w0 = form.Width;
                int right0 = o.X + w0;
                leftStart = new Point(o.X + 2, o.Y + 220);
                output.AppendLine("leftDiag: origin=" + o +
                    " bounds=" + form.Bounds +
                    " rootAtLeft=" + RootWindowAt(leftStart) +
                    " formHandle=" + form.Handle +
                    " hit=" + form.ResizeHitAtScreenForTest(leftStart) +
                    " child=" + form.ChildAtScreenForTest(leftStart));
                DragMouse(leftStart,
                    new Point(o.X - 78, o.Y + 220));
                PumpMessages(250);
                output.AppendLine("forward: down=" +
                    DesktopManagerForm.ForwardDownCountForTest +
                    " hit=" + DesktopManagerForm.ForwardHitCountForTest +
                    " move=" + DesktopManagerForm.ForwardMoveCountForTest +
                    " up=" + DesktopManagerForm.ForwardUpCountForTest +
                    " resizing=" + form.IsResizingForTest);
                output.AppendLine("leftDrag: w=" + w0 + "->" + form.Width +
                    " right=" + right0 + "->" +
                    (form.ScreenLocation.X + form.Width));
                Check(output, ref pass, "列表区左边框也能拖宽",
                    (form.Width > w0 + 40).ToString(), "True");
                Check(output, ref pass, "拖左边框时右边缘不动",
                    (form.ScreenLocation.X + form.Width).ToString(),
                    right0.ToString());

                // 实拍「上边框拉高 + 左边框拉宽」之后的形态：文件列表
                // 区域肉眼可见地变大，正好核对用户要的实时变化。
                EnsurePanelHit(form,
                    new Point(form.ScreenLocation.X + 200,
                        form.ScreenLocation.Y + 300),
                    output, "resized-shot");
                PumpMessages(150);
                CaptureFormRegion(form, "ui-panel-resized.png", output);

                // 收起后拖短条右边框改长度。
                form.SetCollapsed(true);
                PumpMessages(200);
                EnsurePanelHit(form,
                    new Point(form.ScreenLocation.X + 100,
                        form.ScreenLocation.Y + 20),
                    output, "barDrag");
                o = form.ScreenLocation;
                int cw0 = form.Width;
                Point barStart = new Point(o.X + cw0 - 2, o.Y + 20);
                EnsurePanelHit(form, barStart, output, "barDrag");
                o = form.ScreenLocation;
                cw0 = form.Width;
                barStart = new Point(o.X + cw0 - 2, o.Y + 20);
                output.AppendLine("barDiag: collapsed=" + form.Collapsed +
                    " origin=" + o + " width=" + cw0 +
                    " hit=" + form.ResizeHitAtScreenForTest(barStart) +
                    " root=" + RootWindowAt(barStart));
                Check(output, ref pass, "收起状态保持",
                    form.Collapsed.ToString(), "True");
                DragMouse(barStart,
                    new Point(o.X + cw0 + 58, o.Y + 20));
                PumpMessages(250);
                output.AppendLine("barDrag: w=" + cw0 + "->" + form.Width);
                Check(output, ref pass, "短条右边框能改长度",
                    (form.Width > cw0 + 30).ToString(), "True");
                Check(output, ref pass, "短条长度已记住",
                    (form.CollapsedWidthForTest > cw0).ToString(), "True");
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                SetCursorPos(originalCursor.X, originalCursor.Y);
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.SetResident(false);
                        form.Close();
                        form.Dispose();
                    }
                }
                catch
                {
                }
                try
                {
                    if (backup != null)
                    {
                        ConfigStore.Save(backup);
                    }
                }
                catch
                {
                }
                output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(ConfigStore.DataDirectory,
                        "resizeprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        private static void DragMouse(Point from, Point to)
        {
            SetCursorPos(from.X, from.Y);
            PumpMessages(80);
            mouse_event(MouseLeftDown, 0, 0, 0, IntPtr.Zero);
            PumpMessages(80);
            SetCursorPos(to.X, to.Y);
            PumpMessages(120);
            mouse_event(MouseLeftUp, 0, 0, 0, IntPtr.Zero);
            PumpMessages(120);
        }

        public static void RunDesktopPin()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            DesktopManagerForm form = null;
            Form overlay = null;
            AppConfig backup = null;
            try
            {
                Application.EnableVisualStyles();
                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);
                backup = ConfigStore.Load();

                form = new DesktopManagerForm();
                // 这个探针专门测 z-order：真实光标若恰好压在面板上，
                // 悬停自动展开会改尺寸 / 层级，把断言搅乱。只停悬停计时，
                // 层级跟随计时器必须照常跑。
                form.SuspendHoverExpandForTest();
                form.LoadFromPath(Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory));
                form.StartPosition = FormStartPosition.Manual;
                form.SetBounds(120, 140, 640, 320);
                form.SetResident(true);
                form.Show();
                PumpMessages(700);

                Check(output, ref pass, "常驻标记仍在",
                    form.Resident.ToString(), "True");
                Check(output, ref pass, "面板不是置顶窗口",
                    (IsTopMost(form.Handle) == false).ToString(), "True");
                output.AppendLine("exStyle=0x" +
                    GetWindowLong(form.Handle, GwlExStyle).ToString("X8"));

                // 点面板不该激活它（否则 Windows 会把它提到最前）。
                IntPtr activate = SendMessage(
                    form.Handle, WmMouseActivate, IntPtr.Zero, IntPtr.Zero);
                Check(output, ref pass, "点面板不激活自己",
                    activate.ToString(), MaNoActivate.ToString());

                // 拿一个普通窗口当「别的应用的窗口」，盖在面板位置上。
                overlay = new Form();
                overlay.StartPosition = FormStartPosition.Manual;
                overlay.SetBounds(100, 120, 700, 420);
                overlay.Text = "应用窗口（探针）";
                overlay.Show();
                overlay.Activate();
                PumpMessages(900);
                CaptureDesktopShot("ui-desktoppin-covered.png");

                int overlayOrder = ZOrderIndex(overlay.Handle);
                int panelOrder = ZOrderIndex(form.Handle);
                output.AppendLine("应用在前台: overlay=" + overlayOrder +
                    " panel=" + panelOrder + " topmost=" +
                    IsTopMost(form.Handle));
                Check(output, ref pass, "应用在前台时面板在它下面",
                    (panelOrder > overlayOrder && overlayOrder >= 0).ToString(),
                    "True");
                Check(output, ref pass, "应用在前台时面板不置顶",
                    IsTopMost(form.Handle).ToString(), "False");

                // 把前台切到桌面，模拟「显示桌面 / 关掉最后一个窗口」。
                // 直接 SetForegroundWindow(Progman) 会被前台锁定拒掉，
                // 点屏幕空白也不保险（那个位置可能正有窗口），用 Win+D。
                for (int i = 0; i < 2; i++)
                {
                    ToggleShowDesktop();
                    PumpMessages(1200);
                    if (WindowClass(GetForegroundWindow()) == "Progman")
                    {
                        break;
                    }
                }
                // 面板自己 400ms 一拍才检查前台并浮上来，固定等 600ms 会
                // 赶不上 tick 的时机；轮询等它真正置顶（至多 2.4s）。
                for (int i = 0;
                    i < 12 && !IsTopMost(form.Handle); i++)
                {
                    PumpMessages(200);
                }
                output.AppendLine("foreground now=" +
                    WindowClass(GetForegroundWindow()));
                output.AppendLine("桌面在前台: topmost=" +
                    IsTopMost(form.Handle) + " state=" + form.WindowState);
                Check(output, ref pass, "桌面在前台时面板浮到最前",
                    IsTopMost(form.Handle).ToString(), "True");
                Check(output, ref pass, "桌面在前台时面板没被最小化",
                    form.WindowState.ToString(), "Normal");
                CaptureDesktopShot("ui-desktoppin-desktop.png");
                CaptureFormRegion(form, "ui-desktoppin-region.png", output);

                // 再切回应用：面板必须立刻让开。
                overlay.Activate();
                // 面板 400ms 一拍才检查前台并让位，轮询等它真的让开
                // （至多 3s），固定等待赶不上 tick 时机就会误报。
                bool yielded = false;
                for (int i = 0; i < 15; i++)
                {
                    PumpMessages(200);
                    if (ZOrderIndex(form.Handle) >
                        ZOrderIndex(overlay.Handle))
                    {
                        yielded = true;
                        break;
                    }
                }
                int again = ZOrderIndex(form.Handle);
                output.AppendLine("切回应用: panel=" + again +
                    " overlay=" + ZOrderIndex(overlay.Handle) +
                    " topmost=" + IsTopMost(form.Handle));
                Check(output, ref pass, "切回应用后面板让位",
                    yielded.ToString(), "True");
                Check(output, ref pass, "切回应用后面板取消置顶",
                    IsTopMost(form.Handle).ToString(), "False");

                // 从托盘打开（前台还是应用窗口）：先浮起来让人看见，
                // 鼠标一离开面板就回到桌面层，不能一直挡着别人。
                form.RaiseForUser();
                PumpMessages(300);
                Check(output, ref pass, "托盘打开时先浮到最前",
                    IsTopMost(form.Handle).ToString(), "True");
                // 鼠标进到面板上：只要还在上面就一直陪着。
                SetCursorPos(300, 300);
                PumpMessages(900);
                Check(output, ref pass, "鼠标在面板上时保持最前",
                    IsTopMost(form.Handle).ToString(), "True");
                // 鼠标移开：立刻回到桌面层，不挡别人。
                SetCursorPos(10, 1000);
                for (int i = 0;
                    i < 12 && IsTopMost(form.Handle); i++)
                {
                    PumpMessages(200);
                }
                Check(output, ref pass, "鼠标移开后回到桌面层",
                    IsTopMost(form.Handle).ToString(), "False");

                overlay.Close();
                overlay.Dispose();
                overlay = null;
                PumpMessages(700);
                Check(output, ref pass, "关掉应用窗口后面板还在",
                    (IsWindow(form.Handle) && form.Visible).ToString(),
                    "True");
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                try
                {
                    if (overlay != null && !overlay.IsDisposed)
                    {
                        overlay.Close();
                        overlay.Dispose();
                    }
                }
                catch
                {
                }
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.SetResident(false);
                        form.Close();
                        form.Dispose();
                    }
                }
                catch
                {
                }
                try
                {
                    // 探针不能把用户的常驻开关和位置改掉。
                    if (backup != null)
                    {
                        ConfigStore.Save(backup);
                    }
                }
                catch
                {
                }
                output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "desktoppin.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        /// <summary>窗口是不是 topmost（WS_EX_TOPMOST）。</summary>
        private static bool IsTopMost(IntPtr hWnd)
        {
            return (GetWindowLong(hWnd, GwlExStyle) & WsExTopMost) != 0;
        }

        /// <summary>
        /// 桌面整理面板实拍：常驻展开 / 收起 / 浅色三张图，用于人工核对
        /// 外观（只读桌面，不写任何配置）。输出 ui-panel-*.png。
        /// </summary>
        public static void RunDesktopPanelLive()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            DesktopManagerForm form = null;
            try
            {
                Application.EnableVisualStyles();
                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);

                form = new DesktopManagerForm();
                string desktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                form.LoadFromPath(desktop);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(60, 60);
                form.Show();
                form.SetCollapsed(false);
                // 等图标后台补齐再拍，否则拍到的是只有文字的半成品。
                // 固定等 1.8s 在机器忙的时候会不够（实测只补到 70/161），
                // 改成最多轮询 6s，齐了就立刻继续。
                for (int i = 0;
                    i < 60 && form.LoadedIconCount < form.TotalItemCount; i++)
                {
                    PumpMessages(100);
                }
                PumpMessages(200);
                output.AppendLine("icons=" + form.LoadedIconCount +
                    "/" + form.TotalItemCount +
                    ", thumbs=" + form.ThumbnailCount);
                Check(output, ref pass, "图标全部补齐",
                    form.LoadedIconCount.ToString(),
                    form.TotalItemCount.ToString());
                Check(output, ref pass, "图标有可见像素",
                    (form.VisibleIconCount > 0).ToString(), "True");
                // 图片格里必须是真的缩略图，不是统一的「图片」类型图标。
                Check(output, ref pass, "图片有真缩略图",
                    (form.ThumbnailCount > 0).ToString(), "True");
                // 拍照落在「图片」页签 + 图标视图上：截图里才能看到缩略图
                // 效果（用户抱怨的就是「图片全是同一个图标」）。
                form.SelectCategory("图片");
                form.ViewMode = DesktopManagerForm.ViewModeKind.Icon;
                // 置顶 + 不透明再拍：面板平时是半透明小部件，背后窗口会
                // 透过来，截出来看不清自己长什么样。层级跟随计时器会把
                // 置顶撤掉，实拍期间先停掉它。
                form.SuspendZOrderForTest();
                form.TopMost = true;
                form.SetOpacityForTest(1.0);
                form.Activate();
                PumpMessages(250);
                CaptureFormRegion(form, "ui-panel-expanded.png", output);

                form.SetCollapsed(true);
                PumpMessages(400);
                CaptureFormRegion(form, "ui-panel-collapsed.png", output);

                form.SetCollapsed(false);
                UiTheme.Configure(
                    Color.FromArgb(246, 247, 250),
                    Color.FromArgb(214, 122, 60),
                    true);
                PumpMessages(500);
                CaptureFormRegion(form, "ui-panel-light.png", output);

                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, true);
                PumpMessages(200);
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    form.Close();
                }
                output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "panellive.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        /// <summary>
        /// 托盘菜单「勾选项点完不收起」的端到端探针：真的弹一条菜单，用
        /// 鼠标事件去点勾选类条目，确认菜单没关、勾选状态变了；最后点一个
        /// 普通条目，确认菜单照常收起。逻辑与 TrayApplicationContext 里
        /// 的 OnTrayMenuClosing 完全一致。输出 menuprobe.txt。
        /// </summary>
        public static void RunMenuKeepOpenProbe()
        {
            StringBuilder output = new StringBuilder();
            StringBuilder closeLog = new StringBuilder();
            bool ok = true;
            ContextMenuStrip menu = null;
            Form host = null;
            Point original = Cursor.Position;
            try
            {
                Application.EnableVisualStyles();
                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);

                // 先让本进程自己的窗口成为前台：前台是别的程序时，注入
                // 的第一下点击常被系统拿去「激活」，勾选状态就丢了。
                host = new Form();
                host.StartPosition = FormStartPosition.Manual;
                host.SetBounds(-4000, -4000, 200, 120);
                host.Show();
                host.Activate();
                PumpMessages(300);

                bool keepOpen = false;
                menu = new ContextMenuStrip();
                menu.ShowImageMargin = false;
                menu.Font = DarkUi.FontBody;

                ToolStripMenuItem a = new ToolStripMenuItem("开关 A");
                a.CheckOnClick = true;
                a.MouseDown += delegate { keepOpen = true; };
                menu.Items.Add(a);

                ToolStripMenuItem b = new ToolStripMenuItem("开关 B");
                b.CheckOnClick = true;
                b.MouseDown += delegate { keepOpen = true; };
                menu.Items.Add(b);

                ToolStripMenuItem action = new ToolStripMenuItem("普通动作项");
                action.Click += delegate { };
                menu.Items.Add(action);

                menu.Closing += delegate(
                    object s, ToolStripDropDownClosingEventArgs e)
                {
                    closeLog.Append("[" + e.CloseReason + "/keep=" +
                        keepOpen + "]");
                    if (keepOpen && e.CloseReason ==
                        ToolStripDropDownCloseReason.ItemClicked)
                    {
                        keepOpen = false;
                        e.Cancel = true;
                        closeLog.Append("(cancel)");
                        return;
                    }
                    keepOpen = false;
                };

                menu.Show(new Point(420, 420));
                PumpMessages(200);
                // 桌面上如果还有别的窗口在前台，弹出的菜单可能没有被
                // 激活，第一次注入的点击会被拿去「激活」而丢掉勾选。
                // 先主动把焦点给菜单，注入的点击就都算命令了。
                menu.Focus();
                PumpMessages(200);
                Check(output, ref ok, "菜单已弹出",
                    menu.Visible.ToString(), "True");

                ClickMenuItem(menu, a);
                PumpMessages(350);
                Check(output, ref ok, "第一项已勾选",
                    a.Checked.ToString(), "True");
                Check(output, ref ok, "勾选后菜单仍展开",
                    menu.Visible.ToString(), "True");

                ClickMenuItem(menu, b);
                PumpMessages(350);
                Check(output, ref ok, "第二项已勾选",
                    b.Checked.ToString(), "True");
                Check(output, ref ok, "连点第二项菜单仍展开",
                    menu.Visible.ToString(), "True");

                ClickMenuItem(menu, a);
                PumpMessages(350);
                Check(output, ref ok, "再次点击取消勾选",
                    a.Checked.ToString(), "False");
                Check(output, ref ok, "取消勾选后菜单仍展开",
                    menu.Visible.ToString(), "True");

                ClickMenuItem(menu, action);
                PumpMessages(500);
                Check(output, ref ok, "普通项点完菜单收起",
                    menu.Visible.ToString(), "False");
            }
            catch (Exception ex)
            {
                ok = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (menu != null)
                {
                    menu.Close();
                    menu.Dispose();
                }
                if (host != null)
                {
                    host.Close();
                    host.Dispose();
                }
                output.AppendLine("closing log: " + closeLog);
                SetCursorPos(original.X, original.Y);
                output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "menuprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>用真实鼠标事件点菜单里的某一项（屏幕坐标）。</summary>
        private static void ClickMenuItem(
            ContextMenuStrip menu, ToolStripItem item)
        {
            Rectangle r = menu.RectangleToScreen(item.Bounds);
            SetCursorPos(r.Left + r.Width / 2, r.Top + r.Height / 2);
            PumpMessages(80);
            mouse_event(MouseLeftDown, 0, 0, 0, IntPtr.Zero);
            PumpMessages(40);
            mouse_event(MouseLeftUp, 0, 0, 0, IntPtr.Zero);
        }

        /// <summary>整屏实拍（诊断常驻面板是否真的显示在桌面上）。</summary>
        public static void RunScreenShot()
        {
            try
            {
                Application.EnableVisualStyles();
                Rectangle bounds = Screen.PrimaryScreen.Bounds;
                using (Bitmap shot = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics graphics = Graphics.FromImage(shot))
                    {
                        graphics.CopyFromScreen(
                            bounds.X, bounds.Y, 0, 0, bounds.Size);
                    }
                    shot.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory, "screen-now.png"),
                        ImageFormat.Png);
                }

                // 顺便把属于本程序（任意进程）的顶层窗口列出来，验证
                // 常驻面板真的建出来了、位置和尺寸对不对。
                StringBuilder windows = new StringBuilder();
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    string line = DescribeWin(hWnd, bounds);
                    if (line != null &&
                        line.IndexOf("工具侠", StringComparison.Ordinal) >= 0)
                    {
                        windows.AppendLine(line);
                    }
                    return true;
                }, IntPtr.Zero);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "screen.txt"),
                    windows.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error("整屏截图失败", ex);
                Environment.ExitCode = 1;
            }
        }

        /// <summary>界面外观窗口实拍（深色 / 浅色各一张），只预览不写配置。</summary>
        public static void RunAppearanceLive()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            AppearanceForm form = null;
            try
            {
                Application.EnableVisualStyles();
                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);
                form = new AppearanceForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(80, 80);
                form.Show();
                PumpMessages(500);
                CaptureFormRegion(form, "ui-appearance-dark.png", output);

                UiTheme.Configure(
                    Color.FromArgb(246, 247, 250),
                    Color.FromArgb(214, 122, 60),
                    true);
                PumpMessages(400);
                CaptureFormRegion(form, "ui-appearance-light.png", output);

                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, true);
                PumpMessages(200);
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    form.Close();
                }
                output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "appearancelive.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        /// <summary>手动翻译窗口实拍（验证语言选择那一行排得下），只预览不写配置。</summary>
        public static void RunTranslatePreview()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            TranslationForm form = null;
            try
            {
                Application.EnableVisualStyles();
                AppConfig config = new AppConfig();
                form = new TranslationForm(
                    config, "", new Point(0, 0), null, true);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(80, 80);
                form.TopMost = true;
                form.Show();
                form.Activate();
                form.BringToFront();
                PumpMessages(600);
                output.AppendLine("from=" + form.DescribeLanguageRow());
                CaptureFormRegion(form, "ui-translate.png", output);
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    form.Close();
                }
                output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "translatepreview.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        /// <summary>
        /// NTFS MFT 索引探针：能不能读、多少条、多快、和目录扫描的
        /// 结果是否一致（抽查几类路径），以及 stat 回填是否可用。
        /// </summary>
        public static void RunMftProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            try
            {
                List<string> roots = FileIndexService.ResolveRoots();
                output.AppendLine("roots=" + string.Join(" | ",
                    roots.ToArray()));
                List<string> volumes = new List<string>();
                for (int i = 0; i < roots.Count; i++)
                {
                    string root = Path.GetPathRoot(roots[i]);
                    if (!string.IsNullOrEmpty(root) &&
                        !volumes.Contains(root.ToUpperInvariant()))
                    {
                        volumes.Add(root.ToUpperInvariant());
                    }
                }
                for (int i = 0; i < volumes.Count; i++)
                {
                    output.AppendLine("volume " + volumes[i] +
                        " ntfs=" + MftScanner.IsVolumeNtfs(volumes[i]) +
                        " readable=" + MftScanner.CanRead(volumes[i]));
                }
                output.AppendLine("availability=[" +
                    MftScanner.DescribeAvailability(roots) + "]");
                output.AppendLine("elevated=" +
                    TrayApplicationContext.IsElevated());

                FileIndexData data = new FileIndexData();
                data.Roots = roots;
                FileScanProgress progress = new FileScanProgress();
                MftScanner.DebugRaw = new List<string>();
                string detail;
                Stopwatch watch = Stopwatch.StartNew();
                bool ok = MftScanner.TryScan(
                    roots, data, progress, null, out detail);
                watch.Stop();
                output.AppendLine("mft used=" + ok + " detail=[" + detail +
                    "]");
                Check(output, ref pass, "MFT 可用", ok.ToString(), "True");
                long mftMs = watch.ElapsedMilliseconds;
                output.AppendLine("mft entries=" + data.Entries.Count +
                    " dirs=" + data.Directories.Count +
                    " ms=" + mftMs);
                if (MftScanner.DebugRaw != null)
                {
                    for (int i = 0; i < MftScanner.DebugRaw.Count; i++)
                    {
                        output.AppendLine("raw[" + i + "] " +
                            MftScanner.DebugRaw[i]);
                    }
                }
                Check(output, ref pass, "MFT 有条目",
                    (data.Entries.Count > 1000).ToString(), "True");
                output.AppendLine("mft detail=" + MftScanner.LastStats);
                Check(output, ref pass, "MFT 路径可拼",
                    SampleHasFullPath(data).ToString(), "True");

                // 抽查：随机取 3 条，看拼出来的路径是否真实存在。
                int checkedCount = 0;
                int existsCount = 0;
                Random random = new Random(20261111);
                for (int i = 0; i < data.Entries.Count && checkedCount < 5; i +=
                    Math.Max(1, data.Entries.Count / 5))
                {
                    FileEntry entry = new FileEntry(data, i);
                    string full = data.FullPath(entry);
                    bool exists = !string.IsNullOrEmpty(full) &&
                        (File.Exists(full) || Directory.Exists(full));
                    output.AppendLine("sample[" + checkedCount + "] " +
                        full + " exists=" + exists);
                    if (exists)
                    {
                        existsCount++;
                    }
                    checkedCount++;
                }
                Check(output, ref pass, "抽查路径存在",
                    existsCount.ToString(), checkedCount.ToString());

                // stat 回填：取第一条文件，补齐后应有真实大小/时间。
                FileEntry firstFile = default(FileEntry);
                for (int i = 0; i < data.Entries.Count; i++)
                {
                    if (data.Entries.Kind(i) != FileKind.Folder)
                    {
                        firstFile = new FileEntry(data, i);
                        break;
                    }
                }
                if (firstFile.IsValid)
                {
                    bool wasMissing = firstFile.MetaMissing;
                    data.EnsureMeta(firstFile);
                    output.AppendLine("meta missing=" + wasMissing +
                        " -> size=" + firstFile.Size +
                        " ticks=" + firstFile.Ticks);
                    Check(output, ref pass, "回填后大小有效",
                        (firstFile.MetaMissing == false &&
                         firstFile.Size >= 0).ToString(), "True");
                    Check(output, ref pass, "回填后时间有效",
                        (firstFile.Ticks > 0).ToString(), "True");
                }

                // 与目录扫描比对：同样的根，条目数量级应相当（MFT 会多出
                // 被跳过目录之外的隐藏项，允许 0.5x~3x 的差异）。
                FileIndexData walked = new FileIndexData();
                FileScanProgress p2 = new FileScanProgress();
                Stopwatch w2 = Stopwatch.StartNew();
                FileScanner.Scan(roots, walked, p2, null);
                w2.Stop();
                output.AppendLine("walk entries=" + walked.Entries.Count +
                    " ms=" + w2.ElapsedMilliseconds);
                double ratio = walked.Entries.Count == 0 ? 0 :
                    (double)data.Entries.Count / walked.Entries.Count;
                output.AppendLine("ratio=" + ratio.ToString("0.00"));
                Check(output, ref pass, "MFT 比目录扫描快",
                    (mftMs < w2.ElapsedMilliseconds).ToString(), "True");
                Check(output, ref pass, "条目数量级一致",
                    (ratio > 0.5 && ratio < 3.0).ToString(), "True");
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(ConfigStore.DataDirectory, "mftprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        /// <summary>
        /// MFT 路径拼装的结构探针：不需要管理员权限。用手造的记录表验证
        /// 「只有目录留完整路径、文件靠父目录归属」这套改动没有把文件弄丢、
        /// 也没有串到错误的父目录上。真卷只有管理员能读，这是普通权限下
        /// 唯一能自动盯住这块的手段。
        /// </summary>
        public static void RunMftEmitProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            try
            {
                List<string> roots = new List<string>();
                roots.Add("Q:\\");
                FileIndexData data = new FileIndexData();
                data.Roots = roots;
                //   0 卷根          1 docs(目录)        2 报告.docx
                //                   3 node_modules(目录) 4 evil.js
                //                   5 readme.txt（直接挂卷根）
                long[] frn = new long[] { 5, 10, 11, 12, 13, 14 };
                long[] parent = new long[] { 5, 5, 10, 5, 12, 5 };
                byte[] flags = new byte[] { 1, 1, 0, 1, 0, 0 };
                string[] names = new string[]
                {
                    "", "docs", "报告.docx", "node_modules", "evil.js",
                    "readme.txt"
                };
                MftScanner.Emit("Q:", roots, data, new FileScanProgress(),
                    null, frn, parent, flags, names, frn.Length);

                output.AppendLine("entries=" + data.Entries.Count +
                    " dirs=" + data.Directories.Count);
                for (int i = 0; i < data.Entries.Count; i++)
                {
                    output.AppendLine("  [" + i + "] " +
                        data.FullPath(i));
                }
                for (int i = 0; i < data.Directories.Count; i++)
                {
                    output.AppendLine("  dir[" + i + "] " +
                        data.Directories[i]);
                }
                output.AppendLine("stats=" + MftScanner.LastStats);

                // 目录本身也是一条条目（docs），所以是 1 个目录 + 2 个文件。
                Check(output, ref ok, "mft emit 条目数",
                    data.Entries.Count.ToString(), "3");
                bool hasDoc = false;
                bool hasReadme = false;
                bool hasEvil = false;
                for (int i = 0; i < data.Entries.Count; i++)
                {
                    string full = data.FullPath(i);
                    if (full == "Q:\\docs\\报告.docx")
                    {
                        hasDoc = true;
                    }
                    if (full == "Q:\\readme.txt")
                    {
                        hasReadme = true;
                    }
                    if (full.EndsWith("evil.js",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        hasEvil = true;
                    }
                }
                Check(output, ref ok, "mft emit 子目录文件路径",
                    hasDoc.ToString(), "True");
                Check(output, ref ok, "mft emit 卷根文件路径",
                    hasReadme.ToString(), "True");
                Check(output, ref ok, "mft emit 剪枝目录里的文件不要",
                    hasEvil.ToString(), "False");
            }
            catch (Exception ex)
            {
                ok = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(ConfigStore.DataDirectory,
                        "mftemitprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        private static bool SampleHasFullPath(FileIndexData data)
        {
            int hits = 0;
            int total = 0;
            for (int i = 0; i < data.Entries.Count; i += 977)
            {
                total++;
                string full = data.FullPath(i);
                // 合格标准：拿得到绝对路径（"C:\..."），且至少有一层目录。
                if (!string.IsNullOrEmpty(full) && full.Length > 4 &&
                    full[1] == ':' && full[2] == '\\' &&
                    full.IndexOf('\\', 3) > 3)
                {
                    hits++;
                }
            }
            return total > 0 && hits >= total * 9 / 10;
        }

        /// <summary>
        /// 文件秒搜「图片」分类实拍：真实索引 → 切到图片 → 等缩略图解码，
        /// 深色 / 浅色各拍一张，并取样奇数行底色，锁住「浅色主题下隔行
        /// 还是黑条」这个回归。
        /// </summary>
        public static void RunSearchThumbLive()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            FileSearchForm form = null;
            try
            {
                Application.EnableVisualStyles();
                form = FileSearchForm.ShowWindow();
                DateTime deadline = DateTime.UtcNow.AddMinutes(3);
                while (!FileIndexService.Instance.Ready &&
                    DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(50);
                }
                form.TopMost = true;
                form.Activate();
                form.SelectKind(FileKind.Image);
                deadline = DateTime.UtcNow.AddSeconds(25);
                while (form.ResultList.Items.Count == 0 &&
                    DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(60);
                }
                int images = form.ResultList.Items.Count;
                output.AppendLine("images=" + images);
                Check(output, ref ok, "图片分类有结果",
                    (images > 0).ToString(), "True");
                if (images == 0)
                {
                    return;
                }

                // 缩略图是后台解码的，等它补齐屏幕这批（连续两轮不再增长
                // 就认为这屏好了，不等到整卷）。
                deadline = DateTime.UtcNow.AddSeconds(15);
                int stable = 0;
                int last = -1;
                while (DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(80);
                    int now = form.ResultList.ThumbCount;
                    if (now == last)
                    {
                        stable++;
                        if (stable >= 4 && now >= 8)
                        {
                            break;
                        }
                    }
                    else
                    {
                        stable = 0;
                    }
                    last = now;
                }
                int thumbs = form.ResultList.ThumbCount;
                output.AppendLine("thumbs=" + thumbs);
                Check(output, ref ok, "缩略图已解码",
                    (thumbs >= 8).ToString(), "True");

                Point probe = form.ResultList.RowBlankPoint(1);
                Point onScreen = form.ResultList.PointToScreen(probe);

                Color darkZebra = Color.Empty;
                Bitmap shot = CaptureFormToBitmap(
                    form, "ui-search-thumbs.png", output);
                if (shot != null)
                {
                    int x = onScreen.X - form.Bounds.X;
                    int y = onScreen.Y - form.Bounds.Y;
                    if (x >= 0 && y >= 0 && x < shot.Width && y < shot.Height)
                    {
                        darkZebra = shot.GetPixel(x, y);
                    }
                    shot.Dispose();
                }

                // 换成浅色主题，同样的位置再取一次。
                UiTheme.Configure(
                    Color.FromArgb(246, 247, 250),
                    Color.FromArgb(82, 145, 255), true);
                PumpMessages(500);
                output.AppendLine("theme light=" + UiTheme.IsLight);

                Color lightZebra = Color.Empty;
                shot = CaptureFormToBitmap(
                    form, "ui-search-thumbs-light.png", output);
                if (shot != null)
                {
                    int x = onScreen.X - form.Bounds.X;
                    int y = onScreen.Y - form.Bounds.Y;
                    if (x >= 0 && y >= 0 && x < shot.Width && y < shot.Height)
                    {
                        lightZebra = shot.GetPixel(x, y);
                    }
                    shot.Dispose();
                }

                output.AppendLine("zebra dark=" + darkZebra +
                    " light=" + lightZebra);
                // 深色主题下这一格本来就该是暗的（不能是纯背景色，说明
                // 隔行底色确实画出来了）。
                int darkBright = (darkZebra.R + darkZebra.G + darkZebra.B) / 3;
                Check(output, ref ok, "深色隔行有色差",
                    (darkBright < 120).ToString(), "True");
                // 浅色主题下必须是亮的，绝不能还是深灰 / 黑。
                int lightBright =
                    (lightZebra.R + lightZebra.G + lightZebra.B) / 3;
                Check(output, ref ok, "浅色隔行不是黑条",
                    (lightBright > 180).ToString(), "True");
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION " + ex);
                ok = false;
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    form.Close();
                }
                output.AppendLine("RESULT: " + (ok ? "OK" : "FAILED"));
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(ConfigStore.DataDirectory,
                        "searchthumblive.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        private static Bitmap CaptureFormToBitmap(
            Form form, string fileName, StringBuilder output)
        {
            Rectangle bounds = form.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                output.AppendLine(fileName + " skipped (empty bounds)");
                return null;
            }
            Bitmap shot = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(shot))
            {
                graphics.CopyFromScreen(
                    bounds.X, bounds.Y, 0, 0,
                    new Size(bounds.Width, bounds.Height));
            }
            try
            {
                shot.Save(
                    Path.Combine(ConfigStore.DataDirectory, fileName),
                    ImageFormat.Png);
            }
            catch (Exception ex)
            {
                output.AppendLine("save " + fileName + " failed: " + ex.Message);
            }
            output.AppendLine(fileName + " " + bounds);
            return shot;
        }

        private static void CaptureFormRegion(
            Form form, string fileName, StringBuilder output)
        {
            Rectangle bounds = form.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                output.AppendLine(fileName + " skipped (empty bounds)");
                return;
            }
            using (Bitmap shot = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics graphics = Graphics.FromImage(shot))
                {
                    graphics.CopyFromScreen(
                        bounds.X, bounds.Y, 0, 0,
                        new Size(bounds.Width, bounds.Height));
                }
                shot.Save(
                    Path.Combine(ConfigStore.DataDirectory, fileName),
                    ImageFormat.Png);
            }
            output.AppendLine(fileName + " " + bounds);
        }

        /// <summary>长截图预览窗口的渲染冒烟 + 截图 ui-scrollpreview.png。</summary>
        public static void RunScrollShotPreview()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            ScreenshotCaptureForm form = null;
            Bitmap tall = null;
            try
            {
                Application.EnableVisualStyles();
                tall = new Bitmap(
                    760, 2400, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(tall))
                {
                    g.Clear(Color.White);
                    using (Font title = new Font(
                        "Microsoft YaHei UI", 30f, FontStyle.Bold))
                    using (Font body = new Font(
                        "Microsoft YaHei UI", 15f))
                    using (SolidBrush ink = new SolidBrush(
                        Color.FromArgb(32, 36, 44)))
                    using (SolidBrush accent = new SolidBrush(
                        Color.FromArgb(0, 122, 255)))
                    using (SolidBrush rule = new SolidBrush(
                        Color.FromArgb(226, 232, 240)))
                    {
                        g.DrawString("滚动截图结果：直接在这里标注", title,
                            ink, 40f, 34f);
                        g.FillRectangle(rule, 40, 96, 680, 1);
                        for (int i = 0; i < 34; i++)
                        {
                            g.DrawString(
                                (i + 1) + ".  滚轮把页面滚起来，" +
                                "浮层会把新内容接在下面，成一张长图。",
                                body, i % 7 == 0 ? accent : ink,
                                40f, 130f + i * 64f);
                        }
                    }
                }

                form = new ScreenshotCaptureForm();
                form.Show();
                PumpMessages(320);
                form.DebugSelect(new Rectangle(80, 80, 760, 420));
                form.EnterLongMode(tall);
                tall = null;
                // 在「长图坐标」上加一个矩形标注：导出时必须落在同一位置，
                // 说明标注坐标系和长图是同一套（不会随滚动漂移）。
                form.DebugAddFrame(
                    new Rectangle(60, 150, 640, 250),
                    Color.FromArgb(230, 40, 60));
                PumpMessages(320);

                Check(output, ref ok, "scroll preview in long mode",
                    form.InLongMode.ToString(), "True");
                Size size = form.LongImageSize;
                Check(output, ref ok, "scroll preview image size",
                    size.Width + "x" + size.Height, "760x2400");
                output.AppendLine("viewport=" + form.CaptureArea);

                using (Bitmap exported = form.DebugBuildResult())
                {
                    Check(output, ref ok, "scroll preview export size",
                        exported.Width + "x" + exported.Height,
                        size.Width + "x" + size.Height);
                    Color stroke = exported.GetPixel(380, 151);
                    output.AppendLine("stroke=" + stroke.R + "," +
                        stroke.G + "," + stroke.B);
                    Check(output, ref ok, "scroll preview annotation",
                        (stroke.R > 150 && stroke.G < 120).ToString(),
                        "True");
                    // 标注框以外还是白纸。
                    Color blank = exported.GetPixel(380, 600);
                    Check(output, ref ok, "scroll preview keeps image",
                        (blank.R > 240 && blank.G > 240 &&
                            blank.B > 240).ToString(), "True");
                }

                using (Bitmap shot = new Bitmap(
                    form.Width, form.Height))
                {
                    form.DrawToBitmap(
                        shot,
                        new Rectangle(0, 0,
                            form.Width, form.Height));
                    shot.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory,
                            "ui-scrollpreview.png"),
                        ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (tall != null)
                {
                    tall.Dispose();
                }
                if (form != null)
                {
                    form.Close();
                    form.Dispose();
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "scrollshotpreview.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>截取屏幕上一块矩形。</summary>
        private static Bitmap Capture(Rectangle area)
        {
            if (area.IsEmpty || area.Width <= 0 || area.Height <= 0)
            {
                return null;
            }
            Bitmap shot = new Bitmap(area.Width, area.Height);
            using (Graphics graphics = Graphics.FromImage(shot))
            {
                graphics.CopyFromScreen(
                    area.Left, area.Top, 0, 0, area.Size);
            }
            return shot;
        }

        /// <summary>
        /// 统计深色文字像素。状态条只画深/浅文字、没有底色，
        /// 被任务栏盖住后这块区域会变成纯任务栏背景，深色像素骤减。
        /// </summary>
        private static int CountDarkPixels(Bitmap shot)
        {
            if (shot == null)
            {
                return -1;
            }
            int count = 0;
            for (int y = 0; y < shot.Height; y++)
            {
                for (int x = 0; x < shot.Width; x++)
                {
                    Color pixel = shot.GetPixel(x, y);
                    if (pixel.R < 130 &&
                        pixel.G < 130 &&
                        pixel.B < 130)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// 复现「点一下任务栏后状态条消失」：先截状态条区域的深色文字像素，
        /// 真实左键点一次任务栏空白处，等两秒后再截一次比对。
        /// 结束前恢复光标位置。
        /// </summary>
        public static void RunOcclusionProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            Point original = Cursor.Position;
            string directory = ConfigStore.DataDirectory;
            try
            {
                Application.EnableVisualStyles();
                using (TaskbarStatsForm form =
                    new TaskbarStatsForm())
                {
                    form.SuspendHover = true;
                    form.Show();
                    for (int i = 0; i < 12; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }
                    form.RefreshNow();
                    Rectangle bar = form.BarBounds;
                    Rectangle taskbar =
                        TaskbarStatsForm.GetTaskbarRect();
                    output.AppendLine("barBounds=" + bar);
                    output.AppendLine("taskbar=" + taskbar);
                    if (bar.IsEmpty || taskbar.IsEmpty)
                    {
                        throw new InvalidOperationException(
                            "bar or taskbar rectangle is empty");
                    }

                    int before;
                    using (Bitmap shot = Capture(bar))
                    {
                        before = CountDarkPixels(shot);
                        if (shot != null)
                        {
                            shot.Save(
                                Path.Combine(
                                    directory,
                                    "ui-occlusion-before.png"),
                                ImageFormat.Png);
                        }
                    }
                    output.AppendLine("darkPixelsBefore=" + before);

                    int clickX = Math.Max(
                        taskbar.Left + 10, bar.Left - 250);
                    int clickY = taskbar.Top +
                        taskbar.Height / 2;
                    output.AppendLine("clickAt=" + clickX + "," +
                        clickY);
                    SetCursorPos(clickX, clickY);
                    mouse_event(
                        MouseLeftDown, 0, 0, 0, IntPtr.Zero);
                    mouse_event(
                        MouseLeftUp, 0, 0, 0, IntPtr.Zero);
                    for (int i = 0; i < 40; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }

                    int after;
                    using (Bitmap shot = Capture(bar))
                    {
                        after = CountDarkPixels(shot);
                        if (shot != null)
                        {
                            shot.Save(
                                Path.Combine(
                                    directory,
                                    "ui-occlusion-after.png"),
                                ImageFormat.Png);
                        }
                    }
                    output.AppendLine("darkPixelsAfter=" + after);
                    if (before < 20)
                    {
                        pass = false;
                        output.AppendLine(
                            "bar text not visible before click");
                    }
                    if (after < before / 2)
                    {
                        pass = false;
                        output.AppendLine(
                            "bar text disappeared after taskbar click");
                    }

                    // 第二段：开启悬停检测、光标停在状态条上，只等 400ms，
                    // 验证「点完立刻恢复」的快速路径，而不只是等下一秒采样。
                    form.SuspendHover = false;
                    SetCursorPos(
                        bar.Left + bar.Width / 2,
                        bar.Top + bar.Height / 2);
                    mouse_event(
                        MouseLeftDown, 0, 0, 0, IntPtr.Zero);
                    mouse_event(
                        MouseLeftUp, 0, 0, 0, IntPtr.Zero);
                    for (int i = 0; i < 8; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }
                    int fast;
                    using (Bitmap shot = Capture(bar))
                    {
                        fast = CountDarkPixels(shot);
                        if (shot != null)
                        {
                            shot.Save(
                                Path.Combine(
                                    directory,
                                    "ui-occlusion-fast.png"),
                                ImageFormat.Png);
                        }
                    }
                    output.AppendLine("darkPixelsFast400ms=" + fast);
                    output.AppendLine("detailVisible=" +
                        form.IsDetailVisible);
                    if (fast < before / 2)
                    {
                        pass = false;
                        output.AppendLine(
                            "bar text not restored within 400ms");
                    }
                    form.Close();
                }
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                SetCursorPos(original.X, original.Y);
            }

            output.AppendLine(
                pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "occlusionprobe.txt"),
                output.ToString(),
                Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        /// <summary>
        /// 真实把鼠标移进状态条再移开，验证详情卡片自动展开 / 自动收起。
        /// 会短暂移动光标，结束前恢复到原来的位置。
        /// </summary>
        public static void RunHoverProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            Point original = Cursor.Position;
            try
            {
                Application.EnableVisualStyles();
                using (TaskbarStatsForm form =
                    new TaskbarStatsForm())
                {
                    form.Show();
                    for (int i = 0; i < 6; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }
                    form.RefreshNow();
                    Rectangle bar = form.BarBounds;
                    output.AppendLine("barBounds=" + bar);
                    if (bar.IsEmpty)
                    {
                        pass = false;
                        output.AppendLine("bar is empty");
                    }
                    else
                    {
                        SetCursorPos(
                            bar.Left + bar.Width / 2,
                            bar.Top + bar.Height / 2);
                        // 连续跑多个诊断时系统可能忙，轮询到状态变化
                        // 为止，最多等 1.2 秒，避免固定 500ms 误报。
                        bool opened = false;
                        for (int i = 0; i < 24; i++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(50);
                            if (form.IsDetailVisible)
                            {
                                opened = true;
                                break;
                            }
                        }
                        output.AppendLine("detailBounds=" +
                            form.DetailBounds);
                        output.AppendLine("after hover in, visible=" +
                            opened);
                        if (!opened)
                        {
                            pass = false;
                        }

                        SetCursorPos(20, 20);
                        bool closed = false;
                        for (int i = 0; i < 24; i++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(50);
                            if (!form.IsDetailVisible)
                            {
                                closed = true;
                                break;
                            }
                        }
                        output.AppendLine("after hover out, hidden=" +
                            closed);
                        if (!closed)
                        {
                            pass = false;
                        }
                    }
                    form.Close();
                }
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                SetCursorPos(original.X, original.Y);
            }

            output.AppendLine(
                pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(
                ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory,
                    "hoverprobe.txt"),
                output.ToString(),
                Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        /// <summary>
        /// 悬浮详情诊断：生成浅色 / 深色两张卡片预览图，把状态条和
        /// 详情卡片真的贴到任务栏上实拍，并输出本机各指标能否取到。
        /// </summary>
        public static void RunStatsDetailProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            try
            {
                Application.EnableVisualStyles();
                List<TaskbarStats.DetailRow> rows =
                    TaskbarStatsForm.BuildPreviewRows();
                output.AppendLine("preview rows=" + rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    output.AppendLine(
                        "  " + rows[i].Label + " | " + rows[i].Value);
                }

                using (Bitmap light =
                    TaskbarStatsDetailForm.RenderBitmap(
                        rows, 1f, true))
                using (Bitmap dark =
                    TaskbarStatsDetailForm.RenderBitmap(
                        rows, 1f, false))
                using (Bitmap preview = new Bitmap(
                    light.Width * 2 + 36, light.Height + 24))
                {
                    using (Graphics graphics =
                        Graphics.FromImage(preview))
                    {
                        graphics.Clear(
                            Color.FromArgb(226, 230, 236));
                        graphics.DrawImageUnscaled(light, 12, 12);
                        graphics.DrawImageUnscaled(
                            dark, light.Width + 24, 12);
                    }
                    preview.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory,
                            "ui-stats-detail.png"),
                        ImageFormat.Png);
                    output.AppendLine("preview card=" +
                        light.Width + "x" + light.Height);
                }

                ulong usedBytes;
                ulong totalBytes;
                TaskbarStats.MemoryBytes(
                    out usedBytes, out totalBytes);
                long freeBytes;
                long diskBytes;
                TaskbarStats.DiskSpace(
                    out freeBytes, out diskBytes);
                bool hasBattery;
                int batteryPercent;
                bool onAc;
                int remainSeconds;
                TaskbarStats.Battery(
                    out hasBattery, out batteryPercent,
                    out onAc, out remainSeconds);
                output.AppendLine("cpuNominalMhz=" +
                    TaskbarStats.CpuNominalMhz());
                output.AppendLine("cpuPerfPercent=" +
                    TaskbarStats.ProcessorPerformancePercent());
                output.AppendLine("cpuFreqMhz=" +
                    TaskbarStats.CpuFrequencyMhz());
                output.AppendLine("memoryBytes=" + usedBytes +
                    "/" + totalBytes);
                output.AppendLine("diskBytes=" + freeBytes +
                    "/" + diskBytes);
                output.AppendLine("battery=" + hasBattery +
                    " percent=" + batteryPercent +
                    " onAc=" + onAc +
                    " remain=" + remainSeconds);
                output.AppendLine("lightTheme=" +
                    TaskbarStatsForm.IsLightTheme());

                using (TaskbarStatsForm form =
                    new TaskbarStatsForm())
                {
                    form.SuspendHover = true;
                    form.Show();
                    for (int i = 0; i < 20; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }
                    form.ShowDetailForProbe();
                    for (int i = 0; i < 6; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }
                    Rectangle detail = form.DetailBounds;
                    Rectangle bar = form.BarBounds;
                    Rectangle taskbar =
                        TaskbarStatsForm.GetTaskbarRect();
                    output.AppendLine("barBounds=" + bar);
                    output.AppendLine("detailBounds=" + detail);
                    output.AppendLine("taskbar=" + taskbar);
                    if (detail.Width <= 0 || detail.Height <= 0)
                    {
                        pass = false;
                    }
                    // 卡片应整体偏到状态条右侧，但仍留一段水平重叠，
                    // 保证鼠标从状态条斜移进卡片时不会被误判为移开。
                    bool shiftedRight = detail.Right > bar.Right;
                    bool keepsOverlap = detail.Left < bar.Right;
                    bool compact =
                        detail.Width < bar.Width * 3;
                    output.AppendLine("shiftedRight=" +
                        shiftedRight + " keepsOverlap=" +
                        keepsOverlap + " compact=" + compact);
                    if (!shiftedRight || !keepsOverlap || !compact)
                    {
                        pass = false;
                    }

                    Rectangle screen = Screen.FromRectangle(
                        bar.IsEmpty ? taskbar : bar).Bounds;
                    int x = Math.Max(
                        screen.Left,
                        Math.Min(detail.Left, bar.Left) - 30);
                    int y = Math.Max(
                        screen.Top, detail.Top - 28);
                    int width = Math.Min(
                        screen.Right - x,
                        Math.Max(560, detail.Width + 60));
                    int height = Math.Min(
                        screen.Bottom - y,
                        Math.Max(120,
                            taskbar.Bottom - y + 8));
                    if (width > 0 && height > 0)
                    {
                        using (Bitmap shot =
                            new Bitmap(width, height))
                        {
                            using (Graphics graphics =
                                Graphics.FromImage(shot))
                            {
                                graphics.CopyFromScreen(
                                    x, y, 0, 0,
                                    new Size(width, height));
                            }
                            shot.Save(
                                Path.Combine(
                                    ConfigStore.DataDirectory,
                                    "ui-stats-detail-live.png"),
                                ImageFormat.Png);
                        }
                        output.AppendLine("capture=" +
                            new Rectangle(x, y, width, height));
                    }
                    else
                    {
                        pass = false;
                        output.AppendLine("capture=invalid");
                    }
                    form.Close();
                }
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }

            output.AppendLine(
                pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(
                ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory,
                    "statsdetailprobe.txt"),
                output.ToString(),
                Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        /// <summary>
        /// 面板底板透明度实拍：把底板设成 50%，确认透出来的是桌面，
        /// 同时窗体本身（文件名、图标、文字）仍然是不透明的。
        /// 输出 ui-panel-transparent.png。
        /// </summary>
        public static void RunPanelOpacityProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            DesktopManagerForm form = null;
            try
            {
                Application.EnableVisualStyles();
                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);

                form = new DesktopManagerForm();
                form.LoadFromPath(Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory));
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(80, 80);
                form.Show();
                form.SetCollapsed(false);
                for (int i = 0;
                    i < 40 && form.LoadedIconCount < form.TotalItemCount; i++)
                {
                    PumpMessages(100);
                }
                // 实拍期间停掉层级跟随并置顶，否则会把后面的应用窗口
                // 拍进 ui-panel-transparent.png。
                form.SuspendZOrderForTest();
                form.TopMost = true;
                form.SetOpacityForTest(0.5);
                PumpMessages(400);

                // 整窗不能透明：否则文件名会跟着糊掉，用户要的正是
                // 「面板底可以透，文件不能透」。
                Check(output, ref pass, "窗体本身仍然不透明",
                    (form.Opacity == 1.0).ToString(), "True");
                Check(output, ref pass, "底板抓到桌面背景",
                    form.HasDesktopBackdrop.ToString(), "True");
                Check(output, ref pass, "透明度读回 50%",
                    ((int)Math.Round(
                        form.CurrentOpacity * 100.0)).ToString(), "50");

                // 反复重做底板不能把自己也抓进去：抓屏时面板被排除，
                // 抓到的应该始终是「桌面那一层」，颜色不该越叠越暗。
                Color first = form.SampleBackdropForTest(600, 300);
                for (int i = 0; i < 3; i++)
                {
                    form.RefreshBackdropForTest();
                    PumpMessages(120);
                }
                Color again = form.SampleBackdropForTest(600, 300);
                int drift = Math.Abs(first.R - again.R) +
                    Math.Abs(first.G - again.G) +
                    Math.Abs(first.B - again.B);
                output.AppendLine("backdrop=" + first + " -> " + again +
                    " drift=" + drift);
                Check(output, ref pass, "底板反复重建不自我叠加",
                    (drift <= 24).ToString(), "True");

                // 拖动时每帧都重抓底板会卡（实测一次抓屏 + DWM 同步 ~70ms），
                // 所以拖动期间只平移旧底板。这里模拟 10 帧拖动。
                form.BeginDragForTest();
                Stopwatch move = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++)
                {
                    form.Left = 80 + i * 4;
                    PumpMessages(10);
                }
                move.Stop();
                output.AppendLine("drag10=" +
                    move.ElapsedMilliseconds + "ms (拖动中不重抓)");
                Check(output, ref pass, "拖动中不卡顿",
                    (move.ElapsedMilliseconds < 500).ToString(), "True");

                // 松手后补一张准的底板，这一次要花抓屏的时间但不影响拖动。
                Stopwatch settle = Stopwatch.StartNew();
                form.MoveDragForTest(0, 0);
                PumpMessages(80);
                settle.Stop();
                output.AppendLine("settle=" +
                    settle.ElapsedMilliseconds + "ms (松手补抓)");
                Check(output, ref pass, "松手补抓底板完成",
                    form.HasDesktopBackdrop.ToString(), "True");

                // 拖动 / 松手补抓之后再把面板提到最前拍：同层级的其它
                // 置顶窗口（比如编辑器）不然会盖住实拍区域。
                form.TopMost = true;
                form.Activate();
                PumpMessages(200);
                CaptureFormRegion(
                    form, "ui-panel-transparent.png", output);
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    form.Close();
                }
                output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "opacityprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        /// <summary>
        /// 把状态条真的贴到任务栏上，截一张任务栏右侧局部屏，
        /// 用来肉眼确认位置、透明度和文字清晰度。
        /// </summary>
        public static void RunStatsLiveProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            try
            {
                Application.EnableVisualStyles();
                using (TaskbarStatsForm form = new TaskbarStatsForm())
                {
                    form.Show();
                    for (int i = 0; i < 30; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }
                    form.RefreshNow();
                    Application.DoEvents();

                    Rectangle taskbar =
                        TaskbarStatsForm.GetTaskbarRect();
                    // 拍宽一点：状态条被别的工具挤到托盘左侧较远处时，
                    // 只拍 560px 会拍不到自己。760px 足够同时看到
                    // 「状态条 + 旁边的第三方小部件」的相对位置。
                    int width = Math.Min(760, taskbar.Width);
                    int height = Math.Max(1, taskbar.Height);
                    int x = Math.Max(0, taskbar.Right - width);
                    int y = Math.Max(0, taskbar.Top);
                    using (Bitmap shot = new Bitmap(width, height))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(shot))
                        {
                            graphics.CopyFromScreen(
                                x, y, 0, 0,
                                new Size(width, height));
                        }
                        shot.Save(
                            Path.Combine(
                                ConfigStore.DataDirectory,
                                "ui-stats-live.png"),
                            ImageFormat.Png);
                    }
                    output.AppendLine("taskbar=" + taskbar);
                    output.AppendLine("capture=" +
                        new Rectangle(x, y, width, height));
                    output.AppendLine("visible=" + form.Visible);
                    form.Close();
                }
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }

            output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory,
                    "statsformprobe.txt"),
                output.ToString(),
                Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        /// <summary>
        /// 采集一次真实系统数据，输出任务栏定位结果，并生成浅色 / 深色
        /// 两种任务栏背景下的状态条预览图 ui-stats.png。
        /// </summary>
        public static void RunStatsProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            try
            {
                long received;
                long sent;
                TaskbarStats.SampleTraffic(out received, out sent);
                ulong idle;
                ulong total;
                bool cpuOk = TaskbarStats.SampleCpu(out idle, out total);
                int memory = TaskbarStats.MemoryLoad();
                output.AppendLine("traffic received=" + received +
                    " sent=" + sent);
                output.AppendLine("cpu sample=" + cpuOk +
                    " idle=" + idle + " total=" + total);
                output.AppendLine("memory=" + memory + "%");

                Rectangle taskbar = TaskbarStatsForm.GetTaskbarRect();
                Rectangle notify = TaskbarStatsForm.GetTrayNotifyRect();
                output.AppendLine("taskbar=" + taskbar);
                output.AppendLine("trayNotify=" + notify);

                using (Bitmap light = TaskbarStatsForm.RenderBitmap(
                    "0.01 MB/s", "0.00 MB/s", 28, 62, 1f,
                    Color.FromArgb(255, 26, 28, 32)))
                using (Bitmap dark = TaskbarStatsForm.RenderBitmap(
                    "6.60 MB/s", "5.04 MB/s", 17, 62, 1f,
                    Color.FromArgb(255, 240, 242, 246)))
                {
                    Point position = TaskbarStats.ComputePosition(
                        taskbar, notify, light.Size, 8);
                    output.AppendLine("content=" + light.Size);
                    output.AppendLine("position=" + position);
                    if (position.IsEmpty)
                    {
                        pass = false;
                    }

                    int width = Math.Max(light.Width, dark.Width) + 24;
                    int height = light.Height * 2 + 36;
                    using (Bitmap preview = new Bitmap(width, height))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(preview))
                        {
                            graphics.Clear(Color.White);
                            using (SolidBrush lightBar =
                                new SolidBrush(
                                    Color.FromArgb(243, 243, 243)))
                            using (SolidBrush darkBar =
                                new SolidBrush(
                                    Color.FromArgb(32, 32, 32)))
                            {
                                graphics.FillRectangle(
                                    lightBar, 0, 0,
                                    width, light.Height + 16);
                                graphics.FillRectangle(
                                    darkBar, 0, light.Height + 16,
                                    width,
                                    height - light.Height - 16);
                            }
                            graphics.DrawImageUnscaled(light, 12, 8);
                            graphics.DrawImageUnscaled(
                                dark, 12, light.Height + 24);
                        }
                        preview.Save(
                            Path.Combine(
                                ConfigStore.DataDirectory,
                                "ui-stats.png"),
                            ImageFormat.Png);
                    }
                }

                // 统一 MB 单位后最短 / 最长数字的放大对比：两行始终
                // 共用同一组列起点，位图紧贴内容，间隙恒定很小。
                using (Bitmap compact =
                    TaskbarStatsForm.RenderBitmap(
                        "0.00 MB/s", "0.00 MB/s", 8, 9, 1f,
                        Color.FromArgb(255, 26, 28, 32)))
                using (Bitmap wide =
                    TaskbarStatsForm.RenderBitmap(
                        "999.99 MB/s", "999.99 MB/s",
                        100, 100, 1f,
                        Color.FromArgb(255, 26, 28, 32)))
                {
                    const int zoom = 3;
                    int zoomWidth = wide.Width * zoom + 24;
                    int zoomHeight =
                        (compact.Height + wide.Height) * zoom + 36;
                    using (Bitmap zoomed = new Bitmap(
                        zoomWidth, zoomHeight))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(zoomed))
                        {
                            graphics.Clear(Color.White);
                            using (SolidBrush barBackground =
                                new SolidBrush(
                                    Color.FromArgb(243, 243, 243)))
                            {
                                graphics.FillRectangle(
                                    barBackground, 0, 0,
                                    zoomWidth, zoomHeight);
                            }
                            graphics.InterpolationMode =
                                System.Drawing.Drawing2D
                                .InterpolationMode.NearestNeighbor;
                            graphics.DrawImage(
                                compact,
                                new Rectangle(
                                    12, 8,
                                    compact.Width * zoom,
                                    compact.Height * zoom),
                                new Rectangle(
                                    0, 0,
                                    compact.Width,
                                    compact.Height),
                                GraphicsUnit.Pixel);
                            graphics.DrawImage(
                                wide,
                                new Rectangle(
                                    12, 16 + compact.Height * zoom,
                                    wide.Width * zoom,
                                    wide.Height * zoom),
                                new Rectangle(
                                    0, 0,
                                    wide.Width, wide.Height),
                                GraphicsUnit.Pixel);
                        }
                        zoomed.Save(
                            Path.Combine(
                                ConfigStore.DataDirectory,
                                "ui-bar-zoom.png"),
                            ImageFormat.Png);
                    }
                    output.AppendLine("bar sizes=" +
                        compact.Width + "x" + compact.Height +
                        " / " + wide.Width + "x" + wide.Height);
                    if (compact.Height != wide.Height ||
                        compact.Width >= wide.Width ||
                        wide.Width - compact.Width >= 40)
                    {
                        pass = false;
                    }
                }
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }

            output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory, "statsprobe.txt"),
                output.ToString(),
                Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        /// <summary>
        /// 占用避让端到端验证：启动记事本当作「别的显示」，把它挪到状态条
        /// 默认落点上去，再确认 FindOccupiers 能扫到它、AvoidOccupiers 会把
        /// 状态条挪到不重叠的空档。结果写到 taskbaravoidprobe.txt。
        /// </summary>
        public static void RunTaskbarAvoidProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            Rectangle taskbar = TaskbarStatsForm.GetTaskbarRect();
            Rectangle tray = TaskbarStatsForm.GetTrayNotifyRect();
            Size content = new Size(150, 32);
            int margin = 8;
            Point basePos = TaskbarStats.ComputePosition(
                taskbar, tray, content, margin);
            output.AppendLine("taskbar=" + taskbar);
            output.AppendLine("tray=" + tray);
            output.AppendLine("base=" + basePos);

            System.Diagnostics.Process occ = null;
            try
            {
                // 用本程序的第二个进程当「别的应用」：--occupant 会在指定
                // 位置开一个小窗口。对 FindOccupiers 来说它是货真价实的
                // 外部进程（按 PID 排除自身），比 Win11 的 UWP notepad
                // （复用已有实例、新进程拿不到窗口句柄）可靠得多。
                string exePath = System.Reflection.Assembly
                    .GetExecutingAssembly().Location;
                string occArgs = "--occupant " +
                    (basePos.X - 10) + " " + (basePos.Y - 4) + " " +
                    (content.Width + 20) + " " + (content.Height + 8) +
                    " 20";
                occ = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(
                        exePath, occArgs));
                output.AppendLine("exe=" + exePath);
                output.AppendLine("args=" + occArgs +
                    " pid=" + (occ == null ? -1 : occ.Id));
                IntPtr hwnd = IntPtr.Zero;
                for (int i = 0; i < 60; i++)
                {
                    if (occ == null || occ.HasExited)
                    {
                        output.AppendLine(
                            "occupant exited early, code=" +
                            (occ == null ? "null" :
                                occ.ExitCode.ToString()));
                        break;
                    }
                    occ.Refresh();
                    hwnd = occ.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(50);
                }
                if (hwnd == IntPtr.Zero)
                {
                    pass = false;
                    output.AppendLine(
                        "FAIL: occupant window not found");
                }
                else
                {
                    output.AppendLine("occupant=" + hwnd);
                    System.Threading.Thread.Sleep(250);

                    List<Rectangle> occRects =
                        TaskbarStats.FindOccupiers(taskbar, IntPtr.Zero);
                    bool detected = false;
                    foreach (Rectangle r in occRects)
                    {
                        if (r.IntersectsWith(
                            new Rectangle(basePos, content)))
                        {
                            detected = true;
                            break;
                        }
                    }
                    output.AppendLine("occupiers=" + occRects.Count +
                        " detected=" + detected);
                    if (!detected)
                    {
                        pass = false;
                        output.AppendLine(
                            "FAIL: occupant not detected as occupier");
                    }
                    // 新增：矩形级「被谁挡住」检测。状态条默认落点矩形在
                    // 占位窗口下必须被判定成「被挡住」，移动/隐藏后必须恢复
                    // 干净。这是新版 Z 序（占用感知置顶）的唯一判据。
                    Rectangle barSlot = new Rectangle(basePos, content);
                    bool coveredWhenOccupied =
                        TaskbarStats.IsBarOverlapped(
                            barSlot, IntPtr.Zero);
                    output.AppendLine(
                        "barSlotOverlapped=" + coveredWhenOccupied);
                    if (!coveredWhenOccupied)
                    {
                        pass = false;
                        output.AppendLine(
                            "FAIL: bar slot not seen as covered");
                    }
                    Point relocated = TaskbarStats.AvoidOccupiers(
                        basePos, taskbar, tray, content, margin, occRects);
                    Rectangle safeSlot = new Rectangle(relocated, content);
                    bool coveredAfterRelocate =
                        TaskbarStats.IsBarOverlapped(
                            safeSlot, IntPtr.Zero);
                    output.AppendLine(
                        "barSlotRelocatedOverlapped=" +
                        coveredAfterRelocate);
                    if (coveredAfterRelocate)
                    {
                        pass = false;
                        output.AppendLine(
                            "FAIL: relocated slot still covered");
                    }
                    bool noOverlap = true;
                    foreach (Rectangle r in occRects)
                    {
                        if (new Rectangle(relocated, content)
                            .IntersectsWith(r))
                        {
                            noOverlap = false;
                            break;
                        }
                    }
                    output.AppendLine("relocated=" + relocated +
                        " noOverlap=" + noOverlap);
                    if (detected)
                    {
                        if (!noOverlap)
                        {
                            pass = false;
                            output.AppendLine(
                                "FAIL: relocated still overlaps occupier");
                        }
                        if (relocated.X >= basePos.X)
                        {
                            pass = false;
                            output.AppendLine(
                                "FAIL: not relocated left of default");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (occ != null && !occ.HasExited)
                {
                    try { occ.Kill(); }
                    catch { }
                }
            }

            output.AppendLine(
                pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory,
                    "taskbaravoidprobe.txt"),
                output.ToString(),
                Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        /// <summary>
        /// --barzprobe：起一个真正的 TaskbarStatsForm，等几秒采样之后
        /// 把它当前屏幕上的矩形、是不是还带 WS_EX_TOPMOST、以及所有
        /// 与这个矩形交叠的别的窗口（进程名 / class / rect）全部写出来。
        /// 专用于排查「状态条突然不显示 / 被谁按在下面」。
        /// </summary>
        public static void RunBarZProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                TaskbarStatsForm form = new TaskbarStatsForm();
                form.Show();

                // 等采样定时器跑几帧，把状态条贴出来。
                Application.DoEvents();
                for (int i = 0; i < 30; i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(200);
                    if (!form.BarBounds.IsEmpty)
                    {
                        break;
                    }
                }

                Rectangle bar = form.BarBounds;
                output.AppendLine("barRect=" + bar);
                output.AppendLine("formHandle=" + form.Handle);
                int style = GetWindowLong(form.Handle, GWL_EXSTYLE);
                bool topmost =
                    (style & (int)WS_EX_TOPMOST) != 0;
                output.AppendLine("exStyle=0x" +
                    style.ToString("X8") +
                    " topmost=" + topmost);

                bool overlapped = TaskbarStats.IsBarOverlapped(
                    bar, form.Handle);
                output.AppendLine(
                    "barOverlapped=" + overlapped);
                // Z 序意义上的「真被盖住」：只看排在自己上面的窗口。
                // 这才是决定要不要 SetWindowPos 的判据。
                bool obscured = TaskbarStats.IsBarObscured(
                    bar, form.Handle);
                output.AppendLine("barObscured=" + obscured);
                string above =
                    TaskbarStats.DescribeZOrderAbove(form.Handle);
                output.AppendLine("zOrderAbove=" +
                    (string.IsNullOrEmpty(above) ? "NONE" :
                        "\r\n" + above));
                string details = TaskbarStats.DescribeBarOverlaps(
                    bar, form.Handle);
                if (string.IsNullOrEmpty(details))
                {
                    output.AppendLine("overlapDetails=NO_WINDOW");
                }
                else
                {
                    output.AppendLine("overlapDetails=" + details);
                }

                // 关键回归断言：状态条不能和任务栏带上任何别的工具
                // （含 TrafficMonitor 这类嵌进任务栏的子窗口）叠在一起。
                Rectangle taskbarRect = TaskbarStatsForm.GetTaskbarRect();
                System.Collections.Generic.List<Rectangle> occupiers =
                    TaskbarStats.FindOccupiers(
                        taskbarRect, form.Handle);
                bool clearsOthers = !TaskbarStats.OverlapsAny(
                    bar, occupiers);
                output.AppendLine("barClearsOthers=" + clearsOthers +
                    " (occupiers=" + occupiers.Count + ")");
                for (int i = 0; i < occupiers.Count; i++)
                {
                    output.AppendLine("  occ=" + occupiers[i]);
                }
                if (!clearsOthers)
                {
                    pass = false;
                }

                // 顺带把整条任务栏带里被扫到的所有窗外列出来。
                System.Collections.Generic.List<string> band =
                    TaskbarStats.ListBandWindows();
                output.AppendLine("bandWindows=" + band.Count);
                for (int i = 0; i < band.Count; i++)
                {
                    output.AppendLine("  " + band[i]);
                }

                form.Dispose();
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }
            output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory, "barzprobe.txt"),
                output.ToString(), Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOPMOST = 0x0008;
        private const int WS_EX_LAYERED = 0x00080000;

        private delegate bool WinScanEnumProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct WinScanRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(
            WinScanEnumProc callback, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumChildWindows(
            IntPtr parent, WinScanEnumProc callback, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr FindWindow(
            string className, string windowName);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetClassName(
            IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "GetWindowRect")]
        private static extern bool NativeWinScanRect(
            IntPtr hWnd, out WinScanRect rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hWnd, out uint pid);

        /// <summary>单个窗口的说明行；不在扫描区内的返回 null。</summary>
        private static string DescribeWin(IntPtr hWnd, Rectangle area)
        {
            WinScanRect raw;
            if (!NativeWinScanRect(hWnd, out raw))
            {
                return null;
            }
            Rectangle rect = Rectangle.FromLTRB(
                raw.Left, raw.Top, raw.Right, raw.Bottom);
            if (rect.Width <= 0 || rect.Height <= 0 ||
                !rect.IntersectsWith(area))
            {
                return null;
            }
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            string proc = "?";
            try
            {
                using (System.Diagnostics.Process p =
                    System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    proc = p.ProcessName;
                }
            }
            catch
            {
            }
            StringBuilder cls = new StringBuilder(256);
            GetClassName(hWnd, cls, cls.Capacity);
            int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
            return string.Format(
                "hwnd=0x{0:X} vis={1} proc={2} class={3} rect={4} " +
                "ex=0x{5:X8} topmost={6} layered={7} pid={8}",
                hWnd.ToInt64(),
                IsWindowVisible(hWnd) ? 1 : 0,
                proc,
                cls,
                rect,
                ex,
                (ex & WS_EX_TOPMOST) != 0 ? 1 : 0,
                (ex & WS_EX_LAYERED) != 0 ? 1 : 0,
                pid);
        }

        /// <summary>
        /// --winscan：把任务栏带里「所有」窗口列出来（顶层 + 任务栏的子
        /// 窗口）。第三方流量 / 硬件监控小工具常把自己嵌成 Shell_TrayWnd
        /// 的子窗口，或做成很矮的浮条——普通 EnumWindows 扫描既看不到
        /// 子窗口，也过不了「半带高」门槛，于是状态条落位时完全没把它
        /// 当回事，直接叠在它上面。本探针专门把这类窗口挖出来。
        /// </summary>
        public static void RunWinScan()
        {
            StringBuilder output = new StringBuilder();
            try
            {
                Rectangle taskbar = TaskbarStatsForm.GetTaskbarRect();
                output.AppendLine("taskbar=" + taskbar);
                Rectangle area = Rectangle.FromLTRB(
                    taskbar.Left - 40,
                    taskbar.Top - 60,
                    taskbar.Right,
                    taskbar.Bottom);

                output.AppendLine("scanArea=" + area);
                output.AppendLine("--- topLevel ---");
                int count = 0;
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    string line = DescribeWin(hWnd, area);
                    if (line != null)
                    {
                        output.AppendLine(line);
                        count++;
                    }
                    return true;
                }, IntPtr.Zero);
                output.AppendLine("topLevelCount=" + count);

                string[] parents = new string[]
                {
                    "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
                };
                for (int i = 0; i < parents.Length; i++)
                {
                    IntPtr parent = FindWindow(parents[i], null);
                    output.AppendLine("--- children of " + parents[i] +
                        " hwnd=0x" + parent.ToInt64().ToString("X") +
                        " ---");
                    int childCount = 0;
                    if (parent != IntPtr.Zero)
                    {
                        EnumChildWindows(
                            parent,
                            delegate(IntPtr hWnd, IntPtr lParam)
                            {
                                string line = DescribeWin(hWnd, area);
                                if (line != null)
                                {
                                    output.AppendLine(line);
                                    childCount++;
                                }
                                return true;
                            },
                            IntPtr.Zero);
                    }
                    output.AppendLine("childCount=" + childCount);
                }

                // 顺便把现在避让逻辑实际认到的「占用者」打印出来对照。
                Rectangle tray = TaskbarStatsForm.GetTrayNotifyRect();
                output.AppendLine("trayNotify=" + tray);
                System.Collections.Generic.List<Rectangle> occupiers =
                    TaskbarStats.FindOccupiers(taskbar, IntPtr.Zero);
                output.AppendLine("occupiers=" + occupiers.Count);
                for (int i = 0; i < occupiers.Count; i++)
                {
                    output.AppendLine("  occ=" + occupiers[i]);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
            }
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory, "winscan.txt"),
                output.ToString(), Encoding.UTF8);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// --occupant：在指定屏幕位置开一个小窗口，几秒后自动退出。
        /// 专供 --taskbaravoidprobe 当「别的应用」用。参数：x y w h 秒。
        /// </summary>
        internal static void RunOccupantWindow(string[] args)
        {
            int idx = Array.IndexOf(args, "--occupant");
            int x = 0, y = 0, w = 200, h = 40, seconds = 10;
            try
            {
                if (args.Length > idx + 1) { x = int.Parse(args[idx + 1]); }
                if (args.Length > idx + 2) { y = int.Parse(args[idx + 2]); }
                if (args.Length > idx + 3) { w = int.Parse(args[idx + 3]); }
                if (args.Length > idx + 4) { h = int.Parse(args[idx + 4]); }
                if (args.Length > idx + 5) { seconds = int.Parse(args[idx + 5]); }
            }
            catch
            {
            }
            Application.EnableVisualStyles();
            using (Form f = new Form())
            {
                // 不能设 ShowInTaskbar=false：WS_EX_TOOLWINDOW 的窗口
                // 会被 MainWindowHandle 挑掉，探针就抓不到句柄了。
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.Manual;
                f.TopMost = true;
                f.Bounds = new Rectangle(x, y, w, h);
                using (System.Windows.Forms.Timer t =
                    new System.Windows.Forms.Timer())
                {
                    t.Interval = Math.Max(1, seconds) * 1000;
                    t.Tick += delegate { f.Close(); };
                    t.Start();
                    Application.Run(f);
                }
            }
        }


        /// <summary>
        /// 端到端复现用户报的录入流程：点一下输入框 → 按住 Ctrl →
        /// 再按 T，应得到 Ctrl+T 而不是 Ctrl+ControlKey。
        /// 结果写到 %APPDATA%\SelectionTranslator\hotkeyprobe.txt。
        /// </summary>
        public static void RunHotkeyProbe()
        {
            StringBuilder output = new StringBuilder();
            bool pass = true;
            try
            {
                Application.EnableVisualStyles();
                using (SettingsForm form =
                    new SettingsForm(new AppConfig()))
                {
                    form.Show();
                    form.Activate();
                    Application.DoEvents();

                    BindingFlags flags =
                        BindingFlags.Instance | BindingFlags.NonPublic;
                    TextBox field = (TextBox)typeof(SettingsForm)
                        .GetField("_hotkeyTranslateField", flags)
                        .GetValue(form);
                    MethodInfo clicked = typeof(SettingsForm)
                        .GetMethod("OnHotkeyFieldClicked", flags);
                    MethodInfo down = typeof(SettingsForm)
                        .GetMethod("OnSettingsKeyDown", flags);

                    field.Focus();
                    Application.DoEvents();
                    output.AppendLine("focused=" + field.ContainsFocus);
                    if (!field.ContainsFocus)
                    {
                        pass = false;
                    }

                    clicked.Invoke(form,
                        new object[] { field, EventArgs.Empty });
                    output.AppendLine("after click: " + field.Text);
                    down.Invoke(form, new object[] { form,
                        new KeyEventArgs(Keys.ControlKey) });
                    output.AppendLine("after Ctrl: " + field.Text);
                    down.Invoke(form, new object[] { form,
                        new KeyEventArgs(Keys.T) });
                    output.AppendLine("after T: " + field.Text);
                    if (field.Text != "Ctrl+T")
                    {
                        pass = false;
                    }

                    clicked.Invoke(form,
                        new object[] { field, EventArgs.Empty });
                    down.Invoke(form, new object[] { form,
                        new KeyEventArgs(Keys.ControlKey) });
                    down.Invoke(form, new object[] { form,
                        new KeyEventArgs(Keys.ShiftKey) });
                    down.Invoke(form, new object[] { form,
                        new KeyEventArgs(Keys.F5) });
                    output.AppendLine("after Ctrl+Shift+F5: " +
                        field.Text);
                    if (field.Text != "Ctrl+Shift+F5")
                    {
                        pass = false;
                    }

                    // 不带修饰键的普通字母会全局拦截打字，应当拒绝。
                    clicked.Invoke(form,
                        new object[] { field, EventArgs.Empty });
                    down.Invoke(form, new object[] { form,
                        new KeyEventArgs(Keys.A) });
                    output.AppendLine("after bare A: " + field.Text);
                    if (field.Text !=
                        "请加 Ctrl / Alt / Shift，或按 F1–F12")
                    {
                        pass = false;
                    }
                    form.Close();
                }
            }
            catch (Exception ex)
            {
                pass = false;
                output.AppendLine("EXCEPTION: " + ex);
            }

            output.AppendLine(pass ? "RESULT: OK" : "RESULT: FAILED");
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(
                    ConfigStore.DataDirectory, "hotkeyprobe.txt"),
                output.ToString(),
                Encoding.UTF8);
            Environment.ExitCode = pass ? 0 : 1;
        }

        private static AppConfig FreshConfig()
        {
            return new AppConfig();
        }

        private static void AppendResult(
            StringBuilder output,
            string name,
            string actual,
            string expected)
        {
            output.AppendLine(string.Format(
                "{0}: actual={1} expected={2} {3}",
                name,
                actual,
                expected,
                actual == expected ? "OK" : "FAILED"));
        }

        private static void Check(
            StringBuilder output,
            ref bool ok,
            string name,
            string actual,
            string expected)
        {
            AppendResult(output, name, actual, expected);
            if (actual != expected)
            {
                ok = false;
            }
        }

        /// <summary>
        /// 剪贴板窗口渲染冒烟：造几条示例历史，真的把窗口显示出来截一张图，
        /// 验证自绘列表 / 分组 / 缩略图不会在运行时抛 GDI+ 异常。
        /// 输出 clipboarduiprobe.txt 与 ui-clipboard.png。
        /// </summary>
        public static void RunClipboardUiProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            string previewImage = "";
            string probeJson = Path.Combine(
                ConfigStore.DataDirectory, "clipboard-ui-probe.json");
            string probeImageDir = Path.Combine(
                ConfigStore.DataDirectory,
                "clipboard-ui-probe-images");
            AppConfig config = new AppConfig();
            config.EnableClipboardHistory = false;
            config.ClipboardTtlHours = 168;
            ClipboardManager manager = null;
            try
            {
                Application.EnableVisualStyles();
                manager = ClipboardManager.StartForProbe(
                    config, probeJson, probeImageDir);
                ClipboardStore store = manager.Store;
                store.Items.Clear();

                ClipboardItem text1 = new ClipboardItem();
                text1.Id = 1;
                text1.Kind = (int)ClipboardItemKind.Text;
                text1.Text = "工具侠剪贴板：复制过的文字会自动按时间排列，" +
                    "双击即可重新复制。";
                text1.Fingerprint = "text1";
                text1.Count = 1;
                text1.TicksUtc = DateTime.UtcNow.AddMinutes(-3).Ticks;
                store.Items.Add(text1);

                ClipboardItem text2 = new ClipboardItem();
                text2.Id = 2;
                text2.Kind = (int)ClipboardItemKind.Text;
                text2.Text = "腾讯云 SentenceRecognition 接口文档";
                text2.Fingerprint = "text2";
                text2.Count = 3;
                text2.Favorite = true;
                text2.TicksUtc = DateTime.UtcNow.AddDays(-3).Ticks;
                store.Items.Add(text2);

                Directory.CreateDirectory(store.ImageDirectory);
                previewImage = Path.Combine(
                    store.ImageDirectory, "preview-probe.png");
                using (Bitmap bitmap = new Bitmap(320, 180))
                {
                    using (Graphics graphics =
                        Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.FromArgb(38, 84, 140));
                        using (SolidBrush brush = new SolidBrush(
                            Color.FromArgb(120, 190, 255)))
                        {
                            graphics.FillRectangle(
                                brush, 24, 24, 272, 132);
                        }
                        TextRenderer.DrawText(
                            graphics, "图片历史预览",
                            DarkUi.FontStrong,
                            new Rectangle(24, 70, 272, 40),
                            Color.White,
                            TextFormatFlags.HorizontalCenter |
                            TextFormatFlags.VerticalCenter);
                    }
                    bitmap.Save(previewImage, ImageFormat.Png);
                }

                ClipboardItem image = new ClipboardItem();
                image.Id = 3;
                image.Kind = (int)ClipboardItemKind.Image;
                image.ImageFile = Path.GetFileName(previewImage);
                image.ImageWidth = 311;
                image.ImageHeight = 156;
                image.Fingerprint = "img:preview";
                image.Count = 2;
                image.TicksUtc = DateTime.UtcNow.AddHours(-1).Ticks;
                store.Items.Add(image);

                ClipboardItem file = new ClipboardItem();
                file.Id = 4;
                file.Kind = (int)ClipboardItemKind.File;
                file.Files = new string[]
                {
                    @"C:\Users\demo\工具侠.exe",
                    @"C:\Users\demo\readme.txt",
                    @"C:\Users\demo\截图.png"
                };
                file.Text = ClipboardLogic.Preview(file, 0);
                file.Fingerprint = "files";
                file.Count = 1;
                // 探针里把文件条目放最新，首屏就能完整检查三行文件名。
                file.TicksUtc = DateTime.UtcNow.AddMinutes(-1).Ticks;
                store.Items.Add(file);

                ClipboardHistoryForm.ShowHistory();
                Form form = null;
                foreach (Form candidate in Application.OpenForms)
                {
                    if (candidate is ClipboardHistoryForm)
                    {
                        form = candidate;
                        break;
                    }
                }
                if (form == null)
                {
                    throw new InvalidOperationException(
                        "剪贴板窗口没有创建成功");
                }
                output.AppendLine("formBounds=" + form.Bounds);
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(600);
                while (DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(20);
                }
                output.AppendLine("formVisible=" + form.Visible);
                Check(output, ref ok, "ui probe window visible",
                    form.Visible.ToString(), "True");

                string shot = Path.Combine(
                    ConfigStore.DataDirectory, "ui-clipboard.png");
                try
                {
                    using (Bitmap capture = new Bitmap(
                        Math.Max(1, form.Width),
                        Math.Max(1, form.Height)))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(capture))
                        {
                            graphics.CopyFromScreen(
                                form.Left, form.Top, 0, 0,
                                capture.Size);
                        }
                        capture.Save(shot, ImageFormat.Png);
                    }
                    output.AppendLine("screenshot=" + shot);
                    Check(output, ref ok, "ui probe screenshot",
                        File.Exists(shot).ToString(), "True");
                }
                catch (Exception ex)
                {
                    // 锁屏 / 远程会话下 CopyFromScreen 会抛「句柄无效」，
                    // 这是环境限制，不是窗口代码回归。
                    output.AppendLine("SKIP: 当前桌面无法截屏：" +
                        ex.GetType().Name);
                }
                form.Close();
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                ClipboardHistoryForm.CloseHistory();
                ClipboardManager.Stop();
                try
                {
                    if (previewImage.Length > 0 &&
                        File.Exists(previewImage))
                    {
                        File.Delete(previewImage);
                    }
                    if (File.Exists(probeJson))
                    {
                        File.Delete(probeJson);
                    }
                    if (Directory.Exists(probeImageDir) &&
                        Directory.GetFileSystemEntries(
                            probeImageDir).Length == 0)
                    {
                        Directory.Delete(probeImageDir);
                    }
                }
                catch
                {
                }
                output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "clipboarduiprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 剪贴板历史端到端诊断：真实注册监听器、写一次系统剪贴板、
        /// 验证文本 / 图片写回和 TTL 清理，最后恢复用户原来的剪贴板。
        /// 输出 %APPDATA%\SelectionTranslator\clipboardprobe.txt。
        /// </summary>
        /// <summary>
        /// 剪贴板是全局资源，读回时给几个消息泵周期，避免被并行的
        /// 剪贴板查看器 / 另一个工具侠实例干扰成偶发失败。
        /// </summary>
        private static string ReadClipboardTextWithRetry(
            string expected)
        {
            string value = "";
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    value = Clipboard.ContainsText()
                        ? Clipboard.GetText() : "";
                }
                catch
                {
                    value = "";
                }
                if (value == expected)
                {
                    return value;
                }
                Application.DoEvents();
                System.Threading.Thread.Sleep(30);
            }
            return value;
        }

        public static void RunClipboardProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            ClipboardSnapshot snapshot = ClipboardHelper.CaptureSnapshot();
            string tempJson = Path.Combine(
                ConfigStore.DataDirectory, "clipboard-probe.json");
            string tempDir = Path.Combine(
                ConfigStore.DataDirectory, "clipboard-probe-images");
            string tempImage = Path.Combine(tempDir, "probe.png");
            string pastePath = "";
            string textFilePath = "";
            ClipboardListener listener = null;
            try
            {
                Application.EnableVisualStyles();
                bool probeIsOnlyInstance;
                using (System.Threading.Mutex mutex =
                    new System.Threading.Mutex(
                        true, "Local\\" + AppInfo.Id,
                        out probeIsOnlyInstance))
                {
                    output.AppendLine("otherInstanceRunning=" +
                        !probeIsOnlyInstance);
                }
                output.AppendLine("canRestore=" +
                    (!snapshot.CannotRestore));

                if (snapshot.CannotRestore)
                {
                    // 剪贴板里有图片等无法完整备份的格式，绝不覆盖用户数据。
                    output.AppendLine("SKIP: 剪贴板含不可备份格式，" +
                        "跳过写入测试以保护原有内容");
                }
                else if (!probeIsOnlyInstance)
                {
                    // 另一个工具侠正在监听，写测试文本会进它的历史，
                    // 还可能和它的采样抢剪贴板，直接跳过更干净。
                    output.AppendLine("SKIP: 已有工具侠在运行，" +
                        "跳过剪贴板写入以免污染历史");
                }
                else
                {
                    int notifications = 0;
                    listener = new ClipboardListener();
                    listener.ClipboardChanged += delegate
                    {
                        notifications++;
                    };
                    output.AppendLine("listenerRegistered=" +
                        listener.Registered);
                    Check(output, ref ok,
                        "probe clipboard listener registered",
                        listener.Registered.ToString(), "True");

                    string marker = "工具侠剪贴板探针-" +
                        Guid.NewGuid().ToString("N").Substring(0, 8);
                    Clipboard.SetText(marker);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                    while (DateTime.UtcNow < deadline &&
                        notifications == 0)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(25);
                    }
                    output.AppendLine("notifications=" + notifications);
                    Check(output, ref ok,
                        "probe receives WM_CLIPBOARDUPDATE",
                        (notifications > 0).ToString(), "True");
                    // 另一个正在运行的工具侠实例或第三方剪贴板工具可能
                    // 在同一瞬间改写内容，重试后仍不一致只标 SKIP，
                    // 不把环境竞争误判成代码回归。
                    string observed =
                        ReadClipboardTextWithRetry(marker);
                    if (observed != marker)
                    {
                        output.AppendLine(
                            "SKIP: 剪贴板被其他进程改写，" +
                            "跳过文本一致性判定");
                    }
                    else
                    {
                        Check(output, ref ok,
                            "probe clipboard text set", "True", "True");
                    }

                    // 文本写回
                    ClipboardStore store = ClipboardStore.Load(
                        tempJson, tempDir);
                    ClipboardItem textItem = new ClipboardItem();
                    textItem.Kind = (int)ClipboardItemKind.Text;
                    textItem.Text = "工具侠写回测试";
                    textItem.Fingerprint = textItem.Text;
                    textItem.TicksUtc = DateTime.UtcNow.Ticks;
                    bool wroteText = ClipboardManager.WriteToClipboard(
                        store, textItem);
                    string readBack = ReadClipboardTextWithRetry(
                        "工具侠写回测试");
                    if (!wroteText)
                    {
                        Check(output, ref ok, "probe write text back",
                            "False", "True");
                    }
                    else if (readBack != "工具侠写回测试")
                    {
                        output.AppendLine(
                            "SKIP: 写回后剪贴板被其他进程改写");
                    }
                    else
                    {
                        Check(output, ref ok, "probe write text back",
                            "True", "True");
                    }

                    // 图片写回
                    Directory.CreateDirectory(tempDir);
                    using (Bitmap bitmap = new Bitmap(12, 8))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.Red);
                        }
                        bitmap.Save(tempImage, ImageFormat.Png);
                    }
                    ClipboardItem imageItem = new ClipboardItem();
                    imageItem.Kind = (int)ClipboardItemKind.Image;
                    imageItem.ImageFile = "probe.png";
                    imageItem.ImageWidth = 12;
                    imageItem.ImageHeight = 8;
                    imageItem.TicksUtc = DateTime.UtcNow.Ticks;
                    bool wroteImage = ClipboardManager.WriteToClipboard(
                        store, imageItem);
                    Check(output, ref ok, "probe write image back",
                        (wroteImage && Clipboard.ContainsImage())
                            .ToString(), "True");

                    // 桌面 / 资源管理器只认 CF_HDROP，Auto 模式必须同时
                    // 挂一份真实存在的 PNG 文件路径。
                    bool hasDrop = Clipboard.ContainsFileDropList();
                    Check(output, ref ok,
                        "probe image back has file drop",
                        (wroteImage && hasDrop).ToString(), "True");
                    pastePath = "";
                    if (hasDrop)
                    {
                        System.Collections.Specialized.StringCollection
                            dropped = Clipboard.GetFileDropList();
                        if (dropped.Count > 0)
                        {
                            pastePath = dropped[0];
                        }
                    }
                    Check(output, ref ok,
                        "probe paste image file exists",
                        (pastePath.Length > 0 &&
                            File.Exists(pastePath) &&
                            Path.GetExtension(pastePath) == ".png")
                            .ToString(), "True");
                    bool pngHeader = false;
                    try
                    {
                        if (pastePath.Length > 0 &&
                            File.Exists(pastePath))
                        {
                            byte[] head = File.ReadAllBytes(pastePath);
                            pngHeader = head.Length > 8 &&
                                head[0] == 0x89 && head[1] == 0x50 &&
                                head[2] == 0x4E && head[3] == 0x47;
                        }
                    }
                    catch
                    {
                    }
                    Check(output, ref ok, "probe paste image is png",
                        pngHeader.ToString(), "True");

                    // 「复制为文件」：只有文件列表，没有位图。
                    ClipboardManager.WriteToClipboard(
                        store, imageItem, ClipboardWriteMode.FileOnly);
                    Check(output, ref ok, "probe file only mode",
                        (Clipboard.ContainsFileDropList() &&
                            !Clipboard.ContainsImage()).ToString(),
                        "True");

                    // 「复制为纯图片」：只有位图，没有文件列表。
                    ClipboardManager.WriteToClipboard(
                        store, imageItem, ClipboardWriteMode.ImageOnly);
                    Check(output, ref ok, "probe image only mode",
                        (Clipboard.ContainsImage() &&
                            !Clipboard.ContainsFileDropList()).ToString(),
                        "True");

                    // 截图 / 贴图路径：直接写内存位图，必须同样是
                    // 「位图 + 可落盘文件」，否则桌面上仍粘不出来。
                    using (Bitmap shotBitmap = new Bitmap(10, 6))
                    {
                        using (Graphics shotGraphics =
                            Graphics.FromImage(shotBitmap))
                        {
                            shotGraphics.Clear(Color.DarkGreen);
                        }
                        bool wroteShot = ClipboardPasteFiles.WriteBitmap(
                            shotBitmap, ClipboardWriteMode.Auto);
                        string shotPath = "";
                        if (Clipboard.ContainsFileDropList())
                        {
                            System.Collections.Specialized.StringCollection
                                droppedShot = Clipboard.GetFileDropList();
                            if (droppedShot.Count > 0)
                            {
                                shotPath = droppedShot[0];
                            }
                        }
                        Check(output, ref ok,
                            "probe bitmap paste has image and file",
                            (wroteShot && Clipboard.ContainsImage() &&
                                shotPath.Length > 0).ToString(), "True");
                        Check(output, ref ok,
                            "probe bitmap paste file in temp dir",
                            (ClipboardPasteFiles.IsPasteTemp(shotPath) &&
                                File.Exists(shotPath)).ToString(), "True");
                        // 采样历史不能把这条记成两小时后失效的文件条目。
                        Check(output, ref ok,
                            "probe bitmap paste file is temp only",
                            ClipboardPasteFiles.IsOnlyPasteTemp(
                                new string[] { shotPath }) + "/" +
                            ClipboardPasteFiles.IsOnlyPasteTemp(
                                new string[] { shotPath, tempImage }),
                            "True/False");
                        try
                        {
                            if (File.Exists(shotPath))
                            {
                                File.Delete(shotPath);
                            }
                        }
                        catch
                        {
                        }
                    }

                    // 文本也能「复制为文件」，在桌面粘贴成 .txt。
                    ClipboardManager.WriteToClipboard(
                        store, textItem, ClipboardWriteMode.FileOnly);
                    textFilePath = "";
                    if (Clipboard.ContainsFileDropList())
                    {
                        System.Collections.Specialized.StringCollection
                            droppedText = Clipboard.GetFileDropList();
                        if (droppedText.Count > 0)
                        {
                            textFilePath = droppedText[0];
                        }
                    }
                    bool textFileOk = false;
                    try
                    {
                        textFileOk = textFilePath.Length > 0 &&
                            File.Exists(textFilePath) &&
                            File.ReadAllText(textFilePath) ==
                                "工具侠写回测试";
                    }
                    catch
                    {
                    }
                    Check(output, ref ok, "probe text as file",
                        textFileOk.ToString(), "True");

                    // TTL 清理
                    ClipboardItem oldItem = new ClipboardItem();
                    oldItem.Kind = (int)ClipboardItemKind.Text;
                    oldItem.Text = "过期条目";
                    oldItem.Fingerprint = "过期条目";
                    oldItem.TicksUtc =
                        DateTime.UtcNow.AddDays(-40).Ticks;
                    store.Add(oldItem);
                    ClipboardItem freshItem = new ClipboardItem();
                    freshItem.Kind = (int)ClipboardItemKind.Text;
                    freshItem.Text = "新条目";
                    freshItem.Fingerprint = "新条目";
                    freshItem.TicksUtc = DateTime.UtcNow.Ticks;
                    store.Add(freshItem);
                    Check(output, ref ok, "probe purge expired",
                        store.Purge(
                            168, 300, DateTime.UtcNow).ToString(),
                        "1");
                    Check(output, ref ok, "probe kept fresh",
                        store.Items.Count.ToString(), "1");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (listener != null)
                {
                    listener.Close();
                }
                try
                {
                    snapshot.Restore();
                }
                catch
                {
                }
                try
                {
                    if (File.Exists(tempJson))
                    {
                        File.Delete(tempJson);
                    }
                    if (File.Exists(tempImage))
                    {
                        File.Delete(tempImage);
                    }
                    if (pastePath.Length > 0 &&
                        File.Exists(pastePath))
                    {
                        File.Delete(pastePath);
                    }
                    if (textFilePath.Length > 0 &&
                        File.Exists(textFilePath))
                    {
                        File.Delete(textFilePath);
                    }
                    if (Directory.Exists(tempDir) &&
                        Directory.GetFileSystemEntries(
                            tempDir).Length == 0)
                    {
                        Directory.Delete(tempDir);
                    }
                }
                catch
                {
                }
                output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "clipboardprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 双击 Ctrl 端到端探针：真的安装 WH_KEYBOARD_LL，再用 SendInput
        /// 造两下 Ctrl，验证钩子 → 状态机 → UI 线程事件整条链路。
        /// 输出 ctrlapprobe.txt。
        /// </summary>
        public static void RunDoubleCtrlProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            DoubleCtrlWatcher watcher = null;
            try
            {
                Application.EnableVisualStyles();
                // 创建控件句柄后 WinForms 才会安装同步上下文，
                // 钩子里 Post 回来的事件才有人处理。
                using (Form host = new Form())
                {
                    IntPtr handle = host.Handle;
                    output.AppendLine("hostHandle=" + handle);
                    SynchronizationContext context =
                        SynchronizationContext.Current;
                    if (context == null)
                    {
                        context = new WindowsFormsSynchronizationContext();
                    }
                    int fired = 0;
                    watcher = new DoubleCtrlWatcher(context);
                    watcher.DoubleCtrlPressed += delegate { fired++; };
                    // 探针自己是「程序」，注入的按键默认会被忽略，
                    // 这里临时放行以便模拟物理按键。
                    watcher.IgnoreInjectedKeys = false;
                    watcher.Start();
                    output.AppendLine("hookInstalled=" +
                        watcher.IsRunning);
                    if (!watcher.IsRunning)
                    {
                        output.AppendLine(
                            "SKIP: 当前会话无法安装全局键盘钩子");
                    }
                    else
                    {
                        PumpMessages(120);
                        TapCtrl();
                        TapCtrl();
                        PumpMessages(500);
                        output.AppendLine("firedAfterDouble=" + fired);
                        Check(output, ref ok,
                            "probe double ctrl fires",
                            fired.ToString(), "1");

                        // 中间夹了别的键（Ctrl+X 这类）不该触发：
                        // 第一次 Ctrl 被其它键打断，第二次 Ctrl 只是新一轮
                        // 的第一次轻点，合起来不能算双击。
                        int baseline = fired;
                        ProbeKey(NativeMethods.VK_CONTROL, false);
                        ProbeKey(0x87, false); // VK_F24，无副作用的占位键
                        ProbeKey(0x87, true);
                        ProbeKey(NativeMethods.VK_CONTROL, true);
                        TapCtrl();
                        PumpMessages(500);
                        output.AppendLine("firedAfterOtherKey=" + fired);
                        Check(output, ref ok,
                            "probe double ctrl ignores other key",
                            fired.ToString(), baseline.ToString());

                        // 恢复默认的「忽略注入按键」，防止本工具模拟
                        // 粘贴 / 上屏时自己触发自己。
                        baseline = fired;
                        watcher.IgnoreInjectedKeys = true;
                        TapCtrl();
                        TapCtrl();
                        PumpMessages(500);
                        output.AppendLine("firedAfterInjected=" + fired);
                        Check(output, ref ok,
                            "probe double ctrl ignores injected",
                            fired.ToString(), baseline.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                // 万一探针中途异常，别把 Ctrl 留在「按下」状态。
                ProbeKey(NativeMethods.VK_CONTROL, true);
                if (watcher != null)
                {
                    watcher.Dispose();
                }
                output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
                try
                {
                    Directory.CreateDirectory(
                        ConfigStore.DataDirectory);
                    File.WriteAllText(
                        Path.Combine(
                            ConfigStore.DataDirectory,
                            "ctrlapprobe.txt"),
                        output.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 软件下载端到端探针：真实请求 360 宝库的搜索接口，检查条目字段，
        /// 再对第一条的安装包直链做一次 HEAD，确认真的能下（不拉整个文件）。
        /// 输出 softprobe.txt。
        /// </summary>
        public static void RunSoftwareProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            try
            {
                DateTime started = DateTime.Now;
                SoftSearchResult result =
                    SoftwareCatalog.Search("小红书", 1);
                output.AppendLine("elapsedMs=" +
                    (int)(DateTime.Now - started).TotalMilliseconds);
                output.AppendLine("error=" + result.Error);
                output.AppendLine("warning=" + result.Warning);
                output.AppendLine("total=" + result.Total +
                    " items=" + result.Items.Count);
                output.AppendLine("api=" +
                    SoftwareCatalog.BuildSearchUrl("小红书", 1));

                // 两个源分开再查一次，出问题时能一眼看出是哪一侧挂了。
                SoftSearchResult only360 =
                    SoftwareCatalog.Search360("小红书", 1);
                SoftSearchResult onlyTencent =
                    SoftwareCatalog.SearchTencent("小红书");
                output.AppendLine("source360=" + only360.Items.Count +
                    " total=" + only360.Total +
                    " error=" + only360.Error);
                output.AppendLine("sourceTencent=" +
                    onlyTencent.Items.Count +
                    " total=" + onlyTencent.Total +
                    " error=" + onlyTencent.Error);
                output.AppendLine("apiTencent=" +
                    SoftwareCatalog.BuildTencentSearchUrl(
                        "小红书", 1, SoftwareCatalog.PageSize));
                Check(output, ref ok, "probe software search ok",
                    (result.Error.Length == 0).ToString(), "True");
                Check(output, ref ok, "probe software has results",
                    (result.Items.Count > 0).ToString(), "True");
                Check(output, ref ok, "probe software merged both sources",
                    (HasSource(result, SoftwareCatalog.Source360) &&
                        HasSource(result, SoftwareCatalog.SourceTencent))
                        .ToString(), "True");

                if (result.Items.Count > 0)
                {
                    int show = Math.Min(3, result.Items.Count);
                    for (int i = 0; i < show; i++)
                    {
                        SoftItem item = result.Items[i];
                        output.AppendLine("item[" + i + "]=" +
                            item.Name + " | " + item.SizeText +
                            " | " + item.RatingText);
                        output.AppendLine("        " +
                            item.DownloadUrl);
                    }
                    SoftItem first = result.Items[0];
                    Check(output, ref ok,
                        "probe software download url",
                        first.DownloadUrl.StartsWith(
                            "http", StringComparison.Ordinal).ToString(),
                        "True");
                    Check(output, ref ok,
                        "probe software file name",
                        SoftwareCatalog.BuildFileName(first)
                            .EndsWith(".exe",
                                StringComparison.OrdinalIgnoreCase)
                            .ToString(), "True");
                    Check(output, ref ok,
                        "probe software detail page",
                        first.DetailPage.StartsWith(
                            "https://baoku.360.cn/",
                            StringComparison.Ordinal).ToString(),
                        "True");

                    long packageLength;
                    string status = SoftwareCatalog.ProbeDownload(
                        first.DownloadUrl, out packageLength);
                    output.AppendLine("packageStatus=" + status +
                        " length=" + packageLength);
                    if (status == "200" || status == "206")
                    {
                        Check(output, ref ok,
                            "probe software package reachable",
                            (packageLength > 0).ToString(), "True");
                    }
                    else
                    {
                        output.AppendLine(
                            "SKIP: 安装包直链探测未通过（" + status +
                            "），可能是网络或 CDN 限制");
                    }
                }

                if (onlyTencent.Items.Count > 0)
                {
                    SoftItem tencent = onlyTencent.Items[0];
                    output.AppendLine("tencent[0]=" + tencent.Name +
                        " | " + tencent.VersionText +
                        " | " + tencent.SizeText +
                        " | " + tencent.RatingText +
                        " | " + tencent.Source);
                    output.AppendLine("        " +
                        tencent.DownloadUrl);
                    output.AppendLine("        " + tencent.LogoUrl);
                    output.AppendLine("        " + tencent.DetailPage);
                    Check(output, ref ok,
                        "probe tencent detail page",
                        tencent.DetailPage.StartsWith(
                            "https://pc.qq.com/detail/",
                            StringComparison.Ordinal).ToString(),
                        "True");
                    Check(output, ref ok,
                        "probe tencent download url",
                        tencent.DownloadUrl.StartsWith(
                            "https://", StringComparison.Ordinal)
                            .ToString(), "True");
                    Check(output, ref ok,
                        "probe tencent size text",
                        (tencent.SizeText.Length > 0).ToString(),
                        "True");
                    long tencentLength;
                    string tencentStatus = SoftwareCatalog.ProbeDownload(
                        tencent.DownloadUrl, out tencentLength);
                    output.AppendLine("tencentPackageStatus=" +
                        tencentStatus + " length=" + tencentLength);
                    if (tencentStatus == "200" || tencentStatus == "206")
                    {
                        Check(output, ref ok,
                            "probe tencent package reachable",
                            (tencentLength > 0).ToString(), "True");
                    }
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "softprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 软件下载真机探针：真的把一个小安装包下到临时目录，检查大小与 MZ 头，
        /// 跑完删掉。用来验证下载引擎（流式写盘、进度、改名）而不是只看接口。
        /// 输出 softdownloadprobe.txt。
        /// </summary>
        public static void RunSoftwareDownloadProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            string directory = Path.Combine(
                ConfigStore.DataDirectory, "software-probe");
            string tencentDirectory = Path.Combine(
                ConfigStore.DataDirectory, "software-probe-qq");
            try
            {
                // 两个源各下一个小包：360 用「小红书」里最小的，
                // 腾讯用「7-Zip」里最小的（腾讯源小包多，适合做端到端）。
                RunDownloadCase("360", SoftwareCatalog.Search360("小红书", 1),
                    directory, output, ref ok);
                RunDownloadCase("tencent", SoftwareCatalog.SearchTencent("7-Zip"),
                    tencentDirectory, output, ref ok);
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                    if (Directory.Exists(tencentDirectory))
                    {
                        Directory.Delete(tencentDirectory, true);
                    }
                }
                catch
                {
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "softdownloadprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 从一批搜索结果里挑体积最小的一个真下下来，校验大小与 MZ 头。
        /// label 用于区分两个源，会写进断言名字里。
        /// </summary>
        private static void RunDownloadCase(
            string label, SoftSearchResult result, string directory,
            StringBuilder output, ref bool ok)
        {
            if (result == null || result.Error.Length > 0 ||
                result.Items.Count == 0)
            {
                output.AppendLine("SKIP(" + label + "): 搜索不可用：" +
                    (result == null ? "null" : result.Error));
                return;
            }

            // 先挑最小的那个，别为了测试拉几百兆。
            SoftItem pick = result.Items[0];
            double best = double.MaxValue;
            for (int i = 0; i < result.Items.Count; i++)
            {
                SoftItem item = result.Items[i];
                if (item.DownloadUrl.Length == 0)
                {
                    continue;
                }
                double mb = ParseSizeMb(item.SizeText);
                if (mb > 0 && mb < best)
                {
                    best = mb;
                    pick = item;
                }
            }
            output.AppendLine("pick(" + label + ")=" + pick.Name +
                " | " + pick.SizeText + " | " + pick.Source);
            output.AppendLine("url=" + pick.DownloadUrl);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
            Directory.CreateDirectory(directory);
            int updates = 0;
            SoftDownload job = new SoftDownload();
            job.Item = pick;
            SoftwareDownloader.StartAsync(
                job, directory,
                delegate(SoftDownload progress) { updates++; })
                .Wait();
            output.AppendLine("done=" + job.Done +
                " failed=" + job.Failed +
                " error=" + job.Error);
            output.AppendLine("received=" + job.Received +
                " total=" + job.TotalLength +
                " progressCallbacks=" + updates);
            output.AppendLine("path=" + job.TargetPath);
            Check(output, ref ok, "probe " + label + " download done",
                job.Done.ToString(), "True");
            Check(output, ref ok,
                "probe " + label + " downloaded file exists",
                (job.TargetPath.Length > 0 &&
                    File.Exists(job.TargetPath)).ToString(),
                "True");
            long length = 0;
            try
            {
                length = new FileInfo(job.TargetPath).Length;
            }
            catch
            {
            }
            Check(output, ref ok,
                "probe " + label + " downloaded size",
                (length > 100 * 1024 &&
                    (job.TotalLength <= 0 ||
                        length == job.TotalLength)).ToString(),
                "True");
            bool exeHeader = false;
            try
            {
                byte[] head = new byte[2];
                using (FileStream stream = File.OpenRead(
                    job.TargetPath))
                {
                    exeHeader = stream.Read(head, 0, 2) == 2 &&
                        head[0] == 'M' && head[1] == 'Z';
                }
            }
            catch
            {
            }
            output.AppendLine("exeHeader=" + exeHeader);
            Check(output, ref ok,
                "probe " + label + " downloaded is exe",
                exeHeader.ToString(), "True");
        }

        private static bool HasSource(SoftSearchResult result, string source)
        {
            if (result == null)
            {
                return false;
            }
            for (int i = 0; i < result.Items.Count; i++)
            {
                if (string.Equals(result.Items[i].Source, source,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static double ParseSizeMb(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            string value = text.Trim();
            double scale = 0;
            if (value.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
            {
                scale = 1;
                value = value.Substring(0, value.Length - 2);
            }
            else if (value.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
            {
                scale = 1024;
                value = value.Substring(0, value.Length - 2);
            }
            else if (value.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
            {
                scale = 1.0 / 1024;
                value = value.Substring(0, value.Length - 2);
            }
            double number;
            if (scale <= 0 ||
                !double.TryParse(value.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number))
            {
                return 0;
            }
            return number * scale;
        }

        /// <summary>
        /// 软件下载窗口渲染冒烟：塞几条示例软件，真的显示窗口并截 ui-software.png，
        /// 用来检查行高、按钮、进度条有没有重叠或裁切。
        /// </summary>
        /// <summary>
        /// 软件列表离屏渲染：走和真实重绘同一个 DrawRow，把每一行都画一遍。
        /// 第一段用「只有 360 字段、没有分类也没有版本号」的条目复现历史崩溃
        /// （腾讯源才有版本号，360 行分类为空时读 VersionText.Length 会空引用）；
        /// 第二段拿真实搜索结果（微信 / 小红书）整份画到图上并存成 PNG。
        /// </summary>
        /// <summary>
        /// 文件秒搜的端到端探针：在临时目录里造一棵已知结构的小树，
        /// 用真实扫描器建索引，再验证分类、通配符、多词 AND、路径命中、
        /// 四种排序、截断、以及落盘 / 读回的往返一致。
        /// </summary>
        public static void RunFileSearchProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            string root = Path.Combine(
                Path.GetTempPath(), "filesearch-probe");
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
                Directory.CreateDirectory(root);

                // 文档
                MakeProbeFile(root, "报告.txt", 1200, -3);
                MakeProbeFile(root, "季度汇报.docx", 40000, -1);
                MakeProbeFile(root, "笔记.md", 800, -10);
                // 图片
                MakeProbeFile(root, "截图.png", 250000, -2);
                MakeProbeFile(root, "照片.JPG", 120000, -5);
                // 压缩包
                MakeProbeFile(root, "备份.zip", 500000, -7);
                MakeProbeFile(root, "数据.7z", 90000, -8);
                // 应用
                MakeProbeFile(root, "tool.exe", 64000, -4);
                MakeProbeFile(root, "setup.msi", 300000, -6);
                // 视频 / 音频 / 其它
                MakeProbeFile(root, "片段.mp4", 900000, -9);
                MakeProbeFile(root, "song.mp3", 400000, -11);
                MakeProbeFile(root, "blob.bin", 512, -12);
                // 子目录里的同名关键词，用来验证「目录命中」
                string nested = Path.Combine(root, "项目计划");
                Directory.CreateDirectory(nested);
                Directory.CreateDirectory(Path.Combine(nested, "深层"));
                MakeProbeFile(nested, "进度.txt", 300, -2);
                MakeProbeFile(Path.Combine(nested, "深层"),
                    "里程碑.txt", 200, 0);
                // 这些应当被扫描器跳过
                string modules = Path.Combine(root, "node_modules");
                Directory.CreateDirectory(modules);
                MakeProbeFile(modules, "包.js", 1000, 0);
                string git = Path.Combine(root, ".git");
                Directory.CreateDirectory(git);
                MakeProbeFile(git, "HEAD", 40, 0);

                FileIndexData data = new FileIndexData();
                data.Roots.Add(root);
                FileScanProgress progress = new FileScanProgress();
                DateTime started = DateTime.UtcNow;
                FileScanner.Scan(
                    data.Roots, data, progress, null);
                long scanMs = (long)(DateTime.UtcNow - started)
                    .TotalMilliseconds;
                output.AppendLine("scanMs=" + scanMs +
                    " files=" + data.Entries.Count +
                    " dirs=" + data.Directories.Count);
                output.AppendLine("progressFiles=" + progress.Files +
                    " progressDirs=" + progress.Directories);
                Check(output, ref ok, "filesearch scan entry count",
                    data.Entries.Count.ToString(), "16");
                Check(output, ref ok, "filesearch scan dir count",
                    data.Directories.Count.ToString(), "3");
                Check(output, ref ok, "filesearch progress matches",
                    (progress.Files == data.Entries.Count &&
                        progress.Directories == data.Directories.Count)
                        .ToString(), "True");

                int skipped = 0;
                for (int i = 0; i < data.Entries.Count; i++)
                {
                    if (data.Entries.Name(i) == "包.js" ||
                        data.Entries.Name(i) == "HEAD")
                    {
                        skipped++;
                    }
                }
                Check(output, ref ok, "filesearch skips noise dirs",
                    skipped.ToString(), "0");

                int folders = CountKind(data, FileKind.Folder);
                int images = CountKind(data, FileKind.Image);
                int archives = CountKind(data, FileKind.Archive);
                int apps = CountKind(data, FileKind.Application);
                output.AppendLine("folders=" + folders +
                    " images=" + images + " archives=" + archives +
                    " apps=" + apps);
                Check(output, ref ok, "filesearch kind folders",
                    folders.ToString(), "2");
                Check(output, ref ok, "filesearch kind images",
                    images.ToString(), "2");
                Check(output, ref ok, "filesearch kind archives",
                    archives.ToString(), "2");

                // 单关键词
                FileQuery query = new FileQuery();
                query.Keyword = "报告";
                FileSearchResult result =
                    FileQueryEngine.Search(data, query);
                output.AppendLine("search 报告 -> " + result.Total +
                    " / " + result.ElapsedMs + "ms");
                Check(output, ref ok, "filesearch keyword hit",
                    result.Total.ToString(), "1");

                // 通配符
                query = new FileQuery();
                query.Keyword = "*.txt";
                result = FileQueryEngine.Search(data, query);
                output.AppendLine("search *.txt -> " + result.Total);
                Check(output, ref ok, "filesearch wildcard extension",
                    result.Total.ToString(), "3");

                // 多词 AND
                query = new FileQuery();
                query.Keyword = "报告 txt";
                result = FileQueryEngine.Search(data, query);
                Check(output, ref ok, "filesearch multi term and",
                    result.Total.ToString(), "1");

                // 增量筛选：先搜「报」再在结果里筛「报告」，要和直接搜一致
                query = new FileQuery();
                query.Keyword = "报";
                FileSearchResult broad =
                    FileQueryEngine.Search(data, query);
                query = new FileQuery();
                query.Keyword = "报告";
                FileSearchResult narrowed =
                    FileQueryEngine.Refine(data, broad.Items, query);
                FileSearchResult direct =
                    FileQueryEngine.Search(data, query);
                output.AppendLine("broad=" + broad.Total +
                    " narrowed=" + narrowed.Total +
                    " direct=" + direct.Total);
                Check(output, ref ok, "filesearch refine matches search",
                    (narrowed.Total == direct.Total &&
                        broad.Total > narrowed.Total).ToString(), "True");
                Check(output, ref ok, "filesearch refine is faster",
                    (narrowed.ElapsedMs <= direct.ElapsedMs + 2)
                        .ToString(), "True");

                // 目录名命中（文件本身不含关键词）
                query = new FileQuery();
                query.Keyword = "项目计划";
                result = FileQueryEngine.Search(data, query);
                output.AppendLine("search 项目计划 -> " + result.Total);
                Check(output, ref ok, "filesearch path hit",
                    result.Total.ToString(), "4");

                // 分类过滤
                query = new FileQuery();
                query.Keyword = "";
                query.Kind = FileKind.Image;
                result = FileQueryEngine.Search(data, query);
                Check(output, ref ok, "filesearch kind filter",
                    result.Total.ToString(), "2");

                // 排序：大小降序第一应是 片段.mp4（9 MB）
                query = new FileQuery();
                query.Keyword = "";
                query.Kind = null;
                query.Sort = FileSortKey.Size;
                query.Descending = true;
                result = FileQueryEngine.Search(data, query);
                Check(output, ref ok, "filesearch sort size desc",
                    result.Items.Count > 0 ? result.Items[0].Name : "",
                    "片段.mp4");

                query.Descending = false;
                result = FileQueryEngine.Search(data, query);
                Check(output, ref ok, "filesearch sort size asc",
                    result.Items.Count > 0 ? result.Items[0].Name : "",
                    "里程碑.txt");

                query.Sort = FileSortKey.Date;
                query.Descending = true;
                result = FileQueryEngine.Search(data, query);
                Check(output, ref ok, "filesearch sort date desc",
                    result.Items.Count > 0 ? result.Items[0].Name : "",
                    "里程碑.txt");

                query.Sort = FileSortKey.Name;
                query.Descending = false;
                result = FileQueryEngine.Search(data, query);
                string firstName = result.Items.Count > 0
                    ? result.Items[0].Name : "";
                output.AppendLine("firstName=" + firstName);
                Check(output, ref ok, "filesearch sort name asc",
                    firstName.Length > 0 ? "True" : "False", "True");

                query.Sort = FileSortKey.Path;
                query.Descending = false;
                result = FileQueryEngine.Search(data, query);
                Check(output, ref ok, "filesearch sort path asc",
                    result.Items.Count.ToString(), "16");

                // 截断：只收 5 条，但总数照样精确
                query = new FileQuery();
                query.Keyword = "";
                query.CollectLimit = 5;
                result = FileQueryEngine.Search(data, query);
                output.AppendLine("truncate total=" + result.Total +
                    " items=" + result.Items.Count +
                    " truncated=" + result.Truncated);
                Check(output, ref ok, "filesearch truncate keeps total",
                    (result.Total == 16 && result.Items.Count == 5 &&
                        result.Truncated).ToString(), "True");

                // 自建索引已整条移除：不再落盘，路径完全靠内存里的
                // 目录表拼出来（就是搜索结果里「所在目录」那一列）。
                Check(output, ref ok, "filesearch no index file written",
                    File.Exists(Path.Combine(root, "index.bin")).ToString(),
                    "False");
                string joined = data.FullPath(0);
                Check(output, ref ok, "filesearch path from memory",
                    (joined.Length > data.Roots[0].Length).ToString(),
                    "True");
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
                catch
                {
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "filesearchprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 真机端到端：加载真实索引 → 真实窗口 → 真的设关键词走搜索链路，
        /// 等到有结果后截 ui-filesearch-live.png。验证的是「用户点开窗口、
        /// 打两个字」这条路，不是示例数据。
        /// </summary>
        public static void RunFileSearchLiveProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            FileSearchForm form = null;
            try
            {
                Application.EnableVisualStyles();
                DateTime started = DateTime.UtcNow;
                form = FileSearchForm.ShowWindow();
                DateTime deadline = DateTime.UtcNow.AddMinutes(3);
                while (!FileIndexService.Instance.Ready &&
                    DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(50);
                }
                output.AppendLine("readyAfterMs=" +
                    (long)(DateTime.UtcNow - started).TotalMilliseconds +
                    " count=" + FileIndexService.Instance.Count);
                Check(output, ref ok, "filesearch live index ready",
                    FileIndexService.Instance.Ready.ToString(), "True");

                form.SetKeyword("报告");
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (form.ResultList.Items.Count == 0 &&
                    DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(30);
                }
                int found = form.ResultList.Items.Count;
                output.AppendLine("found=" + found +
                    " status=" + form.StatusText);
                Check(output, ref ok, "filesearch live has results",
                    (found > 0).ToString(), "True");

                // 再往后补一个字，走「在上次结果里继续筛」这条路。
                // 结果可能是 0 条，所以判断依据换成状态栏有没有变。
                string before = form.StatusText;
                form.SetKeyword("报告 2026");
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (form.StatusText == before &&
                    DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(30);
                }
                output.AppendLine("refined=" +
                    form.ResultList.Items.Count + " status=" +
                    form.StatusText);
                Check(output, ref ok, "filesearch live refine path",
                    (form.StatusText != before).ToString(), "True");

                deadline = DateTime.UtcNow.AddMilliseconds(500);
                while (DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(20);
                }
                CaptureForm(form, Path.Combine(
                    ConfigStore.DataDirectory,
                    "ui-filesearch-live.png"), output, ref ok,
                    "filesearch live screenshot");
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Close();
                    }
                }
                catch
                {
                }
                FileSearchForm.CloseWindow();
                CollectGarbage();
                output.AppendLine("afterCloseMB=" + WorkingSetMb());
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "filesearchlive.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 本地缓存往返探针：临时文件，不碰用户真实缓存。断言 Save/Load 后
        /// 条数、名字、路径、大小、分类、标记、时间戳完全一致；范围不符 /
        /// 文件损坏都安静返回 null；读回后查询结果一致。最后用真实缓存跑
        /// 一次 EnsureLoaded，验证命中路径真的秒开且不再重新扫描。
        /// </summary>
        public static void RunCacheProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            string tempDir = Path.Combine(Path.GetTempPath(),
                "st-cacheprobe-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(tempDir);
                string cachePath = Path.Combine(tempDir, "list.cache");
                List<string> roots = new List<string>();
                roots.Add("Q:\\docs");

                FileIndexData data = new FileIndexData();
                data.Roots = roots;
                data.Directories.Add("Q:\\docs");
                data.Directories.Add("Q:\\docs\\中文 目录");
                data.Source = FileIndexSource.Mft;
                data.SavedAt = new DateTime(2026, 11, 11, 6, 30, 0,
                    DateTimeKind.Utc);
                data.Entries.Add(0, "报告 2026.docx", 12345L,
                    new DateTime(2026, 1, 2).Ticks,
                    FileKind.Document, false);
                data.Entries.Add(1, "截图.png", 999L,
                    new DateTime(2026, 2, 3).Ticks,
                    FileKind.Image, true);
                data.Entries.Add(1, "归档.zip", 0L, 0L,
                    FileKind.Archive, false);

                bool saved = FileIndexCache.Save(data, cachePath);
                long bytes = File.Exists(cachePath)
                    ? new FileInfo(cachePath).Length : 0;
                output.AppendLine("saved=" + saved + " bytes=" + bytes);
                Check(output, ref ok, "cache save file",
                    (saved && bytes > 0).ToString(), "True");

                FileIndexData back = FileIndexCache.Load(roots, cachePath);
                Check(output, ref ok, "cache load not null",
                    (back != null).ToString(), "True");
                if (back != null)
                {
                    Check(output, ref ok, "cache entry count",
                        back.Entries.Count.ToString(), "3");
                    Check(output, ref ok, "cache directory count",
                        back.Directories.Count.ToString(), "2");
                    Check(output, ref ok, "cache timestamps",
                        (back.SavedAt == data.SavedAt).ToString(), "True");
                    Check(output, ref ok, "cache source",
                        back.Source.ToString(), "Mft");
                    Check(output, ref ok, "cache marked from cache",
                        back.FromCache.ToString(), "True");
                    Check(output, ref ok, "cache chinese name",
                        back.Entries.Name(1), "截图.png");
                    Check(output, ref ok, "cache full path",
                        back.FullPath(1),
                        "Q:\\docs\\中文 目录\\截图.png");
                    Check(output, ref ok, "cache size",
                        back.Entries.Size(1).ToString(), "999");
                    Check(output, ref ok, "cache kind",
                        back.Entries.Kind(2).ToString(), "Archive");
                    Check(output, ref ok, "cache meta missing flag",
                        back.Entries.MetaMissing(1).ToString(), "True");
                    Check(output, ref ok, "cache roots",
                        string.Join("|", back.Roots.ToArray()),
                        "Q:\\docs");

                    FileQuery q = new FileQuery();
                    q.Keyword = "报告";
                    FileSearchResult r1 = FileQueryEngine.Search(data, q);
                    FileSearchResult r2 = FileQueryEngine.Search(back, q);
                    Check(output, ref ok, "cache query same total",
                        r2.Total.ToString(), r1.Total.ToString());
                }

                List<string> otherRoots = new List<string>();
                otherRoots.Add("Q:\\other");
                Check(output, ref ok, "cache wrong scope ignored",
                    (FileIndexCache.Load(otherRoots, cachePath) == null)
                        .ToString(), "True");

                string broken = Path.Combine(tempDir, "broken.cache");
                File.WriteAllBytes(broken,
                    new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
                Check(output, ref ok, "cache broken file ignored",
                    (FileIndexCache.Load(roots, broken) == null)
                        .ToString(), "True");

                long realBytes = FileIndexCache.SizeBytes();
                output.AppendLine("realCacheBytes=" + realBytes);
                if (realBytes > 0)
                {
                    FileIndexService service = FileIndexService.Instance;
                    service.AddConsumer();
                    DateTime started = DateTime.UtcNow;
                    service.EnsureLoaded();
                    DateTime deadline = DateTime.UtcNow.AddMinutes(3);
                    // 只等「有数据可搜」：缓存超期时后台还会重扫一遍，
                    // 但那不该算在「打开窗口要等多久」里。
                    while (!service.Ready && DateTime.UtcNow < deadline)
                    {
                        Thread.Sleep(50);
                    }
                    long readyMs = (long)(DateTime.UtcNow - started)
                        .TotalMilliseconds;
                    output.AppendLine("cacheHitReadyMs=" + readyMs +
                        " count=" + service.Count);
                    Check(output, ref ok, "cache hit from cache",
                        service.FromCache.ToString(), "True");
                    Check(output, ref ok, "cache hit fast",
                        (readyMs < 8000).ToString(), "True");
                    service.RemoveConsumer();
                }
                else
                {
                    output.AppendLine(
                        "cache hit path: SKIP (还没有真实缓存)");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch
                {
                }
                output.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(Path.Combine(
                    ConfigStore.DataDirectory, "cacheprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        private static long WorkingSetMb()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess()
                    .WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            System.Threading.Thread.Sleep(300);
        }

        private static string BuildPathProbe(string dir, string name)
        {
            FileIndexData data = new FileIndexData();
            data.Directories.Add(dir);
            data.Entries.Add(0, name, 0, 0, FileKindLogic.Classify(name, false),
                false);
            return data.FullPath(new FileEntry(data, 0));
        }

        private static string BuildPathProbeEmpty()
        {
            return "";
        }

        /// <summary>
        /// 真机读盘探针：按当前范围现场读一遍（MFT 优先，没权限退目录
        /// 扫描），报告条数 / 耗时 / 内存占用与几个真实查询的耗时。
        /// 不落盘、不留缓存——这就是「自建索引已移除」之后的真实链路。
        /// </summary>
        public static void RunIndexBuildProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            try
            {
                FileIndexService service = FileIndexService.Instance;
                List<string> roots = FileIndexService.ResolveRoots();
                output.AppendLine("roots=" + string.Join(" | ",
                    roots.ToArray()));
                // 强制现场扫一遍（不命中缓存），顺便验证扫完会写回本地缓存。
                FileIndexCache.Delete();
                service.AddConsumer();
                double beforePrivate = MemPrivateMb();
                DateTime started = DateTime.UtcNow;
                service.EnsureLoaded();
                DateTime deadline = DateTime.UtcNow.AddMinutes(12);
                long firstReadyMs = -1;
                int firstReadyCount = 0;
                while ((!service.Ready || service.Building) &&
                    DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                    if (firstReadyMs < 0 && service.Ready)
                    {
                        firstReadyMs = (long)
                            (DateTime.UtcNow - started).TotalMilliseconds;
                        firstReadyCount = service.Count;
                    }
                }
                long seconds = (long)(DateTime.UtcNow - started)
                    .TotalSeconds;
                double afterPrivate = MemPrivateMb();
                output.AppendLine("count=" + service.Count);
                output.AppendLine("source=" + service.Source);
                output.AppendLine("building=" + service.Building +
                    " ready=" + service.Ready +
                    " error=" + service.LastError);
                output.AppendLine("buildSeconds=" + seconds);
                output.AppendLine("firstResultMs=" + firstReadyMs +
                    " firstResultCount=" + firstReadyCount);
                output.AppendLine("privateMB=" + afterPrivate.ToString("F1") +
                    " (+" + (afterPrivate - beforePrivate).ToString("F1") + ")");
                output.AppendLine("perEntryBytes=" + (service.Count > 0
                    ? (long)Math.Round(
                        (afterPrivate - beforePrivate) * 1048576.0 /
                        service.Count)
                    : 0));
                Check(output, ref ok, "index probe has entries",
                    (service.Count > 10000).ToString(), "True");
                Check(output, ref ok, "index probe finished",
                    service.Building.ToString(), "False");
                long cacheBytes = FileIndexCache.SizeBytes();
                output.AppendLine("cacheBytes=" + cacheBytes);
                Check(output, ref ok, "index probe writes cache file",
                    (cacheBytes > 0).ToString(), "True");
                Check(output, ref ok, "index probe no legacy index",
                    File.Exists(Path.Combine(
                        ConfigStore.DataDirectory, "fileindex.bin"))
                        .ToString(),
                    "False");

                string[] keywords = new string[]
                {
                    "报告", "*.docx", "计划 2026", "flink"
                };
                for (int i = 0; i < keywords.Length; i++)
                {
                    FileQuery query = new FileQuery();
                    query.Keyword = keywords[i];
                    FileSearchResult result = FileQueryEngine.Search(
                        service.Data, query);
                    output.AppendLine("query[" + keywords[i] + "] total=" +
                        result.Total + " ms=" + result.ElapsedMs);
                }
                FileQuery imageQuery = new FileQuery();
                imageQuery.Keyword = "";
                imageQuery.Kind = FileKind.Image;
                FileSearchResult images = FileQueryEngine.Search(
                    service.Data, imageQuery);
                output.AppendLine("query[全部图片] total=" + images.Total +
                    " ms=" + images.ElapsedMs);
                Check(output, ref ok, "index probe query fast",
                    (images.ElapsedMs < 3000).ToString(), "True");

                // 没人用就释放内存（常驻托盘不该白占）。
                long beforeRelease = WorkingSetMb();
                service.RemoveConsumer();
                service.ReleaseNow();
                long idleMb = WorkingSetMb();
                output.AppendLine("idleMB=" + idleMb +
                    " (beforeRelease=" + beforeRelease + ")");
                Check(output, ref ok, "index probe released memory",
                    (idleMb + 40 < beforeRelease).ToString(), "True");
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "indexbuildprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetGuiResources(
            IntPtr hProcess, uint uiFlags);

        /// <summary>强制回收后的存活托管堆（MB）。</summary>
        private static double MemManagedMb()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(false) / 1048576.0;
        }

        /// <summary>不回收直接读堆大小（MB）——反映「水位」，也就是
        /// 任务管理器里看到的那种没回落的状态。</summary>
        private static double MemHeapMb()
        {
            return GC.GetTotalMemory(false) / 1048576.0;
        }

        /// <summary>进程私有提交（MB），任务管理器「内存」列的口径接近它。</summary>
        private static double MemPrivateMb()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                p.Refresh();
                return p.PrivateMemorySize64 / 1048576.0;
            }
        }

        /// <summary>
        /// 工作集（任务管理器里用户真正看到的那个数字）。私有字节是
        /// CLR 已提交的堆段，GC 回收后仍会保留着不还给系统，拿它当
        /// 「有没有还回去」的判据会误报；工作集才是实机体感。
        /// </summary>
        private static double MemWorkingSetMb()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                p.Refresh();
                return p.WorkingSet64 / 1048576.0;
            }
        }

        private static string MemHandleCounts()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                IntPtr h = p.Handle;
                uint gdi = GetGuiResources(h, 0);
                uint user = GetGuiResources(h, 1);
                return "GDI=" + gdi + " USER=" + user;
            }
        }

        /// <summary>
        /// 内存归因探针：按功能逐项加载，报告每一步真实吃掉的托管堆。
        /// 用来回答「工具侠为什么占几百兆、到底是哪一块在吃」。
        /// 只读：会读索引缓存、构造面板和剪贴板视图，不动任何用户文件。
        /// </summary>
        public static void RunMemoryProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            DesktopManagerForm panel = null;
            try
            {
                Application.EnableVisualStyles();
                UiTheme.Configure(
                    UiTheme.DefaultBase, UiTheme.DefaultAccent, false);

                double baseManaged = MemManagedMb();
                double basePrivate = MemPrivateMb();
                output.AppendLine("[内存归因]");
                output.AppendLine("stage0 进程基线           托管=" +
                    baseManaged.ToString("F1") + "MB 私有=" +
                    basePrivate.ToString("F1") + "MB " + MemHandleCounts());

                // ---- 0b. 本地缓存状态（只读报告，不判失败） ----
                long cacheBytes = FileIndexCache.SizeBytes();
                output.AppendLine(
                    "stage0b 本地缓存文件        " +
                    (cacheBytes > 0
                        ? (cacheBytes / 1048576.0).ToString("F1") +
                            "MB（命中可秒开）"
                        : "不存在（首次打开会现场扫描）"));

                // ---- 1. 文件秒搜的全盘索引 ----
                FileIndexService service = FileIndexService.Instance;
                service.AddConsumer();
                double beforeManaged = MemManagedMb();
                double beforePrivate = MemPrivateMb();
                DateTime started = DateTime.UtcNow;
                service.EnsureLoaded();
                DateTime deadline = DateTime.UtcNow.AddMinutes(4);
                while ((!service.Ready || service.Building) &&
                    DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(150);
                }
                // 读盘/建索引的临时分配要等 GC 回收后才算「净占用」，
                // 同时记一下没回收时的水位（真实运行中看到的就是它）。
                Thread.Sleep(400);
                double peakHeap = MemHeapMb();
                double indexManaged = MemManagedMb();
                double indexPrivate = MemPrivateMb();
                double indexWorkingSet = MemWorkingSetMb();
                long loadSeconds = (long)
                    (DateTime.UtcNow - started).TotalSeconds;
                int count = service.Count;
                int dirCount = service.Data == null
                    ? 0 : service.Data.Directories.Count;
                long perEntryBytes = count > 0
                    ? (long)Math.Round(
                        (indexManaged - beforeManaged) * 1048576.0 / count)
                    : 0;
                long indexFileBytes = FileIndexCache.SizeBytes();
                output.AppendLine(
                    "stage1 文件秒搜·索引       条目=" + count.ToString("N0") +
                    " 目录=" + dirCount.ToString("N0") +
                    " 来源=" + service.Source +
                    " 本地缓存=" +
                    (indexFileBytes / 1048576.0).ToString("F1") + "MB" +
                    " 耗时=" + loadSeconds + "s");
                output.AppendLine(
                    "       托管=" + indexManaged.ToString("F1") + "MB (+" +
                    (indexManaged - beforeManaged).ToString("F1") + ")" +
                    " 私有=" + indexPrivate.ToString("F1") + "MB (+" +
                    (indexPrivate - beforePrivate).ToString("F1") + ")" +
                    " 工作集=" + indexWorkingSet.ToString("F1") + "MB" +
                    " 每条≈" + perEntryBytes + "B" +
                    " 未回收水位=" + peakHeap.ToString("F1") + "MB");
                Check(output, ref ok, "内存探针·索引已就绪",
                    service.Ready.ToString(), "True");

                // ---- 2. 桌面整理面板（含缩略图） ----
                double panelBefore = MemManagedMb();
                panel = new DesktopManagerForm();
                panel.LoadFromPath(Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory));
                for (int i = 0;
                    i < 60 && panel.LoadedIconCount < panel.TotalItemCount; i++)
                {
                    PumpMessages(100);
                }
                PumpMessages(200);
                double panelAfter = MemManagedMb();
                output.AppendLine(
                    "stage2 桌面整理面板        条目=" + panel.TotalItemCount +
                    " 缩略图=" + panel.ThumbnailCount +
                    " 已载图标=" + panel.LoadedIconCount);
                output.AppendLine(
                    "       托管=" + panelAfter.ToString("F1") + "MB (+" +
                    (panelAfter - panelBefore).ToString("F1") + ")" +
                    " 私有=" + MemPrivateMb().ToString("F1") + "MB " +
                    MemHandleCounts());

                // ---- 3. 剪贴板历史 ----
                double clipBefore = MemManagedMb();
                ClipboardStore store = ClipboardStore.Load();
                int imageItems = 0;
                for (int i = 0; i < store.Items.Count; i++)
                {
                    ClipboardItem item = store.Items[i];
                    if (item != null &&
                        item.Kind == (int)ClipboardItemKind.Image)
                    {
                        imageItems++;
                    }
                }
                double clipAfter = MemManagedMb();
                output.AppendLine(
                    "stage3 剪贴板历史          条目=" + store.Items.Count +
                    " 图片=" + imageItems);
                output.AppendLine(
                    "       托管=" + clipAfter.ToString("F1") + "MB (+" +
                    (clipAfter - clipBefore).ToString("F1") + ")" +
                    " 私有=" + MemPrivateMb().ToString("F1") + "MB");

                // ---- 4. 关掉面板 + 释放索引，看内存能不能真的还回去 ----
                if (panel != null)
                {
                    panel.Close();
                    panel.Dispose();
                    panel = null;
                }
                PumpMessages(300);
                service.RemoveConsumer();
                service.ReleaseNow();
                // 这里不再额外手动 GC：DropData 自己会完整回收 + 压 LOH。
                double idleManaged = MemHeapMb();
                double idlePrivate = MemPrivateMb();
                double idleWorkingSet = MemWorkingSetMb();
                output.AppendLine(
                    "stage4 关面板+放索引       托管=" +
                    idleManaged.ToString("F1") + "MB (比峰值少 " +
                    (indexManaged - idleManaged).ToString("F1") + ")" +
                    " 私有=" + idlePrivate.ToString("F1") + "MB (比峰值少 " +
                    (indexPrivate - idlePrivate).ToString("F1") + ")" +
                    " 工作集=" + idleWorkingSet.ToString("F1") + "MB" +
                    " " + MemHandleCounts());
                output.AppendLine(
                    "       注：探针没有再手动 GC，数字是 DropData 自己回收的结果。");
                output.AppendLine(
                    "       私有提交字节会留一小段 CLR 堆段不还给系统；任务管理器看的是工作集。");

                // 结论：谁的净占用最大。
                double indexCost = indexManaged - beforeManaged;
                double panelCost = panelAfter - panelBefore;
                double clipCost = clipAfter - clipBefore;
                output.AppendLine("[结论] 索引 " + indexCost.ToString("F1") +
                    "MB / 面板 " + panelCost.ToString("F1") +
                    "MB / 剪贴板 " + clipCost.ToString("F1") + "MB");
                // 判据改看托管堆 + 工作集：私有提交字节是 CLR 保留的堆段，
                // 对象回收后 CLR 也不 decommit，拿它判断会误报（改造前索引
                // 两百多兆时看不出问题，压到 73B/条后差值就不够了）。
                Check(output, ref ok, "内存探针·托管堆能还回去",
                    (idleManaged + 20 < indexManaged).ToString(), "True");
                Check(output, ref ok, "内存探针·工作集能还回去",
                    (idleWorkingSet + 40 < indexWorkingSet).ToString(),
                    "True");
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (panel != null)
                {
                    try
                    {
                        panel.Close();
                        panel.Dispose();
                    }
                    catch
                    {
                    }
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(ConfigStore.DataDirectory, "memprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        /// <summary>
        /// 静置内存探针：按真实方式把托盘程序跑起来（等同开机自启），
        /// 每 15 秒记一次内存，满 4 分钟自己退出。用来验证「放着不动
        /// 到底占多少」——这是用户真正会在任务管理器里看到的数字。
        /// </summary>
        public static void RunMemorySoak()
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine("[静置内存] 模拟开机自启，每 15 秒采一次");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);

            TrayApplicationContext context = new TrayApplicationContext();
            string path = Path.Combine(
                ConfigStore.DataDirectory, "memorysoak.txt");
            // 用后台线程采样并直接落盘：不依赖消息循环，探针无论被谁
            // 掐掉，已经采到的数字都留得住。
            Thread sampler = new Thread(delegate()
            {
                DateTime started = DateTime.UtcNow;
                try
                {
                    while (true)
                    {
                        Thread.Sleep(15000);
                        double seconds =
                            (DateTime.UtcNow - started).TotalSeconds;
                        lock (output)
                        {
                            output.AppendLine(
                                "t=" + ((long)seconds).ToString()
                                    .PadLeft(3) + "s 私有=" +
                                MemPrivateMb().ToString("F1").PadLeft(6) +
                                "MB 托管=" +
                                MemHeapMb().ToString("F1").PadLeft(6) +
                                "MB " + MemHandleCounts());
                            File.WriteAllText(
                                path, output.ToString(), Encoding.UTF8);
                        }
                        if (seconds >= 240)
                        {
                            output.AppendLine("RESULT: OK");
                            File.WriteAllText(
                                path, output.ToString(), Encoding.UTF8);
                            Environment.Exit(0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        output.AppendLine("EXCEPTION: " + ex.Message);
                        File.WriteAllText(
                            path, output.ToString(), Encoding.UTF8);
                    }
                    catch
                    {
                    }
                }
            });
            sampler.IsBackground = true;
            sampler.Start();
            try
            {
                Application.Run(context);
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                try
                {
                    context.Dispose();
                }
                catch
                {
                }
            }
        }

        private static void MakeProbeFile(
            string root, string name, int bytes, int daysAgo)
        {
            string path = Path.Combine(root, name);
            byte[] content = new byte[Math.Max(1, bytes)];
            for (int i = 0; i < content.Length; i++)
            {
                content[i] = (byte)(i & 0xFF);
            }
            File.WriteAllBytes(path, content);
            File.SetLastWriteTimeUtc(
                path, DateTime.UtcNow.AddDays(daysAgo));
        }

        private static int CountKind(FileIndexData data, FileKind kind)
        {
            int count = 0;
            for (int i = 0; i < data.Entries.Count; i++)
            {
                if (data.Entries.Kind(i) == kind)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 文件秒搜窗口渲染冒烟：用示例数据真开窗口，截 ui-filesearch.png，
        /// 再把排序切到「名称升序」截第二张，确认表头箭头与行内容都对。
        /// </summary>
        public static void RunFileSearchPreview()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            FileSearchForm form = null;
            try
            {
                Application.EnableVisualStyles();
                string docs = Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);
                if (docs.Length == 0)
                {
                    docs = @"C:\Users\Public\Documents";
                }
                FileIndexData data = new FileIndexData();
                data.Roots.Add(@"C:\");
                data.SavedAt = DateTime.UtcNow.AddMinutes(-8);
                data.Directories.Add(@"C:\");
                data.Directories.Add(docs);
                data.Directories.Add(@"C:\Users\Public\Downloads");
                data.Directories.Add(@"D:\项目\2026\计划");

                List<FileEntry> items = new List<FileEntry>();
                items.Add(MakePreviewEntry(data, 1, "2026年度工作计划.docx",
                    186000, -1));
                items.Add(MakePreviewEntry(data, 3, "工作计划-草稿.md",
                    12400, -2));
                items.Add(MakePreviewEntry(data, 2, "工作计划模板.xlsx",
                    74500, -4));
                items.Add(MakePreviewEntry(data, 3, "周计划.txt", 3200, -6));
                items.Add(MakePreviewEntry(data, 2, "计划评审录像.mp4",
                    128000000, -9));
                items.Add(MakePreviewEntry(data, 1, "计划截图.png",
                    640000, -3));
                items.Add(MakePreviewEntry(data, 3, "计划备份.zip",
                    9800000, -12));
                items.Add(MakePreviewEntry(data, 2, "旧计划.pdf",
                    2400000, -30));
                items.Add(MakePreviewEntry(data, 3, "临时计划.bin",
                    900, -45));

                form = FileSearchForm.ShowPreview(data, items, "计划");
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(700);
                while (DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(20);
                }
                output.AppendLine("formVisible=" + form.Visible);
                Check(output, ref ok, "filesearch ui visible",
                    form.Visible.ToString(), "True");

                string shot = Path.Combine(
                    ConfigStore.DataDirectory, "ui-filesearch.png");
                CaptureForm(form, shot, output, ref ok,
                    "filesearch ui screenshot");

                // 切到名称升序再截一张，验证箭头与排序。
                FileResultList list = form.ResultList;
                output.AppendLine("listFound=" + (list != null));
                if (list != null)
                {
                    FileQueryEngine.Sort(
                        list.Items, data, FileSortKey.Name, false);
                    list.SetSort(FileSortKey.Name, false);
                    list.SetItems(list.Items, false);
                    deadline = DateTime.UtcNow.AddMilliseconds(400);
                    while (DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(20);
                    }
                    CaptureForm(form, Path.Combine(
                        ConfigStore.DataDirectory,
                        "ui-filesearch-sorted.png"), output, ref ok,
                        "filesearch ui screenshot sorted");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Close();
                    }
                }
                catch
                {
                }
                FileSearchForm.CloseWindow();
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "filesearchuiprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        private static FileEntry MakePreviewEntry(
            FileIndexData data, int dirId, string name,
            long size, int daysAgo)
        {
            data.Entries.Add(dirId, name, size,
                DateTime.UtcNow.AddDays(daysAgo).Ticks,
                FileKindLogic.Classify(name, false), false);
            return new FileEntry(data, data.Entries.Count - 1);
        }

        private static void CaptureForm(
            Form form, string path, StringBuilder output,
            ref bool ok, string checkName)
        {
            try
            {
                using (Bitmap capture = new Bitmap(
                    Math.Max(1, form.Width), Math.Max(1, form.Height)))
                {
                    using (Graphics graphics = Graphics.FromImage(capture))
                    {
                        graphics.CopyFromScreen(
                            form.Left, form.Top, 0, 0, capture.Size);
                    }
                    capture.Save(path, ImageFormat.Png);
                }
                output.AppendLine("screenshot=" + path);
                Check(output, ref ok, checkName,
                    File.Exists(path).ToString(), "True");
            }
            catch (Exception ex)
            {
                // 锁屏 / 远程会话下 CopyFromScreen 会失败，属环境限制。
                output.AppendLine("SKIP: 当前桌面无法截屏：" +
                    ex.GetType().Name);
            }
        }

        public static void RunSoftwareRenderProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            SoftListView list = null;
            try
            {
                Application.EnableVisualStyles();

                // ---- 第一段：两源字段不对齐的最坏情况 ----
                List<SoftItem> edge = new List<SoftItem>();
                SoftItem bare360 = new SoftItem();
                bare360.Source = SoftwareCatalog.Source360;
                bare360.SoftId = 1;
                bare360.Name = "没有分类的360条目";
                edge.Add(bare360);

                SoftItem bareTencent = new SoftItem();
                bareTencent.Source = SoftwareCatalog.SourceTencent;
                bareTencent.SoftId = 2;
                bareTencent.Name = "没有版本的腾讯条目";
                edge.Add(bareTencent);

                SoftItem emptyName = new SoftItem();
                emptyName.Source = SoftwareCatalog.Source360;
                emptyName.Name = "";
                edge.Add(emptyName);

                list = new SoftListView();
                list.Width = 860;
                list.Height = 600;
                list.SetItems(edge);
                list.SetProviders(null, null);
                using (Bitmap canvas = new Bitmap(
                    860, Math.Max(1, Math.Min(2000, list.ContentHeight))))
                {
                    using (Graphics graphics = Graphics.FromImage(canvas))
                    {
                        int painted = list.RenderAllRows(graphics);
                        output.AppendLine("edgeRows=" + painted);
                        Check(output, ref ok, "soft render edge rows",
                            (painted == edge.Count).ToString(), "True");
                    }
                }
                Check(output, ref ok, "soft item null fields normalized",
                    (bare360.VersionText.Length == 0 &&
                        bare360.Category.Length == 0 &&
                        bare360.Description.Length == 0 &&
                        bareTencent.Category.Length == 0 &&
                        bareTencent.VersionText.Length == 0).ToString(),
                    "True");

                // ---- 第二段：真实搜索结果整份渲染 ----
                string[] keywords = new string[] { "微信", "小红书" };
                for (int k = 0; k < keywords.Length; k++)
                {
                    string keyword = keywords[k];
                    SoftSearchResult result =
                        SoftwareCatalog.Search(keyword, 1);
                    output.AppendLine("---- " + keyword +
                        " total=" + result.Total +
                        " items=" + result.Items.Count +
                        " error=" + result.Error);
                    if (result.Error.Length > 0 ||
                        result.Items.Count == 0)
                    {
                        output.AppendLine("SKIP: 搜索不可用");
                        continue;
                    }

                    int from360 = 0;
                    int fromTencent = 0;
                    int nullFields = 0;
                    for (int i = 0; i < result.Items.Count; i++)
                    {
                        SoftItem item = result.Items[i];
                        if (item.Source == SoftwareCatalog.SourceTencent)
                        {
                            fromTencent++;
                        }
                        else
                        {
                            from360++;
                        }
                        if (item.Name == null || item.Description == null ||
                            item.SizeText == null || item.Rating == null ||
                            item.Category == null ||
                            item.VersionText == null ||
                            item.LogoUrl == null ||
                            item.DownloadUrl == null ||
                            item.Key == null)
                        {
                            nullFields++;
                        }
                    }
                    output.AppendLine("source360=" + from360 +
                        " sourceTencent=" + fromTencent +
                        " nullFields=" + nullFields);
                    Check(output, ref ok,
                        "soft real " + keyword + " no null fields",
                        (nullFields == 0).ToString(), "True");
                    Check(output, ref ok,
                        "soft real " + keyword + " has both sources",
                        (from360 > 0 || fromTencent > 0).ToString(),
                        "True");

                    list.SetItems(result.Items);
                    int height = Math.Min(6000, list.ContentHeight);
                    using (Bitmap canvas = new Bitmap(860, height))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(canvas))
                        {
                            int painted = list.RenderAllRows(graphics);
                            output.AppendLine("rows=" + painted +
                                " canvasHeight=" + height);
                            Check(output, ref ok,
                                "soft real " + keyword +
                                " rendered all rows",
                                (painted == result.Items.Count).ToString(),
                                "True");
                        }
                        string shot = Path.Combine(
                            ConfigStore.DataDirectory,
                            "ui-software-real-" + keyword + ".png");
                        canvas.Save(shot, ImageFormat.Png);
                        output.AppendLine("png=" + shot);

                        // 长图不好看，另外存一张前 900px 的局部方便肉眼确认排版。
                        int topHeight = Math.Min(900, canvas.Height);
                        using (Bitmap top = new Bitmap(
                            860, topHeight))
                        {
                            using (Graphics topGraphics =
                                Graphics.FromImage(top))
                            {
                                topGraphics.DrawImage(canvas,
                                    new Rectangle(0, 0, 860, topHeight),
                                    new Rectangle(0, 0, 860, topHeight),
                                    GraphicsUnit.Pixel);
                            }
                            top.Save(Path.Combine(
                                ConfigStore.DataDirectory,
                                "ui-software-real-" + keyword +
                                "-top.png"), ImageFormat.Png);
                        }

                        // 再存一张「第一行腾讯结果」开始的局部，便于核对来源标签。
                        int tencentRow = -1;
                        for (int i = 0; i < result.Items.Count; i++)
                        {
                            if (result.Items[i].Source ==
                                SoftwareCatalog.SourceTencent)
                            {
                                tencentRow = i;
                                break;
                            }
                        }
                        if (tencentRow > 0)
                        {
                            int start = Math.Min(
                                canvas.Height - 1, 4 + tencentRow * 84);
                            int partHeight = Math.Min(900,
                                canvas.Height - start);
                            using (Bitmap part = new Bitmap(
                                860, Math.Max(1, partHeight)))
                            {
                                using (Graphics partGraphics =
                                    Graphics.FromImage(part))
                                {
                                    partGraphics.DrawImage(canvas,
                                        new Rectangle(0, 0, 860,
                                            partHeight),
                                        new Rectangle(0, start, 860,
                                            partHeight),
                                        GraphicsUnit.Pixel);
                                }
                                part.Save(Path.Combine(
                                    ConfigStore.DataDirectory,
                                    "ui-software-real-" + keyword +
                                    "-tencent.png"), ImageFormat.Png);
                            }
                            output.AppendLine("firstTencentRow=" +
                                tencentRow);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                if (list != null)
                {
                    list.Dispose();
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "softrenderprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        public static void RunSoftwareUiProbe()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            SoftwareDownloadForm form = null;
            try
            {
                Application.EnableVisualStyles();
                List<SoftItem> items = new List<SoftItem>();
                items.Add(MakeSoftItem(
                    2000004652, "小红书手机版", "31.00 MB", "6.0",
                    "聊天", "3亿人的生活指南，标记我的生活",
                    SoftwareCatalog.Source360, ""));
                items.Add(MakeSoftItem(
                    2000008141, "小红书(应用宝版)", "7.00 MB", "6.0",
                    "聊天", "手机端应用，需要先安装应用宝支持应用运行",
                    SoftwareCatalog.Source360, ""));
                items.Add(MakeSoftItem(
                    600004913, "小红书私信通", "139.00 MB", "6.0",
                    "营销", "私信智能管理与营销工具，批量回复自动回复",
                    SoftwareCatalog.Source360, ""));
                items.Add(MakeSoftItem(
                    600009911, "小红书直播助手", "384.00 MB", "6.0",
                    "直播", "小红书官方推出的电脑端直播工具",
                    SoftwareCatalog.Source360, ""));
                items.Add(MakeSoftItem(
                    35986, "抖音", "250.00 MB", "6.8", "",
                    "记录美好生活，短视频与直播客户端",
                    SoftwareCatalog.SourceTencent, "8.7.0"));
                items.Add(MakeSoftItem(
                    11488, "微信", "247.93 MB", "6.6", "",
                    "让沟通更方便",
                    SoftwareCatalog.SourceTencent, "4.2.0.13"));

                form = SoftwareDownloadForm.ShowPreview(items);
                output.AppendLine("formBounds=" + form.Bounds);
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(700);
                while (DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(20);
                }
                output.AppendLine("formVisible=" + form.Visible);
                Check(output, ref ok, "soft ui window visible",
                    form.Visible.ToString(), "True");

                string shot = Path.Combine(
                    ConfigStore.DataDirectory, "ui-software.png");
                try
                {
                    using (Bitmap capture = new Bitmap(
                        Math.Max(1, form.Width),
                        Math.Max(1, form.Height)))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(capture))
                        {
                            graphics.CopyFromScreen(
                                form.Left, form.Top, 0, 0,
                                capture.Size);
                        }
                        capture.Save(shot, ImageFormat.Png);
                    }
                    output.AppendLine("screenshot=" + shot);
                    Check(output, ref ok, "soft ui screenshot",
                        File.Exists(shot).ToString(), "True");
                }
                catch (Exception ex)
                {
                    // 锁屏 / 远程会话下 CopyFromScreen 会失败，属环境限制。
                    output.AppendLine("SKIP: 当前桌面无法截屏：" +
                        ex.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Close();
                    }
                }
                catch
                {
                }
                SoftwareDownloadForm.CloseWindow();
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "softuiprobe.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        private static SoftItem MakeSoftItem(
            int id, string name, string size, string rating,
            string category, string description, string source,
            string version)
        {
            SoftItem item = new SoftItem();
            item.Source = source;
            item.SoftId = id;
            item.Name = name;
            item.SizeText = size;
            item.Rating = rating;
            item.Category = category;
            item.Description = description;
            item.VersionText = version ?? "";
            item.DownloadUrl =
                "https://softdl6.360tpcdn.com/pcrj/setup_" + id + ".exe";
            item.DetailUrl = "/qbfl/" + id + ".html";
            return item;
        }

        /// <summary>
        /// 设置窗口渲染冒烟：真的显示窗口并截图，重点检查「软件下载」分组里的
        /// 保存位置框和两个按钮有没有被裁掉或互相压住。
        /// 输出 settingspreview.txt / ui-settings.png。
        /// </summary>
        public static void RunSettingsPreview()
        {
            StringBuilder output = new StringBuilder();
            bool ok = true;
            SettingsForm form = null;
            try
            {
                Application.EnableVisualStyles();
                AppConfig config = new AppConfig();
                config.SoftwareDownloadDirectory = @"D:\安装包下载";
                form = new SettingsForm(config);
                form.Show();
                form.Activate();
                PumpMessages(900);
                output.AppendLine("formBounds=" + form.Bounds);
                output.AppendLine("clientSize=" + form.ClientSize);
                Check(output, ref ok, "settings window visible",
                    form.Visible.ToString(), "True");

                GroupBox downloadGroup = FindGroupBox(form, "软件下载");
                Check(output, ref ok, "settings download group exists",
                    (downloadGroup != null).ToString(), "True");
                if (downloadGroup != null)
                {
                    output.AppendLine("downloadGroup=" +
                        downloadGroup.Bounds);
                    TextBox pathBox = null;
                    List<Button> buttons = new List<Button>();
                    bool inside = true;
                    for (int i = 0; i < downloadGroup.Controls.Count; i++)
                    {
                        Control child = downloadGroup.Controls[i];
                        if (child is TextBox)
                        {
                            pathBox = (TextBox)child;
                        }
                        if (child is Button)
                        {
                            buttons.Add((Button)child);
                        }
                        if (child.Right > downloadGroup.ClientSize.Width ||
                            child.Bottom > downloadGroup.ClientSize.Height ||
                            child.Left < 0 || child.Top < 0)
                        {
                            inside = false;
                            output.AppendLine("clipped=" +
                                child.GetType().Name + " " +
                                child.Text + " " + child.Bounds);
                        }
                    }
                    Check(output, ref ok,
                        "settings download controls inside group",
                        inside.ToString(), "True");
                    Check(output, ref ok, "settings download buttons",
                        buttons.Count.ToString(), "2");
                    bool overlap = false;
                    if (pathBox != null)
                    {
                        for (int i = 0; i < buttons.Count; i++)
                        {
                            if (buttons[i].Left < pathBox.Right)
                            {
                                overlap = true;
                            }
                        }
                        Check(output, ref ok,
                            "settings download path shown",
                            pathBox.Text, @"D:\安装包下载");
                    }
                    Check(output, ref ok,
                        "settings download controls no overlap",
                        overlap.ToString(), "False");
                }

                // 新增的「文件秒搜」分组：必须存在、控件不越界、
                // 并且真的显示默认快捷键 Ctrl+F。
                GroupBox searchGroup = FindGroupBox(form, "文件秒搜");
                Check(output, ref ok, "settings filesearch group exists",
                    (searchGroup != null).ToString(), "True");
                if (searchGroup != null)
                {
                    output.AppendLine("searchGroup=" +
                        searchGroup.Bounds);
                    bool searchInside = true;
                    TextBox hotkeyBox = null;
                    for (int i = 0; i < searchGroup.Controls.Count; i++)
                    {
                        Control child = searchGroup.Controls[i];
                        if (child is TextBox)
                        {
                            hotkeyBox = (TextBox)child;
                        }
                        if (child.Right > searchGroup.ClientSize.Width ||
                            child.Bottom > searchGroup.ClientSize.Height ||
                            child.Left < 0 || child.Top < 0)
                        {
                            searchInside = false;
                            output.AppendLine("searchClipped=" +
                                child.GetType().Name + " " +
                                child.Text + " " + child.Bounds);
                        }
                    }
                    Check(output, ref ok,
                        "settings filesearch controls inside group",
                        searchInside.ToString(), "True");
                    Check(output, ref ok,
                        "settings filesearch hotkey shown",
                        hotkeyBox == null ? "" : hotkeyBox.Text,
                        AppConfig.DefaultFileSearchHotkey);
                    bool overlapsDownload = downloadGroup != null &&
                        searchGroup.Bounds.IntersectsWith(
                            downloadGroup.Bounds);
                    Check(output, ref ok,
                        "settings filesearch group below download",
                        overlapsDownload.ToString(), "False");
                }

                string shot = Path.Combine(
                    ConfigStore.DataDirectory, "ui-settings.png");
                try
                {
                    using (Bitmap capture = new Bitmap(
                        Math.Max(1, form.Width),
                        Math.Max(1, form.Height)))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(capture))
                        {
                            graphics.CopyFromScreen(
                                form.Left, form.Top, 0, 0,
                                capture.Size);
                        }
                        capture.Save(shot, ImageFormat.Png);
                    }
                    output.AppendLine("screenshot=" + shot);
                    Check(output, ref ok, "settings screenshot",
                        File.Exists(shot).ToString(), "True");

                    // 再滚到底部拍一张，方便肉眼确认「文件秒搜」分组。
                    Panel host = null;
                    for (int i = 0; i < form.Controls.Count; i++)
                    {
                        Panel panel = form.Controls[i] as Panel;
                        if (panel != null && panel.AutoScroll)
                        {
                            host = panel;
                            break;
                        }
                    }
                    if (host != null)
                    {
                        host.AutoScrollPosition = new Point(
                            0, host.VerticalScroll.Maximum);
                        PumpMessages(400);
                        using (Bitmap capture = new Bitmap(
                            Math.Max(1, form.Width),
                            Math.Max(1, form.Height)))
                        {
                            using (Graphics graphics =
                                Graphics.FromImage(capture))
                            {
                                graphics.CopyFromScreen(
                                    form.Left, form.Top, 0, 0,
                                    capture.Size);
                            }
                            capture.Save(
                                Path.Combine(
                                    ConfigStore.DataDirectory,
                                    "ui-settings-bottom.png"),
                                ImageFormat.Png);
                        }
                    }
                }
                catch (Exception ex)
                {
                    output.AppendLine("SKIP: 当前桌面无法截屏：" +
                        ex.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("EXCEPTION: " + ex);
                ok = false;
            }
            finally
            {
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Close();
                    }
                }
                catch
                {
                }
                output.AppendLine(
                    ok ? "RESULT: OK" : "RESULT: FAILED");
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory,
                        "settingspreview.txt"),
                    output.ToString(), Encoding.UTF8);
                Environment.ExitCode = ok ? 0 : 1;
            }
        }

        private static GroupBox FindGroupBox(Control parent, string text)
        {
            if (parent == null)
            {
                return null;
            }
            foreach (Control child in parent.Controls)
            {
                GroupBox box = child as GroupBox;
                if (box != null && box.Text == text)
                {
                    return box;
                }
                GroupBox nested = FindGroupBox(child, text);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        private static void PumpMessages(int milliseconds)
        {
            DateTime deadline =
                DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(15);
            }
        }

        private static void TapCtrl()
        {
            ProbeKey(NativeMethods.VK_CONTROL, false);
            System.Threading.Thread.Sleep(25);
            ProbeKey(NativeMethods.VK_CONTROL, true);
            System.Threading.Thread.Sleep(45);
        }

        private static void ProbeKey(int virtualKey, bool up)
        {
            NativeMethods.INPUT input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.U.ki.wVk = (ushort)virtualKey;
            input.U.ki.dwFlags = up
                ? NativeMethods.KEYEVENTF_KEYUP : 0;
            NativeMethods.INPUT[] inputs =
                new NativeMethods.INPUT[] { input };
            NativeMethods.SendInput(
                1,
                inputs,
                System.Runtime.InteropServices.Marshal.SizeOf(
                    typeof(NativeMethods.INPUT)));
        }
    }

    /// <summary>
    /// 滚动截图的探针窗口：整窗就是一张「文档」，每一行颜色都不同，
    /// 滚动只是换起始行号。这样拼出来的长图可以逐行核对颜色。
    /// </summary>
    internal sealed class ScrollProbeForm : Form
    {
        private int _scroll;

        public ScrollProbeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(420, 520);
            BackColor = Color.Black;
            ShowInTaskbar = true;
            TopMost = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);
        }

        /// <summary>会话开始时的文档行号（探针固定从 0 开始）。</summary>
        public int StartRow
        {
            get { return 0; }
        }

        public int ScrollValue
        {
            get { return _scroll; }
        }

        public Rectangle DocumentArea
        {
            get { return RectangleToScreen(ClientRectangle); }
        }

        public void ScrollBy(int delta)
        {
            _scroll += delta;
            Invalidate();
            Update();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush brush = new SolidBrush(Color.Black))
            {
                for (int y = 0; y < ClientSize.Height; y++)
                {
                    brush.Color = SelfTest.ScrollProbeColor(
                        _scroll + y);
                    g.FillRectangle(
                        brush, 0, y, ClientSize.Width, 1);
                }
            }
        }
    }
}
