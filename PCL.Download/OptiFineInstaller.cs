using System.IO.Compression;

namespace PCL.Download;

/// <summary>
/// 从 OptiFine 安装器 jar 里探测它要求的 Java 版本。
/// </summary>
/// <param name="JavaMajor">所需 Java 主版本号。探测失败时为默认值。</param>
/// <param name="ClassVersion">读到的 class 文件版本号；未读到为 null。</param>
/// <param name="Failure">探测失败的原因；成功为 null。</param>
public readonly record struct OptiFineJavaProbe(int JavaMajor, int? ClassVersion, string? Failure);

/// <summary>
/// OptiFine 安装过程中的纯逻辑：Java 版本探测、安装器启动参数、安装器输出→进度与成败判定。
/// <para>
/// 这些片段原本写在 <c>ModDownloadLib.McDownloadOptiFineInstall</c> 里，与 <c>Process</c>、<c>LoaderTask</c>
/// 搅在一起：探测逻辑被一个「吞掉所有异常」的 <c>Try</c> 包着，失败原因（找不到 class、文件头不对、
/// 版本超范围）全部退化成同一句「读取 Java 版本失败」，用户装了错的 Java 也看不出是哪一步不对。
/// </para>
/// <para>
/// 这里只做「读字节、拼字符串、判成败」，进程怎么起、进度怎么报仍留在宿主。
/// </para>
/// </summary>
public static class OptiFineInstaller
{
    /// <summary>探测失败时的默认 Java 主版本号。原实现即按 21 起步，保守取高不取低。</summary>
    public const int DefaultJavaMajor = 21;

    /// <summary>class 文件版本号下限（Java 5）。低于此值说明读到的不是安装器主类。</summary>
    public const int MinClassVersion = 49;

    /// <summary>class 文件版本号上限。高于此值说明格式已变，按此推算 Java 版本会给出荒谬结果。</summary>
    public const int MaxClassVersion = 100;

    /// <summary>class 文件版本号与 Java 主版本号的固定差值（52 → Java 8，61 → Java 17）。</summary>
    public const int ClassVersionToJavaOffset = 44;

    /// <summary>
    /// 安装器「正常跑完」的最小输出行数。低于此值基本可以断定它在启动阶段就崩了，
    /// 只是没有把异常打到 stderr。
    /// </summary>
    public const int MinOutputLines = 1000;

    /// <summary>
    /// 每读到一行输出推进的进度量。安装器会打七千行左右，故把剩余的 0.7 摊到 7000 行上。
    /// </summary>
    public const double ProgressPerLine = 0.7 / 7000;

    private const string InstallerClassEntry = "optifine/Installer.class";

    /// <summary>
    /// 读安装器 jar 里 <c>optifine/Installer.class</c> 的 class 版本号，据此推算所需 Java 版本。
    /// </summary>
    /// <remarks>
    /// 任何一步失败都返回「默认版本 + 原因」而不是抛异常：原实现把异常吞掉记一条警告，
    /// 装错 Java 的表现是安装器一闪而过，用户与日志都看不出所以然。现在原因会被带回去记录。
    /// </remarks>
    public static OptiFineJavaProbe ProbeRequiredJava(string installerJarPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerJarPath);

        int classVersion;
        try
        {
            using var zip = ZipFile.OpenRead(installerJarPath);
            var entry = zip.GetEntry(InstallerClassEntry);
            if (entry is null)
                return Fail($"未找到 {InstallerClassEntry}");

            using var stream = entry.Open();
            Span<byte> header = stackalloc byte[8];
            // ReadAtLeast 只有在「读满」或「流结束」时才返回；返回实际字节数，短读必须自己判。
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (read < header.Length)
                return Fail($"{InstallerClassEntry} 长度不足（{read} 字节）");

            // CAFEBABE：Java class 文件魔数。
            if (header[0] != 0xCA || header[1] != 0xFE || header[2] != 0xBA || header[3] != 0xBE)
                return Fail($"{InstallerClassEntry} 文件头无效");

            // 第 4、5 字节是次版本，第 6、7 字节是主版本（大端）。
            classVersion = header[6] * 0x100 + header[7];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            // 文件不是 zip、被占用、权限不足都算探测失败，按默认版本继续装。
            return Fail($"读取 {InstallerClassEntry} 失败：{ex.Message}");
        }

        if (classVersion < MinClassVersion)
            return Fail($"{InstallerClassEntry} 版本过低（{classVersion}）", classVersion);
        if (classVersion > MaxClassVersion)
            return Fail($"{InstallerClassEntry} 版本过高（{classVersion}）", classVersion);

        return new OptiFineJavaProbe(classVersion - ClassVersionToJavaOffset, classVersion, null);

        static OptiFineJavaProbe Fail(string reason, int? classVersion = null) =>
            new(DefaultJavaMajor, classVersion, reason);
    }

    /// <summary>
    /// 组装 OptiFine 安装器的启动参数。
    /// </summary>
    /// <param name="javaMajor">所选 Java 的主版本号。</param>
    /// <param name="installerPath">安装器 jar 的本地路径。</param>
    /// <param name="mcFolder">.minecraft 目录，安装器把库与版本写到这里；允许带尾斜杠。</param>
    /// <param name="javaWrapperPath">JavaWrapper.jar 的路径，仅在 <paramref name="useJavaWrapper"/> 为真时使用。</param>
    /// <param name="javaWrapperTempDir">JavaWrapper 的临时目录（<c>oolloo.jlw.tmpdir</c>）。</param>
    /// <param name="useJavaWrapper">是否经 JavaWrapper 启动。</param>
    /// <remarks>
    /// 与 Forge 安装器的两处差异，改的时候容易照抄错：
    /// <list type="bullet">
    /// <item>classpath 只有安装器自己，没有被打补丁的目标 jar——OptiFine 是覆盖式安装，不存在「被注入的 jar」。</item>
    /// <item>入口是 <c>optifine.Installer</c> 而不是清单里的 <c>Main-Class</c>，故不用 <c>-jar</c> 单独跑，
    /// 而是用 <c>-cp + 主类</c>。</item>
    /// </list>
    /// </remarks>
    public static string BuildArguments(int javaMajor, string installerPath, string mcFolder,
        string? javaWrapperPath = null, string? javaWrapperTempDir = null, bool useJavaWrapper = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mcFolder);

        // 两个 -D 都带引号：.minecraft 路径常含空格（「我的世界」「Minecraft Launcher」）。
        // 目录参数统一经 InstallerArguments.QuoteDirectory 去尾斜杠，避免 "<dir>\" 把闭合引号转义掉。
        var quotedMcFolder = InstallerArguments.QuoteDirectory(mcFolder);
        string arguments;
        if (useJavaWrapper && !string.IsNullOrWhiteSpace(javaWrapperPath))
        {
            arguments = $"-Doolloo.jlw.tmpdir={InstallerArguments.QuoteDirectory(javaWrapperTempDir)}"
                + $" -Duser.home={quotedMcFolder}"
                + $" -cp \"{installerPath}\" -jar \"{javaWrapperPath}\" optifine.Installer";
        }
        else
        {
            arguments = $"-Duser.home={quotedMcFolder} -cp \"{installerPath}\" optifine.Installer";
        }

        // Java 9 起模块系统默认不导出内部包，安装器要用 bootstraplauncher 的内部类，必须显式开缺口。
        if (javaMajor >= 9)
            arguments = "--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED " + arguments;

        return arguments;
    }

    /// <summary>
    /// 安装器跑完之后判定成败。
    /// </summary>
    /// <param name="outputLineCount">累计收到的输出行数（stdout + stderr）。</param>
    /// <param name="lastLine">最后一行输出。</param>
    /// <remarks>
    /// 「末行含 <c>at </c>」是判断异常栈的土办法：安装器把未捕获异常打到 stderr 时以退出码 0 结束，
    /// 唯一的痕迹就是末行是一帧 <c>at xxx.yyy(...)</c>。这条判定依赖安装器「正常结束时末行不是栈帧」，
    /// 换版本时要重新核对，否则会把成功当失败。
    /// </remarks>
    public static bool IsFailed(int outputLineCount, string? lastLine)
        => outputLineCount < MinOutputLines || (lastLine?.Contains("at ", StringComparison.Ordinal) ?? false);
}
