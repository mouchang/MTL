using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 导入外部 .minecraft 的版本：复制到我们自己的 .minecraft\versions，
/// 成功后按用户选择删除源版本目录（或整个源 .minecraft）。
/// </summary>
public static class MinecraftImportService
{
    public sealed record VersionInfo(string Name, long Bytes, int Files);

    public sealed record Options(
        bool CopySharedFiles,
        bool DeleteSourceVersions,
        bool DeleteWholeSource);

    public sealed record Progress(string Message, double Percent);

    public sealed record Result(
        List<string> Imported,
        List<(string Name, string Reason)> Failed,
        long Bytes);

    /// <summary>扫描可导入的版本（带体积）。</summary>
    public static async Task<IReadOnlyList<VersionInfo>> ScanAsync(string root,
        CancellationToken cancellationToken = default)
    {
        var names = ExternalSourceService.ListVersions(root);
        var result = new List<VersionInfo>(names.Count);

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dir = Path.Combine(root, "versions", name);

            var (files, bytes) = await Task.Run(() => PathMigrator.Measure(dir), cancellationToken);
            result.Add(new VersionInfo(name, bytes, files));
        }

        return result;
    }

    /// <summary>把选中的版本复制进来；libraries / assets 只补缺失的文件。</summary>
    public static async Task<Result> ImportAsync(string root, IReadOnlyCollection<string> versions,
        Options options, IProgress<Progress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var imported = new List<string>();
        var failed = new List<(string, string)>();
        long totalBytes = 0;

        Directory.CreateDirectory(GamePaths.VersionsDir);
        Directory.CreateDirectory(GamePaths.LibrariesDir);
        Directory.CreateDirectory(GamePaths.AssetsDir);

        var index = 0;

        foreach (var version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            index++;
            var percent = (index - 1) * 100.0 / Math.Max(1, versions.Count);

            progress?.Report(new Progress($"正在导入 {version}…", percent));

            try
            {
                var source = Path.Combine(root, "versions", version);
                var target = Path.Combine(GamePaths.VersionsDir, version);

                if (!Directory.Exists(source))
                {
                    failed.Add((version, "源目录不存在"));
                    continue;
                }

                if (Directory.Exists(target))
                {
                    failed.Add((version, "本地已经有同名版本"));
                    continue;
                }

                var copy = await Task.Run(
                    () => PathMigrator.CopyDirectory(source, target), cancellationToken);

                if (!copy.Ok)
                {
                    failed.Add((version, copy.Error ?? "复制失败"));
                    TryDelete(target);
                    continue;
                }

                totalBytes += copy.Bytes;
                imported.Add(version);

                // 复制成功才删源版本
                if (options.DeleteSourceVersions)
                {
                    try
                    {
                        SafeDelete(source);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, "delete imported source " + version);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "import " + version);
                failed.Add((version, ex.Message));
            }
        }

        if (options.CopySharedFiles && imported.Count > 0)
        {
            progress?.Report(new Progress("补齐 libraries / assets…", 92));
            await Task.Run(() => CopyShared(root, cancellationToken), cancellationToken);
        }

        if (options.DeleteWholeSource && options.DeleteSourceVersions && failed.Count == 0)
        {
            progress?.Report(new Progress("删除源 .minecraft…", 98));

            try
            {
                SafeDelete(root);
                AppLog.Info("import: removed source " + root);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "delete entire source");
            }
        }

        progress?.Report(new Progress("完成", 100));
        return new Result(imported, failed, totalBytes);
    }

    /// <summary>只补本地缺的文件，已存在的跳过（按相对路径 + 大小判断）。</summary>
    private static void CopyShared(string root, CancellationToken cancellationToken)
    {
        foreach (var (sub, targetRoot) in new[]
                 {
                     ("libraries", GamePaths.LibrariesDir),
                     ("assets", GamePaths.AssetsDir),
                 })
        {
            var sourceRoot = Path.Combine(root, sub);
            if (!Directory.Exists(sourceRoot)) continue;

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(sourceRoot, file);
                var target = Path.Combine(targetRoot, relative);

                try
                {
                    if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(file).Length)
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "copy shared " + relative);
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) SafeDelete(path);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "cleanup " + path);
        }
    }

    /// <summary>逐项删除，遇到联接只删链接（和引用模块同一套安全逻辑）。</summary>
    private static void SafeDelete(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            try
            {
                var attributes = File.GetAttributes(entry);

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Directory.Delete(entry);
                    continue;
                }

                if (Directory.Exists(entry)) SafeDelete(entry);
                else File.Delete(entry);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "delete " + entry);
            }
        }

        Directory.Delete(dir);
    }
}
