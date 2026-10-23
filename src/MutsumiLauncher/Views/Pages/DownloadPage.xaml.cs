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
    }

    private void CommunityToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (CommunityItems is null) return;
        CommunityItems.Visibility = CommunityToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
