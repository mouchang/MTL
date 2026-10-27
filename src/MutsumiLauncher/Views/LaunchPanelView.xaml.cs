using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public partial class LaunchPanelView : UserControl
{
    private string _instance = string.Empty;
    private string _logPath = string.Empty;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _running;

    public LaunchPanelView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) =>
        {
            CheckProcess();
            PumpLog();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    public event Action? Closed;

    public void Start(string instance, string javaPath)
    {
        _instance = instance;
        TitleText.Text = $"启动 {instance}";
        SubtitleText.Text = "正在准备启动…";
        StatusText.Text = "准备中…";
        PercentText.Text = "0%";
        ProgressFill.Width = 0;
        InstanceText.Text = instance;
        JavaText.Text = string.IsNullOrWhiteSpace(javaPath) ? "自动选择" : javaPath;
        ProcessText.Text = "—";
        LogText.Text = "—";
        KillButton.IsEnabled = false;
        LogBody.Text = string.Empty;
        _logOffset = 0;
        _running = false;
        _timer.Start();
    }

    public void Update(LaunchProgress progress)
    {
        StatusText.Text = progress.Message;
        PercentText.Text = $"{Math.Clamp(progress.Percent, 0, 100):0}%";

        var width = ProgressTrack.ActualWidth * Math.Clamp(progress.Percent, 0, 100) / 100.0;
        ProgressFill.Width = width;

        SubtitleText.Text = "正在启动，请稍候…";
    }

    public void SetRunning(int processId, string? logPath)
    {
        StatusText.Text = "游戏已启动";
        PercentText.Text = "100%";
        ProgressFill.Width = ProgressTrack.ActualWidth;
        SubtitleText.Text = $"已启动（PID {processId}）";
        ProcessText.Text = processId > 0 ? $"PID {processId}" : "—";
        KillButton.IsEnabled = processId > 0;
        _running = true;

        _logPath = logPath ?? string.Empty;
        LogText.Text = string.IsNullOrWhiteSpace(_logPath) ? "—" : Path.GetFileName(_logPath);
        _logOffset = 0;
        PumpLog();

        _timer.Start();
    }

    public void SetFailed(string message)
    {
        StatusText.Text = "启动失败";
        PercentText.Text = "";
        SubtitleText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x2E));
        KillButton.IsEnabled = false;
        _timer.Stop();
    }

    private void CheckProcess()
    {
        if (!_running) return;

        var process = GameLauncher.ActiveProcess;

        if (process is not null && !process.HasExited) return;

        _running = false;
        _timer.Stop();

        var exitCode = GameLauncher.LastExitCode ?? 0;

        if (exitCode == 0)
        {
            // 正常退出：自动收起面板
            Closed?.Invoke();
            return;
        }

        // 异常退出：保留面板并给出可读原因
        var reason = CrashAnalyzer.Analyze(_logPath, exitCode);
        SetFailed(reason);
        StatusText.Text = "游戏异常退出";
        SubtitleText.Text = $"退出代码 {exitCode}";
        KillButton.IsEnabled = false;
        PumpLog();
    }

    private void Kill_Click(object sender, RoutedEventArgs e)
    {
        GameLauncher.KillActive();

        _running = false;
        _timer.Stop();
        Closed?.Invoke();
    }

    private long _logOffset;

    /// <summary>每秒把日志文件新增的内容追加到面板上（最多保留最近 200 行）。</summary>
    private void PumpLog()
    {
        if (string.IsNullOrWhiteSpace(_logPath) || !File.Exists(_logPath)) return;

        try
        {
            using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < _logOffset) _logOffset = 0; // 文件被截断
            if (stream.Length == _logOffset) return;

            stream.Seek(_logOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var chunk = reader.ReadToEnd();
            _logOffset = stream.Position;

            if (chunk.Length == 0) return;

            var lines = (LogBody.Text + chunk).Split('\n');
            var trimmed = lines.Length > 200 ? lines[^200..] : lines;

            LogBody.Text = string.Join("\n", trimmed);
            LogScroll.ScrollToEnd();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "launch panel log");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}
