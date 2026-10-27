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
        // 带上发布版本号：Worker 更新订阅后，不同边缘节点上的旧缓存会自然失效
        var version = await GetPublishedVersionAsync(cancellationToken);
        var fresh = !string.IsNullOrEmpty(version) && version != _lastPublished;

        var url = $"{BaseUrl}/config?did={Uri.EscapeDataString(DeviceIdentity.Current)}" +
                  (string.IsNullOrEmpty(version) ? string.Empty : $"&v={Uri.EscapeDataString(version)}") +
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

            if (!ClashConfigBuilder.LooksLikeClash(yaml))
            {
                AppLog.Info($"subscription gateway: 返回内容不是 Clash 配置（{yaml.Length} 字符）");
                return (null, "网关返回的不是 Clash 配置");
            }

            _lastPublished = version;
            AppLog.Info($"subscription gateway: {yaml.Length} 字符（发布版本 {version}，fresh={fresh}）");
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

    private static string? _lastPublished;

    /// <summary>网关上一次发布订阅的时间（用于破缓存）；失败返回空串。</summary>
    private static async Task<string> GetPublishedVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync($"{BaseUrl}/status", cancellationToken);
            if (!response.IsSuccessStatusCode) return string.Empty;

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = System.Text.Json.JsonDocument.Parse(text);

            return document.RootElement.TryGetProperty("publishedAt", out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Truncate(string text) =>
        text.Length <= 120 ? text : text[..120];
}
