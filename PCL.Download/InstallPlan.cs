namespace PCL.Download;

/// <summary>
/// 合并安装时，顶层「步骤」的种类。
/// <para>
/// 与 VB <c>ModDownloadLib.McInstallLoadersGet</c> 里逐个 <c>LoaderCombo</c> / <c>LoaderDownload</c> /
/// <c>LoaderTask</c> 的构造一一对应。C# 只负责决定「有哪些步骤、以什么顺序、带什么权重 /
/// Block / Show」，真正的 <c>LoaderBase</c> 构造与执行 lambda 仍留在 VB（见 §4.9① 的桥接约束：
/// 不动 <c>LoaderBase</c> 签名，不搬含 <c>McInstance</c> / <c>MergeJson</c> 的 lambda）。
/// </para>
/// </summary>
public enum InstallStepKind
{
    /// <summary>最先执行的「添加忽略标识」任务。</summary>
    IgnoreMarker,

    /// <summary>下载 Fabric API（作为 Mod 依赖）。</summary>
    FabricApiDownload,

    /// <summary>下载 OptiFabric（作为 Mod 依赖）。</summary>
    OptiFabricDownload,

    /// <summary>原版客户端下载（含 json / 支持库 / 资源的复合加载器）。</summary>
    Client,

    /// <summary>OptiFine 下载（按是否作为 Mod 分流到不同的子加载器）。</summary>
    OptiFine,

    /// <summary>Forge 下载。</summary>
    Forge,

    /// <summary>NeoForge 下载。</summary>
    NeoForge,

    /// <summary>LiteLoader 下载。</summary>
    LiteLoader,

    /// <summary>Fabric 下载。</summary>
    Fabric,

    /// <summary>合并 json + 迁移文件 + 建 Mod / 资源包目录的「安装游戏」任务。</summary>
    InstallGame,

    /// <summary>安装完成后的「下载游戏支持库文件」复合加载器。</summary>
    PostLibraries,

    /// <summary>写安装清单（<c>McInstallManifest</c>）的「记录安装清单」任务。</summary>
    RecordManifest,

    /// <summary>收尾的「删除忽略标识」任务（仅非中间产物输出时存在）。</summary>
    RemoveIgnoreMarker,
}

/// <summary>
/// 合并安装顶层步骤的只读投影：仅携带编排所需的纯数据，不含任何执行逻辑。
/// <para>
/// 口径 A 裁决后，每个步骤还挂一份 <see cref="InstallTaskDescriptor"/> 装配规格。
/// 装配规格由 <see cref="InstallPlan.AttachDescriptors"/> 在 <c>Build</c> 末尾统一附加，
/// 这样本构造函数与 dev.6 已有的 VB 调用方零改动。
/// </para>
/// </summary>
public sealed class InstallPlanStep
{
    private InstallTaskDescriptor? _task;

    /// <summary>步骤种类，VB 侧据此分派构造对应的 <c>LoaderBase</c>。</summary>
    public InstallStepKind Kind { get; }

    /// <summary>展示名（与 VB 原构造里的字面量一致，如 <c>"下载 Forge 41.2.8"</c>）。</summary>
    public string Name { get; }

    /// <summary>计算总进度时的权重（预计耗时秒数）。</summary>
    public double ProgressWeight { get; }

    /// <summary>该步骤是否会阻碍后续步骤开始。</summary>
    public bool Block { get; }

    /// <summary>该步骤是否显示在加载列表里。</summary>
    public bool Show { get; }

    /// <summary>该步骤的装配规格（加载器种类、槽位、子步骤、下载类别、取消与进度语义）。</summary>
    public InstallTaskDescriptor Task
        => _task ??= InstallTaskDescriptors.Create(Kind, Name, ProgressWeight, Block, Show);

    /// <summary>执行该步骤所需的宿主资产名。</summary>
    public IReadOnlyList<string> Inputs => Task.Inputs;

    /// <summary>该步骤产出的宿主资产名。</summary>
    public IReadOnlyList<string> Outputs => Task.Outputs;

    /// <summary>复合体内部的子加载器骨架。</summary>
    public IReadOnlyList<InstallTaskDescriptor> SubSteps => Task.SubSteps;

    /// <summary>该步骤涉及的下载文件类别。</summary>
    public IReadOnlyList<NetFileDescriptorKind> DownloadKinds => Task.DownloadKinds;

    /// <summary>要构造的加载器种类。</summary>
    public InstallLoaderKind LoaderKind => Task.LoaderKind;

    /// <summary>取消语义。</summary>
    public InstallCancelSemantics Cancel => Task.Cancel;

    /// <summary>执行 lambda 是否自行设置 <c>Task.Progress</c>。</summary>
    public bool ReportsProgress => Task.ReportsProgress;

    /// <summary>子列表仍由宿主 VB 函数提供时登记的函数名。</summary>
    public string? SubStepsSource => Task.SubStepsSource;

    public InstallPlanStep(InstallStepKind kind, string name, double progressWeight, bool block, bool show)
    {
        Kind = kind;
        Name = name;
        ProgressWeight = progressWeight;
        Block = block;
        Show = show;
    }

    /// <summary>由 <see cref="InstallPlan.AttachDescriptors"/> 调用，注入带请求上下文的装配规格。</summary>
    internal void Attach(InstallTaskDescriptor task)
        => _task = task ?? throw new ArgumentNullException(nameof(task));
}

/// <summary>
/// <c>InstallPlanner.Build</c> 的纯输入：从 VB <c>McInstallRequest</c> 里抽取、与执行无关的「是否存在 /
/// 选了什么」事实。刻意只用基元与可空字符串，不引用任何 VB 类型，保证内核自包含、可单测。
/// </summary>
public sealed class InstallPlanRequest
{
    /// <summary>目标 MC 版本。</summary>
    public required string MinecraftVersion { get; set; }

    /// <summary>Forge 版本串，未选择时为 <c>null</c>。</summary>
    public string? ForgeVersion { get; set; }

    /// <summary>NeoForge 版本串，未选择时为 <c>null</c>。</summary>
    public string? NeoForgeVersion { get; set; }

    /// <summary>LiteLoader 版本串，未选择时为 <c>null</c>。</summary>
    public string? LiteLoaderVersion { get; set; }

    /// <summary>Fabric 版本串，未选择时为 <c>null</c>。</summary>
    public string? FabricVersion { get; set; }

    /// <summary>是否选择了 OptiFine 条目（不看是否作为 Mod）。</summary>
    public bool HasOptiFineEntry { get; set; }

    /// <summary>OptiFine 展示名，用于步骤命名；未选择时传 <c>null</c>。</summary>
    public string? OptiFineDisplayName { get; set; }

    /// <summary>OptiFine 是否以 Mod 形式安装。</summary>
    public bool OptiFineAsMod { get; set; }

    /// <summary>是否选择了 Fabric API（作为依赖下载）。</summary>
    public bool HasFabricApi { get; set; }

    /// <summary>是否选择了 OptiFabric（作为依赖下载）。</summary>
    public bool HasOptiFabric { get; set; }

    /// <summary>
    /// 是否存在 Forge 条目（<c>Request.ForgeEntry</c> 非 null）。
    /// 注意这与 <see cref="ForgeVersion"/> 不同：版本串来自 <c>ModLoaderVersions</c>，
    /// 条目来自 <c>Request.ForgeEntry</c>，二者独立决定不同步骤的 Block 判定。
    /// </summary>
    public bool HasForgeEntry { get; set; }

    /// <summary>是否存在 NeoForge 条目（<c>Request.NeoForgeEntry</c> 非 null）。</summary>
    public bool HasNeoForgeEntry { get; set; }

    /// <summary>是否存在 LiteLoader 条目（<c>Request.LiteLoaderEntry</c> 非 null）。</summary>
    public bool HasLiteLoaderEntry { get; set; }

    /// <summary>是否存在任意 Mod 加载器（对齐 VB <c>ModLoaderVersions.Any</c>）。</summary>
    public bool HasAnyModLoader { get; set; }

    /// <summary>本次安装是否为「中间产物输出」（影响是否删除忽略标识）。</summary>
    public bool IsIntermediateOutput { get; set; }

    /// <summary>
    /// 调用方是否已提供原版版本 json 地址（<c>Request.MinecraftJson</c> 非 <c>Nothing</c>）。
    /// 为 false 时 VB <c>McDownloadClientLoader</c> 会额外插入「获取原版 json 文件下载地址」任务，
    /// 因此会影响 <see cref="InstallTaskDescriptor.SubSteps"/> 的骨架，但不影响顶层步骤决策。
    /// P4-B 接线时须由 VB 显式传入；默认 false 与当前 VB 调用最保守的形态一致。
    /// </summary>
    public bool HasMinecraftJson { get; set; }
}

/// <summary>
/// 合并安装的顶层编排计划：有序步骤列表。
/// <para>
/// 平移自 VB <c>ModDownloadLib.McInstallLoadersGet</c> 的步骤决策段（约 1611–1773 行）。
/// 只搬「纯决策」（步骤包含 / 顺序 / 权重 / Block / Show），不搬任何 <c>LoaderBase</c> 构造或
/// 含 VB 资产的 lambda。逐字对齐原实现里的 <c>If … IsNot Nothing</c> 与 <c>Block</c> 布尔式，
/// 否则合并顺序 / 阻塞关系一变，启动时才暴露（装到一半卡住或顺序错乱）。
/// </para>
/// </summary>
public sealed class InstallPlan
{
    /// <summary>有序的顶层步骤。顺序即执行顺序。</summary>
    public IReadOnlyList<InstallPlanStep> Steps { get; }

    public InstallPlan(IReadOnlyList<InstallPlanStep> steps)
    {
        Steps = steps;
    }

    /// <summary>
    /// 为每个步骤注入带请求上下文的装配规格（口径 A 的跨语言契约）。
    /// <para>
    /// 只有 <c>OptiFine</c> 的分流与 <c>Client</c> 的可选子步骤依赖请求，
    /// 顺序 / 权重 / Block / Show 全部照抄步骤本身，不在这里重算。
    /// </para>
    /// </summary>
    public void AttachDescriptors(InstallPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var step in Steps)
        {
            step.Attach(InstallTaskDescriptors.Create(
                step.Kind, step.Name, step.ProgressWeight, step.Block, step.Show, request));
        }
    }

    /// <summary>
    /// 按种类取步骤；该步骤未包含时返回 <c>null</c>。
    /// VB 侧在对应的 <c>If 组件已选</c> 分支里调用，故通常能取到；取不到说明调用方与计划不一致。
    /// </summary>
    public InstallPlanStep? GetStep(InstallStepKind kind)
    {
        foreach (var step in Steps)
        {
            if (step.Kind == kind) return step;
        }
        return null;
    }
}

/// <summary>
/// 合并安装顶层编排的纯决策层。输入 <see cref="InstallPlanRequest"/>，输出 <see cref="InstallPlan"/>。
/// </summary>
public static class InstallPlanner
{
    /// <summary>
    /// 计算合并安装的顶层步骤列表（含顺序、权重、Block、Show）。
    /// 逻辑逐字对齐 VB <c>McInstallLoadersGet</c>。
    /// </summary>
    public static InstallPlan Build(InstallPlanRequest request)
    {
        var steps = new List<InstallPlanStep>();
        bool modable = request.HasAnyModLoader;

        // 添加忽略标识（原 LoaderTask，VB 仅设 Show = False，其余取 LoaderBase 默认值）
        steps.Add(new InstallPlanStep(InstallStepKind.IgnoreMarker, "添加忽略标识", 1, block: true, show: false));

        // Fabric API
        if (request.HasFabricApi)
        {
            steps.Add(new InstallPlanStep(InstallStepKind.FabricApiDownload, "下载 Fabric API", 3, block: false, show: true));
        }

        // OptiFabric
        if (request.HasOptiFabric)
        {
            steps.Add(new InstallPlanStep(InstallStepKind.OptiFabricDownload, "下载 OptiFabric", 3, block: false, show: true));
        }

        // 原版客户端
        // Block = Not Modable AndAlso OptiFineEntry Is Nothing
        bool clientBlock = !modable && !request.HasOptiFineEntry;
        steps.Add(new InstallPlanStep(InstallStepKind.Client, "下载原版 " + request.MinecraftVersion, 39, clientBlock, show: false));

        // OptiFine
        if (request.HasOptiFineEntry)
        {
            // ProgressWeight = If(Modable, 16, 24)；Block = Not Modable
            double optiFineWeight = modable ? 16 : 24;
            steps.Add(new InstallPlanStep(InstallStepKind.OptiFine,
                "下载 OptiFine " + (request.OptiFineDisplayName ?? ""), optiFineWeight, block: !modable, show: false));
        }

        // Forge
        if (request.ForgeVersion is not null)
        {
            // Block = FabricVersion Is Nothing AndAlso Request.LiteLoaderEntry Is Nothing AndAlso Request.NeoForgeEntry Is Nothing
            bool forgeBlock = request.FabricVersion is null && !request.HasLiteLoaderEntry && !request.HasNeoForgeEntry;
            steps.Add(new InstallPlanStep(InstallStepKind.Forge, "下载 Forge " + request.ForgeVersion, 25, forgeBlock, show: false));
        }

        // NeoForge
        if (request.NeoForgeVersion is not null)
        {
            // Block = Request.ForgeEntry Is Nothing AndAlso FabricVersion Is Nothing AndAlso Request.LiteLoaderEntry Is Nothing
            bool neoForgeBlock = !request.HasForgeEntry && request.FabricVersion is null && !request.HasLiteLoaderEntry;
            steps.Add(new InstallPlanStep(InstallStepKind.NeoForge, "下载 NeoForge " + request.NeoForgeVersion, 25, neoForgeBlock, show: false));
        }

        // LiteLoader
        if (request.LiteLoaderVersion is not null)
        {
            // Block = FabricVersion Is Nothing
            bool liteLoaderBlock = request.FabricVersion is null;
            steps.Add(new InstallPlanStep(InstallStepKind.LiteLoader, "下载 LiteLoader " + request.MinecraftVersion, 1, liteLoaderBlock, show: false));
        }

        // Fabric
        if (request.FabricVersion is not null)
        {
            // Block 恒为 True
            steps.Add(new InstallPlanStep(InstallStepKind.Fabric, "下载 Fabric " + request.FabricVersion, 2, block: true, show: false));
        }

        // 安装游戏（原 LoaderTask，VB 设 Block = True，Show 取默认值 True）
        steps.Add(new InstallPlanStep(InstallStepKind.InstallGame, "安装游戏", 2, block: true, show: true));

        // 下载游戏支持库文件（原 LoaderCombo，VB 仅设 ProgressWeight = 8，其余取默认值）
        steps.Add(new InstallPlanStep(InstallStepKind.PostLibraries, "下载游戏支持库文件", 8, block: true, show: true));

        // 记录安装清单（原 LoaderTask，VB 仅设 Show = False，其余取默认值）
        steps.Add(new InstallPlanStep(InstallStepKind.RecordManifest, "记录安装清单", 1, block: true, show: false));

        // 删除忽略标识（仅非中间产物输出时存在；VB 仅设 Show = False，其余取默认值）
        if (!request.IsIntermediateOutput)
        {
            steps.Add(new InstallPlanStep(InstallStepKind.RemoveIgnoreMarker, "删除忽略标识", 1, block: true, show: false));
        }

        var plan = new InstallPlan(steps);
        plan.AttachDescriptors(request);
        return plan;
    }
}
