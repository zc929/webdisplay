using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;

namespace WebDisplay.Services;

public sealed record SystemStatus(bool StartAtLogon, bool RestartEnabled, string RestartTime,
    string RestartDays, bool AutoLogonEnabled, string AutoLogonUser, string AutoLogonDomain);

public static class WindowsIntegrationService
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValue = "WebDisplay";
    private const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    internal const string RestartTaskName = "WebDisplay - Scheduled Restart";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly (string Code, string Name)[] Weekdays =
    {
        ("MON", "Monday"), ("TUE", "Tuesday"), ("WED", "Wednesday"),
        ("THU", "Thursday"), ("FRI", "Friday"), ("SAT", "Saturday"), ("SUN", "Sunday")
    };

    public static SystemStatus ReadStatus()
    {
        bool startup = false;
        bool restart = false;
        string time = "03:00";
        string days = "Daily";
        bool autoLogon = false;
        string username = string.Empty;
        string domain = Environment.MachineName;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupKey);
            string? value = key?.GetValue(StartupValue) as string;
            startup = string.Equals(value, QuoteExecutable(), StringComparison.OrdinalIgnoreCase);
        }
        catch { /* Missing/locked-down machine settings do not prevent displaying a page. */ }
        try
        {
            using RegistryKey machine = OpenMachineRegistry();
            using RegistryKey? key = machine.OpenSubKey(WinlogonKey);
            autoLogon = string.Equals(key?.GetValue("AutoAdminLogon")?.ToString(), "1", StringComparison.Ordinal);
            username = key?.GetValue("DefaultUserName") as string ?? string.Empty;
            domain = key?.GetValue("DefaultDomainName") as string ?? Environment.MachineName;
        }
        catch { }
        object? serviceObject = null;
        object? folderObject = null;
        object? taskObject = null;
        try
        {
            dynamic service = CreateScheduler(out serviceObject);
            service.Connect();
            dynamic folder = service.GetFolder(@"\");
            folderObject = folder;
            dynamic task = folder.GetTask(RestartTaskName);
            taskObject = task;
            XDocument xml = XDocument.Parse((string)task.Xml);
            XElement? trigger = xml.Root?.Element(TaskNamespace + "Triggers")?.Element(TaskNamespace + "CalendarTrigger");
            XElement? action = xml.Root?.Element(TaskNamespace + "Actions")?.Element(TaskNamespace + "Exec");
            bool expectedAction = string.Equals(action?.Element(TaskNamespace + "Command")?.Value,
                ShutdownPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(action?.Element(TaskNamespace + "Arguments")?.Value, "/r /t 0", StringComparison.Ordinal);
            restart = (bool)task.Enabled && expectedAction && trigger is not null
                && !string.Equals(trigger.Element(TaskNamespace + "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase);
            if (DateTime.TryParse(trigger?.Element(TaskNamespace + "StartBoundary")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime boundary))
                time = boundary.ToString("HH:mm", CultureInfo.InvariantCulture);
            XElement? week = trigger?.Element(TaskNamespace + "ScheduleByWeek")?.Element(TaskNamespace + "DaysOfWeek");
            if (week is not null)
            {
                string[] selected = Weekdays.Where(day => week.Element(TaskNamespace + day.Name) is not null)
                    .Select(day => day.Code).ToArray();
                if (selected.Length > 0)
                    days = string.Join(",", selected);
            }
        }
        catch { }
        finally
        {
            ReleaseCom(taskObject);
            ReleaseCom(folderObject);
            ReleaseCom(serviceObject);
        }
        return new SystemStatus(startup, restart, time, days, autoLogon, username, domain);
    }

    /// <summary>Starts the published executable after the current user signs in.</summary>
    public static void SetStartAtLogon(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey, true)
            ?? throw new InvalidOperationException(L.Text("无法打开当前用户的开机启动设置。"));
        if (enabled)
            key.SetValue(StartupValue, QuoteExecutable(), RegistryValueKind.String);
        else
            key.DeleteValue(StartupValue, false);
    }

    public static Task<string> ConfigureRestartAsync(bool enabled, string time, string days)
    {
        if (enabled)
            ValidateSchedule(time, days);
        return AdminPipe.SendAsync(new AdminRequest
        {
            Operation = "restart", Enabled = enabled, Time = time, Days = days
        });
    }

    public static Task<string> ConfigureAutoLogonAsync(bool enabled, string username, string domain, string password)
    {
        if (enabled)
            ValidateAutoLogon(username, domain, password);
        return AdminPipe.SendAsync(new AdminRequest
        {
            Operation = "autologon", Enabled = enabled,
            Username = username?.Trim() ?? string.Empty,
            Domain = domain?.Trim() ?? string.Empty,
            Password = password ?? string.Empty
        });
    }

    public static Task<int> RunAdminCommandAsync(string[] args)
        => AdminPipe.RunAsync(args, ApplyAdminRequest);

    private static string ApplyAdminRequest(AdminRequest request)
        => request.Operation switch
        {
            "restart" => ApplyRestart(request.Enabled, request.Time, request.Days),
            "autologon" => ApplyAutoLogon(request.Enabled, request.Username, request.Domain, request.Password),
            _ => throw new InvalidOperationException(L.Text("不支持的管理员操作。"))
        };

    private static string QuoteExecutable() => "\"" + AdminPipe.ExecutablePath + "\"";

    private static string ShutdownPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");

    private static RegistryKey OpenMachineRegistry() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
        Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);

    private static void ValidateSchedule(string time, string days)
    {
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException(L.Text("重启时间必须为 24 小时制 HH:mm，例如 03:00。"));
        ParseDays(days);
    }

    private static IReadOnlyList<string> ParseDays(string days)
    {
        if (string.Equals(days, "Daily", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();
        string[] selected = (days ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(day => day.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0 || selected.Any(day => !Weekdays.Any(known => known.Code == day)))
            throw new ArgumentException(L.Text("请选择每天，或至少一个有效的星期。"));
        return Weekdays.Where(day => selected.Contains(day.Code, StringComparer.Ordinal))
            .Select(day => day.Name).ToArray();
    }

    // Pure XML construction is separate so scheduling semantics can be verified
    // without registering a task or restarting the development machine.
    internal static string BuildRestartTaskXml(string time, string days, DateTime now)
    {
        ValidateSchedule(time, days);
        IReadOnlyList<string> selectedDays = ParseDays(days);
        TimeOnly clock = TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);
        DateTime start = now.Date.Add(clock.ToTimeSpan());
        if (start <= now)
            start = start.AddDays(1);
        XNamespace ns = TaskNamespace;
        XElement schedule = selectedDays.Count == 0
            ? new XElement(ns + "ScheduleByDay", new XElement(ns + "DaysInterval", 1))
            : new XElement(ns + "ScheduleByWeek",
                new XElement(ns + "WeeksInterval", 1),
                new XElement(ns + "DaysOfWeek", selectedDays.Select(name => new XElement(ns + name))));
        var document = new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.3"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Author", "WebDisplay"),
                new XElement(ns + "Description", L.Text("网页展示程序配置的定时重启。按本机时间运行；错过时间不会在开机后补执行；不强制关闭有未保存内容的程序。"))),
            new XElement(ns + "Triggers", new XElement(ns + "CalendarTrigger",
                new XElement(ns + "Enabled", true),
                new XElement(ns + "StartBoundary", start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)), schedule)),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "System"),
                new XElement(ns + "UserId", "S-1-5-18"),
                new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", false),
                new XElement(ns + "StopIfGoingOnBatteries", false),
                new XElement(ns + "AllowHardTerminate", false),
                new XElement(ns + "StartWhenAvailable", false),
                new XElement(ns + "RunOnlyIfNetworkAvailable", false),
                new XElement(ns + "AllowStartOnDemand", true),
                new XElement(ns + "Enabled", true),
                new XElement(ns + "Hidden", false),
                new XElement(ns + "RunOnlyIfIdle", false),
                new XElement(ns + "WakeToRun", true),
                new XElement(ns + "ExecutionTimeLimit", "PT1M"),
                new XElement(ns + "Priority", 7)),
            new XElement(ns + "Actions", new XAttribute("Context", "System"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", ShutdownPath),
                    new XElement(ns + "Arguments", "/r /t 0")))));
        return document.ToString();
    }

    private static string ApplyRestart(bool enabled, string time, string days)
    {
        string? xml = enabled ? BuildRestartTaskXml(time, days, DateTime.Now) : null;
        object? serviceObject = null;
        object? folderObject = null;
        object? registeredObject = null;
        try
        {
            dynamic service = CreateScheduler(out serviceObject);
            service.Connect();
            dynamic folder = service.GetFolder(@"\");
            folderObject = folder;
            if (!enabled)
            {
                try { folder.DeleteTask(RestartTaskName, 0); }
                catch (Exception ex) when ((uint)ex.HResult == 0x80070002 || (uint)ex.HResult == 0x8004130F) { }
                return L.Text("已关闭定时重启。");
            }
            // TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_SERVICE_ACCOUNT = 5.
            // Read access for ordinary users allows settings to show the actual
            // system state even when another administrator approved the change.
            registeredObject = folder.RegisterTask(RestartTaskName, xml, 6, "SYSTEM", null, 5,
                "D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)");
            return L.Format("定时重启已保存，将按电脑本地时间 {0} 执行。", time);
        }
        finally
        {
            ReleaseCom(registeredObject);
            ReleaseCom(folderObject);
            ReleaseCom(serviceObject);
        }
    }

    private static dynamic CreateScheduler(out object instance)
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException(L.Text("此电脑的 Windows 任务计划程序不可用。"));
        instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(L.Text("无法连接 Windows 任务计划程序。"));
        return instance;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); }
            catch { }
        }
    }

    private static void ValidateAutoLogon(string username, string domain, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 256 || username.IndexOf('\0') >= 0)
            throw new ArgumentException(L.Text("请输入有效的 Windows 账号，最多 256 个字符。"));
        if (domain is null || domain.Length > 255 || domain.IndexOf('\0') >= 0)
            throw new ArgumentException(L.Text("Windows 域或计算机名无效。"));
        if (password is null || password.Length > 16382 || password.IndexOf('\0') >= 0)
            throw new ArgumentException(L.Text("Windows 密码长度或格式无效。"));
    }

    private static string ApplyAutoLogon(bool enabled, string username, string domain, string password)
    {
        if (enabled)
            ValidateAutoLogon(username, domain, password);
        using RegistryKey machine = OpenMachineRegistry();
        using RegistryKey key = machine.OpenSubKey(WinlogonKey, true)
            ?? throw new InvalidOperationException(L.Text("无法打开 Windows 自动登录配置。"));
        // Fail closed if a subsequent LSA or registry operation fails.
        key.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
        key.DeleteValue("DefaultPassword", false);
        if (!enabled)
        {
            AutoLogonSecret.Store(null);
            key.DeleteValue("AutoLogonCount", false);
            key.DeleteValue("ForceAutoLogon", false);
            return L.Text("已关闭 Windows 自动登录，并清除自动登录密码。");
        }
        AutoLogonSecret.Store(password);
        key.SetValue("DefaultUserName", username.Trim(), RegistryValueKind.String);
        key.SetValue("DefaultDomainName", string.IsNullOrWhiteSpace(domain) ? Environment.MachineName : domain.Trim(), RegistryValueKind.String);
        key.DeleteValue("AutoLogonCount", false);
        key.DeleteValue("ForceAutoLogon", false);
        key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
        return L.Text("Windows 自动登录配置已保存，将在下次开机时生效。账号密码是否正确及组织策略是否允许，需要在登录时验证。");
    }
}

internal static class AutoLogonSecret
{
    internal static void Store(string? password)
    {
        var attributes = new AutoLogonNative.LsaObjectAttributes { Length = Marshal.SizeOf<AutoLogonNative.LsaObjectAttributes>() };
        uint status = AutoLogonNative.LsaOpenPolicy(IntPtr.Zero, ref attributes, 0x00000020, out IntPtr policy);
        ThrowIfError(status);
        try
        {
            using var name = new UnicodeValue("DefaultPassword");
            if (password is null)
            {
                status = AutoLogonNative.LsaStorePrivateDataDelete(policy, ref name.Value, IntPtr.Zero);
                uint error = AutoLogonNative.LsaNtStatusToWinError(status);
                if (error != 2 && error != 3)
                    ThrowIfError(status);
            }
            else
            {
                using var secret = new UnicodeValue(password);
                status = AutoLogonNative.LsaStorePrivateData(policy, ref name.Value, ref secret.Value);
                ThrowIfError(status);
            }
        }
        finally
        {
            AutoLogonNative.LsaClose(policy);
        }
    }

    private static void ThrowIfError(uint status)
    {
        if (status != 0)
            throw new Win32Exception(unchecked((int)AutoLogonNative.LsaNtStatusToWinError(status)));
    }

    private sealed class UnicodeValue : IDisposable
    {
        internal AutoLogonNative.LsaUnicodeString Value;

        internal UnicodeValue(string text)
        {
            Value = new AutoLogonNative.LsaUnicodeString
            {
                Length = checked((ushort)(text.Length * 2)),
                MaximumLength = checked((ushort)((text.Length + 1) * 2)),
                Buffer = Marshal.StringToHGlobalUni(text)
            };
        }

        public void Dispose()
        {
            if (Value.Buffer != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(Value.Buffer);
                Value.Buffer = IntPtr.Zero;
            }
        }
    }
}

internal static class AutoLogonNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct LsaUnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LsaObjectAttributes
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [DllImport("advapi32.dll")]
    internal static extern uint LsaOpenPolicy(IntPtr systemName, ref LsaObjectAttributes attributes, uint desiredAccess, out IntPtr policy);

    [DllImport("advapi32.dll")]
    internal static extern uint LsaClose(IntPtr policy);

    [DllImport("advapi32.dll")]
    internal static extern uint LsaNtStatusToWinError(uint status);

    [DllImport("advapi32.dll")]
    internal static extern uint LsaStorePrivateData(IntPtr policy, ref LsaUnicodeString key, ref LsaUnicodeString privateData);

    [DllImport("advapi32.dll", EntryPoint = "LsaStorePrivateData")]
    internal static extern uint LsaStorePrivateDataDelete(IntPtr policy, ref LsaUnicodeString key, IntPtr privateData);
}
