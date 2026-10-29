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

    /// <summary>本进程是否已经取过一次订阅（第一次会带 fresh=1 破缓存）。</summary>
    private static bool _fetchedOnce;

    /// <summary>取订阅；失败返回原因，调用方回落直连。</summary>
    public static async Task<(string? Yaml, string Reason)> FetchAsync(CancellationToken cancellationToken = default)
    {
        // fresh=1 跳过边缘缓存：进程内第一次取订阅时用一次，避免拿到更新前的旧配置。
        // 之后走缓存，省一次跨境往返（网关自己会在 publish 时清缓存）。
        var fresh = !_fetchedOnce;
        _fetchedOnce = true;

        var url = $"{BaseUrl}/config?did={Uri.EscapeDataString(DeviceIdentity.Current)}" +
                  (fresh ? "&fresh=1" : string.Empty);

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

            // 网关会告诉我们这份配置是 KV 里的副本还是回源拿的，排查时很有用
            var source = response.Headers.TryGetValues("X-MTL-Source", out var values)
                ? values.FirstOrDefault() ?? "?"
                : "?";

            if (!ClashConfigBuilder.LooksLikeClash(yaml))
            {
                AppLog.Info($"subscription gateway: 返回内容不是 Clash 配置（{yaml.Length} 字符，来源 {source}）");
                return (null, "网关返回的不是 Clash 配置");
            }

            AppLog.Info($"subscription gateway: {yaml.Length} 字符（来源 {source}，fresh={fresh}）");
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