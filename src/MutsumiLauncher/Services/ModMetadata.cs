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

    /// <summary>
    /// 从 jar 判断这个 Mod 属于哪些加载器（用于逐文件检查更新）。
    /// 判断依据是元数据文件本身：fabric.mod.json → fabric、quilt.mod.json → quilt、
    /// META-INF/neoforge.mods.toml → neoforge、META-INF/mods.toml → forge。
    /// </summary>
    public static IReadOnlyList<string> LoadersOf(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            if (archive.GetEntry("fabric.mod.json") is not null) return ["fabric"];
            if (archive.GetEntry("quilt.mod.json") is not null) return ["quilt"];
            if (archive.GetEntry("META-INF/neoforge.mods.toml") is not null) return ["neoforge"];

            if (archive.GetEntry("META-INF/mods.toml") is not null)
            {
                // 1.20.1 的 NeoForge 也用 mods.toml，靠 loaderVersion 里的版本号区分
                using var stream = archive.GetEntry("META-INF/mods.toml")!.Open();
                using var reader = new StreamReader(stream);

                var text = reader.ReadToEnd();

                if (text.Contains("javafml", StringComparison.OrdinalIgnoreCase)
                    && text.Contains("loaderVersion=\"[", StringComparison.Ordinal))
                    return ["forge"];

                return ["forge"];
            }
        }
        catch
        {
            // 读不了就当未知
        }

        return [];
    }

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
                // 不少 Mod 的 fabric.mod.json 里字符串带裸换行（description 最常见），
                // 严格 JSON 解析会直接抛异常——先试严格，失败再用正则捞。
                try
                {
                    using var stream = fabric.Open();
                    using var document = JsonDocument.Parse(stream);
                    var root = document.RootElement;

                    var name = Text(root, "name") ?? Text(root, "id");

                    if (!string.IsNullOrWhiteSpace(name))
                        return new Info(name!, Text(root, "version"), Text(root, "id"));
                }
                catch (JsonException)
                {
                    // 落到下面的宽松解析
                }

                if (ReadLenient(archive) is { } fromFabric) return fromFabric;
            }

            var quilt = archive.GetEntry("quilt.mod.json");
            if (quilt is not null)
            {
                try
                {
                    using var stream = quilt.Open();
                    using var document = JsonDocument.Parse(stream);

                    if (document.RootElement.TryGetProperty("quilt_loader", out var loader)
                        && loader.TryGetProperty("metadata", out var metadata))
                    {
                        var name = Text(metadata, "name") ?? Text(loader, "id");

                        if (!string.IsNullOrWhiteSpace(name))
                            return new Info(name!, Text(loader, "version"), Text(loader, "id"));
                    }
                }
                catch (JsonException)
                {
                    // 继续用宽松解析
                }

                if (ReadLenient(archive) is { } fromQuilt) return fromQuilt;
            }

            // 到这一步还没有结果：试着用正则从 fabric/quilt 元数据里捞
            if (ReadLenient(archive) is { } lenient) return lenient;

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
    /// <summary>
    /// 宽松读取：部分 Mod 的 fabric.mod.json 带裸换行或注释，严格 JSON 解析会失败，
    /// 这时用正则捞 name / version，总比显示文件名强。
    /// </summary>
    private static Info? ReadLenient(ZipArchive archive)
    {
        foreach (var name in new[] { "fabric.mod.json", "quilt.mod.json" })
        {
            var entry = archive.GetEntry(name);
            if (entry is null) continue;

            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();

                var nameMatch = Regex.Match(text, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                var versionMatch = Regex.Match(text, "\"version\"\\s*:\\s*\"([^\"]+)\"");

                if (!nameMatch.Success) continue;

                return new Info(nameMatch.Groups[1].Value, versionMatch.Success
                    ? versionMatch.Groups[1].Value
                    : null, null);
            }
            catch
            {
                // 继续试下一个
            }
        }

        return null;
    }

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