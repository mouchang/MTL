using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views.Pages;

public partial class DownloadSourceView : UserControl
{
    private Action? _onBack;
    private Action? _onChanged;

    public DownloadSourceView()
    {
        InitializeComponent();
    }

    public void Load(Action onBack, Action onChanged)
    {
        _onBack = onBack;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        var config = AppConfig.Current;
        var vanilla = (config.VanillaMirror ?? "auto").ToLowerInvariant();
        var list = (config.VanillaListMirror ?? "auto").ToLowerInvariant();
        var mod = (config.ModMirror ?? "auto").ToLowerInvariant();

        Highlight(VanillaAuto, VanillaAutoCheck, vanilla is not ("official" or "mirror"));
        Highlight(VanillaOfficial, VanillaOfficialCheck, vanilla == "official");
        Highlight(VanillaMirror, VanillaMirrorCheck, vanilla == "mirror");
        Highlight(ListAuto, ListAutoCheck, list is not ("official" or "mirror"));
        Highlight(ListOfficial, ListOfficialCheck, list == "official");
        Highlight(ListMirror, ListMirrorCheck, list == "mirror");
        Highlight(ModAuto, ModAutoCheck, mod != "official");
        Highlight(ModOfficial, ModOfficialCheck, mod == "official");

        var mode = (config.NetworkMode ?? "direct").Trim().ToLowerInvariant();
        Highlight(ProxyAuto, ProxyAutoCheck, mode == "auto");
        Highlight(ProxyDirect, ProxyDirectCheck, mode == "direct");
        Highlight(ProxySystem, ProxySystemCheck, mode == "system");
        Highlight(ProxyCustom, ProxyCustomCheck, mode == "custom");

        CustomProxyRow.Visibility = mode == "custom" ? Visibility.Visible : Visibility.Collapsed;
        ProxyHostBox.Text = config.ProxyHost;
        ProxyPortBox.Text = config.ProxyPort.ToString();

        CurseForgeStatus.Text = CurseForgeClient.IsProxied
            ? $"CurseForge：官方接口（经 {config.CurseForgeProxy}）"
            : "CurseForge：社区镜像（官方反代不可用时的兜底，一次搜索要十几秒）";

        RefreshSpeed();
        RefreshBuiltIn();
    }

    private void RefreshBuiltIn()
    {
        var config = AppConfig.Current;

        BuiltInToggle.Content = config.BuiltInProxyEnabled ? "停用" : "启用";

        if (!config.BuiltInProxyEnabled)
        {
            BuiltInStatus.Text = "已停用：资源下载只走直连 / 系统代理。";
            return;
        }

        BuiltInStatus.Text = ProxyCore.Shared.IsRunning
            ? $"运行中 · 端口 {ProxyCore.Shared.Port} · {ProxyCore.Shared.ActiveNode}"
            : "已启用：取用资源时自动启动，完成后自动停止。";
    }

    private void BuiltInToggle_Click(object sender, RoutedEventArgs e)
    {
        var config = AppConfig.Current;
        config.BuiltInProxyEnabled = !config.BuiltInProxyEnabled;
        config.Save();

        if (!config.BuiltInProxyEnabled) ProxyCore.Shared.Stop("用户停用");

        RefreshBuiltIn();
        _onChanged?.Invoke();
    }

    private async void BuiltInTest_Click(object sender, RoutedEventArgs e)
    {
        BuiltInTest.IsEnabled = false;
        BuiltInStatus.Text = "正在启动内核自检…（首次会下载约 20 MB 内核，请稍候）";

        try
        {
            var (ok, milliseconds, message) = await ProxyCore.Shared.RunSelfTestAsync();

            BuiltInStatus.Text = ok
                ? $"自检通过 · {milliseconds} ms · {message}（内核已停止）"
                : $"自检失败 · {message}（内核已停止，下载会自动回落直连）";
        }
        catch (Exception ex)
        {
            BuiltInStatus.Text = "自检失败：" + ex.Message;
        }
        finally
        {
            BuiltInTest.IsEnabled = true;
        }
    }

    private void RefreshSpeed()
    {
        var current = AppConfig.Current.MaxDownloadSpeedKbps;

        HighlightChip(SpeedUnlimited, SpeedUnlimitedText, current <= 0);
        HighlightChip(Speed1M, Speed1MText, current == 1024);
        HighlightChip(Speed4M, Speed4MText, current == 4096);
        HighlightChip(Speed10M, Speed10MText, current == 10240);
    }

    private static void HighlightChip(Button button, TextBlock text, bool selected)
    {
        button.Background = selected
            ? (Brush)Application.Current.Resources["Accent"]
            : Brushes.Transparent;

        text.Foreground = selected
            ? Brushes.White
            : (Brush)Application.Current.Resources["InkSoft"];

        text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string raw } || !int.TryParse(raw, out var kbps)) return;

        AppConfig.Current.MaxDownloadSpeedKbps = Math.Max(0, kbps);
        AppConfig.Current.Save();
        Refresh();
        _onChanged?.Invoke();
    }

    private static void Highlight(Button button, TextBlock check, bool selected)
    {
        button.Background = selected ? (Brush)Application.Current.Resources["AccentWash"] : Brushes.Transparent;
        check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VanillaAuto_Click(object sender, RoutedEventArgs e) => SetVanilla("auto");

    private void VanillaOfficial_Click(object sender, RoutedEventArgs e) => SetVanilla("official");

    private void VanillaMirror_Click(object sender, RoutedEventArgs e) => SetVanilla("mirror");

    private void ModAuto_Click(object sender, RoutedEventArgs e) => SetMod("auto");

    private void ModOfficial_Click(object sender, RoutedEventArgs e) => SetMod("official");

    private void SetVanilla(string value)
    {
        AppConfig.Current.VanillaMirror = value;
        AppConfig.Current.Save();
        Refresh();
        _onChanged?.Invoke();
    }

    private void ListAuto_Click(object sender, RoutedEventArgs e) => SetList("auto");

    private void ListOfficial_Click(object sender, RoutedEventArgs e) => SetList("official");

    private void ListMirror_Click(object sender, RoutedEventArgs e) => SetList("mirror");

    private void SetList(string value)
    {
        AppConfig.Current.VanillaListMirror = value;
        AppConfig.Current.Save();
        Refresh();
        _onChanged?.Invoke();
    }

    private void SetMod(string value)
    {
        AppConfig.Current.ModMirror = value;
        AppConfig.Current.Save();
        Refresh();
        _onChanged?.Invoke();
    }

    private void ProxyAuto_Click(object sender, RoutedEventArgs e) => SetProxyMode("auto");

    private void ProxyDirect_Click(object sender, RoutedEventArgs e) => SetProxyMode("direct");

    private void ProxySystem_Click(object sender, RoutedEventArgs e) => SetProxyMode("system");

    private void ProxyCustom_Click(object sender, RoutedEventArgs e)
    {
        CustomProxyRow.Visibility = Visibility.Visible;
        ApplyProxy_Click(sender, e);
    }

    private void ApplyProxy_Click(object sender, RoutedEventArgs e)
    {
        var config = AppConfig.Current;
        config.NetworkMode = "custom";
        config.NetworkModeChosen = true;
        config.ProxyHost = string.IsNullOrWhiteSpace(ProxyHostBox.Text) ? "127.0.0.1" : ProxyHostBox.Text.Trim();
        if (int.TryParse(ProxyPortBox.Text.Trim(), out var port) && port > 0) config.ProxyPort = port;
        config.Save();
        Refresh();
        _onChanged?.Invoke();
    }

    private void SetProxyMode(string mode)
    {
        AppConfig.Current.NetworkMode = mode;
        AppConfig.Current.NetworkModeChosen = true;
        AppConfig.Current.Save();
        Refresh();
        _onChanged?.Invoke();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _onBack?.Invoke();
}