using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// 在线反查与更新检测（VB <c>LocalResourceOnlineLoad</c> 平移，dev.4 §2.1 A5）测试。
/// 覆盖：Modrinth 分支回填工程/版本/更新、CurseForge 分支（含 Key 缺失降级跳过）、缓存时效。
/// </summary>
[Collection("Resource")]
public class LocalResourceOnlineLoaderTests : IDisposable
{
    private readonly string _root;
    private readonly JsonObject _cache = new();

    public LocalResourceOnlineLoaderTests()
    {
        ResourceProject.ClearCache();
        ResourceVersion.ClearCache();
        ResourceConfig.DebugMode = false;
        ResourceConfig.WikiLookup = null;
        ResourceConfig.WikiEntries = null;
        ResourceApi.Default = null;

        _root = Path.Combine(Path.GetTempPath(), "pcl-next-online-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理失败不影响测试结论 */ }
    }

    [Fact]
    public void Load_ModrinthBranch_FillsProjectAndDetectsUpdate()
    {
        var file = CreateModFile("xmod-1.0.jar");
        var sha1 = file.ModrinthHash;
        var api = new FakeResourceApi();

        // 注意顺序：update_individual 的 URL 也包含 version_files，必须先注册
        api.MapJson("api.modrinth.com/v2/version_files/update_individual", new JsonObject
        {
            [sha1] = VersionJson("1111111111111111111111111111111111111111", "P1", "xmod-1.1.jar", "1.1", "2026-06-01T00:00:00Z"),
        }.ToJsonString());
        api.MapJson("api.modrinth.com/v2/version_files", new JsonObject
        {
            [sha1] = VersionJson(sha1, "P1", "xmod-1.0.jar", "1.0", "2026-01-01T00:00:00Z"),
        }.ToJsonString());
        api.MapJson("api.modrinth.com/v2/projects?ids=", new JsonArray(ProjectJson("P1", "xmod")).ToJsonString());

        var registry = new ResourceProviderRegistry(
        [
            new ModrinthProvider(api),
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = null }),
        ]);
        var loader = new LocalResourceOnlineLoader(registry);

        var report = loader.Load([file], _cache, new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric });

        Assert.Equal(1, report.Pending);
        Assert.Equal(1, report.Resolved);
        Assert.Equal(1, report.UpdatesFound);
        Assert.False(report.Failed);
        Assert.False(report.CurseForgeUsed);                                  // 未配置 Key → 分支跳过
        Assert.Equal(ResourceProviderRegistry.CurseForgeDisabledNotice, report.Notice);

        Assert.NotNull(file.Project);
        Assert.Equal("X Mod", file.Project!.RawName);
        Assert.Equal("xmod-1.0.jar", file.ProjectVersion!.FileName);
        Assert.Equal("xmod-1.1.jar", file.UpdateFile!.FileName);
        Assert.Equal("https://modrinth.com/mod/P1/changelog?g=1.20.1", Assert.Single(file.ChangelogUrls));
        Assert.True(file.OnlineDataLoaded);

        // 缓存回写：键为「指纹 + 版本 + 加载器」，且含工程信息
        var key = LocalResourceOnlineLoader.CacheKey(file, new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric });
        Assert.True(_cache.ContainsKey(key));
        Assert.NotNull(_cache[key]!["Project"]);
    }

    [Fact]
    public void Load_FreshCacheSkipsNetwork()
    {
        var file = CreateModFile("cached-1.0.jar");
        var context = new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric };

        // 先用一次真实（假接口）加载填好缓存
        var firstApi = new FakeResourceApi();
        firstApi.MapJson("api.modrinth.com/v2/version_files/update_individual", "{}");
        firstApi.MapJson("api.modrinth.com/v2/version_files", new JsonObject
        {
            [file.ModrinthHash] = VersionJson(file.ModrinthHash, "P9", "cached-1.0.jar", "1.0", "2026-01-01T00:00:00Z"),
        }.ToJsonString());
        firstApi.MapJson("api.modrinth.com/v2/projects?ids=", new JsonArray(ProjectJson("P9", "cached")).ToJsonString());

        var firstRegistry = new ResourceProviderRegistry([new ModrinthProvider(firstApi)]);
        var first = new LocalResourceOnlineLoader(firstRegistry).Load([file], _cache, context);
        Assert.Equal(1, first.Pending);

        // 第二次：缓存仍新鲜（6 小时内），不应再联网
        var secondApi = new FakeResourceApi();
        var secondRegistry = new ResourceProviderRegistry([new ModrinthProvider(secondApi)]);
        var second = new LocalResourceOnlineLoader(secondRegistry).Load([file], _cache, context);

        Assert.Equal(0, second.Pending);
        Assert.Empty(secondApi.Requests);
        Assert.NotNull(file.Project); // 从缓存还原
    }

    [Fact]
    public void Load_CurseForgeBranch_DetectsUpdateAndBuildsChangelog()
    {
        var file = CreateModFile("jei-1.0.jar");
        var fingerprint = file.CurseForgeHash;
        var api = new FakeResourceApi();

        // 顺序：/v1/mods/files 是 /v1/mods 的前缀延伸，必须先注册
        api.MapJson("api.curseforge.com/v1/mods/files", """
        { "data": [ {
            "id": 222, "gameId": 432, "displayName": "1.1", "fileDate": "2026-06-01T00:00:00Z",
            "releaseType": 1, "downloadCount": 10, "fileName": "jei-1.1.jar", "fileLength": 200,
            "hashes": [ { "algo": 1, "value": "BB" } ],
            "downloadUrl": "https://edge.forgecdn.net/files/0/222/jei-1.1.jar",
            "gameVersions": [ "1.20.1", "Fabric" ] } ] }
        """);
        api.MapJson("api.curseforge.com/v1/mods", CurseForgeProjectJson("238222", "jei", 222).ToJsonString());
        api.MapJson("api.curseforge.com/v1/fingerprints/432", new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["exactMatches"] = new JsonArray(new JsonObject
                {
                    ["id"] = 238222,
                    ["file"] = new JsonObject
                    {
                        ["id"] = 111,
                        ["displayName"] = "1.0",
                        ["fileDate"] = "2026-01-01T00:00:00Z",
                        ["releaseType"] = 1,
                        ["downloadCount"] = 5,
                        ["fileName"] = "jei-1.0.jar",
                        ["fileLength"] = 100,
                        ["hashes"] = new JsonArray(new JsonObject { ["algo"] = 1, ["value"] = "AA" }),
                        ["downloadUrl"] = "https://edge.forgecdn.net/files/0/111/jei-1.0.jar",
                        ["gameVersions"] = new JsonArray("1.20.1", "Fabric"),
                        ["fileFingerprint"] = fingerprint,
                    },
                }),
            },
        }.ToJsonString());

        var registry = new ResourceProviderRegistry(
        [
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = "k" }),
        ]);
        var loader = new LocalResourceOnlineLoader(registry);

        var report = loader.Load([file], _cache, new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric });

        Assert.True(report.CurseForgeUsed);
        Assert.Null(report.Notice);
        Assert.Equal(1, report.Resolved);
        Assert.Equal(1, report.UpdatesFound);
        Assert.Equal("jei", file.Project!.Slug);
        Assert.Equal("jei-1.1.jar", file.UpdateFile!.FileName);
        Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/jei/files/222", Assert.Single(file.ChangelogUrls));
    }

    [Fact]
    public void Load_CurseForgeUnavailable_FallsBackWithoutThrowing()
    {
        var file = CreateModFile("degraded-1.0.jar");
        var api = new FakeResourceApi();
        // Modrinth 未命中（返回空映射），CurseForge 不可用 → 不应抛异常，只是没有结果
        api.MapJson("api.modrinth.com/v2/version_files", "{}");

        var registry = new ResourceProviderRegistry(
        [
            new ModrinthProvider(api),
            new CurseForgeProvider(api, new ResourceSigningOptions { CurseForgeApiKey = null }),
        ]);

        var report = new LocalResourceOnlineLoader(registry)
            .Load([file], _cache, new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric });

        Assert.False(report.CurseForgeUsed);
        Assert.NotNull(report.Notice);
        Assert.False(report.Failed);
        Assert.Equal(0, report.Resolved);
        Assert.DoesNotContain(api.Requests, r => r.Url.Contains("curseforge"));
    }

    // ---- 夹具 ----

    private LocalResourceFile CreateModFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, [.. Enumerable.Range(0, 256).Select(i => (byte)i)]);
        return new LocalResourceFile(path);
    }

    private static JsonObject VersionJson(string sha1, string projectId, string fileName, string version, string publishedAt) => new()
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
            ["url"] = $"https://cdn.modrinth.com/data/{projectId}/{fileName}",
            ["size"] = 100,
            ["hashes"] = new JsonObject { ["sha1"] = sha1 },
        }),
        ["loaders"] = new JsonArray("fabric"),
        ["game_versions"] = new JsonArray("1.20.1"),
    };

    private static JsonObject ProjectJson(string id, string slug) => new()
    {
        ["id"] = id,
        ["slug"] = slug,
        ["title"] = "X Mod",
        ["description"] = "d",
        ["project_type"] = "mod",
        ["downloads"] = 100,
        ["icon_url"] = "",
        ["team"] = "t",
        ["game_versions"] = new JsonArray("1.20.1"),
        ["loaders"] = new JsonArray("fabric"),
        ["categories"] = new JsonArray("utility"),
        ["date_modified"] = "2026-01-01T00:00:00Z",
    };

    private static JsonObject CurseForgeProjectJson(string id, string slug, int latestFileId) => new()
    {
        ["data"] = new JsonArray(new JsonObject
        {
            ["id"] = int.Parse(id),
            ["slug"] = slug,
            ["name"] = "Just Enough Items",
            ["summary"] = "s",
            ["links"] = new JsonObject { ["websiteUrl"] = $"https://www.curseforge.com/minecraft/mc-mods/{slug}/" },
            ["dateReleased"] = "2026-01-01T00:00:00Z",
            ["downloadCount"] = 100,
            ["isAvailable"] = true,
            ["logo"] = new JsonObject(),
            ["latestFiles"] = new JsonArray(),
            ["latestFilesIndexes"] = new JsonArray(new JsonObject
            {
                ["fileId"] = latestFileId,
                ["modLoader"] = 4,      // Fabric
                ["gameVersion"] = "1.20.1",
            }),
            ["categories"] = new JsonArray(),
        }),
    };
}
