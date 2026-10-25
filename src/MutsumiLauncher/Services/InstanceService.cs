using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MutsumiLauncher.Services;

public sealed record InstanceInfo(
    string Name,
    string VersionId,
    string? InheritsFrom,
    string GameVersion,
    string Loader,
    string Path,
    long SizeBytes,
    int ModCount);

/// <summary>实例（versions/&lt;name&gt;）的读取、重命名、删除和文件夹打开。</summary>
public static class InstanceService
{
    public static string DirFor(string name) => Path.Combine(GamePaths.VersionsDir, name);

    /// <summary>版本隔离时游戏目录就是实例目录，否则是游戏根目录。</summary>
    public static string GameDirFor(string name)
        => InstanceSettings.Load(name).EffectiveIsolation ? DirFor(name) : GamePaths.Root;

    public static string ModsDirFor(string name) => Path.Combine(GameDirFor(name), "mods");

    public static InstanceInfo GetInfo(string name)
    {
        var dir = DirFor(name);
        var jsonPath = Path.Combine(dir, $"{name}.json");

        var versionId = name;
        string? inheritsFrom = null;
        string? baseVersion = null;
        var loader = "原版";

        try
        {
            if (File.Exists(jsonPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = document.RootElement;

                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    versionId = id.GetString() ?? name;

                if (root.TryGetProperty("inheritsFrom", out var inherits)
                    && inherits.ValueKind == JsonValueKind.String)
                    inheritsFrom = inherits.GetString();

                if (root.TryGetProperty("mtlBaseVersion", out var baseElement)
                    && baseElement.ValueKind == JsonValueKind.String)
                    baseVersion = baseElement.GetString();

                var mainClass = root.TryGetProperty("mainClass", out var main)
                    ? main.GetString() ?? string.Empty
                    : string.Empty;

                loader = DetectLoader(name, mainClass);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "read instance json");
        }

        return new InstanceInfo(name, versionId, inheritsFrom,
            baseVersion ?? inheritsFrom ?? versionId, loader, dir, DirectorySize(dir), CountMods(name));
    }

    private static string DetectLoader(string name, string mainClass)
    {
        if (mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
            return "Fabric";

        if (name.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)
            || mainClass.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            return "NeoForge";

        if (name.Contains("optifine", StringComparison.OrdinalIgnoreCase))
            return "OptiFine";

        if (name.Contains("forge", StringComparison.OrdinalIgnoreCase)
            || mainClass.Contains("forge", StringComparison.OrdinalIgnoreCase))
            return "Forge";

        return "原版";
    }

    private static long DirectorySize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;

        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });
        }
        catch
        {
            return 0;
        }
    }

    private static int CountMods(string name)
    {
        try
        {
            var dir = ModsDirFor(name);
            if (!Directory.Exists(dir)) return 0;

            return Directory.EnumerateFiles(dir)
                .Count(file => file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return 0;
        }
    }

    public static void Rename(string oldName, string newName)
    {
        var oldDir = DirFor(oldName);
        var newDir = DirFor(newName);

        if (!Directory.Exists(oldDir))
            throw new InvalidOperationException($"实例「{oldName}」不存在。");

        if (Directory.Exists(newDir) && Directory.EnumerateFileSystemEntries(newDir).Any())
            throw new InvalidOperationException($"实例「{newName}」已存在，换个名字吧。");

        Directory.Move(oldDir, newDir);

        foreach (var extension in new[] { ".json", ".jar" })
        {
            var from = Path.Combine(newDir, oldName + extension);
            if (File.Exists(from))
                File.Move(from, Path.Combine(newDir, newName + extension));
        }

        // 改写版本 JSON 里的 id，保证启动器与 Forge 系列一致
        var jsonPath = Path.Combine(newDir, newName + ".json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
                if (node is not null)
                {
                    node["id"] = newName;
                    File.WriteAllText(jsonPath, node.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "rewrite instance id");
            }
        }

        InstanceSettings.Rename(oldName, newName);

        var config = AppConfig.Current;
        if (config.SelectedVersion == oldName)
        {
            config.SelectedVersion = newName;
            config.Save();
        }

        AppLog.Info($"instance renamed: {oldName} -> {newName}");
    }

    public static void Delete(string name)
    {
        var dir = DirFor(name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        InstanceSettings.Delete(name);

        var config = AppConfig.Current;
        if (config.SelectedVersion == name)
        {
            config.SelectedVersion = GameLauncher.InstalledVersions().FirstOrDefault() ?? string.Empty;
            config.Save();
        }

        AppLog.Info($"instance deleted: {name}");
    }

    public static string? OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // 目录不存在 / 无权限时仍然尝试打开
        }

        // 1) cmd start：和打开网页同一套做法，最稳
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.SystemDirectory,
            })?.Dispose();

            AppLog.Info($"open folder via cmd: {path}");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open folder via cmd");
        }

        // 2) 直接拉 explorer
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true,
            })?.Dispose();

            AppLog.Info($"open folder via explorer: {path}");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open folder via explorer");
        }

        // 3) ShellExecute 目录
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            })?.Dispose();

            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open folder via shell");
            return ex.Message;
        }
    }

    public static string FormatSize(long bytes)
        => bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024.0 / 1024 / 1024:0.00} GB"
            : bytes >= 1024 * 1024
                ? $"{bytes / 1024.0 / 1024:0.0} MB"
                : $"{bytes / 1024.0:0} KB";
}
