using System.Collections.Generic;

namespace WebDisplay.Services;

public static partial class L
{
    static partial void AddApplicationTranslations(Dictionary<string, Translation> entries)
    {
        entries["网页展示器"] = new("網頁展示器", "WebDisplay");
        entries["刷新"] = new("重新整理", "Refresh");
        entries["全屏"] = new("全螢幕", "Full screen");
        entries["设置"] = new("設定", "Settings");
        entries["刷新 · Ctrl+R"] = new("重新整理 · Ctrl+R", "Refresh · Ctrl+R");
        entries["全屏 · F11"] = new("全螢幕 · F11", "Full screen · F11");
        entries["设置 · Ctrl+,"] = new("設定 · Ctrl+,", "Settings · Ctrl+,");
        entries["F11 全屏  ·  Esc 返回  ·  Ctrl+, 设置"] = new("F11 全螢幕  ·  Esc 返回  ·  Ctrl+, 設定", "F11 Full screen  ·  Esc Return  ·  Ctrl+, Settings");
        entries["等待恢复"] = new("等待復原", "Waiting to recover");
        entries["立即重试"] = new("立即重試", "Retry now");
        entries["设置提示"] = new("設定提示", "Settings notice");
        entries["启动未完成"] = new("啟動未完成", "Startup incomplete");
        entries["正在初始化网页引擎…"] = new("正在初始化網頁引擎…", "Starting the browser…");
        entries["正在加载网页…"] = new("正在載入網頁…", "Loading the page…");
        entries["网页加载失败（{0}）"] = new("網頁載入失敗（{0}）", "The page could not load ({0}).");
        entries["网页进程异常，将重新创建浏览器"] = new("網頁處理程序異常，將重新建立瀏覽器", "The browser stopped responding. It will be restarted.");
        entries["未检测到 WebView2 Runtime，请安装后点击重试"] = new("未偵測到 WebView2 Runtime，請安裝後按一下重試", "WebView2 Runtime was not found. Install it, then retry.");
        entries["网页引擎初始化失败，将自动重试"] = new("網頁引擎初始化失敗，將自動重試", "The browser could not start. Retrying automatically.");
        entries["需要网页运行组件"] = new("需要網頁執行元件", "Browser runtime required");
        entries["运行网页展示器需要 Microsoft Edge WebView2 Runtime。安装后返回程序点击“立即重试”。"] = new("執行網頁展示器需要 Microsoft Edge WebView2 Runtime。安裝後返回程式，按一下「立即重試」。", "WebDisplay requires Microsoft Edge WebView2 Runtime. After installing it, return to the app and choose Retry now.");
        entries["打开微软下载页"] = new("開啟 Microsoft 下載頁面", "Open Microsoft download page");
        entries["稍后"] = new("稍後", "Later");
        entries["滚动条设置暂未生效，请更新 WebView2 Runtime 后重试"] = new("捲軸設定暫未生效，請更新 WebView2 Runtime 後重試", "Scrollbar settings could not be applied. Update WebView2 Runtime and retry.");
        entries["{0} · {1} 秒后重试"] = new("{0} · {1} 秒後重試", "{0} · Retrying in {1} s");
        entries["网页加载超时"] = new("網頁載入逾時", "The page took too long to load.");
        entries["网络连接已断开"] = new("網路連線已中斷", "The network connection was lost.");
        entries["网页缩放应用失败，将重新创建浏览器"] = new("網頁縮放套用失敗，將重新建立瀏覽器", "Page zoom could not be applied. Restarting the browser.");
        entries["网页声音设置应用失败，将重新创建浏览器"] = new("網頁聲音設定套用失敗，將重新建立瀏覽器", "Page audio settings could not be applied. Restarting the browser.");
        entries["滚动条设置暂未生效"] = new("捲軸設定暫未生效", "Scrollbar settings not applied");
        entries["设置已保存，但网页引擎暂时无法应用滚动条选项。请更新 WebView2 Runtime 后重试。网页仍可正常显示。"] = new("設定已儲存，但網頁引擎暫時無法套用捲軸選項。請更新 WebView2 Runtime 後重試。網頁仍可正常顯示。", "Your settings were saved, but the browser could not apply the scrollbar option. Update WebView2 Runtime and retry. The page can still be displayed.");
        entries["保存失败"] = new("儲存失敗", "Could not save");
        entries["设置未能保存：{0}"] = new("設定未能儲存：{0}", "Your settings could not be saved: {0}");
        entries["确定"] = new("確定", "OK");
        entries["每 {0} 分钟刷新"] = new("每 {0} 分鐘重新整理", "Refresh every {0} min");
        entries["自动刷新已关闭"] = new("自動重新整理已關閉", "Auto refresh off");
        entries["防休眠已开启"] = new("防止睡眠已開啟", "Keep awake on");
        entries["防休眠已关闭"] = new("防止睡眠已關閉", "Keep awake off");
        entries["已忽略证书错误"] = new("已忽略憑證錯誤", "Certificate errors ignored");
        entries["网页已静音"] = new("網頁已靜音", "Page muted");
        entries["无法启用防休眠，请检查系统策略"] = new("無法啟用防止睡眠，請檢查系統原則", "Could not keep the display awake. Check system policies.");
        entries["正在重新应用 HTTPS 证书设置…"] = new("正在重新套用 HTTPS 憑證設定…", "Applying HTTPS certificate settings…");
        entries["正在关闭旧网页会话，证书设置应用后将重新加载"] = new("正在關閉舊網頁工作階段，憑證設定套用後將重新載入", "Closing the previous browser session. The page will reload after applying certificate settings.");
        entries["界面语言必须为简体中文、繁体中文或英文。"] = new("介面語言必須為簡體中文、繁體中文或英文。", "Choose Simplified Chinese, Traditional Chinese, or English.");
        entries["界面主题必须为跟随系统、浅色或深色。"] = new("介面主題必須為跟隨系統、淺色或深色。", "Choose the system, light, or dark theme.");
        entries["请输入完整的 http:// 或 https:// 网页地址，且不要在网址中包含账号密码。"] = new("請輸入完整的 http:// 或 https:// 網頁位址，且不要在網址中包含帳號密碼。", "Enter a complete http:// or https:// address without a username or password.");
        entries["网页缩放比例应为 25 到 500 的整数百分比。"] = new("網頁縮放比例應為 25 到 500 的整數百分比。", "Page zoom must be a whole percentage from 25 to 500.");
        entries["刷新间隔应为 1 到 10080 分钟。"] = new("重新整理間隔應為 1 到 10080 分鐘。", "The refresh interval must be from 1 to 10080 minutes.");
        entries["网页展示器已经运行，请从任务栏或系统托盘打开设置。"] = new("網頁展示器已在執行，請從工作列或系統匣開啟設定。", "WebDisplay is already running. Open it from the taskbar or system tray.");
        entries["程序启动失败：{0}"] = new("程式啟動失敗：{0}", "WebDisplay could not start: {0}");
    }
}