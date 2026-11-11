using System;
using System.Windows;
using System.Windows.Input;
using MeloongCore;

namespace PCL
{
    public partial class PageLoginAuthSkin
    {

        public PageLoginAuthSkin()
        {
            this.InitializeComponent();
            this.Skin.Loader = PageLaunchLeft.SkinAuth;
            this.Loaded += (_, __) => PageLoginLegacy_Loaded();
        }
        private void PageLoginLegacy_Loaded()
        {
            this.Skin.Loader.Start();
        }

        /// <summary>
    /// 刷新页面显示的所有信息。
    /// </summary>
        public void Reload(bool KeepInput)
        {
            this.TextName.Text = Settings.Get<string>("CacheAuthName");
            this.TextEmail.Text = Settings.Get<string>("CacheAuthUsername");
            this.TextEmail.Visibility = Settings.Get<bool>("UiLauncherEmail") ? Visibility.Collapsed : Visibility.Visible;
            PageLoginLegacy_Loaded();
        }
        /// <summary>
    /// 获取当前页面的登录信息。
    /// </summary>
        public static ModLaunch.McLoginServer GetLoginData()
        {
            string Server = (ModMinecraft.McInstanceSelected == null ? Settings.Get<string>("CacheAuthServerServer") : Settings.Get<string>("VersionServerAuthServer", Instance: ModMinecraft.McInstanceSelected)) + "/authserver";
            return new ModLaunch.McLoginServer(ModLaunch.McLoginType.Auth) { Token = "Auth", BaseUrl = Server, UserName = Settings.Get<string>("CacheAuthUsername"), Password = Settings.Get<string>("CacheAuthPass"), Description = "Authlib-Injector", Type = ModLaunch.McLoginType.Auth };
        }

        private void PageLoginAuthSkin_MouseEnter(object sender, MouseEventArgs e)
        {
            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.BtnEdit, 1d - this.BtnEdit.Opacity, 80), ModAnimation.AaHeight(this.BtnEdit, 25.5d - this.BtnEdit.Height, 140, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnEdit, -1.5d, 50, 140, new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(this.BtnExit, 1d - this.BtnExit.Opacity, 80), ModAnimation.AaHeight(this.BtnExit, 25.5d - this.BtnExit.Height, 140, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnExit, -1.5d, 50, 140, new ModAnimation.AniEaseInFluent()) }, "PageLoginAuthSkin Button");
        }
        private void PageLoginAuthSkin_MouseLeave(object sender, MouseEventArgs e)
        {
            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.BtnEdit, -this.BtnEdit.Opacity, 120, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnEdit, 14d - this.BtnEdit.Height, 120, Ease: new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(this.BtnExit, -this.BtnExit.Opacity, 120, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaHeight(this.BtnExit, 14d - this.BtnExit.Height, 120, Ease: new ModAnimation.AniEaseInFluent()) }, "PageLoginAuthSkin Button");
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (ModLaunch.McLoginLoader.State == ModBase.LoadState.Loading)
            {
                Logger.Warn("要求更换角色，但登录加载器繁忙");
                if (ModLaunch.McLoginLoader.Input is ModLaunch.McLoginServer && ((ModLaunch.McLoginServer)ModLaunch.McLoginLoader.Input).ForceReselectProfile)
                {
                    ModMain.Hint("正在尝试更换，请稍候！");
                }
                else
                {
                    ModMain.Hint("正在登录中，请稍后再更换角色！", ModMain.HintType.Red);
                }
                return;
            }
            ModMain.Hint("正在尝试更换，请稍候！");
            Settings.Set("CacheAuthUuid", ""); // 清空选择缓存
            Settings.Set("CacheAuthName", "");
            ModBase.RunInThread(() => { try { var Data = GetLoginData(); Data.ForceReselectProfile = true; ModLaunch.McLoginLoader.WaitForExit(Data, IsForceRestart: true); ModBase.RunInUi(() => Reload(true)); } catch (Exception ex) { Logger.Error(ex, "更换角色失败", LogBehavior.Toast); } });
        }
        public static void ExitLogin()
        {
            Settings.Set("CacheAuthAccess", "");
            Settings.Set("CacheAuthUuid", "");
            Settings.Set("CacheAuthName", "");
            ModLaunch.McLoginAuthLoader.Input = null; // 防止因为输入的用户名密码相同，直接使用了上次登录的加载器结果
            ModMain.FrmLaunchLeft.RefreshPage(false, true);
        }

        private void Skin_Click(object sender, MouseButtonEventArgs e)
        {
            string Address = ModMinecraft.McInstanceSelected is not null ? Settings.Get<string>("VersionServerAuthRegister", Instance: ModMinecraft.McInstanceSelected) : Settings.Get<string>("CacheAuthServerRegister");
            if (string.IsNullOrEmpty(new ValidateHttp().Validate(Address)))
                ModBase.OpenWebsite(Address.Replace("/auth/register", "/user/closet"));
        }

    }
}