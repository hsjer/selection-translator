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

        private static bool HasShowBubbleOnSelectionSetting(string json)
        {
            return json != null &&
                json.IndexOf(
                    "\"ShowBubbleOnSelection\"",
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
