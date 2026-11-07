using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 图片翻译连通性诊断：
    /// SelectionTranslator.exe --imagetest &lt;图片路径&gt;
    ///
    /// 用当前保存的 AppID / 密钥真实调用一次百度图片翻译，把请求关键
    /// 信息与结果写到 %APPDATA%\SelectionTranslator\imagetest.txt，
    /// 并弹出一个结果提示。不会输出密钥。
    /// </summary>
    internal static class ImageTest
    {
        public static void Run(string path)
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    MessageBox.Show(
                        "用法：\r\n" +
                        "SelectionTranslator.exe --imagetest <图片路径>\r\n\r\n" +
                        "请传入一张含有文字的 PNG/JPG 图片。",
                        "图片翻译诊断",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                AppConfig config = ConfigStore.Load();
                log.AppendLine("appid=" + config.AppId);
                log.AppendLine("target=" + config.ImageTargetLanguage);
                if (string.IsNullOrEmpty(config.AppId) ||
                    string.IsNullOrEmpty(config.GetSecret()))
                {
                    throw new InvalidOperationException(
                        "未配置 AppID 或密钥，请先在设置中填写。");
                }

                using (Bitmap image = new Bitmap(path))
                {
                    log.AppendLine("image=" + image.Width + "x" +
                        image.Height);
                    string note;
                    byte[] bytes = BaiduImageTranslator.PrepareImage(
                        image, out note);
                    log.AppendLine("bytes=" + bytes.Length +
                        " " + note);

                    ImageTranslationResult result =
                        BaiduImageTranslator.TranslateImageAsync(
                            config, bytes, config.ImageTargetLanguage)
                            .GetAwaiter().GetResult();

                    log.AppendLine("from=" + result.From);
                    log.AppendLine("to=" + result.To);
                    log.AppendLine("sumSrc=" + result.SumSrc);
                    log.AppendLine("sumDst=" + result.SumDst);
                    log.AppendLine("segments=" +
                        (result.Segments == null
                            ? 0 : result.Segments.Count));
                    log.AppendLine("pasteImg=" +
                        (result.PastedImage == null
                            ? 0 : result.PastedImage.Length));
                }

                ok = true;
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }

            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllText(
                    Path.Combine(
                        ConfigStore.DataDirectory, "imagetest.txt"),
                    log.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 写文件失败不阻断提示。
            }

            string summary = ok
                ? "图片翻译成功。\r\n\r\n" + log.ToString()
                : "图片翻译失败。\r\n\r\n" +
                    (log.ToString().Length > 900
                        ? log.ToString().Substring(0, 900)
                        : log.ToString());
            // ST_NO_UI=1 供自动化诊断使用，不弹窗打扰用户。
            if (Environment.GetEnvironmentVariable("ST_NO_UI") == "1")
            {
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }

            MessageBox.Show(
                summary + "\r\n\r\n完整诊断：" +
                    Path.Combine(
                        ConfigStore.DataDirectory, "imagetest.txt"),
                "图片翻译诊断",
                MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
    }
}
