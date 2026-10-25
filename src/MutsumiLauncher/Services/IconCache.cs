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

    private static bool IsValidImage(string path)
    {
        try
        {
            if (new FileInfo(path).Length < 16) return false;
            return LooksLikeImage(File.ReadAllBytes(path));
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 8) return false;

        // PNG / JPEG / GIF / WebP
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return true;

        return false;
    }

    public static async Task<string?> EnsureAsync(string url, string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            var safe = string.Concat(key.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            if (string.IsNullOrEmpty(safe)) safe = "icon";

            var path = Path.Combine(CacheDir, safe + ".png");
            if (File.Exists(path) && IsValidImage(path)) return path;

            // 图标永远直连，不走下载镜像
            foreach (var candidate in new[] { url })
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(candidate, cancellationToken);
                    if (!LooksLikeImage(bytes)) continue;

                    await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                    return path;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"icon {candidate}");
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "icon cache");
            return null;
        }
    }
}
