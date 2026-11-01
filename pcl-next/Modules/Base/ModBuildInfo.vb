''' <summary>
''' 构建小版本：把「这份产物编译自哪个提交」写进程序，用于事后判断某个 exe 是否包含某次推送。
''' <para>
''' 规则见《分支与发布规范》§5.2.1：构建小版本 = 自本周期起点（最近一次发布标签）以来的提交数 × 0.01，
''' 写成 M.mm（满 100 进 1）。数据由 <c>scripts\Get-BuildSmallVersion.ps1</c> 在编译前生成，
''' 见 <see cref="PclBuildInfoGenerated"/>。
''' </para>
''' <para>
''' 它<strong>不参与</strong> VersionCode 号段，也不影响升级判定——只用于追溯。
''' </para>
''' </summary>
Public Module ModBuildInfo

    ''' <summary>构建小版本，形如 2.27（本周期内第 227 个提交）。无 git 时为 0.00。</summary>
    Public ReadOnly Property BuildSmall As String = PclBuildInfoGenerated.BuildSmall

    ''' <summary>构建时的提交短哈希（7 位）。无 git 时为 nogit。</summary>
    Public ReadOnly Property BuildCommit As String = PclBuildInfoGenerated.BuildCommit

    ''' <summary>构建时工作树是否含未提交的已跟踪改动。为 True 的产物不可用于发布。</summary>
    Public ReadOnly Property BuildIsDirty As Boolean = PclBuildInfoGenerated.BuildIsDirty

    ''' <summary>本周期起点的发布标签；无标签时为空串。</summary>
    Public ReadOnly Property BuildBaseTag As String = PclBuildInfoGenerated.BuildBaseTag

    ''' <summary>自周期起点以来的提交数。</summary>
    Public ReadOnly Property BuildCommitCount As Integer = PclBuildInfoGenerated.BuildCommitCount

    ''' <summary>
    ''' 构建标识，形如 <c>2.27.g211b6dc</c>；含未提交改动时追加 <c>.dirty</c>。
    ''' 与程序集 AssemblyInformationalVersion 一致。
    ''' </summary>
    Public ReadOnly Property BuildToken As String
        Get
            Dim result As String = $"{BuildSmall}.g{BuildCommit}"
            If BuildIsDirty Then result &= ".dirty"
            Return result
        End Get
    End Property

    ''' <summary>
    ''' 日志与界面用的构建说明，形如「构建 2.27，#211b6dc」；
    ''' 含未提交改动时追加「，含未提交改动」以便识别非发布构建。
    ''' </summary>
    Public ReadOnly Property Display As String
        Get
            Dim result As String = $"构建 {BuildSmall}，#{BuildCommit}"
            If BuildIsDirty Then result &= "，含未提交改动"
            Return result
        End Get
    End Property

End Module
