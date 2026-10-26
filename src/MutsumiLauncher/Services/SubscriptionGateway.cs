using System.Net.Http;

namespace MutsumiLauncher.Services;

/// <summary>
/// 订阅网关：从 mtl.dpdns.org/sub 取 Clash YAML。
/// 订阅链接本身只存在 Cloudflare，客户端拿不到。
/// </summary>
public static class SubscriptionGateway
{
    public const string BaseUrl = "https://mtl.dpdns.org/sub";

    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(60), forceDirect: false);

    /// <summary>网关是否配置好（只做一次轻量探测，失败也不影响启动）。</summary>
    public static async Task<(string? Yaml, string Reason)> FetchAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/config?did={Uri.EscapeDataString(DeviceIdentity.Current)}";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                AppLog.Info($"subscription gateway: {response.StatusCode} {Truncate(text)}");
                return (null, $"网关返回 {(int)response.StatusCode}");
            }

            var yaml = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!ClashConfigBuilder.LooksLikeClash(yaml))
            {
                AppLog.Info($"subscription gateway: 返回内容不是 Clash 配置（{yaml.Length} 字符）");
                return (null, "网关返回的不是 Clash 配置");
            }

            AppLog.Info($"subscription gateway: {yaml.Length} 字符");
            return (yaml, "ok");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "subscription gateway");
            return (null, "无法连接订阅网关");
        }
    }

    private static string Truncate(string text) =>
        text.Length <= 120 ? text : text[..120];
}
