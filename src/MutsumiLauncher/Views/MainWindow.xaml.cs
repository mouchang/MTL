using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MutsumiLauncher.Views.Pages;

namespace MutsumiLauncher.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };
    private bool _suppressNav;

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

    private void UpdateShellClip()
    {
        if (Shell.ActualWidth <= 0 || Shell.ActualHeight <= 0) return;
        Shell.Clip = new RectangleGeometry(
            new Rect(0, 0, Shell.ActualWidth, Shell.ActualHeight), 26, 26);
    }

    private void ShowPage(string key, bool animate = true)
    {
        if (!_pages.TryGetValue(key, out var page)) return;

        _suppressNav = true;
        NavLaunch.IsChecked = key == "launch";
        NavDownload.IsChecked = key == "download";
        NavSettings.IsChecked = key == "settings";
        _suppressNav = false;

        NavTabs.Visibility = key == "launch" ? Visibility.Collapsed : Visibility.Visible;

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

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressNav || _pages.Count == 0) return;
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e) => MenuPopup.IsOpen = !MenuPopup.IsOpen;

    private void MenuNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        MenuPopup.IsOpen = false;
        ShowPage(key);
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        ShowToast("Mutsumi Launcher 0.1.0 · WPF / .NET 10");
    }

    private void MenuMinimize_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        WindowState = WindowState.Minimized;
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

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
