namespace MutsumiLauncher.Services;

/// <summary>
/// 精简版语义化版本：`1.0.0` / `1.0.0-beta` / `1.0.0-beta.2` / `v1.2.3` 都能解析。
/// 用来比较启动器版本，预发布后缀要参与比较（否则 beta 之间永远收不到更新）。
/// </summary>
public readonly record struct Semver(int Major, int Minor, int Patch, string Prerelease)
    : IComparable<Semver>
{
    public static readonly Semver Zero = new(0, 0, 0, string.Empty);

    public bool IsPrerelease => Prerelease.Length > 0;

    public static Semver Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Zero;

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];

        var dash = value.IndexOf('-');
        var core = dash >= 0 ? value[..dash] : value;
        var prerelease = dash >= 0 ? value[(dash + 1)..].Trim() : string.Empty;

        // build 元数据（+xxx）不参与比较
        var plus = prerelease.IndexOf('+');
        if (plus >= 0) prerelease = prerelease[..plus];

        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);

        return new Semver(
            parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 0,
            parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : 0,
            parts.Length > 2 && int.TryParse(parts[2], out var patch) ? patch : 0,
            prerelease);
    }

    public int CompareTo(Semver other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // 语义化版本：带预发布后缀的比正式版小
        if (Prerelease.Length == 0 && other.Prerelease.Length == 0) return 0;
        if (Prerelease.Length == 0) return 1;
        if (other.Prerelease.Length == 0) return -1;

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var b = right.Split('.', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var result = CompareIdentifier(a[i], b[i]);
            if (result != 0) return result;
        }

        // 前缀相同的，段数多的更大（beta.1 < beta.1.1）
        return a.Length.CompareTo(b.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = long.TryParse(left, out var leftValue);
        var rightNumeric = long.TryParse(right, out var rightValue);

        if (leftNumeric && rightNumeric) return leftValue.CompareTo(rightValue);

        // 数字段比字母段小
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString()
        => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
}
