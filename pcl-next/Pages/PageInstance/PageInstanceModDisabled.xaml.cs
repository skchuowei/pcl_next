using System;
using System.Windows;

namespace PCL
{
    public partial class PageInstanceModDisabled
    {

        public PageInstanceModDisabled()
        {
            InitializeComponent();
            this.BtnDownload.Click += (p1, p2) => BtnDownload_Click((object)p1, (EventArgs)p2);
            this.BtnVersion.Click += (p1, p2) => BtnVersion_Click((object)p1, (EventArgs)p2);
            this.BtnDownload.Loaded += (p1, p2) => BtnDownload_Loaded();
        }

        private void BtnDownload_Click(object sender, EventArgs e)
        {
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall);
        }
        private void BtnVersion_Click(object sender, EventArgs e)
        {
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Launch); // 在版本选择页面选定版本的时候只会返回一层，因此如果不先锚定 Launch，在选择版本后会回退到版本设置的这个页面
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.InstanceSelect);
        }

        public void BtnDownload_Loaded()
        {
            var NewVisibility = ModDownloadPolicy.RestrictMcDownload || Settings.Get<bool>("UiHiddenPageDownload") && !PageSetupHome.HiddenForceShow || (ModMain.FrmSelectRight is null ? false : ModMain.FrmSelectRight.ShowHidden) ? Visibility.Collapsed : Visibility.Visible;
            if (this.BtnDownload.Visibility != NewVisibility)
            {
                this.BtnDownload.Visibility = NewVisibility;
                this.PanMain.TriggerForceResize();
            }
        }

    }
}