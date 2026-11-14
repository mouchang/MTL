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
        Apply();

        // 列表刚换过 ItemsSource 时，可视树可能还没重建完：布局完成后再补一次
        Dispatcher.BeginInvoke(new Action(Apply),
            System.Windows.Threading.DispatcherPriority.Loaded);

        void Apply()
        {
            ModList.ApplyTemplate();

            // 直接问模板要 PART_Scroll：比遍历可视树可靠，列表刚换数据时也不会扑空
            if (ModList.Template?.FindName("PART_Scroll", ModList) is not ScrollViewer viewer)
            {
                Services.AppLog.Info("mods scroll top: PART_Scroll not found");
                return;
            }

            var before = viewer.VerticalOffset;
            viewer.ScrollToTop();
            viewer.UpdateLayout();

            Services.AppLog.Info($"mods scroll top: {before:0.#} -> {viewer.VerticalOffset:0.#}");
        }
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
