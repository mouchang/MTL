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
    int PageCount,
    bool CurseForgePending = false);

/// <summary>
/// 资源获取统一入口（参考 PCL2 的 ResourceSearcher / ResourceProject / ResourceVersion）：
/// 搜索、项目详情、版本列表、依赖、下载都按平台路由，界面只关心统一的模型。
/// </summary>
public static class ResourceService
{
    /// <summary>聚合缓存里最多存多少条。合并去重后差不多一条换半个分组，够翻 25 页。</summary>
    private const int AggregateMaxItems = 1000;

    /// <summary>聚合模式最多翻到第几页。</summary>
    private const int AggregateMaxPages = 25;

    /// <summary>
    /// 聚合时 CurseForge 走的是第三方镜像，偶尔一次请求要十几秒。
    /// 超时就先不用它，别让整个搜索页卡住 —— 已经抓到的 Modrinth 结果照常显示。
    /// </summary>
    private static readonly TimeSpan CurseForgeFillTimeout = TimeSpan.FromSeconds(45);

    // 和 CurseForge 一样每块 20 条：两边同步翻页，混排后每页都均衡
    private const int ModrinthChunk = 20;
    // 官方 API 的响应随 pageSize 线性变大（50 条 ≈ 685 KB ≈ 10 秒），20 条最平衡
    private const int CurseForgeChunk = 20;

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
        // 关键词化：先按中文译名表翻成英文（只对 Mod），再统一去标点、转小写。
        // 这样 "Create: Steam 'n' Rails" / "create steam n rails" / "CREATE-STEAM"
        // 都会变成同一串关键词，不会因为符号和大小写搜不到。
        var translated = string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase)
                         && TranslationTable.HasChinese(query)
            ? TranslationTable.TranslateToEnglish(query)
            : null;

        var effective = BuildQuery(query, translated);

        if (!string.IsNullOrWhiteSpace(translated)
            && !translated.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Info($"search translated: {query} -> {translated}（关键词：{effective}）");
        }

        var result = await SearchCoreAsync(platform, effective, projectType, gameVersion, loader,
            category, page, pageSize, sort, cancellationToken);

        // 关键词串太长导致一条都没有（比如把版本号、加载器也打进去了）：退回最长那个词再找一次
        if (result.Hits.Count > 0 || page > 0) return result;

        var fallback = SearchText.LongestToken(effective);

        if (string.IsNullOrWhiteSpace(fallback)
            || fallback.Equals(effective, StringComparison.OrdinalIgnoreCase))
            return result;

        AppLog.Info($"search fallback: {effective} -> {fallback}");

        return await SearchCoreAsync(platform, fallback, projectType, gameVersion, loader,
            category, page, pageSize, sort, cancellationToken);
    }

    /// <summary>把用户输入整理成「关键词串」：去过标点、统一小写，空的话退回原文。</summary>
    private static string BuildQuery(string raw, string? translated)
    {
        var source = string.IsNullOrWhiteSpace(translated) ? raw : translated;
        var normalized = SearchText.Normalize(source);

        if (normalized.Length > 0) return normalized;

        return (source ?? string.Empty).Trim();
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
            // 两边都抓「到这一页为止」的量：每页各补 20 条，翻到第 N 页时两边都有 N×20 条。
            // 以前 Modrinth 一块 100 条、CF 一块 20 条，CF 第一页就被用光，第 2 页起只剩 MD。
            var perSideTarget = Math.Min(AggregateMaxItems, (page + 1) * pageSize);

            await EnsureModrinthAsync(bucket, query, projectType, gameVersion, loader, category,
                sort, perSideTarget, cancellationToken).ConfigureAwait(false);

            await EnsureCurseForgeAsync(bucket, query, projectType, gameVersion, loader,
                perSideTarget, cancellationToken).ConfigureAwait(false);

            var sorted = sort switch
            {
                ResourceSort.Updated => bucket.Hits().OrderByDescending(hit => hit.Updated).ToList(),
                ResourceSort.Relevance => Interleave(bucket.Modrinth, bucket.CurseForge).ToList(),
                _ => bucket.Hits().OrderByDescending(hit => hit.Hit.Downloads).ToList(),
            };

            // 「同一资源两边各出现一次」：把同一个项目的两条结果编成一组挨在一起
            var groups = GroupHits(sorted);

            if (sort == ResourceSort.Updated)
                groups = groups.OrderByDescending(group => group.Max(hit => hit.Updated)).ToList();
            else if (sort == ResourceSort.Downloads)
                groups = groups.OrderByDescending(group => group.Max(hit => hit.Hit.Downloads)).ToList();

            var hits = TakeGroups(groups, page, pageSize);

            // 每个分组平均由不到两条结果合成，所以「能翻到第几页」要按条数折半算，
            // 否则分页条会给出根本抓不到数据的页码（点进去是空的）
            var servablePages = Math.Max(1, AggregateMaxItems / pageSize / 2);
            var cap = Math.Min(AggregateMaxPages, servablePages);

            var knownPages = Math.Max(1, (groups.Count + pageSize - 1) / pageSize);
            var apiPages = (int)Math.Ceiling(
                (bucket.ModrinthTotal + bucket.CurseForgeTotal) / (double)pageSize);

            // 两边都抓空了：页数就按实际拿到的分组算，别给出点进去是空的页码
            var exhausted = (bucket.ModrinthDone && bucket.CurseForgeDone)
                            || bucket.Count >= AggregateMaxItems;

            var pageCount = exhausted
                ? Math.Clamp(knownPages, 1, cap)
                : Math.Clamp(Math.Max(knownPages, Math.Min(apiPages, cap)), 1, cap);

            var pending = !bucket.CurseForgeDone
                          && bucket.CurseForgeTask is { IsCompleted: false };

            return new ResourceSearchPage(hits, bucket.ModrinthTotal + bucket.CurseForgeTotal,
                bucket.ModrinthTotal, bucket.CurseForgeTotal, pageCount, pending);
        }
        finally
        {
            bucket.Gate.Release();
        }
    }

    /// <summary>Modrinth 补抓到「这一页该有的条数」为止，每块 20 条。</summary>
    private static async Task EnsureModrinthAsync(AggregateBucket bucket, string query,
        string projectType, string? gameVersion, string? loader, string? category,
        ResourceSort sort, int target, CancellationToken cancellationToken)
    {
        var guard = 0;

        while (!bucket.ModrinthDone && bucket.Modrinth.Count < target
               && bucket.Count < AggregateMaxItems && guard++ < 8)
        {
            var before = bucket.Modrinth.Count;

            await FillModrinthAsync(bucket, query, projectType, gameVersion, loader, category, sort,
                cancellationToken).ConfigureAwait(false);

            if (bucket.Modrinth.Count == before) break;
        }
    }

    /// <summary>
    /// 抓一块 CurseForge。跟 Modrinth 一样等它抓完再出页面，不再按时间先返回 ——
    /// 之前那样做的结果是首屏永远只有 Modrinth。
    /// </summary>
    private static async Task EnsureCurseForgeAsync(AggregateBucket bucket, string query,
        string projectType, string? gameVersion, string? loader, int target,
        CancellationToken cancellationToken)
    {
        var guard = 0;

        while (!bucket.CurseForgeDone && CurseForgeCount(bucket) < target
               && bucket.Count < AggregateMaxItems && guard++ < 8)
        {
            Task? task;

            lock (bucket.Sync)
            {
                if (bucket.CurseForgeTask is null || bucket.CurseForgeTask.IsCompleted)
                {
                    bucket.CurseForgeTask = Task.Run(
                        () => FillCurseForgeAsync(bucket, query, projectType, gameVersion, loader,
                            bucket.Lifetime.Token),
                        CancellationToken.None);
                }

                task = bucket.CurseForgeTask;
            }

            if (task is null) return;

            try
            {
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "curseforge aggregate wait");
                return;
            }
        }
    }

    private static int CurseForgeCount(AggregateBucket bucket)
    {
        lock (bucket.Sync) return bucket.CurseForge.Count;
    }

    private static async Task FillCurseForgeAsync(AggregateBucket bucket, string query,
        string projectType, string? gameVersion, string? loader, CancellationToken cancellationToken)
    {
        int limit;

        lock (bucket.Sync)
        {
            limit = Math.Min(CurseForgeChunk, AggregateMaxItems - bucket.CurseForge.Count);
        }

        if (limit <= 0)
        {
            bucket.CurseForgeDone = true;
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CurseForgeFillTimeout);

        try
        {
            var page = 0;
            long total;

            lock (bucket.Sync)
            {
                page = bucket.CurseForgePage;
            }

            var result = await CurseForge.SearchAsync(query, ClassIdOf(projectType), gameVersion,
                LoaderTypeOf(loader), page, limit, 6, "desc", timeout.Token);

            lock (bucket.Sync)
            {
                total = result.Total;
                bucket.CurseForgeTotal = total;
                bucket.CurseForgePage = page + 1;
                bucket.CurseForge.AddRange(result.Mods.Select(mod => ToHit(mod, projectType)));
            }

            if (result.Mods.Count < limit) bucket.CurseForgeDone = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 45 秒还没回来：记一笔，不要永久放弃，下次翻页再试
            AppLog.Info("curseforge 聚合搜索超时，稍后重试");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Error(ex, "curseforge search in aggregate");
            bucket.CurseForgeDone = true;
        }
    }

    private static async Task FillModrinthAsync(AggregateBucket bucket, string query, string projectType,
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

        /// <summary>翻页互斥：一次只让一个请求改 bucket。</summary>
        public readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>CurseForge 是在后台线程里追加的，读写都得拿这把锁。</summary>
        public readonly object Sync = new();

        /// <summary>bucket 被淘汰时取消还在跑的 CurseForge 请求。</summary>
        public readonly CancellationTokenSource Lifetime = new();

        public int ModrinthOffset;
        public int CurseForgePage;
        public long ModrinthTotal;
        public long CurseForgeTotal;
        public bool ModrinthDone;
        public bool CurseForgeDone;

        /// <summary>后台正在跑的 CurseForge 抓取（同时只允许一个）。</summary>
        public Task? CurseForgeTask;



        public int Count
        {
            get
            {
                lock (Sync) return Modrinth.Count + CurseForge.Count;
            }
        }

        public IEnumerable<ResourceHit> Hits()
        {
            lock (Sync) return Modrinth.Concat(CurseForge).ToList();
        }
    }

    private static AggregateBucket GetBucket(string key)
    {
        if (AggregateCache.Count > 6)
        {
            foreach (var stale in AggregateCache.Values)
            {
                try { stale.Lifetime.Cancel(); }
                catch { /* 已经释放 */ }
            }

            AggregateCache.Clear();
        }

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