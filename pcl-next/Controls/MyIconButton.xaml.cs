using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCL
{

    public partial class MyIconButton
    {

        // 自定义事件
        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, EventArgs e);

        // 自定义属性

        public int Uuid = ModBase.GetUuid();
        public string Logo
        {
            get
            {
                return this.Path.Data.ToString();
            }
            set
            {
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

        public enum Themes
        {
            Color,
            White,
            Black,
            Red,
            Custom
        }
        public Themes Theme { get; set; } = Themes.Color;

        private SolidColorBrush _Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        public SolidColorBrush Foreground
        {
            get
            {
                return _Foreground;
            }
            set
            {
                _Foreground = value;
                ModAnimation.AniControlEnabled += 1;
                RefreshAnim();
                ModAnimation.AniControlEnabled -= 1;
            }
        }

        // 鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
        private bool IsMouseDown = false;
        public bool CanClick { get; set; } = true;

        public MyIconButton()
        {
            InitializeComponent();
            this.MouseLeftButtonUp += Button_MouseUp;
            this.MouseLeftButtonDown += Button_MouseDown;
            this.MouseLeftButtonUp += (_, __) => Button_MouseUp();
            this.MouseLeave += (_, __) => Button_MouseLeave();
            this.MouseEnter += (_, __) => RefreshAnim();
            this.MouseLeave += (_, __) => RefreshAnim();
            this.Loaded += (_, __) => RefreshAnim();
            this.IsEnabledChanged += (_, __) => RefreshAnim();
        }
        private void Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!CanClick)
                return;
            if (!IsMouseDown)
                return;
            Logger.Info($"按下图标按钮{(string.IsNullOrEmpty(this.Name) ? "" : "：" + this.Name)}");
            Click?.Invoke(sender, e);
            e.Handled = true;
            Button_MouseUp();
            this.RaiseCustomEvent(); // 自定义事件
        }
        private void Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!CanClick)
                return;
            IsMouseDown = true;
            this.Focus();
            // 指向
            ModAnimation.AniStart(ModAnimation.AaScaleTransform(this.PanBack, 0.8d - ((ScaleTransform)this.PanBack.RenderTransform).ScaleX, 400, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), "MyIconButton Scale " + Uuid);
        }
        private void Button_MouseUp()
        {
            if (!CanClick)
                return;
            if (IsMouseDown)
            {
                IsMouseDown = false;
                ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanBack, 1.05d - ((ScaleTransform)this.PanBack.RenderTransform).ScaleX, 250, Ease: new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)), ModAnimation.AaScaleTransform(this.PanBack, -0.05d, 250, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)) }, "MyIconButton Scale " + Uuid);
            }
            RefreshAnim(); // 直接刷新颜色以判断是否已触发 MouseLeave
        }
        private void Button_MouseLeave()
        {
            if (!CanClick)
                return;
            IsMouseDown = false;
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanBack, 1d - ((ScaleTransform)this.PanBack.RenderTransform).ScaleX, 250, Ease: new ModAnimation.AniEaseOutFluent()) }, "MyIconButton Scale " + Uuid);
            RefreshAnim(); // 直接刷新颜色以判断是否已触发 MouseLeave
        }

        // 务必放在 IsMouseDown 更新之后
        private const int AnimationColorIn = 120;
        private const int AnimationColorOut = 150;
        public void RefreshAnim()
        {
            try
            {
                if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
                {
                    if (this.PanBack.Background is null)
                        this.PanBack.Background = new ModBase.MyColor(0d, 255d, 255d, 255d);
                    if (this.Path.Fill is null)
                    {
                        switch (Theme)
                        {
                            case Themes.Red:
                                {
                                    this.Path.Fill = new ModBase.MyColor(160d, 255d, 76d, 76d);
                                    break;
                                }
                            case Themes.Black:
                                {
                                    this.Path.Fill = new ModBase.MyColor(160d, 0d, 0d, 0d);
                                    break;
                                }
                            case Themes.Custom:
                                {
                                    this.Path.Fill = new ModBase.MyColor(160d, Foreground);
                                    break;
                                }
                        }
                    }
                    var AnimList = new List<ModAnimation.AniData>();
                    if (!this.IsEnabled)
                    {
                        AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, "ColorBrushGray5", AnimationColorIn));
                    }
                    else if (this.IsMouseOver) // 指向
                    {
                        switch (Theme)
                        {
                            case Themes.Color:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, "ColorBrush2", AnimationColorIn));
                                    break;
                                }
                            case Themes.White:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.PanBack, Border.BackgroundProperty, new ModBase.MyColor(50d, 255d, 255d, 255d) - this.PanBack.Background, AnimationColorIn));
                                    break;
                                }
                            case Themes.Red:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, new ModBase.MyColor(255d, 76d, 76d) - this.Path.Fill, AnimationColorIn));
                                    break;
                                }
                            case Themes.Black:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, new ModBase.MyColor(230d, 0d, 0d, 0d) - this.Path.Fill, AnimationColorIn));
                                    break;
                                }
                            case Themes.Custom:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, new ModBase.MyColor(255d, Foreground) - this.Path.Fill, AnimationColorIn));
                                    break;
                                }
                        }
                    }
                    else // 普通
                    {
                        switch (Theme)
                        {
                            case Themes.Color:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, "ColorBrush4", AnimationColorOut));
                                    this.PanBack.Background = new ModBase.MyColor(0d, 255d, 255d, 255d);
                                    break;
                                }
                            case Themes.White:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, "ColorBrush8", AnimationColorOut));
                                    AnimList.Add(ModAnimation.AaColor(this.PanBack, Border.BackgroundProperty, new ModBase.MyColor(0d, 255d, 255d, 255d) - this.PanBack.Background, AnimationColorOut));
                                    break;
                                }
                            case Themes.Red:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, new ModBase.MyColor(160d, 255d, 76d, 76d) - this.Path.Fill, AnimationColorOut));
                                    this.PanBack.Background = new ModBase.MyColor(0d, 255d, 255d, 255d);
                                    break;
                                }
                            case Themes.Black:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, new ModBase.MyColor(160d, 0d, 0d, 0d) - this.Path.Fill, AnimationColorOut));
                                    this.PanBack.Background = new ModBase.MyColor(0d, 255d, 255d, 255d);
                                    break;
                                }
                            case Themes.Custom:
                                {
                                    AnimList.Add(ModAnimation.AaColor(this.Path, Shape.FillProperty, new ModBase.MyColor(160d, Foreground) - this.Path.Fill, AnimationColorOut));
                                    this.PanBack.Background = new ModBase.MyColor(0d, 255d, 255d, 255d);
                                    break;
                                }
                        }
                    }
                    ModAnimation.AniStart(AnimList, "MyIconButton Color " + Uuid);
                }
                else
                {
                    ModAnimation.AniStop("MyIconButton Color " + Uuid);
                    if (this.IsEnabled)
                    {
                        switch (Theme)
                        {
                            case Themes.Color:
                                {
                                    this.Path.SetResourceReference(Shape.FillProperty, "ColorBrush5");
                                    break;
                                }
                            case Themes.White:
                                {
                                    this.Path.SetResourceReference(Shape.FillProperty, "ColorBrush8");
                                    break;
                                }
                            case Themes.Red:
                                {
                                    this.Path.Fill = new ModBase.MyColor(160d, 255d, 76d, 76d);
                                    break;
                                }
                            case Themes.Black:
                                {
                                    this.Path.Fill = new ModBase.MyColor(160d, 0d, 0d, 0d);
                                    break;
                                }
                            case Themes.Custom:
                                {
                                    this.Path.Fill = new ModBase.MyColor(160d, Foreground);
                                    break;
                                }
                        }
                    }
                    else
                    {
                        this.Path.SetResourceReference(Shape.FillProperty, "ColorBrushGray5");
                    }
                    this.PanBack.Background = new ModBase.MyColor(0d, 255d, 255d, 255d);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新图标按钮动画状态出错");
            }
        }

    }

    public static partial class ModAnimation
    {
        public static void AniDispose(MyIconButton Control, bool RemoveFromChildren, ParameterizedThreadStart CallBack = null)
        {
            if (!Control.IsHitTestVisible)
                return;
            Control.IsHitTestVisible = false;
            AniStart(new[] {
                 AaScaleTransform(Control, -1.5d, 200, Ease: new AniEaseInFluent()),
                 ModAnimation.AaCode(() =>
                {
                            if (RemoveFromChildren)
                    {
                                ((dynamic)((object)Control.Parent)).Children.Remove(Control);
                    }
                            else
                    {
                                Control.Visibility = Visibility.Collapsed;
                            }
                            if (CallBack is not null)
                        CallBack(Control);
                        }, After: true)
        }, "MyIconButton Dispose " + Control.Uuid);
        }
    }
}