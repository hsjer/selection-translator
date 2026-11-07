# 选中文本翻译（Windows）

一个轻量的 Windows 托盘小工具：在任意应用里选中文字，旁边会自动浮出一个「译」按钮，点击即可用百度翻译 API 翻译。也支持右键和全局快捷键触发。

## 为什么不是系统右键菜单

Windows 没有公开接口可以往**所有应用**的文本右键菜单里统一插入菜单项。各应用的右键菜单由应用自己绘制，无法安全地全局注入。

所以本工具采用：

- **选中即显示悬浮按钮**：轮询检测当前焦点元素的选区（带防抖），选中稳定后自动在鼠标附近显示「译」按钮，无需右键。点击按钮即翻译；选区清空或切换后按钮自动消失。
- **右键悬浮按钮**（可关闭）：右键按下时通过 Windows UI Automation 读取选中文本；读不到时自动用一次 `Ctrl+C` 兜底（会备份并恢复原剪贴板）。读到文本后，在鼠标附近显示「译」按钮，并关闭应用自己弹出的右键菜单，避免菜单挡住按钮。
- **全局快捷键（可自定义）**：在设置中点击快捷键输入框后按下任意组合键即可更改。默认 `Ctrl+Alt+T` 翻译选中文本，`Ctrl+Alt+E` 打开手动翻译窗口。支持 Ctrl、Alt、Shift、Win 修饰符 + 任意按键。

## 功能

- 任意应用选中文本 → 自动浮出「译」按钮，点击即翻译（无需右键）
- 全局快捷键翻译选中文本（默认 `Ctrl+Alt+T`，可在设置中自定义）
- 全局快捷键打开手动翻译窗口（默认 `Ctrl+Alt+E`，可在设置中自定义）
- 托盘菜单：翻译选中文本、手动翻译、翻译剪贴板、设置、开机启动、退出
- 百度翻译 API（通用翻译）
- 自动目标语言：中文→英文，其他→中文；也可固定目标语言
- 密钥使用 Windows DPAPI 加密后保存在当前用户目录
- 翻译结果窗口支持一键复制原文/译文，并可编辑原文后重新翻译
- 超长文本自动分段翻译（6000 UTF-8 字节上限）
- 可在设置中开关「选中即显示」和「右键显示」两种触发方式
- 可在设置中自定义两个全局快捷键（支持 Ctrl/Alt/Shift/Win + 任意键）

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
2. 开通「通用翻译」服务，拿到 **AppID** 和 **密钥**。
3. 运行 `SelectionTranslator.exe`。
4. 右键托盘图标 →「设置…」，填入 AppID、密钥，点击「测试连接」确认可用后保存。
5. （可选）在设置中自定义快捷键：点击「翻译选中文本快捷键」或「手动翻译快捷键」输入框，按下想要的组合键即可。
6. 在任意应用中选中文字：
   - 选中后自动浮出「译」按钮，点击即可翻译；
   - 按快捷键（默认 `Ctrl+Alt+T`）；或
   - 右键，点击浮出的「译」按钮（如果设置了允许右键触发）。

   没有现成文本可选中时，按快捷键（默认 `Ctrl+Alt+E`）打开手动翻译窗口，输入文本后回车即翻译。

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

自检覆盖 MD5 签名、自动目标语言判断和 DPAPI 加解密。

## 已知限制

- 「选中即显示」依赖 UI Automation 读取焦点元素选区；如果应用不暴露选区（部分游戏、远程桌面、Electron 应用等），请改用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 右键读取优先使用 UI Automation；如果应用不暴露选区，会自动发一次 `Ctrl+C` 兜底。终端类应用（Windows Terminal、cmd、PowerShell 等）不会发 `Ctrl+C`，以免中断命令，这类应用请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 以管理员身份运行的目标程序，普通权限的工具无法向它发送 `Ctrl+C`（Windows UIPI 限制）。如有需要，让本工具也以管理员身份运行。
- 远程桌面、部分游戏可能无法读取选区，请用 `Ctrl+Alt+T` 或 `Ctrl+Alt+E`。
- 全局快捷键被其他程序占用时，托盘菜单仍可使用；日志会记录注册失败。可在设置中改为其他组合。
- Win 键作为修饰符可能被系统拦截（如 Win+E 会打开资源管理器），建议使用 Ctrl+Alt+Shift 组合。
- 翻译文本会发送到百度翻译服务器，请勿翻译敏感内容。

## 文件结构

```text
SelectionTranslator.cs 相关源文件
build.ps1              使用系统 csc.exe 编译
app.manifest           DPI 与 Windows 兼容性声明
dist/                  编译输出（已 gitignore）
```
