using System.Diagnostics;

namespace MutsumiLauncher.Services;

/// <summary>
/// 全局下载限速（令牌桶）。所有下载线程共用一份预算，
/// 每个线程读完一块就按额度等待，总带宽稳定在设定值附近。
/// </summary>
public sealed class SpeedLimiter
{
    private readonly object _gate = new();
    private double _tokens;
    private long _lastTicks;
    private long _perSecond;

    /// <summary>当前限速（字节/秒），0 表示不限速。</summary>
    public long BytesPerSecond
    {
        get
        {
            lock (_gate) return _perSecond;
        }
    }

    /// <summary>设置限速；设为 0 立即解除限制。</summary>
    public void Configure(long bytesPerSecond)
    {
        lock (_gate)
        {
            if (_perSecond == bytesPerSecond) return;

            _perSecond = Math.Max(0, bytesPerSecond);
            _lastTicks = Stopwatch.GetTimestamp();
            _tokens = _perSecond > 0 ? Capacity / 4 : 0;
        }
    }

    /// <summary>预支 <paramref name="bytes"/> 字节额度，不够就等够为止。</summary>
    public async Task AcquireAsync(int bytes, CancellationToken cancellationToken = default)
    {
        long perSecond;
        TimeSpan wait;

        lock (_gate)
        {
            perSecond = _perSecond;
            if (perSecond <= 0) return;

            Refill();

            _tokens -= bytes;
            if (_tokens >= 0) return;

            var deficit = -_tokens;
            _tokens = 0;
            wait = TimeSpan.FromSeconds(deficit / perSecond);
        }

        if (wait <= TimeSpan.Zero) return;

        // 等待期间不应该再累积额度（这段等待本身就是"还债"），否则实际速度会是设定值的两倍
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(wait.TotalMilliseconds, 1000)),
            cancellationToken);

        lock (_gate)
        {
            _lastTicks = Stopwatch.GetTimestamp();
            if (_tokens < 0) _tokens = 0;
        }
    }

    private double Capacity => _perSecond > 0 ? Math.Max(128 * 1024, _perSecond / 4.0) : 0;

    private void Refill()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;

        if (_perSecond <= 0) return;

        _tokens = Math.Min(Capacity, _tokens + elapsed * _perSecond);
    }
}
