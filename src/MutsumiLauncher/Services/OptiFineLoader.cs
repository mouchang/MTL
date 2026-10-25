using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MutsumiLauncher.Services;

/// <summary>BMCLAPI 的 OptiFine 条目，例如 type=HD_U、patch=J1 → OptiFine_1.21.1_HD_U_J1.jar。</summary>
public sealed record OptiFineBuild(string Type, string Patch, string FileName)
{
    /// <summary>界面上显示的版本号，例如 HD_U_J1。</summary>
    public string Display => string.IsNullOrEmpty(Patch) ? Type : $"{Type}_{Patch}";

    public bool IsPreview => FileName.StartsWith("preview_", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// OptiFine 安装：调用官方安装器的命令行入口（optifine.Installer），
/// 把 APPDATA 指到临时目录，让安装器只在临时 .minecraft 里生成版本与依赖。
/// </summary>
public sealed class OptiFineLoader
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromMinutes(5));

    private const string MavenBase = DownloadSources.Bmclapi + "/maven/com/optifine";

    /// <summary>列出某个游戏版本可用的 OptiFine（正式版在前，预览版在后）。</summary>
    public static async Task<IReadOnlyList<OptiFineBuild>> GetVersionsAsync(string gameVersion,
        CancellationToken cancellationToken = default)
    {
        var url = $"{DownloadSources.Bmclapi}/optifine/{Uri.EscapeDataString(gameVersion)}";
        var json = await Http.GetStringAsync(url, cancellationToken);

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

        var builds = new List<OptiFineBuild>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var fileName = ReadString(item, "filename");
            var type = ReadString(item, "type");
            var patch = ReadString(item, "patch");

            if (string.IsNullOrWhiteSpace(fileName)) continue;
            if (string.IsNullOrWhiteSpace(type)) continue;
            if (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;

            builds.Add(new OptiFineBuild(type, patch, fileName));
        }

        return builds
            .DistinctBy(build => build.FileName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(build => build.IsPreview ? 1 : 0)
            .ToList();
    }

    /// <summary>安装 OptiFine，返回生成的版本 ID。基础原版必须已安装。</summary>
    public async Task<string> InstallAsync(string gameVersion, OptiFineBuild build,
        IProgress<InstallProgress>? progress = null, string? instanceName = null,
        CancellationToken cancellationToken = default)
    {
        var vanillaDir = Path.Combine(GamePaths.VersionsDir, gameVersion);
        var vanillaJson = Path.Combine(vanillaDir, $"{gameVersion}.json");
        if (!File.Exists(vanillaJson))
            throw new InvalidOperationException($"请先安装原版 {gameVersion}");

        var temp = Path.Combine(Path.GetTempPath(), "mtl-optifine-" + Guid.NewGuid().ToString("N"));
        var minecraft = Path.Combine(temp, ".minecraft");
        var installer = Path.Combine(minecraft, build.FileName);

        try
        {
            progress?.Report(new InstallProgress(3, "准备安装环境…"));

            // 安装器只认识 .minecraft 结构：版本目录 + launcher_profiles.json
            CopyDirectory(vanillaDir, Path.Combine(minecraft, "versions", gameVersion));
            await File.WriteAllTextAsync(Path.Combine(minecraft, "launcher_profiles.json"),
                "{\"profiles\":{},\"settings\":{},\"version\":\"3\"}", cancellationToken);

            progress?.Report(new InstallProgress(10, "下载 OptiFine 安装器…"));
            await DownloadInstallerAsync(gameVersion, build, installer, cancellationToken);

            var java = await JavaSelector.PickAsync(gameVersion, cancellationToken);
            if (java is null) throw new InvalidOperationException("没有可用的 Java 运行时");

            progress?.Report(new InstallProgress(25, "运行 OptiFine 安装器（打补丁中）…"));
            var log = await RunInstallerAsync(java.ExecutablePath, installer, minecraft, temp,
                cancellationToken);

            var versionsRoot = Path.Combine(minecraft, "versions");
            var produced = Directory.Exists(versionsRoot)
                ? Directory.GetDirectories(versionsRoot)
                    .Where(dir => !Path.GetFileName(dir)
                        .Equals(gameVersion, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : [];

            if (produced.Count == 0)
                throw new InvalidOperationException("OptiFine 安装器没有生成版本目录：\n" + Tail(log));

            var producedDir = produced[0];
            var producedId = Path.GetFileName(producedDir);

            progress?.Report(new InstallProgress(80, "复制 OptiFine 依赖…"));
            var libraries = Path.Combine(minecraft, "libraries");
            if (Directory.Exists(libraries)) CopyDirectory(libraries, GamePaths.LibrariesDir);

            var finalName = string.IsNullOrWhiteSpace(instanceName) ? producedId : instanceName.Trim();
            var finalDir = Path.Combine(GamePaths.VersionsDir, finalName);
            CopyDirectory(producedDir, finalDir);
            RenameVersion(finalDir, producedId, finalName);

            progress?.Report(new InstallProgress(92, "OptiFine 安装完成"));
            AppLog.Info($"optifine installed: {finalName} ({build.FileName})");
            return finalName;
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

    private static async Task DownloadInstallerAsync(string gameVersion, OptiFineBuild build,
        string target, CancellationToken cancellationToken)
    {
        // 主路径走 BMCLAPI 的 optifine 转发（会 302 到 maven），失败再用 maven 直链
        var endpoints = new[]
        {
            $"{DownloadSources.Bmclapi}/optifine/{Uri.EscapeDataString(gameVersion)}" +
            $"/{Uri.EscapeDataString(build.Type)}/{Uri.EscapeDataString(build.Patch)}",
            $"{MavenBase}/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(build.FileName)}",
        };

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await DownloadEngine.DownloadAsync(endpoints, target, null, null, null, cancellationToken);
    }

    /// <summary>
    /// 安装器的 getWorkingDirectory() 在 Windows 上是 %APPDATA%\\.minecraft，
    /// 所以把子进程的 APPDATA 指到临时目录，改动不会落到用户真实的 .minecraft。
    /// </summary>
    private static async Task<string> RunInstallerAsync(string java, string installer, string workingDir,
        string appData, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(java)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.Environment["APPDATA"] = appData;

        // 安装器出错时会弹 Swing 对话框，headless 让它直接抛异常退出而不是卡住
        startInfo.ArgumentList.Add("-Djava.awt.headless=true");
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(installer);
        startInfo.ArgumentList.Add("optifine.Installer");

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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 已经退出了
            }

            if (cancellationToken.IsCancellationRequested) throw;

            throw new InvalidOperationException("OptiFine 安装器超时（10 分钟）。\n" + Tail(output.ToString()));
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"OptiFine 安装器退出码 {process.ExitCode}：\n{Tail(output.ToString())}");

        return output.ToString();
    }

    /// <summary>把安装器生成的 &lt;id&gt;.json / &lt;id&gt;.jar 改成实例名。</summary>
    private static void RenameVersion(string dir, string oldId, string newId)
    {
        if (oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)) return;

        var jsonPath = Path.Combine(dir, $"{oldId}.json");
        var newJsonPath = Path.Combine(dir, $"{newId}.json");

        if (File.Exists(jsonPath))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
                if (node is not null) node["id"] = newId;

                File.Delete(jsonPath);
                File.WriteAllText(newJsonPath,
                    node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "rewrite optifine version id");
                File.Move(jsonPath, newJsonPath, overwrite: true);
            }
        }

        var jarPath = Path.Combine(dir, $"{oldId}.jar");
        if (File.Exists(jarPath)) File.Move(jarPath, Path.Combine(dir, $"{newId}.jar"), overwrite: true);
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));

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
