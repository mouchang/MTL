using System.IO;
using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class OverviewPage : UserControl, IInstancePage
{
    private string _version = string.Empty;
    private bool _busy;

    public OverviewPage()
    {
        InitializeComponent();
    }

    /// <summary>实例被重命名（参数为新名称）。</summary>
    public event Action<string>? Renamed;

    /// <summary>实例被删除。</summary>
    public event Action? Deleted;

    public void SetInstance(string version)
    {
        _version = version;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_version))
        {
            NameText.Text = "未选择实例";
            CaptionText.Text = "先在启动页选择一个版本。";
            return;
        }

        NameText.Text = _version;
        CaptionText.Text = "正在读取…";
        RenameBox.Text = _version;

        InstanceInfo info;
        try
        {
            info = await Task.Run(() => InstanceService.GetInfo(_version));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "instance info");
            CaptionText.Text = "读取失败：" + ex.Message;
            return;
        }

        NameText.Text = info.Name;
        CaptionText.Text = $"{info.Loader} · 基础版本 {info.GameVersion}";

        PathText.Text = info.Path;
        SizeText.Text = InstanceService.FormatSize(info.SizeBytes);
        ModText.Text = $"{info.ModCount} 个";

        var settings = InstanceSettings.Load(_version);
        JavaText.Text = string.IsNullOrWhiteSpace(settings.JavaPath)
            ? "跟随全局"
            : Path.GetFileName(Path.GetDirectoryName(settings.JavaPath)) ?? settings.JavaPath;

        MemoryText.Text = settings.MemoryMb switch
        {
            > 0 => $"{settings.MemoryMb} MB",
            < 0 => "自动分配",
            _ => "跟随全局",
        };
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(_version)) return;

        var newName = RenameBox.Text.Trim();
        if (newName.Length == 0)
        {
            Toast("实例名称不能为空。");
            return;
        }

        if (newName == _version)
        {
            Toast("名称没有变化。");
            return;
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            if (newName.Contains(invalid))
            {
                Toast("名称里不能包含 \\ / : * ? \" < > | 这些字符。");
                return;
            }
        }

        _busy = true;
        try
        {
            await Task.Run(() => InstanceService.Rename(_version, newName));
            _version = newName;
            Renamed?.Invoke(newName);
            Toast($"已重命名为「{newName}」");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "rename instance");
            Toast("重命名失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void DeleteVersion_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var owner = Window.GetWindow(this);
        var confirmed = MessageDialog.Show(owner, "删除实例",
            $"确定要删除「{_version}」吗？\n\n整个实例目录（含 mods、存档、日志）都会被删除，无法恢复。",
            "删除");

        if (!confirmed) return;

        try
        {
            InstanceService.Delete(_version);
            Toast($"已删除「{_version}」");
            Deleted?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "delete instance");
            Toast("删除失败：" + ex.Message);
        }
    }

    private void OpenVersionFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(InstanceService.DirFor(_version));

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(InstanceService.ModsDirFor(_version));

    private void OpenSavesFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(Path.Combine(InstanceService.GameDirFor(_version), "saves"));

    private void OpenScreenshotsFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(Path.Combine(InstanceService.GameDirFor(_version), "screenshots"));

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;
        (Window.GetWindow(this) as MainWindow)?.OpenLogViewer(_version);
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        => OpenFolder(Path.Combine(InstanceService.GameDirFor(_version), "logs"));

    private void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(_version)) return;

        var error = InstanceService.OpenFolder(path);
        if (error is not null) Toast("打开文件夹失败：" + error);
    }

    private void Toast(string message)
        => (Window.GetWindow(this) as MainWindow)?.ShowToast(message);
}
