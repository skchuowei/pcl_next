using System.Net.Http;

namespace PCL.Download;

/// <summary>
/// 资源站请求的签名与请求头选项（对齐 VB <c>ModSecret.SecretHeadersSign</c>）。
/// <para/>
/// 密钥只允许来自环境变量：<see cref="CurseForgeApiKey"/> 由宿主经
/// <see cref="FromEnvironment"/> 注入，**禁止在任何源码或配置文件里硬编码**。
/// 该对象不落盘、不参与日志输出（日志只输出「已配置 / 未配置」）。
/// </summary>
public sealed class ResourceSigningOptions
{
    /// <summary>CurseForge API Key 的环境变量名（与 VB <c>ModSecret</c> 现行做法一致）。</summary>
    public const string CurseForgeKeyEnvironmentVariable = "PCL_CURSEFORGE_API_KEY";

    /// <summary>
    /// 全局默认选项。宿主（VB 侧 / 桥接层）启动时注入；未注入时 <see cref="Download.ResourceSigningOptions"/> 的使用方
    /// 回退到 <see cref="FromEnvironment"/> 的结果（不缓存环境变量之外的东西）。
    /// </summary>
    public static ResourceSigningOptions? Default { get; set; }

    /// <summary>User-Agent。默认值与 VB 的构造格式一致（<c>PCL2/&lt;版本&gt;.&lt;渠道&gt;</c>），宿主可覆盖。</summary>
    public string UserAgent { get; set; } = "PCL2/pcl-next";

    /// <summary>Referer；为空表示不发送（VB 发送 <c>http://{VersionCode}.open.pcl2.server/</c>）。</summary>
    public string? Referer { get; set; }

    /// <summary>CurseForge API Key。为空表示未配置，CF 源应整体降级。</summary>
    public string? CurseForgeApiKey { get; set; }

    /// <summary>是否已配置 CurseForge API Key（去空白后非空）。</summary>
    public bool HasCurseForgeApiKey => !string.IsNullOrWhiteSpace(CurseForgeApiKey);

    /// <summary>从环境变量读取 CurseForge API Key；未配置时返回 null。</summary>
    public static string? ReadCurseForgeKeyFromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable(CurseForgeKeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    /// <summary>按环境变量构造选项（UA / Referer 用默认值）。</summary>
    public static ResourceSigningOptions FromEnvironment() => new()
    {
        CurseForgeApiKey = ReadCurseForgeKeyFromEnvironment(),
    };

    /// <summary>取实际生效的选项：显式注入优先，其次 <see cref="Default"/>，都没有则读环境变量。</summary>
    public static ResourceSigningOptions Resolve(ResourceSigningOptions? options)
        => options ?? Default ?? FromEnvironment();
}

/// <summary>
/// 资源站请求的签名/请求头注入器（DelegatingHandler 形态，可挂到任意 <see cref="HttpClient"/> 上）。
/// </summary>
public sealed class RequestSigningHandler : DelegatingHandler
{
    private readonly ResourceSigningOptions _options;

    /// <summary>使用指定选项构造；null 时取 <see cref="ResourceSigningOptions.Resolve"/> 的结果。</summary>
    public RequestSigningHandler(ResourceSigningOptions? options = null)
        => _options = ResourceSigningOptions.Resolve(options);

    /// <summary>实际生效的选项（只读观测用）。</summary>
    public ResourceSigningOptions Options => _options;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ResourceSigning.Apply(request, _options);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// 同步发送入口。<see cref="HttpResourceApi"/> 走的是同步 <c>HttpClient.Send</c>，
    /// 而 <see cref="DelegatingHandler"/> 的同步路径**不会**经过 <see cref="SendAsync"/>，
    /// 只重写异步入口会导致 UA / 密钥头全部丢失（安全相关，必须同时实现）。
    /// </summary>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ResourceSigning.Apply(request, _options);
        return base.Send(request, cancellationToken);
    }
}

/// <summary>
/// 资源站请求头的实际注入逻辑（平移自 VB <c>ModSecret.SecretHeadersSign</c> / <c>SecretCdnSign</c>）。
/// </summary>
public static class ResourceSigning
{
    /// <summary>
    /// 为请求补齐 UA、Referer 与 CurseForge 密钥头。已存在的头不会被覆盖（与 VB 的 <c>If Not ... Any</c> 一致）。
    /// </summary>
    public static void Apply(HttpRequestMessage request, ResourceSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!request.Headers.UserAgent.Any() && !string.IsNullOrWhiteSpace(options.UserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);

        if (!string.IsNullOrWhiteSpace(options.Referer) && request.Headers.Referrer is null)
            request.Headers.TryAddWithoutValidation("Referer", options.Referer);

        var url = request.RequestUri?.ToString() ?? "";
        if (options.HasCurseForgeApiKey && IsCurseForgeRequest(url) && !request.Headers.Contains("x-api-key"))
            request.Headers.TryAddWithoutValidation("x-api-key", options.CurseForgeApiKey);
    }

    /// <summary>
    /// 是否属于需要携带 CurseForge 密钥的请求（VB：URL 含 <c>api.curseforge.com</c> 或 <c>forgecdn.net</c>）。
    /// <para/>注意：MCIM 镜像地址（<c>mod.mcimirror.top/curseforge</c>）不匹配，因此不会把本地 Key 泄漏给镜像。
    /// </summary>
    public static bool IsCurseForgeRequest(string url)
        => url.Contains("api.curseforge.com", StringComparison.OrdinalIgnoreCase)
           || url.Contains("forgecdn.net", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 处理带 <c>{CDN}</c> 标记的下载地址（平移自 VB <c>SecretCdnSign</c>）：
    /// 去掉标记与空格，空格按 URL 规则转义。
    /// </summary>
    public static string SignCdnUrl(string url)
    {
        if (!url.EndsWith("{CDN}", StringComparison.Ordinal)) return url;
        return url.Replace("{CDN}", "", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);
    }
}
