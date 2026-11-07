using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const int HotkeyId = 1;
        private const int HotkeyIdManual = 2;
        private const int HotkeyIdScreenshot = 3;

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _translateMenuItem;
        private readonly ToolStripMenuItem _manualMenuItem;
        private readonly ToolStripMenuItem _screenshotMenuItem;
        private readonly Icon _trayIconImage;
        private readonly HotkeyWindow _hotkeyWindow;
        private readonly System.Windows.Forms.Timer _firstRunTimer;

        private AppConfig _config;
        private int _hotkeyTranslateModifiers;
        private Keys _hotkeyTranslateKey;
        private int _hotkeyManualModifiers;
        private Keys _hotkeyManualKey;
        private int _hotkeyScreenshotModifiers;
        private Keys _hotkeyScreenshotKey;
        private RightClickWatcher _rightClickWatcher;
        private SelectionMonitor _selectionMonitor;
        private TranslateBubbleForm _bubble;
        private TranslationForm _translationForm;
        private ScreenshotEditorForm _screenshotEditor;
        private ToolStripMenuItem _closePinsItem;
        // 防止热键在浮层显示期间重复触发、嵌套打开第二个截图会话。
        private bool _captureInProgress;
        private SettingsForm _settingsForm;
        private bool _started;
        private bool _busy;
        private bool _disposed;

        // 右键按下时就立刻开始读取选中文本，避免原生右键菜单弹出后
        // UIA 在鼠标位置读到的是菜单窗口而不是文本控件。
        private Task<string> _rightClickCaptureTask;
        private Task<string> _rightClickClipboardTask;
        private Point _rightClickCapturePoint;
        private SynchronizationContext _uiContext;

        public TrayApplicationContext()
        {
            _config = ConfigStore.Load();
            _config.StartWithWindows = StartupManager.IsEnabled();

            _trayIconImage = IconFactory.Create();
            _trayMenu = new ContextMenuStrip();
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = _trayIconImage;
            _trayIcon.Text = "选中文本翻译";
            _trayIcon.Visible = true;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.DoubleClick += OnTrayDoubleClick;

            _translateMenuItem =
                new ToolStripMenuItem("翻译选中文本");
            _translateMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyTranslateSelection;
            _translateMenuItem.Click += OnTranslateSelectionClicked;
            _trayMenu.Items.Add(_translateMenuItem);

            _manualMenuItem =
                new ToolStripMenuItem("手动翻译…");
            _manualMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyManualInput;
            _manualMenuItem.Click += OnManualInputClicked;
            _trayMenu.Items.Add(_manualMenuItem);

            _screenshotMenuItem =
                new ToolStripMenuItem("截图翻译…");
            _screenshotMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyScreenshot;
            _screenshotMenuItem.Click += OnScreenshotClicked;
            _trayMenu.Items.Add(_screenshotMenuItem);

            ToolStripMenuItem clipboardItem =
                new ToolStripMenuItem("翻译剪贴板");
            clipboardItem.Click += OnTranslateClipboardClicked;
            _trayMenu.Items.Add(clipboardItem);

            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem settingsItem =
                new ToolStripMenuItem("设置…");
            settingsItem.Click += OnSettingsClicked;
            _trayMenu.Items.Add(settingsItem);

            _startupItem = new ToolStripMenuItem("开机自动启动");
            _startupItem.CheckOnClick = true;
            _startupItem.Checked = _config.StartWithWindows;
            _startupItem.Click += OnStartupClicked;
            _trayMenu.Items.Add(_startupItem);

            _closePinsItem = new ToolStripMenuItem("关闭所有桌面贴图");
            _closePinsItem.Enabled = false;
            _closePinsItem.Click += delegate
            {
                PinnedImageForm.CloseAll();
                _closePinsItem.Enabled = false;
            };
            _trayMenu.Items.Add(_closePinsItem);

            // 贴图数量变化时同步菜单可用状态。
            PinnedImageForm.CountChanged += delegate(int count)
            {
                if (_closePinsItem != null && !_closePinsItem.IsDisposed)
                {
                    _closePinsItem.Enabled = count > 0;
                }
            };

            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += OnExitClicked;
            _trayMenu.Items.Add(exitItem);

            _hotkeyWindow = new HotkeyWindow();
            _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
            RegisterHotkeys();

            Application.Idle += OnFirstIdle;

            _firstRunTimer = new System.Windows.Forms.Timer();
            _firstRunTimer.Interval = 800;
            _firstRunTimer.Tick += OnFirstRunTimerTick;
            if (!HasCredentials())
            {
                _firstRunTimer.Start();
            }
        }

        private bool HasCredentials()
        {
            return !string.IsNullOrEmpty((_config.AppId ?? "").Trim()) &&
                !string.IsNullOrEmpty(_config.GetSecret());
        }

        /// <summary>
        /// 从配置解析快捷键并注册。两个快捷键共用 MOD_NOREPEAT。
        /// </summary>
        private void RegisterHotkeys()
        {
            int modifiers;
            Keys key;

            if (HotkeyParser.TryParse(
                _config.HotkeyTranslateSelection,
                out modifiers,
                out key))
            {
                _hotkeyTranslateModifiers =
                    modifiers | NativeMethods.MOD_NOREPEAT;
                _hotkeyTranslateKey = key;
                bool ok = _hotkeyWindow.Register(
                    HotkeyId,
                    _hotkeyTranslateModifiers,
                    key);
                if (!ok)
                {
                    Log.Info("全局快捷键 " +
                        _config.HotkeyTranslateSelection +
                        " 注册失败，可能已被其他程序占用。");
                }
            }
            else
            {
                Log.Info("快捷键格式无效：" +
                    _config.HotkeyTranslateSelection);
            }

            if (HotkeyParser.TryParse(
                _config.HotkeyManualInput,
                out modifiers,
                out key))
            {
                _hotkeyManualModifiers =
                    modifiers | NativeMethods.MOD_NOREPEAT;
                _hotkeyManualKey = key;
                bool ok = _hotkeyWindow.Register(
                    HotkeyIdManual,
                    _hotkeyManualModifiers,
                    key);
                if (!ok)
                {
                    Log.Info("全局快捷键 " +
                        _config.HotkeyManualInput +
                        " 注册失败，可能已被其他程序占用。");
                }
            }
            if (HotkeyParser.TryParse(
                _config.HotkeyScreenshot,
                out modifiers,
                out key))
            {
                _hotkeyScreenshotModifiers =
                    modifiers | NativeMethods.MOD_NOREPEAT;
                _hotkeyScreenshotKey = key;
                bool ok = _hotkeyWindow.Register(
                    HotkeyIdScreenshot,
                    _hotkeyScreenshotModifiers,
                    key);
                if (!ok)
                {
                    Log.Info("全局快捷键 " +
                        _config.HotkeyScreenshot +
                        " 注册失败，可能已被其他程序占用。");
                }
            }
            else
            {
                Log.Info("快捷键格式无效：" +
                    _config.HotkeyScreenshot);
            }
        }

        /// <summary>
        /// 注销当前快捷键。
        /// </summary>
        private void UnregisterHotkeys()
        {
            _hotkeyWindow.Unregister(HotkeyId);
            _hotkeyWindow.Unregister(HotkeyIdManual);
            _hotkeyWindow.Unregister(HotkeyIdScreenshot);
        }

        /// <summary>
        /// 配置变更时重新注册快捷键。
        /// </summary>
        private void ReRegisterHotkeys()
        {
            UnregisterHotkeys();
            RegisterHotkeys();
            _translateMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyTranslateSelection;
            _manualMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyManualInput;
            _screenshotMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyScreenshot;
        }

        private void OnFirstIdle(object sender, EventArgs e)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            Application.Idle -= OnFirstIdle;

            SynchronizationContext context = SynchronizationContext.Current;
            if (context == null)
            {
                context = new WindowsFormsSynchronizationContext();
            }
            _uiContext = context;

            _rightClickWatcher = new RightClickWatcher(context);
            _rightClickWatcher.RightButtonDown += OnRightButtonDown;
            _rightClickWatcher.RightClicked += OnRightClicked;
            _rightClickWatcher.LeftButtonDown += OnLeftButtonDown;
            if (_config.EnableRightClick)
            {
                _rightClickWatcher.Start();
                Log.Info(_rightClickWatcher.IsRunning
                    ? "右键监听已启动"
                    : "右键监听启动失败");
            }

            if (_config.ShowBubbleOnSelection)
            {
                _selectionMonitor = new SelectionMonitor(
                    context,
                    _config.MaxTextLength);
                _selectionMonitor.SelectionChanged += OnSelectionChanged;
                _selectionMonitor.Start();
                Log.Info(_selectionMonitor.IsRunning
                    ? "选中监听已启动"
                    : "选中监听启动失败");
            }
        }

        private void OnFirstRunTimerTick(object sender, EventArgs e)
        {
            _firstRunTimer.Stop();
            if (!HasCredentials())
            {
                ShowSettings();
            }
        }

        private void OnRightButtonDown(
            object sender,
            RightClickEventArgs e)
        {
            if (_disposed || !_config.EnableRightClick)
            {
                return;
            }
            if (IsOwnPoint(e.Location))
            {
                return;
            }

            _rightClickCapturePoint = e.Location;
            _rightClickClipboardTask = null;

            // 先在后台线程定位元素并读取选区。不能直接在鼠标钩子里调用
            // UIA，否则可能因为输入同步调用而失败。
            _rightClickCaptureTask =
                SelectionCapture.GetSelectedTextViaProbeAsync(
                    e.Location.X,
                    e.Location.Y,
                    _config.MaxTextLength);

            // 剪贴板操作绝对不能放在鼠标钩子里：钩子回调运行在 UI 线程，
            // 一旦被剪贴板阻塞，系统会认为钩子超时并直接卸载它。
            // 这里只投递到消息队列，钩子立即返回，消息会在右键松开、
            // 原生菜单弹出之前被处理。
            SynchronizationContext context = _uiContext;
            if (context != null)
            {
                context.Post(delegate { StartClipboardFallback(); }, null);
            }
        }

        /// <summary>
        /// 在 UI 线程（STA）上发送 Ctrl+C 读取选区。适用于 IDEA、
        /// Java Swing、部分 Electron/浏览器等 UIA 读不到选区的应用。
        /// </summary>
        private void StartClipboardFallback()
        {
            if (_disposed ||
                !_config.EnableRightClick ||
                !_config.EnableClipboardFallback)
            {
                return;
            }
            if (!ShouldUseClipboardFallback())
            {
                return;
            }

            _rightClickClipboardTask = ClipboardHelper.CopySelectionAsync();
        }

        private bool ShouldUseClipboardFallback()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    return false;
                }

                uint processId;
                NativeMethods.GetWindowThreadProcessId(
                    foreground,
                    out processId);
                if (processId == 0)
                {
                    return false;
                }

                string name = System.Diagnostics.Process
                    .GetProcessById((int)processId)
                    .ProcessName.ToLowerInvariant();

                if (name == "selectiontranslator")
                {
                    return false;
                }

                string[] terminals = new string[]
                {
                    "windowsterminal",
                    "conhost",
                    "openconsole",
                    "cmd",
                    "powershell",
                    "pwsh",
                    "mintty",
                    "bash",
                    "wsl"
                };
                foreach (string terminal in terminals)
                {
                    if (name == terminal)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("判断是否使用剪贴板兜底失败", ex);
                return true;
            }
        }

        private static string GetForegroundProcessName()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    return "?";
                }

                uint processId;
                NativeMethods.GetWindowThreadProcessId(
                    foreground,
                    out processId);
                if (processId == 0)
                {
                    return "?";
                }

                return System.Diagnostics.Process
                    .GetProcessById((int)processId)
                    .ProcessName;
            }
            catch
            {
                return "?";
            }
        }

        private async void OnRightClicked(
            object sender,
            RightClickEventArgs e)
        {
            if (_disposed || !_config.EnableRightClick)
            {
                return;
            }
            if (IsOwnPoint(e.Location))
            {
                return;
            }

            try
            {
                Task<string> capture = _rightClickCaptureTask;
                Task<string> clipboard = _rightClickClipboardTask;
                _rightClickCaptureTask = null;
                _rightClickClipboardTask = null;

                // 正常情况下使用右键按下时启动的捕获任务；如果按下事件
                // 没有触发（例如监听刚启动），退回到按当前位置重新读取。
                if (capture == null ||
                    Math.Abs(e.Location.X - _rightClickCapturePoint.X) > 6 ||
                    Math.Abs(e.Location.Y - _rightClickCapturePoint.Y) > 6)
                {
                    capture = SelectionCapture.GetSelectedTextAsync(
                        e.Location.X,
                        e.Location.Y,
                        _config.MaxTextLength);
                }

                string text = "";
                string source = "无";
                if (clipboard == null)
                {
                    text = await capture;
                    if (!string.IsNullOrEmpty(text))
                    {
                        source = "UIA";
                    }
                }
                else
                {
                    // 两个来源谁先拿到文本就用谁，避免被较慢的一方拖住。
                    Task<string> first = await Task.WhenAny(capture, clipboard);
                    text = await first;
                    if (!string.IsNullOrEmpty(text))
                    {
                        source = first == capture ? "UIA" : "剪贴板";
                    }
                    else
                    {
                        Task<string> other = first == capture
                            ? clipboard
                            : capture;
                        text = await other;
                        if (!string.IsNullOrEmpty(text))
                        {
                            source = other == capture ? "UIA" : "剪贴板";
                        }
                    }
                }

                Log.Info(
                    "右键：进程=" + GetForegroundProcessName() +
                    " 来源=" + source +
                    " 长度=" + (text == null ? 0 : text.Length));

                if (!string.IsNullOrEmpty(text) &&
                    _config.MaxTextLength > 0 &&
                    text.Length > _config.MaxTextLength)
                {
                    text = text.Substring(0, _config.MaxTextLength);
                }
                if (_disposed || string.IsNullOrEmpty(text))
                {
                    return;
                }
                ShowBubble(text, e.Location);
            }
            catch (Exception ex)
            {
                Log.Error("处理右键选中文本失败", ex);
            }
        }

        private void ShowBubble(string text, Point location)
        {
            if (_bubble != null && !_bubble.IsDisposed)
            {
                _bubble.Close();
            }

            _bubble = new TranslateBubbleForm(
                text,
                location,
                OnBubbleClicked);
            _bubble.FormClosed += delegate { _bubble = null; };
            _bubble.Show();
            // 不再主动关闭原生右键菜单：菜单会「闪一下就消失」，
            // 用户也没法继续使用复制 / 粘贴等菜单项。气泡点击由
            // 低级鼠标钩子直接拦截（OnLeftButtonDown），不需要关菜单。
        }

        private void OnBubbleClicked(string text, Point location)
        {
            Log.Info("气泡点击：窗口");
            // 用户已明确选择翻译，此时才收起残留的原生菜单。
            NativeMethods.CloseActiveMenu();
            ShowTranslation(text, location);
        }

        private void OnLeftButtonDown(object sender, RightClickEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            TranslateBubbleForm bubble = _bubble;
            if (bubble == null || bubble.IsDisposed || !bubble.Visible)
            {
                return;
            }
            if (!bubble.Bounds.Contains(e.Location))
            {
                return;
            }

            // 原生右键菜单还开着时，第一次点击会被菜单吞掉。
            // 这里直接通过鼠标钩子处理气泡点击，并吞掉这次左键，
            // 避免点击穿透到下面的应用。
            e.Handled = true;

            string text = bubble.SelectedText;
            Point location = bubble.Location;
            bubble.Close();
            _bubble = null;

            Log.Info("气泡点击：钩子");
            NativeMethods.CloseActiveMenu();
            ShowTranslation(text, location);
        }

        private async void OnTranslateSelectionClicked(
            object sender,
            EventArgs e)
        {
            await TranslateSelectionAsync();
        }

        private async void OnHotkeyPressed(object sender, int id)
        {
            if (id == HotkeyIdManual)
            {
                OpenManualInput();
                return;
            }
            if (id == HotkeyIdScreenshot)
            {
                OpenScreenshotEditor();
                return;
            }
            await TranslateSelectionAsync();
        }

        private void OnManualInputClicked(object sender, EventArgs e)
        {
            OpenManualInput();
        }

        private void OnScreenshotClicked(object sender, EventArgs e)
        {
            OpenScreenshotEditor();
        }

        /// <summary>
        /// 截屏浮层 → 截图编辑器。浮层内已完成框选与标注，
        /// 「完成」进入编辑器，「翻译图片」进入编辑器并自动识别文字。
        /// </summary>
        private void OpenScreenshotEditor()
        {
            // 一次快捷键只对应一次截图；浮层显示期间忽略重复触发。
            if (_disposed || _captureInProgress)
            {
                return;
            }
            _captureInProgress = true;

            // 已有编辑器在则先关闭，避免打开多个实例。
            if (_screenshotEditor != null &&
                !_screenshotEditor.IsDisposed)
            {
                _screenshotEditor.Close();
            }

            // 截图期间必须暂停全局右键钩子与选区监听：否则右键拖拽
            // 会触发「译」浮窗与 UIA 探测，导致画面闪烁甚至抢焦点。
            bool watcherRunning = _rightClickWatcher != null &&
                _rightClickWatcher.IsRunning;
            bool monitorRunning = _selectionMonitor != null &&
                _selectionMonitor.IsRunning;
            if (watcherRunning)
            {
                _rightClickWatcher.Stop();
            }
            if (monitorRunning)
            {
                _selectionMonitor.Stop();
            }

            Bitmap image = null;
            Bitmap rawImage = null;
            Point pinLocation = Point.Empty;
            ScreenshotCaptureForm.CaptureResult action =
                ScreenshotCaptureForm.CaptureResult.None;
            // 贴图窗口在截图期间先隐藏，避免被拍进新截图（QQ 的行为）。
            int hiddenPins = 0;
            try
            {
                hiddenPins = PinnedImageForm.HideAllForCapture();

                // 让托盘菜单 / 贴图窗口先彻底收起，避免被拍进截图。
                Application.DoEvents();

                using (ScreenshotCaptureForm capture =
                    new ScreenshotCaptureForm())
                {
                    DialogResult dialogResult = capture.ShowDialog();
                    if (dialogResult == DialogResult.OK &&
                        capture.Captured != null)
                    {
                        image = capture.Captured;
                        rawImage = capture.RawCaptured;
                        action = capture.Result;
                        pinLocation = capture.CaptureLocation;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("截图失败", ex);
            }
            finally
            {
                if (hiddenPins > 0)
                {
                    PinnedImageForm.ShowAllAfterCapture();
                }
                if (watcherRunning && !_disposed)
                {
                    _rightClickWatcher.Start();
                }
                if (monitorRunning && !_disposed)
                {
                    _selectionMonitor.Start();
                }
                _captureInProgress = false;
            }

            if (image == null || _disposed)
            {
                if (image != null)
                {
                    image.Dispose();
                }
                if (rawImage != null)
                {
                    rawImage.Dispose();
                }
                return;
            }

            // 「钉在桌面」：把合成图放进无边框置顶小窗，不打开编辑器。
            if (action == ScreenshotCaptureForm.CaptureResult.Pin)
            {
                try
                {
                    PinnedImageForm pin =
                        new PinnedImageForm(image, pinLocation);
                    image = null; // 位图所有权交给贴图窗口
                    if (rawImage != null)
                    {
                        rawImage.Dispose();
                        rawImage = null;
                    }
                    pin.Show();
                }
                catch (Exception ex)
                {
                    Log.Error("贴图失败", ex);
                    if (image != null)
                    {
                        image.Dispose();
                    }
                    if (rawImage != null)
                    {
                        rawImage.Dispose();
                    }
                }
                return;
            }

            try
            {
                _screenshotEditor =
                    new ScreenshotEditorForm(
                        delegate { return _config; },
                        image, rawImage);
                // 位图由编辑器在关闭时自行释放。
                _screenshotEditor.FormClosed += delegate
                {
                    _screenshotEditor = null;
                };
                if (action == ScreenshotCaptureForm.CaptureResult.Translate)
                {
                    _screenshotEditor.BeginTranslation();
                }
                _screenshotEditor.Show();
                _screenshotEditor.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("打开截图编辑器失败", ex);
                image.Dispose();
                if (rawImage != null)
                {
                    rawImage.Dispose();
                }
            }
        }

        /// <summary>
        /// 弹出手动输入翻译窗口，预填空文本、光标聚焦、回车即翻译。
        /// </summary>
        private void OpenManualInput()
        {
            if (_disposed)
            {
                return;
            }

            if (_translationForm != null && !_translationForm.IsDisposed)
            {
                _translationForm.Close();
            }

            _translationForm = new TranslationForm(
                _config,
                "",
                Cursor.Position,
                ShowSettings,
                manualInput: true);
            _translationForm.FormClosed += delegate
            {
                _translationForm = null;
            };
            _translationForm.Show();
            _translationForm.Activate();
        }

        /// <summary>
        /// 选中文本变化时自动显示「译」气泡。
        /// </summary>
        private void OnSelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_disposed || _busy)
            {
                return;
            }
            if (string.IsNullOrEmpty(e.Text))
            {
                return;
            }
            if (IsOwnPoint(e.Location))
            {
                return;
            }
            ShowBubble(e.Text, e.Location);
        }

        private async System.Threading.Tasks.Task TranslateSelectionAsync()
        {
            if (_disposed || _busy)
            {
                return;
            }

            _busy = true;
            try
            {
                string text = await ClipboardHelper.CopySelectionAsync();
                if (_disposed)
                {
                    return;
                }
                if (string.IsNullOrEmpty(text))
                {
                    _trayIcon.ShowBalloonTip(
                        2500,
                        "选中文本翻译",
                        "没有检测到选中的文本。请先选中文字，或使用托盘菜单的「翻译剪贴板」。",
                        ToolTipIcon.Info);
                    return;
                }
                ShowTranslation(text, Cursor.Position);
            }
            catch (Exception ex)
            {
                Log.Error("翻译选中文本失败", ex);
                _trayIcon.ShowBalloonTip(
                    3000,
                    "选中文本翻译",
                    "读取选中文本失败：" + ex.Message,
                    ToolTipIcon.Warning);
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnTranslateClipboardClicked(object sender, EventArgs e)
        {
            string text = ClipboardHelper.GetText();
            if (string.IsNullOrEmpty(text))
            {
                _trayIcon.ShowBalloonTip(
                    2500,
                    "选中文本翻译",
                    "剪贴板中没有文本。",
                    ToolTipIcon.Info);
                return;
            }
            ShowTranslation(text, Cursor.Position);
        }

        private void ShowTranslation(string text, Point location)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_translationForm != null && !_translationForm.IsDisposed)
            {
                _translationForm.Close();
            }

            _translationForm = new TranslationForm(
                _config,
                text,
                location,
                ShowSettings);
            _translationForm.FormClosed += delegate
            {
                _translationForm = null;
            };
            _translationForm.Show();
            _translationForm.Activate();
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private void ShowSettings()
        {
            if (_disposed)
            {
                return;
            }

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Activate();
                return;
            }

            // 翻译窗口是 TopMost，先关掉，避免挡住设置窗口。
            if (_bubble != null && !_bubble.IsDisposed)
            {
                _bubble.Close();
                _bubble = null;
            }
            if (_translationForm != null && !_translationForm.IsDisposed)
            {
                _translationForm.Close();
                _translationForm = null;
            }

            SettingsForm form = new SettingsForm(_config);
            _settingsForm = form;
            try
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    ApplyConfig(form.Config);
                }
            }
            finally
            {
                _settingsForm = null;
                form.Dispose();
            }
        }

        private void ApplyConfig(AppConfig config)
        {
            _config = config;
            try
            {
                ConfigStore.Save(_config);
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败", ex);
                MessageBox.Show(
                    "保存配置失败：" + ex.Message,
                    "选中文本翻译",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            try
            {
                StartupManager.SetEnabled(_config.StartWithWindows);
            }
            catch (Exception ex)
            {
                Log.Error("更新开机启动设置失败", ex);
            }

            _startupItem.Checked = _config.StartWithWindows;

            if (_rightClickWatcher != null)
            {
                if (_config.EnableRightClick)
                {
                    _rightClickWatcher.Start();
                }
                else
                {
                    _rightClickWatcher.Stop();
                }
            }

            if (_selectionMonitor != null)
            {
                if (_config.ShowBubbleOnSelection)
                {
                    _selectionMonitor.Start();
                }
                else
                {
                    _selectionMonitor.Stop();
                }
            }

            ReRegisterHotkeys();

            if (_translationForm != null && !_translationForm.IsDisposed)
            {
                _translationForm.Close();
            }
        }

        private void OnStartupClicked(object sender, EventArgs e)
        {
            bool enabled = _startupItem.Checked;
            try
            {
                StartupManager.SetEnabled(enabled);
                _config.StartWithWindows = enabled;
                ConfigStore.Save(_config);
            }
            catch (Exception ex)
            {
                _startupItem.Checked = !enabled;
                Log.Error("切换开机启动失败", ex);
                MessageBox.Show(
                    "设置开机启动失败：" + ex.Message,
                    "选中文本翻译",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async void OnTrayDoubleClick(object sender, EventArgs e)
        {
            await TranslateSelectionAsync();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            ExitApplication();
        }

        private bool IsOwnPoint(Point point)
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form != null && form.Visible &&
                    form.Bounds.Contains(point))
                {
                    return true;
                }
            }
            return false;
        }

        private void ExitApplication()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _firstRunTimer.Stop();
            Application.Idle -= OnFirstIdle;

            if (_rightClickWatcher != null)
            {
                _rightClickWatcher.Stop();
                _rightClickWatcher.Dispose();
                _rightClickWatcher = null;
            }

            if (_selectionMonitor != null)
            {
                _selectionMonitor.Stop();
                _selectionMonitor.Dispose();
                _selectionMonitor = null;
            }

            _hotkeyWindow.UnregisterAll();
            _hotkeyWindow.HotkeyPressed -= OnHotkeyPressed;

            _trayIcon.Visible = false;

            if (_bubble != null && !_bubble.IsDisposed)
            {
                _bubble.Close();
                _bubble = null;
            }
            if (_translationForm != null && !_translationForm.IsDisposed)
            {
                _translationForm.Close();
                _translationForm = null;
            }
            if (_screenshotEditor != null && !_screenshotEditor.IsDisposed)
            {
                _screenshotEditor.Close();
                _screenshotEditor = null;
            }
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Close();
                _settingsForm = null;
            }
            // 贴图是独立顶层窗口，退出时一并清理，避免托盘消失后残留。
            PinnedImageForm.CloseAll();

            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _firstRunTimer.Stop();
                _firstRunTimer.Dispose();

                if (_rightClickWatcher != null)
                {
                    _rightClickWatcher.Dispose();
                    _rightClickWatcher = null;
                }

                if (_selectionMonitor != null)
                {
                    _selectionMonitor.Dispose();
                    _selectionMonitor = null;
                }

                _hotkeyWindow.UnregisterAll();
                _hotkeyWindow.Dispose();

                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayMenu.Dispose();
                _trayIconImage.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
