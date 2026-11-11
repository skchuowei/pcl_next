using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class MySkin
    {

        // 事件
        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e);

        // 皮肤储存
        private string _Address;
        public string Address
        {
            get
            {
                return _Address;
            }
            set
            {
                _Address = value;
                this.ToolTip = string.IsNullOrEmpty(_Address) ? "加载中" : "点击更换皮肤（右键查看更多选项）";
            }
        }
        public ModLoader.LoaderTask<(string, string), string> Loader;

        public MySkin()
        {
            this.MouseEnter += PanSkin_MouseEnter;
            this.MouseLeave += PanSkin_MouseLeave;
            this.MouseLeftButtonDown += PanSkin_MouseLeftButtonDown;
            this.MouseLeftButtonUp += PanSkin_MouseLeftButtonUp;
        }

        // 控件动画
        private void PanSkin_MouseEnter(object sender, MouseEventArgs e)
        {
            ModAnimation.AniStart(ModAnimation.AaOpacity(this.ShadowSkin, 0.8d - this.ShadowSkin.Opacity, 200, 100), "Skin Shadow");
        }
        private void PanSkin_MouseLeave(object sender, MouseEventArgs e)
        {
            ModAnimation.AniStart(ModAnimation.AaOpacity(this.ShadowSkin, 0.2d - this.ShadowSkin.Opacity, 200), "Skin Shadow");
            IsSkinMouseDown = false;
            ModAnimation.AniStart(ModAnimation.AaScaleTransform(this, 1d - ((ScaleTransform)this.RenderTransform).ScaleX, 60, Ease: new ModAnimation.AniEaseOutFluent()), "Skin Scale");
        }

        // 点击
        private bool IsSkinMouseDown = false;
        private void PanSkin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            IsSkinMouseDown = true;
            ModAnimation.AniStart(ModAnimation.AaScaleTransform(this, 0.9d - ((ScaleTransform)this.RenderTransform).ScaleX, 60, Ease: new ModAnimation.AniEaseOutFluent()), "Skin Scale");
        }
        private void PanSkin_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ModAnimation.AniStart(ModAnimation.AaScaleTransform(this, 1d - ((ScaleTransform)this.RenderTransform).ScaleX, 60, Ease: new ModAnimation.AniEaseOutFluent()), "Skin Scale");
            if (IsSkinMouseDown)
            {
                IsSkinMouseDown = false;
                Click?.Invoke(sender, e);
            }
        }

        // 保存皮肤
        public void BtnSkinSave_Click()
        {
            Save(Loader);
        }
        public static void Save(ModLoader.LoaderTask<(string, string), string> Loader)
        {
            string Address = Loader.Output;
            if (!(Loader.State == ModBase.LoadState.Finished))
            {
                ModMain.Hint("皮肤正在获取中，请稍候！", ModMain.HintType.Red);
                if (!(Loader.State == ModBase.LoadState.Loading))
                    Loader.Start();
                return;
            }
            try
            {
                string FileAddress = Dialogs.SaveFile("选取保存皮肤的位置", PathUtils.GetLastPart(Address), filter: new[] { ("png", "皮肤图片文件") });
                if (FileAddress is null)
                    return;
                if (FileAddress.Contains(@"\"))
                {
                    FileUtils.Delete(FileAddress);
                    if (Address.StartsWithF(ModBase.PathImage))
                    {
                        var Image = new MyBitmap(Address);
                        Image.Save(FileAddress);
                    }
                    else
                    {
                        FileUtils.Copy(Address, FileAddress);
                    }
                    ModMain.Hint("皮肤保存成功！", ModMain.HintType.Green);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "保存皮肤失败", LogBehavior.Toast);
            }
        }
        private void BtnSkinSave_Checked(MyMenuItem sender, RoutedEventArgs e)
        {
            sender.IsEnabled = string.IsNullOrEmpty(Address);
        }

        /// <summary>
    /// 载入皮肤。
    /// </summary>
        public void Load()
        {
            try
            {
                // 检查文件存在
                Address = Loader.Output;
                if (string.IsNullOrEmpty(Address))
                    throw new Exception("皮肤加载器 " + Loader.Name + " 没有输出");
                if (!Address.StartsWithF(ModBase.PathImage) && !FileUtils.Exists(Address))
                    throw new FileNotFoundException("皮肤文件未找到", Address);
                // 加载
                MyBitmap Image;
                try
                {
                    Image = new MyBitmap(Address);
                }
                catch (Exception ex) // #2272
                {
                    Logger.Error(ex, $"皮肤文件已损坏：{Address}", LogBehavior.Toast);
                    FileUtils.Delete(Address);
                    return;
                }
                this.ImgBack.Tag = Address;
                // 大小检查
                int Scale = (int)Math.Round(Image.Pic.Width / 64d);
                if (Image.Pic.Width < 32 || Image.Pic.Height < 32)
                {
                    this.ImgFore.Source = (ImageSource)null;
                    this.ImgBack.Source = (ImageSource)null;
                    throw new Exception("图片大小不足，长为 " + Image.Pic.Height + "，宽为 " + Image.Pic.Width);
                }
                // 头发层（附加层）
                if (Image.Pic.Width >= 64 && Image.Pic.Height >= 32)
                {
                    if (Image.Pic.GetPixel(1, 1).A == 0 || Image.Pic.GetPixel(Image.Pic.Width - 1, Image.Pic.Height - 1).A == 0 || Image.Pic.GetPixel(Image.Pic.Width - 2, (int)Math.Round(Image.Pic.Height / 2d - 2d)).A == 0 || Image.Pic.GetPixel(1, 1) != Image.Pic.GetPixel(Scale * 41, Scale * 9) && Image.Pic.GetPixel(Image.Pic.Width - 1, Image.Pic.Height - 1) != Image.Pic.GetPixel(Scale * 41, Scale * 9) && Image.Pic.GetPixel(Image.Pic.Width - 2, (int)Math.Round(Image.Pic.Height / 2d - 2d)) != Image.Pic.GetPixel(Scale * 41, Scale * 9)) // 如果图片中有任何透明像素（避免纯色白底）
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            // 或是头部颜色和透明区均不一样
                    {
                        this.ImgFore.Source = Image.Clip(Scale * 40, Scale * 8, Scale * 8, Scale * 8);
                    }
                    else
                    {
                        this.ImgFore.Source = (ImageSource)null;
                    }
                }
                else
                {
                    this.ImgFore.Source = (ImageSource)null;
                }
                // 脸层
                this.ImgBack.Source = Image.Clip(Scale * 8, Scale * 8, Scale * 8, Scale * 8);
                Logger.Info($"载入头像成功：{Loader.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"载入头像失败（{Address ?? "null"},{Loader.Name}）", LogBehavior.Toast);
            }
        }
        /// <summary>
    /// 清空皮肤。
    /// </summary>
        public void Clear()
        {
            Address = "";
            this.ImgFore.Source = (ImageSource)null;
            this.ImgBack.Source = (ImageSource)null;
        }

        // 刷新缓存
        public void RefreshClick()
        {
            RefreshCache(Loader);
        }
        /// <summary>
    /// 刷新皮肤缓存。
    /// </summary>
        public static void RefreshCache(ModLoader.LoaderTask<(string, string), string> sender = null)
        {
            bool HasLoaderRunning = false;
            foreach (var SkinLoader in PageLaunchLeft.SkinLoaders)
            {
                if (SkinLoader.State == ModBase.LoadState.Loading)
                {
                    HasLoaderRunning = true;
                    break;
                }
            }
            if (ModMain.FrmLaunchLeft is not null && HasLoaderRunning)
            {
                // 由于取消不是实时的，暂时不会释放文件，会导致删除报错，故只能取消执行
                ModMain.Hint("有正在获取中的皮肤，请稍后再试！", ModMain.HintType.Blue);
            }
            else
            {
                // 清空缓存
                // 刷新控件
                ModBase.RunInThread(() => { try { ModMain.Hint("正在刷新皮肤……"); Logger.Info("正在清空皮肤缓存"); if (DirectoryUtils.Exists(ModBase.PathTemp + @"Cache\Skin")) DirectoryUtils.Delete(ModBase.PathTemp + @"Cache\Skin"); if (DirectoryUtils.Exists(ModBase.PathTemp + @"Cache\Uuid")) DirectoryUtils.Delete(ModBase.PathTemp + @"Cache\Uuid"); ModBase.IniClearCache(ModBase.PathTemp + @"Cache\Skin\IndexMs.ini"); ModBase.IniClearCache(ModBase.PathTemp + @"Cache\Skin\IndexNide.ini"); ModBase.IniClearCache(ModBase.PathTemp + @"Cache\Skin\IndexAuth.ini"); ModBase.IniClearCache(ModBase.PathTemp + @"Cache\Uuid\Mojang.ini"); foreach (var SkinLoader in sender is not null ? (new[] { sender }) : (new[] { PageLaunchLeft.SkinLegacy, PageLaunchLeft.SkinMs })) SkinLoader.WaitForExit(IsForceRestart: true); ModMain.Hint("已刷新皮肤！", ModMain.HintType.Green); } catch (Exception ex) { Logger.Error(ex, "刷新皮肤缓存失败", LogBehavior.Alert); } });
            }
        }
        /// <summary>
    /// 在更换正版皮肤后，刷新正版皮肤。
    /// </summary>
    /// <param name="SkinAddress">新的正版皮肤完整地址。</param>
        public static void ReloadCache(string SkinAddress)
        {
            // 更新缓存
            // 刷新控件
            // 完成提示
            ModBase.RunInThread(() => { try { ModBase.WriteIni(ModBase.PathTemp + @"Cache\Skin\IndexMs.ini", Settings.Get<string>("CacheMsV2Uuid"), SkinAddress); Logger.Info($"已写入皮肤地址缓存 {Settings.Get<string>("CacheMsV2Uuid")} -> {SkinAddress}"); foreach (var SkinLoader in new[] { PageLaunchLeft.SkinMs, PageLaunchLeft.SkinLegacy }) SkinLoader.WaitForExit(IsForceRestart: true); ModMain.Hint("更改皮肤成功！", ModMain.HintType.Green); } catch (Exception ex) { Logger.Error(ex, "更改正版皮肤后刷新皮肤失败"); } });
        }

        // 披风
        public bool HasCape
        {
            get
            {
                return this.BtnSkinCape.Visibility == Visibility.Collapsed;
            }
            set
            {
                if (value)
                {
                    this.BtnSkinCape.Visibility = Visibility.Visible;
                }
                else
                {
                    this.BtnSkinCape.Visibility = Visibility.Collapsed;
                }
            }
        }
        private bool IsChanging = false;
        public void BtnSkinCape_Click()
        {
            // 检查条件，获取新披风
            if (IsChanging)
            {
                ModMain.Hint("正在更改披风中，请稍候！");
                return;
            }
            if (ModLaunch.McLoginMsLoader.State == ModBase.LoadState.Failed)
            {
                ModMain.Hint("登录失败，无法更改披风！", ModMain.HintType.Red);
                return;
            }
            if (ModLaunch.McLoginMsLoader.State != ModBase.LoadState.Finished)
                ModMain.Hint("正在获取披风列表，请稍候……");
            IsChanging = true;
            // 开始实际获取
            ModBase.RunInNewThread(() =>
        {
        Retry:
            ;

            try
            {
                // 获取登录信息
                if (ModLaunch.McLoginMsLoader.State != ModBase.LoadState.Finished)
                    ModLaunch.McLoginMsLoader.WaitForExit(PageLoginMsSkin.GetLoginData());
                if (ModLaunch.McLoginMsLoader.State != ModBase.LoadState.Finished)
                {
                    ModMain.Hint("登录失败，无法更改披风！", ModMain.HintType.Red);
                    return;
                }
                string AccessToken = ModLaunch.McLoginMsLoader.Output.AccessToken;
                string Uuid = ModLaunch.McLoginMsLoader.Output.Uuid;
                JObject SkinData = (JObject)ModLaunch.McLoginMsLoader.Output.ProfileJson.DeserializeJson();
                // 获取玩家的所有披风
                int? SelectedIndex = default;
                ModBase.RunInUiWait(() => { try { var CapeNames = new Dictionary<string, string>() { { "Migrator", "迁移者披风" }, { "MapMaker", "Realms 地图制作者披风" }, { "Moderator", "Mojira 管理员披风" }, { "Translator-Chinese", "Crowdin 中文翻译者披风" }, { "Translator", "Crowdin 翻译者披风" }, { "Cobalt", "Cobalt 披风" }, { "Vanilla", "原版披风" }, { "Minecon2011", "Minecon 2011 参与者披风" }, { "Minecon2012", "Minecon 2012 参与者披风" }, { "Minecon2013", "Minecon 2013 参与者披风" }, { "Minecon2015", "Minecon 2015 参与者披风" }, { "Minecon2016", "Minecon 2016 参与者披风" }, { "Cherry Blossom", "樱花披风" }, { "15th Anniversary", "15 周年纪念披风" }, { "Purple Heart", "紫色心形披风" }, { "Follower's", "追随者披风" }, { "MCC 15th Year", "MCC 15 周年披风" }, { "Minecraft Experience", "村民救援披风" }, { "Mojang Office", "Mojang 办公室披风" }, { "Home", "家园披风" }, { "Menace", "入侵披风" }, { "Yearn", "渴望披风" }, { "Common", "普通披风" }, { "Pan", "薄煎饼披风" }, { "Founder's", "创始人披风" }, { "Copper", "铜披风" }, { "Zombie Horse", "僵尸马披风" }, { "Builder", "建造者披风" }, { "Crafter", "工匠披风" }, { "Aurora", "极光披风" } }; MyRadioBox @init = new MyRadioBox(); var SelectionControl = new List<IMyRadio>() { (@init.Text = "无披风", @init.Checked = !SkinData["capes"].Any(c => c["state"]?.ToString() == "ACTIVE"), @init).@init }; foreach (var Cape in SkinData["capes"]) { string CapeName = Cape["alias"].ToString(); if (CapeNames.ContainsKey(CapeName)) CapeName = CapeNames[CapeName]; MyRadioBox @init1 = new MyRadioBox(); SelectionControl.Add((@init1.Text = CapeName, @init1.Checked = Cape["state"]?.ToString() == "ACTIVE", @init1).@init1); } SelectedIndex = ModMain.MyMsgBoxSelect(SelectionControl, "选择披风", "确定", "取消"); } catch (Exception ex) { Logger.Error(ex, "获取玩家皮肤列表失败"); } });
                if (SelectedIndex is null)
                    return;
                // 发送请求
                string Result = ModNet.NetRequestByClientRetry("https://api.minecraftservices.com/minecraft/profile/capes/active", SelectedIndex.HasValue && SelectedIndex.Value == 0 ? HttpMethod.Delete : HttpMethod.Put, Content: SelectedIndex.HasValue && SelectedIndex.Value == 0 ? "" : new JObject(new JProperty("capeId", SkinData["capes"][SelectedIndex - 1]["id"])).ToString(0), ContentType: "application/json", Headers: new[,] { { "Authorization", "Bearer " + AccessToken } });
                if (Result.Contains("\"errorMessage\""))
                {
                    ModMain.Hint("更改披风失败：" + Result.DeserializeJson()["errorMessage"].ToString(), ModMain.HintType.Red);
                    return;
                }
                else
                {
                    ModMain.Hint("更改披风成功！", ModMain.HintType.Green);
                    // 更新当前选择的披风
                    foreach (var Cape in SkinData["capes"])
                        Cape["state"] = "INACTIVE";
                    if (SelectedIndex.HasValue && SelectedIndex.Value > 0)
                        SkinData["capes"][SelectedIndex - 1]["state"] = "ACTIVE";
                    ModLaunch.McLoginMsLoader.Output.ProfileJson = SkinData.ToString();
                    Settings.Set("CacheMsV2ProfileJson", ModLaunch.McLoginMsLoader.Output.ProfileJson);
                }
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
                                Logger.Warn(ex, "更改披风时遭遇 400 错误");
                                if (requestException.Response?.Contains("\"error\"") == true)
                                {
                                    ModMain.Hint("更改披风失败：" + requestException.Response.DeserializeJson()["error"].ToString(), ModMain.HintType.Red);
                                    return;
                                }
                                else if (requestException.Response?.Contains("\"errorMessage\"") == true)
                                {
                                    ModMain.Hint("更改披风失败：" + requestException.Response.DeserializeJson()["errorMessage"].ToString(), ModMain.HintType.Red);
                                    return;
                                }

                                break;
                            }
                        case HttpStatusCode.Unauthorized:
                            {
                                Logger.Warn(ex, "更改披风时遭遇 401 错误");
                                ModMain.Hint("正在重新登录，将在登录后自动更改披风……");
                                ModLaunch.McLoginMsLoader.Start(PageLoginMsSkin.GetLoginData(), IsForceRestart: true);
                                goto Retry;
                                // 原 VB 为 GoTo Retry，转换后追加的 break 永不执行（CS0162），故删除
                            }
                    }
                }
                else if (ex.IsBadNetwork())
                {
                    ModMain.Hint("更改披风失败：连接 Mojang 服务器超时，请稍后再试，或使用 VPN 改善网络环境", ModMain.HintType.Red);
                }
                else
                {
                    Logger.Error(ex, "更改披风失败", LogBehavior.Toast);
                }
            }
            finally
            {
                IsChanging = false;
            }
        }, "Cape Change");
        }

    }
}