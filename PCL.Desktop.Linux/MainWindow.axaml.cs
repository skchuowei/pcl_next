using System;
using System.Collections.Generic;
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
    /// <summary>版本选择是次级页面栈（Windows `PageType.InstanceSelect`），不属于标题栏 5 个 Tab。</summary>
    private const int StackVersionSelect = 5;

    /// <summary>版本设置是次级页面栈（Windows `PageType.InstanceSetup`，左导航为 `FrmInstanceLeft`）。</summary>
    private const int StackInstanceSetup = 6;

    private readonly Control? _linkPage;
    private readonly PageLaunchLeftView _launchLeft;
    private readonly PageLaunchRightView _launchRight;
    private readonly PageSelectLeftView _selectLeft;
    private readonly PageSelectRightView _selectRight;
    private readonly PageDownloadLeftView _downloadLeft;
    private readonly PageDownloadInstallView _downloadInstall;
    private readonly Dictionary<int, PageResourceView> _downloadResourcePages = new();
    private PageSetupLeftView? _setupLeftPage;
    private PageSetupLaunchView? _setupLaunchPage;
    private PageSetupUIView? _setupUiPage;
    private PageSetupSystemView? _setupSystemPage;
    private PageOtherLeftView? _otherLeftPage;
    private PageOtherHelpView? _otherHelpPage;
    private PageOtherAboutView? _otherAboutPage;
    private PageOtherTestView? _otherTestPage;
    private PageInstanceLeftView? _instanceLeftPage;
    private PageInstanceOverallView? _instanceOverallPage;
    private PageInstanceSetupView? _instanceSetupPage;
    private PageInstanceModView? _instanceModPage;
    private PageInstanceModDisabledView? _instanceModDisabledPage;
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

        // 页面实例：启动栈两页（vb L329–L330 在 FormMain 构造函数里预先创建）、版本选择栈两页
        _launchLeft = new PageLaunchLeftView();
        _launchRight = new PageLaunchRightView();
        _selectLeft = new PageSelectLeftView();
        _selectRight = new PageSelectRightView();
        _downloadLeft = new PageDownloadLeftView();
        _downloadInstall = new PageDownloadInstallView();
        _linkPage = new PageSetupLinkView();

        _launchLeft.VersionSelectRequested += (_, _) => ShowStack(StackVersionSelect);
        _launchLeft.DownloadRequested += (_, _) => ShowStack(1);
        _selectRight.DownloadRequested += (_, _) => ShowStack(1);
        _downloadLeft.PageChanged += (_, id) => ShowDownloadPage(id);

        // 栈切换（vb L1438–L1471）
        BtnTitleSelect0.Check += (_, on) => { if (on) ShowStack(0); };
        BtnTitleSelect1.Check += (_, on) => { if (on) ShowStack(1); };
        BtnTitleSelect2.Check += (_, on) => { if (on) ShowStack(2); };
        BtnTitleSelect3.Check += (_, on) => { if (on) ShowStack(3); };
        BtnTitleSelect4.Check += (_, on) => { if (on) ShowStack(4); };

        // 首页 = 启动栈（vb L378–L385：直接把 FrmLaunchLeft / FrmLaunchRight 挂到左右宿主，无切换动画）
        ShowStack(0);
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
    /// 切换页面栈（vb L1438–L1471）。
    /// 0 启动 / 1 下载 / 2 联机 / 3 设置 / 4 更多 均已有页面；
    /// 5 = 版本选择次级栈（`PageType.InstanceSelect`，标题栏保持「启动」选中，与原逻辑一致）。
    /// </summary>
    private void ShowStack(int index)
    {
        if (PageLeft.Content is MyPageLeft leaving) leaving.TriggerHideAnimation();

        switch (index)
        {
            case 0:
                PageLeft.Content = _launchLeft;
                PageRight.Content = _launchRight;
                // 页面刚挂上，等布局跑完再播放进入动画（对应 Windows 端 `TriggerShowAnimation`）
                Dispatcher.UIThread.Post(() => _launchLeft.TriggerShowAnimation(), DispatcherPriority.Loaded);
                break;
            case 1:
                PageLeft.Content = _downloadLeft;
                PageRight.Content = _downloadInstall;
                // 页面刚挂上，等布局跑完再播放进入动画（对应 Windows 端 `TriggerShowAnimation`）
                Dispatcher.UIThread.Post(() => _downloadLeft.TriggerShowAnimation(), DispatcherPriority.Loaded);
                break;
            case 2:
                PageLeft.Content = null;
                PageRight.Content = _linkPage;
                break;
            case 3:
                if (_setupLeftPage is null)
                {
                    _setupLeftPage = new PageSetupLeftView();
                    _setupLeftPage.PageChanged += (_, id) => ShowSetupPage(id);
                }
                PageLeft.Content = _setupLeftPage;
                // 设置栈默认子页 = 启动（`PageSetupLeft.xaml.vb` L20–L30 / L44–L55）
                ShowSetupPage(_setupLeftPage.PageId);
                // 页面刚挂上，等布局跑完再取宽度并播放逐个进入动画（对应 Windows 端 `TriggerShowAnimation`）
                Dispatcher.UIThread.Post(() =>
                {
                    _setupLeftPage.TriggerShowAnimation();
                    UpdateLeftBackgroundWidth();
                }, DispatcherPriority.Loaded);
                return;
            case 4:
                if (_otherLeftPage is null)
                {
                    _otherLeftPage = new PageOtherLeftView();
                    _otherLeftPage.PageChanged += (_, id) => ShowOtherPage(id);
                }
                PageLeft.Content = _otherLeftPage;
                // 更多栈默认子页 = 帮助（`PageOtherLeft.xaml.vb` L37–L47）
                ShowOtherPage(_otherLeftPage.PageId);
                Dispatcher.UIThread.Post(() =>
                {
                    _otherLeftPage.TriggerShowAnimation();
                    UpdateLeftBackgroundWidth();
                }, DispatcherPriority.Loaded);
                return;
            case StackVersionSelect:
                PageLeft.Content = _selectLeft;
                PageRight.Content = _selectRight;
                Dispatcher.UIThread.Post(() => _selectLeft.TriggerShowAnimation(), DispatcherPriority.Loaded);
                break;
            case StackInstanceSetup:
                if (_instanceLeftPage is null)
                {
                    _instanceLeftPage = new PageInstanceLeftView();
                    _instanceLeftPage.PageChanged += (_, id) => ShowInstancePage(id);
                }
                PageLeft.Content = _instanceLeftPage;
                ShowInstancePage(_instanceLeftPage.PageId);
                Dispatcher.UIThread.Post(() =>
                {
                    _instanceLeftPage.TriggerShowAnimation();
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
    /// 下载栈子页切换（对应 `PageDownloadLeft.xaml.vb` L59–L70 `PageChange`）：
    /// 0 原版游戏；2 Mod / 3 整合包 / 4 数据包 / 5 资源包 / 6 光影包共用 `PageResourceView`（R40）。
    /// </summary>
    private void ShowDownloadPage(int pageId)
    {
        if (pageId == 0)
        {
            PageRight.Content = _downloadInstall;
            return;
        }

        if (!_downloadResourcePages.TryGetValue(pageId, out var page))
        {
            ResourcePageKind? kind = pageId switch
            {
                2 => ResourcePageKind.Mod,
                3 => ResourcePageKind.ModPack,
                4 => ResourcePageKind.DataPack,
                5 => ResourcePageKind.ResourcePack,
                6 => ResourcePageKind.Shader,
                _ => null
            };
            if (kind is null)
            {
                PageRight.Content = null;
                return;
            }
            page = new PageResourceView(kind.Value);
            _downloadResourcePages[pageId] = page;
        }
        PageRight.Content = page;
    }

    /// <summary>
    /// 设置栈子页切换（对应 `PageSetupLeft.xaml.vb` L93–L122 `PageChange`）：
    /// 0 启动 / 1 联机 / 2 个性化 / 3 其他（均已移植）。
    /// </summary>
    private void ShowSetupPage(int pageId)
    {
        PageRight.Content = pageId switch
        {
            0 => _setupLaunchPage ??= new PageSetupLaunchView(),
            1 => _linkPage,
            2 => _setupUiPage ??= new PageSetupUIView(),
            3 => _setupSystemPage ??= new PageSetupSystemView(),
            _ => null
        };
    }

    /// <summary>
    /// 更多栈子页切换（对应 `PageOtherLeft.xaml.vb` L58–L73 `PageGet`）：
    /// 0 帮助 / 1 关于与鸣谢 / 2 百宝箱（3 反馈 / 4 新功能投票不切页，R46）。
    /// </summary>
    private void ShowOtherPage(int pageId)
    {
        PageRight.Content = pageId switch
        {
            0 => _otherHelpPage ??= new PageOtherHelpView(),
            1 => _otherAboutPage ??= new PageOtherAboutView(),
            2 => _otherTestPage ??= new PageOtherTestView(),
            _ => null
        };
    }

    /// <summary>
    /// 实例设置栈子页切换（对应 `PageInstanceLeft.xaml.vb` L39–L63 `PageGet`）：
    /// 0 概览 / 1 设置 / 3 Mod / 4 ModDisabled 已移植；2 修改（PageDownloadModify）与 5 导出尚未移植（R50）。
    /// </summary>
    private void ShowInstancePage(int pageId)
    {
        switch (pageId)
        {
            case 0:
                PageRight.Content = _instanceOverallPage ??= new PageInstanceOverallView();
                break;
            case 1:
                PageRight.Content = _instanceSetupPage ??= new PageInstanceSetupView();
                break;
            case 3:
                PageRight.Content = _instanceModPage ??= new PageInstanceModView();
                break;
            case 4:
                if (_instanceModDisabledPage is null)
                {
                    _instanceModDisabledPage = new PageInstanceModDisabledView();
                    _instanceModDisabledPage.DownloadRequested += (_, _) => ShowStack(1);
                    _instanceModDisabledPage.VersionSelectRequested += (_, _) => ShowStack(StackVersionSelect);
                }
                PageRight.Content = _instanceModDisabledPage;
                break;
            default:
                // 2 修改 / 5 导出：页面未移植（R50），宿主置空
                PageRight.Content = null;
                break;
        }
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
