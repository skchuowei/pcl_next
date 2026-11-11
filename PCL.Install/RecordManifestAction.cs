namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 「记录安装清单」：把本次由 PCL 管理的 Mod 文件与组件版本写进
/// <c>{VersionFolder}/PCL/config.json</c> 的 <c>MinecraftInstallManifest</c> 键。
/// <para>
/// 逐段平移自 VB <c>LoaderPlanAdapter.vb</c> 的 <c>AssembleRecordManifest</c>：
/// 相对路径用 <see cref="InstallManifestRules.ManagedModPath"/> 规范化（正斜杠 + 全小写 + <c>mods/</c> 前缀），
/// 哈希用 SHA1 小写，Forge / NeoForge 版本剥掉 MC 前缀后写入。
/// </para>
/// </summary>
public sealed class RecordManifestAction : IInstallStepAction
{
    private readonly InstallManifestComponents _components;
    private readonly string _modsTempFolder;

    /// <param name="components">
    /// 组件信息（MC 版本、各加载器原始版本串、OptiFine 条目等）。由宿主构造——
    /// <see cref="InstallPlanRequest"/> 只带「选了什么」，不带 Fabric API / OptiFabric 的版本串。
    /// </param>
    /// <param name="modsTempFolder">本次安装的临时 mods 根目录（<see cref="InstallLayout.ModsRoot"/>）。</param>
    public RecordManifestAction(InstallManifestComponents components, string modsTempFolder)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsTempFolder);
        _components = components;
        _modsTempFolder = modsTempFolder;
    }

    public Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var targetFolder = context.GetAsset<string>("TargetFolder");
        var versionFolder = context.GetAsset<string>("VersionFolder");
        var managedFileSources = context.GetAsset<IReadOnlyDictionary<string, string>>("ManagedFileSources");

        var manifest = new InstallManifest(
            InstallManifest.CurrentSchemaVersion,
            BuildComponents(),
            DateTime.UtcNow,
            BuildManagedFiles(managedFileSources, targetFolder));

        InstallManifestStore.Write(versionFolder, manifest);
        return Task.CompletedTask;
    }

    /// <summary>Forge / NeoForge 的版本串在写入前剥掉 MC 前缀；其余加载器原样保留（对齐 VB）。</summary>
    private InstallManifestComponents BuildComponents()
    {
        var versions = new Dictionary<ModLoaders, string>(_components.ModLoaderVersions);
        if (versions.TryGetValue(ModLoaders.Forge, out var forgeVersion))
            versions[ModLoaders.Forge] = InstallManifestRules.NormalizeLoaderVersion(ModLoaders.Forge, forgeVersion);
        if (versions.TryGetValue(ModLoaders.NeoForge, out var neoForgeVersion))
            versions[ModLoaders.NeoForge] = InstallManifestRules.NormalizeLoaderVersion(ModLoaders.NeoForge, neoForgeVersion);

        return new InstallManifestComponents
        {
            MinecraftVersion = _components.MinecraftVersion,
            ModLoaderVersions = versions,
            OptiFineEntry = _components.OptiFineEntry,
            FabricApiVersion = _components.FabricApiVersion,
            OptiFabricVersion = _components.OptiFabricVersion,
        };
    }

    /// <summary>
    /// 组件名 → 临时 mods 目录下的绝对路径，换算成目标实例里的清单条目。
    /// </summary>
    private List<ManagedInstallFile> BuildManagedFiles(
        IReadOnlyDictionary<string, string> managedFileSources, string targetFolder)
    {
        var files = new List<ManagedInstallFile>(managedFileSources.Count);
        foreach (var (component, sourcePath) in managedFileSources)
        {
            var relativePath = Path.GetRelativePath(_modsTempFolder, sourcePath);
            var fullPath = InstallCommitPlanner.ForCompare(
                Path.Combine(targetFolder, "mods", relativePath));

            files.Add(new ManagedInstallFile(
                InstallManifestRules.ManagedModPath(relativePath),
                HashHelper.ComputeFile(fullPath, HashType.Sha1),
                File.GetLastWriteTimeUtc(fullPath),
                component));
        }
        return files;
    }
}
