using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

/// <summary>
/// 首次启动的公告页：代理内核安装提示 + 免责申明。
/// 关闭按钮要等 10 秒才能点，避免用户看都不看就关掉。
/// </summary>
public partial class FirstRunDialog : Window
{
    private const int WaitSeconds = 10;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private int _remaining = WaitSeconds;
    private bool _unlocked;

    public FirstRunDialog()
    {
        InitializeComponent();

        // DisplayName 就是 "MTL beta v1.0.0 build 6" 这种完整写法
        VersionText.Text = $"{AppInfo.DisplayName} · 首次启动公告";

        BuildDisclaimer();
        UpdateCoreStatus();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _statusTimer.Tick += (_, _) => UpdateCoreStatus();
        _statusTimer.Start();

        // 公告页打开的同时开始装内核/拉订阅，等这 10 秒正好用来下载
        _ = PrepareCoreAsync();

        ContinueButton.Content = $"请稍候 {_remaining} 秒";
    }

    /// <summary>倒计时期间不允许关闭（Esc、Alt+F4 都不行）。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_unlocked) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Tick()
    {
        _remaining--;

        if (_remaining > 0)
        {
            ContinueButton.Content = $"请稍候 {_remaining} 秒";
            return;
        }

        _timer.Stop();
        _unlocked = true;

        ContinueButton.Content = "进入启动器";
        ContinueButton.IsEnabled = true;
        HintText.Text = "可以关闭这个页面了。";
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (!_unlocked) return;

        var config = AppConfig.Current;
        config.AnnouncementShown = true;
        config.Save();

        Close();
    }

    private async Task PrepareCoreAsync()
    {
        try
        {
            await ProxyCore.Shared.EnsureRunningAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "first run proxy prepare");
        }
    }

    private void UpdateCoreStatus()
    {
        if (ProxyCore.Shared.IsRunning)
        {
            CoreProgress.IsIndeterminate = false;
            CoreProgress.Value = 100;
            CoreStatus.Text = "代理内核已就绪";
            return;
        }

        if (ProxyCore.Shared.LastError is { Length: > 0 } error)
        {
            CoreProgress.IsIndeterminate = false;
            CoreProgress.Value = 0;
            CoreStatus.Text = $"代理内核没装好：{error}。不影响使用，之后可以在「设置 → 下载 → 内置代理」里重试。";
            return;
        }

        CoreProgress.IsIndeterminate = true;
        CoreStatus.Text = "正在下载代理内核（约 22 MB），下载完成后会自动启动。";
    }

    private void BuildDisclaimer()
    {
        string[] items =
        [
            "本软件是第三方开源启动器，与 Mojang Studios、Microsoft 及任何模组平台都没有从属关系，也未获得它们的背书。",
            "本软件不提供、也不分发 Minecraft 游戏本体或账号。你需要自行购买正版，并遵守 Minecraft 最终用户许可协议（EULA）。",
            "通过本软件下载的游戏、模组、整合包、光影等文件，版权归各自作者所有，请遵守对应的许可证。",
            "内置代理仅用于加速资源下载，不提供任何翻墙或其他代理服务，也不对经它访问到的内容负责。",
            "账号令牌和配置只保存在本机。除更新检查（只发送版本号）外，本软件不会上传、收集任何个人信息。",
            "使用本软件产生的风险由你自行承担，包括但不限于存档损坏、账号异常、违反第三方服务条款。",
            "本软件以 MIT 协议开源，按「现状」提供，不附带任何明示或暗示的担保。",
        ];

        for (var index = 0; index < items.Length; index++)
        {
            var row = new Grid { Margin = new Thickness(0, index == 0 ? 0 : 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new TextBlock
            {
                Text = $"{index + 1}.",
                FontSize = 12,
                Width = 20,
                Foreground = (Brush)FindResource("InkMute"),
                VerticalAlignment = VerticalAlignment.Top,
            };

            var text = new TextBlock
            {
                Text = items[index],
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Foreground = (Brush)FindResource("InkSoft"),
            };

            Grid.SetColumn(text, 1);
            row.Children.Add(dot);
            row.Children.Add(text);
            DisclaimerHost.Children.Add(row);
        }
    }
}
