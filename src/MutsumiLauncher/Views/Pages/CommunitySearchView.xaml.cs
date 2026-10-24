using System.IO;
using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.Pages;

public partial class CommunitySearchView : UserControl
{
    private readonly ModrinthClient _client = new();

    private string _kind = "mod";
    private string _projectType = "mod";

    /// <summary>点击结果行时请求上层打开详情页。</summary>
    public event Action<ModrinthItem>? DetailRequested;

    public CommunitySearchView()
    {
        InitializeComponent();
    }

    /// <summary>按分类切换标题、搜索占位与筛选条件，并立即拉一次 Modrinth。</summary>
    public void Configure(string kind, string title)
    {
        _kind = kind;
        _projectType = ProjectTypeOf(kind);

        TitleText.Text = title;
        SearchNameBox.Placeholder = kind switch
        {
            "pack" => "输入整合包名称",
            "datapack" => "输入数据包名称",
            "resourcepack" => "输入资源包名称",
            "shader" => "输入光影包名称",
            _ => "输入 Mod 名称",
        };

        LoaderCombo.Visibility = kind == "mod" ? Visibility.Visible : Visibility.Collapsed;
        InstallPackButton.Visibility = kind == "pack" ? Visibility.Visible : Visibility.Collapsed;

        SearchNameBox.Text = string.Empty;
        _ = RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        Loading.IsActive = true;
        StatusText.Text = "正在搜索 Modrinth…";

        try
        {
            var hits = await _client.SearchAsync(SearchNameBox.Text.Trim(), _projectType,
                GameVersion(), Loader(), Category());

            var items = hits.Select(h => new ModrinthItem { Hit = h }).ToList();
            ResultsList.ItemsSource = items;
            _ = LoadIconsAsync(items);

            StatusText.Text = hits.Count == 0
                ? "没有找到结果，换个关键词或条件试试。"
                : $"找到 {hits.Count} 个结果（来自 Modrinth）";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth search");
            ResultsList.ItemsSource = null;
            StatusText.Text = "搜索失败：" + ex.Message;
        }
        finally
        {
            Loading.IsActive = false;
        }
    }

    private static async Task LoadIconsAsync(IReadOnlyList<ModrinthItem> items)
    {
        using var semaphore = new SemaphoreSlim(6);

        var tasks = items
            .Where(item => !string.IsNullOrWhiteSpace(item.IconUrl))
            .Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var path = await IconCache.EnsureAsync(item.IconUrl!, item.ProjectId);
                    if (path is not null) item.IconPath = path;
                }
                finally
                {
                    semaphore.Release();
                }
            });

        await Task.WhenAll(tasks);
    }

    private string? GameVersion() => (VersionCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private string? Loader() =>
        _kind == "mod" ? (LoaderCombo.SelectedItem as ComboBoxItem)?.Tag as string : null;

    private string? Category() => (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string ProjectTypeOf(string kind) => kind switch
    {
        "pack" => "modpack",
        "datapack" => "datapack",
        "resourcepack" => "resourcepack",
        "shader" => "shader",
        _ => "mod",
    };

    private static string FolderFor(string projectType) => projectType switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        "datapack" => "datapacks",
        "modpack" => "modpacks",
        _ => "mods",
    };

    /// <summary>开了版本隔离就装进 versions/&lt;版本&gt;，否则装到游戏根目录。</summary>
    private static string GameDir()
    {
        var config = AppConfig.Current;
        if (config.VersionIsolation && !string.IsNullOrWhiteSpace(config.SelectedVersion))
        {
            var dir = Path.Combine(GamePaths.VersionsDir, config.SelectedVersion);
            if (Directory.Exists(dir)) return dir;
        }

        return GamePaths.Root;
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void ResetSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchNameBox.Text = string.Empty;
        VersionCombo.SelectedIndex = 0;
        if (LoaderCombo.Visibility == Visibility.Visible) LoaderCombo.SelectedIndex = 0;
        TypeCombo.SelectedIndex = 0;

        await RunSearchAsync();
    }

    private void OpenDetail_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ModrinthItem item })
            DetailRequested?.Invoke(item);
    }

    private void InstallPack_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.ShowToast("整合包自动安装还在做，目前可以从搜索结果里下载 .mrpack。");
    }
}
