Partial Public Module ModDownloadLib

#Region "P4-B 编排装配层（DTO → LoaderBase）"

    ''' <summary>
    ''' 合并安装装配层的宿主上下文：只保存 VB 侧资产与请求，不含任何编排决策。
    ''' 步骤是否出现、顺序、名称、权重、Block、Show、输出槽位与下载类别
    ''' 全部来自 <see cref="PCL.Download.InstallTaskDescriptor"/>。
    ''' </summary>
    ''' <remarks>
    ''' 本类型是 Partial Module 的一部分，与 ModDownloadLib.vb 共享 Private 宿主函数
    ''' （McDownloadClientLoader / McDownloadForgelikeLoader / MergeJson 等）。
    ''' 口径 A 下这些函数仍属于 VB 执行层，装配层只负责调用它们。
    ''' </remarks>
    Friend Class InstallLoaderContext
        Public Request As McInstallRequest
        Public Plan As PCL.Download.InstallPlan
        Public TempMcFolder As String
        Public Layout As PCL.Download.InstallLayout
        Public OptiFineEntry As DlOptiFineListEntry
        Public ForgeVersion As String
        Public NeoForgeVersion As String
        Public LiteLoaderVersion As String
        Public FabricVersion As String
        Public VersionFolder As String
        Public ModsTempFolder As String
        Public OptiFineFolder As String
        Public ForgeFolder As String
        Public NeoForgeFolder As String
        Public LiteLoaderFolder As String
        Public FabricFolder As String
        Public OptiFineAsMod As Boolean
        Public Modable As Boolean
        Public MinecraftFolder As String
        Public ManagedFileSources As Dictionary(Of String, String)
        Public ClientLoader As LoaderCombo(Of String)
        Public TargetFolder As String = Nothing
    End Class

    ''' <summary>
    ''' P4-B 唯一的 DTO → LoaderBase 装配处：遍历 C# 产出的描述符，按 Kind 分派到具名装配方法。
    ''' 装配层只做三件事——读描述符、构造对应 LoaderBase 子类、挂上 VB 侧执行 lambda；
    ''' 不得出现「If 组件已选 Then」形式的业务判定（步骤是否已包含由 InstallPlanner.Build 决定）。
    ''' </summary>
    Friend Function LoaderPlanAssemble(Ctx As InstallLoaderContext) As List(Of LoaderBase)
        Dim LoaderList As New List(Of LoaderBase)
        For Each D In PCL.Download.InstallTaskPlanner.Describe(Ctx.Plan)
            Dim Loader As LoaderBase = Nothing
            Select Case D.Kind.Value
                Case PCL.Download.InstallStepKind.IgnoreMarker
                    Loader = AssembleIgnoreMarker(D, Ctx)
                Case PCL.Download.InstallStepKind.FabricApiDownload
                    Loader = AssembleFabricApi(D, Ctx)
                Case PCL.Download.InstallStepKind.OptiFabricDownload
                    Loader = AssembleOptiFabric(D, Ctx)
                Case PCL.Download.InstallStepKind.Client
                    Loader = AssembleClient(D, Ctx)
                Case PCL.Download.InstallStepKind.OptiFine
                    Loader = AssembleOptiFine(D, Ctx)
                Case PCL.Download.InstallStepKind.Forge
                    Loader = AssembleForge(D, Ctx)
                Case PCL.Download.InstallStepKind.NeoForge
                    Loader = AssembleNeoForge(D, Ctx)
                Case PCL.Download.InstallStepKind.LiteLoader
                    Loader = AssembleLiteLoader(D, Ctx)
                Case PCL.Download.InstallStepKind.Fabric
                    Loader = AssembleFabric(D, Ctx)
                Case PCL.Download.InstallStepKind.InstallGame
                    Loader = AssembleInstallGame(D, Ctx)
                Case PCL.Download.InstallStepKind.PostLibraries
                    Loader = AssemblePostLibraries(D, Ctx)
                Case PCL.Download.InstallStepKind.RecordManifest
                    Loader = AssembleRecordManifest(D, Ctx)
                Case PCL.Download.InstallStepKind.RemoveIgnoreMarker
                    Loader = AssembleRemoveIgnoreMarker(D, Ctx)
                Case Else
                    Throw New InvalidOperationException("未知的装配步骤种类：" & D.Kind.Value.ToString())
            End Select
            LoaderList.Add(Loader)
        Next
        Return LoaderList
    End Function

    ''' <summary>描述符是否声明了某个输出槽位（用 DTO 决策代替装配层业务判定）。</summary>
    Private Function DescriptorHasOutput(D As PCL.Download.InstallTaskDescriptor, Name As String) As Boolean
        For Each Item In D.Outputs
            If String.Equals(Item, Name, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    Private Function AssembleIgnoreMarker(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Return New LoaderTask(Of Integer, Integer)(D.Name,
            Sub() FileUtils.Write(Ctx.VersionFolder & ".pclignore", "用于临时地在 PCL Next 的版本列表中屏蔽此版本。")) With {
                .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    Private Function AssembleFabricApi(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim DownloadFile = Ctx.Request.FabricApi.ToNetFile(Ctx.ModsTempFolder,
            ResourceVersion.DownloadReason.Dependency, Ctx.Request.Components.MinecraftVersion, ModLoaders.Fabric)
        Ctx.ManagedFileSources.Add("FabricApi", DownloadFile.LocalPath)
        Return New LoaderDownload(D.Name, New List(Of NetFile) From {DownloadFile}) With {
            .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    Private Function AssembleOptiFabric(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim DownloadFile = Ctx.Request.OptiFabric.ToNetFile(Ctx.ModsTempFolder,
            ResourceVersion.DownloadReason.Dependency, Ctx.Request.Components.MinecraftVersion, ModLoaders.Fabric)
        Ctx.ManagedFileSources.Add("OptiFabric", DownloadFile.LocalPath)
        Return New LoaderDownload(D.Name, New List(Of NetFile) From {DownloadFile}) With {
            .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    ''' <summary>Client 复合体：子加载器列表仍由 VB 宿主函数提供（SubSteps 已由 C# 描述，逐类下沉属 P4-B 后续批次）。</summary>
    Private Function AssembleClient(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim Loader = New LoaderCombo(Of String)(D.Name,
            McDownloadClientLoader(Ctx.Request.Components.MinecraftVersion, Ctx.Request.MinecraftJson, Ctx.VersionFolder)) With {
                .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
        Ctx.ClientLoader = Loader
        Return Loader
    End Function

    ''' <summary>OptiFine 双形态由 DTO 的 SubStepsSource 决定，装配层不自行判断「作为 Mod」。</summary>
    Private Function AssembleOptiFine(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim InstallerPath = Ctx.Layout.BuildOptiFineInstallerPath(Ctx.OptiFineEntry.FileName)
        If DescriptorHasOutput(D, "ManagedFileSources.OptiFine") Then
            Ctx.ManagedFileSources.Add("OptiFine", InstallerPath)
        End If
        Dim Loaders As List(Of LoaderBase)
        Select Case D.SubStepsSource
            Case "McDownloadOptiFineSaveLoader"
                Loaders = McDownloadOptiFineSaveLoader(Ctx.OptiFineEntry, InstallerPath)
            Case "McDownloadOptiFineLoader"
                Loaders = McDownloadOptiFineLoader(Ctx.OptiFineEntry, Ctx.TempMcFolder, Ctx.ClientLoader, Ctx.VersionFolder)
            Case Else
                Throw New InvalidOperationException("OptiFine 装配规格缺少已知的 SubStepsSource")
        End Select
        Return New LoaderCombo(Of String)(D.Name, Loaders) With {
            .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    Private Function AssembleForge(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim Loaders = McDownloadForgelikeLoader(False, Ctx.ForgeVersion, "forge-" & Ctx.ForgeVersion,
            Ctx.Request.Components.MinecraftVersion, Ctx.Request.ForgeEntry, Ctx.TempMcFolder, Ctx.ClientLoader, Ctx.VersionFolder)
        Return New LoaderCombo(Of String)(D.Name, Loaders) With {
            .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    Private Function AssembleNeoForge(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim Loaders = McDownloadForgelikeLoader(True, Ctx.NeoForgeVersion, "neoforge-" & Ctx.NeoForgeVersion,
            Ctx.Request.Components.MinecraftVersion, Ctx.Request.NeoForgeEntry, Ctx.TempMcFolder, Ctx.ClientLoader, Ctx.VersionFolder)
        Return New LoaderCombo(Of String)(D.Name, Loaders) With {
            .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    Private Function AssembleLiteLoader(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim Loaders = McDownloadLiteLoaderLoader(Ctx.Request.LiteLoaderEntry, Ctx.TempMcFolder)
        Return New LoaderCombo(Of String)(D.Name, Loaders) With {
            .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    Private Function AssembleFabric(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim Loaders = McDownloadFabricLoader(Ctx.FabricVersion, Ctx.Request.Components.MinecraftVersion, Ctx.TempMcFolder)
        Return New LoaderCombo(Of String)(D.Name, Loaders) With {
            .ProgressWeight = D.ProgressWeight, .Show = D.Show, .Block = D.Block}
    End Function

    Private Function AssembleInstallGame(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Return New LoaderTask(Of String, String)(D.Name,
        Sub(Task As LoaderTask(Of String, String))
            '合并 JSON：将多个版本 JSON 进行合并，如果已存在则直接覆盖
            MergeJson(Ctx.VersionFolder, Ctx.OptiFineFolder, Ctx.OptiFineAsMod,
                Ctx.ForgeFolder, Ctx.NeoForgeFolder, Ctx.FabricFolder, Ctx.LiteLoaderFolder)
            Task.Progress = 0.2
            '迁移文件
            DirectoryUtils.Copy(Ctx.TempMcFolder & "libraries", Ctx.MinecraftFolder & "libraries")
            Task.Progress = 0.8
            '创建 Mod 和资源包文件夹
            Ctx.TargetFolder = If(Ctx.Request.IsIntermediateOutput, Ctx.VersionFolder, New McInstance(Ctx.VersionFolder).PathIndie) '版本隔离信息在此时被决定
            Dim ModsFolder = Ctx.TargetFolder & "mods\"
            If DirectoryUtils.Exists(Ctx.ModsTempFolder) Then
                DirectoryUtils.Copy(Ctx.ModsTempFolder, ModsFolder)
            ElseIf Ctx.Modable Then
                DirectoryUtils.Create(ModsFolder)
                Logger.Info($"自动创建 Mod 文件夹：{ModsFolder}")
            End If
            Dim ResourcepacksFolder = Ctx.TargetFolder & "resourcepacks\"
            DirectoryUtils.Create(ResourcepacksFolder)
            Logger.Info($"自动创建资源包文件夹：{ResourcepacksFolder}")
        End Sub) With {.ProgressWeight = D.ProgressWeight, .Block = D.Block, .Show = D.Show}
    End Function

    ''' <summary>补全文件复合体：两个副加载器的名称 / 权重 / Show 取自 DTO 的 SubSteps。</summary>
    Private Function AssemblePostLibraries(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Dim LoadersLib As New List(Of LoaderBase)
        Dim Analysis = D.SubSteps(0)
        Dim Download = D.SubSteps(1)
        LoadersLib.Add(New LoaderTask(Of String, List(Of NetFile))(Analysis.Name,
            Sub(Task) Task.Output = McLibNetFilesFromInstance(New McInstance(Ctx.VersionFolder))) With {
                .ProgressWeight = Analysis.ProgressWeight, .Show = Analysis.Show})
        LoadersLib.Add(New LoaderDownload(Download.Name, New List(Of NetFile)) With {
                .ProgressWeight = Download.ProgressWeight, .Show = Download.Show})
        Return New LoaderCombo(Of String)(D.Name, LoadersLib) With {
            .ProgressWeight = D.ProgressWeight, .Block = D.Block, .Show = D.Show}
    End Function

    Private Function AssembleRecordManifest(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Return New LoaderTask(Of Integer, Integer)(D.Name,
        Sub()
            Dim ManagedFiles As New List(Of McInstallManifest.ManagedFile)
            '记录由 PCL 管理的 Mod 信息
            For Each Entry In Ctx.ManagedFileSources
                Dim RelativePath = Entry.Value.Substring(Ctx.ModsTempFolder.Length)
                Dim FullPath = PathUtils.ForCompare(Path.Combine(Ctx.TargetFolder, "mods", RelativePath))
                ManagedFiles.Add(New McInstallManifest.ManagedFile(PCL.Download.InstallManifestRules.ManagedModPath(RelativePath),
                    CryptographyUtils.ComputeFileHash(FullPath, CryptographyUtils.HashMethod.Sha1), FileUtils.GetInfo(FullPath).LastWriteTimeUtc, Entry.Key))
            Next
            '处理加载器版本信息（剥掉 MC 前缀，规则见 PCL.Download.InstallManifestRules）
            Dim TempLoaderVersions As New Dictionary(Of ModLoaders, String)(Ctx.Request.Components.ModLoaderVersions)
            If Ctx.ForgeVersion IsNot Nothing Then
                TempLoaderVersions(ModLoaders.Forge) = PCL.Download.InstallManifestRules.NormalizeLoaderVersion(ModLoaders.Forge, Ctx.ForgeVersion)
            End If
            If Ctx.NeoForgeVersion IsNot Nothing Then
                TempLoaderVersions(ModLoaders.NeoForge) = PCL.Download.InstallManifestRules.NormalizeLoaderVersion(ModLoaders.NeoForge, Ctx.NeoForgeVersion)
            End If
            '存储 Manifest
            Dim Target = New McComponents(Ctx.Request.Components.MinecraftVersion, TempLoaderVersions,
                Ctx.Request.Components.OptiFineEntry, Ctx.Request.Components.FabricApiVersion, Ctx.Request.Components.OptiFabricVersion)
            Dim InstanceConfig = New McInstance(Ctx.VersionFolder).Config
            Configs.McInstallManifest.Set(New McInstallManifest(Versions.McInstallManifestVersion, Target, Date.UtcNow, ManagedFiles), InstanceConfig)
            InstanceConfig.Save()
        End Sub) With {.ProgressWeight = D.ProgressWeight, .Block = D.Block, .Show = D.Show}
    End Function

    Private Function AssembleRemoveIgnoreMarker(D As PCL.Download.InstallTaskDescriptor, Ctx As InstallLoaderContext) As LoaderBase
        Return New LoaderTask(Of Integer, Integer)(D.Name,
            Sub() FileUtils.Delete(Ctx.VersionFolder & ".pclignore")) With {
                .ProgressWeight = D.ProgressWeight, .Block = D.Block, .Show = D.Show}
    End Function

#End Region

End Module
