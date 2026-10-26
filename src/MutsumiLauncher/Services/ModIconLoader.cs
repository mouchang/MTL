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
