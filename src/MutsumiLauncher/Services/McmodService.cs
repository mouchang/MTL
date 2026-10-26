using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MutsumiLauncher.Services;

/// <summary>MC百科上的一条条目。</summary>
public sealed class McmodEntry
{
    public required string Address { get; init; }

    public required string Title { get; init; }

    public string? ChineseName { get; init; }

    public string? SubName { get; init; }

    public string? Abbr { get; init; }

    public string? McmodId { get; init; }

    /// <summary>只认模组 / 整合包页面，条目（合成表、教程）不算。</summary>
    public bool IsMod =>
        Address.Contains("/class/", StringComparison.OrdinalIgnoreCase)
        || Address.Contains("/modpack/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// mcmod-api（https://github.com/zkitefly/mcmod-api）：
/// 英文名 → 中文名 + MC百科链接；中文关键词 → 英文名（用于社区搜索）。
/// </summary>
public static class McmodService
{
    public const string BaseUrl = "https://mcmod-api.zkitefly.eu.org";

    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(20));
    private static readonly SemaphoreSlim Gate = new(4, 4);
    private static readonly ConcurrentDictionary<string, McmodEntry?> LookupCache = new();
    private static readonly ConcurrentDictionary<string, string?> TranslateCache = new();
    private static readonly Regex Cjk = new(@"[\u4e00-\u9fff]", RegexOptions.Compiled);

    public static bool ContainsChinese(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Cjk.IsMatch(text);

    /// <summary>按关键词搜 MC百科（返回模组 / 整合包条目）。</summary>
    public static async Task<IReadOnlyList<McmodEntry>> SearchAsync(string keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return [];

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var url = $"{BaseUrl}/s/key={Uri.EscapeDataString(keyword.Trim())}";
            using var response = await Http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                AppLog.Info($"mcmod search {keyword}: HTTP {(int)response.StatusCode}");
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            var results = new List<McmodEntry>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var entry = Parse(element);
                if (entry is not null && entry.IsMod) results.Add(entry);
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "mcmod search");
            return [];
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>英文名 → MC百科条目（中文名 + 链接）。查不到返回 null，并缓存这次「没有」。</summary>
    public static async Task<McmodEntry?> LookupAsync(string name,
        CancellationToken cancellationToken = default)
    {
        var key = Normalize(name);
        if (key.Length == 0) return null;

        if (LookupCache.TryGetValue(key, out var cached)) return cached;

        var entries = await SearchAsync(name, cancellationToken).ConfigureAwait(false);
        var best = PickBest(entries, name);

        LookupCache[key] = best;
        return best;
    }

    /// <summary>中文关键词 → 英文名。搜不到英文名时退回详情接口的 subtitle。</summary>
    public static async Task<string?> TranslateToEnglishAsync(string query,
        CancellationToken cancellationToken = default)
    {
        var key = query.Trim();
        if (key.Length == 0 || !ContainsChinese(key)) return null;

        if (TranslateCache.TryGetValue(key, out var cached)) return cached;

        var entries = await SearchAsync(key, cancellationToken).ConfigureAwait(false);
        var first = entries.FirstOrDefault(entry => !ContainsChinese(entry.SubName));

        var english = first?.SubName;

        if (string.IsNullOrWhiteSpace(english) && first?.McmodId is { Length: > 0 } id)
            english = await FetchEnglishTitleAsync(id, cancellationToken).ConfigureAwait(false);

        english = english?.Trim();

        TranslateCache[key] = string.IsNullOrWhiteSpace(english) ? null : english;
        return TranslateCache[key];
    }

    /// <summary>已经翻译过的关键词（不发请求），给界面提示用。</summary>
    public static string? CachedTranslation(string query) =>
        TranslateCache.TryGetValue(query.Trim(), out var value) ? value : null;

    private static async Task<string?> FetchEnglishTitleAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var response = await Http.GetAsync($"{BaseUrl}/d/class/{id}", cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                return document.RootElement.TryGetProperty("subtitle", out var subtitle)
                    ? subtitle.GetString()
                    : null;
            }
            finally
            {
                Gate.Release();
            }
        }
        catch
        {
            return null;
        }
    }

    private static McmodEntry? Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var address = element.TryGetProperty("address", out var a) ? a.GetString() : null;
        if (string.IsNullOrWhiteSpace(address)) return null;

        var title = element.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;

        string? chinese = null, sub = null, abbr = null, id = null;

        if (element.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            chinese = Text(data, "chinese_name");
            sub = Text(data, "sub_name");
            abbr = Text(data, "abbr");
            id = Text(data, "mcmod_id");
        }

        return new McmodEntry
        {
            Address = address,
            Title = title,
            ChineseName = chinese,
            SubName = sub,
            Abbr = abbr,
            McmodId = id,
        };
    }

    private static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static McmodEntry? PickBest(IReadOnlyList<McmodEntry> entries, string name)
    {
        var target = Normalize(name);
        if (target.Length == 0) return null;

        McmodEntry? best = null;
        var bestScore = 0;

        foreach (var entry in entries)
        {
            var score = Math.Max(
                Math.Max(Score(Normalize(entry.SubName), target, 100, 70),
                    Score(Normalize(entry.ChineseName), target, 90, 60)),
                Score(Normalize(entry.Title), target, 80, 50));

            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        return bestScore >= 50 ? best : null;
    }

    private static int Score(string candidate, string target, int exact, int partial)
    {
        if (candidate.Length == 0) return 0;
        if (candidate == target) return exact;
        if (candidate.Contains(target, StringComparison.Ordinal)) return partial;

        return 0;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        return new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
