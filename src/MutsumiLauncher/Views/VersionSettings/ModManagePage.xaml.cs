using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class ModManagePage : UserControl, IInstancePage
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x5B, 0x8C, 0x5A),
        Color.FromRgb(0x4F, 0x7E, 0xA8),
        Color.FromRgb(0xC9, 0x8A, 0x5B),
        Color.FromRgb(0x8C, 0x5B, 0xA8),
        Color.FromRgb(0xA8, 0x5B, 0x6D),
        Color.FromRgb(0x5B, 0xA8, 0x99),
    ];

    private string _version = string.Empty;
    private string _filter = "all";
    private string _query = string.Empty;
    private List<ModFileItem> _items = [];

    public ModManagePage()
    {
        InitializeComponent();
    }

    public void SetInstance(string version)
    {
        _version = version;

        var info = InstanceService.GetInfo(version);
        var supported = !info.Loader.Equals("原版", StringComparison.OrdinalIgnoreCase);

        NoLoaderHint.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
        InstallNewModButton.IsEnabled = supported;
        InstallNewModButton.ToolTip = supported ? null : "原版没有加载器，无法安装 Mod";

        Refresh();
    }

    private string ModsDir => InstanceService.ModsDirFor(_version);

    public void Refresh()
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var dir = ModsDir;
        Directory.CreateDirectory(dir);

        _items = [];

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                bool enabled;

                if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    enabled = true;
                else if (name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                    enabled = false;
                else
                    continue;

                var display = enabled
                    ? Path.GetFileNameWithoutExtension(name)
                    : Path.GetFileNameWithoutExtension(name[..^".disabled".Length]);

                var size = 0L;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch
                {
                    // 忽略单个文件读取失败
                }

                _items.Add(new ModFileItem
                {
                    FileName = name,
                    DisplayName = display,
                    FullPath = file,
                    SizeText = InstanceService.FormatSize(size),
                    Enabled = enabled,
                    Initial = Initials(display),
                    IconBrush = BrushFor(display),
                });
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "scan mods");
        }

        _items = _items
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var enabledCount = _items.Count(item => item.Enabled);
        AllFilterText.Text = $"全部 ({_items.Count})";
        EnabledFilterText.Text = $"启用 ({enabledCount})";
        DisabledFilterText.Text = $"禁用 ({_items.Count - enabledCount})";

        ApplyFilter();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this) as MainWindow;

        if (string.IsNullOrWhiteSpace(_version)) return;

        var info = InstanceService.GetInfo(_version);
        if (info.Loader.Equals("原版", StringComparison.OrdinalIgnoreCase))
        {
            owner?.ShowToast("原版实例没有加载器，无法检查 Mod 更新。");
            return;
        }

        var sha1Map = new Dictionary<string, ModFileItem>(StringComparer.OrdinalIgnoreCase);
        var fingerprintMap = new Dictionary<uint, ModFileItem>();

        await Task.Run(() =>
        {
            foreach (var item in _items)
            {
                try
                {
                    sha1Map[Sha1Of(item.FullPath)] = item;
                    fingerprintMap[CurseForgeClient.ComputeFingerprint(item.FullPath)] = item;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "hash mod " + item.FileName);
                }
            }
        });

        if (sha1Map.Count == 0)
        {
            owner?.ShowToast("没有可检查的 Mod。");
            return;
        }

        owner?.ShowToast("正在检查 Mod 更新…");
        var found = 0;

        // Modrinth：SHA-1 反查
        try
        {
            var updates = await new ModrinthClient().CheckUpdatesAsync(sha1Map.Keys.ToList(),
                info.GameVersion, info.Loader.ToLowerInvariant());

            foreach (var (hash, version) in updates)
            {
                if (sha1Map.TryGetValue(hash, out var item) && MarkUpdate(item, version)) found++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "modrinth update check");
        }

        // CurseForge：指纹反查 + 最新文件
        try
        {
            var matches = await new CurseForgeClient().MatchFingerprintsAsync(fingerprintMap.Keys.ToList());

            foreach (var match in matches)
            {
                if (!fingerprintMap.TryGetValue(FindFingerprint(fingerprintMap, match), out var item))
                    continue;

                var files = await new CurseForgeClient().GetFilesAsync(match.ModId,
                    info.GameVersion, LoaderType(info.Loader));

                var latest = files.FirstOrDefault(file => file.Id != match.FileId);
                if (latest is null) continue;

                var version = new ModrinthVersion
                {
                    Name = latest.DisplayName,
                    VersionNumber = latest.DisplayName,
                    VersionType = latest.ReleaseType,
                    DatePublished = latest.FileDate,
                    GameVersions = latest.GameVersions.ToList(),
                    Loaders = latest.Loaders.ToList(),
                    Files =
                    [
                        new ModrinthFile
                        {
                            Url = latest.DownloadUrl,
                            Filename = latest.FileName,
                            Primary = true,
                            Hashes = new ModrinthHashes { Sha1 = latest.Sha1 },
                        },
                    ],
                };

                if (MarkUpdate(item, version)) found++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "curseforge update check");
        }

        owner?.ShowToast(found == 0 ? "所有 Mod 都是最新的。" : $"发现 {found} 个可更新的 Mod。");
    }

    private static bool MarkUpdate(ModFileItem item, ModrinthVersion version)
    {
        var current = item.Update;

        if (current is not null && current.DatePublished >= version.DatePublished)
            return false;

        item.SetUpdate(version);
        return true;
    }

    private static uint FindFingerprint(Dictionary<uint, ModFileItem> map, CurseForgeFingerprintMatch match)
    {
        foreach (var (fingerprint, item) in map)
        {
            if (item.FileName.Equals(match.FileName, StringComparison.OrdinalIgnoreCase))
                return fingerprint;
        }

        return 0;
    }

    private static int? LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        "forge" => 1,
        _ => null,
    };

    private async void UpdateMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item } || item.Update is not { } version)
            return;

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file is null)
        {
            (Window.GetWindow(this) as MainWindow)?.ShowToast("这个版本没有可下载的文件。");
            return;
        }

        var owner = Window.GetWindow(this) as MainWindow;

        try
        {
            await ResourceService.DownloadAsync(file, ModsDir);

            // 文件名变化时删掉旧 jar，避免同时存在两个版本
            if (!string.Equals(file.Filename, item.FileName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(item.FullPath)) File.Delete(item.FullPath);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "delete old mod " + item.FileName);
                }
            }

            owner?.ShowToast($"已更新 {item.DisplayName} → {version.VersionNumber}");
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "update mod " + item.FileName);
            owner?.ShowToast("更新失败：" + ex.Message);
        }
    }

    private static string Sha1Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    private void ApplyFilter()
    {
        if (ModList is null) return;

        IEnumerable<ModFileItem> items = _items;

        items = _filter switch
        {
            "enabled" => items.Where(item => item.Enabled),
            "disabled" => items.Where(item => !item.Enabled),
            _ => items,
        };

        if (_query.Length > 0)
            items = items.Where(item =>
                item.DisplayName.Contains(_query, StringComparison.OrdinalIgnoreCase));

        var list = items.ToList();
        ModList.ItemsSource = list;
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _items.Count == 0
            ? "这个实例还没有安装 Mod。"
            : "没有符合条件的 Mod。";
    }

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        _query = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string filter }) return;
        _filter = filter;
        ApplyFilter();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void InstallNewMod_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var info = InstanceService.GetInfo(_version);
        var gameVersion = info.GameVersion;

        var loader = info.Loader switch
        {
            "Fabric" => "fabric",
            "Forge" => "forge",
            "NeoForge" => "neoforge",
            _ => null,
        };

        (Window.GetWindow(this) as MainWindow)?.OpenCommunityMod(_version, gameVersion, loader);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        (Window.GetWindow(this) as MainWindow)?.OpenFolderBrowser(ModsDir, "Mod 文件夹");
    }

    private void InstallFromFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要安装的 Mod",
            Filter = "Mod 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true) return;

        var dir = ModsDir;
        Directory.CreateDirectory(dir);

        var copied = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var target = Path.Combine(dir, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "copy mod");
                Toast("复制失败：" + ex.Message);
            }
        }

        Refresh();
        if (copied > 0) Toast($"已安装 {copied} 个 Mod");
    }

    private void ToggleMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item }) return;

        try
        {
            if (item.Enabled)
            {
                var target = item.FullPath + ".disabled";
                if (File.Exists(target)) File.Delete(target);
                File.Move(item.FullPath, target);
            }
            else
            {
                var target = item.FullPath[..^".disabled".Length];
                if (File.Exists(target)) File.Delete(target);
                File.Move(item.FullPath, target);
            }

            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "toggle mod");
            Toast("操作失败：" + ex.Message);
        }
    }

    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModFileItem item }) return;

        var owner = Window.GetWindow(this);
        if (!MessageDialog.Show(owner, "删除 Mod", $"确定删除「{item.DisplayName}」吗？", "删除")) return;

        try
        {
            File.Delete(item.FullPath);
            Refresh();
            Toast("已删除 " + item.DisplayName);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete mod");
            Toast("删除失败：" + ex.Message);
        }
    }

    private static string Initials(string name)
    {
        var cleaned = name.Trim();
        if (cleaned.Length == 0) return "M";

        var first = char.ToUpperInvariant(cleaned[0]);
        return first.ToString();
    }

    private static Brush BrushFor(string name)
    {
        var hash = name.Aggregate(17, (current, c) => current * 31 + c);
        var index = Math.Abs(hash) % Palette.Length;
        return new SolidColorBrush(Palette[index]);
    }

    private void Toast(string message)
        => (Window.GetWindow(this) as MainWindow)?.ShowToast(message);
}
