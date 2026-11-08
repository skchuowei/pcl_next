namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 装配产物：一棵只读的可执行节点树。
/// <para>
/// 所有决策字段都是 <see cref="InstallTaskDescriptor"/> 的**原样投影**，
/// 装配层不持有、也不重算任何 Block / 权重 / Show / 顺序。
/// 需要单测锁住「投影 == 描述符」这一点（见 <c>PCL.Install.Tests/AssemblyTests.cs</c>）。
/// </para>
/// </summary>
public sealed class InstallNode
{
    internal InstallNode(
        InstallTaskDescriptor descriptor,
        IInstallStepAction? action,
        IReadOnlyList<NetFileDescriptor> files,
        IReadOnlyList<InstallNode> children)
    {
        Descriptor = descriptor;
        Action = action;
        Files = files;
        Children = children;
    }

    /// <summary>原始装配规格（单一真源）。</summary>
    public InstallTaskDescriptor Descriptor { get; }

    public InstallStepKind? Kind => Descriptor.Kind;

    public string Name => Descriptor.Name;

    public InstallLoaderKind LoaderKind => Descriptor.LoaderKind;

    public double ProgressWeight => Descriptor.ProgressWeight;

    public bool Block => Descriptor.Block;

    public bool Show => Descriptor.Show;

    public InstallCancelSemantics Cancel => Descriptor.Cancel;

    public bool ReportsProgress => Descriptor.ReportsProgress;

    public IReadOnlyList<string> Inputs => Descriptor.Inputs;

    public IReadOnlyList<string> Outputs => Descriptor.Outputs;

    public IReadOnlyList<NetFileDescriptorKind> DownloadKinds => Descriptor.DownloadKinds;

    /// <summary>宿主提供的真实下载清单；非下载节点为空。</summary>
    public IReadOnlyList<NetFileDescriptor> Files { get; }

    /// <summary>Task 节点的执行实现；非 Task 节点为 <c>null</c>。</summary>
    public IInstallStepAction? Action { get; }

    /// <summary>复合体的子加载器（已递归装配）。</summary>
    public IReadOnlyList<InstallNode> Children { get; }

    /// <summary>只统计叶子（Task / Download）的权重合计；用于总进度折算，避免复合体与其子节点重复计权。</summary>
    internal double LeafWeightSum()
    {
        if (LoaderKind is InstallLoaderKind.Task or InstallLoaderKind.Download) return ProgressWeight;
        double sum = 0;
        foreach (var child in Children) sum += child.LeafWeightSum();
        return sum;
    }
}
