Imports System.Text.Json
Imports System.Text.Json.Nodes

''' <summary>
''' 实例 Mod 列表的加载与联网反查。
''' <para/>
''' Phase 4：扫描、去重、排序与联网反查的**取数与判定**已全部迁至 C#（<c>PCL.Download.LocalResourceScanner</c> /
''' <c>LocalResourceOnlineLoader</c>），这里只保留 LoaderTask 外壳、等待更新、UI 文案、缓存落盘与 UI 刷新。
''' </para>
''' </summary>
Public Module LocalResourceLoaders

    '加载 Mod 列表
    Public LocalResourceLoader As New LoaderTask(Of String, List(Of LocalResourceFile))("LocalResourceLoader", AddressOf LocalResourceFileLoad)
    Private Sub LocalResourceFileLoad(Loader As LoaderTask(Of String, List(Of LocalResourceFile)))
        Try
            RunInUiWait(Sub() If FrmInstanceMod IsNot Nothing Then FrmInstanceMod.Load.ShowProgress = False)

            '等待 Mod 更新完成
            If PageInstanceMod.UpdatingInstanceModFolders.Contains(Loader.Input) Then
                Logger.Info($"等待 Mod 更新完成后才能继续加载 Mod 列表：{Loader.Input}")
                Try
                    RunInUiWait(Sub() If FrmInstanceMod IsNot Nothing Then FrmInstanceMod.Load.Text = "正在更新 Mod")
                    Do Until Not PageInstanceMod.UpdatingInstanceModFolders.Contains(Loader.Input)
                        If Loader.IsCanceled Then Return
                        Thread.Sleep(100)
                    Loop
                Finally
                    RunInUiWait(Sub() If FrmInstanceMod IsNot Nothing Then FrmInstanceMod.Load.Text = "正在加载 Mod 列表")
                End Try
                FrmInstanceMod.LoaderRun(LoaderFolderRunType.UpdateOnly)
            End If

            '扫描 Mod 文件夹：递归枚举、Forge 1.13- 的版本号子目录特例、同名启用/禁用去重、
            '按文件名排序，全部在 C# 侧（LocalResourceScanner）
            Dim Instance = PageInstanceLeft.Instance
            Dim LegacySubfolder As String = Nothing
            If Instance IsNot Nothing AndAlso Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) AndAlso
               Instance.Version.Vanilla.Major < 13 Then
                LegacySubfolder = $"1.{Instance.Version.Vanilla.Major}.{Instance.Version.Vanilla.Build}"
            End If
            Dim Scanned = PCL.Download.LocalResourceScanner.Scan(Loader.Input,
                New PCL.Download.LocalResourceScanner.Options With {
                    .IsLegacyForgeSubfolder = Function(Name As String) Name = LegacySubfolder '为 Nothing 时恒为 False，等价于「子目录一律不收录」
                })
            If Loader.IsCanceled Then Return

            '投影回 VB 侧的条目：文件信息、OnUpdate 事件与跨运行的指纹缓存（Cache\ModHash.ini）都还在这边
            Dim ModList As New List(Of LocalResourceFile)(Scanned.Count)
            For Each Entry In Scanned
                ModList.Add(New LocalResourceFile(Entry.File.FullName))
            Next

            '获取本地文件缓存
            Dim CachePath As String = PathTemp & "Cache\LocalMod.json"
            Dim Cache As New JObject
            Try
                Dim CacheContent As String = FileUtils.TryReadAsString(CachePath)
                If Not String.IsNullOrWhiteSpace(CacheContent) Then
                    Cache = CacheContent.DeserializeJson()
                    If Not Cache.ContainsKey("version") OrElse Cache("version").ToObject(Of Integer) <> Versions.LocalModCacheVersion Then
                        Logger.Warn($"本地 Mod 信息缓存版本已过期，将弃用这些缓存信息")
                        Cache = New JObject
                    End If
                End If
            Catch ex As Exception
                Logger.Warn(ex, "读取本地 Mod 信息缓存失败，已重置")
                Cache = New JObject
            End Try
            Cache("version") = Versions.LocalModCacheVersion

            '回设
            If Loader.IsCanceled Then Return
            Loader.Output = ModList

            '开始联网加载
            'TODO: 添加信息获取中提示
            LocalResourceOnlineLoader.Start((ModList, Cache, PageInstanceLeft.Instance.Version), IsForceRestart:=True)

        Catch ex As Exception
            Logger.Warn(ex, "Mod 列表加载失败")
            Throw
        End Try
    End Sub

    '联网加载 Mod 详情
    Public LocalResourceOnlineLoader As New LoaderTask(Of (ModList As List(Of LocalResourceFile), Cache As JObject, Target As McVersion), Integer)("LocalResourceOnlineLoader", AddressOf LocalResourceOnlineLoad)
    Private Sub LocalResourceOnlineLoad(Loader As LoaderTask(Of (ModList As List(Of LocalResourceFile), Cache As JObject, Target As McVersion), Integer))
        Dim Target As McVersion = Loader.Input.Target
        Dim Context As New PCL.Download.OnlineLoadContext With {
            .GameVersion = Target.VanillaName,
            .Loaders = CType(CInt(Target.ModLoaders), PCL.Download.ModLoaders)
        }

        '投影到 C# 侧，并把已经算好的指纹一并带过去：
        'VB 的指纹有跨运行缓存，不带的话内核每次启动都要把全部 Mod 重读一遍算哈希
        Dim CoreList As New List(Of PCL.Download.LocalResourceFile)(Loader.Input.ModList.Count)
        Dim CoreToLocal As New Dictionary(Of PCL.Download.LocalResourceFile, LocalResourceFile)
        For Each Entry In Loader.Input.ModList
            If Loader.IsCanceled Then Return
            Dim Core As New PCL.Download.LocalResourceFile(Entry.File.FullName)
            Core.PrimeHashes(Entry.ModrinthHash, Entry.CurseForgeHash)
            CoreList.Add(Core)
            CoreToLocal(Core) = Entry
        Next

        '缓存 JSON 转一次格式：Newtonsoft ↔ System.Text.Json；落盘仍由这边负责
        Dim CoreCache As JsonObject = JsonNode.Parse(Loader.Input.Cache.ToString())
        Dim Client As New PCL.Download.LocalResourceOnlineLoader()
        Dim Report = Client.Load(CoreList, CoreCache, Context, Function() Loader.IsCanceled)
        If Report Is Nothing Then Return '已取消

        '把结果投影回 VB 条目：Project / UpdateFile 的 setter 会触发 OnUpdate，UI 才能跟着刷新
        For Each Core In CoreList
            Dim Entry = CoreToLocal(Core)
            If Core.Project IsNot Nothing Then Entry.Project = New ResourceProject(Core.Project)
            If Core.ProjectVersion IsNot Nothing Then Entry.ProjectVersion = New ResourceVersion(Core.ProjectVersion)
            If Core.UpdateFile IsNot Nothing Then Entry.UpdateFile = New ResourceVersion(Core.UpdateFile)
            Entry.ChangelogUrls = Core.ChangelogUrls
            Entry.OnlineDataLoaded = Core.OnlineDataLoaded
        Next

        '保存缓存（有失败时内核不给条目标记 OnlineDataLoaded，下次启动会重试，与旧实现一致）
        If Loader.IsCanceled Then Return
        If Report.Resolved > 0 Then
            Dim Format As JsonSerializerOptions = If(ModeDebug, New JsonSerializerOptions With {.WriteIndented = True}, Nothing)
            FileUtils.Write(PathTemp & "Cache\LocalMod.json", CoreCache.ToJsonString(Format))
        End If

        '刷新边栏
        If Loader.IsCanceled Then Return
        If FrmInstanceMod?.Filter = PageInstanceMod.FilterType.CanUpdate Then
            RunInUi(Sub() FrmInstanceMod?.RefreshUI()) '同步 “可更新” 列表 (#4677)
        Else
            RunInUi(Sub() FrmInstanceMod?.RefreshBars())
        End If
    End Sub

End Module
