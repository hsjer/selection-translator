# 选中文本翻译（Windows）

一个轻量的 Windows 托盘小工具：在任意应用里选中文字，旁边会自动浮出一个「译」按钮，点击即可用百度翻译 API 翻译。也支持右键、全局快捷键，以及**截图翻译**（框选屏幕 → 就地标注 → 识别图中文字并把译文直接显示在图片上，可选中复制）。截图后也可以**只取文字**：一键把 OCR 识别出的原文复制到剪贴板，不弹窗口、不显示译文。

图片文字处理采用**双服务分工**：**腾讯云 GeneralAccurateOCR** 负责识别文字和坐标，**百度通用文本翻译**负责把识别出的文字逐行翻译；两者拼在一起把译文叠加回原图。只取字时只调腾讯云 OCR，完全不调用翻译接口。未配置腾讯云凭证（或在设置里关闭开关）时自动回退到百度图片翻译接口。

## 为什么不是系统右键菜单

Windows 没有公开接口可以往**所有应用**的文本右键菜单里统一插入菜单项。各应用的右键菜单由应用自己绘制，无法安全地全局注入。

所以本工具采用：

- **选中即显示悬浮按钮**：轮询检测当前焦点元素的选区（带防抖），选中稳定后自动在鼠标附近显示「译」按钮，无需右键。点击按钮即翻译；选区清空或切换后按钮自动消失。
- **右键悬浮按钮**（可关闭）：右键按下时通过 Windows UI Automation 读取选中文本；读不到时自动用一次 `Ctrl+C` 兜底（会备份并恢复原剪贴板）。读到文本后，在鼠标附近显示「译」按钮，并**保留应用自己的右键菜单**；只有真正点击了「译」按钮才会关闭该菜单。
- **全局快捷键（可自定义）**：在设置中点击快捷键输入框后按下任意组合键即可更改。默认 `Ctrl+Alt+T` 翻译选中文本，`Ctrl+Alt+E` 打开手动翻译窗口，`Ctrl+Alt+S` 开始截图翻译。支持 Ctrl、Alt、Shift、Win 修饰符 + 任意按键，三个快捷键不能重复。

## 截图翻译

按 `Ctrl+Alt+S`（或托盘菜单「截图翻译…」）进入全屏截图浮层：

1. **框选**：按住鼠标**左键或右键**拖拽。松开后选区锁定（一次快捷键只截一张图），光标由十字变回普通箭头，选区出现蓝色边框、8 个缩放手柄和 `宽 × 高` 尺寸标签，下方自动浮出图标工具栏。手柄上仍显示缩放光标，选区内拖动可移动选区；右键单击只退出当前标注工具，不会重新框选。想再截一张请重按 `Ctrl+Alt+S`。
2. **就地标注**：工具栏从左到右是矩形、椭圆、箭头、画笔、文字、序号、高亮、马赛克、撤销、裁剪、颜色、线宽、保存、钉在桌面、翻译、取字、取消、完成。`Ctrl+Z` 撤销，数字键 `1`–`8` 快速切换工具；颜色有 6 种、线宽 3 档。
3. **结束方式**：
   - **完成 ✓（对号）**：合成图（含标注）直接复制到剪贴板，浮层随即关闭，**不弹出任何窗口**；
   - **译**：腾讯云 OCR 识别 → 百度文本翻译逐行翻译 → 打开轻量结果窗口，译文按坐标叠加在图片上，可在图上选字复制；
   - **取字 📄**：只调腾讯云 OCR（不翻译），打开同一个可选字结果窗口：默认只显示原图 + 文字块，**由你自己点选 / 框选要复制的文字**，不会一次性把全文塞进剪贴板；
   - **钉 📌**：把合成图钉在桌面上（无边框置顶小窗）；
   - **保存 ↓**：直接导出 PNG 并退出；
   - **取消 ✕ / Esc**：放弃本次截图。

> 所有标注都在浮层里就地完成。**旧版独立的截图编辑器窗口已经移除**，截图后不会再弹出带裁剪 / 画笔 / 马赛克的编辑界面。

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

1. 按下快捷键时先做焦点判定：只有当前焦点是可编辑控件（`GetGUIThreadInfo` 拿跨进程焦点窗口 + 类名白名单 + 插入符检测）才启动录音；光标在桌面、菜单、资源管理器等地方时只给一条托盘提示，不录音。
2. 录音时屏幕底部浮出状态条（红色脉冲点 + 实时电平 + 计时），它带 `WS_EX_NOACTIVATE`，**不抢焦点**，光标一直留在原输入框。
3. 松开快捷键 → 浮条变「识别中…」→ WAV 传给腾讯云 `SentenceRecognition` → 文字通过 `SendInput(KEYEVENTF_UNICODE)` 直接注入原输入框，浮条闪一下「✓ 已输入 N 字」后自动消失。

几个设计取舍：

- **默认不碰剪贴板**：模拟键入逐字符送 UTF-16 码元，中文、emoji 代理对都能原样上屏，用户剪贴板内容不受影响。设置里可勾选「同时复制到剪贴板」。
- **个别软件不收 Unicode 注入**（终端、游戏、某些自绘控件）时，在设置的「上屏方式」里改成「剪贴板粘贴（兼容）」：临时借用剪贴板发 `Ctrl+V`，粘贴完成后自动恢复原内容。
- **切走窗口就安全放弃**：说话期间如果用户切到了别的窗口，程序不会把字打进新窗口，而是把结果放进剪贴板并提示手动粘贴。
- **修饰键先松开**：语音键是 `Ctrl+Alt+V`，注入前会等 Ctrl/Alt/Shift 释放，避免中文变成 `Ctrl+字母` 快捷键。
- **按住说话而不是点一下开始、再点一下停**：热键 API 只投递按下消息，拿不到松开消息，所以程序用 40ms 的定时器轮询 `GetAsyncKeyState`；松开即结束，最短 300ms，最长 60 秒自动收尾（接口上限）。
- **录音全程在内存里**：16kHz / 16bit / 单声道 PCM，60 秒约 1.9MB，在接口 3MB 限制内；不落盘（除非用 `--voicetest` 自测）。
- **不引入音频库**：直接 P/Invoke `winmm.dll` 的 `waveIn*`，保持「单文件、无第三方 DLL、拷走就能跑」。

> 识别走腾讯云**语音识别 ASR**，与文字识别共用同一对 SecretId / SecretKey，但需要在控制台**单独开通「语音识别」**，否则会返回 `FailedOperation.UserNotRegistered`。

## 功能

- 任意应用选中文本 → 自动浮出「译」按钮，点击即翻译（无需右键）
- 全局快捷键翻译选中文本（默认 `Ctrl+Alt+T`，可在设置中自定义）
- 全局快捷键打开手动翻译窗口（默认 `Ctrl+Alt+E`，可在设置中自定义）
- 全局快捷键截图翻译（默认 `Ctrl+Alt+S`，可在设置中自定义）
- 全局快捷键语音输入（默认 `Ctrl+Alt+V`）：只在输入框里生效，按住说话、松开即识别，文字直接上屏，不弹结果窗口
- 截图浮层：左键/右键框选、选区锁定、8 手柄调整、尺寸标签、18 个线性图标就地标注（含序号、马赛克、裁剪、贴图、取字）
- 桌面贴图：置顶无边框小窗，可拖动、缩放、调透明度，托盘菜单一键关闭全部
- 截图就地标注完成后「对号」直接复制到剪贴板，不再弹出编辑窗口
- 「取字」OCR：只用腾讯云识别文字，打开仅识别窗口，由你自己点选 / 框选要复制的文字（不自动全量复制）
- 「译」：腾讯云 OCR 出文字 + 坐标 → 百度文本翻译逐行翻译 → 译文叠加到原图上
- 图片翻译结果窗口：译文直接显示在图片上，可选字复制 / 复制图片 / 贴图 / 保存 / 重新翻译
- 翻译后在图片上点选 / 框选文字直接复制（原文或译文，按阅读顺序拼接）
- 未配置腾讯云凭证时自动回退百度图片翻译，功能不中断
- 托盘菜单：翻译选中文本、手动翻译、截图翻译…、语音识别（按住说话）、翻译剪贴板、关闭所有桌面贴图、设置、开机启动、退出
- 百度翻译 API（通用文本翻译，回退时含图片翻译）+ 腾讯云 OCR（GeneralAccurateOCR）+ 腾讯云语音识别（SentenceRecognition），全部自行实现 TC3-HMAC-SHA256 签名
- 自动目标语言：中文→英文，其他→中文；也可固定目标语言（文本与图片分别设置）
- 百度密钥、腾讯云 SecretKey 都用 Windows DPAPI 加密后保存在当前用户目录
- 翻译结果窗口支持一键复制原文/译文，并可编辑原文后重新翻译
- 超长文本自动分段翻译（6000 UTF-8 字节上限）
- 可在设置中开关「选中即显示」和「右键显示」两种触发方式
- 可在设置中自定义四个全局快捷键（支持 Ctrl/Alt/Shift/Win + 任意键）

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
dist\SelectionTranslator.exe
```

`app.ico` 是 16/24/32/48/64/128/256px 多尺寸图标，`build.ps1` 用 `/win32icon` 把它嵌进 exe（资源管理器 / 任务栏看到的图标），同时用 `/resource` 嵌入同一份数据供托盘图标复用。若 `app.ico` 缺失，构建脚本会自动编译 `tools\MakeIcon.cs` 现场生成，不依赖任何第三方库；想换图标时删掉 `app.ico` 再重新编译即可。

程序运行时 exe 被占用、无法直接覆盖，可先用环境变量把新版编译到旁路口径验证，确认无误后再替换：

```powershell
$env:ST_OUTPUT_NAME = "_next.exe"; .\build.ps1
.\dist\_next.exe --selftest
```

## 首次使用

1. 打开 [百度翻译开放平台](https://fanyi-api.baidu.com/manage/developer)，注册并创建应用，开通「通用翻译」，拿到 **AppID** 和 **密钥**。
2. （推荐，用于截图识别）打开 [腾讯云访问密钥控制台](https://console.cloud.tencent.com/cam/capi)，在 [文字识别 OCR](https://console.cloud.tencent.com/ocr) 开通服务，创建 **SecretId** 和 **SecretKey**。不填也能用，但截图识别会回退到百度图片翻译（需在百度侧单独开通该服务）。想用语音识别，再到 [语音识别控制台](https://console.cloud.tencent.com/asr) 开通服务（与 OCR 共用同一对密钥）。
3. 运行 `SelectionTranslator.exe`。
4. 右键托盘图标 →「设置…」，填入百度 AppID / 密钥，以及腾讯云 SecretId / SecretKey；选择目标语言后，可分别点「测试翻译」「测试 OCR」「测试麦克风」（会录 3 秒真实调用一次）确认可用，再保存。
5. （可选）在设置中自定义快捷键：点击任一快捷键输入框，按下想要的组合键即可。
6. 在任意应用中选中文字：
   - 选中后自动浮出「译」按钮，点击即可翻译；
   - 按快捷键（默认 `Ctrl+Alt+T`）；或
   - 右键，点击浮出的「译」按钮（如果设置了允许右键触发）。

   没有现成文本可选中时，按快捷键（默认 `Ctrl+Alt+E`）打开手动翻译窗口，输入文本后回车即翻译。

   文字在图片里、无法选中时，按 `Ctrl+Alt+S`，用左键或右键拖拽框选，松开后点浮层工具栏里的「译」即可识别并在结果窗口里看到叠在图片上的译文；只想把图里的字拿出来就点「取字 📄」，会在同一个窗口里显示识别出的文字块，你自己点选 / 框选要复制的部分；点「完成 ✓（对号）」则直接把截图复制到剪贴板。截图后不会再出现旧版编辑器。

   想说话代替打字时，**按住** `Ctrl+Alt+V` 说完再松开，底部浮条会变成「识别中…」，随后弹出结果小窗；改完错别字点「粘贴到原窗口」就能把文字送回刚才那个输入框。

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

`dist\SelectionTranslator.exe` 是单文件、无第三方 DLL，可以单独拷到别的 Windows 电脑（需自带 .NET Framework 4.8，Win10/11 已内置）；但**密钥不会跟着走**：新机器上要在设置里重新填百度 AppID / 密钥，以及（可选）腾讯云 SecretId / SecretKey。

## 自检

```powershell
.\dist\SelectionTranslator.exe --selftest
Get-Content "$env:APPDATA\SelectionTranslator\selftest.txt"
```

自检（共 140 项）覆盖文本翻译 MD5 签名、自动目标语言判断、DPAPI 加解密、文本分段，图片翻译的 MD5 签名、固定参数（cuid/mac）、语种映射与目标语种校验，截图浮层的构造与选区导出冒烟测试，exe / 托盘图标资源，结果窗口的 `rect` 解析、按阅读顺序拼接选字文本、贴合图解码与画布绘制冒烟测试，**TC3-HMAC-SHA256 签名固定向量**、腾讯云 OCR 响应解析（含 `Polygon` 回退、鉴权错误映射）、阅读顺序、目标语种归一化、管道结果拼装、两套 `Prepare` 路径。语音识别部分新增：WAV 头字节级校验（RIFF/WAVE/fmt/data、采样率、data 长度、时长换算）、引擎码归一与列表、`asr` 与 `ocr` 签名必须不同、`SentenceRecognition` 响应与错误码解析、录音浮条（含完成态）的渲染冒烟测试、焦点控件类名判定、`KEYEVENTF_UNICODE` 输入序列构造、换行/控制字符归一、上屏方式归一与设置窗口上屏方式下拉，以及仅识别模式（结果无译文、窗口不绘制译文遮罩、默认复制原文）的回归用例。

真实请求诊断（使用当前已保存的凭证，不输出密钥）：

```powershell
# 只取字（自动选引擎）
.\dist\SelectionTranslator.exe --ocrtest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\ocrtest.txt"

# 识别 + 翻译整条链路（自动选引擎）
.\dist\SelectionTranslator.exe --imagetest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\imagetest.txt"

# 只调腾讯云 OCR，逐行打印坐标与原文
.\dist\SelectionTranslator.exe --tencenttest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\tencenttest.txt"

# 验证百度「逐行文本翻译」通道（不需要腾讯云凭证）
.\dist\SelectionTranslator.exe --linetest
Get-Content "$env:APPDATA\SelectionTranslator\linetest.txt"

# 验证腾讯云签名 / 公共参数格式（用假凭证，预期 AuthFailure.*）
.\dist\SelectionTranslator.exe --tencentprobe
Get-Content "$env:APPDATA\SelectionTranslator\tencentprobe.txt"

# 只测录音链路（不需要凭证）：录 3 秒，报告设备数/有效时长/峰值电平
.\dist\SelectionTranslator.exe --voicetest 3
Get-Content "$env:APPDATA\SelectionTranslator\voicetest.txt"

# 用真实凭证识别一个 wav（不给路径则用 1 秒正弦波，只验证报文）
.\dist\SelectionTranslator.exe --asrtest "C:\path\to\speech.wav"
Get-Content "$env:APPDATA\SelectionTranslator\asrtest.txt"

# 验证 asr 的签名 / 公共参数格式（用假凭证，预期 AuthFailure.*）
.\dist\SelectionTranslator.exe --asrprobe
Get-Content "$env:APPDATA\SelectionTranslator\asrprobe.txt"

# 强制走旧的百度图片翻译回退路径
.\dist\SelectionTranslator.exe --baiduimagetest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\imagetest.txt"

# 延时 3 秒探测当前焦点（验证「只有输入框才触发」）
.\dist\SelectionTranslator.exe --focustest
Get-Content "$env:APPDATA\SelectionTranslator\focustest.txt"

# 延时 3 秒后把文字打进当前光标处（先点开记事本，再运行）
.\dist\SelectionTranslator.exe --inputtest "中文语音上屏 123"

# 真实回环：屏幕外建一个 TextBox，注入后读回文本比对
.\dist\SelectionTranslator.exe --typetest
Get-Content "$env:APPDATA\SelectionTranslator\typetest.txt"

# 离屏渲染界面预览图（ui-*.png）
.\dist\SelectionTranslator.exe --uipreview
Get-Content "$env:APPDATA\SelectionTranslator\uipreview.txt"

```

`RESULT: OK` 即该段链路正常；失败时文件里有完整异常信息。`--tencentprobe` / `--asrprobe` 用一组无效凭证发一次真实请求，收到 `AuthFailure.SecretIdNotFound` 之类的鉴权错误反而说明**签名串与公共参数格式被服务端正确解析**，可用来在没填密钥时先排除代码问题。

## 已知限制

- 「选中即显示」依赖 UI Automation 读取焦点元素选区；如果应用不暴露选区（部分游戏、远程桌面、Electron 应用等），请改用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 右键读取优先使用 UI Automation；如果应用不暴露选区，会自动发一次 `Ctrl+C` 兜底。终端类应用（Windows Terminal、cmd、PowerShell 等）不会发 `Ctrl+C`，以免中断命令，这类应用请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 以管理员身份运行的目标程序，普通权限的工具无法向它发送 `Ctrl+C`（Windows UIPI 限制）。如有需要，让本工具也以管理员身份运行。
- 远程桌面、部分游戏可能无法读取选区，请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 全局快捷键被其他程序占用时，托盘菜单仍可使用；日志会记录注册失败。可在设置中改为其他组合。
- Win 键作为修饰符可能被系统拦截（如 Win+E 会打开资源管理器），建议使用 Ctrl+Alt+Shift 组合。
- 翻译文本会发送到百度，截图会发送到腾讯云 OCR（回退时发送到百度图片翻译），请勿处理敏感内容。
- 腾讯云 OCR 需在控制台开通并配置 SecretId / SecretKey；只填百度凭证时截图功能自动回退百度「图片翻译」，而该服务需在百度开放平台单独开通，仅开通「通用翻译」时会返回 52003 未授权。
- 默认「取字」只调腾讯云 OCR，不消耗百度翻译额度，但仍需联网并按腾讯云 OCR 调用计费；关闭腾讯云开关后回退百度图片翻译，此时接口没有纯 OCR 开关，会照常计费一次翻译调用。
- 「译」在腾讯云路径下需要两次串行请求（OCR + 文本翻译），网络较差时延迟会累加；自动目标语种在回退路径下可能产生两次百度图片翻译请求。
- 逐行译文按 OCR 行一一对齐；如果接口把多行合并或某行过长，程序会做数量对齐，个别文字块可能回退显示原文而非译文。
- 图片过窄、文字过小或倾斜、背景复杂时识别效果会下降；长宽比超过 3:1 的限制只存在于百度图片翻译回退路径。
- 截图浮层中的马赛克仅作用于当前显示的截图，不会修改系统剪贴板中的原始内容。
- 语音识别需要麦克风，并且要在腾讯云控制台**单独开通「语音识别 ASR」**；只开通 OCR 时调用会返回 `FailedOperation.UserNotRegistered`。
- 语音一次最长 60 秒（接口限制），超时会自动截断收尾；连续说话请分多次录入。
- 语音输入只在检测到输入框时触发；少数不创建 Win32 插入符、也不在类名白名单里的自绘输入框可能被判为「非输入框」，可反馈软件名称以便补充白名单。
- 以管理员权限运行的程序会拦截普通权限进程的模拟键入（UIPI）；让本工具也以管理员身份运行，或改用「剪贴板粘贴」上屏方式。
- 说话期间切到别的窗口时，文字不会写入新窗口，只会放进剪贴板，手动 `Ctrl+V` 即可。
- 录音浮条不会抢焦点，所以它上面的「点这里停止」需要鼠标点击浮条本身；键盘请用「松开快捷键」结束。

## 文件结构

```text
BaiduTranslator.cs            百度文本翻译 API（含逐行翻译）
BaiduImageTranslator.cs       百度图片翻译 API（回退路径）
TencentOcr.cs                 腾讯云 GeneralAccurateOCR（TC3 签名/解析/预处理）
TencentAsr.cs                 腾讯云 SentenceRecognition（一句话识别，共用 TC3 签名）
VoiceRecorder.cs              麦克风录音（winmm waveIn，16k/16bit/mono → WAV）
VoiceCaptureForm.cs           录音浮条（不抢焦点；录音/识别/已上屏/失败四态）
InputFocusProbe.cs            焦点判定 + 文字直投（Unicode 键入 / 剪贴板粘贴）
PicturePipeline.cs            识别 + 翻译管道，统一产出结果结构
ScreenshotCaptureForm.cs      一体化截图浮层（框选/标注/工具栏）
ImageTranslationForm.cs       图片翻译结果窗口（译文贴图上/选字复制）
PinnedImageForm.cs            桌面贴图窗口（置顶/拖动/缩放）
AppConfig.cs / SettingsForm.cs 配置（两套凭证）与设置界面
TrayApplicationContext.cs     托盘、全局热键与流程编排
SelfTest.cs                    自检用例（140 项）
ImageTest.cs                   真实请求诊断（--imagetest / --typetest / --asrtest 等）
IconFactory.cs / app.ico      托盘与 exe 图标（多尺寸）
build.ps1                     使用系统 csc.exe 编译并嵌入图标
app.manifest                  DPI 与 Windows 兼容性声明
tools/MakeIcon.cs             图标生成器（app.ico 缺失时自动调用）
dist/                         编译输出（已 gitignore）
```
