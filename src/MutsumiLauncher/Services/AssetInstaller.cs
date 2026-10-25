using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

public sealed class AssetIndexFile
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject> Objects { get; set; } = [];
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>下载资源索引与 objects（按哈希寻址，可断点续传）。</summary>
public static class AssetInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromMinutes(5));

    public static async Task EnsureAsync(AssetIndex index, IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var indexDir = Path.Combine(GamePaths.AssetsDir, "indexes");
        Directory.CreateDirectory(indexDir);
        var indexPath = Path.Combine(indexDir, $"{index.Id}.json");

        if (!FileIntegrity.Matches(indexPath, index.Sha1))
        {
            progress?.Report(new InstallProgress(0, "下载资源索引…"));
            var indexJson = await Http.GetStringAsync(index.Url, cancellationToken);
            await File.WriteAllTextAsync(indexPath, indexJson, cancellationToken);
        }

        var file = JsonSerializer.Deserialize<AssetIndexFile>(
            await File.ReadAllTextAsync(indexPath, cancellationToken));
        var objects = file?.Objects?.Values
            .GroupBy(o => o.Hash, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (objects is null || objects.Count == 0) return;

        var total = objects.Count;
        var done = 0;
        var bytesDone = 0L;

        using var semaphore = new SemaphoreSlim(32);

        var tasks = objects.Select(async obj =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var target = ObjectPath(obj.Hash);

                // 资源文件有几千个，只比对大小（哈希寻址 + 大小一致已足够可靠）
                if (File.Exists(target) && new FileInfo(target).Length == obj.Size) return;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var endpoints = await DownloadSources.AssetAsync(obj.Hash, cancellationToken);
                await DownloadEngine.DownloadAsync(endpoints, target, null, obj.Size, null,
                    cancellationToken);
                Interlocked.Add(ref bytesDone, obj.Size);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"download asset {obj.Hash}");
            }
            finally
            {
                var current = Interlocked.Increment(ref done);
                if (current % 25 == 0 || current == total)
                {
                    progress?.Report(new InstallProgress(
                        total == 0 ? 0 : 100.0 * current / total,
                        $"资源文件 {current}/{total}（{Interlocked.Read(ref bytesDone) / 1048576.0:0.0} MB）",
                        Interlocked.Read(ref bytesDone)));
                }

                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static string ObjectPath(string hash)
        => Path.Combine(GamePaths.AssetsDir, "objects", hash[..2], hash);
}
