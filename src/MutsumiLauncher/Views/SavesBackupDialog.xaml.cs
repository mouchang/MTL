using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MutsumiLauncher.Services;
using MutsumiLauncher.ViewModels;

namespace MutsumiLauncher.Views;

/// <summary>某个存档的备份列表：还原 / 删除。</summary>
public partial class SavesBackupDialog : Window
{
    private readonly SaveWorld _world;
    private readonly string _backupsDir;

    public SavesBackupDialog(SaveWorld world, string backupsDir)
    {
        InitializeComponent();

        _world = world;
        _backupsDir = backupsDir;

        TitleText.Text = $"「{world.DisplayName}」的备份";
        PreviewKeyDown += OnPreviewKeyDown;

        Reload();
    }

    /// <summary>弹窗里有没有真的还原过东西，关闭后由存档页决定要不要刷新。</summary>
    public bool Changed { get; private set; }

    private void Reload()
    {
        var backups = SavesService.ListBackups(_backupsDir, _world.Folder);

        BackupList.ItemsSource = backups.Select(SaveBackupItem.Create).ToList();

        var empty = backups.Count == 0;
        BackupList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        SummaryText.Text = empty ? string.Empty : $"共 {backups.Count} 个备份，最新的在最上面。";
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SaveBackupItem item }) return;

        var confirmed = MessageDialog.Show(this, "还原存档",
            $"会用这份备份覆盖存档「{_world.DisplayName}」，当前进度会丢。\n\n" +
            $"备份时间：{item.TimeText}\n大小：{item.SizeText}\n\n继续吗？",
            "还原");

        if (!confirmed) return;

        try
        {
            SavesService.Restore(item.Backup, _world.Directory);
            Changed = true;
            StatusText.Text = $"已还原到 {item.TimeText} 的备份。";
            Toast("存档已还原，进游戏前记得先退出正在运行的游戏。");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "restore save");
            StatusText.Text = "还原失败，原存档没有改动。";
            MessageDialog.Show(this, "还原失败", ex.Message, "知道了", null);
        }

        Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SaveBackupItem item }) return;

        if (!MessageDialog.Show(this, "删除备份",
                $"确定删除 {item.TimeText} 的备份吗？（{item.SizeText}）", "删除"))
            return;

        try
        {
            SavesService.DeleteBackup(item.Backup);
            Changed = true;   // 备份数量变了，存档页要跟着刷新
            StatusText.Text = "已删除备份。";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete save backup");
            StatusText.Text = "删除失败。";
            MessageDialog.Show(this, "删除失败", ex.Message, "知道了", null);
        }

        Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Toast(string message)
        => (Owner as MainWindow)?.ShowToast(message);
}
