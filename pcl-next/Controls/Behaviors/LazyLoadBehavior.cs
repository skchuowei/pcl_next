using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL
{
    public class LazyLoadBehavior
    {

        /// <summary>
    /// 指定首次进入 ScrollViewer 的可视范围时执行的操作。
    /// </summary>
        public static void OnFirstEnterScrollViewerViewport(DependencyObject obj, Action value)
        {
            obj.SetValue(IsInViewportProperty, value);
        }

        private static readonly DependencyProperty IsInViewportProperty = DependencyProperty.RegisterAttached("IsInViewport", typeof(Action), typeof(LazyLoadBehavior), new PropertyMetadata(null, OnIsInViewportChanged));

        private static void OnIsInViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            FrameworkElement Element = d as FrameworkElement;
            if (Element is null || e.NewValue is null)
                return;

            bool Handled = false;
            EventHandler Handler = null;
            Handler = (_, __) =>
        {
            if (Handled)
                return;
            // 判断元素是否可见
            if (Element.RenderSize.Width < double.Epsilon)
                return;
            if (!Element.IsVisible)
                return;
            // 判断是否在 ScrollViewer 的可视区域内
            var Scroll = FindParentScrollViewer(Element);
            if (Scroll is null)
                return;
            if (!new Rect(0d, 0d, Scroll.ViewportWidth, Scroll.ViewportHeight).IntersectsWith(Element.TransformToAncestor(Scroll).TransformBounds(new Rect(new Point(0d, 0d), Element.RenderSize))))
                return;
            // 执行
            Handled = true;
            ((Action)e.NewValue).Invoke();
            Element.LayoutUpdated -= Handler;
        };
            Element.LayoutUpdated += Handler;
        }

        private static ScrollViewer FindParentScrollViewer(DependencyObject d)
        {
            while (d is not null)
            {
                if (d is ScrollViewer)
                    return (ScrollViewer)d;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

    }
}