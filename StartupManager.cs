using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SelectionTranslator
{
    internal static class StartupManager
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SelectionTranslator";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RunKeyPath,
                    false))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    object value = key.GetValue(ValueName);
                    return value != null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取开机启动设置失败", ex);
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException(
                        "无法打开当前用户的开机启动注册表项。");
                }

                if (enabled)
                {
                    string command = "\"" + Application.ExecutablePath + "\"";
                    key.SetValue(ValueName, command);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
