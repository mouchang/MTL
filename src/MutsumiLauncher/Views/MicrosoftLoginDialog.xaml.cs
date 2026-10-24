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

            // 与 PCL2 一致：先复制设备码，等窗口稳定后再打开网页
            var copied = TrySetClipboard(code.UserCode);
            StatusText.Text = copied ? "设备码已复制，正在打开浏览器…" : "设备码复制失败，请手动复制。";

            await Task.Delay(2000, _cts.Token);

            var openError = UrlLauncher.Open(code.VerificationUri);

            if (openError is not null)
            {
                // 打不开就退而求其次：把链接也放进剪贴板
                TrySetClipboard(code.VerificationUri);
                StatusText.Text = "浏览器未能自动打开，验证链接已复制到剪贴板，请手动粘贴访问。";
            }
            else
            {
                StatusText.Text = (copied ? "设备码已复制。" : "请手动复制设备码。") +
                                  " 已打开浏览器；如果没有弹出，请点右侧「打开」。";
            }

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

            if (ex.Message.Contains("mce-reviewappid", StringComparison.OrdinalIgnoreCase))
                AppRegButton.Visibility = Visibility.Visible;
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
        var error = UrlLauncher.Open(LinkText.Text);
        StatusText.Text = error is null
            ? "已打开浏览器。"
            : $"打开失败：{error}";
    }

    private void OpenAppReg_Click(object sender, RoutedEventArgs e)
    {
        UrlLauncher.Open("https://aka.ms/mce-reviewappid");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
