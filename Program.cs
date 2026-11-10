using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (Array.IndexOf(args, "--selftest") >= 0)
            {
                SelfTest.Run();
                return;
            }

            if (Array.IndexOf(args, "--hotkeyprobe") >= 0)
            {
                SelfTest.RunHotkeyProbe();
                return;
            }

            if (Array.IndexOf(args, "--statsprobe") >= 0)
            {
                SelfTest.RunStatsProbe();
                return;
            }

            if (Array.IndexOf(args, "--statslive") >= 0)
            {
                SelfTest.RunStatsLiveProbe();
                return;
            }

            if (Array.IndexOf(args, "--statsdetail") >= 0)
            {
                SelfTest.RunStatsDetailProbe();
                return;
            }

            if (Array.IndexOf(args, "--clipboardprobe") >= 0)
            {
                SelfTest.RunClipboardProbe();
                return;
            }

            if (Array.IndexOf(args, "--clipboarduipreview") >= 0)
            {
                SelfTest.RunClipboardUiProbe();
                return;
            }

            if (Array.IndexOf(args, "--ctrlapprobe") >= 0)
            {
                SelfTest.RunDoubleCtrlProbe();
                return;
            }

            if (Array.IndexOf(args, "--softprobe") >= 0)
            {
                SelfTest.RunSoftwareProbe();
                return;
            }

            if (Array.IndexOf(args, "--filesearchlive") >= 0)
            {
                SelfTest.RunFileSearchLiveProbe();
                return;
            }

            if (Array.IndexOf(args, "--filesearchprobe") >= 0)
            {
                SelfTest.RunFileSearchProbe();
                return;
            }

            if (Array.IndexOf(args, "--indexbuildprobe") >= 0)
            {
                SelfTest.RunIndexBuildProbe();
                return;
            }

            if (Array.IndexOf(args, "--filesearchpreview") >= 0)
            {
                SelfTest.RunFileSearchPreview();
                return;
            }

            if (Array.IndexOf(args, "--scrollshotprobe") >= 0)
            {
                SelfTest.RunScrollShotProbe();
                return;
            }

            if (Array.IndexOf(args, "--scrollbarprobe") >= 0)
            {
                SelfTest.RunScrollBarProbe();
                return;
            }

            if (Array.IndexOf(args, "--scrollflowprobe") >= 0)
            {
                SelfTest.RunScrollFlowProbe();
                return;
            }

            if (Array.IndexOf(args, "--scrollframeprobe") >= 0)
            {
                SelfTest.RunScrollFrameProbe();
                return;
            }

            if (Array.IndexOf(args, "--captureuiprobe") >= 0)
            {
                SelfTest.RunCaptureUiProbe();
                return;
            }

            if (Array.IndexOf(args, "--iconstrip") >= 0)
            {
                SelfTest.RunIconStripProbe();
                return;
            }

            if (Array.IndexOf(args, "--scrollshotpreview") >= 0)
            {
                SelfTest.RunScrollShotPreview();
                return;
            }

            if (Array.IndexOf(args, "--softrenderprobe") >= 0)
            {
                SelfTest.RunSoftwareRenderProbe();
                return;
            }

            if (Array.IndexOf(args, "--softuipreview") >= 0)
            {
                SelfTest.RunSoftwareUiProbe();
                return;
            }

            if (Array.IndexOf(args, "--softdownloadprobe") >= 0)
            {
                SelfTest.RunSoftwareDownloadProbe();
                return;
            }

            if (Array.IndexOf(args, "--settingspreview") >= 0)
            {
                SelfTest.RunSettingsPreview();
                return;
            }

            if (Array.IndexOf(args, "--hoverprobe") >= 0)
            {
                SelfTest.RunHoverProbe();
                return;
            }

            if (Array.IndexOf(args, "--occlusionprobe") >= 0)
            {
                SelfTest.RunOcclusionProbe();
                return;
            }

            int imageTestIndex = Array.IndexOf(args, "--imagetest");
            if (imageTestIndex >= 0)
            {
                string path = imageTestIndex + 1 < args.Length
                    ? args[imageTestIndex + 1]
                    : null;
                ImageTest.Run(path);
                return;
            }

            int ocrTestIndex = Array.IndexOf(args, "--ocrtest");
            if (ocrTestIndex >= 0)
            {
                string path = ocrTestIndex + 1 < args.Length
                    ? args[ocrTestIndex + 1]
                    : null;
                ImageTest.RunOcr(path);
                return;
            }

            int tencentTestIndex = Array.IndexOf(
                args, "--tencenttest");
            if (tencentTestIndex >= 0)
            {
                string path = tencentTestIndex + 1 < args.Length
                    ? args[tencentTestIndex + 1]
                    : null;
                ImageTest.RunTencent(path);
                return;
            }

            int baiduImageIndex = Array.IndexOf(
                args, "--baiduimagetest");
            if (baiduImageIndex >= 0)
            {
                string path = baiduImageIndex + 1 < args.Length
                    ? args[baiduImageIndex + 1]
                    : null;
                ImageTest.RunBaiduImage(path);
                return;
            }

            if (Array.IndexOf(args, "--linetest") >= 0)
            {
                ImageTest.RunLineTest();
                return;
            }

            if (Array.IndexOf(args, "--tencentprobe") >= 0)
            {
                ImageTest.RunTencentProbe(null);
                return;
            }

            if (Array.IndexOf(args, "--asrprobe") >= 0)
            {
                ImageTest.RunAsrProbe();
                return;
            }

            int voiceTestIndex = Array.IndexOf(args, "--voicetest");
            if (voiceTestIndex >= 0)
            {
                int seconds = 2;
                if (voiceTestIndex + 1 < args.Length)
                {
                    int.TryParse(args[voiceTestIndex + 1], out seconds);
                }
                ImageTest.RunVoiceTest(seconds);
                return;
            }

            if (Array.IndexOf(args, "--uipreview") >= 0)
            {
                ImageTest.RunUiPreview();
                return;
            }

            int focusTestIndex = Array.IndexOf(args, "--focustest");
            int inputTestIndex = Array.IndexOf(args, "--inputtest");
            if (focusTestIndex >= 0 || inputTestIndex >= 0)
            {
                string text = inputTestIndex >= 0 &&
                    inputTestIndex + 1 < args.Length
                        ? args[inputTestIndex + 1] : null;
                ImageTest.RunFocusTest(text);
                return;
            }

            if (Array.IndexOf(args, "--typetest") >= 0)
            {
                ImageTest.RunTypeTest();
                return;
            }

            int asrTestIndex = Array.IndexOf(args, "--asrtest");
            if (asrTestIndex >= 0)
            {
                string path = asrTestIndex + 1 < args.Length
                    ? args[asrTestIndex + 1]
                    : null;
                ImageTest.RunAsrTest(path);
                return;
            }

            if (Array.IndexOf(args, "--overlaypreview") >= 0)
            {
                ImageTest.RunOverlayPreview();
                return;
            }

            if (Array.IndexOf(args, "--startupprobe") >= 0)
            {
                ImageTest.RunStartupProbe();
                return;
            }

            int windowProbeIndex = Array.IndexOf(args, "--windowprobe");
            if (windowProbeIndex >= 0)
            {
                int x = int.MinValue;
                int y = int.MinValue;
                if (windowProbeIndex + 2 < args.Length)
                {
                    int.TryParse(args[windowProbeIndex + 1], out x);
                    int.TryParse(args[windowProbeIndex + 2], out y);
                }
                ImageTest.RunWindowProbe(x, y);
                return;
            }

            if (Array.IndexOf(args, "--windowlist") >= 0)
            {
                ImageTest.RunWindowList();
                return;
            }

            if (Array.IndexOf(args, "--iconpreview") >= 0)
            {
                ImageTest.RunIconPreview();
                return;
            }

            if (Array.IndexOf(args, "--shellon") >= 0)
            {
                ImageTest.RunShellToggle(true);
                return;
            }

            if (Array.IndexOf(args, "--shelloff") >= 0)
            {
                ImageTest.RunShellToggle(false);
                return;
            }

            if (Array.IndexOf(args, "--shellprobe") >= 0)
            {
                ImageTest.RunShellProbe();
                return;
            }

            if (Array.IndexOf(args, "--assocprobe") >= 0)
            {
                ImageTest.RunAssocProbe();
                return;
            }

            // 查看 / 执行一次「首次运行自动配置」。--firstrun 走的是
            // 与正常启动完全相同的入口，用来验证全新安装时的行为。
            if (Array.IndexOf(args, "--firstrun") >= 0)
            {
                ImageTest.RunFirstRunProbe(true);
                return;
            }

            if (Array.IndexOf(args, "--firstprobe") >= 0)
            {
                ImageTest.RunFirstRunProbe(false);
                return;
            }

            // 手动重跑首次配置（修复被清理过的集成项，不影响普通启动）。
            if (Array.IndexOf(args, "--firstsetup") >= 0)
            {
                FirstRunSetup.ForceRun();
                return;
            }

            int viewerPreviewIndex = Array.IndexOf(args, "--viewerpreview");
            if (viewerPreviewIndex >= 0)
            {
                string path = viewerPreviewIndex + 1 < args.Length
                    ? args[viewerPreviewIndex + 1]
                    : null;
                ImageTest.RunViewerPreview(path);
                return;
            }

            // 第一次真正运行（双击 exe、或第一次用图片打开）时，
            // 自动开启开机启动、图片右键菜单和「打开方式」。诊断命令
            // 已经在上面提前 return，不会被这套初始化影响。
            FirstRunSetup.Run();

            // 直接把图片拖到 exe 上，或用「打开方式」指定本程序：
            // 这时候只开一个图片查看器窗口，不启动托盘常驻。
            if (args.Length > 0 && System.IO.File.Exists(args[0]) &&
                ImageToolKit.IsImagePath(args[0]))
            {
                // 这类启动通常是右键菜单/「打开方式」进来的，
                // 顺手把注册项对齐到当前 exe，程序被挪了目录也能自愈。
                try
                {
                    ShellIntegration.Sync(
                        ConfigStore.Load(), Application.ExecutablePath);
                }
                catch (Exception ex)
                {
                    Log.Error("同步图片右键菜单失败", ex);
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(
                    UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;

                ImageViewerForm viewer = new ImageViewerForm();
                viewer.OpenPath(args[0]);
                if (!viewer.HasImage)
                {
                    viewer.Dispose();
                    return;
                }
                Application.Run(viewer);
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(
                true,
                "Local\\" + AppInfo.Id,
                out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "「" + AppInfo.Name + "」已经在运行。\r\n" +
                        "请查看任务栏右下角的托盘图标。",
                        AppInfo.Name,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(
                    UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException +=
                    OnUnhandledException;

                try
                {
                    Application.Run(new TrayApplicationContext());
                }
                catch (Exception ex)
                {
                    Log.Error("程序异常退出", ex);
                    MessageBox.Show(
                        "程序发生错误：" + ex.Message +
                        "\r\n\r\n日志：" + Log.FilePath,
                        AppInfo.Name,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private static void OnThreadException(
            object sender,
            ThreadExceptionEventArgs e)
        {
            Log.Error("UI 线程异常", e.Exception);
            MessageBox.Show(
                "发生错误：" + e.Exception.Message +
                "\r\n\r\n日志：" + Log.FilePath,
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static void OnUnhandledException(
            object sender,
            UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;
            Log.Error("未处理异常", exception);
        }
    }
}
