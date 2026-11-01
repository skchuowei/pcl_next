using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// <see cref="McVersionManifestClient"/> 测试（Phase 4：下载页版本列表数据源改由 C# 提供）。
/// <para/>全部走桩请求器，不依赖外网；竞速用例用短延时模拟「官方慢、镜像快」。
/// </summary>
public class McVersionManifestTests
{
    private const string Mojang = McVersionSources.MojangUrl;
    private const string Bmclapi = McVersionSources.BmclapiUrl;

    // ---- 候选排期：必须与 VB DlSourceLoader 的 WaitCycle 语义一致 ----

    [Fact]
    public void Build_MirrorFirst_StartsMirrorImmediatelyThenOfficialAt30s()
    {
        var sources = McVersionSources.Build(McVersionSourceMode.MirrorFirst);

        Assert.Equal([Bmclapi, Mojang], sources.Select(s => s.Url));
        Assert.Equal(0, sources[0].StartAtSeconds);
        Assert.Equal(30, sources[1].StartAtSeconds);
        Assert.False(sources[0].IsOfficial);
    }

    [Fact]
    public void Build_OfficialFirst_StartsOfficialImmediatelyThenMirrorAt5s()
    {
        var sources = McVersionSources.Build(McVersionSourceMode.OfficialFirst);

        Assert.Equal([Mojang, Bmclapi], sources.Select(s => s.Url));
        Assert.Equal(0, sources[0].StartAtSeconds);   // 第一个源永远立即启动，不是 5s
        Assert.Equal(5, sources[1].StartAtSeconds);
    }

    [Fact]
    public void Build_OfficialPreferred_KeepsMirrorUntil60s()
    {
        var sources = McVersionSources.Build(McVersionSourceMode.OfficialPreferred);

        Assert.Equal([Mojang, Bmclapi], sources.Select(s => s.Url));
        Assert.Equal(0, sources[0].StartAtSeconds);
        Assert.Equal(60, sources[1].StartAtSeconds);
    }

    // ---- 获取与解析 ----

    [Fact]
    public void Fetch_ParsesManifestAndReportsSource()
    {
        var api = new FakeDirectResourceApi();
        api.MapJsonExact(Mojang, ManifestJson(("1.21.5", "release"), ("25w14a", "snapshot")));

        var manifest = new McVersionManifestClient(api).Fetch(McVersionSourceMode.OfficialFirst);

        Assert.Equal("Mojang 官方源", manifest.SourceName);
        Assert.True(manifest.IsOfficial);
        Assert.Equal("1.21.5", manifest.LatestRelease);
        Assert.Equal("25w14a", manifest.LatestSnapshot);
        Assert.Equal(210, manifest.Versions.Count);
        Assert.Equal("https://x/1.21.5.json", manifest.GetVersionJsonUrl("1.21.5"));
        Assert.Null(manifest.GetVersionJsonUrl("不存在的版本"));
        Assert.Contains("\"versions\"", manifest.RawJson);
    }

    [Fact]
    public void Fetch_ShortVersionList_RejectsThatSourceAndFallsBack()
    {
        var api = new FakeDirectResourceApi();
        api.MapJsonExact(Mojang, """{ "latest": {}, "versions": [ { "id": "1.21.5", "type": "release", "url": "u" } ] }""");
        api.MapJsonExact(Bmclapi, ManifestJson());

        var manifest = new McVersionManifestClient(api)
            .Fetch([McVersionSources.Mojang(0), McVersionSources.Bmclapi(0)]);

        // 版本数不足 200 视为「拿到了错误页面」，该源作废，改用镜像
        Assert.Equal("BMCLAPI", manifest.SourceName);
        Assert.Contains(Mojang, api.Calls);
    }

    [Fact]
    public void Fetch_RequiredVersionMissingOnMirror_UsesOfficial()
    {
        var api = new FakeDirectResourceApi();
        api.MapJsonExact(Bmclapi, ManifestJson(("1.21.4", "release")));   // 镜像落后一个版本
        api.MapJsonExact(Mojang, ManifestJson(("1.21.5", "release")));

        var manifest = new McVersionManifestClient(api)
            .Fetch([McVersionSources.Bmclapi(0), McVersionSources.Mojang(0)], requiredVersion: "1.21.5");

        Assert.Equal("Mojang 官方源", manifest.SourceName);
        Assert.Equal("1.21.5", manifest.LatestRelease);
        Assert.Contains(Bmclapi, api.Calls);   // 镜像确实被尝试过，只是被目标版本校验挡掉
    }

    [Fact]
    public void Fetch_PrefersEarlierSourceWhenBothReady()
    {
        var api = new FakeDirectResourceApi();
        api.MapJsonExact(Mojang, ManifestJson(("1.21.5", "release")));
        api.MapJsonExact(Bmclapi, ManifestJson(("1.21.5", "release")));

        var manifest = new McVersionManifestClient(api)
            .Fetch([McVersionSources.Mojang(0), McVersionSources.Bmclapi(0)]);

        Assert.Equal("Mojang 官方源", manifest.SourceName);
    }

    [Fact]
    public void Fetch_SlowFirstSource_DoesNotBlockReadySecond()
    {
        var api = new FakeDirectResourceApi();
        api.MapExact(Mojang, () =>
        {
            Thread.Sleep(400);
            return JsonNode.Parse(ManifestJson(("1.21.5", "release")))!;
        });
        api.MapJsonExact(Bmclapi, ManifestJson(("1.21.5", "release")));

        var manifest = new McVersionManifestClient(api)
            .Fetch([McVersionSources.Mojang(0), McVersionSources.Bmclapi(0)]);

        Assert.Equal("BMCLAPI", manifest.SourceName);   // 先完成者胜，不等慢源
    }

    [Fact]
    public void Fetch_AllSourcesFail_ThrowsAggregateWithDetail()
    {
        var api = new FakeDirectResourceApi();
        api.MapExact(Mojang, () => throw new HttpRequestException("官方不可达"));
        api.MapExact(Bmclapi, () => throw new HttpRequestException("镜像不可达"));

        var ex = Assert.Throws<ResourceRequestException>(() => new McVersionManifestClient(api)
            .Fetch([McVersionSources.Mojang(0), McVersionSources.Bmclapi(0)]));

        Assert.Contains("所有版本列表源均获取失败", ex.Message);
        Assert.Contains("官方不可达", ex.Message);
        Assert.Contains("镜像不可达", ex.Message);
    }

    [Fact]
    public void Fetch_MergesExtraVersionsFileAndDeduplicates()
    {
        var extraPath = Path.Combine(Path.GetTempPath(), $"pcl-extra-{Guid.NewGuid():N}.json");
        File.WriteAllText(extraPath, """
        { "versions": [
            { "id": "1.99.9", "type": "release", "url": "https://x/1.99.9.json", "releaseTime": "2026-10-01T00:00:00Z" },
            { "id": "1.21.5", "type": "release", "url": "https://dup", "releaseTime": "2026-01-01T00:00:00Z" } ] }
        """);

        try
        {
            var api = new FakeDirectResourceApi();
            api.MapJsonExact(Bmclapi, ManifestJson(("1.21.5", "release")));

            var manifest = new McVersionManifestClient(api).Fetch(McVersionSourceMode.MirrorFirst, null, extraPath);

            Assert.Equal(211, manifest.Versions.Count);                 // 只新增 1.99.9，1.21.5 不重复
            Assert.Equal("https://x/1.99.9.json", manifest.GetVersionJsonUrl("1.99.9"));
            Assert.Equal("https://x/1.21.5.json", manifest.GetVersionJsonUrl("1.21.5"));  // 原记录优先，未被覆盖
            Assert.Contains("1.99.9", manifest.RawJson);
        }
        finally
        {
            File.Delete(extraPath);
        }
    }

    [Fact]
    public void Fetch_MissingExtraFileIsIgnored()
    {
        var api = new FakeDirectResourceApi();
        api.MapJsonExact(Mojang, ManifestJson());

        var manifest = new McVersionManifestClient(api)
            .Fetch(McVersionSourceMode.OfficialFirst, null, Path.Combine(Path.GetTempPath(), "pcl-extra-not-exists.json"));

        Assert.Equal(210, manifest.Versions.Count);
    }

    // ---- Drop 提取 ----

    [Fact]
    public void ExtractDrops_SkipsPrereleaseAndOldVersions_SortsDescending()
    {
        var versions = new[]
        {
            new McVersionEntry("1.21.5", "release", "u", ""),
            new McVersionEntry("26.3.2", "release", "u", ""),
            new McVersionEntry("1.20.1", "release", "u", ""),
            new McVersionEntry("1.20.1-pre1", "snapshot", "u", ""),   // 带 - ，跳过
            new McVersionEntry("25w14a", "snapshot", "u", ""),        // 快照，换算为 209，跳过
            new McVersionEntry("1.20.1", "release", "u", ""),         // 重复，去重
        };

        var drops = McVersionManifestClient.ExtractDrops(versions);

        // Drop = Major*10+Minor：1.20.1 与 1.20 同属 200，1.21.5 为 210，26.3.2 为 263
        Assert.Equal(new[] { 263, 210, 200 }, drops);
    }

    [Fact]
    public void ExtractDrops_EmptyInput_ReturnsEmpty()
        => Assert.Empty(McVersionManifestClient.ExtractDrops([]));

    // ---- 夹具 ----

    /// <summary>生成 210 条版本（前几条自定义，其余补位）；可指定 latest 字段。</summary>
    private static string ManifestJson(params (string Id, string Type)[] head)
    {
        var items = new List<string>();
        foreach (var (id, type) in head)
            items.Add($"{{\"id\":\"{id}\",\"type\":\"{type}\",\"url\":\"https://x/{id}.json\",\"releaseTime\":\"2026-01-01T00:00:00Z\"}}");
        for (var i = items.Count; i < 210; i++)
            items.Add($"{{\"id\":\"1.{i}.0\",\"type\":\"release\",\"url\":\"https://x/1.{i}.0.json\",\"releaseTime\":\"2020-01-01T00:00:00Z\"}}");

        return $"{{\"latest\":{{\"release\":\"1.21.5\",\"snapshot\":\"25w14a\"}},\"versions\":[{string.Join(",", items)}]}}";
    }
}

/// <summary>
/// 同时实现 <see cref="IResourceApi"/> 与 <see cref="IDirectResourceApi"/> 的桩，
/// 用于验证版本清单走直连路径（不展开候选源）。
/// </summary>
public sealed class FakeDirectResourceApi : IResourceApi, IDirectResourceApi
{
    private readonly Dictionary<string, Func<JsonNode>> _routes = new(StringComparer.Ordinal);

    /// <summary>按 URL 记录的请求（含 Request 与 RequestDirect 两条路径）。</summary>
    public List<string> Calls { get; } = [];

    /// <summary>注册精确匹配的响应；respond 抛异常即模拟该源失败。</summary>
    public void MapExact(string url, Func<JsonNode> respond) => _routes[url] = respond;

    /// <summary>注册精确匹配的固定 JSON 响应。</summary>
    public void MapJsonExact(string url, string json) => _routes[url] = () => JsonNode.Parse(json)!;

    public JsonNode Request(string url, HttpMethod? method = null, string? content = null, string? contentType = null)
    {
        Calls.Add(url);
        return Respond(url);
    }

    public JsonNode RequestDirect(string url, TimeSpan timeout)
    {
        Calls.Add(url);
        return Respond(url);
    }

    private JsonNode Respond(string url)
    {
        if (_routes.TryGetValue(url, out var respond)) return respond();
        throw new InvalidOperationException($"FakeDirectResourceApi 未注册该请求的响应：{url}");
    }
}
