using System;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{

    public partial class PageLaunchRight : IRefreshable, IDispatcherUnhandledException
    {
        public PageLaunchRight()
        {
            InitializeComponent();
            OnlineLoader = new ModLoader.LoaderTask<(string Address, bool ShouldRefresh), int>("下载主页", OnlineLoaderSub) { ReloadTimeout = 10 * 60 * 1000 };
            this.Loaded += (_, __) => Init();
            this.Loaded += (_, __) => Refresh();
        }

        private void Init()
        {
            this.PanBack.ScrollToHome();
            this.PanLog.Visibility = ModBase.ModeDebug.ToVisibility();
            // 非正式渠道提示
            this.PanHint.Visibility = (ModBuildChannel.ChannelIsInternal && ModSecret.VersionBranchMain == "Official" && ModSecret.CurrentRank < DonationRank.Rank23 && !Settings.Get<bool>("HintSnapshot")).ToVisibility();
        }

        #region 主页

        /// <summary>
    /// 刷新主页。
    /// </summary>
        private void Refresh()
        {
            ModBase.RunInThread(() => { try { lock (RefreshLock) RefreshReal(); } catch (Exception ex) { Logger.Error(ex, "加载 PCL Next 主页自定义信息失败", ModBase.ModeDebug ? LogBehavior.Alert : LogBehavior.Toast); } });
        }
        private void RefreshReal()
        {
            string Content = null;
            string Url = null;
            switch (Settings.Get<int>("UiCustomType"))
            {
                case 1:
                    {
                        // 加载本地文件
                        Logger.Info("主页自定义数据来源：本地文件");
                        Content = FileUtils.TryReadAsString(Paths.Base + @"PCL\Custom.xaml");
                        break;
                    }
                case 2:
                    {
                        // 联网下载
                        Url = Settings.Get<string>("UiCustomNet");
                        break;
                    }
                case 3:
                    {
                        // 预设
                        switch (Settings.Get<int>("UiCustomPreset"))
                        {
                            case 0:
                                {
                                    Logger.Info("主页预设：你知道吗");
                                    Content = @"
                            <local:MyCard Title=""你知道吗？"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{hint}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>";
                                    break;
                                }
                            case 1:
                                {
                                    Logger.Info("主页预设：回声洞");
                                    Content = @"
                            <local:MyCard Title=""回声洞"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{cave}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>";
                                    break;
                                }
                            case 2:
                                {
                                    Logger.Info("主页预设：Minecraft 新闻");
                                    Url = "https://mcnews.meloong.com";
                                    break;
                                }
                            case 3:
                                {
                                    Logger.Info("主页预设：简单主页");
                                    Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/MFn233/Custom.xaml";
                                    break;
                                }
                            case 4:
                                {
                                    Logger.Info("主页预设：每日整合包推荐");
                                    Url = "https://pclsub.sodamc.com/";
                                    break;
                                }
                            case 5:
                                {
                                    Logger.Info("主页预设：Minecraft 皮肤推荐");
                                    Url = "https://forgepixel.com/pcl_sub_file";
                                    break;
                                }
                            case 6:
                                {
                                    Logger.Info("主页预设：OpenBMCLAPI 仪表盘 Lite");
                                    Url = "https://pcl-bmcl.milu.ink/";
                                    break;
                                }
                            case 7:
                                {
                                    Logger.Info("主页预设：主页市场");
                                    Url = "https://pclhomeplazaoss.lingyunawa.top:26994/p/Homepages/Homepage.Market/Custom.xaml";
                                    break;
                                }
                            case 8:
                                {
                                    Logger.Info("主页预设：更新日志");
                                    Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/Joker2184/UpdateHomepage.xaml";
                                    break;
                                }
                            case 9:
                                {
                                    Logger.Info("主页预设：PCL Next 新功能说明书");
                                    Url = "https://raw.gitcode.com/WForst-Breeze/WhatsNewPCL/raw/main/Custom.xaml";
                                    break;
                                }
                            case 10:
                                {
                                    Logger.Info("主页预设：OpenMCIM Dashboard");
                                    Url = "https://files.mcimirror.top/PCL";
                                    break;
                                }
                            case 11:
                                {
                                    Logger.Info("主页预设：杂志主页");
                                    Url = "https://gh-proxy.com/https://github.com/Neclyon/Magazine-Homepage-PCL/raw/main/output/Custom.xaml";
                                    break;
                                }
                            case 12:
                                {
                                    Logger.Info("主页预设：PCL Next GitHub 仪表盘");
                                    Url = "https://ddf.pcl-community.top/Custom.xaml";
                                    break;
                                }
                            case 13:
                                {
                                    Logger.Info("主页预设：PCL Next 更新摘要");
                                    Url = "https://raw.gitcode.com/ENC_Euphony/PCL-AI-Summary-HomePage/raw/master/Custom.xaml";
                                    break;
                                }
                            case 14:
                                {
                                    Logger.Info("主页预设：今日新闻热点");
                                    Url = "https://pcl.wyc-w.top/index.xaml";
                                    break;
                                }
                            case 15:
                                {
                                    Logger.Info("主页预设：Minecraft 芝士站");
                                    Url = "https://mkss-services.awaeric.xyz";
                                    break;
                                }
                            case 16:
                                {
                                    Logger.Info("主页预设：整合包推荐引擎");
                                    Url = "https://qawsedrftgyhujiko.fun/pcl2/Custom.xaml";
                                    break;
                                }
                            case 17:
                                {
                                    Logger.Info("主页预设：Bangumi 番剧主页");
                                    Url = "https://bangumi.p.kaphia.top";
                                    break;
                                }
                            case 18:
                                {
                                    Logger.Info("主页预设：Bilibili 热门");
                                    Url = "https://bilibili.p.kaphia.top";
                                    break;
                                }
                            case 19:
                                {
                                    Logger.Info("主页预设：Music 云音乐热门");
                                    Url = "https://cloudmusic.p.kaphia.top";
                                    break;
                                }
                            case 20:
                                {
                                    Logger.Info("主页预设：模组推荐");
                                    Url = "https://g-fish.dpdns.org/download/ModPage.xaml";
                                    break;
                                }
                            case 21:
                                {
                                    Logger.Info("主页预设：每日游戏推荐");
                                    Url = "https://pcl.xn--9iq209asnop94b.top/download/GamePage.xaml";
                                    break;
                                }
                            case 22:
                                {
                                    Logger.Info("主页预设：Wallpaper 每日壁纸");
                                    Url = "https://wallpaper.p.kaphia.top";
                                    break;
                                }
                            case 23:
                                {
                                    Logger.Info("主页预设：Weather 天气预报");
                                    Url = "https://weather-p.kaphia.top/{variable:PCLDailyWeatherPage:choose}.xaml";
                                    break;
                                }
                            case 24:
                                {
                                    Logger.Info("主页预设：新一代狗屁王大杂烩主页");
                                    Url = "https://end.g-fish.dpdns.org/Custom.xaml";
                                    break;
                                }
                        }

                        break;
                    }
            }
            // 联网下载
            if (!string.IsNullOrWhiteSpace(Url))
            {
                // 替换自定义变量与设置
                Url = ModMain.ArgumentReplace(Url, WebUtility.HtmlEncode);
                if ((Url ?? "") == (Settings.Get<string>("CacheSavedPageUrl") ?? "") && FileUtils.Exists(ModBase.PathTemp + @"Cache\Custom.xaml"))
                {
                    // 缓存可用
                    Logger.Info("主页自定义数据来源：联网缓存文件");
                    Content = FileUtils.TryReadAsString(ModBase.PathTemp + @"Cache\Custom.xaml");
                    // 后台更新缓存
                    OnlineLoader.Start((Url, false));
                }
                else
                {
                    // 缓存不可用
                    Logger.Info("主页自定义数据来源：联网全新下载");
                    ModMain.Hint("正在加载主页……");
                    ModBase.RunInUiWait(() => LoadContent(null)); // 在加载结束前清空页面
                    Settings.Set("CacheSavedPageVersion", "");
                    OnlineLoader.Start((Url, true)); // 下载完成后将会再次触发更新
                    return;
                }
            }
            // 同步到 UI
            ModBase.RunInUi(() => LoadContent(Content));
        }
        private object RefreshLock = new object();

        // 联网获取主页文件
        private ModLoader.LoaderTask<(string Address, bool ShouldRefresh), int> OnlineLoader;
        private void OnlineLoaderSub(ModLoader.LoaderTask<(string Address, bool ShouldRefresh), int> Task)
        {
            string Address = Task.Input.Address; // #3721 中连续触发两次导致内容变化
            bool ShouldRefresh = Task.Input.ShouldRefresh;
            try
            {
                // 获取版本校验地址
                string VersionAddress;
                if (Address.Contains(".xaml"))
                {
                    VersionAddress = Address.Replace(".xaml", ".xaml.ini");
                }
                else
                {
                    VersionAddress = Address.BeforeFirst("?");
                    if (!VersionAddress.EndsWithF("/"))
                        VersionAddress += "/";
                    VersionAddress += "version";
                    if (Address.Contains("?"))
                        VersionAddress += "?" + Address.AfterFirst("?");
                }
                // 校验版本
                string Version = "";
                try
                {
                    Version = ModNet.NetRequestByClientRetry(VersionAddress);
                    if (Version.Length > 1000)
                        throw new Exception($"获取的主页版本过长（{Version.Length} 字符）");
                    string CurrentVersion = Settings.Get<string>("CacheSavedPageVersion");
                    if (!string.IsNullOrEmpty(Version) && !string.IsNullOrEmpty(CurrentVersion) && (Version ?? "") == (CurrentVersion ?? ""))
                    {
                        Logger.Info($"当前缓存的主页已为最新，当前版本：{Version}，检查源：{VersionAddress}");
                        return;
                    }
                    Logger.Info($"需要下载联网主页，当前版本：{Version}，检查源：{VersionAddress}");
                }
                catch (Exception exx)
                {
                    Logger.Warn(exx, $"联网获取主页版本失败");
                    Logger.Info($"无法检查联网主页版本，将直接下载，检查源：{VersionAddress}");
                }
                // 实际下载
                string FileContent = ModNet.NetRequestByClientRetry(Address);
                Logger.Info($"已联网下载主页，内容长度：{FileContent.Length}，来源：{Address}");
                Settings.Set("CacheSavedPageUrl", Address);
                Settings.Set("CacheSavedPageVersion", Version);
                FileUtils.Write(ModBase.PathTemp + @"Cache\Custom.xaml", FileContent);
                // 若内容变更则要求刷新
                if (((FileContent.GetStableHashCode() is var arg1 && LoadedContentHash.HasValue ? LoadedContentHash.Value != arg1 : (bool?)null) is var arg2 && !arg2.HasValue || arg2.Value) && ShouldRefresh && arg2.HasValue)
                    Refresh();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"下载主页失败（{Address}）", ModBase.ModeDebug ? LogBehavior.Alert : LogBehavior.Toast);
            }
        }

        /// <summary>
    /// 立即强制刷新主页。
    /// 必须在 UI 线程调用。
    /// </summary>
        public void ForceRefresh()
        {
            Logger.Info("要求强制刷新主页");
            ClearCache();
            // 实际的刷新
            if (ModMain.FrmMain.PageCurrent.Page == FormMain.PageType.Launch)
            {
                this.PanBack.ScrollToHome();
                Refresh();
            }
            else
            {
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Launch);
            }
        }

        void IRefreshable.Refresh() => ForceRefresh();

        /// <summary>
    /// 清空主页缓存信息。
    /// </summary>
        private void ClearCache()
        {
            LoadedContentHash = default;
            OnlineLoader.Input = ("", true);
            Settings.Set("CacheSavedPageUrl", "");
            Settings.Set("CacheSavedPageVersion", "");
            Logger.Info("已清空主页缓存");
        }

        /// <summary>
    /// 从文本内容中加载主页。
    /// 必须在 UI 线程调用。
    /// </summary>
        private void LoadContent(string Content)
        {
            try
            {
                lock (LoadContentLock)
                {
                    // 如果加载目标内容一致则不加载
                    ulong Hash = (Content ?? "").GetStableHashCode();
                    if (LoadedContentHash.HasValue && Hash == LoadedContentHash.Value)
                        return;
                    LoadedContentHash = Hash;
                    // 实际加载内容
                    this.PanCustom.Children.Clear();
                    if (string.IsNullOrWhiteSpace(Content))
                    {
                        Logger.Info($"实例化：清空主页 UI，来源为空");
                        return;
                    }
                    var LoadStartTime = DateTime.Now;
                    // 修改时应同时修改 PageOtherHelpDetail.Init
                    Content = ModMain.ArgumentReplace(Content, StringUtils.XmlEscape);
                    while (Content.Contains("xmlns"))
                        Content = Content.RegexReplace("xmlns[^\"']*(\"|')[^\"']*(\"|')", "").Replace("xmlns", ""); // 禁止声明命名空间
                    Content = "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=pcl-next\" xmlns:core=\"clr-namespace:MeloongCore;assembly=MeloongCore\" xmlns:corewpf=\"clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf\">" + Content + "</StackPanel>";
                    Logger.Info($"实例化：加载主页 UI 开始，最终内容长度：{Content.Count()}");
                    this.PanCustom.Children.Add((UIElement)ModBase.GetObjectFromXML(Content));
                    // 加载计时
                    int LoadCostTime = (DateTime.Now - LoadStartTime).Milliseconds;
                    Logger.Info($"实例化：加载主页 UI 完成，耗时 {LoadCostTime}ms");
                    if (LoadCostTime > 3000)
                        ModMain.Hint($"主页加载过于缓慢（花费了 {Math.Round(LoadCostTime / 1000d, 1)} 秒），请向主页作者反馈此问题，或暂时停止使用该主页");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"加载失败的主页内容：{"\r\n"}{Content}");
                OnLoadContentFailed(ex);
            }
        }
        /// <summary>
    /// 加载主页失败时调用。
    /// </summary>
        private void OnLoadContentFailed(Exception ex)
        {
            if (ModBase.ModeDebug || Settings.Get<int>("UiCustomType") == 1)
            {
                Logger.Warn(ex, "加载主页失败");
                if (ModMain.MyMsgBox(ex is UnauthorizedAccessException ? ex.Message : $"主页内容编写有误，请根据下列错误信息进行检查：{"\r\n"}{ex.GetDisplay(false)}", "加载主页失败", "重试", "取消") == 1)
                    ForceRefresh();
            }
            else
            {
                Logger.Error(ex, "加载主页失败", LogBehavior.Toast);
            }
        }
        /// <summary>
    /// 捕获主页在 Measure 和 Arrange 阶段抛出的异常。
    /// </summary>
        private void DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (!(e.Exception is System.Windows.Markup.XamlParseException))
                return;
            e.Handled = true;
            LoadContent(null);
            OnLoadContentFailed(e.Exception);
        }

        void IDispatcherUnhandledException.DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) => DispatcherUnhandledException(sender, e);

        private ulong? LoadedContentHash = default;
        private object LoadContentLock = new object();

        #endregion

    }
}