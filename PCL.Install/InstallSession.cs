namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 装配与执行所需的宿主注入项。内核只认接口，不认具体 UI / CLI 类型。
/// </summary>
public sealed class InstallOptions
{
    /// <summary>宿主资产（键名与描述符 Inputs / Outputs 一致）。</summary>
    public required IInstallAssetSource Assets { get; init; }

    /// <summary>每个下载节点的真实文件清单。</summary>
    public required INetFileSource Files { get; init; }

    /// <summary>下载引擎（PCL.Download 的实现）。</summary>
    public required IDownloadEngine Engine { get; init; }

    public InstallActionRegistry Actions { get; init; } = new();

    public SubStepProviderRegistry SubSteps { get; init; } = new();

    public DownloadOptions? DownloadOptions { get; init; }
}

/// <summary>
/// 一次安装会话：决策（P4-A 计划）+ 装配产物（节点树）+ 执行器。
/// <para>
/// 会话本身不持有 UI 类型，进度通过 <see cref="IProgress{T}"/> 外流，
/// 宿主可以是 Avalonia、CLI 或无头 selftest。
/// </para>
/// </summary>
public sealed class InstallSession
{
    private readonly InstallRunner _runner;

    internal InstallSession(
        InstallPlanRequest request, InstallPlan plan, IReadOnlyList<InstallNode> nodes, InstallRunner runner)
    {
        Request = request;
        Plan = plan;
        Nodes = nodes;
        _runner = runner;
    }

    public InstallPlanRequest Request { get; }

    /// <summary>P4-A 产出的编排计划（只读；装配层不修改它）。</summary>
    public InstallPlan Plan { get; }

    /// <summary>装配出的顶层节点，顺序与 <see cref="InstallPlan.Steps"/> 一致。</summary>
    public IReadOnlyList<InstallNode> Nodes { get; }

    public Task<InstallRunReport> RunAsync(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _runner.RunAsync(progress, cancellationToken);
}

/// <summary>
/// 装配层入口：<see cref="InstallPlanRequest"/> → P4-A 计划 → 节点树 → 可执行会话。
/// </summary>
public static class InstallSessionFactory
{
    public static InstallSession Create(InstallPlanRequest request, InstallOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var plan = InstallPlanner.Build(request);
        var assembler = new InstallAssembler(
            request, options.Assets, options.Files, options.Actions, options.SubSteps);
        var nodes = assembler.Assemble(plan);
        var runner = new InstallRunner(nodes, options.Assets, options.Engine, options.DownloadOptions);
        return new InstallSession(request, plan, nodes, runner);
    }
}
