using System;
using MeloongCore;
using PCL.Core;

namespace PCL
{
    // 本文件由本分支新增：把上游因合规原因在编译期关闭的 MC 下载功能，改为部署期可配置。
    //
    // 2026-11-14「GUI+CLI 开壳」：解析逻辑已下沉到 PCL.Core/PclDownloadPolicy.cs，
    // 本类降为**宿主转发层**——GUI 读 ini 的目录、首次访问即缓存的语义留在这里，
    // 规则本身（env → ini → 默认、取值解析、键名）只有 Core 一份实现，
    // 与 PCL.Cli 的 CliPolicy 共用，杜绝「GUI 已禁用、CLI 照下」的合规漏洞。

    /// <summary>
    /// Minecraft 下载功能的部署期开关（GUI 侧门面）。
    /// <para/>
    /// 生效顺序见 <see cref="PclDownloadPolicy.Resolve"/>：环境变量 <c>PCL_ALLOW_MC_DOWNLOAD</c> →
    /// 启动器目录下的 <c>DownloadPolicy.ini</c> → 内置默认值 <see cref="DefaultAllowMcDownload"/>。
    /// </para>
    /// <remarks>
    /// 每次启动仅在首次访问时读取一次，随后使用缓存，因此运行期修改配置文件不会立即生效。
    /// </remarks>
    /// </summary>
    internal static class ModDownloadPolicy
    {
        /// <summary>未做任何配置时的默认状态（转发 Core 常量，保证两端默认值一致）。</summary>
        public const bool DefaultAllowMcDownload = PclDownloadPolicy.DefaultAllowMcDownload;

        /// <summary>配置文件名称。</summary>
        public const string PolicyFileName = PclDownloadPolicy.PolicyFileName;

        /// <summary>覆盖开关的环境变量名。</summary>
        public const string PolicyEnvName = PclDownloadPolicy.PolicyEnvName;

        private static readonly object LoadLock = new();

        private static PclDownloadPolicyResult? _resolved;

        /// <summary>
        /// 是否允许下载 Minecraft 本体与整合包，以及与之相关的安装、修改入口。
        /// </summary>
        public static bool AllowMcDownload => GetResult().AllowMcDownload;

        /// <summary>
        /// 是否禁用 MC 下载相关功能。
        /// 保留该名称以便沿用上游的调用点写法（等价的取反），减少与上游的合并冲突。
        /// </summary>
        public static bool RestrictMcDownload => GetResult().RestrictMcDownload;

        /// <summary>当前策略的来源描述，便于日志与问题排查。</summary>
        public static string PolicySource => GetResult().SourceDisplay;

        /// <summary>配置文件的预期路径（启动器所在目录）。</summary>
        public static string PolicyFilePath => PclDownloadPolicy.DefaultPolicyFilePath();

        private static PclDownloadPolicyResult GetResult()
        {
            lock (LoadLock)
            {
                if (_resolved is not null)
                    return _resolved;

                // GUI 自身目录即启动器目录，不向上查找——向上查找只属于 CLI 的产物布局。
                PclDownloadPolicyResult result = PclDownloadPolicy.Resolve(searchParentDirectory: false);
                _resolved = result;

                if (!string.IsNullOrEmpty(result.Warning))
                    Logger.Warn(result.Warning);
                Logger.Info($"MC 下载功能：{(result.AllowMcDownload ? "已启用" : "已禁用")}（来源：{result.SourceDisplay}）");
                return result;
            }
        }
    }
}
