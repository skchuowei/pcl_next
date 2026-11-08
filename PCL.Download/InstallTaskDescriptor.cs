namespace PCL.Download;

/// <summary>
/// 装配层需要构造的 <c>LoaderBase</c> 子类种类。
/// <para>
/// 与 VB <c>ModLoader.vb</c> 里的 <c>LoaderTask</c> / <c>LoaderCombo</c> / <c>LoaderDownload</c>
/// 一一对应，但这里只是枚举：描述符不引用任何 VB 类型，也不产出真正的加载器。
/// 见《计划-Phase4收尾与Phase6下线》§0.1 口径 A 裁决。
/// </para>
/// </summary>
public enum InstallLoaderKind
{
    /// <summary><c>LoaderTask</c>：一段执行 lambda。</summary>
    Task,

    /// <summary><c>LoaderCombo</c>：一组子加载器。</summary>
    Combo,

    /// <summary><c>LoaderDownload</c>：按 <c>NetFile</c> 清单下载。</summary>
    Download,
}

/// <summary>
/// 步骤如何响应取消。C# 只描述语义，实际判定仍由 VB 状态机与 <c>NetInterop</c> 的取消令牌执行。
/// </summary>
public enum InstallCancelSemantics
{
    /// <summary>无独立取消判定，随父加载器取消。</summary>
    Inherit,

    /// <summary>下载类步骤：由 <c>NetInterop</c> 的取消令牌协作取消，取消后 <c>DownloadDone</c> 归位。</summary>
    Cooperative,

    /// <summary>等待原版客户端加载器完成后再继续（OptiFine 方式 A/B 的「等待原版下载」）。</summary>
    WaitForClient,

    /// <summary>检测到条件不满足时主动 <c>Task.Cancel()</c>（如 NeoForge 不可下载判定）。</summary>
    CancelIfUnavailable,
}

/// <summary>
/// 「一个安装步骤要被装配成什么」的只读描述符。
/// <para>
/// 口径 A 的跨语言契约：C# 决定加载器种类、名称、权重、Block、Show、输入 / 输出资产、
/// 子加载器骨架、下载清单类别、取消与进度语义；VB 只按 <see cref="Kind"/> 分派构造
/// <c>LoaderBase</c> 并挂上执行 lambda，不做任何业务判定。
/// </para>
/// <para>
/// 只含基元 / 字符串 / 集合 / 枚举，不引用 <c>McInstance</c> / <c>MergeJson</c> /
/// <c>MyListItem</c> / <c>NetFile</c> / <c>LoaderBase</c>，保证内核自包含、可单测。
/// <see cref="Kind"/> 为 <c>null</c> 时表示这是复合体内部的子加载器，不属于顶层步骤。
/// </para>
/// </summary>
public sealed class InstallTaskDescriptor
{
    /// <summary>顶层步骤种类；子加载器为 <c>null</c>。</summary>
    public InstallStepKind? Kind { get; }

    /// <summary>展示名，与 VB 构造里的字面量逐字一致。</summary>
    public string Name { get; }

    /// <summary>要构造的加载器种类。</summary>
    public InstallLoaderKind LoaderKind { get; }

    /// <summary>计算总进度时的权重（预计耗时秒数）。</summary>
    public double ProgressWeight { get; }

    /// <summary>是否阻碍后续步骤开始。</summary>
    public bool Block { get; }

    /// <summary>是否显示在加载列表里。</summary>
    public bool Show { get; }

    /// <summary>执行该步骤所需的宿主资产名（数据依赖，不是 VB 类型）。</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>该步骤产出的宿主资产名。</summary>
    public IReadOnlyList<string> Outputs { get; }

    /// <summary>复合体内部的子加载器骨架；空表示由 <see cref="SubStepsSource"/> 提供。</summary>
    public IReadOnlyList<InstallTaskDescriptor> SubSteps { get; }

    /// <summary>该步骤涉及的下载文件类别，见 <see cref="NetFileDescriptorKind"/>。</summary>
    public IReadOnlyList<NetFileDescriptorKind> DownloadKinds { get; }

    /// <summary>取消语义。</summary>
    public InstallCancelSemantics Cancel { get; }

    /// <summary>执行 lambda 是否自行设置 <c>Task.Progress</c>（而非只由下载引擎推进）。</summary>
    public bool ReportsProgress { get; }

    /// <summary>
    /// 复合体内部子列表仍由宿主函数提供时，登记该 VB 函数名。
    /// P4-B 逐类下沉时按这份清单推进；<c>null</c> 表示子列表已由 <see cref="SubSteps"/> 完整描述。
    /// </summary>
    public string? SubStepsSource { get; }

    public InstallTaskDescriptor(
        InstallStepKind? kind,
        string name,
        InstallLoaderKind loaderKind,
        double progressWeight = 1,
        bool block = true,
        bool show = true,
        IReadOnlyList<string>? inputs = null,
        IReadOnlyList<string>? outputs = null,
        IReadOnlyList<InstallTaskDescriptor>? subSteps = null,
        IReadOnlyList<NetFileDescriptorKind>? downloadKinds = null,
        InstallCancelSemantics cancel = InstallCancelSemantics.Inherit,
        bool reportsProgress = false,
        string? subStepsSource = null)
    {
        Kind = kind;
        Name = name;
        LoaderKind = loaderKind;
        ProgressWeight = progressWeight;
        Block = block;
        Show = show;
        Inputs = inputs ?? Array.Empty<string>();
        Outputs = outputs ?? Array.Empty<string>();
        SubSteps = subSteps ?? Array.Empty<InstallTaskDescriptor>();
        DownloadKinds = downloadKinds ?? Array.Empty<NetFileDescriptorKind>();
        Cancel = cancel;
        ReportsProgress = reportsProgress;
        SubStepsSource = subStepsSource;
    }
}

/// <summary>
/// 把 <see cref="InstallPlanStep"/> 翻译成装配规格的纯工厂。
/// <para>
/// 所有常量（加载器种类、子加载器名称 / 权重 / Show、资产依赖、下载类别）逐字取自 VB
/// <c>ModDownloadLib.McInstallLoadersGet</c> 1609–1819 与 <c>McDownloadClientLoader</c> 9–69。
/// 这里<b>不重算</b>任何 Block / 权重——那些由 <see cref="InstallPlanner.Build"/> 决定后原样传入。
/// </para>
/// </summary>
public static class InstallTaskDescriptors
{
    /// <summary>为一个顶层步骤生成装配规格。</summary>
    public static InstallTaskDescriptor Create(
        InstallStepKind kind, string name, double progressWeight, bool block, bool show,
        InstallPlanRequest? request = null)
    {
        // request 可为 null（独立构造步骤时）；只用于挑 OptiFine 分流与 Client 可选子步骤，
        // 不参与任何 Block / 权重决策。
        var hasMinecraftJson = request?.HasMinecraftJson ?? false;
        var optiFineAsMod = request?.OptiFineAsMod ?? false;

        return kind switch
        {
            InstallStepKind.IgnoreMarker => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Task, progressWeight, block, show,
                inputs: ["VersionFolder"], outputs: [".pclignore"],
                cancel: InstallCancelSemantics.Inherit),

            InstallStepKind.FabricApiDownload => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Download, progressWeight, block, show,
                inputs: ["Request.FabricApi", "ModsTempFolder"],
                outputs: ["ManagedFileSources.FabricApi"],
                downloadKinds: [NetFileDescriptorKind.FabricApi],
                cancel: InstallCancelSemantics.Cooperative),

            InstallStepKind.OptiFabricDownload => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Download, progressWeight, block, show,
                inputs: ["Request.OptiFabric", "ModsTempFolder"],
                outputs: ["ManagedFileSources.OptiFabric"],
                downloadKinds: [NetFileDescriptorKind.OptiFabric],
                cancel: InstallCancelSemantics.Cooperative),

            InstallStepKind.Client => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Combo, progressWeight, block, show,
                inputs: ["Request.MinecraftJson", "VersionFolder"],
                outputs: ["ClientLoader"],
                subSteps: ClientSubSteps(hasMinecraftJson),
                downloadKinds: [
                    NetFileDescriptorKind.VanillaJson,
                    NetFileDescriptorKind.VanillaJar,
                    NetFileDescriptorKind.VanillaLibrary,
                    NetFileDescriptorKind.AssetIndex,
                    NetFileDescriptorKind.AssetObject,
                ],
                cancel: InstallCancelSemantics.Cooperative),

            InstallStepKind.OptiFine => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Combo, progressWeight, block, show,
                inputs: ["OptiFineEntry", "ClientLoader", "VersionFolder"],
                outputs: optiFineAsMod ? ["ManagedFileSources.OptiFine"] : Array.Empty<string>(),
                downloadKinds: [NetFileDescriptorKind.OptiFineInstaller],
                cancel: optiFineAsMod
                    ? InstallCancelSemantics.Cooperative
                    : InstallCancelSemantics.WaitForClient,
                reportsProgress: true,
                subStepsSource: optiFineAsMod
                    ? "McDownloadOptiFineSaveLoader"
                    : "McDownloadOptiFineLoader"),

            InstallStepKind.Forge => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Combo, progressWeight, block, show,
                inputs: ["ForgeVersion", "Request.ForgeEntry", "ClientLoader", "VersionFolder"],
                downloadKinds: [NetFileDescriptorKind.ForgeInstaller, NetFileDescriptorKind.VanillaLibrary],
                cancel: InstallCancelSemantics.Cooperative,
                reportsProgress: true,
                subStepsSource: "McDownloadForgelikeLoader(false)"),

            InstallStepKind.NeoForge => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Combo, progressWeight, block, show,
                inputs: ["NeoForgeVersion", "Request.NeoForgeEntry", "ClientLoader", "VersionFolder"],
                downloadKinds: [NetFileDescriptorKind.NeoForgeInstaller, NetFileDescriptorKind.VanillaLibrary],
                // 不可下载判定会 Task.Cancel()，见 McDownloadForgelikeLoader 内的 NeoForge 分支
                cancel: InstallCancelSemantics.CancelIfUnavailable,
                reportsProgress: true,
                subStepsSource: "McDownloadForgelikeLoader(true)"),

            InstallStepKind.LiteLoader => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Combo, progressWeight, block, show,
                inputs: ["Request.LiteLoaderEntry", "TempMcFolder"],
                // 合并安装里的 LiteLoader 只生成版本 json，不下 NetFile
                cancel: InstallCancelSemantics.Inherit,
                subStepsSource: "McDownloadLiteLoaderLoader"),

            InstallStepKind.Fabric => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Combo, progressWeight, block, show,
                inputs: ["FabricVersion", "TempMcFolder"],
                downloadKinds: [NetFileDescriptorKind.FabricMain],
                cancel: InstallCancelSemantics.Cooperative,
                subStepsSource: "McDownloadFabricLoader"),

            InstallStepKind.InstallGame => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Task, progressWeight, block, show,
                inputs: ["VersionFolder", "Layout", "McFolderSelected", "ModsTempFolder"],
                outputs: ["TargetFolder"],
                // lambda 里显式 Task.Progress = 0.2 / 0.8
                reportsProgress: true),

            InstallStepKind.PostLibraries => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Combo, progressWeight, block, show,
                inputs: ["VersionFolder"],
                subSteps: PostLibrariesSubSteps(),
                downloadKinds: [NetFileDescriptorKind.VanillaLibrary],
                cancel: InstallCancelSemantics.Cooperative),

            InstallStepKind.RecordManifest => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Task, progressWeight, block, show,
                inputs: ["TargetFolder", "ManagedFileSources", "VersionFolder"],
                outputs: ["McInstallManifest"]),

            InstallStepKind.RemoveIgnoreMarker => new InstallTaskDescriptor(
                kind, name, InstallLoaderKind.Task, progressWeight, block, show,
                inputs: ["VersionFolder"], outputs: [".pclignore"]),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的安装步骤种类"),
        };
    }

    /// <summary>
    /// VB <c>McDownloadClientLoader</c>（9–69 行）的子加载器骨架。
    /// <c>hasMinecraftJson</c> 为 false 时 VB 会额外插入「获取原版 json 文件下载地址」任务。
    /// </summary>
    private static IReadOnlyList<InstallTaskDescriptor> ClientSubSteps(bool hasMinecraftJson)
    {
        var steps = new List<InstallTaskDescriptor>(6);
        if (!hasMinecraftJson)
        {
            steps.Add(new InstallTaskDescriptor(null, "获取原版 json 文件下载地址",
                InstallLoaderKind.Task, 2, block: true, show: false,
                inputs: ["Id"], outputs: ["List(Of NetFile)"],
                downloadKinds: [NetFileDescriptorKind.VanillaJson]));
        }

        steps.Add(new InstallTaskDescriptor(null, "下载原版 json 文件",
            InstallLoaderKind.Download, 3,
            downloadKinds: [NetFileDescriptorKind.VanillaJson],
            cancel: InstallCancelSemantics.Cooperative));
        steps.Add(new InstallTaskDescriptor(null, "读取 json 文件",
            InstallLoaderKind.Task, 0, show: false,
            inputs: ["VersionFolder"], outputs: ["McInstance"]));

        // 支持库复合体：Block = False，权重 14，两个副加载器都不显示
        steps.Add(new InstallTaskDescriptor(null, "下载原版支持库文件",
            InstallLoaderKind.Combo, 14, block: false,
            subSteps: [
                new InstallTaskDescriptor(null, "分析原版支持库文件（副加载器）",
                    InstallLoaderKind.Task, 1, show: false,
                    inputs: ["McInstance"], outputs: ["List(Of NetFile)"]),
                new InstallTaskDescriptor(null, "下载原版支持库文件（副加载器）",
                    InstallLoaderKind.Download, 13, show: false,
                    downloadKinds: [NetFileDescriptorKind.VanillaLibrary],
                    cancel: InstallCancelSemantics.Cooperative),
            ],
            downloadKinds: [NetFileDescriptorKind.VanillaLibrary],
            cancel: InstallCancelSemantics.Cooperative));

        // 资源复合体：Block = False，权重 18，四个副加载器都不显示
        steps.Add(new InstallTaskDescriptor(null, "下载原版资源文件",
            InstallLoaderKind.Combo, 18, block: false,
            subSteps: [
                new InstallTaskDescriptor(null, "分析资源文件索引地址（副加载器）",
                    InstallLoaderKind.Task, 1, show: false,
                    inputs: ["McInstance"], outputs: ["List(Of NetFile)"]),
                new InstallTaskDescriptor(null, "下载资源文件索引（副加载器）",
                    InstallLoaderKind.Download, 3, show: false,
                    downloadKinds: [NetFileDescriptorKind.AssetIndex],
                    cancel: InstallCancelSemantics.Cooperative),
                new InstallTaskDescriptor(null, "分析所需资源文件（副加载器）",
                    InstallLoaderKind.Task, 0.01, show: false,
                    inputs: ["McInstance"], outputs: ["List(Of NetFile)"]),
                new InstallTaskDescriptor(null, "下载资源文件（副加载器）",
                    InstallLoaderKind.Download, 14, show: false,
                    downloadKinds: [NetFileDescriptorKind.AssetObject],
                    cancel: InstallCancelSemantics.Cooperative),
            ],
            downloadKinds: [NetFileDescriptorKind.AssetIndex, NetFileDescriptorKind.AssetObject],
            cancel: InstallCancelSemantics.Cooperative));

        return steps;
    }

    /// <summary>VB <c>McInstallLoadersGet</c> 1766–1770 行的「补全文件」复合体。</summary>
    private static IReadOnlyList<InstallTaskDescriptor> PostLibrariesSubSteps()
        =>
        [
            new InstallTaskDescriptor(null, "分析游戏支持库文件（副加载器）",
                InstallLoaderKind.Task, 1, show: false,
                inputs: ["VersionFolder"], outputs: ["List(Of NetFile)"]),
            new InstallTaskDescriptor(null, "下载游戏支持库文件（副加载器）",
                InstallLoaderKind.Download, 7, show: false,
                downloadKinds: [NetFileDescriptorKind.VanillaLibrary],
                cancel: InstallCancelSemantics.Cooperative),
        ];
}

/// <summary>
/// 口径 A 下 VB 装配层的入口：把编排计划投影为一组装配规格。
/// 装配层只读取这些描述符，不得重算 Block / 权重，也不得内联业务判定。
/// </summary>
public static class InstallTaskPlanner
{
    public static IReadOnlyList<InstallTaskDescriptor> Describe(InstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Steps.Select(step => step.Task).ToArray();
    }

    public static IReadOnlyList<InstallTaskDescriptor> Describe(InstallPlanRequest request)
        => Describe(InstallPlanner.Build(request));
}
