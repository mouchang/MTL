using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views.Pages;

public partial class SettingsPage : UserControl
{
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.DisplayName;
        UpdateStatus.Text = $"版本 {AppInfo.Version} · WPF · .NET 10 · 开源";
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            LoadSourceInfo();
            LoadCacheInfo();
            await LoadJavaAsync();
        };
    }

    private async void RefreshJava_Click(object sender, RoutedEventArgs e) => await LoadJavaAsync();

    private void LoadSourceInfo()
    {
        var config = AppConfig.Current;
        var vanilla = config.VanillaMirror.Equals("official", StringComparison.OrdinalIgnoreCase)
            ? "官方源"
            : "镜像（MC 光速镜像）";
        var mod = config.ModMirror.Equals("official", StringComparison.OrdinalIgnoreCase)
            ? "官方源"
            : "镜像（MCIM）";

        SourceSummary.Text = $"原版资源：{vanilla}  ·  社区资源：{mod}";
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        var view = new DownloadSourceView();
        view.Load(CloseSource, LoadSourceInfo);

        SettingsScroll.Visibility = Visibility.Collapsed;
        SourceHost.Content = view;
        SourceHost.Visibility = Visibility.Visible;

        var slide = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        SourceSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        SourceHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private void CloseSource()
    {
        var slide = new DoubleAnimation(0, 90, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slide.Completed += (_, _) =>
        {
            SourceHost.Visibility = Visibility.Collapsed;
            SourceHost.Content = null;
            SettingsScroll.Visibility = Visibility.Visible;
        };

        SourceSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        SourceHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));
    }

    private void LoadCacheInfo()
    {
        var size = CacheService.GetSize();
        CacheSizeText.Text = $"当前缓存 {CacheService.FormatSize(size)}（版本清单 + 项目图标）";
        CachePathText.Text = string.Join("  ·  ", CacheService.Roots);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "正在检查更新…";

        try
        {
            var info = await UpdateService.CheckAsync();

            if (!info.Available)
            {
                UpdateStatus.Text = $"已经是最新版本（{UpdateService.CurrentVersion}）";
                return;
            }

            UpdateStatus.Text = $"发现新版本 {info.LatestVersion}";

            var message = $"当前版本：{UpdateService.CurrentVersion}{Environment.NewLine}"
                          + $"最新版本：{info.LatestVersion}{Environment.NewLine}{Environment.NewLine}"
                          + "是否下载并安装？";

            var confirmed = MessageDialog.Show(owner, "发现新版本", message,
                "下载并安装", "稍后");

            if (!confirmed) return;

            var task = DownloadCenter.Begin($"下载更新 {info.LatestVersion}");
            var progress = new Progress<InstallProgress>(p =>
            {
                UpdateStatus.Text = $"{p.Message} {p.Percent:0}%";
                DownloadCenter.Report(task, p.Message, p.Percent);
            });

            var setup = await UpdateService.DownloadAsync(info, progress, task.Token);

            DownloadCenter.Complete(task, "更新已下载，正在启动安装程序");
            UpdateStatus.Text = "正在启动安装程序…";

            UpdateService.InstallAndExit(setup);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus.Text = "已取消更新";
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = ex.Message;
            AppLog.Error(ex, "auto update");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
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
