namespace MutsumiLauncher.Services;

/// <summary>下载源：原版走 BMCLAPI，社区走 MCIM（可在配置里切回官方）。</summary>
public static class DownloadMirror
{
    public const string BmclapiBase = "https://bmclapi2.bangbang93.com";

    public const string McimBase = "https://mod.mcimirror.top";

    public const string OfficialManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public const string OfficialModrinthApi = "https://api.modrinth.com/v2";

    public static bool UseBmclapi =>
        !AppConfig.Current.VanillaMirror.Equals("official", StringComparison.OrdinalIgnoreCase);

    /// <summary>自定义 CDN 前缀（例如自建 Cloudflare Worker），为空则不启用。</summary>
    public static string? CustomCdnBase
    {
        get
        {
            var value = AppConfig.Current.VanillaCdnBase;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
        }
    }

    private static string ApplyCdn(string url)
    {
        var baseUrl = CustomCdnBase;
        if (baseUrl is null || string.IsNullOrWhiteSpace(url)) return url;

        try
        {
            var uri = new Uri(url);
            return $"{baseUrl}/{uri.Host}{uri.AbsolutePath}{uri.Query}";
        }
        catch
        {
            return url;
        }
    }

    public static bool UseMcim =>
        !AppConfig.Current.ModMirror.Equals("official", StringComparison.OrdinalIgnoreCase);

    public static string ManifestUrl => ApplyCdn(UseBmclapi
        ? $"{BmclapiBase}/mc/game/version_manifest_v2.json"
        : OfficialManifest);

    public static string VersionJsonUrl(string id, string officialUrl) =>
        ApplyCdn(UseBmclapi ? $"{BmclapiBase}/version/{id}/json" : officialUrl);

    public static string ClientUrl(string id, string officialUrl) =>
        ApplyCdn(UseBmclapi ? $"{BmclapiBase}/version/{id}/client" : officialUrl);

    /// <summary>libraries.minecraft.net 的资源走 BMCLAPI 的 maven 路由。</summary>
    public static string LibraryUrl(string? officialUrl, string? path)
    {
        if (!UseBmclapi || string.IsNullOrWhiteSpace(officialUrl) || string.IsNullOrWhiteSpace(path))
            return officialUrl ?? string.Empty;

        if (officialUrl.Contains("libraries.minecraft.net", StringComparison.OrdinalIgnoreCase))
            return $"{BmclapiBase}/maven/{path.Replace('\\', '/')}";

        return officialUrl;
    }

    public static string AssetObjectUrl(string hash) => ApplyCdn(UseBmclapi
        ? $"{BmclapiBase}/assets/{hash[..2]}/{hash}"
        : $"https://resources.download.minecraft.net/{hash[..2]}/{hash}");

    public static string ModrinthApiBase => UseMcim ? $"{McimBase}/modrinth/v2" : OfficialModrinthApi;

    /// <summary>Modrinth CDN 文件走 MCIM。</summary>
    public static string ModFileUrl(string url)
    {
        if (!UseMcim || string.IsNullOrWhiteSpace(url)) return url;

        if (url.Contains("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("cdn.modrinth.com", "mod.mcimirror.top", StringComparison.OrdinalIgnoreCase);

        return url;
    }
}
