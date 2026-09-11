using System;
using Microsoft.UI.Xaml;
using WebDisplay.Services;

namespace WebDisplay;

public partial class App : Application
{
    private readonly SettingsStore _store;
    private readonly bool _smoke;
    private MainWindow? _window;
    public App(SettingsStore store, bool smoke)
    {
        _store = store; _smoke = smoke;
        InitializeComponent();
        UnhandledException += (_, args) => AppLog.Write("Unhandled WinUI error: " + args.Exception.GetType().Name + ": " + args.Exception.Message);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow(_store, _smoke);
            _window.Closed += (_, _) => Exit();
            _window.Activate();
        }
        catch (Exception ex)
        {
            AppLog.Write("Window creation failed: " + ex);
            WindowInteropService.ShowMessage(null, "网页展示器", "程序启动失败：" + ex.Message);
            Environment.ExitCode = 1;
            Exit();
        }
    }
}

