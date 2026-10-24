using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>游戏目录里的 options.txt 处理。</summary>
public static class GameOptions
{
    public const string DefaultLanguage = "zh_cn";

    /// <summary>确保 options.txt 里 lang 为指定语言；缺文件就创建，缺行就追加。</summary>
    public static void EnsureLanguage(string gameDir, string language = DefaultLanguage)
    {
        try
        {
            Directory.CreateDirectory(gameDir);
            var path = Path.Combine(gameDir, "options.txt");

            var lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : [];

            var index = lines.FindIndex(line =>
                line.StartsWith("lang:", StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                if (lines[index] == $"lang:{language}") return;
                lines[index] = $"lang:{language}";
            }
            else
            {
                lines.Add($"lang:{language}");
            }

            File.WriteAllLines(path, lines);
            AppLog.Info($"options.txt language set: {language} ({gameDir})");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "ensure language");
        }
    }
}
