using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>本地缓存（版本清单缓存 + 图标缓存）的统计与清理。</summary>
public static class CacheService
{
    public static IReadOnlyList<string> Roots => [AppPaths.CacheDir, IconCache.CacheDir];

    public static long GetSize()
    {
        long total = 0;

        foreach (var dir in Roots)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                total += new DirectoryInfo(dir)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "cache size");
            }
        }

        return total;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1073741824.0:0.##} GB",
        >= 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    /// <summary>删除缓存文件，返回（文件数、释放字节数）。</summary>
    public static (int Files, long Freed) Clear()
    {
        var files = 0;
        var freed = 0L;

        foreach (var dir in Roots)
        {
            if (!Directory.Exists(dir)) continue;

            List<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "cache clear enumerate");
                continue;
            }

            foreach (var path in paths)
            {
                try
                {
                    var length = new FileInfo(path).Length;
                    File.Delete(path);
                    files++;
                    freed += length;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "cache clear file");
                }
            }
        }

        return (files, freed);
    }
}
