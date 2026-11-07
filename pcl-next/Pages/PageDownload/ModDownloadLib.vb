Public Module ModDownloadLib

#Region "Minecraft 下载"

    ''' <summary>
    ''' 获取下载某个 Minecraft 版本的加载器列表。
    ''' </summary>
    ''' <param name="VersionFolder">版本文件输出目录。</param>
    Private Function McDownloadClientLoader(Id As String, JsonUrl As String, VersionFolder As String) As List(Of LoaderBase)
        Dim InstanceName = PathUtils.GetLastPart(VersionFolder)
        Dim Loaders As New List(Of LoaderBase)
        Dim NewInstance As McInstance = Nothing

        '下载版本 Json 文件
        If JsonUrl Is Nothing Then
            Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("获取原版 json 文件下载地址",
            Sub(Task As LoaderTask(Of String, List(Of NetFile)))
                Dim JsonAddress As String = DlClientListGet(Id)
                Task.Output = New List(Of NetFile) From {New NetFile(DlSourceLauncherOrMetaGet(JsonAddress), VersionFolder & InstanceName & ".json")}
            End Sub) With {.ProgressWeight = 2, .Show = False})
        End If
        Loaders.Add(New LoaderDownload(McDownloadClientJsonName, New List(Of NetFile) From {
            New NetFile(DlSourceLauncherOrMetaGet(If(JsonUrl, "")), VersionFolder & InstanceName & ".json", New FileChecker With {.IsJson = True})
        }) With {.ProgressWeight = 3})
        '读取 Json 文件并创建实例
        Loaders.Add(New LoaderTask(Of String, String)("读取 json 文件",
            Sub(Task As LoaderTask(Of String, String))
                NewInstance = New McInstance(VersionFolder)
                NewInstance.Load()
            End Sub) With {.ProgressWeight = 0, .Show = False})

        '下载支持库文件
        Dim LoadersLib As New List(Of LoaderBase)
        LoadersLib.Add(New LoaderTask(Of String, List(Of NetFile))("分析原版支持库文件（副加载器）",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            Logger.Info($"开始分析原版支持库文件：{VersionFolder}")
            Task.Output = McLibNetFilesFromInstance(NewInstance)
        End Sub) With {.ProgressWeight = 1, .Show = False})
        LoadersLib.Add(New LoaderDownload("下载原版支持库文件（副加载器）", New List(Of NetFile)) With {.ProgressWeight = 13, .Show = False})
        Loaders.Add(New LoaderCombo(Of String)(McDownloadClientLibName, LoadersLib) With {.Block = False, .ProgressWeight = 14})

        '下载资源文件
        Dim LoadersAssets As New List(Of LoaderBase)
        LoadersAssets.Add(New LoaderTask(Of String, List(Of NetFile))("分析资源文件索引地址（副加载器）",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            Try
                Dim AssetIndex = DlClientAssetIndexGet(NewInstance)
                Task.Output = If(AssetIndex Is Nothing, New List(Of NetFile), New List(Of NetFile) From {AssetIndex})
            Catch ex As Exception
                Throw New Exception("分析资源文件索引地址失败", ex)
            End Try
            '顺手添加 Json 项目
            Try
                Dim InstanceJson As JObject = FileUtils.ReadAsJson(VersionFolder & InstanceName & ".json")
                InstanceJson("clientVersion") = Id
                FileUtils.Write($"{VersionFolder}{InstanceName}.json", InstanceJson.ToString)
            Catch ex As Exception
                Throw New Exception("添加客户端版本失败", ex)
            End Try
        End Sub) With {.ProgressWeight = 1, .Show = False})
        LoadersAssets.Add(New LoaderDownload("下载资源文件索引（副加载器）", New List(Of NetFile)) With {.ProgressWeight = 3, .Show = False})
        LoadersAssets.Add(New LoaderTask(Of String, List(Of NetFile))("分析所需资源文件（副加载器）",
            Sub(Task) Task.Output = McAssetsFixList(NewInstance, True, Task)) With {.ProgressWeight = 0.01, .Show = False})
        LoadersAssets.Add(New LoaderDownload("下载资源文件（副加载器）", New List(Of NetFile)) With {.ProgressWeight = 14, .Show = False})
        Loaders.Add(New LoaderCombo(Of String)("下载原版资源文件", LoadersAssets) With {.Block = False, .ProgressWeight = 18})

        Return Loaders

    End Function
    Private Const McDownloadClientLibName As String = "下载原版支持库文件"
    Private Const McDownloadClientJsonName As String = "下载原版 json 文件"

#End Region

#Region "Minecraft 下载菜单"

    Public Function McDownloadListItem(Entry As JObject, OnClick As MyListItem.ClickEventHandler, IsSaveOnly As Boolean) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            '确定图标
            Dim Logo As String
            Select Case Entry("type")
                Case "release"
                    Logo = PathImage & "Blocks/Grass.png"
                Case "snapshot"
                    Logo = PathImage & "Blocks/CommandBlock.png"
                Case "special"
                    Logo = PathImage & "Blocks/GoldBlock.png"
                Case Else
                    Logo = PathImage & "Blocks/CobbleStone.png"
            End Select
            '建立控件
            Dim NewItem As New MyListItem With {.Logo = Logo, .SnapsToDevicePixels = True, .Title = Entry("id").ToString, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Entry}
            If Entry("lore") Is Nothing Then
                NewItem.Info = Entry("releaseTime").Value(Of Date).ToString("yyyy'/'MM'/'dd HH':'mm")
            Else
                NewItem.Info = Entry("lore").ToString
            End If
            If Entry("url").ToString.Contains("pcl") Then NewItem.Info = "[PCL Next 特供下载] " & NewItem.Info
            AddHandler NewItem.Click, OnClick
            '建立菜单
            If IsSaveOnly Then
                NewItem.ContentHandler = AddressOf McDownloadSaveMenuBuild
            Else
                NewItem.ContentHandler = AddressOf McDownloadMenuBuild
            End If
            '结束
            Return NewItem
        End Function) With {.Height = 42}
    End Function
    Private Sub McDownloadSaveMenuBuild(sender As Object, e As EventArgs)
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf McDownloadMenuLog
        Dim BtnServer As New MyIconButton With {.LogoScale = 1, .Logo = Logo.IconButtonServer, .ToolTip = "下载服务端"}
        AddHandler BtnServer.Click, AddressOf McDownloadMenuSaveServer
        sender.Buttons = {BtnServer, BtnInfo}
    End Sub
    Private Sub McDownloadMenuBuild(sender As Object, e As EventArgs)
        Dim BtnSave As New MyIconButton With {.Logo = Logo.IconButtonSave, .ToolTip = "另存为"}
        AddHandler BtnSave.Click, AddressOf McDownloadMenuSave
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf McDownloadMenuLog
        Dim BtnServer As New MyIconButton With {.LogoScale = 1, .Logo = Logo.IconButtonServer, .ToolTip = "下载服务端"}
        AddHandler BtnServer.Click, AddressOf McDownloadMenuSaveServer
        sender.Buttons = {BtnSave, BtnInfo, BtnServer}
    End Sub
    Private Sub McDownloadMenuLog(sender As Object, e As RoutedEventArgs)
        Dim Version As JToken
        If sender.Tag IsNot Nothing Then
            Version = sender.Tag
        ElseIf sender.Parent.Tag IsNot Nothing Then
            Version = sender.Parent.Tag
        Else
            Version = sender.Parent.Parent.Tag
        End If
        McUpdateLogShow(Version)
    End Sub
    Private Sub McDownloadMenuSaveServer(sender As Object, e As RoutedEventArgs)
        Dim Version As MyListItem
        If TypeOf sender Is MyListItem Then
            Version = sender
        ElseIf TypeOf sender.Parent Is MyListItem Then
            Version = sender.Parent
        Else
            Version = sender.Parent.Parent
        End If
        Try
            Dim Id = Version.Title
            Dim JsonUrl = Version.Tag("url").ToString
            Dim VersionFolder As String = Dialogs.SelectFolder("选择目标文件夹", False).FirstOrDefault
            If VersionFolder Is Nothing Then Return
            VersionFolder = VersionFolder & Id & "\"

            '重复任务检查
            For Each OngoingLoader In LoaderTaskbar.ToList()
                If OngoingLoader.Name <> $"Minecraft {Id} 服务端下载" Then Continue For
                Hint("该服务端正在下载中！", HintType.Red)
                Return
            Next

            Dim Loaders As New List(Of LoaderBase)
            '下载版本 JSON 文件
            Loaders.Add(New LoaderDownload("下载版本 JSON 文件", New List(Of NetFile) From {
                New NetFile(DlSourceLauncherOrMetaGet(JsonUrl), VersionFolder & Id & ".json", New FileChecker With {.IsJson = True})
            }) With {.ProgressWeight = 2})
            '构建服务端
            Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("构建服务端",
            Sub(Task As LoaderTask(Of String, List(Of NetFile)))
                '分析服务端 JAR 文件下载地址
                Dim Instance As New McInstance(VersionFolder)
                If Instance.JsonObject("downloads") Is Nothing OrElse Instance.JsonObject("downloads")("server") Is Nothing OrElse Instance.JsonObject("downloads")("server")("url") Is Nothing Then
                    FileUtils.Delete(VersionFolder & Id & ".json")
                    If Not DirectoryUtils.GetInfo(VersionFolder).GetFileSystemInfos.Any() Then DirectoryUtils.Delete(VersionFolder)
                    Task.Output = New List(Of NetFile)
                    Hint($"Mojang 没有给 Minecraft {Id} 提供官方服务端下载，没法下，撤退！", HintType.Red)
                    Thread.Sleep(2000) '等玩家把上一个提示看完
                    Task.Cancel()
                    Return
                End If
                Dim JarUrl As String = Instance.JsonObject("downloads")("server")("url")
                Dim Checker As New FileChecker With {.MinSize = 1024, .ActualSize = If(Instance.JsonObject("downloads")("server")("size"), -1), .Hash = Instance.JsonObject("downloads")("server")("sha1")}
                Task.Output = New List(Of NetFile) From {New NetFile(DlSourceLauncherOrMetaGet(JarUrl), VersionFolder & Id & "-server.jar", Checker)}
                '添加启动脚本
                Dim Bat As String =
$"@echo off
title {Id} 原版服务端
echo 如果服务端立即停止，请右键编辑该脚本，将下一行开头的 java 替换为适合该 Minecraft 版本的完整 java.exe 的路径。
echo 你可以在 PCL 的 [设置 → 启动选项] 中查看已安装的 java，所需的 java.exe 一般在其中的 bin 文件夹下。
echo ------------------------------
echo 如果提示 ""You need to agree to the EULA in order to run the server""，请打开 eula.txt，按说明阅读并同意 Minecraft EULA 后，将该文件最后一行中的 eula=false 改为 eula=true。
echo ------------------------------
""java"" -server -XX:+UseG1GC -Xmx6144M -Xms1024M -jar {Id}-server.jar nogui
echo ----------------------
echo 服务端已停止。
pause"
                FileUtils.Write(VersionFolder & "Launch Server.bat", Bat,
                        encoding:=If(Encoding.Default.Equals(Encoding.UTF8), Encoding.UTF8, Encoding.GetEncoding("GB18030")))
                '删除版本 JSON
                FileUtils.Delete(VersionFolder & Id & ".json")
            End Sub) With {.ProgressWeight = 0.5, .Show = False})
            '下载服务端文件
            Loaders.Add(New LoaderDownload("下载服务端文件", New List(Of NetFile)) With {.ProgressWeight = 5})

            '启动
            Dim Loader As New LoaderCombo(Of String)("Minecraft " & Id & " 服务端下载", Loaders) With {.OnStateChanged = AddressOf LoaderStateChangedHintOnly}
            Loader.Start(Id)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, "开始 Minecraft 服务端下载失败")
        End Try
    End Sub
    Public Sub McDownloadMenuSave(sender As Object, e As RoutedEventArgs)
        Dim Version As MyListItem
        If TypeOf sender Is MyListItem Then
            Version = sender
        ElseIf TypeOf sender.Parent Is MyListItem Then
            Version = sender.Parent
        Else
            Version = sender.Parent.Parent
        End If
        Try
            Dim Id = Version.Title
            Dim JsonUrl = Version.Tag("url").ToString
            Dim VersionFolder As String = Dialogs.SelectFolder("选择目标文件夹", False).FirstOrDefault
            If VersionFolder Is Nothing Then Return
            VersionFolder = VersionFolder & Id & "\"

            '重复任务检查
            For Each OngoingLoader In LoaderTaskbar.ToList()
                If OngoingLoader.Name <> $"Minecraft {Id} 下载" Then Continue For
                Hint("该版本正在下载中！", HintType.Red)
                Return
            Next

            Dim Loaders As New List(Of LoaderBase)
            '下载版本 JSON 文件
            Loaders.Add(New LoaderDownload("下载版本 JSON 文件", New List(Of NetFile) From {
                New NetFile(DlSourceLauncherOrMetaGet(JsonUrl), VersionFolder & Id & ".json", New FileChecker With {.IsJson = True})
            }) With {.ProgressWeight = 2})
            '获取支持库文件地址
            Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("分析版本 JAR 文件下载地址",
                Sub(Task) Task.Output = New List(Of NetFile) From {DlClientJarGet(New McInstance(VersionFolder), False)}
            ) With {.ProgressWeight = 0.5, .Show = False})
            '下载支持库文件
            Loaders.Add(New LoaderDownload("下载版本 JAR 文件", New List(Of NetFile)) With {.ProgressWeight = 5})

            '启动
            Dim Loader As New LoaderCombo(Of String)("Minecraft " & Id & " 下载", Loaders) With {.OnStateChanged = AddressOf LoaderStateChangedHintOnly}
            Loader.Start(Id)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, "开始 Minecraft 下载失败")
        End Try
    End Sub
    ''' <summary>
    ''' 显示某 Minecraft 版本的更新日志。
    ''' </summary>
    ''' <param name="InstanceJson">在 version_manifest.json 中的对应项。</param>
    Public Sub McUpdateLogShow(InstanceJson As JToken)
        Dim WikiName As String
        Dim Id As String = InstanceJson("id").ToString.Lower
        If Id = "3d shareware v1.34" Then
            WikiName = "3D_Shareware_v1.34"
        ElseIf Id = "2.0" Then
            WikiName = "Java版2.0"
        ElseIf Id = "1.rv-pre1" Then
            WikiName = "Java版1.RV-Pre1"
        ElseIf Id = "combat test 1" OrElse Id.Contains("combat-1") OrElse Id.Contains("combat-212796") Then
            WikiName = "Java版1.14.3_-_Combat_Test"
        ElseIf Id = "combat test 2" OrElse Id.Contains("combat-2") OrElse Id.Contains("combat-0") Then
            WikiName = "Java版Combat_Test_2"
        ElseIf Id = "combat test 3" OrElse Id = "1.14_combat-3" Then
            WikiName = "Java版Combat_Test_3"
        ElseIf Id = "combat test 4" OrElse Id = "1.15_combat-1" Then
            WikiName = "Java版Combat_Test_4"
        ElseIf Id = "combat test 5" OrElse Id = "1.15_combat-6" Then
            WikiName = "Java版Combat_Test_5"
        ElseIf Id = "combat test 6" OrElse Id = "1.16_combat-0" Then
            WikiName = "Java版Combat_Test_6"
        ElseIf Id = "combat test 7c" OrElse Id = "1.16_combat-3" Then
            WikiName = "Java版Combat_Test_7c"
        ElseIf Id = "combat test 8b" OrElse Id = "1.16_combat-5" Then
            WikiName = "Java版Combat_Test_8b"
        ElseIf Id = "combat test 8c" OrElse Id = "1.16_combat-6" Then
            WikiName = "Java版Combat_Test_8c"
        ElseIf Id = "1.0.0-rc2-2" Then
            WikiName = "Java版RC2"
        ElseIf Id.StartsWithF("1.19_deep_dark_experimental_snapshot-") OrElse Id.StartsWithF("1_19_deep_dark_experimental_snapshot-") Then
            WikiName = Id.Replace("1_19", "1.19").Replace("1.19_deep_dark_experimental_snapshot-", "Java版Deep_Dark_Experimental_Snapshot_")
        ElseIf Id = "b1.9-pre6" Then
            WikiName = "Java版Beta_1.9_Prerelease_6"
        ElseIf Id.Contains("b1.9") Then
            WikiName = "Java版Beta_1.9_Prerelease"
        ElseIf InstanceJson("type") = "release" OrElse InstanceJson("type") = "snapshot" OrElse InstanceJson("type") = "special" Then
            WikiName = If(Id.Contains("w"), "", "Java版") & Id.Replace(" Pre-Release ", "-pre")
        ElseIf Id.StartsWithF("b") Then
            WikiName = "Java版" & Id.TrimEnd("a", "b", "c", "d", "e").Replace("b", "Beta_")
        ElseIf Id.StartsWithF("a") Then
            WikiName = "Java版" & Id.TrimEnd("a", "b", "c", "d", "e").Replace("a", "Alpha_v")
        ElseIf Id = "inf-20100618" Then
            WikiName = "Java版Infdev_20100618"
        ElseIf Id = "c0.30_01c" OrElse Id = "c0.30_survival" OrElse Id.Contains("生存测试") Then
            WikiName = "Java版Classic_0.30（生存模式）"
        ElseIf Id.StartsWithF("c0.31") Then
            WikiName = "Java版Indev_0.31_20100130"
        ElseIf Id.StartsWithF("c") Then
            WikiName = "Java版" & Id.Replace("c", "Classic_")
        ElseIf Id.StartsWithF("rd-") Then
            WikiName = "Java版Pre-classic_" & Id
        Else
            Logger.Error($"未知的版本格式：{Id}。")
            Return
        End If
        OpenWebsite("https://zh.minecraft.wiki/w/" & WikiName.Replace("_experimental-snapshot-", "-exp"))
    End Sub

#End Region

#Region "OptiFine 下载"

    Private Sub McDownloadOptiFineSave(DownloadInfo As DlOptiFineListEntry)
        Try
            Dim Target As String = Dialogs.SaveFile("选择保存位置", DownloadInfo.FileName, filter:={("jar", "OptiFine Jar")})
            If Target Is Nothing Then Return

            '重复任务检查
            For Each OngoingLoader In LoaderTaskbar.ToList()
                If OngoingLoader.Name <> $"OptiFine {DownloadInfo.DisplayName} 下载" Then Continue For
                Hint("该版本正在下载中！", HintType.Red)
                Return
            Next

            Dim Loader As New LoaderCombo(Of DlOptiFineListEntry)("OptiFine " & DownloadInfo.DisplayName & " 下载", McDownloadOptiFineSaveLoader(DownloadInfo, Target)) With {.OnStateChanged = AddressOf LoaderStateChangedHintOnly}
            Loader.Start(DownloadInfo)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()

        Catch ex As Exception
            Logger.Error(ex, "开始 OptiFine 下载失败")
        End Try
    End Sub
    Private Sub McDownloadOptiFineInstall(BaseMcFolderHome As String, Target As String, Task As LoaderTask(Of List(Of NetFile), Boolean), UseJavaWrapper As Boolean)
        '探测安装器要求的 Java 版本：读 jar 里 optifine/Installer.class 的 class 版本号（已迁至 C#）
        Dim JavaProbe = PCL.Download.OptiFineInstaller.ProbeRequiredJava(Target)
        If JavaProbe.Failure Is Nothing Then
            Logger.Info($"OptiFine 安装器 class 版本：{JavaProbe.ClassVersion}，需要 Java {JavaProbe.JavaMajor}")
        Else
            '失败原因要落日志：原先吞掉异常只留一句「读取失败」，装错 Java 时无从判断卡在哪一步
            Logger.Warn($"读取 OptiFine 安装器 Java 版本失败：{JavaProbe.Failure}，按 Java {JavaProbe.JavaMajor} 处理")
        End If
        Dim JavaRange = ValueRange(Of Version).AtLeast(New Version(JavaProbe.JavaMajor, 0))
        '选择 Java
        Dim Java As Java = SelectOrDownloadJava(JavaRange, True, Task.CreateCancellationToken, Task.CreateSyncProgressProvider(0, 0.2))
        If Task.IsCanceled Then Return
        If Java Is Nothing Then Throw New OperationCanceledException
        '添加 Java Wrapper 作为主 Jar（只有真要用时才解压，避免多余的文件写入）
        Dim UseWrapper As Boolean = UseJavaWrapper AndAlso Not Settings.Get(Of Boolean)("LaunchAdvanceDisableJLW")
        Dim WrapperPath As String = Nothing
        If UseWrapper Then WrapperPath = LaunchUtils.ExtractPatch("Launch\JavaWrapper.jar", PathPure.Value)
        Dim Arguments As String = PCL.Download.OptiFineInstaller.BuildArguments(
            Java.Version.Major, Target, PathUtils.RemoveSlashSuffix(BaseMcFolderHome),
            WrapperPath, PathUtils.RemoveSlashSuffix(PathPure.Value), UseWrapper)
        '开始启动
        SyncLock InstallSyncLock
            Dim Info = New ProcessStartInfo With {
                .FileName = Java.JavaExePath,
                .Arguments = Arguments,
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardError = True,
                .RedirectStandardOutput = True,
                .WorkingDirectory = PathUtils.ToShortPath(BaseMcFolderHome)
            }
            If Info.EnvironmentVariables.ContainsKey("appdata") Then
                Info.EnvironmentVariables("appdata") = BaseMcFolderHome
            Else
                Info.EnvironmentVariables.Add("appdata", BaseMcFolderHome)
            End If
            Logger.Info($"开始安装 OptiFine：{Target}")
            Dim TotalLength As Integer = 0
            Dim process As New Process With {.StartInfo = Info}
            Dim LastResult As String = ""
            Using outputWaitHandle As New AutoResetEvent(False)
                Using errorWaitHandle As New AutoResetEvent(False)
                    AddHandler process.OutputDataReceived,
                    Function(sender, e)
                        Try
                            If e.Data Is Nothing Then
                                outputWaitHandle.[Set]()
                            Else
                                LastResult = e.Data
                                Logger.Trace(LastResult)
                                TotalLength += 1
                                Task.Progress += PCL.Download.OptiFineInstaller.ProgressPerLine
                            End If
                        Catch ex As ObjectDisposedException
                        Catch ex As Exception
                            Logger.Warn(ex, "读取 OptiFine 安装器信息失败")
                        End Try
                        Try
                            If Task.State = LoadState.Canceled AndAlso Not process.HasExited Then
                                Logger.Info("由于任务取消，已中止 OptiFine 安装")
                                process.Kill()
                            End If
                        Catch
                        End Try
                        Return Nothing
                    End Function
                    AddHandler process.ErrorDataReceived,
                    Function(sender, e)
                        Try
                            If e.Data Is Nothing Then
                                errorWaitHandle.[Set]()
                            Else
                                LastResult = e.Data
                                Logger.Trace(LastResult)
                                TotalLength += 1
                                Task.Progress += PCL.Download.OptiFineInstaller.ProgressPerLine
                            End If
                        Catch ex As ObjectDisposedException
                        Catch ex As Exception
                            Logger.Warn(ex, "读取 OptiFine 安装器错误信息失败")
                        End Try
                        Try
                            If Task.State = LoadState.Canceled AndAlso Not process.HasExited Then
                                Logger.Info("由于任务取消，已中止 OptiFine 安装")
                                process.Kill()
                            End If
                        Catch
                        End Try
                        Return Nothing
                    End Function
                    process.Start()
                    process.BeginOutputReadLine()
                    process.BeginErrorReadLine()
                    '等待
                    Do Until process.HasExited
                        Thread.Sleep(10)
                    Loop
                    '输出
                    outputWaitHandle.WaitOne(10000)
                    errorWaitHandle.WaitOne(10000)
                    process.Dispose()
                    If PCL.Download.OptiFineInstaller.IsFailed(TotalLength, LastResult) Then Throw New Exception("安装器运行出错，末行为 " & LastResult)
                End Using
            End Using
        End SyncLock
    End Sub

    ''' <summary>
    ''' 构造 OptiFine 的候选下载源：镜像固定一个，官方源需要先解析下载页，取不到时只留镜像。
    ''' </summary>
    ''' <remarks>
    ''' 「安装」与「另存为」原先各写了一份逐字相同的推导，改一处就会让两者打到不同地址。
    ''' 地址形状与页面解析已迁至 <see cref="PCL.Download.OptiFineProfile"/>，这里只剩联网与进度。
    ''' </remarks>
    Private Function McDownloadOptiFineSources(DownloadInfo As DlOptiFineListEntry, Task As LoaderBase) As List(Of String)
        Dim Sources As New List(Of String) From {
            PCL.Download.OptiFineProfile.BuildMirrorUrl(DownloadInfo.Inherit, DownloadInfo.IsPreview, DownloadInfo.DisplayName)}
        Try
            Dim PageData As String = NetRequestByClientRetry(
                PCL.Download.OptiFineProfile.BuildOfficialPageUrl(DownloadInfo.FileName),
                Encoding:=New UTF8Encoding(False), Accept:="text/html", SimulateBrowserHeaders:=True)
            Task.Progress = 0.8
            Dim OfficialUrl As String = PCL.Download.OptiFineProfile.FindOfficialDownloadUrl(PageData)
            If OfficialUrl Is Nothing Then
                '页面里没有下载链接时不再硬拼一个地址：那样会下回几百字节的 HTML，只在体积检查处失败
                Logger.Warn($"OptiFine {DownloadInfo.DisplayName} 官方下载页中未找到下载链接，仅使用镜像源")
            Else
                Sources.Add(OfficialUrl)
                Logger.Info($"OptiFine {DownloadInfo.DisplayName} 官方下载地址：{OfficialUrl}")
            End If
        Catch ex As Exception
            Logger.Warn(ex, $"获取 OptiFine {DownloadInfo.DisplayName} 官方下载地址失败")
        End Try
        Return Sources
    End Function

    ''' <summary>
    ''' 获取下载某个 OptiFine 版本的加载器列表。
    ''' 这不会补全所需的支持库文件，需要后续手动补全支持库。
    ''' </summary>
    Private Function McDownloadOptiFineLoader(DownloadInfo As DlOptiFineListEntry, McFolder As String, ClientDownloadLoader As LoaderCombo(Of String), ClientFolder As String) As List(Of LoaderBase)

        '参数初始化
        Dim Id As String = DownloadInfo.InstanceName
        Dim VersionFolder As String = McFolder & "versions\" & Id & "\"
        Dim IsNewerVersion As Boolean = DownloadInfo.Inherit.Contains("w") OrElse McVersion.VersionToDrop(DownloadInfo.Inherit) >= 140
        '安装器落盘位置：新版走任务临时目录（装完即删），旧版留在 libraries 下（启动时当库加载）
        Dim Target As String = PCL.Download.OptiFineProfile.BuildInstallerTarget(
            IsNewerVersion, If(IsNewerVersion, RequestTaskTempFolder(True), Nothing), McFolder, DownloadInfo.FileName)
        Dim Loaders As New List(Of LoaderBase)

        '获取下载地址
        Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("获取 OptiFine 主文件下载地址",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            '候选源推导已迁至 C#（镜像地址形状 + 官方页解析），「安装」与「另存为」共用这一份
            Dim Sources = McDownloadOptiFineSources(DownloadInfo, Task)
            '构造文件请求
            Task.Output = New List(Of NetFile) From {New NetFile(Sources, Target, New FileChecker With {.MinSize = 300 * 1024})}
        End Sub) With {.ProgressWeight = 8})
        Loaders.Add(New LoaderDownload("下载 OptiFine 主文件", New List(Of NetFile)) With {.ProgressWeight = 8})
        Loaders.Add(New LoaderTask(Of List(Of NetFile), Boolean)("等待原版下载",
        Sub(Task As LoaderTask(Of List(Of NetFile), Boolean))
            '等待原版文件下载完成
            Dim TargetLoaders As List(Of LoaderBase) =
               ClientDownloadLoader.GetLoaderList.Where(Function(l) l.Name = McDownloadClientLibName OrElse l.Name = McDownloadClientJsonName).
               Where(Function(l) l.State <> LoadState.Finished).ToList
            If TargetLoaders.Any Then Logger.Info("OptiFine 安装正在等待原版文件下载完成")
            Do While TargetLoaders.Any AndAlso Not Task.IsCanceled
                TargetLoaders = TargetLoaders.Where(Function(l) l.State <> LoadState.Finished).ToList
                Thread.Sleep(50)
            Loop
            If Task.IsCanceled Then Return
            '复制原版文件
            SyncLock VanillaSyncLock
                Dim ClientName As String = PathUtils.GetLastPart(ClientFolder)
                DirectoryUtils.Create(McFolder & "versions\" & DownloadInfo.Inherit)
                If Not FileUtils.Exists(McFolder & "versions\" & DownloadInfo.Inherit & "\" & DownloadInfo.Inherit & ".json") Then
                    FileUtils.Copy($"{ClientFolder}{ClientName}.json", $"{McFolder}versions\{DownloadInfo.Inherit}\{DownloadInfo.Inherit}.json")
                End If
                If Not FileUtils.Exists(McFolder & "versions\" & DownloadInfo.Inherit & "\" & DownloadInfo.Inherit & ".jar") Then
                    FileUtils.Copy($"{ClientFolder}{ClientName}.jar", $"{McFolder}versions\{DownloadInfo.Inherit}\{DownloadInfo.Inherit}.jar")
                End If
            End SyncLock
        End Sub) With {.ProgressWeight = 0.1, .Show = False})

        '安装（新旧方式均需要原版 Jar 和 Json）
        If IsNewerVersion Then
            Logger.Info($"检测为新版 OptiFine：{DownloadInfo.Inherit}")
            Loaders.Add(New LoaderTask(Of List(Of NetFile), Boolean)("安装 OptiFine（方式 A）",
            Sub(Task As LoaderTask(Of List(Of NetFile), Boolean))
                Dim BaseMcFolderHome As String = RequestTaskTempFolder(True)
                Dim BaseMcFolder As String = BaseMcFolderHome & ".minecraft\"
                Try
                    '准备安装环境
                    If DirectoryUtils.Exists(BaseMcFolder & "versions\" & DownloadInfo.Inherit) Then
                        DirectoryUtils.Delete(BaseMcFolder & "versions\" & DownloadInfo.Inherit)
                    End If
                    DirectoryUtils.Create(BaseMcFolder & "versions\" & DownloadInfo.Inherit & "\")
                    McFolderLauncherProfilesJsonCreate(BaseMcFolder)
                    FileUtils.Copy(McFolder & "versions\" & DownloadInfo.Inherit & "\" & DownloadInfo.Inherit & ".json",
                              BaseMcFolder & "versions\" & DownloadInfo.Inherit & "\" & DownloadInfo.Inherit & ".json")
                    FileUtils.Copy(McFolder & "versions\" & DownloadInfo.Inherit & "\" & DownloadInfo.Inherit & ".jar",
                              BaseMcFolder & "versions\" & DownloadInfo.Inherit & "\" & DownloadInfo.Inherit & ".jar")
                    Task.Progress = 0.06
                    '进行安装
                    Dim UseJavaWrapper As Boolean = True
Retry:
                    Try
                        McDownloadOptiFineInstall(BaseMcFolderHome, Target, Task, UseJavaWrapper)
                    Catch ex As Exception
                        If UseJavaWrapper Then
                            Logger.Warn(ex, "使用 JavaWrapper 安装 OptiFine 失败，将不使用 JavaWrapper 并重试")
                            UseJavaWrapper = False
                            GoTo Retry
                        Else
                            Throw New Exception("运行 OptiFine 安装器失败", ex)
                        End If
                    End Try
                    Task.Progress = 0.96
                    '复制文件
                    FileUtils.Delete(BaseMcFolder & "launcher_profiles.json")
                    DirectoryUtils.Copy(BaseMcFolder, McFolder)
                    Task.Progress = 0.98
                    '清理文件
                    FileUtils.Delete(Target)
                    DirectoryUtils.Delete(BaseMcFolderHome)
                Catch ex As Exception
                    Throw New Exception("安装 OptiFine（方式 A）失败", ex)
                End Try
            End Sub) With {.ProgressWeight = 8})
        Else
            Logger.Info($"检测为旧版 OptiFine：{DownloadInfo.Inherit}")
            Loaders.Add(New LoaderTask(Of List(Of NetFile), Boolean)("安装 OptiFine（方式 B）",
            Sub(Task As LoaderTask(Of List(Of NetFile), Boolean))
                Try
                    '复制 Jar 文件
                    FileUtils.Copy(McFolder & "versions\" & DownloadInfo.Inherit & "\" & DownloadInfo.Inherit & ".jar", VersionFolder & Id & ".jar")
                    Task.Progress = 0.7
                    '建立 Json 文件
                    Dim InheritInstance As New McInstance(McFolder & "versions\" & DownloadInfo.Inherit)
                    Dim Json As String = PCL.Download.OptiFineProfile.BuildLegacyProfileJson(
                        New PCL.Download.OptiFineLegacyProfile(
                            Id, DownloadInfo.Inherit, DownloadInfo.FileName, DownloadInfo.ReleaseTime,
                            InheritInstance.ReleaseTime.ToString("yyyy'-'MM'-'dd"),
                            InheritInstance.IsOldJson,
                            If(InheritInstance.IsOldJson, InheritInstance.JsonObject("minecraftArguments").ToString, Nothing)))
                    FileUtils.Write(VersionFolder & Id & ".json", Json)
                Catch ex As Exception
                    Throw New Exception("安装 OptiFine（方式 B）失败", ex)
                End Try
            End Sub) With {.ProgressWeight = 1})
        End If

        Return Loaders
    End Function
    ''' <summary>
    ''' 获取保存某个 OptiFine 版本的加载器列表。
    ''' </summary>
    Private Function McDownloadOptiFineSaveLoader(DownloadInfo As DlOptiFineListEntry, TargetFolder As String) As List(Of LoaderBase)
        Dim Loaders As New List(Of LoaderBase)
        '获取下载地址
        Loaders.Add(New LoaderTask(Of DlOptiFineListEntry, List(Of NetFile))("获取 OptiFine 下载地址",
        Sub(Task As LoaderTask(Of DlOptiFineListEntry, List(Of NetFile)))
            '候选源推导已迁至 C#（镜像地址形状 + 官方页解析），「安装」与「另存为」共用这一份
            Dim Sources = McDownloadOptiFineSources(DownloadInfo, Task)
            Task.Progress = 0.9
            '构造文件请求
            Task.Output = New List(Of NetFile) From {New NetFile(Sources, TargetFolder, New FileChecker With {.MinSize = 64 * 1024})}
        End Sub) With {.ProgressWeight = 6})
        '下载
        Loaders.Add(New LoaderDownload("下载 OptiFine 主文件", New List(Of NetFile)) With {.ProgressWeight = 10, .Block = True})
        Return Loaders
    End Function

#End Region

#Region "OptiFine 下载菜单"

    Public Function OptiFineDownloadListItem(Entry As DlOptiFineListEntry, OnClick As MyListItem.ClickEventHandler, IsSaveOnly As Boolean) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            '建立控件
            Dim NewItem As New MyListItem With {
                .Title = Entry.DisplayName, .SnapsToDevicePixels = True, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Entry,
                .Info = If(Entry.IsPreview, "测试版", "正式版") &
                        If(Entry.ReleaseTime = "", "", "，发布于 " & Entry.ReleaseTime) &
                        If(Entry.RequiredForgeVersion Is Nothing, "，不兼容 Forge", If(Entry.RequiredForgeVersion = "", "", "，兼容 Forge " & Entry.RequiredForgeVersion)),
                .Logo = PathImage & "Blocks/GrassPath.png"
            }
            AddHandler NewItem.Click, OnClick
            '建立菜单
            If IsSaveOnly Then
                NewItem.ContentHandler = AddressOf OptiFineSaveContMenuBuild
            Else
                NewItem.ContentHandler = AddressOf OptiFineContMenuBuild
            End If
            Return NewItem
        End Function) With {.Height = 42}
    End Function
    Private Sub OptiFineSaveContMenuBuild(sender As Object, e As EventArgs)
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf OptiFineLog_Click
        sender.Buttons = {BtnInfo}
    End Sub
    Private Sub OptiFineContMenuBuild(sender As Object, e As EventArgs)
        Dim BtnSave As New MyIconButton With {.Logo = Logo.IconButtonSave, .ToolTip = "另存为"}
        AddHandler BtnSave.Click, AddressOf OptiFineSave_Click
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf OptiFineLog_Click
        sender.Buttons = {BtnSave, BtnInfo}
    End Sub
    Private Sub OptiFineLog_Click(sender As Object, e As RoutedEventArgs)
        Dim Version As DlOptiFineListEntry
        If sender.Tag IsNot Nothing Then
            Version = sender.Tag
        ElseIf sender.Parent.Tag IsNot Nothing Then
            Version = sender.Parent.Tag
        Else
            Version = sender.Parent.Parent.Tag
        End If
        OpenWebsite("https://optifine.net/changelog?f=" & Version.FileName)
    End Sub
    Public Sub OptiFineSave_Click(sender As Object, e As RoutedEventArgs)
        Dim Version As DlOptiFineListEntry
        If sender.Tag IsNot Nothing Then
            Version = sender.Tag
        ElseIf sender.Parent.Tag IsNot Nothing Then
            Version = sender.Parent.Tag
        Else
            Version = sender.Parent.Parent.Tag
        End If
        McDownloadOptiFineSave(Version)
    End Sub

#End Region

#Region "LiteLoader 下载"

    Private Sub McDownloadLiteLoaderSave(DownloadInfo As DlLiteLoaderListEntry)
        Try
            Dim Id As String = DownloadInfo.Inherit
            Dim Target As String = Dialogs.SaveFile("选择保存位置", DownloadInfo.FileName.Replace("-SNAPSHOT", ""), filter:={("jar", "LiteLoader 安装器")})
            If Target Is Nothing Then Return

            '重复任务检查
            For Each OngoingLoader In LoaderTaskbar.ToList()
                If OngoingLoader.Name <> $"LiteLoader {Id} 下载" Then Continue For
                Hint("该版本正在下载中！", HintType.Red)
                Return
            Next

            '构造步骤加载器
            Dim Loaders As New List(Of LoaderBase)
            '下载
            Dim Address As New List(Of String)
            If DownloadInfo.IsLegacy Then
                '老版本
                Select Case DownloadInfo.Inherit
                    Case "1.7.10"
                        Address.Add("https://dl.liteloader.com/redist/1.7.10/liteloader-installer-1.7.10-04.jar")
                    Case "1.7.2"
                        Address.Add("https://dl.liteloader.com/redist/1.7.2/liteloader-installer-1.7.2-04.jar")
                    Case "1.6.4"
                        Address.Add("https://dl.liteloader.com/redist/1.6.4/liteloader-installer-1.6.4-01.jar")
                    Case "1.6.2"
                        Address.Add("https://dl.liteloader.com/redist/1.6.2/liteloader-installer-1.6.2-04.jar")
                    Case "1.5.2"
                        Address.Add("https://dl.liteloader.com/redist/1.5.2/liteloader-installer-1.5.2-01.jar")
                    Case Else
                        Throw New NotSupportedException("未知的 Minecraft 版本（" & DownloadInfo.Inherit & "）")
                End Select
            Else
                '官方源
                Address.Add("http://jenkins.liteloader.com/job/LiteLoaderInstaller%20" & DownloadInfo.Inherit & "/lastSuccessfulBuild/artifact/" & If(DownloadInfo.Inherit = "1.8", "ant/dist/", "build/libs/") & DownloadInfo.FileName)
            End If
            Loaders.Add(New LoaderDownload("下载主文件", New List(Of NetFile) From {New NetFile(Address.ToArray, Target, New FileChecker With {.MinSize = 1024 * 1024})}) With {.ProgressWeight = 15})
            '启动
            Dim Loader As New LoaderCombo(Of DlLiteLoaderListEntry)("LiteLoader " & Id & " 安装器下载", Loaders) With {.OnStateChanged = AddressOf LoaderStateChangedHintOnly}
            Loader.Start(DownloadInfo)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()

        Catch ex As Exception
            Logger.Error(ex, "开始 LiteLoader 安装器下载失败")
        End Try
    End Sub

    ''' <summary>
    ''' 获取下载某个 LiteLoader 版本的加载器列表。
    ''' 这不会补全所需的支持库文件，需要后续手动补全支持库。
    ''' </summary>
    Private Function McDownloadLiteLoaderLoader(DownloadInfo As DlLiteLoaderListEntry, McFolder As String) As List(Of LoaderBase)
        Dim Loaders As New List(Of LoaderBase)

        '安装
        Loaders.Add(New LoaderTask(Of String, String)("安装 LiteLoader",
        Sub(Task As LoaderTask(Of String, String))
            Try
                Dim VersionName As String = DownloadInfo.Inherit & PCL.Download.LiteLoaderProfile.InstanceSuffix
                Dim VersionFolder As String = McFolder & "versions\" & VersionName & "\"
                '版本 json 的字段顺序与「追加 liteloader 库」规则已迁至 C#
                Dim Json As String = PCL.Download.LiteLoaderProfile.BuildVersionJson(
                    DownloadInfo.JsonToken.ToString(), DownloadInfo.Inherit, DownloadInfo.ReleaseTime)
                '输出 Json 文件，同时会新建该文件夹
                FileUtils.Write(VersionFolder & VersionName & ".json", Json)
            Catch ex As Exception
                Throw New Exception("安装新 LiteLoader 版本失败", ex)
            End Try
        End Sub) With {.ProgressWeight = 1})

        Return Loaders
    End Function

#End Region

#Region "LiteLoader 下载菜单"

    Public Function LiteLoaderDownloadListItem(Entry As DlLiteLoaderListEntry, OnClick As MyListItem.ClickEventHandler, IsSaveOnly As Boolean) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            '建立控件
            Dim NewItem As New MyListItem With {
                .Title = Entry.Inherit, .SnapsToDevicePixels = True, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Entry,
                .Info = If(Entry.IsPreview, "测试版", "稳定版") & If(Entry.ReleaseTime = "", "", "，发布于 " & Entry.ReleaseTime),
                .Logo = PathImage & "Blocks/Egg.png"
            }
            AddHandler NewItem.Click, OnClick
            '建立菜单
            If IsSaveOnly Then
                NewItem.ContentHandler = AddressOf LiteLoaderSaveContMenuBuild
            Else
                NewItem.ContentHandler = AddressOf LiteLoaderContMenuBuild
            End If
            '结束
            Return NewItem
        End Function) With {.Height = 42}
    End Function
    Private Sub LiteLoaderSaveContMenuBuild(sender As MyListItem, e As EventArgs)
        If sender.Tag.IsLegacy Then
            sender.Buttons = {}
        Else
            Dim BtnList As New MyIconButton With {.Logo = Logo.IconButtonList, .ToolTip = "查看全部版本", .Tag = sender}
            AddHandler BtnList.Click, AddressOf LiteLoaderAll_Click
            sender.Buttons = {BtnList}
        End If
    End Sub
    Private Sub LiteLoaderContMenuBuild(sender As MyListItem, e As EventArgs)
        Dim BtnSave As New MyIconButton With {.Logo = Logo.IconButtonSave, .ToolTip = "保存安装器", .Tag = sender}
        AddHandler BtnSave.Click, AddressOf LiteLoaderSave_Click
        If sender.Tag.IsLegacy Then
            sender.Buttons = {BtnSave}
        Else
            Dim BtnList As New MyIconButton With {.Logo = Logo.IconButtonList, .ToolTip = "查看全部版本", .Tag = sender}
            AddHandler BtnList.Click, AddressOf LiteLoaderAll_Click
            sender.Buttons = {BtnSave, BtnList}
        End If
    End Sub
    Private Sub LiteLoaderAll_Click(sender As Object, e As RoutedEventArgs)
        Dim Version As DlLiteLoaderListEntry
        If TypeOf sender.Tag Is DlLiteLoaderListEntry Then
            Version = sender.Tag
        Else
            Version = sender.Tag.Tag
        End If
        OpenWebsite("https://jenkins.liteloader.com/view/" & Version.Inherit)
    End Sub
    Public Sub LiteLoaderSave_Click(sender As Object, e As RoutedEventArgs)
        'ListItem 与小按钮都会调用这个方法
        Dim Version As DlLiteLoaderListEntry
        If TypeOf sender.Tag Is DlLiteLoaderListEntry Then
            Version = sender.Tag
        Else
            Version = sender.Tag.Tag
        End If
        McDownloadLiteLoaderSave(Version)
    End Sub

#End Region

#Region "Forgelike 下载"

    Public Sub McDownloadForgelikeSave(Info As DlForgelikeEntry)
        Try
            Dim Target As String = Dialogs.SaveFile("选择保存位置", $"{Info.LoaderName}-{Info.Inherit}-{Info.VersionName}.{Info.FileExtension}",
                                            filter:={(Info.FileExtension, $"{Info.LoaderName} 安装器")})
            If Target Is Nothing Then Return
            Dim DisplayName As String = $"{Info.LoaderName} {Info.Inherit} - {Info.VersionName}"

            '重复任务检查
            For Each OngoingLoader In LoaderTaskbar.ToList()
                If OngoingLoader.Name <> $"{DisplayName} 下载" Then Continue For
                Hint("该版本正在下载中！", HintType.Red)
                Return
            Next

            '获取下载地址（镜像在前、官方在后）
            Dim Files As New List(Of NetFile)
            If Info.IsNeoForge Then
                Dim NeoForge As DlNeoForgeListEntry = Info
                Dim Installer = PCL.Download.ForgelikeInstaller.BuildNeoForgeInstaller(NeoForge.Inherit, NeoForge.ApiName)
                Files.Add(New NetFile(Installer.Urls, Target, New FileChecker With {.MinSize = CInt(Installer.MinSize)}))
            Else
                Dim Forge As DlForgeVersionEntry = Info
                Dim Installer = PCL.Download.ForgelikeInstaller.BuildForgeInstaller(
                    Forge.Inherit, Forge.FileVersion, Forge.Category, Forge.FileExtension, Forge.Hash)
                Files.Add(New NetFile(Installer.Urls, Target,
                                      New FileChecker With {.MinSize = CInt(Installer.MinSize), .Hash = Installer.Hash}))
            End If

            '构造加载器
            Dim Loaders As New List(Of LoaderBase)
            Loaders.Add(New LoaderDownload("下载主文件", Files) With {.ProgressWeight = 6})

            '启动
            Dim Loader = New LoaderCombo(Of DlForgelikeEntry)(DisplayName & " 下载", Loaders) With {.OnStateChanged = AddressOf LoaderStateChangedHintOnly}
            Loader.Start(Info)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()

        Catch ex As Exception
            Logger.Error(ex, $"开始 {Info.LoaderName} 安装器下载失败")
        End Try
    End Sub

    Private Sub ForgelikeInjector(Target As String, Task As LoaderTask(Of Boolean, Boolean), McFolder As String, UseJavaWrapper As Boolean, IsNeoForge As Boolean)
        '选择 Java
        Dim Java As Java = SelectOrDownloadJava(ValueRange(Of Version).AtLeast(New Version(8, 0, 60)), True, Task.CreateCancellationToken, Task.CreateSyncProgressProvider(0, 0.2))
        If Task.IsCanceled Then Return
        If Java Is Nothing Then Throw New OperationCanceledException
        '添加 Java Wrapper 作为主 Jar
        Dim UseWrapper As Boolean = UseJavaWrapper AndAlso Not Settings.Get(Of Boolean)("LaunchAdvanceDisableJLW")
        Dim WrapperPath As String = If(UseWrapper, LaunchUtils.ExtractPatch("Launch\JavaWrapper.jar", PathPure.Value), Nothing)
        Dim Arguments As String = PCL.Download.ForgelikeInstaller.BuildArguments(
            Java.Version.Major, PathTemp & "Cache\forge_installer.jar", Target, McFolder,
            WrapperPath, PathUtils.RemoveSlashSuffix(PathPure.Value), UseWrapper)
        '开始启动
        SyncLock InstallSyncLock
            Dim Info = New ProcessStartInfo With {
                .FileName = Java.JavaExePath,
                .Arguments = Arguments,
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardError = True,
                .RedirectStandardOutput = True
            }
            Dim LoaderName As String = If(IsNeoForge, "NeoForge", "Forge")
            Logger.Info($"开始安装 {LoaderName}：{Arguments}")
            Dim process As New Process With {.StartInfo = Info}
            Dim LastResults As New Queue(Of String)
            Using outputWaitHandle As New AutoResetEvent(False)
                Using errorWaitHandle As New AutoResetEvent(False)
                    AddHandler process.OutputDataReceived,
                    Function(sender, e)
                        Try
                            If e.Data Is Nothing Then
                                outputWaitHandle.[Set]()
                            Else
                                LastResults.Enqueue(e.Data)
                                If LastResults.Count > 100 Then LastResults.Dequeue()
                                ForgelikeInjectorLine(e.Data, Task)
                            End If
                        Catch ex As ObjectDisposedException
                        Catch ex As Exception
                            Logger.Warn(ex, $"读取 {LoaderName} 安装器信息失败")
                        End Try
                        Try
                            If Task.State = LoadState.Canceled AndAlso Not process.HasExited Then
                                Logger.Info($"由于任务取消，已中止 {LoaderName} 安装")
                                process.Kill()
                            End If
                        Catch
                        End Try
                        Return Nothing
                    End Function
                    AddHandler process.ErrorDataReceived,
                    Function(sender, e)
                        Try
                            If e.Data Is Nothing Then
                                errorWaitHandle.[Set]()
                            Else
                                LastResults.Enqueue(e.Data)
                                If LastResults.Count > 100 Then LastResults.Dequeue()
                                ForgelikeInjectorLine(e.Data, Task)
                            End If
                        Catch ex As ObjectDisposedException
                        Catch ex As Exception
                            Logger.Warn(ex, $"读取 {LoaderName} 安装器错误信息失败")
                        End Try
                        Try
                            If Task.State = LoadState.Canceled AndAlso Not process.HasExited Then
                                Logger.Info($"由于任务取消，已中止 {LoaderName} 安装")
                                process.Kill()
                            End If
                        Catch
                        End Try
                        Return Nothing
                    End Function
                    process.Start()
                    process.BeginOutputReadLine()
                    process.BeginErrorReadLine()
                    '等待
                    Do Until process.HasExited
                        Thread.Sleep(10)
                    Loop
                    '输出
                    outputWaitHandle.WaitOne(10000)
                    errorWaitHandle.WaitOne(10000)
                    process.Dispose()
                    '检查是否安装成功：最后 5 行中是否有 true（true 可能在倒数数行，见 #832）
                    If LastResults.Reverse().Take(5).Any(Function(l) l = "true") Then Return
                    Logger.Warn(LastResults.Join(vbCrLf))
                    Dim LastLines As String = ""
                    For i As Integer = Math.Max(0, LastResults.Count - 5) To LastResults.Count - 1 '最后 5 行
                        LastLines &= vbCrLf & LastResults(i)
                    Next
                    Throw New Exception($"{LoaderName} 安装器出错，日志结束部分为：" & LastLines)
                End Using
            End Using
        End SyncLock
    End Sub
    ''' <summary>
    ''' 把 Forge / NeoForge 安装器的一行输出翻译成进度动作。
    ''' </summary>
    ''' <remarks>
    ''' 识别表在 <c>PCL.Download.ForgelikeInstaller.ReadProgress</c>；不认识的行只进 trace，不上抛进度。
    ''' </remarks>
    Private Sub ForgelikeInjectorLine(Content As String, Task As LoaderTask(Of Boolean, Boolean))
        Dim ProgressStep = PCL.Download.ForgelikeInstaller.ReadProgress(Content)
        If Not ProgressStep.HasValue Then
            Logger.Trace(Content)
            Return
        End If
        '校验命中这类行是累加的，别写成赋值，否则进度条会来回跳
        If ProgressStep.Value.IsIncrement Then
            Task.Progress += ProgressStep.Value.Progress
        Else
            Task.Progress = ProgressStep.Value.Progress
        End If
        Logger.Info(Content)
    End Sub

    ''' <summary>
    ''' 获取下载某个 Forgelike 版本的加载器列表。
    ''' </summary>
    Private Function McDownloadForgelikeLoader(IsNeoForge As Boolean, LoaderVersion As String, NewInstanceName As String, Inherit As String, Info As DlForgelikeEntry, McFolder As String, ClientDownloadLoader As LoaderCombo(Of String), ClientFolder As String) As List(Of LoaderBase)
        '参数初始化：整合包/实例给出的版本串可能自带 MC 版本，或缺少 NeoForge 的包名前缀
        Dim VersionTarget = PCL.Download.ForgelikeInstaller.Normalize(IsNeoForge, LoaderVersion, Inherit)
        Inherit = VersionTarget.Inherit
        LoaderVersion = VersionTarget.LoaderVersion
        If IsNeoForge AndAlso Info Is Nothing Then Info = New DlNeoForgeListEntry(LoaderVersion)
        Dim LoaderName As String = If(IsNeoForge, "NeoForge", "Forge")
        Dim InstallerAddress As String = RequestTaskTempFolder(True) & "forge_installer.jar"
        Dim VersionFolder As String = $"{McFolder}versions\{NewInstanceName}\"
        Dim Loaders As New List(Of LoaderBase)

        '获取 Forge 下载信息
        If Info Is Nothing Then
            Loaders.Add(New LoaderTask(Of String, String)($"获取 {LoaderName} 详细信息",
            Sub(Task As LoaderTask(Of String, String))
                '获取 Forge 对应 MC 版本列表
                Dim ForgeLoader = New LoaderTask(Of String, List(Of DlForgeVersionEntry))("McDownloadForgeLoader " & Inherit, AddressOf DlForgeVersionMain)
                ForgeLoader.WaitForExit(Inherit)
                Task.Progress = 0.8
                '查找对应版本
                For Each ForgeVersion In ForgeLoader.Output
                    If McUtils.CompareVersion(ForgeVersion.Version.ToString, LoaderVersion) = 0 Then
                        Info = ForgeVersion
                        Return
                    End If
                Next
                Throw New Exception($"未能找到 {LoaderName} " & Inherit & "-" & LoaderVersion & " 的详细信息！")
            End Sub) With {.ProgressWeight = 3})
        End If
        '下载 Forgelike 主文件
        Loaders.Add(New LoaderTask(Of String, List(Of NetFile))($"准备下载 {LoaderName}",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            '添加主文件下载（镜像在前、官方在后）
            Dim Files As New List(Of NetFile)
            If Info.IsNeoForge Then
                Dim Neo As DlNeoForgeListEntry = Info
                Dim Installer = PCL.Download.ForgelikeInstaller.BuildNeoForgeInstaller(Neo.Inherit, Neo.ApiName)
                Files.Add(New NetFile(Installer.Urls, InstallerAddress,
                                      New FileChecker With {.MinSize = CInt(Installer.MinSize)}))
            Else
                Dim Forge As DlForgeVersionEntry = Info
                Dim Installer = PCL.Download.ForgelikeInstaller.BuildForgeInstaller(
                    Forge.Inherit, Forge.FileVersion, Forge.Category, Forge.FileExtension, Forge.Hash)
                Files.Add(New NetFile(Installer.Urls, InstallerAddress,
                                      New FileChecker With {.MinSize = CInt(Installer.MinSize), .Hash = Installer.Hash}))
            End If
            Task.Output = Files
        End Sub) With {.ProgressWeight = 0.5, .Show = False})
        Loaders.Add(New LoaderDownload($"下载 {LoaderName} 主文件", New List(Of NetFile)) With {.ProgressWeight = 9})

        '安装（仅在新版安装时需要原版 Jar）
        Dim CurrentMcFolder = McFolderSelected
        If IsNeoForge OrElse LoaderVersion.BeforeFirst(".") >= 20 Then
            Logger.Info($"检测为{If(IsNeoForge, " Neo", "新版 ")}Forge：{LoaderVersion}")
            Dim Libs As List(Of McLibEntry) = Nothing
            Loaders.Add(New LoaderTask(Of String, List(Of NetFile))($"分析 {LoaderName} 支持库文件",
            Sub(Task As LoaderTask(Of String, List(Of NetFile)))
                Task.Output = New List(Of NetFile)
                Dim Installer As ZipArchive = Nothing
                Try
                    '解压并获取、合并两个 Json 的信息
                    Installer = FileUtils.OpenZip(InstallerAddress)
                    Task.Progress = 0.2
                    Dim Json As JObject = Installer.GetEntry("install_profile.json").Open().ReadString().DeserializeJson()
                    Dim Json2 As JObject = Installer.GetEntry("version.json").Open().ReadString().DeserializeJson()
                    Json.Merge(Json2)
                    '获取 Lib 下载信息
                    Libs = McLibListGetWithJson(Json, True, CurrentMcFolder)
                    '添加 Mappings 下载信息
                    If Json("data") IsNot Nothing AndAlso Json("data")("MOJMAPS") IsNot Nothing Then
                        '下载原版 Json 文件
                        Task.Progress = 0.4
                        Dim RawJson As JObject = NetRequestByLoader(DlSourceLauncherOrMetaGet(DlClientListGet(Inherit)), IsJson:=True).DeserializeJson()
                        '[net.minecraft:client:1.17.1-20210706.113038:mappings@txt] 或 @tsrg]
                        Dim OriginalName As String = Json("data")("MOJMAPS")("client").ToString.Trim("[]".ToCharArray()).BeforeFirst("@")
                        Dim Address = Path.ChangeExtension(McLibGet(OriginalName, CustomMcFolder:=CurrentMcFolder), "." & Json("data")("MOJMAPS")("client").ToString.Trim("[]".ToCharArray()).Split("@")(1))
                        Dim ClientMappings As JToken = RawJson("downloads")("client_mappings")
                        Libs.Add(New McLibEntry With {
                                 .IsNatives = False, .LocalPath = Address, .OriginalName = OriginalName,
                                 .Url = ClientMappings("url"), .Size = ClientMappings("size"), .SHA1 = ClientMappings("sha1")})
                        Logger.Info($"需要下载 Mappings：{ClientMappings("url")} (SHA1: {ClientMappings("sha1")})")
                    End If
                    Task.Progress = 0.8
                    '去除其中的原始 Forgelike 项
                    For i = 0 To Libs.Count - 1
                        If Libs(i).LocalPath.EndsWithF($"{LoaderName.Lower}-{Inherit}-{LoaderVersion}.jar") OrElse
                           Libs(i).LocalPath.EndsWithF($"{LoaderName.Lower}-{Inherit}-{LoaderVersion}-client.jar") Then
                            Logger.Warn($"已从待下载 {LoaderName} 支持库中移除：{Libs(i).LocalPath}")
                            Libs.RemoveAt(i)
                            Exit For
                        End If
                    Next
                    Task.Output = McLibNetFilesFromTokens(Libs, CurrentMcFolder)
                Catch ex As Exception
                    Throw New Exception($"获取{If(IsNeoForge, " Neo", "新版 ")}Forge 支持库列表失败", ex)
                Finally
                    If Installer IsNot Nothing Then Installer.Dispose()
                End Try
            End Sub) With {.ProgressWeight = 1})
            Loaders.Add(New LoaderDownload($"下载 {LoaderName} 支持库文件", New List(Of NetFile)) With {.ProgressWeight = 12})
            Loaders.Add(New LoaderTask(Of List(Of NetFile), Boolean)($"获取 {LoaderName} 支持库文件",
            Sub(Task As LoaderTask(Of List(Of NetFile), Boolean))
#Region "Forgelike 文件"
                For Each LibFile As McLibEntry In Libs
                    Dim RealPath As String = Path.Combine(McFolder, LibFile.LocalPath.Substring(CurrentMcFolder.Length))
                    If Not FileUtils.Exists(RealPath) Then FileUtils.Copy(LibFile.LocalPath, RealPath)
                    Logger.Trace($"复制的 {LoaderName} 支持库文件：{LibFile.LocalPath}")
                Next
#End Region
#Region "原版文件"
                '等待原版文件下载完成
                Dim TargetLoaders As List(Of LoaderBase) =
                    ClientDownloadLoader.GetLoaderList.Where(Function(l) l.Name = McDownloadClientLibName OrElse l.Name = McDownloadClientJsonName).
                    Where(Function(l) l.State <> LoadState.Finished).ToList()
                If TargetLoaders.Any Then Logger.Info($"{LoaderName} 安装正在等待原版文件下载完成")
                Do While TargetLoaders.Any AndAlso Not Task.IsCanceled
                    TargetLoaders = TargetLoaders.Where(Function(l) l.State <> LoadState.Finished).ToList
                    Thread.Sleep(50)
                Loop
                If Task.IsCanceled Then Return
                '复制原版文件
                SyncLock VanillaSyncLock
                    Dim ClientName As String = PathUtils.GetLastPart(ClientFolder)
                    DirectoryUtils.Create(McFolder & "versions\" & Inherit)
                    If Not FileUtils.Exists(McFolder & "versions\" & Inherit & "\" & Inherit & ".json") Then
                        FileUtils.Copy(ClientFolder & ClientName & ".json", McFolder & "versions\" & Inherit & "\" & Inherit & ".json")
                    End If
                    If Not FileUtils.Exists(McFolder & "versions\" & Inherit & "\" & Inherit & ".jar") Then
                        FileUtils.Copy(ClientFolder & ClientName & ".jar", McFolder & "versions\" & Inherit & "\" & Inherit & ".jar")
                    End If
                End SyncLock
#End Region
            End Sub) With {.ProgressWeight = 0.1, .Show = False})
            Loaders.Add(New LoaderTask(Of Boolean, Boolean)(If(IsNeoForge, "安装 NeoForge", "安装 Forge（方式 A）"),
            Sub(Task As LoaderTask(Of Boolean, Boolean))
                Try
                    Logger.Info($"开始进行 Forgelike 安装：{InstallerAddress}")
                    '记录当前文件夹列表（在新建目标文件夹之前）
                    Dim OldList = DirectoryUtils.EnumerateDirectories(McFolder & "versions\").ToList()
                    '新建目标版本文件夹
                    DirectoryUtils.Create(VersionFolder)
                    Task.Progress = 0.04
                    '释放 launcher_installer.json
                    McFolderLauncherProfilesJsonCreate(McFolder)
                    Task.Progress = 0.05
                    '运行 Forge 安装器
                    Dim UseJavaWrapper As Boolean = True
Retry:
                    Try
                        '释放 Forge 注入器
                        ExtractResources(PathTemp & "Cache\forge_installer.jar", "ForgeInstaller")
                        Task.Progress = 0.06
                        '运行注入器
                        ForgelikeInjector(InstallerAddress, Task, McFolder, UseJavaWrapper, IsNeoForge)
                        Task.Progress = 0.97
                    Catch ex As Exception
                        If UseJavaWrapper Then
                            Logger.Warn(ex, $"使用 JavaWrapper 安装 {LoaderName} 失败，将不使用 JavaWrapper 并重试")
                            UseJavaWrapper = False
                            GoTo Retry
                        Else
                            Throw New Exception($"运行 {LoaderName} 安装器失败", ex)
                        End If
                    End Try
                    '复制新增的版本 Json
                    Dim DeltaList = DirectoryUtils.EnumerateDirectories(McFolder & "versions\").
                        SkipWhile(Function(i) OldList.Contains(i)).Select(Function(f) DirectoryUtils.GetInfo(f)).ToList()
                    If DeltaList.Count > 1 Then
                        '它可能和 OptiFine 安装同时运行，导致增加的文件不止一个（这导致了 #151）
                        '也可能是因为 Forge 安装器的 Bug，生成了一个名字错误的文件夹，所以需要检查文件夹是否为空
                        DeltaList = DeltaList.Where(Function(l) l.Name.ContainsIgnoreCase("forge") AndAlso l.EnumerateFiles.Any).ToList
                    End If
                    If DeltaList.Count = 1 Then
                        '如果没有新增文件夹，那么预测的文件夹名就是正确的
                        '如果只新增 1 个文件夹，那么复制 json 文件
                        Dim JsonFile As FileInfo = DeltaList(0).EnumerateFiles.First()
                        FileUtils.Copy(JsonFile.FullName, VersionFolder & NewInstanceName & ".json")
                        Logger.Info($"已复制新增的版本 JSON 文件：{JsonFile.FullName} -> {VersionFolder}{NewInstanceName}.json")
                    ElseIf DeltaList.Count > 1 Then
                        '新增了多个文件夹
                        Logger.Info($"有多个疑似的新增版本，无法确定：{DeltaList.Select(Function(d) d.Name).Join(";")}")
                    Else
                        '没有新增文件夹
                        Logger.Info("未找到新增的版本文件夹")
                    End If
                Catch ex As Exception
                    Throw New Exception($"安装新 {LoaderName} 版本失败", ex)
                Finally
                    '清理文件
                    Try
                        FileUtils.Delete(InstallerAddress)
                    Catch ex As Exception
                        Logger.Warn(ex, $"安装 {LoaderName} 清理文件时出错")
                    End Try
                End Try
            End Sub) With {.ProgressWeight = 10})
        Else
            Logger.Info($"检测为非新版 Forge：{LoaderVersion}")
            Loaders.Add(New LoaderTask(Of List(Of NetFile), Boolean)($"安装 {LoaderName}（方式 B）",
            Sub(Task As LoaderTask(Of List(Of NetFile), Boolean))
                Dim Installer As ZipArchive = Nothing
                Try
                    '解压并获取信息
                    Installer = FileUtils.OpenZip(InstallerAddress)
                    Task.Progress = 0.2
                    Dim Json As JObject = Installer.GetEntry("install_profile.json").Open().ReadString().DeserializeJson()
                    Task.Progress = 0.4
                    '新建版本文件夹
                    DirectoryUtils.Create(VersionFolder)
                    Task.Progress = 0.5
                    If Json("install") Is Nothing Then
                        '中版：Legacy 方式 1
                        Logger.Info($"开始进行 Forge 安装，Legacy 方式 1：{InstallerAddress}")
                        '建立 Json 文件
                        Dim JsonVersion As JObject = Installer.GetEntry(Json("json").ToString.TrimStart("/")).Open().ReadString().DeserializeJson()
                        JsonVersion("id") = NewInstanceName
                        FileUtils.Write(VersionFolder & NewInstanceName & ".json", JsonVersion.ToString)
                        Task.Progress = 0.6
                        '解压支持库文件
                        Installer.Dispose()
                        FileUtils.ExtractToDirectory(InstallerAddress, InstallerAddress & "_unrar\")
                        If DirectoryUtils.Exists(InstallerAddress & "_unrar\maven\") Then DirectoryUtils.Copy(InstallerAddress & "_unrar\maven\", McFolder & "libraries\")
                        DirectoryUtils.Delete(InstallerAddress & "_unrar\")
                    Else
                        '旧版：Legacy 方式 2
                        Logger.Info($"开始进行 Forge 安装，Legacy 方式 2：{InstallerAddress}")
                        '解压 Jar 文件
                        Dim JarAddress As String = McLibGet(Json("install")("path"), CustomMcFolder:=McFolder)
                        FileUtils.Write(JarAddress, Installer.GetEntry(Json("install")("filePath")).Open)
                        Task.Progress = 0.9
                        '建立 Json 文件
                        Json("versionInfo")("id") = NewInstanceName
                        If Json("versionInfo")("inheritsFrom") Is Nothing Then CType(Json("versionInfo"), JObject).Add("inheritsFrom", Inherit)
                        FileUtils.Write(VersionFolder & NewInstanceName & ".json", Json("versionInfo").ToString)
                    End If
                Catch ex As Exception
                    Throw New Exception("非新版方式安装 Forge 失败", ex)
                Finally
                    Try
                        '清理文件
                        If Installer IsNot Nothing Then Installer.Dispose()
                        FileUtils.Delete(InstallerAddress)
                        DirectoryUtils.Delete(InstallerAddress & "_unrar\")
                    Catch ex As Exception
                        Logger.Warn(ex, "非新版方式安装 Forge 清理文件时出错")
                    End Try
                End Try
            End Sub) With {.ProgressWeight = 1})
        End If

        Return Loaders
    End Function

#End Region

#Region "Forge 下载菜单"

    Public Sub ForgeDownloadListItemPreload(Stack As StackPanel, Entries As List(Of DlForgeVersionEntry), OnClick As MyListItem.ClickEventHandler, IsSaveOnly As Boolean)
        '如果只有一个版本，则不特别列出
        If Entries.IsSingle Then Return
        '获取推荐版本与最新版本
        Dim FreshVersion As DlForgeVersionEntry = Nothing
        If Entries.Any Then
            FreshVersion = Entries(0)
        Else
            Logger.Info("未找到可用的 Forge 版本")
        End If
        Dim RecommendedVersion As DlForgeVersionEntry = Nothing
        For Each Entry In Entries
            If Entry.IsRecommended Then RecommendedVersion = Entry
        Next
        '若推荐版本与最新版本为同一版本，则仅显示推荐版本
        If FreshVersion IsNot Nothing AndAlso FreshVersion Is RecommendedVersion Then FreshVersion = Nothing
        '显示各个版本
        If RecommendedVersion IsNot Nothing Then
            Dim Recommended = ForgeDownloadListItem(RecommendedVersion, OnClick, IsSaveOnly).Init()
            Recommended.Info = "推荐版" & If(Recommended.Info = "", "", "，" & Recommended.Info)
            Stack.Children.Add(Recommended)
        End If
        If FreshVersion IsNot Nothing Then
            Dim Fresh = ForgeDownloadListItem(FreshVersion, OnClick, IsSaveOnly).Init()
            Fresh.Info = "最新版" & If(Fresh.Info = "", "", "，" & Fresh.Info)
            Stack.Children.Add(Fresh)
        End If
        '添加间隔
        Stack.Children.Add(New TextBlock With {.Text = "全部版本 (" & Entries.Count & ")", .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(6, 13, 0, 4)})
    End Sub
    Public Function ForgeDownloadListItem(Entry As DlForgeVersionEntry, OnClick As MyListItem.ClickEventHandler, IsSaveOnly As Boolean) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            '建立控件
            Dim NewItem As New MyListItem With {
                .Title = Entry.VersionName, .SnapsToDevicePixels = True, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Entry,
                .Info = {If(Entry.ReleaseTime = "", "", "发布于 " & Entry.ReleaseTime), If(ModeDebug, "种类：" & Entry.Category, "")}.
                    Where(Function(d) d <> "").Join("，"),
                .Logo = PathImage & "Blocks/Anvil.png"
            }
            AddHandler NewItem.Click, OnClick
            '建立菜单
            If IsSaveOnly Then
                NewItem.ContentHandler = AddressOf ForgeSaveContMenuBuild
            Else
                NewItem.ContentHandler = AddressOf ForgeContMenuBuild
            End If
            '结束
            Return NewItem
        End Function) With {.Height = 42}
    End Function
    Private Sub ForgeContMenuBuild(sender As MyListItem, e As EventArgs)
        Dim BtnSave As New MyIconButton With {.Logo = Logo.IconButtonSave, .ToolTip = "另存为"}
        AddHandler BtnSave.Click, AddressOf ForgeSave_Click
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf ForgeLog_Click
        sender.Buttons = {BtnSave, BtnInfo}
    End Sub
    Private Sub ForgeSaveContMenuBuild(sender As MyListItem, e As EventArgs)
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf ForgeLog_Click
        sender.Buttons = {BtnInfo}
    End Sub
    Private Sub ForgeLog_Click(sender As Object, e As RoutedEventArgs)
        Dim Version As DlForgeVersionEntry
        If sender.Tag IsNot Nothing Then
            Version = sender.Tag
        ElseIf sender.Parent.Tag IsNot Nothing Then
            Version = sender.Parent.Tag
        Else
            Version = sender.Parent.Parent.Tag
        End If
        OpenWebsite($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{Version.Inherit}-{Version.VersionName}/forge-{Version.Inherit}-{Version.VersionName}-changelog.txt")
    End Sub
    Public Sub ForgeSave_Click(sender As Object, e As RoutedEventArgs)
        Dim Version As DlForgeVersionEntry
        If sender.Tag IsNot Nothing Then
            Version = sender.Tag
        ElseIf sender.Parent.Tag IsNot Nothing Then
            Version = sender.Parent.Tag
        Else
            Version = sender.Parent.Parent.Tag
        End If
        McDownloadForgelikeSave(Version)
    End Sub

#End Region

#Region "Forge 推荐版本获取"

    ''' <summary>
    ''' 尝试刷新 Forge 推荐版本缓存。
    ''' </summary>
    Public Sub McDownloadForgeRecommendedRefresh()
        If IsForgeRecommendedRefreshed Then Return
        IsForgeRecommendedRefreshed = True
        RunInNewThread(
        Sub()
            Try
                Logger.Info("刷新 Forge 推荐版本缓存开始")
                Dim Result As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/forge/promos", RequireJson:=True)
                If Result.Length < 1000 Then Throw New Exception("获取的结果过短（" & Result & "）")
                Dim ResultJson As JContainer = Result.DeserializeJson()
                '获取所有推荐版本列表
                Dim RecommendedList As New List(Of String)
                For Each Version As JObject In ResultJson
                    If Version("name") Is Nothing OrElse Version("build") Is Nothing Then Continue For
                    Dim Name As String = Version("name")
                    If Not Name.EndsWithF("-recommended") Then Continue For
                    '内容为："1.15.2":"31.2.0"
                    RecommendedList.Add("""" & Name.Replace("-recommended", """:""" & Version("build")("version").ToString & """"))
                Next
                If RecommendedList.Count < 5 Then Throw New Exception("获取的推荐版本数过少（" & Result & "）")
                '保存
                Dim CacheJson As String = "{" & RecommendedList.Join(",") & "}"
                FileUtils.Write(PathTemp & "Cache\ForgeRecommendedList.json", CacheJson)
                Logger.Info("刷新 Forge 推荐版本缓存成功")
            Catch ex As Exception
                Logger.Warn(ex, "刷新 Forge 推荐版本缓存失败")
            End Try
        End Sub, "ForgeRecommendedRefresh")
    End Sub
    Private IsForgeRecommendedRefreshed As Boolean = False


#End Region

#Region "NeoForge 下载菜单"

    Public Sub NeoForgeDownloadListItemPreload(Stack As StackPanel, Entries As List(Of DlNeoForgeListEntry), OnClick As MyListItem.ClickEventHandler, IsSaveOnly As Boolean)
        '如果只有一个版本，则不特别列出
        If Entries.IsSingle Then Return
        '获取最新稳定版和测试版
        Dim FreshStableVersion As DlNeoForgeListEntry = Nothing
        Dim FreshBetaVersion As DlNeoForgeListEntry = Nothing
        If Entries.Any() Then
            For Each Entry In Entries.ToList()
                If Entry.IsBeta Then
                    If FreshBetaVersion Is Nothing Then FreshBetaVersion = Entry
                Else
                    FreshStableVersion = Entry
                    Exit For
                End If
            Next
        Else
            Logger.Warn("未找到可用的 NeoForge 版本")
        End If
        '显示各个版本
        If FreshStableVersion IsNot Nothing Then
            Dim Fresh = NeoForgeDownloadListItem(FreshStableVersion, OnClick, IsSaveOnly).Init()
            Fresh.Info = If(Fresh.Info = "", "最新稳定版", "最新" & Fresh.Info)
            Stack.Children.Add(Fresh)
        End If
        If FreshBetaVersion IsNot Nothing Then
            Dim Fresh = NeoForgeDownloadListItem(FreshBetaVersion, OnClick, IsSaveOnly).Init()
            Fresh.Info = If(Fresh.Info = "", "最新测试版", "最新" & Fresh.Info)
            Stack.Children.Add(Fresh)
        End If
        '添加间隔
        Stack.Children.Add(New TextBlock With {.Text = "全部版本 (" & Entries.Count & ")", .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(6, 13, 0, 4)})
    End Sub
    Public Function NeoForgeDownloadListItem(Info As DlNeoForgeListEntry, OnClick As MyListItem.ClickEventHandler, IsSaveOnly As Boolean) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            '建立控件
            Dim NewItem As New MyListItem With {
                .Title = Info.VersionName, .SnapsToDevicePixels = True, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Info,
                .Info = If(Info.IsBeta, "测试版", "稳定版"),
                .Logo = PathImage & "Blocks/NeoForge.png"
            }
            AddHandler NewItem.Click, OnClick
            '建立菜单
            If IsSaveOnly Then
                NewItem.ContentHandler = AddressOf NeoForgeSaveContMenuBuild
            Else
                NewItem.ContentHandler = AddressOf NeoForgeContMenuBuild
            End If
            Return NewItem
        End Function
        ) With {.Height = 42}
    End Function
    Private Sub NeoForgeContMenuBuild(sender As MyListItem, e As EventArgs)
        Dim BtnSave As New MyIconButton With {.Logo = Logo.IconButtonSave, .ToolTip = "另存为"}
        AddHandler BtnSave.Click, AddressOf NeoForgeSave_Click
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf NeoForgeLog_Click
        sender.Buttons = {BtnSave, BtnInfo}
    End Sub
    Private Sub NeoForgeSaveContMenuBuild(sender As MyListItem, e As EventArgs)
        Dim BtnInfo As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonInfo, .ToolTip = "更新日志"}
        AddHandler BtnInfo.Click, AddressOf NeoForgeLog_Click
        sender.Buttons = {BtnInfo}
    End Sub
    Private Sub NeoForgeLog_Click(sender As Object, e As RoutedEventArgs)
        Dim Info As DlNeoForgeListEntry
        If sender.Tag IsNot Nothing Then
            Info = sender.Tag
        ElseIf sender.Parent.Tag IsNot Nothing Then
            Info = sender.Parent.Tag
        Else
            Info = sender.Parent.Parent.Tag
        End If
        OpenWebsite(Info.UrlBase & "-changelog.txt")
    End Sub
    Public Sub NeoForgeSave_Click(sender As Object, e As RoutedEventArgs)
        Dim Info As DlNeoForgeListEntry
        If sender.Tag IsNot Nothing Then
            Info = sender.Tag
        ElseIf sender.Parent.Tag IsNot Nothing Then
            Info = sender.Parent.Tag
        Else
            Info = sender.Parent.Parent.Tag
        End If
        McDownloadForgelikeSave(Info)
    End Sub

#End Region

#Region "Fabric 下载"

    Public Sub McDownloadFabricLoaderSave(DownloadInfo As JObject)
        Try
            Dim Url As String = DownloadInfo("url").ToString
            Dim FileName As String = PathUtils.GetLastPart(Url)
            Dim Version As String = PathUtils.GetLastPart(DownloadInfo("version").ToString)
            Dim Target As String = Dialogs.SaveFile("选择保存位置", FileName, filter:={("jar", "Fabric 安装器")})
            If Target Is Nothing Then Return

            '重复任务检查
            For Each OngoingLoader In LoaderTaskbar.ToList()
                If OngoingLoader.Name <> $"Fabric {Version} 安装器下载" Then Continue For
                Hint("该版本正在下载中！", HintType.Red)
                Return
            Next

            '构造步骤加载器
            Dim Loaders As New List(Of LoaderBase)
            '下载
            'BMCLAPI 不支持 Fabric Installer 下载
            Dim Address As New List(Of String)
            Address.Add(Url)
            Loaders.Add(New LoaderDownload("下载主文件", New List(Of NetFile) From {New NetFile(Address.ToArray, Target, New FileChecker With {.MinSize = 1024 * 64})}) With {.ProgressWeight = 15})
            '启动
            Dim Loader As New LoaderCombo(Of JObject)("Fabric " & Version & " 安装器下载", Loaders) With {.OnStateChanged = AddressOf LoaderStateChangedHintOnly}
            Loader.Start(DownloadInfo)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()

        Catch ex As Exception
            Logger.Error(ex, "开始 Fabric 安装器下载失败")
        End Try
    End Sub

    ''' <summary>
    ''' 获取下载某个 Fabric 版本的加载器列表。
    ''' 这不会补全所需的支持库文件，需要后续手动补全支持库。
    ''' </summary>
    Private Function McDownloadFabricLoader(FabricVersion As String, MinecraftName As String, McFolder As String) As List(Of LoaderBase)

        '实例名保留原始版本串（含 ∞），请求地址里才把 ∞ 换成 infinite——两者顺序写反不报错，
        '只会让版本文件夹名对不上实例列表，故统一放进 FabricProfile
        Dim Id As String = PCL.Download.FabricProfile.BuildInstanceName(FabricVersion, MinecraftName)
        Dim VersionFolder As String = McFolder & "versions\" & Id & "\"
        Dim Loaders As New List(Of LoaderBase)

        '下载 Json
        Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("获取 Fabric 主文件下载地址",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            Task.Output = New List(Of NetFile) From {New NetFile(
                PCL.Download.FabricProfile.BuildProfileUrls(FabricVersion, MinecraftName),
                VersionFolder & Id & ".json", New FileChecker With {.IsJson = True})} '构造文件请求
        End Sub) With {.ProgressWeight = 0.5})
        Loaders.Add(New LoaderDownload("下载 Fabric 主文件", New List(Of NetFile)) With {.ProgressWeight = 2.5})

        Return Loaders
    End Function

#End Region

#Region "Fabric 下载菜单"

    Public Function FabricDownloadListItem(Entry As JObject, OnClick As MyListItem.ClickEventHandler) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            Dim NewItem As New MyListItem With {
                .Title = Entry("version").ToString.Replace("+build", ""), .SnapsToDevicePixels = True, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Entry,
                .Info = If(Entry("stable").ToObject(Of Boolean), "稳定版", "测试版"),
                .Logo = PathImage & "Blocks/Fabric.png"
            }
            AddHandler NewItem.Click, OnClick
            Return NewItem
        End Function) With {.Height = 42}
    End Function
    Public Function FabricApiDownloadListItem(Entry As ResourceVersion, OnClick As MyListItem.ClickEventHandler) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            Dim NewItem As New MyListItem With {
                .Title = Entry.Display.Split("]")(1).Replace("Fabric API ", "").Replace(" build ", ".").BeforeFirst("+").Trim, .SnapsToDevicePixels = True, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Entry,
                .Info = Entry.ReleaseTypeDisplay & "，发布于 " & Entry.ReleaseDate.ToString("yyyy'/'MM'/'dd HH':'mm"),
                .Logo = PathImage & "Blocks/Fabric.png"
            }
            AddHandler NewItem.Click, OnClick
            Return NewItem
        End Function) With {.Height = 42}
    End Function
    Public Function OptiFabricDownloadListItem(Entry As ResourceVersion, OnClick As MyListItem.ClickEventHandler) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            Dim NewItem As New MyListItem With {
               .Title = Entry.Display.Lower.Replace("optifabric-", "").Replace(".jar", "").Trim.TrimStart("v"), .SnapsToDevicePixels = True, .Height = 42, .Type = MyListItem.CheckType.Clickable, .Tag = Entry,
               .Info = Entry.ReleaseTypeDisplay & "，发布于 " & Entry.ReleaseDate.ToString("yyyy'/'MM'/'dd HH':'mm"),
               .Logo = PathImage & "Blocks/OptiFabric.png"
            }
            AddHandler NewItem.Click, OnClick
            Return NewItem
        End Function) With {.Height = 42}
    End Function

#End Region

#Region "安装"

    ''' <summary>
    ''' 安装请求。
    ''' </summary>
    Public Class McInstallRequest
        ''' <summary>
        ''' 新版本安装必填。安装目标版本名称。
        ''' </summary>
        Public NewInstanceName As String
        ''' <summary>
        ''' 必填。安装目标文件夹。
        ''' </summary>
        Public InstanceFolder As String
        ''' <summary>
        ''' 必填。欲安装的 Minecraft 与组件版本。
        ''' </summary>
        Public Components As McComponents
        ''' <summary>
        ''' 可选。欲下载的 Minecraft Json 地址。
        ''' </summary>
        Public MinecraftJson As String = Nothing
        ''' <summary>
        ''' 欲下载的 Forge。
        ''' </summary>
        Public ForgeEntry As DlForgeVersionEntry = Nothing
        ''' <summary>
        ''' 欲下载的 NeoForge。
        ''' </summary>
        Public NeoForgeEntry As DlNeoForgeListEntry = Nothing
        ''' <summary>
        ''' 欲下载的 Fabric API 信息。
        ''' </summary>
        Public FabricApi As ResourceVersion = Nothing
        ''' <summary>
        ''' 欲下载的 OptiFabric 信息。
        ''' </summary>
        Public OptiFabric As ResourceVersion = Nothing
        ''' <summary>
        ''' 欲下载的 LiteLoader 详细信息。
        ''' </summary>
        Public LiteLoaderEntry As DlLiteLoaderListEntry = Nothing
        ''' <summary>
        ''' 是否为中间产物。
        ''' </summary>
        Public IsIntermediateOutput As Boolean = False
    End Class

    ''' <summary>
    ''' 获取合并安装加载器列表。
    ''' </summary>
    Public Function McInstallLoadersGet(Request As McInstallRequest) As List(Of LoaderBase)
        '获取缓存目录（安装 Mod 加载器的文件夹不能包含空格）
        Dim TempMcFolder As String = RequestTaskTempFolder(Request.Components.OptiFineEntry IsNot Nothing OrElse
            Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.Forge) OrElse Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge))

        '获取各个 Mod 加载器的缓存文件夹
        Dim OptiFineEntry = Request.Components.OptiFineEntry?.ToObject(Of DlOptiFineListEntry)()
        Dim OptiFineFolder = If(OptiFineEntry Is Nothing, Nothing, $"{TempMcFolder}versions\{OptiFineEntry.InstanceName}")
        Dim ForgeVersion = If(Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.Forge), Request.Components.ModLoaderVersions(ModLoaders.Forge), Nothing)
        Dim ForgeFolder = If(ForgeVersion Is Nothing, Nothing, $"{TempMcFolder}versions\forge-{ForgeVersion}")
        Dim NeoForgeVersion = If(Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge), Request.Components.ModLoaderVersions(ModLoaders.NeoForge), Nothing)
        Dim NeoForgeFolder = If(NeoForgeVersion Is Nothing, Nothing, $"{TempMcFolder}versions\neoforge-{NeoForgeVersion}")
        Dim LiteLoaderVersion = If(Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.LiteLoader), Request.Components.ModLoaderVersions(ModLoaders.LiteLoader), Nothing)
        Dim LiteLoaderFolder = If(LiteLoaderVersion Is Nothing, Nothing, $"{TempMcFolder}versions\{Request.Components.MinecraftVersion}-LiteLoader")
        Dim FabricVersion = If(Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.Fabric), Request.Components.ModLoaderVersions(ModLoaders.Fabric), Nothing)
        Dim FabricFolder = If(FabricVersion Is Nothing, Nothing, $"{TempMcFolder}versions\fabric-loader-{FabricVersion}-{Request.Components.MinecraftVersion}")

        '判断 OptiFine 是否作为 Mod 进行下载
        Dim Modable = Request.Components.ModLoaderVersions.Any
        Dim ModsTempFolder As String = $"{TempMcFolder}mods\"
        Dim OptiFineAsMod = OptiFineEntry IsNot Nothing AndAlso Modable '选择了 OptiFine 与任意 Mod 加载器
        If OptiFineAsMod Then
            Logger.Info("OptiFine 将作为 Mod 进行下载")
            If LiteLoaderVersion IsNot Nothing Then
                OptiFineFolder = $"{ModsTempFolder}{Request.Components.MinecraftVersion}\" '#8147
            Else
                OptiFineFolder = ModsTempFolder
            End If
        End If

        '记录日志
        If OptiFineFolder IsNot Nothing Then Logger.Info($"OptiFine 缓存：{OptiFineFolder}")
        If ForgeFolder IsNot Nothing Then Logger.Info($"Forge 缓存：{ForgeFolder}")
        If NeoForgeFolder IsNot Nothing Then Logger.Info($"NeoForge 缓存：{NeoForgeFolder}")
        If FabricFolder IsNot Nothing Then Logger.Info($"Fabric 缓存：{FabricFolder}")
        If LiteLoaderFolder IsNot Nothing Then Logger.Info($"LiteLoader 缓存：{LiteLoaderFolder}")
        Logger.Info($"对应的原版版本：{Request.Components.MinecraftVersion}")

        '重复版本检查
        Dim VersionFolder As String = PathUtils.AddSlashSuffix(Request.InstanceFolder)
        If FileUtils.Exists($"{VersionFolder}{Request.NewInstanceName}.json") Then
            Hint("版本 " & Request.NewInstanceName & " 已经存在！", HintType.Red)
            Throw New OperationCanceledException
        End If

        Dim LoaderList As New List(Of LoaderBase)
        Dim ManagedFileSources As New Dictionary(Of String, String)
        '添加忽略标识
        LoaderList.Add(New LoaderTask(Of Integer, Integer)("添加忽略标识", Sub() FileUtils.Write(VersionFolder & ".pclignore", "用于临时地在 PCL Next 的版本列表中屏蔽此版本。")) With {.Show = False})
        'Fabric API
        If Request.Components.FabricApiVersion IsNot Nothing Then
            Dim DownloadFile = Request.FabricApi.ToNetFile(ModsTempFolder, ResourceVersion.DownloadReason.Dependency, Request.Components.MinecraftVersion, ModLoaders.Fabric)
            ManagedFileSources.Add("FabricApi", DownloadFile.LocalPath)
            LoaderList.Add(New LoaderDownload("下载 Fabric API", New List(Of NetFile) From {DownloadFile}) With {
                .ProgressWeight = 3, .Show = True, .Block = False})
        End If
        'OptiFabric
        If Request.Components.OptiFabricVersion IsNot Nothing Then
            Dim DownloadFile = Request.OptiFabric.ToNetFile(ModsTempFolder, ResourceVersion.DownloadReason.Dependency, Request.Components.MinecraftVersion, ModLoaders.Fabric)
            ManagedFileSources.Add("OptiFabric", DownloadFile.LocalPath)
            LoaderList.Add(New LoaderDownload("下载 OptiFabric", New List(Of NetFile) From {DownloadFile}) With {
                .ProgressWeight = 3, .Show = True, .Block = False})
        End If
        '原版
        Dim ClientLoader = New LoaderCombo(Of String)("下载原版 " & Request.Components.MinecraftVersion,
            McDownloadClientLoader(Request.Components.MinecraftVersion, Request.MinecraftJson, VersionFolder)) With {
            .ProgressWeight = 39, .Show = False, .Block = Not Modable AndAlso OptiFineEntry Is Nothing}
        LoaderList.Add(ClientLoader)
        'OptiFine
        If OptiFineEntry IsNot Nothing Then
            If OptiFineAsMod Then ManagedFileSources.Add("OptiFine", OptiFineFolder & OptiFineEntry.FileName)
            LoaderList.Add(New LoaderCombo(Of String)("下载 OptiFine " & OptiFineEntry.DisplayName,
                If(OptiFineAsMod,
                    McDownloadOptiFineSaveLoader(OptiFineEntry, OptiFineFolder & OptiFineEntry.FileName),
                    McDownloadOptiFineLoader(OptiFineEntry, TempMcFolder, ClientLoader, VersionFolder))) With {
                .ProgressWeight = If(Modable, 16, 24), .Show = False, .Block = Not Modable})
        End If
        'Forge
        If ForgeVersion IsNot Nothing Then
            LoaderList.Add(New LoaderCombo(Of String)("下载 Forge " & ForgeVersion,
                McDownloadForgelikeLoader(False, ForgeVersion, "forge-" & ForgeVersion, Request.Components.MinecraftVersion, Request.ForgeEntry, TempMcFolder, ClientLoader, VersionFolder)) With {
                    .ProgressWeight = 25, .Show = False, .Block = FabricVersion Is Nothing AndAlso Request.LiteLoaderEntry Is Nothing AndAlso Request.NeoForgeEntry Is Nothing})
        End If
        'NeoForge
        If NeoForgeVersion IsNot Nothing Then
            LoaderList.Add(New LoaderCombo(Of String)("下载 NeoForge " & NeoForgeVersion,
                McDownloadForgelikeLoader(True, NeoForgeVersion, "neoforge-" & NeoForgeVersion, Request.Components.MinecraftVersion, Request.NeoForgeEntry, TempMcFolder, ClientLoader, VersionFolder)) With {
                .ProgressWeight = 25, .Show = False, .Block = Request.ForgeEntry Is Nothing AndAlso FabricVersion Is Nothing AndAlso Request.LiteLoaderEntry Is Nothing})
        End If
        'LiteLoader
        If LiteLoaderVersion IsNot Nothing Then
            LoaderList.Add(New LoaderCombo(Of String)("下载 LiteLoader " & Request.Components.MinecraftVersion,
                McDownloadLiteLoaderLoader(Request.LiteLoaderEntry, TempMcFolder)) With {
                    .ProgressWeight = 1, .Show = False, .Block = FabricVersion Is Nothing})
        End If
        'Fabric
        If FabricVersion IsNot Nothing Then
            LoaderList.Add(New LoaderCombo(Of String)("下载 Fabric " & FabricVersion,
                McDownloadFabricLoader(FabricVersion, Request.Components.MinecraftVersion, TempMcFolder)) With {
                .ProgressWeight = 2, .Show = False, .Block = True})
        End If
        '合并安装
        Dim MinecraftFolder = McFolderSelected
        Dim TargetFolder As String = Nothing
        LoaderList.Add(New LoaderTask(Of String, String)("安装游戏",
        Sub(Task As LoaderTask(Of String, String))
            '合并 JSON：将多个版本 JSON 进行合并，如果已存在则直接覆盖
            MergeJson(VersionFolder, OptiFineFolder, OptiFineAsMod, ForgeFolder, NeoForgeFolder, FabricFolder, LiteLoaderFolder)
            Task.Progress = 0.2
            '迁移文件
            DirectoryUtils.Copy(TempMcFolder & "libraries", MinecraftFolder & "libraries")
            Task.Progress = 0.8
            '创建 Mod 和资源包文件夹
            TargetFolder = If(Request.IsIntermediateOutput, VersionFolder, New McInstance(VersionFolder).PathIndie) '版本隔离信息在此时被决定
            Dim ModsFolder = TargetFolder & "mods\"
            If DirectoryUtils.Exists(ModsTempFolder) Then
                DirectoryUtils.Copy(ModsTempFolder, ModsFolder)
            ElseIf Modable Then
                DirectoryUtils.Create(ModsFolder)
                Logger.Info($"自动创建 Mod 文件夹：{ModsFolder}")
            End If
            Dim ResourcepacksFolder = TargetFolder & "resourcepacks\"
            DirectoryUtils.Create(ResourcepacksFolder)
            Logger.Info($"自动创建资源包文件夹：{ResourcepacksFolder}")
        End Sub) With {.ProgressWeight = 2, .Block = True})
        '补全文件
        Dim LoadersLib As New List(Of LoaderBase)
        LoadersLib.Add(New LoaderTask(Of String, List(Of NetFile))("分析游戏支持库文件（副加载器）", Sub(Task) Task.Output = McLibNetFilesFromInstance(New McInstance(VersionFolder))) With {.ProgressWeight = 1, .Show = False})
        LoadersLib.Add(New LoaderDownload("下载游戏支持库文件（副加载器）", New List(Of NetFile)) With {.ProgressWeight = 7, .Show = False})
        LoaderList.Add(New LoaderCombo(Of String)("下载游戏支持库文件", LoadersLib) With {.ProgressWeight = 8})
        '记录安装清单
        LoaderList.Add(New LoaderTask(Of Integer, Integer)("记录安装清单",
        Sub()
            Dim ManagedFiles As New List(Of McInstallManifest.ManagedFile)
            '记录由 PCL 管理的 Mod 信息
            For Each Entry In ManagedFileSources
                Dim RelativePath = Entry.Value.Substring(ModsTempFolder.Length)
                Dim FullPath = PathUtils.ForCompare(Path.Combine(TargetFolder, "mods", RelativePath))
                ManagedFiles.Add(New McInstallManifest.ManagedFile("mods/" & RelativePath.Replace("\", "/").ToLowerInvariant(),
                    CryptographyUtils.ComputeFileHash(FullPath, CryptographyUtils.HashMethod.Sha1), FileUtils.GetInfo(FullPath).LastWriteTimeUtc, Entry.Key))
            Next
            '处理加载器版本信息
            Dim TempLoaderVersions As New Dictionary(Of ModLoaders, String)(Request.Components.ModLoaderVersions)
            If ForgeVersion IsNot Nothing AndAlso ForgeVersion.StartsWithF("1.") AndAlso ForgeVersion.Contains("-") Then
                TempLoaderVersions(ModLoaders.Forge) = ForgeVersion.AfterLast("-")
            End If
            If NeoForgeVersion IsNot Nothing AndAlso NeoForgeVersion.StartsWithF("1.20.1-") Then
                TempLoaderVersions(ModLoaders.NeoForge) = NeoForgeVersion.Replace("1.20.1-", "")
            End If
            '存储 Manifest
            Dim Target = New McComponents(Request.Components.MinecraftVersion, TempLoaderVersions,
                Request.Components.OptiFineEntry, Request.Components.FabricApiVersion, Request.Components.OptiFabricVersion)
            Dim InstanceConfig = New McInstance(VersionFolder).Config
            Configs.McInstallManifest.Set(New McInstallManifest(Versions.McInstallManifestVersion, Target, Date.UtcNow, ManagedFiles), InstanceConfig)
            InstanceConfig.Save()
        End Sub) With {.Show = False})
        '删除忽略标识
        If Not Request.IsIntermediateOutput Then
            LoaderList.Add(New LoaderTask(Of Integer, Integer)("删除忽略标识", Sub() FileUtils.Delete(VersionFolder & ".pclignore")) With {.Show = False})
        End If
        '总加载器
        Return LoaderList
    End Function
    ''' <summary>
    ''' 合并原版与各加载器的 json，输出为一份独立版本 json。
    ''' </summary>
    ''' <param name="VersionFolder">输出目录，同时也是原版 profile 的所在目录。</param>
    ''' <param name="OptiFineFolder">OptiFine 目录。作为 Mod 安装时传 Nothing。</param>
    ''' <param name="OptiFineAsMod">OptiFine 是否以 Mod 形式安装。为真时不参与合并。</param>
    ''' <param name="ForgeFolder">Forge 目录，未安装传 Nothing。</param>
    ''' <param name="NeoForgeFolder">NeoForge 目录，未安装传 Nothing。</param>
    ''' <param name="FabricFolder">Fabric 目录，未安装传 Nothing。</param>
    ''' <param name="LiteLoaderFolder">LiteLoader 目录，未安装传 Nothing。</param>
    ''' <remarks>
    ''' 合并顺序即叠加顺序，决定同名标量（如 mainClass）最终归属。具体合并规则见
    ''' <c>PCL.Download.VersionMerger</c>；此处只负责按安装形态挑选参与合并的目录。
    ''' </remarks>
    Private Sub MergeJson(VersionFolder As String, OptiFineFolder As String, OptiFineAsMod As Boolean,
                          ForgeFolder As String, NeoForgeFolder As String, FabricFolder As String, LiteLoaderFolder As String)
        Dim Sources As New List(Of PCL.Download.VersionMergeSource) From {
            New PCL.Download.VersionMergeSource("Minecraft", VersionFolder)}
        If OptiFineFolder IsNot Nothing AndAlso Not OptiFineAsMod Then
            Sources.Add(New PCL.Download.VersionMergeSource("OptiFine", OptiFineFolder))
        End If
        If ForgeFolder IsNot Nothing Then Sources.Add(New PCL.Download.VersionMergeSource("Forge", ForgeFolder))
        If NeoForgeFolder IsNot Nothing Then Sources.Add(New PCL.Download.VersionMergeSource("NeoForge", NeoForgeFolder))
        If LiteLoaderFolder IsNot Nothing Then
            Sources.Add(New PCL.Download.VersionMergeSource("LiteLoader", LiteLoaderFolder))
        End If
        If FabricFolder IsNot Nothing Then Sources.Add(New PCL.Download.VersionMergeSource("Fabric", FabricFolder))
        PCL.Download.VersionMerger.MergeToDirectory(Sources, VersionFolder)
    End Sub
    ''' <summary>
    ''' 进行合并安装。返回是否已经开始安装。
    ''' </summary>
    Public Function McInstallStart(Request As McInstallRequest) As Boolean
        Try
            Dim Loader As New LoaderCombo(Of String)(Request.NewInstanceName & " 安装", McInstallLoadersGet(Request)) With {.OnStateChanged = AddressOf McInstallState}
            '启动
            Loader.Start(Request.InstanceFolder)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
            Return True
        Catch ex As Exception
            Logger.Error(ex, "开始合并安装失败")
            Return False
        End Try
    End Function
    ''' <summary>
    ''' 进行修改安装。返回是否已经开始安装。
    ''' </summary>
    Public Function McModificationStart(Request As McInstallRequest, Instance As McInstance) As Boolean
        Try
            '检查版本目录下的 Natives，防止修改正在运行的游戏
            Dim TargetFolder = Instance.PathIndie
            Dim SubLoaders As New List(Of LoaderBase)
            SubLoaders.Add(New LoaderTask(Of Integer, Integer)("检查 Natives 文件占用",
            Sub()
                For Each File In DirectoryUtils.EnumerateFiles(GetNativesFolder(Instance), includeSubDirectories:=True, searchPattern:="*.dll")
                    If Not FileUtils.IsAvailable(File) Then Throw New Exception($"Natives 文件正在被占用，请关闭游戏后再次尝试：{File}")
                Next
            End Sub) With {.Show = False})
            '安装修改后的 MC 版本
            Dim BuildFolder = RequestTaskTempFolder() & Instance.Name & "\"
            Dim BuildRequest As New McInstallRequest With {
                .NewInstanceName = Instance.Name,
                .InstanceFolder = BuildFolder,
                .Components = Request.Components,
                .MinecraftJson = Request.MinecraftJson,
                .ForgeEntry = Request.ForgeEntry,
                .NeoForgeEntry = Request.NeoForgeEntry,
                .FabricApi = Request.FabricApi,
                .OptiFabric = Request.OptiFabric,
                .LiteLoaderEntry = Request.LiteLoaderEntry,
                .IsIntermediateOutput = True
            }
            SubLoaders.AddRange(McInstallLoadersGet(BuildRequest))
            '版本合并
            Dim RenameFailed As Boolean = False
            SubLoaders.Add(New LoaderTask(Of Integer, Integer)("修改版本",
            Sub(Task)
                'TODO: 提交前显示文件修改列表（大量文件按“XXX 文件夹下 XXX 项”汇总），可以询问玩家是否覆盖原版本或作为新版本安装（不覆盖）？
                McInstallCommit(Instance, Configs.McInstallManifest.Get(New McInstance(BuildFolder).Config), BuildFolder, TargetFolder)
                '重命名和引用交接
                Dim NewInstance As McInstance = Nothing
                If Request.NewInstanceName Is Nothing Then
                    NewInstance = New McInstance(Instance.PathVersion).Load()
                Else
                    Try
                        NewInstance = Instance.Rename(Request.NewInstanceName)
                    Catch ex As Exception
                        RenameFailed = True
                        Logger.Warn(ex, $"版本修改成功，但重命名为 {Request.NewInstanceName} 失败：{ex.GetDisplay(False)}", LogBehavior.None)
                        NewInstance = New McInstance(Instance.PathVersion).Load()
                    End Try
                End If
                If NewInstance Is Nothing Then NewInstance = New McInstance(Instance.PathVersion).Load()
                RunInUiWait(
                Sub()
                    WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "")
                    '未切换则选中修改后的版本
                    If PageInstanceLeft.Instance IsNot Nothing AndAlso
                       PathUtils.IsSame(PageInstanceLeft.Instance.PathVersion, Instance.PathVersion) Then
                        PageInstanceLeft.Instance = NewInstance
                    End If
                    If McInstanceSelected IsNot Nothing AndAlso
                       PathUtils.IsSame(McInstanceSelected.PathVersion, Instance.PathVersion) Then
                        McInstanceSelected = NewInstance
                    End If
                End Sub)
            End Sub) With {.ProgressWeight = 1, .Block = True})
            '实际修改任务
            Dim Loader As New LoaderCombo(Of String)(Instance.Name & " 修改", SubLoaders) With {.OnStateChanged =
            Sub(CurrentLoader)
                '流程收尾
                Select Case CurrentLoader.State
                    Case LoadState.Finished
                        If Not RenameFailed Then
                            Hint(CurrentLoader.Name & "成功！", HintType.Green)
                        Else
                            Hint(CurrentLoader.Name & "成功，但重命名失败！", HintType.Green)
                        End If
                    Case LoadState.Failed
                        MyMsgBox(CurrentLoader.Error.GetDisplay(True), CurrentLoader.Name & "失败", IsWarn:=True)
                    Case LoadState.Canceled
                        Hint(CurrentLoader.Name & "已取消！", HintType.Blue)
                End Select
            End Sub}
            Dim Worker As New RedoableWorker(Sub() Loader.WaitForExit(Instance.PathVersion))
            '启动
            If McInstanceLock.TryAcquire(Worker, Instance.PathVersion) Then
                AddHandler Worker.Stopped, Sub() RunInUi(Sub() LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\"))
                Worker.Start()
                '加锁后刷新版本列表，隐藏正在修改的实例。
                LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Else
                Logger.Error("版本被其他 Worker 占用，版本修改失败")
                Return False
            End If
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
            Return True
        Catch ex As Exception
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Logger.Error(ex, "开始修改版本失败", LogBehavior.Toast)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 将安装好的临时版本文件夹覆盖到一个现有文件夹中。
    ''' </summary>
    Public Sub McInstallCommit(Instance As McInstance, NewManifest As McInstallManifest, BuildFolder As String, TargetFolder As String)
        Dim OldFiles = If(Configs.McInstallManifest.Get(Instance.Config)?.ManagedFiles, {})
        Dim NewFiles = NewManifest.ManagedFiles

        '============== 整理变更的文件列表 ==============

        Dim FilesToDelete As New List(Of String)
        Dim FilesToMove As New List(Of (Source As String, Target As String))
        'ManagedFiles
        '新版里有：添加或覆盖新版文件
        For Each NewFile In NewFiles
            FilesToMove.Add((Path.Combine(BuildFolder, NewFile.RelativePath), Path.Combine(TargetFolder, NewFile.RelativePath)))
        Next
        '新版里没有，老版里有：删除本地文件到回收站
        Dim NewFilePaths As New HashSet(Of String)(NewFiles.Select(Function(File) File.RelativePath))
        For Each OldFile In OldFiles
            If NewFilePaths.Contains(OldFile.RelativePath) Then Continue For
            FilesToDelete.Add(PathUtils.ForCompare(Path.Combine(TargetFolder, OldFile.RelativePath)))
        Next
        '老版本 MC 的 assets 文件
        If DirectoryUtils.Exists(BuildFolder & "resources\") Then DirectoryUtils.Copy(BuildFolder & "resources\", TargetFolder & "resources\")
        'json 与 jar
        Dim OldJsonPath As String = Instance.GetJsonPath()
        FilesToDelete.Add(OldJsonPath)
        FilesToDelete.Add(Path.ChangeExtension(OldJsonPath, ".jar"))
        FilesToDelete.Add(Instance.PathVersion & Instance.Name & ".jar")
        FilesToDelete = FilesToDelete.Distinct(StringComparer.OrdinalIgnoreCase).ToList()

        '============== 实际修改 ==============

        Instance.Config.Save()
        For Each TargetPath In FilesToDelete
            FileUtils.Delete(TargetPath, True)
        Next
        FileUtils.Move(BuildFolder & Instance.Name & ".jar", Instance.PathVersion & Instance.Name & ".jar")
        FileUtils.Move(BuildFolder & Instance.Name & ".json", OldJsonPath)
        For Each File In FilesToMove
            DirectoryUtils.Create(PathUtils.RemoveLastPart(File.Target))
            FileUtils.Copy(File.Source, File.Target)
        Next
        Dim ManagedFiles = NewFiles.Select(Function(File) New McInstallManifest.ManagedFile(File.RelativePath, File.Sha1,
            FileUtils.GetInfo(Path.Combine(TargetFolder, File.RelativePath)).LastWriteTimeUtc, File.Component)).ToList()
        Configs.McInstallManifest.Set(New McInstallManifest(NewManifest.SchemaVersion, NewManifest.Components, NewManifest.LastModifiedUtc, ManagedFiles), Instance.Config)
        Instance.Config.Save()
    End Sub

    ''' <summary>
    ''' 在加载器状态改变后显示一条提示。
    ''' 不会进行任何其他操作。
    ''' </summary>
    Public Sub LoaderStateChangedHintOnly(Loader As LoaderBase)
        Select Case Loader.State
            Case LoadState.Finished
                Hint(Loader.Name & "成功！", HintType.Green)
            Case LoadState.Failed
                Hint(Loader.Name & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
            Case LoadState.Canceled
                Hint(Loader.Name & "已取消！", HintType.Blue)
        End Select
    End Sub
    ''' <summary>
    ''' 安装加载器状态改变后进行提示和重载文件夹列表的方法。
    ''' </summary>
    Public Sub McInstallState(Loader As LoaderBase)
        Select Case Loader.State
            Case LoadState.Finished
                WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "") '清空缓存（合并安装会先生成文件夹，这会在刷新时误判为可以使用缓存）
                Hint(Loader.Name & "成功！", HintType.Green)
                'TODO: 自动选择安装成功的版本
            Case LoadState.Failed
                MyMsgBox(Loader.Error.GetDisplay(True), Loader.Name & "失败", IsWarn:=True)
            Case LoadState.Canceled
                Hint(Loader.Name & "已取消！", HintType.Blue)
            Case LoadState.Loading
                Return '不重新加载版本列表
        End Select
        McInstallFailedClearFolder(Loader)
        LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
    End Sub
    Public Sub McInstallFailedClearFolder(Loader)
        If Loader.State <> LoadState.Failed AndAlso Loader.State <> LoadState.Canceled Then Return
        RunInThread(
        Sub()
            Try
                Thread.Sleep(1000) '防止存在尚未完全释放的文件，导致清理失败（例如整合包安装）
                '删除版本文件夹
                If DirectoryUtils.Exists(Loader.Input & "saves\") OrElse DirectoryUtils.Exists(Loader.Input & "versions\") Then
                    Logger.Warn($"由于版本已被独立启动，不清理版本文件夹：{Loader.Input}")
                Else
                    Logger.Warn($"由于下载失败或取消，清理版本文件夹：{Loader.Input}")
                    DirectoryUtils.Delete(Loader.Input)
                End If
            Catch ex As Exception
                Logger.Warn(ex, "下载失败或取消后清理版本文件夹失败")
            End Try
        End Sub)
    End Sub

#End Region

    ''' <summary>
    ''' 如果 OptiFine 与 Forge 同时开始安装，就会导致 Forge 安装失败。
    ''' </summary>
    Private InstallSyncLock As New Object
    ''' <summary>
    ''' 如果 OptiFine 与 Forge 同时复制原版 JAR，就会导致复制文件时冲突。
    ''' </summary>
    Private VanillaSyncLock As New Object

End Module
