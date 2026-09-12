using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using WebDisplay.Models;

namespace WebDisplay.Services;

internal static class SelfTests
{
    public static int Run(string directory)
    {
        var checks = new List<object>();
        int failures = 0;
        void Check(string name, Action test)
        {
            try { test(); checks.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; checks.Add(new { name, passed = false, error = ex.Message }); }
        }
        static void Assert(bool condition, string message = "Unexpected result") { if (!condition) throw new InvalidOperationException(message); }
        static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid input accepted"); }

        Check("Default system mutations disabled", () => { var s = new AppSettings(); Assert(!s.StartAtLogon && !s.RestartEnabled); });
        Check("HTTPS and intranet HTTP accepted", () => Assert(AppSettings.IsValidUrl("https://example.com/dashboard?q=a") && AppSettings.IsValidUrl("http://intranet:8080")));
        foreach (string url in new[] { "javascript:alert(1)", "file:///C:/Windows", "example.com", "https://", "https://user:secret@example.com", "ftp://example.com" })
            Check("Reject unsafe URL " + url.Split(':')[0], () => Assert(!AppSettings.IsValidUrl(url)));
        Check("Refresh interval bounds", () => { new AppSettings { RefreshIntervalMinutes = 1 }.Validate(); new AppSettings { RefreshIntervalMinutes = 10080 }.Validate(); Reject(() => new AppSettings { RefreshIntervalMinutes = 0 }.Validate()); Reject(() => new AppSettings { RefreshIntervalMinutes = 10081 }.Validate()); });
        Check("Independent settings clone", () => { var a = new AppSettings(); var b = a.Clone(); b.Url = "https://different.example"; Assert(a.Url != b.Url); });
        Check("Theme choices and legacy config compatibility", () =>
        {
            foreach (string theme in new[] { "System", "Light", "Dark" }) new AppSettings { ThemePreference = theme }.Validate();
            Reject(() => new AppSettings { ThemePreference = "invalid" }.Validate());
            var legacy = JsonSerializer.Deserialize<AppSettings>("{\"Url\":\"https://example.com\",\"FullScreen\":false}")!;
            Assert(legacy.ThemePreference == "System" && !legacy.FullScreen);
        });
        Check("Exponential retry with 60 second cap and reset", () => { var p = new RecoveryPolicy(); var delays = Enumerable.Range(0, 8).Select(_ => p.NextDelay().TotalSeconds).ToArray(); Assert(delays.SequenceEqual(new double[] { 5, 10, 20, 40, 60, 60, 60, 60 })); p.Reset(); Assert(p.NextDelay().TotalSeconds == 5); });
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Check("Daily reboot no missed-time catch-up or forced close", () =>
        {
            var xml = XDocument.Parse(WindowsIntegrationService.BuildRestartTaskXml("03:00", "Daily", new DateTime(2026, 9, 9, 4, 0, 0)));
            Assert(xml.Descendants(ns + "StartBoundary").Single().Value == "2026-09-10T03:00:00");
            Assert(xml.Descendants(ns + "StartWhenAvailable").Single().Value == "false");
            Assert(xml.Descendants(ns + "Arguments").Single().Value == "/r /t 0");
            Assert(xml.Descendants(ns + "DaysInterval").Single().Value == "1");
        });
        Check("Weekly reboot selected days", () =>
        {
            var xml = XDocument.Parse(WindowsIntegrationService.BuildRestartTaskXml("23:30", "FRI,MON,FRI", new DateTime(2026, 9, 9, 4, 0, 0)));
            Assert(xml.Descendants(ns + "DaysOfWeek").Single().Elements().Select(x => x.Name.LocalName).SequenceEqual(new[] { "Monday", "Friday" }));
            Assert(xml.Descendants(ns + "StartBoundary").Single().Value == "2026-09-09T23:30:00");
        });
        Check("Reject invalid reboot time and day input before elevation", () =>
        {
            Reject(() => WindowsIntegrationService.BuildRestartTaskXml("25:99", "Daily", DateTime.Now));
            Reject(() => WindowsIntegrationService.BuildRestartTaskXml("03:00", "", DateTime.Now));
            Reject(() => WindowsIntegrationService.BuildRestartTaskXml("03:00", "MON;<payload>", DateTime.Now));
        });
        Check("Settings persistence and corrupt-file recovery", () =>
        {
            var store = new SettingsStore(Path.Combine(directory, "self-test-data"));
            var expected = new AppSettings { Url = "https://example.com/test", AutoRefreshEnabled = true, RefreshIntervalMinutes = 17, FullScreen = false, PreventSleep = false, ThemePreference = "Dark" };
            store.Save(expected);
            var actual = store.Load();
            Assert(actual.Url == expected.Url && actual.RefreshIntervalMinutes == 17 && !actual.PreventSleep && actual.ThemePreference == "Dark");
            Assert(!File.ReadAllText(store.FilePath).Contains("password", StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(store.FilePath, "{broken");
            Assert(store.Load().Url == new AppSettings().Url && store.LoadWarning != null);
            store.Save(expected);
        });
        File.WriteAllText(Path.Combine(directory, "self-test-result.json"), JsonSerializer.Serialize(new { passed = failures == 0, total = checks.Count, failures, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
