using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public partial class LogViewerView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private string _instance = string.Empty;
    private string _path = string.Empty;

    public LogViewerView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => ReloadIfChanged();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public event Action? Closed;

    public void Load(string instance)
    {
        _instance = instance;
        Reload();
    }

    private void Reload()
    {
        _path = GameLogService.LatestLogPath(_instance) ?? string.Empty;
        PathText.Text = string.IsNullOrWhiteSpace(_path)
            ? $"没有找到日志（{GameLogService.LogDirFor(_instance)}），先启动一次游戏。"
            : _path;

        LogText.Text = string.IsNullOrWhiteSpace(_path)
            ? "暂无日志。"
            : GameLogService.ReadTail(_path);

        LogScroll.ScrollToEnd();
    }

    private void ReloadIfChanged()
    {
        if (AutoRefreshCheck.IsChecked != true || string.IsNullOrWhiteSpace(_path)) return;

        try
        {
            if (!File.Exists(_path)) return;

            var length = new FileInfo(_path).Length;
            if (length == _lastLength) return;

            _lastLength = length;
            Reload();
        }
        catch
        {
            // 文件被占用时忽略，下次再试
        }
    }

    private long _lastLength = -1;

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_instance)) return;

        var error = InstanceService.OpenFolder(GameLogService.LogDirFor(_instance));
        if (error is not null)
            (Window.GetWindow(this) as MainWindow)?.ShowToast("打开失败：" + error);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LogText.Text);
            (Window.GetWindow(this) as MainWindow)?.ShowToast("日志已复制到剪贴板");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "copy log");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Grid) Closed?.Invoke();
    }
}
