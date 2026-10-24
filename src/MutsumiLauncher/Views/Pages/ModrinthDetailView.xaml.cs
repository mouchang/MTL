using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public sealed class ModrinthFileRow
{
    public required ModrinthVersion Version { get; init; }

    public required ModrinthFile File { get; init; }

    public string Title => Version.Name;

    public string LoaderInitial => Version.Loaders.FirstOrDefault()?.ToLowerInvariant() switch
    {
        "fabric" => "F",
        "forge" => "Fg",
        "neoforge" => "N",
        "quilt" => "Q",
        _ => "?",
    };

    public System.Windows.Media.Brush LoaderBrush => Version.Loaders.FirstOrDefault()?.ToLowerInvariant() switch
    {
        "fabric" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4E, 0x9E, 0x72)),
        "forge" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0x7B, 0x4A)),
        "neoforge" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6F, 0xA8, 0xDC)),
        "quilt" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9B, 0x7B, 0xC9)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0x9E, 0x94)),
    };

    public string Subtitle
    {
        get
        {
            var loaders = string.Join(" / ", Version.Loaders.Select(Capitalize));
            var type = Version.VersionType switch
            {
                "beta" => " · 测试版",
                "alpha" => " · 内测版",
                _ => string.Empty,
            };
            return $"{loaders} · {File.Filename}{type}";
        }
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

public partial class ModrinthDetailView : UserControl
{
    private readonly ModrinthClient _client = new();

    private ModrinthItem? _item;
    private ModrinthProject? _project;
    private IReadOnlyList<ModrinthVersion> _versions = [];
    private string? _selectedGameVersion;
    private Action? _onBack;
    private Action<string>? _toast;

    public ModrinthDetailView()
    {
        InitializeComponent();
    }

    public async Task LoadAsync(ModrinthItem item, string? initialGameVersion, Action onBack,
        Action<string> toast)
    {
        _item = item;
        _onBack = onBack;
        _toast = toast;

        BreadcrumbText.Text = $"社区资源 / {item.Title}";
        TitleText.Text = item.Title;
        AuthorText.Text = item.Author;
        DescriptionText.Text = item.Description;
        InitialText.Text = item.Initial;

        if (!string.IsNullOrWhiteSpace(item.IconUrl))
        {
            var iconPath = await IconCache.EnsureAsync(item.IconUrl!, item.ProjectId);
            if (iconPath is not null)
            {
                IconImage.Source = new BitmapImage(new Uri(iconPath));
                IconBorder.Visibility = Visibility.Visible;
            }
        }

        Loading.IsActive = true;
        try
        {
            _project = await _client.GetProjectAsync(item.ProjectId);
            _versions = await _client.GetVersionsAsync(item.ProjectId);

            if (_project is not null)
            {
                DescriptionText.Text = _project.Description;
                MetaText.Text = BuildMeta(_project);
            }

            BuildTabs();

            if (!string.IsNullOrWhiteSpace(initialGameVersion)
                && _versions.SelectMany(v => v.GameVersions).Contains(initialGameVersion))
            {
                _selectedGameVersion = initialGameVersion;
                HighlightTabs();
                ApplyFilter(initialGameVersion);
            }
            else
            {
                ApplyFilter(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth detail");
            BreadcrumbText.Text = "加载失败：" + ex.Message;
        }
        finally
        {
            Loading.IsActive = false;
        }
    }

    private static string BuildMeta(ModrinthProject project)
    {
        var downloads = project.Downloads switch
        {
            >= 100_000_000 => $"{project.Downloads / 100_000_000.0:0.#} 亿",
            >= 10_000 => $"{project.Downloads / 10_000.0:0.#} 万",
            _ => project.Downloads.ToString("N0"),
        };

        var categories = string.Join(" · ", project.Categories.Take(3));
        var updated = project.Updated.LocalDateTime.ToString("yyyy/M/d");
        return $"{downloads} 次下载 · {categories} · 最近更新 {updated} · Modrinth";
    }

    private void BuildTabs()
    {
        TabPanel.Children.Clear();
        TabPanel.ColumnDefinitions.Clear();
        TabPanel.RowDefinitions.Clear();

        var versions = _versions
            .SelectMany(v => v.GameVersions)
            .Distinct()
            .OrderByDescending(ParseGameVersion)
            .ToList();

        var labels = new List<(string Text, string? Value)> { ("全部", null) };
        labels.AddRange(versions.Select(v => (v, (string?)v)));

        var columns = (int)Math.Ceiling(labels.Count / 2.0);
        for (var i = 0; i < columns; i++) TabPanel.ColumnDefinitions.Add(new ColumnDefinition());
        TabPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        TabPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

        for (var i = 0; i < labels.Count; i++)
        {
            var button = CreateTab(labels[i].Text, labels[i].Value);
            Grid.SetRow(button, i / columns);
            Grid.SetColumn(button, i % columns);
            TabPanel.Children.Add(button);
        }

        VersionSourceText.Text = CurseForgeClient.IsConfigured
            ? $"版本来源：Modrinth（{versions.Count} 个版本）+ CurseForge（CF 匹配接入中）"
            : $"版本来源：Modrinth（{versions.Count} 个版本）· CurseForge 待配置 API Key";

        HighlightTabs();
    }

    private Button CreateTab(string text, string? gameVersion)
    {
        var content = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = (Brush)FindResource("InkSoft"),
        };

        var button = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Padding = new Thickness(14, 4, 14, 4),
            Height = 32,
            Margin = new Thickness(0, 4, 8, 4),
            Tag = gameVersion,
            Content = content,
        };

        button.Click += (_, _) =>
        {
            _selectedGameVersion = gameVersion;
            HighlightTabs();
            ApplyFilter(gameVersion);
        };

        return button;
    }

    private void HighlightTabs()
    {
        foreach (var child in TabPanel.Children.OfType<Button>())
        {
            var selected = (string?)child.Tag == _selectedGameVersion;
            child.Background = selected ? (Brush)FindResource("Accent") : Brushes.Transparent;

            if (child.Content is TextBlock text)
            {
                text.Foreground = selected ? Brushes.White : (Brush)FindResource("InkSoft");
                text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }

            if (selected) child.BringIntoView();
        }
    }

    private void ApplyFilter(string? gameVersion)
    {
        var versions = _versions
            .Where(v => gameVersion is null || v.GameVersions.Contains(gameVersion))
            .OrderByDescending(v => v.DatePublished)
            .ToList();

        ListHeader.Text = gameVersion is null ? "全部版本" : $"{gameVersion}（所选版本）";

        FileList.ItemsSource = versions
            .Select(v => new ModrinthFileRow
            {
                Version = v,
                File = v.Files.FirstOrDefault(f => f.Primary) ?? v.Files.FirstOrDefault()!,
            })
            .Where(row => row.File is not null)
            .ToList();
    }

    private static double ParseGameVersion(string version)
    {
        var parts = version.Split('.', '-');
        return parts.Length >= 2 && double.TryParse(parts[0], out var major)
                               && double.TryParse(parts[1], out var minor)
            ? major * 100 + minor
            : 0;
    }

    private async void InstallFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModrinthFileRow row } || _item is null) return;

        Loading.IsActive = true;
        var task = DownloadCenter.Begin($"安装 {row.Version.Name}", indeterminate: true);

        try
        {
            DownloadCenter.Report(task, "正在下载文件…", 20);
            var directory = Path.Combine(GameDir(), FolderFor(_item.Hit.ProjectType));
            await _client.DownloadAsync(row.File.Url, row.File.Filename, directory, row.File.Hashes?.Sha1);

            DownloadCenter.Complete(task, "已安装");
            _toast?.Invoke($"{row.Version.Name} 安装完成");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth install");
            DownloadCenter.Complete(task, "失败：" + ex.Message);
            _toast?.Invoke("安装失败：" + ex.Message);
        }
        finally
        {
            Loading.IsActive = false;
        }
    }

    private static string FolderFor(string projectType) => projectType switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        "datapack" => "datapacks",
        "modpack" => "modpacks",
        _ => "mods",
    };

    private static string GameDir()
    {
        var config = AppConfig.Current;
        if (config.VersionIsolation && !string.IsNullOrWhiteSpace(config.SelectedVersion))
        {
            var dir = Path.Combine(GamePaths.VersionsDir, config.SelectedVersion);
            if (Directory.Exists(dir)) return dir;
        }

        return GamePaths.Root;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _onBack?.Invoke();

    private void OpenModrinth_Click(object sender, RoutedEventArgs e)
    {
        var slug = _item?.Hit.Slug;
        if (!string.IsNullOrWhiteSpace(slug))
            UrlLauncher.Open($"https://modrinth.com/project/{slug}");
    }

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TitleText.Text);
            _toast?.Invoke("名称已复制");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "clipboard");
        }
    }
}
