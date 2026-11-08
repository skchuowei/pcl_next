using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

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
/// 下拉框，对齐 `pcl-next/Application.xaml` 的 MyComboBox 样式：
/// 1px 边框 ColorBrushBg0、背景 #55FFFFFF、圆角 3、字号 13、下拉项高 26。
/// 当前只按属性级样式对齐，完整弹出层模板与模板化下拉按钮尚待逐像素核对（登记项 R6）。
/// </summary>
public class MyComboBox : ComboBox
{
}

/// <summary>
/// 下拉项，对齐 `pcl-next/Controls/MyComboBoxItem.vb` 的视觉（无额外装饰，供 ComboBox 使用）。
/// </summary>
public class MyComboBoxItem : ComboBoxItem
{
}
