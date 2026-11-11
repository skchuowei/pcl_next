namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 「安装游戏」动作的宿主策略。这些值都来自 <see cref="InstallPlanRequest"/> 或宿主设置，
/// 动作本身不做任何业务判定。
/// </summary>
public sealed class InstallGameOptions
{
    /// <summary>是否为中间产物输出。为真时不做版本隔离，目标目录就是版本目录。</summary>
    public bool IsIntermediateOutput { get; init; }

    /// <summary>是否存在任意 Mod 加载器；决定临时 mods 目录不存在时是否仍要建空的 mods 目录。</summary>
    public bool HasAnyModLoader { get; init; }

    /// <summary>
    /// 版本隔离后的目标目录解析器。为 <c>null</c> 时直接使用版本目录（等价于不隔离）。
    /// <para>
    /// VB 侧这一步是 <c>New McInstance(VersionFolder).PathIndie</c>，取决于宿主的版本隔离设置；
    /// Linux 侧由宿主注入，内核不读配置。
    /// </para>
    /// </summary>
    public Func<string, string>? ResolveTargetFolder { get; init; }

    /// <summary>从计划请求取两项纯数据，省去宿主逐字段抄写。</summary>
    public static InstallGameOptions FromRequest(InstallPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new InstallGameOptions
        {
            IsIntermediateOutput = request.IsIntermediateOutput,
            HasAnyModLoader = request.HasAnyModLoader,
        };
    }
}

/// <summary>
/// 「安装游戏」：合并版本 json、把临时 libraries 搬进游戏目录、创建 Mod / 资源包目录。
/// <para>
/// 逐段平移自 VB <c>LoaderPlanAdapter.vb</c> 的 <c>AssembleInstallGame</c> 执行 lambda
/// （顺序、进度点、目录拼接均保持一致）。其中的 <c>MergeJson</c> 只负责按安装形态挑选合并源，
/// 合并规则本身在 <see cref="VersionMerger"/>。
/// </para>
/// </summary>
public sealed class InstallGameAction : IInstallStepAction
{
    private readonly InstallGameOptions _options;

    public InstallGameAction(InstallGameOptions? options = null)
        => _options = options ?? new InstallGameOptions();

    public Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var versionFolder = context.GetAsset<string>("VersionFolder");
        var layout = context.GetAsset<InstallLayout>("Layout");
        var minecraftFolder = context.GetAsset<string>("McFolderSelected");
        var modsTempFolder = context.GetAsset<string>("ModsTempFolder");

        // 1. 合并版本 json：原版在前，加载器按 OptiFine → Forge → NeoForge → LiteLoader → Fabric 叠加。
        VersionMerger.MergeToDirectory(BuildMergeSources(versionFolder, layout), versionFolder);
        ReportProgress(context, 0.2);
        cancellationToken.ThrowIfCancellationRequested();

        // 2. 迁移支持库：临时目录里的 libraries 覆盖进游戏目录。
        InstallFileOps.CopyDirectory(
            Path.Combine(layout.TempFolder, "libraries"),
            Path.Combine(minecraftFolder, "libraries"));
        ReportProgress(context, 0.8);
        cancellationToken.ThrowIfCancellationRequested();

        // 3. 决定目标目录（版本隔离在此时生效），再建 Mod / 资源包目录。
        var targetFolder = ResolveTargetFolder(versionFolder);
        var modsFolder = Path.Combine(targetFolder, "mods");
        if (Directory.Exists(modsTempFolder))
        {
            InstallFileOps.CopyDirectory(modsTempFolder, modsFolder);
        }
        else if (_options.HasAnyModLoader)
        {
            InstallFileOps.EnsureDirectory(modsFolder);
            DownloadLog.Info($"自动创建 Mod 文件夹：{modsFolder}");
        }

        var resourcepacksFolder = Path.Combine(targetFolder, "resourcepacks");
        InstallFileOps.EnsureDirectory(resourcepacksFolder);
        DownloadLog.Info($"自动创建资源包文件夹：{resourcepacksFolder}");

        return Task.CompletedTask;
    }

    private string ResolveTargetFolder(string versionFolder)
    {
        if (_options.IsIntermediateOutput) return versionFolder;
        var resolved = _options.ResolveTargetFolder?.Invoke(versionFolder);
        return string.IsNullOrWhiteSpace(resolved) ? versionFolder : resolved;
    }

    /// <summary>合并源顺序与 VB <c>MergeJson</c> 逐项一致；OptiFine 作为 Mod 时不参与合并。</summary>
    private static IReadOnlyList<VersionMergeSource> BuildMergeSources(string versionFolder, InstallLayout layout)
    {
        var sources = new List<VersionMergeSource> { new("Minecraft", versionFolder) };
        if (layout.OptiFineFolder is not null && !layout.OptiFineAsMod)
            sources.Add(new VersionMergeSource("OptiFine", layout.OptiFineFolder));
        if (layout.ForgeFolder is not null) sources.Add(new VersionMergeSource("Forge", layout.ForgeFolder));
        if (layout.NeoForgeFolder is not null) sources.Add(new VersionMergeSource("NeoForge", layout.NeoForgeFolder));
        if (layout.LiteLoaderFolder is not null) sources.Add(new VersionMergeSource("LiteLoader", layout.LiteLoaderFolder));
        if (layout.FabricFolder is not null) sources.Add(new VersionMergeSource("Fabric", layout.FabricFolder));
        return sources;
    }

    private static void ReportProgress(InstallStepContext context, double fraction)
        => context.Progress?.Report(new InstallStepProgress { Fraction = fraction });
}
