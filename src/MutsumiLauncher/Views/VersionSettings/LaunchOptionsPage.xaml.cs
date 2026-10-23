using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class LaunchOptionsPage : UserControl
{
    private IReadOnlyList<JavaRuntime> _runtimes = [];
    private bool _loaded;
    private bool _configLoaded;

    public LaunchOptionsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadJavaAsync();
        };
    }

    private async Task LoadJavaAsync()
    {
        JavaInfoText.Text = "正在检测本机 Java…";

        try
        {
            _runtimes = await JavaDetector.DetectAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "detect java");
            _runtimes = [];
        }

        JavaCombo.Items.Clear();
        JavaCombo.Items.Add(new ComboBoxItem { Content = "自动选择" });
        foreach (var runtime in _runtimes)
        {
            JavaCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{runtime.DisplayVersion} · {runtime.Home}",
                Tag = runtime,
            });
        }

        var savedJava = AppConfig.Current.JavaPath;
        var selectedIndex = 0;
        for (var i = 1; i < JavaCombo.Items.Count; i++)
        {
            if (JavaCombo.Items[i] is ComboBoxItem { Tag: JavaRuntime runtime }
                && runtime.ExecutablePath.Equals(savedJava, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        JavaCombo.SelectedIndex = selectedIndex;

        ApplyConfig();
        _configLoaded = true;

        JavaInfoText.Text = _runtimes.Count == 0
            ? "没有检测到 Java。请安装 JDK 或在设置中手动指定路径。"
            : $"已检测到 {_runtimes.Count} 个 Java：{string.Join("、", _runtimes.Select(r => r.DisplayVersion).Distinct())}。当前为自动选择，启动器会按游戏版本要求挑一个。";
    }

    private void ApplyConfig()
    {
        var config = AppConfig.Current;

        IsolationCombo.SelectedIndex = config.VersionIsolation ? 0 : 1;

        if (config.MemoryMb > 0)
        {
            MemoryCustomRadio.IsChecked = true;
            MemorySlider.Value = Math.Clamp(config.MemoryMb, (int)MemorySlider.Minimum,
                (int)MemorySlider.Maximum);
        }

        UpdateMemoryBar();
    }

    private void UpdateMemoryBar()
    {
        if (MemoryAllocText is null) return;

        var mb = MemoryCustomRadio.IsChecked == true ? (int)MemorySlider.Value : 8192;
        MemoryAllocText.Text = $"{mb / 1024.0:0.0} GB（可用 7.1 GB）";
    }

    private void MemoryRadio_Checked(object sender, RoutedEventArgs e)
    {
        UpdateMemoryBar();
        if (!_configLoaded) return;

        var config = AppConfig.Current;
        config.MemoryMb = MemoryCustomRadio.IsChecked == true ? (int)MemorySlider.Value : 0;
        config.Save();
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateMemoryBar();
        if (!_configLoaded) return;

        if (MemoryCustomRadio.IsChecked == true)
        {
            var config = AppConfig.Current;
            config.MemoryMb = (int)MemorySlider.Value;
            config.Save();
        }
    }

    private void IsolationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_configLoaded) return;

        var config = AppConfig.Current;
        config.VersionIsolation = IsolationCombo.SelectedIndex != 1;
        config.Save();
    }

    private void JavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JavaInfoText is null) return;

        if (JavaCombo.SelectedItem is ComboBoxItem { Tag: JavaRuntime runtime })
        {
            if (_configLoaded)
            {
                var config = AppConfig.Current;
                config.JavaPath = runtime.ExecutablePath;
                config.Save();
            }

            JavaInfoText.Text = $"将会使用：{runtime.DisplayVersion}\n{runtime.ExecutablePath}";
            return;
        }

        if (_configLoaded)
        {
            var config = AppConfig.Current;
            config.JavaPath = string.Empty;
            config.Save();
        }

        JavaInfoText.Text = _runtimes.Count == 0
            ? "没有检测到 Java。"
            : "将会使用：自动选择（按游戏版本要求匹配）。";
    }
}
