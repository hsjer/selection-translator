using System;
using System.Collections;
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
