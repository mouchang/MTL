using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 游戏目录优先级：MTL_GAME_DIR → 安装目录下的 game\（可写时）→ %APPDATA%\.mutsumi。
/// 这样默认把版本、资源、日志都放在安装文件夹里。
/// </summary>
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

        // 安装目录下的 game\（默认位置）
        var local = Path.Combine(AppContext.BaseDirectory, "game");
        if (TryCreate(local)) return local;

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

    /// <summary>旧版本把数据放在 %APPDATA%\.mutsumi；如果新目录还是空的，自动迁移过去。</summary>
    public static void MigrateFromAppDataIfNeeded()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData)) return;

            var old = Path.Combine(appData, ".mutsumi");
            if (!Directory.Exists(old)) return;
            if (Path.GetFullPath(old).Equals(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.EnumerateFileSystemEntries(Root).Any()) return;
            if (!Directory.EnumerateFileSystemEntries(old).Any()) return;

            AppLog.Info($"migrating game data: {old} -> {Root}");

            foreach (var entry in Directory.EnumerateFileSystemEntries(old))
            {
                var name = Path.GetFileName(entry);
                var target = Path.Combine(Root, name);

                try
                {
                    if (Directory.Exists(entry))
                    {
                        try
                        {
                            Directory.Move(entry, target);
                        }
                        catch (IOException)
                        {
                            // 跨盘时 Move 不行，改为复制后删除
                            CopyDirectory(entry, target);
                            Directory.Delete(entry, recursive: true);
                        }
                    }
                    else
                    {
                        File.Move(entry, target, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"migrate {name}");
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(old).Any()) Directory.Delete(old);
            }
            catch
            {
                // 旧目录删不掉就留着
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "migrate game data");
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = file.Replace(source, target, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static bool TryCreate(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            // 目录存在不代表可写（例如装在 Program Files 里、由管理员创建过）
            var probe = Path.Combine(path, ".mtl-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
