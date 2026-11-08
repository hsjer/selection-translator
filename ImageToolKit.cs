using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace SelectionTranslator
{
    /// <summary>一张图片的文件信息，供查看器右侧信息面板显示。</summary>
    internal sealed class ImageFileInfo
    {
        public string FilePath { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }
        public string FormatName { get; set; }
        public string Bits { get; set; }
        public long SizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime Modified { get; set; }
        public int Index { get; set; }
        public int Total { get; set; }
    }

    /// <summary>可转换的目标格式。</summary>
    internal sealed class ImageFormatOption
    {
        public ImageFormatOption(
            string key, string label, string extension, bool lossy)
        {
            Key = key;
            Label = label;
            Extension = extension;
            Lossy = lossy;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public string Extension { get; private set; }

        /// <summary>有损格式（只有 JPEG 需要问质量）。</summary>
        public bool Lossy { get; private set; }

        public static readonly ImageFormatOption[] All = new ImageFormatOption[]
        {
            new ImageFormatOption("png", "PNG", ".png", false),
            new ImageFormatOption("jpg", "JPG", ".jpg", true),
            new ImageFormatOption("bmp", "BMP", ".bmp", false),
            new ImageFormatOption("gif", "GIF", ".gif", false),
            new ImageFormatOption("tif", "TIFF", ".tif", false)
        };

        public static ImageFormatOption Find(string key)
        {
            string value = (key ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Key == value)
                {
                    return All[i];
                }
            }
            return All[0];
        }

        /// <summary>按扩展名（可带点）找格式，找不到按 PNG 处理。</summary>
        public static ImageFormatOption FromExtension(string extension)
        {
            string ext = (extension ?? "").Trim().ToLowerInvariant();
            if (ext.StartsWith("."))
            {
                ext = ext.Substring(1);
            }
            if (ext == "jpeg")
            {
                ext = "jpg";
            }
            if (ext == "tiff")
            {
                ext = "tif";
            }
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Key == ext)
                {
                    return All[i];
                }
            }
            return All[0];
        }
    }

    /// <summary>
    /// 图片工具的全部纯逻辑：加载（不长期占用文件句柄）、编码保存、
    /// 缩放 / 旋转 / 裁剪、目录内排序、重命名校验与体积预估。
    /// 不引用任何 UI 类型，方便自检直接调用。
    /// </summary>
    internal static class ImageToolKit
    {
        public const string OpenFilter =
            "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|" +
            "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp|" +
            "GIF|*.gif|TIFF|*.tif;*.tiff|所有文件|*.*";

        public static readonly string[] Extensions = new string[]
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
        };

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string x, string y);

        public static bool IsImagePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                return false;
            }
            string lower = ext.ToLowerInvariant();
            for (int i = 0; i < Extensions.Length; i++)
            {
                if (Extensions[i] == lower)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 读进内存再解码，这样文件句柄立刻释放：重命名、覆盖、删除文件
        /// 都不会因为我们正显示着这张图而失败。
        /// </summary>
        public static Bitmap Load(string path, out string error)
        {
            error = null;
            if (!File.Exists(path))
            {
                error = "文件不存在";
                return null;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Image raw = Image.FromStream(stream, false, false))
                {
                    bool hasAlpha =
                        (raw.PixelFormat & PixelFormat.Alpha) != 0;
                    PixelFormat target = hasAlpha
                        ? PixelFormat.Format32bppArgb
                        : PixelFormat.Format24bppRgb;
                    Bitmap copy = new Bitmap(raw.Width, raw.Height, target);
                    using (Graphics g = Graphics.FromImage(copy))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode =
                            InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(raw, new Rectangle(
                            0, 0, copy.Width, copy.Height));
                    }
                    return copy;
                }
            }
            catch (OutOfMemoryException)
            {
                error = "无法解码：文件可能不是图片，或者图片过大";
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                error = "没有权限读取这个文件";
                return null;
            }
            catch (Exception ex)
            {
                error = "读取失败：" + ex.Message;
                return null;
            }
        }

        public static ImageFileInfo Describe(
            string path, Bitmap image, List<string> siblings)
        {
            ImageFileInfo info = new ImageFileInfo();
            info.FilePath = path;
            info.Name = Path.GetFileName(path);
            info.Folder = Path.GetDirectoryName(path) ?? "";
            info.Width = image == null ? 0 : image.Width;
            info.Height = image == null ? 0 : image.Height;
            info.FormatName = DetectFormatName(image, Path.GetExtension(path));
            info.Bits = image == null ? "—" : DescribeBits(image.PixelFormat);
            try
            {
                FileInfo file = new FileInfo(path);
                info.SizeBytes = file.Exists ? file.Length : 0;
                info.Modified = file.Exists
                    ? file.LastWriteTime : DateTime.MinValue;
            }
            catch
            {
                info.SizeBytes = 0;
                info.Modified = DateTime.MinValue;
            }
            info.Total = siblings == null ? 0 : siblings.Count;
            info.Index = 0;
            if (siblings != null)
            {
                for (int i = 0; i < siblings.Count; i++)
                {
                    if (string.Equals(siblings[i], path,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        info.Index = i + 1;
                        break;
                    }
                }
            }
            return info;
        }

        /// <summary>类型列：优先相信图片自身的编码格式，其次看扩展名。</summary>
        public static string DetectFormatName(Image image, string extension)
        {
            if (image != null)
            {
                Guid guid = image.RawFormat.Guid;
                if (guid == ImageFormat.Png.Guid)
                {
                    return "PNG";
                }
                if (guid == ImageFormat.Jpeg.Guid)
                {
                    return "JPEG";
                }
                if (guid == ImageFormat.Bmp.Guid)
                {
                    return "BMP";
                }
                if (guid == ImageFormat.Gif.Guid)
                {
                    return "GIF";
                }
                if (guid == ImageFormat.Tiff.Guid)
                {
                    return "TIFF";
                }
                if (guid == ImageFormat.Icon.Guid)
                {
                    return "ICO";
                }
            }
            return FormatLabel(extension);
        }

        public static string FormatLabel(string extension)
        {
            string ext = (extension ?? "").Trim().TrimStart('.');
            if (ext.Length == 0)
            {
                return "未知";
            }
            string lower = ext.ToLowerInvariant();
            if (lower == "jpg" || lower == "jpeg")
            {
                return "JPEG";
            }
            if (lower == "tif" || lower == "tiff")
            {
                return "TIFF";
            }
            return lower.ToUpperInvariant();
        }

        public static string DescribeBits(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.Format1bppIndexed:
                    return "1 位";
                case PixelFormat.Format4bppIndexed:
                    return "4 位";
                case PixelFormat.Format8bppIndexed:
                    return "8 位";
                case PixelFormat.Format16bppGrayScale:
                    return "16 位灰度";
                case PixelFormat.Format24bppRgb:
                    return "24 位";
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    return "32 位";
                case PixelFormat.Format48bppRgb:
                    return "48 位";
                case PixelFormat.Format64bppArgb:
                    return "64 位";
                default:
                    return "—";
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            double kb = bytes / 1024.0;
            if (kb < 1024.0)
            {
                return kb.ToString("0.##",
                    CultureInfo.InvariantCulture) + " KB";
            }
            double mb = kb / 1024.0;
            if (mb < 1024.0)
            {
                return mb.ToString("0.##",
                    CultureInfo.InvariantCulture) + " MB";
            }
            return (mb / 1024.0).ToString("0.##",
                CultureInfo.InvariantCulture) + " GB";
        }

        public static string FormatDateTime(DateTime time)
        {
            if (time == DateTime.MinValue)
            {
                return "—";
            }
            return time.ToString("yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);
        }

        /// <summary>路径太长时省略中间，"D:\下载\C…程入门\img"。</summary>
        public static string ShortenPath(string path, int maxChars)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxChars)
            {
                return path ?? "";
            }
            int keep = maxChars - 1;
            int head = Math.Max(4, keep / 3);
            int tail = keep - head;
            return path.Substring(0, head) + "…" +
                path.Substring(path.Length - tail);
        }

        public static bool HasAlpha(Bitmap image)
        {
            return image != null &&
                (image.PixelFormat & PixelFormat.Alpha) != 0;
        }

        /// <summary>编码成内存字节。JPEG 走质量参数，其它格式按原样保存。</summary>
        public static byte[] Encode(
            Bitmap image, string formatKey, int quality, out string error)
        {
            error = null;
            if (image == null)
            {
                error = "没有可保存的图片";
                return null;
            }
            ImageFormatOption option = ImageFormatOption.Find(formatKey);
            Bitmap source = image;
            Bitmap flattened = null;
            try
            {
                if (option.Key != "png" && HasAlpha(image))
                {
                    flattened = Flatten(image);
                    source = flattened;
                }
                using (MemoryStream stream = new MemoryStream())
                {
                    if (option.Key == "jpg")
                    {
                        SaveJpeg(source, stream, quality);
                    }
                    else
                    {
                        source.Save(stream, FormatOf(option.Key));
                    }
                    return stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                error = "编码失败：" + ex.Message;
                return null;
            }
            finally
            {
                if (flattened != null)
                {
                    flattened.Dispose();
                }
            }
        }

        public static bool Save(
            Bitmap image, string path, string formatKey, int quality,
            out string error)
        {
            byte[] bytes = Encode(image, formatKey, quality, out error);
            if (bytes == null)
            {
                return false;
            }
            return SaveBytes(bytes, path, out error);
        }

        public static bool SaveBytes(
            byte[] bytes, string path, out string error)
        {
            error = null;
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = "写入失败：" + ex.Message;
                return false;
            }
        }

        private static void SaveJpeg(
            Bitmap image, Stream stream, int quality)
        {
            int value = quality < 1 ? 1 : (quality > 100 ? 100 : quality);
            ImageCodecInfo codec = null;
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].FormatID == ImageFormat.Jpeg.Guid)
                {
                    codec = codecs[i];
                    break;
                }
            }
            if (codec == null)
            {
                image.Save(stream, ImageFormat.Jpeg);
                return;
            }
            using (EncoderParameters parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, (long)value);
                image.Save(stream, codec, parameters);
            }
        }

        public static ImageFormat FormatOf(string formatKey)
        {
            switch ((formatKey ?? "").ToLowerInvariant())
            {
                case "jpg":
                case "jpeg":
                    return ImageFormat.Jpeg;
                case "bmp":
                    return ImageFormat.Bmp;
                case "gif":
                    return ImageFormat.Gif;
                case "tif":
                case "tiff":
                    return ImageFormat.Tiff;
                default:
                    return ImageFormat.Png;
            }
        }

        /// <summary>带透明通道的图转成不透明格式时，先铺一层白底。</summary>
        private static Bitmap Flatten(Bitmap image)
        {
            Bitmap result = new Bitmap(
                image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.White);
                g.CompositingMode = CompositingMode.SourceOver;
                g.DrawImage(image, new Rectangle(
                    0, 0, image.Width, image.Height));
            }
            return result;
        }

        public static Bitmap Resize(Bitmap source, int width, int height)
        {
            if (width < 1)
            {
                width = 1;
            }
            if (height < 1)
            {
                height = 1;
            }
            PixelFormat target = HasAlpha(source)
                ? PixelFormat.Format32bppArgb
                : PixelFormat.Format24bppRgb;
            Bitmap result = new Bitmap(width, height, target);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    // 边缘像素向外复制，避免缩放后出现半透明黑边。
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source,
                        new Rectangle(0, 0, width, height),
                        0, 0, source.Width, source.Height,
                        GraphicsUnit.Pixel, attributes);
                }
            }
            return result;
        }

        public static Bitmap Rotate(Bitmap source, bool clockwise)
        {
            Bitmap result = (Bitmap)source.Clone();
            result.RotateFlip(clockwise
                ? RotateFlipType.Rotate90FlipNone
                : RotateFlipType.Rotate270FlipNone);
            return result;
        }

        public static Bitmap Crop(Bitmap source, Rectangle rect)
        {
            Rectangle safe = Rectangle.Intersect(
                rect, new Rectangle(0, 0, source.Width, source.Height));
            if (safe.Width < 1 || safe.Height < 1)
            {
                return null;
            }
            PixelFormat target = HasAlpha(source)
                ? PixelFormat.Format32bppArgb
                : PixelFormat.Format24bppRgb;
            Bitmap result = new Bitmap(safe.Width, safe.Height, target);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source,
                    new Rectangle(0, 0, safe.Width, safe.Height),
                    safe.X, safe.Y, safe.Width, safe.Height,
                    GraphicsUnit.Pixel);
            }
            return result;
        }

        /// <summary>同目录内的图片，按资源管理器习惯的自然序排列。</summary>
        public static List<string> SiblingImages(string path)
        {
            List<string> result = new List<string>();
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    return result;
                }
                string[] files = Directory.GetFiles(folder);
                for (int i = 0; i < files.Length; i++)
                {
                    if (IsImagePath(files[i]))
                    {
                        result.Add(files[i]);
                    }
                }
                result.Sort(delegate(string a, string b)
                {
                    int name = CompareName(
                        Path.GetFileName(a), Path.GetFileName(b));
                    if (name != 0)
                    {
                        return name;
                    }
                    return string.Compare(a, b,
                        StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
                // 目录不可读时退化成「只有当前这一张」。
            }
            return result;
        }

        private static int CompareName(string a, string b)
        {
            try
            {
                return StrCmpLogicalW(a ?? "", b ?? "");
            }
            catch
            {
                return string.Compare(a, b,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>同名文件已存在时追加 (1)、(2)…，不覆盖任何已有文件。</summary>
        public static string UniquePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }
            string folder = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(
                    folder, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return Path.Combine(
                folder, name + " (" + Guid.NewGuid().ToString("N") +
                ")" + ext);
        }

        /// <summary>在文件名后面加后缀（改尺寸 / 压缩的默认输出名）。</summary>
        public static string WithSuffix(
            string path, string suffix, string extension)
        {
            string folder = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = string.IsNullOrEmpty(extension)
                ? Path.GetExtension(path) : extension;
            return UniquePath(Path.Combine(folder, name + suffix + ext));
        }

        /// <summary>重命名只要新名字（不含扩展名），扩展名保持不变。</summary>
        public static bool TryRename(
            string path, string newBaseName,
            out string newPath, out string error)
        {
            newPath = path;
            error = null;

            string name = (newBaseName ?? "").Trim();
            if (name.Length == 0)
            {
                error = "名称不能为空";
                return false;
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "名称不能包含 \\ / : * ? \" < > |";
                return false;
            }
            if (name.EndsWith(".", StringComparison.Ordinal))
            {
                error = "名称不能以点结尾";
                return false;
            }

            string folder = Path.GetDirectoryName(path) ?? "";
            string extension = Path.GetExtension(path);
            string target = Path.Combine(folder, name + extension);
            if (string.Equals(target, path,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (File.Exists(target))
            {
                error = "同目录下已经有同名文件了";
                return false;
            }
            try
            {
                File.Move(path, target);
                newPath = target;
                return true;
            }
            catch (Exception ex)
            {
                error = "重命名失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>给对话框用的紧凑错误文案。</summary>
        public static string ShortError(Exception ex)
        {
            if (ex == null)
            {
                return "未知错误";
            }
            return ex.Message;
        }
    }
}
