# WebDisplay 网页展示程序 2.0.0

基于 C#、.NET 10、WinUI 3（Windows App SDK 2.4.0）和 Microsoft Edge WebView2 的 Windows 桌面程序，用于在展示屏上持续打开指定网页。2.0.0 将界面迁移到 WinUI 3，保留网页展示与 Windows 系统管理功能。

## 功能

- 设置展示网址，按分钟开启或关闭定时刷新。
- 切换窗口置顶、全屏和阻止电脑自动休眠。
- 跟随系统、浅色、深色三种主题，设置时实时预览。
- 断网或网页加载失败后自动重试；浏览器进程异常后恢复显示。
- 在通知区域托盘中管理程序。
- 设置登录 Windows 后自动启动。
- 设置定时重启电脑。
- 配置 Windows 自动登录，包括账号、密码和域。

首次运行时打开设置窗口。开机启动、定时重启和 Windows 自动登录默认关闭。展示设置与开机启动随“保存”应用；定时重启和 Windows 自动登录使用各自的管理员操作按钮立即应用，之后取消设置窗口不会撤销已经应用的系统操作。

## 运行

1. 将发布包中的**全部文件**解压到一个固定位置，例如 `C:\Apps\WebDisplay`。
2. 运行 `WebDisplay.exe`，填写网址并保存设置。
3. 根据需要开启刷新、置顶、全屏等选项。
4. 确认程序所在位置稳定后，再启用开机启动；移动或删除程序可能使启动项失效。

发布目录为 `dist\WebDisplay-WinUI3-win-x64`，入口为 `WebDisplay.exe`。此版本面向 Windows x64，项目最低 Windows 目标版本为 `10.0.19041.0`。程序采用自包含单 EXE 发布，内含 .NET 10 与 Windows App SDK 2.4.0，无需另装这两个运行时。

单 EXE 首次运行会把依赖解压到 .NET 管理的本地目录，因此首次启动可能略慢。**WebView2 Runtime 仍需要单独安装**。如果目标电脑尚未安装，请从[微软 WebView2 下载页](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)安装 Evergreen Runtime。

发布包为 `dist\WebDisplay-WinUI3-win-x64.zip`，源码包为 `dist\WebDisplay-WinUI3-source.zip`。源码项目入口是 `src\WebDisplay\WebDisplay.csproj`。原 WPF 版本的代码与发布备份保存在 `artifacts` 中。此构建未做商业代码签名；首次运行的发布者信息不会显示受信任的软件厂商名称。

网页本身的访问权限、网络连接和网站登录仍由对应网站决定。Windows 自动登录只负责进入 Windows 桌面，不会自动填写网站账号密码。

## 界面主题

设置中提供 `System`（跟随系统）、`Light`（浅色）和 `Dark`（深色）。选择后即时预览主窗口及设置窗口的配色；点击“保存”保留选择，点击“取消”或关闭设置窗口恢复此前保存的主题。跟随系统模式会响应 Windows 的应用颜色设置变化。

程序同时把配色偏好传给 WebView2。支持系统配色偏好的网站可随之切换；程序不会注入 CSS 强行改变网页。网站如果固定使用浅色、深色或自己的主题设置，仍以网站行为为准。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `F11` | 切换全屏 |
| `Esc` | 退出全屏 |
| `Ctrl` + `,` | 打开设置 |
| `Ctrl` + `R` | 刷新网页 |

也可以使用任务栏通知区域中的托盘图标进行操作。

## 系统设置与权限

### 开机启动

展示窗口需要在用户登录 Windows 后运行。无人值守的完整流程是：电脑启动 → Windows 自动登录 → WebDisplay 自动启动 → 打开网页。仅启用程序开机启动不会跳过 Windows 登录界面。

启动项写入当前 Windows 用户的注册表，仅对当前账号生效。如果自动登录配置的是另一个账号，需要先进入该账号的 Windows 会话，在那里配置展示网址并启用开机启动。

### 定时重启

重启由 Windows 任务计划程序执行，因此不依赖展示窗口一直保持打开。创建或修改需要管理员权限的任务时，会出现 Windows UAC 提示；取消后不会完成该次管理员操作。

支持每天或每周指定日期，按电脑本地时间执行。错过的计划不会在下次开机时补执行。

重启使用 `shutdown /r /t 0`，不添加强制关闭参数。存在未保存内容的其他程序可能阻止或延迟重启，因此需要在实际展示电脑上验证。

### Windows 自动登录

配置 Windows 自动登录需要管理员权限。程序通过管理员辅助进程应用相关系统设置，日常网页展示无需持续使用管理员权限。

密码由 Windows LSA Secret 保存，不写入程序普通设置文件或日志。本机管理员仍可能提取该密码；建议使用专门的展示账号。域策略、登录横幅及账号限制可能导致自动登录无法生效，不能通过此设置绕过组织策略。

请在目标电脑上确认账号、密码和域有效，并自行安排重启验证。启用自动登录后，能够直接接触电脑的人可以进入该账号的桌面。相关机制见[微软 Autologon 说明](https://learn.microsoft.com/en-us/sysinternals/downloads/autologon)。

开发及自动检查不会实际启用此电脑的开机启动、定时重启或 Windows 自动登录。管理员功能需要用户在合适的测试环境中手动选择启用并验证。

## 设置与日志

开启阻止休眠时，只有展示窗口可见且没有最小化才会保持电脑和显示器唤醒；最小化窗口或退出程序会解除此请求。

默认数据目录：

```text
%LOCALAPPDATA%\WebDisplay
```

普通设置与运行日志保存在此目录，Windows 自动登录密码不保存在其中。`BrowserProfile` 子目录保存 WebView2 的网站会话等浏览数据。排查故障时可检查日志；对外分享前请留意网址或错误信息中是否包含内部业务信息。

可为测试指定独立的数据目录：

```powershell
.\WebDisplay.exe --data-dir C:\Temp\WebDisplay-Test
```

`--data-dir` 只改变应用数据位置，不隔离操作系统设置。测试时仍应保持开机启动、定时重启和自动登录关闭。

## 构建与自检

开发环境需要 Windows、PowerShell 7 和 .NET 10 SDK；首次还原需要访问 NuGet。本项目使用 `Microsoft.WindowsAppSDK 2.4.0` 和 `Microsoft.Windows.SDK.BuildTools 10.0.26100.9169`，目标框架为 `net10.0-windows10.0.26100.0`。

已使用 .NET SDK 10.0.401 在未安装 Visual Studio 或 Windows Kits 的本机完成 WinUI 3 CLI 构建及单 EXE 启动验证。XAML 编译器与 Windows 构建工具来自 NuGet；XAML 编译器需要 .NET Framework 4.7.2 或更高版本，本机已有兼容版本。这项探针验证不替代最终程序验收，最终检查记录见 `VALIDATION.md`。

构建并发布 Windows x64 版本：

```powershell
.\scripts\build.ps1
```

单文件发布同时要求 .NET 和 Windows App SDK 自包含，并保留以下关键设置：

```xml
<UseWinUI>true</UseWinUI>
<WindowsPackageType>None</WindowsPackageType>
<SelfContained>true</SelfContained>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<EnableMsixTooling>true</EnableMsixTooling>
<PublishSingleFile>true</PublishSingleFile>
<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
<PublishTrimmed>false</PublishTrimmed>
```

使用 `win-x64` 运行时及 x64 平台发布。单文件依赖启动时解压，不能省略 `IncludeAllContentForSelfExtract`。构建不需要把程序打成 MSIX，也不需要安装系统级 Windows App SDK Runtime。机制说明见[微软单文件发布文档](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)。

发布后运行安全自检：

```powershell
.\dist\WebDisplay-WinUI3-win-x64\WebDisplay.exe --self-test
```

`--self-test` 执行不依赖展示窗口的安全检查，不实际设置自动登录、启动项或重启电脑。自检不能替代目标机器上的界面与管理员功能验证。

自检结果写入数据目录中的 `self-test-result.json`。真实浏览器测试可运行 `--smoke-test`，详见 `VALIDATION.md`。发布程序已完成的检查和未实际执行的管理员功能验收也记录在该文件中。

## 手动验收

建议先使用独立数据目录，并保持系统功能关闭。

- 首次启动出现设置；保存网址后正常加载，关闭重开后仍保留设置。
- 开启短间隔刷新并观察一次；关闭后确认不再定时刷新。
- 验证置顶、全屏、快捷键以及托盘入口。
- 依次预览 System、Light、Dark；取消后恢复原主题，保存后重新打开仍保留选择。跟随系统时切换 Windows 配色，检查主窗口与设置窗口同步更新。
- 对支持系统配色的网站验证网页主题切换；固定主题的网站无需被强制改色。
- 暂时断开网络，恢复连接后确认网页能重新加载。
- 在测试环境中结束 WebDisplay 所属的浏览器进程，确认页面自动恢复；避免结束其他程序使用的 WebView2 进程。
- 开启和关闭阻止休眠，确认开关状态及程序退出后的行为符合预期。
- 触发管理员设置并取消 UAC，确认程序仍可使用，且显示操作未完成。
- 在获准的测试电脑上逐项开启启动项、定时重启和 Windows 自动登录，检查实际效果，再逐项关闭并确认设置已撤销。重启测试前先保存其他程序中的工作。

企业域账号、网站认证和长时间无人值守运行应在实际部署环境中进一步验证。
