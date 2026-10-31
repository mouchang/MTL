using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private int _pageCount = 1;
    private string? _presetGameVersion;
    private string? _presetLoader;

    /// <summary>CurseForge 慢的时候会在后台补抓，用剩下的补刷次数控制自动刷新。</summary>
    private int _curseForgeRefreshLeft;

    private CancellationTokenSource? _curseForgeRefresh;

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
        _ = LoadCategoriesAsync();
        _ = RunSearchAsync(0);
    }

    /// <summary>译名表只覆盖 Mod，其它资源类型不套中文名。</summary>
    private static bool IsMod(string? projectType)
        => string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase);

    /// <summary>「类型」下拉：按平台标签动态填充（Modrinth 的分类接口）。</summary>
    private async Task LoadCategoriesAsync()
    {
        try
        {
            var categories = await ResourceService.GetCategoriesAsync(_projectType);

            var loaded = TypeCombo.SelectedItem as ComboBoxItem;
            var current = loaded?.Tag as string ?? string.Empty;

            TypeCombo.Items.Clear();
            TypeCombo.Items.Add(new ComboBoxItem { Content = "全部", Tag = string.Empty });

            foreach (var name in categories)
            {
                if (!CategoryNames.IsVisible(name)) continue;

                TypeCombo.Items.Add(new ComboBoxItem
                {
                    Content = CategoryNames.Chinese(name),
                    Tag = name,
                });
            }

            var index = 0;

            for (var i = 0; i < TypeCombo.Items.Count; i++)
            {
                if (TypeCombo.Items[i] is ComboBoxItem item && (item.Tag as string) == current)
                {
                    index = i;
                    break;
                }
            }

            TypeCombo.SelectedIndex = index;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load categories");
        }
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
                if (string.Equals(VersionCombo.Items[i] as string, _presetGameVersion,
                        StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                VersionCombo.Items.Add(_presetGameVersion);
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

    /// <summary>列表自带的 ScrollViewer 在模板里，取出来滚回顶部。</summary>
    private void ScrollResultsToTop()
    {
        ResultsList.ApplyTemplate();
        ResultsList.UpdateLayout();

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(ResultsList); i++)
        {
            if (VisualTreeHelper.GetChild(ResultsList, i) is ScrollViewer viewer)
            {
                viewer.ScrollToTop();
                return;
            }
        }
    }

    /// <summary>搜索代次：慢请求回来时如果已经有更新的请求，就丢掉这批结果。</summary>
    private int _searchGeneration;

    /// <summary>输入框里按回车直接搜索，不用移动到按钮上。</summary>
    private void Filter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;

        e.Handled = true;
        _ = RunSearchAsync(0);
    }

    private async Task RunSearchAsync(int page = 0)
    {
        if (page != _page) _curseForgeRefreshLeft = 0;

        var generation = ++_searchGeneration;
        _page = Math.Max(0, page);
        Loading.IsActive = true;

        var platform = SourcePlatform();

        try
        {
            var result = await ResourceService.SearchAsync(platform, SearchNameBox.Text.Trim(),
                _projectType, GameVersion(), Loader(), Category(), _page, PageSize, SortOption());

            // 翻页点得太快时，前一次（更慢的）请求可能后返回 —— 别让它覆盖当前页
            if (generation != _searchGeneration) return;

            _totalHits = result.Total;
            _pageCount = result.PageCount;

            // 同一个资源在 Modrinth / CurseForge 的两条结果合并成一张卡片（顶部来源标签切换）
            var items = ResourceService.GroupResults(result.Hits)
                .Select(group =>
                {
                    var card = new SearchResultCard(group);

                    // 译名表只有 Mod 这一套数据，光影 / 资源包 / 整合包不套用
                    if (IsMod(card.Current.Hit.ProjectType))
                        card.ChineseTitle = McmodIndex.ChineseFor(card.Current.Hit.Slug, card.Title)
                                            ?? TranslationTable.ChineseFor(card.Title);

                    return card;
                })
                .ToList();

            ResultsList.ItemsSource = items;
            _ = LoadIconsAsync(items);


            // 翻页后回到顶部，不然还停在上一页的底部
            ScrollResultsToTop();

            ResultsList.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });

            // 不再显示“找到 N 个结果”：CurseForge 的 totalCount 封顶 10000，
            // 聚合又是两边相加，数字既不准也没意义，只在没结果时给个提示
            StatusText.Text = items.Count == 0 ? "没有找到结果，换个关键词或条件试试。" : string.Empty;

            BuildPageBar();

            // CurseForge 还在后台抓：过一会儿自己再刷一次，抓到了就会补进来
            if (result.CurseForgePending) ScheduleCurseForgeRefresh(page);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "community search");
            if (generation != _searchGeneration) return;

            ResultsList.ItemsSource = null;
            StatusText.Text = "搜索失败：" + ex.Message;
            PageBar.Visibility = Visibility.Collapsed;
        }
        finally
        {
            // 只有「当前这一代」请求才关掉 loading，否则慢请求会把新请求的转圈提前关掉
            if (generation == _searchGeneration) Loading.IsActive = false;
        }
    }

    /// <summary>
    /// CurseForge 走第三方镜像，实测一次要 20 秒左右，所以第一次只等 8 秒。
    /// 没等到就在这里挂一个延时刷新：后台抓完后再拉一次同一页，把 CF 的结果补上。
    /// </summary>
    private void ScheduleCurseForgeRefresh(int page)
    {
        if (_curseForgeRefreshLeft <= 0) _curseForgeRefreshLeft = 3;

        _curseForgeRefresh?.Cancel();
        _curseForgeRefresh = new CancellationTokenSource();
        var token = _curseForgeRefresh.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(22), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await Dispatcher.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested || _page != page) return;
                if (_curseForgeRefreshLeft <= 0) return;

                _curseForgeRefreshLeft--;
                await RunSearchAsync(page);
            });
        }, token);
    }

    private void BuildPageBar()
    {
        // 页数由服务端算好：聚合模式按合并后的卡片数，单平台按接口返回的总数
        var totalPages = Math.Max(1, _pageCount);

        if (totalPages <= 1)
        {
            PageBar.Visibility = Visibility.Collapsed;
            PagePanel.Children.Clear();
            return;
        }

        PageBar.Visibility = Visibility.Visible;
        PageInfo.Text = string.Empty;
        PagePanel.Children.Clear();

        AddPageButton("上一页", Math.Max(0, _page - 1), enabled: _page > 0);

        // 每轮 6 页；走到本轮最后一页时整轮后移，停在第 6 页也能直接看到 6-11
        var (start, end) = PageWindow.Range(_page, totalPages);

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
            VersionCombo.Items.Add(AllVersionsLabel);
            foreach (var version in versions)
                VersionCombo.Items.Add(version);

            VersionCombo.SelectedIndex = 0;

            if (selected is not null)
            {
                var index = VersionCombo.Items.IndexOf(selected);

                if (index > 0) VersionCombo.SelectedIndex = index;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth game versions");
        }
    }

    private static async Task LoadIconsAsync(IReadOnlyList<SearchResultCard> cards)
    {
        using var semaphore = new SemaphoreSlim(6);

        var tasks = cards
            .SelectMany(card => card.Items)
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

    private const string AllVersionsLabel = "全部（也可自行输入）";

    /// <summary>版本可以下拉选，也可以手输；输入框里写什么就用什么。</summary>
    private string? GameVersion()
    {
        var text = VersionCombo.Text?.Trim();

        if (string.IsNullOrEmpty(text) || text.StartsWith("全部", StringComparison.Ordinal))
            return null;

        return text;
    }

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

    private void OpenDetail_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SearchResultCard card })
            DetailRequested?.Invoke(card.Current, GameVersion());
    }

    /// <summary>点来源标签：切换这张卡片用哪个平台的数据（不动列表顺序，也不重新请求）。</summary>
    private void SourceChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string platform, DataContext: SearchResultCard card }
            || !Enum.TryParse<ResourcePlatform>(platform, ignoreCase: true, out var target))
            return;

        // 切换前先判断是不是真的有变化，没变化就别弹
        var changed = card.Current.Platform != target;
        card.Select(target);
        if (changed) Pop(sender as FrameworkElement);

        // 别让外层「点整行开详情」也跟着触发
        e.Handled = true;
    }

    /// <summary>标签选中时的弹性反馈：模板里的 Transform 会被冻结，这里现造一个可变实例。</summary>
    private static void Pop(FrameworkElement? chip)
    {
        if (chip is null) return;

        var scale = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(0.5, 0.5);
        chip.RenderTransform = scale;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 1.16, TimeSpan.FromMilliseconds(90))
            {
                AutoReverse = true,
                EasingFunction = ease,
            });

        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 1.16, TimeSpan.FromMilliseconds(90))
            {
                AutoReverse = true,
                EasingFunction = ease,
            });
    }

    private ResourceSort SortOption() =>
        ((SortCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "updated" => ResourceSort.Updated,
            "relevance" => ResourceSort.Relevance,
            _ => ResourceSort.Downloads,
        };

    private ResourcePlatform? SourcePlatform() =>
        ((SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "curseforge" => ResourcePlatform.CurseForge,
            "modrinth" => ResourcePlatform.Modrinth,
            _ => null,
        };

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