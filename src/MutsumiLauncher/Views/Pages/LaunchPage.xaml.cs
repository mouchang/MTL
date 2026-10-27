using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MutsumiLauncher.Services;
using MutsumiLauncher.Views;

namespace MutsumiLauncher.Views.Pages;

public partial class LaunchPage : UserControl
{
    private IReadOnlyList<string> _versions = [];
    private bool _subscribed;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _watcherDebounce;

    public LaunchPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AlignAvatar();
            RefreshVersions();
            StartWatchingVersions();

            if (!_subscribed && Window.GetWindow(this) is MainWindow main)
            {
                _subscribed = true;
                main.VersionSelectionChanged += RefreshVersions;
                main.AccountChanged += _ => ApplyAccount();
            }

            ApplyAccount();
        };
        NameCard.SizeChanged += (_, _) => AlignAvatar();
        Unloaded += (_, _) => StopWatchingVersions();
    }

    /// <summary>实时监听 versions 目录，安装/删除/重命名版本后自动刷新，不用手动点刷新。</summary>
    private void StartWatchingVersions()
    {
        if (_watcher is not null) return;

        try
        {
            Directory.CreateDirectory(GamePaths.VersionsDir);

            _watcherDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _watcherDebounce.Tick += (_, _) =>
            {
                _watcherDebounce?.Stop();
                RefreshVersions();
            };

            _watcher = new FileSystemWatcher(GamePaths.VersionsDir)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };

            void Schedule()
            {
                Dispatcher.Invoke(() =>
                {
                    if (_watcherDebounce is null) return;
                    _watcherDebounce.Stop();
                    _watcherDebounce.Start();
                });
            }

            _watcher.Created += (_, _) => Schedule();
            _watcher.Deleted += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
            _watcher.EnableRaisingEvents = true;

            AppLog.Info("watching versions dir: " + GamePaths.VersionsDir);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "watch versions");
        }
    }

    private void StopWatchingVersions()
    {
        try
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            _watcherDebounce?.Stop();
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>头像中心与用户名胶囊中心对齐。</summary>
    private void AlignAvatar()
    {
        var width = NameCard.ActualWidth;
        if (width <= 0) return;

        var offset = Math.Max(0, (width - AvatarHost.Width) / 2);
        AvatarHost.Margin = new Thickness(offset, 0, 0, 0);
    }

    /// <summary>重新读取版本列表和账号信息（切回启动页 / 安装完成后调用）。</summary>
    public void RefreshAll()
    {
        RefreshVersions();
        ApplyAccount();
    }

    /// <summary>读取已安装版本，恢复上次选择。</summary>
    public void RefreshVersions()
    {
        _versions = GameLauncher.InstalledVersions();
        VersionText.Text = SelectedVersion() ?? "未安装版本";

        var config = AppConfig.Current;

        if (_versions.Count > 0 && !_versions.Contains(config.SelectedVersion))
        {
            // 当前版本被删除或改名：自动切换到第一个可用版本
            config.SelectedVersion = _versions[0];
            config.Save();
            VersionText.Text = config.SelectedVersion;
        }
        else if (string.IsNullOrWhiteSpace(config.SelectedVersion) && _versions.Count > 0)
        {
            config.SelectedVersion = _versions[0];
            config.Save();
        }
    }

    /// <summary>头像显示尺寸（与 XAML 里的卡片一致），按这个尺寸生成位图才能 1:1 清晰。</summary>
    private const int AvatarSize = 116;

    private void ApplyAccount()
    {
        var account = AccountService.EnsureDefault();
        AccountNameText.Text = account.Name;
        AccountTypeBadge.Text = account.TypeLabel;

        // 微软账号显示皮肤正脸，离线默认史蒂夫
        if (!string.IsNullOrWhiteSpace(account.SkinPath) && File.Exists(account.SkinPath)
            && SkinService.CreateFace(account.SkinPath, AvatarSize) is { } face)
        {
            AvatarImage.Source = face;
        }
        else
        {
            AvatarImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/steve-head.png"));
        }
    }

    private string? SelectedVersion()
    {
        if (_versions.Count == 0) return null;

        var selected = AppConfig.Current.SelectedVersion;
        return _versions.Contains(selected) ? selected : _versions[0];
    }

    private void VersionPill_Click(object sender, RoutedEventArgs e)
    {
        if (_versions.Count == 0)
        {
            (Window.GetWindow(this) as MainWindow)?.ShowToast("还没有安装任何版本，先去下载页装一个吧。");
            return;
        }

        (Window.GetWindow(this) as MainWindow)?.OpenVersionPicker();
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var owner = Window.GetWindow(this) as MainWindow;

        var version = SelectedVersion();
        if (version is null)
        {
            owner?.ShowToast("还没有安装任何版本，先去下载页装一个吧。");
            return;
        }

        var config = AppConfig.Current;
        if (config.SelectedVersion != version)
        {
            config.SelectedVersion = version;
            config.Save();
        }

        var original = button.Content;
        button.IsEnabled = false;
        button.Content = "启动中…";

        try
        {
            var account = AccountService.Current ?? AccountService.EnsureDefault();

            // 正版账号：游戏令牌 24 小时过期，启动前用刷新令牌免密续期
            if (account.IsMicrosoft && !await AccountService.EnsureFreshAsync(account))
            {
                var relogin = MessageDialog.Show(owner, "正版账号需要重新登录",
                    "登录状态已过期，请到账号选择里重新登录微软账号。" + Environment.NewLine +
                    "本次将用原账号名以离线模式启动。",
                    "继续启动", "取消");

                if (relogin != true) return;
            }

            var launcher = new GameLauncher();
            var settings = InstanceSettings.Load(version);
            var options = new LaunchOptions(account.Name, account.Uuid, account.AccessToken,
                account.IsMicrosoft, account.Xuid, settings.EffectiveJavaPath, settings.EffectiveMemoryMb,
                settings.EffectiveIsolation);

            var panel = new LaunchPanelView();
            panel.Start(version, settings.EffectiveJavaPath);
            owner?.ShowLaunchPanel(panel);

            var progress = new Progress<LaunchProgress>(panel.Update);

            var result = await launcher.LaunchAsync(version, options, progress);

            if (result.Started)
                panel.SetRunning(result.ProcessId, result.LogPath);
            else
                panel.SetFailed(result.Message);

            owner?.ShowToast(result.Message);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "launch game");
            owner?.ShowToast("启动失败：" + ex.Message);
        }
        finally
        {
            button.Content = original;
            button.IsEnabled = true;
        }
    }

    private void SwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenAccountPicker();
    }

    private void VersionSettings_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenVersionSettings();
    }
}
