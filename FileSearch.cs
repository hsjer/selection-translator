using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SelectionTranslator
{
    /// <summary>文件大类。列表顶部的分类标签就是按它过滤的。</summary>
    internal enum FileKind
    {
        Folder = 0,
        Document = 1,
        Image = 2,
        Archive = 3,
        Application = 4,
        Video = 5,
        Audio = 6,
        Other = 7
    }

    /// <summary>
    /// 分类规则、标签文案、体积 / 时间格式化。
    /// 全是纯函数，自检可以直接喂样例断言，不需要真的碰磁盘。
    /// </summary>
    internal static class FileKindLogic
    {
        /// <summary>「全部」用 null 表示，其余对应具体分类。</summary>
        public static readonly FileKind?[] TabKinds = new FileKind?[]
        {
            null,
            FileKind.Document,
            FileKind.Image,
            FileKind.Archive,
            FileKind.Application,
            FileKind.Video,
            FileKind.Audio,
            FileKind.Other
        };

        public static readonly string[] TabLabels = new string[]
        {
            "全部", "文档", "图片", "压缩包",
            "应用程序", "视频", "音频", "其他"
        };

        private static readonly HashSet<string> Documents =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "doc", "docx", "xls", "xlsx", "xlsm", "ppt", "pptx",
                "pdf", "txt", "md", "markdown", "rtf", "csv", "tsv",
                "odt", "ods", "odp", "wps", "et", "dps", "epub",
                "mobi", "chm", "tex", "log", "json", "xml", "yml",
                "yaml", "ini", "toml", "conf", "cfg", "properties",
                "html", "htm", "css", "scss", "less", "js", "ts",
                "jsx", "tsx", "vue", "py", "java", "kt", "c", "h",
                "cpp", "hpp", "cc", "cs", "go", "rs", "php", "rb",
                "sh", "bat", "cmd", "ps1", "lua", "sql", "gradle",
                "sln", "csproj", "vcxproj", "cmake", "makefile"
            };

        private static readonly HashSet<string> Images =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "jpg", "jpeg", "jpe", "png", "gif", "bmp", "webp",
                "tif", "tiff", "ico", "svg", "psd", "ai", "raw",
                "cr2", "cr3", "nef", "arw", "heic", "avif", "dng"
            };

        private static readonly HashSet<string> Archives =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "zip", "rar", "7z", "tar", "gz", "tgz", "bz2",
                "xz", "zst", "cab", "iso", "img", "lz", "lzma",
                "jar", "war", "apk", "whl", "nupkg", "deb", "rpm"
            };

        private static readonly HashSet<string> Applications =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "exe", "msi", "msix", "appx", "com", "scr", "lnk",
                "url", "appref-ms", "dll", "ocx", "sys", "drv",
                "msc", "cpl", "psd1", "appimage"
            };

        private static readonly HashSet<string> Videos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mp4", "mkv", "avi", "mov", "wmv", "flv", "rmvb",
                "rm", "ts", "m4v", "webm", "mpg", "mpeg", "3gp",
                "vob", "m2ts", "f4v"
            };

        private static readonly HashSet<string> Audios =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mp3", "wav", "flac", "aac", "ogg", "wma", "m4a",
                "ape", "opus", "mid", "midi", "amr", "aiff", "dsf"
            };

        /// <summary>扩展名（不含点），没有扩展名时返回空串。</summary>
        public static string ExtensionOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }
            int dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1)
            {
                return "";
            }
            return name.Substring(dot + 1);
        }

        public static FileKind Classify(string name, bool isDirectory)
        {
            if (isDirectory)
            {
                return FileKind.Folder;
            }
            string ext = ExtensionOf(name);
            if (ext.Length == 0)
            {
                return FileKind.Other;
            }
            if (Documents.Contains(ext))
            {
                return FileKind.Document;
            }
            if (Images.Contains(ext))
            {
                return FileKind.Image;
            }
            if (Archives.Contains(ext))
            {
                return FileKind.Archive;
            }
            if (Applications.Contains(ext))
            {
                return FileKind.Application;
            }
            if (Videos.Contains(ext))
            {
                return FileKind.Video;
            }
            if (Audios.Contains(ext))
            {
                return FileKind.Audio;
            }
            return FileKind.Other;
        }

        public static string Label(FileKind kind)
        {
            switch (kind)
            {
                case FileKind.Folder: return "文件夹";
                case FileKind.Document: return "文档";
                case FileKind.Image: return "图片";
                case FileKind.Archive: return "压缩包";
                case FileKind.Application: return "应用程序";
                case FileKind.Video: return "视频";
                case FileKind.Audio: return "音频";
                default: return "其他";
            }
        }

        /// <summary>列表里显示的体积：文件夹不显示，其它按 KB / MB / GB。</summary>
        public static string SizeText(long bytes, FileKind kind)
        {
            if (kind == FileKind.Folder)
            {
                return "";
            }
            if (bytes < 0)
            {
                bytes = 0;
            }
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            double value = bytes / 1024.0;
            if (value < 1024)
            {
                return value.ToString("0", CultureInfo.InvariantCulture) + " KB";
            }
            value = value / 1024.0;
            if (value < 1024)
            {
                return value.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            }
            value = value / 1024.0;
            if (value < 1024)
            {
                return value.ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            }
            return (value / 1024.0).ToString(
                "0.0", CultureInfo.InvariantCulture) + " TB";
        }

        public static string DateText(long utcTicks)
        {
            if (utcTicks <= 0)
            {
                return "";
            }
            try
            {
                return new DateTime(utcTicks, DateTimeKind.Utc)
                    .ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>索引里的一条记录。目录 + 文件名分开存，省掉每条都存一整条路径。</summary>
    internal sealed class FileEntry
    {
        public int DirId;
        public string Name = "";
        public long Size;
        public long Ticks;
        public FileKind Kind;
    }

    /// <summary>整份索引：根目录、目录表、文件表。</summary>
    internal sealed class FileIndexData
    {
        public List<string> Roots = new List<string>();
        public List<string> Directories = new List<string>();
        public List<FileEntry> Entries = new List<FileEntry>();
        public DateTime SavedAt = DateTime.MinValue;

        public string FullPath(FileEntry entry)
        {
            if (entry == null)
            {
                return "";
            }
            string dir = "";
            if (entry.DirId >= 0 && entry.DirId < Directories.Count)
            {
                dir = Directories[entry.DirId];
            }
            if (dir.Length == 0)
            {
                return entry.Name;
            }
            if (dir[dir.Length - 1] == '\\' || dir[dir.Length - 1] == '/')
            {
                return dir + entry.Name;
            }
            return dir + "\\" + entry.Name;
        }
    }

    /// <summary>
    /// 索引落盘。格式是自定义的紧凑二进制（没有第三方依赖，也不能引
    /// SQLite）：头部魔数 + 版本，后面依次是根目录表、目录表、条目表。
    /// 读的时候任何异常都返回 null，让上层安静地重建，不要把界面搞崩。
    /// </summary>
    internal static class FileIndexStore
    {
        private const int Magic = 0x54465346;   // "TSFI"
        private const int Version = 1;
        private const int MaxDirectories = 4000000;
        private const int MaxEntries = 5000000;

        public static string IndexPath
        {
            get
            {
                return Path.Combine(
                    ConfigStore.DataDirectory, "fileindex.bin");
            }
        }

        public static void Save(FileIndexData data, string path)
        {
            if (data == null || string.IsNullOrEmpty(path))
            {
                return;
            }
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(
                temp, FileMode.Create, FileAccess.Write,
                FileShare.None, 65536))
            using (BinaryWriter writer = new BinaryWriter(
                stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(data.SavedAt.ToUniversalTime().Ticks);
                WriteStrings(writer, data.Roots);
                WriteStrings(writer, data.Directories);
                writer.Write(data.Entries.Count);
                for (int i = 0; i < data.Entries.Count; i++)
                {
                    FileEntry entry = data.Entries[i];
                    writer.Write(entry.DirId);
                    writer.Write(entry.Name ?? "");
                    writer.Write(entry.Size);
                    writer.Write(entry.Ticks);
                    writer.Write((byte)entry.Kind);
                }
            }
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
            File.Move(temp, path);
        }

        private static void WriteStrings(
            BinaryWriter writer, List<string> values)
        {
            writer.Write(values == null ? 0 : values.Count);
            if (values == null)
            {
                return;
            }
            for (int i = 0; i < values.Count; i++)
            {
                writer.Write(values[i] ?? "");
            }
        }

        /// <summary>
        /// 缓存文件是否还新鲜（只看修改时间，不读内容）。托盘预热时用：
        /// 缓存够新就干脆不读，省掉一次几百兆的内存峰值。
        /// </summary>
        public static bool IsFresh(string path, TimeSpan maxAge)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return false;
                }
                return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <=
                    maxAge;
            }
            catch
            {
                return false;
            }
        }

        public static FileIndexData Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }
            try
            {
                using (FileStream stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536))
                using (BinaryReader reader = new BinaryReader(
                    stream, Encoding.UTF8))
                {
                    if (reader.ReadInt32() != Magic)
                    {
                        return null;
                    }
                    if (reader.ReadInt32() != Version)
                    {
                        return null;
                    }
                    FileIndexData data = new FileIndexData();
                    long ticks = reader.ReadInt64();
                    data.SavedAt = ticks > 0
                        ? new DateTime(ticks, DateTimeKind.Utc)
                        : DateTime.MinValue;
                    data.Roots = ReadStrings(reader, MaxDirectories);
                    data.Directories =
                        ReadStrings(reader, MaxDirectories);
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaxEntries)
                    {
                        return null;
                    }
                    data.Entries = new List<FileEntry>(count);
                    for (int i = 0; i < count; i++)
                    {
                        FileEntry entry = new FileEntry();
                        entry.DirId = reader.ReadInt32();
                        entry.Name = reader.ReadString();
                        entry.Size = reader.ReadInt64();
                        entry.Ticks = reader.ReadInt64();
                        byte kind = reader.ReadByte();
                        entry.Kind = kind > (byte)FileKind.Other
                            ? FileKind.Other : (FileKind)kind;
                        data.Entries.Add(entry);
                    }
                    return data;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取文件索引失败，将重新建立", ex);
                return null;
            }
        }

        private static List<string> ReadStrings(
            BinaryReader reader, int limit)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > limit)
            {
                throw new InvalidDataException("索引字符串表长度异常");
            }
            List<string> values = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                values.Add(reader.ReadString());
            }
            return values;
        }
    }

    /// <summary>扫描进度。多线程累加，界面只读。</summary>
    internal sealed class FileScanProgress
    {
        private int _files;
        private int _directories;

        public int Files
        {
            get { return Volatile.Read(ref _files); }
        }

        public int Directories
        {
            get { return Volatile.Read(ref _directories); }
        }

        public void AddFiles(int count)
        {
            Interlocked.Add(ref _files, count);
        }

        public void AddDirectory()
        {
            Interlocked.Increment(ref _directories);
        }
    }

    /// <summary>
    /// 全盘枚举。用 DirectoryInfo.EnumerateFileSystemInfos 拿
    /// FileSystemInfo，大小/时间直接来自枚举缓冲（FindFirstFile 的
    /// WIN32_FIND_DATA），不需要每条再 stat 一次，这是能「秒建」的关键。
    /// 多线程按目录队列消费，跳过重解析点避免目录环。
    /// </summary>
    internal static class FileScanner
    {
        /// <summary>单份索引的条数上限，防止误扫到超大目录把内存吃光。</summary>
        public const int MaxEntries = 2000000;

        private static readonly string[] SkipNames = new string[]
        {
            "$RECYCLE.BIN", "System Volume Information",
            "Recovery", "PerfLogs", "Windows.old", "Config.Msi",
            "$WinREAgent", "$SysReset", "node_modules", ".git",
            ".svn", ".hg"
        };

        /// <summary>这些目录整棵树都不扫：系统目录几万文件且基本没人搜。</summary>
        private static readonly string[] SkipUnder =
            new string[]
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData)
            };

        public static bool ShouldSkipDirectory(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }
            for (int i = 0; i < SkipNames.Length; i++)
            {
                if (string.Equals(name, SkipNames[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsUnderSkippedRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }
            for (int i = 0; i < SkipUnder.Length; i++)
            {
                string root = SkipUnder[i];
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }
                if (path.Length >= root.Length &&
                    path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    bool boundary = path.Length == root.Length ||
                        path[root.Length] == '\\' ||
                        root[root.Length - 1] == '\\';
                    if (boundary)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>把整棵树的文件 / 文件夹收进 data。</summary>
        public static void Scan(
            List<string> roots, FileIndexData data,
            FileScanProgress progress, Func<bool> cancelled)
        {
            if (roots == null || roots.Count == 0 || data == null)
            {
                return;
            }
            ConcurrentQueue<string> queue =
                new ConcurrentQueue<string>();
            object dirSync = new object();
            Dictionary<string, int> dirIds =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            // 用数组当计数器：匿名线程里不能对捕获的局部变量取 ref。
            int[] pending = new int[1];

            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (string.IsNullOrEmpty(root) ||
                    !Directory.Exists(root))
                {
                    continue;
                }
                RegisterDirectory(data, dirIds, dirSync, root);
                queue.Enqueue(root);
                pending[0]++;
            }
            if (pending[0] == 0)
            {
                return;
            }

            int workers = Math.Max(1,
                Math.Min(4, Environment.ProcessorCount));
            List<Thread> threads = new List<Thread>(workers);
            for (int i = 0; i < workers; i++)
            {
                Thread thread = new Thread(delegate()
                {
                    Work(queue, data, progress, cancelled,
                        dirIds, dirSync, pending);
                });
                thread.IsBackground = true;
                thread.Priority = ThreadPriority.BelowNormal;
                thread.Name = "file-index-" + i;
                threads.Add(thread);
                thread.Start();
            }
            for (int i = 0; i < threads.Count; i++)
            {
                threads[i].Join();
            }
        }

        private static void Work(
            ConcurrentQueue<string> queue, FileIndexData data,
            FileScanProgress progress, Func<bool> cancelled,
            Dictionary<string, int> dirIds, object dirSync,
            int[] pending)
        {
            while (true)
            {
                if (cancelled != null && cancelled())
                {
                    return;
                }
                if (Volatile.Read(ref pending[0]) <= 0)
                {
                    return;
                }
                string dir;
                if (!queue.TryDequeue(out dir))
                {
                    Thread.Sleep(1);
                    continue;
                }
                int dirId;
                lock (dirSync)
                {
                    if (!dirIds.TryGetValue(dir, out dirId))
                    {
                        dirId = data.Directories.Count;
                        data.Directories.Add(dir);
                        dirIds[dir] = dirId;
                    }
                }

                List<FileEntry> local = new List<FileEntry>(64);
                List<string> children = new List<string>(8);
                try
                {
                    DirectoryInfo info = new DirectoryInfo(dir);
                    foreach (FileSystemInfo child in
                        info.EnumerateFileSystemInfos())
                    {
                        if (cancelled != null && cancelled())
                        {
                            break;
                        }
                        FileAttributes attributes;
                        try
                        {
                            attributes = child.Attributes;
                        }
                        catch
                        {
                            continue;
                        }
                        bool isDirectory =
                            (attributes & FileAttributes.Directory) != 0;
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            // 目录联接 / 符号链接：跟着走容易绕成环。
                            continue;
                        }
                        string name = child.Name;
                        if (isDirectory)
                        {
                            if (ShouldSkipDirectory(name) ||
                                IsUnderSkippedRoot(child.FullName))
                            {
                                continue;
                            }
                            children.Add(child.FullName);
                        }
                        FileEntry entry = new FileEntry();
                        entry.DirId = dirId;
                        entry.Name = name;
                        entry.Kind = FileKindLogic.Classify(
                            name, isDirectory);
                        if (!isDirectory)
                        {
                            FileInfo file = child as FileInfo;
                            if (file != null)
                            {
                                try
                                {
                                    entry.Size = file.Length;
                                    entry.Ticks =
                                        file.LastWriteTimeUtc.Ticks;
                                }
                                catch
                                {
                                }
                            }
                        }
                        else
                        {
                            try
                            {
                                entry.Ticks =
                                    child.LastWriteTimeUtc.Ticks;
                            }
                            catch
                            {
                            }
                        }
                        local.Add(entry);
                    }
                }
                catch
                {
                    // 权限不足 / 目录被删：跳过，别让整次扫描失败。
                }

                if (local.Count > 0)
                {
                    lock (data)
                    {
                        data.Entries.AddRange(local);
                    }
                    progress.AddFiles(local.Count);
                }
                progress.AddDirectory();

                if (data.Entries.Count < MaxEntries)
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        queue.Enqueue(children[i]);
                        Interlocked.Increment(ref pending[0]);
                    }
                }
                Interlocked.Decrement(ref pending[0]);
            }
        }

        private static void RegisterDirectory(
            FileIndexData data, Dictionary<string, int> dirIds,
            object dirSync, string dir)
        {
            lock (dirSync)
            {
                if (dirIds.ContainsKey(dir))
                {
                    return;
                }
                dirIds[dir] = data.Directories.Count;
                data.Directories.Add(dir);
            }
        }
    }

    /// <summary>排序键。表头上点哪一列就是它。</summary>
    internal enum FileSortKey
    {
        Name = 0,
        Path = 1,
        Date = 2,
        Size = 3
    }

    /// <summary>一次搜索请求。</summary>
    internal sealed class FileQuery
    {
        public string Keyword = "";
        public FileKind? Kind;
        public FileSortKey Sort = FileSortKey.Date;
        public bool Descending = true;
        /// <summary>最多收集多少条结果，超过就截断（总数照样精确统计）。</summary>
        public int CollectLimit = 20000;
    }

    internal sealed class FileSearchResult
    {
        public List<FileEntry> Items = new List<FileEntry>();
        public int Total;
        public bool Truncated;
        public long ElapsedMs;
        public string Error = "";
    }

    /// <summary>
    /// 在内存索引里做匹配。每条只比名字，名字没命中再看「所在目录」
    /// 是否命中（目录字符串是共享的，每个目录只算一次），所以既是
    /// 全路径语义，又不用为每条结果拼一次完整路径。
    /// </summary>
    internal static class FileQueryEngine
    {
        public static List<string> SplitTerms(string keyword)
        {
            List<string> terms = new List<string>(4);
            if (string.IsNullOrEmpty(keyword))
            {
                return terms;
            }
            string[] parts = keyword.Split(
                new char[] { ' ', '\t', '　' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string term = parts[i].Trim();
                if (term.Length > 0 && terms.Count < 6)
                {
                    terms.Add(term);
                }
            }
            return terms;
        }

        /// <summary>支持 * 和 ? 的通配符匹配（不区分大小写）。</summary>
        public static bool WildcardMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }
            if (text == null)
            {
                return false;
            }
            return WildcardMatch(
                text, 0, pattern, 0);
        }

        private static bool WildcardMatch(
            string text, int ti, string pattern, int pi)
        {
            while (pi < pattern.Length)
            {
                char pc = pattern[pi];
                if (pc == '*')
                {
                    while (pi < pattern.Length && pattern[pi] == '*')
                    {
                        pi++;
                    }
                    if (pi == pattern.Length)
                    {
                        return true;
                    }
                    for (int i = ti; i <= text.Length; i++)
                    {
                        if (WildcardMatch(text, i, pattern, pi))
                        {
                            return true;
                        }
                    }
                    return false;
                }
                if (ti >= text.Length)
                {
                    return false;
                }
                if (pc == '?')
                {
                    ti++;
                    pi++;
                    continue;
                }
                if (char.ToUpperInvariant(pc) !=
                    char.ToUpperInvariant(text[ti]))
                {
                    return false;
                }
                ti++;
                pi++;
            }
            return ti == text.Length;
        }

        public static bool HasWildcard(string term)
        {
            return !string.IsNullOrEmpty(term) &&
                (term.IndexOf('*') >= 0 || term.IndexOf('?') >= 0);
        }

        /// <summary>一次查询里预处理好的东西，Search / Refine 共用。</summary>
        private sealed class PreparedQuery
        {
            public List<string> Terms;
            public bool[] Wildcard;
            public string[] Suffixes;
            public bool[] OrdinalOnly;
            public bool[][] DirHits;
            public int DirCount;
            public int Limit;
            public FileKind? Kind;
        }

        private static PreparedQuery Prepare(
            FileIndexData data, FileQuery query)
        {
            PreparedQuery prepared = new PreparedQuery();
            prepared.Terms = SplitTerms(query.Keyword);
            int termCount = prepared.Terms.Count;
            prepared.Wildcard = new bool[termCount];
            prepared.Suffixes = new string[termCount];
            prepared.OrdinalOnly = new bool[termCount];
            prepared.DirHits = new bool[termCount][];
            prepared.DirCount = data.Directories == null
                ? 0 : data.Directories.Count;
            prepared.Limit = query.CollectLimit > 0
                ? query.CollectLimit : 20000;
            prepared.Kind = query.Kind;

            long dirBudget = 4000000L;
            for (int t = 0; t < termCount; t++)
            {
                string term = prepared.Terms[t];
                prepared.Wildcard[t] = HasWildcard(term);
                prepared.Suffixes[t] = prepared.Wildcard[t]
                    ? SuffixPattern(term) : null;
                // 纯中文 / 纯符号的词没有大小写，用 Ordinal 比
                // OrdinalIgnoreCase 快一截，而结果完全一样。
                prepared.OrdinalOnly[t] = !HasAsciiLetter(term);
                prepared.DirHits[t] = new bool[prepared.DirCount];
                if ((long)prepared.DirCount * termCount > dirBudget)
                {
                    // 目录表太大就别给每条都算一遍，退化成只比文件名。
                    continue;
                }
                for (int d = 0; d < prepared.DirCount; d++)
                {
                    string dir = data.Directories[d];
                    if (string.IsNullOrEmpty(dir))
                    {
                        continue;
                    }
                    prepared.DirHits[t][d] = prepared.Wildcard[t]
                        ? WildcardMatch(dir, term)
                        : dir.IndexOf(term,
                            StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            return prepared;
        }

        /// <summary>词里有没有 ASCII 字母（有就得按不区分大小写比）。</summary>
        public static bool HasAsciiLetter(string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return false;
            }
            for (int i = 0; i < term.Length; i++)
            {
                char c = term[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    return true;
                }
            }
            return false;
        }

        public static FileSearchResult Search(
            FileIndexData data, FileQuery query)
        {
            FileSearchResult result = new FileSearchResult();
            if (query == null)
            {
                query = new FileQuery();
            }
            if (data == null || data.Entries == null)
            {
                result.Error = "索引还没建好";
                return result;
            }
            return Run(data, data.Entries, Prepare(data, query), query);
        }

        /// <summary>
        /// 在上一次的结果里继续筛。用户接着往后打字（「报」→「报告」）时，
        /// 新结果一定是旧结果的子集，扫两万条比扫两百万条快两个数量级，
        /// 所以输入过程是「第一下等一下、后面每一下都是即时的」。
        /// 调用方负责保证子集是完整的（上次结果没被截断）。
        /// </summary>
        public static FileSearchResult Refine(
            FileIndexData data, List<FileEntry> subset, FileQuery query)
        {
            FileSearchResult result = new FileSearchResult();
            if (query == null || data == null || subset == null)
            {
                result.Error = "索引还没建好";
                return result;
            }
            return Run(data, subset, Prepare(data, query), query);
        }

        private static FileSearchResult Run(
            FileIndexData data, List<FileEntry> source,
            PreparedQuery prepared, FileQuery query)
        {
            FileSearchResult result = new FileSearchResult();
            DateTime started = DateTime.UtcNow;
            int count = source.Count;
            int limit = prepared.Limit;
            // 上百万条时单线程扫一遍要 300ms 以上，拆成几段并行，
            // 各自收各自的命中，最后合并再排序（排序结果确定）。
            int workers = count >= 150000
                ? Math.Max(1, Math.Min(4, Environment.ProcessorCount))
                : 1;
            List<FileEntry>[] parts = new List<FileEntry>[workers];
            int[] counts = new int[workers];
            if (workers == 1)
            {
                int scanned;
                parts[0] = ScanRange(source, 0, count, prepared,
                    out scanned);
                counts[0] = scanned;
            }
            else
            {
                Parallel.For(0, workers, delegate(int w)
                {
                    int from = (int)((long)count * w / workers);
                    int to = (int)((long)count * (w + 1) / workers);
                    int scanned;
                    parts[w] = ScanRange(source, from, to, prepared,
                        out scanned);
                    counts[w] = scanned;
                });
            }

            int total = 0;
            for (int w = 0; w < workers; w++)
            {
                total += counts[w];
            }
            List<FileEntry> hits = new List<FileEntry>(
                Math.Min(total, limit * workers));
            for (int w = 0; w < workers; w++)
            {
                if (parts[w] == null)
                {
                    continue;
                }
                for (int i = 0; i < parts[w].Count; i++)
                {
                    hits.Add(parts[w][i]);
                }
            }

            result.Total = total;
            result.Truncated = total > limit;
            Sort(hits, data, query.Sort, query.Descending);
            if (hits.Count > limit)
            {
                hits.RemoveRange(limit, hits.Count - limit);
            }
            result.Items = hits;
            result.ElapsedMs = (long)(DateTime.UtcNow - started)
                .TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// 扫描一段区间，返回命中条目（最多 limit 条），count 是这一段
        /// 的命中总数（可能远大于返回的条数）。
        /// </summary>
        private static List<FileEntry> ScanRange(
            List<FileEntry> entries, int from, int to,
            PreparedQuery prepared, out int count)
        {
            List<string> terms = prepared.Terms;
            int termCount = terms.Count;
            int limit = prepared.Limit;
            bool[] wildcard = prepared.Wildcard;
            string[] suffixes = prepared.Suffixes;
            bool[] ordinalOnly = prepared.OrdinalOnly;
            bool[][] dirHits = prepared.DirHits;
            int dirCount = prepared.DirCount;
            FileKind? kind = prepared.Kind;

            List<FileEntry> hits = new List<FileEntry>(
                Math.Min(limit, 1024));
            int matched = 0;
            for (int i = from; i < to; i++)
            {
                FileEntry entry = entries[i];
                if (kind.HasValue && entry.Kind != kind.Value)
                {
                    continue;
                }
                bool ok = true;
                string name = entry.Name;
                for (int t = 0; t < termCount; t++)
                {
                    bool nameHit;
                    if (suffixes[t] != null)
                    {
                        // 「*.docx」这种最常见，直接比结尾，不走通配符回溯。
                        nameHit = name.EndsWith(suffixes[t],
                            StringComparison.OrdinalIgnoreCase);
                    }
                    else if (wildcard[t])
                    {
                        nameHit = WildcardMatch(name, terms[t]);
                    }
                    else
                    {
                        nameHit = name.IndexOf(terms[t],
                            ordinalOnly[t]
                                ? StringComparison.Ordinal
                                : StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    if (!nameHit)
                    {
                        int dirId = entry.DirId;
                        if (dirId < 0 || dirId >= dirCount ||
                            !dirHits[t][dirId])
                        {
                            ok = false;
                            break;
                        }
                    }
                }
                if (!ok)
                {
                    continue;
                }
                matched++;
                if (hits.Count < limit)
                {
                    hits.Add(entry);
                }
            }
            count = matched;
            return hits;
        }

        /// <summary>
        /// 纯后缀模式「*.扩展名」返回扩展名（含点），其余返回 null。
        /// 这类查询占了实际使用的一大半，值得给它一条快路径。
        /// </summary>
        public static string SuffixPattern(string term)
        {
            if (string.IsNullOrEmpty(term) || term.Length < 2)
            {
                return null;
            }
            if (term[0] != '*' || term[1] != '.')
            {
                return null;
            }
            string suffix = term.Substring(1);
            if (suffix.IndexOf('*') >= 0 || suffix.IndexOf('?') >= 0)
            {
                return null;
            }
            return suffix;
        }

        public static void Sort(
            List<FileEntry> items, FileIndexData data,
            FileSortKey key, bool descending)
        {
            if (items == null || items.Count < 2)
            {
                return;
            }
            if (key == FileSortKey.Size)
            {
                // 文件夹没有体积，不管升序降序都排在文件后面，
                // 否则点「大小」会先看到一屏 0 字节的目录。
                items.Sort(delegate(FileEntry a, FileEntry b)
                {
                    int groupA = a.Kind == FileKind.Folder ? 1 : 0;
                    int groupB = b.Kind == FileKind.Folder ? 1 : 0;
                    if (groupA != groupB)
                    {
                        return groupA - groupB;
                    }
                    int bySize = descending
                        ? b.Size.CompareTo(a.Size)
                        : a.Size.CompareTo(b.Size);
                    if (bySize != 0)
                    {
                        return bySize;
                    }
                    return CompareName(a, b);
                });
                return;
            }
            Comparison<FileEntry> comparison = BuildComparison(
                key, data);
            if (descending)
            {
                Comparison<FileEntry> ascending = comparison;
                comparison = delegate(FileEntry a, FileEntry b)
                {
                    return ascending(b, a);
                };
            }
            items.Sort(comparison);
        }

        public static Comparison<FileEntry> BuildComparison(
            FileSortKey key, FileIndexData data)
        {
            switch (key)
            {
                case FileSortKey.Size:
                    return delegate(FileEntry a, FileEntry b)
                    {
                        int bySize = a.Size.CompareTo(b.Size);
                        if (bySize != 0)
                        {
                            return bySize;
                        }
                        return CompareName(a, b);
                    };
                case FileSortKey.Date:
                    return delegate(FileEntry a, FileEntry b)
                    {
                        int byDate = a.Ticks.CompareTo(b.Ticks);
                        if (byDate != 0)
                        {
                            return byDate;
                        }
                        return CompareName(a, b);
                    };
                case FileSortKey.Path:
                    return delegate(FileEntry a, FileEntry b)
                    {
                        string left = data == null
                            ? a.Name : data.FullPath(a);
                        string right = data == null
                            ? b.Name : data.FullPath(b);
                        int byPath = string.Compare(
                            left, right,
                            StringComparison.CurrentCultureIgnoreCase);
                        if (byPath != 0)
                        {
                            return byPath;
                        }
                        return CompareName(a, b);
                    };
                default:
                    return delegate(FileEntry a, FileEntry b)
                    {
                        int byName = CompareName(a, b);
                        if (byName != 0)
                        {
                            return byName;
                        }
                        string left = data == null
                            ? "" : data.FullPath(a);
                        string right = data == null
                            ? "" : data.FullPath(b);
                        return string.Compare(left, right,
                            StringComparison.CurrentCultureIgnoreCase);
                    };
            }
        }

        private static int CompareName(FileEntry a, FileEntry b)
        {
            return string.Compare(a.Name, b.Name,
                StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>
    /// 索引服务：进程内单例。首次打开窗口时读缓存（几十万条通常 1 秒
    /// 内读完），缓存缺失或太旧就在后台重建，建完再落盘。
    /// 扫描全程在后台线程、BelowNormal 优先级，不抢前台。
    /// </summary>
    internal sealed class FileIndexService
    {
        /// <summary>缓存超过这个时长就在后台悄悄重建一次。</summary>
        private const int StaleHours = 6;

        private static readonly FileIndexService _instance =
            new FileIndexService();

        /// <summary>窗口关掉之后再过这么久才把内存里的索引丢掉。</summary>
        private const int ReleaseIdleMinutes = 5;

        private readonly object _sync = new object();
        private readonly Timer _releaseTimer;
        private volatile FileIndexData _data;
        private FileScanProgress _progress = new FileScanProgress();
        private volatile bool _building;
        private volatile bool _working;
        private int _version;
        private string _lastError = "";
        private int _consumers;

        private FileIndexService()
        {
            _releaseTimer = new Timer(OnReleaseTimer,
                null, Timeout.Infinite, Timeout.Infinite);
        }

        public static FileIndexService Instance
        {
            get { return _instance; }
        }

        /// <summary>每次索引被替换都会 +1，界面靠它判断要不要重画。</summary>
        public int Version
        {
            get { return _version; }
        }

        public FileIndexData Data
        {
            get { return _data; }
        }

        public bool Ready
        {
            get { return _data != null; }
        }

        public bool Building
        {
            get { return _building; }
        }

        /// <summary>正在从磁盘读缓存（还没有可查的数据）。</summary>
        public bool Loading
        {
            get { return _working && _data == null && !_building; }
        }

        /// <summary>
        /// 有人要用索引（窗口打开）就 +1，关窗口就 -1。
        /// 没人用的时候过一会儿把内存里的那份丢掉——缓存文件还在，
        /// 下次打开 1 秒读回来；常驻托盘时不该白占两百兆。
        /// </summary>
        public void AddConsumer()
        {
            Interlocked.Increment(ref _consumers);
            _releaseTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void RemoveConsumer()
        {
            if (Interlocked.Decrement(ref _consumers) <= 0)
            {
                _releaseTimer.Change(
                    ReleaseIdleMinutes * 60 * 1000, Timeout.Infinite);
            }
        }

        private void OnReleaseTimer(object state)
        {
            if (Volatile.Read(ref _consumers) > 0)
            {
                return;
            }
            _data = null;
        }

        /// <summary>立刻放弃内存里的索引（自检用，验证释放后还能重新读回来）。</summary>
        public void ReleaseNow()
        {
            _data = null;
        }

        public int Count
        {
            get
            {
                FileIndexData data = _data;
                return data == null ? 0 : data.Entries.Count;
            }
        }

        public int ScannedFiles
        {
            get { return _progress.Files; }
        }

        public int ScannedDirectories
        {
            get { return _progress.Directories; }
        }

        public DateTime IndexedAt
        {
            get
            {
                FileIndexData data = _data;
                return data == null ? DateTime.MinValue : data.SavedAt;
            }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        /// <summary>
        /// 保证有一份可查的索引：有内存版直接返回；没有就读缓存，
        /// 缓存缺失或太旧再重建。重复调用是安全的。窗口打开、托盘预热、
        /// 释放之后重新打开都会走这里。
        /// </summary>
        public void EnsureLoaded()
        {
            lock (_sync)
            {
                if (_working || _data != null)
                {
                    return;
                }
                _working = true;
            }
            StartWorker(delegate
            {
                try
                {
                    LoadAndMaybeRebuild();
                }
                finally
                {
                    _working = false;
                    ReleaseIfIdle();
                }
            }, "file-index-load");
        }

        /// <summary>用户点「重建索引」：丢掉缓存重新扫一遍。</summary>
        public void Rebuild()
        {
            lock (_sync)
            {
                if (_working)
                {
                    return;
                }
                _working = true;
            }
            StartWorker(delegate
            {
                try
                {
                    Build();
                }
                finally
                {
                    _working = false;
                    ReleaseIfIdle();
                }
            }, "file-index-build");
        }

        private void StartWorker(ThreadStart body, string name)
        {
            Thread thread = new Thread(body);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Name = name;
            thread.Start();
        }

        private void LoadAndMaybeRebuild()
        {
            // 没人开着窗口（托盘预热）：缓存还新就什么都不做，
            // 别为了「预热」把两百兆白读进内存再丢掉。
            if (Volatile.Read(ref _consumers) <= 0 &&
                FileIndexStore.IsFresh(FileIndexStore.IndexPath,
                    TimeSpan.FromHours(StaleHours)))
            {
                return;
            }
            FileIndexData cached = FileIndexStore.Load(
                FileIndexStore.IndexPath);
            bool stale = true;
            if (cached != null && cached.Entries.Count > 0)
            {
                Publish(cached);
                stale = DateTime.UtcNow - cached.SavedAt >
                    TimeSpan.FromHours(StaleHours);
            }
            if (!stale)
            {
                return;
            }
            Build();
        }

        /// <summary>没人开着窗口就立刻放掉内存（缓存文件保留）。</summary>
        private void ReleaseIfIdle()
        {
            if (Volatile.Read(ref _consumers) > 0)
            {
                return;
            }
            _data = null;
        }

        private void Build()
        {
            _building = true;
            _progress = new FileScanProgress();
            try
            {
                List<string> roots = ResolveRoots();
                FileIndexData data = new FileIndexData();
                data.Roots = roots;
                DateTime started = DateTime.UtcNow;
                FileScanner.Scan(roots, data, _progress, null);
                data.SavedAt = DateTime.UtcNow;
                // 扫出来是空的（比如全被跳过）就别覆盖已有缓存。
                if (data.Entries.Count > 0 || _data == null)
                {
                    Publish(data);
                    try
                    {
                        FileIndexStore.Save(data,
                            FileIndexStore.IndexPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("保存文件索引失败", ex);
                    }
                }
                _lastError = "";
                Log.Info("文件索引完成：" + data.Entries.Count +
                    " 条 / " + data.Directories.Count + " 个目录，耗时 " +
                    (int)(DateTime.UtcNow - started).TotalSeconds + " 秒");
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Log.Error("建立文件索引失败", ex);
            }
            finally
            {
                _building = false;
            }
        }

        private void Publish(FileIndexData data)
        {
            if (data == null)
            {
                return;
            }
            _data = data;
            _version++;
        }

        /// <summary>
        /// 扫描范围单独存一个小文本文件（分号分隔），不塞进 AppConfig：
        /// 设置窗口保存配置时是整份覆盖的，放进去会被顺手抹掉。
        /// </summary>
        public static string ScopePath
        {
            get
            {
                return Path.Combine(
                    ConfigStore.DataDirectory, "filesearch-scope.txt");
            }
        }

        public static string ReadScope()
        {
            try
            {
                string path = ScopePath;
                if (!File.Exists(path))
                {
                    return "";
                }
                return (File.ReadAllText(path) ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        public static void WriteScope(string value)
        {
            string path = ScopePath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, value ?? "");
        }

        /// <summary>
        /// 扫描范围：范围文件里指定的目录；没配就扫所有本地固定磁盘。
        /// 系统目录在 FileScanner 里统一跳过。
        /// </summary>
        public static List<string> ResolveRoots()
        {
            List<string> roots = new List<string>();
            string configured = ReadScope();
            string[] parts = configured.Split(
                new char[] { ';', '|', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string path = parts[i].Trim();
                if (path.Length == 0)
                {
                    continue;
                }
                try
                {
                    if (Directory.Exists(path))
                    {
                        roots.Add(path);
                    }
                }
                catch
                {
                }
            }
            if (roots.Count > 0)
            {
                return roots;
            }
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        if (drives[i].DriveType != DriveType.Fixed ||
                            !drives[i].IsReady)
                        {
                            continue;
                        }
                        roots.Add(drives[i].RootDirectory.FullName);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("枚举磁盘失败", ex);
            }
            return roots;
        }

        /// <summary>在后台线程同步搜索，界面用 Task.Run 包一层即可。</summary>
        public FileSearchResult Search(FileQuery query)
        {
            FileIndexData data = _data;
            if (data == null)
            {
                FileSearchResult pending = new FileSearchResult();
                pending.Error = _building ? "正在建立索引…" : "索引还没准备好";
                return pending;
            }
            return FileQueryEngine.Search(data, query);
        }

        public Task<FileSearchResult> SearchAsync(FileQuery query)
        {
            return Task.Run(delegate
            {
                return Search(query);
            });
        }
    }
}
