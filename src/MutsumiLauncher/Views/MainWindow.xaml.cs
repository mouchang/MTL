using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views.Pages;

namespace MutsumiLauncher.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };
    private bool _suppressNav;
    private bool _navExpanded;
    private bool _settingsOpen;
    private VersionSettingsView? _settingsView;
    private string _currentPage = "launch";

    public MainWindow()
    {
        InitializeComponent();

        DownloadCenter.Changed += () => Dispatcher.Invoke(UpdateDownloadFloater);

        // 适配小屏 / 高缩放：窗口不超过工作区的 92%
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1120, work.Width * 0.92);
        Height = Math.Min(820, work.Height * 0.92);

        _pages["launch"] = new LaunchPage();
        _pages["download"] = new DownloadPage();
        _pages["settings"] = new SettingsPage();

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240)));
        };

        Loaded += (_, _) =>
        {
            UpdateShellClip();

            // 调试用：MTL_START_PAGE=download 可直接进下载页
            var start = Environment.GetEnvironmentVariable("MTL_START_PAGE");
            ShowPage(start is not null && _pages.ContainsKey(start) ? start : "launch", animate: false);

            if (Environment.GetEnvironmentVariable("MTL_OPEN_SETTINGS") == "1")
            {
                OpenVersionSettings();
            }
        };
        SizeChanged += (_, _) => UpdateShellClip();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        // 导航层像弹层一样：启动页上点外部自动收起（子页上保持常驻，同设计稿）
        if (_navExpanded && _currentPage == "launch" && !TitleBar.IsMouseOver) SetNavExpanded(false);
    }

    private void UpdateShellClip()
    {
        if (Shell.ActualWidth <= 0 || Shell.ActualHeight <= 0) return;
        Shell.Clip = new RectangleGeometry(
            new Rect(0, 0, Shell.ActualWidth, Shell.ActualHeight), 26, 26);
    }

    private void ShowPage(string key, bool animate = true)
    {
        if (!_pages.TryGetValue(key, out var page)) return;

        _currentPage = key;

        _suppressNav = true;
        NavLaunch.IsChecked = key == "launch";
        NavDownload.IsChecked = key == "download";
        NavSettings.IsChecked = key == "settings";
        _suppressNav = false;

        // 进子页时导航层自动展开，回启动页时收起
        SetNavExpanded(key != "launch", animate);

        Backdrop.Effect = key == "launch"
            ? null
            : new BlurEffect { Radius = 36, KernelType = KernelType.Gaussian };

        PageHost.Content = page;

        if (page is LaunchPage launch) launch.RefreshAll();

        if (animate)
        {
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            page.Opacity = 1;
        }

        UpdateDownloadFloater();
    }

    private void UpdateDownloadFloater()
    {
        var active = DownloadCenter.ActiveCount;
        var show = active > 0 && _currentPage != "download";
        DownloadFloater.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show) return;

        var percent = DownloadCenter.Tasks.Where(task => !task.IsDone)
            .Select(task => task.Percent)
            .DefaultIfEmpty(0)
            .Average();

        FloaterText.Text = $"下载中 {active} 项 · {percent:0}%";
    }

    /// <summary>跳到下载页的社区资源 Mod 页面（会先关掉版本设置浮层）。</summary>
    public void OpenCommunityMod(string? gameVersion, string? loader)
    {
        if (_settingsOpen) CloseVersionSettings();

        ShowPage("download");

        if (_pages.TryGetValue("download", out var page) && page is DownloadPage download)
            download.OpenCommunityMod(gameVersion, loader);
    }

    public void OpenFolderBrowser(string path, string? title = null)
    {
        var browser = new FolderBrowserView();
        browser.Closed += () =>
        {
            FolderHost.Visibility = Visibility.Collapsed;
            FolderHost.Content = null;
        };

        browser.Load(path, title);
        FolderHost.Content = browser;
        FolderHost.Visibility = Visibility.Visible;
    }

    public void OpenLogViewer(string instance)
    {
        var viewer = new LogViewerView();
        viewer.Closed += () =>
        {
            LogHost.Visibility = Visibility.Collapsed;
            LogHost.Content = null;
        };

        viewer.Load(instance);
        LogHost.Content = viewer;
        LogHost.Visibility = Visibility.Visible;
    }

    private void DownloadFloater_Click(object sender, MouseButtonEventArgs e)
    {
        var panel = new DownloadPanelView();
        panel.Closed += () =>
        {
            DownloadPanelHost.Visibility = Visibility.Collapsed;
            DownloadPanelHost.Content = null;
        };

        DownloadPanelHost.Content = panel;
        DownloadPanelHost.Visibility = Visibility.Visible;
    }

    private void SetNavExpanded(bool expanded, bool animate = true)
    {
        _navExpanded = expanded;
        AppLog.Info($"nav expanded={expanded} animate={animate}");

        var fullWidth = NavInner.ActualWidth > 0 ? NavInner.ActualWidth : 40;
        var translate = (TranslateTransform)NavTabs.RenderTransform;

        if (!animate)
        {
            NavBarSurface.Width = expanded ? fullWidth : 40;
            NavBarSurface.Height = expanded ? 68 : 40;
            NavTabs.Opacity = expanded ? 1 : 0;
            NavTabs.IsHitTestVisible = expanded;
            translate.Y = expanded ? 0 : -6;
            CloseChrome.Opacity = expanded ? 0 : 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // 导航条从左上按钮的形状长出来：宽高同时过渡
        NavBarSurface.BeginAnimation(WidthProperty,
            new DoubleAnimation(expanded ? fullWidth : 40, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = ease,
            });
        NavBarSurface.BeginAnimation(HeightProperty,
            new DoubleAnimation(expanded ? 68 : 40, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = ease,
            });

        // 页签等导航条铺开后再浮出
        NavTabs.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expanded ? 1 : 0, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = expanded ? TimeSpan.FromMilliseconds(170) : TimeSpan.Zero,
                EasingFunction = ease,
            });
        NavTabs.IsHitTestVisible = expanded;

        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(expanded ? 0 : -6, TimeSpan.FromMilliseconds(240))
            {
                BeginTime = expanded ? TimeSpan.FromMilliseconds(170) : TimeSpan.Zero,
                EasingFunction = ease,
            });

        // 右侧按钮底片随导航条到达而溶解 / 随导航条退回而复原
        CloseChrome.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expanded ? 0 : 1, TimeSpan.FromMilliseconds(expanded ? 260 : 200))
            {
                BeginTime = expanded ? TimeSpan.FromMilliseconds(120) : TimeSpan.FromMilliseconds(240),
                EasingFunction = ease,
            });
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressNav || _pages.Count == 0) return;
        if (sender is not RadioButton { Tag: string key }) return;

        if (_settingsOpen) CloseVersionSettings();
        ShowPage(key);
    }

    /// <summary>在主窗口内从底部滑出版本设置。</summary>
    public event Action? VersionSelectionChanged;

    /// <summary>实例被安装 / 重命名 / 删除后刷新启动页与版本列表。</summary>
    public void RefreshInstances()
    {
        if (_pages.TryGetValue("launch", out var page) && page is LaunchPage launch) launch.RefreshVersions();
        VersionSelectionChanged?.Invoke();
    }

    public event Action<Account?>? AccountChanged;

    public void OpenAccountPicker()
    {
        AccountService.EnsureDefault();

        var picker = new AccountPickerView();
        picker.Configure(AccountService.All, AccountService.Current);
        picker.AccountChosen += account => AccountChanged?.Invoke(account);
        picker.Closed += () =>
        {
            AccountHost.Visibility = Visibility.Collapsed;
            AccountHost.Content = null;
        };

        AccountHost.Content = picker;
        AccountHost.Visibility = Visibility.Visible;
    }

    public void OpenVersionPicker()
    {
        var versions = GameLauncher.InstalledVersions();

        var picker = new VersionPickerView();
        picker.Configure(versions, AppConfig.Current.SelectedVersion);
        picker.VersionChosen += _ => VersionSelectionChanged?.Invoke();
        picker.Closed += () =>
        {
            VersionPickerHost.Visibility = Visibility.Collapsed;
            VersionPickerHost.Content = null;
        };

        VersionPickerHost.Content = picker;
        VersionPickerHost.Visibility = Visibility.Visible;
    }

    public void OpenVersionSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;

        if (_settingsView is null)
        {
            _settingsView = new VersionSettingsView();
            _settingsView.CloseRequested += (_, _) => CloseVersionSettings();
            _settingsView.InstanceChanged += _ => RefreshInstances();
        }

        var version = AppConfig.Current.SelectedVersion;
        if (string.IsNullOrWhiteSpace(version) || !Directory.Exists(Path.Combine(GamePaths.VersionsDir, version)))
            version = GameLauncher.InstalledVersions().FirstOrDefault() ?? string.Empty;

        _settingsView.Load(version);

        SettingsHost.Content = _settingsView;
        SettingsHost.Visibility = Visibility.Visible;

        var translate = (TranslateTransform)SettingsHost.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        SettingsHost.Opacity = 0;
        translate.Y = 320;
        SettingsHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(320, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });

        // 主界面标题栏按钮随浮层升起溶解
        var dissolve = new CubicEase { EasingMode = EasingMode.EaseOut };
        MenuButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = dissolve });
        CloseButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = dissolve });
        NavTabs.BeginAnimation(OpacityProperty,
            new DoubleAnimation(NavTabs.Opacity, 0, TimeSpan.FromMilliseconds(160)));
    }

    private void CloseVersionSettings()
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;

        var translate = (TranslateTransform)SettingsHost.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        SettingsHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

        var slide = new DoubleAnimation(0, 320, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
        slide.Completed += (_, _) =>
        {
            SettingsHost.Visibility = Visibility.Collapsed;

            // 清掉动画后再复原标题栏与导航层状态
            MenuButton.BeginAnimation(OpacityProperty, null);
            CloseButton.BeginAnimation(OpacityProperty, null);
            NavTabs.BeginAnimation(OpacityProperty, null);
            MenuButton.Opacity = 1;
            CloseButton.Opacity = 1;

            _suppressNav = true;
            NavLaunch.IsChecked = _currentPage == "launch";
            NavDownload.IsChecked = _currentPage == "download";
            NavSettings.IsChecked = _currentPage == "settings";
            _suppressNav = false;

            SetNavExpanded(_currentPage != "launch", animate: false);
        };
        translate.BeginAnimation(TranslateTransform.YProperty, slide);

        // 标题栏按钮随浮层退场而复原
        var appear = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        MenuButton.BeginAnimation(OpacityProperty, appear);
        CloseButton.BeginAnimation(OpacityProperty, appear);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e) => SetNavExpanded(!_navExpanded);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
