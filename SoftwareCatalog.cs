using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    /// <summary>360 软件宝库里的一条搜索结果。</summary>
    internal sealed class SoftItem
    {
        public int SoftId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string SizeText { get; set; }
        public string Rating { get; set; }
        public string Category { get; set; }
        public string LogoUrl { get; set; }

        /// <summary>详情页「普通下载」直链，通常就是安装包本体。</summary>
        public string DownloadUrl { get; set; }

        /// <summary>「安全下载」直链，会先装 360 卫士，只在用户主动选择时使用。</summary>
        public string SafeDownloadUrl { get; set; }

        public string DetailUrl { get; set; }

        public string DetailPage
        {
            get { return SoftwareCatalog.AbsoluteUrl(DetailUrl); }
        }

        /// <summary>列表里用的「6.0 分」。没有评分时返回空串。</summary>
        public string RatingText
        {
            get
            {
                if (string.IsNullOrEmpty(Rating))
                {
                    return "";
                }
                return Rating + " 分";
            }
        }
    }

    internal sealed class SoftSearchResult
    {
        public int Total;
        public List<SoftItem> Items = new List<SoftItem>();
        public string Error = "";
    }

    /// <summary>
    /// 360 软件宝库的搜索接口。
    /// 网页版是前端渲染，直接抓 HTML 拿不到结果；这里用它的 JSON 接口
    /// main/v1/soft/search，字段 soft_download 正好等于详情页「普通下载」
    /// 的地址（safe_download 是「安全下载」，会先给装 360 卫士）。
    /// 解析部分全部是纯函数，便于自检直接喂样例 JSON。
    /// </summary>
    internal static class SoftwareCatalog
    {
        public const string ApiBase =
            "https://soft-api.safe.360.cn/main/v1/soft/search";
        public const string SiteRoot = "https://baoku.360.cn";
        public const int PageSize = 50;

        private const string Agent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/120.0.0.0 Safari/537.36";

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |=
                    System.Net.SecurityProtocolType.Tls12;
            }
            catch
            {
            }
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(Agent);
                client.DefaultRequestHeaders.Referrer =
                    new Uri(SiteRoot + "/");
                client.DefaultRequestHeaders.Accept.ParseAdd(
                    "application/json, text/plain, */*");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(
                    "zh-CN,zh;q=0.9");
            }
            catch
            {
            }
            return client;
        }

        /// <summary>后台线程执行搜索，结果直接返回解析好的条目。</summary>
        public static Task<SoftSearchResult> SearchAsync(
            string keyword, int page)
        {
            string word = (keyword ?? "").Trim();
            int index = page < 1 ? 1 : page;
            return Task.Run(delegate
            {
                return Search(word, index);
            });
        }

        /// <summary>取小图（软件图标）用，失败时返回的 Task 会带异常，调用方自行吞掉。</summary>
        public static Task<byte[]> GetBytesAsync(string url)
        {
            return Client.GetByteArrayAsync(url);
        }

        /// <summary>
        /// 探针用：只取响应头，确认安装包地址真的可下载，不把几十兆拉下来。
        /// 返回 HTTP 状态码文本，异常时返回 ERR:类型。
        /// </summary>
        public static string ProbeDownload(string url, out long length)
        {
            length = 0;
            if (string.IsNullOrEmpty(url))
            {
                return "EMPTY";
            }
            try
            {
                using (HttpRequestMessage request =
                    new HttpRequestMessage(HttpMethod.Head, url))
                using (HttpResponseMessage response =
                    Client.SendAsync(request).Result)
                {
                    if (response.Content.Headers.ContentLength.HasValue)
                    {
                        length =
                            response.Content.Headers.ContentLength.Value;
                    }
                    return ((int)response.StatusCode).ToString();
                }
            }
            catch (Exception ex)
            {
                return "ERR:" + ex.GetType().Name;
            }
        }

        /// <summary>同步搜索，供探针直接调用。</summary>
        public static SoftSearchResult Search(string keyword, int page)
        {
            SoftSearchResult result = new SoftSearchResult();
            if (string.IsNullOrEmpty(keyword))
            {
                result.Error = "请输入要搜索的软件名称";
                return result;
            }
            string url = BuildSearchUrl(keyword, page);
            try
            {
                byte[] body = Client.GetByteArrayAsync(url).Result;
                string json = Encoding.UTF8.GetString(body);
                result = Parse(json);
                return result;
            }
            catch (AggregateException ex)
            {
                result.Error = DescribeError(
                    ex.InnerException == null ? ex : ex.InnerException);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = DescribeError(ex);
                return result;
            }
        }

        public static string BuildSearchUrl(string keyword, int page)
        {
            return ApiBase +
                "?keyword=" + Uri.EscapeDataString(keyword ?? "") +
                "&page=" + (page < 1 ? 1 : page);
        }

        private static string DescribeError(Exception ex)
        {
            if (ex is TaskCanceledException)
            {
                return "连接 360 软件宝库超时，请稍后重试";
            }
            if (ex is HttpRequestException)
            {
                return "连不上 360 软件宝库，请检查网络或代理设置";
            }
            return "搜索失败：" + ex.Message;
        }

        /// <summary>
        /// 纯函数：把接口返回的 JSON 解析成条目列表。
        /// 字段缺失、类型异常都只会跳过该条，不会抛异常。
        /// </summary>
        public static SoftSearchResult Parse(string json)
        {
            SoftSearchResult result = new SoftSearchResult();
            if (string.IsNullOrEmpty(json))
            {
                result.Error = "接口没有返回内容";
                return result;
            }

            object rootObject;
            try
            {
                JavaScriptSerializer serializer =
                    new JavaScriptSerializer();
                serializer.MaxJsonLength = 8 * 1024 * 1024;
                rootObject = serializer.DeserializeObject(json);
            }
            catch
            {
                result.Error = "搜索结果格式无法识别（接口可能已调整）";
                return result;
            }

            Dictionary<string, object> root =
                rootObject as Dictionary<string, object>;
            if (root == null)
            {
                result.Error = "搜索结果格式无法识别（接口可能已调整）";
                return result;
            }

            int errno = ToInt(Get(root, "errno"));
            if (errno != 0)
            {
                string message = ToText(Get(root, "errmsg"));
                result.Error = message.Length > 0
                    ? "360 接口返回错误：" + message
                    : "360 接口返回错误：" + errno;
                return result;
            }

            Dictionary<string, object> data =
                Get(root, "data") as Dictionary<string, object>;
            if (data == null)
            {
                result.Error = "搜索结果里没有 data 字段";
                return result;
            }

            result.Total = ToInt(Get(data, "total"));
            object[] list = Get(data, "list") as object[];
            if (list == null)
            {
                return result;
            }

            for (int i = 0; i < list.Length; i++)
            {
                Dictionary<string, object> row =
                    list[i] as Dictionary<string, object>;
                if (row == null)
                {
                    continue;
                }
                SoftItem item = ReadItem(row);
                if (item != null)
                {
                    result.Items.Add(item);
                }
            }
            if (result.Total <= 0)
            {
                result.Total = result.Items.Count;
            }
            return result;
        }

        private static SoftItem ReadItem(Dictionary<string, object> row)
        {
            string name = Unescape(ToText(Get(row, "softname")));
            if (name.Length == 0)
            {
                return null;
            }

            SoftItem item = new SoftItem();
            item.SoftId = ToInt(Get(row, "softid"));
            item.Name = name;
            item.Description = Unescape(
                ToText(Get(row, "desc")));
            item.SizeText = ToText(Get(row, "size"));
            item.Rating = ToText(Get(row, "poll"));
            item.Category = ToText(Get(row, "cname"));
            item.LogoUrl = AbsoluteUrl(ToText(Get(row, "logo")));
            item.DownloadUrl = AbsoluteUrl(
                ToText(Get(row, "soft_download")));
            item.SafeDownloadUrl = AbsoluteUrl(
                ToText(Get(row, "safe_download")));
            item.DetailUrl = ToText(Get(row, "url"));
            return item;
        }

        /// <summary>把 //p4.ssl... 这类协议相对地址补成 https。</summary>
        public static string AbsoluteUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return "";
            }
            string value = url.Trim();
            if (value.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + value;
            }
            if (value.StartsWith("/", StringComparison.Ordinal))
            {
                return SiteRoot + value;
            }
            return value;
        }

        /// <summary>纯函数：从下载地址里取出原始文件名（去掉查询串）。</summary>
        public static string FileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return "";
            }
            string path = url;
            int query = path.IndexOf('?');
            if (query >= 0)
            {
                path = path.Substring(0, query);
            }
            int slash = path.LastIndexOf('/');
            if (slash >= 0)
            {
                path = path.Substring(slash + 1);
            }
            try
            {
                path = Uri.UnescapeDataString(path);
            }
            catch
            {
            }
            return path;
        }

        /// <summary>纯函数：去掉 Windows 文件名里的非法字符。</summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }
            StringBuilder builder = new StringBuilder(name.Length);
            char[] bad = new char[]
            {
                '\\', '/', ':', '*', '?', '"', '<', '>', '|',
                '\r', '\n', '\t'
            };
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < 32 || Array.IndexOf(bad, c) >= 0)
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(c);
                }
            }
            string result = builder.ToString().Trim().TrimEnd('.');
            if (result.Length > 80)
            {
                result = result.Substring(0, 80).TrimEnd();
            }
            return result;
        }

        /// <summary>
        /// 纯函数：给下载的安装包起名，形如「小红书手机版_2000004652.exe」。
        /// 扩展名优先跟下载地址一致，取不到时按 exe 处理。
        /// </summary>
        public static string BuildFileName(SoftItem item)
        {
            string extension = ".exe";
            string original = FileNameFromUrl(
                item == null ? "" : item.DownloadUrl);
            int dot = original.LastIndexOf('.');
            if (dot > 0 && original.Length - dot <= 6)
            {
                extension = original.Substring(dot);
            }
            string name = SanitizeFileName(
                item == null ? "" : item.Name);
            if (name.Length == 0)
            {
                name = "软件";
            }
            int id = item == null ? 0 : item.SoftId;
            return name + (id > 0 ? "_" + id : "") + extension;
        }

        /// <summary>纯函数：目标文件已存在时追加 (1)、(2)……</summary>
        public static string UniquePath(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                return path;
            }
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(
                    directory, baseName + "(" + i + ")" + extension);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return Path.Combine(
                directory,
                baseName + "_" +
                DateTime.Now.ToString("HHmmss") + extension);
        }

        private static object Get(
            Dictionary<string, object> dict, string key)
        {
            if (dict == null || key == null)
            {
                return null;
            }
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private static string ToText(object value)
        {
            if (value == null)
            {
                return "";
            }
            return Convert.ToString(value);
        }

        private static int ToInt(object value)
        {
            if (value == null)
            {
                return 0;
            }
            int parsed;
            if (value is int)
            {
                return (int)value;
            }
            if (int.TryParse(Convert.ToString(value), out parsed))
            {
                return parsed;
            }
            return 0;
        }

        /// <summary>接口里的简介偶尔带 HTML 实体，列表里要显示成正常字符。</summary>
        public static string Unescape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text
                .Replace("&amp;", "&")
                .Replace("&quot;", "\"")
                .Replace("&#39;", "'")
                .Replace("&apos;", "'")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&nbsp;", " ");
        }

        /// <summary>纯函数：把一段多行简介压成单行并截断。</summary>
        public static string OneLine(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            string line = text.Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");
            while (line.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                line = line.Replace("  ", " ");
            }
            line = line.Trim();
            if (maxChars > 0 && line.Length > maxChars)
            {
                line = line.Substring(0, maxChars) + "…";
            }
            return line;
        }
    }
}
