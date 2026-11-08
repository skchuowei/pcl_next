namespace PCL.Install.Tests;

using PCL.Download;

public sealed class RunnerTests
{
    private static (IReadOnlyList<InstallNode> Nodes, FakeEngine Engine, List<string> Events) Setup(
        TimeSpan actionDelay,
        IInstallAssetSource? assets = null,
        bool downloadSteps = false)
    {
        var request = new InstallPlanRequest { MinecraftVersion = "1.20.1" };
        var steps = downloadSteps
            ? new List<InstallPlanStep>
            {
                new(InstallStepKind.IgnoreMarker, "写入忽略标记", 1, block: true, show: false),
                new(InstallStepKind.FabricApiDownload, "下载 Fabric API", 2, block: false, show: true),
                new(InstallStepKind.OptiFabricDownload, "下载 OptiFabric", 3, block: true, show: true),
            }
            : new List<InstallPlanStep>
            {
                new(InstallStepKind.IgnoreMarker, "步骤A", 1, block: true, show: false),
                new(InstallStepKind.InstallGame, "步骤B", 2, block: false, show: true),
                new(InstallStepKind.RecordManifest, "步骤C", 3, block: true, show: true),
            };
        var plan = new InstallPlan(steps);
        plan.AttachDescriptors(request);

        var events = new List<string>();
        var actions = new InstallActionRegistry
        {
            Fallback = new RecordingAction(events, actionDelay),
        };
        var assembler = new InstallAssembler(
            request, assets ?? new AllPresentAssets(), new FakeFileSource(), actions);
        var nodes = assembler.Assemble(plan);
        return (nodes, new FakeEngine(), events);
    }

    [Fact]
    public async Task 阻塞步骤会等待在它之前的非阻塞步骤完成()
    {
        var (nodes, engine, events) = Setup(TimeSpan.FromMilliseconds(60));
        var runner = new InstallRunner(nodes, new AllPresentAssets(), engine);

        await runner.RunAsync();

        Assert.Equal(
            [
                "start:步骤A", "end:步骤A",
                "start:步骤B", "end:步骤B",
                "start:步骤C", "end:步骤C",
            ],
            events);
    }

    [Fact]
    public async Task 缺宿主资产时执行前抛出且不执行任何动作()
    {
        var (nodes, engine, events) = Setup(TimeSpan.Zero, assets: new InstallAssetBag());
        var runner = new InstallRunner(nodes, new InstallAssetBag(), engine);

        var ex = await Assert.ThrowsAsync<MissingInstallAssetException>(() => runner.RunAsync());

        Assert.Equal("步骤A", ex.StepName);
        Assert.Equal("VersionFolder", ex.AssetName);
        Assert.Empty(events);
    }

    [Fact]
    public async Task 取消后不再启动任何节点()
    {
        var (nodes, engine, events) = Setup(TimeSpan.Zero);
        var runner = new InstallRunner(nodes, new AllPresentAssets(), engine);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(null, cts.Token));

        Assert.Empty(events);
        Assert.Equal(0, engine.CallCount);
    }

    [Fact]
    public async Task 进度单调不减且最终为一()
    {
        var (nodes, engine, _) = Setup(TimeSpan.FromMilliseconds(30));
        var runner = new InstallRunner(nodes, new AllPresentAssets(), engine);
        var values = new List<double>();
        var progress = new SyncProgress<InstallProgress>(p => values.Add(p.OverallProgress));

        var report = await runner.RunAsync(progress);

        Assert.NotEmpty(values);
        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i] >= values[i - 1], $"进度出现回退：{values[i - 1]} → {values[i]}");
        Assert.All(values, v => Assert.InRange(v, 0.0, 1.0));
        Assert.Equal(1.0, report.OverallProgress);
    }

    [Fact]
    public async Task 下载节点把清单交给引擎并累计字节()
    {
        var (nodes, engine, _) = Setup(TimeSpan.Zero, downloadSteps: true);
        var runner = new InstallRunner(nodes, new AllPresentAssets(), engine);

        var report = await runner.RunAsync();

        Assert.Equal(2, engine.CallCount);
        Assert.Equal(2, engine.Requests.Count);
        Assert.Equal(200, report.TotalBytesDownloaded);
    }

    [Fact]
    public async Task 会话工厂端到端跑完原版计划()
    {
        var events = new List<string>();
        var request = new InstallPlanRequest { MinecraftVersion = "1.20.1" };
        var options = new InstallOptions
        {
            Assets = new AllPresentAssets(),
            Files = new FakeFileSource(),
            Engine = new FakeEngine(),
            Actions = new InstallActionRegistry { Fallback = new RecordingAction(events) },
        };

        var session = InstallSessionFactory.Create(request, options);
        var report = await session.RunAsync();

        Assert.NotEmpty(session.Nodes);
        Assert.Equal(1.0, report.OverallProgress);
        Assert.Contains("end:" + session.Nodes[0].Name, events);
        Assert.True(report.TotalBytesDownloaded > 0);
    }
}

/// <summary>同步回调的进度适配器（<see cref="Progress{T}"/> 在无同步上下文时会把回调丢到线程池）。</summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SyncProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
