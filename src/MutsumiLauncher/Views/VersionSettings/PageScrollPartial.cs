using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
    public void ScrollToTop()
    {
        if (FindScrollViewer(ModList) is { } viewer) viewer.ScrollToTop();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (FindScrollViewer(child) is { } found) return found;
        }

        return null;
    }
}
