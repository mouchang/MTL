using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace MutsumiLauncher.Services;

public sealed record DownloadProgress(long Received, long Total, double BytesPerSecond);

/// <summary>所有下载源都不支持分段下载。</summary>
public sealed class RangeNotSupportedException(string message) : Exception(message);

/// <summary>所有下载源都试过了仍然失败（会触发最后一轮"单线程不续传"重试）。</summary>
public sealed class DownloadExhaustedException(string message, Exception? inner = null)
    : IOException(message, inner);

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
    public static int MaxThreads { get; set; } = 16;

    /// <summary>累计收到的字节：给任务面板算速度用（很多上层调用只有百分比）。</summary>
    public static long TotalReceived => Interlocked.Read(ref _totalReceived);

    private static long _totalReceived;

    /// <summary>下载引擎每读一块就累加，任务面板据此换算速度。</summary>
    private static void CountBytes(long bytes) => Interlocked.Add(ref _totalReceived, bytes);

    private static readonly SemaphoreSlim Slots = new(MaxThreads, MaxThreads);
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromMinutes(30));

    /// <summary>全局限速（所有下载线程共用一份预算）。</summary>
    public static SpeedLimiter Limiter { get; } = new();

    /// <summary>按配置同步限速值（0 = 不限速）。</summary>
    private static void ApplySpeedLimit()
        => Limiter.Configure(Math.Max(0, AppConfig.Current.MaxDownloadSpeedKbps) * 1024L);

    /// <summary>连接级失败（超时 / DNS / 拒绝）过的域名，短时间内排到最后，避免每个文件都白等一遍。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> HostCooldown =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private static void CoolDown(string host)
        => HostCooldown[host] = DateTime.UtcNow.Add(Cooldown).Ticks;

    private static bool IsCooling(string host)
        => HostCooldown.TryGetValue(host, out var until) && until > DateTime.UtcNow.Ticks;

    private static readonly HashSet<string> NoSplitHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "bmclapi2.bangbang93.com",
        "github.com",
        "objects.githubusercontent.com",
        "optifine.net",
        "mtl.dpdns.org",
    };

    // 单连接经常被 CDN / 代理限到几百 KB/s，所以 512KB 以上就允许分段
    /// <summary>
    /// 各分段最近的速度样本。判定“太慢”要用**相对**标准：
    /// 16 条连接平分带宽时每条只有总速度的 1/16，
    /// 拿绝对速度比较会把所有连接都判死（之前越下越慢就是这个原因）。
    /// </summary>
    private static readonly ConcurrentQueue<long> SpeedSamples = new();

    /// <summary>近一段时间各分段速度的中位数；样本不足时返回 0。</summary>
    private static long MedianSpeed()
    {
        if (SpeedSamples.Count < 3) return 0;

        var values = SpeedSamples.ToArray();

        if (values.Length > 48) values = values[^48..];

        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void ObserveSpeed(long bytesPerSecond)
    {
        SpeedSamples.Enqueue(bytesPerSecond);

        while (SpeedSamples.Count > 64) SpeedSamples.TryDequeue(out _);
    }

    /// <summary>低于中位数的 1/6、且不到 32KB/s，才算这条连接有问题。</summary>
    private static long SlowThreshold()
    {
        var median = MedianSpeed();
        return Math.Max(32 * 1024, median / 6);
    }

    /// <summary>分段下载时连接太慢：failover 会从断点续传，不重下。</summary>
    private sealed class SlowSourceException() : Exception("下载源太慢");

    /// <summary>
    /// 一个分段的进度游标。任何失败（超时/断流/换源）都从 Start 继续，
    /// 这样既不会重复下载，也不会把同一批字节统计两遍（之前“下载不完整”就是重复计数造成的）。
    /// </summary>
    private sealed class PieceCursor(long start)
    {
        public long Start { get; set; } = start;

        public long Written { get; set; }
    }

    private const long MinSplitSize = 256 * 1024;
    // 每段 4MB：段太小会让连接数爆炸，反而把代理节点挤爆（越下越慢）
    private const long PreferredPiece = 4 * 1024 * 1024;
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
        ApplySpeedLimit();

        // 本地已经有完全一致的文件（SHA-1 校验通过）就直接复制，不占带宽
        if (LocalFileIndex.TryCopy(targetPath, sha1, expectedSize)) return targetPath;

        var pool = new SourcePool(endpoints);
        var size = expectedSize ?? await ProbeSizeAsync(pool, cancellationToken);

        while (true)
        {
            try
            {
                if (!pool.SingleThreadOnly && size is >= MinSplitSize && pool.HasSplitSource)
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

                await DownloadSingleAsync(pool, targetPath, sha1, expectedSize, progress,
                    cancellationToken);

                return targetPath;
            }
            catch (DownloadExhaustedException) when (pool.CanRetryAsOnce)
            {
                // 所有源都失败过一次：把所有源重新启用，降级为「单线程 + 不续传」再试一轮。
                // 一部分源在多线程下会返回坏数据、或者 Range 行为异常，单线程能救回来（PCL2 同款策略）。
                // 镜像站（MCIM）是边下边缓存的，第一次经常只给半截文件，等一下再试往往就好了。
                var round = pool.BeginFallbackRound();
                AppLog.Info($"all sources failed, retry round {round}: {Path.GetFileName(targetPath)}");
                await Task.Delay(600, cancellationToken);
            }
        }
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

        // HttpUtil 已按域名排好「直连 / 代理」顺序，这里只负责发请求
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
                throw new DownloadExhaustedException(
                    $"下载失败：{Path.GetFileName(targetPath)}（{lastError?.Message}）", lastError);

            TryDelete(temp);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                using var response = await SendAsync(request, endpoint, cancellationToken);
                response.EnsureSuccessStatusCode();

                // 服务器声明的长度和已知大小对不上：多半是镜像还没缓存完，直接换源，别白下
                var declared = response.Content.Headers.ContentLength;
                if (expectedSize is > 0 && declared is > 0 && declared != expectedSize)
                    throw new InvalidDataException(
                        $"内容长度不符：{declared}/{expectedSize}（{Path.GetFileName(targetPath)}）");

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
                            CountBytes(read);

                            await Limiter.AcquireAsync(read, cancellationToken);

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
                pool.Fail(endpoint, ex);
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

                // 错峰启动：同时建一堆连接会把代理节点的队列塞满，前几秒快、后面全变慢
                if (i > 0) await Task.Delay(120, cancellationToken);

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
        var cursor = new PieceCursor(start);
        var slowSwitches = 0;

        while (cursor.Start <= end)
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
                await DownloadPieceAsync(endpoint, path, cursor, end, cancellationToken, onBytes);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SlowSourceException)
            {
                // 太慢：保留已写的数据，换个源从断点继续
                slowSwitches++;
                cursor.Start += cursor.Written;
                cursor.Written = 0;

                pool.MarkSlow(endpoint);

                if (slowSwitches >= 6)
                    throw new IOException($"分段下载太慢（{start}-{end}），已尝试 {slowSwitches} 个源");

                continue;
            }
            catch (RangeNotSupportedException)
            {
                pool.MarkRangeBroken(endpoint);
            }
            catch (Exception ex)
            {
                lastError = ex;

                // 断流 / 超时：已写多少就从哪儿接着下，别整段重来
                cursor.Start += cursor.Written;
                cursor.Written = 0;

                pool.Fail(endpoint, ex);
                AppLog.Error(ex, $"piece {start}-{end} failed: {endpoint.Url}");
            }
        }
    }

    private static async Task DownloadPieceAsync(DownloadEndpoint endpoint, string path, PieceCursor cursor,
        long end, CancellationToken cancellationToken, Action<long> onBytes)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;
            await Slots.WaitAsync(cancellationToken);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(cursor.Start, end);

                using var response = await SendAsync(request, endpoint, cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK)
                    throw new RangeNotSupportedException("服务器忽略 Range，返回 200");

                if (response.StatusCode != HttpStatusCode.PartialContent)
                    response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Write,
                    FileShare.ReadWrite, 81920, FileOptions.Asynchronous);

                file.Seek(cursor.Start, SeekOrigin.Begin);

                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                var length = end - cursor.Start + 1;
                var written = 0L;

                // 测速窗口：每 2 秒算一次这条连接的速度
                var sample = Stopwatch.StartNew();
                var sampleBytes = 0L;
                var slowWindows = 0;

                try
                {
                    int read;
                    while ((read = await ReadWithTimeoutAsync(stream, buffer, cancellationToken)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        written += read;
                        cursor.Written = written;
                        sampleBytes += read;
                        CountBytes(read);
                        onBytes(read);

                        await Limiter.AcquireAsync(read, cancellationToken);

                        if (written > length)
                            throw new InvalidDataException("分段数据超出范围");

                        if (sample.Elapsed.TotalSeconds < 2) continue;

                        var speed = sampleBytes / sample.Elapsed.TotalSeconds;
                        var remaining = length - written;

                        ObserveSpeed((long)speed);

                        // 只在「明显低于其它分段」且「连续三个窗口都慢」且「还剩较多数据」时才换源。
                        // 收尾阶段一律不动：重连开销比剩余数据还大。
                        var threshold = SlowThreshold();

                        if (speed < threshold && remaining > 1024 * 1024)
                        {
                            slowWindows++;

                            if (slowWindows >= 4)
                                throw new SlowSourceException();
                        }
                        else
                        {
                            slowWindows = 0;
                        }

                        sample.Restart();
                        sampleBytes = 0;
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
            catch (SlowSourceException)
            {
                // 慢源交给 failover 处理，不必在这一层重试
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= 2) throw;

                // 这一次已经写进去的字节要计入游标，重试才不会重复下载/重复计数
                cursor.Start += cursor.Written;
                cursor.Written = 0;

                AppLog.Error(ex, $"piece retry {attempt}: {endpoint.Url}");
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
        private int _fallbackRounds;

        public SourcePool(IReadOnlyList<DownloadEndpoint> endpoints)
        {
            _sources = endpoints.Select(endpoint => new Source(endpoint)).ToList();
        }

        /// <summary>当前是否处于「不续传、只单线程」的兜底重试轮。</summary>
        public bool SingleThreadOnly
        {
            get
            {
                lock (_lock) return _fallbackRounds > 0;
            }
        }

        /// <summary>是否还能再降级重试一次。</summary>
        public bool CanRetryAsOnce
        {
            get
            {
                lock (_lock) return _fallbackRounds < MaxFallbackRounds && _sources.Count > 0;
            }
        }

        /// <summary>最多重来几轮（每轮都是全体源重新启用）。</summary>
        private const int MaxFallbackRounds = 2;

        /// <summary>进入兜底轮：所有源重新启用，并且只允许单线程下载。返回当前是第几轮。</summary>
        public int BeginFallbackRound()
        {
            lock (_lock)
            {
                _fallbackRounds++;
                _cursor = 0;

                foreach (var source in _sources)
                {
                    source.Disabled = false;
                    source.FailCount = 0;
                    source.CorruptCount = 0;
                }

                return _fallbackRounds;
            }
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
                // 先找没在冷却中的源，全都在冷却才退回去硬试
                return Find(needSplit, skipCooling: true) ?? Find(needSplit, skipCooling: false);
            }
        }

        private DownloadEndpoint? Find(bool needSplit, bool skipCooling)
        {
            for (var i = 0; i < _sources.Count; i++)
            {
                var source = _sources[(_cursor + i) % _sources.Count];

                if (source.Disabled) continue;
                if (needSplit && (source.RangeBroken || NoSplitHosts.Contains(source.Host))) continue;
                if (skipCooling && IsCooling(source.Host)) continue;

                _cursor = (_cursor + i + 1) % _sources.Count;
                return source.Endpoint;
            }

            return null;
        }

        /// <summary>
        /// 源能连上但太慢：只把它的优先级往后挪一点。
        /// 不能做域名级冷却——一次误判就会把最快的源排掉两分钟，后面全在下慢的。
        /// </summary>
        public void MarkSlow(DownloadEndpoint endpoint)
        {
            lock (_lock)
            {
                var source = _sources.FirstOrDefault(item => item.Endpoint == endpoint);
                if (source is null) return;

                source.SlowCount++;
            }
        }

        public void Fail(DownloadEndpoint endpoint, Exception? exception = null)
        {
            lock (_lock)
            {
                var source = _sources.FirstOrDefault(item => item.Endpoint == endpoint);
                if (source is null) return;

                source.FailCount++;

                // 镜像站（尤其是 MCIM）边下边缓存，第一次经常只返回半截内容，
                // 这种情况先重试，别一次就把源打死
                if (exception is InvalidDataException)
                {
                    source.CorruptCount++;
                    if (source.CorruptCount >= 2) source.Disabled = true;
                    return;
                }

                if (source.FailCount >= 3 || IsPermanentFailure(source, exception))
                    source.Disabled = true;

                // 连不上的域名（超时 / DNS / 重置）冷却一会儿，后面的文件先别试它
                if (exception is TaskCanceledException or TimeoutException
                    || exception is HttpRequestException { StatusCode: null })
                {
                    CoolDown(source.Host);
                }
            }
        }

        /// <summary>
        /// 一次就放弃某个源的错误：地址不存在、网关错误、Range 冲突、校验失败，
        /// 以及非 BMCLAPI 源的 403/429（BMCLAPI 限流是临时的，不该因此禁用）。
        /// </summary>
        private static bool IsPermanentFailure(Source source, Exception? exception)
        {
            switch (exception)
            {
                case null:
                    return false;

                case HttpRequestException http:
                {
                    var status = http.StatusCode;

                    if (status is HttpStatusCode.NotFound
                        or HttpStatusCode.BadGateway
                        or HttpStatusCode.RequestedRangeNotSatisfiable)
                        return true;

                    if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                        return !source.Host.Contains("bmclapi", StringComparison.OrdinalIgnoreCase);

                    return false;
                }

                default:
                    return false;
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
            public int SlowCount { get; set; }

            public Source(DownloadEndpoint endpoint)
            {
                Endpoint = endpoint;
                Host = new Uri(endpoint.Url).Host;
            }

            public DownloadEndpoint Endpoint { get; }

            public string Host { get; }

            public int FailCount { get; set; }

            /// <summary>返回了内容但校验 / 长度不对的次数。</summary>
            public int CorruptCount { get; set; }

            public bool RangeBroken { get; set; }

            public bool Disabled { get; set; }
        }
    }
}