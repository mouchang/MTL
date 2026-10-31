using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MutsumiLauncher.Services;

/// <summary>
/// Mod 图标：优先从 jar 里取（Fabric/Forge 的 icon.png / logo.png），
/// 取不到再按 SHA-1 去 Modrinth 查项目图标，走 IconCache 下载（会经过内置代理）。
/// </summary>
public static class ModIconLoader
{
    private static readonly Regex AssetIcon = new(@"^assets/[^/]+/icon\.png$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(30));

    public static string CacheKeyFor(string filePath, long size)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        name = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));

        if (name.Length > 48) name = name[..48];

        return $"mod-{name}-{size}";
    }

    public static async Task<string?> LoadAsync(string jarPath, long size, string? sha1,
        CancellationToken cancellationToken = default)
    {
        var key = CacheKeyFor(jarPath, size);
        var cached = Path.Combine(IconCache.CacheDir, key + ".png");

        if (File.Exists(cached)) return cached;

        var fromJar = TryFromJar(jarPath, cached);
        if (fromJar is not null) return fromJar;

        try
        {
            sha1 ??= FileIntegrity.Sha1Of(jarPath);
            if (string.IsNullOrWhiteSpace(sha1)) return null;

            var projectId = await GetProjectIdAsync(sha1, cancellationToken);
            if (projectId is null) return null;

            var iconUrl = await GetProjectIconAsync(projectId, cancellationToken);
            if (iconUrl is null) return null;

            return await IconCache.EnsureAsync(iconUrl, key, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "mod icon");
            return null;
        }
    }

    /// <summary>
    /// 批量从 CurseForge 取图标（版本管理优先显示 CF 图标，没有才用 Modrinth）。
    /// 返回 缓存键 → 本地 png 路径。
    /// </summary>
    public static async Task<Dictionary<string, string>> LoadCurseForgeIconsAsync(
        IReadOnlyList<(string Key, string JarPath, long Size)> items,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>();
        if (items.Count == 0) return result;

        var pending = new List<(string Key, string JarPath, long Size)>();

        foreach (var item in items)
        {
            var cached = Path.Combine(IconCache.CacheDir, item.Key + ".png");

            if (File.Exists(cached)) result[item.Key] = cached;
            else pending.Add(item);
        }

        if (pending.Count == 0) return result;

        // 1. 算指纹（读整个文件，限并发）
        var fingerprints = new Dictionary<uint, string>();
        using var gate = new SemaphoreSlim(4);

        var tasks = pending.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);

            try
            {
                var fingerprint = await Task.Run(() => CurseForgeFingerprint.OfFile(item.JarPath),
                    cancellationToken);

                if (fingerprint == 0) return;

                lock (fingerprints) fingerprints[fingerprint] = item.Key;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (fingerprints.Count == 0) return result;

        // 2. 反查项目 + logo
        var icons = await CurseForgeClient.LookupIconsByFingerprintAsync(
            fingerprints.Keys.ToList(), cancellationToken);

        foreach (var (fingerprint, url) in icons)
        {
            if (!fingerprints.TryGetValue(fingerprint, out var key)) continue;

            try
            {
                var local = await IconCache.EnsureAsync(url, key, cancellationToken);
                if (local is not null) result[key] = local;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "cache curseforge icon");
            }
        }

        return result;
    }

    private static string? TryFromJar(string jarPath, string target)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            var entry = archive.Entries
                .Where(item => AssetIcon.IsMatch(item.FullName)
                               || item.FullName.Equals("icon.png", StringComparison.OrdinalIgnoreCase)
                               || item.FullName.Equals("logo.png", StringComparison.OrdinalIgnoreCase)
                               || item.FullName.Equals("pack.png", StringComparison.OrdinalIgnoreCase))
                .Where(item => item.Length > 256)
                .OrderByDescending(item => item.Name.Equals("icon.png", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(item => item.Length)
                .FirstOrDefault();

            if (entry is null) return null;

            Directory.CreateDirectory(IconCache.CacheDir);
            entry.ExtractToFile(target, overwrite: true);

            return new FileInfo(target).Length > 256 ? target : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetProjectIdAsync(string sha1, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            "https://api.modrinth.com/v2/version_file/" + sha1, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("project_id", out var id)
            ? id.GetString()
            : null;
    }

    private static async Task<string?> GetProjectIconAsync(string projectId, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            "https://api.modrinth.com/v2/project/" + projectId, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("icon_url", out var icon)
            ? icon.GetString()
            : null;
    }
}