using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace MutsumiLauncher.Services;

/// <summary>一个下载任务（版本 / 加载器 / Mod 文件）。</summary>
public sealed class DownloadTask : INotifyPropertyChanged
{
    private double _percent;
    private string _status = "准备中…";
    private string _speedText = string.Empty;
    private bool _isDone;

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public required string Title { get; init; }

    public bool Indeterminate { get; init; }

    public double Percent
    {
        get => _percent;
        set
        {
            if (Math.Abs(_percent - value) < 0.01) return;
            _percent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Percent)));
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    /// <summary>最近一次测得的下载速度（MB/s），用于汇总。</summary>
    public double SpeedMb { get; set; }

    /// <summary>最近一次真正收到字节的时间（UTC）。超过 3 秒就认为速度已失效。</summary>
    public DateTime SpeedStamp { get; set; } = DateTime.MinValue;

    /// <summary>速度是否还有效（太久没收到字节就别再显示旧值）。</summary>
    public bool SpeedFresh => (DateTime.UtcNow - SpeedStamp).TotalSeconds < 3;

    public string SpeedText
    {
        get => _speedText;
        set
        {
            if (_speedText == value) return;
            _speedText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpeedText)));
        }
    }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value) return;
            _isDone = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDone)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CancelVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RetryVisibility)));
        }
    }

    /// <summary>取消下载/安装。</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    public CancellationToken Token => Cancellation.Token;

    /// <summary>取消后可以重新执行的动作（由调用方设置）。</summary>
    public Action? RetryAction { get; set; }

    public Visibility CancelVisibility => _isDone ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RetryVisibility =>
        _isDone && RetryAction is not null ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>全局下载任务中心：供右下角浮标与下载面板读取。</summary>
public static class DownloadCenter
{
    private static readonly Dictionary<string, (DateTime Time, long Bytes)> SpeedSamples = new();

    public static ObservableCollection<DownloadTask> Tasks { get; } = [];

    public static event Action? Changed;

    public static int ActiveCount => Tasks.Count(task => !task.IsDone);

    public static DownloadTask Begin(string title, bool indeterminate = false)
    {
        var task = new DownloadTask { Title = title, Indeterminate = indeterminate };
        Tasks.Insert(0, task);

        AppLog.Info($"download task begin: {title}（活动 {ActiveCount}）");
        SpeedSamples[task.Id] = (DateTime.UtcNow, 0);
        Changed?.Invoke();
        return task;
    }

    public static void Cancel(DownloadTask task)
    {
        try
        {
            task.Cancellation.Cancel();
            task.Status = "正在取消…";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "cancel download task");
        }
    }

    public static void Retry(DownloadTask task)
    {
        var action = task.RetryAction;
        if (action is null) return;

        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "retry download task");
            task.Status = "重试失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 全局速度采样：保留最近 3 秒的 (时间, 累计字节)，
    /// 用窗口两端的差值算速度——比「每次 Report 各算一段再相加」稳得多。
<<<<<<< HEAD
    /// 这个做法和 PCL-CE 的 RefreshStat 一致（它保留 30 个样本做加权平均）。
=======
    /// 这个做法参考 PCL-CE 的 RefreshStat（它保留 30 个样本做加权平均）。
>>>>>>> main
    /// </summary>
    private static readonly List<(DateTime At, long Bytes)> GlobalSamples = [];

    private static readonly object GlobalLock = new();

    /// <summary>全局下载速度（MB/s），由 <see cref="SampleGlobalSpeed"/> 刷新。</summary>
    public static double GlobalSpeedMb { get; private set; }

    /// <summary>面板定时调用：采样并重算全局速度。</summary>
    public static void SampleGlobalSpeed()
    {
        var now = DateTime.UtcNow;
        var total = DownloadEngine.TotalReceived;

        lock (GlobalLock)
        {
            GlobalSamples.Add((now, total));

            // 只留 4 秒，跨过 3 秒窗口再算
            while (GlobalSamples.Count > 2 && (now - GlobalSamples[0].At).TotalSeconds > 4)
                GlobalSamples.RemoveAt(0);

            var first = GlobalSamples[0];
            var span = (now - first.At).TotalSeconds;

            GlobalSpeedMb = span >= 0.5 ? Math.Max(0, total - first.Bytes) / 1048576.0 / span : 0;
        }
    }

    /// <summary>上次采样时全局累计的字节数，用来给「只报百分比」的任务算速度。</summary>
    private static long _lastGlobalBytes;
    private static DateTime _lastGlobalAt = DateTime.UtcNow;

    public static void Report(DownloadTask task, string message, double? percent = null, long bytes = 0)
    {
        task.Status = message;
        if (percent is not null) task.Percent = Math.Clamp(percent.Value, 0, 100);

        // 上层很多地方只有百分比（整合包、安装器），这里用下载引擎的全局字节补速度
        if (bytes == 0)
        {
            var sampleAt = DateTime.UtcNow;
            var total = DownloadEngine.TotalReceived;
            var elapsed = (sampleAt - _lastGlobalAt).TotalSeconds;

            if (elapsed >= 1 && total > _lastGlobalBytes)
            {
                var mb = (total - _lastGlobalBytes) / 1048576.0 / elapsed;

                task.SpeedMb = mb;
                task.SpeedStamp = sampleAt;
                task.SpeedText = mb >= 1 ? $"{mb:0.0} MB/s" : $"{mb * 1024:0} KB/s";

                Changed?.Invoke();
            }

            if (elapsed >= 1)
            {
                _lastGlobalBytes = total;
                _lastGlobalAt = sampleAt;
            }

            return;
        }

        var now = DateTime.UtcNow;
        if (SpeedSamples.TryGetValue(task.Id, out var last))
        {
            var seconds = (now - last.Time).TotalSeconds;
            if (seconds >= 0.5 && bytes > last.Bytes)
            {
                var mbPerSecond = (bytes - last.Bytes) / 1048576.0 / seconds;

                // 指数平滑：瞬时窗口容易被分段抖动带偏
                task.SpeedMb = task.SpeedMb > 0
                    ? task.SpeedMb * 0.6 + mbPerSecond * 0.4
                    : mbPerSecond;
                task.SpeedStamp = now;
                task.SpeedText = mbPerSecond >= 1
                    ? $"{mbPerSecond:0.0} MB/s"
                    : $"{mbPerSecond * 1024:0} KB/s";
                SpeedSamples[task.Id] = (now, bytes);
            }
        }
        else
        {
            SpeedSamples[task.Id] = (now, bytes);
        }

        Changed?.Invoke();
    }

    public static void Complete(DownloadTask task, string message)
    {
        task.Status = message;
        task.Percent = 100;
        task.SpeedText = string.Empty;
        task.IsDone = true;
        SpeedSamples.Remove(task.Id);
        Changed?.Invoke();
    }

    public static void Remove(DownloadTask task)
    {
        Tasks.Remove(task);
        SpeedSamples.Remove(task.Id);
        Changed?.Invoke();
    }
}