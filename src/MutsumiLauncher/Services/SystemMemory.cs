namespace MutsumiLauncher.Services;

/// <summary>系统物理内存（不用 P/Invoke，GC 看到的就是物理内存上限）。</summary>
public static class SystemMemory
{
    /// <summary>物理内存总量（MB），按 512 MB 向下取整。</summary>
    public static int TotalMb =>
        (int)Math.Min(int.MaxValue, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024) / 512 * 512;
}
