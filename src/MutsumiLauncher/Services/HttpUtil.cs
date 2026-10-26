using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace MutsumiLauncher.Services;

/// <summary>
/// 统一的 HttpClient 工厂。
/// 每条请求都会在「直连」和「代理」（有代理时）之间自动兜底：
/// 国内镜像域名优先直连，其余域名按设置出牌，第一条路失败自动换另一条。
/// </summary>
public static class HttpUtil
{
    public static string DefaultUserAgent => $"MutsumiLauncher/{AppInfo.Version} (github.com/mouchang/MTL)";

    private static readonly object ProbeLock = new();
    private static bool? _systemProxyReachable;

    public static string Mode => (AppConfig.Current.NetworkMode ?? "auto").Trim().ToLowerInvariant();

    /// <summary>国内镜像域名：一律优先直连，不要绕代理。</summary>
    private static bool IsDomestic(string url) =>
        url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase)
        || url.Contains("bangbang93", StringComparison.OrdinalIgnoreCase)
        || url.Contains("mcimirror", StringComparison.OrdinalIgnoreCase);

    /// <summary>探测本机系统代理是否真的可用（端口能连上）。结果缓存到进程结束。</summary>
    public static bool SystemProxyReachable()
    {
        if (_systemProxyReachable is { } cached) return cached;

        lock (ProbeLock)
        {
            if (_systemProxyReachable is { } again) return again;

            _systemProxyReachable = ProbeSystemProxy();
            AppLog.Info($"system proxy available: {_systemProxyReachable}");
            return _systemProxyReachable.Value;
        }
    }

    private static bool ProbeSystemProxy()
    {
        try
        {
            var proxy = WebRequest.GetSystemWebProxy()
                .GetProxy(new Uri("https://www.example.com"));

            if (proxy is null || proxy.Port <= 0) return false;
            if (proxy.Host.Contains("example.com", StringComparison.OrdinalIgnoreCase)) return false;

            using var client = new TcpClient();
            var connect = client.ConnectAsync(proxy.Host, proxy.Port);

            return connect.Wait(TimeSpan.FromMilliseconds(700)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static readonly int[] CommonProxyPorts = [7890, 7897, 10808, 1080];

    /// <summary>
    /// 用户在跑 Clash 但没开「系统代理」（或用的是 TUN）时，系统代理探测会失败。
    /// 这里再试几个常见本地端口，能连上就当代理用——只影响境外域名。
    /// </summary>
    private static WebProxy? DetectLocalProxy()
    {
        foreach (var port in CommonProxyPorts)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(System.Net.IPAddress.Loopback, port);

                if (connect.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected)
                {
                    AppLog.Info($"detected local proxy on 127.0.0.1:{port}");
                    return new WebProxy("http://127.0.0.1:" + port);
                }
            }
            catch
            {
                // 没开就下一个
            }
        }

        return null;
    }

    /// <param name="forceDirect">忽略设置强制直连（诊断用）。</param>
    public static HttpClient Create(TimeSpan? timeout = null, string? userAgent = null,
        bool forceDirect = false)
    {
        var config = AppConfig.Current;
        var mode = forceDirect ? "direct" : Mode;

        var direct = NewHandler();
        SocketsHttpHandler? proxy = null;

        switch (mode)
        {
            case "auto" when SystemProxyReachable():
            case "system":
                proxy = NewHandler();
                proxy.UseProxy = true;
                proxy.Proxy = WebRequest.GetSystemWebProxy();
                break;

            case "auto":
            {
                // 系统代理没开，但本机可能有 Clash 在跑
                var local = DetectLocalProxy();
                if (local is null) break;

                proxy = NewHandler();
                proxy.UseProxy = true;
                proxy.Proxy = local;
                break;
            }

            case "custom" when !string.IsNullOrWhiteSpace(ProxyHostOf(config)):
                proxy = NewHandler();
                proxy.UseProxy = true;
                proxy.Proxy = new WebProxy(ProxyHostOf(config).Trim(),
                    config.ProxyPort <= 0 ? 7890 : config.ProxyPort);
                break;
        }

        var client = new HttpClient(new RouteHandler(mode, direct, proxy))
        {
            Timeout = timeout ?? TimeSpan.FromMinutes(2),
        };

        userAgent ??= DefaultUserAgent;
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return client;
    }

    private static string ProxyHostOf(AppConfig config) => config.ProxyHost ?? "";

    private static SocketsHttpHandler NewHandler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    };

    /// <summary>单次尝试的上限：一条路卡死时不要拖垮整个请求，好歹给另一条路一次机会。</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(25);

    /// <summary>按顺序尝试直连 / 代理，前者失败自动换后者。</summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly string _mode;
        private readonly HttpClient _direct;
        private readonly HttpClient? _proxy;

        public RouteHandler(string mode, SocketsHttpHandler direct, SocketsHttpHandler? proxy)
        {
            _mode = mode;
            _direct = new HttpClient(direct, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            _proxy = proxy is null
                ? null
                : new HttpClient(proxy, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var routes = BuildRoutes(url);

            // 内置代理优先：只有境外资源域名会命中；内核不可用时自动回落直连
            var builtIn = await ProxyCore.Shared.TryGetClientAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (builtIn is not null)
            {
                routes.Insert(0, builtIn);
                ProxyCore.Shared.EnterRequest();
            }

            try
            {
            byte[]? body = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            Exception? last = null;

            foreach (var route in routes)
            {
                var clone = Clone(request, body);
                var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(AttemptTimeout);

                try
                {
                    var response = await route.SendAsync(clone, HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token);

                    // 成功后不能释放 timeout：ResponseHeadersRead 的后续读取还挂着这个 token
                    return response;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    timeout.Dispose();
                    clone.Dispose();
                    throw;
                }
                catch (Exception ex)
                {
                    timeout.Dispose();
                    clone.Dispose();
                    last = ex;

                    AppLog.Info($"网络失败，换下一条路（{(ReferenceEquals(route, _proxy) ? "代理" : "直连")}）：{url} -> {ex.Message}");
                }
            }

            throw new HttpRequestException($"请求失败（直连 / 代理都试过了）：{last?.Message}", last);
            }
            finally
            {
                if (builtIn is not null) ProxyCore.Shared.ExitRequest();
            }
        }

        private List<HttpClient> BuildRoutes(string url)
        {
            var routes = new List<HttpClient>(2);

            if (_proxy is null)
            {
                routes.Add(_direct);
                return routes;
            }

            if (IsDomestic(url))
            {
                // 国内镜像：先直连，真不行再考虑代理
                routes.Add(_direct);
                routes.Add(_proxy);
                return routes;
            }

            if (_mode is "auto" or "system" or "custom")
            {
                // 有代理可用时先走代理（这类域名在国内基本直连不通）
                routes.Add(_proxy);
                routes.Add(_direct);
                return routes;
            }

            routes.Add(_direct);
            routes.Add(_proxy);
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
                if (source.Content is not null)
                {
                    foreach (var header in source.Content.Headers)
                        clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}
