using System;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 第一次运行 exe 时的一次性初始化：把「开机自动启动」「图片右键菜单」
    /// 「打开方式」三个开关全部打开，并把结果写回配置，之后永不重复执行。
    ///
    /// 为什么要持久化标记：把 exe 拷到别的电脑后，%APPDATA% 里没有
    /// config.json，等于全新安装；这时候用户不该还要自己去托盘里点两下。
    /// 老用户的 config.json 里没有这个字段，按「已经跑过」处理，
    /// 不会把人家手动关掉的开机启动又偷偷打开。
    /// </summary>
    internal static class FirstRunSetup
    {
        /// <summary>本次进程刚刚完成过首次配置，托盘据此弹一次提示。</summary>
        public static bool JustCompleted;

        public static bool NeedsSetup(AppConfig config)
        {
            return config != null && !config.FirstRunDone;
        }

        /// <summary>正常启动走这里：只在真正的第一次运行时动手。</summary>
        public static void Run()
        {
            Apply(false);
        }

        /// <summary>--firstsetup 走这里：不管标记，强制重刷一遍（修复用）。</summary>
        public static void ForceRun()
        {
            Apply(true);
        }

        private static void Apply(bool force)
        {
            try
            {
                AppConfig config = ConfigStore.Load();
                if (!force && !NeedsSetup(config))
                {
                    return;
                }

                // 1) 开机自动启动（顺手清掉启动管理工具写的「已禁用」标记）
                StartupManager.SetEnabled(true);
                config.StartWithWindows = true;

                // 2) 图片右键菜单 + 3) 打开方式，共用同一个注册入口
                config.EnableImageShellMenu = true;
                string error;
                bool shellOk = ShellIntegration.SetEnabled(
                    true, Application.ExecutablePath, out error);

                config.FirstRunDone = true;
                ConfigStore.Save(config);
                JustCompleted = true;

                Log.Info((force ? "手动重跑首次配置" : "首次运行") +
                    "：已自动开启开机启动、图片右键菜单、打开方式" +
                    (shellOk ? "。" : "；右键菜单注册失败：" + error));
            }
            catch (Exception ex)
            {
                // 失败不写标记，下次启动会再试；也绝不让初始化异常挡住主程序。
                Log.Error("首次运行初始化失败", ex);
            }
        }
    }
}
