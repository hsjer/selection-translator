using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SelectionTranslator
{
    /// <summary>一个正在下载（或已完成）的安装包任务。</summary>
    internal sealed class SoftDownload
    {
        private readonly CancellationTokenSource _cancel =
            new CancellationTokenSource();

        public SoftItem Item { get; set; }
        public string TargetPath { get; set; }
        public long Received { get; set; }
        public long TotalLength { get; set; }
        public double BytesPerSecond { get; set; }
        public bool Done { get; set; }
        public bool Failed { get; set; }
        public string Error { get; set; }

        public SoftDownload()
        {
            Error = "";
        }

        public CancellationToken Token
        {
            get { return _cancel.Token; }
        }

        public bool Canceled
        {
            get { return _cancel.IsCancellationRequested; }
        }

        public void Cancel()
        {
            try
            {
                _cancel.Cancel();
            }
            catch
            {
            }
        }

        /// <summary>0–1；总长度未知时返回 -1，界面画成滚动条。</summary>
        public double Progress
        {
            get
            {
                if (TotalLength <= 0)
                {
                    return -1;
                }
                double value = (double)Received / TotalLength;
                if (value < 0)
                {
                    return 0;
                }
                return value > 1 ? 1 : value;
            }
        }
    }

    /// <summary>
    /// 下载安装包：直连软件源给出的安装包地址（360 的 soft_download，
    /// 或者腾讯软件中心的 https 直链），不经过任何「软件管家」，
    /// 所以不会顺带把 360 卫士之类的套装装下来。
    /// 先写 .part 再改名，取消或失败时清掉残留。
    /// </summary>
    internal static class SoftwareDownloader
    {
        private const int BufferSize = 64 * 1024;

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            // 部分 CDN（腾讯云等）只接受 TLS 1.2+，.NET 4.8 默认协议
            // 在旧系统上可能还是 TLS 1.0，这里显式补上。
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |=
                    System.Net.SecurityProtocolType.Tls12;
            }
            catch
            {
            }
            HttpClient client = new HttpClient();
            // 大文件整体下载可能很久，超时要给足；真正的卡死由用户取消。
            client.Timeout = TimeSpan.FromMinutes(60);
            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Referrer =
                    new Uri(SoftwareCatalog.SiteRoot + "/");
            }
            catch
            {
            }
            return client;
        }

        /// <summary>默认下载目录：用户「下载」文件夹，取不到时退回桌面。</summary>
        public static string DefaultDirectory()
        {
            try
            {
                string profile = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                {
                    string downloads = Path.Combine(profile, "Downloads");
                    if (Directory.Exists(downloads))
                    {
                        return downloads;
                    }
                }
            }
            catch
            {
            }
            try
            {
                string desktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(desktop) &&
                    Directory.Exists(desktop))
                {
                    return desktop;
                }
            }
            catch
            {
            }
            return ConfigStore.DataDirectory;
        }

        /// <summary>
        /// 设置里指定的保存目录；没设置或路径不可用时返回空串。
        /// 每次调用都重新读一次配置，这样设置窗口改完立即生效，
        /// 不需要重启软件下载窗口。
        /// </summary>
        public static string ConfiguredDirectory()
        {
            string configured = "";
            try
            {
                AppConfig config = ConfigStore.Load();
                if (config != null)
                {
                    configured =
                        (config.SoftwareDownloadDirectory ?? "").Trim();
                }
            }
            catch
            {
                return "";
            }
            if (configured.Length == 0)
            {
                return "";
            }
            try
            {
                // 目录不存在就建出来；建不出来（盘符没了 / 权限不足）算无效。
                Directory.CreateDirectory(configured);
                return Directory.Exists(configured) ? configured : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>真正要用的保存目录：用户设置优先，否则系统「下载」文件夹。</summary>
        public static string DownloadDirectory()
        {
            string configured = ConfiguredDirectory();
            return configured.Length > 0 ? configured : DefaultDirectory();
        }

        /// <summary>后台下载，进度回调可能来自工作线程，界面需要自己切回 UI 线程。</summary>
        public static Task<SoftDownload> StartAsync(
            SoftItem item, string directory, Action<SoftDownload> progress)
        {
            SoftDownload job = new SoftDownload();
            job.Item = item;
            return StartAsync(job, directory, progress);
        }

        /// <summary>
        /// 用调用方自己创建的任务对象下载。界面必须走这个重载：
        /// 列表里的状态是按这个对象画的，另建一个内部对象会导致
        /// 进度和完成态永远回不到界面（下载完了还显示「下载中」）。
        /// </summary>
        public static Task<SoftDownload> StartAsync(
            SoftDownload job, string directory,
            Action<SoftDownload> progress)
        {
            if (job == null)
            {
                SoftDownload empty = new SoftDownload();
                empty.Failed = true;
                empty.Error = "下载任务为空";
                return Task.FromResult(empty);
            }
            return Task.Run(delegate
            {
                Run(job, directory, progress);
                return job;
            });
        }

        private static void Run(
            SoftDownload job, string directory, Action<SoftDownload> progress)
        {
            string part = "";
            try
            {
                if (job.Item == null ||
                    string.IsNullOrEmpty(job.Item.DownloadUrl))
                {
                    job.Failed = true;
                    job.Error = "这条软件没有可用的下载地址";
                    return;
                }
                if (string.IsNullOrEmpty(directory))
                {
                    directory = DownloadDirectory();
                }
                Directory.CreateDirectory(directory);
                job.TargetPath = SoftwareCatalog.UniquePath(
                    directory,
                    SoftwareCatalog.BuildFileName(job.Item));
                part = job.TargetPath + ".part";

                using (HttpResponseMessage response = Client.GetAsync(
                    job.Item.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    job.Token).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        job.Failed = true;
                        job.Error = "下载服务器返回 " +
                            (int)response.StatusCode;
                        return;
                    }
                    if (response.Content.Headers.ContentLength.HasValue)
                    {
                        job.TotalLength =
                            response.Content.Headers.ContentLength.Value;
                    }

                    using (Stream input = response.Content
                        .ReadAsStreamAsync().Result)
                    using (FileStream output = new FileStream(
                        part, FileMode.Create, FileAccess.Write,
                        FileShare.None, BufferSize))
                    {
                        byte[] buffer = new byte[BufferSize];
                        Stopwatch watch = Stopwatch.StartNew();
                        long lastBytes = 0;
                        long lastTicks = 0;
                        while (true)
                        {
                            if (job.Token.IsCancellationRequested)
                            {
                                break;
                            }
                            int read = input.Read(buffer, 0, buffer.Length);
                            if (read <= 0)
                            {
                                break;
                            }
                            output.Write(buffer, 0, read);
                            job.Received += read;

                            // 每 300ms 报一次进度，避免把 UI 线程刷爆。
                            long now = watch.ElapsedMilliseconds;
                            if (now - lastTicks >= 300)
                            {
                                double seconds =
                                    (now - lastTicks) / 1000.0;
                                if (seconds > 0)
                                {
                                    job.BytesPerSecond =
                                        (job.Received - lastBytes) / seconds;
                                }
                                lastTicks = now;
                                lastBytes = job.Received;
                                if (progress != null)
                                {
                                    progress(job);
                                }
                            }
                        }
                    }
                }

                if (job.Token.IsCancellationRequested)
                {
                    DeleteQuietly(part);
                    job.Error = "已取消";
                    return;
                }
                if (job.Received <= 0)
                {
                    DeleteQuietly(part);
                    job.Failed = true;
                    job.Error = "下载内容为空，请稍后重试";
                    return;
                }
                File.Move(part, job.TargetPath);
                job.Done = true;
                if (progress != null)
                {
                    progress(job);
                }
            }
            catch (AggregateException ex)
            {
                DeleteQuietly(part);
                Exception inner = ex.InnerException == null
                    ? ex : ex.InnerException;
                if (job.Token.IsCancellationRequested)
                {
                    job.Error = "已取消";
                    return;
                }
                job.Failed = true;
                job.Error = inner is TaskCanceledException
                    ? "下载超时，请重试"
                    : "下载失败：" + inner.Message;
            }
            catch (Exception ex)
            {
                DeleteQuietly(part);
                if (job.Token.IsCancellationRequested)
                {
                    job.Error = "已取消";
                    return;
                }
                job.Failed = true;
                job.Error = "下载失败：" + ex.Message;
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        /// <summary>纯函数：把字节数写成 12.3 MB。</summary>
        public static string SizeText(long bytes)
        {
            if (bytes < 0)
            {
                bytes = 0;
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
            return value.ToString(
                value >= 100 ? "0" : "0.0") + " " + units[unit];
        }

        /// <summary>纯函数：速度文案，例如 2.4 MB/s。</summary>
        public static string SpeedText(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return "";
            }
            return SizeText((long)bytesPerSecond) + "/s";
        }

        /// <summary>纯函数：粗略剩余时间，未知或过快时返回空串。</summary>
        public static string RemainingText(
            long received, long total, double bytesPerSecond)
        {
            if (total <= 0 || received >= total || bytesPerSecond <= 0)
            {
                return "";
            }
            double seconds = (total - received) / bytesPerSecond;
            if (seconds < 1)
            {
                return "即将完成";
            }
            if (seconds < 60)
            {
                return "剩余 " + (int)Math.Round(seconds) + " 秒";
            }
            if (seconds < 3600)
            {
                return "剩余 " + (int)(seconds / 60) + " 分钟";
            }
            return "剩余 " + (int)(seconds / 3600) + " 小时";
        }
    }
}
