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

    /// <summary>输入里有没有汉字（没有就当英文查询处理，别拿中文条目去套）。</summary>
    public static bool HasChinese(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF) return true;   // 基本汉字
            if (ch >= 0x3400 && ch <= 0x4DBF) return true;   // 扩展 A
            if (ch >= 0xF900 && ch <= 0xFAFF) return true;   // 兼容汉字
        }

        return false;
    }

    /// <summary>中文关键词 → 英文名（搜不到返回 null）。英文查询一律返回 null。</summary>
    public static string? TranslateToEnglish(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        // 译名表是「中文 → 英文」的，英文输入进来只会被乱套
        // （MC百科里有大量条目名字就是英文缩写，比如 cn="JEI" → en="JEI Bees"）
        if (!HasChinese(query)) return null;

        var data = Lazy.Value;
        var key = Normalize(query);

        if (key.Length == 0) return null;

        if (data.CnToEn.TryGetValue(key, out var exact) && exact.Length > 0) return exact;

        // 多关键词：逐个翻，取最长的那个英文名（一般也是更具体的那个）
        // 「机械动力 汽鸣铁道」→ Create: Steam 'n' Rails，而不是笼统的 Create
        string? bestSegment = null;

        foreach (var segment in SearchText.Tokens(query))
        {
            var segmentKey = Normalize(segment);
            if (segmentKey.Length < 2) continue;
            if (!data.CnToEn.TryGetValue(segmentKey, out var name) || name.Length == 0) continue;

            if (bestSegment is null || name.Length > bestSegment.Length) bestSegment = name;
        }

        if (bestSegment is not null) return bestSegment;

        // 包含匹配：取名字最短的那条（「工业」优先命中「工业」而不是「工业时代2附属」）。
        // 用规范化后的键比较，多了空格 / 标点也能命中
        (string Cn, string En)? best = null;

        foreach (var item in data.Items)
        {
            if (item.En.Length == 0) continue;
            if (item.CnKey.Length < 2) continue;

            if (!item.CnKey.Contains(key, StringComparison.Ordinal)
                && !key.Contains(item.CnKey, StringComparison.Ordinal))
                continue;

            if (best is null || item.CnKey.Length < best.Value.Cn.Length) best = (item.CnKey, item.En);
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
