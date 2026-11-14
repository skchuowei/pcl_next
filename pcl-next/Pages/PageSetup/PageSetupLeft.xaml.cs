using System;
using System.Windows;
using System.Windows.Controls;
using MeloongCore;
using Microsoft.VisualBasic;

namespace PCL
{
    public partial class PageSetupLeft
    {

        private bool IsLoad = false;
        private bool IsPageSwitched = false; // 如果在 Loaded 前切换到其他页面，会导致触发 Loaded 时再次切换一次
        private void PageSetupLeft_Loaded(object sender, RoutedEventArgs e)
        {
            // 是否处于隐藏的子页面
            bool IsHiddenPage = false;
            if (this.ItemLaunch.Checked && Settings.Get<bool>("UiHiddenSetupLaunch"))
                IsHiddenPage = true;
            if (this.ItemUI.Checked && Settings.Get<bool>("UiHiddenSetupUi"))
                IsHiddenPage = true;
            if (this.ItemBackdrop.Checked && Settings.Get<bool>("UiHiddenSetupUi"))
                IsHiddenPage = true;
            if (this.ItemHome.Checked && Settings.Get<bool>("UiHiddenSetupUi"))
                IsHiddenPage = true;
            if (this.ItemDownload.Checked && Settings.Get<bool>("UiHiddenSetupSystem"))
                IsHiddenPage = true;
            if (this.ItemNotify.Checked && Settings.Get<bool>("UiHiddenSetupSystem"))
                IsHiddenPage = true;
            if (this.ItemAccount.Checked && Settings.Get<bool>("UiHiddenSetupSystem"))
                IsHiddenPage = true;
            if (this.ItemSystem.Checked && Settings.Get<bool>("UiHiddenSetupSystem"))
                IsHiddenPage = true;
            if (this.ItemUIAdv.Checked && Settings.Get<bool>("UiHiddenSetupSystem"))
                IsHiddenPage = true;
            if (this.ItemLink.Checked && Settings.Get<bool>("UiHiddenSetupLink"))
                IsHiddenPage = true;
            if (PageSetupHome.HiddenForceShow)
                IsHiddenPage = false;
            // 若页面错误，或尚未加载，则继续
            if (IsLoad && !IsHiddenPage)
                return;
            IsLoad = true;
            // 刷新子页面隐藏情况
            PageSetupHome.HiddenRefresh();
            // 选择第一个未被禁用的子页面
            if (IsPageSwitched)
                return;
            if (!Settings.Get<bool>("UiHiddenSetupLaunch"))
            {
                this.ItemLaunch.SetChecked(true, false, false);
            }
            else if (!Settings.Get<bool>("UiHiddenSetupUi"))
            {
                this.ItemUI.SetChecked(true, false, false);
            }
            else if (!Settings.Get<bool>("UiHiddenSetupSystem"))
            {
                this.ItemDownload.SetChecked(true, false, false);
            }
            else if (!Settings.Get<bool>("UiHiddenSetupLink"))
            {
                this.ItemLink.SetChecked(true, false, false);
            }
            else
            {
                this.ItemLaunch.SetChecked(true, false, false);
            }
        }
        private void PageOtherLeft_Unloaded(object sender, RoutedEventArgs e)
        {
            IsPageSwitched = false;
        }

        /// <summary>
        /// 按「功能隐藏」设置同步各个导航项与分组标题的可见性。
        /// 三个旧设置键的语义映射：Launch → 启动；Ui → 外观/背景与音乐/主页与入口；
        /// System → 下载与资源/通知与更新/账号与皮肤/调试与实验/界面试验台；Link → 联机。
        /// </summary>
        public void RefreshHiddenItems()
        {
            bool ForceShow = PageSetupHome.HiddenForceShow;
            bool HideGame = !ForceShow && Settings.Get<bool>("UiHiddenSetupLaunch");
            bool HideUi = !ForceShow && Settings.Get<bool>("UiHiddenSetupUi");
            bool HideSystem = !ForceShow && Settings.Get<bool>("UiHiddenSetupSystem");
            bool HideLink = !ForceShow && Settings.Get<bool>("UiHiddenSetupLink");
            this.ItemLaunch.Visibility = HideGame ? Visibility.Collapsed : Visibility.Visible;
            this.ItemLink.Visibility = HideLink ? Visibility.Collapsed : Visibility.Visible;
            this.ItemUI.Visibility = HideUi ? Visibility.Collapsed : Visibility.Visible;
            this.ItemBackdrop.Visibility = HideUi ? Visibility.Collapsed : Visibility.Visible;
            this.ItemHome.Visibility = HideUi ? Visibility.Collapsed : Visibility.Visible;
            this.ItemDownload.Visibility = HideSystem ? Visibility.Collapsed : Visibility.Visible;
            this.ItemNotify.Visibility = HideSystem ? Visibility.Collapsed : Visibility.Visible;
            this.ItemAccount.Visibility = HideSystem ? Visibility.Collapsed : Visibility.Visible;
            this.ItemSystem.Visibility = HideSystem ? Visibility.Collapsed : Visibility.Visible;
            this.ItemUIAdv.Visibility = HideSystem ? Visibility.Collapsed : Visibility.Visible;
            // 分组标题：组内还有可见项时才显示
            this.LabGroupGame.Visibility = (!HideGame || !ForceShow) ? Visibility.Visible : Visibility.Collapsed;
            this.LabGroupLauncher.Visibility = !HideUi ? Visibility.Visible : Visibility.Collapsed;
            this.LabGroupAdvanced.Visibility = !HideSystem ? Visibility.Visible : Visibility.Collapsed;
            // 全部隐藏时收回整栏
            int AvaliableCount = 0;
            if (!Settings.Get<bool>("UiHiddenSetupLaunch"))
                AvaliableCount += 1;
            if (!Settings.Get<bool>("UiHiddenSetupUi"))
                AvaliableCount += 1;
            if (!Settings.Get<bool>("UiHiddenSetupSystem"))
                AvaliableCount += 1;
            this.PanItem.Visibility = AvaliableCount < 2 && !ForceShow ? Visibility.Collapsed : Visibility.Visible;
        }

        #region 页面切换

        /// <summary>
        /// 当前页面的编号。按导航顺序从 0 开始计算。
        /// </summary>
        public FormMain.PageSubType PageID;
        public PageSetupLeft()
        {
            this.InitializeComponent();
            this.ItemLaunch.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemLink.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemUI.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemBackdrop.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemHome.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemDownload.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemNotify.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemAccount.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemSystem.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            this.ItemUIAdv.Check += (p1, p2) => PageCheck((MyListItem)p1, (EventArgs)p2);
            // 选择第一个未被禁用的子页面
            if (!Settings.Get<bool>("UiHiddenSetupLaunch"))
            {
                PageID = FormMain.PageSubType.SetupLaunch;
            }
            else if (!Settings.Get<bool>("UiHiddenSetupUi"))
            {
                PageID = FormMain.PageSubType.SetupUI;
            }
            else if (!Settings.Get<bool>("UiHiddenSetupSystem"))
            {
                PageID = FormMain.PageSubType.SetupDownload;
            }
            else if (!Settings.Get<bool>("UiHiddenSetupLink"))
            {
                PageID = FormMain.PageSubType.SetupLink;
            }
            else
            {
                PageID = FormMain.PageSubType.SetupLaunch;
            }

            this.Loaded += PageSetupLeft_Loaded;
            this.Unloaded += PageOtherLeft_Unloaded;
        }

        /// <summary>
        /// 勾选事件改变页面。
        /// </summary>
        private void PageCheck(MyListItem sender, EventArgs e)
        {
            // 尚未初始化控件属性时，sender.Tag 为 Nothing，会跳过切换，且由于 PageID 默认为 0 而切换到第一个页面
            // 若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
            if (sender.Tag is not null)
                PageChange((FormMain.PageSubType)ModBase.Val(sender.Tag));
        }

        /// <summary>
        /// 获取当前导航指定的右页面。
        /// </summary>
        public object PageGet(FormMain.PageSubType ID = (FormMain.PageSubType)(-1))
        {
            if ((int)ID == -1)
                ID = PageID;
            switch (ID)
            {
                case FormMain.PageSubType.SetupLaunch:
                    {
                        if (ModMain.FrmSetupLaunch is null)
                            ModMain.FrmSetupLaunch = new PageSetupLaunch();
                        return ModMain.FrmSetupLaunch;
                    }
                case FormMain.PageSubType.SetupLink:
                    {
                        if (ModMain.FrmSetupLink is null)
                            ModMain.FrmSetupLink = new PageSetupLink();
                        return ModMain.FrmSetupLink;
                    }
                case FormMain.PageSubType.SetupUI:
                    {
                        if (ModMain.FrmSetupUI is null)
                            ModMain.FrmSetupUI = new PageSetupUI();
                        return ModMain.FrmSetupUI;
                    }
                case FormMain.PageSubType.SetupBackdrop:
                    {
                        if (ModMain.FrmSetupBackdrop is null)
                            ModMain.FrmSetupBackdrop = new PageSetupBackdrop();
                        return ModMain.FrmSetupBackdrop;
                    }
                case FormMain.PageSubType.SetupHome:
                    {
                        if (ModMain.FrmSetupHome is null)
                            ModMain.FrmSetupHome = new PageSetupHome();
                        return ModMain.FrmSetupHome;
                    }
                case FormMain.PageSubType.SetupDownload:
                    {
                        if (ModMain.FrmSetupDownload is null)
                            ModMain.FrmSetupDownload = new PageSetupDownload();
                        return ModMain.FrmSetupDownload;
                    }
                case FormMain.PageSubType.SetupNotify:
                    {
                        if (ModMain.FrmSetupNotify is null)
                            ModMain.FrmSetupNotify = new PageSetupNotify();
                        return ModMain.FrmSetupNotify;
                    }
                case FormMain.PageSubType.SetupAccount:
                    {
                        if (ModMain.FrmSetupAccount is null)
                            ModMain.FrmSetupAccount = new PageSetupAccount();
                        return ModMain.FrmSetupAccount;
                    }
                case FormMain.PageSubType.SetupSystem:
                    {
                        if (ModMain.FrmSetupSystem is null)
                            ModMain.FrmSetupSystem = new PageSetupSystem();
                        return ModMain.FrmSetupSystem;
                    }
                case FormMain.PageSubType.SetupUIAdv:
                    {
                        if (ModMain.FrmSetupUIAdv is null)
                            ModMain.FrmSetupUIAdv = new PageSetupUIAdv();
                        return ModMain.FrmSetupUIAdv;
                    }

                default:
                    {
                        throw new Exception("未知的设置子页面种类：" + ((int)ID).ToString());
                    }
            }
        }

        /// <summary>
        /// 切换现有页面。
        /// </summary>
        public void PageChange(FormMain.PageSubType ID)
        {
            if (PageID == ID)
                return;
            ModAnimation.AniControlEnabled += 1;
            IsPageSwitched = true;
            try
            {

                switch (ID)
                {
                    case FormMain.PageSubType.SetupLaunch:
                        {
                            if (ModMain.FrmSetupLaunch == null)
                                ModMain.FrmSetupLaunch = new PageSetupLaunch();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupLaunch);
                            break;
                        }
                    case FormMain.PageSubType.SetupLink:
                        {
                            if (ModMain.FrmSetupLink == null)
                                ModMain.FrmSetupLink = new PageSetupLink();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupLink);
                            break;
                        }
                    case FormMain.PageSubType.SetupUI:
                        {
                            if (ModMain.FrmSetupUI == null)
                                ModMain.FrmSetupUI = new PageSetupUI();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupUI);
                            break;
                        }
                    case FormMain.PageSubType.SetupBackdrop:
                        {
                            if (ModMain.FrmSetupBackdrop == null)
                                ModMain.FrmSetupBackdrop = new PageSetupBackdrop();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupBackdrop);
                            break;
                        }
                    case FormMain.PageSubType.SetupHome:
                        {
                            if (ModMain.FrmSetupHome == null)
                                ModMain.FrmSetupHome = new PageSetupHome();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupHome);
                            break;
                        }
                    case FormMain.PageSubType.SetupDownload:
                        {
                            if (ModMain.FrmSetupDownload == null)
                                ModMain.FrmSetupDownload = new PageSetupDownload();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupDownload);
                            break;
                        }
                    case FormMain.PageSubType.SetupNotify:
                        {
                            if (ModMain.FrmSetupNotify == null)
                                ModMain.FrmSetupNotify = new PageSetupNotify();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupNotify);
                            break;
                        }
                    case FormMain.PageSubType.SetupAccount:
                        {
                            if (ModMain.FrmSetupAccount == null)
                                ModMain.FrmSetupAccount = new PageSetupAccount();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupAccount);
                            break;
                        }
                    case FormMain.PageSubType.SetupSystem:
                        {
                            if (ModMain.FrmSetupSystem == null)
                                ModMain.FrmSetupSystem = new PageSetupSystem();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupSystem);
                            break;
                        }
                    case FormMain.PageSubType.SetupUIAdv:
                        {
                            if (ModMain.FrmSetupUIAdv == null)
                                ModMain.FrmSetupUIAdv = new PageSetupUIAdv();
                            PageSetupLeft.PageChangeRun(ModMain.FrmSetupUIAdv);
                            break;
                        }

                    default:
                        {
                            throw new Exception("未知的设置子页面种类：" + ((int)ID).ToString());
                        }
                }

                PageID = ID;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"切换分页面失败（ID {ID}）");
            }
            finally
            {
                ModAnimation.AniControlEnabled -= 1;
            }
        }
        private static void PageChangeRun(MyPageRight Target)
        {
            ModAnimation.AniStop("FrmMain PageChangeRight"); // 停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
            if (Target.Parent is not null)
                Target.SetValue(ContentPresenter.ContentProperty, null);
            ModMain.FrmMain.PageRight = Target;
            ((MyPageRight)ModMain.FrmMain.PanMainRight.Child).PageOnExit();
            ModAnimation.AniStart(new[] {
                        ModAnimation.AaCode(() =>
                {
                ((MyPageRight)ModMain.FrmMain.PanMainRight.Child).PageOnForceExit();
                ModMain.FrmMain.PanMainRight.Child = ModMain.FrmMain.PageRight;
                ModMain.FrmMain.PageRight.Opacity = 0d;
            }, 130),
                        ModAnimation.AaCode(() =>
                {
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                ModMain.FrmMain.PageRight.Opacity = 1d;
                ModMain.FrmMain.PageRight.PageOnEnter();
            }, 30, true)
        }, "PageLeft PageChange");
        }

        #endregion

        public void Reset(object sender, EventArgs e)
        {
            switch (ModBase.Val(((dynamic)sender).Tag))
            {
                case (double)FormMain.PageSubType.SetupLaunch:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 启动 页面的所有设置？该操作不可撤销。", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupLaunch == null)
                                ModMain.FrmSetupLaunch = new PageSetupLaunch();
                            ModMain.FrmSetupLaunch.Reset();
                            this.ItemLaunch.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupUI:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 外观 页面的所有设置？该操作不可撤销。" + Constants.vbCrLf + "（背景图片与音乐、主页等外部文件不会被删除）", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupUI == null)
                                ModMain.FrmSetupUI = new PageSetupUI();
                            ModMain.FrmSetupUI.Reset();
                            this.ItemUI.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupBackdrop:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 背景与音乐 页面的所有设置？该操作不可撤销。" + Constants.vbCrLf + "（背景图片与音乐等外部文件不会被删除）", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupBackdrop == null)
                                ModMain.FrmSetupBackdrop = new PageSetupBackdrop();
                            ModMain.FrmSetupBackdrop.Reset();
                            this.ItemBackdrop.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupHome:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 主页与入口 页面的所有设置？该操作不可撤销。" + Constants.vbCrLf + "（主页文件等外部内容不会被删除）", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupHome == null)
                                ModMain.FrmSetupHome = new PageSetupHome();
                            ModMain.FrmSetupHome.Reset();
                            this.ItemHome.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupDownload:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 下载与资源 页面的所有设置？该操作不可撤销。", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupDownload == null)
                                ModMain.FrmSetupDownload = new PageSetupDownload();
                            ModMain.FrmSetupDownload.Reset();
                            this.ItemDownload.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupNotify:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 通知与更新 页面的所有设置？该操作不可撤销。", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupNotify == null)
                                ModMain.FrmSetupNotify = new PageSetupNotify();
                            ModMain.FrmSetupNotify.Reset();
                            this.ItemNotify.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupSystem:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 调试与实验 页面的所有设置？该操作不可撤销。", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupSystem == null)
                                ModMain.FrmSetupSystem = new PageSetupSystem();
                            ModMain.FrmSetupSystem.Reset();
                            this.ItemSystem.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupLink:
                    {
                        if (ModMain.MyMsgBox("是否要初始化 联机 页面的所有设置？该操作不可撤销。", "初始化确认", Button2: "取消", IsWarn: true) == 1)
                        {
                            if (ModMain.FrmSetupLink == null)
                                ModMain.FrmSetupLink = new PageSetupLink();
                            ModMain.FrmSetupLink.Reset();
                            this.ItemLink.Checked = true;
                        }

                        break;
                    }
                case (double)FormMain.PageSubType.SetupUIAdv:
                    {
                        if (ModMain.FrmSetupUIAdv == null)
                            ModMain.FrmSetupUIAdv = new PageSetupUIAdv();
                        ModMain.FrmSetupUIAdv.Reset();
                        this.ItemUIAdv.Checked = true;
                        break;
                    }
                case (double)FormMain.PageSubType.SetupAccount:
                    {
                        if (ModMain.FrmSetupAccount == null)
                            ModMain.FrmSetupAccount = new PageSetupAccount();
                        ModMain.FrmSetupAccount.Reset();
                        this.ItemAccount.Checked = true;
                        break;
                    }
            }
        }

    }
}
