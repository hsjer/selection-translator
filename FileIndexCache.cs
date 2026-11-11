using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SelectionTranslator
{
    /// <summary>
    /// 文件清单的本地缓存：把 FileIndexData 的平行数组按内存布局整块写盘，
    /// 读回来也整块灌回数组，不做逐条反序列化（200 万条约 1 秒）。
    ///
    /// 它只是「打开窗口时不必重新枚举一遍磁盘」的加速手段，不是常驻索引：
    /// 托盘期间绝不读它，窗口关掉 30 秒后内存里那份照旧释放。旧版出过的
    /// 高内存问题来自「开机预热把缓存搬进堆 + 引用置空不回收」，不是缓存
    /// 文件本身，所以这里不提供任何预热入口。
    /// </summary>
    internal static class FileIndexCache
    {
        private const int FormatVersion = 1;
        private const int ChunkBytes = 1 << 20;
        private static readonly byte[] Magic = new byte[]
        {
            (byte)'S', (byte)'T', (byte)'F', (byte)'L',
            (byte)'I', (byte)'S', (byte)'T', (byte)'1'
        };

        public static string CachePath
        {
            get
            {
                return Path.Combine(
                    ConfigStore.DataDirectory, "filelist.cache");
            }
        }

        /// <summary>范围变了缓存就作废：签名按盘符 / 目录规范化后拼起来。</summary>
        public static string ScopeSignature(List<string> roots)
        {
            if (roots == null)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < roots.Count; i++)
            {
                string r = (roots[i] ?? "").TrimEnd('\\', '/')
                    .ToUpperInvariant();
                if (i > 0)
                {
                    sb.Append('|');
                }
                sb.Append(r);
            }
            return sb.ToString();
        }

        /// <summary>写临时文件再原子替换，写一半崩了不会留下半份缓存。</summary>
        public static bool Save(FileIndexData data)
        {
            return Save(data, CachePath);
        }

        /// <summary>可指定路径：探针用临时文件，不碰用户真实缓存。</summary>
        public static bool Save(FileIndexData data, string path)
        {
            if (data == null || data.Entries == null ||
                data.Entries.Count == 0)
            {
                return false;
            }
            string temp = path + ".tmp";
            try
            {
                FileEntryTable t = data.Entries;
                int count = t.Count;
                using (FileStream fs = new FileStream(temp, FileMode.Create,
                    FileAccess.Write, FileShare.None, 1 << 16))
                using (BinaryWriter w = new BinaryWriter(fs, Encoding.UTF8))
                {
                    w.Write(Magic);
                    w.Write(FormatVersion);
                    w.Write(ScopeSignature(data.Roots));
                    w.Write((byte)data.Source);
                    w.Write(data.SavedAt.Ticks);
                    w.Write(count);
                    w.Write(data.Directories.Count);
                    w.Write(t.PoolUsed);

                    for (int i = 0; i < data.Directories.Count; i++)
                    {
                        w.Write(data.Directories[i] ?? "");
                    }
                    WriteRaw(w, t.Pool, t.PoolUsed, 2);
                    WriteRaw(w, t.DirIds, count, 4);
                    WriteRaw(w, t.NameStarts, count, 4);
                    WriteRaw(w, t.NameLengths, count, 2);
                    WriteRaw(w, t.Sizes, count, 8);
                    WriteRaw(w, t.TicksArray, count, 8);
                    WriteRaw(w, t.Kinds, count, 1);
                    WriteRaw(w, t.Flags, count, 1);
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temp, path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("保存文件清单缓存失败", ex);
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                }
                return false;
            }
        }

        /// <summary>
        /// 读缓存。文件不存在 / 版本不对 / 范围不符 / 内容坏了都安静返回
        /// null，让调用方退回现场扫描，不能把启动流程搞崩。
        /// </summary>
        public static FileIndexData Load(List<string> roots)
        {
            return Load(roots, CachePath);
        }

        public static FileIndexData Load(List<string> roots, string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 1 << 16))
                using (BinaryReader r = new BinaryReader(fs, Encoding.UTF8))
                {
                    byte[] magic = r.ReadBytes(Magic.Length);
                    if (magic.Length != Magic.Length)
                    {
                        return null;
                    }
                    for (int i = 0; i < Magic.Length; i++)
                    {
                        if (magic[i] != Magic[i])
                        {
                            return null;
                        }
                    }
                    if (r.ReadInt32() != FormatVersion)
                    {
                        return null;
                    }
                    if (!string.Equals(r.ReadString(),
                        ScopeSignature(roots), StringComparison.Ordinal))
                    {
                        return null;
                    }
                    int source = r.ReadByte();
                    long ticks = r.ReadInt64();
                    int count = r.ReadInt32();
                    int dirCount = r.ReadInt32();
                    int poolUsed = r.ReadInt32();
                    // 不能用 FileScanner.MaxEntries 当上限：目录扫描在
                    // 「停入子目录」之前会把当前目录整批加进来，实际条数
                    // 会略超 200 万（本机 2,049,697）。缓存是程序自己写
                    // 的，条数可信；这里只挡住损坏文件里伪造的巨型数字。
                    // 分配数组之前先按「每条 28 字节 + 字符池 2 字节/字」
                    // 核对文件剩余长度，坏文件不会先吃掉几百兆再报错。
                    long arraysBytes = (long)count * 28 +
                        (long)poolUsed * 2;
                    if (count <= 0 || count > 10000000 ||
                        dirCount < 0 || dirCount > count ||
                        poolUsed < 0 || poolUsed > 200000000 ||
                        arraysBytes > fs.Length - fs.Position)
                    {
                        return null;
                    }

                    List<string> dirs = new List<string>(dirCount);
                    for (int i = 0; i < dirCount; i++)
                    {
                        dirs.Add(r.ReadString());
                    }
                    char[] pool = new char[poolUsed];
                    int[] dirId = new int[count];
                    int[] nameStart = new int[count];
                    ushort[] nameLen = new ushort[count];
                    long[] size = new long[count];
                    long[] time = new long[count];
                    byte[] kind = new byte[count];
                    byte[] flags = new byte[count];

                    Stream s = r.BaseStream;
                    ReadRaw(s, pool, poolUsed, 2);
                    ReadRaw(s, dirId, count, 4);
                    ReadRaw(s, nameStart, count, 4);
                    ReadRaw(s, nameLen, count, 2);
                    ReadRaw(s, size, count, 8);
                    ReadRaw(s, time, count, 8);
                    ReadRaw(s, kind, count, 1);
                    ReadRaw(s, flags, count, 1);

                    FileIndexData data = new FileIndexData();
                    data.Roots = roots;
                    data.Directories = dirs;
                    data.Entries.LoadDirect(count, poolUsed, pool,
                        dirId, nameStart, nameLen, size, time, kind, flags);
                    data.SavedAt = new DateTime(ticks, DateTimeKind.Utc);
                    data.Source = (FileIndexSource)source;
                    data.FromCache = true;
                    return data;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取文件清单缓存失败，将重新扫描", ex);
                return null;
            }
        }

        public static long SizeBytes()
        {
            try
            {
                return new FileInfo(CachePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(CachePath))
                {
                    File.Delete(CachePath);
                }
            }
            catch
            {
            }
        }

        /// <summary>整块 memcpy，不逐元素 Write（200 万条逐元素要十几秒）。</summary>
        private static void WriteRaw(
            BinaryWriter w, Array source, int count, int elemSize)
        {
            int total = count * elemSize;
            byte[] buffer = new byte[Math.Min(ChunkBytes,
                Math.Max(elemSize, 4096))];
            int offset = 0;
            while (offset < total)
            {
                int chunk = Math.Min(buffer.Length, total - offset);
                chunk -= chunk % elemSize;
                if (chunk <= 0)
                {
                    break;
                }
                Buffer.BlockCopy(source, offset, buffer, 0, chunk);
                w.Write(buffer, 0, chunk);
                offset += chunk;
            }
        }

        private static void ReadRaw(
            Stream s, Array dest, int count, int elemSize)
        {
            int total = count * elemSize;
            byte[] buffer = new byte[Math.Min(ChunkBytes,
                Math.Max(elemSize, 4096))];
            int offset = 0;
            while (offset < total)
            {
                int chunk = Math.Min(buffer.Length, total - offset);
                chunk -= chunk % elemSize;
                if (chunk <= 0)
                {
                    break;
                }
                int done = 0;
                while (done < chunk)
                {
                    int n = s.Read(buffer, done, chunk - done);
                    if (n <= 0)
                    {
                        throw new EndOfStreamException(
                            "文件清单缓存被截断");
                    }
                    done += n;
                }
                Buffer.BlockCopy(buffer, 0, dest, offset, chunk);
                offset += chunk;
            }
        }
    }
}
