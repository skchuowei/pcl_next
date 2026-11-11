using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCL
{

    public partial class Application
    {
        public Application()
        {
            InitializeComponent();
            this.Startup += Application_Startup;
            this.SessionEnding += Application_SessionEnding;
            this.DispatcherUnhandledException += Application_DispatcherUnhandledException;
        }

        // 开始
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // 注册代码页编码（GBK / GB2312 / GB18030 等）
                // .NET Core 起运行时只自带 UTF-8 等少数编码，缺了这个提供程序，
                // StrConv(..., SimplifiedChinese)、Encoding.GetEncoding("GB18030")、
                // 以及写 launcher_profiles.json 都会抛 NotSupportedException
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                // 核心初始化
                MeloongCore.Main.Init("PCL");
                MeloongCore.Wpf.Main.Init();
                if (!(e.Args.Contains("--update") || e.Args.Contains("--gpu") || e.Args.Contains("--memory"))) // 若是通过命令行调用，则不检查单例、不轮换日志（#9137）
                {
                    WaitingMutex(e);
                    Logger.Instance = new PclLogger() { logFolder = Paths.BaseThenName, MinLevel = ModBase.ModeDebug ? LogLevel.Trace : LogLevel.Info };
                }
                // 提升主线程优先级
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                // 把内核（PCL.Download）的日志接进 Log1.txt
                // Phase 3/4 起，候选源竞速、资源站请求、加载器列表、本地反查都改在 C# 里打日志；
                // 此前宿主从未设置 sink，这些日志只写进 Console，用户反馈时附带的 Log1.txt 里一行都看不到
                try
                {
                    Download.DownloadLog.InfoSink = new Action<string>(message => Logger.Info(message));
                    Download.DownloadLog.WarnSink = new Action<string>(message => Logger.Warn(message));
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "接入 C# 内核日志失败");
                }
                // 注入 C# 侧资源站客户端（Phase 3/4）：资源搜索、指纹反查、版本枚举都经 ResourceApi.Default 联网
                try
                {
                    ResourceSourceGate.EnsureInitialized();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "初始化 C# 资源站客户端失败，资源联网将回退 VB 实现");
                }
                // 执行开发版测试
                if (ModBase.BuildType == ModBase.BuildTypes.Debug)
                {
                    try
                    {
                        ModDevelop.Start();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "开发者模式测试出错", LogBehavior.Alert);
                    }
                }
                // 检查 .NET Framework 版本
                try
                {
                    if (Conversions.ToInteger(RegistryUtils.TryRead(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 999999)) < 528040)
                        OldEnvironmentAssert();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "检查 .NET Framework 版本失败");
                }
                // 修复 WPF 字体加载（#3467）
                try
                {
                    var _unused = new FormattedText("", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Fonts.SystemTypefaces.First(), 96d, new ModBase.MyColor(), ModBase.DPI);
                }
                catch (UriFormatException) // 未引用异常对象，省略变量名（消除 CS0168）
                {
                    Environment.SetEnvironmentVariable("windir", Environment.GetEnvironmentVariable("SystemRoot"), EnvironmentVariableTarget.User);
                    var _unused = new FormattedText("", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Fonts.SystemTypefaces.First(), 96d, new ModBase.MyColor(), ModBase.DPI);
                }
                // 检查参数调用
                if (e.Args.Length > 0)
                {
                    if (e.Args[0] == "--gpu")
                    {
                        // 调整显卡设置
                        try
                        {
                            ModMain.SetGPUPreference(e.Args[1].Trim('"'));
                            Environment.Exit((int)ModBase.ProcessReturnValues.TaskDone);
                        }
                        catch (Exception) // 未引用异常对象，省略变量名（消除 CS0168）
                        {
                            Environment.Exit((int)ModBase.ProcessReturnValues.Fail);
                        }
                    }
                    else if (e.Args[0] == "--memory")
                    {
                        // 内存优化
                        ulong Ram = My.Computer.Info.AvailablePhysicalMemory;
                        try
                        {
                            PageOtherTest.MemoryOptimizeInternal(false);
                        }
                        catch (Exception ex)
                        {
                            Interaction.MsgBox(ex.Message, MsgBoxStyle.Critical, "内存优化失败");
                            Environment.Exit(-1);
                        }
                        if (My.Computer.Info.AvailablePhysicalMemory < Ram) // 避免 ULong 相减出现负数
                        {
                            Environment.Exit(0);
                        }
                        else
                        {
                            Environment.Exit((int)Math.Round((My.Computer.Info.AvailablePhysicalMemory - Ram) / 1024d));
                        } // 返回清理的内存量（K）
                    }
                }
                // 初始化文件结构
                try
                {
                    DirectoryUtils.Create(Paths.Base + @"PCL\Pictures\");
                    DirectoryUtils.Create(Paths.Base + @"PCL\Musics\");
                    ModBase.CheckPermissionWithException(Paths.Base + @"PCL\");
                }
                catch (Exception) // 未引用异常对象，省略变量名（消除 CS0168）
                {
                    Interaction.MsgBox($@"PCL Next 没有对当前文件夹的权限（{Paths.Base}PCL\），请尝试：" + Constants.vbCrLf + "1. 将 PCL Next 移动到其他文件夹" + (PathUtils.GetDiskName(Paths.Base) is { } arg1 && arg1 == 'C' ? "，例如 C 盘和桌面以外的其他位置。" : "。") + Constants.vbCrLf + "2. 删除当前目录中的 PCL 文件夹，然后再试。" + Constants.vbCrLf + "3. 右键 PCL Next 选择属性，打开 兼容性 中的 以管理员身份运行此程序。", MsgBoxStyle.Critical, "运行环境错误");
                    Environment.Exit((int)ModBase.ProcessReturnValues.Cancel);
                }

            RetryCacheCheck:
                ;

                try
                {
                    DirectoryUtils.Create(ModBase.PathTemp);
                    ModBase.CheckPermissionWithException(ModBase.PathTemp);
                }
                catch (Exception ex)
                {
                    if (PathUtils.IsSame(ModBase.PathTemp, Path.GetTempPath() + @"PCL\"))
                    {
                        ModMain.MyMsgBox("PCL Next 无法访问缓存文件夹，可能导致程序出错或无法正常使用！" + Constants.vbCrLf + Constants.vbCrLf + "错误原因：" + ex.GetDisplay(true), "缓存文件夹不可用");
                    }
                    else
                    {
                        ModMain.MyMsgBox("手动设置的缓存文件夹不可用，PCL Next 将使用默认缓存文件夹。" + Constants.vbCrLf + Constants.vbCrLf + "错误原因：" + ex.GetDisplay(true), "缓存文件夹不可用");
                        Settings.Set("SystemSystemCache", "");
                        ModBase.PathTemp = Path.GetTempPath() + @"PCL\";
                        goto RetryCacheCheck;
                    }
                }
                DirectoryUtils.Create(ModBase.PathTemp + @"Cache\");
                DirectoryUtils.Create(Paths.AppDataThenName);
                // 设置 ToolTipService 默认值
                ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(100));
                // 设置网络配置默认值
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 10000;
                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.EnableDnsRoundRobin = true;
                ServicePointManager.ReusePort = true;
                if (Environment.OSVersion.Version < new Version(6, 2)) // Windows 7 回退（#8519）
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                }
                // 设置初始窗口
                if (Settings.Get<bool>("UiLauncherLogo"))
                {
                    ModMain.FrmStart = new SplashScreen(@"Images\icon.ico");
                    ModMain.FrmStart.Show(false, true);
                }
                // 基础信息
                Logger.Info($"程序版本：{ModBase.VersionDisplay} ({ModBase.VersionCode}，渠道 {ModBuildChannel.ChannelCode}，{ModBuildInfo.Display})");
                if (ModBuildChannel.ChannelIsInternal)
                {
                    Logger.Info($"识别码：{ModBase.Identify}{(ModSecret.ThemeCheckOne(9) ? "，已解锁反馈主题" : "，未解锁反馈主题")}");
                }
                else
                {
                    Logger.Info($"识别码：{ModBase.Identify}");
                }
                Logger.Info($"程序路径：{ModBase.PathExe}");
                Logger.Info($"系统编码：{Encoding.Default.HeaderName} ({Encoding.Default.CodePage}, GBK={ModBase.IsGBKEncoding})");
                Logger.Info($"管理员权限：{WindowsUtils.HasAdminRole()}");
                // 检测异常环境
                if (Paths.Base.Contains(Path.GetTempPath()) || Paths.Base.Contains(@"AppData\Local\Temp\"))
                {
                    ModMain.MyMsgBox("请将 PCL Next 从压缩包中解压后再使用！" + Constants.vbCrLf + "如果不会解压，可以在网上寻找教程。", "需要解压！", "我知道了", IsWarn: true, ForceWait: true);
                    FormMain.EndProgramForce(ModBase.ProcessReturnValues.Cancel);
                }
                if (!Environment.Is64BitOperatingSystem)
                {
                    ModMain.MyMsgBox("PCL Next 和新版 Minecraft 均不再支持 32 位系统，请重装为 64 位系统后再进行游戏！", "环境警告", "我知道了", IsWarn: true, ForceWait: true);
                    FormMain.EndProgramForce(ModBase.ProcessReturnValues.Cancel);
                }
                // 计时
                Logger.Info($"第一阶段加载用时：{ModBase.GetTimeMs() - ModBase.ApplicationStartTick} ms");
                ModBase.ApplicationStartTick = ModBase.GetTimeMs();
                ModAnimation.AniControlEnabled += 1;
            }
            catch (Exception ex)
            {
                string FilePath = null;
                try
                {
                    FilePath = ModBase.PathExe;
                }
                catch
                {
                }
                Interaction.MsgBox(ex.GetDisplay(true) + Constants.vbCrLf + "PCL Next 所在路径：" + (string.IsNullOrEmpty(FilePath) ? "获取失败" : FilePath), MsgBoxStyle.Critical, "PCL Next 初始化错误");
                FormMain.EndProgramForce(ModBase.ProcessReturnValues.Exception);
            }
        }
        /// <summary>
    /// 要求程序以单例运行。
    /// 如果已在运行，则将其窗口拖出来并播放提示音，然后结束当前进程。
    /// </summary>
        private static void WaitingMutex(StartupEventArgs e)
        {
            // ModBase.BuildType 是编译期常量（当前为 Debug），C# 会把其后的代码判为不可达（CS0162）；
            // 该分支在其它渠道构建下可达，故经局部变量取值以避免常量折叠误判，行为不变。
            var buildType = ModBase.BuildType;
            if (buildType == ModBase.BuildTypes.Debug)
                return;
            try
            {
                bool MutexCreatedNew;
                PclMutex = new Mutex(true, "PCL_SingletonMutex", out MutexCreatedNew);
                if (MutexCreatedNew)
                    return;
                Logger.Warn("已有一个 PCL Next 实例正在运行");
                // 等待已有的 PCL 退出
                if (e.Args.Length > 0 && e.Args[0] == "--wait")
                {
                    try
                    {
                        if (PclMutex.WaitOne(10000))
                            return; // 等待至多 10 秒
                    }
                    catch (AbandonedMutexException) // 未引用异常对象，省略变量名（消除 CS0168）
                    {
                        return; // 已有实例异常退出，继续启动
                    }
                }
                // 将已有的 PCL 窗口拖出来（依次匹配本分支标题与上游标题）
                string argClassName = null;
                string argWindowName = "pcl-next";
                var WindowHwnd = ModMain.FindWindow(argClassName, argWindowName);
                if (WindowHwnd == nint.Zero)
                {
                    string argClassName1 = null;
                    string argWindowName1 = "Plain Craft Launcher　";
                    WindowHwnd = ModMain.FindWindow(argClassName1, argWindowName1);
                }
                if (WindowHwnd == nint.Zero)
                {
                    string argClassName2 = null;
                    string argWindowName2 = "Plain Craft Launcher 2　";
                    WindowHwnd = ModMain.FindWindow(argClassName2, argWindowName2);
                }
                if (WindowHwnd != nint.Zero)
                    ModMain.ShowWindowToTop(WindowHwnd);
                // 播放提示音并退出
                Interaction.Beep();
                Environment.Exit((int)ModBase.ProcessReturnValues.Cancel);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "单例检查失败");
            }
        }
        private static Mutex PclMutex;

        // 结束
        private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            ModMain.FrmMain?.EndProgram(false);
        }

        // 异常
        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ;
            // 触发页面的 Dispatcher
            // [VB Port] On Error Resume Next -> ignore exceptions in the rest of this method
            try
            {
            if (ModMain.FrmMain?.PageLeft is not null && ModMain.FrmMain.PageLeft is IDispatcherUnhandledException)
            {
                ((IDispatcherUnhandledException)ModMain.FrmMain.PageLeft).DispatcherUnhandledException(sender, e);
                if (e.Handled)
                    return;
            }
            if (ModMain.FrmMain?.PageRight is not null && ModMain.FrmMain.PageRight is IDispatcherUnhandledException)
            {
                ((IDispatcherUnhandledException)ModMain.FrmMain.PageRight).DispatcherUnhandledException(sender, e);
                if (e.Handled)
                    return;
            }
            // 正常处理
            e.Handled = true;
            if (ModBase.IsProgramEnding)
                return;
            ModMain.FeedbackInfo();
            string Detail = e.Exception.GetDisplay(true);
            if (Detail.Contains("System.Windows.Threading.Dispatcher.Invoke") || Detail.Contains("MS.Internal.AppModel.ITaskbarList.HrInit") || Detail.Contains("未能加载文件或程序集") || Detail.Contains(".NET Framework")) // “自动错误判断” 的结果分析
            {
                OldEnvironmentAssert(e.Exception);
            }
            else
            {
                Logger.Error(e.Exception, "程序出现未知错误", LogBehavior.AlertThenCrash);
            }
            } // [VB Port] end try
            // 对应 Application.xaml.vb:202-221 的「On Error Resume Next」：全局异常处理器自身绝不能再抛异常。
            catch (InvalidOperationException)
            {
                // 页面 DispatcherUnhandledException 回调 / WPF 调度器在窗口或控件状态不一致（已关闭、重复处理）时抛此异常：VB 会跳过该句继续，故忽略。
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // FeedbackInfo / OldEnvironmentAssert 触及系统信息或任务栏接口失败：诊断路径出错不影响兜底处理。
            }
            catch (System.IO.IOException)
            {
                // Logger 写日志文件失败：忽略，避免异常处理器二次崩溃。
            }
            catch (UnauthorizedAccessException)
            {
                // 日志目录无写权限：同上，忽略。
            }
            catch (Exception)
            {
                // 上面各句的异常来自页面处理者等外部代码，类型无法穷举；VB 的 Resume Next 本就吞掉任意异常，
                // 这里兜底保持一致：宁可漏掉一次错误上报，也不能让异常处理器抛出新异常导致进程直接退出。
            }
        }
        private static void OldEnvironmentAssert(Exception ex = null)
        {
            ModBase.OpenWebsite("https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/thank-you/net48-offline-installer");
            if (ex is null)
            {
                Logger.Error($"你的 .NET Framework 版本过低或损坏，请下载并重新安装 .NET Framework 4.8！{Constants.vbCrLf}若安装失败，可以先卸载高版本的 .NET Framework 然后再试。", LogBehavior.AlertThenCrash);
            }
            else
            {
                Logger.Error(ex, $"你的 .NET Framework 版本过低或损坏，请下载并重新安装 .NET Framework 4.8！{Constants.vbCrLf}若安装失败，可以先卸载高版本的 .NET Framework 然后再试。", LogBehavior.AlertThenCrash);
            }
        }

        // WebP 原生库解包
        // 托管程序集（Imazen.WebP / NAudio / CacheCow 等）已改由 NuGet 提供，不再需要 AssemblyResolve 动态加载，
        // 这里只负责把内嵌的 libwebp64 释放成 libwebp.dll，供 Imazen.WebP 的 P/Invoke 使用。
        internal static void ExtractLibwebp() // 这个方法会调用 ModBase，进而调用 MeloongCore，所以不能放在构造函数里
        {
            string arglpPathName = PathUtils.RemoveSlashSuffix(ModBase.PathPure.Value);
            Application.SetDllDirectory(arglpPathName);
            try
            {
                ModBase.ExtractResources(ModBase.PathPure.Value + "libwebp.dll", "libwebp64");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "写入 libwebp.dll 失败"); // 防止同时加载多个图片时，同时写入文件导致文件占用，进而导致崩溃
            }
        }
        [DllImport("kernel32", EntryPoint = "SetDllDirectoryA")]
        private static extern bool SetDllDirectory(string lpPathName);

        // 控件模板事件
        private void MyIconButton_Click(object sender, EventArgs e)
        {
            switch (Settings.Get<ModLaunch.McLoginType>("LoginType"))
            {
                case ModLaunch.McLoginType.Ms:
                    {
                        // 微软
                        JObject MsJson = (JObject)Settings.Get<string>("LoginMsJson").DeserializeJson();
                        MsJson.Remove(Conversions.ToString(((dynamic)sender).Tag));
                        Settings.Set("LoginMsJson", MsJson.ToString(Formatting.None));
                        if (object.ReferenceEquals(ModMain.FrmLoginMs.ComboAccounts.SelectedItem, ((dynamic)sender).Parent))
                            ModMain.FrmLoginMs.ComboAccounts.SelectedIndex = 0;
                        ModMain.FrmLoginMs.ComboAccounts.Items.Remove(((dynamic)sender).Parent);
                        break;
                    }
                case ModLaunch.McLoginType.Legacy:
                    {
                        // 离线
                        var Names = new List<string>();
                        Names.AddRange(Settings.Get<string>("LoginLegacyName").ToString().Split("¨"));
                        Names.Remove(Conversions.ToString(((dynamic)sender).Tag));
                        Settings.Set("LoginLegacyName", Names.Join('¨'));
                        ModMain.FrmLoginLegacy.ComboName.ItemsSource = Names;
                        ModMain.FrmLoginLegacy.ComboName.Text = Names.Any() ? Names[0] : "";
                        break;
                    }

                default:
                    {
                        // 第三方
                        string Token = Settings.Get<ModLaunch.McLoginType>("LoginType").ToString();
                        var Dict = new Dictionary<string, string>();
                        var Names = new List<string>();
                        var Passs = new List<string>();
                        if (!string.IsNullOrEmpty(Settings.Get<string>("Login" + Token + "Email")))
                            Names.AddRange(Settings.Get<string>("Login" + Token + "Email").Split("¨"));
                        if (!string.IsNullOrEmpty(Settings.Get<string>("Login" + Token + "Pass")))
                            Passs.AddRange(Settings.Get<string>("Login" + Token + "Pass").Split("¨"));
                        for (int i = 0, loopTo = Names.Count - 1; i <= loopTo; i++)
                            Dict.Add(Names[i], Passs[i]);
                        Dict.Remove(Conversions.ToString(((dynamic)sender).Tag));
                        Settings.Set("Login" + Token + "Email", Dict.Keys.Join('¨'));
                        Settings.Set("Login" + Token + "Pass", Dict.Values.Join('¨'));
                        switch (Token ?? "")
                        {
                            case "Nide":
                                {
                                    ModMain.FrmLoginNide.ComboName.ItemsSource = Dict.Keys;
                                    ModMain.FrmLoginNide.ComboName.Text = Dict.Keys.Any() ? Dict.Keys.ElementAtOrDefault(0) : "";
                                    ModMain.FrmLoginNide.TextPass.Password = Dict.Values.Any() ? Dict.Values.ElementAtOrDefault(0) : "";
                                    break;
                                }
                            case "Auth":
                                {
                                    ModMain.FrmLoginAuth.ComboName.ItemsSource = Dict.Keys;
                                    ModMain.FrmLoginAuth.ComboName.Text = Dict.Keys.Any() ? Dict.Keys.ElementAtOrDefault(0) : "";
                                    ModMain.FrmLoginAuth.TextPass.Password = Dict.Values.Any() ? Dict.Values.ElementAtOrDefault(0) : "";
                                    break;
                                }
                        }

                        break;
                    }
            }
        }

    }
}