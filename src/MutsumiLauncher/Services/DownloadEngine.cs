using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace MutsumiLauncher.Services;

public sealed record DownloadProgress(long Received, long Total, double BytesPerSecond);

/// <summary>所有下载源都不支持分段下载。</summary>
public sealed class RangeNotSupportedException(string message) : Exception(message);

/// <summary>
/// 参考 PCL2 的下载引擎：
/// - 全局线程槽位；
/// - 大文件 HTTP Range 分段，各分段可从不同源下载；
/// - 源失败计数、禁用、超时切换；
/// - 低速/无数据自动断开并换源；
/// - 单线程流式 SHA-1 校验。
/// </summary>
public static class DownloadEngine
{
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

    private const long MinSplitSize = 1024 * 1024;
    private const long PreferredPiece = 16 * 1024 * 1024;
    private const int MaxPieces = 8;
    private const int ReadTimeoutSeconds = 10;

    public static Task<string> DownloadAsync(IReadOnlyList<string> urls, string targetPath,
        string? sha1 = null, long? expectedSize = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        => DownloadAsync(urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(url => new DownloadEndpoint(url))
                .ToList(),
            targetPath, sha1, expectedSize, progress, cancellationToken);

    public static async Task<string> DownloadAsync(IReadOnlyList<DownloadEndpoint> endpoints,
        string targetPath, string? sha1 = null, long? expectedSize = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (endpoints.Count == 0)
            throw new InvalidOperationException("没有可用的下载地址。");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var pool = new SourcePool(endpoints);
        var size = expectedSize ?? await ProbeSizeAsync(pool, cancellationToken);

        if (size is >= MinSplitSize && pool.HasSplitSource)
        {
            try
            {
                await DownloadRangedAsync(pool, targetPath, size.Value, sha1, progress,
                    cancellationToken);

                return targetPath;
            }
            catch (RangeNotSupportedException ex)
            {
                AppLog.Info($"range unsupported, fallback single: {ex.Message}");
            }
        }

        await DownloadSingleAsync(pool, targetPath, sha1, expectedSize, progress, cancellationToken);
        return targetPath;
    }

    private static async Task<long?> ProbeSizeAsync(SourcePool pool, CancellationToken cancellationToken)
    {
        foreach (var endpoint in pool.Snapshot())
        {
            try
            {
                using var head = new HttpRequestMessage(HttpMethod.Head, endpoint.Url);
                using var response = await SendAsync(head, endpoint, cancellationToken);

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is { } length)
                    return length;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"probe head {endpoint.Url}");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var response = await SendAsync(request, endpoint, cancellationToken);

                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range?.Length is { } total) return total;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"probe range {endpoint.Url}");
            }
        }

        return null;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        DownloadEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, endpoint.TimeoutSeconds)));

        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }

    private static async Task<int> ReadWithTimeoutAsync(Stream stream, byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSeconds));

        try
        {
            return await stream.ReadAsync(buffer, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("读取超时（低速断开）");
        }
    }

    private static async Task DownloadSingleAsync(SourcePool pool, string targetPath, string? sha1,
        long? expectedSize, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var temp = targetPath + ".part";
        Exception? lastError = null;

        while (true)
        {
            var endpoint = pool.Next(needSplit: false);

            if (endpoint is null)
                throw new IOException(
                    $"下载失败：{Path.GetFileName(targetPath)}（{lastError?.Message}）", lastError);

            TryDelete(temp);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                using var response = await SendAsync(request, endpoint, cancellationToken);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? expectedSize ?? 0;
                var received = 0L;
                var lastReport = Stopwatch.StartNew();
                var lastBytes = 0L;

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write,
                                 FileShare.None, 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(81920);

                    try
                    {
                        int read;
                        while ((read = await ReadWithTimeoutAsync(source, buffer, cancellationToken)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            hash.AppendData(buffer, 0, read);
                            received += read;

                            if (lastReport.ElapsedMilliseconds >= 150)
                            {
                                var speed = (received - lastBytes)
                                            / Math.Max(0.001, lastReport.Elapsed.TotalSeconds);

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
                    throw new InvalidDataException(
                        $"下载不完整：{Path.GetFileName(targetPath)} {received}/{total}");

                var actual = Convert.ToHexString(hash.GetHashAndReset());

                if (!string.IsNullOrWhiteSpace(sha1)
                    && !actual.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"校验失败：{Path.GetFileName(targetPath)}");

                progress?.Report(new DownloadProgress(received, received, 0));
                File.Move(temp, targetPath, overwrite: true);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                pool.Fail(endpoint);
                AppLog.Error(ex, $"single download failed: {endpoint.Url}");
            }
        }
    }

    private static async Task DownloadRangedAsync(SourcePool pool, string targetPath, long size,
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
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write,
                             FileShare.ReadWrite, 81920, FileOptions.Asynchronous))
            {
                file.SetLength(size);
            }

            using var pieceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tasks = new List<Task>(pieceCount);

            for (var i = 0; i < pieceCount; i++)
            {
                var start = i * pieceSize;
                var end = Math.Min(size - 1, start + pieceSize - 1);

                tasks.Add(DownloadPieceWithFailoverAsync(pool, temp, start, end, size,
                    pieceCts.Token, delta =>
                    {
                        var total = Math.Min(Interlocked.Add(ref received, delta), size);

                        if (lastReport.ElapsedMilliseconds < 150) return;

                        var speed = (total - lastBytes)
                                    / Math.Max(0.001, lastReport.Elapsed.TotalSeconds);

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
                throw new InvalidDataException(
                    $"下载不完整：{Path.GetFileName(targetPath)} {received}/{size}");

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

    private static async Task DownloadPieceWithFailoverAsync(SourcePool pool, string path, long start,
        long end, long totalSize, CancellationToken cancellationToken, Action<long> onBytes)
    {
        Exception? lastError = null;

        while (true)
        {
            var endpoint = pool.Next(needSplit: true);

            if (endpoint is null)
            {
                if (!pool.HasSplitSource && lastError is null)
                    throw new RangeNotSupportedException("所有下载源都不支持 Range");

                throw new RangeNotSupportedException(
                    $"分块下载失败（{start}-{end}）：{lastError?.Message}");
            }

            try
            {
                await DownloadPieceAsync(endpoint, path, start, end, cancellationToken, onBytes);
                return;
            }
            catch (RangeNotSupportedException)
            {
                pool.MarkRangeBroken(endpoint);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                pool.Fail(endpoint);
                AppLog.Error(ex, $"piece {start}-{end} failed: {endpoint.Url}");
            }
        }
    }

    private static async Task DownloadPieceAsync(DownloadEndpoint endpoint, string path, long start,
        long end, CancellationToken cancellationToken, Action<long> onBytes)
    {
        var length = end - start + 1;
        var attempt = 0;

        while (true)
        {
            attempt++;
            await Slots.WaitAsync(cancellationToken);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

                using var response = await SendAsync(request, endpoint, cancellationToken);

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
                    while ((read = await ReadWithTimeoutAsync(stream, buffer, cancellationToken)) > 0)
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= 2) throw;

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

    private sealed class SourcePool
    {
        private readonly object _lock = new();
        private readonly List<Source> _sources;
        private int _cursor;

        public SourcePool(IReadOnlyList<DownloadEndpoint> endpoints)
        {
            _sources = endpoints.Select(endpoint => new Source(endpoint)).ToList();
        }

        public bool HasSplitSource
        {
            get
            {
                lock (_lock)
                    return _sources.Any(source => !source.Disabled && !source.RangeBroken
                                                  && !NoSplitHosts.Contains(source.Host));
            }
        }

        public IReadOnlyList<DownloadEndpoint> Snapshot()
        {
            lock (_lock)
                return _sources.Select(source => source.Endpoint).ToList();
        }

        public DownloadEndpoint? Next(bool needSplit)
        {
            lock (_lock)
            {
                for (var i = 0; i < _sources.Count; i++)
                {
                    var source = _sources[(_cursor + i) % _sources.Count];

                    if (source.Disabled) continue;
                    if (needSplit && (source.RangeBroken || NoSplitHosts.Contains(source.Host)))
                        continue;

                    _cursor = (_cursor + i + 1) % _sources.Count;
                    return source.Endpoint;
                }

                return null;
            }
        }

        public void Fail(DownloadEndpoint endpoint)
        {
            lock (_lock)
            {
                var source = _sources.FirstOrDefault(item => item.Endpoint == endpoint);
                if (source is null) return;

                source.FailCount++;

                if (source.FailCount >= 3)
                    source.Disabled = true;
            }
        }

        public void MarkRangeBroken(DownloadEndpoint endpoint)
        {
            lock (_lock)
            {
                var source = _sources.FirstOrDefault(item => item.Endpoint == endpoint);
                if (source is not null) source.RangeBroken = true;
            }
        }

        private sealed class Source
        {
            public Source(DownloadEndpoint endpoint)
            {
                Endpoint = endpoint;
                Host = new Uri(endpoint.Url).Host;
            }

            public DownloadEndpoint Endpoint { get; }

            public string Host { get; }

            public int FailCount { get; set; }

            public bool RangeBroken { get; set; }

            public bool Disabled { get; set; }
        }
    }
}
