using System;
using System.IO;
using System.Text;

namespace SelectionTranslator
{
    internal static class Log
    {
        private static readonly object SyncRoot = new object();

        public static string FilePath
        {
            get { return Path.Combine(ConfigStore.DataDirectory, "app.log"); }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message, Exception exception)
        {
            string text = message ?? "";
            if (exception != null)
            {
                text += " | " + exception;
            }
            Write("ERROR", text);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(ConfigStore.DataDirectory);
                    FileInfo info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 512 * 1024)
                    {
                        info.Delete();
                    }

                    string line = string.Format(
                        "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}\r\n",
                        DateTime.Now,
                        level,
                        message);
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不能影响主流程。
            }
        }
    }
}
