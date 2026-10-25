using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record LaunchProgress(double Percent, string Message);

public sealed record LaunchResult(bool Started, string Message, string? LogPath, int ProcessId = 0);

public sealed record LaunchOptions(string PlayerName, string Uuid, string AccessToken, bool IsMicrosoft,
    string JavaPath, int MemoryMb, bool VersionIsolation);

/// <summary>准备并启动一个已安装版本：解压 natives、拼 classpath、替换参数、选 Java、拉资源索引。</summary>
public sealed class GameLauncher
{
    private static readonly object ProcessLock = new();

    /// <summary>当前正在运行的游戏进程（供启动面板强制关闭）。</summary>
    public static Process? ActiveProcess { get; private set; }

    /// <summary>最近一次游戏进程的退出码（供启动面板判断是否崩溃）。</summary>
    public static int? LastExitCode { get; private set; }

    public static bool KillActive()
    {
        lock (ProcessLock)
        {
            try
            {
                if (ActiveProcess is null || ActiveProcess.HasExited) return false;

                ActiveProcess.Kill(entireProcessTree: true);
                ActiveProcess.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "kill game process");
                return false;
            }
        }
    }

    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromMinutes(5));

    public static IReadOnlyList<string> InstalledVersions()
    {
        if (!Directory.Exists(GamePaths.VersionsDir)) return [];

        return Directory.EnumerateDirectories(GamePaths.VersionsDir)
            .Where(dir => File.Exists(Path.Combine(dir, $"{Path.GetFileName(dir)}.json")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderByDescending(name => name, NaturalNameComparer.Instance)
            .ToList();
    }

    /// <summary>版本名自然排序：1.9 &lt; 1.21（字符串排序会把 1.9 排在 1.21 前面）。</summary>
    private sealed class NaturalNameComparer : IComparer<string>
    {
        public static readonly NaturalNameComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var i = 0;
            var j = 0;

            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var startX = i;
                    while (i < x.Length && char.IsAsciiDigit(x[i])) i++;

                    var startY = j;
                    while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                    var numberX = x.AsSpan(startX, i - startX).TrimStart('0');
                    var numberY = y.AsSpan(startY, j - startY).TrimStart('0');

                    if (numberX.Length != numberY.Length) return numberX.Length.CompareTo(numberY.Length);

                    var numeric = numberX.SequenceCompareTo(numberY);
                    if (numeric != 0) return numeric;

                    continue;
                }

                var compare = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (compare != 0) return compare;

                i++;
                j++;
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }
    }

    public async Task<LaunchResult> LaunchAsync(string id, LaunchOptions options,
        IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var versionDir = Path.Combine(GamePaths.VersionsDir, id);
        var jsonPath = Path.Combine(versionDir, $"{id}.json");
        if (!File.Exists(jsonPath)) return new LaunchResult(false, $"版本 {id} 还没有安装", null);

        var detail = JsonSerializer.Deserialize<VersionDetail>(await File.ReadAllTextAsync(jsonPath, cancellationToken))
                     ?? throw new InvalidOperationException("版本 JSON 解析失败");

        if (!string.IsNullOrWhiteSpace(detail.InheritsFrom))
            detail = MergeInherited(detail, detail.InheritsFrom);

        if (detail.AssetIndex is { Url.Length: > 0 } assetIndex)
        {
            progress?.Report(new LaunchProgress(3, "检查资源文件…"));
            IProgress<InstallProgress>? assetProgress = progress is null
                ? null
                : new Progress<InstallProgress>(p =>
                    progress.Report(new LaunchProgress(3 + p.Percent * 0.1, p.Message)));
            await AssetInstaller.EnsureAsync(assetIndex, assetProgress, cancellationToken);
        }

        progress?.Report(new LaunchProgress(15, "解压 natives…"));
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);
        foreach (var artifact in AllowedLibraries(detail).SelectMany(NativeClassifiers))
        {
            var path = Path.Combine(GamePaths.LibrariesDir, artifact.Path!);
            if (File.Exists(path)) ExtractNatives(path, nativesDir);
        }

        var jarPath = Path.Combine(versionDir, $"{id}.jar");
        if (!File.Exists(jarPath) && !string.IsNullOrWhiteSpace(detail.InheritsFrom))
        {
            jarPath = Path.Combine(GamePaths.VersionsDir, detail.InheritsFrom,
                $"{detail.InheritsFrom}.jar");
        }

        var classpath = BuildClasspath(detail, jarPath);

        var missing = classpath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !File.Exists(entry))
            .ToList();

        if (missing.Count > 0)
        {
            var names = string.Join("、", missing.Take(3).Select(Path.GetFileName));
            return new LaunchResult(false,
                $"缺少 {missing.Count} 个依赖文件（如 {names}），请到下载页重新安装该版本以补全文件。",
                null);
        }

        var gameDir = options.VersionIsolation ? versionDir : GamePaths.Root;
        Directory.CreateDirectory(gameDir);

        // Fabric 会在 .fabric/remappedJars 写重映射后的 jar；临时目录也放到游戏目录，避免系统 TEMP 不可写
        var tempDir = Path.Combine(gameDir, "temp");
        Directory.CreateDirectory(tempDir);

        var remapDir = Path.Combine(gameDir, ".fabric", "remappedJars");
        Directory.CreateDirectory(remapDir);

        // 清理上次异常退出留下的半成品（Fabric 遇到会报 can't delete incompletely remapped files）
        try
        {
            foreach (var stale in Directory.EnumerateFiles(remapDir, "*.tmp", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(stale);
                }
                catch
                {
                    // 删不掉就交给 Fabric 处理
                }
            }
        }
        catch
        {
            // 忽略
        }

        GameOptions.EnsureLanguage(gameDir);

        progress?.Report(new LaunchProgress(20, "选择 Java…"));
        var java = await PickJavaAsync(detail, options.JavaPath, progress);
        if (java is null) return new LaunchResult(false, "没有可用的 Java，请先安装或指定 Java 路径。", null);

        var arguments = BuildArguments(detail, id, gameDir, nativesDir, classpath, options.PlayerName,
            options.Uuid, options.AccessToken, options.IsMicrosoft, options.MemoryMb);

        var logDir = Path.Combine(GamePaths.Root, "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"{id}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        progress?.Report(new LaunchProgress(90, "启动游戏…"));

        var startInfo = new ProcessStartInfo(java.ExecutablePath)
        {
            WorkingDirectory = GamePaths.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        AppLog.Info($"launch command: {java.ExecutablePath} {string.Join(' ', arguments)}");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLine(logPath, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLine(logPath, e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        LastExitCode = null;

        lock (ProcessLock)
        {
            ActiveProcess = process;
        }

        process.Exited += (_, _) =>
        {
            try
            {
                LastExitCode = process.ExitCode;
            }
            catch
            {
                LastExitCode = null;
            }

            lock (ProcessLock)
            {
                if (ReferenceEquals(ActiveProcess, process)) ActiveProcess = null;
            }
        };

        AppLog.Info($"launch {id} with {java.ExecutablePath}, pid {process.Id}");
        progress?.Report(new LaunchProgress(100, $"已启动 {id}（Java {java.Version}）"));

        return new LaunchResult(true, $"已启动 {id}（Java {java.Version}）", logPath, process.Id);
    }

    private static string BuildClasspath(VersionDetail detail, string jarPath)
    {
        var entries = new List<string> { jarPath };

        entries.AddRange(AllowedLibraries(detail)
            .Select(library => library.Downloads?.Artifact?.Path is { } artifactPath
                ? artifactPath
                : MavenPathFromName(library.Name))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(GamePaths.LibrariesDir, path!))
            .Where(File.Exists));

        return string.Join(';', entries);
    }

    /// <summary>没有 downloads.artifact 的库（Fabric/NeoForge 常见）按 maven 坐标推导路径。</summary>
    private static string? MavenPathFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var extension = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        if (parts.Length < 3) return null;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}";
    }

    /// <summary>Fabric/Forge 等 profile 通过 inheritsFrom 继承原版，这里合并成一份可启动的版本。</summary>
    private static VersionDetail MergeInherited(VersionDetail child, string parentId)
    {
        var parentJson = Path.Combine(GamePaths.VersionsDir, parentId, $"{parentId}.json");
        if (!File.Exists(parentJson))
            throw new InvalidOperationException($"缺少基础版本 {parentId}，请先安装它。");

        var parent = JsonSerializer.Deserialize<VersionDetail>(
                         File.ReadAllText(parentJson))
                     ?? throw new InvalidOperationException($"基础版本 {parentId} 解析失败");

        var libraries = parent.Libraries
            .Concat(child.Libraries)
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        return new VersionDetail
        {
            Id = child.Id,
            Type = child.Type,
            MainClass = child.MainClass,
            Assets = parent.Assets,
            AssetIndex = parent.AssetIndex,
            Downloads = parent.Downloads,
            Libraries = libraries,
            Arguments = MergeArguments(parent.Arguments, child.Arguments),
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            JavaVersion = parent.JavaVersion ?? child.JavaVersion,
            InheritsFrom = parentId,
        };
    }

    private static bool HasGameArguments(System.Text.Json.JsonElement? arguments)
    {
        if (arguments is not { ValueKind: System.Text.Json.JsonValueKind.Object } element) return false;

        return element.TryGetProperty("game", out var game)
               && game.ValueKind == System.Text.Json.JsonValueKind.Array
               && game.GetArrayLength() > 0;
    }

    private static List<string> BuildArguments(VersionDetail detail, string id, string gameDir,
        string nativesDir, string classpath, string playerName, string uuid, string accessToken,
        bool isMicrosoft, int memoryMb)
    {
        if (string.IsNullOrWhiteSpace(uuid)) uuid = OfflineUuid(playerName);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = playerName,
            ["version_name"] = id,
            ["game_directory"] = gameDir,
            ["assets_root"] = GamePaths.AssetsDir,
            ["game_assets"] = Path.Combine(GamePaths.AssetsDir, "virtual", "legacy"),
            ["assets_index_name"] = detail.AssetIndex?.Id ?? detail.Assets,
            ["auth_uuid"] = uuid,
            ["auth_access_token"] = string.IsNullOrWhiteSpace(accessToken) ? "0" : accessToken,
            ["auth_session"] = "0",
            ["user_type"] = isMicrosoft ? "msa" : "legacy",
            ["version_type"] = string.IsNullOrEmpty(detail.Type) ? "release" : detail.Type,
            ["natives_directory"] = nativesDir,
            ["library_directory"] = GamePaths.LibrariesDir,
            ["classpath"] = classpath,
            ["classpath_separator"] = ";",
            ["launcher_name"] = "MutsumiLauncher",
            ["launcher_version"] = AppInfo.Version,
            ["clientid"] = "0",
            ["auth_xuid"] = "0",
        };

        var arguments = new List<string>
        {
            $"-Djava.library.path={nativesDir}",
            "-Dfile.encoding=UTF-8",
        };

        if (memoryMb > 0) arguments.Add($"-Xmx{memoryMb}M");

        arguments.Add($"-Djava.io.tmpdir={Path.Combine(gameDir, "temp")}");

        // Forge / NeoForge 等会在 arguments.jvm 里带自定义 JVM 参数
        if (detail.Arguments is { ValueKind: System.Text.Json.JsonValueKind.Object } jvmContainer
            && jvmContainer.TryGetProperty("jvm", out var jvmArray)
            && jvmArray.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in jvmArray.EnumerateArray())
            {
                if (!IsArgumentAllowed(item)) continue;

                foreach (var raw in ReadArgumentValues(item))
                {
                    var value = Substitute(raw, values);
                    if (!string.IsNullOrWhiteSpace(value)) arguments.Add(value);
                }
            }
        }

        arguments.Add("-cp");
        arguments.Add(classpath);

        if (!string.IsNullOrWhiteSpace(detail.MainClass)) arguments.Add(detail.MainClass);

        var hasModernArguments = detail.Arguments is
            { ValueKind: System.Text.Json.JsonValueKind.Object } modern
            && modern.TryGetProperty("game", out _);

        if (!hasModernArguments && !string.IsNullOrWhiteSpace(detail.MinecraftArguments))
        {
            foreach (var token in detail.MinecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                arguments.Add(Substitute(token, values));
            return arguments;
        }

        if (detail.Arguments is { ValueKind: JsonValueKind.Object } gameArguments)
        {
            foreach (var element in gameArguments.EnumerateObject())
            {
                // 只处理 game 参数；JVM 参数已在上面手工拼好
                if (!element.Name.Equals("game", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var item in element.Value.EnumerateArray())
                {
                    if (!IsArgumentAllowed(item)) continue;

                    foreach (var raw in ReadArgumentValues(item))
                        arguments.Add(Substitute(raw, values));
                }
            }
        }

        return arguments;
    }

    /// <summary>把父/子版本的 arguments 合并成一个 JSON 对象（game + jvm 数组拼接）。</summary>
    private static System.Text.Json.JsonElement? MergeArguments(
        System.Text.Json.JsonElement? parent, System.Text.Json.JsonElement? child)
    {
        if (parent is null) return child;
        if (child is null) return parent;

        static List<string> Read(System.Text.Json.JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var array)
                || array.ValueKind != System.Text.Json.JsonValueKind.Array)
                return [];

            return array.EnumerateArray().Select(item => item.GetRawText()).ToList();
        }

        var game = Read(parent.Value, "game").Concat(Read(child.Value, "game")).ToList();
        var jvm = Read(parent.Value, "jvm").Concat(Read(child.Value, "jvm")).ToList();

        var json = "{\"game\":[" + string.Join(",", game) + "],\"jvm\":[" + string.Join(",", jvm) + "]}";
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>按 rules 过滤参数（例如 -XstartOnFirstThread 只允许 macOS）。</summary>
    private static bool IsArgumentAllowed(System.Text.Json.JsonElement item)
    {
        if (item.ValueKind != System.Text.Json.JsonValueKind.Object) return true;
        if (!item.TryGetProperty("rules", out var rules)
            || rules.ValueKind != System.Text.Json.JsonValueKind.Array)
            return true;

        var allowed = false;

        foreach (var rule in rules.EnumerateArray())
        {
            if (!RuleMatches(rule)) continue;

            var action = rule.TryGetProperty("action", out var actionElement)
                ? actionElement.GetString()
                : "allow";

            allowed = action == "allow";
        }

        return allowed;
    }

    private static bool RuleMatches(System.Text.Json.JsonElement rule)
    {
        // 需要启动器特性判断的规则（demo、自定义分辨率等）一律跳过
        if (rule.TryGetProperty("features", out _)) return false;

        if (rule.TryGetProperty("os", out var os))
        {
            if (os.TryGetProperty("name", out var name)
                && !string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase))
                return false;

            if (os.TryGetProperty("arch", out var arch)
                && !string.Equals(arch.GetString(), "x86_64", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static IEnumerable<string> ReadArgumentValues(System.Text.Json.JsonElement item)
    {
        if (item.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
            yield break;
        }

        if (item.ValueKind != System.Text.Json.JsonValueKind.Object) yield break;
        if (!item.TryGetProperty("value", out var valueElement)) yield break;

        if (valueElement.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var value = valueElement.GetString();
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }
        else if (valueElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var entry in valueElement.EnumerateArray())
            {
                var value = entry.GetString();
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
        }
    }

    private static string Substitute(string value, IReadOnlyDictionary<string, string> map)
    {
        foreach (var (key, replacement) in map)
        {
            value = value.Replace("${" + key + "}", replacement, StringComparison.Ordinal);
        }

        return value;
    }

    private static async Task<JavaRuntime?> PickJavaAsync(VersionDetail detail, string preferredPath,
        IProgress<LaunchProgress>? progress)
    {
        IReadOnlyList<JavaRuntime> runtimes;
        try
        {
            runtimes = await JavaDetector.DetectAsync();
        }
        catch
        {
            runtimes = [];
        }

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var match = runtimes.FirstOrDefault(r =>
                r.ExecutablePath.Equals(preferredPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;

            if (File.Exists(preferredPath))
            {
                return new JavaRuntime
                {
                    ExecutablePath = preferredPath,
                    Home = Path.GetDirectoryName(Path.GetDirectoryName(preferredPath)) ?? string.Empty,
                    Version = "自定义",
                    Major = 0,
                    Source = "配置",
                };
            }
        }

        var required = RequiredJavaMajor(detail);

        // 没有满足要求的 Java：自动下载一个
        if (runtimes.Count == 0 || runtimes.All(r => r.Major < required))
        {
            try
            {
                var downloadProgress = progress is null
                    ? null
                    : new Progress<InstallProgress>(p =>
                        progress.Report(new LaunchProgress(18 + p.Percent * 0.05, p.Message)));

                var downloaded = await JavaRuntimeService.EnsureAsync(required, downloadProgress);
                if (downloaded is not null) return downloaded;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "auto download java");
            }
        }

        if (runtimes.Count == 0) return null;

        var exact = runtimes.FirstOrDefault(r => r.Major == required);
        if (exact is not null) return exact;

        var newer = runtimes.Where(r => r.Major >= required).OrderBy(r => r.Major).FirstOrDefault();
        return newer ?? runtimes.OrderByDescending(r => r.Major).First();
    }

    private static int RequiredJavaMajor(VersionDetail detail)
    {
        var declared = detail.JavaVersion?.MajorVersion ?? 0;
        if (declared > 0) return declared;

        // 未声明时按版本号规则推断
        var text = detail.Id;
        if (text.StartsWith("1.", StringComparison.Ordinal))
        {
            var parts = text.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            {
                if (minor >= 20) return 21;
                if (minor >= 18) return 17;
                if (minor == 17) return 16;
                return 8;
            }
        }

        return 21;
    }

    private static IEnumerable<Artifact> NativeClassifiers(Library library)
    {
        if (library.Downloads?.Classifiers is null) yield break;
        foreach (var (key, value) in library.Downloads.Classifiers)
        {
            if (key.Contains("natives-windows", StringComparison.OrdinalIgnoreCase))
                yield return value;
        }
    }

    private static IEnumerable<Library> AllowedLibraries(VersionDetail detail)
        => detail.Libraries.Where(IsAllowedOnWindows);

    private static bool IsAllowedOnWindows(Library library)
    {
        if (library.Rules is null || library.Rules.Count == 0) return true;

        var allowed = false;
        foreach (var rule in library.Rules)
        {
            var applies = rule.Os is null
                          || string.IsNullOrEmpty(rule.Os.Name)
                          || rule.Os.Name.Equals("windows", StringComparison.OrdinalIgnoreCase);

            if (!applies) continue;
            allowed = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static void ExtractNatives(string jarPath, string nativesDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            foreach (var entry in archive.Entries)
            {
                if (!entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                var target = Path.Combine(nativesDir, entry.Name);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"extract natives {Path.GetFileName(jarPath)}");
        }
    }

    private static void AppendLine(string path, string line)
    {
        try
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // 日志失败不影响游戏
        }
    }

    private static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
