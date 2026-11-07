# 选中文本翻译（Windows）

一个轻量的 Windows 托盘小工具：在任意应用里选中文字，旁边会自动浮出一个「译」按钮，点击即可用百度翻译 API 翻译。也支持右键、全局快捷键，以及**截图翻译**（框选屏幕 → 标注编辑 → 识别图中文字并翻译）。

## 为什么不是系统右键菜单

Windows 没有公开接口可以往**所有应用**的文本右键菜单里统一插入菜单项。各应用的右键菜单由应用自己绘制，无法安全地全局注入。

所以本工具采用：

- **选中即显示悬浮按钮**：轮询检测当前焦点元素的选区（带防抖），选中稳定后自动在鼠标附近显示「译」按钮，无需右键。点击按钮即翻译；选区清空或切换后按钮自动消失。
- **右键悬浮按钮**（可关闭）：右键按下时通过 Windows UI Automation 读取选中文本；读不到时自动用一次 `Ctrl+C` 兜底（会备份并恢复原剪贴板）。读到文本后，在鼠标附近显示「译」按钮，并关闭应用自己弹出的右键菜单，避免菜单挡住按钮。
- **全局快捷键（可自定义）**：在设置中点击快捷键输入框后按下任意组合键即可更改。默认 `Ctrl+Alt+T` 翻译选中文本，`Ctrl+Alt+E` 打开手动翻译窗口，`Ctrl+Alt+S` 开始截图翻译。支持 Ctrl、Alt、Shift、Win 修饰符 + 任意按键，三个快捷键不能重复。

## 截图翻译

按 `Ctrl+Alt+S`（或托盘菜单「截图翻译…」）进入全屏截图浮层：

1. **框选**：按住鼠标**左键或右键**拖拽。松开后选区锁定（一次快捷键只截一张图），光标由十字变回普通箭头，选区出现蓝色边框、8 个缩放手柄和 `宽 × 高` 尺寸标签，下方自动浮出图标工具栏。手柄上仍显示缩放光标，选区内拖动可移动选区；右键单击只退出当前标注工具，不会重新框选。想再截一张请重按 `Ctrl+Alt+S`。
2. **就地标注**：工具栏从左到右是矩形、椭圆、箭头、画笔、文字、序号、高亮、马赛克、撤销、裁剪、颜色、线宽、保存、钉在桌面、翻译、取消、完成。`Ctrl+Z` 撤销，数字键 `1`–`8` 快速切换工具；颜色有 6 种、线宽 3 档。
3. **结束方式**：
   - **完成 ✓**：合成图复制进剪贴板，并打开截图编辑器继续精细编辑；
   - **译**：打开编辑器并立即调用百度图片翻译；
   - **钉 📌**：把合成图钉在桌面上（无边框置顶小窗）；
   - **保存 ↓**：直接导出 PNG 并退出；
   - **取消 ✕ / Esc**：放弃本次截图。

**桌面贴图**：左键拖动移动、`Ctrl` + 滚轮缩放、右键菜单可复制 / 保存 / 调整透明度 / 切换置顶 / 缩放 / 关闭，`Esc` 或双击关闭；鼠标悬停时右上角出现关闭按钮。托盘菜单「关闭所有桌面贴图」可一次清理。再次按截图快捷键时贴图会临时隐藏，不会被拍进新截图。编辑器底部也有「钉在桌面」按钮。

截图编辑器内仍可裁剪、撤销、复制合成图，并展示 OCR 原文、分段译文、汇总译文及「整图贴合」译文图，可分别复制译文和译文图。

> 翻译时发送的是**未标注的原图**（浮层会单独保留一份干净裁剪图），避免矩形框、马赛克、文字被当成正文识别。图片会自动缩放到长边 ≤4096px、体积 ≤4M；整图贴合失败时自动降级为仅返回文本结果。

> 图片翻译服务需在百度翻译开放平台单独开通，与「通用翻译」是不同的服务。图片接口的目标语种代码与文本接口不同（日语 `jp`、韩语 `kor`、法语 `fra`、西班牙语 `spa`），设置界面已按图片接口列出可用语种；图片接口不支持 `auto`，本工具会自动按「中文→英文，其他→中文」处理。

## 功能

- 任意应用选中文本 → 自动浮出「译」按钮，点击即翻译（无需右键）
- 全局快捷键翻译选中文本（默认 `Ctrl+Alt+T`，可在设置中自定义）
- 全局快捷键打开手动翻译窗口（默认 `Ctrl+Alt+E`，可在设置中自定义）
- 全局快捷键截图翻译（默认 `Ctrl+Alt+S`，可在设置中自定义）
- 截图浮层：左键/右键框选、选区锁定、8 手柄调整、尺寸标签、17 个线性图标就地标注（含序号、马赛克、裁剪、贴图）
- 桌面贴图：置顶无边框小窗，可拖动、缩放、调透明度，托盘菜单一键关闭全部
- 截图编辑器：裁剪、撤销、保存、复制合成图、贴图、图片翻译结果面板
- 托盘菜单：翻译选中文本、手动翻译、截图翻译…、翻译剪贴板、关闭所有桌面贴图、设置、开机启动、退出
- 百度翻译 API（通用翻译 + 图片翻译）
- 自动目标语言：中文→英文，其他→中文；也可固定目标语言（文本与图片分别设置）
- 密钥使用 Windows DPAPI 加密后保存在当前用户目录
- 翻译结果窗口支持一键复制原文/译文，并可编辑原文后重新翻译
- 超长文本自动分段翻译（6000 UTF-8 字节上限）
- 可在设置中开关「选中即显示」和「右键显示」两种触发方式
- 可在设置中自定义三个全局快捷键（支持 Ctrl/Alt/Shift/Win + 任意键）

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

## 首次使用

1. 打开 [百度翻译开放平台](https://fanyi-api.baidu.com/manage/developer)，注册并创建应用。
2. 开通「通用翻译」服务，拿到 **AppID** 和 **密钥**；如需截图翻译，再开通「图片翻译」服务。
3. 运行 `SelectionTranslator.exe`。
4. 右键托盘图标 →「设置…」，填入 AppID、密钥，选择图片翻译目标语言，点击「测试连接」确认可用后保存。
5. （可选）在设置中自定义快捷键：点击任一快捷键输入框，按下想要的组合键即可。
6. 在任意应用中选中文字：
   - 选中后自动浮出「译」按钮，点击即可翻译；
   - 按快捷键（默认 `Ctrl+Alt+T`）；或
   - 右键，点击浮出的「译」按钮（如果设置了允许右键触发）。

   没有现成文本可选中时，按快捷键（默认 `Ctrl+Alt+E`）打开手动翻译窗口，输入文本后回车即翻译。

   文字在图片里、无法选中时，按 `Ctrl+Alt+S`，用左键或右键拖拽框选，松开后点浮层工具栏里的「译」即可识别并翻译；点「完成 ✓」则进入编辑器再手动翻译。

## 百度翻译 API

请求地址：

```text
https://fanyi-api.baidu.com/api/trans/vip/translate
```

签名规则：

```text
sign = MD5(appid + q + salt + 密钥)
```

本工具使用 `from=auto` 自动识别源语言，使用 `to` 指定目标语言。单次请求上限为 6000 UTF-8 字节；超长文本会自动在换行或句末标点处分段翻译，无需手动处理。可在「设置」中调整最大文本长度（字符数）。

### 图片翻译接口

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

## 配置与日志

```text
%APPDATA%\SelectionTranslator\config.json
%APPDATA%\SelectionTranslator\app.log
```

`config.json` 中的密钥是 DPAPI 加密后的 Base64，只能由当前 Windows 用户解密。不要直接把该文件发给别人。

## 自检

```powershell
.\dist\SelectionTranslator.exe --selftest
Get-Content "$env:APPDATA\SelectionTranslator\selftest.txt"
```

自检覆盖文本翻译 MD5 签名、自动目标语言判断、DPAPI 加解密、文本分段，图片翻译的 MD5 签名、固定参数（cuid/mac）、语种映射（ja→jp 等）与目标语种校验，以及截图浮层的构造与选区导出冒烟测试。

图片翻译报 `54001`、`52003` 等错误时，可用真实请求诊断（使用当前已保存的 AppID / 密钥，结果写入 `imagetest.txt`，不输出密钥）：

```powershell
.\dist\SelectionTranslator.exe --imagetest "C:\path\to\screenshot.png"
Get-Content "$env:APPDATA\SelectionTranslator\imagetest.txt"
```

返回 `RESULT: OK` 即凭证、签名与图片翻译服务都正常；失败时文件里会有完整异常信息。

## 已知限制

- 「选中即显示」依赖 UI Automation 读取焦点元素选区；如果应用不暴露选区（部分游戏、远程桌面、Electron 应用等），请改用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 右键读取优先使用 UI Automation；如果应用不暴露选区，会自动发一次 `Ctrl+C` 兜底。终端类应用（Windows Terminal、cmd、PowerShell 等）不会发 `Ctrl+C`，以免中断命令，这类应用请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 以管理员身份运行的目标程序，普通权限的工具无法向它发送 `Ctrl+C`（Windows UIPI 限制）。如有需要，让本工具也以管理员身份运行。
- 远程桌面、部分游戏可能无法读取选区，请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 全局快捷键被其他程序占用时，托盘菜单仍可使用；日志会记录注册失败。可在设置中改为其他组合。
- Win 键作为修饰符可能被系统拦截（如 Win+E 会打开资源管理器），建议使用 Ctrl+Alt+Shift 组合。
- 翻译文本和图片会发送到百度翻译服务器，请勿翻译敏感内容。
- 图片翻译是独立服务，需在开放平台单独开通；仅开通「通用翻译」时截图翻译会返回 52003 未授权。
- 图片翻译目标语言不支持 `auto`，选择「自动」时本工具会先按中文→英文请求，识别到非中文源语言后再按其他→中文重试，因此自动模式可能产生两次请求。
- 图片过窄（长宽比超过 3:1）、文字过小或倾斜、背景复杂时，OCR 与贴合效果会下降。
- 截图编辑器中的马赛克仅作用于当前显示的截图，不会修改系统剪贴板中的原始内容。

## 文件结构

```text
BaiduTranslator.cs            文本翻译 API
BaiduImageTranslator.cs       图片翻译 API（签名/multipart/预处理/解析）
ScreenshotCaptureForm.cs      一体化截图浮层（框选/标注/工具栏）
ScreenshotEditorForm.cs       截图编辑器（标注/裁剪/翻译结果）
PinnedImageForm.cs            桌面贴图窗口（置顶/拖动/缩放）
AppConfig.cs / SettingsForm.cs 配置与设置界面
TrayApplicationContext.cs     托盘、全局热键与流程编排
SelfTest.cs                    自检用例
ImageTest.cs                   图片翻译真实请求诊断（--imagetest）
build.ps1                     使用系统 csc.exe 编译
app.manifest                  DPI 与 Windows 兼容性声明
dist/                         编译输出（已 gitignore）
```
