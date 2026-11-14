using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MutsumiLauncher.Controls;

/// <summary>
/// 一块跟随选中项的「滑动高亮」。
///
/// 位置和尺寸都从目标控件实际布局里量（TransformToAncestor + ActualWidth/Height），
/// 所以顶栏那种不等宽的页签、下载页那种 52/38 混排的侧栏都能用同一个实现。
/// </summary>
public static class SlidingHighlight
{
    /// <summary>把高亮块移到目标控件的位置，并让它和目标同高同宽。</summary>
    public static void Move(Border indicator, FrameworkElement target, FrameworkElement container,
        bool animate, double heightInset = 0, bool allowRetry = true)
    {
        if (!target.IsArrangeValid || target.ActualWidth <= 0)
        {
            // 布局还没完成：等一轮再放（只重试一次，避免无效目标导致死循环）
            if (!allowRetry) return;

            container.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (target.ActualWidth > 0)
                    Move(indicator, target, container, false, heightInset, allowRetry: false);
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            return;
        }

        double x, y;

        try
        {
            var offset = target.TransformToAncestor(container).Transform(new Point(0, 0));
            x = offset.X;
            y = offset.Y;
        }
        catch
        {
            return;
        }

        var width = target.ActualWidth;
        var height = Math.Max(8, target.ActualHeight - heightInset);
        var duration = animate ? TimeSpan.FromMilliseconds(230) : TimeSpan.Zero;

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        indicator.BeginAnimation(FrameworkElement.WidthProperty, Animate(width, duration, ease));
        indicator.BeginAnimation(FrameworkElement.HeightProperty, Animate(height, duration, ease));

        var translate = indicator.RenderTransform as TranslateTransform;

        if (translate is null)
        {
            translate = new TranslateTransform();
            indicator.RenderTransform = translate;
        }

        translate.BeginAnimation(TranslateTransform.XProperty, Animate(x, duration, ease));
        translate.BeginAnimation(TranslateTransform.YProperty,
            Animate(y + heightInset / 2, duration, ease));

        if (indicator.Opacity < 0.99) indicator.Opacity = 1;
    }

    private static DoubleAnimation Animate(double to, TimeSpan duration, IEasingFunction ease)
        => new(to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
}
