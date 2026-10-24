using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record UpdateInfo(bool Available, string LatestVersion, string? SetupUrl, string? Notes);

/// <summary>检查 GitHub Releases 上的新版本，并下载 NSIS 安装器。</summary>
public static class UpdateService
{
    /// <summary>更新源代理（例如 Cloudflare Worker 的 https://mtl.dpdns.org/gh）。</summary>
    private static string? ProxyBase
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("MTL_UPDATE_PROXY");
            var value = string.IsNullOrWhiteSpace(fromEnv) ? AppConfig.Current.UpdateProxy : fromEnv;
            return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
        }
    }

    private static string ReleaseApi => ProxyBase is { } proxy
        ? proxy + "/latest"
        : $"https://api.github.com/repos/{AppConfig.Current.UpdateRepo}/releases/latest";

    public static string CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = HttpUtil.Create(TimeSpan.FromSeconds(30));
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MutsumiLauncher/{CurrentVersion}");

            using var response = await http.GetAsync(ReleaseApi, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateInfo(false, CurrentVersion, null, null);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("更新源鉴权失败（Worker 的 GITHUB_TOKEN 无效或已撤销）。");

            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("GitHub 请求受限（403/429），请稍后再试。");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()?.TrimStart('v', 'V') ?? string.Empty
                : string.Empty;

            var notes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString()
                : null;

            string? setupUrl = null;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.Contains("setup", StringComparison.OrdinalIgnoreCase)) continue;

                    setupUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                        ? urlElement.GetString()
                        : null;

                    if (setupUrl is not null && ProxyBase is { } proxyBase)
                        setupUrl = $"{proxyBase}/download?url={Uri.EscapeDataString(setupUrl)}";

                    break;
                }
            }

            var available = CompareVersion(tag, CurrentVersion) > 0 && !string.IsNullOrWhiteSpace(setupUrl);

            return new UpdateInfo(available, string.IsNullOrWhiteSpace(tag) ? CurrentVersion : tag,
                setupUrl, notes);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "check update");
            throw new InvalidOperationException("检查更新失败：" + ex.Message, ex);
        }
    }

    public static async Task<string> DownloadAsync(UpdateInfo info,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(info.SetupUrl))
            throw new InvalidOperationException("这个版本没有提供安装包。");

        var directory = Path.Combine(AppPaths.CacheDir, "update");
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, $"MutsumiLauncher-{info.LatestVersion}-Setup.exe");

        using var http = HttpUtil.Create(TimeSpan.FromMinutes(20));
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MutsumiLauncher/{CurrentVersion}");

        using var response = await http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(target);

        var buffer = new byte[81920];
        long read = 0;

        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count <= 0) break;

            await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            read += count;

            if (total > 0)
                progress?.Report(new InstallProgress(read * 100.0 / total, "下载更新…"));
        }

        progress?.Report(new InstallProgress(100, "下载完成"));

        if (read == 0) throw new InvalidDataException("下载到的安装包是空的。");

        return target;
    }

    public static void InstallAndExit(string setupPath)
    {
        Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    private static int CompareVersion(string left, string right)
    {
        static Version Parse(string text)
        {
            var cleaned = new string(text.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0);
        }

        return Parse(left).CompareTo(Parse(right));
    }
}
