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
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };

        timer.Tick += OnTick;
        Loaded += (_, _) => timer.Start();
        Unloaded += (_, _) => timer.Stop();

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

    /// <summary>没有新事件时也要刷新：速度超过 3 秒没更新就会失效。</summary>
    private void OnTick(object? sender, EventArgs e) => UpdateSummary();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(UpdateSummary);

    private void UpdateSummary()
    {
        var active = DownloadCenter.Tasks.Where(task => !task.IsDone).ToList();

        var percent = active.Count == 0 ? 0 : active.Average(task => task.Percent);

        // 只统计 3 秒内真的收到过字节的任务：整合包是一个文件接一个文件下，
        // 换文件时旧的 6.7 MB/s 会一直挂在面板上，看着像卡住了。
        // 行内速度文字同样清掉，否则单个任务会一直挂着旧数字
        foreach (var task in active.Where(task => !task.SpeedFresh && task.SpeedText.Length > 0))
            task.SpeedText = string.Empty;

        var speed = active.Where(task => task.SpeedFresh).Sum(task => task.SpeedMb);

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