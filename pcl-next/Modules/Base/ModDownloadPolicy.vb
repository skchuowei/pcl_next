'本文件由本分支新增：把上游因合规原因在编译期关闭的 MC 下载功能，改为部署期可配置。

''' <summary>
''' Minecraft 下载功能的部署期开关。
''' 上游 PCL 用编译期常量 <c>ModSecret.RestrictMcDownload = True</c> 固定关闭该功能，
''' 本分支将该决策从“编译期”下移到“部署期”，使其可在不重新编译的前提下切换，
''' 从而把合规决策与代码实现解耦。
''' </summary>
''' <remarks>
''' 生效顺序（先命中者优先）：
''' <list type="number">
''' <item>环境变量 <c>PCL_ALLOW_MC_DOWNLOAD</c>：True/1/yes/on 为启用，False/0/no/off 为禁用</item>
''' <item>启动器目录下的 <c>DownloadPolicy.ini</c>，文件内写 <c>AllowMcDownload=False</c> 之类的键值对</item>
''' <item>内置默认值 <see cref="DefaultAllowMcDownload"/></item>
''' </list>
''' 每次启动仅在首次访问时读取一次，随后使用缓存，因此运行期修改配置文件不会立即生效。
''' </remarks>
Friend Module ModDownloadPolicy

    ''' <summary>
    ''' 未做任何配置时，MC 下载功能的默认状态。
    ''' 本分支的定位是恢复该功能，故默认启用；若要回到上游的禁用状态，
    ''' 只需在 DownloadPolicy.ini 中写入 <c>AllowMcDownload=False</c>。
    ''' </summary>
    Public Const DefaultAllowMcDownload As Boolean = True

    ''' <summary>
    ''' 配置文件名称，位于启动器所在目录。
    ''' </summary>
    Public Const PolicyFileName As String = "DownloadPolicy.ini"

    ''' <summary>
    ''' 覆盖开关的环境变量名。
    ''' </summary>
    Public Const PolicyEnvName As String = "PCL_ALLOW_MC_DOWNLOAD"

    Private ReadOnly LoadLock As New Object
    Private LoadedAllowMcDownload As Boolean?
    Private LoadedPolicySource As String = ""

    ''' <summary>
    ''' 是否允许下载 Minecraft 本体与整合包，以及与之相关的安装、修改入口。
    ''' </summary>
    Public ReadOnly Property AllowMcDownload As Boolean
        Get
            EnsureLoaded()
            Return LoadedAllowMcDownload.Value
        End Get
    End Property

    ''' <summary>
    ''' 是否禁用 MC 下载相关功能。
    ''' 保留该名称以便沿用上游的调用点写法（等价的取反），减少与上游的合并冲突。
    ''' </summary>
    Public ReadOnly Property RestrictMcDownload As Boolean
        Get
            Return Not AllowMcDownload
        End Get
    End Property

    ''' <summary>
    ''' 当前策略的来源描述，便于日志与问题排查。
    ''' </summary>
    Public ReadOnly Property PolicySource As String
        Get
            EnsureLoaded()
            Return LoadedPolicySource
        End Get
    End Property

    ''' <summary>
    ''' 配置文件的预期路径（启动器所在目录）。
    ''' </summary>
    Public ReadOnly Property PolicyFilePath As String
        Get
            Return IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PolicyFileName)
        End Get
    End Property

    Private Sub EnsureLoaded()
        SyncLock LoadLock
            If LoadedAllowMcDownload IsNot Nothing Then Return
            Dim Allow As Boolean = DefaultAllowMcDownload
            Dim Source As String = "内置默认值"

            '1. 环境变量优先
            Dim EnvRaw As String = Environment.GetEnvironmentVariable(PolicyEnvName)
            If Not String.IsNullOrWhiteSpace(EnvRaw) Then
                Dim EnvValue As Boolean
                If TryParseSwitch(EnvRaw, EnvValue) Then
                    Allow = EnvValue
                    Source = "环境变量 " & PolicyEnvName
                Else
                    Logger.Warn($"环境变量 {PolicyEnvName} 的取值无效（{EnvRaw}），已忽略")
                End If
            End If

            '2. 启动器目录下的配置文件
            If Source = "内置默认值" Then
                Try
                    Dim ConfigPath As String = PolicyFilePath
                    If IO.File.Exists(ConfigPath) Then
                        Dim FileValue As Boolean
                        If TryReadIniSwitch(ConfigPath, FileValue) Then
                            Allow = FileValue
                            Source = ConfigPath
                        Else
                            Logger.Warn($"{PolicyFileName} 中未找到有效的 AllowMcDownload 配置项，已忽略")
                        End If
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, $"读取 {PolicyFileName} 失败，将使用默认的 MC 下载策略")
                End Try
            End If

            LoadedAllowMcDownload = Allow
            LoadedPolicySource = Source
            Logger.Info($"MC 下载功能：{If(Allow, "已启用", "已禁用")}（来源：{Source}）")
        End SyncLock
    End Sub

    ''' <summary>
    ''' 解析开关型文本，支持 True/False、1/0、yes/no、on/off。
    ''' </summary>
    Private Function TryParseSwitch(Raw As String, ByRef Value As Boolean) As Boolean
        Select Case Raw.Trim().ToLowerInvariant()
            Case "true", "1", "yes", "on"
                Value = True
                Return True
            Case "false", "0", "no", "off"
                Value = False
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' 从 ini 风格文件中读取 AllowMcDownload 开关。找到合法取值则返回 True。
    ''' 以 ; # 开头的行视为注释，[Section] 行会被跳过（不区分节）。
    ''' </summary>
    Private Function TryReadIniSwitch(ConfigPath As String, ByRef Value As Boolean) As Boolean
        For Each RawLine As String In IO.File.ReadAllLines(ConfigPath)
            Dim Line As String = RawLine.Trim()
            If Line.Length = 0 Then Continue For
            If Line.StartsWith(";") OrElse Line.StartsWith("#") OrElse Line.StartsWith("[") Then Continue For
            Dim SplitIndex As Integer = Line.IndexOf("="c)
            If SplitIndex <= 0 Then Continue For
            If Not Line.Substring(0, SplitIndex).Trim().Equals("AllowMcDownload", StringComparison.OrdinalIgnoreCase) Then Continue For
            Return TryParseSwitch(Line.Substring(SplitIndex + 1), Value)
        Next
        Return False
    End Function

End Module
