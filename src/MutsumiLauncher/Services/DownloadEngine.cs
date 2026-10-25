using System.Buffers;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace MutsumiLauncher.Services;

public sealed record DownloadProgress(long Received, long Total, double BytesPerSecond);

/// <summary>下载源不支持分段下载。</summary>
public sealed class RangeNotSupportedException(string message) : Exception(message);

/// <summary>
/// 参考 PCL2 的下载引擎：
/// - 全局线程数上限，避免无限开连接；
/// - 大文件 HTTP Range 多段并发，小文件/特殊源单线程；
/// - 源失败自动切换，校验 SHA-1，流式写盘；
/// - 进度按字节上报。
/// </summary>
public static class DownloadEngine
{
    /// <summary>全局最大并发下载线程数（含所有任务、所有分段）。</summary>
    public static int MaxThreads { get; set; } = 32;

    private static readonly SemaphoreSlim Slots = new(MaxThreads, MaxThreads);

    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromMinutes(30));

    private static readonly HashSet<string> NoSplitHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "bmclapi2.bangbang93.com",
        "github.com",
        "objects.githubusercontent.com",
        "optifine.net",
    };

    private const long MinSplitSize = 1024 * 1024;      // 小于 1MB 不分段
    private const long MinPieceSize = 512 * 1024;       // 每段至少 512KB
    private const long PreferredPiece = 16 * 1024 * 1024;
    private const int MaxPieces = 8;

    public static async Task<string> DownloadAsync(IReadOnlyList<string> urls, string targetPath,
        string? sha1 = null, long? expectedSize = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var sources = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sources.Count == 0)
            throw new InvalidOperationException("没有可用的下载地址。");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        Exception? lastError = null;

        foreach (var source in sources)
        {
            try
            {
                var uri = new Uri(source);
                var allowSplit = !NoSplitHosts.Contains(uri.Host);

                var size = expectedSize ?? await TryGetSizeAsync(source, cancellationToken);

                if (allowSplit && size is >= MinSplitSize)
                {
                    try
                    {
                        await DownloadRangedAsync(source, targetPath, size.Value, sha1, progress,
                            cancellationToken);
                        return targetPath;
                    }
                    catch (RangeNotSupportedException)
                    {
                        AppLog.Info($"range not supported, fallback single: {source}");
                    }
                }

                await DownloadSingleAsync(source, targetPath, sha1, expectedSize, progress,
                    cancellationToken);
                return targetPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                AppLog.Error(ex, $"download failed, trying next source: {source}");
            }
        }

        throw new IOException(
            $"下载失败：{Path.GetFileName(targetPath)}（{lastError?.Message}）", lastError);
    }

    private static async Task<long?> TryGetSizeAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is { } length)
                return length;
        }
        catch
        {
            // HEAD 不可用时退回 Range 探测
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range?.Length is { } total) return total;
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }

    private static async Task DownloadSingleAsync(string url, string targetPath, string? sha1,
        long? expectedSize, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var temp = targetPath + ".part";

        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? expectedSize ?? 0;
            var received = 0L;
            var lastReport = Stopwatch.StartNew();
            var lastBytes = 0L;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(81920);

                try
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        hash.AppendData(buffer, 0, read);
                        received += read;

                        if (lastReport.ElapsedMilliseconds >= 150)
                        {
                            var speed = (received - lastBytes) / Math.Max(0.001, lastReport.Elapsed.TotalSeconds);
                            progress?.Report(new DownloadProgress(received, total, speed));
                            lastReport.Restart();
                            lastBytes = received;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (total > 0 && received != total)
                throw new InvalidDataException($"下载不完整：{Path.GetFileName(targetPath)} {received}/{total}");

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.IsNullOrWhiteSpace(sha1) && !actual.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"校验失败：{Path.GetFileName(targetPath)}");

            progress?.Report(new DownloadProgress(received, received, 0));
            File.Move(temp, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task DownloadRangedAsync(string url, string targetPath, long size,
        string? sha1, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var temp = targetPath + ".part";
        var pieceCount = Math.Clamp((int)(size / PreferredPiece), 2, MaxPieces);
        var pieceSize = (size + pieceCount - 1) / pieceCount;

        var received = 0L;
        var lastReport = Stopwatch.StartNew();
        var lastBytes = 0L;

        try
        {
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.ReadWrite,
                             81920, FileOptions.Asynchronous))
            {
                file.SetLength(size);
            }

            using var pieceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tasks = new List<Task>(pieceCount);

            for (var i = 0; i < pieceCount; i++)
            {
                var start = i * pieceSize;
                var end = Math.Min(size - 1, start + pieceSize - 1);
                tasks.Add(DownloadPieceAsync(url, temp, start, end, pieceCts.Token,
                    delta =>
                    {
                        var total = Interlocked.Add(ref received, delta);

                        if (lastReport.ElapsedMilliseconds < 150) return;

                        var speed = (total - lastBytes) / Math.Max(0.001, lastReport.Elapsed.TotalSeconds);
                        progress?.Report(new DownloadProgress(total, size, speed));
                        lastReport.Restart();
                        lastBytes = total;
                    }));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                pieceCts.Cancel();
                throw;
            }

            if (received != size)
                throw new InvalidDataException($"下载不完整：{Path.GetFileName(targetPath)} {received}/{size}");

            if (!string.IsNullOrWhiteSpace(sha1))
            {
                await using var stream = File.OpenRead(temp);
                var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken));

                if (!actual.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"校验失败：{Path.GetFileName(targetPath)}");
            }

            progress?.Report(new DownloadProgress(size, size, 0));
            File.Move(temp, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task DownloadPieceAsync(string url, string path, long start, long end,
        CancellationToken cancellationToken, Action<long> onBytes)
    {
        var length = end - start + 1;
        var attempt = 0;

        while (true)
        {
            attempt++;
            await Slots.WaitAsync(cancellationToken);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

                using var response = await Http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK)
                    throw new RangeNotSupportedException("服务器忽略 Range，返回 200");

                if (response.StatusCode != HttpStatusCode.PartialContent)
                    response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Write,
                    FileShare.ReadWrite, 81920, FileOptions.Asynchronous);

                file.Seek(start, SeekOrigin.Begin);

                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                var written = 0L;

                try
                {
                    int read;
                    while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        written += read;
                        onBytes(read);

                        if (written > length)
                            throw new InvalidDataException("分段数据超出范围");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                if (written != length)
                    throw new IOException($"分段不完整：{written}/{length}");

                return;
            }
            catch (RangeNotSupportedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= 3) throw;

                AppLog.Error(ex, $"piece {start}-{end} retry {attempt}");
                await Task.Delay(300 * attempt, cancellationToken);
            }
            finally
            {
                Slots.Release();
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 忽略
        }
    }
}
