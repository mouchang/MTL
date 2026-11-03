using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MutsumiLauncher.Services;

/// <summary>
/// 检查更新结果的持久化缓存。
///
/// 之前没存：每次刷新列表（刷新按钮、切页、重开实例）都会把 Update 丢掉，
/// 于是「可更新」徽标消失、还得重新检查一遍。PCL2 是把每个文件的
/// ProjectVersion / UpdateFile / ChangelogUrls 一起写进 JSON 缓存的，
/// 这里按同样的思路做，但只存「可更新」这一条结果。
/// </summary>
public static class ModUpdateCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>缓存的更新信息。</summary>
    public sealed record Entry(
        string VersionId,
        string Name,
        string VersionNumber,
        DateTimeOffset DatePublished,
        string? DownloadUrl,
        string FileName,
        long Size,
        string? Sha1,
        string? ChangelogUrl);

    private static string FileFor(string version) => Path.Combine(
        AppPaths.CacheDir, "updates", SafeName(version) + ".json");

    /// <summary>读取某个实例的缓存；文件损坏或不存在都返回空表。</summary>
    public static Dictionary<string, Entry> Load(string version)
    {
        var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var path = FileFor(version);
            if (!File.Exists(path)) return result;

            var text = File.ReadAllText(path);
            var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(text, JsonOptions);

            if (map is null) return result;

            foreach (var (key, entry) in map) result[key] = entry;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load update cache");
        }

        return result;
    }

    public static void Save(string version, Dictionary<string, Entry> entries)
    {
        try
        {
            var path = FileFor(version);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "save update cache");
        }
    }

    /// <summary>
    /// 缓存键用文件名。不能用「文件名 + 大小」——列表改成异步补大小时，
    /// 渲染那一刻还拿不到 size，键对不上就还原不了。
    /// 同一个 mods 目录里文件名本来就唯一。
    /// </summary>
    public static string KeyOf(string fileName) => fileName.ToLowerInvariant();

    private static string SafeName(string text)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            text = text.Replace(ch, '_');

        return text;
    }
}