using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>极简日志：优先 %LOCALAPPDATA%\Mutsumi Launcher\logs\app.log，失败则退回程序目录。</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = ResolveLogPath();

    private static string ResolveLogPath()
    {
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(dir))
                return Path.Combine(dir, "Mutsumi Launcher", "logs", "app.log");
        }
        catch
        {
            // 忽略，走兜底
        }

        return Path.Combine(AppContext.BaseDirectory, "mtl.log");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(Exception? exception, string? context = null)
    {
        if (exception is null) return;
        Write("ERROR", $"{context ?? "unhandled"}: {exception}");
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, line);
            }
            return;
        }
        catch
        {
            // 继续尝试兜底路径
        }

        try
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "mtl.log");
            File.AppendAllText(fallback, line);
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }
}
