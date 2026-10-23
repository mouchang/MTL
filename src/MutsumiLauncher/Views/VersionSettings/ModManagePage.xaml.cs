using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class ModManagePage : UserControl
{
    private string _filter = "all";
    private string _query = string.Empty;

    public ModManagePage()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyFilter();
    }

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        _query = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string filter }) return;
        _filter = filter;
        ApplyFilter();
    }

    private void ScrollTop_Click(object sender, RoutedEventArgs e) => ModScroll.ScrollToTop();

    private void ApplyFilter()
    {
        if (ModList is null) return;

        IEnumerable<ModItem> items = ModSamples.Installed;

        items = _filter switch
        {
            "enabled" => items.Where(i => i.Enabled),
            "disabled" => items.Where(i => !i.Enabled),
            _ => items,
        };

        if (_query.Length > 0)
        {
            items = items.Where(i =>
                i.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                i.EnglishName.Contains(_query, StringComparison.OrdinalIgnoreCase));
        }

        ModList.ItemsSource = items.ToList();
    }
}
