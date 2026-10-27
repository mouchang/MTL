using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record DeviceCodeInfo(string DeviceCode, string UserCode, string VerificationUri,
    int Interval, int ExpiresIn);

/// <summary>微软账号令牌（MSA）。</summary>
public sealed record MsaTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>Minecraft 档案 + 游戏令牌。</summary>
public sealed record MicrosoftProfile(string Name, string Uuid, string AccessToken,
    string? SkinUrl, string Xuid, DateTimeOffset ExpiresAt);

/// <summary>
/// 微软登录：设备码 → MSA → Xbox Live → XSTS → Minecraft 服务。
/// 支持两种 clientId：
/// - 留空 / 微软官方那串：走 login.live.com 的旧版设备码流程，开箱即用
/// - 自建 Azure 应用：走 login.microsoftonline.com 的 v2 流程（拿到过 Mojang AppID 审核的用这个）
/// </summary>
public static class MicrosoftAuth
{
    /// <summary>微软自家启动器的公开 clientId，旧版设备码流程可以直接用。</summary>
    public const string LegacyClientId = "00000000402b5328";

    private const string LegacyScope = "service::user.auth.xboxlive.com::MBI_SSL";

    private const string LegacyDeviceCodeUrl = "https://login.live.com/oauth20_connect.srf";
    private const string LegacyTokenUrl = "https://login.live.com/oauth20_token.srf";

    private const string V2Base = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    private const string V2Scope = "XboxLive.signin offline_access";

    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(30));

    private static bool IsLegacy(string? clientId) =>
        string.IsNullOrWhiteSpace(clientId); // 空 = 用内置的官方 clientId

    private static string EffectiveClientId(string? clientId) =>
        IsLegacy(clientId) ? LegacyClientId : clientId!.Trim();

    public static async Task<DeviceCodeInfo> StartAsync(string? clientId, CancellationToken cancellationToken)
    {
        var legacy = IsLegacy(clientId);

        using var content = new FormUrlEncodedContent(legacy
            ? new Dictionary<string, string>
            {
                ["client_id"] = EffectiveClientId(clientId),
                ["scope"] = LegacyScope,
                ["response_type"] = "device_code",
            }
            : new Dictionary<string, string>
            {
                ["client_id"] = EffectiveClientId(clientId),
                ["scope"] = V2Scope,
            });

        var url = legacy ? LegacyDeviceCodeUrl : V2Base + "/devicecode";
        using var response = await Http.PostAsync(url, content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"设备码请求失败：{ReadError(text)}");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        return new DeviceCodeInfo(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.TryGetProperty("verification_uri", out var uri) ? uri.GetString()!
                : root.TryGetProperty("verification_url", out var url2) ? url2.GetString()!
                : "https://www.microsoft.com/link",
            root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5,
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 900);
    }

    /// <summary>轮询直到用户在浏览器里完成授权。</summary>
    public static async Task<MsaTokens> WaitForTokenAsync(string? clientId, DeviceCodeInfo code,
        CancellationToken cancellationToken)
    {
        var legacy = IsLegacy(clientId);
        var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);
        var interval = Math.Max(2, code.Interval);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);

            using var content = new FormUrlEncodedContent(legacy
                ? new Dictionary<string, string>
                {
                    ["client_id"] = EffectiveClientId(clientId),
                    ["scope"] = LegacyScope,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["device_code"] = code.DeviceCode,
                }
                : new Dictionary<string, string>
                {
                    ["client_id"] = EffectiveClientId(clientId),
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["device_code"] = code.DeviceCode,
                });

            var url = legacy ? LegacyTokenUrl : V2Base + "/token";
            using var response = await Http.PostAsync(url, content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.TryGetProperty("access_token", out var accessToken))
                return ParseTokens(root, accessToken.GetString()!);

            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;

            if (error is "authorization_pending") continue;

            if (error is "slow_down")
            {
                interval += 5;
                continue;
            }

            throw new InvalidOperationException(error switch
            {
                "authorization_declined" or "access_denied" => "授权被取消。",
                "expired_token" or "code_expired" => "设备码已过期，请重新登录。",
                _ => $"登录失败：{ReadError(text)}",
            });
        }

        throw new TimeoutException("设备码已过期，请重新登录。");
    }

    /// <summary>用 refresh token 换新的 MSA 令牌（令牌 24 小时过期，刷新时不需要用户操作）。</summary>
    public static async Task<MsaTokens> RefreshAsync(string? clientId, string refreshToken,
        CancellationToken cancellationToken)
    {
        var legacy = IsLegacy(clientId);

        using var content = new FormUrlEncodedContent(legacy
            ? new Dictionary<string, string>
            {
                ["client_id"] = EffectiveClientId(clientId),
                ["scope"] = LegacyScope,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }
            : new Dictionary<string, string>
            {
                ["client_id"] = EffectiveClientId(clientId),
                ["scope"] = V2Scope,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            });

        var url = legacy ? LegacyTokenUrl : V2Base + "/token";
        using var response = await Http.PostAsync(url, content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new MicrosoftAuthException((int)response.StatusCode, ReadError(text));

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;

        return ParseTokens(root, accessToken);
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
        var xuid = xsts.GetProperty("DisplayClaims").GetProperty("xui")[0]
            .TryGetProperty("xid", out var xid) ? xid.GetString() ?? "0" : "0";

        // 3. 换 Minecraft accessToken
        var minecraft = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" }, cancellationToken);

        var accessToken = minecraft.GetProperty("access_token").GetString()!;
        var expiresIn = minecraft.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 86400;

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
            skinUrl,
            xuid,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static MsaTokens ParseTokens(JsonElement root, string accessToken)
    {
        var refresh = root.TryGetProperty("refresh_token", out var value) ? value.GetString() ?? "" : "";
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        return new MsaTokens(accessToken, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
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
