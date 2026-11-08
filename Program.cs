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

            bool createdNew;
            using (Mutex mutex = new Mutex(
                true,
                "Local\\SelectionTranslator",
                out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "「选中文本翻译」已经在运行。\r\n请查看任务栏右下角的托盘图标。",
                        "选中文本翻译",
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
                        "选中文本翻译",
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
                "选中文本翻译",
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
