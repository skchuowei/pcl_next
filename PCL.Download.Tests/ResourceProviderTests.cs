using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// Provider 抽象与降级（dev.4 §2.1 A1/A2/A4）测试。
/// 关注：CF 未配置 Key 时不报错而是降级、注册表收敛源、指纹反查字段回填、搜索分页。
/// </summary>
[Collection("Resource")]
public class ResourceProviderTests
{
    public ResourceProviderTests()
    {
        ResourceProject.ClearCache();
        ResourceVersion.ClearCache();
        ResourceConfig.DebugMode = false;
        ResourceConfig.WikiLookup = null;
        ResourceConfig.WikiEntries = null;
        ResourceConfig.ToSimplifiedChinese = null;
        ResourceApi.Default = null;
    }

    // ---- 降级 ----

    [Fact]
    public void CurseForgeWithoutKey_IsUnavailableWithReadableReason()
    {
        var provider = new CurseForgeProvider(new FakeResourceApi(), new ResourceSigningOptions { CurseForgeApiKey = null });

        Assert.False(provider.IsAvailable);
        Assert.NotNull(provider.UnavailableReason);
        Assert.Contains("CurseForge 源未启用", provider.UnavailableReason!);
        Assert.Contains(ResourceSigningOptions.CurseForgeKeyEnvironmentVariable, provider.UnavailableReason!);
        Assert.Throws<ResourceProviderUnavailableException>(() => provider.GetProject("238222"));
    }

    [Fact]
    public void Registry_DegradesCurseForgeRequestToModrinth()
    {
        var api = new FakeResourceApi();
        var registry = new ResourceProviderRegistry(
        [
            new ModrinthProvider(api),
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = null }),
        ]);

        var sources = registry.ResolveSources(ResourcePlatforms.Any, out var notice);

        Assert.Equal(ResourcePlatforms.Modrinth, sources);
        Assert.Equal(ResourceProviderRegistry.CurseForgeDisabledNotice, notice);
        Assert.True(registry.HasDegradation);
    }

    [Fact]
    public void Registry_KeepsBothSourcesWhenKeyPresent()
    {
        var api = new FakeResourceApi();
        var registry = new ResourceProviderRegistry(
        [
            new ModrinthProvider(api),
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = "k" }),
        ]);

        var sources = registry.ResolveSources(ResourcePlatforms.Any, out var notice);

        Assert.Equal(ResourcePlatforms.Any, sources);
        Assert.Null(notice);
        Assert.False(registry.HasDegradation);
    }

    // ---- Modrinth 指纹反查 ----

    [Fact]
    public void ModrinthLookup_MapsHashToProjectAndNormalizesLoaders()
    {
        const string sha1 = "aaaabbbbccccddddeeeeffff0000111122223333";
        var api = new FakeResourceApi();
        api.MapJson("api.modrinth.com/v2/version_files", new JsonObject
        {
            [sha1] = ModrinthVersionJson(sha1, "P1", "xmod-1.0.jar", "1.0", "2026-01-01T00:00:00Z", "Fabric", "fabric"),
        }.ToJsonString());

        var provider = new ModrinthProvider(api);
        var matches = provider.LookupFingerprints([new ResourceFingerprint(sha1, null)]);

        var match = Assert.Single(matches);
        Assert.Equal("P1", match.ProjectId);
        Assert.Equal("xmod-1.0.jar", match.File.FileName);
        Assert.Equal(["fabric"], match.Loaders); // 去重 + 小写
    }

    [Fact]
    public void ModrinthLookup_RejectsHashMismatch()
    {
        const string requested = "aaaa000000000000000000000000000000000000";
        var api = new FakeResourceApi();
        // 接口返回的 sha1 与请求不符（VB 的防御性判断）
        api.MapJson("api.modrinth.com/v2/version_files",
            new JsonObject { [requested] = ModrinthVersionJson("bbbb111111111111111111111111111111111111", "P1", "x.jar", "1.0", "2026-01-01T00:00:00Z", "fabric") }.ToJsonString());

        var matches = new ModrinthProvider(api).LookupFingerprints([new ResourceFingerprint(requested, null)]);

        Assert.Empty(matches);
    }

    // ---- CurseForge 指纹反查 ----

    [Fact]
    public void CurseForgeLookup_FillsMissingGameIdAndParsesFile()
    {
        var api = new FakeResourceApi();
        // 真机上 exactMatches 的 file 对象可能没有 gameId，若不补会被误判为 Modrinth
        api.MapJson("api.curseforge.com/v1/fingerprints/432", """
        { "data": { "exactMatches": [ {
            "id": 238222,
            "file": {
              "id": 111, "displayName": "1.0", "fileDate": "2026-01-01T00:00:00Z", "releaseType": 1,
              "downloadCount": 5, "fileName": "jei-1.0.jar", "fileLength": 100,
              "hashes": [ { "algo": 1, "value": "AABBCC" } ],
              "downloadUrl": "https://edge.forgecdn.net/files/0/111/jei-1.0.jar",
              "gameVersions": [ "1.20.1", "Forge" ],
              "fileFingerprint": 123456789
            } } ] } }
        """);

        var provider = new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = "k" });
        var matches = provider.LookupFingerprints([new ResourceFingerprint(null, 123456789)]);

        var match = Assert.Single(matches);
        Assert.Equal("238222", match.ProjectId);
        Assert.Equal("jei-1.0.jar", match.File.FileName);      // 走的是 CF 解析分支
        Assert.Equal("aabbcc", match.File.Hash);               // 小写化
        Assert.True(match.File.ModLoaders.HasFlag(ModLoaders.Forge));
        Assert.Equal(123456789u, match.Fingerprint.CurseForge);
    }

    [Fact]
    public void CurseForgeLookup_UnavailableDoesNotSendRequest()
    {
        var api = new FakeResourceApi();
        var provider = new CurseForgeProvider(api, new ResourceSigningOptions());

        Assert.Throws<ResourceProviderUnavailableException>(
            () => provider.LookupFingerprints([new ResourceFingerprint(null, 1)]));
        Assert.Empty(api.Requests);
    }

    // ---- 搜索分页 ----

    [Fact]
    public void ModrinthSearch_ReturnsOnePageOnlyFromModrinth()
    {
        const int pageSize = 40;
        var hits = new JsonArray();
        for (var i = 0; i < pageSize; i++)
            hits.Add(ModrinthHitJson($"P{i}", $"xmod-{i}", 12345 - i));

        var api = new FakeResourceApi();
        api.MapJson("api.modrinth.com/v2/search", new JsonObject
        {
            ["hits"] = hits,
            ["total_hits"] = pageSize + 1,
        }.ToJsonString());

        var page = new ModrinthProvider(api).Search(new ResourceProviderSearchRequest
        {
            Type = ResourceTypes.Mod,
            SearchText = "xmod",
            GameVersion = "1.20.1",
            PageSize = pageSize,
        });

        Assert.Equal(pageSize, page.Items.Count);
        Assert.Equal(pageSize, page.Offset);
        Assert.Equal(pageSize + 1, page.Total);
        Assert.True(page.HasMore);
        Assert.All(api.Requests, r => Assert.Contains("api.modrinth.com", r.Url));
        Assert.DoesNotContain(api.Requests, r => r.Url.Contains("curseforge"));
    }

    // ---- 融合搜索（Phase 4 首步） ----

    [Fact]
    public void RegistrySearch_DegradesToModrinthAndAccumulatesAcrossPages()
    {
        const int pageSize = 40;
        var api = new FakeResourceApi();
        api.Map(u => u.Contains("api.modrinth.com/v2/search"),
            u => ModrinthSearchPageJson(u, pageSize, totalHits: pageSize * 3));

        var registry = new ResourceProviderRegistry(
        [
            new ModrinthProvider(api),
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = null }),
        ], api);

        var storage = new ResourceSearcher.SearchResult();
        var first = registry.Search(new ResourceProviderSearchRequest
        {
            Type = ResourceTypes.Mod,
            SearchText = "xmod",
            PageSize = pageSize,
            Storage = storage,
        }, out var notice);

        Assert.Equal(ResourceProviderRegistry.CurseForgeDisabledNotice, notice);
        Assert.Equal(pageSize, first.Items.Count);
        Assert.Equal(pageSize, storage.Results.Count);
        Assert.All(api.Requests, r => Assert.Contains("api.modrinth.com", r.Url));
        Assert.DoesNotContain(api.Requests, r => r.Url.Contains("curseforge"));

        //第二页：偏移继续推进，结果累积而不是被替换
        var second = registry.Search(new ResourceProviderSearchRequest
        {
            Type = ResourceTypes.Mod,
            SearchText = "xmod",
            PageSize = pageSize,
            Storage = storage,
        }, out _);

        Assert.Equal(pageSize, second.Items.Count);
        Assert.Equal(pageSize * 2, storage.Results.Count);
        Assert.Equal(pageSize * 2, storage.Results.Select(p => p.Id).Distinct().Count());
        Assert.Equal(pageSize * 3, second.Total);
        Assert.True(second.HasMore);
    }

    [Fact]
    public void RegistrySearch_HonorsCallerSourceSelection()
    {
        var api = new FakeResourceApi();
        api.Map(u => u.Contains("api.modrinth.com/v2/search"),
            u => ModrinthSearchPageJson(u, 1, totalHits: 1));

        var registry = new ResourceProviderRegistry(
        [
            new ModrinthProvider(api),
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = "test-key" }),
        ], api);

        registry.Search(new ResourceProviderSearchRequest
        {
            Type = ResourceTypes.Mod,
            SearchText = "xmod",
            PageSize = 1,
        }, ResourcePlatforms.Modrinth, out var notice);

        Assert.Null(notice); //用户本来就只选 Modrinth，不算降级
        Assert.DoesNotContain(api.Requests, r => r.Url.Contains("curseforge"));
    }

    [Fact]
    public void RegistrySearch_ThrowsWhenNoSourceAvailable()
    {
        var api = new FakeResourceApi();
        var registry = new ResourceProviderRegistry(
        [
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = null }),
        ], api);

        var ex = Assert.Throws<ResourceProviderUnavailableException>(() =>
            registry.Search(new ResourceProviderSearchRequest { Type = ResourceTypes.Mod }, out _));
        Assert.Contains("没有可用的资源源", ex.Message);
        Assert.Empty(api.Requests);
    }

    // ---- 夹具 ----

    /// <summary>构造一页 Modrinth 搜索结果；按 URL 里的 offset 生成互不相同的工程，便于验证翻页累积。</summary>
    private static JsonObject ModrinthSearchPageJson(string url, int pageSize, int totalHits)
    {
        var offset = 0;
        var marker = url.IndexOf("offset=", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var start = marker + "offset=".Length;
            var end = url.IndexOf('&', start);
            int.TryParse(url[start..(end < 0 ? url.Length : end)], out offset);
        }

        var hits = new JsonArray();
        for (var i = 0; i < pageSize; i++)
            hits.Add(ModrinthHitJson($"P{offset + i}", $"xmod-{offset + i}", 100_000 - offset - i));

        return new JsonObject
        {
            ["hits"] = hits,
            ["total_hits"] = totalHits,
        };
    }

    private static JsonObject ModrinthHitJson(string id, string slug, int downloads) => new()
    {
        ["id"] = id,
        ["slug"] = slug,
        ["title"] = slug,
        ["description"] = "d",
        ["project_type"] = "mod",
        ["downloads"] = downloads,
        ["icon_url"] = "",
        ["team"] = "t",
        ["game_versions"] = new JsonArray("1.20.1"),
        ["loaders"] = new JsonArray("fabric"),
        ["categories"] = new JsonArray("utility"),
        ["date_modified"] = "2026-01-01T00:00:00Z",
    };

    private static JsonObject ModrinthVersionJson(string sha1, string projectId, string fileName,
        string version, string publishedAt, params string[] loaders) => new()
    {
        ["id"] = $"{projectId}-{version}",
        ["project_id"] = projectId,
        ["name"] = fileName,
        ["version_number"] = version,
        ["date_published"] = publishedAt,
        ["version_type"] = "release",
        ["downloads"] = 10,
        ["files"] = new JsonArray(new JsonObject
        {
            ["filename"] = fileName,
            ["url"] = $"https://cdn.modrinth.com/data/{projectId}/versions/{fileName}",
            ["size"] = 100,
            ["hashes"] = new JsonObject { ["sha1"] = sha1 },
        }),
        ["loaders"] = new JsonArray(loaders.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()),
        ["game_versions"] = new JsonArray("1.20.1"),
    };
}
