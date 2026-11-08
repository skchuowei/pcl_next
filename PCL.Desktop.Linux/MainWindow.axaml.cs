using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PCL.Desktop.Linux.Controls;
using PCL.Desktop.Linux.Theme;
using PCL.Desktop.Linux.Views;

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
    private MyPageLeft? _setupLeftPage;
    private CancellationTokenSource? _leftWidthAnimation;

    public MainWindow()
    {
        InitializeComponent();

        // 主题：标题栏渐变与主页面背景在 Windows 端是运行期按主题色算出的（ModSecret.vb L116–L132）。
        // 启动时按 Windows 端 `ThemeRefresh(Settings("UiLauncherTheme"))` 的调用方式刷新一次（默认 0，Settings.vb L144）
        PclTheme.Changed += ApplyThemeBrushes;
        PclTheme.Refresh(0);

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
    /// 套用主题渐变（`ModSecret.vb` L111–L135 `ThemeRefreshMain`）：标题栏与主页面背景两支画刷。
    /// 主题参数当前恒为默认值（计划 §13.8 核实结论），但入口按 Windows 端语义保留，便于后续皮肤接入。
    /// </summary>
    private void ApplyThemeBrushes()
    {
        PanTitle.Background = PclTheme.CreateTitleBarBrush();
        PanForm.Background = PclTheme.CreateContentBrush();
    }

    /// <summary>
    /// 切换页面栈（vb L1438–L1471）。Windows 端「联机」栈的左页是空的（L1551 `PageChangeAnim(New MyPageLeft, FrmLinkMain)`）；
    /// 「设置」栈左页用 `PageSetupLeftView`，右页尚缺设置子页（登记 R20）；其余三栈两页都未移植，宿主置空。
    /// </summary>
    private void ShowStack(int index)
    {
        if (PageLeft.Content is MyPageLeft leaving && index != 3) leaving.TriggerHideAnimation();

        switch (index)
        {
            case 2:
                PageLeft.Content = null;
                PageRight.Content = _linkPage;
                break;
            case 3:
                _setupLeftPage ??= new PageSetupLeftView();
                PageRight.Content = null;
                PageLeft.Content = _setupLeftPage;
                // 页面刚挂上，等布局跑完再取宽度并播放逐个进入动画（对应 Windows 端 `TriggerShowAnimation`）
                Dispatcher.UIThread.Post(() =>
                {
                    _setupLeftPage.TriggerShowAnimation();
                    UpdateLeftBackgroundWidth();
                }, DispatcherPriority.Loaded);
                return;
            default:
                PageLeft.Content = null;
                PageRight.Content = null;
                break;
        }

        UpdateLeftBackgroundWidth();
    }

    /// <summary>
    /// 左侧竖条宽度跟随左页宽度（`FormMain.xaml.vb` `PanMainLeft_Resize` / `PanLeft_Resize`）：
    /// `RectLeftBackground.Width` 180ms 渐变到新宽度，收窄到 0 时阴影同步淡出 180ms。
    /// WPF 的 Ani 逐帧缓动改为 Avalonia `Animation`（登记 R4 / R10）。
    /// </summary>
    private void UpdateLeftBackgroundWidth()
    {
        var target = PanMainLeft.Bounds.Width;
        _leftWidthAnimation?.Cancel();

        if (Math.Abs(RectLeftBackground.Width - target) < 0.1)
        {
            RectLeftBackground.Width = target;
            return;
        }

        var cts = new CancellationTokenSource();
        _leftWidthAnimation = cts;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = target > 0 ? new ExponentialEaseOut() : new QuarticEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Layoutable.WidthProperty, target) }
                }
            }
        };
        _ = animation.RunAsync(RectLeftBackground, cts.Token);

        if (target <= 0.1) AnimateShadowOpacity(0);
        else RectLeftShadow.Opacity = 1;
    }

    /// <summary>阴影只在竖条收窄到 0 时淡出（同 `PanLeft_Resize` 的 `AaOpacity` 分支）。</summary>
    private void AnimateShadowOpacity(double target)
    {
        RectLeftShadow.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new CubicEaseOut()
            }
        };
        RectLeftShadow.Opacity = target;
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
