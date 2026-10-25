using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace MutsumiLauncher.Services;

/// <summary>本机没有合适 Java 时，自动下载一个 JRE 到游戏目录的 runtime 下。</summary>
public static class JavaRuntimeService
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromMinutes(20));

    public static string RuntimeRoot => Path.Combine(GamePaths.Root, "runtime");

    public static async Task<JavaRuntime?> EnsureAsync(int majorVersion,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var existing = FindLocal(majorVersion);
        if (existing is not null) return existing;

        var targetDir = Path.Combine(RuntimeRoot, $"jre-{majorVersion}");
        var zipPath = targetDir + ".zip";

        try
        {
            Directory.CreateDirectory(RuntimeRoot);
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);

            progress?.Report(new InstallProgress(0, $"下载 Java {majorVersion} 运行时…"));

            var url = $"https://api.adoptium.net/v3/binary/latest/{majorVersion}/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk";

            var downloadProgress = progress is null
                ? null
                : new Progress<DownloadProgress>(p =>
                    progress.Report(new InstallProgress(
                        Math.Min(68, p.Received / 1048576.0),
                        $"下载 Java {majorVersion} 运行时… {p.Received / 1048576.0:0.0} MB")));

            await DownloadEngine.DownloadAsync(new[] { url }, zipPath, null, null, downloadProgress,
                cancellationToken);

            progress?.Report(new InstallProgress(70, "解压 Java 运行时…"));

            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);

            // 压缩包通常带一层 jdk-xx-jre 目录，把内容提到 runtime/jre-xx
            var nested = Directory.GetDirectories(targetDir);
            if (nested.Length == 1 && Directory.GetFiles(targetDir).Length == 0)
            {
                var inner = nested[0];
                foreach (var entry in Directory.EnumerateFileSystemEntries(inner))
                {
                    var target = Path.Combine(targetDir, Path.GetFileName(entry));

                    if (Directory.Exists(entry))
                        Directory.Move(entry, target);
                    else
                        File.Move(entry, target);
                }

                try
                {
                    Directory.Delete(inner);
                }
                catch
                {
                    // 忽略
                }
            }

            progress?.Report(new InstallProgress(95, "校验 Java 运行时…"));

            var java = FindLocal(majorVersion);
            if (java is null)
                throw new InvalidOperationException($"Java {majorVersion} 解压后没有找到 java.exe");

            AppLog.Info($"java runtime downloaded: {java.ExecutablePath}");
            progress?.Report(new InstallProgress(100, $"Java {majorVersion} 已就绪"));
            return java;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "download java runtime");
            throw new InvalidOperationException($"下载 Java {majorVersion} 失败：{ex.Message}", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
            catch
            {
                // 忽略
            }
        }
    }

    public static JavaRuntime? FindLocal(int majorVersion)
    {
        try
        {
            if (!Directory.Exists(RuntimeRoot)) return null;

            foreach (var exe in Directory.EnumerateFiles(RuntimeRoot, "java.exe", SearchOption.AllDirectories))
            {
                // 只认 jre-<major> 目录或版本号匹配的
                var path = exe.Replace('\\', '/');
                if (!path.Contains($"jre-{majorVersion}/", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"/{majorVersion}.", StringComparison.OrdinalIgnoreCase))
                    continue;

                var home = Path.GetDirectoryName(Path.GetDirectoryName(exe)) ?? string.Empty;
                return new JavaRuntime
                {
                    ExecutablePath = exe,
                    Home = home,
                    Version = $"Java {majorVersion}（启动器下载）",
                    Major = majorVersion,
                    Source = "启动器运行时",
                };
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "find local java runtime");
        }

        return null;
    }
}
