using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

public sealed class FabricLoaderEntry
{
    [JsonPropertyName("loader")]
    public FabricLoaderInfo Loader { get; set; } = new();
}

public sealed class FabricLoaderInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;


}

/// <summary>Fabric Loader 安装：取 meta 的 profile JSON + 下载 Fabric 依赖库。</summary>
public sealed class FabricLoader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromMinutes(5));

    public static async Task<IReadOnlyList<FabricLoaderEntry>> GetLoadersAsync(string gameVersion,
        CancellationToken cancellationToken = default)
    {
        var json = await GetMetaAsync($"versions/loader/{gameVersion}", cancellationToken);
        return JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json) ?? [];
    }

    public static async Task<string> GetProfileJsonAsync(string gameVersion, string loaderVersion,
        CancellationToken cancellationToken = default)
        => await GetMetaAsync($"versions/loader/{gameVersion}/{loaderVersion}/profile/json",
            cancellationToken);

    /// <summary>Fabric meta 官方源失败时切 BMCLAPI。</summary>
    private static async Task<string> GetMetaAsync(string relative, CancellationToken cancellationToken)
    {
        Exception? last = null;

        foreach (var endpoint in DownloadSources.FabricMetaEndpoints())
        {
            try
            {
                return await Http.GetStringAsync(endpoint.Url.TrimEnd('/') + "/" + relative,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                last = ex;
                AppLog.Error(ex, $"fabric meta {endpoint.Url}");
            }
        }

        throw new InvalidOperationException("获取 Fabric 信息失败：" + last?.Message, last);
    }

    /// <summary>安装 Fabric 版本，返回版本 ID。基础原版必须已安装。</summary>
    public async Task<string> InstallAsync(string gameVersion, string loaderVersion,
        IProgress<InstallProgress>? progress = null, string? instanceName = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new InstallProgress(5, "获取 Fabric 配置…"));
        var json = await GetProfileJsonAsync(gameVersion, loaderVersion, cancellationToken);

        var id = string.IsNullOrWhiteSpace(instanceName)
            ? $"fabric-loader-{loaderVersion}-{gameVersion}"
            : instanceName.Trim();
        var versionDir = Path.Combine(GamePaths.VersionsDir, id);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, $"{id}.json"), json, cancellationToken);

        var detail = JsonSerializer.Deserialize<VersionDetail>(json)
                     ?? throw new InvalidOperationException("Fabric 配置解析失败");

        var libraries = detail.Libraries;
        var jobs = new List<(IReadOnlyList<DownloadEndpoint> Endpoints, string Target)>();

        foreach (var library in libraries)
        {
            var path = MavenPath(library.Name);
            if (path is null) continue;

            var target = Path.Combine(GamePaths.LibrariesDir, path);
            if (File.Exists(target)) continue;

            var baseUrl = string.IsNullOrWhiteSpace(library.Url)
                ? "https://maven.fabricmc.net/"
                : library.Url!;

            var officialUrl = baseUrl.TrimEnd('/') + "/" + path.Replace('\\', '/');
            jobs.Add((await DownloadSources.LibraryAsync(officialUrl, path, cancellationToken), target));
        }

        var completed = 0;

        await Task.WhenAll(jobs.Select(async job =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.Target)!);
            await DownloadEngine.DownloadAsync(job.Endpoints, job.Target, null, null, null,
                cancellationToken);

            var done = Interlocked.Increment(ref completed);
            progress?.Report(new InstallProgress(10 + 80.0 * done / Math.Max(1, jobs.Count),
                $"下载 Fabric 依赖 {done}/{jobs.Count}…"));
        }));

        progress?.Report(new InstallProgress(100, "Fabric 安装完成"));
        AppLog.Info($"fabric installed: {id}");
        return id;
    }

    private static string? MavenPath(string coordinates)
    {
        var parts = coordinates.Split(':');
        if (parts.Length < 3) return null;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }

}
