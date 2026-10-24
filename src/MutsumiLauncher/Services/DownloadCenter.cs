using System.Collections.ObjectModel;
using System.ComponentModel;

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
        }
    }

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
        SpeedSamples[task.Id] = (DateTime.UtcNow, 0);
        Changed?.Invoke();
        return task;
    }

    public static void Report(DownloadTask task, string message, double? percent = null, long bytes = 0)
    {
        task.Status = message;
        if (percent is not null) task.Percent = Math.Clamp(percent.Value, 0, 100);

        var now = DateTime.UtcNow;
        if (SpeedSamples.TryGetValue(task.Id, out var last))
        {
            var seconds = (now - last.Time).TotalSeconds;
            if (seconds >= 0.5 && bytes > last.Bytes)
            {
                var mbPerSecond = (bytes - last.Bytes) / 1048576.0 / seconds;
                task.SpeedText = $"{mbPerSecond:0.0} MB/s";
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
