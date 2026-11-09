using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 用 AddClipboardFormatListener 收 WM_CLIPBOARDUPDATE。比定时轮询
    /// GetClipboardSequenceNumber 省 CPU，也不会漏掉复制后立刻又复制的情况。
    /// 监听窗口是 message-only，不出现在任务栏、不抢焦点。
    /// </summary>
    internal sealed class ClipboardListener : NativeWindow
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

        private bool _registered;

        public event EventHandler ClipboardChanged;

        public ClipboardListener()
        {
            CreateParams cp = new CreateParams();
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE
            cp.Caption = AppInfo.Id + ".ClipboardListener";
            CreateHandle(cp);
            try
            {
                _registered = AddClipboardFormatListener(Handle);
            }
            catch (Exception ex)
            {
                Log.Error("注册剪贴板监听失败", ex);
            }
        }

        public bool Registered
        {
            get { return _registered; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                EventHandler handler = ClipboardChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            base.WndProc(ref m);
        }

        // NativeWindow 自己有 Dispose，这里另起一个名字避免接口方法被隐藏。
        public void Close()
        {
            if (_registered)
            {
                try
                {
                    RemoveClipboardFormatListener(Handle);
                }
                catch
                {
                }
                _registered = false;
            }
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }

    /// <summary>
    /// 剪贴板历史的总控：监听 → 采样 → 入库 → 落盘 → 通知窗口刷新，
    /// 外加把历史写回系统剪贴板、按 TTL 定期清理。所有操作都在 UI 线程，
    /// 因此不需要锁（WinForms Timer / NativeWindow 都走同一个消息循环）。
    /// </summary>
    internal sealed class ClipboardManager : IDisposable
    {
        private static ClipboardManager _instance;

        public static ClipboardManager Instance
        {
            get { return _instance; }
        }

        public static ClipboardManager Start(AppConfig config)
        {
            if (_instance == null)
            {
                _instance = new ClipboardManager(
                    ClipboardStore.Load());
            }
            _instance.ApplyConfig(config);
            return _instance;
        }

        /// <summary>
        /// 诊断专用：用临时文件启动一份独立历史，绝不碰用户的
        /// clipboard.json（Stop 时会把内存里的内容落盘）。
        /// </summary>
        public static ClipboardManager StartForProbe(
            AppConfig config, string path, string imageDirectory)
        {
            Stop();
            _instance = new ClipboardManager(
                ClipboardStore.Load(path, imageDirectory));
            _instance.ApplyConfig(config);
            return _instance;
        }

        public static void Stop()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
        }

        private readonly ClipboardStore _store;
        private readonly System.Windows.Forms.Timer _captureTimer;
        private readonly System.Windows.Forms.Timer _purgeTimer;
        private ClipboardListener _listener;
        private bool _enabled;
        private int _ttlHours = ClipboardLogic.DefaultTtlHours;
        private int _maxItems = ClipboardLogic.DefaultMaxItems;
        private bool _disposed;

        // 双击复制时我们自己写剪贴板也会触发 WM_CLIPBOARDUPDATE，
        // 用一个短窗口把这条「自己造的」通知吃掉，避免历史自我复制。
        private DateTime _suppressUntilUtc = DateTime.MinValue;
        private int _suppressKind = -1;

        public event EventHandler HistoryChanged;

        private ClipboardManager(ClipboardStore store)
        {
            _store = store;

            _captureTimer = new System.Windows.Forms.Timer();
            _captureTimer.Interval = 160;
            _captureTimer.Tick += delegate
            {
                _captureTimer.Stop();
                CaptureNow();
            };

            _purgeTimer = new System.Windows.Forms.Timer();
            _purgeTimer.Interval = 60000;
            _purgeTimer.Tick += delegate
            {
                PurgeNow();
            };
            _purgeTimer.Start();
        }

        public ClipboardStore Store
        {
            get { return _store; }
        }

        public AppConfig Config { get; private set; }

        public bool Enabled
        {
            get { return _enabled; }
        }

        public int TtlHours
        {
            get { return _ttlHours; }
        }

        public int MaxItems
        {
            get { return _maxItems; }
        }

        public void ApplyConfig(AppConfig config)
        {
            if (config == null)
            {
                return;
            }
            Config = config;
            _enabled = config.EnableClipboardHistory;
            _ttlHours = ClipboardLogic.NormalizeTtl(
                config.ClipboardTtlHours);
            _maxItems = ClipboardLogic.ClampMaxItems(
                config.ClipboardMaxItems);

            if (_enabled && _listener == null)
            {
                try
                {
                    _listener = new ClipboardListener();
                    _listener.ClipboardChanged += OnClipboardChanged;
                }
                catch (Exception ex)
                {
                    Log.Error("启动剪贴板监听失败", ex);
                    _listener = null;
                }
            }
            else if (!_enabled && _listener != null)
            {
                _listener.ClipboardChanged -= OnClipboardChanged;
                _listener.Close();
                _listener = null;
                _captureTimer.Stop();
            }
            PurgeNow();
        }

        private void OnClipboardChanged(object sender, EventArgs e)
        {
            if (!_enabled || _disposed)
            {
                return;
            }
            // 延迟合并：一次 Ctrl+C 往往会连发多条通知，且延迟渲染的
            // 大图要过几十毫秒才真正可取。
            _captureTimer.Stop();
            _captureTimer.Start();
        }

        /// <summary>立即读一次系统剪贴板并入历史（也可供诊断调用）。</summary>
        public void CaptureNow()
        {
            if (!_enabled || _disposed)
            {
                return;
            }
            try
            {
                ClipboardItem item = ReadCurrentItem();
                if (item == null || IsSuppressed(item))
                {
                    return;
                }
                _store.Add(item);
                _store.Purge(
                    _ttlHours, _maxItems, DateTime.UtcNow);
                _store.Save();
                RaiseChanged();
            }
            catch (Exception ex)
            {
                // 剪贴板被别的进程独占、或内容正在延迟渲染，都属正常，
                // 下一条更新消息会再采样，不必打扰用户。
                Log.Error("采样剪贴板失败", ex);
            }
        }

        private bool IsSuppressed(ClipboardItem item)
        {
            return DateTime.UtcNow < _suppressUntilUtc &&
                item.Kind == _suppressKind;
        }

        private ClipboardItem ReadCurrentItem()
        {
            IDataObject data = Clipboard.GetDataObject();
            if (data == null)
            {
                return null;
            }

            ClipboardItem item = null;
            string[] files = null;
            try
            {
                files = data.GetData(DataFormats.FileDrop) as string[];
            }
            catch
            {
            }
            if (files != null && files.Length > 0)
            {
                item = BuildFileItem(files);
                if (item != null)
                {
                    return item;
                }
            }

            try
            {
                if (Clipboard.ContainsImage())
                {
                    using (Image image = Clipboard.GetImage())
                    {
                        item = BuildImageItem(image);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取剪贴板图片失败", ex);
                item = null;
            }
            if (item != null)
            {
                return item;
            }

            string text = "";
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    text = Clipboard.GetText(
                        TextDataFormat.UnicodeText) ?? "";
                }
                else if (Clipboard.ContainsText())
                {
                    text = Clipboard.GetText() ?? "";
                }
            }
            catch
            {
                text = "";
            }
            if (text.Trim().Length == 0)
            {
                return null;
            }
            if (text.Length > ClipboardLogic.MaxTextChars)
            {
                text = text.Substring(
                    0, ClipboardLogic.MaxTextChars);
            }
            item = new ClipboardItem();
            item.Kind = (int)ClipboardItemKind.Text;
            item.Text = text;
            item.TicksUtc = DateTime.UtcNow.Ticks;
            item.Fingerprint = ClipboardLogic.FingerprintFor(
                item.Kind, text, null, "");
            return item;
        }

        private ClipboardItem BuildFileItem(string[] files)
        {
            string[] clean = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                clean[i] = files[i] ?? "";
            }
            ClipboardItem item = new ClipboardItem();
            item.Kind = (int)ClipboardItemKind.File;
            item.Files = clean;
            item.Text = ClipboardLogic.Preview(item, 0);
            item.TicksUtc = DateTime.UtcNow.Ticks;
            item.Fingerprint = ClipboardLogic.FingerprintFor(
                item.Kind, "", clean, "");
            return item;
        }

        private ClipboardItem BuildImageItem(Image source)
        {
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return null;
            }
            int width = source.Width;
            int height = source.Height;
            double scale = Math.Min(
                1.0, 1600.0 / Math.Max(width, height));
            int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(height * scale));
            string path = _store.NewImagePath();
            using (Bitmap bitmap = new Bitmap(
                targetWidth, targetHeight, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode =
                        InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode =
                        PixelOffsetMode.HighQuality;
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, targetWidth, targetHeight),
                        new Rectangle(0, 0, width, height),
                        GraphicsUnit.Pixel);
                }
                bitmap.Save(path, ImageFormat.Png);
            }

            ClipboardItem item = new ClipboardItem();
            item.Kind = (int)ClipboardItemKind.Image;
            item.ImageFile = Path.GetFileName(path);
            item.ImageWidth = width;
            item.ImageHeight = height;
            item.Text = width + " × " + height + " 图像";
            item.TicksUtc = DateTime.UtcNow.Ticks;
            item.Fingerprint = "img:" + FileFingerprint(path);
            return item;
        }

        private static string FileFingerprint(string path)
        {
            using (MD5 md5 = MD5.Create())
            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = md5.ComputeHash(stream);
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        /// <summary>双击历史条目时把内容原样写回系统剪贴板。</summary>
        public bool CopyToClipboard(ClipboardItem item)
        {
            return CopyToClipboard(item, ClipboardWriteMode.Auto);
        }

        public bool CopyToClipboard(
            ClipboardItem item, ClipboardWriteMode mode)
        {
            if (item == null)
            {
                return false;
            }
            // 自己写进去的这条不应再被记录成一条新历史。
            _suppressKind = item.Kind;
            _suppressUntilUtc = DateTime.UtcNow.AddSeconds(1.5);
            return WriteToClipboard(_store, item, mode);
        }

        /// <summary>纯写回逻辑，抽出来供自检 / 探针直接调用。</summary>
        public static bool WriteToClipboard(
            ClipboardStore store, ClipboardItem item)
        {
            return WriteToClipboard(
                store, item, ClipboardWriteMode.Auto);
        }

        /// <summary>
        /// mode=Auto 时图片会同时带上「位图」和「文件列表」两种格式，
        /// 因此桌面 / 资源管理器也能直接粘贴成 PNG 文件。
        /// </summary>
        public static bool WriteToClipboard(
            ClipboardStore store,
            ClipboardItem item,
            ClipboardWriteMode mode)
        {
            return ClipboardPasteFiles.Write(store, item, mode);
        }

        public void SetFavorite(ClipboardItem item, bool favorite)
        {
            if (item == null)
            {
                return;
            }
            item.Favorite = favorite;
            _store.Save();
            RaiseChanged();
        }

        public void Remove(ClipboardItem item)
        {
            _store.Remove(item);
            _store.Save();
            RaiseChanged();
        }

        public void Clear(bool keepFavorites)
        {
            _store.Clear(keepFavorites);
            _store.Save();
            RaiseChanged();
        }

        public void PurgeNow()
        {
            if (_disposed)
            {
                return;
            }
            int removed = _store.Purge(
                _ttlHours, _maxItems, DateTime.UtcNow);
            if (removed > 0)
            {
                _store.Save();
                RaiseChanged();
            }
        }

        private void RaiseChanged()
        {
            EventHandler handler = HistoryChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _captureTimer.Stop();
            _purgeTimer.Stop();
            _captureTimer.Dispose();
            _purgeTimer.Dispose();
            if (_listener != null)
            {
                _listener.ClipboardChanged -= OnClipboardChanged;
                _listener.Close();
                _listener = null;
            }
            _store.Save();
        }
    }
}
