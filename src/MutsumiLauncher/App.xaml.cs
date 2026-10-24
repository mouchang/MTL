using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MutsumiLauncher.Services;

namespace MutsumiLauncher;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "MutsumiLauncher.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        NormalizePath();

        base.OnStartup(e);
        AppLog.Info($"启动 Mutsumi Launcher，.NET {Environment.Version}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Exit += (_, _) => _singleInstance?.ReleaseMutex();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error(args.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception"));
    }

    /// <summary>清理从 MSYS/Git 继承的 PATH，避免子进程加载到错误 DLL（0xc0000142）。</summary>
    private static void NormalizePath()
    {
        try
        {
            var machine = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            var user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            var combined = string.Join(';', new[] { machine, user }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

            if (!string.IsNullOrWhiteSpace(combined))
                Environment.SetEnvironmentVariable("PATH", combined, EnvironmentVariableTarget.Process);

            // 清掉 MSYS 相关变量，避免影响孙进程
            foreach (var name in new[] { "MSYSTEM", "MINGW_PREFIX", "MINGW_CHOST", "MSYS2_PATH_TYPE" })
                Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "normalize PATH");
        }
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
