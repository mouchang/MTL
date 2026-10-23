using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>游戏目录：优先 %APPDATA%\.mutsumi，不可写或指定 MTL_GAME_DIR 时退回程序目录下的 game\。</summary>
public static class GamePaths
{
    public static string Root { get; } = Resolve();

    public static string VersionsDir => Path.Combine(Root, "versions");

    public static string LibrariesDir => Path.Combine(Root, "libraries");

    public static string AssetsDir => Path.Combine(Root, "assets");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(VersionsDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(AssetsDir);
    }

    private static string Resolve()
    {
        var custom = Environment.GetEnvironmentVariable("MTL_GAME_DIR");
        if (!string.IsNullOrWhiteSpace(custom) && TryCreate(custom)) return custom;

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                var dir = Path.Combine(appData, ".mutsumi");
                if (TryCreate(dir)) return dir;
            }
        }
        catch
        {
            // 继续走兜底
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "game");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool TryCreate(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
