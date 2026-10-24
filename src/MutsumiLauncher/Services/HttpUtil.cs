using System.Net;
using System.Net.Http;

namespace MutsumiLauncher.Services;

/// <summary>统一的 HttpClient 工厂：默认直连（不走系统代理），可切换系统代理或自定义代理。</summary>
public static class HttpUtil
{
    public const string DefaultUserAgent = "MutsumiLauncher/0.1.0 (github.com/mouchang/MTL)";

    public static string Mode => (AppConfig.Current.NetworkMode ?? "direct").Trim().ToLowerInvariant();

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
