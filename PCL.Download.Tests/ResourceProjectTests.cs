using System.Globalization;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// ResourceProject（VB ResourceProject.vb 平移）对拍测试。
/// </summary>
[Collection("Resource")]
public class ResourceProjectTests
{
    public ResourceProjectTests()
    {
        ResourceVersion.ClearCache();
        ResourceProject.ClearCache();
        ResourceConfig.DebugMode = false;
        ResourceConfig.WikiLookup = null;
        ResourceApi.Default = null;
    }

    private const string CfLatestFileJson = """
    {
      "gameId": 432, "id": 111, "displayName": "JEI 1.20.1", "fileDate": "2026-01-01T00:00:00Z",
      "releaseType": 1, "downloadCount": 5, "fileName": "jei.jar", "fileLength": 100,
      "hashes": [ {"algo": 1, "value": "aa"} ],
      "downloadUrl": "https://edge.forgecdn.net/files/0/111/jei.jar",
      "gameVersions": ["1.20.1", "Forge"]
    }
    """;

    private static readonly string CfProjectJson = $$"""
    {
      "id": 238222, "slug": "jei", "name": "Just Enough Items (JEI)", "summary": "Item and recipe viewing.",
      "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/jei/" },
      "dateReleased": "2026-01-01T00:00:00Z",
      "downloadCount": 100000000,
      "logo": { "url": "https://example.com/logo.png", "thumbnailUrl": "https://example.com/thumb.png" },
      "latestFiles": [ {{CfLatestFileJson}} ],
      "latestFilesIndexes": [ { "gameVersion": "1.20.2", "fileId": 222 }, { "gameVersion": "23w13a", "fileId": 333 } ],
      "categories": [ {"id": 412}, {"id": 406}, {"id": 412} ]
    }
    """;

    private static ResourceProject Parse(string json) => new(JsonNode.Parse(json)!);

    [Fact]
    public void 构造_CurseForge_字段解析与VB语义一致()
    {
        var p = Parse(CfProjectJson);
        Assert.Equal(ResourcePlatforms.CurseForge, p.Platform); //含 summary → CurseForge
        Assert.Equal(ResourceTypes.Mod, p.Types); //网址含 /mc-mods/
        Assert.Equal("jei", p.Slug);
        Assert.Equal("238222", p.Id);
        Assert.Equal("Just Enough Items (JEI)", p.RawName);
        Assert.Equal("Item and recipe viewing.", p.Description);
        Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/jei", p.Website); //TrimEnd("/")
        Assert.Equal(DateTime.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), p.LastUpdate);
        Assert.Equal(100000000, p.DownloadCount);
        Assert.Equal("https://example.com/thumb.png", p.LogoUrl); //thumbnailUrl 优先
        Assert.Equal(ModLoaders.Forge, p.ModLoaders); //latestFiles 中的 Forge
    }

    [Fact]
    public void 构造_CurseForge_文件索引与Drops()
    {
        var p = Parse(CfProjectJson);
        //111（latestFiles，需可下载且有标准版本）+ 222（latestFilesIndexes，标准版本）；333 的 23w13a 不是标准格式
        Assert.Equal(new[] { 111, 222 }, p.CurseForgeFileIds);
        //1.20.1 → 200，Forge → 209（剔除），1.20.2 → 200
        Assert.Equal(new[] { 200 }, p.Drops);
    }

    [Fact]
    public void 构造_CurseForge_分类转中文标签并排序去重()
    {
        var p = Parse(CfProjectJson);
        var expected = new List<string> { "科技", "世界元素" }; //412, 406（重复 412 去重）
        expected.Sort(); //与 Tags.Sort() 相同的默认比较器
        Assert.Equal(expected, p.Tags);
    }

    [Fact]
    public void 构造_CurseForge_无分类时标签为其他()
    {
        var json = CfProjectJson.Replace("\"categories\": [ {\"id\": 412}, {\"id\": 406}, {\"id\": 412} ]", "\"categories\": []");
        Assert.Equal(new[] { "其他" }, Parse(json).Tags);
    }

    [Theory]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei", ResourceTypes.Mod)]
    [InlineData("https://www.curseforge.com/minecraft/mod/jei", ResourceTypes.Mod)]
    [InlineData("https://www.curseforge.com/minecraft/modpacks/ftb", ResourceTypes.ModPack)]
    [InlineData("https://www.curseforge.com/minecraft/resourcepacks/faithful", ResourceTypes.ResourcePack)]
    [InlineData("https://www.curseforge.com/minecraft/texture-packs/faithful", ResourceTypes.ResourcePack)]
    [InlineData("https://www.curseforge.com/minecraft/shaders/bsl", ResourceTypes.Shader)]
    [InlineData("https://www.curseforge.com/minecraft/worlds/map", ResourceTypes.DataPack)] //其余 → 数据包
    public void 构造_CurseForge_网址映射类别(string website, ResourceTypes expected)
    {
        var json = """
        {
          "id": 1, "slug": "x", "name": "n", "summary": "s",
          "links": { "websiteUrl": "URL" },
          "dateReleased": "2026-01-01T00:00:00Z", "downloadCount": 0
        }
        """.Replace("URL", website);
        Assert.Equal(expected, Parse(json).Types);
    }

    private const string MrProjectJson = """
    {
      "project_id": "AAbbCCDD",
      "slug": "sodium",
      "title": "Sodium",
      "description": "Rendering engine.",
      "date_modified": "2026-03-01T00:00:00Z",
      "downloads": 5000000,
      "icon_url": "https://cdn.modrinth.com/icon.png",
      "project_type": "mod",
      "game_versions": ["1.21.1", "1.21.2", "23w13a"],
      "loaders": ["fabric"],
      "categories": ["optimization", "fabric", "utility"]
    }
    """;

    [Fact]
    public void 构造_Modrinth_字段解析与VB语义一致()
    {
        var p = Parse(MrProjectJson);
        Assert.Equal(ResourcePlatforms.Modrinth, p.Platform); //无 summary → Modrinth
        Assert.Equal(ResourceTypes.Mod, p.Types);
        Assert.Equal("AAbbCCDD", p.Id); //project_id 优先
        Assert.Equal("Sodium", p.RawName);
        Assert.Equal("https://modrinth.com/mod/sodium", p.Website);
        Assert.Equal("https://cdn.modrinth.com/icon.png", p.LogoUrl);
        Assert.Equal(ModLoaders.Fabric, p.ModLoaders); //loaders 与 categories 中的 fabric
        //UnsafeGameVersions 为原始版本串（未经滤）
        Assert.Equal(new[] { "1.21.1", "1.21.2", "23w13a" }, p.UnsafeGameVersions);
        //1.21.x → 210；23w13a 是愚人节快照 → 19.0.4 → 190（不为 209，不剔除）
        Assert.Equal(new[] { 210, 190 }, p.Drops);
        var expected = new List<string> { "性能优化", "实用" };
        expected.Sort();
        Assert.Equal(expected, p.Tags);
    }

    [Fact]
    public void 构造_Modrinth_搜索结果键名_versions与_updated()
    {
        var json = MrProjectJson
            .Replace("\"date_modified\"", "\"updated\"")
            .Replace("\"game_versions\"", "\"versions\"")
            .Replace("\"project_id\"", "\"id\"");
        var p = Parse(json);
        Assert.Equal("AAbbCCDD", p.Id); //回退到 id
        Assert.Equal(new[] { "1.21.1", "1.21.2", "23w13a" }, p.UnsafeGameVersions); //回退到 versions
    }

    [Fact]
    public void 构造_Modrinth_数据包类别带加载器时为ModOrDataPack()
    {
        var json = MrProjectJson
            .Replace("\"categories\": [\"optimization\", \"fabric\", \"utility\"]", "\"categories\": [\"datapack\", \"technology\"]");
        var p = Parse(json);
        Assert.Equal(ResourceTypes.DataPack | ResourceTypes.Mod, p.Types); //VB 第 379 行
        Assert.Equal(ModLoaders.Fabric, p.ModLoaders);
    }

    [Fact]
    public void 缓存Json_往返保持全部字段()
    {
        var p = Parse(CfProjectJson);
        var json1 = (JsonObject)p.ToJson().DeepClone();
        var p2 = Parse(json1.ToJsonString()); //含 Tags 键 → 走缓存分支
        var json2 = (JsonObject)p2.ToJson().DeepClone();
        json1.Remove("CacheTime"); //缓存时间戳每次生成，不参与比较
        json2.Remove("CacheTime");
        Assert.Equal(json1.ToJsonString(), json2.ToJsonString());
    }

    [Fact]
    public void FromProjectId_结果进缓存_二次调用不再联网()
    {
        var api = new FakeResourceApi();
        api.MapJsonExact("https://api.curseforge.com/v1/mods/238222", "{ \"data\": " + CfProjectJson + " }");
        var p1 = ResourceProject.FromProjectId("238222", ResourcePlatforms.CurseForge, api);
        Assert.Equal("jei", p1.Slug);
        var p2 = ResourceProject.FromProjectId("238222", ResourcePlatforms.CurseForge, api);
        Assert.Same(p1, p2);
        Assert.Single(api.Requests);
    }

    [Fact]
    public void FromProjectIds_缓存命中优先_缺失走批量接口()
    {
        var api = new FakeResourceApi();
        api.MapJsonExact("https://api.curseforge.com/v1/mods/238222", "{ \"data\": " + CfProjectJson + " }");
        var cached = ResourceProject.FromProjectId("238222", ResourcePlatforms.CurseForge, api);

        api.MapJsonExact("https://api.curseforge.com/v1/mods",
            """
            { "data": [ { "id": 999, "slug": "father", "name": "Father", "summary": "s",
                "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/father" },
                "dateReleased": "2026-01-10T00:00:00Z", "downloadCount": 1 } ] }
            """);
        var results = ResourceProject.FromProjectIds(["238222", "999"], ResourcePlatforms.CurseForge, api).ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(cached, results);
        Assert.Contains(results, r => r.Id == "999");
        //批量请求体为 VB 同款拼接：{"modIds": [999]}（数字串直接拼接）
        var batch = api.Requests.Single(r => r.Url == "https://api.curseforge.com/v1/mods");
        Assert.Equal("{\"modIds\": [999]}", batch.Content); //238222 已有缓存，只查缺失的 999
    }

    [Fact]
    public void FromProjectId_拒绝多平台()
        => Assert.Throws<ArgumentException>(() => ResourceProject.FromProjectId("1", ResourcePlatforms.Any, new FakeResourceApi()));

    // ---------- GetControlTitle ----------

    private static ResourceProject MakeProject(string rawName, string? chineseName = null, string slug = "slug")
    {
        ResourceConfig.WikiLookup = chineseName is null
            ? null
            : (s, p) => s == slug ? new WikiInfo { Id = 42, ChineseName = chineseName } : null;
        return Parse($$"""
        {
          "Tags": ["其他"], "Platform": 1, "Types": 1, "Slug": "{{slug}}", "Id": "{{slug}}",
          "RawName": "{{rawName}}", "Description": "d", "Website": "https://example.com",
          "LastUpdate": "2026-01-01T00:00:00Z", "DownloadCount": 0, "Drops": [210]
        }
        """);
    }

    [Theory]
    [InlineData("Just Enough Items", "Just Enough Items", "")] //单词名 → 无副标题
    [InlineData("FPS Counter", "FPS Counter", "")] //无缩写/加载器标记 → 无副标题
    [InlineData("Sodium [Fabric]", "Sodium", "  |  Fabric 版")]
    [InlineData("JER (Plugin)", "Plugin", "  |  JER")] //缩写进副标题
    [InlineData("Mini [Forge]", "Mini", "  |  Forge 版")]
    [InlineData("Something [Forge/Fabric]", "Something", "")] //Forge/Fabric 合并提示被去除
    [InlineData("Quilt [Quilt]", "Quilt [Quilt]", "")] //全部是加载器标记 → 无副标题
    [InlineData("Aqua [NeoForge]", "Aqua", "  |  NeoForge 版")]
    public void GetControlTitle_无中文翻译(string rawName, string title, string subtitle)
    {
        var p = MakeProject(rawName);
        Assert.Equal((title, subtitle), p.GetControlTitle(true));
    }

    [Fact]
    public void GetControlTitle_隐藏加载器描述时过滤加载器标记()
    {
        var p = MakeProject("Mini [Forge]");
        Assert.Equal(("Mini", ""), p.GetControlTitle(false));
        var p2 = MakeProject("JER (Plugin)");
        Assert.Equal(("Plugin", "  |  JER"), p2.GetControlTitle(false)); //缩写不受影响
    }

    [Theory]
    [InlineData("小地图", "小地图", "")] //纯中文名
    [InlineData("汤姆存储 - 星的奇妙优化 (Tom's balabala)", "汤姆存储 - 星的奇妙优化", "  |  Tom's balabala")]
    [InlineData("小地图 (Xaero's Minimap) - Forge 版", "小地图", "  |  Xaero's Minimap  |  Forge 版")]
    [InlineData("超级 (Gamma - Delta)", "超级", "  |  Gamma - Delta")] //多段非标注 → 使用原名
    [InlineData("超级 (AB - CD)", "超级", "  |  AB  |  CD")] //双缩写 → 保留拆分
    public void GetControlTitle_有中文翻译(string chineseName, string title, string subtitle)
    {
        var p = MakeProject("Xaero's Minimap", chineseName);
        Assert.Equal((title, subtitle), p.GetControlTitle(true));
    }

    [Fact]
    public void GetControlTitle_有中文翻译且隐藏加载器描述()
    {
        var p = MakeProject("Xaero's Minimap", "小地图 (Xaero's Minimap) - Forge 版");
        Assert.Equal(("小地图", "  |  Xaero's Minimap"), p.GetControlTitle(false));
    }

    [Fact]
    public void 百科翻译名与WikiId经钩子提供()
    {
        var p = MakeProject("Xaero's Minimap", "小地图");
        Assert.Equal("小地图", p.TranslatedName);
        Assert.Equal(42, p.WikiId);
        var p2 = MakeProject("Other");
        Assert.Equal("Other", p2.TranslatedName);
        Assert.Equal(0, p2.WikiId);
    }

    // ---------- IsLike ----------

    private static ResourceProject MakeForIsLike(string id, ResourcePlatforms platform, string rawName,
        ModLoaders loaders = ModLoaders.Forge, int[]? drops = null, string slug = "s", string? chineseName = null,
        DateTime? lastUpdate = null)
    {
        if (chineseName is not null)
            ResourceConfig.WikiLookup = (s, p) => new WikiInfo { Id = 1, ChineseName = chineseName };
        drops ??= [210];
        return Parse($$"""
        {
          "Tags": ["其他"], "Platform": {{(int)platform}}, "Types": 1, "Slug": "{{slug}}", "Id": "{{id}}",
          "RawName": "{{rawName}}", "Description": "d", "Website": "https://example.com",
          "LastUpdate": "{{(lastUpdate ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}}",
          "DownloadCount": 0, "ModLoaders": {{(int)loaders}}, "Drops": [{{string.Join(",", drops)}}]
        }
        """);
    }

    [Fact]
    public void IsLike_相同ID即相同实例()
    {
        var a = MakeForIsLike("1", ResourcePlatforms.CurseForge, "A");
        var b = MakeForIsLike("1", ResourcePlatforms.Modrinth, "B");
        Assert.True(a.IsLike(b));
    }

    [Fact]
    public void IsLike_同平台不算跨站相同()
    {
        var a = MakeForIsLike("1", ResourcePlatforms.CurseForge, "A");
        var b = MakeForIsLike("2", ResourcePlatforms.CurseForge, "A");
        Assert.False(a.IsLike(b));
    }

    [Fact]
    public void IsLike_Drops相同反而判不相似_VB原样怪癖()
    {
        //VB 原实现：非光影且 Drops 有序相等 → False（与注释「要求 MC 版本一致」相反）；平移保留
        var a = MakeForIsLike("1", ResourcePlatforms.CurseForge, "A", drops: [210]);
        var b = MakeForIsLike("2", ResourcePlatforms.Modrinth, "A", drops: [210]);
        Assert.False(a.IsLike(b));
    }

    [Fact]
    public void IsLike_Drops不同且同名则相似()
    {
        var a = MakeForIsLike("1", ResourcePlatforms.CurseForge, "Same Name", drops: [210]);
        var b = MakeForIsLike("2", ResourcePlatforms.Modrinth, "Same Name", drops: [180, 210]);
        Assert.True(a.IsLike(b));
    }

    [Fact]
    public void IsLike_光影不检查Drops一致性()
    {
        ResourceConfig.WikiLookup = null;
        var a = Parse("""
        { "Tags": ["其他"], "Platform": 1, "Types": 8, "Slug": "s1", "Id": "1",
          "RawName": "BSL", "Description": "d", "Website": "https://example.com",
          "LastUpdate": "2026-01-01T00:00:00Z", "DownloadCount": 0, "Drops": [210] }
        """);
        var b = Parse("""
        { "Tags": ["其他"], "Platform": 2, "Types": 8, "Slug": "s2", "Id": "2",
          "RawName": "BSL", "Description": "d", "Website": "https://example.com",
          "LastUpdate": "2026-01-01T00:00:00Z", "DownloadCount": 0, "Drops": [210] }
        """);
        Assert.True(a.IsLike(b)); //Drops 相等但光影跳过该检查
    }

    [Fact]
    public void IsLike_加载器不同或更新超一周则不相似()
    {
        var a = MakeForIsLike("1", ResourcePlatforms.CurseForge, "A", loaders: ModLoaders.Forge, drops: [210]);
        var b = MakeForIsLike("2", ResourcePlatforms.Modrinth, "A", loaders: ModLoaders.Fabric, drops: [180]);
        Assert.False(a.IsLike(b));

        var c = MakeForIsLike("3", ResourcePlatforms.CurseForge, "A", drops: [210]);
        var d = MakeForIsLike("4", ResourcePlatforms.Modrinth, "A", drops: [180],
            lastUpdate: new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc)); //8 天
        Assert.False(c.IsLike(d));
    }

    [Fact]
    public void IsLike_Slug字母数字部分相同视为相似()
    {
        var a = MakeForIsLike("1", ResourcePlatforms.CurseForge, "Totally Different", drops: [210], slug: "technical-enchant");
        var b = MakeForIsLike("2", ResourcePlatforms.Modrinth, "Another Name", drops: [180], slug: "technical_enchant");
        Assert.True(a.IsLike(b)); //GetRaw("technical-enchant") == GetRaw("technical_enchant")
    }

    [Fact]
    public void IsLike_相似后共享百科条目()
    {
        var a = MakeForIsLike("1", ResourcePlatforms.CurseForge, "Same", drops: [210], slug: "a");
        var b = MakeForIsLike("2", ResourcePlatforms.Modrinth, "Same", drops: [180], slug: "b");
        ResourceConfig.WikiLookup = (s, p) => s == "a" ? new WikiInfo { Id = 1, ChineseName = "同名模组" } : null;
        Assert.Equal("Same", b.TranslatedName); //b 尚无百科
        Assert.True(b.IsLike(a));
        Assert.Equal("同名模组", b.TranslatedName); //WikiEntry 由 a 共享给 b
    }
}
