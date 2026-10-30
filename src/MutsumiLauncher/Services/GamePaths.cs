using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 游戏目录优先级：MTL_GAME_DIR 环境变量 → 设置里的 GameRoot → 安装目录下的 game\。
/// 不再往 %APPDATA% 写任何东西，数据全在用户自己的安装目录里。
/// </summary>
public static class GamePaths
{
    /// <summary>安装在 C 盘时用户可能会改到别的盘，这里记一下默认值给设置页显示。</summary>
    public static string DefaultRoot { get; } = Path.Combine(AppContext.BaseDirectory, "game");

    public static string Root { get; private set; } = Resolve();

    public static string VersionsDir => Path.Combine(Root, "versions");

    public static string LibrariesDir => Path.Combine(Root, "libraries");

    public static string AssetsDir => Path.Combine(Root, "assets");

    /// <summary>安装器解压 / 临时文件都放在游戏目录里，别再落到 C 盘的 TEMP。</summary>
    public static string TempDir => Path.Combine(Root, "temp");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(VersionsDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(AssetsDir);
        Directory.CreateDirectory(TempDir);
    }

    /// <summary>设置里改了游戏目录：写进配置并重新解析（重启后所有服务才会跟着换）。</summary>
    public static void SetRoot(string? path)
    {
        AppConfig.Current.GameRoot = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        AppConfig.Current.Save();
        Root = Resolve();
    }

    public static void Reload() => Root = Resolve();

    private static string Resolve()
    {
        var custom = Environment.GetEnvironmentVariable("MTL_GAME_DIR");
        if (!string.IsNullOrWhiteSpace(custom) && TryCreate(custom)) return custom;

        var configured = AppConfig.Current.GameRoot;
        if (!string.IsNullOrWhiteSpace(configured) && TryCreate(configured)) return configured;

        // 安装目录下的 game（默认位置）
        TryCreate(DefaultRoot);
        return DefaultRoot;
    }

    /// <summary>
    /// 老版本把游戏数据放在 %APPDATA%\.mutsumi 或安装目录的 game',
    /// 这里只负责把 %APPDATA% 的那份搬过来（搬完删掉旧目录）。
    /// </summary>
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
            return true;
        }
        catch
        {
            return false;
        }
    }
}
