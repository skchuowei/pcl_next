'资源源可用性门面：把 C# 侧 ResourceProviderRegistry 的结论交给 VB 使用（dev.4 §2.1 A4 / C）。
'职责有三：
'  1. 启动时注入 C# 侧的 ResourceApi.Default 与 ResourceSigningOptions.Default（C# 联网方法的前提）；
'  2. 未配置 PCL_CURSEFORGE_API_KEY 时收敛搜索源，避免向 CurseForge 发必然 403 的请求；
'  3. 给 UI 提供可直接展示的降级文案。
Imports DlPlatforms = PCL.Download.ResourcePlatforms
Imports DlRegistry = PCL.Download.ResourceProviderRegistry

Public Module ResourceSourceGate

    Private ReadOnly InitLock As New Object
    Private _Registry As DlRegistry = Nothing
    Private _HttpApi As PCL.Download.HttpResourceApi = Nothing

    ''' <summary>
    ''' 初始化 C# 侧资源站客户端并注入全局默认请求器。可重复调用，只有首次生效。
    ''' 未调用时，直接使用 C# 联网方法（Provider / 在线反查 / 融合搜索）会抛「未注入 IResourceApi」。
    ''' </summary>
    Public Sub EnsureInitialized()
        If _Registry IsNot Nothing Then Return
        SyncLock InitLock
            If _Registry IsNot Nothing Then Return
            '请求头规则与 ModSecret.SecretHeadersSign 保持一致：UA / Referer / x-api-key
            Dim Signing As New PCL.Download.ResourceSigningOptions With {
                .UserAgent = $"PCL2/{VersionBaseName}.{CInt(BuildType)}",
                .Referer = $"http://{VersionCode}.open.pcl2.server/",
                .CurseForgeApiKey = ModSecret.CurseForgeAPIKey}
            PCL.Download.ResourceSigningOptions.Default = Signing
            _HttpApi = New PCL.Download.HttpResourceApi(Signing)
            PCL.Download.ResourceApi.Default = _HttpApi
            _Registry = DlRegistry.CreateDefault(_HttpApi, Signing)
            If Not Signing.HasCurseForgeApiKey Then
                Logger.Info("未配置 PCL_CURSEFORGE_API_KEY，CurseForge 源将自动降级为 Modrinth")
            End If
        End SyncLock
    End Sub

    ''' <summary>
    ''' 已初始化的 Provider 注册表。首次访问会自动完成 <see cref="EnsureInitialized"/>。
    ''' </summary>
    Public ReadOnly Property Registry As DlRegistry
        Get
            EnsureInitialized()
            Return _Registry
        End Get
    End Property

    ''' <summary>
    ''' CurseForge 源当前是否可用（未配置 API Key 时为 False）。
    ''' 门面自身出错时按「可用」处理，避免因可用性探测失败而影响正常搜索。
    ''' </summary>
    Public ReadOnly Property CurseForgeAvailable As Boolean
        Get
            Try
                Return Registry.Get(DlPlatforms.CurseForge)?.IsAvailable
            Catch ex As Exception
                Logger.Warn(ex, "探测 CurseForge 源可用性失败，按可用处理")
                Return True
            End Try
        End Get
    End Property

    ''' <summary>
    ''' 降级提示文案；无降级时返回 Nothing。可直接展示在 UI 上。
    ''' </summary>
    Public ReadOnly Property DegradationNotice As String
        Get
            If CurseForgeAvailable Then Return Nothing
            Return DlRegistry.CurseForgeDisabledNotice
        End Get
    End Property

    ''' <summary>
    ''' 把「用户期望的源」收敛为「实际可用的源」。
    ''' </summary>
    ''' <param name="Requested">用户选择的源，可能包含不可用的 CurseForge。</param>
    ''' <param name="Notice">输出：因降级产生的提示文案；无降级时为 Nothing。</param>
    Public Function ResolveSources(Requested As ResourcePlatforms, ByRef Notice As String) As ResourcePlatforms
        Notice = Nothing
        Try
            Dim Resolved As DlPlatforms = Registry.ResolveSources(CType(CInt(Requested), DlPlatforms), Notice)
            Return CType(CInt(Resolved), ResourcePlatforms)
        Catch ex As Exception
            '可用性判断失败不应中断搜索：原样放行，由既有的网络错误提示兜底
            Logger.Warn(ex, "收敛资源源失败，按原选择处理")
            Return Requested
        End Try
    End Function

End Module
