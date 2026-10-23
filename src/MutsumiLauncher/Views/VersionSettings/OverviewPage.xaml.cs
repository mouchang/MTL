using System.Windows;
using System.Windows.Controls;

namespace MutsumiLauncher.Views.VersionSettings;

public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        InitializeComponent();
    }

    private void DeleteVersion_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var confirmed = MessageDialog.Show(owner,
            "删除版本",
            "确定要删除「猫饼镇 beta 1.21.1」吗？版本文件与存档不会被一并删除，但该版本将从列表中移除。",
            "删除");

        if (confirmed)
        {
            MessageDialog.Show(owner, "提示", "删除逻辑还没接上，这里先走通了确认框。", "知道了", null);
        }
    }
}
