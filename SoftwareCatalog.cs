using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    /// <summary>软件搜索里的一条结果（可能来自 360 宝库，也可能来自腾讯软件中心）。</summary>
    /// <remarks>
    /// 所有文本字段都保证不为 null：两个源的字段并不一一对应（腾讯有版本号、
    /// 360 有分类），没赋值的字段如果留着 null，绘制时任何一次 .Length 都会
    /// 直接抛空引用把整个窗口打断。这里统一在 setter 里把 null 收成空串。
    /// </remarks>
    internal sealed class SoftItem
    {
        private string _source = "";
        private string _name = "";
        private string _description = "";
        private string _sizeText = "";
        private string _rating = "";
        private string _category = "";
        private string _logoUrl = "";
        private string _versionText = "";
        private string _downloadUrl = "";
        private string _safeDownloadUrl = "";
        private string _detailUrl = "";

        /// <summary>来源标记：360 / 腾讯。列表里要显示出来。</summary>
        public string Source
        {
            get { return _source; }
            set { _source = value ?? ""; }
        }

        public int SoftId { get; set; }

        public string Name
        {
            get { return _name; }
            set { _name = value ?? ""; }
        }

        public string Description
        {
            get { return _description; }
            set { _description = value ?? ""; }
        }

        public string SizeText
        {
            get { return _sizeText; }
            set { _sizeText = value ?? ""; }
        }

        public string Rating
        {
            get { return _rating; }
            set { _rating = value ?? ""; }
        }

        public string Category
        {
            get { return _category; }
            set { _category = value ?? ""; }
        }

        public string LogoUrl
        {
            get { return _logoUrl; }
            set { _logoUrl = value ?? ""; }
        }

        /// <summary>版本号，腾讯源有，360 源一般没有。</summary>
        public string VersionText
        {
            get { return _versionText; }
            set { _versionText = value ?? ""; }
        }

        /// <summary>详情页「普通下载」直链，通常就是安装包本体。</summary>
        public string DownloadUrl
        {
            get { return _downloadUrl; }
            set { _downloadUrl = value ?? ""; }
        }

        /// <summary>「安全下载」直链，会先装 360 卫士，只在用户主动选择时使用。</summary>
        public string SafeDownloadUrl
        {
            get { return _safeDownloadUrl; }
            set { _safeDownloadUrl = value ?? ""; }
        }

        public string DetailUrl
        {
            get { return _detailUrl; }
            set { _detailUrl = value ?? ""; }
        }

        /// <summary>
        /// 跨源唯一的键。两个源各自的 ID 是独立编号，直接用 ID 做主键
        /// 会让不同软件撞到一起（下载状态、图标缓存都按它索引）。
        /// </summary>
        public string Key
        {
            get
            {
                return (Source ?? "") + ":" + SoftId;
            }
        }

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
        /// <summary>不影响出结果的提示，例如「腾讯源暂时不可用」。</summary>
        public string Warning = "";
    }

    /// <summary>
    /// 软件搜索：合并 360 软件宝库和腾讯软件中心两个来源。
    /// 两边都是网页前端渲染、直接抓 HTML 拿不到结果，所以各走各的 JSON 接口：
    /// 360 用 main/v1/soft/search（soft_download 就是详情页「普通下载」的地址，
    /// safe_download 是「安全下载」，会先给装 360 卫士）；
    /// 腾讯用 luban 的 searchcgi（返回的 xmlInfo 里 &lt;https&gt; 就是安装包直链）。
    /// 两个结果直接首尾拼接，不做去重（同名软件在不同源的版本/包本来就不一样）。
    /// 解析部分全部是纯函数，便于自检直接喂样例数据。
    /// </summary>
    internal static class SoftwareCatalog
    {
        public const string Source360 = "360";
        public const string SourceTencent = "腾讯";

        public const string ApiBase =
            "https://soft-api.safe.360.cn/main/v1/soft/search";
        public const string SiteRoot = "https://baoku.360.cn";
        public const int PageSize = 50;

        public const string TencentApiBase =
            "https://luban.m.qq.com/api/public/software-manager/searchcgi";
        public const string TencentSiteRoot = "https://pc.qq.com";
        public const string TencentLogoRoot =
            "https://pc3.gtimg.com/softmgr/logo";

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

        /// <summary>同步搜索：两个源并行请求，结果首尾拼接，不做去重。</summary>
        public static SoftSearchResult Search(string keyword, int page)
        {
            SoftSearchResult result = new SoftSearchResult();
            if (string.IsNullOrEmpty(keyword))
            {
                result.Error = "请输入要搜索的软件名称";
                return result;
            }
            int index = page < 1 ? 1 : page;

            // 腾讯的搜索接口只有第一页有数据，翻页时不再重复请求。
            Task<SoftSearchResult> from360 = Task.Run(delegate
            {
                return Search360(keyword, index);
            });
            Task<SoftSearchResult> fromTencent = index <= 1
                ? Task.Run(delegate { return SearchTencent(keyword); })
                : null;

            SoftSearchResult left = Wait(from360);
            SoftSearchResult right = fromTencent == null
                ? null : Wait(fromTencent);
            return Merge(left, right);
        }

        private static SoftSearchResult Wait(Task<SoftSearchResult> task)
        {
            try
            {
                return task.Result;
            }
            catch (AggregateException ex)
            {
                SoftSearchResult failed = new SoftSearchResult();
                failed.Error = DescribeError(
                    ex.InnerException == null ? ex : ex.InnerException);
                return failed;
            }
            catch (Exception ex)
            {
                SoftSearchResult failed = new SoftSearchResult();
                failed.Error = DescribeError(ex);
                return failed;
            }
        }

        /// <summary>
        /// 纯函数：把两个源的结果拼在一起。任一侧失败只记 Warning，
        /// 另一侧照常显示；两侧都空才算整体失败。
        /// </summary>
        public static SoftSearchResult Merge(
            SoftSearchResult first, SoftSearchResult second)
        {
            SoftSearchResult merged = new SoftSearchResult();
            List<SoftSearchResult> parts = new List<SoftSearchResult>();
            if (first != null)
            {
                parts.Add(first);
            }
            if (second != null)
            {
                parts.Add(second);
            }
            List<string> warnings = new List<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                SoftSearchResult part = parts[i];
                if (part.Items.Count > 0)
                {
                    merged.Items.AddRange(part.Items);
                    merged.Total += part.Total;
                }
                if (part.Error.Length > 0)
                {
                    warnings.Add(part.Error);
                }
            }
            if (merged.Items.Count == 0)
            {
                merged.Error = warnings.Count > 0
                    ? string.Join("；", warnings.ToArray())
                    : "没有找到相关软件";
                return merged;
            }
            if (warnings.Count > 0)
            {
                merged.Warning = warnings[0];
            }
            return merged;
        }

        /// <summary>360 宝库的搜索（异步包装，供后台线程调用）。</summary>
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

        /// <summary>只查 360 源，供自检/探针单独验证。</summary>
        public static SoftSearchResult Search360(string keyword, int page)
        {
            SoftSearchResult result = new SoftSearchResult();
            string url = BuildSearchUrl(keyword, page);
            try
            {
                byte[] body = Client.GetByteArrayAsync(url).Result;
                string json = Encoding.UTF8.GetString(body);
                return Parse(json);
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

        /// <summary>只查腾讯软件中心，供自检/探针单独验证。</summary>
        public static SoftSearchResult SearchTencent(string keyword)
        {
            SoftSearchResult result = new SoftSearchResult();
            string url = BuildTencentSearchUrl(keyword, 1, PageSize);
            try
            {
                byte[] body = Client.GetByteArrayAsync(url).Result;
                string json = Encoding.UTF8.GetString(body);
                return ParseTencent(json);
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

        /// <summary>腾讯搜索接口。接口的 jsonpCallback 不传就会返回纯 JSON。</summary>
        public static string BuildTencentSearchUrl(
            string keyword, int page, int perNumber)
        {
            return TencentApiBase +
                "?type=search&keyword=" +
                Uri.EscapeDataString(keyword ?? "") +
                "&page=" + (page < 1 ? 1 : page) +
                "&pernum=" + (perNumber <= 0 ? PageSize : perNumber) +
                "&more=0";
        }

        private static string DescribeError(Exception ex)
        {
            if (ex is TaskCanceledException)
            {
                return "连接软件源超时，请稍后重试";
            }
            if (ex is HttpRequestException)
            {
                return "连不上软件源，请检查网络或代理设置";
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

        /// <summary>
        /// 纯函数：解析腾讯软件中心的搜索响应。
        /// 外层是 {"code":0,"info":{…,"total":22},"list":[{"SoftID":…,
        /// "SoftName":…,"xmlInfo":"&lt;soft&gt;…&lt;/soft&gt;"}]}，
        /// 真正有用的字段（安装包直链、体积、版本、简介）都在 xmlInfo 里。
        /// </summary>
        public static SoftSearchResult ParseTencent(string json)
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
                result.Error = "腾讯源结果格式无法识别（接口可能已调整）";
                return result;
            }

            Dictionary<string, object> root =
                rootObject as Dictionary<string, object>;
            if (root == null)
            {
                result.Error = "腾讯源结果格式无法识别（接口可能已调整）";
                return result;
            }

            int code = ToInt(Get(root, "code"));
            if (code != 0)
            {
                result.Error = "腾讯接口返回错误：" + code;
                return result;
            }

            Dictionary<string, object> info =
                Get(root, "info") as Dictionary<string, object>;
            if (info != null)
            {
                result.Total = ToInt(Get(info, "total"));
            }

            object[] list = Get(root, "list") as object[];
            if (list != null)
            {
                for (int i = 0; i < list.Length; i++)
                {
                    Dictionary<string, object> row =
                        list[i] as Dictionary<string, object>;
                    if (row == null)
                    {
                        continue;
                    }
                    SoftItem item = ReadTencentItem(row);
                    if (item != null)
                    {
                        result.Items.Add(item);
                    }
                }
            }
            if (result.Total <= 0)
            {
                result.Total = result.Items.Count;
            }
            return result;
        }

        private static SoftItem ReadTencentItem(
            Dictionary<string, object> row)
        {
            string xml = ToText(Get(row, "xmlInfo"));
            string name = Unescape(XmlText(xml, "displayname"));
            if (name.Length == 0)
            {
                name = Unescape(ToText(Get(row, "SoftName")));
            }
            if (name.Length == 0)
            {
                return null;
            }

            SoftItem item = new SoftItem();
            item.Source = SourceTencent;
            item.SoftId = ToInt(Get(row, "SoftID"));
            item.Name = name;
            item.VersionText = XmlText(xml, "versionname");
            item.SizeText = HumanSize(ToLong(XmlText(xml, "filesize")));
            item.Rating = FormatPoint(XmlText(xml, "point"));
            item.Description = FirstNonEmpty(
                XmlText(xml, "feature"),
                XmlText(xml, "whatsnewfeature"),
                XmlText(xml, "whatsnew"));
            item.Category = "";

            string logo48 = XmlText(xml, "logo48");
            string logo = XmlText(xml, "logo");
            if (logo48.Length > 0)
            {
                item.LogoUrl = TencentLogoRoot + "/48/" +
                    logo48.ToLowerInvariant();
            }
            else if (logo.Length > 0)
            {
                item.LogoUrl = TencentLogoRoot + "/" +
                    logo.ToLowerInvariant();
            }
            else
            {
                item.LogoUrl = "";
            }

            string download = XmlText(xml, "https");
            if (download.Length == 0)
            {
                download = XmlText(xml, "url");
            }
            item.DownloadUrl = download;
            item.SafeDownloadUrl = "";
            item.DetailUrl = BuildTencentDetailUrl(item.SoftId);
            return item;
        }

        /// <summary>纯函数：腾讯详情页地址，形如 /detail/11/detail_56131.html。</summary>
        public static string BuildTencentDetailUrl(int softId)
        {
            if (softId <= 0)
            {
                return "";
            }
            return TencentSiteRoot + "/detail/" + (softId % 20) +
                "/detail_" + softId + ".html";
        }

        /// <summary>
        /// 纯函数：从 xmlInfo 里取一个标签的文本，自动剥掉 CDATA 包裹。
        /// 只认 &lt;tag&gt;…&lt;/tag&gt; 这种没有属性的简单标签。
        /// </summary>
        public static string XmlText(string xml, string tag)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(tag))
            {
                return "";
            }
            string open = "<" + tag + ">";
            string close = "</" + tag + ">";
            int start = xml.IndexOf(
                open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return "";
            }
            start += open.Length;
            int end = xml.IndexOf(
                close, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return "";
            }
            string value = xml.Substring(start, end - start).Trim();
            if (value.StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                value = value.Substring(9);
                if (value.EndsWith("]]>", StringComparison.Ordinal))
                {
                    value = value.Substring(0, value.Length - 3);
                }
            }
            return value.Trim();
        }

        private static string FirstNonEmpty(
            string first, string second, string third)
        {
            string[] values = new string[] { first, second, third };
            for (int i = 0; i < values.Length; i++)
            {
                string value = (values[i] ?? "").Trim();
                if (value.Length > 0)
                {
                    return value;
                }
            }
            return "";
        }

        /// <summary>纯函数：腾讯的 point 是评分 ×10（66 → 6.6）。</summary>
        public static string FormatPoint(string point)
        {
            long value = ToLong(point);
            if (value <= 0)
            {
                return "";
            }
            return (value / 10.0).ToString("0.0");
        }

        /// <summary>纯函数：字节数写成列表里的体积文案。</summary>
        public static string HumanSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "";
            }
            double value = bytes;
            string[] units = new string[]
            {
                "B", "KB", "MB", "GB", "TB"
            };
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            if (unit == 0)
            {
                return bytes + " B";
            }
            string format = value >= 100 ? "0"
                : (value >= 10 ? "0.0" : "0.00");
            return value.ToString(format) + " " + units[unit];
        }

        private static SoftItem ReadItem(Dictionary<string, object> row)
        {
            string name = Unescape(ToText(Get(row, "softname")));
            if (name.Length == 0)
            {
                return null;
            }

            SoftItem item = new SoftItem();
            item.Source = Source360;
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

        private static long ToLong(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }
            long parsed;
            long.TryParse(value.Trim(), out parsed);
            return parsed;
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
