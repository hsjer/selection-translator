using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    public class AppConfig
    {
        public string AppId { get; set; }
        public string SecretProtected { get; set; }
        public string TargetLanguage { get; set; }
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
