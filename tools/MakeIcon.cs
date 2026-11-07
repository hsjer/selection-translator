// 生成 app.ico：蓝底圆角方块 + 白色「译」字，多尺寸 32bpp ICO。
//
// 由 build.ps1 在 app.ico 不存在时自动编译运行：
//   csc /target:exe /r:System.dll /r:System.Drawing.dll tools\MakeIcon.cs
//   MakeIcon.exe <输出路径>
//
// 采用标准 BMP(DIB) 帧而不是 PNG 帧，兼容 .NET Framework 4.x 自带的
// csc.exe 的 /win32icon 参数（老编译器不认 PNG 压缩的 ICO 帧）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

internal static class MakeIcon
{
    private static readonly int[] Sizes =
        new int[] { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "app.ico";
        try
        {
            List<byte[]> frames = new List<byte[]>();
            for (int i = 0; i < Sizes.Length; i++)
            {
                using (Bitmap bitmap = Render(Sizes[i]))
                {
                    frames.Add(BuildDib(bitmap));
                }
            }

            using (FileStream stream = new FileStream(
                path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                // ICONDIR: reserved / type=1(icon) / image count
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)Sizes.Length);

                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int size = Sizes[i];
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)0);   // color count (0 = truecolor)
                    writer.Write((byte)0);   // reserved
                    writer.Write((ushort)1); // color planes
                    writer.Write((ushort)32); // bits per pixel
                    writer.Write((uint)frames[i].Length);
                    writer.Write((uint)offset);
                    offset += frames[i].Length;
                }
                for (int i = 0; i < frames.Count; i++)
                {
                    writer.Write(frames[i]);
                }
            }

            Console.WriteLine("Wrote " + path + " (" +
                new FileInfo(path).Length + " bytes, " +
                Sizes.Length + " sizes)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static FontFamily PickFamily()
    {
        string[] preferred = new string[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun"
        };
        FontFamily[] families = FontFamily.Families;
        for (int i = 0; i < preferred.Length; i++)
        {
            for (int j = 0; j < families.Length; j++)
            {
                if (string.Equals(
                    families[j].Name,
                    preferred[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return families[j];
                }
            }
        }
        return FontFamily.GenericSansSerif;
    }

    private static Bitmap Render(int size)
    {
        Bitmap bitmap = new Bitmap(
            size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float margin = size <= 24
                ? 1f : (float)Math.Round(size * 0.055f);
            float box = size - margin * 2f;
            float radius = Math.Max(2f, box * 0.24f);
            RectangleF bounds = new RectangleF(
                margin, margin, box, box);

            using (GraphicsPath shape =
                RoundedRect(bounds, radius))
            using (LinearGradientBrush brush =
                new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(255, 74, 138, 246),
                    Color.FromArgb(255, 28, 82, 190),
                    LinearGradientMode.Vertical))
            {
                g.FillPath(brush, shape);
            }

            // 顶部一道很淡的高光，让小尺寸图标也有层次。
            if (size >= 32)
            {
                RectangleF highlight = new RectangleF(
                    margin + box * 0.08f,
                    margin + box * 0.06f,
                    box * 0.84f,
                    box * 0.42f);
                using (GraphicsPath path =
                    RoundedRect(highlight, radius * 0.7f))
                using (LinearGradientBrush sheen =
                    new LinearGradientBrush(
                        highlight,
                        Color.FromArgb(38, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255),
                        LinearGradientMode.Vertical))
                {
                    g.FillPath(sheen, path);
                }
            }

            float fontSize = box * (size <= 24 ? 0.70f : 0.60f);
            using (Font font = new Font(
                PickFamily(), fontSize,
                FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                // GDI+ 中文字形的视觉重心略偏上，往下压一点点。
                RectangleF textBounds = new RectangleF(
                    margin,
                    margin + box * 0.025f,
                    box,
                    box);
                g.DrawString("译", font, Brushes.White,
                    textBounds, format);
            }
        }
        return bitmap;
    }

    private static GraphicsPath RoundedRect(
        RectangleF bounds, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2f;
        float x = bounds.X;
        float y = bounds.Y;
        float w = bounds.Width;
        float h = bounds.Height;
        path.AddArc(x, y, d, d, 180f, 90f);
        path.AddArc(x + w - d, y, d, d, 270f, 90f);
        path.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
        path.AddArc(x, y + h - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 一帧 = BITMAPINFOHEADER + 自下而上的 BGRA 像素 + 全 0 的 AND 掩码。
    /// 32bpp 带 alpha 时 Windows 用 alpha 通道，AND 掩码仅作占位。
    /// </summary>
    private static byte[] BuildDib(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int stride = size * 4;
        byte[] pixels = new byte[stride * size];

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < size; y++)
            {
                IntPtr row = new IntPtr(
                    data.Scan0.ToInt64() +
                    (long)(size - 1 - y) * data.Stride);
                Marshal.Copy(row, pixels, y * stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        int maskStride = ((size + 31) / 32) * 4;
        byte[] mask = new byte[maskStride * size];

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(stream))
        {
            w.Write((uint)40);              // biSize
            w.Write((int)size);             // biWidth
            w.Write((int)(size * 2));       // biHeight: XOR + AND
            w.Write((ushort)1);             // biPlanes
            w.Write((ushort)32);            // biBitCount
            w.Write((uint)0);               // BI_RGB
            w.Write((uint)(pixels.Length + mask.Length));
            w.Write((int)0);                // biXPelsPerMeter
            w.Write((int)0);                // biYPelsPerMeter
            w.Write((uint)0);               // biClrUsed
            w.Write((uint)0);               // biClrImportant
            w.Write(pixels);
            w.Write(mask);
            w.Flush();
            return stream.ToArray();
        }
    }
}
