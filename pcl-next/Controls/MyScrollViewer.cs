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
        public void PerformVerticalOffsetDelta(double Delta)
        {
            // VB 原文：AaDouble(Sub(AnimDelta As Double) ... End Sub, Delta * DeltaMult, 300, , New AniEaseOutFluent(6))
            // 转换产物有两处与 VB 对不上，下面逐条按原义修正：
            // 1) 该重载的形参是 ParameterizedThreadStart（Object 入参），VB 的 Sub(AnimDelta As Double) 靠
            //    Option Strict Off 的宽松委托转换才成立；C# 必须把形参写成 object，再按 VB 隐式转换的语义转回 double。
            //    （转换产物写成 (double AnimDelta) 使 lambda 无法匹配该重载，重载决议掉到 AaDouble(Object, DependencyProperty, ...) 上，
            //      于是报「参数 2 无法从 double 转为 DependencyProperty」。）
            // 2) VB 调用跳过了第 4 个可选参数 Delay（取默认 0），Ease 是第 5 个实参；转换器漏了 Delay，把 Ease 顶到了 Delay 位置上
            //    （报「参数 5 无法从 AniEaseOutFluent 转为 int」）。此处显式补 0。
            // 另：VB 的 New AniEaseOutFluent(6) 传入整数 6（Option Strict Off 下转成枚举，底层值即 6），
            //    故保留 (AniEasePower)6，与原值一致。
            ModAnimation.AniStart(ModAnimation.AaDouble((object AnimDelta) =>
            {
                double animDelta = Conversions.ToDouble(AnimDelta);
                RealOffset = (RealOffset + animDelta).Clamp(0d, ExtentHeight - ActualHeight);
                ScrollToVerticalOffset(RealOffset);
            }, Delta * DeltaMult, 300, 0, new ModAnimation.AniEaseOutFluent((ModAnimation.AniEasePower)6)));
        }
        private void MyScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
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