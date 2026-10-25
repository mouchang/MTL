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

    /// <summary>少于这个条数基本可以断定不是正常的版本清单（被墙、被劫持、镜像返回错误页）。</summary>
    private const int MinimumVersionCount = 100;

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
                    var manifest = TryParse(cached);

                    if (manifest is not null) return (manifest, false);

                    AppLog.Info("cached version manifest is invalid, refetching");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "read manifest cache");
            }
        }

        try
        {
            var (json, manifest) = await FetchManifestAsync(cancellationToken);
            Directory.CreateDirectory(AppPaths.CacheDir);
            await File.WriteAllTextAsync(CachePath, json, cancellationToken);
            AppLog.Info($"version manifest fetched, {json.Length} bytes, {manifest.Versions.Count} versions");
            return (manifest, true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "fetch version manifest");

            if (File.Exists(CachePath))
            {
                var cached = await File.ReadAllTextAsync(CachePath, cancellationToken);
                var manifest = TryParse(cached);
                if (manifest is not null) return (manifest, false);
            }

            throw new InvalidOperationException(
                "获取版本清单失败，请检查网络或稍后重试（" + ex.Message + "）", ex);
        }
    }

    /// <summary>官方 / BMCLAPI 双源，按延迟自动排序；内容不合格就换下一个源。</summary>
    private static async Task<(string Json, VersionManifest Manifest)> FetchManifestAsync(
        CancellationToken cancellationToken)
    {
        var endpoints = await DownloadSources.ManifestAsync(cancellationToken);
        Exception? last = null;

        foreach (var endpoint in endpoints)
        {
            try
            {
                var json = await Http.GetStringAsync(endpoint.Url, cancellationToken);
                var manifest = ParseValidated(json, endpoint.Url);
                return (json, manifest);
            }
            catch (Exception ex)
            {
                last = ex;
                AppLog.Error(ex, $"fetch manifest {endpoint.Url}");
            }
        }

        throw new InvalidOperationException("获取版本清单失败：" + last?.Message, last);
    }

    /// <summary>解析并校验；内容不像是正常的版本清单就抛异常，让调用方换源。</summary>
    private static VersionManifest ParseValidated(string json, string source)
    {
        var manifest = JsonSerializer.Deserialize<VersionManifest>(json);

        if (manifest is null || manifest.Versions.Count < MinimumVersionCount)
            throw new InvalidDataException(
                $"版本清单内容异常（{source}，{manifest?.Versions.Count ?? 0} 条）");

        if (manifest.Versions.All(version => !version.Id.Equals(manifest.Latest.Release,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"版本清单缺少最新正式版（{source}）");

        return manifest;
    }

    private static VersionManifest? TryParse(string json)
    {
        try
        {
            return ParseValidated(json, "缓存");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "parse cached manifest");
            return null;
        }
    }
}
