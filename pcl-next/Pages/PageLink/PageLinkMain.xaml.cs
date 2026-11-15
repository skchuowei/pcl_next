using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Extensions;

namespace PCL
{

    /// <summary>
    /// 联机页面（pcl-next 自研重写版）。
    /// <para>
    /// 与上游旧联机页的区别：不再依赖「下载联机模块」加载器链路，也不再读
    /// <c>ModSecret.ServerConfig</c>；所有联机逻辑收敛到 <see cref="ModLinkSession"/>。
    /// 界面为「左侧导航 + 右侧面板」双栏布局，导航项与设置页左栏共用同款 MyListItem。
    /// </para>
    /// </summary>
    public partial class PageLinkMain
    {

        private static string _lastCode = "";

        private static readonly SolidColorBrush BrushOk = new SolidColorBrush(Color.FromRgb(0x35, 0xA1, 0x73));
        private static readonly SolidColorBrush BrushFail = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));
        private static readonly SolidColorBrush BrushBusy = new SolidColorBrush(Color.FromRgb(0x2E, 0x7C, 0xD7));
        private static readonly SolidColorBrush BrushIdle = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));

        public PageLinkMain()
        {
            InitializeComponent();
            this.ItemCreate.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.ItemJoin.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.ItemStatus.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.ItemAbout.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.BtnCreate.Click += (_, __) => CreateRoom();
            this.BtnJoin.Click += (_, __) => JoinFromInput();
            this.BtnLeave.Click += (_, __) => ModLinkSession.Leave();
            this.BtnCopyCode.Click += (_, __) => CopyText(ModLinkSession.InviteCode, "邀请码");
            this.BtnCopyDirect.Click += (_, __) => CopyText(ModLinkSession.HostDirectAddress, "直连地址");
            this.BtnClear.Click += (_, __) => this.TextJoinCode.Text = "";
            this.BtnPaste.Click += (_, __) => PasteCode();
            this.BtnResetPort.Click += (_, __) => this.TextHostPort.Text = "25565";
            this.LinkEasyTier.MouseLeftButtonUp += (_, __) => OpenLink("https://easytier.cn");
            this.LinkDocs.MouseLeftButtonUp += (_, __) => OpenLink("https://easytier.cn/guide/network/quick-networking.html");
            this.TextJoinCode.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    JoinFromInput();
            };
            this.TextHostPort.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    CreateRoom();
            };
            ModLinkSession.Changed += RefreshUi;
            this.Loaded += (_, __) => RefreshUi();
            this.Unloaded += (_, __) => ModLinkSession.Changed -= RefreshUi;
        }

        #region 对外入口

        /// <summary>弹出输入框让用户填邀请码并加入（供剪贴板 / 协议唤起调用）。</summary>
        public static void Join()
        {
            string code = ModMain.MyMsgBoxInput(
                "输入邀请码",
                "输入房主发给你的邀请码。",
                HintText: string.IsNullOrEmpty(_lastCode) ? "" : "使用上一次的邀请码");
            if (string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(_lastCode))
                code = _lastCode;
            if (code is null)
                return;
            Join(code);
        }

        /// <summary>直接用给定邀请码加入大厅。</summary>
        public static void Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;
            if (ModMain.FrmLinkMain is null)
            {
                ModMain.FrmMain?.PageChange((FormMain.PageStackData)FormMain.PageType.Link, FormMain.PageSubType.LinkMain);
                if (ModMain.FrmLinkMain is null)
                {
                    ModMain.Hint("请先打开联机页面", ModMain.HintType.Red);
                    return;
                }
            }
            ModMain.FrmLinkMain.JoinInternal(code);
        }

        /// <summary>
        /// 退出程序前处理联机。返回 true 表示不要关闭程序。
        /// </summary>
        public bool TryExit(bool force, bool askUser)
        {
            if (!ModLinkSession.IsActive)
                return false;
            if (!force && askUser)
            {
                if (ModMain.MyMsgBox("联机仍在进行中，是否退出房间并关闭启动器？", "退出联机", "退出房间", "取消") == 2)
                    return true;
            }
            ModLinkSession.Leave();
            return false;
        }

        #endregion

        #region 导航与面板切换

        /// <summary>面板切换动画的分组名，连续点击时用它终止上一轮动画。</summary>
        private const string PanelAniName = "PageLinkMain PanelChange";

        /// <summary>当前显示的面板序号（0 创建 / 1 加入 / 2 状态 / 3 关于）。</summary>
        private int _shownPanel = 0;

        // 防重入：SetChecked(true) 对已选中项也会无条件重发 Check 事件，
        // 若在 Check 回调里再次同步选中态会形成自激递归，导致界面卡死。
        private bool _updatingPanel;

        private void NavCheck(MyListItem sender)
        {
            if (_updatingPanel)
                return;
            if (sender.Tag is null)
                return;
            ShowPanel((int)ModBase.Val(sender.Tag));
        }

        /// <summary>按序号取左侧导航项。</summary>
        private MyListItem ItemAt(int index)
        {
            return index switch
            {
                0 => this.ItemCreate,
                1 => this.ItemJoin,
                2 => this.ItemStatus,
                3 => this.ItemAbout,
                _ => null
            };
        }

        /// <summary>按序号取右侧面板。</summary>
        private FrameworkElement PaneAt(int index)
        {
            return index switch
            {
                0 => this.PanCreate,
                1 => this.PanJoin,
                2 => this.PanStatus,
                3 => this.PanAbout,
                _ => null
            };
        }

        /// <summary>切换右侧面板：0 创建 / 1 加入 / 2 状态 / 3 关于。</summary>
        private void ShowPanel(int index)
        {
            FrameworkElement target = PaneAt(index);
            if (target is null)
                return;
            // 同步左侧导航选中态（RadioBox 联动会自动取消其余项）。
            // 仅对未选中项调用 SetChecked：已是选中态时重复 SetChecked(true)
            // 会无条件再次触发 Check 事件，经 NavCheck 回到本方法造成递归。
            if (!_updatingPanel)
            {
                MyListItem item = ItemAt(index);
                if (item is not null && !item.Checked)
                {
                    _updatingPanel = true;
                    try
                    {
                        item.SetChecked(true, false, false);
                    }
                    finally
                    {
                        _updatingPanel = false;
                    }
                }
            }
            if (index == _shownPanel && target.Visibility == Visibility.Visible)
                return;
            int oldIndex = _shownPanel;
            _shownPanel = index;
            // 未加载完成或用户关闭了动画时直接切换，避免动画起点取到空布局
            if (!this.IsLoaded || ModAnimation.AniControlEnabled != 0)
            {
                HardSwitch(index);
                return;
            }
            AniSwitch(oldIndex, index);
        }

        /// <summary>不做动画，直接切换。</summary>
        private void HardSwitch(int index)
        {
            ModAnimation.AniStop(PanelAniName);
            for (int i = 0; i < 4; i++)
            {
                FrameworkElement pane = PaneAt(i);
                if (pane is null)
                    continue;
                pane.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
                pane.Opacity = 1d;
                ResetAnimState(pane);
            }
        }

        /// <summary>
        /// 面板切换动画：旧面板淡出并上移，新面板错峰淡入回位，
        /// 与主页面切换（MyPageRight.TriggerEnterAnimation）保持一致的手感。
        /// </summary>
        private void AniSwitch(int oldIndex, int newIndex)
        {
            FrameworkElement newPane = PaneAt(newIndex);
            FrameworkElement oldPane = PaneAt(oldIndex);
            if (newPane is null)
                return;
            ModAnimation.AniStop(PanelAniName);
            // 除新旧两个面板外的其余面板直接收起，免得残留上一轮动画状态
            for (int i = 0; i < 4; i++)
            {
                FrameworkElement pane = PaneAt(i);
                if (pane is null || i == newIndex || i == oldIndex)
                    continue;
                pane.Visibility = Visibility.Collapsed;
                pane.Opacity = 1d;
                ResetAnimState(pane);
            }
            newPane.Visibility = Visibility.Visible;
            newPane.Opacity = 1d;
            var ani = new List<ModAnimation.AniData>();
            int delay = 0;
            // 旧面板退出
            if (oldPane is not null && oldPane != newPane && oldPane.Visibility == Visibility.Visible)
            {
                foreach (FrameworkElement control in this.GetAllAnimControls(oldPane))
                {
                    control.IsHitTestVisible = false;
                    ani.Add(ModAnimation.AaOpacity(control, -1d, 70, delay));
                    ani.Add(ModAnimation.AaTranslateY(control, -6d, 70, delay));
                    delay += 15;
                }
                FrameworkElement fading = oldPane;
                ani.Add(ModAnimation.AaCode(() =>
                {
                    fading.Visibility = Visibility.Collapsed;
                    ResetAnimState(fading);
                }, 70, true));
                delay = Math.Max(delay, 40);
            }
            // 新面板进入：先把控件归位到 0，再淡入 + 回弹（16 → 0）
            int enterDelay = delay;
            foreach (FrameworkElement control in this.GetAllAnimControls(newPane))
            {
                control.IsHitTestVisible = true;
                control.Opacity = 0d;
                control.RenderTransform = new TranslateTransform(0d, -16d);
                ani.Add(ModAnimation.AaOpacity(control, 1d, 100, enterDelay, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
                ani.Add(ModAnimation.AaTranslateY(control, 5d, 250, enterDelay, new ModAnimation.AniEaseOutFluent()));
                ani.Add(ModAnimation.AaTranslateY(control, 11d, 350, enterDelay, new ModAnimation.AniEaseOutBack()));
                enterDelay += 25;
            }
            ModAnimation.AniStart(ani, PanelAniName, true);
        }

        /// <summary>把面板内控件的透明度与位移复位，避免残留上一轮动画状态。</summary>
        private void ResetAnimState(FrameworkElement pane)
        {
            if (pane is null)
                return;
            foreach (FrameworkElement control in this.GetAllAnimControls(pane, true))
            {
                control.Opacity = 1d;
                control.IsHitTestVisible = true;
                if (control.RenderTransform is TranslateTransform)
                    control.RenderTransform = null;
            }
        }

        #endregion

        #region 交互

        private void CreateRoom()
        {
            if (ModLinkSession.IsActive)
            {
                ModMain.Hint("请先退出当前房间", ModMain.HintType.Blue);
                return;
            }
            if (!int.TryParse(this.TextHostPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
            {
                ModMain.Hint("服务器端口需要是 1-65535 之间的数字", ModMain.HintType.Red);
                return;
            }
            _ = ModLinkSession.CreateAsync((ushort)port);
        }

        private void JoinFromInput()
        {
            JoinInternal(this.TextJoinCode.Text);
        }

        internal void JoinInternal(string code)
        {
            if (ModLinkSession.IsActive)
            {
                ModMain.Hint("请先退出当前房间", ModMain.HintType.Blue);
                return;
            }
            string cleaned = ModLinkCodec.ExtractCode(code);
            try
            {
                ModLinkCodec.Parse(cleaned);
            }
            catch (ArgumentException ex)
            {
                ModMain.Hint(ex.Message, ModMain.HintType.Red);
                return;
            }
            _lastCode = cleaned;
            this.TextJoinCode.Text = cleaned;
            ShowPanel(1);
            _ = ModLinkSession.JoinAsync(cleaned, this.TextDirect?.Text);
        }

        private void PasteCode()
        {
            try
            {
                string clip = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clip))
                {
                    ModMain.Hint("剪贴板里没有文字", ModMain.HintType.Blue);
                    return;
                }
                this.TextJoinCode.Text = ModLinkCodec.ExtractCode(clip);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取剪贴板失败");
                ModMain.Hint("读取剪贴板失败", ModMain.HintType.Red);
            }
        }

        private void CopyText(string text, string what)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            try
            {
                Clipboard.SetText(text);
                ModMain.Hint(what + "已复制到剪贴板", ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "复制" + what + "失败");
                ModMain.Hint("复制失败，请手动选中后复制", ModMain.HintType.Red);
            }
        }

        private void OpenLink(string url)
        {
            try
            {
                ModBase.OpenWebsite(url);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "打开链接失败");
            }
        }

        #endregion

        #region 界面刷新

        private void RefreshUi()
        {
            if (this.LabStatus is null)
                return;
            ModLinkSession.Stage stage = ModLinkSession.CurrentStage;
            this.LabStatus.Text = ModLinkSession.StatusText;

            this.DotStatus.Fill = stage switch
            {
                ModLinkSession.Stage.Ready => BrushOk,
                ModLinkSession.Stage.Failed => BrushFail,
                ModLinkSession.Stage.Preparing => BrushBusy,
                ModLinkSession.Stage.Connecting => BrushBusy,
                _ => BrushIdle
            };

            bool active = ModLinkSession.IsActive;
            this.BtnCreate.IsEnabled = !active;
            this.BtnJoin.IsEnabled = !active;
            this.BtnLeave.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

            // 有房间时把面板切到对应的那一侧
            if (ModLinkSession.CurrentRole == ModLinkSession.Role.Host)
                ShowPanel(0);
            else if (ModLinkSession.CurrentRole == ModLinkSession.Role.Joiner)
                ShowPanel(1);

            bool hasCode = ModLinkSession.CurrentRole == ModLinkSession.Role.Host &&
                           stage == ModLinkSession.Stage.Ready &&
                           !string.IsNullOrEmpty(ModLinkSession.InviteCode);
            this.PanCode.Visibility = hasCode ? Visibility.Visible : Visibility.Collapsed;
            this.BtnCopyCode.Visibility = hasCode ? Visibility.Visible : Visibility.Collapsed;
            if (hasCode)
                this.TextCode.Text = ModLinkSession.InviteCode;

            bool hasDirect = hasCode && !string.IsNullOrEmpty(ModLinkSession.HostDirectAddress);
            this.PanDirect.Visibility = hasDirect ? Visibility.Visible : Visibility.Collapsed;
            if (hasDirect)
                this.TextDirectShow.Text = ModLinkSession.HostDirectAddress;

            bool joined = ModLinkSession.CurrentRole == ModLinkSession.Role.Joiner &&
                          stage == ModLinkSession.Stage.Ready;
            this.LabJoinTip.Visibility = joined ? Visibility.Visible : Visibility.Collapsed;
            if (joined)
                this.LabJoinTip.Text = $"在 Minecraft 里连接 127.0.0.1:{ModLinkSession.LocalJoinPort} 即可进入房主的服务器。";
        }

        #endregion

    }
}
