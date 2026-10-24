using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MutsumiLauncher.Setup;

public partial class MainWindow : Window
{
    private string _installDir = string.Empty;
    private bool _installing;
    private bool _finished;
    private readonly bool _silent;

    public MainWindow()
    {
        InitializeComponent();

        Log("start args: " + string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));

        string? dirFromArgs = null;

        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg.Equals("/S", StringComparison.OrdinalIgnoreCase)) _silent = true;
            if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
                dirFromArgs = arg["/DIR=".Length..].Trim('"');
        }

        var defaultDir = IsElevated
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Mutsumi Launcher")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Mutsumi Launcher");

        if (!TryGetWritable(defaultDir))
        {
            defaultDir = Path.Combine(AppContext.BaseDirectory, "MutsumiLauncher");
        }

        DirBox.Text = dirFromArgs is { Length: > 0 } ? dirFromArgs : defaultDir;

        if (_silent)
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(300);
                Install_Click(InstallButton, new RoutedEventArgs());
            };
        }
    }

    private string PayloadPath => Path.Combine(AppContext.BaseDirectory, "payload.zip");

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "setup.log"),
                $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 忽略日志失败
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void DirBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _installDir = DirBox.Text.Trim();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择安装位置",
            InitialDirectory = Directory.Exists(_installDir) ? _installDir : null,
        };

        if (dialog.ShowDialog() != true) return;

        DirBox.Text = dialog.FolderName;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            Close();
            return;
        }

        if (_installing) return;

        var dir = DirBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(dir) || Path.GetPathRoot(dir)?.TrimEnd('\\') == dir.TrimEnd('\\'))
        {
            Status("请选择一个具体的安装文件夹（不能是磁盘根目录）。", error: true);
            return;
        }

        Log($"install requested: dir={dir}, payload={PayloadPath}, exists={File.Exists(PayloadPath)}");

        if (!File.Exists(PayloadPath))
        {
            Status("找不到安装包 payload.zip，请确认它和安装程序在同一个文件夹。", error: true);
            return;
        }

        if (!TryPrepareDirectory(dir))
        {
            Status("无法写入该目录，请换一个位置（或选择用户目录下的文件夹）。", error: true);
            return;
        }

        _installing = true;
        InstallButton.IsEnabled = false;
        Status("正在安装…");

        try
        {
            await Task.Run(() => ExtractPayloadAsync(dir));
            CreateShortcuts(dir);
            WriteUninstaller(dir);
            WriteUninstallRegistry(dir);

            _installing = false;
            _finished = true;

            InstallButton.Content = "完成";
            InstallButton.IsEnabled = true;
            OpenButton.Visibility = Visibility.Visible;

            Log("install completed: " + dir);
            Status($"安装完成：{dir}\n启动器已就绪，点击「完成」退出，或打开安装目录查看。");
            SetProgress(1);

            if (_silent)
            {
                Close();
                return;
            }

            if (MessageBox.Show(this, "安装完成，现在运行 Mutsumi Launcher 吗？", "安装完成",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Launch(dir);
            }
        }
        catch (Exception ex)
        {
            Log("install failed: " + ex);
            _installing = false;
            InstallButton.IsEnabled = true;
            Status("安装失败：" + ex.Message, error: true);
        }
    }

    private async Task ExtractPayloadAsync(string targetDir)
    {
        using var archive = ZipFile.OpenRead(PayloadPath);
        var total = archive.Entries.Count;
        var index = 0;

        foreach (var entry in archive.Entries)
        {
            index++;

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(Path.Combine(targetDir, entry.FullName));
                continue;
            }

            var destination = Path.Combine(targetDir, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            entry.ExtractToFile(destination, overwrite: true);

            if (index % 40 == 0 || index == total)
            {
                var percent = index / (double)total;
                Dispatcher.Invoke(() =>
                {
                    SetProgress(percent);
                    Status($"正在安装… {index}/{total} 个文件");
                });
            }
        }
    }

    private void CreateShortcuts(string dir)
    {
        var exe = Path.Combine(dir, "MutsumiLauncher.exe");
        if (!File.Exists(exe)) return;

        if (DesktopShortcutCheck.IsChecked == true)
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Mutsumi Launcher.lnk"), exe, dir);

        if (StartMenuCheck.IsChecked == true)
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!string.IsNullOrWhiteSpace(programs))
                CreateShortcut(Path.Combine(programs, "Mutsumi Launcher.lnk"), exe, dir);
        }
    }

    /// <summary>快捷方式创建失败（受限环境）不影响安装，只是没有快捷方式。</summary>
    private static void CreateShortcut(string linkPath, string targetPath, string workingDir)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.IconLocation = targetPath + ",0";
            shortcut.Description = "Mutsumi Launcher";
            shortcut.Save();
        }
        catch
        {
            // 忽略：沙箱/受限环境可能禁止创建快捷方式
        }
    }

    private static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>写入「应用和功能」卸载项；非管理员或受限环境忽略失败。</summary>
    private static void WriteUninstallRegistry(string dir)
    {
        if (!IsElevated) return;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MutsumiLauncher");

            if (key is null) return;

            var uninstall = Path.Combine(dir, "卸载 Mutsumi Launcher.cmd");

            key.SetValue("DisplayName", "Mutsumi Launcher");
            key.SetValue("DisplayVersion", "0.1.0");
            key.SetValue("Publisher", "Mutsumi");
            key.SetValue("InstallLocation", dir);
            key.SetValue("DisplayIcon", Path.Combine(dir, "MutsumiLauncher.exe"));
            key.SetValue("UninstallString", "\"" + uninstall + "\"");
            key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch
        {
            // 忽略：某些环境禁止写注册表
        }
    }

    private static void WriteUninstaller(string dir)
    {
        try
        {
            var script = """
                @echo off
                chcp 65001 >nul
                taskkill /f /im MutsumiLauncher.exe >nul 2>&1
                del "%USERPROFILE%\Desktop\Mutsumi Launcher.lnk" >nul 2>&1
                del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Mutsumi Launcher.lnk" >nul 2>&1
                echo 正在删除 Mutsumi Launcher...
                cd /d "%~dp0"
                start "" /b cmd /c "timeout /t 1 >nul & rd /s /q "%~dp0""
                """;

            File.WriteAllText(Path.Combine(dir, "卸载 Mutsumi Launcher.cmd"), script,
                new System.Text.UTF8Encoding(false));
        }
        catch
        {
            // 卸载脚本写不了也不影响使用
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => Launch(_installDir, openFolderOnly: true);

    private void Launch(string dir, bool openFolderOnly = false)
    {
        try
        {
            var exe = Path.Combine(dir, "MutsumiLauncher.exe");

            if (!openFolderOnly && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true });
                Close();
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开失败：" + ex.Message, "提示", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SetProgress(double value)
    {
        var width = 720 - 28 * 2;
        ProgressFill.Width = Math.Clamp(value, 0, 1) * width;
    }

    private void Status(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error
            ? new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B))
            : new SolidColorBrush(Color.FromRgb(0x2E, 0x7A, 0x55));
    }

    private static bool TryPrepareDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".mtl-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
