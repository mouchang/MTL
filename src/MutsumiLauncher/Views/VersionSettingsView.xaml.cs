using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MutsumiLauncher.Views.VersionSettings;

namespace MutsumiLauncher.Views;

public partial class VersionSettingsView : UserControl
{
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.Ordinal);

    public VersionSettingsView()
    {
        InitializeComponent();

        _pages["overview"] = new OverviewPage();
        _pages["launch"] = new LaunchOptionsPage();
        _pages["mods"] = new ModManagePage();
        _pages["export"] = new ExportPage();

        ShowPage("overview");
    }

    /// <summary>点击浮层自带的关闭按钮时触发，由主窗口负责滑回动画。</summary>
    public event EventHandler? CloseRequested;

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

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) Window.GetWindow(this)?.DragMove();
    }
}
