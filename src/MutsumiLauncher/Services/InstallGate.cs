namespace MutsumiLauncher.Services;

/// <summary>
/// 安装并发闸门：最多两个项目同时下载，第三个开始排队。
/// 「项目」指一次完整的版本 / 加载器 / 整合包安装，而不是单个文件。
/// </summary>
public static class InstallGate
{
    private const int Slots = 2;

    private static readonly SemaphoreSlim Gate = new(Slots, Slots);
    private static int _waiting;

    public static int Active => Slots - Gate.CurrentCount;

    public static int Waiting => Volatile.Read(ref _waiting);

    /// <summary>占用一个并发位；满了就先报「排队中」，等有空位再继续。</summary>
    public static async Task<IDisposable> EnterAsync(IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 有空位直接进
        if (Gate.Wait(0))
        {
            progress?.Report(new InstallProgress(0, "开始安装…"));
            return new Slot();
        }

        var position = Interlocked.Increment(ref _waiting);

        try
        {
            for (var second = 0; ; second++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new InstallProgress(0,
                    $"排队中：前面还有 {position - 1} 个项目，当前并行 {Active} 个"));

                if (await Gate.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false))
                    return new Slot();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }

    private sealed class Slot : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            Gate.Release();
        }
    }
}
