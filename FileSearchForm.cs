using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 文件秒搜窗口：本地文件索引 + 即时搜索。
    /// 顶部关键词、右侧搜索范围，下面一排分类标签，再下面是可排序的
    /// 四列表格（名称 / 路径 / 日期 / 大小），点表头切换升降序。
    /// 索引在后台建、落盘复用，窗口本身只负责查询和画。
    /// </summary>
    internal sealed class FileSearchForm : ChromeForm
    {
        private const int HeaderHeightValue = 46;
        private const int SearchHeight = 48;
        private const int TabsHeight = 42;
        private const int FooterHeight = 30;
        private const int TabWidth = 66;
        private const int TabGap = 6;
        private const int ScopeWidth = 132;

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(
            IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static FileSearchForm _instance;

        private readonly FieldBox _search;
        private readonly FlatButton[] _tabs =
            new FlatButton[FileKindLogic.TabKinds.Length];
        private readonly FlatButton _scopeButton;
        private readonly FileResultList _list;
        private readonly GlyphButton _minimizeButton;
        private readonly GlyphButton _closeButton;
        private readonly ContextMenuStrip _itemMenu;
        private readonly ContextMenuStrip _scopeMenu;
        private readonly Timer _searchTimer;
        private readonly Timer _pollTimer;
        private FileKind? _kind;
        private int _generation;
        private string _status = "";
        private int _seenVersion = -1;
        private bool _wasBuilding;
        private bool _preview;
        // 上一次的查询与结果：后面继续打字时直接在它里面筛，快两个数量级。
        private FileQuery _lastQuery;
        private FileSearchResult _lastResult;
        private int _lastVersion = -1;

        public static FileSearchForm ShowWindow()
        {
            FileIndexService.Instance.EnsureLoaded();
            if (_instance != null && !_instance.IsDisposed)
            {
                if (_instance.WindowState == FormWindowState.Minimized)
                {
                    _instance.WindowState = FormWindowState.Normal;
                }
                _instance.Activate();
                _instance.FocusSearch();
                return _instance;
            }
            FileSearchForm form = new FileSearchForm();
            _instance = form;
            form.FormClosed += delegate
            {
                _instance = null;
            };
            form.Show();
            form.Activate();
            form.FocusSearch();
            FileIndexService.Instance.EnsureLoaded();
            return form;
        }

        /// <summary>自检 / 探针用：直接设定关键词并按下去（走真实搜索链路）。</summary>
        internal void SetKeyword(string keyword)
        {
            _search.Text = keyword ?? "";
            _searchTimer.Stop();
            RunSearchNow();
        }

        internal string StatusText
        {
            get { return _status; }
        }

        public static void CloseWindow()
        {
            if (_instance != null && !_instance.IsDisposed)
            {
                _instance.Close();
            }
            _instance = null;
        }

        /// <summary>自检用：拿一份带示例数据的窗口，不碰真实索引。</summary>
        internal static FileSearchForm ShowPreview(
            FileIndexData data, List<FileEntry> items, string keyword)
        {
            FileSearchForm form = new FileSearchForm();
            form._preview = true;
            form._pollTimer.Stop();
            form._searchTimer.Stop();
            form._search.Text = keyword ?? "";
            form._list.SetData(data, items);
            form.SetStatus("预览数据 · " + items.Count + " 条");
            form.Show();
            return form;
        }

        /// <summary>自检要往列表上切排序 / 截图，这里把内部列表露出来。</summary>
        internal FileResultList ResultList
        {
            get { return _list; }
        }

        private FileSearchForm()
        {
            Text = "文件秒搜 - " + AppInfo.Name;
            // 打开窗口期间索引常驻内存；关掉之后过一会儿自动释放。
            FileIndexService.Instance.AddConsumer();
            ClientSize = new Size(980, 660);
            MinimumSize = new Size(760, 480);
            ShowInTaskbar = true;
            KeyPreview = true;

            _search = new FieldBox();
            _search.TextChanged += delegate
            {
                _searchTimer.Stop();
                _searchTimer.Start();
            };
            Controls.Add(_search);

            _scopeButton = new FlatButton();
            _scopeButton.Size = new Size(ScopeWidth, 34);
            _scopeButton.SurfaceColor = DarkUi.Window;
            _scopeButton.Radius = 7;
            _scopeButton.Click += delegate
            {
                ShowScopeMenu();
            };
            Controls.Add(_scopeButton);

            for (int i = 0; i < _tabs.Length; i++)
            {
                FileKind? kind = FileKindLogic.TabKinds[i];
                FlatButton button = new FlatButton();
                button.Text = FileKindLogic.TabLabels[i];
                button.Size = new Size(TabWidth, 30);
                button.SurfaceColor = DarkUi.Window;
                button.Radius = 7;
                button.Click += delegate
                {
                    _kind = kind;
                    _lastQuery = null;
                    _lastResult = null;
                    UpdateTabs();
                    RunSearchNow();
                };
                _tabs[i] = button;
                Controls.Add(button);
            }

            _list = new FileResultList();
            _list.ItemActivated += delegate(FileEntry entry)
            {
                OpenEntry(entry);
            };
            _list.ItemContext += delegate(FileEntry entry, Point at)
            {
                ShowItemMenu(entry, at);
            };
            _list.SortChanged += delegate(FileSortKey key, bool desc)
            {
                ResortInPlace(key, desc);
            };
            Controls.Add(_list);

            _minimizeButton = new GlyphButton(
                GlyphKind.Minimize, DarkUi.Window, 38, 30);
            _minimizeButton.IconScale = 0.85f;
            _minimizeButton.Click += delegate
            {
                WindowState = FormWindowState.Minimized;
            };
            Controls.Add(_minimizeButton);

            _closeButton = new GlyphButton(
                GlyphKind.Close, DarkUi.Window, 38, 30);
            _closeButton.DangerHover = true;
            _closeButton.IconScale = 0.85f;
            _closeButton.Click += delegate { Close(); };
            Controls.Add(_closeButton);

            _itemMenu = new ContextMenuStrip();
            _scopeMenu = new ContextMenuStrip();

            _searchTimer = new Timer();
            _searchTimer.Interval = 130;
            _searchTimer.Tick += delegate
            {
                _searchTimer.Stop();
                RunSearchNow();
            };

            _pollTimer = new Timer();
            _pollTimer.Interval = 500;
            _pollTimer.Tick += delegate { OnPoll(); };
            _pollTimer.Start();

            FormClosed += delegate
            {
                _searchTimer.Stop();
                _searchTimer.Dispose();
                _pollTimer.Stop();
                _pollTimer.Dispose();
                _itemMenu.Dispose();
                _scopeMenu.Dispose();
                FileIndexService.Instance.RemoveConsumer();
            };

            UpdateTabs();
            UpdateScopeLabel();
            DoLayout();
            _seenVersion = FileIndexService.Instance.Version;

            // 输入框里的灰字提示（EM_SETCUEBANNER 只认真正的 Edit 控件）。
            try
            {
                SendMessage(_search.Inner.Handle, EM_SETCUEBANNER, (IntPtr)1,
                    "输入关键词秒搜本地文件，空格分隔多个词，支持 * 和 ?");
            }
            catch
            {
            }
        }

        protected override int HeaderHeight
        {
            get { return HeaderHeightValue; }
        }

        private void FocusSearch()
        {
            try
            {
                _search.Inner.Focus();
                _search.Inner.SelectAll();
            }
            catch
            {
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_list != null)
            {
                DoLayout();
            }
        }

        private void DoLayout()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            int top = HeaderHeightValue;

            int searchWidth = Math.Max(
                200, width - 32 - ScopeWidth - 10);
            _search.SetBounds(16, top + 7, searchWidth, 34);
            _scopeButton.Location = new Point(
                width - 16 - ScopeWidth, top + 7);

            int tabsTop = top + SearchHeight + 8;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].Location = new Point(
                    16 + i * (TabWidth + TabGap), tabsTop);
            }

            int listTop = HeaderHeightValue + SearchHeight + TabsHeight;
            int listHeight = Math.Max(
                120, height - listTop - FooterHeight);
            _list.SetBounds(10, listTop, width - 20, listHeight);

            int right = width - 12;
            _closeButton.Location = new Point(right - 38, 8);
            _minimizeButton.Location = new Point(right - 76, 8);
        }

        private void UpdateTabs()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                FileKind? kind = FileKindLogic.TabKinds[i];
                bool active = kind.Equals(_kind);
                _tabs[i].Checked = active;
                _tabs[i].Primary = active;
            }
        }

        private void UpdateScopeLabel()
        {
            List<string> roots = FileIndexService.ResolveRoots();
            _scopeButton.Text = ScopeText(roots);
        }

        /// <summary>「此电脑 / D: / 3 个位置」这种短标签。</summary>
        public static string ScopeText(List<string> roots)
        {
            if (roots == null || roots.Count == 0)
            {
                return "此电脑";
            }
            if (roots.Count == 1)
            {
                return ShortRoot(roots[0]);
            }
            return roots.Count + " 个位置";
        }

        public static string ShortRoot(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return "此电脑";
            }
            string trimmed = root.TrimEnd('\\', '/');
            if (trimmed.Length == 2 && trimmed[1] == ':')
            {
                return trimmed;
            }
            int slash = trimmed.LastIndexOfAny(
                new char[] { '\\', '/' });
            string name = slash >= 0
                ? trimmed.Substring(slash + 1) : trimmed;
            return name.Length == 0 ? "此电脑" : name;
        }

        private void ShowScopeMenu()
        {
            _scopeMenu.Items.Clear();
            ToolStripMenuItem whole = new ToolStripMenuItem("此电脑（全部本地磁盘）");
            whole.Checked = FileIndexService.ReadScope().Length == 0;
            whole.Click += delegate
            {
                SetScope("");
            };
            _scopeMenu.Items.Add(whole);
            _scopeMenu.Items.Add(new ToolStripSeparator());

            string current = FileIndexService.ReadScope();
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        if (drives[i].DriveType != DriveType.Fixed ||
                            !drives[i].IsReady)
                        {
                            continue;
                        }
                        string root = drives[i].RootDirectory.FullName;
                        ToolStripMenuItem item = new ToolStripMenuItem(
                            ShortRoot(root) + " 盘");
                        item.Checked = string.Equals(
                            current.TrimEnd('\\'), root.TrimEnd('\\'),
                            StringComparison.OrdinalIgnoreCase);
                        string captured = root;
                        item.Click += delegate
                        {
                            SetScope(captured);
                        };
                        _scopeMenu.Items.Add(item);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _scopeMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem pick = new ToolStripMenuItem("选择文件夹…");
            pick.Click += delegate
            {
                ChooseFolder();
            };
            _scopeMenu.Items.Add(pick);
            ToolStripMenuItem rebuild = new ToolStripMenuItem("重建索引");
            rebuild.Click += delegate
            {
                FileIndexService.Instance.Rebuild();
                SetStatus("开始重建索引…");
            };
            _scopeMenu.Items.Add(rebuild);

            _scopeMenu.Show(
                _scopeButton,
                new Point(0, _scopeButton.Height));
        }

        private void ChooseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择要索引的文件夹";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetScope(dialog.SelectedPath);
                }
            }
        }

        private void SetScope(string rootsValue)
        {
            try
            {
                FileIndexService.WriteScope(rootsValue);
            }
            catch (Exception ex)
            {
                Log.Error("保存搜索范围失败", ex);
            }
            UpdateScopeLabel();
            FileIndexService.Instance.Rebuild();
            SetStatus("正在按新范围重建索引…");
            _list.SetData(null, new List<FileEntry>());
        }

        private void OnPoll()
        {
            FileIndexService service = FileIndexService.Instance;
            bool building = service.Building || service.Loading;
            bool changed = service.Version != _seenVersion;
            if (!changed && building == _wasBuilding)
            {
                Invalidate();
                return;
            }
            _wasBuilding = building;
            if (changed)
            {
                _seenVersion = service.Version;
                // 索引换了（首次建完 / 重建完）：按当前条件重跑一次。
                RunSearchNow();
            }
            Invalidate();
        }

        private void RunSearchNow()
        {
            if (_preview)
            {
                return;
            }
            string keyword = (_search.Text ?? "").Trim();
            if (keyword.Length == 0 && !_kind.HasValue)
            {
                _generation++;
                _lastQuery = null;
                _lastResult = null;
                _list.SetData(
                    FileIndexService.Instance.Data,
                    new List<FileEntry>());
                _list.SetEmptyText("输入关键词开始搜索");
                SetStatus("");
                Invalidate();
                return;
            }

            FileQuery query = new FileQuery();
            query.Keyword = keyword;
            query.Kind = _kind;
            query.Sort = _list.SortKey;
            query.Descending = _list.Descending;

            int generation = ++_generation;
            FileIndexData data = FileIndexService.Instance.Data;
            bool canRefine = _lastQuery != null && _lastResult != null &&
                data != null && _lastVersion ==
                    FileIndexService.Instance.Version &&
                _lastResult.Items.Count > 0 && !_lastResult.Truncated &&
                _lastQuery.Kind.Equals(_kind) &&
                _lastQuery.Keyword.Length > 0 &&
                keyword.Length > _lastQuery.Keyword.Length &&
                keyword.StartsWith(_lastQuery.Keyword,
                    StringComparison.OrdinalIgnoreCase);
            FileQuery issued = query;
            Task<FileSearchResult> task = canRefine
                ? Task.Run(delegate
                {
                    return FileQueryEngine.Refine(
                        data, _lastResult.Items, issued);
                })
                : FileIndexService.Instance.SearchAsync(issued);
            task.ContinueWith(delegate(Task<FileSearchResult> finished)
            {
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (generation != _generation || IsDisposed)
                        {
                            return;
                        }
                        ApplyResult(finished, issued);
                    });
                }
                catch
                {
                }
            });
        }

        private void ApplyResult(
            Task<FileSearchResult> finished, FileQuery issued)
        {
            FileSearchResult result = null;
            try
            {
                result = finished.Result;
            }
            catch (Exception ex)
            {
                Log.Error("文件搜索失败", ex);
            }
            if (result == null || result.Error.Length > 0)
            {
                _list.SetData(
                    FileIndexService.Instance.Data,
                    new List<FileEntry>());
                _list.SetEmptyText(result == null
                    ? "搜索失败" : result.Error);
                SetStatus(result == null ? "" : result.Error);
                Invalidate();
                return;
            }
            _lastQuery = issued;
            _lastResult = result;
            _lastVersion = FileIndexService.Instance.Version;
            _list.SetData(
                FileIndexService.Instance.Data, result.Items);
            _list.SetEmptyText("没有匹配的文件，试试更短的关键词");
            SetStatus("共匹配 " + FormatCount(result.Total) + " 个" +
                (result.Truncated
                    ? "（显示前 " + FormatCount(result.Items.Count) + "）"
                    : "") +
                " · 用时 " + result.ElapsedMs + " ms");
            Invalidate();
        }

        private void ResortInPlace(FileSortKey key, bool descending)
        {
            List<FileEntry> items = _list.Items;
            if (items == null || items.Count < 2)
            {
                return;
            }
            FileQueryEngine.Sort(
                items, FileIndexService.Instance.Data, key, descending);
            _list.SetItems(items, true);
            Invalidate();
        }

        private void SetStatus(string text)
        {
            _status = text ?? "";
            Invalidate();
        }

        public static string FormatCount(int value)
        {
            try
            {
                return value.ToString("N0", System.Globalization
                    .CultureInfo.InvariantCulture);
            }
            catch
            {
                return value.ToString();
            }
        }

        private void OpenEntry(FileEntry entry)
        {
            string path = FullPathOf(entry);
            if (path.Length == 0)
            {
                return;
            }
            try
            {
                if (entry.Kind == FileKind.Folder)
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
                else
                {
                    Process.Start(new ProcessStartInfo(path)
                    {
                        UseShellExecute = true
                    });
                }
                SetStatus("已打开：" + path);
            }
            catch (Exception ex)
            {
                SetStatus("打不开：" + ex.Message);
            }
        }

        private void RevealEntry(FileEntry entry)
        {
            string path = FullPathOf(entry);
            if (path.Length == 0)
            {
                return;
            }
            try
            {
                if (entry.Kind == FileKind.Folder)
                {
                    Process.Start("explorer.exe",
                        "/select,\"" + path + "\"");
                }
                else
                {
                    Process.Start("explorer.exe",
                        "/select,\"" + path + "\"");
                }
            }
            catch (Exception ex)
            {
                SetStatus("定位失败：" + ex.Message);
            }
        }

        private string FullPathOf(FileEntry entry)
        {
            if (entry == null)
            {
                return "";
            }
            FileIndexData data = _list.Data;
            if (data == null)
            {
                return entry.Name;
            }
            return data.FullPath(entry);
        }

        private void ShowItemMenu(FileEntry entry, Point at)
        {
            if (entry == null)
            {
                return;
            }
            _itemMenu.Items.Clear();
            string path = FullPathOf(entry);

            ToolStripMenuItem open = new ToolStripMenuItem(
                entry.Kind == FileKind.Folder ? "打开文件夹" : "打开");
            open.Click += delegate { OpenEntry(entry); };
            _itemMenu.Items.Add(open);

            ToolStripMenuItem reveal = new ToolStripMenuItem(
                "在资源管理器中显示");
            reveal.Click += delegate { RevealEntry(entry); };
            _itemMenu.Items.Add(reveal);

            _itemMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem copyPath = new ToolStripMenuItem("复制完整路径");
            copyPath.Click += delegate
            {
                CopyText(path);
            };
            _itemMenu.Items.Add(copyPath);

            ToolStripMenuItem copyName = new ToolStripMenuItem("复制文件名");
            copyName.Click += delegate
            {
                CopyText(entry.Name);
            };
            _itemMenu.Items.Add(copyName);

            if (entry.Kind != FileKind.Folder)
            {
                ToolStripMenuItem copyFile = new ToolStripMenuItem("复制这个文件");
                copyFile.Click += delegate
                {
                    CopyFile(path);
                };
                _itemMenu.Items.Add(copyFile);
            }

            _itemMenu.Show(_list, at);
        }

        private void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                Clipboard.SetText(text);
                SetStatus("已复制：" + text);
            }
            catch (Exception ex)
            {
                SetStatus("复制失败：" + ex.Message);
            }
        }

        private void CopyFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    SetStatus("文件已经不在了");
                    return;
                }
                StringCollection files = new StringCollection();
                files.Add(path);
                Clipboard.SetFileDropList(files);
                SetStatus("已复制文件，可直接粘贴");
            }
            catch (Exception ex)
            {
                SetStatus("复制失败：" + ex.Message);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                FileIndexService.Instance.Rebuild();
                SetStatus("开始重建索引…");
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                // 有内容先清空，空的时候才关窗，避免误关。
                if ((_search.Text ?? "").Length > 0)
                {
                    _search.Text = "";
                    RunSearchNow();
                }
                else
                {
                    Close();
                }
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.C && e.Control)
            {
                FileEntry selected = _list.SelectedItem;
                if (selected != null)
                {
                    CopyText(FullPathOf(selected));
                    e.Handled = true;
                    return;
                }
            }
            if (e.KeyCode == Keys.F && e.Control)
            {
                FocusSearch();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left &&
                e.Y < HeaderHeightValue)
            {
                BeginWindowDrag();
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(DarkUi.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
            {
                g.DrawLine(pen, 0, HeaderHeightValue - 1,
                    ClientSize.Width, HeaderHeightValue - 1);
                g.DrawLine(pen, 0, ClientSize.Height - FooterHeight,
                    ClientSize.Width, ClientSize.Height - FooterHeight);
            }

            DarkUi.Text2(g, "文件秒搜", DarkUi.FontStrong, DarkUi.Text,
                new Rectangle(18, 10, 120, 26),
                DarkUi.LeftMiddle);

            FileIndexService service = FileIndexService.Instance;
            string subtitle;
            Color subtitleColor = DarkUi.TextFaint;
            if (_preview)
            {
                subtitle = "示例数据 · 索引未启用";
            }
            else if (service.Loading)
            {
                subtitle = "正在载入索引…";
                subtitleColor = DarkUi.Accent;
            }
            else if (service.Building)
            {
                subtitle = "正在建立索引 · 已扫描 " +
                    FormatCount(service.ScannedFiles) + " 个文件";
                subtitleColor = DarkUi.Accent;
            }
            else if (service.Ready)
            {
                subtitle = "已索引 " + FormatCount(service.Count) +
                    " 个文件 · 更新于 " +
                    service.IndexedAt.ToLocalTime().ToString("MM-dd HH:mm");
            }
            else
            {
                subtitle = "索引还没建好";
            }
            DarkUi.Text2(g, subtitle, DarkUi.FontSmall, subtitleColor,
                new Rectangle(100, 10, ClientSize.Width - 260, 26),
                DarkUi.LeftMiddleClip);

            string footer = _status.Length > 0
                ? _status
                : "双击打开 · 右键更多 · 点表头排序 · F5 重建索引";
            DarkUi.Text2(g, footer, DarkUi.FontSmall,
                _status.Length > 0 ? DarkUi.Accent : DarkUi.TextFaint,
                new Rectangle(16, ClientSize.Height - FooterHeight + 2,
                    ClientSize.Width - 32, FooterHeight - 4),
                DarkUi.LeftMiddleClip);
        }
    }

    /// <summary>
    /// 结果表格：自绘四列 + 可点表头排序 + 滚轮 + 键盘导航。
    /// 行高固定 26，只画可见行，几万条也不卡。
    /// </summary>
    internal sealed class FileResultList : Control
    {
        private const int RowHeight = 26;
        private const int HeadHeight = 30;
        private const int PadLeft = 14;
        private const int DateWidth = 126;
        private const int SizeWidth = 86;
        private const int ColGap = 12;

        private FileIndexData _data;
        private List<FileEntry> _items = new List<FileEntry>();
        private int _scroll;
        private int _contentHeight;
        private int _selected = -1;
        private int _hover = -1;
        private string _emptyText = "输入关键词开始搜索";
        private FileSortKey _sort = FileSortKey.Date;
        private bool _descending = true;

        public event Action<FileEntry> ItemActivated;
        public event Action<FileEntry, Point> ItemContext;
        public event Action<FileSortKey, bool> SortChanged;

        public FileResultList()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            BackColor = DarkUi.Window;
            Cursor = Cursors.Default;
            TabStop = true;
        }

        public FileIndexData Data
        {
            get { return _data; }
        }

        public List<FileEntry> Items
        {
            get { return _items; }
        }

        public FileSortKey SortKey
        {
            get { return _sort; }
        }

        public bool Descending
        {
            get { return _descending; }
        }

        public FileEntry SelectedItem
        {
            get
            {
                if (_selected >= 0 && _selected < _items.Count)
                {
                    return _items[_selected];
                }
                return null;
            }
        }

        public void SetEmptyText(string text)
        {
            _emptyText = text ?? "";
            Invalidate();
        }

        /// <summary>外部直接改排序状态（探针 / 恢复上次排序用）。</summary>
        internal void SetSort(FileSortKey key, bool descending)
        {
            _sort = key;
            _descending = descending;
            Invalidate();
        }

        public void SetData(FileIndexData data, List<FileEntry> items)
        {
            _data = data;
            SetItems(items, false);
        }

        /// <summary>
        /// 换一批数据。keepSelection 为真时按路径尽量保留选中行
        /// （排序后原地刷新用，不然选中会跳到别的行上）。
        /// </summary>
        public void SetItems(List<FileEntry> items, bool keepSelection)
        {
            FileEntry previous = keepSelection ? SelectedItem : null;
            string previousPath = previous == null || _data == null
                ? "" : _data.FullPath(previous);
            _items = items ?? new List<FileEntry>();
            _contentHeight = HeadHeight + _items.Count * RowHeight + 8;
            _selected = -1;
            if (previousPath.Length > 0)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (string.Equals(_data.FullPath(_items[i]),
                        previousPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _selected = i;
                        break;
                    }
                }
            }
            if (_selected >= _items.Count)
            {
                _selected = -1;
            }
            ClampScroll();
            Invalidate();
        }

        /// <summary>离屏整表渲染，自检用来锁「某一列会不会画崩」。</summary>
        internal int RenderAllRows(Graphics g)
        {
            if (g == null)
            {
                return 0;
            }
            DrawHead(g);
            for (int i = 0; i < _items.Count; i++)
            {
                DrawRow(g, i, HeadHeight + i * RowHeight - _scroll);
            }
            return _items.Count;
        }

        internal int ContentHeight
        {
            get { return Math.Max(1, _contentHeight); }
        }

        private void ClampScroll()
        {
            int max = Math.Max(0, _contentHeight - Height);
            if (_scroll > max)
            {
                _scroll = max;
            }
            if (_scroll < 0)
            {
                _scroll = 0;
            }
        }

        private Rectangle BodyBounds
        {
            get
            {
                return new Rectangle(0, HeadHeight, Width,
                    Math.Max(0, Height - HeadHeight));
            }
        }

        private void Columns(
            out Rectangle name, out Rectangle path,
            out Rectangle date, out Rectangle size)
        {
            int usable = Math.Max(240, Width - PadLeft - 14);
            int nameWidth = Math.Max(
                140, (int)(usable * 0.32));
            int sizeW = SizeWidth;
            int dateW = DateWidth;
            int pathWidth = Math.Max(80,
                usable - nameWidth - sizeW - dateW - ColGap * 3);
            int x = PadLeft;
            name = new Rectangle(x, 0, nameWidth, RowHeight);
            x += nameWidth + ColGap;
            path = new Rectangle(x, 0, pathWidth, RowHeight);
            x += pathWidth + ColGap;
            date = new Rectangle(x, 0, dateW, RowHeight);
            x += dateW + ColGap;
            size = new Rectangle(x, 0, sizeW, RowHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(DarkUi.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawHead(g);

            if (_items.Count == 0)
            {
                DarkUi.Text2(g, _emptyText, DarkUi.FontBody,
                    DarkUi.TextDim,
                    new Rectangle(0, HeadHeight,
                        Width, Math.Max(40, Height - HeadHeight)),
                    DarkUi.CenterMiddle);
                return;
            }

            Rectangle body = BodyBounds;
            for (int i = 0; i < _items.Count; i++)
            {
                int top = HeadHeight + i * RowHeight - _scroll;
                if (top + RowHeight < HeadHeight || top > Height)
                {
                    continue;
                }
                try
                {
                    DrawRow(g, i, top);
                }
                catch (Exception ex)
                {
                    if (!_drawErrorLogged)
                    {
                        _drawErrorLogged = true;
                        Log.Error("文件列表某一行绘制失败，已跳过该行", ex);
                    }
                }
            }
            DrawScrollBar(g);
        }

        private bool _drawErrorLogged;

        private void DrawHead(Graphics g)
        {
            using (SolidBrush brush = new SolidBrush(DarkUi.Surface))
            {
                g.FillRectangle(brush, 0, 0, Width, HeadHeight);
            }
            using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
            {
                g.DrawLine(pen, 0, HeadHeight - 1, Width, HeadHeight - 1);
            }

            Rectangle name, path, date, size;
            Columns(out name, out path, out date, out size);
            DrawHeadCell(g, name, "名称", FileSortKey.Name,
                DarkUi.LeftMiddleClip);
            DrawHeadCell(g, path, "路径", FileSortKey.Path,
                DarkUi.LeftMiddleClip);
            DrawHeadCell(g, date, "日期", FileSortKey.Date,
                DarkUi.LeftMiddle);
            DrawHeadCell(g, size, "大小", FileSortKey.Size,
                DarkUi.RightMiddle);
        }

        private void DrawHeadCell(
            Graphics g, Rectangle column, string text,
            FileSortKey key, TextFormatFlags flags)
        {
            bool active = key == _sort;
            Rectangle rect = new Rectangle(
                column.Left, 0, column.Width, HeadHeight);
            DarkUi.Text2(g, text, DarkUi.FontSmall,
                active ? DarkUi.Text : DarkUi.TextDim,
                rect, flags);
            if (!active)
            {
                return;
            }
            // 排序箭头：画在文字右侧（数值列画在左侧不影响右对齐）。
            string arrow = _descending ? "▼" : "▲";
            Size textSize = TextRenderer.MeasureText(text,
                DarkUi.FontSmall);
            int arrowX;
            if ((flags & TextFormatFlags.Right) != 0)
            {
                arrowX = column.Left + 1;
            }
            else
            {
                arrowX = column.Left + textSize.Width + 2;
            }
            DarkUi.Text2(g, arrow, DarkUi.FontTiny, DarkUi.Accent,
                new Rectangle(arrowX, 0, 16, HeadHeight),
                DarkUi.LeftMiddle);
        }

        private void DrawRow(Graphics g, int index, int top)
        {
            FileEntry entry = _items[index];
            bool selected = index == _selected;
            bool hover = index == _hover;
            Rectangle row = new Rectangle(0, top, Width, RowHeight);
            if (selected)
            {
                using (SolidBrush brush = new SolidBrush(DarkUi.AccentSoft))
                {
                    g.FillRectangle(brush, row);
                }
            }
            else if (hover)
            {
                using (SolidBrush brush = new SolidBrush(DarkUi.Card))
                {
                    g.FillRectangle(brush, row);
                }
            }
            else if ((index & 1) == 1)
            {
                using (SolidBrush brush = new SolidBrush(
                    Color.FromArgb(26, 28, 32)))
                {
                    g.FillRectangle(brush, row);
                }
            }

            Rectangle name, path, date, size;
            Columns(out name, out path, out date, out size);

            // 分类色点，窄，不抢文字位置。
            Rectangle dot = new Rectangle(
                name.Left + 2, top + RowHeight / 2 - 3, 6, 6);
            using (SolidBrush brush = new SolidBrush(KindColor(entry.Kind)))
            {
                g.FillEllipse(brush, dot);
            }

            Rectangle nameRect = new Rectangle(
                name.Left + 14, top, name.Width - 14, RowHeight);
            DarkUi.Text2(g, entry.Name,
                entry.Kind == FileKind.Folder
                    ? DarkUi.FontStrong : DarkUi.FontSmall,
                selected ? DarkUi.Text : DarkUi.Text,
                nameRect, DarkUi.LeftMiddleClip);

            string full = _data == null
                ? "" : _data.FullPath(entry);
            DarkUi.Text2(g, ParentPath(full, entry.Name),
                DarkUi.FontSmall, DarkUi.TextFaint,
                new Rectangle(path.Left, top, path.Width, RowHeight),
                DarkUi.LeftMiddleClip);

            DarkUi.Text2(g, FileKindLogic.DateText(entry.Ticks),
                DarkUi.FontSmall, DarkUi.TextDim,
                new Rectangle(date.Left, top, date.Width, RowHeight),
                DarkUi.LeftMiddle);

            DarkUi.Text2(g,
                FileKindLogic.SizeText(entry.Size, entry.Kind),
                DarkUi.FontSmall, DarkUi.TextDim,
                new Rectangle(size.Left, top, size.Width, RowHeight),
                DarkUi.RightMiddle);
        }

        /// <summary>路径列只显示所在目录，文件本身名字已经在第一列了。</summary>
        public static string ParentPath(string fullPath, string name)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return "";
            }
            if (string.IsNullOrEmpty(name) ||
                fullPath.Length <= name.Length)
            {
                return fullPath;
            }
            int cut = fullPath.Length - name.Length;
            string parent = fullPath.Substring(0, cut);
            if (parent.Length > 1)
            {
                parent = parent.TrimEnd('\\', '/');
                if (parent.Length == 2 && parent[1] == ':')
                {
                    parent += "\\";
                }
            }
            return parent;
        }

        public static Color KindColor(FileKind kind)
        {
            switch (kind)
            {
                case FileKind.Folder:
                    return Color.FromArgb(232, 186, 104);
                case FileKind.Document:
                    return Color.FromArgb(118, 168, 250);
                case FileKind.Image:
                    return Color.FromArgb(104, 200, 188);
                case FileKind.Archive:
                    return Color.FromArgb(170, 148, 240);
                case FileKind.Application:
                    return Color.FromArgb(126, 208, 142);
                case FileKind.Video:
                    return Color.FromArgb(238, 140, 168);
                case FileKind.Audio:
                    return Color.FromArgb(238, 168, 118);
                default:
                    return Color.FromArgb(140, 146, 156);
            }
        }

        private void DrawScrollBar(Graphics g)
        {
            if (_contentHeight <= Height)
            {
                return;
            }
            int trackHeight = Height - HeadHeight - 8;
            int thumbHeight = Math.Max(36,
                (int)((double)(Height - HeadHeight) /
                    _contentHeight * trackHeight));
            int maxScroll = Math.Max(1, _contentHeight - Height);
            int thumbTop = HeadHeight + 4 + (int)(
                (double)_scroll / maxScroll * (trackHeight - thumbHeight));
            Rectangle thumb = new Rectangle(
                Width - 7, thumbTop, 3, thumbHeight);
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(80, 86, 96)))
            {
                g.FillRectangle(brush, thumb);
            }
        }

        private int RowAt(int y)
        {
            if (y < HeadHeight)
            {
                return -1;
            }
            int index = (y - HeadHeight + _scroll) / RowHeight;
            if (index < 0 || index >= _items.Count)
            {
                return -1;
            }
            return index;
        }

        private void EnsureVisible(int index)
        {
            int top = HeadHeight + index * RowHeight - _scroll;
            if (top < HeadHeight)
            {
                _scroll = index * RowHeight;
            }
            else if (top + RowHeight > Height)
            {
                _scroll = index * RowHeight + RowHeight -
                    (Height - HeadHeight);
            }
            ClampScroll();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left && e.Y < HeadHeight)
            {
                Rectangle name, path, date, size;
                Columns(out name, out path, out date, out size);
                FileSortKey key;
                if (e.X >= size.Left)
                {
                    key = FileSortKey.Size;
                }
                else if (e.X >= date.Left)
                {
                    key = FileSortKey.Date;
                }
                else if (e.X >= path.Left)
                {
                    key = FileSortKey.Path;
                }
                else
                {
                    key = FileSortKey.Name;
                }
                if (key == _sort)
                {
                    _descending = !_descending;
                }
                else
                {
                    _sort = key;
                    _descending = key == FileSortKey.Date ||
                        key == FileSortKey.Size;
                }
                Invalidate();
                if (SortChanged != null)
                {
                    SortChanged(_sort, _descending);
                }
                return;
            }
            int index = RowAt(e.Y);
            if (index >= 0)
            {
                _selected = index;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = RowAt(e.Y);
            if (index != _hover)
            {
                _hover = index;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int index = RowAt(e.Y);
                if (index >= 0 && ItemActivated != null)
                {
                    ItemActivated(_items[index]);
                    return;
                }
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = RowAt(e.Y);
                if (index >= 0)
                {
                    _selected = index;
                    Invalidate();
                    if (ItemContext != null)
                    {
                        ItemContext(_items[index], e.Location);
                    }
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll -= Math.Sign(e.Delta) * RowHeight * 3;
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_items.Count == 0)
            {
                base.OnKeyDown(e);
                return;
            }
            int page = Math.Max(
                1, (Height - HeadHeight) / RowHeight - 1);
            if (e.KeyCode == Keys.Down)
            {
                _selected = Math.Min(_items.Count - 1, _selected + 1);
                if (_selected < 0)
                {
                    _selected = 0;
                }
                EnsureVisible(_selected);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Up)
            {
                _selected = Math.Max(0, _selected - 1);
                EnsureVisible(_selected);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.PageDown)
            {
                _selected = Math.Min(
                    _items.Count - 1, Math.Max(0, _selected) + page);
                EnsureVisible(_selected);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.PageUp)
            {
                _selected = Math.Max(
                    0, Math.Max(0, _selected) - page);
                EnsureVisible(_selected);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Home)
            {
                _selected = 0;
                EnsureVisible(0);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.End)
            {
                _selected = _items.Count - 1;
                EnsureVisible(_selected);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && ItemActivated != null)
            {
                FileEntry entry = SelectedItem;
                if (entry != null)
                {
                    ItemActivated(entry);
                    e.Handled = true;
                    return;
                }
            }
            base.OnKeyDown(e);
        }
    }
}
