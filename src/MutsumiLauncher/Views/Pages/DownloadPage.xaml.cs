using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public partial class DownloadPage : UserControl
{
    private CommunitySearchView? _communityView;
    private DownloadPageViewModel? _viewModel;
    private bool _versionsRequested;
    private bool _detailCoveredOriginal;

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

    private async void ImportModpack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择整合包",
            Filter = "Modrinth 整合包 (*.mrpack)|*.mrpack|整合包 / 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        var owner = Window.GetWindow(this) as MainWindow;
        var task = DownloadCenter.Begin($"导入整合包：{System.IO.Path.GetFileName(dialog.FileName)}");

        try
        {
            var progress = new Progress<InstallProgress>(p =>
                DownloadCenter.Report(task, p.Message, p.Percent));

            var instance = await ModpackImporter.ImportAsync(dialog.FileName, progress);

            DownloadCenter.Complete(task, $"已导入 {instance}");
            owner?.ShowToast($"整合包已导入：{instance}");
            owner?.RefreshInstances();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "import modpack");
            DownloadCenter.Complete(task, "失败：" + ex.Message);
            owner?.ShowToast("导入失败：" + ex.Message);
        }
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

        CloseDetail(animated: false);

        if (kind == "original")
        {
            OriginalPanel.Visibility = Visibility.Visible;
            CommunityHost.Visibility = Visibility.Collapsed;
            return;
        }

        if (_communityView is null)
        {
            _communityView = new CommunitySearchView();
            _communityView.DetailRequested += OpenModrinthDetail;
        }

        _communityView.Configure(kind, TitleFor(kind));

        CommunityHost.Content = _communityView;
        CommunityHost.Visibility = Visibility.Visible;
        OriginalPanel.Visibility = Visibility.Collapsed;
    }

    private void Version_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VersionEntry entry }) return;

        var view = new VersionDetailView();
        view.Load(entry, () => CloseDetail(), ShowToast);
        OpenDetail(view);
    }

    private void OpenModrinthDetail(ModrinthItem item, string? gameVersion)
    {
        var view = new ModrinthDetailView();
        _ = view.LoadAsync(item, gameVersion, () => CloseDetail(), ShowToast);
        OpenDetail(view);
    }

    private void OpenDetail(FrameworkElement view)
    {
        _detailCoveredOriginal = OriginalPanel.Visibility == Visibility.Visible;
        OriginalPanel.Visibility = Visibility.Collapsed;
        CommunityHost.Visibility = Visibility.Collapsed;

        DetailHost.Content = view;
        DetailHost.Visibility = Visibility.Visible;

        var slide = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        DetailSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        DetailHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private void CloseDetail(bool animated = true)
    {
        if (DetailHost.Content is null) return;

        if (!animated)
        {
            DetailSlide.BeginAnimation(TranslateTransform.XProperty, null);
            DetailHost.BeginAnimation(OpacityProperty, null);
            DetailHost.Visibility = Visibility.Collapsed;
            DetailHost.Content = null;
            RestorePanels();
            return;
        }

        var slide = new DoubleAnimation(0, 90, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slide.Completed += (_, _) =>
        {
            DetailHost.Visibility = Visibility.Collapsed;
            DetailHost.Content = null;
            RestorePanels();
        };

        DetailSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        DetailHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));
    }

    private void RestorePanels()
    {
        if (_detailCoveredOriginal)
        {
            OriginalPanel.Visibility = Visibility.Visible;
        }
        else if (CommunityHost.Content is not null)
        {
            CommunityHost.Visibility = Visibility.Visible;
        }
    }

    private void ShowToast(string message)
    {
        (Window.GetWindow(this) as MainWindow)?.ShowToast(message);
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
