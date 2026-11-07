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
