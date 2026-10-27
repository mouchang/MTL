using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>
/// 离线译名表（MC百科，约 1.4 万条）：中文名 ↔ 英文名。
/// 数据由 tools/mcmod-crawler 生成，打包进程序，不联网、不依赖第三方 API。
/// </summary>
public static class TranslationTable
{
    private static readonly Lazy<Data> Lazy = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static int Count => Lazy.Value.Count;

    private sealed class Data
    {
        public Dictionary<string, string> CnToEn { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> EnToCn { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> AbbrToCn { get; } = new(StringComparer.Ordinal);

        public List<(string Cn, string En, string CnKey, string EnKey)> Items { get; } = [];

        public int Count => Items.Count;
    }

    private static Data Load()
    {
        var data = new Data();

        try
        {
            using var stream = OpenTableStream();
            if (stream is null)
            {
                AppLog.Info("translation table: 资源不存在");
                return data;
            }

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
            using var document = JsonDocument.Parse(reader.ReadToEnd());

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var cn = item.TryGetProperty("cn", out var cnValue) ? cnValue.GetString() : null;
                var en = item.TryGetProperty("en", out var enValue) ? enValue.GetString() : null;
                var abbr = item.TryGetProperty("abbr", out var abbrValue) ? abbrValue.GetString() : null;

                if (string.IsNullOrWhiteSpace(cn)) continue;

                cn = cn.Trim();
                en = string.IsNullOrWhiteSpace(en) ? null : en.Trim();

                var cnKey = Normalize(cn);
                var enKey = en is null ? string.Empty : Normalize(en);

                data.CnToEn.TryAdd(cnKey, en ?? string.Empty);
                data.Items.Add((cn, en ?? string.Empty, cnKey, enKey));

                if (enKey.Length > 0) data.EnToCn.TryAdd(enKey, cn);

                if (!string.IsNullOrWhiteSpace(abbr))
                {
                    var abbrKey = Normalize(abbr);
                    if (abbrKey.Length >= 2) data.AbbrToCn.TryAdd(abbrKey, cn);
                }
            }

            AppLog.Info($"translation table: 已加载 {data.Count} 条");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "translation table load");
        }

        return data;
    }

    private static Stream? OpenTableStream()
    {
        var assembly = typeof(TranslationTable).Assembly;

        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(item => item.EndsWith("mcmod-zh.json.gz", StringComparison.OrdinalIgnoreCase));

        return name is null ? null : assembly.GetManifestResourceStream(name);
    }

    /// <summary>中文关键词 → 英文名（搜不到返回 null）。</summary>
    public static string? TranslateToEnglish(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var data = Lazy.Value;
        var key = Normalize(query);

        if (key.Length == 0) return null;

        if (data.CnToEn.TryGetValue(key, out var exact) && exact.Length > 0) return exact;

        // 包含匹配：取名字最短的那条（「工业」优先命中「工业」而不是「工业时代2附属」）
        (string Cn, string En)? best = null;
        var raw = query.Trim();

        foreach (var item in data.Items)
        {
            if (item.En.Length == 0) continue;

            if (!item.Cn.Contains(raw, StringComparison.Ordinal) && !raw.Contains(item.Cn, StringComparison.Ordinal))
                continue;

            if (item.Cn.Length < 2) continue;

            if (best is null || item.Cn.Length < best.Value.Cn.Length) best = (item.Cn, item.En);
        }

        return best?.En;
    }

    /// <summary>英文名 / 缩写 → 中文名（搜不到返回 null）。</summary>
    public static string? ChineseFor(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var data = Lazy.Value;
        var key = Normalize(title);

        if (key.Length == 0) return null;

        if (data.EnToCn.TryGetValue(key, out var exact)) return exact;
        if (data.AbbrToCn.TryGetValue(key, out var byAbbr)) return byAbbr;

        // 兜底：标题里包含某个条目名（取最长的那个，减少误配）
        string? bestCn = null;
        var bestLength = 0;

        foreach (var item in data.Items)
        {
            if (item.EnKey.Length < 5) continue;
            if (!key.Contains(item.EnKey, StringComparison.Ordinal)) continue;

            if (item.EnKey.Length > bestLength)
            {
                bestLength = item.EnKey.Length;
                bestCn = item.Cn;
            }
        }

        return bestCn;
    }

    private static string Normalize(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
