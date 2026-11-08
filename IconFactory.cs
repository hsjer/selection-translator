using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;

namespace SelectionTranslator
{
    internal static class IconFactory
    {
        // build.ps1 用 /resource:app.ico,SelectionTranslator.app.ico
        // 把同一张图标嵌成托管资源，托盘与 exe 文件图标保持一致。
        private const string ResourceName = "SelectionTranslator.app.ico";

        public static Icon Create()
        {
            Icon embedded = LoadEmbeddedIcon();
            if (embedded != null)
            {
                return embedded;
            }
            return DrawFallbackIcon();
        }

        /// <summary>优先使用编译进 exe 的多尺寸图标资源。</summary>
        private static Icon LoadEmbeddedIcon()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(ResourceName))
                {
                    if (stream == null || stream.Length == 0)
                    {
                        return null;
                    }
                    // ICO 里第一帧是 16x16，正好是托盘小图标尺寸；
                    // Icon 会保留整个 ICO 数据，需要大尺寸时自行选取。
                    return new Icon(stream);
                }
            }
            catch
            {
                // 资源缺失或损坏时退回运行期绘制，不影响托盘启动。
                return null;
            }
        }

        /// <summary>兜底：老版本 exe / 资源缺失时运行期画一个同样的图标。</summary>
        private static Icon DrawFallbackIcon()
        {
            using (Bitmap bitmap = AppIcon.Render(32))
            {
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }
    }
}
