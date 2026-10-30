using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// 应用数据目录（配置 / 缓存 / 图标 / 日志）。
///
/// 默认放在**安装目录**下的 data\，不再写 %LOCALAPPDATA%：
/// 便携版和安装版的数据都跟程序在一起，卸载时一并处理，C 盘不留东西。
///
/// 想让数据放到别处时，改安装目录下的 data-root.txt（设置页会自动写），
/// 里面写一行目标路径即可 —— 这个文件必须在读配置之前就能定位新位置。
/// </summary>
public static class AppPaths
{
    /// <summary>安装目录下的默认数据位置。</summary>
    public static string DefaultDataRoot { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>指向自定义数据目录的指针文件（一行路径）。</summary>
    public static string PointerFile { get; } = Path.Combine(AppContext.BaseDirectory, "data-root.txt");

    /// <summary>老版本放数据的目录：只在迁移时读取，不再写入。</summary>
    public static string LegacyDataRoot
    {
        get
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return string.IsNullOrWhiteSpace(local)
                    ? string.Empty
                    : Path.Combine(local, "Mutsumi Launcher");
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public static string DataRoot { get; private set; } = Resolve();

    public static string CacheDir => Path.Combine(DataRoot, "cache");

    public static string IconsDir => Path.Combine(DataRoot, "icons");

    public static string LogPath => Path.Combine(DataRoot, "logs", "app.log");

    /// <summary>把数据目录指到别处（写指针文件），重启后生效。</summary>
    public static void SetDataRoot(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) ||
                Path.GetFullPath(path).Equals(Path.GetFullPath(DefaultDataRoot), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(PointerFile)) File.Delete(PointerFile);
            }
            else
            {
                File.WriteAllText(PointerFile, path.Trim());
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "write data root pointer");
        }

        DataRoot = Resolve();
    }

    private static string Resolve()
    {
        // 1. 指针文件优先
        try
        {
            if (File.Exists(PointerFile))
            {
                var custom = File.ReadAllText(PointerFile).Trim().Trim('"');

                if (custom.Length > 0 && TryCreate(custom)) return custom;
            }
        }
        catch
        {
            // 指针坏了就走默认
        }

        // 2. 安装目录下的 data
        TryCreate(DefaultDataRoot);
        return DefaultDataRoot;
    }

    private static bool TryCreate(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);

            // CreateDirectory 成功不代表能写文件
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
