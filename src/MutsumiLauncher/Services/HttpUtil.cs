using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace MutsumiLauncher.Services;

/// <summary>统一的 HttpClient 工厂：默认直连（不走系统代理），可切换系统代理或自定义代理。</summary>
public static class HttpUtil
{
    public const string DefaultUserAgent = "MutsumiLauncher/0.1.0 (github.com/mouchang/MTL)";

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

    public static HttpClient Create(TimeSpan? timeout = null, string? userAgent = DefaultUserAgent)
    {
        var config = AppConfig.Current;

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
        };

        switch (Mode)
        {
            case "auto" when SystemProxyReachable():
            case "system":
                handler.UseProxy = true;
                handler.Proxy = WebRequest.GetSystemWebProxy();
                break;

            case "custom" when !string.IsNullOrWhiteSpace(config.ProxyHost):
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(config.ProxyHost.Trim(),
                    config.ProxyPort <= 0 ? 7890 : config.ProxyPort);
                break;

            default:
                // 直连：镜像源在国内可直连，避免系统代理关掉后所有请求失败
                handler.UseProxy = false;
                break;
        }

        var client = new HttpClient(new CdnHandler(handler)) { Timeout = timeout ?? TimeSpan.FromMinutes(2) };
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return client;
    }

    /// <summary>把允许的 host 重写到 CDN；CDN 拒绝或失败时自动回退直连（并记住该 host 本次会话不再走 CDN）。</summary>
    private sealed class CdnHandler : DelegatingHandler
    {
        private static readonly ConcurrentDictionary<string, bool> BrokenHosts = new(StringComparer.OrdinalIgnoreCase);

        public CdnHandler(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            var baseUrl = DownloadMirror.CustomCdnBase;

            if (uri is null || baseUrl is null
                || !DownloadMirror.ShouldUseCdn(uri)
                || BrokenHosts.ContainsKey(uri.Host))
                return await base.SendAsync(request, cancellationToken);

            byte[]? body = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            try
            {
                using var cdnRequest = Clone(request, DownloadMirror.BuildCdnUri(uri, baseUrl), body);
                var response = await base.SendAsync(cdnRequest, cancellationToken);

                if (response.IsSuccessStatusCode) return response;

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                    or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
                {
                    BrokenHosts[uri.Host] = true;
                }

                AppLog.Info($"cdn fallback {uri.Host}: {(int)response.StatusCode}");
                response.Dispose();
            }
            catch (Exception ex)
            {
                BrokenHosts[uri.Host] = true;
                AppLog.Error(ex, $"cdn request {uri.Host}");
            }

            using var directRequest = Clone(request, uri, body);
            return await base.SendAsync(directRequest, cancellationToken);
        }

        private static HttpRequestMessage Clone(HttpRequestMessage source, Uri uri, byte[]? body)
        {
            var clone = new HttpRequestMessage(source.Method, uri)
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
