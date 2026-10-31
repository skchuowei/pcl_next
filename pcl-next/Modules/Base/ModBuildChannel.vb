''' <summary>
''' pcl-next 的渠道（发布通道）代号。
'''
''' 与《分支与发布规范》的六条长期分支一一对应，命名与分类均以该文档为准：
''' <list type="bullet">
''' <item><c>dev</c>      —— 开发版：功能集成主线，产物不对外分发。</item>
''' <item><c>slush</c>    —— 收敛版：功能冻结线，产物只给内部测试。</item>
''' <item><c>alpha</c>    —— 内测版：小范围内测，允许带已知缺陷发布。</item>
''' <item><c>beta</c>     —— 公测版：面向所有用户试跑，只留可容忍缺陷。</item>
''' <item><c>pc</c>       —— 正式版：对外正式发布，只允许发版与紧急修复。</item>
''' <item><c>snapshot</c> —— 快照版：正式版的 SP 维护线，只出 SP 补丁包。</item>
''' </list>
'''
''' 渠道由 CI 按分支注入编译期常量决定，见《分支与发布规范》§5.4
''' （pcl-next.vbproj 的 <c>PclChannel</c> 属性，本模块的 <c>#If</c> 链）。
''' 本模块是「分支名 ↔ 渠道 ↔ 显示名 ↔ 版本号段 ↔ 制品名」的唯一事实源，
''' 其它模块一律从这里取值，不得再自行硬编码渠道名。
'''
''' 所有公开成员一律以 <c>Channel</c> 开头，避免与既有代码里的局部变量重名。
''' </summary>
Public Module ModBuildChannel

    ''' <summary>
    ''' 渠道枚举。数值仅用于内部区分，不参与任何比较运算。
    ''' </summary>
    Public Enum Channels As Integer
        Dev = 0
        Slush = 1
        Alpha = 2
        Beta = 3
        Pc = 4
        Snapshot = 5
    End Enum

#Region "编译期注入"

#If ALPHA Then
    '内测线（alpha 分支）——《分支与发布规范》§1.3
    Public Const ChannelId As Integer = 2
    Public Const ChannelCode As String = "alpha"
    Public Const ChannelDisplay As String = "内测版"
    Public Const ChannelVersionCodeOffset As Integer = 0
    Public Const ChannelAllowTelemetry As Boolean = False
    Public Const ChannelHighestVersionKey As String = "SystemHighestAlphaVersionReg"
#ElseIf BETA Then
    '公测线（beta 分支）——《分支与发布规范》§1.4
    Public Const ChannelId As Integer = 3
    Public Const ChannelCode As String = "beta"
    Public Const ChannelDisplay As String = "公测版"
    Public Const ChannelVersionCodeOffset As Integer = 40
    Public Const ChannelAllowTelemetry As Boolean = True
    Public Const ChannelHighestVersionKey As String = "SystemHighestBetaVersionReg"
#ElseIf RELEASE Then
    '正式发布线（pc 分支）——《分支与发布规范》§1.5
    Public Const ChannelId As Integer = 4
    Public Const ChannelCode As String = "pc"
    Public Const ChannelDisplay As String = "正式版"
    Public Const ChannelVersionCodeOffset As Integer = 70
    Public Const ChannelAllowTelemetry As Boolean = True
    Public Const ChannelHighestVersionKey As String = "SystemHighestPcVersionReg"
#ElseIf SNAPSHOT Then
    '正式版的 SP 维护线（snapshot 分支）——《分支与发布规范》§1.6、§4
    Public Const ChannelId As Integer = 5
    Public Const ChannelCode As String = "snapshot"
    Public Const ChannelDisplay As String = "正式版"
    Public Const ChannelVersionCodeOffset As Integer = 80
    Public Const ChannelAllowTelemetry As Boolean = True
    Public Const ChannelHighestVersionKey As String = "SystemHighestPcVersionReg"
#ElseIf SLUSH Then
    '收敛 / 冻结线（slush 分支）——《分支与发布规范》§1.2
    Public Const ChannelId As Integer = 1
    Public Const ChannelCode As String = "slush"
    Public Const ChannelDisplay As String = "收敛版"
    Public Const ChannelVersionCodeOffset As Integer = -1
    Public Const ChannelAllowTelemetry As Boolean = False
    Public Const ChannelHighestVersionKey As String = "SystemHighestDevVersionReg"
#Else
    '功能集成主线（dev 分支）——《分支与发布规范》§1.1
    Public Const ChannelId As Integer = 0
    Public Const ChannelCode As String = "dev"
    Public Const ChannelDisplay As String = "开发版"
    Public Const ChannelVersionCodeOffset As Integer = -1
    Public Const ChannelAllowTelemetry As Boolean = False
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
            Return Channel = Channels.Pc OrElse Channel = Channels.Snapshot
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
    ''' 制品名（不含扩展名）。格式见《分支与发布规范》§5.3：PCL-版本名-渠道。
    ''' </summary>
    Public ReadOnly Property ChannelArtifactName As String
        Get
            Dim Suffix = ChannelCode
            If Channel = Channels.Snapshot Then Suffix = If(ChannelPatchNumber > 0, "SP" & ChannelPatchNumber.ToString, "SP")
            Return $"PCL-{VersionBaseName}-{Suffix}"
        End Get
    End Property

End Module
