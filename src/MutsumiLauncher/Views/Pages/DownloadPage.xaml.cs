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
    private string? _jumpInstance;

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

    /// <summary>从版本设置跳转到「社区资源 → Mod」，并预选版本与加载器。</summary>
    /// <summary>离开下载页时调用：取消“默认安装到跳转实例”。</summary>
    public void ClearJumpTarget() => _jumpInstance = null;

    public void OpenCommunityMod(string? instance, string? gameVersion, string? loader)
    {
        _jumpInstance = instance;

        CommunityToggle.IsChecked = true;
        CommunityItems.Visibility = Visibility.Visible;

        if (_communityView is null)
        {
            _communityView = new CommunitySearchView();
            _communityView.DetailRequested += OpenModrinthDetail;
        }

        CommunityModRadio.IsChecked = true;

        // 单选按钮已经处于选中状态时不会触发 Checked，这里保证页面已配置
        if (!ReferenceEquals(CommunityHost.Content, _communityView))
        {
            _communityView.Configure("mod", TitleFor("mod"));
            CommunityHost.Content = _communityView;
        }

        CommunityHost.Visibility = Visibility.Visible;
        OriginalPanel.Visibility = Visibility.Collapsed;

        _communityView.Preset(gameVersion, loader);
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
        _ = view.LoadAsync(item, gameVersion, () => CloseDetail(), ShowToast, _jumpInstance);
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
