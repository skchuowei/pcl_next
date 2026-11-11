using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;

namespace PCL
{
    public partial class PageDownloadLeft : IRefreshable
    {

        #region 页面切换

        /// <summary>
    /// 当前页面的编号。
    /// </summary>
        public FormMain.PageSubType PageID = FormMain.PageSubType.DownloadInstall;

        // 隐藏 MC 下载
        public PageDownloadLeft()
        {
            this.InitializeComponent();
            this.ItemInstall.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemMod.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemPack.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemResourcePack.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemShader.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            this.ItemDataPack.Check += (p1, p2) => PageCheck((FrameworkElement)p1, (ModBase.RouteEventArgs)p2);
            if (!ModDownloadPolicy.RestrictMcDownload)
                return;
            this.ItemInstall.Visibility = Visibility.Collapsed;
            this.LabResource.Visibility = Visibility.Collapsed;
            PageID = FormMain.PageSubType.DownloadMod;
            this.ItemMod.Checked = true;
        }

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
                case FormMain.PageSubType.DownloadInstall:
                    {
                        if (ModMain.FrmDownloadInstall is null)
                            ModMain.FrmDownloadInstall = new PageDownloadInstall();
                        return ModMain.FrmDownloadInstall;
                    }
                case FormMain.PageSubType.DownloadMod:
                    {
                        if (ModMain.FrmDownloadMod is null)
                            ModMain.FrmDownloadMod = new PageDownloadMod();
                        return ModMain.FrmDownloadMod;
                    }
                case FormMain.PageSubType.DownloadPack:
                    {
                        if (ModMain.FrmDownloadPack is null)
                            ModMain.FrmDownloadPack = new PageDownloadPack();
                        return ModMain.FrmDownloadPack;
                    }
                case FormMain.PageSubType.DownloadResourcePack:
                    {
                        if (ModMain.FrmDownloadResourcePack is null)
                            ModMain.FrmDownloadResourcePack = new PageDownloadResourcePack();
                        return ModMain.FrmDownloadResourcePack;
                    }
                case FormMain.PageSubType.DownloadShader:
                    {
                        if (ModMain.FrmDownloadShader is null)
                            ModMain.FrmDownloadShader = new PageDownloadShader();
                        return ModMain.FrmDownloadShader;
                    }
                case FormMain.PageSubType.DownloadDataPack:
                    {
                        if (ModMain.FrmDownloadDataPack is null)
                            ModMain.FrmDownloadDataPack = new PageDownloadDataPack();
                        return ModMain.FrmDownloadDataPack;
                    }

                default:
                    {
                        throw new Exception("未知的下载子页面种类：" + ((int)ID).ToString());
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

        // 强制刷新
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
            ResourceProject.Cache.Clear();
            ResourceVersion.ClearCaches();
            switch (SubType)
            {
                case FormMain.PageSubType.DownloadInstall:
                    {
                        ModDownload.DlClientListLoader.Start(IsForceRestart: true);
                        ModDownload.DlOptiFineListLoader.Start(IsForceRestart: true);
                        ModDownload.DlForgeListLoader.Start(IsForceRestart: true);
                        ModDownload.DlNeoForgeListLoader.Start(IsForceRestart: true);
                        ModDownload.DlLiteLoaderListLoader.Start(IsForceRestart: true);
                        ModDownload.DlFabricListLoader.Start(IsForceRestart: true);
                        ModDownload.DlFabricApiLoader.Start(IsForceRestart: true);
                        ModDownload.DlOptiFabricLoader.Start(IsForceRestart: true);
                        this.ItemInstall.Checked = true;
                        break;
                    }
                case FormMain.PageSubType.DownloadMod:
                    {
                        if (ModMain.FrmDownloadMod is not null)
                        {
                            ModMain.FrmDownloadMod.Content.Storage = new ResourceSearcher.SearchResult();
                            ModMain.FrmDownloadMod.Content.Page = 0;
                            ModMain.FrmDownloadMod.PageLoaderRestart();
                        }
                        this.ItemMod.Checked = true;
                        break;
                    }
                case FormMain.PageSubType.DownloadPack:
                    {
                        if (ModMain.FrmDownloadPack is not null)
                        {
                            ModMain.FrmDownloadPack.Content.Storage = new ResourceSearcher.SearchResult();
                            ModMain.FrmDownloadPack.Content.Page = 0;
                            ModMain.FrmDownloadPack.PageLoaderRestart();
                        }
                        this.ItemPack.Checked = true;
                        break;
                    }
                case FormMain.PageSubType.DownloadResourcePack:
                    {
                        if (ModMain.FrmDownloadResourcePack is not null)
                        {
                            ModMain.FrmDownloadResourcePack.Content.Storage = new ResourceSearcher.SearchResult();
                            ModMain.FrmDownloadResourcePack.Content.Page = 0;
                            ModMain.FrmDownloadResourcePack.PageLoaderRestart();
                        }
                        this.ItemResourcePack.Checked = true;
                        break;
                    }
                case FormMain.PageSubType.DownloadShader:
                    {
                        if (ModMain.FrmDownloadShader is not null)
                        {
                            ModMain.FrmDownloadShader.Content.Storage = new ResourceSearcher.SearchResult();
                            ModMain.FrmDownloadShader.Content.Page = 0;
                            ModMain.FrmDownloadShader.PageLoaderRestart();
                        }
                        this.ItemShader.Checked = true;
                        break;
                    }
                case FormMain.PageSubType.DownloadDataPack:
                    {
                        if (ModMain.FrmDownloadDataPack is not null)
                        {
                            ModMain.FrmDownloadDataPack.Content.Storage = new ResourceSearcher.SearchResult();
                            ModMain.FrmDownloadDataPack.Content.Page = 0;
                            ModMain.FrmDownloadDataPack.PageLoaderRestart();
                        }
                        this.ItemDataPack.Checked = true;
                        break;
                    }
            }
            ModMain.Hint("正在刷新……", Log: false);
        }

        // 点击返回
        private void ItemInstall_Click(object sender, MouseButtonEventArgs e)
        {
            if (!this.ItemInstall.Checked)
                return;
            ModMain.FrmDownloadInstall.ExitSelectPage();
        }

    }
}