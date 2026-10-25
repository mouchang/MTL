using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>导入 .mrpack（Modrinth）整合包：装原版 + 加载器 + 下载文件 + 解压 overrides。</summary>
public static class ModpackImporter
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromMinutes(10));

    public static async Task<string> ImportAsync(string filePath, IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("找不到整合包文件", filePath);

        using var archive = ZipFile.OpenRead(filePath);

        var indexEntry = archive.GetEntry("modrinth.index.json")
                         ?? throw new InvalidOperationException("不是有效的 Modrinth 整合包（缺少 modrinth.index.json）");

        using var indexStream = indexEntry.Open();
        using var index = await JsonDocument.ParseAsync(indexStream, cancellationToken: cancellationToken);
        var root = index.RootElement;

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var versionId = root.TryGetProperty("versionId", out var versionElement)
            ? versionElement.GetString()
            : null;

        var dependencies = root.TryGetProperty("dependencies", out var dep) ? dep : default;

        string? Dep(string key) =>
            dependencies.ValueKind == JsonValueKind.Object
            && dependencies.TryGetProperty(key, out var value)
                ? value.GetString()
                : null;

        var minecraft = Dep("minecraft")
                        ?? throw new InvalidOperationException("整合包缺少 minecraft 依赖版本。");

        var fabric = Dep("fabric-loader");
        var forge = Dep("forge");
        var neoforge = Dep("neoforge");

        progress?.Report(new InstallProgress(2, $"读取整合包：{name ?? Path.GetFileName(filePath)}"));

        // 1) 原版
        var manifestService = new VersionManifestService();
        var (manifest, _) = await manifestService.GetAsync(cancellationToken: cancellationToken);
        var versionEntry = manifest.Versions.FirstOrDefault(v => v.Id == minecraft)
                           ?? throw new InvalidOperationException($"版本清单里没有 Minecraft {minecraft}。");

        var vanillaJson = Path.Combine(GamePaths.VersionsDir, minecraft, $"{minecraft}.json");
        var vanillaWasMissing = !File.Exists(vanillaJson);

        if (vanillaWasMissing)
        {
            progress?.Report(new InstallProgress(5, $"安装原版 {minecraft}…"));
            var vanillaProgress = Map(progress, 5, 40);
            await new VersionInstaller().InstallAsync(minecraft, versionEntry.Url, vanillaProgress);
        }

        // 2) 加载器
        var instanceName = UniqueName(SafeName(string.IsNullOrWhiteSpace(name) ? "整合包" : name!));

        string installed;
        if (!string.IsNullOrWhiteSpace(fabric) || !string.IsNullOrWhiteSpace(forge)
            || !string.IsNullOrWhiteSpace(neoforge))
        {
            var tempLoaderId = "mtl-merge-" + Guid.NewGuid().ToString("N")[..8];
            string loaderId;

            if (!string.IsNullOrWhiteSpace(fabric))
            {
                progress?.Report(new InstallProgress(42, $"安装 Fabric {fabric}…"));
                loaderId = await new FabricLoader().InstallAsync(minecraft, fabric!,
                    Map(progress, 42, 60), tempLoaderId, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(forge))
            {
                progress?.Report(new InstallProgress(42, $"安装 Forge {forge}…"));
                loaderId = await new ForgeLoader().InstallAsync(LoaderKind.Forge, minecraft, forge!,
                    Map(progress, 42, 60), tempLoaderId, cancellationToken);
            }
            else
            {
                progress?.Report(new InstallProgress(42, $"安装 NeoForge {neoforge}…"));
                loaderId = await new ForgeLoader().InstallAsync(LoaderKind.NeoForge, minecraft, neoforge!,
                    Map(progress, 42, 60), tempLoaderId, cancellationToken);
            }

            installed = InstanceMerger.Merge(minecraft, loaderId, instanceName, Map(progress, 60, 62));

            if (vanillaWasMissing && !instanceName.Equals(minecraft, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var vanillaDir = Path.Combine(GamePaths.VersionsDir, minecraft);
                    if (Directory.Exists(vanillaDir)) Directory.Delete(vanillaDir, recursive: true);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "delete temp vanilla");
                }
            }
        }
        else
        {
            progress?.Report(new InstallProgress(45, "没有加载器依赖，使用原版实例。"));
            instanceName = minecraft;
            installed = minecraft;
        }

        var gameDir = InstanceService.GameDirFor(installed);
        Directory.CreateDirectory(gameDir);

        // 3) 下载 files[]
        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            var list = files.EnumerateArray().ToList();
            var total = Math.Max(1, list.Count);

            for (var i = 0; i < list.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = list[i];

                var clientEnv = item.TryGetProperty("env", out var env)
                                && env.ValueKind == JsonValueKind.Object
                                && env.TryGetProperty("client", out var client) ? client.GetString() : null;

                if (string.Equals(clientEnv, "unsupported", StringComparison.OrdinalIgnoreCase)) continue;

                var path = item.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var url = item.TryGetProperty("downloads", out var downloads)
                          && downloads.ValueKind == JsonValueKind.Array
                    ? downloads.EnumerateArray().Select(d => d.GetString()).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                    : null;

                if (string.IsNullOrWhiteSpace(url)) continue;

                string? sha1 = null;
                if (item.TryGetProperty("hashes", out var hashes)
                    && hashes.ValueKind == JsonValueKind.Object
                    && hashes.TryGetProperty("sha1", out var sha1Element))
                    sha1 = sha1Element.GetString();

                var target = Path.Combine(gameDir, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(target) && (sha1 is null || await MatchesAsync(target, sha1, cancellationToken)))
                    continue;

                var percent = 60 + 30.0 * i / total;
                progress?.Report(new InstallProgress(percent,
                    $"下载整合包文件 {i + 1}/{list.Count}…"));

                await DownloadWithHashAsync(url!, target, sha1, cancellationToken);
            }
        }

        // 4) overrides
        progress?.Report(new InstallProgress(92, "解压 overrides…"));

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var relative = entry.FullName["overrides/".Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(gameDir, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        // 5) 选中新实例
        var config = AppConfig.Current;
        config.SelectedVersion = installed;
        config.Save();

        progress?.Report(new InstallProgress(100, $"整合包导入完成：{installed}"));
        AppLog.Info($"modpack imported: {filePath} -> {installed}");

        return installed;
    }

    private static IProgress<InstallProgress>? Map(IProgress<InstallProgress>? parent, double from, double to)
        => parent is null
            ? null
            : new Progress<InstallProgress>(p =>
                parent.Report(new InstallProgress(from + (to - from) * Math.Clamp(p.Percent, 0, 100) / 100.0,
                    p.Message)));

    private static string UniqueName(string baseName)
    {
        var name = baseName;
        var index = 2;

        while (Directory.Exists(Path.Combine(GamePaths.VersionsDir, name)))
            name = $"{baseName}-{index++}";

        return name;
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "整合包";
        return cleaned.Length > 48 ? cleaned[..48] : cleaned;
    }

    private static async Task<bool> MatchesAsync(string path, string sha1, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var hash = await SHA1.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).Equals(sha1, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task DownloadWithHashAsync(string officialUrl, string targetPath, string? sha1,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var mirrored = DownloadMirror.ModFileUrl(officialUrl);
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(mirrored)) candidates.Add(mirrored);
        if (!candidates.Contains(officialUrl, StringComparer.OrdinalIgnoreCase))
            candidates.Add(officialUrl);

        Exception? lastError = null;

        foreach (var candidate in candidates)
        {
            var temp = $"{targetPath}.{Guid.NewGuid():N}.tmp";

            try
            {
                using (var response = await Http.GetAsync(candidate,
                           HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var file = File.Create(temp);
                    await source.CopyToAsync(file, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(sha1) && !await MatchesAsync(temp, sha1, cancellationToken))
                {
                    lastError = new InvalidDataException($"校验失败：{Path.GetFileName(targetPath)}");
                    AppLog.Info($"modpack file checksum mismatch, url={candidate}");
                    continue;
                }

                File.Move(temp, targetPath, overwrite: true);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                AppLog.Error(ex, $"modpack download {candidate}");
            }
            finally
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                    // 忽略
                }
            }
        }

        throw new InvalidDataException(
            $"整合包文件下载失败：{Path.GetFileName(targetPath)}（{lastError?.Message}）", lastError);
    }
}
