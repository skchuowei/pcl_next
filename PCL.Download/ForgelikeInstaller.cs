namespace PCL.Download;

/// <summary>
/// 归一化之后的 Forge / NeoForge 目标。
/// </summary>
/// <param name="Inherit">目标 Minecraft 版本。</param>
/// <param name="LoaderVersion">
/// 加载器版本。对 Forge 是可读版本名（如 <c>41.2.8</c>）；对 NeoForge 是 API 名（如 <c>20.4.30</c>、
/// <c>1.20.1-47.1.99</c>）——因为 NeoForge 的 maven 路径就是按 API 名组织的。
/// </param>
public sealed record ForgelikeVersionTarget(string Inherit, string LoaderVersion);

/// <summary>
/// 一个安装器文件的下载信息：候选源（镜像在前、官方在后）、最小体积、校验值。
/// </summary>
/// <param name="Urls">按优先级排列的下载地址。</param>
/// <param name="MinSize">可接受的最小文件体积，用于识别「下到了错误页」这种伪成功。</param>
/// <param name="Hash">文件校验值（MD5 或 SHA1）。为 null 表示该源没给。</param>
public sealed record ForgelikeInstallerFile(IReadOnlyList<string> Urls, long MinSize, string? Hash);

/// <summary>
/// Forge / NeoForge 安装过程中的纯逻辑：版本串归一化、安装器地址、安装器启动参数、安装器输出→进度。
/// <para>
/// 这些片段原本散在 <c>ModDownloadLib</c> 的 <c>McDownloadForgelikeLoader</c> 与
/// <c>ForgelikeInjector</c> 里，且「另存为」与「安装」两条路径各写了一份地址拼接——
/// 两份已经开始漂移（见 <see cref="BuildForgeInstaller"/> 的注释）。集中到这里后只有一份。
/// </para>
/// </summary>
public static class ForgelikeInstaller
{
    /// <summary>安装器可接受的最小体积：小于该值基本可以断定下到的是错误页而非 jar。</summary>
    public const long MinInstallerSize = 64 * 1024;

    private const string BmclapiMaven = "https://bmclapi2.bangbang93.com/maven/";
    private const string ForgeMaven = "https://files.minecraftforge.net/maven/";
    private const string NeoForgeMaven = "https://maven.neoforged.net/releases/";
    private const string NeoForgeMavenMirror = "https://bmclapi2.bangbang93.com/maven/";

    /// <summary>
    /// 把「整合包/实例里写的加载器版本串」归一化成目标版本与加载器版本。
    /// </summary>
    /// <param name="isNeoForge">是否为 NeoForge。</param>
    /// <param name="loaderVersion">版本串，可能形如 <c>41.2.8</c>、<c>1.19.3-41.2.8</c>、<c>20.4.30</c>。</param>
    /// <param name="inherit">实例声明的 MC 版本。</param>
    public static ForgelikeVersionTarget Normalize(bool isNeoForge, string loaderVersion, string inherit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderVersion);
        inherit ??= "";

        if (isNeoForge)
        {
            // 1.20.1 的 NeoForge 仍发布在 net/neoforged/forge 下，API 名带 MC 版本前缀。
            // 整合包往往只写「47.1.99」，这里补齐；已经带了前缀的不重复加。
            if (inherit == "1.20.1" && !loaderVersion.StartsWith("1.20.1-", StringComparison.Ordinal))
                return new ForgelikeVersionTarget(inherit, "1.20.1-" + loaderVersion);
            return new ForgelikeVersionTarget(inherit, loaderVersion);
        }

        // Forge 的版本串可能自带 MC 版本，如「1.19.3-41.2.8」。此时以这一串为准而不是实例声明的
        // Inherit——整合包里出现过「声明 1.19 但实际用 1.19.3 的 Forge」，按 Inherit 会找不到。
        if (loaderVersion.StartsWith("1.", StringComparison.Ordinal) && loaderVersion.Contains('-'))
            return new ForgelikeVersionTarget(loaderVersion.BeforeFirst("-"), loaderVersion.AfterLast("-"));

        return new ForgelikeVersionTarget(inherit, loaderVersion);
    }

    /// <summary>
    /// Forge 安装器的下载信息。
    /// </summary>
    /// <param name="inherit">目标 MC 版本。</param>
    /// <param name="fileVersion">用于下载的文件版本名，可能带分支后缀（如 <c>14.23.5.2860-1.7.10</c>）。</param>
    /// <param name="category">installer / universal / client。</param>
    /// <param name="fileExtension">不以小数点开头。</param>
    /// <param name="hash">校验值，可为 null。</param>
    /// <remarks>
    /// 快照版本在 Forge 的 maven 里用下划线（<c>1.20.2_pre1</c>）。「另存为安装器」那条路径原先漏了这次
    /// 替换，快照版本会拼出 404 地址；此处统一按下划线处理。
    /// </remarks>
    public static ForgelikeInstallerFile BuildForgeInstaller(
        string inherit, string fileVersion, string category, string fileExtension, string? hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inherit);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileVersion);

        var mc = inherit.Replace('-', '_');
        var path = $"net/minecraftforge/forge/{mc}-{fileVersion}/forge-{mc}-{fileVersion}-{category}.{fileExtension}";
        return new ForgelikeInstallerFile([BmclapiMaven + path, ForgeMaven + path], MinInstallerSize, hash);
    }

    /// <summary>
    /// NeoForge 安装器的下载信息。
    /// </summary>
    /// <param name="inherit">目标 MC 版本。</param>
    /// <param name="apiName">API 名，如 <c>20.4.30</c>、<c>1.20.1-47.1.99</c>。</param>
    public static ForgelikeInstallerFile BuildNeoForgeInstaller(string inherit, string apiName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);

        var packageName = inherit == "1.20.1" ? "forge" : "neoforge";
        var official = $"{NeoForgeMaven}net/neoforged/{packageName}/{apiName}/{packageName}-{apiName}-installer.jar";
        return new ForgelikeInstallerFile(
            [official.Replace(NeoForgeMaven, NeoForgeMavenMirror, StringComparison.Ordinal), official],
            MinInstallerSize, null);
    }

    /// <summary>
    /// 组装安装器的启动参数。
    /// </summary>
    /// <param name="javaMajor">所选 Java 的主版本号。</param>
    /// <param name="installerPath">安装器 jar 的本地路径。</param>
    /// <param name="targetJar">被注入的 Forge jar（作为 classpath 的一部分）。</param>
    /// <param name="mcFolder">.minecraft 目录，安装器把库与版本写到这里。</param>
    /// <param name="javaWrapperPath">JavaWrapper.jar 的路径，仅在 <paramref name="useJavaWrapper"/> 为真时使用。</param>
    /// <param name="javaWrapperTempDir">JavaWrapper 的临时目录（<c>oolloo.jlw.tmpdir</c>）。</param>
    /// <param name="useJavaWrapper">是否经 JavaWrapper 启动。</param>
    public static string BuildArguments(int javaMajor, string installerPath, string targetJar, string mcFolder,
        string? javaWrapperPath = null, string? javaWrapperTempDir = null, bool useJavaWrapper = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mcFolder);

        var classPath = $"\"{installerPath};{targetJar}\"";
        string arguments;
        if (useJavaWrapper && !string.IsNullOrWhiteSpace(javaWrapperPath))
        {
            arguments = $"-Doolloo.jlw.tmpdir=\"{javaWrapperTempDir}\" -cp {classPath} -jar \"{javaWrapperPath}\""
                + $" com.bangbang93.ForgeInstaller \"{mcFolder}\"";
        }
        else
        {
            arguments = $"-cp {classPath} com.bangbang93.ForgeInstaller \"{mcFolder}\"";
        }

        // Java 9 起模块系统默认不导出内部包，安装器要用 bootstraplauncher 的内部类，必须显式开缺口。
        if (javaMajor >= 9)
            arguments = "--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED " + arguments;

        return arguments;
    }

    /// <summary>
    /// 安装器输出的一行 → 进度动作。返回 null 表示这行不认识（调用方按 trace 处理）。
    /// </summary>
    /// <remarks>
    /// 表里的字符串是各版安装器的原文，改动必须对着安装器实际输出核对：写错一个字就只是「进度条不动」，
    /// 不会报错，很难发现。带 <c>+=</c> 的那条不要改成赋值——安装器会连续打很多行，靠它累积细粒度进度。
    /// </remarks>
    public static ForgelikeProgressStep? ReadProgress(string? line)
    {
        if (line is null) return null;
        return line switch
        {
            "Extracting json" => new(0.27, false),
            "Downloading libraries" => new(0.28, false),
            "  File exists: Checksum validated." => new(0.003, true), // 注意两个前导空格
            "Building Processors" => new(0.38, false),
            "Task: DOWNLOAD_MOJMAPS" => new(0.4, false),   // 新版（B）
            "Task: MERGE_MAPPING" => new(0.5, false),      // 新版（B）
            "Splitting: " => new(0.55, false),             // 注意尾部空格
            "Parameter Annotations" => new(0.6, false),    // 新版（B）
            "Processing Complete" => new(0.67, false),     // 新版（B）
            "log: null" => new(0.67, false),
            "Sorting" => new(0.8, false),
            "Remapping final jar" => new(0.85, false),     // 旧版（A）
            "Remapping jar... 50%" => new(0.9, false),     // 旧版（A）
            "Remapping jar... 100%" => new(0.95, false),   // 旧版（A）
            "Injecting profile" => new(0.98, false),
            _ => null,
        };
    }
}

/// <summary>
/// 安装器输出对应的进度动作。
/// </summary>
/// <param name="Progress">进度值。绝对值，或 <paramref name="IsIncrement"/> 为真时的增量。</param>
/// <param name="IsIncrement">是增量（累加）还是绝对值（覆盖）。</param>
public readonly record struct ForgelikeProgressStep(double Progress, bool IsIncrement);
