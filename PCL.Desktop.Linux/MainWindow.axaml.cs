using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using PCL.Desktop.Linux.Controls;
using PCL.Desktop.Linux.Theme;

namespace PCL.Desktop.Linux;

/// <summary>
/// 外壳行为，逐项对照 `pcl-next/FormMain.xaml(.vb)`：
/// · 标题栏拖动（vb L713–L717 `FormDragMove`）
/// · 最小化 / 关闭（vb L709–L711、L737–L740）
/// · 8 个缩放热区（vb L408–L417 `MyResizer`）
/// · 开场动画（vb L443–L454）
/// · 标题栏渐变与主页面背景（`ModSecret.vb` L111–L135 `ThemeRefreshMain`）
/// · 栈切换（vb L1438–L1471 `PageChange`）
/// </summary>
public partial class MainWindow : Window
{
    private readonly Control? _linkPage;

    public MainWindow()
    {
        InitializeComponent();

        // 主题：标题栏渐变与主页面背景在 Windows 端是运行期按主题色算出的（ModSecret.vb L116–L132）
        PanTitle.Background = PclTheme.CreateTitleBarBrush();
        PanForm.Background = PclTheme.CreateContentBrush();

        // 开场动画：PanBack 起于 translate(0,60px) rotate(-4°)，600ms / 500ms 归零（vb L443–L454），
        // 缓动由 AniEaseOutBack（弱）改为 BackEaseOut 近似（R19）
        PanBack.RenderTransform = TransformOperations.Parse("translate(0px,60px) rotate(-4deg)");
        PanBack.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(600),
                Easing = new BackEaseOut()
            }
        };
        Opened += (_, _) => PanBack.RenderTransform = TransformOperations.Identity;

        // 窗口按钮（vb L709–L711、L737–L740）
        BtnTitleClose.Click += (_, _) => Close();
        BtnTitleMin.Click += (_, _) => WindowState = WindowState.Minimized;

        // 标题栏拖动（vb L713–L717）：只在没有落在交互控件上时才拖动窗口
        PanTitle.PointerPressed += OnTitlePressed;

        HookResizers();

        // 栈切换（vb L1438–L1471）：当前只有「联机」栈的页面已移植（R20）
        _linkPage = PageRight.Content as Control;
        BtnTitleSelect0.Check += (_, on) => { if (on) ShowStack(0); };
        BtnTitleSelect1.Check += (_, on) => { if (on) ShowStack(1); };
        BtnTitleSelect2.Check += (_, on) => { if (on) ShowStack(2); };
        BtnTitleSelect3.Check += (_, on) => { if (on) ShowStack(3); };
        BtnTitleSelect4.Check += (_, on) => { if (on) ShowStack(4); };
        ShowStack(2);
    }

    /// <summary>
    /// 切换页面栈。Windows 端「联机」栈的左页是空的（vb L1551 `PageChangeAnim(New MyPageLeft, FrmLinkMain)`），
    /// 其余四栈的页面尚未移植，宿主置空（登记 R20）。
    /// </summary>
    private void ShowStack(int index)
    {
        PageRight.Content = index == 2 ? _linkPage : null;
        PageLeft.Content = null;
    }

    private void OnTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsOverInteractiveControl(e.Source as Visual)) return;
        BeginMoveDrag(e);
    }

    /// <summary>
    /// 原实现靠 `IsMouseDirectlyOver` 区分「点在标题栏空白处」与「点在按钮上」（vb L716）；
    /// Avalonia 没有等价判断，这里改为向上找交互控件（R19）。
    /// </summary>
    private static bool IsOverInteractiveControl(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is MyIconButton or MyRadioButton) return true;
        }
        return false;
    }

    /// <summary>8 个缩放热区，对应 `MyResizer.addResizer*`（vb L408–L417）。</summary>
    private void HookResizers()
    {
        HookResizer(ResizerT, WindowEdge.North, StandardCursorType.TopSide);
        HookResizer(ResizerB, WindowEdge.South, StandardCursorType.BottomSide);
        HookResizer(ResizerL, WindowEdge.West, StandardCursorType.LeftSide);
        HookResizer(ResizerR, WindowEdge.East, StandardCursorType.RightSide);
        HookResizer(ResizerLT, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner);
        HookResizer(ResizerLB, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner);
        HookResizer(ResizerRT, WindowEdge.NorthEast, StandardCursorType.TopRightCorner);
        HookResizer(ResizerRB, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner);
    }

    private void HookResizer(Control resizer, WindowEdge edge, StandardCursorType cursor)
    {
        resizer.Cursor = new Cursor(cursor);
        resizer.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginResizeDrag(edge, e);
            e.Handled = true;
        };
    }
}
