using System;
using System.Net.Http;
using System.Threading.Tasks;
using CacheCow.Common;
using MeloongCore;

namespace PCL
{

    /// <summary>
/// 给 CacheCow 的缓存存储加两层保护，避免缓存问题直接变成请求失败。
/// <para>
/// 1. 写入前去掉 Transfer-Encoding 头：CacheCow 保存的是已解块（de-chunked）的实体，
///    但响应头里仍带着 chunked，读回时解析器会按分块格式去解析正文，
///    抛 <c>InvalidOperationException: Error parsing HTTP message header byte N</c>，
///    使本来命中缓存的请求整体失败（表现为 BMCLAPI 兜底源等加载器 Failed）。
/// </para>
/// <para>
/// 2. 读取缓存抛异常时，删除该条目并返回 Nothing（视为未命中），让请求改走网络重取。
/// </para>
/// </summary>
    internal sealed class ResilientCacheStore : ICacheStore, IDisposable
    {

        private readonly ICacheStore Inner;

        public ResilientCacheStore(ICacheStore Store)
        {
            if (Store is null)
                throw new ArgumentNullException(nameof(Store));
            Inner = Store;
        }

        public Task<HttpResponseMessage> GetValueAsync(CacheKey Key)
        {
            return GetValueCoreAsync(Key);
        }

        private async Task<HttpResponseMessage> GetValueCoreAsync(CacheKey Key)
        {
            Exception FailReason = null;
            try
            {
                return await Inner.GetValueAsync(Key).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FailReason = ex; // VB 不允许在 Catch 里 Await，先记下来，出去再处理
            }
            // 缓存文件损坏、格式与当前 CacheCow 版本不兼容等：清掉这条，当作未命中
            try
            {
                await Inner.TryRemoveAsync(Key).ConfigureAwait(false);
            }
            catch
            {
                // 删除失败也不影响主流程，下次还会再试
            }
            try
            {
                Logger.Warn(FailReason, $"丢弃无法解析的 HTTP 缓存（{Key}）");
            }
            catch
            {
                // 日志系统尚未就绪时忽略
            }
            return null;
        }

        public Task AddOrUpdateAsync(CacheKey Key, HttpResponseMessage Response)
        {
            if (Response is not null && Response.Headers.TransferEncodingChunked.GetValueOrDefault())
            {
                // 正文已经被 HttpClient 解块，头里再留 chunked 会让读回时解析失败
                Response.Headers.TransferEncodingChunked = false;
            }
            return Inner.AddOrUpdateAsync(Key, Response);
        }

        public Task<bool> TryRemoveAsync(CacheKey Key)
        {
            return Inner.TryRemoveAsync(Key);
        }

        public Task ClearAsync()
        {
            return Inner.ClearAsync();
        }

        public void Dispose()
        {
            IDisposable DisposableInner = Inner as IDisposable;
            DisposableInner?.Dispose();
        }

    }
}