using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public partial class PageInstanceSetup
    {

        private bool LoadedFirst = false;

        public PageInstanceSetup()
        {
            InitializeComponent();
            this.SliderRamCustom.Change += (p1, p2) => RefreshRam((bool)p1);
            this.RadioRamType0.Check += (p1, p2) => RefreshRam((bool)p1);
            this.RadioRamType1.Check += (p1, p2) => RefreshRam((bool)p1);
            this.RadioRamType2.Check += (p1, p2) => RefreshRam((bool)p1);
            this.RectRamGame.SizeChanged += (p1, p2) => RefreshRamText();
            this.RectRamEmpty.SizeChanged += (p1, p2) => RefreshRamText();
            this.LabRamGame.SizeChanged += (p1, p2) => RefreshRamText();
            this.TextServerEnter.TextChanged += (p1, p2) => TextServerEnter_Change();
            this.ComboServerLogin.SelectionChanged += (p1, p2) => ComboServerLogin_Changed();
            this.TextServerNide.ValidatedTextChanged += (p1, p2) => ComboServerLogin_Changed();
            this.TextServerAuthServer.ValidatedTextChanged += (p1, p2) => ComboServerLogin_Changed();
            this.TextServerAuthRegister.ValidatedTextChanged += (p1, p2) => ComboServerLogin_Changed();
            this.BtnServerAuthLittle.Click += (p1, p2) => BtnServerAuthLittle_Click((object)p1, (EventArgs)p2);
            this.ComboArgumentJava.SelectionChanged += (p1, p2) => ReloadCurrentJava();
            this.TextArgumentJavaRange.ValidatedTextChanged += (p1, p2) => ReloadCurrentJava();
            this.ComboArgumentJava.SelectionChanged += (p1, p2) => UpdateJavaRelatedUi();
            this.TextArgumentJavaRange.TextChanged += (p1, p2) => UpdateJavaRelatedUi();
            this.HintArgumentJava.Click += (p1, p2) => HintArgumentJava_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnArgumentJavaSearch.Click += (p1, p2) => BtnAdvanceJavaSearch_Click();
            this.ComboArgumentIndieV2.SelectionChanged += (p1, p2) => ComboArgumentIndieV2_SelectionChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.TextAdvanceRun.TextChanged += (p1, p2) => TextAdvanceRun_TextChanged((object)p1, (TextChangedEventArgs)p2);
            this.BtnSwitch.Click += (p1, p2) => BtnSwitch_Click((object)p1, (MouseButtonEventArgs)p2);
            this.TextAdvanceJvm.TextChanged += (p1, p2) => ReplaceEnter((MyTextBox)p1, (TextChangedEventArgs)p2);
            this.TextAdvanceGame.TextChanged += (p1, p2) => ReplaceEnter((MyTextBox)p1, (TextChangedEventArgs)p2);
            this.Loaded += PageSetupSystem_Loaded;
        }
        private void PageSetupSystem_Loaded(object sender, RoutedEventArgs e)
        {

            // 重复加载部分
            this.PanBack.ScrollToHome();
            RefreshRam(false);

            // 由于各个版本不同，每次都需要重新加载
            ModAnimation.AniControlEnabled += 1;
            Reload();
            ModAnimation.AniControlEnabled -= 1;

            // 非重复加载部分
            if (LoadedFirst)
                return;
            LoadedFirst = true;

            // Java 加载器同步
            PageInstanceLeft.CurrentJavaWorker.Started += UpdateJavaRelatedUi;
            PageInstanceLeft.CurrentJavaWorker.Stopped += UpdateJavaRelatedUi;

            // 版本范围校验
            this.TextArgumentJavaRange.ValidateRules = new System.Collections.ObjectModel.Collection<ValidateType>() { new ValidateFunc((Str) => { try { if (string.IsNullOrWhiteSpace(Str)) return "不能为空"; var Range = ValueRange<Version>.FromString(Str, s => StringUtils.ParseVersionWithDefaults(s)); if (Range.IsEmpty()) return "范围下限的版本号比上限更高，导致该范围无法匹配到任何版本"; if (Range.HasLower && Range.Lower.Major <= 1 && (Range.Lower.Minor > 0 || Range.Lower.Build > 0 || Range.Lower.Revision > 0)) return "不应使用 1.x 格式进行匹配（例如，若想匹配 Java 17，应填写 17 而非 1.17）"; if (Range.HasUpper && Range.Upper.Major <= 1 && (Range.Upper.Minor > 0 || Range.Upper.Build > 0 || Range.Upper.Revision > 0)) return "不应使用 1.x 格式进行匹配（例如，若想匹配 Java 17，应填写 17 而非 1.17）"; if (Range.HasUpper && Range.Upper.Major <= 4) return "该范围所要求的 Java 版本过低"; if (Range.HasUpper && Range.IsUpperInclusive && Range.Upper.Minor <= 0 && Range.Upper.Build <= 0) return $"右侧闭区间的意图并不明确。如果不想允许 Java {Range.Upper.Major}，请改为 {ModBase.vbLQ}{Range.Upper.Major}){ModBase.vbRQ}。如果想允许 Java {Range.Upper.Major}，请改为 {ModBase.vbLQ}{Range.Upper.Major + 1}){ModBase.vbRQ}。"; if (Range.Intersect(ValueRange<Version>.OpenClosed(new Version(5, 0), new Version(99, 0))).IsEmpty()) return "该范围无法匹配到任何常见的 Java"; return ""; } catch (Exception ex) { return ex.Message; } }) };

            // 内存自动刷新
            var timer = new System.Windows.Threading.DispatcherTimer() { Interval = new TimeSpan(0, 0, 0, 1) };
            timer.Tick += (_, __) => RefreshRam();
            timer.Start();

        }
        public void Reload()
        {
            try
            {

                // 启动参数
                string _unused = PageInstanceLeft.Instance.PathIndie; // 触发自动判定
                this.ComboArgumentIndieV2.SelectedIndex = Settings.Get<bool>("VersionArgumentIndieV2", Instance: PageInstanceLeft.Instance) ? 0 : 1;
                UpdateJavaList();

                // 服务器
                this.ComboServerLogin.SelectedIndex = Settings.Get<int>("VersionServerLogin", Instance: PageInstanceLeft.Instance);
                ComboServerLoginLast = this.ComboServerLogin.SelectedIndex;
                UpdateServerLoginUI();

                // 高级设置
                if (Settings.Get<int>("VersionAdvanceAssets", Instance: PageInstanceLeft.Instance) == 2)
                {
                    Logger.Info("已迁移老版本的关闭文件校验设置");
                    Settings.Reset("VersionAdvanceAssets", Instance: PageInstanceLeft.Instance);
                    Settings.Set("VersionAdvanceAssetsV2", true, Instance: PageInstanceLeft.Instance);
                }

                SettingService.RefreshSettings(this);

                // 游戏内存
                OnVersionRamTypeChanged(Settings.Get<int>("VersionRamType", Instance: PageInstanceLeft.Instance));
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "重载版本独立设置时出错");
            }
        }

        // 初始化
        public void Reset()
        {
            try
            {

                SettingService.ResetSettings(this);

                Settings.Reset("VersionServerLogin", Instance: PageInstanceLeft.Instance);
                Settings.Reset("VersionArgumentIndieV2", Instance: PageInstanceLeft.Instance);
                Settings.Reset("VersionAdvanceAssets", Instance: PageInstanceLeft.Instance);
                Configs.JavaForced.Reset(PageInstanceLeft.Instance.Config);
                ModJava.JavaListRefreshWorker.Start();

                Logger.Info("已初始化版本独立设置");
                ModMain.Hint("已初始化版本独立设置！", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化版本独立设置失败", LogBehavior.Alert);
            }

            Reload();
        }

        #region 游戏内存

        public static void OnVersionRamTypeChanged(int Type)
        {
            if (ModMain.FrmInstanceSetup is null)
                return;
            ModMain.FrmInstanceSetup.RamType(Type);
        }

        public void RamType(int Type)
        {
            if (this.SliderRamCustom is null)
                return;
            this.SliderRamCustom.IsEnabled = Type == 1;
        }

        /// <summary>
    /// 刷新 UI 上的 RAM 显示。
    /// </summary>
        public void RefreshRam(bool ShowAnim)
        {
            if (this.LabRamGame is null || this.LabRamUsed is null || this.LabRamTotal is null || this.SliderRamCustom is null ||
                this.ColumnRamUsed is null || this.ColumnRamGame is null || this.ColumnRamEmpty is null ||
                ModMain.FrmMain is null || ModMain.FrmInstanceLeft is null || PageInstanceLeft.Instance is null ||
                ModMain.FrmMain.PageCurrent != (FormMain.PageStackData)FormMain.PageType.InstanceSetup ||
                ModMain.FrmInstanceLeft.PageID != FormMain.PageSubType.InstanceSetup)
                return;
            // 获取内存情况
            double RamGame = Math.Round(GetRam(PageInstanceLeft.Instance), 5);
            double RamTotal = Math.Round(My.Computer.Info.TotalPhysicalMemory / 1024d / 1024d / 1024d, 1);
            double RamAvailable = Math.Round(My.Computer.Info.AvailablePhysicalMemory / 1024d / 1024d / 1024d, 1);
            double RamGameActual = Math.Round(Math.Min(RamGame, RamAvailable), 5);
            double RamUsed = Math.Round(RamTotal - RamAvailable, 5);
            double RamEmpty = Math.Round((RamTotal - RamUsed - RamGame).Clamp(0d, 1000d), 1);
            // 设置最大可用内存
            if (RamTotal <= 1.5d)
            {
                this.SliderRamCustom.MaxValue = (int)Math.Round(Math.Max(Math.Floor((RamTotal - 0.3d) / 0.1d), 1d));
            }
            else if (RamTotal <= 8d)
            {
                this.SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((RamTotal - 1.5d) / 0.5d) + 12d);
            }
            else if (RamTotal <= 16d)
            {
                this.SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((RamTotal - 8d) / 1d) + 25d);
            }
            else
            {
                this.SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((RamTotal - 16d) / 2d) + 33d);
            }
            // 设置文本
            this.LabRamGame.Text = Conversions.ToString(Operators.ConcatenateObject(Operators.ConcatenateObject(RamGame == Math.Floor(RamGame) ? RamGame + ".0" : RamGame, " GB"), RamGame != RamGameActual ? Operators.ConcatenateObject(Operators.ConcatenateObject(" (可用 ", RamGameActual == Math.Floor(RamGameActual) ? RamGameActual + ".0" : RamGameActual), " GB)") : ""));
            this.LabRamUsed.Text = Conversions.ToString(Operators.ConcatenateObject(RamUsed == Math.Floor(RamUsed) ? RamUsed + ".0" : RamUsed, " GB"));
            this.LabRamTotal.Text = Conversions.ToString(Operators.ConcatenateObject(Operators.ConcatenateObject(" / ", RamTotal == Math.Floor(RamTotal) ? RamTotal + ".0" : RamTotal), " GB"));
            if (ShowAnim)
            {
                // 宽度动画
                ModAnimation.AniStart(new[] { ModAnimation.AaGridLengthWidth(this.ColumnRamUsed, RamUsed - this.ColumnRamUsed.Width.Value, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), ModAnimation.AaGridLengthWidth(this.ColumnRamGame, RamGameActual - this.ColumnRamGame.Width.Value, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), ModAnimation.AaGridLengthWidth(this.ColumnRamEmpty, RamEmpty - this.ColumnRamEmpty.Width.Value, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)) }, "VersionSetup Ram Grid");
            }
            else
            {
                // 宽度设置
                this.ColumnRamUsed.Width = new GridLength(RamUsed, GridUnitType.Star);
                this.ColumnRamGame.Width = new GridLength(RamGameActual, GridUnitType.Star);
                this.ColumnRamEmpty.Width = new GridLength(RamEmpty, GridUnitType.Star);
            }
        }
        private void RefreshRam()
        {
            RefreshRam(true);
        }

        private int RamTextLeft = 2;
        private int RamTextRight = 1;
        /// <summary>
    /// 刷新 UI 上的文本位置。
    /// </summary>
        private void RefreshRamText()
        {
            // 获取宽度信息
            double RectUsedWidth = this.RectRamUsed.ActualWidth;
            double TotalWidth = this.PanRamDisplay.ActualWidth;
            double LabGameWidth = this.LabRamGame.ActualWidth;
            double LabUsedWidth = this.LabRamUsed.ActualWidth;
            double LabTotalWidth = this.LabRamTotal.ActualWidth;
            double LabGameTitleWidth = this.LabRamGameTitle.ActualWidth;
            double LabUsedTitleWidth = this.LabRamUsedTitle.ActualWidth;
            // 左侧
            int Left;
            if (RectUsedWidth - 30d < LabUsedWidth || RectUsedWidth - 30d < LabUsedTitleWidth)
            {
                // 全写不下了
                Left = 0;
            }
            else if (RectUsedWidth - 25d < LabUsedWidth + LabTotalWidth)
            {
                // 显示不下完整数据
                Left = 1;
            }
            else
            {
                // 正常
                Left = 2;
            }
            if (RamTextLeft != Left)
            {
                RamTextLeft = Left;
                switch (Left)
                {
                    case 0:
                        {
                            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.LabRamUsed, -this.LabRamUsed.Opacity, 100), ModAnimation.AaOpacity(this.LabRamTotal, -this.LabRamTotal.Opacity, 100), ModAnimation.AaOpacity(this.LabRamUsedTitle, -this.LabRamUsedTitle.Opacity, 100) }, "VersionSetup Ram TextLeft");
                            break;
                        }
                    case 1:
                        {
                            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.LabRamUsed, 1d - this.LabRamUsed.Opacity, 100), ModAnimation.AaOpacity(this.LabRamTotal, -this.LabRamTotal.Opacity, 100), ModAnimation.AaOpacity(this.LabRamUsedTitle, 0.7d - this.LabRamUsedTitle.Opacity, 100) }, "VersionSetup Ram TextLeft");
                            break;
                        }
                    case 2:
                        {
                            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.LabRamUsed, 1d - this.LabRamUsed.Opacity, 100), ModAnimation.AaOpacity(this.LabRamTotal, 1d - this.LabRamTotal.Opacity, 100), ModAnimation.AaOpacity(this.LabRamUsedTitle, 0.7d - this.LabRamUsedTitle.Opacity, 100) }, "VersionSetup Ram TextLeft");
                            break;
                        }
                }
            }
            // 右侧
            int Right;
            if (TotalWidth < LabGameWidth + 2d + RectUsedWidth || TotalWidth < LabGameTitleWidth + 2d + RectUsedWidth)
            {
                // 挤到最右边
                Right = 0;
            }
            else
            {
                // 正常情况
                Right = 1;
            }
            if (Right == 0)
            {
                if (ModAnimation.AniControlEnabled == 0 && (RamTextRight != Right || ModAnimation.AniIsRun("VersionSetup Ram TextRight")))
                {
                    // 需要动画
                    ModAnimation.AniStart(new[] { ModAnimation.AaX(this.LabRamGame, TotalWidth - LabGameWidth - this.LabRamGame.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaX(this.LabRamGameTitle, TotalWidth - LabGameTitleWidth - this.LabRamGameTitle.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)) }, "VersionSetup Ram TextRight");
                }
                else
                {
                    // 不需要动画
                    this.LabRamGame.Margin = new Thickness(TotalWidth - LabGameWidth, 3d, 0d, 0d);
                    this.LabRamGameTitle.Margin = new Thickness(TotalWidth - LabGameTitleWidth, 0d, 0d, 5d);
                }
            }
            else if (ModAnimation.AniControlEnabled == 0 && (RamTextRight != Right || ModAnimation.AniIsRun("VersionSetup Ram TextRight")))
            {
                // 需要动画
                ModAnimation.AniStart(new[] { ModAnimation.AaX(this.LabRamGame, 2d + RectUsedWidth - this.LabRamGame.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaX(this.LabRamGameTitle, 2d + RectUsedWidth - this.LabRamGameTitle.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)) }, "VersionSetup Ram TextRight");
            }
            else
            {
                // 不需要动画
                this.LabRamGame.Margin = new Thickness(2d + RectUsedWidth, 3d, 0d, 0d);
                this.LabRamGameTitle.Margin = new Thickness(2d + RectUsedWidth, 0d, 0d, 5d);
            }
            RamTextRight = Right;
        }

        /// <summary>
    /// 获取当前设置的 RAM 值。单位为 GB。
    /// </summary>
        public static double GetRam(ModMinecraft.McInstance Instance)
        {
            // 跟随全局设置
            if (Settings.Get<int>("VersionRamType", Instance: Instance) == 2)
            {
                return PageSetupLaunch.GetRam(Instance, true);
            }

            // ------------------------------------------
            // 修改下方代码时需要一并修改 PageSetupLaunch
            // ------------------------------------------

            // 使用当前版本的设置
            var RamGive = default(double);
            if (Settings.Get<int>("VersionRamType", Instance: Instance) == 0)
            {
                // 自动配置
                double RamAvailable = Math.Round(My.Computer.Info.AvailablePhysicalMemory / 1024d / 1024d / 1024d * 10d) / 10d;
                // 确定需求的内存值
                double RamMininum; // 无论如何也需要保证的最低限度内存
                double RamTarget1; // 估计能勉强带动了的内存
                double RamTarget2; // 估计没啥问题了的内存
                double RamTarget3; // 安装过多附加组件需要的内存
                if (Instance is not null && !Instance.IsLoaded)
                    Instance.Load();
                if (Instance is not null && Instance.Modable)
                {
                    // 可安装 Mod 的版本
                    var ModDir = DirectoryUtils.GetInfo(Instance.PathIndie + @"mods\");
                    int ModCount = ModDir.Exists ? ModDir.GetFiles().Count(f => new[] { ".jar", ".zip", ".litemod" }.Contains(f.Extension.Lower())) : 0;
                    RamMininum = 0.5d + ModCount / 150d;
                    RamTarget1 = 1.5d + ModCount / 90d;
                    RamTarget2 = 2.7d + ModCount / 50d;
                    RamTarget3 = 4.5d + ModCount / 25d;
                }
                else if (Instance is not null && Instance.Version.HasOptiFine)
                {
                    // OptiFine 版本
                    RamMininum = 0.5d;
                    RamTarget1 = 1.5d;
                    RamTarget2 = 3d;
                    RamTarget3 = 5d;
                }
                else
                {
                    // 普通版本
                    RamMininum = 0.5d;
                    RamTarget1 = 1.5d;
                    RamTarget2 = 2.5d;
                    RamTarget3 = 4d;
                }
                double RamDelta;
                // 预分配内存，阶段一，0 ~ T1，100%
                RamDelta = RamTarget1;
                RamGive += Math.Min(RamAvailable, RamDelta);
                RamAvailable -= RamDelta;
                if (RamAvailable < 0.1d)
                    goto PreFin;
                // 预分配内存，阶段二，T1 ~ T2，70%
                RamDelta = RamTarget2 - RamTarget1;
                RamGive += Math.Min(RamAvailable * 0.7d, RamDelta);
                RamAvailable -= RamDelta / 0.7d;
                if (RamAvailable < 0.1d)
                    goto PreFin;
                // 预分配内存，阶段三，T2 ~ T3，40%
                RamDelta = RamTarget3 - RamTarget2;
                RamGive += Math.Min(RamAvailable * 0.4d, RamDelta);
                RamAvailable -= RamDelta / 0.4d;
                if (RamAvailable < 0.1d)
                    goto PreFin;
                // 预分配内存，阶段四，T3 ~ T3 * 2，15%
                RamDelta = RamTarget3;
                RamGive += Math.Min(RamAvailable * 0.15d, RamDelta);
                RamAvailable -= RamDelta / 0.15d;
                if (RamAvailable < 0.1d)
                    goto PreFin;
                PreFin:
                ;

                // 不低于最低值
                RamGive = Math.Round(Math.Max(RamGive, RamMininum), 1);
            }
            else
            {
                // 手动配置
                int Value = Settings.Get<int>("VersionRamCustom", Instance: Instance);
                if (Value <= 12)
                {
                    RamGive = Value * 0.1d + 0.3d;
                }
                else if (Value <= 25)
                {
                    RamGive = (Value - 12) * 0.5d + 1.5d;
                }
                else if (Value <= 33)
                {
                    RamGive = (Value - 25) * 1 + 8;
                }
                else
                {
                    RamGive = (Value - 33) * 2 + 16;
                }
            }
            return RamGive;
        }

        #endregion

        #region 服务器

        // 当第三方登录更改时，清空版本列表缓存以更新版本分类
        // TODO: 这会不会导致拖拽改变第三方登录的时候版本列表缓存没有更新？
        public static void OnVersionServerLoginChanged(int Type)
        {
            if (ModMain.FrmInstanceSetup is null)
                return;
            ModBase.WriteIni(ModMinecraft.McFolderSelected + "PCL.ini", "InstanceCache", "");
            if (PageInstanceLeft.Instance is null)
                return;
            PageInstanceLeft.Instance = new ModMinecraft.McInstance(PageInstanceLeft.Instance.Name).Load();
            ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
        }

        // 自动替换标点
        private void TextServerEnter_Change()
        {
            string NewText = this.TextServerEnter.Text.Replace("：", ":").Replace("。", ".");
            if ((NewText ?? "") == (this.TextServerEnter.Text ?? ""))
                return;
            int CurrentPosition = this.TextServerEnter.SelectionStart; // 重设焦点
            this.TextServerEnter.Text = NewText;
            this.TextServerEnter.SelectionStart = CurrentPosition;
        }

        // 全局
        private int ComboServerLoginLast;
        private void ComboServerLogin_Changed()
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            UpdateServerLoginUI();
            // 检查是否输入正确，正确才触发设置改变
            if (this.ComboServerLogin.SelectedIndex == 3 && !this.TextServerNide.IsValidated)
                return;
            if (this.ComboServerLogin.SelectedIndex == 4 && !this.TextServerAuthServer.IsValidated)
                return;
            // 检查结果是否发生改变，未改变则不触发设置改变
            if (ComboServerLoginLast == this.ComboServerLogin.SelectedIndex)
                return;
            // 触发
            ComboServerLoginLast = this.ComboServerLogin.SelectedIndex;
            Settings.Set("VersionServerLogin", this.ComboServerLogin.SelectedIndex, Instance: PageInstanceLeft.Instance);
        }
        private void UpdateServerLoginUI()
        {
            if (this.LabServerNide is null)
                return;
            int Type = this.ComboServerLogin.SelectedIndex;
            this.LabServerNide.Visibility = Type == 3 ? Visibility.Visible : Visibility.Collapsed;
            this.TextServerNide.Visibility = Type == 3 ? Visibility.Visible : Visibility.Collapsed;
            this.PanServerNide.Visibility = Type == 3 ? Visibility.Visible : Visibility.Collapsed;
            this.LabServerAuthName.Visibility = Type == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.TextServerAuthName.Visibility = Type == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.LabServerAuthRegister.Visibility = Type == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.TextServerAuthRegister.Visibility = Type == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.LabServerAuthServer.Visibility = Type == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.TextServerAuthServer.Visibility = Type == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.BtnServerAuthLittle.Visibility = Type == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.CardServer.TriggerForceResize();
        }

        // LittleSkin
        private void BtnServerAuthLittle_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(this.TextServerAuthServer.Text) && this.TextServerAuthServer.Text != "https://littleskin.cn/api/yggdrasil" && ModMain.MyMsgBox("即将把第三方登录设置覆盖为 LittleSkin 登录。" + "\r\n" + "除非你是服主，或者服主要求你这样做，否则请不要继续。" + "\r\n" + "\r\n" + "是否确实需要覆盖当前设置？", "设置覆盖确认", "继续", "取消") == 2)
                return;
            this.TextServerAuthServer.Text = "https://littleskin.cn/api/yggdrasil";
            this.TextServerAuthRegister.Text = "https://littleskin.cn/auth/register";
            this.TextServerAuthName.Text = "LittleSkin 登录";
        }

        #endregion

        #region Java 主类别和预览

        private void ReloadCurrentJava()
        {
            PageInstanceLeft.ReloadCurrentJava();
        }

        private void UpdateJavaRelatedUi()
        {
            ModBase.RunInUi(_UpdateJavaRelatedUi);
        }
        private void _UpdateJavaRelatedUi()
        {
            var JavaWorker = PageInstanceLeft.CurrentJavaWorker;
            switch (this.ComboArgumentJava.SelectedIndex)
            {
                case 0:
                case 2:
                    {
                        this.HintArgumentJava.Visibility = Visibility.Visible;
                        this.TextArgumentJavaRange.Visibility = Visibility.Collapsed;
                        this.PanArgumentJavaSelect.Visibility = Visibility.Collapsed;
                        // 显示当前会选择的 Java
                        if (!JavaWorker.LastSucceeded || JavaWorker.Running)
                        {
                            if (!JavaWorker.Running)
                                JavaWorker.Start();
                            this.HintArgumentJava.Text = $"正在查找 Java……";
                            this.HintArgumentJava.Theme = MyHint.Themes.Blue;
                        }
                        else if (JavaWorker.LastResult is null)
                        {
                            if (this.ComboArgumentJava.SelectedIndex == 0)
                            {
                                this.HintArgumentJava.Text = $"你的电脑上没有可供该版本使用的 Java，PCL Next 会在启动游戏时自动下载。";
                                this.HintArgumentJava.Theme = MyHint.Themes.Yellow;
                            }
                            else
                            {
                                this.HintArgumentJava.Text = $"该版本的版本文件夹中没有发现任何 Java！{"\r\n"}请点击此处打开版本文件夹，然后将 Java 文件夹复制进去。";
                                this.HintArgumentJava.Theme = MyHint.Themes.Red;
                            }
                        }
                        else
                        {
                            this.HintArgumentJava.Text = $"将会使用：{JavaWorker.LastResult}";
                            this.HintArgumentJava.Theme = MyHint.Themes.Blue;
                        }

                        break;
                    }
                case 1:
                    {
                        this.HintArgumentJava.Visibility = Visibility.Visible;
                        this.TextArgumentJavaRange.Visibility = Visibility.Visible;
                        this.PanArgumentJavaSelect.Visibility = Visibility.Collapsed;
                        // 首次设置
                        if (!Settings.HasSaved("VersionArgumentJavaRange", PageInstanceLeft.Instance))
                        {
                            string DefaultRange = ModJava.GetJavaRequirement(PageInstanceLeft.Instance).Range.ToString().Replace("-∞", "").Replace("+∞", "");
                            Settings.Set("VersionArgumentJavaRange", DefaultRange, PageInstanceLeft.Instance);
                            this.TextArgumentJavaRange.Text = DefaultRange;
                        }
                        // 输入的范围有误
                        if (!string.IsNullOrEmpty(this.TextArgumentJavaRange.ValidateResult))
                        {
                            this.HintArgumentJava.Text = $"Java 版本范围错误：{this.TextArgumentJavaRange.ValidateResult}";
                            this.HintArgumentJava.Theme = MyHint.Themes.Red;
                            return;
                        }
                        // 显示当前会选择的 Java
                        if (!JavaWorker.LastSucceeded || JavaWorker.Running)
                        {
                            if (!JavaWorker.Running)
                                JavaWorker.Start();
                            this.HintArgumentJava.Text = $"正在查找 Java……";
                            this.HintArgumentJava.Theme = MyHint.Themes.Blue;
                        }
                        else if (JavaWorker.LastResult is null)
                        {
                            this.HintArgumentJava.Text = $"你的电脑上没有任何 Java 符合该版本范围！{"\r\n"}如果 Mojang 提供了对应版本的 Java，PCL Next 会在启动游戏时自动下载。";
                            this.HintArgumentJava.Theme = MyHint.Themes.Yellow;
                        }
                        else
                        {
                            this.HintArgumentJava.Text = $"将会使用：{JavaWorker.LastResult}";
                            this.HintArgumentJava.Theme = MyHint.Themes.Blue;
                        }

                        break;
                    }
                case 3:
                    {
                        this.HintArgumentJava.Visibility = Visibility.Collapsed;
                        this.TextArgumentJavaRange.Visibility = Visibility.Collapsed;
                        this.PanArgumentJavaSelect.Visibility = Visibility.Visible;
                        break;
                    }
            }
        }
        private void HintArgumentJava_Click(object sender, MouseButtonEventArgs e)
        {
            if (this.ComboArgumentJava.SelectedIndex == 2)
                PageInstanceOverall.OpenInstanceFolder(PageInstanceLeft.Instance);
        }

        #endregion

        #region 选择特定 Java 的下拉框
        // 注意：修改此处代码时需要同时修改 PageSetupLaunch.xaml.vb

        // 刷新 Java 下拉框显示
        public void UpdateJavaList()
        {
            if (this.ComboArgumentJavaSelect is null || this.LabArgumentJava is null || !this.ComboArgumentJavaSelect.IsLoaded)
                return;
            this.ComboArgumentJavaSelect.Items.Clear();
            // 还需要等待搜索结束
            if (ModJava.JavaListRefreshWorker.Running)
            {
                this.BtnArgumentJavaSearch.IsEnabled = false;
                this.ComboArgumentJavaSelect.IsEnabled = false;
                this.LabArgumentJava.Text = "搜索中 …";
                return;
            }
            // ========================================== 显示结果 ==========================================
            this.BtnArgumentJavaSearch.IsEnabled = true;
            this.ComboArgumentJavaSelect.IsEnabled = true;
            var SelectedJava = Configs.JavaForced.Get(PageInstanceLeft.Instance.Config);
            // 更新下拉框文本
            if (!Configs.JavaList.Get().Any())
            {
                this.LabArgumentJava.Text = "未找到 Java，点击以导入已有的 Java";
            }
            else if (SelectedJava is null)
            {
                this.LabArgumentJava.Text = "点击选择 Java …";
            }
            else
            {
                this.LabArgumentJava.Text = SelectedJava.ToString();
            }
            // 更新列表
            try
            {
                foreach (var JavaEntry in Configs.JavaList.Get())
                {
                    var JavaItem = new MyListItem()
                    {
                        FontSize = 13d,
                        Height = 24d,
                        IsScaleAnimationEnabled = false,
                        Type = MyListItem.CheckType.Clickable,
                        Tag = JavaEntry,
                        Title = JavaEntry.ToString()
                    };
                    JavaItem.MouseLeftButtonUp += (object sender, global::System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;
                    JavaItem.Click += (_, __) =>
        {
            Configs.JavaForced.Set(JavaEntry, PageInstanceLeft.Instance.Config);
            UpdateJavaList();
            this.ComboArgumentJavaSelect.IsDropDownOpen = false;
        };
                    this.ComboArgumentJavaSelect.Items.Add(JavaItem);
                    var Buttons = new List<MyIconButton>();
                    // 从列表中移除按钮
                    bool IsOfficial = PathUtils.IsParentOf($@"{Paths.AppData}.minecraft\runtime\", JavaEntry.Folder);
                    var DeleteButton = new MyIconButton() { Logo = ModBase.Logo.IconButtonStop, Height = 24d, Width = 24d };
                    DeleteButton.IsEnabled = !IsOfficial;
                    DeleteButton.ToolTip = DeleteButton.IsEnabled ? "从列表中移除" : "无法移除官方 Java";
                    DeleteButton.Click += (_, __) => ModJava.ManuallyRemoveJava(JavaEntry);
                    ToolTipService.SetShowOnDisabled(DeleteButton, true);
                    Buttons.Add(DeleteButton);
                    // 打开文件夹按钮
                    var OpenButton = new MyIconButton() { Logo = ModBase.Logo.IconButtonOpen, LogoScale = 1.1d, Height = 24d, Width = 24d };
                    OpenButton.ToolTip = "打开文件夹";
                    OpenButton.Click += (_, __) => ModBase.OpenExplorer(JavaEntry.JavaExePath);
                    Buttons.Add(OpenButton);
                    JavaItem.Buttons = Buttons;
                }
                var ImportItem = new MyListItem() { FontSize = 13d, Height = 24d, IsScaleAnimationEnabled = false, Title = " 导入电脑中已有的 Java…", Type = MyListItem.CheckType.Clickable };
                ImportItem.MouseLeftButtonUp += (object sender, global::System.Windows.Input.MouseButtonEventArgs e) =>
        {
            e.Handled = true;
            global::PCL.ModJava.ManuallyImportJava();
        };
                this.ComboArgumentJavaSelect.Items.Add(ImportItem);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "更新 Java 下拉框失败");
            }
        }
        private void BtnAdvanceJavaSearch_Click()
        {
            ModJava.ManuallySearchJava();
        }
        private bool _ComboArgumentIndieV2_SelectionChanged_IsReverting = false;

        #endregion

        // 版本隔离
        private void ComboArgumentIndieV2_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (_ComboArgumentIndieV2_SelectionChanged_IsReverting)
                return;
            if (ModMain.MyMsgBox("调整版本隔离设置后，你需要游戏存档、Mod 等文件手动迁移到新的游戏文件夹中。" + "\r\n" + "如果发现存档消失，把这项设置改回来就能恢复。" + "\r\n" + "如果你不会迁移存档，不建议修改这项设置！", "警告", "我知道我在做什么", "取消", IsWarn: true) == 2)
            {
                _ComboArgumentIndieV2_SelectionChanged_IsReverting = true;
                if (e.RemovedItems.Count > 0)
                    this.ComboArgumentIndieV2.SelectedItem = e.RemovedItems[0];
                _ComboArgumentIndieV2_SelectionChanged_IsReverting = false;
            }
            // 实际应用设置
            Settings.Set("VersionArgumentIndieV2", Operators.ConditionalCompareObjectEqual(((dynamic)sender).SelectedIndex, 0, false), Instance: PageInstanceLeft.Instance);
        }

        // 启动前执行命令
        private void TextAdvanceRun_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.CheckAdvanceRunWait.Visibility = (!string.IsNullOrEmpty(this.TextAdvanceRun.Text)).ToVisibility();
        }

        // 切换到全局设置
        private void BtnSwitch_Click(object sender, MouseButtonEventArgs e)
        {
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Setup, FormMain.PageSubType.SetupLaunch);
        }

        // 去除参数中的回车
        private void ReplaceEnter(MyTextBox sender, TextChangedEventArgs e)
        {
            string NewText = sender.Text.ReplaceLineEndings(" ", mergeMultiple: true);
            if ((NewText ?? "") == (sender.Text ?? ""))
                return;
            int CaretIndex = sender.CaretIndex;
            sender.Text = NewText;
            sender.CaretIndex = Math.Max(0, CaretIndex - 1);
        }

    }
}