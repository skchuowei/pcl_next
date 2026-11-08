'资源搜索的 C# 数据源加载器（dev.4 §2.1 C：Phase 4 首步）。
'与 ResourceSearcher.GetLoader 的接口完全一致，页面只需替换 Loader 的构造处，
'内部把搜索交给 C# 侧 ProviderRegistry：源收敛（CF 无 Key 时降级）→ 融合搜索 → 结果回填。
Imports DlSearchResult = PCL.Download.ResourceSearcher.SearchResult
Imports DlSearchRequest = PCL.Download.ResourceProviderSearchRequest

Public Module ResourceProviderSearch

    ''' <summary>每次至少向 C# 侧要一页，避免目标数量已满足时传 0 导致 Provider 内部按 1 处理。</summary>
    Private Const DEFAULT_PAGE_SIZE As Integer = 40

    ''' <summary>
    ''' 构造加载器。签名与 <see cref="ResourceSearcher.GetLoader"/> 一致，便于页面侧就地替换。
    ''' </summary>
    Public Function GetLoader(InputDelegate As Func(Of ResourceSearcher.SearchRequest)) As LoaderTask(Of ResourceSearcher.SearchRequest, Integer)
        Return New LoaderTask(Of ResourceSearcher.SearchRequest, Integer)("社区资源获取", AddressOf Run, InputDelegate) With {.ReloadTimeout = 60 * 1000}
    End Function

    Private Sub Run(Task As LoaderTask(Of ResourceSearcher.SearchRequest, Integer))
        Dim Request As ResourceSearcher.SearchRequest = Task.Input
        Dim Storage = Request.Storage '避免其他线程对 Request.Storage 重新进行了赋值

        '---------- 前置检查（与 VB 版一致） ----------
        If Storage.Results.Count >= Request.TargetResultCount Then
            Logger.Info($"已有 {Storage.Results.Count} 个结果，多于所需的 {Request.TargetResultCount} 个结果，结束处理")
            Return
        End If
        If Not Request.CanContinue Then
            If Not Storage.Results.Any() Then Throw New Exception("没有符合条件的结果")
            Logger.Info($"已有 {Storage.Results.Count} 个结果，少于所需的 {Request.TargetResultCount} 个结果，但无法继续获取，结束处理")
            Return
        End If

        '---------- 准备 C# 侧状态 ----------
        'C# 侧的 Storage 承载翻页偏移、总量与跨页去重；与 VB 的 Storage 同生共死
        Dim CSharpStorage As DlSearchResult = Storage.CSharpStorage
        If CSharpStorage Is Nothing Then
            CSharpStorage = New DlSearchResult
            Storage.CSharpStorage = CSharpStorage
        End If
        '本次还需要多少个结果；C# 侧不足时会自行递归翻页补齐
        Dim PageSize As Integer = Math.Max(DEFAULT_PAGE_SIZE, Request.TargetResultCount - CSharpStorage.Results.Count)

        Dim CsRequest As New DlSearchRequest With {
            .Type = CType(CInt(Request.Type), PCL.Download.ResourceTypes),
            .SearchText = Request.SearchText,
            .Tag = If(Request.Tag, ""),
            .ModLoaders = CType(CInt(Request.ModLoaders), PCL.Download.ModLoaders),
            .GameVersion = Request.GameVersion,
            .PageSize = PageSize,
            .Storage = CSharpStorage}

        '---------- 搜索 ----------
        Dim Notice As String = Nothing
        Dim Page = ResourceSourceGate.Registry.Search(CsRequest, CType(CInt(Request.Sources), PCL.Download.ResourcePlatforms), Notice)
        If Not String.IsNullOrEmpty(Notice) Then Logger.Info($"资源搜索已降级：{Notice}")

        '---------- 回填（全量重建，保证顺序与去重结果与 C# 侧完全一致） ----------
        Storage.Results.Clear()
        For Each Project In CSharpStorage.Results
            Storage.Results.Add(New ResourceProject(Project))
        Next
        Storage.CurseForgeOffset = CSharpStorage.CurseForgeOffset
        Storage.CurseForgeTotal = CSharpStorage.CurseForgeTotal
        Storage.ModrinthOffset = CSharpStorage.ModrinthOffset
        Storage.ModrinthTotal = CSharpStorage.ModrinthTotal
        Storage.ErrorMessage = CSharpStorage.ErrorMessage
        Logger.Info($"资源搜索完成：本页新增 {Page.Items.Count} 个，累计 {Storage.Results.Count} 个" &
                    $"（CurseForge {Storage.CurseForgeOffset}/{Storage.CurseForgeTotal}，Modrinth {Storage.ModrinthOffset}/{Storage.ModrinthTotal}）")
    End Sub

End Module
