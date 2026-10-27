using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text;

namespace MutsumiLauncher.Services;

public sealed class Account
{
    public string Name { get; set; } = string.Empty;

    /// <summary>offline 或 microsoft。</summary>
    public string Type { get; set; } = "offline";

    public string Uuid { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    /// <summary>微软刷新令牌（正版账号才有），用于免密续期。</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>游戏令牌过期时间（ISO 8601）。</summary>
    public string ExpiresAt { get; set; } = string.Empty;

    /// <summary>Xbox 用户 ID（正版账号启动参数里的 auth_xuid）。</summary>
    public string Xuid { get; set; } = string.Empty;

    /// <summary>缓存的皮肤文件（用于显示正脸）。</summary>
    public string SkinPath { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTimeOffset? Expiry =>
        DateTimeOffset.TryParse(ExpiresAt, out var value) ? value : null;

    [JsonIgnore]
    public bool IsMicrosoft => Type.Equals("microsoft", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string TypeLabel => IsMicrosoft ? "正版" : "离线";
}

public static class AccountService
{
    public static IReadOnlyList<Account> All => AppConfig.Current.Accounts;

    public static Account? Current
    {
        get
        {
            var config = AppConfig.Current;
            return config.Accounts.FirstOrDefault(a => a.Name == config.SelectedAccount)
                   ?? config.Accounts.FirstOrDefault();
        }
    }

    /// <summary>保证至少有一个账号（用旧的 PlayerName 迁移）。</summary>
    public static Account EnsureDefault()
    {
        var config = AppConfig.Current;
        if (config.Accounts.Count == 0)
        {
            var name = string.IsNullOrWhiteSpace(config.PlayerName) ? "Player" : config.PlayerName;
            config.Accounts.Add(new Account
            {
                Name = name,
                Type = "offline",
                Uuid = OfflineUuid(name),
            });
            config.SelectedAccount = name;
            config.PlayerName = name;
            config.Save();
        }

        return Current!;
    }

    public static Account AddOffline(string name)
    {
        var config = AppConfig.Current;
        var existing = config.Accounts.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            config.SelectedAccount = existing.Name;
            config.Save();
            return existing;
        }

        var account = new Account
        {
            Name = name,
            Type = "offline",
            Uuid = OfflineUuid(name),
        };

        config.Accounts.Add(account);
        config.SelectedAccount = name;
        config.PlayerName = name;
        config.Save();
        return account;
    }

    public static Account AddMicrosoft(string name, string uuid, string accessToken,
        string refreshToken, DateTimeOffset expiresAt, string xuid, string? skinPath)
    {
        var config = AppConfig.Current;
        var account = config.Accounts.FirstOrDefault(a => a.Uuid == uuid);

        if (account is null)
        {
            account = new Account { Name = name, Type = "microsoft", Uuid = uuid };
            config.Accounts.Add(account);
        }
        else
        {
            account.Name = name;
        }

        account.Type = "microsoft";
        account.AccessToken = accessToken;
        account.RefreshToken = refreshToken;
        account.ExpiresAt = expiresAt.ToString("O");
        account.Xuid = xuid;
        if (!string.IsNullOrWhiteSpace(skinPath)) account.SkinPath = skinPath;

        config.SelectedAccount = account.Name;
        config.PlayerName = account.Name;
        config.Save();
        return account;
    }

    /// <summary>游戏令牌快过期时用刷新令牌免密续期；失败就保持原样（启动时会提示重新登录）。</summary>
    public static async Task<bool> EnsureFreshAsync(Account account,
        CancellationToken cancellationToken = default)
    {
        if (!account.IsMicrosoft) return true;

        if (account.Expiry is { } expiry && expiry > DateTimeOffset.UtcNow.AddMinutes(5)) return true;
        if (string.IsNullOrWhiteSpace(account.RefreshToken)) return false;

        try
        {
            var msa = await MicrosoftAuth.RefreshAsync(AppConfig.Current.MicrosoftClientId,
                account.RefreshToken, cancellationToken);

            var profile = await MicrosoftAuth.LoginMinecraftAsync(msa.AccessToken, cancellationToken);

            account.AccessToken = profile.AccessToken;
            account.RefreshToken = msa.RefreshToken;
            account.ExpiresAt = profile.ExpiresAt.ToString("O");
            account.Uuid = profile.Uuid;
            account.Name = profile.Name;
            account.Xuid = profile.Xuid;

            AppConfig.Current.Save();
            AppLog.Info($"microsoft token refreshed for {account.Name}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "microsoft token refresh");
            return false;
        }
    }

    public static void Select(string name)
    {
        var config = AppConfig.Current;
        if (config.Accounts.All(a => a.Name != name)) return;

        config.SelectedAccount = name;
        config.PlayerName = name;
        config.Save();
    }

    public static void Remove(string name)
    {
        var config = AppConfig.Current;
        var account = config.Accounts.FirstOrDefault(a => a.Name == name);
        if (account is null) return;

        config.Accounts.Remove(account);
        if (config.SelectedAccount == name)
        {
            config.SelectedAccount = config.Accounts.FirstOrDefault()?.Name ?? string.Empty;
            config.PlayerName = config.SelectedAccount;
        }

        config.Save();
        EnsureDefault();
    }

    public static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
