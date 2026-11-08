using System.Net;
using System.Net.Http;

namespace PCL.Download.Tests;

/// <summary>
/// 资源站请求签名/头注入（VB <c>ModSecret.SecretHeadersSign</c> 平移）测试。
/// 关注：UA/Referer 注入与不覆盖、CF 密钥只发官方域、镜像不泄漏密钥、{CDN} 标记。
/// </summary>
public class ResourceSigningTests
{
    [Fact]
    public void Apply_AddsDefaultUserAgentAndReferer()
    {
        var options = new ResourceSigningOptions { Referer = "http://5003.open.pcl2.server/" };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.modrinth.com/v2/project/jei");

        ResourceSigning.Apply(request, options);

        Assert.Equal("PCL2/pcl-next", string.Join("", request.Headers.GetValues("User-Agent")));
        Assert.Equal("http://5003.open.pcl2.server/", request.Headers.Referrer?.ToString());
    }

    [Fact]
    public void Apply_DoesNotOverrideExistingUserAgent()
    {
        var options = new ResourceSigningOptions { UserAgent = "PCL2/9.9.9.9" };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.modrinth.com/v2/project/jei");
        request.Headers.TryAddWithoutValidation("User-Agent", "Custom/1.0");

        ResourceSigning.Apply(request, options);

        Assert.Equal("Custom/1.0", string.Join("", request.Headers.GetValues("User-Agent")));
    }

    [Theory]
    [InlineData("https://api.curseforge.com/v1/mods/238222", true)]
    [InlineData("https://edge.forgecdn.net/files/0/111/x.jar", true)]
    [InlineData("https://mod.mcimirror.top/curseforge/v1/mods/238222", false)]
    [InlineData("https://api.modrinth.com/v2/project/jei", false)]
    public void CurseForgeKey_OnlySentToOfficialHosts(string url, bool expectHeader)
    {
        var options = new ResourceSigningOptions { CurseForgeApiKey = "secret-key" };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        ResourceSigning.Apply(request, options);

        Assert.Equal(expectHeader, request.Headers.Contains("x-api-key"));
        if (expectHeader) Assert.Equal("secret-key", request.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public void MissingKey_SendsNoHeader()
    {
        var options = new ResourceSigningOptions { CurseForgeApiKey = null };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/1");

        ResourceSigning.Apply(request, options);

        Assert.False(request.Headers.Contains("x-api-key"));
        Assert.False(options.HasCurseForgeApiKey);
    }

    [Fact]
    public async Task Handler_InjectsHeadersIntoOutgoingRequest()
    {
        var capture = new CapturingHandler();
        var options = new ResourceSigningOptions { CurseForgeApiKey = "k-123", Referer = "http://1.open.pcl2.server/" };
        using var invoker = new HttpMessageInvoker(new RequestSigningHandler(options) { InnerHandler = capture });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/1");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capture.LastHeaders);
        Assert.Equal("k-123", capture.LastHeaders!["x-api-key"]);
        Assert.Contains("PCL2/", capture.LastHeaders["User-Agent"]);
        Assert.Equal("http://1.open.pcl2.server/", capture.LastHeaders["Referer"]);
    }

    [Theory]
    [InlineData("https://x.com/a.jar{CDN}", "https://x.com/a.jar")]
    [InlineData("https://x.com/a b.jar{CDN}", "https://x.com/a%20b.jar")]
    [InlineData("https://x.com/a.jar", "https://x.com/a.jar")]
    public void SignCdnUrl_StripsMarkerAndEscapesSpaces(string input, string expected)
        => Assert.Equal(expected, ResourceSigning.SignCdnUrl(input));

    [Fact]
    public void FromEnvironment_ReadsDedicatedVariable()
    {
        var original = Environment.GetEnvironmentVariable(ResourceSigningOptions.CurseForgeKeyEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ResourceSigningOptions.CurseForgeKeyEnvironmentVariable, "  env-key  ");
            Assert.Equal("env-key", ResourceSigningOptions.ReadCurseForgeKeyFromEnvironment());
            Assert.Equal("env-key", ResourceSigningOptions.FromEnvironment().CurseForgeApiKey);

            Environment.SetEnvironmentVariable(ResourceSigningOptions.CurseForgeKeyEnvironmentVariable, "   ");
            Assert.Null(ResourceSigningOptions.ReadCurseForgeKeyFromEnvironment());
            Assert.False(ResourceSigningOptions.FromEnvironment().HasCurseForgeApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ResourceSigningOptions.CurseForgeKeyEnvironmentVariable, original);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Dictionary<string, string>? LastHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastHeaders = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
