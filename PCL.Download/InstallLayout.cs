namespace PCL.Download;

/// <summary>
/// 合并安装请求里与「临时目录布局」相关的输入。
/// </summary>
public sealed class InstallLayoutRequest
{
    /// <summary>
    /// 本次安装的临时根目录（VB <c>RequestTaskTempFolder</c> 的返回值，通常带尾分隔符）。
    /// 是否落在「无空格」的目录由宿主决定，这里只按传入值拼接。
    /// </summary>
    public required string TempFolder { get; init; }

    /// <summary>目标 MC 版本，参与 LiteLoader / Fabric 的目录名。</summary>
    public required string MinecraftVersion { get; init; }

    /// <summary>OptiFine 条目的版本名（<c>InstanceName</c>），未选择 OptiFine 时为 <c>null</c>。</summary>
    public string? OptiFineInstanceName { get; init; }

    /// <summary>Forge 版本串（可能自带 MC 前缀，如 <c>1.19.3-41.2.8</c>），未选择时为 <c>null</c>。</summary>
    public string? ForgeVersion { get; init; }

    /// <summary>NeoForge 版本串，未选择时为 <c>null</c>。</summary>
    public string? NeoForgeVersion { get; init; }

    /// <summary>LiteLoader 版本串，未选择时为 <c>null</c>。</summary>
    public string? LiteLoaderVersion { get; init; }

    /// <summary>Fabric 版本串，未选择时为 <c>null</c>。</summary>
    public string? FabricVersion { get; init; }

    /// <summary>
    /// 是否存在任意 Mod 加载器。为空时按四个版本号是否齐全自动推断；
    /// 由宿主显式传入是为了对齐 VB 侧「<c>ModLoaderVersions.Any</c>」——字典里只要有一个键
    /// 就算有加载器，即使对应的版本串是空串。
    /// </summary>
    public bool? HasAnyModLoader { get; init; }
}

/// <summary>
/// 合并安装时的临时目录布局：各加载器各自的缓存目录、以及 OptiFine「作为 Mod 安装」时的落点。
/// <para>
/// 平移自 VB <c>ModDownloadLib.McInstallLoadersGet</c> 开头的目录推导。这段逻辑全是字符串拼接，
/// 拼错的后果不报错、只在结果里体现（OptiFine 装到了别的目录、Forge 找不到自己的 profile、
/// 合并时少了一份源），所以单独提出来用例锁住。
/// </para>
/// <para>
/// 所有目录属性<b>不带</b>尾部路径分隔符；需要拼接文件名时用
/// <see cref="BuildOptiFineInstallerPath"/> 或 <c>Path.Combine</c>。
/// </para>
/// </summary>
public sealed class InstallLayout
{
    private InstallLayout(
        string tempFolder, string versionsRoot, string modsRoot,
        string? optiFineFolder, bool optiFineAsMod,
        string? forgeFolder, string? neoForgeFolder, string? liteLoaderFolder, string? fabricFolder)
    {
        TempFolder = tempFolder;
        VersionsRoot = versionsRoot;
        ModsRoot = modsRoot;
        OptiFineFolder = optiFineFolder;
        OptiFineAsMod = optiFineAsMod;
        ForgeFolder = forgeFolder;
        NeoForgeFolder = neoForgeFolder;
        LiteLoaderFolder = liteLoaderFolder;
        FabricFolder = fabricFolder;
    }

    /// <summary>临时根目录（已去掉尾部分隔符）。</summary>
    public string TempFolder { get; }

    /// <summary><c>{TempFolder}/versions/</c>，带尾分隔符。</summary>
    public string VersionsRoot { get; }

    /// <summary><c>{TempFolder}/mods/</c>，带尾分隔符。</summary>
    public string ModsRoot { get; }

    /// <summary>
    /// OptiFine 的落点。作为 Mod 安装时是 <c>mods</c>（配合 LiteLoader 时是 <c>mods/{MC版本}</c>，
    /// 见 #8147——LiteLoader 只认版本号子目录）；否则是 <c>versions/{InstanceName}</c>。
    /// 未选择 OptiFine 时为 <c>null</c>。
    /// </summary>
    public string? OptiFineFolder { get; }

    /// <summary>
    /// OptiFine 是否以 Mod 形式下载：选择了 OptiFine 且同时选择了任意 Mod 加载器。
    /// 为真时 OptiFine 不参与版本合并。
    /// </summary>
    public bool OptiFineAsMod { get; }

    /// <summary><c>versions/forge-{版本}</c>，未选择时为 <c>null</c>。</summary>
    public string? ForgeFolder { get; }

    /// <summary><c>versions/neoforge-{版本}</c>，未选择时为 <c>null</c>。</summary>
    public string? NeoForgeFolder { get; }

    /// <summary><c>versions/{MC版本}-LiteLoader</c>，未选择时为 <c>null</c>。</summary>
    public string? LiteLoaderFolder { get; }

    /// <summary><c>versions/fabric-loader-{版本}-{MC版本}</c>，未选择时为 <c>null</c>。</summary>
    public string? FabricFolder { get; }

    /// <summary>
    /// OptiFine 安装器的完整落盘路径。仅在 <see cref="OptiFineAsMod"/> 时需要
    /// （非 Mod 安装走临时目录，由 <c>OptiFineProfile.BuildInstallerTarget</c> 决定）。
    /// </summary>
    public string? BuildOptiFineInstallerPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return OptiFineFolder is null ? null : Path.Combine(OptiFineFolder, fileName);
    }

    /// <summary>
    /// 缓存目录的日志行，顺序与 VB 原实现一致（OptiFine / Forge / NeoForge / Fabric / LiteLoader）。
    /// </summary>
    public IReadOnlyList<string> LogLines()
    {
        var lines = new List<string>(5);
        if (OptiFineFolder is not null) lines.Add($"OptiFine 缓存：{OptiFineFolder}");
        if (ForgeFolder is not null) lines.Add($"Forge 缓存：{ForgeFolder}");
        if (NeoForgeFolder is not null) lines.Add($"NeoForge 缓存：{NeoForgeFolder}");
        if (FabricFolder is not null) lines.Add($"Fabric 缓存：{FabricFolder}");
        if (LiteLoaderFolder is not null) lines.Add($"LiteLoader 缓存：{LiteLoaderFolder}");
        return lines;
    }

    /// <summary>按请求推导布局。</summary>
    public static InstallLayout Create(InstallLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TempFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MinecraftVersion);

        var tempFolder = TrimTrailingSeparators(request.TempFolder);
        var versionsRoot = WithSeparator(Path.Combine(tempFolder, "versions"));
        var modsRoot = WithSeparator(Path.Combine(tempFolder, "mods"));

        var hasModLoader = request.HasAnyModLoader
            ?? (request.ForgeVersion is not null || request.NeoForgeVersion is not null
                || request.LiteLoaderVersion is not null || request.FabricVersion is not null);

        // OptiFine 只有在「同时选了 Mod 加载器」时才退化成 Mod；否则参与版本合并。
        var optiFineAsMod = request.OptiFineInstanceName is not null && hasModLoader;
        string? optiFineFolder;
        if (request.OptiFineInstanceName is null)
        {
            optiFineFolder = null;
        }
        else if (optiFineAsMod)
        {
            // #8147：LiteLoader 的 Mod 只会从 mods/{MC版本}/ 里读，放 mods 根目录加载不到。
            optiFineFolder = request.LiteLoaderVersion is not null
                ? Path.Combine(tempFolder, "mods", request.MinecraftVersion)
                : Path.Combine(tempFolder, "mods");
        }
        else
        {
            optiFineFolder = Path.Combine(tempFolder, "versions", request.OptiFineInstanceName);
        }

        return new InstallLayout(
            tempFolder,
            versionsRoot,
            modsRoot,
            optiFineFolder,
            optiFineAsMod,
            forgeFolder: request.ForgeVersion is null
                ? null
                : Path.Combine(tempFolder, "versions", "forge-" + request.ForgeVersion),
            neoForgeFolder: request.NeoForgeVersion is null
                ? null
                : Path.Combine(tempFolder, "versions", "neoforge-" + request.NeoForgeVersion),
            liteLoaderFolder: request.LiteLoaderVersion is null
                ? null
                : Path.Combine(tempFolder, "versions", request.MinecraftVersion + "-LiteLoader"),
            fabricFolder: request.FabricVersion is null
                ? null
                : Path.Combine(tempFolder, "versions",
                    $"fabric-loader-{request.FabricVersion}-{request.MinecraftVersion}"));
    }

    private static string TrimTrailingSeparators(string path)
        => path.TrimEnd('\\', '/');

    private static string WithSeparator(string path)
        => path + Path.DirectorySeparatorChar;
}

/// <summary>
/// 安装清单（<c>McInstallManifest</c>）里的两条字符串规则。
/// <para>
/// 两条都只在「修改页能不能认出已装的 Mod / 加载器」上体现，写错了不报错：
/// </para>
/// <list type="bullet">
/// <item>相对路径统一成 <c>mods/xxx</c>（正斜杠 + 小写）——与 Windows 上的实际路径比较时靠这条；</item>
/// <item>加载器版本串剥掉 MC 前缀——装的时候是 <c>1.19.3-41.2.8</c>，清单里要记 <c>41.2.8</c>。</item>
/// </list>
/// </summary>
public static class InstallManifestRules
{
    /// <summary>
    /// 把 <c>mods</c> 目录下的相对路径规范成清单里记录的键：正斜杠分隔、全小写、以 <c>mods/</c> 开头。
    /// </summary>
    public static string ManagedModPath(string relativePathInMods)
    {
        ArgumentNullException.ThrowIfNull(relativePathInMods);
        return "mods/" + relativePathInMods.Replace('\\', '/').ToLowerInvariant();
    }

    /// <summary>
    /// 把「可能自带 MC 前缀」的加载器版本串剥成纯加载器版本，用于写入安装清单。
    /// <para>
    /// 两种加载器的旧命名不一样，这里逐字平移原实现，<b>不做统一</b>：
    /// </para>
    /// <list type="bullet">
    /// <item>Forge：以 <c>1.</c> 开头且含 <c>-</c> 时取最后一个 <c>-</c> 之后的部分
    /// （<c>1.19.3-41.2.8</c> → <c>41.2.8</c>）。老版本形如 <c>1.7.10-10.13.4.1614-1.7.10</c>，
    /// 按这条会得到 <c>1.7.10</c>——原实现就是这样，改动会让已装实例在修改页里对不上，故保留。</item>
    /// <item>NeoForge：只剥固定的 <c>1.20.1-</c> 前缀（<c>1.20.1-47.1.82</c> → <c>47.1.82</c>）。
    /// 1.20.4 起 NeoForge 版本串本身不带 MC 前缀，原样保留。</item>
    /// </list>
    /// </summary>
    public static string NormalizeLoaderVersion(ModLoaders loader, string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        switch (loader)
        {
            case ModLoaders.Forge:
                if (version.StartsWith("1.", StringComparison.Ordinal) && version.Contains('-'))
                    return version[(version.LastIndexOf('-') + 1)..];
                return version;
            case ModLoaders.NeoForge:
                // 原实现用 Replace（全局），逐字对齐；实际版本串里只会出现一次。
                return version.Replace("1.20.1-", "", StringComparison.Ordinal);
            default:
                return version;
        }
    }
}
