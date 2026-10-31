using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MutsumiLauncher.Services;

/// <summary>
/// 从 jar 里读 Mod 元数据：Fabric 的 fabric.mod.json、Forge/NeoForge 的 mods.toml、
/// Quilt 的 quilt.mod.json。拿不到就返回 null，调用方退回文件名。
/// </summary>
public static class ModMetadata
{
    public sealed record Info(string Name, string? Version, string? ModId);

    private static readonly Regex TomlDisplayName =
        new("^\\s*displayName\\s*=\\s*[\"'](.+?)[\"']", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex TomlModId =
        new("^\\s*modId\\s*=\\s*[\"'](.+?)[\"']", RegexOptions.Multiline | RegexOptions.Compiled);

    public static Info? Read(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            var legacy = archive.GetEntry("mcmod.info");
            if (legacy is not null)
            {
                using var stream = legacy.Open();
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;

                // mcmod.info 可能是数组，也可能是 { "modList": [...] }
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var first = root[0];

                    return new Info(
                        Text(first, "name") ?? Text(first, "modid") ?? string.Empty,
                        Text(first, "version"),
                        Text(first, "modid"));
                }

                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("modList", out var list)
                    && list.ValueKind == JsonValueKind.Array
                    && list.GetArrayLength() > 0)
                {
                    var first = list[0];

                    return new Info(
                        Text(first, "name") ?? Text(first, "modid") ?? string.Empty,
                        Text(first, "version"),
                        Text(first, "modid"));
                }
            }

            var fabric = archive.GetEntry("fabric.mod.json");
            if (fabric is not null)
            {
                using var stream = fabric.Open();
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;

                return new Info(
                    Text(root, "name") ?? Text(root, "id") ?? string.Empty,
                    Text(root, "version"),
                    Text(root, "id"));
            }

            var quilt = archive.GetEntry("quilt.mod.json");
            if (quilt is not null)
            {
                using var stream = quilt.Open();
                using var document = JsonDocument.Parse(stream);

                if (document.RootElement.TryGetProperty("quilt_loader", out var loader)
                    && loader.TryGetProperty("metadata", out var metadata))
                {
                    return new Info(
                        Text(metadata, "name") ?? Text(loader, "id") ?? string.Empty,
                        Text(loader, "version"),
                        Text(loader, "id"));
                }
            }

            foreach (var tomlName in new[] { "META-INF/neoforge.mods.toml", "META-INF/mods.toml" })
            {
                var toml = archive.GetEntry(tomlName);
                if (toml is null) continue;

                using var stream = toml.Open();
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();

                var display = TomlDisplayName.Match(text);
                var id = TomlModId.Match(text);

                if (display.Success)
                    return new Info(display.Groups[1].Value.Trim(), null,
                        id.Success ? id.Groups[1].Value : null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "read mod metadata " + Path.GetFileName(jarPath));
        }

        return null;
    }

    /// <summary>界面显示用：优先 MC百科中文名，其次 jar 里的名称，最后文件名。</summary>
    public static string DisplayNameFor(string jarPath, string fallback)
    {
        var info = Read(jarPath);
        var english = info?.Name;

        if (!string.IsNullOrWhiteSpace(english))
        {
            var chinese = McmodIndex.ChineseFor(null, english);
            if (!string.IsNullOrWhiteSpace(chinese)) return chinese;

            return english!;
        }

        // 没有元数据：用文件名去掉版本号后缀，至少别显示一长串
        return fallback;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}