using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public enum ResourcePlatform
{
    Modrinth,
    CurseForge,
}

public enum ResourceSort
{
    Downloads,
    Updated,
    Relevance,
}

public sealed record ResourceHit(ModrinthHit Hit, ResourcePlatform Platform, DateTimeOffset Updated);

public sealed record ResourceSearchPage(
    IReadOnlyList<ResourceHit> Hits,
    long Total,
    long ModrinthTotal,
    long CurseForgeTotal);

/// <summary>
/// 资源获取统一入口（参考 PCL2 的 ResourceSearcher / ResourceProject / ResourceVersion）：
/// 搜索、项目详情、版本列表、依赖、下载都按平台路由，界面只关心统一的模型。
/// </summary>
public static class ResourceService
{
    private const int AggregateMaxItems = 100;

    private static readonly ModrinthClient Modrinth = new();
    private static readonly CurseForgeClient CurseForge = new();

    private static readonly ConcurrentDictionary<string, ModrinthProject> ProjectCache = new();
    private static readonly ConcurrentDictionary<string, IReadOnlyList<ModrinthVersion>> VersionCache = new();

    public static async Task<ResourceSearchPage> SearchAsync(ResourcePlatform? platform,
        string query, string projectType, string? gameVersion, string? loader, string? category,
        int page, int pageSize, ResourceSort sort = ResourceSort.Downloads,
        CancellationToken cancellationToken = default)
    {
        // 社区平台只认英文：中文查询先翻译再搜（PCL2 也是把中文关键词换成英文）。
        // 翻译不出来的仍然按原文搜，至少中文标题的项目还能命中。
        var translated = SearchAliases.Translate(query);
        var effective = translated ?? query;

        if (translated is not null && !translated.Equals(query, StringComparison.OrdinalIgnoreCase))
            AppLog.Info($"search translated: {query} -> {translated}");

        return await SearchCoreAsync(platform, effective, projectType, gameVersion, loader,
            category, page, pageSize, sort, cancellationToken);
    }

    private static async Task<ResourceSearchPage> SearchCoreAsync(ResourcePlatform? platform,
        string query, string projectType, string? gameVersion, string? loader, string? category,
        int page, int pageSize, ResourceSort sort,
        CancellationToken cancellationToken)
    {
        var modrinthHits = new List<ResourceHit>();
        var curseForgeHits = new List<ResourceHit>();
        long modrinthTotal = 0;
        long curseForgeTotal = 0;

        var aggregate = platform is null;
        var fetchLimit = aggregate ? Math.Min(AggregateMaxItems, (page + 1) * pageSize) : pageSize;
        var modrinthLimit = Math.Min(100, fetchLimit);
        var curseForgeLimit = Math.Min(50, fetchLimit);
        var modrinthOffset = aggregate ? 0 : page * pageSize;

        Task<ModrinthSearchResult>? modrinthTask = null;
        Task<CurseForgeSearchResult>? curseForgeTask = null;

        if (platform is null or ResourcePlatform.Modrinth)
            modrinthTask = Modrinth.SearchAsync(query, projectType, gameVersion, loader, category,
                modrinthLimit, modrinthOffset, IndexOf(sort), cancellationToken);

        if (platform is null or ResourcePlatform.CurseForge)
            curseForgeTask = CurseForge.SearchAsync(query, ClassIdOf(projectType), gameVersion,
                LoaderTypeOf(loader), aggregate ? 0 : page, curseForgeLimit,
                sort == ResourceSort.Updated ? 3 : sort == ResourceSort.Relevance ? 1 : 6,
                "desc", cancellationToken);

        if (modrinthTask is not null)
        {
            var result = await modrinthTask;
            modrinthTotal = result.TotalHits;
            modrinthHits = result.Hits
                .Select(hit => new ResourceHit(hit, ResourcePlatform.Modrinth, hit.DateModified))
                .ToList();
        }

        if (curseForgeTask is not null)
        {
            try
            {
                var result = await curseForgeTask;
                curseForgeTotal = result.Total;
                curseForgeHits = result.Mods.Select(mod => new ResourceHit(new ModrinthHit
                {
                    ProjectId = mod.Id.ToString(),
                    Slug = mod.Slug,
                    Title = mod.Name,
                    Description = mod.Summary,
                    Author = mod.Author,
                    Downloads = mod.Downloads,
                    IconUrl = mod.IconUrl,
                    ProjectType = projectType,
                }, ResourcePlatform.CurseForge, mod.Updated)).ToList();
            }
            catch (Exception ex) when (aggregate)
            {
                AppLog.Error(ex, "curseforge search in aggregate");
                curseForgeTotal = 0;
            }
        }

        List<ResourceHit> hits;

        if (!aggregate)
        {
            hits = platform == ResourcePlatform.CurseForge ? curseForgeHits : modrinthHits;
        }
        else
        {
            IEnumerable<ResourceHit> sorted = sort switch
            {
                ResourceSort.Updated => modrinthHits.Concat(curseForgeHits)
                    .OrderByDescending(hit => hit.Updated),
                ResourceSort.Relevance => Interleave(modrinthHits, curseForgeHits),
                _ => modrinthHits.Concat(curseForgeHits).OrderByDescending(hit => hit.Hit.Downloads),
            };

            hits = sorted.Skip(page * pageSize).Take(pageSize).ToList();
        }

        var total = aggregate
            ? modrinthTotal + curseForgeTotal
            : platform == ResourcePlatform.CurseForge ? curseForgeTotal : modrinthTotal;

        return new ResourceSearchPage(hits, total, modrinthTotal, curseForgeTotal);
    }

    public static async Task<ModrinthProject?> GetProjectAsync(ResourcePlatform platform, string projectId,
        string projectType = "mod", CancellationToken cancellationToken = default)
    {
        var key = $"{platform}:{projectId}";
        if (ProjectCache.TryGetValue(key, out var cached)) return cached;

        if (TryLoadDisk($"project-{platform}-{projectId}", out ModrinthProject? diskProject))
        {
            ProjectCache[key] = diskProject!;
            return diskProject;
        }

        ModrinthProject? project;

        if (platform == ResourcePlatform.CurseForge)
        {
            if (!int.TryParse(projectId, out var modId)) return null;

            var detail = await CurseForge.GetModAsync(modId, cancellationToken);
            if (detail is null) return null;

            project = new ModrinthProject
            {
                Id = detail.Id.ToString(),
                Slug = detail.WebsiteUrl.Split('/').LastOrDefault() ?? detail.Id.ToString(),
                Title = detail.Name,
                Description = detail.Summary,
                Downloads = detail.Downloads,
                IconUrl = detail.IconUrl,
                Updated = detail.Updated,
                ProjectType = projectType,
                SourceUrl = detail.WebsiteUrl,
            };
        }
        else
        {
            project = await Modrinth.GetProjectAsync(projectId, cancellationToken);
        }

        if (project is not null)
        {
            ProjectCache[key] = project;
            SaveDisk($"project-{platform}-{projectId}", project);
        }

        return project;
    }

    public static async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(ResourcePlatform platform,
        string projectId, CancellationToken cancellationToken = default)
    {
        var key = $"{platform}:{projectId}";
        if (VersionCache.TryGetValue(key, out var cached)) return cached;

        if (TryLoadDisk($"versions-{platform}-{projectId}", out List<ModrinthVersion>? diskVersions))
        {
            VersionCache[key] = diskVersions!;
            return diskVersions!;
        }

        IReadOnlyList<ModrinthVersion> versions;

        if (platform == ResourcePlatform.CurseForge)
        {
            if (!int.TryParse(projectId, out var modId)) return [];

            var files = await CurseForge.GetFilesAsync(modId, null, null, cancellationToken);
            versions = files.Select(file => new ModrinthVersion
            {
                Id = file.Id.ToString(),
                Name = file.DisplayName,
                VersionNumber = file.DisplayName,
                GameVersions = file.GameVersions.ToList(),
                Loaders = file.Loaders.ToList(),
                VersionType = file.ReleaseType,
                DatePublished = file.FileDate,
                Files =
                [
                    new ModrinthFile
                    {
                        Url = file.DownloadUrl,
                        Filename = file.FileName,
                        Primary = true,
                        Hashes = new ModrinthHashes { Sha1 = file.Sha1 },
                    },
                ],
                Dependencies = file.Dependencies
                    .Where(dependency => dependency.RelationType is 2 or 3)
                    .Select(dependency => new ModrinthDependency
                    {
                        ProjectId = dependency.ModId.ToString(),
                        DependencyType = dependency.RelationType == 3 ? "required" : "optional",
                    })
                    .DistinctBy(dependency => dependency.ProjectId)
                    .ToList(),
            }).ToList();
        }
        else
        {
            versions = await Modrinth.GetVersionsAsync(projectId, cancellationToken);
        }

        VersionCache[key] = versions;
        SaveDisk($"versions-{platform}-{projectId}", versions);
        return versions;
    }

    public static async Task<string> DownloadAsync(ModrinthFile file, string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);

        var target = Path.Combine(targetDirectory, file.Filename);
        var endpoints = DownloadSources.ModFile(file.Url);

        await DownloadEngine.DownloadAsync(endpoints, target, file.Hashes?.Sha1, null, null,
            cancellationToken);

        return target;
    }

    public static string WebUrl(ResourcePlatform platform, ModrinthProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SourceUrl)) return project.SourceUrl;

        if (platform == ResourcePlatform.CurseForge)
            return $"https://www.curseforge.com/minecraft/mc-mods/{project.Slug}";

        return $"https://modrinth.com/project/{project.Slug}";
    }

    private static List<ResourceHit> Interleave(IReadOnlyList<ResourceHit> first,
        IReadOnlyList<ResourceHit> second)
    {
        var result = new List<ResourceHit>(first.Count + second.Count);
        var max = Math.Max(first.Count, second.Count);

        for (var i = 0; i < max; i++)
        {
            if (i < first.Count) result.Add(first[i]);
            if (i < second.Count) result.Add(second[i]);
        }

        return result;
    }

    private static string IndexOf(ResourceSort sort) => sort switch
    {
        ResourceSort.Updated => "updated",
        ResourceSort.Relevance => "relevance",
        _ => "downloads",
    };

    private static string DiskPath(string key)
    {
        var safe = string.Concat(key.Where(character => char.IsLetterOrDigit(character)
                                                        || character is '-' or '_'));
        return Path.Combine(AppPaths.CacheDir, "api", safe + ".json");
    }

    private static bool TryLoadDisk<T>(string key, out T? value)
    {
        value = default;

        try
        {
            var path = DiskPath(key);
            if (!File.Exists(path)) return false;
            if (DateTime.Now - File.GetLastWriteTime(path) > TimeSpan.FromHours(6)) return false;

            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveDisk<T>(string key, T value)
    {
        try
        {
            var path = DiskPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "save resource cache");
        }
    }

    private static int ClassIdOf(string projectType) => projectType switch
    {
        "modpack" => 4471,
        "datapack" => 6945,
        "resourcepack" => 12,
        "shader" => 6552,
        _ => 6,
    };

    private static int? LoaderTypeOf(string? loader) => loader?.ToLowerInvariant() switch
    {
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        "forge" => 1,
        _ => null,
    };
}
