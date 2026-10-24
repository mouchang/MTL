using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public partial class FolderBrowserView : UserControl
{
    private sealed class Entry
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public bool IsDirectory { get; init; }
        public string Icon => IsDirectory ? "\uE8B7" : "\uE8A5";
        public string SizeText { get; init; } = string.Empty;
    }

    private string _path = string.Empty;

    public FolderBrowserView()
    {
        InitializeComponent();
    }

    public event Action? Closed;

    public void Load(string path, string? title = null)
    {
        _path = path;
        TitleText.Text = title ?? "文件夹";
        Refresh();
    }

    private void Refresh()
    {
        PathText.Text = _path;

        var entries = new List<Entry>();

        try
        {
            var directory = new DirectoryInfo(_path);
            if (!directory.Exists)
            {
                EntryList.ItemsSource = entries;
                EmptyText.Visibility = Visibility.Visible;
                EmptyText.Text = "文件夹不存在。";
                return;
            }

            entries.AddRange(directory.EnumerateDirectories()
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(info => new Entry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = true,
                    SizeText = "文件夹",
                }));

            entries.AddRange(directory.EnumerateFiles()
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(info => new Entry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    SizeText = InstanceService.FormatSize(SafeLength(info)),
                }));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "browse folder");
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = "读取失败：" + ex.Message;
        }

        EntryList.ItemsSource = entries;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (entries.Count == 0) EmptyText.Text = "（空文件夹）";
    }

    private static long SafeLength(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch
        {
            return 0;
        }
    }

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is not Entry entry) return;

        if (entry.IsDirectory)
        {
            _path = entry.FullPath;
            Refresh();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open file");
            (Window.GetWindow(this) as MainWindow)?.ShowToast("无法打开文件：" + ex.Message);
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_path);
        if (parent is null) return;

        _path = parent.FullName;
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var error = InstanceService.OpenFolder(_path);
        if (error is not null)
            (Window.GetWindow(this) as MainWindow)?.ShowToast("无法打开资源管理器：" + error);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Grid) Closed?.Invoke();
    }
}
