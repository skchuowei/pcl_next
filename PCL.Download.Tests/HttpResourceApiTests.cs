using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// <see cref="HttpResourceApi"/> 测试（dev.4 §2.1 A2/A3）：候选源轮换、限流退避、错误分类、签名头。
/// <para/>用桩传输层代替真实网络，避免测试依赖外网。
/// </summary>
public class HttpResourceApiTests
{
    private const string CurseForgeUrl = "https://api.curseforge.com/v1/mods/238222/files";
    private const string MirrorHost = "mod.mcimirror.top";

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResourceApiOptions FastOptions(Action<int>? cooldown = null) => new()
    {
        Preference = ModSourcePreference.Automatic,
        RateLimitCooldownMilliseconds = 5000,
        Cooldown = cooldown ?? (_ => { }),
    };

    [Fact]
    public void RateLimited429_CoolsDownThenFallsBackToMirror()
    {
        var cooldowns = new List<int>();
        var handler = new StubHandler(request => request.RequestUri!.Host.Contains("curseforge")
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Json("{\"data\": [], \"pagination\": {\"resultCount\": 0, \"totalCount\": 0}}"));

        using var api = new HttpResourceApi(new ResourceSigningOptions(), FastOptions(cooldowns.Add), handler);
        var result = api.Request(CurseForgeUrl);

        Assert.NotNull(result["data"]);
        Assert.Equal(2, handler.Calls.Count);                       // 官方 429 → 镜像
        Assert.Contains(MirrorHost, handler.Calls[1].Url);
        Assert.Equal([5000], cooldowns);                            // 429 必须触发冷却，而不是立刻重打
    }

    [Fact]
    public void NotFound_DoesNotWasteOtherCandidates()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var api = new HttpResourceApi(new ResourceSigningOptions(), FastOptions(), handler);

        var ex = Assert.Throws<ResourceRequestException>(() => api.Request(CurseForgeUrl));

        Assert.Equal(ResourceRequestErrorKind.NotFound, ex.Kind);
        Assert.False(ex.IsTransient);
        Assert.Single(handler.Calls);                               // 4xx 换源无意义，只打一次
    }

    [Fact]
    public void ServerError_TriesNextCandidateAndSucceeds()
    {
        var handler = new StubHandler(request => request.RequestUri!.Host.Contains("curseforge")
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Json("{\"data\": [], \"pagination\": {\"resultCount\": 0, \"totalCount\": 0}}"));

        using var api = new HttpResourceApi(new ResourceSigningOptions(), FastOptions(), handler);
        var result = api.Request(CurseForgeUrl);

        Assert.NotNull(result);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Contains(MirrorHost, handler.Calls[1].Url);
    }

    [Fact]
    public void AllCandidatesFail_ThrowsWithLastKindAndAggregate()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var api = new HttpResourceApi(new ResourceSigningOptions(), FastOptions(), handler);

        var ex = Assert.Throws<ResourceRequestException>(() => api.Request(CurseForgeUrl));

        Assert.Equal(ResourceRequestErrorKind.ServerError, ex.Kind);
        Assert.True(ex.IsTransient);
        Assert.Equal(4, handler.Calls.Count);                       // Automatic 分支的四个候选全部尝试
        Assert.IsType<AggregateException>(ex.InnerException);
    }

    [Fact]
    public void InvalidJson_ReportsParseError()
    {
        var handler = new StubHandler(_ => Json("<html>gateway error</html>"));
        using var api = new HttpResourceApi(new ResourceSigningOptions(), FastOptions(), handler);

        var ex = Assert.Throws<ResourceRequestException>(() => api.Request(CurseForgeUrl));

        Assert.Equal(ResourceRequestErrorKind.Parse, ex.Kind);
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void CurseForgeKey_IsSentToOfficialButNotToMirror()
    {
        var handler = new StubHandler(request => request.RequestUri!.Host.Contains("curseforge")
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Json("{\"data\": [], \"pagination\": {\"resultCount\": 0, \"totalCount\": 0}}"));
        using var api = new HttpResourceApi(new ResourceSigningOptions { CurseForgeApiKey = "k-1" }, FastOptions(), handler);

        api.Request(CurseForgeUrl);

        Assert.Equal("k-1", handler.Calls[0].Headers["x-api-key"]);
        Assert.False(handler.Calls[1].Headers.ContainsKey("x-api-key"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ResourceRequestErrorKind.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, ResourceRequestErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ResourceRequestErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, ResourceRequestErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, ResourceRequestErrorKind.ClientError)]
    [InlineData(HttpStatusCode.InternalServerError, ResourceRequestErrorKind.ServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ResourceRequestErrorKind.ServerError)]
    public void Classify_MapsStatusCodes(HttpStatusCode status, ResourceRequestErrorKind expected)
        => Assert.Equal(expected, HttpResourceApi.Classify(status));

    // ---- 直连路径（MC 下载源等「两条独立地址」的场景） ----

    [Fact]
    public void RequestDirect_DoesNotExpandCandidates()
    {
        var handler = new StubHandler(_ => Json("{\"versions\": []}"));
        using var api = new HttpResourceApi(new ResourceSigningOptions(), FastOptions(), handler);

        api.RequestDirect(McVersionSources.MojangUrl, TimeSpan.FromSeconds(5));

        // 普通 Request 对无镜像映射的地址会重试两次；直连只打一次
        Assert.Single(handler.Calls);
        Assert.Equal(McVersionSources.MojangUrl, handler.Calls[0].Url);
    }

    [Fact]
    public void RequestDirect_TimeoutIsClassifiedAsTimeout()
    {
        var handler = new StubHandler(_ =>
        {
            Thread.Sleep(1500);
            return Json("{}");
        });
        using var api = new HttpResourceApi(new ResourceSigningOptions(), FastOptions(), handler);

        var ex = Assert.Throws<ResourceRequestException>(
            () => api.RequestDirect(McVersionSources.BmclapiUrl, TimeSpan.FromSeconds(1)));

        Assert.Equal(ResourceRequestErrorKind.Timeout, ex.Kind);
        Assert.True(ex.IsTransient);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, Dictionary<string, string> Headers)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Handle(request));

        // HttpResourceApi 走同步 Send，自定义 handler 必须同时实现同步入口
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => Handle(request);

        private HttpResponseMessage Handle(HttpRequestMessage request)
        {
            Calls.Add((request.RequestUri!.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
            return respond(request);
        }
    }
}
