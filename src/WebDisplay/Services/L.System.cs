using System.Collections.Generic;

namespace WebDisplay.Services;

public static partial class L
{
    static partial void AddSystemTranslations(Dictionary<string, Translation> entries)
    {
        entries["显示窗口"] = new Translation("顯示視窗", "Show window");
        entries["设置"] = new Translation("設定", "Settings");
        entries["刷新网页"] = new Translation("重新整理網頁", "Refresh webpage");
        entries["切换全屏"] = new Translation("切換全螢幕", "Toggle full screen");
        entries["退出程序"] = new Translation("結束程式", "Exit");
        entries["操作未完成"] = new Translation("操作未完成", "Action could not be completed");
        entries["网页展示器 — 双击显示，右键打开菜单"] = new Translation("網頁展示器 — 按兩下顯示，按右鍵開啟選單", "Web Display — Double-click to show; right-click for menu");
        entries["无法创建通知区域图标的窗口消息处理程序。"] = new Translation("無法建立通知區域圖示的視窗訊息處理程序。", "Could not initialize the notification-area icon.");
        entries["无法为当前应用窗口初始化快捷键。"] = new Translation("無法為目前的應用程式視窗初始化快速鍵。", "Could not initialize shortcuts for the application window.");
        entries["无法初始化窗口快捷键。"] = new Translation("無法初始化視窗快速鍵。", "Could not initialize window shortcuts.");
        entries["无法更新防休眠状态"] = new Translation("無法更新防止休眠狀態", "Could not update sleep prevention");
        entries["配置为空"] = new Translation("設定內容為空", "The settings file is empty");
        entries["设置文件无法读取，已使用默认值。原文件保留，保存设置后会更新。"] = new Translation("無法讀取設定檔，已使用預設值。原始檔案仍保留，儲存設定後將會更新。", "The settings file could not be read. Defaults are being used. The original file is preserved until you save settings.");
        entries["无法打开当前用户的开机启动设置。"] = new Translation("無法開啟目前使用者的開機啟動設定。", "Could not open the startup settings for the current user.");
        entries["不支持的管理员操作。"] = new Translation("不支援的系統管理員操作。", "This administrator operation is not supported.");
        entries["重启时间必须为 24 小时制 HH:mm，例如 03:00。"] = new Translation("重新啟動時間必須使用 24 小時制 HH:mm，例如 03:00。", "Restart time must use the 24-hour HH:mm format, for example 03:00.");
        entries["请选择每天，或至少一个有效的星期。"] = new Translation("請選擇每天，或至少一個有效的星期。", "Select Daily or at least one valid day of the week.");
        entries["网页展示程序配置的定时重启。按本机时间运行；错过时间不会在开机后补执行；不强制关闭有未保存内容的程序。"] = new Translation("網頁展示程式設定的定時重新啟動。依本機時間執行；錯過的排程不會在開機後補執行；不會強制關閉有未儲存內容的程式。", "Scheduled restart configured by Web Display. Uses the computer's local time; missed runs are not retried at startup. Applications with unsaved work are not forcibly closed.");
        entries["已关闭定时重启。"] = new Translation("已關閉定時重新啟動。", "Scheduled restart is turned off.");
        entries["定时重启已保存，将按电脑本地时间 {0} 执行。"] = new Translation("已儲存定時重新啟動，將於電腦本機時間 {0} 執行。", "Scheduled restart has been saved and will run at {0} in the computer's local time.");
        entries["此电脑的 Windows 任务计划程序不可用。"] = new Translation("此電腦的 Windows 工作排程器無法使用。", "Windows Task Scheduler is unavailable on this computer.");
        entries["无法连接 Windows 任务计划程序。"] = new Translation("無法連線至 Windows 工作排程器。", "Could not connect to Windows Task Scheduler.");
        entries["请输入有效的 Windows 账号，最多 256 个字符。"] = new Translation("請輸入有效的 Windows 帳號，最多 256 個字元。", "Enter a valid Windows account name, up to 256 characters.");
        entries["Windows 域或计算机名无效。"] = new Translation("Windows 網域或電腦名稱無效。", "The Windows domain or computer name is invalid.");
        entries["Windows 密码长度或格式无效。"] = new Translation("Windows 密碼的長度或格式無效。", "The Windows password length or format is invalid.");
        entries["无法打开 Windows 自动登录配置。"] = new Translation("無法開啟 Windows 自動登入設定。", "Could not open the Windows automatic sign-in settings.");
        entries["已关闭 Windows 自动登录，并清除自动登录密码。"] = new Translation("已關閉 Windows 自動登入，並清除自動登入密碼。", "Windows automatic sign-in is turned off and its saved password has been cleared.");
        entries["Windows 自动登录配置已保存，将在下次开机时生效。账号密码是否正确及组织策略是否允许，需要在登录时验证。"] = new Translation("已儲存 Windows 自動登入設定，將於下次開機時生效。帳號密碼是否正確，以及組織原則是否允許，需在登入時驗證。", "Windows automatic sign-in settings have been saved and will take effect at the next startup. Account credentials and organization policies will be checked at sign-in.");
        entries["无法确定程序文件路径，请从已发布的 EXE 启动程序。"] = new Translation("無法判定程式檔案路徑，請從已發佈的 EXE 啟動程式。", "Could not determine the application path. Start the application from its published EXE.");
        entries["无法启动管理员配置程序。"] = new Translation("無法啟動系統管理員設定程式。", "Could not start the administrator configuration helper.");
        entries["管理员配置程序在建立安全连接前已退出。"] = new Translation("系統管理員設定程式在建立安全連線前已結束。", "The administrator helper exited before establishing a secure connection.");
        entries["管理员配置连接身份验证失败。"] = new Translation("系統管理員設定連線的身分驗證失敗。", "Authentication of the administrator connection failed.");
        entries["已取消管理员授权，系统设置未更改。"] = new Translation("已取消系統管理員授權，系統設定未變更。", "Administrator approval was canceled. System settings were not changed.");
        entries["管理员配置等待超时。请重新打开设置检查系统状态后重试。"] = new Translation("等待系統管理員設定逾時。請重新開啟設定，檢查系統狀態後再試。", "Administrator configuration timed out. Reopen settings, check the system status, then try again.");
        entries["系统配置失败：{0}"] = new Translation("系統設定失敗：{0}", "System configuration failed: {0}");
        entries["无法获取当前 Windows 用户标识。"] = new Translation("無法取得目前 Windows 使用者的識別碼。", "Could not obtain the current Windows user identifier.");
        entries["配置数据过长。"] = new Translation("設定資料過長。", "The configuration data is too large.");
        entries["无效的管理员通信数据。"] = new Translation("無效的系統管理員通訊資料。", "The administrator communication data is invalid.");
        entries["管理员通信数据为空。"] = new Translation("系統管理員通訊資料為空。", "The administrator communication data is empty.");
    }
}
