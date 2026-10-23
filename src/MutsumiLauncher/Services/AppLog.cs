using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>极简日志：写入 %LOCALAPPDATA%\Mutsumi Launcher\logs\app.log。</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mutsumi Launcher", "logs", "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(Exception? exception, string? context = null)
    {
        if (exception is null) return;
        Write("ERROR", $"{context ?? "unhandled"}: {exception}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }
}
