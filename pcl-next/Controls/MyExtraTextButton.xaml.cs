using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{

    [ContentProperty("Inlines")]
    public partial class MyExtraTextButton
    {

        // 声明
        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e); // 自定义事件

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
                if (this.Path is not null)
                    this.Path.RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale };
            }
        }
        // 显示文本
        public InlineCollection Inlines
        {
            get
            {
                return this.LabText.Inlines;
            }
        }
        public string Text
        {
            get
            {
                return Conversions.ToString(this.GetValue(TextProperty));
            }
            set
            {
                this.SetValue(TextProperty, value);
            }
        }
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyExtraTextButton), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (sender is not null) ((MyExtraTextButton)sender).LabText.Text = Conversions.ToString(e.NewValue); })));

        // 动画
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
                this.Opacity = 0d;
                ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this, 1d - this.Opacity, 80, 50), ModAnimation.AaScaleTransform(this, 0.15d - ((ScaleTransform)this.RenderTransform).ScaleX, 400, 50, new ModAnimation.AniEaseOutBack()), ModAnimation.AaScaleTransform(this, 0.85d, 160, 50, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Middle)) }, "MyExtraTextButton MainScale " + Uuid);
            }
            else
            {
                // 没了
                ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this, -this.Opacity, 50, 50), ModAnimation.AaScaleTransform(this, -((ScaleTransform)this.RenderTransform).ScaleX, 100, Ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)) }, "MyExtraTextButton MainScale " + Uuid);
            }
            this.IsHitTestVisible = value; // 防止缩放动画中依然可以点进去
        });
            }
        }

        public MyExtraTextButton()
        {
            InitializeComponent();
            this.PanClick.MouseLeftButtonUp += (p1, p2) => Button_LeftMouseUp((object)p1, (MouseButtonEventArgs)p2);
            this.PanClick.MouseLeftButtonDown += (p1, p2) => Button_LeftMouseDown((object)p1, (MouseButtonEventArgs)p2);
            this.PanClick.MouseLeftButtonUp += (p1, p2) => Button_LeftMouseUp((object)p1, (MouseButtonEventArgs)p2);
            this.PanClick.MouseRightButtonUp += (p1, p2) => Button_RightMouseUp();
            this.PanClick.MouseLeave += (p1, p2) => Button_MouseLeave();
            this.PanClick.MouseEnter += (p1, p2) => RefreshColor();
            this.PanClick.MouseLeave += (p1, p2) => RefreshColor();
            this.Loaded += (_, __) => RefreshColor();
            this.IsEnabledChanged += (_, __) => RefreshColor();
        }

        // 触发点击事件
        private void Button_LeftMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsLeftMouseHeld)
                return;
            Logger.Info($"按下附加图标按钮：{Text}");
            Click?.Invoke(sender, e);
            e.Handled = true;
            this.RaiseCustomEvent();
            Button_LeftMouseUp();
        }

        // 鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
        private bool IsLeftMouseHeld = false;
        private void Button_LeftMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsLeftMouseHeld)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 0.85d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), ModAnimation.AaScaleTransform(this.PanScale, -0.05d, 60, Ease: new ModAnimation.AniEaseOutFluent()) }, "MyExtraTextButton Scale " + Uuid);
            }
            IsLeftMouseHeld = true;
            this.Focus();
        }
        private void Button_LeftMouseUp()
        {
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 1d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 300, Ease: new ModAnimation.AniEaseOutBack()) }, "MyExtraTextButton Scale " + Uuid);
            IsLeftMouseHeld = false;
            RefreshColor(); // 直接刷新颜色以判断是否已触发 MouseLeave
        }
        private void Button_RightMouseUp()
        {
            if (!IsLeftMouseHeld)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 1d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 300, Ease: new ModAnimation.AniEaseOutBack()) }, "MyExtraTextButton Scale " + Uuid);
            }
            RefreshColor(); // 直接刷新颜色以判断是否已触发 MouseLeave
        }
        private void Button_MouseLeave()
        {
            IsLeftMouseHeld = false;
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanScale, 1d - ((ScaleTransform)this.PanScale.RenderTransform).ScaleX, 500, Ease: new ModAnimation.AniEaseOutFluent()) }, "MyExtraTextButton Scale " + Uuid);
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
                        ModAnimation.AniStart(ModAnimation.AaColor(this.PanColor, Panel.BackgroundProperty, "ColorBrushGray4", AnimationColorIn), "MyExtraTextButton Color " + Uuid);
                    }
                    else if (this.IsMouseOver)
                    {
                        // 指向
                        ModAnimation.AniStart(ModAnimation.AaColor(this.PanColor, Panel.BackgroundProperty, "ColorBrush4", AnimationColorIn), "MyExtraTextButton Color " + Uuid);
                    }
                    else
                    {
                        // 普通
                        ModAnimation.AniStart(ModAnimation.AaColor(this.PanColor, Panel.BackgroundProperty, "ColorBrush3", AnimationColorOut), "MyExtraTextButton Color " + Uuid);
                    }
                }

                else
                {

                    ModAnimation.AniStop("MyExtraTextButton Color " + Uuid);
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
                Logger.Warn(ex, "刷新附加图标按钮颜色出错");
            }
        }

    }
}