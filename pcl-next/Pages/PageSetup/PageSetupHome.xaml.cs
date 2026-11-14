using System;
using System.Windows;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public partial class PageSetupHome
    {

        // 非重复加载部分
        private bool _PageSetupHome_Loaded_Reloaded = false;

        public PageSetupHome()
        {
            InitializeComponent();
            this.BtnCustomFile.Click += (p1, p2) => BtnCustomFile_Click((object)p1, (EventArgs)p2);
            this.BtnCustomRefresh.Click += (p1, p2) => BtnCustomRefresh_Click();
            this.BtnCustomTutorial.Click += (p1, p2) => BtnCustomTutorial_Click((object)p1, (EventArgs)p2);
            this.BtnCustomFolder.Click += (p1, p2) => BtnCustomFolder_Click((object)p1, (MouseButtonEventArgs)p2);
            this.CheckHiddenPageSetup.Change += (p1, p2) => HiddenSetupMain();
            this.CheckHiddenSetupLaunch.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenSetupSystem.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenSetupLink.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenSetupUI.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenPageOther.Change += (p1, p2) => HiddenOtherMain();
            this.CheckHiddenOtherHelp.Change += (p1, p2) => HiddenOtherSub((object)p1, (bool)p2);
            this.CheckHiddenOtherAbout.Change += (p1, p2) => HiddenOtherSub((object)p1, (bool)p2);
            this.CheckHiddenOtherTest.Change += (p1, p2) => HiddenOtherSub((object)p1, (bool)p2);
            this.CheckHiddenOtherFeedback.Change += (p1, p2) => HiddenOtherNet((object)p1, (bool)p2);
            this.CheckHiddenOtherVote.Change += (p1, p2) => HiddenOtherNet((object)p1, (bool)p2);
            this.CheckHiddenFunctionHidden.Change += (p1, p2) => HiddenHint((object)p1, (bool)p2);
            this.CheckHiddenPageSetup.Change += (p1, p2) => HiddenHint((object)p1, (bool)p2);
            this.CheckHiddenSetupUI.Change += (p1, p2) => HiddenHint((object)p1, (bool)p2);
            this.Loaded += PageSetupHome_Loaded;
            this.Loaded += (_, __) => HiddenRefresh();
        }

        private void PageSetupHome_Loaded(object sender, RoutedEventArgs e)
        {
            this.PanBack.ScrollToHome();
            ModAnimation.AniControlEnabled += 1;
            Refresh();
            ModAnimation.AniControlEnabled -= 1;
            if (_PageSetupHome_Loaded_Reloaded)
                return;
            _PageSetupHome_Loaded_Reloaded = true;
        }

        public void Refresh()
        {
            try
            {
                SettingService.RefreshSettings(this);
                // 主页
                OnMainPageTypeChanged();
            }
            catch (NullReferenceException ex)
            {
                Logger.Error(ex, "主页设置项存在异常，已被自动重置", LogBehavior.Alert);
                Reset();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重载主页设置时出错");
            }
        }

        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化主页设置！");
                ModMain.Hint("已初始化主页设置", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化主页设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

        // 主页
        private void BtnCustomFile_Click(object sender, EventArgs e)
        {
            try
            {
                if (FileUtils.Exists(Paths.Base + @"PCL\Custom.xaml"))
                {
                    if (ModMain.MyMsgBox("当前已存在布局文件，继续生成教学文件将会覆盖现有布局文件！", "覆盖确认", "继续", "取消", IsWarn: true) == 2)
                        return;
                }
                ModBase.ExtractResources(Paths.Base + @"PCL\Custom.xaml", "Custom");
                ModMain.Hint("教学文件已生成！", ModMain.HintType.Green);
                ModBase.OpenExplorer(Paths.Base + @"PCL\Custom.xaml");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "生成教学文件失败");
            }
        }
        private void BtnCustomRefresh_Click()
        {
            if (ModMain.FrmLaunchRight is null)
            {
                Logger.Warn("刷新主页时主页面板尚未就绪");
                ModMain.Hint("主页尚未就绪，请稍后重试", ModMain.HintType.Red);
                return;
            }
            ModMain.FrmLaunchRight.ForceRefresh();
            ModMain.Hint("已刷新主页！", ModMain.HintType.Green);
        }
        private void BtnCustomTutorial_Click(object sender, EventArgs e)
        {
            ModMain.MyMsgBox("1. 点击 生成教学文件 按钮，这会在 PCL 文件夹下生成 Custom.xaml 布局文件。" + Constants.vbCrLf + "2. 使用记事本等工具打开这个文件并进行修改，修改完记得保存。" + Constants.vbCrLf + "3. 点击 刷新主页 按钮，查看主页现在长啥样了。" + Constants.vbCrLf + Constants.vbCrLf + "你可以在生成教学文件后直接刷新主页，对照着进行修改，更有助于理解。" + Constants.vbCrLf + "直接将主页文件拖进 PCL Next 窗口也可以快捷加载。", "主页自定义教程");
        }
        public static void OnMainPageTypeChanged()
        {
            if (ModMain.FrmSetupHome is null)
                return;
            switch (Settings.Get<int>("UiCustomType"))
            {
                case 0: // 无
                    {
                        ModMain.FrmSetupHome.PanCustomPreset.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.PanCustomLocal.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.PanCustomNet.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.HintCustom.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.HintCustomWarn.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 1: // 本地
                    {
                        ModMain.FrmSetupHome.PanCustomPreset.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.PanCustomLocal.Visibility = Visibility.Visible;
                        ModMain.FrmSetupHome.PanCustomNet.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.HintCustom.Visibility = Visibility.Visible;
                        ModMain.FrmSetupHome.HintCustomWarn.Visibility = Settings.Get<bool>("HintCustomWarn") ? Visibility.Collapsed : Visibility.Visible;
                        ModMain.FrmSetupHome.HintCustom.Text = $"从 PCL 文件夹下的 Custom.xaml 读取主页内容。{Constants.vbCrLf}你可以手动编辑该文件，向主页添加文本、图片、常用网站、快捷启动等功能。";
                        CustomEventService.SetEventType(ModMain.FrmSetupHome.HintCustom, CustomEvent.EventType.None);
                        break;
                    }
                case 2: // 联网
                    {
                        ModMain.FrmSetupHome.PanCustomPreset.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.PanCustomLocal.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.PanCustomNet.Visibility = Visibility.Visible;
                        ModMain.FrmSetupHome.HintCustom.Visibility = Visibility.Visible;
                        ModMain.FrmSetupHome.HintCustomWarn.Visibility = Settings.Get<bool>("HintCustomWarn") ? Visibility.Collapsed : Visibility.Visible;
                        ModMain.FrmSetupHome.HintCustom.Text = $"从指定网址联网获取主页内容。服主也可以用于动态更新服务器公告。{Constants.vbCrLf}如果你制作了稳定运行的联网主页，可以点击这条提示投稿，若合格即可加入预设！";
                        CustomEventService.SetEventType(ModMain.FrmSetupHome.HintCustom, CustomEvent.EventType.打开网页);
                        CustomEventService.SetEventData(ModMain.FrmSetupHome.HintCustom, "https://github.com/Meloong-Git/PCL/discussions/2528");
                        break;
                    }
                case 3: // 预设
                    {
                        ModMain.FrmSetupHome.PanCustomPreset.Visibility = Visibility.Visible;
                        ModMain.FrmSetupHome.PanCustomLocal.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.PanCustomNet.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.HintCustom.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupHome.HintCustomWarn.Visibility = Visibility.Collapsed;
                        break;
                    }
            }
            ModMain.FrmSetupHome.CardCustom.TriggerForceResize();
        }
        private void BtnCustomFolder_Click(object sender, MouseButtonEventArgs e)
        {
            string CustomPath = Paths.Base + @"PCL\Custom.xaml";
            if (FileUtils.Exists(CustomPath))
            {
                ModBase.OpenExplorer(CustomPath);
            }
            else
            {
                ModBase.OpenExplorer(Paths.Base + @"PCL\");
            }
        }

        #region 功能隐藏

        private static bool _HiddenForceShow = false;
        /// <summary>
        /// 是否强制显示被禁用的功能。
        /// </summary>
        public static bool HiddenForceShow
        {
            get
            {
                return _HiddenForceShow;
            }
            set
            {
                _HiddenForceShow = value;
                HiddenRefresh();
            }
        }

        /// <summary>
        /// 更新功能隐藏带来的显示变化。
        /// </summary>
        public static void HiddenRefresh()
        {
            if (ModMain.FrmMain is null || ModMain.FrmMain.PanTitleSelect is null || !ModMain.FrmMain.PanTitleSelect.IsLoaded)
                return;
            try
            {
                // 顶部栏
                if (!HiddenForceShow && Settings.Get<bool>("UiHiddenPageDownload") && Settings.Get<bool>("UiHiddenPageLink") && Settings.Get<bool>("UiHiddenPageSetup") && Settings.Get<bool>("UiHiddenPageOther"))
                {
                    // 顶部栏已被全部隐藏
                    ModMain.FrmMain.PanTitleSelect.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // 顶部栏未被全部隐藏
                    ModMain.FrmMain.PanTitleSelect.Visibility = Visibility.Visible;
                    ModMain.FrmMain.BtnTitleSelect1.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenPageDownload") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmMain.BtnTitleSelect2.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenPageLink") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmMain.BtnTitleSelect3.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenPageSetup") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmMain.BtnTitleSelect4.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenPageOther") ? Visibility.Collapsed : Visibility.Visible;
                }
                // 功能
                ModMain.FrmLaunchLeft?.RefreshButtonsUI();
                if (ModMain.FrmSetupHome is not null)
                {
                    ModMain.FrmSetupHome.CardSwitch.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenFunctionHidden") ? Visibility.Collapsed : Visibility.Visible;
                }
                // 版本设置子页面
                if (ModMain.FrmInstanceLeft is not null)
                {
                    ModMain.FrmInstanceLeft.ItemModify.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenFunctionInstanceModify") ? Visibility.Collapsed : Visibility.Visible;
                }
                // 设置子页面（导航项与分组标题按隐藏设置统一刷新，见 PageSetupLeft.RefreshHiddenItems）
                ModMain.FrmSetupLeft?.RefreshHiddenItems();
                // 更多子页面
                int OtherAvaliableCount = 0;
                if (!Settings.Get<bool>("UiHiddenOtherHelp"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherAbout"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherTest"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherFeedback"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherVote"))
                    OtherAvaliableCount += 1;
                if (ModMain.FrmOtherLeft is not null)
                {
                    ModMain.FrmOtherLeft.ItemHelp.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherHelp") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemFeedback.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherFeedback") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemVote.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherVote") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemAbout.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherAbout") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemTest.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherTest") ? Visibility.Collapsed : Visibility.Visible;
                    // 隐藏左边选择卡
                    ModMain.FrmOtherLeft.PanItem.Visibility = OtherAvaliableCount < 2 && !HiddenForceShow ? Visibility.Collapsed : Visibility.Visible;
                }
                if (OtherAvaliableCount == 1 && !HiddenForceShow)
                {
                    if (!Settings.Get<bool>("UiHiddenOtherHelp"))
                    {
                        ModMain.FrmMain.BtnTitleSelect4.Text = "帮助";
                    }
                    else if (!Settings.Get<bool>("UiHiddenOtherAbout"))
                    {
                        ModMain.FrmMain.BtnTitleSelect4.Text = "关于";
                    }
                    else
                    {
                        ModMain.FrmMain.BtnTitleSelect4.Text = "百宝箱";
                    }
                }
                else
                {
                    ModMain.FrmMain.BtnTitleSelect4.Text = "更多";
                }
                // 各个页面的入口
                if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSelect)
                    ModMain.FrmSelectRight?.BtnEmptyDownload_Loaded();
                if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.Launch)
                    ModMain.FrmLaunchLeft?.RefreshButtonsUI();
                if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSetup && ModMain.FrmInstanceModDisabled is not null)
                    ModMain.FrmInstanceModDisabled.BtnDownload_Loaded();
                // 备注
                if (ModMain.FrmSetupHome is not null)
                    ModMain.FrmSetupHome.CardSwitch.Title = HiddenForceShow ? "功能隐藏（已暂时关闭，按 F12 以重新启用）" : "功能隐藏";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "刷新功能隐藏项目失败");
            }
        }

        // UI 协同改变
        private void HiddenSetupMain()
        {
            // 设置主页面
            if (this.CheckHiddenPageSetup.Checked)
            {
                // 开启
                this.CheckHiddenSetupLaunch.Checked = true;
                this.CheckHiddenSetupSystem.Checked = true;
                this.CheckHiddenSetupLink.Checked = true;
                this.CheckHiddenSetupUI.Checked = true;
            }
            // 关闭
            else if (Settings.Get<bool>("UiHiddenSetupLaunch") && Settings.Get<bool>("UiHiddenSetupUi") && Settings.Get<bool>("UiHiddenSetupSystem") && Settings.Get<bool>("UiHiddenSetupLink"))
            {
                this.CheckHiddenSetupLaunch.Checked = false;
                this.CheckHiddenSetupSystem.Checked = false;
                this.CheckHiddenSetupLink.Checked = false;
                this.CheckHiddenSetupUI.Checked = false;
            }
        }
        private void HiddenSetupSub()
        {
            // 设置子页面
            if (Settings.Get<bool>("UiHiddenSetupLaunch") && Settings.Get<bool>("UiHiddenSetupUi") && Settings.Get<bool>("UiHiddenSetupSystem") && Settings.Get<bool>("UiHiddenSetupLink"))
            {
                // 已被全部隐藏
                this.CheckHiddenPageSetup.Checked = true;
            }
            else
            {
                // 未被全部隐藏
                this.CheckHiddenPageSetup.Checked = false;
            }
        }
        private void HiddenOtherMain()
        {
            // 更多主页面
            if (this.CheckHiddenPageOther.Checked)
            {
                // 开启
                this.CheckHiddenOtherAbout.Checked = true;
                this.CheckHiddenOtherTest.Checked = true;
                this.CheckHiddenOtherFeedback.Checked = true;
                this.CheckHiddenOtherVote.Checked = true;
                this.CheckHiddenOtherHelp.Checked = true;
            }
            // 关闭
            else if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest") && Settings.Get<bool>("UiHiddenOtherVote") && Settings.Get<bool>("UiHiddenOtherFeedback"))
            {
                this.CheckHiddenOtherAbout.Checked = false;
                this.CheckHiddenOtherTest.Checked = false;
                this.CheckHiddenOtherFeedback.Checked = false;
                this.CheckHiddenOtherVote.Checked = false;
                this.CheckHiddenOtherHelp.Checked = false;
            }
        }
        private void HiddenOtherSub(object sender, bool user)
        {
            // 更多子页面（有具体内容的）
            if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest"))
            {
                // 已被全部隐藏
                this.CheckHiddenPageOther.Checked = true;
            }
            else
            {
                // 未被全部隐藏
                this.CheckHiddenPageOther.Checked = false;
            }
            // 修改无具体内容的项
            if (!user)
                return;
            if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest"))
            {
                this.CheckHiddenOtherFeedback.Checked = true;
                this.CheckHiddenOtherVote.Checked = true;
            }
        }
        private void HiddenOtherNet(object sender, bool user)
        {
            // 更多子页面（无具体内容的）
            if (!user)
                return;
            if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest") && (!Settings.Get<bool>("UiHiddenOtherFeedback") || !Settings.Get<bool>("UiHiddenOtherVote")))
            {
                this.CheckHiddenOtherAbout.Checked = false;
                this.CheckHiddenOtherTest.Checked = false;
                this.CheckHiddenOtherHelp.Checked = false;
            }
        }

        // 警告提示
        private void HiddenHint(object sender, bool user)
        {
            if (Conversions.ToBoolean(ModAnimation.AniControlEnabled == 0 && ((dynamic)sender).Checked))
                ModMain.Hint("按 F12 即可暂时关闭功能隐藏设置。千万别忘了，要不然设置就改不回来了……");
        }

        #endregion

    }
}
