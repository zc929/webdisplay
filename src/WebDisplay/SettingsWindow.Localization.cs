using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WebDisplay.Services;

namespace WebDisplay;

public sealed partial class SettingsWindow
{
    // This explicit list translates only UI labels declared in XAML. It never
    // traverses control templates or reads/translates editable user values.
    private void ApplyLocalization()
    {
        RootGrid.Language = L.Language;
        Title = L.Text("网页展示 · 设置");
        LocalizedElement001.Text = L.Text("网页展示设置");
        LocalizedElement002.Text = L.Text("配置展示网页、窗口外观和 Windows 自动化。");
        LocalizedElement003.Header = L.Text("网页与展示");
        LocalizedElement004.Text = L.Text("外观主题");
        LocalizedElement005.Text = L.Text("即时预览；保存后记住你的选择。");
        AutomationProperties.SetName(ThemePreferenceBox, L.Text("外观主题"));
        LocalizedElement006.Content = L.Text("跟随系统");
        LocalizedElement007.Content = L.Text("浅色");
        LocalizedElement008.Content = L.Text("深色");
        LocalizedElement009.Text = L.Text("界面语言");
        LocalizedElement010.Text = L.Text("保存后生效。");
        AutomationProperties.SetName(LanguageBox, L.Text("界面语言"));
        LocalizedElement011.Text = L.Text("展示网页");
        UrlBox.Header = WrappedLabel(L.Text("网页地址"));
        AutomationProperties.SetName(UrlBox, L.Text("网页地址"));
        LocalizedElement012.Text = L.Text("请输入完整的 https:// 或 http:// 地址。需要登录的网站会保留本机浏览器会话。");
        ZoomPercentBox.Header = WrappedLabel(L.Text("网页缩放（%）"));
        AutomationProperties.SetName(ZoomPercentBox, L.Text("网页缩放百分比"));
        ResetZoomButton.Content = L.Text("恢复100%");
        LocalizedElement013.Text = L.Text("可输入 25–500 的整数。100% 为原始大小，保存设置后生效。");
        ShowScrollbarsToggle.Header = WrappedLabel(L.Text("显示网页滚动条"));
        ShowScrollbarsToggle.OnContent = L.Text("显示");
        ShowScrollbarsToggle.OffContent = L.Text("隐藏");
        LocalizedElement014.Text = L.Text("关闭后仍可用滚轮、触控或键盘滚动。更改此选项并保存后会刷新网页。");
        MutePageToggle.Header = WrappedLabel(L.Text("网页静音"));
        MutePageToggle.OnContent = L.Text("已开启");
        MutePageToggle.OffContent = L.Text("已关闭");
        LocalizedElement015.Text = L.Text("开启后仅静音本程序中的网页，不影响其他应用的声音。保存设置后生效。");
        RefreshToggle.Header = WrappedLabel(L.Text("定时刷新网页"));
        RefreshToggle.OnContent = L.Text("已开启");
        RefreshToggle.OffContent = L.Text("已关闭");
        RefreshMinutesBox.Header = WrappedLabel(L.Text("刷新间隔（分钟）"));
        AutomationProperties.SetName(RefreshMinutesBox, L.Text("刷新间隔（分钟）"));
        LocalizedElement016.Text = L.Text("间隔可设为 1–10080 分钟。");
        LocalizedElement017.Text = L.Text("HTTPS 证书");
        IgnoreCertificateErrorsToggle.Header = WrappedLabel(L.Text("忽略 SSL/TLS 证书错误"));
        IgnoreCertificateErrorsToggle.OnContent = L.Text("已开启");
        IgnoreCertificateErrorsToggle.OffContent = L.Text("已关闭");
        LocalizedElement018.Text = L.Text("仅在确认可信的内部展示网站需要时开启。更改并保存后会重新加载网页。");
        LocalizedElement019.Title = L.Text("开启前请确认网站可信");
        LocalizedElement019.Message = L.Text("开启后无法可靠验证网站身份，可能遭到网站冒充或中间人攻击。此设置对网页及其加载的资源均适用。");
        LocalizedElement020.Text = L.Text("窗口与电源");
        TopmostToggle.Header = WrappedLabel(L.Text("窗口始终置顶"));
        TopmostToggle.OnContent = L.Text("已开启");
        TopmostToggle.OffContent = L.Text("已关闭");
        FullScreenToggle.Header = WrappedLabel(L.Text("全屏展示"));
        FullScreenToggle.OnContent = L.Text("已开启");
        FullScreenToggle.OffContent = L.Text("已关闭");
        PreventSleepToggle.Header = WrappedLabel(L.Text("展示期间防止系统休眠与显示器自动关闭"));
        PreventSleepToggle.OnContent = L.Text("已开启");
        PreventSleepToggle.OffContent = L.Text("已关闭");
        LocalizedElement021.Text = L.Text("关闭后遵循 Windows 电源设置；手动锁屏、手动休眠和管理员策略仍然有效。");
        StartAtLogonToggle.Header = WrappedLabel(L.Text("登录 Windows 后自动启动展示程序"));
        StartAtLogonToggle.OnContent = L.Text("已开启");
        StartAtLogonToggle.OffContent = L.Text("已关闭");
        LocalizedElement022.Text = L.Text("仅适用于当前 Windows 用户。如果自动登录使用其他账号，请登录该账号后开启此选项。开机直接展示还需配置 Windows 自动登录。");
        LocalizedElement023.Title = L.Text("无人值守恢复已启用");
        LocalizedElement023.Message = L.Text("断网或网页加载失败后会自动重试；网页进程异常退出时会自动重建浏览器。");
        LocalizedElement024.Header = L.Text("系统与账号");
        LocalizedElement025.Title = L.Text("系统设置独立应用");
        LocalizedElement025.Message = L.Text("本页设置通过各自的按钮立即生效，并会请求 Windows 管理员授权。");
        LocalizedElement026.Text = L.Text("定时重启电脑");
        RestartToggle.Header = WrappedLabel(L.Text("启用定时重启"));
        RestartToggle.OnContent = L.Text("已开启");
        RestartToggle.OffContent = L.Text("已关闭");
        RestartTimeBox.Header = WrappedLabel(L.Text("重启时间"));
        AutomationProperties.SetName(RestartTimeBox, L.Text("重启时间（24小时制）"));
        LocalizedElement027.Text = L.Text("24 小时制 · 本机时间");
        DailyRadio.Content = L.Text("每天");
        WeeklyRadio.Content = L.Text("每周指定日期");
        MondayCheck.Content = L.Text("周一");
        TuesdayCheck.Content = L.Text("周二");
        WednesdayCheck.Content = L.Text("周三");
        ThursdayCheck.Content = L.Text("周四");
        FridayCheck.Content = L.Text("周五");
        SaturdayCheck.Content = L.Text("周六");
        SundayCheck.Content = L.Text("周日");
        LocalizedElement028.Text = L.Text("计划由 Windows 执行，即使本程序退出也有效。其他程序未保存的内容可能延迟或阻止重启。");
        LocalizedElement029.Text = L.Text("应用重启计划（管理员）");
        LocalizedElement030.Text = L.Text("Windows 自动登录");
        UserNameBox.Header = WrappedLabel(L.Text("Windows 账号"));
        AutomationProperties.SetName(UserNameBox, L.Text("Windows 账号"));
        DomainBox.Header = WrappedLabel(L.Text("域 / 本机名称"));
        AutomationProperties.SetName(DomainBox, L.Text("域或本机名称"));
        AccountPasswordBox.Header = WrappedLabel(L.Text("账号密码"));
        AutomationProperties.SetName(AccountPasswordBox, L.Text("Windows 账号密码"));
        LocalizedElement031.Text = L.Text("本地账号使用本机名称作为域。请填写账号密码，而非 Windows Hello PIN；域账号受组织策略约束。密码只用于本次系统配置，不写入程序设置文件。");
        LocalizedElement032.Text = L.Text("启用自动登录（管理员）");
        LocalizedElement033.Text = L.Text("关闭自动登录（管理员）");
        ValidationText.Text = L.Text("保存将应用展示设置和登录后启动选项。");
        CancelButton.Content = L.Text("取消");
        SaveButton.Content = L.Text("保存设置");
    }

    private static TextBlock WrappedLabel(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap
    };
}