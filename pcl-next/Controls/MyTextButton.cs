using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public class MyTextButton : Label
    {

        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, EventArgs e);

        // 基础

        public int Uuid = ModBase.GetUuid();
        public MyTextButton()
        {
            SetResourceReference(ForegroundProperty, "ColorBrush1");
            Background = ModSecret.ColorSemiTransparent;
            PreviewMouseLeftButtonDown += MyTextButton_MouseLeftButtonDown;
            MouseLeave += (_, __) => MyTextButton_MouseLeave();
            PreviewMouseLeftButtonUp += MyTextButton_MouseLeftButtonUp;
            MouseEnter += (_, __) => RefreshColor();
            MouseLeave += (_, __) => RefreshColor();
            IsEnabledChanged += (_, __) => RefreshColor();
            MouseLeftButtonDown += (_, __) => RefreshColor();
            MouseLeftButtonUp += (_, __) => RefreshColor();
        }

        // 文本

        public string Text
        {
            get
            {
                return Conversions.ToString(GetValue(TextProperty));
            }
            set
            {
                SetValue(TextProperty, value);
            }
        }
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyTextButton), new PropertyMetadata("", (sender, e) =>
{
if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(e.OldValue, e.NewValue, false)))
    return;
ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(sender, -((dynamic)sender).Opacity, 50), ModAnimation.AaCode(() => ((dynamic)sender).Content = e.NewValue, After: true), ModAnimation.AaOpacity(sender, 1d, 170) }, "MyTextButton Text " + ((dynamic)sender).Uuid);
}));

        // 鼠标事件

        public bool IsMouseDown = false;
        private void MyTextButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            IsMouseDown = true;
            e.Handled = true;
        }
        private void MyTextButton_MouseLeave()
        {
            IsMouseDown = false;
        }
        private void MyTextButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsMouseDown)
                return;
            IsMouseDown = false;
            Logger.Info($"按下文本按钮：{Text}");
            Click?.Invoke(this, null);
            this.RaiseCustomEvent();
            e.Handled = true;
        }

        // 指向动画

        private const int AnimationTimeIn = 100;
        private const int AnimationTimeOut = 200;
        private string ColorName;
        private void RefreshColor()
        {
            // 判断当前颜色
            string ForeName;
            int Time;
            if (IsMouseDown)
            {
                ForeName = "ColorBrush4";
                Time = 30;
            }
            else if (IsMouseOver)
            {
                ForeName = "ColorBrush3";
                Time = AnimationTimeIn;
            }
            else
            {
                ForeName = "ColorBrush1";
                Time = AnimationTimeOut;
            }
            // 重复性验证
            if ((ColorName ?? "") == (ForeName ?? ""))
                return;
            ColorName = ForeName;
            // 触发颜色动画
            if (IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
            {
                // 有动画
                ModAnimation.AniStart(ModAnimation.AaColor(this, ForegroundProperty, ForeName, Time), "MyTextButton Color " + Uuid);
            }
            else
            {
                // 无动画
                ModAnimation.AniStop("MyTextButton Color " + Uuid);
                SetResourceReference(ForegroundProperty, ForeName);
            }
        }

    }
}