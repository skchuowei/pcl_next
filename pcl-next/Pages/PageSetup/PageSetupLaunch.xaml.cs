using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public partial class PageSetupLaunch
    {

        // 非重复加载部分
        private bool _PageSetupLaunch_Loaded_Reloaded = false;

        public PageSetupLaunch()
        {
            InitializeComponent();
            this.RadioSkinType4.PreviewCheck += (p1, p2) => RadioSkinType3_Check((object)p1, (ModBase.RouteEventArgs)p2);
            this.SliderRamCustom.Change += (p1, p2) => RefreshRam((bool)p1);
            this.RadioRamType0.Check += (p1, p2) => RefreshRam((bool)p1);
            this.RadioRamType1.Check += (p1, p2) => RefreshRam((bool)p1);
            this.RectRamGame.SizeChanged += (p1, p2) => RefreshRamText();
            this.RectRamEmpty.SizeChanged += (p1, p2) => RefreshRamText();
            this.LabRamGame.SizeChanged += (p1, p2) => RefreshRamText();
            this.ComboArgumentWindowType.SelectionChanged += (p1, p2) => WindowTypeUIRefresh();
            this.ComboArgumentVisibie.SelectionChanged += (p1, p2) => ComboArgumentVisibie_SizeChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.CheckArgumentRam.Change += (p1, p2) => CheckArgumentRam_Change();
            this.ComboArgumentIndieV2.SelectionChanged += (p1, p2) => ComboArgumentIndie_SelectionChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.TextAdvanceRun.TextChanged += (p1, p2) => TextAdvanceRun_TextChanged((object)p1, (TextChangedEventArgs)p2);
            this.TextAdvanceJvm.ValidatedTextChanged += (p1, p2) => TextAdvanceJvm_TextChanged();
            this.TextAdvanceJvm.TextChanged += (p1, p2) => ReplaceEnter((MyTextBox)p1, (TextChangedEventArgs)p2);
            this.TextAdvanceGame.TextChanged += (p1, p2) => ReplaceEnter((MyTextBox)p1, (TextChangedEventArgs)p2);
            // VB 原版全部靠 Handles 绑定，迁移后一个都没订阅：离线皮肤四个按钮、JVM 参数重设、
            // 「版本独立设置」跳转全部点了无反应（2026-11-11 修复）。
            this.BtnSkinChange.Click += (p1, p2) => BtnSkinChange_Click(p1, p2);
            this.BtnSkinDelete.Click += (p1, p2) => BtnSkinDelete_Click(p1, p2);
            this.BtnSkinSave.Click += (p1, p2) => BtnSkinSave_Click(p1, p2);
            this.BtnSkinCache.Click += (p1, p2) => BtnSkinCache_Click(p1, p2);
            this.BtnAdvanceJvmReset.Click += (p1, p2) => BtnAdvanceJvmReset_Click(p1, p2);
            this.BtnSwitch.Click += (p1, p2) => BtnSwitch_Click(p1, p2);
            // VB 的 Handles BtnAdvanceJavaSearch.Click 迁移时被丢弃 → 设置里「手动搜索 Java」点了没反应（2026-11-11 补）。
            this.BtnAdvanceJavaSearch.Click += (p1, p2) => BtnAdvanceJavaSearch_Click();
            this.Loaded += PageSetupLaunch_Loaded;
        }

        private void PageSetupLaunch_Loaded(object sender, RoutedEventArgs e)
        {
            // 重复加载部分
            this.PanBack.ScrollToHome();
            RefreshRam(false);
            this.BtnSwitch.Visibility = ModMinecraft.McInstanceSelected is null ? Visibility.Collapsed : Visibility.Visible;

            // Java 可能在版本设置被修改，所以总是重新加载（反正其他的 Refresh 也不太吃性能）
            ModAnimation.AniControlEnabled += 1;
            Refresh();
            ModAnimation.AniControlEnabled -= 1;
            if (_PageSetupLaunch_Loaded_Reloaded)
                return;
            _PageSetupLaunch_Loaded_Reloaded = true;

            // 内存自动刷新
            var timer = new System.Windows.Threading.DispatcherTimer() { Interval = new TimeSpan(0, 0, 0, 1) };
            timer.Tick += (_, __) => RefreshRam();
            timer.Start();
        }
        public void Refresh()
        {
            try
            {
                SettingService.RefreshSettings(this);
                UpdateSkinType();
                UpdateRamType();
                UpdateJavaList();
            }
            catch (NullReferenceException ex)
            {
                Logger.Error(ex, "启动设置项存在异常，已被自动重置", LogBehavior.Alert);
                Reset();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重载启动设置时出错");
            }
        }
        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Settings.Set("LaunchArgumentIndieV2", Settings.GetDefault("LaunchArgumentIndieV2"));
                Configs.JavaList.Reset();
                Configs.JavaRemovedList.Reset();
                ModJava.JavaListRefreshWorker.Start();
                ModMain.Hint("已初始化启动设置！", ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化启动设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

        #region 离线皮肤

        private void BtnSkinChange_Click(object sender, MouseButtonEventArgs e)
        {
            var SkinInfo = ModMinecraft.McSkinSelect();
            if (!SkinInfo.IsVaild)
                return;
            ChangeSkin(SkinInfo);
        }
        private void RadioSkinType3_Check(object sender, ModBase.RouteEventArgs e)
        {
            if (!(ModAnimation.AniControlEnabled == 0 && e.RaiseByMouse))
                return;
            // 已有图片则不再选择
            if (FileUtils.Exists(Paths.AppDataThenName + "CustomSkin.png"))
                return;
            // 没有图片则要求选择
            var SkinInfo = ModMinecraft.McSkinSelect();
            if (!SkinInfo.IsVaild)
            {
                e.Handled = true;
                return;
            }
            // 正式改变
            if (!ChangeSkin(SkinInfo))
                e.Handled = true;
        }
        // 返回是否成功改变
        private bool ChangeSkin(ModMinecraft.McSkinInfo SkinInfo)
        {
            try
            {
                // 复制文件
                FileUtils.Copy(SkinInfo.LocalFile, Paths.AppDataThenName + "CustomSkin.png");
                // 将单层皮肤扩展到双层
                var Bitmap = new MyBitmap(Paths.AppDataThenName + "CustomSkin.png");
                if (Bitmap.Pic.Width == 64 && Bitmap.Pic.Height == 32)
                {
                    System.Drawing.Image Img = Bitmap;
                    var NewBitmap = new System.Drawing.Bitmap(64, 64);
                    using (var g = System.Drawing.Graphics.FromImage(NewBitmap))
                    {
                        g.DrawImageUnscaled(Img, new System.Drawing.Point(0, 0));
                    }
                    FileUtils.Delete(Paths.AppDataThenName + "CustomSkin.png");
                    NewBitmap.Save(Paths.AppDataThenName + "CustomSkin.png");
                }
                // 更新设置
                Settings.Set("LaunchSkinSlim", SkinInfo.IsSlim);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "改变离线皮肤失败", LogBehavior.Alert);
                return false;
            }
            finally
            {
                // 设置当前显示
                PageLaunchLeft.SkinLegacy.Start(IsForceRestart: true);
            }
        }
        private void BtnSkinDelete_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                FileUtils.Delete(Paths.AppDataThenName + "CustomSkin.png");
                this.RadioSkinType0.SetChecked(true, true);
                ModMain.Hint("离线皮肤已清空！", ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "清空离线皮肤失败", LogBehavior.Alert);
            }
        }
        private void BtnSkinSave_Click(object sender, MouseButtonEventArgs e)
        {
            MySkin.Save(PageLaunchLeft.SkinLegacy);
        }
        private void BtnSkinCache_Click(object sender, MouseButtonEventArgs e)
        {
            MySkin.RefreshCache(null);
        }

        public static void UpdateSkinType()
        {
            PageLaunchLeft.SkinLegacy.Start();
            // 设置 UI 改变
            if (ModMain.FrmSetupLaunch is null)
                return;
            switch (Settings.Get<int>("LaunchSkinType"))
            {
                case 0:
                case 1:
                case 2: // 默认
                    {
                        ModMain.FrmSetupLaunch.PanSkinID.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupLaunch.PanSkinChange.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 3: // 正版
                    {
                        ModMain.FrmSetupLaunch.PanSkinID.Visibility = Visibility.Visible;
                        ModMain.FrmSetupLaunch.PanSkinChange.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 4: // 自定义
                    {
                        ModMain.FrmSetupLaunch.PanSkinID.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupLaunch.PanSkinChange.Visibility = Visibility.Visible;
                        break;
                    }
            }
            ModMain.FrmSetupLaunch.CardSkin.TriggerForceResize();
        }

        #endregion

        #region 游戏内存

        public static void UpdateRamType()
        {
            if (ModMain.FrmSetupLaunch?.SliderRamCustom is null)
                return;
            ModMain.FrmSetupLaunch.SliderRamCustom.IsEnabled = Settings.Get<int>("LaunchRamType") == 1;
        }

        /// <summary>
    /// 刷新 UI 上的 RAM 显示。
    /// </summary>
        public void RefreshRam(bool ShowAnim)
        {
            if (this.LabRamGame is null || this.LabRamUsed is null || this.LabRamTotal is null || this.SliderRamCustom is null ||
                this.ColumnRamUsed is null || this.ColumnRamGame is null || this.ColumnRamEmpty is null ||
                ModMain.FrmMain is null || ModMain.FrmSetupLeft is null ||
                ModMain.FrmMain.PageCurrent != (FormMain.PageStackData)FormMain.PageType.Setup ||
                ModMain.FrmSetupLeft.PageID != FormMain.PageSubType.SetupLaunch)
                return;
            // 获取内存情况
            double RamGame = Math.Round(GetRam(ModMinecraft.McInstanceSelected, false), 5);
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
                ModAnimation.AniStart(new[] { ModAnimation.AaGridLengthWidth(this.ColumnRamUsed, RamUsed - this.ColumnRamUsed.Width.Value, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), ModAnimation.AaGridLengthWidth(this.ColumnRamGame, RamGameActual - this.ColumnRamGame.Width.Value, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)), ModAnimation.AaGridLengthWidth(this.ColumnRamEmpty, RamEmpty - this.ColumnRamEmpty.Width.Value, 800, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)) }, "SetupLaunch Ram Grid");
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
                            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.LabRamUsed, -this.LabRamUsed.Opacity, 100), ModAnimation.AaOpacity(this.LabRamTotal, -this.LabRamTotal.Opacity, 100), ModAnimation.AaOpacity(this.LabRamUsedTitle, -this.LabRamUsedTitle.Opacity, 100) }, "SetupLaunch Ram TextLeft");
                            break;
                        }
                    case 1:
                        {
                            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.LabRamUsed, 1d - this.LabRamUsed.Opacity, 100), ModAnimation.AaOpacity(this.LabRamTotal, -this.LabRamTotal.Opacity, 100), ModAnimation.AaOpacity(this.LabRamUsedTitle, 0.7d - this.LabRamUsedTitle.Opacity, 100) }, "SetupLaunch Ram TextLeft");
                            break;
                        }
                    case 2:
                        {
                            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.LabRamUsed, 1d - this.LabRamUsed.Opacity, 100), ModAnimation.AaOpacity(this.LabRamTotal, 1d - this.LabRamTotal.Opacity, 100), ModAnimation.AaOpacity(this.LabRamUsedTitle, 0.7d - this.LabRamUsedTitle.Opacity, 100) }, "SetupLaunch Ram TextLeft");
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
                if (ModAnimation.AniControlEnabled == 0 && (RamTextRight != Right || ModAnimation.AniIsRun("SetupLaunch Ram TextRight")))
                {
                    // 需要动画
                    ModAnimation.AniStart(new[] { ModAnimation.AaX(this.LabRamGame, TotalWidth - LabGameWidth - this.LabRamGame.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaX(this.LabRamGameTitle, TotalWidth - LabGameTitleWidth - this.LabRamGameTitle.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)) }, "SetupLaunch Ram TextRight");
                }
                else
                {
                    // 不需要动画
                    ModAnimation.AniStop("SetupLaunch Ram TextRight");
                    this.LabRamGame.Margin = new Thickness(TotalWidth - LabGameWidth, 3d, 0d, 0d);
                    this.LabRamGameTitle.Margin = new Thickness(TotalWidth - LabGameTitleWidth, 0d, 0d, 5d);
                }
            }
            else if (ModAnimation.AniControlEnabled == 0 && (RamTextRight != Right || ModAnimation.AniIsRun("SetupLaunch Ram TextRight")))
            {
                // 需要动画
                ModAnimation.AniStart(new[] { ModAnimation.AaX(this.LabRamGame, 2d + RectUsedWidth - this.LabRamGame.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaX(this.LabRamGameTitle, 2d + RectUsedWidth - this.LabRamGameTitle.Margin.Left, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)) }, "SetupLaunch Ram TextRight");
            }
            else
            {
                // 不需要动画
                ModAnimation.AniStop("SetupLaunch Ram TextRight");
                this.LabRamGame.Margin = new Thickness(2d + RectUsedWidth, 3d, 0d, 0d);
                this.LabRamGameTitle.Margin = new Thickness(2d + RectUsedWidth, 0d, 0d, 5d);
            }
            RamTextRight = Right;
        }

        /// <summary>
    /// 获取当前设置的 RAM 值。单位为 GB。
    /// </summary>
        public static double GetRam(ModMinecraft.McInstance Instance, bool UseVersionJavaSetup)
        {

            // ------------------------------------------
            // 修改下方代码时需要一并修改 PageInstanceSetup
            // ------------------------------------------

            var RamGive = default(double);
            if (Settings.Get<int>("LaunchRamType") == 0)
            {
                // 自动配置
                double RamAvailable = Math.Round(My.Computer.Info.AvailablePhysicalMemory / 1024d / 1024d / 1024d * 10d) / 10d;
                // 确定需求的内存值
                double RamMininum; // 无论如何也需要保证的最低限度内存
                double RamTarget1; // 估计能勉强带动了的内存
                double RamTarget2; // 估计没啥问题了的内存
                double RamTarget3; // 放一百万个材质和 Mod 和光影需要的内存
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
                int Value = Settings.Get<int>("LaunchRamCustom");
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

        #region Java 选择
        // 注意：修改此处代码时需要同时修改 PageInstanceSetup.xaml.vb

        // 刷新 Java 下拉框显示
        public void UpdateJavaList()
        {
            if (this.ComboAdvanceJava is null || this.LabAdvanceJava is null || !this.ComboAdvanceJava.IsLoaded)
                return;
            this.ComboAdvanceJava.Items.Clear();
            // 还需要等待搜索结束
            if (ModJava.JavaListRefreshWorker.Running)
            {
                this.BtnAdvanceJavaSearch.IsEnabled = false;
                this.ComboAdvanceJava.IsEnabled = false;
                this.LabAdvanceJava.Text = "搜索中 …";
                return;
            }
            // ========================================== 显示结果 ==========================================
            this.BtnAdvanceJavaSearch.IsEnabled = true;
            this.ComboAdvanceJava.IsEnabled = true;
            // 更新下拉框文本
            int Count = Configs.JavaList.Get().Count;
            this.LabAdvanceJava.Text = Count > 0 ? $"共有 {Count} 个 Java …" : "未找到 Java，点击以导入已有的 Java";
            // 更新列表
            try
            {
                var JavaEntries = Configs.JavaList.Get();
                for (int i = 0, loopTo = JavaEntries.Count - 1; i <= loopTo; i++)
                {
                    var JavaEntry = JavaEntries[i];
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
                    JavaItem.Click += (_, __) => ModMain.Hint("点击选项右侧的箭头可以进行排序，以控制 PCL Next 优先选择哪个 Java！");
                    this.ComboAdvanceJava.Items.Add(JavaItem);
                    var Buttons = new List<MyIconButton>();
                    // 向上移动按钮
                    var UpButton = new MyIconButton() { Logo = ModBase.Logo.IconButtonArrowUp, LogoScale = 0.95d, Height = 24d, Width = 24d };
                    UpButton.IsEnabled = i > 0;
                    UpButton.ToolTip = "提高优先级";
                    ToolTipService.SetShowOnDisabled(UpButton, true);
                    UpButton.Click += (_, __) => MoveJavaInList(JavaEntry, -1);
                    Buttons.Add(UpButton);
                    // 向下移动按钮
                    var DownButton = new MyIconButton() { Logo = ModBase.Logo.IconButtonArrowDown, LogoScale = 0.95d, Height = 24d, Width = 24d };
                    DownButton.IsEnabled = i < JavaEntries.Count - 1;
                    DownButton.ToolTip = "降低优先级";
                    ToolTipService.SetShowOnDisabled(DownButton, true);
                    DownButton.Click += (_, __) => MoveJavaInList(JavaEntry, 1);
                    Buttons.Add(DownButton);
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
                var ImportItem = new MyListItem() { FontSize = 13d, Height = 24d, IsScaleAnimationEnabled = false, Title = "导入电脑中已有的 Java…", Type = MyListItem.CheckType.Clickable };
                ImportItem.MouseLeftButtonUp += (object sender, global::System.Windows.Input.MouseButtonEventArgs e) =>
        {
            e.Handled = true;
            global::PCL.ModJava.ManuallyImportJava();
        };
                this.ComboAdvanceJava.Items.Add(ImportItem);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "更新 Java 列表下拉框失败");
            }
        }
        private void MoveJavaInList(Java JavaEntry, int Offset)
        {
            // 对应 PageSetupLaunch.xaml.vb:425-433「Configs.JavaList.Edit(Sub(ByRef List) … If 越界 Then Return …)」：
            // VB 的 Sub 在越界时直接 Return；C# 转成取值型 lambda 后可走任意路径，故越界分支也要返回同一个列表。
            // Edit(Func) 与 Edit(RefAction) 对同一实例做原地修改后回写，行为等价。
            Configs.JavaList.Edit(List =>
        {
            int Index = List.IndexOf(JavaEntry);
            int NewIndex = Index + Offset;
            if (Index < 0 || NewIndex < 0 || NewIndex >= List.Count)
                return List;
            List.RemoveAt(Index);
            List.Insert(NewIndex, JavaEntry);
            return List;
        });
            ModJava.UpdateJavaLists();
        }
        private void BtnAdvanceJavaSearch_Click()
        {
            ModJava.ManuallySearchJava();
        }

        #endregion

        #region 其他选项

        private void WindowTypeUIRefresh()
        {
            var IsVisibie = (this.ComboArgumentWindowType.SelectedIndex == 3).ToVisibility();
            this.TextArgumentWindowHeight.Visibility = IsVisibie;
            this.LabArgumentWindowMiddle.Visibility = IsVisibie;
            this.TextArgumentWindowWidth.Visibility = IsVisibie;
        }

        // 可见性选择直接关闭的警告
        private void ComboArgumentVisibie_SizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (this.ComboArgumentVisibie.SelectedIndex == 0)
            {
                if (ModMain.MyMsgBox("若在游戏启动后立即关闭启动器，崩溃检测、更改游戏标题等功能将失效。" + "\r\n" + "如果想保留这些功能，可以选择让启动器在游戏启动后隐藏，游戏退出后自动关闭。", "提醒", "继续", "取消") == 2)
                {
                    if (e.RemovedItems.Count > 0)
                        this.ComboArgumentVisibie.SelectedItem = e.RemovedItems[0];
                }
            }
        }

        // 开启自动内存优化的警告
        private void CheckArgumentRam_Change()
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (!this.CheckArgumentRam.Checked)
                return;
            if (ModMain.MyMsgBox("内存优化会显著延长启动耗时，建议仅在内存不足时开启。" + "\r\n" + "如果你在使用机械硬盘，这还可能导致一小段时间的严重卡顿。" + (WindowsUtils.HasAdminRole() ? "" : $"{"\r\n"}{"\r\n"}每次启动游戏，PCL Next 都需要申请管理员权限以进行内存优化。{"\r\n"}若想自动授予权限，可以右键 PCL Next，打开 属性 → 兼容性 → 以管理员身份运行此程序。"), "提醒", "确定", "取消") == 2)
            {
                this.CheckArgumentRam.Checked = false;
            }
        }

        // 版本隔离提示
        private void ComboArgumentIndie_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            ModMain.MyMsgBox("本设置仅会对之后新安装的版本生效。" + "\r\n" + "如果要修改已安装的版本的隔离方式，请在它的版本独立设置中调整。");
        }

        #endregion

        #region 高级设置

        private void TextAdvanceRun_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.CheckAdvanceRunWait.Visibility = string.IsNullOrEmpty(this.TextAdvanceRun.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // JVM 参数重设
        private void TextAdvanceJvm_TextChanged()
        {
            this.BtnAdvanceJvmReset.Visibility = (this.TextAdvanceJvm.Text ?? "") == (Settings.GetDefault("LaunchAdvanceJvm") ?? "") ? Visibility.Hidden : Visibility.Visible;
        }
        private void BtnAdvanceJvmReset_Click(object sender, EventArgs e)
        {
            Settings.Reset("LaunchAdvanceJvm");
            Refresh();
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

        #endregion

        // 切换到版本独立设置
        private void BtnSwitch_Click(object sender, MouseButtonEventArgs e)
        {
            ModMinecraft.McInstanceSelected.Load();
            PageInstanceLeft.Instance = ModMinecraft.McInstanceSelected;
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.InstanceSetup, FormMain.PageSubType.InstanceSetup);
        }

    }
}