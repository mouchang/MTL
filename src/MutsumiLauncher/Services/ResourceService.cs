using System.Collections.Concurrent;
using System.IO;

namespace MutsumiLauncher.Services;

public enum ResourcePlatform
{
    Modrinth,
    CurseForge,
}

public sealed record ResourceHit(ModrinthHit Hit, ResourcePlatform Platform);

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
        int page, int pageSize, CancellationToken cancellationToken = default)
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
                modrinthLimit, modrinthOffset, cancellationToken);

        if (platform is null or ResourcePlatform.CurseForge)
            curseForgeTask = CurseForge.SearchAsync(query, ClassIdOf(projectType), gameVersion,
                LoaderTypeOf(loader), aggregate ? 0 : page, curseForgeLimit, cancellationToken);

        if (modrinthTask is not null)
        {
            var result = await modrinthTask;
            modrinthTotal = result.TotalHits;
            modrinthHits = result.Hits
                .Select(hit => new ResourceHit(hit, ResourcePlatform.Modrinth))
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
                }, ResourcePlatform.CurseForge)).ToList();
            }
            catch (Exception ex) when (aggregate)
            {
                AppLog.Error(ex, "curseforge search in aggregate");
                curseForgeTotal = 0;
            }
        }

        var hits = aggregate
            ? modrinthHits.Concat(curseForgeHits)
                .OrderByDescending(hit => hit.Hit.Downloads)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToList()
            : (platform == ResourcePlatform.CurseForge ? curseForgeHits : modrinthHits);

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

        if (project is not null) ProjectCache[key] = project;
        return project;
    }

    public static async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(ResourcePlatform platform,
        string projectId, CancellationToken cancellationToken = default)
    {
        var key = $"{platform}:{projectId}";
        if (VersionCache.TryGetValue(key, out var cached)) return cached;

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
