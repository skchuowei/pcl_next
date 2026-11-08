using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// 测试用资源站请求器：记录请求并按 URL 前缀返回预设响应。
/// </summary>
public sealed class FakeResourceApi : IResourceApi
{
    private readonly List<(string Url, HttpMethod? Method, string? Content)> _requests = [];
    private readonly List<(Func<string, bool> Match, Func<string, JsonNode> Respond)> _routes = [];

    /// <summary>已发生的请求（URL、方法、请求体）。</summary>
    public IReadOnlyList<(string Url, HttpMethod? Method, string? Content)> Requests => _requests;

    /// <summary>注册路由：URL 满足 match 时返回 respond 的结果。</summary>
    public void Map(Func<string, bool> match, Func<string, JsonNode> respond) => _routes.Add((match, respond));

    /// <summary>注册路由：URL 包含 subUrl 时返回固定 JSON 文本。</summary>
    public void MapJson(string subUrl, string json) => Map(u => u.Contains(subUrl, StringComparison.Ordinal), _ => JsonNode.Parse(json)!);

    /// <summary>注册路由：URL 完全等于 url 时返回固定 JSON 文本。</summary>
    public void MapJsonExact(string url, string json) => Map(u => u == url, _ => JsonNode.Parse(json)!);

    public JsonNode Request(string url, HttpMethod? method = null, string? content = null, string? contentType = null)
    {
        _requests.Add((url, method, content));
        foreach (var (match, respond) in _routes)
            if (match(url)) return respond(url);
        throw new InvalidOperationException($"FakeResourceApi 未注册该请求的响应：{url}");
    }
}
