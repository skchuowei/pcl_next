namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 宿主资产的只读访问入口。键名与 <see cref="InstallTaskDescriptor.Inputs"/> /
/// <see cref="InstallTaskDescriptor.Outputs"/> 里的字符串逐字一致。
/// <para>
/// 「键存在、值为 null」也算存在——对齐 VB 里变量已声明但为 <c>Nothing</c> 的情形。
/// 缺失（键不存在）时由装配 / 执行层 fail-fast，不静默跳过。
/// </para>
/// </summary>
public interface IInstallAssetSource
{
    bool TryGet(string name, out object? value);
}

/// <summary>
/// 每个节点「真正要下载哪些文件」的提供者。
/// <para>
/// P4-A 的描述符只给 <see cref="InstallTaskDescriptor.DownloadKinds"/>（类别），
/// 真实 urls / 落盘路径 / hash / size 来自宿主（资源对象、镜像解析、版本 json）。
/// 装配层不拼 URL、不拼路径。
/// </para>
/// </summary>
public interface INetFileSource
{
    /// <summary>
    /// 取该节点的下载文件清单。返回 <c>null</c> 或空集合视为「宿主未提供」，
    /// 由 <see cref="InstallAssembler"/> 抛 <see cref="MissingNetFileSourceException"/>。
    /// </summary>
    IReadOnlyList<NetFileDescriptor>? Get(InstallTaskDescriptor descriptor, IInstallAssetSource assets);
}

/// <summary>
/// 单步执行实现。下载类节点的实现由 <see cref="DownloadStepAction"/> 提供；
/// 其余 Task 节点由宿主或后续阶段（PB3 / PB4）注册。
/// </summary>
public interface IInstallStepAction
{
    Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 复合体子列表的 C# 提供者。
/// <para>
/// P4-A 在 6 处描述符上登记了 <see cref="InstallTaskDescriptor.SubStepsSource"/>（原 VB 函数名）。
/// Linux 没有 VB，这些名字改由本接口的注册项兑现；未注册即视为缺装配（PB1 阶段全部未注册）。
/// </para>
/// </summary>
public interface ISubStepProvider
{
    IReadOnlyList<InstallTaskDescriptor> Describe(InstallPlanRequest request);
}

/// <summary>
/// 执行单个节点的上下文：只读节点 + 宿主资产 + 下载引擎 + 进度出口。
/// </summary>
public sealed class InstallStepContext
{
    public required InstallNode Node { get; init; }

    public required IInstallAssetSource Assets { get; init; }

    public required IDownloadEngine Engine { get; init; }

    public DownloadOptions? DownloadOptions { get; init; }

    /// <summary>步骤内部进度（0~1）；为 <c>null</c> 表示宿主不关心进度。</summary>
    public IProgress<InstallStepProgress>? Progress { get; init; }

    /// <summary>上报本次已下载字节数（由执行器累计，供最终报告使用）。</summary>
    public Action<long>? AddDownloadedBytes { get; init; }

    /// <summary>短名，便于动作实现写日志。</summary>
    public string Name => Node.Name;

    /// <summary>顶层步骤种类；子加载器为 <c>null</c>。</summary>
    public InstallStepKind? Kind => Node.Kind;

    /// <summary>该节点可用的下载文件清单（非下载节点为空）。</summary>
    public IReadOnlyList<NetFileDescriptor> Files => Node.Files;

    /// <summary>
    /// 按名取宿主资产并校验类型。缺失抛 <see cref="MissingInstallAssetException"/>，
    /// 类型不符抛 <see cref="InstallAssetTypeException"/>——两者都不静默降级。
    /// </summary>
    public T GetAsset<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Assets.TryGet(name, out var value))
            throw new MissingInstallAssetException(Name, name);
        if (value is not T typed)
            throw new InstallAssetTypeException(Name, name, typeof(T), value?.GetType());
        return typed;
    }

    internal InstallStepContext WithProgress(IProgress<InstallStepProgress>? progress)
        => new()
        {
            Node = Node,
            Assets = Assets,
            Engine = Engine,
            DownloadOptions = DownloadOptions,
            Progress = progress,
            AddDownloadedBytes = AddDownloadedBytes,
        };
}

/// <summary>单个步骤的进度快照。</summary>
public sealed class InstallStepProgress
{
    /// <summary>步骤内部进度（0~1）。</summary>
    public required double Fraction { get; init; }

    /// <summary>已下载字节数。</summary>
    public long BytesDownloaded { get; init; }

    /// <summary>预计总字节数；未知为 -1。</summary>
    public long BytesExpected { get; init; } = -1;

    /// <summary>当前速度（Byte/s）。</summary>
    public long Speed { get; init; }
}

/// <summary>整次安装的进度快照（由权重折算后的总进度）。</summary>
public sealed class InstallProgress
{
    /// <summary>当前步骤展示名。</summary>
    public required string StepName { get; init; }

    /// <summary>总进度（0~1），按 P4-A 描述符权重折算。</summary>
    public required double OverallProgress { get; init; }

    /// <summary>当前步骤内部进度（0~1）。</summary>
    public double StepProgress { get; init; }

    public long BytesDownloaded { get; init; }

    public long BytesExpected { get; init; } = -1;

    public long Speed { get; init; }
}

/// <summary>一次安装执行的汇总。</summary>
public sealed class InstallRunReport
{
    /// <summary>按完成顺序记录已执行的节点名（含复合体与其子节点）。</summary>
    public required IReadOnlyList<string> CompletedSteps { get; init; }

    public required double OverallProgress { get; init; }

    public required long TotalBytesDownloaded { get; init; }

    public required TimeSpan Elapsed { get; init; }
}
