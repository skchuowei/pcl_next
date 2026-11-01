using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 资源站请求失败的分类。让上层（Provider / UI）能区分「网络不通」「限流」「接口不存在」，
/// 而不是像 VB 那样只有一条 <see cref="AggregateException"/>。
/// </summary>
public enum ResourceRequestErrorKind
{
    /// <summary>网络层失败（DNS / 连接被拒 / TLS / 连接中断）。</summary>
    Network,
    /// <summary>超时（连接或响应超时）。</summary>
    Timeout,
    /// <summary>404：接口或工程不存在。</summary>
    NotFound,
    /// <summary>401：密钥缺失或无效。</summary>
    Unauthorized,
    /// <summary>403：无权限（含 CurseForge 密钥无效）。</summary>
    Forbidden,
    /// <summary>429：被限流，应退避后重试或降级。</summary>
    RateLimited,
    /// <summary>其他 4xx：请求本身有问题，换源无意义。</summary>
    ClientError,
    /// <summary>5xx：服务端错误，可换源或稍后重试。</summary>
    ServerError,
    /// <summary>响应不是合法 JSON。</summary>
    Parse,
}

/// <summary>
/// 资源站请求失败。<see cref="Kind"/> 用于分类处理；<see cref="IsTransient"/> 表示换源/退避是否有意义。
/// </summary>
public sealed class ResourceRequestException : Exception
{
    public ResourceRequestException(ResourceRequestErrorKind kind, string url, string message,
        int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Url = url;
        StatusCode = statusCode;
    }

    /// <summary>失败分类。</summary>
    public ResourceRequestErrorKind Kind { get; }

    /// <summary>原始（官方）请求地址。</summary>
    public string Url { get; }

    /// <summary>HTTP 状态码；网络/解析类失败为 null。</summary>
    public int? StatusCode { get; }

    /// <summary>换源或退避是否有意义（限流、5xx、网络、超时）。</summary>
    public bool IsTransient => Kind is ResourceRequestErrorKind.Network or ResourceRequestErrorKind.Timeout
        or ResourceRequestErrorKind.RateLimited or ResourceRequestErrorKind.ServerError;

    public override string ToString() => $"{Kind}（{StatusCode?.ToString() ?? "无状态码"}）{Url}：{Message}";
}

/// <summary>
/// <see cref="HttpResourceApi"/> 的行为选项。
/// </summary>
public sealed class HttpResourceApiOptions
{
    /// <summary>源偏好（对应 VB 设置项 <c>ToolDownloadMod</c>）。</summary>
    public ModSourcePreference Preference { get; init; } = ModSourcePreference.Automatic;

    /// <summary>
    /// 收到 429 后，切换到下一个候选前的冷却时长（毫秒）。默认 5000，与下载引擎
    /// <see cref="DownloadOptions.RateLimitCooldownMilliseconds"/> 同一取值。
    /// </summary>
    public int RateLimitCooldownMilliseconds { get; init; } = 5000;

    /// <summary>是否优先按响应头 <c>Retry-After</c> 决定冷却（上限仍受 <see cref="RateLimitCooldownMilliseconds"/> 约束）。</summary>
    public bool HonorRetryAfter { get; init; } = true;

    /// <summary>请求间的限频冷却实现；测试可注入以跳过真实等待。</summary>
    public Action<int>? Cooldown { get; init; }
}

/// <summary>
/// 基于 <see cref="HttpClient"/> 的资源站请求器（<see cref="IResourceApi"/> 的纯 BCL 实现）。
/// <para/>
/// 行为对齐 VB <c>ModDownload.DlModRequest</c>：按 <see cref="ResourceSourceResolver"/> 给出的候选顺序逐个尝试，
/// 每个候选使用自己的超时；差异在于**错误分类**——4xx（429 除外）视为不可恢复，立即抛出而不再换源，
/// 429 与 5xx 则按冷却退避后换下一个候选。
/// </summary>
public sealed class HttpResourceApi : IResourceApi, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly HttpResourceApiOptions _options;

    /// <summary>
    /// 构造请求器。
    /// </summary>
    /// <param name="signing">签名/请求头选项；null 时取 <see cref="ResourceSigningOptions.Resolve"/>。</param>
    /// <param name="options">行为选项。</param>
    /// <param name="handler">自定义传输层（测试注入）；null 时使用内部创建的 <see cref="SocketsHttpHandler"/>。</param>
    public HttpResourceApi(ResourceSigningOptions? signing = null, HttpResourceApiOptions? options = null,
        HttpMessageHandler? handler = null)
    {
        _options = options ?? new HttpResourceApiOptions();
        var inner = handler ?? CreateHandler();
        _client = new HttpClient(new RequestSigningHandler(signing) { InnerHandler = inner }, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan, // 超时由每个候选自己的 CTS 控制
        };
        _ownsClient = true;
    }

    /// <summary>把自己已经配置好（含签名处理）的 <see cref="HttpClient"/> 交给本类使用；本类不负责释放它。</summary>
    public HttpResourceApi(HttpClient client, HttpResourceApiOptions? options = null)
    {
        _client = client;
        _options = options ?? new HttpResourceApiOptions();
        _ownsClient = false;
    }

    /// <inheritdoc />
    public JsonNode Request(string url, HttpMethod? method = null, string? content = null, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var candidates = ResourceSourceResolver.Resolve(url, _options.Preference);
        var errors = new List<ResourceRequestException>(candidates.Count);

        foreach (var candidate in candidates)
        {
            try
            {
                var result = SendOnce(candidate, method, content, contentType);
                if (errors.Count > 0)
                    DownloadLog.Info($"资源站请求已切换候选源成功：{candidate.Url}（先前失败 {errors.Count} 次）");
                return result;
            }
            catch (ResourceRequestException ex)
            {
                errors.Add(ex);
                DownloadLog.Warn($"资源站请求失败（{ex.Kind}，{candidate.TimeoutSeconds}s 超时，{(candidate.IsMirror ? "镜像" : "官方")}源 {candidate.Url}）：{ex.Message}");
                if (!ex.IsTransient) break; // 4xx：换源无意义，直接结束
                if (ex.Kind == ResourceRequestErrorKind.RateLimited) ApplyCooldown(candidate);
            }
        }

        var last = errors.Count > 0
            ? errors[^1]
            : new ResourceRequestException(ResourceRequestErrorKind.Network, url, "没有可用的候选源");
        throw new ResourceRequestException(last.Kind, url,
            $"所有候选源均请求失败（尝试 {errors.Count} 次），最后一次：{last.Message}",
            last.StatusCode, new AggregateException(errors));
    }

    /// <summary>
    /// 分类一个 HTTP 状态码；供测试与上层复用。
    /// </summary>
    public static ResourceRequestErrorKind Classify(HttpStatusCode statusCode) => (int)statusCode switch
    {
        404 => ResourceRequestErrorKind.NotFound,
        401 => ResourceRequestErrorKind.Unauthorized,
        403 => ResourceRequestErrorKind.Forbidden,
        429 => ResourceRequestErrorKind.RateLimited,
        >= 400 and < 500 => ResourceRequestErrorKind.ClientError,
        >= 500 and < 600 => ResourceRequestErrorKind.ServerError,
        _ => ResourceRequestErrorKind.ClientError,
    };

    private JsonNode SendOnce(ResourceSourceCandidate candidate, HttpMethod? method, string? content, string? contentType)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, candidate.TimeoutSeconds));
        using var cts = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, candidate.Url);
        if (content is not null)
            request.Content = new StringContent(content, Encoding.UTF8, contentType ?? "application/json");

        HttpResponseMessage? response = null;
        try
        {
            response = _client.Send(request, HttpCompletionOption.ResponseContentRead, cts.Token);
            var body = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var kind = Classify(response.StatusCode);
                throw new ResourceRequestException(kind, candidate.Url,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", (int)response.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(body))
                throw new ResourceRequestException(ResourceRequestErrorKind.Parse, candidate.Url, "响应内容为空");

            try
            {
                return JsonNode.Parse(body)
                       ?? throw new ResourceRequestException(ResourceRequestErrorKind.Parse, candidate.Url, "响应 JSON 为 null");
            }
            catch (JsonException ex)
            {
                throw new ResourceRequestException(ResourceRequestErrorKind.Parse, candidate.Url,
                    $"响应不是合法 JSON：{ex.Message}", null, ex);
            }
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new ResourceRequestException(ResourceRequestErrorKind.Timeout, candidate.Url,
                $"请求超时（{candidate.TimeoutSeconds}s）", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ResourceRequestException(ResourceRequestErrorKind.Network, candidate.Url, ex.Message, null, ex);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private void ApplyCooldown(ResourceSourceCandidate candidate)
    {
        var delay = _options.RateLimitCooldownMilliseconds;
        if (delay <= 0) return;
        DownloadLog.Warn($"资源站限流（429），冷却 {delay}ms 后换源重试");
        if (_options.Cooldown is not null) _options.Cooldown(delay);
        else Thread.Sleep(delay);
    }

    private static SocketsHttpHandler CreateHandler() => new()
    {
        MaxConnectionsPerServer = 64,
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(3),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    };

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
