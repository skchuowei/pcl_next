using System.Runtime.CompilerServices;

namespace PCL.Download.Tests;

/// <summary>
/// 进程级测试初始化。
/// <para>
/// 存在的理由只有一条：把线程池的最小线程数抬上去，避免「用同步阻塞模拟慢源」的用例
/// 在小核数 runner 上被线程池饥饿判成失败。
/// </para>
/// <para>
/// 背景（2026-11-07）：<see cref="SourceRace"/> 用 <c>Task.Run(Fetch)</c> 派发候选源，
/// 而测试里的「慢源」是用 <c>Thread.Sleep</c> 模拟的（<see cref="SourceRaceCandidate{T}"/>
/// 的 <c>Fetch</c> 是 <c>Func&lt;T&gt;</c>，同步签名，没法 await）。线程池的最小线程数默认等于核数，
/// Linux 门禁的 runner 只有 2~4 核，于是慢源先把线程占住 400ms，本该立刻完成的快源反而在队列里
/// 排队——先完成者胜的规则没变，是快源压根没被调度起来。
/// </para>
/// <para>
/// 表现出来的样子极具误导性：同一份代码在开发机（多核，线程池从不饥饿）上全绿，在 CI 上稳定红，
/// 而且每次红的用例还不一样（谁先被饿到谁红）。真实网络请求是异步 I/O，不占线程，产品侧没有这个问题，
/// 所以这是测试夹具的失真，不该靠改产品代码来绕。
/// </para>
/// </summary>
internal static class TestSetup
{
    /// <summary>最小工作线程数与最小 I/O 线程数。</summary>
    private const int MinThreads = 64;

    [ModuleInitializer]
    internal static void Initialize()
    {
        // 只在当前值更低时抬高，避免覆盖宿主（如 dotnet test 的 runner）已经调过的设置。
        ThreadPool.GetMinThreads(out var worker, out var io);
        if (worker < MinThreads || io < MinThreads)
            ThreadPool.SetMinThreads(Math.Max(worker, MinThreads), Math.Max(io, MinThreads));
    }
}
