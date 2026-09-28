using System.Collections.Generic;

namespace WebDisplay.Services;

public static partial class L
{
    static partial void AddUpdateTranslations(Dictionary<string, Translation> entries)
    {
        entries["检查更新"] = new("檢查更新", "Check for updates");
        entries["正在检查…"] = new("正在檢查…", "Checking…");
        entries["已是最新版本"] = new("已是最新版本", "Up to date");
        entries["暂时无法检查，点击重试"] = new("暫時無法檢查，按一下重試", "Unable to check. Click to retry");
        entries["发现新版本 {0}"] = new("發現新版本 {0}", "Version {0} available");
        entries["点击打开 GitHub 发布页面"] = new("按一下開啟 GitHub 發行頁面", "Click to open the GitHub release page");
        entries["当前版本 {0}"] = new("目前版本 {0}", "Current version {0}");
        entries["每 6 小时自动检查更新，点击立即检查"] = new("每 6 小時自動檢查更新，按一下立即檢查", "Checks for updates every 6 hours. Click to check now");
        entries["无法打开浏览器，请稍后重试"] = new("無法開啟瀏覽器，請稍後重試", "Could not open the browser. Please try again later");
    }
}
