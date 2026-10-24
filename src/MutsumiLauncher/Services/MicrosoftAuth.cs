using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record DeviceCodeInfo(string DeviceCode, string UserCode, string VerificationUri,
    int Interval, int ExpiresIn);

public sealed record MicrosoftProfile(string Name, string Uuid, string AccessToken, string? SkinUrl);

/// <summary>微软设备码登录 → Xbox Live → XSTS → Minecraft 服务。</summary>
public static class MicrosoftAuth
{
    private const string DeviceCodeUrl =
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";

    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    private const string Scope = "XboxLive.signin offline_access";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromSeconds(30));

    public static async Task<DeviceCodeInfo> StartAsync(string clientId, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope,
        });

        using var response = await Http.PostAsync(DeviceCodeUrl, content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"设备码请求失败：{ReadError(text)}");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return new DeviceCodeInfo(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5,
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 900);
    }

    /// <summary>轮询直到用户在浏览器里完成授权。</summary>
    public static async Task<string> WaitForTokenAsync(string clientId, DeviceCodeInfo code,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);
        var interval = Math.Max(2, code.Interval);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = code.DeviceCode,
            });

            using var response = await Http.PostAsync(TokenUrl, content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.TryGetProperty("access_token", out var accessToken))
                return accessToken.GetString()!;

            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : null;

            if (error == "authorization_pending") continue;
            if (error == "slow_down")
            {
                interval += 5;
                continue;
            }

            throw new InvalidOperationException(error switch
            {
                "authorization_declined" => "授权被取消。",
                "expired_token" => "设备码已过期，请重新登录。",
                _ => $"登录失败：{ReadError(text)}",
            });
        }

        throw new TimeoutException("设备码已过期，请重新登录。");
    }

    public static async Task<MicrosoftProfile> LoginMinecraftAsync(string msaAccessToken,
        CancellationToken cancellationToken)
    {
        // 1. Xbox Live 用户认证
        var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate",
            new
            {
                Properties = new
                {
                    AuthMethod = "RPS",
                    SiteName = "user.auth.xboxlive.com",
                    RpsTicket = $"d={msaAccessToken}",
                },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT",
            }, cancellationToken);

        var xblToken = xbl.GetProperty("Token").GetString()!;
        var uhs = xbl.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;

        // 2. XSTS 授权
        JsonElement xsts;
        try
        {
            xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize",
                new
                {
                    Properties = new
                    {
                        SandboxId = "RETAIL",
                        UserTokens = new[] { xblToken },
                    },
                    RelyingParty = "rp://api.minecraftservices.com/",
                    TokenType = "JWT",
                }, cancellationToken);
        }
        catch (MicrosoftAuthException ex) when (ex.Status == 401)
        {
            throw new InvalidOperationException(
                "该微软账号没有 Xbox 档案（需要先在 xbox.com 创建档案），或所在地区不支持。");
        }

        var xstsToken = xsts.GetProperty("Token").GetString()!;

        // 3. 换 Minecraft accessToken
        var minecraft = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" }, cancellationToken);

        var accessToken = minecraft.GetProperty("access_token").GetString()!;

        // 4. 取档案（名字 / UUID / 皮肤）
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await Http.SendAsync(request, cancellationToken);
        var profileText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 403 &&
                profileText.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "微软账号已授权，但这个 Azure 应用还没有 Minecraft API 权限（Invalid app registration）。" +
                    Environment.NewLine +
                    "请到 https://aka.ms/mce-reviewappid 提交 clientId 申请，审核通过后再登录；" +
                    "也可以先用离线账号。");
            }

            throw new InvalidOperationException("该账号没有 Minecraft Java 版（或档案不可用）。");
        }

        using var profileDoc = JsonDocument.Parse(profileText);
        var profile = profileDoc.RootElement;

        string? skinUrl = null;
        if (profile.TryGetProperty("skins", out var skins) && skins.GetArrayLength() > 0)
            skinUrl = skins[0].TryGetProperty("url", out var url) ? url.GetString() : null;

        return new MicrosoftProfile(
            profile.GetProperty("name").GetString()!,
            profile.GetProperty("id").GetString()!,
            accessToken,
            skinUrl);
    }

    private static async Task<JsonElement> PostJsonAsync(string url, object body,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,
            "application/json");
        using var response = await Http.PostAsync(url, content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new MicrosoftAuthException((int)response.StatusCode, ReadError(text));

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string ReadError(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.TryGetProperty("error_description", out var description))
                return description.GetString() ?? text;

            if (root.TryGetProperty("error", out var error))
                return error.GetString() ?? text;

            if (root.TryGetProperty("XErr", out var xerr))
                return $"Xbox 错误 {xerr}";
        }
        catch
        {
            // 原样返回
        }

        return text;
    }
}

public sealed class MicrosoftAuthException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
