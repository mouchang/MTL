using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views.VersionSettings;

namespace MutsumiLauncher.Views;

public partial class VersionSettingsView : UserControl
{
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);
    private string _version = string.Empty;

    // 一个「版本设置」会话里，Mod 板块只自动检查一次更新；
    // 关掉浮层再打开会重置（Load 里清零）。
    private bool _modsAutoChecked;

    // 侧栏选中块是否已经摆好位置：首次直接就位，之后才做过渡
    private bool _indicatorReady;

    // 当前正在显示的那块高亮（两块交替，用来做交叉淡化）
    private FrameworkElement? _activeIndicator;

    public VersionSettingsView()
    {
        InitializeComponent();

        _activeIndicator = NavIndicator;

        var overview = new OverviewPage();
        overview.Renamed += OnRenamed;
        overview.Deleted += OnDeleted;

        _pages["overview"] = overview;
        _pages["launch"] = new LaunchOptionsPage();
        _pages["mods"] = new ModManagePage();
        _pages["saves"] = new SavesPage();
        _pages["export"] = new ExportPage();

        ShowPage("overview");
    }

    /// <summary>实例被重命名或删除（参数为新名称，删除时为 null）。</summary>
    public event Action<string?>? InstanceChanged;

    /// <summary>没有实例时显示引导页（下载 / 引用）。</summary>
    private void RefreshEmptyState()
    {
        if (EmptyState is null) return;

        var any = GameLauncher.InstalledVersions().Count > 0;

        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void GoDownload_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        (Window.GetWindow(this) as MainWindow)?.ShowDownloadPagePublic();
    }

    /// <summary>导入其它启动器的版本（复制进来，可选删除源）。</summary>
    private async void ImportMinecraft_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要导入的 .minecraft 目录",
            Multiselect = false,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var root = dialog.FolderName;

        if (!ExternalSourceService.LooksLikeMinecraft(root))
        {
            MessageDialog.Show(Window.GetWindow(this), "不是有效的目录",
                "这个目录里没有 versions / libraries / assets，看起来不是 .minecraft。",
                "知道了", null);
            return;
        }

        var versions = await MinecraftImportService.ScanAsync(root);

        if (versions.Count == 0)
        {
            MessageDialog.Show(Window.GetWindow(this), "没有可导入的版本",
                "这个目录里没找到带版本 JSON 的版本。", "知道了", null);
            return;
        }

        var importDialog = new ImportVersionsDialog(root, versions) { Owner = Window.GetWindow(this) };

        if (importDialog.ShowDialog() != true) return;

        RefreshEmptyState();
        (Window.GetWindow(this) as MainWindow)?.RefreshInstances();

        // 空状态导入了第一个版本：直接把设置页接上，不用重开浮层
        if (string.IsNullOrWhiteSpace(_version)
            && GameLauncher.InstalledVersions().FirstOrDefault() is { } first)
        {
            Load(first);
        }
    }

    /// <summary>引用其它启动器的 .minecraft（文件留在原处）。</summary>
    private void ReferenceMinecraft_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择其它启动器的 .minecraft 目录",
            Multiselect = false,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var picked = dialog.FolderName;

        if (!ExternalSourceService.LooksLikeMinecraft(picked))
        {
            MessageDialog.Show(Window.GetWindow(this), "不是有效的目录",
                "这个目录里没有 versions / libraries / assets，看起来不是 .minecraft。",
                "知道了", null);
            return;
        }

        var versions = ExternalSourceService.ListVersions(picked);

        if (versions.Count == 0)
        {
            MessageDialog.Show(Window.GetWindow(this), "没有可引用的版本",
                "这个目录里没找到带版本 JSON 的版本。", "知道了", null);
            return;
        }

        var preview = string.Join("、", versions.Take(4));
        if (versions.Count > 4) preview += " 等";

        var go = MessageDialog.Show(Window.GetWindow(this), "引用现有版本",
            $"在 {picked} 里发现 {versions.Count} 个版本：\n{preview}\n\n" +
            "引用不会复制或移动任何文件，只在启动器里生成引用实例。",
            "开始引用", "取消");

        if (!go) return;

        var created = 0;

        foreach (var version in versions)
        {
            if (ExternalSourceService.CreateShadow(picked, version, out _) is not null) created++;
        }

        if (created > 0) ExternalSourceService.AddRoot(picked);

        RefreshEmptyState();

        if (created == 0)
        {
            MessageDialog.Show(Window.GetWindow(this), "没有引用成功",
                "这些版本都缺版本 JSON 或父版本，无法引用。", "知道了", null);
            return;
        }

        MessageDialog.Show(Window.GetWindow(this), "引用完成",
            $"已引用 {created} 个版本，可以在版本列表里选择它们启动。", "好的", null);

        CloseRequested?.Invoke(this, EventArgs.Empty);
        (Window.GetWindow(this) as MainWindow)?.RefreshInstances();
    }

    public void Load(string version)
    {
        _version = version;
        RefreshEmptyState();

        TitleText.Text = string.IsNullOrWhiteSpace(version)
            ? "版本设置"
            : $"版本设置 · {ExternalSourceService.DisplayName(version)}";

        // 每次打开都回到「概览」，而不是停在上次关掉时的板块
        _modsAutoChecked = false;
        _loaded.Clear();

        // 没有版本时不初始化任何页面：否则空状态遮罩后面还在跑扫盘 / 检查更新
        var empty = string.IsNullOrWhiteSpace(version);

        NavPanel.IsEnabled = !empty;
        NavIndicator.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (empty)
        {
            PageHost.Content = null;
            NavOverview.IsChecked = false;
            return;
        }

        NavOverview.IsChecked = true;
        ShowPage("overview");

        // 调试用：MTL_VERSION_TAB=overview|launch|mods|saves|export 直接进某个标签页
        var tab = Environment.GetEnvironmentVariable("MTL_VERSION_TAB");
        if (string.IsNullOrWhiteSpace(tab)) return;

        foreach (var item in NavPanel.Children)
        {
            if (item is RadioButton { Tag: string tag } radio
                && tag.Equals(tab, StringComparison.OrdinalIgnoreCase))
            {
                radio.IsChecked = true;
                break;
            }
        }
    }

    private string? CurrentPageKey()
        => _pages.FirstOrDefault(pair => ReferenceEquals(pair.Value, PageHost.Content)).Key;

    private void InitializePage(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;
        if (page is not IInstancePage instancePage) return;
        if (!_loaded.Add(key)) return;

        instancePage.SetInstance(_version);
    }

    private void OnRenamed(string newName)
    {
        _version = newName;
        TitleText.Text = $"版本设置 · {newName}";

        // 换名字后所有页的数据都要重读，先清标记，当前页立刻刷新
        _loaded.Clear();
        if (CurrentPageKey() is { } key) InitializePage(key);

        InstanceChanged?.Invoke(newName);
    }

    private void OnDeleted()
    {
        InstanceChanged?.Invoke(null);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>点击浮层自带的关闭按钮时触发，由主窗口负责滑回动画。</summary>
    public event EventHandler? CloseRequested;

    private void ShowPage(string key)
    {
        // 没有版本时任何页面都不跑（空状态遮罩后面不该有扫盘 / 网络请求）
        if (string.IsNullOrWhiteSpace(_version)) return;

        if (!_pages.TryGetValue(key, out var page)) return;

        InitializePage(key);

        PageHost.Content = page;

        // 切板块回到顶部：否则从存档 / Mod 列表滚到一半再切回来会停在老位置。
        // 放到 Loaded 优先级，等模板和可视树都建好再滚，Mod 列表的 ScrollViewer 才找得到。
        if (page is IInstancePage instancePage)
        {
            Dispatcher.BeginInvoke(new Action(instancePage.ScrollToTop),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        // 与主窗口导航栏同款：淡入 + 轻微上浮
        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        var slide = page.RenderTransform as System.Windows.Media.TranslateTransform;

        if (slide is null)
        {
            slide = new System.Windows.Media.TranslateTransform();
            page.RenderTransform = slide;
        }

        slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = ease,
            });

        page.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = ease,
            });

        MoveNavIndicator(key);

        // 打开 Mod 板块时自动检查一次更新（每个设置会话只做一次）
        if (key == "mods" && page is ModManagePage mods && !_modsAutoChecked)
        {
            _modsAutoChecked = true;
            mods.AutoCheckUpdates();
        }
    }

    /// <summary>
    /// 侧栏选中高亮：两块圆角块交替淡入淡出，切换时不在条目之间穿行。
    /// 位置按实际布局量（控件相对 NavPanel 的偏移），不写死行高。
    /// </summary>
    private void MoveNavIndicator(string key)
    {
        if (NavIndicator is null || NavPanel is null) return;

        RadioButton? target = null;

        foreach (var child in NavPanel.Children)
        {
            if (child is RadioButton { Tag: string tag } radio && tag == key)
            {
                target = radio;
                break;
            }
        }

        if (target is null) return;

        double y;

        try
        {
            y = target.TransformToAncestor(NavPanel).Transform(new Point(0, 0)).Y;
        }
        catch
        {
            // 布局还没完成时退回按序号估算
            y = NavPanel.Children.IndexOf(target) * 56 + 2;
        }

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        var current = _activeIndicator;
        var next = ReferenceEquals(current, NavIndicator) ? NavIndicatorAlt : NavIndicator;

        // 新块先就位，再从透明淡入；旧块同时淡出
        var nextTransform = (System.Windows.Media.TranslateTransform)next.RenderTransform;
        nextTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        nextTransform.Y = y;

        next.BeginAnimation(OpacityProperty, null);

        if (!_indicatorReady)
        {
            next.Opacity = 1;
            _indicatorReady = true;
        }
        else
        {
            // 旧块先淡出，新块延迟一点淡入：中间不会两块同时亮着
            next.Opacity = 0;
            next.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    BeginTime = TimeSpan.FromMilliseconds(110),
                    EasingFunction = ease,
                });
        }

        if (current is not null && !ReferenceEquals(current, next))
        {
            current.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110))
                {
                    EasingFunction = ease,
                });
        }

        _activeIndicator = next;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0) return;
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) Window.GetWindow(this)?.DragMove();
    }
}