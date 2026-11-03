namespace MutsumiLauncher.Services;

/// <summary>
/// 项目 → MC百科链接。只负责拼地址，条目匹配交给 <see cref="McmodIndex"/>。
/// </summary>
public static class McmodService
{
    /// <summary>百科条目地址。</summary>
    public static string ClassUrl(int id) => "https://www.mcmod.cn/class/" + id + ".html";

    /// <summary>站内搜索地址：索引里找不到条目时的兜底。</summary>
    public static string SearchUrl(string? keyword) =>
        "https://www.mcmod.cn/s?key=" + Uri.EscapeDataString(keyword?.Trim() ?? string.Empty) + "&site=all&filter=0";
}
