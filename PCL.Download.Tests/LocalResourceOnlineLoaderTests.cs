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

    [Fact]
    public void Load_ModrinthUpdateWithDifferentLoaders_IsIgnored()
    {
        var file = CreateModFile("loader-mismatch-1.0.jar");
        var sha1 = file.ModrinthHash;
        var api = new FakeResourceApi();

        // 本地文件是纯 Fabric，更新候选是 Fabric + Forge：加载器集合不完全一致 → 不能当作更新
        api.MapJson("api.modrinth.com/v2/version_files/update_individual", new JsonObject
        {
            [sha1] = VersionJson("1111111111111111111111111111111111111111", "P2", "mm-1.1.jar", "1.1",
                "2026-06-01T00:00:00Z", ["fabric", "forge"]),
        }.ToJsonString());
        api.MapJson("api.modrinth.com/v2/version_files", new JsonObject
        {
            [sha1] = VersionJson(sha1, "P2", "loader-mismatch-1.0.jar", "1.0", "2026-01-01T00:00:00Z"),
        }.ToJsonString());
        api.MapJson("api.modrinth.com/v2/projects?ids=", new JsonArray(ProjectJson("P2", "mm")).ToJsonString());

        var registry = new ResourceProviderRegistry([new ModrinthProvider(api)]);
        var report = new LocalResourceOnlineLoader(registry)
            .Load([file], _cache, new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric })!;

        Assert.Equal(1, report.Resolved);
        Assert.Equal(0, report.UpdatesFound);
        Assert.NotNull(file.Project);            // 工程信息照常回填
        Assert.Null(file.UpdateFile);            // 但不能给出加载器不一致的更新
        Assert.Empty(file.ChangelogUrls);
    }

    [Fact]
    public void Load_RequestFailed_DoesNotMarkCacheAsLoaded()
    {
        var file = CreateModFile("retry-1.0.jar");
        var sha1 = file.ModrinthHash;
        var context = new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric };

        // 工程信息拿得到，但更新查询炸掉（未注册的 URL 会让桩请求器抛错）
        var brokenApi = new FakeResourceApi();
        // 必须抢在 version_files 之前注册：update_individual 的 URL 同样包含 version_files
        brokenApi.Map(u => u.Contains("update_individual", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("模拟更新查询失败"));
        brokenApi.MapJson("api.modrinth.com/v2/version_files", new JsonObject
        {
            [sha1] = VersionJson(sha1, "P3", "retry-1.0.jar", "1.0", "2026-01-01T00:00:00Z"),
        }.ToJsonString());
        brokenApi.MapJson("api.modrinth.com/v2/projects?ids=", new JsonArray(ProjectJson("P3", "retry")).ToJsonString());

        var first = new LocalResourceOnlineLoader(new ResourceProviderRegistry([new ModrinthProvider(brokenApi)]))
            .Load([file], _cache, context)!;

        Assert.True(first.Failed);
        Assert.Equal(1, first.Resolved);
        Assert.False(file.OnlineDataLoaded);     // 有站点失败时不给「已加载」标记
        Assert.True(_cache.ContainsKey(LocalResourceOnlineLoader.CacheKey(file, context)));

        // 下一次必须重试，不能因为「6 小时内」就跳过
        var secondApi = new FakeResourceApi();
        secondApi.MapJson("api.modrinth.com/v2/version_files", "{}");
        var second = new LocalResourceOnlineLoader(new ResourceProviderRegistry([new ModrinthProvider(secondApi)]))
            .Load([file], _cache, context)!;

        Assert.Equal(1, second.Pending);
        Assert.NotEmpty(secondApi.Requests);
    }

    [Fact]
    public void Load_Canceled_ReturnsNullAndStops()
    {
        var file = CreateModFile("cancel-1.0.jar");
        var api = new FakeResourceApi();

        var report = new LocalResourceOnlineLoader(new ResourceProviderRegistry([new ModrinthProvider(api)]))
            .Load([file], _cache, new OnlineLoadContext { GameVersion = "1.20.1", Loaders = ModLoaders.Fabric },
                isCancelled: () => true);

        Assert.Null(report);
        Assert.Empty(api.Requests);   // 第一条就开始检查，一个请求都不该发
    }

    [Fact]
    public void PrimeHashes_OverridesFileDerivedFingerprints()
    {
        var file = CreateModFile("primed-1.0.jar");
        var reference = new LocalResourceFile(file.File.FullName);
        var realSha1 = reference.ModrinthHash;
        var realCurseForge = reference.CurseForgeHash;

        // 宿主（VB）侧算好的指纹必须被原样采用，否则每次启动都要重读全部 Mod 文件
        var primed = new LocalResourceFile(file.File.FullName);
        primed.PrimeHashes("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", 12345u);

        Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", primed.ModrinthHash);
        Assert.Equal(12345u, primed.CurseForgeHash);
        Assert.NotEqual(realSha1, primed.ModrinthHash);
        Assert.NotEqual(realCurseForge, primed.CurseForgeHash);

        // 传 null 表示「不覆盖」，先灌入的值也不会被后来的覆盖（先到先得）
        primed.PrimeHashes(null, null);
        primed.PrimeHashes("cafebabe", 999u);
        Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", primed.ModrinthHash);
        Assert.Equal(12345u, primed.CurseForgeHash);
    }

    // ---- 夹具 ----

    private LocalResourceFile CreateModFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, [.. Enumerable.Range(0, 256).Select(i => (byte)i)]);
        return new LocalResourceFile(path);
    }

    private static JsonObject VersionJson(string sha1, string projectId, string fileName, string version, string publishedAt,
        string[]? loaders = null) => new()
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
        ["loaders"] = new JsonArray((loaders ?? ["fabric"]).Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
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
