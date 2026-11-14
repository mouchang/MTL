using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class ModManagePage : UserControl, IInstancePage
{
    private string _version = string.Empty;
    private string _filter = "all";
    private string _query = string.Empty;
    private List<ModFileItem> _items = [];

    // MODS 目录实时监听：外部增删文件、我们自己改扩展名都会触发刷新
    private FileSystemWatcher? _modsWatcher;
    private System.Windows.Threading.DispatcherTimer? _refreshDebounce;

    /// <summary>更新进行中：此时不再让监听器插进来刷新，避免打断选中状态。</summary>
    private bool _updating;

    public ModManagePage()
    {
        InitializeComponent();
    }

    public void SetInstance(string version)
    {
        _version = version;

        var info = InstanceService.GetInfo(version);
        var supported = !info.Loader.Equals("原版", StringComparison.OrdinalIgnoreCase);

        NoLoaderHint.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
        InstallNewModButton.IsEnabled = supported;
        InstallNewModButton.ToolTip = supported ? null : "原版没有加载器，无法安装 Mod";

        StartWatching(ModsDir);
        Refresh();
    }

    private string ModsDir => InstanceService.ModsDirFor(_version);

    /// <summary>
    /// 监听 mods 目录，文件增删 / 改名（启用禁用）后自动刷新列表。
    /// 用 400ms 防抖：一次下载完成会连着抛出好几个事件。
    /// </summary>
    private void StartWatching(string dir)
    {
        _modsWatcher?.Dispose();
        _modsWatcher = null;

        try
        {
            Directory.CreateDirectory(dir);

            _refreshDebounce ??= CreateDebounceTimer();

            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };

            watcher.Created += (_, _) => ScheduleRefresh();
            watcher.Deleted += (_, _) => ScheduleRefresh();
            watcher.Renamed += (_, _) => ScheduleRefresh();
            watcher.Changed += (_, _) => ScheduleRefresh();

            _modsWatcher = watcher;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "watch mods " + dir);
        }
    }

    private System.Windows.Threading.DispatcherTimer CreateDebounceTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_updating) Refresh();
        };

        return timer;
    }

    private void ScheduleRefresh()
    {
        if (_updating || _refreshDebounce is null) return;

        Dispatcher.Invoke(() =>
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        });
    }

    public void Refresh()
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var dir = ModsDir;
        Directory.CreateDirectory(dir);

        _items = [];

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                bool enabled;

                if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    enabled = true;
                else if (name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                    enabled = false;
                else
                    continue;

                var display = enabled
                    ? Path.GetFileNameWithoutExtension(name)
                    : Path.GetFileNameWithoutExtension(name[..^".disabled".Length]);

                // 大小留空，渲染后由后台补：目录里几百个 jar 时同步读会明显卡顿
                _items.Add(new ModFileItem
                {
                    FileName = name,
                    DisplayName = display,   // 先用文件名占位，后台解析 jar 元数据后替换
                    FullPath = file,
                    Enabled = enabled,
                    Initial = Initials(display),
                });
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "scan mods");
        }

        _items = _items
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplyCachedUpdates();

        foreach (var item in _items) item.SelectionChanged += OnSelectionChanged;

        SyncUpdateFilter();

        var enabledCount = _items.Count(item => item.Enabled);
        RefreshSelectionBar();

        // 筛选项按数据自动显隐：全部启用时不显示「启用」，没有禁用项时不显示「禁用」
        var anyCount = _items.Count;
        var enabledItems = enabledCount;
        var disabledItems = anyCount - enabledItems;

        AllFilterText.Text = $"全部 ({anyCount})";
        EnabledFilterText.Text = $"启用 ({enabledItems})";
        DisabledFilterText.Text = $"禁用 ({disabledItems})";

        EnabledFilter.Visibility = _filter == "enabled" || (enabledItems > 0 && enabledItems < anyCount)
            ? Visibility.Visible
            : Visibility.Collapsed;

        DisabledFilter.Visibility = _filter == "disabled" || disabledItems > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyFilter();

        // 界面已经出来了，大小和元数据在后台补
        _ = FillSizesAsync(_items);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync();

    /// <summary>打开 Mod 板块时自动跑一次（每个版本设置会话只跑一次，由外层控制）。</summary>
    public void AutoCheckUpdates()
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        AppLog.Info($"mod auto check: version={_version}");
        _ = CheckUpdatesAsync();
    }

    private async Task CheckUpdatesAsync()
    {
        var owner = Window.GetWindow(this) as MainWindow;

        if (string.IsNullOrWhiteSpace(_version)) return;

        var info = InstanceService.GetInfo(_version);
        if (info.Loader.Equals("原版", StringComparison.OrdinalIgnoreCase))
        {
            owner?.ShowToast("原版实例没有加载器，无法检查 Mod 更新。");
            return;
        }

        AppLog.Info($"check updates: version={_version} loader={info.Loader} items={_items.Count}");

        var sha1Map = new System.Collections.Concurrent.ConcurrentDictionary<string, ModFileItem>(
            StringComparer.OrdinalIgnoreCase);
        var fingerprintMap = new System.Collections.Concurrent.ConcurrentDictionary<uint, ModFileItem>();

        // 哈希是纯本地 CPU 活，串行算几百个 jar 会明显拖慢检查；按 4 路并行
        await Task.Run(() => Parallel.ForEach(_items,
            new ParallelOptions { MaxDegreeOfParallelism = 4 }, item =>
            {
                try
                {
                    var (sha1, fingerprint) = HashOf(item.FullPath);

                    sha1Map.TryAdd(sha1, item);
                    if (fingerprint != 0) fingerprintMap.TryAdd(fingerprint, item);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "hash mod " + item.FileName);
                }
            }));

        AppLog.Info($"check updates: hashed={sha1Map.Count} fingerprints={fingerprintMap.Count}");

        if (sha1Map.Count == 0)
        {
            owner?.ShowToast("没有可检查的 Mod。");
            return;
        }

        owner?.ShowToast("正在检查 Mod 更新…");
        StartBusy();

        var queries = BuildModrinthQueries(sha1Map, info);
        var modrinth = new ModrinthClient();
        var curseForge = new CurseForgeClient();

        // 两个平台并行查：原来是 Modrinth 等完再查 CurseForge，串行等两轮网络。
        // 参考 PCL-CE：两个平台各开一个线程分别查。
        var modrinthTask = Task.Run(() => modrinth.CheckUpdatesIndividualAsync(queries));
        var curseForgeTask = Task.Run(() => CheckCurseForgeAsync(curseForge, fingerprintMap, info));

        var found = 0;
        var matchedFiles = 0;

        try
        {
            await Task.WhenAll(modrinthTask, curseForgeTask);

            (found, matchedFiles) = MergeUpdates(sha1Map, modrinthTask.Result,
                curseForgeTask.Result, info);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "check updates");
        }
        finally
        {
            // 无论成功失败都要收起遮罩，否则界面会一直卡在「检查更新中」
            StopBusy();
        }

        SaveUpdateCache();

        // 有更新才显示筛选标签
        SyncUpdateFilter();

        MergeDownloadSources();

        var total = _items.Count;

        // 既没在 Modrinth 命中、也没在 CurseForge 命中的，就是查不到来源的
        var unknown = Math.Max(0, total - matchedFiles);

        var message = found == 0
            ? (unknown > 0 ? $"所有能识别的 Mod 都是最新的（{unknown} 个未找到来源）。" : "所有 Mod 都是最新的。")
            : unknown > 0
                ? $"发现 {found} 个可更新的 Mod（{unknown} 个未找到来源）。"
                : $"发现 {found} 个可更新的 Mod。";

        owner?.ShowToast(message);
    }

    /// <summary>测试版也参与更新检查（用户要求默认识别）。</summary>
    private static bool IncludeVersion(string? type) => true;

    /// <summary>把当前的可更新状态写盘，供下次打开列表直接还原。</summary>
    /// <summary>
    /// 同一版本经常在两个平台各有一份（同 SHA-1 或同文件名）。
    /// 把各自的下载地址收集起来互相补充，下载时就能自动换源。
    /// </summary>
    private void MergeDownloadSources()
    {
        var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string? KeyOf(ModFileItem item)
        {
            if (item.Update is not { } update) return null;

            var file = update.Files.FirstOrDefault(f => f.Primary) ?? update.Files.FirstOrDefault();
            if (file is null || string.IsNullOrWhiteSpace(file.Url)) return null;

            return file.Hashes?.Sha1 is { Length: > 0 } sha1
                ? "sha1:" + sha1
                : "name:" + file.Filename.ToLowerInvariant();
        }

        foreach (var item in _items)
        {
            if (KeyOf(item) is not { } key || item.Update is not { } update) continue;
            var file = update.Files.FirstOrDefault(f => f.Primary) ?? update.Files.FirstOrDefault();
            if (file is null) continue;

            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
            }

            if (!list.Contains(file.Url, StringComparer.OrdinalIgnoreCase)) list.Add(file.Url);
        }

        foreach (var item in _items)
        {
            if (KeyOf(item) is not { } key) continue;
            if (!byKey.TryGetValue(key, out var list) || list.Count <= 1) continue;

            item.ExtraDownloadUrls.Clear();

            foreach (var url in list) item.ExtraDownloadUrls.Add(url);
        }

        var merged = _items.Count(item => item.ExtraDownloadUrls.Count > 1);
        if (merged > 0) AppLog.Info($"mod updates: {merged} 条合并了多个下载源");
    }


    /// <summary>把本地哈希整理成 Modrinth 逐文件查询用的载荷。</summary>
    private List<(string Hash, IReadOnlyList<string> Loaders, string? GameVersion)> BuildModrinthQueries(
        System.Collections.Concurrent.ConcurrentDictionary<string, ModFileItem> sha1Map,
        InstanceInfo info)
    {
        var queries = new List<(string, IReadOnlyList<string>, string?)>();

        foreach (var (hash, item) in sha1Map)
        {
            var loader = ReadModMeta(item.FullPath).Loader;

            IReadOnlyList<string> loaders = string.IsNullOrWhiteSpace(loader)
                ? [LoaderKey(info.Loader)]
                : [LoaderKey(loader)];

            queries.Add((hash, loaders, info.GameVersion));
        }

        return queries;
    }

    /// <summary>CurseForge 检查：指纹 → 工程（含 latestFiles）→ 本地文件，三次批量请求。</summary>
    private static async Task<List<(ModFileItem Item, ModrinthVersion Version)>> CheckCurseForgeAsync(
        CurseForgeClient client,
        System.Collections.Concurrent.ConcurrentDictionary<uint, ModFileItem> fingerprintMap,
        InstanceInfo info)
    {
        var result = new List<(ModFileItem, ModrinthVersion)>();

        try
        {
            var matches = await client.MatchFingerprintsAsync(fingerprintMap.Keys.ToList());

            AppLog.Info($"curseforge update check: {matches.Count}/{fingerprintMap.Count} 命中");

            if (matches.Count == 0) return result;

            var modIds = matches.Select(match => match.ModId).Distinct().ToList();
            var fileIds = matches.Select(match => match.FileId).Distinct().ToList();

            var projects = await client.GetModsWithLatestFilesAsync(modIds);
            var localFiles = await client.GetFilesBatchAsync(fileIds);

            foreach (var match in matches)
            {
                if (FindItem(fingerprintMap, match) is not { } item) continue;
                if (!projects.TryGetValue(match.ModId, out var project)) continue;

                localFiles.TryGetValue(match.FileId, out var local);

                var latest = project.Files
                    .Where(file => !string.IsNullOrWhiteSpace(file.DownloadUrl))
                    .Where(file => IsCompatible(file, info.GameVersion, info.Loader))
                    .Where(file => local is null || IsNewer(file, local))
                    .Where(file => file.Id != match.FileId)
                    .OrderByDescending(file => file.FileDate)
                    .ThenByDescending(file => file.Id)
                    .FirstOrDefault();

                if (latest is null) continue;

                result.Add((item, new ModrinthVersion
                {
                    Name = latest.DisplayName,
                    VersionNumber = string.IsNullOrWhiteSpace(latest.DisplayName)
                        ? latest.FileName
                        : latest.DisplayName,
                    VersionType = latest.ReleaseType,
                    DatePublished = latest.FileDate,
                    GameVersions = latest.GameVersions.ToList(),
                    Loaders = latest.Loaders.ToList(),
                    Files =
                    [
                        new ModrinthFile
                        {
                            Url = latest.DownloadUrl,
                            Filename = latest.FileName,
                            Primary = true,
                            Size = latest.Length,
                            Hashes = new ModrinthHashes { Sha1 = latest.Sha1 },
                        },
                    ],
                }));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "curseforge update check");
        }

        return result;
    }

    /// <summary>把两个平台的结果合并到列表，返回可更新数量。</summary>
    private (int Found, int Matched) MergeUpdates(
        System.Collections.Concurrent.ConcurrentDictionary<string, ModFileItem> sha1Map,
        Dictionary<string, ModrinthVersion> modrinth,
        List<(ModFileItem Item, ModrinthVersion Version)> curseForge,
        InstanceInfo info)
    {
        var found = 0;
        var matched = new HashSet<ModFileItem>();

        AppLog.Info($"modrinth update check: {modrinth.Count}/{sha1Map.Count} 命中");

        foreach (var (hash, version) in modrinth)
        {
            if (!sha1Map.TryGetValue(hash, out var item)) continue;

            matched.Add(item);

            if (!IncludeVersion(version.VersionType)) continue;
            if (MarkUpdate(item, version)) found++;
        }

        foreach (var (item, version) in curseForge)
        {
            matched.Add(item);

            if (!IncludeVersion(version.VersionType)) continue;
            if (MarkUpdate(item, version)) found++;
        }

        return (found, matched.Count);
    }

    /// <summary>按当前列表重算「可更新」计数与显隐；没有可更新项时自动回到「全部」。</summary>
    private void SyncUpdateFilter()
    {
        var count = _items.Count(item => item.Update is not null);

        UpdatesFilterText.Text = "可更新 (" + count + ")";

        // 计数归零就藏起来；当前正好停在「可更新」时先切回「全部」
        if (count == 0 && _filter == "updates")
        {
            AllFilter.IsChecked = true;
        }

        UpdatesFilter.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ApplyFilter();
    }

    private void SaveUpdateCache()
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        try
        {
            var entries = new Dictionary<string, ModUpdateCache.Entry>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in _items)
            {
                if (item.Update is not { } update) continue;

                var file = update.Files.FirstOrDefault(f => f.Primary) ?? update.Files.FirstOrDefault();
                if (file is null || string.IsNullOrWhiteSpace(file.Url)) continue;

                long size = 0;

                try
                {
                    size = new FileInfo(item.FullPath).Length;
                }
                catch
                {
                    // 文件刚好被移走也无所谓，记录用而已
                }

                entries[ModUpdateCache.KeyOf(item.FileName)] = new ModUpdateCache.Entry(
                    update.Id, update.Name, update.VersionNumber, update.DatePublished,
                    file.Url, file.Filename, file.Size, file.Hashes?.Sha1);
            }

            ModUpdateCache.Save(_version, entries);
            AppLog.Info($"mod updates cache: 保存 {entries.Count} 条");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "save update cache");
        }
    }

    private static bool MarkUpdate(ModFileItem item, ModrinthVersion version)
    {
        var current = item.Update;

        if (current is not null && current.DatePublished >= version.DatePublished)
            return false;

        item.SetUpdate(version);
        return true;
    }

    /// <summary>把 CurseForge 的匹配结果对回本地文件：优先用回显的指纹，其次文件名。</summary>
    private static ModFileItem? FindItem(
        System.Collections.Concurrent.ConcurrentDictionary<uint, ModFileItem> map,
        CurseForgeFingerprintMatch match)
    {
        if (match.Fingerprint != 0 && map.TryGetValue(match.Fingerprint, out var byFingerprint))
            return byFingerprint;

        return map.Values.FirstOrDefault(item =>
            item.FileName.Equals(match.FileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>CurseForge 文件是否比本地那个新：先比发布时间，同一天再比 ID。</summary>
    /// <summary>CurseForge 文件是否兼容当前游戏版本与加载器。</summary>
    private static bool IsCompatible(CurseForgeFile file, string gameVersion, string loader)
    {
        var loaders = file.Loaders;

        if (loaders.Count > 0 && !loaders.Contains(LoaderKey(loader), StringComparer.OrdinalIgnoreCase))
            return false;

        return file.GameVersions.Contains(gameVersion);
    }

    private static bool IsNewer(CurseForgeFile candidate, CurseForgeFile current)
        => candidate.FileDate > current.FileDate
           || (candidate.FileDate == current.FileDate && candidate.Id > current.Id);

    /// <summary>返回的版本必须真的支持当前实例的游戏版本 / 加载器，不然装了也起不来。</summary>
    private static bool IsCompatible(ModrinthVersion version, string gameVersion, string loader)
    {
        if (version.Files.Count == 0) return false;

        if (version.GameVersions.Count > 0
            && !version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            return false;

        var key = LoaderKey(loader);

        if (version.Loaders.Count > 0
            && !version.Loaders.Contains(key, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>界面上的加载器名（NeoForge / Fabric…）转成接口用的 key（neoforge / fabric…）。</summary>
    private static string LoaderKey(string loader) => loader.ToLowerInvariant() switch
    {
        "neo forge" => "neoforge",
        _ => loader.ToLowerInvariant(),
    };

    private static int? LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        "forge" => 1,
        _ => null,
    };

    private async void UpdateMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item } || item.Update is null)
            return;

        var owner = Window.GetWindow(this) as MainWindow;

        // 单条更新也走下载面板，用户能看到百分比
        var task = DownloadCenter.Begin($"更新 {item.DisplayName}");
        var versionNumber = item.Update!.VersionNumber;

        _updating = true;
        ShowUpdateProgress($"正在更新 {item.DisplayName}");

        var progress = new Progress<DownloadProgress>(p =>
        {
            var percent = p.Total > 0 ? Math.Clamp(p.Received * 100.0 / p.Total, 0, 100) : 0;
            DownloadCenter.Report(task, $"正在下载 {item.DisplayName}", percent, p.Received);

            SetUpdateProgress(percent, $"正在更新 {item.DisplayName} · {percent:0}%");
        });

        try
        {
            if (await ApplyUpdateAsync(item, owner, progress))
            {
                DownloadCenter.Complete(task, "已更新");
                owner?.ShowToast($"已更新 {item.DisplayName} → {versionNumber}");
                Refresh();
            }
            else
            {
                DownloadCenter.Complete(task, "更新失败（详见日志）");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "update mod");
            DownloadCenter.Complete(task, "更新失败：" + ex.Message);
        }
        finally
        {
            HideUpdateProgress();
            _updating = false;

            // 单条更新后也回到顶部，和批量保持一致
            Refresh();
            ScrollToTop();
        }
    }

    /// <summary>批量更新：挨个走同一条更新流程，失败的跳过继续。</summary>
    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this) as MainWindow;
        var targets = _items.Where(item => item.Update is not null).ToList();

        if (targets.Count == 0)
        {
            owner?.ShowToast("没有可更新的 Mod。");
            return;
        }

        if (sender is Button button) button.IsEnabled = false;

        int done;

        try
        {
            done = await UpdateManyAsync(targets, owner, "更新 " + targets.Count + " 个 Mod");
        }
        finally
        {
            if (sender is Button current) current.IsEnabled = true;
        }

        owner?.ShowToast(done == targets.Count
            ? $"已更新全部 {done} 个 Mod。"
            : $"更新完成 {done}/{targets.Count}，其余失败（详见日志）。");

        Refresh();
        ScrollToTop();
    }

    /// <summary>批量更新：在下载面板里显示总体进度（第几个 / 百分比）。</summary>
    private async Task<int> UpdateManyAsync(List<ModFileItem> targets, MainWindow? owner, string title)
    {
        if (targets.Count == 0) return 0;

        var task = DownloadCenter.Begin(title);
        var done = 0;

        _updating = true;
        ShowUpdateProgress(title);

        try
        {
            foreach (var item in targets)
            {
                var index = done;
                var current = item;
                var captured = done + 1;

                var progress = new Progress<DownloadProgress>(p =>
                {
                    var sub = p.Total > 0 ? Math.Clamp(p.Received * 100.0 / p.Total, 0, 100) : 0;
                    var overall = (index + sub / 100.0) * 100 / targets.Count;

                    DownloadCenter.Report(task,
                        "正在更新 " + current.DisplayName + "（" + captured + "/" + targets.Count + "）",
                        overall, p.Received);

                    SetUpdateProgress(overall,
                        "正在更新 " + current.DisplayName + "（" + captured + "/" + targets.Count + " · " +
                        sub.ToString("0") + "%）");
                });

                owner?.ShowToast("正在更新 " + item.DisplayName + "（" + captured + "/" + targets.Count + "）…");

                if (await ApplyUpdateAsync(item, owner, progress)) done++;
            }
        }
        finally
        {
            DownloadCenter.Complete(task, "更新完成 " + done + "/" + targets.Count);
            HideUpdateProgress();
            _updating = false;
        }

        return done;
    }

    /// <summary>
    /// 下载并替换一个 Mod：保留原来的启用/禁用状态，装好之后删掉旧文件。
    /// 返回是否成功。
    /// </summary>
    private async Task<bool> ApplyUpdateAsync(ModFileItem item, MainWindow? owner,
        IProgress<DownloadProgress>? progress = null)
    {
        if (item.Update is not { } version) return false;

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();

        if (file is null || string.IsNullOrWhiteSpace(file.Url))
        {
            owner?.ShowToast($"{item.DisplayName}：这个版本没有可下载的文件。");
            return false;
        }

        try
        {
            var directory = ModsDir;

            // 和单独下载一致：文件名前加中文名，方便在资源管理器里认
            var downloadName = PrefixedFileName(item, file.Filename);

            // 主下载源 + 合并进来的备用源一起交给下载引擎，失败能自动换源
            var urls = new List<string> { file.Url };
            urls.AddRange(item.ExtraDownloadUrls.Where(url => !string.IsNullOrWhiteSpace(url)));
            urls.AddRange(DownloadSources.ModFile(file.Url).Select(endpoint => endpoint.Url));

            var target = Path.Combine(directory, downloadName);

            await DownloadEngine.DownloadAsync(urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                target, file.Hashes?.Sha1, file.Size > 0 ? file.Size : null, progress);

            var downloaded = target;

            // 原来是被禁用的，新文件保持 .disabled
            var finalName = item.Enabled ? downloadName : downloadName + ".disabled";
            var finalPath = Path.Combine(directory, finalName);

            if (!string.Equals(Path.GetFullPath(downloaded), Path.GetFullPath(finalPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(downloaded, finalPath);
            }

            // 旧文件（同名或改名过）都清掉，避免两个版本共存
            if (!string.Equals(Path.GetFullPath(item.FullPath), Path.GetFullPath(finalPath),
                    StringComparison.OrdinalIgnoreCase)
                && File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
            }

            AppLog.Info($"mod updated: {item.FileName} -> {finalName}");

            // 更新成功：把这条从持久化缓存里删掉，免得刷新后又冒出旧的「可更新」
            item.ExtraDownloadUrls.Clear();
            item.SetUpdate(null);
            RemoveCachedUpdate(item);

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "update mod " + item.FileName);
            owner?.ShowToast($"更新 {item.DisplayName} 失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>更新完成后移除对应的缓存条目。</summary>
    private void RemoveCachedUpdate(ModFileItem item)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        try
        {
            var cached = ModUpdateCache.Load(_version);
            if (cached.Count == 0) return;

            if (cached.Remove(ModUpdateCache.KeyOf(item.FileName)))
                ModUpdateCache.Save(_version, cached);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "remove update cache");
        }
    }

    private static string Sha1Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>把上次检查更新的结果套回列表，刷新/重开实例后徽标不会消失。</summary>
    private void ApplyCachedUpdates()
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var cached = ModUpdateCache.Load(_version);
        if (cached.Count == 0) return;

        var applied = 0;

        foreach (var item in _items)
        {
            if (!cached.TryGetValue(ModUpdateCache.KeyOf(item.FileName), out var entry)) continue;
            if (entry.DownloadUrl is null) continue;

            item.SetUpdate(new ModrinthVersion
            {
                Id = entry.VersionId,
                Name = entry.Name,
                VersionNumber = entry.VersionNumber,
                VersionType = "release",
                DatePublished = entry.DatePublished,
                GameVersions = [],
                Loaders = [],
                Files =
                [
                    new ModrinthFile
                    {
                        Url = entry.DownloadUrl,
                        Filename = entry.FileName,
                        Primary = true,
                        Size = entry.Size,
                        Hashes = new ModrinthHashes { Sha1 = entry.Sha1 },
                    },
                ],
            });

            applied++;
        }

        if (applied > 0) AppLog.Info($"mod updates cache: 还原 {applied} 条");
    }

    // ---------- 更新进度 ----------

    private void ShowUpdateProgress(string text)
    {
        UpdateProgressText.Text = text;
        UpdateProgressValue.Value = 0;
        UpdateProgressBar.Visibility = Visibility.Visible;
    }

    private void SetUpdateProgress(double percent, string text)
    {
        UpdateProgressValue.Value = Math.Clamp(percent, 0, 100);
        UpdateProgressText.Text = text;
    }

    private void HideUpdateProgress() => UpdateProgressBar.Visibility = Visibility.Collapsed;

    // ---------- 多选与批量操作 ----------

    private List<ModFileItem> SelectedItems => _items.Where(item => item.IsSelected).ToList();

    /// <summary>选中状态变化后刷新底部栏。</summary>
    private void OnSelectionChanged(object? sender, EventArgs e) => RefreshSelectionBar();

    private void RefreshSelectionBar()
    {
        var selected = SelectedItems;

        SelectionBar.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionText.Text = $"已选择 {selected.Count} 个文件";

        SelectEnableButton.IsEnabled = selected.Any(item => !item.Enabled);
        SelectDisableButton.IsEnabled = selected.Any(item => item.Enabled);
        SelectUpdateButton.IsEnabled = selected.Any(item => item.CanUpdate);
        SelectDeleteButton.IsEnabled = selected.Count > 0;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items) item.IsSelected = false;

        RefreshSelectionBar();
    }

    private async void BatchEnable_Click(object sender, RoutedEventArgs e)
        => await SetEnabledAsync(SelectedItems.Where(item => !item.Enabled).ToList(), true);

    private async void BatchDisable_Click(object sender, RoutedEventArgs e)
        => await SetEnabledAsync(SelectedItems.Where(item => item.Enabled).ToList(), false);

    /// <summary>批量启用/禁用：改扩展名，和单条操作走同一套规则。</summary>
    private async Task SetEnabledAsync(List<ModFileItem> items, bool enabled)
    {
        if (items.Count == 0) return;

        var owner = Window.GetWindow(this) as MainWindow;

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    var target = enabled
                        ? item.FullPath[..^".disabled".Length]
                        : item.FullPath + ".disabled";

                    if (string.Equals(target, item.FullPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(item.FullPath)) continue;

                    File.Move(item.FullPath, target, overwrite: true);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "batch toggle " + item.FileName);
                }
            }
        });

        owner?.ShowToast($"已{(enabled ? "启用" : "禁用")} {items.Count} 个 Mod");
        Refresh();
    }

    private async void BatchUpdate_Click(object sender, RoutedEventArgs e)
    {
        var targets = SelectedItems.Where(item => item.CanUpdate).ToList();
        if (targets.Count == 0) return;

        var owner = Window.GetWindow(this) as MainWindow;

        SelectUpdateButton.IsEnabled = false;

        try
        {
            await UpdateManyAsync(targets, owner, "更新 " + targets.Count + " 个 Mod");
        }
        finally
        {
            SelectUpdateButton.IsEnabled = true;
        }

        Refresh();
        ScrollToTop();
    }

    private void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        var targets = SelectedItems;
        if (targets.Count == 0) return;

        var owner = Window.GetWindow(this);
        var answer = MessageBox.Show(owner!, $"确定删除选中的 {targets.Count} 个 Mod 文件吗？此操作不可撤销。",
            "删除 Mod", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK) return;

        var failed = 0;

        foreach (var item in targets)
        {
            try
            {
                File.Delete(item.FullPath);
            }
            catch (Exception ex)
            {
                failed++;
                AppLog.Error(ex, "batch delete " + item.FileName);
            }
        }

        (owner as MainWindow)?.ShowToast(failed == 0
            ? $"已删除 {targets.Count} 个 Mod"
            : $"删除完成，{failed} 个失败（详见日志）");

        Refresh();
    }

    /// <summary>在资源管理器里定位这个文件。</summary>
    private void RevealMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item }) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.FullPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "reveal mod");
            Toast("打开资源管理器失败。");
        }
    }

    /// <summary>信息：打不开详情页时按名字去 MC百科搜索（参考 PCL-CE）。</summary>
    private void InfoMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item }) return;

        // 全名在百科上常常搜不到（比如 C2ME 的英文全名就是无结果的），
        // 优先用索引里的简称，其次中文名。
        // 索引里能精确命中就直接进详情页（和资源模块一致，参考 PCL-CE），
        // 命中不了才退回搜索——宁可多点一次，也别链到错误的条目上。
        var url = McmodIndex.FindExact(null, item.DisplayName) is { } match
            ? McmodService.ClassUrl(match.Id)
            : "https://www.mcmod.cn/s?key=" + Uri.EscapeDataString(item.DisplayName);

        var error = UrlLauncher.Open(url);
        if (error is not null) Toast(error);
    }

    private void ApplyFilter()
    {
        if (ModList is null) return;

        IEnumerable<ModFileItem> items = _items;

        items = _filter switch
        {
            "enabled" => items.Where(item => item.Enabled),
            "disabled" => items.Where(item => !item.Enabled),
            _ => items,
        };

        if (_filter == "updates")
            items = items.Where(item => item.Update is not null);

        if (_query.Length > 0)
            items = items.Where(item =>
                item.DisplayName.Contains(_query, StringComparison.OrdinalIgnoreCase));

        var list = items.ToList();

        AppLog.Info($"mod filter: {_filter} 总={_items.Count} 可更新={_items.Count(item => item.Update is not null)} 显示={list.Count}");

        ModList.ItemsSource = list;
        _ = LoadMetadataAsync(list);
        _ = LoadIconsAsync(list);
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _items.Count == 0
            ? "这个实例还没有安装 Mod。"
            : "没有符合条件的 Mod。";
    }

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        _query = SearchBox.Text.Trim();
        ApplyFilter();
    }

    /// <summary>遮罩里的滑块动画：直接对 TranslateTransform 做动画，不经 Storyboard 的名字解析。</summary>
    private void StartBusy()
    {
        CheckingOverlay.Visibility = Visibility.Visible;

        var animation = new DoubleAnimation(-64, 220, TimeSpan.FromSeconds(1.1))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        BusyTranslate.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void StopBusy()
    {
        BusyTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        CheckingOverlay.Visibility = Visibility.Collapsed;
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string filter }) return;
        _filter = filter;
        ApplyFilter();
    }

    /// <summary>「[中文名] 原文件名」，没有中文名就用原名。</summary>
    private static string PrefixedFileName(ModFileItem item, string fileName)
    {
        var display = item.DisplayName?.Trim();

        if (string.IsNullOrWhiteSpace(display) || display.Length < 2) return fileName;
        if (display.StartsWith("[", StringComparison.Ordinal)) return fileName;
        if (fileName.StartsWith(display, StringComparison.OrdinalIgnoreCase)) return fileName;

        // 显示名本身就是文件名（没有中文名）时别加前缀
        if (string.Equals(display, Path.GetFileNameWithoutExtension(fileName),
                StringComparison.OrdinalIgnoreCase))
            return fileName;

        return $"[{display}] {fileName}";
    }

    private void InstallNewMod_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var info = InstanceService.GetInfo(_version);
        var gameVersion = info.GameVersion;

        var loader = info.Loader switch
        {
            "Fabric" => "fabric",
            "Forge" => "forge",
            "NeoForge" => "neoforge",
            _ => null,
        };

        (Window.GetWindow(this) as MainWindow)?.OpenCommunityMod(_version, gameVersion, loader);
    }

    /// <summary>后台给列表补图标：先读 jar 里的，再按 SHA-1 查 Modrinth（走内置代理）。</summary>
    /// <summary>
    /// 后台读 jar 元数据，把显示名从文件名换成真正的 Mod 名（有 MC百科中文名就用中文）。
    /// 并发 4，别一口气开几百个 zip。
    /// </summary>
    /// <summary>jar 元数据缓存：按「路径 + 大小 + 修改时间」记，刷新列表不用重读。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Version,
        string Loader)> MetaCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 后台补文件大小。列表先渲染、再填这一行，
    /// 这样几百个 Mod 的目录也不会在打开瞬间卡住。
    /// </summary>
    private static async Task FillSizesAsync(IReadOnlyList<ModFileItem> items)
    {
        using var semaphore = new SemaphoreSlim(8);

        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync();

            try
            {
                var size = await Task.Run(() =>
                {
                    try
                    {
                        return new FileInfo(item.FullPath).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });

                if (size <= 0) return;

                var text = InstanceService.FormatSize(size);
                Application.Current?.Dispatcher.Invoke(() => item.SizeText = text);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 本地哈希缓存（SHA-1 + Murmur2）。参考 PCL-CE：它把 Murmur 结果写进 INI，
    /// 文件没变就不重复算——54 个 Mod 第一次约 0.3s，之后基本瞬时。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Sha1, uint
        Fingerprint)> HashCache = new(StringComparer.OrdinalIgnoreCase);

    private static (string Sha1, uint Fingerprint) HashOf(string path)
    {
        string key;

        try
        {
            var info = new FileInfo(path);
            key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return (Sha1Of(path), CurseForgeClient.ComputeFingerprint(path));
        }

        if (HashCache.TryGetValue(key, out var cached)) return cached;

        var sha1 = Sha1Of(path);
        var fingerprint = CurseForgeClient.ComputeFingerprint(path);

        HashCache[key] = (sha1, fingerprint);
        return (sha1, fingerprint);
    }

    /// <summary>读版本号与加载器（带缓存）。</summary>
    private static (string Version, string Loader) ReadModMeta(string path)
    {
        string key;

        try
        {
            var info = new FileInfo(path);
            key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return (string.Empty, string.Empty);
        }

        if (MetaCache.TryGetValue(key, out var cached)) return (cached.Version, cached.Loader);

        var version = string.Empty;
        var loader = string.Empty;

        try
        {
            version = ModMetadata.Read(path)?.Version ?? string.Empty;

            var loaders = ModMetadata.LoadersOf(path);

            loader = loaders.Count == 0 ? string.Empty : loaders[0] switch
            {
                "fabric" => "Fabric",
                "forge" => "Forge",
                "neoforge" => "NeoForge",
                "quilt" => "Quilt",
                var other => other,
            };
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "read mod meta " + path);
        }

        MetaCache[key] = (version, loader);
        return (version, loader);
    }

    private static async Task LoadMetadataAsync(IReadOnlyList<ModFileItem> items)
    {
        using var semaphore = new SemaphoreSlim(4);

        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync();

            try
            {
                var (display, version, loader) = await Task.Run(() =>
                {
                    var name = ModMetadata.DisplayNameFor(item.FullPath, item.DisplayName);
                    var (metaVersion, metaLoader) = ReadModMeta(item.FullPath);

                    return (name, metaVersion, metaLoader);
                });

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(display)) item.DisplayName = display;
                    if (!string.IsNullOrWhiteSpace(loader)) item.LoaderText = loader;
                    if (!string.IsNullOrWhiteSpace(version)) item.VersionText = version;
                });
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "load mod metadata");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 图标分两步：先读 jar 自带的（本地、秒出），剩下的再走网络兜底
    /// （CurseForge 指纹 → Modrinth SHA-1）。网络那步不阻塞列表显示。
    /// </summary>
    private static async Task LoadIconsAsync(IReadOnlyList<ModFileItem> items)
    {
        // 1. 本地：jar 里的 logoFile / assets/*/icon.png / logo.png
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (item.IconPath is not null) continue;

                var size = TrySize(item.FullPath);
                var icon = ModIconLoader.TryIconFromJar(item.FullPath, size);
                if (icon is null) continue;

                var path = icon;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    item.IconPath = path;

                    // 透明 PNG 不该透出随机色块；底色透明后自然显示界面背景
                    item.IconBrush = System.Windows.Media.Brushes.Transparent;
                });
            }
        });

        var missing = items.Where(item => item.IconPath is null).ToList();
        if (missing.Count == 0) return;

        // 2. CurseForge 批量反查（一次请求最多 1000 个指纹）
        try
        {
            var query = missing
                .Select(item => (ModIconLoader.CacheKeyFor(item.FullPath, TrySize(item.FullPath)),
                    item.FullPath, TrySize(item.FullPath)))
                .ToList();

            var curseForge = await ModIconLoader.LoadCurseForgeIconsAsync(query);

            foreach (var item in missing)
            {
                var key = ModIconLoader.CacheKeyFor(item.FullPath, TrySize(item.FullPath));

                if (!curseForge.TryGetValue(key, out var icon)) continue;

                await Application.Current.Dispatcher.InvokeAsync(() => item.IconPath = icon);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "curseforge icon batch");
        }

        // 3. 还没图标的按 SHA-1 去 Modrinth 查
        using var gate = new SemaphoreSlim(4);

        var tasks = items.Where(item => item.IconPath is null).Select(async item =>
        {
            await gate.WaitAsync();

            try
            {
                var icon = await ModIconLoader.TryModrinthAsync(item.FullPath, TrySize(item.FullPath));

                if (icon is not null)
                    await Application.Current.Dispatcher.InvokeAsync(() => item.IconPath = icon);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "modrinth icon");
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static long TrySize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        InstanceService.OpenFolder(ModsDir);
    }

    private void InstallFromFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要安装的 Mod",
            Filter = "Mod 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true) return;

        var dir = ModsDir;
        Directory.CreateDirectory(dir);

        var copied = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var target = Path.Combine(dir, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "copy mod");
                Toast("复制失败：" + ex.Message);
            }
        }

        Refresh();
        if (copied > 0) Toast($"已安装 {copied} 个 Mod");
    }

    private void ToggleMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item }) return;

        try
        {
            if (item.Enabled)
            {
                var target = item.FullPath + ".disabled";
                if (File.Exists(target)) File.Delete(target);
                File.Move(item.FullPath, target);
            }
            else
            {
                var target = item.FullPath[..^".disabled".Length];
                if (File.Exists(target)) File.Delete(target);
                File.Move(item.FullPath, target);
            }

            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "toggle mod");
            Toast("操作失败：" + ex.Message);
        }
    }

    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item }) return;

        var owner = Window.GetWindow(this);
        if (!MessageDialog.Show(owner, "删除 Mod", $"确定删除「{item.DisplayName}」吗？", "删除")) return;

        try
        {
            File.Delete(item.FullPath);
            Refresh();
            Toast("已删除 " + item.DisplayName);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete mod");
            Toast("删除失败：" + ex.Message);
        }
    }

    private static string Initials(string name)
    {
        var cleaned = name.Trim();
        if (cleaned.Length == 0) return "M";

        var first = char.ToUpperInvariant(cleaned[0]);
        return first.ToString();
    }

    private void Toast(string message)
        => (Window.GetWindow(this) as MainWindow)?.ShowToast(message);
}