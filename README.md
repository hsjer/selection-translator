# 工具侠（Windows 托盘小工具：翻译 · 截图识字 · 语音输入 · 图片工具 · 软件下载）

> 仓库：https://github.com/hsjer/toolbox-hero

一个轻量的 Windows 托盘小工具**工具侠**（原「选中文本翻译」）：在任意应用里选中文字，旁边会自动浮出一个「译」按钮，点击即可用百度翻译 API 翻译。也支持右键、全局快捷键，以及**截图翻译**（框选屏幕 → 就地标注 → 识别图中文字并把译文直接显示在图片上，可选中复制）。截图后也可以**只取文字**：一键把 OCR 识别出的原文复制到剪贴板，不弹窗口、不显示译文。

图片文字处理采用**双服务分工**：**腾讯云 GeneralAccurateOCR** 负责识别文字和坐标，**百度通用文本翻译**负责把识别出的文字逐行翻译；两者拼在一起把译文叠加回原图。只取字时只调腾讯云 OCR，完全不调用翻译接口。未配置腾讯云凭证（或在设置里关闭开关）时自动回退到百度图片翻译接口。

此外还带了一个**轻量图片工具**：托盘菜单「打开图片…」、在图片上**右键选「使用工具侠打开图片」**，或把图片拖到 exe 上，即可用深色查看器看图，并就地完成格式转换、压缩、改尺寸、重命名、旋转、裁剪与另存（见下文「图片工具」）。

启动后还会在**任务栏托盘区左侧显示一条实时状态**：上下行网速、CPU、内存占用，只有文字、不带底色块；位置随托盘图标增减、任务栏位置、分辨率 / DPI 自动重算，可在托盘菜单「任务栏状态显示」里随时关掉。网速**单位统一按 MB/s 算**（≥1000 MB/s 用 GB/s，详情卡片里仍显示精确的 B/KB 值），两行共用同一组列起点，位图紧贴内容、间隙很小，数字跳动也不会左右抖或上下错开。**鼠标移到这条状态上**会在**右上方**弹出一张紧凑的圆角详情卡片，显示今日累计流量、实时网速、CPU 占用与频率、内存用量、磁盘剩余、显卡利用率、电池状态；鼠标移开即自动收起。卡片同样是透明分层窗口、鼠标穿透，不挡任务栏点击，也不抢焦点。

还内置了**剪贴板历史**：托盘菜单「剪贴板历史…」、`Ctrl+Alt+C`，或**快速连按两下 Ctrl**（左右 Ctrl 都行，默认开启，可在托盘菜单 / 设置里关）打开，自动记录复制过的**文本 / 图像 / 文件**三类内容，按日期分组、最新在最上；顶部可搜索文本、文件名与图片尺寸，标签页可切「全部 / 文本 / 图像 / 文件 / 收藏」。**双击任一条即写回系统剪贴板**（Enter 同效），右键可收藏、删除、打开 / 定位；收藏的条目不受保留时长限制。保留时长（TTL）可选 1 小时 / 6 小时 / 1 天 / 3 天 / 7 天 / 30 天 / 永久，在窗口右侧「设置」里随时改，托盘「设置」里还能改监听开关、最多条数和快捷键。历史只落在本机 `%APPDATA%\SelectionTranslator\clipboard.json`，图片存 `clipboard-images\*.png`（长边缩到 1600px），不上传任何数据；不想要时可以一键暂停记录或清空。

**图片能直接粘贴到桌面 / 资源管理器**：普通剪贴板只写 `CF_DIB` 位图，而桌面（Shell）不收位图、只认 `CF_HDROP` 文件列表，所以大多数剪贴板工具的图片在桌面上「粘贴」是灰的。工具侠写回图片时**同时挂上「位图 + 真实 PNG 文件」两种格式**：在聊天框 / 画图里还是粘成图，在桌面或文件夹里按 `Ctrl+V` 就会生成一个 `图片-时间戳.png` 文件；文本条目也能右键「复制为文件」粘成 `.txt`。**截图、桌面贴图 `Ctrl+C`、图片查看器「复制」都走同一套写法**，所以截图同样能直接粘到桌面；这些临时文件放在 `clipboard-paste\`，两小时后自动清理（剪贴板里引用着的那份不会立刻删）。如果某些软件偏好把图片当附件发，右键改选「复制为纯图片」即可。可粘贴的临时文件放在 `clipboard-paste\`，两小时后自动清理。

## 为什么不是系统右键菜单

Windows 没有公开接口可以往**所有应用**的文本右键菜单里统一插入菜单项。各应用的右键菜单由应用自己绘制，无法安全地全局注入。

所以本工具采用：

- **选中即显示悬浮按钮**：轮询检测当前焦点元素的选区（带防抖），选中稳定后自动在鼠标附近显示「译」按钮，无需右键。点击按钮即翻译；选区清空或切换后按钮自动消失。
- **右键悬浮按钮**（可关闭）：右键按下时通过 Windows UI Automation 读取选中文本；读不到时自动用一次 `Ctrl+C` 兜底（会备份并恢复原剪贴板）。读到文本后，在鼠标附近显示「译」按钮，并**保留应用自己的右键菜单**；只有真正点击了「译」按钮才会关闭该菜单。
- **全局快捷键（可自定义）**：在设置中**点一下快捷键输入框就开始录入**——按住 Ctrl / Alt / Shift / Win 不松，再按一个键即可完成，过程中输入框实时显示 `Ctrl+…` 这类前缀；也可以只按 `F1`–`F12`、`PrintScreen` 等功能键。默认 `Ctrl+Alt+T` 翻译选中文本、`Ctrl+Alt+E` 手动翻译、`Ctrl+Alt+S` 截图、`Ctrl+Alt+V` 语音输入，四个快捷键的组合不能完全相同。托盘菜单每次展开都回读当前配置，右侧显示的快捷键与设置保持一致；被其他程序占用时会标注「（被占用）」并弹气泡提示。

## 截图翻译

按 `Ctrl+Alt+S`（或托盘菜单「截图…」）进入全屏截图浮层：

1. **选区**：两种方式，随便哪种都行。
   - **直接点一个窗口**：鼠标移到目标窗口上，整个窗口会被高亮（按原图亮度重新绘出、描一圈蓝边，左上角标出尺寸和「单击截取此窗口」），**单击即把整个窗口作为选区**，不用沿着窗口边缘小心翼翼地圈。窗口边界取 DWM 的真实外框（`DWMWA_EXTENDED_FRAME_BOUNDS`），不会多出 Win10 那圈看不见的缩放手柄。
   - **自由框选**：按住鼠标**左键或右键**拖拽，松手即定。
   两种方式松开后选区都会锁定（一次快捷键只截一张图），光标由十字变回普通箭头，选区出现蓝色边框、8 个缩放手柄和 `宽 × 高` 尺寸标签，下方自动浮出图标工具栏。手柄上仍显示缩放光标，选区内拖动可移动选区；右键单击只退出当前标注工具，不会重新框选。想再截一张请重按 `Ctrl+Alt+S`。
2. **就地标注**：工具栏从左到右是矩形、椭圆、箭头、画笔、文字、序号、高亮、马赛克、撤销、裁剪、颜色、线宽、保存、钉在桌面、翻译、取字、取消、完成。`Ctrl+Z` 撤销，数字键 `1`–`8` 快速切换工具；颜色有 6 种、线宽 3 档。
3. **结束方式**：
   - **完成 ✓（对号）**：合成图（含标注）同时以「位图 + 真实 PNG 文件」两种格式写入剪贴板——聊天框 / 画图里粘成图，桌面或文件夹里按 `Ctrl+V` 直接生成 `图片-时间戳.png`；浮层随即关闭，**不弹出任何窗口**；
   - **译**：腾讯云 OCR 识别 → 百度文本翻译逐行翻译 → 打开轻量结果窗口，译文按坐标叠加在图片上，可在图上选字复制；
   - **取字 📄**：只调腾讯云 OCR（不翻译），打开同一个可选字结果窗口：默认只显示原图 + 文字块，**由你自己点选 / 框选要复制的文字**，不会一次性把全文塞进剪贴板；
   - **钉 📌**：把合成图钉在桌面上（无边框置顶小窗）；
   - **保存 ↓**：直接导出 PNG 并退出；
   - **取消 ✕ / Esc**：放弃本次截图。

> 所有标注都在浮层里就地完成。**旧版独立的截图编辑器窗口已经移除**，截图后不会再弹出带裁剪 / 画笔 / 马赛克的编辑界面。

> **窗口拾取规则**：按 Z 序找鼠标下第一个属于**其他进程**的可见顶层窗口，跳过桌面（`Progman` / `WorkerW`）、任务栏、工具窗口（输入法候选、浮动提示）、小于 24px 的条状窗口，以及 DWM 隐藏（cloaked）的幽灵窗口 —— 那些「Windows 输入体验」之类的全屏影子层会把真正的目标挡住。所以在桌面或任务栏上悬停不会高亮，直接拖框就好。

**桌面贴图**：左键拖动移动、`Ctrl` + 滚轮缩放、右键菜单可复制 / 保存 / 调整透明度 / 切换置顶 / 缩放 / 关闭，`Esc` 或双击关闭；鼠标悬停时右上角出现关闭按钮。托盘菜单「关闭所有桌面贴图」可一次清理。再次按截图快捷键时贴图会临时隐藏，不会被拍进新截图。翻译结果窗口的工具条里也有「钉在桌面」。

**图片翻译结果窗口**：腾讯云路径没有整图贴合译文图，窗口按腾讯云返回的文字坐标把逐行译文直接绘制到每个文字块上；回退百度图片翻译路径时则优先显示接口返回的 `pasteImg` 贴合译文图。窗口里没有任何标注 / 裁剪工具，工具条只有：显示译文图 / 原图对照、选字复制：原文 / 译文、复制选中、复制全部译文、复制图片、钉在桌面、保存 PNG、重新翻译、适应窗口、关闭。

**在图片上直接选字复制**：腾讯云 `TextDetections[].ItemPolygon`（缺失时用 `Polygon` 四点求外接矩形）会被转换成统一的 `rect`（`left top width height`），结果窗口把每段做成一个可点选 / 可框选的热区：

- **单击**某个文字块 → 选中并立即复制该段文字；
- **拖拽框选**多个文字块 → 按阅读顺序（先分行、再从左到右）拼接后复制；
- `Ctrl+A` 全选复制，`Ctrl+C` 重新复制当前选中内容，`Esc` 关闭窗口；
- 工具条「选字复制：原文 / 译文」可切换复制的是 OCR 原文还是译文（翻译完成默认复制译文）；
- 状态栏显示「已复制 N 段…」及内容预览；未选中时「复制选中」等价于全选；
- 中键或右键拖动平移图片，`Ctrl` + 滚轮缩放，「适应窗口」复位。

> 复制的是**文字**而不是图片像素；想复制译文图请用工具条「复制图片」。

### 只识别文字（不翻译）

点工具栏的「取字 📄」即进入 OCR 取字：把**未标注的原图**编码后送腾讯云 `GeneralAccurateOCR`，按阅读顺序拼出全文，并保留每行的 `TextDetections[].ItemPolygon` 坐标，然后打开**仅识别**的结果窗口 —— 界面只有原图和一层可选择的文字块：

- **单击**某个文字块 → 复制这一段；
- **拖拽框选**多个文字块 → 按阅读顺序拼接后复制；
- `Ctrl+A` 全选复制，`Ctrl+C` 复制选中，`Esc` 关闭；
- 工具条：复制全部原文、复制选中、复制图片、钉在桌面、保存 PNG、重新识别、适应窗口、关闭（**没有**译文相关按钮，也**不会**自动把全文写进剪贴板）。

适合「只想把图里的一部分字抠出来」的场景（复制报错信息的某一行、只取文档中的一段等）。

> **这一次是真的不翻译**：取字只调用腾讯云 OCR，**不发任何翻译请求**，也不消耗百度翻译额度。回退到百度图片翻译时才是旧行为（接口没有纯 OCR 开关，服务端仍会翻译并计费一次，只是忽略译文）。

**速度**：新管道把「OCR + 翻译」拆成两个独立服务，取字只发 1 次纯 OCR 请求；翻译则在 OCR 之后把全部文字行用**一次**百度文本翻译请求翻完（百度会把 `q` 里的换行拆成逐条 `trans_result`，行数一一对应），不再需要 `paste=1` 贴合图。实测同一张图：腾讯云取字约 **0.76s**，百度图片翻译回退约 **1.23s**。

体验上的细节：PNG/JPEG 编码放在线程池，浮层关闭后窗口立刻出现并显示「正在识别图片中的文字…」；识别完成后状态栏会写出段数、字符数与操作提示。`app.log` 会记录引擎、浮层收尾耗时与识别结果。

> 识别与翻译时发送的都是**未标注的原图**（浮层会单独保留一份干净裁剪图），避免矩形框、马赛克、文字被当成正文识别。腾讯云路径尽量不缩放以保证文字坐标一一对应，仅在超过 7M / 长边 4096px 时才降 JPEG 质量或等比缩放，并用实际上传尺寸做坐标映射；百度图片翻译路径沿用 ≤4M、长边 ≤4096px 的限制。

> **凭证**：腾讯云需在控制台开通「文字识别 OCR」并创建 SecretId / SecretKey（在设置里填写，SecretKey 用 DPAPI 加密保存）；百度只需「通用翻译」的 AppID / 密钥。只有回退到百度图片翻译时才需要单独开通百度的「图片翻译」服务。设置里的目标语言使用**文本翻译**语种代码（`ja`/`ko`/`fr`/`es`…），回退时会自动映射为图片接口的 `jp`/`kor`/`fra`/`spa`。

## 语音输入（按住说话，直接上屏）

和微信输入法的语音输入一样：**先把光标点进任意输入框，按住 `Ctrl+Alt+V`（可改）说话，松开后文字就直接出现在光标处**。全程没有结果窗口、没有按钮要按。

1. 按下快捷键只记录当时的焦点窗口（`GetGUIThreadInfo` 跨进程拿焦点控件），**不对输入框类型做任何限制**：不论记事本、Word、浏览器网页、微信/QQ 聊天框、Electron 应用、终端，还是没见过的自绘输入框，一律直接开始录音。已知控件类名只用于写日志，不再当准入白名单。
2. 录音时屏幕底部浮出状态条（红色脉冲点 + 实时电平 + 计时），它带 `WS_EX_NOACTIVATE`，**不抢焦点**，光标一直留在原输入框。
3. 松开快捷键 → 浮条变「识别中…」→ WAV 传给腾讯云 `SentenceRecognition` → 文字通过 `SendInput(KEYEVENTF_UNICODE)` 直接注入原输入框，浮条闪一下「✓ 已输入 N 字」后自动消失。

几个设计取舍：

- **唯一保留的兜底**：只有焦点明确是桌面 / 任务栏 / 弹出菜单这类系统窗口时才不模拟键入（打了也没用，还可能触发系统快捷键），识别结果自动放进剪贴板并提示 `Ctrl+V`；这不会阻止录音。
- **默认不碰剪贴板**：模拟键入逐字符送 UTF-16 码元，中文、emoji 代理对都能原样上屏，用户剪贴板内容不受影响。设置里可勾选「同时复制到剪贴板」。
- **个别软件不收 Unicode 注入**（终端、游戏、某些自绘控件）时，在设置的「上屏方式」里改成「剪贴板粘贴（兼容）」：临时借用剪贴板发 `Ctrl+V`，粘贴完成后自动恢复原内容。
- **切走窗口就安全放弃**：说话期间如果用户切到了别的窗口，程序不会把字打进新窗口，而是把结果放进剪贴板并提示手动粘贴。
- **修饰键先松开**：语音键是 `Ctrl+Alt+V`，注入前会等 Ctrl/Alt/Shift 释放，避免中文变成 `Ctrl+字母` 快捷键。
- **按住说话而不是点一下开始、再点一下停**：热键 API 只投递按下消息，拿不到松开消息，所以程序用 40ms 的定时器轮询 `GetAsyncKeyState`；松开即结束，最短 300ms，最长 60 秒自动收尾（接口上限）。
- **录音全程在内存里**：16kHz / 16bit / 单声道 PCM，60 秒约 1.9MB，在接口 3MB 限制内；不落盘（除非用 `--voicetest` 自测）。
- **不引入音频库**：直接 P/Invoke `winmm.dll` 的 `waveIn*`，保持「单文件、无第三方 DLL、拷走就能跑」。

> 识别走腾讯云**语音识别 ASR**，与文字识别共用同一对 SecretId / SecretKey，但需要在控制台**单独开通「语音识别」**，否则会返回 `FailedOperation.UserNotRegistered`。

## 图片工具（打开 / 转格式 / 压缩 / 改尺寸）

一个不联网、零依赖的深色图片查看器，专注「看图 + 改文件」这件小事。入口有四个：

- **在任意图片上右键 →「使用工具侠打开图片」**（默认开启，见下）；
- 托盘菜单「打开图片…」；
- 命令行 `工具侠.exe "D:\图片\a.png"`；
- **把图片直接拖到 exe 上**，或在资源管理器里「打开方式」选它 —— 后面这两种只开查看器窗口，不启动托盘常驻。

**界面**：无边框深色窗口（Win11 自动圆角），自绘标题栏显示文件名与 `3 / 19` 位置，中间是画布，右侧「图片信息」卡片，底部悬浮一条圆角图标工具条。全部图形都是 GDI+ 手绘矢量，不引入任何 UI 库或图片资源。

**右侧信息卡片**每行一个属性 + 一个就地按钮：

| 属性 | 按钮 | 作用 |
| --- | --- | --- |
| 名称 | 重命名 | 只改主文件名，扩展名不变；查重、非法字符当场提示 |
| 类型 | 转格式 | PNG / JPG / BMP / GIF / TIFF，实时预估转换后体积 |
| 文件大小 | 压缩 | JPEG 质量 + 可选最长边，实时预估「约 24 KB / 原图 16%」 |
| 尺寸 | 改尺寸 | 宽高输入 + 锁定比例 + 25/50/75/100/200% 快捷 |
| 位置 | 打开 | 在资源管理器里选中该文件 |
| 修改时间 | — | 只读 |

**底部工具条**：上一张 / 下一张（`←` `→`）、适应窗口（`Ctrl+0`）、实际大小 100%（`Ctrl+1`）、缩小 / 放大、左旋 / 右旋 90°、裁剪（`C`）、复制图片（`Ctrl+C`）、另存为（`Ctrl+S`）。

几个实现上的取舍：

- **不锁文件**：先把字节读进内存再解码，文件句柄立刻释放，所以「转格式 / 压缩 / 覆盖保存 / 重命名」都不会因为自己正显示这张图而失败。
- **编辑只在内存里**：旋转、裁剪、改尺寸默认只改画布，不碰磁盘；`Ctrl+Z` 可逐级撤销（最多 8 步），只有「另存 / 覆盖」才写文件。未保存就关窗或切图会先问一句。
- **不覆盖任何已有文件**：转换 / 压缩 / 改尺寸另存时用 `名字_后缀.ext`，同名自动加 `(1)`，绝不静默覆盖。
- **透明图有棋盘底**：画布在图片下方铺一层棋盘格，PNG 的透明区域一眼可见；透明度图转 JPEG 时自动铺白底。
- **缩放看像素**：滚轮以光标为中心缩放（2%–3200%），放大到 250% 以上切到最近邻插值，方便检查像素；双击画面在「适应窗口」与 100% 之间切换。
- **裁剪是真裁剪**：`C` 进入裁剪，拖出保留区域（框外压暗、实时显示像素尺寸），`Enter` 应用、`Esc` 取消，裁剪后可 `Ctrl+Z` 退回。
- **同目录浏览**：自动收集同文件夹的图片，用 `StrCmpLogicalW` 按资源管理器习惯排序，循环切换。

> 压缩固定输出 JPEG（体积收益最大）；动图 GIF 只处理第一帧；不支持 WebP / HEIC / AVIF（系统 GDI+ 没有对应编解码器，硬塞第三方库会破坏「单文件」）。

### 图片右键菜单 / 打开方式

第一次运行（或启动托盘）时，程序会在 **当前用户** 的注册表里挂**两处**入口：右键菜单里的「使用工具侠打开图片」，以及「**打开方式(H)**」子菜单里的「**工具侠**」一行（也可在「选择其他应用」里找到）。两处共用托盘里同一个开关，都**不改文件的默认打开方式**。

#### 1. 经典右键菜单

```text
HKEY_CURRENT_USER\Software\Classes\SystemFileAssociations\image\shell\ToolboxHero
  (默认)            = 使用工具侠打开图片
  Icon              = "<程序路径>",0
  MultiSelectModel  = Single
  command\(默认)     = "<程序路径>" "%1"
```

- 挂在 `SystemFileAssociations\image` 下，所以 png / jpg / jpeg / bmp / gif / tif / tiff 这些「图片」类型都能看到；
- **Windows 11 提示**：这一项属于经典菜单，Win11 默认收在「显示更多选项」（`Shift+F10` 或右键 → 显示更多选项）里。想让它出现在 Win11 新菜单的第一层，需要注册 `IExplorerCommand` COM 组件，跟「单文件绿色」冲突，所以没做 —— 但下方的「打开方式」入口不受这个限制。

#### 2. 「打开方式(H)」子菜单

按 Windows 的「应用程序注册」规范写三处，缺一个列表里就可能不显示：

```text
HKCU\Software\Classes\ToolboxHero.Image
  (默认)                     = 工具侠 图片文件
  Application\ApplicationName = 工具侠      ← 列表里显示的就是这一行字
  DefaultIcon\(默认)          = "<程序路径>",0
  shell\open\command\(默认)   = "<程序路径>" "%1"

HKCU\Software\Classes\Applications\工具侠.exe
  FriendlyAppName           = 工具侠
  shell\open\command\(默认)   = "<程序路径>" "%1"
  SupportedTypes            = .png .jpg .jpeg .bmp .gif .tif .tiff .webp

HKCU\Software\Classes\.png\OpenWithProgids      → 追加值 ToolboxHero.Image
HKCU\...\Explorer\FileExts\.png\OpenWithProgids → 追加值 ToolboxHero.Image
（8 种图片扩展名各写一次）
```

- 只往候选表里**追加自己那一个值**，别人注册的 ProgID 和 `UserChoice`（默认程序）一律不动；关掉开关只删自己的值和键；
- 写完调 `SHChangeNotify(SHCNE_ASSOCCHANGED)` 通知资源管理器刷新，通常右键立刻就能看到。偶尔 Explorer 缓存顽固，重启资源管理器或重新登录即可；
- **只写 HKCU，不需要管理员权限**，也不弹 UAC；托盘菜单「图片右键菜单 / 打开方式：工具侠」可以随时开关（前面带勾），状态每次都从注册表回读；
- 程序被挪了目录、改了名字，下次启动托盘会自动把两处路径一起修正过来。

等价的命令行（调试或脚本里用）：

```powershell
.\工具侠.exe --shellon      # 注册两处入口（指向当前 exe）
.\工具侠.exe --shelloff     # 两处一起移除
.\工具侠.exe --shellprobe   # 打印两处的注册状态、8 个扩展名的候选表
.\工具侠.exe --assocprobe   # 让 Shell 亲自解析 ProgID，确认「打开方式」认得它
```

## 软件下载（360 宝库）

托盘图标上**右键 →「软件下载…」**，弹出深色小窗口：输入软件名回车，列表就出来了，**点一下任意一项直接开始下载安装包**，不弹任何确认框、也不跳浏览器。

和网页版的区别在「少走两步、少装一个东西」：

- 网页里要先点进详情页、翻到底部才有「普通下载」；这里搜索结果里每一行的下载地址就是那个「普通下载」直链（接口字段 `soft_download`），点一下等于替你点完了「普通下载」。
- **不会顺带装 360 卫士**：网页上的绿色「安全下载」走的是 `safe_download`（先装 360 软件管家 / 卫士再装软件），本工具刻意不用它。行内也没有「安全下载」入口，避免误点。
- 安装包默认存到**用户「下载」文件夹**（`%USERPROFILE%\Downloads`，没有这个目录时退回桌面），文件名形如 `小红书手机版_2000004652.exe`；重名自动加 `(1)`。
- 下载中行内胶囊显示百分比 + 细进度条，底部状态栏显示「已下载 / 总大小 · 速度 · 剩余时间」；**右键可取消**、复制下载地址、在浏览器打开详情页；下完点那一行会直接在资源管理器里定位到文件。
- 搜索走 `https://soft-api.safe.360.cn/main/v1/soft/search?keyword=…&page=N`（网页是前端渲染，抓 HTML 拿不到结果）。结果超过一页时底部出现「加载更多」。软件图标异步加载，取不到时用首字色块占位。
- 只发一个搜索关键词，不上传任何本机信息；下载是 360 / 厂商 CDN 的直链，工具只负责把文件存下来，不做静默安装。

> 接口属于第三方服务，360 若调整字段或加风控可能失效；此时窗口底部会显示具体错误（超时 / 连不上 / 接口返回错误），不会静默失败。

## 功能

- 任意应用选中文本 → 自动浮出「译」按钮，点击即翻译（无需右键）
- 全局快捷键翻译选中文本（默认 `Ctrl+Alt+T`，可在设置中自定义）
- 全局快捷键打开手动翻译窗口（默认 `Ctrl+Alt+E`，可在设置中自定义）
- 全局快捷键截图（默认 `Ctrl+Alt+S`，可在设置中自定义）
- 全局快捷键语音输入（默认 `Ctrl+Alt+V`）：不限制输入框类型，按住说话、松开即识别，文字直接上屏，不弹结果窗口
- 截图浮层：左键/右键框选、选区锁定、8 手柄调整、尺寸标签、18 个线性图标就地标注（含序号、马赛克、裁剪、贴图、取字）
- 截图可以直接点一个窗口整窗截取：悬停高亮 + DWM 真实外框，跳过桌面 / 任务栏 / 工具窗口 / 幽灵层
- 桌面贴图：置顶无边框小窗，可拖动、缩放、调透明度，托盘菜单一键关闭全部
- 截图就地标注完成后「对号」直接复制到剪贴板（位图 + 可落盘 PNG，桌面 `Ctrl+V` 即生成文件），不再弹出编辑窗口
- 「取字」OCR：只用腾讯云识别文字，打开仅识别窗口，由你自己点选 / 框选要复制的文字（不自动全量复制）
- 「译」：腾讯云 OCR 出文字 + 坐标 → 百度文本翻译逐行翻译 → 译文叠加到原图上
- 图片翻译结果窗口：译文直接显示在图片上，可选字复制 / 复制图片 / 贴图 / 保存 / 重新翻译
- 翻译后在图片上点选 / 框选文字直接复制（原文或译文，按阅读顺序拼接）
- 未配置腾讯云凭证时自动回退百度图片翻译，功能不中断
- 图片工具：深色轻量查看器，就地转格式（PNG/JPG/BMP/GIF/TIFF）、压缩、改尺寸、重命名、旋转、裁剪、另存、复制，可滚动缩放/拖动平移、同目录循环切换
- 资源管理器集成：在任意图片上右键 →「使用工具侠打开图片」，或「打开方式」→「工具侠」（只写 HKCU，可随时开关，不抢文件关联）
- **首次运行自动配置**：把 exe 拷到新电脑第一次双击时，自动开启「开机自动启动 + 图片右键菜单 + 打开方式」三项并打上 `FirstRunDone` 标记，之后不再重复、也不覆盖用户手动改过的开关
- 托盘菜单：翻译选中文本、手动翻译、截图…、语音识别（按住说话）、翻译剪贴板、打开图片…、软件下载…、图片右键菜单 / 打开方式、任务栏状态显示、关闭所有桌面贴图、设置、开机启动、退出
- 任务栏状态条：`WS_EX_LAYERED` + `UpdateLayeredWindow` 每像素透明，只画文字不铺底色；网速用 `NetworkInterface.GetIPv4Statistics()` 累加、CPU 用 `GetSystemTimes` 差值、内存用 `GlobalMemoryStatusEx`，纯系统 API 无第三方依赖；位置每秒重算，鼠标穿透不挡托盘点击
- **单位统一 + 间隙收紧**：`FormatSpeedCompact` 把状态条网速统一成 `0.00 MB/s` 形式（≥1000 MB/s 自动用 GB/s），不再出现 B/s / KB/s / MB/s 三档长度差异；详情卡片仍用 `FormatSpeed` 显示 `220 B/s` 这类精确值。每行拆成「箭头 / 速度 / 指标名 / 百分比」四段，速度列宽取两行当前速度的较宽者并量化到「一个数字」的粒度，四列全部左对齐到两行共用的起点，列间只留 4–6px 固定间隙、位图右端紧贴最后一列。定位则始终用「最长数据渲染一次」得到的固定参考宽度，位图变窄时左边缘纹丝不动。这样既不会像早期版本那样因单位长度不一导致上下不对称，也不会像按最长文本预留固定列宽那样在网速和 CPU 之间空出一大截
- **点任务栏不会把状态条点没**：Explorer 在点击任务栏 / 托盘时会把任务栏提到同为 topmost 的状态条之上。每次重绘和光标停留在任务栏时都会用 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` 重新压回顶层，点击后 150ms 内恢复，且只在光标位于任务栏时走快速路径，全屏应用下不额外抢 Z 序
- **悬停详情卡片**：今日流量按网卡记底数累计到 `traffic.json`（跨天自动归零、每 30 秒落盘，计数器回绕不计入），CPU 频率走 PDH 的 `% Processor Performance` × CPU 标称频率（与任务管理器同一算法；`CallNtPowerInformation` 在 Win8+ 上常年返回固定基准值，只作计数器不可用时的回退）、电池走 `GetSystemPowerStatus`、磁盘走 `DriveInfo`、GPU 走 `GPU Engine` 性能计数器两次采样求差；取不到的指标自动隐藏对应行（如台式机无电池、老驱动无 GPU 计数器）。CPU 温度 / 电池 Wh 需要厂商 WMI 或驱动支持，本机取不到时不显示，避免为一个数字拖慢每秒刷新。卡片刻意做小（10.5px 字、紧凑内边距与行距），**右边缘尽量靠任务栏右端**而不是整块往左伸出；同时保留与状态条的一段水平重叠，鼠标从状态条斜移进卡片时不会被误判成「移开」
- 设置窗口：内容区独立滚动、「保存 / 取消」常驻底部，按系统 DPI 整体缩放，低分辨率与 125%–250% 缩放下不会出现选项框 / 按钮被裁切
- 软件下载：托盘右键「软件下载…」搜索 360 宝库，列表点一下直接下「普通下载」的安装包（不经过 360 软件管家，不会顺带装卫士），默认存到「下载」文件夹，支持进度 / 取消 / 定位文件
- 开机自动启动写在 `HKCU\...\Run\工具侠`；每次启动都会校正路径，并清掉任务管理器 / 启动管理工具写下的「已禁用」标记，所以程序换目录、改名字、被优化软件关掉过，下次手动启动一次就自动修好
- 百度翻译 API（通用文本翻译，回退时含图片翻译）+ 腾讯云 OCR（GeneralAccurateOCR）+ 腾讯云语音识别（SentenceRecognition），全部自行实现 TC3-HMAC-SHA256 签名
- 自动目标语言：中文→英文，其他→中文；也可固定目标语言（文本与图片分别设置）
- 百度密钥、腾讯云 SecretKey 都用 Windows DPAPI 加密后保存在当前用户目录
- 翻译结果窗口支持一键复制原文/译文，并可编辑原文后重新翻译
- 超长文本自动分段翻译（6000 UTF-8 字节上限）
- 可在设置中开关「选中即显示」和「右键显示」两种触发方式
- 可在设置中自定义四个全局快捷键（Ctrl/Alt/Shift/Win 任意组合 + 一个主键，或单独一个功能键）；托盘菜单显示的快捷键与设置实时同步

## 环境

- Windows 10 / 11
- .NET Framework 4.8（Windows 10/11 通常自带）

不需要安装 .NET SDK，使用系统自带的 `csc.exe` 编译。

## 编译

在 PowerShell 中执行：

```powershell
cd <仓库根目录>
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

生成：

```text
dist\工具侠.exe
```

`app.ico` 是 16/24/32/48/64/128/256px 多尺寸图标，`build.ps1` 用 `/win32icon` 把它嵌进 exe（资源管理器 / 任务栏看到的图标），同时用 `/resource` 嵌入同一份数据供托盘图标复用。若 `app.ico` 缺失，构建脚本会自动编译 `tools\MakeIcon.cs` 现场生成，不依赖任何第三方库；想换图标时删掉 `app.ico` 再重新编译即可。

程序运行时 exe 被占用、无法直接覆盖，可先用环境变量把新版编译到旁路口径验证，确认无误后再替换：

```powershell
$env:ST_OUTPUT_NAME = "_next.exe"; .\build.ps1
.\dist\_next.exe --selftest
```

## 凭证与隐私

- **密钥不进代码、也不进 exe**：百度 AppID / 密钥、腾讯云 SecretId / SecretKey 只存在本机 `%APPDATA%\SelectionTranslator\config.json`，密钥用 Windows DPAPI 加密（绑定当前账户，换台电脑解不开）。`release\工具侠.exe` 是**零预置凭证**的干净构建，谁下载都要自己填。
- **日志不写凭证**：诊断命令只输出 `hasBaiduCredentials=True/False` 这类布尔状态，运行日志不记录 AppID、SecretId、密钥或签名值。
- 清空本机凭证：把 `config.json` 里 `AppId`、`SecretProtected`、`TencentSecretId`、`TencentSecretKeyProtected` 四个字段置空（或直接删掉文件，其余设置会回默认），下次启动即为未配置状态。**制作对外发布版不需要清空本机配置**——凭证从来不在 exe 里，直接编译即可。
- **每次保存配置前自动留快照**：`ConfigStore.Save` 覆盖 `config.json` 之前会写一份 `%APPDATA%\SelectionTranslator\config.auto-backup.<时间戳>.json`（同一分钟内不重复，只保留最近 8 份）。凭证或快捷键被误改时，把最近一份快照复制回 `config.json` 就能完整恢复；里面是 DPAPI 密文，同一 Windows 账户下可以直接解开。
- **配置不跟着 exe 走**：单文件复制给别人时，设置、快捷键、凭证都留在原机器的 `%APPDATA%`，新机器等于全新安装，会按上面的「首次运行自动配置」把三个开关打开，然后要求自己填 API 凭证。这也正是 `release` 版可以放心分发的原因。
- 仓库不含 `config.json` 和任何日志（`dist/`、`.workbuddy/` 已被 `.gitignore` 排除）；请勿把本机 `config.json` 或 `%APPDATA%\SelectionTranslator\*.txt` 提交、打包或截图外发。

## 首次使用

1. 打开 [百度翻译开放平台](https://fanyi-api.baidu.com/manage/developer)，注册并创建应用，开通「通用翻译」，拿到 **AppID** 和 **密钥**。
2. （推荐，用于截图识别）打开 [腾讯云访问密钥控制台](https://console.cloud.tencent.com/cam/capi)，在 [文字识别 OCR](https://console.cloud.tencent.com/ocr) 开通服务，创建 **SecretId** 和 **SecretKey**。不填也能用，但截图识别会回退到百度图片翻译（需在百度侧单独开通该服务）。想用语音识别，再到 [语音识别控制台](https://console.cloud.tencent.com/asr) 开通服务（与 OCR 共用同一对密钥）。
3. 运行 `工具侠.exe`。**第一次运行时会自动开启三件事**：开机自动启动、图片右键菜单、图片「打开方式」里的「工具侠」——不用手动去托盘里逐个勾选。这一步只在全新安装时做一次，结果写进 `config.json` 的 `FirstRunDone` 标记；之后你手动关掉哪个开关，下次启动都不会被强行改回来。老配置文件（没有这个标记）按「已经跑过」处理，不会动你原来的设置。
4. 右键托盘图标 →「设置…」，填入百度 AppID / 密钥，以及腾讯云 SecretId / SecretKey；选择目标语言后，可分别点「测试翻译」「测试 OCR」「测试麦克风」（会录 3 秒真实调用一次）确认可用，再保存。第一次运行且没填凭证时，设置窗口会自己在几秒后弹出。
5. （可选）在设置中自定义快捷键：点一下任一输入框（会变成浅蓝底提示「请按下组合键…」），按住 Ctrl / Alt / Shift / Win 不松再按一个键，或只按 `F1`–`F12` 等功能键；`Esc` 取消、`Backspace` 清空已按下的修饰键。录入结果在保存后立即生效，托盘菜单右侧也会同步更新。
6. 在任意应用中选中文字：
   - 选中后自动浮出「译」按钮，点击即可翻译；
   - 按快捷键（默认 `Ctrl+Alt+T`）；或
   - 右键，点击浮出的「译」按钮（如果设置了允许右键触发）。

   没有现成文本可选中时，按快捷键（默认 `Ctrl+Alt+E`）打开手动翻译窗口，输入文本后回车即翻译。

   文字在图片里、无法选中时，按 `Ctrl+Alt+S`，用左键或右键拖拽框选，松开后点浮层工具栏里的「译」即可识别并在结果窗口里看到叠在图片上的译文；只想把图里的字拿出来就点「取字 📄」，会在同一个窗口里显示识别出的文字块，你自己点选 / 框选要复制的部分；点「完成 ✓（对号）」则直接把截图复制到剪贴板。截图后不会再出现旧版编辑器。

   想说话代替打字时，先把光标点进任意输入框，**按住** `Ctrl+Alt+V` 说完再松开，底部浮条会变成「识别中…」，随后文字直接打在光标处；不弹任何窗口。

## 第三方接口

请求地址：

```text
https://fanyi-api.baidu.com/api/trans/vip/translate
```

签名规则：

```text
sign = MD5(appid + q + salt + 密钥)
```

本工具使用 `from=auto` 自动识别源语言，使用 `to` 指定目标语言。单次请求上限为 6000 UTF-8 字节；超长文本会自动在换行或句末标点处分段翻译，无需手动处理。可在「设置」中调整最大文本长度（字符数）。

### 百度图片翻译接口（回退路径）

> 默认流程**不走这个接口**。只有「未配置腾讯云凭证」或「在设置里关闭腾讯云 OCR」时才回退到这里，因此需要先在百度开放平台单独开通「图片翻译」服务。

请求地址（文档：[doc/26](https://fanyi-api.baidu.com/doc/26)）：

```text
POST https://fanyi-api.baidu.com/api/trans/sdk/picture
Content-Type: multipart/form-data
```

**参数位置很关键**：按官方文档的请求示例，`from`、`to`、`appid`、`salt`、`cuid=APICUID`、`mac=mac`、`version=3`、`paste`、`sign` 必须放在 **URL 查询串**上；只有 `image` 通过 `multipart/form-data` 上传，且该 part 必须带 `filename`（否则服务端不把它当作文件）。把这些参数塞进表单体、或图片 part 缺 `filename`，服务端都会返回 `54001 Invalid Sign`。

签名规则（注意 `md5(image)` 是**原始图片字节**的 MD5，32 位小写，不做 hex/base64 编码）：

```text
sign = MD5(appid + MD5(image_bytes) + salt + "APICUID" + "mac" + 密钥)
```

- 图片限制：jpg/jpeg/png、≤4M、短边 ≥30px、长边 ≤4096px、长宽比 ≤3:1；本工具会自动缩放和压缩。
- `paste=1` 返回整图贴合译文图（base64 `pasteImg`），异常时自动降级为 `paste=0` 仅返回文本。
- 响应 `data` 含 `from`、`to`、`sumSrc`、`sumDst`、`content[]`（`src`/`dst`/`rect`/`lineCount`）、`pasteImg`。
- 目标语种与文本接口不同：`jp`/`kor`/`fra`/`spa`，且不支持 `auto`、`ar`、`th`、`vi`。错误码 69001–69008、69012 为图片接口专属，已做中文友好提示。
- 回退路径下的「取字」使用 `paste=0` 且忽略译文（只读取 `sumSrc`，缺失时按 `rect` 阅读顺序拼接 `content[].src`）；`to` 仍须是合法语种（`auto` 时按 `en` 请求，遇到 58001/58002 方向性错误自动改用 `zh` 重试一次）。该接口没有「仅 OCR」选项，调用仍计入图片翻译额度。

### 腾讯云 OCR 接口（GeneralAccurateOCR）

请求地址（文档：[产品文档](https://cloud.tencent.com/document/product/866/33526)）：

```text
POST https://ocr.tencentcloudapi.com
X-TC-Action: GeneralAccurateOCR
X-TC-Version: 2018-11-19
Content-Type: application/json; charset=utf-8
```

请求体只需 `ImageBase64` 与 `ConfigID`（默认 `MulOCR`，支持 100+ 语种；也可在设置里切到 `OCR` 通用模式）。不引入腾讯云 SDK，签名按云 API 3.0 的 **TC3-HMAC-SHA256** 自行实现：

```text
CanonicalRequest = POST\n/\n\ncontent-type:...\nhost:ocr.tencentcloudapi.com\n\ncontent-type;host\nSHA256(payload)
StringToSign     = TC3-HMAC-SHA256\n<unix timestamp>\n<date>/ocr/tc3_request\nSHA256(CanonicalRequest)
派生密钥          = HMAC("TC3"+SecretKey, date) → HMAC(·,"ocr") → HMAC(·,"tc3_request")
Authorization    = TC3-HMAC-SHA256 Credential=<SecretId>/<scope>, SignedHeaders=content-type;host, Signature=<hex>
```

- 响应 `Response.TextDetections[]` 的 `DetectedText` 是每行文字，`ItemPolygon`（或 `Polygon` 四点外接矩形）给出坐标，统一转成 `left top width height`；`Confidence` 保留备用。
- 按 Y 分行（容差 0.6 倍行高）、行内按 X 排序，得到阅读顺序；同一行用空格、不同行用换行拼接成全文。
- 图片 Base64 后不超过 10M；程序优先 PNG，超大时降 JPEG 质量，仍超限才等比缩放到长边 4096px 并做坐标映射。
- 常见错误码已做中文提示：`AuthFailure.*`（凭证/签名/时间）、`FailedOperation.UnOpenError`（未开通服务）、`ResourceUnavailable.InArrears`（欠费）、`FailedOperation.ImageNoText`（图中无文字）等。

**为什么这样拆**：百度文本翻译比图片翻译轻得多（一次 HTTP 表单请求、无贴合图解码），而腾讯云高精度 OCR 在长串数字、小字、复杂版式上更准；「取字」因此不用再为「不翻译」付出一次翻译调用。代价是腾讯云 OCR 是另一套凭证、按 OCR 调用计费。

### 腾讯云语音识别接口（SentenceRecognition）

请求地址（文档：[一句话识别](https://cloud.tencent.com/document/product/1093/35646)）：

```text
POST https://asr.tencentcloudapi.com
X-TC-Action: SentenceRecognition
X-TC-Version: 2019-06-14
Content-Type: application/json; charset=utf-8
```

签名与 OCR 完全同一套 TC3-HMAC-SHA256，只是派生密钥里的 service 换成 `asr`（因此两者的 `Signature` 必然不同，自检里有一条专门比对）。请求体：

```json
{ "EngSerViceType": "16k_zh", "SourceType": 1, "VoiceFormat": "wav",
  "Data": "<base64 的 wav>", "DataLen": 19244,
  "WordInfo": 0, "FilterDirty": 0, "FilterModal": 0, "FilterPunc": 0,
  "ConvertNumMode": 1 }
```

- `SourceType=1` 表示直接上传语音数据（不走 `Url`，也就不需要 COS）；`DataLen` 是 **base64 之前**的字节数。
- 音频限制：≤ 60 秒、base64 前 ≤ 3MB；`VoiceFormat=wav` 时用标准 44 字节头。
- 引擎（`EngSerViceType`）在设置里可选 20 种：`16k_zh`（中文普通话，默认）、`16k_zh-PY`（中英粤）、`16k_zh_dialect`（23 种方言）、`16k_en`、`16k_ja`、`16k_ko`、`16k_yue` 等。
- 响应只取 `Response.Result`（整句文本）、`AudioDuration`（毫秒）和 `RequestId`；`WordList` 词级时间戳本轮不解析（`WordInfo=0`）。
- 常见错误码已做中文提示：`FailedOperation.UserNotRegistered`（未开通语音识别）、`FailedOperation.UserHasNoAmount`（资源包耗尽）、`AuthFailure.*`（凭证/签名）、`InvalidParameterValue.ErrorVoicedataTooLong`（超过 60 秒）等。

**录音实现**：`VoiceRecorder.cs` 直接 P/Invoke `winmm.dll`（`waveInOpen/PrepareHeader/AddBuffer/Start/Stop/Reset`），回调里把数据追加进 `MemoryStream`，并复用同一块缓冲继续录音 —— 复用时要保留 `WHDR_PREPARED`、只清 `WHDR_DONE|WHDR_INQUEUE`，否则 `waveInAddBuffer` 会静默失败，录音只剩头 600ms（6 个缓冲的容量）。

## 配置与日志

```text
%APPDATA%\SelectionTranslator\config.json
%APPDATA%\SelectionTranslator\app.log
```

`config.json` 中 `SecretProtected`（百度密钥）和 `TencentSecretKeyProtected`（腾讯云 SecretKey）是 DPAPI 加密后的 Base64，只能由当前 Windows 用户解密。不要直接把该文件发给别人。

`dist\工具侠.exe` 是单文件、无第三方 DLL，可以单独拷到别的 Windows 电脑（需自带 .NET Framework 4.8，Win10/11 已内置）；但**密钥不会跟着走**：新机器上要在设置里重新填百度 AppID / 密钥，以及（可选）腾讯云 SecretId / SecretKey。

## 自检

```powershell
.\dist\工具侠.exe --selftest
Get-Content "$env:APPDATA\SelectionTranslator\selftest.txt"
```

自检（共 410 项）覆盖文本翻译 MD5 签名、自动目标语言判断、DPAPI 加解密、文本分段，图片翻译的 MD5 签名、固定参数（cuid/mac）、语种映射与目标语种校验，截图浮层的构造与选区导出冒烟测试，exe / 托盘图标资源，结果窗口的 `rect` 解析、按阅读顺序拼接选字文本、贴合图解码与画布绘制冒烟测试，**TC3-HMAC-SHA256 签名固定向量**、腾讯云 OCR 响应解析（含 `Polygon` 回退、鉴权错误映射）、阅读顺序、目标语种归一化、管道结果拼装、两套 `Prepare` 路径。语音识别部分新增：WAV 头字节级校验（RIFF/WAVE/fmt/data、采样率、data 长度、时长换算）、引擎码归一与列表、`asr` 与 `ocr` 签名必须不同、`SentenceRecognition` 响应与错误码解析、录音浮条（含完成态）的渲染冒烟测试、焦点默认放行分类（未知控件类名 / 资源管理器 / 空类名都放行，桌面 / 菜单 / 无效窗口拦截）、上屏闸门校验、`KEYEVENTF_UNICODE` 输入序列构造、换行/控制字符归一、上屏方式归一与设置窗口上屏方式下拉，以及仅识别模式（结果无译文、窗口不绘制译文遮罩、默认复制原文）的回归用例。图片工具部分新增：格式/扩展名归一与中文标签、体积与位数格式化、路径中间省略、PNG 落盘→读回→尺寸校验、缩放 / 旋转 / 裁剪（含越界返回 null）、32 位透明图转 JPEG 自动铺白、同名不覆盖的 `(1)` 命名、带后缀命名、重命名成功与三类失败原因、同目录图片数量与自然序、以及查看器构造冒烟测试。窗口拾取部分新增：桌面 / 工作窗类名识别、最小可截尺寸、越界夹取（含完全在屏幕外返回空）。图标部分新增：7 档尺寸清单、16px 实际像素有不透明内容且四角透明、ICO 文件头与帧数。改名与右键菜单部分新增：显示名 / 内部标识、菜单文案与注册表路径、带空格路径的命令行加引号与去引号、空路径、图标值格式，以及**真的往 HKCU 写一次再删掉**的注册 / 读回 / 反注册往返测试（跑完自动恢复原状态），开机启动命令行加引号与 Run 键路径。「打开方式」部分新增：ProgID / 8 种扩展名清单、两条候选表路径、Applications 键名、ProgID 命令行读回、候选值写入与反注册后不残留（同样是真的写一次 HKCU 再恢复）。首次运行部分新增：全新配置的默认三个开关值、NeedsSetup 的三种边界（新配置 / 已完成 / null）、以及老配置缺少 FirstRunDone 字段时的迁移判定。快捷键录入部分新增：`ControlKey` / `ShiftKey` / `Menu` / `LWin` 键码到 MOD 位的映射、`Ctrl+ControlKey` 这类非法组合必须被拒绝、`Ctrl+Shift+Z` 解析与格式化往返、无修饰键时单功能键（F5）合法而普通字母非法、录入前缀与多修饰键组合生成；另有 `--hotkeyprobe` 端到端模拟真实录入流程。任务栏状态条部分新增：网速三档格式化与负值夹取、CPU 差值计算与异常差值、两行文案、贴托盘区定位与 Win11 无 `TrayNotifyWnd` 时的回退位置、透明位图尺寸与四角透明、设置窗口内容区滚动 / 底部按钮常驻 / 按钮归属，以及 `ShowTaskbarStats` 默认值。状态条排版部分新增：四段拆分文案、统一 MB 单位的空闲 / 字节 / MB / GB / 负值五种格式化、列宽量化与上限钳位、最短与最长数字下「短速度位图更窄 / 行高一致 / 箭头起点一致 / 宽度差有界」（含 125% DPI）、紧凑卡片尺寸上限，以及 `--statsdetail` 实拍里卡片右移 / 保留重叠 / 不过大三项判定。悬停详情部分新增：今日流量首次采样只记底数、增量累计、计数器回绕 / 新网卡插拔不计入、跨天归零与持久化读回，容量 / 频率 / 时长格式化，GPU 实例名解析与按物理显卡聚合钳位，详情行「取不到即隐藏」的完整 / 最小 / 充电中 / 多显卡样本，详情卡片圆角透明、深浅两套配色与中心不透明，以及悬停判定的移入展开 / 移开收起 / 卡片内保持 / 空边界与实时 CPU 频率合理性。另有三个端到端探针：`--statsdetail` 实拍贴屏卡片并列出本机各指标可达性，`--hoverprobe` 真的把鼠标移进状态条再移开验证自动展开与收起，`--occlusionprobe` 真实左键点击任务栏并比对点击前后状态条区域的文字像素，锁住「点一下任务栏后状态条被盖住」这个回归（修复前 674 → 0，修复后保持 700+）。锁屏 / 远程会话 / 虚拟显示器下系统会直接拒绝屏幕 `BitBlt`，此时自检里的 4 条截图浮层冒烟断言会标注 `SKIP` 而不判失败，贴屏类诊断探针则无法取图；回到正常桌面重跑即可。剪贴板历史部分新增：类型标签与 TTL 档位归一、分类 / 多关键词 / 尺寸搜索、按时间倒序、相对时间与日期分组、TTL 过期与收藏豁免、条数上限清理（含 20 条下限钳位）、重复内容合并、摘要截断、JSON 写盘读回，以及 `--clipboardprobe` 端到端验证监听注册、`WM_CLIPBOARDUPDATE`、文本 / 图片写回与过期清理（探针会先备份并恢复用户剪贴板），`--clipboarduipreview` 真实显示窗口并截 `ui-clipboard.png` 检查自绘列表与缩略图。双击 Ctrl 部分新增：状态机的双击触发 / 间隔超时 / 中间夹键取消 / 长按取消 / 自动重复 / 三连击只触发一次、左右 Ctrl 键码与注入标志识别、配置默认值与老配置迁移，以及 `--ctrlapprobe` 真装低级键盘钩子并用 `SendInput` 走完整条链路。桌面粘贴部分新增：文件名与三种格式策略、临时文件清理边界、PNG / TXT 落盘读回、粘贴临时目录识别（单文件 / 混合列表 / 空列表三种边界），以及 `--clipboardprobe` 真实验证 `Auto` 模式同时含位图与 `CF_HDROP`、文件真实存在且为 PNG、`FileOnly` / `ImageOnly` 互斥、文本转 `.txt` 可粘贴、**截图路径直接写内存位图后同样挂上可落盘文件**。软件下载部分新增：360 宝库 JSON 解析（正常 / errno 错误 / 非 JSON 三种）、协议相对 URL 绝对化、文件名提取与非法字符清理、`软件名_ID.exe` 命名与扩展名保留、重名 `(1)`、简介压行截断、体积 / 速度 / 剩余时间文案，以及三个端到端探针：`--softprobe` 真请求搜索接口并 HEAD 安装包直链、`--softdownloadprobe` 真的把最小安装包下到临时目录并校验大小与 `MZ` 头（跑完自动删除）、`--softuipreview` 真实显示窗口并截 `ui-software.png`。

真实请求诊断（使用当前已保存的凭证，不输出密钥）：

```powershell
# 只取字（自动选引擎）
.\dist\工具侠.exe --ocrtest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\ocrtest.txt"

# 识别 + 翻译整条链路（自动选引擎）
.\dist\工具侠.exe --imagetest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\imagetest.txt"

# 只调腾讯云 OCR，逐行打印坐标与原文
.\dist\工具侠.exe --tencenttest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\tencenttest.txt"

# 验证百度「逐行文本翻译」通道（不需要腾讯云凭证）
.\dist\工具侠.exe --linetest
Get-Content "$env:APPDATA\SelectionTranslator\linetest.txt"

# 验证腾讯云签名 / 公共参数格式（用假凭证，预期 AuthFailure.*）
.\dist\工具侠.exe --tencentprobe
Get-Content "$env:APPDATA\SelectionTranslator\tencentprobe.txt"

# 只测录音链路（不需要凭证）：录 3 秒，报告设备数/有效时长/峰值电平
.\dist\工具侠.exe --voicetest 3
Get-Content "$env:APPDATA\SelectionTranslator\voicetest.txt"

# 用真实凭证识别一个 wav（不给路径则用 1 秒正弦波，只验证报文）
.\dist\工具侠.exe --asrtest "C:\path\to\speech.wav"
Get-Content "$env:APPDATA\SelectionTranslator\asrtest.txt"

# 验证 asr 的签名 / 公共参数格式（用假凭证，预期 AuthFailure.*）
.\dist\工具侠.exe --asrprobe
Get-Content "$env:APPDATA\SelectionTranslator\asrprobe.txt"

# 强制走旧的百度图片翻译回退路径
.\dist\工具侠.exe --baiduimagetest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\imagetest.txt"

# 延时 3 秒探测当前焦点（验证任意控件都放行，只排除桌面/菜单）
.\dist\工具侠.exe --focustest
Get-Content "$env:APPDATA\SelectionTranslator\focustest.txt"

# 延时 3 秒后把文字打进当前光标处（先点开记事本，再运行）
.\dist\工具侠.exe --inputtest "中文语音上屏 123"

# 真实回环：屏幕外建一个 TextBox，注入后读回文本比对
.\dist\工具侠.exe --typetest
Get-Content "$env:APPDATA\SelectionTranslator\typetest.txt"

# 离屏渲染界面预览图（ui-*.png）
.\dist\工具侠.exe --uipreview
Get-Content "$env:APPDATA\SelectionTranslator\uipreview.txt"

# 离屏渲染图片查看器与四个工具对话框（ui-viewer*.png）
.\dist\工具侠.exe --viewerpreview
Get-Content "$env:APPDATA\SelectionTranslator\viewerpreview.txt"

# 任务栏状态条：系统采样 + 任务栏定位 + 深浅色预览图 ui-stats.png
# 另出 ui-bar-zoom.png：统一 MB 后最短/最长数字两组的放大对比，可肉眼确认间隙小且两行对齐
.\dist\工具侠.exe --statsprobe
Get-Content "$env:APPDATA\SelectionTranslator\statsprobe.txt"

# 把状态条真的贴到任务栏上，截一张局部屏 ui-stats-live.png
.\dist\工具侠.exe --statslive
Get-Content "$env:APPDATA\SelectionTranslator\statsformprobe.txt"

# 悬停详情：深浅色预览图 ui-stats-detail.png + 贴屏实拍 ui-stats-detail-live.png
.\dist\工具侠.exe --statsdetail
Get-Content "$env:APPDATA\SelectionTranslator\statsdetailprobe.txt"

# 真实把鼠标移进状态条再移开，验证自动展开 / 收起（结束后会还原光标位置）
.\dist\工具侠.exe --hoverprobe
Get-Content "$env:APPDATA\SelectionTranslator\hoverprobe.txt"

# 真实点击任务栏，比对状态条文字像素，验证不会被 Explorer 盖住
.\dist\工具侠.exe --occlusionprobe
Get-Content "$env:APPDATA\SelectionTranslator\occlusionprobe.txt"

# 快捷键录入端到端探针：模拟「点输入框 → 按 Ctrl（不松）→ 按 T」
.\dist\工具侠.exe --hotkeyprobe
Get-Content "$env:APPDATA\SelectionTranslator\hotkeyprobe.txt"

# 剪贴板历史：监听注册 / 系统剪贴板读写 / TTL 清理（会自动恢复原剪贴板）
.\dist\工具侠.exe --clipboardprobe
Get-Content "$env:APPDATA\SelectionTranslator\clipboardprobe.txt"

# 剪贴板窗口渲染冒烟：造示例历史、真的显示窗口并截 ui-clipboard.png
.\dist\工具侠.exe --clipboarduipreview
Get-Content "$env:APPDATA\SelectionTranslator\clipboarduiprobe.txt"

# 双击 Ctrl：真装 WH_KEYBOARD_LL，模拟两下 Ctrl / 中间夹键 / 注入按键
.\dist\工具侠.exe --ctrlapprobe
Get-Content "$env:APPDATA\SelectionTranslator\ctrlapprobe.txt"

# 软件下载：真请求 360 宝库搜索 + HEAD 安装包直链
.\dist\工具侠.exe --softprobe
Get-Content "$env:APPDATA\SelectionTranslator\softprobe.txt"
# 真的下一个小安装包，校验大小与 MZ 头后自动删除
.\dist\工具侠.exe --softdownloadprobe
Get-Content "$env:APPDATA\SelectionTranslator\softdownloadprobe.txt"
# 软件下载窗口渲染冒烟：截 ui-software.png
.\dist\工具侠.exe --softuipreview
Get-Content "$env:APPDATA\SelectionTranslator\softuiprobe.txt"

# 首次运行自动配置：查看状态 / 手动执行一次（与正常启动同一入口）/ 强制修复
.\dist\工具侠.exe --firstprobe
.\dist\工具侠.exe --firstrun
.\dist\工具侠.exe --firstsetup
Get-Content "$env:APPDATA\SelectionTranslator\firstprobe.txt"

# 图片右键菜单 + 打开方式：注册 / 移除 / 查看状态 / 让 Shell 解析验证
.\dist\工具侠.exe --shellon
.\dist\工具侠.exe --shelloff
.\dist\工具侠.exe --shellprobe
Get-Content "$env:APPDATA\SelectionTranslator\shellprobe.txt"
.\dist\工具侠.exe --assocprobe
Get-Content "$env:APPDATA\SelectionTranslator\assocprobe.txt"

# 开机启动项：看它到底指向哪个 exe、是否被禁用
.\dist\工具侠.exe --startupprobe
Get-Content "$env:APPDATA\SelectionTranslator\startupprobe.txt"

# 截图窗口拾取：列出可见顶层窗口 / 在指定坐标试一次拾取
.\dist\工具侠.exe --windowlist
.\dist\工具侠.exe --windowprobe 960 500
Get-Content "$env:APPDATA\SelectionTranslator\windowprobe.txt"

# 图标各尺寸预览 + 截图浮层的窗口高亮效果
.\dist\工具侠.exe --iconpreview
.\dist\工具侠.exe --overlaypreview

```

`RESULT: OK` 即该段链路正常；失败时文件里有完整异常信息。`--tencentprobe` / `--asrprobe` 用一组无效凭证发一次真实请求，收到 `AuthFailure.SecretIdNotFound` 之类的鉴权错误反而说明**签名串与公共参数格式被服务端正确解析**，可用来在没填密钥时先排除代码问题。

## 已知限制

- 「选中即显示」依赖 UI Automation 读取焦点元素选区；如果应用不暴露选区（部分游戏、远程桌面、Electron 应用等），请改用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 右键读取优先使用 UI Automation；如果应用不暴露选区，会自动发一次 `Ctrl+C` 兜底。终端类应用（Windows Terminal、cmd、PowerShell 等）不会发 `Ctrl+C`，以免中断命令，这类应用请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 以管理员身份运行的目标程序，普通权限的工具无法向它发送 `Ctrl+C`（Windows UIPI 限制）。如有需要，让本工具也以管理员身份运行。
- 远程桌面、部分游戏可能无法读取选区，请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 全局快捷键被其他程序占用时，托盘菜单仍可使用；菜单该项右侧会显示「（被占用）」，并弹一次气泡提示，日志也会记录注册失败。请在设置中改为其他组合。
- Win 键作为修饰符可能被系统拦截（如 Win+E 会打开资源管理器），建议使用 Ctrl+Alt+Shift 组合。
- 翻译文本会发送到百度，截图会发送到腾讯云 OCR（回退时发送到百度图片翻译），请勿处理敏感内容。
- 腾讯云 OCR 需在控制台开通并配置 SecretId / SecretKey；只填百度凭证时截图功能自动回退百度「图片翻译」，而该服务需在百度开放平台单独开通，仅开通「通用翻译」时会返回 52003 未授权。
- 默认「取字」只调腾讯云 OCR，不消耗百度翻译额度，但仍需联网并按腾讯云 OCR 调用计费；关闭腾讯云开关后回退百度图片翻译，此时接口没有纯 OCR 开关，会照常计费一次翻译调用。
- 「译」在腾讯云路径下需要两次串行请求（OCR + 文本翻译），网络较差时延迟会累加；自动目标语种在回退路径下可能产生两次百度图片翻译请求。
- 逐行译文按 OCR 行一一对齐；如果接口把多行合并或某行过长，程序会做数量对齐，个别文字块可能回退显示原文而非译文。
- 图片过窄、文字过小或倾斜、背景复杂时识别效果会下降；长宽比超过 3:1 的限制只存在于百度图片翻译回退路径。
- 截图浮层中的马赛克仅作用于当前显示的截图，不会修改系统剪贴板中的原始内容。
- 窗口直选只能命中**顶层窗口**：想要截图里的某个子面板（浏览器里的单个标签页内容、软件里的一个子区域），还是用自由框选；而受 DRM 保护的画面（部分播放器）本身就会截成黑屏，与拾取方式无关。
- 全屏独占的游戏窗口无法被普通浮层覆盖，这类场景请用系统自带的截图工具。
- 语音识别需要麦克风，并且要在腾讯云控制台**单独开通「语音识别 ASR」**；只开通 OCR 时调用会返回 `FailedOperation.UserNotRegistered`。
- 「软件下载」依赖 360 宝库的 JSON 接口（`soft-api.safe.360.cn`），360 改版或加风控时会失效，窗口会显示具体错误而不是静默失败；安装包由厂商 / 360 CDN 提供，工具只保存文件、**不做静默安装、也不校验数字签名**，双击安装前请自行确认来源。列表里的下载地址就是网页「普通下载」那一条，不是「安全下载」。
- 语音一次最长 60 秒（接口限制），超时会自动截断收尾；连续说话请分多次录入。
- 语音输入不限制输入框类型；只有当前焦点明确是桌面 / 任务栏 / 弹出菜单时才不模拟键入，识别结果改放剪贴板并提示 `Ctrl+V`。
- 以管理员权限运行的程序会拦截普通权限进程的模拟键入（UIPI）；让本工具也以管理员身份运行，或改用「剪贴板粘贴」上屏方式。
- 说话期间切到别的窗口时，文字不会写入新窗口，只会放进剪贴板，手动 `Ctrl+V` 即可。
- 录音浮条不会抢焦点，所以它上面的「点这里停止」需要鼠标点击浮条本身；键盘请用「松开快捷键」结束。
- 图片工具压缩固定输出 JPEG；动图 GIF 只取第一帧；不支持 WebP / HEIC / AVIF（系统 GDI+ 无对应编解码器）。
- 图片编辑只在内存中进行，超过 8 步的撤销会被丢弃；超大图（数千万像素）连续裁剪 / 撤销会占用较多内存。

## 文件结构

```text
BaiduTranslator.cs            百度文本翻译 API（含逐行翻译）
BaiduImageTranslator.cs       百度图片翻译 API（回退路径）
TencentOcr.cs                 腾讯云 GeneralAccurateOCR（TC3 签名/解析/预处理）
TencentAsr.cs                 腾讯云 SentenceRecognition（一句话识别，共用 TC3 签名）
VoiceRecorder.cs              麦克风录音（winmm waveIn，16k/16bit/mono → WAV）
VoiceCaptureForm.cs           录音浮条（不抢焦点；录音/识别/已上屏/失败四态）
InputFocusProbe.cs            焦点快照（默认放行） + 文字直投（Unicode 键入 / 剪贴板粘贴）
PicturePipeline.cs            识别 + 翻译管道，统一产出结果结构
ScreenshotCaptureForm.cs      一体化截图浮层（框选/标注/工具栏）
WindowPicker.cs               窗口拾取（Z 序枚举 + DWM 真实外框 + 幽灵层过滤）
AppIcon.cs                    图标绘制（蓝底圆角 + 白色扳手，托盘与 ico 共用）
ImageTranslationForm.cs       图片翻译结果窗口（译文贴图上/选字复制）
PinnedImageForm.cs            桌面贴图窗口（置顶/拖动/缩放）
ImageViewerForm.cs            图片查看器主窗口（标题栏/画布/信息卡片/工具条）
ImageViewerParts.cs           画布（缩放/平移/裁剪选框）、信息面板、胶囊工具条
ImageToolKit.cs               图片读写/编码/缩放/旋转/裁剪/命名（无 UI 依赖）
ImageToolDialogs.cs           转格式 / 压缩 / 改尺寸 / 重命名 / 提示框
DarkUi.cs                     深色调色板、按钮、输入框、矢量图标、无边框窗口基类
AppInfo.cs                    产品名「工具侠」与内部标识（界面文字统一从这里取）
ShellIntegration.cs           图片右键菜单 + 打开方式注册（HKCU，可开关、可自愈路径）
FirstRunSetup.cs              首次运行自动开启三个开关（带 FirstRunDone 标记）
AppConfig.cs / SettingsForm.cs 配置（两套凭证、剪贴板 TTL / 上限 / 快捷键、双击 Ctrl 开关）与设置界面
ClipboardHistory.cs            剪贴板条目模型、clipboard.json 持久化、分类 / 搜索 / 排序 / TTL 纯函数
ClipboardWatcher.cs            WM_CLIPBOARDUPDATE 监听、采样入库、抑制自身写回、文本 / 图片 / 文件写回
ClipboardPasteFiles.cs         CF_HDROP 多格式写回：图片 / 文本可粘贴成桌面文件，临时文件清理
DoubleCtrlWatcher.cs           双击 Ctrl 纯状态机 + WH_KEYBOARD_LL 全局键盘钩子
SoftwareCatalog.cs             360 宝库搜索接口 + JSON 解析 / 文件名等纯函数
SoftwareDownloader.cs          安装包流式下载（进度 / 取消 / 临时文件 / 重命名）
SoftwareDownloadForm.cs        软件下载窗口（搜索 / 列表 / 点击即下 / 右键菜单）
ClipboardHistoryForm.cs        剪贴板历史窗口（分类标签 / 搜索 / 日期分组 / 缩略图 / 双击复制 / TTL 菜单）
TrayApplicationContext.cs     托盘、全局热键、双击 Ctrl 钩子与流程编排
SelfTest.cs                    自检用例（410 项）与各类探针
TaskbarStats.cs / TaskbarStatsForm.cs  状态条采样、统一 MB 单位、紧凑列宽排版、定位与分层透明窗口
TrafficDayTracker.cs          今日流量按网卡累计与按天持久化（traffic.json）
GpuSampler.cs / LayeredWindow.cs  GPU 计数器采样 / 分层窗口绘制与 topmost Z 序维护
TaskbarStatsDetailForm.cs     悬停详情卡片（紧凑圆角、深浅主题、靠右展开并保留重叠）
ImageTest.cs                   真实请求诊断（--imagetest / --typetest / --viewerpreview 等）
IconFactory.cs / app.ico      托盘与 exe 图标（多尺寸）
build.ps1                     使用系统 csc.exe 编译并嵌入图标
app.manifest                  DPI 与 Windows 兼容性声明
tools/MakeIcon.cs             图标生成器（app.ico 缺失时自动调用）
dist/                         编译输出（已 gitignore）
```
