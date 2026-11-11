using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Documents;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public static class ModLoader
    {

        // 各类加载器
        /// <summary>
    /// 加载器的统一基类。
    /// </summary>
        public abstract class LoaderBase : ILoadingTrigger
        {

            public bool IsLoader { get; private set; } = true;

            // 基础属性
            /// <summary>
        /// 加载器的标识编号。
        /// </summary>
            public int Uuid = ModBase.GetUuid();
            /// <summary>
        /// 加载器的名称。
        /// </summary>
            public string Name;
            /// <summary>
        /// 用于状态改变检测的同步锁。
        /// </summary>
            public readonly object LockState = new object();
            /// <summary>
        /// 父加载器。
        /// </summary>
            public LoaderBase Parent = null;
            /// <summary>
        /// 最上级的加载器。
        /// </summary>
            public LoaderBase RealParent
            {
                get
                {
                    LoaderBase RealParentRet = default;
                    try
                    {
                        RealParentRet = Parent;
                        while (RealParentRet is not null && RealParentRet.Parent is not null)
                            RealParentRet = RealParentRet.Parent;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"获取父加载器失败（{Name}）");
                        return null;
                    }

                    return RealParentRet;
                }
            }

            public LoaderBase()
            {
                Name = "未命名任务 " + Uuid + "#";
            }

            public virtual void InitParent(LoaderBase Parent)
            {
                this.Parent = Parent;
            }

            // 事件

            /// <summary>
        /// 当状态改变时，在工作线程触发代码。在添加事件后，必须将 HasOnStateChangedThread 设为 True。
        /// </summary>
            public event OnStateChangedThreadEventHandler OnStateChangedThread;

            public delegate void OnStateChangedThreadEventHandler(LoaderBase Loader, ModBase.LoadState NewState, ModBase.LoadState OldState);
            public bool HasOnStateChangedThread = false;

            /// <summary>
        /// 当状态改变时，在 UI 线程触发代码。
        /// </summary>
            public event OnStateChangedUiEventHandler OnStateChangedUi;

            public delegate void OnStateChangedUiEventHandler(LoaderBase Loader, ModBase.LoadState NewState, ModBase.LoadState OldState);
            /// <summary>
        /// 简易的在 UI 线程添加触发事件的方式。主要用于在新建 Loader 时直接使用 With 绑定事件，以及进行老代码兼容。
        /// </summary>
            public Action<LoaderBase> OnStateChanged
            {
                set
                {
                    OnStateChangedUi += (Loader, NewState, OldState) => value(Loader);
                }
            }

            /// <summary>
        /// 在加载器目标事件执行完成，加载器状态即将变为 Finish 时调用。可以视为扩展加载器目标事件。
        /// </summary>
            public event PreviewFinishEventHandler PreviewFinish;

            public delegate void PreviewFinishEventHandler(LoaderBase Loader);
            protected void RaisePreviewFinish()
            {
                PreviewFinish?.Invoke(this);
            }

            // 状态监控
            /// <summary>
        /// 加载器的状态。
        /// </summary>
            public ModBase.LoadState State
            {
                get
                {
                    return _State;
                }
                set
                {
                    if (_State == value)
                        return;
                    var OldState = _State;
                    if (value == ModBase.LoadState.Finished && Settings.Get<bool>("SystemDebugDelay"))
                        Thread.Sleep(ModBase.RandomInteger(100, 2000));
                    _State = value;
                    if (value == ModBase.LoadState.Canceled)
                        CancellationTokenSource?.Cancel();
                    Logger.Log($"加载器 {GetType().Name} {Name} 状态改变：{value}", value == ModBase.LoadState.Failed ? LogLevel.Error : LogLevel.Info);
                    // 实现 ILoadingTrigger 接口与 OnStateChanged 回调
                    ModBase.RunInUi(() =>
        {
            switch (value)
            {
                case ModBase.LoadState.Loading:
                    {
                        LoadingState = MyLoading.MyLoadingState.Run;
                        break;
                    }
                case ModBase.LoadState.Failed:
                    {
                        LoadingState = MyLoading.MyLoadingState.Error;
                        break;
                    }

                default:
                    {
                        LoadingState = MyLoading.MyLoadingState.Stop;
                        break;
                    }
            }
            OnStateChangedUi?.Invoke(this, value, OldState);
        });
                    if (HasOnStateChangedThread)
                        ModBase.RunInThread(() => OnStateChangedThread?.Invoke(this, value, OldState));
                }
            }
            private ModBase.LoadState _State = ModBase.LoadState.Waiting;
            public MyLoading.MyLoadingState LoadingState
            {
                get
                {
                    return _LoadingState;
                }
                set
                {
                    if (_LoadingState == value)
                        return;
                    var OldState = _LoadingState;
                    _LoadingState = value;
                    LoadingStateChanged?.Invoke(value, OldState);
                }
            }
            private MyLoading.MyLoadingState _LoadingState = MyLoading.MyLoadingState.Stop;
            public event ILoadingTrigger.LoadingStateChangedEventHandler LoadingStateChanged;
            /// <summary>
        /// 若加载器出错，可提供给外部参考的异常。
        /// </summary>
            public Exception Error { get; set; } = null;
            /// <summary>
        /// 使用 LoaderCombo 加载时，该任务是否会阻碍后续任务的进行。
        /// </summary>
            public bool Block = true;
            /// <summary>
        /// 该加载器是否显示在列表中。
        /// </summary>
            public bool Show = true;
            /// <summary>
        /// 当前加载器是否由 IsForceRestart 强制调起。
        /// 这个属性自身不会干任何事，而是提供给加载器执行的函数，使得加载器调用另一个加载器时，可以继承强制重启属性。
        /// </summary>
            public bool IsForceRestarting = false;

            // 进度监控
            /// <summary>
        /// 加载器的执行进度，为 0 至 1 的小数。
        /// </summary>
            public virtual double Progress
            {
                get
                {
                    switch (State)
                    {
                        case ModBase.LoadState.Waiting:
                            {
                                return 0d;
                            }
                        case ModBase.LoadState.Loading:
                            {
                                return _Progress == -1 ? 0d : _Progress;
                            }

                        default:
                            {
                                return 1d;
                            }
                    }
                }
                set
                {
                    if (_Progress == value)
                        return;
                    double OldValue = _Progress;
                    _Progress = value;
                    ProgressChanged?.Invoke(value, OldValue);
                }
            }
            private double _Progress = -1;
            public event ILoadingTrigger.ProgressChangedEventHandler ProgressChanged;
            /// <summary>
        /// 创建一个 ProgressProvider，在其进度改变时，并将其进度单向映射到此加载器上。
        /// 这是 Loader 对新框架的兼容层。
        /// </summary>
            public ProgressProvider CreateSyncProgressProvider(double FromPercentage = 0d, double ToPercentage = 1d)
            {
                var Provider = new ProgressProvider();
                Provider.ProgressChanged += ChangedProgress =>
        {
            var RawProgress = Provider.GetRaw();
            Progress = FromPercentage + (RawProgress.actual + RawProgress.skiped) * (ToPercentage - FromPercentage);
        };
                return Provider;
            }
            /// <summary>
        /// 计算总进度时的权重。它应该为预计时间（秒）。
        /// </summary>
            public double ProgressWeight { get; set; } = 1d;

            // 状态变化
            public abstract void Start(object Input = null, bool IsForceRestart = false);
            public abstract void Cancel();
            public abstract void Failed(Exception Ex);
            private CancellationTokenSource CancellationTokenSource;
            /// <summary>
        /// 创建一个随此加载器取消的取消令牌；令牌取消时也会调用此加载器的 Cancel。
        /// 这是 Loader 对新框架的兼容层。
        /// </summary>
            public CancellationToken CreateCancellationToken()
            {
                lock (LockState)
                {
                    if (CancellationTokenSource is null || CancellationTokenSource.IsCancellationRequested)
                    {
                        CancellationTokenSource = new CancellationTokenSource();
                        CancellationTokenSource.Token.Register(Cancel);
                    }
                    if (State == ModBase.LoadState.Canceled)
                        CancellationTokenSource.Cancel();
                    return CancellationTokenSource.Token;
                }
            }

            // 等待结束
            public const string WaitForExitTimeoutMessage = "等待加载器执行超时。";

            /// <summary>
        /// 无限期地等待加载器完成，直到结束或抛出异常。若加载器尚未开始，则会开始执行。
        /// </summary>
            public void WaitForExit(object Input = null, LoaderBase LoaderToSyncProgress = null, bool IsForceRestart = false, CancellationToken c = default, ProgressProvider p = null)
            {
                Start(Input, IsForceRestart);
                while (State == ModBase.LoadState.Loading)
                {
                    if (LoaderToSyncProgress is not null)
                        LoaderToSyncProgress.Progress = Progress;
                    p?.Set(Progress);
                    c.ThrowIfCancellationRequested();
                    Thread.Sleep(10);
                }
                if (State == ModBase.LoadState.Finished)
                {
                    return;
                }
                else if (State == ModBase.LoadState.Canceled)
                {
                    throw new OperationCanceledException("加载器执行已中断");
                }
                else if (Error == null)
                {
                    throw new Exception("未知错误！");
                }
                else
                {
                    throw new Exception(Error.Message, Error);
                } // 保留调用堆栈，同时不影响信息输出与单元测试
            }
            /// <summary>
        /// 等待加载器完成，直到结束、抛出异常或超时。若加载器尚未开始，则会开始执行。
        /// </summary>
        /// <param name="Timeout">等待的超时时间，以毫秒为单位。</param>
        /// <param name="TimeoutMessage">若执行超时，将会抛出的异常信息。</param>
            public void WaitForExitTime(int Timeout, object Input = null, string TimeoutMessage = WaitForExitTimeoutMessage, object LoaderToSyncProgress = null, bool IsForceRestart = false)
            {
                Start(Input, IsForceRestart);
                while (State == ModBase.LoadState.Loading)
                {
                    if (LoaderToSyncProgress is not null)
                        ((dynamic)LoaderToSyncProgress).Progress = Progress;
                    Thread.Sleep(10);
                    Timeout -= 10;
                    if (Timeout < 0)
                        throw new TimeoutException(TimeoutMessage);
                }
                if (State == ModBase.LoadState.Finished)
                {
                    return;
                }
                else if (State == ModBase.LoadState.Canceled)
                {
                    throw new OperationCanceledException("加载器执行已中断");
                }
                else if (Error == null)
                {
                    throw new Exception("未知错误！");
                }
                else
                {
                    throw Error;
                }
            }

            // 相同重载
            public override bool Equals(object obj)
            {
                LoaderBase @base = obj as LoaderBase;
                return @base is not null && Uuid == @base.Uuid;
            }

        }
        /// <summary>
    /// 用于异步执行并监控单一函数的加载器。
    /// </summary>
        public class LoaderTask<TIn, TOut> : LoaderBase
        {

            // 线程设定
            protected internal ThreadPriority ThreadPriority;
            // 执行事件
            protected internal Action<LoaderTask<TIn, TOut>> LoadDelegate;
            protected internal Func<object> InputDelegate;
            // 状态指示
            /// <summary>
        /// 当前执行线程是否应当中断。只应用在加载器的工作线程中判断，不可跨线程调用。
        /// </summary>
            public bool IsCanceled
            {
                get
                {
                    return IsCanceledWithThread(Thread.CurrentThread);
                }
            }
            /// <summary>
        /// 当前执行线程是否应当中断。需要手动提供加载器线程，用于需要跨线程检查的情况。
        /// </summary>
            public bool IsCanceledWithThread(Thread Thread)
            {
                return LastRunningThread is null || !ReferenceEquals(Thread, LastRunningThread) || State == ModBase.LoadState.Canceled;
            }
            /// <summary>
        /// 在输入相同时使用原有结果的超时，单位为毫秒。
        /// </summary>
            public int ReloadTimeout = -1;
            /// <summary>
        /// 上次完成加载时的时间。
        /// </summary>
            public long LastFinishedTime = 0L;
            /// <summary>
        /// 最后一次运行加载器的线程。可能为 Nothing，或线程已结束。
        /// </summary>
            public Thread LastRunningThread = null;
            // 输入输出
            public TIn Input = default;
            public TOut Output = default;

            // 获取输入
            public TIn StartGetInput(TIn Input = default, Func<TIn> InputDelegate = null) // InputDelegate 参数存在匿名调用
            {
                // VB 原文：If InputDelegate Is Nothing Then InputDelegate = Me.InputDelegate
                // VB 允许 Func(Of Object) → Func(Of TIn) 的宽松委托转换（C# 不允许隐式变型）。
                // 这里改为显式包装：调用字段委托拿到 Object，再按 TIn 转换，与 VB 运行时语义一致；
                // 字段为 Nothing 时保持 Nothing（VB 对 Nothing 的转换结果仍是 Nothing），不影响下方 IsNothing 判断。
                if (InputDelegate is null && this.InputDelegate is not null)
                    InputDelegate = () => Conversions.ToGenericParameter<TIn>(this.InputDelegate());
                TIn NewInput = default; // 若 InputType 不能为 Nothing，则会导致 Input Is Nothing 永远失败，因此需要额外判断
                if ((Input is null || NewInput is not null && Input.Equals(NewInput)) && InputDelegate is not null)
                {
                    ModBase.RunInUiWait(() => Input = InputDelegate());
                }
                return Input;
            }
            // 代码执行
            public bool ShouldStart(ref TIn Input, bool IsForceRestart = false, bool IgnoreReloadTimeout = false)
            {
                // 获取输入
                try
                {
                    Input = StartGetInput(Input);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"加载输入获取失败（{Name}）", LogBehavior.Toast);
                    Error = ex;
                    lock (LockState)
                        State = ModBase.LoadState.Failed;
                }
                // 检验输入以确定情况
                if (IsForceRestart)
                    return true; // 强制要求重启
                if (Input is null != this.Input is null || Input is not null && !Input.Equals(this.Input))
                    return true; // 输入不同

                if ((State == ModBase.LoadState.Loading || State == ModBase.LoadState.Finished) && (IgnoreReloadTimeout || ReloadTimeout == -1 || LastFinishedTime == 0L || ModBase.GetTimeMs() - LastFinishedTime < ReloadTimeout)) // 正在加载或已结束…
                                                                                                                                                                                                                                     // …且没有超时…
                {
                    return false; // …则不重试
                }
                else
                {
                    return true;
                } // 否则需要重启
            }
            public override void Start(object Input = null, bool IsForceRestart = false)
            {
                // 确认是否开始加载
                bool localShouldStart() { TIn argInput = Conversions.ToGenericParameter<TIn>(Input); var ret = ShouldStart(ref argInput, IsForceRestart); Input = argInput; return ret; }

                if (localShouldStart())
                {
                    // 输入不同或失败，开始加载
                    if (State == ModBase.LoadState.Loading)
                        TriggerThreadInterrupt();
                    this.Input = Conversions.ToGenericParameter<TIn>(Input);
                    lock (LockState)
                    {
                        State = ModBase.LoadState.Loading;
                        Progress = -1;
                    }
                }
                else
                {
                    return;
                }

                // 如果线程是因为判断到 IsCanceled 而提前中止，则代表已有新线程被重启，此时不应当改为 Canceled
                // 如果线程是在没有 IsCanceled 时手动引发了取消异常，则代表没有重启线程，这通常代表用户手动取消，应当改为 Canceled
                LastRunningThread = new Thread(() => { try { IsForceRestarting = IsForceRestart; Logger.Trace(() => $"加载线程 {Name} 已{(IsForceRestarting ? "强制" : "")}启动"); LoadDelegate(this); if (IsCanceled) { Logger.Warn($"加载线程 {Name} 已中断但线程正常运行至结束，输出被弃用（最新线程：{(LastRunningThread is null ? -1 : LastRunningThread.ManagedThreadId)}）"); return; } Logger.Trace(() => $"加载线程 {Name} 已完成"); RaisePreviewFinish(); State = ModBase.LoadState.Finished; LastFinishedTime = ModBase.GetTimeMs(); } catch (Exception ex) { if (ex.IsCanceled()) { Logger.Trace(ex, $"加载线程 {Name} 已取消，当前已完成 {Math.Round(Progress * 100d)}%"); if (!IsCanceled) State = ModBase.LoadState.Canceled; } else { Failed(ex); } } }) { Name = "L/" + Name, Priority = ThreadPriority }; // 未中断，本次输出有效
                try
                {
                    LastRunningThread.Start(); // 不能使用 RunInNewThread，否则在函数返回前线程就会运行完，导致误判 IsCanceled
                }
                catch (ThreadStateException) // 未引用异常对象，省略变量名（消除 CS0168）
                // 若遇到偶发的 “线程正在运行或被终止”，则等待后重试
                {
                    Thread.Sleep(500);
                    LastRunningThread.Start();
                }
            }
            public override void Failed(Exception ex)
            {
                Error = ex;
                lock (LockState)
                {
                    if (IsCanceled || State >= ModBase.LoadState.Finished)
                        return;
                    State = ModBase.LoadState.Failed;
                }
                Logger.Warn(ex, $"加载线程 {Name} 出错，已完成 {Math.Round(Progress * 100d)}%");
                TriggerThreadInterrupt();
            }
            public override void Cancel()
            {
                lock (LockState)
                {
                    if (State != ModBase.LoadState.Loading)
                        return;
                    State = ModBase.LoadState.Canceled;
                }
                TriggerThreadInterrupt();
            }
            private void TriggerThreadInterrupt()
            {
                if (LastRunningThread is null)
                    return;
                if (LastRunningThread.IsAlive)
                {
                    LastRunningThread.Interrupt();
                    Logger.Trace(() => $"加载线程 {Name} 已中断");
                }
                LastRunningThread = null;
            }

            public LoaderTask()
            {
                // 仅仅是为了避免一些智障报错（继承类必须重写 New 的情况）
            }
            public LoaderTask(string Name, Action<LoaderTask<TIn, TOut>> LoadDelegate, Func<TIn> InputDelegate = null, ThreadPriority Priority = ThreadPriority.Normal)
            {
                this.Name = Name;
                this.LoadDelegate = LoadDelegate;
                // VB 原文：Me.InputDelegate = InputDelegate（Func(Of TIn) → Func(Of Object) 宽松委托转换）
                // C# 无隐式委托变型，需显式包一层；Nothing 仍赋 Nothing。
                this.InputDelegate = InputDelegate is null ? null : new Func<object>(() => InputDelegate());
                ThreadPriority = Priority;
            }

        }

        /// <summary>
    /// 新 Worker 对老 Loader 的兼容包装类。
    /// </summary>
        public class LoaderWorker : LoaderBase
        {
            private IWorker _Worker;

            public virtual IWorker Worker
            {
                [MethodImpl(MethodImplOptions.Synchronized)]
                get
                {
                    return _Worker;
                }

                [MethodImpl(MethodImplOptions.Synchronized)]
                set
                {
                    if (_Worker != null)
                    {
                        _Worker.Succeeded -= Worker_Succeeded;
                        _Worker.Canceled -= Cancel;
                        _Worker.Failed -= Failed;
                    }

                    _Worker = value;
                    if (_Worker != null)
                    {
                        _Worker.Succeeded += Worker_Succeeded;
                        _Worker.Canceled += Cancel;
                        _Worker.Failed += Failed;
                    }
                }
            }
            public LoaderWorker(string Name, IWorker Worker)
            {
                this.Name = Name;
                this.Worker = Worker;
                Worker.Progress.ProgressChanged += Raw => Progress = Raw.actual + Raw.skiped;
            }

            public override void Start(object Input = null, bool IsForceRestart = false)
            {
                lock (LockState)
                    State = ModBase.LoadState.Loading;
                Logger.Trace(() => $"Worker {Name} 已启动");
                IsForceRestarting = IsForceRestart;
                Worker.Start();
            }
            private void Worker_Succeeded()
            {
                Logger.Trace(() => $"Worker {Name} 已完成");
                RaisePreviewFinish();
                lock (LockState)
                    State = ModBase.LoadState.Finished;
            }
            public override void Cancel()
            {
                lock (LockState)
                {
                    if (State != ModBase.LoadState.Loading)
                        return;
                    State = ModBase.LoadState.Canceled;
                }
                Logger.Trace(() => $"Worker {Name} 已取消，当前已完成 {Math.Round(Progress * 100d)}%");
                Worker.Cancel();
            }
            public override void Failed(Exception Ex)
            {
                lock (LockState)
                {
                    if (State >= ModBase.LoadState.Finished)
                        return;
                    State = ModBase.LoadState.Failed;
                }
                Logger.Warn(Ex, $"Worker {Name} 出错，已完成 {Math.Round(Progress * 100d)}%");
                Error = Ex;
                Worker.Cancel();
            }
        }

        /// <summary>
    /// 支持多个加载器连续运作的复合加载器。
    /// </summary>
        public class LoaderCombo<InputType> : LoaderBase
        {

            public override double Progress
            {
                get
                {
                    switch (State)
                    {
                        case ModBase.LoadState.Waiting:
                            {
                                return 0d;
                            }
                        case ModBase.LoadState.Loading:
                            {
                                double Total = 0d;
                                double Finished = 0d;
                                foreach (var Loader in Loaders)
                                {
                                    Total += Loader.ProgressWeight;
                                    Finished += Loader.ProgressWeight * Loader.Progress;
                                }
                                if (Total == 0d)
                                    return 0d;
                                return Finished / Total;
                            }

                        default:
                            {
                                return 1d;
                            }
                    }
                }
                set
                {
                    throw new Exception("多重加载器不支持设置进度");
                }
            }

            public List<LoaderBase> Loaders = new List<LoaderBase>();
            public InputType Input;
            public LoaderCombo(string Name, IEnumerable<LoaderBase> Loaders)
            {
                this.Loaders.Clear();
                foreach (LoaderBase Loader in Loaders)
                {
                    if (Loader is not null)
                    {
                        this.Loaders.Add(Loader);
                        Loader.OnStateChangedThread += SubTaskStateChanged;
                        Loader.HasOnStateChangedThread = true;
                    }
                }
                InitParent(null);
                this.Name = Name;
            }
            public override void InitParent(LoaderBase Parent)
            {
                this.Parent = Parent;
                foreach (var Loader in Loaders)
                    Loader.InitParent(this);
            }
            public override void Start(object Input = null, bool IsForceRestart = false)
            {
                IsForceRestarting = IsForceRestart;
                // 改变状态
                lock (LockState)
                {
                    if (State == ModBase.LoadState.Loading)
                    {
                        return;
                    }
                    else
                    {
                        State = ModBase.LoadState.Loading;
                    }
                }
                // 启动加载
                this.Input = Conversions.ToGenericParameter<InputType>(Input);
                if (IsForceRestart)
                {
                    foreach (var Loader in Loaders)
                        Loader.State = ModBase.LoadState.Waiting;
                }
                ModBase.RunInThread(Update);
            }
            public override void Cancel()
            {
                // 改变状态
                lock (LockState)
                {
                    if (State == ModBase.LoadState.Loading || State == ModBase.LoadState.Waiting)
                    {
                        State = ModBase.LoadState.Canceled;
                    }
                    else
                    {
                        return;
                    }
                }
                // 中断加载器
                ModBase.RunInThread(() => { foreach (var Loader in Loaders) Loader.Cancel(); });
            }
            public override void Failed(Exception Ex)
            {
                Error = Ex; // 先设置错误再调整状态，防止父加载器获取不到异常
                lock (LockState)
                {
                    if (State >= ModBase.LoadState.Finished)
                        return;
                    State = ModBase.LoadState.Failed;
                }
                foreach (var Loader in Loaders)
                    Loader.Cancel();
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
            }

            /// <summary>
        /// 子任务状态变更。
        /// </summary>
            private void SubTaskStateChanged(LoaderBase Loader, ModBase.LoadState NewState, ModBase.LoadState OldState)
            {
                switch (NewState)
                {
                    case ModBase.LoadState.Loading:
                        {
                            break;
                        }
                    // 开始，啥都不干
                    case ModBase.LoadState.Waiting:
                        {
                            break;
                        }
                    // 子加载器可能由于外部输入改变而暂时变为 Waiting，之后会立即重新启动
                    // 所以啥都不干就行
                    case ModBase.LoadState.Finished:
                        {
                            // 正常结束，触发刷新
                            Update();
                            break;
                        }
                    case ModBase.LoadState.Canceled:
                        {
                            // 被中断，这个任务也中断
                            Cancel();
                            break;
                        }
                    case ModBase.LoadState.Failed:
                        {
                            // 完蛋，出错了
                            Failed(new Exception(Loader.Name + "失败", Loader.Error));
                            break;
                        }
                }
            }
            /// <summary>
        /// 触发一次更新，以启动新加载器或完成。
        /// </summary>
            private void Update()
            {
                if (State == ModBase.LoadState.Finished || State == ModBase.LoadState.Failed || State == ModBase.LoadState.Canceled)
                    return;
                bool IsFinished = true;
                bool Blocked = false;
                object Input = this.Input;
                foreach (var Loader in Loaders)
                {
                    switch (Loader.State)
                    {
                        case ModBase.LoadState.Finished:
                            {
                                // 检查 LoaderTask 是否需要重启
                                if (GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderTask<,>)))
                                {
                                    if (Conversions.ToBoolean(((dynamic)((object)Loader)).ShouldStart(Input is not null && ReferenceEquals(Loader.GetType().GenericTypeArguments.First(), Input.GetType()) ? Input : null, IgnoreReloadTimeout: true)))
                                    {
                                        Logger.Info($"由于输入条件变更，重启已完成的加载器 {Loader.Name}");
                                        goto default;
                                    }
                                    // 更新下一个加载器的输入
                                    Input = ((dynamic)((object)Loader)).Output;
                                }
                                // 如果不让继续启动，且已有加载器正在加载中，就不继续启动
                                if (Loader.Block && !IsFinished)
                                    Blocked = true;
                                break;
                            }
                        case ModBase.LoadState.Loading:
                            {
                                // 检查 LoaderTask 是否需要重启
                                if (GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderTask<,>)))
                                {
                                    if (Conversions.ToBoolean(((dynamic)((object)Loader)).ShouldStart(Input is not null && ReferenceEquals(Loader.GetType().GenericTypeArguments.First(), Input.GetType()) ? Input : null, IgnoreReloadTimeout: true)))
                                    {
                                        Logger.Warn($"由于输入条件变更，重启进行中的加载器 {Loader.Name}");
                                        goto default;
                                    }
                                }
                                // 已经有正在加载中的了，不需要再启动了
                                IsFinished = false;
                                Blocked = true;
                                break;
                            }

                        default:
                            {
                                // 原 VB 的 Restart 标签在重构为 goto default 后已无引用（CS0164），故删除该残留标签
                                // 未启动，则启动加载器
                                IsFinished = false;
                                if (Blocked)
                                    continue;
                                if (Input is not null)
                                {
                                    // 若输入类型与下一个加载器相同才继续
                                    if (GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderTask<,>)) || GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderCombo<>)))
                                    {
                                        Loader.Start(ReferenceEquals(Loader.GetType().GenericTypeArguments.First(), Input.GetType()) ? Input : null, IsForceRestarting);
                                    }
                                    else if (Loader is LoaderWorker)
                                    {
                                        Loader.Start(IsForceRestart: IsForceRestarting);
                                    }
                                    else if (Loader is ModNet.LoaderDownload)
                                    {
                                        Loader.Start(Input is List<ModNet.NetFile> ? Input : null, IsForceRestarting);
                                    }
                                    else
                                    {
                                        throw new Exception("未知的加载器类型（" + Loader.GetType().Name + "）");
                                    }
                                }
                                else
                                {
                                    Loader.Start(IsForceRestart: IsForceRestarting);
                                }
                                // 阻止继续
                                if (Loader.Block)
                                    Blocked = true;
                                break;
                            }
                    }
                }
                if (IsFinished)
                {
                    // 顺利完成，贼棒
                    RaisePreviewFinish();
                    State = ModBase.LoadState.Finished;
                    ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                }
            }

            /// <summary>
        /// 获得最底层的，应被显示给用户的加载器列表，并追加于 List。
        /// </summary>
            public static void GetLoaderList(object Loader, ref List<LoaderBase> List, bool RequireShow = true)
            {
                foreach (var SubLoader in (IEnumerable)((dynamic)Loader).Loaders)
                {
                    if (Conversions.ToBoolean(((dynamic)SubLoader).Show || !RequireShow))
                        List.Add((LoaderBase)SubLoader);
                    if (GeneralUtils.IsGenericInstanceOf(SubLoader, typeof(LoaderCombo<>)))
                        GetLoaderList(SubLoader, ref List);
                }
            }
            /// <summary>
        /// 获得最底层的，应被显示给用户的加载器列表，并追加于 List。
        /// </summary>
            public void GetLoaderList(ref List<LoaderBase> List, bool RequireShow = true)
            {
                GetLoaderList(this, ref List, RequireShow);
            }
            /// <summary>
        /// 获得最底层的，应被显示给用户的加载器列表。
        /// </summary>
            public List<LoaderBase> GetLoaderList(bool RequireShow = true)
            {
                var List = new List<LoaderBase>();
                GetLoaderList(ref List, RequireShow);
                return List;
            }

        }

        // 任务栏进度条
        public static ConcurrentList<LoaderBase> LoaderTaskbar = new ConcurrentList<LoaderBase>();
        public static double LoaderTaskbarProgress = 0d; // 平滑后的进度
        private static System.Windows.Shell.TaskbarItemProgressState LoaderTaskbarProgressLast = System.Windows.Shell.TaskbarItemProgressState.None;

        public static void LoaderTaskbarAdd<T>(LoaderCombo<T> Loader)
        {
            if (ModMain.FrmSpeedLeft is not null)
                ModMain.FrmSpeedLeft.TaskRemove(Loader);
            LoaderTaskbar.Add(Loader);
            Logger.Info($"{Loader.Name} 已加入任务列表");
        }
        public static void LoaderTaskbarProgressRefresh()
        {
            try
            {
                System.Windows.Shell.TaskbarItemProgressState NewState;
                double NewProgress = LoaderTaskbarProgressGet();
                // 若单个任务已中止，或全部任务已完成，则刷新并移除
                foreach (var Task in LoaderTaskbar)
                {
                    if (LoaderTaskbar.All(l => l.State != ModBase.LoadState.Loading) || Task.State == ModBase.LoadState.Waiting || Task.State == ModBase.LoadState.Canceled)
                    {
                        ModMain.FrmSpeedLeft?.TaskRefresh(Task);
                        LoaderTaskbar.Remove(Task);
                        Logger.Info($"{Task.Name} 已移出任务列表");
                    }
                }
                // 更新平滑后的进度
                if (NewProgress <= 0d || NewProgress >= 1d || LoaderTaskbarProgress > NewProgress)
                {
                    LoaderTaskbarProgress = NewProgress;
                }
                else
                {
                    LoaderTaskbarProgress = LoaderTaskbarProgress * 0.9d + NewProgress * 0.1d;
                }
                ModBase.RunInUi(() => ModMain.FrmMain.BtnExtraDownload.Progress = LoaderTaskbarProgress);
                // 更新任务栏信息
                if (!LoaderTaskbar.Any() || LoaderTaskbarProgress == 1d)
                {
                    NewState = System.Windows.Shell.TaskbarItemProgressState.None;
                }
                else if (LoaderTaskbarProgress < 0.015d)
                {
                    NewState = System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
                }
                else
                {
                    NewState = System.Windows.Shell.TaskbarItemProgressState.Normal;
                    ModMain.FrmMain.TaskbarItemInfo.ProgressValue = LoaderTaskbarProgress;
                }
                if (LoaderTaskbarProgressLast != NewState)
                {
                    LoaderTaskbarProgressLast = NewState;
                    ModMain.FrmMain.TaskbarItemInfo.ProgressState = NewState;
                    ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "刷新任务栏进度显示失败");
            }
        }
        public static double LoaderTaskbarProgressGet()
        {
            try
            {
                if (!LoaderTaskbar.Any())
                    return 1d;
                return LoaderTaskbar.Select(l => l.Progress).Average().Clamp(0d, 1d);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "获取任务栏进度出错");
                return 0.5d;
            }
        }

        // 文件夹刷新类委托
        private static Dictionary<LoaderBase, LoaderFolderDictionaryEntry> LoaderFolderDictionary = new Dictionary<LoaderBase, LoaderFolderDictionaryEntry>();
        private struct LoaderFolderDictionaryEntry
        {
            public DateTime? LastCheckTime;
            public string FolderPath;
            public override bool Equals(object obj)
            {
                if (!(obj is LoaderFolderDictionaryEntry))
                    return false;
                LoaderFolderDictionaryEntry entry = (LoaderFolderDictionaryEntry)obj;
                return EqualityComparer<DateTime?>.Default.Equals(LastCheckTime, entry.LastCheckTime) && (FolderPath ?? "") == (entry.FolderPath ?? "");
            }
        }
        public enum LoaderFolderRunType
        {
            RunOnUpdated,
            ForceRun,
            UpdateOnly
        }
        /// <summary>
    /// 执行以文件夹检测作为输入的加载器。加载器需以文件夹路径为输入值。
    /// 返回是否执行了加载器。
    /// </summary>
    /// <param name="ExtraPath">用于检查文件夹修改的额外路径。该路径不会传入加载器。</param>
        public static bool LoaderFolderRun(LoaderBase Loader, string FolderPath, LoaderFolderRunType Type, int MaxDepth = 0, string ExtraPath = "", bool WaitForExit = false)
        {
            var Value = new LoaderFolderDictionaryEntry() { FolderPath = FolderPath + ExtraPath, LastCheckTime = default };
            try
            {
                // 获取数据
                var FolderInfo = DirectoryUtils.GetInfo(FolderPath + ExtraPath);
                Value.LastCheckTime = FolderInfo.Exists ? global::PCL.ModLoader.GetActualLastWriteTimeUtc(FolderInfo, MaxDepth) : null;
                // 如果已经检查过，则跳过
                if (Type == LoaderFolderRunType.RunOnUpdated && LoaderFolderDictionary.ContainsKey(Loader))
                {
                    if (FolderInfo.Exists)
                    {
                        if (LoaderFolderDictionary[Loader].LastCheckTime is not null && Value.Equals(LoaderFolderDictionary[Loader]))
                            return false;
                    }
                    else if (LoaderFolderDictionary[Loader].LastCheckTime is null)
                        return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "文件夹加载器启动检测出错");
            }
            // 写入检查数据
            LoaderFolderDictionary[Loader] = Value;
            // 开始检查
            if (Type == LoaderFolderRunType.UpdateOnly)
                return false;
            if (WaitForExit)
            {
                Loader.WaitForExit(FolderPath, IsForceRestart: true);
            }
            else
            {
                Loader.Start(FolderPath, IsForceRestart: true);
            }
            return true;
        }
        private static DateTime GetActualLastWriteTimeUtc(DirectoryInfo FolderInfo, int MaxDepth)
        {
            var Time = FolderInfo.LastWriteTimeUtc;
            if (MaxDepth > 0)
            {
                foreach (var Folder in FolderInfo.EnumerateDirectories())
                {
                    var FolderTime = GetActualLastWriteTimeUtc(Folder, MaxDepth - 1);
                    if (FolderTime > Time)
                        Time = FolderTime;
                }
            }
            return Time;
        }

    }
}