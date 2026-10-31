Imports System.Threading
Imports PCL.Download

''' <summary>
''' VB 下载编排层与 C# 下载引擎（PCL.Download）的桥接。
''' <para/>接管策略：保留 VB 侧的 NetFile/LoaderDownload/NetManager 编排与 UI 统计，
''' 仅把"字节传输"部分（原 NetThread 的 HTTP 分片下载）交给 C# 引擎：
''' NetFile.TryBeginThread 在文件的首线程时机 CAS 抢占后调用 <see cref="RunCSharpDownload"/>，
''' 引擎内部自行完成多源 failover、Range 分片、合并与校验，
''' 完成后回写 VB 侧状态（DownloadDone / FileSize / State / Finish / Fail）。
''' </summary>
Public Module NetInterop

    ''' <summary>
    ''' 是否启用 C# 下载引擎执行文件传输。False 时回退到 VB NetThread 实现。
    ''' </summary>
    Public IsCSharpDownloadEnabled As Boolean = True

    ''' <summary>
    ''' 用 C# 引擎下载单个文件（由 NetFile.TryBeginThread 抢占标记后调用）。
    ''' </summary>
    Public Sub RunCSharpDownload(File As NetFile)
        Try
            '---------- 构造请求 ----------
            Dim Sources As New List(Of String)
            For Each Source In File.Sources
                Sources.Add(Source.Url) 'URL 已在 NetFile 构造时经 SecretCdnSign 签名
            Next
            Dim Check As FileCheckRule = Nothing
            If File.Check IsNot Nothing Then
                Check = New FileCheckRule With {
                    .ActualSize = File.Check.ActualSize,
                    .MinSize = File.Check.MinSize,
                    .Hash = File.Check.Hash,
                    .IsJson = File.Check.IsJson}
            End If
            '按 URL 生成请求头：完全复用 SecretHeadersSign 的既有规则
            '（User-Agent / pcl2 Referer / CurseForge Key），并捕获文件的浏览器模拟开关
            Dim Simulate As Boolean = File.SimulateBrowserHeaders
            Dim HeaderProvider As Func(Of String, IEnumerable(Of KeyValuePair(Of String, String))) =
            Function(Url As String)
                Dim Req As New HttpRequestMessage(HttpMethod.Get, Url)
                SecretHeadersSign(Url, Req, Simulate)
                Return Req.Headers.SelectMany(
                    Function(h) h.Value.Select(Function(v) New KeyValuePair(Of String, String)(h.Key, v))).ToList()
            End Function
            Dim Request As New DownloadFileRequest With {
                .Sources = Sources,
                .LocalPath = File.LocalPath,
                .Check = Check,
                .SimulateBrowserHeaders = File.SimulateBrowserHeaders,
                .HeaderProvider = HeaderProvider}

            '---------- 构造选项 ----------
            Dim Options As New DownloadOptions With {
                .MaxConcurrentChunks = NetTaskThreadLimit,
                .SpeedLimitHigh = NetTaskSpeedLimitHigh,
                .Log = Sub(m) Logger.Info(m),
                .LogWarn = Sub(ex, m) Logger.Warn(ex, m)}

            '---------- 状态镜像 ----------
            Dim Cts As New CancellationTokenSource
            File.CSharpCts = Cts
            SyncLock File.LockState
                If File.State < NetState.Connecting Then File.State = NetState.Connecting
            End SyncLock
            Dim LastDone As Long = 0
            Dim SizeMirrored As Boolean = False
            Dim ProgressCallback As New Progress(Of DownloadProgress)(
            Sub(p)
                Try
                    '用户已通过 VB 侧取消/失败（State 被置为 Canceled）或已完成 → 中断引擎
                    If File.State >= NetState.Finished Then
                        Cts.Cancel()
                        Return
                    End If
                    '镜像文件大小（供 File.Progress 计算百分比）
                    If Not SizeMirrored AndAlso p.TotalBytesExpected >= 0 Then
                        Interlocked.Exchange(File.FileSize, p.TotalBytesExpected)
                        SizeMirrored = True
                    End If
                    '镜像字节计数
                    Dim Delta As Long = p.TotalBytesDone - LastDone
                    If Delta > 0 Then
                        LastDone = p.TotalBytesDone
                        Interlocked.Add(File.DownloadDone, Delta)
                        Interlocked.Add(NetManager.DownloadDone, Delta)
                    End If
                    '镜像状态：有活动分块或已有数据 → Downloading
                    If p.ActiveChunks > 0 OrElse p.TotalBytesDone > 0 Then
                        SyncLock File.LockState
                            If File.State >= NetState.Reading AndAlso File.State < NetState.Merging Then
                                File.State = NetState.Downloading
                            End If
                        End SyncLock
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "C# 下载引擎进度镜像出错")
                End Try
            End Sub)

            '---------- 执行 ----------
            Dim Engine As New DownloadEngine()
            Task.Run(
            Async Function()
                Try
                    Dim Report = Await Engine.DownloadAsync({Request}, Options, ProgressCallback, Cts.Token)
                    '引擎的本地复用结果也计入完成
                    Dim Reused As DownloadFileReport = Report.Files.FirstOrDefault()
                    If Reused IsNot Nothing AndAlso Reused.IsLocalReuse AndAlso File.FileSize < 0 Then
                        Interlocked.Exchange(File.FileSize, -1) '复用场景引擎未下载，大小留给 VB 校验逻辑
                    End If
                    Dim BytesLog As Long = If(Reused IsNot Nothing, Reused.BytesDownloaded, 0)
                    File.Finish()
                    Logger.Info($"{File.LocalName}：C# 引擎下载完成（{StringUtils.FormatByteSize(BytesLog)}）")
                Catch cancelEx As OperationCanceledException When File.State >= NetState.Finished
                    'VB 侧主动取消/失败导致的中断，状态已由 VB 管理
                Catch ex As Exception
                    '把错误分发到各下载源，供 LoaderDownload.OnFileFail 汇总展示
                    Dim SourceErrors As IReadOnlyList(Of Exception) = {ex}
                    Dim Failed As DownloadFailedException = TryCast(ex, DownloadFailedException)
                    If Failed IsNot Nothing Then
                        For Each kvp In Failed.ErrorsByFile
                            SourceErrors = kvp.Value
                            Exit For '单文件任务
                        Next
                    End If
                    For i = 0 To File.Sources.Count - 1
                        If i < SourceErrors.Count Then
                            File.Sources(i).Ex = SourceErrors(i)
                            File.Sources(i).IsFailed = True
                        End If
                    Next
                    File.Fail(ex)
                End Try
            End Function)
        Catch ex As Exception
            '接管失败：撤销抢占标记并回退 VB 实现
            Interlocked.Exchange(File.CSharpDownloadStarted, 0)
            Logger.Warn(ex, $"C# 下载引擎启动失败，回退 VB 实现（{File.LocalName}）")
        End Try
    End Sub

End Module
