using System.Windows;
using System.Windows.Threading;
using MutsumiLauncher.Services;

namespace MutsumiLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Info($"启动 Mutsumi Launcher，.NET {Environment.Version}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error(args.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception"));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error(e.Exception);
        MessageBox.Show(
            $"出了点问题：{e.Exception.Message}\n\n日志已写入：{AppLog.LogPath}",
            "Mutsumi Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
