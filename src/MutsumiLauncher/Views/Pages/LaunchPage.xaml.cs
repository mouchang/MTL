using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.Pages;

public partial class LaunchPage : UserControl
{
    public LaunchPage()
    {
        InitializeComponent();
        Loaded += (_, _) => AlignAvatar();
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

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var owner = Window.GetWindow(this) as MainWindow;

        var versions = GameLauncher.InstalledVersions();
        if (versions.Count == 0)
        {
            owner?.ShowToast("还没有安装任何版本，先去下载页装一个吧。");
            return;
        }

        var original = button.Content;
        button.IsEnabled = false;
        button.Content = "启动中…";

        try
        {
            var launcher = new GameLauncher();
            var result = await launcher.LaunchAsync(versions[0], "the_box_mc");
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
