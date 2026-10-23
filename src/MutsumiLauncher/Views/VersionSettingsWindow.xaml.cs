using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MutsumiLauncher.Views.VersionSettings;

namespace MutsumiLauncher.Views;

public partial class VersionSettingsWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.Ordinal);

    public VersionSettingsWindow()
    {
        InitializeComponent();

        _pages["overview"] = new OverviewPage();
        _pages["launch"] = new LaunchOptionsPage();
        _pages["mods"] = new ModManagePage();
        _pages["export"] = new ExportPage();

        Loaded += (_, _) =>
        {
            UpdateClip();
            ShowPage("overview");
        };
        SizeChanged += (_, _) => UpdateClip();
    }

    private void UpdateClip()
    {
        if (Shell.ActualWidth <= 0 || Shell.ActualHeight <= 0) return;
        Shell.Clip = new RectangleGeometry(
            new Rect(0, 0, Shell.ActualWidth, Shell.ActualHeight), 24, 24);
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;

        PageHost.Content = page;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0) return;
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
