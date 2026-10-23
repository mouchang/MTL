using System.Windows;
using System.Windows.Controls;
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
        var original = button.Content;

        button.IsEnabled = false;
        button.Content = "启动中…";

        await Task.Delay(900);

        button.Content = original;
        button.IsEnabled = true;

        (Window.GetWindow(this) as MainWindow)?.ShowToast("启动流程还没接上，下一步接入版本扫描与 JVM 参数。");
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
