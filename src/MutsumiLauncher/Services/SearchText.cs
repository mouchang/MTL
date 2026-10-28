using System.Text;

namespace MutsumiLauncher.Services;

/// <summary>
/// 搜索关键词处理：忽略大小写和标点，按「关键词」而不是整句去匹配。
/// 例：`Create: Steam 'n' Rails` → `create steam n rails`，
/// `sodium [1.21]` → `sodium 1.21`。
/// </summary>
public static class SearchText
{
    /// <summary>去掉标点、统一小写、合并空白；小数点、连字符这类夹在词里的符号保留。</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];

            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            // 数字中间的小数点（1.21）和词中间的分隔符（forge-1.20.1）保留
            if (ch is '.' or '-' or '_' && builder.Length > 0 && index + 1 < text.Length
                && char.IsLetterOrDigit(text[index + 1]))
            {
                pendingSpace = false;
                builder.Append(ch);
                continue;
            }

            pendingSpace = true;
        }

        return builder.ToString();
    }

    /// <summary>拆成关键词，去掉只有一个字符的噪音词（除非整句就一个词）。</summary>
    public static IReadOnlyList<string> Tokens(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return [];

        var parts = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parts.Count <= 1) return parts;

        var meaningful = parts.Where(part => part.Length > 1).ToList();
        return meaningful.Count > 0 ? meaningful : parts;
    }

    /// <summary>最长的那个关键词，通常是最有辨识度的词。</summary>
    public static string? LongestToken(string? text)
    {
        var tokens = Tokens(text);
        return tokens.Count == 0
            ? null
            : tokens.OrderByDescending(token => token.Length).First();
    }

    /// <summary>这些关键词在文本里命中了几个（用于模糊排序 / 过滤）。</summary>
    public static int Coverage(IReadOnlyList<string> tokens, string? text)
    {
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(text)) return 0;

        var normalized = Normalize(text);
        if (normalized.Length == 0) return 0;

        var hits = 0;

        foreach (var token in tokens)
        {
            if (normalized.Contains(token, StringComparison.Ordinal)) hits++;
        }

        return hits;
    }
}
