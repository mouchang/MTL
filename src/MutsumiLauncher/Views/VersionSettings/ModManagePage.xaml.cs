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
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x5B, 0x8C, 0x5A),
        Color.FromRgb(0x4F, 0x7E, 0xA8),
        Color.FromRgb(0xC9, 0x8A, 0x5B),
        Color.FromRgb(0x8C, 0x5B, 0xA8),
        Color.FromRgb(0xA8, 0x5B, 0x6D),
        Color.FromRgb(0x5B, 0xA8, 0x99),
    ];

    private string _version = string.Empty;
    private string _filter = "all";
    private string _query = string.Empty;
    private List<ModFileItem> _items = [];

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

        Refresh();
    }

    private string ModsDir => InstanceService.ModsDirFor(_version);

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
                    IconBrush = BrushFor(display),
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

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
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

        var matchedFiles = 0;
        var sha1Map = new Dictionary<string, ModFileItem>(StringComparer.OrdinalIgnoreCase);
        var fingerprintMap = new Dictionary<uint, ModFileItem>();

        await Task.Run(() =>
        {
            foreach (var item in _items)
            {
                try
                {
                    sha1Map[Sha1Of(item.FullPath)] = item;
                    fingerprintMap[CurseForgeClient.ComputeFingerprint(item.FullPath)] = item;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "hash mod " + item.FileName);
                }
            }
        });

        AppLog.Info($"check updates: hashed={sha1Map.Count} fingerprints={fingerprintMap.Count}");

        if (sha1Map.Count == 0)
        {
            owner?.ShowToast("没有可检查的 Mod。");
            return;
        }

        owner?.ShowToast("正在检查 Mod 更新…");
        StartBusy();
        var found = 0;

        // Modrinth：逐文件带上自己的 loaders 查更新（PCL2 同款 update_individual）。
        // 多加载器实例里，用实例 loader 统一过滤会误判。
        try
        {
            var queries = new List<(string Hash, IReadOnlyList<string> Loaders, string? GameVersion)>();

            foreach (var (hash, item) in sha1Map)
            {
                var loaders = ModMetadata.LoadersOf(item.FullPath);

                if (loaders.Count == 0)
                    loaders = [LoaderKey(info.Loader)];

                queries.Add((hash, loaders, info.GameVersion));
            }

            var updates = await new ModrinthClient().CheckUpdatesIndividualAsync(queries);

            AppLog.Info($"modrinth update check: {updates.Count}/{sha1Map.Count} 命中");
            matchedFiles = Math.Max(matchedFiles, updates.Count);

            foreach (var (hash, version) in updates)
            {
                if (!sha1Map.TryGetValue(hash, out var item)) continue;

                // update_individual 已经按每个文件自己的 loaders + 游戏版本筛过了，
                // 这里不再用实例级 loader 二次过滤（多加载器实例会误杀）。
                if (!MarkUpdate(item, version)) continue;

                found++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth update check");
        }

        // CurseForge：批量指纹反查 → 批量取工程信息（带 latestFiles）→ 批量取本地文件信息。
        // 原来是「每个 Mod 单独查一次文件列表」，一百多个 Mod 就是一百多次请求。
        try
        {
            var curseForge = new CurseForgeClient();
            var matches = await curseForge.MatchFingerprintsAsync(fingerprintMap.Keys.ToList());

            AppLog.Info($"curseforge update check: {matches.Count}/{fingerprintMap.Count} 命中");
            matchedFiles = Math.Max(matchedFiles, matches.Count);

            if (matches.Count > 0)
            {
                var modIds = matches.Select(match => match.ModId).Distinct().ToList();
                var fileIds = matches.Select(match => match.FileId).Distinct().ToList();

                var projects = await curseForge.GetModsWithLatestFilesAsync(modIds);
                var localFiles = await curseForge.GetFilesBatchAsync(fileIds);

                foreach (var match in matches)
                {
                    if (FindItem(fingerprintMap, match) is not { } item) continue;
                    if (!projects.TryGetValue(match.ModId, out var project)) continue;

                    localFiles.TryGetValue(match.FileId, out var local);

                    // 候选：同游戏版本、加载器匹配、且比本地新
                    var latest = project.Files
                        .Where(file => !string.IsNullOrWhiteSpace(file.DownloadUrl))
                        .Where(file => IsCompatible(file, info.GameVersion, info.Loader))
                        .Where(file => local is null || IsNewer(file, local))
                        .Where(file => file.Id != match.FileId)
                        .OrderByDescending(file => file.FileDate)
                        .ThenByDescending(file => file.Id)
                        .FirstOrDefault();

                    if (latest is null) continue;

                    var version = new ModrinthVersion
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
                    };

                    if (!MarkUpdate(item, version)) continue;

                    if (!string.IsNullOrWhiteSpace(project.Detail.WebsiteUrl))
                    {
                    }

                    found++;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "curseforge update check");
        }

        SaveUpdateCache();

        // 有更新才显示筛选标签
        UpdatesFilter.Visibility = found > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatesFilterText.Text = $"可更新 ({found})";

        if (found == 0 && UpdatesFilter.IsChecked == true)
        {
            UpdatesFilter.IsChecked = false;
            AllFilter.IsChecked = true;
        }

        ApplyFilter();

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

    /// <summary>把当前的可更新状态写盘，供下次打开列表直接还原。</summary>
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
                    file.Url, file.Filename, file.Size, file.Hashes?.Sha1, null);
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
    private static ModFileItem? FindItem(Dictionary<uint, ModFileItem> map, CurseForgeFingerprintMatch match)
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

        if (await ApplyUpdateAsync(item, owner))
        {
            owner?.ShowToast($"已更新 {item.DisplayName} → {item.Update!.VersionNumber}");
            Refresh();
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

        var done = 0;
        try
        {
            foreach (var item in targets)
            {
                owner?.ShowToast($"正在更新 {item.DisplayName}（{done + 1}/{targets.Count}）…");

                if (await ApplyUpdateAsync(item, owner)) done++;
            }
        }
        finally
        {
            if (sender is Button current) current.IsEnabled = true;
        }

        owner?.ShowToast(done == targets.Count
            ? $"已更新全部 {done} 个 Mod。"
            : $"更新完成 {done}/{targets.Count}，其余失败（详见日志）。");

        Refresh();
    }

    /// <summary>
    /// 下载并替换一个 Mod：保留原来的启用/禁用状态，装好之后删掉旧文件。
    /// 返回是否成功。
    /// </summary>
    private async Task<bool> ApplyUpdateAsync(ModFileItem item, MainWindow? owner)
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
            var downloaded = await ResourceService.DownloadAsync(file, directory, downloadName);

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
        var done = 0;

        SelectUpdateButton.IsEnabled = false;

        try
        {
            foreach (var item in targets)
            {
                owner?.ShowToast($"正在更新 {item.DisplayName}（{done + 1}/{targets.Count}）…");

                if (await ApplyUpdateAsync(item, owner)) done++;
            }
        }
        finally
        {
            SelectUpdateButton.IsEnabled = true;
        }

        owner?.ShowToast($"批量更新完成 {done}/{targets.Count}");
        Refresh();
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

    /// <summary>信息：按名字去 MC百科搜索（和 PCL2 的兜底一致）。</summary>
    private void InfoMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item }) return;

        var keyword = item.DisplayName;
        var url = "https://www.mcmod.cn/s?key=" + Uri.EscapeDataString(keyword);

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

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

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
                Application.Current?.Dispatcher.Invoke(() => item.IconPath = path);
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

        var confirm = MessageBox.Show(Window.GetWindow(this)!,
            $"确定删除「{item.DisplayName}」吗？此操作不可撤销。",
            "删除 Mod", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

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

    private static Brush BrushFor(string name)
    {
        var hash = name.Aggregate(17, (current, c) => current * 31 + c);
        var index = Math.Abs(hash) % Palette.Length;
        return new SolidColorBrush(Palette[index]);
    }

    private void Toast(string message)
        => (Window.GetWindow(this) as MainWindow)?.ShowToast(message);
}