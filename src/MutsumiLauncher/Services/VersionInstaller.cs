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
        // 最多两个项目同时进行，第三个开始排队（进度里会显示排队位次）
        using var slot = await InstallGate.EnterAsync(progress, cancellationToken);

        GamePaths.EnsureCreated();

        // 每次安装重新扫一遍本地文件，才能复用刚才装好的 jar / 库
        LocalFileIndex.Invalidate();

        var name = string.IsNullOrWhiteSpace(instanceName) ? id : instanceName.Trim();
        var versionDir = Path.Combine(GamePaths.VersionsDir, name);
        Directory.CreateDirectory(versionDir);

        progress?.Report(new InstallProgress(1, $"获取 {id} 的版本信息…"));
        var json = await FetchVersionJsonAsync(id, versionUrl, cancellationToken);
        var detail = JsonSerializer.Deserialize<VersionDetail>(json)
                     ?? throw new InvalidOperationException("版本 JSON 解析失败");

        await File.WriteAllTextAsync(Path.Combine(versionDir, $"{name}.json"), json, cancellationToken);
        AppLog.Info($"install {id} -> {name}: json saved, {detail.Libraries.Count} libraries");

        var transferred = 0L;

        if (detail.Downloads.TryGetValue("client", out var client) && client.Url is not null)
        {
            var jarPath = Path.Combine(versionDir, $"{name}.jar");

            if (FileIntegrity.Matches(jarPath, client.Sha1, client.Size))
            {
                progress?.Report(new InstallProgress(6, "客户端 jar 已存在，跳过下载"));
                AppLog.Info($"install {id} -> {name}: client jar already matches");
                transferred += client.Size;
            }
            else
            {
                progress?.Report(new InstallProgress(6, "下载客户端 jar…"));
                var clientEndpoints = await DownloadSources.ClientAsync(id, client.Url!,
                    cancellationToken);
                var clientBase = transferred;
                var lastClientReport = DateTime.UtcNow;

                await DownloadAsync(clientEndpoints, jarPath,
                    client.Sha1, client.Size > 0 ? client.Size : null, cancellationToken, chunk =>
                    {
                        if ((DateTime.UtcNow - lastClientReport).TotalMilliseconds < 180) return;
                        lastClientReport = DateTime.UtcNow;

                        progress?.Report(new InstallProgress(6 + Math.Min(14, chunk / 1048576.0),
                            $"下载客户端 jar… {chunk / 1048576.0:0.0} MB", clientBase + chunk));
                    });

                transferred += client.Size;
            }
        }

        var artifacts = detail.Libraries
            .Where(IsAllowedOnWindows)
            .SelectMany(GetWindowsArtifacts)
            .Where(a => a.Url is not null && a.Path is not null)
            .GroupBy(a => a.Path!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var artifactCount = artifacts.Count;
        var completed = 0;

        var libraryTasks = artifacts.Select(async artifact =>
        {
            var target = Path.Combine(GamePaths.LibrariesDir, artifact.Path!);

            // 大小对不上或哈希不匹配就当没下载过（原版库不大，校验成本可以接受）
            if (FileIntegrity.Matches(target, artifact.Sha1, artifact.Size)) return;

            var endpoints = await DownloadSources.LibraryAsync(artifact.Url, artifact.Path,
                cancellationToken);

            // 清单里已经给了大小，直接传给下载引擎，省掉一次 HEAD 探测
            await DownloadAsync(endpoints, target, artifact.Sha1,
                artifact.Size > 0 ? artifact.Size : null, cancellationToken, null);

            var done = Interlocked.Increment(ref completed);
            progress?.Report(new InstallProgress(8 + 86.0 * done / Math.Max(1, artifactCount),
                $"下载依赖 {done}/{artifactCount}…"));
        }).ToList();

        await Task.WhenAll(libraryTasks);

        transferred += artifacts.Sum(artifact => artifact.Size);

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
        AppLog.Info($"install {id} -> {name}: done, {artifactCount} artifacts");

        var gameDir = InstanceSettings.Load(name).EffectiveIsolation ? versionDir : GamePaths.Root;
        GameOptions.EnsureLanguage(gameDir);
    }

    private static async Task<string> FetchVersionJsonAsync(string id, string officialUrl,
        CancellationToken cancellationToken)
    {
        var endpoints = await DownloadSources.VersionJsonAsync(id, officialUrl, cancellationToken);
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
                AppLog.Error(ex, $"version json {endpoint.Url}");
            }
        }

        throw new InvalidOperationException("获取版本 JSON 失败：" + last?.Message, last);
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

    private static async Task DownloadAsync(IReadOnlyList<DownloadEndpoint> endpoints, string targetPath,
        string? sha1, long? expectedSize, CancellationToken cancellationToken, Action<long>? onBytes)
    {
        var progress = onBytes is null
            ? null
            : new Progress<DownloadProgress>(p => onBytes(p.Received));

        await DownloadEngine.DownloadAsync(endpoints, targetPath, sha1, expectedSize, progress,
            cancellationToken);
    }
}
