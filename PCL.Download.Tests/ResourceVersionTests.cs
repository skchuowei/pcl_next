using System.Globalization;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// ResourceVersion（VB ResourceVersion.vb 平移）对拍测试：固定 JSON 样例按 VB 语义手工核算期望值。
/// </summary>
[Collection("Resource")]
public class ResourceVersionTests
{
    public ResourceVersionTests()
    {
        ResourceVersion.ClearCache();
        ResourceProject.ClearCache();
        ResourceConfig.DebugMode = false;
        ResourceConfig.WikiLookup = null;
        ResourceApi.Default = null;
    }

    private const string CfFileJson = """
    {
      "gameId": 432,
      "id": 3456789,
      "displayName": "\tFancy Mod  ",
      "fileDate": "2026-01-15T08:30:00Z",
      "releaseType": 1,
      "downloadCount": 12345,
      "fileName": "fancymod-1.2.3.jar",
      "fileLength": 1048576,
      "hashes": [ {"algo": 2, "value": "AABBCCDD"}, {"algo": 1, "value": "1122334455"} ],
      "downloadUrl": "https://edge.forgecdn.net/files/3456/789/fancymod-1.2.3.jar",
      "dependencies": [
        { "relationType": 3, "modId": 238222 },
        { "relationType": 3, "modId": 306612 },
        { "relationType": 3, "modId": 634179 },
        { "relationType": 2, "modId": 999 }
      ],
      "gameVersions": ["1.20.1", "Forge", "1.20-snapshot", "Fabric", "Unknown Thing"]
    }
    """;

    private static ResourceVersion Parse(string json, ResourceTypes type = ResourceTypes.Mod)
        => ResourceVersion.FromPlatformJson(JsonNode.Parse(json)!, type);

    [Fact]
    public void FromPlatformJson_CurseForge_字段解析与VB语义一致()
    {
        var v = Parse(CfFileJson);
        Assert.Equal("3456789", v.Id);
        Assert.Equal("Fancy Mod", v.Display); //去制表符 + Trim 空格
        Assert.Null(v.Version); //CurseForge 无版本号
        Assert.Equal(ResourceVersion.ReleaseTypes.Release, v.ReleaseType);
        Assert.Equal(12345, v.DownloadCount);
        Assert.Equal("fancymod-1.2.3.jar", v.FileName);
        Assert.Equal(1048576, v.Size);
        Assert.Equal("1122334455", v.Hash); //优先 algo=1（SHA1），小写
        Assert.Equal(DateTime.Parse("2026-01-15T08:30:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), v.ReleaseDate);
        Assert.True(v.DownloadAvailable);
    }

    [Fact]
    public void FromPlatformJson_CurseForge_下载地址生成镜像去重()
    {
        var v = Parse(CfFileJson);
        Assert.Equal(new[]
        {
            "https://mediafilez.forgecdn.net/files/3456/789/fancymod-1.2.3.jar",
            "https://edge.forgecdn.net/files/3456/789/fancymod-1.2.3.jar",
            "https://mod.mcimirror.top/files/3456/789/fancymod-1.2.3.jar",
        }, v.DownloadUrls);
    }

    [Fact]
    public void FromPlatformJson_CurseForge_无下载地址时按ID拆分构造()
    {
        //VB：CInt(Id.Substring(0,4)) / CInt(Id.Substring(4)) → https://edge.forgecdn.net/files/{前4位}/{其余}/{文件名}
        var json = CfFileJson.Replace("\"downloadUrl\": \"https://edge.forgecdn.net/files/3456/789/fancymod-1.2.3.jar\"", "\"downloadUrl\": \"\"")
            .Replace("\"id\": 3456789", "\"id\": 12345678");
        var v = Parse(json);
        Assert.Contains("https://edge.forgecdn.net/files/1234/5678/fancymod-1.2.3.jar", v.DownloadUrls!);
        Assert.Contains("https://mod.mcimirror.top/files/1234/5678/fancymod-1.2.3.jar", v.DownloadUrls!);

        //ID 位数较短时的边界：123456 → 1234 / 56
        var json2 = CfFileJson.Replace("\"downloadUrl\": \"https://edge.forgecdn.net/files/3456/789/fancymod-1.2.3.jar\"", "\"downloadUrl\": \"\"")
            .Replace("\"id\": 3456789", "\"id\": 123456");
        var v2 = Parse(json2);
        Assert.Contains("https://edge.forgecdn.net/files/1234/56/fancymod-1.2.3.jar", v2.DownloadUrls!);
    }

    [Fact]
    public void FromPlatformJson_CurseForge_依赖排除Fabric与Quilt_API()
    {
        var v = Parse(CfFileJson);
        //relationType=3 且 modId 不为 306612（Fabric API）/ 634179（Quilt API）
        Assert.Equal(new[] { "238222" }, v.ToCacheJson()["RawDependencies"]!.AsArray().Select(t => t!.GetValue<string>()).ToList());
    }

    [Fact]
    public void FromPlatformJson_CurseForge_游戏版本过滤排序与加载器识别()
    {
        var v = Parse(CfFileJson);
        //IsStandardFormat 过滤 → -snapshot 换为「 预览版」→ 按新→旧排序
        Assert.Equal(["1.20.1", "1.20 预览版"], v.GameVersions);
        Assert.Equal(ModLoaders.Forge | ModLoaders.Fabric, v.ModLoaders);
    }

    [Fact]
    public void FromPlatformJson_CurseForge_零尺寸视为不可用()
    {
        var json = CfFileJson.Replace("\"fileLength\": 1048576", "\"fileLength\": 0");
        var v = Parse(json);
        Assert.Equal(-1, v.Size); //#9104：CurseForge 可能错误地返回 0 尺寸
    }

    [Fact]
    public void FromPlatformJson_CurseForge_仅SHA256哈希时回落到MD5()
    {
        var json = CfFileJson.Replace("[ {\"algo\": 2, \"value\": \"AABBCCDD\"}, {\"algo\": 1, \"value\": \"1122334455\"} ]",
            "[ {\"algo\": 2, \"value\": \"AABBCCDD\"} ]");
        var v = Parse(json);
        Assert.Equal("aabbccdd", v.Hash); //无 algo=1 时取 algo=2（MD5）
    }

    [Fact]
    public void FromPlatformJson_CurseForge_无标准游戏版本时视为未知版本()
    {
        var json = CfFileJson.Replace("\"gameVersions\": [\"1.20.1\", \"Forge\", \"1.20-snapshot\", \"Fabric\", \"Unknown Thing\"]",
            "\"gameVersions\": [\"Forge\", \"Fabric\"]");
        var v = Parse(json);
        Assert.Equal(["未知版本"], v.GameVersions);
    }

    [Fact]
    public void FromPlatformJson_CurseForge_整合包只保留最新游戏版本()
    {
        var json = CfFileJson.Replace("\"gameVersions\": [\"1.20.1\", \"Forge\", \"1.20-snapshot\", \"Fabric\", \"Unknown Thing\"]",
            "\"gameVersions\": [\"1.20.1\", \"1.19.2\", \"1.20.2\"]");
        var v = Parse(json, ResourceTypes.ModPack);
        Assert.Equal(["1.20.2"], v.GameVersions); //新→旧排序后只留首个
    }

    private const string MrVersionJson = """
    {
      "project_id": "P7dR8mSHX",
      "name": "Cool Mod (Fabric)",
      "version_number": "2.0.1",
      "date_published": "2026-02-01T00:00:00Z",
      "version_type": "beta",
      "downloads": 777,
      "files": [ { "filename": "coolmod.jar", "url": "https://cdn.modrinth.com/data/AbC/coolmod.jar", "size": 2048, "hashes": { "sha1": "AABB", "sha512": "xxyy" } } ],
      "loaders": ["fabric"],
      "dependencies": [
        { "dependency_type": "required", "project_id": "P7dR8mSH" },
        { "dependency_type": "required", "project_id": "AAbbCC" },
        { "dependency_type": "optional", "project_id": "xx" },
        { "dependency_type": "required", "project_id": "" }
      ],
      "game_versions": ["1.21.1", "1.21.2", "24w14a"]
    }
    """;

    [Fact]
    public void FromPlatformJson_Modrinth_字段解析与VB语义一致()
    {
        var v = Parse(MrVersionJson);
        Assert.Equal("P7dR8mSHX", v.Id); //project_id 优先于 id
        Assert.Equal("Cool Mod (Fabric)", v.Display);
        Assert.Equal("2.0.1", v.Version);
        Assert.Equal(ResourceVersion.ReleaseTypes.Beta, v.ReleaseType);
        Assert.Equal(777, v.DownloadCount);
        Assert.Equal("coolmod.jar", v.FileName);
        Assert.Equal(2048, v.Size);
        Assert.Equal("aabb", v.Hash); //sha1 小写
        Assert.Equal(new[]
        {
            "https://cdn.modrinth.com/data/AbC/coolmod.jar",
            "https://mod.mcimirror.top/data/AbC/coolmod.jar",
        }, v.DownloadUrls);
        //required 且非 Fabric/Quilt API 且非空
        Assert.Equal(new[] { "AAbbCC" }, v.ToCacheJson()["RawDependencies"]!.AsArray().Select(t => t!.GetValue<string>()).ToList());
    }

    [Fact]
    public void FromPlatformJson_Modrinth_游戏版本排序与快照回退()
    {
        var v = Parse(MrVersionJson);
        Assert.Equal(["1.21.2", "1.21.1"], v.GameVersions); //新→旧
        Assert.Equal(ModLoaders.Fabric, v.ModLoaders);
    }

    [Fact]
    public void FromPlatformJson_Modrinth_纯周快照时使用原始快照名()
    {
        var json = MrVersionJson.Replace("\"game_versions\": [\"1.21.1\", \"1.21.2\", \"24w14a\"]",
            "\"game_versions\": [\"23w13a\"]");
        var v = Parse(json);
        Assert.Equal(["23w13a"], v.GameVersions);
    }

    [Fact]
    public void FromPlatformJson_Modrinth_beta1版本标为远古版本()
    {
        var json = MrVersionJson.Replace("\"game_versions\": [\"1.21.1\", \"1.21.2\", \"24w14a\"]",
            "\"game_versions\": [\"b1.7.3\"]");
        var v = Parse(json);
        Assert.Equal(["远古版本"], v.GameVersions);
    }

    [Fact]
    public void FromPlatformJson_Modrinth_预览版标注()
    {
        var json = MrVersionJson.Replace("\"game_versions\": [\"1.21.1\", \"1.21.2\", \"24w14a\"]",
            "\"game_versions\": [\"1.20-pre1\", \"1.20\"]");
        var v = Parse(json);
        //1.20-pre1 → "1.20 预览版"；排序：正式版 1.20 新于 1.20 预览版
        Assert.Equal(["1.20", "1.20 预览版"], v.GameVersions);
    }

    [Theory]
    [InlineData("[\"paper\"]", ResourceTypes.Mod, ResourceTypes.Plugin)] //纯服务端 → Plugin
    [InlineData("[\"paper\", \"fabric\"]", ResourceTypes.Mod, ResourceTypes.Mod)] //含加载器 → Mod 覆盖（VB 原语义）
    [InlineData("[\"datapack\"]", ResourceTypes.Mod, ResourceTypes.DataPack)]
    [InlineData("[\"paper\", \"datapack\"]", ResourceTypes.Mod, ResourceTypes.DataPack)]
    [InlineData("[\"fabric\"]", ResourceTypes.DataPack, ResourceTypes.Mod)] //数据包工程带加载器 → Mod 覆盖
    [InlineData("[\"fabric\"]", ResourceTypes.ModPack, ResourceTypes.ModPack)] //非 Mod/数据包不改类别（#8377）
    [InlineData("[\"fabric\"]", ResourceTypes.Shader, ResourceTypes.Shader)]
    public void FromPlatformJson_Modrinth_类别重分类(string loaders, ResourceTypes expectedType, ResourceTypes actualType)
    {
        var json = MrVersionJson.Replace("\"loaders\": [\"fabric\"]", $"\"loaders\": {loaders}");
        var v = Parse(json, expectedType);
        Assert.Equal(actualType, v.ResourceType);
    }

    [Theory]
    [InlineData("release", ResourceVersion.ReleaseTypes.Release)]
    [InlineData("beta", ResourceVersion.ReleaseTypes.Beta)]
    [InlineData("alpha", ResourceVersion.ReleaseTypes.Alpha)]
    [InlineData("nightly", ResourceVersion.ReleaseTypes.Alpha)] //未知类型按 Alpha（VB 原语义）
    public void FromPlatformJson_Modrinth_发布状态映射(string versionType, ResourceVersion.ReleaseTypes expected)
    {
        var json = MrVersionJson.Replace("\"version_type\": \"beta\"", $"\"version_type\": \"{versionType}\"");
        Assert.Equal(expected, Parse(json).ReleaseType);
    }

    [Theory]
    [InlineData(false, "正式版", "测试版", "早期测试版")]
    [InlineData(true, "正式版", "Beta 版", "Alpha 版")]
    public void ReleaseTypeDisplay_随调试模式变化(bool debug, string release, string beta, string alpha)
    {
        ResourceConfig.DebugMode = debug;
        Assert.Equal(release, new ResourceVersion { ReleaseType = ResourceVersion.ReleaseTypes.Release }.ReleaseTypeDisplay);
        Assert.Equal(beta, new ResourceVersion { ReleaseType = ResourceVersion.ReleaseTypes.Beta }.ReleaseTypeDisplay);
        Assert.Equal(alpha, new ResourceVersion { ReleaseType = ResourceVersion.ReleaseTypes.Alpha }.ReleaseTypeDisplay);
    }

    [Fact]
    public void 缓存Json_往返保持全部字段()
    {
        var v = Parse(CfFileJson);
        var json1 = v.ToCacheJson().ToJsonString();
        var v2 = ResourceVersion.FromCacheJson(JsonNode.Parse(json1)!);
        Assert.Equal(json1, v2.ToCacheJson().ToJsonString());
    }

    [Fact]
    public void ParseCurseForgeDownloadUrls_五种地址修正去重()
    {
        //edge → mediafilez；重复项去除
        Assert.Equal(new[]
        {
            "https://mediafilez.forgecdn.net/files/1/2/a.jar",
            "https://edge.forgecdn.net/files/1/2/a.jar",
        }, ResourceVersion.ParseCurseForgeDownloadUrls("https://edge.forgecdn.net/files/1/2/a.jar"));

        //media → mediafilez，另含 media→edge 变体
        Assert.Equal(new[]
        {
            "https://mediafilez.forgecdn.net/files/1/2/a.jar",
            "https://media.forgecdn.net/files/1/2/a.jar",
            "https://edge.forgecdn.net/files/1/2/a.jar",
        }, ResourceVersion.ParseCurseForgeDownloadUrls("https://media.forgecdn.net/files/1/2/a.jar"));

        //overwolf 域名还原为 forgecdn
        Assert.Equal(new[]
        {
            "https://foo.example.com/.forgecdn.net/files/1/2/a.jar",
            "https://foo.example.com/-service.overwolf.wtf/files/1/2/a.jar",
        }, ResourceVersion.ParseCurseForgeDownloadUrls("https://foo.example.com/-service.overwolf.wtf/files/1/2/a.jar"));
    }

    [Fact]
    public void ParseModrinthTrackArguments_钉住VB原样怪癖()
    {
        //怪癖 1：无追踪参数时仍附加 "?"（VB 未判断空参数串）
        Assert.Equal(["https://cdn.modrinth.com/a.jar?"],
            ResourceVersion.ParseModrinthTrackArguments(["https://cdn.modrinth.com/a.jar"]).ToList());
        //怪癖 2：非 MR CDN 的 URL 会额外产出一份不带参数的副本（VB 的 If 后无 Else）
        Assert.Equal(new[]
        {
            "https://edge.forgecdn.net/a.jar",
            "https://edge.forgecdn.net/a.jar?",
        }, ResourceVersion.ParseModrinthTrackArguments(["https://edge.forgecdn.net/a.jar"]).ToList());
    }

    [Fact]
    public void ParseModrinthTrackArguments_参数拼装与已有查询串()
    {
        var result = ResourceVersion.ParseModrinthTrackArguments(
            ["https://cdn.modrinth.com/a.jar", "https://cdn.modrinth.com/b.jar?x=1"],
            ResourceVersion.DownloadReason.Update, "1.20.1", ModLoaders.Fabric).ToList();
        Assert.Equal(new[]
        {
            "https://cdn.modrinth.com/a.jar?mr_download_reason=update&mr_game_version=1.20.1&mr_loader=fabric",
            "https://cdn.modrinth.com/b.jar?x=1&mr_download_reason=update&mr_game_version=1.20.1&mr_loader=fabric",
        }, result);
    }

    // ---------- FromProjectId（联网 + 缓存 + 依赖） ----------

    private const string CfProject999Json = """
    {
      "id": 999, "slug": "father", "name": "Father Mod", "summary": "s",
      "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/father" },
      "dateReleased": "2026-01-10T00:00:00Z", "downloadCount": 1
    }
    """;

    private const string CfProject238222Json = """
    {
      "id": 238222, "slug": "jei", "name": "JEI", "summary": "s2",
      "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/jei" },
      "dateReleased": "2026-01-11T00:00:00Z", "downloadCount": 2
    }
    """;

    private const string CfFileWithDepJson = """
    {
      "gameId": 432, "id": 555, "displayName": "F1", "fileDate": "2026-01-12T00:00:00Z",
      "releaseType": 1, "downloadCount": 3, "fileName": "f1.jar", "fileLength": 10,
      "hashes": [ {"algo": 1, "value": "aa"} ],
      "downloadUrl": "https://edge.forgecdn.net/files/0/555/f1.jar",
      "dependencies": [ { "relationType": 3, "modId": 238222 } ],
      "gameVersions": ["1.20.1"]
    }
    """;

    [Fact]
    public void FromProjectId_CurseForge_分页缺失重试并递增pageSize()
    {
        var api = new FakeResourceApi();
        api.MapJson("api.curseforge.com/v1/mods/999/files?pageSize=10000",
            """{ "pagination": { "resultCount": 1, "totalCount": 2 }, "data": [ ] }"""); //缺失 → 触发重试
        api.MapJson("api.curseforge.com/v1/mods/999/files?pageSize=10001",
            """{ "pagination": { "resultCount": 1, "totalCount": 1 }, "data": [ """ + CfFileWithDepJson + """ ] }""");
        api.MapJsonExact("https://api.curseforge.com/v1/mods/999",
            "{ \"data\": " + CfProject999Json + " }");
        api.MapJsonExact("https://api.curseforge.com/v1/mods",
            "{ \"data\": [ " + CfProject238222Json + " ] }"); //批量前置查询

        var versions = ResourceVersion.FromProjectId("999", ResourcePlatforms.CurseForge, api: api);
        Assert.Single(versions);
        Assert.Equal("555", versions[0].Id);
        //重试语义：pageSize 每次 +1（#6224）
        Assert.Contains(api.Requests, r => r.Url.EndsWith("pageSize=10000", StringComparison.Ordinal));
        Assert.Contains(api.Requests, r => r.Url.EndsWith("pageSize=10001", StringComparison.Ordinal));
    }

    [Fact]
    public void FromProjectId_CurseForge_三轮分页均缺失则抛异常()
    {
        var api = new FakeResourceApi();
        api.MapJsonExact("https://api.curseforge.com/v1/mods/999",
            "{ \"data\": " + CfProject999Json + " }");
        api.Map(u => u.Contains("files?pageSize=", StringComparison.Ordinal),
            _ => JsonNode.Parse("""{ "pagination": { "resultCount": 1, "totalCount": 2 }, "data": [ ] }""")!);
        var ex = Assert.Throws<Exception>(() => ResourceVersion.FromProjectId("999", ResourcePlatforms.CurseForge, api: api));
        Assert.Equal("CurseForge 返回的资源版本列表存在缺失", ex.Message);
        Assert.Equal(3, api.Requests.Count(r => r.Url.Contains("files?pageSize=", StringComparison.Ordinal)));
    }

    [Fact]
    public void FromProjectId_加载前置并去重()
    {
        var api = new FakeResourceApi();
        api.MapJsonExact("https://api.curseforge.com/v1/mods/999",
            "{ \"data\": " + CfProject999Json + " }");
        api.Map(u => u.Contains("/mods/999/files?pageSize=", StringComparison.Ordinal),
            _ => JsonNode.Parse("""{ "pagination": { "resultCount": 1, "totalCount": 1 }, "data": [ """ + CfFileWithDepJson + """ ] }""")!);
        api.MapJsonExact("https://api.curseforge.com/v1/mods",
            "{ \"data\": [ " + CfProject238222Json + " ] }");

        var versions = ResourceVersion.FromProjectId("999", ResourcePlatforms.CurseForge, api: api);
        Assert.Equal(new[] { "238222" }, versions[0].Dependencies);

        //二次调用走缓存：不再发起任何请求
        int before = api.Requests.Count;
        var again = ResourceVersion.FromProjectId("999", ResourcePlatforms.CurseForge, api: api);
        Assert.Equal(before, api.Requests.Count);
        Assert.Same(versions, again);
    }

    [Fact]
    public void FromProjectId_不加载前置时依赖为空()
    {
        var api = new FakeResourceApi();
        api.MapJsonExact("https://api.curseforge.com/v1/mods/999",
            "{ \"data\": " + CfProject999Json + " }");
        api.Map(u => u.Contains("/mods/999/files?pageSize=", StringComparison.Ordinal),
            _ => JsonNode.Parse("""{ "pagination": { "resultCount": 1, "totalCount": 1 }, "data": [ """ + CfFileWithDepJson + """ ] }""")!);
        var versions = ResourceVersion.FromProjectId("999", ResourcePlatforms.CurseForge, loadDependencies: false, api: api);
        Assert.Empty(versions[0].Dependencies);
    }

    [Fact]
    public void FromProjectId_Modrinth_直接返回版本数组()
    {
        var api = new FakeResourceApi();
        api.MapJsonExact("https://api.modrinth.com/v2/project/P1",
            """
            { "id": "P1", "slug": "m", "title": "M", "description": "d", "project_type": "mod",
              "downloads": 1, "icon_url": "", "game_versions": ["1.20.1"], "loaders": ["fabric"], "categories": ["utility"] }
            """);
        api.Map(u => u.Contains("/version", StringComparison.Ordinal),
            _ => JsonNode.Parse("[ " + MrVersionJson.Replace("\"project_id\": \"P7dR8mSHX\"", "\"id\": \"V1\"") + " ]")!);
        var versions = ResourceVersion.FromProjectId("P1", ResourcePlatforms.Modrinth, loadDependencies: false, api: api);
        Assert.Single(versions);
        Assert.Equal("V1", versions[0].Id);
    }

    [Fact]
    public void FromProjectId_拒绝多平台()
        => Assert.Throws<ArgumentException>(() => ResourceVersion.FromProjectId("1", ResourcePlatforms.Any, api: new FakeResourceApi()));

    [Fact]
    public void FromProjectId_未注入请求器时抛异常()
        => Assert.Throws<InvalidOperationException>(() => ResourceVersion.FromProjectId("1", ResourcePlatforms.CurseForge));
}
