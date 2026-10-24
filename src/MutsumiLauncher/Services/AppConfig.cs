using System.IO;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>启动器配置（%LOCALAPPDATA%\Mutsumi Launcher\config.json，不可写时退回 data\）。</summary>
public sealed class AppConfig
{
    public string PlayerName { get; set; } = "the_box_mc";

    public string SelectedVersion { get; set; } = string.Empty;

    public string JavaPath { get; set; } = string.Empty;

    /// <summary>0 表示自动。</summary>
    public int MemoryMb { get; set; }

    public bool VersionIsolation { get; set; } = true;

    public string WindowTitle { get; set; } = string.Empty;

    public string CustomInfo { get; set; } = "PCL";

    public List<Account> Accounts { get; set; } = [];

    public string SelectedAccount { get; set; } = string.Empty;

    /// <summary>微软登录用的 Azure 应用 clientId（也可用环境变量 MTL_MS_CLIENT_ID）。</summary>
    public string MicrosoftClientId { get; set; } = string.Empty;

    /// <summary>Modrinth Personal Access Token（也可用环境变量 MTL_MODRINTH_TOKEN）。</summary>
    public string ModrinthToken { get; set; } = string.Empty;


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
                if (config is not null) return config;
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
