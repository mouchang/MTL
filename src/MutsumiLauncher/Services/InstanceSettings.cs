using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

/// <summary>单个实例（版本）的启动设置。null 表示跟随全局设置。</summary>
public sealed class InstanceSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [JsonPropertyName("javaPath")]
    public string? JavaPath { get; set; }

    [JsonPropertyName("memoryMb")]
    public int MemoryMb { get; set; }

    [JsonPropertyName("versionIsolation")]
    public bool? VersionIsolation { get; set; }

    public static string Dir => Path.Combine(GamePaths.Root, "instances");

    public static string FileFor(string version)
        => Path.Combine(Dir, SafeName(version) + ".json");

    public string EffectiveJavaPath
        => JavaPath ?? AppConfig.Current.JavaPath;

    public int EffectiveMemoryMb
        => MemoryMb > 0 ? MemoryMb : AppConfig.Current.MemoryMb;

    public bool EffectiveIsolation
        => VersionIsolation ?? AppConfig.Current.VersionIsolation;

    public static InstanceSettings Load(string version)
    {
        try
        {
            var path = FileFor(version);
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<InstanceSettings>(File.ReadAllText(path));
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load instance settings");
        }

        return new InstanceSettings();
    }

    public void Save(string version)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FileFor(version), JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "save instance settings");
        }
    }

    public static void Delete(string version)
    {
        try
        {
            var path = FileFor(version);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete instance settings");
        }
    }

    public static void Rename(string oldName, string newName)
    {
        try
        {
            var from = FileFor(oldName);
            if (File.Exists(from)) File.Move(from, FileFor(newName), overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "rename instance settings");
        }
    }

    private static string SafeName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
