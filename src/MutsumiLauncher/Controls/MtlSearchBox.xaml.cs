using System.Windows;
using System.Windows.Controls;

namespace MutsumiLauncher.Controls;

public partial class MtlSearchBox : UserControl
{
    public event EventHandler? TextChanged;

    public MtlSearchBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => Inner.Text;
        set => Inner.Text = value;
    }

    public string Placeholder
    {
        get => PlaceholderText.Text;
        set => PlaceholderText.Text = value;
    }

    private void Inner_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PlaceholderText is null || ClearButton is null) return;

        var empty = Inner.Text.Length == 0;
        PlaceholderText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Inner.Text = string.Empty;
        Inner.Focus();
    }
}
