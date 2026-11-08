using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SelectionTranslator
{
    /// <summary>
    /// 极简 WAV 录音器：直接调用 winmm 的 waveIn API，采集
    /// 16kHz / 16bit / 单声道 PCM，输出带 44 字节标准头的 WAV。
    ///
    /// 之所以不用任何第三方音频库：本项目只用系统自带程序集编译，
    /// 拷到别的电脑上双击就能跑，不能引入 NAudio 之类的依赖。
    ///
    /// 录音数据全部留在内存里，最多 60 秒（约 1.9MB），
    /// 正好卡在腾讯云一句话识别 3MB 的上限之内。
    /// </summary>
    internal sealed class VoiceRecorder : IDisposable
    {
        public const int SampleRate = 16000;
        public const int BitsPerSample = 16;
        public const int ChannelCount = 1;
        public const int MaxSeconds = 60;

        public const int HeaderBytes = 44;

        private const int BytesPerSample = BitsPerSample / 8;
        private const int BlockAlign = ChannelCount * BytesPerSample;
        private const int ByteRate = SampleRate * BlockAlign;
        // 每个缓冲 100ms，6 个缓冲足够覆盖驱动调度抖动。
        private const int BufferBytes = ByteRate / 10;
        private const int BufferCount = 6;
        private const int MaxPcmBytes = ByteRate * MaxSeconds;

        private const int WaveMapper = -1;
        private const int CallbackFunction = 0x00030000;
        private const int MmWimData = 0x03C0;

        // WAVEHDR.dwFlags
        private const int WaveHdrDone = 0x00000008;
        private const int WaveHdrInQueue = 0x00000010;

        private const short WaveFormatPcm = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public short FormatTag;
            public short Channels;
            public int SamplesPerSec;
            public int AvgBytesPerSec;
            public short BlockAlign;
            public short BitsPerSample;
            public short Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr Data;
            public int BufferLength;
            public int BytesRecorded;
            public IntPtr User;
            public int Flags;
            public int Loops;
            public IntPtr Next;
            public IntPtr Reserved;
        }

        private delegate void WaveInProc(
            IntPtr handle,
            uint message,
            IntPtr instance,
            IntPtr param1,
            IntPtr param2);

        [DllImport("winmm.dll")]
        private static extern int waveInGetNumDevs();

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInOpen(
            out IntPtr handle,
            int deviceId,
            ref WaveFormatEx format,
            IntPtr callback,
            IntPtr instance,
            int flags);

        [DllImport("winmm.dll")]
        private static extern int waveInClose(IntPtr handle);

        [DllImport("winmm.dll")]
        private static extern int waveInPrepareHeader(
            IntPtr handle,
            IntPtr header,
            int headerSize);

        [DllImport("winmm.dll")]
        private static extern int waveInUnprepareHeader(
            IntPtr handle,
            IntPtr header,
            int headerSize);

        [DllImport("winmm.dll")]
        private static extern int waveInAddBuffer(
            IntPtr handle,
            IntPtr header,
            int headerSize);

        [DllImport("winmm.dll")]
        private static extern int waveInStart(IntPtr handle);

        [DllImport("winmm.dll")]
        private static extern int waveInStop(IntPtr handle);

        [DllImport("winmm.dll")]
        private static extern int waveInReset(IntPtr handle);

        private readonly object _sync = new object();
        private readonly MemoryStream _pcm = new MemoryStream();

        private IntPtr _handle;
        private IntPtr[] _headers;
        private IntPtr[] _buffers;
        private int _headerSize;
        private WaveInProc _callback;
        private volatile bool _recording;
        private float _level;
        private int _reuseFailures;
        private bool _disposed;

        // ------------------------------------------------------------------
        // 状态
        // ------------------------------------------------------------------

        public static int DeviceCount()
        {
            try
            {
                return waveInGetNumDevs();
            }
            catch (Exception ex)
            {
                Log.Error("枚举麦克风设备失败", ex);
                return 0;
            }
        }

        public bool IsRecording
        {
            get { return _recording; }
        }

        /// <summary>缓冲重新提交失败的次数，仅用于诊断。</summary>
        public int ReuseFailures
        {
            get { return _reuseFailures; }
        }

        /// <summary>0..1 的峰值电平，供录音浮条画波动条。</summary>
        public float Level
        {
            get
            {
                lock (_sync)
                {
                    return _level;
                }
            }
        }

        public TimeSpan RecordedDuration
        {
            get
            {
                lock (_sync)
                {
                    return TimeSpan.FromMilliseconds(
                        _pcm.Length * 1000.0 / ByteRate);
                }
            }
        }

        // ------------------------------------------------------------------
        // 开始 / 停止
        // ------------------------------------------------------------------

        public void Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("VoiceRecorder");
            }
            if (_handle != IntPtr.Zero)
            {
                throw new InvalidOperationException("录音已经开始。");
            }
            if (DeviceCount() <= 0)
            {
                throw new TranslationException(
                    "没有检测到麦克风。请插好麦克风后重试。",
                    "NO_MIC");
            }

            WaveFormatEx format = new WaveFormatEx();
            format.FormatTag = WaveFormatPcm;
            format.Channels = (short)ChannelCount;
            format.SamplesPerSec = SampleRate;
            format.AvgBytesPerSec = ByteRate;
            format.BlockAlign = (short)BlockAlign;
            format.BitsPerSample = (short)BitsPerSample;
            format.Size = 0;

            // 委托必须由字段强引用住，否则 GC 回收后驱动回调会崩。
            _callback = new WaveInProc(OnWaveMessage);
            IntPtr callbackPointer =
                Marshal.GetFunctionPointerForDelegate(_callback);

            IntPtr handle;
            int result = waveInOpen(
                out handle,
                WaveMapper,
                ref format,
                callbackPointer,
                IntPtr.Zero,
                CallbackFunction);
            if (result != 0)
            {
                _callback = null;
                throw new TranslationException(
                    "无法打开麦克风：" + DescribeWaveError(result),
                    "MIC_OPEN_" + result);
            }

            _handle = handle;
            try
            {
                AllocateBuffers();
            }
            catch
            {
                ReleaseBuffers(_handle);
                waveInClose(_handle);
                _handle = IntPtr.Zero;
                _callback = null;
                throw;
            }

            lock (_sync)
            {
                _pcm.SetLength(0);
                _level = 0f;
            }
            _recording = true;

            result = waveInStart(_handle);
            if (result != 0)
            {
                _recording = false;
                Stop();
                throw new TranslationException(
                    "无法开始录音：" + DescribeWaveError(result),
                    "MIC_START_" + result);
            }

            Log.Info("开始录音：16000Hz 16bit mono");
        }

        /// <summary>停止录音并返回完整 WAV 字节（含头）。</summary>
        public byte[] Stop()
        {
            _recording = false;

            IntPtr handle = _handle;
            _handle = IntPtr.Zero;

            if (handle != IntPtr.Zero)
            {
                try
                {
                    waveInStop(handle);
                }
                catch
                {
                }
                try
                {
                    waveInReset(handle);
                }
                catch
                {
                }

                // waveInReset 会为尚未归还的缓冲补发 WIM_DATA 回调，
                // 等一小会儿再释放内存，避免回调访问已释放的指针。
                Thread.Sleep(30);
                ReleaseBuffers(handle);
                try
                {
                    waveInClose(handle);
                }
                catch
                {
                }
            }

            byte[] pcm;
            lock (_sync)
            {
                pcm = _pcm.ToArray();
                _level = 0f;
            }
            Log.Info("停止录音：pcm=" + pcm.Length + " bytes");
            return WrapWav(pcm);
        }

        private void AllocateBuffers()
        {
            _headerSize = Marshal.SizeOf(typeof(WaveHdr));
            _headers = new IntPtr[BufferCount];
            _buffers = new IntPtr[BufferCount];

            for (int i = 0; i < BufferCount; i++)
            {
                _buffers[i] = Marshal.AllocHGlobal(BufferBytes);
                _headers[i] = Marshal.AllocHGlobal(_headerSize);

                WaveHdr header = new WaveHdr();
                header.Data = _buffers[i];
                header.BufferLength = BufferBytes;
                Marshal.StructureToPtr(header, _headers[i], false);

                int result = waveInPrepareHeader(
                    _handle, _headers[i], _headerSize);
                if (result != 0)
                {
                    throw new TranslationException(
                        "准备录音缓冲失败：" + DescribeWaveError(result),
                        "MIC_PREPARE_" + result);
                }

                result = waveInAddBuffer(
                    _handle, _headers[i], _headerSize);
                if (result != 0)
                {
                    throw new TranslationException(
                        "提交录音缓冲失败：" + DescribeWaveError(result),
                        "MIC_BUFFER_" + result);
                }
            }
        }

        private void ReleaseBuffers(IntPtr handle)
        {
            if (_headers != null)
            {
                for (int i = 0; i < _headers.Length; i++)
                {
                    if (_headers[i] == IntPtr.Zero)
                    {
                        continue;
                    }
                    if (handle != IntPtr.Zero)
                    {
                        // 极少数驱动归还缓冲较慢，重试几次再放弃。
                        for (int attempt = 0; attempt < 20; attempt++)
                        {
                            int result = waveInUnprepareHeader(
                                handle, _headers[i], _headerSize);
                            if (result == 0)
                            {
                                break;
                            }
                            Thread.Sleep(10);
                        }
                    }
                    Marshal.FreeHGlobal(_headers[i]);
                    _headers[i] = IntPtr.Zero;
                }
                _headers = null;
            }

            if (_buffers != null)
            {
                for (int i = 0; i < _buffers.Length; i++)
                {
                    if (_buffers[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_buffers[i]);
                        _buffers[i] = IntPtr.Zero;
                    }
                }
                _buffers = null;
            }
        }

        // ------------------------------------------------------------------
        // 驱动回调
        // ------------------------------------------------------------------

        private void OnWaveMessage(
            IntPtr handle,
            uint message,
            IntPtr instance,
            IntPtr param1,
            IntPtr param2)
        {
            if (message != MmWimData || param1 == IntPtr.Zero)
            {
                return;
            }

            try
            {
                WaveHdr header = (WaveHdr)Marshal.PtrToStructure(
                    param1, typeof(WaveHdr));

                int count = header.BytesRecorded;
                if (count > header.BufferLength)
                {
                    count = header.BufferLength;
                }
                if (count > 0 && header.Data != IntPtr.Zero)
                {
                    byte[] chunk = new byte[count];
                    Marshal.Copy(header.Data, chunk, 0, count);
                    Append(chunk);
                    UpdateLevel(chunk, count);
                }

                if (!_recording || _handle == IntPtr.Zero)
                {
                    return;
                }

                // 复用同一块缓冲：只清除 DONE / INQUEUE，保留 PREPARED。
                // 把 dwFlags 整体清零会让驱动认为缓冲「未准备」，于是
                // 后续 waveInAddBuffer 静默失败——录音就只剩头 600ms。
                header.BytesRecorded = 0;
                header.Flags = header.Flags &
                    ~(WaveHdrDone | WaveHdrInQueue);
                Marshal.StructureToPtr(header, param1, false);

                int result = waveInPrepareHeader(
                    _handle, param1, _headerSize);
                if (result == 0)
                {
                    result = waveInAddBuffer(
                        _handle, param1, _headerSize);
                }
                if (result != 0)
                {
                    Interlocked.Increment(ref _reuseFailures);
                }
            }
            catch (Exception ex)
            {
                Log.Error("录音回调失败", ex);
            }
        }

        private void Append(byte[] chunk)
        {
            lock (_sync)
            {
                if (_pcm.Length >= MaxPcmBytes)
                {
                    return;
                }
                int allowed = chunk.Length;
                long remaining = MaxPcmBytes - _pcm.Length;
                if (allowed > remaining)
                {
                    allowed = (int)remaining;
                }
                _pcm.Write(chunk, 0, allowed);
            }
        }

        private void UpdateLevel(byte[] chunk, int count)
        {
            int peak = 0;
            for (int i = 0; i + 1 < count; i += 2)
            {
                short sample = (short)(chunk[i] | (chunk[i + 1] << 8));
                int abs = sample < 0 ? -sample : sample;
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            float level = peak / 32768f;
            lock (_sync)
            {
                // 慢升快降，视觉上更像真电平表。
                float target = level;
                _level = _level * 0.35f + target * 0.65f;
            }
        }

        // ------------------------------------------------------------------
        // WAV 封装
        // ------------------------------------------------------------------

        /// <summary>给裸 PCM 加 44 字节标准 RIFF/WAVE 头。</summary>
        internal static byte[] WrapWav(byte[] pcm)
        {
            int dataLength = pcm == null ? 0 : pcm.Length;
            byte[] wav = new byte[HeaderBytes + dataLength];

            WriteAscii(wav, 0, "RIFF");
            WriteInt32(wav, 4, 36 + dataLength);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteInt32(wav, 16, 16);
            WriteInt16(wav, 20, WaveFormatPcm);
            WriteInt16(wav, 22, ChannelCount);
            WriteInt32(wav, 24, SampleRate);
            WriteInt32(wav, 28, ByteRate);
            WriteInt16(wav, 32, BlockAlign);
            WriteInt16(wav, 34, BitsPerSample);
            WriteAscii(wav, 36, "data");
            WriteInt32(wav, 40, dataLength);

            if (dataLength > 0)
            {
                Buffer.BlockCopy(pcm, 0, wav, HeaderBytes, dataLength);
            }
            return wav;
        }

        /// <summary>按 WAV 头推算音频毫秒数（自检与状态显示共用）。</summary>
        internal static int DurationMs(int wavBytes)
        {
            int dataLength = wavBytes - HeaderBytes;
            if (dataLength <= 0)
            {
                return 0;
            }
            return dataLength * 1000 / ByteRate;
        }

        private static void WriteAscii(byte[] buffer, int offset, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteInt16(byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static string DescribeWaveError(int code)
        {
            switch (code)
            {
                case 2:
                    return "设备 ID 无效";
                case 4:
                    return "驱动不支持该操作";
                case 5:
                    return "内存不足";
                case 6:
                    return "驱动不支持该功能";
                case 7:
                    return "设备已被其他程序独占";
                case 8:
                    return "音频格式不被支持";
                case 32:
                    return "麦克风不支持 16kHz 录音";
                case 33:
                    return "缓冲正在使用中";
                default:
                    return "Win32 错误码 " + code;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                Log.Error("释放录音器失败", ex);
            }
            lock (_sync)
            {
                _pcm.Dispose();
            }
            _callback = null;
        }
    }
}
