using System.IO;
using System.Threading;
using System.Windows;
using MutsumiLauncher.Services;

namespace MutsumiLauncher;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    public App()
    {
        // 兜底：启动阶段（加载 XAML/资源）出错时至少留下日志，方便在别的机器上排查
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteStartupError(args.ExceptionObject as Exception);

        DispatcherUnhandledException += (_, args) =>
            WriteStartupError(args.Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _singleInstance = new Mutex(true, "MutsumiLauncher.SingleInstance", out var isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            // 卸载程序调用：先断开所有引用实例，再让 NSIS 删目录
            if (Environment.GetCommandLineArgs().Any(arg =>
                    arg.Equals("--cleanup-references", StringComparison.OrdinalIgnoreCase)))
            {
                if (ExternalSourceService.DisconnectAll() is var removed)
                    AppLog.Info($"uninstall cleanup: 断开 {removed} 个引用实例");

                Shutdown(0);
                return;
            }

            NormalizePath();

            // 老版本把配置/缓存写在 %LOCALAPPDATA%：搬到安装目录后删掉旧的，C 盘不留东西
            PathMigrator.MigrateLegacyDataIfNeeded();

            GamePaths.MigrateFromAppDataIfNeeded();
            GamePaths.EnsureCreated();

            // 必须在主窗口创建之前套主题，否则第一帧会闪一下浅色
            ThemeService.ApplyFromConfig();
            ThemeService.Verify();

            base.OnStartup(e);
            AppLog.Info($"启动 Mutsumi Launcher，.NET {Environment.Version}");
            Exit += (_, _) => _singleInstance?.ReleaseMutex();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(1);
        }
    }

    /// <summary>启动失败时写日志并弹窗，避免精简版系统上双击没反应、无从排查。</summary>
    private static void WriteStartupError(Exception? exception)
    {
        if (exception is null) return;

        var details = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}";
        // 优先写到安装目录（和数据放一起），实在写不了才退回系统的临时目录
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
            Path.Combine(AppContext.BaseDirectory, "data", "startup-error.log"),
            Path.Combine(Path.GetTempPath(), "MutsumiLauncher-startup-error.log"),
        };

        foreach (var path in candidates)
        {
            try
            {
                File.AppendAllText(path, details + Environment.NewLine);
                break;
            }
            catch
            {
                // 换下一个位置
            }
        }

        try
        {
            MessageBox.Show(
                $"启动失败：{exception.Message}\n\n详细信息已写入：{candidates[0]}",
                "Mutsumi Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // 连弹窗都失败就算了
        }
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

}