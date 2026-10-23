using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public partial class MicrosoftLoginDialog : Window
{
    private readonly string _clientId;
    private readonly CancellationTokenSource _cts = new();

    public MicrosoftLoginDialog(string clientId)
    {
        _clientId = clientId;
        InitializeComponent();
        Loaded += async (_, _) => await RunAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    public MicrosoftProfile? Profile { get; private set; }

    public string? SkinPath { get; private set; }

    private async Task RunAsync()
    {
        try
        {
            var code = await MicrosoftAuth.StartAsync(_clientId, _cts.Token);

            CodeText.Text = code.UserCode;
            LinkText.Text = code.VerificationUri;
            CodePanel.Visibility = Visibility.Visible;
            HintText.Text = "等待浏览器中完成授权…（窗口可以保持打开）";

            var msaToken = await MicrosoftAuth.WaitForTokenAsync(_clientId, code, _cts.Token);

            HintText.Text = "授权成功，正在验证 Xbox 账号…";
            var profile = await MicrosoftAuth.LoginMinecraftAsync(msaToken, _cts.Token);

            HintText.Text = "正在获取皮肤…";
            if (!string.IsNullOrWhiteSpace(profile.SkinUrl))
                SkinPath = await SkinService.EnsureSkinAsync(profile.SkinUrl, profile.Uuid, _cts.Token);

            Profile = profile;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "microsoft login");
            HintText.Text = ex.Message;
            HintText.Foreground = System.Windows.Media.Brushes.Firebrick;
            CancelButton.Content = "关闭";
        }
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(LinkText.Text) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open browser");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
