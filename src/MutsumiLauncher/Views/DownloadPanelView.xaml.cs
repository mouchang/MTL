using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public partial class DownloadPanelView : UserControl
{
    public DownloadPanelView()
    {
        InitializeComponent();
        TaskList.ItemsSource = DownloadCenter.Tasks;
        DownloadCenter.Changed += OnChanged;
        DownloadCenter.Tasks.CollectionChanged += OnCollectionChanged;
        Loaded += (_, _) => UpdateSummary();
        Unloaded += (_, _) =>
        {
            DownloadCenter.Changed -= OnChanged;
            DownloadCenter.Tasks.CollectionChanged -= OnCollectionChanged;
        };
    }

    private void OnChanged() => Dispatcher.Invoke(UpdateSummary);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(UpdateSummary);

    private void UpdateSummary()
    {
        var active = DownloadCenter.ActiveCount;
        SummaryText.Text = active == 0
            ? "当前没有进行中的任务。"
            : $"进行中 {active} 项，其余为已完成。";
    }

    private void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadTask task }) DownloadCenter.Cancel(task);
    }

    private void RetryTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadTask task }) DownloadCenter.Retry(task);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    public event Action? Closed;

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Grid) Closed?.Invoke();
    }
}
