using System.Windows;
using System.Windows.Controls;
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
            }
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
            var launcher = new GameLauncher();
            var options = new LaunchOptions(config.PlayerName, config.JavaPath, config.MemoryMb,
                config.VersionIsolation);
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
        (Window.GetWindow(this) as MainWindow)?.ShowToast("账号切换还没接上，后面接微软 / 离线登录。");
    }

    private void VersionSettings_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenVersionSettings();
    }
}
