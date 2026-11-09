using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SelectionTranslator
{
    /// <summary>
    /// 任务栏状态条的数据采样与位置计算。所有方法都不依赖窗口，
    /// 便于自检直接验证；绘图与窗口在 TaskbarStatsForm。
    /// </summary>
    internal static class TaskbarStats
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out FileTime idleTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(
            ref MemoryStatusEx buffer);

        private static ulong ToUInt64(FileTime value)
        {
            return ((ulong)value.dwHighDateTime << 32) |
                value.dwLowDateTime;
        }

        /// <summary>累加所有已启用网卡的收发字节数（用托管 API，零第三方依赖）。</summary>
        public static void SampleTraffic(out long received, out long sent)
        {
            Dictionary<string, long> byNicReceived;
            Dictionary<string, long> byNicSent;
            SampleTrafficByNic(
                out byNicReceived, out byNicSent,
                out received, out sent);
        }

        /// <summary>
        /// 按网卡分别采样，同时给出总和。今日流量统计必须按网卡记底数，
        /// 否则网卡插拔 / 计数器回绕会把开机以来的累计误算成今日用量。
        /// </summary>
        public static void SampleTrafficByNic(
            out Dictionary<string, long> receivedByNic,
            out Dictionary<string, long> sentByNic,
            out long received,
            out long sent)
        {
            receivedByNic = new Dictionary<string, long>();
            sentByNic = new Dictionary<string, long>();
            received = 0;
            sent = 0;
            try
            {
                NetworkInterface[] interfaces =
                    NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface nic = interfaces[i];
                    if (nic.NetworkInterfaceType ==
                        NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    if (nic.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }
                    IPv4InterfaceStatistics stats =
                        nic.GetIPv4Statistics();
                    long nicReceived = stats.BytesReceived;
                    long nicSent = stats.BytesSent;
                    receivedByNic[nic.Id] = nicReceived;
                    sentByNic[nic.Id] = nicSent;
                    received += nicReceived;
                    sent += nicSent;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// GetSystemTimes 的 kernel 时间已包含 idle，total = kernel + user。
        /// 用两次采样的差值算占用率，比 PerformanceCounter 更轻且不要求权限。
        /// </summary>
        public static bool SampleCpu(out ulong idle, out ulong total)
        {
            FileTime idleTime;
            FileTime kernelTime;
            FileTime userTime;
            if (!GetSystemTimes(
                out idleTime, out kernelTime, out userTime))
            {
                idle = 0;
                total = 0;
                return false;
            }
            idle = ToUInt64(idleTime);
            total = ToUInt64(kernelTime) + ToUInt64(userTime);
            return true;
        }

        public static int ComputeCpuPercent(
            ulong idleDelta,
            ulong totalDelta)
        {
            if (totalDelta == 0 || idleDelta > totalDelta)
            {
                return 0;
            }
            double busy = 1.0 - (double)idleDelta / totalDelta;
            int percent = (int)Math.Round(busy * 100.0);
            if (percent < 0)
            {
                return 0;
            }
            if (percent > 100)
            {
                return 100;
            }
            return percent;
        }

        public static int MemoryLoad()
        {
            MemoryStatusEx status = new MemoryStatusEx();
            status.dwLength = (uint)Marshal.SizeOf(
                typeof(MemoryStatusEx));
            if (!GlobalMemoryStatusEx(ref status))
            {
                return 0;
            }
            int load = (int)status.dwMemoryLoad;
            if (load < 0)
            {
                return 0;
            }
            if (load > 100)
            {
                return 100;
            }
            return load;
        }

        /// <summary>内存字节数（已用 / 总量）。失败时两者为 0。</summary>
        public static void MemoryBytes(out ulong used, out ulong total)
        {
            used = 0;
            total = 0;
            MemoryStatusEx status = new MemoryStatusEx();
            status.dwLength = (uint)Marshal.SizeOf(
                typeof(MemoryStatusEx));
            if (!GlobalMemoryStatusEx(ref status))
            {
                return;
            }
            total = status.ullTotalPhys;
            used = status.ullTotalPhys > status.ullAvailPhys
                ? status.ullTotalPhys - status.ullAvailPhys
                : 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessorPowerInformation
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [DllImport("powrprof.dll")]
        private static extern uint CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            int inputBufferSize,
            IntPtr outputBuffer,
            int outputBufferSize);

        // ===== 实时 CPU 频率 =====
        // Windows 8 之后 CallNtPowerInformation(ProcessorInformation) 的
        // CurrentMhz 在绝大多数机器上恒等于标称/基准频率，睿频、降频都不更新
        // （实测 i7-1065G7 空载满载都固定 1298MHz）。任务管理器用的是 PDH
        // 计数器 % Processor Performance × 标称频率，这里走同一套算法。
        // PDH 计数器路径用英文 API，避免中文系统计数器名本地化导致读取失败。
        private const uint PdhFmtDouble = 0x00000200;
        private const string CpuPerfCounterPath =
            "\\Processor Information(_Total)\\% Processor Performance";

        [StructLayout(LayoutKind.Explicit)]
        private struct PdhValue
        {
            [FieldOffset(0)] public int LongValue;
            [FieldOffset(0)] public double DoubleValue;
            [FieldOffset(0)] public long LargeValue;
            [FieldOffset(0)] public IntPtr StringValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFormattedValue
        {
            public uint CStatus;
            public PdhValue Value;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQueryW(
            string dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounterW(
            IntPtr query, string counterPath,
            IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(
            IntPtr counter, uint format, out uint counterType,
            out PdhFormattedValue value);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        private static readonly object CpuFreqLock = new object();
        private static IntPtr s_pdhQuery = IntPtr.Zero;
        private static IntPtr s_pdhCounter = IntPtr.Zero;
        private static bool s_pdhUnavailable;
        private static double s_smoothedMhz = -1;

        /// <summary>
        /// 当前 CPU 频率（MHz）。优先用实时性能百分比换算；
        /// 计数器不可用时回退到电源 API 的旧读数。0 表示完全取不到。
        /// </summary>
        public static int CpuFrequencyMhz()
        {
            double percent = ProcessorPerformancePercent();
            if (percent > 0)
            {
                int nominal = CpuNominalMhz();
                if (nominal > 0)
                {
                    int mhz = (int)Math.Round(
                        nominal * percent / 100.0);
                    if (mhz >= 100 && mhz <= 10000)
                    {
                        // 轻度平滑，避免每帧跳几十 MHz 看着刺眼，
                        // 负载变化仍能在一两秒内体现出来。
                        lock (CpuFreqLock)
                        {
                            s_smoothedMhz = s_smoothedMhz <= 0
                                ? mhz
                                : s_smoothedMhz + 0.5 * (mhz - s_smoothedMhz);
                            mhz = (int)Math.Round(s_smoothedMhz);
                        }
                        return mhz;
                    }
                }
            }
            return LegacyCpuFrequencyMhz();
        }

        /// <summary>旧读数：电源 API 的 CurrentMhz，取所有核心最大值。</summary>
        private static int LegacyCpuFrequencyMhz()
        {
            try
            {
                int structSize = Marshal.SizeOf(
                    typeof(ProcessorPowerInformation));
                int count = Math.Max(
                    1, Math.Min(256, Environment.ProcessorCount));
                // 缓冲区必须清零且只遍历真实核心数：AllocHGlobal 不清零，
                // 多读的槽位是随机内存，会冒出几十亿 MHz 的假频率。
                byte[] buffer = new byte[structSize * count];
                GCHandle pinned = GCHandle.Alloc(
                    buffer, GCHandleType.Pinned);
                try
                {
                    IntPtr address = pinned.AddrOfPinnedObject();
                    uint result = CallNtPowerInformation(
                        11, IntPtr.Zero, 0, address, buffer.Length);
                    if (result != 0)
                    {
                        return 0;
                    }
                    int best = 0;
                    for (int i = 0; i < count; i++)
                    {
                        ProcessorPowerInformation info =
                            (ProcessorPowerInformation)
                            Marshal.PtrToStructure(
                                new IntPtr(
                                    address.ToInt64() + i * structSize),
                                typeof(ProcessorPowerInformation));
                        // 合理范围过滤，避免驱动异常值显示成几千 GHz。
                        if (info.CurrentMhz >= 100 &&
                            info.CurrentMhz <= 10000 &&
                            (int)info.CurrentMhz > best)
                        {
                            best = (int)info.CurrentMhz;
                        }
                    }
                    return best;
                }
                finally
                {
                    pinned.Free();
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 读取 _Total 的 % Processor Performance（100 = 标称频率，
        /// 200 = 两倍标称频率即睿频）。失败返回 0。
        /// </summary>
        public static double ProcessorPerformancePercent()
        {
            lock (CpuFreqLock)
            {
                if (s_pdhUnavailable)
                {
                    return 0;
                }
                if (s_pdhQuery == IntPtr.Zero)
                {
                    IntPtr query;
                    IntPtr counter;
                    if (PdhOpenQueryW(
                            null, IntPtr.Zero, out query) != 0)
                    {
                        s_pdhUnavailable = true;
                        return 0;
                    }
                    if (PdhAddEnglishCounterW(
                            query, CpuPerfCounterPath,
                            IntPtr.Zero, out counter) != 0)
                    {
                        PdhCloseQuery(query);
                        s_pdhUnavailable = true;
                        return 0;
                    }
                    s_pdhQuery = query;
                    s_pdhCounter = counter;
                    // 预热一次，让计数器有初值。
                    PdhCollectQueryData(query);
                }
                if (PdhCollectQueryData(s_pdhQuery) != 0)
                {
                    return 0;
                }
                uint counterType;
                PdhFormattedValue value;
                if (PdhGetFormattedCounterValue(
                        s_pdhCounter, PdhFmtDouble,
                        out counterType, out value) != 0 ||
                    value.CStatus != 0)
                {
                    return 0;
                }
                double percent = value.Value.DoubleValue;
                if (percent <= 0 || percent > 1000)
                {
                    return 0;
                }
                return percent;
            }
        }

        /// <summary>
        /// CPU 标称/基准频率（MHz）。优先解析 CPU 名称里的 @ x.xxGHz
        /// （与任务管理器的「基准速度」一致），再退回注册表 ~MHz。
        /// </summary>
        public static int CpuNominalMhz()
        {
            int mhz = 0;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        string name = key.GetValue(
                            "ProcessorNameString") as string;
                        mhz = ParseCpuNameMhz(name);
                        if (mhz <= 0)
                        {
                            object raw = key.GetValue("~MHz");
                            if (raw != null)
                            {
                                mhz = Convert.ToInt32(
                                    raw, CultureInfo.InvariantCulture);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            if (mhz < 100 || mhz > 10000)
            {
                mhz = 0;
            }
            return mhz;
        }

        /// <summary>"... CPU @ 1.30GHz" → 1300；解析不出来返回 0。</summary>
        public static int ParseCpuNameMhz(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }
            int index = name.IndexOf(
                "GHz", StringComparison.OrdinalIgnoreCase);
            if (index <= 0)
            {
                return 0;
            }
            int end = index;
            while (end > 0 && name[end - 1] == ' ')
            {
                end--;
            }
            int start = end;
            while (start > 0 &&
                (char.IsDigit(name[start - 1]) ||
                    name[start - 1] == '.'))
            {
                start--;
            }
            if (start == end)
            {
                return 0;
            }
            double ghz;
            if (!double.TryParse(
                    name.Substring(start, end - start),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out ghz))
            {
                return 0;
            }
            if (ghz < 0.1 || ghz > 10.0)
            {
                return 0;
            }
            return (int)Math.Round(ghz * 1000.0);
        }

        /// <summary>系统盘剩余 / 总容量（字节）。失败时两者为 0。</summary>
        public static void DiskSpace(
            out long freeBytes, out long totalBytes)
        {
            freeBytes = 0;
            totalBytes = 0;
            try
            {
                string root = Path.GetPathRoot(
                    Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root))
                {
                    return;
                }
                DriveInfo drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    return;
                }
                freeBytes = drive.AvailableFreeSpace;
                totalBytes = drive.TotalSize;
            }
            catch
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(
            ref SystemPowerStatus status);

        /// <summary>
        /// 电池：电量百分比、是否接电源、剩余秒数（-1 表示未知）。
        /// 台式机（无电池）hasBattery 为 false，详情面板里整行隐藏。
        /// </summary>
        public static void Battery(
            out bool hasBattery,
            out int percent,
            out bool onAc,
            out int remainSeconds)
        {
            hasBattery = false;
            percent = 0;
            onAc = true;
            remainSeconds = -1;
            try
            {
                SystemPowerStatus status = new SystemPowerStatus();
                if (!GetSystemPowerStatus(ref status))
                {
                    return;
                }
                byte flag = status.BatteryFlag;
                if (flag == 0 || flag == 128 || flag == 255)
                {
                    // 0 = 无有效信息，128 = 无电池，255 = 未知。
                    return;
                }
                if (status.BatteryLifePercent > 100)
                {
                    return;
                }
                hasBattery = true;
                onAc = status.ACLineStatus == 1;
                if (status.BatteryLifePercent <= 100)
                {
                    percent = status.BatteryLifePercent;
                }
                if (status.BatteryLifeTime != 0xFFFFFFFF)
                {
                    remainSeconds = (int)status.BatteryLifeTime;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 把 GPU Engine 性能计数器的实例值按「物理 GPU → 引擎类型」分组求和，
        /// 每组钳到 0-100，再取该物理 GPU 各引擎类型的最大值作为利用率。
        /// 纯函数：实例值直接传入，方便自检；实时采样在 GpuSampler。
        /// 返回数组按 phys 序号升序，取不到时为空数组。
        /// </summary>
        public static int[] AggregateGpuLoad(
            IList<KeyValuePair<string, float>> instanceValues)
        {
            Dictionary<int, Dictionary<string, float>> byPhys =
                new Dictionary<int, Dictionary<string, float>>();
            if (instanceValues != null)
            {
                for (int i = 0; i < instanceValues.Count; i++)
                {
                    KeyValuePair<string, float> pair = instanceValues[i];
                    int phys;
                    string engine;
                    if (!TryParseGpuInstance(
                        pair.Key, out phys, out engine))
                    {
                        continue;
                    }
                    Dictionary<string, float> engines;
                    if (!byPhys.TryGetValue(phys, out engines))
                    {
                        engines = new Dictionary<string, float>();
                        byPhys[phys] = engines;
                    }
                    float sum;
                    engines.TryGetValue(engine, out sum);
                    engines[engine] = sum + pair.Value;
                }
            }

            List<int> keys = new List<int>(byPhys.Keys);
            keys.Sort();
            int[] loads = new int[keys.Count];
            for (int k = 0; k < keys.Count; k++)
            {
                float best = 0;
                foreach (KeyValuePair<string, float> engine
                    in byPhys[keys[k]])
                {
                    float clamped = engine.Value;
                    if (clamped < 0)
                    {
                        clamped = 0;
                    }
                    if (clamped > 100)
                    {
                        clamped = 100;
                    }
                    if (clamped > best)
                    {
                        best = clamped;
                    }
                }
                loads[k] = (int)Math.Round(best);
            }
            return loads;
        }

        /// <summary>
        /// 解析 GPU Engine 实例名，例如
        /// pid_10256_luid_0x0_0x1_phys_0_eng_0_engtype_3D
        /// → phys=0，engine="3D"。格式不对返回 false。
        /// </summary>
        public static bool TryParseGpuInstance(
            string instanceName,
            out int phys,
            out string engine)
        {
            phys = -1;
            engine = "";
            if (string.IsNullOrEmpty(instanceName))
            {
                return false;
            }
            int physIndex = instanceName.IndexOf("_phys_");
            int engineIndex = instanceName.IndexOf("_engtype_");
            if (physIndex < 0 || engineIndex < 0 ||
                engineIndex <= physIndex)
            {
                return false;
            }
            string physText = "";
            for (int i = physIndex + 6;
                i < instanceName.Length &&
                instanceName[i] >= '0' && instanceName[i] <= '9';
                i++)
            {
                physText += instanceName[i];
            }
            if (physText.Length == 0)
            {
                return false;
            }
            phys = int.Parse(physText);
            engine = instanceName.Substring(engineIndex + 9);
            return engine.Length > 0;
        }

        /// <summary>74.32 MB 这样的容量文案（与网速格式共用一套进位）。</summary>
        public static string FormatBytes(double bytes)
        {
            if (bytes < 0)
            {
                bytes = 0;
            }
            if (bytes < 1024.0)
            {
                return bytes.ToString("0") + " B";
            }
            if (bytes < 1024.0 * 1024.0)
            {
                return (bytes / 1024.0).ToString("0.00") + " KB";
            }
            if (bytes < 1024.0 * 1024.0 * 1024.0)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("0.00") +
                    " MB";
            }
            return (bytes / (1024.0 * 1024.0 * 1024.0))
                .ToString("0.00") + " GB";
        }

        /// <summary>3370 → 3.37 GHz；0 → 空串（调用方隐藏该行）。</summary>
        public static string FormatGhz(int mhz)
        {
            if (mhz <= 0)
            {
                return "";
            }
            return (mhz / 1000.0).ToString("0.00") + " GHz";
        }

        /// <summary>8100 → 2 小时 15 分；小于 1 分钟 → 不到 1 分钟。</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds < 0)
            {
                return "";
            }
            if (seconds < 60)
            {
                return "不到 1 分钟";
            }
            int minutes = seconds / 60;
            if (minutes < 60)
            {
                return minutes + " 分钟";
            }
            return (minutes / 60) + " 小时 " + (minutes % 60) + " 分";
        }

        /// <summary>一条「标签 + 数值」行。Value 为空则整行不显示。</summary>
        internal sealed class DetailRow
        {
            public string Label;
            public string Value;
        }

        /// <summary>
        /// 悬浮详情面板的一次完整采样。取不到的字段保持默认值，
        /// FormatDetailRows 会自动隐藏对应行。
        /// </summary>
        internal sealed class DetailSample
        {
            public long DayReceived;
            public long DaySent;
            public double UploadBps;
            public double DownloadBps;
            public int CpuPercent;
            public int CpuMhz;
            public long MemoryUsed;
            public long MemoryTotal;
            public long DiskFree;
            public long DiskTotal;
            public int[] GpuLoads;
            public bool HasBattery;
            public int BatteryPercent;
            public bool BatteryOnAc;
            public int BatteryRemainSeconds;
        }

        /// <summary>
        /// 把采样转成详情行：能取到的才输出，取不到的行自动省略。
        /// 纯函数，自检直接喂假数据断言行数与内容。
        /// </summary>
        public static List<DetailRow> FormatDetailRows(
            DetailSample sample)
        {
            List<DetailRow> rows = new List<DetailRow>();
            if (sample == null)
            {
                return rows;
            }

            DetailRow traffic = new DetailRow();
            traffic.Label = "今日流量";
            traffic.Value = FormatBytes(sample.DayReceived + sample.DaySent) +
                "  (↑ " + FormatBytes(sample.DaySent) +
                " / ↓ " + FormatBytes(sample.DayReceived) + ")";
            rows.Add(traffic);

            DetailRow speed = new DetailRow();
            speed.Label = "实时网速";
            speed.Value = "↑ " + FormatSpeed(sample.UploadBps) +
                "   ↓ " + FormatSpeed(sample.DownloadBps);
            rows.Add(speed);

            DetailRow cpu = new DetailRow();
            cpu.Label = "CPU";
            cpu.Value = sample.CpuPercent + "%";
            string ghz = FormatGhz(sample.CpuMhz);
            if (ghz.Length > 0)
            {
                cpu.Value += "   " + ghz;
            }
            rows.Add(cpu);

            if (sample.MemoryTotal > 0)
            {
                DetailRow memory = new DetailRow();
                memory.Label = "内存";
                memory.Value = FormatBytes(sample.MemoryUsed) +
                    " / " + FormatBytes(sample.MemoryTotal);
                int percent = sample.MemoryTotal > 0
                    ? (int)Math.Round(
                        100.0 * sample.MemoryUsed / sample.MemoryTotal)
                    : 0;
                memory.Value += "  (" + percent + "%)";
                rows.Add(memory);
            }

            if (sample.DiskTotal > 0)
            {
                DetailRow disk = new DetailRow();
                disk.Label = "磁盘剩余";
                disk.Value = FormatBytes(sample.DiskFree) +
                    " / " + FormatBytes(sample.DiskTotal);
                rows.Add(disk);
            }

            if (sample.GpuLoads != null && sample.GpuLoads.Length > 0)
            {
                if (sample.GpuLoads.Length == 1)
                {
                    DetailRow gpu = new DetailRow();
                    gpu.Label = "显卡利用率";
                    gpu.Value = sample.GpuLoads[0] + "%";
                    rows.Add(gpu);
                }
                else
                {
                    for (int i = 0; i < sample.GpuLoads.Length; i++)
                    {
                        DetailRow gpu = new DetailRow();
                        gpu.Label = "显卡 " + i + " 利用率";
                        gpu.Value = sample.GpuLoads[i] + "%";
                        rows.Add(gpu);
                    }
                }
            }

            if (sample.HasBattery)
            {
                DetailRow battery = new DetailRow();
                battery.Label = "电池";
                battery.Value = sample.BatteryPercent + "%";
                if (sample.BatteryOnAc)
                {
                    battery.Value += "   充电中";
                }
                else
                {
                    string duration = FormatDuration(
                        sample.BatteryRemainSeconds);
                    if (duration.Length > 0)
                    {
                        battery.Value += "   剩余 " + duration;
                    }
                }
                rows.Add(battery);
            }

            return rows;
        }

        /// <summary>
        /// 悬停判定：光标在状态条（含一点点缓冲）或在已展开的详情卡片内
        /// 就保持展开，移出两者即收起。抽成纯函数方便自检。
        /// </summary>
        public static bool ShouldShowDetail(
            Point cursor,
            Rectangle barBounds,
            Rectangle detailBounds,
            bool detailVisible)
        {
            if (!barBounds.IsEmpty &&
                Rectangle.Inflate(barBounds, 8, 8)
                    .Contains(cursor))
            {
                return true;
            }
            if (detailVisible && !detailBounds.IsEmpty &&
                Rectangle.Inflate(detailBounds, 10, 10)
                    .Contains(cursor))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 状态条专用：单位统一按 MB/s 算（≥1000 MB/s 用 GB/s），
        /// 数字位数稳定，不必为 B/s / KB/s / MB/s 三档各留一段余量，
        /// 网速与 CPU 之间也就不会空出一大截。详情卡片仍用
        /// FormatSpeed 显示精确的 B / KB 数值。
        /// </summary>
        public static string FormatSpeedCompact(double bytesPerSecond)
        {
            if (bytesPerSecond < 0)
            {
                bytesPerSecond = 0;
            }
            double mb = bytesPerSecond /
                (1024.0 * 1024.0);
            if (mb < 1000.0)
            {
                return mb.ToString(
                    "0.00", CultureInfo.InvariantCulture) +
                    " MB/s";
            }
            return (mb / 1024.0).ToString(
                "0.00", CultureInfo.InvariantCulture) +
                " GB/s";
        }

        public static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 0)
            {
                bytesPerSecond = 0;
            }
            if (bytesPerSecond < 1024.0)
            {
                return bytesPerSecond.ToString("0") + " B/s";
            }
            if (bytesPerSecond < 1024.0 * 1024.0)
            {
                return (bytesPerSecond / 1024.0).ToString("0.00") +
                    " KB/s";
            }
            if (bytesPerSecond < 1024.0 * 1024.0 * 1024.0)
            {
                return (bytesPerSecond / (1024.0 * 1024.0))
                    .ToString("0.00") + " MB/s";
            }
            return (bytesPerSecond /
                (1024.0 * 1024.0 * 1024.0)).ToString("0.00") +
                " GB/s";
        }

        /// <summary>
        /// 把状态条贴到系统托盘区左侧，与任务栏垂直居中。
        /// 托盘图标增减、任务栏换边、分辨率变化都会重新调用本方法。
        /// </summary>
        public static Point ComputePosition(
            Rectangle taskbar,
            Rectangle trayNotify,
            Size content,
            int margin)
        {
            if (taskbar.Width <= 0 || taskbar.Height <= 0)
            {
                return Point.Empty;
            }

            int x;
            bool hasNotify =
                trayNotify.Width > 40 &&
                trayNotify.Left > taskbar.Left &&
                trayNotify.Left <= taskbar.Right;

            if (hasNotify)
            {
                x = trayNotify.Left - content.Width - margin;
            }
            else
            {
                // Win11 的任务栏可能不暴露 TrayNotifyWnd，给系统图标区
                // 预留一段固定宽度，避免盖住时间 / 输入法 / 音量图标。
                x = taskbar.Right - content.Width -
                    (int)Math.Round(210.0 * content.Height / 32.0) -
                    margin;
            }

            if (x < taskbar.Left + 4)
            {
                x = taskbar.Left + 4;
            }

            int y = taskbar.Top +
                (taskbar.Height - content.Height) / 2;
            return new Point(x, y);
        }

        /// <summary>
        /// 状态条一行的四段。拆开画是为了让两行共用固定的列边界，
        /// 数字位数跳变时列位置不跟着动（否则上下会明显不对称）。
        /// </summary>
        internal sealed class BarLine
        {
            public string Arrow;
            public string Speed;
            public string Label;
            public string Percent;
        }

        /// <summary>
        /// 把列宽向上量化到 quantum 的整数倍并夹在最大宽度内。
        /// 数字小幅跳变不挪列，只有位数变了才跳一档；纯函数便于自检。
        /// </summary>
        public static float QuantizeColumn(
            float width, float quantum, float maxWidth)
        {
            if (quantum <= 0f)
            {
                quantum = 1f;
            }
            if (width < quantum)
            {
                width = quantum;
            }
            float result =
                (float)Math.Ceiling(width / quantum) * quantum;
            if (maxWidth > 0f && result > maxWidth)
            {
                result = maxWidth;
            }
            return result;
        }

        /// <summary>把两行文本拆成对齐用的四段，纯函数便于自检。</summary>
        public static void FormatLineParts(
            string upload,
            string download,
            int cpuPercent,
            int memoryPercent,
            out BarLine first,
            out BarLine second)
        {
            first = new BarLine();
            first.Arrow = "↑";
            first.Speed = upload;
            first.Label = "CPU";
            first.Percent = cpuPercent + "%";

            second = new BarLine();
            second.Arrow = "↓";
            second.Speed = download;
            second.Label = "内存";
            second.Percent = memoryPercent + "%";
        }

        /// <summary>
        /// 两行文案，和数据刷新分开，方便自检与预览复用。
        /// 绘制已改用 FormatLineParts 的分段布局，这里只保留文本形式。
        /// </summary>
        public static void FormatLines(
            string upload,
            string download,
            int cpuPercent,
            int memoryPercent,
            out string first,
            out string second)
        {
            first = "↑ " + upload + "   CPU: " + cpuPercent + "%";
            second = "↓ " + download + "   内存: " +
                memoryPercent + "%";
        }
    }
}
