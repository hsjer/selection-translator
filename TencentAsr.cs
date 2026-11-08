using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    /// <summary>设置界面下拉里的一个语音识别引擎。</summary>
    public sealed class AsrEngineOption
    {
        public string Code { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>一次「一句话识别」的归一化结果。</summary>
    public class AsrResult
    {
        /// <summary>识别文本，已去掉首尾空白。</summary>
        public string Text { get; set; }

        /// <summary>服务端返回的音频时长（毫秒）。</summary>
        public int AudioDurationMs { get; set; }

        public string RequestId { get; set; }

        public AsrResult()
        {
            Text = "";
            RequestId = "";
        }
    }

    /// <summary>
    /// 腾讯云语音识别「一句话识别」SentenceRecognition 客户端。
    ///
    /// 与 TencentOcr 共用同一套云 API 3.0 TC3-HMAC-SHA256 签名算法，
    /// 区别只在 host / service（asr）/ action / version。签名实现刻意
    /// 复制了一份而没有抽公共类，避免改动已经验证通过的 OCR 链路。
    ///
    /// 文档：https://cloud.tencent.com/document/product/1093/35646
    /// 限制：音频 ≤ 60 秒，Base64 前 ≤ 3MB。
    /// </summary>
    public static class TencentAsr
    {
        public const string Endpoint = "https://asr.tencentcloudapi.com";
        public const string Host = "asr.tencentcloudapi.com";
        public const string Service = "asr";
        public const string Action = "SentenceRecognition";
        public const string Version = "2019-06-14";
        public const string DefaultEngine = "16k_zh";

        /// <summary>接口硬限制：音频数据超过 3MB 会直接被拒。</summary>
        public const int MaxAudioBytes = 3 * 1024 * 1024;

        private const string Algorithm = "TC3-HMAC-SHA256";
        private const string ContentType = "application/json; charset=utf-8";
        private const string SignedHeaders = "content-type;host";

        private static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(40);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SelectionTranslator/1.0");
            return client;
        }

        // ------------------------------------------------------------------
        // 引擎列表
        // ------------------------------------------------------------------

        /// <summary>文档里非电话场景支持的引擎，按常用程度排序。</summary>
        public static List<AsrEngineOption> BuildEngineOptions()
        {
            List<AsrEngineOption> options = new List<AsrEngineOption>();
            AddEngine(options, "16k_zh", "中文普通话（推荐）");
            AddEngine(options, "16k_zh-PY", "中文 + 英文 + 粤语");
            AddEngine(options, "16k_zh_dialect", "中文多方言（23 种）");
            AddEngine(options, "16k_zh_medical", "中文医疗");
            AddEngine(options, "16k_en", "英语");
            AddEngine(options, "16k_yue", "粤语");
            AddEngine(options, "16k_ja", "日语");
            AddEngine(options, "16k_ko", "韩语");
            AddEngine(options, "16k_vi", "越南语");
            AddEngine(options, "16k_ms", "马来语");
            AddEngine(options, "16k_id", "印度尼西亚语");
            AddEngine(options, "16k_fil", "菲律宾语");
            AddEngine(options, "16k_th", "泰语");
            AddEngine(options, "16k_pt", "葡萄牙语");
            AddEngine(options, "16k_tr", "土耳其语");
            AddEngine(options, "16k_ar", "阿拉伯语");
            AddEngine(options, "16k_es", "西班牙语");
            AddEngine(options, "16k_hi", "印地语");
            AddEngine(options, "16k_fr", "法语");
            AddEngine(options, "16k_de", "德语");
            return options;
        }

        private static void AddEngine(
            List<AsrEngineOption> options,
            string code,
            string name)
        {
            AsrEngineOption option = new AsrEngineOption();
            option.Code = code;
            option.Name = name;
            options.Add(option);
        }

        /// <summary>把配置里的引擎码归一；未知值回退到 16k_zh。</summary>
        public static string NormalizeEngine(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return DefaultEngine;
            }
            List<AsrEngineOption> options = BuildEngineOptions();
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(
                    options[i].Code,
                    code.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return options[i].Code;
                }
            }
            return DefaultEngine;
        }

        /// <summary>引擎码对应的中文说明，用于结果窗口状态行。</summary>
        public static string DescribeEngine(string code)
        {
            string normalized = NormalizeEngine(code);
            List<AsrEngineOption> options = BuildEngineOptions();
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Code == normalized)
                {
                    return normalized + " · " + options[i].Name;
                }
            }
            return normalized;
        }

        // ------------------------------------------------------------------
        // 签名
        // ------------------------------------------------------------------

        /// <summary>生成 Authorization 头；timestamp 由调用方传入以便自检。</summary>
        public static string BuildAuthorization(
            string secretId,
            string secretKey,
            string payload,
            long timestamp)
        {
            string date = Epoch.AddSeconds(timestamp)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string credentialScope = date + "/" + Service + "/tc3_request";

            string canonicalHeaders =
                "content-type:" + ContentType + "\n" +
                "host:" + Host + "\n";
            string canonicalRequest =
                "POST\n/\n\n" +
                canonicalHeaders + "\n" +
                SignedHeaders + "\n" +
                TencentOcr.Sha256Hex(payload);

            string stringToSign =
                Algorithm + "\n" +
                timestamp.ToString(CultureInfo.InvariantCulture) + "\n" +
                credentialScope + "\n" +
                TencentOcr.Sha256Hex(canonicalRequest);

            byte[] secretDate = TencentOcr.HmacSha256(
                Encoding.UTF8.GetBytes("TC3" + (secretKey ?? "")), date);
            byte[] secretService = TencentOcr.HmacSha256(
                secretDate, Service);
            byte[] secretSigning = TencentOcr.HmacSha256(
                secretService, "tc3_request");
            byte[] signature = TencentOcr.HmacSha256(
                secretSigning, stringToSign);

            return Algorithm +
                " Credential=" + (secretId ?? "") + "/" +
                credentialScope +
                ", SignedHeaders=" + SignedHeaders +
                ", Signature=" + ToHex(signature);
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

        private static long CurrentTimestamp()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        // ------------------------------------------------------------------
        // 请求
        // ------------------------------------------------------------------

        /// <summary>
        /// 识别一段 WAV。durationMs 只用于日志与结果展示，不影响请求。
        /// </summary>
        public static async Task<AsrResult> RecognizeAsync(
            AppConfig config,
            byte[] wavBytes,
            int durationMs)
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
                    "未配置腾讯云 SecretId / SecretKey。请右键托盘图标打开「设置」，在「腾讯云」里填写（语音识别与文字识别共用同一对凭证）。",
                    "NO_TENCENT_CREDENTIALS");
            }
            if (wavBytes == null || wavBytes.Length <= VoiceRecorder.HeaderBytes)
            {
                throw new TranslationException(
                    "录音内容为空。", "EMPTY_AUDIO");
            }
            if (wavBytes.Length > MaxAudioBytes)
            {
                throw new TranslationException(
                    "录音超过 3MB（约 60 秒）上限，请缩短后重试。",
                    "AUDIO_TOO_LARGE");
            }

            string engine = NormalizeEngine(config.VoiceEngineType);
            string region = string.IsNullOrEmpty(config.TencentRegion)
                ? TencentOcr.DefaultRegion : config.TencentRegion;

            Dictionary<string, object> body =
                new Dictionary<string, object>();
            body["EngSerViceType"] = engine;
            body["SourceType"] = 1;          // 1 = 直接上传语音数据
            body["VoiceFormat"] = "wav";
            body["Data"] = Convert.ToBase64String(wavBytes);
            body["DataLen"] = wavBytes.Length;
            body["WordInfo"] = 0;
            body["FilterDirty"] = 0;
            body["FilterModal"] = 0;
            body["FilterPunc"] = 0;
            body["ConvertNumMode"] = 1;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string payload = serializer.Serialize(body);

            long timestamp = CurrentTimestamp();
            string authorization = BuildAuthorization(
                secretId, secretKey, payload, timestamp);

            Log.Info("腾讯云 ASR 请求：engine=" + engine +
                " bytes=" + wavBytes.Length +
                " duration=" + durationMs + "ms region=" + region);

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
                            Log.Info("腾讯云 ASR HTTP " +
                                (int)response.StatusCode + "：" +
                                (json.Length > 500
                                    ? json.Substring(0, 500) : json));
                            throw new TranslationException(
                                "腾讯云语音识别返回 HTTP " +
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
                        "腾讯云语音识别请求超时，请检查网络后重试。",
                        "TIMEOUT");
                }
                catch (Exception ex)
                {
                    Log.Error("请求腾讯云语音识别失败", ex);
                    throw new TranslationException(
                        "无法连接腾讯云语音识别，请检查网络、代理或防火墙。",
                        "NETWORK");
                }
            }

            AsrResult result = ParseResponse(json);
            Log.Info("腾讯云 ASR 成功：chars=" + result.Text.Length +
                " audio=" + result.AudioDurationMs + "ms" +
                " requestId=" + result.RequestId);
            return result;
        }

        /// <summary>解析响应；SelfTest 会传入固定 JSON 验证。</summary>
        public static AsrResult ParseResponse(string json)
        {
            AsrResult result = new AsrResult();

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
                Log.Error("解析腾讯云 ASR 响应失败", ex);
                throw new TranslationException(
                    "腾讯云语音识别返回了无法解析的数据。",
                    "BAD_RESPONSE");
            }
            if (root == null)
            {
                throw new TranslationException(
                    "腾讯云语音识别返回了空数据。", "BAD_RESPONSE");
            }

            Dictionary<string, object> response =
                GetDictionary(root, "Response");
            if (response == null)
            {
                throw new TranslationException(
                    "腾讯云语音识别返回格式异常（缺少 Response）。",
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

            result.Text = GetString(response, "Result").Trim();
            result.AudioDurationMs = (int)GetDouble(
                GetValue(response, "AudioDuration"));
            return result;
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
                    friendly = "该密钥没有语音识别的调用权限，请到腾讯云 CAM 授权。";
                    break;
                case "FailedOperation.UserNotRegistered":
                    friendly = "语音识别服务未开通，请在腾讯云控制台开通「语音识别 ASR」后重试。";
                    break;
                case "FailedOperation.ServiceIsolate":
                    friendly = "腾讯云账号因欠费已停止服务，请充值后重试。";
                    break;
                case "FailedOperation.UserHasNoAmount":
                case "FailedOperation.UserHasNoFreeAmount":
                    friendly = "语音识别资源包已用尽，请在腾讯云控制台购买资源包或开通后付费。";
                    break;
                case "FailedOperation.ErrorRecognize":
                case "InternalError.ErrorRecognize":
                    friendly = "语音识别失败，可能是录音过于嘈杂或没有清晰的说话声。";
                    break;
                case "InvalidParameterValue.ErrorInvalidEngservice":
                    friendly = "识别引擎类型无效，请到「设置 → 语音识别」重新选择。";
                    break;
                case "InvalidParameterValue.ErrorVoicedataTooLong":
                    friendly = "音频时长超过 60 秒上限，请说短一点。";
                    break;
                case "InvalidParameterValue.ErrorInvalidVoicedata":
                    friendly = "音频数据无效，请重新录音。";
                    break;
                case "InvalidParameterValue.ErrorInvalidVoiceFormat":
                    friendly = "音频格式不被支持，请重新录音。";
                    break;
                case "InvalidParameter.ErrorParamsMissing":
                    friendly = "请求参数不全，请重启程序后重试。";
                    break;
                case "RequestLimitExceeded":
                    friendly = "请求过于频繁（默认 30 次/秒），请稍后重试。";
                    break;
                default:
                    friendly = "腾讯云语音识别返回错误";
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
