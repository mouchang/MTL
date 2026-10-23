using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public partial class DownloadPage : UserControl
{
    public DownloadPage()
    {
        InitializeComponent();
        DataContext = new DownloadPageViewModel();
        Loaded += (_, _) => ResultsList.ItemsSource = ModSamples.SearchResults;
    }

    private void CommunityToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (CommunityItems is null) return;
        CommunityItems.Visibility = CommunityToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (OriginalPanel is null || CommunityPanel is null) return;
        if (sender is not RadioButton { Tag: string tag }) return;

        var original = tag == "original";
        OriginalPanel.Visibility = original ? Visibility.Visible : Visibility.Collapsed;
        CommunityPanel.Visibility = original ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        SearchLoading.IsActive = true;
        await Task.Delay(450);

        var query = SearchNameBox.Text.Trim();
        ResultsList.ItemsSource = query.Length == 0
            ? ModSamples.SearchResults
            : ModSamples.SearchResults
                .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || m.EnglishName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        SearchLoading.IsActive = false;
    }

    private void ResetSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchNameBox.Text = string.Empty;
        ResultsList.ItemsSource = ModSamples.SearchResults;
        SearchLoading.IsActive = false;
    }
}
