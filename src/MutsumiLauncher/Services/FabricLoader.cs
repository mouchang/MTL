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

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

/// <summary>Fabric Loader 安装：取 meta 的 profile JSON + 下载 Fabric 依赖库。</summary>
public sealed class FabricLoader
{
    private const string MetaBase = "https://meta.fabricmc.net/v2";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromMinutes(5));

    public static async Task<IReadOnlyList<FabricLoaderEntry>> GetLoadersAsync(string gameVersion,
        CancellationToken cancellationToken = default)
    {
        var json = await Http.GetStringAsync($"{MetaBase}/versions/loader/{gameVersion}", cancellationToken);
        return JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json) ?? [];
    }

    public static async Task<string> GetProfileJsonAsync(string gameVersion, string loaderVersion,
        CancellationToken cancellationToken = default)
        => await Http.GetStringAsync(
            $"{MetaBase}/versions/loader/{gameVersion}/{loaderVersion}/profile/json", cancellationToken);

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
        var index = 0;
        var transferred = 0L;
        foreach (var library in libraries)
        {
            index++;
            cancellationToken.ThrowIfCancellationRequested();

            var path = MavenPath(library.Name);
            if (path is null) continue;

            var target = Path.Combine(GamePaths.LibrariesDir, path);
            if (File.Exists(target)) continue;

            var baseUrl = string.IsNullOrWhiteSpace(library.Url)
                ? "https://maven.fabricmc.net/"
                : library.Url!;

            var url = baseUrl.TrimEnd('/') + "/" + path.Replace('\\', '/');

            progress?.Report(new InstallProgress(
                10 + 80.0 * index / Math.Max(1, libraries.Count),
                $"下载 Fabric 依赖 {index}/{libraries.Count}…",
                transferred));

            transferred += await DownloadAsync(url, target, cancellationToken);
        }

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

    private static async Task<long> DownloadAsync(string url, string target,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(target);
        if (directory is not null) Directory.CreateDirectory(directory);

        var temp = target + ".tmp";
        long written = 0;

        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"下载失败：{url}（{(int)response.StatusCode}）");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(temp);
            await source.CopyToAsync(file, cancellationToken);
            written = file.Length;
        }

        File.Move(temp, target, overwrite: true);
        return written;
    }
}
