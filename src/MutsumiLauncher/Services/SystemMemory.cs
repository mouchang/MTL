namespace MutsumiLauncher.Services;

/// <summary>系统物理内存（不用 P/Invoke，GC 看到的就是物理内存上限）。</summary>
public static class SystemMemory
{
    /// <summary>物理内存总量（MB），按 512 MB 向下取整。</summary>
    public static int TotalMb =>
        (int)Math.Min(int.MaxValue, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024) / 512 * 512;

    /// <summary>
    /// 自动配置的内存（MB）：物理内存的一半，至少 2 GB，且不超过物理内存本身。
    /// 小内存机器（物理内存不足 2 GB）直接取总量，避免 min &gt; max 让 Math.Clamp 抛异常。
    /// </summary>
    public static int AutoMb
    {
        get
        {
            var total = TotalMb;
            if (total <= 0) return 2048;

            var half = total / 2 / 512 * 512;
            return total <= 2048 ? total : Math.Clamp(half, 2048, total);
        }
    }
}
