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

    /// <summary>允许走 CDN 的 host（登录/账号接口不在其中）。</summary>
    public static readonly HashSet<string> CdnHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "piston-meta.mojang.com",
        "piston-data.mojang.com",
        "launcher.mojang.com",
        "libraries.minecraft.net",
        "resources.download.minecraft.net",
        "bmclapi2.bangbang93.com",
        "bmclapi.bangbang93.com",
        "cdn.modrinth.com",
        "meta.fabricmc.net",
        "maven.fabricmc.net",
        "maven.minecraftforge.net",
        "maven.neoforged.net",
        "files.minecraftforge.net",
    };

    /// <summary>账号 / 登录 / 带凭据的接口，永远不走 CDN。</summary>
    public static readonly HashSet<string> NoCdnHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "login.microsoftonline.com",
        "login.live.com",
        "user.auth.xboxlive.com",
        "xsts.auth.xboxlive.com",
        "api.minecraftservices.com",
        "api.modrinth.com",
        "sessionserver.mojang.com",
        "authserver.mojang.com",
        "textures.minecraft.net",
    };

    public static bool ShouldUseCdn(Uri uri)
    {
        var baseUrl = CustomCdnBase;
        if (baseUrl is null) return false;
        if (uri.Host.Equals(new Uri(baseUrl).Host, StringComparison.OrdinalIgnoreCase)) return false;
        if (NoCdnHosts.Contains(uri.Host)) return false;

        return CdnHosts.Contains(uri.Host);
    }

    public static Uri BuildCdnUri(Uri uri, string baseUrl)
        => new($"{baseUrl.TrimEnd('/')}/{uri.Host}{uri.AbsolutePath}{uri.Query}");

    private static string ApplyCdn(string url)
    {
        var baseUrl = CustomCdnBase;
        if (baseUrl is null || string.IsNullOrWhiteSpace(url)) return url;

        try
        {
            var uri = new Uri(url);
            return ShouldUseCdn(uri) ? BuildCdnUri(uri, baseUrl).ToString() : url;
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

    public static string ModrinthApiBase
        => ApplyCdn(UseMcim ? $"{McimBase}/modrinth/v2" : OfficialModrinthApi);

    /// <summary>Modrinth CDN 文件走 MCIM。</summary>
    public static string ModFileUrl(string url)
    {
        if (!UseMcim || string.IsNullOrWhiteSpace(url)) return url;

        if (url.Contains("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            return ApplyCdn(url.Replace("cdn.modrinth.com", "mod.mcimirror.top",
                StringComparison.OrdinalIgnoreCase));

        return ApplyCdn(url);
    }
}
