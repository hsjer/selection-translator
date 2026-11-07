using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SelectionTranslator
{
    internal static class IconFactory
    {
        public static Icon Create()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint =
                        TextRenderingHint.AntiAliasGridFit;

                    using (SolidBrush background =
                        new SolidBrush(Color.FromArgb(45, 110, 220)))
                    {
                        graphics.FillEllipse(background, 0, 0, 31, 31);
                    }

                    using (Font font = new Font(
                        "Microsoft YaHei UI",
                        15F,
                        FontStyle.Bold,
                        GraphicsUnit.Pixel))
                    using (StringFormat format = new StringFormat())
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        graphics.DrawString(
                            "译",
                            font,
                            Brushes.White,
                            new RectangleF(0, 0, 32, 32),
                            format);
                    }
                }

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
