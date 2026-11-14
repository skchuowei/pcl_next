using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    [ContentProperty("Inlines")]
    public partial class MyListItem : IMyRadio
    {

        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e);
        public event LogoClickEventHandler LogoClick;

        public delegate void LogoClickEventHandler(object sender, MouseButtonEventArgs e);
        public event IMyRadio.CheckEventHandler Check;
        public event IMyRadio.ChangedEventHandler Changed;

        #region 后加载控件

        // 指向背景
        private Border _RectBack = null;
        public Border RectBack
        {
            get
            {
                if (_RectBack is null)
                {
                    var Rect = new Border()
                    {
                        Name = "RectBack",
                        CornerRadius = new CornerRadius(IsScaleAnimationEnabled || this.Height > 40d ? 6 : 0),
                        RenderTransform = IsScaleAnimationEnabled ? new ScaleTransform(0.8d, 0.8d) : null,
                        RenderTransformOrigin = new Point(0.5d, 0.5d),
                        BorderThickness = new Thickness(ModBase.GetWPFSize(1d)),
                        SnapsToDevicePixels = true,
                        IsHitTestVisible = false,
                        Opacity = 0d
                    };
                    Rect.SetResourceReference(Border.BackgroundProperty, "ColorBrush7");
                    Rect.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6");
                    Grid.SetColumnSpan(Rect, 999);
                    Grid.SetRowSpan(Rect, 999);
                    this.Children.Insert(0, Rect);
                    _RectBack = Rect;
                    // <!--<Border x:Name = "RectBack" CornerRadius="3" RenderTransformOrigin="0.5,0.5" SnapsToDevicePixels="True" 
                    // IsHitTestVisible = "False" Opacity="0" BorderThickness="1" 
                    // Grid.ColumnSpan = "4" Background="{DynamicResource ColorBrush7}" BorderBrush="{DynamicResource ColorBrush6}"/>-->
                }
                return _RectBack;
            }
        }

        // 按钮
        public FrameworkElement ButtonStack;

        // 图标
        public FrameworkElement PathLogo;

        // 勾选条
        public Border RectCheck;

        // 副文本
        private TextBlock _LabInfo = null;
        public TextBlock LabInfo
        {
            get
            {
                if (_LabInfo is null)
                {
                    var Lab = new TextBlock()
                    {
                        Name = "LabInfo",
                        SnapsToDevicePixels = false,
                        UseLayoutRounding = false,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsHitTestVisible = false,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Visibility = Visibility.Collapsed,
                        FontSize = 12d,
                        Margin = new Thickness(4d, 0d, 0d, 0d),
                        Opacity = 0.6d
                    };
                    Grid.SetColumn(Lab, 3);
                    Grid.SetRow(Lab, 2);
                    this.Children.Add(Lab);
                    _LabInfo = Lab;
                    // <TextBlock Grid.Row="2" SnapsToDevicePixels="False" UseLayoutRounding="False" HorizontalAlignment="Left" x:Name = "LabInfo" IsHitTestVisible="False" Grid.Column="2" 
                    // TextTrimming = "CharacterEllipsis" Visibility="Collapsed" FontSize="12" Foreground="{StaticResource ColorBrushGray2}" Margin="4,0,0,0" />
                }
                return _LabInfo;
            }
        }

        #endregion

        #region 自定义属性

        // Uuid
        public int Uuid = ModBase.GetUuid();

        /// <summary>
    /// 是否启用缩放动画。
    /// </summary>
        public bool IsScaleAnimationEnabled
        {
            get
            {
                return _IsScaleAnimationEnabled;
            }
            set
            {
                _IsScaleAnimationEnabled = value;
                if (_RectBack is not null)
                    RectBack.CornerRadius = new CornerRadius(value ? 6 : 0);
            }
        }
        private bool _IsScaleAnimationEnabled = true;

        // 边距
        public int PaddingLeft
        {
            get
            {
                return (int)Math.Round(this.ColumnPaddingLeft.Width.Value);
            }
            set
            {
                this.ColumnPaddingLeft.Width = new GridLength(value);
            }
        }
        /// <summary>
    /// 右边距的最小值。
    /// 在存在右侧按钮时，右边距会被自动设置为 5 + 按钮数 * 25。
    /// </summary>
        public int MinPaddingRight { get; set; } = 4;

        // 按钮
        private IEnumerable<MyIconButton> _Buttons;
        public IEnumerable<MyIconButton> Buttons
        {
            get
            {
                return _Buttons;
            }
            set
            {
                _Buttons = value;
                // 没有特殊按钮，移除原 Stack
                if (ButtonStack is not null)
                {
                    this.Children.Remove(ButtonStack);
                    ButtonStack = null;
                }
                // 添加新 Stack
                switch (value.Count())
                {
                    case 0:
                        {
                            break;
                        }
                    // 没有按钮，不添加新的
                    case 1:
                        {
                            // 只有一个按钮
                            foreach (MyIconButton Btn in value)
                            {
                                if (Btn.Height.Equals(double.NaN))
                                    Btn.Height = 25d;
                                if (Btn.Width.Equals(double.NaN))
                                    Btn.Width = 25d;
                                Btn.Opacity = 0d;
                                Btn.Margin = new Thickness(0d, 0d, 5d, 0d);
                                Btn.SnapsToDevicePixels = false;
                                Btn.HorizontalAlignment = HorizontalAlignment.Right;
                                Btn.VerticalAlignment = VerticalAlignment.Center;
                                Btn.SnapsToDevicePixels = false;
                                Btn.UseLayoutRounding = false;
                                Grid.SetColumnSpan(Btn, 10);
                                Grid.SetRowSpan(Btn, 10);
                                this.Children.Add(Btn);
                                ButtonStack = Btn;
                            }

                            break;
                        }

                    default:
                        {
                            // 有复数按钮，使用 StackPanel
                            ButtonStack = new StackPanel() { Opacity = 0d, Margin = new Thickness(0d, 0d, 5d, 0d), SnapsToDevicePixels = false, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, UseLayoutRounding = false };
                            Grid.SetColumnSpan(ButtonStack, 10);
                            Grid.SetRowSpan(ButtonStack, 10);
                            // 构造按钮
                            foreach (MyIconButton Btn in value)
                            {
                                if (Btn.Height.Equals(double.NaN))
                                    Btn.Height = 25d;
                                if (Btn.Width.Equals(double.NaN))
                                    Btn.Width = 25d;
                                ((StackPanel)ButtonStack).Children.Add(Btn);
                            }
                            this.Children.Add(ButtonStack);
                            break;
                        }
                }
            }
        }

        // 标题
        public InlineCollection Inlines
        {
            get
            {
                return this.LabTitle.Inlines;
            }
        }
        public string Title
        {
            get
            {
                return Conversions.ToString(this.GetValue(TitleProperty));
            }
            set
            {
                this.SetValue(TitleProperty, value.ReplaceLineEndings(""));
            }
        }
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(MyListItem));

        // 字号
        public double FontSize
        {
            get
            {
                return Conversions.ToDouble(this.GetValue(FontSizeProperty));
            }
            set
            {
                this.SetValue(FontSizeProperty, value);
            }
        }
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register("FontSize", typeof(double), typeof(MyListItem), new PropertyMetadata(14d));

        // 信息
        private string _Info = "";
        public string Info
        {
            get
            {
                return _Info;
            }
            set
            {
                if ((_Info ?? "") == (value ?? ""))
                    return;
                value = value.ReplaceLineEndings("");
                _Info = value;
                LabInfo.Text = value;
                LabInfo.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // 图片
        private string _Logo = "";
        public string Logo
        {
            get
            {
                return _Logo;
            }
            set
            {
                if ((_Logo ?? "") == (value ?? ""))
                    return;
                _Logo = value;
                // 删除旧 Logo
                if (!(PathLogo == null))
                    this.Children.Remove(PathLogo);
                // 添加新 Logo
                if (!string.IsNullOrEmpty(_Logo))
                {
                    if (_Logo.StartsWithF("http", true))
                    {
                        // 网络图片
                        PathLogo = new MyImage()
                        {
                            Tag = this,
                            IsHitTestVisible = LogoClickable,
                            Source = _Logo,
                            RenderTransformOrigin = new Point(0.5d, 0.5d),
                            RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale },
                            SnapsToDevicePixels = true,
                            UseLayoutRounding = false
                        };
                        RenderOptions.SetBitmapScalingMode(PathLogo, BitmapScalingMode.Linear);
                    }
                    else if (_Logo.EndsWithF(".png", true) || _Logo.EndsWithF(".jpg", true) || _Logo.EndsWithF(".webp", true))
                    {
                        // 位图
                        PathLogo = new Canvas()
                        {
                            Tag = this,
                            IsHitTestVisible = LogoClickable,
                            Background = new MyBitmap(_Logo),
                            RenderTransformOrigin = new Point(0.5d, 0.5d),
                            RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale },
                            SnapsToDevicePixels = true,
                            UseLayoutRounding = false,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };
                        RenderOptions.SetBitmapScalingMode(PathLogo, BitmapScalingMode.Linear);
                    }
                    else
                    {
                        // 矢量图
                        PathLogo = new System.Windows.Shapes.Path()
                        {
                            Tag = this,
                            IsHitTestVisible = LogoClickable,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Stretch = Stretch.Uniform,
                            Data = (Geometry)new GeometryConverter().ConvertFromString(_Logo),
                            RenderTransformOrigin = new Point(0.5d, 0.5d),
                            RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale },
                            SnapsToDevicePixels = false,
                            UseLayoutRounding = false
                        };
                        PathLogo.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding("Foreground") { Source = this });
                    }
                    Grid.SetColumn(PathLogo, 2);
                    Grid.SetRowSpan(PathLogo, 4);
                    OnSizeChanged(); // 设置边距
                    this.Children.Add(PathLogo);
                    // 图标的点击事件
                    if (LogoClickable)
                    {
                        PathLogo.MouseLeave += (sender, e) => IsLogoDown = false;
                        PathLogo.MouseLeftButtonDown += (sender, e) => IsLogoDown = true;
                        PathLogo.MouseLeftButtonUp += (sender, e) => { if (IsLogoDown) { IsLogoDown = false; LogoClick?.Invoke(((dynamic)sender).Tag, e); } };
                    }
                }
                // 改变行距
                this.ColumnLogo.Width = new GridLength((double)((string.IsNullOrEmpty(_Logo) ? 0 : 34) + (this.Height < 40d ? 0 : 4)));
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
                if (!(PathLogo == null))
                    PathLogo.RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale };
            }
        }

        // 图标的点击
        /// <summary>
    /// 该 Logo 是否可用点击触发事件。需要在 Logo 属性之前设置。
    /// </summary>
        public bool LogoClickable { get; set; } = false;
        private bool IsLogoDown = false;

        // 勾选选项
        public enum CheckType
        {
            None,
            Clickable,
            RadioBox,
            CheckBox
        }
        private CheckType _Type = CheckType.None;
        public CheckType Type
        {
            get
            {
                return _Type;
            }
            set
            {
                if (_Type == value)
                    return;
                _Type = value;
                // 切换左栏大小
                this.ColumnCheck.Width = new GridLength(_Type == CheckType.None || _Type == CheckType.Clickable ? this.Height < 40d ? 4 : 2 : 6);
                // 切换竖条控件
                if (_Type == CheckType.None || _Type == CheckType.Clickable)
                {
                    // 移除竖条控件
                    if (!(RectCheck == null))
                    {
                        this.Children.Remove(RectCheck);
                        RectCheck = null;
                    }
                    SetChecked(false, false, false);
                }
                // 添加竖条控件
                else if (RectCheck == null)
                {
                    RectCheck = new Border()
                    {
                        Width = 5d,
                        Height = Checked ? double.NaN : 0d,
                        CornerRadius = new CornerRadius(2d, 2d, 2d, 2d),
                        VerticalAlignment = Checked ? VerticalAlignment.Stretch : VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        UseLayoutRounding = false,
                        SnapsToDevicePixels = false,
                        Margin = Checked ? new Thickness(-1, 6d, 0d, 6d) : new Thickness(-1, 0d, 0d, 0d)
                    };
                    RectCheck.SetResourceReference(Border.BackgroundProperty, "ColorBrush3");
                    Grid.SetRowSpan(RectCheck, 4);
                    this.Children.Add(RectCheck);
                }
            }
        }

        public MyListItem()
        {
            InitializeComponent();
            this.SizeChanged += (_, __) => OnSizeChanged();
            this.PreviewMouseLeftButtonUp += Button_MouseUp;
            this.PreviewMouseLeftButtonDown += Button_MouseDown;
            this.MouseLeave += Button_MouseLeave;
            this.PreviewMouseLeftButtonUp += Button_MouseLeave;
            this.MouseEnter += RefreshColor;
            this.MouseLeave += RefreshColor;
            this.PreviewMouseLeftButtonDown += RefreshColor;
            this.PreviewMouseLeftButtonUp += RefreshColor;
            this.Loaded += MyListItem_Loaded;
        }

        // 适应尺寸
        private void OnSizeChanged()
        {
            this.ColumnCheck.Width = new GridLength(_Type == CheckType.None || _Type == CheckType.Clickable ? this.Height < 40d ? 4 : 2 : 6);
            this.ColumnLogo.Width = new GridLength((double)((string.IsNullOrEmpty(_Logo) ? 0 : 34) + (this.Height < 40d ? 0 : 4)));
            if (PathLogo is not null)
            {
                if (_Logo.EndsWithF(".png", true) || _Logo.EndsWithF(".jpg", true) || _Logo.EndsWithF(".webp", true))
                {
                    PathLogo.Margin = new Thickness(4d, 5d, 3d, 5d);
                }
                else
                {
                    PathLogo.Margin = new Thickness(this.Height < 40d ? 6 : 8, 8d, this.Height < 40d ? 4 : 6, 8d);
                }
            }
            this.LabTitle.Margin = new Thickness(4d, 0d, 0d, this.Height < 40d ? 0 : 2);
        }

        // 勾选状态
        private bool _Checked = false;
        public bool Checked
        {
            get
            {
                return _Checked;
            }
            set
            {
                SetChecked(value, false, value != _Checked); // 仅在值发生变化时触发动画 (#4596)
            }
        }
        /// <summary>
    /// 手动设置 Checked 属性。
    /// </summary>
    /// <param name="value">新的 Checked 属性。</param>
    /// <param name="user">是否由用户引发。</param>
    /// <param name="anime">是否执行动画。</param>
        public void SetChecked(bool value, bool user, bool anime)
        {
            try
            {

                // 自定义属性基础

                var ChangedEventArgs = new ModBase.RouteEventArgs(user);
                bool RawValue = _Checked;
                if (Type == CheckType.RadioBox)
                {
                    if (this.IsInitialized && !(value == _Checked))
                    {
                        _Checked = value;
                        Changed?.Invoke(this, ChangedEventArgs);
                        if (ChangedEventArgs.Handled)
                        {
                            _Checked = RawValue;
                            return;
                        }
                    }
                    _Checked = value;
                }
                else
                {
                    if (value == _Checked)
                        return;
                    _Checked = value;
                    if (this.IsInitialized)
                    {
                        Changed?.Invoke(this, ChangedEventArgs);
                        if (ChangedEventArgs.Handled)
                        {
                            _Checked = RawValue;
                            return;
                        }
                    }
                }
                if (value)
                {
                    var CheckEventArgs = new ModBase.RouteEventArgs(user);
                    Check?.Invoke(this, CheckEventArgs);
                    if (CheckEventArgs.Handled)
                        return;
                }

                // 保证只有一个单选 ListItem 选中

                if (Type == CheckType.RadioBox)
                {
                    if (this.Parent == null)
                        return;
                    var RadioboxList = new List<MyListItem>();
                    int CheckedCount = 0;
                    // 收集控件列表与选中个数
                    // 原文为 For Each Control In CType(Parent, Object).Children（VB 后期绑定取 Parent.Children），
                    // codeconv 误把 Parent 本身强转成 IEnumerable；此处保留后期绑定，只把结果当集合遍历
                    foreach (var Control_Iter in (IEnumerable)((dynamic)this.Parent).Children)
                    {
                        var Control = Control_Iter;
                        Control = MyVirtualizingElement.TryInit((FrameworkElement)Control);
                        if (Control is MyListItem && ((MyListItem)Control).Type == CheckType.RadioBox)
                        {
                            RadioboxList.Add((MyListItem)Control);
                            if (Conversions.ToBoolean(((dynamic)Control).Checked))
                                CheckedCount += 1;
                        }
                    }
                    // 判断选中情况
                    switch (CheckedCount)
                    {
                        case 0:
                            {
                                // 没有任何单选框被选中，选择第一个
                                RadioboxList[0].Checked = true;
                                break;
                            }
                        case var @case when @case > 1:
                            {
                                // 选中项目多于 1 个
                                if (Checked)
                                {
                                    // 如果本控件选中，则取消其他所有控件的选中
                                    foreach (MyListItem Control in RadioboxList)
                                    {
                                        if (Control.Checked && !Control.Equals(this))
                                            Control.Checked = false;
                                    }
                                }
                                else
                                {
                                    // 如果本控件未选中，则只保留第一个选中的控件
                                    bool FirstChecked = false;
                                    foreach (MyListItem Control in RadioboxList)
                                    {
                                        if (Control.Checked)
                                        {
                                            if (FirstChecked)
                                            {
                                                Control.Checked = false; // 修改 Checked 会自动触发 Change 事件，所以不用额外触发
                                            }
                                            else
                                            {
                                                FirstChecked = true;
                                            }
                                        }
                                    }
                                }

                                break;
                            }
                    }
                }

                // 更改动画

                if (this.IsLoaded && ModAnimation.AniControlEnabled == 0 && anime) // 防止默认属性变更触发动画
                {
                    var Anim = new List<ModAnimation.AniData>();
                    if (Checked)
                    {
                        // 由无变有
                        if (!(RectCheck == null))
                        {
                            double Delta = this.ActualHeight - RectCheck.ActualHeight - 12d;
                            Anim.Add(ModAnimation.AaHeight(RectCheck, Delta * 0.4d, 200, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
                            Anim.Add(ModAnimation.AaHeight(RectCheck, Delta * 0.6d, 300, Ease: new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)));
                            Anim.Add(ModAnimation.AaOpacity(RectCheck, 1d - RectCheck.Opacity, 30));
                            RectCheck.VerticalAlignment = VerticalAlignment.Center;
                            RectCheck.Margin = new Thickness(-1, 0d, 0d, 0d);
                        }
                        Anim.Add(ModAnimation.AaColor(this, ForegroundProperty, this.Height < 40d ? "ColorBrush3" : "ColorBrush2", 200));
                    }
                    else
                    {
                        // 由有变无
                        if (!(RectCheck == null))
                        {
                            // Anim.Add(AaWidth(RectCheck, -RectCheck.Width, 120,, New AniEaseInFluent))
                            Anim.Add(ModAnimation.AaHeight(RectCheck, -RectCheck.ActualHeight, 120, Ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)));
                            Anim.Add(ModAnimation.AaOpacity(RectCheck, -RectCheck.Opacity, 70, 40));
                            RectCheck.VerticalAlignment = VerticalAlignment.Center;
                        }
                        Anim.Add(ModAnimation.AaColor(this, ForegroundProperty, "ColorBrush1", 120));
                    }
                    ModAnimation.AniStart(Anim, "MyListItem Checked " + Uuid);
                }
                else
                {
                    // 不使用动画
                    ModAnimation.AniStop("MyListItem Checked " + Uuid);
                    if (Checked)
                    {
                        if (!(RectCheck == null))
                        {
                            RectCheck.Height = double.NaN;
                            RectCheck.Margin = new Thickness(-1, 6d, 0d, 6d);
                            RectCheck.Opacity = 1d;
                            RectCheck.VerticalAlignment = VerticalAlignment.Stretch;
                        }
                        this.SetResourceReference(ForegroundProperty, this.Height < 40d ? "ColorBrush3" : "ColorBrush2");
                    }
                    else
                    {
                        if (!(RectCheck == null))
                        {
                            RectCheck.Height = 0d;
                            RectCheck.Margin = new Thickness(-1, 0d, 0d, 0d);
                            RectCheck.Opacity = 0d;
                            RectCheck.VerticalAlignment = VerticalAlignment.Center;
                        }
                        this.SetResourceReference(ForegroundProperty, "ColorBrush1");
                    }
                }
            }

            catch (Exception ex)
            {
                Logger.Warn(ex, "设置 Checked 失败");
            }
        }

        // 前景色绑定
        public Brush Foreground
        {
            get
            {
                return (Brush)this.GetValue(ForegroundProperty);
            }
            set
            {
                this.SetValue(ForegroundProperty, value);
            }
        }
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(MyListItem), new PropertyMetadata((SolidColorBrush)ModSecret.Color1));

        // 菜单与按钮绑定
        public Action<MyListItem, EventArgs> ContentHandler;

        #endregion

        #region 点击

        // 触发点击事件
        private void Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsMouseDown)
                return;
            Click?.Invoke(sender, e);
            if (e.Handled)
                return;
            // 触发自定义事件
            if (CustomEventService.GetEventType((DependencyObject)sender) != CustomEvent.EventType.None || CustomEventService.GetEvents((DependencyObject)sender).Events.Count > 0)
            {
                this.RaiseCustomEvent();
                e.Handled = true;
            }
            if (e.Handled)
                return;
            // 实际的单击处理
            switch (Type)
            {
                case CheckType.Clickable:
                    {
                        Logger.Info($"按下单击列表项：{Title}");
                        break;
                    }
                case CheckType.RadioBox:
                    {
                        Logger.Info($"按下单选列表项：{Title}");
                        if (!Checked)
                            SetChecked(true, true, true);
                        break;
                    }
                case CheckType.CheckBox:
                    {
                        Logger.Info($"按下复选列表项（{!Checked}）：{Title}");
                        SetChecked(!Checked, true, true);
                        break;
                    }
            }
        }

        // 鼠标点击判定
        private bool IsMouseDown = false;
        private void Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!this.IsMouseDirectlyOver || Type == CheckType.None)
                return;
            // 右侧图标按钮所在区域不能当成整行点击：以前这里会把 ButtonStack 直接
            // 设成 IsHitTestVisible = false，导致图标永远收不到 MouseUp，编辑/复制/删除
            // 全部退化成「点击账号行」。现在只按横向位置区分，图标区域交给按钮自己处理。
            if (ButtonStack is not null && Buttons is not null)
            {
                double ButtonAreaWidth = Buttons.Count() * 25d + 10d;
                if (e.GetPosition(this).X >= this.ActualWidth - ButtonAreaWidth)
                    return;
            }
            IsMouseDown = true;
        }
        private void Button_MouseLeave(object sender, object e)
        {
            IsMouseDown = false;
            if (ButtonStack is not null)
                ButtonStack.IsHitTestVisible = true;
        }

        #endregion

        private string StateLast;
        public bool IsMouseOverAnimationEnabled = true;
        public void RefreshColor(object sender, EventArgs e)
        {
            // 菜单虚拟化检测
            if (ContentHandler is not null)
            {
                ContentHandler(this, e);
                ContentHandler = null;
            }
            // 判断当前颜色
            string StateNew;
            int Time;
            if (IsMouseDown && !(Type == CheckType.RadioBox && Checked))
            {
                StateNew = "MouseDown";
                Time = 120;
            }
            else if (this.IsMouseOver && IsMouseOverAnimationEnabled)
            {
                StateNew = "MouseOver";
                Time = 120;
            }
            else
            {
                StateNew = "Idle";
                Time = 180;
            }
            if ((StateLast ?? "") == (StateNew ?? ""))
                return;
            StateLast = StateNew;
            // 触发颜色动画
            if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
            {
                // 有动画
                var Ani = new List<ModAnimation.AniData>();
                if (this.IsMouseOver && IsMouseOverAnimationEnabled)
                {
                    if (ButtonStack is not null)
                    {
                        Ani.Add(ModAnimation.AaOpacity(ButtonStack, 1d - ButtonStack.Opacity, (int)Math.Round(Time * 0.7d), (int)Math.Round(Time * 0.3d)));
                        Ani.Add(ModAnimation.AaDouble(i => this.ColumnPaddingRight.Width = new GridLength(Conversions.ToDouble(Math.Max(0, (dynamic)(Operators.AddObject(this.ColumnPaddingRight.Width.Value, i))))), (double)Math.Max(MinPaddingRight, 5 + Buttons.Count() * 25) - this.ColumnPaddingRight.Width.Value, (int)Math.Round(Time * 0.3d), (int)Math.Round(Time * 0.7d)));
                    }
                    Ani.AddRange(new[] { ModAnimation.AaColor(RectBack, Border.BackgroundProperty, IsMouseDown ? "ColorBrush6" : "ColorBrushBg1", Time), ModAnimation.AaOpacity(RectBack, 1d - RectBack.Opacity, Time, Ease: new ModAnimation.AniEaseOutFluent()) });
                    if (IsScaleAnimationEnabled)
                    {
                        Ani.Add(ModAnimation.AaScaleTransform(RectBack, 1d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, (int)Math.Round(Time * 1.6d), Ease: new ModAnimation.AniEaseOutFluent()));
                        if (IsMouseDown)
                        {
                            Ani.Add(ModAnimation.AaScaleTransform(this, 0.98d - ((ScaleTransform)this.RenderTransform).ScaleX, (int)Math.Round(Time * 0.9d), Ease: new ModAnimation.AniEaseOutFluent()));
                        }
                        else
                        {
                            Ani.Add(ModAnimation.AaScaleTransform(this, 1d - ((ScaleTransform)this.RenderTransform).ScaleX, (int)Math.Round(Time * 1.2d), Ease: new ModAnimation.AniEaseOutFluent()));
                        }
                    }
                }
                else
                {
                    if (ButtonStack is not null)
                    {
                        Ani.Add(ModAnimation.AaOpacity(ButtonStack, -ButtonStack.Opacity, (int)Math.Round(Time * 0.4d)));
                        Ani.Add(ModAnimation.AaDouble(i => this.ColumnPaddingRight.Width = new GridLength(Conversions.ToDouble(Math.Max(0, (dynamic)(Operators.AddObject(this.ColumnPaddingRight.Width.Value, i))))), (double)MinPaddingRight - this.ColumnPaddingRight.Width.Value, (int)Math.Round(Time * 0.4d)));
                    }
                    Ani.Add(ModAnimation.AaOpacity(RectBack, -RectBack.Opacity, Time));
                    if (IsScaleAnimationEnabled)
                    {
                        Ani.AddRange(new[] { ModAnimation.AaColor(RectBack, Border.BackgroundProperty, IsMouseDown ? "ColorBrush6" : "ColorBrush7", Time), ModAnimation.AaScaleTransform(this, 1d - ((ScaleTransform)this.RenderTransform).ScaleX, Time * 3, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaScaleTransform(RectBack, 0.996d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, Time, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaScaleTransform(RectBack, -0.246d, 1, After: true) });
                    }
                }
                ModAnimation.AniStart(Ani, "ListItem Color " + Uuid);
            }
            else
            {
                // 无动画
                if (this.IsMouseOver && IsMouseOverAnimationEnabled)
                {
                    if (ButtonStack is not null)
                    {
                        ButtonStack.Opacity = 1d;
                        this.ColumnPaddingRight.Width = new GridLength(Math.Max(MinPaddingRight, 5 + Buttons.Count() * 25));
                    }
                    // 由于鼠标已经移入，所以直接实例化 RectBack
                    RectBack.Background = ModSecret.ColorBg1;
                    RectBack.Opacity = 1d;
                    RectBack.RenderTransform = new ScaleTransform(1d, 1d);
                    this.RenderTransform = new ScaleTransform(1d, 1d);
                }
                else
                {
                    if (ButtonStack is not null)
                    {
                        ButtonStack.Opacity = 0d;
                        this.ColumnPaddingRight.Width = new GridLength(MinPaddingRight);
                    }
                    this.RenderTransform = new ScaleTransform(1d, 1d);
                    if (_RectBack is not null)
                    {
                        if (IsScaleAnimationEnabled)
                            RectBack.RenderTransform = new ScaleTransform(0.75d, 0.75d);
                        RectBack.Background = ModSecret.Color7;
                        RectBack.Opacity = 0d;
                    }
                }
                ModAnimation.AniStop("ListItem Color " + Uuid);
            }
        }

        private void MyListItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (Checked)
            {
                this.SetResourceReference(ForegroundProperty, this.Height < 40d ? "ColorBrush3" : "ColorBrush2");
            }
            else
            {
                this.SetResourceReference(ForegroundProperty, "ColorBrush1");
            }
            this.ColumnPaddingRight.Width = new GridLength(MinPaddingRight);
            if (CustomEventService.GetEventType(this) == CustomEvent.EventType.打开帮助 && !(!string.IsNullOrEmpty(Title) && !string.IsNullOrEmpty(Info))) // #3266
            {
                try
                {
                    var Entry = new ModMain.HelpEntry(CustomEvent.GetAbsoluteUrls(CustomEventService.GetEventData(this), CustomEventService.GetEventType(this))[0]);
                    Entry.SetToListItem(this);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "设置帮助 MyListItem 失败", LogBehavior.Alert);
                    CustomEventService.SetEventType(this, CustomEvent.EventType.None);
                    CustomEventService.SetEventData(this, "");
                }
            }
        }
        public override string ToString()
        {
            return Title;
        }

    }
}