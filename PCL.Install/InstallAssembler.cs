namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 装配层：把 P4-A 的 <see cref="InstallPlan"/> 投影为可执行节点树。
/// <para>
/// 只做三件事：<b>投影</b>（原样搬运描述符字段）、<b>求值子步骤来源</b>、<b>校验</b>
/// （缺 handler / 缺下载清单 / 缺子步骤提供者 / 类别不符 → 立即抛出）。
/// 不做任何 IO、不拼 URL、不拼路径、不重算权重与 Block。
/// </para>
/// </summary>
public sealed class InstallAssembler
{
    private readonly InstallPlanRequest _request;
    private readonly IInstallAssetSource _assets;
    private readonly INetFileSource _files;
    private readonly InstallActionRegistry _actions;
    private readonly SubStepProviderRegistry _subSteps;

    public InstallAssembler(
        InstallPlanRequest request,
        IInstallAssetSource assets,
        INetFileSource files,
        InstallActionRegistry? actions = null,
        SubStepProviderRegistry? subSteps = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(files);
        _request = request;
        _assets = assets;
        _files = files;
        _actions = actions ?? new InstallActionRegistry();
        _subSteps = subSteps ?? new SubStepProviderRegistry();
    }

    /// <summary>装配整份计划。返回顺序与 <see cref="InstallPlan.Steps"/> 完全一致。</summary>
    public IReadOnlyList<InstallNode> Assemble(InstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var nodes = new List<InstallNode>(plan.Steps.Count);
        foreach (var step in plan.Steps) nodes.Add(BuildNode(step.Task));
        return nodes;
    }

    /// <summary>按单个描述符装配（供子步骤与后续阶段复用）。</summary>
    public InstallNode BuildNode(InstallTaskDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        IInstallStepAction? action = null;
        IReadOnlyList<NetFileDescriptor> files = [];
        IReadOnlyList<InstallNode> children = [];

        switch (descriptor.LoaderKind)
        {
            case InstallLoaderKind.Download:
                files = ResolveFiles(descriptor);
                break;

            case InstallLoaderKind.Task:
                action = _actions.Resolve(descriptor)
                    ?? throw new MissingInstallActionException(descriptor.Name);
                break;

            case InstallLoaderKind.Combo:
                children = ResolveChildren(descriptor);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.LoaderKind, "未知的加载器种类");
        }

        return new InstallNode(descriptor, action, files, children);
    }

    private IReadOnlyList<NetFileDescriptor> ResolveFiles(InstallTaskDescriptor descriptor)
    {
        var supplied = _files.Get(descriptor, _assets);
        if (supplied is null || supplied.Count == 0)
            throw new MissingNetFileSourceException(descriptor.Name);

        if (descriptor.DownloadKinds.Count > 0)
        {
            foreach (var file in supplied)
            {
                if (!descriptor.DownloadKinds.Contains(file.Kind))
                {
                    throw new NetFileKindMismatchException(
                        descriptor.Name,
                        string.Join(", ", descriptor.DownloadKinds),
                        file.Kind.ToString(),
                        file.LocalPath);
                }
            }
        }

        return supplied;
    }

    private IReadOnlyList<InstallNode> ResolveChildren(InstallTaskDescriptor descriptor)
    {
        IReadOnlyList<InstallTaskDescriptor> subSteps;

        if (descriptor.SubSteps.Count > 0)
        {
            subSteps = descriptor.SubSteps;
        }
        else if (descriptor.SubStepsSource is { } source)
        {
            if (!_subSteps.TryGet(source, out var provider))
                throw new MissingSubStepSourceException(descriptor.Name, source, "PB1/PB2 仅内置内联骨架。");

            subSteps = provider.Describe(_request);
            if (subSteps.Count == 0)
                throw new MissingSubStepSourceException(descriptor.Name, source, "提供者返回了空子列表。");
        }
        else
        {
            throw new MissingSubStepSourceException(descriptor.Name, null, "该复合体必须由宿主登记子步骤。");
        }

        var children = new List<InstallNode>(subSteps.Count);
        foreach (var subStep in subSteps) children.Add(BuildNode(subStep));
        return children;
    }
}
