using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace SelectionTranslator
{
    /// <summary>已编码、待送识别的图片。</summary>
    public class PreparedPicture
    {
        public byte[] Bytes { get; set; }
        public Size Size { get; set; }
        public string Note { get; set; }
        public bool UseTencent { get; set; }
    }

    /// <summary>
    /// 图片文字处理管道。
    ///
    /// 默认路径（推荐）：
    ///   腾讯云 GeneralAccurateOCR 识别文字 + 坐标
    ///     → 百度「通用文本翻译」逐行翻译（一次请求翻完整张图）
    ///     → 译文按坐标叠加回原图，文字块可选中复制。
    ///
    /// 兜底路径（未配置腾讯云凭证或用户主动关闭）：
    ///   百度「图片翻译」接口一次完成 OCR + 翻译 + 整图贴合。
    ///
    /// 两条路径对外都产出 ImageTranslationResult，结果窗口无需区分来源。
    /// </summary>
    public static class PicturePipeline
    {
        /// <summary>是否走腾讯云 OCR：开关打开且 SecretId / SecretKey 都已填。</summary>
        public static bool UseTencent(AppConfig config)
        {
            return config != null &&
                config.UseTencentOcr &&
                !string.IsNullOrEmpty(
                    (config.TencentSecretId ?? "").Trim()) &&
                !string.IsNullOrEmpty(
                    config.GetTencentSecretKey());
        }

        public static PreparedPicture Prepare(
            Bitmap image, AppConfig config)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }

            PreparedPicture prepared = new PreparedPicture();
            prepared.UseTencent = UseTencent(config);
            string note;
            if (prepared.UseTencent)
            {
                Size size;
                prepared.Bytes = TencentOcr.PrepareImage(
                    image, out note, out size);
                prepared.Size = size;
            }
            else
            {
                prepared.Bytes = BaiduImageTranslator.PrepareImage(
                    image, out note);
                prepared.Size = DecodeSize(prepared.Bytes);
            }
            prepared.Note = note;
            return prepared;
        }

        private static Size DecodeSize(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image image = Image.FromStream(stream))
            {
                return image.Size;
            }
        }

        /// <summary>
        /// 「取字」：只识别文字并复制到剪贴板，不翻译、不弹窗。
        /// 腾讯云路径完全不消耗百度图片翻译额度。
        /// </summary>
        public static async Task<string> RecognizeTextAsync(
            AppConfig config, PreparedPicture picture)
        {
            if (picture == null || picture.Bytes == null)
            {
                throw new TranslationException(
                    "没有可识别的图片", "EMPTY_IMAGE");
            }

            if (picture.UseTencent)
            {
                OcrResult ocr = await TencentOcr.RecognizeAsync(
                    config, picture.Bytes, picture.Size);
                if (string.IsNullOrEmpty(ocr.SumText))
                {
                    throw new TranslationException(
                        "图片中没有识别到文字。", "NO_TEXT");
                }
                return ocr.SumText;
            }

            Log.Info("腾讯云 OCR 未启用，取字回退百度图片翻译");
            return await BaiduImageTranslator.RecognizeTextAsync(
                config, picture.Bytes);
        }

        /// <summary>
        /// 「译」：识别 + 翻译，产出结果窗口可直接消费的结构。
        /// </summary>
        public static async Task<ImageTranslationResult> TranslateAsync(
            AppConfig config, PreparedPicture picture)
        {
            if (picture == null || picture.Bytes == null)
            {
                throw new TranslationException(
                    "没有可翻译的图片", "EMPTY_IMAGE");
            }

            if (!picture.UseTencent)
            {
                Log.Info("腾讯云 OCR 未启用，图片翻译回退百度图片翻译");
                try
                {
                    return await BaiduImageTranslator
                        .TranslateImageAsync(
                            config, picture.Bytes,
                            config.ImageTargetLanguage);
                }
                catch (TranslationException ex)
                {
                    // 设置里用的是文本翻译语种表，其中 ar/th/vi 等
                    // 百度图片翻译不支持；回退时自动改成 auto 再试一次。
                    if (ex.Code != "LANG_UNSUPPORTED")
                    {
                        throw;
                    }
                    Log.Info("回退路径目标语种不受支持，改用自动：" +
                        config.ImageTargetLanguage);
                }
                return await BaiduImageTranslator.TranslateImageAsync(
                    config, picture.Bytes, "auto");
            }

            OcrResult ocr = await TencentOcr.RecognizeAsync(
                config, picture.Bytes, picture.Size);
            if (ocr.Lines.Count == 0)
            {
                throw new TranslationException(
                    "图片中没有识别到文字，无法翻译。", "NO_TEXT");
            }

            List<string> sources = new List<string>();
            for (int i = 0; i < ocr.Lines.Count; i++)
            {
                sources.Add(ocr.Lines[i].Text);
            }

            string target = MapTextTarget(config.ImageTargetLanguage);
            LineTranslationResult translated =
                await BaiduTranslator.TranslateLinesAsync(
                    config, sources, target);

            Log.Info("图片翻译完成：OCR 行数=" + ocr.Lines.Count +
                " 译文行数=" + translated.Lines.Count +
                " 目标语种=" + target);

            return BuildResult(ocr, translated);
        }

        /// <summary>
        /// 把腾讯云 OCR 行 + 百度逐行译文拼成结果窗口认识的结构。
        /// 没有整图贴合译文图，结果窗口会自行在每个文字块上绘制译文。
        /// </summary>
        public static ImageTranslationResult BuildResult(
            OcrResult ocr,
            LineTranslationResult translated)
        {
            ImageTranslationResult result =
                new ImageTranslationResult();
            result.Segments = new List<ImageSegmentInfo>();
            result.PastedImage = null;
            result.From = translated == null
                ? "" : translated.From;
            result.To = translated == null
                ? "" : translated.To;
            result.SumSrc = ocr == null ? "" : ocr.SumText;

            if (ocr == null || ocr.Lines == null)
            {
                result.SumDst = "";
                return result;
            }

            List<string> dst = new List<string>();
            if (translated != null && translated.Lines != null)
            {
                dst = translated.Lines;
            }
            for (int i = 0; i < ocr.Lines.Count; i++)
            {
                ImageSegmentInfo segment = new ImageSegmentInfo();
                segment.Src = ocr.Lines[i].Text;
                segment.Rect = ocr.Lines[i].Rect;
                segment.LineCount = 1;
                segment.Dst = i < dst.Count ? dst[i] : "";
                if (string.IsNullOrEmpty(segment.Dst))
                {
                    segment.Dst = segment.Src;
                }
                result.Segments.Add(segment);
            }

            result.SumDst = TencentOcr.BuildOrderedText(
                ocr.Lines, dst);
            if (string.IsNullOrEmpty(result.SumDst))
            {
                result.SumDst = result.SumSrc;
            }
            return result;
        }

        /// <summary>
        /// 目标语言代码归一化：新管道走百度「文本翻译」，用 ja/ko/fr/es；
        /// 旧版本设置里可能存的是图片翻译的 jp/kor/fra/spa，这里兼容回退。
        /// </summary>
        public static string MapTextTarget(string code)
        {
            string value = (code ?? "").Trim();
            if (value.Length == 0)
            {
                return "auto";
            }
            switch (value.ToLowerInvariant())
            {
                case "auto": return "auto";
                case "jp": return "ja";
                case "kor": return "ko";
                case "fra": return "fr";
                case "spa": return "es";
                default: return value.ToLowerInvariant();
            }
        }
    }
}
