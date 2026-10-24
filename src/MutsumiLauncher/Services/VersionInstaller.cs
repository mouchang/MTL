using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

public sealed record InstallProgress(double Percent, string Message, long Bytes = 0);

public sealed class VersionDetail
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("mainClass")] public string MainClass { get; set; } = string.Empty;

    [JsonPropertyName("assets")] public string Assets { get; set; } = string.Empty;

    [JsonPropertyName("assetIndex")] public AssetIndex? AssetIndex { get; set; }

    [JsonPropertyName("downloads")] public Dictionary<string, Artifact> Downloads { get; set; } = [];

    [JsonPropertyName("libraries")] public List<Library> Libraries { get; set; } = [];

    [JsonPropertyName("arguments")] public JsonElement? Arguments { get; set; }

    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; set; }

    [JsonPropertyName("javaVersion")] public JavaVersionInfo? JavaVersion { get; set; }

    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; set; }
}

public sealed class JavaVersionInfo
{
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; }
}

public sealed class AssetIndex
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
}

public sealed class Library
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")] public string? Url { get; set; }

    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("rules")] public List<Rule>? Rules { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")] public Artifact? Artifact { get; set; }

    [JsonPropertyName("classifiers")] public Dictionary<string, Artifact>? Classifiers { get; set; }
}

public sealed class Artifact
{
    [JsonPropertyName("path")] public string? Path { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }

    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }

    [JsonPropertyName("size")] public long Size { get; set; }
}

public sealed class Rule
{
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;

    [JsonPropertyName("os")] public OsRule? Os { get; set; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>下载并安装一个 Minecraft 版本：版本 JSON、客户端 jar、依赖库（含 natives）。</summary>
public sealed class VersionInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromMinutes(5));

    public async Task InstallAsync(string id, string versionUrl,
        IProgress<InstallProgress>? progress = null, bool installAssets = true,
        string? instanceName = null, CancellationToken cancellationToken = default)
    {
        GamePaths.EnsureCreated();

        var name = string.IsNullOrWhiteSpace(instanceName) ? id : instanceName.Trim();
        var versionDir = Path.Combine(GamePaths.VersionsDir, name);
        Directory.CreateDirectory(versionDir);

        progress?.Report(new InstallProgress(1, $"获取 {id} 的版本信息…"));
        var json = await Http.GetStringAsync(
            DownloadMirror.VersionJsonUrl(id, versionUrl), cancellationToken);
        var detail = JsonSerializer.Deserialize<VersionDetail>(json)
                     ?? throw new InvalidOperationException("版本 JSON 解析失败");

        await File.WriteAllTextAsync(Path.Combine(versionDir, $"{name}.json"), json, cancellationToken);
        AppLog.Info($"install {id} -> {name}: json saved, {detail.Libraries.Count} libraries");

        var transferred = 0L;

        if (detail.Downloads.TryGetValue("client", out var client) && client.Url is not null)
        {
            progress?.Report(new InstallProgress(6, "下载客户端 jar…"));
            var clientUrl = DownloadMirror.ClientUrl(id, client.Url);
            var clientBase = transferred;
            var lastClientReport = DateTime.UtcNow;

            await DownloadAsync(clientUrl, Path.Combine(versionDir, $"{name}.jar"), client.Sha1,
                cancellationToken, chunk =>
                {
                    if ((DateTime.UtcNow - lastClientReport).TotalMilliseconds < 180) return;
                    lastClientReport = DateTime.UtcNow;

                    progress?.Report(new InstallProgress(6 + Math.Min(14, chunk / 1048576.0),
                        $"下载客户端 jar… {chunk / 1048576.0:0.0} MB", clientBase + chunk));
                });

            transferred += client.Size;
        }

        var artifacts = detail.Libraries
            .Where(IsAllowedOnWindows)
            .SelectMany(GetWindowsArtifacts)
            .Where(a => a.Url is not null && a.Path is not null)
            .GroupBy(a => a.Path!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var index = 0;
        foreach (var artifact in artifacts)
        {
            index++;
            cancellationToken.ThrowIfCancellationRequested();

            var percent = 8 + 86.0 * index / Math.Max(1, artifacts.Count);
            progress?.Report(new InstallProgress(percent, $"下载依赖 {index}/{artifacts.Count}…",
                transferred));

            var target = Path.Combine(GamePaths.LibrariesDir, artifact.Path!);
            if (File.Exists(target) && new FileInfo(target).Length > 0) continue;

            var libraryUrl = DownloadMirror.LibraryUrl(artifact.Url, artifact.Path);
            var libraryBase = transferred;
            var lastLibraryReport = DateTime.UtcNow;
            var libraryPercent = percent;

            await DownloadAsync(libraryUrl, target, artifact.Sha1, cancellationToken, chunk =>
            {
                if ((DateTime.UtcNow - lastLibraryReport).TotalMilliseconds < 180) return;
                lastLibraryReport = DateTime.UtcNow;

                progress?.Report(new InstallProgress(libraryPercent,
                    $"下载依赖 {index}/{artifacts.Count}…", libraryBase + chunk));
            });

            transferred += artifact.Size;
        }

        if (installAssets && detail.AssetIndex is { Url.Length: > 0 } assetIndex)
        {
            var mapped = progress is null
                ? null
                : new Progress<InstallProgress>(p =>
                    progress.Report(new InstallProgress(88 + p.Percent * 0.11, p.Message,
                        transferred + p.Bytes)));

            await AssetInstaller.EnsureAsync(assetIndex, mapped, cancellationToken);
        }

        progress?.Report(new InstallProgress(100, "安装完成"));
        AppLog.Info($"install {id} -> {name}: done, {index} artifacts");

        var gameDir = InstanceSettings.Load(name).EffectiveIsolation ? versionDir : GamePaths.Root;
        GameOptions.EnsureLanguage(gameDir);
    }

    private static IEnumerable<Artifact> GetWindowsArtifacts(Library library)
    {
        var downloads = library.Downloads;
        if (downloads is null) yield break;

        if (downloads.Artifact is { } artifact) yield return artifact;

        if (downloads.Classifiers is not null)
        {
            foreach (var (key, value) in downloads.Classifiers)
            {
                if (key.Contains("natives-windows", StringComparison.OrdinalIgnoreCase))
                    yield return value;
            }
        }
    }

    private static bool IsAllowedOnWindows(Library library)
    {
        if (library.Rules is null || library.Rules.Count == 0) return true;

        var allowed = false;
        foreach (var rule in library.Rules)
        {
            var applies = rule.Os is null
                          || string.IsNullOrEmpty(rule.Os.Name)
                          || rule.Os.Name.Equals("windows", StringComparison.OrdinalIgnoreCase);

            if (!applies) continue;
            allowed = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static async Task DownloadAsync(string url, string targetPath, string? sha1,
        CancellationToken cancellationToken, Action<long>? onBytes = null)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (directory is not null) Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (existing > 0)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

                using var response = await Http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                var resumed = existing > 0
                              && response.StatusCode == System.Net.HttpStatusCode.PartialContent;

                if (!resumed) existing = 0;
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(tempPath,
                    resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None,
                    81920, useAsync: true);

                var buffer = new byte[81920];
                var total = existing;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    onBytes?.Invoke(total);
                }
            }
            catch (Exception ex) when (attempt == 0)
            {
                AppLog.Error(ex, $"download {url}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(sha1)) break;

            var bytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
            var actual = Convert.ToHexString(SHA1.HashData(bytes));

            if (actual.Equals(sha1, StringComparison.OrdinalIgnoreCase)) break;

            AppLog.Info($"checksum mismatch, retrying full download: {Path.GetFileName(targetPath)}");
            File.Delete(tempPath);
        }

        File.Move(tempPath, targetPath, overwrite: true);
    }

}
