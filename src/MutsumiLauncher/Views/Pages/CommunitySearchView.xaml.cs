using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.Pages;

public partial class CommunitySearchView : UserControl
{
    private readonly ModrinthClient _client = new();

    private const int PageSize = 20;

    private string _kind = "mod";
    private string _projectType = "mod";
    private int _page;
    private long _totalHits;
    private string? _presetGameVersion;
    private string? _presetLoader;

    /// <summary>点击结果行时请求上层打开详情页。</summary>
    public event Action<ModrinthItem, string?>? DetailRequested;

    public CommunitySearchView()
    {
        InitializeComponent();
    }

    /// <summary>按分类切换标题、搜索占位与筛选条件，并立即拉一次 Modrinth。</summary>
    public void Configure(string kind, string title)
    {
        _kind = kind;
        _projectType = ProjectTypeOf(kind);

        TitleText.Text = title;
        SearchNameBox.Placeholder = kind switch
        {
            "pack" => "输入整合包名称",
            "datapack" => "输入数据包名称",
            "resourcepack" => "输入资源包名称",
            "shader" => "输入光影包名称",
            _ => "输入 Mod 名称",
        };

        LoaderCombo.Visibility = kind == "mod" ? Visibility.Visible : Visibility.Collapsed;
        InstallPackButton.Visibility = kind == "pack" ? Visibility.Visible : Visibility.Collapsed;

        SearchNameBox.Text = string.Empty;
        _page = 0;
        _ = LoadVersionsAsync();
        _ = RunSearchAsync(0);
    }

    /// <summary>预选游戏版本 / 加载器并立即搜索（从版本设置的 Mod 管理跳转过来）。</summary>
    public void Preset(string? gameVersion, string? loader)
    {
        _presetGameVersion = gameVersion;
        _presetLoader = loader;
        _ = ApplyPresetAsync();
    }

    private async Task ApplyPresetAsync()
    {
        await LoadVersionsAsync();

        if (!string.IsNullOrWhiteSpace(_presetGameVersion))
        {
            var index = -1;

            for (var i = 1; i < VersionCombo.Items.Count; i++)
            {
                if (VersionCombo.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag as string, _presetGameVersion, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                VersionCombo.Items.Add(new ComboBoxItem
                {
                    Content = _presetGameVersion,
                    Tag = _presetGameVersion,
                });
                index = VersionCombo.Items.Count - 1;
            }

            VersionCombo.SelectedIndex = index;
        }

        if (!string.IsNullOrWhiteSpace(_presetLoader))
        {
            for (var i = 1; i < LoaderCombo.Items.Count; i++)
            {
                if (LoaderCombo.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag as string, _presetLoader, StringComparison.OrdinalIgnoreCase))
                {
                    LoaderCombo.SelectedIndex = i;
                    break;
                }
            }
        }

        await RunSearchAsync(0);
    }

    private async Task RunSearchAsync(int page = 0)
    {
        _page = Math.Max(0, page);
        Loading.IsActive = true;
        StatusText.Text = "正在搜索 Modrinth…";

        try
        {
            var result = await _client.SearchAsync(SearchNameBox.Text.Trim(), _projectType,
                GameVersion(), Loader(), Category(), PageSize, _page * PageSize);

            _totalHits = result.TotalHits;

            var items = result.Hits.Select(h => new ModrinthItem { Hit = h }).ToList();
            ResultsList.ItemsSource = items;
            _ = LoadIconsAsync(items);

            StatusText.Text = result.Hits.Count == 0
                ? "没有找到结果，换个关键词或条件试试。"
                : $"找到 {_totalHits:N0} 个结果（来自 Modrinth）";

            BuildPageBar();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth search");
            ResultsList.ItemsSource = null;
            StatusText.Text = "搜索失败：" + ex.Message;
            PageBar.Visibility = Visibility.Collapsed;
        }
        finally
        {
            Loading.IsActive = false;
        }
    }

    private void BuildPageBar()
    {
        var totalPages = (int)Math.Ceiling(_totalHits / (double)PageSize);
        if (totalPages <= 1)
        {
            PageBar.Visibility = Visibility.Collapsed;
            PagePanel.Children.Clear();
            return;
        }

        PageBar.Visibility = Visibility.Visible;
        PageInfo.Text = $"第 {_page + 1} / {totalPages} 页 · 共 {_totalHits:N0} 个结果";
        PagePanel.Children.Clear();

        AddPageButton("上一页", Math.Max(0, _page - 1), enabled: _page > 0);

        var start = Math.Max(0, _page - 3);
        var end = Math.Min(totalPages - 1, start + 6);
        start = Math.Max(0, end - 6);

        for (var i = start; i <= end; i++)
        {
            var pageNumber = i;
            var button = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = new TextBlock
                {
                    Text = (pageNumber + 1).ToString(),
                    FontSize = 12,
                    Foreground = pageNumber == _page
                        ? Brushes.White
                        : (Brush)FindResource("InkSoft"),
                },
                Background = pageNumber == _page ? (Brush)FindResource("Accent") : Brushes.Transparent,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0),
            };
            button.Click += (_, _) => _ = RunSearchAsync(pageNumber);
            PagePanel.Children.Add(button);
        }

        AddPageButton("下一页", _page + 1, enabled: _page < totalPages - 1);
    }

    private void AddPageButton(string text, int page, bool enabled)
    {
        var button = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Content = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = enabled ? (Brush)FindResource("InkSoft") : Brushes.Gray,
            },
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = enabled,
        };

        button.Click += (_, _) => _ = RunSearchAsync(page);
        PagePanel.Children.Add(button);
    }

    /// <summary>版本下拉来源于 Modrinth 的 game_version 标签。</summary>
    private async Task LoadVersionsAsync()
    {
        try
        {
            var tags = await _client.GetGameVersionTagsAsync();
            if (tags.Count == 0) return;

            var releases = tags
                .Where(t => t.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Version)
                .ToList();

            var versions = (releases.Count > 0 ? releases : tags.Select(t => t.Version).ToList())
                .Distinct()
                .Take(80)
                .ToList();

            var selected = GameVersion();

            VersionCombo.Items.Clear();
            VersionCombo.Items.Add(new ComboBoxItem { Content = "全部（也可自行输入）", Tag = string.Empty });
            foreach (var version in versions)
                VersionCombo.Items.Add(new ComboBoxItem { Content = version, Tag = version });

            VersionCombo.SelectedIndex = 0;
            if (selected is not null)
            {
                for (var i = 1; i < VersionCombo.Items.Count; i++)
                {
                    if ((VersionCombo.Items[i] as ComboBoxItem)?.Tag as string == selected)
                    {
                        VersionCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth game versions");
        }
    }

    private static async Task LoadIconsAsync(IReadOnlyList<ModrinthItem> items)
    {
        using var semaphore = new SemaphoreSlim(6);

        var tasks = items
            .Where(item => !string.IsNullOrWhiteSpace(item.IconUrl))
            .Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var path = await IconCache.EnsureAsync(item.IconUrl!, item.ProjectId);
                    if (path is not null) item.IconPath = path;
                }
                finally
                {
                    semaphore.Release();
                }
            });

        await Task.WhenAll(tasks);
    }

    private string? GameVersion() => (VersionCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private string? Loader() =>
        _kind == "mod" ? (LoaderCombo.SelectedItem as ComboBoxItem)?.Tag as string : null;

    private string? Category() => (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string ProjectTypeOf(string kind) => kind switch
    {
        "pack" => "modpack",
        "datapack" => "datapack",
        "resourcepack" => "resourcepack",
        "shader" => "shader",
        _ => "mod",
    };

    private static string FolderFor(string projectType) => projectType switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        "datapack" => "datapacks",
        "modpack" => "modpacks",
        _ => "mods",
    };

    /// <summary>开了版本隔离就装进 versions/&lt;版本&gt;，否则装到游戏根目录。</summary>
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

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearchAsync(0);

    private async void ResetSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchNameBox.Text = string.Empty;
        VersionCombo.SelectedIndex = 0;
        if (LoaderCombo.Visibility == Visibility.Visible) LoaderCombo.SelectedIndex = 0;
        TypeCombo.SelectedIndex = 0;

        await RunSearchAsync(0);
    }

    private void OpenDetail_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ModrinthItem item })
            DetailRequested?.Invoke(item, GameVersion());
    }

    private async void InstallPack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择整合包",
            Filter = "Modrinth 整合包 (*.mrpack)|*.mrpack|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        await ImportPackAsync(dialog.FileName);
    }

    private async Task ImportPackAsync(string filePath)
    {
        var owner = Window.GetWindow(this) as MainWindow;
        var task = DownloadCenter.Begin($"导入整合包：{System.IO.Path.GetFileName(filePath)}");
        task.RetryAction = () => _ = ImportPackAsync(filePath);

        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                StatusText.Text = p.Message;
                DownloadCenter.Report(task, p.Message, p.Percent);
            });

            var instance = await ModpackImporter.ImportAsync(filePath, progress, task.Token);

            StatusText.Text = $"整合包已导入：{instance}";
            DownloadCenter.Complete(task, $"已导入 {instance}");
            owner?.ShowToast($"整合包已导入：{instance}");
            owner?.RefreshInstances();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消导入";
            DownloadCenter.Complete(task, "已取消");
            owner?.ShowToast("已取消导入");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "import modpack");
            StatusText.Text = "导入失败：" + ex.Message;
            DownloadCenter.Complete(task, "失败：" + ex.Message);
            owner?.ShowToast("导入失败：" + ex.Message);
        }
    }
}
