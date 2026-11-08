using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SelectionTranslator
{
    /// <summary>
    /// 「工具侠」的图标：蓝色渐变圆角方块 + 白色扳手。
    /// 托盘、exe 文件图标、右键菜单图标都用这一份画法，避免三处不一致。
    ///
    /// 这里只用 System.Drawing，不引用 WinForms，因为 tools\MakeIcon.cs
    /// 会把这个文件一起编译成独立的小工具来生成 app.ico。
    /// </summary>
    internal static class AppIcon
    {
        public static readonly int[] IcoSizes =
            new int[] { 16, 24, 32, 48, 64, 128, 256 };

        /// <summary>按目标像素尺寸生成图标位图（内部按 4~6 倍超采样后缩下来）。</summary>
        public static Bitmap Render(int size)
        {
            int scale = size <= 48 ? 6 : 4;
            using (Bitmap big = RenderDirect(size * scale))
            {
                Bitmap result = new Bitmap(
                    size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode =
                        InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality =
                        CompositingQuality.HighQuality;
                    g.DrawImage(big,
                        new Rectangle(0, 0, size, size),
                        new Rectangle(0, 0, big.Width, big.Height),
                        GraphicsUnit.Pixel);
                }
                return result;
            }
        }

        private static Bitmap RenderDirect(int size)
        {
            Bitmap bitmap = new Bitmap(
                size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float margin = size * 0.035f;
                float box = size - margin * 2f;
                float radius = box * 0.235f;
                RectangleF bounds = new RectangleF(
                    margin, margin, box, box);

                using (GraphicsPath shape = RoundedRect(bounds, radius))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(255, 86, 150, 255),
                    Color.FromArgb(255, 31, 74, 189),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, shape);
                }

                // 顶部一层很淡的高光，让扁平图标有点体积。
                RectangleF sheen = new RectangleF(
                    margin + box * 0.08f,
                    margin + box * 0.05f,
                    box * 0.84f,
                    box * 0.44f);
                using (GraphicsPath path = RoundedRect(sheen, radius * 0.75f))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    sheen,
                    Color.FromArgb(46, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, path);
                }

                DrawWrench(g, bounds);
            }
            return bitmap;
        }

        /// <summary>
        /// 白色扳手，斜 45°：右上是一个带开口的扳手头，左下是包胶手柄。
        /// 用 Region 做布尔运算，再整体按 4~6 倍超采样缩小，边缘不会有锯齿。
        /// </summary>
        private static void DrawWrench(Graphics g, RectangleF box)
        {
            float u = box.Width / 100f;
            float x = box.X;
            float y = box.Y;

            PointF head = new PointF(x + 62f * u, y + 35f * u);
            PointF tail = new PointF(x + 28f * u, y + 69f * u);
            float headRadius = 21.5f * u;
            float handleWidth = 15.5f * u;

            using (Region shape = new Region())
            {
                shape.MakeEmpty();

                using (GraphicsPath handle = new GraphicsPath())
                {
                    handle.AddLine(head, tail);
                    using (Pen wide = new Pen(Color.Black, handleWidth))
                    {
                        wide.LineJoin = LineJoin.Round;
                        wide.StartCap = LineCap.Round;
                        wide.EndCap = LineCap.Round;
                        handle.Widen(wide);
                    }
                    shape.Union(handle);
                }

                using (GraphicsPath bulb = new GraphicsPath())
                {
                    bulb.AddEllipse(
                        head.X - headRadius,
                        head.Y - headRadius,
                        headRadius * 2f,
                        headRadius * 2f);
                    shape.Union(bulb);
                }

                // 开口：从圆心朝右上斜着切出去的一条缝，做出「开口扳手」的样子。
                float dx = 0.7071f;
                float dy = -0.7071f;
                float half = headRadius * 0.29f;
                float outer = headRadius * 1.04f;
                float inner = headRadius * 0.22f;

                PointF[] slot = new PointF[4];
                slot[0] = new PointF(
                    head.X + dx * outer - dy * half,
                    head.Y + dy * outer + dx * half);
                slot[1] = new PointF(
                    head.X + dx * outer + dy * half,
                    head.Y + dy * outer - dx * half);
                slot[2] = new PointF(
                    head.X + dx * inner + dy * half,
                    head.Y + dy * inner - dx * half);
                slot[3] = new PointF(
                    head.X + dx * inner - dy * half,
                    head.Y + dy * inner + dx * half);

                using (GraphicsPath cut = new GraphicsPath())
                {
                    cut.AddPolygon(slot);
                    shape.Exclude(cut);
                }

                using (SolidBrush white = new SolidBrush(Color.White))
                {
                    g.FillRegion(white, shape);
                }
            }
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
        /// 写出多尺寸 ICO。用 BMP(DIB) 帧而不是 PNG 帧，老 csc 的
        /// /win32icon 才认。
        /// </summary>
        public static void WriteIco(string path)
        {
            int count = IcoSizes.Length;
            byte[][] frames = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                using (Bitmap bitmap = Render(IcoSizes[i]))
                {
                    frames[i] = BuildDib(bitmap);
                }
            }

            using (FileStream stream = new FileStream(
                path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)count);

                int offset = 6 + 16 * count;
                for (int i = 0; i < count; i++)
                {
                    int size = IcoSizes[i];
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)frames[i].Length);
                    writer.Write((uint)offset);
                    offset += frames[i].Length;
                }
                for (int i = 0; i < count; i++)
                {
                    writer.Write(frames[i]);
                }
            }
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
                w.Write((uint)40);
                w.Write((int)size);
                w.Write((int)(size * 2));
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)0);
                w.Write((uint)(pixels.Length + mask.Length));
                w.Write((int)0);
                w.Write((int)0);
                w.Write((uint)0);
                w.Write((uint)0);
                w.Write(pixels);
                w.Write(mask);
                w.Flush();
                return stream.ToArray();
            }
        }
    }
}
