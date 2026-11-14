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

        /// <summary>
        /// 深色主题的固定编号。与上游 0~14 号主题号段不冲突，避免后续同步上游主题时撞号。
        /// </summary>
        public const int ThemeDarkId = 100;

        public static void ThemeRefresh(int NewTheme = -1)
        {
            try
            {
                if (ThemeNow == NewTheme && NewTheme >= 0)
                    return;
                if (NewTheme >= 0)
                    ThemeNow = NewTheme;

                bool IsDark = ThemeNow == ThemeDarkId;
                if (IsDark)
                {
                    // 深色主题：中性面亮度整体反转，强调色略提亮以补偿深底上的对比度
                    Color1 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.15d, 88d);
                    Color2 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 72d);
                    Color3 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 62d);
                    Color4 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 76d);
                    Color5 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.6d, 45d);
                    Color6 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.35d, 21d);
                    Color7 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.3d, 17d);
                    Color8 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.25d, 14d);
                    ColorBg0 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.3d, 30d);
                    ColorGray1 = new ModBase.MyColor(230d, 230d, 230d);
                    ColorGray2 = new ModBase.MyColor(184d, 184d, 184d);
                    ColorGray3 = new ModBase.MyColor(153d, 153d, 153d);
                    ColorGray4 = new ModBase.MyColor(122d, 122d, 122d);
                    ColorGray5 = new ModBase.MyColor(77d, 77d, 77d);
                    ColorGray6 = new ModBase.MyColor(51d, 51d, 51d);
                    ColorGray7 = new ModBase.MyColor(42d, 42d, 42d);
                    ColorGray8 = new ModBase.MyColor(36d, 36d, 36d);
                }
                else
                {
                    Color1 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.2d, 25d + ColorLightAdjust * 0.3d);
                    Color2 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust);
                    Color3 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust);
                    Color4 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust);
                    Color5 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 80d + ColorLightAdjust * 0.4d);
                    Color6 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 91d + ColorLightAdjust * 0.1d);
                    Color7 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 95d);
                    Color8 = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat, 97d);
                    ColorGray1 = new ModBase.MyColor(64d, 64d, 64d);
                    ColorGray2 = new ModBase.MyColor(115d, 115d, 115d);
                    ColorGray3 = new ModBase.MyColor(140d, 140d, 140d);
                    ColorGray4 = new ModBase.MyColor(166d, 166d, 166d);
                    ColorGray5 = new ModBase.MyColor(204d, 204d, 204d);
                    ColorGray6 = new ModBase.MyColor(235d, 235d, 235d);
                    ColorGray7 = new ModBase.MyColor(240d, 240d, 240d);
                    ColorGray8 = new ModBase.MyColor(245d, 245d, 245d);
                    ColorBg0 = Color4 * 0.4d + Color5 * 0.4d + ColorGray4 * 0.2d;
                }
                ColorBg1 = new ModBase.MyColor(190d, Color7);

                ColorSemiTransparent = new ModBase.MyColor(1d, Color8);
                ModBase.MyColor ColorCard, ColorPopup, ColorTooltip, ColorOverlay, ColorSidebar, ColorSidebarGlass, ColorWindow;
                if (IsDark)
                {
                    ColorCard = new ModBase.MyColor(242d, new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.3d, 20d));
                    ColorPopup = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.3d, 18d);
                    ColorTooltip = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.28d, 22d);
                    ColorOverlay = new ModBase.MyColor(221d, new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.2d, 6d));
                    ColorSidebar = new ModBase.MyColor(241d, new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.28d, 13d));
                    ColorSidebarGlass = new ModBase.MyColor(150d, new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.28d, 13d));
                    ColorWindow = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.3d, 10d);
                }
                else
                {
                    ColorCard = new ModBase.MyColor(245d, 255d, 255d);
                    ColorPopup = new ModBase.MyColor(255d, 255d, 255d);
                    ColorTooltip = new ModBase.MyColor(251d, 251d, 251d);
                    ColorOverlay = new ModBase.MyColor(221d, 221d, 221d);
                    ColorSidebar = new ModBase.MyColor(241d, 255d, 255d);
                    ColorSidebarGlass = new ModBase.MyColor(150d, 255d, 255d, 255d);
                    ColorWindow = new ModBase.MyColor(245d, 245d, 245d);
                }

                var Res = Application.Current.Resources;
                SetBrush("ColorBrush1", Color1);
                SetBrush("ColorBrush2", Color2);
                SetBrush("ColorBrush3", Color3);
                SetBrush("ColorBrush4", Color4);
                SetBrush("ColorBrush5", Color5);
                SetBrush("ColorBrush6", Color6);
                SetBrush("ColorBrush7", Color7);
                SetBrush("ColorBrush8", Color8);
                SetBrush("ColorBrushBg0", ColorBg0);
                SetBrush("ColorBrushBg1", ColorBg1);
                SetBrush("ColorBrushGray1", ColorGray1);
                SetBrush("ColorBrushGray2", ColorGray2);
                SetBrush("ColorBrushGray3", ColorGray3);
                SetBrush("ColorBrushGray4", ColorGray4);
                SetBrush("ColorBrushGray5", ColorGray5);
                SetBrush("ColorBrushGray6", ColorGray6);
                SetBrush("ColorBrushGray7", ColorGray7);
                SetBrush("ColorBrushGray8", ColorGray8);
                SetBrush("ColorBrushHalfWhite", IsDark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(85, 255, 255, 255));
                SetBrush("ColorBrushOnAccent", IsDark ? Color.FromArgb(255, 253, 254, 255) : (Color)Color8);
                SetBrush("ColorBrushCard", ColorCard);
                SetBrush("ColorBrushPopup", ColorPopup);
                SetBrush("ColorBrushTooltip", ColorTooltip);
                SetBrush("ColorBrushOverlay", ColorOverlay);
                SetBrush("ColorBrushSidebar", ColorSidebar);
                SetBrush("ColorBrushSidebarGlass", ColorSidebarGlass);
                SetBrush("ColorBrushWindow", ColorWindow);
                SetBrush("ColorBrushInk", IsDark ? Color.FromArgb(160, 232, 235, 240) : Color.FromArgb(160, 0, 0, 0));
                SetBrush("ColorBrushInkHover", IsDark ? Color.FromArgb(230, 240, 243, 248) : Color.FromArgb(230, 0, 0, 0));
                SetBrush("ColorBrushRedBack", IsDark ? Color.FromArgb(128, 64, 32, 32) : Color.FromArgb(128, 251, 221, 221));
                SetBrush("ColorBrushRedDark", IsDark ? Color.FromArgb(255, 255, 138, 122) : Color.FromArgb(255, 206, 33, 17));
                SetBrush("ColorBrushRedLight", IsDark ? Color.FromArgb(255, 255, 107, 107) : Color.FromArgb(255, 255, 76, 76));
                SetBrush("ColorBrushSemiTransparent", ColorSemiTransparent);
                // 左侧导航栏背景与右侧 MyCard 卡片统一为同一底色（ColorBrushCard），
                // 避免侧栏比卡片偏黄/偏主题色、两侧配色不一致。沿用 PageDownloadInstall 的既有约定。
                SetBrush("ColorBrushBackgroundTransparentSidebar", ColorCard);
                // 按钮边框专用画刷：默认与主题保持一致（Color1 / Color3 / Gray4），供开发者 UI 细调独立覆盖
                SetBrush("ColorBrushButton", Color1);
                SetBrush("ColorBrushButtonHover", Color3);
                SetBrush("ColorBrushButtonPressed", Color3);
                SetBrush("ColorBrushButtonDisabled", ColorGray4);
                Res["ColorObject1"] = (Color)Color1;
                Res["ColorObject2"] = (Color)Color2;
                Res["ColorObject3"] = (Color)Color3;
                Res["ColorObject4"] = (Color)Color4;
                Res["ColorObject5"] = (Color)Color5;
                Res["ColorObject6"] = (Color)Color6;
                Res["ColorObject7"] = (Color)Color7;
                Res["ColorObject8"] = (Color)Color8;
                Res["ColorObjectBg0"] = (Color)ColorBg0;
                Res["ColorObjectBg1"] = (Color)ColorBg1;
                Res["ColorObjectGray1"] = (Color)ColorGray1;
                Res["ColorObjectGray2"] = (Color)ColorGray2;
                Res["ColorObjectGray3"] = (Color)ColorGray3;
                Res["ColorObjectGray4"] = (Color)ColorGray4;
                Res["ColorObjectGray5"] = (Color)ColorGray5;
                Res["ColorObjectGray6"] = (Color)ColorGray6;
                Res["ColorObjectGray7"] = (Color)ColorGray7;
                Res["ColorObjectGray8"] = (Color)ColorGray8;
                Res["ColorObjectShadow"] = IsDark ? Color.FromArgb(255, 0, 0, 0) : (Color)Color1;
                ThemeRefreshMain();
                DevColorApplyOverrides();

                void SetBrush(string Key, Color Value) => Res[Key] = new SolidColorBrush(Value);
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
            bool IsDark = ThemeNow == ThemeDarkId;
            // 顶部条背景
            var Brush = new LinearGradientBrush() { EndPoint = new Point(1d, 0d), StartPoint = new Point(0d, 0d) };
            int[] Deltas = ColorHueTopbarDelta.Switch(d => new int[] { -d, 0, d }, d => d);
            int TopLight = IsDark ? 22 : 48;
            int TopLightMid = IsDark ? 26 : 54;
            Brush.GradientStops.Add(new GradientStop() { Offset = 0d, Color = new ModBase.MyColor().FromHSL2(ColorHue + Deltas[0], IsDark ? ColorSat * 0.8d : ColorSat, TopLight + ColorLightAdjust) });
            Brush.GradientStops.Add(new GradientStop() { Offset = 0.5d, Color = new ModBase.MyColor().FromHSL2(ColorHue + Deltas[1], IsDark ? ColorSat * 0.8d : ColorSat, TopLightMid + ColorLightAdjust) });
            Brush.GradientStops.Add(new GradientStop() { Offset = 1d, Color = new ModBase.MyColor().FromHSL2(ColorHue + Deltas[2], IsDark ? ColorSat * 0.8d : ColorSat, TopLight + ColorLightAdjust) });
            ModMain.FrmMain.PanTitle.Background = Brush;
            ModMain.FrmMain.PanTitle.Background.Freeze();
            // 主页面背景
            if (Settings.Get<bool>("UiBackgroundColorful"))
            {
                Brush = new LinearGradientBrush() { EndPoint = new Point(0.1d, 1d), StartPoint = new Point(0.9d, 0d) };
                double FormLight = IsDark ? 15d : 91d;
                Brush.GradientStops.Add(new GradientStop() { Offset = -0.1d, Color = new ModBase.MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8d, FormLight) });
                Brush.GradientStops.Add(new GradientStop() { Offset = 0.4d, Color = new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.8d, FormLight) });
                Brush.GradientStops.Add(new GradientStop() { Offset = 1.1d, Color = new ModBase.MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8d, FormLight) });
                ModMain.FrmMain.PanForm.Background = Brush;
            }
            else
            {
                ModMain.FrmMain.PanForm.Background = IsDark
                    ? (ModBase.MyColor)new ModBase.MyColor().FromHSL2(ColorHue, ColorSat * 0.3d, 10d)
                    : new ModBase.MyColor(245d, 245d, 245d);
            }
            ModMain.FrmMain.PanForm.Background.Freeze();
        });
        }

        // —— 开发者测试：UI 细调覆盖层 ——
        // 在 ThemeRefresh 算出的主题基线之上，叠加开发者在「UI 设置（测试）」中写入的覆盖色。
        // 仅当对应设置非空时才覆盖；清空（恢复默认）时自动回落到主题基线，不污染正式配色。
        public static void DevColorApplyOverrides()
        {
            try
            {
                var Res = Application.Current.Resources;
                // ColorBrushScrollBar 不随 ThemeRefresh 重算，不纳入覆盖，否则清空后无法回落。
                ApplyDevBrush(Res, "UiDevColorAccent", "ColorBrush3", "ColorBrush4", "ColorBrushOnAccent");
                ApplyDevBrush(Res, "UiDevColorText", "ColorBrush1");
                ApplyDevBrush(Res, "UiDevColorBtnDefault", "ColorBrushButton");
                ApplyDevBrush(Res, "UiDevColorBtnHover", "ColorBrushButtonHover");
                ApplyDevBrush(Res, "UiDevColorBtnPressed", "ColorBrushButtonPressed");
                ApplyDevBrush(Res, "UiDevColorBtnDisabled", "ColorBrushButtonDisabled");
                ApplyDevBrush(Res, "UiDevColorPopup", "ColorBrushPopup");
                // 主窗体背景与标题栏：与 ThemeRefreshMain 一致在 UI 队列中执行，保证排在主题基线之后。
                // 清空覆盖时 ThemeRefreshMain 会自行恢复基线，故此处无需额外回滚。
                string Bg = Settings.Get<string>("UiDevColorBg");
                string Ac = Settings.Get<string>("UiDevColorAccent");
                if (!string.IsNullOrEmpty(Bg) || !string.IsNullOrEmpty(Ac))
                    ModBase.RunInUi(() =>
                    {
                        if (ModMain.FrmMain is null || !ModMain.FrmMain.IsLoaded)
                            return;
                        if (!string.IsNullOrEmpty(Bg))
                            ModMain.FrmMain.PanForm.Background = new SolidColorBrush((Color)new ModBase.MyColor(Bg));
                        if (!string.IsNullOrEmpty(Ac))
                            DevApplyTitleBar(new ModBase.MyColor(Ac));
                    });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "应用开发者配色覆盖失败");
            }
        }

        private static void ApplyDevBrush(System.Windows.ResourceDictionary Res, string SettingKey, params string[] BrushKeys)
        {
            string Hex = Settings.Get<string>(SettingKey);
            if (string.IsNullOrEmpty(Hex))
                return;
            var Brush = new SolidColorBrush((Color)new ModBase.MyColor(Hex));
            foreach (var Key in BrushKeys)
                Res[Key] = Brush;
        }

        // 用开发者指定的主题色重算标题栏三段渐变（沿用 ThemeRefreshMain 的三段结构，明暗随主题色轻微浮动）
        private static void DevApplyTitleBar(ModBase.MyColor Accent)
        {
            ModBase.RunInUi(() =>
            {
                if (ModMain.FrmMain is null || !ModMain.FrmMain.IsLoaded)
                    return;
                var Brush = new LinearGradientBrush() { EndPoint = new Point(1d, 0d), StartPoint = new Point(0d, 0d) };
                Brush.GradientStops.Add(new GradientStop() { Offset = 0d, Color = (Color)(Accent * 0.9d) });
                Brush.GradientStops.Add(new GradientStop() { Offset = 0.5d, Color = (Color)Accent });
                Brush.GradientStops.Add(new GradientStop() { Offset = 1d, Color = (Color)(Accent * 1.1d) });
                ModMain.FrmMain.PanTitle.Background = Brush;
                ModMain.FrmMain.PanTitle.Background.Freeze();
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