using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

public sealed class VersionManifest
{
    [JsonPropertyName("latest")]
    public LatestVersions Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<MinecraftVersion> Versions { get; set; } = [];
}

public sealed class LatestVersions
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = string.Empty;

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = string.Empty;
}

public sealed class MinecraftVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset ReleaseTime { get; set; }
}

/// <summary>Mojang 版本清单：网络优先，失败时回退到本地缓存。</summary>
public sealed class VersionManifestService
{
    private const string OfficialManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromSeconds(20));

    public static string CachePath => Path.Combine(AppPaths.CacheDir, "version_manifest_v2.json");

    /// <summary>取版本清单；返回值表示是否来自网络。</summary>
    public async Task<(VersionManifest Manifest, bool FromNetwork)> GetAsync(
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && File.Exists(CachePath))
        {
            try
            {
                var age = DateTime.Now - File.GetLastWriteTime(CachePath);
                if (age < TimeSpan.FromHours(6))
                {
                    var cached = await File.ReadAllTextAsync(CachePath, cancellationToken);
                    return (Parse(cached), false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "read manifest cache");
            }
        }

        try
        {
            var json = await FetchManifestAsync(cancellationToken);
            Directory.CreateDirectory(AppPaths.CacheDir);
            await File.WriteAllTextAsync(CachePath, json, cancellationToken);
            AppLog.Info($"version manifest fetched, {json.Length} bytes");
            return (Parse(json), true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "fetch version manifest");

            if (File.Exists(CachePath))
            {
                var cached = await File.ReadAllTextAsync(CachePath, cancellationToken);
                return (Parse(cached), false);
            }

            throw;
        }
    }

    /// <summary>官方 / BMCLAPI 双源，按延迟自动排序。</summary>
    private static async Task<string> FetchManifestAsync(CancellationToken cancellationToken)
    {
        var endpoints = await DownloadSources.ManifestAsync(cancellationToken);
        Exception? last = null;

        foreach (var endpoint in endpoints)
        {
            try
            {
                return await Http.GetStringAsync(endpoint.Url, cancellationToken);
            }
            catch (Exception ex)
            {
                last = ex;
                AppLog.Error(ex, $"fetch manifest {endpoint.Url}");
            }
        }

        throw new InvalidOperationException("获取版本清单失败：" + last?.Message, last);
    }

    private static VersionManifest Parse(string json)
        => JsonSerializer.Deserialize<VersionManifest>(json) ?? new VersionManifest();
}
