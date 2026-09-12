using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using WebDisplay.Services;

namespace WebDisplay;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == "--admin")
        {
            try { Shutdown(await WindowsIntegrationService.RunAdminCommandAsync(e.Args)); }
            catch { Shutdown(1); }
            return;
        }
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebDisplay");
            for (int i = 0; i < e.Args.Length - 1; i++) if (e.Args[i] == "--data-dir") directory = e.Args[++i];
            var store = new SettingsStore(directory);
            AppLog.Initialize(store.DataDirectory);
            if (Array.IndexOf(e.Args, "--self-test") >= 0) { Shutdown(SelfTests.Run(store.DataDirectory)); return; }
            bool smoke = Array.IndexOf(e.Args, "--smoke-test") >= 0;
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(store.DataDirectory.ToUpperInvariant())))[..20];
            _mutex = new Mutex(true, @"Local\WebDisplay-" + key, out _ownsMutex);
            if (!_ownsMutex) { MessageBox.Show("网页展示器已经在运行，请从任务栏或系统托盘打开设置。", "网页展示器"); Shutdown(); return; }
            DispatcherUnhandledException += (_, args) => { AppLog.Write("Unhandled UI error: " + args.Exception.GetType().Name); };
            var window = new MainWindow(store, smoke);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup failure: " + ex.GetType().Name);
            MessageBox.Show("程序启动失败：" + ex.Message, "网页展示器", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
