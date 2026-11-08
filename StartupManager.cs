using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SelectionTranslator
{
    internal static class StartupManager
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// 任务管理器「启动」页会往这里写「用户手动禁用」的标记。
        /// 有些启动优化工具（360、各种管家）也写这里；只要这枚标记还在，
        /// 就算 Run 键值本身没删，Windows 也不会启动程序。
        /// </summary>
        private const string ApprovedKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\" +
            @"StartupApproved\Run";

        /// <summary>任务管理器「启动」页里显示的名字，跟产品名保持一致。</summary>
        private const string ValueName = AppInfo.Name;

        /// <summary>改名之前用的值名，读到就顺手迁移掉。</summary>
        private const string LegacyValueName = "SelectionTranslator";

        /// <summary>给自检用的只读快照，避免把路径常量暴露成 public。</summary>
        public static string RunKeyPathForTest
        {
            get { return RunKeyPath; }
        }

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
                    return key.GetValue(ValueName) != null ||
                        key.GetValue(LegacyValueName) != null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取开机启动设置失败", ex);
                return false;
            }
        }

        /// <summary>当前注册的命令行（没有则为空）。</summary>
        public static string GetCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RunKeyPath,
                    false))
                {
                    if (key == null)
                    {
                        return "";
                    }
                    object value = key.GetValue(ValueName) ??
                        key.GetValue(LegacyValueName);
                    return value as string ?? "";
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取开机启动命令行失败", ex);
                return "";
            }
        }

        /// <summary>启动项是否就指向当前这个 exe（换目录 / 改名后要重修）。</summary>
        public static bool IsCurrent()
        {
            return GetCommand() == BuildCommand();
        }

        public static string BuildCommand()
        {
            return "\"" + Application.ExecutablePath + "\"";
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

                // 不管开还是关，旧名字的值都清掉，避免两套启动项打架。
                key.DeleteValue(LegacyValueName, false);

                if (enabled)
                {
                    string command = BuildCommand();
                    key.SetValue(ValueName, command);
                    ClearApproval();
                    Log.Info("已写入开机启动项：" + command);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                    ClearApproval();
                    Log.Info("已移除开机启动项");
                }
            }
        }

        /// <summary>
        /// 每次启动都对齐一次：开关是开的，就保证命令行指向当前 exe，
        /// 并清掉「已被禁用」的标记。程序换过目录、改过名字、
        /// 或被启动管理工具关掉过，靠这一步自愈。
        /// </summary>
        public static void EnsureCurrent()
        {
            try
            {
                if (!IsEnabled())
                {
                    return;
                }

                if (IsCurrent())
                {
                    ClearApproval();
                    return;
                }

                Log.Info("开机启动项指向旧路径，重写：" +
                    GetCommand() + " → " + BuildCommand());
                SetEnabled(true);
            }
            catch (Exception ex)
            {
                Log.Error("校正开机启动项失败", ex);
            }
        }

        /// <summary>删掉「用户禁用」标记（不存在就什么都别做）。</summary>
        private static void ClearApproval()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    ApprovedKeyPath, true))
                {
                    if (key == null)
                    {
                        return;
                    }
                    key.DeleteValue(ValueName, false);
                    key.DeleteValue(LegacyValueName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("清理启动项禁用标记失败", ex);
            }
        }
    }
}
