namespace SelectionTranslator
{
    /// <summary>
    /// 程序对外显示的名字。只有这一处定义，窗口标题、托盘、消息框都引用它。
    /// 内部标识（互斥量、%APPDATA% 数据目录、注册表值名以外的键）仍沿用
    /// 旧名 SelectionTranslator，保证老用户升级后配置、日志、开机启动都不丢。
    /// </summary>
    internal static class AppInfo
    {
        public const string Name = "工具侠";

        /// <summary>内部标识，不要随意改：数据目录与单实例互斥量都用它。</summary>
        public const string Id = "SelectionTranslator";

        public const string Version = "1.1.0.0";

        public const string Slogan =
            "选中即译 · 截图识字 · 语音上屏 · 图片工具";
    }
}
