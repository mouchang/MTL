using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MutsumiLauncher.Views.VersionSettings;

namespace MutsumiLauncher.Views;

public partial class VersionSettingsView : UserControl
{
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.Ordinal);
    private string _version = string.Empty;

    public VersionSettingsView()
    {
        InitializeComponent();

        var overview = new OverviewPage();
        overview.Renamed += OnRenamed;
        overview.Deleted += OnDeleted;

        _pages["overview"] = overview;
        _pages["launch"] = new LaunchOptionsPage();
        _pages["mods"] = new ModManagePage();
        _pages["export"] = new ExportPage();

        ShowPage("overview");
    }

    /// <summary>实例被重命名或删除（参数为新名称，删除时为 null）。</summary>
    public event Action<string?>? InstanceChanged;

    public void Load(string version)
    {
        _version = version;
        TitleText.Text = string.IsNullOrWhiteSpace(version) ? "版本设置" : $"版本设置 · {version}";

        foreach (var page in _pages.Values.OfType<IInstancePage>())
            page.SetInstance(version);
    }

    private void OnRenamed(string newName)
    {
        _version = newName;
        TitleText.Text = $"版本设置 · {newName}";
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
