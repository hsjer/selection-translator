using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 剪贴板历史窗口：顶部搜索 + 分类标签，中间按日期分组的倒序列表，
    /// 右侧竖排操作按钮。双击（或 Enter）把选中条目写回系统剪贴板。
    /// 全部用 DarkUi 自绘，跟图片查看器保持同一套深色外观，不引入新依赖。
    /// </summary>
    internal sealed class ClipboardHistoryForm : ChromeForm
    {
        private const int HeaderHeightValue = 46;
        private const int SearchHeight = 48;
        private const int TabsHeight = 44;
        private const int FooterHeight = 30;
        private const int RightWidth = 96;

        private static ClipboardHistoryForm _instance;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(
            IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        private readonly ClipboardManager _manager;
        private readonly FieldBox _search;
        private readonly FlatButton[] _tabs = new FlatButton[5];
        private readonly ClipboardList _list;
        private readonly FlatButton _copyButton;
        private readonly FlatButton _favButton;
        private readonly FlatButton _deleteButton;
        private readonly FlatButton _clearButton;
        private readonly FlatButton _settingsButton;
        private readonly GlyphButton _minimizeButton;
        private readonly GlyphButton _closeButton;
        private readonly ContextMenuStrip _itemMenu;
        private readonly ContextMenuStrip _settingsMenu;
        private readonly ToolStripMenuItem[] _ttlItems;
        private readonly ToolStripMenuItem _pauseItem;
        private readonly EventHandler _historyChangedHandler;
        private readonly Timer _clock;
        private readonly Timer _statusTimer;
        private ClipboardTab _tab = ClipboardTab.All;
        private string _status = "";

        public static void ShowHistory()
        {
            ClipboardManager manager = ClipboardManager.Instance;
            if (manager == null)
            {
                return;
            }
            if (_instance != null && !_instance.IsDisposed)
            {
                _instance.RefreshAll();
                _instance.Activate();
                return;
            }
            ClipboardHistoryForm form =
                new ClipboardHistoryForm(manager);
            _instance = form;
            form.FormClosed += delegate
            {
                _instance = null;
            };
            form.Show();
            form.Activate();
        }

        public static void CloseHistory()
        {
            if (_instance != null && !_instance.IsDisposed)
            {
                _instance.Close();
            }
            _instance = null;
        }

        private ClipboardHistoryForm(ClipboardManager manager)
        {
            _manager = manager;
            Text = "剪贴板历史 - " + AppInfo.Name;
            ClientSize = new Size(820, 660);
            MinimumSize = new Size(680, 500);
            ShowInTaskbar = true;
            KeyPreview = true;

            _search = new FieldBox();
            _search.TextChanged += delegate
            {
                RefreshAll();
            };
            Controls.Add(_search);

            for (int i = 0; i < _tabs.Length; i++)
            {
                ClipboardTab tab = (ClipboardTab)i;
                FlatButton button = new FlatButton();
                button.Size = new Size(88, 30);
                button.SurfaceColor = DarkUi.Window;
                button.Radius = 7;
                button.Click += delegate
                {
                    _tab = tab;
                    UpdateTabs();
                    RefreshAll();
                };
                _tabs[i] = button;
                Controls.Add(button);
            }

            _list = new ClipboardList(manager.Store);
            _list.ItemActivated += delegate(ClipboardItem item)
            {
                CopySelected();
            };
            _list.SelectionChanged += delegate
            {
                UpdateActionButtons();
            };
            _list.ItemContext += delegate(ClipboardItem item, Point at)
            {
                ShowItemMenu(item, at);
            };
            Controls.Add(_list);

            _copyButton = MakeAction("复制", false, delegate
            {
                CopySelected();
            });
            _favButton = MakeAction("收藏", false, delegate
            {
                ToggleFavorite();
            });
            _deleteButton = MakeAction("删除", false, delegate
            {
                DeleteSelected();
            });
            _clearButton = MakeAction("清空", true, delegate
            {
                ClearHistory();
            });
            _settingsButton = MakeAction("设置", false, delegate
            {
                ShowSettingsMenu();
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

            _itemMenu = new ContextMenuStrip();
            _settingsMenu = new ContextMenuStrip();
            _ttlItems = new ToolStripMenuItem[
                ClipboardLogic.TtlChoices.Length];
            _pauseItem = BuildSettingsMenu();

            _clock = new Timer();
            _clock.Interval = 30000;
            _clock.Tick += delegate { RefreshAll(); };
            _clock.Start();

            _statusTimer = new Timer();
            _statusTimer.Interval = 2600;
            _statusTimer.Tick += delegate
            {
                _status = "";
                _statusTimer.Stop();
                Invalidate();
            };

            _historyChangedHandler = delegate
            {
                RefreshAll();
            };
            _manager.HistoryChanged += _historyChangedHandler;
            FormClosed += delegate
            {
                _manager.HistoryChanged -= _historyChangedHandler;
                _clock.Stop();
                _clock.Dispose();
                _statusTimer.Stop();
                _statusTimer.Dispose();
                _list.DisposeThumbs();
            };

            UpdateTabs();
            DoLayout();
            RefreshAll();
        }

        protected override int HeaderHeight
        {
            get { return HeaderHeightValue; }
        }

        private FlatButton MakeAction(
            string text, bool danger, EventHandler onClick)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Size = new Size(RightWidth - 20, 32);
            button.SurfaceColor = DarkUi.Window;
            button.Danger = danger;
            button.Radius = 7;
            button.Click += onClick;
            Controls.Add(button);
            return button;
        }

        private ToolStripMenuItem BuildSettingsMenu()
        {
            ToolStripMenuItem title =
                new ToolStripMenuItem("保留时长（TTL）");
            title.Enabled = false;
            _settingsMenu.Items.Add(title);
            for (int i = 0; i < ClipboardLogic.TtlChoices.Length; i++)
            {
                int hours = ClipboardLogic.TtlChoices[i];
                ToolStripMenuItem item = new ToolStripMenuItem(
                    ClipboardLogic.TtlLabels[i]);
                item.Click += delegate
                {
                    SetTtl(hours);
                };
                _ttlItems[i] = item;
                _settingsMenu.Items.Add(item);
            }
            _settingsMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem pauseItem =
                new ToolStripMenuItem("暂停记录");
            pauseItem.Click += delegate
            {
                ToggleListening();
            };
            _settingsMenu.Items.Add(pauseItem);

            ToolStripMenuItem purgeItem =
                new ToolStripMenuItem("立即清理过期项");
            purgeItem.Click += delegate
            {
                _manager.PurgeNow();
                RefreshAll();
                SetStatus("已清理过期记录");
            };
            _settingsMenu.Items.Add(purgeItem);

            ToolStripMenuItem openSettings =
                new ToolStripMenuItem("更多设置…");
            openSettings.Click += delegate
            {
                SetStatus("剪贴板设置也可在托盘「设置」里调整");
            };
            _settingsMenu.Items.Add(openSettings);
            return pauseItem;
        }

        private void ShowSettingsMenu()
        {
            int current = ClipboardLogic.TtlIndex(
                _manager.TtlHours);
            for (int i = 0; i < _ttlItems.Length; i++)
            {
                _ttlItems[i].Checked = i == current;
            }
            _pauseItem.Text = _manager.Enabled
                ? "暂停记录" : "继续记录";
            _settingsMenu.Show(
                _settingsButton,
                new Point(0, _settingsButton.Height));
        }

        private void SetTtl(int hours)
        {
            AppConfig config = _manager.Config;
            if (config == null)
            {
                return;
            }
            config.ClipboardTtlHours =
                ClipboardLogic.NormalizeTtl(hours);
            try
            {
                ConfigStore.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error("保存剪贴板 TTL 失败", ex);
            }
            _manager.ApplyConfig(config);
            RefreshAll();
            SetStatus("保留时长：" +
                ClipboardLogic.TtlLabel(config.ClipboardTtlHours) +
                "（收藏不受限制）");
        }

        private void ToggleListening()
        {
            AppConfig config = _manager.Config;
            if (config == null)
            {
                return;
            }
            config.EnableClipboardHistory =
                !config.EnableClipboardHistory;
            try
            {
                ConfigStore.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error("保存剪贴板开关失败", ex);
            }
            _manager.ApplyConfig(config);
            RefreshAll();
            SetStatus(config.EnableClipboardHistory
                ? "已开始记录剪贴板" : "已暂停记录剪贴板");
        }

        private void SetStatus(string text)
        {
            _status = text;
            _statusTimer.Stop();
            _statusTimer.Start();
            Invalidate();
        }

        private void UpdateTabs()
        {
            int favorites = _manager.Store.CountFavorites();
            for (int i = 0; i < _tabs.Length; i++)
            {
                ClipboardTab tab = (ClipboardTab)i;
                _tabs[i].Text = ClipboardLogic.TabLabel(tab) +
                    (tab == ClipboardTab.Favorite
                        ? " (" + favorites + ")" : "");
                _tabs[i].Checked = tab == _tab;
            }
        }

        private void RefreshAll()
        {
            if (IsDisposed)
            {
                return;
            }
            List<ClipboardItem> items = ClipboardLogic.Filter(
                _manager.Store.Items, _tab, _search.Text);
            _list.SetItems(items, DateTime.Now);
            UpdateTabs();
            UpdateActionButtons();
            Invalidate();
        }

        private void UpdateActionButtons()
        {
            ClipboardItem item = _list.SelectedItem;
            _copyButton.Enabled = item != null;
            _favButton.Enabled = item != null;
            _deleteButton.Enabled = item != null;
            _favButton.Text = item != null && item.Favorite
                ? "取消收藏" : "收藏";
        }

        private void CopySelected()
        {
            ClipboardItem item = _list.SelectedItem;
            if (item == null)
            {
                return;
            }
            if (_manager.CopyToClipboard(item))
            {
                SetStatus("已复制到剪贴板：" +
                    ClipboardLogic.Preview(item, 32));
            }
            else
            {
                SetStatus("复制失败：原始图片 / 文件已不存在");
            }
        }

        private void ToggleFavorite()
        {
            ClipboardItem item = _list.SelectedItem;
            if (item == null)
            {
                return;
            }
            _manager.SetFavorite(item, !item.Favorite);
            SetStatus(item.Favorite ? "已收藏（不受 TTL 限制）" : "已取消收藏");
            RefreshAll();
        }

        private void DeleteSelected()
        {
            ClipboardItem item = _list.SelectedItem;
            if (item == null)
            {
                return;
            }
            _manager.Remove(item);
            SetStatus("已删除 1 条记录");
            RefreshAll();
        }

        private void ClearHistory()
        {
            if (!ImageMessage.Ask(
                    this, "清空剪贴板历史",
                    "确定清空全部历史吗？\r\n已收藏的条目会保留。",
                    "清空"))
            {
                return;
            }
            _manager.Clear(true);
            SetStatus("已清空历史（收藏保留）");
            RefreshAll();
        }

        private void ShowItemMenu(ClipboardItem item, Point at)
        {
            _itemMenu.Items.Clear();
            if (item == null)
            {
                return;
            }
            ToolStripMenuItem copy =
                new ToolStripMenuItem("复制");
            copy.Click += delegate
            {
                _list.SelectItem(item);
                CopySelected();
            };
            _itemMenu.Items.Add(copy);

            ToolStripMenuItem favorite =
                new ToolStripMenuItem(
                    item.Favorite ? "取消收藏" : "收藏");
            favorite.Click += delegate { ToggleFavorite(); };
            _itemMenu.Items.Add(favorite);

            ToolStripMenuItem delete =
                new ToolStripMenuItem("删除");
            delete.Click += delegate { DeleteSelected(); };
            _itemMenu.Items.Add(delete);

            if (item.Kind == (int)ClipboardItemKind.Image ||
                item.Kind == (int)ClipboardItemKind.File)
            {
                _itemMenu.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem open =
                    new ToolStripMenuItem("打开 / 定位");
                open.Click += delegate { OpenItem(item); };
                _itemMenu.Items.Add(open);
            }
            _itemMenu.Show(_list, at);
        }

        private void OpenItem(ClipboardItem item)
        {
            try
            {
                if (item.Kind == (int)ClipboardItemKind.Image)
                {
                    string path = _manager.Store
                        .ImageFullPath(item.ImageFile);
                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo(path)
                        {
                            UseShellExecute = true
                        });
                    }
                    return;
                }
                if (item.Files != null && item.Files.Length > 0 &&
                    File.Exists(item.Files[0]))
                {
                    Process.Start("explorer.exe",
                        "/select,\"" + item.Files[0] + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.Error("打开剪贴板条目失败", ex);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // TextBox 自带的水印：不会出现一个盖住输入区的 Label，
            // 用户点击搜索框任何位置都能直接开始输入。
            try
            {
                SendMessage(
                    _search.Inner.Handle,
                    EM_SETCUEBANNER,
                    (IntPtr)1,
                    "搜索文本 / 文件名 / 尺寸…");
            }
            catch
            {
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool typing = _search.Inner.Focused;
            if (e.Control && e.KeyCode == Keys.F)
            {
                _search.Inner.Focus();
                _search.Inner.SelectAll();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                if (typing && _search.Text.Length > 0)
                {
                    _search.Text = "";
                }
                else
                {
                    Close();
                }
                e.Handled = true;
                return;
            }
            if (typing)
            {
                base.OnKeyDown(e);
                return;
            }
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                _list.MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                CopySelected();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected();
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
                160, width - 32 - 120);
            _search.SetBounds(16, top + 7, searchWidth, 34);

            int tabsTop = top + SearchHeight + 6;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].Location =
                    new Point(16 + i * 96, tabsTop);
            }

            int listTop = HeaderHeightValue +
                SearchHeight + TabsHeight;
            int listHeight = Math.Max(
                80, height - listTop - FooterHeight);
            int listWidth = Math.Max(
                200, width - 24 - RightWidth);
            _list.SetBounds(12, listTop, listWidth, listHeight);

            int buttonX = width - RightWidth + 2;
            FlatButton[] actions = new FlatButton[]
            {
                _copyButton, _favButton, _deleteButton,
                _clearButton, _settingsButton
            };
            for (int i = 0; i < actions.Length; i++)
            {
                actions[i].Location = new Point(
                    buttonX, listTop + 6 + i * 40);
            }

            int right = width - 12;
            _closeButton.Location = new Point(right - 38, 8);
            _minimizeButton.Location = new Point(right - 76, 8);
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

            DarkUi.Text2(g, "剪贴板", DarkUi.FontStrong,
                DarkUi.Text,
                new Rectangle(18, 10, 120, 26),
                DarkUi.LeftMiddle);

            string countText = "共 " + _manager.Store.Items.Count +
                " 条 · 显示 " + _list.VisibleCount + " 条";
            DarkUi.Text2(g, countText, DarkUi.FontSmall,
                DarkUi.TextFaint,
                new Rectangle(112, 10, 280, 26),
                DarkUi.LeftMiddle);

            string searchHint = "按 Ctrl+F 搜索";
            DarkUi.Text2(g, searchHint, DarkUi.FontSmall,
                DarkUi.TextFaint,
                new Rectangle(ClientSize.Width - 136,
                    HeaderHeightValue + 12, 120, 24),
                DarkUi.LeftMiddle);

            string footer = _status.Length > 0
                ? _status
                : "双击复制 · Enter 复制 · Delete 删除 · 保留 " +
                    ClipboardLogic.TtlLabel(_manager.TtlHours) +
                    (_manager.Enabled ? "" : " · 已暂停记录");
            DarkUi.Text2(g, footer, DarkUi.FontSmall,
                _status.Length > 0 ? DarkUi.Accent : DarkUi.TextFaint,
                new Rectangle(16,
                    ClientSize.Height - FooterHeight + 2,
                    ClientSize.Width - 32, FooterHeight - 4),
                DarkUi.LeftMiddleClip);
        }
    }

    /// <summary>
    /// 自绘历史列表：按日期分组、倒序、支持图片缩略图、鼠标滚轮、
    /// 悬停高亮、选中、双击与右键。不用 ListView 是为了完全控制外观，
    /// 也避免为每种类型写 OwnerDraw 的复杂状态。
    /// </summary>
    internal sealed class ClipboardList : Control
    {
        private sealed class Row
        {
            public ClipboardItem Item;
            public string Group;
            public int Top;
            public int Height;
        }

        private readonly ClipboardStore _store;
        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<long, Image> _thumbs =
            new Dictionary<long, Image>();
        private int _scroll;
        private int _contentHeight;
        private int _selected = -1;
        private int _hover = -1;
        private int _visibleCount;

        public event Action<ClipboardItem> ItemActivated;
        public event Action<ClipboardItem, Point> ItemContext;
        public event EventHandler SelectionChanged;

        public ClipboardList(ClipboardStore store)
        {
            _store = store;
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

        public int VisibleCount
        {
            get { return _visibleCount; }
        }

        public ClipboardItem SelectedItem
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

        public void SetItems(List<ClipboardItem> items, DateTime nowLocal)
        {
            long selectedId = SelectedItem == null
                ? -1 : SelectedItem.Id;
            _rows.Clear();
            _visibleCount = items == null ? 0 : items.Count;
            int y = 4;
            string lastGroup = null;
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ClipboardItem item = items[i];
                    DateTime local;
                    try
                    {
                        local = item.Utc.ToLocalTime();
                    }
                    catch
                    {
                        local = nowLocal;
                    }
                    string group = ClipboardLogic.DateGroup(
                        local, nowLocal);
                    if (group != lastGroup)
                    {
                        Row groupRow = new Row();
                        groupRow.Group = group;
                        groupRow.Top = y;
                        groupRow.Height = 28;
                        _rows.Add(groupRow);
                        y += 28;
                        lastGroup = group;
                    }
                    Row row = new Row();
                    row.Item = item;
                    row.Top = y;
                    row.Height = ItemHeight(item);
                    _rows.Add(row);
                    y += row.Height;
                }
            }
            _contentHeight = y + 8;
            _selected = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Item != null &&
                    _rows[i].Item.Id == selectedId)
                {
                    _selected = i;
                    break;
                }
            }
            if (_selected < 0)
            {
                SelectFirst();
            }
            ClampScroll();
            if (_thumbs.Count > 30)
            {
                DisposeThumbs();
            }
            Invalidate();
        }

        private static int ItemHeight(ClipboardItem item)
        {
            if (item.Kind == (int)ClipboardItemKind.Image)
            {
                return 156;
            }
            if (item.Kind == (int)ClipboardItemKind.File)
            {
                return 108;
            }
            return 106;
        }

        private void SelectFirst()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Item != null)
                {
                    _selected = i;
                    return;
                }
            }
            _selected = -1;
        }

        public void SelectItem(ClipboardItem item)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Item == item)
                {
                    _selected = i;
                    EnsureVisible(i);
                    Invalidate();
                    RaiseSelectionChanged();
                    return;
                }
            }
        }

        public void MoveSelection(int delta)
        {
            if (_rows.Count == 0)
            {
                return;
            }
            int next = _selected;
            for (int step = 0; step < _rows.Count; step++)
            {
                next += delta;
                if (next < 0 || next >= _rows.Count)
                {
                    return;
                }
                if (_rows[next].Item != null)
                {
                    _selected = next;
                    EnsureVisible(next);
                    Invalidate();
                    RaiseSelectionChanged();
                    return;
                }
            }
        }

        private void EnsureVisible(int index)
        {
            Row row = _rows[index];
            int top = row.Top - _scroll;
            if (top < 0)
            {
                _scroll = row.Top - 4;
            }
            else if (top + row.Height > Height)
            {
                _scroll = row.Top + row.Height - Height + 4;
            }
            ClampScroll();
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
            int y = point.Y + _scroll;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (y >= _rows[i].Top &&
                    y < _rows[i].Top + _rows[i].Height &&
                    _rows[i].Item != null)
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int index = RowAt(e.Location);
            if (index >= 0)
            {
                _selected = index;
                Invalidate();
                RaiseSelectionChanged();
                if (e.Button == MouseButtons.Right &&
                    ItemContext != null)
                {
                    ItemContext(_rows[index].Item, e.Location);
                }
                if (e.Button == MouseButtons.Left && e.Clicks >= 2 &&
                    ItemActivated != null)
                {
                    ItemActivated(_rows[index].Item);
                }
            }
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = RowAt(e.Location);
            if (index != _hover)
            {
                _hover = index;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll -= (e.Delta / 120) * 72;
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        private void RaiseSelectionChanged()
        {
            EventHandler handler = SelectionChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void DisposeThumbs()
        {
            foreach (Image image in _thumbs.Values)
            {
                if (image != null)
                {
                    image.Dispose();
                }
            }
            _thumbs.Clear();
        }

        private Image GetThumb(ClipboardItem item)
        {
            if (_thumbs.ContainsKey(item.Id))
            {
                return _thumbs[item.Id];
            }
            Image thumb = null;
            try
            {
                string path = _store.ImageFullPath(item.ImageFile);
                if (File.Exists(path))
                {
                    using (Image source = Image.FromFile(path))
                    {
                        thumb = new Bitmap(source);
                    }
                }
            }
            catch
            {
                thumb = null;
            }
            _thumbs[item.Id] = thumb;
            return thumb;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(DarkUi.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_rows.Count == 0)
            {
                string empty = "还没有记录。复制一段文字、图片或文件后，"
                    + "这里会自动出现。";
                TextRenderer.DrawText(
                    g, empty, DarkUi.FontSmall,
                    new Rectangle(24, 40, Math.Max(80, Width - 48), 60),
                    DarkUi.TextFaint,
                    TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPadding);
                return;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                int top = row.Top - _scroll;
                if (top > Height || top + row.Height < 0)
                {
                    continue;
                }
                if (row.Item == null)
                {
                    DrawGroupRow(g, row, top);
                }
                else
                {
                    DrawItemRow(g, row, top, i);
                }
            }
            DrawScrollBar(g);
        }

        private void DrawGroupRow(Graphics g, Row row, int top)
        {
            DarkUi.Text2(g, row.Group, DarkUi.FontSmall,
                DarkUi.TextFaint,
                new Rectangle(16, top + 4, 160, 20),
                DarkUi.LeftMiddle);
            using (Pen pen = new Pen(DarkUi.BorderSoft, 1f))
            {
                g.DrawLine(pen, 82, top + 14,
                    Width - 18, top + 14);
            }
        }

        private void DrawItemRow(
            Graphics g, Row row, int top, int index)
        {
            ClipboardItem item = row.Item;
            Rectangle card = new Rectangle(
                8, top + 2, Math.Max(40, Width - 16), row.Height - 6);
            bool selected = index == _selected;
            bool hover = index == _hover;
            Color fill = selected
                ? DarkUi.AccentSoft
                : (hover ? DarkUi.CardHover : DarkUi.Card);
            using (GraphicsPath path = DarkUi.Rounded(
                new RectangleF(card.X + 0.5f, card.Y + 0.5f,
                    card.Width - 1f, card.Height - 1f), 9f))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                using (Pen pen = new Pen(
                    selected ? DarkUi.Accent : DarkUi.BorderSoft, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            int left = card.Left + 12;
            int right = card.Right - 12;
            int lineTop = card.Top + 8;

            // 类型徽标
            Rectangle badge = new Rectangle(left, lineTop + 1, 46, 19);
            Color badgeColor = item.Kind ==
                (int)ClipboardItemKind.Image
                    ? Color.FromArgb(78, 142, 116)
                    : (item.Kind == (int)ClipboardItemKind.File
                        ? Color.FromArgb(150, 116, 74)
                        : DarkUi.Accent);
            using (GraphicsPath badgePath = DarkUi.Rounded(
                new RectangleF(badge.X, badge.Y,
                    badge.Width, badge.Height), 5f))
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(46, badgeColor)))
            {
                g.FillPath(brush, badgePath);
            }
            DarkUi.Text2(g, ClipboardLogic.KindLabel(item.Kind),
                DarkUi.FontTiny, badgeColor,
                badge, DarkUi.CenterMiddle);

            int badgeRight = badge.Right + 8;
            if (item.Favorite)
            {
                DarkUi.Text2(g, "★ 收藏", DarkUi.FontTiny,
                    Color.FromArgb(240, 196, 96),
                    new Rectangle(badgeRight, lineTop, 60, 20),
                    DarkUi.LeftMiddle);
                badgeRight += 64;
            }

            // 字符数 / 文件数放在类型徽标后面，正文区就能完整留给内容，
            // 不会再出现第三行文字压到底部小字上的情况。
            string meta = "";
            if (item.Kind == (int)ClipboardItemKind.Text)
            {
                meta = (item.Text ?? "").Length + " 字符";
            }
            else if (item.Kind == (int)ClipboardItemKind.File)
            {
                meta = (item.Files == null ? 0 : item.Files.Length) +
                    " 个文件";
            }
            if (meta.Length > 0)
            {
                DarkUi.Text2(g, meta, DarkUi.FontTiny,
                    DarkUi.TextFaint,
                    new Rectangle(badgeRight, lineTop, 100, 20),
                    DarkUi.LeftMiddle);
            }

            DateTime local;
            try
            {
                local = item.Utc.ToLocalTime();
            }
            catch
            {
                local = DateTime.Now;
            }
            string when = ClipboardLogic.FormatRelative(
                local, DateTime.Now);
            DarkUi.Text2(g, when, DarkUi.FontTiny,
                DarkUi.TextFaint,
                new Rectangle(right - 190, lineTop, 120, 20),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            DarkUi.Text2(g, "× " +
                ClipboardLogic.FormatCount(item.Count),
                DarkUi.FontTiny, DarkUi.TextFaint,
                new Rectangle(right - 60, lineTop, 60, 20),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            Rectangle body = new Rectangle(
                left, lineTop + 26,
                Math.Max(60, right - left),
                Math.Max(20, card.Bottom - (lineTop + 26) - 8));

            if (item.Kind == (int)ClipboardItemKind.Image)
            {
                Image thumb = GetThumb(item);
                Rectangle imageBox = new Rectangle(
                    body.Left, body.Top,
                    Math.Min(240, body.Width - 190),
                    body.Height);
                if (imageBox.Width > 20 && imageBox.Height > 20)
                {
                    if (thumb != null)
                    {
                        double ratio = Math.Min(
                            (double)imageBox.Width / thumb.Width,
                            (double)imageBox.Height / thumb.Height);
                        int drawWidth = Math.Max(1,
                            (int)(thumb.Width * ratio));
                        int drawHeight = Math.Max(1,
                            (int)(thumb.Height * ratio));
                        g.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        g.DrawImage(thumb,
                            new Rectangle(imageBox.Left,
                                imageBox.Top + (imageBox.Height - drawHeight) / 2,
                                drawWidth, drawHeight));
                    }
                    else
                    {
                        DarkUi.Text2(g, "图片已丢失",
                            DarkUi.FontSmall, DarkUi.TextFaint,
                            imageBox, DarkUi.CenterMiddle);
                    }
                }
                int infoLeft = imageBox.Right + 14;
                DarkUi.Text2(g,
                    item.ImageWidth + " × " + item.ImageHeight,
                    DarkUi.FontBody, DarkUi.Text,
                    new Rectangle(infoLeft, body.Top + 8,
                        Math.Max(60, right - infoLeft), 24),
                    DarkUi.LeftMiddleClip);
                DarkUi.Text2(g, "双击复制图片",
                    DarkUi.FontTiny, DarkUi.TextFaint,
                    new Rectangle(infoLeft, body.Top + 38,
                        Math.Max(60, right - infoLeft), 22),
                    DarkUi.LeftMiddleClip);
                return;
            }

            string preview;
            if (item.Kind == (int)ClipboardItemKind.File)
            {
                preview = FileListPreview(item);
            }
            else
            {
                preview = ClipboardLogic.Preview(item, 260);
            }
            TextRenderer.DrawText(g, preview, DarkUi.FontBody,
                body, DarkUi.Text,
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);
        }

        private static string FileListPreview(ClipboardItem item)
        {
            if (item.Files == null || item.Files.Length == 0)
            {
                return "（空文件列表）";
            }
            string text = "";
            int count = Math.Min(3, item.Files.Length);
            for (int i = 0; i < count; i++)
            {
                if (text.Length > 0)
                {
                    text += "\r\n";
                }
                text += Path.GetFileName(item.Files[i]);
            }
            if (item.Files.Length > count)
            {
                text += "\r\n… 共 " + item.Files.Length + " 个文件";
            }
            return text;
        }

        private void DrawScrollBar(Graphics g)
        {
            if (_contentHeight <= Height)
            {
                return;
            }
            int trackHeight = Height - 8;
            int thumbHeight = Math.Max(24,
                (int)((double)Height / _contentHeight * trackHeight));
            int maxScroll = _contentHeight - Height;
            int thumbTop = 4 + (int)(
                (double)_scroll / maxScroll *
                (trackHeight - thumbHeight));
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(70, 76, 86)))
            {
                g.FillRectangle(brush,
                    new Rectangle(Width - 5, thumbTop,
                        3, thumbHeight));
            }
        }
    }
}
