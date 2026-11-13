
namespace PCL
{
    /// <summary>
/// 构建小版本：把「这份产物编译自哪个提交」写进程序，用于事后判断某个 exe 是否包含某次推送。
/// <para>
/// 规则见《分支与发布规范》§5.2.1：构建小版本 = 自<strong>固定锚点</strong>以来的提交数 × 0.01，
/// 写成 M.mm（满 100 进 1）。锚点是首个 dev 发布（v2.100.0.1-dev.1）的提交，永不移动，
/// 因此编号随提交单调递增——<strong>后提交的产物编号一定大于先提交的</strong>，
/// 打新的发布标签也不会归零。数据由 <c>scripts\Get-BuildSmallVersion.ps1</c> 在编译前生成，
/// 见 <see cref="PclBuildInfoGenerated"/>。
/// </para>
/// <para>
/// 它<strong>不参与</strong> VersionCode 号段，也不影响升级判定——只用于追溯。
/// </para>
/// </summary>
    public static class ModBuildInfo
    {

        /// <summary>构建小版本，形如 2.27（锚点以来第 227 个提交）。无 git 时为 0.00。</summary>
        public static string BuildSmall { get; private set; } = PCL.PclBuildInfoGenerated.BuildSmall;

        /// <summary>
    /// 构建小版本的数值形式（等于锚点以来的提交数）。
    /// 比较新旧必须用它：M.mm 是显示形式，字符串比较会把 <c>10.00</c> 判成小于 <c>2.00</c>。
    /// </summary>
        public static int BuildSmallNumber { get; private set; } = PCL.PclBuildInfoGenerated.BuildSmallNumber;

        /// <summary>构建时的提交短哈希（7 位）。无 git 时为 nogit。</summary>
        public static string BuildCommit { get; private set; } = PCL.PclBuildInfoGenerated.BuildCommit;

        /// <summary>构建时工作树是否含未提交的已跟踪改动。为 True 的产物不可用于发布。</summary>
        public static bool BuildIsDirty { get; private set; } = PCL.PclBuildInfoGenerated.BuildIsDirty;

        /// <summary>编号锚点的提交短哈希（永久固定）。</summary>
        public static string BuildAnchor { get; private set; } = PCL.PclBuildInfoGenerated.BuildAnchor;

        /// <summary>自锚点以来的提交数。</summary>
        public static int BuildCommitCount { get; private set; } = PCL.PclBuildInfoGenerated.BuildCommitCount;

        /// <summary>构建时 HEAD 的完整 40 位哈希，便于直接 git show 定位提交。</summary>
        public static string BuildCommitFull { get; private set; } = PCL.PclBuildInfoGenerated.BuildCommitFull;

        /// <summary>源码内容指纹（12 位 hex）：同一提交号下内容不同即不同。</summary>
        public static string BuildContentId { get; private set; } = PCL.PclBuildInfoGenerated.BuildContentId;

        /// <summary>本版本 UUID v4，与内容指纹绑定（内容不变则复用）。</summary>
        public static string BuildUuid { get; private set; } = PCL.PclBuildInfoGenerated.BuildUuid;

        /// <summary>
    /// 完整构建标识，形如 <c>2.27.g211b6dc.c0f1e2d3a4b5[.dirty]</c>，
    /// 与程序集 AssemblyInformationalVersion 一致。由编译期脚本生成、不在运行时拼接，
    /// 避免公式在两处漂移（2026-11-13）。
    /// </summary>
        public static string BuildToken { get; private set; } = PCL.PclBuildInfoGenerated.BuildToken;

        /// <summary>
    /// 日志与界面用的构建说明，形如「构建 2.27，#211b6dc」；
    /// 含未提交改动时追加「，含未提交改动」以便识别非发布构建。
    /// </summary>
        public static string Display
        {
            get
            {
                string result = $"构建 {BuildSmall}，#{BuildCommit}，内容 {BuildContentId}，UUID {BuildUuid}";
                if (BuildIsDirty)
                    result += "，含未提交改动";
                return result;
            }
        }

    }
}