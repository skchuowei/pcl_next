namespace PCL.Download.Tests;

/// <summary>
/// ResourceSearcher（VB ResourceSearcher.vb 平移）对拍测试。
/// 关注：URL 拼装、双平台拉取 + IsLike 去重 + 评分排序、中文反查分支。
/// 与 ResourceProjectTests 共享静态缓存，故同属 "Resource" 集合串行执行。
/// </summary>
[Collection("Resource")]
public class ResourceSearcherTests
{
    private readonly FakeResourceApi _api = new();

    public ResourceSearcherTests()
    {
        ResourceProject.ClearCache();
        ResourceConfig.DebugMode = false;
        ResourceConfig.WikiLookup = null;
        ResourceConfig.WikiEntries = null;
        ResourceConfig.ToSimplifiedChinese = null;
        ResourceApi.Default = null;
    }

    // ---- 固定 JSON 夹具（纯原始字符串，花括号为字面量）----

    private const string CfJei = """
    { "data": [ {
        "id": 238222, "slug": "jei", "name": "Just Enough Items (JEI)", "summary": "s",
        "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/jei/" },
        "dateReleased": "2026-01-01T00:00:00Z", "downloadCount": 100000000,
        "logo": { "url": "https://e.com/l.png", "thumbnailUrl": "https://e.com/t.png" },
        "latestFiles": [ { "id": 111, "gameId": 432, "displayName": "1", "fileDate": "2026-01-01T00:00:00Z", "releaseType": 1,
          "downloadCount": 1, "fileName": "x.jar", "fileLength": 1, "hashes": [ {"algo":1,"value":"aa"} ],
          "downloadUrl": "https://edge.forgecdn.net/files/0/111/x.jar", "gameVersions": [ "1.20.1", "Forge" ] } ],
        "latestFilesIndexes": [], "categories": [ {"id":412} ] } ],
      "pagination": { "resultCount": 1, "totalCount": 1 } }
    """;

    private const string MrFabric = """
    { "hits": [ {
        "id": "AANobbMI", "slug": "fabric-api", "title": "Fabric API", "description": "d",
        "project_type": "mod", "downloads": 5000000, "icon_url": "", "team": "t",
        "game_versions": [ "1.20.1" ], "loaders": [ "fabric" ],
        "categories": [ "utility" ], "date_modified": "2026-01-01T00:00:00Z" } ],
      "total_hits": 1 }
    """;

    private const string CfJeiDedup = """
    { "data": [ {
        "id": 238222, "slug": "jei", "name": "Just Enough Items (JEI)", "summary": "s",
        "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/jei/" },
        "dateReleased": "2026-01-01T00:00:00Z", "downloadCount": 100,
        "logo": { "url": "https://e.com/l.png", "thumbnailUrl": "https://e.com/t.png" },
        "latestFiles": [ { "id": 111, "gameId": 432, "displayName": "1", "fileDate": "2026-01-01T00:00:00Z", "releaseType": 1,
          "downloadCount": 1, "fileName": "x.jar", "fileLength": 1, "hashes": [ {"algo":1,"value":"aa"} ],
          "downloadUrl": "https://edge.forgecdn.net/files/0/111/x.jar", "gameVersions": [ "1.20.1", "Forge" ] } ],
        "latestFilesIndexes": [], "categories": [ {"id":412} ] } ],
      "pagination": { "resultCount": 1, "totalCount": 1 } }
    """;

    private const string MrJeiDedup = """
    { "hits": [ {
        "id": "AANobbMI", "slug": "jei", "title": "Just Enough Items (JEI)", "description": "d",
        "project_type": "mod", "downloads": 50, "icon_url": "", "team": "t",
        "game_versions": [ "1.19.2" ], "loaders": [ "forge" ],
        "categories": [ "utility" ], "date_modified": "2026-01-01T00:00:00Z" } ],
      "total_hits": 1 }
    """;

    private const string CfPage1 = """
    { "data": [ {
        "id": 1, "slug": "a", "name": "A Mod", "summary": "s",
        "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/a/" },
        "dateReleased": "2026-01-01T00:00:00Z", "downloadCount": 100,
        "logo": { "url": "https://e.com/l.png", "thumbnailUrl": "https://e.com/t.png" },
        "latestFiles": [ { "id": 10, "gameId": 432, "displayName": "1", "fileDate": "2026-01-01T00:00:00Z", "releaseType": 1,
          "downloadCount": 1, "fileName": "a.jar", "fileLength": 1, "hashes": [ {"algo":1,"value":"aa"} ],
          "downloadUrl": "https://edge.forgecdn.net/files/0/10/a.jar", "gameVersions": [ "1.20.1" ] } ],
        "latestFilesIndexes": [], "categories": [ {"id":412} ] } ],
      "pagination": { "resultCount": 1, "totalCount": 2 } }
    """;

    // ---- URL 拼装 ----

    [Fact]
    public void GetCurseForgeAddress_Mod类型与搜索词()
    {
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, new ResourceSearcher.SearchResult(), 40) { SearchText = "abc" };
        var url = req.GetCurseForgeAddress("abc", ignoreModLoaderFilter: false);
        Assert.StartsWith("https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize=40", url);
        Assert.Contains("classId=6", url); //Mod
        Assert.Contains("searchFilter=abc", url);
    }

    [Fact]
    public void GetCurseForgeAddress_带分类与加载器与版本()
    {
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.ModPack, new ResourceSearcher.SearchResult(), 40)
        {
            Tag = "4471/worldgen",
            ModLoaders = ModLoaders.Forge,
            GameVersion = "1.20.1",
        };
        var url = req.GetCurseForgeAddress("", ignoreModLoaderFilter: false);
        Assert.Contains("classId=4471", url); //ModPack
        Assert.Contains("categoryId=4471", url); //Tag.BeforeFirst("/")
        Assert.Contains("modLoaderType=1", url); //Forge → 1
        Assert.Contains("gameVersion=1.20.1", url);
    }

    [Fact]
    public void GetModrinthAddress_类型与查询()
    {
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, new ResourceSearcher.SearchResult(), 40) { SearchText = "abc" };
        var url = req.GetModrinthAddress("abc", ignoreModLoaderFilter: false);
        Assert.StartsWith("https://api.modrinth.com/v2/search?limit=40&index=relevance", url);
        Assert.Contains("query=abc", url);
        Assert.Contains("project_type:mod", url);
    }

    [Fact]
    public void GetModrinthAddress_带分类与多加载器与版本()
    {
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, new ResourceSearcher.SearchResult(), 40)
        {
            Tag = "406/worldgen",
            ModLoaders = ModLoaders.Fabric | ModLoaders.Forge,
            GameVersion = "1.20.1",
        };
        var url = req.GetModrinthAddress("", ignoreModLoaderFilter: false);
        Assert.Contains("categories:'worldgen'", url); //Tag.AfterLast("/")
        Assert.Contains("categories:fabric", url);
        Assert.Contains("categories:forge", url);
        Assert.Contains("versions:'1.20.1'", url);
    }

    // ---- 搜索编排（无搜索词：按下载量排序）----

    [Fact]
    public void Search_双平台拉取并按下载量排序去重()
    {
        _api.MapJson("api.curseforge.com/v1/mods/search", CfJei);
        _api.MapJson("api.modrinth.com/v2/search", MrFabric);
        var storage = new ResourceSearcher.SearchResult();
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, storage, 100);
        ResourceSearcher.Search(req, _api);

        //两个平台、不同 ModLoader、不同名 → 不会 IsLike 去重，两个都保留
        Assert.Equal(2, storage.Results.Count);
        //CF 下载量 1e8×1；MR 5e6×5=2.5e7 → CF 排在前
        Assert.Equal(ResourcePlatforms.CurseForge, storage.Results[0].Platform);
        Assert.Equal(ResourcePlatforms.Modrinth, storage.Results[1].Platform);
        Assert.Null(storage.ErrorMessage);
    }

    [Fact]
    public void Search_仅CurseForge源时不含Modrinth请求()
    {
        _api.MapJson("api.curseforge.com/v1/mods/search", CfJei);
        var storage = new ResourceSearcher.SearchResult();
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, storage, 10) { Sources = ResourcePlatforms.CurseForge };
        ResourceSearcher.Search(req, _api);
        Assert.All(_api.Requests, r => Assert.DoesNotContain("api.modrinth.com", r.Url));
        Assert.Single(storage.Results);
    }

    [Fact]
    public void Search_单页不足目标且可继续时加载下一页()
    {
        _api.MapJson("api.curseforge.com/v1/mods/search", CfPage1);
        var storage = new ResourceSearcher.SearchResult();
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, storage, 100) { Sources = ResourcePlatforms.CurseForge };
        ResourceSearcher.Search(req, _api);
        //totalCount=2 且 offset 已 1<2 → 触发再拉一页（fixture 仍返回同一条）
        Assert.True(_api.Requests.Count(r => r.Url.Contains("api.curseforge.com/v1/mods/search")) >= 2);
    }

    [Fact]
    public void Search_两平台同名同Loader不同MC版本时按IsLike跨站去重()
    {
        //CF 与 MR 各一个 "JEI"，但 MC 版本不同（Drops 不同，规避 VB 的 Drops 相等反判怪癖），ModLoader 均为 Forge
        _api.MapJson("api.curseforge.com/v1/mods/search", CfJeiDedup);
        _api.MapJson("api.modrinth.com/v2/search", MrJeiDedup);
        var storage = new ResourceSearcher.SearchResult();
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, storage, 100);
        ResourceSearcher.Search(req, _api);
        //跨站同 Mod → 去重，仅剩 1 个（CF 排在前面）
        Assert.Single(storage.Results);
        Assert.Equal(ResourcePlatforms.CurseForge, storage.Results[0].Platform);
    }

    [Fact]
    public void Search_源失败时设置ErrorMessage但不抛异常()
    {
        _api.Map(u => u.Contains("api.curseforge.com/v1/mods/search", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("模拟 CF 失败"));
        _api.MapJson("api.modrinth.com/v2/search", MrFabric);
        var storage = new ResourceSearcher.SearchResult();
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, storage, 100);
        ResourceSearcher.Search(req, _api);
        Assert.NotNull(storage.ErrorMessage);
        Assert.Contains("CurseForge", storage.ErrorMessage);
        Assert.Single(storage.Results); //仅 MR 的结果
    }

    [Fact]
    public void Search_两源均失败时抛出首个异常()
    {
        _api.Map(u => u.Contains("api.curseforge.com/v1/mods/search", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("模拟 CF 失败"));
        _api.Map(u => u.Contains("api.modrinth.com/v2/search", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("模拟 MR 失败"));
        var storage = new ResourceSearcher.SearchResult();
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, storage, 100);
        Assert.Throws<InvalidOperationException>(() => ResourceSearcher.Search(req, _api));
    }

    // ---- 中文反查分支 ----

    [Fact]
    public void Search_中文搜索经WikiEntry反查为英文并改写请求()
    {
        //注入百科：中文名 "中文测试"，CurseForge/Modrinth 的 slug 均为 "jei"
        ResourceConfig.WikiEntries = [
            new WikiEntry { Id = 1, ChineseName = "中文测试", Popularity = 1,
                Slugs = new() { [ResourcePlatforms.CurseForge] = "jei", [ResourcePlatforms.Modrinth] = "jei" } },
        ];
        _api.MapJson("api.curseforge.com/v1/mods/search", CfJei);
        _api.MapJson("api.modrinth.com/v2/search", MrFabric);
        var storage = new ResourceSearcher.SearchResult();
        var req = new ResourceSearcher.SearchRequest(ResourceTypes.Mod, storage, 100) { SearchText = "中文测试" };
        ResourceSearcher.Search(req, _api);
        //两个平台的请求都应使用反查出的英文 "jei"，而非原文 "中文测试"
        var diagnostics = string.Join(Environment.NewLine, _api.Requests.Select(r => r.Url)) +
                          Environment.NewLine + "ErrorMessage: " + (storage.ErrorMessage ?? "(null)");
        Assert.True(_api.Requests.Any(r => r.Url.Contains("searchFilter=jei")),
            "未看到 CurseForge 的改写请求：\n" + diagnostics);
        Assert.True(_api.Requests.Any(r => r.Url.Contains("query=jei")),
            "未看到 Modrinth 的改写请求：\n" + diagnostics);
        Assert.DoesNotContain(_api.Requests, r => r.Url.Contains("中文测试"));
    }
}
