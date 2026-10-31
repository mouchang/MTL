using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public sealed class DependencyChip
{
    public required string ProjectId { get; init; }

    /// <summary>标签里显示的名字（有中文名就用中文）。</summary>
    public required string Name { get; init; }

    /// <summary>悬停提示：中英文一起给，方便确认是哪个 Mod。</summary>
    public required string Tooltip { get; init; }
}

public sealed class ModrinthFileRow
{
    public required ModrinthVersion Version { get; init; }

    public required ModrinthFile File { get; init; }

    /// <summary>该版本的前置（异步填充）。</summary>
    public ObservableCollection<DependencyChip> Dependencies { get; } = [];

    public string Title => Version.Name;

    /// <summary>项目级的加载器（CurseForge 的单条版本经常没有，用它兜底）。</summary>
    public IReadOnlyList<string> ProjectLoaders { get; init; } = [];

    /// <summary>项目类型：modpack 的版本没有加载器信息时显示「包」而不是「?」。</summary>
    public string ProjectType { get; init; } = "mod";

    private string? EffectiveLoader
    {
        get
        {
            var loader = Version.Loaders.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(loader)) return loader.ToLowerInvariant();

            var project = ProjectLoaders.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(project)) return project.ToLowerInvariant();

            // 名字里带加载器字样的（RLCraft 2.9.2d 这类）也认一下
            var name = Version.Name.ToLowerInvariant();

            if (name.Contains("neoforge")) return "neoforge";
            if (name.Contains("forge")) return "forge";
            if (name.Contains("fabric")) return "fabric";
            if (name.Contains("quilt")) return "quilt";

            return null;
        }
    }

    public string LoaderInitial => EffectiveLoader switch
    {
        "fabric" => "F",
        "forge" => "Fg",
        "neoforge" => "N",
        "quilt" => "Q",
        _ => ProjectType == "modpack" ? "包" : "?",
    };

    public System.Windows.Media.Brush LoaderBrush => EffectiveLoader switch
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

    private ModrinthItem? _item;
    private ModrinthProject? _project;
    private IReadOnlyList<ModrinthVersion> _versions = [];
    private string? _selectedGameVersion;
    private string? _currentGameVersion;
    private string? _targetInstance;
    private bool _installing;
    private string _loaderFilter = string.Empty;
    private readonly Stack<(ModrinthItem Item, string? GameVersion)> _history = new();
    private Action? _onBack;
    private Action<string>? _toast;

    public ModrinthDetailView()
    {
        InitializeComponent();

        // 构造完成后再选中，避免 XAML 解析阶段触发 Checked 事件时控件还没赋值
        LoaderAllRadio.IsChecked = true;
    }

    public async Task LoadAsync(ModrinthItem item, string? initialGameVersion, Action onBack,
        Action<string> toast, string? targetInstance = null)
    {
        _item = item;
        _targetInstance = targetInstance;
        _currentGameVersion = initialGameVersion;
        _onBack = onBack;
        _toast = toast;

        BreadcrumbText.Text = $"社区资源 / {item.Title}";
        TitleText.Text = item.Title;

        if (IsMod(item.Hit.ProjectType))
        {
            ShowChineseTitle(item.Title);
            LoadMcmod(item.Title, item.Hit.Slug);
        }
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
            _project = await ResourceService.GetProjectAsync(item.Platform, item.ProjectId,
                item.Hit.ProjectType);
            _versions = await ResourceService.GetVersionsAsync(item.Platform, item.ProjectId);

            if (_project is not null)
            {
                if (!string.IsNullOrWhiteSpace(_project.Description))
                    DescriptionText.Text = _project.Description;

                if (!string.IsNullOrWhiteSpace(_project.Title))
                {
                    TitleText.Text = _project.Title;

                    if (IsMod(item.Hit.ProjectType))
                        LoadMcmod(_project.Title, _project.Slug);
                }

                MetaText.Text = BuildMeta(_project, item.Platform);
            }

            ExternalButton.Content = item.Platform == ResourcePlatform.CurseForge
                ? "转到 CurseForge"
                : "转到 Modrinth";

            BuildTabs();

            if (!string.IsNullOrWhiteSpace(initialGameVersion)
                && _versions.SelectMany(v => v.GameVersions).Contains(initialGameVersion))
            {
                _selectedGameVersion = initialGameVersion;
                HighlightTabs();
                LoaderRow.Visibility = item.Hit.ProjectType == "mod" ? Visibility.Visible : Visibility.Collapsed;

                ApplyFilter(initialGameVersion ?? null);
                SelectLoaderForInstance(targetInstance);
            }
            else
            {
                ApplyFilter(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "resource detail");
            BreadcrumbText.Text = "加载失败：" + ex.Message;
        }
        finally
        {
            Loading.IsActive = false;
        }
    }

    private static string BuildMeta(ModrinthProject project, ResourcePlatform platform)
    {
        var downloads = project.Downloads switch
        {
            >= 100_000_000 => $"{project.Downloads / 100_000_000.0:0.#} 亿",
            >= 10_000 => $"{project.Downloads / 10_000.0:0.#} 万",
            _ => project.Downloads.ToString("N0"),
        };

        var categories = CategoryNames.VisibleText(project.Categories, 3);
        var updated = project.Updated == DateTimeOffset.MinValue
            ? string.Empty
            : $" · 最近更新 {project.Updated.LocalDateTime:yyyy/M/d}";
        var source = platform == ResourcePlatform.CurseForge ? "CurseForge" : "Modrinth";

        return string.IsNullOrEmpty(categories)
            ? $"{downloads} 次下载{updated} · {source}"
            : $"{downloads} 次下载 · {categories}{updated} · {source}";
    }

    private void BuildTabs()
    {
        TabPanel.Children.Clear();
        TabPanel.ColumnDefinitions.Clear();
        TabPanel.RowDefinitions.Clear();

        var versions = GameVersionTags.Arrange(
            _versions.SelectMany(v => v.GameVersions), _item?.Hit.ProjectType);

        var labels = new List<(string Text, string? Value)> { ("全部", null) };
        labels.AddRange(versions.Select(v => (GameVersionTags.Display(v), (string?)v)));

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

        VersionSourceText.Text = $"版本来源：{( _item?.Platform == ResourcePlatform.CurseForge ? "CurseForge" : "Modrinth")}（{versions.Count} 个版本）";

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
        if (FileList is null || ListHeader is null) return;

        var versions = _versions
            .Where(v => gameVersion is null || v.GameVersions.Contains(gameVersion))
            .Where(v => _loaderFilter.Length == 0
                        || v.Loaders.Contains(_loaderFilter, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(v => v.DatePublished)
            .ToList();

        ListHeader.Text = gameVersion is null ? "全部版本" : $"{gameVersion}（所选版本）";

        var rows = versions
            .Select(v => new ModrinthFileRow
            {
                Version = v,
                ProjectLoaders = string.IsNullOrEmpty(_loaderFilter) ? [] : [_loaderFilter],
                ProjectType = _item?.Hit.ProjectType ?? "mod",
                File = v.Files.FirstOrDefault(f => f.Primary) ?? v.Files.FirstOrDefault()!,
            })
            .Where(row => row.File is not null)
            .ToList();

        // 版本动辄上百条：先渲染一批，滚到底再续，避免一次性建几百行
        _filteredRows = rows;
        _visibleRows = new ObservableCollection<ModrinthFileRow>(rows.Take(VersionPageSize));
        FileList.ItemsSource = _visibleRows;

        // 前置：批量取名字立刻贴标签，再后台核对是否需要
        _ = LoadDependenciesForVisibleAsync();
    }

    /// <summary>
    /// 前置标签：先把名字一次性批量取回来，立刻贴标签；「这一版还需不需要它」的核对放后台。
    /// </summary>
    private async Task LoadDependenciesForVisibleAsync()
    {
        var platform = _item?.Platform ?? ResourcePlatform.Modrinth;
        var projectType = _item?.Hit.ProjectType ?? "mod";

        var targets = _visibleRows
            .Take(DependencyPrefetchRows)
            .Select(row => (Row: row, Deps: RequiredDependencies(row)))
            .Where(target => target.Deps.Count > 0)
            .ToList();

        if (targets.Count == 0) return;

        var ids = targets.SelectMany(target => target.Deps)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        IReadOnlyDictionary<string, ModrinthProject> projects;

        try
        {
            projects = await ResourceService.GetProjectsAsync(platform, ids, projectType);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "batch dependency projects");
            projects = new Dictionary<string, ModrinthProject>();
        }

        // 1. 立刻贴标签：名字拿不到就先用项目 ID，比空白强
        Dispatcher.Invoke(() =>
        {
            foreach (var (row, deps) in targets)
            {
                foreach (var projectId in deps)
                {
                    if (row.Dependencies.Any(chip => chip.ProjectId == projectId)) continue;

                    projects.TryGetValue(projectId, out var project);

                    var english = project?.Title ?? projectId;
                    var chinese = McmodIndex.ChineseFor(project?.Slug, english);

                    row.Dependencies.Add(new DependencyChip
                    {
                        ProjectId = projectId,
                        Name = string.IsNullOrWhiteSpace(chinese) ? english : chinese,
                        Tooltip = string.IsNullOrWhiteSpace(chinese) ? english : chinese + "\n" + english,
                    });
                }
            }
        });

        // 2. 后台核对：这一版下前置已经没有构建的，说明只是老版本需要，撤掉
        foreach (var (row, deps) in targets)
            _ = VerifyDependenciesAsync(platform, row, deps);
    }

    /// <summary>这一版真正要展示的前置：required、去重、排除加载器本体、最多 6 个。</summary>
    private static List<string> RequiredDependencies(ModrinthFileRow row) =>
        row.Version.Dependencies
            .Where(dependency => dependency.DependencyType == "required")
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency.ProjectId))
            .Where(dependency => !IsLoaderDependency(dependency.ProjectId!))
            .Select(dependency => dependency.ProjectId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxDependenciesPerVersion)
            .ToList();

    private async Task VerifyDependenciesAsync(ResourcePlatform platform, ModrinthFileRow row,
        IReadOnlyList<string> projectIds)
    {
        var versions = row.Version.GameVersions
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (versions.Count == 0) return;

        foreach (var projectId in projectIds)
        {
            if (await DependencyCoversAsync(platform, projectId, versions)) continue;

            Dispatcher.Invoke(() =>
            {
                var chip = row.Dependencies.FirstOrDefault(item => item.ProjectId == projectId);
                if (chip is not null) row.Dependencies.Remove(chip);
            });
        }
    }

    private const int MaxDependenciesPerVersion = 6;

    /// <summary>只为最前面这么多行预加载前置。</summary>
    private const int DependencyPrefetchRows = 12;

    /// <summary>每次渲染多少行版本，滚到底再续下一页。</summary>
    private const int VersionPageSize = 30;

    private IReadOnlyList<ModrinthFileRow> _filteredRows = [];
    private ObservableCollection<ModrinthFileRow> _visibleRows = [];

    /// <summary>滚到离底部还有一屏时，把下一批版本补进来。</summary>
    private void PageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (PageScroll.VerticalOffset + PageScroll.ViewportHeight < PageScroll.ExtentHeight - 400) return;

        AppendVisibleRows();
    }

    private void AppendVisibleRows()
    {
        if (_visibleRows.Count >= _filteredRows.Count) return;

        foreach (var row in _filteredRows.Skip(_visibleRows.Count).Take(VersionPageSize))
            _visibleRows.Add(row);

        _ = LoadDependenciesForVisibleAsync();
    }

    /// <summary>Fabric API / Quilt API：几乎所有 Mod 都写着，显示出来只会刷屏。</summary>
    private static bool IsLoaderDependency(string projectId) =>
        projectId is "P7dR8mSH" or "qvIfYCYJ" or "306612" or "634179"
        || projectId.Equals("fabric-api", StringComparison.OrdinalIgnoreCase)
        || projectId.Equals("quilted-fabric-api", StringComparison.OrdinalIgnoreCase);

    /// <summary>这个前置在给定游戏版本的任意版本里还有构建吗？查过的按项目缓存。</summary>
    private async Task<bool> DependencyCoversAsync(ResourcePlatform platform, string projectId,
        HashSet<string> versions)
    {
        var key = $"{platform}:{projectId}";

        if (DependencyVersionCache.TryGetValue(key, out var known))
            return known.Count == 0 || known.Overlaps(versions);

        HashSet<string> supported;

        try
        {
            var list = await ResourceService.GetVersionsAsync(platform, projectId);
            supported = list
                .SelectMany(version => version.GameVersions)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "dependency versions " + projectId);
            supported = [];
        }

        DependencyVersionCache[key] = supported;
        return supported.Count == 0 || supported.Overlaps(versions);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>>
        DependencyVersionCache = new(StringComparer.OrdinalIgnoreCase);

    private async void Dependency_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DependencyChip chip } || _item is null) return;

        try
        {
            Loading.IsActive = true;

            var project = await ResourceService.GetProjectAsync(_item.Platform, chip.ProjectId,
                _item.Hit.ProjectType);

            if (project is null)
            {
                _toast?.Invoke("打不开这个前置项目。");
                return;
            }

            var hit = new ModrinthHit
            {
                ProjectId = project.Id,
                Slug = project.Slug,
                Title = project.Title,
                Description = project.Description,
                Author = string.Empty,
                Downloads = project.Downloads,
                IconUrl = project.IconUrl,
                Categories = project.Categories,
                ProjectType = _item.Hit.ProjectType,
            };

            var item = new ModrinthItem { Hit = hit, Platform = _item.Platform };

            _history.Push((_item, _currentGameVersion));
            await LoadAsync(item, _currentGameVersion, _onBack!, _toast!, _targetInstance);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open dependency");
            _toast?.Invoke("打开前置失败：" + ex.Message);
        }
        finally
        {
            Loading.IsActive = false;
        }
    }

    private void LoaderFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        _loaderFilter = tag;
        ApplyFilter(_selectedGameVersion);
    }

    /// <summary>跳转过来时按实例的加载器自动选中。</summary>
    private void SelectLoaderForInstance(string? instance)
    {
        if (string.IsNullOrWhiteSpace(instance) || _item?.Hit.ProjectType != "mod") return;

        try
        {
            var loader = InstanceService.GetInfo(instance).Loader switch
            {
                "Fabric" => "fabric",
                "Forge" => "forge",
                "NeoForge" => "neoforge",
                _ => string.Empty,
            };

            if (loader.Length == 0) return;

            RadioButton? target = null;

            foreach (var child in LoaderRow.Children)
            {
                if (child is RadioButton { Tag: string tag } radio && tag == loader)
                {
                    target = radio;
                    break;
                }
            }

            if (target is not null) target.IsChecked = true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "select loader filter");
        }
    }

    private async void InstallFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModrinthFileRow row } || _item is null) return;
        if (_installing) return;

        _installing = true;
        // 不要 indeterminate：进度条要跟着真实百分比走
        var task = DownloadCenter.Begin($"安装 {row.Version.Name}");
        task.RetryAction = () => InstallFile_Click(sender, new RoutedEventArgs());

        try
        {
            DownloadCenter.Report(task, "正在下载文件…", 20);

            // 整合包统一放到 <GameRoot>\modpacks\downloads，不再塞进实例目录
            var directory = _item.Hit.ProjectType == "modpack"
                ? GamePaths.ModpacksDownloadsDir
                : Path.Combine(GameDir(), FolderFor(_item.Hit.ProjectType));

            Directory.CreateDirectory(directory);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "确认安装位置",
                InitialDirectory = directory,
                FileName = DownloadFileName(row),
                Filter = "文件 (*.*)|*.*",
                OverwritePrompt = false,
                CheckPathExists = true,
            };

            if (dialog.ShowDialog() != true)
            {
                DownloadCenter.Complete(task, "已取消安装");
                return;
            }

            var targetDirectory = Path.GetDirectoryName(dialog.FileName) ?? directory;

            // 进度：按已收字节 / 总大小换算百分比
            var total = row.File.Size > 0 ? row.File.Size : 0;
            var progress = new Progress<DownloadProgress>(p =>
            {
                var percent = total > 0 ? Math.Clamp(p.Received * 100.0 / total, 0, 100) : 0;
                var speed = p.BytesPerSecond > 0 ? $" · {FormatSpeed((long)p.BytesPerSecond)}" : string.Empty;

                // 带上已收字节，面板才能算出总下载速度
                DownloadCenter.Report(task, $"正在下载… {percent:0}%{speed}", percent, p.Received);
            });

            // 对话框里可能改过文件名（默认带了 [中文名] 前缀），按选中的名字落地
            var chosenName = Path.GetFileName(dialog.FileName);

            var downloaded = await ResourceService.DownloadAsync(row.File, targetDirectory, chosenName,
                progress, task.Token);

            // 整合包：下载完直接安装，不用再手动去「安装已有整合包」里选一次
            if (_item.Hit.ProjectType == "modpack")
            {
                DownloadCenter.Report(task, "正在安装整合包…", 0);

                var packProgress = new Progress<InstallProgress>(p =>
                    DownloadCenter.Report(task, p.Message, p.Percent));

                var instance = await ModpackImporter.ImportAsync(downloaded, packProgress, task.Token);
                DownloadCenter.Complete(task, $"已安装整合包 {instance}");
                _toast?.Invoke($"整合包已安装：{instance}");
                (Window.GetWindow(this) as MainWindow)?.RefreshInstances();
                return;
            }

            DownloadCenter.Complete(task, "已安装");
            _toast?.Invoke($"{row.Version.Name} 安装完成");
        }
        catch (OperationCanceledException)
        {
            DownloadCenter.Complete(task, "已取消安装");
            _toast?.Invoke("已取消安装");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth install");
            DownloadCenter.Complete(task, "失败：" + ex.Message);
            _toast?.Invoke("安装失败：" + ex.Message);
        }
        finally
        {
            _installing = false;
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

    private string GameDir()
    {
        // 从版本设置跳转过来时，默认安装到那个实例
        if (!string.IsNullOrWhiteSpace(_targetInstance))
        {
            var target = InstanceService.GameDirFor(_targetInstance);
            if (Directory.Exists(target)) return target;
        }

        // 否则跟随「当前实例」的隔离设置（每个实例可以单独配置）
        var selected = AppConfig.Current.SelectedVersion;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var dir = InstanceService.GameDirFor(selected);
            if (Directory.Exists(dir)) return dir;
        }

        return GamePaths.Root;
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count > 0)
        {
            var previous = _history.Pop();
            await LoadAsync(previous.Item, previous.GameVersion, _onBack!, _toast!, _targetInstance);
            return;
        }

        _onBack?.Invoke();
    }

    private void OpenModrinth_Click(object sender, RoutedEventArgs e)
    {
        if (_item is null || _project is null) return;

        try
        {
            UrlLauncher.Open(ResourceService.WebUrl(_item.Platform, _project));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open project page");
        }
    }

    /// <summary>标题下方补一行译名表里的中文名。</summary>
    /// <summary>译名表只覆盖 Mod，光影 / 资源包 / 整合包不显示中文副标题。</summary>
    private static bool IsMod(string? projectType)
        => string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase);

    private void ShowChineseTitle(string title)
    {
        var chinese = McmodIndex.ChineseFor(_item?.Hit.Slug, title)
                      ?? TranslationTable.ChineseFor(title);

        ChineseTitleText.Text = chinese ?? string.Empty;
        ChineseTitleText.Visibility = chinese is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 下载文件名：有 MC百科中文名就加个前缀（PCL2 的做法，如「[3D 皮肤层] skinlayers3d-….jar」），
    /// 没有中文名就保持原文件名。整包文件也照样处理。
    /// </summary>
    private string DownloadFileName(ModrinthFileRow row)
    {
        var name = row.File.Filename;

        try
        {
            var slug = _item?.Hit.Slug;
            var title = _item?.Hit.Title ?? row.Version.Name;

            var chinese = McmodIndex.ChineseFor(slug, title);

            AppLog.Info($"download name: slug={slug} title={title} chinese={chinese ?? "(null)"}");

            if (!string.IsNullOrWhiteSpace(chinese) && !name.Contains(chinese, StringComparison.Ordinal))
                return $"[{chinese}] {name}";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "download name");
        }

        return name;
    }

    private static string FormatSpeed(long bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024L * 1024 => $"{bytesPerSecond / 1024.0 / 1024:0.#} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024.0:0.#} KB/s",
        _ => $"{bytesPerSecond} B/s",
    };

    private string? _mcmodUrl;

    /// <summary>按 slug / 项目名匹配 MC百科；命中百科页显示中文名，没命中就给搜索页。</summary>
    private void LoadMcmod(string title, string? slug)
    {
        var entry = McmodService.Lookup(title, slug);

        if (entry is null)
        {
            McmodButton.Visibility = Visibility.Collapsed;
            return;
        }

        _mcmodUrl = entry.Address;
        ApplyMcmodEntry(entry);

    }

    private void ApplyMcmodEntry(McmodEntry entry)
    {
        _mcmodUrl = entry.Address;

        McmodButton.Content = string.IsNullOrWhiteSpace(entry.ChineseName)
            ? (entry.IsExact ? "MC百科" : "MC百科搜索")
            : $"MC百科 · {entry.ChineseName}";

        McmodButton.Visibility = Visibility.Visible;
    }

    private void OpenMcmod_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_mcmodUrl)) return;

        var error = UrlLauncher.Open(_mcmodUrl);
        if (error is not null) _toast?.Invoke(error);
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