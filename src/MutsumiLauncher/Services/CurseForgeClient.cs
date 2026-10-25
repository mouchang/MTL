using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record CurseForgeMod(
    int Id,
    string Name,
    string Slug,
    string Summary,
    string? IconUrl,
    string Author,
    long Downloads,
    DateTimeOffset Updated);

public sealed record CurseForgeDependency(int ModId, int RelationType);

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
    IReadOnlyList<string> Loaders,
    IReadOnlyList<CurseForgeDependency> Dependencies);

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

public sealed record CurseForgeFingerprintMatch(
    int ModId,
    int FileId,
    string FileName,
    string DisplayName);

/// <summary>
/// CurseForge 客户端。默认走 MCIM 镜像（无需 Key）；关闭社区镜像并配置 Key 后才会请求官方 API。
/// </summary>
public sealed class CurseForgeClient
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(30));

    /// <summary>暂无官方 Key，统一走 MCIM 镜像，因此始终可用。</summary>
    public static bool Available => true;

    /// <summary>classId：mod 6 / modpack 4471 / resourcepack 12 / shader 6552 / datapack 6945。</summary>
    public async Task<CurseForgeSearchResult> SearchAsync(string query, int classId,
        string? gameVersion, int? loaderType, int page, int pageSize,
        int sortField = 6, string sortOrder = "desc",
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            "gameId=432",
            $"classId={classId}",
            $"index={page * pageSize}",
            $"pageSize={pageSize}",
            $"sortField={sortField}",
            $"sortOrder={sortOrder}",
        };

        if (!string.IsNullOrWhiteSpace(query))
            parameters.Add("searchFilter=" + Uri.EscapeDataString(query));

        if (!string.IsNullOrWhiteSpace(gameVersion))
            parameters.Add("gameVersion=" + Uri.EscapeDataString(gameVersion));

        if (loaderType is { } loader)
            parameters.Add("modLoaderType=" + loader);

        var url = $"{DownloadSources.CurseForgeApiBase}/mods/search?" + string.Join('&', parameters);

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
                    Long(item, "downloadCount"),
                    DateTimeOffset.TryParse(Text(item, "dateModified"), out var modified)
                        ? modified
                        : DateTimeOffset.MinValue));
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
        using var document = await GetAsync($"{DownloadSources.CurseForgeApiBase}/mods/{modId}",
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

        var url = $"{DownloadSources.CurseForgeApiBase}/mods/{modId}/files?"
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
                loaders,
                Dependencies(item)));
        }

        return files;
    }

    public async Task<CurseForgeFile?> GetFileAsync(int modId, int fileId,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetAsync(
            $"{DownloadSources.CurseForgeApiBase}/mods/{modId}/files/{fileId}", cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
            return null;

        return ParseFile(data);
    }

    private static CurseForgeFile? ParseFile(JsonElement item)
    {
        var downloadUrl = Text(item, "downloadUrl");
        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

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

        return new CurseForgeFile(
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
            loaders,
            Dependencies(item));
    }

    /// <summary>用本地 jar 的指纹反查 CurseForge 上的文件，用于检查更新。</summary>
    public async Task<IReadOnlyList<CurseForgeFingerprintMatch>> MatchFingerprintsAsync(
        IReadOnlyList<uint> fingerprints, CancellationToken cancellationToken = default)
    {
        if (fingerprints.Count == 0) return [];

        var payload = JsonSerializer.Serialize(new
        {
            fingerprints = fingerprints.Select(value => (long)value).ToArray(),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{DownloadSources.CurseForgeApiBase}/fingerprints")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.UserAgent.ParseAdd("MutsumiLauncher/1.0");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);

        var matches = new List<CurseForgeFingerprintMatch>();

        if (document.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("exactMatches", out var exact)
            && exact.ValueKind == JsonValueKind.Array)
        {
            foreach (var match in exact.EnumerateArray())
            {
                var modId = Int(match, "id");
                if (modId == 0) continue;

                if (!match.TryGetProperty("file", out var file)
                    || file.ValueKind != JsonValueKind.Object)
                    continue;

                matches.Add(new CurseForgeFingerprintMatch(
                    modId,
                    Int(file, "id"),
                    Text(file, "fileName"),
                    Text(file, "displayName")));
            }
        }

        return matches;
    }

    /// <summary>CurseForge 文件指纹（MurmurHash2，seed=1，忽略空白字符）。</summary>
    public static uint ComputeFingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        var length = CountFingerprintBytes(stream);

        stream.Position = 0;

        const uint m = 0x5bd1e995;
        const int r = 24;

        var h = 1u ^ length;
        var buffer = new byte[81920];
        var block = new byte[4];
        var filled = 0;
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var value = buffer[i];
                if (value is 0x09 or 0x0A or 0x0D or 0x20) continue;

                block[filled++] = value;
                if (filled < 4) continue;

                var k = BitConverter.ToUInt32(block, 0);
                k *= m;
                k ^= k >> r;
                k *= m;

                h *= m;
                h ^= k;
                filled = 0;
            }
        }

        if (filled > 0)
        {
            uint tail = 0;
            for (var i = 0; i < filled; i++) tail |= (uint)block[i] << (8 * i);

            h ^= tail;
            h *= m;
        }

        h ^= h >> 13;
        h *= m;
        h ^= h >> 15;

        return h;
    }

    private static uint CountFingerprintBytes(Stream stream)
    {
        var buffer = new byte[81920];
        uint count = 0;
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var value = buffer[i];
                if (value is not (0x09 or 0x0A or 0x0D or 0x20)) count++;
            }
        }

        return count;
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

    private static IReadOnlyList<CurseForgeDependency> Dependencies(JsonElement file)
    {
        if (!file.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<CurseForgeDependency>();

        foreach (var dependency in dependencies.EnumerateArray())
        {
            var modId = Int(dependency, "modId");
            if (modId == 0) continue;

            result.Add(new CurseForgeDependency(modId, Int(dependency, "relationType")));
        }

        return result;
    }

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
