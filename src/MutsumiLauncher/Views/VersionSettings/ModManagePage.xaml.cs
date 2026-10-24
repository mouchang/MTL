using System.IO;
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
