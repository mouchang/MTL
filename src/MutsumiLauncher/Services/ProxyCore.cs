using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
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
    private const string CoreUrl = "https://github.com/MetaCubeX/mihomo/releases/download/v1.19.33/" + CoreZipName;
    private const string CoreSha256 = "7aaf636a4f2172dcd8ec34ac4f52797b059195e5afd6409508c963e545f8864d";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90);

    public static ProxyCore Shared { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();

    private Process? _process;
    private Timer? _idleTimer;
    private HttpClient? _client;
    private int _port;
    private string? _node;
    private DateTime _lastUsed = DateTime.UtcNow;

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

        return true;
    }

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
        lock (_stateLock) _lastUsed = DateTime.UtcNow;
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
            if (core is null) return false;

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

            AppLog.Info($"proxy core: 启动 mihomo，端口 {port}，节点 {built.Value.Node}");

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
                _lastUsed = DateTime.UtcNow;
            }

            LastError = null;
            StartIdleTimer();
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
            _idleTimer?.Dispose();
            _idleTimer = null;
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
        if (File.Exists(CoreExePath)) return CoreExePath;

        try
        {
            Directory.CreateDirectory(CoreDir);

            var zipPath = Path.Combine(CoreDir, CoreZipName);

            if (!File.Exists(zipPath) || !MatchesSha256(zipPath))
            {
                AppLog.Info("proxy core: 下载 mihomo…");
                using var http = HttpUtil.Create(TimeSpan.FromMinutes(10), forceDirect: false);
                await using var stream = await http.GetStreamAsync(CoreUrl, cancellationToken);
                await using var file = File.Create(zipPath);
                await stream.CopyToAsync(file, cancellationToken);
            }

            if (!MatchesSha256(zipPath))
            {
                AppLog.Info("proxy core: 校验失败，删除重下");
                File.Delete(zipPath);
                return null;
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

            File.Delete(zipPath);
            AppLog.Info($"proxy core: 已就绪 {CoreVersion}");
            return File.Exists(CoreExePath) ? CoreExePath : null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "proxy core download");
            return null;
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

    private void StartIdleTimer()
    {
        lock (_stateLock)
        {
            _idleTimer?.Dispose();
            _idleTimer = new Timer(_ =>
            {
                DateTime last;
                lock (_stateLock) last = _lastUsed;

                if (DateTime.UtcNow - last > IdleTimeout && IsRunning) Stop("空闲超时");
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }
    }
}
