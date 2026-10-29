using System.Diagnostics;
using System.Net.Http;

namespace MutsumiLauncher.Services;

/// <summary>一个下载源：URL、超时、是否允许分段。</summary>
public sealed record DownloadEndpoint(string Url, int TimeoutSeconds = 30, bool AllowSplit = true);

/// <summary>
/// 参考 PCL2 的下载源选择：
/// - 原版：官方 Mojang / BMCLAPI 自动择优，版本列表与文件下载分别有自己的偏好；
/// - 社区：官方 Modrinth / CurseForge 优先，MCIM 兜底；
/// - 每种资源返回有序的源列表，由下载引擎依次尝试。
/// </summary>
public static class DownloadSources
{
    public const string OfficialManifest =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public const string OfficialResources = "https://resources.download.minecraft.net";


    public const string Bmclapi = "https://bmclapi2.bangbang93.com";
    public const string Mcim = "https://mod.mcimirror.top";

    public const string FabricMetaBase = "https://meta.fabricmc.net/v2";
    public const string FabricMetaMirror = "https://bmclapi2.bangbang93.com/fabric-meta/v2";



    public const string OfficialModrinthApi = "https://api.modrinth.com/v2";

    /// <summary>自建 Cloudflare Worker：墙外回源 + 边缘缓存，官方 CDN 直连不通时兜底。</summary>
    public const string WorkerProxy = "https://mtl.dpdns.org";

    /// <summary>
    /// Worker 能反代的社区 CDN 域名。
    /// 目前只放行了 cdn.modrinth.com（独立 Worker 路由）—— forgecdn 要等
    /// `mtl-neoforge` 的白名单加上对应域名后再开，否则每个 CurseForge 文件都会白拿一个 403。
    /// </summary>
    private static readonly string[] WorkerMirroredHosts =
    [
        "cdn.modrinth.com",
    ];

    private static Task<bool>? _probe;

    /// <summary>官方 Mojang 是否足够快（PCL2 的 DlPreferMojang）。</summary>
    public static bool PreferMojang { get; private set; }

    /// <summary>文件下载源偏好：auto / official / mirror。</summary>
    private static string FileMode =>
        Normalize(AppConfig.Current.VanillaMirror);

    /// <summary>版本清单源偏好：auto / official / mirror（与原文件下载分开设置）。</summary>
    private static string ListMode =>
        Normalize(AppConfig.Current.VanillaListMirror);

    private static string Normalize(string? mode)
    {
        var value = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return value is "official" or "mirror" ? value : "auto";
    }

    /// <summary>探测官方源延迟，结果缓存到本次运行。</summary>
    public static async Task EnsureProbeAsync(CancellationToken cancellationToken = default)
    {
        _probe ??= ProbeAsync(cancellationToken);

        try
        {
            PreferMojang = await _probe;
        }
        catch
        {
            PreferMojang = false;
        }
    }

    private static async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = HttpUtil.Create(TimeSpan.FromSeconds(4));
            var watch = Stopwatch.StartNew();

            using var response = await http.GetAsync(OfficialManifest,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode) return false;

            var elapsed = watch.ElapsedMilliseconds;
            AppLog.Info($"mojang manifest latency: {elapsed}ms");
            return elapsed < 4000;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "probe mojang source");
            return false;
        }
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> ManifestAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);

        return Order(
            new DownloadEndpoint(OfficialManifest, 15),
            new DownloadEndpoint($"{Bmclapi}/mc/game/version_manifest_v2.json", 15),
            ListMode);
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> VersionJsonAsync(string id, string officialUrl,
        CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);
        return Order(
            new DownloadEndpoint(officialUrl, 20),
            new DownloadEndpoint($"{Bmclapi}/version/{id}/json", 20),
            ListMode);
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> ClientAsync(string id, string officialUrl,
        CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);
        return Order(
            new DownloadEndpoint(officialUrl),
            new DownloadEndpoint($"{Bmclapi}/version/{id}/client"),
            FileMode);
    }

    /// <summary>
    /// 依赖库下载源。libraries.minecraft.net 与 Forge / Fabric / NeoForge 的 maven
    /// 都能在 BMCLAPI 找到镜像（/maven 与 /libraries 两条路径），加载器自身的库只走镜像优先。
    /// </summary>
    public static async Task<IReadOnlyList<DownloadEndpoint>> LibraryAsync(string? officialUrl,
        string? path, CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(officialUrl))
            return [];

        var official = new DownloadEndpoint(officialUrl);
        var host = HostOf(officialUrl);
        var mavenPath = NormalizeMavenPath(path, officialUrl, host);

        if (mavenPath is null || !IsMirroredMavenHost(host))
            return [official];

        var mirrors = new List<DownloadEndpoint>
        {
            new($"{Bmclapi}/maven/{mavenPath}"),
            new($"{Bmclapi}/libraries/{mavenPath}"),
        };

        // 加载器 maven 在国内基本连不上官方，镜像优先；用户显式选官方时除外
        if (IsLoaderMavenHost(host))
            return FileMode == "official" ? [official, .. mirrors] : [.. mirrors, official];

        if (FileMode == "official" || (FileMode == "auto" && PreferMojang))
            return [official, .. mirrors];

        return [.. mirrors, official];
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> AssetAsync(string hash,
        CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);

        var relative = $"{hash[..2]}/{hash}";
        return Order(
            new DownloadEndpoint($"{OfficialResources}/{relative}"),
            new DownloadEndpoint($"{Bmclapi}/assets/{relative}"),
            FileMode);
    }

    /// <summary>
    /// 社区文件下载源：官方 CDN 的各种变体 + MCIM 镜像。
    /// PCL2 会为 CurseForge 地址生成 edge / mediafilez / media / overwolf 多个变体，
    /// 部分地区只有其中某个能通。
    /// </summary>
    public static IReadOnlyList<DownloadEndpoint> ModFile(string officialUrl)
    {
        if (string.IsNullOrWhiteSpace(officialUrl)) return [];

        // 文件不走 MCIM：它只是个 302 跳转器，还会给半截文件和错文件名。
        // 现在只走 官方 CDN + 自建 Worker 镜像（墙外回源 + 边缘缓存）。
        var urls = new List<string>();
        urls.AddRange(OfficialVariants(officialUrl));
        urls.AddRange(WorkerVariants(officialUrl));

        return urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(url => new DownloadEndpoint(url))
            .ToList();
    }

    /// <summary>官方 CDN 地址的多个等价变体（CurseForge 的 edge / mediafilez / media / overwolf）。</summary>
    private static IEnumerable<string> OfficialVariants(string url)
    {
        if (!url.Contains("forgecdn", StringComparison.OrdinalIgnoreCase)
            && !url.Contains("overwolf.wtf", StringComparison.OrdinalIgnoreCase))
        {
            yield return url;
            yield break;
        }

        yield return url
            .Replace("-service.overwolf.wtf", ".forgecdn.net", StringComparison.OrdinalIgnoreCase)
            .Replace("://edge.", "://mediafilez.", StringComparison.OrdinalIgnoreCase)
            .Replace("://media.", "://mediafilez.", StringComparison.OrdinalIgnoreCase);

        yield return url
            .Replace("://edge.", "://mediafilez.", StringComparison.OrdinalIgnoreCase)
            .Replace("://media.", "://mediafilez.", StringComparison.OrdinalIgnoreCase);

        yield return url.Replace("-service.overwolf.wtf", ".forgecdn.net", StringComparison.OrdinalIgnoreCase);
        yield return url.Replace("://media.", "://edge.", StringComparison.OrdinalIgnoreCase);
        yield return url;
    }

    /// <summary>把官方 CDN 地址换成自建 Worker 的镜像地址（mtl.dpdns.org/&lt;host&gt;/&lt;path&gt;）。</summary>
    private static IEnumerable<string> WorkerVariants(string url)
    {
        foreach (var host in WorkerMirroredHosts)
        {
            var prefix = $"https://{host}/";
            if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            yield return $"{WorkerProxy}/{host}/{url[prefix.Length..]}";
            yield break;
        }
    }

    /// <summary>Modrinth API 基址：自家的 Worker 反代在前，官方直连兜底。</summary>
    public static IReadOnlyList<string> ModrinthApiBases
    {
        get
        {
            var proxy = AppConfig.Current.ModrinthProxy;

            if (string.IsNullOrWhiteSpace(proxy)) return [OfficialModrinthApi];

            var normalized = proxy.Trim().TrimEnd('/');
            if (normalized.EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^3];

            return [normalized + "/v2", OfficialModrinthApi];
        }
    }

    // 注意：这不是文件下载源。没有 CurseForge API Key 之前，
    // 搜索 / 项目详情这类「元数据」只能走 MCIM 的 CurseForge 接口（返回的是 JSON，量很小）。


    public static IReadOnlyList<DownloadEndpoint> ForgeInstaller(string gameVersion, string loaderVersion)
    {
        var official =
            $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{gameVersion}-{loaderVersion}/forge-{gameVersion}-{loaderVersion}-installer.jar";
        var mirror =
            $"{Bmclapi}/forge/download?mcversion={gameVersion}&version={loaderVersion}&category=installer";

        return Order(new DownloadEndpoint(official, 30), new DownloadEndpoint(mirror, 30), FileMode);
    }

    public static IReadOnlyList<DownloadEndpoint> FabricMetaEndpoints()
        => Order(new DownloadEndpoint(FabricMetaBase, 15),
            new DownloadEndpoint(FabricMetaMirror, 15), FileMode);

    private static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

    private static bool IsLoaderMavenHost(string host) => host is
        "maven.minecraftforge.net" or "files.minecraftforge.net" or
        "maven.fabricmc.net" or "maven.neoforged.net";

    private static bool IsMirroredMavenHost(string host) => host is
        "libraries.minecraft.net" or "maven.minecraftforge.net" or "files.minecraftforge.net" or
        "maven.fabricmc.net" or "maven.neoforged.net";

    /// <summary>把各种 maven 地址规整成 BMCLAPI 的路径形式（去掉 maven/、releases/ 前缀）。</summary>
    private static string? NormalizeMavenPath(string? path, string officialUrl, string host)
    {
        var value = path;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (!Uri.TryCreate(officialUrl, UriKind.Absolute, out var uri)) return null;
            value = uri.AbsolutePath.TrimStart('/');
        }

        value = value!.Replace('\\', '/').TrimStart('/');

        if (value.StartsWith("maven/", StringComparison.OrdinalIgnoreCase))
            value = value["maven/".Length..];

        if (value.StartsWith("releases/", StringComparison.OrdinalIgnoreCase))
            value = value["releases/".Length..];

        if (value.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase))
            value = value["libraries/".Length..];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyList<DownloadEndpoint> Order(DownloadEndpoint official,
        DownloadEndpoint mirror, string mode)
    {
        if (mode == "official") return [official];
        if (mode == "mirror") return [mirror, official];

        return PreferMojang ? [official, mirror] : [mirror, official];
    }
}
