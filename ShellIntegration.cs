using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SelectionTranslator
{
    /// <summary>
    /// 把「使用工具侠打开图片」挂到资源管理器的图片右键菜单上。
    ///
    /// 只写 HKCU（当前用户），不需要管理员权限，也不改动任何文件关联；
    /// 卸载/关闭开关时把整棵子键删掉，系统里不留痕迹。
    /// 挂在 SystemFileAssociations\image 下，所以 png / jpg / bmp / gif /
    /// tif 这些「图片」类型都能看到这一项。
    /// </summary>
    internal static class ShellIntegration
    {
        public const string KeyName = "ToolboxHero";

        public const string MenuText = "使用" + AppInfo.Name + "打开图片";

        private const string ShellRoot =
            @"Software\Classes\SystemFileAssociations\image\shell";

        public const string MenuKeyPath = ShellRoot + @"\" + KeyName;

        public const string CommandKeyPath = MenuKeyPath + @"\command";

        /// <summary>右键菜单实际执行的命令行，路径加引号，%1 是选中的文件。</summary>
        public static string BuildCommand(string exePath)
        {
            string exe = TrimQuotes(exePath);
            if (exe.Length == 0)
            {
                return "";
            }
            return "\"" + exe + "\" \"%1\"";
        }

        /// <summary>图标值，写成 "路径",0 这种形式，带空格的路径必须加引号。</summary>
        public static string BuildIconValue(string exePath)
        {
            string exe = TrimQuotes(exePath);
            if (exe.Length == 0)
            {
                return "";
            }
            return "\"" + exe + "\",0";
        }

        public static string TrimQuotes(string path)
        {
            string value = (path ?? "").Trim();
            while (value.Length >= 2 &&
                value[0] == '"' &&
                value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2).Trim();
            }
            return value;
        }

        public static bool IsEnabled()
        {
            return !string.IsNullOrEmpty(GetCommand());
        }

        /// <summary>读回已经写入的命令行，空字符串表示没有注册。</summary>
        public static string GetCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    CommandKeyPath, false))
                {
                    if (key == null)
                    {
                        return "";
                    }
                    object value = key.GetValue(null);
                    return value as string ?? "";
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取图片右键菜单失败", ex);
                return "";
            }
        }

        /// <summary>当前注册的命令行是否就指向这个 exe（程序被移动/改名后要重修）。</summary>
        public static bool IsCurrent(string exePath)
        {
            return GetCommand() == BuildCommand(exePath);
        }

        public static bool SetEnabled(bool enabled, string exePath,
            out string error)
        {
            error = null;
            try
            {
                if (!enabled)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath, false);
                    return true;
                }

                string exe = TrimQuotes(exePath);
                if (exe.Length == 0)
                {
                    error = "没有拿到程序路径。";
                    return false;
                }

                using (RegistryKey menu = Registry.CurrentUser.CreateSubKey(
                    MenuKeyPath))
                {
                    if (menu == null)
                    {
                        error = "无法创建注册表项。";
                        return false;
                    }
                    menu.SetValue(null, MenuText);
                    menu.SetValue("Icon", BuildIconValue(exe));
                    // 一次只交给程序一个文件，多选时系统不会把一串路径塞进来。
                    menu.SetValue("MultiSelectModel", "Single");
                    using (RegistryKey command = menu.CreateSubKey("command"))
                    {
                        if (command == null)
                        {
                            error = "无法创建命令行项。";
                            return false;
                        }
                        command.SetValue(null, BuildCommand(exe));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("写入图片右键菜单失败", ex);
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 启动时对齐一次：开关打开就保证菜单指向当前 exe（换了目录也能自愈），
        /// 开关关闭但菜单还在（比如上次删失败）就清掉。
        /// </summary>
        public static void Sync(AppConfig config, string exePath)
        {
            try
            {
                bool want = config == null || config.EnableImageShellMenu;
                if (want)
                {
                    if (!IsCurrent(exePath))
                    {
                        string error;
                        if (!SetEnabled(true, exePath, out error))
                        {
                            Log.Info("图片右键菜单注册失败：" + error);
                        }
                    }
                    return;
                }

                if (IsEnabled())
                {
                    string error;
                    SetEnabled(false, exePath, out error);
                }
            }
            catch (Exception ex)
            {
                Log.Error("同步图片右键菜单失败", ex);
            }
        }
    }
}
