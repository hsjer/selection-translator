using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 图片文字链路诊断（使用当前已保存的凭证，结果写入 %APPDATA%）：
    ///   --ocrtest        &lt;图片&gt;  只取字（自动选引擎）
    ///   --imagetest      &lt;图片&gt;  识别 + 翻译（自动选引擎）
    ///   --tencenttest    &lt;图片&gt;  只调腾讯云 OCR，逐行输出坐标
    ///   --baiduimagetest &lt;图片&gt;  强制旧的百度图片翻译接口
    ///
    /// 日志不输出 AppID / 密钥 / 图片 Base64。
    /// </summary>
    internal static class ImageTest
    {
        private enum Mode
        {
            Ocr,
            Translate,
            Tencent,
            BaiduImage
        }

        public static void Run(string path)
        {
            RunCore(path, Mode.Translate);
        }

        public static void RunOcr(string path)
        {
            RunCore(path, Mode.Ocr);
        }

        public static void RunTencent(string path)
        {
            RunCore(path, Mode.Tencent);
        }

        public static void RunBaiduImage(string path)
        {
            RunCore(path, Mode.BaiduImage);
        }

        /// <summary>
        /// 真实验证「腾讯云 OCR 的文字行 → 百度文本翻译」这一段：
        /// 不需要腾讯云凭证，直接用固定的三行文本调用逐行翻译。
        /// </summary>
        public static void RunLineTest()
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;
                AppConfig config = ConfigStore.Load();
                log.AppendLine("appid=" + config.AppId);
                if (string.IsNullOrEmpty(config.AppId) ||
                    string.IsNullOrEmpty(config.GetSecret()))
                {
                    throw new InvalidOperationException(
                        "未配置百度翻译 AppID 或密钥，请先在设置中填写。");
                }

                System.Collections.Generic.List<string> lines =
                    new System.Collections.Generic.List<string>(
                        new string[]
                        {
                            "Hello world",
                            "This is line two",
                            "第三行中文"
                        });
                LineTranslationResult result =
                    BaiduTranslator.TranslateLinesAsync(
                        config, lines, "auto")
                        .GetAwaiter().GetResult();
                log.AppendLine("from=" + result.From);
                log.AppendLine("to=" + result.To);
                log.AppendLine("count=" + result.Lines.Count);
                for (int i = 0; i < lines.Count; i++)
                {
                    string dst = i < result.Lines.Count
                        ? result.Lines[i] : "";
                    log.AppendLine("[" + i + "] " + lines[i] +
                        " -> " + dst);
                }
                ok = result.Lines.Count == lines.Count &&
                    result.Lines[0].Length > 0;
                if (!ok)
                {
                    throw new InvalidOperationException(
                        "逐行译文数量与输入行数不一致。");
                }
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("linetest.txt", log, ok,
                "百度逐行翻译诊断");
        }

        /// <summary>
        /// 用一组固定的「假凭证」向腾讯云发一次真实请求，验证签名格式、
        /// Action / Version / Content-Type 等公共参数能被服务端正确解析。
        /// 预期返回 AuthFailure.*（凭证不存在），说明请求链路通畅；
        /// 返回网络错误或参数错误则说明代码有问题。不会读取或改动配置。
        /// </summary>
        public static void RunTencentProbe(string path)
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // 没有图片时用程序生成的一张小图。
                    path = null;
                }

                AppConfig probe = new AppConfig();
                probe.TencentSecretId =
                    "AKID00000000000000000000000000";
                probe.SetTencentSecretKey("probe-only-invalid-key");
                log.AppendLine("note=使用假凭证，仅验证请求格式");
                log.AppendLine("endpoint=" + TencentOcr.Endpoint);
                log.AppendLine("action=" + TencentOcr.Action);

                byte[] bytes;
                Size size;
                if (path == null)
                {
                    using (Bitmap bitmap = new Bitmap(320, 120))
                    {
                        using (Graphics graphics =
                            Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.White);
                        }
                        string note;
                        bytes = TencentOcr.PrepareImage(
                            bitmap, out note, out size);
                    }
                }
                else
                {
                    using (Bitmap bitmap = new Bitmap(path))
                    {
                        string note;
                        bytes = TencentOcr.PrepareImage(
                            bitmap, out note, out size);
                    }
                }
                log.AppendLine("bytes=" + bytes.Length);

                string code = "NONE";
                string message = "";
                try
                {
                    OcrResult result =
                        TencentOcr.RecognizeAsync(
                            probe, bytes, size)
                            .GetAwaiter().GetResult();
                    message = "意外成功，lines=" +
                        result.Lines.Count;
                }
                catch (TranslationException ex)
                {
                    code = ex.Code;
                    message = ex.Message;
                }
                log.AppendLine("serverCode=" + code);
                log.AppendLine("message=" + message);

                // 服务端进到鉴权层就说明签名串与公共参数格式都被接受了。
                ok = code.StartsWith(
                    "AuthFailure", StringComparison.Ordinal);
                if (!ok)
                {
                    log.AppendLine(
                        "hint=未收到鉴权错误，请检查网络或请求格式");
                }
                log.AppendLine(ok
                    ? "RESULT: OK (request format accepted)"
                    : "RESULT: FAILED");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("tencentprobe.txt", log, ok,
                "腾讯云请求格式探针");
        }

        private static void WriteReport(
            string fileName,
            StringBuilder log,
            bool ok,
            string title)
        {
            string reportPath = Path.Combine(
                ConfigStore.DataDirectory, fileName);
            try
            {
                Directory.CreateDirectory(
                    ConfigStore.DataDirectory);
                File.WriteAllText(
                    reportPath, log.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 写文件失败不阻断提示。
            }

            if (Environment.GetEnvironmentVariable(
                "ST_NO_UI") == "1")
            {
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }
            string text = log.ToString();
            MessageBox.Show(
                (text.Length > 900
                    ? text.Substring(0, 900) : text) +
                    "\r\n\r\n完整诊断：" + reportPath,
                title,
                MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }

        private static void RunCore(string path, Mode mode)
        {
            string title;
            string fileName;
            string command;
            switch (mode)
            {
                case Mode.Ocr:
                    title = "OCR 取字诊断";
                    fileName = "ocrtest.txt";
                    command = "--ocrtest";
                    break;
                case Mode.Tencent:
                    title = "腾讯云 OCR 诊断";
                    fileName = "tencenttest.txt";
                    command = "--tencenttest";
                    break;
                case Mode.BaiduImage:
                    title = "百度图片翻译诊断";
                    fileName = "imagetest.txt";
                    command = "--baiduimagetest";
                    break;
                default:
                    title = "图片识别翻译诊断";
                    fileName = "imagetest.txt";
                    command = "--imagetest";
                    break;
            }

            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    if (Environment.GetEnvironmentVariable(
                        "ST_NO_UI") == "1")
                    {
                        Environment.ExitCode = 1;
                        return;
                    }
                    MessageBox.Show(
                        "用法：\r\n" +
                        "SelectionTranslator.exe " + command +
                        " <图片路径>\r\n\r\n" +
                        "请传入一张含有文字的 PNG/JPG 图片。",
                        title,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                AppConfig config = ConfigStore.Load();
                log.AppendLine("mode=" + mode);
                log.AppendLine("baiduAppId=" + config.AppId);
                log.AppendLine("tencentSecretId=" +
                    (config.TencentSecretId ?? ""));
                log.AppendLine("useTencentOcr=" +
                    config.UseTencentOcr);
                log.AppendLine("hasTencentKey=" +
                    config.HasTencentCredentials());
                log.AppendLine("target=" +
                    config.ImageTargetLanguage);
                log.AppendLine("pipeline=" +
                    (PicturePipeline.UseTencent(config)
                        ? "tencent-ocr + baidu-text"
                        : "baidu-picture (fallback)"));

                if (string.IsNullOrEmpty(config.AppId) ||
                    string.IsNullOrEmpty(config.GetSecret()))
                {
                    if (mode == Mode.Tencent || mode == Mode.Ocr)
                    {
                        // 取字与纯 OCR 不需要百度凭证。
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "未配置百度翻译 AppID 或密钥，请先在设置中填写。");
                    }
                }

                using (Bitmap image = new Bitmap(path))
                {
                    log.AppendLine("image=" + image.Width +
                        "x" + image.Height);

                    if (mode == Mode.BaiduImage)
                    {
                        // 强制回退路径，不走腾讯云。
                        AppConfig baiduOnly = CloneWithoutTencent(
                            config);
                        PreparedPicture prepared =
                            PicturePipeline.Prepare(
                                image, baiduOnly);
                        log.AppendLine("bytes=" +
                            prepared.Bytes.Length + " " +
                            prepared.Note);
                        ImageTranslationResult result =
                            PicturePipeline.TranslateAsync(
                                baiduOnly, prepared)
                                .GetAwaiter().GetResult();
                        AppendResult(log, result);
                    }
                    else if (mode == Mode.Tencent)
                    {
                        if (!config.HasTencentCredentials())
                        {
                            throw new InvalidOperationException(
                                "未配置腾讯云 SecretId / SecretKey，请先在「设置」里填写。");
                        }
                        string note;
                        Size size;
                        byte[] bytes = TencentOcr.PrepareImage(
                            image, out note, out size);
                        log.AppendLine("bytes=" + bytes.Length +
                            " " + note + " sent=" + size.Width +
                            "x" + size.Height);
                        OcrResult ocr =
                            TencentOcr.RecognizeAsync(
                                config, bytes, size)
                                .GetAwaiter().GetResult();
                        log.AppendLine("requestId=" + ocr.RequestId);
                        log.AppendLine("lines=" + ocr.Lines.Count);
                        log.AppendLine("chars=" +
                            ocr.SumText.Length);
                        for (int i = 0; i < ocr.Lines.Count; i++)
                        {
                            log.AppendLine(
                                "[" + i + "] " +
                                ocr.Lines[i].Rect + " | " +
                                ocr.Lines[i].Text);
                        }
                        log.AppendLine("sumText=" + ocr.SumText);
                    }
                    else
                    {
                        PreparedPicture prepared =
                            PicturePipeline.Prepare(image, config);
                        log.AppendLine("bytes=" +
                            prepared.Bytes.Length + " " +
                            prepared.Note);
                        if (mode == Mode.Ocr)
                        {
                            string text =
                                PicturePipeline.RecognizeTextAsync(
                                    config, prepared)
                                    .GetAwaiter().GetResult();
                            log.AppendLine("chars=" + text.Length);
                            log.AppendLine("text=" + text);
                        }
                        else
                        {
                            ImageTranslationResult result =
                                PicturePipeline.TranslateAsync(
                                    config, prepared)
                                    .GetAwaiter().GetResult();
                            AppendResult(log, result);
                        }
                    }
                }

                ok = true;
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }

            string reportPath = Path.Combine(
                ConfigStore.DataDirectory, fileName);
            try
            {
                Directory.CreateDirectory(
                    ConfigStore.DataDirectory);
                File.WriteAllText(
                    reportPath, log.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 写文件失败不阻断提示。
            }

            // ST_NO_UI=1 供自动化诊断使用，不弹窗打扰用户。
            if (Environment.GetEnvironmentVariable(
                "ST_NO_UI") == "1")
            {
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }

            string summary = (ok ? "诊断成功。\r\n\r\n"
                    : "诊断失败。\r\n\r\n") +
                (log.Length > 900
                    ? log.ToString(0, 900)
                    : log.ToString());
            MessageBox.Show(
                summary + "\r\n\r\n完整诊断：" + reportPath,
                title,
                MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information
                    : MessageBoxIcon.Error);
        }

        private static void AppendResult(
            StringBuilder log,
            ImageTranslationResult result)
        {
            log.AppendLine("from=" + result.From);
            log.AppendLine("to=" + result.To);
            log.AppendLine("sumSrc=" + result.SumSrc);
            log.AppendLine("sumDst=" + result.SumDst);
            int count = result.Segments == null
                ? 0 : result.Segments.Count;
            log.AppendLine("segments=" + count);
            for (int i = 0; i < count && i < 20; i++)
            {
                ImageSegmentInfo segment = result.Segments[i];
                log.AppendLine(
                    "[" + i + "] " + segment.Rect + " | " +
                    segment.Src + " -> " + segment.Dst);
            }
            log.AppendLine("pasteImg=" +
                (result.PastedImage == null
                    ? 0 : result.PastedImage.Length));
        }

        private static AppConfig CloneWithoutTencent(AppConfig source)
        {
            AppConfig copy = new AppConfig();
            copy.AppId = source.AppId;
            copy.SecretProtected = source.SecretProtected;
            copy.TargetLanguage = source.TargetLanguage;
            copy.ImageTargetLanguage = source.ImageTargetLanguage;
            copy.UseTencentOcr = false;
            return copy;
        }
    }
}
