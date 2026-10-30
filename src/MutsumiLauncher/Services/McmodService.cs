using System.Text;

namespace MutsumiLauncher.Services;

/// <summary>MC百科上的一条条目。</summary>
public sealed class McmodEntry
{
    /// <summary>条目地址：命中百科页是 /class/{id}.html，没命中是站内搜索页。</summary>
    public required string Address { get; init; }

    public required string Title { get; init; }

    public string? ChineseName { get; init; }

    public string? Abbr { get; init; }

    public string? McmodId { get; init; }

    /// <summary>是否是精确命中的百科页（false 表示只给了搜索页）。</summary>
    public bool IsExact => !string.IsNullOrWhiteSpace(McmodId);
}

/// <summary>
/// 项目 → MC百科链接。全程离线，不调第三方接口：
///
/// 1. 先用离线索引（mcmod-zh.json.gz）按 slug / 标题精确匹配，命中就用
///    <c>https://www.mcmod.cn/class/{id}.html</c> 并带上中文名；
/// 2. 匹配不上就退回站内搜索页（PCL2 对不认识的 Mod 也是这么处理的），
///    这样按钮在任何情况下都有意义，也不会指到错误的条目上。
/// </summary>
public static class McmodService
{
    /// <summary>百科条目地址。</summary>
    public static string ClassUrl(int id) => $"https://www.mcmod.cn/class/{id}.html";

    /// <summary>站内搜索地址：匹配不上时的兜底。</summary>
    public static string SearchUrl(string keyword) =>
        "https://www.mcmod.cn/s?key=" + Uri.EscapeDataString(keyword?.Trim() ?? string.Empty) + "&site=all&filter=0";

    /// <summary>按 slug / 标题找百科条目，永远返回一个可用的链接。</summary>
    public static McmodEntry? Lookup(string title, string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(slug)) return null;

        var match = McmodIndex.Find(slug, title);

        if (match is not null)
        {
            return new McmodEntry
            {
                Address = match.Url,
                Title = match.English,
                ChineseName = match.Chinese,
                Abbr = match.Abbr,
                McmodId = match.Id.ToString(),
            };
        }

        var keyword = !string.IsNullOrWhiteSpace(title) ? title : slug!;

        return new McmodEntry
        {
            Address = SearchUrl(keyword),
            Title = keyword,
        };
    }
}
