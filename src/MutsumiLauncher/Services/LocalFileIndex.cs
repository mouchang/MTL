using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 本地已有文件索引（参考 PCL2 的 CheckExistingFile）：
/// 下载前先在本地目录里找一份「大小 + SHA-1」都一致的文件直接复制，省一次网络下载。
/// 只在明确知道 SHA-1 时才会复用，避免认错文件。
/// </summary>
public static class LocalFileIndex
{
    private static readonly object Gate = new();

    private static Dictionary<string, List<Entry>>? _index;
    private static Dictionary<long, List<Entry>>? _jarsBySize;
    private static DateTime _builtAt;

    /// <summary>索引有效期：安装过程中新增的文件不影响查找，过期后重建即可。</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);

    /// <summary>单次扫描的文件数上限，避免玩家的超大整合包目录拖慢安装。</summary>
    private const int MaxFiles = 20000;

    private sealed record Entry(long Size, string Path);

    /// <summary>丢弃索引，下次调用时重新扫描。</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _index = null;
            _jarsBySize = null;
        }
    }

    /// <summary>
    /// 目标位置还没有这个文件时，尝试在本地找一份完全一致的文件复制过来。
    /// 返回 true 表示已经就位，不需要下载。
    /// </summary>
    public static bool TryCopy(string targetPath, string? sha1, long? size)
    {
        if (string.IsNullOrWhiteSpace(sha1)) return false;

        var name = Path.GetFileName(targetPath);
        if (string.IsNullOrWhiteSpace(name)) return false;

        // 目标已经是对的，交给调用方
        if (FileIntegrity.Matches(targetPath, sha1, size)) return true;

        var candidates = Lookup(name, size);
        if (candidates.Count == 0) return false;

        foreach (var candidate in candidates)
        {
            if (candidate.Equals(targetPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!FileIntegrity.Matches(candidate, sha1, size)) continue;

            try
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                var temp = targetPath + ".reuse";
                File.Copy(candidate, temp, overwrite: true);
                File.Move(temp, targetPath, overwrite: true);

                AppLog.Info($"local reuse: {name} <- {candidate}");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"reuse {candidate}");
            }
        }

        return false;
    }

    private static List<string> Lookup(string name, long? size)
    {
        EnsureIndex();

        lock (Gate)
        {
            var candidates = new List<Entry>();

            if (_index is not null && _index.TryGetValue(name, out var entries))
                candidates.AddRange(entries.Where(entry => size is null || entry.Size == size.Value));

            // jar 经常被改名（1.21.1.jar / 1.21.1-OptiFine.jar），名字对不上时按大小找同哈希的 jar
            if (candidates.Count == 0
                && size is > 0
                && name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                && _jarsBySize is not null
                && _jarsBySize.TryGetValue(size.Value, out var jars))
            {
                candidates.AddRange(jars);
            }

            return candidates
                .Select(entry => entry.Path)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }
    }

    private static Dictionary<string, List<Entry>> EnsureIndex()
    {
        lock (Gate)
        {
            if (_index is not null && DateTime.UtcNow - _builtAt < Lifetime) return _index;

            var index = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
            var jarsBySize = new Dictionary<long, List<Entry>>();
            var scanned = 0;

            foreach (var root in CandidateRoots())
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                };

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*", options);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"index {root}");
                    continue;
                }

                using var enumerator = files.GetEnumerator();

                while (true)
                {
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, $"index {root}");
                        break;
                    }

                    if (++scanned > MaxFiles) break;

                    var path = enumerator.Current;
                    long length;

                    try
                    {
                        length = new FileInfo(path).Length;
                    }
                    catch
                    {
                        continue;
                    }

                    if (length <= 0) continue;

                    var name = Path.GetFileName(path);
                    if (!index.TryGetValue(name, out var entries))
                        index[name] = entries = [];

                    if (entries.Count < 4) entries.Add(new Entry(length, path));

                    if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!jarsBySize.TryGetValue(length, out var sameSize))
                            jarsBySize[length] = sameSize = [];

                        if (sameSize.Count < 4) sameSize.Add(new Entry(length, path));
                    }
                }
            }

            _index = index;
            _jarsBySize = jarsBySize;
            _builtAt = DateTime.UtcNow;
            AppLog.Info($"local file index built: {index.Count} names, {jarsBySize.Count} jar sizes, {scanned} files");

            return index;
        }
    }

    /// <summary>扫描位置：本启动器所有实例目录，外加官方启动器的 libraries / versions（如果有）。</summary>
    private static IEnumerable<string> CandidateRoots()
    {
        if (Directory.Exists(GamePaths.VersionsDir)) yield return GamePaths.VersionsDir;

        string? roaming;
        try
        {
            roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        catch
        {
            roaming = null;
        }

        if (string.IsNullOrWhiteSpace(roaming)) yield break;

        var vanilla = Path.Combine(roaming, ".minecraft");
        if (!Directory.Exists(vanilla)) yield break;

        foreach (var sub in new[] { "libraries", "versions" })
        {
            var dir = Path.Combine(vanilla, sub);
            if (Directory.Exists(dir)) yield return dir;
        }
    }
}
