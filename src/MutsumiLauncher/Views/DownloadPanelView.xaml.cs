using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        Loaded += (_, _) =>
        {
            PlayOpen();
            UpdateSummary();
        };
        Unloaded += (_, _) =>
        {
            DownloadCenter.Changed -= OnChanged;
            DownloadCenter.Tasks.CollectionChanged -= OnCollectionChanged;
        };
    }

    public event Action? Closed;

    private void PlayOpen()
    {
        Card.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        Slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private void OnChanged() => Dispatcher.Invoke(UpdateSummary);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(UpdateSummary);

    private void UpdateSummary()
    {
        var active = DownloadCenter.Tasks.Where(task => !task.IsDone).ToList();

        var percent = active.Count == 0 ? 0 : active.Average(task => task.Percent);
        var speed = active.Sum(task => task.SpeedMb);

        TotalPercentText.Text = $"{percent:0}%";
        SpeedText.Text = speed <= 0.001
            ? "—"
            : speed >= 1 ? $"{speed:0.0} MB/s" : $"{speed * 1024:0} KB/s";
        RemainingText.Text = active.Count.ToString();
    }

    private void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadTask task }) DownloadCenter.Cancel(task);
    }

    private void RetryTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadTask task }) DownloadCenter.Retry(task);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) Close();
    }

    private void Close()
    {
        DownloadCenter.Changed -= OnChanged;
        DownloadCenter.Tasks.CollectionChanged -= OnCollectionChanged;
        Closed?.Invoke();
    }
}
