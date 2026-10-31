using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace MutsumiLauncher.Services;

/// <summary>
/// 内置代理内核（mihomo）托管：
/// 首次使用时下载并校验，之后按需启动、空闲自动停，绝不常驻。
/// 配置文件里带着节点凭据，所以停止时立即删除。
/// </summary>
public sealed class ProxyCore
{
    public const string CoreVersion = "v1.19.33";

    private const string CoreZipName = "mihomo-windows-amd64-compatible-v" + "1.19.33.zip";
    private const string CoreSha256 = "7aaf636a4f2172dcd8ec34ac4f52797b059195e5afd6409508c963e545f8864d";

    /// <summary>订阅里的节点数（健康与否另说）。下载并发按它分摊：每个节点最多 3 条连接。</summary>
    public static int NodeCount { get; private set; } = 3;

    /// <summary>下载源按顺序尝试：自建边缘缓存优先，GitHub 兜底。两者都要过 SHA-256 校验。</summary>
    private static readonly string[] CoreUrls =
    [
        "https://mtl.dpdns.org/core/mihomo.zip",
        "https://github.com/MetaCubeX/mihomo/releases/download/v1.19.33/" + CoreZipName,
    ];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(25);


    public static ProxyCore Shared { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();

    private Process? _process;
    private HttpClient? _client;
    private int _port;
    private string? _node;
    private int _active;
    private Timer? _stopTimer;

    /// <summary>交互请求（搜索 / 详情 / 图标）结束后多久停内核。</summary>
    private const int RequestGraceSeconds = 20;

    /// <summary>连续下载之间留一点粘合时间，避免每个文件都重启一次内核。</summary>
    private const int DownloadCoalesceSeconds = 3;

    static ProxyCore()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shared.Stop("进程退出");
    }

    public string? LastError { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock) return _process is { HasExited: false };
        }
    }

    public string? ActiveNode
    {
        get
        {
            lock (_stateLock) return _node;
        }
    }

    public int Port
    {
        get
        {
            lock (_stateLock) return _port;
        }
    }

    private static string CoreDir => Path.Combine(AppPaths.DataRoot, "proxy");

    private static string CoreExePath => Path.Combine(CoreDir, "mihomo.exe");

    private static string ConfigPath => Path.Combine(CoreDir, "config.yaml");

    /// <summary>
    /// 是否走内置代理：默认「除了明确排除的，全部走」——
    /// 资源获取和下载一律经代理，只有正版账号登录、订阅网关和本机地址例外。
    /// </summary>
    public static bool ShouldRoute(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        if (IsLoopback(host)) return false;
        if (IsAuthHost(host)) return false;
        if (IsGatewayHost(host)) return false;
        if (IsVanillaHost(host)) return false;

        // 只在下载 Modrinth / CurseForge 资源时启用内置代理：
        // 其它请求（GitHub、更新检查等）走直连，避免内核被无谓地拉起来
        return IsCommunityHost(host);
    }

    /// <summary>社区资源：Modrinth 与 CurseForge 的 API / 文件 CDN。</summary>
    private static bool IsCommunityHost(string host) =>
        host.EndsWith("modrinth.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("curseforge.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("forgecdn.net", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("overwolf.wtf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 原版资源（Mojang 版本清单 / client jar / libraries / assets）和它们的国内镜像走直连：
    /// 这些域名绕道国外反而慢。加载器 maven（Forge / Fabric / NeoForge）不在此列，照常走代理。
    /// </summary>
    private static bool IsVanillaHost(string host) =>
        host.EndsWith("mojang.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("minecraft.net", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("bangbang93.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("mcimirror.top", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
        || host.StartsWith("10.", StringComparison.Ordinal)
        || host.StartsWith("192.168.", StringComparison.Ordinal)
        || host.StartsWith("172.16.", StringComparison.Ordinal);

    /// <summary>
    /// 正版账号登录 / 鉴权：只走本机网络，不经过代理。
    /// 注意这里只排除「鉴权」域名——版本清单、client jar、libraries 这些是资源，照常走代理。
    /// </summary>
    private static bool IsAuthHost(string host) =>
        host.EndsWith("live.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("microsoftonline.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("microsoftonline.cn", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("xboxlive.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("xbox.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("minecraftservices.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("api.mojang.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("authserver.mojang.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("sessionserver.mojang.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>订阅网关与自建镜像：走代理没有意义，还会绕回来递归。</summary>
    private static bool IsGatewayHost(string host) =>
        host.EndsWith("mtl.dpdns.org", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("dpdns.org", StringComparison.OrdinalIgnoreCase);

    /// <summary>给某条请求取一个走内置代理的客户端；不需要/不可用时返回 null，调用方回落直连。</summary>
    public async Task<HttpClient?> TryGetClientAsync(string url, CancellationToken cancellationToken)
    {
        if (!AppConfig.Current.BuiltInProxyEnabled) return null;

        string host;
        try
        {
            host = new Uri(url).Host;
        }
        catch
        {
            return null;
        }

        if (!ShouldRoute(host)) return null;

        // 正在启动（下载内核 / 拉订阅）时不要再递归进代理
        if (_gate.CurrentCount == 0) return null;

        if (!await EnsureRunningAsync(cancellationToken).ConfigureAwait(false)) return null;

        Touch();
        return _client;
    }

    public void Touch()
    {
    }

    /// <summary>
    /// 占用一次会话，返回值 Dispose 时释放。
    /// 下载任务用这种方式把整批文件包住，计数归零后立刻停内核。
    /// </summary>
    public IDisposable Pin()
    {
        Interlocked.Increment(ref _active);
        CancelStop();

        return new Session(this);
    }

    private sealed class Session(ProxyCore owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            owner.Release(DownloadCoalesceSeconds);
        }
    }

    /// <summary>单条 HTTP 请求占用；结束后按宽限期停内核。</summary>
    public void EnterRequest()
    {
        Interlocked.Increment(ref _active);
        CancelStop();
    }

    public void ExitRequest() => Release(RequestGraceSeconds);

    private void Release(int stopDelaySeconds)
    {
        if (Interlocked.Decrement(ref _active) > 0) return;

        ScheduleStop(stopDelaySeconds);
    }

    private void ScheduleStop(int seconds)
    {
        if (seconds <= 0)
        {
            StopNow("空闲");
            return;
        }

        lock (_stateLock)
        {
            _stopTimer?.Dispose();
            _stopTimer = new Timer(_ =>
            {
                if (Volatile.Read(ref _active) > 0) return;
                StopNow("空闲超时");
            }, null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelStop()
    {
        lock (_stateLock)
        {
            _stopTimer?.Dispose();
            _stopTimer = null;
        }
    }

    /// <summary>立即停内核（测试按钮、用户关掉代理时用）。</summary>
    public void StopNow(string reason) => Stop(reason);

    /// <summary>
    /// 测试按钮：强制启动内核 → 真实请求一次 Modrinth → 立刻停止。
    /// 不受「启用」开关影响，测完不留后台进程。
    /// </summary>
    public async Task<(bool Ok, long Milliseconds, string? Message)> RunSelfTestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            if (!await EnsureRunningAsync(cancellationToken).ConfigureAwait(false))
                return (false, watch.ElapsedMilliseconds, LastError ?? "内核启动失败");

            using var client = CreateClient(Port);

            using var response = await client.GetAsync(
                "https://api.modrinth.com/v2/tag/game_version", cancellationToken);

            watch.Stop();

            return response.IsSuccessStatusCode
                ? (true, watch.ElapsedMilliseconds, ActiveNode)
                : (false, watch.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "proxy self test");
            return (false, 0, DescribeSelfTestFailure(ex));
        }
        finally
        {
            Stop("自检结束");
        }
    }

    /// <summary>把自检失败的异常翻译成人话，界面上别直接甩英文堆栈。</summary>
    private static string DescribeSelfTestFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Security.Authentication.AuthenticationException
                or System.Net.Sockets.SocketException
                or System.IO.IOException
                or System.Net.Http.HttpRequestException)
            {
                return "连不上节点（订阅可能已失效，或者节点在维护）";
            }
        }

        return exception.Message;
    }

    /// <summary>按需启动内核。并发调用会串行化。</summary>
    public async Task<bool> EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            Touch();
            return true;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (IsRunning)
            {
                Touch();
                return true;
            }

            var core = await EnsureCoreAsync(cancellationToken).ConfigureAwait(false);

            if (core is null)
            {
                LastError = "内核下载失败（网络不稳定，或两个下载源都连不上）";
                return false;
            }

            var (yaml, reason) = await SubscriptionGateway.FetchAsync(cancellationToken).ConfigureAwait(false);

            if (yaml is null)
            {
                LastError = reason;
                AppLog.Info($"proxy core: 订阅不可用（{reason}），回落直连");
                return false;
            }

            var port = FindFreePort();
            var built = ClashConfigBuilder.Build(yaml, AppConfig.Current.BuiltInProxyNode, port);

            if (built is null)
            {
                LastError = "订阅里没有可用节点";
                return false;
            }

            Directory.CreateDirectory(CoreDir);
            await File.WriteAllTextAsync(ConfigPath, built.Value.Yaml, cancellationToken).ConfigureAwait(false);

            NodeCount = Math.Max(1, built.Value.NodeCount);
            AppLog.Info($"proxy core: 启动 mihomo，端口 {port}，节点 {built.Value.Node}（共 {NodeCount} 个）");

            var process = Start(core, ConfigPath);

            if (process is null || !await WaitForPortAsync(port, StartupTimeout, cancellationToken)
                    .ConfigureAwait(false))
            {
                LastError = "内核启动失败";
                Stop("启动失败");
                return false;
            }

            lock (_stateLock)
            {
                _process = process;
                _port = port;
                _node = built.Value.Node;
                _client?.Dispose();
                _client = CreateClient(port);
            }

            LastError = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "proxy core start");
            LastError = ex.Message;
            Stop("异常");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Stop(string reason)
    {
        lock (_stateLock)
        {
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "proxy core kill");
                }

                _process.Dispose();
                _process = null;
                AppLog.Info($"proxy core: 已停止（{reason}）");
            }

            _client?.Dispose();
            _client = null;
            _node = null;
            _port = 0;
            _stopTimer?.Dispose();
            _stopTimer = null;
            Interlocked.Exchange(ref _active, 0);
        }

        // 配置里带着节点凭据，用完就删
        try
        {
            if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
        }
        catch
        {
            // 删不掉就算了
        }
    }

    // ------------------------------------------------------------ 内核

    private static async Task<string?> EnsureCoreAsync(CancellationToken cancellationToken)
    {
        if (IsCoreReady()) return CoreExePath;

        try
        {
            Directory.CreateDirectory(CoreDir);

            var zipPath = Path.Combine(CoreDir, CoreZipName);
            var partPath = zipPath + ".part";

            if (!File.Exists(zipPath) || !MatchesSha256(zipPath))
            {
                TryDelete(zipPath);

                // 安装包里带了内核就不用下载：首次启动也能秒开
                var bundled = Path.Combine(AppContext.BaseDirectory, "core", "mihomo.zip");

                if (File.Exists(bundled) && MatchesSha256(bundled))
                {
                    AppLog.Info("proxy core: 使用安装包内置内核");
                    File.Copy(bundled, zipPath, overwrite: true);
                }
                else if (!await DownloadCoreAsync(partPath, zipPath, cancellationToken).ConfigureAwait(false))
                {
                    AppLog.Info("proxy core: 所有下载源都失败");
                    return null;
                }
            }

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.FirstOrDefault(item =>
                    item.Name.StartsWith("mihomo", StringComparison.OrdinalIgnoreCase)
                    && item.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (entry is null)
                {
                    AppLog.Info("proxy core: 压缩包里没有 mihomo.exe");
                    return null;
                }

                entry.ExtractToFile(CoreExePath, overwrite: true);
            }

            // 压缩包只用来提取一次，留着白占 22 MB
            TryDelete(zipPath);
            TryDelete(zipPath + ".part");

            AppLog.Info($"proxy core: 已就绪 {CoreVersion}");
            return IsCoreReady() ? CoreExePath : null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "proxy core download");
            return null;
        }
    }

    /// <summary>
    /// 下载内核 zip：先写 `.part`，校验通过再改名成正式文件。
    /// 断线时可以从断点续传 —— 以前直接 File.Create 覆盖，一断线就把已经下好的部分全截断了，
    /// 慢镜像上永远下不完。
    /// </summary>
    private static async Task<bool> DownloadCoreAsync(string partPath, string zipPath,
        CancellationToken cancellationToken)
    {
        using var http = HttpUtil.Create(TimeSpan.FromMinutes(10));

        foreach (var url in CoreUrls)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var start = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

                try
                {
                    AppLog.Info($"proxy core: 下载内核 {url}（第 {attempt} 次，从 {start} 字节起）");

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (start > 0) request.Headers.Range = new RangeHeaderValue(start, null);

                    using var response = await http
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();

                    // 服务器不认 Range 时会回 200 全量，这时候要重新从头写
                    var append = start > 0 && response.StatusCode == HttpStatusCode.PartialContent;

                    await using (var file = new FileStream(partPath,
                                     append ? FileMode.Append : FileMode.Create,
                                     FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                    }

                    if (MatchesSha256(partPath))
                    {
                        File.Move(partPath, zipPath, overwrite: true);
                        return true;
                    }

                    AppLog.Info("proxy core: 内容校验不通过，丢掉重下");
                    TryDelete(partPath);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"proxy core download {url}");
                    await Task.Delay(700, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return false;
    }

    /// <summary>内核文件存在且大小合理（防止下了一半的残文件被当成可用）。</summary>
    private static bool IsCoreReady()
    {
        try
        {
            var info = new FileInfo(CoreExePath);
            return info.Exists && info.Length > 5 * 1024 * 1024;
        }
        catch
        {
            return false;
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
            // 删不掉就算了，下次覆盖
        }
    }

    private static bool MatchesSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream))
                .Equals(CoreSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static Process? Start(string exe, string configPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(configPath)!;

            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            info.ArgumentList.Add("-d");
            info.ArgumentList.Add(directory);
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add(configPath);

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) AppLog.Info($"[mihomo] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) AppLog.Info($"[mihomo] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "proxy core start process");
            return null;
        }
    }

    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                if (client.Connected) return true;
            }
            catch
            {
                // 还没起来
            }

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static HttpClient CreateClient(int port)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy("http://127.0.0.1:" + port),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
    }


}