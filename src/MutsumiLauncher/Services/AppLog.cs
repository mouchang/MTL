using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>极简日志：写到数据目录下的 logs\app.log（默认在安装目录里，不进 C 盘的 AppData）。</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogPath
    {
        get
        {
            try
            {
                return AppPaths.LogPath;
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, "mtl.log");
            }
        }
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
