using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>应用数据目录：优先 %LOCALAPPDATA%，不可用时退回程序目录下的 data\。</summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Resolve();

    public static string CacheDir => Path.Combine(DataRoot, "cache");

    public static string LogPath => Path.Combine(DataRoot, "logs", "app.log");

    private static string Resolve()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                var dir = Path.Combine(local, "Mutsumi Launcher");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        catch
        {
            // 继续走兜底
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
