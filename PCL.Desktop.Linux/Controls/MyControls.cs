using System.Collections.Specialized;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Shapes = Avalonia.Controls.Shapes;

namespace PCL.Desktop.Linux.Controls;

/// <summary>
/// 卡片容器，1:1 对齐 `pcl-next/Controls/MyCard.vb`：
/// 外圈阴影（Margin -3、半径 3、静止不透明度 0.07 / 悬停 0.4）、白色卡片底 #F5FFFFFF、圆角 5、
/// 标题字号 13 加粗、位置 (15,12)、颜色 ColorBrush1 → 悬停 ColorBrush2。
/// 折叠（CanSwap / IsSwapped）与高度动画属交互层，暂未接入（登记项 R5）。
/// </summary>
public class MyCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<MyCard, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}

/// <summary>
/// 按钮，1:1 对齐 `pcl-next/Controls/MyButton.xaml(.vb)`：
/// 外层 #01eaf2fe → 内层 Border 圆角 3 / 1px 边框 / 背景 #55FFFFFF。
/// 配色：Normal=ColorBrush1、Highlight=ColorBrush2、Red=ColorBrushRedDark；
/// 悬停统一 ColorBrush3（Red 用 RedLight），背景切 ColorBrush7（Red 用 RedBack），
/// 变色 100ms 进 / 200ms 出；按下整体缩放 0.955（80ms 进，300ms 回弹）。
/// </summary>
public class MyButton : Button
{
    public enum MyButtonColor
    {
        Normal,
        Highlight,
        Red
    }

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MyButton, string?>(nameof(Text));

    public static readonly StyledProperty<MyButtonColor> ColorTypeProperty =
        AvaloniaProperty.Register<MyButton, MyButtonColor>(nameof(ColorType), MyButtonColor.Normal);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MyButtonColor ColorType
    {
        get => GetValue(ColorTypeProperty);
        set => SetValue(ColorTypeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColorTypeProperty)
        {
            Classes.Set("highlight", ColorType == MyButtonColor.Highlight);
            Classes.Set("red", ColorType == MyButtonColor.Red);
        }
    }
}

/// <summary>
/// 输入框，对齐 `pcl-next/Application.xaml` 的 MyTextBox 样式：
/// 1px 边框 ColorBrushBg0、背景 #55FFFFFF、圆角 3、字号 13、文字颜色 ColorBrush1。
/// </summary>
public class MyTextBox : TextBox
{
}

/// <summary>
/// 下拉框，1:1 对齐 `pcl-next/Controls/MyComboBox.vb`：
/// Windows 端**没有自定义模板**（纯继承 WPF ComboBox 默认模板 + 属性/动画改造），
/// 因此这里同样复用 Avalonia Fluent 的 ComboBox 模板，只覆写 <see cref="StyleKeyOverride"/> 让
/// Fluent 的 ControlTheme 能按基类 <c>ComboBox</c> 命中——否则派生类型查不到主题、无 Template、
/// 控件完全不渲染（R13 根因）。
/// 配色照抄 `RefreshColor`：常态 前景 ColorBrushBg0 / 背景 ColorBrushHalfWhite、
/// 悬停 前景 ColorBrush4 / 背景 ColorBrush7、按下或展开 前景 ColorBrush3 / 背景 ColorBrush7、
/// 禁用 前景 ColorBrushGray5 / 背景 ColorBrushGray6；边框固定 ColorBrushBg0、圆角 3、字号 13、下拉项高 26。
/// WPF 侧「可编辑态文本框聚焦」分支在 Linux 只对可编辑 ComboBox 有意义，本轮页面未用到，登记于 R6。
/// 动画时序（10ms / 100ms / 200ms）用 Transitions 近似，登记于 R10。
/// </summary>
public class MyComboBox : ComboBox
{
    /// <summary>Fluent 的 ComboBox 主题按基类注册；派生类必须回到基类键，否则无模板（R13）。</summary>
    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsDropDownOpenProperty) SyncExpanded();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SyncExpanded();
    }

    /// <summary>展开态与按下态同色（Windows 端 RefreshColor 把 IsDropDownOpen 与按下并为同一分支）。</summary>
    private void SyncExpanded() => PseudoClasses.Set(":expanded", IsDropDownOpen);

    // 按下态：照抄 Windows 端的 PreviewMouseLeftButtonDown / Up + MouseLeave 三个钩子，
    // 自己维护 :pressed，避免依赖 Fluent 模板是否给 ComboBox 设了同名伪类。
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        PseudoClasses.Set(":pressed", true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        PseudoClasses.Set(":pressed", false);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PseudoClasses.Set(":pressed", false);
    }
}

/// <summary>
/// 下拉项，1:1 对齐 `pcl-next/Controls/MyComboBoxItem.vb` 的视觉（无额外装饰，供 ComboBox 使用）。
/// 同样需要 <see cref="StyleKeyOverride"/> 才能命中 Fluent 的 ComboBoxItem 主题（R13）。
/// </summary>
public class MyComboBoxItem : ComboBoxItem
{
    protected override Type StyleKeyOverride => typeof(ComboBoxItem);
}

/// <summary>
/// 复选框，1:1 对齐 `pcl-next/Controls/MyCheckBox.xaml(.vb)`：
/// 边框 18×18 / 圆角 3 / 边框 1.1 / 背景 #55FFFFFF / 左边距 1；勾选路径 12×12（中心缩放 0 → 1）；
/// 文字字号 13、左边距 26、颜色 ColorBrush1。
/// 配色：勾选=ColorBrush2、未勾选=ColorBrush1、悬停=ColorBrush3、禁用=ColorBrushGray5；
/// 勾选动画 150ms，按下背景 ColorBrushBg1（100ms），按压缩放至 16.5/18。
/// WPF 自研 Ani 引擎的缓动曲线无法逐帧复刻，本轮用 Avalonia Transitions 近似（登记项 R10）。
/// </summary>
public class MyCheckBox : TemplatedControl
{
    public static readonly StyledProperty<bool> CheckedProperty =
        AvaloniaProperty.Register<MyCheckBox, bool>(nameof(Checked));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MyCheckBox, string?>(nameof(Text));

    public bool Checked
    {
        get => GetValue(CheckedProperty);
        set => SetValue(CheckedProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>勾选状态改变；user 表示是否由用户手动触发（对应 WPF 的 Change(sender, user)）。</summary>
    public event EventHandler<bool>? Changed;

    private bool _isPointerDown;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CheckedProperty) SyncState();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SyncState();
    }

    private void SyncState() => PseudoClasses.Set(":checked", Checked);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _isPointerDown = true;
        PseudoClasses.Set(":pressed", true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        PseudoClasses.Set(":pressed", false);
        if (!_isPointerDown) return;
        _isPointerDown = false;
        if (!IsEnabled || e.InitialPressMouseButton != MouseButton.Left) return;
        SetChecked(!Checked, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isPointerDown = false;
        PseudoClasses.Set(":pressed", false);
    }

    public void SetChecked(bool value, bool user)
    {
        if (value == Checked) return;
        Checked = value;
        Changed?.Invoke(this, user);
    }
}

/// <summary>
/// 提示条，1:1 对齐 `pcl-next/Controls/MyHint.xaml(.vb)`：
/// 左边框 3px、圆角 2；HasBorder=true 时补上 1px 的上/右/下边框。
/// 三套主题：Blue #d9ecff/#1172D4/#0F64B8、Red #ffdddf/#d82929/#bf0b0b、
/// Yellow #ffebd7/#f57a00/#d86c00；文字行高 16、内边距 (12,9)、右边距 -1、自动换行。
/// 关闭按钮为 20×20 的 MyIconButton（Logo 为 F1 路径、ToolTip「不再提示」）。
/// WPF 版关闭时写入 Settings 记忆并在 200ms/150ms 内缩放+淡出+收高，Linux 侧 Settings 尚未接入，
/// 本轮关闭只做淡出隐藏，记忆与收高动画登记为差异（R11）。
/// </summary>
public class MyHint : TemplatedControl
{
    public enum MyHintTheme
    {
        Blue,
        Red,
        Yellow
    }

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MyHint, string?>(nameof(Text));

    public static readonly StyledProperty<MyHintTheme> HintThemeProperty =
        AvaloniaProperty.Register<MyHint, MyHintTheme>(nameof(HintTheme), MyHintTheme.Red);

    public static readonly StyledProperty<bool> HasBorderProperty =
        AvaloniaProperty.Register<MyHint, bool>(nameof(HasBorder));

    public static readonly StyledProperty<bool> CanCloseProperty =
        AvaloniaProperty.Register<MyHint, bool>(nameof(CanClose));

    /// <summary>关闭后应记住的 Settings 键（Linux 侧尚未接入 Settings，仅保留字段以便后续接线）。</summary>
    public static readonly StyledProperty<string?> RelativeSetupProperty =
        AvaloniaProperty.Register<MyHint, string?>(nameof(RelativeSetup));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MyHintTheme HintTheme
    {
        get => GetValue(HintThemeProperty);
        set => SetValue(HintThemeProperty, value);
    }

    public bool HasBorder
    {
        get => GetValue(HasBorderProperty);
        set => SetValue(HasBorderProperty, value);
    }

    public bool CanClose
    {
        get => GetValue(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

    public string? RelativeSetup
    {
        get => GetValue(RelativeSetupProperty);
        set => SetValue(RelativeSetupProperty, value);
    }

    public event EventHandler<PointerReleasedEventArgs>? Click;

    private MyIconButton? _btnClose;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HintThemeProperty)
        {
            var (background, border, foreground) = HintTheme switch
            {
                MyHintTheme.Blue => (Color.Parse("#d9ecff"), Color.Parse("#1172D4"), Color.Parse("#0F64B8")),
                MyHintTheme.Yellow => (Color.Parse("#ffebd7"), Color.Parse("#f57a00"), Color.Parse("#d86c00")),
                _ => (Color.Parse("#ffdddf"), Color.Parse("#d82929"), Color.Parse("#bf0b0b"))
            };
            Background = new SolidColorBrush(background);
            BorderBrush = new SolidColorBrush(border);
            Foreground = new SolidColorBrush(foreground);
        }
        else if (change.Property == HasBorderProperty)
        {
            BorderThickness = HasBorder
                ? new Thickness(3, 1, 1, 1)
                : new Thickness(3, 0, 0, 0);
        }
        else if (change.Property == CanCloseProperty)
        {
            PseudoClasses.Set(":closable", CanClose);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_btnClose is not null) _btnClose.Click -= OnCloseClick;
        _btnClose = e.NameScope.Find<MyIconButton>("PART_BtnClose");
        if (_btnClose is not null) _btnClose.Click += OnCloseClick;

        PseudoClasses.Set(":closable", CanClose);
    }

    private void OnCloseClick(object? sender, EventArgs e) => IsVisible = false;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsEnabled || e.InitialPressMouseButton != MouseButton.Left) return;
        Click?.Invoke(this, e);
    }
}

/// <summary>列表项的勾选形态，对应 `MyListItem.CheckType`。</summary>
public enum MyListItemCheckType
{
    None,
    Clickable,
    RadioBox,
    CheckBox
}

/// <summary>列表项 `Check` 事件参数（对应 `RouteEventArgs` 的 `User` / `Handled`）。</summary>
public sealed class MyListItemCheckEventArgs : EventArgs
{
    public bool User { get; init; }
    public bool Handled { get; set; }
}

/// <summary>
/// 列表项，1:1 对齐 `pcl-next/Controls/MyListItem.xaml(.vb)`：
/// 列 2 / 0 / 4 / 1* / 4 与行 1* / Auto / Auto / 1*（列值随 `Type`、是否有 Logo、高度 &lt; 40 变化）；
/// 标题在第 2 个 Auto 行第 4 列（左边距 4，超出省略，字号/前景色跟随自身）；
/// 勾选竖条 5 宽、ColorBrush3、圆角 2、Margin -1,6,0,6、跨 4 行；右侧按钮 25×25、默认不透明度 0、悬停淡入；
/// 悬停背景 RectBack（ColorBrush7 → ColorBrushBg1、1px ColorBrush6 边框、圆角由 `IsScaleAnimationEnabled`/高度决定）。
/// WPF 的 Ani 逐帧动画改为 Avalonia `Transitions` / `Animation` 近似（登记 R4 / R10）。
/// </summary>
public class MyListItem : Grid
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<MyListItem, string?>(nameof(Title));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<MyListItem, double>(nameof(FontSize), 14d);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<MyListItem, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<string?> LogoProperty =
        AvaloniaProperty.Register<MyListItem, string?>(nameof(Logo));

    public static readonly StyledProperty<double> LogoScaleProperty =
        AvaloniaProperty.Register<MyListItem, double>(nameof(LogoScale), 1d);

    public static readonly StyledProperty<int> MinPaddingRightProperty =
        AvaloniaProperty.Register<MyListItem, int>(nameof(MinPaddingRight), 4);

    /// <summary>导航项编号（对应 WPF 的 `Tag`，`PageSetupLeft` 用它区分四个子页）。</summary>
    public static readonly StyledProperty<int> IndexProperty =
        AvaloniaProperty.Register<MyListItem, int>(nameof(Index));

    public static readonly StyledProperty<MyListItemCheckType> TypeProperty =
        AvaloniaProperty.Register<MyListItem, MyListItemCheckType>(nameof(Type));

    public static readonly StyledProperty<bool> CheckedProperty =
        AvaloniaProperty.Register<MyListItem, bool>(nameof(Checked));

    public static readonly StyledProperty<bool> IsScaleAnimationEnabledProperty =
        AvaloniaProperty.Register<MyListItem, bool>(nameof(IsScaleAnimationEnabled), true);

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public string? Logo { get => GetValue(LogoProperty); set => SetValue(LogoProperty, value); }
    public double LogoScale { get => GetValue(LogoScaleProperty); set => SetValue(LogoScaleProperty, value); }
    public int MinPaddingRight { get => GetValue(MinPaddingRightProperty); set => SetValue(MinPaddingRightProperty, value); }
    public int Index { get => GetValue(IndexProperty); set => SetValue(IndexProperty, value); }
    public MyListItemCheckType Type { get => GetValue(TypeProperty); set => SetValue(TypeProperty, value); }
    public bool Checked { get => GetValue(CheckedProperty); set => SetValue(CheckedProperty, value); }
    public bool IsScaleAnimationEnabled { get => GetValue(IsScaleAnimationEnabledProperty); set => SetValue(IsScaleAnimationEnabledProperty, value); }

    public event EventHandler<PointerReleasedEventArgs>? Click;
    public event EventHandler<MyListItemCheckEventArgs>? Check;

    /// <summary>逐个进入/退出动画期间由 `MyPageLeft` 临时关闭悬停配色（对应 WPF 同名字段）。</summary>
    public bool IsMouseOverAnimationEnabled = true;

    private readonly TextBlock _labTitle;
    private readonly AvaloniaList<MyIconButton> _buttons = new();
    private Border? _rectBack;
    private Border? _rectCheck;
    private Shapes.Path? _pathLogo;
    private bool _isDown;
    private bool _suppressCheckedSync;

    /// <summary>右侧按钮栏（XAML 中以集合子元素提供，对应 WPF 的 `Buttons`）。</summary>
    public AvaloniaList<MyIconButton> Buttons => _buttons;

    public MyListItem()
    {
        ColumnDefinitions = new ColumnDefinitions("2,0,4,*,4");
        RowDefinitions = new RowDefinitions("*,Auto,Auto,*");

        _labTitle = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 0, 0)
        };
        Grid.SetRow(_labTitle, 1);
        Grid.SetColumn(_labTitle, 3);
        _labTitle.Bind(TextBlock.TextProperty, this.GetObservable(TitleProperty));
        _labTitle.Bind(TextBlock.FontSizeProperty, this.GetObservable(FontSizeProperty));
        _labTitle.Bind(TextBlock.ForegroundProperty, this.GetObservable(ForegroundProperty));
        Children.Add(_labTitle);

        _buttons.CollectionChanged += (_, _) => AttachButtons();

        PointerEntered += (_, _) => RefreshColor();
        PointerExited += (_, _) =>
        {
            _isDown = false;
            PseudoClasses.Set(":down", false);
            RefreshColor();
        };
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && Type != MyListItemCheckType.None)
            {
                _isDown = true;
                PseudoClasses.Set(":down", true);
            }
        };
        PointerReleased += OnPointerReleased;

        RefreshMetrics();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LogoProperty) RefreshLogo();
        else if (change.Property == LogoScaleProperty)
        {
            if (_pathLogo is not null) _pathLogo.RenderTransform = new ScaleTransform(LogoScale, LogoScale);
        }
        else if (change.Property == TypeProperty) RefreshType();
        else if (change.Property == MinPaddingRightProperty) RefreshMetrics();
        else if (change.Property == HeightProperty) RefreshMetrics();
        else if (change.Property == CheckedProperty && !_suppressCheckedSync) ApplyCheckedState(animate: false);
    }

    /// <summary>勾选状态（对应 `MyListItem.SetChecked(value, user, anime)`）。</summary>
    public void SetChecked(bool value, bool user)
    {
        if (value == Checked && Type != MyListItemCheckType.RadioBox) return;

        _suppressCheckedSync = true;
        Checked = value;
        _suppressCheckedSync = false;

        if (value)
        {
            var args = new MyListItemCheckEventArgs { User = user };
            Check?.Invoke(this, args);
            if (args.Handled) return;
        }

        EnforceRadioGroup();
        ApplyCheckedState(animate: true);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var wasDown = _isDown;
        _isDown = false;
        PseudoClasses.Set(":down", false);
        RefreshColor();
        if (!wasDown) return;

        Click?.Invoke(this, e);
        if (e.Handled) return;

        if (Type == MyListItemCheckType.RadioBox)
        {
            if (!Checked) SetChecked(true, true);
        }
        else if (Type == MyListItemCheckType.CheckBox)
        {
            SetChecked(!Checked, true);
        }
    }

    /// <summary>同 `MyListItem.vb` L394–L432：同一父容器内只允许一个 RadioBox 选中。</summary>
    private void EnforceRadioGroup()
    {
        if (Type != MyListItemCheckType.RadioBox || Parent is not Panel panel) return;

        var group = new List<MyListItem>();
        foreach (var child in panel.Children)
        {
            if (child is MyListItem item && item.Type == MyListItemCheckType.RadioBox) group.Add(item);
        }
        if (group.Count == 0) return;

        var checkedCount = group.Count(i => i.Checked);
        if (checkedCount == 0)
        {
            group[0].Checked = true;
        }
        else if (checkedCount > 1)
        {
            if (Checked)
            {
                foreach (var item in group)
                {
                    if (item.Checked && item != this) item.Checked = false;
                }
            }
            else
            {
                var first = true;
                foreach (var item in group)
                {
                    if (!item.Checked) continue;
                    if (first) first = false;
                    else item.Checked = false;
                }
            }
        }
    }

    private void ApplyCheckedState(bool animate)
    {
        PseudoClasses.Set(":checked", Checked);

        if (_rectCheck is null) return;
        if (Checked)
        {
            _rectCheck.VerticalAlignment = VerticalAlignment.Stretch;
            _rectCheck.Margin = new Thickness(-1, 6, 0, 6);
            _rectCheck.Height = double.NaN;
            if (animate) AnimateOpacity(_rectCheck, 1, 30);
            else _rectCheck.Opacity = 1;
        }
        else
        {
            _rectCheck.VerticalAlignment = VerticalAlignment.Center;
            _rectCheck.Margin = new Thickness(-1, 0, 0, 0);
            _rectCheck.Height = 0;
            if (animate) AnimateOpacity(_rectCheck, 0, 70);
            else _rectCheck.Opacity = 0;
        }
    }

    private void RefreshType()
    {
        if (Type is MyListItemCheckType.None or MyListItemCheckType.Clickable)
        {
            if (_rectCheck is not null)
            {
                Children.Remove(_rectCheck);
                _rectCheck = null;
            }
            RefreshMetrics();
            return;
        }

        if (_rectCheck is null)
        {
            _rectCheck = new Border
            {
                Name = "PART_RectCheck",
                Width = 5,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            };
            Grid.SetRowSpan(_rectCheck, 4);
            Children.Insert(Children.IndexOf(_labTitle), _rectCheck);
        }
        RefreshMetrics();
        ApplyCheckedState(animate: false);
    }

    private void RefreshLogo()
    {
        if (_pathLogo is not null)
        {
            Children.Remove(_pathLogo);
            _pathLogo = null;
        }

        var logo = Logo;
        if (string.IsNullOrEmpty(logo))
        {
            RefreshMetrics();
            return;
        }

        var path = new Shapes.Path
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new ScaleTransform(LogoScale, LogoScale)
        };
        path.Data = Geometry.Parse(logo);
        path.Bind(Shapes.Shape.FillProperty, this.GetObservable(ForegroundProperty));
        Grid.SetColumn(path, 2);
        Grid.SetRowSpan(path, 4);
        Children.Add(path);
        _pathLogo = path;
        RefreshMetrics();
    }

    private void AttachButtons()
    {
        foreach (var button in _buttons)
        {
            if (Children.Contains(button)) continue;
            if (double.IsNaN(button.Width)) button.Width = 25;
            if (double.IsNaN(button.Height)) button.Height = 25;
            button.Margin = new Thickness(0, 0, 5, 0);
            button.Opacity = 0;
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumnSpan(button, 10);
            Grid.SetRowSpan(button, 10);
            Children.Add(button);
        }
    }

    /// <summary>列宽/边距随 `Type`、Logo、高度变化（同 `MyListItem.vb` L329–L340）。</summary>
    private void RefreshMetrics()
    {
        var height = Height;
        var small = !double.IsNaN(height) && height < 40;

        ColumnDefinitions[0].Width = new GridLength(
            Type is MyListItemCheckType.None or MyListItemCheckType.Clickable ? (small ? 4 : 2) : 6);
        ColumnDefinitions[2].Width = new GridLength(string.IsNullOrEmpty(Logo) ? 0 : 34 + (small ? 0 : 4));
        ColumnDefinitions[4].Width = new GridLength(MinPaddingRight);
        _labTitle.Margin = new Thickness(4, 0, 0, small ? 0 : 2);
        if (_pathLogo is not null) _pathLogo.Margin = new Thickness(small ? 6 : 8, 8, small ? 4 : 6, 8);

        PseudoClasses.Set(":small", small);
    }

    private void RefreshColor()
    {
        var hover = IsPointerOver && IsMouseOverAnimationEnabled;

        if (hover) ShowButtons(true);
        else ShowButtons(false);

        if (hover)
        {
            var rect = EnsureRectBack();
            rect.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(120),
                    Easing = new CubicEaseOut()
                }
            };
            rect.Opacity = 1;
        }
        else if (_rectBack is not null)
        {
            _rectBack.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(180),
                    Easing = new CubicEaseOut()
                }
            };
            _rectBack.Opacity = 0;
        }
    }

    private Border EnsureRectBack()
    {
        if (_rectBack is not null) return _rectBack;

        var height = Height;
        _rectBack = new Border
        {
            Name = "PART_RectBack",
            CornerRadius = new CornerRadius(IsScaleAnimationEnabled || (!double.IsNaN(height) && height > 40) ? 6 : 0),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Opacity = 0
        };
        Grid.SetColumnSpan(_rectBack, 10);
        Grid.SetRowSpan(_rectBack, 10);
        Children.Insert(0, _rectBack);
        return _rectBack;
    }

    private void ShowButtons(bool show)
    {
        foreach (var button in _buttons)
        {
            button.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(show ? 84 : 48),
                    Easing = new CubicEaseOut()
                }
            };
            button.Opacity = show ? 1 : 0;
        }
    }

    private static void AnimateOpacity(Visual visual, double to, int ms)
    {
        visual.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(ms),
                Easing = new CubicEaseOut()
            }
        };
        visual.Opacity = to;
    }
}

/// <summary>
/// 加载指示器，1:1 对齐 `pcl-next/Controls/MyLoading.xaml(.vb)`：
/// 最小 50×50、背景 #01eaf2fe；图形区 60×47（镐子 35 高、描边 2、旋转 55°、边距 10,6,0,0；
/// 左右小碎片 3×5、旋转 ∓45°、不透明度 0；错误叉 17×17、缩放 0.6、不透明度 0；底部横线 25×2）；
/// 文本字号 16、上边距 10，默认「加载中」，错误态默认「加载失败」；前景默认 ColorBrush3。
/// 镐子摆动在 WPF 由代码逐帧驱动，本轮改为无限循环旋转近似（登记项 R10）。
/// </summary>
public class MyLoading : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MyLoading, string?>(nameof(Text), "加载中");

    public static readonly StyledProperty<string?> TextErrorProperty =
        AvaloniaProperty.Register<MyLoading, string?>(nameof(TextError), "加载失败");

    public static readonly StyledProperty<bool> IsErrorProperty =
        AvaloniaProperty.Register<MyLoading, bool>(nameof(IsError));

    public static readonly StyledProperty<bool> ShowProgressProperty =
        AvaloniaProperty.Register<MyLoading, bool>(nameof(ShowProgress));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? TextError
    {
        get => GetValue(TextErrorProperty);
        set => SetValue(TextErrorProperty, value);
    }

    public bool IsError
    {
        get => GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public bool ShowProgress
    {
        get => GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    private TextBlock? _labText;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == TextErrorProperty || change.Property == IsErrorProperty)
            RefreshText();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _labText = e.NameScope.Find<TextBlock>("PART_LabText");
        RefreshText();
    }

    private void RefreshText()
    {
        if (_labText is null) return;
        _labText.Text = IsError ? (TextError ?? "加载失败") : (Text ?? "加载中");
    }
}

/// <summary>
/// 图标按钮，1:1 对齐 `pcl-next/Controls/MyIconButton.xaml(.vb)`：
/// 外层圆角 1000、背景 #01eaf2fe、边框 0.001；内层同圆角/边框，承载 Path（Stretch=Uniform、边距 5），
/// 内外两层都以中心为缩放原点。按下内层缩到 0.8（400ms），抬起回弹到 1.05 再回到 1（各 250ms）。
/// Logo 以 SVG 路径字符串传入（与 WPF 的 GeometryConverter 同源）。
/// 主题色映射（Color/White/Black/Red/Custom）在源码中按动画分支分散实现，
/// 此处按出现频次取 ColorBrush3 / White / ColorBrush1 / RedLight（登记项 R12）。
/// </summary>
public class MyIconButton : TemplatedControl
{
    public enum MyIconButtonTheme
    {
        Color,
        White,
        Black,
        Red,
        Custom
    }

    public static readonly StyledProperty<string?> LogoProperty =
        AvaloniaProperty.Register<MyIconButton, string?>(nameof(Logo));

    public static readonly StyledProperty<double> LogoScaleProperty =
        AvaloniaProperty.Register<MyIconButton, double>(nameof(LogoScale), 1d);

    public static readonly StyledProperty<MyIconButtonTheme> IconThemeProperty =
        AvaloniaProperty.Register<MyIconButton, MyIconButtonTheme>(nameof(IconTheme), MyIconButtonTheme.Color);

    public static readonly StyledProperty<bool> CanClickProperty =
        AvaloniaProperty.Register<MyIconButton, bool>(nameof(CanClick), true);

    public string? Logo
    {
        get => GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    public double LogoScale
    {
        get => GetValue(LogoScaleProperty);
        set => SetValue(LogoScaleProperty, value);
    }

    public MyIconButtonTheme IconTheme
    {
        get => GetValue(IconThemeProperty);
        set => SetValue(IconThemeProperty, value);
    }

    public bool CanClick
    {
        get => GetValue(CanClickProperty);
        set => SetValue(CanClickProperty, value);
    }

    public event EventHandler? Click;

    private Avalonia.Controls.Shapes.Path? _path;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LogoProperty) SyncLogo();
        if (change.Property == LogoScaleProperty) SyncLogo();
        if (change.Property == IconThemeProperty)
        {
            Foreground = IconTheme switch
            {
                MyIconButtonTheme.White => Brushes.White,
                MyIconButtonTheme.Black => new SolidColorBrush(Color.Parse("#343d4a")),
                MyIconButtonTheme.Red => new SolidColorBrush(Color.Parse("#ff4c4c")),
                MyIconButtonTheme.Custom => Foreground,
                _ => new SolidColorBrush(Color.Parse("#1370f3"))
            };
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _path = e.NameScope.Find<Avalonia.Controls.Shapes.Path>("PART_Path");
        SyncLogo();
    }

    private void SyncLogo()
    {
        if (_path is null) return;
        _path.Data = string.IsNullOrWhiteSpace(Logo) ? null : Geometry.Parse(Logo);
        // 对照 MyIconButton.xaml.vb：Logo 上挂 ScaleTransform，缩放中心 0.5,0.5（模板 Margin 5、Stretch Uniform）
        _path.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _path.RenderTransform = new ScaleTransform(LogoScale, LogoScale);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !CanClick) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        PseudoClasses.Set(":pressed", true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        PseudoClasses.Set(":pressed", false);
        if (!IsEnabled || !CanClick) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        Click?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// 单选按钮，1:1 对齐 `pcl-next/Controls/MyRadioButton.xaml(.vb)`：
/// 圆角 13.5、`MinHeight/MaxHeight 27`、常态底 `ColorBrushSemiTransparent`；图标上限 16（`Margin 12,0,0,0`）、
/// 文案 `Margin 8,0,12,0`，白字白图标；选中 → 白底 + `ColorBrush3`；悬停 → 50 alpha `Color8` 底；
/// 按下 → 120 alpha `Color8` 底（WPF 的 90/120/150ms 颜色过渡改为即时切换，并入 R10）。
/// 同父容器内互斥语义照抄原 `SetChecked`：自己选中时取消同层其他按钮。
/// `ColorState.Highlight` 分支当前无调用点，未接入（用到时按原 vb L200–L225 补）。
/// </summary>
public class MyRadioButton : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MyRadioButton, string?>(nameof(Text));

    public static readonly StyledProperty<string?> LogoProperty =
        AvaloniaProperty.Register<MyRadioButton, string?>(nameof(Logo));

    public static readonly StyledProperty<double> LogoScaleProperty =
        AvaloniaProperty.Register<MyRadioButton, double>(nameof(LogoScale), 1d);

    public static readonly StyledProperty<bool> CheckedProperty =
        AvaloniaProperty.Register<MyRadioButton, bool>(nameof(Checked));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Logo
    {
        get => GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    public double LogoScale
    {
        get => GetValue(LogoScaleProperty);
        set => SetValue(LogoScaleProperty, value);
    }

    public bool Checked
    {
        get => GetValue(CheckedProperty);
        set => SetValue(CheckedProperty, value);
    }

    /// <summary>参数为勾选后的状态（对应原 `Check(raiseByMouse)` 的勾选态）。</summary>
    public event EventHandler<bool>? Check;

    private Avalonia.Controls.Shapes.Path? _path;
    private bool _syncing;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LogoProperty || change.Property == LogoScaleProperty) SyncLogo();
        if (change.Property == CheckedProperty) OnCheckedChanged();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _path = e.NameScope.Find<Avalonia.Controls.Shapes.Path>("PART_Path");
        SyncLogo();
        PseudoClasses.Set(":checked", Checked);
    }

    private void SyncLogo()
    {
        if (_path is null) return;
        _path.Data = string.IsNullOrWhiteSpace(Logo) ? null : Geometry.Parse(Logo);
        _path.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _path.RenderTransform = new ScaleTransform(LogoScale, LogoScale);
    }

    private void OnCheckedChanged()
    {
        PseudoClasses.Set(":checked", Checked);
        if (Checked && !_syncing) UncheckSiblings();
        Check?.Invoke(this, Checked);
    }

    private void UncheckSiblings()
    {
        if (Parent is not Panel panel) return;
        foreach (var sibling in panel.Children)
        {
            if (sibling is MyRadioButton radio && !ReferenceEquals(radio, this) && radio.Checked)
            {
                radio._syncing = true;
                radio.Checked = false;
                radio._syncing = false;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsEnabled) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (!Checked) Checked = true;
    }
}

/// <summary>
/// 左侧页基类，1:1 对齐 `pcl-next/Controls/MyPageLeft.vb`：
/// `AnimatedControl` 指向容器时，对其中每个元素逐个做「下移 -25 → 100ms 淡入 + 500ms 位移」的错峰进入动画；
/// 置空时退化为整页 0.96 → 1 缩放 + 淡入。退出方向为「-6 位移 + 50ms 淡出」，错峰间隔 70/N。
/// WPF 的两段位移（先 +5 再 +20）与工作日缓动改为 Avalonia 单段 `Transitions` 近似（登记 R4 / R10）。
/// </summary>
public class MyPageLeft : Grid
{
    public static readonly StyledProperty<string?> AnimatedControlProperty =
        AvaloniaProperty.Register<MyPageLeft, string?>(nameof(AnimatedControl));

    public string? AnimatedControl
    {
        get => GetValue(AnimatedControlProperty);
        set => SetValue(AnimatedControlProperty, value);
    }

    public void TriggerShowAnimation()
    {
        var items = GetAnimatedItems();
        if (AnimatedControl is null || items.Count == 0)
        {
            // Windows `MyPageLeft.vb` L17–L28 只在 AnimatedControl 为空时走缩放；
            // 指定了 AnimatedControl 但没有可动画元素（如版本选择页的空列表）时，原逻辑不产生任何动画。
            if (AnimatedControl is not null) return;
            ShowWithScale();
            return;
        }

        double delay = 0;
        var id = 0;
        foreach (var item in items)
        {
            var element = (Control)item;
            if (!element.IsVisible)
            {
                element.Opacity = 1;
                element.RenderTransform = null;
                delay += Math.Max(15 - id, 7) * 2;
                id++;
                continue;
            }

            element.Opacity = 0;
            element.RenderTransformOrigin = RelativePoint.Center;
            element.RenderTransform = TransformOperations.Parse("translate(-25px,0px)");
            element.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(100),
                    Delay = TimeSpan.FromMilliseconds(delay),
                    Easing = new CubicEaseOut()
                },
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(500),
                    Delay = TimeSpan.FromMilliseconds(delay),
                    Easing = new BackEaseOut()
                }
            };
            element.Opacity = element is TextBlock ? 0.6 : 1;
            element.RenderTransform = TransformOperations.Identity;
            delay += Math.Max(15 - id, 7) * 2;
            id++;
        }
    }

    public void TriggerHideAnimation()
    {
        var items = GetAnimatedItems();
        if (AnimatedControl is null || items.Count == 0)
        {
            // 同 TriggerShowAnimation：指定了 AnimatedControl 但无元素时不产生动画
            if (AnimatedControl is not null) return;
            HideWithScale();
            return;
        }

        var count = items.Count;
        var id = 0;
        foreach (var item in items)
        {
            item.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(50),
                    Delay = TimeSpan.FromMilliseconds(70d * id / count),
                    Easing = new CubicEaseOut()
                },
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(50),
                    Delay = TimeSpan.FromMilliseconds(70d * id / count),
                    Easing = new CubicEaseOut()
                }
            };
            item.Opacity = 0;
            item.RenderTransform = TransformOperations.Parse("translate(-6px,0px)");
            id++;
        }
    }

    private void ShowWithScale()
    {
        Opacity = 0;
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = TransformOperations.Parse("scale(0.96)");
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(100),
                Easing = new CubicEaseOut()
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(400),
                Easing = new BackEaseOut()
            }
        };
        Opacity = 1;
        RenderTransform = TransformOperations.Identity;
    }

    private void HideWithScale()
    {
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(400),
                Easing = new CubicEaseOut()
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(400),
                Easing = new BackEaseOut()
            }
        };
        Opacity = 0;
        RenderTransform = TransformOperations.Parse("scale(0.95)");
    }

    private List<Visual> GetAnimatedItems()
    {
        var list = new List<Visual>();
        if (string.IsNullOrEmpty(AnimatedControl)) return list;

        var root = this.FindControl<Control>(AnimatedControl);
        if (root is null) return list;

        Collect(root, list);
        return list;
    }

    /// <summary>同 `MyPageLeft.vb` 的 `GetAnimatedControls`：面板递归到叶子，其余元素各自参与动画。</summary>
    private static void Collect(Visual visual, List<Visual> list)
    {
        if (visual is Panel panel)
        {
            foreach (var child in panel.Children) Collect(child, list);
            return;
        }
        list.Add(visual);
    }
}
