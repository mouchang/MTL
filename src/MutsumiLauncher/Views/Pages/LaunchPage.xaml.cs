using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.Pages;

public partial class LaunchPage : UserControl
{
    private IReadOnlyList<string> _versions = [];
    private bool _subscribed;

    public LaunchPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AlignAvatar();
            RefreshVersions();

            if (!_subscribed && Window.GetWindow(this) is MainWindow main)
            {
                _subscribed = true;
                main.VersionSelectionChanged += RefreshVersions;
                main.AccountChanged += _ => ApplyAccount();
            }

            ApplyAccount();
        };
        NameCard.SizeChanged += (_, _) => AlignAvatar();
    }

    /// <summary>头像中心与用户名胶囊中心对齐。</summary>
    private void AlignAvatar()
    {
        var width = NameCard.ActualWidth;
        if (width <= 0) return;

        var offset = Math.Max(0, (width - AvatarHost.Width) / 2);
        AvatarHost.Margin = new Thickness(offset, 0, 0, 0);
    }

    /// <summary>读取已安装版本，恢复上次选择。</summary>
    private void RefreshVersions()
    {
        _versions = GameLauncher.InstalledVersions();
        VersionText.Text = SelectedVersion() ?? "未安装版本";

        var config = AppConfig.Current;
        if (string.IsNullOrWhiteSpace(config.SelectedVersion) && _versions.Count > 0)
        {
            config.SelectedVersion = _versions[0];
            config.Save();
        }
    }

    private void ApplyAccount()
    {
        var account = AccountService.EnsureDefault();
        AccountNameText.Text = account.Name;
        AccountTypeBadge.Text = account.TypeLabel;

        // 微软账号显示皮肤正脸，离线默认史蒂夫
        if (!string.IsNullOrWhiteSpace(account.SkinPath) && File.Exists(account.SkinPath)
            && SkinService.CreateFace(account.SkinPath) is { } face)
        {
            AvatarImage.Source = face;
        }
        else
        {
            AvatarImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/steve-head.png"));
        }
    }

    private string? SelectedVersion()
    {
        if (_versions.Count == 0) return null;

        var selected = AppConfig.Current.SelectedVersion;
        return _versions.Contains(selected) ? selected : _versions[0];
    }

    private void VersionPill_Click(object sender, RoutedEventArgs e)
    {
        if (_versions.Count == 0)
        {
            (Window.GetWindow(this) as MainWindow)?.ShowToast("还没有安装任何版本，先去下载页装一个吧。");
            return;
        }

        (Window.GetWindow(this) as MainWindow)?.OpenVersionPicker();
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var owner = Window.GetWindow(this) as MainWindow;

        var version = SelectedVersion();
        if (version is null)
        {
            owner?.ShowToast("还没有安装任何版本，先去下载页装一个吧。");
            return;
        }

        var config = AppConfig.Current;
        if (config.SelectedVersion != version)
        {
            config.SelectedVersion = version;
            config.Save();
        }

        var original = button.Content;
        button.IsEnabled = false;
        button.Content = "启动中…";

        try
        {
            var account = AccountService.Current ?? AccountService.EnsureDefault();
            var launcher = new GameLauncher();
            var options = new LaunchOptions(account.Name, account.Uuid, account.AccessToken,
                account.IsMicrosoft, config.JavaPath, config.MemoryMb, config.VersionIsolation);
            var result = await launcher.LaunchAsync(version, options);
            owner?.ShowToast(result.Message);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "launch game");
            owner?.ShowToast("启动失败：" + ex.Message);
        }
        finally
        {
            button.Content = original;
            button.IsEnabled = true;
        }
    }

    private void SwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenAccountPicker();
    }

    private void VersionSettings_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenVersionSettings();
    }
}
