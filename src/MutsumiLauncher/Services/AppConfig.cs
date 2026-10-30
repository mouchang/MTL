using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

/// <summary>启动器配置（%LOCALAPPDATA%\Mutsumi Launcher\config.json，不可写时退回 data\）。</summary>
public sealed class AppConfig
{
    public string PlayerName { get; set; } = string.Empty;

    public string SelectedVersion { get; set; } = string.Empty;

    public string JavaPath { get; set; } = string.Empty;

    /// <summary>手动配置时使用的内存（MB）；是否生效由 <see cref="MemoryAuto"/> 决定。</summary>
    public int MemoryMb { get; set; } = 4096;

    /// <summary>内存是否由「自动配置」决定（按物理内存的一半）。</summary>
    public bool MemoryAuto { get; set; } = true;

    /// <summary>实际分配的内存量（MB）：自动模式取 <see cref="SystemMemory.AutoMb"/>。</summary>
    [JsonIgnore]
    public int EffectiveMemoryMb => MemoryAuto || MemoryMb <= 0 ? SystemMemory.AutoMb : MemoryMb;

    public bool VersionIsolation { get; set; } = true;

    public List<Account> Accounts { get; set; } = [];

    public string SelectedAccount { get; set; } = string.Empty;

    /// <summary>
    /// 微软登录用的 clientId（也可用环境变量 MTL_MS_CLIENT_ID）。
    /// 默认是 MTL 在 Azure 上注册、已通过 Mojang AppID 审核的应用；需要换成自己的应用时改这里。
    /// </summary>
    public string MicrosoftClientId { get; set; } = "15263b9d-c342-48cb-8a6e-8c6ca4bfb433";

    /// <summary>Modrinth Personal Access Token（也可用环境变量 MTL_MODRINTH_TOKEN）。</summary>
    public string ModrinthToken { get; set; } = string.Empty;

    /// <summary>
    /// Modrinth API 反代基址（默认走 mtl-modrinth-api Worker，末尾不带 /v2）。
    /// 留空则直连 api.modrinth.com。
    /// </summary>
    public string ModrinthProxy { get; set; } = "https://mtl.dpdns.org/md";

    /// <summary>
    /// CurseForge 反代地址（默认走 mtl-curseforge Worker）。
    /// 官方 Key 存在 Cloudflare 的 secret 里，客户端拿不到；留空则直接用社区镜像。
    /// </summary>
    public string CurseForgeProxy { get; set; } = "https://mtl.dpdns.org/cf/v1";

    /// <summary>游戏目录（版本 / 库 / 资源）。留空 = 安装目录下的 game\。</summary>
    public string GameRoot { get; set; } = string.Empty;

    /// <summary>更新检查仓库（owner/repo）。</summary>
    public string UpdateRepo { get; set; } = "mouchang/MTL";

    /// <summary>可选的更新代理（Cloudflare Worker），为空时直连 GitHub API。</summary>
    public string UpdateProxy { get; set; } = "https://releases.mtl.dpdns.org/gh";

    /// <summary>原版文件下载源：auto（默认，测速择优）/ mirror（BMCLAPI）/ official。</summary>
    public string VanillaMirror { get; set; } = "auto";

    /// <summary>版本清单（正式版/预览版列表）下载源，与文件下载分开设置。</summary>
    public string VanillaListMirror { get; set; } = "auto";

    /// <summary>社区下载源：mcim（默认，国内加速）或 official。</summary>
    public string ModMirror { get; set; } = "auto";

    /// <summary>下载限速（KB/s），0 表示不限速。</summary>
    public int MaxDownloadSpeedKbps { get; set; }

    /// <summary>主题：light（默认）/ dark / custom。</summary>
    public string ThemeMode { get; set; } = "light";

    /// <summary>自定义主题的主色（#AARRGGBB）。</summary>
    public string ThemeAccent { get; set; } = ThemeService.DefaultAccent;

    /// <summary>自定义主题的底色：light / dark。</summary>
    public string ThemeBase { get; set; } = "light";

    /// <summary>内置代理（自带 mihomo 内核）。默认开启，只控制功能开关，不控制内核启停。</summary>
    public bool BuiltInProxyEnabled { get; set; } = true;

    /// <summary>内置代理优先使用的节点（按名称包含匹配），找不到时按顺序试其余真实节点。</summary>
    public string BuiltInProxyNode { get; set; } = "美国|BGP②|X0.2";

    /// <summary>设备标识，首次运行生成，用于订阅网关白名单。</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>是否已经做过首次启动的代理自检（只做一次）。</summary>
    public bool ProxySelfTestDone { get; set; }

    /// <summary>首次启动的公告页（代理内核提示 + 免责申明）是否已经看过。</summary>
    public bool AnnouncementShown { get; set; }

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
