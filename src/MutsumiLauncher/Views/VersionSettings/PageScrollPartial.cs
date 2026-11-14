using System.Windows;
using System.Windows.Controls;

namespace MutsumiLauncher.Views.VersionSettings;

// 各页的「回到顶部」实现：切换板块时统一调用，避免停留上次浏览的位置。

public partial class OverviewPage
{
    public void ScrollToTop() => RootScroll.ScrollToTop();
}

public partial class LaunchOptionsPage
{
    public void ScrollToTop() => RootScroll.ScrollToTop();
}

public partial class ExportPage
{
    public void ScrollToTop() => RootScroll.ScrollToTop();
}

public partial class SavesPage
{
    public void ScrollToTop() => WorldScroll.ScrollToTop();
}

public partial class ModManagePage
{
    /// <summary>
    /// 回到第 1 行。列表的模板里那个 ScrollViewer 名叫 PART_Scroll，
    /// 直接拿它滚到顶即可；首行已经实例化时用 BringIntoView 更稳。
    /// </summary>
    public void ScrollToTop()
    {
        if (ModList.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement first)
        {
            first.BringIntoView();
            return;
        }

        ModList.ApplyTemplate();

        if (ModList.Template?.FindName("PART_Scroll", ModList) is ScrollViewer viewer)
        {
            viewer.ScrollToTop();
        }
    }
}
