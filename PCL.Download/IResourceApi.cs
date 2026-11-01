using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 资源站 API 请求抽象（VB 侧对应 ModDownload.DlModRequest）。
/// 镜像降级、超时、重试与配额策略由宿主实现负责；本库只约定「请求并返回已解析的 JSON」。
/// 请求失败时应抛出异常（VB 的 DlModRequest 抛 AggregateException）。
/// </summary>
public interface IResourceApi
{
    /// <summary>
    /// 请求资源站 API 并返回解析后的 JSON 根节点。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <param name="method">HTTP 方法；null 表示 GET。</param>
    /// <param name="content">请求体（POST 时使用）。</param>
    /// <param name="contentType">请求体类型，例如 application/json。</param>
    JsonNode Request(string url, HttpMethod? method = null, string? content = null, string? contentType = null);
}

/// <summary>
/// <see cref="IResourceApi"/> 的全局默认实例持有者。
/// </summary>
public static class ResourceApi
{
    /// <summary>
    /// 全局默认请求器。联网方法未显式传入 <see cref="IResourceApi"/> 时使用它；宿主启动时注入。
    /// 未注入时联网方法抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static IResourceApi? Default { get; set; }

    /// <summary>解析出实际应使用的请求器：显式传入优先，其次全局默认，均无则抛出。</summary>
    internal static IResourceApi Resolve(IResourceApi? api)
        => api ?? Default ?? throw new InvalidOperationException("未注入 IResourceApi，请先设置 ResourceApi.Default 或传入 api 参数。");
}
