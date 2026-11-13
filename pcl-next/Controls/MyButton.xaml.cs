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
    public partial class MyButton
    {

        // 声明
        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e); // 自定义事件

        // 自定义属性
        public int Uuid = ModBase.GetUuid();
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
        } // 显示文本
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyButton), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (sender is not null) ((MyButton)sender).LabText.Text = Conversions.ToString(e.NewValue); })));
        public Thickness TextPadding
        {
            get
            {
                return this.LabText.Padding;
            }
            set
            {
                this.LabText.Padding = value;
            }
        }
        private ColorState _ColorType = ColorState.Normal;  // 配色方案
        public ColorState ColorType
        {
            get
            {
                return _ColorType;
            }
            set
            {
                _ColorType = value;
                RefreshColor();
            }
        }
        public enum ColorState
        {
            Normal = 0,
            Highlight = 1,
            Red = 2
        }
        // 属性穿透
        public static new readonly DependencyProperty PaddingProperty = DependencyProperty.Register("Padding", typeof(Thickness), typeof(MyButton), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (sender is not null) ((dynamic)sender).PanFore.Padding = (Thickness)e.NewValue; })));
        public new Thickness Padding
        {
            get
            {
                return this.PanFore.Padding;
            }
            set
            {
                this.PanFore.Padding = value;
            }
        }
        public Transform RealRenderTransform
        {
            get
            {
                return this.PanFore.RenderTransform;
            }
            set
            {
                this.PanFore.RenderTransform = value;
            }
        }

        // 自定义事件
        private const int AnimationColorIn = 100;
        private const int AnimationColorOut = 200;

        public MyButton()
        {
            InitializeComponent();
            this.MouseEnter += (_, __) => RefreshColor();
            this.MouseLeave += (_, __) => RefreshColor();
            this.Loaded += (_, __) => RefreshColor();
            this.IsEnabledChanged += (_, __) => RefreshColor();
            this.MouseLeftButtonUp += Button_MouseUp;
            this.MouseLeftButtonDown += Button_MouseDown;
            this.MouseEnter += (_, __) => Button_MouseEnter();
            this.MouseLeftButtonUp += (_, __) => Button_MouseUp();
            this.MouseLeave += (_, __) => Button_MouseLeave();
        }
        private void RefreshColor(object obj = null, object e = null)
        {
            try
            {
                if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
                {

                    if (this.IsEnabled)
                    {
                        switch (ColorType)
                        {
                            case ColorState.Normal:
                                {
                                    if (this.IsMouseOver)
                                    {
                                        // 指向（按钮悬停色）
                                        ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrushButtonHover", AnimationColorIn) }, "MyButton Color " + Uuid);
                                    }
                                    else
                                    {
                                        // 普通（按钮默认色）
                                        ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrushButton", AnimationColorOut) }, "MyButton Color " + Uuid);
                                    }

                                    break;
                                }
                            case ColorState.Highlight:
                                {
                                    if (this.IsMouseOver)
                                    {
                                        // 指向（按钮悬停色）
                                        ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrushButtonHover", AnimationColorIn) }, "MyButton Color " + Uuid);
                                    }
                                    else
                                    {
                                        // 高亮（Main 2）
                                        ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrush2", AnimationColorOut) }, "MyButton Color " + Uuid);
                                    }

                                    break;
                                }
                            case ColorState.Red:
                                {
                                    if (this.IsMouseOver)
                                    {
                                        // 红色指向
                                        ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrushRedLight", AnimationColorIn) }, "MyButton Color " + Uuid);
                                    }
                                    else
                                    {
                                        // 红色
                                        ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrushRedDark", AnimationColorOut) }, "MyButton Color " + Uuid);
                                    }

                                    break;
                                }
                        }
                    }
                    else
                    {
                        // 不可用（按钮禁用色）
                        ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrushButtonDisabled", AnimationColorOut) }, "MyButton Color " + Uuid);
                    }
                }
                else
                {

                    ModAnimation.AniStop("MyButton Color " + Uuid);
                    if (this.IsEnabled)
                    {
                        switch (ColorType)
                        {
                            case ColorState.Normal:
                                {
                                    if (this.IsMouseOver)
                                    {
                                        this.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushButtonHover");
                                    }
                                    else
                                    {
                                        this.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushButton");
                                    }

                                    break;
                                }
                            case ColorState.Highlight:
                                {
                                    if (this.IsMouseOver)
                                    {
                                        this.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushButtonHover");
                                    }
                                    else
                                    {
                                        this.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush2");
                                    }

                                    break;
                                }
                            case ColorState.Red:
                                {
                                    if (this.IsMouseOver)
                                    {
                                        this.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedLight");
                                    }
                                    else
                                    {
                                        this.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedDark");
                                    }

                                    break;
                                }
                        }
                    }
                    else
                    {
                        this.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushButtonDisabled");
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新按钮颜色出错");
            }
        }

        // 鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
        private bool IsMouseDown = false;
        private void Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsMouseDown)
                return;
            Logger.Info($"按下按钮：{Text}");
            Click?.Invoke(sender, e);
            this.RaiseCustomEvent(); // 自定义事件
            RefreshColor(); // 松手后复位边框色（悬停/普通）
        }
        private void Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            IsMouseDown = true;
            this.Focus();
            // 按下（按钮按下色）
            ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.PanFore, Border.BorderBrushProperty, "ColorBrushButtonPressed", 60) }, "MyButton Color " + Uuid);
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanFore, 0.955d - ((ScaleTransform)this.PanFore.RenderTransform).ScaleX, 80, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.ExtraStrong)), ModAnimation.AaScaleTransform(this.PanFore, -0.01d, 700, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Middle)) }, "MyButton Scale " + Uuid);
        }
        private void Button_MouseEnter()
        {
            ModAnimation.AniStart(ModAnimation.AaColor(this.PanFore, Border.BackgroundProperty, _ColorType == ColorState.Red ? "ColorBrushRedBack" : "ColorBrush7", AnimationColorIn), "MyButton Background " + Uuid);
        }
        private void Button_MouseUp()
        {
            if (!IsMouseDown)
                return;
            IsMouseDown = false;
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.PanFore, 1d - ((ScaleTransform)this.PanFore.RenderTransform).ScaleX, 300, 10, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Middle)) }, "MyButton Scale " + Uuid);
        }
        private void Button_MouseLeave()
        {
            ModAnimation.AniStart(ModAnimation.AaColor(this.PanFore, Border.BackgroundProperty, "ColorBrushHalfWhite", AnimationColorOut), "MyButton Background " + Uuid);
            RefreshColor(); // 离开后复位边框色（普通）
            if (!IsMouseDown)
                return;
            IsMouseDown = false;
            ModAnimation.AniStart(ModAnimation.AaScaleTransform(this.PanFore, 1d - ((ScaleTransform)this.PanFore.RenderTransform).ScaleX, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), "MyButton Scale " + Uuid);
        }

    }
}