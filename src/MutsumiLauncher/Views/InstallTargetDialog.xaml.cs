using System.IO;
using System.Windows;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

/// <summary>
/// 安装位置确认框：明确显示「装到哪个文件夹、叫什么名字」，也允许换位置。
/// 用它替代直接弹系统保存对话框——那个对话框偶尔会被浮层挡住，
/// 而且不会告诉用户资源最终进了哪个实例。
/// </summary>
public partial class InstallTargetDialog : Window
{
    public string TargetDirectory { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public InstallTargetDialog(string title, string hint, string directory, string fileName,
        string note)
    {
        InitializeComponent();

        TitleText.Text = title;
        HintText.Text = hint;
        NoteText.Text = note;

        TargetDirectory = directory;
        FileName = fileName;

        Refresh();
    }

    private void Refresh()
    {
        FolderText.Text = TargetDirectory;
        FileText.Text = FileName;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "选择安装位置",
            InitialDirectory = Directory.Exists(TargetDirectory)
                ? TargetDirectory
                : Path.GetDirectoryName(TargetDirectory) ?? TargetDirectory,
            FileName = FileName,
            Filter = "文件 (*.*)|*.*",
            OverwritePrompt = false,
            CheckPathExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        TargetDirectory = Path.GetDirectoryName(dialog.FileName) ?? TargetDirectory;
        FileName = Path.GetFileName(dialog.FileName);
        Refresh();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(TargetDirectory);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "install target directory");
            MessageBox.Show(this, "这个文件夹无法写入：" + ex.Message, "安装位置");
            return;
        }

        DialogResult = true;
    }

    /// <summary>没有指定 Owner 时也能居中到主窗口。</summary>
    public static void CenterOn(FrameworkElement owner, Window dialog)
    {
        dialog.Owner = Window.GetWindow(owner);
    }
}
