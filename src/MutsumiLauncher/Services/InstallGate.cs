namespace MutsumiLauncher.Services;

/// <summary>
/// 安装并发闸门：最多两个项目同时下载，第三个开始排队。
/// 「项目」指一次完整的版本 / 加载器 / 整合包安装，而不是单个文件。
/// 并发位一定要成对借还：拿位之后哪怕上报进度失败，也要把位还回去，
/// 否则闸门会永久少一个位 —— 表现就是「前两个早就装完了，还是只能同时下一个」。
/// </summary>
public static class InstallGate
{
    private const int Slots = 2;

    private static readonly SemaphoreSlim Gate = new(Slots, Slots);
    private static int _waiting;

    public static int SlotsTotal => Slots;

    /// <summary>当前占用中的并发位。</summary>
    public static int Active => Slots - Gate.CurrentCount;

    /// <summary>正在排队的项目数。</summary>
    public static int Waiting => Volatile.Read(ref _waiting);

    /// <summary>占用一个并发位；满了就先报「排队中」，等有空位再继续。</summary>
    public static async Task<IDisposable> EnterAsync(IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 有空位直接进。拿位和上报之间不能抛异常，抛了位就漏了
        if (Gate.Wait(0))
        {
            TryReport(progress, new InstallProgress(0, "开始安装…"));
            return new Slot();
        }

        Interlocked.Increment(ref _waiting);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TryReport(progress, new InstallProgress(0,
                    $"排队中：当前并行 {Active}/{Slots} 个，前面还有 {Math.Max(0, Waiting - 1)} 个项目"));

                if (await Gate.WaitAsync(TimeSpan.FromMilliseconds(400), cancellationToken)
                        .ConfigureAwait(false))
                {
                    TryReport(progress, new InstallProgress(0, "开始安装…"));
                    return new Slot();
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }

    /// <summary>上报进度本身出错不能影响闸门，也不能把异常抛给安装流程。</summary>
    private static void TryReport(IProgress<InstallProgress>? progress, InstallProgress value)
    {
        try
        {
            progress?.Report(value);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "install gate report");
        }
    }

    private sealed class Slot : IDisposable
    {
        private readonly DateTime _since = DateTime.UtcNow;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            // 占着位太久通常意味着某次安装卡住了，留个日志方便排查
            var held = DateTime.UtcNow - _since;
            if (held > TimeSpan.FromMinutes(10))
                AppLog.Info($"install gate: 一个并发位被占用了 {held.TotalMinutes:0} 分钟");

            Gate.Release();
        }
    }
}
