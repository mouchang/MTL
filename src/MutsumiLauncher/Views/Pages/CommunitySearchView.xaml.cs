using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.ViewModels;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.Pages;

public partial class CommunitySearchView : UserControl
{
    private string _kind = "mod";
    private IReadOnlyList<ModItem> _source = ModSamples.SearchResults;

    public CommunitySearchView()
    {
        InitializeComponent();
    }

    /// <summary>按分类切换标题、搜索占位与数据源。</summary>
    public void Configure(string kind, string title)
    {
        _kind = kind;
        TitleText.Text = title;
        SearchNameBox.Placeholder = kind switch
        {
            "pack" => "输入整合包名称",
            "datapack" => "输入数据包名称",
            "resourcepack" => "输入资源包名称",
            "shader" => "输入光影包名称",
            _ => "输入 Mod 名称",
        };

        // Mod 分类多一个加载器下拉；整合包分类多一个“安装已有整合包”按钮
        LoaderCombo.Visibility = kind == "mod" ? Visibility.Visible : Visibility.Collapsed;
        InstallPackButton.Visibility = kind == "pack" ? Visibility.Visible : Visibility.Collapsed;

        _source = CommunitySamples.ForKind(kind);
        SearchNameBox.Text = string.Empty;
        ResultsList.ItemsSource = _source;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        Loading.IsActive = true;
        await Task.Delay(400);

        var query = SearchNameBox.Text.Trim();
        ResultsList.ItemsSource = query.Length == 0
            ? _source
            : _source
                .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || m.EnglishName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Loading.IsActive = false;
    }

    private void InstallPack_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.ShowToast("安装已有整合包：文件选择与解析还没接上。");
    }

    private void ResetSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchNameBox.Text = string.Empty;
        ResultsList.ItemsSource = _source;
        Loading.IsActive = false;
    }
}
