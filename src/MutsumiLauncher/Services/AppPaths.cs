using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>应用数据目录：优先 %LOCALAPPDATA%，不可用时退回程序目录下的 data\。</summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Resolve();

    public static string CacheDir => Path.Combine(DataRoot, "cache");

    public static string LogPath => Path.Combine(DataRoot, "logs", "app.log");

    private static string Resolve()
    {
        string? localDir = null;

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                localDir = Path.Combine(local, "Mutsumi Launcher");
                if (IsWritable(localDir)) return localDir;
            }
        }
        catch
        {
            // 继续走兜底
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(fallback);

        // 旧目录可能只是不可写但仍可读，把配置搬过来，避免设置丢失
        if (localDir is not null)
        {
            foreach (var file in new[] { "config.json", "accounts.json" })
            {
                try
                {
                    var source = Path.Combine(localDir, file);
                    var target = Path.Combine(fallback, file);

                    if (File.Exists(source) && !File.Exists(target))
                        File.Copy(source, target, overwrite: false);
                }
                catch
                {
                    // 读不到就算了
                }
            }
        }

        return fallback;
    }

    /// <summary>目录存在且真的能写文件（CreateDirectory 成功不代表可写）。</summary>
    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);

            var probe = Path.Combine(dir, ".mtl-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
