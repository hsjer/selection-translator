using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    internal enum ClipboardItemKind
    {
        Text = 0,
        Image = 1,
        File = 2
    }

    internal enum ClipboardTab
    {
        All = 0,
        Text = 1,
        Image = 2,
        File = 3,
        Favorite = 4
    }

    /// <summary>一条剪贴板历史。字段保持简单，方便 JavaScriptSerializer 直接读写。</summary>
    public sealed class ClipboardItem
    {
        public long Id { get; set; }
        public int Kind { get; set; }
        public long TicksUtc { get; set; }
        public string Text { get; set; }
        public string Fingerprint { get; set; }
        public string ImageFile { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public string[] Files { get; set; }
        public bool Favorite { get; set; }
        public int Count { get; set; }

        [ScriptIgnore]
        public DateTime Utc
        {
            get
            {
                long ticks = TicksUtc;
                if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
                {
                    return DateTime.UtcNow;
                }
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }
    }

    public sealed class ClipboardHistoryState
    {
        public List<ClipboardItem> Items { get; set; }
    }

    /// <summary>
    /// 剪贴板历史里所有与窗口无关的纯逻辑：分类、搜索、按日期排序、
    /// 相对时间、TTL 过期判定。自检直接调用这里，不需要真实剪贴板。
    /// </summary>
    internal static class ClipboardLogic
    {
        public static readonly int[] TtlChoices =
            new int[] { 1, 6, 24, 72, 168, 720, 0 };

        public static readonly string[] TtlLabels =
            new string[] { "1 小时", "6 小时", "1 天", "3 天", "7 天", "30 天", "永久" };

        public const int DefaultTtlHours = 168;
        public const int DefaultMaxItems = 300;
        public const int MaxTextChars = 100000;

        public static string KindLabel(int kind)
        {
            if (kind == (int)ClipboardItemKind.Image)
            {
                return "图像";
            }
            if (kind == (int)ClipboardItemKind.File)
            {
                return "文件";
            }
            return "文本";
        }

        public static string TabLabel(ClipboardTab tab)
        {
            switch (tab)
            {
                case ClipboardTab.Text: return "文本";
                case ClipboardTab.Image: return "图像";
                case ClipboardTab.File: return "文件";
                case ClipboardTab.Favorite: return "收藏";
                default: return "全部";
            }
        }

        public static bool IsFavorite(ClipboardItem item)
        {
            return item != null && item.Favorite;
        }

        /// <summary>TTL 只允许下拉里的档位，异常值一律回到默认 7 天。</summary>
        public static int NormalizeTtl(int hours)
        {
            for (int i = 0; i < TtlChoices.Length; i++)
            {
                if (TtlChoices[i] == hours)
                {
                    return hours;
                }
            }
            return DefaultTtlHours;
        }

        public static int TtlIndex(int hours)
        {
            int normalized = NormalizeTtl(hours);
            for (int i = 0; i < TtlChoices.Length; i++)
            {
                if (TtlChoices[i] == normalized)
                {
                    return i;
                }
            }
            return 4;
        }

        public static string TtlLabel(int hours)
        {
            return TtlLabels[TtlIndex(hours)];
        }

        public static int ClampMaxItems(int value)
        {
            if (value < 20)
            {
                return 20;
            }
            if (value > 2000)
            {
                return 2000;
            }
            return value;
        }

        /// <summary>收藏的条目不受 TTL 与条数上限影响。</summary>
        public static bool IsExpired(
            ClipboardItem item, int ttlHours, DateTime nowUtc)
        {
            if (item == null || item.Favorite || ttlHours <= 0)
            {
                return false;
            }
            return (nowUtc - item.Utc).TotalHours > ttlHours;
        }

        /// <summary>按「内容指纹」判断重复复制，避免同一段文字刷屏。</summary>
        public static bool SameContent(ClipboardItem a, ClipboardItem b)
        {
            if (a == null || b == null || a.Kind != b.Kind)
            {
                return false;
            }
            string left = a.Fingerprint ?? "";
            string right = b.Fingerprint ?? "";
            if (left.Length == 0 || right.Length == 0)
            {
                return false;
            }
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        public static string FingerprintFor(
            int kind, string text, string[] files, string imageHash)
        {
            if (kind == (int)ClipboardItemKind.File)
            {
                if (files == null)
                {
                    return "";
                }
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < files.Length; i++)
                {
                    builder.Append((files[i] ?? "").ToLowerInvariant());
                    builder.Append('|');
                }
                return builder.ToString();
            }
            if (kind == (int)ClipboardItemKind.Image)
            {
                return imageHash ?? "";
            }
            return text ?? "";
        }

        /// <summary>多关键词按空格分，全部命中才算匹配（大小写不敏感）。</summary>
        public static bool Matches(ClipboardItem item, string query)
        {
            if (item == null)
            {
                return false;
            }
            string trimmed = (query ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }
            string haystack = SearchText(item);
            string[] words = trimmed.Split(new char[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (haystack.IndexOf(
                        words[i], StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>搜索范围：文本、文件名、图像尺寸、类型名、日期。</summary>
        public static string SearchText(ClipboardItem item)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(KindLabel(item.Kind));
            builder.Append(' ');
            builder.Append(item.Text ?? "");
            builder.Append(' ');
            if (item.Files != null)
            {
                for (int i = 0; i < item.Files.Length; i++)
                {
                    builder.Append(item.Files[i] ?? "");
                    builder.Append(' ');
                }
            }
            if (item.Kind == (int)ClipboardItemKind.Image)
            {
                builder.Append(item.ImageWidth);
                builder.Append('x');
                builder.Append(item.ImageHeight);
                builder.Append(' ');
            }
            try
            {
                builder.Append(item.Utc.ToLocalTime().ToString(
                    "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }
            catch
            {
            }
            return builder.ToString();
        }

        /// <summary>分类 + 关键词过滤，再按复制时间倒序（最新在最上面）。</summary>
        public static List<ClipboardItem> Filter(
            IList<ClipboardItem> items, ClipboardTab tab, string query)
        {
            List<ClipboardItem> result = new List<ClipboardItem>();
            if (items == null)
            {
                return result;
            }
            for (int i = 0; i < items.Count; i++)
            {
                ClipboardItem item = items[i];
                if (item == null)
                {
                    continue;
                }
                if (tab == ClipboardTab.Favorite && !item.Favorite)
                {
                    continue;
                }
                if (tab != ClipboardTab.All &&
                    tab != ClipboardTab.Favorite &&
                    // Tab 的 Text=1/Image=2/File=3，Kind 从 0 起，差 1。
                    item.Kind != ((int)tab - 1))
                {
                    continue;
                }
                if (!Matches(item, query))
                {
                    continue;
                }
                result.Add(item);
            }
            result.Sort(delegate(ClipboardItem a, ClipboardItem b)
            {
                return b.TicksUtc.CompareTo(a.TicksUtc);
            });
            return result;
        }

        /// <summary>
        /// 挑出该清掉的条目：TTL 过期的，以及非收藏条目超出条数上限的。
        /// 纯函数，返回待删列表，由 Store 负责删文件。
        /// </summary>
        public static List<ClipboardItem> SelectExpired(
            IList<ClipboardItem> items, int ttlHours,
            int maxItems, DateTime nowUtc)
        {
            List<ClipboardItem> expired = new List<ClipboardItem>();
            if (items == null)
            {
                return expired;
            }
            List<ClipboardItem> ordered = new List<ClipboardItem>();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    ordered.Add(items[i]);
                }
            }
            ordered.Sort(delegate(ClipboardItem a, ClipboardItem b)
            {
                return b.TicksUtc.CompareTo(a.TicksUtc);
            });

            int kept = 0;
            int limit = ClampMaxItems(maxItems);
            for (int i = 0; i < ordered.Count; i++)
            {
                ClipboardItem item = ordered[i];
                if (item.Favorite)
                {
                    continue;
                }
                kept++;
                if (IsExpired(item, ttlHours, nowUtc) || kept > limit)
                {
                    if (!expired.Contains(item))
                    {
                        expired.Add(item);
                    }
                }
            }
            return expired;
        }

        /// <summary>列表里的单行摘要（换行压成空格）。</summary>
        public static string Preview(ClipboardItem item, int maxChars)
        {
            if (item == null)
            {
                return "";
            }
            if (item.Kind == (int)ClipboardItemKind.Image)
            {
                return item.ImageWidth + " × " + item.ImageHeight + " 图像";
            }
            if (item.Kind == (int)ClipboardItemKind.File)
            {
                int count = item.Files == null ? 0 : item.Files.Length;
                string first = count > 0
                    ? Path.GetFileName(item.Files[0]) : "";
                return first + (count > 1 ? " 等 " + count + " 个文件" : "");
            }
            string text = (item.Text ?? "").Replace('\r', ' ')
                .Replace('\n', ' ').Replace('\t', ' ').Trim();
            if (maxChars > 0 && text.Length > maxChars)
            {
                text = text.Substring(0, maxChars) + "…";
            }
            return text;
        }

        public static string FormatRelative(DateTime local, DateTime nowLocal)
        {
            double seconds = (nowLocal - local).TotalSeconds;
            if (seconds < 0)
            {
                seconds = 0;
            }
            if (seconds < 60)
            {
                return "刚刚";
            }
            if (seconds < 3600)
            {
                return ((int)(seconds / 60)) + " 分钟前";
            }
            if (local.Date == nowLocal.Date)
            {
                return ((int)(seconds / 3600)) + " 小时前";
            }
            if (local.Date == nowLocal.Date.AddDays(-1))
            {
                return "昨天 " + local.ToString("HH:mm");
            }
            return local.ToString("M月d日 HH:mm");
        }

        /// <summary>列表按日期分组时显示的标题。</summary>
        public static string DateGroup(DateTime local, DateTime nowLocal)
        {
            if (local.Date == nowLocal.Date)
            {
                return "今天";
            }
            if (local.Date == nowLocal.Date.AddDays(-1))
            {
                return "昨天";
            }
            if (local.Year == nowLocal.Year)
            {
                return local.ToString("M月d日");
            }
            return local.ToString("yyyy年M月d日");
        }

        public static string FormatCount(int count)
        {
            return count < 2 ? "1" : count.ToString(
                CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// clipboard.json + clipboard-images\*.png 的读写。和 ConfigStore 一样
    /// 用 tmp + Replace 原子落盘，绝不因为一次写失败毁掉整个历史。
    /// </summary>
    internal sealed class ClipboardStore
    {
        private readonly string _path;
        private readonly string _imageDirectory;
        private long _nextId = 1;

        public readonly List<ClipboardItem> Items =
            new List<ClipboardItem>();

        public ClipboardStore(string path, string imageDirectory)
        {
            _path = path;
            _imageDirectory = imageDirectory;
        }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    ConfigStore.DataDirectory, "clipboard.json");
            }
        }

        public static string DefaultImageDirectory
        {
            get
            {
                return Path.Combine(
                    ConfigStore.DataDirectory, "clipboard-images");
            }
        }

        public string ImageDirectory
        {
            get { return _imageDirectory; }
        }

        public static ClipboardStore Load()
        {
            return Load(DefaultPath, DefaultImageDirectory);
        }

        public static ClipboardStore Load(string path, string imageDirectory)
        {
            ClipboardStore store = new ClipboardStore(
                path, imageDirectory);
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    JavaScriptSerializer serializer = NewSerializer();
                    ClipboardHistoryState state =
                        serializer.Deserialize<ClipboardHistoryState>(json);
                    if (state != null && state.Items != null)
                    {
                        for (int i = 0; i < state.Items.Count; i++)
                        {
                            ClipboardItem item = state.Items[i];
                            if (item != null)
                            {
                                store.Items.Add(item);
                                if (item.Id >= store._nextId)
                                {
                                    store._nextId = item.Id + 1;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取剪贴板历史失败", ex);
            }
            return store;
        }

        private static JavaScriptSerializer NewSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            // 默认 2MB 不够放长文本历史。
            serializer.MaxJsonLength = 16 * 1024 * 1024;
            return serializer;
        }

        public void Save()
        {
            try
            {
                ClipboardHistoryState state =
                    new ClipboardHistoryState();
                state.Items = Items;
                string json = NewSerializer().Serialize(state);
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string temp = _path + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temp, _path, null);
                    }
                    catch
                    {
                        File.Copy(temp, _path, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch (Exception ex)
            {
                Log.Error("保存剪贴板历史失败", ex);
            }
        }

        public string ImageFullPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "";
            }
            return Path.Combine(_imageDirectory, fileName);
        }

        /// <summary>新建一张图片的完整路径（调用方负责写入位图）。</summary>
        public string NewImagePath()
        {
            Directory.CreateDirectory(_imageDirectory);
            string name = "clip-" +
                DateTime.UtcNow.ToString(
                    "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
            return Path.Combine(_imageDirectory, name);
        }

        public void DeleteImage(ClipboardItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.ImageFile))
            {
                return;
            }
            try
            {
                string path = ImageFullPath(item.ImageFile);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 入库。内容重复时不新增，把旧条目提到最前并累计复制次数。
        /// 返回真正留在列表里的那条。
        /// </summary>
        public ClipboardItem Add(ClipboardItem item)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (!ClipboardLogic.SameContent(Items[i], item))
                {
                    continue;
                }
                ClipboardItem existing = Items[i];
                Items.RemoveAt(i);
                existing.TicksUtc = item.TicksUtc;
                existing.Count = Math.Max(1, existing.Count) + 1;
                if (!string.IsNullOrEmpty(item.ImageFile) &&
                    item.ImageFile != existing.ImageFile)
                {
                    // 重复图片刚存的那份文件用不上了。
                    DeleteImage(item);
                }
                Items.Insert(0, existing);
                return existing;
            }
            item.Id = _nextId++;
            item.Count = 1;
            Items.Insert(0, item);
            return item;
        }

        public void Remove(ClipboardItem item)
        {
            if (item == null)
            {
                return;
            }
            if (Items.Remove(item))
            {
                DeleteImage(item);
            }
        }

        public void Clear(bool keepFavorites)
        {
            List<ClipboardItem> removed = new List<ClipboardItem>();
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (keepFavorites && Items[i].Favorite)
                {
                    continue;
                }
                removed.Add(Items[i]);
                Items.RemoveAt(i);
            }
            for (int i = 0; i < removed.Count; i++)
            {
                DeleteImage(removed[i]);
            }
        }

        /// <summary>按 TTL 与条数上限清理，返回删除条数。</summary>
        public int Purge(int ttlHours, int maxItems, DateTime nowUtc)
        {
            List<ClipboardItem> expired = ClipboardLogic.SelectExpired(
                Items, ttlHours, maxItems, nowUtc);
            for (int i = 0; i < expired.Count; i++)
            {
                Remove(expired[i]);
            }
            return expired.Count;
        }

        public int CountFavorites()
        {
            int count = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Favorite)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
