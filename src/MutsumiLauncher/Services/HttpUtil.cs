using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace MutsumiLauncher.Services;

/// <summary>
/// 统一的 HttpClient 工厂。
/// 每条请求都会在「直连」和「代理」之间自动兜底：国内镜像域名优先直连，其余域名先走代理。
/// 代理是**每次请求现查**的（15 秒缓存），所以中途启动 Clash 不用重开启动器也能用上。
/// </summary>
public static class HttpUtil
{
    public static string DefaultUserAgent => $"MutsumiLauncher/{AppInfo.Version} (github.com/mouchang/MTL)";

    public static string Mode => (AppConfig.Current.NetworkMode ?? "auto").Trim().ToLowerInvariant();

    /// <summary>国内镜像域名：一律优先直连，不要绕代理。</summary>
    private static bool IsDomestic(string url) =>
        url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase)
        || url.Contains("bangbang93", StringComparison.OrdinalIgnoreCase)
        || url.Contains("mcimirror", StringComparison.OrdinalIgnoreCase);

    /// <summary>常见本地代理端口（Clash / v2ray / Shadowsocks 客户端）。</summary>
    private static readonly int[] CommonProxyPorts = [7890, 7897, 10808, 1080];

    private static readonly TimeSpan ProxyTtl = TimeSpan.FromSeconds(15);

    /// <summary>按 host:port 复用代理客户端，避免每次探测都新建 handler。</summary>
    private static readonly ConcurrentDictionary<string, HttpClient> ProxyClients =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="forceDirect">忽略设置强制直连（诊断用）。</param>
    public static HttpClient Create(TimeSpan? timeout = null, string? userAgent = null,
        bool forceDirect = false)
    {
        var client = new HttpClient(new RouteHandler(forceDirect ? "direct" : Mode))
        {
            Timeout = timeout ?? TimeSpan.FromMinutes(2),
        };

        userAgent ??= DefaultUserAgent;
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return client;
    }

    private static SocketsHttpHandler NewHandler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    };

    /// <summary>从系统代理设置里读一个 endpoint；没配置时返回 null。</summary>
    private static (string Host, int Port)? SystemProxyEndpoint()
    {
        try
        {
            var proxy = WebRequest.GetSystemWebProxy().GetProxy(new Uri("https://www.example.com"));

            if (proxy is null || proxy.Port <= 0) return null;

            // 没配置系统代理时，GetProxy 会把原地址返回
            if (proxy.Host.Contains("example.com", StringComparison.OrdinalIgnoreCase)) return null;

            return (proxy.Host, proxy.Port);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>试连一下，能连上才算可用（避免配置了代理但没开的情况）。</summary>
    private static async Task<bool> CanConnectAsync(string host, int port, int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port, cancellationToken).AsTask();

            var completed = await Task.WhenAny(connect, Task.Delay(timeoutMs, cancellationToken))
                .ConfigureAwait(false);

            return completed == connect && connect.IsCompletedSuccessfully && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按当前模式找一个可用代理；找不到返回 null（只用直连）。</summary>
    private static async Task<(string Host, int Port)?> FindProxyAsync(string mode,
        CancellationToken cancellationToken)
    {
        if (mode is "auto" or "system")
        {
            if (SystemProxyEndpoint() is { } system
                && await CanConnectAsync(system.Host, system.Port, 400, cancellationToken))
                return system;
        }

        if (mode == "custom")
        {
            var config = AppConfig.Current;
            var host = string.IsNullOrWhiteSpace(config.ProxyHost) ? "127.0.0.1" : config.ProxyHost.Trim();
            var port = config.ProxyPort <= 0 ? 7890 : config.ProxyPort;

            return await CanConnectAsync(host, port, 500, cancellationToken) ? (host, port) : null;
        }

        if (mode == "auto")
        {
            foreach (var port in CommonProxyPorts)
            {
                if (await CanConnectAsync("127.0.0.1", port, 200, cancellationToken))
                    return ("127.0.0.1", port);
            }
        }

        return null;
    }

    private static HttpClient ProxyClientFor(string host, int port)
    {
        var key = host + ":" + port;

        return ProxyClients.GetOrAdd(key, _ =>
        {
            var handler = NewHandler();
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(host, port);

            AppLog.Info($"代理：使用 {key}");
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        });
    }

    /// <summary>单次尝试的上限：一条路卡死时不要拖垮整个请求，好歹给另一条路一次机会。</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(25);

    /// <summary>按顺序尝试直连 / 代理，前者失败自动换后者。</summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly string _mode;
        private readonly HttpClient _direct;
        private readonly object _sync = new();

        private HttpClient? _proxyClient;
        private string? _proxyKey;
        private DateTime _proxyResolvedAt = DateTime.MinValue;
        private bool _proxyProbeFailed;

        /// <summary>内置代理失败后的冷却时间（静态：所有请求共享，别每次都白等一遍）。</summary>
        private static long _builtInDownUntilTicks;

        public RouteHandler(string mode)
        {
            _mode = mode;
            _direct = new HttpClient(NewHandler(), disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }

        /// <summary>现查一次代理（15 秒内复用），中途开 Clash 也能用上。</summary>
        private async Task<HttpClient?> ProxyAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (DateTime.UtcNow - _proxyResolvedAt < ProxyTtl) return _proxyClient;
            }

            (string Host, int Port)? endpoint = null;

            if (_mode is "auto" or "system" or "custom")
                endpoint = await FindProxyAsync(_mode, cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                _proxyResolvedAt = DateTime.UtcNow;

                if (endpoint is { } found)
                {
                    var key = found.Host + ":" + found.Port;
                    if (!string.Equals(key, _proxyKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _proxyClient = ProxyClientFor(found.Host, found.Port);
                        _proxyKey = key;
                    }

                    _proxyProbeFailed = false;
                }
                else
                {
                    _proxyClient = null;
                    _proxyKey = null;

                    if (!_proxyProbeFailed)
                    {
                        _proxyProbeFailed = true;
                        AppLog.Info("代理：没找到可用的系统代理 / 本机端口，先只用直连");
                    }
                }

                return _proxyClient;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var proxy = await ProxyAsync(cancellationToken).ConfigureAwait(false);

            var routes = BuildRoutes(url, proxy);

            // 内置代理优先：只有境外资源域名会命中；最近失败过就先别用它
            HttpClient? builtIn = null;

            if (DateTime.UtcNow.Ticks >= Interlocked.Read(ref _builtInDownUntilTicks))
            {
                builtIn = await ProxyCore.Shared.TryGetClientAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                if (builtIn is not null)
                {
                    routes.Insert(0, builtIn);
                    ProxyCore.Shared.EnterRequest();
                }
            }

            try
            {
                var body = request.Content is null
                    ? null
                    : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                Exception? last = null;

                foreach (var route in routes)
                {
                    var clone = Clone(request, body);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(AttemptTimeout);

                    try
                    {
                        // 成功后不能释放 timeout：ResponseHeadersRead 的后续读取还挂着这个 token
                        return await route.SendAsync(clone, HttpCompletionOption.ResponseHeadersRead,
                            timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        clone.Dispose();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        clone.Dispose();
                        last = ex;

                        var label = ReferenceEquals(route, builtIn) ? "内置代理"
                            : ReferenceEquals(route, proxy) ? "代理"
                            : "直连";

                        AppLog.Info($"网络失败，换下一条路（{label}）：{url} -> {ex.Message}");

                        // 内置代理挂了就别让它继续挡在前面
                        if (ReferenceEquals(route, builtIn))
                            Interlocked.Exchange(ref _builtInDownUntilTicks,
                                DateTime.UtcNow.AddMinutes(2).Ticks);
                    }
                }

                throw new HttpRequestException(
                    BuildFailureMessage(last, proxy, builtIn), last);
            }
            finally
            {
                if (builtIn is not null) ProxyCore.Shared.ExitRequest();
            }
        }

        private string BuildFailureMessage(Exception? last, HttpClient? proxy, HttpClient? builtIn)
        {
            var message = $"请求失败（直连 / 代理都试过了）：{last?.Message}";

            if (proxy is null && builtIn is null)
            {
                message += Environment.NewLine +
                           "提示：没检测到可用代理（系统代理没开，本机 7890 / 7897 / 10808 / 1080 也连不上）。" +
                           "这类域名在国内通常直连不通，可以打开 Clash 后重试，或在「设置 → 下载 → 更改下载源与网络」里指定代理。";
            }
            else if (builtIn is not null)
            {
                message += Environment.NewLine + "提示：内置代理也没连上，可能是订阅节点暂时不可用。";
            }

            return message;
        }

        private List<HttpClient> BuildRoutes(string url, HttpClient? proxy)
        {
            var routes = new List<HttpClient>(2);

            if (proxy is null)
            {
                routes.Add(_direct);
                return routes;
            }

            if (IsDomestic(url))
            {
                // 国内镜像：先直连，真不行再考虑代理
                routes.Add(_direct);
                routes.Add(proxy);
                return routes;
            }

            // 其余情况先走代理（这类域名在国内基本直连不通）
            routes.Add(proxy);
            routes.Add(_direct);
            return routes;
        }

        private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body)
        {
            var clone = new HttpRequestMessage(source.Method, source.RequestUri)
            {
                Version = source.Version,
                VersionPolicy = source.VersionPolicy,
            };

            foreach (var header in source.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (body is not null)
            {
                clone.Content = new ByteArrayContent(body);

                foreach (var header in source.Content!.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _direct.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
