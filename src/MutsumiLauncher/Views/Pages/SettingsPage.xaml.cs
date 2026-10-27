using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views.Pages;

public partial class SettingsPage : UserControl
{
    private bool _loaded;
    private bool _memoryLoading;
    private bool _themeLoading;
    private bool _isolationLoading;
    private readonly List<Border> _swatches = [];

    public SettingsPage()
    {
        InitializeComponent();

        VersionText.Text = AppInfo.DisplayName;
        UpdateStatus.Text = $"版本 {AppInfo.Version} · WPF · .NET 10 · 开源";

        GameDirText.Text = GamePaths.Root;
        DataDirText.Text = AppPaths.DataRoot;
        LogPathText.Text = AppLog.LogPath;

        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;

            LoadTheme();
            LoadIsolation();
            LoadSourceInfo();
            LoadMemoryInfo();
            LoadCacheInfo();
            await LoadJavaAsync();
        };
    }

    // ==================== 左侧分类 ====================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string key } || BasicScroll is null) return;

        // 切换分类时，把滑出来的下载源子页收回去
        if (SourceHost.Visibility == Visibility.Visible) HideSourceImmediately();

        BasicScroll.Visibility = key == "basic" ? Visibility.Visible : Visibility.Collapsed;
        LaunchScroll.Visibility = key == "launch" ? Visibility.Visible : Visibility.Collapsed;
        DownloadScroll.Visibility = key == "download" ? Visibility.Visible : Visibility.Collapsed;
        AboutScroll.Visibility = key == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== 内存 ====================

    private static int TotalMemoryMb => SystemMemory.TotalMb;

    private static string FormatMemory(int mb) =>
        mb >= 1024 ? $"{mb / 1024.0:0.#} GB" : $"{mb} MB";

    private static int AutoMemoryMb() => SystemMemory.AutoMb;

    private void LoadMemoryInfo()
    {
        _memoryLoading = true;

        var config = AppConfig.Current;
        // 以 MemoryAuto 为准：自动模式下 MemoryMb 存的也是那个算出来的值（>0），不能再拿它判断模式
        var manual = !config.MemoryAuto;

        MemoryManualRadio.IsChecked = manual;
        MemoryAutoRadio.IsChecked = !manual;

        MemorySlider.Maximum = Math.Max(4096, TotalMemoryMb);
        MemorySlider.Value = Math.Clamp(manual ? config.MemoryMb : AutoMemoryMb(), 512, MemorySlider.Maximum);
        MemorySlider.IsEnabled = manual;

        MemoryHint.Text = $"检测到物理内存 {FormatMemory(TotalMemoryMb)}：自动配置取一半，" +
                          $"手动最多可以拉到 {FormatMemory(TotalMemoryMb)}。版本设置里可以给单个实例单独指定。";
        var shown = (int)MemorySlider.Value;
        MemoryValueText.Text = manual ? FormatMemory(shown) : $"{FormatMemory(shown)}（自动）";

        _memoryLoading = false;
    }

    private void MemoryMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_memoryLoading || MemorySlider is null) return;

        var manual = MemoryManualRadio.IsChecked == true;
        MemorySlider.IsEnabled = manual;

        var value = manual ? (int)MemorySlider.Value : AutoMemoryMb();

        AppConfig.Current.MemoryMb = value;
        AppConfig.Current.MemoryAuto = !manual;
        AppConfig.Current.Save();

        MemoryValueText.Text = manual ? FormatMemory(value) : $"{FormatMemory(value)}（自动）";
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_memoryLoading || MemoryValueText is null || MemoryManualRadio?.IsChecked != true) return;

        var value = (int)e.NewValue;

        AppConfig.Current.MemoryMb = value;
        AppConfig.Current.MemoryAuto = false;
        AppConfig.Current.Save();

        MemoryValueText.Text = FormatMemory(value);
    }

    // ==================== 主题 ====================

    private void LoadTheme()
    {
        _themeLoading = true;

        var config = AppConfig.Current;
        var mode = config.ThemeMode switch
        {
            "dark" => AppTheme.Dark,
            "custom" => AppTheme.Custom,
            _ => AppTheme.Light,
        };

        ThemeLightRadio.IsChecked = mode == AppTheme.Light;
        ThemeDarkRadio.IsChecked = mode == AppTheme.Dark;
        ThemeCustomRadio.IsChecked = mode == AppTheme.Custom;

        ThemeBaseLightRadio.IsChecked = config.ThemeBase != "dark";
        ThemeBaseDarkRadio.IsChecked = config.ThemeBase == "dark";

        AccentHexBox.Text = ThemeService.NormalizeHex(config.ThemeAccent);
        AccentHint.Text = mode == AppTheme.Custom ? "已应用" : "选个色值会切到自定义";

        ThemeCustomPanel.Visibility = mode == AppTheme.Custom ? Visibility.Visible : Visibility.Collapsed;

        BuildSwatches(ThemeService.NormalizeHex(config.ThemeAccent));

        _themeLoading = false;
    }

    private void ThemeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_themeLoading || ThemeCustomPanel is null) return;

        var mode = ThemeDarkRadio.IsChecked == true ? AppTheme.Dark
            : ThemeCustomRadio.IsChecked == true ? AppTheme.Custom
            : AppTheme.Light;

        ThemeCustomPanel.Visibility = mode == AppTheme.Custom ? Visibility.Visible : Visibility.Collapsed;
        ThemeService.SetTheme(mode, AccentHexBox.Text, ThemeBaseDarkRadio.IsChecked == true);

        AccentHint.Text = mode == AppTheme.Custom ? "已应用" : "选个色值会切到自定义";
    }

    private void ThemeBase_Checked(object sender, RoutedEventArgs e)
    {
        if (_themeLoading || ThemeCustomRadio is null) return;

        ApplyCustomTheme(AccentHexBox.Text, ThemeBaseDarkRadio.IsChecked == true);
    }

    private void ApplyAccent_Click(object sender, RoutedEventArgs e)
    {
        var hex = ThemeService.NormalizeHex(AccentHexBox.Text);
        AccentHexBox.Text = hex;

        ApplyCustomTheme(hex, ThemeBaseDarkRadio.IsChecked == true);
    }

    /// <summary>换成自定义主题（主色一律走这里，顺带把单选按钮同步过去）。</summary>
    private void ApplyCustomTheme(string? hex, bool darkBase)
    {
        var accent = ThemeService.NormalizeHex(hex);

        _themeLoading = true;
        ThemeCustomRadio.IsChecked = true;
        ThemeCustomPanel.Visibility = Visibility.Visible;
        _themeLoading = false;

        ThemeService.SetTheme(AppTheme.Custom, accent, darkBase);

        AccentHint.Text = "已应用";
        UpdateSwatchSelection(accent);
    }

    private void BuildSwatches(string selected)
    {
        if (_swatches.Count == 0)
        {
            foreach (var (name, hex) in ThemeService.AccentPresets)
            {
                var brush = new SolidColorBrush(ThemeService.ParseColor(hex));
                brush.Freeze();

                var swatch = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(11),
                    Background = brush,
                    Margin = new Thickness(0, 0, 9, 9),
                    Cursor = Cursors.Hand,
                    Tag = hex,
                    ToolTip = name,
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
                };

                swatch.MouseLeftButtonUp += Swatch_Click;
                AccentSwatches.Children.Add(swatch);
                _swatches.Add(swatch);
            }
        }

        UpdateSwatchSelection(selected);
    }

    private void UpdateSwatchSelection(string selected)
    {
        foreach (var swatch in _swatches)
        {
            var hex = swatch.Tag as string ?? string.Empty;
            swatch.BorderBrush = string.Equals(hex, selected, StringComparison.OrdinalIgnoreCase)
                ? (Brush)(TryFindResource("Ink") ?? Brushes.Transparent)
                : Brushes.Transparent;
        }
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string hex }) return;

        AccentHexBox.Text = hex;
        ApplyCustomTheme(hex, ThemeBaseDarkRadio.IsChecked == true);
    }

    // ==================== 版本隔离 ====================

    private void LoadIsolation()
    {
        _isolationLoading = true;
        IsolationCombo.SelectedIndex = AppConfig.Current.VersionIsolation ? 0 : 1;
        _isolationLoading = false;
    }

    private void Isolation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isolationLoading) return;

        AppConfig.Current.VersionIsolation = IsolationCombo.SelectedIndex == 0;
        AppConfig.Current.Save();
    }

    // ==================== 其它 ====================

    private async void RefreshJava_Click(object sender, RoutedEventArgs e) => await LoadJavaAsync();

    private void LoadSourceInfo()
    {
        var config = AppConfig.Current;
        var vanillaMode = (config.VanillaMirror ?? "auto").ToLowerInvariant();
        var listMode = (config.VanillaListMirror ?? "auto").ToLowerInvariant();
        var modMode = (config.ModMirror ?? "auto").ToLowerInvariant();

        var vanilla = vanillaMode switch
        {
            "official" => "官方源",
            "mirror" => "镜像（BMCLAPI）",
            _ => "自动（测速后选择）",
        };
        var list = listMode switch
        {
            "official" => "官方源",
            "mirror" => "镜像（BMCLAPI）",
            _ => "自动（失败换源）",
        };
        var mod = modMode switch
        {
            "official" => "官方源",
            _ => "自动（官方优先，自建镜像回退）",
        };

        SourceSummary.Text = $"原版文件：{vanilla}  ·  版本清单：{list}  ·  社区资源：{mod}";
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        var view = new DownloadSourceView();
        view.Load(CloseSource, LoadSourceInfo);

        DownloadScroll.Visibility = Visibility.Collapsed;
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
            HideSourceImmediately();
            DownloadScroll.Visibility = Visibility.Visible;
        };

        SourceSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        SourceHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));
    }

    private void HideSourceImmediately()
    {
        SourceHost.Visibility = Visibility.Collapsed;
        SourceHost.Content = null;
        SourceHost.BeginAnimation(OpacityProperty, null);
        SourceSlide.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void LoadCacheInfo()
    {
        var size = CacheService.GetSize();
        CacheSizeText.Text = $"当前缓存 {CacheService.FormatSize(size)}（版本清单 + 项目图标）";
        CachePathText.Text = string.Join("  ·  ", CacheService.Roots);
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e) => OpenPath(GamePaths.Root);

    private void OpenDataDir_Click(object sender, RoutedEventArgs e) => OpenPath(AppPaths.DataRoot);

    private void OpenLogDir_Click(object sender, RoutedEventArgs e)
        => OpenPath(Path.GetDirectoryName(AppLog.LogPath) ?? AppPaths.DataRoot);

    private void OpenPath(string path)
    {
        var error = InstanceService.OpenFolder(path);
        if (error is not null)
            MessageDialog.Show(Window.GetWindow(this), "打开失败", error, "知道了", null);
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
