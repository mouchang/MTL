using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views.Pages;

public partial class SettingsPage : UserControl
{
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            LoadCacheInfo();
            await LoadJavaAsync();
        };
    }

    private async void RefreshJava_Click(object sender, RoutedEventArgs e) => await LoadJavaAsync();

    private void LoadCacheInfo()
    {
        var size = CacheService.GetSize();
        CacheSizeText.Text = $"当前缓存 {CacheService.FormatSize(size)}（版本清单 + 项目图标）";
        CachePathText.Text = string.Join("  ·  ", CacheService.Roots);
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var (files, freed) = CacheService.Clear();
        LoadCacheInfo();
        CacheSizeText.Text = files == 0
            ? "缓存已经是空的。"
            : $"已清理 {files} 个文件，释放 {CacheService.FormatSize(freed)}。";
    }

    private async Task LoadJavaAsync()
    {
        JavaStatus.Text = "正在检测本机 Java…";
        JavaList.ItemsSource = null;

        IReadOnlyList<JavaRuntime> runtimes;
        try
        {
            runtimes = await JavaDetector.DetectAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "detect java");
            JavaStatus.Text = $"检测失败：{ex.Message}";
            return;
        }

        JavaList.ItemsSource = runtimes;
        JavaStatus.Text = runtimes.Count == 0
            ? "没有检测到 Java。请安装 JDK（推荐 21 或 25），或稍后手动指定路径。"
            : $"检测到 {runtimes.Count} 个 Java 运行时，版本从高到低排列。版本设置里的 Java 下拉会直接使用这份列表。";
    }
}
