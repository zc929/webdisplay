# 第三方组件与许可说明

WebDisplay 2.0.0 使用 C#、.NET 10、WinUI 3（Windows App SDK 2.4.0）和 Microsoft Edge WebView2。应用界面已迁移到 WinUI 3；此前 WPF 版本的备份位于 `artifacts`。

| 组件 | 本版本用途及许可来源 |
| --- | --- |
| Microsoft .NET 10 | 自包含发布中的托管运行时及基础类库。[运行时许可证](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)、[第三方声明](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)。 |
| Microsoft.WindowsAppSDK 2.4.0 | WinUI 3 控件、窗口管理及 Windows App SDK 运行时组件。[该版本许可](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.4.0/License)、[微软项目仓库](https://github.com/microsoft/WindowsAppSDK)。 |
| Microsoft.WindowsAppSDK.WinUI 2.3.6 | 由 Windows App SDK 2.4.0 引入的 WinUI 组件版本。[该版本许可](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/2.3.6/License)、[WinUI 源码仓库](https://github.com/microsoft/microsoft-ui-xaml)。 |
| Microsoft.Web.WebView2 1.0.3719.77 | 由 WinUI 包引入的 WebView2 SDK，用于嵌入网页。[该版本许可](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3719.77/License)。 |
| Microsoft.Windows.SDK.BuildTools 10.0.26100.9169 | 构建时的 Windows SDK 工具，不要求用户安装完整开发工具链。[包信息及许可入口](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.26100.9169)。 |
| Microsoft Edge WebView2 Runtime | 网页运行组件，由目标电脑单独安装和维护，未打包进 WebDisplay EXE。[下载与许可信息](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)。 |

Windows App SDK 总包还会引入 Foundation、Base、InteractiveExperiences、DWrite、Widgets、AI、ML、Search 等组件及其依赖。实际自包含输出可能包含 ONNX Runtime、DirectML 等原生文件；WebDisplay 不调用这些组件来执行 AI 功能。组件版本与依赖关系以项目还原结果及各 NuGet 包为准。

发布程序将 .NET 与 Windows App SDK 运行依赖打包到单 EXE，启动时解压使用。版权、许可和第三方声明仍归各自权利人所有；单文件发布不会改变原有许可。微软项目的开源仓库许可证与其 NuGet 二进制分发许可可能不同，应以所分发组件附带的许可文件为准。

发布包中的 `licenses` 目录提供构建时收集的原始许可及声明；本文件用于定位组件和许可来源，不替代这些原文。Windows 自带 API（任务计划程序、注册表、LSA 和电源管理）由目标系统提供；WebDisplay 使用这些 API 实现系统设置，没有分发 Sysinternals Autologon 工具。
