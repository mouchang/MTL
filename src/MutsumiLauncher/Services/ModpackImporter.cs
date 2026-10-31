using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MutsumiLauncher.Services;

/// <summary>
/// 整合包导入：Modrinth（modrinth.index.json）与 CurseForge（manifest.json）。
/// 统一走「原版 + 加载器合并实例 → 下载文件 → 解压 overrides」。
/// </summary>
public static class ModpackImporter
{
    public static async Task<string> ImportAsync(string filePath,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("找不到整合包文件", filePath);

        using var archive = ZipFile.OpenRead(filePath);

        var modrinth = archive.GetEntry("modrinth.index.json");
        if (modrinth is not null)
            return await ImportModrinthAsync(archive, modrinth, filePath, progress, cancellationToken);

        var mmc = archive.GetEntry("mmc-pack.json");
        if (mmc is not null)
            return await ImportMmcAsync(archive, mmc, filePath, progress, cancellationToken);

        var curseForge = archive.GetEntry("manifest.json");
        if (curseForge is not null && await IsCurseForgeManifestAsync(curseForge, cancellationToken))
            return await ImportCurseForgeAsync(archive, curseForge, filePath, progress, cancellationToken);

        if (archive.GetEntry("modpack.json") is not null)
            throw new InvalidOperationException("暂不支持 HMCL 整合包，目前支持 .mrpack / CurseForge / MMC。");

        throw new InvalidOperationException("无法识别整合包格式（支持 Modrinth .mrpack 与 CurseForge）。");
    }

    private static async Task<bool> IsCurseForgeManifestAsync(ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = entry.Open();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            return root.TryGetProperty("manifestType", out var type)
                   && type.GetString()?.Contains("Modpack", StringComparison.OrdinalIgnoreCase) == true
                   && root.TryGetProperty("minecraft", out _)
                   && root.TryGetProperty("files", out _);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ImportModrinthAsync(ZipArchive archive, ZipArchiveEntry indexEntry,
        string filePath, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var indexStream = indexEntry.Open();
        using var index = await JsonDocument.ParseAsync(indexStream, cancellationToken: cancellationToken);
        var root = index.RootElement;

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var dependencies = root.TryGetProperty("dependencies", out var dep) ? dep : default;

        string? Dep(string key) =>
            dependencies.ValueKind == JsonValueKind.Object
            && dependencies.TryGetProperty(key, out var value)
                ? value.GetString()
                : null;

        var minecraft = Dep("minecraft")
                        ?? throw new InvalidOperationException("整合包缺少 minecraft 依赖版本。");

        progress?.Report(new InstallProgress(2, $"读取整合包：{name ?? Path.GetFileName(filePath)}"));

        var (installed, gameDir) = await InstallBaseAsync(minecraft, Dep("fabric-loader"),
            Dep("forge"), Dep("neoforge"), name, progress, cancellationToken);

        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            var list = files.EnumerateArray().ToList();
            var total = Math.Max(1, list.Count);
            var done = 0;

            // 整合包动辄一两百个文件，串行下载每个都要重新握手，慢得离谱。
            // PCL2 也是并发拉，这里开 6 条，速度能翻好几倍。
            using var gate = new SemaphoreSlim(6);

            var tasks = list.Select(async item =>
            {
                var clientEnv = item.TryGetProperty("env", out var env)
                                && env.ValueKind == JsonValueKind.Object
                                && env.TryGetProperty("client", out var client)
                    ? client.GetString()
                    : null;

                if (string.Equals(clientEnv, "unsupported", StringComparison.OrdinalIgnoreCase)) return;

                var path = item.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(path)) return;

                var url = item.TryGetProperty("downloads", out var downloads)
                          && downloads.ValueKind == JsonValueKind.Array
                    ? downloads.EnumerateArray()
                        .Select(value => value.GetString())
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    : null;

                if (string.IsNullOrWhiteSpace(url)) return;

                string? sha1 = null;
                if (item.TryGetProperty("hashes", out var hashes)
                    && hashes.ValueKind == JsonValueKind.Object
                    && hashes.TryGetProperty("sha1", out var sha1Element))
                    sha1 = sha1Element.GetString();

                var target = Path.Combine(gameDir, path.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(target)
                    && (sha1 is null || await MatchesAsync(target, sha1, cancellationToken)))
                {
                    var skipped = Interlocked.Increment(ref done);
                    progress?.Report(new InstallProgress(
                        62 + 30.0 * skipped / total, $"整合包文件 {skipped}/{total}…"));
                    return;
                }

                await gate.WaitAsync(cancellationToken);

                try
                {
                    await DownloadWithHashAsync(url!, target, sha1, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }

                var current = Interlocked.Increment(ref done);
                progress?.Report(new InstallProgress(
                    62 + 30.0 * current / total, $"下载整合包文件 {current}/{total}…"));
            });

            await Task.WhenAll(tasks);
        }

        ExtractOverrides(archive, "overrides/", gameDir, progress);
        Finish(filePath, installed, progress);
        return installed;
    }

    private static async Task<string> ImportMmcAsync(ZipArchive archive, ZipArchiveEntry packEntry,
        string filePath, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var stream = packEntry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var minecraft = string.Empty;
        string? fabric = null;
        string? forge = null;
        string? neoforge = null;

        if (document.RootElement.TryGetProperty("components", out var components)
            && components.ValueKind == JsonValueKind.Array)
        {
            foreach (var component in components.EnumerateArray())
            {
                var uid = component.TryGetProperty("uid", out var uidElement)
                    ? uidElement.GetString() ?? string.Empty
                    : string.Empty;
                var version = component.TryGetProperty("version", out var versionElement)
                    ? versionElement.GetString() ?? string.Empty
                    : string.Empty;

                switch (uid.ToLowerInvariant())
                {
                    case "net.minecraft": minecraft = version; break;
                    case "net.fabricmc.fabric-loader": fabric = version; break;
                    case "net.minecraftforge": forge = version; break;
                    case "net.neoforged":
                    case "net.neoforged.neoforge": neoforge = version; break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(minecraft))
            throw new InvalidOperationException("MMC 整合包缺少 net.minecraft 组件版本。");

        var instanceName = ReadMmcInstanceName(archive) ?? Path.GetFileNameWithoutExtension(filePath);
        progress?.Report(new InstallProgress(2, $"读取整合包：{instanceName}"));

        var (installed, gameDir) = await InstallBaseAsync(minecraft, fabric, forge, neoforge,
            instanceName, progress, cancellationToken);

        ExtractMmcGameFolder(archive, gameDir, progress);
        Finish(filePath, installed, progress);
        return installed;
    }

    private static string? ReadMmcInstanceName(ZipArchive archive)
    {
        var cfg = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith("instance.cfg", StringComparison.OrdinalIgnoreCase));

        if (cfg is null) return null;

        try
        {
            using var reader = new StreamReader(cfg.Open());
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                    return line["name=".Length..].Trim();
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }

    private static void ExtractMmcGameFolder(ZipArchive archive, string gameDir,
        IProgress<InstallProgress>? progress)
    {
        progress?.Report(new InstallProgress(70, "解压 MMC 游戏文件…"));

        var prefix = archive.Entries
            .Select(entry => entry.FullName)
            .FirstOrDefault(name => name.StartsWith("minecraft/", StringComparison.OrdinalIgnoreCase)
                                    || name.StartsWith(".minecraft/", StringComparison.OrdinalIgnoreCase))
            ?.Split('/')[0];

        if (prefix is null) return;

        prefix += "/";

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var relative = entry.FullName[prefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(gameDir, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static async Task<string> ImportCurseForgeAsync(ZipArchive archive, ZipArchiveEntry manifestEntry,
        string filePath, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var stream = manifestEntry.Open();
        using var manifest = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = manifest.RootElement;

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;

        var mineElement = root.TryGetProperty("minecraft", out var mine)
            ? mine
            : default;

        var minecraft = mineElement.ValueKind == JsonValueKind.Object
                        && mineElement.TryGetProperty("version", out var versionElement)
            ? versionElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(minecraft))
            throw new InvalidOperationException("CurseForge 整合包缺少 Minecraft 版本。");

        string? fabric = null;
        string? forge = null;
        string? neoforge = null;

        if (mineElement.ValueKind == JsonValueKind.Object
            && mineElement.TryGetProperty("modLoaders", out var loaders)
            && loaders.ValueKind == JsonValueKind.Array)
        {
            var loader = loaders.EnumerateArray()
                .OrderByDescending(item => item.TryGetProperty("primary", out var primary)
                                           && primary.ValueKind == JsonValueKind.True)
                .FirstOrDefault();

            var id = loader.ValueKind == JsonValueKind.Object
                ? loader.TryGetProperty("id", out var idElement) ? idElement.GetString() : null
                : null;

            if (!string.IsNullOrWhiteSpace(id))
            {
                var dash = id.IndexOf('-');
                var kind = dash > 0 ? id[..dash] : id;
                var version = dash > 0 ? id[(dash + 1)..] : string.Empty;

                switch (kind.ToLowerInvariant())
                {
                    case "fabric": fabric = version; break;
                    case "forge": forge = version; break;
                    case "neoforge": neoforge = version; break;
                }
            }
        }

        progress?.Report(new InstallProgress(2,
            $"读取整合包：{name ?? Path.GetFileName(filePath)}"));

        var (installed, gameDir) = await InstallBaseAsync(minecraft, fabric, forge, neoforge, name,
            progress, cancellationToken);

        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            var list = files.EnumerateArray()
                .Where(item => !item.TryGetProperty("required", out var required)
                               || required.ValueKind != JsonValueKind.False)
                .ToList();

            var client = new CurseForgeClient();
            var modsDir = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(modsDir);

            // CurseForge 的包每个文件都要先查一次 API 再下载。API 并发太高会被限流
            // （返回 null），所以查询单独限到 2 路；每个文件最多重试 3 次；
            // 最后只要有一个文件没下到就整体报错——静默漏装会让整合包启动直接崩。
            var done = 0;
            var total = Math.Max(1, list.Count);
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

            using var apiGate = new SemaphoreSlim(2);
            using var fileGate = new SemaphoreSlim(6);

            var tasks = list.Select(async item =>
            {
                var projectId = item.TryGetProperty("projectID", out var projectElement)
                    ? projectElement.GetInt32()
                    : 0;
                var fileId = item.TryGetProperty("fileID", out var fileElement)
                    ? fileElement.GetInt32()
                    : 0;

                if (projectId == 0 || fileId == 0) return;

                try
                {
                    CurseForgeFile? file = null;

                    for (var attempt = 0; attempt < 3 && file is null; attempt++)
                    {
                        await apiGate.WaitAsync(cancellationToken);

                        try
                        {
                            file = await client.GetFileAsync(projectId, fileId, cancellationToken);
                        }
                        finally
                        {
                            apiGate.Release();
                        }

                        if (file is null) await Task.Delay(400 * (attempt + 1), cancellationToken);
                    }

                    if (file is null)
                    {
                        failures.Add($"#{projectId}/{fileId}（查询失败）");
                        return;
                    }

                    var target = Path.Combine(modsDir, file.FileName);

                    if (File.Exists(target) && new FileInfo(target).Length == file.Length) return;

                    await fileGate.WaitAsync(cancellationToken);

                    try
                    {
                        await DownloadEngine.DownloadAsync(DownloadSources.ModFile(file.DownloadUrl),
                            target, file.Sha1, file.Length, null, cancellationToken);
                    }
                    finally
                    {
                        fileGate.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"curseforge pack file {projectId}/{fileId}");
                    failures.Add($"{projectId}/{fileId}（{ex.Message}）");
                }
                finally
                {
                    var current = Interlocked.Increment(ref done);
                    progress?.Report(new InstallProgress(62 + 30.0 * current / total,
                        $"下载整合包文件 {current}/{total}…"));
                }
            });

            await Task.WhenAll(tasks);

            if (!failures.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"有 {failures.Count} 个文件没能下载，整合包不完整（启动会崩溃）：" +
                    string.Join("、", failures.Take(3)));
            }
        }

        ExtractOverrides(archive, "overrides/", gameDir, progress);
        ExtractOverrides(archive, "client-overrides/", gameDir, progress);
        Finish(filePath, installed, progress);
        return installed;
    }

    private static async Task<(string Installed, string GameDir)> InstallBaseAsync(string minecraft,
        string? fabric, string? forge, string? neoforge, string? name,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var manifestService = new VersionManifestService();
        var (manifest, _) = await manifestService.GetAsync(cancellationToken: cancellationToken);
        var versionEntry = manifest.Versions.FirstOrDefault(v => v.Id == minecraft)
                           ?? throw new InvalidOperationException($"版本清单里没有 Minecraft {minecraft}。");

        var vanillaJson = Path.Combine(GamePaths.VersionsDir, minecraft, $"{minecraft}.json");
        var vanillaWasMissing = !File.Exists(vanillaJson);

        if (vanillaWasMissing)
        {
            progress?.Report(new InstallProgress(5, $"安装原版 {minecraft}…"));
            await new VersionInstaller().InstallAsync(minecraft, versionEntry.Url,
                Map(progress, 5, 40), cancellationToken: cancellationToken);
        }

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
                loaderId = await new ForgeLoader().InstallAsync(LoaderKind.NeoForge, minecraft,
                    neoforge!, Map(progress, 42, 60), tempLoaderId, cancellationToken);
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

        return (installed, gameDir);
    }

    private static void ExtractOverrides(ZipArchive archive, string prefix, string gameDir,
        IProgress<InstallProgress>? progress)
    {
        progress?.Report(new InstallProgress(92, "解压 overrides…"));

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var relative = entry.FullName[prefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(gameDir, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void Finish(string filePath, string installed, IProgress<InstallProgress>? progress)
    {
        var config = AppConfig.Current;
        config.SelectedVersion = installed;
        config.Save();

        progress?.Report(new InstallProgress(100, $"整合包导入完成：{installed}"));
        AppLog.Info($"modpack imported: {filePath} -> {installed}");

        // 装完就把安装包删掉：几百 MB 的 .mrpack / .zip 留在那只是占地方。
        // 只删压缩包本体，失败也只记日志，不影响已经装好的实例。
        DeleteInstaller(filePath);
    }

    private static void DeleteInstaller(string path)
    {
        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension is not (".mrpack" or ".zip" or ".jar" or ".mcpack")) return;
            if (!File.Exists(path)) return;

            var size = new FileInfo(path).Length;
            File.Delete(path);

            AppLog.Info($"modpack: 已删除安装包 {Path.GetFileName(path)}（{PathMigrator.FormatSize(size)}）");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete modpack installer " + path);
        }
    }

    private static IProgress<InstallProgress>? Map(IProgress<InstallProgress>? parent, double from, double to)
        => parent is null
            ? null
            : new Progress<InstallProgress>(p =>
                parent.Report(new InstallProgress(
                    from + (to - from) * Math.Clamp(p.Percent, 0, 100) / 100.0, p.Message)));

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

    private static async Task<bool> MatchesAsync(string path, string sha1,
        CancellationToken cancellationToken)
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

        var endpoints = DownloadSources.ModFile(officialUrl);
        await DownloadEngine.DownloadAsync(endpoints, targetPath, sha1, null, null, cancellationToken);
    }
}