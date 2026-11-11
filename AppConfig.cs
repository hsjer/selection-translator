using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    public class AppConfig
    {
        /// <summary>
        /// 文件秒搜的默认快捷键。
        ///
        /// 早先默认是 `Ctrl+F`，但 `RegisterHotKey` 是**全局独占**的：只要
        /// 本程序在跑，Ctrl+F 就在所有程序里被截走——任务管理器点搜索框、
        /// 浏览器 / Word / 记事本里按查找，弹出来的都是文件秒搜（用户反馈
        /// 「在任务管理器点搜索就弹出秒搜」就是这个）。所以默认改成
        /// `Ctrl+Alt+F`，老配置里的 Ctrl+F 会被自动迁移过来。
        /// </summary>
        public const string DefaultFileSearchHotkey = "Ctrl+Alt+F";

        public string AppId { get; set; }
        public string SecretProtected { get; set; }
        public string TargetLanguage { get; set; }
        /// <summary>手动翻译窗口上次选的源语言（auto=自动检测）。</summary>
        public string TranslateFromLang { get; set; }
        /// <summary>手动翻译窗口上次选的目标语言（auto=按内容自动定向）。</summary>
        public string TranslateToLang { get; set; }
        public bool EnableRightClick { get; set; }
        public bool EnableClipboardFallback { get; set; }
        public bool ShowBubbleOnSelection { get; set; }
        public bool StartWithWindows { get; set; }
        public int MaxTextLength { get; set; }
        public string HotkeyTranslateSelection { get; set; }
        public string HotkeyManualInput { get; set; }
        public string HotkeyScreenshot { get; set; }
        public string ImageTargetLanguage { get; set; }

        // ---- 文字识别（腾讯云 OCR）----
        /// <summary>SecretId 本身不是机密，明文保存即可。</summary>
        public string TencentSecretId { get; set; }
        /// <summary>SecretKey 用 DPAPI 加密后保存，与百度密钥同样处理。</summary>
        public string TencentSecretKeyProtected { get; set; }
        public string TencentRegion { get; set; }
        /// <summary>OCR=通用场景，MulOCR=多语种场景。</summary>
        public string TencentOcrConfigId { get; set; }
        /// <summary>关闭时图片文字处理回退到百度图片翻译接口。</summary>
        public bool UseTencentOcr { get; set; }

        // ---- 语音识别（腾讯云一句话识别）----
        /// <summary>按住快捷键说话，松开即识别。</summary>
        public bool EnableVoiceInput { get; set; }
        public string HotkeyVoiceInput { get; set; }
        /// <summary>EngSerViceType，默认 16k_zh（中文普通话）。</summary>
        public string VoiceEngineType { get; set; }
        /// <summary>
        /// 上屏方式：type=模拟键入（不碰剪贴板），paste=剪贴板 + Ctrl+V。
        /// </summary>
        public string VoiceInputMethod { get; set; }
        /// <summary>除了上屏之外，是否顺便把识别文字留在剪贴板里。</summary>
        public bool VoiceAutoCopy { get; set; }

        // ---- 资源管理器集成 ----
        /// <summary>在图片的右键菜单里加「使用工具侠打开图片」。</summary>
        public bool EnableImageShellMenu { get; set; }

        /// <summary>在任务栏托盘区左侧显示网速 / CPU / 内存小字。</summary>
        public bool ShowTaskbarStats { get; set; }

        // ---- 剪贴板历史 ----
        /// <summary>监听系统剪贴板，记录文本 / 图片 / 文件。</summary>
        public bool EnableClipboardHistory { get; set; }
        /// <summary>历史保留时长（小时），0 = 永久；收藏不受限制。</summary>
        public int ClipboardTtlHours { get; set; }
        /// <summary>最多保留的非收藏条目数。</summary>
        public int ClipboardMaxItems { get; set; }
        /// <summary>打开剪贴板历史窗口的全局快捷键。</summary>
        public string HotkeyClipboardHistory { get; set; }

        /// <summary>打开「文件秒搜」窗口的全局快捷键，默认 Ctrl+Alt+F。</summary>
        public string HotkeyFileSearch { get; set; }
        /// <summary>
        /// 文件秒搜快捷键是否已从旧的默认值 `Ctrl+F` 迁到 `Ctrl+Alt+F`。
        /// 迁移只做一次，之后用户在设置里自己改回什么都尊重。
        /// </summary>
        public bool FileSearchHotkeyMigrated { get; set; }
        /// <summary>快速连按两下 Ctrl 打开剪贴板历史（低级键盘钩子）。</summary>
        public bool EnableDoubleCtrlHotkey { get; set; }

        /// <summary>
        /// 「桌面管理」窗口的透明度，0.3~1.0。默认 0.95，让面板半透明
        /// 又不至于看不清。序列化缺省会是 0（double 默认值），所以加载时
        /// 必须兜底，否则窗口会整片透明看不见任何东西。
        /// </summary>
        public double DesktopManagerOpacity { get; set; }

        // ---- 界面外观 ----
        /// <summary>
        /// 界面底色（"#RRGGBB"）。空串 = 默认深色 #16171A。
        /// 所有自绘窗口（桌面整理 / 剪贴板 / 文件秒搜 / 软件下载 / 图片
        /// 查看器…）都从这一个值推出整套配色：底色亮就走浅色方案，
        /// 底色暗就走深色方案，文字自动取高对比度。
        /// </summary>
        public string ThemeBaseColor { get; set; }

        /// <summary>强调色（按钮、选中医、进度条）。空串 = 默认蓝。</summary>
        public string ThemeAccentColor { get; set; }

        // ---- 桌面整理面板 ----
        /// <summary>面板是否常驻桌面（不进任务栏、不抢焦点，开机自动出现）。</summary>
        public bool DesktopPanelResident { get; set; }

        /// <summary>面板上次是不是收起成一条标题栏。</summary>
        public bool DesktopPanelCollapsed { get; set; }

        /// <summary>
        /// 锁定面板位置：锁定后拖动标题栏不会挪动面板，避免误拖。
        /// </summary>
        public bool DesktopPanelLocked { get; set; }

        /// <summary>面板上次的位置是否有效（新配置为 false，走默认落点）。</summary>
        public bool DesktopPanelPosSet { get; set; }

        public int DesktopPanelX { get; set; }

        public int DesktopPanelY { get; set; }

        /// <summary>展开状态的宽度（拖左边框调）。0 / 越界时回落到 400。</summary>
        public int DesktopPanelExpandedWidth { get; set; }

        /// <summary>展开状态的高度（拖上边框调，下边缘不动）。0 / 越界回落 620。</summary>
        public int DesktopPanelExpandedHeight { get; set; }

        /// <summary>收起后短横条的长度（拖右边框调）。0 / 越界回落 210。</summary>
        public int DesktopPanelCollapsedWidth { get; set; }

        /// <summary>
        /// 面板里不显示的桌面文件夹（完整路径，多个用 ; 分隔）。
        /// 用户自己的资料目录常和分类页签同名（比如桌面上的「文档」），
        /// 以前按名字一律当旧版分类文件夹跳过、还把它里面的文件塞进
        /// 文档页签，点「打开所在位置」就跳到那个文件夹去了。现在改成
        /// 按路径显式屏蔽：只有这里列出的文件夹不显示，别的一概照常。
        /// </summary>
        public string DesktopHiddenFolders { get; set; }

        // ---- 软件下载 ----
        /// <summary>
        /// 安装包保存目录。空串表示用系统「下载」文件夹（取不到时退回桌面），
        /// 这样配置拷到别的电脑上也不会指到一个不存在的路径。
        /// </summary>
        public string SoftwareDownloadDirectory { get; set; }

        /// <summary>
        /// 首次运行初始化（开机启动 / 右键菜单 / 打开方式）是否已经跑过。
        /// 全新安装时为 false，跑完写 true；老配置里没这个字段按 true 处理。
        /// </summary>
        public bool FirstRunDone { get; set; }

        public AppConfig()
        {
            AppId = "";
            SecretProtected = "";
            TargetLanguage = "auto";
            TranslateFromLang = "auto";
            TranslateToLang = "";
            EnableRightClick = true;
            EnableClipboardFallback = true;
            ShowBubbleOnSelection = true;
            StartWithWindows = false;
            MaxTextLength = 3000;
            HotkeyTranslateSelection = "Ctrl+Alt+T";
            HotkeyManualInput = "Ctrl+Alt+E";
            HotkeyScreenshot = "Ctrl+Alt+S";
            ImageTargetLanguage = "auto";
            TencentSecretId = "";
            TencentSecretKeyProtected = "";
            TencentRegion = TencentOcr.DefaultRegion;
            TencentOcrConfigId = "MulOCR";
            UseTencentOcr = true;
            EnableVoiceInput = true;
            HotkeyVoiceInput = "Ctrl+Alt+V";
            VoiceEngineType = TencentAsr.DefaultEngine;
            VoiceInputMethod = InputFocusProbe.MethodType;
            VoiceAutoCopy = false;
            EnableImageShellMenu = true;
            ShowTaskbarStats = true;
            EnableClipboardHistory = true;
            ClipboardTtlHours = ClipboardLogic.DefaultTtlHours;
            ClipboardMaxItems = ClipboardLogic.DefaultMaxItems;
            HotkeyClipboardHistory = "Ctrl+Alt+C";
            HotkeyFileSearch = DefaultFileSearchHotkey;
            EnableDoubleCtrlHotkey = true;
            DesktopManagerOpacity = 0.95;
            ThemeBaseColor = "";
            ThemeAccentColor = "";
            DesktopPanelResident = false;
            DesktopPanelCollapsed = false;
            DesktopPanelLocked = false;
            DesktopPanelPosSet = false;
            DesktopPanelX = 0;
            DesktopPanelY = 0;
            DesktopPanelExpandedWidth = 400;
            DesktopPanelExpandedHeight = 620;
            DesktopPanelCollapsedWidth = 210;
            DesktopHiddenFolders = "";
            SoftwareDownloadDirectory = "";
            FirstRunDone = false;
        }

        public string GetSecret()
        {
            return ConfigStore.Unprotect(SecretProtected);
        }

        public void SetSecret(string secret)
        {
            SecretProtected = ConfigStore.Protect(secret);
        }

        public string GetTencentSecretKey()
        {
            return ConfigStore.Unprotect(TencentSecretKeyProtected);
        }

        public void SetTencentSecretKey(string key)
        {
            TencentSecretKeyProtected = ConfigStore.Protect(key);
        }

        public bool HasTencentCredentials()
        {
            return !string.IsNullOrEmpty(
                    (TencentSecretId ?? "").Trim()) &&
                !string.IsNullOrEmpty(GetTencentSecretKey());
        }
    }

    internal static class ConfigStore
    {
        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SelectionTranslator");
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(DataDirectory, "config.json"); }
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return new AppConfig();
                }

                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppConfig config = serializer.Deserialize<AppConfig>(json);
                if (config == null)
                {
                    return new AppConfig();
                }
                if (string.IsNullOrEmpty(config.TargetLanguage))
                {
                    config.TargetLanguage = "auto";
                }
                if (config.MaxTextLength <= 0)
                {
                    config.MaxTextLength = 3000;
                }
                if (string.IsNullOrEmpty(config.HotkeyTranslateSelection))
                {
                    config.HotkeyTranslateSelection = "Ctrl+Alt+T";
                }
                if (string.IsNullOrEmpty(config.HotkeyManualInput))
                {
                    config.HotkeyManualInput = "Ctrl+Alt+E";
                }
                if (string.IsNullOrEmpty(config.HotkeyScreenshot))
                {
                    config.HotkeyScreenshot = "Ctrl+Alt+S";
                }
                if (string.IsNullOrEmpty(config.ImageTargetLanguage))
                {
                    config.ImageTargetLanguage = "auto";
                }
                if (string.IsNullOrEmpty(config.TencentRegion))
                {
                    config.TencentRegion = TencentOcr.DefaultRegion;
                }
                if (string.IsNullOrEmpty(config.TencentOcrConfigId))
                {
                    config.TencentOcrConfigId = "MulOCR";
                }
                // 旧版本配置没有这个开关时默认启用腾讯云 OCR。
                if (!HasUseTencentOcrSetting(json))
                {
                    config.UseTencentOcr = true;
                }
                // 老配置没有这三个尺寸字段，反序列化后是 0；一律回落到
                // 默认形状，别让面板启动变成 0×0。用户拉过的合法尺寸照用。
                if (config.DesktopPanelExpandedWidth < 300 ||
                    config.DesktopPanelExpandedWidth > 1600)
                {
                    config.DesktopPanelExpandedWidth = 400;
                }
                if (config.DesktopPanelExpandedHeight < 220 ||
                    config.DesktopPanelExpandedHeight > 1600)
                {
                    config.DesktopPanelExpandedHeight = 620;
                }
                if (config.DesktopPanelCollapsedWidth < 150 ||
                    config.DesktopPanelCollapsedWidth > 800)
                {
                    config.DesktopPanelCollapsedWidth = 210;
                }

                // 语音识别是后加的功能：旧配置里完全没有这几个字段，
                // 用 VoiceEngineType 是否存在作为「新版配置」的判据。
                if (!HasVoiceSettings(json))
                {
                    config.EnableVoiceInput = true;
                    config.HotkeyVoiceInput = "Ctrl+Alt+V";
                    config.VoiceEngineType = TencentAsr.DefaultEngine;
                    config.VoiceInputMethod = InputFocusProbe.MethodType;
                    config.VoiceAutoCopy = false;
                }
                if (string.IsNullOrEmpty(config.HotkeyVoiceInput))
                {
                    config.HotkeyVoiceInput = "Ctrl+Alt+V";
                }
                // 老版本是「弹结果窗口 + 自动复制」，新版本直接上屏，
                // 没有这个字段时一律回到模拟键入 / 不占用剪贴板。
                if (!HasVoiceInputMethodSetting(json))
                {
                    config.VoiceInputMethod = InputFocusProbe.MethodType;
                    config.VoiceAutoCopy = false;
                }
                config.VoiceEngineType =
                    TencentAsr.NormalizeEngine(config.VoiceEngineType);
                config.VoiceInputMethod =
                    InputFocusProbe.NormalizeMethod(config.VoiceInputMethod);

                // 旧版本配置里没有这个字段，反序列化后为 false，
                // 这里按默认开启处理。
                if (!HasClipboardFallbackSetting(json))
                {
                    config.EnableClipboardFallback = true;
                }

                // 同上，新增的选中即显示开关在旧配置中不存在，按默认开启处理。
                if (!HasShowBubbleOnSelectionSetting(json))
                {
                    config.ShowBubbleOnSelection = true;
                }

                // 图片右键菜单也是后加的，旧配置里没有这个字段。
                if (!HasImageShellMenuSetting(json))
                {
                    config.EnableImageShellMenu = true;
                }

                // 任务栏状态条是后加的功能：老配置里没有这个字段时
                // 默认显示（用户明确要求「运行后直接就显示在任务栏」）。
                if (!HasShowTaskbarStatsSetting(json))
                {
                    config.ShowTaskbarStats = true;
                }

                // 剪贴板历史是后加的：老配置里没有这几个字段时按默认开启，
                // 但用户一旦改过（哪怕关掉）就原样保留。
                if (!HasClipboardHistorySettings(json))
                {
                    config.EnableClipboardHistory = true;
                    config.ClipboardTtlHours =
                        ClipboardLogic.DefaultTtlHours;
                    config.ClipboardMaxItems =
                        ClipboardLogic.DefaultMaxItems;
                    config.HotkeyClipboardHistory = "Ctrl+Alt+C";
                }
                config.ClipboardTtlHours = ClipboardLogic.NormalizeTtl(
                    config.ClipboardTtlHours);
                config.ClipboardMaxItems = ClipboardLogic.ClampMaxItems(
                    config.ClipboardMaxItems);
                if (string.IsNullOrEmpty(config.HotkeyClipboardHistory))
                {
                    config.HotkeyClipboardHistory = "Ctrl+Alt+C";
                }

                // 双击 Ctrl 也是后加的：老配置里没这个字段时默认开启，
                // 用户关过就原样保留（哪怕值为 false）。
                if (!HasDoubleCtrlSetting(json))
                {
                    config.EnableDoubleCtrlHotkey = true;
                }

                // 软件下载目录：老配置里没有这个字段，保持空串（用系统默认）。
                if (config.SoftwareDownloadDirectory == null)
                {
                    config.SoftwareDownloadDirectory = "";
                }

                // 文件秒搜的快捷键是后加的：老配置里没有这个字段，
                // 补上默认值；有字段但被清空也回到默认。
                if (!HasFileSearchHotkeySetting(json) ||
                    string.IsNullOrEmpty(
                        (config.HotkeyFileSearch ?? "").Trim()))
                {
                    config.HotkeyFileSearch =
                        AppConfig.DefaultFileSearchHotkey;
                }

                // 默认值从 Ctrl+F 换成了 Ctrl+Alt+F。Ctrl+F 在几乎所有
                // 程序里都是「查找」，被全局热键截走后，任务管理器点搜索
                // 框、浏览器里找东西都会弹文件秒搜。老配置里还留着旧默认
                // 值的一次性迁过来；迁移过的（标记已置位）不再动，用户
                // 想改回 Ctrl+F 由他自己决定。
                if (!config.FileSearchHotkeyMigrated)
                {
                    config.FileSearchHotkeyMigrated = true;
                    if (string.Equals(
                            (config.HotkeyFileSearch ?? "").Trim(),
                            "Ctrl+F", StringComparison.OrdinalIgnoreCase))
                    {
                        config.HotkeyFileSearch =
                            AppConfig.DefaultFileSearchHotkey;
                    }
                }

                // 透明度缺省会被反序列化成 0（double 默认），必须兜底成可见值，
                // 否则「桌面管理」窗口整片透明、什么都看不到。
                if (!HasDesktopManagerOpacitySetting(json) ||
                    config.DesktopManagerOpacity <= 0.0 ||
                    config.DesktopManagerOpacity > 1.0)
                {
                    config.DesktopManagerOpacity = 0.95;
                }

                // 老用户已经有自己的开关状态，别把人家的设置改掉。
                // 只有「完全没有 FirstRunDone 字段」才当成老配置跳过；
                // 全新安装（config.json 不存在）在上面已经返回了 false。
                if (!HasFirstRunDoneSetting(json))
                {
                    config.FirstRunDone = true;
                }
                return config;
            }
            catch (Exception ex)
            {
                Log.Error("加载配置失败", ex);
                return new AppConfig();
            }
        }

        private static bool HasUseTencentOcrSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"UseTencentOcr\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasClipboardFallbackSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"EnableClipboardFallback\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasVoiceSettings(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"VoiceEngineType\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasVoiceInputMethodSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"VoiceInputMethod\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasShowBubbleOnSelectionSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"ShowBubbleOnSelection\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasImageShellMenuSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"EnableImageShellMenu\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasShowTaskbarStatsSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"ShowTaskbarStats\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasClipboardHistorySettings(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"EnableClipboardHistory\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>供自检验证老配置迁移逻辑。</summary>
        public static bool HasDoubleCtrlSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"EnableDoubleCtrlHotkey\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>供自检直接验证老配置迁移逻辑，不依赖真实 config.json。</summary>
        public static bool HasFileSearchHotkeySetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"HotkeyFileSearch\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>供自检直接验证老配置迁移逻辑，不依赖真实 config.json。</summary>
        public static bool HasDesktopManagerOpacitySetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"DesktopManagerOpacity\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>供自检直接验证老配置迁移逻辑，不依赖真实 config.json。</summary>
        public static bool HasFirstRunDoneSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"FirstRunDone\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Save(AppConfig config)
        {
            Directory.CreateDirectory(DataDirectory);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(config);
            string tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            if (File.Exists(ConfigPath))
            {
                BackupExistingConfig();
                try
                {
                    File.Replace(tempPath, ConfigPath, null);
                }
                catch
                {
                    File.Copy(tempPath, ConfigPath, true);
                    File.Delete(tempPath);
                }
            }
            else
            {
                File.Move(tempPath, ConfigPath);
            }
        }

        /// <summary>
        /// 覆盖配置前留一份带时间戳的快照。发布、诊断或误操作把凭证
        /// 清掉时，可以直接从最近的快照恢复。同一分钟内不重复备份，
        /// 只保留最近 8 份，避免无限堆积。
        /// </summary>
        private static void BackupExistingConfig()
        {
            try
            {
                string[] existing = Directory.GetFiles(
                    DataDirectory, "config.auto-backup.*.json");
                foreach (string file in existing)
                {
                    if (DateTime.Now - File.GetLastWriteTime(file) <
                        TimeSpan.FromSeconds(60))
                    {
                        return;
                    }
                }

                File.Copy(
                    ConfigPath,
                    Path.Combine(
                        DataDirectory,
                        "config.auto-backup." +
                        DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                        ".json"),
                    true);

                Array.Sort(existing, delegate(string a, string b)
                {
                    return string.CompareOrdinal(b, a);
                });
                for (int i = 7; i < existing.Length; i++)
                {
                    File.Delete(existing[i]);
                }
            }
            catch (Exception ex)
            {
                Log.Error("备份配置失败", ex);
            }
        }

        internal static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(value);
                byte[] encrypted = ProtectedData.Protect(
                    plain,
                    null,
                    DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Log.Error("加密密钥失败", ex);
                return "";
            }
        }

        internal static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            try
            {
                byte[] encrypted = Convert.FromBase64String(value);
                byte[] plain = ProtectedData.Unprotect(
                    encrypted,
                    null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Log.Error("解密密钥失败", ex);
                return "";
            }
        }
    }
}
