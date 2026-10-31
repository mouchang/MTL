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
    string DisplayName,
    uint Fingerprint);

/// <summary>
/// CurseForge 客户端。默认走自家的 Worker 反代（mtl-curseforge，官方 Key 存在 Cloudflare），
/// 反代不可用时退回 MCIM 社区镜像。
/// </summary>
public sealed class CurseForgeClient
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(30));

    /// <summary>社区镜像（免 Key，但慢）。</summary>
    private static string MirrorBase => DownloadSources.Mcim + "/curseforge/v1";

    /// <summary>Worker 反代不可用（没部署 / 没配 Key）时退回镜像。</summary>
    private static bool _proxyUnavailable;

    private static string ProxyUrl
    {
        get
        {
            var url = AppConfig.Current.CurseForgeProxy;
            return string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');
        }
    }

    /// <summary>当前是不是走 Worker 反代。</summary>
    public static bool IsProxied => ProxyUrl.Length > 0 && !_proxyUnavailable;

    /// <summary>Worker 反代 → 社区镜像（官方 Key 只在 Worker 里，客户端不接触）。</summary>
    private static string BaseUrl => IsProxied ? ProxyUrl : MirrorBase;

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

        var url = $"{BaseUrl}/mods/search?" + string.Join('&', parameters);

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
        using var document = await GetAsync($"{BaseUrl}/mods/{modId}",
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

    /// <summary>批量取项目（一次最多 100 个）。</summary>
    public async Task<IReadOnlyList<CurseForgeModDetail>> GetModsAsync(
        IReadOnlyCollection<int> modIds, CancellationToken cancellationToken = default)
    {
        var ids = modIds.Where(id => id > 0).Distinct().Take(100).ToList();
        if (ids.Count == 0) return [];

        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["modIds"] = ids });

        using var document = await PostAsync($"{BaseUrl}/mods", payload, cancellationToken);

        if (document is null || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<CurseForgeModDetail>();

        foreach (var element in data.EnumerateArray())
        {
            var updated = DateTimeOffset.TryParse(Text(element, "dateModified"), out var date)
                ? date
                : DateTimeOffset.MinValue;

            result.Add(new CurseForgeModDetail(
                Int(element, "id"),
                Text(element, "name"),
                Text(element, "summary"),
                Logo(element),
                Authors(element),
                Long(element, "downloadCount"),
                updated,
                Website(element)));
        }

        return result;
    }

    /// <summary>单页条数。CurseForge 接口单页最多 50，想拿全只能翻页。</summary>
    private const int FilePageSize = 50;

    /// <summary>最多翻取的文件数。热门 Mod 有几万个文件，没必要全拉（也拉不完）。</summary>
    private const int MaxFiles = 300;

    /// <summary>
    /// 拉取某个项目的文件列表。
    /// 之前只请求第一页（50 个），像 JEI 这种 3900+ 文件的项目拿到的版本是残缺的；
    /// 这里按页翻到够用为止，并像 PCL2 那样对「空页 / 少返回」重试。
    /// </summary>
    public async Task<IReadOnlyList<CurseForgeFile>> GetFilesAsync(int modId, string? gameVersion,
        int? loaderType, CancellationToken cancellationToken = default)
    {
        var files = new List<CurseForgeFile>();
        var seen = new HashSet<int>();
        var index = 0;
        var total = -1;

        for (var page = 0; page < MaxFiles / FilePageSize; page++)
        {
            var (parsed, rawCount, totalCount) =
                await RequestFilesAsync(modId, gameVersion, loaderType, index, cancellationToken);

            if (rawCount == 0) break;

            foreach (var file in parsed)
                if (seen.Add(file.Id)) files.Add(file);

            index += rawCount;
            if (totalCount > 0) total = totalCount;

            if (total >= 0 && index >= total) break;
            if (files.Count >= MaxFiles) break;
        }

        if (total > files.Count)
            AppLog.Info($"curseforge files: mod {modId} 取到 {files.Count} 个（共 {total} 个，已截断）");
        else
            AppLog.Info($"curseforge files: mod {modId} 取到 {files.Count} 个");

        return files;
    }

    private async Task<(List<CurseForgeFile> Files, int RawCount, int Total)> RequestFilesAsync(
        int modId, string? gameVersion, int? loaderType, int index, CancellationToken cancellationToken)
    {
        Exception? last = null;

        for (var retry = 0; retry < 3; retry++)
        {
            var parameters = new List<string> { $"pageSize={FilePageSize + retry}" };

            if (!string.IsNullOrWhiteSpace(gameVersion))
                parameters.Add("gameVersion=" + Uri.EscapeDataString(gameVersion));

            if (loaderType is { } loader)
                parameters.Add("modLoaderType=" + loader);

            if (index > 0) parameters.Add("index=" + index);

            var url = $"{BaseUrl}/mods/{modId}/files?"
                      + string.Join('&', parameters);

            try
            {
                using var document = await GetAsync(url, cancellationToken);

                if (!document.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                    return ([], 0, -1);

                var rawCount = data.GetArrayLength();
                var parsed = new List<CurseForgeFile>();

                foreach (var item in data.EnumerateArray())
                {
                    var file = ParseFile(item);
                    if (file is not null) parsed.Add(file);
                }

                var total = document.RootElement.TryGetProperty("pagination", out var pagination)
                            && pagination.ValueKind == JsonValueKind.Object
                    ? (int)Long(pagination, "totalCount")
                    : -1;

                // 总量还有却返回空页：接口抽风了，重试（PCL2 的「分页缺失」处理）
                if (rawCount == 0 && total > index && retry < 2)
                {
                    AppLog.Info($"curseforge files 返回空页（index {index} / total {total}），重试");
                    await Task.Delay(300 * (retry + 1), cancellationToken);
                    continue;
                }

                return (parsed, rawCount, total);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                AppLog.Error(ex, $"curseforge files {url}");
                await Task.Delay(300 * (retry + 1), cancellationToken);
            }
        }

        if (last is not null)
            throw new InvalidOperationException("CurseForge 文件列表获取失败：" + last.Message, last);

        return ([], 0, -1);
    }

    public async Task<CurseForgeFile?> GetFileAsync(int modId, int fileId,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetAsync(
            $"{BaseUrl}/mods/{modId}/files/{fileId}", cancellationToken);

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

        var json = await PostJsonAsync(payload, cancellationToken).ConfigureAwait(false);
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

                // file.fileFingerprint 是服务端回显的指纹，靠它才能把结果和本地文件对上；
                // 按文件名匹配在文件被改过名（比如 xxx (1).jar）时会失配
                var fingerprint = file.TryGetProperty("fileFingerprint", out var fp)
                                  && fp.ValueKind == JsonValueKind.Number
                    ? (uint)fp.GetInt64()
                    : 0u;

                matches.Add(new CurseForgeFingerprintMatch(
                    modId,
                    Int(file, "id"),
                    Text(file, "fileName"),
                    Text(file, "displayName"),
                    fingerprint));
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


    /// <summary>
    /// 按文件指纹反查项目，返回 指纹 → 图标地址。
    /// 一次最多 1000 个指纹，项目信息再按 50 个一批查 logo。
    /// </summary>
    public static async Task<Dictionary<uint, string>> LookupIconsByFingerprintAsync(
        IReadOnlyList<uint> fingerprints, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<uint, string>();
        if (fingerprints.Count == 0) return result;

        var matches = new Dictionary<uint, int>();   // 指纹 → 项目 id

        foreach (var batch in Chunk(fingerprints.Distinct().ToList(), 1000))
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { fingerprints = batch });
                using var document = await PostAsync($"{BaseUrl}/fingerprints", payload, cancellationToken);

                if (!document.RootElement.TryGetProperty("data", out var data)) continue;
                if (!data.TryGetProperty("exactMatches", out var matchesArray)) continue;
                if (matchesArray.ValueKind != JsonValueKind.Array) continue;

                foreach (var match in matchesArray.EnumerateArray())
                {
                    if (!match.TryGetProperty("id", out var idElement)) continue;
                    if (!match.TryGetProperty("file", out var file)) continue;
                    if (!file.TryGetProperty("fingerprint", out var fingerprint)) continue;

                    var modId = idElement.GetInt32();
                    var value = fingerprint.TryGetUInt32(out var parsed)
                        ? parsed
                        : (uint)fingerprint.GetInt64();

                    matches[value] = modId;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "curseforge fingerprints");
            }
        }

        if (matches.Count == 0) return result;

        foreach (var batch in Chunk(matches.Values.Distinct().ToList(), 50))
        {
            try
            {
                var ids = string.Join(',', batch);
                using var document = await GetAsync($"{BaseUrl}/mods?modIds={ids}", cancellationToken);

                if (!document.RootElement.TryGetProperty("data", out var data)) continue;
                if (data.ValueKind != JsonValueKind.Array) continue;

                foreach (var mod in data.EnumerateArray())
                {
                    if (!mod.TryGetProperty("id", out var idElement)) continue;
                    if (!mod.TryGetProperty("logo", out var logo)) continue;

                    var modId = idElement.GetInt32();
                    var url = logo.ValueKind == JsonValueKind.Object
                        ? (Text(logo, "thumbnailUrl") ?? Text(logo, "url"))
                        : null;

                    if (string.IsNullOrWhiteSpace(url)) continue;

                    foreach (var (fingerprint, owner) in matches.Where(pair => pair.Value == modId))
                        result[fingerprint] = url!;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "curseforge mod ascii");
            }
        }

        return result;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }

    private static async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ShouldFallBack(ex))
        {
            return await SendAsync(ToMirror(url), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<JsonDocument> SendAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MutsumiLauncher/1.0");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(json);
    }

    private static async Task<string> PostJsonAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            return await SendPostAsync(BaseUrl, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ShouldFallBack(ex))
        {
            return await SendPostAsync(MirrorBase, payload, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>POST 并解析 JSON（带镜像回落）。</summary>
    private static async Task<JsonDocument> PostAsync(string url, string payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendPostDocumentAsync(url, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ShouldFallBack(ex))
        {
            return await SendPostDocumentAsync(ToMirror(url), payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<JsonDocument> SendPostDocumentAsync(string url, string payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.UserAgent.ParseAdd("MutsumiLauncher/1.0");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(json);
    }

    private static async Task<string> SendPostAsync(string baseUrl, string payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/fingerprints")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.UserAgent.ParseAdd("MutsumiLauncher/1.0");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>官方 403 / 反代不可用：标记一下，这一次直接换镜像重试。</summary>
    private static bool ShouldFallBack(Exception exception)
    {
        if (exception is not HttpRequestException { StatusCode: { } status }) return false;

        // 反代没部署 / 路由被别人接走时，这些状态码都会出现（实测主 Worker 对未知路径回 403）
        if (IsProxied && status is System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.MethodNotAllowed
                or System.Net.HttpStatusCode.NotImplemented
                or System.Net.HttpStatusCode.BadGateway
                or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            _proxyUnavailable = true;
            AppLog.Info($"curseforge: Worker 反代不可用（{(int)status}），改用社区镜像");
            return true;
        }

        return false;
    }

    private static string ToMirror(string url)
    {
        var proxy = ProxyUrl;

        if (proxy.Length > 0 && url.StartsWith(proxy, StringComparison.OrdinalIgnoreCase))
            return MirrorBase + url[proxy.Length..];

        return url;
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