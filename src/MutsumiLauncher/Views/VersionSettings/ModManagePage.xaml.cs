using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

                var size = 0L;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch
                {
                    // 忽略单个文件读取失败
                }

                _items.Add(new ModFileItem
                {
                    FileName = name,
                    DisplayName = display,   // 先用文件名占位，后台解析 jar 元数据后替换
                    FullPath = file,
                    SizeText = InstanceService.FormatSize(size),
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

        var enabledCount = _items.Count(item => item.Enabled);
        AllFilterText.Text = $"全部 ({_items.Count})";
        EnabledFilterText.Text = $"启用 ({enabledCount})";
        DisabledFilterText.Text = $"禁用 ({_items.Count - enabledCount})";

        ApplyFilter();
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

        if (sha1Map.Count == 0)
        {
            owner?.ShowToast("没有可检查的 Mod。");
            return;
        }

        owner?.ShowToast("正在检查 Mod 更新…");
        var found = 0;

        // Modrinth：SHA-1 反查。update 接口只会返回**比本地新**、且匹配游戏版本/加载器的文件
        try
        {
            var updates = await new ModrinthClient().CheckUpdatesAsync(sha1Map.Keys.ToList(),
                info.GameVersion, LoaderKey(info.Loader));

            AppLog.Info($"modrinth update check: {updates.Count}/{sha1Map.Count} 命中");

            foreach (var (hash, version) in updates)
            {
                if (!sha1Map.TryGetValue(hash, out var item)) continue;
                if (!IsCompatible(version, info.GameVersion, info.Loader)) continue;
                if (MarkUpdate(item, version)) found++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth update check");
        }

        // CurseForge：指纹反查 → 按 mod 拉文件列表 → 取比本地新的那一个
        try
        {
            var curseForge = new CurseForgeClient();
            var matches = await curseForge.MatchFingerprintsAsync(fingerprintMap.Keys.ToList());

            AppLog.Info($"curseforge update check: {matches.Count}/{fingerprintMap.Count} 命中");

            foreach (var match in matches)
            {
                if (FindItem(fingerprintMap, match) is not { } item) continue;

                var files = await curseForge.GetFilesAsync(match.ModId,
                    info.GameVersion, LoaderType(info.Loader));

                // 本地那一版如果在列表里，拿它的发布时间做基准：只有更新的才提示
                var current = files.FirstOrDefault(file => file.Id == match.FileId);

                // 同一天的多个文件靠文件 ID 区分（CurseForge 的 ID 是递增的）
                var latest = files
                    .Where(file => file.Id != match.FileId && !string.IsNullOrWhiteSpace(file.DownloadUrl))
                    .OrderByDescending(file => file.FileDate)
                    .ThenByDescending(file => file.Id)
                    .FirstOrDefault();

                if (latest is null) continue;
                if (current is not null && !IsNewer(latest, current)) continue;

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

                if (MarkUpdate(item, version)) found++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "curseforge update check");
        }

        owner?.ShowToast(found == 0 ? "所有 Mod 都是最新的。" : $"发现 {found} 个可更新的 Mod。");
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
            var downloaded = await ResourceService.DownloadAsync(file, directory);

            // 原来是被禁用的，新文件保持 .disabled
            var finalName = item.Enabled ? file.Filename : file.Filename + ".disabled";
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
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "update mod " + item.FileName);
            owner?.ShowToast($"更新 {item.DisplayName} 失败：{ex.Message}");
            return false;
        }
    }

    private static string Sha1Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
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

        if (_query.Length > 0)
            items = items.Where(item =>
                item.DisplayName.Contains(_query, StringComparison.OrdinalIgnoreCase));

        var list = items.ToList();
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

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string filter }) return;
        _filter = filter;
        ApplyFilter();
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
    private static async Task LoadMetadataAsync(IReadOnlyList<ModFileItem> items)
    {
        using var semaphore = new SemaphoreSlim(4);

        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync();

            try
            {
                var display = await Task.Run(() =>
                    ModMetadata.DisplayNameFor(item.FullPath, item.DisplayName));

                if (string.IsNullOrWhiteSpace(display) || display == item.DisplayName) return;

                Application.Current?.Dispatcher.Invoke(() => item.DisplayName = display);
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

    private static async Task LoadIconsAsync(IReadOnlyList<ModFileItem> items)
    {
        // 1. jar 自带的图标优先（大多数 Mod 都有，不用联网）
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                // 只给还没图标的算，别覆盖已有结果（列表刷新时 item 可能是复用的）
                if (item.IconPath is not null) continue;

                var size = 0L;
                try
                {
                    size = new FileInfo(item.FullPath).Length;
                }
                catch
                {
                    // 读不到就当 0
                }

                var fromJar = ModIconLoader.TryIconFromJar(item.FullPath, size);
                if (fromJar is null) continue;

                var icon = fromJar;
                Application.Current?.Dispatcher.Invoke(() => item.IconPath = icon);
            }
        });

        // 2. 没有自带图标的，批量问 CurseForge
        try
        {
            var missing = items.Where(item => item.IconPath is null).ToList();
            if (missing.Count == 0) return;

            var query = missing.Select(item =>
            {
                var size = 0L;
                try
                {
                    size = new FileInfo(item.FullPath).Length;
                }
                catch
                {
                    // 读不到就当 0
                }

                return (ModIconLoader.CacheKeyFor(item.FullPath, size), item.FullPath, size);
            }).ToList();

            var curseForge = await ModIconLoader.LoadCurseForgeIconsAsync(query);

            if (curseForge.Count > 0)
            {
                var apply = items.Select(item =>
                {
                    var size = 0L;
                    try
                    {
                        size = new FileInfo(item.FullPath).Length;
                    }
                    catch
                    {
                        // 忽略
                    }

                    return (Item: item, Key: ModIconLoader.CacheKeyFor(item.FullPath, size));
                });

                foreach (var (item, key) in apply)
                {
                    if (curseForge.TryGetValue(key, out var icon) && item.IconPath is null)
                        await Application.Current.Dispatcher.InvokeAsync(() => item.IconPath = icon);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "curseforge icon batch");
        }

        using var gate = new SemaphoreSlim(4);

        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync();

            try
            {
                var size = 0L;

                try
                {
                    size = new FileInfo(item.FullPath).Length;
                }
                catch
                {
                    // 文件没了就跳过
                }

                var icon = await ModIconLoader.LoadAsync(item.FullPath, size, null);

                if (icon is not null)
                    await Application.Current.Dispatcher.InvokeAsync(() => item.IconPath = icon);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "mod icon list");
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
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

    private static Brush BrushFor(string name)
    {
        var hash = name.Aggregate(17, (current, c) => current * 31 + c);
        var index = Math.Abs(hash) % Palette.Length;
        return new SolidColorBrush(Palette[index]);
    }

    private void Toast(string message)
        => (Window.GetWindow(this) as MainWindow)?.ShowToast(message);
}