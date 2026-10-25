using System.Text.RegularExpressions;

namespace MutsumiLauncher.Services;

/// <summary>
/// 游戏版本标签的统一口径（参考 PCL2 的版本号处理）：
/// 排序不再按字符串、也不再把快照当成 0 分；显示时把快照 / 预览版标出来。
/// 这里只影响「展示顺序与文案」，筛选仍然是原始字符串精确匹配。
/// </summary>
public static class GameVersionTags
{
    private static readonly Regex WeeklySnapshot =
        new(@"^(\d{2})w(\d{2})([a-z])$", RegexOptions.Compiled);

    private static readonly Regex LegacyPrefix =
        new(@"^(a|b|rd-|pre-classic|classic|infdev)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>周快照（23w13a）或带 -pre / -rc / -snapshot 的预览版。</summary>
    public static bool IsSnapshot(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;

        var value = version.Trim();
        if (WeeklySnapshot.IsMatch(value.ToLowerInvariant())) return true;

        var lower = value.ToLowerInvariant();
        return lower.Contains("-pre") || lower.Contains("-rc") || lower.Contains("-snapshot");
    }

    /// <summary>列表里显示的名字：快照 / 预览版加后缀，正式版原样。</summary>
    public static string Display(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return version ?? string.Empty;

        var value = version.Trim();
        var lower = value.ToLowerInvariant();

        if (WeeklySnapshot.IsMatch(lower)) return value + " 快照";
        if (lower.Contains("-pre") || lower.Contains("-rc") || lower.Contains("-snapshot"))
            return value + " 预览版";

        return value;
    }

    /// <summary>
    /// 排序权重，越大越新。
    /// 正式版 1.21.1 = 10210019；它的预览版略低（1.21.1-pre1 = 10210013）；
    /// 周快照排在最前面（当前快照通常比最新正式版还新）；远古版本垫底。
    /// </summary>
    public static long SortKey(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 0;

        var value = version.Trim().ToLowerInvariant();

        var snapshot = WeeklySnapshot.Match(value);
        if (snapshot.Success)
        {
            var year = int.Parse(snapshot.Groups[1].Value);
            var week = int.Parse(snapshot.Groups[2].Value);
            var revision = snapshot.Groups[3].Value[0] - 'a';

            return 9_000_000_000L + year * 1_000_000L + week * 1_000L + revision;
        }

        if (LegacyPrefix.IsMatch(value)) return 100_000 + ParseNumberTail(value);

        var stage = 9; // 正式版
        var main = value;
        var dash = value.IndexOf('-');

        if (dash > 0)
        {
            var suffix = value[(dash + 1)..];
            main = value[..dash];

            stage = suffix switch
            {
                _ when suffix.StartsWith("rc", StringComparison.Ordinal) => 6,
                _ when suffix.StartsWith("pre", StringComparison.Ordinal) => 3,
                _ when suffix.StartsWith("snapshot", StringComparison.Ordinal) => 2,
                _ => 1,
            };
        }

        var numbers = new List<int>();

        foreach (var part in main.Split('.'))
        {
            numbers.Add(int.TryParse(part, out var number) ? number : 0);
            if (numbers.Count == 3) break;
        }

        var major = numbers.Count > 0 ? numbers[0] : 0;
        var minor = numbers.Count > 1 ? numbers[1] : 0;
        var patch = numbers.Count > 2 ? numbers[2] : 0;

        if (major <= 0 && minor <= 0) return 0;

        var basis = major * 1_000_000L + minor * 1_000L + patch;
        return basis * 10 + stage;
    }

    /// <summary>按新到旧排序并去重；整合包只保留最新的一个游戏版本（PCL2 同样处理）。</summary>
    public static IReadOnlyList<string> Arrange(IEnumerable<string> versions, string? projectType)
    {
        var sorted = versions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(SortKey)
            .ThenBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.Equals(projectType, "modpack", StringComparison.OrdinalIgnoreCase) && sorted.Count > 1)
        {
            // 整合包通常只针对一个游戏版本；优先取最新的正式版，没有正式版才退回快照
            var release = sorted.FirstOrDefault(version => !IsSnapshot(version));
            return [release ?? sorted[0]];
        }

        return sorted;
    }

    private static int ParseNumberTail(string value)
    {
        var digits = new string(value.SkipWhile(character => !char.IsAsciiDigit(character)).ToArray());
        var parts = digits.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var result = 0;
        for (var i = 0; i < Math.Min(parts.Length, 3); i++)
            result = result * 100 + (int.TryParse(parts[i], out var number) ? Math.Min(number, 99) : 0);

        return result;
    }
}
