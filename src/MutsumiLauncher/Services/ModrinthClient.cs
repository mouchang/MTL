using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

public sealed class ModrinthSearchResponse
{
    [JsonPropertyName("hits")]
    public List<ModrinthHit> Hits { get; set; } = [];

    [JsonPropertyName("total_hits")]
    public long TotalHits { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}

public sealed record ModrinthSearchResult(IReadOnlyList<ModrinthHit> Hits, long TotalHits);

public sealed class ModrinthHit
{
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("slug")] public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;

    [JsonPropertyName("downloads")] public long Downloads { get; set; }

    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }

    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];

    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = string.Empty;

    [JsonPropertyName("date_modified")] public DateTimeOffset DateModified { get; set; }
}

public sealed class ModrinthCategory
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = string.Empty;

    [JsonPropertyName("header")] public string Header { get; set; } = string.Empty;
}

public sealed class ModrinthGameVersion
{
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;

    [JsonPropertyName("version_type")] public string VersionType { get; set; } = string.Empty;
}

public sealed class ModrinthProject
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("slug")] public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;

    [JsonPropertyName("body")] public string Body { get; set; } = string.Empty;

    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];

    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("downloads")] public long Downloads { get; set; }

    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }

    [JsonPropertyName("updated")] public DateTimeOffset Updated { get; set; }

    [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }

    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "mod";
}

public sealed class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("version_type")] public string VersionType { get; set; } = "release";

    [JsonPropertyName("date_published")] public DateTimeOffset DatePublished { get; set; }

    [JsonPropertyName("downloads")] public long Downloads { get; set; }

    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")] public List<ModrinthDependency> Dependencies { get; set; } = [];
}

public sealed class ModrinthDependency
{
    [JsonPropertyName("version_id")] public string? VersionId { get; set; }

    [JsonPropertyName("project_id")] public string? ProjectId { get; set; }

    [JsonPropertyName("file_name")] public string? FileName { get; set; }

    [JsonPropertyName("dependency_type")] public string DependencyType { get; set; } = "required";
}

public sealed class ModrinthFile
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("primary")] public bool Primary { get; set; }

    [JsonPropertyName("size")] public long Size { get; set; }

    [JsonPropertyName("hashes")] public ModrinthHashes? Hashes { get; set; }
}

public sealed class ModrinthHashes
{
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")] public string? Sha512 { get; set; }
}

/// <summary>Modrinth 开放 API：搜索项目、取版本、下载文件。</summary>
public sealed class ModrinthClient
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>按源顺序请求 Modrinth API，前面的源失败时自动换下一个（PCL2 的 DlModRequest 同款）。</summary>
    private static async Task<string> GetStringAsync(string relativeUrl,
        CancellationToken cancellationToken = default)
    {
        Exception? last = null;

        foreach (var baseUrl in DownloadSources.ModrinthApiBases)
        {
            try
            {
                return await Http.GetStringAsync(baseUrl + relativeUrl, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                AppLog.Error(ex, $"modrinth api {baseUrl}");
            }
        }

        throw new InvalidOperationException("Modrinth 接口请求失败：" + last?.Message, last);
    }

    /// <summary>同上，用于需要 POST 的接口。</summary>
    private static async Task<string> PostJsonAsync(string relativeUrl, string payload,
        CancellationToken cancellationToken)
    {
        Exception? last = null;

        foreach (var baseUrl in DownloadSources.ModrinthApiBases)
        {
            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(baseUrl + relativeUrl, content,
                    cancellationToken);

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                AppLog.Error(ex, $"modrinth api post {baseUrl}");
            }
        }

        throw new InvalidOperationException("Modrinth 接口请求失败：" + last?.Message, last);
    }

    private static HttpClient CreateClient()
    {
        var client = HttpUtil.Create(TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var token = ResolveToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            AppLog.Info("modrinth: 使用已配置的 Personal Access Token");
        }
        else
        {
            AppLog.Info("modrinth: 匿名请求（未配置 token）");
        }

        return client;
    }

    /// <summary>环境变量优先，其次本地 config.json；不写入日志、不进仓库。</summary>
    private static string? ResolveToken()
    {
        var fromEnv = Environment.GetEnvironmentVariable("MTL_MODRINTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        var fromConfig = AppConfig.Current.ModrinthToken;
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig.Trim();
    }

    public async Task<ModrinthSearchResult> SearchAsync(string query, string projectType,
        string? gameVersion, string? loader, string? category = null, int limit = 20, int offset = 0,
        string index = "downloads", CancellationToken cancellationToken = default)
    {
        var facets = new List<string> { $"[\"project_type:{projectType}\"]" };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add($"[\"versions:{gameVersion}\"]");

        if (!string.IsNullOrWhiteSpace(loader))
            facets.Add($"[\"categories:{loader}\"]");

        if (!string.IsNullOrWhiteSpace(category))
            facets.Add($"[\"categories:{category}\"]");

        var facetsJson = "[" + string.Join(",", facets) + "]";

        var url = $"/search?limit={limit}&offset={offset}&index={index}" +
                  $"&query={Uri.EscapeDataString(query ?? string.Empty)}" +
                  $"&facets={Uri.EscapeDataString(facetsJson)}";

        var json = await GetStringAsync(url, cancellationToken);
        var response = JsonSerializer.Deserialize<ModrinthSearchResponse>(json);
        return new ModrinthSearchResult(response?.Hits ?? [], response?.TotalHits ?? 0);
    }

    /// <summary>Modrinth 的分类标签（type=mod 等），用于搜索页「类型」下拉。</summary>
    public async Task<IReadOnlyList<ModrinthCategory>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync("/tag/category", cancellationToken);
        return JsonSerializer.Deserialize<List<ModrinthCategory>>(json) ?? [];
    }

    /// <summary>Modrinth 支持的全部游戏版本（用于搜索页版本下拉）。</summary>
    public async Task<IReadOnlyList<ModrinthGameVersion>> GetGameVersionTagsAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync("/tag/game_version", cancellationToken);
        return JsonSerializer.Deserialize<List<ModrinthGameVersion>>(json) ?? [];
    }

    public async Task<ModrinthProject?> GetProjectAsync(string projectId,
        CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync($"/project/{projectId}", cancellationToken);
        return JsonSerializer.Deserialize<ModrinthProject>(json);
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(string projectId,
        CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync($"/project/{projectId}/version", cancellationToken);
        return JsonSerializer.Deserialize<List<ModrinthVersion>>(json) ?? [];
    }

    public async Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string? gameVersion,
        string? loader, CancellationToken cancellationToken = default)
    {
        var url = $"/project/{projectId}/version";
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}");

        if (!string.IsNullOrWhiteSpace(loader))
            query.Add($"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}");

        if (query.Count > 0) url += "?" + string.Join("&", query);

        var json = await GetStringAsync(url, cancellationToken);
        var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json) ?? [];
        return versions.FirstOrDefault();
    }

    /// <summary>用本地 jar 的 SHA-1 查询 Modrinth 上匹配当前游戏版本/加载器的更新。</summary>
    public async Task<Dictionary<string, ModrinthVersion>> CheckUpdatesAsync(
        IReadOnlyCollection<string> sha1Hashes, string? gameVersion, string? loader,
        CancellationToken cancellationToken = default)
    {
        var hashes = sha1Hashes
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hashes.Count == 0) return [];

        var payload = new Dictionary<string, object>
        {
            ["hashes"] = hashes,
            ["algorithm"] = "sha1",
        };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            payload["game_versions"] = new[] { gameVersion };

        if (!string.IsNullOrWhiteSpace(loader))
            payload["loaders"] = new[] { loader };

        var json = await PostJsonAsync("/version_files/update",
            JsonSerializer.Serialize(payload), cancellationToken);
        var result = JsonSerializer.Deserialize<Dictionary<string, ModrinthVersion>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result is null) return [];

        // update 接口对「本地已经是最新」的哈希也会把那个最新版回给你，
        // 所以用文件 SHA-1 再确认一次：返回的版本里如果装着本地这个文件，就不算更新
        foreach (var (hash, version) in result.ToList())
        {
            var isSameFile = version.Files.Any(file =>
                string.Equals(file.Hashes?.Sha1, hash, StringComparison.OrdinalIgnoreCase));

            if (isSameFile) result.Remove(hash);
        }

        return result;
    }

    public async Task<string> DownloadAsync(string url, string fileName, string targetDirectory,
        string? sha1 = null, long? size = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);

        var target = Path.Combine(targetDirectory, fileName);
        var endpoints = DownloadSources.ModFile(url);

        await DownloadEngine.DownloadAsync(endpoints, target, sha1, size, null, cancellationToken);

        AppLog.Info($"modrinth download: {fileName} -> {target}");
        return target;
    }
}
