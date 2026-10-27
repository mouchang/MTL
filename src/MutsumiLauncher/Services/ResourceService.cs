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
    long CurseForgeTotal,
    int PageCount);

/// <summary>
/// 资源获取统一入口（参考 PCL2 的 ResourceSearcher / ResourceProject / ResourceVersion）：
/// 搜索、项目详情、版本列表、依赖、下载都按平台路由，界面只关心统一的模型。
/// </summary>
public static class ResourceService
{
    /// <summary>聚合缓存里最多存多少条（够翻 20 页）。</summary>
    private const int AggregateMaxItems = 400;

    /// <summary>聚合模式最多翻到第几页。</summary>
    private const int AggregateMaxPages = 20;

    private const int ModrinthChunk = 100;
    private const int CurseForgeChunk = 50;

    private static readonly ModrinthClient Modrinth = new();
    private static readonly CurseForgeClient CurseForge = new();

    private static readonly ConcurrentDictionary<string, ModrinthProject> ProjectCache = new();
    private static readonly ConcurrentDictionary<string, IReadOnlyList<ModrinthVersion>> VersionCache = new();
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> CategoryCache = new();
    private static readonly ConcurrentDictionary<string, AggregateBucket> AggregateCache = new();

    public static async Task<ResourceSearchPage> SearchAsync(ResourcePlatform? platform,
        string query, string projectType, string? gameVersion, string? loader, string? category,
        int page, int pageSize, ResourceSort sort = ResourceSort.Downloads,
        CancellationToken cancellationToken = default)
    {
        // 中文查询：用内置译名表换成英文名（离线，不联网）。
        // 译名表只覆盖 Mod，光影 / 资源包 / 整合包的中文关键词不翻译，免得翻串。
        var effective = query;
        var english = string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase)
            ? TranslationTable.TranslateToEnglish(query)
            : null;

        if (!string.IsNullOrWhiteSpace(english)
            && !english.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            effective = english;
            AppLog.Info($"search translated: {query} -> {english}");
        }

        return await SearchCoreAsync(platform, effective, projectType, gameVersion, loader,
            category, page, pageSize, sort, cancellationToken);
    }

    private static async Task<ResourceSearchPage> SearchCoreAsync(ResourcePlatform? platform,
        string query, string projectType, string? gameVersion, string? loader, string? category,
        int page, int pageSize, ResourceSort sort,
        CancellationToken cancellationToken)
    {
        if (platform is { } single)
        {
            return await SearchSingleAsync(single, query, projectType, gameVersion, loader, category,
                page, pageSize, sort, cancellationToken);
        }

        return await SearchAggregateAsync(query, projectType, gameVersion, loader, category,
            page, pageSize, sort, cancellationToken);
    }

    /// <summary>单平台：直接用接口的 offset 翻页。</summary>
    private static async Task<ResourceSearchPage> SearchSingleAsync(ResourcePlatform platform,
        string query, string projectType, string? gameVersion, string? loader, string? category,
        int page, int pageSize, ResourceSort sort, CancellationToken cancellationToken)
    {
        if (platform == ResourcePlatform.CurseForge)
        {
            var result = await CurseForge.SearchAsync(query, ClassIdOf(projectType), gameVersion,
                LoaderTypeOf(loader), page, pageSize, SortFieldOf(sort), "desc", cancellationToken);

            var hits = result.Mods.Select(mod => ToHit(mod, projectType)).ToList();
            var pages = Math.Max(1, (int)Math.Ceiling(result.Total / (double)pageSize));

            return new ResourceSearchPage(hits, result.Total, 0, result.Total, pages);
        }

        var modrinth = await Modrinth.SearchAsync(query, projectType, gameVersion, loader, category,
            pageSize, page * pageSize, IndexOf(sort), cancellationToken);

        var modrinthHits = modrinth.Hits
            .Select(hit => new ResourceHit(hit, ResourcePlatform.Modrinth, hit.DateModified))
            .ToList();
        var modrinthPages = Math.Max(1, (int)Math.Ceiling(modrinth.TotalHits / (double)pageSize));

        return new ResourceSearchPage(modrinthHits, modrinth.TotalHits, modrinth.TotalHits, 0,
            modrinthPages);
    }

    /// <summary>
    /// 聚合：两边都从第 0 条开始抓，抓多少按页码按需增长。
    /// 缓存在进程内，翻页只在需要更深的数据时补抓，已经展示过的页不会跳来跳去。
    /// </summary>
    private static async Task<ResourceSearchPage> SearchAggregateAsync(string query, string projectType,
        string? gameVersion, string? loader, string? category, int page, int pageSize,
        ResourceSort sort, CancellationToken cancellationToken)
    {
        var key = string.Join('|', query, projectType, gameVersion, loader, category, (int)sort);
        var bucket = GetBucket(key);

        await bucket.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 合并会去重，所以按「两倍页大小」估需要抓多少条
            var needed = Math.Min(AggregateMaxItems, (page + 1) * pageSize * 2);

            while (bucket.Count < needed && bucket.Count < AggregateMaxItems
                   && (!bucket.ModrinthDone || !bucket.CurseForgeDone))
            {
                var before = bucket.Count;
                await FillAsync(bucket, query, projectType, gameVersion, loader, category, sort,
                    cancellationToken);

                if (bucket.Count == before) break;  // 没抓到新东西，别死循环
            }

            var sorted = sort switch
            {
                ResourceSort.Updated => bucket.Hits().OrderByDescending(hit => hit.Updated).ToList(),
                ResourceSort.Relevance => bucket.Hits().ToList(),
                _ => bucket.Hits().OrderByDescending(hit => hit.Hit.Downloads).ToList(),
            };

            // 「同一资源两边各出现一次」：把同一个项目的两条结果编成一组挨在一起
            var groups = GroupHits(sorted);

            if (sort == ResourceSort.Updated)
                groups = groups.OrderByDescending(group => group.Max(hit => hit.Updated)).ToList();
            else if (sort == ResourceSort.Downloads)
                groups = groups.OrderByDescending(group => group.Max(hit => hit.Hit.Downloads)).ToList();

            var hits = TakeGroups(groups, page, pageSize);

            var knownPages = Math.Max(1, (groups.Count + pageSize - 1) / pageSize);
            var apiPages = (int)Math.Ceiling(
                (bucket.ModrinthTotal + bucket.CurseForgeTotal) / (double)pageSize);
            var pageCount = Math.Clamp(Math.Max(knownPages, Math.Min(apiPages, AggregateMaxPages)),
                1, AggregateMaxPages);

            return new ResourceSearchPage(hits, bucket.ModrinthTotal + bucket.CurseForgeTotal,
                bucket.ModrinthTotal, bucket.CurseForgeTotal, pageCount);
        }
        finally
        {
            bucket.Gate.Release();
        }
    }

    /// <summary>两边各补抓一块（Modrinth 100 条 / CurseForge 50 条）。</summary>
    private static async Task FillAsync(AggregateBucket bucket, string query, string projectType,
        string? gameVersion, string? loader, string? category, ResourceSort sort,
        CancellationToken cancellationToken)
    {
        if (!bucket.ModrinthDone)
        {
            var limit = Math.Min(ModrinthChunk, AggregateMaxItems - bucket.Modrinth.Count);

            if (limit <= 0)
            {
                bucket.ModrinthDone = true;
            }
            else
            {
                var result = await Modrinth.SearchAsync(query, projectType, gameVersion, loader,
                    category, limit, bucket.ModrinthOffset, IndexOf(sort), cancellationToken);

                bucket.ModrinthTotal = result.TotalHits;
                bucket.ModrinthOffset += result.Hits.Count;
                bucket.Modrinth.AddRange(result.Hits.Select(hit =>
                    new ResourceHit(hit, ResourcePlatform.Modrinth, hit.DateModified)));

                if (result.Hits.Count < limit) bucket.ModrinthDone = true;
            }
        }

        if (!bucket.CurseForgeDone)
        {
            var limit = Math.Min(CurseForgeChunk, AggregateMaxItems - bucket.CurseForge.Count);

            if (limit <= 0)
            {
                bucket.CurseForgeDone = true;
            }
            else
            {
                try
                {
                    var result = await CurseForge.SearchAsync(query, ClassIdOf(projectType),
                        gameVersion, LoaderTypeOf(loader), bucket.CurseForgePage, limit,
                        SortFieldOf(sort), "desc", cancellationToken);

                    bucket.CurseForgeTotal = result.Total;
                    bucket.CurseForgePage++;
                    bucket.CurseForge.AddRange(result.Mods.Select(mod => ToHit(mod, projectType)));

                    if (result.Mods.Count < limit) bucket.CurseForgeDone = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // CurseForge 挂了不影响 Modrinth 的结果
                    AppLog.Error(ex, "curseforge search in aggregate");
                    bucket.CurseForgeDone = true;
                }
            }
        }
    }

    private static ResourceHit ToHit(CurseForgeMod mod, string projectType)
        => new(new ModrinthHit
        {
            ProjectId = mod.Id.ToString(),
            Slug = mod.Slug,
            Title = mod.Name,
            Description = mod.Summary,
            Author = mod.Author,
            Downloads = mod.Downloads,
            IconUrl = mod.IconUrl,
            ProjectType = projectType,
        }, ResourcePlatform.CurseForge, mod.Updated);

    private static int SortFieldOf(ResourceSort sort)
        => sort == ResourceSort.Updated ? 3 : sort == ResourceSort.Relevance ? 1 : 6;

    /// <summary>聚合搜索的分批缓存：翻页只补抓更深的条目，已经展示过的页不变。</summary>
    private sealed class AggregateBucket
    {
        public readonly List<ResourceHit> Modrinth = [];
        public readonly List<ResourceHit> CurseForge = [];
        public readonly SemaphoreSlim Gate = new(1, 1);

        public int ModrinthOffset;
        public int CurseForgePage;
        public long ModrinthTotal;
        public long CurseForgeTotal;
        public bool ModrinthDone;
        public bool CurseForgeDone;

        public int Count => Modrinth.Count + CurseForge.Count;

        public IEnumerable<ResourceHit> Hits() => Modrinth.Concat(CurseForge);
    }

    private static AggregateBucket GetBucket(string key)
    {
        if (AggregateCache.Count > 6) AggregateCache.Clear();
        return AggregateCache.GetOrAdd(key, _ => new AggregateBucket());
    }

    private static List<ResourceHit> TakeGroups(IReadOnlyList<List<ResourceHit>> groups, int page, int pageSize)
        => groups.Skip(page * pageSize).Take(pageSize).SelectMany(group => group).ToList();

    /// <summary>当前项目类型的全部分类标签（Modrinth 提供，CurseForge 也用同一套）。</summary>
    public static async Task<IReadOnlyList<string>> GetCategoriesAsync(string projectType,
        CancellationToken cancellationToken = default)
    {
        var key = $"categories:{projectType}";
        if (CategoryCache.TryGetValue(key, out var cached)) return cached;

        var all = await Modrinth.GetCategoriesAsync(cancellationToken);

        var list = all
            .Where(item => item.ProjectType.Equals(projectType, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CategoryCache[key] = list;
        return list;
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
                        Size = file.Length,
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

        // 接口已经给了文件大小，直接传给下载引擎，省掉一次 HEAD 探测（不通的源一次探测就要 15 秒）
        await DownloadEngine.DownloadAsync(endpoints, target, file.Hashes?.Sha1,
            file.Size > 0 ? file.Size : null, null, cancellationToken);

        return target;
    }

    public static string WebUrl(ResourcePlatform platform, ModrinthProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SourceUrl)) return project.SourceUrl;

        if (platform == ResourcePlatform.CurseForge)
            return $"https://www.curseforge.com/minecraft/mc-mods/{project.Slug}";

        return $"https://modrinth.com/project/{project.Slug}";
    }

    /// <summary>把一页结果按项目分组，供界面把同一资源的多个来源合成一张卡片。</summary>
    public static IReadOnlyList<IReadOnlyList<ResourceHit>> GroupResults(IEnumerable<ResourceHit> hits)
        => GroupHits(hits).Select(group => (IReadOnlyList<ResourceHit>)group).ToList();

    /// <summary>
    /// 按「项目」分组：slug 相同（或名字规范化后相同）视为同一个资源。
    /// 组内固定 Modrinth 在前、CurseForge 在后，方便对照两边的版本。
    /// </summary>
    private static List<List<ResourceHit>> GroupHits(IEnumerable<ResourceHit> hits)
    {
        var groups = new List<List<ResourceHit>>();
        var index = new Dictionary<string, List<ResourceHit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var hit in hits)
        {
            var key = AggregateKey(hit);

            if (index.TryGetValue(key, out var existing))
            {
                // 同一平台重复出现（接口偶发）就不再重复展示
                if (existing.Any(other => other.Platform == hit.Platform
                                          && other.Hit.ProjectId == hit.Hit.ProjectId))
                    continue;

                existing.Add(hit);
                continue;
            }

            var group = new List<ResourceHit> { hit };
            index[key] = group;
            groups.Add(group);
        }

        foreach (var group in groups) group.Sort((left, right) => left.Platform.CompareTo(right.Platform));

        return groups;
    }

    private static string AggregateKey(ResourceHit hit)
    {
        var slug = NormalizeKey(hit.Hit.Slug);
        return slug.Length > 0 ? slug : NormalizeKey(hit.Hit.Title);
    }

    /// <summary>统一成小写字母数字：industrial-craft / Industrial Craft → industrialcraft。</summary>
    private static string NormalizeKey(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>按组分页，尽量不让同一资源的两个来源被切到两页。</summary>
    private static List<ResourceHit> TakePage(IEnumerable<List<ResourceHit>> groups, int page, int pageSize)
    {
        var result = new List<ResourceHit>();
        var target = page * pageSize;
        var index = 0;

        foreach (var group in groups)
        {
            if (index + group.Count <= target)
            {
                index += group.Count;
                continue;
            }

            foreach (var hit in group)
            {
                if (index >= target && result.Count < pageSize) result.Add(hit);
                index++;
            }

            if (result.Count >= pageSize) break;
        }

        return result;
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
