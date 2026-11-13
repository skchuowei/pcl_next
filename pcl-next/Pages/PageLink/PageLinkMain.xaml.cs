using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    public partial class PageLinkMain
    {
        private const int INVITE_CODE_VERSION = 2;

        // ===============================
        // 状态机与前端页面
        // ===============================

        #region 状态管理

        public enum LinkStates
        {
            Waiting,
            Loading,
            Failed,
            Finished
        }
        public static LinkStates LinkState = LinkStates.Waiting;

        public PageLinkMain()
        {
            InitializeComponent();
            this.PanSelectCreate.MouseLeftButtonUp += (p1, p2) => Create_MouseLeftButtonUp();
            this.PanSelectJoin.Loaded += (p1, p2) => PanSelectJoin_Loaded();
            // VB 的 Handles PanSelectJoin.MouseLeftButtonUp 迁移时被丢弃 → 联机页「加入房间」点了没反应（2026-11-11 补）。
            this.PanSelectJoin.MouseLeftButtonUp += (p1, p2) => Join();
            this.BtnLoadCancel.Click += (p1, p2) => BtnLoadCancel_Click();
            this.CardLoad.MouseLeftButtonUp += (p1, p2) => CardLoad_MouseLeftButtonUp();
            this.CardLoad.SizeChanged += (p1, p2) => CardLoad_SizeChanged();
            this.BtnFinishExit.Click += (p1, p2) => BtnFinishExit_Click();
            this.BtnFinishCopy.Click += (p1, p2) => Copy();
            this.BtnFinishIp.MouseLeftButtonUp += (p1, p2) => BtnFinishIp_MouseLeftButtonUp((object)p1, (MouseButtonEventArgs)p2);
            // 原文是 LoaderCombo(Of Integer)("联机", { ... }) 的花括号数组，元素都是 LoaderBase；
            // 转换器误写成 object[]，这里改回 ModLoader.LoaderBase[] 以匹配构造函数的 IEnumerable<LoaderBase> 形参
            LinkLoader = new ModLoader.LoaderCombo<int>("联机", new ModLoader.LoaderBase[]{ new ModLoader.LoaderTask<int, int>("获取配置", InitConfig) { ProgressWeight = 8d }, new ModLoader.LoaderTask<int, List<ModNet.NetFile>>("准备下载联机模块", InitPrepareDownload) { ProgressWeight = 2d }, new ModNet.LoaderDownload("下载联机模块", new List<ModNet.NetFile>()) { ProgressWeight = 40d }, new ModLoader.LoaderTask<int, int>("启动联机模块", InitLaunch) { ProgressWeight = 50d } });
            RefreshPeerLoader = new ModLoader.LoaderTask<int, List<Peer>>("EasyTier CLI", (_) => RefreshPeer());
            this.PageEnter += UpdatePanelVisibility;
            this.Loaded += (_, __) => StartTimerThread();
            this.Loaded += (_, __) => Update();
            // Initialized 在 InitializeComponent() 内部就已触发，事后订阅收不到，PageLoaderInit 不会被执行；
            // 这里放到构造末尾显式调用（此时 LinkLoader 等字段已赋值，2026-11-11 修复）。
            LoaderInit();
        }

        /// <summary>
    /// 切换到指定的状态。
    /// </summary>
        private void ChangeState(LinkStates NewState)
        {
            if (LinkState == NewState)
                return;
            var OldState = LinkState;
            LinkState = NewState;
            Logger.Info($"主状态由 {OldState} 变更为 {NewState}");
            // 触发状态切换
            ModBase.RunInUi(() => { switch (NewState) { case LinkStates.Waiting: { SwitchToWaiting(OldState); break; } case LinkStates.Loading: { SwitchToLoading(OldState); break; } case LinkStates.Failed: { SwitchToFailed(OldState); break; } case LinkStates.Finished: { SwitchToFinished(OldState); break; } } });
        }

        // 让加载器的 成功/失败 事件触发状态切换
        private void LoaderInit()
        {
            this.PageLoaderInit(this.Load, this.PanLoad, this.PanContent, this.PanAlways, LinkLoader, AutoRun: false);
            LinkLoader.OnStateChangedUi += (Loader, NewState, OldState) => { switch (NewState) { case ModBase.LoadState.Finished: { ChangeState(LinkStates.Finished); break; } case ModBase.LoadState.Failed: { ChangeState(LinkStates.Failed); break; } } };
        }

        // 页面动画兼容
        private void UpdatePanelVisibility()
        {
            ModMain.FrmLinkMain.PanSelect.Visibility = LinkState == LinkStates.Waiting ? Visibility.Visible : Visibility.Collapsed;
            ModMain.FrmLinkMain.PanFinish.Visibility = LinkState == LinkStates.Finished ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region 初始页面 (Waiting)

        // 由任意状态切换到 Waiting
        private void SwitchToWaiting(LinkStates OldState)
        {
            // 加载器与进程状态
            LinkLoader.Cancel();
            lock (LinkLoader.LockState)
                LinkLoader.State = ModBase.LoadState.Waiting;
            ProcessStop();
            // 页面切换
            if (OldState != LinkStates.Finished)
            {
                // 从完成页面退出时，PageOnContentExit 结束会触发 PageEnter 事件，这会调用 UpdatePanelVisibility
                // 如果在现在提前调用了，会丢失完成页面的退出动画
                UpdatePanelVisibility();
            }
            this.PageOnContentExit();
        }

        // ===============================
        // 页面 UI
        // ===============================

        // 创建
        private void Create_MouseLeftButtonUp()
        {
            // 输入端口号
            string Port = ModMain.MyMsgBoxInput("输入端口", $"在单人游戏的暂停菜单选择 {ModBase.vbLQ}对局域网开放{ModBase.vbRQ}，然后输入端口数字。{"\r\n"}甚至可以输入其他游戏的端口……嗯……", ValidateRules: new Collection<ValidateType>() { new ValidateInteger(1024, 65535) }, HintText: "端口号");
            if (Port is null)
                return;
            // 基础信息
            IsServerSide = true;
            ServerPort = Conversions.ToInteger(Port);
            string GenerateRandomCode()
            {
                string Result = "";
                for (int i = 1; i <= 5; i++)
                    Result += ModBase.RandomInteger(0, 35).ConvertRadix(36);
                return Result.Replace("O", "0").Replace("I", "1");
            };
            NetworkName = $"P{ServerPort.ConvertRadix(16).PadLeft(4, '0')}-{GenerateRandomCode()}";
            NetworkSecret = GenerateRandomCode();
            DiscoverNodeID = -1;
            Logger.Info($"尝试创建房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}");
            // 启动
            ChangeState(LinkStates.Loading);
        }

        // 加入
        private static string LastCode = null;
        /// <summary>
    /// 输入邀请码，切换到联机页并立即加入房间。
    /// </summary>
        public static void Join()
        {
            string Code = ModMain.MyMsgBoxInput("输入邀请码", "输入房主发给你的邀请码。", HintText: string.IsNullOrEmpty(LastCode) ? "" : "使用上一次的邀请码");
            if (!string.IsNullOrEmpty(LastCode) && Code is not null && string.IsNullOrEmpty(Code))
                Code = LastCode;
            if (Code is null)
                return;
            Join(Code);
        }
        public void JoinInternal(string Code)
        {
            if (Code is null)
                return;
            // 基础格式校验
            string ValidateResult = ValidateCodeFormat(Code);
            if (ValidateResult is not null)
            {
                ModMain.Hint(ValidateResult, ModMain.HintType.Red);
                return;
            }
            Code = FixCodeFormat(Code);
            Logger.Info($"实际使用的邀请码：{Code}");
            // 基础信息
            IsServerSide = false;
            ServerPort = Conversions.ToInteger(Code.Substring(1, 4).Upper().ConvertRadix(16, 10));
            NetworkName = Code.Substring(0, 11);
            NetworkSecret = Code.Substring(12, 5);
            if (Code.Substring(20, 3) == "000")
            {
                DiscoverNodeID = -2;
            }
            else
            {
                DiscoverNodeID = Conversions.ToInteger(Code.Substring(20, 3).Upper().ConvertRadix(16, 10));
            }
            Logger.Info($"尝试加入房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}，发现节点 {DiscoverNodeID}");
            // 启动
            LastCode = Code;
            ChangeState(LinkStates.Loading);
        }
        public static string ValidateCodeFormat(string Code)
        {
            if (Code is null)
                return "邀请码为空！";
            Code = FixCodeFormat(Code);
            // 判断类型
            if (!(Code.Length >= 14 && Code[0] == 'P' && Code[5] == '-' && Code[11] == '-'))
            {
                if (Code.StartsWithF("U/")) // HMCL
                {
                    return "请让房主使用 PCL Next 创建房间！";
                }
                else if (Code.Length == 10) // PCL CE
                {
                    return "请让房主使用非社区版的 PCL Next 创建房间！";
                }
                else
                {
                    return "邀请码有误，请让房主使用 PCL Next 创建房间！";
                }
            }
            // 校验版本
            if (Code.Length >= 23 && Code[17] == '-' && ModBase.Val(Code.Substring(18, 2)) > INVITE_CODE_VERSION)
                return "你的 PCL Next 版本太老了，请在更新 PCL Next 之后再联机！";
            return null;
        }
        private static string FixCodeFormat(string Code)
        {
            Code = Code.Between("【", "】").Between("[", "]"); // 从完整消息中提取
            Code = Code.Upper().Replace("O", "0").Replace("I", "1"); // 输入修正
                                                                     // 版本 1 兼容
            if (Code.Length >= 17 && (Code.Length < 23 || Code.Length >= 18 && Code[17] != '-'))
            {
                Code = Code.Substring(0, 17) + "-0105E";
            }
            return Code;
        }

        // 自动加入

        /// <summary>
    /// 切换到联机页并立即加入指定房间。
    /// </summary>
        public static void Join(string Code)
        {
            if (LinkState != LinkStates.Waiting)
            {
                ModMain.Hint("你已经在联机房间中了！", ModMain.HintType.Red);
            }
            else if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.Link)
            {
                ModMain.FrmLinkMain.JoinInternal(Code);
            }
            else
            {
                AutoJoinCode = Code;
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Link, FormMain.PageSubType.LinkMain);
            }
        }
        // 在进入页面时自动尝试加入房间
        private static string AutoJoinCode = null;
        private void PanSelectJoin_Loaded()
        {
            if (AutoJoinCode is null)
                return;
            if (LinkState == LinkStates.Waiting)
                JoinInternal(AutoJoinCode);
            AutoJoinCode = null;
        }

        #endregion

        #region 加载页面 (Loading | Failed)

        // 由 Waiting 或 Failed 状态切换到 Loading
        private void SwitchToLoading(LinkStates OldState)
        {
            // 清理变量
            Peers = null;
            FailCount = 0;
            // 加载器与进程状态
            LinkLoader.Start(IsForceRestart: true);
            // 页面切换会由 Loader 调用 MyPageRight 来触发
            // UI 更新
            UpdateProgressBar(0d);
            this.LabLoadTitle.Text = IsServerSide ? "创建房间中" : "加入房间中";
            UpdateLoadingPage("正在初始化……", "准备初始化");
        }

        // 由 Loading 状态切换到 Failed
        private void SwitchToFailed(LinkStates OldState)
        {
            // 加载器与进程状态
            LinkLoader.Cancel();
            ProcessStop();
            // UI 更新
            UpdateProgressBar(1d);
            // 显示错误信息
            this.LabLoadTitle.Text = FailReason;
            string Brief = LinkLoader.Error.GetDisplay(false);
            var InnerEx = LinkLoader.Error;
            if (InnerEx.Message.StartsWithF("$"))
                Brief = InnerEx.Message;
            while (!(InnerEx.InnerException is null))
            {
                InnerEx = InnerEx.InnerException;
                if (InnerEx.Message.StartsWithF("$"))
                    Brief = InnerEx.Message;
            }
            this.LabLoadDesc.Text = Brief.StartsWithF("$") ? Brief.TrimStart('$') : LinkLoader.Error.GetDisplay(true);
            Logger.Warn(LinkLoader.Error, this.LabLoadTitle.Text);
        }

        // ===============================
        // 页面 UI
        // ===============================

        private void UpdateLoadingPage(string Title, string FailBrief)
        {
            if ((FailReason ?? "") == (FailBrief + "失败" ?? ""))
                return;
            FailReason = FailBrief + "失败";
            Logger.Info($"开始步骤：{Title}");
            ModBase.RunInUiWait(() =>
        {
            if (ModMain.FrmLinkMain is null || !ModMain.FrmLinkMain.LabLoadDesc.IsLoaded)
                return;
            ModMain.FrmLinkMain.LabLoadDesc.Text = Title;
        });
        }
        private string FailReason = "准备初始化";

        // 取消加载
        private void BtnLoadCancel_Click()
        {
            ChangeState(LinkStates.Waiting);
        }

        // 点击重试
        private void CardLoad_MouseLeftButtonUp()
        {
            ChangeState(LinkStates.Loading);
        }

        // 进度条
        private void UpdateProgressBar(double Value = -1)
        {
            if (Value == -1)
                Value = LinkLoader.Progress;
            double DisplayingProgress = this.ColumnProgressA.Width.Value;
            if (Math.Round(Value - DisplayingProgress, 3) == 0d)
                return;
            if (DisplayingProgress <= Value)
            {
                double NewProgress = Value == 1d ? 1d : DisplayingProgress + Math.Pow(Value - DisplayingProgress, 2d) * 0.5d;
                ModAnimation.AniStart(new[] { ModAnimation.AaGridLengthWidth(this.ColumnProgressA, NewProgress - this.ColumnProgressA.Width.Value, 200), ModAnimation.AaGridLengthWidth(this.ColumnProgressB, 1d - NewProgress - this.ColumnProgressB.Width.Value, 200) }, "Link Progress");
            }
            else
            {
                this.ColumnProgressA.Width = new GridLength(Value, GridUnitType.Star);
                this.ColumnProgressB.Width = new GridLength(1d - Value, GridUnitType.Star);
                ModAnimation.AniStop("Link Progress");
            }
        }
        private void CardLoad_SizeChanged()
        {
            this.RectProgressClip.Rect = new Rect(0d, 0d, this.CardLoad.ActualWidth, 12d);
        }

        #endregion

        #region 完成页面 (Finished)

        // 由 Loading 状态切换到 Finished
        private void SwitchToFinished(LinkStates OldState)
        {
            // 由于只可能从加载器的完成事件触发，不需要管加载器
            UpdatePanelVisibility(); // 页面的实际切换会由 Loader 调用 MyPageRight 来触发

            // UI 更新
            if (IsServerSide)
            {
                this.LabFinishTitle.Text = "已创建房间";
                this.LabFinishDesc.Text = $"把邀请码发给朋友，让大家加入房间吧！{"\r\n"}邀请码：{GetInviteCode()}";
                this.BtnFinishExit.Text = "关闭";
                this.BtnFinishPing.ToolTip = "网络延迟";
                this.BtnFinishCopy.Visibility = Visibility.Visible;
                Copy(); // 立即复制邀请码
                        // 下边栏
                this.BtnFinishPort.Visibility = Visibility.Visible;
                this.LabFinishPort.Visibility = Visibility.Visible;
                this.BtnFinishIp.Visibility = Visibility.Collapsed;
                this.LabFinishIp.Visibility = Visibility.Collapsed;
                this.LabFinishPort.Text = ServerPort.ToString();
            }
            else
            {
                this.LabFinishTitle.Text = "已加入房间";
                this.LabFinishDesc.Text = $"在多人游戏页面的最下方就能找到联机房间！{"\r\n"}注意：使用离线登录时不要手动输入 IP！";
                this.BtnFinishExit.Text = "离开";
                this.BtnFinishPing.ToolTip = "与房主的延迟";
                this.BtnFinishCopy.Visibility = Visibility.Collapsed;
                // 下边栏
                this.BtnFinishPort.Visibility = Visibility.Collapsed;
                this.LabFinishPort.Visibility = Visibility.Collapsed;
                this.LabFinishIp.Text = ClientAddress;
                this.BtnFinishIp.Visibility = Visibility.Visible;
                this.LabFinishIp.Visibility = Visibility.Visible;
            }
            Update(); // 立即刷新
        }

        // ===============================
        // 页面 UI
        // ===============================

        // 退出
        private void BtnFinishExit_Click()
        {
            TryExit(false, false);
        }
        /// <summary>
    /// 退出联机。
    /// 返回是否弹出了警告窗口并且玩家选择了取消。
    /// </summary>
        public bool TryExit(bool Silent, bool Closing)
        {
            if (LinkState == LinkStates.Waiting || LinkState == LinkStates.Failed)
                return false;
            if (!Silent)
            {
                if (IsServerSide && PeopleCount > 1)
                {
                    if (ModMain.MyMsgBox("你确定要关闭联机房间吗？" + "\r\n" + "所有玩家都需要重新输入邀请码才可加入游戏！", "退出联机", "确定", "取消", IsWarn: true) == 2)
                        return true;
                }
                else if (Closing)
                {
                    if (ModMain.MyMsgBox(IsServerSide ? "你确定要关闭联机房间吗？" : "你确定要离开联机房间吗？", "退出联机", "确定", "取消") == 2)
                        return true;
                }
            }
            ChangeState(LinkStates.Waiting);
            return false;
        }

        // 复制邀请码
        private void Copy()
        {
            string CodeText = $"在 PCL Next 启动器中输入邀请码【{GetInviteCode()}】，即可加入联机房间！";
            ModBase.ClipboardSet(CodeText, false);
            Settings.Set("LinkLastAutoJoinInviteCode", CodeText);
            ModMain.Hint("已复制邀请码！", ModMain.HintType.Green);
        }
        private string GetInviteCode()
        {
            return $"{NetworkName}-{NetworkSecret}-{INVITE_CODE_VERSION.ToString().PadLeft(2, '0')}{(DiscoverNodeID == -1 ? 0 : DiscoverNodeID).ConvertRadix(16).PadLeft(3, '0')}";
        }

        // 复制 IP
        private void BtnFinishIp_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ModBase.ClipboardSet(ClientAddress, false);
            ModMain.Hint("已复制服务器地址！", ModMain.HintType.Green);
        }

        #endregion

        // ===============================
        // 后端
        // ===============================

        /// <summary>
    /// EasyTier 所在的文件夹路径，以 \ 结尾。
    /// </summary>
        private string PathEasyTier = Paths.AppDataThenName + @"EasyTier\";
        /// <summary>
    /// 当前是服务端还是客户端。
    /// </summary>
        private bool IsServerSide;
        /// <summary>
    /// 服务端映射前的端口号。
    /// </summary>
        private int ServerPort;
        /// <summary>
    /// 客户端映射后的端口号。
    /// </summary>
        private int ClientPort;
        /// <summary>
    /// RPC 端口号。
    /// </summary>
        private int RPCPort;
        /// <summary>
    /// 在客户端映射后的地址。
    /// 除非已作为客户端启动 EasyTier，否则一直为 Nothing。
    /// </summary>
        private string ClientAddress = null;
        /// <summary>
    /// 发现节点的 ID。
    /// 若必须设定自定义节点则为 -2；若等待选择则为 -1；选择回退节点则为 0；否则为对应节点的 ID。
    /// </summary>
        private int DiscoverNodeID = -1;
        /// <summary>
    /// 网络信息。
    /// </summary>
        private string NetworkName;
        private string NetworkSecret;

        #region 加载

        private ModLoader.LoaderCombo<int> LinkLoader;

        // 1. 获取服务器配置
        private void InitConfig(ModLoader.LoaderTask<int, int> Task)
        {
            UpdateLoadingPage("正在联网获取配置……", "联网获取配置");
            // ModSecret.VersionBranchMain 是编译期常量（当前为 OpenSource），C# 会把 throw 之后的语句判为不可达（CS0162）；
            // 该判断在官方版构建下不成立，故经局部变量取值以避免常量折叠误判，行为不变。
            string versionBranchMain = ModSecret.VersionBranchMain;
            if (versionBranchMain != "Official")
            {
                throw new Exception($"$开源版无法联网获取配置。{"\r\n"}你可以在 PCL 官方版的缓存文件夹下查看 ServerConfig 的当前内容， 并在代码中进行相应修改。");
            }
            ModSecret.ServerLoader.WaitForExit(LoaderToSyncProgress: Task);
            if (ModSecret.ServerConfig is null)
                throw new Exception("无法从服务器获取配置");
            // 检查是否已禁用联机功能
            string DisableReason = ModSecret.ServerConfig["Link"]?["DisableReason2"]?.ToString();
            if (!string.IsNullOrEmpty(DisableReason))
                throw new Exception("$" + DisableReason); // TODO: 可能不会显示错误原因？
            if (((JObject)ModSecret.ServerConfig["Link"]).ContainsKey("MinVersionCode") && ModBase.VersionCode < ModSecret.ServerConfig["Link"]["MinVersionCode"].ToObject<int>())
            {
                throw new Exception("$你的 PCL Next 版本太老了，请在更新 PCL Next 之后再联机！");
            }
        }

        // 2. 获取需要下载的文件
        private int ServerVersion;
        private void InitPrepareDownload(ModLoader.LoaderTask<int, List<ModNet.NetFile>> Task)
        {
            // 获取 CPU 架构
            UpdateLoadingPage("正在获取 CPU 架构……", "获取 CPU 架构");
            string Architecture = ((int)typeof(string).Assembly.GetName().ProcessorArchitecture).ToString();
            switch (Architecture ?? "")
            {
                case "2":
                    {
                        Architecture = "i686";
                        break;
                    }

                case "4":
                    {
                        Architecture = "x86_64";
                        break;
                    }

                case "5":
                    {
                        Architecture = "arm64";
                        break;
                    }

                default:
                    {
                        Logger.Warn($"CPU 是不支持的 {Architecture} 架构，这可能会导致联机模块无法启动！");
                        Architecture = "arm64";
                        break;
                    }
            }
            Logger.Info($"CPU 架构：{Architecture}");
            // 检查 EasyTier 版本
            UpdateLoadingPage("正在检查联机模块版本……", "检查联机模块版本");
            if (!(FileUtils.Exists(PathEasyTier + "联机模块 CLI.exe") && FileUtils.Exists(PathEasyTier + "联机模块.exe") && FileUtils.Exists(PathEasyTier + "Packet.dll")))
            {
                Settings.Set("LinkEasyTierVersion", -1);
            }
            int LocalVersion = Settings.Get<int>("LinkEasyTierVersion");
            ServerVersion = (int)ModSecret.ServerConfig["Link"]["EasyTierVersion"];
            var RequiredFiles = new List<ModNet.NetFile>();
            Logger.Info($"EasyTier 本地版本：{LocalVersion}，需求版本：{ServerVersion}");
            if (LocalVersion < ServerVersion)
            {
                RequiredFiles.Add(new ModNet.NetFile(ModSecret.ServerConfig["Link"]["Downloads"].Select(UrlEntry => UrlEntry.ToString().Replace("{arch}", Architecture)), PathEasyTier + "EasyTier.zip", new FileChecker() { MinSize = 1024 * 1024 * 2 }));
            }
            // 开始下载
            UpdateLoadingPage("正在下载联机模块……", "下载联机模块");
            Task.Output = RequiredFiles;
        }

        // 3. 启动联机模块
        private void InitLaunch(ModLoader.LoaderTask<int, int> Task)
        {
            // 解压文件
            UpdateLoadingPage("正在解压联机模块……", "解压联机模块");
            if (FileUtils.Exists(PathEasyTier + "EasyTier.zip"))
            {
                // 解压
                string ExtractPath = ModMain.RequestTaskTempFolder();
                // FileUtils.ExtractToDirectory(PathEasyTier & "EasyTier.zip", ExtractPath,
                // progressHandler:=Sub(Percentage) Task.Progress = Percentage * 0.05)
                DirectoryUtils.Copy(DirectoryUtils.EnumerateDirectories(ExtractPath, true).First(), PathEasyTier);
                // 重命名
                FileUtils.Move(PathEasyTier + "easytier-core.exe", PathEasyTier + "联机模块.exe");
                FileUtils.Move(PathEasyTier + "easytier-cli.exe", PathEasyTier + "联机模块 CLI.exe");
                // 清理
                FileUtils.Delete(PathEasyTier + "EasyTier.zip");
                Settings.Set("LinkEasyTierVersion", ServerVersion);
            }
            Task.Progress = 0.07d;
            // 获取节点列表
            UpdateLoadingPage("正在获取节点列表……", "获取节点列表");
            List<string> RawPeers;
            string CustomPeers = Settings.Get<string>("LinkCustomPeer");
            if (string.IsNullOrWhiteSpace(CustomPeers))
            {
                if (DiscoverNodeID == -2 && !IsServerSide)
                {
                    Panic("未填写自定义节点设置", $"$你必须在 {ModBase.vbLQ}自定义节点{ModBase.vbRQ} 设置中填写与房主相同的内容，{"\r\n"}才能进入该房间！");
                    return;
                }
                RawPeers = GetTargetPeers();
            }
            else
            {
                if (DiscoverNodeID != -2 && !IsServerSide)
                    ModMain.Hint("房主可能没有使用自定义节点设置，请确认你们的自定义节点设置是否一致！");
                RawPeers = CustomPeers.Split("，,".ToCharArray()).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                Logger.Info($"使用自定义节点：{CustomPeers}");
            }
            Task.Progress = 0.13d;
            // 获取空闲端口
            UpdateLoadingPage("正在启动联机模块……", "启动联机模块");
            var FreePorts = ModNet.FindFreePorts(5, ServerPort);
            ClientPort = FreePorts[0];
            RPCPort = FreePorts[1];
            int ListenersPort = FreePorts[2]; // 它会占用【当前、当前 +1、当前 +2】共三个端口
                                              // 获取启动参数
            string Arguments = (string)ModSecret.ServerConfig["Link"]["Argument"];
            Arguments += $" --network-name={NetworkName} --network-secret={NetworkSecret} --listeners {ListenersPort} --rpc-portal {RPCPort} --private-mode true";
            string HostName = (IsServerSide ? "Server-" : "Client-") + ModBase.Identify.GetStableHashCode().ConvertRadix(36).TrimStart('-').EnsureLength('0', 6);
            if (IsServerSide)
            {
                Arguments += $" -i 10.114.114.114 --hostname={HostName} --tcp-whitelist={ServerPort} --udp-whitelist={ServerPort}";
            }
            else
            {
                Arguments += $" -d --hostname={HostName} --tcp-whitelist=0 --udp-whitelist=0";
                // 端口转发
                ClientAddress = $"localhost:{ClientPort}";
                Arguments += $" --port-forward tcp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}";
                Arguments += $" --port-forward udp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}";
                Arguments += $" --port-forward tcp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}";
                Arguments += $" --port-forward udp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}";
            }
            foreach (string Peer in RawPeers)
                Arguments += $" -p=\"{Peer}\"";
            if (Settings.Get<int>("LinkLatencyMode") == 1)
                Arguments += " --latency-first";
            // 启动进程
            ProcessStart(Arguments);
            Task.Progress = 0.15d;
            // 等待连接
            int TimeoutCounter = 0;
            do
            {
                Thread.Sleep(500); // 为避免还没启动好先跑 CLI 了，先等半秒
                                   // 刷新信息
                CheckCrash();
                RefreshPeerLoader.WaitForExit(IsForceRestart: true);
                // 查找目标节点
                double Ping = GetPeerPing();
                if (Ping > 0d)
                {
                    Logger.Info($"已与目标建立连接，当前 Ping 为 {Ping:0.0}ms");
                    break; // 退出循环
                }
                // 更新进度
                double LastProgress = Task.Progress;
                int PeerCount = Peers is null ? -1 : Peers.Where(p => p.Ping > 0d).Count();
                switch (PeerCount)
                {
                    case -1: // CLI 无返回
                        {
                            Task.Progress = (Task.Progress + 0.02d).Clamp(0.15d, 0.25d);
                            break;
                        }
                    case 0: // CLI 有返回，但未连接到任何节点
                        {
                            UpdateLoadingPage("正在连接到节点……", "连接节点");
                            Task.Progress = (Task.Progress + 0.02d).Clamp(IsServerSide ? 0.5d : 0.3d, IsServerSide ? 0.95d : 0.5d); // 已连接到节点，但未连接到房主
                            break;
                        }

                    default:
                        {
                            UpdateLoadingPage("正在连接到房主……", "连接房主");
                            Task.Progress = (Task.Progress + 0.02d).Clamp(Math.Min(0.45d + PeerCount * 0.05d, 0.65d), 0.95d);
                            break;
                        }
                }
                // 超时判定
                if (LastProgress != Task.Progress)
                {
                    TimeoutCounter = 0;
                }
                else if (TimeoutCounter < 30 * 2)
                {
                    TimeoutCounter += 1;
                }
                else // 进度停滞超过 30s
                {
                    switch (PeerCount)
                    {
                        case -1: // CLI 无返回
                            {
                                Panic("无法启动联机模块", "近期日志：" + "\r\n" + LogHistory.Join("\r\n"));
                                break;
                            }
                        case 0: // CLI 有返回，但未连接到任何节点
                            {
                                Panic("无法连接到节点", $"请检查你的网络环境是否良好。"); // 已连接到节点，但未连接到房主
                                break;
                            }

                        default:
                            {
                                Panic("无法连接到房主", $"可能的原因：{"\r\n"}- 你或者房主的网络环境不佳{"\r\n"}- 房主已关闭房间{"\r\n"}- 邀请码输错了");
                                break;
                            }
                    }
                }
            }
            while (!Task.IsCanceled);
            if (Task.IsCanceled)
                throw new OperationCanceledException();
            // 等待连接稳定，最多 5s
            if (IsServerSide)
                return;
            UpdateLoadingPage("连接优化中……", "优化连接");
            Task.Progress = 0.999d;
            for (int i = 1; i <= 50; i++)
            {
                var Server = GetTargetPeer();
                if (Server is not null && !Server.Relay && Server.Ping < 100d)
                    return; // 结束
                if (Task.IsCanceled)
                    throw new OperationCanceledException();
                Thread.Sleep(100);
            }
        }
        /// <summary>
    /// 从在线配置和 API 获取需要连接的节点列表，并更新发现节点 ID。
    /// 根据发现节点 ID，会有以下行为变化：
    ///   -1：作为房主，根据负载均衡选择一个发现节点。
    ///   0：作为加入者，但邀请码未提供发现节点 ID，使用回退发现节点。
    ///   >0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点。
    /// </summary>
        private List<string> GetTargetPeers()
        {
            var FinalPeers = new List<string>();
            int FinalDiscoverID = -1;
            int FallbackDiscoverID = ModSecret.ServerConfig["Link"]["DiscoverPeerId"].ToObject<int>();
            string FallbackDiscoverAddress = ModSecret.ServerConfig["Link"]["DiscoverPeer"].ToString();
            try
            {
                // 从 API 获取节点列表
                JObject RawNodes;
                bool IsFallbackRawList = false;
                try
                {
                    RawNodes = (JObject)ModNet.NetRequestByClient("https://uptime.easytier.cn/api/nodes?page=1&per_page=1000", RequireJson: true).DeserializeJson();
                }
                catch (Exception exx)
                {
                    Logger.Warn(exx, "从源站获取节点列表失败，将使用 CDN 缓存");
                    RawNodes = (JObject)ModNet.NetRequestByClientRetry("https://easytier.meloong.com/?page=1&per_page=1000", RequireJson: true).DeserializeJson();
                    IsFallbackRawList = true;
                }
                // 分析节点列表
                var Nodes = new List<JObject>(); // 负载会添加在 load 字段上
                var BlackList = ModSecret.ServerConfig["Link"]["PeersBlackList"].Select(p => p.ToString()).ToList();
                foreach (JObject Node in RawNodes["data"]["items"])
                {
                    // 状态检查
                    int ID = Node["id"].ToObject<int>();
                    if (!Node["is_active"].ToObject<bool>() || !Node["is_approved"].ToObject<bool>())
                        continue;
                    if (ID == DiscoverNodeID)
                        goto ForcedPass; // 若为指定的发现节点，忽略后续检查
                    if (ID == FallbackDiscoverID)
                        continue; // 不主动选取回退发现节点
                    if (BlackList.Contains(Node["address"].ToString()))
                        continue; // ServerConfig 黑名单
                    if (Node["usage_percentage"].ToObject<double>() == 0d && ModBase.RandomInteger(1, 100) != 1)
                        continue; // 或许节点有问题才导致是 0 负载，让它只有 1% 概率被选中
                                  // 标签检查
                    var Tags = Node["tags"].Select(t => t.ToString()).ToList();
                    if (!Tags.Contains("国内") || !Tags.Contains("MC中继"))
                        continue;
                    // 计算负载并加入列表
                    ForcedPass:
                    ;

                    double Load = Node["usage_percentage"].ToObject<double>(); // 负载
                    Load *= 110d - Node["health_percentage_24h"].ToObject<double>(); // 可用率
                    Node["load"] = Load;
                    Nodes.Add(Node);
                }
                // 排序
                if (!IsFallbackRawList)
                {
                    Nodes = Nodes.OrderBy(n => n["load"].ToObject<double>()).ToList(); // 按负载从低到高排序
                }
                else
                {
                    Nodes = Nodes.Shuffle().ToList();
                } // 回退到 CDN 缓存时，由于负载数据可能过期，直接随机选择
                  // 选取发现节点
                JObject SelectedDiscoverNode = null;
                if (DiscoverNodeID == -1) // -1：作为房主，根据负载均衡选择一个发现节点
                {
                    SelectedDiscoverNode = Nodes.FirstOrDefault(n => !n["allow_relay"].ToObject<bool>());
                }
                else if (DiscoverNodeID > 0) // >0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点
                {
                    SelectedDiscoverNode = Nodes.FirstOrDefault(n => n["id"].ToObject<int>() == DiscoverNodeID);
                    if (SelectedDiscoverNode is null)
                    {
                        Logger.Warn($"未找到 ID {DiscoverNodeID} 的发现节点");
                        Panic("房间已过期", "请让房主重新创建房间！");
                        throw new OperationCanceledException();
                    }
                }
                if (SelectedDiscoverNode is null) // 使用回退发现节点
                {
                    SelectedDiscoverNode = new JObject() { { "address", FallbackDiscoverAddress }, { "id", FallbackDiscoverID } };
                    Logger.Warn("将使用回退发现节点");
                }
                FinalPeers.Add(SelectedDiscoverNode["address"].ToString());
                FinalDiscoverID = SelectedDiscoverNode["id"].ToObject<int>();
                Logger.Info($"发现节点：{SelectedDiscoverNode["address"]} (ID: {FinalDiscoverID})");
                // 选取中继节点
                if (ModBase.ModeDebug || !IsServerSide) // 房主只连接发现节点，不连接中继节点
                {
                    int RelayCount = ModSecret.ServerConfig["Link"]["RandomPeer"].ToObject<int>();
                    var RelayNodes = Nodes.Where(n => n["allow_relay"].ToObject<bool>()).ToList();
                    if (RelayNodes.Count < RelayCount)
                        throw new Exception($"可用的中继节点数量不足，需要 {RelayCount} 个，实际 {RelayNodes.Count} 个");
                    FinalPeers.AddRange(RelayNodes.Take(RelayCount).Select(n => n["address"].ToString()));
                }
            }
            catch (Exception ex)
            {
                ex.ThrowIfCanceled();
                Logger.Error(ex, "获取节点列表失败，联机质量可能受到影响", LogBehavior.Toast);
                FinalPeers.AddRange(ModSecret.ServerConfig["Link"]["Peers"].Select(p => p.ToString()));
                if (FinalDiscoverID <= 0)
                {
                    FinalPeers.Add(FallbackDiscoverAddress);
                    FinalDiscoverID = FallbackDiscoverID;
                }
            }
            // 版本 1 兼容
            if (((JObject)ModSecret.ServerConfig["Link"]).ContainsKey("EnableForcedPeer"))
            {
                FinalPeers.Add("tcp://mc1.easytier.cn:55558");
                if (FinalDiscoverID <= 0)
                    FinalDiscoverID = 94;
            }
            // 强制添加的节点
            FinalPeers.AddRange(ModSecret.ServerConfig["Link"]["MandatoryPeers"].Select(p => p.ToString()));
            // 结束
            DiscoverNodeID = FinalDiscoverID;
            return FinalPeers.Distinct().ToList();
        }

        #endregion

        #region 进程与日志

        /// <summary>
    /// EasyTier Core 进程。
    /// </summary>
        private static Process ProcessCore;
        private static AutoResetEvent ProcessOutputHandle;
        private static AutoResetEvent ProcessErrorHandle;

        /// <summary>
    /// 若程序正在运行，则结束程序进程。
    /// 即使失败也不会抛出异常。
    /// </summary>
        public static void ProcessStop()
        {
            try
            {
                // 关闭未捕获的进程
                foreach (var ProcessObject in Process.GetProcesses())
                {
                    if (ProcessObject.Id is var arg1 && (ProcessCore?.Id) is { } arg2 && arg1 == arg2)
                        continue;
                    if (ProcessObject.ProcessName != "联机模块" && ProcessObject.ProcessName != "联机模块 CLI")
                        continue;
                    try
                    {
                        Logger.Info($"停止残留的联机模块，PID：{ProcessObject.Id}，进程名：{ProcessObject.ProcessName}");
                        ProcessObject.Kill();
                        ProcessObject.Close();
                    }
                    catch (Exception exx)
                    {
                        Logger.Warn(exx, $"结束进程失败（{ProcessObject.ProcessName}，PID {ProcessObject.Id}）");
                    }
                }
                // 关闭由自身启动的进程
                if (ProcessCore is not null && !ProcessCore.HasExited)
                {
                    Logger.Info($"停止所启动的联机模块，PID：{ProcessCore.Id}");
                    ProcessCore.Kill();
                    ProcessCore.Close();
                    ProcessCore = null;
                }
                // 释放资源
                ProcessOutputHandle?.Dispose();
                ProcessErrorHandle?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "停止联机模块失败");
            }
        }
        /// <summary>
    /// 出现意外错误，给出错误信息并结束联机。
    /// </summary>
        private void Panic(string Brief, string Detail)
        {
            // 常见原因分析
            if (Detail.Contains("failed to listen on "))
            {
                Detail = $"监听端口失败。{"\r\n"}请点击重试，如果还是出现此错误，可以重启电脑解决。{"\r\n"}{"\r\n"}{Detail}";
            }
            // 显示信息
            if (LinkState == LinkStates.Loading)
            {
                FailReason = Brief;
                LinkLoader.Failed(new Exception(Detail));
            }
            else
            {
                ModMain.MyMsgBox(Detail, "联机出错：" + Brief, IsWarn: true);
                ChangeState(LinkStates.Waiting);
            }
        }

        /// <summary>
    /// 启动进程。
    /// </summary>
        private void ProcessStart(string Arguments)
        {
            var Info = new ProcessStartInfo()
            {
                FileName = PathEasyTier + "联机模块.exe",
                Arguments = Arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            Logger.Info($"正在启动 EasyTier：{Arguments}");
            ProcessCore = new Process() { StartInfo = Info };
            void LogLineHandler(object sender, DataReceivedEventArgs e, AutoResetEvent Handle) { try { if (e.Data is null) { Handle.Set(); return; } ProcessLogLine(e.Data); } catch (ObjectDisposedException) { } catch (Exception ex) { Logger.Warn(ex, "读取 EasyTier 信息失败"); } };
            ProcessOutputHandle = new AutoResetEvent(false);
            ProcessCore.OutputDataReceived += (sender, e) => LogLineHandler(sender, e, ProcessOutputHandle);
            ProcessErrorHandle = new AutoResetEvent(false);
            ProcessCore.ErrorDataReceived += (sender, e) => LogLineHandler(sender, e, ProcessErrorHandle);
            ProcessCore.Start();
            ProcessCore.BeginOutputReadLine();
            ProcessCore.BeginErrorReadLine();
        }
        private void ProcessLogLine(string Line)
        {
            // 记录日志
            Logger.Info(Line);
            LogHistory.Enqueue(Line);
            if (LogHistory.Count >= 10)
                LogHistory.Dequeue();
            // 检查日志内容
            if (Line.ContainsIgnoreCase("new peer connection added"))
            {
                Logger.Warn($"已建立连接：{Line.RegexSeek("(?<=remote_addr.+?\")[^\"}]{3,}") ?? Line}");
                Update();
            }
            else if (Line.ContainsIgnoreCase("peer connection removed"))
            {
                Logger.Warn($"已断开连接：{Line.RegexSeek("(?<=remote_addr.+?\")[^\"}]{3,}") ?? Line}");
                Update();
            }
        }
        private Queue<string> LogHistory = new Queue<string>(10);

        #endregion

        #region CLI 节点信息获取

        private class Peer
        {
            /// <summary>
        /// 该节点的类型。
        /// </summary>
            public readonly Types Type;
            public enum Types
            {
                /// <summary>
            /// 客户端。
            /// </summary>
                Client,
                /// <summary>
            /// 服务器。
            /// </summary>
                Server,
                /// <summary>
            /// 公共节点。
            /// </summary>
                Misc,
                /// <summary>
            /// 自己，或者自己的重复（它可能返回多个自己）。
            /// </summary>
                Self
            }
            /// <summary>
        /// 与该节点的延迟 (ms)。
        /// 若是自己则为 0。
        /// </summary>
            public readonly double Ping;
            /// <summary>
        /// 名称。
        /// </summary>
            public readonly string Name;
            /// <summary>
        /// 是否通过中继连接。
        /// </summary>
            public readonly bool Relay;
            /// <summary>
        /// NAT 类型。
        /// </summary>
            public readonly NATTypes NATType;

            /// <summary>
        /// 从 CLI 给出的信息分析对应的数据。
        /// </summary>
            public Peer(JObject Info)
            {
                // 类别
                string PeerName = Info["hostname"].ToString();
                if (Info["cost"].ToString() == "Local")
                {
                    Type = Types.Self;
                }
                else if (PeerName.StartsWithF("Client"))
                {
                    Type = Types.Client;
                }
                else if ((string)Info["ipv4"] == "10.114.114.114") // 只允许这个 IP 作为房主，避免网络被多个房主搞炸的情况
                {
                    Type = Types.Server;
                }
                else
                {
                    Type = Types.Misc;
                }
                // 基础信息
                double.TryParse((string)Info["lat_ms"], NumberStyles.Any, CultureInfo.InvariantCulture, out Ping);
                Name = PeerName;
                Relay = Info["cost"].ToString().ContainsIgnoreCase("relay");
                NATType = EnumUtils.FromString<NATTypes>((string)Info["nat_type"]);
            }
            public override string ToString()
            {
                return $"{Type} - {Name} - Ping {Ping:0.0}ms [中继? {Relay}] - NAT {NATType}";
            }
        }
        private enum NATTypes
        {
            // https://github.com/EasyTier/EasyTier/blob/6bb2fd9a15ab2499bdeabdcc3a925e9bd9aebf50/easytier/src/proto/common.proto#L129
            Unknown = 0,
            OpenInternet = 1,
            NoPAT = 2,
            FullCone = 3,
            Restricted = 4,
            PortRestricted = 5,
            Symmetric = 6,
            SymUdpFirewall = 7,
            SymmetricEasyInc = 8,
            SymmetricEasyDec = 9,
            /// <summary>
        /// 尚未获取。
        /// </summary>
            Pending = 10
        }

        /// <summary>
    /// 当前的节点列表，使用 RefreshPeerLoader 来刷新。
    /// 若尚未成功获取过则为 Nothing，但保证在加载完成后至少是一个列表。
    /// </summary>
        private List<Peer> Peers = null;
        /// <summary>
    /// 自己的 NAT 类型。
    /// </summary>
        private NATTypes NATType = NATTypes.Pending;

        /// <summary>
    /// 调用 EasyTier CLI 获取已连接节点信息。
    /// 这会过滤掉尚未完成连接的其他节点以及自己。
    /// 若在加载完成后连续获取失败 3 次，则会强制断开连接。
    /// </summary>
        private ModLoader.LoaderTask<int, List<Peer>> RefreshPeerLoader;
        private void RefreshPeer()
        {
            try
            {
                string CliResult = ""; // StartProcessAndGetOutput(PathEasyTier & "联机模块 CLI.exe", $"-o json -p 127.0.0.1:{RPCPort} peer", 2000, Encoding:=Encoding.UTF8, PrintLog:=False)
                                       // 解析
                if (!CliResult.Contains("lat_ms"))
                    throw new Exception("CLI 调用失败：" + "\r\n" + CliResult);
                if (ModBase.GetUuid() % (ModBase.ModeDebug ? 23 : 103) == 0)
                    Logger.Info($"CLI 输出抽样：{"\r\n"}{CliResult}");
                var NewPeers = new List<Peer>();
                foreach (JObject Line in (JArray)CliResult.DeserializeJson())
                {
                    try
                    {
                        var Peer = new Peer(Line);
                        if (Peer.Type == Peer.Types.Self)
                        {
                            NATType = Peer.NATType; // 记录自己的 NAT
                        }
                        else
                        {
                            NewPeers.Add(Peer);
                        }
                    }
                    catch (Exception exx)
                    {
                        Logger.Warn(exx, $"错误的信息（{Line}）");
                    }
                }
                // 完成
                Peers = NewPeers;
                FailCount = 0;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "获取节点信息失败");
                if (LinkState == LinkStates.Finished)
                {
                    FailCount += 1;
                    if (FailCount >= 4)
                        Panic("获取节点信息失败", ex.Message);
                }
            }
        }
        /// <summary>
    /// CLI 调用连续失败的次数。
    /// </summary>
        private int FailCount = 0;

        /// <summary>
    /// 根据当前的节点列表获取 Ping 值。
    /// 服务端会返回所有节点中最低的 Ping，客户端会返回服务端的 Ping。
    /// 0 代表尚不可用。
    /// </summary>
        private double GetPeerPing()
        {
            var Peer = GetTargetPeer();
            if (Peer is null)
                return 0d;
            return Peer.Ping;
        }
        /// <summary>
    /// 服务端会返回所有节点中 Ping 大于 0 最低的那一个，客户端会返回服务端。
    /// 若没有则为 Nothing。
    /// </summary>
        private Peer GetTargetPeer()
        {
            if (Peers is null)
                return null;
            IEnumerable<Peer> Targets;
            if (IsServerSide)
            {
                Targets = Peers.Where(p => p.Ping > 0d);
            }
            else
            {
                Targets = Peers.Where(p => p.Type == Peer.Types.Server && p.Ping > 0d);
            }
            if (!Targets.Any())
                return null;
            // 返回 Ping 且最低的那个
            double MinPing = Targets.Min(p => p.Ping);
            return Targets.First(p => p.Ping == MinPing);
        }

        /// <summary>
    /// 获取当前房间中的人数，包括自己。
    /// 0 代表不可用。
    /// </summary>
        private int PeopleCount
        {
            get
            {
                if (Peers is null)
                    return 0;
                return Peers.Where(p => p.Type != Peer.Types.Misc).Count() + 1; // 加上自己
            }
        }
        private bool _StartTimerThread_IsTimerStarted = false;

        #endregion

        #region 定时任务

        // 启动
        private void StartTimerThread()
        {
            if (_StartTimerThread_IsTimerStarted)
                return;
            ModBase.RunInNewThread(() =>
        {
            int Counter = 0;
            while (true)
            {
                try
                {
                    Thread.Sleep(200);
                    Counter += 200;
                    // 每秒执行一次
                    if (Counter >= 1000)
                    {
                        Counter = 0;
                        Update();
                    }
                    // 每 200ms 更新进度条
                    if (LinkState == LinkStates.Loading)
                        ModBase.RunInUi(() => UpdateProgressBar());
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "联机模块主时钟出错");
                    Thread.Sleep(10000);
                }
            }
        }, "Link Timer");
        }
        private Socket _Update_BroadcastSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);

        // 每秒或进入页面时触发
        private void Update()
        {
            if (LinkState != LinkStates.Finished)
                return;
            // 重新获取信息
            lock (RefreshPeerLoader.LockState)
            {
                if (RefreshPeerLoader.State != ModBase.LoadState.Loading)
                    RefreshPeerLoader.Start(IsForceRestart: true);
            }
            // 更新 UI
            if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.Link)
            {
                ModBase.RunInUi(() =>
        {
            // Logo 旋转动画
            ModAnimation.AniStart(ModAnimation.AaRotateTransform(this.ImgFinishLogo, 500d, 5000), "Link Logo Rotation");
            // Ping
            double Ping = GetPeerPing();
            bool Connecting = Ping % 500d == 0d || $"{Ping:0.0}" == "1.0" || FailCount > 0;
            if (Connecting)
            {
                this.LabFinishPing.Text = "连接优化中";
            }
            else
            {
                this.LabFinishPing.Text = Ping >= 10d ? $"{Ping:0} ms" : $"{Ping:0.0} ms";
            }
            // 人数显示
            this.LabFinishPlayer.Text = PeopleCount + "  人";
            // ------------------------
            // 提示条
            // ------------------------
            this.HintFinish.Visibility = Visibility.Collapsed;
            if (IsServerSide)
                return; // 服务端没有需要显示的提示
            if (Connecting)
                return; // 连接稳定过程中不显示提示
                        // 中继提示
            var Server = GetTargetPeer();
            if (Server is null)
                return;
            if (Server.Relay)
            {
                if (NATType >= NATTypes.Symmetric && Server.NATType >= NATTypes.Symmetric)
                {
                    this.HintFinish.Text = "你和房主的网络环境都不太好，";
                }
                else if (NATType >= NATTypes.Symmetric)
                {
                    this.HintFinish.Text = "你的网络环境不太好，";
                }
                else if (Server.NATType >= NATTypes.Symmetric)
                {
                    this.HintFinish.Text = "房主的网络环境不太好，";
                }
                else
                {
                    this.HintFinish.Text = "你或者房主的网络环境不太好，";
                }
                if (string.IsNullOrWhiteSpace(Settings.Get<string>("LinkCustomPeer")))
                {
                    this.HintFinish.Text += "正使用社区节点进行中继。";
                }
                else
                {
                    this.HintFinish.Text += "正通过自定义节点进行中继。";
                }
                this.HintFinish.Visibility = Visibility.Visible;
                this.HintFinish.Theme = MyHint.Themes.Yellow;
                return;
            }
            // 环境比房主更好的提示
            if (NATType >= NATTypes.PortRestricted)
                return; // 自身的 NAT 为 2 或更好
            if (Server.NATType < NATTypes.PortRestricted)
                return; // 房主的 NAT 为 3 或更差
            var OtherPlayers = Peers.Where(p => p.Type == Peer.Types.Client).ToList();
            if (!OtherPlayers.Any())
                return; // 房间里还有其他玩家
            if (OtherPlayers.Any(p => p.Relay))
                return; // 自己可以与所有玩家打洞
            if (OtherPlayers.All(p => p.NATType < NATTypes.PortRestricted))
                return; // 任意其他玩家的 NAT 为 3 或更差
            this.HintFinish.Visibility = Visibility.Visible;
            this.HintFinish.Theme = MyHint.Themes.Blue;
            this.HintFinish.Text = "你的网络环境比房主更好！如果你来当房主，其他玩家或许能更加流畅！";
        });
            }
            // 检查核心状态
            CheckCrash();
            // 检查节点状态
            if (!Peers.Any())
            {
                Panic("网络连接已断开", "请检查你的网络环境是否正常。");
                return;
            }
            if (!IsServerSide && !Peers.Any(p => p.Type == Peer.Types.Server))
            {
                ModMain.MyMsgBox("房主已离开房间！", "联机结束");
                ChangeState(LinkStates.Waiting);
                return;
            }
            // 广播联机房间端口
            if (!IsServerSide)
            {
                try
                {
                    _Update_BroadcastSocket.SendTo(Encoding.UTF8.GetBytes($"[MOTD]PCL Next 联机房间[/MOTD][AD]{ClientPort}[/AD]"), SocketFlags.None, new IPEndPoint(IPAddress.Loopback, 4445));
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "广播联机房间端口失败");
                }
            }
        }
        /// <summary>
    /// 检查联机模块是否崩溃。
    /// </summary>
        private void CheckCrash()
        {
            if (ProcessCore is not null && !ProcessCore.HasExited)
                return;
            Panic("联机模块已崩溃", "近期日志：" + "\r\n" + LogHistory.Join("\r\n"));
        }

        #endregion

    }
}