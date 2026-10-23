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

    /// <summary>缓存的皮肤文件（用于显示正脸）。</summary>
    public string SkinPath { get; set; } = string.Empty;

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

    public static Account AddMicrosoft(string name, string uuid, string accessToken, string? skinPath)
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
        if (!string.IsNullOrWhiteSpace(skinPath)) account.SkinPath = skinPath;

        config.SelectedAccount = account.Name;
        config.PlayerName = account.Name;
        config.Save();
        return account;
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
