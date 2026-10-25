using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>按游戏版本挑一个合适的本机 Java（加载器安装器、原版启动共用同一套规则）。</summary>
public static class JavaSelector
{
    /// <summary>挑一个能跑该游戏版本的 Java；本机没有时返回 null。</summary>
    public static async Task<JavaRuntime?> PickAsync(string gameVersion,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<JavaRuntime> runtimes;
        try
        {
            runtimes = await JavaDetector.DetectAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "detect java");
            return null;
        }

        if (runtimes.Count == 0) return null;

        var required = RequiredMajor(gameVersion);
        return runtimes.FirstOrDefault(runtime => runtime.Major == required)
               ?? runtimes.Where(runtime => runtime.Major >= required)
                   .OrderBy(runtime => runtime.Major)
                   .FirstOrDefault()
               ?? runtimes.OrderByDescending(runtime => runtime.Major).First();
    }

    /// <summary>游戏版本需要的 Java 主版本（1.17 → 16，1.18-1.20.4 → 17，1.20.5+ → 21）。</summary>
    public static int RequiredMajor(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion)) return 21;

        var text = gameVersion.Trim();

        // 26.3 这类新版本号：统一按 21 处理
        if (!text.StartsWith("1.", StringComparison.Ordinal))
        {
            return int.TryParse(text.Split('.')[0], out var modern) && modern >= 26 ? 21 : 21;
        }

        var parts = text.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
        {
            if (minor >= 21) return 21;
            if (minor >= 18) return 17;
            if (minor == 17) return 16;
        }

        return 8;
    }
}
