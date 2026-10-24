using System.Net.Http;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>
/// CurseForge API 客户端（结构先写好，等 API Key 到手后即可启用）。
/// Key 来源：环境变量 MTL_CURSEFORGE_KEY，其次本地 config.json 的 CurseForgeApiKey。
/// </summary>
public sealed class CurseForgeClient
{
    private const string BaseUrl = "https://api.curseforge.com/v1";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string? ApiKey
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("MTL_CURSEFORGE_KEY");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            var fromConfig = AppConfig.Current.CurseForgeApiKey;
            return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig.Trim();
        }
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>取某个 CF 模组支持的游戏版本（未配置 Key 时返回空）。</summary>
    public async Task<IReadOnlyList<string>> GetGameVersionsAsync(int modId,
        CancellationToken cancellationToken = default)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return [];

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/mods/{modId}");
            request.Headers.Add("x-api-key", key);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data)) return [];

            if (!data.TryGetProperty("latestFilesIndexes", out var indexes)
                || indexes.ValueKind != JsonValueKind.Array)
                return [];

            return indexes.EnumerateArray()
                .Select(item => item.TryGetProperty("gameVersion", out var gv) ? gv.GetString() : null)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "curseforge get game versions");
            return [];
        }
    }
}
