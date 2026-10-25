using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public partial class VersionDetailView : UserControl
{
    private VersionEntry? _entry;
    private Action? _onBack;
    private Action<string>? _toast;

    private string? _selectedLoader;
    private string? _selectedLoaderVersion;

    private readonly Dictionary<string, bool> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OptiFineBuild> _optiFineBuilds = new(StringComparer.Ordinal);
    private readonly ModrinthClient _modrinth = new();
    private readonly List<Button> _fabricApiChips = [];
    private ModrinthVersion? _selectedFabricApi;
    private bool _fabricApiLoaded;
    private readonly Dictionary<string, List<Button>> _chips = new(StringComparer.Ordinal);

    public VersionDetailView()
    {
        InitializeComponent();
    }

    public void Load(VersionEntry entry, Action onBack, Action<string> toast)
    {
        _entry = entry;
        _onBack = onBack;
        _toast = toast;

        VersionText.Text = entry.Name;
        InstanceNameBox.Text = entry.Name;
        VersionCaption.Text = entry.Caption;
        BreadcrumbText.Text = $"原版资源 / {entry.Name}";
        HintText.Text = string.Empty;

        _selectedLoader = null;
        _selectedLoaderVersion = null;
        _selectedFabricApi = null;
        _fabricApiLoaded = false;
        _fabricApiChips.Clear();
        FabricApiVersions.Children.Clear();
        FabricApiSection.Visibility = Visibility.Collapsed;
        _loaded.Clear();
        _chips.Clear();
        _optiFineBuilds.Clear();

        foreach (var kind in new[] { "fabric", "forge", "neoforge", "optifine" })
        {
            var wrap = VersionsPanel(kind);
            wrap?.Children.Clear();
            var panel = Panel(kind);
            if (panel is not null) panel.Visibility = Visibility.Collapsed;
            var chevron = Chevron(kind);
            if (chevron is not null) chevron.Text = "\uE76C";
        }

        FabricRow.IsEnabled = true;
        ForgeRow.IsEnabled = true;
        NeoForgeRow.IsEnabled = true;
        OptiFineRow.IsEnabled = true;

        FabricStatus.Text = HasLoader("fabric", entry.Name) ? "已安装" : "可以添加";
        ForgeStatus.Text = HasLoader("forge", entry.Name) ? "已安装" : "可以添加";
        NeoForgeStatus.Text = HasLoader("neoforge", entry.Name) ? "已安装" : "可以添加";
        OptiFineStatus.Text = HasOptiFine() ? "已安装" : "可以添加";
        StartButton.Content = "开始下载";
    }

    /// <summary>
    /// 这个游戏版本下装了某个加载器？两种命名都认：
    /// 加载器自己生成的 fabric-loader-0.16.0-1.21.1，以及合并后的 1.21.1-Fabric-0.20.1。
    /// </summary>
    private static bool HasLoader(string loader, string gameVersion)
    {
        if (!Directory.Exists(GamePaths.VersionsDir)) return false;

        try
        {
            return Directory.EnumerateDirectories(GamePaths.VersionsDir)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Any(name => name.Contains(gameVersion, StringComparison.OrdinalIgnoreCase)
                             && MatchesLoader(name, loader));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "scan loader instances");
            return false;
        }
    }

    private static bool MatchesLoader(string name, string loader) => loader switch
    {
        "fabric" => name.Contains("fabric", StringComparison.OrdinalIgnoreCase),
        "neoforge" => name.Contains("neoforge", StringComparison.OrdinalIgnoreCase),
        "forge" => name.Contains("forge", StringComparison.OrdinalIgnoreCase)
                   && !name.Contains("neoforge", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>已经装过 OptiFine？实例目录名里带 optifine 就算（合并后的实例名由用户决定）。</summary>
    private static bool HasOptiFine()
    {
        if (!Directory.Exists(GamePaths.VersionsDir)) return false;

        try
        {
            return Directory.EnumerateDirectories(GamePaths.VersionsDir)
                .Select(Path.GetFileName)
                .Any(name => name is not null
                             && name.Contains("optifine", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "scan optifine instances");
            return false;
        }
    }

    private Button Row(string kind) => kind switch
    {
        "fabric" => FabricRow,
        "forge" => ForgeRow,
        "optifine" => OptiFineRow,
        _ => NeoForgeRow,
    };

    private TextBlock? Status(string kind) => kind switch
    {
        "fabric" => FabricStatus,
        "forge" => ForgeStatus,
        "optifine" => OptiFineStatus,
        _ => NeoForgeStatus,
    };

    private TextBlock? Chevron(string kind) => kind switch
    {
        "fabric" => FabricChevron,
        "forge" => ForgeChevron,
        "optifine" => OptiFineChevron,
        _ => NeoForgeChevron,
    };

    private WrapPanel VersionsPanel(string kind) => kind switch
    {
        "fabric" => FabricVersions,
        "forge" => ForgeVersions,
        "optifine" => OptiFineVersions,
        _ => NeoForgeVersions,
    };

    private Border? Panel(string kind) => kind switch
    {
        "fabric" => FabricVersionsPanel,
        "forge" => ForgeVersionsPanel,
        "optifine" => OptiFineVersionsPanel,
        _ => NeoForgeVersionsPanel,
    };

    private TextBlock? Hint(string kind) => kind switch
    {
        "fabric" => FabricVersionsHint,
        "forge" => ForgeVersionsHint,
        "optifine" => OptiFineVersionsHint,
        _ => NeoForgeVersionsHint,
    };

    private string LoaderName(string kind) => kind switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "optifine" => "OptiFine",
        _ => "NeoForge",
    };

    private void Back_Click(object sender, RoutedEventArgs e) => _onBack?.Invoke();

    private async void Fabric_Click(object sender, RoutedEventArgs e) => await ToggleLoaderAsync("fabric");

    private async void Forge_Click(object sender, RoutedEventArgs e) => await ToggleLoaderAsync("forge");

    private async void NeoForge_Click(object sender, RoutedEventArgs e) => await ToggleLoaderAsync("neoforge");

    private async void OptiFine_Click(object sender, RoutedEventArgs e) => await ToggleLoaderAsync("optifine");

    private async Task ToggleLoaderAsync(string kind)
    {
        if (_selectedLoaderVersion is not null && _selectedLoader != kind) return;

        var panel = Panel(kind);
        var chevron = Chevron(kind);
        if (panel is null) return;

        if (panel.Visibility == Visibility.Visible)
        {
            panel.Visibility = Visibility.Collapsed;
            if (chevron is not null) chevron.Text = "\uE76C";
            return;
        }

        panel.Visibility = Visibility.Visible;
        if (chevron is not null) chevron.Text = "\uE70E";

        if (_loaded.TryGetValue(kind, out var loaded) && loaded) return;

        var hint = Hint(kind);
        if (hint is not null) hint.Text = $"正在获取 {LoaderName(kind)} 版本…";

        try
        {
            var versions = await LoadVersionsAsync(kind);
            var wrap = VersionsPanel(kind);
            wrap.Children.Clear();
            _chips[kind] = [];

            if (versions.Count == 0)
            {
                if (hint is not null) hint.Text = $"{LoaderName(kind)} 暂不支持这个游戏版本。";
                return;
            }

            if (hint is not null) hint.Text = $"选择 {LoaderName(kind)} 版本（共 {versions.Count} 个）：";

            foreach (var version in versions)
            {
                var chip = CreateChip(kind, version);
                wrap.Children.Add(chip);
                _chips[kind].Add(chip);
            }

            _loaded[kind] = true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"load {kind} versions");
            if (hint is not null) hint.Text = $"获取 {LoaderName(kind)} 版本失败：{ex.Message}";
        }
    }

    private async Task<IReadOnlyList<string>> LoadVersionsAsync(string kind)
    {
        if (kind == "fabric")
        {
            var entries = await FabricLoader.GetLoadersAsync(_entry!.Name);
            return entries.Select(item => item.Loader.Version).ToList();
        }

        if (kind == "optifine")
        {
            var builds = await OptiFineLoader.GetVersionsAsync(_entry!.Name);

            // 有正式版就先只列正式版；预览版只在没有正式版时兜底
            var stable = builds.Where(build => !build.IsPreview).ToList();
            var shown = stable.Count > 0 ? stable : builds;

            _optiFineBuilds.Clear();
            foreach (var build in shown) _optiFineBuilds[build.Display] = build;

            return shown.Select(build => build.Display).ToList();
        }

        var loaderKind = kind == "forge" ? LoaderKind.Forge : LoaderKind.NeoForge;
        return await ForgeLoader.GetVersionsAsync(loaderKind, _entry!.Name);
    }

    private Button CreateChip(string kind, string version)
    {
        var chip = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Content = new TextBlock
            {
                Text = version,
                FontSize = 12,
                Foreground = (Brush)FindResource("InkSoft"),
            },
            Tag = version,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };

        chip.Click += (_, _) => SelectLoader(kind, chip, version);
        return chip;
    }

    private void SelectLoader(string kind, Button chip, string version)
    {
        if (_chips.TryGetValue(kind, out var chips))
        {
            foreach (var item in chips)
            {
                var selected = item == chip;
                item.Background = selected ? (Brush)FindResource("Accent") : Brushes.Transparent;

                if (item.Content is TextBlock text)
                {
                    text.Foreground = selected ? Brushes.White : (Brush)FindResource("InkSoft");
                    text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
        }

        _selectedLoader = kind;
        _selectedLoaderVersion = version;

        // 选加载器后默认给一个不冲突的实例名，用户仍可修改
        InstanceNameBox.Text = $"{_entry?.Name}-{LoaderName(kind)}-{version}";

        FabricStatus.Text = "与所选加载器不互通";
        ForgeStatus.Text = "与所选加载器不互通";
        NeoForgeStatus.Text = "与所选加载器不互通";
        OptiFineStatus.Text = "与所选加载器不互通";
        if (Status(kind) is { } status) status.Text = $"已选 {version}";

        FabricRow.IsEnabled = kind == "fabric";
        ForgeRow.IsEnabled = kind == "forge";
        NeoForgeRow.IsEnabled = kind == "neoforge";
        OptiFineRow.IsEnabled = kind == "optifine";

        StartButton.Content = "开始下载";
        HintText.Text = $"已选择 {LoaderName(kind)} {version}，点击「开始下载」。";

        if (kind == "fabric")
            _ = LoadFabricApiAsync();
        else
            FabricApiSection.Visibility = Visibility.Collapsed;
    }

    /// <summary>安装完成后把新实例设为当前版本，并刷新启动页。</summary>
    private void NotifyInstalled(string id)
    {
        var config = AppConfig.Current;
        config.SelectedVersion = id;
        config.Save();

        if (Window.GetWindow(this) is MainWindow main)
            main.RefreshInstances();
    }

    private async Task LoadFabricApiAsync()
    {
        FabricApiSection.Visibility = Visibility.Visible;

        if (_fabricApiLoaded) return;
        _fabricApiLoaded = true;

        FabricApiHint.Text = "正在获取 Fabric API 版本…";
        FabricApiVersions.Children.Clear();
        _fabricApiChips.Clear();

        try
        {
            var versions = await _modrinth.GetVersionsAsync("fabric-api");
            var mc = _entry?.Name ?? string.Empty;

            var matching = versions
                .Where(v => v.GameVersions.Contains(mc))
                .Where(v => v.Loaders.Contains("fabric", StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(v => v.DatePublished)
                .Take(30)
                .ToList();

            AddFabricApiChip(null, "不安装");

            foreach (var version in matching)
                AddFabricApiChip(version, version.VersionNumber);

            if (matching.Count > 0)
                SelectFabricApi(matching[0], _fabricApiChips.Count > 1 ? _fabricApiChips[1] : null);
            else
                FabricApiHint.Text = "这个游戏版本没有可用的 Fabric API，将只安装加载器。";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load fabric api");
            FabricApiHint.Text = "获取 Fabric API 版本失败：" + ex.Message;
        }
    }

    private void AddFabricApiChip(ModrinthVersion? version, string label)
    {
        var chip = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Content = new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)FindResource("InkSoft") },
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };

        chip.Click += (_, _) => SelectFabricApi(version, chip);
        FabricApiVersions.Children.Add(chip);
        _fabricApiChips.Add(chip);
    }

    private void SelectFabricApi(ModrinthVersion? version, Button? chip)
    {
        _selectedFabricApi = version;

        foreach (var item in _fabricApiChips)
        {
            var selected = chip is not null ? item == chip : false;
            item.Background = selected ? (Brush)FindResource("Accent") : Brushes.Transparent;

            if (item.Content is TextBlock text)
            {
                text.Foreground = selected ? Brushes.White : (Brush)FindResource("InkSoft");
                text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        FabricApiHint.Text = version is null
            ? "Fabric API：不安装"
            : $"Fabric API：已选 {version.VersionNumber}";
    }

    private Progress<InstallProgress> CreateProgress(DownloadTask task) => new(p =>
    {
        ProgressText.Text = $"{_entry?.Name}：{p.Message}";
        ProgressFill.Width = ProgressTrack.ActualWidth * Math.Clamp(p.Percent, 0, 100) / 100.0;
        DownloadCenter.Report(task, p.Message, p.Percent, p.Bytes);
    });

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_entry is null) return;

        if (string.IsNullOrEmpty(_entry.Url))
        {
            // 版本清单没拿到时不再显示假数据，这里也不会再静默无反应
            _toast?.Invoke("缺少这个版本的下载地址，请回下载页刷新版本清单后重试。");
            return;
        }

        StartButton.IsEnabled = false;
        ProgressCard.Visibility = Visibility.Visible;
        ProgressText.Text = string.Empty;
        ProgressFill.Width = 0;

        var instanceName = InstanceNameBox.Text.Trim();
        if (instanceName.Length == 0) instanceName = _entry.Name;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            if (instanceName.Contains(invalid))
            {
                _toast?.Invoke("实例名称包含非法字符。");
                StartButton.IsEnabled = true;
                return;
            }
        }

        var title = string.IsNullOrEmpty(_selectedLoaderVersion)
            ? $"安装 {_entry.Name}"
            : $"安装 {_entry.Name} + {LoaderName(_selectedLoader!)} {_selectedLoaderVersion}";
        var task = DownloadCenter.Begin(title);
        task.RetryAction = () => StartDownload_Click(StartButton, new RoutedEventArgs());

        try
        {
            var progress = CreateProgress(task);

            if (!string.IsNullOrEmpty(_selectedLoaderVersion))
            {
                var vanillaJson = Path.Combine(GamePaths.VersionsDir, _entry.Name, $"{_entry.Name}.json");
                var vanillaWasMissing = !File.Exists(vanillaJson);

                if (vanillaWasMissing)
                {
                    ProgressText.Text = $"先安装原版 {_entry.Name}…";
                    await new VersionInstaller().InstallAsync(_entry.Name, _entry.Url, progress,
                        installAssets: false, cancellationToken: task.Token);
                }

                // 加载器先装到临时版本目录，再按 PCL2 的方式合并成一个完整实例
                var tempLoaderId = "mtl-merge-" + Guid.NewGuid().ToString("N")[..8];
                string loaderId;

                if (_selectedLoader == "optifine")
                {
                    if (!_optiFineBuilds.TryGetValue(_selectedLoaderVersion, out var build))
                        throw new InvalidOperationException("OptiFine 版本信息丢失，请重新选择。");

                    // OptiFine 安装器会往临时 .minecraft 里写版本，再由 InstanceMerger 合并成实例
                    loaderId = await new OptiFineLoader().InstallAsync(_entry.Name, build,
                        progress, tempLoaderId, task.Token);
                }
                else if (_selectedLoader == "fabric")
                {
                    loaderId = await new FabricLoader().InstallAsync(_entry.Name, _selectedLoaderVersion,
                        progress, tempLoaderId, task.Token);
                }
                else
                {
                    var kind = _selectedLoader == "forge" ? LoaderKind.Forge : LoaderKind.NeoForge;
                    loaderId = await new ForgeLoader().InstallAsync(kind, _entry.Name, _selectedLoaderVersion,
                        progress, tempLoaderId, task.Token);
                }

                var id = InstanceMerger.Merge(_entry.Name, loaderId, instanceName, progress,
                    useLoaderJar: _selectedLoader == "optifine");

                // 原版只是这次合并的临时依赖，合并完就删掉，避免版本列表里出现两个条目
                if (vanillaWasMissing && !instanceName.Equals(_entry.Name, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var vanillaDir = Path.Combine(GamePaths.VersionsDir, _entry.Name);
                        if (Directory.Exists(vanillaDir)) Directory.Delete(vanillaDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, "delete temp vanilla");
                    }
                }

                if (_selectedLoader == "fabric" && _selectedFabricApi is not null)
                {
                    var file = _selectedFabricApi.Files.FirstOrDefault(f => f.Primary)
                               ?? _selectedFabricApi.Files.FirstOrDefault();

                    if (file is not null)
                    {
                        ProgressText.Text = "下载 Fabric API…";
                        DownloadCenter.Report(task, "下载 Fabric API…", 96);

                        var modsDir = Path.Combine(InstanceService.GameDirFor(id), "mods");
                        Directory.CreateDirectory(modsDir);

                        await _modrinth.DownloadAsync(file.Url, file.Filename, modsDir,
                            file.Hashes?.Sha1, task.Token);
                    }
                }

                ProgressText.Text = $"已安装 {id}";
                if (Status(_selectedLoader!) is { } status) status.Text = "已安装";
                DownloadCenter.Complete(task, $"已安装 {id}");
                NotifyInstalled(id);
                _toast?.Invoke($"安装完成：{id}");
                return;
            }

            await new VersionInstaller().InstallAsync(_entry.Name, _entry.Url, progress,
                instanceName: instanceName, cancellationToken: task.Token);
            ProgressText.Text = $"{instanceName} 安装完成";
            HintText.Text = "可以在启动页切换到该版本并启动。";
            DownloadCenter.Complete(task, "安装完成");
            NotifyInstalled(instanceName);
            _toast?.Invoke($"{_entry.Name} 安装完成");
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "已取消安装";
            DownloadCenter.Complete(task, "已取消");
            _toast?.Invoke("已取消安装");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "install version");

            var reason = ex.Message;
            if (!string.IsNullOrWhiteSpace(ex.InnerException?.Message)
                && !reason.Contains(ex.InnerException.Message, StringComparison.Ordinal))
                reason += "（" + ex.InnerException.Message + "）";

            ProgressText.Text = "安装失败：" + reason;
            DownloadCenter.Complete(task, "失败：" + reason);
            _toast?.Invoke("安装失败：" + reason);
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }
}
