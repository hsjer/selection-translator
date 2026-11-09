using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal static class SelfTest
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

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
                        Check(output, ref ok, "capture export not null",
                            (exported != null).ToString(), "True");
                        if (exported != null)
                        {
                            Check(output, ref ok, "capture export width",
                                exported.Width.ToString(), "120");
                            Check(output, ref ok, "capture export height",
                                exported.Height.ToString(), "80");
                        }
                    }
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
                        for (int i = 0; i < 10; i++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(50);
                        }
                        bool opened = form.IsDetailVisible;
                        output.AppendLine("detailBounds=" +
                            form.DetailBounds);
                        output.AppendLine("after hover in, visible=" +
                            opened);
                        if (!opened)
                        {
                            pass = false;
                        }

                        SetCursorPos(20, 20);
                        for (int i = 0; i < 10; i++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(50);
                        }
                        bool closed = !form.IsDetailVisible;
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
                    int width = Math.Min(560, taskbar.Width);
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
                    "6.60 KB/s", "5.04 KB/s", 28, 62, 1f,
                    Color.FromArgb(255, 26, 28, 32)))
                using (Bitmap dark = TaskbarStatsForm.RenderBitmap(
                    "8.57 KB/s", "2.05 KB/s", 17, 62, 1f,
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
    }
}
