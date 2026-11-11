using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{

    [ContentProperty("Inlines")]
    public partial class MyIconTextButton
    {

        // 基础

        public int Uuid = ModBase.GetUuid();
        // 保留上游公开事件：外部（XAML 或调用方）可能订阅，当前代码内未触发，故豁免 CS0067
#pragma warning disable CS0067
        public event CheckEventHandler Check;

        public delegate void CheckEventHandler(object sender, bool raiseByMouse);
        public event ChangeEventHandler Change;
#pragma warning restore CS0067

        public delegate void ChangeEventHandler(object sender, bool raiseByMouse);

        // 自定义属性

        public string Logo
        {
            get
            {
                return this.ShapeLogo.Data.ToString();
            }
            set
            {
                this.ShapeLogo.Data = (Geometry)new GeometryConverter().ConvertFromString(value);
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
                if (!(this.ShapeLogo == null))
                    this.ShapeLogo.RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale };
            }
        }

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
        } // 内容
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyIconTextButton), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (!(sender == null)) ((MyIconTextButton)sender).LabText.Text = Conversions.ToString(e.NewValue); })));
        public enum ColorState
        {
            Black,
            Highlight
        }
        public ColorState ColorType
        {
            get
            {
                return (ColorState)Conversions.ToInteger(this.GetValue(ColorTypeProperty));
            }
            set
            {
                if (ColorType == value)
                    return;
                this.SetValue(ColorTypeProperty, value);
                RefreshColor();
            }
        } // 颜色类别
        public static readonly DependencyProperty ColorTypeProperty = DependencyProperty.Register("ColorType", typeof(ColorState), typeof(MyIconTextButton), new PropertyMetadata(ColorState.Black));

        // 点击事件

        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, ModBase.RouteEventArgs e);
        private bool IsMouseDown = false;

        public MyIconTextButton()
        {
            this.MouseLeftButtonUp += (_, __) => MyIconTextButton_MouseUp();
            this.MouseLeftButtonDown += (_, __) => MyIconTextButton_MouseDown();
            this.MouseLeave += (_, __) => MyIconTextButton_MouseLeave();
            this.MouseEnter += (_, __) => RefreshColor();
            this.Loaded += (_, __) => RefreshColor();
            this.IsEnabledChanged += (_, __) => RefreshColor();
        }
        private void MyIconTextButton_MouseUp()
        {
            if (!IsMouseDown)
                return;
            Logger.Info($"按下带图标按钮：{Text}");
            IsMouseDown = false;
            Click?.Invoke(this, new ModBase.RouteEventArgs(true));
            this.RaiseCustomEvent(); // 自定义事件
            RefreshColor();
        }
        private void MyIconTextButton_MouseDown()
        {
            IsMouseDown = true;
            RefreshColor();
        }
        private void MyIconTextButton_MouseLeave()
        {
            IsMouseDown = false;
            RefreshColor();
        }

        // 动画

        private const int AnimationTimeOfMouseIn = 100; // 鼠标指向动画长度
        private const int AnimationTimeOfMouseOut = 150; // 鼠标移出动画长度
        private void RefreshColor(object obj = null, object e = null)
        {
            try
            {
                if (this.IsLoaded && ModAnimation.AniControlEnabled == 0 && !false.Equals(e)) // 防止默认属性变更触发动画，若强制不执行动画，则 e 为 False
                {

                    switch (ColorType)
                    {
                        case ColorState.Black:
                            {
                                if (IsMouseDown)
                                {
                                    // 按下
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, "ColorBrush6", 70), "MyIconTextButton Color " + Uuid);
                                }
                                else if (this.IsMouseOver)
                                {
                                    // 指向
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrush3", AnimationTimeOfMouseIn), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseIn) }, "MyIconTextButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, "ColorBrushBg1", AnimationTimeOfMouseIn), "MyIconTextButton Color " + Uuid);
                                }
                                else if (this.IsEnabled)
                                {
                                    // 正常
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrush1", AnimationTimeOfMouseOut), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush1", AnimationTimeOfMouseOut) }, "MyIconTextButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, ModSecret.ColorSemiTransparent - this.Background, AnimationTimeOfMouseOut), "MyIconTextButton Color " + Uuid);
                                }
                                else
                                {
                                    // 禁用
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrushGray5", 100), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrushGray5", 100) }, "MyIconTextButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, ModSecret.ColorSemiTransparent - this.Background, AnimationTimeOfMouseOut), "MyIconTextButton Color " + Uuid);
                                }

                                break;
                            }
                        case ColorState.Highlight:
                            {
                                if (IsMouseDown)
                                {
                                    // 按下
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, "ColorBrush6", 70), "MyIconTextButton Color " + Uuid);
                                }
                                else if (this.IsMouseOver)
                                {
                                    // 指向
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrush3", AnimationTimeOfMouseIn), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseIn) }, "MyIconTextButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, "ColorBrushBg1", AnimationTimeOfMouseIn), "MyIconTextButton Color " + Uuid);
                                }
                                else if (this.IsEnabled)
                                {
                                    // 正常
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrush3", AnimationTimeOfMouseOut), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseOut) }, "MyIconTextButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, ModSecret.ColorSemiTransparent - this.Background, AnimationTimeOfMouseOut), "MyIconTextButton Color " + Uuid);
                                }
                                else
                                {
                                    // 禁用
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrushGray5", 100), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrushGray5", 100) }, "MyIconTextButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, ModSecret.ColorSemiTransparent - this.Background, AnimationTimeOfMouseOut), "MyIconTextButton Color " + Uuid);
                                }

                                break;
                            }
                    }
                }

                else
                {

                    // 不使用动画
                    ModAnimation.AniStop("MyIconTextButton Checked " + Uuid);
                    ModAnimation.AniStop("MyIconTextButton Color " + Uuid);
                    switch (ColorType)
                    {
                        case ColorState.Black:
                            {
                                this.Background = ModSecret.ColorSemiTransparent;
                                this.ShapeLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5");
                                this.LabText.SetResourceReference(TextBlock.ForegroundProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5");
                                break;
                            }
                        case ColorState.Highlight:
                            {
                                this.Background = ModSecret.ColorSemiTransparent;
                                this.ShapeLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, this.IsEnabled ? "ColorBrush3" : "ColorBrushGray5");
                                this.LabText.SetResourceReference(TextBlock.ForegroundProperty, this.IsEnabled ? "ColorBrush3" : "ColorBrushGray5");
                                break;
                            }
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新带图标按钮颜色出错");
            }
        }

    }
}