using System;
using System.Windows;
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
    /// 界面按「加入大厅 / 创建大厅」两段式排布，步骤写在卡片里。
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
            this.TabCreate.Click += (_, __) => SelectTab(true);
            this.TabJoin.Click += (_, __) => SelectTab(false);
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

        #region 交互

        /// <summary>切换「创建大厅 / 加入大厅」两个大选项卡。</summary>
        private void SelectTab(bool isCreate)
        {
            this.PanCreate.Visibility = isCreate ? Visibility.Visible : Visibility.Collapsed;
            this.PanJoin.Visibility = isCreate ? Visibility.Collapsed : Visibility.Visible;
            this.TabCreate.ColorType = isCreate ? MyButton.ColorState.Highlight : MyButton.ColorState.Normal;
            this.TabJoin.ColorType = isCreate ? MyButton.ColorState.Normal : MyButton.ColorState.Highlight;
        }

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
            SelectTab(false);
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

            // 有房间时把选项卡切到对应的那一侧
            if (ModLinkSession.CurrentRole == ModLinkSession.Role.Host)
                SelectTab(true);
            else if (ModLinkSession.CurrentRole == ModLinkSession.Role.Joiner)
                SelectTab(false);

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
