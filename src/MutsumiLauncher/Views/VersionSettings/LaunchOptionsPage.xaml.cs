using System.Windows.Controls;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class LaunchOptionsPage : UserControl
{
    private IReadOnlyList<JavaRuntime> _runtimes = [];
    private bool _loaded;

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

        JavaCombo.SelectedIndex = 0;

        JavaInfoText.Text = _runtimes.Count == 0
            ? "没有检测到 Java。请安装 JDK 或在设置中手动指定路径。"
            : $"已检测到 {_runtimes.Count} 个 Java：{string.Join("、", _runtimes.Select(r => r.DisplayVersion).Distinct())}。当前为自动选择，启动器会按游戏版本要求挑一个。";
    }

    private void JavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JavaInfoText is null) return;

        if (JavaCombo.SelectedItem is ComboBoxItem { Tag: JavaRuntime runtime })
        {
            JavaInfoText.Text = $"将会使用：{runtime.DisplayVersion}\n{runtime.ExecutablePath}";
            return;
        }

        JavaInfoText.Text = _runtimes.Count == 0
            ? "没有检测到 Java。"
            : "将会使用：自动选择（按游戏版本要求匹配）。";
    }
}
