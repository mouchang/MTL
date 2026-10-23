using System.Windows;
using System.Windows.Input;

namespace MutsumiLauncher.Views;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>显示一个玻璃风消息框；确定返回 true。</summary>
    public static bool Show(Window? owner, string title, string message,
        string okText = "确定", string? cancelText = "取消")
    {
        var dialog = new MessageDialog();
        if (owner is not null) dialog.Owner = owner;

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.OkButton.Content = okText;

        if (cancelText is null)
        {
            dialog.CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            dialog.CancelButton.Content = cancelText;
        }

        return dialog.ShowDialog() == true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
        else if (e.Key == Key.Enter)
        {
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
