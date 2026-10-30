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