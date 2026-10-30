using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 目录布局迁移：把旧版扁平的 game\versions|libraries|assets 收进 game\.minecraft\。
/// 先改名（同盘瞬间完成），跨盘才复制；校验通过才写 LayoutVersion，失败原样退回。
/// </summary>
public static class LayoutMigrator
{
    public sealed record Result(bool Ok, string? Error);

    private static readonly string[] Shared = ["versions", "libraries", "assets"];

    /// <summary>迁移需要多少东西（给确认框显示）。</summary>
    public static (int Files, long Bytes) Measure()
    {
        long bytes = 0;
        var files = 0;

        foreach (var name in Shared)
        {
            var dir = Path.Combine(GamePaths.Root, name);
            if (!Directory.Exists(dir)) continue;

            var (count, size) = PathMigrator.Measure(dir);
            files += count;
            bytes += size;
        }

        return (files, bytes);
    }

    /// <summary>执行迁移。失败时把已经搬过去的目录搬回来，保证还能用旧布局启动。</summary>
    public static Result Migrate(Action<string>? progress = null)
    {
        var root = GamePaths.Root;
        var target = Path.Combine(root, ".minecraft");

        var moved = new List<(string From, string To)>();

        try
        {
            Directory.CreateDirectory(target);

            foreach (var name in Shared)
            {
                var source = Path.Combine(root, name);
                if (!Directory.Exists(source)) continue;

                var destination = Path.Combine(target, name);

                progress?.Invoke($"正在整理 {name}…");

                // 目标已存在：合并进去（少见，通常是用户手动动过）
                if (Directory.Exists(destination))
                {
                    if (!MergeInto(source, destination, progress))
                        return Rollback(moved, "目标目录已存在，合并失败");
                    continue;
                }

                try
                {
                    Directory.Move(source, destination);
                }
                catch (IOException)
                {
                    // 跨盘：复制后删源
                    var copy = PathMigrator.CopyDirectory(source, destination, progress);
                    if (!copy.Ok) return Rollback(moved, copy.Error);

                    var (sourceCount, _) = PathMigrator.Measure(source);
                    var (targetCount, _) = PathMigrator.Measure(destination);

                    if (sourceCount != targetCount)
                        return Rollback(moved, $"{name} 复制后文件数不一致");

                    Directory.Delete(source, recursive: true);
                }

                moved.Add((source, destination));
            }

            Directory.CreateDirectory(GamePaths.ModpacksDownloadsDir);
            Directory.CreateDirectory(GamePaths.ModpacksExportsDir);

            AppConfig.Current.LayoutVersion = 2;
            AppConfig.Current.Save();

            GamePaths.Reload();
            progress?.Invoke("整理完成");

            return new Result(true, null);
        }
        catch (Exception ex)
        {
            return Rollback(moved, ex.Message);
        }
    }

    private static Result Rollback(List<(string From, string To)> moved, string? error)
    {
        error ??= "未知错误";
        AppLog.Error(new InvalidOperationException(error), "layout migration");

        foreach (var (from, to) in Enumerable.Reverse(moved))
        {
            try
            {
                if (Directory.Exists(from)) continue;
                if (!Directory.Exists(to)) continue;

                Directory.Move(to, from);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "layout rollback");
            }
        }

        return new Result(false, error);
    }

    private static bool MergeInto(string source, string target, Action<string>? progress)
    {
        try
        {
            foreach (var entry in Directory.EnumerateDirectories(source))
            {
                var name = Path.GetFileName(entry);
                var destination = Path.Combine(target, name);

                if (Directory.Exists(destination)) continue;

                Directory.Move(entry, destination);
                progress?.Invoke($"合并 {name}…");
            }

            foreach (var file in Directory.EnumerateFiles(source))
            {
                var destination = Path.Combine(target, Path.GetFileName(file));
                if (!File.Exists(destination)) File.Move(file, destination);
            }

            Directory.Delete(source, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "merge into .minecraft");
            return false;
        }
    }
}
