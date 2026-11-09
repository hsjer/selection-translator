using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    /// <summary>traffic.json 的持久化字段（只存当日累计，不存网卡底数）。</summary>
    internal sealed class TrafficDayState
    {
        public string Date { get; set; }
        public long DayReceived { get; set; }
        public long DaySent { get; set; }
    }

    /// <summary>
    /// 按天累计的流量统计。网卡计数器是「开机以来」的累计值，
    /// 直接相减会把开机前的流量也算进来，所以这里对每张网卡单独记底数，
    /// 只累计差值；网卡消失/计数器回绕时只重新记底数、不计流量。
    /// 网卡底数只驻留内存（重启后自然重建），跨天/跨重启保留的是当日总量。
    /// </summary>
    internal sealed class TrafficDayTracker
    {
        private readonly string _path;
        private readonly Dictionary<string, long> _lastReceived =
            new Dictionary<string, long>();
        private readonly Dictionary<string, long> _lastSent =
            new Dictionary<string, long>();

        public string Date = "";
        public long DayReceived;
        public long DaySent;

        private TrafficDayTracker(string path)
        {
            _path = path;
        }

        public static TrafficDayTracker Load(string path)
        {
            TrafficDayTracker tracker = new TrafficDayTracker(path);
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    JavaScriptSerializer serializer =
                        new JavaScriptSerializer();
                    TrafficDayState state =
                        serializer.Deserialize<TrafficDayState>(json);
                    if (state != null)
                    {
                        tracker.Date = state.Date ?? "";
                        tracker.DayReceived = state.DayReceived;
                        tracker.DaySent = state.DaySent;
                    }
                }
            }
            catch
            {
            }
            return tracker;
        }

        public void Save()
        {
            try
            {
                TrafficDayState state = new TrafficDayState();
                state.Date = Date;
                state.DayReceived = DayReceived;
                state.DaySent = DaySent;
                JavaScriptSerializer serializer =
                    new JavaScriptSerializer();
                string json = serializer.Serialize(state);
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                if (File.Exists(_path))
                {
                    File.Replace(tempPath, _path, null);
                }
                else
                {
                    File.Move(tempPath, _path);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 用本次采样推进当日累计。首次见到的网卡只记底数、不计流量，
        /// 所以程序中途启动不会把开机以来的流量误算成「今天用的」。
        /// </summary>
        public void Update(
            DateTime now,
            IDictionary<string, long> received,
            IDictionary<string, long> sent)
        {
            string today = now.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (today != Date)
            {
                Date = today;
                DayReceived = 0;
                DaySent = 0;
            }
            DayReceived += Accumulate(_lastReceived, received);
            DaySent += Accumulate(_lastSent, sent);
        }

        private static long Accumulate(
            Dictionary<string, long> last,
            IDictionary<string, long> current)
        {
            long added = 0;
            foreach (KeyValuePair<string, long> pair in current)
            {
                long previous;
                if (last.TryGetValue(pair.Key, out previous))
                {
                    long delta = pair.Value - previous;
                    if (delta > 0)
                    {
                        added += delta;
                    }
                    // delta <= 0：网卡重启或计数器回绕，只更新底数。
                }
                last[pair.Key] = pair.Value;
            }
            return added;
        }
    }
}
