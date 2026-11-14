using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Windows;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public static class ModLaunch
    {

        #region 开始

        public static McLaunchOptions CurrentLaunchOptions = null;
        public class McLaunchOptions
        {
            /// <summary>
        /// 强制指定在启动后进入的服务器 IP。
        /// 默认值：Nothing。使用版本设置的值。
        /// </summary>
            public string ServerIp = null;
            /// <summary>
        /// 将启动脚本保存到该地址，然后取消启动。这同时会改变启动时的提示等。
        /// 默认值：Nothing。不保存。
        /// </summary>
            public string SaveBatch = null;
            /// <summary>
        /// 强行指定启动的 MC 版本。
        /// 默认值：Nothing。使用 McInstanceCurrent。
        /// </summary>
            public ModMinecraft.McInstance Instance = null;
            /// <summary>
        /// 额外的启动参数。
        /// </summary>
            public List<string> ExtraGameArgs = new List<string>();
        }
        /// <summary>
    /// 尝试启动 Minecraft。必须在 UI 线程调用。
    /// 返回是否实际开始了启动（如果没有，则一定弹出了错误提示）。
    /// </summary>
        public static bool McLaunchStart(McLaunchOptions Options = null)
        {
            CurrentLaunchOptions = Options ?? new McLaunchOptions();
            // 预检查
            if (!ModBase.RunInUi())
                throw new Exception("McLaunchStart 必须在 UI 线程调用！");
            if (McLaunchLoader.State == ModBase.LoadState.Loading)
            {
                ModMain.Hint("已有游戏正在启动中！", ModMain.HintType.Red);
                return false;
            }
            // 强制切换需要启动的版本
            if (CurrentLaunchOptions.Instance is not null && ModMinecraft.McInstanceSelected != CurrentLaunchOptions.Instance)
            {
                McLaunchLog("在启动前切换到版本 " + CurrentLaunchOptions.Instance.Name);
                // 检查版本是否存在
                try
                {
                    CurrentLaunchOptions.Instance.GetJsonPath();
                }
                catch
                {
                    ModMain.Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：当前 Minecraft 文件夹中尚未安装该版本！", ModMain.HintType.Red);
                    return false;
                }
                // 检查版本
                CurrentLaunchOptions.Instance.Load();
                if (CurrentLaunchOptions.Instance.State == ModMinecraft.McInstance.McInstanceState.Error)
                {
                    ModMain.Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：{CurrentLaunchOptions.Instance.Info}", ModMain.HintType.Red);
                    return false;
                }
                // 切换版本
                ModMinecraft.McInstanceSelected = CurrentLaunchOptions.Instance;
                ModMain.FrmLaunchLeft.RefreshButtonsUI();
                ModMain.FrmLaunchLeft.RefreshPage(false, false);
            }
            if (ModMinecraft.McInstanceSelected is not null && ModMinecraft.McInstanceLock.IsLocked(ModMinecraft.McInstanceSelected))
            {
                ModMain.Hint($"版本 {ModMinecraft.McInstanceSelected.Name} 正在被修改，请稍后再启动！", ModMain.HintType.Red);
                return false;
            }
            ModMain.FrmMain.AprilGiveup();
            // 禁止进入版本选择页面（否则就可以在启动中切换 McInstanceSelected 了）
            ModMain.FrmMain.PageStack = ModMain.FrmMain.PageStack.Where(p => p.Page != FormMain.PageType.InstanceSelect).ToList();
            // 实际启动加载器
            McLaunchLoader.Start(Options, IsForceRestart: true);
            return true;
        }

        /// <summary>
    /// 记录启动日志。
    /// </summary>
        public static void McLaunchLog(string Text, LogLevel Level = LogLevel.Info)
        {
            Text = ModMinecraft.FilterUserName(ModMinecraft.FilterAccessToken(Text, '*'), '*');
            if (ModBase.ModeDebug)
            {
                ModBase.RunInUi(() => ModMain.FrmLaunchRight.LabLog.AppendText($"[{DateTime.Now:HH':'mm':'ss'.'fff}] {Text}{"\r\n"}"));
            }
            Logger.Log(Text, Level);
        }

        // 启动状态切换
        public static ModLoader.LoaderTask<McLaunchOptions, object> McLaunchLoader = new ModLoader.LoaderTask<McLaunchOptions, object>("Loader Launch", McLaunchStart) { OnStateChanged = (_) => ModLaunch.McLaunchState((dynamic)_) };
        public static ModLoader.LoaderCombo<object> McLaunchLoaderReal;
        public static Process McLaunchProcess;
        public static ModWatcher.Watcher McLaunchWatcher;
        private static void McLaunchState(ModLoader.LoaderTask<McLaunchOptions, object> Loader)
        {
            switch (McLaunchLoader.State)
            {
                case ModBase.LoadState.Finished:
                case ModBase.LoadState.Failed:
                case ModBase.LoadState.Waiting:
                case ModBase.LoadState.Canceled:
                    {
                        ModMain.FrmLaunchLeft.PageChangeToLogin();
                        break;
                    }
                case ModBase.LoadState.Loading:
                    {
                        // 在预检测结束后再触发动画
                        ModMain.FrmLaunchRight.LabLog.Text = "";
                        break;
                    }
            }
        }
        /// <summary>
    /// 指定启动中断时的提示文本。若不为 Nothing 则会显示为绿色。
    /// </summary>
        private static string CanceledHint = null;

        // 实际的启动方法
        private static void McLaunchStart(ModLoader.LoaderTask<McLaunchOptions, object> Loader)
        {
            // 开始动画
            ModBase.RunInUiWait(ModMain.FrmLaunchLeft.PageChangeToLaunching);
            // 预检测（预检测的错误将直接抛出）
            try
            {
                McLaunchPrecheck();
                McLaunchLog("预检测已通过");
            }
            catch (Exception ex)
            {
                if (!ex.IsCanceled())
                    ModMain.Hint(ex.Message, ModMain.HintType.Red);
                throw;
            }
            bool IsSavingBatch = CurrentLaunchOptions?.SaveBatch is not null;

            // 正式加载
            try
            {
                // 构造主加载器
                var Loaders = new List<ModLoader.LoaderBase>() { new ModLoader.LoaderTask<int, int>("获取 Java", McLaunchJava) { ProgressWeight = 4d, Block = false }, McLoginLoader, new ModLoader.LoaderCombo<string>("补全文件", ModDownload.DlClientFix(ModMinecraft.McInstanceSelected, false, true)) { ProgressWeight = 15d, Show = false }, new ModLoader.LoaderTask<string, int>("获取启动参数", McLaunchArgumentMain) { ProgressWeight = 2d }, new ModLoader.LoaderTask<int, int>("解压文件", McLaunchNatives) { ProgressWeight = 2d }, new ModLoader.LoaderTask<int, int>("预启动处理", (_) => McLaunchPrerun()) { ProgressWeight = 1d }, new ModLoader.LoaderTask<int, int>("执行自定义命令", McLaunchCustom) { ProgressWeight = 1d }, new ModLoader.LoaderTask<int, Process>("启动进程", McLaunchRun) { ProgressWeight = 2d }, new ModLoader.LoaderTask<Process, int>("等待游戏窗口出现", McLaunchWait) { ProgressWeight = 1d }, new ModLoader.LoaderTask<int, int>("结束处理", (_) => McLaunchEnd()) { ProgressWeight = 1d } }; // .ProgressWeight = 15, .Block = False
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 // 内存优化
                switch (Settings.Get<int>("VersionRamOptimize", Instance: ModMinecraft.McInstanceSelected))
                {
                    case 0: // 全局
                        {
                            if (Settings.Get<bool>("LaunchArgumentRam")) // 使用全局设置
                            {
                                ((ModLoader.LoaderCombo<string>)Loaders[2]).Block = false;
                                Loaders.Insert(3, new ModLoader.LoaderTask<int, int>("内存优化", McLaunchMemoryOptimize) { ProgressWeight = 30d });
                            }

                            break;
                        }
                    case 1: // 开启
                        {
                            ((ModLoader.LoaderCombo<string>)Loaders[2]).Block = false;
                            Loaders.Insert(3, new ModLoader.LoaderTask<int, int>("内存优化", McLaunchMemoryOptimize) { ProgressWeight = 30d });
                            break;
                        }
                    case 2: // 关闭
                        {
                            break;
                        }
                }
                var LaunchLoader = new ModLoader.LoaderCombo<object>("Minecraft 启动", Loaders) { Show = false };
                if (McLoginLoader.State == ModBase.LoadState.Finished)
                    McLoginLoader.State = ModBase.LoadState.Waiting; // 要求重启登录主加载器，它会自行决定是否启动副加载器
                                                                     // 等待加载器执行并更新 UI
                McLaunchLoaderReal = LaunchLoader;
                CanceledHint = null;
                LaunchLoader.Start();
                // 任务栏进度条
                ModLoader.LoaderTaskbarAdd(LaunchLoader);
                while (LaunchLoader.State == ModBase.LoadState.Loading)
                {
                    ModMain.FrmLaunchLeft.Dispatcher.Invoke(ModMain.FrmLaunchLeft.LaunchingRefresh);
                    Thread.Sleep(100);
                }
                ModMain.FrmLaunchLeft.Dispatcher.Invoke(ModMain.FrmLaunchLeft.LaunchingRefresh);
                // 成功与失败处理
                switch (LaunchLoader.State)
                {
                    case ModBase.LoadState.Finished:
                        {
                            ModMain.Hint(ModMinecraft.McInstanceSelected.Name + " 启动成功！", ModMain.HintType.Green);
                            break;
                        }
                    case ModBase.LoadState.Canceled:
                        {
                            if (CanceledHint is null)
                            {
                                ModMain.Hint(IsSavingBatch ? "已取消导出启动脚本！" : "已取消启动！", ModMain.HintType.Blue);
                            }
                            else
                            {
                                ModMain.Hint(CanceledHint, ModMain.HintType.Green);
                            }

                            break;
                        }
                    case ModBase.LoadState.Failed:
                        {
                            throw LaunchLoader.Error;
                        }

                    default:
                        {
                            throw new Exception("错误的状态改变：" + LaunchLoader.State.ToString());
                        }
                }
            }
            catch (Exception ex)
            {
                ex.ThrowIfCanceled();
                var CurrentEx = ex;
            NextInner:
                ;

                if (CurrentEx.Message.StartsWithF("$"))
                {
                    // 若有以 $ 开头的错误信息，则以此为准显示提示
                    ModMain.MyMsgBox(CurrentEx.Message.TrimStart('$'), IsSavingBatch ? "导出启动脚本失败" : "启动失败");
                    throw;
                }
                else if (CurrentEx.InnerException is not null)
                {
                    // 检查下一级错误
                    CurrentEx = CurrentEx.InnerException;
                    goto NextInner;
                }
                else
                {
                    // 没有特殊处理过的错误信息
                    McLaunchLog("错误：" + ex.GetDisplay(true));
                    Logger.Error(ex, IsSavingBatch ? "导出启动脚本失败" : "Minecraft 启动失败", LogBehavior.Alert);
                    throw;
                }
            }
        }

        #endregion

        #region 内存优化

        private static void McLaunchMemoryOptimize(ModLoader.LoaderTask<int, int> Loader)
        {
            McLaunchLog("内存优化开始");
            bool Finished = false;
            ModBase.RunInNewThread(() =>
        {
            PageOtherTest.MemoryOptimize(false);
            Finished = true;
        }, "Launch Memory Optimize");
            while (!Finished && !Loader.IsCanceled)
            {
                if (Loader.Progress < 0.7d)
                {
                    Loader.Progress += 0.007d; // 10s
                }
                else
                {
                    Loader.Progress += (0.95d - Loader.Progress) * 0.02d;
                } // 最快 += 0.005
                Thread.Sleep(100);
            }
        }

        #endregion

        #region 预检测

        private static void McLaunchPrecheck()
        {
            if (Settings.Get<bool>("SystemDebugDelay"))
                Thread.Sleep(ModBase.RandomInteger(100, 2000));
            // 检查路径
            if (ModMinecraft.McInstanceSelected.PathIndie.Contains("!") || ModMinecraft.McInstanceSelected.PathIndie.Contains(";"))
                throw new Exception("游戏路径中不可包含 ! 或 ;（" + ModMinecraft.McInstanceSelected.PathIndie + "）");
            if (ModMinecraft.McInstanceSelected.PathVersion.Contains("!") || ModMinecraft.McInstanceSelected.PathVersion.Contains(";"))
                throw new Exception("游戏路径中不可包含 ! 或 ;（" + ModMinecraft.McInstanceSelected.PathVersion + "）");
            // 检查版本
            if (ModMinecraft.McInstanceSelected is null)
                throw new Exception("未选择 Minecraft 版本！");
            ModMinecraft.McInstanceSelected.Load();
            if (ModMinecraft.McInstanceSelected.State == ModMinecraft.McInstance.McInstanceState.Error)
                throw new Exception("Minecraft 存在问题：" + ModMinecraft.McInstanceSelected.Info);
            // 检查输入信息
            string CheckResult = "";
            ModBase.RunInUiWait(() => CheckResult = McLoginAble(McLoginInput()));
            if (!string.IsNullOrEmpty(CheckResult))
                throw new ArgumentException(CheckResult);
            // 求赞助
            if (ModBuildChannel.ChannelIsOfficial && CurrentLaunchOptions?.SaveBatch is null) // 保存脚本时不提示
            {
                ModBase.RunInNewThread(() => { switch (Settings.Get<int>("SystemLaunchCount")) { case 10: case 20: case 40: case 60: case 80: case 100: case 120: case 150: case 200: case 250: case 300: case 350: case 400: case 500: case 600: case 700: case 800: case 900: case 1000: case 1200: case 1400: case 1600: case 1800: case 2000: { if (ModMain.MyMsgBox("PCL Next 已经为你启动了 " + Settings.Get<int>("SystemLaunchCount") + " 次游戏啦！" + "\r\n" + "如果 PCL Next 还算好用的话，能不能考虑赞助一下 PCL Next……" + "\r\n" + "如果没有大家的支持，PCL Next 很难在免费、无任何广告的情况下维持数年的更新（磕头）……！", Settings.Get<int>("SystemLaunchCount") + " 次启动！", "支持 PCL Next！", "但是我拒绝") == 1) { ModBase.OpenWebsite("https://meloong.com/afd/a/LTCat"); } break; } } }, "Donate");
            }
            // 正版购买提示
            if (CurrentLaunchOptions?.SaveBatch is null && !Settings.Get<bool>("HintBuy") && Settings.Get<McLoginType>("LoginType") != McLoginType.Ms) // 保存脚本时不提示
            {
                if (System.Globalization.CultureInfo.CurrentCulture.Name.StartsWithF("zh") || System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWithF("zh")) // 中文？
                {
                    ModBase.RunInNewThread(() => { switch (Settings.Get<int>("SystemLaunchCount")) { case 3: case 8: case 15: case 30: case 50: case 70: case 90: case 110: case 130: case 180: case 220: case 280: case 330: case 380: case 450: case 550: case 660: case 750: case 880: case 950: case 1100: case 1300: case 1500: case 1700: case 1900: { if (ModMain.MyMsgBox("你已经启动了 " + Settings.Get<int>("SystemLaunchCount") + " 次 Minecraft 啦！" + "\r\n" + "如果觉得 Minecraft 还不错，可以购买正版支持一下，毕竟开发游戏也真的很不容易……不要一直白嫖啦。" + "\r\n" + "\r\n" + "在登录一次正版账号后，就不会再出现这个提示了！", "考虑一下正版？", "支持正版游戏！", "下次一定") == 1) { ModBase.OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj"); } break; } } }, "Buy Minecraft");
                }
                else if (Settings.Get<McLoginType>("LoginType") == McLoginType.Legacy)
                {
                    switch (ModMain.MyMsgBox("你必须先登录正版账号，才能进行离线登录！", "正版验证", "购买正版", "试玩", "返回", Button1Action: () => ModBase.OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj")))
                    {
                        case 2:
                            {
                                ModMain.Hint("游戏将以试玩模式启动！", ModMain.HintType.Red);
                                CurrentLaunchOptions.ExtraGameArgs.Add("--demo");
                                break;
                            }
                        case 3:
                            {
                                throw new OperationCanceledException();
                            }
                    }
                }
            }
        }

        #endregion

        #region 主登录模块

        // 登录方式
        public enum McLoginType
        {
            Legacy = 0,
            Nide = 2,
            Auth = 3,
            Ms = 5
        }

        // 各个登录方式的对应数据
        public abstract class McLoginData
        {
            /// <summary>
        /// 登录方式。
        /// </summary>
            public McLoginType Type;
            public override bool Equals(object obj)
            {
                return obj is not null && obj.GetHashCode() == GetHashCode();
            }
        }
        public class McLoginServer : McLoginData
        {

            /// <summary>
        /// 登录用户名。
        /// </summary>
            public string UserName;
            /// <summary>
        /// 登录密码。
        /// </summary>
            public string Password;
            /// <summary>
        /// 登录服务器基础地址。
        /// </summary>
            public string BaseUrl;
            /// <summary>
        /// 登录所使用的标识符，目前只可能为 “Auth” 或 “Nide”，用于存储缓存等。
        /// </summary>
            public string Token;
            /// <summary>
        /// 登录方式的描述字符串，如 “正版”、“统一通行证”。
        /// </summary>
            public string Description;
            /// <summary>
        /// 是否在本次登录中强制要求玩家重新选择角色，目前仅对 Authlib-Injector 生效。
        /// </summary>
            public bool ForceReselectProfile = false;

            public McLoginServer(McLoginType Type)
            {
                this.Type = Type;
            }
            public override int GetHashCode()
            {
                return (int)Math.Round((UserName + Password + BaseUrl + Token + ((int)Type).ToString()).GetStableHashCode() % (decimal)int.MaxValue);
            }

        }
        public class McLoginMs : McLoginData
        {

            /// <summary>
        /// 缓存的 OAuth Refresh Token。若没有则为空字符串。
        /// </summary>
            public string OAuthRefreshToken = "";
            public string AccessToken = "";
            public string Uuid = "";
            public string UserName = "";
            public string ProfileJson = "";

            public McLoginMs()
            {
                Type = McLoginType.Ms;
            }
            public override int GetHashCode()
            {
                // 不能使用全部信息，这俩就足以在登录输入时确保是同一个用户了
                return (int)Math.Round((OAuthRefreshToken + UserName).GetStableHashCode() % (decimal)int.MaxValue);
            }
        }
        public class McLoginLegacy : McLoginData
        {
            /// <summary>
        /// 登录用户名。
        /// </summary>
            public string UserName;
            /// <summary>
        /// 皮肤种类。
        /// </summary>
            public int SkinType;
            /// <summary>
        /// 若采用正版皮肤，则为该皮肤名。
        /// </summary>
            public string SkinName;

            public McLoginLegacy()
            {
                Type = McLoginType.Legacy;
            }
            public override int GetHashCode()
            {
                return (int)Math.Round((UserName + SkinType + SkinName + ((int)Type).ToString()).GetStableHashCode() % (decimal)int.MaxValue);
            }
        }

        // 登录返回结果
        public struct McLoginResult
        {
            public string Name;
            public string Uuid;
            public string AccessToken;
            public string Type;
            public string ClientToken;
            /// <summary>
        /// 进行微软登录时返回的 profile 信息。
        /// </summary>
            public string ProfileJson;
        }

        /// <summary>
    /// 根据登录信息获取玩家的 MC 用户名。如果无法获取则返回 Nothing。
    /// </summary>
        public static string McLoginName()
        {
            // 根据当前登录方式优先返回
            switch (Settings.Get<McLoginType>("LoginType"))
            {
                case McLoginType.Ms:
                    {
                        if (!string.IsNullOrEmpty(Settings.Get<string>("CacheMsV2Name")))
                            return Settings.Get<string>("CacheMsV2Name");
                        break;
                    }
                case McLoginType.Legacy:
                    {
                        if (!string.IsNullOrEmpty(Settings.Get<string>("LoginLegacyName")))
                            return Settings.Get<string>("LoginLegacyName").ToString().BeforeFirst("¨");
                        break;
                    }
                case McLoginType.Nide:
                    {
                        if (!string.IsNullOrEmpty(Settings.Get<string>("CacheNideName")))
                            return Settings.Get<string>("CacheNideName");
                        break;
                    }
                case McLoginType.Auth:
                    {
                        if (!string.IsNullOrEmpty(Settings.Get<string>("CacheAuthName")))
                            return Settings.Get<string>("CacheAuthName");
                        break;
                    }
            }
            // 查找所有可能的项
            if (!string.IsNullOrEmpty(Settings.Get<string>("CacheMsV2Name")))
                return Settings.Get<string>("CacheMsV2Name");
            if (!string.IsNullOrEmpty(Settings.Get<string>("CacheNideName")))
                return Settings.Get<string>("CacheNideName");
            if (!string.IsNullOrEmpty(Settings.Get<string>("CacheAuthName")))
                return Settings.Get<string>("CacheAuthName");
            if (!string.IsNullOrEmpty(Settings.Get<string>("LoginLegacyName")))
                return Settings.Get<string>("LoginLegacyName").ToString().BeforeFirst("¨");
            return null;
        }
        /// <summary>
    /// 当前是否可以进行登录。若不可以则会返回错误原因。
    /// </summary>
        public static string McLoginAble()
        {
            switch (Settings.Get<McLoginType>("LoginType"))
            {
                case McLoginType.Ms:
                    {
                        if (string.IsNullOrEmpty(Settings.Get<string>("CacheMsV2OAuthRefresh")))
                        {
                            return ModMain.FrmLoginMs.IsVaild();
                        }
                        else
                        {
                            return "";
                        }
                    }
                case McLoginType.Legacy:
                    {
                        return ModMain.FrmLoginLegacy.IsVaild();
                    }
                case McLoginType.Nide:
                    {
                        if (string.IsNullOrEmpty(Settings.Get<string>("CacheNideAccess")))
                        {
                            return ModMain.FrmLoginNide.IsVaild();
                        }
                        else
                        {
                            return "";
                        }
                    }
                case McLoginType.Auth:
                    {
                        if (string.IsNullOrEmpty(Settings.Get<string>("CacheAuthAccess")))
                        {
                            return ModMain.FrmLoginAuth.IsVaild();
                        }
                        else
                        {
                            return "";
                        }
                    }

                default:
                    {
                        return "未知的登录方式";
                    }
            }
        }
        /// <summary>
    /// 登录输入是否可以进行登录。若不可以则会返回错误原因。
    /// </summary>
        public static string McLoginAble(McLoginData LoginData)
        {
            switch (LoginData.Type)
            {
                case McLoginType.Ms:
                    {
                        return PageLoginMs.IsVaild((McLoginMs)LoginData);
                    }
                case McLoginType.Legacy:
                    {
                        return PageLoginLegacy.IsVaild((McLoginLegacy)LoginData);
                    }
                case McLoginType.Nide:
                    {
                        return PageLoginNide.IsVaild((McLoginServer)LoginData);
                    }
                case McLoginType.Auth:
                    {
                        return PageLoginAuth.IsVaild((McLoginServer)LoginData);
                    }

                default:
                    {
                        return "未知的登录方式";
                    }
            }
        }

        // 登录主模块加载器
        public static ModLoader.LoaderTask<McLoginData, McLoginResult> McLoginLoader = new ModLoader.LoaderTask<McLoginData, McLoginResult>("登录", McLoginStart, McLoginInput, ThreadPriority.BelowNormal) { ReloadTimeout = 1, ProgressWeight = 15d, Block = false };
        public static McLoginData McLoginInput()
        {
            McLoginData LoginData = null;
            var LoginType = Settings.Get<McLoginType>("LoginType");
            try
            {
                switch (LoginType)
                {
                    case McLoginType.Legacy:
                        {
                            LoginData = PageLoginLegacy.GetLoginData();
                            break;
                        }
                    case McLoginType.Ms:
                        {
                            if (string.IsNullOrEmpty(Settings.Get<string>("CacheMsV2OAuthRefresh")))
                            {
                                LoginData = PageLoginMs.GetLoginData();
                            }
                            else
                            {
                                LoginData = PageLoginMsSkin.GetLoginData();
                            }

                            break;
                        }
                    case McLoginType.Nide:
                        {
                            if (string.IsNullOrEmpty(Settings.Get<string>("CacheNideAccess")))
                            {
                                LoginData = PageLoginNide.GetLoginData();
                            }
                            else
                            {
                                LoginData = PageLoginNideSkin.GetLoginData();
                            }

                            break;
                        }
                    case McLoginType.Auth:
                        {
                            if (string.IsNullOrEmpty(Settings.Get<string>("CacheAuthAccess")))
                            {
                                LoginData = PageLoginAuth.GetLoginData();
                            }
                            else
                            {
                                LoginData = PageLoginAuthSkin.GetLoginData();
                            }

                            break;
                        }
                }
                return LoginData;
            }
            catch (Exception ex)
            {
                throw new Exception($"获取登录输入信息失败（{LoginType}）", ex);
            }
        }
        private static void McLoginStart(ModLoader.LoaderTask<McLoginData, McLoginResult> Data)
        {
            McLaunchLog("登录加载已开始");
            // 校验登录信息
            string CheckResult = McLoginAble(Data.Input);
            if (!string.IsNullOrEmpty(CheckResult))
                throw new ArgumentException(CheckResult);
            // 获取对应加载器
            ModLoader.LoaderBase Loader = null;
            switch (Data.Input.Type)
            {
                case McLoginType.Ms:
                    {
                        Loader = McLoginMsLoader;
                        break;
                    }
                case McLoginType.Legacy:
                    {
                        Loader = McLoginLegacyLoader;
                        break;
                    }
                case McLoginType.Nide:
                    {
                        Loader = McLoginNideLoader;
                        break;
                    }
                case McLoginType.Auth:
                    {
                        Loader = McLoginAuthLoader;
                        break;
                    }
            }
            // 尝试加载
            Loader.WaitForExit(Data.Input, McLoginLoader, Data.IsForceRestarting);
            // VB 原文是 Loader.Output 的晚绑定调用：Loader 声明成 Object，运行时才按实际加载器类型取 Output。
            // 迁移器把它写成 ((dynamic)(McLoginResult)((object)Loader)).Output，多插的强转把 LoaderBase
            // 先转成 McLoginResult，必然抛 InvalidCastException——结果是**所有登录方式都失败**，
            // 启动页一直停在「登录失败」，无法启动游戏（2026-11-14 修复）。
            // LoaderBase 上没有 Output 属性，Output 只存在于各 LoaderTask<TIn, TOut> 上，因此按实际类型取。
            switch (Loader)
            {
                case ModLoader.LoaderTask<McLoginMs, McLoginResult> MsLoader:
                    Data.Output = MsLoader.Output;
                    break;
                case ModLoader.LoaderTask<McLoginLegacy, McLoginResult> LegacyLoader:
                    Data.Output = LegacyLoader.Output;
                    break;
                case ModLoader.LoaderTask<McLoginServer, McLoginResult> ServerLoader:
                    Data.Output = ServerLoader.Output;
                    break;
                default:
                    throw new Exception($"未知的登录加载器：{(Loader is null ? "null" : Loader.GetType().Name)}");
            }
            ModBase.RunInUi(() => ModMain.FrmLaunchLeft.RefreshPage(true, false)); // 刷新自动填充列表
            McLaunchLog("登录加载已结束");
        }

        #endregion
        #region 分方式登录模块

        // 各个登录方式的主对象与输入构造
        public static ModLoader.LoaderTask<McLoginMs, McLoginResult> McLoginMsLoader = new ModLoader.LoaderTask<McLoginMs, McLoginResult>("Loader Login Ms", McLoginMsStart) { ReloadTimeout = 1 };
        public static ModLoader.LoaderTask<McLoginLegacy, McLoginResult> McLoginLegacyLoader = new ModLoader.LoaderTask<McLoginLegacy, McLoginResult>("Loader Login Legacy", McLoginLegacyStart);
        public static ModLoader.LoaderTask<McLoginServer, McLoginResult> McLoginNideLoader = new ModLoader.LoaderTask<McLoginServer, McLoginResult>("Loader Login Nide", McLoginServerStart) { ReloadTimeout = 1000 * 60 * 10 };
        public static ModLoader.LoaderTask<McLoginServer, McLoginResult> McLoginAuthLoader = new ModLoader.LoaderTask<McLoginServer, McLoginResult>("Loader Login Auth", McLoginServerStart) { ReloadTimeout = 1000 * 60 * 10 };

        // 主加载函数，返回所有需要的登录信息
        private static void McLoginMsStart(ModLoader.LoaderTask<McLoginMs, McLoginResult> Data)
        {
            var Input = Data.Input;
            string LogUsername = Input.UserName;
            McLaunchLog("登录方式：正版（" + (string.IsNullOrEmpty(LogUsername) ? "尚未登录" : LogUsername) + "）");
            Data.Progress = 0.05d;
            // 检查是否已经登录完成
            long ExpiresAt = Settings.Get<long>("CacheMsV2Expires");
            if (!Data.IsForceRestarting && ExpiresAt > 0L && ExpiresAt > ModBase.ToUnixTimestampUtc(DateTime.UtcNow) && (Input.UserName ?? "") == (Settings.Get<string>("CacheMsV2Name") ?? "")) // 不要求强行重启
                                                                                                                                                                                                 // AccessToken 尚未过期
            {
                Data.Output = new McLoginResult()
                {
                    Name = Input.UserName,
                    Type = "Microsoft",
                    AccessToken = Settings.Get<string>("CacheMsV2Access"),
                    Uuid = Settings.Get<string>("CacheMsV2Uuid"),
                    ClientToken = Settings.Get<string>("CacheMsV2Uuid"),
                    ProfileJson = Settings.Get<string>("CacheMsV2ProfileJson")
                };
                McLaunchLog("无需登录，AccessToken 尚未过期");
                goto SkipLogin;
            }
            // 尝试登录
            (string OAuthAccessToken, string OAuthRefreshToken) OAuthTokens;
            // VB 原文把 Relogin 标签放在 `If Input.OAuthRefreshToken = ""` 分支内部，
            // `Else` 分支里的 `GoTo Relogin` 会跳进该分支执行 MsLoginStep1New，然后照常往下走。
            // C# 禁止跳入兄弟 If 块，故直接在该处调用 MsLoginStep1New：执行次数与顺序与 VB 完全一致（只执行一次）。
            if (string.IsNullOrEmpty(Input.OAuthRefreshToken))
            {
            // 无 RefreshToken
                OAuthTokens = MsLoginStep1New(Data);
            }
            else
            {
                // 有 RefreshToken
                OAuthTokens = MsLoginStep1Refresh(Input.OAuthRefreshToken);
                if (OAuthTokens.OAuthAccessToken == "Relogin")
                    OAuthTokens = MsLoginStep1New(Data); // 对应 VB 的 GoTo Relogin（要求重新打开登录网页认证）
            }
            if (Data.IsCanceled)
                throw new OperationCanceledException();
            Data.Progress = 0.25d;
            if (Data.IsCanceled)
                throw new OperationCanceledException();
            string OAuthAccessToken = OAuthTokens.OAuthAccessToken;
            string OAuthRefreshToken = OAuthTokens.OAuthRefreshToken;
            string XBLToken = MsLoginStep2(OAuthAccessToken);
            Data.Progress = 0.4d;
            if (Data.IsCanceled)
                throw new OperationCanceledException();
            var Tokens = MsLoginStep3(XBLToken);
            Data.Progress = 0.55d;
            if (Data.IsCanceled)
                throw new OperationCanceledException();
            var LoginResult = MsLoginStep4(Tokens);
            ExpiresAt = LoginResult.ExpiresAt;
            Data.Progress = 0.7d;
            if (Data.IsCanceled)
                throw new OperationCanceledException();
            MsLoginStep5(LoginResult.AccessToken);
            Data.Progress = 0.85d;
            if (Data.IsCanceled)
                throw new OperationCanceledException();
            var Result = MsLoginStep6(LoginResult.AccessToken);
            Data.Progress = 0.98d;
            // 输出登录结果
            Settings.Set("CacheMsV2OAuthRefresh", OAuthRefreshToken);
            Settings.Set("CacheMsV2Access", LoginResult.AccessToken);
            Settings.Set("CacheMsV2Uuid", Result.UUID);
            Settings.Set("CacheMsV2Name", Result.UserName);
            Settings.Set("CacheMsV2ProfileJson", Result.ProfileJson);
            Settings.Set("CacheMsV2Expires", LoginResult.ExpiresAt);
            JObject MsJson = (JObject)Settings.Get<string>("LoginMsJson").DeserializeJson();
            MsJson.Remove(Input.UserName); // 如果更改了玩家名……
            MsJson[Result.UserName] = OAuthRefreshToken;
            Settings.Set("LoginMsJson", MsJson.ToString(Formatting.None));
            Data.Output = new McLoginResult() { AccessToken = LoginResult.AccessToken, Name = Result.UserName, Uuid = Result.UUID, Type = "Microsoft", ClientToken = Result.UUID, ProfileJson = Result.ProfileJson };
        // 结束
        SkipLogin:
            ;

            McLaunchLog($"微软登录结束，AccessToken 将在 {DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).LocalDateTime} 过期");
            Settings.Set("HintBuy", true); // 关闭正版购买提示
            if (ModSecret.ThemeUnlock(10, false))
                ModMain.MyMsgBox("感谢你对正版游戏的支持！" + "\r\n" + "隐藏主题 跳票红 已解锁！", "提示");
        }
        private static void McLoginServerStart(ModLoader.LoaderTask<McLoginServer, McLoginResult> Data)
        {
            var Input = Data.Input;
            bool NeedRefresh = false;
            bool WasRefreshed = false;
            McLaunchLog("登录方式：" + Input.Description);
            Data.Progress = 0.05d;
            // 尝试登录
            // VB 原文的 Refresh 标签位于 `If ... Then` 块内部，而「尝试普通登录」之后的 `GoTo Refresh`
            // 会跳回块内重跑一次刷新登录；C# 禁止跳入块内，故改为「标记位 + 循环」，
            // 严格保持 VB 执行顺序：验证登录 → 刷新登录 → 普通登录 →（若需要）再刷新一次。
            bool EnterRefresh = false; // 是否需要执行刷新登录段（等价于 VB「进入了上面的 If 块」或「GoTo Refresh」）
            if (!Data.Input.ForceReselectProfile && (Settings.Get<string>("Cache" + Input.Token + "Username") ?? "") == (Data.Input.UserName ?? "") && (Settings.Get<string>("Cache" + Input.Token + "Pass") ?? "") == (Data.Input.Password ?? "") && !string.IsNullOrEmpty(Settings.Get<string>("Cache" + Input.Token + "Access")) && !string.IsNullOrEmpty(Settings.Get<string>("Cache" + Input.Token + "Client")) && !string.IsNullOrEmpty(Settings.Get<string>("Cache" + Input.Token + "Uuid")) && !string.IsNullOrEmpty(Settings.Get<string>("Cache" + Input.Token + "Name")))
            {
                // 尝试验证登录
                try
                {
                    if (Data.IsCanceled)
                        throw new OperationCanceledException();
                    McLoginRequestValidate(ref Data);
                    goto LoginFinish;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "验证登录失败");
                    if (ex.IsBadNetwork())
                        throw new Exception("$登录失败：你的网络环境不佳，导致难以连接到海外服务器。" + "\r\n" + "请稍后再试，或使用加速器、VPN 改善网络环境。");
                }
                Data.Progress = 0.25d;
                EnterRefresh = true;
            }

            while (true)
            {
                if (EnterRefresh)
                {
                    EnterRefresh = false;
                    // 尝试刷新登录（VB 的 Refresh 标签段）
                    try
                    {
                        if (Data.IsCanceled)
                            throw new OperationCanceledException();
                        McLoginRequestRefresh(ref Data, NeedRefresh);
                        goto LoginFinish;
                    }
                    catch (Exception ex)
                    {
                        McLaunchLog("刷新登录失败：" + ex.GetDisplay(true));
                        if (WasRefreshed)
                            throw new Exception("二轮刷新登录失败", ex);
                    }
                    Data.Progress = NeedRefresh ? 0.85d : 0.45d;
                    // 首轮刷新失败后落到下面的普通登录；若是 GoTo Refresh 的回跳，本段失败必定在上面的 catch 抛出、
                    // 成功则已跳到 LoginFinish，因此不会再回落到普通登录，与 VB 流程一致。
                    continue;
                }
                // 尝试普通登录
                try
                {
                    if (Data.IsCanceled)
                        throw new OperationCanceledException();
                    NeedRefresh = McLoginRequestLogin(ref Data);
                }
                catch (Exception ex)
                {
                    McLaunchLog("登录失败：" + ex.GetDisplay(true));
                    throw;
                }
                if (!NeedRefresh)
                    break; // VB 原文 If NeedRefresh Then ... 否则直接到 LoginFinish
                McLaunchLog("重新进行刷新登录");
                WasRefreshed = true;
                Data.Progress = 0.65d;
                EnterRefresh = true; // 对应 VB 的 GoTo Refresh
            }

        LoginFinish:
            ;

            Data.Progress = 0.95d;
            // 保存启动记录
            var Dict = new Dictionary<string, string>();
            var Emails = new List<string>();
            var Passwords = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(Settings.Get<string>("Login" + Input.Token + "Email")))
                    Emails.AddRange(Settings.Get<string>("Login" + Input.Token + "Email").Split("¨"));
                if (!string.IsNullOrEmpty(Settings.Get<string>("Login" + Input.Token + "Pass")))
                    Passwords.AddRange(Settings.Get<string>("Login" + Input.Token + "Pass").Split("¨"));
                for (int i = 0, loopTo = Emails.Count - 1; i <= loopTo; i++)
                    Dict.Add(Emails[i], Passwords[i]);
                Dict.Remove(Input.UserName);
                Emails = new List<string>(Dict.Keys);
                Emails.Insert(0, Input.UserName);
                Passwords = new List<string>(Dict.Values);
                Passwords.Insert(0, Input.Password);
                Settings.Set("Login" + Input.Token + "Email", Emails.Join('¨'));
                Settings.Set("Login" + Input.Token + "Pass", Passwords.Join('¨'));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "保存启动记录失败", LogBehavior.Toast);
                Settings.Set("Login" + Input.Token + "Email", "");
                Settings.Set("Login" + Input.Token + "Pass", "");
            }
        }
        private static void McLoginLegacyStart(ModLoader.LoaderTask<McLoginLegacy, McLoginResult> Data)
        {
            var Input = Data.Input;
            McLaunchLog("登录方式：离线（" + Input.UserName + "）");
            Data.Progress = 0.1d;
            {
                ref var withBlock = ref Data.Output;
                withBlock.Name = Input.UserName;
                withBlock.Uuid = McLoginLegacyUuidWithCustomSkin(Input.UserName, Input.SkinType, Input.SkinName);
                withBlock.Type = "Legacy";
            }
            // 将结果扩展到所有项目中
            Data.Output.AccessToken = Data.Output.Uuid;
            Data.Output.ClientToken = Data.Output.Uuid;
            // 保存启动记录
            var Names = new List<string>();
            if (!string.IsNullOrEmpty(Settings.Get<string>("LoginLegacyName")))
                Names.AddRange(Settings.Get<string>("LoginLegacyName").ToString().Split("¨"));
            Names.Remove(Input.UserName);
            Names.Insert(0, Input.UserName);
            Settings.Set("LoginLegacyName", Names.Join('¨'));
        }

        // Server 登录：三种验证方式的请求
        private static void McLoginRequestValidate(ref ModLoader.LoaderTask<McLoginServer, McLoginResult> Data)
        {
            McLaunchLog("验证登录开始（Validate, " + Data.Input.Token + "）");
            // 提前缓存信息，否则如果在登录请求过程中退出登录，设置项目会被清空，导致输出存在空值
            string AccessToken = Settings.Get<string>("Cache" + Data.Input.Token + "Access");
            string ClientToken = Settings.Get<string>("Cache" + Data.Input.Token + "Client");
            string Uuid = Settings.Get<string>("Cache" + Data.Input.Token + "Uuid");
            string Name = Settings.Get<string>("Cache" + Data.Input.Token + "Name");
            // 发送登录请求
            var RequestData = new JObject(new JProperty("accessToken", AccessToken), new JProperty("clientToken", ClientToken), new JProperty("requestUser", true));
            ModNet.NetRequestByClientRetry(Data.Input.BaseUrl + "/validate", HttpMethod.Post, Content: RequestData.ToString(Formatting.None), Headers: new[,] { { "Accept-Language", "zh-CN" } }, ContentType: "application/json; charset=utf-8"); // 没有返回值的
                                                                                                                                                                                                                                                   // 将登录结果输出
            Data.Output.AccessToken = AccessToken;
            Data.Output.ClientToken = ClientToken;
            Data.Output.Uuid = Uuid;
            Data.Output.Name = Name;
            Data.Output.Type = Data.Input.Token;
            // 不更改缓存，直接结束
            McLaunchLog("验证登录成功（Validate, " + Data.Input.Token + "）");
        }
        private static void McLoginRequestRefresh(ref ModLoader.LoaderTask<McLoginServer, McLoginResult> Data, bool RequestUser)
        {
            McLaunchLog("刷新登录开始（Refresh, " + Data.Input.Token + "）");
            JObject LoginJson = (JObject)ModNet.NetRequestByClientRetry(Data.Input.BaseUrl + "/refresh", HttpMethod.Post, Content: new JObject(new JProperty("selectedProfile", new JObject(new JProperty("name", Settings.Get<string>($"Cache{Data.Input.Token}Name")), new JProperty("id", Settings.Get<string>($"Cache{Data.Input.Token}Uuid")))), new JProperty("accessToken", Settings.Get<string>($"Cache{Data.Input.Token}Access")), new JProperty("requestUser", true)).ToString(Formatting.None), Headers: new[,] { { "Accept-Language", "zh-CN" } }, ContentType: "application/json; charset=utf-8", RequireJson: true).DeserializeJson();
            // 将登录结果输出
            if (LoginJson["selectedProfile"] is null)
                throw new Exception("选择的角色 " + Settings.Get<string>("Cache" + Data.Input.Token + "Name") + " 无效！");
            Data.Output.AccessToken = LoginJson["accessToken"].ToString();
            Data.Output.ClientToken = LoginJson["clientToken"].ToString();
            Data.Output.Uuid = LoginJson["selectedProfile"]["id"].ToString();
            Data.Output.Name = LoginJson["selectedProfile"]["name"].ToString();
            Data.Output.Type = Data.Input.Token;
            // 保存缓存
            Settings.Set("Cache" + Data.Input.Token + "Access", Data.Output.AccessToken);
            Settings.Set("Cache" + Data.Input.Token + "Client", Data.Output.ClientToken);
            Settings.Set("Cache" + Data.Input.Token + "Uuid", Data.Output.Uuid);
            Settings.Set("Cache" + Data.Input.Token + "Name", Data.Output.Name);
            Settings.Set("Cache" + Data.Input.Token + "Username", Data.Input.UserName);
            Settings.Set("Cache" + Data.Input.Token + "Pass", Data.Input.Password);
            McLaunchLog("刷新登录成功（Refresh, " + Data.Input.Token + "）");
        }
        private static bool McLoginRequestLogin(ref ModLoader.LoaderTask<McLoginServer, McLoginResult> Data)
        {
            try
            {
                bool NeedRefresh = false;
                McLaunchLog("登录开始（Login, " + Data.Input.Token + "）");
                var RequestData = new JObject(new JProperty("agent", new JObject(new JProperty("name", "Minecraft"), new JProperty("version", 1))), new JProperty("username", Data.Input.UserName), new JProperty("password", Data.Input.Password), new JProperty("requestUser", true));
                JObject LoginJson = (JObject)ModNet.NetRequestByClientRetry(Data.Input.BaseUrl + "/authenticate", HttpMethod.Post, Content: RequestData.ToString(Formatting.None), Headers: new[,] { { "Accept-Language", "zh-CN" } }, ContentType: "application/json; charset=utf-8", RequireJson: true).DeserializeJson();
                // 检查登录结果
                if (LoginJson["availableProfiles"].Count() == 0)
                {
                    if (Data.Input.ForceReselectProfile)
                        ModMain.Hint("你还没有创建角色，无法更换！", ModMain.HintType.Red);
                    throw new Exception("$你还没有创建角色，请在创建角色后再试！");
                }
                else if (Data.Input.ForceReselectProfile && LoginJson["availableProfiles"].IsSingle())
                {
                    ModMain.Hint("你的账户中只有一个角色，无法更换！", ModMain.HintType.Red);
                }
                string SelectedName = null;
                string SelectedId = null;
                if ((LoginJson["selectedProfile"] is null || Data.Input.ForceReselectProfile) && LoginJson["availableProfiles"].Count() > 1)
                {
                    // 要求选择档案；优先从缓存读取
                    NeedRefresh = true;
                    string CacheId = Settings.Get<string>("Cache" + Data.Input.Token + "Uuid");
                    foreach (var Profile in LoginJson["availableProfiles"])
                    {
                        if ((Profile["id"].ToString() ?? "") == (CacheId ?? ""))
                        {
                            SelectedName = Profile["name"].ToString();
                            SelectedId = Profile["id"].ToString();
                            McLaunchLog("根据缓存选择的角色：" + SelectedName);
                        }
                    }
                    // 缓存无效，要求玩家选择
                    if (SelectedName is null)
                    {
                        McLaunchLog("要求玩家选择角色");
                        ModBase.RunInUiWait(() =>
        {
            var SelectionControl = new List<IMyRadio>();
            var SelectionJson = new List<JToken>();
            foreach (var Profile in LoginJson["availableProfiles"])
            {
                SelectionControl.Add(new MyRadioBox() { Text = Profile["name"].ToString() });
                SelectionJson.Add(Profile);
            }
            int SelectedIndex = (int)ModMain.MyMsgBoxSelect(SelectionControl, "选择使用的角色");
            SelectedName = SelectionJson[SelectedIndex]["name"].ToString();
            SelectedId = SelectionJson[SelectedIndex]["id"].ToString();
        });
                        McLaunchLog("玩家选择的角色：" + SelectedName);
                    }
                }
                else
                {
                    SelectedName = LoginJson["selectedProfile"]["name"].ToString();
                    SelectedId = LoginJson["selectedProfile"]["id"].ToString();
                }
                // 将登录结果输出
                Data.Output.AccessToken = LoginJson["accessToken"].ToString();
                Data.Output.ClientToken = LoginJson["clientToken"].ToString();
                Data.Output.Name = SelectedName;
                Data.Output.Uuid = SelectedId;
                Data.Output.Type = Data.Input.Token;
                // 保存缓存
                Settings.Set("Cache" + Data.Input.Token + "Access", Data.Output.AccessToken);
                Settings.Set("Cache" + Data.Input.Token + "Client", Data.Output.ClientToken);
                Settings.Set("Cache" + Data.Input.Token + "Uuid", Data.Output.Uuid);
                Settings.Set("Cache" + Data.Input.Token + "Name", Data.Output.Name);
                Settings.Set("Cache" + Data.Input.Token + "Username", Data.Input.UserName);
                Settings.Set("Cache" + Data.Input.Token + "Pass", Data.Input.Password);
                McLaunchLog("登录成功（Login, " + Data.Input.Token + "）");
                return NeedRefresh;
            }
            catch (Exception ex)
            {
                string AllMessage = ex.GetDisplay(false);
                Logger.Info(ex, "登录失败原始错误信息");
                // 读取服务器返回的错误
                if (ex is HttpRequestCodeException)
                {
                    string ErrorMessage = null;
                    try
                    {
                        string Response = ((HttpRequestCodeException)ex).Response;
                        if (Response.ContainsIgnoreCase("errorMessage"))
                            ErrorMessage = Response.DeserializeJson()["errorMessage"].ToString();
                    }
                    catch
                    {
                    }
                    if (!string.IsNullOrWhiteSpace(ErrorMessage))
                    {
                        if (ErrorMessage.Contains("密码错误") || ErrorMessage.ContainsIgnoreCase("Incorrect username or password"))
                        {
                            // 密码错误，退出登录 (#5090)
                            McLaunchLog("密码错误，退出登录");
                            switch (Data.Input.Type)
                            {
                                case McLoginType.Auth:
                                    {
                                        ModBase.RunInUi(PageLoginAuthSkin.ExitLogin);
                                        break;
                                    }
                                case McLoginType.Nide:
                                    {
                                        ModBase.RunInUi(PageLoginNideSkin.ExitLogin);
                                        break;
                                    }
                            }
                        }
                        throw new Exception("$登录失败：" + ErrorMessage);
                    }
                }
                // 通用关键字检测
                if (AllMessage.Contains("(403)"))
                {
                    switch (Data.Input.Type)
                    {
                        case McLoginType.Auth:
                            {
                                throw new Exception("$登录失败，以下为可能的原因：" + "\r\n" + " - 输入的账号或密码错误。" + "\r\n" + " - 登录尝试过于频繁，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" + "\r\n" + " - 只注册了账号，但没有在皮肤站新建角色。");
                            }
                        case McLoginType.Nide:
                            {
                                throw new Exception("$登录失败，以下为可能的原因：" + "\r\n" + " - 输入的账号或密码错误。" + "\r\n" + " - 密码错误次数过多，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" + "\r\n" + (Data.Input.UserName.Contains("@") ? "" : " - 登录账号应为邮箱或统一通行证账号，而非游戏角色 ID。" + "\r\n") + " - 只注册了账号，但没有加入对应服务器。");
                            }
                    }
                }
                else if (ex.IsBadNetwork())
                {
                    throw new Exception("$登录失败：你的网络环境不佳，导致难以连接到海外服务器。" + "\r\n" + "请稍后再试，或使用加速器、VPN 改善网络环境。");
                }
                else if (ex.Message.StartsWithF("$"))
                {
                    throw;
                }
                else
                {
                    throw new Exception("登录失败：" + ex.Message, ex);
                }
                return false;
            }
        }

        // 微软登录步骤 1，原始登录：获取 DeviceCode 并开启登录网页
        private static (string OAuthAccessToken, string OAuthRefreshToken) MsLoginStep1New(ModLoader.LoaderTask<McLoginMs, McLoginResult> Data)
        {
        // 参考：https://learn.microsoft.com/zh-cn/entra/identity-platform/v2-oauth2-device-code

        // 初始请求
        Retry:
            ;

            McLaunchLog("开始微软登录步骤 1/6（原始登录）");
            JObject PrepareJson = (JObject)ModNet.NetRequestByClientRetry("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", HttpMethod.Post, Content: $"client_id={ModSecret.OAuthClientId}&tenant=/consumers&scope=XboxLive.signin%20offline_access", ContentType: "application/x-www-form-urlencoded", RequireJson: true).DeserializeJson();
            McLaunchLog("网页登录地址：" + PrepareJson["verification_uri"].ToString());

            // 弹窗
            var Converter = new ModMain.MyMsgBoxConverter() { Content = PrepareJson, ForceWait = true, Type = ModMain.MyMsgBoxType.Login };
            ModMain.WaitingMyMsgBox.Enqueue(Converter);
            while (Converter.Result is null)
                Thread.Sleep(100);
            if (Converter.Result is ModBase.RestartException)
            {
                if (ModMain.MyMsgBox($"请在登录时选择 {ModBase.vbLQ}其他登录方法{ModBase.vbRQ}，然后选择 {ModBase.vbLQ}使用我的密码{ModBase.vbRQ}。{"\r\n"}如果没有该选项，请选择 {ModBase.vbLQ}设置密码{ModBase.vbRQ}，设置完毕后再登录。", "需要使用密码登录", "重新登录", "设置密码", "取消", Button2Action: () => ModBase.OpenWebsite("https://account.live.com/password/Change")) == 1)
                {
                    goto Retry;
                }
                else
                {
                    throw new OperationCanceledException();
                }
            }
            else if (Converter.Result is Exception)
            {
                throw (Exception)Converter.Result;
            }
            else
            {
                return ((string OAuthAccessToken, string OAuthRefreshToken))(((dynamic)Converter.Result)[0], ((dynamic)Converter.Result)[1]);
            }
        }
        // 微软登录步骤 1，刷新登录：从 OAuth Code 或 OAuth RefreshToken 获取 {OAuth AccessToken, OAuth RefreshToken}
        private static (string OAuthAccessToken, string OAuthRefreshToken) MsLoginStep1Refresh(string Code)
        {
            McLaunchLog("开始微软登录步骤 1/6（刷新登录）");

            string Result;
            try
            {
                Result = ModNet.NetRequestByClientMultiple("https://login.live.com/oauth20_token.srf", HttpMethod.Post, Content: $"client_id={ModSecret.OAuthClientId}&refresh_token={StringUtils.FormUrlEscape(Code)}&grant_type=refresh_token&scope=XboxLive.signin%20offline_access", ContentType: "application/x-www-form-urlencoded", Headers: new[,] { { "Accept-Language", "en-US,en;q=0.5" }, { "X-Requested-With", "XMLHttpRequest" } }, ThreadCount: 2);
            }
            catch (HttpRequestCodeException ex)
            {
                // 修改错误列表时，同时检查 MyMsgLogin.xaml.vb 中的对应代码
                string Response = ex.Response;
                if (Response.ContainsIgnoreCase("must sign in again") || Response.ContainsIgnoreCase("password expired") || Response.Contains("refresh_token") && Response.ContainsIgnoreCase("is not valid") || Response.ContainsIgnoreCase("expired")) // #269
                                                                                                                                                                                                                                                         // #8611
                {
                    return ("Relogin", "");
                }
                else if (Response.Contains("Account security interrupt"))
                {
                    ModMain.MyMsgBox("该账号由于安全问题无法登陆，请前往微软账户页获取更多信息。", "登录失败", "我知道了", IsWarn: true);
                    throw new OperationCanceledException();
                }
                else if (Response.Contains("service abuse"))
                {
                    ModMain.MyMsgBox("非常抱歉，该账号已被微软封禁，无法登录。", "登录失败", "我知道了", IsWarn: true);
                    throw new OperationCanceledException();
                }
                else
                {
                    throw;
                }
            }

            JObject ResultJson = (JObject)Result.DeserializeJson();
            string AccessToken = ResultJson["access_token"].ToString();
            string RefreshToken = ResultJson["refresh_token"].ToString();
            return (AccessToken, RefreshToken);
        }
        // 微软登录步骤 2：从 OAuth AccessToken 获取 XBLToken
        private static string MsLoginStep2(string AccessToken)
        {
            McLaunchLog("开始微软登录步骤 2/6");

            string Request = @"{
           ""Properties"": {
               ""AuthMethod"": ""RPS"",
               ""SiteName"": ""user.auth.xboxlive.com"",
               ""RpsTicket"": """ + (AccessToken.StartsWithF("d=") ? "" : "d=") + AccessToken + @"""
           },
           ""RelyingParty"": ""http://auth.xboxlive.com"",
           ""TokenType"": ""JWT""
        }";
            return ModNet.NetRequestByClientMultiple("https://user.auth.xboxlive.com/user/authenticate", HttpMethod.Post, Content: Request, ContentType: "application/json", RequireJson: true).DeserializeJson()["Token"].ToString();
        }
        // 微软登录步骤 3：从 XBLToken 获取 {XSTSToken, UHS}
        private static (string XSTSToken, string UHS) MsLoginStep3(string XBLToken)
        {
            McLaunchLog("开始微软登录步骤 3/6");

            string Request = @"{
           ""Properties"": {
               ""SandboxId"": ""RETAIL"",
               ""UserTokens"": [""" + XBLToken + @"""]
           },
           ""RelyingParty"": ""rp://api.minecraftservices.com/"",
           ""TokenType"": ""JWT""
        }";
            string Result;
            try
            {
                Result = ModNet.NetRequestByClientMultiple("https://xsts.auth.xboxlive.com/xsts/authorize", HttpMethod.Post, Content: Request, ContentType: "application/json", RequireJson: true);
            }
            catch (HttpRequestCodeException ex)
            {
                // 参考 https://github.com/PrismarineJS/prismarine-auth/blob/master/src/common/Constants.js
                if (ex.Response.Contains("2148916227"))
                {
                    ModMain.MyMsgBox("该账号似乎已被微软封禁，无法登录。", "登录失败", "我知道了", IsWarn: true);
                    throw new OperationCanceledException();
                }
                else if (ex.Response.Contains("2148916233"))
                {
                    if (ModMain.MyMsgBox("你尚未注册 Xbox 账户，请在注册后再登录。", "登录提示", "注册", "取消") == 1)
                    {
                        ModBase.OpenWebsite("https://signup.live.com/signup");
                    }
                    throw new OperationCanceledException();
                }
                else if (ex.Response.Contains("2148916235"))
                {
                    ModMain.MyMsgBox($"你的网络所在的国家或地区无法登录微软账号。{"\r\n"}请使用加速器或 VPN，然后再试。", "登录失败", "我知道了");
                    throw new OperationCanceledException();
                }
                else if (ex.Response.Contains("2148916238"))
                {
                    if (ModMain.MyMsgBox("该账号年龄不足，你需要先修改出生日期，然后才能登录。" + "\r\n" + "该账号目前填写的年龄是否在 13 岁以上？", "登录提示", "13 岁以上", "12 岁以下", "我不知道") == 1)
                    {
                        ModBase.OpenWebsite("https://account.live.com/editprof.aspx");
                        ModMain.MyMsgBox("请在打开的网页中修改账号的出生日期（至少改为 18 岁以上）。" + "\r\n" + "在修改成功后等待一分钟，然后再回到 PCL Next，就可以正常登录了！", "登录提示");
                    }
                    else
                    {
                        ModBase.OpenWebsite("https://support.microsoft.com/zh-cn/account-billing/如何更改-microsoft-帐户上的出生日期-837badbc-999e-54d2-2617-d19206b9540a");
                        ModMain.MyMsgBox("请根据打开的网页的说明，修改账号的出生日期（至少改为 18 岁以上）。" + "\r\n" + "在修改成功后等待一分钟，然后再回到 PCL Next，就可以正常登录了！", "登录提示");
                    }
                    throw new OperationCanceledException();
                }
                else
                {
                    throw;
                }
            }

            JObject ResultJson = (JObject)Result.DeserializeJson();
            return (ResultJson["Token"].ToString(), ResultJson["DisplayClaims"]["xui"][0]["uhs"].ToString()); // XSTSToken
                                                                                                              // UHS
        }
        // 微软登录步骤 4：从 {XSTSToken, UHS} 获取 {Minecraft AccessToken, 过期于 (Unix 时间戳)}
        private static (string AccessToken, int ExpiresAt) MsLoginStep4((string XSTSToken, string UHS) Tokens)
        {
            McLaunchLog("开始微软登录步骤 4/6");

            string Request = new JObject(new JProperty("identityToken", $"XBL3.0 x={Tokens.UHS};{Tokens.XSTSToken}")).ToString(0);
            string Result;
            try
            {
                Result = ModNet.NetRequestByClientRetry("https://api.minecraftservices.com/authentication/login_with_xbox", HttpMethod.Post, Content: Request, ContentType: "application/json", RequireJson: true);
            }
            catch (HttpRequestCodeException ex)
            {
                if ((int)ex.StatusCode == 429)
                {
                    Logger.Warn(ex, "微软登录第 4 步汇报 429");
                    throw new Exception("$登录尝试太过频繁，请等待几分钟后再试！");
                }
                else if (ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    Logger.Warn(ex, "微软登录第 4 步汇报 403");
                    throw new Exception("$当前 IP 的登录尝试异常。" + "\r\n" + "如果你使用了 VPN 或加速器，请把它们关掉或更换节点后再试！");
                }
                else if (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    Logger.Warn(ex, "微软登录第 4 步汇报 503");
                    throw new Exception("$Mojang 服务器出现问题，请稍后再试。" + "\r\n" + "你的网络是正常的，PCL Next 也是正常的，是 Mojang 出问题了……");
                }
                else if (ex.Response?.Contains("ACCOUNT_SUSPENDED") == true) // #8655
                {
                    ModMain.MyMsgBox("该账号似乎已被微软封禁，无法登录。", "登录失败", "我知道了", IsWarn: true);
                    throw new OperationCanceledException();
                }
                else
                {
                    throw;
                }
            }

            JObject ResultJson = (JObject)Result.DeserializeJson();
            return ((string AccessToken, int ExpiresAt))(ResultJson["access_token"].ToString(), ResultJson["expires_in"].ToObject<int>() + ModBase.GetUnixTimestampUtc() - 3600L); // 提前 1 小时视作过期
        }
        // 微软登录步骤 5：验证微软账号是否持有 MC，这也会刷新 XGP
        private static void MsLoginStep5(string AccessToken)
        {
            McLaunchLog("开始微软登录步骤 5/6");

            string Result = ModNet.NetRequestByClientMultiple("https://api.minecraftservices.com/entitlements/mcstore", ContentType: "application/json", ThreadCount: 2, Headers: new[,] { { "Authorization", "Bearer " + AccessToken } }, RequireJson: true);
            try
            {
                JObject ResultJson = (JObject)Result.DeserializeJson();
                if (!(ResultJson.ContainsKey("items") && ResultJson["items"].Any()))
                {
                    switch (ModMain.MyMsgBox("你尚未购买正版 Minecraft，或者 Xbox Game Pass 已到期。", "登录失败", "购买 Minecraft", "取消"))
                    {
                        case 1:
                            {
                                ModBase.OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj");
                                break;
                            }
                    }
                    throw new OperationCanceledException();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"微软登录第 5 步异常：{Result}");
                throw;
            }
        }
        // 微软登录步骤 6：从 Minecraft AccessToken 获取 {UUID, UserName, ProfileJson}
        private static (string UUID, string UserName, string ProfileJson) MsLoginStep6(string AccessToken)
        {
            McLaunchLog("开始微软登录步骤 6/6");

            string Result;
            try
            {
                Result = ModNet.NetRequestByClientMultiple("https://api.minecraftservices.com/minecraft/profile", ContentType: "application/json", ThreadCount: 2, Headers: new[,] { { "Authorization", "Bearer " + AccessToken } }, RequireJson: true);
            }
            catch (HttpRequestCodeException ex)
            {
                switch (ex.StatusCode)
                {
                    case (HttpStatusCode)429:
                        {
                            Logger.Warn(ex, "微软登录第 6 步汇报 429");
                            throw new Exception("$登录尝试太过频繁，请等待几分钟后再试！");
                        }
                    case HttpStatusCode.NotFound:
                        {
                            Logger.Warn(ex, "微软登录第 6 步汇报 404");
                            ModBase.RunInNewThread(() => { switch (ModMain.MyMsgBox("请先创建 Minecraft 玩家档案，然后再重新登录。", "登录失败", "创建档案", "取消")) { case 1: { ModBase.OpenWebsite("https://www.minecraft.net/zh-hans/msaprofile/mygames/editprofile"); break; } } }, "Login Failed: Create Profile");
                            throw new OperationCanceledException();
                        }

                    default:
                        {
                            throw;
                        }
                }
            }
            JObject ResultJson = (JObject)Result.DeserializeJson();
            string UUID = ResultJson["id"].ToString();
            string UserName = ResultJson["name"].ToString();
            return (UUID, UserName, Result);
        }

        // 返回符合离线皮肤设置的 UUID
        private static string McLoginLegacyUuidWithCustomSkin(string UserName, int SkinType, string SkinName)
        {
            string Uuid = Conversions.ToString(McLoginLegacyUuid(UserName));
            // 根据离线皮肤获取实际使用的 Uuid
            switch (SkinType)
            {
                case 0:
                    {
                        break;
                    }
                // 默认，不需要处理
                case 1:
                    {
                        // Steve
                        while (ModMinecraft.McSkinSex(Uuid) != "Steve")
                        {
                            if (Uuid.EndsWithF("FFFFF"))
                                Uuid = Uuid.Substring(0, 27) + "00000";
                            Uuid = Uuid.Substring(0, 27) + (long.Parse(Uuid.Substring(27), System.Globalization.NumberStyles.AllowHexSpecifier) + 1L).ToString("X").PadLeft(5, '0');
                        }

                        break;
                    }
                case 2:
                    {
                        // Alex
                        while (ModMinecraft.McSkinSex(Uuid) != "Alex")
                        {
                            if (Uuid.EndsWithF("FFFFF"))
                                Uuid = Uuid.Substring(0, 27) + "00000";
                            Uuid = Uuid.Substring(0, 27) + (long.Parse(Uuid.Substring(27), System.Globalization.NumberStyles.AllowHexSpecifier) + 1L).ToString("X").PadLeft(5, '0');
                        }

                        break;
                    }
                case 3:
                    {
                        // 使用正版用户名
                        try
                        {
                            if (!string.IsNullOrEmpty(SkinName) && ModMinecraft.McInstanceSelected is not null && ModMinecraft.McInstanceSelected.Version.Vanilla.Major < 20) // 1.20+ 或快照版不能使用该项（#3746）
                            {
                                Logger.Info($"由于离线皮肤设置，使用正版 UUID：{SkinName}");
                                Uuid = Conversions.ToString(McLoginMojangUuid(SkinName, false));
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "皮肤信息获取失败，游戏将以无皮肤的方式启动", LogBehavior.Toast);
                        }

                        break;
                    }
                case 4:
                    {
                        // 自定义
                        while ((ModMinecraft.McSkinSex(Uuid) ?? "") != ((Settings.Get<bool>("LaunchSkinSlim") ? "Alex" : "Steve") ?? ""))
                        {
                            if (Uuid.EndsWithF("FFFFF"))
                                Uuid = Uuid.Substring(0, 27) + "00000";
                            Uuid = Uuid.Substring(0, 27) + (long.Parse(Uuid.Substring(27), System.Globalization.NumberStyles.AllowHexSpecifier) + 1L).ToString("X").PadLeft(5, '0');
                        }

                        break;
                    }
            }
            return Uuid;
        }
        // 根据用户名返回对应 UUID，需要多线程
        public static object McLoginMojangUuid(string Name, bool ThrowOnNotFound)
        {
            if (Name.Trim().Length == 0)
                return new string('0', 32);
            // 从缓存获取
            string Uuid = ModBase.ReadIni(ModBase.PathTemp + @"Cache\Uuid\Mojang.ini", Name, "");
            if (Strings.Len(Uuid) == 32)
                return Uuid;
            // 从官网获取
            try
            {
                JObject GotJson = (JObject)ModNet.NetRequestByClientRetry("https://api.mojang.com/users/profiles/minecraft/" + Name, RequireJson: true).DeserializeJson();
                if (GotJson is null)
                    throw new FileNotFoundException("正版玩家档案不存在（" + Name + "）");
                Uuid = (string)(GotJson["id"] ?? "");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"从官网获取正版 Uuid 失败（{Name}）");
                if (!ThrowOnNotFound && ex.GetType().Name == "FileNotFoundException")
                {
                    Uuid = Conversions.ToString(McLoginLegacyUuid(Name)); // 玩家档案不存在
                }
                else
                {
                    throw new Exception("从官网获取正版 Uuid 失败", ex);
                }
            }
            // 写入缓存
            if (!(Strings.Len(Uuid) == 32))
                throw new Exception("获取的正版 Uuid 长度不足（" + Uuid + "）");
            ModBase.WriteIni(ModBase.PathTemp + @"Cache\Uuid\Mojang.ini", Name, Uuid);
            return Uuid;
        }
        public static object McLoginLegacyUuid(string Name)
        {
            string FullUuid = Name.Length.ToString("X").EnsureLength('0', 16) + Name.GetStableHashCode().ToString("X").EnsureLength('0', 16);
            return FullUuid.Substring(0, 12) + "3" + FullUuid.Substring(13, 3) + "9" + FullUuid.Substring(17, 15);
        }

        #endregion

        #region Java 处理

        public static Java McLaunchJavaSelected = null;
        private static void McLaunchJava(ModLoader.LoaderTask<int, int> Task)
        {
            McLaunchJavaSelected = ModJava.SelectOrDownloadJava(ModMinecraft.McInstanceSelected, true, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider());
            if (Task.IsCanceled)
                return;
            if (McLaunchJavaSelected is null)
                throw new OperationCanceledException(); // SelectOrDownloadJava 已经会给予适当的提示
            if (ModBase.ModeDebug)
            {
                McLaunchLog($"预期 Java 版本范围：{ModJava.GetJavaRequirement(ModMinecraft.McInstanceSelected).Range}");
                McLaunchLog($"Java 版本设置类别：{Settings.Get<int>("VersionArgumentJavaV2", ModMinecraft.McInstanceSelected)}");
            }
            McLaunchLog("选择的 Java：" + McLaunchJavaSelected.ToString());
        }

        #endregion

        #region 启动参数
        private static string McLaunchArgument;
        private static Encoding McLaunchEncoding;

        // 主方法
        private static void McLaunchArgumentMain(ModLoader.LoaderTask<string, int> Loader)
        {
            // 获取基础参数
            var Arguments = new List<string>();
            McLaunchLog("参数获取开始");
            Arguments.AddRange(McLaunchArgumentsJVM()); // JVM 参数
            Arguments.Add((string)ModMinecraft.McInstanceSelected.JsonObject["mainClass"]); // mainClass
            Arguments.AddRange(McLaunchArgumentsGame()); // 游戏参数
                                                         // 替换
            McLaunchLog("=================== 启动参数开始 ===================");
            var Replacements = McLaunchArgumentsReplace(Arguments).ToList();
            string Argument = "";
            foreach (var Arg_Iter in Arguments)
            {
                var Arg = Arg_Iter;
                // 应用替换标记
                foreach (var Replacement in Replacements)
                    Arg = Arg.Replace(Replacement.Key, Replacement.Value);
                // 为带特殊字符的参数添加双引号
                if (Arg.IndexOfAny(new[] { '&', '|', '<', '>', '^', ' ' }) >= 0 && !Arg.StartsWithF("\"") && !Arg.EndsWithF("\""))
                {
                    Arg = $"\"{Arg.Replace("\"", @"\""")}\""; // 转义在原本参数中间的双引号
                }
                // 输出
                McLaunchLog(Arg);
                Argument += Arg + " ";
            }
            McLaunchLog("=================== 启动参数结束 ===================");
            // 输出
            McLaunchArgument = Argument.TrimEnd();
        }

        // JVM 部分
        private static List<string> McLaunchArgumentsJVM()
        {
            var Args = new List<string>();

            // Minecraft 参数
            if (ModMinecraft.McInstanceSelected.JsonObject["arguments"] is not null && ModMinecraft.McInstanceSelected.JsonObject["arguments"]["jvm"] is not null)
            {
                // 新版 MC：从版本 JSON 获取参数
                var CurrentInstance = ModMinecraft.McInstanceSelected;
            NextInstance:
                ;

                McLaunchLog($"新版 JVM 参数：从版本 JSON 获取（{CurrentInstance.Name}）");
                if (CurrentInstance.JsonObject["arguments"] is not null && CurrentInstance.JsonObject["arguments"]["jvm"] is not null)
                {
                    foreach (JToken SubJson in CurrentInstance.JsonObject["arguments"]["jvm"])
                    {
                        switch (SubJson.Type)
                        {
                            case JTokenType.String:
                                {
                                    Args.Add(SubJson.ToString().Trim());
                                    break;
                                }
                            case JTokenType.Object:
                                {
                                    JObject Argument = (JObject)SubJson;
                                    if (Argument.ContainsKey("rules") && !ModMinecraft.McJsonRuleCheck(Argument["rules"]))
                                        continue; // 不满足准则
                                    if (!Argument.ContainsKey("value"))
                                        continue; // 没有 value 字段
                                    if (Argument["value"].Type == JTokenType.String)
                                    {
                                        Args.Add(Argument["value"].ToString().Trim());
                                    }
                                    else
                                    {
                                        foreach (JToken value in Argument["value"])
                                            Args.Add(value.ToString().Trim());
                                    }

                                    break;
                                }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(CurrentInstance.InheritName))
                {
                    CurrentInstance = new ModMinecraft.McInstance(CurrentInstance.InheritName);
                    goto NextInstance;
                }
                // 1.17.1 中有个 "-Dos.name=Windows 10"，不立即加双引号会导致 10 被隔开
                Args = Args.Select(a => a.Contains(" ") && !a.StartsWithF("\"") && !a.EndsWithF("\"") ? $"\"{a.Replace("\"", @"\""")}\"" : a).ToList();
            }
            else
            {
                // 旧版 MC：直接添加参数
                McLaunchLog($"旧版 JVM 参数");
                Args.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
                Args.Add("-Djava.library.path=${natives_directory}");
                Args.Add("-cp");
                Args.Add("${classpath}");
            }

            // 设置里自定义的参数
            string CustomArg = Settings.Get<string>("VersionAdvanceJvm", Instance: ModMinecraft.McInstanceSelected);
            if (string.IsNullOrEmpty(CustomArg))
                CustomArg = Settings.Get<string>("LaunchAdvanceJvm");
            if (!string.IsNullOrEmpty(CustomArg))
                CustomArg = ModMain.ArgumentReplace(CustomArg);

            // 重新分割字符串并去重
            Args = DeduplicateJavaArguments(SplitJavaArguments(Args.Join(' ').Replace("McEmu= ", "McEmu=") + " " + CustomArg).ToList(), true);

            // 统一通行证
            if (McLoginLoader.Output.Type == "Nide")
            {
                Args.Add(@"-javaagent:""${pure_directory}\nide8auth.jar""=""" + Settings.Get<string>("VersionServerNide", Instance: ModMinecraft.McInstanceSelected) + "\"");
            }
            // Authlib-Injector
            if (McLoginLoader.Output.Type == "Auth")
            {
                if (McLaunchJavaSelected.Version.Major >= 6)
                    Args.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT"); // 信任系统根证书（5252#）
                string Server = Settings.Get<string>("VersionServerAuthServer", Instance: ModMinecraft.McInstanceSelected);
                try
                {
                    McLaunchLog($"开始 Authlib Injector Prefetch");
                    string Response = ModNet.NetRequestByClientRetry(Server, Encoding: Encoding.UTF8);
                    Args.Add(@"-javaagent:""${pure_directory}\authlib-injector.jar""=""" + Server + "\"");
                    Args.Add("-Dauthlibinjector.side=client");
                    Args.Add("-Dauthlibinjector.yggdrasil.prefetched=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Response)));
                }
                catch (Exception ex)
                {
                    throw new Exception("无法连接到第三方登录服务器（" + (Server ?? null) + "）", ex);
                }
            }

            // JLW
            // 非 GBK 编码下命令行参数乱码的问题（JDK-8272352）
            bool UseJLW = !Settings.Get<bool>("LaunchAdvanceDisableJLW") && !Settings.Get<bool>("VersionAdvanceDisableJLW", ModMinecraft.McInstanceSelected) && !ModBase.IsGBKEncoding; // 即使游戏文件夹名称不含非 ASCII 字符，但玩家名等也可能出现中文，因此依然需要 JLW（#8739）
            if (UseJLW && CustomArg.Contains("-javaagent"))
            {
                UseJLW = false;
                McLaunchLog("由于使用了自定义 javaagent，已禁用 JLW，这可能导致游戏崩溃！", LogLevel.Warn);
            }
            if (UseJLW)
            {
                if (McLaunchJavaSelected.Version.Major >= 9)
                {
                    Args.Add("--add-exports");
                    Args.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
                }
                Args.Add("-Doolloo.jlw.tmpdir=${pure_directory}"); // 这里需要不以 \ 结尾
            }
            McLaunchLog("使用 JLW：" + UseJLW);

            // LUA
            bool UseLUA = !Settings.Get<bool>("LaunchAdvanceDisableLUA") && !Settings.Get<bool>("VersionAdvanceDisableLUA", ModMinecraft.McInstanceSelected) && ModMinecraft.McLibListGet(ModMinecraft.McInstanceSelected, false).Select(e => e.OriginalName).Contains("org.lwjgl:lwjgl:3.4.1");
            if (UseLUA)
                Args.Add($"-javaagent:\"{LaunchUtils.ExtractPatch(@"Launch\LwjglUnsafeAgent.jar", ModBase.PathPure.Value)}\"");
            McLaunchLog("使用 LUA：" + UseLUA);

            #region 内存管理

            McLaunchLog("当前剩余内存：" + Math.Round(My.Computer.Info.AvailablePhysicalMemory / 1024d / 1024d / 1024d * 10d) / 10d + "G");
            int TargetRam = (int)Math.Round(Math.Floor(PageInstanceSetup.GetRam(ModMinecraft.McInstanceSelected) * 1024d));
            Args.Add("-Xmx" + TargetRam + "m");

            // 获取设置的管理方式
            // 0：在 Java 21+ 使用分代 ZGC，20~15 使用非分代的 ZGC，14- 使用 G1GC
            // 1：在 Java 21+ 使用分代 ZGC，20- 使用 G1GC
            // 2：仅 G1GC
            // 3：不指定，玩家自定义
            int SetupType = Settings.Get<int>("LaunchAdvanceGC");
            if (ModMinecraft.McInstanceSelected is not null && Settings.Get<int>("VersionAdvanceGC", ModMinecraft.McInstanceSelected) > 0)
                SetupType = Settings.Get<int>("VersionAdvanceGC", ModMinecraft.McInstanceSelected) - 1; // 去掉默认
            if (SetupType != 3)
            {
                // 确定是使用 G1GC 还是 ZGC
                bool UseG1GC = false;
                if (SetupType == 0 && McLaunchJavaSelected.Version.Major < 15 || SetupType == 1 && McLaunchJavaSelected.Version.Major < 21 || SetupType == 2 || SetupType == 4)
                    UseG1GC = true; // 检查 Java 版本，ZGC 在 Java 15+ 可用，分代 ZGC 在 Java 21+ 可用
                if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 17763)
                    UseG1GC = true; // 检查系统兼容性，ZGC 需要 Windows 10 1809+
                McLaunchLog($"GC 设置：{SetupType}，选取 {(UseG1GC ? "G1GC" : "ZGC")}，Java 版本：{McLaunchJavaSelected.Version.Major}");
                // 移除已有的 GC 参数
                Args = Args.Where(a => !a.RegexCheck(@"( )*-XX:[+-]?(Use\w+GC|ZGenerational|UseCompactObjectHeaders|G1\w+Percent|G1\w+Size|(Max|Min)(GCPauseMillis|HeapFreeRatio))")).ToList();
                // 添加 GC 参数
                Args.Add("-XX:+UnlockExperimentalVMOptions");
                if (McLaunchJavaSelected.Version.Major >= 24)
                    Args.Add("-XX:+UseCompactObjectHeaders");
                if (UseG1GC && SetupType == 4)
                {
                    // 优化的 G1GC
                    Args.Add("-XX:+UseG1GC");
                    Args.Add("-XX:G1NewSizePercent=20");
                    Args.Add("-XX:G1ReservePercent=20");
                    Args.Add("-XX:G1HeapRegionSize=32M");
                    Args.Add("-XX:MaxGCPauseMillis=50");
                    if (!ModBase.ModeDebug)
                        Args.Add("-XX:+PerfDisableSharedMem");
                    if (McLaunchJavaSelected.Version.Major <= 7)
                        Args.Add("-XX:MaxPermSize=512m"); // #8286
                    if (McLaunchJavaSelected.Version.Major == 8)
                        Args.Add("-XX:+ParallelRefProcEnabled");
                    if (McLaunchJavaSelected.Version.Major >= 12)
                        Args.Add("-XX:MinHeapFreeRatio=25"); // 需要比 G1NewSizePercent 大一点
                    if (McLaunchJavaSelected.Version.Major >= 12)
                        Args.Add("-XX:MaxHeapFreeRatio=40"); // 需要比 MinHeapFreeRatio 大一些
                }
                else if (UseG1GC)
                {
                    // Mojang G1GC
                    Args.Add("-XX:+UseG1GC");
                    Args.Add("-XX:G1NewSizePercent=20");
                    Args.Add("-XX:G1ReservePercent=20");
                    Args.Add("-XX:G1HeapRegionSize=32M");
                    Args.Add("-XX:MaxGCPauseMillis=50");
                    if (McLaunchJavaSelected.Version.Major <= 7)
                        Args.Add("-XX:MaxPermSize=512m"); // #8286
                }
                else
                {
                    // ZGC
                    Args.Add("-XX:+UseZGC");
                    if (McLaunchJavaSelected.Version.Major == 21 || McLaunchJavaSelected.Version.Major == 22)
                        Args.Add("-XX:+ZGenerational");
                } // Java 23 起默认启用分代 ZGC，不需要再添加参数
            }

            Args.Remove("-XX:MaxDirectMemorySize=256M"); // #3511 的清理

            #endregion

            // Log4j 漏洞防御参数
            Args.Add("-Dlog4j2.formatMsgNoLookups=true");

            // 编码（#4700、#5892、#5909、#6934、#8950、#9241）
            bool UseCompatEncoding = McLaunchJavaSelected.Version.Major >= 18;
            string EncodingName = UseCompatEncoding ? McLaunchJavaSelected.NativeEncodingName : McLaunchJavaSelected.FileEncodingName; // Java 18+ 会添加 COMPAT，因此 Java 会回退到 native.encoding
                                                                                                                                       // Java 17- 直接使用 file.encoding
            try
            {
                McLaunchEncoding = Encoding.GetEncoding(EncodingName.Equals("GBK", StringComparison.OrdinalIgnoreCase) ? "GB18030" : EncodingName);
            }
            catch (Exception) // 未引用异常对象，省略变量名（消除 CS0168）
            {
                var FallbackEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage); // 老版本 Java 的编码选择行为
                McLaunchLog($"无法识别 Java 编码 {EncodingName ?? "Nothing"}，已回退为 {FallbackEncoding.WebName}", LogLevel.Warn);
                McLaunchEncoding = FallbackEncoding;
            }
            McLaunchLog($"使用编码：{McLaunchEncoding.WebName}");
            string LaunchEncodingName = McLaunchEncoding.CodePage == 54936 ? "GBK" : McLaunchEncoding.WebName;
            string StdoutArgName = McLaunchJavaSelected.Version.Major < 19 ? "sun.stdout.encoding" : "stdout.encoding"; // 老版本 Java 应该设置 sun.std
            string StderrArgName = McLaunchJavaSelected.Version.Major < 19 ? "sun.stderr.encoding" : "stderr.encoding";
            if (!Args.Any(a => a.StartsWithF($"-D{StdoutArgName}=")))
                Args.Add($"-D{StdoutArgName}={LaunchEncodingName}");
            if (!Args.Any(a => a.StartsWithF($"-D{StderrArgName}=")))
                Args.Add($"-D{StderrArgName}={LaunchEncodingName}");
            if (UseCompatEncoding && !Args.Any(a => a.StartsWithF("-Dfile.encoding=")))
                Args.Add("-Dfile.encoding=COMPAT"); // 防止老版本 MC 与部分 Mod 不兼容 UTF-8

            // 为 JLW 添加 -jar，它必须放在最后
            if (UseJLW)
            {
                Args.Add("-jar");
                Args.Add(LaunchUtils.ExtractPatch(@"Launch\JavaWrapper.jar", ModBase.PathPure.Value));
            }

            // 再次去重并输出
            return DeduplicateJavaArguments(Args, true);
        }
        // 游戏参数部分
        private static List<string> McLaunchArgumentsGame()
        {
            string Arg = "";

            // Minecraft 参数
            // 旧版 MC：直接添加参数
            if (ModMinecraft.McInstanceSelected.JsonObject["minecraftArguments"]?.ToString().Any() == true)
            {
                string BasicString = ModMinecraft.McInstanceSelected.JsonObject["minecraftArguments"].ToString();
                McLaunchLog($"旧版游戏参数：" + BasicString);
                BasicString += " --height ${resolution_height} --width ${resolution_width}"; // 总是添加宽高选项，之后去重的时候会覆盖 MC 自带的
                Arg += " " + BasicString;
            }
            // 新版 MC：从版本 JSON 获取参数
            if (ModMinecraft.McInstanceSelected.JsonObject["arguments"] is not null && ModMinecraft.McInstanceSelected.JsonObject["arguments"]["game"] is not null)
            {
                var CurrentInstance = ModMinecraft.McInstanceSelected;
                void AppendArg(string a) => Arg += " " + (a.Contains(" ") && !a.StartsWithF("\"") && !a.EndsWithF("\"") ? $"\"{a.Replace("\"", @"\""")}\"" : a);
            NextInstance:
                ;

                McLaunchLog($"新版游戏参数：从版本 JSON 获取（{CurrentInstance.Name}）");
                if (CurrentInstance.JsonObject["arguments"] is not null && CurrentInstance.JsonObject["arguments"]["game"] is not null)
                {
                    foreach (JToken SubJson in CurrentInstance.JsonObject["arguments"]["game"])
                    {
                        switch (SubJson.Type)
                        {
                            case JTokenType.String:
                                {
                                    AppendArg(SubJson.ToString());
                                    break;
                                }
                            case JTokenType.Object:
                                {
                                    JObject Argument = (JObject)SubJson;
                                    if (Argument.ContainsKey("rules") && !ModMinecraft.McJsonRuleCheck(Argument["rules"]))
                                        continue; // 不满足准则
                                    if (!Argument.ContainsKey("value"))
                                        continue; // 没有 value 字段
                                    if (Argument["value"].Type == JTokenType.String)
                                    {
                                        AppendArg(Argument["value"].ToString());
                                    }
                                    else
                                    {
                                        foreach (JToken value in Argument["value"])
                                            AppendArg(value.ToString());
                                    }

                                    break;
                                }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(CurrentInstance.InheritName))
                {
                    CurrentInstance = new ModMinecraft.McInstance(CurrentInstance.InheritName);
                    goto NextInstance;
                }
            }

            // 设置里自定义的参数
            string CustomArg = Settings.Get<string>("VersionAdvanceGame", Instance: ModMinecraft.McInstanceSelected);
            if (string.IsNullOrEmpty(CustomArg))
                CustomArg = Settings.Get<string>("LaunchAdvanceGame");
            if (!string.IsNullOrEmpty(CustomArg))
                CustomArg = ModMain.ArgumentReplace(CustomArg);
            Arg += " " + CustomArg;

            // 把 OptiFineForgeTweaker 放在参数的末尾
            if ((ModMinecraft.McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Forge) || ModMinecraft.McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) && ModMinecraft.McInstanceSelected.Version.HasOptiFine && !ModMinecraft.McInstanceSelected.Version.OptiFineAsMod)
            {
                if (Arg.Contains("--tweakClass optifine.OptiFineForgeTweaker"))
                {
                    McLaunchLog("发现正确的 OptiFineForge TweakClass，目前的游戏参数：" + Arg);
                    Arg = Arg.Replace(" --tweakClass optifine.OptiFineForgeTweaker", "") + " --tweakClass optifine.OptiFineForgeTweaker";
                }
                if (Arg.Contains("--tweakClass optifine.OptiFineTweaker"))
                {
                    McLaunchLog("发现错误的 OptiFineForge TweakClass，目前的游戏参数：" + Arg);
                    Arg = Arg.Replace(" --tweakClass optifine.OptiFineTweaker", "") + " --tweakClass optifine.OptiFineForgeTweaker";
                    try
                    {
                        FileUtils.Write(ModMinecraft.McInstanceSelected.GetJsonPath(), FileUtils.ReadAsString(ModMinecraft.McInstanceSelected.GetJsonPath()).Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"));
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "替换 OptiFineForge TweakClass 失败");
                    }
                }
            }

            // 重新分割字符串并去重
            var Args = DeduplicateJavaArguments(SplitJavaArguments(Arg).ToList(), false);

            // 额外传入的参数
            Args.AddRange(CurrentLaunchOptions.ExtraGameArgs);

            // 全屏
            if (Settings.Get<int>("LaunchArgumentWindowType") == 0)
                Args.Add("--fullscreen");

            // 自动进入服务器
            string Server = string.IsNullOrEmpty(CurrentLaunchOptions.ServerIp) ? Settings.Get<string>("VersionServerEnter", ModMinecraft.McInstanceSelected) : CurrentLaunchOptions.ServerIp;
            if (Server.Any())
            {
                if (ModMinecraft.McInstanceSelected.ReleaseTime > new DateTime(2023, 4, 4))
                {
                    // QuickPlay
                    Args.Add($"--quickPlayMultiplayer");
                    Args.Add(Server);
                }
                else
                {
                    // 老版本
                    if (Server.Contains(":"))
                    {
                        // 包含端口号
                        Args.Add("--server");
                        Args.Add(Server.Split(":")[0]);
                        Args.Add("--port");
                        Args.Add(Server.Split(":")[1]);
                    }
                    else
                    {
                        // 不包含端口号
                        Args.Add("--server");
                        Args.Add(Server);
                        Args.Add("--port");
                        Args.Add("25565");
                    }
                    if (ModMinecraft.McInstanceSelected.Version.HasOptiFine)
                        ModMain.Hint("OptiFine 与自动进入服务器可能不兼容，有概率导致材质丢失甚至游戏崩溃！", ModMain.HintType.Red);
                }
            }

            // 再次去重并输出
            return DeduplicateJavaArguments(Args, false);
        }
        // 获取替换标记
        private static IEnumerable<(string Key, string Value)> McLaunchArgumentsReplace(List<string> Arguments)
        {
            // 基础参数
            yield return ("${classpath_separator}", ";");
            yield return ("${natives_directory}", PathUtils.RemoveSlashSuffix(GetNativesFolder()));
            yield return ("${library_directory}", PathUtils.ToShortPath(ModMinecraft.McFolderSelected + "libraries"));
            yield return ("${libraries_directory}", PathUtils.ToShortPath(ModMinecraft.McFolderSelected + "libraries"));
            yield return ("${pure_directory}", PathUtils.RemoveSlashSuffix(ModBase.PathPure.Value)); // 由 PCL 添加，这会允许在分割参数并去重后再替换路径，防止路径中的特殊字符影响参数分割和去重
            yield return ("${launcher_name}", "PCL");
            yield return ("${launcher_version}", ModBase.VersionCode.ToString());
            yield return ("${version_name}", ModMinecraft.McInstanceSelected.Name);
            yield return ("${game_directory}", PathUtils.ToShortPath(ModMinecraft.McInstanceSelected.PathIndie[..^1]));
            yield return ("${assets_root}", PathUtils.ToShortPath(ModMinecraft.McFolderSelected + "assets"));
            yield return ("${user_properties}", "{}");
            yield return ("${auth_player_name}", McLoginLoader.Output.Name);
            yield return ("${auth_uuid}", McLoginLoader.Output.Uuid);
            yield return ("${auth_access_token}", McLoginLoader.Output.AccessToken);
            yield return ("${access_token}", McLoginLoader.Output.AccessToken);
            yield return ("${auth_session}", McLoginLoader.Output.AccessToken);
            yield return ("${user_type}", "msa"); // #1221
            yield return ("${primary_jar}", PathUtils.ToShortPath(ModMinecraft.McInstanceSelected.PathVersion + ModMinecraft.McInstanceSelected.Name + ".jar")); // #6942
            yield return ("${game_assets}", PathUtils.ToShortPath(ModMinecraft.McFolderSelected + @"assets\virtual\legacy")); // 1.5.2 的 pre-1.6 资源索引应与 legacy 合并
            yield return ("${assets_index_name}", ModMinecraft.McAssetsGetIndexName(ModMinecraft.McInstanceSelected));

            // 自定义信息
            string ArgumentInfo;
            ArgumentInfo = Settings.Get<string>("VersionArgumentInfo", Instance: ModMinecraft.McInstanceSelected);
            if (string.IsNullOrEmpty(ArgumentInfo))
                ArgumentInfo = Settings.Get<string>("LaunchArgumentInfo");
            if (string.IsNullOrEmpty(ArgumentInfo)) // 若自定义信息为空，则必须从参数中去掉该部分，不然 MC 会显示 ${version_type} 或者一个空字符串
            {
                if (Arguments.Contains("--versionType"))
                {
                    int Index = Arguments.IndexOf("--versionType");
                    Arguments.RemoveAt(Index);
                    if (Index < Arguments.Count)
                        Arguments.RemoveAt(Index);
                }
                yield return ("${version_type}", "\"\"");
            }
            else
            {
                yield return ("${version_type}", ModMain.ArgumentReplace(ArgumentInfo));
            }

            // 窗口尺寸
            Size TargetSize;
            switch (Settings.Get<int>("LaunchArgumentWindowType"))
            {
                case 2: // 与启动器尺寸一致
                    {
                        Size Result;
                        ModBase.RunInUiWait(() => Result = new Size(ModBase.GetPixelSize(ModMain.FrmMain.PanForm.ActualWidth), ModBase.GetPixelSize(ModMain.FrmMain.PanForm.ActualHeight)));
                        TargetSize = Result;
                        TargetSize.Height -= 29.5d * ModBase.DPI / 96d; // 标题栏高度
                        break;
                    }
                case 3: // 自定义
                    {
                        TargetSize = new Size(Math.Max(100, Settings.Get<int>("LaunchArgumentWindowWidth")), Math.Max(100, Settings.Get<int>("LaunchArgumentWindowHeight")));
                        break;
                    }

                default:
                    {
                        TargetSize = new Size(854d, 480d);
                        break;
                    }
            }
            if (ModMinecraft.McInstanceSelected.Version.Vanilla.Major <= 12 && McLaunchJavaSelected.Version >= new Version(8, 0, 200) && McLaunchJavaSelected.Version <= new Version(8, 0, 321) && !ModMinecraft.McInstanceSelected.Version.HasOptiFine && !ModMinecraft.McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Forge)) // 修复 1.12.2- 下 JRE 8u200~321 下窗口大小为设置大小的 DPI% 倍（#3463）
            {
                McLaunchLog($"已应用窗口大小过大修复（Java 版本：{McLaunchJavaSelected.Version}）");
                TargetSize.Width /= ModBase.DPI / 96d;
                TargetSize.Height /= ModBase.DPI / 96d;
            }
            yield return ("${resolution_width}", Math.Round(TargetSize.Width).ToString());
            yield return ("${resolution_height}", Math.Round(TargetSize.Height).ToString());

            // 支持库
            var Cps = new List<string>();
            string OptiFineCp = null;
            foreach (ModMinecraft.McLibEntry Library in ModMinecraft.McLibListGet(ModMinecraft.McInstanceSelected, true))
            {
                if (Library.IsNatives)
                    continue;
                if (Library.Name is not null && Library.Name == "optifine:OptiFine")
                {
                    OptiFineCp = Library.LocalPath;
                }
                else
                {
                    Cps.Add(Library.LocalPath);
                }
            }
            if (OptiFineCp is not null)
                Cps.Insert(Cps.Count - 2, OptiFineCp); // OptiFine 的总是需要放到倒数第二位
            yield return ("${classpath}", Enumerable.Select((IEnumerable<string>)Cps, c => PathUtils.ToShortPath(c)).Join(';'));
        }

        /// <summary>
    /// 分割 Java 参数字符串。
    /// </summary>
        private static IEnumerable<string> SplitJavaArguments(string Arg)
        {
            Arg = Arg.ReplaceLineEndings(" ", mergeMultiple: true).Trim(); // 预处理换行符
            if (string.IsNullOrWhiteSpace(Arg))
                yield break;
            var Current = new StringBuilder();
            bool InQuotes = false;
            int i = 0;
            while (i < Arg.Length)
            {
                char c = Arg[i];
                if (c == '\\' && i + 1 < Arg.Length && Arg[i + 1] == '"') // 检测 \"
                {
                    // 转义的双引号：保留反斜杠和引号，不切换 InQuotes
                    Current.Append(@"\""");
                    i += 1; // 额外跳过下一个字符（双引号）
                }
                else if (c == '"')
                {
                    // 未转义的双引号：切换 InQuotes
                    InQuotes = !InQuotes;
                    Current.Append(c);
                }
                else if (c == ' ' && !InQuotes)
                {
                    // 不在双引号内的空格：一个参数结束
                    if (Current.Length > 0)
                    {
                        yield return Current.ToString();
                        Current.Clear();
                    }
                }
                else
                {
                    // 普通字符，直接添加
                    Current.Append(c);
                }
                i += 1;
            }
            if (Current.Length > 0)
                yield return Current.ToString(); // 添加剩余的
        }
        /// <summary>
    /// 对 Java 参数进行去重。
    /// </summary>
        private static List<string> DeduplicateJavaArguments(List<string> Args, bool IsJVMArgs)
        {
            var Result = new List<string>();
            int i = 0;
            while (i < Args.Count)
            {
                string Key = Args[i];
                if (!Key.StartsWithF("-") || i + 1 >= Args.Count || Args[i + 1].StartsWithF("-") && ModBase.Val(Args[i + 1].AfterFirst("-")) == 0d) // 避免 -xPos 23 -xPos -50 中后面的 -50 被识别为参数
                {
                    // 单参数
                    i += 1;
                    if (Result.Contains(Key))
                    {
                        McLaunchLog($"已去重单个{(IsJVMArgs ? " JVM " : "游戏")}参数：{Key}");
                        continue;
                    }
                    Result.Add(Key);
                }
                else
                {
                    // 以空格间隔的键值对（例如 --width 233）
                    string Value = Args[i + 1];
                    i += 2;
                    bool continueWhile = false;
                    for (int j = 0, loopTo = Result.Count - 2; j <= loopTo; j++) // 向前寻找相同的键
                    {
                        if ((Result[j] ?? "") != (Key ?? ""))
                            continue;
                        // 仅当是游戏参数且不是 tweakClass 时，用新的值覆盖旧的值
                        // 据测试，如果包含多个相同 --width 或 --uuid 等参数，两个都会直接失效，如果这发生在关键参数上会导致崩溃
                        if (!IsJVMArgs && Key != "--tweakClass")
                        {
                            McLaunchLog($"已覆盖重复的{(IsJVMArgs ? " JVM " : "游戏")}参数：{Key} {Result[j + 1]} → {Key} {Value}");
                            Result[j + 1] = Value;
                            continueWhile = true;
                            break;
                        }
                        // 键和值完全相同，直接抛弃
                        if ((Result[j + 1] ?? "") == (Value ?? ""))
                        {
                            McLaunchLog($"已去重{(IsJVMArgs ? " JVM " : "游戏")}参数对：{Key} {Value}");
                            continueWhile = true;
                            break;
                        }
                    }

                    if (continueWhile)
                    {
                        continue;
                    }
                    Result.Add(Key);
                    Result.Add(Value);
                }
            }
            return Result;
        }

        #endregion

        #region 解压 Natives

        private static void McLaunchNatives(ModLoader.LoaderTask<int, int> Loader)
        {

            // 创建文件夹
            string Target = GetNativesFolder();
            DirectoryUtils.Create(Target);

            // 解压文件
            McLaunchLog("正在解压 Natives 文件");
            var ExistFiles = new List<string>();
            foreach (ModMinecraft.McLibEntry Native in ModMinecraft.McLibListGet(ModMinecraft.McInstanceSelected, true))
            {
                if (!Native.IsNatives)
                    continue;
                ZipArchive Zip;
                try
                {
                    Zip = FileUtils.OpenZip(Native.LocalPath);
                }
                catch (InvalidDataException ex)
                {
                    Logger.Warn(ex, $"打开 Natives 文件失败（{Native.LocalPath}）");
                    FileUtils.Delete(Native.LocalPath);
                    throw new Exception("无法打开 Natives 文件（" + Native.LocalPath + "），该文件可能已损坏，请重新尝试启动游戏");
                }
                foreach (var Entry in Zip.Entries)
                {
                    string FileName = Entry.FullName;
                    if (PathUtils.GetExtension(FileName) == "dll")
                    {
                        // 实际解压文件的步骤
                        string FilePath = Target + FileName;
                        ExistFiles.Add(FilePath);
                        var OriginalFile = FileUtils.GetInfo(FilePath);
                        if (OriginalFile.Exists)
                        {
                            if (OriginalFile.Length == Entry.Length)
                            {
                                if (ModBase.ModeDebug)
                                    McLaunchLog("无需解压：" + FilePath);
                                continue;
                            }
                            // 删除原文件
                            try
                            {
                                FileUtils.Delete(FilePath);
                            }
                            catch (UnauthorizedAccessException ex)
                            {
                                McLaunchLog("删除原 dll 访问被拒绝，这通常代表有一个 MC 正在运行，跳过解压：" + FilePath);
                                McLaunchLog("实际的错误信息：" + ex.GetDisplay(true));
                                break;
                            }
                        }
                        // 解压新文件
                        FileUtils.Write(FilePath, Entry.Open());
                        McLaunchLog("已解压：" + FilePath);
                    }
                }
                if (Zip is not null)
                    Zip.Dispose();
            }

            // 删除多余文件
            foreach (string FileName in DirectoryUtils.EnumerateFiles(Target))
            {
                if (ExistFiles.Contains(FileName))
                    continue;
                try
                {
                    McLaunchLog("删除：" + FileName);
                    FileUtils.Delete(FileName);
                }
                catch (UnauthorizedAccessException ex)
                {
                    McLaunchLog("删除多余文件访问被拒绝，跳过删除步骤");
                    McLaunchLog("实际的错误信息：" + ex.GetDisplay(true));
                    return;
                }
            }

        }
        /// <summary>
    /// 获取 Natives 文件夹路径，以 \ 结尾。
    /// </summary>
        public static string GetNativesFolder(ModMinecraft.McInstance Instance = null)
        {
            if (Instance is null)
                Instance = ModMinecraft.McInstanceSelected;
            string Result = PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Instance.PathVersion)) + Instance.Name + @"-natives\";
            if (!(ModBase.IsGBKEncoding || Result.IsAsciiOnly()))
            {
                Result = Paths.AppData + @".minecraft\bin\natives\";
                if (!Result.IsAsciiOnly())
                {
                    Result = ModBase.OsDrive + @"ProgramData\PCL\natives\";
                }
            }
            DirectoryUtils.Create(Result); // 提前创建，这样 DirectoryUtils.Shorten 才有结果（否则在长路径下首次启动 Forge 会崩溃）
            return PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Result));
        }

        #endregion

        #region 启动与前后处理

        private static void McLaunchPrerun()
        {

            // 使用高性能显卡
            if (Settings.Get<bool>("LaunchAdvanceGraphicCard"))
            {
                try
                {
                    ModMain.SetGPUPreference(McLaunchJavaSelected.JavaExePath);
                    ModMain.SetGPUPreference(ModBase.PathExe);
                }
                catch (Exception ex)
                {
                    if (WindowsUtils.HasAdminRole())
                    {
                        Logger.Warn(ex, "直接调整显卡设置失败");
                    }
                    else
                    {
                        Logger.Warn(ex, "直接调整显卡设置失败，将以管理员权限重启 PCL Next 再次尝试");
                        try
                        {
                            if (ModBase.RunAsAdmin($"--gpu \"{McLaunchJavaSelected.JavaExePath}\"") == (int)ModBase.ProcessReturnValues.TaskDone)
                            {
                                McLaunchLog("以管理员权限重启 PCL Next 并调整显卡设置成功");
                            }
                            else
                            {
                                throw new Exception("调整过程中出现异常");
                            }
                        }
                        catch (Exception exx)
                        {
                            Logger.Error(exx, "调整显卡设置失败，Minecraft 可能会使用默认显卡运行", LogBehavior.Toast);
                        }
                    }
                }
            }

            // 更新 launcher_profiles.json
            do
            {
                try
                {
                    // 确保可用
                    if (!(McLoginLoader.Output.Type == "Microsoft"))
                        break;
                    ModMinecraft.McFolderLauncherProfilesJsonCreate(ModMinecraft.McFolderSelected);
                    // 构建需要替换的 Json 对象
                    string ReplaceJsonString = @"
            {
              ""authenticationDatabase"": {
                ""00000111112222233333444445555566"": {
                  ""username"": """ + McLoginLoader.Output.Name.Replace("\"", "-") + @""",
                  ""profiles"": {
                    ""66666555554444433333222221111100"": {
                        ""displayName"": """ + McLoginLoader.Output.Name + @"""
                    }
                  }
                }
              },
              ""clientToken"": """ + McLoginLoader.Output.ClientToken + @""",
              ""selectedUser"": {
                ""account"": ""00000111112222233333444445555566"", 
                ""profile"": ""66666555554444433333222221111100""
              }
            }";
                    JObject ReplaceJson = (JObject)ReplaceJsonString.DeserializeJson();
                    // 更新文件
                    JObject Profiles = (JObject)FileUtils.ReadAsJson(ModMinecraft.McFolderSelected + "launcher_profiles.json");
                    Profiles.Merge(ReplaceJson);
                    FileUtils.Write(ModMinecraft.McFolderSelected + "launcher_profiles.json", Profiles.ToString(), encoding: Encoding.GetEncoding("GB18030"));
                    McLaunchLog("已更新 launcher_profiles.json");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "更新 launcher_profiles.json 失败，将在删除文件后重试");
                    try
                    {
                        FileUtils.Delete(ModMinecraft.McFolderSelected + "launcher_profiles.json");
                        ModMinecraft.McFolderLauncherProfilesJsonCreate(ModMinecraft.McFolderSelected);
                        // 构建需要替换的 Json 对象
                        string ReplaceJsonString = @"
                    {
                      ""authenticationDatabase"": {
                        ""00000111112222233333444445555566"": {
                          ""username"": """ + McLoginLoader.Output.Name.Replace("\"", "-") + @""",
                          ""profiles"": {
                            ""66666555554444433333222221111100"": {
                                ""displayName"": """ + McLoginLoader.Output.Name + @"""
                            }
                          }
                        }
                      },
                      ""clientToken"": """ + McLoginLoader.Output.ClientToken + @""",
                      ""selectedUser"": {
                        ""account"": ""00000111112222233333444445555566"", 
                        ""profile"": ""66666555554444433333222221111100""
                      }
                    }";
                        JObject ReplaceJson = (JObject)ReplaceJsonString.DeserializeJson();
                        // 更新文件
                        JObject Profiles = (JObject)FileUtils.ReadAsJson(ModMinecraft.McFolderSelected + "launcher_profiles.json");
                        Profiles.Merge(ReplaceJson);
                        FileUtils.Write(ModMinecraft.McFolderSelected + "launcher_profiles.json", Profiles.ToString(), encoding: Encoding.GetEncoding("GB18030"));
                        McLaunchLog("已在删除后更新 launcher_profiles.json");
                    }
                    catch (Exception exx)
                    {
                        Logger.Error(exx, "更新 launcher_profiles.json 失败");
                    }
                }
            }
            while (false);

            // 更新 options.txt
            string SetupFileAddress = ModMinecraft.McInstanceSelected.PathIndie + "options.txt";
            if (!FileUtils.Exists(SetupFileAddress))
            {
                // Yosbr Mod 兼容（#2385）：https://www.curseforge.com/minecraft/mc-mods/yosbr
                string YosbrFileAddress = ModMinecraft.McInstanceSelected.PathIndie + @"config\yosbr\options.txt";
                if (FileUtils.Exists(YosbrFileAddress))
                {
                    McLaunchLog("将修改 Yosbr Mod 中的 options.txt");
                    SetupFileAddress = YosbrFileAddress;
                    ModBase.WriteIni(SetupFileAddress, "lang", "none"); // 忽略默认语言
                }
            }
            try
            {
                // 语言
                // 1.0-     ：没有语言选项
                // 1.1 ~ 5  ：zh_CN 时正常，zh_cn 时崩溃（最后两位字母必须大写，否则将会 NPE 崩溃）
                // 1.6 ~ 10 ：zh_CN 时正常，zh_cn 时自动切换为英文
                // 1.11 ~ 12：zh_cn 时正常，zh_CN 时虽然显示了中文但语言设置会错误地显示选择英文
                // 1.13+    ：zh_cn 时正常，zh_CN 时自动切换为英文
                string CurrentLang = ModBase.ReadIni(SetupFileAddress, "lang", "none");
                string RequiredLang = CurrentLang == "none" || !DirectoryUtils.Exists(ModMinecraft.McInstanceSelected.PathIndie + "saves") ? Settings.Get<bool>("ToolHelpChinese") ? "zh_cn" : "en_us" : CurrentLang.Lower(); // #3844，整合包可能已经自带了 options.txt
                if (!ModMinecraft.McInstanceSelected.Version.Valid || ModMinecraft.McInstanceSelected.Version.Vanilla.Major <= 10)
                {
                    // 将最后两位改为大写，前面的部分保留
                    RequiredLang = RequiredLang.Substring(0, RequiredLang.Length - 2) + RequiredLang.Substring(RequiredLang.Length - 2).Upper();
                }
                if ((CurrentLang ?? "") == (RequiredLang ?? ""))
                {
                    McLaunchLog($"需要的语言为 {RequiredLang}，当前语言为 {CurrentLang}，无需修改");
                }
                else
                {
                    ModBase.WriteIni(SetupFileAddress, "lang", "-"); // 触发缓存更改，避免删除后重新下载残留缓存
                    ModBase.WriteIni(SetupFileAddress, "lang", RequiredLang);
                    McLaunchLog($"已将语言从 {CurrentLang} 修改为 {RequiredLang}");
                }
                // '如果是初次设置，一并修改 forceUnicodeFont
                // If Settings.Get(Of Boolean)("ToolHelpChinese") AndAlso (CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves")) Then
                // WriteIni(SetupFileAddress, "forceUnicodeFont", "true")
                // McLaunchLog("已开启 forceUnicodeFont")
                // End If
                // 窗口
                switch (Settings.Get<int>("LaunchArgumentWindowType"))
                {
                    case 0: // 全屏
                        {
                            ModBase.WriteIni(SetupFileAddress, "fullscreen", "true");
                            break;
                        }
                    case 1: // 默认
                            // 其他
                        {
                            break;
                        }

                    default:
                        {
                            ModBase.WriteIni(SetupFileAddress, "fullscreen", "false");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "更新 options.txt 失败", LogBehavior.Toast);
            }

            // 离线皮肤 Alex 警告
            if (ModMinecraft.McInstanceSelected.Version.Vanilla.Major <= 7 && ModMinecraft.McInstanceSelected.Version.Vanilla.Major >= 2 && McLoginLoader.Input.Type == McLoginType.Legacy && (Settings.Get<int>("LaunchSkinType") == 2 || Settings.Get<int>("LaunchSkinType") == 4 && Settings.Get<bool>("LaunchSkinSlim"))) // 1.2 ~ 1.7
                                                                                                                                                                                                                                                                                                                              // 离线登录
                                                                                                                                                                                                                                                                                                                              // 强制 Alex
                                                                                                                                                                                                                                                                                                                              // 或选用 Alex 皮肤
            {
                ModMain.Hint("此 Minecraft 版本尚不支持 Alex 皮肤，你的皮肤可能会显示为 Steve！", ModMain.HintType.Red);
            }

            // 离线皮肤资源包
            try
            {
                string ZipFileAddress = ModMinecraft.McInstanceSelected.PathIndie + @"resourcepacks\PCL2 Skin.zip";
                bool NewTypeSetup = ModMinecraft.McInstanceSelected.Version.Vanilla.Major >= 13 || ModMinecraft.McInstanceSelected.Version.Vanilla.Major < 6;
                if (McLoginLoader.Input.Type == McLoginType.Legacy && Settings.Get<int>("LaunchSkinType") == 4 && FileUtils.Exists(Paths.AppDataThenName + "CustomSkin.png"))
                {
                    string MetaFileAddress = ModBase.PathTemp + "pack.mcmeta";
                    string PackPicAddress = ModBase.PathTemp + "pack.png";
                    if (ModMinecraft.McInstanceSelected.Version.Vanilla.Major < 6)
                    {
                        McLaunchLog("Minecraft 版本过老，尚不支持自定义离线皮肤");
                        goto IgnoreCustomSkin;
                    }
                    var PackVersionJToken = ModMinecraft.McInstanceSelected.JsonVersion?["pack_version"];
                    if (PackVersionJToken is JObject)
                        PackVersionJToken = ((JObject)PackVersionJToken)["resource"]; // 从 1.17 开始，可以从 pack_version.resource 获取资源包版本
                    int PackVersion = (int)Math.Round(ModBase.Val(PackVersionJToken?.ToString()));
                    if (PackVersion <= 0) // 没有对应字段的老版本 MC，或兜底
                    {
                        switch (ModMinecraft.McInstanceSelected.Version.Vanilla.Major)
                        {
                            case 6:
                            case 7:
                            case 8:
                                {
                                    PackVersion = 1;
                                    break;
                                }
                            case 9:
                            case 10:
                                {
                                    PackVersion = 2;
                                    break;
                                }
                            case 11:
                            case 12:
                                {
                                    PackVersion = 3;
                                    break;
                                }
                            case 13:
                            case 14:
                                {
                                    PackVersion = 4;
                                    break;
                                }
                            case 15:
                                {
                                    PackVersion = 5;
                                    break;
                                }
                            case 16:
                                {
                                    PackVersion = ModMinecraft.McInstanceSelected.Version.Vanilla.Build <= 1 ? 5 : 6;
                                    break;
                                }
                            case 17:
                                {
                                    PackVersion = 7;
                                    break;
                                }
                            case 18:
                                {
                                    PackVersion = 8; // 旧版快照可能没有可用的资源包格式声明
                                    break;
                                }

                            default:
                                {
                                    PackVersion = 17;
                                    break;
                                }
                        }
                    }
                    McLaunchLog($"正在构建自定义皮肤资源包，版本号 {PackVersion}，MC 版本 {ModMinecraft.McInstanceSelected.Version.Vanilla}");
                    // 准备文件
                    var Bit = new MyBitmap(ModBase.PathImage + "Heads/Logo.png");
                    Bit.Save(PackPicAddress);
                    FileUtils.Write(MetaFileAddress, "{\"pack\":{\"pack_format\":" + PackVersion + ",\"description\":\"PCL Next 自定义离线皮肤资源包\"}}");
                    var Skin = new MyBitmap(Paths.AppDataThenName + "CustomSkin.png");
                    if ((ModMinecraft.McInstanceSelected.Version.Vanilla.Major == 6 || ModMinecraft.McInstanceSelected.Version.Vanilla.Major == 7) && Skin.Pic.Height == 64)
                    {
                        McLaunchLog("该 Minecraft 版本不支持双层皮肤，已进行裁剪");
                        Skin = Skin.Clip(0, 0, 64, 32);
                    }
                    Skin.Save(Paths.Base + @"PCL\CustomSkin_Cliped.png");
                    // 构建压缩文件
                    var FilesToCompress = new Dictionary<string, string>() { { "pack.mcmeta", MetaFileAddress }, { "pack.png", PackPicAddress } };
                    if (ModMinecraft.McInstanceSelected.Version.Vanilla >= new Version(19, 0, 3))
                    {
                        // 1.19.3+ 使用复杂版本的替换
                        foreach (var SkinName in new[] { "alex", "ari", "efe", "kai", "makena", "noor", "steve", "sunny", "zuri" })
                            FilesToCompress.Add($"assets/minecraft/textures/entity/player/{(Settings.Get<bool>("LaunchSkinSlim") ? "slim" : "wide")}/{SkinName}.png", Paths.Base + @"PCL\CustomSkin_Cliped.png");
                    }
                    else
                    {
                        FilesToCompress.Add($"assets/minecraft/textures/entity/{(Settings.Get<bool>("LaunchSkinSlim") ? "alex.png" : "steve.png")}", Paths.Base + @"PCL\CustomSkin_Cliped.png");
                    }
                    FileUtils.CreateZipFromFiles(ZipFileAddress, FilesToCompress);
                    FileUtils.Delete(Paths.Base + @"PCL\CustomSkin_Cliped.png");
                    // 更改设置文件
                    ModBase.IniClearCache(SetupFileAddress);
                    string EnabledResourcePack = ModBase.ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart('[').TrimEnd(']');
                    if (NewTypeSetup)
                    {
                        if (string.IsNullOrEmpty(EnabledResourcePack))
                            EnabledResourcePack = "\"vanilla\"";
                        var EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(","));
                        var NewResourcePacks = new List<string>();
                        foreach (var Res in EnabledResourcePacks)
                        {
                            if (Res != "\"file/PCL2 Skin.zip\"" && !string.IsNullOrEmpty(Res))
                                NewResourcePacks.Add(Res);
                        }
                        NewResourcePacks.Add("\"file/PCL2 Skin.zip\"");
                        string Result = "[" + NewResourcePacks.Join(',') + "]";
                        ModBase.WriteIni(SetupFileAddress, "resourcePacks", Result);
                    }
                    else
                    {
                        var EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(","));
                        var NewResourcePacks = new List<string>();
                        foreach (var Res in EnabledResourcePacks)
                        {
                            if (Res != "\"PCL2 Skin.zip\"" && !string.IsNullOrEmpty(Res))
                                NewResourcePacks.Add(Res);
                        }
                        NewResourcePacks.Add("\"PCL2 Skin.zip\"");
                        string Result = "[" + NewResourcePacks.Join(',') + "]";
                        ModBase.WriteIni(SetupFileAddress, "resourcePacks", Result);
                    }

                IgnoreCustomSkin:
                    ;
                }

                else if (FileUtils.Exists(ZipFileAddress))
                {
                    McLaunchLog("正在清空自定义皮肤资源包");
                    // 删除压缩文件
                    FileUtils.Delete(ZipFileAddress);
                    // 更改设置文件
                    ModBase.IniClearCache(SetupFileAddress);
                    string EnabledResourcePack = ModBase.ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart('[').TrimEnd(']');
                    if (NewTypeSetup)
                    {
                        if (string.IsNullOrEmpty(EnabledResourcePack))
                            EnabledResourcePack = "\"vanilla\"";
                        var EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(","));
                        EnabledResourcePacks.Remove("\"file/PCL2 Skin.zip\"");
                        string Result = "[" + EnabledResourcePacks.Join(',') + "]";
                        ModBase.WriteIni(SetupFileAddress, "resourcePacks", Result);
                    }
                    else
                    {
                        var EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(","));
                        EnabledResourcePacks.Remove("\"PCL2 Skin.zip\"");
                        string Result = "[" + EnabledResourcePacks.Join(',') + "]";
                        ModBase.WriteIni(SetupFileAddress, "resourcePacks", Result);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "离线皮肤资源包设置失败", LogBehavior.Toast);
            }

        }
        private static void McLaunchCustom(ModLoader.LoaderTask<int, int> Loader)
        {

            // 获取自定义命令
            string CustomCommandGlobal = Settings.Get<string>("LaunchAdvanceRun");
            if (!string.IsNullOrEmpty(CustomCommandGlobal))
                CustomCommandGlobal = ModMain.ArgumentReplace(CustomCommandGlobal);
            string CustomCommandVersion = Settings.Get<string>("VersionAdvanceRun", Instance: ModMinecraft.McInstanceSelected);
            if (!string.IsNullOrEmpty(CustomCommandVersion))
                CustomCommandVersion = ModMain.ArgumentReplace(CustomCommandVersion);

            // 输出 bat
            try
            {
                string CmdString = $"chcp {(McLaunchEncoding.CodePage == 54936 && ModBase.IsGBKEncoding ? 936 : McLaunchEncoding.CodePage)}>nul" + "\r\n" + "@echo off" + "\r\n" + $"title 启动 - {ModMinecraft.McInstanceSelected.Name}" + "\r\n" + "echo 游戏正在启动，请稍候。" + "\r\n" + $"cd /D \"{PathUtils.ToShortPath(ModMinecraft.McInstanceSelected.PathIndie)}\"" + "\r\n" + CustomCommandGlobal + "\r\n" + CustomCommandVersion + "\r\n" + $"\"{McLaunchJavaSelected.JavaExePath}\" {McLaunchArgument}" + "\r\n" + "echo 游戏已退出。" + "\r\n" + "pause";
                FileUtils.Write(filePath: CurrentLaunchOptions.SaveBatch ?? Paths.Base + @"PCL\LatestLaunch.bat", text: ModMinecraft.FilterAccessToken(CmdString, 'F').Replace("%", "%%"), encoding: McLaunchEncoding);
                if (CurrentLaunchOptions.SaveBatch is not null)
                {
                    McLaunchLog("导出启动脚本完成，强制结束启动过程");
                    CanceledHint = "导出启动脚本成功！";
                    ModBase.OpenExplorer(CurrentLaunchOptions.SaveBatch);
                    Loader.Parent.Cancel();
                    return; // 导出脚本完成
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "输出启动脚本失败");
                if (CurrentLaunchOptions.SaveBatch is not null)
                    throw; // 直接触发启动失败（原 VB 为 Throw，改为 throw; 以保留堆栈，CA2200）
            }

            // 执行自定义命令
            if (!string.IsNullOrEmpty(CustomCommandGlobal))
            {
                McLaunchLog("正在执行全局自定义命令：" + CustomCommandGlobal);
                Process CustomProcess = null;
                try
                {
                    CustomProcess = ModBase.StartProcess(new ProcessStartInfo()
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{CustomCommandGlobal}\"",
                        WorkingDirectory = ModMinecraft.McFolderSelected,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (Settings.Get<bool>("LaunchAdvanceRunWait"))
                    {
                        while (!CustomProcess.HasExited && !Loader.IsCanceled)
                            Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "执行全局自定义命令失败", LogBehavior.Toast);
                }
                finally
                {
                    if (CustomProcess is not null && !CustomProcess.HasExited && Loader.IsCanceled)
                    {
                        McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程"); // #1183
                        CustomProcess.Kill();
                    }
                }
            }
            if (!string.IsNullOrEmpty(CustomCommandVersion))
            {
                McLaunchLog("正在执行版本自定义命令：" + CustomCommandVersion);
                Process CustomProcess = null;
                try
                {
                    CustomProcess = ModBase.StartProcess(new ProcessStartInfo()
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{CustomCommandVersion}\"",
                        WorkingDirectory = ModMinecraft.McFolderSelected,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (Settings.Get<bool>("VersionAdvanceRunWait", Instance: ModMinecraft.McInstanceSelected))
                    {
                        while (!CustomProcess.HasExited && !Loader.IsCanceled)
                            Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "执行版本自定义命令失败", LogBehavior.Toast);
                }
                finally
                {
                    if (CustomProcess is not null && !CustomProcess.HasExited && Loader.IsCanceled)
                    {
                        McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程"); // #1183
                        CustomProcess.Kill();
                    }
                }
            }

        }
        private static void McLaunchRun(ModLoader.LoaderTask<int, Process> Loader)
        {

            // 启动信息
            var StartInfo = new ProcessStartInfo(McLaunchJavaSelected.JavaExePath); // 使用 javaw.exe 会导致 #6263

            // 设置环境变量
            string PathEnv = StartInfo.EnvironmentVariables["Path"];
            var Paths = new List<string>();
            if (!string.IsNullOrEmpty(PathEnv))
                Paths.AddRange(PathEnv.Split(";"));
            Paths.Add(PathUtils.ToShortPath(McLaunchJavaSelected.Folder));
            StartInfo.EnvironmentVariables["Path"] = Paths.Distinct().Join(';');
            StartInfo.EnvironmentVariables["appdata"] = PathUtils.ToShortPath(ModMinecraft.McFolderSelected);

            // 设置其他参数
            StartInfo.WorkingDirectory = ModMinecraft.McInstanceSelected.PathIndie;
            StartInfo.UseShellExecute = false;
            StartInfo.StandardOutputEncoding = McLaunchEncoding;
            StartInfo.StandardErrorEncoding = McLaunchEncoding;
            StartInfo.RedirectStandardOutput = true;
            StartInfo.RedirectStandardError = true;
            StartInfo.CreateNoWindow = true;
            StartInfo.Arguments = McLaunchArgument;

            // 开始进程
            var GameProcess = ModBase.StartProcess(StartInfo);
            McLaunchLog("已启动游戏进程：" + McLaunchJavaSelected.JavaExePath);
            if (Loader.IsCanceled)
            {
                McLaunchLog("由于取消启动，已强制结束游戏进程"); // #1631
                GameProcess.Kill();
                return;
            }
            Loader.Output = GameProcess;
            McLaunchProcess = GameProcess;

            // 进程优先级处理
            try
            {
                GameProcess.PriorityBoostEnabled = true;
                switch (Settings.Get<int>("LaunchArgumentPriority"))
                {
                    case 0: // 高
                        {
                            GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
                            break;
                        }
                    case 2: // 低
                        {
                            GameProcess.PriorityClass = ProcessPriorityClass.BelowNormal; // 中
                            break;
                        }

                    default:
                        {
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "设置进程优先级失败");
            }

        }
        private static void McLaunchWait(ModLoader.LoaderTask<Process, int> Loader)
        {

            // 输出信息
            McLaunchLog("");
            McLaunchLog("~ 基础参数 ~");
            McLaunchLog("PCL Next 版本：" + ModBase.VersionDisplay + " (" + ModBase.VersionCode + ")");
            McLaunchLog($"游戏版本：{ModMinecraft.McInstanceSelected.VersionDisplayName()}（{ModMinecraft.McInstanceSelected.Version.Vanilla}，Drop {ModMinecraft.McInstanceSelected.Version.Drop}{(ModMinecraft.McInstanceSelected.Version.Reliable ? "" : "，无法完全确定")}）");
            McLaunchLog("资源版本：" + ModMinecraft.McAssetsGetIndexName(ModMinecraft.McInstanceSelected));
            McLaunchLog("版本继承：" + (string.IsNullOrEmpty(ModMinecraft.McInstanceSelected.InheritName) ? "无" : ModMinecraft.McInstanceSelected.InheritName));
            McLaunchLog("分配的内存：" + PageInstanceSetup.GetRam(ModMinecraft.McInstanceSelected) + " GB（" + Math.Round(PageInstanceSetup.GetRam(ModMinecraft.McInstanceSelected) * 1024d) + " MB）");
            McLaunchLog("MC 文件夹：" + ModMinecraft.McFolderSelected);
            McLaunchLog("版本文件夹：" + ModMinecraft.McInstanceSelected.PathVersion);
            McLaunchLog("版本隔离：" + ((ModMinecraft.McInstanceSelected.PathIndie ?? "") == (ModMinecraft.McInstanceSelected.PathVersion ?? "")));
            McLaunchLog("HMCL 格式：" + ModMinecraft.McInstanceSelected.IsHmclFormatJson);
            McLaunchLog("Java 信息：" + (McLaunchJavaSelected is not null ? McLaunchJavaSelected.ToString() : "无可用 Java"));
            McLaunchLog("Natives 文件夹：" + GetNativesFolder());
            McLaunchLog("");
            McLaunchLog("~ 登录参数 ~");
            McLaunchLog("玩家用户名：" + McLoginLoader.Output.Name);
            McLaunchLog("AccessToken：" + McLoginLoader.Output.AccessToken);
            McLaunchLog("ClientToken：" + McLoginLoader.Output.ClientToken);
            McLaunchLog("UUID：" + McLoginLoader.Output.Uuid);
            McLaunchLog("登录方式：" + McLoginLoader.Output.Type);
            McLaunchLog("");

            // 获取窗口标题
            string WindowTitle = Settings.Get<string>("VersionArgumentTitle", Instance: ModMinecraft.McInstanceSelected);
            if (string.IsNullOrEmpty(WindowTitle))
                WindowTitle = Settings.Get<string>("LaunchArgumentTitle");
            WindowTitle = ModMain.ArgumentReplace(WindowTitle, ReplaceTime: false);

            // 初始化等待
            var Watcher = new ModWatcher.Watcher(Loader, ModMinecraft.McInstanceSelected, WindowTitle);
            McLaunchWatcher = Watcher;

            // 等待
            while (Watcher.State == ModWatcher.Watcher.MinecraftState.Loading)
                Thread.Sleep(100);
            if (Watcher.State == ModWatcher.Watcher.MinecraftState.Crashed)
            {
                throw new OperationCanceledException();
            }

        }
        private static void McLaunchEnd()
        {
            McLaunchLog("开始启动结束处理");

            // 暂停或开始音乐播放
            if (Settings.Get<bool>("UiMusicStop"))
            {
                ModBase.RunInUi(() => { if (ModMusic.MusicPause()) Logger.Info("已根据设置，在启动后暂停音乐播放"); });
            }
            else if (Settings.Get<bool>("UiMusicStart"))
            {
                ModBase.RunInUi(() => { if (ModMusic.MusicResume()) Logger.Info("已根据设置，在启动后开始音乐播放"); });
            }

            // 启动器可见性
            McLaunchLog("启动器可见性：" + Settings.Get<int>("LaunchArgumentVisible"));
            switch (Settings.Get<int>("LaunchArgumentVisible"))
            {
                case 0:
                    {
                        // 直接关闭
                        McLaunchLog("已根据设置，在启动后关闭启动器");
                        ModBase.RunInUi(() => ModMain.FrmMain.EndProgram(false));
                        break;
                    }
                case 2:
                case 3:
                    {
                        // 隐藏
                        McLaunchLog("已根据设置，在启动后隐藏启动器");
                        ModBase.RunInUi(() => ModMain.FrmMain.Hidden = true);
                        break;
                    }
                case 4:
                    {
                        // 最小化
                        McLaunchLog("已根据设置，在启动后最小化启动器");
                        ModBase.RunInUi(() => ModMain.FrmMain.WindowState = WindowState.Minimized);
                        break;
                    }
                case 5:
                    {
                        break;
                    }
                    // 啥都不干
            }

            // 启动计数
            Settings.Set("SystemLaunchCount", Settings.Get<int>("SystemLaunchCount") + 1);

        }

        #endregion

    }
}