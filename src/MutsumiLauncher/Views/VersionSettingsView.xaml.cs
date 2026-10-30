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

    public VersionSettingsView()
    {
        InitializeComponent();

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

    public void Load(string version)
    {
        _version = version;
        TitleText.Text = string.IsNullOrWhiteSpace(version)
            ? "版本设置"
            : $"版本设置 · {ExternalSourceService.DisplayName(version)}";

        // 只初始化当前看得见的那一页：版本设置里每页都要扫盘（Mod 列表、存档、导出统计），
        // 一次性全加载会让打开浮层卡上好几百毫秒
        _loaded.Clear();

        if (CurrentPageKey() is { } key) InitializePage(key);

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
        if (!_pages.TryGetValue(key, out var page)) return;

        InitializePage(key);

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