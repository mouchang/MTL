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
        var vanillaOfficial = config.VanillaMirror.Equals("official", StringComparison.OrdinalIgnoreCase);
        var modOfficial = config.ModMirror.Equals("official", StringComparison.OrdinalIgnoreCase);

        Highlight(VanillaOfficial, VanillaOfficialCheck, vanillaOfficial);
        Highlight(VanillaMirror, VanillaMirrorCheck, !vanillaOfficial);
        Highlight(ModOfficial, ModOfficialCheck, modOfficial);
        Highlight(ModMirror, ModMirrorCheck, !modOfficial);

        var mode = (config.NetworkMode ?? "direct").Trim().ToLowerInvariant();
        Highlight(ProxyAuto, ProxyAutoCheck, mode == "auto");
        Highlight(ProxyDirect, ProxyDirectCheck, mode == "direct");
        Highlight(ProxySystem, ProxySystemCheck, mode == "system");
        Highlight(ProxyCustom, ProxyCustomCheck, mode == "custom");

        CustomProxyRow.Visibility = mode == "custom" ? Visibility.Visible : Visibility.Collapsed;
        ProxyHostBox.Text = config.ProxyHost;
        VanillaCdnBox.Text = config.VanillaCdnBase;
        ProxyPortBox.Text = config.ProxyPort.ToString();
    }

    private static void Highlight(Button button, TextBlock check, bool selected)
    {
        button.Background = selected ? (Brush)Application.Current.Resources["AccentWash"] : Brushes.Transparent;
        check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VanillaOfficial_Click(object sender, RoutedEventArgs e) => SetVanilla("official");

    private void VanillaMirror_Click(object sender, RoutedEventArgs e) => SetVanilla("bmclapi");

    private void ModOfficial_Click(object sender, RoutedEventArgs e) => SetMod("official");

    private void ModMirror_Click(object sender, RoutedEventArgs e) => SetMod("mcim");

    private void SetVanilla(string value)
    {
        AppConfig.Current.VanillaMirror = value;
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

    private void SaveCdn_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Current.VanillaCdnBase = VanillaCdnBox.Text.Trim();
        AppConfig.Current.CdnChosen = true;
        AppConfig.Current.Save();
        _onChanged?.Invoke();
    }

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
