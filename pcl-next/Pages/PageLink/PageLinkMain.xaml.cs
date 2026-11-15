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

        /// <summary>上一次刷新时看到的联机会话角色，用于判断是否需要自动切面板。</summary>
        private ModLinkSession.Role _lastRole = ModLinkSession.Role.None;

        private static readonly SolidColorBrush BrushOk = new SolidColorBrush(Color.FromRgb(0x35, 0xA1, 0x73));
        private static readonly SolidColorBrush BrushFail = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));
        private static readonly SolidColorBrush BrushBusy = new SolidColorBrush(Color.FromRgb(0x2E, 0x7C, 0xD7));
        private static readonly SolidColorBrush BrushIdle = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));

        public PageLinkMain()
        {
            InitializeComponent();
            this.ItemCreate.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.ItemJoin.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.ItemRoom.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.ItemAbout.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.ItemSettings.Check += (p1, p2) => NavCheck((MyListItem)p1);
            this.BtnResetLink.Click += (_, __) => ResetLinkSettings();
            this.CheckLatency.Change += (_, __) => SaveLinkSettings();
            this.CheckPunch.Change += (_, __) => SaveLinkSettings();
            this.CheckIPv6.Change += (_, __) => SaveLinkSettings();
            this.CheckCliOutput.Change += (_, __) => SaveLinkSettings();
            this.ComboProtocol.SelectionChanged += (_, __) => SaveLinkSettings();
            this.ComboInterop.SelectionChanged += (_, __) => SaveLinkSettings();
            this.TextNick.LostFocus += (_, __) => SaveLinkSettings();
            this.TextCustomPeer.LostFocus += (_, __) => SaveLinkSettings();
            this.BtnCreate.Click += (_, __) => CreateRoom();
            this.BtnJoin.Click += (_, __) => JoinFromInput();
            this.BtnLeave.Click += (_, __) => ModLinkSession.Leave();
            this.BtnCopyCode.Click += (_, __) => CopyText(ModLinkSession.InviteCode, "邀请码");
            this.BtnCopyRoomCode.Click += (_, __) => CopyText(ModLinkSession.InviteCode, "邀请码");
            this.BtnCopyHostId.Click += (_, __) => CopyText(ModLinkSession.HostMachineId, "房主 ID");
            this.BtnCopyRoomAddress.Click += (_, __) =>
                CopyText(ModLinkSession.LocalJoinPort > 0 ? "127.0.0.1:" + ModLinkSession.LocalJoinPort : "", "游戏内地址");
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
            this.Loaded += (_, __) =>
            {
                LoadLinkSettings();
                RefreshUi();
            };
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

        /// <summary>面板总数（0 创建 / 1 加入 / 2 当前房间 / 3 关于 / 4 设置）。</summary>
        private const int PanelCount = 5;

        /// <summary>当前显示的面板序号。</summary>
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
                2 => this.ItemRoom,
                3 => this.ItemAbout,
                4 => this.ItemSettings,
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
                2 => this.PanRoom,
                3 => this.PanAbout,
                4 => this.PanSettings,
                _ => null
            };
        }

        /// <summary>
        /// 进房 / 退房时调整左侧导航：房间里把「创建」「加入」收起，换成「当前房间」，
        /// 避免高亮还停在创建卡上（本轮反馈的"导航栏不对"）。
        /// </summary>
        private void UpdateNavForRoom(bool inRoom)
        {
            Visibility entryVisibility = inRoom ? Visibility.Collapsed : Visibility.Visible;
            this.ItemCreate.Visibility = entryVisibility;
            this.ItemJoin.Visibility = entryVisibility;
            this.ItemRoom.Visibility = inRoom ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>切换右侧面板：0 创建 / 1 加入 / 2 当前房间 / 3 关于 / 4 设置。</summary>
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
            for (int i = 0; i < PanelCount; i++)
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
            for (int i = 0; i < PanelCount; i++)
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

        #region 联机设置

        // 加载设置时置位，避免把「读取」误当成用户改动又写回去
        private bool _loadingSettings;

        /// <summary>把 Settings 里的联机设置值填进各控件。</summary>
        private void LoadLinkSettings()
        {
            _loadingSettings = true;
            try
            {
                this.TextNick.Text = Settings.Get<string>("LinkUsername");
                this.ComboInterop.SelectedIndex = Math.Clamp(Settings.Get<int>("LinkInteropTarget"), 0, 2);
                this.ComboProtocol.SelectedIndex = Settings.Get<int>("LinkProtocolPreference") >= 1 ? 1 : 0;
                this.CheckLatency.Checked = Settings.Get<int>("LinkLatencyMode") == 1;
                this.CheckPunch.Checked = Settings.Get<bool>("LinkTryPunchSym");
                this.CheckIPv6.Checked = Settings.Get<bool>("LinkEnableIPv6");
                this.CheckCliOutput.Checked = Settings.Get<bool>("LinkEnableCliOutput");
                this.TextCustomPeer.Text = Settings.Get<string>("LinkCustomPeer");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取联机设置失败");
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        /// <summary>把控件当前值写回 Settings（下一次建连时生效）。</summary>
        private void SaveLinkSettings()
        {
            if (_loadingSettings || !this.IsLoaded)
                return;
            try
            {
                if (Settings.Get<string>("LinkUsername") != this.TextNick.Text.Trim())
                    Settings.Set("LinkUsername", this.TextNick.Text.Trim());
                int protocol = this.ComboProtocol.SelectedIndex >= 1 ? 1 : 0;
                if (Settings.Get<int>("LinkProtocolPreference") != protocol)
                    Settings.Set("LinkProtocolPreference", protocol);
                int interop = this.ComboInterop.SelectedIndex < 0 ? 0 : this.ComboInterop.SelectedIndex;
                if (Settings.Get<int>("LinkInteropTarget") != interop)
                    Settings.Set("LinkInteropTarget", interop);
                int latency = this.CheckLatency.Checked ? 1 : 0;
                if (Settings.Get<int>("LinkLatencyMode") != latency)
                    Settings.Set("LinkLatencyMode", latency);
                if (Settings.Get<bool>("LinkTryPunchSym") != this.CheckPunch.Checked)
                    Settings.Set("LinkTryPunchSym", this.CheckPunch.Checked);
                if (Settings.Get<bool>("LinkEnableIPv6") != this.CheckIPv6.Checked)
                    Settings.Set("LinkEnableIPv6", this.CheckIPv6.Checked);
                if (Settings.Get<bool>("LinkEnableCliOutput") != this.CheckCliOutput.Checked)
                    Settings.Set("LinkEnableCliOutput", this.CheckCliOutput.Checked);
                if (Settings.Get<string>("LinkCustomPeer") != this.TextCustomPeer.Text.Trim())
                    Settings.Set("LinkCustomPeer", this.TextCustomPeer.Text.Trim());
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "保存联机设置失败");
            }
        }

        /// <summary>把联机设置恢复成默认值，并刷新控件显示。</summary>
        private void ResetLinkSettings()
        {
            foreach (string key in new[]
                     {
                         "LinkUsername", "LinkProtocolPreference", "LinkLatencyMode",
                         "LinkTryPunchSym", "LinkEnableIPv6", "LinkEnableCliOutput", "LinkCustomPeer",
                         "LinkInteropTarget"
                     })
            {
                try
                {
                    Settings.Reset(key);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "重置联机设置失败（" + key + "）");
                }
            }
            LoadLinkSettings();
            ModMain.Hint("联机设置已恢复默认", ModMain.HintType.Green);
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
            // 四类码都要能进（pcl-next 短码 / PCL2-CE 的 U/ 码 / 陶瓦码 / PCL2-CE 旧数字码）
            string cleaned = ModLinkInterop.ExtractCode(code);
            try
            {
                ModLinkInterop.Parse(cleaned);
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
                this.TextJoinCode.Text = ModLinkInterop.ExtractCode(clip);
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
            ModLinkSession.Role role = ModLinkSession.CurrentRole;
            bool active = ModLinkSession.IsActive;
            bool inRoom = role != ModLinkSession.Role.None;
            this.LabStatus.Text = ModLinkSession.StatusText;

            this.DotStatus.Fill = stage switch
            {
                ModLinkSession.Stage.Ready => BrushOk,
                ModLinkSession.Stage.Failed => BrushFail,
                ModLinkSession.Stage.Preparing => BrushBusy,
                ModLinkSession.Stage.Connecting => BrushBusy,
                _ => BrushIdle
            };

            this.BtnCreate.IsEnabled = !active;
            this.BtnJoin.IsEnabled = !active;
            this.BtnLeave.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

            // 进房 / 退房时换导航：房间里收起「创建 / 加入」，只留「当前房间」，
            // 面板也切到房间卡片；退房后恢复。只在角色变化时切面板，
            // 否则用户停在「关于联机 / 联机设置」时会被每次状态刷新拽回房间。
            UpdateNavForRoom(inRoom);
            if (role != _lastRole)
            {
                _lastRole = role;
                ShowPanel(inRoom ? 2 : 0);
            }

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

            if (inRoom)
                RefreshRoomCard(stage, role);
        }

        /// <summary>填充房间卡片：房主、房主 ID、延迟、房间号、邀请码、游戏内地址与成员列表。</summary>
        private void RefreshRoomCard(ModLinkSession.Stage stage, ModLinkSession.Role role)
        {
            bool ready = stage == ModLinkSession.Stage.Ready;
            string hostName = ModLinkSession.HostDisplayName;
            string hostId = ModLinkSession.HostMachineId;
            int ping = ModLinkSession.HostPingMs;

            this.LabRoomHost.Text = string.IsNullOrEmpty(hostName) ? "—" : hostName;
            this.TextRoomHostId.Text = hostId;
            this.BtnCopyHostId.IsEnabled = !string.IsNullOrEmpty(hostId);
            this.LabRoomRole.Text = ModLinkSession.IsHost ? "本机是房主" : "本机是成员";
            this.LabRoomPing.Text = ping >= 0 ? ping + " ms" : "—";
            this.LabRoomNumber.Text = string.IsNullOrEmpty(ModLinkSession.RoomNumber) ? "—" : ModLinkSession.RoomNumber;
            this.TextRoomCode.Text = string.IsNullOrEmpty(ModLinkSession.InviteCode) ? "（尚未生成）" : ModLinkSession.InviteCode;
            this.BtnCopyRoomCode.IsEnabled = !string.IsNullOrEmpty(ModLinkSession.InviteCode);

            bool showAddress = role == ModLinkSession.Role.Joiner && ready && ModLinkSession.LocalJoinPort > 0;
            this.PanRoomAddress.Visibility = showAddress ? Visibility.Visible : Visibility.Collapsed;
            if (showAddress)
                this.TextRoomAddress.Text = "127.0.0.1:" + ModLinkSession.LocalJoinPort;

            RefreshRoomPlayers();
        }

        /// <summary>重建成员列表（每 5 秒随会话刷新一次，数量很少，直接整体重建最省事）。</summary>
        private void RefreshRoomPlayers()
        {
            bool isHost = ModLinkSession.IsHost;
            List<RoomPlayerRow> rows = new List<RoomPlayerRow>();
            foreach (ModLinkScaffold.PlayerInfo player in ModLinkSession.Players)
            {
                string name = string.IsNullOrWhiteSpace(player.Name) ? player.MachineId : player.Name;
                if (string.IsNullOrWhiteSpace(name))
                    name = "未知玩家";
                string sub = player.IsHost ? "房主" : "成员";
                if (!string.IsNullOrEmpty(player.MachineId))
                    sub += " · " + player.MachineId;
                rows.Add(new RoomPlayerRow
                {
                    Name = name,
                    Sub = sub,
                    Detail = sub + (string.IsNullOrEmpty(player.Vendor) ? "" : "\n" + player.Vendor),
                    PingText = player.PingMs >= 0 ? player.PingMs + " ms" : "—",
                    MachineId = player.MachineId,
                    KickVisibility = isHost && !player.IsHost && !string.IsNullOrEmpty(player.MachineId)
                        ? Visibility.Visible
                        : Visibility.Collapsed
                });
            }
            this.ListPlayers.ItemsSource = rows;
            this.LabPlayersEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>成员行上的踢人按钮：房主把该成员移出房间。</summary>
        private void BtnKick_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            string machineId = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(machineId))
                return;
            string name = machineId;
            foreach (ModLinkScaffold.PlayerInfo player in ModLinkSession.Players)
            {
                if (player.MachineId == machineId)
                {
                    name = string.IsNullOrWhiteSpace(player.Name) ? machineId : player.Name;
                    break;
                }
            }
            if (ModLinkSession.KickPlayer(machineId))
            {
                ModMain.Hint("已把 " + name + " 移出房间", ModMain.HintType.Green);
                RefreshRoomPlayers();
            }
            else
            {
                ModMain.Hint("没能移出该成员（对方可能已经离开）", ModMain.HintType.Red);
            }
        }

        #endregion

    }

    /// <summary>
    /// 房间成员列表的一行（DataTemplate 直接绑这个类）。
    /// </summary>
    public sealed class RoomPlayerRow
    {
        public string Name { get; init; } = "";
        public string Sub { get; init; } = "";
        public string Detail { get; init; } = "";
        public string PingText { get; init; } = "—";
        public string MachineId { get; init; } = "";
        public Visibility KickVisibility { get; init; } = Visibility.Collapsed;
    }
}
