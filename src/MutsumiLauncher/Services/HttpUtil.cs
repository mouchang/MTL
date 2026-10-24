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

        var client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromMinutes(2) };
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return client;
    }
}
