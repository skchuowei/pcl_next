''' <summary>
''' pcl-next 的代号（发布通道）。
'''
''' 与《分支与发布规范》的六条长期分支一一对应，命名与分类均以该文档为准：
''' <list type="bullet">
''' <item><c>dev</c>      —— 开发版：功能集成主线，产物不对外分发。</item>
''' <item><c>slush</c>    —— 收敛版：功能冻结线，产物只给内部测试。</item>
''' <item><c>alpha</c>    —— 内测版：小范围内测，允许带已知缺陷发布。</item>
''' <item><c>beta</c>     —— 公测版：面向所有用户试跑，只留可容忍缺陷。</item>
''' <item><c>rc</c>       —— 正式版（RC）：对外正式发布，只允许发版与紧急修复。</item>
''' <item><c>snapshot</c> —— 快照版：正式版的 SP 维护线，只出 SP 补丁包。</item>
''' </list>
'''
''' 代号由 CI 按分支注入编译期常量决定，见《分支与发布规范》§5.6
''' （pcl-next.vbproj 的 <c>PclChannel</c> 属性，本模块的 <c>#If</c> 链）。
''' 本模块是「分支名 ↔ 代号 ↔ 显示名 ↔ 版本号段 ↔ 遥测 ↔ 制品名」的唯一事实源，
''' 其它模块一律从这里取值，不得再自行硬编码代号名。
'''
''' 号段按代号阶梯严格递增（dev < slush < alpha < beta < RC < SP），
''' 且每个代号都占独立区间——dev 与 slush 若共用同一号段，切换时代会被判成版本倒退。
''' 详见《分支与发布规范》§5.2。
''' 所有公开成员一律以 <c>Channel</c> 开头，避免与既有代码里的局部变量重名。
''' </summary>
Public Module ModBuildChannel

    ''' <summary>
    ''' 代号枚举。数值仅用于内部区分，不参与任何比较运算。
    ''' </summary>
    Public Enum Channels As Integer
        Dev = 0
        Slush = 1
        Alpha = 2
        Beta = 3
        Rc = 4
        Snapshot = 5
    End Enum

#Region "编译期注入"

'注意：这里的条件编译常量必须带 PCL_CHANNEL_ 前缀。
'RELEASE / DEBUG 是 .NET SDK 在 Release / Debug 配置下内建定义的常量
'（DefineConstants 会带上 TRACE;RELEASE;...），若直接用 RELEASE 做渠道开关，
'Release 构建下该分支会恒成立，导致除 alpha / beta 之外的渠道全部被编成正式版。
#If PCL_CHANNEL_ALPHA Then
    '内测线（alpha 分支）——《分支与发布规范》§1.3
    Public Const ChannelId As Integer = 2
    Public Const ChannelCode As String = "alpha"
    Public Const ChannelDisplay As String = "内测版"
    Public Const ChannelVersionCodeOffset As Integer = 20
    Public Const ChannelHighestVersionKey As String = "SystemHighestAlphaVersionReg"
#ElseIf PCL_CHANNEL_BETA Then
    '公测线（beta 分支）——《分支与发布规范》§1.4
    Public Const ChannelId As Integer = 3
    Public Const ChannelCode As String = "beta"
    Public Const ChannelDisplay As String = "公测版"
    Public Const ChannelVersionCodeOffset As Integer = 60
    Public Const ChannelHighestVersionKey As String = "SystemHighestBetaVersionReg"
#ElseIf PCL_CHANNEL_RC Then
    '正式发布线（rc 分支）——《分支与发布规范》§1.5
    Public Const ChannelId As Integer = 4
    Public Const ChannelCode As String = "rc"
    Public Const ChannelDisplay As String = "正式版"
    Public Const ChannelVersionCodeOffset As Integer = 90
    Public Const ChannelHighestVersionKey As String = "SystemHighestRcVersionReg"
#ElseIf PCL_CHANNEL_SNAPSHOT Then
    '正式版的 SP 维护线（snapshot 分支）——《分支与发布规范》§1.6、§4
    Public Const ChannelId As Integer = 5
    Public Const ChannelCode As String = "snapshot"
    Public Const ChannelDisplay As String = "正式版"
    Public Const ChannelVersionCodeOffset As Integer = 95
    Public Const ChannelHighestVersionKey As String = "SystemHighestRcVersionReg"
#ElseIf PCL_CHANNEL_SLUSH Then
    '收敛 / 冻结线（slush 分支）——《分支与发布规范》§1.2
    Public Const ChannelId As Integer = 1
    Public Const ChannelCode As String = "slush"
    Public Const ChannelDisplay As String = "收敛版"
    Public Const ChannelVersionCodeOffset As Integer = 10
    Public Const ChannelHighestVersionKey As String = "SystemHighestSlushVersionReg"
#Else
    '功能集成主线（dev 分支）——《分支与发布规范》§1.1
    Public Const ChannelId As Integer = 0
    Public Const ChannelCode As String = "dev"
    Public Const ChannelDisplay As String = "开发版"
    Public Const ChannelVersionCodeOffset As Integer = 0
    Public Const ChannelHighestVersionKey As String = "SystemHighestDevVersionReg"
#End If

#End Region

    ''' <summary>
    ''' 版本号段的周期基址（《分支与发布规范》§5.2：base = 5000 + 100 × 周期序号）。
    ''' 周期 1（2.100）为 5000，周期 2（2.101）为 5100。
    ''' </summary>
    Public Const ChannelVersionCodeBase As Integer = 5000

    ''' <summary>
    ''' 当前构建所属的渠道。
    ''' </summary>
    Public ReadOnly Property Channel As Channels
        Get
            Return CType(ChannelId, Channels)
        End Get
    End Property

    ''' <summary>
    ''' 是否为正式通道（正式版及其 SP 修订）。
    ''' 正式通道才会启用正式版的帮助条目、整合包内置 PCL 等特权。
    ''' </summary>
    Public ReadOnly Property ChannelIsOfficial As Boolean
        Get
            Return Channel = Channels.Rc OrElse Channel = Channels.Snapshot
        End Get
    End Property

    ''' <summary>
    ''' 是否只在内部使用（不对外分发）。
    ''' </summary>
    Public ReadOnly Property ChannelIsInternal As Boolean
        Get
            Return Channel = Channels.Dev OrElse Channel = Channels.Slush
        End Get
    End Property

    ''' <summary>
    ''' 正式版的修订号（版本名的 PATCH 段），SP 序号即此值。
    ''' </summary>
    Public ReadOnly Property ChannelPatchNumber As Integer
        Get
            Dim Parts = VersionBaseName.Split(".")
            If Parts.Length >= 3 Then
                Dim Value As Integer
                If Integer.TryParse(Parts(2), Value) Then Return Value
            End If
            Return 0
        End Get
    End Property

    ''' <summary>
    ''' SP 渠道的序号标注，例如「（SP1）」；其余渠道为空。
    ''' </summary>
    Public ReadOnly Property ChannelSpSuffix As String
        Get
            If Channel = Channels.Snapshot AndAlso ChannelPatchNumber > 0 Then Return $"（SP{ChannelPatchNumber}）"
            Return ""
        End Get
    End Property

    ''' <summary>
    ''' 制品名（不含扩展名）。格式见《分支与发布规范》§5.3：PCL-版本名-代号。
    ''' RC 与 SP 分列两支：RC 用代号本身，SP 用 SP 序号。
    ''' </summary>
    Public ReadOnly Property ChannelArtifactName As String
        Get
            Dim Suffix = ChannelCode
            If Channel = Channels.Rc Then Suffix = "rc"
            If Channel = Channels.Snapshot Then Suffix = If(ChannelPatchNumber > 0, "SP" & ChannelPatchNumber.ToString, "SP")
            Return $"PCL-{VersionBaseName}-{Suffix}"
        End Get
    End Property

End Module
