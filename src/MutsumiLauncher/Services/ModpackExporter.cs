using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MutsumiLauncher.Services;

/// <summary>把实例导出成 .mrpack（Modrinth 整合包）或普通 zip。</summary>
public static class ModpackExporter
{
    public sealed record ExportOptions(
        string Instance,
        string Name,
        string Version,
        bool IncludeMods,
        bool IncludeConfig,
        bool IncludeOptions,
        bool IncludeResourcePacks,
        bool IncludeShaderPacks,
        bool IncludeSaves);

    /// <summary>导出，返回生成的文件路径。</summary>
    public static async Task<string> ExportAsync(ExportOptions options, string targetPath,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var instanceDir = InstanceService.DirFor(options.Instance);
        if (!Directory.Exists(instanceDir))
            throw new InvalidOperationException($"实例「{options.Instance}」不存在。");

        var info = InstanceService.GetInfo(options.Instance);
        var gameDir = InstanceService.GameDirFor(options.Instance);

        var tempDir = Path.Combine(GamePaths.Root, "cache",
            "export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var overrides = Path.Combine(tempDir, "overrides");
            Directory.CreateDirectory(overrides);

            progress?.Report("复制文件…");

            if (options.IncludeMods)
                CopyRelative(Path.Combine(gameDir, "mods"), Path.Combine(overrides, "mods"));

            if (options.IncludeConfig)
                CopyRelative(Path.Combine(gameDir, "config"), Path.Combine(overrides, "config"));

            if (options.IncludeOptions)
            {
                var optionsFile = Path.Combine(gameDir, "options.txt");
                if (File.Exists(optionsFile))
                    File.Copy(optionsFile, Path.Combine(overrides, "options.txt"), overwrite: true);
            }

            if (options.IncludeResourcePacks)
                CopyRelative(Path.Combine(gameDir, "resourcepacks"),
                    Path.Combine(overrides, "resourcepacks"));

            if (options.IncludeShaderPacks)
                CopyRelative(Path.Combine(gameDir, "shaderpacks"),
                    Path.Combine(overrides, "shaderpacks"));

            if (options.IncludeSaves)
                CopyRelative(Path.Combine(gameDir, "saves"), Path.Combine(overrides, "saves"));

            var isMrpack = targetPath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase);

            if (isMrpack)
            {
                progress?.Report("生成 modrinth.index.json…");
                await File.WriteAllTextAsync(Path.Combine(tempDir, "modrinth.index.json"),
                    BuildIndex(options, info), cancellationToken);
            }

            progress?.Report("打包…");
            if (File.Exists(targetPath)) File.Delete(targetPath);
            ZipFile.CreateFromDirectory(tempDir, targetPath, CompressionLevel.Optimal, false);

            progress?.Report("完成");
            AppLog.Info($"export {(isMrpack ? "mrpack" : "zip")}: {targetPath}");
            return targetPath;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // 清理失败忽略
            }
        }
    }

    private static string BuildIndex(ExportOptions options, InstanceInfo info)
    {
        var dependencies = new JsonObject
        {
            ["minecraft"] = info.InheritsFrom ?? info.VersionId,
        };

        switch (info.Loader)
        {
            case "Fabric" when TryParseFabric(options.Instance, out var loaderVersion):
                dependencies["fabric-loader"] = loaderVersion;
                break;

            case "Forge" when TryParseForge(options.Instance, out var forgeVersion):
                dependencies["forge"] = forgeVersion;
                break;

            case "NeoForge" when TryParseNeoForge(options.Instance, out var neoVersion):
                dependencies["neoforge"] = neoVersion;
                break;
        }

        var index = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["versionId"] = options.Version,
            ["name"] = options.Name,
            ["summary"] = "由 Mutsumi Launcher 导出",
            ["files"] = new JsonArray(),
            ["dependencies"] = dependencies,
        };

        return index.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool TryParseFabric(string instance, out string version)
    {
        version = string.Empty;
        if (!instance.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase)) return false;

        var rest = instance["fabric-loader-".Length..];
        var dash = rest.IndexOf('-');
        if (dash <= 0) return false;

        version = rest[..dash];
        return true;
    }

    private static bool TryParseForge(string instance, out string version)
    {
        version = string.Empty;
        if (!instance.StartsWith("forge-", StringComparison.OrdinalIgnoreCase)) return false;

        var rest = instance["forge-".Length..];
        var dash = rest.IndexOf('-');
        if (dash <= 0) return false;

        version = rest[(dash + 1)..];
        return true;
    }

    private static bool TryParseNeoForge(string instance, out string version)
    {
        version = string.Empty;
        if (!instance.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)) return false;

        version = instance["neoforge-".Length..];
        return true;
    }

    private static void CopyRelative(string source, string target)
    {
        if (!Directory.Exists(source)) return;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
