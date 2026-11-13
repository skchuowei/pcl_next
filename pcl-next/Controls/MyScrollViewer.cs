using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public class MyScrollViewer : ScrollViewer
    {

        public double DeltaMult { get; set; } = 1d;

        private double RealOffset;

        public MyScrollViewer()
        {
            PreviewMouseWheel += MyScrollViewer_PreviewMouseWheel;
            ScrollChanged += MyScrollViewer_ScrollChanged;
            IsVisibleChanged += MyScrollViewer_IsVisibleChanged;
            Loaded += (_, __) => Load();
            PreviewGotKeyboardFocus += MyScrollViewer_PreviewGotKeyboardFocus;
        }
        private void MyScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta == 0 || ActualHeight == 0d || ScrollableHeight == 0d)
                return;
            var SourceType = e.Source.GetType();
            if (((dynamic)Content).TemplatedParent is null && (typeof(ComboBox).IsAssignableFrom(SourceType) && ((ComboBox)e.Source).IsDropDownOpen || typeof(TextBox).IsAssignableFrom(SourceType) && ((TextBox)e.Source).AcceptsReturn || typeof(ComboBoxItem).IsAssignableFrom(SourceType) || e.Source is Visual && PresentationSource.FromVisual((Visual)e.Source)?.RootVisual?.GetType().FullName == "System.Windows.Controls.Primitives.PopupRoot"))
            {
                // 如果当前是在对有滚动条的下拉框或文本框执行，则不接管操作
                return;
            }
            e.Handled = true;
            PerformVerticalOffsetDelta(-e.Delta);
        }
        // 滚动动画改由渲染帧驱动，不再挂在 ModAnimation 的后台定时器上。
        // 原因（2026-11-13 实测日志）：ModAnimation 的动画线程每帧都要 RunInUiWait → Dispatcher.Invoke 排队，
        // 实测帧间隔 124~398 ms（3~8 FPS）。原实现把一个 300 ms 的 AaDouble 动画交给这条时钟，
        // 等于整个滚动只有 1~2 帧被执行，画面「一步跳到位」，用户感知就是「滚动惯性没了」。
        // 这里保留原版完全相同的语义（时长 300 ms、AniEaseOutFluent(6) 的同一条曲线、像素增量累加），
        // 只把驱动时钟换成 CompositionTarget.Rendering —— 它在每次 WPF 渲染帧回调（约 60 Hz，UI 线程内），
        // 不受后台动画线程排队延迟影响，因此低帧率时也不会被压成「瞬移」。
        private const int ScrollAnimTime = 300;
        private bool isScrollAnimating;
        private double scrollAnimFrom;
        private double scrollAnimTo;
        private long scrollAnimStart;

        public void PerformVerticalOffsetDelta(double Delta)
        {
            if (ActualHeight == 0d || ExtentHeight - ActualHeight <= 0d)
                return;
            long Now = Environment.TickCount64;
            // 上一段动画已接近尾声（剩不到 100 ms）时不再续接，直接以当前位置为新起点重开，
            // 免得剩余时间不够、最后一帧一次性跳完剩余距离。
            if (isScrollAnimating && Now - scrollAnimStart >= ScrollAnimTime - 100)
                isScrollAnimating = false;
            if (!isScrollAnimating)
            {
                // 必须以「当前实际位置」为基准：用户中途拖过滚动条时，旧目标值不能再用
                scrollAnimFrom = VerticalOffset;
                scrollAnimTo = VerticalOffset;
                scrollAnimStart = Now;
            }
            // 连续滚轮时在既有目标上继续叠加，保持原版的「惯性累积」手感
            scrollAnimTo = (scrollAnimTo + Delta * DeltaMult).Clamp(0d, ExtentHeight - ActualHeight);
            if (isScrollAnimating)
                return; // 已有动画在跑，只更新目标，交给同一个渲染帧回调继续推进
            double Target = scrollAnimTo;
            if (Target - scrollAnimFrom is > -0.5d and < 0.5d)
            {
                // 位移可忽略：直接落位，并确保渲染帧回调不残留订阅（否则会每帧空转并重复订阅）
                CompositionTarget.Rendering -= ScrollAnimOnRendering;
                isScrollAnimating = false;
                return;
            }
            isScrollAnimating = true;
            CompositionTarget.Rendering += ScrollAnimOnRendering;
        }

        private void ScrollAnimOnRendering(object sender, EventArgs e)
        {
            if (!isScrollAnimating)
                return;
            double Progress = (Environment.TickCount64 - scrollAnimStart) / (double)ScrollAnimTime;
            if (Progress > 1d || !IsVisible)
                Progress = 1d; // 被切走/最小化时直接落位，避免动画卡在中间
            if (Progress < 0d)
                Progress = 0d;
            // 与原版 AniEaseOutFluent(6) 一致：AniEase.GetDelta 的累积值即 1 - (1-t)^6
            double Eased = 1d - Math.Pow(1d - Progress, 6d);
            RealOffset = (scrollAnimFrom + (scrollAnimTo - scrollAnimFrom) * Eased).Clamp(0d, ExtentHeight - ActualHeight);
            ScrollToVerticalOffset(RealOffset);
            if (Progress >= 1d)
            {
                CompositionTarget.Rendering -= ScrollAnimOnRendering;
                isScrollAnimating = false;
                RealOffset = VerticalOffset;
            }
        }

        private void MyScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!isScrollAnimating)
                RealOffset = VerticalOffset;
            if (ModMain.FrmMain is not null && (Conversions.ToBoolean(e.VerticalChange) || Conversions.ToBoolean(e.ViewportHeightChange)))
                ModMain.FrmMain.BtnExtraBack.ShowRefresh();
        }
        private void MyScrollViewer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ModMain.FrmMain.BtnExtraBack.ShowRefresh();
        }

        public MyScrollBar ScrollBar;
        private void Load()
        {
            ScrollBar = (MyScrollBar)GetTemplateChild("PART_VerticalScrollBar");
        }

        private void MyScrollViewer_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is not null && e.NewFocus is MySlider)
                e.Handled = true; // #3854，阻止获得焦点时自动滚动
        }
    }
}