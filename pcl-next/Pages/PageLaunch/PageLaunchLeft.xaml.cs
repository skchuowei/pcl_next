using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    public partial class PageLaunchLeft
    {

        // 加载当前版本
        private bool IsLoad = false;
        private bool IsLoadFinished = false;

        public PageLaunchLeft()
        {
            InitializeComponent();
            this.RadioLoginType0.Check += (p1, p2) => RadioLoginType_Change((object)p1, (bool)p2);
            this.RadioLoginType5.Check += (p1, p2) => RadioLoginType_Change((object)p1, (bool)p2);
            this.BtnVersion.Click += (p1, p2) => BtnVersion_Click();
            this.BtnLaunch.Click += (p1, p2) => LaunchButtonClick();
            this.BtnLaunch.Loaded += (p1, p2) => RefreshButtonsUI();
            this.BtnCancel.Click += (p1, p2) => BtnCancel_Click();
            this.BtnMore.Click += (p1, p2) => BtnMore_Click();
            // 账号区三态
            SetupCompactButton(this.BtnCompactSkin, LogoCompactSkin, "更换皮肤（前往「我的账号」）");
            SetupCompactButton(this.BtnCompactEdit, ModBase.Logo.IconButtonEdit, "编辑账号（前往「我的账号」）");
            SetupCompactButton(this.BtnCompactSwitch, ModBase.Logo.IconButtonRefresh, "切换账号");
            this.BtnCompactSkin.Click += (p1, p2) => OpenAccountSetupPage();
            this.BtnCompactEdit.Click += (p1, p2) => OpenAccountSetupPage();
            this.BtnCompactSwitch.Click += (p1, p2) => SwitchAccountMode(AccountPanelMode.Select);
            this.BtnLoginCancel.Click += (p1, p2) => SwitchAccountMode(ModAccountList.GetAll().Count > 0 ? AccountPanelMode.Select : AccountPanelMode.Login);
            this.BtnLoginConfirm.Click += (p1, p2) => ConfirmLogin();
            this.PanLaunchingInfo.SizeChanged += (p1, p2) => PanLaunchingInfo_SizeChangedW((object)p1, (SizeChangedEventArgs)p2);
            this.PanLaunchingInfo.SizeChanged += (p1, p2) => PanLaunchingInfo_SizeChangedH((object)p1, (SizeChangedEventArgs)p2);
            this.Loaded += (_, __) => PageLaunchLeft_Loaded();
        }
        public void PageLaunchLeft_Loaded()
        {
            if (IsLoad)
                RefreshPage(true, false);

            this.AprilPosTrans.X = 0d;
            this.AprilPosTrans.Y = 0d;

            if (IsLoad)
                return;
            IsLoad = true;
            ModAnimation.AniControlEnabled += 1;

            // 开始按钮
            ModMinecraft.McInstanceListLoader.LoadingStateChanged += (_, __) => RefreshButtonsUI();
            ModMinecraft.McFolderListLoader.LoadingStateChanged += (_, __) => RefreshButtonsUI();
            RefreshButtonsUI();

            // 加载版本
            ModBase.RunInNewThread(() =>
        {
            // 自动整合包安装：准备
            string PackInstallPath = null;
            if (FileUtils.Exists(Paths.Base + "modpack.zip"))
                PackInstallPath = Paths.Base + "modpack.zip";
            if (FileUtils.Exists(Paths.Base + "modpack.mrpack"))
                PackInstallPath = Paths.Base + "modpack.mrpack";
            if (PackInstallPath is not null)
            {
                Logger.Warn($"需自动安装整合包：{PackInstallPath}");
                PageSelectLeft.CreateMcFolderInCurrentPath();
                ModMinecraft.McFolderListLoader.WaitForExit();
            }
            // 确认 Minecraft 文件夹存在
            if (string.IsNullOrEmpty(ModMinecraft.McFolderSelected) || !DirectoryUtils.Exists(ModMinecraft.McFolderSelected))
            {
                // 无效的文件夹
                if (string.IsNullOrEmpty(ModMinecraft.McFolderSelected))
                {
                    Logger.Info("没有已储存的 Minecraft 文件夹");
                }
                else
                {
                    Logger.Warn($"Minecraft 文件夹无效，该文件夹已不存在：{ModMinecraft.McFolderSelected}");
                }
                ModMinecraft.McFolderListLoader.WaitForExit(IsForceRestart: true);
                ModMinecraft.McFolderSelected = ModMinecraft.McFolderList.First().Location;
            }
            if (Settings.Get<bool>("SystemDebugDelay"))
                Thread.Sleep(ModBase.RandomInteger(500, 3000));
            // 自动整合包安装
            if (PackInstallPath is not null)
            {
                try
                {
                    var InstallLoader = ModModpack.ModpackInstall(PackInstallPath);
                    Logger.Info($"自动安装整合包已开始：{PackInstallPath}");
                    InstallLoader.WaitForExit();
                    if (InstallLoader.State == ModBase.LoadState.Finished)
                    {
                        Logger.Info($"自动安装整合包成功，清理安装包：{PackInstallPath}");
                        FileUtils.Delete(PackInstallPath);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.IsCanceled())
                    {
                        Logger.Info($"自动安装整合包已取消：{PackInstallPath}");
                    }
                    else
                    {
                        Logger.Error(ex, $"自动安装整合包失败：{PackInstallPath}", LogBehavior.Alert);
                    }
                }
            }
            // 确认 Minecraft 版本存在
            string Selection = ModBase.ReadIni(ModMinecraft.McFolderSelected + "PCL.ini", "Version");
            var Instance = string.IsNullOrEmpty(Selection) ? null : new ModMinecraft.McInstance(Selection);
            if (Instance is null || !PathUtils.IsParentOf(ModMinecraft.McFolderSelected, Instance.PathVersion) || !Instance.Check())
            {
                // 无效的版本
                Logger.Info($"当前选择的 Minecraft 版本无效：{(Instance is null ? "null" : Instance.PathVersion)}", Instance == null ? LogBehavior.None : LogBehavior.ToastIfDebug);
                if (!(ModMinecraft.McInstanceListLoader.State == ModBase.LoadState.Finished))
                    ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\", WaitForExit: true);
                if (!ModMinecraft.McInstanceList.Any() || ModMinecraft.McInstanceList.First().Value[0].Logo.Contains("RedstoneBlock"))
                {
                    Instance = null;
                    Logger.Info("无可用 Minecraft 版本");
                }
                else
                {
                    Instance = ModMinecraft.McInstanceList.First().Value[0];
                    Logger.Info($"自动选择 Minecraft 版本：{Instance.PathVersion}");
                }
            }
            ModBase.RunInUi(() =>
        {
                ModMinecraft.McInstanceSelected = Instance; // 绕这一圈是为了避免 McVersionCheck 触发第二次版本改变
                IsLoadFinished = true;
                RefreshButtonsUI();
                RefreshPage(false, false); // 有可能选择的版本变化了，需要重新刷新
                if (string.IsNullOrEmpty(ModLaunch.McLoginAble()))
                    ModLaunch.McLoginLoader.Start(); // 自动登录
                                                     // 用于自动化测试生成的程序是否可以正常运行
                if (Environment.CommandLine.Contains("--test"))
                {
                    ModBase.RunInThread(() =>
        {
                        Thread.Sleep(500);
                        ModBase.RunInUi(() => ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Download));
                        Thread.Sleep(500);
                        ModBase.RunInUi(() => ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Download, FormMain.PageSubType.DownloadMod));
                        Thread.Sleep(500);
                        ModBase.RunInUi(() => ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Setup));
                        Thread.Sleep(500);
                        ModBase.RunInUi(() => ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Other));
                        Thread.Sleep(500);
                        ModBase.RunInUi(() => BtnVersion_Click());
                        Thread.Sleep(500);
                        ModBase.RunInUi(() => BtnMore_Click());
                        Thread.Sleep(500);
                        FormMain.EndProgramForce((ModBase.ProcessReturnValues)123);
                    });
                }
            });
        }, "Version Check", ThreadPriority.AboveNormal);

            // 改变页面
            var LoginType = Settings.Get<ModLaunch.McLoginType>("LoginType");
            if (LoginType == ModLaunch.McLoginType.Legacy || LoginType == ModLaunch.McLoginType.Ms)
                ((MyRadioButton)this.FindName("RadioLoginType" + ((int)LoginType).ToString())).Checked = true;
            RefreshPage(false, false);

            ModAnimation.AniControlEnabled -= 1;
        }

        #region 切换大页面

        /// <summary>
    /// 切换至启动中页面。
    /// </summary>
        public void PageChangeToLaunching()
        {
            // 修改登陆方式
            switch (Settings.Get<ModLaunch.McLoginType>("LoginType"))
            {
                case ModLaunch.McLoginType.Legacy:
                    {
                        this.LabLaunchingMethod.Text = "离线登录";
                        break;
                    }
                case ModLaunch.McLoginType.Ms:
                    {
                        this.LabLaunchingMethod.Text = "正版登录";
                        break;
                    }
                case ModLaunch.McLoginType.Nide:
                    {
                        this.LabLaunchingMethod.Text = "统一通行证";
                        break;
                    }
                case ModLaunch.McLoginType.Auth:
                    {
                        this.LabLaunchingMethod.Text = "Authlib-Injector";
                        break;
                    }
            }
            // 初始化页面
            this.LabLaunchingName.Text = ModMinecraft.McInstanceSelected.Name;
            this.LabLaunchingStage.Text = "初始化";
            this.LabLaunchingTitle.Text = ModLaunch.CurrentLaunchOptions?.SaveBatch is null ? "正在启动游戏" : "正在导出启动脚本";
            this.LabLaunchingProgress.Text = "0.00 %";
            this.LabLaunchingProgress.Opacity = 1d;
            this.LabLaunchingDownload.Visibility = Visibility.Visible;
            this.LabLaunchingProgressLeft.Opacity = 0.6d;
            this.LabLaunchingDownload.Visibility = Visibility.Visible;
            this.LabLaunchingDownload.Text = "0 B/s";
            this.LabLaunchingDownload.Opacity = 0d;
            this.LabLaunchingDownload.Visibility = Visibility.Collapsed;
            this.LabLaunchingDownloadLeft.Opacity = 0d;
            this.LabLaunchingDownloadLeft.Visibility = Visibility.Collapsed;
            this.ProgressLaunchingFinished.Width = new GridLength(0d, GridUnitType.Star);
            this.ProgressLaunchingUnfinished.Width = new GridLength(1d, GridUnitType.Star);
            this.PanLaunchingHint.Opacity = 0d;
            this.PanLaunchingHint.Visibility = Visibility.Collapsed;
            this.PanLaunchingInfo.Width = double.NaN; // 重置宽度改变动画
            ModLaunch.McLaunchProcess = null;
            ModLaunch.McLaunchWatcher = null;
            // 获取 “你知道吗” 提示
            this.LabLaunchingHint.Text = PageOtherTest.GetRandomHint();
            // 初始化其他页面
            this.PanInput.IsHitTestVisible = false;
            this.PanLaunching.IsHitTestVisible = false;
            this.LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Run;
            this.PanLaunching.Visibility = Visibility.Visible;
            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.PanInput, 0d, 50), ModAnimation.AaOpacity(this.PanInput, -this.PanInput.Opacity, 110, Ease: new ModAnimation.AniEaseInFluent(), After: true), ModAnimation.AaScaleTransform(this.PanInput, 1.2d - ((ScaleTransform)this.PanInput.RenderTransform).ScaleX, 160), ModAnimation.AaOpacity(this.PanLaunching, 1d - this.PanLaunching.Opacity, 150, 100), ModAnimation.AaScaleTransform(this.PanLaunching, 1d - ((ScaleTransform)this.PanLaunching.RenderTransform).ScaleX, 500, 100, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)), ModAnimation.AaCode(() => this.PanLaunching.IsHitTestVisible = true, 150) }, "Launch State Page"); // 略作延迟，这样如果预检测失败，不会出现奇怪的弹一下的动画
        }
        /// <summary>
    /// 切换至登录页面。
    /// </summary>
        public void PageChangeToLogin()
        {
            ((dynamic)PageGet(PageCurrent)).Reload(KeepInput: false);
            this.PanInput.IsHitTestVisible = false;
            this.PanLaunching.IsHitTestVisible = false;
            this.LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Stop;
            this.PanInput.Visibility = Visibility.Visible;
            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.PanLaunching, -this.PanLaunching.Opacity, 150), ModAnimation.AaScaleTransform(this.PanLaunching, 0.8d - ((ScaleTransform)this.PanLaunching.RenderTransform).ScaleX, 150, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaOpacity(this.PanInput, 1d - this.PanInput.Opacity, 250, 50), ModAnimation.AaScaleTransform(this.PanInput, 1d - ((ScaleTransform)this.PanInput.RenderTransform).ScaleX, 300, 50, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)), ModAnimation.AaCode(() => this.PanInput.IsHitTestVisible = true, 200) }, "Launch State Page", true);
        }

        #endregion

        #region 切换登录页面

        private enum PageType
        {
            None,
            Legacy,
            Nide,
            NideSkin,
            Auth,
            AuthSkin,
            Ms,
            MsSkin
        }
        /// <summary>
    /// 当前页面的种类。
    /// </summary>
        private PageType PageCurrent = PageType.None;

        private object PageGet(PageType Type)
        {
            switch (Type)
            {
                case PageType.Legacy:
                    {
                        if (ModMain.FrmLoginLegacy == null)
                            ModMain.FrmLoginLegacy = new PageLoginLegacy();
                        return ModMain.FrmLoginLegacy;
                    }
                case PageType.Nide:
                    {
                        if (ModMain.FrmLoginNide == null)
                            ModMain.FrmLoginNide = new PageLoginNide();
                        return ModMain.FrmLoginNide;
                    }
                case PageType.NideSkin:
                    {
                        if (ModMain.FrmLoginNideSkin == null)
                            ModMain.FrmLoginNideSkin = new PageLoginNideSkin();
                        return ModMain.FrmLoginNideSkin;
                    }
                case PageType.Auth:
                    {
                        if (ModMain.FrmLoginAuth == null)
                            ModMain.FrmLoginAuth = new PageLoginAuth();
                        return ModMain.FrmLoginAuth;
                    }
                case PageType.AuthSkin:
                    {
                        if (ModMain.FrmLoginAuthSkin == null)
                            ModMain.FrmLoginAuthSkin = new PageLoginAuthSkin();
                        return ModMain.FrmLoginAuthSkin;
                    }
                case PageType.Ms:
                    {
                        if (ModMain.FrmLoginMs == null)
                            ModMain.FrmLoginMs = new PageLoginMs();
                        return ModMain.FrmLoginMs;
                    }
                case PageType.MsSkin:
                    {
                        if (ModMain.FrmLoginMsSkin == null)
                            ModMain.FrmLoginMsSkin = new PageLoginMsSkin();
                        return ModMain.FrmLoginMsSkin;
                    }

                default:
                    {
                        throw new ArgumentOutOfRangeException("Type", "即将切换的登录分页编号越界");
                    }
            }
        }
        /// <summary>
    /// 切换现有登录页面种类，返回新页面的实例。
    /// </summary>
    /// <param name="Type">新页面的种类。</param>
    /// <param name="Anim">是否显示动画。</param>
        private object PageChange(PageType Type, bool Anim)
        {
            object PageNew = ModMain.FrmLoginMs; // 初始化一个东西，避免在执行时出现异常导致雪崩
            try
            {

                #region 确定更改的页面实例并实例化
                if (PageCurrent == Type)
                    return PageNew;
                PageNew = PageGet(Type);
                #endregion

                #region 切换页面
                ModAnimation.AniStop("FrmLogin PageChange");
                // 清除页面关联性
                if (!(PageNew == null) && !(((dynamic)PageNew).Parent == null))
                    ((dynamic)PageNew).SetValue(ContentPresenter.ContentProperty, (object)null);
                if (Anim)
                {
                    // 动画
                    // 执行动画
                    this.Dispatcher.Invoke(() => ModAnimation.AniStart(new[] {
                        ModAnimation.AaOpacity(this.PanLogin, -this.PanLogin.Opacity, 100, Ease: new ModAnimation.AniEaseOutFluent()),
                                                ModAnimation.AaCode(() =>
                        {
                            ModAnimation.AniControlEnabled += 1;
                            this.PanLogin.Children.Clear();
                            this.PanLogin.Children.Add((UIElement)PageNew);
                            ModAnimation.AniControlEnabled -= 1;
                        }, 100),
                        ModAnimation.AaOpacity(this.PanLogin, 1d, 100, 120, new ModAnimation.AniEaseInFluent())
                                    }, "FrmLogin PageChange"), DispatcherPriority.Render);
                }
                else
                {
                    // 无动画
                    ModAnimation.AniControlEnabled += 1;
                    this.PanLogin.Children.Clear();
                    this.PanLogin.Children.Add((UIElement)PageNew);
                    ModAnimation.AniControlEnabled -= 1;
                }
                #endregion

                PageCurrent = Type;
                return PageNew;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"切换登录分页失败（{Type}）");
                return PageNew;
            }
        }

        /// <summary>
    /// 确认当前显示的子页面正确，并刷新该页面。
    /// </summary>
        public void RefreshPage(bool KeepInput, bool Anim)
        {
            // 刷新账号区（独立于登录分页，需在下方提前返回之前更新）
            RefreshAccountPanel();
            // 获取页面的可用种类并回写缓存
            PageType Type = PageType.None; // VB 的 Dim Type As PageType 默认值为 PageType.None，按原文补默认值
            int LoginPageType;
            if (_LoginPageTypeOverride >= 0)
            {
                // 新建档案时临时指定的验证类型，不写回设置
                LoginPageType = _LoginPageTypeOverride;
            }
            else if (ModMinecraft.McInstanceSelected is not null)
            {
                LoginPageType = Settings.Get<int>("VersionServerLogin", Instance: ModMinecraft.McInstanceSelected);
                // 缓存当前版本的页面种类，下一次打开 McInstanceSelected 为空时才能加载出正确的页面
                Settings.Set("LoginPageType", LoginPageType);
            }
            else
            {
                LoginPageType = Settings.Get<int>("LoginPageType");
            }
            switch (LoginPageType)
            {
                // VB 原文的 Case Else 先记错误日志，再 GoTo UnknownType 跳入 Case 0 的分支体；
                // C# 不允许跨 case 跳转，改为 case 0 与 default 合并共用同一段分支体
                case 0: // 正版或离线
                default:
                    {
                        // 仅从 default 进入时才记录该日志，与原 GoTo 的执行顺序一致
                        if (LoginPageType != 0)
                            Logger.Error($"未知的登录页面：{LoginPageType}", LogBehavior.Toast);

                        if (this.RadioLoginType5.Checked)
                        {
                            if (string.IsNullOrEmpty(Settings.Get<string>("CacheMsV2Access")) || _LoginPageTypeOverride >= 0)
                            {
                                Type = PageType.Ms;
                            }
                            else
                            {
                                Type = PageType.MsSkin;
                            }
                            ApplyLoginType(ModLaunch.McLoginType.Ms);
                        }
                        else
                        {
                            Type = PageType.Legacy;
                            ApplyLoginType(ModLaunch.McLoginType.Legacy);
                        }
                        this.PanType.Visibility = Visibility.Visible;
                        this.PanTypeOne.Visibility = Visibility.Collapsed;
                        this.RadioLoginType5.Visibility = Visibility.Visible;
                        this.RadioLoginType0.Visibility = Visibility.Visible;
                        break;
                    }
                case 1: // 仅正版
                    {
                        // 新建档案时一定进入登录流程，而不是已登录后的换肤页
                        if (string.IsNullOrEmpty(Settings.Get<string>("CacheMsV2Access")) || _LoginPageTypeOverride >= 0)
                        {
                            Type = PageType.Ms;
                        }
                        else
                        {
                            Type = PageType.MsSkin;
                        }
                        ApplyLoginType(ModLaunch.McLoginType.Ms);
                        this.PanType.Visibility = Visibility.Collapsed;
                        this.PanTypeOne.Visibility = Visibility.Visible;
                        this.PathTypeOne.Data = (Geometry)new GeometryConverter().ConvertFromString(ModBase.Logo.IconButtonShield);
                        this.LabTypeOne.Text = "正版登录";
                        this.RadioLoginType5.Visibility = Visibility.Visible;
                        this.RadioLoginType0.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 2: // 仅离线
                    {
                        Type = PageType.Legacy;
                        ApplyLoginType(ModLaunch.McLoginType.Legacy);
                        this.PanType.Visibility = Visibility.Collapsed;
                        this.PanTypeOne.Visibility = Visibility.Visible;
                        this.PathTypeOne.Data = (Geometry)new GeometryConverter().ConvertFromString(ModBase.Logo.IconButtonOffline);
                        this.LabTypeOne.Text = "离线登录";
                        break;
                    }
                case 3: // 统一通行证
                    {
                        if (string.IsNullOrEmpty(Settings.Get<string>("CacheNideAccess")) || _LoginPageTypeOverride >= 0)
                        {
                            Type = PageType.Nide;
                        }
                        else
                        {
                            Type = PageType.NideSkin;
                        }
                        ApplyLoginType(ModLaunch.McLoginType.Nide);
                        this.PanType.Visibility = Visibility.Collapsed;
                        this.PanTypeOne.Visibility = Visibility.Visible;
                        this.PathTypeOne.Data = (Geometry)new GeometryConverter().ConvertFromString(ModBase.Logo.IconButtonCard);
                        this.LabTypeOne.Text = "统一通行证登录";
                        break;
                    }
                case 4: // Authlib-Injector
                    {
                        if (string.IsNullOrEmpty(Settings.Get<string>("CacheAuthAccess")) || _LoginPageTypeOverride >= 0)
                        {
                            Type = PageType.Auth;
                        }
                        else
                        {
                            Type = PageType.AuthSkin;
                        }
                        ApplyLoginType(ModLaunch.McLoginType.Auth);
                        this.PanType.Visibility = Visibility.Collapsed;
                        this.PanTypeOne.Visibility = Visibility.Visible;
                        this.PathTypeOne.Data = (Geometry)new GeometryConverter().ConvertFromString(ModBase.Logo.IconButtonCard);
                        this.LabTypeOne.Text = ModMinecraft.McInstanceSelected is null ? Settings.Get<string>("CacheAuthServerName") : Settings.Get<string>("VersionServerAuthName", Instance: ModMinecraft.McInstanceSelected);
                        if (string.IsNullOrEmpty(this.LabTypeOne.Text))
                            this.LabTypeOne.Text = "第三方登录";
                        break;
                    }
            }
            // 非登录态时收起「正版 / 离线」选择控件
            bool TypeChanged = _CurrentPageType != Type;
            _CurrentPageType = Type;
            ApplyAccountChrome();
            // 登录页类型要等上面 switch 算完才知道，登录页的「确认」按钮是否显示依赖它，
            // 类型变化时重绘一次账号区，避免按钮停留在上一页的判断上
            if (TypeChanged && AccountMode == AccountPanelMode.Login)
                RefreshAccountPanel();
            // 刷新页面
            if (PageCurrent == Type)
                return;
            ((dynamic)PageChange(Type, Anim)).Reload(KeepInput);
            MyRadioButton Control = (MyRadioButton)this.FindName("RadioLoginType" + ((int)Settings.Get<ModLaunch.McLoginType>("LoginType")).ToString());
            if (Control is not null)
                Control.Checked = true;
        }

        /// <summary>
        /// 启动页账号区（对齐社区版的三态）：完成态只显示当前账号的大头像与信息；
        /// 选择态列出全部账号（当前账号一行 + 其余账号卡片 + 新建账号按钮）；
        /// 新建档案先选验证类型，再进入对应登录页。复制 / 删除等完整管理仍在设置页「我的账号」。
        /// </summary>
        private enum AccountPanelMode
        {
            Compact,   // 完成态
            Select,    // 选择态
            Login      // 新建档案第二步：登录页
        }

        private AccountPanelMode AccountMode = AccountPanelMode.Compact;
        private PageType _CurrentPageType = PageType.None;
        private int _LastAccountCount = -1;
        // 新建档案对话框里选中的验证类型，取值即 LoginPageType：1 正版 / 4 第三方 / 2 离线
        private int _NewTypeChoice = 0;
        // >= 0 时 RefreshPage 优先采用该登录页类型（新建档案时不写回设置）
        private int _LoginPageTypeOverride = -1;

        // 图标（全部为线框风格：外轮廓 + 内缩轮廓构成空心环，Path 迷你语言的默认填充规则是 EvenOdd）
        // 皮肤：T 恤线框
        private const string LogoCompactSkin =
            "M7.6,2.4 L10,4.2 L14,4.2 L16.4,2.4 L20.6,6.4 L17.6,9.4 L16.2,8.6 L16.2,21.4 L7.8,21.4 L7.8,8.6 L6.4,9.4 L3.4,6.4 Z " +
            "M8.8,4 L10.4,5.5 L13.6,5.5 L15.2,4 L18.6,6.6 L17.1,8.2 L14.8,7.2 L14.8,20 L9.2,20 L9.2,7.2 L6.9,8.2 L5.4,6.6 Z";
        // 新建账号：线框人形 + 细线加号
        private const string LogoNewAccount =
            "M8,1.5 A3.1,3.1 0 1 1 8,7.7 A3.1,3.1 0 1 1 8,1.5 Z " +
            "M8,2.8 A1.8,1.8 0 1 1 8,6.4 A1.8,1.8 0 1 1 8,2.8 Z " +
            "M2,20 C2,14.6 4.6,11.4 8,11.4 C11.4,11.4 14,14.6 14,20 Z " +
            "M4,20 C4,15.9 5.9,12.9 8,12.9 C10.1,12.9 12,15.9 12,20 Z " +
            "M18.2,9.6 L20.2,9.6 L20.2,12 L22.4,12 L22.4,14 L20.2,14 L20.2,16.4 L18.2,16.4 L18.2,14 L16,14 L16,12 L18.2,12 Z";

        private Brush GetBrush(string Key)
        {
            return (Brush)this.FindResource(Key);
        }

        /// <summary>刷新账号区内容与形态。在 RefreshPage 开头调用，保证账号变化立刻反映到启动页。</summary>
        public void RefreshAccountPanel()
        {
            try
            {
                // 先自检：LoginType 指向的账号可能已被删除，而列表里还有别的账号，
                // 此时界面显示与实际启动用的账号会不一致（表现为「启动游戏时不会切换账号」）
                ModAccountList.EnsureCurrent();
                var Accounts = ModAccountList.GetAll();
                var Current = ModAccountList.GetCurrent();
                // GetCurrent 可能返回一个用户名为空的空壳（LoginType 指向的类型已无有效用户名），
                // 这种账号不属于账号列表，回退到列表第一个账号展示
                if (Current is not null && string.IsNullOrEmpty(Current.Name))
                    Current = null;
                if (Current is null && Accounts.Count > 0)
                    Current = Accounts[0];
                // 账号数量变多 = 刚登录成功，回到完成态
                if (_LastAccountCount >= 0 && Accounts.Count > _LastAccountCount)
                {
                    AccountMode = AccountPanelMode.Compact;
                    _LoginPageTypeOverride = -1;
                }
                _LastAccountCount = Accounts.Count;
                // 一个账号都没有时只能先登录
                if (Accounts.Count <= 0)
                {
                    AccountMode = AccountPanelMode.Login;
                    _LoginPageTypeOverride = -1;
                }
                RenderAccountPanel(Accounts, Current);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新启动页账号区失败");
            }
        }

        private void RenderAccountPanel(List<AccountEntry> Accounts, AccountEntry Current)
        {
            bool HasAccount = Accounts.Count > 0;
            this.PanAccountCompact.Visibility = AccountMode == AccountPanelMode.Compact && HasAccount ? Visibility.Visible : Visibility.Collapsed;
            this.PanAccountSelect.Visibility = AccountMode == AccountPanelMode.Select && HasAccount ? Visibility.Visible : Visibility.Collapsed;
            this.PanLoginHost.Visibility = AccountMode == AccountPanelMode.Login ? Visibility.Visible : Visibility.Collapsed;
            // 一个账号都没有时没有可取消的对象
            this.BtnLoginCancel.Visibility = AccountMode == AccountPanelMode.Login && HasAccount ? Visibility.Visible : Visibility.Collapsed;
            // 只有自带「登录」按钮的正版页（PageLoginMs）不需要我们补确认；其余登录页只有输入框，
            // 必须由启动页提供「确认」，否则新建档案流程没有任何提交入口。
            this.BtnLoginConfirm.Visibility = AccountMode == AccountPanelMode.Login && IsSelfConfirmPage(_CurrentPageType) ? Visibility.Visible : Visibility.Collapsed;
            switch (AccountMode)
            {
                case AccountPanelMode.Compact:
                    if (HasAccount)
                        RenderCompact(Current);
                    break;
                case AccountPanelMode.Select:
                    if (HasAccount)
                        RenderSelect(Accounts, Current);
                    break;
                case AccountPanelMode.Login:
                    this.PanLogin.Opacity = 1d;
                    break;
            }
        }

        // 完成态
        private void RenderCompact(AccountEntry Current)
        {
            this.LabCompactName.Text = string.IsNullOrEmpty(Current?.Name) ? "未登录" : Current.Name;
            this.LabCompactInfo.Text = Current is null ? "" : Current.InfoText;
            string Head = Current is null ? "" : GetAccountHead(Current, Current, 128);
            try
            {
                this.ImgCompactHead.Source = string.IsNullOrEmpty(Head) || !FileUtils.Exists(Head) ? null : new MyBitmap(Head);
            }
            catch (Exception ex)
            {
                this.ImgCompactHead.Source = null;
                Logger.Warn(ex, "加载账号大头像失败");
            }
        }

        // 选择态
        private void RenderSelect(List<AccountEntry> Accounts, AccountEntry Current)
        {
            this.PanAccountList.Children.Clear();
            foreach (var Entry in Accounts)
            {
                bool IsCurrent = Current != null && Current.Type == Entry.Type && (Current.Name ?? "") == (Entry.Name ?? "");
                this.PanAccountList.Children.Add(CreateAccountRow(Entry, Current, IsCurrent));
            }
            this.PanAccountList.Children.Add(CreateNewAccountButton());
        }

        private FrameworkElement CreateAccountRow(AccountEntry Entry, AccountEntry Current, bool IsCurrent)
        {
            var Row = new Grid();
            Row.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            Row.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            Row.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            var Head = new Image() { Width = 30, Height = 30, Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(Head, BitmapScalingMode.NearestNeighbor);
            string HeadPath = GetAccountHead(Entry, Current, 64);
            if (!string.IsNullOrEmpty(HeadPath) && FileUtils.Exists(HeadPath))
            {
                try
                {
                    Head.Source = new MyBitmap(HeadPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "加载账号头像失败：" + Entry.Name);
                }
            }
            Grid.SetColumn(Head, 0);
            Row.Children.Add(Head);

            var Name = new TextBlock()
            {
                Text = Entry.Name,
                FontSize = 14,
                FontWeight = IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("ColorBrush1")
            };
            var Info = new TextBlock()
            {
                Text = Entry.InfoText,
                FontSize = 11.5,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("ColorBrushGray2")
            };
            var Texts = new StackPanel() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Texts.Children.Add(Name);
            Texts.Children.Add(Info);
            Grid.SetColumn(Texts, 1);
            Row.Children.Add(Texts);

            var Card = new Border()
            {
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 7, 8, 7),
                Background = IsCurrent ? Brushes.Transparent : GetBrush("ColorBrush7"),
                Child = Row,
                Cursor = Cursors.Hand
            };
            // 当前账号不提供编辑 / 删除，点它本身就是收起列表
            if (!IsCurrent)
            {
                var Buttons = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var BtnEdit = new MyIconButton() { ToolTip = "编辑账号", Logo = ModBase.Logo.IconButtonEdit, Height = 26, Width = 26, Margin = new Thickness(4, 0, 0, 0) };
                SetupSquareButton(BtnEdit);
                BtnEdit.Click += (s, e) => OpenAccountSetupPage();
                var BtnDelete = new MyIconButton() { ToolTip = "删除账号", Logo = ModBase.Logo.IconButtonDelete, Height = 26, Width = 26, Margin = new Thickness(4, 0, 0, 0), Theme = MyIconButton.Themes.Red };
                SetupSquareButton(BtnDelete);
                BtnDelete.Click += (s, e) =>
                {
                    ModAccountList.Delete(Entry);
                    RefreshPage(false, true);
                };
                Buttons.Children.Add(BtnEdit);
                Buttons.Children.Add(BtnDelete);
                Grid.SetColumn(Buttons, 2);
                Row.Children.Add(Buttons);
            }
            Card.MouseLeftButtonUp += (s, e) =>
            {
                if (IsCurrent)
                {
                    SwitchAccountMode(AccountPanelMode.Compact);
                }
                else
                {
                    // 先切形态再换账号，SwitchTo 触发的 RefreshPage 才会直接画成完成态
                    AccountMode = AccountPanelMode.Compact;
                    _LoginPageTypeOverride = -1;
                    ModAccountList.SwitchTo(Entry);
                }
            };
            return Card;
        }

        private FrameworkElement CreateNewAccountButton()
        {
            var Btn = new MyIconButton()
            {
                ToolTip = "新建账号",
                Logo = LogoNewAccount,
                Width = 44,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            SetupSquareButton(Btn);
            Btn.Click += (s, e) => OpenNewTypeDialog();
            return Btn;
        }

        // 新建档案：选择验证类型（社区版是一个浮在主窗口上方的独立弹窗）
        private void OpenNewTypeDialog()
        {
            try
            {
                var Win = new Window()
                {
                    Owner = ModMain.FrmMain,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false,
                    Title = "新建档案"
                };
                var Card = new Border()
                {
                    Background = GetBrush("ColorBrushWhite"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(24, 18, 24, 16),
                    Margin = new Thickness(18),
                    MinWidth = 300,
                    Effect = new DropShadowEffect() { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.22 }
                };
                var Panel = new StackPanel();
                Panel.Children.Add(new TextBlock()
                {
                    Text = "新建档案 - 选择验证类型",
                    FontSize = 16.5,
                    Foreground = GetBrush("ColorBrush2")
                });
                Panel.Children.Add(new Rectangle()
                {
                    Height = 1.5,
                    Margin = new Thickness(0, 8, 0, 0),
                    Fill = GetBrush("ColorBrush2"),
                    Opacity = 0.35
                });
                var List = new StackPanel() { Margin = new Thickness(0, 8, 0, 0) };
                Panel.Children.Add(List);

                var BtnNext = new MyButton() { Text = "继续", Height = 30, MinWidth = 76, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
                var BtnCancel = new MyButton() { Text = "取消", Height = 30, MinWidth = 76 };
                var Buttons = new StackPanel()
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 14, 0, 0)
                };
                Buttons.Children.Add(BtnNext);
                Buttons.Children.Add(BtnCancel);
                Panel.Children.Add(Buttons);
                Card.Child = Panel;
                Win.Content = Card;

                _NewTypeChoice = 0;
                CreateNewTypeRow(List, BtnNext, "正版验证", ModBase.Logo.IconButtonShield, 1);
                CreateNewTypeRow(List, BtnNext, "第三方验证", ModBase.Logo.IconButtonCard, 4);
                CreateNewTypeRow(List, BtnNext, "离线验证", ModBase.Logo.IconButtonOffline, 2);

                bool Accepted = false;
                BtnCancel.Click += (s, e) => Win.Close();
                BtnNext.Click += (s, e) =>
                {
                    if (_NewTypeChoice <= 0)
                        return;
                    Accepted = true;
                    Win.Close();
                };
                Win.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                        Win.Close();
                };
                Win.ShowDialog();
                if (!Accepted)
                    return;
                // 只影响本次显示，不写回「LoginPageType」设置
                _LoginPageTypeOverride = _NewTypeChoice;
                AccountMode = AccountPanelMode.Login;
                RefreshPage(true, true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "打开新建档案对话框失败");
            }
        }

        private void CreateNewTypeRow(StackPanel Host, MyButton BtnNext, string Title, string Logo, int PageType)
        {
            var Icon = new System.Windows.Shapes.Path()
            {
                Data = (Geometry)new GeometryConverter().ConvertFromString(Logo),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = GetBrush("ColorBrush1")
            };
            var Text = new TextBlock()
            {
                Text = Title,
                FontSize = 14,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = GetBrush("ColorBrush1")
            };
            var Row = new Grid();
            Row.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            Row.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(Icon, 0);
            Grid.SetColumn(Text, 1);
            Row.Children.Add(Icon);
            Row.Children.Add(Text);
            var Card = new Border()
            {
                Height = 40,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 0, 2),
                Background = Brushes.Transparent,
                Child = Row,
                Cursor = Cursors.Hand
            };
            Card.MouseLeftButtonUp += (s, e) =>
            {
                _NewTypeChoice = PageType;
                foreach (var Child in Host.Children)
                {
                    if (Child is not Border Other || Other.Child is not Grid OtherRow)
                        continue;
                    var OtherText = OtherRow.Children.OfType<TextBlock>().FirstOrDefault();
                    var OtherIcon = OtherRow.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
                    int OtherType = NewTypeRowTag(OtherText?.Text ?? "");
                    bool IsSelected = OtherType == _NewTypeChoice;
                    Other.Background = IsSelected ? GetBrush("ColorBrush6") : Brushes.Transparent;
                    if (OtherIcon is not null)
                        OtherIcon.Fill = IsSelected ? GetBrush("ColorBrush2") : GetBrush("ColorBrush1");
                    if (OtherText is not null)
                        OtherText.Foreground = IsSelected ? GetBrush("ColorBrush2") : GetBrush("ColorBrush1");
                }
                BtnNext.IsEnabled = true;
            };
            Host.Children.Add(Card);
        }

        private static int NewTypeRowTag(string Title)
        {
            switch (Title)
            {
                case "正版验证":
                    return 1;
                case "第三方验证":
                    return 4;
                case "离线验证":
                    return 2;
                default:
                    return 0;
            }
        }

        /// <summary>正常刷新时才把登录类型写回设置；新建档案的临时选择不落盘，取消后仍是原账号。</summary>
        private void ApplyLoginType(ModLaunch.McLoginType Type)
        {
            if (_LoginPageTypeOverride < 0)
                Settings.Set("LoginType", Type);
        }

        private void SwitchAccountMode(AccountPanelMode Mode, bool KeepInput = false)
        {
            AccountMode = Mode;
            if (Mode != AccountPanelMode.Login)
                _LoginPageTypeOverride = -1;
            RefreshPage(KeepInput, true);
        }

        /// <summary>该登录页自己是否带「登录」按钮（目前只有正版登录页有）。</summary>
        private static bool IsSelfConfirmPage(PageType Type)
        {
            return Type == PageType.Legacy || Type == PageType.Nide || Type == PageType.Auth;
        }

        /// <summary>
        /// 登录页的「确认」：把这些输入框里的账号存进账号库并设为当前账号。
        /// 真正的服务器验证仍在启动游戏时由 ModLaunch 完成，这里只负责建档与切换，
        /// 这样同一种登录方式也能保存多个账号，而不是每次都被第一项回填覆盖。
        /// </summary>
        private void ConfirmLogin()
        {
            try
            {
                string Error;
                string Name;
                string Password = "";
                ModLaunch.McLoginType LoginType;
                switch (_CurrentPageType)
                {
                    case PageType.Legacy:
                        {
                            var Data = (ModLaunch.McLoginLegacy)PageLoginLegacy.GetLoginData();
                            Error = PageLoginLegacy.IsVaild(Data);
                            Name = Data.UserName;
                            LoginType = ModLaunch.McLoginType.Legacy;
                            break;
                        }
                    case PageType.Nide:
                        {
                            var Data = PageLoginNide.GetLoginData();
                            Error = PageLoginNide.IsVaild(Data);
                            Name = Data.UserName;
                            Password = Data.Password;
                            LoginType = ModLaunch.McLoginType.Nide;
                            break;
                        }
                    case PageType.Auth:
                        {
                            var Data = PageLoginAuth.GetLoginData();
                            Error = PageLoginAuth.IsVaild(Data);
                            Name = Data.UserName;
                            Password = Data.Password;
                            LoginType = ModLaunch.McLoginType.Auth;
                            break;
                        }
                    default:
                        // 正版页自己带登录按钮，居中的确认不应该接管
                        return;
                }
                if (!string.IsNullOrEmpty(Error))
                {
                    ModMain.Hint(Error, ModMain.HintType.Red);
                    return;
                }
                ModAccountList.AddAccount(LoginType, Name, Password);
                _LoginPageTypeOverride = -1;
                AccountMode = AccountPanelMode.Compact;
                _LastAccountCount = -1;
                ModMain.Hint("已保存账号：" + Name, ModMain.HintType.Green);
                RefreshPage(false, true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "确认新建档案失败");
            }
        }

        /// <summary>非登录态时隐藏「正版 / 离线」单选与类型标签，把版面完全交给账号区。</summary>
        private void ApplyAccountChrome()
        {
            if (AccountMode == AccountPanelMode.Login)
                return;
            this.PanType.Visibility = Visibility.Collapsed;
            this.PanTypeOne.Visibility = Visibility.Collapsed;
        }

        private void OpenAccountSetupPage()
        {
            try
            {
                ModMain.FrmMain?.PageChange(new FormMain.PageStackData() { Page = FormMain.PageType.Setup }, FormMain.PageSubType.SetupAccount);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "打开「我的账号」页失败");
            }
        }

        private void SetupSquareButton(MyIconButton Btn)
        {
            try
            {
                Btn.CornerRadius = new CornerRadius(6);
                Btn.PanBack.CornerRadius = new CornerRadius(6);
                // 底色必须画在 PanBack 上：点击动画（缩放）作用在 PanBack，
                // 若把底色放在外层 Border，按下时只有中间的小图标在缩，看起来像「点击动画没了」。
                Btn.Background = Brushes.Transparent;
                Btn.PanBack.Background = new ModBase.MyColor(14d, 0d, 0d, 0d);
                // 图标统一为灰色线框，悬停时由 RefreshAnim 动画到主题蓝
                if (Btn.Path.Fill is null)
                    Btn.Path.Fill = GetBrush("ColorBrushGray2");
                Btn.Theme = MyIconButton.Themes.Color;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "设置圆角按钮样式失败");
            }
        }

        private void SetupCompactButton(MyIconButton Btn, string Logo, string ToolTip)
        {
            Btn.Logo = Logo;
            Btn.ToolTip = ToolTip;
            SetupSquareButton(Btn);
        }

        /// <summary>
        /// 取得账号头像图片路径（带缓存）：Size 为头像边长像素。
        /// 当前账号皮肤已加载时优先用真实皮肤，否则退回内置的 Steve / Alex 默认皮肤；
        /// 头像由皮肤纹理的头部区域（基准层 8,8 + 帽子层 40,8）裁出，不做网络请求。
        /// </summary>
        private static string GetAccountHead(AccountEntry Entry, AccountEntry Current, int Size)
        {
            try
            {
                string Texture = "";
                bool IsCurrent = Current != null && Current.Type == Entry.Type && (Current.Name ?? "") == (Entry.Name ?? "");
                if (IsCurrent)
                {
                    if (Entry.Type == ModLaunch.McLoginType.Ms && SkinMs.State == ModBase.LoadState.Finished)
                        Texture = SkinMs.Output;
                    else if (Entry.Type == ModLaunch.McLoginType.Legacy && SkinLegacy.State == ModBase.LoadState.Finished)
                        Texture = SkinLegacy.Output;
                }
                // 皮肤文件缺失时退回内置默认皮肤，保证每行都有头像
                if (string.IsNullOrEmpty(Texture) || (!Texture.StartsWithF(ModBase.PathImage) && !FileUtils.Exists(Texture)))
                {
                    string Sex;
                    if (Entry.Type == ModLaunch.McLoginType.Legacy)
                        Sex = ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(Entry.Name)));
                    else
                        Sex = Entry.Name.GetStableHashCode() % 2 == 0 ? "Steve" : "Alex";
                    Texture = ModBase.PathImage + "Skins/" + Sex + ".png";
                }
                string Folder = ModBase.PathTemp + @"Cache\AccountHead\";
                string File = Folder + Entry.Type + "_" + Entry.Name.GetStableHashCode() + "_" + Texture.GetStableHashCode() + "_" + Size + ".png";
                if (FileUtils.Exists(File))
                    return File;
                if (!System.IO.Directory.Exists(Folder))
                    System.IO.Directory.CreateDirectory(Folder);
                var Skin = new MyBitmap(Texture);
                using (var Head = new System.Drawing.Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var G = System.Drawing.Graphics.FromImage(Head))
                    {
                        G.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        G.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        G.DrawImage(Skin.Pic, new System.Drawing.Rectangle(0, 0, Size, Size), 8, 8, 8, 8, System.Drawing.GraphicsUnit.Pixel);
                        // 64×32 的旧版皮肤没有帽子层，直接跳过
                        if (Skin.Pic.Width >= 64)
                            G.DrawImage(Skin.Pic, new System.Drawing.Rectangle(0, 0, Size, Size), 40, 8, 8, 8, System.Drawing.GraphicsUnit.Pixel);
                    }
                    Head.Save(File, System.Drawing.Imaging.ImageFormat.Png);
                }
                return File;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "生成账号头像失败：" + (Entry?.Name ?? ""));
                return "";
            }
        }

        private void RadioLoginType_Change(object sender, bool raiseByMouse)
        {
            if (raiseByMouse)
                RefreshPage(true, true);
        }

        #endregion

        #region 皮肤

        // 微软正版皮肤
        public static ModLoader.LoaderTask<(string, string), string> SkinMs = new ModLoader.LoaderTask<(string, string), string>("Loader Skin Ms", SkinMsLoad, SkinMsInput, ThreadPriority.AboveNormal);
        private static (string, string) SkinMsInput()
        {
            // 获取名称
            return (Settings.Get<string>("CacheMsV2Name"), Settings.Get<string>("CacheMsV2Uuid"));
        }
        private static void SkinMsLoad(ModLoader.LoaderTask<(string, string), string> Data)
        {
            // 清空已有皮肤
            // 如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
            ModBase.RunInUi(() => { if (ModMain.FrmLoginMsSkin is not null && ModMain.FrmLoginMsSkin.Skin is not null) ModMain.FrmLoginMsSkin.Skin.Clear(); });
            // 获取 Url
            string UserName = Data.Input.Item1;
            string Uuid = Data.Input.Item2;
            if (string.IsNullOrEmpty(UserName))
            {
                Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(UserName))) + ".png";
                Logger.Info("获取微软正版皮肤失败，ID 为空");
                goto Finish;
            }
            try
            {
                string Result = ModMinecraft.McSkinGetAddress(Uuid, "Ms");
                if (Data.IsCanceled)
                    throw new OperationCanceledException("当前任务已取消：" + UserName);
                Result = ModMinecraft.McSkinDownload(Result);
                if (Data.IsCanceled)
                    throw new OperationCanceledException("当前任务已取消：" + UserName);
                Data.Output = Result;
            }
            catch (Exception ex)
            {
                if (ex.IsCanceled())
                {
                    Data.Output = "";
                    return;
                }
                else if (ex.GetDisplay(false).Contains("(429)"))
                {
                    Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(UserName))) + ".png";
                    Logger.Error($"获取正版皮肤失败（{UserName}）：获取皮肤太过频繁，请 5 分钟后再试！", LogBehavior.Toast);
                }
                else if (ex.GetDisplay(false).Contains("未设置自定义皮肤"))
                {
                    Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(UserName))) + ".png";
                    Logger.Info("用户未设置自定义皮肤，跳过皮肤加载");
                }
                else
                {
                    Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(UserName))) + ".png";
                    Logger.Error(ex, $"获取微软正版皮肤失败（{UserName}）", LogBehavior.Toast);
                }
            }

        Finish:
            ;

            // 刷新显示
            if (ModMain.FrmLoginMsSkin is not null)
            {
                ModBase.RunInUi(ModMain.FrmLoginMsSkin.Skin.Load);
            }
            else if (!Data.IsCanceled) // 如果已经中断，Input 也被清空，就不会再次刷新
            {
                Data.Input = default; // 清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
            }
        }

        // 离线皮肤
        public static ModLoader.LoaderTask<(string, string), string> SkinLegacy = new ModLoader.LoaderTask<(string, string), string>("Loader Skin Legacy", SkinLegacyLoad, SkinLegacyInput, ThreadPriority.AboveNormal);
        private static (string, string) SkinLegacyInput()
        {
            // 根据类型判断输入
            int Type = Settings.Get<int>("LaunchSkinType");
            switch (Type)
            {
                case 0:
                    {
                        if (ModMain.FrmLoginLegacy?.IsReloaded == true)
                        {
                            return ("0", ModMain.FrmLoginLegacy.ComboName.Text.Trim() ?? "");
                        }
                        else if (string.IsNullOrEmpty(Settings.Get<string>("LoginLegacyName")))
                        {
                            return ("0", "");
                        }
                        else
                        {
                            return ("0", Settings.Get<string>("LoginLegacyName").ToString().BeforeFirst("¨") ?? "");
                        }
                    }
                case 3:
                    {
                        return ("3", Settings.Get<string>("LaunchSkinID"));
                    }

                default:
                    {
                        return (Type.ToString(), "");
                    }
            }
        }
        private static void SkinLegacyLoad(ModLoader.LoaderTask<(string, string), string> Data)
        {
            // 清空已有皮肤
            ModBase.RunInUi(() => { if (ModMain.FrmLoginLegacy is not null && ModMain.FrmLoginLegacy.Skin is not null) ModMain.FrmLoginLegacy.Skin.Clear(); });
            // 获取 Url
            // VB 原文在 Case 4 中用 GoTo UseDefault 跳进 Case 1 的赋值语句，C# 不允许跨 case 跳转，
            // 改为标记位 + switch 后的顺序 if：仍然只在 switch 内决定走哪条路径，赋值内容与原文相同
            bool UseDefaultSkin = false;
            switch (Conversions.ToInteger(Data.Input.Item1))
            {
                case 0: // 默认
                    {
                        Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(Data.Input.Item2))) + ".png";
                        break;
                    }
                case 1: // Steve
                    {
                        UseDefaultSkin = true;
                        break;
                    }
                case 2: // Alex
                    {
                        Data.Output = ModBase.PathImage + "Skins/Alex.png";
                        break;
                    }
                case 3: // 正版
                    {
                        string ID = Data.Input.Item2;
                        try
                        {
                            if (ID.Count() < 2)
                            {
                                Data.Output = ModBase.PathImage + "Skins/Steve.png";
                            }
                            else
                            {
                                string Result = Conversions.ToString(ModLaunch.McLoginMojangUuid(ID, true));
                                if (Data.IsCanceled)
                                    throw new OperationCanceledException("当前任务已取消：" + ID);
                                Result = ModMinecraft.McSkinGetAddress(Result, "Mojang");
                                if (Data.IsCanceled)
                                    throw new OperationCanceledException("当前任务已取消：" + ID);
                                Result = ModMinecraft.McSkinDownload(Result);
                                if (Data.IsCanceled)
                                    throw new OperationCanceledException("当前任务已取消：" + ID);
                                Data.Output = Result;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.IsCanceled())
                            {
                                Data.Output = "";
                                return;
                            }
                            else if (ex.GetDisplay(false).Contains("(429)"))
                            {
                                Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(ID))) + ".png";
                                Logger.Info($"获取离线登录使用的正版皮肤失败（{ID}）：获取皮肤太过频繁，请 5 分钟后再试！");
                            }
                            else
                            {
                                Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(ID))) + ".png";
                                Logger.Warn(ex, $"获取离线登录使用的正版皮肤失败（{ID}）");
                            }
                        }

                        break;
                    }
                case 4: // 自定义
                    {
                        if (!FileUtils.Exists(Paths.AppDataThenName + "CustomSkin.png"))
                        {
                            ModMain.Hint("未找到离线皮肤自定义文件，可能它已被删除。PCL Next 将使用默认的 Steve 皮肤！");
                            Settings.Set("LaunchSkinType", 1);
                            UseDefaultSkin = true; // 等价于原文的 GoTo UseDefault
                            break;
                        }
                        Data.Output = Paths.AppDataThenName + "CustomSkin.png";
                        break;
                    }
            }
            if (UseDefaultSkin)
                Data.Output = ModBase.PathImage + "Skins/Steve.png";
            // 刷新显示
            if (ModMain.FrmLoginLegacy is not null)
            {
                ModBase.RunInUi(ModMain.FrmLoginLegacy.Skin.Load);
            }
            else if (!Data.IsCanceled) // 如果已经中断，Input 也被清空，就不会再次刷新
            {
                Data.Input = default; // 清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
            }
        }

        // 统一通行证皮肤
        public static ModLoader.LoaderTask<(string, string), string> SkinNide = new ModLoader.LoaderTask<(string, string), string>("Loader Skin Nide", SkinNideLoad, SkinNideInput, ThreadPriority.AboveNormal);
        private static (string, string) SkinNideInput()
        {
            // 获取名称
            return (Settings.Get<string>("CacheNideName"), Settings.Get<string>("CacheNideUuid"));
        }
        private static void SkinNideLoad(ModLoader.LoaderTask<(string, string), string> Data)
        {
            // 清空已有皮肤
            // 如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
            ModBase.RunInUi(() => { if (ModMain.FrmLoginNideSkin is not null && ModMain.FrmLoginNideSkin.Skin is not null) ModMain.FrmLoginNideSkin.Skin.Clear(); });
            // 获取 Url
            string UserName = Data.Input.Item1;
            string Uuid = Data.Input.Item2;
            if (string.IsNullOrEmpty(UserName))
            {
                Data.Output = ModBase.PathImage + "Skins/" + ModMinecraft.McSkinSex(Conversions.ToString(ModLaunch.McLoginLegacyUuid(UserName))) + ".png";
                Logger.Info("获取统一通行证皮肤失败，ID 为空");
                goto Finish;
            }
            try
            {
                string Result = ModMinecraft.McSkinGetAddress(Uuid, "Nide");
                if (Data.IsCanceled)
                    throw new OperationCanceledException("当前任务已取消：" + UserName);
                Result = ModMinecraft.McSkinDownload(Result);
                if (Data.IsCanceled)
                    throw new OperationCanceledException("当前任务已取消：" + UserName);
                Data.Output = Result;
            }
            catch (Exception ex)
            {
                if (ex.IsCanceled())
                {
                    Data.Output = "";
                    return;
                }
                else if (ex.GetDisplay(false).Contains("(429)"))
                {
                    Data.Output = ModBase.PathImage + "Skins/Steve.png";
                    Logger.Error($"获取统一通行证皮肤失败（{UserName}）：获取皮肤太过频繁，请 5 分钟后再试！", LogBehavior.Toast);
                }
                else if (ex.GetDisplay(false).Contains("未设置自定义皮肤"))
                {
                    Data.Output = ModBase.PathImage + "Skins/Steve.png";
                    Logger.Info("用户未设置自定义皮肤，跳过皮肤加载");
                }
                else
                {
                    Data.Output = ModBase.PathImage + "Skins/Steve.png";
                    Logger.Error(ex, $"获取统一通行证皮肤失败（{UserName}）", LogBehavior.Toast);
                }
            }

        Finish:
            ;

            // 刷新显示
            if (ModMain.FrmLoginNideSkin is not null)
            {
                ModBase.RunInUi(ModMain.FrmLoginNideSkin.Skin.Load);
            }
            else if (!Data.IsCanceled) // 如果已经中断，Input 也被清空，就不会再次刷新
            {
                Data.Input = default; // 清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
            }
        }

        // Authlib-Injector 皮肤
        public static ModLoader.LoaderTask<(string, string), string> SkinAuth = new ModLoader.LoaderTask<(string, string), string>("Loader Skin Auth", SkinAuthLoad, SkinAuthInput, ThreadPriority.AboveNormal);
        private static (string, string) SkinAuthInput()
        {
            // 获取名称
            return (Settings.Get<string>("CacheAuthName"), Settings.Get<string>("CacheAuthUuid"));
        }
        private static void SkinAuthLoad(ModLoader.LoaderTask<(string, string), string> Data)
        {
            // 清空已有皮肤
            // 如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
            ModBase.RunInUi(() => { if (ModMain.FrmLoginAuthSkin is not null && ModMain.FrmLoginAuthSkin.Skin is not null) ModMain.FrmLoginAuthSkin.Skin.Clear(); });
            // 获取 Url
            string UserName = Data.Input.Item1;
            string UUID = Data.Input.Item2;
            if (string.IsNullOrEmpty(UserName))
            {
                Data.Output = ModBase.PathImage + "Skins/Steve.png";
                Logger.Info("获取 Authlib-Injector 皮肤失败，ID 为空");
                goto Finish;
            }
            try
            {
                string Result = ModMinecraft.McSkinGetAddress(UUID, "Auth");
                if (Data.IsCanceled)
                    throw new OperationCanceledException("当前任务已取消：" + UserName);
                Result = ModMinecraft.McSkinDownload(Result);
                if (Data.IsCanceled)
                    throw new OperationCanceledException("当前任务已取消：" + UserName);
                Data.Output = Result;
            }
            catch (Exception ex)
            {
                if (ex.IsCanceled())
                {
                    Data.Output = "";
                    return;
                }
                else if (ex.GetDisplay(false).Contains("(429)"))
                {
                    Data.Output = ModBase.PathImage + "Skins/Steve.png";
                    Logger.Error($"获取 Authlib-Injector 皮肤失败（{UserName}）：获取皮肤太过频繁，请 5 分钟后再试！", LogBehavior.Toast);
                }
                else if (ex.GetDisplay(false).Contains("未设置自定义皮肤"))
                {
                    Data.Output = ModBase.PathImage + "Skins/Steve.png";
                    Logger.Info("用户未设置自定义皮肤，跳过皮肤加载");
                }
                else
                {
                    Data.Output = ModBase.PathImage + "Skins/Steve.png";
                    Logger.Error(ex, $"获取 Authlib-Injector 皮肤失败（{UserName}）", LogBehavior.Toast);
                }
            }

        Finish:
            ;

            // 刷新显示
            if (ModMain.FrmLoginAuthSkin is not null)
            {
                ModBase.RunInUi(ModMain.FrmLoginAuthSkin.Skin.Load);
            }
            else if (!Data.IsCanceled) // 如果已经中断，Input 也被清空，就不会再次刷新
            {
                Data.Input = default; // 清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
            }
        }

        // 全部皮肤加载器
        // 需要放在其中元素的后面，否则会因为它提前被加载而莫名其妙变成 Nothing
        public static List<ModLoader.LoaderTask<(string, string), string>> SkinLoaders = new List<ModLoader.LoaderTask<(string, string), string>>() { SkinMs, SkinLegacy, SkinNide, SkinAuth };

        #endregion

        // 版本选择按钮
        private void BtnVersion_Click()
        {
            if (ModLaunch.McLaunchLoader.State == ModBase.LoadState.Loading)
                return;
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.InstanceSelect);
        }
        // 启动按钮
        public void LaunchButtonClick()
        {
            if (ModLaunch.McLaunchLoader.State == ModBase.LoadState.Loading || !this.BtnLaunch.IsEnabled || ModMain.FrmMain.PageRight is not null && ModMain.FrmMain.PageRight.PageState != MyPageRight.PageStates.ContentStay && ModMain.FrmMain.PageRight.PageState != MyPageRight.PageStates.ContentEnter)
                return;
            // 愚人节处理
            if (ModMain.IsAprilEnabled && !ModMain.IsAprilGiveup)
            {
                ModSecret.ThemeUnlock(12, false, "隐藏主题 滑稽彩 已解锁！");
                ModMain.IsAprilGiveup = true;
                Settings.Set("AprilYear", DateTime.Now.Year);
                ModMain.FrmLaunchLeft.AprilScaleTrans.ScaleX = 1d;
                ModMain.FrmLaunchLeft.AprilScaleTrans.ScaleY = 1d;
                ModMain.FrmLaunchLeft.AprilPosTrans.X = 0d;
                ModMain.FrmLaunchLeft.AprilPosTrans.Y = 0d;
                ModMain.FrmMain.BtnExtraApril.ShowRefresh();
            }
            // 实际的启动
            if (this.BtnLaunch.Text == "启动游戏")
            {
                ModLaunch.McLaunchStart();
            }
            else if (this.BtnLaunch.Text == "下载游戏")
            {
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall);
            }
        }
        private int BtnLaunchState = 0;
        private ModMinecraft.McInstance BtnLaunchInstance = null;
        public void RefreshButtonsUI()
        {
            if (!this.BtnLaunch.IsLoaded)
                return;
            // 获取当前状态
            int CurrentState;
            if (!IsLoadFinished || ModMinecraft.McInstanceListLoader.State == ModBase.LoadState.Loading || ModMinecraft.McFolderListLoader.State == ModBase.LoadState.Loading)
            {
                CurrentState = 0;
            }
            else if (ModMinecraft.McInstanceSelected is null)
            {
                if (ModDownloadPolicy.RestrictMcDownload || Settings.Get<bool>("UiHiddenPageDownload") && !PageSetupUI.HiddenForceShow)
                {
                    CurrentState = 1;
                }
                else
                {
                    CurrentState = 2;
                }
            }
            else
            {
                CurrentState = 3;
            }
            // 更新状态
            if (CurrentState == BtnLaunchState && ((ModMinecraft.McInstanceSelected is null ? "" : ModMinecraft.McInstanceSelected.PathVersion) ?? "") == ((BtnLaunchInstance is null ? "" : BtnLaunchInstance.PathVersion) ?? ""))
                goto ExitRefresh;
            BtnLaunchInstance = ModMinecraft.McInstanceSelected;
            BtnLaunchState = CurrentState;
            switch (CurrentState)
            {
                case 0:
                    {
                        Logger.Info("启动按钮：正在加载 Minecraft 版本");
                        ModMain.FrmLaunchLeft.BtnLaunch.Text = "正在加载";
                        ModMain.FrmLaunchLeft.BtnLaunch.IsEnabled = false;
                        ModMain.FrmLaunchLeft.LabVersion.Text = "正在加载中，请稍候";
                        ModMain.FrmLaunchLeft.BtnVersion.IsEnabled = false;
                        ModMain.FrmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 1:
                    {
                        Logger.Info("启动按钮：无 Minecraft 版本，下载已禁用");
                        ModMain.FrmLaunchLeft.BtnLaunch.Text = "启动游戏";
                        ModMain.FrmLaunchLeft.BtnLaunch.IsEnabled = false;
                        ModMain.FrmLaunchLeft.LabVersion.Text = "未找到可用的游戏版本";
                        ModMain.FrmLaunchLeft.BtnVersion.IsEnabled = true;
                        ModMain.FrmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 2:
                    {
                        Logger.Info("启动按钮：无 Minecraft 版本，要求下载");
                        ModMain.FrmLaunchLeft.BtnLaunch.Text = "下载游戏";
                        ModMain.FrmLaunchLeft.BtnLaunch.IsEnabled = true;
                        ModMain.FrmLaunchLeft.LabVersion.Text = "未找到可用的游戏版本";
                        ModMain.FrmLaunchLeft.BtnVersion.IsEnabled = true;
                        ModMain.FrmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 3:
                    {
                        Logger.Info($"启动按钮：Minecraft 版本：{ModMinecraft.McInstanceSelected.PathVersion}");
                        ModMain.FrmLaunchLeft.BtnLaunch.Text = "启动游戏";
                        ModMain.FrmLaunchLeft.BtnVersion.IsEnabled = true;
                        ModMain.FrmLaunchLeft.BtnLaunch.IsEnabled = true;
                        ModMain.FrmLaunchLeft.LabVersion.Text = ModMinecraft.McInstanceSelected.Name;
                        break;
                    }
                    // FrmLaunchLeft.BtnMore.Visibility = Visibility.Visible '由功能隐藏设置修改
            }

        ExitRefresh:
            ;

            // 功能隐藏
            ModMain.FrmLaunchLeft.BtnVersion.Visibility = !PageSetupUI.HiddenForceShow && Settings.Get<bool>("UiHiddenFunctionSelect") ? Visibility.Collapsed : Visibility.Visible;
            if (CurrentState == 3)
            {
                ModMain.FrmLaunchLeft.BtnMore.Visibility = ModMain.FrmLaunchLeft.BtnVersion.Visibility;
            }
        }
        // 取消按钮
        public void BtnCancel_Click()
        {
            if (ModLaunch.McLaunchLoaderReal is not null)
            {
                ModLaunch.McLaunchLoaderReal.Cancel();
                ModLaunch.McLaunchLog("已取消启动");
                try
                {
                    if (ModLaunch.McLaunchWatcher is not null)
                    {
                        ModLaunch.McLaunchWatcher.Kill();
                    }
                    else if (ModLaunch.McLaunchProcess is not null)
                    {
                        if (!ModLaunch.McLaunchProcess.HasExited)
                            ModLaunch.McLaunchProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "取消启动结束进程失败", LogBehavior.Toast);
                }
            }
        }
        // 版本设置按钮
        private void BtnMore_Click()
        {
            if (ModLaunch.McLaunchLoader.State == ModBase.LoadState.Loading)
                return;
            if (ModMinecraft.McInstanceLock.IsLocked(ModMinecraft.McInstanceSelected))
            {
                ModMain.Hint("该版本正在被修改，暂时无法进入版本设置！", ModMain.HintType.Red);
                return;
            }
            ModMinecraft.McInstanceSelected.Load();
            PageInstanceLeft.Instance = ModMinecraft.McInstanceSelected;
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.InstanceSetup, 0);
        }
        /// <summary>
    /// 每 0.1s 执行一次，刷新启动的数据 UI 显示。
    /// </summary>
        public void LaunchingRefresh()
        {
            try
            {
                if (ModLaunch.McLaunchLoaderReal.State == ModBase.LoadState.Canceled)
                    return;
                // 阶段状态获取
                bool IsLaunched = false; // 是否已经启动游戏，只是在等待窗口
                do
                {
                    try
                    {
                        bool exitTry = false;
                        foreach (var Loader in ModLaunch.McLaunchLoaderReal.GetLoaderList(false))
                        {
                            if (Loader.State == ModBase.LoadState.Loading || Loader.State == ModBase.LoadState.Waiting)
                            {
                                this.LabLaunchingStage.Text = Loader.Name;
                                IsLaunched = Loader.Name == "等待游戏窗口出现" || Loader.Name == "结束处理";
                                exitTry = true;
                                break;
                            }
                        }

                        if (exitTry)
                        {
                            break;
                        }
                        this.LabLaunchingStage.Text = "已完成";
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "获取是否启动完成失败，可能是由于启动状态改变导致集合已修改");
                        return;
                    }
                }
                while (false);
                this.LabLaunchingTitle.Text = IsLaunched || ModLaunch.McLaunchLoaderReal.State == ModBase.LoadState.Finished ? "已启动游戏" : ModLaunch.CurrentLaunchOptions.SaveBatch is null ? "正在启动游戏" : "正在导出启动脚本";
                if (ModAnimation.AniIsRun("Launch State Page"))
                    IsLaunched = false; // 等待页面切换动画完成
                                        // 更新进度
                double ActualProgress = ModLaunch.McLaunchLoaderReal.Progress;
                if (ActualProgress >= ShowProgress)
                    ShowProgress += (ActualProgress - ShowProgress) * 0.1d + 0.0025d; // 向实际进度靠一点
                if (ActualProgress <= ShowProgress)
                    ShowProgress = ActualProgress; // 原来或处理后变得比实际进度高，直接回退
                if (IsLaunched)
                    ShowProgress = 1d; // 如果已经完成了，就不卖关子了
                this.LabLaunchingProgress.Text = (ShowProgress * 100d).ToString("0.00") + " %";
                // 更新下载速度
                bool HasLaunchDownloader = false;
                try
                {
                    if (ModNet.NetManager.Speed == 0L && this.LabLaunchingDownload.Visibility == Visibility.Collapsed)
                    {
                        // 可能只是在检查文件，没有实际下载
                        HasLaunchDownloader = false;
                    }
                    else
                    {
                        foreach (var Loader in ModNet.NetManager.Tasks)
                        {
                            if (Loader.RealParent is not null && Loader.RealParent.Name == "Minecraft 启动" && Loader.State == ModBase.LoadState.Loading)
                                HasLaunchDownloader = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "获取 Minecraft 启动下载器失败，可能是因为启动被取消");
                    HasLaunchDownloader = false;
                }
                this.LabLaunchingDownload.Text = StringUtils.FormatByteSize(ModNet.NetManager.Speed) + "/s";
                // 进度改变动画
                var AnimList = new List<ModAnimation.AniData>() { ModAnimation.AaGridLengthWidth(this.ProgressLaunchingFinished, ShowProgress - this.ProgressLaunchingFinished.Width.Value, 130, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaGridLengthWidth(this.ProgressLaunchingUnfinished, 1d - ShowProgress - this.ProgressLaunchingUnfinished.Width.Value, 130, Ease: new ModAnimation.AniEaseOutFluent()) };
                if (HasLaunchDownloader == (this.LabLaunchingDownload.Visibility == Visibility.Collapsed)) // IsDownloadStateChanged
                {
                    this.LabLaunchingDownload.Visibility = Visibility.Visible;
                    this.LabLaunchingDownloadLeft.Visibility = Visibility.Visible;
                    AnimList.AddRange(new[] { ModAnimation.AaOpacity(this.LabLaunchingDownload, (double)(HasLaunchDownloader ? 1 : 0) - this.LabLaunchingDownload.Opacity, 90), ModAnimation.AaOpacity(this.LabLaunchingDownloadLeft, (HasLaunchDownloader ? 0.5d : 0d) - this.LabLaunchingDownloadLeft.Opacity, 90), ModAnimation.AaCode(() => { if (!HasLaunchDownloader) { this.LabLaunchingDownload.Visibility = Visibility.Collapsed; this.LabLaunchingDownloadLeft.Visibility = Visibility.Collapsed; } }, 110) });
                }
                if (!IsLaunched == (this.LabLaunchingProgress.Visibility == Visibility.Collapsed)) // IsProgressStateChanged
                {
                    this.LabLaunchingProgress.Visibility = Visibility.Visible;
                    this.LabLaunchingProgressLeft.Visibility = Visibility.Visible;
                    if (IsLaunched)
                        this.PanLaunchingHint.Visibility = Visibility.Visible;
                    AnimList.AddRange(new[] { ModAnimation.AaOpacity(this.LabLaunchingProgress, (double)(!IsLaunched ? 1 : 0) - this.LabLaunchingProgress.Opacity, 90), ModAnimation.AaOpacity(this.LabLaunchingProgressLeft, (!IsLaunched ? 0.5d : 0d) - this.LabLaunchingProgressLeft.Opacity, 90), ModAnimation.AaOpacity(this.PanLaunchingHint, (double)(IsLaunched ? 1 : 0) - this.PanLaunchingHint.Opacity, 90) });
                }
                ModAnimation.AniStart(AnimList, "Launching Progress");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "刷新启动信息失败");
            }
        }
        private double ShowProgress = 0d;
        // 尺寸改变动画
        private bool IsWidthAnimating = false;
        private double ActualUsedWidth;
        private void PanLaunchingInfo_SizeChangedW(object sender, SizeChangedEventArgs e)
        {
            double DeltaWidth = e.NewSize.Width - e.PreviousSize.Width;
            if (e.PreviousSize.Width == 0d || IsWidthAnimating || Math.Abs(DeltaWidth) < 1d || this.PanLaunchingInfo.ActualWidth == 0d)
                return;
            ModAnimation.AniStart(new[] {
            ModAnimation.AaWidth(this.PanLaunchingInfo, DeltaWidth, 180, Ease: new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaCode(() =>
                {
                       IsWidthAnimating = false;
                       this.PanLaunchingInfo.Width = ActualUsedWidth;
                   }, After: true)
        }, "Launching Info Width");
            IsWidthAnimating = true;
            ActualUsedWidth = this.PanLaunchingInfo.Width;
            this.PanLaunchingInfo.Width = e.PreviousSize.Width;
        }
        private bool IsHeightAnimating = false;
        private double ActualUsedHeight;
        private void PanLaunchingInfo_SizeChangedH(object sender, SizeChangedEventArgs e)
        {
            double DeltaHeight = e.NewSize.Height - e.PreviousSize.Height;
            if (e.PreviousSize.Height == 0d || IsHeightAnimating || Math.Abs(DeltaHeight) < 1d || this.PanLaunchingInfo.ActualHeight == 0d)
                return;
            ModAnimation.AniStart(new[] {
            ModAnimation.AaHeight(this.PanLaunchingInfo, DeltaHeight, 180, Ease: new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaCode(() =>
                {
                       IsHeightAnimating = false;
                       this.PanLaunchingInfo.Height = ActualUsedHeight;
                   }, After: true)
        }, "Launching Info Height");
            IsHeightAnimating = true;
            ActualUsedHeight = this.PanLaunchingInfo.Height;
            this.PanLaunchingInfo.Height = e.PreviousSize.Height;
        }

    }
}