using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public sealed class AccountRow
{
    public required Account Account { get; init; }

    public required bool IsCurrent { get; init; }

    public string Name => Account.Name;

    public string TypeLabel => Account.TypeLabel;

    public string Initial => string.IsNullOrEmpty(Account.Name)
        ? "?"
        : Account.Name[..1].ToUpperInvariant();

    public Brush AvatarBrush => Account.IsMicrosoft
        ? new SolidColorBrush(Color.FromRgb(0x5B, 0x8C, 0xDA))
        : new SolidColorBrush(Color.FromRgb(0x7B, 0xC4, 0x7F));
}

public partial class AccountPickerView : UserControl
{
    private static readonly Regex NamePattern = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    private bool _closing;

    public AccountPickerView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PlayOpen();
            NewNameBox.Focus();
        };
        NewNameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) AddAccount();
        };
    }

    public event Action<Account>? AccountChosen;

    public event Action? Closed;

    public void Configure(IReadOnlyList<Account> accounts, Account? current)
    {
        Refresh(accounts, current?.Name);
    }

    private void Refresh(IReadOnlyList<Account> accounts, string? currentName)
    {
        AccountList.ItemsSource = accounts
            .Select(a => new AccountRow { Account = a, IsCurrent = a.Name == currentName })
            .ToList();
    }

    private void PlayOpen()
    {
        var slide = new DoubleAnimation(460, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Slide.BeginAnimation(TranslateTransform.YProperty, slide);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void Close()
    {
        if (_closing) return;
        _closing = true;

        var slide = new DoubleAnimation(0, 460, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slide.Completed += (_, _) => Closed?.Invoke();
        Slide.BeginAnimation(TranslateTransform.YProperty, slide);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountRow row }) return;

        AccountService.Select(row.Account.Name);
        AccountChosen?.Invoke(row.Account);
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountRow row }) return;

        AccountService.Remove(row.Account.Name);
        var current = AccountService.Current;
        Refresh(AccountService.All, current?.Name);
        AccountChosen?.Invoke(current ?? new Account());
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddAccount();

    private void AddAccount()
    {
        var name = NewNameBox.Text.Trim();
        if (!NamePattern.IsMatch(name))
        {
            HintText.Text = "用户名不合法：3–16 位，只能用字母、数字或下划线。";
            HintText.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x4A, 0x3E));
            return;
        }

        var account = AccountService.AddOffline(name);
        NewNameBox.Text = string.Empty;
        HintText.Text = $"已添加离线账号 {account.Name}。";
        HintText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0x9E, 0x72));

        Refresh(AccountService.All, account.Name);
        AccountChosen?.Invoke(account);
    }

    private void MicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        // 留空就用内置的官方 clientId（旧版设备码流程）；填了就用自建 Azure 应用
        var clientId = AppConfig.Current.MicrosoftClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            clientId = Environment.GetEnvironmentVariable("MTL_MS_CLIENT_ID") ?? string.Empty;

        var dialog = new MicrosoftLoginDialog(clientId) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Profile is not { } profile) return;

        var account = AccountService.AddMicrosoft(profile.Name, profile.Uuid, profile.AccessToken,
            dialog.RefreshToken ?? string.Empty, profile.ExpiresAt, profile.Xuid, dialog.SkinPath);

        Refresh(AccountService.All, account.Name);
        HintText.Text = $"已登录微软账号 {account.Name}。";
        HintText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0x9E, 0x72));
        AccountChosen?.Invoke(account);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Grid) Close();
    }
}
