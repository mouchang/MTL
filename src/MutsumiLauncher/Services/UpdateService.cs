using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record UpdateInfo(
    bool Available,
    string LatestVersion,
    string? SetupUrl,
    string? Notes,
    string? DirectSetupUrl = null);

/// <summary>检查 GitHub Releases 上的新版本，并下载 NSIS 安装器。</summary>
public static class UpdateService
{
    private const string GitHubApi =
        "https://api.github.com/repos/{0}/releases/latest";

    /// <summary>更新源代理（例如 Cloudflare Worker 的 https://releases.mtl.dpdns.org/gh）。</summary>
    private static string? ProxyBase
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("MTL_UPDATE_PROXY");
            var value = string.IsNullOrWhiteSpace(fromEnv) ? AppConfig.Current.UpdateProxy : fromEnv;
            return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
        }
    }

    private static string Repo => string.IsNullOrWhiteSpace(AppConfig.Current.UpdateRepo)
        ? "mouchang/MTL"
        : AppConfig.Current.UpdateRepo.Trim();

    private static string DirectApi => string.Format(GitHubApi, Repo);

    private static string? _currentVersion;

    /// <summary>完整版本号，带预发布后缀（1.0.0-beta）。比较更新时不能用截断过的数字版本。</summary>
    public static string CurrentVersion
    {
        get
        {
            if (_currentVersion is not null) return _currentVersion;

            // csproj 里通过 AssemblyMetadata 写进来的是 $(Version)，含 -beta 这类后缀
            var metadata = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "Version")
                ?.Value;

            if (!string.IsNullOrWhiteSpace(metadata))
            {
                _currentVersion = metadata.Trim();
                return _currentVersion;
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _currentVersion = version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            return _currentVersion;
        }
    }

    public static async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        Exception? proxyError = null;

        if (ProxyBase is { } proxy)
        {
            try
            {
                return await CheckFromAsync(proxy + "/latest", proxy, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                proxyError = ex;
                AppLog.Error(ex, "check update via proxy, falling back to github");
            }
        }

        try
        {
            return await CheckFromAsync(DirectApi, null, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "check update direct");
            throw proxyError is null
                ? new InvalidOperationException("检查更新失败：" + ex.Message, ex)
                : new InvalidOperationException(
                    "检查更新失败：" + ex.Message + "（代理：" + proxyError.Message + "）", ex);
        }
    }

    private static async Task<UpdateInfo> CheckFromAsync(string apiUrl, string? proxyBase,
        CancellationToken cancellationToken)
    {
        using var http = HttpUtil.Create(TimeSpan.FromSeconds(30));
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MutsumiLauncher/{CurrentVersion}");

        using var response = await http.GetAsync(apiUrl, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new UpdateInfo(false, CurrentVersion, null, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("更新源鉴权失败（token 无效）。");

        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("更新源请求受限（403/429）。");

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

        string? directUrl = null;

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

                directUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;

                break;
            }
        }

        var setupUrl = directUrl;

        if (setupUrl is not null && proxyBase is not null)
            setupUrl = $"{proxyBase}/download?url={Uri.EscapeDataString(setupUrl)}";

        var available = CompareVersion(tag, CurrentVersion) > 0 && !string.IsNullOrWhiteSpace(setupUrl);

        return new UpdateInfo(available, string.IsNullOrWhiteSpace(tag) ? CurrentVersion : tag,
            setupUrl, notes, directUrl);
    }

    public static async Task<string> DownloadAsync(UpdateInfo info,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var candidates = new[]
        {
            info.SetupUrl,
            info.DirectSetupUrl,
        }.Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException("这个版本没有提供安装包。");

        var directory = Path.Combine(AppPaths.CacheDir, "update");
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, $"MutsumiLauncher-{info.LatestVersion}-Setup.exe");

        Exception? lastError = null;

        foreach (var url in candidates)
        {
            try
            {
                await DownloadOneAsync(url, target, progress, cancellationToken);
                return target;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                AppLog.Error(ex, $"download update from {url}");
            }
        }

        throw new InvalidOperationException("更新下载失败：" + lastError?.Message, lastError);
    }

    private static async Task DownloadOneAsync(string url, string target,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var adapter = progress is null
            ? null
            : new Progress<DownloadProgress>(p =>
                progress.Report(new InstallProgress(
                    p.Total > 0 ? p.Received * 100.0 / p.Total : 0,
                    $"下载更新… {p.Received / 1048576.0:0.0} MB")));

        await DownloadEngine.DownloadAsync(new[] { url }, target, null, null, adapter,
            cancellationToken);

        progress?.Report(new InstallProgress(100, "下载完成"));
    }

    public static void InstallAndExit(string setupPath)
    {
        Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// 完整版本比较：`1.0.0-beta` 与 `1.0.0` 不再算同一个版本，
    /// `1.0.0-beta` &lt; `1.0.0-beta.2` &lt; `1.0.0` &lt; `1.0.1`。
    /// </summary>
    private static int CompareVersion(string left, string right)
        => Semver.Parse(left).CompareTo(Semver.Parse(right));
}
