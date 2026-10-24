using System.IO;
using System.Net.Http;

namespace MutsumiLauncher.Services;

/// <summary>远程图标下载到本地缓存，避免界面依赖实时网络。</summary>
public static class IconCache
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromSeconds(30));

    public static string CacheDir => Path.Combine(AppPaths.DataRoot, "icons");

    public static async Task<string?> EnsureAsync(string url, string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            var safe = string.Concat(key.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            if (string.IsNullOrEmpty(safe)) safe = "icon";

            var path = Path.Combine(CacheDir, safe + ".png");
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

            var bytes = await Http.GetByteArrayAsync(DownloadMirror.ModFileUrl(url), cancellationToken);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "icon cache");
            return null;
        }
    }
}
