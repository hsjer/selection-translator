using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    public class TranslationResult
    {
        public string Text { get; set; }
        public string From { get; set; }
        public string To { get; set; }
    }

    public class TranslationException : Exception
    {
        public string Code { get; private set; }

        public TranslationException(string message, string code)
            : base(message)
        {
            Code = code ?? "";
        }
    }

    /// <summary>
    /// 逐行翻译结果。Lines 与输入行一一对应，供「腾讯云 OCR → 百度文本翻译」
    /// 管道把译文贴回每个文字块。
    /// </summary>
    public class LineTranslationResult
    {
        public List<string> Lines { get; set; }
        public string From { get; set; }
        public string To { get; set; }

        public LineTranslationResult()
        {
            Lines = new List<string>();
            From = "";
            To = "";
        }
    }

    public static class BaiduTranslator
    {
        private const string Endpoint =
            "https://fanyi-api.baidu.com/api/trans/vip/translate";

        // 百度通用翻译接口单次请求的 UTF-8 字节上限。
        private const int MaxByteLength = 6000;

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SelectionTranslator/1.0");
            return client;
        }

        public static string BuildSign(
            string appId,
            string query,
            string salt,
            string secret)
        {
            return Md5Hex(
                (appId ?? "") +
                (query ?? "") +
                (salt ?? "") +
                (secret ?? ""));
        }

        public static bool ContainsCjk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u4e00' && c <= '\u9fff') ||
                    (c >= '\u3400' && c <= '\u4dbf') ||
                    (c >= '\uf900' && c <= '\ufaff'))
                {
                    return true;
                }
            }
            return false;
        }

        public static string ResolveTarget(string text, string configured)
        {
            if (string.IsNullOrEmpty(configured) ||
                string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return ContainsCjk(text) ? "en" : "zh";
            }
            return configured;
        }

        public static async Task<TranslationResult> TranslateAsync(
            AppConfig config,
            string text)
        {
            return await TranslateAsync(config, text, null, null);
        }

        /// <summary>
        /// 指定源语言 / 目标语言翻译。fromLanguage 传 null 或 "auto"
        /// 表示自动检测；toLanguage 传 null 时按配置里的目标语言走
        /// （配置为 auto 时仍按「中文→英文，其他→中文」自动定向）。
        /// </summary>
        public static async Task<TranslationResult> TranslateAsync(
            AppConfig config,
            string text,
            string fromLanguage,
            string toLanguage)
        {
            if (config == null)
            {
                throw new TranslationException("配置为空", "NO_CONFIG");
            }

            string appId = (config.AppId ?? "").Trim();
            string secret = config.GetSecret();
            if (appId.Length == 0 || secret.Length == 0)
            {
                throw new TranslationException(
                    "未配置百度翻译 AppID 或密钥。请右键托盘图标，打开「设置」填写。",
                    "NO_CREDENTIALS");
            }

            string query = (text ?? "").Trim();
            if (query.Length == 0)
            {
                throw new TranslationException("没有可翻译的文本", "EMPTY_TEXT");
            }

            // 未超过单次接口上限时直接翻译，行为与原来完全一致。
            int byteCount = Encoding.UTF8.GetByteCount(query);
            if (byteCount <= MaxByteLength)
            {
                return await TranslateChunk(
                    config, query, fromLanguage, toLanguage);
            }

            // 超过上限时自动分段翻译，避免直接报错中断。
            // 每段独立请求，最后用换行拼接为完整译文。
            List<string> chunks = SplitForTranslation(query);
            StringBuilder combined = new StringBuilder();
            string from = "";
            string to = "";
            int succeeded = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                TranslationResult part = await TranslateChunk(
                    config, chunks[i], fromLanguage, toLanguage);
                if (succeeded == 0)
                {
                    from = part.From;
                    to = part.To;
                }
                if (combined.Length > 0)
                {
                    combined.AppendLine();
                }
                combined.Append(part.Text);
                succeeded++;
            }

            if (succeeded == 0)
            {
                throw new TranslationException(
                    "百度翻译没有返回任何翻译结果。",
                    "NO_RESULT");
            }

            TranslationResult result = new TranslationResult();
            result.Text = combined.ToString();
            result.From = from;
            result.To = to;
            return result;
        }

        /// <summary>
        /// 逐行翻译。百度文本翻译会把 q 里的换行拆成多条 trans_result，
        /// 顺序与输入行一致，因此整张图的 OCR 文字通常一次请求即可翻完。
        /// 超过 6000 字节时自动分批，最终仍按输入行数一一对齐返回。
        /// </summary>
        public static async Task<LineTranslationResult> TranslateLinesAsync(
            AppConfig config,
            List<string> lines,
            string targetLanguage)
        {
            if (config == null)
            {
                throw new TranslationException("配置为空", "NO_CONFIG");
            }
            if (lines == null || lines.Count == 0)
            {
                return new LineTranslationResult();
            }
            string appId = (config.AppId ?? "").Trim();
            string secret = config.GetSecret();
            if (appId.Length == 0 || secret.Length == 0)
            {
                throw new TranslationException(
                    "未配置百度翻译 AppID 或密钥。请右键托盘图标，打开「设置」填写。",
                    "NO_CREDENTIALS");
            }

            // 展开：过长的单行先切成多段，各自记住归属的原始行号。
            List<LineSlot> slots = ExpandLines(lines);
            List<List<LineSlot>> batches =
                SplitSlots(slots, MaxByteLength - 64);

            List<string> translatedSlots = new List<string>();
            string from = "";
            string to = "";
            for (int i = 0; i < batches.Count; i++)
            {
                LineBatchResult batchResult =
                    await TranslateLineBatchAsync(
                        config, batches[i], targetLanguage);
                translatedSlots.AddRange(batchResult.Lines);
                if (from.Length == 0)
                {
                    from = batchResult.From;
                    to = batchResult.To;
                }
            }

            // 按归属行号把多段译文拼回去，保证与输入行数严格一致。
            LineTranslationResult result = new LineTranslationResult();
            result.From = from;
            result.To = to;
            for (int i = 0; i < lines.Count; i++)
            {
                StringBuilder builder = new StringBuilder();
                for (int j = 0; j < slots.Count; j++)
                {
                    if (slots[j].Owner != i ||
                        j >= translatedSlots.Count)
                    {
                        continue;
                    }
                    string part = translatedSlots[j] ?? "";
                    if (part.Length == 0)
                    {
                        continue;
                    }
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }
                    builder.Append(part);
                }
                result.Lines.Add(builder.ToString());
            }
            return result;
        }

        private sealed class LineSlot
        {
            public string Text;
            public int Owner;
        }

        private sealed class LineBatchResult
        {
            public List<string> Lines = new List<string>();
            public string From = "";
            public string To = "";
        }

        private static List<LineSlot> ExpandLines(List<string> lines)
        {
            List<LineSlot> slots = new List<LineSlot>();
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i] ?? "";
                if (Encoding.UTF8.GetByteCount(text) >
                    MaxByteLength - 64)
                {
                    List<string> pieces = SplitForTranslation(
                        text, MaxByteLength - 64);
                    for (int j = 0; j < pieces.Count; j++)
                    {
                        LineSlot piece = new LineSlot();
                        piece.Text = pieces[j];
                        piece.Owner = i;
                        slots.Add(piece);
                    }
                }
                else
                {
                    LineSlot slot = new LineSlot();
                    slot.Text = text;
                    slot.Owner = i;
                    slots.Add(slot);
                }
            }
            return slots;
        }

        /// <summary>按换行拼接后的 UTF-8 字节上限分批。</summary>
        private static List<List<LineSlot>> SplitSlots(
            List<LineSlot> slots, int maxBytes)
        {
            List<List<LineSlot>> batches =
                new List<List<LineSlot>>();
            List<LineSlot> current = new List<LineSlot>();
            int bytes = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                int size = Encoding.UTF8.GetByteCount(
                    slots[i].Text ?? "") + 1;
                if (current.Count > 0 &&
                    bytes + size > maxBytes)
                {
                    batches.Add(current);
                    current = new List<LineSlot>();
                    bytes = 0;
                }
                current.Add(slots[i]);
                bytes += size;
            }
            if (current.Count > 0)
            {
                batches.Add(current);
            }
            return batches;
        }

        private static async Task<LineBatchResult> TranslateLineBatchAsync(
            AppConfig config,
            List<LineSlot> batch,
            string targetLanguage)
        {
            string[] texts = new string[batch.Count];
            for (int i = 0; i < batch.Count; i++)
            {
                texts[i] = batch[i].Text ?? "";
            }
            string query = string.Join("\n", texts);
            string target = ResolveTarget(query, targetLanguage);

            string appId = (config.AppId ?? "").Trim();
            string secret = config.GetSecret();
            string salt = DateTime.UtcNow.Ticks.ToString();
            string sign = BuildSign(appId, query, salt, secret);

            Dictionary<string, string> form =
                new Dictionary<string, string>();
            form["q"] = query;
            form["from"] = "auto";
            form["to"] = target;
            form["appid"] = appId;
            form["salt"] = salt;
            form["sign"] = sign;

            string json = await PostFormAsync(form);

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
                Log.Error("解析百度逐行翻译响应失败", ex);
                throw new TranslationException(
                    "百度翻译返回了无法解析的数据。", "BAD_RESPONSE");
            }
            if (root == null)
            {
                throw new TranslationException(
                    "百度翻译返回了空数据。", "BAD_RESPONSE");
            }

            object errorCodeObject;
            if (root.TryGetValue("error_code", out errorCodeObject))
            {
                string errorMessage = "";
                object errorMessageObject;
                if (root.TryGetValue(
                    "error_msg", out errorMessageObject))
                {
                    errorMessage = Convert.ToString(
                        errorMessageObject);
                }
                throw new TranslationException(
                    DescribeError(
                        Convert.ToString(errorCodeObject),
                        errorMessage),
                    Convert.ToString(errorCodeObject));
            }

            LineBatchResult result = new LineBatchResult();
            result.From = GetString(root, "from");
            result.To = GetString(root, "to");
            if (result.To.Length == 0)
            {
                result.To = target;
            }

            object transResultObject;
            System.Collections.IList items = null;
            if (root.TryGetValue(
                "trans_result", out transResultObject))
            {
                items = transResultObject
                    as System.Collections.IList;
            }
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Dictionary<string, object> row =
                        items[i] as Dictionary<string, object>;
                    string dst = "";
                    if (row != null)
                    {
                        object dstObject;
                        if (row.TryGetValue(
                            "dst", out dstObject))
                        {
                            dst = Convert.ToString(dstObject);
                        }
                    }
                    result.Lines.Add(dst ?? "");
                }
            }

            // 接口偶尔会把多行合并成一条；此时保证输出槽位数与输入一致，
            // 让结果窗口至少能展示整段译文，不至于整体错位。
            if (result.Lines.Count == 0)
            {
                result.Lines.Add(query);
            }
            while (result.Lines.Count < batch.Count)
            {
                result.Lines.Add("");
            }
            if (result.Lines.Count > batch.Count)
            {
                result.Lines.RemoveRange(
                    batch.Count, result.Lines.Count - batch.Count);
            }
            return result;
        }

        /// <summary>
        /// 翻译单段文本（不超过接口字节上限）。
        /// </summary>
        private static async Task<TranslationResult> TranslateChunk(
            AppConfig config,
            string query)
        {
            return await TranslateChunk(config, query, null, null);
        }

        /// <summary>
        /// 翻译单段文本，可指定源语言 / 目标语言（null 表示沿用原逻辑）。
        /// </summary>
        private static async Task<TranslationResult> TranslateChunk(
            AppConfig config,
            string query,
            string fromLanguage,
            string toLanguage)
        {
            string appId = (config.AppId ?? "").Trim();
            string secret = config.GetSecret();
            string source = string.IsNullOrEmpty(fromLanguage) ||
                string.Equals(fromLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                ? "auto"
                : fromLanguage;
            string target;
            if (string.IsNullOrEmpty(toLanguage))
            {
                target = ResolveTarget(query, config.TargetLanguage);
            }
            else if (string.Equals(
                toLanguage, "auto", StringComparison.OrdinalIgnoreCase))
            {
                target = ResolveTarget(query, "auto");
            }
            else
            {
                target = toLanguage;
            }
            string salt = DateTime.UtcNow.Ticks.ToString();
            string sign = BuildSign(appId, query, salt, secret);

            Dictionary<string, string> form =
                new Dictionary<string, string>();
            form["q"] = query;
            form["from"] = source;
            form["to"] = target;
            form["appid"] = appId;
            form["salt"] = salt;
            form["sign"] = sign;

            string json = await PostFormAsync(form);

            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                root = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                Log.Error("解析百度翻译响应失败", ex);
                throw new TranslationException(
                    "百度翻译返回了无法解析的数据。",
                    "BAD_RESPONSE");
            }

            if (root == null)
            {
                throw new TranslationException(
                    "百度翻译返回了空数据。",
                    "BAD_RESPONSE");
            }

            object errorCodeObject;
            if (root.TryGetValue("error_code", out errorCodeObject))
            {
                string errorCode = Convert.ToString(errorCodeObject);
                string errorMessage = "";
                object errorMessageObject;
                if (root.TryGetValue("error_msg", out errorMessageObject))
                {
                    errorMessage = Convert.ToString(errorMessageObject);
                }
                throw new TranslationException(
                    DescribeError(errorCode, errorMessage),
                    errorCode);
            }

            object transResultObject;
            if (!root.TryGetValue("trans_result", out transResultObject))
            {
                Log.Info("百度响应缺少 trans_result，原始响应：" +
                    (json.Length > 800 ? json.Substring(0, 800) : json));
                throw new TranslationException(
                    "百度翻译没有返回翻译结果。",
                    "NO_RESULT");
            }

            // JavaScriptSerializer 的泛型反序列化会把 JSON 数组解析成
            // ArrayList（而不是 object[]），这里统一按 IList 处理。
            System.Collections.IList items =
                transResultObject as System.Collections.IList;
            if (items == null || items.Count == 0)
            {
                throw new TranslationException(
                    "百度翻译没有返回翻译结果。",
                    "NO_RESULT");
            }

            StringBuilder builder = new StringBuilder();
            foreach (object item in items)
            {
                Dictionary<string, object> row =
                    item as Dictionary<string, object>;
                if (row == null)
                {
                    continue;
                }

                object dstObject;
                if (!row.TryGetValue("dst", out dstObject))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.Append(Convert.ToString(dstObject));
            }

            if (builder.Length == 0)
            {
                throw new TranslationException(
                    "百度翻译返回的译文为空。",
                    "EMPTY_RESULT");
            }

            TranslationResult result = new TranslationResult();
            result.Text = builder.ToString();
            result.From = GetString(root, "from");
            result.To = GetString(root, "to");
            if (result.To.Length == 0)
            {
                result.To = target;
            }
            return result;
        }

        /// <summary>
        /// 按 UTF-8 字节上限把长文本拆成多段，每段尽量在换行或句末标点处断开，
        /// 保证不在多字节字符中间截断。返回的段拼接后近似还原原文。
        /// </summary>
        private static List<string> SplitForTranslation(string text)
        {
            return SplitForTranslation(text, MaxByteLength);
        }

        internal static List<string> SplitForTranslation(
            string text,
            int maxBytes)
        {
            List<string> chunks = new List<string>();
            int length = text.Length;
            int start = 0;

            while (start < length)
            {
                // 找到最远的字符下标，使 [start, end) 的 UTF-8 字节数不超过上限。
                int end = start;
                int bytes = 0;
                while (end < length)
                {
                    int charBytes = Encoding.UTF8.GetByteCount(
                        text[end].ToString());
                    if (bytes + charBytes > maxBytes)
                    {
                        break;
                    }
                    bytes += charBytes;
                    end++;
                }
                if (end == start)
                {
                    // 单个字符就超过上限（合法文本不会发生，兜底防死循环）。
                    end = start + 1;
                }

                // 优先在自然断点（换行或句末标点）处结束，兼顾可读性。
                int breakPoint = FindBreakPoint(text, start, end);
                if (breakPoint > start)
                {
                    end = breakPoint;
                }

                chunks.Add(text.Substring(start, end - start));
                start = end;
            }

            return chunks;
        }

        /// <summary>
        /// 在 [start, end) 的后半段里寻找自然断点，返回断点后的下标。
        /// 找不到则返回 -1。
        /// </summary>
        private static int FindBreakPoint(string text, int start, int end)
        {
            int searchStart = start + (end - start) * 6 / 10;
            for (int i = end - 1; i >= searchStart; i--)
            {
                char c = text[i];
                if (c == '\n' || c == '\r')
                {
                    return i + 1;
                }
                if (c == '。' || c == '！' || c == '？' ||
                    c == '.' || c == '!' || c == '?')
                {
                    return i + 1;
                }
            }
            return -1;
        }

        private static async Task<string> PostFormAsync(
            Dictionary<string, string> form)
        {
            Exception lastError = null;
            bool timedOut = false;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (FormUrlEncodedContent content =
                        new FormUrlEncodedContent(form))
                    using (HttpResponseMessage response =
                        await Client.PostAsync(Endpoint, content))
                    {
                        string json =
                            await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new TranslationException(
                                string.Format(
                                    "百度翻译接口返回 HTTP {0}",
                                    (int)response.StatusCode),
                                "HTTP_" + (int)response.StatusCode);
                        }
                        return json;
                    }
                }
                catch (TranslationException)
                {
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
                        "请求百度翻译超时，请检查网络后重试。",
                        "TIMEOUT");
                }

                Log.Error("请求百度翻译失败", lastError);
                string detail = lastError == null ||
                    lastError.InnerException == null
                    ? ""
                    : "（" + lastError.InnerException.Message + "）";
                throw new TranslationException(
                    "网络请求失败，请检查网络、代理或防火墙后重试。" +
                    detail,
                    "NETWORK");
            }

            throw new TranslationException(
                "网络请求失败，请稍后重试。",
                "NETWORK");
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

        private static string DescribeError(string code, string message)
        {
            string friendly;
            switch (code)
            {
                case "52001":
                    friendly = "请求超时，请重试。";
                    break;
                case "52002":
                    friendly = "百度翻译系统错误，请稍后重试。";
                    break;
                case "52003":
                    friendly = "未授权用户：请检查 AppID 是否正确，或确认服务已开通。";
                    break;
                case "54000":
                    friendly = "缺少必填参数。";
                    break;
                case "54001":
                    friendly = "签名错误：请检查密钥是否填写正确。";
                    break;
                case "54003":
                    friendly = "访问频率受限，请稍后重试。";
                    break;
                case "54004":
                    friendly = "账户余额不足。";
                    break;
                case "54005":
                    friendly = "长文本请求过于频繁，请分段翻译。";
                    break;
                case "58000":
                    friendly = "客户端 IP 非法。";
                    break;
                case "58001":
                    friendly = "译文语言方向不支持。";
                    break;
                case "58002":
                    friendly = "服务当前已关闭。";
                    break;
                case "90107":
                    friendly = "认证未通过或未生效。";
                    break;
                default:
                    friendly = "百度翻译返回错误。";
                    break;
            }

            if (!string.IsNullOrEmpty(message))
            {
                friendly += "（" + message + "）";
            }
            return friendly;
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
