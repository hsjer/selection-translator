using System;
using System.Diagnostics;
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
        private const int HotkeyIdVoice = 4;

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _translateMenuItem;
        private readonly ToolStripMenuItem _manualMenuItem;
        private readonly ToolStripMenuItem _screenshotMenuItem;
        private readonly ToolStripMenuItem _voiceMenuItem;
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
        private int _hotkeyVoiceModifiers;
        private Keys _hotkeyVoiceKey;
        private RightClickWatcher _rightClickWatcher;
        private SelectionMonitor _selectionMonitor;
        private TranslateBubbleForm _bubble;
        private TranslationForm _translationForm;
        private ImageTranslationForm _translationWindow;
        private ToolStripMenuItem _closePinsItem;
        private ToolStripMenuItem _shellMenuItem;
        // 防止热键在浮层显示期间重复触发、嵌套打开第二个截图会话。
        private bool _captureInProgress;

        // ---- 语音识别（按住说话）----
        private VoiceRecorder _recorder;
        private VoiceCaptureForm _voiceBar;
        private System.Windows.Forms.Timer _voicePollTimer;
        private System.Windows.Forms.Timer _voiceMenuDelayTimer;
        private bool _voiceInProgress;
        // 防止「定时器收尾」和「点浮条停止」同时进来跑两遍。
        private bool _voiceStopping;
        // 按下语音键那一刻的焦点快照，文字要送回这个输入框。
        private FocusTarget _voiceTarget;
        // 主键是否已观测到按下：避免热键刚触发就误判为「已松开」。
        private bool _voiceKeyDown;
        private DateTime _voiceStartedUtc;
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

            // 老版本把启动项写在 SelectionTranslator 这个名字下，
            // 被启动管理工具禁用过、或程序换过目录时这里一次性纠正。
            if (_config.StartWithWindows)
            {
                StartupManager.EnsureCurrent();
            }

            _trayIconImage = IconFactory.Create();
            _trayMenu = new ContextMenuStrip();
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = _trayIconImage;
            _trayIcon.Text = AppInfo.Name;
            _trayIcon.Visible = true;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.DoubleClick += OnTrayDoubleClick;

            // 每次展开菜单都回读一次注册表，保证勾选状态和系统里真实一致
            // （用户可能在别处用注册表工具改过）。
            _trayMenu.Opening += delegate
            {
                if (_shellMenuItem != null && !_shellMenuItem.IsDisposed)
                {
                    _shellMenuItem.Checked = ShellIntegration.IsEnabled();
                }
            };

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

            _voiceMenuItem =
                new ToolStripMenuItem("语音识别（按住说话）");
            _voiceMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyVoiceInput;
            _voiceMenuItem.Click += OnVoiceClicked;
            _trayMenu.Items.Add(_voiceMenuItem);

            ToolStripMenuItem clipboardItem =
                new ToolStripMenuItem("翻译剪贴板");
            clipboardItem.Click += OnTranslateClipboardClicked;
            _trayMenu.Items.Add(clipboardItem);

            ToolStripMenuItem imageItem =
                new ToolStripMenuItem("打开图片…");
            imageItem.Click += OnOpenImageClicked;
            _trayMenu.Items.Add(imageItem);

            // 资源管理器右键菜单的开关。默认开启，首次启动就会写进 HKCU。
            _shellMenuItem = new ToolStripMenuItem(
                "图片右键菜单 / 打开方式：" + AppInfo.Name);
            _shellMenuItem.Click += OnShellMenuClicked;
            _trayMenu.Items.Add(_shellMenuItem);
            SyncShellMenu();

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
            if (!HasCredentials() || FirstRunSetup.JustCompleted)
            {
                _firstRunTimer.Start();
            }

            // 录音期间靠这个定时器轮询主键是否已经松开（热键 API 只给
            // 按下消息，拿不到松开消息），同时刷新浮条的时长与电平。
            _voicePollTimer = new System.Windows.Forms.Timer();
            _voicePollTimer.Interval = 40;
            _voicePollTimer.Tick += OnVoicePollTick;

            // 从托盘菜单点「语音输入」时菜单还占着前台，等它收起、
            // 焦点回到原窗口后再记录焦点快照。
            _voiceMenuDelayTimer = new System.Windows.Forms.Timer();
            _voiceMenuDelayTimer.Interval = 260;
            _voiceMenuDelayTimer.Tick += OnVoiceMenuDelayTick;
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

            if (HotkeyParser.TryParse(
                _config.HotkeyVoiceInput,
                out modifiers,
                out key))
            {
                _hotkeyVoiceModifiers =
                    modifiers | NativeMethods.MOD_NOREPEAT;
                _hotkeyVoiceKey = key;
                bool ok = _hotkeyWindow.Register(
                    HotkeyIdVoice,
                    _hotkeyVoiceModifiers,
                    key);
                if (!ok)
                {
                    Log.Info("全局快捷键 " +
                        _config.HotkeyVoiceInput +
                        " 注册失败，可能已被其他程序占用。");
                }
            }
            else
            {
                Log.Info("快捷键格式无效：" +
                    _config.HotkeyVoiceInput);
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
            _hotkeyWindow.Unregister(HotkeyIdVoice);
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
            _voiceMenuItem.ShortcutKeyDisplayString =
                _config.HotkeyVoiceInput;
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

            // 首次运行时告诉用户「三个开关已经替你开好了」。
            // 没填凭证时设置窗口会紧接着弹出，就不叠一条气泡了。
            if (FirstRunSetup.JustCompleted)
            {
                FirstRunSetup.JustCompleted = false;
                if (HasCredentials())
                {
                    try
                    {
                        _trayIcon.ShowBalloonTip(
                            3200,
                            AppInfo.Name,
                            "已自动开启：开机启动、图片右键菜单、打开方式",
                            ToolTipIcon.Info);
                    }
                    catch
                    {
                        // 系统关了通知就不提示，不影响功能。
                    }
                }
            }

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
                StartScreenshot();
                return;
            }
            if (id == HotkeyIdVoice)
            {
                StartVoiceInput();
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
            StartScreenshot();
        }

        // ------------------------------------------------------------------
        // 语音识别（按住说话）
        // ------------------------------------------------------------------

        private void OnVoiceClicked(object sender, EventArgs e)
        {
            _voiceMenuDelayTimer.Stop();
            _voiceMenuDelayTimer.Start();
        }

        private void OnVoiceMenuDelayTick(object sender, EventArgs e)
        {
            _voiceMenuDelayTimer.Stop();
            StartVoiceInput();
        }

        /// <summary>
        /// 按住快捷键开始录音：不限制输入框类型，任何控件都直接录音；
        /// 松开后上传腾讯云「一句话识别」，文字直接送回那个光标处，
        /// 全程不弹出任何需要操作的窗口。
        /// 热键 API 只投递按下消息，拿不到松开消息，所以主键状态靠
        /// _voicePollTimer 轮询 GetAsyncKeyState。
        /// </summary>
        private void StartVoiceInput()
        {
            if (_disposed || _voiceInProgress)
            {
                return;
            }
            if (!_config.EnableVoiceInput)
            {
                _trayIcon.ShowBalloonTip(
                    3000,
                    "语音识别",
                    "语音识别已在「设置」里关闭，勾选「启用语音识别」后即可使用。",
                    ToolTipIcon.Info);
                return;
            }
            if (!_config.HasTencentCredentials())
            {
                _trayIcon.ShowBalloonTip(
                    4000,
                    "语音识别",
                    "请先在「设置」里填写腾讯云 SecretId / SecretKey（语音识别与文字识别共用同一对凭证）。",
                    ToolTipIcon.Warning);
                ShowSettings();
                return;
            }
            if (VoiceRecorder.DeviceCount() <= 0)
            {
                _trayIcon.ShowBalloonTip(
                    3000,
                    "语音识别",
                    "没有检测到麦克风，请插好麦克风后重试。",
                    ToolTipIcon.Warning);
                return;
            }

            // 只记录焦点快照，不做任何「是不是输入框」的拦截：
            // 自绘控件、浏览器、聊天窗口、终端等一律允许，避免白名单
            // 漏判导致按键没反应。只有桌面/菜单这类明确非输入场景，
            // 才在上屏阶段改成把文字放进剪贴板兜底。
            FocusTarget focus = InputFocusProbe.Probe();
            Log.Info("语音输入焦点快照：" + focus.Describe());

            _voiceTarget = focus;
            _voiceStartedUtc = DateTime.UtcNow;
            // 热键刚触发，主键此刻必然是按下的。
            _voiceKeyDown = true;

            VoiceRecorder recorder = new VoiceRecorder();
            try
            {
                recorder.Start();
            }
            catch (Exception ex)
            {
                Log.Error("启动录音失败", ex);
                recorder.Dispose();
                _trayIcon.ShowBalloonTip(
                    3500, "语音识别", ex.Message, ToolTipIcon.Warning);
                return;
            }

            // 录音期间暂停选区监听，避免误弹「译」气泡抢焦点。
            if (_rightClickWatcher != null &&
                _rightClickWatcher.IsRunning)
            {
                _rightClickWatcher.Stop();
            }
            if (_selectionMonitor != null &&
                _selectionMonitor.IsRunning)
            {
                _selectionMonitor.Stop();
            }

            _recorder = recorder;
            _voiceInProgress = true;

            VoiceCaptureForm bar =
                new VoiceCaptureForm(_config.HotkeyVoiceInput);
            bar.StopRequested += delegate { StopVoiceInput(); };
            bar.CancelRequested += delegate { CancelVoiceInput(); };
            _voiceBar = bar;
            bar.Show();
            _voicePollTimer.Start();
        }

        private void OnVoicePollTick(object sender, EventArgs e)
        {
            VoiceRecorder recorder = _recorder;
            if (!_voiceInProgress || recorder == null)
            {
                _voicePollTimer.Stop();
                return;
            }

            double elapsedMs =
                (DateTime.UtcNow - _voiceStartedUtc).TotalMilliseconds;

            VoiceCaptureForm bar = _voiceBar;
            if (bar != null && !bar.IsDisposed)
            {
                bar.UpdateRecording(
                    TimeSpan.FromMilliseconds(elapsedMs),
                    recorder.Level);
            }

            // 接口上限 60 秒，到点自动收尾，避免白录。
            if (elapsedMs >= VoiceRecorder.MaxSeconds * 1000)
            {
                StopVoiceInput();
                return;
            }

            bool down = (NativeMethods.GetAsyncKeyState(
                (int)_hotkeyVoiceKey) & 0x8000) != 0;
            if (down)
            {
                _voiceKeyDown = true;
                return;
            }

            // 松开主键就结束；留 300ms 让「点一下」也能录到一小段。
            if (_voiceKeyDown && elapsedMs >= 300)
            {
                StopVoiceInput();
            }
        }

        private async void StopVoiceInput()
        {
            if (!_voiceInProgress || _voiceStopping)
            {
                return;
            }
            _voiceStopping = true;
            _voicePollTimer.Stop();

            VoiceRecorder recorder = _recorder;
            _recorder = null;

            byte[] wav = null;
            if (recorder != null)
            {
                try
                {
                    wav = recorder.Stop();
                }
                catch (Exception ex)
                {
                    Log.Error("停止录音失败", ex);
                }
                recorder.Dispose();
            }

            int durationMs = wav == null
                ? 0 : VoiceRecorder.DurationMs(wav.Length);
            if (wav == null || durationMs < 300)
            {
                CloseVoiceBar();
                RestoreSelectionWatchers();
                _voiceInProgress = false;
                _voiceStopping = false;
                _trayIcon.ShowBalloonTip(
                    2200,
                    "语音识别",
                    "录音太短了，请按住快捷键把话说完再松开。",
                    ToolTipIcon.Info);
                return;
            }

            VoiceCaptureForm bar = _voiceBar;
            if (bar != null && !bar.IsDisposed)
            {
                bar.ShowRecognizing("正在识别…");
            }

            int started = Environment.TickCount;
            try
            {
                AsrResult result = await TencentAsr.RecognizeAsync(
                    _config, wav, durationMs);
                int elapsed = Environment.TickCount - started;
                Log.Info("语音识别端到端 " + elapsed + " ms，" +
                    "音频 " + durationMs + " ms");

                RestoreSelectionWatchers();
                _voiceInProgress = false;
                _voiceStopping = false;
                await DeliverVoiceTextAsync(result, elapsed);
            }
            catch (TranslationException ex)
            {
                RestoreSelectionWatchers();
                _voiceInProgress = false;
                _voiceStopping = false;
                Log.Info("语音识别失败：" + ex.Code + " " + ex.Message);
                ShowBarNote(DetachVoiceBar(), false, "识别失败");
                _trayIcon.ShowBalloonTip(
                    4500, "语音识别失败", ex.Message, ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                RestoreSelectionWatchers();
                _voiceInProgress = false;
                _voiceStopping = false;
                Log.Error("语音识别时发生未处理错误", ex);
                ShowBarNote(DetachVoiceBar(), false, "识别失败");
                _trayIcon.ShowBalloonTip(
                    4500, "语音识别失败", ex.Message, ToolTipIcon.Warning);
            }
        }

        /// <summary>点浮条上的 × ，直接丢弃这次录音。</summary>
        private void CancelVoiceInput()
        {
            if (!_voiceInProgress || _voiceStopping)
            {
                return;
            }
            _voiceStopping = true;
            _voicePollTimer.Stop();

            VoiceRecorder recorder = _recorder;
            _recorder = null;
            if (recorder != null)
            {
                try
                {
                    recorder.Stop();
                }
                catch (Exception ex)
                {
                    Log.Error("取消录音失败", ex);
                }
                recorder.Dispose();
            }

            CloseVoiceBar();
            RestoreSelectionWatchers();
            _voiceInProgress = false;
            _voiceStopping = false;
            Log.Info("录音已取消");
        }

        private void RestoreSelectionWatchers()
        {
            if (_disposed)
            {
                return;
            }
            if (_rightClickWatcher != null &&
                _config.EnableRightClick &&
                !_rightClickWatcher.IsRunning)
            {
                _rightClickWatcher.Start();
            }
            if (_selectionMonitor != null &&
                _config.ShowBubbleOnSelection &&
                !_selectionMonitor.IsRunning)
            {
                _selectionMonitor.Start();
            }
        }

        private void CloseVoiceBar()
        {
            VoiceCaptureForm bar = _voiceBar;
            _voiceBar = null;
            if (bar == null || bar.IsDisposed)
            {
                return;
            }
            bar.StopAnimation();
            bar.Close();
        }

        /// <summary>
        /// 识别完成后不再开任何窗口：直接把文字写到说话前那个光标处。
        /// 只有窗口被切走 / 注入失败时才退化为「放进剪贴板」并给提示，
        /// 保证识别结果不会凭空丢掉。
        /// </summary>
        private async Task DeliverVoiceTextAsync(
            AsrResult result, int elapsedMs)
        {
            VoiceCaptureForm bar = DetachVoiceBar();
            FocusTarget voiceTarget = _voiceTarget;
            string text = result == null
                ? "" : (result.Text ?? "").Trim();
            try
            {
                if (text.Length == 0)
                {
                    Log.Info("语音识别：没有识别到文字");
                    ShowBarNote(bar, false, "没听清，再说一次");
                    return;
                }

                // 用户勾了「同时复制到剪贴板」才去占用剪贴板；
                // 默认的模拟键入模式完全不碰它。
                if (_config.VoiceAutoCopy)
                {
                    ClipboardHelper.SetText(text);
                }

                VoiceInsertOutcome outcome =
                    await InputFocusProbe.InsertAsync(
                        voiceTarget,
                        text,
                        _config.VoiceInputMethod,
                        _config.VoiceAutoCopy);

                if (outcome == VoiceInsertOutcome.Inserted)
                {
                    Log.Info("语音识别端到端 " + elapsedMs +
                        " ms，已上屏 " + text.Length + " 字");
                    ShowBarNote(bar, true,
                        "已输入 " + text.Length + " 字");
                    return;
                }

                string note;
                if (outcome == VoiceInsertOutcome.WindowChanged)
                {
                    note = "窗口已切换，文字已复制到剪贴板";
                }
                else if (voiceTarget != null &&
                    voiceTarget.IsLikelyNonInput)
                {
                    note = "当前不是输入框，文字已复制到剪贴板";
                }
                else
                {
                    note = "无法写入输入框，已复制到剪贴板";
                }
                Log.Info("语音识别：" + note);
                ClipboardHelper.SetText(text);
                ShowBarNote(bar, false, note);
                _trayIcon.ShowBalloonTip(
                    3200,
                    "语音识别",
                    note + "，按 Ctrl+V 粘贴。",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Error("语音输入上屏失败", ex);
                if (text.Length > 0)
                {
                    ClipboardHelper.SetText(text);
                }
                ShowBarNote(bar, false, "上屏失败，已复制到剪贴板");
            }
        }

        /// <summary>把浮条交给「完成态」自行收尾，不再由托盘持有。</summary>
        private VoiceCaptureForm DetachVoiceBar()
        {
            VoiceCaptureForm bar = _voiceBar;
            _voiceBar = null;
            if (bar == null || bar.IsDisposed)
            {
                return null;
            }
            bar.StopAnimation();
            return bar;
        }

        private void ShowBarNote(
            VoiceCaptureForm bar, bool done, string note)
        {
            if (bar == null || bar.IsDisposed)
            {
                return;
            }
            if (done)
            {
                bar.ShowDone(note);
            }
            else
            {
                bar.ShowFailed(note);
            }
            ScheduleBarClose(bar, done ? 900 : 2000);
        }

        /// <summary>成功只闪一下，失败留久一点让人看清原因。</summary>
        private void ScheduleBarClose(VoiceCaptureForm bar, int delayMs)
        {
            System.Windows.Forms.Timer timer =
                new System.Windows.Forms.Timer();
            timer.Interval = delayMs;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                if (!bar.IsDisposed)
                {
                    bar.Close();
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 截屏浮层 → 直接收尾，不再打开旧的截图编辑器。
        /// 「对号」在浮层内已把合成图复制到剪贴板，这里只回收位图；
        /// 「翻译图片」打开轻量结果窗口，译文直接显示在图上且可选中复制；
        /// 「钉在桌面」打开贴图窗口。
        /// </summary>
        private void StartScreenshot()
        {
            // 一次快捷键只对应一次截图；浮层显示期间忽略重复触发。
            if (_disposed || _captureInProgress)
            {
                return;
            }
            _captureInProgress = true;

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
            long finishMs = 0;
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
                        finishMs = capture.FinishElapsedMs;
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

            if (finishMs > 0)
            {
                Log.Info("截图浮层收尾耗时 " + finishMs + " ms");
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

            // 「对号」：浮层在 Finish() 里已经 Clipboard.SetImage，
            // 这里只回收位图并给一次轻提示，绝不再弹任何窗口。
            if (action == ScreenshotCaptureForm.CaptureResult.Copy)
            {
                image.Dispose();
                image = null;
                if (rawImage != null)
                {
                    rawImage.Dispose();
                    rawImage = null;
                }
                Log.Info("截图已复制到剪贴板");
                if (!_disposed)
                {
                    _trayIcon.ShowBalloonTip(
                        1200,
                        "截图",
                        "截图已复制到剪贴板",
                        ToolTipIcon.Info);
                }
                return;
            }

            // 「取字」与「译」都打开同一个结果窗口，区别只是取字不翻译：
            // 用户可以在图上自己点选 / 框选要复制的文字，而不是一次性
            // 把全文塞进剪贴板。

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

            // 「翻译图片」与「取字」都需要结果窗口；其余结果在浮层内已处理完。
            bool ocrOnly =
                action == ScreenshotCaptureForm.CaptureResult.Ocr;
            if (action != ScreenshotCaptureForm.CaptureResult.Translate &&
                !ocrOnly)
            {
                image.Dispose();
                if (rawImage != null)
                {
                    rawImage.Dispose();
                }
                return;
            }

            try
            {
                // 已有的结果窗口先关掉，避免同时开多个实例。
                if (_translationWindow != null &&
                    !_translationWindow.IsDisposed)
                {
                    _translationWindow.Close();
                }

                _translationWindow =
                    new ImageTranslationForm(
                        delegate { return _config; },
                        image, rawImage, ocrOnly);
                // 位图所有权移交给结果窗口，由它在关闭时释放。
                image = null;
                rawImage = null;
                _translationWindow.FormClosed += delegate
                {
                    _translationWindow = null;
                };
                if (ocrOnly)
                {
                    _translationWindow.BeginRecognition();
                }
                else
                {
                    _translationWindow.BeginTranslation();
                }
                _translationWindow.Show();
                _translationWindow.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("打开图片结果窗口失败", ex);
                if (_translationWindow != null &&
                    !_translationWindow.IsDisposed)
                {
                    _translationWindow.Close();
                    _translationWindow = null;
                }
                if (image != null)
                {
                    image.Dispose();
                }
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
                        AppInfo.Name,
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
                    AppInfo.Name,
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
                    AppInfo.Name,
                    "剪贴板中没有文本。",
                    ToolTipIcon.Info);
                return;
            }
            ShowTranslation(text, Cursor.Position);
        }

        /// <summary>从托盘打开图片工具（查看 / 转格式 / 压缩 / 改尺寸）。</summary>
        private void OnOpenImageClicked(object sender, EventArgs e)
        {
            ImageViewerForm.ShowOpenDialog(null);
        }

        /// <summary>启动时把右键菜单对齐到当前 exe，勾选状态取注册表的真实值。</summary>
        private void SyncShellMenu()
        {
            ShellIntegration.Sync(_config, Application.ExecutablePath);
            if (_shellMenuItem != null && !_shellMenuItem.IsDisposed)
            {
                _shellMenuItem.Checked = ShellIntegration.IsEnabled();
            }
        }

        private void OnShellMenuClicked(object sender, EventArgs e)
        {
            bool want = !_shellMenuItem.Checked;
            string error;
            if (!ShellIntegration.SetEnabled(
                want, Application.ExecutablePath, out error))
            {
                _shellMenuItem.Checked = ShellIntegration.IsEnabled();
                MessageBox.Show(
                    "修改图片右键菜单失败：" +
                        (error ?? "系统拒绝了这次写入。") +
                        "\r\n\r\n可以换一个目录重新运行程序再试。",
                    AppInfo.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _config.EnableImageShellMenu = want;
            try
            {
                ConfigStore.Save(_config);
            }
            catch (Exception ex)
            {
                Log.Error("保存右键菜单开关失败", ex);
            }

            _shellMenuItem.Checked = want;
            try
            {
                _trayIcon.ShowBalloonTip(
                    2600,
                    AppInfo.Name,
                    want
                        ? "已添加。图片右键可直接选「" +
                            ShellIntegration.MenuText +
                            "」，或在「打开方式」里选「" +
                            AppInfo.Name + "」。"
                        : "已移除。右键菜单和「打开方式」里都不再显示。",
                    ToolTipIcon.Info);
            }
            catch
            {
                // 气泡提示失败不影响功能。
            }
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
                    AppInfo.Name,
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
                    AppInfo.Name,
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
            _voicePollTimer.Stop();
            _voiceMenuDelayTimer.Stop();
            Application.Idle -= OnFirstIdle;

            if (_recorder != null)
            {
                try
                {
                    _recorder.Stop();
                }
                catch (Exception ex)
                {
                    Log.Error("退出时停止录音失败", ex);
                }
                _recorder.Dispose();
                _recorder = null;
            }
            CloseVoiceBar();

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
            if (_translationWindow != null && !_translationWindow.IsDisposed)
            {
                _translationWindow.Close();
                _translationWindow = null;
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
                _voicePollTimer.Stop();
                _voicePollTimer.Dispose();
                _voiceMenuDelayTimer.Stop();
                _voiceMenuDelayTimer.Dispose();

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
