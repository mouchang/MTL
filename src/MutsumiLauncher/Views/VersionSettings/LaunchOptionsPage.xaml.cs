using System.IO;
using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class LaunchOptionsPage : UserControl, IInstancePage
{
    private string _version = string.Empty;
    private IReadOnlyList<JavaRuntime> _runtimes = [];
    private bool _javaLoaded;
    private bool _suppress;

    public LaunchOptionsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await EnsureJavaAsync();
    }

    public void SetInstance(string version)
    {
        _version = version;
        InstanceHintText.Text = $"这些设置只对「{version}」生效，不影响其他版本。";
        ApplySettings();
        _ = EnsureJavaAsync();
    }

    private async Task EnsureJavaAsync()
    {
        if (_javaLoaded) return;
        _javaLoaded = true;

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

        _suppress = true;

        JavaCombo.Items.Clear();
        JavaCombo.Items.Add(new ComboBoxItem { Content = "跟随全局设置", Tag = null });
        JavaCombo.Items.Add(new ComboBoxItem { Content = "自动选择（按版本匹配）", Tag = "auto" });

        foreach (var runtime in _runtimes)
        {
            JavaCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{runtime.DisplayVersion} · {runtime.Home}",
                Tag = runtime,
            });
        }

        _suppress = false;

        ApplyJavaSelection();
    }

    private void ApplySettings()
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var settings = InstanceSettings.Load(_version);
        _suppress = true;

        IsolationCombo.SelectedIndex = settings.VersionIsolation switch
        {
            true => 0,
            false => 1,
            _ => 2,
        };

        if (settings.MemoryMb > 0)
        {
            MemoryCustomRadio.IsChecked = true;
            MemorySlider.Value = Math.Clamp(settings.MemoryMb, (int)MemorySlider.Minimum,
                (int)MemorySlider.Maximum);
        }
        else if (settings.MemoryMb < 0)
        {
            MemoryAutoRadio.IsChecked = true;
        }
        else
        {
            MemoryFollowRadio.IsChecked = true;
        }

        _suppress = false;

        UpdateMemoryText();
        ApplyJavaSelection();
    }

    private void ApplyJavaSelection()
    {
        if (JavaCombo.Items.Count == 0 || string.IsNullOrWhiteSpace(_version)) return;

        var settings = InstanceSettings.Load(_version);
        var index = 0;

        if (settings.JavaPath is null)
        {
            index = 0;
        }
        else if (settings.JavaPath.Length == 0)
        {
            index = 1;
        }
        else
        {
            for (var i = 2; i < JavaCombo.Items.Count; i++)
            {
                if (JavaCombo.Items[i] is ComboBoxItem { Tag: JavaRuntime runtime }
                    && runtime.ExecutablePath.Equals(settings.JavaPath, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
        }

        _suppress = true;
        JavaCombo.SelectedIndex = index;
        _suppress = false;

        UpdateJavaInfo();
    }

    private void UpdateJavaInfo()
    {
        if (JavaInfoText is null) return;

        switch (JavaCombo.SelectedItem)
        {
            case ComboBoxItem { Tag: JavaRuntime runtime }:
                JavaInfoText.Text = $"将会使用：{runtime.DisplayVersion}\n{runtime.ExecutablePath}";
                break;

            case ComboBoxItem { Tag: "auto" }:
                JavaInfoText.Text = _runtimes.Count == 0
                    ? "没有检测到 Java。"
                    : "将会使用：自动选择（按游戏版本要求匹配）。";
                break;

            default:
                var global = AppConfig.Current.JavaPath;
                JavaInfoText.Text = string.IsNullOrWhiteSpace(global)
                    ? "跟随全局：自动选择（按游戏版本要求匹配）。"
                    : $"跟随全局：{global}";
                break;
        }
    }

    private void UpdateMemoryText()
    {
        if (MemoryAllocText is null) return;

        if (MemoryFollowRadio.IsChecked == true)
        {
            var global = AppConfig.Current;
            MemoryAllocText.Text = global.MemoryAuto
                ? $"跟随全局：自动配置 {global.EffectiveMemoryMb} MB"
                : $"跟随全局：{global.EffectiveMemoryMb} MB";
        }
        else if (MemoryAutoRadio.IsChecked == true)
        {
            MemoryAllocText.Text = "自动分配（不指定 -Xmx，由 JVM 决定）";
        }
        else
        {
            var mb = (int)MemorySlider.Value;
            MemoryAllocText.Text = $"自定义：{mb} MB（约 {mb / 1024.0:0.0} GB）";
        }
    }

    private void Save(Action<InstanceSettings> mutate)
    {
        if (_suppress || string.IsNullOrWhiteSpace(_version)) return;

        var settings = InstanceSettings.Load(_version);
        mutate(settings);
        settings.Save(_version);
    }

    private void IsolationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Save(settings => settings.VersionIsolation = IsolationCombo.SelectedIndex switch
        {
            0 => true,
            1 => false,
            _ => null,
        });
    }

    private void JavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JavaInfoText is null) return;

        Save(settings => settings.JavaPath = JavaCombo.SelectedItem switch
        {
            ComboBoxItem { Tag: JavaRuntime runtime } => runtime.ExecutablePath,
            ComboBoxItem { Tag: "auto" } => string.Empty,
            _ => null,
        });

        UpdateJavaInfo();
    }

    private void MemoryRadio_Checked(object sender, RoutedEventArgs e)
    {
        UpdateMemoryText();

        Save(settings => settings.MemoryMb = MemoryFollowRadio.IsChecked == true
            ? 0
            : MemoryAutoRadio.IsChecked == true
                ? -1
                : (int)MemorySlider.Value);
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateMemoryText();

        if (MemoryCustomRadio.IsChecked != true) return;

        Save(settings => settings.MemoryMb = (int)MemorySlider.Value);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        InstanceSettings.Delete(_version);
        ApplySettings();

        (Window.GetWindow(this) as MainWindow)?.ShowToast("已恢复为跟随全局设置。");
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var dir = Path.Combine(InstanceService.GameDirFor(_version), "logs");
        var error = InstanceService.OpenFolder(dir);
        if (error is not null)
            (Window.GetWindow(this) as MainWindow)?.ShowToast("打开失败：" + error);
    }
}
