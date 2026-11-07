using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    /// <summary>
    /// 图片翻译接口返回的单个识别分段。
    /// </summary>
    public class ImageSegmentInfo
    {
        public string Src { get; set; }
        public string Dst { get; set; }
        public string Rect { get; set; }
        public int LineCount { get; set; }
    }

    /// <summary>
    /// 图片翻译结果。
    /// </summary>
    public class ImageTranslationResult
    {
        /// <summary>识别出的源语种。</summary>
        public string From { get; set; }

        /// <summary>实际使用的目标语种。</summary>
        public string To { get; set; }

        /// <summary>未分段翻译原文（OCR 识别文本）。</summary>
        public string SumSrc { get; set; }

        /// <summary>未分段翻译译文。</summary>
        public string SumDst { get; set; }

        /// <summary>分段明细。</summary>
        public List<ImageSegmentInfo> Segments { get; set; }

        /// <summary>整图贴合译文图片（paste=1 时有效），可能为 null。</summary>
        public byte[] PastedImage { get; set; }
    }

    /// <summary>
    /// 百度翻译「图片翻译」接口客户端。
    /// 文档：https://fanyi-api.baidu.com/doc/26
    ///
    /// 与文本翻译的差异注意：
    /// - 接口地址为 /api/trans/sdk/picture，图片以 multipart 表单上传；
    /// - 签名规则是 md5(appid + md5(image) + salt + cuid + mac + 密钥)，
    ///   其中 md5(image) 对原始图片字节做 MD5（不做任何编码转换），
    ///   cuid 固定 APICUID、mac 固定 mac、version 固定 3；
    /// - 语种代码不同：日语 jp（不是 ja）、韩语 kor（不是 ko）、
    ///   法语 fra（不是 fr）、西班牙语 spa（不是 es）；
    /// - 目标语种不支持 auto，也不支持 ar/th/vi 等文本翻译常见语种。
    /// </summary>
    public static class BaiduImageTranslator
    {
        private const string Endpoint =
            "https://fanyi-api.baidu.com/api/trans/sdk/picture";

        // 接口对图片的限制：≤4M、短边≥30px、长边≤4096px、长宽比≤3:1。
        private const int MaxFileBytes = 4 * 1024 * 1024;
        private const int MaxImageSide = 4096;
        private const int MinImageSide = 30;

        // 接口要求固定值。
        public const string Cuid = "APICUID";
        public const string Mac = "mac";

        /// <summary>图片翻译支持的目标语种（不含 auto）。</summary>
        public static readonly string[] SupportedTargetLanguages = new string[]
        {
            "zh", "en", "jp", "kor", "fra", "spa", "ru", "pt", "de", "it",
            "dan", "nl", "may", "swe", "id", "pl", "rom", "tr", "el", "hu"
        };

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpClient client = new HttpClient(handler);
            // 图片翻译包含 OCR，耗时通常比文本翻译长。
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SelectionTranslator/1.0");
            return client;
        }

        /// <summary>
        /// 构造图片翻译签名：md5(appid + md5(image) + salt + cuid + mac + 密钥)。
        /// imageMd5 应为原始图片字节的 32 位小写 MD5。
        /// </summary>
        public static string BuildImageSign(
            string appId,
            string imageMd5,
            string salt,
            string secret)
        {
            return Md5Hex(
                (appId ?? "") +
                (imageMd5 ?? "") +
                (salt ?? "") +
                Cuid +
                Mac +
                (secret ?? ""));
        }

        /// <summary>计算原始字节数据的 32 位小写 MD5。</summary>
        public static string Md5OfBytes(byte[] data)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(data ?? new byte[0]);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        /// <summary>
        /// 把文本翻译语种代码映射为图片翻译语种代码。
        /// ja→jp、ko→kor、fr→fra、es→spa，其余原样返回。
        /// unknown（含 ar/th/vi）抛出 TranslationException。
        /// </summary>
        public static string MapImageLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return "auto"; // 由调用方按 auto 处理
            }
            switch (code.ToLowerInvariant())
            {
                case "ja": return "jp";
                case "ko": return "kor";
                case "fr": return "fra";
                case "es": return "spa";
                case "auto": return "auto";
                default:
                    if (IsSupportedTarget(code.ToLowerInvariant()))
                    {
                        return code.ToLowerInvariant();
                    }
                    throw new TranslationException(
                        "图片翻译不支持目标语言：" + code +
                        "。请在设置中选择受支持的语种。",
                        "LANG_UNSUPPORTED");
            }
        }

        public static bool IsSupportedTarget(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return false;
            }
            string normalized = code.ToLowerInvariant();
            for (int i = 0; i < SupportedTargetLanguages.Length; i++)
            {
                if (SupportedTargetLanguages[i] == normalized)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 只取 OCR 识别出的原文，不展示 / 不使用译文。
        ///
        /// 注意：百度图片翻译接口没有「仅 OCR」开关，from / to 都是必填，
        /// 服务端每次都会照常翻译并按一次图片翻译调用计费。这里能做的是
        /// 只读取 sumSrc / content[].src，并用 paste=0 跳过贴合译文图，
        /// 让请求更轻、更快。真正不翻译需要另接独立 OCR 服务。
        /// </summary>
        public static async Task<string> RecognizeTextAsync(
            AppConfig config, byte[] imageBytes)
        {
            ImageTranslationResult result =
                await RecognizeResultAsync(config, imageBytes);
            return BuildOcrText(result);
        }

        /// <summary>
        /// 与 RecognizeTextAsync 相同的请求（paste=0），但保留每段的 src 和
        /// rect，供结果窗口在图上做「可选字」热区；译文被清空。
        /// </summary>
        public static async Task<ImageTranslationResult> RecognizeResultAsync(
            AppConfig config, byte[] imageBytes)
        {
            if (config == null)
            {
                throw new TranslationException("配置为空", "NO_CONFIG");
            }
            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new TranslationException(
                    "没有可识别的图片", "EMPTY_IMAGE");
            }

            string appId = (config.AppId ?? "").Trim();
            string secret = config.GetSecret();
            if (appId.Length == 0 || secret.Length == 0)
            {
                throw new TranslationException(
                    "未配置百度翻译 AppID 或密钥。请右键托盘图标，打开「设置」填写。",
                    "NO_CREDENTIALS");
            }

            // 目标语种只要是接口支持的合法值即可，译文会被忽略。
            // auto 不能直传，沿用「中文→英文」的默认方向。
            string target;
            try
            {
                target = MapImageLanguage(config.ImageTargetLanguage);
            }
            catch (TranslationException)
            {
                target = "en";
            }
            if (target == "auto")
            {
                target = "en";
            }

            ImageTranslationResult result;
            bool retryToChinese = false;
            try
            {
                result = await PostImageAsync(
                    appId, secret, imageBytes, "auto", target, 0);
            }
            catch (TranslationException ex)
            {
                // 源语种碰巧与目标语种相同等方向性错误时，换个方向重试一次。
                if (target == "zh" ||
                    (ex.Code != "58001" && ex.Code != "58002"))
                {
                    throw;
                }
                retryToChinese = true;
                result = null;
            }
            // C# 5 不允许在 catch 块中 await，降级请求放到 catch 之外。
            if (retryToChinese)
            {
                result = await PostImageAsync(
                    appId, secret, imageBytes, "auto", "zh", 0);
            }

            string text = BuildOcrText(result);
            if (text.Length == 0)
            {
                throw new TranslationException(
                    "图片中没有识别到文字，请确认截图区域内有清晰文本。",
                    "NO_TEXT");
            }

            // 「取字」只需要原文：清掉译文与贴合图，避免结果窗口
            // 把译文当正文画到图上。
            if (result != null)
            {
                result.SumDst = "";
                result.PastedImage = null;
                if (result.Segments != null)
                {
                    for (int i = 0; i < result.Segments.Count; i++)
                    {
                        ImageSegmentInfo segment = result.Segments[i];
                        if (segment != null)
                        {
                            segment.Dst = "";
                            segment.LineCount = 1;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 从图片翻译结果里抽出纯 OCR 原文：优先用接口整理好的
        /// sumSrc；缺失时按 rect 阅读顺序拼接各分段 src。
        /// </summary>
        public static string BuildOcrText(ImageTranslationResult result)
        {
            if (result == null)
            {
                return "";
            }
            string text = (result.SumSrc ?? "").Trim();
            if (text.Length > 0)
            {
                return text;
            }
            return JoinOcrSegments(result.Segments);
        }

        private sealed class OcrBox
        {
            public string Text;
            public float Left;
            public float Top;
            public float Height;
            public int Index;
        }

        /// <summary>
        /// 按阅读顺序（先上后下、同一行从左到右）拼接 OCR 分段原文。
        /// 没有 rect 或 rect 无法解析时保持接口返回的原始顺序。
        /// </summary>
        public static string JoinOcrSegments(
            List<ImageSegmentInfo> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return "";
            }

            List<OcrBox> boxes = new List<OcrBox>();
            for (int i = 0; i < segments.Count; i++)
            {
                ImageSegmentInfo segment = segments[i];
                if (segment == null)
                {
                    continue;
                }
                string source = (segment.Src ?? "").Trim();
                if (source.Length == 0)
                {
                    continue;
                }

                OcrBox box = new OcrBox();
                box.Text = source;
                box.Index = i;
                box.Left = i;
                box.Top = i;
                box.Height = 0;
                RectangleF rect;
                if (TryParseOcrRect(segment.Rect, out rect))
                {
                    box.Left = rect.X;
                    box.Top = rect.Y;
                    box.Height = rect.Height;
                }
                boxes.Add(box);
            }

            boxes.Sort(CompareOcrBoxes);

            StringBuilder builder = new StringBuilder();
            float lineTop = 0;
            float lineHeight = 0;
            bool hasLine = false;
            for (int i = 0; i < boxes.Count; i++)
            {
                OcrBox box = boxes[i];
                if (!hasLine)
                {
                    hasLine = true;
                    lineTop = box.Top;
                    lineHeight = box.Height;
                }
                else if (Math.Abs(box.Top - lineTop) <=
                    Math.Max(6f, lineHeight * 0.6f))
                {
                    builder.Append(' ');
                    lineHeight = Math.Max(lineHeight, box.Height);
                }
                else
                {
                    builder.AppendLine();
                    lineTop = box.Top;
                    lineHeight = box.Height;
                }
                builder.Append(box.Text);
            }
            return builder.ToString().Trim();
        }

        private static int CompareOcrBoxes(OcrBox a, OcrBox b)
        {
            int compared = a.Top.CompareTo(b.Top);
            if (compared != 0)
            {
                return compared;
            }
            compared = a.Left.CompareTo(b.Left);
            if (compared != 0)
            {
                return compared;
            }
            return a.Index.CompareTo(b.Index);
        }

        /// <summary>解析接口返回的 rect（"left top width height"）。</summary>
        public static bool TryParseOcrRect(
            string text, out RectangleF rect)
        {
            rect = RectangleF.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string[] parts = text.Trim().Split(
                new char[] { ' ', '\t', ',' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                return false;
            }
            float left, top, width, height;
            if (!float.TryParse(parts[0], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out left) ||
                !float.TryParse(parts[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out top) ||
                !float.TryParse(parts[2], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out width) ||
                !float.TryParse(parts[3], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out height))
            {
                return false;
            }
            if (width <= 0 || height <= 0)
            {
                return false;
            }
            rect = new RectangleF(left, top, width, height);
            return true;
        }

        /// <summary>
        /// 翻译图片。from 始终传 auto（由接口自动识别）。
        /// to 为目标语种代码（图片接口代码，或 auto 按“中文→英文，其他→中文”处理）。
        /// 默认请求 paste=1（整图贴合），失败时自动降级为 paste=0 仅返回文本。
        /// </summary>
        public static async Task<ImageTranslationResult> TranslateImageAsync(
            AppConfig config,
            byte[] imageBytes,
            string to)
        {
            if (config == null)
            {
                throw new TranslationException("配置为空", "NO_CONFIG");
            }
            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new TranslationException(
                    "没有可翻译的图片", "EMPTY_IMAGE");
            }

            string appId = (config.AppId ?? "").Trim();
            string secret = config.GetSecret();
            if (appId.Length == 0 || secret.Length == 0)
            {
                throw new TranslationException(
                    "未配置百度翻译 AppID 或密钥。请右键托盘图标，打开「设置」填写。",
                    "NO_CREDENTIALS");
            }

            string target = MapImageLanguage(to);
            if (target == "auto")
            {
                // 图片接口目标语种不支持 auto：先按“中文→英文”请求，
                // 若识别出的源语种不是中文，再按“其他→中文”重试一次。
                ImageTranslationResult first = await RunOnceAsync(
                    config, appId, secret, imageBytes, "en");
                if (IsChineseSource(first.From) &&
                    first.SumDst != null &&
                    first.SumDst.Length > 0)
                {
                    first.To = "en";
                    return first;
                }
                return await RunOnceAsync(config, appId, secret,
                    imageBytes, "zh");
            }

            return await RunOnceAsync(config, appId, secret, imageBytes, target);
        }

        /// <summary>判断接口识别出的源语种是否为中文（zh/chi/chinese 系列）。</summary>
        private static bool IsChineseSource(string from)
        {
            if (string.IsNullOrEmpty(from))
            {
                return false;
            }
            string lower = from.ToLowerInvariant().Replace("-", "");
            return lower.StartsWith("zh") ||
                lower.StartsWith("chi") ||
                lower == "cn";
        }

        /// <summary>
        /// 执行一次请求。优先请求整图贴合（paste=1）；若接口因
        /// needIntervene/paste 等原因返回错误，降级为仅文本（paste=0）。
        /// </summary>
        private static async Task<ImageTranslationResult> RunOnceAsync(
            AppConfig config,
            string appId,
            string secret,
            byte[] imageBytes,
            string target)
        {
            // C# 5 不允许在 catch 块中使用 await，因此用标记记录降级，
            // 把重试请求放到 catch 之外执行。
            try
            {
                return await PostImageAsync(
                    appId, secret, imageBytes, "auto", target, 1);
            }
            catch (TranslationException ex)
            {
                if (ex.Code != "PASTE_REDUCED" &&
                    ex.Code != "IMAGE_TOO_LARGE")
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                // 任何未知异常（如 base64 过大）都尝试降级为纯文本。
                Log.Error("整图贴合请求失败，降级为纯文本：", ex);
            }

            return await PostImageAsync(
                appId, secret, imageBytes, "auto", target, 0);
        }

        private static async Task<ImageTranslationResult> PostImageAsync(
            string appId,
            string secret,
            byte[] imageBytes,
            string from,
            string to,
            int paste)
        {
            string salt = DateTime.UtcNow.Ticks.ToString();
            string imageMd5 = Md5OfBytes(imageBytes);
            string sign = BuildImageSign(appId, imageMd5, salt, secret);

            // 便于排查 54001：只记录非敏感字段，绝不写入密钥。
            Log.Info("图片翻译请求：appid=" + appId +
                " salt=" + salt + " imageMd5=" + imageMd5 +
                " imageBytes=" + imageBytes.Length +
                " from=" + from + " to=" + to +
                " paste=" + paste + " sign=" + sign);

            Dictionary<string, string> fields = new Dictionary<string, string>();
            fields["from"] = from;
            fields["to"] = to;
            fields["appid"] = appId;
            fields["salt"] = salt;
            fields["cuid"] = Cuid;
            fields["mac"] = Mac;
            fields["version"] = "3";
            fields["paste"] = paste.ToString();
            fields["sign"] = sign;

            string json = await PostMultipartAsync(
                imageBytes, fields, "image/png");

            return ParseResponse(json, to);
        }

        private static async Task<string> PostMultipartAsync(
            byte[] imageBytes,
            Dictionary<string, string> fields,
            string contentType)
        {
            Exception lastError = null;
            bool timedOut = false;

            // 官方文档的请求示例把 appid/salt/sign/cuid/mac/version 等
            // 参数全部放在 URL query 上，只有 image 通过 multipart 上传。
            // 若把参数塞进表单体，服务端验签时取到的值与签名时对不上，
            // 会直接返回 54001 Invalid Sign。
            string url = Endpoint + "?" + BuildQuery(fields);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (MultipartFormDataContent content =
                        new MultipartFormDataContent())
                    {
                        ByteArrayContent imageContent =
                            new ByteArrayContent(imageBytes);
                        imageContent.Headers.ContentType =
                            MediaTypeHeaderValue.Parse(contentType);
                        // 必须带 filename，否则服务端不会把它当作上传文件，
                        // md5(image) 会算成空值，同样导致 54001。
                        // 手写 Content-Disposition，避免框架额外附加
                        // filename*= 造成服务端解析差异。
                        imageContent.Headers.ContentDisposition =
                            new ContentDispositionHeaderValue("form-data")
                            {
                                Name = "\"image\"",
                                FileName = "\"image.png\""
                            };
                        content.Add(imageContent);

                        using (HttpResponseMessage response =
                            await Client.PostAsync(url, content))
                        {
                            string json =
                                await response.Content.ReadAsStringAsync();
                            if (!response.IsSuccessStatusCode)
                            {
                                throw new TranslationException(
                                    string.Format(
                                        "图片翻译接口返回 HTTP {0}",
                                        (int)response.StatusCode),
                                    "HTTP_" + (int)response.StatusCode);
                            }
                            return json;
                        }
                    }
                }
                catch (TranslationException ex)
                {
                    // paste=1 时的结构性错误（如贴合被降级拒绝）
                    // 由调用方处理；这里只透传。
                    lastError = ex;
                    throw;
                }
                catch (TaskCanceledException ex)
                {
                    lastError = ex;
                    timedOut = true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    timedOut = false;
                }

                if (attempt == 0)
                {
                    await Task.Delay(250);
                    continue;
                }

                if (timedOut)
                {
                    throw new TranslationException(
                        "图片翻译请求超时，请检查网络后重试。",
                        "TIMEOUT");
                }

                Log.Error("请求图片翻译失败", lastError);
                string detail = lastError == null ? ""
                    : "（" + lastError.Message + "）";
                throw new TranslationException(
                    "网络请求失败，请检查网络、代理或防火墙后重试。" +
                    detail, "NETWORK");
            }

            throw new TranslationException("网络请求失败，请稍后重试。", "NETWORK");
        }

        private static ImageTranslationResult ParseResponse(
            string json,
            string requestedTo)
        {
            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                root = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                Log.Error("解析图片翻译响应失败", ex);
                throw new TranslationException(
                    "图片翻译接口返回了无法解析的数据。",
                    "BAD_RESPONSE");
            }

            if (root == null)
            {
                throw new TranslationException(
                    "图片翻译接口返回了空数据。", "BAD_RESPONSE");
            }

            object errorCodeObject;
            if (root.TryGetValue("error_code", out errorCodeObject))
            {
                string errorCode = Convert.ToString(errorCodeObject);
                if (!string.IsNullOrEmpty(errorCode) && errorCode != "0")
                {
                    string errorMessage = "";
                    object errorMessageObject;
                    if (root.TryGetValue("error_msg", out errorMessageObject))
                    {
                        errorMessage = Convert.ToString(errorMessageObject);
                    }
                    Log.Info("图片翻译返回错误码 " + errorCode +
                        "：" + errorMessage +
                        "；原始响应：" +
                        (json.Length > 600 ? json.Substring(0, 600) : json));
                    throw new TranslationException(
                        DescribeError(errorCode, errorMessage), errorCode);
                }
            }

            object dataObject;
            if (!root.TryGetValue("data", out dataObject) ||
                !(dataObject is Dictionary<string, object>))
            {
                throw new TranslationException(
                    "图片翻译接口没有返回数据。", "NO_RESULT");
            }
            Dictionary<string, object> data =
                dataObject as Dictionary<string, object>;

            ImageTranslationResult result = new ImageTranslationResult();
            result.From = GetString(data, "from");
            result.To = GetString(data, "to");
            if (result.To.Length == 0)
            {
                result.To = requestedTo;
            }
            result.SumSrc = GetString(data, "sumSrc");
            result.SumDst = GetString(data, "sumDst");
            result.Segments = new List<ImageSegmentInfo>();

            object contentObject;
            if (data.TryGetValue("content", out contentObject))
            {
                System.Collections.IList items =
                    contentObject as System.Collections.IList;
                if (items != null)
                {
                    foreach (object item in items)
                    {
                        Dictionary<string, object> row =
                            item as Dictionary<string, object>;
                        if (row == null)
                        {
                            continue;
                        }
                        ImageSegmentInfo segment = new ImageSegmentInfo();
                        segment.Src = GetString(row, "src");
                        segment.Dst = GetString(row, "dst");
                        segment.Rect = GetString(row, "rect");
                        object lineCountObject;
                        if (row.TryGetValue("lineCount", out lineCountObject))
                        {
                            segment.LineCount = Convert.ToInt32(
                                lineCountObject);
                        }
                        result.Segments.Add(segment);
                    }
                }
            }

            result.PastedImage = DecodePastedImage(GetString(data, "pasteImg"));

            return result;
        }

        /// <summary>
        /// 解码整图贴合返回的 base64 图片；带 data:URI 前缀时自动剥离。
        /// 解码失败返回 null（不影响文本结果）。
        /// </summary>
        private static byte[] DecodePastedImage(string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return null;
            }
            try
            {
                string clean = base64.Trim();
                int comma = clean.IndexOf(',');
                if (clean.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    && comma >= 0)
                {
                    clean = clean.Substring(comma + 1);
                }
                return Convert.FromBase64String(clean);
            }
            catch (Exception ex)
            {
                Log.Error("解码贴合译文图片失败", ex);
                return null;
            }
        }

        /// <summary>
        /// 把截图预处理为接口可用的 PNG 字节流：
        /// 长边超过 4096px 或体积超过 4M 时自动缩小。
        /// note 输出人类可读的处理说明（无处理时为空串）。
        /// </summary>
        public static byte[] PrepareImage(Bitmap image, out string note)
        {
            note = "";
            if (image == null ||
                image.Width <= 0 || image.Height <= 0)
            {
                throw new TranslationException("图片无效", "EMPTY_IMAGE");
            }

            int width = image.Width;
            int height = image.Height;
            int maxSide = Math.Max(width, height);
            int minSide = Math.Min(width, height);

            if (minSide < MinImageSide && maxSide * 3 > minSide * 1000)
            {
                note = "图片过窄（长宽比超过 3:1），接口可能拒绝。";
            }

            // 先解决尺寸上限，再循环解决体积上限。
            float scale = 1.0f;
            if (maxSide > MaxImageSide)
            {
                scale = (float)MaxImageSide / maxSide;
                note = "图片已缩小到 " + (int)(width * scale) + "×" +
                    (int)(height * scale) + "（长边上限 4096px）。";
            }

            int scaledWidth = width;
            int scaledHeight = height;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                Bitmap current = new Bitmap(
                    (int)Math.Round(width * scale),
                    (int)Math.Round(height * scale),
                    PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(current))
                    {
                        graphics.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(
                            image,
                            0, 0, current.Width, current.Height);
                    }

                    byte[] bytes = EncodePng(current);
                    if (bytes.Length <= MaxFileBytes || scale <= 0.15f)
                    {
                        if (bytes.Length > MaxFileBytes)
                        {
                            note += "图片已进一步缩小以符合 4M 体积限制。";
                        }
                        return bytes;
                    }

                    scaledWidth = current.Width;
                    scaledHeight = current.Height;
                    scale *= 0.75f;
                }
                finally
                {
                    current.Dispose();
                }
            }

            throw new TranslationException(
                "图片压缩后仍超过 4M 限制，请截取更小的区域。",
                "IMAGE_TOO_LARGE");
        }

        private static byte[] EncodePng(Bitmap bitmap)
        {
            ImageFormat format = ImageFormat.Png;
            MemoryStream stream = new MemoryStream();
            try
            {
                bitmap.Save(stream, format);
                return stream.ToArray();
            }
            finally
            {
                stream.Dispose();
            }
        }

        private static string GetString(
            Dictionary<string, object> dictionary,
            string key)
        {
            object value;
            if (dictionary.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToString(value);
            }
            return "";
        }

        /// <summary>
        /// 图片翻译专属错误码 + 通用错误码的友好描述。
        /// </summary>
        private static string DescribeError(string code, string message)
        {
            string friendly;
            switch (code)
            {
                case "52001":
                    friendly = "请求超时，请重试。";
                    break;
                case "52002":
                    friendly = "百度图片翻译系统错误，请稍后重试。";
                    break;
                case "52003":
                    friendly = "未授权用户：请检查 AppID 是否正确，或确认图片翻译服务已开通。";
                    break;
                case "52010":
                    friendly = "开放设备授权容量不足：请在百度翻译开放平台检查配额。";
                    break;
                case "54000":
                    friendly = "缺少必填参数或固定参数有误。";
                    break;
                case "54001":
                    friendly = "签名错误（Invalid Sign）：" +
                        "请确认已在百度翻译开放平台开通「图片翻译」服务，" +
                        "且设置中的 AppID / 密钥与控制台一致（修改密钥后需重启程序）。";
                    break;
                case "54003":
                    friendly = "访问频率受限，请稍后重试。";
                    break;
                case "54004":
                    friendly = "账户余额不足。";
                    break;
                case "54005":
                    friendly = "请求过于频繁，请 3 秒后再试。";
                    break;
                case "58000":
                    friendly = "客户端 IP 非法。";
                    break;
                case "58001":
                    friendly = "译文语言方向不支持：" +
                        "请在设置中选择图片翻译支持的语种。";
                    break;
                case "58002":
                    friendly = "服务当前已关闭。";
                    break;
                case "69001":
                    friendly = "上传图片数据有误：请检查图片内容。";
                    break;
                case "69002":
                    friendly = "图片识别超时，请重试。";
                    break;
                case "69003":
                    friendly = "内容识别失败：请确认图片中存在文字。";
                    break;
                case "69004":
                    friendly = "识别内容为空：图片中没有可识别的文字。";
                    break;
                case "69005":
                    friendly = "图片大小超过 4M 限制，请截取更小区域。";
                    break;
                case "69006":
                    friendly = "图片尺寸不符合标准（短边≥30px，长边≤4096px）。";
                    break;
                case "69007":
                    friendly = "图片格式不支持（仅支持 png/jpg）。";
                    break;
                case "69008":
                    friendly = "设备号（cuid）为空。";
                    break;
                case "69012":
                    friendly = "文字贴合参数异常，已自动降级为仅文本结果。";
                    break;
                default:
                    friendly = "百度图片翻译返回错误。";
                    break;
            }

            if (!string.IsNullOrEmpty(message))
            {
                friendly += "（" + message + "）";
            }
            return friendly;
        }

        private static string BuildQuery(
            Dictionary<string, string> fields)
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }
                builder.Append(Uri.EscapeDataString(pair.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value ?? ""));
            }
            return builder.ToString();
        }

        private static string Md5Hex(string text)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(
                    Encoding.UTF8.GetBytes(text ?? ""));
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
