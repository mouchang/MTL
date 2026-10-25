using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MutsumiLauncher.Services;

/// <summary>
/// 按 PCL2 的方式合并版本：把原版 JSON 和加载器 JSON 合成一个独立的版本 JSON，
/// 去掉 inheritsFrom / jar，复制原版 jar 到实例目录，这样实例就是一个完整文件夹，
/// 不再依赖 versions/&lt;原版&gt; 是否存在。
/// </summary>
public static class InstanceMerger
{
    /// <param name="useLoaderJar">
    /// 加载器安装器产出了自己的客户端 jar 时（OptiFine 会打补丁）用它，否则复制原版 jar。
    /// </param>
    public static string Merge(string vanillaId, string loaderId, string instanceName,
        IProgress<InstallProgress>? progress = null, bool useLoaderJar = false)
    {
        var vanillaDir = Path.Combine(GamePaths.VersionsDir, vanillaId);
        var loaderDir = Path.Combine(GamePaths.VersionsDir, loaderId);
        var targetDir = Path.Combine(GamePaths.VersionsDir, instanceName);

        var vanillaJsonPath = Path.Combine(vanillaDir, $"{vanillaId}.json");
        var loaderJsonPath = Path.Combine(loaderDir, $"{loaderId}.json");

        if (!File.Exists(vanillaJsonPath))
            throw new InvalidOperationException($"缺少原版版本文件：{vanillaJsonPath}");

        if (!File.Exists(loaderJsonPath))
            throw new InvalidOperationException($"缺少加载器版本文件：{loaderJsonPath}");

        progress?.Report(new InstallProgress(92, "合并版本文件…"));

        Directory.CreateDirectory(targetDir);

        var vanilla = JsonNode.Parse(File.ReadAllText(vanillaJsonPath)) as JsonObject
                      ?? throw new InvalidOperationException("原版 JSON 解析失败");
        var loader = JsonNode.Parse(File.ReadAllText(loaderJsonPath)) as JsonObject
                     ?? throw new InvalidOperationException("加载器 JSON 解析失败");

        MergeObject(vanilla, loader);

        vanilla.Remove("inheritsFrom");
        vanilla.Remove("jar");
        vanilla.Remove("_comment_");
        vanilla["id"] = instanceName;

        // 记录合并前的原版版本，供「安装新 Mod」等场景还原游戏版本
        vanilla["mtlBaseVersion"] = vanillaId;

        // 有 arguments 对象时，旧式 minecraftArguments 会与启动器自己拼的参数冲突（重复 -cp / 主类）
        if (vanilla["arguments"] is JsonObject)
        {
            vanilla.Remove("minecraftArguments");

            if (vanilla["arguments"] is JsonObject argsObject
                && argsObject["jvm"] is JsonArray jvmArray)
            {
                var filtered = new JsonArray();

                for (var i = 0; i < jvmArray.Count; i++)
                {
                    var item = jvmArray[i];
                    var text = item is JsonValue value && value.TryGetValue<string>(out var str) ? str : null;

                    if (text is "-cp" or "${classpath}" or "-classpath") continue;
                    if (text is not null && text.Contains("${classpath}")) continue;

                    filtered.Add(item?.DeepClone());
                }

                argsObject["jvm"] = filtered;
            }
        }

        File.WriteAllText(Path.Combine(targetDir, $"{instanceName}.json"),
            vanilla.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // 实例 jar：加载器可能产出打过补丁的客户端 jar（OptiFine），否则用原版 jar
        var loaderJar = Path.Combine(loaderDir, $"{loaderId}.jar");
        var sourceJar = useLoaderJar && File.Exists(loaderJar)
            ? loaderJar
            : Path.Combine(vanillaDir, $"{vanillaId}.jar");

        if (File.Exists(sourceJar))
        {
            var targetJar = Path.Combine(targetDir, $"{instanceName}.jar");
            if (!File.Exists(targetJar) || new FileInfo(targetJar).Length != new FileInfo(sourceJar).Length)
                File.Copy(sourceJar, targetJar, overwrite: true);
        }

        // 删除临时的加载器版本目录
        try
        {
            if (Directory.Exists(loaderDir)
                && !loaderDir.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(loaderDir, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete loader temp version");
        }

        AppLog.Info($"instance merged: {instanceName} (vanilla={vanillaId}, loader={loaderId})");
        progress?.Report(new InstallProgress(96, "版本合并完成"));
        return instanceName;
    }

    /// <summary>PCL2 的合并规则：对象递归合并（加载器覆盖原版），数组以原版在前、加载器在后拼接。</summary>
    private static void MergeObject(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source.ToList())
        {
            if (key is "releaseTime" or "time") continue;

            if (value is JsonObject sourceObject
                && target[key] is JsonObject targetObject)
            {
                MergeObject(targetObject, sourceObject);
                continue;
            }

            if (value is JsonArray sourceArray
                && target[key] is JsonArray targetArray)
            {
                foreach (var item in sourceArray.ToList())
                    targetArray.Add(item?.DeepClone());

                continue;
            }

            target[key] = value?.DeepClone();
        }
    }
}
