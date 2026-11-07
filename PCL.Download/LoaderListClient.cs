namespace PCL.Download;

/// <summary>
/// 加载器列表获取器（平移自 VB <c>ModDownload</c> 的 <c>DlOptiFineListMain</c> 等主加载器）。
/// <para>
/// 职责边界：本类负责「按版本列表源偏好排期候选 → 取文本 → 交给解析器」，不参与 UI。
/// VB 侧保留 <c>LoaderTask</c> 外壳与结果结构，只是为了不改动 UI 契约。
/// </para>
/// <para>
/// 缓存语义对齐 VB 的子加载器复用：同一个 <see cref="LoaderListClient"/> 实例内，同一列表
/// （含 MC 版本参数）只会真正联网一次；<paramref name="forceRestart"/> 为 true 时绕过缓存
/// （对应 VB 的 <c>Loader.IsForceRestarting</c>）。
/// </para>
/// </summary>
public sealed class LoaderListClient
{
    /// <summary>一个列表的取数规格：地址、展示名与解析方式，均按「是否官方源」分支。</summary>
    private sealed record Spec<T>(string Kind, string Label,
        Func<bool, string> Url, Func<bool, string> SourceName,
        Func<bool, string, (List<T> Items, string? RawJson)> Parse);

    private readonly IResourceApi? _api;
    private readonly Dictionary<string, object> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheGate = new();

    /// <summary>
    /// 构造获取器。
    /// </summary>
    /// <param name="api">
    /// 请求器；null 时在每次取数时读取 <see cref="ResourceApi.Default"/>。
    /// 延迟解析的原因：VB 侧的获取器是模块级单例，构造时宿主可能尚未注入默认请求器。
    /// </param>
    public LoaderListClient(IResourceApi? api = null) => _api = api;

    /// <summary>清空进程内缓存（对应 VB 手动刷新全部版本列表的场景）。</summary>
    public void ClearCache()
    {
        lock (_cacheGate) _cache.Clear();
    }

    // ---- OptiFine ----

    private static readonly Spec<OptiFineListEntry> OptiFineSpec = new("OptiFine", "OptiFine 版本列表",
        official => official ? LoaderListSources.OptiFineOfficialUrl : LoaderListSources.OptiFineMirrorUrl,
        official => official ? "OptiFine 官方源" : "BMCLAPI",
        (official, text) => official
            ? (LoaderListParsers.OptiFineFromHtml(text, LoaderListSources.OptiFineOfficialUrl), null)
            : (LoaderListParsers.OptiFineFromJson(text, LoaderListSources.OptiFineMirrorUrl), null));

    /// <summary>获取 OptiFine 版本列表。</summary>
    /// <param name="mode">版本列表源偏好（VB 设置项 <c>ToolDownloadVersion</c>）。</param>
    /// <param name="forceRestart">是否忽略缓存。</param>
    /// <param name="isCancelled">取消检查；返回 true 时返回 null。</param>
    public LoaderListResult<OptiFineListEntry>? FetchOptiFine(McVersionSourceMode mode, bool forceRestart = false,
        Func<bool>? isCancelled = null)
        => FetchCore(OptiFineSpec, mode, forceRestart, "", isCancelled);

    /// <summary>
    /// 只从 BMCLAPI 获取 OptiFine 版本列表。
    /// <para>
    /// 调用方需要「全量且稳定」的版本清单（官方页会删掉旧预览版，导致已安装实例或整合包的
    /// OptiFine 条目反查不到），因此这里显式指定单一镜像源，不受版本列表源设置影响。
    /// </para>
    /// </summary>
    /// <param name="forceRestart">是否忽略缓存。</param>
    /// <param name="isCancelled">取消检查；返回 true 时返回 null。</param>
    public LoaderListResult<OptiFineListEntry>? FetchOptiFineMirror(bool forceRestart = false,
        Func<bool>? isCancelled = null)
        => FetchCore(OptiFineSpec, McVersionSourceMode.MirrorFirst, forceRestart, "", isCancelled,
            [(false, 0)], "mirror");

    // ---- Forge 的 MC 版本列表 ----

    /// <summary>获取 Forge 支持的 MC 版本名列表。</summary>
    public LoaderListResult<string>? FetchForgeGameVersions(McVersionSourceMode mode, bool forceRestart = false,
        Func<bool>? isCancelled = null)
        => FetchCore(new Spec<string>("ForgeGameVersions", "Forge MC 版本列表",
                official => official
                    ? LoaderListSources.ForgeGameListOfficialUrl
                    : LoaderListSources.ForgeGameListMirrorUrl,
                official => official ? "Forge 官方源" : "BMCLAPI",
                (official, text) => official
                    ? (LoaderListParsers.ForgeGameVersionsFromHtml(text, LoaderListSources.ForgeGameListOfficialUrl), null)
                    : (LoaderListParsers.ForgeGameVersionsFromJson(text, LoaderListSources.ForgeGameListMirrorUrl), null)),
            mode, forceRestart, "", isCancelled);

    // ---- Forge 版本列表（按 MC 版本） ----

    /// <summary>获取某个 MC 版本下的 Forge 版本列表。</summary>
    /// <param name="mcVersion">MC 版本名。</param>
    /// <param name="forgeRecommendedJsonPath">
    /// 推荐版本缓存文件（VB 的 <c>Cache/ForgeRecommendedList.json</c>）；不存在或缺少该项时不影响列表，
    /// 只是无法标记推荐版本。
    /// </param>
    /// <param name="mode">版本列表源偏好。</param>
    /// <param name="forceRestart">是否忽略缓存。</param>
    /// <param name="isCancelled">取消检查；返回 true 时返回 null。</param>
    public LoaderListResult<ForgeVersionEntry>? FetchForgeVersions(string mcVersion,
        string? forgeRecommendedJsonPath, McVersionSourceMode mode, bool forceRestart = false,
        Func<bool>? isCancelled = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcVersion);

        var officialUrl = LoaderListSources.ForgeVersionsOfficialUrl(mcVersion);
        var mirrorUrl = LoaderListSources.ForgeVersionsMirrorUrl(mcVersion);
        var recommended = ReadForgeRecommended(forgeRecommendedJsonPath, mcVersion);

        return FetchCore(new Spec<ForgeVersionEntry>("ForgeVersions", $"Forge 版本列表（{mcVersion}）",
                official => official ? officialUrl : mirrorUrl,
                official => official ? "Forge 官方源" : "BMCLAPI",
                (official, text) => official
                    ? (LoaderListParsers.ForgeVersionsFromHtml(text, mcVersion, officialUrl), null)
                    : (LoaderListParsers.ForgeVersionsFromJson(text, mcVersion, recommended, mirrorUrl), null)),
            mode, forceRestart, mcVersion, isCancelled);
    }

    // ---- NeoForge ----

    /// <summary>获取 NeoForge 版本列表。</summary>
    public LoaderListResult<NeoForgeListEntry>? FetchNeoForge(McVersionSourceMode mode, bool forceRestart = false,
        Func<bool>? isCancelled = null)
    {
        // NeoForge 每个源要连续取两份（新版 + Legacy），且都以两份原文一起做正则扫描
        (List<NeoForgeListEntry> Items, string? RawJson) ParsePair(bool official, string _)
        {
            var latest = FetchText(official
                ? LoaderListSources.NeoForgeOfficialLatestUrl
                : LoaderListSources.NeoForgeMirrorLatestUrl);
            var legacy = FetchText(official
                ? LoaderListSources.NeoForgeOfficialLegacyUrl
                : LoaderListSources.NeoForgeMirrorLegacyUrl);
            var url = official ? LoaderListSources.NeoForgeOfficialLatestUrl : LoaderListSources.NeoForgeMirrorLatestUrl;
            return (LoaderListParsers.NeoForgeFromText(latest, legacy, url), null);
        }

        return FetchCore(new Spec<NeoForgeListEntry>("NeoForge", "NeoForge 版本列表",
                official => official ? LoaderListSources.NeoForgeOfficialLatestUrl : LoaderListSources.NeoForgeMirrorLatestUrl,
                official => official ? "NeoForge 官方源" : "BMCLAPI",
                ParsePair),
            mode, forceRestart, "", isCancelled);
    }

    // ---- LiteLoader ----

    /// <summary>获取 LiteLoader 版本列表。</summary>
    public LoaderListResult<LiteLoaderListEntry>? FetchLiteLoader(McVersionSourceMode mode, bool forceRestart = false,
        Func<bool>? isCancelled = null)
        => FetchCore(new Spec<LiteLoaderListEntry>("LiteLoader", "LiteLoader 版本列表",
                official => official ? LoaderListSources.LiteLoaderOfficialUrl : LoaderListSources.LiteLoaderMirrorUrl,
                official => official ? "LiteLoader 官方源" : "BMCLAPI",
                (official, text) => (LoaderListParsers.LiteLoaderFromJson(text,
                    official ? LoaderListSources.LiteLoaderOfficialUrl : LoaderListSources.LiteLoaderMirrorUrl), null)),
            mode, forceRestart, "", isCancelled);

    // ---- Fabric ----

    /// <summary>
    /// 获取 Fabric 元数据。结果以原始 JSON 文本返回（<see cref="LoaderListResult{T}.RawJson"/>）：
    /// UI 直接读取 game / loader / installer 三个数组，转成强类型反而会丢字段。
    /// </summary>
    public LoaderListResult<string>? FetchFabric(McVersionSourceMode mode, bool forceRestart = false,
        Func<bool>? isCancelled = null)
        => FetchCore(new Spec<string>("Fabric", "Fabric 版本列表",
                official => official ? LoaderListSources.FabricOfficialUrl : LoaderListSources.FabricMirrorUrl,
                official => official ? "Fabric 官方源" : "BMCLAPI",
                (official, text) =>
                {
                    var url = official ? LoaderListSources.FabricOfficialUrl : LoaderListSources.FabricMirrorUrl;
                    LoaderListParsers.ValidateFabric(text, url);
                    return ([], text);
                }),
            mode, forceRestart, "", isCancelled);

    // ---- 调度与缓存 ----

    /// <summary>
    /// 按偏好给出的候选排期：(是否官方源, 启动时刻秒)。
    /// 与 VB 各 <c>*Main</c> 的 <c>Select Case Settings.Get(Of Integer)("ToolDownloadVersion")</c> 逐字对应。
    /// </summary>
    private static IReadOnlyList<(bool IsOfficial, int StartAtSeconds)> Schedule(McVersionSourceMode mode) => mode switch
    {
        McVersionSourceMode.MirrorFirst => [(false, 0), (true, 30)],
        McVersionSourceMode.OfficialFirst => [(true, 0), (false, 5)],
        _ => [(true, 0), (false, 60)],
    };

    private LoaderListResult<T>? FetchCore<T>(Spec<T> spec, McVersionSourceMode mode, bool forceRestart,
        string parameter, Func<bool>? isCancelled,
        IReadOnlyList<(bool IsOfficial, int StartAtSeconds)>? schedule = null, string cacheSuffix = "")
    {
        var cacheKey = $"{spec.Kind}|{parameter}|{cacheSuffix}";
        if (!forceRestart && TryGetCached(cacheKey, out LoaderListResult<T>? cached)) return cached;

        var planned = schedule ?? Schedule(mode);
        var candidates = new List<SourceRaceCandidate<(List<T> Items, string? RawJson)>>(planned.Count);
        foreach (var (isOfficial, startAt) in planned)
        {
            var official = isOfficial;
            var url = spec.Url(official);
            candidates.Add(new SourceRaceCandidate<(List<T> Items, string? RawJson)>(spec.SourceName(official), url,
                official, startAt, () => spec.Parse(official, FetchText(url))));
        }

        var raced = SourceRace.Run(candidates, spec.Label, isCancelled);
        if (raced is null) return null;

        var result = new LoaderListResult<T>
        {
            SourceName = raced.SourceName,
            IsOfficial = raced.IsOfficial,
            Items = raced.Value.Items,
            RawJson = raced.Value.RawJson,
        };
        PutCached(cacheKey, result);
        return result;
    }

    /// <summary>
    /// 取一段文本。优先走「单地址 + 显式超时」的直连通道：加载器列表是两条互不映射的独立地址，
    /// 走资源站的候选解析会被当成「没有镜像映射的官方源」反复重试同一地址。
    /// </summary>
    private string FetchText(string url)
    {
        var api = ResourceApi.Resolve(_api);
        var timeout = TimeSpan.FromSeconds(SourceRace.SingleSourceTimeoutSeconds);
        return api switch
        {
            IDirectTextResourceApi textApi => textApi.RequestDirectText(url, timeout),
            IDirectResourceApi directApi => directApi.RequestDirect(url, timeout).ToJsonString(),
            _ => api.Request(url).ToJsonString(),
        };
    }

    /// <summary>读取 Forge 推荐版本缓存；任何异常都只降级为「无推荐版本」，不影响列表本身。</summary>
    private static string? ReadForgeRecommended(string? path, string mcVersion)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) is not System.Text.Json.Nodes.JsonObject json)
                return null;
            var value = json[mcVersion].AsString();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            DownloadLog.Warn(ex, $"读取 Forge 推荐版本失败，已忽略：{path}");
            return null;
        }
    }

    private bool TryGetCached<T>(string key, out LoaderListResult<T>? result)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached) && cached is LoaderListResult<T> typed)
            {
                result = typed;
                return true;
            }
        }
        result = null;
        return false;
    }

    private void PutCached<T>(string key, LoaderListResult<T> result)
    {
        lock (_cacheGate) _cache[key] = result;
    }
}
