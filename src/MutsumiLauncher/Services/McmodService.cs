using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(15));
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private static readonly ConcurrentDictionary<string, McmodEntry?> RemoteCache = new();

    /// <summary>
    /// 离线索引没命中时，去 MC百科站内搜索一次（PCL2 对不认识的 Mod 也是这么做的）。
    /// 结果写磁盘缓存，同一个名字只查一次。
    /// </summary>
    public static async Task<McmodEntry?> SearchRemoteAsync(string title,
        CancellationToken cancellationToken = default)
    {
        var keyword = title?.Trim();
        var key = Normalize(keyword);
        if (key.Length < 3) return null;

        if (RemoteCache.TryGetValue(key, out var cached)) return cached;

        if (TryLoadDisk(key, out var disk))
        {
            RemoteCache[key] = disk;
            return disk;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var url = "https://search.mcmod.cn/s?key=" + Uri.EscapeDataString(keyword!) + "&site=all&filter=0";
            using var response = await Http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                AppLog.Info($"mcmod remote search {keyword}: HTTP {(int)response.StatusCode}");
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var entry = PickFromSearch(html, keyword!);

            RemoteCache[key] = entry;
            SaveDisk(key, entry);

            if (entry is not null)
                AppLog.Info($"mcmod remote hit {keyword} -> {entry.McmodId} {entry.ChineseName}");

            return entry;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "mcmod remote search");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static readonly Regex ClassLink = new(
        "<a[^>]+href=\"(?:https?:)?//www\\.mcmod\\.cn/class/(\\d+)\\.html\"[^>]*>(.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);

    private static McmodEntry? PickFromSearch(string html, string title)
    {
        var target = Normalize(title);
        var tokens = Tokenize(title);

        McmodEntry? best = null;
        var bestScore = 0;

        foreach (Match match in ClassLink.Matches(html))
        {
            var id = match.Groups[1].Value;
            var text = WebUtility.HtmlDecode(Tag.Replace(match.Groups[2].Value, string.Empty)).Trim();

            if (string.IsNullOrWhiteSpace(text)) continue;

            var score = 0;

            if (Normalize(text).Contains(target, StringComparison.Ordinal)) score = 100;
            else if (tokens.Count > 0 && tokens.All(token => Normalize(text).Contains(token, StringComparison.Ordinal)))
                score = 60 + tokens.Count;

            if (score <= bestScore) continue;

            bestScore = score;
            best = new McmodEntry
            {
                Address = ClassUrl(int.TryParse(id, out var number) ? number : 0),
                Title = text,
                ChineseName = ChineseFromTitle(text),
                McmodId = id,
            };
        }

        return bestScore >= 60 ? best : null;
    }

    /// <summary>「[TaCZR] TaCZ：重织 (Timeless and Classics Zero: Refabricated)」→「TaCZ：重织」。</summary>
    private static string? ChineseFromTitle(string text)
    {
        var value = text;

        var bracket = value.IndexOf(']');
        if (value.StartsWith('[') && bracket > 0) value = value[(bracket + 1)..];

        var paren = value.IndexOf('(');
        if (paren > 0) value = value[..paren];

        value = value.Trim();

        return value.Length > 0 && TranslationTable.HasChinese(value) ? value : null;
    }

    private static List<string> Tokenize(string text)
    {
        var builder = new StringBuilder();

        foreach (var ch in text)
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToList();
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static string CachePath(string key) =>
        Path.Combine(AppPaths.CacheDir, "mcmod-search", key + ".json");

    private static bool TryLoadDisk(string key, out McmodEntry? entry)
    {
        entry = null;

        try
        {
            var path = CachePath(key);
            if (!File.Exists(path)) return false;

            entry = JsonSerializer.Deserialize<McmodEntry>(File.ReadAllText(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveDisk(string key, McmodEntry? entry)
    {
        try
        {
            var path = CachePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (entry is null)
            {
                File.WriteAllText(path, "null");
                return;
            }

            File.WriteAllText(path, JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "mcmod cache save");
        }
    }
}