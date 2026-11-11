namespace PCL.Install;

/// <summary>
/// 安装动作用到的极小文件系统工具。
/// <para>
/// 刻意不复刻 MeloongCore 的 <c>DirectoryUtils</c> / <c>FileUtils</c>（内核护栏禁止引用），
/// 只保留 PB3 四个动作真正需要的两件事：建目录、递归复制目录内容。
/// 哈希统一走 <see cref="PCL.Download.HashHelper"/>，不在这里另造一份。
/// </para>
/// </summary>
internal static class InstallFileOps
{
    /// <summary>建目录；已存在不报错。</summary>
    public static void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>
    /// 递归复制 <paramref name="sourceFolder"/> 下的所有文件到 <paramref name="destFolder"/>，
    /// 同名文件直接覆盖。
    /// <para>
    /// 语义对齐 MeloongCore <c>DirectoryUtils.Copy</c>：只复制文件、目标目录按需创建、
    /// 源目录不存在时什么也不做（安装流程里 libraries / mods 目录可能本就没有）。
    /// </para>
    /// </summary>
    public static void CopyDirectory(string sourceFolder, string destFolder)
    {
        if (!Directory.Exists(sourceFolder)) return;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFolder, sourceFile);
            var destFile = Path.Combine(destFolder, relativePath);
            var destDirectory = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destDirectory)) Directory.CreateDirectory(destDirectory);
            File.Copy(sourceFile, destFile, overwrite: true);
        }
    }
}
