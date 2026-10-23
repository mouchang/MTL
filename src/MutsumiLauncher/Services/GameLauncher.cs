using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MutsumiLauncher.Services;

public sealed record LaunchProgress(double Percent, string Message);

public sealed record LaunchResult(bool Started, string Message, string? LogPath);

public sealed record LaunchOptions(string PlayerName, string Uuid, string JavaPath, int MemoryMb,
    bool VersionIsolation);

/// <summary>准备并启动一个已安装版本：解压 natives、拼 classpath、替换参数、选 Java、拉资源索引。</summary>
public sealed class GameLauncher
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MutsumiLauncher/0.1.0");
        return client;
    }

    public static IReadOnlyList<string> InstalledVersions()
    {
        if (!Directory.Exists(GamePaths.VersionsDir)) return [];

        return Directory.EnumerateDirectories(GamePaths.VersionsDir)
            .Where(dir => File.Exists(Path.Combine(dir, $"{Path.GetFileName(dir)}.json")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<LaunchResult> LaunchAsync(string id, LaunchOptions options,
        IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var versionDir = Path.Combine(GamePaths.VersionsDir, id);
        var jsonPath = Path.Combine(versionDir, $"{id}.json");
        if (!File.Exists(jsonPath)) return new LaunchResult(false, $"版本 {id} 还没有安装", null);

        var detail = JsonSerializer.Deserialize<VersionDetail>(await File.ReadAllTextAsync(jsonPath, cancellationToken))
                     ?? throw new InvalidOperationException("版本 JSON 解析失败");

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

        var classpath = BuildClasspath(detail, versionDir, id);

        var gameDir = options.VersionIsolation ? versionDir : GamePaths.Root;
        Directory.CreateDirectory(gameDir);

        progress?.Report(new LaunchProgress(20, "选择 Java…"));
        var java = await PickJavaAsync(detail, options.JavaPath);
        if (java is null) return new LaunchResult(false, "没有可用的 Java，请先安装或指定 Java 路径。", null);

        var arguments = BuildArguments(detail, id, gameDir, nativesDir, classpath, options.PlayerName,
            options.Uuid, options.MemoryMb);

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

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLine(logPath, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLine(logPath, e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        AppLog.Info($"launch {id} with {java.ExecutablePath}, pid {process.Id}");
        progress?.Report(new LaunchProgress(100, $"已启动 {id}（Java {java.Version}）"));

        return new LaunchResult(true, $"已启动 {id}（Java {java.Version}）", logPath);
    }

    private static string BuildClasspath(VersionDetail detail, string versionDir, string id)
    {
        var entries = new List<string> { Path.Combine(versionDir, $"{id}.jar") };

        entries.AddRange(AllowedLibraries(detail)
            .Select(l => l.Downloads?.Artifact)
            .Where(a => a?.Path is not null)
            .Select(a => Path.Combine(GamePaths.LibrariesDir, a!.Path!))
            .Where(File.Exists));

        return string.Join(';', entries);
    }

    private static List<string> BuildArguments(VersionDetail detail, string id, string gameDir,
        string nativesDir, string classpath, string playerName, string uuid, int memoryMb)
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
            ["auth_access_token"] = "0",
            ["auth_session"] = "0",
            ["user_type"] = "legacy",
            ["version_type"] = string.IsNullOrEmpty(detail.Type) ? "release" : detail.Type,
            ["natives_directory"] = nativesDir,
            ["classpath"] = classpath,
            ["classpath_separator"] = ";",
            ["launcher_name"] = "MutsumiLauncher",
            ["launcher_version"] = "0.1.0",
            ["clientid"] = "0",
            ["auth_xuid"] = "0",
        };

        var arguments = new List<string>
        {
            $"-Djava.library.path={nativesDir}",
            "-Dfile.encoding=UTF-8",
        };

        if (memoryMb > 0) arguments.Add($"-Xmx{memoryMb}M");

        arguments.Add("-cp");
        arguments.Add(classpath);

        if (!string.IsNullOrWhiteSpace(detail.MainClass)) arguments.Add(detail.MainClass);

        if (!string.IsNullOrWhiteSpace(detail.MinecraftArguments))
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
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        arguments.Add(Substitute(item.GetString() ?? string.Empty, values));
                    }
                    else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("value", out var value))
                    {
                        if (value.ValueKind == JsonValueKind.String)
                            arguments.Add(Substitute(value.GetString() ?? string.Empty, values));
                        else if (value.ValueKind == JsonValueKind.Array)
                            arguments.AddRange(value.EnumerateArray().Select(v => Substitute(v.GetString() ?? string.Empty, values)));
                    }
                }
            }
        }

        return arguments;
    }

    private static string Substitute(string value, IReadOnlyDictionary<string, string> map)
    {
        foreach (var (key, replacement) in map)
        {
            value = value.Replace("${" + key + "}", replacement, StringComparison.Ordinal);
        }

        return value;
    }

    private static async Task<JavaRuntime?> PickJavaAsync(VersionDetail detail, string preferredPath)
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

        if (runtimes.Count == 0) return null;

        var required = RequiredJavaMajor(detail);
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
