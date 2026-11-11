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
    /// <summary>
    /// 条目表：平行数组 + 一个名字字符池。
    ///
    /// 早先是「每条一个 FileEntry 对象 + 一个 string 名字」：对象头 16 B +
    /// 字段 33 B + string 头 24 B + 名字字符 + 列表槽 8 B，一条约 139 B，
    /// 155 万条就是 205 MB。改成平行数组之后每条固定只占
    ///   目录 4 + 名字起点 4 + 名字长度 2 + 大小 8 + 时间 8 + 类别 1 + 标记 1 = 28 B
    /// 再加名字字符本身（平均 19 字 ≈ 38 B），合计约 66 B，正好一半。
    ///
    /// 顺带两个好处：连续数组对 CPU 缓存友好，全盘搜索不用再追 155 万个
    /// 堆对象；名字搜索可以直接在字符池上比，不分配字符串。
    /// </summary>
    internal sealed class FileEntryTable
    {
        private int[] _dirId = new int[4096];
        private int[] _nameStart = new int[4096];
        private ushort[] _nameLen = new ushort[4096];
        private long[] _size = new long[4096];
        private long[] _ticks = new long[4096];
        private byte[] _kind = new byte[4096];
        private byte[] _flags = new byte[4096];   // 1 = 大小 / 时间还没补
        private char[] _pool = new char[1 << 16];
        private int _poolUsed;
        private int _count;

        public int Count
        {
            get { return _count; }
        }

        /// <summary>字符池里真正用掉的字符数（数组尾部还有预扩的空槽）。</summary>
        public int PoolUsed
        {
            get { return _poolUsed; }
        }

        /// <summary>
        /// 从本地缓存整块灌回来。数组已经按精确条数分配好，不需要 Grow，
        /// 也不做逐条 Add——那两百万次调用会把「1 秒读回」拖成十几秒。
        /// </summary>
        public void LoadDirect(int count, int poolUsed, char[] pool,
            int[] dirId, int[] nameStart, ushort[] nameLen,
            long[] size, long[] ticks, byte[] kind, byte[] flags)
        {
            _pool = pool;
            _poolUsed = poolUsed;
            _dirId = dirId;
            _nameStart = nameStart;
            _nameLen = nameLen;
            _size = size;
            _ticks = ticks;
            _kind = kind;
            _flags = flags;
            Thread.VolatileWrite(ref _count, count);
        }

        // ---- 只读给「整卷扫描」用：把数组一次抓出来，循环里不做属性调用 ----
        public char[] Pool
        {
            get { return _pool; }
        }

        public int[] DirIds
        {
            get { return _dirId; }
        }

        public int[] NameStarts
        {
            get { return _nameStart; }
        }

        public ushort[] NameLengths
        {
            get { return _nameLen; }
        }

        public long[] Sizes
        {
            get { return _size; }
        }

        public long[] TicksArray
        {
            get { return _ticks; }
        }

        public byte[] Kinds
        {
            get { return _kind; }
        }

        public byte[] Flags
        {
            get { return _flags; }
        }

        public void Add(int dirId, string name, long size, long ticks,
            FileKind kind, bool metaMissing)
        {
            if (_count >= _dirId.Length)
            {
                Grow(_dirId.Length * 2);
            }
            int len = name == null ? 0 : name.Length;
            if (len > ushort.MaxValue)
            {
                len = ushort.MaxValue;
            }
            if (_poolUsed + len > _pool.Length)
            {
                int want = _pool.Length;
                while (want < _poolUsed + len)
                {
                    want *= 2;
                }
                char[] bigger = new char[want];
                Array.Copy(_pool, bigger, _poolUsed);
                _pool = bigger;
            }
            if (len > 0)
            {
                name.CopyTo(0, _pool, _poolUsed, len);
            }
            _dirId[_count] = dirId;
            _nameStart[_count] = _poolUsed;
            _nameLen[_count] = (ushort)len;
            _size[_count] = size;
            _ticks[_count] = ticks;
            _kind[_count] = (byte)kind;
            _flags[_count] = metaMissing ? (byte)1 : (byte)0;
            _poolUsed += len;
            // 最后才写 Count：读的一方是先抓数组、再看 Count，这个顺序能
            // 保证它永远不会读到还没写满的槽位。
            Thread.VolatileWrite(ref _count, _count + 1);
        }

        public void AddRange(List<FileEntryDraft> drafts)
        {
            if (drafts == null)
            {
                return;
            }
            for (int i = 0; i < drafts.Count; i++)
            {
                FileEntryDraft draft = drafts[i];
                Add(draft.DirId, draft.Name, draft.Size, draft.Ticks,
                    draft.Kind, draft.MetaMissing);
            }
        }

        /// <summary>清空（MFT 扫到一半失败时要把已经写进去的丢掉重来）。</summary>
        public void Clear()
        {
            Thread.VolatileWrite(ref _count, 0);
            _poolUsed = 0;
        }

        /// <summary>
        /// 扫完之后把「按倍数预扩」多出来的容量收掉。155 万条时数组容量
        /// 是 209 万，光是这些空槽就有近二十兆。
        /// </summary>
        public void TrimExcess()
        {
            int n = _count;
            if (n <= 0)
            {
                return;
            }
            if (_dirId.Length > n)
            {
                _dirId = CopyOf(_dirId, n);
                _nameStart = CopyOf(_nameStart, n);
                _nameLen = CopyOf(_nameLen, n);
                _size = CopyOf(_size, n);
                _ticks = CopyOf(_ticks, n);
                _kind = CopyOf(_kind, n);
                _flags = CopyOf(_flags, n);
            }
            if (_pool.Length > _poolUsed)
            {
                char[] pool = new char[_poolUsed];
                Array.Copy(_pool, pool, _poolUsed);
                _pool = pool;
            }
        }

        private static int[] CopyOf(int[] source, int length)
        {
            int[] copy = new int[length];
            Array.Copy(source, copy, length);
            return copy;
        }

        private static ushort[] CopyOf(ushort[] source, int length)
        {
            ushort[] copy = new ushort[length];
            Array.Copy(source, copy, length);
            return copy;
        }

        private static long[] CopyOf(long[] source, int length)
        {
            long[] copy = new long[length];
            Array.Copy(source, copy, length);
            return copy;
        }

        private static byte[] CopyOf(byte[] source, int length)
        {
            byte[] copy = new byte[length];
            Array.Copy(source, copy, length);
            return copy;
        }

        private void Grow(int capacity)
        {
            int[] dirId = new int[capacity];
            Array.Copy(_dirId, dirId, _count);
            _dirId = dirId;

            int[] nameStart = new int[capacity];
            Array.Copy(_nameStart, nameStart, _count);
            _nameStart = nameStart;

            ushort[] nameLen = new ushort[capacity];
            Array.Copy(_nameLen, nameLen, _count);
            _nameLen = nameLen;

            long[] size = new long[capacity];
            Array.Copy(_size, size, _count);
            _size = size;

            long[] ticks = new long[capacity];
            Array.Copy(_ticks, ticks, _count);
            _ticks = ticks;

            byte[] kind = new byte[capacity];
            Array.Copy(_kind, kind, _count);
            _kind = kind;

            byte[] flags = new byte[capacity];
            Array.Copy(_flags, flags, _count);
            _flags = flags;
        }

        public string Name(int index)
        {
            if ((uint)index >= (uint)_count)
            {
                return "";
            }
            int len = _nameLen[index];
            return len == 0 ? ""
                : new string(_pool, _nameStart[index], len);
        }

        public int DirId(int index)
        {
            return (uint)index < (uint)_count ? _dirId[index] : -1;
        }

        public long Size(int index)
        {
            return (uint)index < (uint)_count ? _size[index] : 0;
        }

        public long Ticks(int index)
        {
            return (uint)index < (uint)_count ? _ticks[index] : 0;
        }

        public FileKind Kind(int index)
        {
            return (uint)index < (uint)_count
                ? (FileKind)_kind[index] : FileKind.Other;
        }

        public bool MetaMissing(int index)
        {
            return (uint)index < (uint)_count && _flags[index] != 0;
        }

        /// <summary>把 stat 回来的大小 / 时间写进去，并清掉「未知」标记。</summary>
        public void SetMeta(int index, long size, long ticks)
        {
            if ((uint)index >= (uint)_count)
            {
                return;
            }
            _size[index] = size;
            _ticks[index] = ticks;
            _flags[index] = 0;
        }
    }

    /// <summary>
    /// 造条目用的临时草稿（目录扫描时按目录攒一批再整批入库）。
    /// </summary>
    internal struct FileEntryDraft
    {
        public int DirId;
        public string Name;
        public long Size;
        public long Ticks;
        public FileKind Kind;
        public bool MetaMissing;
    }

    /// <summary>
    /// 条目视图：只有「属于哪份索引 + 下标」16 字节，真正的数据在
    /// <see cref="FileEntryTable"/> 的平行数组里。界面和查询照旧写
    /// entry.Name / entry.Size，不用关心底层怎么存。
    /// </summary>
    internal struct FileEntry
    {
        private readonly FileIndexData _owner;
        private readonly int _index;

        public FileEntry(FileIndexData owner, int index)
        {
            _owner = owner;
            _index = index;
        }

        public bool IsValid
        {
            get
            {
                return _owner != null && _index >= 0 &&
                    _index < _owner.Entries.Count;
            }
        }

        public int Index
        {
            get { return _index; }
        }

        public FileIndexData Owner
        {
            get { return _owner; }
        }

        public string Name
        {
            get { return IsValid ? _owner.Entries.Name(_index) : ""; }
        }

        public int DirId
        {
            get { return IsValid ? _owner.Entries.DirId(_index) : -1; }
        }

        public long Size
        {
            get { return IsValid ? _owner.Entries.Size(_index) : 0; }
        }

        public long Ticks
        {
            get { return IsValid ? _owner.Entries.Ticks(_index) : 0; }
        }

        public FileKind Kind
        {
            get
            {
                return IsValid
                    ? _owner.Entries.Kind(_index) : FileKind.Other;
            }
        }

        public bool MetaMissing
        {
            get { return IsValid && _owner.Entries.MetaMissing(_index); }
        }

        /// <summary>完整路径。用条目自己所属的那份索引拼，避免界面拿着
        /// 新索引去查旧快照的下标。</summary>
        public string FullPath
        {
            get { return IsValid ? _owner.FullPath(_index) : ""; }
        }
    }

    /// <summary>索引是怎么来的：MFT 秒建 还是 目录遍历。</summary>
    internal enum FileIndexSource
    {
        Directory = 0,
        Mft = 1
    }

    /// <summary>整份索引：根目录、目录表、文件表。</summary>
    internal sealed class FileIndexData
    {
        public List<string> Roots = new List<string>();
        public List<string> Directories = new List<string>();
        public FileEntryTable Entries = new FileEntryTable();
        public DateTime SavedAt = DateTime.MinValue;
        public FileIndexSource Source = FileIndexSource.Directory;
        /// <summary>这份数据是从本地缓存读回来的（不是刚扫出来的）。</summary>
        public bool FromCache;

        /// <summary>
        /// 扫描是边扫边发布的：后台线程往表里加，界面线程要能随时拿一份
        /// 快照显示「先搜到一部分」。两张表的增删都走这把锁。
        /// </summary>
        internal readonly object Sync = new object();

        /// <summary>
        /// 把某一条的大小 / 修改时间补上。MFT 索引里这两项一开始是
        /// 未知的，等真正要显示（或按日期/大小排序）时才 stat 一次，
        /// 避免建索引时对几百万个文件各做一次系统调用。
        /// </summary>
        public bool EnsureMeta(FileEntry entry)
        {
            if (!entry.IsValid || !entry.MetaMissing)
            {
                return false;
            }
            FileEntryTable table = entry.Owner.Entries;
            string full = entry.FullPath;
            long size;
            long ticks;
            try
            {
                if (entry.Kind == FileKind.Folder)
                {
                    DirectoryInfo dir = new DirectoryInfo(full);
                    ticks = dir.LastWriteTimeUtc.Ticks;
                    size = 0;
                }
                else
                {
                    FileInfo file = new FileInfo(full);
                    ticks = file.LastWriteTimeUtc.Ticks;
                    size = file.Length;
                }
            }
            catch
            {
                // 文件已经不在 / 没权限：留空，不要反复重试。
                ticks = 0;
                size = 0;
            }
            table.SetMeta(entry.Index, size, ticks);
            return true;
        }

        /// <summary>
        /// 批量补齐大小 / 时间。按日期或大小排序要看全量结果，一条一条
        /// stat 五万次要六七秒；每条之间没有任何共享状态，直接按核数
        /// 并行，通常能压到一秒以内。
        /// </summary>
        public void FillMeta(List<FileEntry> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }
            int pending = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].IsValid && items[i].MetaMissing)
                {
                    pending++;
                }
            }
            if (pending == 0)
            {
                return;
            }
            int workers = Environment.ProcessorCount;
            if (workers > 8)
            {
                workers = 8;
            }
            if (pending < 256 || workers <= 1)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    EnsureMeta(items[i]);
                }
                return;
            }
            // 分片比 Parallel.For 更稳：单条 stat 偶尔会卡在慢盘上，
            // 固定分片不会因为某一片卡住就整体等在那里。
            int chunk = (items.Count + workers - 1) / workers;
            Thread[] threads = new Thread[workers];
            for (int w = 0; w < workers; w++)
            {
                int from = w * chunk;
                int to = Math.Min(items.Count, from + chunk);
                threads[w] = new Thread(delegate()
                {
                    for (int i = from; i < to; i++)
                    {
                        EnsureMeta(items[i]);
                    }
                });
                threads[w].IsBackground = true;
                threads[w].Priority = ThreadPriority.BelowNormal;
                threads[w].Start();
            }
            for (int w = 0; w < workers; w++)
            {
                threads[w].Join();
            }
        }

        public string FullPath(int index)
        {
            if (index < 0 || index >= Entries.Count)
            {
                return "";
            }
            string name = Entries.Name(index);
            string dir = "";
            int dirId = Entries.DirId(index);
            if (dirId >= 0 && dirId < Directories.Count)
            {
                dir = Directories[dirId];
            }
            if (dir.Length == 0)
            {
                return name;
            }
            if (dir[dir.Length - 1] == '\\' || dir[dir.Length - 1] == '/')
            {
                return dir + name;
            }
            return dir + "\\" + name;
        }

        /// <summary>
        /// 完整路径。走条目自己所属的那份索引——界面万一还拿着旧快照的
        /// 结果，也不会拿新索引的目录表去拼错路径。
        /// </summary>
        public string FullPath(FileEntry entry)
        {
            return entry.FullPath;
        }

        public string Name(int index)
        {
            return Entries.Name(index);
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

        public void AddDirectories(int count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _directories, count);
            }
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

        /// <summary>
        /// 边扫边发布的回调（由 FileIndexService.Build 装上）。没有它就是
        /// 纯离线扫描，扫完才有人看见结果。
        /// </summary>
        internal static Action PartialPublish;

        /// <summary>每扫出这么多条就对外发布一次快照。</summary>
        private const int PartialBatch = 30000;

        private static int _nextPublish = PartialBatch;

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
            Volatile.Write(ref _nextPublish, PartialBatch);
            ConcurrentQueue<string> queue =
                new ConcurrentQueue<string>();
            object dirSync = data.Sync;
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

                List<FileEntryDraft> local = new List<FileEntryDraft>(64);
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
                        FileEntryDraft draft = new FileEntryDraft();
                        draft.DirId = dirId;
                        draft.Name = name;
                        draft.Kind = FileKindLogic.Classify(
                            name, isDirectory);
                        if (!isDirectory)
                        {
                            FileInfo file = child as FileInfo;
                            if (file != null)
                            {
                                try
                                {
                                    draft.Size = file.Length;
                                    draft.Ticks =
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
                                draft.Ticks =
                                    child.LastWriteTimeUtc.Ticks;
                            }
                            catch
                            {
                            }
                        }
                        local.Add(draft);
                    }
                }
                catch
                {
                    // 权限不足 / 目录被删：跳过，别让整次扫描失败。
                }

                if (local.Count > 0)
                {
                    int total;
                    lock (data.Sync)
                    {
                        data.Entries.AddRange(local);
                        total = data.Entries.Count;
                    }
                    progress.AddFiles(local.Count);
                    // 边扫边发布：打开窗口一两秒就能搜到前面扫出来的部分，
                    // 不用对着「正在扫描」干等全盘（百万级要二三十秒）。
                    // 现在是「发布同一个表」而不是拷一份，成本几乎为零，
                    // 所以扫到哪儿发到哪儿，不用再限制条数。
                    Action publish = PartialPublish;
                    if (publish != null &&
                        total >= Volatile.Read(ref _nextPublish) &&
                        Interlocked.Exchange(
                            ref _nextPublish, total + PartialBatch) <= total)
                    {
                        publish();
                    }
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
            return Run(data, null, Prepare(data, query), query);
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
            int[] indices = new int[subset.Count];
            for (int i = 0; i < subset.Count; i++)
            {
                indices[i] = subset[i].Index;
            }
            return Run(data, indices, Prepare(data, query), query);
        }

        private static FileSearchResult Run(
            FileIndexData data, int[] subset,
            PreparedQuery prepared, FileQuery query)
        {
            FileSearchResult result = new FileSearchResult();
            DateTime started = DateTime.UtcNow;
            FileEntryTable table = data.Entries;
            // 先读条数、再抓数组：扫描线程可能正在往后追加（甚至刚换过
            // 更大的数组），旧数组里的内容始终完整，只是看不见后来的条目；
            // 再按数组长度夹一次，保证不越界。
            int count = table.Count;
            int[] dirIds = table.DirIds;
            int[] nameStart = table.NameStarts;
            ushort[] nameLen = table.NameLengths;
            byte[] kinds = table.Kinds;
            char[] pool = table.Pool;
            count = Math.Min(count, Math.Min(nameStart.Length, kinds.Length));
            if (subset != null)
            {
                count = subset.Length;
            }
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
                parts[0] = ScanRange(data, dirIds, nameStart, nameLen, kinds,
                    pool, subset, 0, count, prepared, out scanned);
                counts[0] = scanned;
            }
            else
            {
                Parallel.For(0, workers, delegate(int w)
                {
                    int from = (int)((long)count * w / workers);
                    int to = (int)((long)count * (w + 1) / workers);
                    int scanned;
                    parts[w] = ScanRange(data, dirIds, nameStart, nameLen,
                        kinds, pool, subset, from, to, prepared, out scanned);
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
        /// 名字直接在字符池上比，不分配字符串——否则每敲一个键就要为
        /// 155 万条各造一个 string，几百兆垃圾、GC 直接卡住界面。
        /// </summary>
        private static List<FileEntry> ScanRange(
            FileIndexData data, int[] dirIds, int[] nameStart,
            ushort[] nameLen, byte[] kinds, char[] pool,
            int[] subset, int from, int to,
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
            bool hasKind = prepared.Kind.HasValue;
            FileKind onlyKind = prepared.Kind.GetValueOrDefault();

            List<FileEntry> hits = new List<FileEntry>(
                Math.Min(limit, 1024));
            int matched = 0;
            for (int i = from; i < to; i++)
            {
                int index = subset == null ? i : subset[i];
                if (hasKind && (FileKind)kinds[index] != onlyKind)
                {
                    continue;
                }
                bool ok = true;
                int start = nameStart[index];
                int length = nameLen[index];
                int end = start + length;
                for (int t = 0; t < termCount; t++)
                {
                    bool nameHit;
                    if (suffixes[t] != null)
                    {
                        // 「*.docx」这种最常见，直接比结尾，不走通配符回溯。
                        nameHit = PoolEndsWith(pool, start, length,
                            suffixes[t]);
                    }
                    else if (wildcard[t])
                    {
                        nameHit = PoolWildcard(pool, start, end,
                            terms[t], 0);
                    }
                    else
                    {
                        nameHit = PoolContains(pool, start, length,
                            terms[t], ordinalOnly[t]);
                    }
                    if (!nameHit)
                    {
                        int dirId = dirIds[index];
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
                    hits.Add(new FileEntry(data, index));
                }
            }
            count = matched;
            return hits;
        }

        /// <summary>
        /// 字符折叠：只折 ASCII 的大小写。中文没有大小写，非 ASCII 原样
        /// 比即可——和 OrdinalIgnoreCase 的唯一差别是拉丁扩展字母这类极
        /// 少数文件名，换来的是整条搜索路径不再分配字符串。
        /// </summary>
        private static char FoldChar(char c)
        {
            if (c >= 'a' && c <= 'z')
            {
                return c;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return (char)(c + 32);
            }
            return c;
        }

        private static bool PoolContains(
            char[] pool, int start, int length, string term, bool ordinal)
        {
            int tl = term.Length;
            if (tl == 0)
            {
                return true;
            }
            if (tl > length)
            {
                return false;
            }
            int last = start + length - tl;
            if (ordinal)
            {
                for (int i = start; i <= last; i++)
                {
                    int t = 0;
                    while (t < tl && pool[i + t] == term[t])
                    {
                        t++;
                    }
                    if (t == tl)
                    {
                        return true;
                    }
                }
                return false;
            }
            for (int i = start; i <= last; i++)
            {
                int t = 0;
                while (t < tl &&
                    FoldChar(pool[i + t]) == FoldChar(term[t]))
                {
                    t++;
                }
                if (t == tl)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PoolEndsWith(
            char[] pool, int start, int length, string suffix)
        {
            int sl = suffix.Length;
            if (sl > length)
            {
                return false;
            }
            int at = start + length - sl;
            for (int i = 0; i < sl; i++)
            {
                if (FoldChar(pool[at + i]) != FoldChar(suffix[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>通配符匹配（* 任意串、? 单字符），文本取自字符池。</summary>
        private static bool PoolWildcard(
            char[] pool, int ti, int end, string pattern, int pi)
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
                    for (int i = ti; i <= end; i++)
                    {
                        if (PoolWildcard(pool, i, end, pattern, pi))
                        {
                            return true;
                        }
                    }
                    return false;
                }
                if (ti >= end)
                {
                    return false;
                }
                if (pc == '?')
                {
                    ti++;
                    pi++;
                    continue;
                }
                if (FoldChar(pc) != FoldChar(pool[ti]))
                {
                    return false;
                }
                ti++;
                pi++;
            }
            return ti == end;
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
            // 按日期 / 大小排序前先把 MFT 里未知的这两项补齐（只补参与
            // 排序的这批结果，不是整卷）。
            if (data != null &&
                (key == FileSortKey.Size || key == FileSortKey.Date))
            {
                data.FillMeta(items);
            }
            int n = items.Count;
            // 要参与比较的字符串先算一遍：以前是在比较器里现取 entry.Name
            // / 现拼完整路径，一次排序要造几十万个临时字符串。
            string[] nameKeys = new string[n];
            for (int i = 0; i < n; i++)
            {
                nameKeys[i] = items[i].Name;
            }
            string[] pathKeys = null;
            if (key == FileSortKey.Path)
            {
                pathKeys = new string[n];
                for (int i = 0; i < n; i++)
                {
                    pathKeys[i] = items[i].FullPath;
                }
            }
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
            }
            if (key == FileSortKey.Size)
            {
                // 文件夹没有体积，不管升序降序都排在文件后面，
                // 否则点「大小」会先看到一屏 0 字节的目录。
                Array.Sort(order, delegate(int a, int b)
                {
                    int groupA = items[a].Kind == FileKind.Folder ? 1 : 0;
                    int groupB = items[b].Kind == FileKind.Folder ? 1 : 0;
                    if (groupA != groupB)
                    {
                        return groupA - groupB;
                    }
                    int bySize = descending
                        ? items[b].Size.CompareTo(items[a].Size)
                        : items[a].Size.CompareTo(items[b].Size);
                    if (bySize != 0)
                    {
                        return bySize;
                    }
                    return CompareNameKeys(nameKeys, a, b);
                });
            }
            else
            {
                Array.Sort(order, delegate(int a, int b)
                {
                    int primary;
                    if (key == FileSortKey.Date)
                    {
                        primary = items[a].Ticks.CompareTo(items[b].Ticks);
                    }
                    else if (key == FileSortKey.Path)
                    {
                        primary = string.Compare(pathKeys[a], pathKeys[b],
                            StringComparison.CurrentCultureIgnoreCase);
                    }
                    else
                    {
                        primary = CompareNameKeys(nameKeys, a, b);
                    }
                    if (primary != 0)
                    {
                        return descending ? -primary : primary;
                    }
                    // 主键相等时：日期 / 路径按名字收尾；按名字排时同名文件
                    // （不同目录）再按完整路径收尾，顺序才是稳定的。
                    int tie = CompareNameKeys(nameKeys, a, b);
                    if (tie == 0 && key == FileSortKey.Name)
                    {
                        tie = string.Compare(items[a].FullPath,
                            items[b].FullPath,
                            StringComparison.CurrentCultureIgnoreCase);
                    }
                    return descending ? -tie : tie;
                });
            }
            FileEntry[] sorted = new FileEntry[n];
            for (int i = 0; i < n; i++)
            {
                sorted[i] = items[order[i]];
            }
            items.Clear();
            items.AddRange(sorted);
        }

        private static int CompareNameKeys(string[] nameKeys, int a, int b)
        {
            int byName = string.Compare(nameKeys[a], nameKeys[b],
                StringComparison.CurrentCultureIgnoreCase);
            return byName;
        }
    }

    /// <summary>
    /// 索引服务：进程内单例。**不再落盘**——打开窗口时现场读一遍
    /// （MFT 优先，没管理员权限才退回目录扫描），关掉就释放。
    /// 扫描全程在后台线程、BelowNormal 优先级，不抢前台。
    /// </summary>
    internal sealed class FileIndexService
    {
        private static readonly FileIndexService _instance =
            new FileIndexService();

        /// <summary>
        /// 老版本（自建落盘索引）留下的 `fileindex.bin` 格式不兼容，顺手
        /// 删掉。现在用的是 `filelist.cache`，别把两个混为一谈。
        /// </summary>
        static FileIndexService()
        {
            try
            {
                string legacy = Path.Combine(
                    ConfigStore.DataDirectory, "fileindex.bin");
                if (File.Exists(legacy))
                {
                    File.Delete(legacy);
                    Log.Info("已清理旧版文件索引缓存 fileindex.bin");
                }
            }
            catch (Exception ex)
            {
                Log.Error("清理旧索引文件失败", ex);
            }
        }

        /// <summary>
        /// 窗口关掉之后再过这么久才把内存里的索引丢掉。
        /// 30 秒：够容纳「关掉又想再搜一次」这种连续操作（索引还热着，
        /// 第二次秒开）；又不至于让任务管理器里一直挂着两百多兆——用户
        /// 关掉窗口一分钟后看，数字已经掉下来了。读回来 1 秒，不心疼。
        /// </summary>
        private const int ReleaseIdleSeconds = 30;

        /// <summary>
        /// 本地缓存超过这么久才在后台重扫一遍。6 小时：绝大多数打开都是
        /// 「1 秒读回、零峰值」，一天最多一两次要重新枚举 MFT。设得太短
        /// 会让每次开窗口都同时握着新旧两份索引，内存峰值又冲到几百兆。
        /// 想要最新就手动按 F5（菜单「重新读取」）。
        /// </summary>
        private const int CacheStaleHours = 6;

        private readonly object _sync = new object();
        private readonly Timer _releaseTimer;
        private volatile FileIndexData _data;
        private FileScanProgress _progress = new FileScanProgress();
        private volatile bool _building;
        private volatile bool _working;
        /// <summary>当前 _data 是不是「扫到一半」的快照（还会有更多）。</summary>
        private volatile bool _partialPublished;
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
                    ReleaseIdleSeconds * 1000, Timeout.Infinite);
            }
        }

        private void OnReleaseTimer(object state)
        {
            if (Volatile.Read(ref _consumers) > 0)
            {
                return;
            }
            DropData();
        }

        /// <summary>立刻放弃内存里的索引（自检用，验证释放后还能重新读回来）。</summary>
        public void ReleaseNow()
        {
            DropData();
        }

        /// <summary>
        /// 丢掉内存里的索引，并把内存真的还给系统。
        ///
        /// 只把引用置空是不够的：一百五十多万条记录是两百兆对象，置空
        /// 只是让它们变成垃圾，要等下一次 GC 才回收。常驻托盘的程序几
        /// 乎没有分配压力，可能几个小时都不触发一次 gen2——任务管理器
        /// 里就一直挂着两百多兆，用户看到的就是「放着不动也占 280MB」。
        /// 这里在自己的后台线程里主动回收一次，数字才真的降下去。
        /// </summary>
        private void DropData()
        {
            bool hadData = _data != null;
            _data = null;
            _partialPublished = false;
            if (hadData)
            {
                TrimHeap();
                TrimWorkingSet();
            }
        }

        /// <summary>强制一次完整回收。调用方已经脱离分配热路径，可以承受停顿。</summary>
        internal static void TrimHeap()
        {
            try
            {
                // 索引里的大块（名字字符池、平行数组）都在大对象堆上，
                // 而 LOH 默认只回收不压缩，页留着不还给系统——任务管理器
                // 里就会看到「托管已经降到 1 MB，私有还挂着六十兆」。
                // 这一次性压一下，数字才真的掉下来。
                System.Runtime.GCSettings
                    .LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode
                        .CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                System.Runtime.GCSettings
                    .LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode
                        .CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true);
            }
            catch (Exception ex)
            {
                Log.Error("回收索引内存失败", ex);
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll",
            EntryPoint = "SetProcessWorkingSetSize", SetLastError = true)]
        private static extern bool NativeSetProcessWorkingSetSize(
            IntPtr process, IntPtr minimum, IntPtr maximum);

        [System.Runtime.InteropServices.DllImport("kernel32.dll",
            EntryPoint = "GetCurrentProcess")]
        private static extern IntPtr NativeGetCurrentProcess();

        /// <summary>
        /// 把工作集修剪到最小。GC 压缩大对象堆时会把那些页全部 touch 一遍，
        /// 于是出现「托管堆已经降到 1 MB，任务管理器还挂一百多兆」——CLR
        /// 不会主动把驻留页换出去，只能让系统剪一次。只在真正释放索引时
        /// 调，代价是下次用到这些代码页会有几个软错误，托盘常驻可忽略。
        /// </summary>
        private static void TrimWorkingSet()
        {
            try
            {
                // -1 / -1 = 让系统把工作集修剪到最小集。
                NativeSetProcessWorkingSetSize(
                    NativeGetCurrentProcess(),
                    new IntPtr(-1), new IntPtr(-1));
            }
            catch (Exception ex)
            {
                Log.Error("修剪工作集失败", ex);
            }
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

        /// <summary>
        /// 这份索引是怎么来的：MFT 整卷枚举，还是退回来的目录遍历。
        /// 界面上要显示出来——用户得知道「极速」这一档到底有没有生效。
        /// </summary>
        public FileIndexSource Source
        {
            get
            {
                FileIndexData data = _data;
                return data == null
                    ? FileIndexSource.Directory : data.Source;
            }
        }

        /// <summary>当前这份清单是不是从本地缓存读回来的。</summary>
        public bool FromCache
        {
            get
            {
                FileIndexData data = _data;
                return data != null && data.FromCache;
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
                    Build(ResolveRoots());
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
            List<string> roots = ResolveRoots();
            // 先读本地缓存：命中就立刻 Publish，界面 1 秒内可搜。托盘常驻
            // 期间完全不碰这个文件，只有窗口打开才会走到这里。
            FileIndexData cached = FileIndexCache.Load(roots);
            if (cached != null)
            {
                Publish(cached);
                if (DateTime.UtcNow - cached.SavedAt <=
                    TimeSpan.FromHours(CacheStaleHours))
                {
                    return;
                }
                Log.Info("本地缓存超过 " + CacheStaleHours +
                    " 小时，后台更新文件清单（当前缓存仍可搜）");
            }
            Build(roots);
        }

        /// <summary>没人开着窗口就立刻放掉内存（缓存文件保留）。</summary>
        private void ReleaseIfIdle()
        {
            if (Volatile.Read(ref _consumers) > 0)
            {
                return;
            }
            DropData();
        }

        private void Build(List<string> roots)
        {
            _building = true;
            _progress = new FileScanProgress();
            FileIndexData data = null;
            try
            {
                data = new FileIndexData();
                data.Roots = roots;
                DateTime started = DateTime.UtcNow;
                // 边扫边发布：目录扫描是流式的，扫出三万条就先让界面能搜
                // 到一批，不用干等全盘（百万级要二三十秒）。MFT 那条路是
                // 一次性算完才写表，装不装这个回调都一样。
                FileIndexData snapshotSource = data;
                List<string> snapshotRoots = roots;
                FileScanner.PartialPublish = delegate
                {
                    PublishSnapshot(snapshotSource, snapshotRoots);
                };
                // 优先走 NTFS MFT（整卷一次枚举，比遍历目录快一到两个
                // 数量级）；没有管理员权限或非 NTFS 时自动退回目录扫描。
                string mftDetail;
                bool usedMft = MftScanner.TryScan(
                    roots, data, _progress, null, out mftDetail);
                data.Source = usedMft ? FileIndexSource.Mft
                    : FileIndexSource.Directory;
                if (usedMft)
                {
                    Log.Info("文件索引用 NTFS MFT 建立");
                }
                else
                {
                    Log.Info("MFT 不可用，退回目录扫描：" + mftDetail);
                    FileScanner.Scan(roots, data, _progress, null);
                }
                data.SavedAt = DateTime.UtcNow;
                // 扫出来是空的（比如全被跳过）就别顶掉已经在用的那份。
                if (data.Entries.Count > 0 || _data == null)
                {
                    _partialPublished = false;
                    Publish(data);
                    // 写盘在后台线程，界面已经在用这份数据了；失败只记日志，
                    // 不影响本次搜索（下次打开退化成现场扫描而已）。
                    FileIndexCache.Save(data);
                }
                _lastError = "";
                Log.Info("文件索引完成：" + data.Entries.Count +
                    " 条 / " + data.Directories.Count + " 个目录，耗时 " +
                    (int)(DateTime.UtcNow - started).TotalSeconds + " 秒");
                // 建索引这一趟会产生大量一次性垃圾（MFT 的父链回溯要两个
                // 百万级字典、目录扫描每批一个临时 List），而托盘程序平时
                // 几乎不分配内存，gen2 可能几小时都不跑——不主动收一次，
                // 任务管理器里就一直挂着建索引那几分钟的峰值。
                data.Entries.TrimExcess();
                long heap = GC.GetTotalMemory(false);
                if (heap > 120L * 1024 * 1024)
                {
                    long before = heap;
                    TrimHeap();
                    Log.Info("索引建完回收一次：托管 " +
                        (before / 1048576).ToString() + "MB → " +
                        (GC.GetTotalMemory(false) / 1048576).ToString() +
                        "MB");
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Log.Error("建立文件索引失败", ex);
            }
            finally
            {
                FileScanner.PartialPublish = null;
                _building = false;
            }
        }

        /// <summary>
        /// 发布「扫到一半」的索引。现在是**直接发布同一个对象**，不再拷
        /// 一份：条目表只追加不改写、下标永远稳定，界面搜到哪算哪；每次
        /// 发布只把版本号 +1，让界面重跑一次搜索。以前每批都整份拷一份
        /// 引用，155 万条时那是一串百兆级的垃圾。
        /// </summary>
        private void PublishSnapshot(
            FileIndexData source, List<string> roots)
        {
            if (source == null)
            {
                return;
            }
            if (_data != null && !_partialPublished)
            {
                // 已经有完整版在用，别被半成品顶掉。
                return;
            }
            if (source.Entries.Count == 0)
            {
                return;
            }
            _partialPublished = true;
            Publish(source);
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
