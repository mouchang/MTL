using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>
/// MC百科索引：把「Modrinth / CurseForge 的项目」对上 MC百科的条目，拿到
/// 中文名和 <c>https://www.mcmod.cn/class/{id}.html</c> 链接。
///
/// 数据来自 tools/mcmod-crawler 生成的 mcmod-zh.json.gz（约 1.4 万条，
/// 每条含 id / cn / en / abbr），打包进程序，全程离线、不调第三方接口。
///
/// 匹配思路参考 PCL2：优先用 slug 精确匹配，其次用标题，最后才做包含匹配；
/// 命中越"精确"分越高，低于阈值就当作没有，宁可不出中文名也不硬套错条目。
/// </summary>
public static class McmodIndex
{
    /// <summary>一条 MC百科条目。</summary>
    public sealed record Match(int Id, string English, string? Chinese, string? Abbr, bool IsModpack)
    {
        public string Url => $"https://www.mcmod.cn/class/{Id}.html";
    }

    private sealed class Entry
    {
        public required int Id { get; init; }
        public required string English { get; init; }
        public required string EnKey { get; init; }
        public string? Chinese { get; init; }
        public string? CnKey { get; init; }
        public string? Abbr { get; init; }
        public string? AbbrKey { get; init; }
        public required bool IsModpack { get; init; }
    }

    private sealed class Data
    {
        public Dictionary<string, Entry> ByName { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Entry> ByAbbr { get; } = new(StringComparer.Ordinal);
        public List<Entry> All { get; } = [];
    }

    private static readonly Lazy<Data> Lazy = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static int Count => Lazy.Value.All.Count;

    /// <summary>
    /// 按 slug / 标题匹配 MC百科条目；匹配不上返回 null。
    /// <paramref name="slug"/> 比标题可信得多（Modrinth 的 slug 就是项目标识），先试它。
    /// </summary>
    public static Match? Find(string? slug, string? title)
    {
        var data = Lazy.Value;
        if (data.All.Count == 0) return null;

        var slugKey = Normalize(slug);
        var titleKey = Normalize(title);
        var bareKey = Normalize(StripDecorations(title));

        if (slugKey.Length == 0 && titleKey.Length == 0) return null;

        // 1. slug / 标题的精确匹配，O(1)
        foreach (var key in new[] { slugKey, titleKey, bareKey })
        {
            if (key.Length < 3) continue;

            if (data.ByName.TryGetValue(key, out var entry))
                return ToMatch(entry);

            if (data.ByAbbr.TryGetValue(key, out var byAbbr))
                return ToMatch(byAbbr);
        }

        // 2. 扫一遍做包含 / 分词匹配
        Entry? best = null;
        var bestScore = 0;

        foreach (var entry in data.All)
        {
            var score = Score(entry, slugKey, slug, titleKey, bareKey, title);

            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        return bestScore >= 60 ? ToMatch(best!) : null;
    }

    /// <summary>只要中文名（没有、或者条目本身没翻译成中文时返回 null）。</summary>
    public static string? ChineseFor(string? slug, string? title)
    {
        var match = Find(slug, title);
        if (match?.Chinese is not { Length: > 0 } chinese) return null;

        // 少数条目在百科里本来就只有英文名（比如 Curios 的 cn 是 "Adorned"），别当中文名用
        return HasChinese(chinese) ? chinese : null;
    }

    private static bool HasChinese(string text)
    {
        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF) return true;
            if (ch >= 0x3400 && ch <= 0x4DBF) return true;
            if (ch >= 0xF900 && ch <= 0xFAFF) return true;
        }

        return false;
    }

    private static Match ToMatch(Entry entry) =>
        new(entry.Id, entry.English, CleanChinese(entry.Chinese), entry.Abbr, entry.IsModpack);

    /// <summary>百科里有少数条目根本没翻成中文（cn 字段还是英文），这种就当没有中文名。</summary>
    private static string? CleanChinese(string? text) =>
        text is { Length: > 0 } && HasChinese(text) ? text : null;

    private static int Score(Entry entry, string slugKey, string? slug, string titleKey, string bareKey,
        string? title)
    {
        // 名字太短（api / lib / core）一律只认精确匹配，免得把几百个 Mod 都配上同一个条目
        var specific = entry.EnKey.Length >= 4;

        if (specific)
        {
            if (entry.EnKey == slugKey) return 100;
            if (entry.EnKey == titleKey) return 96;
            if (entry.EnKey == bareKey) return 92;
        }

        // 缩写（JEI / IC2 / AE2）：至少 3 个字符，且和 slug / 标题完全一致
        if (entry.AbbrKey is { Length: >= 3 })
        {
            if (entry.AbbrKey == slugKey || entry.AbbrKey == titleKey) return 88;
        }

        if (!specific) return 0;

        // 按「词」比较，避免 "Fabric" 把 "Fabric API" 吃掉这类误配。
        // 注意必须用原始标题切词：规范化后的 "3dskinlayers" 已经没分隔符了。
        var titleWords = Words(StripDecorations(title) ?? titleKey);

        if (titleWords.Count < 2) titleWords = Words(slug);

        var entryWords = Words(entry.English);

        if (entryWords.Count == 0 || titleWords.Count == 0) return 0;

        // 标题以条目名开头，剩下的是 api / lib / extra 这类修饰词
        if (entryWords.Count < titleWords.Count && StartsWith(titleWords, entryWords))
        {
            var rest = string.Join("", titleWords.Skip(entryWords.Count));

            if (rest is "api" or "lib" or "library" or "core" or "mod" or "utility" or "utilities"
                or "fabric" or "forge" or "neoforge" or "quilt")
                return 84;
        }

        // 词一样但顺序不同：「3D Skin Layers」vs「Skin Layers 3D」
        if (entryWords.Count >= 2 && entryWords.Count == titleWords.Count)
        {
            var left = entryWords.OrderBy(word => word, StringComparer.Ordinal);
            var right = titleWords.OrderBy(word => word, StringComparer.Ordinal);

            if (left.SequenceEqual(right)) return 82;
        }

        // 条目名的每个词都在标题里（顺序一致但不连续，比如带副标题的情况）
        if (entryWords.Count >= 2 && Contains(titleWords, entryWords)) return 66;

        return 0;
    }

    private static bool StartsWith(List<string> words, List<string> prefix)
    {
        if (prefix.Count > words.Count) return false;

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(words[i], prefix[i], StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private static bool Contains(List<string> words, List<string> needle)
    {
        for (var i = 0; i + needle.Count <= words.Count; i++)
        {
            var hit = true;

            for (var j = 0; j < needle.Count; j++)
            {
                if (string.Equals(words[i + j], needle[j], StringComparison.Ordinal)) continue;
                hit = false;
                break;
            }

            if (hit) return true;
        }

        return false;
    }

    /// <summary>把名字切成词：先按分隔符切，再把 camelCase 拆开。</summary>
    private static List<string> Words(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var words = new List<string>();
        var builder = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (!char.IsLetterOrDigit(ch))
            {
                Flush(words, builder);
                continue;
            }

            // 小写转大写处断开：JustEnoughItems -> Just Enough Items
            if (builder.Length > 0 && char.IsUpper(ch) && char.IsLower(text[i - 1]))
                Flush(words, builder);

            builder.Append(ch);
        }

        Flush(words, builder);

        return words.Where(word => word.Length > 0).ToList();
    }

    private static void Flush(List<string> words, StringBuilder builder)
    {
        if (builder.Length == 0) return;

        words.Add(builder.ToString().ToLowerInvariant());
        builder.Clear();
    }

    private static Data Load()
    {
        var data = new Data();

        try
        {
            using var stream = OpenStream();
            if (stream is null)
            {
                AppLog.Info("mcmod index: 资源不存在");
                return data;
            }

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            using var document = JsonDocument.Parse(reader.ReadToEnd());

            if (document.RootElement.ValueKind != JsonValueKind.Array) return data;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var id = Int(element, "id");
                var english = Text(element, "en");
                if (id <= 0 || string.IsNullOrWhiteSpace(english)) continue;

                var type = Text(element, "type");
                var chinese = Text(element, "cn");
                var abbr = Text(element, "abbr");

                var entry = new Entry
                {
                    Id = id,
                    English = english.Trim(),
                    EnKey = Normalize(english),
                    Chinese = string.IsNullOrWhiteSpace(chinese) ? null : chinese.Trim(),
                    CnKey = string.IsNullOrWhiteSpace(chinese) ? null : Normalize(chinese),
                    Abbr = string.IsNullOrWhiteSpace(abbr) ? null : abbr.Trim(),
                    AbbrKey = string.IsNullOrWhiteSpace(abbr) ? null : Normalize(abbr),
                    IsModpack = string.Equals(type, "modpack", StringComparison.OrdinalIgnoreCase),
                };

                if (entry.EnKey.Length == 0) continue;

                data.All.Add(entry);
                data.ByName.TryAdd(entry.EnKey, entry);

                if (entry.AbbrKey is { Length: >= 3 }) data.ByAbbr.TryAdd(entry.AbbrKey, entry);
            }

            AppLog.Info($"mcmod index: {data.All.Count} 条");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "mcmod index load");
        }

        return data;
    }

    private static Stream? OpenStream()
    {
        var assembly = typeof(McmodIndex).Assembly;

        // 优先全量表（3.1 万条，含没有中文名的条目），没有就退回只有中文名的旧表
        var names = assembly.GetManifestResourceNames();

        foreach (var suffix in new[] { "mcmod-all.json.gz", "mcmod-zh.json.gz" })
        {
            var name = names.FirstOrDefault(item => item.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            if (name is not null) return assembly.GetManifestResourceStream(name);
        }

        return null;
    }

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>去掉括号里的补充说明和版本号：「Sodium (Fabric) 1.21」→「Sodium」。</summary>
    private static string? StripDecorations(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (ch is '(' or '（') break;
            builder.Append(ch);
        }

        var result = builder.ToString();

        // 去掉结尾的「 - 1.20.1」「 [Fabric]」这类尾巴
        var cut = result.IndexOfAny(['[', '【']);
        if (cut > 0) result = result[..cut];

        return result.Trim();
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
}