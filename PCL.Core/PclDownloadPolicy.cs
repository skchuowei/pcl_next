using System;
using System.Collections.Generic;
using System.IO;

namespace PCL.Core
{
    /// <summary>
    /// 策略命中的来源类别。GUI 与 CLI 用同一个枚举判断，不再靠中文字符串偶合分支。
    /// </summary>
    public enum PclPolicySourceKind
    {
        /// <summary>既没有环境变量也没有配置文件，使用内置默认值。</summary>
        Default = 0,

        /// <summary>环境变量 <see cref="PclDownloadPolicy.PolicyEnvName"/>。</summary>
        Environment = 1,

        /// <summary><c>DownloadPolicy.ini</c> 配置文件。</summary>
        IniFile = 2
    }

    /// <summary>
    /// 一次策略解析的结果（不可变）。解析本身不写日志，由前端决定如何呈现。
    /// </summary>
    public sealed class PclDownloadPolicyResult
    {
        internal PclDownloadPolicyResult(bool allowMcDownload, PclPolicySourceKind sourceKind,
            string? iniPath, string? warning)
        {
            AllowMcDownload = allowMcDownload;
            SourceKind = sourceKind;
            IniPath = iniPath;
            Warning = warning;
        }

        /// <summary>是否允许下载 Minecraft 本体与整合包，以及相关的安装、修改入口。</summary>
        public bool AllowMcDownload { get; }

        /// <summary>是否禁用 MC 下载（上游调用点的等价取反写法）。</summary>
        public bool RestrictMcDownload => !AllowMcDownload;

        /// <summary>命中来源类别。</summary>
        public PclPolicySourceKind SourceKind { get; }

        /// <summary>命中的 ini 路径；来源不是 ini 时为 null。</summary>
        public string? IniPath { get; }

        /// <summary>解析过程中的可呈现告警（取值非法、读取失败等）；没有则为 null。</summary>
        public string? Warning { get; }

        /// <summary>人读来源描述，GUI 日志直接使用。</summary>
        public string SourceDisplay
        {
            get
            {
                switch (SourceKind)
                {
                    case PclPolicySourceKind.Environment:
                        return "环境变量 " + PclDownloadPolicy.PolicyEnvName;
                    case PclPolicySourceKind.IniFile:
                        return IniPath ?? PclDownloadPolicy.PolicyFileName;
                    default:
                        return "内置默认值";
                }
            }
        }
    }

    /// <summary>
    /// MC 下载功能的部署期开关——<b>GUI 与 CLI 的唯一实现</b>（合规红线，
    /// 《计划-内核与GUI拆分（CLI）》§5.4）。
    /// <para/>
    /// 上游 PCL 用编译期常量固定关闭该功能，本分支把决策下移到部署期，使合规决策与代码实现解耦。
    /// </para>
    /// <para/>
    /// 生效顺序（先命中者优先）：
    /// <list type="number">
    /// <item>环境变量 <c>PCL_ALLOW_MC_DOWNLOAD</c>：True/1/yes/on 启用，False/0/no/off 禁用</item>
    /// <item><c>DownloadPolicy.ini</c> 中的 <c>AllowMcDownload</c> 键</item>
    /// <item>内置默认值 <see cref="DefaultAllowMcDownload"/></item>
    /// </list>
    /// 这里只做「解析」（纯函数，可重复调用）；GUI 侧首次访问即缓存、运行期改文件不生效的语义
    /// 由宿主转发层 <c>ModDownloadPolicy</c> 保留，CLI 每次进程内只读一次。
    /// </para>
    /// </summary>
    public static class PclDownloadPolicy
    {
        /// <summary>未做任何配置时 MC 下载功能的默认状态。本分支定位是恢复该功能，故默认启用。</summary>
        public const bool DefaultAllowMcDownload = true;

        /// <summary>配置文件名称，位于启动器所在目录。</summary>
        public const string PolicyFileName = "DownloadPolicy.ini";

        /// <summary>覆盖开关的环境变量名。</summary>
        public const string PolicyEnvName = "PCL_ALLOW_MC_DOWNLOAD";

        /// <summary>配置文件中的键名。</summary>
        public const string PolicyIniKey = "AllowMcDownload";

        /// <summary>
        /// 解析策略。
        /// </summary>
        /// <param name="explicitIniPath">
        /// 显式指定的 ini 路径（CLI 的 <c>--policy-file</c>）。非空白时只认它，不再做目录查找。
        /// </param>
        /// <param name="baseDirectory">
        /// 起始目录，默认 <see cref="AppContext.BaseDirectory"/>。
        /// GUI 传启动器目录；CLI 传自身 exe 目录。
        /// </param>
        /// <param name="searchParentDirectory">
        /// 是否额外检查 <paramref name="baseDirectory"/> 的上一级目录。
        /// CLI 产物落在 <c>...\&lt;RID&gt;\cli\</c>，启动器的 ini 在上一级，必须为 true 才能堵住
        /// 「GUI 已禁用、CLI 照下」的合规漏洞；GUI 自身目录即启动器目录，为 false。
        /// </param>
        public static PclDownloadPolicyResult Resolve(string? explicitIniPath = null,
            string? baseDirectory = null, bool searchParentDirectory = false)
        {
            string? warning = null;

            // 1. 环境变量优先
            string? envRaw = Environment.GetEnvironmentVariable(PolicyEnvName);
            if (!string.IsNullOrWhiteSpace(envRaw))
            {
                if (TryParseSwitch(envRaw, out bool envValue))
                    return new PclDownloadPolicyResult(envValue, PclPolicySourceKind.Environment, null, null);

                warning = $"环境变量 {PolicyEnvName} 的取值无效（{envRaw}），已忽略";
            }

            // 2. 配置文件
            foreach (string candidate in CandidateIniPaths(explicitIniPath, baseDirectory, searchParentDirectory))
            {
                try
                {
                    if (!File.Exists(candidate))
                        continue;

                    if (TryReadIniSwitch(candidate, out bool iniValue))
                        return new PclDownloadPolicyResult(iniValue, PclPolicySourceKind.IniFile, candidate, warning);

                    warning ??= $"{PolicyFileName} 中未找到有效的 {PolicyIniKey} 配置项，已忽略";
                }
                catch (Exception ex)
                {
                    warning ??= $"读取 {candidate} 失败，已忽略（{ex.Message}）";
                }
            }

            // 3. 内置默认
            return new PclDownloadPolicyResult(DefaultAllowMcDownload, PclPolicySourceKind.Default, null, warning);
        }

        /// <summary>GUI 使用的「启动器目录下的 ini」预期路径。</summary>
        public static string DefaultPolicyFilePath(string? baseDirectory = null)
        {
            return Path.Combine(ResolveBaseDirectory(baseDirectory), PolicyFileName);
        }

        /// <summary>
        /// 解析开关型文本，支持 True/False、1/0、yes/no、on/off（大小写不敏感）。
        /// </summary>
        public static bool TryParseSwitch(string raw, out bool value)
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    value = true;
                    return true;

                case "false":
                case "0":
                case "no":
                case "off":
                    value = false;
                    return true;

                default:
                    value = false;
                    return false;
            }
        }

        private static IEnumerable<string> CandidateIniPaths(string? explicitIniPath,
            string? baseDirectory, bool searchParentDirectory)
        {
            if (!string.IsNullOrWhiteSpace(explicitIniPath))
            {
                yield return Path.GetFullPath(explicitIniPath);
                yield break;
            }

            string dir = ResolveBaseDirectory(baseDirectory);
            yield return Path.Combine(dir, PolicyFileName);

            if (!searchParentDirectory)
                yield break;

            string? parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parent))
                yield return Path.Combine(parent, PolicyFileName);
        }

        private static string ResolveBaseDirectory(string? baseDirectory)
        {
            return string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
        }

        /// <summary>
        /// 从 ini 风格文件读取 <c>AllowMcDownload</c>。
        /// 以 <c>;</c> <c>#</c> 开头视为注释，<c>[Section]</c> 行跳过（不区分节），与上游一致。
        /// </summary>
        private static bool TryReadIniSwitch(string configPath, out bool value)
        {
            value = false;
            foreach (string rawLine in File.ReadAllLines(configPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                    continue;

                int splitIndex = line.IndexOf('=');
                if (splitIndex <= 0)
                    continue;
                if (!line[..splitIndex].Trim().Equals(PolicyIniKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                return TryParseSwitch(line[(splitIndex + 1)..], out value);
            }
            return false;
        }
    }
}
