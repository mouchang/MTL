using System.Diagnostics;
using System.Net.Http;

namespace MutsumiLauncher.Services;

/// <summary>一个下载源：URL、超时、是否允许分段。</summary>
public sealed record DownloadEndpoint(string Url, int TimeoutSeconds = 30, bool AllowSplit = true);

/// <summary>
/// 参考 PCL2 的下载源选择：
/// - 原版：官方 Mojang / BMCLAPI 自动择优；
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
    private static Task<bool>? _probe;

    /// <summary>官方 Mojang 是否足够快（PCL2 的 DlPreferMojang）。</summary>
    public static bool PreferMojang { get; private set; }

    private static bool VanillaAuto =>
        string.IsNullOrWhiteSpace(AppConfig.Current.VanillaMirror)
        || AppConfig.Current.VanillaMirror.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static bool VanillaOfficial =>
        AppConfig.Current.VanillaMirror.Equals("official", StringComparison.OrdinalIgnoreCase);

    private static bool ModAuto =>
        string.IsNullOrWhiteSpace(AppConfig.Current.ModMirror)
        || AppConfig.Current.ModMirror.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static bool ModMirrorFirst =>
        AppConfig.Current.ModMirror.Equals("mcim", StringComparison.OrdinalIgnoreCase);

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
            new DownloadEndpoint($"{Bmclapi}/mc/game/version_manifest_v2.json", 15));
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> VersionJsonAsync(string id, string officialUrl,
        CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);
        return Order(
            new DownloadEndpoint(officialUrl, 20),
            new DownloadEndpoint($"{Bmclapi}/version/{id}/json", 20));
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> ClientAsync(string id, string officialUrl,
        CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);
        return Order(
            new DownloadEndpoint(officialUrl),
            new DownloadEndpoint($"{Bmclapi}/version/{id}/client"));
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> LibraryAsync(string? officialUrl,
        string? path, CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(officialUrl))
            return [];

        var mirror = officialUrl.Contains("libraries.minecraft.net", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(path)
            ? $"{Bmclapi}/maven/{path.Replace('\\', '/')}"
            : null;

        return mirror is null
            ? [new DownloadEndpoint(officialUrl)]
            : Order(new DownloadEndpoint(officialUrl), new DownloadEndpoint(mirror));
    }

    public static async Task<IReadOnlyList<DownloadEndpoint>> AssetAsync(string hash,
        CancellationToken cancellationToken = default)
    {
        await EnsureProbeAsync(cancellationToken);

        var relative = $"{hash[..2]}/{hash}";
        return Order(
            new DownloadEndpoint($"{OfficialResources}/{relative}"),
            new DownloadEndpoint($"{Bmclapi}/assets/{relative}"));
    }

    public static IReadOnlyList<DownloadEndpoint> ModFile(string officialUrl)
    {
        var mirror = McimFileUrl(officialUrl);

        if (string.IsNullOrWhiteSpace(mirror)
            || mirror.Equals(officialUrl, StringComparison.OrdinalIgnoreCase))
            return [new DownloadEndpoint(officialUrl)];

        return ModMirrorFirst
            ? [new DownloadEndpoint(mirror), new DownloadEndpoint(officialUrl)]
            : [new DownloadEndpoint(officialUrl), new DownloadEndpoint(mirror)];
    }

    public static string ModrinthApiBase => ModMirrorFirst && !ModAuto
        ? $"{Mcim}/modrinth/v2"
        : OfficialModrinthApi;

    // 没有 API Key 之前，CurseForge 元数据继续走 MCIM 镜像
    public static string CurseForgeApiBase => $"{Mcim}/curseforge/v1";

    public static IReadOnlyList<DownloadEndpoint> ForgeInstaller(string gameVersion, string loaderVersion)
    {
        var official =
            $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{gameVersion}-{loaderVersion}/forge-{gameVersion}-{loaderVersion}-installer.jar";
        var mirror =
            $"{Bmclapi}/forge/download?mcversion={gameVersion}&version={loaderVersion}&category=installer";

        return Order(new DownloadEndpoint(official, 30), new DownloadEndpoint(mirror, 30));
    }

    public static IReadOnlyList<DownloadEndpoint> FabricMetaEndpoints()
        => Order(new DownloadEndpoint(FabricMetaBase, 15),
            new DownloadEndpoint(FabricMetaMirror, 15));

    public static string McimFileUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        return url
            .Replace("cdn.modrinth.com", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("edge.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("mediafilez.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase)
            .Replace("media.forgecdn.net", Mcim, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DownloadEndpoint> Order(DownloadEndpoint official, DownloadEndpoint mirror)
    {
        if (VanillaOfficial) return [official];
        if (!VanillaAuto) return [mirror, official];
        return PreferMojang ? [official, mirror] : [mirror, official];
    }
}
