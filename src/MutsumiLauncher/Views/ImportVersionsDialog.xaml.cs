using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public partial class ImportVersionsDialog : Window
{
    public sealed class Row : INotifyPropertyChanged
    {
        private bool _selected = true;

        public required string Name { get; init; }

        public required long Bytes { get; init; }

        public string SizeText => PathMigrator.FormatSize(Bytes);

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<Row> _rows = [];
    private readonly string _root;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public bool Imported { get; private set; }

    public ImportVersionsDialog(string root, IReadOnlyList<MinecraftImportService.VersionInfo> versions)
    {
        InitializeComponent();

        _root = root;
        SourceText.Text = root;

        foreach (var version in versions)
            _rows.Add(new Row { Name = version.Name, Bytes = version.Bytes });

        VersionList.ItemsSource = _rows;

        if (_rows.Count == 0)
        {
            StatusText.Text = "这个目录里没有可导入的版本。";
            StartButton.IsEnabled = false;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAll(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool selected)
    {
        foreach (var row in _rows) row.Selected = selected;
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        // 点复选框本身时别重复切换
        if (e.OriginalSource is CheckBox) return;

        if (sender is FrameworkElement { Tag: Row row }) row.Selected = !row.Selected;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var selected = _rows.Where(row => row.Selected).Select(row => row.Name).ToList();

        if (selected.Count == 0)
        {
            StatusText.Text = "先勾选要导入的版本。";
            return;
        }

        if (DeleteWholeCheck.IsChecked == true)
        {
            var confirm = MessageDialog.Show(this, "确认删除源目录",
                $"全部导入成功后会删除整个 {_root}。\n\n这个操作不可恢复，确定吗？",
                "确定删除", "取消");

            if (!confirm) return;
        }

        _busy = true;
        StartButton.IsEnabled = false;
        CancelButton.Content = "停止";
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;

        _cts = new CancellationTokenSource();

        var options = new MinecraftImportService.Options(
            CopySharedCheck.IsChecked == true,
            DeleteSourceCheck.IsChecked == true,
            DeleteWholeCheck.IsChecked == true);

        var progress = new Progress<MinecraftImportService.Progress>(p =>
        {
            StatusText.Text = p.Message;
            Progress.Value = p.Percent;
        });

        try
        {
            var result = await MinecraftImportService.ImportAsync(_root, selected, options, progress, _cts.Token);

            Imported = result.Imported.Count > 0;

            Progress.Value = 100;

            var summary = $"成功导入 {result.Imported.Count} 个版本" +
                          (result.Bytes > 0 ? $"，共 {PathMigrator.FormatSize(result.Bytes)}" : string.Empty);

            if (result.Failed.Count > 0)
                summary += $"；{result.Failed.Count} 个失败：" +
                           string.Join("、", result.Failed.Take(3).Select(item => item.Name));

            StatusText.Text = summary;

            if (result.Imported.Count > 0)
            {
                MessageDialog.Show(this, "导入完成", summary, "好的", null);
                DialogResult = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已停止（已导入的部分保留）。";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "import versions");
            StatusText.Text = "导入失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StartButton.IsEnabled = true;
            CancelButton.Content = "关闭";
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _cts?.Cancel();
            return;
        }

        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // 拖动失败就算了
            }
        }
    }
}