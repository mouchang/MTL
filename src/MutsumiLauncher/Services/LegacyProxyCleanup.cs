using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 旧版本自带 mihomo 代理内核，1.1.1-beta.28 起移除。
/// 升级上来的用户目录里还留着内核二进制和含节点凭据的配置，启动时清掉。
/// </summary>
public static class LegacyProxyCleanup
{
    public static void Run()
    {
        try
        {
            DeleteDirectory(Path.Combine(AppPaths.DataRoot, "proxy"));

            // 安装包自带的 core/mihomo.zip（新版本不再打包）
            var coreDir = Path.Combine(AppContext.BaseDirectory, "core");

            foreach (var file in new[] { "mihomo.zip", "mihomo.exe", "mihomo.zip.part" })
            {
                var path = Path.Combine(coreDir, file);

                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "legacy proxy cleanup " + path);
                }
            }

            if (Directory.Exists(coreDir) && !Directory.EnumerateFileSystemEntries(coreDir).Any())
                Directory.Delete(coreDir);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "legacy proxy cleanup");
        }
    }

    private static void DeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        try
        {
            Directory.Delete(dir, recursive: true);
            AppLog.Info("legacy proxy cleanup: 已删除 " + dir);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "legacy proxy cleanup " + dir);
        }
    }
}
