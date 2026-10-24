using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MutsumiLauncher.Services;

public sealed class JavaRuntime
{
    public required string ExecutablePath { get; init; }

    public required string Home { get; init; }

    public required string Version { get; init; }

    public required int Major { get; init; }

    public required string Source { get; init; }

    public string DisplayVersion => $"Java {Version}";

    public string Display => $"{DisplayVersion} · {Home}";
}

/// <summary>扫描本机 Java：JAVA_HOME、PATH、注册表、常见安装目录、Minecraft 自带运行时。</summary>
public static class JavaDetector
{
    private static readonly Regex VersionPattern = new("version \"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<IReadOnlyList<JavaRuntime>> DetectAsync()
    {
        var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? exe, string source)
        {
            if (string.IsNullOrWhiteSpace(exe)) return;

            try
            {
                var full = Path.GetFullPath(exe);
                if (!File.Exists(full)) return;
                candidates.TryAdd(full, source);
            }
            catch
            {
                // 路径非法时忽略
            }
        }

        // 1. JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            Add(Path.Combine(javaHome, "bin", "java.exe"), "环境变量 JAVA_HOME");

        // 2. PATH
        foreach (var dir in SplitPath(Environment.GetEnvironmentVariable("PATH")))
            Add(Path.Combine(dir, "java.exe"), "PATH");

        // 3. 注册表
        AddFromRegistry(Add);

        // 4. 常见安装目录
        foreach (var root in CommonRoots())
        {
            foreach (var dir in SafeEnumerateDirectories(root))
            {
                Add(Path.Combine(dir, "bin", "java.exe"), "安装目录");
            }
        }

        // 5. 启动器自己下载的运行时
        var ownRuntime = JavaRuntimeService.RuntimeRoot;
        if (Directory.Exists(ownRuntime))
        {
            foreach (var exe in SafeEnumerateFiles(ownRuntime, "java.exe"))
            {
                if (exe.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    Add(exe, "启动器运行时");
            }
        }

        // 6. Minecraft 自带运行时
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var runtimeRoot = Path.Combine(appData, ".minecraft", "runtime");
            if (Directory.Exists(runtimeRoot))
            {
                foreach (var exe in SafeEnumerateFiles(runtimeRoot, "java.exe"))
                {
                    if (exe.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                        Add(exe, "Minecraft 运行时");
                }
            }
        }

        var result = new List<JavaRuntime>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (exe, source) in candidates)
        {
            var runtime = await ProbeAsync(exe, source);
            if (runtime is null) continue;

            // 同一份 Java 常被多个来源命中，按 exe 去重即可；这里再按 home 去重
            if (!seenVersions.Add(runtime.Home)) continue;
            result.Add(runtime);
        }

        return result
            .OrderByDescending(r => r.Major)
            .ThenBy(r => r.Home, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddFromRegistry(Action<string?, string> add)
    {
        string[] paths =
        [
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\JavaSoft\JRE",
        ];

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var path in paths)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                        .OpenSubKey(path);
                    if (baseKey is null) continue;

                    foreach (var subName in baseKey.GetSubKeyNames())
                    {
                        using var sub = baseKey.OpenSubKey(subName);
                        if (sub?.GetValue("JavaHome") is string home && !string.IsNullOrWhiteSpace(home))
                        {
                            add(Path.Combine(home, "bin", "java.exe"), $"注册表 · {subName}");
                        }
                    }
                }
                catch
                {
                    // 无权限或键不存在
                }
            }
        }
    }

    private static async Task<JavaRuntime?> ProbeAsync(string exe, string source)
    {
        var home = Path.GetDirectoryName(Path.GetDirectoryName(exe));
        if (string.IsNullOrWhiteSpace(home)) return null;

        var version = ReadReleaseVersion(home) ?? await ReadProcessVersionAsync(exe);
        if (string.IsNullOrWhiteSpace(version)) return null;

        var major = ParseMajor(version);
        if (major <= 0) return null;

        return new JavaRuntime
        {
            ExecutablePath = exe,
            Home = home,
            Version = version,
            Major = major,
            Source = source,
        };
    }

    /// <summary>JDK/JRE 目录下的 release 文件里有 JAVA_VERSION="17.0.8"。</summary>
    private static string? ReadReleaseVersion(string home)
    {
        try
        {
            var release = Path.Combine(home, "release");
            if (!File.Exists(release)) return null;

            foreach (var line in File.ReadLines(release))
            {
                if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal)) continue;

                var value = line["JAVA_VERSION=".Length..].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }

    private static async Task<string?> ReadProcessVersionAsync(string exe)
    {
        try
        {
            var startInfo = new ProcessStartInfo(exe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null) return null;

            var stderr = await process.StandardError.ReadToEndAsync();
            var stdout = await process.StandardOutput.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);

            var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            var match = VersionPattern.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static int ParseMajor(string version)
    {
        var text = version.Trim();
        var parts = text.Split('.', '_', '-', '+');

        if (parts.Length == 0 || !int.TryParse(parts[0], out var first)) return 0;
        if (first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var legacy)) return legacy;

        return first;
    }

    private static IEnumerable<string> CommonRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var roots = new[]
        {
            Path.Combine(programFiles, "Java"),
            Path.Combine(programFiles, "Eclipse Adoptium"),
            Path.Combine(programFiles, "Microsoft"),
            Path.Combine(programFiles, "BellSoft"),
            Path.Combine(programFiles, "Zulu"),
            Path.Combine(programFiles, "Amazon Corretto"),
            Path.Combine(programFilesX86, "Java"),
            Path.Combine(localAppData, "Programs", "Eclipse Adoptium"),
            Path.Combine(localAppData, "Programs", "Microsoft"),
        };

        return roots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r));
    }

    private static IEnumerable<string> SplitPath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? []
            : path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
        }
        catch
        {
            return [];
        }
    }
}
