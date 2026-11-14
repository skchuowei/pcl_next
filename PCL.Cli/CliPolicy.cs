using PCL.Core;

namespace PCL.Cli;

/// <summary>
/// MC 下载的部署期开关（CLI 侧门面）。
/// <para/>
/// 2026-11-14「GUI+CLI 开壳」：解析规则已下沉到 <c>PCL.Core.PclDownloadPolicy</c>，
/// 与 GUI 的 <c>ModDownloadPolicy</c> 共用同一份实现（计划 §5.4，合规红线）。
/// 本类只保留 CLI 需要的三样东西：<c>--policy-file</c> 显式路径、
/// 「向上查一级 ini」的产物布局约定、以及 <c>env: / ini: / default</c> 的来源字符串格式。
/// </para>
/// <para/>
/// 与 GUI 的**唯一差异**仍是候选 ini 路径：CLI 产物落在
/// <c>bin-net10\&lt;代号&gt;\&lt;版本&gt;\&lt;RID&gt;\cli\</c>，而启动器的 <c>DownloadPolicy.ini</c> 在其上一级。
/// 若不向上查找，就会出现「GUI 已禁用、CLI 照下」的漏洞——这是收紧而非放宽。
/// </para>
/// </summary>
internal sealed class CliPolicy
{
    /// <summary>未做任何配置时的默认状态（与 Core / GUI 同源）。</summary>
    public const bool DefaultAllowMcDownload = PclCoreDefaults.DefaultAllowMcDownload;

    /// <summary>配置文件名称。</summary>
    public const string PolicyFileName = PclCoreDefaults.PolicyFileName;

    /// <summary>覆盖开关的环境变量名。</summary>
    public const string PolicyEnvName = PclCoreDefaults.PolicyEnvName;

    private CliPolicy(bool allow, string source, string? filePath, string? warning)
    {
        AllowMcDownload = allow;
        Source = source;
        FilePath = filePath;
        Warning = warning;
    }

    /// <summary>是否允许下载 Minecraft 本体与相关资源。</summary>
    public bool AllowMcDownload { get; }

    /// <summary>命中的来源描述（<c>env:&lt;变量名&gt;</c> / <c>ini:&lt;路径&gt;</c> / <c>default</c>）。</summary>
    public string Source { get; }

    /// <summary>实际读取到的 ini 路径；未命中时为 null。</summary>
    public string? FilePath { get; }

    /// <summary>解析告警（取值非法、读取失败等），由调用方决定是否打印。</summary>
    public string? Warning { get; }

    /// <summary>解析策略，规则与顺序全部委托 <see cref="PclDownloadPolicy.Resolve"/>。</summary>
    /// <param name="explicitIniPath">显式指定的 ini 路径（<c>--policy-file</c>）；非 null 时只认它。</param>
    public static CliPolicy Resolve(string? explicitIniPath = null)
    {
        PclDownloadPolicyResult result = PclDownloadPolicy.Resolve(
            explicitIniPath, baseDirectory: AppContext.BaseDirectory, searchParentDirectory: true);

        string source;
        switch (result.SourceKind)
        {
            case PclPolicySourceKind.Environment:
                source = "env:" + PolicyEnvName;
                break;
            case PclPolicySourceKind.IniFile:
                source = "ini:" + result.IniPath;
                break;
            default:
                source = "default";
                break;
        }

        return new CliPolicy(result.AllowMcDownload, source, result.IniPath, result.Warning);
    }
}

/// <summary>CLI 侧引用 Core 常量的短别名，避免每条注释都写一遍完整类型名。</summary>
internal static class PclCoreDefaults
{
    public const bool DefaultAllowMcDownload = PclDownloadPolicy.DefaultAllowMcDownload;
    public const string PolicyFileName = PclDownloadPolicy.PolicyFileName;
    public const string PolicyEnvName = PclDownloadPolicy.PolicyEnvName;
}
