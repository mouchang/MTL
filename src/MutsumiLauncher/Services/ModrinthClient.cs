using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

public sealed class ModrinthSearchResponse
{
    [JsonPropertyName("hits")]
    public List<ModrinthHit> Hits { get; set; } = [];
}

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
}

public sealed class ModrinthFile
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("primary")] public bool Primary { get; set; }

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
    private static string BaseUrl => DownloadMirror.ModrinthApiBase;

    private static readonly HttpClient Http = CreateClient();

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

    public async Task<IReadOnlyList<ModrinthHit>> SearchAsync(string query, string projectType,
        string? gameVersion, string? loader, string? category = null, int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var facets = new List<string> { $"[\"project_type:{projectType}\"]" };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add($"[\"versions:{gameVersion}\"]");

        if (!string.IsNullOrWhiteSpace(loader))
            facets.Add($"[\"categories:{loader}\"]");

        if (!string.IsNullOrWhiteSpace(category))
            facets.Add($"[\"categories:{category}\"]");

        var facetsJson = "[" + string.Join(",", facets) + "]";

        var url = $"{BaseUrl}/search?limit={limit}&index=relevance" +
                  $"&query={Uri.EscapeDataString(query ?? string.Empty)}" +
                  $"&facets={Uri.EscapeDataString(facetsJson)}";

        var json = await Http.GetStringAsync(url, cancellationToken);
        var response = JsonSerializer.Deserialize<ModrinthSearchResponse>(json);
        return response?.Hits ?? [];
    }

    /// <summary>Modrinth 支持的全部游戏版本（用于搜索页版本下拉）。</summary>
    public async Task<IReadOnlyList<ModrinthGameVersion>> GetGameVersionTagsAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await Http.GetStringAsync($"{BaseUrl}/tag/game_version", cancellationToken);
        return JsonSerializer.Deserialize<List<ModrinthGameVersion>>(json) ?? [];
    }

    public async Task<ModrinthProject?> GetProjectAsync(string projectId,
        CancellationToken cancellationToken = default)
    {
        var json = await Http.GetStringAsync($"{BaseUrl}/project/{projectId}", cancellationToken);
        return JsonSerializer.Deserialize<ModrinthProject>(json);
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(string projectId,
        CancellationToken cancellationToken = default)
    {
        var json = await Http.GetStringAsync($"{BaseUrl}/project/{projectId}/version", cancellationToken);
        return JsonSerializer.Deserialize<List<ModrinthVersion>>(json) ?? [];
    }

    public async Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string? gameVersion,
        string? loader, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/project/{projectId}/version";
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}");

        if (!string.IsNullOrWhiteSpace(loader))
            query.Add($"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}");

        if (query.Count > 0) url += "?" + string.Join("&", query);

        var json = await Http.GetStringAsync(url, cancellationToken);
        var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json) ?? [];
        return versions.FirstOrDefault();
    }

    public async Task<string> DownloadAsync(string url, string fileName, string targetDirectory,
        string? sha1 = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        var target = Path.Combine(targetDirectory, fileName);
        var temp = target + ".tmp";

        var downloadUrl = DownloadMirror.ModFileUrl(url);
        using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(temp);
            await source.CopyToAsync(file, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(sha1))
        {
            var actual = Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(temp, cancellationToken)));
            if (!actual.Equals(sha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new InvalidDataException($"校验失败：{fileName}");
            }
        }

        File.Move(temp, target, overwrite: true);
        AppLog.Info($"modrinth download: {fileName} -> {target}");
        return target;
    }
}
