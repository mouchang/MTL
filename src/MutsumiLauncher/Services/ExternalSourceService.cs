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
                        DeleteSafely(dir);
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

            // 3. 先写标记（EnsureLinks 要靠它找来源），再建 mods / config / saves 的联接
            var marker = new ReferenceMarker
            {
                Root = root,
                Version = version,
                CreatedAncestors = ancestors,
            };

            File.WriteAllText(System.IO.Path.Combine(dir, MarkerName),
                JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));

            EnsureLinks(instance);

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

    /// <summary>断开所有引用实例（卸载前调用）。返回处理了几个。</summary>
    public static int DisconnectAll()
    {
        var count = 0;

        foreach (var instance in GameLauncher.InstalledVersions())
        {
            if (!IsReference(instance)) continue;

            try
            {
                if (Disconnect(instance, out _)) count++;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "disconnect all " + instance);
            }
        }

        return count;
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

            // 先把 junction 拆掉：递归删除有可能跟着联接进到外部目录，把玩家的东西删了
            foreach (var name in new[] { "mods", "config", "saves", "resourcepacks", "shaderpacks" })
            {
                var link = System.IO.Path.Combine(dir, name);
                if (IsJunction(link)) Unlink(link);
            }

            var versionJar = System.IO.Path.Combine(dir, instance + ".jar");
            if (File.Exists(versionJar)) File.Delete(versionJar);

            DeleteSafely(dir);

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

    /// <summary>引用实例的来源（root + 外部版本名）；不是引用实例返回 null。</summary>
    public sealed record ReferenceInfo(string Root, string Version);

    public static ReferenceInfo? GetReference(string instance)
    {
        try
        {
            var dir = System.IO.Path.Combine(GamePaths.VersionsDir, instance);
            var marker = ReadMarker(dir);

            return marker is null || string.IsNullOrWhiteSpace(marker.Root)
                ? null
                : new ReferenceInfo(marker.Root, marker.Version);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>库文件：先找本地 libraries，再找引用源的 libraries。返回第一个存在的完整路径。</summary>
    public static string? ResolveLibrary(string instance, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var local = System.IO.Path.Combine(GamePaths.LibrariesDir, relativePath);
        if (File.Exists(local)) return local;

        var reference = GetReference(instance);
        if (reference is null) return null;

        var external = System.IO.Path.Combine(reference.Root, "libraries", relativePath);
        return File.Exists(external) ? external : null;
    }

    /// <summary>引用实例用外部 .minecraft 的 assets（有 indexes 才算数）。</summary>
    public static string? AssetsDirFor(string instance)
    {
        var reference = GetReference(instance);
        if (reference is null) return null;

        try
        {
            var assets = System.IO.Path.Combine(reference.Root, "assets");
            return Directory.Exists(System.IO.Path.Combine(assets, "indexes")) ? assets : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>客户端 jar：本地实例目录 → 外部版本目录 → 继承链（本地 / 外部）。</summary>
    public static string? ResolveClientJar(string instance, string instanceDir, string? inheritsFrom)
    {
        var reference = GetReference(instance);

        var candidates = new List<string>
        {
            System.IO.Path.Combine(instanceDir, instance + ".jar"),
        };

        if (reference is not null)
        {
            candidates.Add(System.IO.Path.Combine(reference.Root, "versions", reference.Version,
                reference.Version + ".jar"));
        }

        if (!string.IsNullOrWhiteSpace(inheritsFrom))
        {
            candidates.Add(System.IO.Path.Combine(GamePaths.VersionsDir, inheritsFrom, inheritsFrom + ".jar"));

            if (reference is not null)
            {
                candidates.Add(System.IO.Path.Combine(reference.Root, "versions", inheritsFrom,
                    inheritsFrom + ".jar"));
            }
        }

        // 实例目录里任意一个 jar 也能用（有些启动器会把 jar 改名）
        if (Directory.Exists(instanceDir))
        {
            candidates.AddRange(Directory.EnumerateFiles(instanceDir, "*.jar")
                .OrderByDescending(path => System.IO.Path.GetFileName(path) == instance + ".jar"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 补齐 / 修复引用实例的目录联接：mods、config、saves、resourcepacks、shaderpacks。
    /// 版本目录里没有的，退回外部 .minecraft 的全局目录（很多启动器不按版本隔离）。
    /// 每次启动前都会跑一遍，之前建残了也能修好。
    /// </summary>
    public static void EnsureLinks(string instance)
    {
        try
        {
            var reference = GetReference(instance);
            if (reference is null) return;

            var dir = System.IO.Path.Combine(GamePaths.VersionsDir, instance);
            var versionDir = System.IO.Path.Combine(reference.Root, "versions", reference.Version);

            foreach (var name in new[] { "mods", "config", "saves", "resourcepacks", "shaderpacks" })
            {
                var link = System.IO.Path.Combine(dir, name);
                var target = PickLinkTarget(versionDir, reference.Root, name);

                if (target is null) continue;

                // 已经有联接：指向的目录是空的、而另一个候选里有东西 → 换过去
                if (Directory.Exists(link))
                {
                    if (!IsJunction(link)) continue;            // 本地真实目录，别动

                    var current = CurrentLinkTarget(link);
                    if (current is not null &&
                        string.Equals(System.IO.Path.GetFullPath(current), System.IO.Path.GetFullPath(target),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (current is not null && HasContent(current) && !HasContent(target)) continue;

                    Unlink(link);

                    if (!Directory.Exists(link)) Link(link, target);
                    continue;
                }

                Link(link, target);
            }

            // 设置文件：外部有就复制一份过来（第一份本地，之后游戏自己改）
            foreach (var file in new[] { "options.txt", "servers.dat" })
            {
                var local = System.IO.Path.Combine(dir, file);
                if (System.IO.File.Exists(local)) continue;

                var external = System.IO.Path.Combine(versionDir, file);
                if (!System.IO.File.Exists(external)) external = System.IO.Path.Combine(reference.Root, file);
                if (!System.IO.File.Exists(external)) continue;

                try
                {
                    System.IO.File.Copy(external, local, overwrite: false);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "copy " + file);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "ensure reference links");
        }
    }

    /// <summary>
    /// 选联接目标：优先「有内容的目录」——PCL2 这类启动器即使不按版本隔离，
    /// 也会给每个版本建一个空的 mods/ 目录，直接链过去就是空壳。
    /// </summary>
    private static string? PickLinkTarget(string versionDir, string root, string name)
    {
        var withContent = new[]
            {
                System.IO.Path.Combine(versionDir, name),
                System.IO.Path.Combine(root, name),
            }
            .FirstOrDefault(HasContent);

        if (withContent is not null) return withContent;

        var existing = new[]
            {
                System.IO.Path.Combine(versionDir, name),
                System.IO.Path.Combine(root, name),
            }
            .FirstOrDefault(Directory.Exists);

        return existing;
    }

    /// <summary>
    /// 安全删除目录：碰到联接（reparse point）只删链接本身，绝不递归进目标目录。
    /// .NET 的 Directory.Delete(recursive) 在某些场景会跟着联接把外部内容删掉，这里手动走。
    /// </summary>
    private static void DeleteSafely(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            try
            {
                var attributes = System.IO.File.GetAttributes(entry);

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // 联接 / 符号链接：只删链接
                    Directory.Delete(entry);
                    continue;
                }

                if (Directory.Exists(entry)) DeleteSafely(entry);
                else System.IO.File.Delete(entry);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "delete " + entry);
            }
        }

        try
        {
            Directory.Delete(dir);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete dir " + dir);
        }
    }

    private static bool HasContent(string dir)
    {
        try
        {
            return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJunction(string path)
    {
        try
        {
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static string? CurrentLinkTarget(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget;
        }
        catch
        {
            return null;
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

            if (!Directory.Exists(link))
            {
                // mklink 失败（极少见）：再试一次 .NET 的目录符号链接
                try
                {
                    Directory.CreateSymbolicLink(link, target);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "create link fallback " + link);
                }
            }

            if (Directory.Exists(link)) AppLog.Info($"reference link: {link} -> {target}");
            else AppLog.Error(new IOException("link failed"), $"reference link {link} -> {target}");
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