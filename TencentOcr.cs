using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    /// <summary>
    /// 腾讯云 OCR 返回的单行文字。Rect 采用与百度图片翻译相同的
    /// "left top width height" 字符串格式，结果窗口可直接复用。
    /// </summary>
    public class OcrLine
    {
        public string Text { get; set; }
        public string Rect { get; set; }
        public RectangleF Box { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>
    /// 一次 GeneralAccurateOCR 的归一化结果。
    /// </summary>
    public class OcrResult
    {
        /// <summary>按阅读顺序（先分行、再从左到右）排好的文字行。</summary>
        public List<OcrLine> Lines { get; set; }

        /// <summary>按阅读顺序拼接好的全文，可直接写进剪贴板。</summary>
        public string SumText { get; set; }

        /// <summary>实际上传图片的尺寸，文字块坐标以它为基准。</summary>
        public Size ImageSize { get; set; }

        public string RequestId { get; set; }

        public OcrResult()
        {
            Lines = new List<OcrLine>();
            SumText = "";
            RequestId = "";
        }
    }

    /// <summary>
    /// 腾讯云文字识别 GeneralAccurateOCR（高精度版）客户端。
    ///
    /// 签名使用云 API 3.0 的 TC3-HMAC-SHA256：
    ///   1) CanonicalRequest = 方法 / URI / query / 规范化头 / 签名头 / payload 哈希
    ///   2) StringToSign     = 算法 + 时间戳 + CredentialScope + CanonicalRequest 哈希
    ///   3) 派生密钥          = HMAC 链：TC3+SecretKey → 日期 → 服务 → tc3_request
    ///   4) Signature         = HMAC-SHA256(派生密钥, StringToSign) 的十六进制
    ///
    /// 文档：https://cloud.tencent.com/document/product/866/33526
    /// 只依赖 .NET Framework 自带程序集，不引入任何第三方 SDK。
    /// </summary>
    public static class TencentOcr
    {
        public const string Endpoint = "https://ocr.tencentcloudapi.com";
        public const string Host = "ocr.tencentcloudapi.com";
        public const string Service = "ocr";
        public const string Action = "GeneralAccurateOCR";
        public const string Version = "2018-11-19";
        public const string DefaultRegion = "ap-guangzhou";

        private const string Algorithm = "TC3-HMAC-SHA256";
        private const string ContentType = "application/json; charset=utf-8";
        private const string SignedHeaders = "content-type;host";

        // 腾讯云要求图片 Base64 后不超过 10M，换算原始字节约 7.5M。
        private const int MaxRawBytes = 7 * 1024 * 1024;
        private const int MaxImageSide = 4096;

        private static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SelectionTranslator/1.0");
            return client;
        }

        // ------------------------------------------------------------------
        // 签名（public / internal 供自检验证固定向量）
        // ------------------------------------------------------------------

        /// <summary>生成 Authorization 头。timestamp 由调用方传入以便自检。</summary>
        public static string BuildAuthorization(
            string secretId,
            string secretKey,
            string region,
            string payload,
            long timestamp)
        {
            string date = Epoch.AddSeconds(timestamp)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string credentialScope =
                date + "/" + Service + "/tc3_request";

            string canonicalHeaders =
                "content-type:" + ContentType + "\n" +
                "host:" + Host + "\n";
            string canonicalRequest =
                "POST\n/\n\n" +
                canonicalHeaders + "\n" +
                SignedHeaders + "\n" +
                Sha256Hex(payload);

            string stringToSign =
                Algorithm + "\n" +
                timestamp.ToString(CultureInfo.InvariantCulture) + "\n" +
                credentialScope + "\n" +
                Sha256Hex(canonicalRequest);

            byte[] secretDate = HmacSha256(
                Encoding.UTF8.GetBytes("TC3" + (secretKey ?? "")),
                date);
            byte[] secretService = HmacSha256(
                secretDate, Service);
            byte[] secretSigning = HmacSha256(
                secretService, "tc3_request");
            byte[] signature = HmacSha256(
                secretSigning, stringToSign);

            return Algorithm +
                " Credential=" + (secretId ?? "") + "/" +
                credentialScope +
                ", SignedHeaders=" + SignedHeaders +
                ", Signature=" + ToHex(signature);
        }

        internal static string Sha256Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(text ?? ""));
                return ToHex(hash);
            }
        }

        internal static byte[] HmacSha256(byte[] key, string text)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(
                    Encoding.UTF8.GetBytes(text ?? ""));
            }
        }

        private static string ToHex(byte[] data)
        {
            StringBuilder builder = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                builder.Append(data[i].ToString("x2"));
            }
            return builder.ToString();
        }

        internal static long CurrentTimestamp()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        // ------------------------------------------------------------------
        // 图片准备
        // ------------------------------------------------------------------

        /// <summary>
        /// 把截图编码成腾讯云可接受的图片字节。尽量不缩放，保证 OCR
        /// 坐标与显示底图一一对应；只有超过大小上限才降级 JPEG / 等比缩放，
        /// 并通过 sentSize 返回实际尺寸供坐标映射。
        /// </summary>
        public static byte[] PrepareImage(
            Bitmap image, out string note, out Size sentSize)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }

            Bitmap work = image;
            bool cloned = false;
            try
            {
                int width = image.Width;
                int height = image.Height;
                int longest = Math.Max(width, height);
                if (longest > MaxImageSide)
                {
                    float ratio = MaxImageSide / (float)longest;
                    int newWidth = Math.Max(1,
                        (int)Math.Round(width * ratio));
                    int newHeight = Math.Max(1,
                        (int)Math.Round(height * ratio));
                    work = new Bitmap(newWidth, newHeight,
                        PixelFormat.Format24bppRgb);
                    cloned = true;
                    using (Graphics g = Graphics.FromImage(work))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode =
                            System.Drawing.Drawing2D.InterpolationMode
                                .HighQualityBicubic;
                        g.DrawImage(image, 0, 0, newWidth, newHeight);
                    }
                }

                sentSize = new Size(work.Width, work.Height);

                byte[] png = EncodePng(work);
                if (png.Length <= MaxRawBytes)
                {
                    note = "PNG " + work.Width + "x" + work.Height;
                    return png;
                }

                byte[] jpeg = EncodeJpeg(work, 88L);
                if (jpeg.Length <= MaxRawBytes)
                {
                    note = "JPEG q88 " + work.Width + "x" + work.Height;
                    return jpeg;
                }

                // 极端情况：再降一档质量，仍保持原尺寸以便坐标不错位。
                byte[] smaller = EncodeJpeg(work, 65L);
                note = "JPEG q65 " + work.Width + "x" + work.Height;
                return smaller;
            }
            finally
            {
                if (cloned && work != null)
                {
                    work.Dispose();
                }
            }
        }

        private static byte[] EncodePng(Bitmap image)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                image.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static byte[] EncodeJpeg(Bitmap image, long quality)
        {
            ImageCodecInfo codec = null;
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].MimeType == "image/jpeg")
                {
                    codec = codecs[i];
                    break;
                }
            }

            using (MemoryStream stream = new MemoryStream())
            {
                if (codec == null)
                {
                    image.Save(stream, ImageFormat.Jpeg);
                    return stream.ToArray();
                }
                using (EncoderParameters parameters =
                    new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality, quality);
                    image.Save(stream, codec, parameters);
                }
                return stream.ToArray();
            }
        }

        // ------------------------------------------------------------------
        // 请求
        // ------------------------------------------------------------------

        /// <summary>调用 GeneralAccurateOCR 识别图片中的文字。</summary>
        public static async Task<OcrResult> RecognizeAsync(
            AppConfig config,
            byte[] imageBytes,
            Size imageSize)
        {
            if (config == null)
            {
                throw new TranslationException("配置为空", "NO_CONFIG");
            }
            string secretId = (config.TencentSecretId ?? "").Trim();
            string secretKey = config.GetTencentSecretKey();
            if (secretId.Length == 0 || secretKey.Length == 0)
            {
                throw new TranslationException(
                    "未配置腾讯云 SecretId / SecretKey。请右键托盘图标打开「设置」，在「文字识别（腾讯云 OCR）」里填写。",
                    "NO_TENCENT_CREDENTIALS");
            }
            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new TranslationException("图片内容为空", "EMPTY_IMAGE");
            }

            string region = string.IsNullOrEmpty(config.TencentRegion)
                ? DefaultRegion : config.TencentRegion;
            string configId = string.IsNullOrEmpty(config.TencentOcrConfigId)
                ? "MulOCR" : config.TencentOcrConfigId;

            Dictionary<string, object> body =
                new Dictionary<string, object>();
            body["ImageBase64"] = Convert.ToBase64String(imageBytes);
            body["ConfigID"] = configId;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string payload = serializer.Serialize(body);

            long timestamp = CurrentTimestamp();
            string authorization = BuildAuthorization(
                secretId, secretKey, region, payload, timestamp);

            Log.Info("腾讯云 OCR 请求：size=" + imageSize.Width +
                "x" + imageSize.Height + " bytes=" + imageBytes.Length +
                " configId=" + configId + " region=" + region);

            string json;
            using (HttpRequestMessage request =
                new HttpRequestMessage(HttpMethod.Post, Endpoint))
            {
                request.Headers.TryAddWithoutValidation(
                    "Authorization", authorization);
                request.Headers.TryAddWithoutValidation(
                    "X-TC-Action", Action);
                request.Headers.TryAddWithoutValidation(
                    "X-TC-Version", Version);
                request.Headers.TryAddWithoutValidation(
                    "X-TC-Timestamp",
                    timestamp.ToString(CultureInfo.InvariantCulture));
                request.Headers.TryAddWithoutValidation(
                    "X-TC-Region", region);
                request.Content = new StringContent(
                    payload, Encoding.UTF8, "application/json");

                try
                {
                    using (HttpResponseMessage response =
                        await Client.SendAsync(request))
                    {
                        json = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            Log.Info("腾讯云 OCR HTTP " +
                                (int)response.StatusCode + "：" +
                                (json.Length > 500
                                    ? json.Substring(0, 500) : json));
                            throw new TranslationException(
                                "腾讯云 OCR 返回 HTTP " +
                                    (int)response.StatusCode,
                                "HTTP_" + (int)response.StatusCode);
                        }
                    }
                }
                catch (TranslationException)
                {
                    throw;
                }
                catch (TaskCanceledException)
                {
                    throw new TranslationException(
                        "腾讯云 OCR 请求超时，请检查网络后重试。",
                        "TIMEOUT");
                }
                catch (Exception ex)
                {
                    Log.Error("请求腾讯云 OCR 失败", ex);
                    throw new TranslationException(
                        "无法连接腾讯云 OCR，请检查网络、代理或防火墙。",
                        "NETWORK");
                }
            }

            OcrResult result = ParseResponse(json, imageSize);
            Log.Info("腾讯云 OCR 成功：lines=" + result.Lines.Count +
                " chars=" + result.SumText.Length +
                " requestId=" + result.RequestId);
            return result;
        }

        /// <summary>解析腾讯云响应；SelfTest 会传入固定 JSON 验证。</summary>
        public static OcrResult ParseResponse(
            string json, Size imageSize)
        {
            OcrResult result = new OcrResult();
            result.ImageSize = imageSize;

            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer serializer =
                    new JavaScriptSerializer();
                root = serializer.Deserialize<
                    Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                Log.Error("解析腾讯云 OCR 响应失败", ex);
                throw new TranslationException(
                    "腾讯云 OCR 返回了无法解析的数据。",
                    "BAD_RESPONSE");
            }
            if (root == null)
            {
                throw new TranslationException(
                    "腾讯云 OCR 返回了空数据。", "BAD_RESPONSE");
            }

            Dictionary<string, object> response =
                GetDictionary(root, "Response");
            if (response == null)
            {
                throw new TranslationException(
                    "腾讯云 OCR 返回格式异常（缺少 Response）。",
                    "BAD_RESPONSE");
            }

            result.RequestId = GetString(response, "RequestId");

            Dictionary<string, object> error =
                GetDictionary(response, "Error");
            if (error != null)
            {
                string code = GetString(error, "Code");
                string message = GetString(error, "Message");
                throw new TranslationException(
                    DescribeError(code, message), code);
            }

            IList items = GetValue(response, "TextDetections") as IList;
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Dictionary<string, object> item =
                        items[i] as Dictionary<string, object>;
                    if (item == null)
                    {
                        continue;
                    }
                    string text = GetString(item, "DetectedText");
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    RectangleF box = ParseBox(item);
                    if (box.Width <= 0f || box.Height <= 0f)
                    {
                        continue;
                    }

                    OcrLine line = new OcrLine();
                    line.Text = text;
                    line.Box = box;
                    line.Rect = FormatRect(box);
                    line.Confidence = GetDouble(
                        GetValue(item, "Confidence"));
                    result.Lines.Add(line);
                }
            }

            SortLines(result.Lines);
            result.SumText = BuildOrderedText(result.Lines, null);
            return result;
        }

        /// <summary>
        /// 从 ItemPolygon 取包围盒；缺失时回退到 Polygon 四点求外接矩形。
        /// </summary>
        private static RectangleF ParseBox(
            Dictionary<string, object> item)
        {
            Dictionary<string, object> polygon =
                GetDictionary(item, "ItemPolygon");
            if (polygon != null)
            {
                float x = GetFloat(GetValue(polygon, "X"));
                float y = GetFloat(GetValue(polygon, "Y"));
                float w = GetFloat(GetValue(polygon, "Width"));
                float h = GetFloat(GetValue(polygon, "Height"));
                if (w > 0f && h > 0f)
                {
                    return new RectangleF(x, y, w, h);
                }
            }

            IList points = GetValue(item, "Polygon") as IList;
            if (points != null && points.Count > 0)
            {
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                for (int i = 0; i < points.Count; i++)
                {
                    Dictionary<string, object> point =
                        points[i] as Dictionary<string, object>;
                    if (point == null)
                    {
                        continue;
                    }
                    float px = GetFloat(GetValue(point, "X"));
                    float py = GetFloat(GetValue(point, "Y"));
                    minX = Math.Min(minX, px);
                    minY = Math.Min(minY, py);
                    maxX = Math.Max(maxX, px);
                    maxY = Math.Max(maxY, py);
                }
                if (maxX > minX && maxY > minY)
                {
                    return new RectangleF(
                        minX, minY, maxX - minX, maxY - minY);
                }
            }
            return RectangleF.Empty;
        }

        internal static string FormatRect(RectangleF box)
        {
            return Round(box.X) + " " + Round(box.Y) + " " +
                Round(box.Width) + " " + Round(box.Height);
        }

        private static string Round(float value)
        {
            return Math.Round(value, 1).ToString(
                "0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>阅读顺序：Top 容差内视为同一行，行内按 Left。</summary>
        internal static void SortLines(List<OcrLine> lines)
        {
            if (lines == null || lines.Count < 2)
            {
                return;
            }
            lines.Sort(delegate(OcrLine a, OcrLine b)
            {
                float tolerance =
                    Math.Max(a.Box.Height, b.Box.Height) * 0.6f;
                if (Math.Abs(a.Box.Top - b.Box.Top) > tolerance)
                {
                    return a.Box.Top.CompareTo(b.Box.Top);
                }
                return a.Box.Left.CompareTo(b.Box.Left);
            });
        }

        /// <summary>
        /// 按排版拼接文字：同一行用空格，不同行换行。
        /// translations 非空时取对应译文，否则取原文。
        /// </summary>
        internal static string BuildOrderedText(
            List<OcrLine> lines,
            List<string> translations)
        {
            StringBuilder builder = new StringBuilder();
            if (lines == null)
            {
                return "";
            }
            float previousTop = float.NaN;
            float previousHeight = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].Text;
                if (translations != null &&
                    i < translations.Count &&
                    !string.IsNullOrEmpty(translations[i]))
                {
                    text = translations[i];
                }
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }
                RectangleF r = lines[i].Box;
                if (builder.Length > 0)
                {
                    bool sameLine = !float.IsNaN(previousTop) &&
                        Math.Abs(r.Top - previousTop) <=
                        Math.Max(r.Height, previousHeight) * 0.6f;
                    builder.Append(sameLine ? " " : "\r\n");
                }
                builder.Append(text);
                previousTop = r.Top;
                previousHeight = r.Height;
            }
            return builder.ToString();
        }

        // ------------------------------------------------------------------
        // JSON / 错误码小工具
        // ------------------------------------------------------------------

        private static object GetValue(
            Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary != null &&
                dictionary.TryGetValue(key, out value))
            {
                return value;
            }
            return null;
        }

        private static Dictionary<string, object> GetDictionary(
            Dictionary<string, object> dictionary, string key)
        {
            return GetValue(dictionary, key)
                as Dictionary<string, object>;
        }

        private static string GetString(
            Dictionary<string, object> dictionary, string key)
        {
            object value = GetValue(dictionary, key);
            return value == null ? "" : Convert.ToString(value);
        }

        private static float GetFloat(object value)
        {
            if (value == null)
            {
                return 0f;
            }
            try
            {
                return Convert.ToSingle(
                    value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0f;
            }
        }

        private static double GetDouble(object value)
        {
            if (value == null)
            {
                return 0d;
            }
            try
            {
                return Convert.ToDouble(
                    value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0d;
            }
        }

        private static string DescribeError(string code, string message)
        {
            string friendly;
            switch (code)
            {
                case "AuthFailure.SignatureExpire":
                    friendly = "签名已过期，请确认本机系统时间正确。";
                    break;
                case "AuthFailure.SignatureFailure":
                case "AuthFailure.InvalidSecretId":
                case "AuthFailure.SecretIdNotFound":
                    friendly = "腾讯云签名或 SecretId 不正确，请在「设置」里检查凭证。";
                    break;
                case "AuthFailure.UnauthorizedOperation":
                case "FailedOperation.UnOpenError":
                    friendly = "腾讯云文字识别服务未开通，请在腾讯云控制台开通「文字识别 OCR」。";
                    break;
                case "ResourceUnavailable.InArrears":
                    friendly = "腾讯云账号已欠费。";
                    break;
                case "ResourceUnavailable.ResourcePackageRunOut":
                    friendly = "腾讯云 OCR 资源包已用尽。";
                    break;
                case "FailedOperation.ImageNoText":
                    friendly = "图片中没有检测到文字。";
                    break;
                case "FailedOperation.ImageBlur":
                    friendly = "图片过于模糊，无法识别。";
                    break;
                case "FailedOperation.ImageDecodeFailed":
                case "InvalidParameter.EngineImageDecodeFailed":
                    friendly = "图片解码失败，请换 PNG / JPG 格式重试。";
                    break;
                case "FailedOperation.ImageSizeTooLarge":
                case "LimitExceeded.TooLargeFileError":
                    friendly = "图片过大，请缩小截图区域后重试。";
                    break;
                case "FailedOperation.EngineRecognizeTimeout":
                    friendly = "腾讯云识别超时，请稍后重试。";
                    break;
                case "RequestLimitExceeded":
                    friendly = "请求过于频繁（默认 10 次/秒），请稍后重试。";
                    break;
                default:
                    friendly = "腾讯云 OCR 返回错误";
                    break;
            }
            if (!string.IsNullOrEmpty(code))
            {
                friendly += "（" + code + "）";
            }
            if (!string.IsNullOrEmpty(message))
            {
                friendly += "：" + message;
            }
            return friendly;
        }
    }
}
