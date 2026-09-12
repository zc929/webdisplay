# WebDisplay 2.0.0 WinUI 3 验证记录

验证环境：Windows 11 x64，.NET SDK 10.0.401，Windows App SDK 2.4.0，Microsoft.Windows.SDK.BuildTools 10.0.26100.9169，传递依赖 Microsoft.Web.WebView2 1.0.3719.77，已安装 WebView2 Runtime 152。

## 已完成

- WinUI 3 Release 编译通过，无警告、无错误。主窗口和设置窗口均为 Microsoft.UI.Xaml.Window；不再依赖 WPF 或 Windows Forms。
- 自包含单 EXE 已发布；直接运行发布目录中的 WebDisplay.exe，通过下列 16 项自检及 13 项真实运行检查，退出码均为 0。
- 16 项安全自检：网址校验、刷新间隔、配置保存和损坏恢复、重试间隔、每天/每周重启规则，以及新增主题选项、旧配置兼容和主题持久化。
- 13 项真实 WinUI 3 / WebView2 运行检查通过：HTTP 网页和 JavaScript、定时刷新及关闭刷新、本地网站不可达后自动恢复、结束本测试实例的浏览器进程后重建、全屏还原、置顶窗口样式、电源请求开启/释放、最小化释放/恢复展示重新申请电源请求、深色/浅色窗口和网页配色同步、跟随系统选项、取消设置恢复原主题。
- 设置窗口的深浅色两页均生成 PNG 并检查；窗口底部操作按钮保持可见。真实窗口中检查深色原生标题栏、设置页切换与滚动、账号设置区域、取消返回展示以及正常退出。
- 已在网页获得焦点时验证 F11 全屏、全屏状态下 Ctrl+, 打开设置、Esc 退出全屏。快捷键仅在本应用窗口前台生效，使用当前 UI 线程消息处理，没有注册全局热键。
- 浏览器环境和控件初始化分别有 30 秒、45 秒超时，失败后进入自动重试，避免初始化阶段无限等待。
- 迁移沿用原系统服务代码。该代码已通过 11 项系统服务检查：命名管道 ACL、客户端/服务端进程 ID 校验、非法操作拒绝、任务时间和参数，以及任务计划程序对每日和每周 XML 的 TASK_VALIDATE_ONLY 验证（没有注册任务）。

网络恢复测试只停止并恢复本机回环地址上的测试 HTTP 服务，没有断开用户电脑的网络连接。浏览器崩溃测试只终止本测试实例通过 WebView2 API 返回的浏览器进程。自动截图会等待 WinUI Pivot 切页动画结束。

## 部署时还需验证

未修改开发电脑的开机启动、自动登录或重启任务，也没有重启电脑。实际 UAC 授权/取消、跨管理员账号授权、自动登录与实际计划重启，需在目标电脑上明确启用后验证。域策略、网站登录、长期连续运行、多显示器混合 DPI、系统配色在程序运行中切换以及缺失 Runtime 的安装流程尚未做完整部署验收。

## 重现

```powershell
.\scripts\build.ps1
.\dist\WebDisplay-WinUI3-win-x64\WebDisplay.exe --self-test --data-dir .\artifacts\manual-self-test
.\dist\WebDisplay-WinUI3-win-x64\WebDisplay.exe --smoke-test --data-dir .\artifacts\manual-smoke-test
```

`--self-test` 输出 `self-test-result.json`。`--smoke-test` 输出 `smoke-test-result.json` 和窗口预览 PNG，过程会临时显示窗口、开启后释放防休眠请求，并终止/重建本测试实例的浏览器进程。应在正常交互式 Windows 会话运行，不能用受限进程沙箱的 WebView2 故障来判定正常桌面下的运行结果。

开发会话的系统服务测试工具源码保存在 `artifacts/servicecheck`，不随用户发布包分发。自动验证不等同于长时间运行或目标企业域验收。
