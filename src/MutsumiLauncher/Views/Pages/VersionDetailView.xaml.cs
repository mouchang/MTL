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
        _loaded.Clear();
        _chips.Clear();

        foreach (var kind in new[] { "fabric", "forge", "neoforge" })
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
        OptiFineRow.IsEnabled = false;

        FabricStatus.Text = HasLoader("fabric-loader-", entry.Name) ? "已安装" : "可以添加";
        ForgeStatus.Text = HasLoader("forge-", entry.Name) ? "已安装" : "可以添加";
        NeoForgeStatus.Text = HasLoader("neoforge-", entry.Name) ? "已安装" : "可以添加";
        StartButton.Content = "开始下载";
    }

    private static bool HasLoader(string prefix, string gameVersion)
    {
        if (!Directory.Exists(GamePaths.VersionsDir)) return false;

        return Directory.EnumerateDirectories(GamePaths.VersionsDir)
            .Select(Path.GetFileName)
            .Any(name => name is not null
                         && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                         && name.EndsWith("-" + gameVersion, StringComparison.OrdinalIgnoreCase)
                         || name is not null
                         && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                         && name.Contains(gameVersion, StringComparison.OrdinalIgnoreCase));
    }

    private Button Row(string kind) => kind switch
    {
        "fabric" => FabricRow,
        "forge" => ForgeRow,
        _ => NeoForgeRow,
    };

    private TextBlock? Status(string kind) => kind switch
    {
        "fabric" => FabricStatus,
        "forge" => ForgeStatus,
        _ => NeoForgeStatus,
    };

    private TextBlock? Chevron(string kind) => kind switch
    {
        "fabric" => FabricChevron,
        "forge" => ForgeChevron,
        _ => NeoForgeChevron,
    };

    private WrapPanel VersionsPanel(string kind) => kind switch
    {
        "fabric" => FabricVersions,
        "forge" => ForgeVersions,
        _ => NeoForgeVersions,
    };

    private Border? Panel(string kind) => kind switch
    {
        "fabric" => FabricVersionsPanel,
        "forge" => ForgeVersionsPanel,
        _ => NeoForgeVersionsPanel,
    };

    private TextBlock? Hint(string kind) => kind switch
    {
        "fabric" => FabricVersionsHint,
        "forge" => ForgeVersionsHint,
        _ => NeoForgeVersionsHint,
    };

    private string LoaderName(string kind) => kind switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        _ => "NeoForge",
    };

    private void Back_Click(object sender, RoutedEventArgs e) => _onBack?.Invoke();

    private async void Fabric_Click(object sender, RoutedEventArgs e) => await ToggleLoaderAsync("fabric");

    private async void Forge_Click(object sender, RoutedEventArgs e) => await ToggleLoaderAsync("forge");

    private async void NeoForge_Click(object sender, RoutedEventArgs e) => await ToggleLoaderAsync("neoforge");

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
        if (Status(kind) is { } status) status.Text = $"已选 {version}";

        FabricRow.IsEnabled = kind == "fabric";
        ForgeRow.IsEnabled = kind == "forge";
        NeoForgeRow.IsEnabled = kind == "neoforge";
        OptiFineRow.IsEnabled = false;

        StartButton.Content = "开始下载";
        HintText.Text = $"已选择 {LoaderName(kind)} {version}，点击「开始下载」。";
    }

    private void UnsupportedLoader_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLoaderVersion is not null) return;
        _toast?.Invoke("OptiFine 还没接入，目前支持 Fabric / Forge / NeoForge。");
    }

    private Progress<InstallProgress> CreateProgress(DownloadTask task) => new(p =>
    {
        ProgressText.Text = $"{_entry?.Name}：{p.Message}";
        ProgressFill.Width = ProgressTrack.ActualWidth * Math.Clamp(p.Percent, 0, 100) / 100.0;
        DownloadCenter.Report(task, p.Message, p.Percent, p.Bytes);
    });

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_entry is null || string.IsNullOrEmpty(_entry.Url)) return;

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

        try
        {
            var progress = CreateProgress(task);

            if (!string.IsNullOrEmpty(_selectedLoaderVersion))
            {
                var vanillaJson = Path.Combine(GamePaths.VersionsDir, _entry.Name, $"{_entry.Name}.json");
                if (!File.Exists(vanillaJson))
                {
                    ProgressText.Text = $"先安装原版 {_entry.Name}…";
                    await new VersionInstaller().InstallAsync(_entry.Name, _entry.Url, progress,
                        installAssets: false, instanceName: instanceName);
                }

                string id;
                if (_selectedLoader == "fabric")
                {
                    id = await new FabricLoader().InstallAsync(_entry.Name, _selectedLoaderVersion, progress,
                        instanceName);
                }
                else
                {
                    var kind = _selectedLoader == "forge" ? LoaderKind.Forge : LoaderKind.NeoForge;
                    id = await new ForgeLoader().InstallAsync(kind, _entry.Name, _selectedLoaderVersion,
                        progress, instanceName);
                }

                ProgressText.Text = $"已安装 {id}";
                if (Status(_selectedLoader!) is { } status) status.Text = "已安装";
                DownloadCenter.Complete(task, $"已安装 {id}");
                _toast?.Invoke($"安装完成：{id}");
                return;
            }

            await new VersionInstaller().InstallAsync(_entry.Name, _entry.Url, progress,
                instanceName: instanceName);
            ProgressText.Text = $"{instanceName} 安装完成";
            HintText.Text = "可以在启动页切换到该版本并启动。";
            DownloadCenter.Complete(task, "安装完成");
            _toast?.Invoke($"{_entry.Name} 安装完成");
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
