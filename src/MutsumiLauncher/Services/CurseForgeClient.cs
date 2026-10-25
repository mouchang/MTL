using System.Net.Http;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record CurseForgeMod(
    int Id,
    string Name,
    string Slug,
    string Summary,
    string? IconUrl,
    string Author,
    long Downloads);

public sealed record CurseForgeFile(
    int Id,
    string FileName,
    string DisplayName,
    string DownloadUrl,
    string? Sha1,
    long Length,
    string ReleaseType,
    DateTimeOffset FileDate,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders);

public sealed record CurseForgeModDetail(
    int Id,
    string Name,
    string Summary,
    string? IconUrl,
    string Author,
    long Downloads,
    DateTimeOffset Updated,
    string WebsiteUrl);

public sealed record CurseForgeSearchResult(IReadOnlyList<CurseForgeMod> Mods, long Total);

/// <summary>
/// CurseForge 客户端。默认走 MCIM 镜像（无需 Key）；关闭社区镜像并配置 Key 后才会请求官方 API。
/// </summary>
public sealed class CurseForgeClient
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(30));

    public static bool Available =>
        DownloadMirror.CurseForgeViaMirror || !string.IsNullOrWhiteSpace(ApiKey);

    private static string? ApiKey
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("MTL_CURSEFORGE_KEY");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            var fromConfig = AppConfig.Current.CurseForgeApiKey;
            return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig.Trim();
        }
    }

    /// <summary>classId：mod 6 / modpack 4471 / resourcepack 12 / shader 6552 / datapack 6945。</summary>
    public async Task<CurseForgeSearchResult> SearchAsync(string query, int classId,
        string? gameVersion, int? loaderType, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            "gameId=432",
            $"classId={classId}",
            $"index={page * pageSize}",
            $"pageSize={pageSize}",
        };

        if (!string.IsNullOrWhiteSpace(query))
            parameters.Add("searchFilter=" + Uri.EscapeDataString(query));

        if (!string.IsNullOrWhiteSpace(gameVersion))
            parameters.Add("gameVersion=" + Uri.EscapeDataString(gameVersion));

        if (loaderType is { } loader)
            parameters.Add("modLoaderType=" + loader);

        var url = $"{DownloadMirror.CurseForgeApiBase}/mods/search?" + string.Join('&', parameters);

        using var document = await GetAsync(url, cancellationToken);
        var root = document.RootElement;

        var mods = new List<CurseForgeMod>();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = Int(item, "id");
                if (id == 0) continue;

                mods.Add(new CurseForgeMod(
                    id,
                    Text(item, "name"),
                    Text(item, "slug"),
                    Text(item, "summary"),
                    Logo(item),
                    Authors(item),
                    Long(item, "downloadCount")));
            }
        }

        var total = root.TryGetProperty("pagination", out var pagination)
            ? Long(pagination, "totalCount")
            : mods.Count;

        return new CurseForgeSearchResult(mods, total);
    }

    public async Task<CurseForgeModDetail?> GetModAsync(int modId,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetAsync($"{DownloadMirror.CurseForgeApiBase}/mods/{modId}",
            cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
            return null;

        var updated = DateTimeOffset.TryParse(Text(data, "dateModified"), out var date)
            ? date
            : DateTimeOffset.MinValue;

        return new CurseForgeModDetail(
            Int(data, "id"),
            Text(data, "name"),
            Text(data, "summary"),
            Logo(data),
            Authors(data),
            Long(data, "downloadCount"),
            updated,
            Website(data));
    }

    public async Task<IReadOnlyList<CurseForgeFile>> GetFilesAsync(int modId, string? gameVersion,
        int? loaderType, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string> { "pageSize=50" };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            parameters.Add("gameVersion=" + Uri.EscapeDataString(gameVersion));

        if (loaderType is { } loader)
            parameters.Add("modLoaderType=" + loader);

        var url = $"{DownloadMirror.CurseForgeApiBase}/mods/{modId}/files?"
                  + string.Join('&', parameters);

        using var document = await GetAsync(url, cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return [];

        var files = new List<CurseForgeFile>();

        foreach (var item in data.EnumerateArray())
        {
            var downloadUrl = Text(item, "downloadUrl");
            if (string.IsNullOrWhiteSpace(downloadUrl)) continue;

            var gameVersions = Strings(item, "gameVersions");
            var loaders = gameVersions
                .Where(IsLoaderName)
                .Select(loader => loader.ToLowerInvariant())
                .ToList();

            var mcVersions = gameVersions
                .Where(version => !IsLoaderName(version))
                .Where(version => !version.Equals("Client", StringComparison.OrdinalIgnoreCase)
                                  && !version.Equals("Server", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var releaseType = Int(item, "releaseType") switch
            {
                1 => "release",
                2 => "beta",
                _ => "alpha",
            };

            files.Add(new CurseForgeFile(
                Int(item, "id"),
                Text(item, "fileName"),
                Text(item, "displayName"),
                downloadUrl,
                Sha1(item),
                Long(item, "fileLength"),
                releaseType,
                DateTimeOffset.TryParse(Text(item, "fileDate"), out var date)
                    ? date
                    : DateTimeOffset.MinValue,
                mcVersions,
                loaders));
        }

        return files;
    }

    private static bool IsLoaderName(string value) =>
        value.Equals("Fabric", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Forge", StringComparison.OrdinalIgnoreCase)
        || value.Equals("NeoForge", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Quilt", StringComparison.OrdinalIgnoreCase);

    private static async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MutsumiLauncher/1.0");

        if (!DownloadMirror.CurseForgeViaMirror && !string.IsNullOrWhiteSpace(ApiKey))
            request.Headers.Add("x-api-key", ApiKey);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(json);
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static long Long(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static List<string> Strings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToList();
    }

    private static string Website(JsonElement item) =>
        item.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object
            ? Text(links, "websiteUrl")
            : string.Empty;

    private static string? Logo(JsonElement element) =>
        element.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object
            ? Text(logo, "thumbnailUrl") is { Length: > 0 } thumb ? thumb : Text(logo, "url")
            : null;

    private static string Authors(JsonElement item) =>
        item.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array
            ? string.Join("、", authors.EnumerateArray()
                .Select(author => Text(author, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name)))
            : string.Empty;

    private static string? Sha1(JsonElement file)
    {
        if (!file.TryGetProperty("hashes", out var hashes) || hashes.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var hash in hashes.EnumerateArray())
        {
            // algo 1 = SHA-1
            if (Int(hash, "algo") == 1) return Text(hash, "value");
        }

        return null;
    }
}
