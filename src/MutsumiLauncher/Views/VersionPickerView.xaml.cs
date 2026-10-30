using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.Views;

public sealed class VersionRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Id { get; init; }

    /// <summary>界面显示用：引用实例标成「版本名（引用）」。</summary>
    public string Display => ExternalSourceService.DisplayName(Id);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class VersionPickerView : UserControl
{
    private bool _closing;

    public VersionPickerView()
    {
        InitializeComponent();
        Loaded += (_, _) => PlayOpen();
    }

    public event Action<string>? VersionChosen;

    public event Action? Closed;

    public void Configure(IReadOnlyList<string> versions, string? selected)
    {
        var active = string.IsNullOrWhiteSpace(selected) || !versions.Contains(selected)
            ? versions.FirstOrDefault()
            : selected;

        VersionList.ItemsSource = versions
            .Select(v => new VersionRow { Id = v, IsSelected = v == active })
            .ToList();

        EmptyHint.Visibility = versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PlayOpen()
    {
        var slide = new DoubleAnimation(420, 0, TimeSpan.FromMilliseconds(320))
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

        var slide = new DoubleAnimation(0, 420, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slide.Completed += (_, _) => Closed?.Invoke();
        Slide.BeginAnimation(TranslateTransform.YProperty, slide);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void Version_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VersionRow row }) return;

        var config = AppConfig.Current;
        config.SelectedVersion = row.Id;
        config.Save();

        VersionChosen?.Invoke(row.Id);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // 只有点在卡片外（背景）才关闭
        if (e.OriginalSource is Grid) Close();
    }
}
