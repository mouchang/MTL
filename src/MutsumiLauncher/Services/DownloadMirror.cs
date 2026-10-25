namespace MutsumiLauncher.Services;

/// <summary>下载源：原版走 MC 光速镜像，社区走 MCIM（可在配置里切回官方）。</summary>
public static class DownloadMirror
{
    public const string MirrorBase = "https://bsmirror.hkmc.online";

    public const string McimBase = "https://mod.mcimirror.top";

    public const string OfficialManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public const string OfficialModrinthApi = "https://api.modrinth.com/v2";

    public static bool UseMirror =>
        !AppConfig.Current.VanillaMirror.Equals("official", StringComparison.OrdinalIgnoreCase);

    public static bool UseMcim =>
        !AppConfig.Current.ModMirror.Equals("official", StringComparison.OrdinalIgnoreCase);

    public static string ManifestUrl => UseMirror
        ? $"{MirrorBase}/mc/game/version_manifest_v2.json"
        : OfficialManifest;

    public static string VersionJsonUrl(string id, string officialUrl) =>
        UseMirror ? $"{MirrorBase}/version/{id}/json" : officialUrl;

    public static string ClientUrl(string id, string officialUrl) =>
        UseMirror ? $"{MirrorBase}/version/{id}/client" : officialUrl;

    /// <summary>libraries.minecraft.net 的资源走光速镜像的 maven 路由。</summary>
    public static string LibraryUrl(string? officialUrl, string? path)
    {
        if (!UseMirror || string.IsNullOrWhiteSpace(officialUrl) || string.IsNullOrWhiteSpace(path))
            return officialUrl ?? string.Empty;

        if (officialUrl.Contains("libraries.minecraft.net", StringComparison.OrdinalIgnoreCase))
            return $"{MirrorBase}/maven/{path.Replace('\\', '/')}";

        return officialUrl;
    }

    public static string AssetObjectUrl(string hash) => UseMirror
        ? $"{MirrorBase}/assets/{hash[..2]}/{hash}"
        : $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";

    public static string ModrinthApiBase => UseMcim ? $"{McimBase}/modrinth/v2" : OfficialModrinthApi;

    /// <summary>CurseForge：先统一走 MCIM 镜像，官方 API 需要 Key，后续再切。</summary>
    public static string CurseForgeApiBase => UseMcim
        ? $"{McimBase}/curseforge/v1"
        : "https://api.curseforge.com/v1";

    public static bool CurseForgeViaMirror => UseMcim;

    /// <summary>Modrinth CDN 文件走 MCIM。</summary>
    public static string ModFileUrl(string url)
    {
        if (!UseMcim || string.IsNullOrWhiteSpace(url)) return url;

        if (url.Contains("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("cdn.modrinth.com", "mod.mcimirror.top", StringComparison.OrdinalIgnoreCase);

        return url;
    }
}
