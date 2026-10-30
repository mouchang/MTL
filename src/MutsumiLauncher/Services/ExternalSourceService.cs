using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>外部 .minecraft 的一个引用源。</summary>
public sealed class ExternalRoot
{
    public string Path { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>影子实例的标记文件内容（放在 &lt;VersionsDir&gt;\ref-&lt;name&gt;\.mtl-ref.json）。</summary>
internal sealed class ReferenceMarker
{
    public string Root { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>为了解析 inheritsFrom，我们自己补进来的父版本 json / jar 链接（断开时清理）。</summary>
    public List<string> CreatedAncestors { get; set; } = [];
}

/// <summary>
/// 引用外部 .minecraft：不复制游戏文件，只在我们的 versions 里建一个「影子实例」，
/// mods / config / saves 等目录用 junction 指回原位置，日志和 options 落在我们这边。
///
/// 断开引用时只删影子实例（连同我们补的父版本 json），原目录一个字节都不动。
/// </summary>
public static class ExternalSourceService
{
    private const string MarkerName = ".mtl-ref.json";
    private const string LinkPrefix = "ref-";

    public static bool IsReference(string instanceName) =>
        instanceName.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(GamePaths.VersionsDir, instanceName, MarkerName));

    /// <summary>引用实例显示成「版本名（引用）」。</summary>
    public static string DisplayName(string instanceName)
    {
        if (!IsReference(instanceName)) return instanceName;

        try
        {
            var marker = ReadMarker(Path.Combine(GamePaths.VersionsDir, instanceName));
            if (!string.IsNullOrWhiteSpace(marker?.Version)) return marker!.Version + "（引用）";
        }
        catch
        {
            // 读不到就退回目录名
        }

        return instanceName[LinkPrefix.Length..] + "（引用）";
    }

    /// <summary>探测目录像不像一个 .minecraft（有 versions 或 libraries）。</summary>
    public static bool LooksLikeMinecraft(string path)
    {
        try
        {
            return Directory.Exists(Path.Combine(path, "versions"))
                   || Directory.Exists(Path.Combine(path, "libraries"))
                   || Directory.Exists(Path.Combine(path, "assets"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>这个外部目录里可引用的版本（有 &lt;id&gt;.json 的目录）。</summary>
    public static IReadOnlyList<string> ListVersions(string root)
    {
        try
        {
            var versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions)) return [];

            return Directory.EnumerateDirectories(versions)
                .Where(dir => File.Exists(Path.Combine(dir, Path.GetFileName(dir) + ".json")))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderByDescending(name => name, NaturalOrder.Instance)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "list external versions");
            return [];
        }
    }

    /// <summary>登记一个引用源（写进配置）。</summary>
    public static void AddRoot(string path)
    {
        var config = AppConfig.Current;
        var full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');

        if (config.ExternalRoots.Any(root =>
                string.Equals(System.IO.Path.GetFullPath(root.Path).TrimEnd('\\', '/'), full,
                    StringComparison.OrdinalIgnoreCase)))
            return;

        config.ExternalRoots.Add(new ExternalRoot { Path = full });
        config.Save();
    }

    public static void RemoveRoot(string path)
    {
        var config = AppConfig.Current;
        var full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');

        config.ExternalRoots.RemoveAll(root =>
            string.Equals(System.IO.Path.GetFullPath(root.Path).TrimEnd('\\', '/'), full,
                StringComparison.OrdinalIgnoreCase));

        config.Save();
    }

    /// <summary>
    /// 为外部版本创建影子实例。返回实例名（ref-xxx）；失败返回 null 并给出原因。
    /// </summary>
    public static string? CreateShadow(string root, string version, out string? error)
    {
        error = null;

        try
        {
            var source = System.IO.Path.Combine(root, "versions", version);
            var sourceJson = System.IO.Path.Combine(source, version + ".json");

            if (!File.Exists(sourceJson))
            {
                error = "这个版本没有版本 JSON，无法引用。";
                return null;
            }

            var instance = LinkPrefix + Sanitize(version);
            var dir = System.IO.Path.Combine(GamePaths.VersionsDir, instance);

            if (Directory.Exists(dir))
            {
                error = "同名引用实例已存在。";
                return null;
            }

            Directory.CreateDirectory(dir);

            // 1. 版本 JSON：复制一份，名字换成实例名，内部 id 保持外部版本 id
            var json = File.ReadAllText(sourceJson);
            var document = JsonDocument.Parse(json);
            File.WriteAllText(System.IO.Path.Combine(dir, instance + ".json"), json);

            // 2. 补父版本（inheritsFrom）：本地没有的话，json 复制、jar 建链接
            var ancestors = new List<string>();
            var parent = document.RootElement.TryGetProperty("inheritsFrom", out var inherits)
                ? inherits.GetString()
                : null;

            while (!string.IsNullOrWhiteSpace(parent))
            {
                var localDir = System.IO.Path.Combine(GamePaths.VersionsDir, parent);

                if (!Directory.Exists(localDir))
                {
                    var parentSource = System.IO.Path.Combine(root, "versions", parent);
                    var parentJson = System.IO.Path.Combine(parentSource, parent + ".json");

                    if (!File.Exists(parentJson))
                    {
                        error = $"缺少父版本 {parent}，无法引用。";
                        Directory.Delete(dir, recursive: true);
                        return null;
                    }

                    Directory.CreateDirectory(localDir);
                    File.Copy(parentJson, System.IO.Path.Combine(localDir, parent + ".json"), overwrite: false);

                    var parentJar = System.IO.Path.Combine(parentSource, parent + ".jar");
                    if (File.Exists(parentJar))
                        Link(System.IO.Path.Combine(localDir, parent + ".jar"), parentJar);

                    ancestors.Add(parent);
                }

                parent = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(
                    System.IO.Path.Combine(root, "versions", parent), parent + ".json")))
                    .RootElement.TryGetProperty("inheritsFrom", out var next) ? next.GetString() : null;
            }

            // 3. mods / config / saves 等：建 junction 指回原目录（只链接存在的）
            foreach (var name in new[] { "mods", "config", "saves", "resourcepacks", "shaderpacks" })
            {
                var from = System.IO.Path.Combine(source, name);
                if (Directory.Exists(from)) Link(System.IO.Path.Combine(dir, name), from);
            }

            // 4. 标记
            var marker = new ReferenceMarker
            {
                Root = root,
                Version = version,
                CreatedAncestors = ancestors,
            };

            File.WriteAllText(System.IO.Path.Combine(dir, MarkerName),
                JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));

            AppLog.Info($"reference instance created: {instance} -> {source}");
            return instance;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "create reference instance");
            error = ex.Message;
            return null;
        }
    }

    /// <summary>断开引用：删影子实例（只删链接和我们补的父版本 json），原目录不动。</summary>
    public static bool Disconnect(string instance, out string? error)
    {
        error = null;

        try
        {
            var dir = System.IO.Path.Combine(GamePaths.VersionsDir, instance);
            if (!Directory.Exists(dir))
            {
                error = "找不到这个引用实例。";
                return false;
            }

            var marker = ReadMarker(dir);

            // 先把 junction 拆掉：直接递归删除会跟着进去把原文件删了
            foreach (var name in new[] { "mods", "config", "saves", "resourcepacks", "shaderpacks" })
            {
                var link = System.IO.Path.Combine(dir, name);
                if (Directory.Exists(link)) Unlink(link);
            }

            var versionJar = System.IO.Path.Combine(dir, instance + ".jar");
            if (File.Exists(versionJar)) File.Delete(versionJar);

            Directory.Delete(dir, recursive: true);

            // 清理我们补进来的父版本（json 是我们复制的；jar 是链接）
            foreach (var ancestor in marker?.CreatedAncestors ?? [])
            {
                var ancestorDir = System.IO.Path.Combine(GamePaths.VersionsDir, ancestor);
                if (!Directory.Exists(ancestorDir)) continue;

                // 还有别的实例依赖它就不删
                var used = Directory.EnumerateDirectories(GamePaths.VersionsDir)
                    .Any(other => File.ReadAllText(System.IO.Path.Combine(other, System.IO.Path.GetFileName(other) + ".json"))
                        .Contains($"\"{ancestor}\"", StringComparison.Ordinal));

                if (used) continue;

                try
                {
                    var jar = System.IO.Path.Combine(ancestorDir, ancestor + ".jar");
                    if (File.Exists(jar)) File.Delete(jar);

                    Directory.Delete(ancestorDir, recursive: true);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "clean ancestor " + ancestor);
                }
            }

            AppLog.Info($"reference instance removed: {instance}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "disconnect reference");
            error = ex.Message;
            return false;
        }
    }

    private static ReferenceMarker? ReadMarker(string dir)
    {
        try
        {
            var path = System.IO.Path.Combine(dir, MarkerName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ReferenceMarker>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>建目录联接（junction），不需要管理员权限，删链接不会影响目标。</summary>
    private static void Link(string link, string target)
    {
        try
        {
            if (Directory.Exists(link) || File.Exists(link)) return;

            var info = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("mklink");
            info.ArgumentList.Add("/J");
            info.ArgumentList.Add(link);
            info.ArgumentList.Add(target);

            using var process = Process.Start(info);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "create junction " + link);
        }
    }

    private static void Unlink(string link)
    {
        try
        {
            var info = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("rmdir");
            info.ArgumentList.Add(link);

            using var process = Process.Start(info);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "remove junction " + link);
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private sealed class NaturalOrder : IComparer<string>
    {
        public static readonly NaturalOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x is null || y is null) return string.Compare(x, y, StringComparison.Ordinal);

            var a = x.Split('.', '-', '_');
            var b = y.Split('.', '-', '_');

            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var left = int.TryParse(a[i], out var na);
                var right = int.TryParse(b[i], out var nb);

                var compare = left && right
                    ? na.CompareTo(nb)
                    : string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);

                if (compare != 0) return -compare;
            }

            return -string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }
}
