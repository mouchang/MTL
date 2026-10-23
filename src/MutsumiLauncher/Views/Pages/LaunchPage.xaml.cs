using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.Pages;

public partial class LaunchPage : UserControl
{
    public LaunchPage()
    {
        InitializeComponent();
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

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText("the_box_mc");
            (Window.GetWindow(this) as MainWindow)?.ShowToast("已复制用户名");
        }
        catch
        {
            // 剪贴板偶尔会被其他程序占用，忽略即可
        }
    }

    private void VersionSettings_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.ShowToast("版本设置：等实例管理做完后接到这里。");
    }
}
