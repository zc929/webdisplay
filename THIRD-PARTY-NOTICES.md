# 第三方组件与许可说明

WebDisplay 2.3.0 使用 C#、.NET 10、WinUI 3 和 Microsoft Edge WebView2，并使用 Inno Setup 制作当前用户安装包。程序以多文件方式部署，所需运行依赖与程序一起安装到固定目录。

| 组件 | 本版本用途及许可来源 |
| --- | --- |
| Microsoft .NET 10 | 自包含发布中的托管运行时及基础类库。[运行时许可证](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)、[第三方声明](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)。 |
| Microsoft.WindowsAppSDK.WinUI 2.3.6 | 直接引用的 WinUI 3 控件与界面组件。[该版本许可](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/2.3.6/License)、[WinUI 源码仓库](https://github.com/microsoft/microsoft-ui-xaml)。 |
| Microsoft.WindowsAppSDK.InteractiveExperiences 2.1.6 | 显式引入的交互组件依赖，用于完整的 WinUI 运行环境。[该版本许可](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.InteractiveExperiences/2.1.6/License)、[Windows App SDK 仓库](https://github.com/microsoft/WindowsAppSDK)。 |
| Microsoft.WindowsAppSDK.Foundation 2.3.9、Base 2.0.4 | 由上述组件引入的基础运行组件，原始许可与声明见发布包的 `licenses` 目录。 |
| Microsoft.Web.WebView2 1.0.3719.77 | 由 WinUI 包引入的 WebView2 SDK，用于嵌入网页。[该版本许可](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3719.77/License)。 |
| Microsoft.Windows.SDK.BuildTools 10.0.26100.9169 | 构建时的 Windows SDK 工具，不要求用户安装完整开发工具链。[包信息及许可入口](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.26100.9169)。 |
| Microsoft Edge WebView2 Runtime | 网页运行组件，由微软安装程序单独安装和维护。安装器仅在检测到缺失时补装，不把完整浏览器运行时放进 WebDisplay 的程序目录；卸载 WebDisplay 时保留共享运行时。[下载与许可信息](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)、[官方分发说明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)。 |
| Inno Setup 6.7.3 | 用于生成安装器和卸载器。由 Jordan Russell、Martijn Laan 等作者提供，WebDisplay 未声称拥有这些组件的原创权利。[项目网站](https://jrsoftware.org/isinfo.php)、[Inno Setup 许可](https://jrsoftware.org/files/is/license.txt)。 |

2.1.0 采用精简的 WinUI 组件依赖，没有引用 `Microsoft.WindowsAppSDK` 总包，也没有分发该总包额外引入的 ONNX Runtime、DirectML 等 AI 运行文件。组件版本与依赖关系以当前项目的 `project.assets.json` 和发布包中的 `licenses/resolved-packages.json` 为准。许可收集按该还原结果中的确切包版本查找，不选用本地缓存中的其他版本；同时处理 .NET 自包含运行时等 `downloadDependencies`。清单也可能包括构建时使用的参考包或工具包，这不表示这些包的全部文件都会部署到目标电脑。

多文件 ZIP 和 Inno Setup 安装包使用同一份发布目录及许可文件。版权、许可和第三方声明仍归各自权利人所有；安装包格式不会改变原有许可。微软项目的开源仓库许可证与其 NuGet 二进制分发许可可能不同，应以所分发组件附带的许可文件为准。

安装包及源码包还包含微软官方 WebView2 引导安装器；它在需要时下载并安装共享的 Evergreen Runtime。Inno Setup 编译器属于构建工具，未作为 WebDisplay 的日常运行依赖安装到目标电脑。

发布包中的 `licenses` 目录提供构建时收集的原始许可及声明；本文件用于定位组件和许可来源，不替代这些原文。Windows 自带 API（任务计划程序、注册表、LSA 和电源管理）由目标系统提供；WebDisplay 使用这些 API 实现系统设置，没有分发 Sysinternals Autologon 工具。
