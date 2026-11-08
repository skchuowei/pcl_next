namespace PCL.Download;

/// <summary>
/// 一个「从构建目录搬到目标目录」的文件，两段都是绝对路径。
/// </summary>
public sealed record InstallCommitMove(string Source, string Target);

/// <summary>
/// <see cref="InstallCommitPlanner.Build"/> 的输入。
/// </summary>
public sealed class InstallCommitPlanRequest
{
    /// <summary>本次安装产出的临时版本目录。</summary>
    public required string BuildFolder { get; init; }

    /// <summary>要覆盖的现有实例目录（版本隔离后的 .minecraft 或版本目录）。</summary>
    public required string TargetFolder { get; init; }

    /// <summary>现有实例的版本目录（<c>Instance.PathVersion</c>），用于定位它自己的 jar。</summary>
    public required string VersionFolder { get; init; }

    /// <summary>现有实例名，用于拼 <c>{VersionFolder}{Name}.jar</c>。</summary>
    public required string InstanceName { get; init; }

    /// <summary>现有实例的版本 json 路径。为空表示实例已损坏、找不到 json。</summary>
    public string? OldJsonPath { get; init; }

    /// <summary>新安装清单里的相对路径（<c>mods/xxx</c> 形式），决定要覆盖哪些文件。</summary>
    public required IReadOnlyList<string> NewRelativePaths { get; init; }

    /// <summary>现有安装清单里的相对路径，其中不在新清单里的要被删除。</summary>
    public required IReadOnlyList<string> OldRelativePaths { get; init; }

    /// <summary>
    /// 构建目录下是否存在旧版 MC 的 <c>resources</c> 目录。为空时由本方法自行检查文件系统，
    /// 显式传入只为让宿主与用例避免真的去建目录。
    /// </summary>
    public bool? LegacyResourcesExist { get; init; }
}

/// <summary>
/// 修改安装（把新版本覆盖到现有实例上）的变更计划。
/// </summary>
/// <param name="FilesToDelete">要删除的绝对路径，已按忽略大小写去重。</param>
/// <param name="FilesToMove">要从构建目录复制到目标目录的文件对。</param>
/// <param name="CopyLegacyResources">是否需要把旧版 MC 的 <c>resources</c> 一并复制过去。</param>
public sealed record InstallCommitPlan(
    IReadOnlyList<string> FilesToDelete,
    IReadOnlyList<InstallCommitMove> FilesToMove,
    bool CopyLegacyResources);

/// <summary>
/// 「修改版本」时的变更计划推导：新清单里的文件覆盖过去、旧清单里多出来的删掉、
/// 再额外清理实例自己的 json / jar。
/// <para>
/// 平移自 VB <c>ModDownloadLib.McInstallCommit</c> 的整理段。这段只算不改，但两处比较用了
/// 不同的规则，很容易在这里「顺手统一」而改坏已装实例：
/// </para>
/// <list type="bullet">
/// <item>判断「新清单里有没有这一项」用的是<b>区分大小写</b>的集合
/// （<see cref="HashSet{T}(IEnumerable{T})"/> 默认 <c>Ordinal</c>）；</item>
/// <item>删除列表本身用 <c>OrdinalIgnoreCase</c> 去重。</item>
/// </list>
/// <para>
/// 两条规则不一致是原实现的状态：清单里的相对路径在生成时就已规范化成小写
/// （见 <see cref="InstallManifestRules.ManagedModPath"/>），所以实践中不会撞车；
/// 但若哪天清单来自别处，这个差异就会体现为「同一个文件被删一次还是两次」。
/// </para>
/// </summary>
public static class InstallCommitPlanner
{
    /// <summary>按请求推导变更计划。不触碰文件系统（除非需要检查旧版 resources 目录）。</summary>
    public static InstallCommitPlan Build(InstallCommitPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BuildFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VersionFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceName);
        ArgumentNullException.ThrowIfNull(request.NewRelativePaths);
        ArgumentNullException.ThrowIfNull(request.OldRelativePaths);

        // 新版里有的：从构建目录复制过去（覆盖）。
        var moves = new List<InstallCommitMove>(request.NewRelativePaths.Count);
        foreach (var relativePath in request.NewRelativePaths)
        {
            moves.Add(new InstallCommitMove(
                Path.Combine(request.BuildFolder, relativePath),
                Path.Combine(request.TargetFolder, relativePath)));
        }

        // 新版里没有、老版里有的：删掉。注意这里是区分大小写的集合。
        var newPaths = new HashSet<string>(request.NewRelativePaths);
        var filesToDelete = new List<string>();
        foreach (var relativePath in request.OldRelativePaths)
        {
            if (newPaths.Contains(relativePath)) continue;
            filesToDelete.Add(ForCompare(Path.Combine(request.TargetFolder, relativePath)));
        }

        // 实例自己的 json / jar：不归清单管，一律先删再搬。
        if (!string.IsNullOrEmpty(request.OldJsonPath))
        {
            filesToDelete.Add(request.OldJsonPath);
            filesToDelete.Add(Path.ChangeExtension(request.OldJsonPath, ".jar"));
        }
        filesToDelete.Add(Path.Combine(request.VersionFolder, request.InstanceName + ".jar"));

        // 两条路径可能指向同一个 jar（上面的 .json → .jar，与本行拼出来的同名文件）。
        var deduped = filesToDelete
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var copyLegacyResources = request.LegacyResourcesExist
            ?? Directory.Exists(Path.Combine(request.BuildFolder, "resources"));

        return new InstallCommitPlan(deduped, moves, copyLegacyResources);
    }

    /// <summary>
    /// 路径归一，用于比较：展开成绝对路径、去掉 <c>\\?\</c> 前缀、分隔符统一、去掉尾分隔符。
    /// <para>
    /// 对齐 MeloongCore 的 <c>PathUtils.ForCompare</c>。内核不能引用 MeloongCore
    /// （架构护栏禁止 GUI 依赖），所以这里用纯 BCL 复现同样的口径。
    /// 注意：<c>PathUtils.ForCompare</c> 的注释里提到「将短路径展开」，实际实现并没有这一步，
    /// 这里也不做——真要展开 8.3 短名得调 Win32 <c>GetLongPathName</c>，属于另一件事。
    /// </para>
    /// </summary>
    public static string ForCompare(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full[4..];

        var separator = Path.DirectorySeparatorChar;
        full = full.Replace(Path.AltDirectorySeparatorChar, separator);
        return full.TrimEnd(separator);
    }
}
