namespace PCL.Install;

/// <summary>
/// 「添加忽略标识」：在版本目录下写入 <c>.pclignore</c>，让启动器临时从版本列表里隐藏它。
/// <para>逐字平移自 VB <c>LoaderPlanAdapter.vb</c> 的 <c>AssembleIgnoreMarker</c>。</para>
/// </summary>
public sealed class IgnoreMarkerAction : IInstallStepAction
{
    /// <summary>忽略标识的文件名（VB 为 <c>VersionFolder &amp; ".pclignore"</c>）。</summary>
    public const string FileName = ".pclignore";

    /// <summary>文件内容（逐字取自 VB，改动会让已存在的标识文件在新旧版本间不一致）。</summary>
    public const string Content = "用于临时地在 PCL Next 的版本列表中屏蔽此版本。";

    public Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var versionFolder = context.GetAsset<string>("VersionFolder");
        // VB 的 FileUtils.Write 会先建父目录，这里保持一致（版本目录通常是存在的）。
        Directory.CreateDirectory(versionFolder);
        File.WriteAllText(Path.Combine(versionFolder, FileName), Content);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 「删除忽略标识」：安装收尾时移除 <c>.pclignore</c>。
/// <para>逐字平移自 VB <c>LoaderPlanAdapter.vb</c> 的 <c>AssembleRemoveIgnoreMarker</c>。</para>
/// </summary>
public sealed class RemoveIgnoreMarkerAction : IInstallStepAction
{
    public Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var versionFolder = context.GetAsset<string>("VersionFolder");
        // File.Delete 对不存在的文件不报错，与 VB FileUtils.Delete 一致。
        File.Delete(Path.Combine(versionFolder, IgnoreMarkerAction.FileName));
        return Task.CompletedTask;
    }
}
