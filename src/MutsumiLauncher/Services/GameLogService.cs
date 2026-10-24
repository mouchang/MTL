using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>读取实例的启动日志（game/logs 下最新的 .log）。</summary>
public static class GameLogService
{
    /// <summary>启动日志固定写在游戏根目录的 logs/（GameLauncher 的约定）。</summary>
    public static string LogDirFor(string instance) => Path.Combine(GamePaths.Root, "logs");

    public static string? LatestLogPath(string instance)
    {
        try
        {
            var dir = LogDirFor(instance);
            if (!Directory.Exists(dir)) return null;

            var logs = Directory.EnumerateFiles(dir, "*.log")
                .Select(file => new FileInfo(file))
                .ToList();

            if (!string.IsNullOrWhiteSpace(instance))
            {
                var match = logs
                    .Where(info => info.Name.StartsWith(instance + "-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (match is not null) return match.FullName;
            }

            return logs
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => info.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "find latest log");
            return null;
        }
    }

    /// <summary>读取日志末尾若干行。</summary>
    public static string ReadTail(string path, int maxLines = 400)
    {
        try
        {
            if (!File.Exists(path)) return "（日志文件不存在）";

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var lines = new LinkedList<string>();

            while (reader.ReadLine() is { } line)
            {
                lines.AddLast(line);
                if (lines.Count > maxLines) lines.RemoveFirst();
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "read log tail");
            return "读取日志失败：" + ex.Message;
        }
    }
}
