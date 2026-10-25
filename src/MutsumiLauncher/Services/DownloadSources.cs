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
    public const string OfficialLibraries = "https://libraries.minecraft.net";

    public const string Bmclapi = "https://bmclapi2.bangbang93.com";
    public const string Mcim = "https://mod.mcimirror.top";

    public const string FabricMetaBase = "https://meta.fabricmc.net/v2";
    public const string FabricMetaMirror = "https://bmclapi2.bangbang93.com/fabric-meta/v2";

    public const string FabricMaven = "https://maven.fabricmc.net";

    public const string OfficialModrinthApi = "https://api.modrinth.com/v2";

    /// <summary>MCIM 的 Modrinth API 镜像（PCL2 同样把 api.modrinth.com 映射到这里）。</summary>
    public const string McimModrinthApi = Mcim + "/modrinth/v2";

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

    private static bool ModAuto =>
        string.IsNullOrWhiteSpace(AppConfig.Current.ModMirror)
        || AppConfig.Current.ModMirror.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static bool ModMirrorFirst =>
        AppConfig.Current.ModMirror.Equals("mcim", StringComparison.OrdinalIgnoreCase);

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

        var urls = new List<string>();

        if (ModMirrorFirst)
        {
            urls.AddRange(McimVariants(officialUrl));
            urls.AddRange(WorkerVariants(officialUrl));
            urls.AddRange(OfficialVariants(officialUrl));
        }
        else if (!ModAuto)
        {
            urls.AddRange(OfficialVariants(officialUrl));
        }
        else
        {
            // 官方直连不通是常态，自建 Worker 镜像紧跟在官方后面
            urls.AddRange(OfficialVariants(officialUrl));
            urls.AddRange(WorkerVariants(officialUrl));
            urls.AddRange(McimVariants(officialUrl));
        }

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

    private static IEnumerable<string> McimVariants(string url)
    {
        var mirror = McimFileUrl(url);
        if (!string.IsNullOrWhiteSpace(mirror)) yield return mirror;
    }

    /// <summary>Modrinth API 地址列表：auto 官方优先、mcim 镜像优先，都会互相回退。</summary>
    public static IReadOnlyList<string> ModrinthApiBases => ModMirrorFirst
        ? [McimModrinthApi, OfficialModrinthApi]
        : ModAuto
            ? [OfficialModrinthApi, McimModrinthApi]
            : [OfficialModrinthApi];

    public static string ModrinthApiBase => ModrinthApiBases[0];

    // 没有 API Key 之前，CurseForge 元数据继续走 MCIM 镜像
    public static string CurseForgeApiBase => $"{Mcim}/curseforge/v1";

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

    /// <summary>把社区下载地址换成 MCIM 镜像（不支持的地址原样返回）。</summary>
    public static string McimFileUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        var replaced = url
            .Replace("https://cdn.modrinth.com", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("http://cdn.modrinth.com", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("https://edge.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("http://edge.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("https://mediafilez.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("http://mediafilez.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("https://media.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("http://media.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase);

        if (replaced.Equals(url, StringComparison.OrdinalIgnoreCase)) return url;

        // MCIM 不认 URL 编码后的文件名（fabric-api-0.116.17%2B1.21.1.jar 这种），
        // 必须传解码后的名字（PCL2 同样处理）
        try
        {
            var uri = new Uri(replaced);
            var path = uri.AbsolutePath;
            var slash = path.LastIndexOf('/');
            if (slash < 0 || slash >= path.Length - 1) return replaced;

            var name = path[(slash + 1)..];
            var decoded = Uri.UnescapeDataString(name);

            // 解码后带空格 / 井号之类就别动了，保持编码形态
            if (decoded.Any(char.IsWhiteSpace) || decoded.Contains('#') || decoded.Contains('?'))
                return replaced;

            return uri.GetLeftPart(UriPartial.Authority) + path[..(slash + 1)] + decoded;
        }
        catch
        {
            return replaced;
        }
    }

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
