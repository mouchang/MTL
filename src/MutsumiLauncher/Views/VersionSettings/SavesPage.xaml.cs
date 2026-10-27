using System.IO;
using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class SavesPage : UserControl, IInstancePage
{
    private string _version = string.Empty;
    private string _query = string.Empty;
    private List<SaveWorldItem> _items = [];
    private bool _busy;

    public SavesPage()
    {
        InitializeComponent();
    }

    private string GameDir => InstanceService.GameDirFor(_version);

    private string SavesDir => SavesService.SavesDirFor(GameDir);

    private string BackupsDir => SavesService.BackupRootFor(GameDir);

    public void SetInstance(string version)
    {
        _version = version;

        _query = string.Empty;
        SearchBox.Text = string.Empty;

        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var version = _version;
        var savesDir = SavesDir;
        var backupsDir = BackupsDir;

        List<SaveWorldItem> items;

        try
        {
            // 世界可能很大，统计大小／读 level.dat 都放后台，别卡住界面
            items = await Task.Run(() => SavesService.List(savesDir)
                .Select(world => SaveWorldItem.Create(world,
                    SavesService.ListBackups(backupsDir, world.Folder).Count))
                .ToList());
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load saves");
            items = [];
        }

        if (!string.Equals(version, _version, StringComparison.Ordinal)) return;

        _items = items;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_query)
            ? _items
            : _items.Where(item =>
                    item.DisplayName.Contains(_query, StringComparison.OrdinalIgnoreCase)
                    || item.World.Folder.Contains(_query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        WorldList.ItemsSource = filtered;

        var empty = filtered.Count == 0;
        WorldList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _items.Count == 0 ? "这个实例还没有存档。" : "没有匹配的存档。";

        SummaryText.Text = $"共 {_items.Count} 个存档 · {_items.Sum(item => item.BackupCount)} 个备份";
    }

    private void SearchBox_TextChanged(object sender, EventArgs e)
    {
        _query = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OpenSavesFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SavesDir);
        OpenFolder(SavesDir);
    }

    private void OpenBackupsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupsDir);
        OpenFolder(BackupsDir);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SaveWorldItem item }) OpenFolder(item.World.Directory);
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not Button { Tag: SaveWorldItem item } button) return;

        var version = _version;
        var backupsDir = BackupsDir;

        _busy = true;
        button.IsEnabled = false;
        BusyText.Text = $"正在备份「{item.DisplayName}」…";

        try
        {
            var backup = await SavesService.BackupAsync(backupsDir, item.World);
            Toast($"已备份「{item.DisplayName}」（{InstanceService.FormatSize(backup.SizeBytes)}）");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "backup save");
            Toast("备份失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            button.IsEnabled = true;
            BusyText.Text = string.Empty;
        }

        if (string.Equals(version, _version, StringComparison.Ordinal)) await ReloadAsync();
    }

    private async void BackupAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (_items.Count == 0)
        {
            Toast("没有可备份的存档。");
            return;
        }

        _busy = true;

        var total = _items.Count;
        var succeeded = 0;
        var index = 0;

        try
        {
            foreach (var item in _items)
            {
                index++;
                BusyText.Text = $"正在备份「{item.DisplayName}」…（{index}/{total}）";

                try
                {
                    await SavesService.BackupAsync(BackupsDir, item.World);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "backup save " + item.World.Folder);
                }
            }
        }
        finally
        {
            _busy = false;
            BusyText.Text = string.Empty;
            await ReloadAsync();
        }

        Toast(succeeded == total
            ? $"已备份全部 {total} 个存档。"
            : $"备份完成 {succeeded}/{total}，失败的可以看日志。");
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SaveWorldItem item }) return;

        var dialog = new SavesBackupDialog(item.World, BackupsDir)
        {
            Owner = Window.GetWindow(this),
        };

        dialog.ShowDialog();

        if (dialog.Changed) _ = ReloadAsync();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SaveWorldItem item }) return;

        var confirmed = MessageDialog.Show(Window.GetWindow(this), "删除存档",
            $"确定删除存档「{item.DisplayName}」吗？\n\n" +
            $"目录：{item.World.Directory}\n\n" +
            "世界文件会整个删掉，无法恢复；已经做过的备份会留在 mtl-backups 里。",
            "删除");

        if (!confirmed) return;

        try
        {
            SavesService.DeleteWorld(item.World);
            Toast($"已删除存档「{item.DisplayName}」");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete save");
            Toast("删除失败：" + ex.Message);
        }

        _ = ReloadAsync();
    }

    private void OpenFolder(string path)
    {
        var error = InstanceService.OpenFolder(path);
        if (error is not null) Toast("打开失败：" + error);
    }

    private void Toast(string message)
        => (Window.GetWindow(this) as MainWindow)?.ShowToast(message);
}
