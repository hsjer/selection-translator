using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SelectionTranslator
{
    /// <summary>
    /// 用 NTFS 的 $MFT 直接枚举整卷文件名，替代「一个目录一个目录地
    /// FindFirstFile」。原理和 Everything 的快速索引一样：
    ///
    ///   CreateFile("\\.\C:") → DeviceIoControl(FSCTL_ENUM_USN_DATA)
    ///
    /// 一次系统调用就吐回成百上千条 USN 记录（文件名 + 父目录的文件
    /// 引用号 + 属性），整卷几百万条数据一般 2～5 秒读完，比遍历目录
    /// 快一到两个数量级，而且不碰文件内容、不需要逐个 stat。
    ///
    /// 限制：要读卷句柄，必须管理员权限；FAT32 / exFAT / 网络盘没有
    /// MFT。两种情况都返回 false，调用方自动退回目录扫描。
    /// 另外 USN 记录里没有文件大小和修改时间，这两列由结果列表按需
    /// 补齐（见 FileEntry 的 SizeMissing 标记）。
    /// </summary>
    internal static class MftScanner
    {
        private const int FsctlEnumUsnData = 0x000900B3;
        private const uint GenericRead = 0x80000000;
        private const uint FileShareReadWriteDelete = 0x00000007;
        private const uint OpenExisting = 3;
        private const int ErrorHandleEof = 38;
        private const int ErrorAccessDenied = 5;
        private const long RootFrn = 5L;          // 卷根目录固定的文件引用号
        /// <summary>低 48 位是 MFT 记录号，高 16 位是序列号。</summary>
        private const long EntryNoMask = 0x0000FFFFFFFFFFFFL;
        private const int BufferSize = 1 << 20;   // 1 MB，一次约 3000~8000 条
        private const int MaxChainDepth = 256;

        /// <summary>
        /// 当前进程有没有管理员权限。MFT 能不能用就卡在这一条上——
        /// 读 \\.\X: 卷句柄需要 SeManageVolume / 管理员组。
        /// 界面上要靠它区分「MFT 不适用（非 NTFS）」和「只是没提权」。
        /// </summary>
        internal static bool IsElevated()
        {
            try
            {
                System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true,
            CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr device, int controlCode,
            IntPtr inBuffer, int inBufferSize,
            IntPtr outBuffer, int outBufferSize,
            out int bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>MFT 结果里没有大小 / 时间，需要时再 stat。</summary>
        internal static bool IsVolumeNtfs(string volume)
        {
            try
            {
                return string.Equals(
                    new DriveInfo(volume).DriveFormat, "NTFS",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>能否对这个卷用 MFT（NTFS + 拿得到卷句柄）。</summary>
        public static bool CanRead(string volume)
        {
            if (!IsVolumeNtfs(volume))
            {
                return false;
            }
            IntPtr handle = CreateFileW(
                @"\\.\" + volume, GenericRead,
                FileShareReadWriteDelete, IntPtr.Zero,
                OpenExisting, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                return false;
            }
            CloseHandle(handle);
            return true;
        }

        /// <summary>给界面显示的一句话说明：为什么没用 MFT。</summary>
        public static string DescribeAvailability(List<string> roots)
        {
            if (roots == null || roots.Count == 0)
            {
                return "没有扫描范围";
            }
            for (int i = 0; i < roots.Count; i++)
            {
                string volume = VolumeOf(roots[i]);
                if (volume == null)
                {
                    return "不是本地盘：" + roots[i];
                }
                if (!IsVolumeNtfs(volume))
                {
                    return volume + " 不是 NTFS";
                }
                if (!CanRead(volume))
                {
                    return volume + " 需要管理员权限";
                }
            }
            return "";
        }

        /// <summary>
        /// 尝试整卷 MFT 枚举。返回 false 时 data 会被清空，调用方直接
        /// 退回 FileScanner 即可，不会留下半份索引。
        /// </summary>
        public static bool TryScan(
            List<string> roots, FileIndexData data,
            FileScanProgress progress, Func<bool> cancelled,
            out string detail)
        {
            detail = "";
            if (roots == null || roots.Count == 0 || data == null)
            {
                detail = "没有扫描范围";
                return false;
            }

            // 按卷分组：同一卷只读一次 MFT，多个根目录共用。
            List<string> volumes = new List<string>();
            Dictionary<string, List<string>> rootsByVolume =
                new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                string volume = VolumeOf(root);
                if (volume == null)
                {
                    detail = "不是本地盘：" + root;
                    return false;
                }
                if (!IsVolumeNtfs(volume))
                {
                    detail = volume + " 不是 NTFS";
                    return false;
                }
                List<string> list;
                if (!rootsByVolume.TryGetValue(volume, out list))
                {
                    list = new List<string>();
                    rootsByVolume[volume] = list;
                    volumes.Add(volume);
                }
                list.Add(NormalizeRoot(root));
            }

            for (int i = 0; i < volumes.Count; i++)
            {
                string error;
                if (!ScanVolume(volumes[i], rootsByVolume[volumes[i]],
                    data, progress, cancelled, out error))
                {
                    data.Entries.Clear();
                    data.Directories.Clear();
                    detail = error;
                    return false;
                }
            }
            return true;
        }

        private static string VolumeOf(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }
            string pathRoot;
            try
            {
                pathRoot = Path.GetPathRoot(root);
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrEmpty(pathRoot) || pathRoot.Length < 2 ||
                pathRoot[1] != ':')
            {
                return null;
            }
            return pathRoot.Substring(0, 2).ToUpperInvariant();
        }

        /// <summary>根目录统一成「不带结尾反斜杠」的形式，方便和 MFT 路径比对。</summary>
        private static string NormalizeRoot(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return "";
            }
            string trimmed = root.TrimEnd('\\', '/');
            if (trimmed.Length == 2 && trimmed[1] == ':')
            {
                // "C:" 还原成 "C:\"，否则前缀比对会把 "C:\x" 当成子串。
                return trimmed + "\\";
            }
            return trimmed;
        }

        private static bool ScanVolume(
            string volume, List<string> roots,
            FileIndexData data, FileScanProgress progress,
            Func<bool> cancelled, out string error)
        {
            error = "";
            string device = @"\\.\" + volume;
            IntPtr handle = CreateFileW(
                device, GenericRead, FileShareReadWriteDelete,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                int code = Marshal.GetLastWin32Error();
                error = volume + (code == ErrorAccessDenied
                    ? " 需要管理员权限才能读 MFT"
                    : " 打开卷失败(" + code + ")");
                return false;
            }

            long[] frn = new long[1 << 16];
            long[] parent = new long[1 << 16];
            byte[] flags = new byte[1 << 16];   // 1=目录 2=重解析点
            string[] names = new string[1 << 16];
            int count = 0;
            try
            {
                string readError;
                if (!EnumerateRecords(handle, ref frn, ref parent,
                    ref flags, ref names, ref count, cancelled, out readError))
                {
                    error = readError;
                    return false;
                }
            }
            finally
            {
                CloseHandle(handle);
            }

            Emit(volume, roots, data, progress, cancelled,
                frn, parent, flags, names, count);
            return true;
        }

        private static bool EnumerateRecords(
            IntPtr handle,
            ref long[] frn, ref long[] parent, ref byte[] flags,
            ref string[] names, ref int count,
            Func<bool> cancelled, out string error)
        {
            error = "";
            // MFT_ENUM_DATA_V0 = { StartFileReferenceNumber, LowUsn, HighUsn }，
            // 三个字段都是 USN（有符号 64 位）。这里必须用 Marshal.WriteInt64
            // 直接写结构体，不能按字节数组手工拼。
            byte[] input = new byte[24];
            byte[] output = new byte[BufferSize];
            GCHandle inputPin = GCHandle.Alloc(input, GCHandleType.Pinned);
            GCHandle outputPin = GCHandle.Alloc(output, GCHandleType.Pinned);
            try
            {
                IntPtr inputPtr = inputPin.AddrOfPinnedObject();
                IntPtr outputPtr = outputPin.AddrOfPinnedObject();
                long startFrn = 0;
                while (true)
                {
                    if (cancelled != null && cancelled())
                    {
                        return true;
                    }
                    // MFT_ENUM_DATA_V0：起始引用号 + USN 区间。
                    Marshal.WriteInt64(inputPtr, 0, startFrn);
                    Marshal.WriteInt64(inputPtr, 8, 0);
                    Marshal.WriteInt64(inputPtr, 16, long.MaxValue);

                    int returned;
                    if (!DeviceIoControl(
                        handle, FsctlEnumUsnData,
                        inputPtr, 24, outputPtr, BufferSize,
                        out returned, IntPtr.Zero))
                    {
                        int code = Marshal.GetLastWin32Error();
                        if (code == ErrorHandleEof)
                        {
                            return true;
                        }
                        error = "枚举 MFT 失败(" + code + ")";
                        return false;
                    }
                    if (returned <= 8)
                    {
                        return true;
                    }

                    startFrn = Marshal.ReadInt64(outputPtr, 0);
                    int offset = 8;
                    while (offset + 60 <= returned)
                    {
                        int length = Marshal.ReadInt32(outputPtr, offset);
                        if (length <= 0 || offset + length > returned)
                        {
                            break;
                        }
                        short major = Marshal.ReadInt16(
                            outputPtr, offset + 4);
                        if (major != 2 && major != 3)
                        {
                            offset += length;
                            continue;
                        }
                        if (major == 3)
                        {
                            // USN_RECORD_V3 用 128 位引用号，极少见，跳过
                            // 总比解析错位好。
                            offset += length;
                            continue;
                        }
                        long recordFrn = Marshal.ReadInt64(
                            outputPtr, offset + 8);
                        long parentFrn = Marshal.ReadInt64(
                            outputPtr, offset + 16);
                        int attributes = Marshal.ReadInt32(
                            outputPtr, offset + 52);
                        short nameBytes = Marshal.ReadInt16(
                            outputPtr, offset + 56);
                        short nameOffset = Marshal.ReadInt16(
                            outputPtr, offset + 58);
                        offset += length;

                        if (nameBytes <= 0 || nameOffset <= 0 ||
                            nameOffset + nameBytes > length)
                        {
                            continue;
                        }
                        // 重解析点（junction / 符号链接）跳过：避免和真实
                        // 路径撞出重复条目，行为与目录扫描保持一致。
                        bool reparse =
                            (attributes & 0x00000400) != 0;
                        bool isDirectory =
                            (attributes & 0x00000010) != 0;
                        if (reparse)
                        {
                            continue;
                        }
                        if (count >= names.Length)
                        {
                            int next = names.Length * 2;
                            Array.Resize(ref frn, next);
                            Array.Resize(ref parent, next);
                            Array.Resize(ref flags, next);
                            Array.Resize(ref names, next);
                        }
                        frn[count] = recordFrn;
                        parent[count] = parentFrn;
                        flags[count] = (byte)(isDirectory ? 1 : 0);
                        names[count] = Marshal.PtrToStringUni(
                            new IntPtr(outputPtr.ToInt64() +
                                offset - length + nameOffset),
                            nameBytes / 2);
                        if (DebugRaw != null && DebugRaw.Count < 12)
                        {
                            DebugRaw.Add(string.Format(
                                "frn={0} parent={1} attr=0x{2:X} name={3}",
                                recordFrn, parentFrn, attributes,
                                names[count]));
                        }
                        count++;
                    }
                }
            }
            finally
            {
                inputPin.Free();
                outputPin.Free();
            }
        }

        /// <summary>探针用：留出前几条原始记录，方便核对解析是否正确。</summary>
        internal static List<string> DebugRaw = null;

        /// <summary>探针用：最后一次枚举的统计摘要。</summary>
        internal static string LastStats = "";

        /// <summary>把扁平记录拼成完整路径，再按根目录 / 跳过规则过滤。</summary>
        internal static void Emit(
            string volume, List<string> roots,
            FileIndexData data, FileScanProgress progress,
            Func<bool> cancelled,
            long[] frn, long[] parent, byte[] flags,
            string[] names, int count)
        {
            if (count == 0)
            {
                LastStats = volume + " 没有记录";
                return;
            }
            string volumeRoot = volume + "\\";
            int resolved = 0;
            int prunedCount = 0;
            int outOfRoots = 0;
            int noParent = 0;
            int brokenCount = 0;
            int brokenFirst = -1;
            string brokenReason = "";
            // 64 位引用号是「高 16 位序列号 + 低 48 位 MFT 记录号」。父目录
            // 的序列号几乎总跟子项记录号对不上（文件改名、记录复用都会
            // 让序列号 +1），所以父链只能按低 48 位的记录号去查。
            Dictionary<long, int> byEntry = new Dictionary<long, int>(count);
            for (int i = 0; i < count; i++)
            {
                long entryNo = frn[i] & EntryNoMask;
                if (!byEntry.ContainsKey(entryNo))
                {
                    byEntry[entryNo] = i;
                }
            }

            // 只给「目录」留完整路径：文件永远不可能当别人的父节点，
            // 留着它的路径除了占内存没有任何用处。以前这里给每条记录都
            // 存一份全路径，D 盘 168 万条记录光这一项就是三百多兆，托盘
            // 程序在任务管理器里直接冲到八百兆。
            string[] paths = new string[count];
            byte[] state = new byte[count];   // 0 未知 1 解析中 2 完成 3 放弃
            byte[] pruned = new byte[count];  // 1=整棵子树不要
            List<int> chain = new List<int>(32);

            // 探针诊断：卷根记录（entry 5）在枚举结果里出现的次数。
            int rootHits = 0;
            for (int i = 0; i < count; i++)
            {
                if ((frn[i] & EntryNoMask) == RootFrn)
                {
                    rootHits++;
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (cancelled != null && cancelled())
                {
                    return;
                }
                if (state[i] != 0)
                {
                    continue;
                }
                if ((frn[i] & EntryNoMask) == RootFrn)
                {
                    paths[i] = volumeRoot;
                    state[i] = 2;
                    continue;
                }
                chain.Clear();
                int cursor = i;
                string basePath = null;
                // 回溯全程不写 state：中途标成「解析中」会让祖先自己被
                // 外层循环当成已处理而跳过。只临时记录链条，回溯结束、
                // 确认有 basePath 后再统一落盘。
                bool broken = false;
                while (true)
                {
                    if (state[cursor] == 2)
                    {
                        basePath = paths[cursor];
                        break;
                    }
                    if (state[cursor] == 3)
                    {
                        broken = true;
                        break;
                    }
                    if ((frn[cursor] & EntryNoMask) == RootFrn)
                    {
                        basePath = volumeRoot;
                        break;
                    }
                    // 卷根记录（entry 5）不一定出现在枚举结果里（实测
                    // FSCTL_ENUM_USN_DATA 会漏掉 0~16 号保留记录），所以
                    // 不能只靠「找到根记录」来收尾：父号就是 5 时同样
                    // 说明已经到顶，当前这条就是根的一级子项。
                    if ((parent[cursor] & EntryNoMask) == RootFrn)
                    {
                        chain.Add(cursor);
                        basePath = volumeRoot;
                        break;
                    }
                    if (chain.Count > MaxChainDepth)
                    {
                        brokenCount++;
                        if (brokenFirst < 0)
                        {
                            brokenFirst = i;
                            brokenReason = "depth max at cursor=" + cursor +
                                " parentNo=" + (parent[cursor] & EntryNoMask);
                        }
                        broken = true;
                        break;
                    }
                    int parentIndex;
                    if (!byEntry.TryGetValue(parent[cursor] & EntryNoMask,
                        out parentIndex))
                    {
                        brokenCount++;
                        if (brokenFirst < 0)
                        {
                            brokenFirst = i;
                        }
                        if (brokenCount <= 3)
                        {
                            brokenReason += "[i=" + i + " cursor=" + cursor +
                                " parentNo=" + (parent[cursor] & EntryNoMask) +
                                " state=" + state[cursor] + "]";
                        }
                        broken = true;
                        break;
                    }
                    chain.Add(cursor);
                    cursor = parentIndex;
                }
                if (broken || basePath == null)
                {
                    brokenCount++;
                    if (brokenFirst < 0)
                    {
                        brokenFirst = i;
                        brokenReason = "cursor=" + cursor + " chain=" +
                            chain.Count + " stateCursor=" +
                            state[cursor] + " parentNo=" +
                            (parent[i] & EntryNoMask) +
                            " hasParent=" + byEntry.ContainsKey(
                                parent[i] & EntryNoMask);
                    }
                    state[i] = 3;
                    continue;
                }
                string accumulated = basePath;
                for (int k = chain.Count - 1; k >= 0; k--)
                {
                    int id = chain[k];
                    bool dirNode = flags[id] == 1;
                    if (dirNode)
                    {
                        accumulated = Combine(accumulated, names[id]);
                        paths[id] = accumulated;
                    }
                    state[id] = 2;
                    // 父目录被剪掉，子项一律剪掉；名字在跳过名单里也剪掉。
                    // 注意这里对文件也要打标记：只标目录的话，剪枝目录下的
                    // 文件会被误算成「父目录不在范围里」，统计就乱了。
                    int p;
                    bool parentPruned = byEntry.TryGetValue(
                        parent[id] & EntryNoMask, out p) && pruned[p] == 1;
                    if (parentPruned ||
                        (dirNode && (FileScanner.ShouldSkipDirectory(names[id]) ||
                        FileScanner.IsUnderSkippedRoot(accumulated))))
                    {
                        pruned[id] = 1;
                    }
                }
            }

            Dictionary<string, int> dirIds =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (!dirIds.ContainsKey(root))
                {
                    dirIds[root] = data.Directories.Count;
                    data.Directories.Add(root);
                }
            }

            int added = 0;
            int dirs = 0;
            for (int i = 0; i < count; i++)
            {
                if (state[i] == 2)
                {
                    resolved++;
                }
                if (pruned[i] == 1)
                {
                    prunedCount++;
                }
            }
            // 第一趟：先把所有目录登记进 dirIds。MFT 记录按记录号排列，
            // 子项完全可能排在父目录前面；如果边遍历边登记，大量文件会
            // 因为「父目录还不在表里」被误判丢弃（实测丢了 75 万条）。
            for (int i = 0; i < count; i++)
            {
                if (state[i] != 2 || paths[i] == null || pruned[i] == 1 ||
                    flags[i] != 1)
                {
                    continue;
                }
                string dirPath = paths[i];
                if (string.Equals(dirPath, volumeRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                    !UnderRoots(dirPath, roots))
                {
                    continue;
                }
                if (!dirIds.ContainsKey(dirPath))
                {
                    dirIds[dirPath] = data.Directories.Count;
                    data.Directories.Add(dirPath);
                    dirs++;
                }
            }

            // 第二趟：收文件与目录条目。
            for (int i = 0; i < count; i++)
            {
                if (state[i] != 2 || pruned[i] == 1)
                {
                    continue;
                }
                bool isDirectory = flags[i] == 1;
                // 只有目录留了完整路径。文件不用再算一遍路径：dirIds 里
                // 装的都是「扫描范围内的目录」，父目录能在里面查到，就说明
                // 这条文件也在范围内，等价于以前那次 UnderRoots 全路径判断。
                if (isDirectory)
                {
                    string dirPath = paths[i];
                    if (dirPath == null ||
                        string.Equals(dirPath, volumeRoot,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!UnderRoots(dirPath, roots))
                    {
                        outOfRoots++;
                        continue;
                    }
                }
                int parentIndex;
                string parentPath;
                if ((parent[i] & EntryNoMask) == RootFrn)
                {
                    // 父就是卷根，而根记录本身通常不在枚举里。
                    parentPath = volumeRoot;
                }
                else if (!byEntry.TryGetValue(parent[i] & EntryNoMask,
                        out parentIndex) ||
                    state[parentIndex] != 2 || paths[parentIndex] == null)
                {
                    noParent++;
                    continue;
                }
                else
                {
                    parentPath = paths[parentIndex];
                }
                int dirId;
                if (!dirIds.TryGetValue(parentPath, out dirId))
                {
                    // 父目录不在扫描范围里（或本身被剪掉），这条也就不要。
                    noParent++;
                    continue;
                }

                data.Entries.Add(dirId, names[i], -1, -1,
                    FileKindLogic.Classify(names[i], isDirectory), true);
                added++;

                if (data.Entries.Count >= FileScanner.MaxEntries)
                {
                    break;
                }
            }
            progress.AddFiles(added);
            if (dirs > 0)
            {
                progress.AddDirectories(dirs);
            }
            LastStats = string.Format(
                "记录 {0}：解析 {1}，剪枝 {2}，范围外 {3}，" +
                "无父目录 {4}，写入 {5}，目录表 {6}，根记录 {7} | " +
                "broken={8} first={9} why={10}",
                count, resolved, prunedCount, outOfRoots,
                noParent, added, data.Directories.Count, rootHits,
                brokenCount, brokenFirst, brokenReason);
            Log.Info("MFT 索引：" + LastStats);
        }

        private static string Combine(string directory, string name)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return name;
            }
            if (directory[directory.Length - 1] == '\\')
            {
                return directory + name;
            }
            return directory + "\\" + name;
        }

        private static bool UnderRoots(string path, List<string> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (root.Length == 0)
                {
                    continue;
                }
                if (path.Length < root.Length)
                {
                    continue;
                }
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (path.Length == root.Length)
                {
                    return true;
                }
                if (root[root.Length - 1] == '\\' ||
                    path[root.Length] == '\\')
                {
                    return true;
                }
            }
            return false;
        }
    }
}
