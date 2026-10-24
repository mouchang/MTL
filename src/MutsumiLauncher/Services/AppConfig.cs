using System.IO;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>启动器配置（%LOCALAPPDATA%\Mutsumi Launcher\config.json，不可写时退回 data\）。</summary>
public sealed class AppConfig
{
    public string PlayerName { get; set; } = string.Empty;

    public string SelectedVersion { get; set; } = string.Empty;

    public string JavaPath { get; set; } = string.Empty;

    /// <summary>0 表示自动。</summary>
    public int MemoryMb { get; set; }

    public bool VersionIsolation { get; set; } = true;

    public string WindowTitle { get; set; } = string.Empty;

    public string CustomInfo { get; set; } = "MTL";

    public List<Account> Accounts { get; set; } = [];

    public string SelectedAccount { get; set; } = string.Empty;

    /// <summary>微软登录用的 Azure 应用 clientId（也可用环境变量 MTL_MS_CLIENT_ID）。</summary>
    public string MicrosoftClientId { get; set; } = string.Empty;

    /// <summary>Modrinth Personal Access Token（也可用环境变量 MTL_MODRINTH_TOKEN）。</summary>
    public string ModrinthToken { get; set; } = string.Empty;

    /// <summary>CurseForge API Key（也可用环境变量 MTL_CURSEFORGE_KEY）。</summary>
    public string CurseForgeApiKey { get; set; } = string.Empty;

    /// <summary>更新检查仓库（owner/repo）。</summary>
    public string UpdateRepo { get; set; } = "mouchang/MTL";

    /// <summary>可选的更新代理（Cloudflare Worker），为空时直连 GitHub API。</summary>
    public string UpdateProxy { get; set; } = "https://releases.mtl.dpdns.org/gh";

    /// <summary>原版下载源：bmclapi（默认，国内加速）或 official。</summary>
    public string VanillaMirror { get; set; } = "bmclapi";

    /// <summary>社区下载源：mcim（默认，国内加速）或 official。</summary>
    public string ModMirror { get; set; } = "mcim";

    /// <summary>网络代理：auto（默认，有可用系统代理就用）/ direct / system / custom。</summary>
    public string NetworkMode { get; set; } = "auto";

    /// <summary>用户是否手动选过代理模式；没选过时允许自动迁移到 auto。</summary>
    public bool NetworkModeChosen { get; set; }

    public string ProxyHost { get; set; } = "127.0.0.1";

    public int ProxyPort { get; set; } = 7890;


    private static readonly string ConfigPath = Path.Combine(AppPaths.DataRoot, "config.json");

    private static AppConfig? _current;

    public static AppConfig Current => _current ??= Load();

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config is not null)
                {
                    // 老配置默认是 direct，用户没手动选过就迁移到「自动」
                    if (!config.NetworkModeChosen
                        && config.NetworkMode.Equals("direct", StringComparison.OrdinalIgnoreCase))
                    {
                        config.NetworkMode = "auto";
                        config.Save();
                    }


                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load config");
        }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "save config");
        }
    }
}
