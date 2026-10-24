using System.IO;
using System.Text.RegularExpressions;

namespace MutsumiLauncher.Services;

/// <summary>从游戏日志末尾提取常见崩溃原因，给用户可读的提示。</summary>
public static class CrashAnalyzer
{
    private static readonly (string Pattern, string Advice)[] Rules =
    [
        ("UnsupportedClassVersionError", "Java 版本太低，需要更高版本的 Java。"),
        ("java.lang.OutOfMemoryError", "内存不足，请在实例设置里调大内存分配。"),
        ("Incompatible mod set", "模组不兼容：请检查加载器版本与模组是否匹配。"),
        ("Missing or unsupported mandatory dependencies", "缺少模组前置，请先安装它要求的前置模组。"),
        ("Duplicate mods", "存在重复模组，请删除重复的 jar。"),
        ("Could not find or load main class", "启动参数或版本文件不完整，请重新安装该实例。"),
        ("NoClassDefFoundError", "缺少依赖库，请重新安装该实例补全文件。"),
        ("ClassNotFoundException", "缺少依赖库或加载器不完整，请重新安装该实例。"),
        ("remapping", "Fabric 重映射失败，可以删除实例下的 .fabric 目录后重试。"),
        ("GLFW error", "显卡驱动或 OpenGL 初始化失败，请更新显卡驱动。"),
        ("Failed to create window", "无法创建游戏窗口，请检查显卡驱动。"),
        ("Pixel format not accelerated", "OpenGL 不可用，请更新显卡驱动或改用独显运行。"),
        ("AccessDeniedException", "文件权限不足，请把游戏装在可写目录，或以管理员身份运行启动器。"),
        ("FileNotFoundException", "缺少游戏文件（可能是资源或版本文件），请重新安装该实例。"),
        ("UnsupportedOperationException", "Java 版本与游戏不匹配，请换用对应版本的 Java。"),
    ];

    public static string Analyze(string? logPath, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            return $"游戏异常退出（代码 {exitCode}），没有找到日志。";

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var lines = new LinkedList<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.AddLast(line);
                if (lines.Count > 400) lines.RemoveFirst();
            }

            var text = string.Join('\n', lines);

            foreach (var (pattern, advice) in Rules)
            {
                if (!text.Contains(pattern, StringComparison.OrdinalIgnoreCase)) continue;

                var detail = lines.LastOrDefault(l =>
                    l.Contains(pattern, StringComparison.OrdinalIgnoreCase))?.Trim();

                return string.IsNullOrWhiteSpace(detail) ? advice : $"{advice}\n{Trim(detail)}";
            }

            var lastError = lines.LastOrDefault(l =>
                Regex.IsMatch(l, "ERROR|Exception|Error:", RegexOptions.IgnoreCase))?.Trim();

            return string.IsNullOrWhiteSpace(lastError)
                ? $"游戏异常退出（代码 {exitCode}），请查看日志。"
                : $"游戏异常退出（代码 {exitCode}）：{Trim(lastError)}";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "crash analyze");
            return $"游戏异常退出（代码 {exitCode}），日志读取失败。";
        }
    }

    private static string Trim(string line) => line.Length > 300 ? line[..300] + "…" : line;
}
