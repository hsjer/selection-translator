using System;
using System.IO;
using System.Text;

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
