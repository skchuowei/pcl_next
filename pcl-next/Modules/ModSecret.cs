using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    // 由于包含加解密等安全信息，本文件中的部分代码已被删除

    internal static class ModSecret
    {

        // 标注 PCL 的不同分支，仅用于替换标记
        public const string VersionBranchMain = "OpenSource";
        // 在开源版的注册表与常规版的注册表隔离，以防数据冲突
        public const string RegFolder = "PCLDebug";
        // 用于微软登录的 ClientId
        public static string OAuthClientId = Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID") ?? "";
        // CurseForge API Key
        public static string CurseForgeAPIKey = Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY") ?? "";

        #region 网络鉴权

        // MC 下载功能的开关已从编译期常量下移为部署期配置，
        // 改由 ModDownloadPolicy（Modules/Base/ModDownloadPolicy.vb）提供同名只读属性：
        // 优先读环境变量 PCL_ALLOW_MC_DOWNLOAD，其次读启动器目录下的 DownloadPolicy.ini，
        // 均未配置时取内置默认值。
        internal static string SecretCdnSign(string UrlWithMark)
        {
            if (!UrlWithMark.EndsWithF("{CDN}"))
                return UrlWithMark;
            return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20");
        }
        /// <summary>
    /// 设置 Headers 的 UA、Referer。
    /// </summary>
        internal static void SecretHeadersSign(string Url, ref HttpRequestMessage Req, bool SimulateBrowserHeaders = false)
        {
            if (!Req.Headers.UserAgent.Any())
            {
                if (Url.Contains("baidupcs.com") || Url.Contains("baidu.com"))
                {
                    Req.Headers.Add("User-Agent", "LogStatistic");  // #4951
                }
                else if (SimulateBrowserHeaders)
                {
                    Req.Headers.Add("User-Agent", $"PCL2/{ModBase.VersionBaseName}.{(int)ModBase.BuildType} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36");
                }
                else
                {
                    Req.Headers.Add("User-Agent", $"PCL2/{ModBase.VersionBaseName}.{(int)ModBase.BuildType}");
                }
            }
            if (!SimulateBrowserHeaders)
                Req.Headers.Add("Referer", $"http://{ModBase.VersionCode}.open.pcl2.server/");
            if (Url.Contains("api.curseforge.com") || Url.Contains("forgecdn.net"))
                Req.Headers.Add("x-api-key", CurseForgeAPIKey);
        }

        #endregion

        #region 主题

        public static ModBase.MyColor Color1 = new ModBase.MyColor(52d, 61d, 74d);
        public static ModBase.MyColor Color2 = new ModBase.MyColor(11d, 91d, 203d);
        public static ModBase.MyColor Color3 = new ModBase.MyColor(19d, 112d, 243d);
        public static ModBase.MyColor Color4 = new ModBase.MyColor(72d, 144d, 245d);
        public static ModBase.MyColor Color5 = new ModBase.MyColor(150d, 192d, 249d);
        public static ModBase.MyColor Color6 = new ModBase.MyColor(213d, 230d, 253d);
        public static ModBase.MyColor Color7 = new ModBase.MyColor(222d, 236d, 253d);
        public static ModBase.MyColor Color8 = new ModBase.MyColor(234d, 242d, 254d);
        public static ModBase.MyColor ColorBg0 = new ModBase.MyColor(150d, 192d, 249d);
        public static ModBase.MyColor ColorBg1 = new ModBase.MyColor(190d, Color7);
        public static ModBase.MyColor ColorGray1 = new ModBase.MyColor(64d, 64d, 64d);
        public static ModBase.MyColor ColorGray2 = new ModBase.MyColor(115d, 115d, 115d);
        public static ModBase.MyColor ColorGray3 = new ModBase.MyColor(140d, 140d, 140d);
        public static ModBase.MyColor ColorGray4 = new ModBase.MyColor(166d, 166d, 166d);
        public static ModBase.MyColor ColorGray5 = new ModBase.MyColor(204d, 204d, 204d);
        public static ModBase.MyColor ColorGray6 = new ModBase.MyColor(235d, 235d, 235d);
        public static ModBase.MyColor ColorGray7 = new ModBase.MyColor(240d, 240d, 240d);
        public static ModBase.MyColor ColorGray8 = new ModBase.MyColor(245d, 245d, 245d);
        public static ModBase.MyColor ColorSemiTransparent = new ModBase.MyColor(1d, Color8);

        public static int ThemeNow = -1;
        public static int ColorHue = 210;
        public static int ColorSat = 85;
        public static int ColorLightAdjust = 0;
        public static OneOf<int, int[]> ColorHueTopbarDelta = 0;
        public static int ThemeDontClick = 0;

        public static void ThemeRefresh(int NewTheme = -1)
        {
            try
            {
                if (ThemeNow == NewTheme && NewTheme >= 0)
                    return;
                if (NewTheme >= 0)
                    ThemeNow = NewTheme;

                Color1 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.2d, 25d + ColorLightAdjust * 0.3d);
                Color2 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust);
                Color3 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust);
                Color4 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust);
                Color5 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 80d + ColorLightAdjust * 0.4d);
                Color6 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 91d + ColorLightAdjust * 0.1d);
                Color7 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 95d);
                Color8 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 97d);
                ColorBg0 = Color4 * 0.4d + Color5 * 0.4d + ColorGray4 * 0.2d;
                ColorBg1 = new ModBase.MyColor(190d, Color7);

                ColorSemiTransparent = new ModBase.MyColor(1d, Color8);
                ((dynamic)Application.Current.Resources)["ColorBrush1"] = new SolidColorBrush(Color1);
                ((dynamic)Application.Current.Resources)["ColorBrush2"] = new SolidColorBrush(Color2);
                ((dynamic)Application.Current.Resources)["ColorBrush3"] = new SolidColorBrush(Color3);
                ((dynamic)Application.Current.Resources)["ColorBrush4"] = new SolidColorBrush(Color4);
                ((dynamic)Application.Current.Resources)["ColorBrush5"] = new SolidColorBrush(Color5);
                ((dynamic)Application.Current.Resources)["ColorBrush6"] = new SolidColorBrush(Color6);
                ((dynamic)Application.Current.Resources)["ColorBrush7"] = new SolidColorBrush(Color7);
                ((dynamic)Application.Current.Resources)["ColorBrush8"] = new SolidColorBrush(Color8);
                ((dynamic)Application.Current.Resources)["ColorBrushBg0"] = new SolidColorBrush(ColorBg0);
                ((dynamic)Application.Current.Resources)["ColorBrushBg1"] = new SolidColorBrush(ColorBg1);
                ((dynamic)Application.Current.Resources)["ColorObject1"] = (Color)Color1;
                ((dynamic)Application.Current.Resources)["ColorObject2"] = (Color)Color2;
                ((dynamic)Application.Current.Resources)["ColorObject3"] = (Color)Color3;
                ((dynamic)Application.Current.Resources)["ColorObject4"] = (Color)Color4;
                ((dynamic)Application.Current.Resources)["ColorObject5"] = (Color)Color5;
                ((dynamic)Application.Current.Resources)["ColorObject6"] = (Color)Color6;
                ((dynamic)Application.Current.Resources)["ColorObject7"] = (Color)Color7;
                ((dynamic)Application.Current.Resources)["ColorObject8"] = (Color)Color8;
                ((dynamic)Application.Current.Resources)["ColorObjectBg0"] = (Color)ColorBg0;
                ((dynamic)Application.Current.Resources)["ColorObjectBg1"] = (Color)ColorBg1;
                ThemeRefreshMain();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast);
            }
        }
        public static void ThemeRefreshMain()
        {
            ModBase.RunInUi(() =>
        {
            if (!ModMain.FrmMain.IsLoaded)
                return;
            // 顶部条背景
            var Brush = new LinearGradientBrush() { EndPoint = new Point(1d, 0d), StartPoint = new Point(0d, 0d) };
            int[] Deltas = ColorHueTopbarDelta.Switch(d => new int[] { -d, 0, d }, d => d);
            Brush.GradientStops.Add(new GradientStop() { Offset = 0d, Color = new ModBase.MyColor().FromHSL2(ColorHue + Deltas[0], ColorSat, 48 + ColorLightAdjust) });
            Brush.GradientStops.Add(new GradientStop() { Offset = 0.5d, Color = new ModBase.MyColor().FromHSL2(ColorHue + Deltas[1], ColorSat, 54 + ColorLightAdjust) });
            Brush.GradientStops.Add(new GradientStop() { Offset = 1d, Color = new ModBase.MyColor().FromHSL2(ColorHue + Deltas[2], ColorSat, 48 + ColorLightAdjust) });
            ModMain.FrmMain.PanTitle.Background = Brush;
            ModMain.FrmMain.PanTitle.Background.Freeze();
            // 主页面背景
            if (Settings.Get<bool>("UiBackgroundColorful"))
            {
                Brush = new LinearGradientBrush() { EndPoint = new Point(0.1d, 1d), StartPoint = new Point(0.9d, 0d) };
                Brush.GradientStops.Add(new GradientStop() { Offset = -0.1d, Color = new ModBase.MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8d, 91d) });
                Brush.GradientStops.Add(new GradientStop() { Offset = 0.4d, Color = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.8d, 91d) });
                Brush.GradientStops.Add(new GradientStop() { Offset = 1.1d, Color = new ModBase.MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8d, 91d) });
                ModMain.FrmMain.PanForm.Background = Brush;
            }
            else
            {
                ModMain.FrmMain.PanForm.Background = new ModBase.MyColor(245d, 245d, 245d);
            }
            ModMain.FrmMain.PanForm.Background.Freeze();
        });
        }
        internal static void ThemeCheckAll(bool EffectSetup)
        {
        }
        internal static bool ThemeCheckOne(int Id)
        {
            return true;
        }
        internal static bool ThemeUnlock(int Id, bool ShowDoubleHint = true, string UnlockHint = null)
        {
            return false;
        }

        #endregion

        #region 更新

        // pcl-next 没有内置自动更新服务器，检查更新时统一引导用户前往 GitHub 仓库 Releases 获取新版本。
        // 仓库地址：https://github.com/skchuowei/pcl_next
        internal static void UpdateCheckByButton()
        {
            ModBase.OpenWebsite("https://github.com/skchuowei/pcl_next/releases");
        }

        #endregion

        #region 联网配置

        // 上游遗留字段：开源版的配置获取流程已被移除（见 PageLinkMain 中「开源版无法联网获取配置」），
        // 全仓库无任何赋值点，运行期恒为 null；保留以维持上游 API 与后续恢复余地，故豁免 CS0649
#pragma warning disable CS0649
        /// <summary>
    /// 联网获取的配置信息。
    /// 若获取失败或仍在获取中，可能为 Nothing。
    /// </summary>
        public static JObject ServerConfig;
#pragma warning restore CS0649

        public static ModLoader.LoaderTask<int, int> ServerLoader = new ModLoader.LoaderTask<int, int>("PCL Next 配置更新", (_) => Logger.Info("该版本中不包含更新通知功能……"), Priority: ThreadPriority.BelowNormal) { ReloadTimeout = 1000 * 60 * 60 }; // 超时 1 小时

        #endregion

        #region 赞助等级

        public static DonationRank CurrentRank
        {
            get
            {
                return DonationRank.None;
            }
        }

        public static void InputPotatoCode(bool IsUpdating)
        {
        }
        internal static void GeneratePotatoCode()
        {
        }

        /// <summary>
    /// 获取设备识别码。
    /// </summary>
        internal static string GetIdentify()
        {
            return "0000-0000-0000-0000";
        }

        #endregion

    }
}