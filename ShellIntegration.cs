using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SelectionTranslator
{
    /// <summary>
    /// 资源管理器里的两处入口，都由托盘里的同一个开关控制：
    ///
    /// 1. 经典右键菜单「使用工具侠打开图片」——挂在
    ///    SystemFileAssociations\image\shell 下，png/jpg/bmp/gif/tif 通用；
    /// 2. 「打开方式(H)」子菜单里的一行「工具侠」——按 Windows 的
    ///    「应用程序注册」规范写 ProgID + OpenWithProgids + SupportedTypes。
    ///
    /// 全部只写 HKCU（当前用户），不需要管理员权限，**不改任何文件的默认
    /// 打开方式**（UserChoice 原样保留）。卸载/关闭开关时只删自己写的键和值，
    /// 别人注册的 OpenWithProgids 一律保留。
    /// </summary>
    internal static class ShellIntegration
    {
        public const string KeyName = "ToolboxHero";

        /// <summary>我们自己的 ProgID，用来在「打开方式」列表里占一行。</summary>
        public const string ProgId = "ToolboxHero.Image";

        public const string MenuText = "使用" + AppInfo.Name + "打开图片";

        private const string ClassesRoot = @"Software\Classes";
        private const string ShellRoot =
            @"Software\Classes\SystemFileAssociations\image\shell";
        private const string FileExtsRoot =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

        public const string MenuKeyPath = ShellRoot + @"\" + KeyName;
        public const string CommandKeyPath = MenuKeyPath + @"\command";
        public const string ProgIdKeyPath = ClassesRoot + @"\" + ProgId;

        /// <summary>注册进「打开方式」的图片扩展名，和 ImageToolKit 支持的一致。</summary>
        public static readonly string[] ImageExtensions = new string[]
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
        };

        public static string OpenWithProgidsPath(string extension)
        {
            return ClassesRoot + "\\" + extension + @"\OpenWithProgids";
        }

        public static string FileExtsProgidsPath(string extension)
        {
            return FileExtsRoot + "\\" + extension + @"\OpenWithProgids";
        }

        /// <summary>Applications 下的键名按 exe 文件名算（Windows 的规矩）。</summary>
        public static string AppFileName(string exePath)
        {
            string exe = TrimQuotes(exePath);
            if (exe.Length == 0)
            {
                exe = Application.ExecutablePath;
            }
            string name = Path.GetFileName(exe);
            return string.IsNullOrEmpty(name) ? AppInfo.Name + ".exe" : name;
        }

        public static string AppKeyPath(string exePath)
        {
            return ClassesRoot + @"\Applications\" + AppFileName(exePath);
        }

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

        private static string ReadDefault(string path)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    path, false))
                {
                    if (key == null)
                    {
                        return "";
                    }
                    return key.GetValue(null) as string ?? "";
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取注册表失败：" + path, ex);
                return "";
            }
        }

        public static bool IsEnabled()
        {
            return !string.IsNullOrEmpty(GetCommand());
        }

        /// <summary>「打开方式」那一行是否已经注册。</summary>
        public static bool IsOpenWithEnabled()
        {
            return !string.IsNullOrEmpty(GetOpenWithCommand());
        }

        /// <summary>读回经典右键菜单的命令行，空字符串表示没有注册。</summary>
        public static string GetCommand()
        {
            return ReadDefault(CommandKeyPath);
        }

        /// <summary>读回「打开方式」ProgID 的命令行。</summary>
        public static string GetOpenWithCommand()
        {
            return ReadDefault(ProgIdKeyPath + @"\shell\open\command");
        }

        /// <summary>两处注册是否都指向这个 exe（程序被移动/改名后要重修）。</summary>
        public static bool IsCurrent(string exePath)
        {
            string command = BuildCommand(exePath);
            return command.Length > 0 &&
                GetCommand() == command &&
                GetOpenWithCommand() == command;
        }

        public static bool SetEnabled(bool enabled, string exePath,
            out string error)
        {
            error = null;
            try
            {
                if (!enabled)
                {
                    UnregisterOpenWith(exePath);
                    Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath, false);
                    NativeMethods.NotifyAssociationChanged();
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

                RegisterOpenWith(exe);
                NativeMethods.NotifyAssociationChanged();
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
        /// 按 Windows「Registering an Application for Use with Open With」的
        /// 结构写三处：ProgID、Applications\&lt;exe&gt;、每个扩展名的
        /// OpenWithProgids。三处缺一个，Win11 的「打开方式」就可能不列出来。
        /// </summary>
        private static void RegisterOpenWith(string exe)
        {
            string command = BuildCommand(exe);
            string icon = BuildIconValue(exe);

            // ---- 1. ProgID：决定列表里显示的名字、图标和点击后的动作 ----
            using (RegistryKey prog = Registry.CurrentUser.CreateSubKey(
                ProgIdKeyPath))
            {
                if (prog != null)
                {
                    prog.SetValue(null, AppInfo.Name + " 图片文件");
                    using (RegistryKey iconKey =
                        prog.CreateSubKey("DefaultIcon"))
                    {
                        if (iconKey != null)
                        {
                            iconKey.SetValue(null, icon);
                        }
                    }
                    // ApplicationName 才是「打开方式」列表里显示的那行字。
                    using (RegistryKey info = prog.CreateSubKey("Application"))
                    {
                        if (info != null)
                        {
                            info.SetValue(
                                "ApplicationName", AppInfo.Name);
                            info.SetValue("ApplicationIcon", icon);
                            info.SetValue(
                                "ApplicationDescription",
                                "用" + AppInfo.Name + "查看、转换和处理图片");
                            info.SetValue("AppUserModelId", ProgId);
                        }
                    }
                    WriteOpenCommand(prog, command);
                }
            }

            // ---- 2. Applications\<exe>：让「选择其他应用」里也能认出它 ----
            using (RegistryKey app = Registry.CurrentUser.CreateSubKey(
                AppKeyPath(exe)))
            {
                if (app != null)
                {
                    app.SetValue("FriendlyAppName", AppInfo.Name);
                    // 系统有时会给「选过一次的应用」打上 NoOpenWith，
                    // 有这个值时列表里会被过滤掉。
                    if (Array.IndexOf(
                        app.GetValueNames(), "NoOpenWith") >= 0)
                    {
                        app.DeleteValue("NoOpenWith", false);
                    }
                    WriteOpenCommand(app, command);
                    using (RegistryKey types =
                        app.CreateSubKey("SupportedTypes"))
                    {
                        if (types != null)
                        {
                            for (int i = 0; i < ImageExtensions.Length; i++)
                            {
                                types.SetValue(ImageExtensions[i], "");
                            }
                        }
                    }
                }
            }

            // ---- 3. 每个扩展名的候选表：只追加自己那一个值 ----
            for (int i = 0; i < ImageExtensions.Length; i++)
            {
                AddProgIdValue(ImageExtensions[i]);
            }
        }

        private static void WriteOpenCommand(RegistryKey root, string command)
        {
            using (RegistryKey shell = root.CreateSubKey("shell"))
            {
                if (shell == null)
                {
                    return;
                }
                using (RegistryKey open = shell.CreateSubKey("open"))
                {
                    if (open == null)
                    {
                        return;
                    }
                    open.SetValue("FriendlyAppName", AppInfo.Name);
                    using (RegistryKey cmd = open.CreateSubKey("command"))
                    {
                        if (cmd != null)
                        {
                            cmd.SetValue(null, command);
                        }
                    }
                }
            }
        }

        private static void AddProgIdValue(string extension)
        {
            // 只有当 HKCU 里本来就有这个 .ext 键时才写 Classes，
            // 免得新建一个空的 .ext 键把 HKLM 的默认关联遮掉。
            using (RegistryKey ext = Registry.CurrentUser.OpenSubKey(
                ClassesRoot + "\\" + extension))
            {
                if (ext != null)
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                        OpenWithProgidsPath(extension)))
                    {
                        if (key != null)
                        {
                            key.SetValue(ProgId, "");
                        }
                    }
                }
            }

            // Explorer 的用户级候选表，纯追加，不会影响默认程序。
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                FileExtsProgidsPath(extension)))
            {
                if (key != null)
                {
                    key.SetValue(ProgId, "");
                }
            }
        }

        /// <summary>某个扩展名的候选表里有没有我们这一行（自检 / 诊断用）。</summary>
        public static bool HasProgIdValue(string extension)
        {
            string[] paths = new string[]
            {
                OpenWithProgidsPath(extension),
                FileExtsProgidsPath(extension)
            };
            bool found = false;
            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        paths[i], false))
                    {
                        if (key != null &&
                            Array.IndexOf(
                                key.GetValueNames(), ProgId) >= 0)
                        {
                            found = true;
                        }
                    }
                }
                catch
                {
                    // 读不到就当没有，诊断函数不该抛异常。
                }
            }
            return found;
        }

        private static void UnregisterOpenWith(string exePath)
        {
            Registry.CurrentUser.DeleteSubKeyTree(ProgIdKeyPath, false);
            for (int i = 0; i < ImageExtensions.Length; i++)
            {
                RemoveValue(
                    OpenWithProgidsPath(ImageExtensions[i]), ProgId);
                RemoveValue(
                    FileExtsProgidsPath(ImageExtensions[i]), ProgId);
            }
            Registry.CurrentUser.DeleteSubKeyTree(
                AppKeyPath(exePath), false);
        }

        /// <summary>只删自己那一个值，键本身和别人注册的应用都留着。</summary>
        private static void RemoveValue(string path, string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    path, true))
                {
                    if (key == null)
                    {
                        return;
                    }
                    if (Array.IndexOf(key.GetValueNames(), name) >= 0)
                    {
                        key.DeleteValue(name, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("移除打开方式注册失败：" + path, ex);
            }
        }

        /// <summary>
        /// 启动时对齐一次：开关打开就保证两处都指向当前 exe（换了目录也能
        /// 自愈），开关关闭但注册表里还留着东西就清掉。
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

                if (IsEnabled() || IsOpenWithEnabled())
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
