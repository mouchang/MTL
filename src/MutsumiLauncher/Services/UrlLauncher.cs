using System.Diagnostics;

namespace MutsumiLauncher.Services;

/// <summary>
/// 打开网页。对齐 PCL2 的做法：只用 ShellExecute（Process.Start + url），
/// 失败再试一次 cmd start。绝不启动 explorer.exe——反复拉起/杀掉 shell 会让桌面崩溃。
/// </summary>
public static class UrlLauncher
{
    public static string? Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "链接为空";

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("minecraft://", StringComparison.OrdinalIgnoreCase))
        {
            return "不是有效的网址（必须以 http 开头）";
        }

        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{url}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.SystemDirectory,
            };

            Process.Start(startInfo)?.Dispose();
            AppLog.Info($"open url via cmd: {url}");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open url via cmd");
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })?.Dispose();
            AppLog.Info($"open url via ShellExecute: {url}");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open url via ShellExecute");
            return ex.Message;
        }
    }
}
