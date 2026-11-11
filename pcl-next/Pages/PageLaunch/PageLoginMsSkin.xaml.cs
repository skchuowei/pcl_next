using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class PageLoginMsSkin
    {

        public PageLoginMsSkin()
        {
            this.InitializeComponent();
            this.PanData.MouseEnter += (p1, p2) => ShowPanel((object)p1, (MouseEventArgs)p2);
            this.PanData.MouseLeave += (p1, p2) => HidePanel((object)p1, (EventArgs)p2);
            // VB 原版靠 Handles BtnEdit.Click / BtnExit.Click / BtnSkin.Click 绑定，
            // 迁移后完全没订阅，导致「修改账号信息」「退出登录」「展开皮肤」三个按钮点了没反应（2026-11-11 修复）。
            this.BtnEdit.Click += (p1, p2) => BtnEdit_Click(p1, p2);
            this.BtnExit.Click += (p1, p2) => BtnExit_Click(p1, p2);
            this.BtnSkin.Click += (p1, p2) => BtnSkin_Click(p1, p2);
            this.Skin.Loader = PageLaunchLeft.SkinMs;
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
            this.TextName.Text = Settings.Get<string>("CacheMsV2Name");
            // 皮肤在 Loaded 加载
        }
        /// <summary>
    /// 获取当前页面的登录信息。
    /// </summary>
        public static ModLaunch.McLoginMs GetLoginData()
        {
            if (ModLaunch.McLoginMsLoader.State == ModBase.LoadState.Finished)
            {
                return new ModLaunch.McLoginMs() { OAuthRefreshToken = Settings.Get<string>("CacheMsV2OAuthRefresh"), UserName = Settings.Get<string>("CacheMsV2Name"), AccessToken = Settings.Get<string>("CacheMsV2Access"), Uuid = Settings.Get<string>("CacheMsV2Uuid"), ProfileJson = Settings.Get<string>("CacheMsV2ProfileJson") };
            }
            else
            {
                return new ModLaunch.McLoginMs() { OAuthRefreshToken = Settings.Get<string>("CacheMsV2OAuthRefresh"), UserName = Settings.Get<string>("CacheMsV2Name") };
            }
        }

        #region 下边栏其他内容

        // 显示/隐藏控制
        private void ShowPanel(object sender, MouseEventArgs e)
        {
            ModAnimation.AniStart(ModAnimation.AaOpacity(this.PanButtons, 1d - this.PanButtons.Opacity, 120), "PageLoginMsSkin Button");
        }
        public void HidePanel(object sender, EventArgs e)
        {
            if (this.BtnEdit.ContextMenu.IsOpen || this.BtnSkin.ContextMenu.IsOpen || this.PanData.IsMouseOver)
                return;
            ModAnimation.AniStart(ModAnimation.AaOpacity(this.PanButtons, -this.PanButtons.Opacity, 120), "PageLoginMsSkin Button");
        }

        // 修改账号信息
        private void BtnEdit_Click(object sender, EventArgs e)
        {
            this.BtnEdit.ContextMenu.IsOpen = true;
        }

        // 退出登录
        // VB 原版无参（Handles 允许），C# 委托要求 (object, EventArgs)（2026-11-11 修正）。
        private void BtnExit_Click(object sender, EventArgs e)
        {
            Settings.Set("CacheMsV2OAuthRefresh", "");
            Settings.Set("CacheMsV2Access", "");
            Settings.Set("CacheMsV2ProfileJson", "");
            Settings.Set("CacheMsV2Uuid", "");
            Settings.Set("CacheMsV2Name", "");
            Settings.Set("CacheMsV2Expires", 0L);
            ModLaunch.McLoginMsLoader.Cancel();
            ModMain.FrmLaunchLeft.RefreshPage(false, true);
        }

        #endregion

        #region 皮肤/披风

        // 展开
        private void BtnSkin_Click(object sender, EventArgs e)
        {
            this.BtnSkin.ContextMenu.IsOpen = true;
        }

        // 修改皮肤
        private bool IsChanging = false;
        public void BtnSkinEdit_Click(object sender, RoutedEventArgs e)
        {
            // 检查条件，获取新皮肤
            if (IsChanging)
            {
                ModMain.Hint("正在更改皮肤中，请稍候！");
                return;
            }
            if (ModLaunch.McLoginLoader.State == ModBase.LoadState.Failed)
            {
                ModMain.Hint("登录失败，无法更改皮肤！", ModMain.HintType.Red);
                return;
            }
            var SkinInfo = ModMinecraft.McSkinSelect();
            if (!SkinInfo.IsVaild)
                return;
            ModMain.Hint("正在更改皮肤……");
            IsChanging = true;
            // 开始实际获取
            ModBase.RunInNewThread(() => EditSkin(SkinInfo), "Ms Skin Upload");
        }
        private void EditSkin(ModMinecraft.McSkinInfo SkinInfo)
        {
        Retry:
            ;

            try
            {
                while (ModLaunch.McLoginMsLoader.State == ModBase.LoadState.Loading) // 等待登录结束
                    Thread.Sleep(10);
                if (ModLaunch.McLoginMsLoader.State == ModBase.LoadState.Failed)
                    throw new Exception("登录失败", ModLaunch.McLoginMsLoader.Error);
                string AccessToken = Settings.Get<string>("CacheMsV2Access");
                string Uuid = Settings.Get<string>("CacheMsV2Uuid");
                string Result = ModNet.NetRequestByClientRetry("https://api.minecraftservices.com/minecraft/profile/skins", HttpMethod.Post, Content: new MultipartFormDataContent() { { new StringContent(SkinInfo.IsSlim ? "slim" : "classic"), "variant" }, { new ByteArrayContent(FileUtils.ReadAsBytes(SkinInfo.LocalFile)), "file", PathUtils.GetLastPart(SkinInfo.LocalFile) } }, Headers: new[,] { { "Authorization", "Bearer " + AccessToken }, { "Accept", "*/*" }, { "User-Agent", "MojangSharp/0.1" } });
                if (Result.Contains("request requires user authentication"))
                {
                    ModMain.Hint("正在重新登录，将在登录后自动更改皮肤……");
                    ModLaunch.McLoginMsLoader.Start(GetLoginData(), IsForceRestart: true);
                    goto Retry;
                }
                // 获取新皮肤地址
                Logger.Info($"皮肤修改返回值：{Constants.vbCrLf}{Result}");
                JObject ResultJson = (JObject)Result.DeserializeJson();
                if (ResultJson.ContainsKey("errorMessage"))
                    throw new Exception(ResultJson["errorMessage"].ToString()); // #5309
                foreach (JObject Skin in ResultJson["skins"])
                {
                    if (Skin["state"].ToString() == "ACTIVE")
                    {
                        MySkin.ReloadCache((string)Skin["url"]);
                        return;
                    }
                }
                throw new Exception("未知错误（" + Result + "）");
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestCodeException)
                {
                    HttpRequestCodeException requestException = (HttpRequestCodeException)ex;
                    switch (requestException.StatusCode)
                    {
                        case HttpStatusCode.BadRequest:
                            {
                                Logger.Warn(ex, "更改皮肤时遭遇 400 错误");
                                if (requestException.Response?.Contains("\"error\"") == true)
                                {
                                    ModMain.Hint("更改皮肤失败：" + requestException.Response.DeserializeJson()["error"].ToString(), ModMain.HintType.Red);
                                    return;
                                }
                                else if (requestException.Response?.Contains("\"errorMessage\"") == true)
                                {
                                    ModMain.Hint("更改皮肤失败：" + requestException.Response.DeserializeJson()["errorMessage"].ToString(), ModMain.HintType.Red);
                                    return;
                                }

                                break;
                            }
                        case HttpStatusCode.Unauthorized:
                            {
                                Logger.Warn(ex, "更改皮肤时遭遇 401 错误");
                                ModMain.Hint("正在重新登录，将在登录后自动更改皮肤……");
                                ModLaunch.McLoginMsLoader.Start(GetLoginData(), IsForceRestart: true);
                                goto Retry;
                                // 原 VB 为 GoTo Retry，转换后追加的 break 永不执行（CS0162），故删除
                            }
                    }
                }
                else if (ex.IsBadNetwork())
                {
                    ModMain.Hint("更改皮肤失败：连接 Mojang 服务器超时，请稍后再试，或使用 VPN 改善网络环境", ModMain.HintType.Red);
                }
                else
                {
                    Logger.Error(ex, "更改皮肤失败", LogBehavior.Toast);
                }
            }
            finally
            {
                IsChanging = false;
            }
        }

        // 保存皮肤
        public void BtnSkinSave_Click(object sender, RoutedEventArgs e)
        {
            this.Skin.BtnSkinSave_Click();
        }

        // 刷新头像
        public void BtnSkinRefresh_Click(object sender, RoutedEventArgs e)
        {
            this.Skin.RefreshClick();
        }

        // 修改披风
        public void BtnSkinCape_Click(object sender, RoutedEventArgs e)
        {
            this.Skin.BtnSkinCape_Click();
        }

        // 刷新披风
        public void BtnSkinCapeRefresh_Click(object sender, RoutedEventArgs e)
        {
            ModBase.RunInThread(() => { try { ModMain.Hint("正在刷新披风列表……"); if (ModLaunch.McLaunchLoader.State == ModBase.LoadState.Loading) { ModLaunch.McLoginMsLoader.WaitForExit(); } else { ModLaunch.McLoginMsLoader.WaitForExit(GetLoginData(), IsForceRestart: true); } ModMain.Hint("已刷新披风列表！", ModMain.HintType.Green); } catch (Exception ex) { Logger.Error(ex, "刷新披风列表失败", LogBehavior.Toast); } });
        }

        #endregion

    }
}