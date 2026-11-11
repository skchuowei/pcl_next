
namespace PCL
{
    /// <summary>
/// pcl-next 的代号（发布通道）。
/// 
/// 与《分支与发布规范》的六条长期分支一一对应，命名与分类均以该文档为准：
/// <list type="bullet">
/// <item><c>dev</c>      —— 开发版：功能集成主线，产物不对外分发。</item>
/// <item><c>slush</c>    —— 收敛版：功能冻结线，产物只给内部测试。</item>
/// <item><c>alpha</c>    —— 内测版：小范围内测，允许带已知缺陷发布。</item>
/// <item><c>beta</c>     —— 公测版：面向所有用户试跑，只留可容忍缺陷。</item>
/// <item><c>rc</c>       —— 正式版（RC）：对外正式发布，只允许发版与紧急修复。</item>
/// <item><c>snapshot</c> —— 快照版：正式版的 SP 维护线，只出 SP 补丁包。</item>
/// </list>
/// 
/// 代号由 CI 按分支注入编译期常量决定，见《分支与发布规范》§5.6
/// （pcl-next.vbproj 的 <c>PclChannel</c> 属性，本模块的 <c>#If</c> 链）。
/// 本模块是「分支名 ↔ 代号 ↔ 显示名 ↔ 版本号段 ↔ 遥测 ↔ 制品名」的唯一事实源，
/// 其它模块一律从这里取值，不得再自行硬编码代号名。
/// 
/// 号段按代号阶梯严格递增（dev < slush < alpha < beta < RC < SP），
/// 且每个代号都占独立区间——dev 与 slush 若共用同一号段，切换时代会被判成版本倒退。
/// 详见《分支与发布规范》§5.2。
/// 所有公开成员一律以 <c>Channel</c> 开头，避免与既有代码里的局部变量重名。
/// </summary>
    public static class ModBuildChannel
    {

        /// <summary>
    /// 代号枚举。数值仅用于内部区分，不参与任何比较运算。
    /// </summary>
        public enum Channels : int
        {
            Dev = 0,
            Slush = 1,
            Alpha = 2,
            Beta = 3,
            Rc = 4,
            Snapshot = 5
        }

        #region 编译期注入

        // 注意：这里的条件编译常量必须带 PCL_CHANNEL_ 前缀。
        // RELEASE / DEBUG 是 .NET SDK 在 Release / Debug 配置下内建定义的常量
        // （DefineConstants 会带上 TRACE;RELEASE;...），若直接用 RELEASE 做渠道开关，
        // Release 构建下该分支会恒成立，导致除 alpha / beta 之外的渠道全部被编成正式版。
        // 原 VB 的 #If PCL_CHANNEL_ALPHA … #Else 链被转换器打平，只剩 dev 分支，
        // 会让 -p:PclChannel=Alpha/Beta/Rc/Snapshot/Slush 编出的程序全部自称「开发版」。
        // 这里按 C# 预处理器还原：常量由 csproj 的 PclChannel 属性注入 DefineConstants。
#if PCL_CHANNEL_ALPHA
        // 内测线（alpha 分支）——《分支与发布规范》§1.3
        public const int ChannelId = 2;
        public const string ChannelCode = "alpha";
        public const string ChannelDisplay = "内测版";
        public const int ChannelVersionCodeOffset = 20;
        public const string ChannelHighestVersionKey = "SystemHighestAlphaVersionReg";
#elif PCL_CHANNEL_BETA
        // 公测线（beta 分支）——《分支与发布规范》§1.4
        public const int ChannelId = 3;
        public const string ChannelCode = "beta";
        public const string ChannelDisplay = "公测版";
        public const int ChannelVersionCodeOffset = 60;
        public const string ChannelHighestVersionKey = "SystemHighestBetaVersionReg";
#elif PCL_CHANNEL_RC
        // 正式发布线（rc 分支）——《分支与发布规范》§1.5
        public const int ChannelId = 4;
        public const string ChannelCode = "rc";
        public const string ChannelDisplay = "正式版";
        public const int ChannelVersionCodeOffset = 90;
        public const string ChannelHighestVersionKey = "SystemHighestRcVersionReg";
#elif PCL_CHANNEL_SNAPSHOT
        // 正式版的 SP 维护线（snapshot 分支）——《分支与发布规范》§1.6、§4
        public const int ChannelId = 5;
        public const string ChannelCode = "snapshot";
        public const string ChannelDisplay = "正式版";
        public const int ChannelVersionCodeOffset = 95;
        public const string ChannelHighestVersionKey = "SystemHighestRcVersionReg";
#elif PCL_CHANNEL_SLUSH
        // 收敛 / 冻结线（slush 分支）——《分支与发布规范》§1.2
        public const int ChannelId = 1;
        public const string ChannelCode = "slush";
        public const string ChannelDisplay = "收敛版";
        public const int ChannelVersionCodeOffset = 10;
        public const string ChannelHighestVersionKey = "SystemHighestSlushVersionReg";
#else
        // 功能集成主线（dev 分支）——《分支与发布规范》§1.1
        public const int ChannelId = 0;
        public const string ChannelCode = "dev";
        public const string ChannelDisplay = "开发版";
        public const int ChannelVersionCodeOffset = 0;
        public const string ChannelHighestVersionKey = "SystemHighestDevVersionReg";
#endif
        #endregion

        /// <summary>
    /// 版本号段的周期基址（《分支与发布规范》§5.2：base = 5000 + 100 × 周期序号）。
    /// 周期 1（2.100）为 5000，周期 2（2.101）为 5100。
    /// </summary>
        public const int ChannelVersionCodeBase = 5000;

        /// <summary>
    /// 当前构建所属的渠道。
    /// </summary>
        public static Channels Channel
        {
            get
            {
                // VB 允许 Integer ↔ 枚举隐式互转，C# 只允许常量 0 隐式转成枚举。
                // dev 渠道的 ChannelId 恰好是 0 才没暴露问题，Alpha/Beta/Rc 等渠道会编译失败，故显式强转。
                return (Channels)ChannelId;
            }
        }

        /// <summary>
    /// 是否为正式通道（正式版及其 SP 修订）。
    /// 正式通道才会启用正式版的帮助条目、整合包内置 PCL 等特权。
    /// </summary>
        public static bool ChannelIsOfficial
        {
            get
            {
                return Channel == Channels.Rc || Channel == Channels.Snapshot;
            }
        }

        /// <summary>
    /// 是否只在内部使用（不对外分发）。
    /// </summary>
        public static bool ChannelIsInternal
        {
            get
            {
                return Channel == Channels.Dev || Channel == Channels.Slush;
            }
        }

        /// <summary>
    /// 正式版的修订号（版本名的 PATCH 段），SP 序号即此值。
    /// </summary>
        public static int ChannelPatchNumber
        {
            get
            {
                string[] Parts = ModBase.VersionBaseName.Split(".");
                if (Parts.Length >= 3)
                {
                    int Value;
                    if (int.TryParse(Parts[2], out Value))
                        return Value;
                }
                return 0;
            }
        }

        /// <summary>
    /// SP 渠道的序号标注，例如「（SP1）」；其余渠道为空。
    /// </summary>
        public static string ChannelSpSuffix
        {
            get
            {
                if (Channel == Channels.Snapshot && ChannelPatchNumber > 0)
                    return $"（SP{ChannelPatchNumber}）";
                return "";
            }
        }

        /// <summary>
    /// 制品名（不含扩展名）。格式见《分支与发布规范》§5.3：PCL-版本名-代号。
    /// RC 与 SP 分列两支：RC 用代号本身，SP 用 SP 序号。
    /// </summary>
        public static string ChannelArtifactName
        {
            get
            {
                string Suffix = ChannelCode;
                if (Channel == Channels.Rc)
                    Suffix = "rc";
                if (Channel == Channels.Snapshot)
                    Suffix = ChannelPatchNumber > 0 ? "SP" + ChannelPatchNumber.ToString() : "SP";
                return $"PCL-{ModBase.VersionBaseName}-{Suffix}";
            }
        }

    }
}