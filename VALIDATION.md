# WebDisplay 1.0 验证记录

验证环境：Windows x64，.NET SDK 10.0.401，Microsoft.Web.WebView2 1.0.4191.47，已安装 WebView2 Runtime 152。

## 已完成

- Release 自包含单文件发布成功，编译无警告、无错误。
- 15 项安全自检通过：网址校验、刷新间隔、配置保存和损坏恢复、重试间隔、每天/每周重启规则以及密码不进入普通配置模型。
- 11 项系统服务检查通过：命名管道 ACL、客户端/服务端进程 ID 校验、非法操作拒绝、任务时间和参数，以及 Windows 任务计划程序对每日和每周 XML 的 `TASK_VALIDATE_ONLY` 验证（没有注册任务）。
- 9 项真实 WebView2 运行检查通过：HTTP 网页与 JavaScript、定时刷新、关闭刷新、本地测试网站不可达后自动恢复、结束该测试实例的浏览器进程后自动恢复、全屏还原、置顶、电源请求开启/释放、两个设置页渲染。
- 使用真实 Windows 窗口验证网页内容、设置页滚动、网页获得焦点时的 F11 全屏、Ctrl+, 打开设置、Esc 关闭设置与退出全屏，以及正常退出。
- 设置窗口按当前显示器工作区和 DPI 调整初始大小；窗口底部操作按钮保持可见。

网络恢复测试只停止并恢复本机回环地址上的测试 HTTP 服务，没有断开用户电脑的网络连接。浏览器崩溃测试只终止测试实例通过 WebView2 API 返回的浏览器进程。

## 部署时还需验证

未修改开发电脑的开机启动、自动登录或重启任务，也没有重启电脑。实际 UAC 授权/取消、跨管理员账号授权、自动登录与实际计划重启，需在目标电脑上明确启用后验证。域策略、网站登录、长期连续运行、多显示器混合 DPI 和缺失 Runtime 的安装流程尚未做完整部署验收。

## 重现

```powershell
.\scripts\build.ps1
.\dist\WebDisplay-win-x64\WebDisplay.exe --self-test --data-dir .\artifacts\manual-self-test
.\dist\WebDisplay-win-x64\WebDisplay.exe --smoke-test --data-dir .\artifacts\manual-smoke-test
```

`--self-test` 输出 `self-test-result.json`。`--smoke-test` 输出 `smoke-test-result.json` 和窗口预览 PNG，过程会临时显示窗口、开启后释放防休眠请求，并终止/重建本测试实例的浏览器进程。应在正常交互式 Windows 会话运行，不能用受限进程沙箱的 WebView2 故障来判定正常桌面下的运行结果。

开发会话的系统服务测试工具源码保存在 `artifacts/servicecheck`，不随用户发布包分发。自动验证不等同于长时间运行或目标企业域验收。
