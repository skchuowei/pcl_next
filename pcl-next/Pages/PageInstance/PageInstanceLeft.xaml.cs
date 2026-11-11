using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MeloongCore;
using PCLCS;

namespace PCL
{
    public partial class PageInstanceLeft : IRefreshable
    {

        /// <summary>
    /// 当前显示设置的 MC 版本。
    /// </summary>
        public static ModMinecraft.McInstance Instance = null;

        public PageInstanceLeft()
        {
            InitializeComponent();
            this.ItemOverall.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemMod.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemModDisabled.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemSetup.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemModify.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemExport.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.Loaded += PageInstanceLeft_Loaded;
            this.Loaded += (_, __) => RefreshModDisabled();
            this.Loaded += (_, __) => ReloadCurrentJava();
        }

        private void PageInstanceLeft_Loaded(object sender, RoutedEventArgs e)
        {
            PageSetupUI.HiddenRefresh();
        }

        public void RefreshModDisabled()
        {
            if (Instance is not null && Instance.Modable)
            {
                this.ItemMod.Visibility = Visibility.Visible;
                this.ItemModDisabled.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.ItemMod.Visibility = Visibility.Collapsed;
                this.ItemModDisabled.Visibility = Visibility.Visible;
            }
        }

        #region 页面切换

        /// <summary>
    /// 当前页面的编号。从 0 开始计算。
    /// </summary>
        public FormMain.PageSubType PageID = FormMain.PageSubType.Default;

        /// <summary>
    /// 勾选事件改变页面。
    /// </summary>
        private void PageCheck(FrameworkElement sender, ModBase.RouteEventArgs e)
        {
            // 尚未初始化控件属性时，sender.Tag 为 Nothing，会导致切换到页面 0
            // 若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
            if (sender.Tag is not null)
                PageChange((FormMain.PageSubType)ModBase.Val(sender.Tag));
        }

        public object PageGet(FormMain.PageSubType ID = (FormMain.PageSubType)(-1))
        {
            if ((int)ID == -1)
                ID = PageID;
            switch (ID)
            {
                case FormMain.PageSubType.InstanceOverall:
                    {
                        if (ModMain.FrmInstanceOverall is null)
                            ModMain.FrmInstanceOverall = new PageInstanceOverall();
                        return ModMain.FrmInstanceOverall;
                    }
                case FormMain.PageSubType.InstanceMod:
                    {
                        if (ModMain.FrmInstanceMod is null)
                            ModMain.FrmInstanceMod = new PageInstanceMod();
                        return ModMain.FrmInstanceMod;
                    }
                case FormMain.PageSubType.InstanceModDisabled:
                    {
                        if (ModMain.FrmInstanceModDisabled is null)
                            ModMain.FrmInstanceModDisabled = new PageInstanceModDisabled();
                        return ModMain.FrmInstanceModDisabled;
                    }
                case FormMain.PageSubType.InstanceSetup:
                    {
                        if (ModMain.FrmInstanceSetup is null)
                            ModMain.FrmInstanceSetup = new PageInstanceSetup();
                        return ModMain.FrmInstanceSetup;
                    }
                case FormMain.PageSubType.InstanceExport:
                    {
                        if (ModMain.FrmInstanceExport is null)
                            ModMain.FrmInstanceExport = new PageInstanceExport();
                        return ModMain.FrmInstanceExport;
                    }
                case FormMain.PageSubType.InstanceModify:
                    {
                        if (ModMain.FrmDownloadModify is null)
                            ModMain.FrmDownloadModify = new PageDownloadModify();
                        return ModMain.FrmDownloadModify;
                    }

                default:
                    {
                        throw new Exception("未知的版本设置子页面种类：" + ((int)ID).ToString());
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
            if (Instance is not null && ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该版本正在被修改，请稍后再切换页面！", ModMain.HintType.Red);
                return;
            }
            ModAnimation.AniControlEnabled += 1;
            try
            {
                PageChangeRun((MyPageRight)PageGet(ID));
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

        public void Refresh_Click(object sender, EventArgs e) // 由边栏按钮匿名调用
        {
            Refresh((FormMain.PageSubType)ModBase.Val(((dynamic)sender).Tag));
        }
        public void Refresh()
        {
            Refresh(ModMain.FrmMain.PageCurrentSub);
        }
        public void Refresh(FormMain.PageSubType SubType)
        {
            if (Instance is not null && ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该版本正在被修改，请稍后再刷新！", ModMain.HintType.Red);
                return;
            }
            switch (SubType)
            {
                case FormMain.PageSubType.InstanceMod:
                    {
                        PageInstanceMod.Refresh();
                        this.ItemMod.Checked = true;
                        break;
                    }
                case FormMain.PageSubType.InstanceExport:
                    {
                        ModMain.FrmInstanceExport?.RefreshAll();
                        this.ItemExport.Checked = true;
                        break;
                    }
                case FormMain.PageSubType.InstanceModify:
                    {
                        ModMain.FrmDownloadModify?.PageLoaderRestart();
                        this.ItemModify.Checked = true;
                        break;
                    }
            }
        }

        public void Reset(object sender, EventArgs e)
        {
            if (ModMain.MyMsgBox("是否要初始化该版本的版本独立设置？该操作不可撤销。", "初始化确认", Button2: "取消", IsWarn: true) == 1)
            {
                if (ModMain.FrmInstanceSetup == null)
                    ModMain.FrmInstanceSetup = new PageInstanceSetup();
                ModMain.FrmInstanceSetup.Reset();
                this.ItemSetup.Checked = true;
            }
        }

        // 加载当前版本将要选择的 Java
        public static RedoableWorker<Java> CurrentJavaWorker = new RedoableWorker<Java>((c, p) => ModJava.SelectOrDownloadJava(Instance, false, (CancellationToken)c, p));
        public static void ReloadCurrentJava()
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (Instance is null)
                return;
            CurrentJavaWorker.Start();
        }

    }
}