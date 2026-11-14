using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public static class ModWatcher
    {

        // 对全体的监视
        public static List<Watcher> McWatcherList = new List<Watcher>();
        // Watcher 的构造函数（启动游戏线程）与状态变更回调（日志 / 时钟线程）会同时读写这个列表：
        // 构造函数是「整表替换」，涉及「读旧表 → 建新表 → 写回」三步，并发时可能丢掉刚加入的监视器。
        // 列表本身一经发布不再修改，因此只需要用锁把「读-换」这一步串起来，读取方取到引用即可安全枚举。
        private static readonly object McWatcherListLock = new object();
        private static bool IsWatcherRunning = false;
        public static bool HasRunningMinecraft = false;
        private static void WatcherStateChanged()
        {
            bool IsRunning = false;
            bool TriggerLauncherShutdown = true;
            List<Watcher> Snapshot;
            lock (McWatcherListLock)
                Snapshot = McWatcherList;
            foreach (var Watcher in Snapshot)
            {
                if (Watcher.State == Watcher.MinecraftState.Loading || Watcher.State == Watcher.MinecraftState.Running || Watcher.State == Watcher.MinecraftState.Crashed && !Watcher.GameProcess.HasExited)
                {
                    IsRunning = true;
                    break;
                }
                else if (Watcher.State == Watcher.MinecraftState.Crashed || Watcher.State == Watcher.MinecraftState.Canceled)
                {
                    TriggerLauncherShutdown = false;
                }
            }
            if (IsWatcherRunning == IsRunning)
                return;
            IsWatcherRunning = IsRunning;
            if (IsWatcherRunning)
            {
                MinecraftStart();
            }
            else
            {
                MinecraftStop(TriggerLauncherShutdown);
            }
        }
        private static void MinecraftStart()
        {
            ModLaunch.McLaunchLog("[全局] 出现运行中的 Minecraft");
            HasRunningMinecraft = true;
            ModMain.FrmMain.BtnExtraShutdown.ShowRefresh();
        }
        private static void MinecraftStop(bool TriggerLauncherShutdown)
        {
            ModLaunch.McLaunchLog("[全局] 已无运行中的 Minecraft");
            HasRunningMinecraft = false;
            ModMain.FrmMain.BtnExtraShutdown.ShowRefresh();
            // 音乐播放
            if (Settings.Get<bool>("UiMusicStop"))
            {
                ModBase.RunInUi(() => { if (ModMusic.MusicResume()) Logger.Info("已根据设置，在结束后开始音乐播放"); });
            }
            else if (Settings.Get<bool>("UiMusicStart"))
            {
                ModBase.RunInUi(() => { if (ModMusic.MusicPause()) Logger.Info("已根据设置，在结束后暂停音乐播放"); });
            }
            // 启动器可见性
            switch (Settings.Get<int>("LaunchArgumentVisible"))
            {
                case 2:
                    {
                        // 直接关闭
                        if (TriggerLauncherShutdown)
                        {
                            ModBase.RunInUi(() => ModMain.FrmMain.EndProgram(false));
                        }
                        else
                        {
                            ModBase.RunInUi(() => ModMain.FrmMain.Hidden = false);
                        }

                        break;
                    }
                case 3:
                    {
                        // 恢复
                        ModBase.RunInUi(() => ModMain.FrmMain.Hidden = false);
                        break;
                    }
            }
        }

        // 对单个进程的监视
        public class Watcher
        {

            // 初始化
            public Process GameProcess;
            public ModMinecraft.McInstance Instance;
            private string WindowTitle = "";
            private int PID;
            public ModLoader.LoaderTask<Process, int> Loader;
            public Watcher(ModLoader.LoaderTask<Process, int> Loader, ModMinecraft.McInstance Instance, string WindowTitle)
            {
                this.Loader = Loader;
                this.Instance = Instance;
                this.WindowTitle = WindowTitle;
                PID = Loader.Input.Id;
                WatcherLog("开始 Minecraft 日志监控");
                if (!string.IsNullOrEmpty(this.WindowTitle))
                    WatcherLog("要求窗口标题：" + WindowTitle);

                // 更改列表
                lock (McWatcherListLock)
                {
                    var NewWatcherList = new List<Watcher>();
                    foreach (var Watch in McWatcherList)
                    {
                        if (Watch.State == MinecraftState.Crashed || Watch.State == MinecraftState.Ended || Watch.State == MinecraftState.Canceled)
                            continue;
                        NewWatcherList.Add(Watch);
                    }
                    NewWatcherList.Add(this);
                    McWatcherList = NewWatcherList;
                }
                WatcherStateChanged();

                // 初始化进程与日志读取
                GameProcess = Loader.Input;
                GameProcess.BeginOutputReadLine();
                GameProcess.BeginErrorReadLine();
                GameProcess.OutputDataReceived += LogReceived;
                GameProcess.ErrorDataReceived += LogReceived;

                // 初始化时钟
                // 设置窗口标题
                ModBase.RunInNewThread(() => { try { while (State != MinecraftState.Ended && State != MinecraftState.Canceled && (Loader.State != ModBase.LoadState.Canceled || State == MinecraftState.Crashed) && (State != MinecraftState.Crashed || !GameProcess.HasExited)) { TimerWindow(); TimerLog(); for (int i = 1; i <= 3; i++) { if (IsWindowFinished && IsWindowAppeared && !string.IsNullOrEmpty(WindowTitle) && State == MinecraftState.Running && !GameProcess.HasExited) { string RealTitle = WindowTitle.Replace("{date}", DateTime.Now.ToString("yyyy'/'M'/'d")).Replace("{time}", DateTime.Now.ToString("HH':'mm':'ss")); string argstr = Conversions.ToString(RealTitle.ToCharArray()); Watcher.SetWindowText((int)WindowHandle, argstr); } Thread.Sleep(64); } } if (State == MinecraftState.Crashed) WatcherStateChanged(); WatcherLog("Minecraft 日志监控已退出"); } catch (Exception ex) { Logger.Error(ex, "Minecraft 日志监控主循环出错"); State = MinecraftState.Ended; } }, "Minecraft Watcher PID " + PID);
            }

            // 状态
            private MinecraftState _State = MinecraftState.Loading;
            public MinecraftState State
            {
                get
                {
                    return _State;
                }
                set
                {
                    if (_State == value)
                        return;
                    _State = value;
                    WatcherStateChanged();
                }
            }
            public enum MinecraftState
            {
                Loading,
                Running,
                Crashed,
                Ended,
                Canceled
            }

            // 日志
            public List<string> WaitingLog = new List<string>(1000);
            private readonly object WaitingLogLock = new object();
            private void LogReceived(object sender, DataReceivedEventArgs e)
            {
                lock (WaitingLogLock)
                    WaitingLog.Add(e.Data);
            }
            private void TimerLog()
            {
                try
                {
                    // 输出文本
                    var Copyed = new List<string>();
                    lock (WaitingLogLock)
                    {
                        if (!WaitingLog.Any())
                            return;
                        Copyed = WaitingLog;
                        WaitingLog = new List<string>(1000);
                    }
                    foreach (string Str in Copyed)
                        GameLog(Str);
                    if (State == MinecraftState.Loading)
                        ProgressUpdate();
                    // 游戏退出检查
                    if (GameProcess.HasExited)
                    {
                        WatcherLog("Minecraft 已退出，返回值：" + GameProcess.ExitCode);
                        // If Process.ExitCode = 1 Then
                        // '返回值为 1，考虑是任务管理器结束
                        // WatcherLog("Minecraft 返回值为 1，考虑为任务管理器结束") '并不，崩了照样是 1
                        // State = MinecraftState.Ended
                        // Else
                        if (State == MinecraftState.Loading)
                        {
                            // 窗口未出现
                            WatcherLog("Minecraft 尚未加载完成，可能已崩溃", LogLevel.Error);
                            Crashed();
                        }
                        else if (GameProcess.ExitCode != 0 && State == MinecraftState.Running && Instance is not null && Instance.ReleaseTime.Year >= 2012)
                        {
                            // 返回值不为 0 且未结束
                            WatcherLog("Minecraft 返回值异常，可能已崩溃", LogLevel.Error);
                            Crashed();
                        }
                        else if (State != MinecraftState.Crashed)
                        {
                            // 正常关闭
                            State = MinecraftState.Ended;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "输出 Minecraft 日志失败");
                }
            }
            public ConcurrentQueue<string> LatestLog = new ConcurrentQueue<string>();
            private void GameLog(string Text)
            {
                // 预处理
                if (Text is null)
                    return;
                Text = Text.ReplaceLineEndings("\r\n");
                // If Text.Contains("�����") Then Hint("检测到错误的日志编码：" & Text)
                // 加入预存储
                LatestLog.Enqueue(Text);
                if (LatestLog.Count >= 501)
                {
                    string argresult = null;
                    LatestLog.TryDequeue(out argresult);
                }
                Logger.Trace(Text);
                // 进度处理
                if (LogProgress < 1)
                {
                    WatcherLog("日志 1/5：已出现日志输出");
                    LogProgress = 1;
                } // 可能第一句就是后面需要判断的 Log（重现：启动 1.15.2 原版）
                if (LogProgress < 2 && Text.Contains("Setting user:"))
                {
                    WatcherLog("日志 2/5：游戏用户已设置"); // 仅确保支持 Minecraft 1.7+
                    LogProgress = 2;
                }
                else if (LogProgress < 3 && Text.ContainsIgnoreCase("lwjgl version"))
                {
                    WatcherLog("日志 3/5：LWJGL 版本已确认");
                    LogProgress = 3;
                }
                else if (LogProgress < 4 && (Text.Contains("OpenAL initialized") || Text.Contains("Starting up SoundSystem")))
                {
                    WatcherLog("日志 4/5：OpenAL 已加载"); // 仅确保支持 Minecraft 1.7+
                    LogProgress = 4;
                }
                else if (LogProgress < 5 && (Text.Contains("Created") && Text.Contains("textures") && Text.Contains("-atlas") || Text.Contains("Found animation info")))
                {
                    WatcherLog("日志 5/5：材质已加载"); // 仅确保支持 Minecraft 1.7+
                    LogProgress = 5;
                }
                // 输出日志
                // Log(Text)
                // 关闭与崩溃检测
                if (!Text.Contains("[CHAT]"))
                {
                    if (Text.Contains("Someone is closing me!") || Text.Contains("Restarting Minecraft with command")) // #1258
                    {
                        WatcherLog("识别为关闭的 Log：" + Text);
                        State = MinecraftState.Ended;
                    }
                    else if (Text.Contains("Crash report saved to") || Text.Contains("This crash report has been saved to:"))
                    {
                        // Text.Contains("Minecraft ran into a problem! Report saved to:") Then
                        // Minecraft 崩溃，忽略 VanillaFix
                        WatcherLog("识别为崩溃的 Log：" + Text, LogLevel.Error);
                        Crashed();
                    }
                    else if (Text.Contains("Could not save crash report to"))
                    {
                        // Minecraft 崩溃，无法保存崩溃日志
                        WatcherLog("识别为崩溃的 Log：" + Text, LogLevel.Error);
                        Crashed();
                    }
                    else if (Text.Contains("/ERROR]: Unable to launch") || Text.Contains("An exception was thrown, the game will display an error screen and halt."))
                    {
                        // Forge 崩溃
                        WatcherLog("识别为崩溃的 Log：" + Text, LogLevel.Error);
                        Crashed();
                        // ElseIf Text.Contains("Shutdown failure!") Then
                        // 'Minecraft 强行崩溃，由于点 X 强行关闭也会触发这句话，所以不可用
                        // Crashed(Nothing)
                    }
                }
            }
            private void WatcherLog(string Text, LogLevel Level = LogLevel.Info)
            {
                ModLaunch.McLaunchLog("[" + PID + "] " + Text, Level);
            }

            // 进度更新
            private int LogProgress = 0;
            private void ProgressUpdate()
            {
                double CurrentProgress;
                if (IsWindowAppeared || LogProgress == 5)
                {
                    CurrentProgress = 0.95d;
                    WatcherLog("Minecraft 加载已完成");
                    State = MinecraftState.Running;
                }
                else
                {
                    CurrentProgress = Math.Min(LogProgress, 3) / 3d * 0.9d;
                }
                Loader.Progress = CurrentProgress;
            }

            // 窗口检查
            private bool IsWindowAppeared = false;
            /// <summary>
        /// 窗口检查是否已经完成。这不一定代表着找到了窗口（如果没有找到，IsWindowAppeared 仍为 False）。
        /// </summary>
            private bool IsWindowFinished = false;
            private nint WindowHandle;
            private void TimerWindow()
            {
                try
                {
                    if (GameProcess.HasExited)
                        return;
                    if (IsWindowFinished)
                        return;
                    // 获取全部窗口，检查是否有新增的
                    KeyValuePair<nint, string>? MinecraftWindow = default;
                    try
                    {
                        MinecraftWindow = TryGetMinecraftWindow();
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        // 拒绝访问（#1062）
                        Logger.Error(ex, "由于反作弊或安全软件拦截，PCL Next 无法操作游戏窗口", LogBehavior.Toast);
                        IsWindowFinished = true;
                    }
                    if (MinecraftWindow is null)
                        return;
                    string MinecraftWindowName = MinecraftWindow.Value.Value;
                    var MinecraftWindowHandle = MinecraftWindow.Value.Key;
                    // 已找到窗口
                    if (!MinecraftWindowName.StartsWithF("FML") && !MinecraftWindowName.StartsWithF("Quilt Loader"))
                    {
                        // 已找到 Minecraft 窗口
                        WindowHandle = MinecraftWindowHandle;
                        WatcherLog($"Minecraft 窗口已加载：{MinecraftWindowName}（{MinecraftWindowHandle.ToInt64()}）");
                        IsWindowFinished = true;
                        // 最大化
                        if (Settings.Get<int>("LaunchArgumentWindowType") == 4)
                        {
                            // 如果最大化导致屏幕渲染大小不对，那是 MC 的 Bug，不是我的 Bug
                            // ……虽然我很想这样说，但总有人反馈，算了
                            ModBase.RunInNewThread(() => { try { Thread.Sleep(2000); ShowWindow(WindowHandle, 3U); WatcherLog($"已最大化 Minecraft 窗口：{MinecraftWindowHandle.ToInt64()}"); } catch (Exception ex) { Logger.Warn(ex, "最大化 Minecraft 窗口时出现错误"); } }, "MinecraftWindowMaximize");
                        }
                    }
                    else if (!IsWindowAppeared)
                    {
                        // 已找到 Mod Loader 窗口
                        WatcherLog("Mod Loader 窗口已加载：" + MinecraftWindowName + "（" + MinecraftWindowHandle.ToInt64() + "）");
                    }
                    IsWindowAppeared = true;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "检查 Minecraft 窗口失败");
                }
            }
            /// <summary>
        /// 获取可能是当前进程对应的 Minecraft 窗口的句柄和标题。
        /// Nothing 代表未找到。
        /// </summary>
            private KeyValuePair<nint, string>? TryGetMinecraftWindow()
            {
                object Result = null;
                EnumWindows((hwnd, lParam) =>
    {
        if (Result is not null)
            return;
        // 检查类名
        var str = new StringBuilder(512);
        GetClassName((int)hwnd, str, str.Capacity);
        string ClassName = str.ToString();
        if (!(ClassName == "GLFW30" || ClassName == "SDL_app" || ClassName == "LWJGL" || ClassName == "SunAwtFrame"))
            return;
        // SDL 会先创建隐藏窗口，待初始化完成后才显示（#9073）
        if (ClassName == "SDL_app" && !IsWindowVisible(hwnd))
            return;
        // 获取窗口标题名
        str = new StringBuilder(512);
        GetWindowText((int)hwnd, str, str.Capacity);
        string WindowText = str.ToString();
        // 有的 Mod 可以修改窗口标题，所以不能检测是否为 Minecraft 打头，这并不准确
        // 部分版本会搞个 GLFW message window，高版本还有个 Hidden Utility Window，都要反选
        if (string.IsNullOrWhiteSpace(WindowText) || WindowText == "SDL_app" || WindowText == "PopupMessageWindow" || WindowText.StartsWithF("GLFW") || WindowText.EndsWithF("Hidden Utility Window"))
            return;
        // 获取窗口关联的进程
        var ProcessId = default(int);
        GetWindowThreadProcessId(hwnd, ref ProcessId);
        try
        {
            if (Process.GetProcessById(ProcessId).StartTime < GameProcess.StartTime)
                return; // 需要是此后启动的进程
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "枚举 Minecraft 窗口进程失败");
            return;
        }
        // 返回
        Result = new KeyValuePair<nint, string>(hwnd, WindowText);
    }, 0);
                return (KeyValuePair<nint, string>?)Result;
            }
            private delegate void EnumWindowsSub(nint hwnd, int lParam);
            [DllImport("user32")]
            private static extern bool EnumWindows(EnumWindowsSub hWnd, int lParam);
            [DllImport("user32", EntryPoint = "GetClassNameA")]
            private static extern int GetClassName(int hWnd, StringBuilder str, int maxCount);
            [DllImport("user32", EntryPoint = "GetWindowTextA")]
            private static extern int GetWindowText(int hWnd, StringBuilder str, int maxCount);
            [DllImport("user32", EntryPoint = "SetWindowTextA")]
            private static extern bool SetWindowText(int hWnd, string str);
            [DllImport("user32")]
            private static extern bool ShowWindow(nint hWnd, uint cmdWindow);
            [DllImport("user32")]
            private static extern bool IsWindowVisible(nint hWnd);
            [DllImport("user32")]
            private static extern int GetWindowThreadProcessId(nint hWnd, ref int lpdwProcessId);

            // 崩溃处理
            private void Crashed()
            {
                if (State == MinecraftState.Crashed || State == MinecraftState.Ended)
                    return;
                State = MinecraftState.Crashed;
                // 崩溃分析
                WatcherLog("Minecraft 已崩溃，将在 2 秒后开始崩溃分析", LogLevel.Error);
                ModMain.Hint("检测到 Minecraft 出现错误，错误分析已开始……", ModMain.HintType.Red);
                ModMain.FeedbackInfo();
                ModBase.RunInNewThread(() => { try { Thread.Sleep(2000); WatcherLog("崩溃分析开始"); var Analyzer = new CrashAnalyzer(Instance); Analyzer.Collect(Instance.PathIndie, LatestLog.ToList()); Analyzer.Prepare(); Analyzer.Analyze(); Analyzer.Output(false, new List<string>() { Instance.PathVersion + Instance.Name + ".json", Paths.Base + @"PCL\Log1.txt", Paths.Base + @"PCL\LatestLaunch.bat" }); } catch (Exception ex) { Logger.Error(ex, "崩溃分析失败"); } }, "Crash Analyzer");
            }

            // 强制关闭
            public void Kill()
            {
                State = MinecraftState.Canceled;
                WatcherLog("尝试强制结束 Minecraft 进程");
                try
                {
                    if (!GameProcess.HasExited)
                        GameProcess.Kill();
                    WatcherLog("已强制结束 Minecraft 进程");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "强制结束 Minecraft 进程失败", LogBehavior.Toast);
                }
            }

        }

    }
}