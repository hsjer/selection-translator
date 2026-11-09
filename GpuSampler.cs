using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace SelectionTranslator
{
    /// <summary>
    /// GPU Engine 性能计数器采样。计数器类型是 Timer100Ns（累计忙碌
    /// 时间），单次读出的原始值没有意义，必须两次采样做差再除以流逝的
    /// 100ns 拍数才能得到 0-100 的利用率。所以本类维护上一次的原始值，
    /// Sample() 第一次调用只记底数、返回 null（尚未就绪）。
    /// 取不到计数器（老系统 / 无独显驱动）时始终返回 null，详情面板隐藏
    /// 对应行，不影响其它功能。
    /// </summary>
    internal sealed class GpuSampler
    {
        private Dictionary<string, long> _lastRaw;
        private long _lastTicks;

        /// <summary>
        /// 返回「实例名 → 利用率百分比」列表；尚未就绪或取不到时返回 null。
        /// </summary>
        public List<KeyValuePair<string, float>> Sample()
        {
            Dictionary<string, long> raw;
            if (!TryReadRaw(out raw))
            {
                _lastRaw = null;
                return null;
            }

            long ticks = DateTime.UtcNow.Ticks;
            if (_lastRaw == null || _lastTicks <= 0)
            {
                _lastRaw = raw;
                _lastTicks = ticks;
                return null;
            }

            long elapsed = ticks - _lastTicks;
            List<KeyValuePair<string, float>> values =
                new List<KeyValuePair<string, float>>();
            if (elapsed > 0)
            {
                foreach (KeyValuePair<string, long> pair in raw)
                {
                    long previous;
                    if (!_lastRaw.TryGetValue(pair.Key, out previous))
                    {
                        continue;
                    }
                    long busy = pair.Value - previous;
                    if (busy < 0)
                    {
                        continue;
                    }
                    // 1 tick = 100ns，elapsed 即流逝的 100ns 拍数。
                    double percent =
                        100.0 * busy / elapsed;
                    if (percent < 0)
                    {
                        percent = 0;
                    }
                    if (percent > 100)
                    {
                        percent = 100;
                    }
                    values.Add(new KeyValuePair<string, float>(
                        pair.Key, (float)percent));
                }
            }

            _lastRaw = raw;
            _lastTicks = ticks;
            return values;
        }

        private static bool TryReadRaw(
            out Dictionary<string, long> raw)
        {
            raw = null;
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    return false;
                }
                PerformanceCounterCategory category =
                    new PerformanceCounterCategory("GPU Engine");
                InstanceDataCollectionCollection data =
                    category.ReadCategory();
                InstanceDataCollection collection =
                    data["Utilization Percentage"];
                if (collection == null)
                {
                    return false;
                }
                raw = new Dictionary<string, long>();
                foreach (DictionaryEntry entry in collection)
                {
                    string name = entry.Key as string;
                    if (name == null)
                    {
                        continue;
                    }
                    InstanceData instance =
                        entry.Value as InstanceData;
                    if (instance == null)
                    {
                        continue;
                    }
                    raw[name] = instance.Sample.RawValue;
                }
                return raw.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
