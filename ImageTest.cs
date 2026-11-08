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
    ///   --focustest                  延时 3 秒后打印当前焦点控件
    ///   --inputtest      &lt;文字&gt;  延时 3 秒后把文字打进当前输入框
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
                // 诊断日志只记录「是否配置」，绝不写出 AppID / 密钥明文。
                log.AppendLine("hasBaiduCredentials=" +
                    (!string.IsNullOrEmpty(config.AppId) &&
                     !string.IsNullOrEmpty(config.GetSecret())));
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

        /// <summary>
        /// 用假凭证向「一句话识别」发一次真实请求，验证 asr 的签名、
        /// Action / Version / Content-Type 等公共参数能被服务端解析。
        /// 预期返回 AuthFailure.*；返回参数错误则说明报文有问题。
        /// 不读配置、不需要麦克风。
        /// </summary>
        public static void RunAsrProbe()
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;

                AppConfig probe = new AppConfig();
                probe.TencentSecretId =
                    "AKID00000000000000000000000000";
                probe.SetTencentSecretKey("probe-only-invalid-key");

                byte[] wav = BuildToneWav(1);
                log.AppendLine("note=使用假凭证与 1 秒正弦波，仅验证请求格式");
                log.AppendLine("endpoint=" + TencentAsr.Endpoint);
                log.AppendLine("action=" + TencentAsr.Action);
                log.AppendLine("version=" + TencentAsr.Version);
                log.AppendLine("engine=" + TencentAsr.DefaultEngine);
                log.AppendLine("wavBytes=" + wav.Length);
                log.AppendLine("durationMs=" +
                    VoiceRecorder.DurationMs(wav.Length));

                string code = "NONE";
                string message = "";
                try
                {
                    AsrResult result = TencentAsr.RecognizeAsync(
                        probe, wav, VoiceRecorder.DurationMs(wav.Length))
                        .GetAwaiter().GetResult();
                    code = "OK";
                    message = "意外成功：" + result.Text;
                }
                catch (TranslationException ex)
                {
                    code = ex.Code;
                    message = ex.Message;
                }
                log.AppendLine("serverCode=" + code);
                log.AppendLine("message=" + message);

                ok = code.StartsWith(
                    "AuthFailure", StringComparison.Ordinal) ||
                    code == "OK";
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
            WriteReport("asrprobe.txt", log, ok,
                "腾讯云语音识别请求格式探针");
        }

        /// <summary>
        /// 用已保存的真实凭证识别一段 WAV。不给路径时用 1 秒正弦波
        /// （只验证签名与报文，识别结果多半为空）。
        /// </summary>
        public static void RunAsrTest(string wavPath)
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;

                AppConfig config = ConfigStore.Load();
                byte[] wav;
                if (!string.IsNullOrEmpty(wavPath) && File.Exists(wavPath))
                {
                    wav = File.ReadAllBytes(wavPath);
                    log.AppendLine("source=" + wavPath);
                }
                else
                {
                    wav = BuildToneWav(1);
                    log.AppendLine("source=generated 1s tone");
                }

                int durationMs = VoiceRecorder.DurationMs(wav.Length);
                log.AppendLine("engine=" +
                    TencentAsr.DescribeEngine(config.VoiceEngineType));
                log.AppendLine("wavBytes=" + wav.Length);
                log.AppendLine("durationMs=" + durationMs);

                AsrResult result = TencentAsr.RecognizeAsync(
                    config, wav, durationMs)
                    .GetAwaiter().GetResult();

                log.AppendLine("chars=" + result.Text.Length);
                log.AppendLine("audioDurationMs=" +
                    result.AudioDurationMs);
                log.AppendLine("requestId=" + result.RequestId);
                log.AppendLine("text=" + result.Text);
                ok = true;
                log.AppendLine("RESULT: OK");
            }
            catch (TranslationException ex)
            {
                log.AppendLine("serverCode=" + ex.Code);
                log.AppendLine("message=" + ex.Message);
                log.AppendLine("RESULT: FAILED");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("asrtest.txt", log, ok, "腾讯云语音识别实测");
        }

        /// <summary>生成一段 440Hz 正弦波 WAV，用于验证链路是否通畅。</summary>
        private static byte[] BuildToneWav(int seconds)
        {
            int byteRate = VoiceRecorder.SampleRate * 2;
            int total = byteRate * Math.Max(1, seconds);
            byte[] pcm = new byte[total];
            for (int i = 0; i < total / 2; i++)
            {
                double t = (double)i / VoiceRecorder.SampleRate;
                short sample = (short)(
                    Math.Sin(2 * Math.PI * 440.0 * t) * 6000);
                pcm[i * 2] = (byte)(sample & 0xFF);
                pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return VoiceRecorder.WrapWav(pcm);
        }

        /// <summary>
        /// 只测录音链路（不需要凭证、不联网）：录 2 秒，报告设备数、
        /// 有效音频长度、峰值电平和保存的 wav 路径。
        /// </summary>
        public static void RunVoiceTest(int seconds)
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                if (seconds <= 0)
                {
                    seconds = 2;
                }
                int devices = VoiceRecorder.DeviceCount();
                log.AppendLine("devices=" + devices);
                log.AppendLine("seconds=" + seconds);

                byte[] wav;
                int peak;
                using (VoiceRecorder recorder = new VoiceRecorder())
                {
                    recorder.Start();
                    int waited = 0;
                    float maxLevel = 0f;
                    while (waited < seconds * 1000)
                    {
                        System.Threading.Thread.Sleep(100);
                        waited += 100;
                        if (recorder.Level > maxLevel)
                        {
                            maxLevel = recorder.Level;
                        }
                    }
                    wav = recorder.Stop();
                    peak = (int)(maxLevel * 100);
                    log.AppendLine("peakLevel=" + peak + "%");
                    log.AppendLine("reuseFailures=" +
                        recorder.ReuseFailures);
                }

                int durationMs = VoiceRecorder.DurationMs(wav.Length);
                log.AppendLine("wavBytes=" + wav.Length);
                log.AppendLine("durationMs=" + durationMs);

                string wavPath = Path.Combine(
                    ConfigStore.DataDirectory, "voicetest.wav");
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                File.WriteAllBytes(wavPath, wav);
                log.AppendLine("saved=" + wavPath);

                ok = durationMs >= seconds * 1000 - 800;
                log.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("voicetest.txt", log, ok, "麦克风录音自测");
        }

        /// <summary>
        /// 验证「任意输入框都放行」以及「文字直接上屏」。
        /// 命令行启动后给 3 秒，让用户把光标点进目标输入框。
        /// text 为空时只做焦点探测，不写任何东西。
        /// </summary>
        public static void RunFocusTest(string text)
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                log.AppendLine("等待 3 秒，请把光标点进一个输入框…");
                System.Threading.Thread.Sleep(3000);

                FocusTarget target = InputFocusProbe.Probe();
                log.AppendLine(target.Describe());
                log.AppendLine("method=type");

                if (target.IsLikelyNonInput)
                {
                    log.AppendLine("当前是桌面 / 菜单等明确非输入场景，未模拟键入。");
                    log.AppendLine("RESULT: FAILED");
                }
                else if (string.IsNullOrEmpty(text))
                {
                    log.AppendLine("焦点判定通过（只探测，未注入）。");
                    ok = true;
                    log.AppendLine("RESULT: OK");
                }
                else
                {
                    log.AppendLine("text=" + text);
                    log.AppendLine("normalized=" +
                        InputFocusProbe.NormalizeForTyping(text));
                    VoiceInsertOutcome outcome =
                        System.Threading.Tasks.Task.Run(
                            delegate
                            {
                                return InputFocusProbe.InsertAsync(
                                    target, text,
                                    InputFocusProbe.MethodType, false);
                            }).GetAwaiter().GetResult();
                    log.AppendLine("outcome=" + outcome);
                    ok = outcome == VoiceInsertOutcome.Inserted;
                    log.AppendLine(ok
                        ? "RESULT: OK"
                        : "RESULT: FAILED");
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("focustest.txt", log, ok, "焦点探测与上屏测试");
        }

        /// <summary>
        /// 真实回环测试：在屏幕外开一个只带 TextBox 的小窗体并聚焦，
        /// 走完整的「焦点探测 → SendInput Unicode 注入」链路，再读回
        /// TextBox.Text 比对。用来证明中文确实能直接打进输入框。
        /// </summary>
        public static void RunTypeTest()
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            Form host = null;
            TextBox box = null;
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                host = new Form();
                host.StartPosition = FormStartPosition.Manual;
                host.Location = new Point(-6000, -6000);
                host.Size = new Size(320, 120);
                host.ShowInTaskbar = false;

                box = new TextBox();
                box.Multiline = true;
                box.Location = new Point(8, 8);
                box.Size = new Size(280, 60);
                host.Controls.Add(box);

                host.Show();
                box.Focus();
                Application.DoEvents();
                System.Threading.Thread.Sleep(200);

                FocusTarget target = InputFocusProbe.Probe();
                log.AppendLine(target.Describe());

                string sample = "中文语音上屏 123";
                // 必须丢到线程池：WinForms 会安装同步上下文，
                // 直接 .GetResult() 会把 UI 线程堵死形成死锁。
                VoiceInsertOutcome outcome =
                    System.Threading.Tasks.Task.Run(
                        delegate
                        {
                            return InputFocusProbe.InsertAsync(
                                target,
                                sample,
                                InputFocusProbe.MethodType,
                                false);
                        }).GetAwaiter().GetResult();

                log.AppendLine("outcome=" + outcome);
                Application.DoEvents();
                log.AppendLine("textbox=[" + box.Text + "]");
                ok = outcome == VoiceInsertOutcome.Inserted &&
                    box.Text == sample;
                log.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            finally
            {
                if (host != null)
                {
                    host.Close();
                    host.Dispose();
                }
            }
            WriteReport("typetest.txt", log, ok, "文字直投回环测试");
        }

        /// <summary>
        /// 把语音浮条与设置界面离屏渲染成 PNG，方便在没有显示器的环境下
        /// 检查布局（输出到 %APPDATA%\SelectionTranslator\ui-*.png）。
        /// </summary>
        public static void RunUiPreview()
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);

                using (VoiceCaptureForm bar =
                    new VoiceCaptureForm("Ctrl+Alt+V"))
                {
                    bar.UpdateRecording(
                        TimeSpan.FromSeconds(4.2), 0.72f);
                    SaveForm(bar, "ui-voicebar-recording.png", log);
                    bar.ShowRecognizing("正在识别…");
                    SaveForm(bar, "ui-voicebar-recognizing.png", log);
                    bar.ShowDone("已输入 18 字");
                    SaveForm(bar, "ui-voicebar-done.png", log);
                    bar.ShowFailed("没听清，再说一次");
                    SaveForm(bar, "ui-voicebar-failed.png", log);
                }

                using (SettingsForm form = new SettingsForm(new AppConfig()))
                {
                    SaveForm(form, "ui-settings.png", log);
                }

                ok = true;
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("uipreview.txt", log, ok, "界面离屏预览");
        }

        /// <summary>
        /// 把图片查看器与四个工具对话框离屏渲染成 PNG（ui-viewer*.png）。
        /// 没有指定图片时现场生成一张渐变样图，保证任何机器上都能看布局。
        /// </summary>
        public static void RunViewerPreview(string path)
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                string sample = string.IsNullOrEmpty(path)
                    ? BuildSampleImage()
                    : path;
                log.AppendLine("sample: " + sample);

                using (ImageViewerForm viewer = new ImageViewerForm())
                {
                    SaveForm(viewer, "ui-viewer-empty.png", log);
                    viewer.OpenPath(sample);
                    SaveForm(viewer, "ui-viewer.png", log);
                }
                SaveGlyphSheet(log);

                string error;
                using (Bitmap image = ImageToolKit.Load(sample, out error))
                {
                    if (image == null)
                    {
                        throw new InvalidOperationException(
                            "无法加载样图：" + error);
                    }
                    ImageFileInfo info = ImageToolKit.Describe(
                        sample, image, ImageToolKit.SiblingImages(sample));
                    log.AppendLine(string.Format(
                        "info: {0} {1} {2}x{3} {4}",
                        info.Name, info.FormatName,
                        info.Width, info.Height,
                        ImageToolKit.FormatSize(info.SizeBytes)));

                    using (ImageConvertDialog dialog =
                        new ImageConvertDialog(info, image))
                    {
                        SaveForm(dialog, "ui-viewer-convert.png", log);
                    }
                    using (ImageCompressDialog dialog =
                        new ImageCompressDialog(info, image))
                    {
                        SaveForm(dialog, "ui-viewer-compress.png", log);
                    }
                    using (ImageResizeDialog dialog =
                        new ImageResizeDialog(image.Width, image.Height))
                    {
                        SaveForm(dialog, "ui-viewer-resize.png", log);
                    }
                    using (ImageRenameDialog dialog =
                        new ImageRenameDialog(info.Name))
                    {
                        SaveForm(dialog, "ui-viewer-rename.png", log);
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
            WriteReport("viewerpreview.txt", log, ok, "图片查看器离屏预览");
        }

        /// <summary>
        /// 把所有矢量图标放大画在一张图上，用来肉眼核对图形是否画对
        /// （ui-viewer-glyphs.png）。
        /// </summary>
        private static void SaveGlyphSheet(StringBuilder log)
        {
            GlyphKind[] kinds = new GlyphKind[]
            {
                GlyphKind.Prev, GlyphKind.Next, GlyphKind.Fit,
                GlyphKind.Actual, GlyphKind.ZoomIn, GlyphKind.ZoomOut,
                GlyphKind.RotateLeft, GlyphKind.RotateRight,
                GlyphKind.Crop, GlyphKind.Copy, GlyphKind.Save,
                GlyphKind.Open, GlyphKind.Undo, GlyphKind.Minimize,
                GlyphKind.Maximize, GlyphKind.Restore, GlyphKind.Close
            };
            int cell = 96;
            int columns = 6;
            int rows = (kinds.Length + columns - 1) / columns;
            using (Bitmap sheet = new Bitmap(
                columns * cell, rows * (cell + 18)))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(DarkUi.Window);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                for (int i = 0; i < kinds.Length; i++)
                {
                    int x = (i % columns) * cell;
                    int y = (i / columns) * (cell + 18);
                    DarkUi.DrawGlyph(g, kinds[i],
                        new Rectangle(x, y, cell, cell),
                        DarkUi.Text, 1.8f);
                    DarkUi.Text2(g, kinds[i].ToString(), DarkUi.FontSmall,
                        DarkUi.TextFaint,
                        new Rectangle(x, y + cell, cell, 18),
                        DarkUi.CenterMiddle);
                }
                string path = Path.Combine(
                    ConfigStore.DataDirectory, "ui-viewer-glyphs.png");
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                log.AppendLine("ui-viewer-glyphs.png = " +
                    sheet.Width + "x" + sheet.Height);
            }
        }

        /// <summary>生成一张带渐变和文字的样图，供界面预览使用。</summary>
        private static string BuildSampleImage()
        {
            string target = Path.Combine(
                ConfigStore.DataDirectory, "sample-image.png");
            using (Bitmap bitmap = new Bitmap(1200, 800))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush brush =
                        new System.Drawing.Drawing2D.LinearGradientBrush(
                            new Rectangle(0, 0, 1200, 800),
                            Color.FromArgb(24, 48, 96),
                            Color.FromArgb(168, 96, 40), 35f))
                    {
                        g.FillRectangle(brush, 0, 0, 1200, 800);
                    }
                    using (SolidBrush disc = new SolidBrush(
                        Color.FromArgb(120, 255, 255, 255)))
                    {
                        g.FillEllipse(disc, 120, 160, 420, 420);
                    }
                    using (Font font = new Font("Microsoft YaHei UI", 64f,
                        FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush ink = new SolidBrush(Color.White))
                    {
                        g.DrawString("图片工具", font, ink, 120f, 600f);
                    }
                }
                bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
            }
            return target;
        }

        private static void SaveForm(
            Form form, string fileName, StringBuilder log)
        {
            // DrawToBitmap 只会画「已经创建句柄并完成布局」的窗体，
            // 因此在屏幕外真正 Show 一次，再截给自己看。
            bool wasVisible = form.Visible;
            if (!wasVisible)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-6000, -6000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                form.Refresh();
                Application.DoEvents();
            }

            using (Bitmap surface =
                new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(
                    surface,
                    new Rectangle(0, 0, form.Width, form.Height));
                string path = Path.Combine(
                    ConfigStore.DataDirectory, fileName);
                surface.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                log.AppendLine(fileName + " = " +
                    form.Width + "x" + form.Height);
            }

            if (!wasVisible)
            {
                form.Hide();
            }
        }

        /// <summary>命令行开关右键菜单：true=注册，false=移除。</summary>
        public static void RunShellToggle(bool enable)
        {
            StringBuilder log = new StringBuilder();
            bool ok;
            try
            {
                string error;
                ok = ShellIntegration.SetEnabled(
                    enable, Application.ExecutablePath, out error);
                log.AppendLine("action = " + (enable ? "on" : "off"));
                log.AppendLine("exe = " + Application.ExecutablePath);
                log.AppendLine("keyPath = " + ShellIntegration.MenuKeyPath);
                log.AppendLine("enabled = " + ShellIntegration.IsEnabled());
                log.AppendLine("command = " + ShellIntegration.GetCommand());
                log.AppendLine("openWithEnabled = " +
                    ShellIntegration.IsOpenWithEnabled());
                log.AppendLine("openWithCommand = " +
                    ShellIntegration.GetOpenWithCommand());
                log.AppendLine("current = " +
                    ShellIntegration.IsCurrent(Application.ExecutablePath));
                if (!ok)
                {
                    log.AppendLine("error = " + error);
                }
                log.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            Environment.ExitCode = ok ? 0 : 1;
            WriteReport("shelltoggle.txt", log, ok, "图片右键菜单");
        }

        /// <summary>让 Shell 亲自解析新 ProgID，确认「打开方式」真的认得它。</summary>
        public static void RunAssocProbe()
        {
            StringBuilder log = new StringBuilder();
            bool ok = true;
            try
            {
                const uint Command = 1;
                const uint Executable = 2;
                const uint FriendlyAppName = 4;
                const uint DefaultIcon = 15;

                string id = ShellIntegration.ProgId;
                log.AppendLine("progId = " + id);
                log.AppendLine("friendlyAppName = " +
                    NativeMethods.QueryAssociation(FriendlyAppName, id));
                log.AppendLine("executable = " +
                    NativeMethods.QueryAssociation(Executable, id));
                log.AppendLine("command = " +
                    NativeMethods.QueryAssociation(Command, id));
                log.AppendLine("defaultIcon = " +
                    NativeMethods.QueryAssociation(DefaultIcon, id));

                string command = NativeMethods.QueryAssociation(Command, id);
                string exe = NativeMethods.QueryAssociation(
                    Executable, id);
                if (command.IndexOf(
                        ShellIntegration.TrimQuotes(
                            Application.ExecutablePath),
                        StringComparison.OrdinalIgnoreCase) < 0 ||
                    exe.IndexOf(
                        Path.GetFileName(Application.ExecutablePath),
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ok = false;
                }
                log.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            Environment.ExitCode = ok ? 0 : 1;
            WriteReport(
                "assocprobe.txt", log, ok, "打开方式关联解析");
        }

        /// <summary>某个扩展名的 OpenWithProgids 里有没有我们这一行。</summary>
        private static bool HasOpenWithValue(string extension)
        {
            return ShellIntegration.HasProgIdValue(extension);
        }

        /// <summary>打印当前图片右键菜单的注册状态，方便排查「右键没出现」。 </summary>
        public static void RunShellProbe()
        {
            StringBuilder log = new StringBuilder();
            bool ok = true;
            try
            {
                log.AppendLine("exe = " + Application.ExecutablePath);
                log.AppendLine("menuText = " + ShellIntegration.MenuText);
                log.AppendLine("keyPath = " + ShellIntegration.MenuKeyPath);
                log.AppendLine("enabled = " + ShellIntegration.IsEnabled());
                log.AppendLine("current = " +
                    ShellIntegration.IsCurrent(Application.ExecutablePath));
                log.AppendLine("command = " + ShellIntegration.GetCommand());
                log.AppendLine("");
                log.AppendLine("--- 打开方式(H) ---");
                log.AppendLine("progId = " + ShellIntegration.ProgId);
                log.AppendLine("enabled = " +
                    ShellIntegration.IsOpenWithEnabled());
                log.AppendLine("command = " +
                    ShellIntegration.GetOpenWithCommand());
                log.AppendLine("appKey = " +
                    ShellIntegration.AppKeyPath(Application.ExecutablePath));
                for (int i = 0;
                    i < ShellIntegration.ImageExtensions.Length; i++)
                {
                    string ext = ShellIntegration.ImageExtensions[i];
                    log.AppendLine(ext + " -> " +
                        HasOpenWithValue(ext));
                }
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("shellprobe.txt", log, ok, "图片右键菜单状态");
        }

        /// <summary>
        /// 离屏渲染截图浮层的「窗口悬停高亮」效果，只导出高亮区域周围的一块，
        /// 便于确认边框、尺寸标签、提示文字的排版。
        /// </summary>
        public static void RunOverlayPreview()
        {
            StringBuilder log = new StringBuilder();
            bool ok = true;
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                using (ScreenshotCaptureForm form =
                    new ScreenshotCaptureForm())
                {
                    Rectangle hover = new Rectangle(
                        Math.Max(40, form.ClientSize.Width / 2 - 380),
                        Math.Max(120, form.ClientSize.Height / 2 - 260),
                        720, 460);

                    typeof(ScreenshotCaptureForm).GetField(
                        "_hoverRect",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)
                        .SetValue(form, hover);
                    typeof(ScreenshotCaptureForm).GetField(
                        "_hoverPoint",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)
                        .SetValue(form, new Point(0, 0));

                    using (Bitmap surface = new Bitmap(
                        form.ClientSize.Width,
                        form.ClientSize.Height))
                    {
                        form.DrawToBitmap(surface, new Rectangle(
                            0, 0, surface.Width, surface.Height));

                        Rectangle crop = new Rectangle(
                            Math.Max(0, hover.X - 30),
                            Math.Max(0, hover.Y - 46),
                            Math.Min(surface.Width - Math.Max(0, hover.X - 30),
                                hover.Width + 60),
                            Math.Min(surface.Height - Math.Max(0, hover.Y - 46),
                                hover.Height + 80));
                        using (Bitmap part = new Bitmap(
                            crop.Width, crop.Height))
                        {
                            using (Graphics g = Graphics.FromImage(part))
                            {
                                g.DrawImage(surface,
                                    new Rectangle(0, 0, crop.Width, crop.Height),
                                    crop, GraphicsUnit.Pixel);
                            }
                            part.Save(Path.Combine(
                                ConfigStore.DataDirectory,
                                "ui-capture-hover.png"),
                                System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    log.AppendLine("ui-capture-hover.png = " +
                        hover.Width + "x" + hover.Height);
                }
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("overlaypreview.txt", log, ok, "截图浮层预览");
        }

        /// <summary>打印开机启动项的真实状态（路径、是否被禁用）。</summary>
        public static void RunStartupProbe()
        {
            StringBuilder log = new StringBuilder();
            bool ok = true;
            try
            {
                log.AppendLine("exe = " + Application.ExecutablePath);
                log.AppendLine("enabled = " + StartupManager.IsEnabled());
                log.AppendLine("command = " + StartupManager.GetCommand());
                log.AppendLine("expected = " + StartupManager.BuildCommand());
                log.AppendLine("current = " + StartupManager.IsCurrent());
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("startupprobe.txt", log, ok, "开机启动状态");
        }

        /// <summary>列出可见顶层窗口，用于确认拾取器看到的坐标系。</summary>
        public static void RunWindowList()
        {
            StringBuilder log = new StringBuilder();
            bool ok = true;
            try
            {
                List<WindowTarget> items = WindowPicker.ListTopLevel(40);
                log.AppendLine("count = " + items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    log.AppendLine(items[i].Describe());
                }
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("windowlist.txt", log, ok, "顶层窗口列表");
        }

        /// <summary>延时 3 秒后打印鼠标底下那个窗口（验证整窗拾取）。</summary>
        public static void RunWindowProbe(int x, int y)
        {
            StringBuilder log = new StringBuilder();
            bool ok = false;
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);
                Point cursor;
                if (x == int.MinValue)
                {
                    log.AppendLine("等待 3 秒，请把鼠标停在要截的窗口上…");
                    System.Threading.Thread.Sleep(3000);
                    cursor = Cursor.Position;
                }
                else
                {
                    cursor = new Point(x, y);
                }

                log.AppendLine("cursor = " + cursor.X + "," + cursor.Y);
                WindowTarget target = WindowPicker.PickAt(cursor);
                if (target == null)
                {
                    log.AppendLine("window = (none)");
                }
                else
                {
                    log.AppendLine("window = " + target.Describe());
                    log.AppendLine("usable = " +
                        WindowPicker.IsUsable(target.Bounds));
                    ok = WindowPicker.IsUsable(target.Bounds);
                }
                log.AppendLine(ok ? "RESULT: OK" : "RESULT: FAILED");
            }
            catch (Exception ex)
            {
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("windowprobe.txt", log, ok, "窗口拾取");
        }

        /// <summary>渲染图标：一张 256 原尺寸，一张小尺寸放大对照，方便肉眼验收。</summary>
        public static void RunIconPreview()
        {
            StringBuilder log = new StringBuilder();
            bool ok = true;
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDirectory);

                using (Bitmap big = AppIcon.Render(256))
                {
                    big.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory, "ui-icon-256.png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }

                int[] small = new int[] { 16, 24, 32, 48 };
                int zoom = 8;
                int pad = 14;
                int cell = 48 * zoom;
                int width = pad + small.Length * (cell + pad);
                int height = pad + cell + 10 + 18;
                using (Bitmap sheet = new Bitmap(
                    width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(30, 31, 34));
                    g.InterpolationMode =
                        System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode =
                        System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    using (Font font = new Font(
                        "Segoe UI", 11f, FontStyle.Regular,
                        GraphicsUnit.Pixel))
                    using (SolidBrush ink = new SolidBrush(
                        Color.FromArgb(190, 195, 205)))
                    {
                        for (int i = 0; i < small.Length; i++)
                        {
                            int x = pad + i * (cell + pad);
                            using (Bitmap icon = AppIcon.Render(small[i]))
                            {
                                int side = small[i] * zoom;
                                g.DrawImage(icon, new Rectangle(
                                    x + (cell - side) / 2,
                                    pad + (cell - side) / 2,
                                    side, side));
                            }
                            g.DrawString(small[i] + "px", font, ink,
                                x + 4, pad + cell + 6);
                        }
                    }
                    sheet.Save(
                        Path.Combine(
                            ConfigStore.DataDirectory, "ui-icon-sheet.png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }

                log.AppendLine("ui-icon-256.png");
                log.AppendLine("ui-icon-sheet.png = 16/24/32/48 @8x");
                log.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("RESULT: FAILED");
                log.AppendLine(ex.ToString());
            }
            WriteReport("iconpreview.txt", log, ok, "图标预览");
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
                // 只输出布尔状态：SecretId 属于账号标识，同样不写进日志。
                log.AppendLine("hasBaiduCredentials=" +
                    (!string.IsNullOrEmpty(config.AppId) &&
                     !string.IsNullOrEmpty(config.GetSecret())));
                log.AppendLine("hasTencentCredentials=" +
                    config.HasTencentCredentials());
                log.AppendLine("useTencentOcr=" +
                    config.UseTencentOcr);
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
                            // 走结果窗口实际使用的入口，确认能拿到坐标分段。
                            ImageTranslationResult ocrResult =
                                PicturePipeline.RecognizeAsync(
                                    config, prepared)
                                    .GetAwaiter().GetResult();
                            log.AppendLine("chars=" +
                                (ocrResult.SumSrc ?? "").Length);
                            log.AppendLine("segments=" +
                                (ocrResult.Segments == null
                                    ? 0 : ocrResult.Segments.Count));
                            for (int i = 0;
                                i < ocrResult.Segments.Count &&
                                i < 5; i++)
                            {
                                log.AppendLine("[" + i + "] " +
                                    ocrResult.Segments[i].Rect + " | " +
                                    ocrResult.Segments[i].Src);
                            }
                            log.AppendLine("text=" + ocrResult.SumSrc);
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
