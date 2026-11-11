using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class MyExtraButton
    {

        // 声明
        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e); // 自定义事件
        public event RightClickEventHandler RightClick;

        public delegate void RightClickEventHandler(object sender, MouseButtonEventArgs e);

        // 进度条
        private double _Progress = 0d;
        public double Progress
        {
            get
            {
                return _Progress;
            }
            set
            {
                if (_Progress == value)
                    return;
                _Progress = value;
                if (value < 0.0001d)
                {
                    this.PanProgress.Visibility = Visibility.Collapsed;
                }
                else
                {
                    this.PanProgress.Visibility = Visibility.Visible;
                    this.RectProgress.Rect = new Rect(0d, 40d * (1d - value), 40d, 40d * value);
                }
            }
        }

        // 自定义属性
        public int Uuid = ModBase.GetUuid();
        private string _Logo = "";
        public string Logo
        {
            get
            {
                return _Logo;
            }
            set
            {
                if ((value ?? "") == (_Logo ?? ""))
                    return;
                _Logo = value;
                this.Path.Data = (Geometry)new GeometryConverter().ConvertFromString(value);
            }
        }
        private double _LogoScale = 1d;
        public double LogoScale
        {
            get
            {
                return _LogoScale;
            }
            set
            {
                _LogoScale = value;
                if (!(this.Path == null))
                    this.Path.RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale };
            }
        }
        private bool _Show = false;
        public bool Show
        {
            get
            {
                return _Show;
            }
            set
            {
                if (_Show == value)
                    return;
                _Show = value;
                ModBase.RunInUi(() =>
        {
            if (value)
            {
                // 有了
                this.Visibility = Visibility.Visible;
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this, 0.3d - ((ScaleTransform)this.RenderTransform).ScaleX, 500, 60, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaScaleTransform(this, 0.7d, 500, 60, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)), ModAnimation.AaHeight(this, 50d - this.Height, 200, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)) }, "MyExtraButton MainScale " + Uuid);
            }
            else
            {
                // 没了
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this, -((ScaleTransform)this.RenderTransform).ScaleX, 100, Ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaHeight(this, -this.Height, 400, 100, new ModAnimation.AniEaseOutFluent()), ModAnimation.AaCode(() => this.Visibility = Visibility.Collapsed, After: true) }, "MyExtraButton MainScale " + Uuid);
            }
            this.IsHitTestVisible = value; // 防止缩放动画中依然可以点进去
        });
            }
        }
        public delegate bool ShowCheckDelegate();
        public ShowCheckDelegate ShowCheck = null;

        public MyExtraButton()
        {
            this.Loaded += (_, __) => RefreshColor();
            this.IsEnabledChanged += (_, __) => RefreshColor();
        }
        public void ShowRefresh()
        {
            if (ShowCheck is not null)
                Show = ShowCheck();
        }

        // 触发点击事件
        private void Button_LeftMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsLeftMouseHeld)
            {
                Logger.Info($"按下附加按钮{(Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(this.ToolTip, "", false)) ? "" : Operators.ConcatenateObject("：", this.ToolTip))}");
                Click?.Invoke(sender, e);
                e.Handled = true;
                Button_LeftMouseUp();
            }
        }
        private void Button_RightMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsRightMouseHeld)
            {
                Logger.Info($"右键按下附加按钮{(Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(this.ToolTip, "", false)) ? "" : Operators.ConcatenateObject("：", this.ToolTip))}");
                RightClick?.Invoke(sender, e);
                e.Handled = true;
                Button_RightMouseUp();
            }
        }
        private bool _CanRightClick = false;
        public bool CanRightClick
        {
            get
            {
                return _CanRightClick;
            }
            set
            {
                _CanRightClick = value;
            }
        }

        // 鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
        private bool IsLeftMouseHeld = false;
        private bool IsRightMouseHeld = false;
        private void Button_LeftMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsLeftMouseHeld && !IsRightMouseHeld)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 0.85d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), ModAnimation.AaScaleTransform(this.PanScale, -0.05d, 60, Ease: new ModAnimation.AniEaseOutFluent()) }, "MyExtraButton Scale " + Uuid);
            }
            IsLeftMouseHeld = true;
            this.Focus();
        }
        private void Button_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!CanRightClick)
                return;
            if (!IsLeftMouseHeld && !IsRightMouseHeld)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 0.85d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), ModAnimation.AaScaleTransform(this.PanScale, -0.05d, 60, Ease: new ModAnimation.AniEaseOutFluent()) }, "MyExtraButton Scale " + Uuid);
            }
            IsRightMouseHeld = true;
            this.Focus();
        }
        private void Button_LeftMouseUp()
        {
            if (!IsRightMouseHeld)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 1d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 300, Ease: new ModAnimation.AniEaseOutBack()) }, "MyExtraButton Scale " + Uuid);
            }
            if (IsLeftMouseHeld)
                this.RaiseCustomEvent();
            IsLeftMouseHeld = false;
            RefreshColor(); // 直接刷新颜色以判断是否已触发 MouseLeave
        }
        private void Button_RightMouseUp()
        {
            if (!CanRightClick)
                return;
            if (!IsLeftMouseHeld)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 1d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 300, Ease: new ModAnimation.AniEaseOutBack()) }, "MyExtraButton Scale " + Uuid);
            }
            IsRightMouseHeld = false;
            RefreshColor(); // 直接刷新颜色以判断是否已触发 MouseLeave
        }
        private void Button_MouseLeave()
        {
            IsLeftMouseHeld = false;
            IsRightMouseHeld = false;
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 1d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 500, Ease: new ModAnimation.AniEaseOutFluent()) }, "MyExtraButton Scale " + Uuid);
            RefreshColor(); // 直接刷新颜色以判断是否已触发 MouseLeave
        }

        // 自定义事件
        // 务必放在 IsMouseDown 更新之后
        private const int AnimationColorIn = 120;
        private const int AnimationColorOut = 150;
        public void RefreshColor()
        {
            try
            {
                if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
                {

                    if (!this.IsEnabled)
                    {
                        // 禁用
                        ModAnimation.AniStart(ModAnimation.AaColor(this.PanColor, Panel.BackgroundProperty, "ColorBrushGray4", AnimationColorIn), "MyExtraButton Color " + Uuid);
                    }
                    else if (this.IsMouseOver)
                    {
                        // 指向
                        ModAnimation.AniStart(ModAnimation.AaColor(this.PanColor, Panel.BackgroundProperty, "ColorBrush4", AnimationColorIn), "MyExtraButton Color " + Uuid);
                    }
                    else
                    {
                        // 普通
                        ModAnimation.AniStart(ModAnimation.AaColor(this.PanColor, Panel.BackgroundProperty, "ColorBrush3", AnimationColorOut), "MyExtraButton Color " + Uuid);
                    }
                }

                else
                {

                    ModAnimation.AniStop("MyExtraButton Color " + Uuid);
                    if (!this.IsEnabled)
                    {
                        this.PanColor.SetResourceReference(Panel.BackgroundProperty, "ColorBrushGray4");
                    }
                    else if (this.IsMouseOver)
                    {
                        this.PanColor.SetResourceReference(Panel.BackgroundProperty, "ColorBrush4");
                    }
                    else
                    {
                        this.PanColor.SetResourceReference(Panel.BackgroundProperty, "ColorBrush3");
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新图标按钮颜色出错");
            }
        }

        /// <summary>
    /// 发出一圈波浪效果提示。
    /// </summary>
        public void Ribble()
        {
            ModBase.RunInUi(() =>
        {
            var Shape = new Border() { CornerRadius = new CornerRadius(1000d), BorderThickness = new Thickness(0.001d), Opacity = 0.5d, RenderTransformOrigin = new Point(0.5d, 0.5d), RenderTransform = new ScaleTransform() };
            Shape.SetResourceReference(Border.BackgroundProperty, "ColorBrush5");
            this.PanScale.Children.Insert(0, Shape);
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(Shape, 13d, 1000, Ease: new ModAnimation.AniEaseInoutFluent(ModAnimation.AniEasePower.Strong, 0.3d)), ModAnimation.AaOpacity(Shape, -Shape.Opacity, 1000), ModAnimation.AaCode(() => this.PanScale.Children.Remove(Shape), After: true) }, "ExtraButton Ribble " + ModBase.GetUuid());
        });
        }

    }
}