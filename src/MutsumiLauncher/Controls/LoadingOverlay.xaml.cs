using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MutsumiLauncher.Controls;

public partial class LoadingOverlay : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(LoadingOverlay),
        new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(LoadingOverlay),
        new PropertyMetadata("加载中…", OnLabelChanged));

    public LoadingOverlay()
    {
        InitializeComponent();
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LoadingOverlay)d).UpdateState();

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (LoadingOverlay)d;
        if (overlay.LabelText is not null) overlay.LabelText.Text = (string)e.NewValue;
    }

    private void UpdateState()
    {
        Visibility = IsActive ? Visibility.Visible : Visibility.Collapsed;

        if (IsActive)
        {
            var animation = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Spin.BeginAnimation(RotateTransform.AngleProperty, animation);
        }
        else
        {
            Spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
