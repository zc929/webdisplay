using System;

namespace WebDisplay.Models;

public sealed class AppSettings
{
    public string Url { get; set; } = "https://example.com";
    public bool AutoRefreshEnabled { get; set; }
    public int RefreshIntervalMinutes { get; set; } = 5;
    public bool AlwaysOnTop { get; set; }
    public bool FullScreen { get; set; } = true;
    public bool PreventSleep { get; set; } = true;
    public bool StartAtLogon { get; set; }
    public bool RestartEnabled { get; set; }
    public string RestartTime { get; set; } = "03:00";
    public string RestartDays { get; set; } = "Daily";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public static bool IsValidUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    public void Validate()
    {
        if (!IsValidUrl(Url)) throw new ArgumentException("请输入完整的 http:// 或 https:// 网页地址，且不要在网址中包含账号密码。");
        if (RefreshIntervalMinutes < 1 || RefreshIntervalMinutes > 10080)
            throw new ArgumentException("刷新间隔应为 1 到 10080 分钟。");
    }
}
