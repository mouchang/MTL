using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 目录迁移：把数据从一个位置搬到另一个位置（先复制、校验，再删旧）。
/// 迁移失败时保留旧目录，绝不在没验证之前删东西。
/// </summary>
public static class PathMigrator
{
    public sealed record Result(bool Ok, int Files, long Bytes, string? Error);

    /// <summary>
    /// 老版本把配置 / 缓存 / 图标 / 日志写在 %LOCALAPPDATA%\
    /// Mutsumi Launcher。新版本一律放安装目录，首次启动时搬过来并删掉旧目录。
    /// </summary>
    public static void MigrateLegacyDataIfNeeded()
    {
        try
        {
            var legacy = AppPaths.LegacyDataRoot;
            var target = AppPaths.DataRoot;

            if (string.IsNullOrWhiteSpace(legacy) || !Directory.Exists(legacy)) return;
            if (Path.GetFullPath(legacy).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                return;

            // 新目录里已经有配置，说明已经搬过了
            if (File.Exists(Path.Combine(target, "config.json"))) return;

            AppLog.Info($"migrating legacy data: {legacy} -> {target}");

            var result = CopyDirectory(legacy, target, null, CancellationToken.None);

            if (!result.Ok)
            {
                AppLog.Info($"legacy migration failed: {result.Error}");
                return;
            }

            // 确认配置真的落过去了，再删旧目录
            if (!File.Exists(Path.Combine(target, "config.json")) && File.Exists(Path.Combine(legacy, "config.json")))
            {
                AppLog.Info("legacy migration: config.json 没搬过去，保留旧目录");
                return;
            }

            try
            {
                Directory.Delete(legacy, recursive: true);
                AppLog.Info($"legacy data removed: {legacy}");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "remove legacy data dir");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "migrate legacy data");
        }
    }

    /// <summary>把 <paramref name="source"/> 整棵复制到 <paramref name="target"/>（可跨盘）。</summary>
    public static Result CopyDirectory(string source, string target, Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(source)) return new Result(false, 0, 0, "源目录不存在");

            Directory.CreateDirectory(target);

            long bytes = 0;
            var files = 0;

            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Relative(dir, source, target));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destination = Relative(file, source, target);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                File.Copy(file, destination, overwrite: true);

                bytes += new FileInfo(file).Length;
                files++;

                if (files % 50 == 0) progress?.Invoke($"{files} 个文件 / {FormatSize(bytes)}");
            }

            progress?.Invoke($"完成：{files} 个文件 / {FormatSize(bytes)}");
            return new Result(true, files, bytes, null);
        }
        catch (OperationCanceledException)
        {
            return new Result(false, 0, 0, "已取消");
        }
        catch (Exception ex)
        {
            return new Result(false, 0, 0, ex.Message);
        }
    }

    /// <summary>迁移前估算体积，界面用来提示。</summary>
    public static (int Files, long Bytes) Measure(string source)
    {
        try
        {
            if (!Directory.Exists(source)) return (0, 0);

            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();

            return (files.Count, files.Sum(file => new FileInfo(file).Length));
        }
        catch
        {
            return (0, 0);
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.#} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    private static string Relative(string path, string source, string target) =>
        Path.Combine(target, Path.GetRelativePath(source, path));
}
