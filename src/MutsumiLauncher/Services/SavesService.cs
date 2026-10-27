using System.IO;
using System.IO.Compression;

namespace MutsumiLauncher.Services;

/// <summary>saves 目录里的一个世界。</summary>
public sealed record SaveWorld(
    string Folder,
    string Directory,
    string? Name,
    string? GameVersion,
    DateTimeOffset? LastPlayed,
    long SizeBytes,
    string? IconPath)
{
    /// <summary>界面上显示的名字：优先 level.dat 里的 LevelName，读不到就用文件夹名。</summary>
    public string DisplayName => SavesService.CleanName(Name) ?? Folder;
}

/// <summary>一个存档备份（zip）。</summary>
public sealed record SaveBackup(string Path, DateTime CreatedAt, long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// 存档管理：列世界、打包备份、还原、删除。
/// 备份放在 &lt;game&gt;/mtl-backups/saves/&lt;世界文件夹&gt;/&lt;时间戳&gt;.zip，
/// 每个世界一个子目录，避免不同世界互相匹配到对方的备份。
/// </summary>
public static class SavesService
{
    /// <summary>备份根目录名（放在游戏目录下，和 saves 平级）。</summary>
    public const string BackupFolderName = "mtl-backups";

    private const string RestorePrefix = ".mtl-restore-";

    public static string SavesDirFor(string gameDir) => Path.Combine(gameDir, "saves");

    public static string BackupRootFor(string gameDir) => Path.Combine(gameDir, BackupFolderName, "saves");

    public static string BackupDirFor(string backupsDir, string worldFolder)
        => Path.Combine(backupsDir, SafeName(worldFolder));

    /// <summary>扫描存档目录。读不到的单个世界会被跳过，不影响其它世界。</summary>
    public static IReadOnlyList<SaveWorld> List(string savesDir)
    {
        if (!Directory.Exists(savesDir)) return [];

        var worlds = new List<SaveWorld>();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(savesDir))
            {
                var folder = Path.GetFileName(dir);

                // 还原过程中的临时目录、被替换掉的旧目录都不是存档
                if (folder.StartsWith('.') || folder.Contains(".mtl-old-", StringComparison.OrdinalIgnoreCase))
                    continue;

                long size = 0;
                try
                {
                    size = DirectorySize(dir);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "size of save " + folder);
                }

                Nbt.LevelMeta? meta = null;
                try
                {
                    var levelDat = Path.Combine(dir, "level.dat");
                    if (File.Exists(levelDat)) meta = Nbt.ReadLevelDat(levelDat);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "level.dat of " + folder);
                }

                var icon = Path.Combine(dir, "icon.png");
                var lastPlayed = meta?.LastPlayed
                                 ?? new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir), TimeSpan.Zero);

                worlds.Add(new SaveWorld(
                    folder,
                    dir,
                    meta?.Name,
                    meta?.GameVersion,
                    lastPlayed,
                    size,
                    File.Exists(icon) ? icon : null));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "scan saves");
        }

        return worlds
            .OrderByDescending(world => world.LastPlayed ?? DateTimeOffset.MinValue)
            .ThenBy(world => world.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>把这个世界打包成 zip；返回写好的备份。</summary>
    public static async Task<SaveBackup> BackupAsync(string backupsDir, SaveWorld world,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        var targetDir = BackupDirFor(backupsDir, world.Folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = UniquePath(Path.Combine(targetDir, stamp + ".zip"));

        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(targetDir);

            try
            {
                WriteArchive(world, target, progress, token);
            }
            catch
            {
                // 失败的半截备份不要留在目录里
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                }
                catch
                {
                    // 删不掉就算了
                }

                throw;
            }
        }, token).ConfigureAwait(false);

        var info = new FileInfo(target);
        AppLog.Info($"save backup: {world.Folder} -> {target} ({info.Length} bytes)");

        return new SaveBackup(target, info.LastWriteTime, info.Length);
    }

    private static void WriteArchive(SaveWorld world, string target, IProgress<double>? progress,
        CancellationToken token)
    {
        var files = Directory
            .EnumerateFiles(world.Directory, "*", SearchOption.AllDirectories)
            .ToList();

        long total = 0;
        foreach (var file in files)
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // 单个文件读不到就按 0 算，进度条略微不准可以接受
            }
        }

        using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        long done = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var entryName = Path.GetRelativePath(world.Directory, file).Replace('\\', '/');
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            entry.LastWriteTime = SafeTimestamp(File.GetLastWriteTime(file));

            using (var source = File.Open(file, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var destination = entry.Open())
            {
                source.CopyTo(destination, 81920);
            }

            try
            {
                done += new FileInfo(file).Length;
            }
            catch
            {
                // 同上
            }

            if (total > 0) progress?.Report(Math.Clamp(done / (double)total, 0, 1));
        }

        progress?.Report(1);
    }

    /// <summary>某个世界的备份，新的在前。</summary>
    public static IReadOnlyList<SaveBackup> ListBackups(string backupsDir, string worldFolder)
    {
        var dir = BackupDirFor(backupsDir, worldFolder);
        if (!Directory.Exists(dir)) return [];

        var backups = new List<SaveBackup>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.zip"))
            {
                var info = new FileInfo(file);
                backups.Add(new SaveBackup(info.FullName, info.LastWriteTime, info.Length));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "list save backups");
        }

        return backups.OrderByDescending(backup => backup.CreatedAt).ToList();
    }

    /// <summary>
    /// 用备份覆盖存档目录：先解压到临时目录，再整体换过去，
    /// 中途失败时原存档还在，不会被写坏一半。
    /// </summary>
    public static void Restore(SaveBackup backup, string worldDir)
    {
        var parent = Path.GetDirectoryName(worldDir)
                     ?? throw new InvalidDataException("存档目录不合法");

        Directory.CreateDirectory(parent);

        var temp = Path.Combine(parent, RestorePrefix + Guid.NewGuid().ToString("N"));

        try
        {
            ZipFile.ExtractToDirectory(backup.Path, temp);

            if (!Directory.EnumerateFileSystemEntries(temp).Any())
                throw new InvalidDataException("备份是空的。");

            var replaced = Path.Combine(parent, $"{Path.GetFileName(worldDir)}.mtl-old-{DateTime.Now:yyyyMMddHHmmss}");

            if (Directory.Exists(worldDir)) Directory.Move(worldDir, replaced);

            try
            {
                Directory.Move(temp, worldDir);
            }
            catch
            {
                // 换不过去就把原存档恢复回来
                if (!Directory.Exists(worldDir) && Directory.Exists(replaced)) Directory.Move(replaced, worldDir);
                throw;
            }

            try
            {
                if (Directory.Exists(replaced)) Directory.Delete(replaced, recursive: true);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "delete replaced save");
            }

            AppLog.Info($"save restored: {backup.FileName} -> {worldDir}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // 临时目录删不掉就留着，下次扫描也会跳过（以 . 开头）
            }
        }
    }

    public static void DeleteBackup(SaveBackup backup)
    {
        if (File.Exists(backup.Path)) File.Delete(backup.Path);
    }

    public static void DeleteWorld(SaveWorld world)
    {
        if (Directory.Exists(world.Directory)) Directory.Delete(world.Directory, recursive: true);
    }

    public static long DirectorySize(string dir)
    {
        long total = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // 单个文件读不到就跳过
            }
        }

        return total;
    }

    /// <summary>去掉存档名里的 § 颜色代码（玩家真的会拿它给世界起名）。</summary>
    public static string? CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var index = value.IndexOf('§');
        if (index < 0) return value.Trim();

        var builder = new System.Text.StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '§' && i + 1 < value.Length)
            {
                i++;  // 跳过 § 和它后面那个字符
                continue;
            }

            builder.Append(value[i]);
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>文件名里去不掉的非法字符换成下划线。</summary>
    private static string SafeName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(dir, $"{name}-{index}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>zip 的时间戳只支持 1980–2107，越界会直接抛异常。</summary>
    private static DateTimeOffset SafeTimestamp(DateTime value)
    {
        var min = new DateTime(1980, 1, 1);
        var max = new DateTime(2107, 12, 31);
        var safe = value < min ? min : value > max ? max : value;
        return new DateTimeOffset(safe);
    }
}
