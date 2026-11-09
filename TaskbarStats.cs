using System;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

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
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
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

        /// <summary>两行文案，和数据刷新分开，方便自检与预览复用。</summary>
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
