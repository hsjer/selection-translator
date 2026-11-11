using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SelectionTranslator
{
    /// <summary>
    /// 桌面整理面板的外观门面：**整块面板只有一种底色**（标题栏、页签、
    /// 按钮、列表区、状态栏共用同一块磨砂底板），深浅由透明度滑块决定，
    /// 数值越低越透——所以颜色不是写死在每一层上的，改 Base 一行即可换色。
    /// 文字统一白色实色，不随透明度淡化；只有文件图标 / 缩略图保留原色。
    /// 字体、圆角、文字绘制原样转发 DarkUi，字体本身不变。
    /// </summary>
    internal static class DesktopGlass
    {
        /// <summary>唯一的底色：想换颜色只改这一行。</summary>
        private static readonly Color Base = Color.FromArgb(98, 94, 108);

        public static Color Window { get { return Base; } }
        public static Color Surface { get { return Base; } }
        /// <summary>hover / 占位 / 选中都只是这层白色的不同浓度。</summary>
        public static Color Card { get { return Color.FromArgb(26, 255, 255, 255); } }
        public static Color Sunken { get { return Color.FromArgb(32, 255, 255, 255); } }
        public static Color AccentSoft { get { return Color.FromArgb(46, 255, 255, 255); } }
        public static Color BorderSoft { get { return Color.FromArgb(46, 255, 255, 255); } }
        public static Color Accent { get { return Color.White; } }

        // 文字：白色系实色，永远不参与透明度混合。
        public static Color Text { get { return Color.White; } }
        public static Color TextDim { get { return Color.FromArgb(226, 226, 232); } }
        public static Color TextFaint { get { return Color.FromArgb(188, 188, 198); } }

        // 字体、圆角、文字绘制原样转发 DarkUi：字体不变。
        public static Font FontBody { get { return DarkUi.FontBody; } }
        public static Font FontSmall { get { return DarkUi.FontSmall; } }
        public static Font FontTiny { get { return DarkUi.FontTiny; } }
        public static Font FontStrong { get { return DarkUi.FontStrong; } }

        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            return DarkUi.Rounded(rect, radius);
        }

        public static void Text2(
            Graphics g, string text, Font font, Color color,
            Rectangle bounds, TextFormatFlags flags)
        {
            DarkUi.Text2(g, text, font, color, bounds, flags);
        }

        public const TextFormatFlags CenterMiddle = DarkUi.CenterMiddle;
        public const TextFormatFlags LeftMiddle = DarkUi.LeftMiddle;
        public const TextFormatFlags LeftMiddleClip = DarkUi.LeftMiddleClip;
        public const TextFormatFlags RightMiddle = DarkUi.RightMiddle;
    }

    /// <summary>
    /// 桌面整理：把桌面上的杂乱文件列出来按类型收纳到页签（**只分类，
    /// 不移动文件**），支持「图标 / 列表」两种视图；面板可以常驻桌面、
    /// 收起成一条标题栏，配色跟随全局「界面外观」设置。
    ///
    /// 文件图标用系统关联图标，但不再在加载时同步提取——桌面文件一多，
    /// 逐个 ExtractAssociatedIcon 会把 UI 线程卡住几秒。现在先出列表，
    /// 图标在后台线程按扩展名缓存着慢慢补，界面上是渐进填充的。
    /// </summary>
    internal sealed class DesktopManagerForm : Form
    {
        /// <summary>两种文件列表展示方式。</summary>
        internal enum ViewModeKind
        {
            List,
            Icon
        }

        private sealed class Item
        {
            public string Name;
            public string FullPath;
            public bool IsDirectory;
            public long Size;
            public DateTime Modified;
            public string Category;
            /// <summary>所在分类文件夹名；空 = 桌面顶层散落。</summary>
            public string Container;
            /// <summary>图片文件：图标槽位里放的是缩略图而不是 shell 图标。</summary>
            public bool IsImage;
            /// <summary>Icon 里确实是一张缩略图（而不是退回的类型图标）。</summary>
            public bool Thumbnail;
            public Bitmap Icon;
            public bool IconOwned;
        }

        // 图标缓存：按扩展名共用——桌面上几十个 .txt / .pdf 只需要取一次
        // 图标。只有 .exe / .lnk / .ico / .msi 这种「同一个扩展名图标也各不
        // 相同」的才按完整路径缓存。缓存进程内一直保留，条目只拿到克隆，
        // 关闭窗口时释放克隆即可。
        private static readonly Dictionary<string, Bitmap> s_iconCache =
            new Dictionary<string, Bitmap>();
        private static readonly object s_iconLock = new object();

        // 分类扩展名表：按扩展名把桌面文件归到对应页签（只分类，不移动）。
        private static readonly string[] DocExts =
        {
            ".txt", ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt",
            ".pptx", ".md", ".rtf", ".csv", ".odt", ".ods", ".odp",
            ".wps", ".html", ".htm", ".xml", ".json", ".log", ".tex"
        };
        private static readonly string[] ImageExts =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tiff",
            ".tif", ".svg", ".ico", ".psd", ".heic"
        };
        private static readonly string[] VideoExts =
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm",
            ".m4v", ".mpg", ".mpeg", ".3gp"
        };
        private static readonly string[] CompressExts =
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz",
            ".tgz", ".iso"
        };

        private const int TitleH = 44;
        /// <summary>默认形状：短横条 210、竖长面板 400×620。用户拖边框
        /// 改过之后记进配置（见下面三个实例字段）。</summary>
        private const int DefaultCollapsedW = 210;
        private const int DefaultExpandedW = 400;
        private const int DefaultExpandedH = 620;
        private const int MinCollapsedW = 150;
        private const int MinExpandedW = 300;
        private const int MinExpandedH = 220;
        private const int MaxPanelW = 1600;
        private const int MaxPanelH = 1600;
        /// <summary>鼠标落在窗口边缘多少像素内算「拖边框」。</summary>
        private const int ResizeGrip = 6;
        /// <summary>窄到这个宽度以下，页签改成均分，六个分类才不会溢出。</summary>
        private const int CompactTabBelow = 460;
        private const int TabBarH = 38;
        private const int PanelH = 58;
        private const int StatusH = 24;
        private const int ScrollW = 12;
        private const int ListRowH = 30;
        private const int IconCellW = 96;
        // 图标格曾经是 92px 高：32px 图标下面还压着两行文件名，一屏只能
        // 放四行，看着很空。现在 80：40px 图标 + 两行文件名刚好放下，
        // 一屏能多放一行，信息密度反而更高。
        private const int IconCellH = 80;
        private const int IconPx = 40;
        private const int GridIconY = 6;
        private const int GridLabelY = 48;
        // 11.5px 字号一行约 15px，两行要 30——早先给 24 会把第二行切掉。
        private const int GridLabelH = 30;
        private const int Pad = 10;

        /// <summary>缩略图解码尺寸，比列表里画的大一点，缩放不会糊。</summary>
        private const int ThumbPx = 48;

        /// <summary>
        /// 面板顶部的分类页签。分类完全由扩展名决定，文件在桌面上保持
        /// 原位不动，页签只是同一批文件的分类视图。
        /// 第一格不再是「全部」聚合页签——它和后面几类的内容完全重叠，
        /// 白占一格；现在打开面板直接落在第一个有内容的分类上。
        /// 「目录」= 文件夹。
        /// </summary>
        internal static readonly string[] Categories =
        {
            "文档", "图片", "视频", "压缩", "其它", "目录"
        };

        /// <summary>
        /// 交叉页签用的聚合名。界面上已经没有这一格了，但内部仍用它表示
        /// 「不限分类」，配置里存过旧值时不至于打不开面板。
        /// </summary>
        internal const string AllCategory = "全部";

        /// <summary>
        /// 面板里不显示的桌面文件夹（完整路径，; 分隔），来自配置
        /// <see cref="AppConfig.DesktopHiddenFolders"/>。按路径而不是按名字，
        /// 同名但不同位置的文件夹照常显示。
        /// </summary>
        private string _hiddenFolders = "";

        private static DesktopManagerForm _instance;

        /// <summary>
        /// 常驻 / 锁定开关发生变化（面板自己的右键菜单也能改）。托盘那边
        /// 缓存着一份 AppConfig，不同步的话它下次保存配置会把面板刚改的
        /// 值写回去，就会出现「明明取消了常驻，重启又冒出来」。
        /// </summary>
        internal static event Action StateChanged;

        private static void RaiseStateChanged()
        {
            Action handler = StateChanged;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Log.Error("同步桌面助手状态失败", ex);
            }
        }

        private string _root;
        private List<Item> _allItems = new List<Item>();
        private List<Item> _items = new List<Item>();
        private string _activeCategory = Categories[0];
        private string _hoverCategory;
        private ViewModeKind _view = ViewModeKind.List;
        private int _selectedIndex = -1;
        private bool _opacityPanelVisible;

        // ---- 底板半透明 ----
        // 面板底板可以透出桌面，但文件名、图标、文字必须是实心的。WinForms
        // 的 Form.Opacity 是整窗一起透明，所以改成：抓一块桌面背景，按透明
        // 比例和面板底色混成 _backdrop 当底板，内容再画在它上面（不透明）。
        private Bitmap _backdrop;
        /// <summary>底板不透明度：1 = 完全不透明，0.3 = 很透。</summary>
        private double _bgAlpha = 0.95;
        /// <summary>桌面抓不到（远端会话 / 驱动不给）时退回纯色，不再重试。</summary>
        private bool _backdropUnavailable;
        private bool _backdropReady;
        /// <summary>当前底板对应的是屏幕上的哪个位置（拖动时用来算平移）。</summary>
        private Point _backdropOrigin;

        private readonly DoubleBufferedPanel _list;
        private readonly VScrollBar _scroll;
        private readonly GlyphButton _viewButton;
        private readonly FlatButton _organizeButton;
        private readonly FlatButton _opacityButton;
        private readonly GlyphButton _collapseButton;
        private readonly Panel _opacityPanel;
        private readonly TrackBar _opacityTrack;
        private readonly Label _opacityValue;
        private readonly Label _statusLabel;

        // 收起 / 常驻：面板可以缩成一条只留标题栏的横条钉在桌面上
        // （像桌面小部件），点箭头或双击标题栏再展开。
        private bool _collapsed;
        private bool _resident;
        /// <summary>锁定位置：钉死在桌面上，拖动标题栏不再挪动。</summary>
        private bool _locked;
        /// <summary>常驻时盯着前台窗口：桌面在前台就把面板浮上来，否则压底。</summary>
        private readonly Timer _zOrderTimer;
        /// <summary>收起状态下盯着鼠标：停在短条上约 180ms 就自动展开。</summary>
        private readonly Timer _hoverTimer;
        private int _hoverElapsed;
        /// <summary>刚被手动收起、鼠标还压在条上：等它离开才允许再次展开。</summary>
        private bool _hoverBlocked;
        /// <summary>自检 / 实拍探针模式：不自动展开，截图状态才稳。</summary>
        private bool _probeMode;
        private bool _zAtFront;
        /// <summary>用户刚主动从托盘打开面板：先浮起来给他看见。</summary>
        private bool _raiseForUser;
        /// <summary>浮起后鼠标有没有进过面板（进过就等它离开再回落）。</summary>
        private bool _raiseSeenMouse;
        /// <summary>浮起的宽限时间：到点还没人来操作就回桌面层。</summary>
        private DateTime _raiseDeadline;
        private int _collapsedWidth = DefaultCollapsedW;
        private int _expandedWidth = DefaultExpandedW;
        private int _expandedHeight = DefaultExpandedH;
        /// <summary>标题文字可用的右边界（由 LayoutChildren 按最左按钮算出）。</summary>
        private int _titleRight = 200;
        /// <summary>拖边框期间：不重抓底板，松手再补抓。</summary>
        private bool _sizing;
        private bool _resizing;
        private int _resizeHit;
        private Point _resizeStartMouse;
        private Rectangle _resizeStartBounds;
        // 应用配置 / 初始化期间不要反复写盘。
        private bool _restoringState;

        private bool _dragging;
        private Point _dragStart;
        private Point _dragOrigin;
        // 勾选类菜单项点完后保持菜单展开（见 BuildPanelMenu 的 Closing）。
        private bool _keepMenuOpen;

        // 图标异步加载的代次：每次 Reload / 关闭窗口 +1。后台任务发现代次
        // 变了就立刻收手，避免往已经释放掉的列表里塞位图，也避免旧任务的
        // 图标盖到新列表上。
        private volatile int _iconGeneration;

        public DesktopManagerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            BackColor = DesktopGlass.Window;
            ForeColor = DesktopGlass.Text;
            ClientSize = new Size(_expandedWidth, _expandedHeight);
            Font = DesktopGlass.FontBody;
            KeyPreview = true;
            Text = "桌面整理";

            _viewButton = new GlyphButton(
                GlyphKind.Grid, DesktopGlass.Window, 38, 30);
            _viewButton.TabStop = false;
            _viewButton.Click += delegate { ToggleView(); };
            _viewButton.MouseEnter += (s, e) => UpdateViewTooltip();
            Controls.Add(_viewButton);

            _organizeButton = new FlatButton();
            _organizeButton.Text = "重新整理";
            _organizeButton.Size = new Size(82, 30);
            _organizeButton.SurfaceColor = DesktopGlass.Window;
            _organizeButton.Radius = 7;
            // 不设 Primary：蓝色实心块跟旁边几个白底按钮摆一起太乱，
            // 标题栏按钮统一融进磨砂背景（见 SetupGlassButtons）。
            _organizeButton.Click += delegate { Organize(null); };
            Controls.Add(_organizeButton);

            _opacityButton = new FlatButton();
            _opacityButton.Text = "透明度";
            _opacityButton.Size = new Size(68, 30);
            _opacityButton.SurfaceColor = DesktopGlass.Window;
            _opacityButton.Radius = 7;
            _opacityButton.Click += delegate
            {
                _opacityPanelVisible = !_opacityPanelVisible;
                LayoutChildren();
                Invalidate(new Rectangle(0, TitleH + TabBarH, Width, 1));
            };
            Controls.Add(_opacityButton);

            _collapseButton = new GlyphButton(
                GlyphKind.ChevronUp, DesktopGlass.Window, 34, 30);
            _collapseButton.TabStop = false;
            _collapseButton.Click += delegate { ToggleCollapsed(); };
            Controls.Add(_collapseButton);

            // 四个按钮共用窗体的磨砂底板，颜色跟主题 / 透明度一起变。
            SetupGlassButtons();

            // 透明度面板：标题栏下方下拉出来的滑块条。
            _opacityPanel = new DoubleBufferedPanel();
            _opacityPanel.BackColor = DesktopGlass.Surface;
            _opacityPanel.Visible = false;
            _opacityPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                PaintGlassChild(e.Graphics, _opacityPanel);
            };
            Controls.Add(_opacityPanel);

            Label opacityLabel = new DoubleBufferedLabel();
            opacityLabel.Text = "窗口透明度";
            opacityLabel.ForeColor = DesktopGlass.TextDim;
            opacityLabel.Font = DesktopGlass.FontSmall;
            opacityLabel.AutoSize = true;
            opacityLabel.Location = new Point(14, 19);
            _opacityPanel.Controls.Add(opacityLabel);

            _opacityTrack = new TrackBar();
            _opacityTrack.Minimum = 30;
            _opacityTrack.Maximum = 100;
            _opacityTrack.TickFrequency = 5;
            _opacityTrack.Value = 95;
            _opacityTrack.BackColor = DesktopGlass.Surface;
            _opacityTrack.Size = new Size(360, 30);
            _opacityTrack.Location = new Point(108, 14);
            _opacityTrack.Scroll += delegate
            {
                ApplyOpacity(_opacityTrack.Value / 100.0);
            };
            _opacityTrack.MouseUp += delegate { PersistOpacity(); };
            _opacityTrack.KeyUp += delegate { PersistOpacity(); };
            _opacityPanel.Controls.Add(_opacityTrack);

            _opacityValue = new DoubleBufferedLabel();
            _opacityValue.ForeColor = DesktopGlass.Text;
            _opacityValue.Font = DesktopGlass.FontStrong;
            _opacityValue.AutoSize = true;
            _opacityValue.Location = new Point(484, 19);
            _opacityPanel.Controls.Add(_opacityValue);

            _zOrderTimer = new Timer();
            _zOrderTimer.Interval = 400;
            _zOrderTimer.Tick += OnZOrderTick;

            _hoverTimer = new Timer();
            _hoverTimer.Interval = 60;
            _hoverTimer.Tick += OnHoverTick;

            _list = new DoubleBufferedPanel();
            _list.BackColor = DesktopGlass.Window;
            _list.Paint += OnListPaint;
            _list.MouseDown += OnListMouseDown;
            _list.MouseDoubleClick += OnListDoubleClick;
            _list.MouseWheel += (s, e) =>
            {
                SetScroll(_scroll.Value - e.Delta / 3);
            };
            Controls.Add(_list);

            _scroll = new VScrollBar();
            _scroll.Visible = false;
            _scroll.ValueChanged += delegate
            {
                _list.Invalidate();
            };
            Controls.Add(_scroll);

            _statusLabel = new DoubleBufferedLabel();
            _statusLabel.BackColor = DesktopGlass.Surface;
            _statusLabel.ForeColor = DesktopGlass.TextFaint;
            _statusLabel.Font = DesktopGlass.FontTiny;
            _statusLabel.Text = "桌面整理 · 按类型分页签，右上角「整理」重扫（不移动文件）";
            _statusLabel.Location = new Point(12, 4);
            _statusLabel.Paint += delegate(object s, PaintEventArgs e)
            {
                PaintGlassChild(e.Graphics, _statusLabel);
            };
            Controls.Add(_statusLabel);

            ContextMenuStrip itemMenu = new ContextMenuStrip();
            itemMenu.ShowImageMargin = false;
            ToolStripMenuItem open = new ToolStripMenuItem("打开");
            open.Click += delegate { OpenSelected(); };
            ToolStripMenuItem locate = new ToolStripMenuItem("打开所在文件夹");
            locate.Click += delegate { OpenLocation(); };
            ToolStripMenuItem refresh = new ToolStripMenuItem("刷新");
            refresh.Click += delegate { Reload(); SetStatus("已刷新"); };
            itemMenu.Items.Add(open);
            itemMenu.Items.Add(locate);
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add(refresh);
            itemMenu.Opening += delegate
            {
                bool has = _selectedIndex >= 0 &&
                    _selectedIndex < _items.Count;
                open.Enabled = has;
                locate.Enabled = has;
            };
            _list.ContextMenuStrip = itemMenu;

            try
            {
                AppConfig config = ConfigStore.Load();
                double op = config.DesktopManagerOpacity;
                if (op <= 0.0 || op > 1.0)
                {
                    op = 0.95;
                }
                // 注意：这里只记「底板」的透明比例，窗体本身恒不透明，
                // 否则文件名会跟着一起糊掉。
                _bgAlpha = op;
                _opacityTrack.Value = (int)Math.Round(op * 100.0);
                _hiddenFolders = config.DesktopHiddenFolders ?? "";
            }
            catch (Exception ex)
            {
                Log.Error("读取桌面管理透明度失败", ex);
                _bgAlpha = 0.95;
                _opacityTrack.Value = 95;
                _hiddenFolders = "";
            }
            UpdateOpacityValue();
            // 读回用户上次存的透明度后，按钮 hover 浓度也同步一次。
            UpdateGlassOverlay();

            _root = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            BuildPanelMenu();
            UpdateViewTooltip();
            // 列表 / 滚动条 / 滑块面板铺满了左边框那一列，鼠标事件会先被
            // 子控件吃掉；把边缘 6px 的按下 / 移动转发回窗体的拉伸逻辑。
            AttachResizeForward(_list);
            AttachResizeForward(_scroll);
            AttachResizeForward(_opacityPanel);
            AttachResizeForward(_statusLabel);
            LayoutChildren();
            Reload();
            RestorePanelState();

            // 展开状态（没走过 SetCollapsed）也要把拉伸范围设好。
            ApplySizeLimits();
            // 位置 / 尺寸都定下来了，这时候抓的桌面背景才是对的。
            RebuildBackdrop();
            _backdropReady = true;
            Invalidate(true);

            // 面板一存在就一直跟着桌面走：桌面在前台就浮上来，否则压底。
            _zOrderTimer.Start();
            // 收起时长条自己盯着鼠标，hover 展开不用抢焦点、也不用激活窗口。
            _hoverTimer.Start();
        }

        // ---- 标题栏玻璃按钮 ----

        /// <summary>
        /// 四个按钮不再各自铺白底 / 蓝底：背景直接贴窗体那块磨砂底板，
        /// 跟整块面板同一个色、跟透明度一起变；文字 / 图标是白色实色，
        /// 永远不参与透明混合。字体本身仍用 DarkUi 那一套，没动过。
        /// </summary>
        private void SetupGlassButtons()
        {
            Action<Graphics, Control> backdrop =
                delegate(Graphics g, Control c) { PaintGlassChild(g, c); };

            _viewButton.BackColor = DesktopGlass.Window;
            _organizeButton.BackColor = DesktopGlass.Window;
            _opacityButton.BackColor = DesktopGlass.Window;
            _collapseButton.BackColor = DesktopGlass.Window;

            _viewButton.GlassBackdrop = backdrop;
            _organizeButton.GlassBackdrop = backdrop;
            _opacityButton.GlassBackdrop = backdrop;
            _collapseButton.GlassBackdrop = backdrop;

            Color white = DesktopGlass.Text;
            _viewButton.GlassInk = white;
            _collapseButton.GlassInk = white;
            _organizeButton.GlassInk = white;
            _opacityButton.GlassInk = white;

            UpdateGlassOverlay();
        }

        /// <summary>
        /// hover / 按下只叠一层白色，浓度跟透明度联动；不用 CardHover /
        /// Accent 那种独立色块，按钮才不会又跟面板分成两种颜色。
        /// </summary>
        private void UpdateGlassOverlay()
        {
            Color overlay = Color.FromArgb(
                (int)Math.Round(_bgAlpha * 34.0), 255, 255, 255);
            _viewButton.GlassOverlay = overlay;
            _organizeButton.GlassOverlay = overlay;
            _opacityButton.GlassOverlay = overlay;
            _collapseButton.GlassOverlay = overlay;
        }

        // ---- 标题栏右键菜单 ----

        private void BuildPanelMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            ToolStripMenuItem lockItem =
                new ToolStripMenuItem("锁定位置（锁定后拖不动）");
            lockItem.CheckOnClick = true;
            // 勾选类菜单点完别收起：用户常要连着改两项，每次都得重新右键
            // 太折腾。意图必须在 MouseDown 记（菜单先 Close 再触发 Click）。
            lockItem.MouseDown += delegate { _keepMenuOpen = true; };
            lockItem.Click += delegate { SetLocked(lockItem.Checked); };
            menu.Items.Add(lockItem);

            ToolStripMenuItem collapse =
                new ToolStripMenuItem("收起 / 展开");
            collapse.Click += delegate { ToggleCollapsed(); };
            menu.Items.Add(collapse);

            ToolStripMenuItem refresh = new ToolStripMenuItem("刷新列表");
            refresh.Click += delegate { Reload(); SetStatus("已刷新"); };
            menu.Items.Add(refresh);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem appearance =
                new ToolStripMenuItem("界面颜色…");
            appearance.Click += delegate { AppearanceForm.ShowWindow(); };
            menu.Items.Add(appearance);

            menu.Items.Add(new ToolStripSeparator());

            // 「关闭」= 关掉整个桌面助手功能（托盘里的勾也会一起清掉），
            // 下次开机不再自动出现；想在托盘里重新勾选就能回来。
            ToolStripMenuItem close =
                new ToolStripMenuItem("关闭面板（停止常驻）");
            close.Click += delegate { ApplyResidentSetting(false); };
            menu.Items.Add(close);

            menu.Opening += delegate
            {
                lockItem.Checked = _locked;
                collapse.Text = _collapsed ? "展开面板" : "收起面板";
            };
            // 勾选类菜单项点完保持展开，和托盘菜单一致。
            menu.Closing += delegate(object s, ToolStripDropDownClosingEventArgs e)
            {
                if (_keepMenuOpen &&
                    e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                {
                    _keepMenuOpen = false;
                    e.Cancel = true;
                    return;
                }
                _keepMenuOpen = false;
            };
            ContextMenuStrip = menu;
        }

        // ---- 对外 / 自检接口 ----

        public static DesktopManagerForm ShowWindow()
        {
            if (_instance != null && !_instance.IsDisposed)
            {
                if (_instance.WindowState == FormWindowState.Minimized)
                {
                    _instance.WindowState = FormWindowState.Normal;
                }
                if (!_instance.Visible)
                {
                    _instance.Show();
                }
                // 每次唤起都重扫一遍：桌面增减文件很频繁，列表不该停留在
                // 上次打开时的样子。扫描本身只读目录、不取图标，159 个
                // 条目实测十几毫秒，图标随后在后台补。
                _instance.Reload();
                // 用户主动打开：先浮到最前让他看见；鼠标一离开面板就
                // 自动回到桌面层，绝不一直挡着别的程序。
                _instance.RaiseForUser();
                return _instance;
            }
            DesktopManagerForm form = new DesktopManagerForm();
            _instance = form;
            form.FormClosed += delegate { _instance = null; };
            form.Show();
            form.RaiseForUser();
            return form;
        }

        /// <summary>
        /// 开机把面板放出来（常驻桌面）。不抢焦点：常驻面板是桌面上的
        /// 一块小部件，用户正在干别的事时不该被它弹到前台。
        /// </summary>
        internal static DesktopManagerForm ShowResidentPanel()
        {
            ApplyResidentSetting(true);
            return _instance;
        }

        /// <summary>
        /// 供菜单 / 外观窗口统一调用的「常驻桌面」开关。
        /// </summary>
        internal static void ApplyResidentSetting(bool resident)
        {
            if (resident)
            {
                DesktopManagerForm form = _instance;
                if (form == null || form.IsDisposed)
                {
                    form = new DesktopManagerForm();
                    _instance = form;
                    form.FormClosed += delegate { _instance = null; };
                    // 先标记常驻再 Show：ShowWithoutActivation 依赖这个
                    // 标记，开机时才不会把焦点从用户正在用的窗口抢走。
                    form.SetResident(true);
                    form.Show();
                    form.Reload();
                }
                else
                {
                    if (!form.Visible)
                    {
                        form.Show();
                    }
                    form.SetResident(true);
                }
                return;
            }
            if (_instance != null && !_instance.IsDisposed)
            {
                // 关掉这个开关就是「不启动这个功能」：面板整个销毁，不是
                // 藏起来。先清掉常驻标记，否则 OnFormClosing 会把它当成
                // 用户点关闭而只是 Hide。
                _instance._resident = false;
                _instance.Close();
                return;
            }
            SaveResidentFlag(false);
        }

        /// <summary>
        /// 供托盘菜单 / 面板菜单统一调用的「锁定位置」开关。面板没开时
        /// 只写配置，下次面板出现时按这个值生效。
        /// </summary>
        internal static void ApplyLockSetting(bool locked)
        {
            DesktopManagerForm form = _instance;
            if (form != null && !form.IsDisposed)
            {
                form.SetLocked(locked);
                return;
            }
            try
            {
                AppConfig config = ConfigStore.Load();
                config.DesktopPanelLocked = locked;
                ConfigStore.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error("保存桌面助手锁定设置失败", ex);
            }
        }

        // ---- 收起 / 展开 / 常驻 ----

        internal bool Collapsed
        {
            get { return _collapsed; }
        }

        /// <summary>自检用：面板左上角在屏幕上的位置（嵌入桌面后 Left/Top 是相对宿主的）。</summary>
        internal Point ScreenLocation
        {
            get { return ScreenOrigin; }
        }

        internal bool Resident
        {
            get { return _resident; }
        }

        /// <summary>
        /// 当前「常驻桌面」的开关状态。面板没开时以配置为准，托盘菜单
        /// 勾选状态就永远和真实状态一致（外观窗口也能改这个开关）。
        /// </summary>
        internal static bool ResidentEnabled
        {
            get
            {
                if (_instance != null && !_instance.IsDisposed)
                {
                    return _instance._resident;
                }
                try
                {
                    return ConfigStore.Load().DesktopPanelResident;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal void ToggleCollapsed()
        {
            SetCollapsed(!_collapsed);
        }

        internal void SetCollapsed(bool value)
        {
            if (_collapsed == value)
            {
                LayoutChildren();
                return;
            }
            if (value)
            {
                // 记住展开时的高度，展开还原回去。
                if (Height > TitleH + 80)
                {
                    _expandedHeight = Height;
                }
            }
            _collapsed = value;
            ApplySizeLimits();
            _hoverElapsed = 0;
            // 重新展开时把「离开计时」清零，别把上一轮残留的 300 多 ms
            // 带进新一轮，展开瞬间就收回去。
            _leaveElapsed = 0;
            // 点箭头 / 双击收起时鼠标多半还在条上：先压住自动展开，
            // 等鼠标真的离开过一次再放行，否则会「收起瞬间又弹开」。
            _hoverBlocked = value && IsPointerOverForHover();
            _collapseButton.Kind = value
                ? GlyphKind.ChevronDown
                : GlyphKind.ChevronUp;
            _collapseButton.Invalidate();

            bool showBody = !value;
            _viewButton.Visible = showBody;
            _organizeButton.Visible = showBody;
            _opacityButton.Visible = showBody;
            _list.Visible = showBody;
            _statusLabel.Visible = showBody;
            if (!showBody)
            {
                _opacityPanel.Visible = false;
                _scroll.Visible = false;
            }

            int targetW = value ? _collapsedWidth : _expandedWidth;
            int targetH = value
                ? TitleH
                : Math.Max(TitleH + 160, _expandedHeight);
            if (_restoringState)
            {
                // 恢复配置时位置已经按「展开左上角」摆好，这里只改尺寸。
                Size = new Size(targetW, targetH);
            }
            else
            {
                ApplyAnchoredSize(targetW, targetH);
            }
            ApplyRoundedRegion();
            LayoutChildren();
            Invalidate(true);
            SavePanelState();
            if (value)
            {
                SetStatus("已收起，鼠标移上来自动展开");
            }
            else
            {
                // 展开后底部状态回到「共 N 项」的常规文案，别留着
                // 「已收起」的旧话。
                UpdateStatus();
            }
        }

        /// <summary>
        /// 收起 / 展开会改宽度：右边缘保持不动（贴在屏幕右侧时向左长，
        /// 不会顶出屏幕），必要时上移一点保证展开后整块都看得见。
        /// 配置里存的永远是「展开状态左上角」，两种状态来回切不会漂移。
        /// </summary>
        private void ApplyAnchoredSize(int w, int h)
        {
            Point origin = ScreenOrigin;
            int left = origin.X + Size.Width - w;
            int top = origin.Y;
            try
            {
                Rectangle wa = Screen.FromPoint(origin).WorkingArea;
                left = Math.Max(wa.Left, Math.Min(left, wa.Right - w));
                top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - h));
            }
            catch
            {
            }
            Location = new Point(
                Location.X + left - origin.X,
                Location.Y + top - origin.Y);
            Size = new Size(w, h);
        }

        // ---- 悬停自动展开 ----

        private const int HoverExpandDelayMs = 180;

        /// <summary>
        /// 展开状态下鼠标离开面板这么久就自动收起成短条。比悬停展开的
        /// 180ms 长一档：离开多半是要去干别的活，太快收会显得慌张。
        /// </summary>
        private const int AutoCollapseDelayMs = 400;

        /// <summary>展开态鼠标离开面板的累计时长，够 AutoCollapseDelayMs 收起。</summary>
        private int _leaveElapsed;

        /// <summary>
        /// 悬停 / 离开共用一个 60ms 计时器。收起态：鼠标停在短条上 180ms
        /// 就展开。不用 MouseEnter：常驻面板点了也不激活（MA_NOACTIVATE），
        /// 而且被别的窗口盖住时坐标虽然落在面板上，鼠标其实在别的窗口里；
        /// 所以每 60ms 直接问系统「光标下面到底是哪个窗口」。展开态反过来：
        /// 鼠标离开面板 400ms 自动收起，让位给正在干活的窗口。
        /// </summary>
        private void OnHoverTick(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated || !Visible ||
                _probeMode || _resizing || _dragging)
            {
                _hoverElapsed = 0;
                _leaveElapsed = 0;
                return;
            }
            bool over = IsPointerOverForHover();
            if (_collapsed)
            {
                if (!over)
                {
                    _hoverElapsed = 0;
                    // 鼠标离开过一次，解除「刚手动收起」的压制。
                    _hoverBlocked = false;
                    return;
                }
                if (_hoverBlocked)
                {
                    return;
                }
                _hoverElapsed += _hoverTimer.Interval;
                if (_hoverElapsed < HoverExpandDelayMs)
                {
                    return;
                }
                _hoverElapsed = 0;
                SetCollapsed(false);
                _leaveElapsed = 0;
                return;
            }
            // 展开态：右键菜单 / 列表项菜单开着时鼠标在菜单上（菜单是
            // 独立窗口，面板判定为「离开」），别把面板收了。
            if (over || IsAnyMenuOpen())
            {
                _leaveElapsed = 0;
                return;
            }
            _leaveElapsed += _hoverTimer.Interval;
            if (_leaveElapsed < AutoCollapseDelayMs)
            {
                return;
            }
            _leaveElapsed = 0;
            SetCollapsed(true);
        }

        /// <summary>面板自己的右键菜单或列表项右键菜单正开着。</summary>
        private bool IsAnyMenuOpen()
        {
            return (ContextMenuStrip != null && ContextMenuStrip.Visible) ||
                (_list != null && _list.ContextMenuStrip != null &&
                    _list.ContextMenuStrip.Visible);
        }

        // 自检用：把「鼠标在不在条上」替换成确定值，不依赖真实光标位置。
        private bool? _pointerOverrideForTest;

        internal void OverridePointerForTest(bool over)
        {
            _pointerOverrideForTest = over;
        }

        internal void HoverTickForTest()
        {
            OnHoverTick(this, EventArgs.Empty);
        }

        /// <summary>自检用：停掉自动计时，改由 HoverTickForTest 手动驱动。</summary>
        internal void SuspendHoverExpandForTest()
        {
            _hoverTimer.Stop();
        }

        // ---- 自检用：边框拉伸 ----
        internal int ResizeHitForTest(int x, int y)
        {
            return ResizeHitAtClient(new Point(x, y));
        }

        /// <summary>自检用：屏幕坐标落在哪条可拉伸边框上。</summary>
        internal int ResizeHitAtScreenForTest(Point screenPoint)
        {
            return ResizeHitAtClient(PointToClient(screenPoint));
        }

        internal bool IsResizingForTest
        {
            get { return _resizing; }
        }

        /// <summary>自检用：覆盖屏幕坐标点的子控件（诊断消息被谁吃掉）。</summary>
        internal string ChildAtScreenForTest(Point screenPoint)
        {
            Control c = GetChildAtPoint(PointToClient(screenPoint));
            return c == null ? "none" : c.GetType().Name + c.Bounds;
        }

        // 诊断计数：子控件转发的鼠标事件到底有没有到。
        internal static int ForwardDownCountForTest;
        internal static int ForwardHitCountForTest;
        internal static int ForwardMoveCountForTest;
        internal static int ForwardUpCountForTest;

        internal bool IsHoverAreaForTest(int x, int y)
        {
            Point p = new Point(x, y);
            return new Rectangle(Point.Empty, ClientSize).Contains(p) &&
                ResizeHitAtClient(p) == 0;
        }

        internal void EndResizeForTest()
        {
            PersistResizedSize();
        }

        /// <summary>
        /// 自检用：形状回到出厂默认。真实配置里可能残留探针 / 用户拖过
        /// 的尺寸，几何断言要一个确定的起点。
        /// </summary>
        internal void ResetSizesForTest()
        {
            _expandedWidth = DefaultExpandedW;
            _expandedHeight = DefaultExpandedH;
            _collapsedWidth = DefaultCollapsedW;
            Size = _collapsed
                ? new Size(_collapsedWidth, TitleH)
                : new Size(_expandedWidth, _expandedHeight);
            LayoutChildren();
        }

        /// <summary>自检用：按指定边框和位移走一遍真实的拉伸几何。</summary>
        internal void SimulateResizeDragForTest(int hit, int dx, int dy)
        {
            _resizeHit = hit;
            _resizeStartMouse = Point.Empty;
            _resizeStartBounds = new Rectangle(ScreenOrigin, Size);
            _sizing = true;
            try
            {
                UpdateResize(new Point(dx, dy));
            }
            finally
            {
                _sizing = false;
                if (_backdropReady)
                {
                    RebuildBackdrop();
                    Invalidate(true);
                }
            }
        }

        internal int ExpandedWidthForTest
        {
            get { return _expandedWidth; }
        }

        internal int ExpandedHeightForTest
        {
            get { return _expandedHeight; }
        }

        internal int CollapsedWidthForTest
        {
            get { return _collapsedWidth; }
        }

        internal int ListHeightForTest
        {
            get { return _list == null ? 0 : _list.Height; }
        }

        internal static int ClampSizeForTest(
            int value, int min, int max, int fallback)
        {
            return ClampSize(value, min, max, fallback);
        }

        private bool IsPointerOverForHover()
        {
            if (_pointerOverrideForTest.HasValue)
            {
                return _pointerOverrideForTest.Value;
            }
            try
            {
                return PointerOverPanel();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>光标是否真的落在本面板上（而不是压在它上面的窗口）。</summary>
        private bool PointerOverPanel()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return false;
            }
            NativeMethods.POINT pt;
            if (!NativeMethods.GetCursorPos(out pt))
            {
                return false;
            }
            Rectangle bounds = new Rectangle(ScreenOrigin, Size);
            if (!bounds.Contains(pt.X, pt.Y))
            {
                return false;
            }
            // 边框是「拉伸」区域，不算悬停展开区域：否则鼠标停在短条
            // 右边框上想拖长度，180ms 后面板先自己展开了。
            try
            {
                if (ResizeHitAtClient(
                    PointToClient(new Point(pt.X, pt.Y))) != 0)
                {
                    return false;
                }
            }
            catch
            {
            }
            IntPtr hit = NativeWindowFromPoint(pt);
            if (hit == IntPtr.Zero)
            {
                return false;
            }
            IntPtr root = NativeGetAncestor(hit, GaRoot);
            if (root == IntPtr.Zero)
            {
                root = hit;
            }
            return root == Handle;
        }

        internal void SetResident(bool value)
        {
            _resident = value;
            ShowInTaskbar = !value;
            TopMost = false;
            ApplyToolWindow(value);
            SendToDesktopLayer();
            Invalidate();
            SavePanelState();
        }

        // ---- 常驻 = 贴在桌面这一层 ----
        //
        // 试过把窗口 SetParent 进桌面的 Progman / WorkerW 做真正的子窗口，
        // 结论是行不通：窗口的 z-order、位置、IsWindowVisible 全都对，但
        // 屏幕上就是一个像素都画不出来（桌面的合成不吃这种跨进程子窗口）。
        // 所以改成：保持普通顶层窗口，但让它一直待在 z-order 最底、
        // 点击也不激活（WM_MOUSEACTIVATE 返回 MA_NOACTIVATE）。效果是
        // 它永远被应用窗口盖着、点了也不会跳到别人上面去，Win+D 之后
        // 跟桌面一起露出来；关闭任何应用窗口都带不走它。

        /// <summary>
        /// 常驻面板的「跟着桌面」逻辑：前台是桌面（用户按了 Win+D、或者
        /// 关掉了最后一个窗口）时把面板浮到最前，别的时候压到所有应用
        /// 窗口下面。这样它平时不挡人，回桌面又一定看得见。
        /// </summary>
        private void OnZOrderTick(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated || !Visible)
            {
                return;
            }
            try
            {
                IntPtr foreground = NativeGetForegroundWindow();
                bool onDesktop = IsDesktopForeground(foreground);
                if (onDesktop)
                {
                    if (WindowState == FormWindowState.Minimized)
                    {
                        // Win+D 会把面板一起最小化，回来得以原样出现。
                        WindowState = FormWindowState.Normal;
                        _zAtFront = false;
                    }
                    // 不要只信自己记的 _zAtFront：Win+D 之类的系统操作会把
                    // topmost 位清掉，标志和真实状态会对不上。以实际样式为准。
                    if (!IsTopMostNow())
                    {
                        // 必须是真的 topmost：显示桌面时 Progman 会被提到
                        // 所有普通窗口之上，HWND_TOP 顶不过它，面板还是被
                        // 桌面盖住（实测读像素只有壁纸）。
                        NativeSetWindowPos(Handle, HwndTopMost,
                            0, 0, 0, 0,
                            SwpNoMove | SwpNoSize | SwpNoActivate);
                        _zAtFront = true;
                    }
                }
                else if (_raiseForUser)
                {
                    // 刚从托盘打开：鼠标进过面板就一直陪着，移开才回落；
                    // 一直没人理就到点自动回落（不能永远挡着别人）。
                    Rectangle bounds = new Rectangle(ScreenOrigin, Size);
                    if (bounds.Contains(Cursor.Position))
                    {
                        _raiseSeenMouse = true;
                    }
                    else if (_raiseSeenMouse ||
                        DateTime.Now > _raiseDeadline)
                    {
                        DropToDesktopLayer();
                    }
                }
                else if (IsTopMostNow())
                {
                    DropToDesktopLayer();
                }
            }
            catch (Exception ex)
            {
                Log.Error("调整面板前后顺序失败", ex);
            }
        }

        /// <summary>窗口当前的 WS_EX_TOPMOST 位（不看自己记的标志）。</summary>
        private bool IsTopMostNow()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return false;
            }
            int style = GetWindowLong(Handle, GwlexStyle);
            return (style & WsExTopMost) != 0;
        }

        /// <summary>从置顶状态回到桌面层（先摘 topmost 再压底）。</summary>
        private void DropToDesktopLayer()
        {
            _raiseForUser = false;
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }
            NativeSetWindowPos(Handle, HwndNoTopMost,
                0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
            SendToDesktopLayer();
            _zAtFront = false;
        }

        /// <summary>
        /// 用户主动从托盘打开面板：先浮到最前让他看见，鼠标移开就自动
        /// 回到桌面层（免得它一直挡着别人）。
        /// </summary>
        internal void RaiseForUser()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }
            try
            {
                _raiseForUser = true;
                _raiseSeenMouse = false;
                // 给 3 秒宽限：从托盘点开的人多半要先找一下面板在哪。
                _raiseDeadline = DateTime.Now.AddSeconds(3);
                NativeSetWindowPos(Handle, HwndTopMost,
                    0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                _zAtFront = true;
            }
            catch (Exception ex)
            {
                Log.Error("把面板临时提到最前失败", ex);
            }
        }

        /// <summary>前台窗口是不是桌面本身（Progman / WorkerW）。</summary>
        private static bool IsDesktopForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return true;
            }
            string cls = WindowClassOf(hWnd);
            return cls == "Progman" || cls == "WorkerW" ||
                cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
        }

        private static string WindowClassOf(IntPtr hWnd)
        {
            System.Text.StringBuilder sb =
                new System.Text.StringBuilder(128);
            NativeGetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// 把常驻面板压到 z-order 最底（桌面之上、所有应用窗口之下）。
        /// 不激活、不改尺寸，只在显示 / 重新露出来的时候调一次。
        /// </summary>
        private void SendToDesktopLayer()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }
            try
            {
                // 不能直接 HWND_BOTTOM：那会沉到桌面窗口（Progman）底下去，
                // 面板被桌面盖住，屏幕上一个像素都看不见（实测过）。
                // 正确位置是「紧贴桌面之上、所有应用窗口之下」——把面板插到
                // 桌面窗口前面那个窗口之前。
                IntPtr desktop = NativeFindWindow("Progman", null);
                IntPtr after = desktop == IntPtr.Zero
                    ? HwndBottom
                    : NativeGetWindow(desktop, GwHwndPrev);
                if (after == IntPtr.Zero || after == Handle)
                {
                    return;
                }
                NativeSetWindowPos(Handle, after,
                    0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            catch (Exception ex)
            {
                Log.Error("把面板压到桌面层失败", ex);
            }
        }

        /// <summary>
        /// 面板左上角在屏幕上的位置。嵌进桌面后 Left / Top 是相对宿主的，
        /// 抓屏、持久化、位置换算都得用这个。
        /// </summary>
        private Point ScreenOrigin
        {
            get
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        return PointToScreen(Point.Empty);
                    }
                    catch
                    {
                    }
                }
                return new Point(Left, Top);
            }
        }

        /// <summary>
        /// 锁定位置：钉死在桌面上，标题栏拖动不再改变位置（双击收起 /
        /// 展开仍然可用——那不会挪动面板）。
        /// </summary>
        internal void SetLocked(bool value)
        {
            if (_locked == value)
            {
                return;
            }
            _locked = value;
            SavePanelState();
            Invalidate(new Rectangle(0, 0, Width, TitleH));
        }

        internal bool Locked
        {
            get { return _locked; }
        }

        /// <summary>
        /// 当前锁定状态。面板没开时以配置为准，托盘菜单的勾选状态就不会
        /// 和真实状态对不上。
        /// </summary>
        internal static bool LockEnabled
        {
            get
            {
                if (_instance != null && !_instance.IsDisposed)
                {
                    return _instance._locked;
                }
                try
                {
                    return ConfigStore.Load().DesktopPanelLocked;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 常驻时给窗口加上 WS_EX_TOOLWINDOW：不进 Alt+Tab、不进任务栏，
        /// 就是桌面上的一块小部件；取消常驻时还原成普通窗口。
        /// </summary>
        private void ApplyToolWindow(bool enabled)
        {
            if (!IsHandleCreated)
            {
                return;
            }
            try
            {
                // WinForms 在改 ShowInTaskbar / TopMost / Region 时会拿
                // CreateParams 重算整份 ExStyle，直接 SetWindowLong 加上的
                // 位会被抹掉（实测 ex=0x00010000，0x80 不见了）。先让
                // WinForms 按最新 _resident 重算一遍，再补一次系统调用兜底。
                UpdateStyles();
                int style = GetWindowLong(Handle, GwlexStyle);
                int next = enabled
                    ? (style | WsExToolWindow)
                    : (style & ~WsExToolWindow);
                if (next != style)
                {
                    SetWindowLong(Handle, GwlexStyle, next);
                    NativeSetWindowPos(
                        Handle, IntPtr.Zero, 0, 0, 0, 0,
                        SwpNoMove | SwpNoSize | SwpNoZOrder |
                        SwpNoActivate | SwpFrameChanged);
                }
            }
            catch (Exception ex)
            {
                Log.Error("设置桌面整理面板窗口样式失败", ex);
            }
        }

        /// <summary>
        /// 常驻标记必须进 CreateParams 才算真正钉住：WinForms 每次
        /// UpdateStyles 都在这里取值，外面再 SetWindowLong 会被覆盖。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (_resident)
                {
                    cp.ExStyle |= WsExToolWindow;
                }
                return cp;
            }
        }

        private const int GwlexStyle = -20;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTopMost = 0x00000008;
        /// <summary>SetWindowPos 的 insertAfter：压到 z-order 最底。</summary>
        private static readonly IntPtr HwndBottom = new IntPtr(1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        private const int GwHwndPrev = 3;
        private static readonly IntPtr HwndTopMost = new IntPtr(-1);
        private static readonly IntPtr HwndNoTopMost = new IntPtr(-2);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern IntPtr NativeGetForegroundWindow();

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "WindowFromPoint")]
        private static extern IntPtr NativeWindowFromPoint(
            NativeMethods.POINT point);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "GetAncestor")]
        private static extern IntPtr NativeGetAncestor(
            IntPtr hWnd, uint flags);

        private const uint GaRoot = 2;

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "GetClassName",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int NativeGetClassName(
            IntPtr hWnd, System.Text.StringBuilder name, int maxCount);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "FindWindow",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr NativeFindWindow(
            string className, string windowName);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "GetWindow")]
        private static extern IntPtr NativeGetWindow(IntPtr hWnd, int command);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "SetWindowPos")]
        private static extern bool NativeSetWindowPos(
            IntPtr hWnd, IntPtr insertAfter,
            int x, int y, int cx, int cy, uint flags);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [System.Runtime.InteropServices.DllImport(
            "user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(
            IntPtr hWnd, int index, int newStyle);

        /// <summary>
        /// 常驻面板是桌面上的小部件：点它只处理鼠标，不该像普通窗口那样
        /// 把自己激活、顶到正在用的软件上面去（用户抱怨的就是「点一下
        /// 面板就盖在浏览器上了」）。
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseActivate)
            {
                m.Result = (IntPtr)MaNoActivate;
                return;
            }
            base.WndProc(ref m);
        }

        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;

        // 边框拉伸自己处理鼠标，不走系统 sizing 循环：系统那个受
        // 「拖动时显示窗口内容」开关影响（关掉只画虚线框，看不到列表
        // 实时变化），而且常驻窗口点了不激活，系统循环容易被打断。
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;

        /// <summary>
        /// 哪些边框允许拉伸。**展开**时只有左边框（宽度）和上边框（高度），
        /// 左上角可同时拉；右边框 / 下边框一律不响应，保证「下边框不动」。
        /// **收起**时短条高度锁死，只让左右边框改长度。
        /// </summary>
        private int ResizeHitAtClient(Point p)
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0 ||
                p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h)
            {
                return 0;
            }
            bool left = p.X < ResizeGrip;
            bool right = p.X >= w - ResizeGrip;
            bool top = p.Y < ResizeGrip;
            if (_collapsed)
            {
                if (right)
                {
                    return HtRight;
                }
                if (left)
                {
                    return HtLeft;
                }
                return 0;
            }
            if (top && left)
            {
                return HtTopLeft;
            }
            if (left)
            {
                return HtLeft;
            }
            if (top)
            {
                return HtTop;
            }
            return 0;
        }

        /// <summary>鼠标压在可拉伸边框上时显示双向箭头（拉伸形态）。</summary>
        private void UpdateResizeCursor(int hit)
        {
            Cursor want = Cursors.Default;
            switch (hit)
            {
                case HtLeft:
                case HtRight:
                    want = Cursors.SizeWE;
                    break;
                case HtTop:
                    want = Cursors.SizeNS;
                    break;
                case HtTopLeft:
                    want = Cursors.SizeNWSE;
                    break;
            }
            if (Cursor != want)
            {
                Cursor = want;
            }
        }

        private void AttachResizeForward(Control child)
        {
            child.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }
                ForwardDownCountForTest++;
                // 用全局鼠标位置直接换算到窗体客户区：子控件在 Capture /
                // 滚动 / DPI 缩放下的 e.Location 可能和真实光标不一致，
                // 边框只有 6px 宽，差几个像素就会误判成「没抓到」。
                Point local = PointToClient(Cursor.Position);
                int hit = ResizeHitAtClient(local);
                if (hit != 0)
                {
                    ForwardHitCountForTest++;
                    BeginResize(hit);
                }
            };
            child.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_resizing)
                {
                    ForwardMoveCountForTest++;
                    UpdateResize(Cursor.Position);
                    return;
                }
                UpdateResizeCursor(
                    ResizeHitAtClient(PointToClient(Cursor.Position)));
            };
            child.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (_resizing)
                {
                    ForwardUpCountForTest++;
                    EndResize();
                }
            };
        }

        private void BeginResize(int hit)
        {
            _resizeHit = hit;
            _resizeStartMouse = Cursor.Position;
            _resizeStartBounds = new Rectangle(ScreenOrigin, Size);
            _resizing = true;
            _sizing = true;
            _hoverElapsed = 0;
            // 拖动期间摘掉圆角 Region：每帧 SetWindowRgn 会触发整窗
            // 重合成，是拖边框闪烁的大头；松手在 EndResize 里恢复。
            Region = null;
            // Capture 保证拖出窗体范围之后还能收到 MouseMove / MouseUp。
            Capture = true;
            UpdateResizeCursor(hit);
        }

        /// <summary>
        /// 每一帧按鼠标位移重算窗体：左边框改宽时**右边缘不动**，上边框
        /// 改高时**下边缘不动**（往上拉变高、列表显示更多；往下拉变矮、
        /// 显示更少）。SetBounds 立刻触发 OnResize → 列表重排重绘。
        /// </summary>
        private void UpdateResize(Point now)
        {
            int dx = now.X - _resizeStartMouse.X;
            int dy = now.Y - _resizeStartMouse.Y;
            Rectangle b = _resizeStartBounds;
            int left = b.Left;
            int top = b.Top;
            int w = b.Width;
            int h = b.Height;
            int minW = _collapsed ? MinCollapsedW : MinExpandedW;
            int minH = _collapsed ? TitleH : MinExpandedH;

            bool fromLeft = (_resizeHit == HtLeft ||
                _resizeHit == HtTopLeft);
            bool fromRight = (_collapsed && _resizeHit == HtRight);
            bool fromTop = (_resizeHit == HtTop ||
                _resizeHit == HtTopLeft);

            if (fromLeft)
            {
                int want = b.Right - (b.Left + dx);
                if (want < minW)
                {
                    want = minW;
                }
                if (want > MaxPanelW)
                {
                    want = MaxPanelW;
                }
                left = b.Right - want;
                w = want;
            }
            else if (fromRight)
            {
                int want = b.Width + dx;
                if (want < minW)
                {
                    want = minW;
                }
                if (want > MaxPanelW)
                {
                    want = MaxPanelW;
                }
                w = want;
            }
            if (fromTop)
            {
                int want = b.Bottom - (b.Top + dy);
                if (want < minH)
                {
                    want = minH;
                }
                if (want > MaxPanelH)
                {
                    want = MaxPanelH;
                }
                top = b.Bottom - want;
                h = want;
            }

            Point origin = ScreenOrigin;
            // 位置 + 尺寸一次改完：分开赋 Location / Size 会各发一次
            // WM_WINDOWPOSCHANGED、各触发一轮重排重绘，拖动时就是闪烁。
            Bounds = new Rectangle(
                Location.X + left - origin.X,
                Location.Y + top - origin.Y,
                w, h);
        }

        private void EndResize()
        {
            _resizing = false;
            _sizing = false;
            if (Capture)
            {
                Capture = false;
            }
            ApplyRoundedRegion();
            PersistResizedSize();
        }

        /// <summary>
        /// 松手后把用户拉出来的形状记进配置，并补抓一张准的底板。
        /// 拉伸过程中每帧只重排 + 重绘，文件列表行数当场跟着变。
        /// </summary>
        private void PersistResizedSize()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            if (_collapsed)
            {
                _collapsedWidth = ClampSize(Width,
                    MinCollapsedW, MaxPanelW, _collapsedWidth);
                if (Height != TitleH)
                {
                    Height = TitleH;
                }
            }
            else
            {
                _expandedWidth = ClampSize(Width,
                    MinExpandedW, MaxPanelW, _expandedWidth);
                _expandedHeight = ClampSize(Height,
                    MinExpandedH, MaxPanelH, _expandedHeight);
            }
            ApplySizeLimits();
            LayoutChildren();
            if (_backdropReady)
            {
                RebuildBackdrop();
            }
            Invalidate(true);
            SavePanelState();
        }

        /// <summary>圆角外形，像一块桌面小部件，而不是四四方方的窗体。</summary>
        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }
            try
            {
                using (GraphicsPath path = DesktopGlass.Rounded(
                    new RectangleF(0f, 0f, Width, Height), 10f))
                {
                    Region = new Region(path);
                }
            }
            catch
            {
            }
        }

        private static int ClampSize(
            int value, int min, int max, int fallback)
        {
            return (value < min || value > max) ? fallback : value;
        }

        /// <summary>
        /// 系统 sizing 循环会读 MinimumSize / MaximumSize 来限制拉伸范围。
        /// 收起时短条高度锁死 44、宽度最小 150；展开时最小 300×220。
        /// </summary>
        private void ApplySizeLimits()
        {
            MinimumSize = _collapsed
                ? new Size(MinCollapsedW, TitleH)
                : new Size(MinExpandedW, MinExpandedH);
            MaximumSize = new Size(MaxPanelW, MaxPanelH);
        }

        private void RestorePanelState()
        {
            AppConfig config = null;
            try
            {
                config = ConfigStore.Load();
            }
            catch (Exception ex)
            {
                Log.Error("读取桌面整理面板状态失败", ex);
            }
            _restoringState = true;
            try
            {
                if (config != null && config.DesktopPanelResident)
                {
                    SetResident(true);
                }
                if (config != null)
                {
                    // 用户拖过边框的尺寸记在配置里，老配置 / 越界值回落
                    // 到默认形状。展开状态下要立刻把窗体尺寸也换过来，
                    // 后面按宽度做的边界夹取才是对的。
                    _expandedWidth = ClampSize(
                        config.DesktopPanelExpandedWidth,
                        MinExpandedW, MaxPanelW, DefaultExpandedW);
                    _expandedHeight = ClampSize(
                        config.DesktopPanelExpandedHeight,
                        MinExpandedH, MaxPanelH, DefaultExpandedH);
                    _collapsedWidth = ClampSize(
                        config.DesktopPanelCollapsedWidth,
                        MinCollapsedW, MaxPanelW, DefaultCollapsedW);
                    if (!config.DesktopPanelCollapsed)
                    {
                        Size = new Size(_expandedWidth, _expandedHeight);
                    }
                }
                if (config != null && config.DesktopPanelPosSet)
                {
                    // 必须切到 Manual：StartPosition=CenterScreen 会在 Show()
                    // 时把这里设好的 Location 覆盖掉（表现为常驻面板总是
                    // 跑到屏幕正中）。
                    StartPosition = FormStartPosition.Manual;
                    // 配置里存的是「展开状态左上角」。收起时短条与展开
                    // 右边缘对齐，所以左上角要右移这段差值，才是用户上次
                    // 看到的那条短横条的位置。
                    bool wasCollapsed = config.DesktopPanelCollapsed;
                    int savedX = config.DesktopPanelX +
                        (wasCollapsed
                            ? _expandedWidth - _collapsedWidth : 0);
                    Rectangle wa = Screen.FromPoint(
                        new Point(savedX, config.DesktopPanelY)).WorkingArea;
                    int clampW = wasCollapsed ? _collapsedWidth : Width;
                    int clampH = wasCollapsed ? TitleH : Height;
                    int x = Math.Max(wa.Left,
                        Math.Min(savedX, wa.Right - clampW));
                    int y = Math.Max(wa.Top,
                        Math.Min(config.DesktopPanelY, wa.Bottom - clampH));
                    Location = new Point(x, y);
                }
                else if (config != null && config.DesktopPanelResident)
                {
                    // 第一次常驻：贴在屏幕右上角，不挡住中间的活。
                    StartPosition = FormStartPosition.Manual;
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    Location = new Point(
                        Math.Max(wa.Left, wa.Right - Width - 24),
                        wa.Top + 96);
                }
                if (config != null && config.DesktopPanelCollapsed)
                {
                    SetCollapsed(true);
                }
                if (config != null && config.DesktopPanelLocked)
                {
                    SetLocked(true);
                }
            }
            finally
            {
                _restoringState = false;
            }
            ApplyRoundedRegion();
        }

        private void SavePanelState()
        {
            if (_restoringState || IsDisposed || _instance != this)
            {
                return;
            }
            try
            {
                AppConfig config = ConfigStore.Load();
                config.DesktopPanelResident = _resident;
                config.DesktopPanelCollapsed = _collapsed;
                config.DesktopPanelLocked = _locked;
                // 位置统一按「展开状态左上角」记：收起时短条右对齐，
                // 比展开位置右移了「展开宽 - 短条宽」，这里减回去，
                // 下次启动才不会一次往左漂 190px。
                config.DesktopPanelPosSet = true;
                // 嵌进桌面后 Left / Top 是相对桌面宿主的，存盘统一换成
                // 屏幕坐标，下次启动还没嵌进去时才能按原位置摆好。
                Point origin = ScreenOrigin;
                config.DesktopPanelX = origin.X -
                    (_collapsed ? _expandedWidth - _collapsedWidth : 0);
                config.DesktopPanelY = origin.Y;
                config.DesktopPanelExpandedWidth = _expandedWidth;
                config.DesktopPanelExpandedHeight = _expandedHeight;
                config.DesktopPanelCollapsedWidth = _collapsedWidth;
                ConfigStore.Save(config);
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                Log.Error("保存桌面整理面板状态失败", ex);
            }
        }

        private static void SaveResidentFlag(bool resident)
        {
            try
            {
                AppConfig config = ConfigStore.Load();
                config.DesktopPanelResident = resident;
                ConfigStore.Save(config);
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                Log.Error("保存桌面整理常驻设置失败", ex);
            }
        }

        internal void LoadFromPath(string path)
        {
            _root = path;
            // 加载的是新目录，上一个目录里选的页签没有意义；重置后
            // Reload 会用新目录的内容挑第一个非空分类，默认页签才确定。
            _activeCategory = Categories[0];
            Reload();
        }

        /// <summary>自检 / 探针用：指定要屏蔽的文件夹列表（; 分隔的完整路径）。</summary>
        internal void SetHiddenFoldersForTest(string paths)
        {
            _hiddenFolders = paths ?? "";
            Reload();
        }

        internal int ItemCount
        {
            get { return _items == null ? 0 : _items.Count; }
        }

        /// <summary>
        /// 不分页签的总条目数。去掉「全部」页签后 ItemCount 只代表当前
        /// 分类，自检 / 探针要断言「扫到多少条」时得看这个。
        /// </summary>
        internal int TotalItemCount
        {
            get { return _allItems == null ? 0 : _allItems.Count; }
        }

        /// <summary>已经补上图标（非 null）的条目数，自检用来确认后台填充。</summary>
        internal int LoadedIconCount
        {
            get
            {
                if (_allItems == null)
                {
                    return 0;
                }
                int count = 0;
                for (int i = 0; i < _allItems.Count; i++)
                {
                    if (_allItems[i].Icon != null)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        internal ViewModeKind ViewMode
        {
            get { return _view; }
            set
            {
                if (_view != value)
                {
                    _view = value;
                    UpdateViewButton();
                    UpdateScroll();
                    _list.Invalidate();
                }
            }
        }

        internal void ToggleView()
        {
            ViewMode = (_view == ViewModeKind.List)
                ? ViewModeKind.Icon
                : ViewModeKind.List;
        }

        internal double CurrentOpacity
        {
            get { return _bgAlpha; }
        }

        /// <summary>底板用的桌面截图是否真的抓到了（自检用）。</summary>
        internal bool HasDesktopBackdrop
        {
            get
            {
                return _backdrop != null && !_backdropUnavailable &&
                    _bgAlpha < 0.999;
            }
        }

        internal void SetOpacityForTest(double value)
        {
            ApplyOpacity(value);
        }

        /// <summary>
        /// 自检实拍用：暂停层级跟随。常驻面板平时会每 400ms 把
        /// TopMost 撤掉压回桌面层，探针刚设的置顶会被它撤销，
        /// 截图就拍到后面的应用窗口去了。
        /// </summary>
        internal void SuspendZOrderForTest()
        {
            _probeMode = true;
            _zOrderTimer.Stop();
            _hoverTimer.Stop();
        }

        /// <summary>自检用：强制重做一次底板（模拟拖动 / 改透明度）。</summary>
        internal void RefreshBackdropForTest()
        {
            RebuildBackdrop();
            Invalidate(true);
        }

        /// <summary>自检用：取底板上某一点的颜色，验证不会「越叠越暗」。</summary>
        internal Color SampleBackdropForTest(int x, int y)
        {
            Bitmap bmp = _backdrop;
            if (bmp == null || x < 0 || y < 0 ||
                x >= bmp.Width || y >= bmp.Height)
            {
                return Color.Empty;
            }
            return bmp.GetPixel(x, y);
        }

        /// <summary>图标里真的有可见像素的条目数（自检用，防「图标全空」）。</summary>
        internal int VisibleIconCount
        {
            get
            {
                if (_allItems == null)
                {
                    return 0;
                }
                int count = 0;
                for (int i = 0; i < _allItems.Count; i++)
                {
                    Bitmap icon = _allItems[i].Icon;
                    if (icon != null && HasVisiblePixel(icon))
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// 已解出真缩略图的图片条目数。自检用它确认「图片格里真的是缩略图，
        /// 不是统一的类型图标」。
        /// </summary>
        internal int ThumbnailCount
        {
            get
            {
                if (_allItems == null)
                {
                    return 0;
                }
                int count = 0;
                for (int i = 0; i < _allItems.Count; i++)
                {
                    if (_allItems[i].Thumbnail)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>当前激活的分类页签名。</summary>
        internal string ActiveCategory
        {
            get { return _activeCategory; }
        }

        /// <summary>切换到某个分类页签（名字须在 Categories 里）。</summary>
        internal void SelectCategory(string category)
        {
            if (Array.IndexOf(Categories, category) < 0)
            {
                return;
            }
            if (_activeCategory != category)
            {
                _activeCategory = category;
                ApplyCategoryFilter();
            }
        }

        /// <summary>某个分类页签下的条目数（不动当前选中态）。</summary>
        internal int CountForCategory(string category)
        {
            return CountInCategory(category);
        }

        /// <summary>
        /// 只分类、不搬家：重新扫一遍目录，按扩展名把条目归到各分类页签，
        /// **不移动任何文件、不创建任何文件夹**，桌面保持原样。
        /// 早期版本会把文件 Move 进「文档 / 图片 / …」文件夹，桌面会被搅乱、
        /// 跨盘或文件被占用时还会失败，因此改为纯展示层分类。
        /// 返回本次归类的文件数。
        /// </summary>
        internal int Organize(string rootOverride)
        {
            string root = rootOverride ?? _root;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                SetStatus("找不到桌面目录");
                return 0;
            }
            Reload();
            int classified = 0;
            List<Item> items = _allItems;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].IsDirectory)
                {
                    classified++;
                }
            }
            SetStatus("已按类型归类 " + classified +
                " 个文件（文件都还在原位，未移动）");
            return classified;
        }

        // ---- 数据加载 ----

        /// <summary>
        /// 加载桌面：只读顶层条目。旧版会把文件 Move 进「文档/图片/…」
        /// 分类文件夹，那时还得进去把内容映射回来；新版只分类不搬家，
        /// 分类目录映射已经彻底断开，不再读任何子目录的内容。
        /// 图标不在这一步取，由 QueueIconLoad 在后台补。加载完按当前
        /// 页签过滤。
        /// </summary>
        private void Reload()
        {
            // 先让在跑的后台图标任务失效，再释放旧列表。
            _iconGeneration++;
            DisposeItems(_allItems);
            List<Item> next = new List<Item>();
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
            {
                try
                {
                    DirectoryInfo dir = new DirectoryInfo(_root);
                    foreach (FileSystemInfo info in dir.EnumerateFileSystemInfos())
                    {
                        if ((info.Attributes & FileAttributes.Hidden) != 0)
                        {
                            continue;
                        }
                        bool isDir = (info.Attributes & FileAttributes.Directory)
                            != 0;
                        // 用户明确不想在面板里看到桌面上的「文档」文件夹
                        // （那是他自己的资料目录，不是工具的内部结构），
                        // 按名字跳过，也不读它里面的内容。
                        if (isDir && IsMutedFolder(info.FullName))
                        {
                            continue;
                        }
                        next.Add(BuildItem(info, ""));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("读取桌面文件失败", ex);
                }
            }
            next.Sort(CompareItems);
            _allItems = next;
            EnsureCategoryHasItems();
            ApplyCategoryFilter();
            // 列表先出来，图标随后在后台补——不再把 UI 线程卡在几十次
            // ExtractAssociatedIcon 上（那正是打开面板「有点卡」的来源）。
            QueueIconLoad();
        }

        private static Item BuildItem(FileSystemInfo info, string container)
        {
            bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
            Item it = new Item
            {
                Name = info.Name,
                FullPath = info.FullName,
                IsDirectory = isDir,
                IsImage = !isDir && IsImageExt(info.FullName),
                Modified = info.LastWriteTime,
                Category = CategoryOf(info.FullName, isDir),
                Container = container
            };
            // 分类文件夹里的条目归到该文件夹的分类，保证页签过滤正确。
            if (container.Length > 0)
            {
                it.Category = container;
            }
            if (!isDir)
            {
                try
                {
                    // EnumerateFileSystemInfos 返回的本来就是 FileInfo，
                    // 直接读 Length，别再 new 一个重新 stat 一次。
                    FileInfo file = info as FileInfo;
                    it.Size = file != null
                        ? file.Length
                        : new FileInfo(info.FullName).Length;
                }
                catch
                {
                    it.Size = 0;
                }
            }
            // 图标交给后台线程补，这里不碰磁盘——同步提取图标是打开
            // 面板卡顿的主因（每个文件一次 shell 图标查询）。
            it.Icon = null;
            it.IconOwned = false;
            return it;
        }

        /// <summary>
        /// 后台补图标：先出列表再慢慢填，每补几个刷一次界面。
        /// 代次变了（又 Reload 了 / 窗口关了）立刻停手。
        /// </summary>
        private void QueueIconLoad()
        {
            int generation = _iconGeneration;
            List<Item> snapshot = new List<Item>(_allItems);
            if (snapshot.Count == 0)
            {
                return;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        if (generation != _iconGeneration)
                        {
                            return;
                        }
                        Item it = snapshot[i];
                        if (it.Icon != null)
                        {
                            continue;
                        }
                        bool owned = true;
                        Bitmap icon = null;
                        // 图片先试真缩略图：桌面上那堆截图光看一个蓝色
                        // 「图片」图标根本分不清哪张是哪张。解码失败
                        // （文件坏了 / 太大 / 格式不认）再退回 shell 图标。
                        if (it.IsImage)
                        {
                            icon = LoadImageThumb(it.FullPath);
                            it.Thumbnail = icon != null;
                        }
                        if (icon == null)
                        {
                            icon = LoadIconCached(
                                it.FullPath, it.IsDirectory, out owned);
                        }
                        it.Icon = icon;
                        it.IconOwned = owned;
                        if ((i % 4) == 3)
                        {
                            PostIconProgress(generation);
                        }
                    }
                    PostIconProgress(generation);
                }
                catch (Exception ex)
                {
                    // 后台线程里的异常没人接会把整个进程带走，这里必须兜住。
                    Log.Error("后台加载文件图标失败", ex);
                }
            });
        }

        private void PostIconProgress(int generation)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (generation != _iconGeneration || IsDisposed)
                    {
                        return;
                    }
                    _list.Invalidate();
                });
            }
            catch (InvalidOperationException)
            {
                // 窗口正在销毁，忽略即可。
            }
        }

        /// <summary>
        /// 取图标（带缓存）。同一个扩展名只查一次 shell，返回缓存位图的
        /// 克隆，条目自己负责释放，缓存本体进程内常驻。
        /// </summary>
        private static Bitmap LoadIconCached(
            string path, bool isDirectory, out bool owned)
        {
            owned = true;
            string ext = Path.GetExtension(path);
            if (ext == null)
            {
                ext = string.Empty;
            }
            ext = ext.ToLowerInvariant();
            // exe / lnk / ico / msi 每个文件的图标都不同，只能按路径缓存。
            bool perFile = !isDirectory &&
                (ext == ".exe" || ext == ".lnk" ||
                 ext == ".ico" || ext == ".msi");
            string key = perFile
                ? "f:" + path
                : "e:" + (isDirectory
                    ? "*dir*"
                    : (ext.Length == 0 ? "*none*" : ext));

            lock (s_iconLock)
            {
                Bitmap cached;
                if (s_iconCache.TryGetValue(key, out cached))
                {
                    return new Bitmap(cached);
                }
            }

            Bitmap extracted = GetIcon(path, isDirectory);
            lock (s_iconLock)
            {
                if (!s_iconCache.ContainsKey(key))
                {
                    s_iconCache[key] = new Bitmap(extracted);
                }
            }
            return extracted;
        }

        private static int CompareItems(Item a, Item b)
        {
            int c = b.IsDirectory.CompareTo(a.IsDirectory);
            if (c != 0)
            {
                return c;
            }
            return string.Compare(a.Name, b.Name, true);
        }

        /// <summary>
        /// 这个文件夹要不要在面板里藏起来。只看配置里显式列出的完整路径，
        /// 不按名字判断——用户自己建在桌面上的「文档」「图片」这类同名
        /// 资料目录应该正常显示，不能被当成工具的内部结构。
        /// </summary>
        private bool IsMutedFolder(string fullPath)
        {
            if (string.IsNullOrEmpty(_hiddenFolders) || string.IsNullOrEmpty(fullPath))
            {
                return false;
            }
            string normalized = NormalizePath(fullPath);
            string[] parts = _hiddenFolders.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string entry = parts[i].Trim();
                if (entry.Length == 0)
                {
                    continue;
                }
                if (string.Equals(NormalizePath(entry), normalized,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizePath(string path)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                full = path;
            }
            return full.TrimEnd('\\', '/');
        }

        private static bool IsImageExt(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                return false;
            }
            return Array.IndexOf(ImageExts, ext.ToLowerInvariant()) >= 0;
        }

        private static void DisposeItems(List<Item> items)
        {
            if (items == null)
            {
                return;
            }
            foreach (Item it in items)
            {
                if (it.Icon != null && it.IconOwned)
                {
                    it.Icon.Dispose();
                }
            }
        }

        /// <summary>
        /// 打开面板时落在有内容的那个页签上：当前页签空了就去第一个非空
        /// 分类（桌面只剩图片时不该一打开就看见空的「文档」）。
        /// 用户手动点到一个空页签属于主动选择，不在这里被踢走——本方法
        /// 只在重新扫盘后调用。
        /// </summary>
        private void EnsureCategoryHasItems()
        {
            if (Array.IndexOf(Categories, _activeCategory) < 0)
            {
                _activeCategory = Categories[0];
            }
            if (CountInCategory(_activeCategory) > 0)
            {
                return;
            }
            for (int i = 0; i < Categories.Length; i++)
            {
                if (CountInCategory(Categories[i]) > 0)
                {
                    _activeCategory = Categories[i];
                    return;
                }
            }
        }

        private void ApplyCategoryFilter()
        {
            List<Item> filtered = new List<Item>();
            foreach (Item it in _allItems)
            {
                if (MatchesCategory(it, _activeCategory))
                {
                    filtered.Add(it);
                }
            }
            _items = filtered;
            _selectedIndex = -1;
            UpdateScroll();
            if (_list != null)
            {
                _list.Invalidate();
            }
            UpdateStatus();
        }

        private static bool MatchesCategory(Item it, string category)
        {
            if (category == AllCategory)
            {
                return true;
            }
            if (category == "目录")
            {
                return it.IsDirectory;
            }
            return it.Category == category;
        }

        private int CountInCategory(string category)
        {
            if (_allItems == null)
            {
                return 0;
            }
            int n = 0;
            foreach (Item it in _allItems)
            {
                if (MatchesCategory(it, category))
                {
                    n++;
                }
            }
            return n;
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }
            if (_activeCategory == AllCategory)
            {
                _statusLabel.Text = "共 " + _items.Count +
                    " 项 · 桌面整理按类型收纳（文件不移动）";
            }
            else if (_activeCategory == "目录")
            {
                _statusLabel.Text = "目录 · " + _items.Count + " 个文件夹";
            }
            else
            {
                _statusLabel.Text = _activeCategory + " · " + _items.Count +
                    " 个 · 桌面顶层 + 「" + _activeCategory + "」文件夹";
            }
        }

        private static string CategoryOf(string path, bool isDir)
        {
            if (isDir)
            {
                return "目录";
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(DocExts, ext) >= 0)
            {
                return "文档";
            }
            if (Array.IndexOf(ImageExts, ext) >= 0)
            {
                return "图片";
            }
            if (Array.IndexOf(VideoExts, ext) >= 0)
            {
                return "视频";
            }
            if (Array.IndexOf(CompressExts, ext) >= 0)
            {
                return "压缩";
            }
            return "其它";
        }

        private static int s_iconFailuresLogged;

        private static void LogIconFailure(string path, Exception ex)
        {
            // 文件在扫描后又被删掉是常态，不算错误，不要刷日志。
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return;
            }
            if (System.Threading.Interlocked.Increment(
                ref s_iconFailuresLogged) > 5)
            {
                return;
            }
            Log.Error("提取文件图标失败: " + path, ex);
        }

        /// <summary>
        /// 解码图片并等比缩到 48×48（居中、四周透明），列表 / 图标视图里
        /// 一眼能认出是哪张图。上限 40 MB：更大的图片解码动辄几百毫秒，
        /// 桌面上一堆原图会把后台队列堵住，宁可退回类型图标。
        /// 返回 null 表示「这张图读不出来」，调用方退回 shell 图标。
        /// </summary>
        private static Bitmap LoadImageThumb(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 ||
                    info.Length > 40L * 1024 * 1024)
                {
                    return null;
                }
                using (FileStream stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (Image source = Image.FromStream(stream, false, false))
                {
                    int w = source.Width;
                    int h = source.Height;
                    if (w <= 0 || h <= 0)
                    {
                        return null;
                    }
                    float scale = Math.Min(
                        (float)ThumbPx / w, (float)ThumbPx / h);
                    int tw = Math.Max(1, (int)Math.Round(w * scale));
                    int th = Math.Max(1, (int)Math.Round(h * scale));
                    Bitmap result = new Bitmap(
                        ThumbPx, ThumbPx, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(result))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.DrawImage(source, new Rectangle(
                            (ThumbPx - tw) / 2, (ThumbPx - th) / 2,
                            tw, th));
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                LogIconFailure(path, ex);
                return null;
            }
        }

        private static Bitmap GetIcon(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                try
                {
                    Bitmap folder = FolderIcon();
                    if (folder != null)
                    {
                        return folder;
                    }
                }
                catch (Exception ex)
                {
                    LogIconFailure(path, ex);
                }
            }
            try
            {
                using (Icon ico = Icon.ExtractAssociatedIcon(path))
                {
                    if (ico != null)
                    {
                        return ComposeIcon(ico);
                    }
                }
            }
            catch (Exception ex)
            {
                // 取图标失败不致命（退回占位图标），但前几次记日志，
                // 免得「全是白框」这种问题只能靠猜。
                LogIconFailure(path, ex);
            }
            return FallbackIcon();
        }

        /// <summary>
        /// 把系统图标画进一张 32×32 的透明位图。注意返回的位图**不能**
        /// 放进 using：它要交给图标缓存，提前释放的话下一次绘制会抛
        /// GDI+ 异常，整个进程直接崩。
        ///
        /// `Icon.ToBitmap()` 在部分图标上会给出「RGB 正常但 alpha 全 0」
        /// 的位图，画出来就是一片空白；所以这里用 `Graphics.DrawIcon`
        /// 直接走掩码绘制，并检查结果是否真的有可见像素，没有就退回
        /// 「不透明底 + 抠黑」的老办法。
        /// </summary>
        private static Bitmap ComposeIcon(Icon ico)
        {
            Bitmap bmp = new Bitmap(
                32, 32, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawIcon(ico, new Rectangle(0, 0, 32, 32));
            }
            if (!HasVisiblePixel(bmp))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawIcon(ico, new Rectangle(0, 0, 32, 32));
                }
                bmp.MakeTransparent(Color.Black);
            }
            return bmp;
        }

        /// <summary>位图里是否存在不透明像素（抽样 alpha 通道）。</summary>
        private static bool HasVisiblePixel(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(
                rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * bmp.Height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int i = 3; i < bytes; i += 4)
                {
                    if (buffer[i] > 20)
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        /// <summary>
        /// 自绘文件夹图标。系统 shell 的文件夹图标在这台机器上取回来是
        /// 空图（alpha 全 0），不如自己画一个：两段圆角矩形 + 高光，
        /// 深色 / 浅色主题下都清楚，也不用再为每台机器赌 shell 行为。
        /// </summary>
        private static Bitmap FolderIcon()
        {
            Bitmap bmp = new Bitmap(
                32, 32, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // 后面那片「标签」
                using (GraphicsPath tab = DesktopGlass.Rounded(
                    new RectangleF(2.5f, 7f, 13f, 9f), 2.5f))
                using (SolidBrush brush =
                    new SolidBrush(Color.FromArgb(224, 172, 62)))
                {
                    g.FillPath(brush, tab);
                }

                // 文件夹主体
                using (GraphicsPath body = DesktopGlass.Rounded(
                    new RectangleF(2.5f, 10.5f, 27f, 18.5f), 3.2f))
                {
                    using (SolidBrush brush =
                        new SolidBrush(Color.FromArgb(243, 194, 79)))
                    {
                        g.FillPath(brush, body);
                    }
                    using (Pen pen =
                        new Pen(Color.FromArgb(198, 151, 51), 1f))
                    {
                        g.DrawPath(pen, body);
                    }
                }

                // 顶部高光，避免整块死黄
                using (SolidBrush brush =
                    new SolidBrush(Color.FromArgb(70, 255, 248, 214)))
                {
                    g.FillRectangle(brush, 5f, 13f, 22f, 3.5f);
                }
            }
            return bmp;
        }

        private static Bitmap FallbackIcon()
        {
            Bitmap fallback = new Bitmap(
                32, 32, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(fallback))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                // 一张纸 + 折角：认不出类型的文件也别只剩一个空框。
                using (GraphicsPath sheet = DesktopGlass.Rounded(
                    new RectangleF(8f, 4f, 16f, 24f), 2f))
                using (SolidBrush brush =
                    new SolidBrush(Color.FromArgb(178, 186, 198)))
                {
                    g.FillPath(brush, sheet);
                }
                using (SolidBrush brush =
                    new SolidBrush(Color.FromArgb(232, 236, 242)))
                {
                    g.FillRectangle(brush, 10f, 15f, 12f, 1.6f);
                    g.FillRectangle(brush, 10f, 19f, 12f, 1.6f);
                    g.FillRectangle(brush, 10f, 23f, 8f, 1.6f);
                }
            }
            return fallback;
        }


        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            double v = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            int u = -1;
            do
            {
                v /= 1024.0;
                u++;
            } while (v >= 1024 && u < units.Length - 1);
            return v.ToString("0.#") + " " + units[u];
        }

        // ---- 布局 ----

        private void LayoutChildren()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            int x = w - 8;
            int y = 7;
            // 标题栏右边只留展开/收起的箭头：最小化和关闭都挪到右键菜单
            // 与托盘菜单里去了（它们本来就是「误点就找不回来」的按钮）。
            _collapseButton.SetBounds(x - 34, y, 34, 30);
            x -= 34 + 6;
            if (!_collapsed)
            {
                _organizeButton.SetBounds(x - 82, y, 82, 30); x -= 82 + 6;
                _opacityButton.SetBounds(x - 68, y, 68, 30); x -= 68 + 6;
                _viewButton.SetBounds(x - 38, y, 38, 30);
            }
            // 标题说明文字只能写到最左按钮之前，窄面板上不会压到按钮。
            _titleRight =
                (_collapsed ? _collapseButton.Left : _viewButton.Left) - 12;

            int listTop = TitleH + TabBarH +
                (_opacityPanelVisible ? PanelH : 0);
            _opacityPanel.SetBounds(0, TitleH + TabBarH, w, PanelH);
            // 滑块跟面板宽度走：以前写死 360，窄面板会被裁掉一截。
            int trackW = Math.Max(100, w - 108 - 46);
            _opacityTrack.Size = new Size(trackW, 30);
            _opacityValue.Location = new Point(108 + trackW + 8, 19);
            _opacityPanel.Visible = _opacityPanelVisible && !_collapsed;

            int listH = h - listTop - StatusH;
            if (listH < 60)
            {
                listH = 60;
            }
            _list.SetBounds(0, listTop, w - ScrollW, listH);
            _scroll.SetBounds(w - ScrollW, listTop, ScrollW, listH);
            _statusLabel.SetBounds(12, h - StatusH + 4, w - 24, StatusH - 6);
            _list.Visible = !_collapsed;
            _statusLabel.Visible = !_collapsed;

            UpdateScroll();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_resident && WindowState == FormWindowState.Minimized)
            {
                // Win+D / Win+M 会把所有顶层窗口一起最小化，常驻面板要是
                // 跟着一起消失，用户就再也见不到它了（「关个窗口面板也
                // 没了」有一部分就是这么来的）。立刻恢复，并保持压底。
                WindowState = FormWindowState.Normal;
                SendToDesktopLayer();
                return;
            }
            if (_list != null)
            {
                // 拖动期间每帧 SetWindowRgn 会闪，Region 已在 BeginResize
                // 摘掉，松手再恢复。
                if (!_resizing)
                {
                    ApplyRoundedRegion();
                }
                LayoutChildren();
            }
            // 底板是按面板大小抓的，尺寸一变就得重抓重混；但系统
            // sizing 循环里每一帧都抓屏会卡成幻灯片，这里只重排 +
            // 重绘（文件列表行数当场跟着变），松手再补抓一张准的。
            if (_backdropReady)
            {
                if (!_sizing)
                {
                    RebuildBackdrop();
                }
                Invalidate(true);
            }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            // 底板记的是「屏幕这块位置上的桌面」，面板一挪，透出来的
            // 画面就会错位。但抓屏一次要几十毫秒，拖动时每帧重抓会卡，
            // 所以拖动期间只重绘（DrawBackdrop 会按位移平移旧底板），
            // 松手后再抓一张准的。
            if (!_backdropReady)
            {
                return;
            }
            if (_dragging || _sizing)
            {
                Invalidate(true);
                return;
            }
            RebuildBackdrop();
            Invalidate(true);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // 重新打开面板时桌面可能已经换了壁纸 / 挪了图标。
            if (Visible)
            {
                SendToDesktopLayer();
                if (_backdropReady)
                {
                    RebuildBackdrop();
                    Invalidate(true);
                }
            }
        }

        /// <summary>
        /// 常驻面板显示出来之后压到 z-order 最底：它是桌面上的小部件，
        /// 不该盖在用户正在用的窗口上面。
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SendToDesktopLayer();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyToolWindow(_resident);
            ApplyRoundedRegion();
        }

        /// <summary>
        /// 常驻面板出现时不要把焦点从用户正在用的窗口抢走（开机自启那
        /// 一下尤其明显）。
        /// </summary>
        protected override bool ShowWithoutActivation
        {
            get { return _resident; }
        }

        // ---- 滚动 ----

        private int ContentHeight()
        {
            if (_items == null || _items.Count == 0)
            {
                return 0;
            }
            if (_view == ViewModeKind.List)
            {
                return _items.Count * ListRowH;
            }
            int cols = IconCols();
            int rows = (int)Math.Ceiling((double)_items.Count / cols);
            return rows * IconCellH;
        }

        private int IconCols()
        {
            int avail = _list.ClientSize.Width - ScrollW - Pad * 2;
            if (avail < IconCellW)
            {
                return 1;
            }
            return Math.Max(1, avail / IconCellW);
        }

        private int ScrollOffset()
        {
            int max = Math.Max(0,
                ContentHeight() - _list.ClientSize.Height);
            return Math.Min(_scroll.Value, max);
        }

        private void UpdateScroll()
        {
            if (_scroll == null || _list == null)
            {
                return;
            }
            int content = ContentHeight();
            int view = _list.ClientSize.Height;
            int max = Math.Max(0, content - view);
            _scroll.Maximum = content;
            _scroll.LargeChange = Math.Max(1, view);
            _scroll.SmallChange = (_view == ViewModeKind.List)
                ? ListRowH : IconCellH;
            _scroll.Visible = max > 0 && !_collapsed;
            if (_scroll.Value > max)
            {
                _scroll.Value = max;
            }
            else if (_scroll.Value < 0)
            {
                _scroll.Value = 0;
            }
            _list.Invalidate();
        }

        private void SetScroll(int v)
        {
            int max = Math.Max(0,
                ContentHeight() - _list.ClientSize.Height);
            _scroll.Value = Math.Max(0, Math.Min(max, v));
            _list.Invalidate();
        }

        // ---- 列表绘制 ----

        private void OnListPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // 列表区占面板绝大部分，底板必须从这里透出去；文件行、图标、
            // 文字都是之后用不透明画上去的，所以文件始终是清楚的。
            DrawBackdrop(g, _list.ClientRectangle, _list.Location);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_items == null || _items.Count == 0)
            {
                string hint = "「" + _activeCategory + "」分类下暂时没有文件";
                DesktopGlass.Text2(g, hint,
                    DesktopGlass.FontBody, DesktopGlass.TextFaint,
                    _list.ClientRectangle,
                    DesktopGlass.CenterMiddle);
                return;
            }

            int offset = ScrollOffset();
            if (_view == ViewModeKind.List)
            {
                DrawList(g, offset);
            }
            else
            {
                DrawIcon(g, offset);
            }
        }

        private void DrawList(Graphics g, int offset)
        {
            int w = _list.ClientSize.Width - ScrollW;
            for (int i = 0; i < _items.Count; i++)
            {
                int y = -offset + i * ListRowH;
                if (y + ListRowH < 0)
                {
                    continue;
                }
                if (y > _list.ClientSize.Height)
                {
                    break;
                }
                Rectangle row = new Rectangle(0, y, w, ListRowH);
                if (i == _selectedIndex)
                {
                    using (SolidBrush b =
                        new SolidBrush(DesktopGlass.AccentSoft))
                    {
                        g.FillRectangle(b, row);
                    }
                }
                Item it = _items[i];
                if (it.Icon != null)
                {
                    // 行高 30，图标留 26 才不会顶到上下边框（32 会溢出 1px）。
                    const int ls = 26;
                    g.DrawImage(it.Icon,
                        new Rectangle(12, y + (ListRowH - ls) / 2, ls, ls));
                }
                Rectangle nameR = new Rectangle(
                    50, y, w - 50 - 188, ListRowH);
                DesktopGlass.Text2(g, it.Name, DesktopGlass.FontBody, DesktopGlass.Text,
                    nameR, DesktopGlass.LeftMiddleClip);
                Rectangle sizeR = new Rectangle(
                    w - 188, y, 96, ListRowH);
                DesktopGlass.Text2(g,
                    it.IsDirectory ? "" : FormatSize(it.Size),
                    DesktopGlass.FontSmall, DesktopGlass.TextDim,
                    sizeR, DesktopGlass.RightMiddle);
                Rectangle modR = new Rectangle(
                    w - 84, y, 80, ListRowH);
                DesktopGlass.Text2(g,
                    it.Modified.ToString("MM-dd HH:mm"),
                    DesktopGlass.FontSmall, DesktopGlass.TextFaint,
                    modR, DesktopGlass.RightMiddle);
                if (i == _selectedIndex)
                {
                    using (Pen p = new Pen(DesktopGlass.Accent, 1f))
                    {
                        g.DrawRectangle(p, 0, y, w - 1, ListRowH - 1);
                    }
                }
                else
                {
                    using (Pen p = new Pen(DesktopGlass.BorderSoft))
                    {
                        g.DrawLine(p, 10, y + ListRowH - 1, w, y + ListRowH - 1);
                    }
                }
            }
        }

        private void DrawIcon(Graphics g, int offset)
        {
            int cols = IconCols();
            int cellW = IconCellW;
            int cellH = IconCellH;
            int x0 = Pad;
            int y0 = Pad - offset;
            for (int i = 0; i < _items.Count; i++)
            {
                int col = i % cols;
                int rowN = i / cols;
                int x = x0 + col * cellW;
                int y = y0 + rowN * cellH;
                if (y + cellH < 0)
                {
                    continue;
                }
                if (y > _list.ClientSize.Height)
                {
                    break;
                }
                Rectangle cell = new Rectangle(x, y, cellW, cellH);
                if (i == _selectedIndex)
                {
                    using (SolidBrush b =
                        new SolidBrush(DesktopGlass.AccentSoft))
                    using (GraphicsPath p = DesktopGlass.Rounded(cell, 8))
                    {
                        g.FillPath(b, p);
                    }
                }
                Item it = _items[i];
                if (it.Icon != null)
                {
                    // 图片是缩略图，放大到 40 才看得清内容；普通文件图标
                    // 也是 40，格子看着更整。
                    int ix = x + (cellW - IconPx) / 2;
                    g.DrawImage(it.Icon,
                        new Rectangle(ix, y + GridIconY, IconPx, IconPx));
                }
                else
                {
                    // 缩略图还没解出来：先画个占位块，位置和尺寸跟真图完全
                    // 一致，补上来时不会跳动。
                    Rectangle box = new Rectangle(
                        x + (cellW - IconPx) / 2, y + GridIconY,
                        IconPx, IconPx);
                    using (GraphicsPath bp = DesktopGlass.Rounded(box, 5f))
                    using (SolidBrush brush = new SolidBrush(DesktopGlass.Sunken))
                    {
                        g.FillPath(brush, bp);
                    }
                }
                Rectangle label = new Rectangle(
                    x + 4, y + GridLabelY, cellW - 8, GridLabelH);
                DesktopGlass.Text2(g, it.Name, DesktopGlass.FontSmall, DesktopGlass.Text,
                    label,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.Top |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.WordBreak);
            }
        }

        // ---- 交互 ----

        private void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int idx = HitTest(e.Location);
                _selectedIndex = idx;
                _list.Invalidate();
                return;
            }
            int index = HitTest(e.Location);
            if (index != _selectedIndex)
            {
                _selectedIndex = index;
                _list.Invalidate();
            }
        }

        private void OnListDoubleClick(object sender, MouseEventArgs e)
        {
            int index = HitTest(e.Location);
            if (index >= 0)
            {
                OpenItem(_items[index]);
            }
        }

        private int HitTest(Point p)
        {
            if (_items == null || _items.Count == 0)
            {
                return -1;
            }
            int offset = ScrollOffset();
            if (_view == ViewModeKind.List)
            {
                int w = _list.ClientSize.Width - ScrollW;
                int row = (p.Y + offset) / ListRowH;
                if (p.X >= 0 && p.X < w && row >= 0 && row < _items.Count)
                {
                    return row;
                }
                return -1;
            }
            int cols = IconCols();
            int col = (p.X - Pad) / IconCellW;
            int rowN = (p.Y + offset - Pad) / IconCellH;
            int cand = rowN * cols + col;
            if (col >= 0 && col < cols && cand >= 0 && cand < _items.Count)
            {
                return cand;
            }
            return -1;
        }

        private void OpenSelected()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                OpenItem(_items[_selectedIndex]);
            }
        }

        private static void OpenItem(Item it)
        {
            try
            {
                Process.Start(it.FullPath);
            }
            catch (Exception ex)
            {
                Log.Error("打开文件失败: " + it.FullPath, ex);
            }
        }

        private void OpenLocation()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
            {
                return;
            }
            try
            {
                Process.Start("explorer.exe",
                    "/select,\"" + _items[_selectedIndex].FullPath + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("打开所在文件夹失败", ex);
            }
        }

        private void UpdateViewButton()
        {
            _viewButton.Kind = (_view == ViewModeKind.List)
                ? GlyphKind.Grid
                : GlyphKind.List;
            UpdateViewTooltip();
        }

        private void UpdateViewTooltip()
        {
            _viewButton.Kind = (_view == ViewModeKind.List)
                ? GlyphKind.Grid
                : GlyphKind.List;
        }

        // ---- 透明度 ----

        /// <summary>
        /// 调整**底板**的透明比例。窗体 Opacity 始终是 1——整窗透明会把
        /// 文件名、图标一起淡化，滑到 30% 时根本看不清文件叫什么。
        /// </summary>
        private void ApplyOpacity(double value)
        {
            double v = Math.Max(0.3, Math.Min(1.0, value));
            _bgAlpha = v;
            if (Opacity != 1.0)
            {
                Opacity = 1.0;
            }
            if (_opacityTrack.Value != (int)Math.Round(v * 100.0))
            {
                _opacityTrack.Value = (int)Math.Round(v * 100.0);
            }
            // 滑块数值越低 → 底板 alpha 越小 → 整块面板越透。
            UpdateGlassOverlay();
            RebuildBackdrop();
            Invalidate(true);
            UpdateOpacityValue();
        }

        private void UpdateOpacityValue()
        {
            if (_opacityValue != null)
            {
                _opacityValue.Text =
                    ((int)Math.Round(_bgAlpha * 100.0)) + "%";
            }
        }

        // ---- 底板：桌面背景 + 面板底色按比例混合 ----

        /// <summary>
        /// 把抓到的桌面背景做成磨砂：缩到 1/16 再双三次放大回来，高对比
        /// 的壁纸纹理被抹成柔和色块（廉价版 Acrylic 模糊）。只在重做底板
        /// 时算一次，平时重绘直接贴现成的 _backdrop，不会拖慢拖动。
        /// </summary>
        private static void DrawFrosted(Graphics g, Bitmap shot, Size size)
        {
            int sw = Math.Max(2, size.Width / 16);
            int sh = Math.Max(2, size.Height / 16);
            using (Bitmap small = new Bitmap(
                sw, sh, PixelFormat.Format32bppPArgb))
            {
                using (Graphics sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode =
                        InterpolationMode.HighQualityBicubic;
                    sg.DrawImage(shot, new Rectangle(0, 0, sw, sh));
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(small,
                    new Rectangle(0, 0, size.Width, size.Height));
            }
        }

        /// <summary>
        /// 重做底板位图。只在透明度 / 位置 / 尺寸 / 配色变化时调用，
        /// 平时重绘只是把现成的位图贴上去。
        /// </summary>
        private void RebuildBackdrop()
        {
            Size size = ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            Bitmap shot = null;
            if (_bgAlpha < 0.999 && !_backdropUnavailable)
            {
                Rectangle screenRect = new Rectangle(ScreenOrigin, size);
                shot = BackdropCapture.Capture(screenRect, Handle);
                if (shot != null && !BackdropCapture.LooksUsable(shot))
                {
                    shot.Dispose();
                    shot = null;
                    _backdropUnavailable = true;
                }
            }

            Bitmap next = null;
            try
            {
                next = new Bitmap(size.Width, size.Height,
                    PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(next))
                {
                    if (shot != null)
                    {
                        // 参考右边那种磨砂玻璃：壁纸先模糊，别让高对比的
                        // 波纹直接压在文件名底下。
                        DrawFrosted(g, shot, size);
                        // 把透出来的桌面压成暗色磨砂：右边那种背景是暗
                        // 玻璃，亮壁纸原样透出来会把白字吃掉。压暗量是
                        // 固定的，玻璃层越薄就越能看出「透」。
                        using (SolidBrush shade = new SolidBrush(
                            Color.FromArgb(64, Color.Black)))
                        {
                            g.FillRectangle(shade,
                                0, 0, size.Width, size.Height);
                        }
                    }
                    else
                    {
                        g.Clear(DesktopGlass.Window);
                    }
                    // 唯一一层底色：整块面板共用。滑块数值越低 → 玻璃
                    // alpha 越小 → 越透；曲线略陡，低档位差异才看得出来。
                    // 100% 时是一整块实色，不是清透的壁纸。
                    int a = (int)Math.Round(
                        Math.Pow(_bgAlpha, 1.3) * 255.0);
                    using (SolidBrush b = new SolidBrush(
                        Color.FromArgb(a, DesktopGlass.Window)))
                    {
                        g.FillRectangle(b, 0, 0, size.Width, size.Height);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("生成面板底板失败", ex);
                if (next != null)
                {
                    next.Dispose();
                    next = null;
                }
            }
            finally
            {
                if (shot != null)
                {
                    shot.Dispose();
                }
            }

            Bitmap old = _backdrop;
            _backdrop = next;
            _backdropReady = next != null;
            _backdropOrigin = ScreenOrigin;
            if (old != null)
            {
                old.Dispose();
            }
        }

        /// <summary>
        /// 把底板贴到目标 Graphics 上。<paramref name="offset"/> 是目标控件
        /// 在窗体里的位置（子控件绘制时要减掉）。
        /// </summary>
        private void DrawBackdrop(Graphics g, Rectangle dest, Point offset)
        {
            Bitmap bmp = _backdrop;
            if (bmp == null)
            {
                using (SolidBrush brush = new SolidBrush(DesktopGlass.Window))
                {
                    g.FillRectangle(brush, dest);
                }
                return;
            }
            // 拖动过程中不重抓底板（一次抓屏 + DWM 同步要 70ms，拖起来
            // 会卡成幻灯片），改成按位移平移旧底板：壁纸是静态的，平移
            // 后的位置正好对得上，只有新露出来的边是底色，松手立刻重抓。
            int shiftX = ScreenOrigin.X - _backdropOrigin.X;
            int shiftY = ScreenOrigin.Y - _backdropOrigin.Y;
            Rectangle src = new Rectangle(
                offset.X + shiftX, offset.Y + shiftY,
                dest.Width, dest.Height);
            Rectangle valid = Rectangle.Intersect(src, new Rectangle(
                0, 0, bmp.Width, bmp.Height));
            if (valid.Width <= 0 || valid.Height <= 0)
            {
                using (SolidBrush brush = new SolidBrush(DesktopGlass.Window))
                {
                    g.FillRectangle(brush, dest);
                }
                return;
            }
            if (valid.Width != dest.Width || valid.Height != dest.Height)
            {
                using (SolidBrush brush = new SolidBrush(DesktopGlass.Window))
                {
                    g.FillRectangle(brush, dest);
                }
            }
            g.DrawImage(bmp,
                new Rectangle(
                    dest.X + (valid.X - src.X),
                    dest.Y + (valid.Y - src.Y),
                    valid.Width, valid.Height),
                valid.X, valid.Y, valid.Width, valid.Height,
                GraphicsUnit.Pixel);
        }

        /// <summary>
        /// 叠一层薄色（hover / 选中页签用）。alpha 跟底板透明度联动，
        /// 颜色自带 alpha 时再按它折算，所以这些反馈也只是同一层白色
        /// 的不同浓度，不会冒出第二种底色。
        /// </summary>
        private void FillGlass(Graphics g, Rectangle rect, Color color)
        {
            int a = (int)Math.Round(_bgAlpha * 255.0);
            if (color.A < 255)
            {
                a = (int)Math.Round(a * color.A / 255.0);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(a, color)))
            {
                g.FillRectangle(b, rect);
            }
        }

        /// <summary>
        /// 子控件（按钮、状态栏、滑块面板、下拉条）只按窗体坐标贴同一块
        /// 底板，不再叠自己的底色——整块面板因此是严格同一个色。
        /// </summary>
        private void PaintGlassChild(Graphics g, Control child)
        {
            if (child == null)
            {
                return;
            }
            Point offset = PointToClient(child.PointToScreen(Point.Empty));
            DrawBackdrop(g, child.ClientRectangle, offset);
        }

        private void PersistOpacity()
        {
            // 只有托盘真正打开的这块面板才写盘。自检 / 探针会临时造表单
            // 并把透明度调到 50%，绝不能把用户的设置改成测试值。
            if (_instance != this)
            {
                return;
            }
            try
            {
                AppConfig config = ConfigStore.Load();
                config.DesktopManagerOpacity = Math.Round(_bgAlpha, 3);
                ConfigStore.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error("保存桌面管理透明度失败", ex);
            }
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = text;
            }
        }

        // ---- 标题栏拖动 / 绘制 ----

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 底板：抓来的桌面背景 + 面板底色；透明度 100% 时就是纯色。
            DrawBackdrop(g, ClientRectangle, Point.Empty);
            // 标题栏不再单独压一层玻璃：以前那样叠两次底色会让标题栏比
            // 列表区更实，整块面板看着分成两种颜色。现在共用同一块底板。
            int titleMax = Math.Max(80, _titleRight);
            Rectangle titleRect = new Rectangle(
                14, 0, titleMax, TitleH);
            DesktopGlass.Text2(g, "桌面整理", DesktopGlass.FontStrong, DesktopGlass.Text,
                titleRect, DesktopGlass.LeftMiddle);

            // 标题右侧补一行淡色说明：收起时是「共 N 项」，常驻时加个标记，
            // 一眼能看出面板现在处于什么状态。
            Size titleSize = TextRenderer.MeasureText(
                "桌面整理", DesktopGlass.FontStrong);
            int infoX = 14 + titleSize.Width + 10;
            string info = "";
            if (_collapsed)
            {
                // 短条只有 210px，说明文字再长就会被按钮截断成半截。
                info = _locked
                    ? "已锁定"
                    : "共 " + _allItems.Count + " 项";
            }
            else if (_locked)
            {
                info = "已锁定";
            }
            else if (_resident)
            {
                // 窄面板上标题栏右侧全是按钮，说明文字只能留两三个字。
                info = "常驻";
            }
            if (info.Length > 0 && infoX < titleMax)
            {
                DesktopGlass.Text2(g, info, DesktopGlass.FontTiny, DesktopGlass.TextFaint,
                    new Rectangle(infoX, 0, titleMax - infoX, TitleH),
                    DesktopGlass.LeftMiddleClip);
            }

            using (Pen p = new Pen(DesktopGlass.BorderSoft))
            {
                g.DrawLine(p, 0, TitleH, Width, TitleH);
            }
            if (!_collapsed)
            {
                DrawTabs(g);
            }
        }

        // ---- 分类页签 ----

        private Rectangle TabBarRect
        {
            get { return new Rectangle(0, TitleH, Width, TabBarH); }
        }

        private Rectangle TabRect(int index)
        {
            if (Width < CompactTabBelow)
            {
                // 竖长形面板放不下「按文字宽度 + 间距」排的六个页签，
                // 改成等分整行；绘制和命中测试都走这里，不会错位。
                int each = (Width - Pad * 2) / Categories.Length;
                return new Rectangle(
                    Pad + index * each, TitleH, each, TabBarH);
            }
            int x = Pad;
            for (int i = 0; i < index; i++)
            {
                Size s = TextRenderer.MeasureText(
                    Categories[i], DesktopGlass.FontBody);
                x += s.Width + 8 + 22;
            }
            Size sz = TextRenderer.MeasureText(
                Categories[index], DesktopGlass.FontBody);
            return new Rectangle(x, TitleH, sz.Width + 22, TabBarH);
        }

        private void DrawTabs(Graphics g)
        {
            // 页签栏也不再单独铺色，跟整块面板共用同一块底板；选中 /
            // hover 只是薄薄一层白色，不会分出第二种底色。
            using (Pen p = new Pen(DesktopGlass.BorderSoft))
            {
                g.DrawLine(p, 0, TitleH + TabBarH - 1,
                    Width, TitleH + TabBarH - 1);
            }
            for (int i = 0; i < Categories.Length; i++)
            {
                string cat = Categories[i];
                Rectangle r = TabRect(i);
                bool active = cat == _activeCategory;
                bool hover = cat == _hoverCategory && !active;
                // 选中态只靠白色下划线 + 亮一档的白字区分，不铺色块，
                // 整块面板才始终是同一个底色；hover 留一层极淡的白。
                if (hover && !active)
                {
                    FillGlass(g, r, DesktopGlass.Card);
                }
                int count = CountInCategory(cat);
                // 0 项的分类不写数字，颜色也压淡一档，一眼能看出是空页签。
                string text = (count > 0)
                    ? cat + " " + count : cat;
                DesktopGlass.Text2(g, text, DesktopGlass.FontBody,
                    active ? DesktopGlass.Text
                        : (count > 0 ? DesktopGlass.TextDim : DesktopGlass.TextFaint),
                    r, DesktopGlass.CenterMiddle);
                if (active)
                {
                    using (Pen p = new Pen(DesktopGlass.Accent, 2f))
                    {
                        g.DrawLine(p,
                            r.X + 4, r.Bottom - 2,
                            r.Right - 4, r.Bottom - 2);
                    }
                }
            }
        }

        private string HitTestTab(Point p)
        {
            if (p.Y < TitleH || p.Y >= TitleH + TabBarH)
            {
                return null;
            }
            for (int i = 0; i < Categories.Length; i++)
            {
                if (TabRect(i).Contains(p))
                {
                    return Categories[i];
                }
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                int hit = ResizeHitAtClient(e.Location);
                if (hit != 0)
                {
                    BeginResize(hit);
                    return;
                }
                string tab = HitTestTab(e.Location);
                if (tab != null)
                {
                    SelectCategory(tab);
                    return;
                }
                if (e.Y <= TitleH)
                {
                    BeginDrag();
                }
            }
        }

        /// <summary>
        /// 开始拖动标题栏。锁定位置时返回 false（面板钉在桌面上不动）。
        /// </summary>
        private bool BeginDrag()
        {
            if (_locked)
            {
                return false;
            }
            _dragging = true;
            _dragStart = Cursor.Position;
            _dragOrigin = Location;
            return true;
        }

        /// <summary>自检用：走和鼠标拖动完全同一条路径。</summary>
        internal bool BeginDragForTest()
        {
            return BeginDrag();
        }

        /// <summary>自检用：按给定的位移拖动（没在拖动状态则无效果）。</summary>
        internal void MoveDragForTest(int dx, int dy)
        {
            if (!_dragging)
            {
                return;
            }
            Location = new Point(_dragOrigin.X + dx, _dragOrigin.Y + dy);
            _dragging = false;
            if (_backdropReady)
            {
                RebuildBackdrop();
                Invalidate(true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_resizing)
            {
                UpdateResize(Cursor.Position);
                return;
            }
            if (_dragging)
            {
                Point now = Cursor.Position;
                Location = new Point(
                    _dragOrigin.X + now.X - _dragStart.X,
                    _dragOrigin.Y + now.Y - _dragStart.Y);
                return;
            }
            UpdateResizeCursor(ResizeHitAtClient(e.Location));
            string hover = HitTestTab(e.Location);
            if (hover != _hoverCategory)
            {
                _hoverCategory = hover;
                Invalidate(TabBarRect);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_resizing)
            {
                EndResize();
                return;
            }
            bool wasDragging = _dragging;
            _dragging = false;
            // 松手补一张准的底板：拖动期间用的是平移的旧图。
            if (wasDragging && _backdropReady)
            {
                RebuildBackdrop();
                Invalidate(true);
            }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            Point point = PointToClient(Cursor.Position);
            // 双击标题栏 = 收起 / 展开，和手机上的小部件手势一致。
            if (point.Y <= TitleH)
            {
                ToggleCollapsed();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_resizing && Cursor != Cursors.Default)
            {
                Cursor = Cursors.Default;
            }
            if (_hoverCategory != null)
            {
                _hoverCategory = null;
                Invalidate(TabBarRect);
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5)
            {
                Reload();
                SetStatus("已刷新");
                e.Handled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 常驻模式下点关闭只是把面板收起来（不是退出常驻），下次
            // 开机还会回来；真要不常驻请用托盘 / 菜单里的开关。
            if (_resident &&
                e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SavePanelState();
                Hide();
                return;
            }
            base.OnFormClosing(e);
            _hoverTimer.Stop();
            _hoverTimer.Dispose();
            _zOrderTimer.Stop();
            _zOrderTimer.Dispose();
            SavePanelState();
            PersistOpacity();
            _iconGeneration++;
            DisposeItems(_allItems);
            _allItems = new List<Item>();
            _items = new List<Item>();
            DisposeBackdrop();
        }

        private void DisposeBackdrop()
        {
            Bitmap old = _backdrop;
            _backdrop = null;
            _backdropReady = false;
            if (old != null)
            {
                old.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeBackdrop();
            }
            base.Dispose(disposing);
        }

        /// <summary>双缓冲面板，避免自绘列表在滚轮滚动时闪白。</summary>
        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                DoubleBuffered = true;
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer,
                    true);
            }
        }

        /// <summary>
        /// 双缓冲标签。拖边框 / 拖动面板时状态栏、透明度面板每帧重绘，
        /// Label 默认先擦背景再画，不缓冲就是一片片白闪。
        /// </summary>
        private sealed class DoubleBufferedLabel : Label
        {
            public DoubleBufferedLabel()
            {
                DoubleBuffered = true;
            }
        }
    }
}
