using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.UI.Dispatching;
using WebDisplay.Services;

namespace WebDisplay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--admin")
        {
            try { return WindowsIntegrationService.RunAdminCommandAsync(args).GetAwaiter().GetResult(); }
            catch { return 1; }
        }
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebDisplay");
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--data-dir") directory = args[++i];
            var store = new SettingsStore(directory);
            AppLog.Initialize(store.DataDirectory);
            if (Array.IndexOf(args, "--self-test") >= 0) return SelfTests.Run(store.DataDirectory);
            bool smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            L.SetLanguage(smoke ? "zh-CN" : store.Load().Language);
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(store.DataDirectory.ToUpperInvariant())))[..20];
            using var mutex = new Mutex(true, @"Local\WebDisplay-" + key, out bool ownsMutex);
            if (!ownsMutex) { WindowInteropService.ShowMessage(null, L.Text("网页展示器"), L.Text("网页展示器已经运行，请从任务栏或系统托盘打开设置。")); return 0; }
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Microsoft.UI.Xaml.Application.Start(parameters =>
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new App(store, smoke);
                });
            }
            finally { mutex.ReleaseMutex(); }
            return Environment.ExitCode;
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup failed: " + ex);
            WindowInteropService.ShowMessage(null, L.Text("网页展示器"), L.Format("程序启动失败：{0}", ex.Message));
            return 1;
        }
    }
}

