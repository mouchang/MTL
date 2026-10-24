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

    private bool _fabricLoaded;
    private string? _selectedLoader;
    private string? _selectedLoaderVersion;
    private readonly List<Button> _fabricChips = [];

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
        VersionCaption.Text = entry.Caption;
        BreadcrumbText.Text = $"原版资源 / {entry.Name}";
        HintText.Text = string.Empty;

        _selectedLoader = null;
        _selectedLoaderVersion = null;
        FabricVersions.Children.Clear();
        _fabricChips.Clear();
        FabricVersionsPanel.Visibility = Visibility.Collapsed;

        FabricRow.IsEnabled = true;
        ForgeRow.IsEnabled = true;
        NeoForgeRow.IsEnabled = true;
        OptiFineRow.IsEnabled = false;

        FabricStatus.Text = HasFabricVersion(entry.Name) ? "已安装" : "可以添加";
        ForgeStatus.Text = "可以添加";
        NeoForgeStatus.Text = "可以添加";
        StartButton.Content = "下载";
    }

    private static bool HasFabricVersion(string gameVersion)
    {
        if (!Directory.Exists(GamePaths.VersionsDir)) return false;

        return Directory.EnumerateDirectories(GamePaths.VersionsDir)
            .Select(Path.GetFileName)
            .Any(name => name is not null
                         && name.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase)
                         && name.EndsWith("-" + gameVersion, StringComparison.OrdinalIgnoreCase));
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _onBack?.Invoke();

    private async void Fabric_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLoaderVersion is not null) return;

        if (FabricVersionsPanel.Visibility == Visibility.Visible)
        {
            FabricVersionsPanel.Visibility = Visibility.Collapsed;
            FabricChevron.Text = "\uE76C";
            return;
        }

        FabricVersionsPanel.Visibility = Visibility.Visible;
        FabricChevron.Text = "\uE70E";

        if (_fabricLoaded) return;

        FabricVersionsHint.Text = "正在获取 Fabric 版本…";
        try
        {
            var loaders = await FabricLoader.GetLoadersAsync(_entry!.Name);
            var stable = loaders.Where(item => item.Loader.Stable).ToList();
            var list = (stable.Count > 0 ? stable : loaders.ToList()).Take(40).ToList();

            FabricVersions.Children.Clear();
            _fabricChips.Clear();

            if (list.Count == 0)
            {
                FabricVersionsHint.Text = "Fabric 暂不支持这个游戏版本。";
                return;
            }

            FabricVersionsHint.Text = $"选择 Fabric Loader 版本（共 {list.Count} 个）：";
            foreach (var item in list)
            {
                var chip = new Button
                {
                    Style = (Style)FindResource("GhostButton"),
                    Content = new TextBlock
                    {
                        Text = item.Loader.Version,
                        FontSize = 12,
                        Foreground = (Brush)FindResource("InkSoft"),
                    },
                    Tag = item.Loader.Version,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                };

                chip.Click += (_, _) => SelectFabricVersion(chip, (string)chip.Tag);
                FabricVersions.Children.Add(chip);
                _fabricChips.Add(chip);
            }

            _fabricLoaded = true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load fabric versions");
            FabricVersionsHint.Text = "获取 Fabric 版本失败：" + ex.Message;
        }
    }

    private void SelectFabricVersion(Button chip, string version)
    {
        foreach (var item in _fabricChips)
        {
            var selected = item == chip;
            item.Background = selected ? (Brush)FindResource("Accent") : Brushes.Transparent;

            if (item.Content is TextBlock text)
            {
                text.Foreground = selected ? Brushes.White : (Brush)FindResource("InkSoft");
                text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        _selectedLoader = "fabric";
        _selectedLoaderVersion = version;

        FabricStatus.Text = $"已选 {version}";
        ForgeStatus.Text = "与所选加载器不互通";
        NeoForgeStatus.Text = "与所选加载器不互通";
        ForgeRow.IsEnabled = false;
        NeoForgeRow.IsEnabled = false;
        OptiFineRow.IsEnabled = false;

        StartButton.Content = $"下载 Fabric {version}";
        HintText.Text = "点击右下角「下载」开始安装。";
    }

    private void UnsupportedLoader_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLoaderVersion is not null) return;

        var name = (sender as Button)?.Name switch
        {
            "ForgeRow" => "Forge",
            "NeoForgeRow" => "NeoForge",
            _ => "OptiFine",
        };

        _toast?.Invoke($"{name} 还没接入，目前只支持 Fabric。");
    }

    private Progress<InstallProgress> CreateProgress() => new(p =>
    {
        ProgressText.Text = $"{_entry?.Name}：{p.Message}";
        ProgressFill.Width = ProgressTrack.ActualWidth * Math.Clamp(p.Percent, 0, 100) / 100.0;
    });

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_entry is null || string.IsNullOrEmpty(_entry.Url)) return;

        StartButton.IsEnabled = false;
        ProgressCard.Visibility = Visibility.Visible;
        ProgressText.Text = string.Empty;
        ProgressFill.Width = 0;

        try
        {
            var progress = CreateProgress();

            if (!string.IsNullOrEmpty(_selectedLoaderVersion))
            {
                var vanillaJson = Path.Combine(GamePaths.VersionsDir, _entry.Name, $"{_entry.Name}.json");
                if (!File.Exists(vanillaJson))
                {
                    ProgressText.Text = $"先安装原版 {_entry.Name}…";
                    await new VersionInstaller().InstallAsync(_entry.Name, _entry.Url, progress,
                        installAssets: false);
                }

                var id = await new FabricLoader().InstallAsync(_entry.Name, _selectedLoaderVersion,
                    progress);
                ProgressText.Text = $"已安装 {id}";
                FabricStatus.Text = "已安装";
                _toast?.Invoke($"Fabric 安装完成：{id}");
                return;
            }

            await new VersionInstaller().InstallAsync(_entry.Name, _entry.Url, progress);
            ProgressText.Text = $"{_entry.Name} 安装完成";
            HintText.Text = "可以在启动页切换到该版本并启动。";
            _toast?.Invoke($"{_entry.Name} 安装完成");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "install version");
            ProgressText.Text = "安装失败：" + ex.Message;
            _toast?.Invoke("安装失败：" + ex.Message);
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }
}
