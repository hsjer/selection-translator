using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>写回剪贴板时的格式策略。</summary>
    internal enum ClipboardWriteMode
    {
        /// <summary>图片同时给「位图 + 文件列表」；文本 / 文件保持原样。</summary>
        Auto,
        /// <summary>图片只给位图，避免某些程序偏好文件而把图片当附件发出去。</summary>
        ImageOnly,
        /// <summary>统一给「可落盘文件」，资源管理器 / 桌面可以直接粘贴成文件。</summary>
        FileOnly
    }

    /// <summary>
    /// 让图片（以及文本）能直接粘贴到桌面 / 资源管理器。
    /// 桌面不是画图：它只认 CF_HDROP（文件拖放列表）或 FileDescriptor，
    /// 普通剪贴板工具写的 CF_DIB / CF_BITMAP 在桌面上「粘贴」是灰的。
    /// 所以这里额外落一份真实文件，并以多格式 DataObject 一起放上剪贴板：
    /// 画图 / 聊天软件取位图，资源管理器取文件列表，互不干扰。
    /// </summary>
    internal static class ClipboardPasteFiles
    {
        /// <summary>粘贴用的临时文件保留时长，避免剪贴板还引用着就被删掉。</summary>
        public static readonly TimeSpan KeepFor = TimeSpan.FromHours(2);

        public static string PasteDirectory
        {
            get
            {
                return Path.Combine(
                    ConfigStore.DataDirectory, "clipboard-paste");
            }
        }

        /// <summary>纯函数，供自检验证生成的文件名。</summary>
        public static string FileNameFor(int kind, DateTime time)
        {
            string stamp = time.ToString("yyyyMMdd-HHmmss-fff");
            return kind == (int)ClipboardItemKind.Image
                ? "图片-" + stamp + ".png"
                : "文本-" + stamp + ".txt";
        }

        public static bool IncludeImage(ClipboardWriteMode mode)
        {
            return mode != ClipboardWriteMode.FileOnly;
        }

        public static bool IncludeFile(ClipboardWriteMode mode)
        {
            return mode != ClipboardWriteMode.ImageOnly;
        }

        /// <summary>把历史里的 PNG 复制成一份名字友好的可粘贴文件。</summary>
        public static string PrepareImage(string sourcePath, DateTime now)
        {
            if (!File.Exists(sourcePath))
            {
                return "";
            }
            string directory = PasteDirectory;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory,
                FileNameFor((int)ClipboardItemKind.Image, now));
            File.Copy(sourcePath, path, true);
            Cleanup(now);
            return path;
        }

        /// <summary>文本条目也可以「复制为文件」，在桌面粘贴成 .txt。</summary>
        public static string PrepareText(string text, DateTime now)
        {
            string directory = PasteDirectory;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory,
                FileNameFor((int)ClipboardItemKind.Text, now));
            // 带 BOM，老版本记事本也不会把中文认成乱码。
            File.WriteAllText(path, text ?? "", new UTF8Encoding(true));
            Cleanup(now);
            return path;
        }

        /// <summary>纯函数，供自检验证清理边界。</summary>
        public static bool IsExpired(DateTime lastWrite, DateTime now)
        {
            return now - lastWrite > KeepFor;
        }

        /// <summary>只清过期的临时文件，返回删除数量；失败不影响主流程。</summary>
        public static int Cleanup(DateTime now)
        {
            int removed = 0;
            try
            {
                string directory = PasteDirectory;
                if (!Directory.Exists(directory))
                {
                    return 0;
                }
                string[] files = Directory.GetFiles(
                    directory, "*.png");
                string[] texts = Directory.GetFiles(
                    directory, "*.txt");
                string[] all = new string[files.Length + texts.Length];
                Array.Copy(files, 0, all, 0, files.Length);
                Array.Copy(texts, 0, all, files.Length, texts.Length);
                foreach (string file in all)
                {
                    if (!IsExpired(File.GetLastWriteTime(file), now))
                    {
                        continue;
                    }
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch
                    {
                        // 文件可能正被资源管理器占用，下次再清。
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("清理剪贴板粘贴临时文件失败", ex);
            }
            return removed;
        }

        /// <summary>
        /// 写回系统剪贴板。Auto 模式让图片同时具备「图片」和「文件」两种格式，
        /// 因此既能在聊天框里粘成图，也能在桌面粘贴成 PNG 文件。
        /// </summary>
        public static bool Write(
            ClipboardStore store,
            ClipboardItem item,
            ClipboardWriteMode mode)
        {
            if (store == null || item == null)
            {
                return false;
            }
            try
            {
                Clipboard.Clear();
                if (item.Kind == (int)ClipboardItemKind.Image)
                {
                    string source = store.ImageFullPath(item.ImageFile);
                    if (!File.Exists(source))
                    {
                        return false;
                    }
                    // Bitmap 必须活到 SetDataObject 完成：OleFlushClipboard
                    // 会在这期间把 CF_DIB 真正渲染出来，提前 Dispose 会得到空图。
                    using (Image image = Image.FromFile(source))
                    using (Bitmap copy = new Bitmap(image))
                    {
                        DataObject data = new DataObject();
                        if (IncludeImage(mode))
                        {
                            data.SetImage(copy);
                        }
                        if (IncludeFile(mode))
                        {
                            string path = PrepareImage(
                                source, DateTime.Now);
                            if (path.Length == 0)
                            {
                                return false;
                            }
                            StringCollection collection =
                                new StringCollection();
                            collection.Add(path);
                            data.SetFileDropList(collection);
                        }
                        if (data.GetFormats().Length == 0)
                        {
                            return false;
                        }
                        Clipboard.SetDataObject(data, true);
                    }
                    return true;
                }

                if (item.Kind == (int)ClipboardItemKind.File)
                {
                    StringCollection collection = new StringCollection();
                    if (item.Files != null)
                    {
                        for (int i = 0; i < item.Files.Length; i++)
                        {
                            if (File.Exists(item.Files[i]) ||
                                Directory.Exists(item.Files[i]))
                            {
                                collection.Add(item.Files[i]);
                            }
                        }
                    }
                    if (collection.Count == 0)
                    {
                        return false;
                    }
                    Clipboard.SetFileDropList(collection);
                    return true;
                }

                if (mode == ClipboardWriteMode.FileOnly)
                {
                    string path = PrepareText(item.Text, DateTime.Now);
                    StringCollection collection = new StringCollection();
                    collection.Add(path);
                    Clipboard.SetFileDropList(collection);
                }
                else
                {
                    Clipboard.SetText(
                        item.Text ?? "", TextDataFormat.UnicodeText);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("写回剪贴板失败", ex);
                return false;
            }
        }
    }
}
