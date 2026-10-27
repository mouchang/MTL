namespace MutsumiLauncher.ViewModels;

/// <summary>社区资源分页条显示的页码窗口：每轮 6 页。</summary>
public static class PageWindow
{
    public const int PagesPerRound = 6;

    /// <summary>
    /// 算出当前要显示的几个页码（0 基、闭区间）。
    /// 走到本轮最后一页且后面还有内容时整轮后移一页，这样停在第 6 页时
    /// 能直接看到 6-11，点「下一页」就能继续，不会卡在第六页出不去。
    /// </summary>
    public static (int Start, int End) Range(int page, int totalPages)
    {
        var total = Math.Max(1, totalPages);
        var current = Math.Clamp(page, 0, total - 1);

        var start = current / PagesPerRound * PagesPerRound;

        if (current - start == PagesPerRound - 1 && start + PagesPerRound < total)
            start = current;

        return (start, Math.Min(total - 1, start + PagesPerRound - 1));
    }
}
