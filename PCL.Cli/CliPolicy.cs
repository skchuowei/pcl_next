namespace PCL.Cli;

/// <summary>
/// MC 下载的部署期开关，**与 VB 侧 <c>ModDownloadPolicy</c> 同源同序**（计划 §5.4，合规红线）。
/// <para/>
/// 生效顺序：环境变量 <c>PCL_ALLOW_MC_DOWNLOAD</c> → <c>DownloadPolicy.ini</c> → 内置默认 <c>true</c>。
/// 首次访问即缓存（与 VB 一致：运行期改文件不生效，单次 CLI 调用本就只读一次）。
/// </para>
/// <para/>
/// 与 VB 的**唯一差异**：候选 ini 路径比 GUI 多一个「上一级目录」。原因是 CLI 产物落在
/// <c>bin-net10\&lt;代号&gt;\&lt;版本&gt;\&lt;RID&gt;\cli\</c>，而 Launcher 的 <c>DownloadPolicy.ini</c> 在其上一级；
/// 若不向上查找，就会出现「GUI 已禁用、CLI 照下」的合规漏洞——这是必须堵住的，故此差异是收紧而非放宽。
/// </para>
/// </summary>
internal sealed class CliPolicy
{
    /// <summary>未做任何配置时的默认状态（与 <c>ModDownloadPolicy.DefaultAllowMcDownload</c> 一致）。</summary>
    public const bool DefaultAllowMcDownload = true;

    /// <summary>配置文件名称。</summary>
    public const string PolicyFileName = "DownloadPolicy.ini";

    /// <summary>覆盖开关的环境变量名。</summary>
    public const string PolicyEnvName = "PCL_ALLOW_MC_DOWNLOAD";

    private CliPolicy(bool allow, string source, string? filePath)
    {
        AllowMcDownload = allow;
        Source = source;
        FilePath = filePath;
    }

    /// <summary>是否允许下载 Minecraft 本体与相关资源。</summary>
    public bool AllowMcDownload { get; }

    /// <summary>命中的来源描述（<c>env</c> / <c>ini:&lt;路径&gt;</c> / <c>default</c>）。</summary>
    public string Source { get; }

    /// <summary>实际读取到的 ini 路径；未命中时为 null。</summary>
    public string? FilePath { get; }

    /// <summary>解析策略。顺序与 VB <c>ModDownloadPolicy.EnsureLoaded</c> 完全一致。</summary>
    /// <param name="explicitIniPath">显式指定的 ini 路径（<c>--policy-file</c>）；非 null 时只认它。</param>
    public static CliPolicy Resolve(string? explicitIniPath = null)
    {
        // 1. 环境变量优先
        var envRaw = Environment.GetEnvironmentVariable(PolicyEnvName);
        if (!string.IsNullOrWhiteSpace(envRaw))
        {
            if (TryParseSwitch(envRaw, out var envValue))
                return new CliPolicy(envValue, "env:" + PolicyEnvName, null);
            // 取值非法则忽略，继续往下（与 VB 行为一致）
        }

        // 2. ini
        foreach (var candidate in CandidateIniPaths(explicitIniPath))
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                if (TryReadIniSwitch(candidate, out var iniValue))
                    return new CliPolicy(iniValue, "ini:" + candidate, candidate);
            }
            catch (Exception)
            {
                // 读取失败按「未配置」处理，与 VB 一致（VB 侧只 Warn 不抛）
            }
        }

        // 3. 内置默认
        return new CliPolicy(DefaultAllowMcDownload, "default", null);
    }

    /// <summary>候选 ini 路径：显式路径优先；否则 exe 目录 → 上一级目录。</summary>
    private static IEnumerable<string> CandidateIniPaths(string? explicitIniPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitIniPath))
        {
            yield return Path.GetFullPath(explicitIniPath);
            yield break;
        }

        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, PolicyFileName);

        var parent = Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent))
            yield return Path.Combine(parent, PolicyFileName);
    }

    /// <summary>解析开关型文本，支持 True/False、1/0、yes/no、on/off。</summary>
    private static bool TryParseSwitch(string raw, out bool value)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes" or "on":
                value = true;
                return true;
            case "false" or "0" or "no" or "off":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    /// <summary>
    /// 从 ini 风格文件读取 <c>AllowMcDownload</c>。
    /// 以 <c>;</c> <c>#</c> 开头视为注释，<c>[Section]</c> 行跳过（不区分节），与 VB 一致。
    /// </summary>
    private static bool TryReadIniSwitch(string path, out bool value)
    {
        value = false;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('[')) continue;
            var split = line.IndexOf('=');
            if (split <= 0) continue;
            if (!line[..split].Trim().Equals("AllowMcDownload", StringComparison.OrdinalIgnoreCase)) continue;
            return TryParseSwitch(line[(split + 1)..], out value);
        }
        return false;
    }
}
