using System.IO;
using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public partial class DownloadPage : UserControl
{
    private CommunitySearchView? _communityView;
    private DownloadPageViewModel? _viewModel;
    private bool _versionsRequested;

    public DownloadPage()
    {
        InitializeComponent();
        DataContext = _viewModel = new DownloadPageViewModel();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_versionsRequested) return;
        _versionsRequested = true;
        _ = _viewModel?.LoadAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel?.LoadAsync(forceRefresh: true);
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

    private async void InstallVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VersionEntry entry } || string.IsNullOrEmpty(entry.Url)) return;

        var owner = Window.GetWindow(this);
        var message = $"要安装 Minecraft {entry.Name} 吗？" + Environment.NewLine +
                      "将下载版本 JSON、客户端 jar 与依赖库到：" + Environment.NewLine +
                      GamePaths.Root;
        var confirmed = MessageDialog.Show(owner, "安装版本", message, "安装");
        if (!confirmed) return;

        InstallBar.Visibility = Visibility.Visible;
        InstallFill.Width = 0;
        InstallText.Text = $"准备安装 {entry.Name}…";

        try
        {
            var installer = new VersionInstaller();
            var progress = new Progress<InstallProgress>(p =>
            {
                InstallText.Text = $"{entry.Name}：{p.Message}";
                InstallFill.Width = InstallTrack.ActualWidth * Math.Clamp(p.Percent, 0, 100) / 100.0;
            });

            await installer.InstallAsync(entry.Name, entry.Url, progress);
            InstallText.Text = $"{entry.Name} 安装完成 → {Path.Combine(GamePaths.VersionsDir, entry.Name)}";
            (owner as MainWindow)?.ShowToast($"{entry.Name} 安装完成");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "install version");
            InstallText.Text = $"安装失败：{ex.Message}";
        }
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
