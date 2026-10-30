using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 游戏目录优先级：MTL_GAME_DIR 环境变量 → 设置里的 GameRoot → 安装目录下的 game\。
/// 不再往 %APPDATA% 写任何东西，数据全在用户自己的安装目录里。
///
/// 目录结构（LayoutVersion 2）：
///   &lt;GameRoot&gt;\.minecraft\versions|  libraries|  assets|   ← 标准 Minecraft 布局
///   &lt;GameRoot&gt;\modpacks\downloads|exports                  ← 整合包
///   &lt;GameRoot&gt;\temp                                        ← 安装器临时文件
///
/// 旧版本（LayoutVersion 1）把 versions / libraries / assets 直接放在 GameRoot 下，
/// 迁移前继续按旧布局工作，界面会提示用户整理。
/// </summary>
public static class GamePaths
{
    /// <summary>安装在 C 盘时用户可能会改到别的盘，这里记一下默认值给设置页显示。</summary>
    public static string DefaultRoot { get; } = Path.Combine(AppContext.BaseDirectory, "game");

    public static string Root { get; private set; } = Resolve();

    /// <summary>标准 Minecraft 目录（layout 2 才有意义；layout 1 下等于 Root）。</summary>
    public static string MinecraftDir => _layout == 2 ? Path.Combine(Root, ".minecraft") : Root;

    public static string VersionsDir => Path.Combine(MinecraftDir, "versions");

    public static string LibrariesDir => Path.Combine(MinecraftDir, "libraries");

    public static string AssetsDir => Path.Combine(MinecraftDir, "assets");

    public static string ModpacksDir => Path.Combine(Root, "modpacks");

    public static string ModpacksDownloadsDir => Path.Combine(ModpacksDir, "downloads");

    public static string ModpacksExportsDir => Path.Combine(ModpacksDir, "exports");

    /// <summary>安装器解压 / 临时文件都放在游戏目录里，别再落到 C 盘的 TEMP。</summary>
    public static string TempDir => Path.Combine(Root, "temp");

    /// <summary>当前生效的布局版本（1 = 旧的扁平结构，2 = .minecraft 子目录）。</summary>
    public static int Layout => _layout;

    /// <summary>检测到旧布局、等用户确认后迁移。</summary>
    public static bool NeedsLayoutMigration { get; private set; }

    private static int _layout;

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(VersionsDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(AssetsDir);
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(ModpacksDownloadsDir);
        Directory.CreateDirectory(ModpacksExportsDir);
    }

    /// <summary>设置里改了游戏目录：写进配置并重新解析（重启后所有服务才会跟着换）。</summary>
    public static void SetRoot(string? path)
    {
        AppConfig.Current.GameRoot = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        AppConfig.Current.Save();
        Root = Resolve();
    }

    public static void Reload()
    {
        Root = Resolve();
    }

    private static string Resolve()
    {
        var custom = Environment.GetEnvironmentVariable("MTL_GAME_DIR");
        if (!string.IsNullOrWhiteSpace(custom) && TryCreate(custom))
        {
            _layout = DetectLayout(custom);
            return custom;
        }

        var configured = AppConfig.Current.GameRoot;
        if (!string.IsNullOrWhiteSpace(configured) && TryCreate(configured))
        {
            _layout = DetectLayout(configured);
            return configured;
        }

        // 安装目录下的 game\（默认位置）
        TryCreate(DefaultRoot);
        _layout = DetectLayout(DefaultRoot);
        return DefaultRoot;
    }

    /// <summary>
    /// 判断布局：.minecraft\versions 存在 = 新；只有 versions = 旧（并标记待迁移）；
    /// 都没有 = 新（首次使用，目录按需创建）。
    /// </summary>
    private static int DetectLayout(string root)
    {
        var modern = Path.Combine(root, ".minecraft", "versions");
        if (Directory.Exists(modern))
        {
            NeedsLayoutMigration = false;
            return 2;
        }

        var legacy = Path.Combine(root, "versions");
        if (Directory.Exists(legacy) && Directory.EnumerateFileSystemEntries(legacy).Any())
        {
            NeedsLayoutMigration = true;
            return 1;
        }

        NeedsLayoutMigration = false;
        return 2;
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