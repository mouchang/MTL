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
    private string _repoUrl = "https://github.com/mouchang/MTL";
    private string _ownerUrl = "https://github.com/mouchang";

    private bool _loaded;
    private bool _memoryLoading;
    private bool _themeLoading;
    private bool _isolationLoading;

    /// <summary>拖内存滑块会连续触发 ValueChanged，攒一下再落盘，别每跳一格就写一次配置。</summary>
    private readonly System.Windows.Threading.DispatcherTimer _saveDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    public SettingsPage()
    {
        InitializeComponent();

        // 首次显示时把高亮直接放到当前分类（XAML 加载阶段布局还没算好）
        Loaded += (_, _) =>
        {
            if (MemoryAutoRadio is { IsChecked: true } memoryAuto)
                Controls.SlidingHighlight.Move(MemoryIndicator, memoryAuto, MemorySegmentHost, animate: false);
            else if (MemoryManualRadio.IsChecked == true)
                Controls.SlidingHighlight.Move(MemoryIndicator, MemoryManualRadio, MemorySegmentHost, animate: false);

            foreach (var themeRadio in new[] { ThemeLightRadio, ThemeDarkRadio })
            {
                if (themeRadio.IsChecked == true)
                {
                    Controls.SlidingHighlight.Move(ThemeIndicator, themeRadio, ThemeSegmentHost, animate: false);
                    break;
                }
            }

            foreach (var child in NavPanel.Children)
            {
                if (child is RadioButton { IsChecked: true } radio)
                {
                    Controls.SlidingHighlight.Move(NavIndicator, radio, NavPanel, animate: false);
                    break;
                }
            }
        };

        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            AppConfig.Current.Save();
        };

        VersionText.Text = AppInfo.DisplayName;
        UpdateStatus.Text = $"版本 {AppInfo.Version} · WPF · .NET 10 · 开源";

        LoadCredits();

        // 开发者信息：仓库地址跟着 UpdateRepo 走，换仓库时这里自动跟着变
        var repo = string.IsNullOrWhiteSpace(AppConfig.Current.UpdateRepo)
            ? "mouchang/MTL"
            : AppConfig.Current.UpdateRepo.Trim();

        _repoUrl = repo.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? repo.TrimEnd('/')
            : $"https://github.com/{repo}";

        var owner = repo.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "mouchang";
        _ownerUrl = $"https://github.com/{owner}";

        DeveloperText.Text = owner;
        RepoText.Text = _repoUrl.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
        LicenseText.Text = "MIT · 可自由使用与修改（保留版权声明）";

        // 调试用：MTL_SETTINGS_TAB=basic|launch|download|about 直接进某个分类
        var tab = Environment.GetEnvironmentVariable("MTL_SETTINGS_TAB");
        if (!string.IsNullOrWhiteSpace(tab))
        {
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

        if (sender is RadioButton radio)
        {
            Controls.SlidingHighlight.Move(NavIndicator, radio, NavPanel, animate: true);
        }
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
        MemorySlider.Value = SnapMemory(Math.Clamp(manual ? config.MemoryMb : AutoMemoryMb(),
            512, MemorySlider.Maximum));
        MemorySlider.IsEnabled = manual;

        MemoryHint.Text = $"检测到物理内存 {FormatMemory(TotalMemoryMb)}：自动配置取一半，" +
                          $"手动最多可以拉到 {FormatMemory(TotalMemoryMb)}。版本设置里可以给单个实例单独指定。";
        var shown = (int)MemorySlider.Value;
        MemoryValueText.Text = manual ? FormatMemory(shown) : $"{FormatMemory(shown)}（自动）";

        _memoryLoading = false;
    }

    private void MemoryMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton memoryRadio && MemoryIndicator is not null && MemorySegmentHost is not null)
        {
            Controls.SlidingHighlight.Move(MemoryIndicator, memoryRadio, MemorySegmentHost, animate: true);
        }

        if (_memoryLoading || MemorySlider is null) return;

        var manual = MemoryManualRadio.IsChecked == true;
        MemorySlider.IsEnabled = manual;

        var value = manual ? (int)MemorySlider.Value : AutoMemoryMb();

        AppConfig.Current.MemoryMb = value;
        AppConfig.Current.MemoryAuto = !manual;
        AppConfig.Current.Save();

        MemoryValueText.Text = manual ? FormatMemory(value) : $"{FormatMemory(value)}（自动）";
    }

    /// <summary>滑块不在回写过程中，避免吸附时自己触发自己。</summary>
    private bool _memorySnapping;

    /// <summary>
    /// 内存吸附：1 GB 以下按 1 MB 走（512、513…1023），
    /// 1 GB 以上按 0.1 GB 走（1126、1229…），拖起来不会一跳半 G。
    /// </summary>
    private static double SnapMemory(double value)
    {
        if (value < 1024) return Math.Round(value);

        return Math.Round(value / 102.4) * 102.4;
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_memoryLoading || MemoryValueText is null || MemoryManualRadio?.IsChecked != true) return;
        if (_memorySnapping) return;

        var snapped = SnapMemory(e.NewValue);

        if (Math.Abs(e.NewValue - snapped) >= 0.5)
        {
            _memorySnapping = true;
            MemorySlider.Value = snapped;
            _memorySnapping = false;
        }

        var value = (int)snapped;

        AppConfig.Current.MemoryMb = value;
        AppConfig.Current.MemoryAuto = false;

        _saveDebounce.Stop();
        _saveDebounce.Start();

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

        var darkBase = config.ThemeBase == "dark";
        CustomBaseLightRadio.IsChecked = !darkBase;
        CustomBaseDarkRadio.IsChecked = darkBase;

        ThemeCustomPanel.Visibility = mode == AppTheme.Custom ? Visibility.Visible : Visibility.Collapsed;

        BuildCustomColorRows();

        _themeLoading = false;
    }

    private void ThemeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton themeRadio && ThemeIndicator is not null && ThemeSegmentHost is not null)
        {
            Controls.SlidingHighlight.Move(ThemeIndicator, themeRadio, ThemeSegmentHost, animate: true);
        }

        if (_themeLoading || ThemeCustomPanel is null) return;

        var mode = ThemeDarkRadio.IsChecked == true ? AppTheme.Dark
            : ThemeCustomRadio.IsChecked == true ? AppTheme.Custom
            : AppTheme.Light;

        ThemeCustomPanel.Visibility = mode == AppTheme.Custom ? Visibility.Visible : Visibility.Collapsed;

        if (mode == AppTheme.Custom) BuildCustomColorRows();

        // 切到自定义时要带上当前的起始配色，别被 SetTheme 的默认值重置成明亮
        ThemeService.SetTheme(mode, mode == AppTheme.Custom && CustomBaseDarkRadio.IsChecked == true);
    }

    /// <summary>自定义主题：按分组把「明亮 / 暗色」里不同的颜色铺成小卡片，逐项可改。</summary>
    private void BuildCustomColorRows()
    {
        if (CustomColorList is null) return;

        CustomColorList.Children.Clear();

        string[] order = ["文字", "主色", "表面与背景", "描边与分隔", "状态色", "阴影"];

        var groups = ThemeService.EditableKeys
            .GroupBy(ThemeService.KeyGroup)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var name in order.Concat(groups.Keys.Where(k => !order.Contains(k))))
        {
            if (!groups.TryGetValue(name, out var keys) || keys.Count == 0) continue;

            CustomColorList.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = (Brush)FindResource("InkMute"),
                Margin = new Thickness(2, 10, 0, 6),
            });

            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var key in keys) wrap.Children.Add(BuildColorCard(key));
            CustomColorList.Children.Add(wrap);
        }
    }

    private UIElement BuildColorCard(string key)
    {
        var current = ThemeService.CustomColorText(key);
        var customized = ThemeService.IsCustomized(key);

        var swatch = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(ThemeService.ParseColor(current)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text = ThemeService.KeyLabel(key),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var box = new TextBox
        {
            Text = current,
            Width = 96,
            Height = 24,
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("MtlTextBox"),
            ToolTip = "输入 #AARRGGBB 或 #RRGGBB",
        };

        var reset = new Button
        {
            Content = "↺",
            Width = 24,
            Height = 24,
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "恢复这一项",
            Visibility = customized ? Visibility.Visible : Visibility.Collapsed,
        };

        box.TextChanged += (_, _) =>
        {
            if (_themeLoading) return;
            if (!IsValidHex(box.Text)) return;

            ThemeService.SetCustomColor(key, box.Text);
            swatch.Background = new SolidColorBrush(ThemeService.ParseColor(box.Text));
            reset.Visibility = Visibility.Visible;
        };

        reset.Click += (_, _) =>
        {
            ThemeService.SetCustomColor(key, null);

            _themeLoading = true;
            box.Text = ThemeService.CustomColorText(key);
            _themeLoading = false;

            swatch.Background = new SolidColorBrush(ThemeService.ParseColor(box.Text));
            reset.Visibility = Visibility.Collapsed;
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(swatch, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(box, 2);
        Grid.SetColumn(reset, 3);

        row.Children.Add(swatch);
        row.Children.Add(label);
        row.Children.Add(box);
        row.Children.Add(reset);

        return new Border
        {
            Width = 258,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("HoverWash"),
            BorderBrush = (Brush)FindResource("CardBorder"),
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    private static bool IsValidHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (!value.StartsWith('#')) value = "#" + value;

        if (value.Length is not (7 or 9)) return false;

        try
        {
            _ = ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CustomBase_Checked(object sender, RoutedEventArgs e)
    {
        if (_themeLoading || ThemeCustomRadio is null) return;

        var darkBase = CustomBaseDarkRadio.IsChecked == true;

        ThemeCustomRadio.IsChecked = true;
        ThemeCustomPanel.Visibility = Visibility.Visible;

        ThemeService.SetTheme(AppTheme.Custom, darkBase);
        BuildCustomColorRows();
    }

    private void ResetCustom_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.ResetCustomColors();
        BuildCustomColorRows();
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

    private void OpenDeveloper_Click(object sender, RoutedEventArgs e) => OpenUrl(_ownerUrl);

    /// <summary>感谢名单里的一条：项目名 + 用到了什么 + 开源地址。</summary>
    public sealed record CreditEntry(string Name, string Note, string Url);

    private static readonly CreditEntry[] Credits =
    [
        new("PCL-CE（PCL 社区版）",
            "下载引擎的分源与重试策略、文件校验顺序、分页缺失处理、MC百科条目匹配思路都参考了它（Apache-2.0）",
            "https://github.com/PCL-Community/PCL-CE"),
        new("OpenBMCLAPI",
            "原版游戏文件的国内加速镜像（bangbang93 维护）",
            "https://github.com/bangbang93/openbmclapi"),
        new("MCIM",
            "CurseForge 的社区镜像，反代不可用时的兜底（未开源，指向官网）",
            "https://mcimirror.top"),
        new("MC百科",
            "Mod 的中文名、缩写与条目编号数据（由本项目自行抓取）",
            "https://www.mcmod.cn"),
        new("Modrinth / CurseForge",
            "社区资源搜索、项目与版本信息的接口来源",
            "https://modrinth.com"),
        new(".NET 与 WPF",
            "运行框架；界面没有引入任何第三方 UI 库",
            "https://github.com/dotnet/wpf"),
    ];

    private void LoadCredits()
    {
        CreditList.ItemsSource = Credits;
    }

    private void OpenCredit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CreditEntry entry }) OpenUrl(entry.Url);
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => OpenUrl(_repoUrl);

    private void OpenIssues_Click(object sender, RoutedEventArgs e) => OpenUrl(_repoUrl + "/issues");

    private void OpenUrl(string url)
    {
        var error = UrlLauncher.Open(url);
        if (error is not null)
            MessageDialog.Show(Window.GetWindow(this), "打不开链接", error, "知道了", null);
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e) => OpenPath(GamePaths.Root);

    // ==================== 数据 / 游戏目录 ====================

    /// <summary>换游戏目录：可选把现有文件一起搬过去，改完需要重启。</summary>
    private async void ChangeGameDir_Click(object sender, RoutedEventArgs e)
    {
        var picked = PickFolder("选择新的游戏目录", GamePaths.Root);
        if (picked is null) return;
        if (SamePath(picked, GamePaths.Root) || IsInside(picked, GamePaths.Root) || IsInside(GamePaths.Root, picked))
        {
            MessageDialog.Show(Window.GetWindow(this), "目录不可用",
                "新目录不能和旧目录相同，也不能互相包含。", "知道了", null);
            return;
        }

        var old = GamePaths.Root;
        var counts = PathMigrator.Measure(old);

        var migrate = MessageDialog.Show(Window.GetWindow(this), "更换游戏目录",
            $"新目录：{picked}\n\n旧目录里有 {counts.Files} 个文件（{PathMigrator.FormatSize(counts.Bytes)}）。\n" +
            "要不要一起搬过去？（不搬就留在原地，可以自己删）",
            "一起迁移", "只换目录");

        if (migrate) await MigrateAsync(old, picked, "游戏数据");

        GamePaths.SetRoot(picked);
        RefreshPaths();
        Notify("游戏目录已更新，重启启动器后生效");
    }

    /// <summary>换数据目录（配置 / 缓存 / 图标 / 日志）。</summary>
    private async void ChangeDataDir_Click(object sender, RoutedEventArgs e)
    {
        var picked = PickFolder("选择新的数据目录", AppPaths.DataRoot);
        if (picked is null) return;
        if (SamePath(picked, AppPaths.DataRoot) || IsInside(picked, AppPaths.DataRoot) || IsInside(AppPaths.DataRoot, picked))
        {
            MessageDialog.Show(Window.GetWindow(this), "目录不可用",
                "新目录不能和旧目录相同，也不能互相包含。", "知道了", null);
            return;
        }

        var old = AppPaths.DataRoot;
        var counts = PathMigrator.Measure(old);

        var migrate = MessageDialog.Show(Window.GetWindow(this), "更换数据目录",
            $"新目录：{picked}\n\n当前数据 {counts.Files} 个文件（{PathMigrator.FormatSize(counts.Bytes)}）。\n" +
            "要不要一起搬过去？（不搬的话账号和设置会留在旧目录）",
            "一起迁移", "只换目录");

        if (migrate)
        {
            await MigrateAsync(old, picked, "应用数据");
        }
        else
        {
            // 只换目录也至少把配置和账号带过去，否则重启后设置全丢
            CopyConfigFiles(old, picked);
        }

        AppPaths.SetDataRoot(picked);
        RefreshPaths();
        Notify("数据目录已更新，重启启动器后生效");
    }

    private async Task MigrateAsync(string from, string to, string label)
    {
        var task = Task.Run(() => PathMigrator.CopyDirectory(from, to));

        while (!task.IsCompleted) await Task.Delay(60);

        var result = await task;

        if (!result.Ok)
        {
            MessageDialog.Show(Window.GetWindow(this), "迁移失败", result.Error ?? "未知错误", "知道了", null);
            return;
        }

        MessageDialog.Show(Window.GetWindow(this), "迁移完成",
            $"{label}已复制到新位置（{result.Files} 个文件 / {PathMigrator.FormatSize(result.Bytes)}）。\n" +
            "旧目录没有删除，确认没问题后可以自己清理。",
            "知道了", null);
    }

    /// <summary>只换目录时，至少保证新目录里有配置和账号。</summary>
    private static void CopyConfigFiles(string from, string to)
    {
        try
        {
            Directory.CreateDirectory(to);

            foreach (var name in new[] { "config.json", "accounts.json" })
            {
                var source = Path.Combine(from, name);
                var target = Path.Combine(to, name);

                if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "copy config to new data dir");
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInside(string path, string parent)
    {
        try
        {
            var a = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var b = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string? PickFolder(string title, string start)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        try
        {
            if (Directory.Exists(start)) dialog.InitialDirectory = start;
        }
        catch
        {
            // 初始目录无效就让它自己选
        }

        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
    }

    /// <summary>提示一下：主窗口有 toast 就用 toast，否则弹个框。</summary>
    private void Notify(string message)
    {
        if (Window.GetWindow(this) is MainWindow main)
        {
            main.ShowToast(message);
            return;
        }

        MessageDialog.Show(Window.GetWindow(this), "提示", message, "知道了", null);
    }

    private void RefreshPaths()
    {
        GameDirText.Text = GamePaths.Root;
        DataDirText.Text = AppPaths.DataRoot;
        LogPathText.Text = AppLog.LogPath;
    }


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