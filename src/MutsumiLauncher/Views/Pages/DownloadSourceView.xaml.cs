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

    private void Back_Click(object sender, RoutedEventArgs e) => _onBack?.Invoke();
}
