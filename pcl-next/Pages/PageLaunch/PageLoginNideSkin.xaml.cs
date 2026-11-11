using System.Windows;
using System.Windows.Input;

namespace PCL
{
    public partial class PageLoginNideSkin
    {

        public PageLoginNideSkin()
        {
            this.InitializeComponent();
            this.PanData.MouseEnter += (p1, p2) => PageLoginNideSkin_MouseEnter((object)p1, (MouseEventArgs)p2);
            this.PanData.MouseLeave += (p1, p2) => PageLoginNideSkin_MouseLeave((object)p1, (MouseEventArgs)p2);
            this.Skin.Click += (p1, p2) => Skin_Click((object)p1, (MouseButtonEventArgs)p2);
            // VB 的 Handles BtnExit.Click 迁移时被丢弃 → 皮肤登录页的「退出登录」点了没反应（2026-11-11 补）。
            this.BtnExit.Click += (p1, p2) => ExitLogin();
            this.Skin.Loader = PageLaunchLeft.SkinNide;
            this.Loaded += PageLoginLegacy_Loaded;
        }
        private void PageLoginLegacy_Loaded(object sender, RoutedEventArgs e)
        {
            this.Skin.Loader.Start();
        }

        /// <summary>
    /// 刷新页面显示的所有信息。
    /// </summary>
        public void Reload(bool KeepInput)
        {
            this.TextName.Text = Settings.Get<string>("CacheNideName");
            this.TextEmail.Text = Settings.Get<string>("CacheNideUsername");
            this.TextEmail.Visibility = (!Settings.Get<bool>("UiLauncherEmail")).ToVisibility();
            // 皮肤在 Loaded 加载
        }
        /// <summary>
    /// 获取当前页面的登录信息。
    /// </summary>
        public static ModLaunch.McLoginServer GetLoginData()
        {
            string Server = ModMinecraft.McInstanceSelected == null ? Settings.Get<string>("CacheNideServer") : Settings.Get<string>("VersionServerNide", Instance: ModMinecraft.McInstanceSelected);
            return new ModLaunch.McLoginServer(ModLaunch.McLoginType.Nide) { Token = "Nide", UserName = Settings.Get<string>("CacheNideUsername"), Password = Settings.Get<string>("CacheNidePass"), Description = "统一通行证", Type = ModLaunch.McLoginType.Nide, BaseUrl = "https://auth.mc-user.com:233/" + Server + "/authserver" };
        }

        private void PageLoginNideSkin_MouseEnter(object sender, MouseEventArgs e)
        {
            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.BtnEdit, 1d - this.BtnEdit.Opacity, 80), ModAnimation.AaHeight(this.BtnEdit, 25.5d - this.BtnEdit.Height, 140, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnEdit, -1.5d, 50, 140, new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(this.BtnExit, 1d - this.BtnExit.Opacity, 80), ModAnimation.AaHeight(this.BtnExit, 25.5d - this.BtnExit.Height, 140, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnExit, -1.5d, 50, 140, new ModAnimation.AniEaseInFluent()) }, "PageLoginNideSkin Button");
        }
        private void PageLoginNideSkin_MouseLeave(object sender, MouseEventArgs e)
        {
            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.BtnEdit, -this.BtnEdit.Opacity, 120, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnEdit, 14d - this.BtnEdit.Height, 120, Ease: new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(this.BtnExit, -this.BtnExit.Opacity, 120, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnExit, 14d - this.BtnExit.Height, 120, Ease: new ModAnimation.AniEaseInFluent()) }, "PageLoginNideSkin Button");
        }

        public static void ExitLogin()
        {
            Settings.Set("CacheNideAccess", "");
            ModLaunch.McLoginNideLoader.Input = null; // 防止因为输入的用户名密码相同，直接使用了上次登录的加载器结果
            ModMain.FrmLaunchLeft.RefreshPage(false, true);
        }

        private void Skin_Click(object sender, MouseButtonEventArgs e)
        {
            ModBase.OpenWebsite("https://login.mc-user.com:233/" + (ModMinecraft.McInstanceSelected == null ? Settings.Get<string>("CacheNideServer") : Settings.Get<string>("VersionServerNide", Instance: ModMinecraft.McInstanceSelected)) + "/skin");
        }

    }
}