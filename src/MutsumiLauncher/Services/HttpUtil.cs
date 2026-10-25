using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace MutsumiLauncher.Services;

/// <summary>统一的 HttpClient 工厂：默认直连（不走系统代理），可切换系统代理或自定义代理。</summary>
public static class HttpUtil
{
    public static string DefaultUserAgent => $"MutsumiLauncher/{AppInfo.Version} (github.com/mouchang/MTL)";

    private static readonly object ProbeLock = new();
    private static bool? _systemProxyReachable;

    public static string Mode => (AppConfig.Current.NetworkMode ?? "auto").Trim().ToLowerInvariant();

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

    public static HttpClient Create(TimeSpan? timeout = null, string? userAgent = null)
    {
        var config = AppConfig.Current;

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
        };

        var usingProxy = false;

        switch (Mode)
        {
            case "auto" when SystemProxyReachable():
            case "system":
                handler.UseProxy = true;
                handler.Proxy = WebRequest.GetSystemWebProxy();
                usingProxy = true;
                break;

            case "custom" when !string.IsNullOrWhiteSpace(config.ProxyHost):
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(config.ProxyHost.Trim(),
                    config.ProxyPort <= 0 ? 7890 : config.ProxyPort);
                usingProxy = true;
                break;

            default:
                // 直连：镜像源在国内可直连，避免系统代理关掉后所有请求失败
                handler.UseProxy = false;
                break;
        }

        HttpMessageHandler pipeline = usingProxy ? new DirectFallbackHandler(handler) : handler;

        var client = new HttpClient(pipeline) { Timeout = timeout ?? TimeSpan.FromMinutes(2) };

        userAgent ??= DefaultUserAgent;
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return client;
    }

    /// <summary>代理请求遇到 SSL / 连接错误时，自动改用直连重试一次。</summary>
    private sealed class DirectFallbackHandler : DelegatingHandler
    {
        private static readonly HttpClient Direct = new(new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        public DirectFallbackHandler(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[]? body = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            try
            {
                using var first = Clone(request, body);
                return await base.SendAsync(first, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                AppLog.Error(ex, $"proxy request failed, retry direct: {request.RequestUri}");

                using var retry = Clone(request, body);
                return await Direct.SendAsync(retry, cancellationToken);
            }
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
