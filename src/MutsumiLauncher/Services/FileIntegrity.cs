using System.IO;
using System.Security.Cryptography;

namespace MutsumiLauncher.Services;

/// <summary>
/// 本地文件校验：先比大小，大小对上再算 SHA-1。
/// 参考 PCL2 的 FileChecker（先 ActualSize 后 Hash），避免每次启动都去哈希几百 MB 的资源文件。
/// </summary>
public static class FileIntegrity
{
    /// <summary>文件是否存在且与给定的 SHA-1 / 大小一致。未提供哈希与大小时只判断存在且非空。</summary>
    public static bool Matches(string path, string? sha1, long? size = null)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) return false;

            if (size is > 0 && info.Length != size.Value) return false;
            if (string.IsNullOrWhiteSpace(sha1)) return true;

            return Sha1Of(path).Equals(sha1.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"verify {path}");
            return false;
        }
    }

    public static string Sha1Of(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA1.HashData(stream));
    }
}
