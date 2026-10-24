using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        Loaded += async (_, _) =>
        {
            PlayOpen();
            await RunAsync();
        };
        Closed += (_, _) => _cts.Cancel();
    }

    public MicrosoftProfile? Profile { get; private set; }

    public string? SkinPath { get; private set; }

    private void PlayOpen()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Card.BeginAnimation(OpacityProperty, fade);

        var slide = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Slide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private async Task RunAsync()
    {
        try
        {
            var code = await MicrosoftAuth.StartAsync(_clientId, _cts.Token);

            CodeText.Text = code.UserCode;
            LinkText.Text = code.VerificationUri;
            CodePanel.Visibility = Visibility.Visible;
            HintText.Text = "在浏览器里输入设备码并授权，完成后本窗口会自动关闭。";

            // 默认方案：自动复制设备码 + 自动打开验证网站
            var copied = TrySetClipboard(code.UserCode);
            var opened = TryOpenBrowser(code.VerificationUri);

            StatusText.Text =
                (copied ? "设备码已自动复制到剪贴板。" : "自动复制失败，请点右边的「复制」。") + " " +
                (opened ? "已自动打开浏览器。" : "浏览器打开失败，请点「打开」。");

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
            HintText.Foreground = Brushes.Firebrick;
            CancelButton.Content = "关闭";
        }
    }

    private static bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "clipboard");
            return false;
        }
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open browser");
            return false;
        }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = TrySetClipboard(CodeText.Text)
            ? "设备码已复制到剪贴板。"
            : "复制失败，请手动选中设备码复制。";
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = TrySetClipboard(LinkText.Text)
            ? "验证链接已复制到剪贴板。"
            : "复制失败，请手动选中链接复制。";
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = TryOpenBrowser(LinkText.Text)
            ? "已打开浏览器。"
            : "浏览器打开失败，请手动访问链接。";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
