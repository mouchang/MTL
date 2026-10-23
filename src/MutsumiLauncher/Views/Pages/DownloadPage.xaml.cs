using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public partial class DownloadPage : UserControl
{
    private CommunitySearchView? _communityView;

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

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (OriginalPanel is null || CommunityHost is null) return;
        if (sender is not RadioButton { Tag: string kind }) return;

        if (kind == "original")
        {
            OriginalPanel.Visibility = Visibility.Visible;
            CommunityHost.Visibility = Visibility.Collapsed;
            return;
        }

        _communityView ??= new CommunitySearchView();
        _communityView.Configure(kind, TitleFor(kind));

        CommunityHost.Content = _communityView;
        CommunityHost.Visibility = Visibility.Visible;
        OriginalPanel.Visibility = Visibility.Collapsed;
    }

    private static string TitleFor(string kind) => kind switch
    {
        "pack" => "搜索 整合包",
        "datapack" => "搜索 数据包",
        "resourcepack" => "搜索 资源包",
        "shader" => "搜索 光影包",
        _ => "搜索 Mod",
    };
}
