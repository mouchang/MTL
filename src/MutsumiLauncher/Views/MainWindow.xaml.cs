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
    private string _currentPage = "launch";

    public MainWindow()
    {
        InitializeComponent();

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
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
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
