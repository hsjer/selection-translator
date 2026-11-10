using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 软件下载窗口：搜索 360 软件宝库，列表里点一下就直接下安装包。
    /// 走的是详情页「普通下载」的那个直链，不经过 360 软件管家，
    /// 因此不会像「安全下载」那样顺手把 360 卫士装下来。
    /// </summary>
    internal sealed class SoftwareDownloadForm : ChromeForm
    {
        private const int HeaderHeightValue = 46;
        private const int SearchHeight = 48;
        private const int FooterHeight = 30;
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(
            IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static SoftwareDownloadForm _instance;

        private readonly FieldBox _search;
        private readonly FlatButton _searchButton;
        private readonly FlatButton _moreButton;
        private readonly FlatButton _folderButton;
        private readonly GlyphButton _minimizeButton;
        private readonly GlyphButton _closeButton;
        private readonly SoftListView _list;
        private readonly ContextMenuStrip _itemMenu;
        private readonly Timer _statusTimer;
        private readonly List<SoftItem> _items = new List<SoftItem>();
        private readonly Dictionary<int, SoftDownload> _jobs =
            new Dictionary<int, SoftDownload>();
        private readonly Dictionary<int, Image> _logos =
            new Dictionary<int, Image>();
        private readonly HashSet<int> _logoLoading = new HashSet<int>();
        private string _keyword = "";
        private string _status = "";
        private bool _statusError;
        private bool _searching;
        private int _page;
        private int _total;
        private bool _preview;

        public static void ShowWindow()
        {
            if (_instance != null && !_instance.IsDisposed)
            {
                _instance.Activate();
                _instance.FocusSearch();
                return;
            }
            SoftwareDownloadForm form = new SoftwareDownloadForm();
            _instance = form;
            form.FormClosed += delegate { _instance = null; };
            form.Show();
            form.Activate();
            form.FocusSearch();
        }

        public static void CloseWindow()
        {
            if (_instance != null && !_instance.IsDisposed)
            {
                _instance.Close();
            }
            _instance = null;
        }

        public static SoftwareDownloadForm ShowPreview(List<SoftItem> items)
        {
            CloseWindow();
            SoftwareDownloadForm form = new SoftwareDownloadForm();
            form._preview = true;
            _instance = form;
            form.Show();
            form.Activate();
            form.LoadPreview(items);
            return form;
        }

        private SoftwareDownloadForm()
        {
            Text = "软件下载 - " + AppInfo.Name;
            ClientSize = new Size(860, 660);
            MinimumSize = new Size(700, 520);
            ShowInTaskbar = true;

            _search = new FieldBox();
            _search.TextChanged += delegate { UpdateButtons(); };
            _search.Inner.KeyDown += OnSearchKeyDown;
            Controls.Add(_search);

            _searchButton = MakeButton("搜索", true, delegate
            {
                BeginSearch(false);
            });

            _list = new SoftListView();
            _list.SetProviders(
                delegate(SoftItem item) { return JobFor(item); },
                delegate(SoftItem item) { return LogoFor(item); });
            _list.ItemActivated += delegate(SoftItem item)
            {
                StartDownload(item);
            };
            _list.ItemContext += delegate(SoftItem item, Point at)
            {
                ShowItemMenu(item, at);
            };
            Controls.Add(_list);

            _moreButton = MakeButton("加载更多", false, delegate
            {
                BeginSearch(true);
            });
            _moreButton.Visible = false;

            _folderButton = MakeButton("打开下载文件夹", false, delegate
            {
                OpenDownloadFolder();
            });

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

            _itemMenu = BuildItemMenu();

            _statusTimer = new Timer();
            _statusTimer.Interval = 6000;
            _statusTimer.Tick += delegate
            {
                _statusTimer.Stop();
                if (!_statusError)
                {
                    _status = "";
                    Invalidate();
                }
            };

            FormClosed += delegate
            {
                _statusTimer.Stop();
                _statusTimer.Dispose();
                _list.DisposeLogos();
                foreach (KeyValuePair<int, Image> pair in _logos)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.Dispose();
                    }
                }
                _logos.Clear();
                foreach (KeyValuePair<int, SoftDownload> pair in _jobs)
                {
                    if (!pair.Value.Done)
                    {
                        pair.Value.Cancel();
                    }
                }
            };

            DoLayout();
            SetStatus(
                "输入软件名后回车（例如：微信、WPS、网易云音乐）", false);
        }

        private FlatButton MakeButton(
            string text, bool primary, EventHandler onClick)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Primary = primary;
            button.SurfaceColor = DarkUi.Window;
            button.Size = new Size(92, 34);
            button.Click += onClick;
            Controls.Add(button);
            return button;
        }

        private ContextMenuStrip BuildItemMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            ToolStripMenuItem download = new ToolStripMenuItem("下载安装包");
            download.Click += delegate
            {
                StartDownload(_list.SelectedItem);
            };
            menu.Items.Add(download);

            ToolStripMenuItem cancel = new ToolStripMenuItem("取消下载");
            cancel.Click += delegate
            {
                SoftItem item = _list.SelectedItem;
                SoftDownload job = JobFor(item);
                if (job != null && !job.Done)
                {
                    job.Cancel();
                    SetStatus("已取消：" + item.Name, false);
                }
            };
            menu.Items.Add(cancel);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem openFolder =
                new ToolStripMenuItem("打开所在文件夹");
            openFolder.Click += delegate
            {
                SoftItem item = _list.SelectedItem;
                SoftDownload job = JobFor(item);
                if (job != null && job.Done && job.TargetPath.Length > 0)
                {
                    RevealFile(job.TargetPath);
                }
                else
                {
                    OpenDownloadFolder();
                }
            };
            menu.Items.Add(openFolder);

            ToolStripMenuItem copyUrl =
                new ToolStripMenuItem("复制下载地址");
            copyUrl.Click += delegate
            {
                SoftItem item = _list.SelectedItem;
                if (item == null || item.DownloadUrl.Length == 0)
                {
                    return;
                }
                try
                {
                    Clipboard.SetText(item.DownloadUrl);
                    SetStatus("下载地址已复制", false);
                }
                catch
                {
                    SetStatus("复制失败：剪贴板被占用", true);
                }
            };
            menu.Items.Add(copyUrl);

            ToolStripMenuItem detail =
                new ToolStripMenuItem("在浏览器打开详情页");
            detail.Click += delegate
            {
                SoftItem item = _list.SelectedItem;
                if (item == null)
                {
                    return;
                }
                string url = item.DetailPage;
                if (url.Length == 0)
                {
                    url = SoftwareCatalog.SiteRoot + "/search?kw=" +
                        Uri.EscapeDataString(item.Name);
                }
                try
                {
                    Process.Start(url);
                }
                catch (Exception ex)
                {
                    SetStatus("打开浏览器失败：" + ex.Message, true);
                }
            };
            menu.Items.Add(detail);

            menu.Opening += delegate
            {
                SoftItem item = _list.SelectedItem;
                SoftDownload job = JobFor(item);
                bool running = job != null && !job.Done && !job.Failed &&
                    !job.Canceled;
                download.Enabled = item != null && !running;
                cancel.Enabled = running;
                copyUrl.Enabled = item != null &&
                    item.DownloadUrl.Length > 0;
                detail.Enabled = item != null;
            };
            return menu;
        }

        private void ShowItemMenu(SoftItem item, Point at)
        {
            if (item == null)
            {
                return;
            }
            _list.SelectItem(item);
            _itemMenu.Show(_list, at);
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                BeginSearch(false);
            }
        }

        private void FocusSearch()
        {
            _search.Inner.Focus();
            _search.Inner.SelectAll();
        }

        private void BeginSearch(bool append)
        {
            string keyword = _search.Text.Trim();
            if (keyword.Length == 0)
            {
                SetStatus("请输入软件名称，例如「微信」", true);
                FocusSearch();
                return;
            }
            if (_searching || _preview)
            {
                return;
            }
            int page = append ? _page + 1 : 1;
            _searching = true;
            _keyword = keyword;
            SetStatus(
                append
                    ? "正在加载第 " + page + " 页…"
                    : "正在搜索「" + keyword + "」…", false);
            _searchButton.Enabled = false;
            UpdateButtons();

            Task<SoftSearchResult> task =
                SoftwareCatalog.SearchAsync(keyword, page);
            task.ContinueWith(delegate(Task<SoftSearchResult> finished)
            {
                if (IsDisposed)
                {
                    return;
                }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        FinishSearch(finished, page, append);
                    });
                }
                catch
                {
                }
            });
        }

        private void FinishSearch(
            Task<SoftSearchResult> task, int page, bool append)
        {
            if (IsDisposed)
            {
                return;
            }
            _searching = false;
            _searchButton.Enabled = true;

            SoftSearchResult result;
            try
            {
                result = task.Result;
            }
            catch (Exception ex)
            {
                SetStatus("搜索失败：" + ex.Message, true);
                UpdateButtons();
                return;
            }

            if (result.Error.Length > 0)
            {
                SetStatus(result.Error, true);
                UpdateButtons();
                return;
            }

            if (!append)
            {
                _items.Clear();
            }
            _page = page;
            _total = result.Total;
            int added = 0;
            for (int i = 0; i < result.Items.Count; i++)
            {
                SoftItem item = result.Items[i];
                if (IndexOf(item.SoftId) >= 0)
                {
                    continue;
                }
                _items.Add(item);
                added++;
            }
            _list.SetItems(_items);
            RequestLogos(result.Items);
            if (_items.Count == 0)
            {
                SetStatus("没有找到「" + _keyword + "」相关软件，换个关键词试试", true);
            }
            else if (added == 0 && append)
            {
                SetStatus("没有更多了", false);
            }
            else
            {
                SetStatus(
                    "找到 " + _total + " 款 · 点击任意一项直接用普通下载保存安装包",
                    false);
            }
            UpdateButtons();
            Invalidate();
        }

        private int IndexOf(int softId)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].SoftId == softId)
                {
                    return i;
                }
            }
            return -1;
        }

        private void RequestLogos(List<SoftItem> items)
        {
            int started = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (started >= 24)
                {
                    break;
                }
                SoftItem item = items[i];
                if (item.LogoUrl.Length == 0 ||
                    _logos.ContainsKey(item.SoftId) ||
                    _logoLoading.Contains(item.SoftId))
                {
                    continue;
                }
                _logoLoading.Add(item.SoftId);
                started++;
                int id = item.SoftId;
                string url = item.LogoUrl;
                SoftwareCatalog.GetBytesAsync(url)
                    .ContinueWith(delegate(Task<byte[]> finished)
                {
                    Image image = null;
                    try
                    {
                        byte[] bytes = finished.Result;
                        using (MemoryStream stream =
                            new MemoryStream(bytes))
                        using (Image raw = Image.FromStream(stream))
                        {
                            image = new Bitmap(raw, new Size(64, 64));
                        }
                    }
                    catch
                    {
                        image = null;
                    }
                    if (IsDisposed)
                    {
                        if (image != null)
                        {
                            image.Dispose();
                        }
                        return;
                    }
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            _logoLoading.Remove(id);
                            if (image != null && !_logos.ContainsKey(id))
                            {
                                _logos[id] = image;
                            }
                            else if (image != null)
                            {
                                image.Dispose();
                            }
                            _list.Invalidate();
                        });
                    }
                    catch
                    {
                    }
                });
            }
        }

        private Image LogoFor(SoftItem item)
        {
            if (item == null)
            {
                return null;
            }
            Image image;
            return _logos.TryGetValue(item.SoftId, out image)
                ? image : null;
        }

        private SoftDownload JobFor(SoftItem item)
        {
            if (item == null)
            {
                return null;
            }
            SoftDownload job;
            return _jobs.TryGetValue(item.SoftId, out job) ? job : null;
        }

        private void StartDownload(SoftItem item)
        {
            if (item == null || _preview)
            {
                return;
            }
            SoftDownload existing = JobFor(item);
            if (existing != null)
            {
                if (existing.Done)
                {
                    RevealFile(existing.TargetPath);
                    return;
                }
                if (!existing.Failed && !existing.Canceled)
                {
                    return;
                }
            }
            if (item.DownloadUrl.Length == 0)
            {
                SetStatus("这条软件没有可用的下载地址", true);
                return;
            }

            string directory = SoftwareDownloader.DefaultDirectory();
            SoftDownload job = new SoftDownload();
            job.Item = item;
            _jobs[item.SoftId] = job;
            _list.Invalidate();
            SetStatus("开始下载：" + item.Name, false);

            SoftwareDownloader.StartAsync(
                item, directory,
                delegate(SoftDownload progress)
                {
                    if (IsDisposed)
                    {
                        return;
                    }
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            _list.Invalidate();
                            UpdateDownloadStatus(progress);
                        });
                    }
                    catch
                    {
                    }
                })
                .ContinueWith(delegate(Task<SoftDownload> finished)
            {
                if (IsDisposed)
                {
                    return;
                }
                SoftDownload done = null;
                try
                {
                    done = finished.Result;
                }
                catch
                {
                }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        _list.Invalidate();
                        if (done == null)
                        {
                            SetStatus("下载失败", true);
                        }
                        else if (done.Done)
                        {
                            SetStatus(
                                "已保存：" + done.TargetPath, false);
                        }
                        else if (done.Canceled)
                        {
                            SetStatus("已取消：" + item.Name, false);
                        }
                        else if (done.Failed)
                        {
                            SetStatus(done.Error, true);
                        }
                    });
                }
                catch
                {
                }
            });
        }

        private void UpdateDownloadStatus(SoftDownload job)
        {
            if (job == null || job.Item == null || job.Done)
            {
                return;
            }
            string text = "下载中 " + job.Item.Name + " · " +
                SoftwareDownloader.SizeText(job.Received);
            if (job.TotalLength > 0)
            {
                text += " / " +
                    SoftwareDownloader.SizeText(job.TotalLength);
            }
            string speed = SoftwareDownloader.SpeedText(
                job.BytesPerSecond);
            if (speed.Length > 0)
            {
                text += " · " + speed;
            }
            string remaining = SoftwareDownloader.RemainingText(
                job.Received, job.TotalLength, job.BytesPerSecond);
            if (remaining.Length > 0)
            {
                text += " · " + remaining;
            }
            SetStatus(text, false);
        }

        private static void RevealFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }
                Process.Start("explorer.exe",
                    "/select,\"" + path + "\"");
            }
            catch
            {
            }
        }

        private void OpenDownloadFolder()
        {
            try
            {
                string directory = SoftwareDownloader.DefaultDirectory();
                Directory.CreateDirectory(directory);
                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception ex)
            {
                SetStatus("打开文件夹失败：" + ex.Message, true);
            }
        }

        private void UpdateButtons()
        {
            if (_searchButton != null)
            {
                _searchButton.Enabled = !_searching;
            }
            if (_moreButton != null)
            {
                _moreButton.Visible = !_preview &&
                    _items.Count > 0 && _items.Count < _total;
            }
            if (_search != null)
            {
                Invalidate();
            }
        }

        private void SetStatus(string text, bool error)
        {
            _status = text ?? "";
            _statusError = error;
            if (error)
            {
                _statusTimer.Stop();
            }
            else
            {
                _statusTimer.Stop();
                _statusTimer.Start();
            }
            Invalidate();
        }

        internal void LoadPreview(List<SoftItem> items)
        {
            _items.Clear();
            _keyword = "小红书";
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    _items.Add(items[i]);
                }
            }
            _total = _items.Count;
            _search.Text = _keyword;
            _list.SetItems(_items);
            if (_items.Count > 1)
            {
                SoftDownload running = new SoftDownload();
                running.Item = _items[1];
                running.Received = 14680064;
                running.TotalLength = 33168072;
                running.BytesPerSecond = 2411724;
                _jobs[_items[1].SoftId] = running;
            }
            if (_items.Count > 2)
            {
                SoftDownload done = new SoftDownload();
                done.Item = _items[2];
                done.Done = true;
                done.TotalLength = 7690000;
                done.Received = 7690000;
                done.TargetPath = Path.Combine(
                    SoftwareDownloader.DefaultDirectory(),
                    SoftwareCatalog.BuildFileName(_items[2]));
                _jobs[_items[2].SoftId] = done;
            }
            _list.Invalidate();
            SetStatus("找到 " + _total + " 款 · 点击任意一项直接用普通下载保存安装包", false);
            UpdateButtons();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // 搜索框为空时的浅色提示，省掉一个标题占位。
                SendMessage(_search.Inner.Handle, EM_SETCUEBANNER,
                    (IntPtr)1, "搜索软件名，例如 微信、WPS");
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

            int searchWidth = Math.Max(200, width - 32 - 104);
            _search.SetBounds(16, top + 7, searchWidth, 34);
            _searchButton.Location =
                new Point(width - 16 - 92, top + 7);

            int listTop = HeaderHeightValue + SearchHeight;
            int listHeight = Math.Max(
                120, height - listTop - FooterHeight);
            _list.SetBounds(12, listTop, width - 24, listHeight);

            _folderButton.Location = new Point(
                width - 12 - 116, height - FooterHeight + 2);
            _folderButton.Size = new Size(116, 26);
            _moreButton.Location = new Point(
                _folderButton.Left - 8 - 88,
                height - FooterHeight + 2);
            _moreButton.Size = new Size(88, 26);

            int right = width - 12;
            _closeButton.Location = new Point(right - 38, 8);
            _minimizeButton.Location = new Point(right - 76, 8);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.F)
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
                g.DrawLine(pen, 0,
                    ClientSize.Height - FooterHeight,
                    ClientSize.Width,
                    ClientSize.Height - FooterHeight);
            }

            DarkUi.Text2(g, "软件下载", DarkUi.FontStrong,
                DarkUi.Text,
                new Rectangle(18, 10, 110, 26),
                DarkUi.LeftMiddle);

            string subtitle = _preview
                ? "界面预览"
                : "360 软件宝库 · 普通下载";
            DarkUi.Text2(g, subtitle, DarkUi.FontSmall,
                DarkUi.TextFaint,
                new Rectangle(110, 10, 320, 26),
                DarkUi.LeftMiddle);

            string footer = _status;
            DarkUi.Text2(g, footer, DarkUi.FontSmall,
                _statusError ? DarkUi.Danger : DarkUi.TextFaint,
                new Rectangle(16,
                    ClientSize.Height - FooterHeight + 2,
                    ClientSize.Width - 16 - 12 - 116 - 8 - 88 - 12,
                    FooterHeight - 4),
                DarkUi.LeftMiddleClip);
        }
    }

    /// <summary>
    /// 自绘软件列表：每行一张卡片（图标 / 名称 / 评分 / 体积 / 简介 / 下载按钮）。
    /// 单击整行直接开始下载；已完成的行单击就是在资源管理器里定位文件。
    /// </summary>
    internal sealed class SoftListView : Control
    {
        private sealed class Row
        {
            public SoftItem Item;
            public int Top;
            public int Height;
        }

        private readonly List<Row> _rows = new List<Row>();
        private Func<SoftItem, SoftDownload> _jobProvider;
        private Func<SoftItem, Image> _logoProvider;
        private int _scroll;
        private int _contentHeight;
        private int _hover = -1;
        private int _selected = -1;

        public event Action<SoftItem> ItemActivated;
        public event Action<SoftItem, Point> ItemContext;

        public SoftListView()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            BackColor = DarkUi.Window;
            TabStop = true;
        }

        public SoftItem SelectedItem
        {
            get
            {
                if (_selected >= 0 && _selected < _rows.Count)
                {
                    return _rows[_selected].Item;
                }
                return null;
            }
        }

        public void SetProviders(
            Func<SoftItem, SoftDownload> jobProvider,
            Func<SoftItem, Image> logoProvider)
        {
            _jobProvider = jobProvider;
            _logoProvider = logoProvider;
        }

        public void SetItems(List<SoftItem> items)
        {
            _rows.Clear();
            int y = 4;
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Row row = new Row();
                    row.Item = items[i];
                    row.Top = y;
                    row.Height = 84;
                    _rows.Add(row);
                    y += row.Height;
                }
            }
            _contentHeight = y + 8;
            if (_selected >= _rows.Count)
            {
                _selected = _rows.Count - 1;
            }
            ClampScroll();
            Invalidate();
        }

        public void SelectItem(SoftItem item)
        {
            if (item == null)
            {
                return;
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Item.SoftId == item.SoftId)
                {
                    _selected = i;
                    Invalidate();
                    return;
                }
            }
        }

        public void DisposeLogos()
        {
            // 图片由窗口统一持有，这里只负责断开引用。
            _logoProvider = null;
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

        private int RowAt(Point point)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                int top = _rows[i].Top - _scroll;
                if (point.Y >= top && point.Y < top + _rows[i].Height)
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = RowAt(e.Location);
            if (index != _hover)
            {
                _hover = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1)
            {
                _hover = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int index = RowAt(e.Location);
            if (index >= 0)
            {
                _selected = index;
                if (e.Button == MouseButtons.Left &&
                    ItemActivated != null)
                {
                    ItemActivated(_rows[index].Item);
                }
                else if (e.Button == MouseButtons.Right &&
                    ItemContext != null)
                {
                    ItemContext(_rows[index].Item, e.Location);
                }
            }
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll -= e.Delta;
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                if (_selected < _rows.Count - 1)
                {
                    _selected++;
                    EnsureVisible(_selected);
                    Invalidate();
                }
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Up)
            {
                if (_selected > 0)
                {
                    _selected--;
                    EnsureVisible(_selected);
                    Invalidate();
                }
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && _selected >= 0 &&
                _selected < _rows.Count && ItemActivated != null)
            {
                ItemActivated(_rows[_selected].Item);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void EnsureVisible(int index)
        {
            if (index < 0 || index >= _rows.Count)
            {
                return;
            }
            int top = _rows[index].Top;
            int bottom = top + _rows[index].Height;
            if (top - _scroll < 0)
            {
                _scroll = top;
            }
            else if (bottom - _scroll > Height)
            {
                _scroll = bottom - Height;
            }
            ClampScroll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(DarkUi.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (_rows.Count == 0)
            {
                DrawEmpty(g);
                return;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                int top = _rows[i].Top - _scroll;
                if (top + _rows[i].Height < 0 || top > Height)
                {
                    continue;
                }
                DrawRow(g, i, top);
            }
            DrawScrollBar(g);
        }

        private void DrawEmpty(Graphics g)
        {
            DarkUi.Text2(g, "搜索软件后，点一下就能直接下载安装包",
                DarkUi.FontStrong, DarkUi.TextDim,
                new Rectangle(0, Height / 2 - 40, Width, 28),
                DarkUi.CenterMiddle);
            DarkUi.Text2(g,
                "下载的是「普通下载」的安装包，不会顺带安装 360 卫士",
                DarkUi.FontSmall, DarkUi.TextFaint,
                new Rectangle(0, Height / 2 - 12, Width, 24),
                DarkUi.CenterMiddle);
            DarkUi.Text2(g, "试试搜索：微信 · WPS · 网易云音乐 · 小红书",
                DarkUi.FontSmall, DarkUi.TextFaint,
                new Rectangle(0, Height / 2 + 16, Width, 24),
                DarkUi.CenterMiddle);
        }

        private void DrawRow(Graphics g, int index, int top)
        {
            Row row = _rows[index];
            Rectangle card = new Rectangle(6, top + 4, Width - 22, 76);
            bool active = index == _hover || index == _selected;

            using (GraphicsPath path = DarkUi.Rounded(card, 10f))
            {
                using (SolidBrush brush = new SolidBrush(
                    active ? DarkUi.CardHover : DarkUi.Card))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            Rectangle logo = new Rectangle(
                card.Left + 14, card.Top + 14, 48, 48);
            DrawLogo(g, row.Item, logo);

            int textLeft = logo.Right + 14;
            int pillWidth = 96;
            int textRight = card.Right - pillWidth - 26;
            int textWidth = Math.Max(80, textRight - textLeft);

            DarkUi.Text2(g, row.Item.Name, DarkUi.FontStrong,
                DarkUi.Text,
                new Rectangle(textLeft, card.Top + 8, textWidth, 22),
                DarkUi.LeftMiddleClip);

            // 第二行：评分 · 分类 · 体积
            string meta = row.Item.RatingText;
            Color metaColor = Color.FromArgb(240, 196, 96);
            int metaLeft = textLeft;
            if (meta.Length > 0)
            {
                Size metaSize = TextRenderer.MeasureText(
                    meta, DarkUi.FontSmall);
                DarkUi.Text2(g, meta, DarkUi.FontSmall, metaColor,
                    new Rectangle(metaLeft, card.Top + 31,
                        metaSize.Width + 4, 18),
                    DarkUi.LeftMiddle);
                metaLeft += metaSize.Width + 4;
            }
            string extra = "";
            if (row.Item.Category.Length > 0)
            {
                extra += "· " + row.Item.Category + "  ";
            }
            if (row.Item.SizeText.Length > 0)
            {
                extra += "· " + row.Item.SizeText;
            }
            if (extra.Length > 0)
            {
                DarkUi.Text2(g, extra, DarkUi.FontSmall,
                    DarkUi.TextDim,
                    new Rectangle(metaLeft, card.Top + 31,
                        Math.Max(40, card.Right - pillWidth - 20 - metaLeft),
                        18),
                    DarkUi.LeftMiddleClip);
            }

            DarkUi.Text2(g,
                SoftwareCatalog.OneLine(row.Item.Description, 120),
                DarkUi.FontSmall, DarkUi.TextFaint,
                new Rectangle(textLeft, card.Top + 50, textWidth, 18),
                DarkUi.LeftMiddleClip);

            SoftDownload job = _jobProvider == null
                ? null : _jobProvider(row.Item);
            Rectangle pill = new Rectangle(
                card.Right - pillWidth - 14,
                card.Top + (card.Height - 30) / 2, pillWidth, 30);
            DrawPill(g, pill, job);
        }

        private void DrawLogo(Graphics g, SoftItem item, Rectangle box)
        {
            Image logo = _logoProvider == null
                ? null : _logoProvider(item);
            if (logo != null)
            {
                using (GraphicsPath path = DarkUi.Rounded(box, 10f))
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    g.DrawImage(logo, box);
                    g.Clip = old;
                }
                using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
                using (GraphicsPath path = DarkUi.Rounded(box, 10f))
                {
                    g.DrawPath(pen, path);
                }
                return;
            }

            // 图标还没下载好：先画一个带首字的色块，避免行里空一块。
            Color fill = LogoColor(item.SoftId);
            using (GraphicsPath path = DarkUi.Rounded(box, 10f))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
            string letter = item.Name.Length > 0
                ? item.Name.Substring(0, 1) : "软";
            DarkUi.Text2(g, letter, DarkUi.FontBody,
                Color.FromArgb(235, 240, 250),
                box, DarkUi.CenterMiddle);
        }

        private static Color LogoColor(int seed)
        {
            Color[] palette = new Color[]
            {
                Color.FromArgb(64, 104, 168),
                Color.FromArgb(96, 88, 160),
                Color.FromArgb(56, 122, 116),
                Color.FromArgb(150, 92, 96),
                Color.FromArgb(120, 104, 64),
                Color.FromArgb(72, 96, 136)
            };
            int index = Math.Abs(seed) % palette.Length;
            return palette[index];
        }

        private void DrawPill(Graphics g, Rectangle rect, SoftDownload job)
        {
            string text;
            Color fill;
            Color ink;
            double progress = -1;

            if (job == null)
            {
                text = "下载";
                fill = DarkUi.Accent;
                ink = Color.White;
            }
            else if (job.Done)
            {
                text = "打开";
                fill = Color.FromArgb(46, 96, 66);
                ink = Color.FromArgb(150, 232, 180);
            }
            else if (job.Failed || job.Canceled)
            {
                text = "重试";
                fill = DarkUi.Surface;
                ink = DarkUi.TextDim;
            }
            else
            {
                progress = job.Progress;
                text = progress >= 0
                    ? (int)(progress * 100) + "%"
                    : "下载中";
                fill = DarkUi.AccentSoft;
                ink = DarkUi.AccentHover;
            }

            using (GraphicsPath path = DarkUi.Rounded(rect, 15f))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
            DarkUi.Text2(g, text, DarkUi.FontSmall, ink, rect,
                DarkUi.CenterMiddle);

            if (progress >= 0)
            {
                int trackWidth = rect.Width - 16;
                int width = (int)(trackWidth * progress);
                if (width > 0)
                {
                    using (SolidBrush bar =
                        new SolidBrush(DarkUi.AccentHover))
                    {
                        g.FillRectangle(bar, rect.Left + 8,
                            rect.Bottom - 6, width, 2);
                    }
                }
            }
        }

        private void DrawScrollBar(Graphics g)
        {
            if (_contentHeight <= Height)
            {
                return;
            }
            int trackHeight = Height - 8;
            int thumbHeight = Math.Max(
                36, (int)((double)Height / _contentHeight * trackHeight));
            int maxScroll = _contentHeight - Height;
            int thumbTop = 4 + (int)(
                (double)_scroll / maxScroll * (trackHeight - thumbHeight));
            Rectangle thumb = new Rectangle(
                Width - 7, thumbTop, 3, thumbHeight);
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(80, 86, 96)))
            {
                g.FillRectangle(brush, thumb);
            }
        }
    }
}
