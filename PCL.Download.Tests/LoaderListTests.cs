using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// 加载器列表解析与调度测试（Phase 4：OptiFine / Forge / NeoForge / LiteLoader / Fabric）。
/// <para/>全部走桩请求器，不依赖外网。
/// </summary>
public class LoaderListTests
{
    // ---- OptiFine ----

    [Fact]
    public void OptiFine_Html_ParsesDisplayNameAndForgeRequirement()
    {
        var api = new FakeTextApi().Map(LoaderListSources.OptiFineOfficialUrl, OptiFineHtml(12));
        var result = new LoaderListClient(api).FetchOptiFine(McVersionSourceMode.OfficialFirst);

        Assert.NotNull(result);
        Assert.Equal("OptiFine 官方源", result.SourceName);
        Assert.True(result.IsOfficial);
        Assert.Equal(12, result.Items.Count);

        var first = result.Items[0];
        Assert.Equal("1.12.2 C0", first.DisplayName);
        Assert.Equal("1.12.2", first.Inherit);
        Assert.Equal("OptiFine_1.12.2_HD_U_C0.jar", first.FileName);
        Assert.Equal("1.12.2-OptiFine_HD_U_C0", first.InstanceName);
        Assert.False(first.IsPreview);
        Assert.Equal("14.23.5.2860", first.RequiredForgeVersion);
        Assert.Equal("11/01/2020", first.ReleaseTime); // VB 原实现是 日/月/年
    }

    [Fact]
    public void OptiFine_Html_PreviewAndNoForgeSupport()
    {
        var api = new FakeTextApi().Map(LoaderListSources.OptiFineOfficialUrl, OptiFineHtml(10, preview: true));
        var result = new LoaderListClient(api).FetchOptiFine(McVersionSourceMode.OfficialFirst)!;

        Assert.Equal(10, result.Items.Count);
        Assert.All(result.Items, entry =>
        {
            Assert.True(entry.IsPreview);
            Assert.StartsWith("preview_OptiFine_", entry.FileName);
        });
        Assert.Equal("14.23.5.2860", result.Items[0].RequiredForgeVersion);
        Assert.Null(result.Items[^1].RequiredForgeVersion); // N/A → 不兼容
    }

    [Fact]
    public void OptiFine_Html_TooFewVersionsIsRejectedAndFallsBackToMirror()
    {
        var api = new FakeTextApi()
            .Map(LoaderListSources.OptiFineOfficialUrl, OptiFineHtml(3))
            .Map(LoaderListSources.OptiFineMirrorUrl, OptiFineJson(4));
        var result = new LoaderListClient(api).FetchOptiFine(McVersionSourceMode.OfficialFirst)!;

        Assert.Equal("BMCLAPI", result.SourceName);
        Assert.False(result.IsOfficial);
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public void OptiFine_Json_StripsTrailingZeroFromInherit()
    {
        var api = new FakeTextApi().Map(LoaderListSources.OptiFineMirrorUrl, OptiFineJson(2));
        var result = new LoaderListClient(api).FetchOptiFine(McVersionSourceMode.MirrorFirst)!;

        Assert.All(result.Items, item => Assert.Equal("1.7", item.Inherit));
        Assert.Equal("", result.Items[0].ReleaseTime); // BMCLAPI 源无发布时间
    }

    // ---- Forge 的 MC 版本列表 ----

    [Fact]
    public void ForgeGameVersions_Html_AlwaysIncludes124()
    {
        var api = new FakeTextApi().Map(LoaderListSources.ForgeGameListOfficialUrl, ForgeGameListHtml(10));
        var result = new LoaderListClient(api).FetchForgeGameVersions(McVersionSourceMode.OfficialFirst)!;

        Assert.Equal(12, result.Items.Count);
        Assert.Contains("1.10.2", result.Items);
        Assert.Contains("1.2.4", result.Items);   // 正则匹配不到，由代码补入
        Assert.Contains("1.7.10_pre4", result.Items);
    }

    [Fact]
    public void ForgeGameVersions_Json_TooFewIsRejected()
    {
        var api = new FakeTextApi()
            .Map(LoaderListSources.ForgeGameListOfficialUrl, ForgeGameListHtml(10))
            .Map(LoaderListSources.ForgeGameListMirrorUrl, """["1.12.2","1.7.10"]""");
        var result = new LoaderListClient(api).FetchForgeGameVersions(McVersionSourceMode.MirrorFirst)!;

        // 镜像只有 2 个版本 → 判为错误页面 → 加速后回到官方源
        Assert.Equal("Forge 官方源", result.SourceName);
        Assert.Equal(12, result.Items.Count);
    }

    // ---- Forge 版本列表 ----

    [Fact]
    public void ForgeVersions_Html_ParsesCategoryBranchHashAndRecommendation()
    {
        var api = new FakeTextApi().Map("index_1.12.2.html", ForgeVersionsHtml());
        var result = new LoaderListClient(api).FetchForgeVersions("1.12.2", null,
            McVersionSourceMode.OfficialFirst)!;

        Assert.Equal(2, result.Items.Count); // 第三项没有任何下载文件，被跳过

        var recommended = result.Items.Single(i => i.VersionName == "14.23.5.2860");
        Assert.True(recommended.IsRecommended);
        Assert.Equal("installer", recommended.Category);
        Assert.Equal("jar", recommended.FileExtension);
        Assert.Equal("AAAA", recommended.Hash);
        Assert.Equal("14.23.5.2860", recommended.FileVersion);

        var universal = result.Items.Single(i => i.VersionName == "10.13.4.1614");
        Assert.Equal("universal", universal.Category);
        Assert.Equal("zip", universal.FileExtension);
        Assert.Equal("10.13.4.1614-1.7.10", universal.FileVersion); // 分支来自文件名
        Assert.Equal("BBBB", universal.Hash);
        Assert.All(result.Items, item => Assert.Matches(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}$", item.ReleaseTime));
    }

    [Fact]
    public void ForgeVersions_Json_PrefersInstallerThenUniversalThenClient()
    {
        var api = new FakeTextApi().Map("/forge/minecraft/1.16.5", ForgeVersionsJson());
        var result = new LoaderListClient(api).FetchForgeVersions("1.16.5", null,
            McVersionSourceMode.MirrorFirst)!;

        Assert.Equal(3, result.Items.Count);

        var withInstaller = result.Items.Single(i => i.VersionName == "36.2.39");
        Assert.Equal("installer", withInstaller.Category);
        Assert.Equal("H1", withInstaller.Hash);

        var universalOnly = result.Items.Single(i => i.VersionName == "36.2.34");
        Assert.Equal("universal", universalOnly.Category);
        Assert.Equal("H2", universalOnly.Hash);

        var clientOnly = result.Items.Single(i => i.VersionName == "36.2.30");
        Assert.Equal("client", clientOnly.Category);
        Assert.Equal("H3", clientOnly.Hash);
        Assert.Equal("36.2.30-1.16.5", clientOnly.FileVersion);
    }

    [Fact]
    public void ForgeVersions_Json_InstallerBeatsUniversalInSameEntry()
    {
        // 同一版本同时挂了 installer 与 universal 时，安装编排要的是 installer.jar 的 MD5；
        // 这条跨分类优先级与 files 数组里的先后顺序无关（反向验证时发现原夹具没有覆盖）。
        var api = new FakeTextApi().Map("/forge/minecraft/1.16.5", ForgeVersionsMixedFilesJson());
        var result = new LoaderListClient(api).FetchForgeVersions("1.16.5", null,
            McVersionSourceMode.MirrorFirst)!;

        var installerFirst = result.Items.Single(i => i.VersionName == "36.2.40");
        Assert.Equal("installer", installerFirst.Category);
        Assert.Equal("I1", installerFirst.Hash);

        var installerLast = result.Items.Single(i => i.VersionName == "36.2.41");
        Assert.Equal("installer", installerLast.Category);
        Assert.Equal("I2", installerLast.Hash);
    }

    [Fact]
    public void ForgeVersions_Json_MarksRecommendedVersion()
    {
        var recommendedFile = Path.Combine(Path.GetTempPath(), $"forge-rec-{Guid.NewGuid():N}.json");
        File.WriteAllText(recommendedFile, """{ "1.16.5": "36.2.39" }""");
        try
        {
            var api = new FakeTextApi().Map("/forge/minecraft/1.16.5", ForgeVersionsJson());
            var result = new LoaderListClient(api).FetchForgeVersions("1.16.5", recommendedFile,
                McVersionSourceMode.MirrorFirst)!;

            Assert.True(result.Items.Single(i => i.VersionName == "36.2.39").IsRecommended);
            Assert.False(result.Items.Single(i => i.VersionName == "36.2.34").IsRecommended);
        }
        finally
        {
            File.Delete(recommendedFile);
        }
    }

    [Fact]
    public void ForgeVersions_Json_KeepsVersionBranchFixups()
    {
        var api = new FakeTextApi().Map("/forge/minecraft/1.7.10", ForgeLegacyVersionsJson());
        var result = new LoaderListClient(api).FetchForgeVersions("1.7.10", null,
            McVersionSourceMode.MirrorFirst)!;

        // 1.7.10 且分支为空的后期构建：VB 会把分支补成 1.7.10
        Assert.Equal("10.13.4.1614-1.7.10", result.Items.Single(i => i.VersionName == "10.13.4.1614").FileVersion);
        // 司马版本：分支被强制改成 1.8.9
        Assert.Equal("11.15.1.2318-1.8.9", result.Items.Single(i => i.VersionName == "11.15.1.2318").FileVersion);
    }

    // ---- NeoForge ----

    [Fact]
    public void NeoForge_NormalizesThreeVersionNameShapes()
    {
        var api = new FakeTextApi()
            .Map(LoaderListSources.NeoForgeOfficialLatestUrl, NeoForgeLatestJson())
            .Map(LoaderListSources.NeoForgeOfficialLegacyUrl, NeoForgeLegacyJson());
        var result = new LoaderListClient(api).FetchNeoForge(McVersionSourceMode.OfficialFirst)!;

        Assert.Equal("NeoForge 官方源", result.SourceName);
        Assert.DoesNotContain(result.Items, i => i.ApiName == "47.1.82"); // 不可下载，被过滤

        var modern = result.Items.Single(i => i.ApiName == "20.4.30-beta");
        Assert.Equal("1.20.4", modern.Inherit);
        Assert.Equal("20.4.30-beta", modern.VersionName);
        Assert.True(modern.IsBeta);

        var snapshot = result.Items.Single(i => i.ApiName == "26.1.0.0-alpha.1+snapshot-1");
        Assert.Equal("26.1-snapshot-1", snapshot.Inherit);

        var legacy = result.Items.Single(i => i.ApiName == "1.20.1-47.1.99");
        Assert.Equal("1.20.1", legacy.Inherit);
        Assert.Equal("47.1.99", legacy.VersionName);
        Assert.Equal(new Version("19.47.1.99"), legacy.Version);
        Assert.Equal("https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.99/forge-1.20.1-47.1.99",
            legacy.UrlBase);

        var craftmine = result.Items.Single(i => i.ApiName == "0.25w14craftmine.3-beta");
        Assert.Equal("25w14craftmine", craftmine.Inherit);
    }

    [Fact]
    public void NeoForge_SortsByVersionDescending()
    {
        var api = new FakeTextApi()
            .Map(LoaderListSources.NeoForgeOfficialLatestUrl, NeoForgeLatestJson())
            .Map(LoaderListSources.NeoForgeOfficialLegacyUrl, NeoForgeLegacyJson());
        var result = new LoaderListClient(api).FetchNeoForge(McVersionSourceMode.OfficialFirst)!;

        var versions = result.Items.Select(i => i.Version).ToList();
        for (var i = 1; i < versions.Count; i++) Assert.True(versions[i - 1] >= versions[i]);
    }

    // ---- LiteLoader ----

    [Fact]
    public void LiteLoader_ParsesEntriesAndKeepsRawToken()
    {
        var api = new FakeTextApi().Map(LoaderListSources.LiteLoaderOfficialUrl, LiteLoaderJson());
        var result = new LoaderListClient(api).FetchLiteLoader(McVersionSourceMode.OfficialFirst)!;

        Assert.Equal(3, result.Items.Count); // 1.5 / 1.6 被跳过

        var modern = result.Items.Single(i => i.Inherit == "1.12.2");
        Assert.Equal("liteloader-installer-1.12.2-00-SNAPSHOT.jar", modern.FileName);
        Assert.False(modern.IsLegacy);
        Assert.False(modern.IsPreview);
        Assert.Equal("MD5-1", modern.Md5);
        Assert.Matches(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}$", modern.ReleaseTime);
        // 安装编排要从原始 token 里取 tweakClass / libraries / version
        Assert.Contains("tweakClass", modern.RawJson);

        var legacy = result.Items.Single(i => i.Inherit == "1.7.10");
        Assert.True(legacy.IsLegacy);   // 1.7 及更早
        Assert.True(legacy.IsPreview);
        Assert.Equal("liteloader-installer-1.7.10-00-SNAPSHOT.jar", legacy.FileName);

        // 1.8 / 1.9 的文件名要补一段 .0（历史遗留命名），但不算远古版
        var old19 = result.Items.Single(i => i.Inherit == "1.9");
        Assert.False(old19.IsLegacy);
        Assert.Equal("liteloader-installer-1.9.0-00-SNAPSHOT.jar", old19.FileName);
    }

    // ---- Fabric ----

    [Fact]
    public void Fabric_ReturnsRawJsonForUi()
    {
        var api = new FakeTextApi().Map(LoaderListSources.FabricOfficialUrl, FabricJson());
        var result = new LoaderListClient(api).FetchFabric(McVersionSourceMode.OfficialFirst)!;

        Assert.Equal("Fabric 官方源", result.SourceName);
        Assert.Empty(result.Items);
        Assert.NotNull(result.RawJson);
        var root = JsonNode.Parse(result.RawJson!)!;
        Assert.NotNull(root["game"]);
        Assert.NotNull(root["loader"]);
        Assert.NotNull(root["installer"]);
    }

    [Fact]
    public void Fabric_MissingSectionIsRejectedAndFallsBackToMirror()
    {
        var api = new FakeTextApi()
            .Map(LoaderListSources.FabricOfficialUrl, """{ "game": [], "loader": [] }""")
            .Map(LoaderListSources.FabricMirrorUrl, FabricJson());
        var result = new LoaderListClient(api).FetchFabric(McVersionSourceMode.OfficialFirst)!;

        Assert.Equal("BMCLAPI", result.SourceName);
        Assert.False(result.IsOfficial);
    }

    // ---- 调度、加速与缓存 ----

    [Fact]
    public void Race_StartsNextCandidateImmediatelyWhenAllStartedFailed()
    {
        // 官方源 404（未注册路由即抛异常），镜像本应等到 5 秒后才启动；
        // VB 的 BeforeLoadersAllFailed 语义要求此时立即启动，否则用户白等。
        var api = new FakeTextApi().Map(LoaderListSources.LiteLoaderMirrorUrl, LiteLoaderJson());
        var clock = Stopwatch.StartNew();
        var result = new LoaderListClient(api).FetchLiteLoader(McVersionSourceMode.OfficialFirst);

        clock.Stop();
        Assert.NotNull(result);
        Assert.Equal("BMCLAPI", result.SourceName);
        Assert.True(clock.ElapsedMilliseconds < 4000,
            $"已启动源全部失败后应立即换源，实际耗时 {clock.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Race_AllSourcesFailedThrows()
    {
        var api = new FakeTextApi();
        var ex = Assert.Throws<ResourceRequestException>(() =>
            new LoaderListClient(api).FetchLiteLoader(McVersionSourceMode.MirrorFirst));
        Assert.Equal(ResourceRequestErrorKind.Network, ex.Kind);
    }

    [Fact]
    public void Cache_ServesSecondCallWithoutNetworkUntilForceRestart()
    {
        var api = new FakeTextApi().Map(LoaderListSources.LiteLoaderOfficialUrl, LiteLoaderJson());
        var client = new LoaderListClient(api);

        var first = client.FetchLiteLoader(McVersionSourceMode.OfficialFirst);
        var callsAfterFirst = api.Calls;
        var second = client.FetchLiteLoader(McVersionSourceMode.OfficialFirst);

        Assert.Same(first, second);
        Assert.Equal(callsAfterFirst, api.Calls);

        client.FetchLiteLoader(McVersionSourceMode.OfficialFirst, forceRestart: true);
        Assert.Equal(callsAfterFirst + 1, api.Calls);
    }

    [Fact]
    public void Cancel_ReturnsNullWithoutThrowing()
    {
        var api = new FakeTextApi().Map(LoaderListSources.LiteLoaderOfficialUrl, LiteLoaderJson());
        var result = new LoaderListClient(api).FetchLiteLoader(McVersionSourceMode.OfficialFirst,
            isCancelled: () => true);

        Assert.Null(result);
    }

    // ---- 夹具 ----

    /// <summary>生成 OptiFine 官方页（三列数量与文件名一一对应）。</summary>
    private static string OptiFineHtml(int count, bool preview = false)
    {
        var builder = new StringBuilder("<html><head><title>OptiFine Downloads</title></head><table>");
        for (var i = 0; i < count; i++)
        {
            var suffix = preview ? "pre" : $"C{i}";
            builder.Append($"<tr><td class='colDate'>{2020 + i}.0{(i % 9) + 1}.1{(i % 9) + 1}</td>")
                .Append("<td class='colForge'>")
                .Append(i == count - 1 ? "N/A" : "Forge 14.23.5.2860")
                .Append("</td><td><a href=\"adloadx?f=OptiFine_1.12.2_HD_U_")
                .Append(suffix)
                .Append(".jar\">Download</a></td></tr>");
        }
        return builder.Append("</table></html>").ToString();
    }

    /// <summary>生成 OptiFine BMCLAPI 列表（mcversion 带 .0 以覆盖去零逻辑）。</summary>
    private static string OptiFineJson(int count)
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append($$"""
                { "mcversion": "1.7.0", "type": "HD_U", "patch": "C{{i}}", "filename": "OptiFine_1.7.0_HD_U_C{{i}}.jar", "forge": "Forge 10.12.2.1121" }
                """);
        }
        return builder.Append(']').ToString();
    }

    /// <summary>生成 Forge 官方索引页（每条一个 Markdown 式链接）。</summary>
    private static string ForgeGameListHtml(int count)
    {
        var builder = new StringBuilder("<html><body><h1>Forge Downloads</h1><ul>");
        builder.Append("<li><a href=\"index_1.7.10_pre4.html\">1.7.10_pre4</a></li>");
        for (var i = 0; i < count; i++) builder.Append($"<li><a href=\"index_1.{i + 1}.2.html\">1.{i + 1}.2</a></li>");
        return builder.Append("</ul></body></html>").ToString();
    }

    /// <summary>生成 Forge 官方版本列表页：installer（推荐）、universal（带分支）、无下载项。</summary>
    private static string ForgeVersionsHtml()
    {
        const string builder = """
            <html><body><table>
            <td class="download-version fa promo-recommended"><div class="info"><a href="x">14.23.5.2860</a></div>
            <a href="../files/1.12.2/forge-1.12.2-14.23.5.2860-installer.jar" class="classifier-installer"><i class="i-installer"></i>Installer</a>
            <span class="download-time" title="2021-02-15 03:24:02"></span>
            <span class="download"><strong>MD5:</strong> AAAA</span></td>
            <td class="download-version fa promo"><div class="info"><a href="x">10.13.4.1614</a></div>
            <a href="../files/1.7.10/forge-1.7.10-10.13.4.1614-1.7.10-universal.jar" class="classifier-universal"><i class="i-universal"></i>Universal</a>
            <span class="download-time" title="2015-04-29 00:00:00"></span>
            <span class="download"><strong>MD5:</strong> BBBB</span></td>
            <td class="download-version fa promo"><div class="info"><a href="x">9.11.1.1345</a></div>
            <span class="download-time" title="2013-01-01 00:00:00"></span></td>
            <tr><td>padding padding padding padding padding padding padding padding</td></tr>
            <tr><td>padding padding padding padding padding padding padding padding</td></tr>
            <tr><td>padding padding padding padding padding padding padding padding</td></tr>
            <tr><td>padding padding padding padding padding padding padding padding</td></tr>
            <tr><td>padding padding padding padding padding padding padding padding</td></tr>
            <tr><td>padding padding padding padding padding padding padding padding</td></tr>
            <tr><td>padding padding padding padding padding padding padding padding</td></tr>
            </table></body></html>
            """;
        return builder;
    }

    /// <summary>生成 Forge BMCLAPI 版本列表：三种文件优先级各一条。</summary>
    private static string ForgeVersionsJson() => """
        [
          {
            "version": "36.2.39", "branch": null, "modified": "2021-10-03T12:34:56+0000",
            "files": [
              { "category": "client", "format": "zip", "hash": "H0" },
              { "category": "installer", "format": "jar", "hash": "H1" }
            ]
          },
          {
            "version": "36.2.34", "branch": null, "modified": "2021-08-01T00:00:00+0000",
            "files": [
              { "category": "universal", "format": "zip", "hash": "H2" },
              { "category": "client", "format": "zip", "hash": "H3" }
            ]
          },
          {
            "version": "36.2.30", "branch": "1.16.5", "modified": "2021-07-01T00:00:00+0000",
            "files": [
              { "category": "client", "format": "zip", "hash": "H3" }
            ]
          }
        ]
        """;

    /// <summary>同一版本同时挂 installer 与 universal，用于校验跨分类优先级与数组顺序无关。</summary>
    private static string ForgeVersionsMixedFilesJson() => """
        [
          {
            "version": "36.2.40", "branch": null, "modified": "2021-10-05T00:00:00+0000",
            "files": [
              { "category": "installer", "format": "jar", "hash": "I1" },
              { "category": "universal", "format": "zip", "hash": "U1" }
            ]
          },
          {
            "version": "36.2.41", "branch": null, "modified": "2021-10-06T00:00:00+0000",
            "files": [
              { "category": "universal", "format": "zip", "hash": "U2" },
              { "category": "installer", "format": "jar", "hash": "I2" }
            ]
          }
        ]
        """;

    /// <summary>生成 1.7.10 的 Forge 列表，覆盖分支补全与司马版本特例。</summary>
    private static string ForgeLegacyVersionsJson() => """
        [
          { "version": "10.13.4.1614", "branch": null, "modified": "2015-04-29T00:00:00+0000",
            "files": [ { "category": "universal", "format": "zip", "hash": "U1" } ] },
          { "version": "11.15.1.2318", "branch": null, "modified": "2016-01-01T00:00:00+0000",
            "files": [ { "category": "installer", "format": "jar", "hash": "U2" } ] }
        ]
        """;

    /// <summary>NeoForge 新版包（含现代版、快照版、craftmine 版）。</summary>
    private static string NeoForgeLatestJson()
        => """{ "isSnapshot": false, "versions": ["20.4.30-beta", "26.1.0.0-alpha.1+snapshot-1", "0.25w14craftmine.3-beta"] }"""
            .PadRight(200, ' ');

    /// <summary>NeoForge Legacy 包（1.20.1 与不可下载的 47.1.82）。</summary>
    private static string NeoForgeLegacyJson()
        => """{ "isSnapshot": false, "versions": ["1.20.1-47.1.99", "47.1.82"] }""".PadRight(200, ' ');

    /// <summary>LiteLoader 版本清单：正常版本、测试版、以及被跳过的 1.5/1.6。</summary>
    private static string LiteLoaderJson() => """
        {
          "versions": {
            "1.12.2": {
              "artefacts": {
                "com.mumfrey:liteloader": {
                  "latest": { "stream": "release", "version": "1.12.2", "md5": "MD5-1",
                              "timestamp": 1527000000, "tweakClass": "com.mumfrey.liteloader.launch.LiteLoaderTweaker",
                              "libraries": [] }
                }
              }
            },
            "1.7.10": {
              "snapshots": {
                "com.mumfrey:liteloader": {
                  "latest": { "stream": "snapshot", "version": "1.7.10", "md5": "MD5-2",
                              "timestamp": 1440000000, "tweakClass": "x", "libraries": [] }
                }
              }
            },
            "1.9": {
              "artefacts": {
                "com.mumfrey:liteloader": {
                  "latest": { "stream": "release", "version": "1.9", "md5": "MD5-3",
                              "timestamp": 1450000000, "tweakClass": "x", "libraries": [] }
                }
              }
            },
            "1.6.4": {
              "artefacts": { "com.mumfrey:liteloader": { "latest": { "stream": "release" } } }
            },
            "1.5.2": {
              "artefacts": { "com.mumfrey:liteloader": { "latest": { "stream": "release" } } }
            }
          }
        }
        """;

    /// <summary>Fabric 元数据（UI 直接读三个数组）。</summary>
    private static string FabricJson() => """
        {
          "game": [ { "version": "1.20.1", "stable": true } ],
          "loader": [ { "version": "0.15.11", "stable": true } ],
          "installer": [ { "version": "1.0.1", "stable": true } ]
        }
        """;

    /// <summary>
    /// 测试用请求器：按 URL 子串返回固定文本，并记录实际请求次数。
    /// 实现 <see cref="IDirectTextResourceApi"/>，因为加载器列表里既有 JSON 也有 HTML 页面。
    /// </summary>
    private sealed class FakeTextApi : IResourceApi, IDirectTextResourceApi
    {
        private readonly List<(string Match, string Body)> _routes = [];
        private int _calls;

        /// <summary>已发生的请求次数。</summary>
        public int Calls => Volatile.Read(ref _calls);

        public FakeTextApi Map(string urlPart, string body)
        {
            _routes.Add((urlPart, body));
            return this;
        }

        public string RequestDirectText(string url, TimeSpan timeout)
        {
            Interlocked.Increment(ref _calls);
            foreach (var (match, body) in _routes)
                if (url.Contains(match, StringComparison.Ordinal)) return body;
            throw new ResourceRequestException(ResourceRequestErrorKind.NotFound, url, $"HTTP 404 Not Found", 404);
        }

        public JsonNode Request(string url, HttpMethod? method = null, string? content = null, string? contentType = null)
            => JsonNode.Parse(RequestDirectText(url, TimeSpan.FromSeconds(1)))!;
    }
}
