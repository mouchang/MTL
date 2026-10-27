using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.ViewModels;

/// <summary>存档页里的一行世界。</summary>
public sealed class SaveWorldItem
{
    public required SaveWorld World { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>「1.21.1 · 2026-10-27 21:30 · 128 MB」。</summary>
    public required string DetailText { get; init; }

    /// <summary>世界目录名，和存档名不一样时显示出来，方便对文件夹。</summary>
    public required string FolderText { get; init; }

    public ImageSource? Icon { get; init; }

    public int BackupCount { get; init; }

    public string BackupText => BackupCount == 0 ? "未备份" : $"{BackupCount} 个备份";

    public Visibility RestoreVisibility => BackupCount == 0 ? Visibility.Collapsed : Visibility.Visible;

    public static SaveWorldItem Create(SaveWorld world, int backupCount)
    {
        var display = world.DisplayName;

        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(world.GameVersion)) parts.Add(world.GameVersion);
        if (world.LastPlayed is { } played) parts.Add(played.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        parts.Add(InstanceService.FormatSize(world.SizeBytes));

        return new SaveWorldItem
        {
            World = world,
            DisplayName = display,
            DetailText = string.Join(" · ", parts),
            FolderText = display.Equals(world.Folder, StringComparison.Ordinal) ? string.Empty : world.Folder,
            Icon = LoadIcon(world.IconPath),
            BackupCount = backupCount,
        };
    }

    private static ImageSource? LoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            // OnLoad：读进内存后立刻释放文件句柄，否则后面删存档／还原会被占用
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 84;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load save icon");
            return null;
        }
    }
}

/// <summary>备份弹窗里的一行备份。</summary>
public sealed class SaveBackupItem
{
    public required SaveBackup Backup { get; init; }

    public string TimeText => Backup.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public string SizeText => InstanceService.FormatSize(Backup.SizeBytes);

    public string PathText => Backup.FileName;

    public static SaveBackupItem Create(SaveBackup backup) => new() { Backup = backup };
}
