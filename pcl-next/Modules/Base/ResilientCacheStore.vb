Imports CacheCow.Common
Imports System.Net.Http
Imports System.Threading.Tasks

''' <summary>
''' 给 CacheCow 的缓存存储加两层保护，避免缓存问题直接变成请求失败。
''' <para>
''' 1. 写入前去掉 Transfer-Encoding 头：CacheCow 保存的是已解块（de-chunked）的实体，
'''    但响应头里仍带着 chunked，读回时解析器会按分块格式去解析正文，
'''    抛 <c>InvalidOperationException: Error parsing HTTP message header byte N</c>，
'''    使本来命中缓存的请求整体失败（表现为 BMCLAPI 兜底源等加载器 Failed）。
''' </para>
''' <para>
''' 2. 读取缓存抛异常时，删除该条目并返回 Nothing（视为未命中），让请求改走网络重取。
''' </para>
''' </summary>
Friend NotInheritable Class ResilientCacheStore
    Implements ICacheStore
    Implements IDisposable

    Private ReadOnly Inner As ICacheStore

    Public Sub New(Store As ICacheStore)
        If Store Is Nothing Then Throw New ArgumentNullException(NameOf(Store))
        Inner = Store
    End Sub

    Public Function GetValueAsync(Key As CacheKey) As Task(Of HttpResponseMessage) Implements ICacheStore.GetValueAsync
        Return GetValueCoreAsync(Key)
    End Function

    Private Async Function GetValueCoreAsync(Key As CacheKey) As Task(Of HttpResponseMessage)
        Dim FailReason As Exception = Nothing
        Try
            Return Await Inner.GetValueAsync(Key).ConfigureAwait(False)
        Catch ex As Exception
            FailReason = ex 'VB 不允许在 Catch 里 Await，先记下来，出去再处理
        End Try
        '缓存文件损坏、格式与当前 CacheCow 版本不兼容等：清掉这条，当作未命中
        Try
            Await Inner.TryRemoveAsync(Key).ConfigureAwait(False)
        Catch
            '删除失败也不影响主流程，下次还会再试
        End Try
        Try
            Logger.Warn(FailReason, $"丢弃无法解析的 HTTP 缓存（{Key}）")
        Catch
            '日志系统尚未就绪时忽略
        End Try
        Return Nothing
    End Function

    Public Function AddOrUpdateAsync(Key As CacheKey, Response As HttpResponseMessage) As Task Implements ICacheStore.AddOrUpdateAsync
        If Response IsNot Nothing AndAlso Response.Headers.TransferEncodingChunked.GetValueOrDefault() Then
            '正文已经被 HttpClient 解块，头里再留 chunked 会让读回时解析失败
            Response.Headers.TransferEncodingChunked = False
        End If
        Return Inner.AddOrUpdateAsync(Key, Response)
    End Function

    Public Function TryRemoveAsync(Key As CacheKey) As Task(Of Boolean) Implements ICacheStore.TryRemoveAsync
        Return Inner.TryRemoveAsync(Key)
    End Function

    Public Function ClearAsync() As Task Implements ICacheStore.ClearAsync
        Return Inner.ClearAsync()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim DisposableInner = TryCast(Inner, IDisposable)
        DisposableInner?.Dispose()
    End Sub

End Class
