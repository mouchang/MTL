using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MutsumiLauncher.Services;

public enum LoaderKind
{
    Forge,
    NeoForge,
}

/// <summary>Forge / NeoForge 安装：下载官方安装器并在临时目录执行 --installClient，再合并结果。</summary>
public sealed class ForgeLoader
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromMinutes(10));

    private const string NeoForgeOriginBase = "https://maven.neoforged.net";

    private static string NeoForgeVersionListUrl(string baseUrl)
        => $"{baseUrl}/api/maven/versions/releases/net/neoforged/neoforge";

    private static string NeoForgeInstallerUrl(string baseUrl, string version)
        => $"{baseUrl}/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar";

    public static async Task<IReadOnlyList<string>> GetVersionsAsync(LoaderKind kind, string gameVersion,
        CancellationToken cancellationToken = default)
    {
        return kind == LoaderKind.Forge
            ? await GetForgeVersionsAsync(gameVersion, cancellationToken)
            : await GetNeoForgeVersionsAsync(gameVersion, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> GetForgeVersionsAsync(string gameVersion,
        CancellationToken cancellationToken)
    {
        var xml = await Http.GetStringAsync(
            "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", cancellationToken);

        var document = XDocument.Parse(xml);
        var prefix = gameVersion + "-";

        return document.Descendants("version")
            .Select(node => node.Value.Trim())
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => value[prefix.Length..])
            .Distinct()
            .OrderByDescending(ParseVersion)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> GetNeoForgeVersionsAsync(string gameVersion,
        CancellationToken cancellationToken)
    {
        // 版本列表属于「获取数据」，直连官方即可
        var json = await Http.GetStringAsync(NeoForgeVersionListUrl(NeoForgeOriginBase),
            cancellationToken);

        using var document = JsonDocument.Parse(json);
        var prefix = NeoForgePrefix(gameVersion);
        if (string.IsNullOrEmpty(prefix)) return [];

        return document.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderByDescending(ParseVersion)
            .ToList();
    }

    /// <summary>1.21.1 → 21.1.；1.20.4 → 20.4.；1.20.1 → 47.（NeoForge 特例）。</summary>
    private static string NeoForgePrefix(string gameVersion)
    {
        // NeoForge 老版本特例：1.20.1 用 47.x
        if (gameVersion == "1.20.1") return "47.";

        var parts = gameVersion.Split('.');

        // 1.21.1 → 21.1.；26.3 这类新版本号 → 26.3.0.
        if (parts.Length >= 3)
            return $"{parts[1]}.{parts[2]}.";

        if (parts.Length == 2)
            return parts[0] == "1" ? $"{parts[1]}.0." : $"{parts[0]}.{parts[1]}.0.";

        return string.Empty;
    }

    private static double ParseVersion(string version)
    {
        var main = version.Split('-')[0];
        var parts = main.Split('.');
        double result = 0;

        for (var i = 0; i < Math.Min(parts.Length, 4); i++)
        {
            if (!double.TryParse(parts[i], out var number)) number = 0;
            result = result * 100 + number;
        }

        return result;
    }

    public async Task<string> InstallAsync(LoaderKind kind, string gameVersion, string loaderVersion,
        IProgress<InstallProgress>? progress = null, string? instanceName = null,
        CancellationToken cancellationToken = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), "mtl-loader-" + Guid.NewGuid().ToString("N"));
        var vanillaDir = Path.Combine(GamePaths.VersionsDir, gameVersion);
        var vanillaJson = Path.Combine(vanillaDir, $"{gameVersion}.json");
        if (!File.Exists(vanillaJson))
            throw new InvalidOperationException($"请先安装原版 {gameVersion}");

        Directory.CreateDirectory(temp);

        try
        {
            progress?.Report(new InstallProgress(3, "准备安装环境…"));

            // 1. 临时游戏目录
            var tempVanilla = Path.Combine(temp, "versions", gameVersion);
            CopyDirectory(vanillaDir, tempVanilla);
            await File.WriteAllTextAsync(Path.Combine(temp, "launcher_profiles.json"),
                "{\"profiles\":{},\"settings\":{},\"version\":\"3\"}", cancellationToken);

            // 2. 下载安装器
            progress?.Report(new InstallProgress(8, "下载安装器…"));
            var installer = Path.Combine(temp, "installer.jar");

            if (kind == LoaderKind.Forge)
            {
                var forgeUrl =
                    $"https://bmclapi2.bangbang93.com/forge/download?mcversion={gameVersion}&version={loaderVersion}&category=installer";
                await DownloadAsync(forgeUrl, installer, cancellationToken);
            }
            else
            {
                var cdnBase = DownloadMirror.CustomCdnBase;
                var cdnBaseUrl = cdnBase is null ? null : $"{cdnBase}/maven.neoforged.net";

                if (cdnBaseUrl is not null)
                {
                    try
                    {
                        await DownloadAsync(NeoForgeInstallerUrl(cdnBaseUrl, loaderVersion), installer,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, "neoforge cdn installer");
                        await DownloadAsync(NeoForgeInstallerUrl(NeoForgeOriginBase, loaderVersion),
                            installer, cancellationToken);
                    }

                    // 安装器内部还会直接访问 maven.neoforged.net，改写 jar 内的地址到 CDN
                    progress?.Report(new InstallProgress(14, "改写安装器下载源…"));
                    await PatchInstallerHostsAsync(installer, cdnBaseUrl, cancellationToken);
                }
                else
                {
                    await DownloadAsync(NeoForgeInstallerUrl(NeoForgeOriginBase, loaderVersion), installer,
                        cancellationToken);
                }
            }

            // 3. 选 Java
            var java = await PickJavaAsync(gameVersion);
            if (java is null) throw new InvalidOperationException("没有可用的 Java 运行时");

            // 4. 运行安装器
            progress?.Report(new InstallProgress(20,
                $"运行安装器（可能需要几分钟）…"));
            var log = await RunInstallerAsync(java.ExecutablePath, installer, temp, cancellationToken);

            // 5. 合并结果
            progress?.Report(new InstallProgress(80, "合并安装结果…"));
            var versionsRoot = Path.Combine(temp, "versions");
            var produced = Directory.Exists(versionsRoot)
                ? Directory.GetDirectories(versionsRoot)
                    .Where(dir => !Path.GetFileName(dir).Equals(gameVersion, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : [];

            if (produced.Count == 0)
                throw new InvalidOperationException("安装器没有生成版本目录：\n" + Tail(log));

            var id = Path.GetFileName(produced[0]);
            var finalName = string.IsNullOrWhiteSpace(instanceName) ? id : instanceName.Trim();
            var finalDir = Path.Combine(GamePaths.VersionsDir, finalName);
            CopyDirectory(produced[0], finalDir);

            if (!finalName.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                var oldJson = Path.Combine(finalDir, $"{id}.json");
                var newJson = Path.Combine(finalDir, $"{finalName}.json");
                if (File.Exists(oldJson))
                {
                    var text = await File.ReadAllTextAsync(oldJson, cancellationToken);
                    File.Delete(oldJson);
                    File.WriteAllText(newJson, text.Replace($"\"id\": \"{id}\"", $"\"id\": \"{finalName}\""));
                }

                id = finalName;
            }

            var tempLibraries = Path.Combine(temp, "libraries");
            if (Directory.Exists(tempLibraries))
                CopyDirectory(tempLibraries, GamePaths.LibrariesDir);

            progress?.Report(new InstallProgress(100, $"已安装 {id}"));
            AppLog.Info($"{(kind == LoaderKind.Forge ? "forge" : "neoforge")} installed: {id}");
            return id;
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch
            {
                // 临时目录清理失败不影响结果
            }
        }
    }

    private static async Task<string> RunInstallerAsync(string java, string installer, string workingDir,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(java)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installer);
        startInfo.ArgumentList.Add("--installClient");
        startInfo.ArgumentList.Add(workingDir);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"安装器退出码 {process.ExitCode}：\n{Tail(output.ToString())}");

        return output.ToString();
    }

    private static async Task<JavaRuntime?> PickJavaAsync(string gameVersion)
    {
        IReadOnlyList<JavaRuntime> runtimes;
        try
        {
            runtimes = await JavaDetector.DetectAsync();
        }
        catch
        {
            return null;
        }

        if (runtimes.Count == 0) return null;

        var required = RequiredJavaMajor(gameVersion);
        return runtimes.FirstOrDefault(runtime => runtime.Major == required)
               ?? runtimes.Where(runtime => runtime.Major >= required).OrderBy(runtime => runtime.Major)
                   .FirstOrDefault()
               ?? runtimes.OrderByDescending(runtime => runtime.Major).First();
    }

    private static int RequiredJavaMajor(string gameVersion)
    {
        var parts = gameVersion.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
        {
            if (minor >= 21) return 21;
            if (minor >= 18) return 17;
            if (minor == 17) return 16;
        }

        return 8;
    }

    /// <summary>
    /// 把安装器 jar 内 JSON 里的 maven.neoforged.net 地址替换成 CDN，
    /// 这样安装器自身下载依赖时也走 CDN（无需代理）。
    /// </summary>
    private static async Task PatchInstallerHostsAsync(string jarPath, string cdnBaseUrl,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Update);

        var targets = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var patchedCount = 0;

        foreach (var entry in targets)
        {
            string text;
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                text = await reader.ReadToEndAsync(cancellationToken);
            }

            if (!text.Contains("maven.neoforged.net", StringComparison.OrdinalIgnoreCase)) continue;

            var patched = text
                .Replace("https://maven.neoforged.net", cdnBaseUrl, StringComparison.OrdinalIgnoreCase)
                .Replace("http://maven.neoforged.net", cdnBaseUrl, StringComparison.OrdinalIgnoreCase);

            var name = entry.FullName;
            entry.Delete();

            var created = archive.CreateEntry(name);
            await using var output = created.Open();
            await using var writer = new StreamWriter(output, new UTF8Encoding(false));
            await writer.WriteAsync(patched);
            patchedCount++;
        }

        AppLog.Info($"neoforge installer patched: {patchedCount} entries");
    }

    private static async Task<string> GetStringWithFallbackAsync(string primary, string fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Http.GetStringAsync(primary, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "cdn request");
            return await Http.GetStringAsync(fallback, cancellationToken);
        }
    }

    private static async Task DownloadAsync(string url, string target, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"安装器下载失败：{(int)response.StatusCode}");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(target);
        await source.CopyToAsync(file, cancellationToken);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = file.Replace(source, target, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string Tail(string text, int lines = 12)
        => string.Join(Environment.NewLine,
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines));
}
