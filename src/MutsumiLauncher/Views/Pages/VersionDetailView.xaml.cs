using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views.Pages;

public partial class VersionDetailView : UserControl
{
    private VersionEntry? _entry;
    private Action? _onBack;
    private Action<string>? _toast;

    public VersionDetailView()
    {
        InitializeComponent();
    }

    public void Load(VersionEntry entry, Action onBack, Action<string> toast)
    {
        _entry = entry;
        _onBack = onBack;
        _toast = toast;

        VersionText.Text = entry.Name;
        VersionCaption.Text = entry.Caption;
        BreadcrumbText.Text = $"原版资源 / {entry.Name}";
        HintText.Text = string.Empty;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _onBack?.Invoke();

    private void Loader_Click(object sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag as string ?? "加载器";
        _toast?.Invoke($"{name} 安装还没接入，目前可以下载原版。");
    }

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_entry is null || string.IsNullOrEmpty(_entry.Url)) return;

        StartButton.IsEnabled = false;
        StartButton.Content = "下载中…";
        ProgressCard.Visibility = Visibility.Visible;
        ProgressText.Text = $"准备安装 {_entry.Name}…";
        ProgressFill.Width = 0;

        try
        {
            var installer = new VersionInstaller();
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressText.Text = $"{_entry.Name}：{p.Message}";
                ProgressFill.Width = ProgressTrack.ActualWidth * Math.Clamp(p.Percent, 0, 100) / 100.0;
            });

            await installer.InstallAsync(_entry.Name, _entry.Url, progress);
            ProgressText.Text = $"{_entry.Name} 安装完成";
            HintText.Text = "可以在启动页切换到该版本并启动。";
            _toast?.Invoke($"{_entry.Name} 安装完成");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "install version");
            ProgressText.Text = "安装失败：" + ex.Message;
            _toast?.Invoke("安装失败：" + ex.Message);
        }
        finally
        {
            StartButton.IsEnabled = true;
            StartButton.Content = "开始下载";
        }
    }
}
