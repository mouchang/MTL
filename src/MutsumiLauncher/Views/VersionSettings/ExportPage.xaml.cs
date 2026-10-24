using System.IO;
using System.Windows;
using System.Windows.Controls;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class ExportPage : UserControl, IInstancePage
{
    private string _version = string.Empty;
    private bool _busy;

    public ExportPage()
    {
        InitializeComponent();
    }

    public void SetInstance(string version)
    {
        _version = version;
        InstanceHint.Text = string.IsNullOrWhiteSpace(version)
            ? "先在启动页选择一个实例。"
            : $"把「{version}」打包成整合包，方便分享或备份。";

        if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Text == _version)
            NameBox.Text = version;

        VersionBox.Text = "1.0.0";
        StatusText.Text = string.Empty;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(_version))
        {
            StatusText.Text = "先选择一个实例。";
            return;
        }

        var isMrpack = MrpackRadio.IsChecked == true;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出整合包",
            FileName = $"{Sanitize(NameBox.Text, _version)}-{VersionBox.Text.Trim()}",
            DefaultExt = isMrpack ? ".mrpack" : ".zip",
            Filter = isMrpack
                ? "Modrinth 整合包 (*.mrpack)|*.mrpack"
                : "压缩包 (*.zip)|*.zip",
        };

        if (dialog.ShowDialog() != true) return;

        _busy = true;
        StatusText.Text = "正在导出…";

        try
        {
            var options = new ModpackExporter.ExportOptions(
                _version,
                string.IsNullOrWhiteSpace(NameBox.Text) ? _version : NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(VersionBox.Text) ? "1.0.0" : VersionBox.Text.Trim(),
                ModsCheck.IsChecked == true,
                ConfigCheck.IsChecked == true,
                OptionsCheck.IsChecked == true,
                ResourceCheck.IsChecked == true,
                ShaderCheck.IsChecked == true,
                SavesCheck.IsChecked == true);

            var progress = new Progress<string>(message => StatusText.Text = message);

            var path = await ModpackExporter.ExportAsync(options, dialog.FileName, progress);

            var size = new FileInfo(path).Length;
            StatusText.Text = $"已导出：{Path.GetFileName(path)}（{InstanceService.FormatSize(size)}）";
            (Window.GetWindow(this) as MainWindow)?.ShowToast("整合包导出完成");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "export modpack");
            StatusText.Text = "导出失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static string Sanitize(string name, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }
}
