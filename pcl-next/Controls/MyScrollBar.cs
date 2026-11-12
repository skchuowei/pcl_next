using System;
using MeloongCore;

namespace PCL
{
    public class MyScrollBar : System.Windows.Controls.Primitives.ScrollBar
    {

        // 基础

        public int Uuid = ModBase.GetUuid();

        public MyScrollBar()
        {
            IsEnabledChanged += (_, __) => RefreshColor();
            GotMouseCapture += (_, __) => RefreshColor();
            LostMouseCapture += (_, __) => RefreshColor();
            MouseEnter += (_, __) => RefreshColor();
            MouseLeave += (_, __) => RefreshColor();
            IsVisibleChanged += (_, __) => RefreshColor();
        }

        // 指向动画

        private void RefreshColor()
        {
            try
            {

                // 判断当前颜色
                double NewOpacity;
                string NewColor;
                int Time;
                if (!IsVisible)
                {
                    NewOpacity = 0d;
                    Time = 20; // 防止错误的尺寸判断导致闪烁
                    NewColor = "ColorBrushScrollBar";
                }
                else if (IsMouseCaptureWithin)
                {
                    NewOpacity = 1d;
                    NewColor = "ColorBrushScrollBarHover";
                    Time = 50;
                }
                else if (IsMouseOver)
                {
                    NewOpacity = 0.9d;
                    NewColor = "ColorBrushScrollBarHover";
                    Time = 130;
                }
                else
                {
                    NewOpacity = 0.5d;
                    NewColor = "ColorBrushScrollBar";
                    Time = 180;
                }
                // 触发颜色动画
                if (IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
                {
                    // 有动画
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this, ForegroundProperty, NewColor, Time), ModAnimation.AaOpacity(this, NewOpacity - Opacity, Time) }, "MyScrollBar Color " + Uuid);
                }
                else
                {
                    // 无动画
                    ModAnimation.AniStop("MyScrollBar Color " + Uuid);
                    SetResourceReference(ForegroundProperty, NewColor);
                    Opacity = NewOpacity;
                }
            }

            catch (Exception ex)
            {
                Logger.Warn(ex, "滚动条颜色改变出错");
            }
        }

    }
}