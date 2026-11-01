using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 版本列表源偏好（对齐 VB 设置项 <c>ToolDownloadVersion</c> 的取值与文案）。
/// </summary>
public enum McVersionSourceMode
{
    /// <summary>尽量使用镜像源（0）：镜像立即请求，30 秒后才换官方源。</summary>
    MirrorFirst = 0,

    /// <summary>优先使用官方源，加载缓慢时换用镜像源（1）：官方立即请求，5 秒无果即并行镜像。</summary>
    OfficialFirst = 1,

    /// <summary>尽量使用官方源（2）：官方立即请求，60 秒后才换镜像。</summary>
    OfficialPreferred = 2,
}

/// <summary>
/// 版本清单中的一条记录。
/// </summary>
/// <param name="Id">版本 ID，例如 <c>1.21.5</c> / <c>25w14craftmine</c>。</param>
/// <param name="Type">版本类型：release / snapshot / old_beta / old_alpha。</param>
/// <param name="Url">该版本独立 json 的下载地址。</param>
/// <param name="ReleaseTime">发布时间（原始字符串，不解析以免受时区影响）。</param>
public sealed record McVersionEntry(string Id, string Type, string Url, string ReleaseTime)
{
    /// <summary>是否为快照版。</summary>
    public bool IsSnapshot => string.Equals(Type, "snapshot", StringComparison.Ordinal);
}

/// <summary>
/// 版本清单的获取结果。
/// </summary>
public sealed class McVersionManifest
{
    /// <summary>实际命中的源名称，例如「Mojang 官方源」「BMCLAPI」。</summary>
    public required string SourceName { get; init; }

    /// <summary>命中源是否为官方实时数据。</summary>
    public required bool IsOfficial { get; init; }

    /// <summary>合并特供项之后的清单原文（VB 侧仍按 JSON 读取，保持 UI 契约不变）。</summary>
    public required string RawJson { get; init; }

    /// <summary>全部版本记录（保持服务端给出的顺序）。</summary>
    public required IReadOnlyList<McVersionEntry> Versions { get; init; }

    /// <summary>最新正式版 ID；缺失时为空串。</summary>
    public string LatestRelease { get; init; } = "";

    /// <summary>最新快照 ID；缺失时为空串。</summary>
    public string LatestSnapshot { get; init; } = "";

    /// <summary>所有正式版的 Drop 序数，去重并从高到低排列（平移 VB 的 <c>AllDrops</c>）。</summary>
    public IReadOnlyList<int> Drops { get; init; } = [];

    /// <summary>本源的获取耗时（毫秒），用于 VB 侧判断是否优先使用官方源。</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>按版本 ID 查找其 json 下载地址；找不到时返回 null。</summary>
    public string? GetVersionJsonUrl(string id)
    {
        foreach (var version in Versions)
            if (string.Equals(version.Id, id, StringComparison.Ordinal))
                return version.Url;
        return null;
    }
}

/// <summary>
/// 一个版本列表候选源。
/// </summary>
/// <param name="Url">清单地址。</param>
/// <param name="SourceName">展示名称。</param>
/// <param name="IsOfficial">是否为官方实时数据。</param>
/// <param name="StartAtSeconds">相对本次获取开始的启动时刻（秒）。0 表示立即启动。</param>
public readonly record struct McVersionSource(string Url, string SourceName, bool IsOfficial, int StartAtSeconds);

/// <summary>
/// 版本列表候选源的构造（平移自 VB <c>ModDownload.DlClientListMain</c> 的三个分支）。
/// </summary>
public static class McVersionSources
{
    /// <summary>Mojang 官方版本清单。</summary>
    public const string MojangUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    /// <summary>BMCLAPI 镜像版本清单。</summary>
    public const string BmclapiUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json";

    /// <summary>版本数下限：低于该值视为「拿到了错误页面」而非清单（VB 原值）。</summary>
    public const int MinimumVersionCount = 200;

    /// <summary>构建官方源候选。</summary>
    public static McVersionSource Mojang(int startAtSeconds = 0)
        => new(MojangUrl, "Mojang 官方源", true, startAtSeconds);

    /// <summary>构建 BMCLAPI 候选。</summary>
    public static McVersionSource Bmclapi(int startAtSeconds = 0)
        => new(BmclapiUrl, "BMCLAPI", false, startAtSeconds);

    /// <summary>
    /// 按用户的「版本列表源」偏好给出候选顺序与切换时刻（与 VB 的 <c>DlSourceLoader</c> 排期等价）。
    /// </summary>
    public static IReadOnlyList<McVersionSource> Build(McVersionSourceMode mode) => mode switch
    {
        McVersionSourceMode.MirrorFirst => [Bmclapi(0), Mojang(30)],
        McVersionSourceMode.OfficialFirst => [Mojang(0), Bmclapi(5)],
        _ => [Mojang(0), Bmclapi(60)],
    };
}

/// <summary>
/// 版本清单获取器（平移自 VB <c>ModDownload.DlClientList*</c>）。
/// <para>
/// 与 VB 的差异只有一处并已在下方注明：VB 的镜像分支用「上一次主加载器的旧结果」判断
/// 是否包含目标版本，本实现改为对每个候选各自校验，语义更严格且不再依赖调用顺序。
/// </para>
/// </summary>
public sealed class McVersionManifestClient
{
    /// <summary>单源的兜底超时（秒）。候选切换由 <see cref="McVersionSource.StartAtSeconds"/> 控制，
    /// 该值只用于保证任务最终会结束，避免慢连接永久占住轮询。</summary>
    private const int SingleSourceTimeoutSeconds = 45;

    /// <summary>轮询间隔（毫秒）。</summary>
    private const int PollMilliseconds = 50;

    private readonly IResourceApi _api;

    /// <summary>
    /// 构造获取器。
    /// </summary>
    /// <param name="api">请求器；null 时使用 <see cref="ResourceApi.Default"/>。</param>
    public McVersionManifestClient(IResourceApi? api = null) => _api = ResourceApi.Resolve(api);

    /// <summary>
    /// 按偏好获取版本清单。
    /// </summary>
    /// <param name="mode">源偏好（对应 VB 设置项 <c>ToolDownloadVersion</c>）。</param>
    /// <param name="requiredVersion">要求必须包含的版本 ID；镜像缺少刚更新的版本时用它触发换源。</param>
    /// <param name="extraVersionsJsonPath">额外的特供版本文件（VB 的 <c>Cache/download.json</c>）；不存在则忽略。</param>
    /// <exception cref="ResourceRequestException">所有候选源都失败时抛出。</exception>
    public McVersionManifest Fetch(McVersionSourceMode mode, string? requiredVersion = null,
        string? extraVersionsJsonPath = null)
        => Fetch(McVersionSources.Build(mode), requiredVersion, extraVersionsJsonPath);

    /// <summary>
    /// 按给定的候选源获取版本清单：按 <see cref="McVersionSource.StartAtSeconds"/> 依次启动，
    /// 先成功者胜出，全部失败则抛出聚合异常。
    /// </summary>
    public McVersionManifest Fetch(IReadOnlyList<McVersionSource> sources, string? requiredVersion = null,
        string? extraVersionsJsonPath = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0) throw new ArgumentException("至少需要一个版本列表源", nameof(sources));

        var clock = Stopwatch.StartNew();
        var pending = new List<Task<McVersionManifest>>(sources.Count);
        var errors = new List<Exception>(sources.Count);
        var next = 0;
        var deadline = TimeSpan.FromSeconds(sources[^1].StartAtSeconds + SingleSourceTimeoutSeconds);

        while (true)
        {
            // 到点启动候选：先启动第一个，其余按排期在切换时刻启动
            while (next < sources.Count && TimeSpan.FromSeconds(sources[next].StartAtSeconds) <= clock.Elapsed)
            {
                var source = sources[next];
                next++;
                if (next > 1)
                    DownloadLog.Info($"版本列表切换候选源：{source.SourceName}（{clock.ElapsedMilliseconds}ms）");
                pending.Add(Task.Run(() => FetchOne(source, requiredVersion, extraVersionsJsonPath)));
            }

            // 先完成者优先；同一轮有多个完成时按候选顺序取靠前的那个
            for (var i = 0; i < pending.Count; i++)
            {
                var task = pending[i];
                if (!task.IsCompleted) continue;
                pending.RemoveAt(i);
                if (task.IsCompletedSuccessfully)
                {
                    var manifest = task.Result;
                    DownloadLog.Info($"版本列表获取成功：{manifest.SourceName}，{manifest.Versions.Count} 个版本，"
                        + $"{manifest.Drops.Count} 个 Drop，耗时 {manifest.ElapsedMilliseconds}ms");
                    return manifest;
                }
                var error = task.Exception?.GetBaseException() ?? new Exception("版本列表源获取失败");
                errors.Add(error);
                DownloadLog.Warn(error, $"版本列表源失败");
                i--;
            }

            if (next >= sources.Count && pending.Count == 0) break;
            if (clock.Elapsed >= deadline)
            {
                DownloadLog.Warn($"版本列表获取超过兜底时限 {deadline.TotalSeconds:0}s，放弃剩余候选");
                break;
            }
            Thread.Sleep(PollMilliseconds);
        }

        var detail = errors.Count == 0
            ? "没有源在时限内返回结果"
            : string.Join("；", errors.Select(e => e.Message));
        var kind = errors.Count == 0 ? ResourceRequestErrorKind.Timeout : ResourceRequestErrorKind.Network;
        throw new ResourceRequestException(kind, sources[0].Url,
            $"所有版本列表源均获取失败（尝试 {sources.Count} 个源）：{detail}", null, new AggregateException(errors));
    }

    private McVersionManifest FetchOne(McVersionSource source, string? requiredVersion, string? extraVersionsJsonPath)
    {
        var clock = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(SingleSourceTimeoutSeconds);
        var root = _api is IDirectResourceApi direct
            ? direct.RequestDirect(source.Url, timeout)
            : _api.Request(source.Url);

        if (root is not JsonObject manifest)
            throw new ResourceRequestException(ResourceRequestErrorKind.Parse, source.Url,
                "版本列表根节点不是 JSON 对象");

        var versions = manifest["versions"] as JsonArray;
        if (versions is null || versions.Count < McVersionSources.MinimumVersionCount)
        {
            throw new ResourceRequestException(ResourceRequestErrorKind.Parse, source.Url,
                $"获取到的版本列表长度不足（{versions?.Count ?? 0} < {McVersionSources.MinimumVersionCount}）");
        }

        MergeExtraVersions(manifest, versions, extraVersionsJsonPath, source.Url);

        if (!string.IsNullOrEmpty(requiredVersion) && !ContainsVersion(versions, requiredVersion))
        {
            throw new ResourceRequestException(ResourceRequestErrorKind.NotFound, source.Url,
                $"{source.SourceName} 未包含目标版本 {requiredVersion}");
        }

        var entries = new List<McVersionEntry>(versions.Count);
        foreach (var node in versions)
        {
            if (node is not JsonObject item) continue;
            var id = item["id"].AsString();
            if (string.IsNullOrEmpty(id)) continue;
            entries.Add(new McVersionEntry(id,
                item["type"].AsString() ?? "",
                item["url"].AsString() ?? "",
                item["releaseTime"].AsString() ?? ""));
        }

        return new McVersionManifest
        {
            SourceName = source.SourceName,
            IsOfficial = source.IsOfficial,
            RawJson = manifest.ToJsonString(),
            Versions = entries,
            LatestRelease = manifest["latest"]?["release"].AsString() ?? "",
            LatestSnapshot = manifest["latest"]?["snapshot"].AsString() ?? "",
            Drops = ExtractDrops(entries),
            ElapsedMilliseconds = clock.ElapsedMilliseconds,
        };
    }

    /// <summary>
    /// 提取所有正式版的 Drop 序数：跳过带 <c>-</c> 的版本（预发布/快照），跳过无法换算的旧版本（209）。
    /// </summary>
    public static IReadOnlyList<int> ExtractDrops(IEnumerable<McVersionEntry> versions)
    {
        var drops = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        foreach (var version in versions)
        {
            if (version.Id.Contains('-', StringComparison.Ordinal)) continue;
            var drop = McVersionUtil.VersionToDrop(version.Id);
            if (drop == 209) continue;
            drops.Add(drop);
        }
        return [.. drops];
    }

    private static bool ContainsVersion(JsonArray versions, string id)
    {
        foreach (var node in versions)
            if (node is JsonObject item && string.Equals(item["id"].AsString(), id, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// 合并 <c>Cache/download.json</c> 里的特供版本（VB 用 <c>JArray.Merge</c>，同样按 id 去重）。
    /// 支持两种文件形态：版本数组本身，或含 <c>versions</c> 数组的对象。
    /// </summary>
    private static void MergeExtraVersions(JsonObject manifest, JsonArray versions, string? extraPath, string url)
    {
        if (string.IsNullOrEmpty(extraPath) || !File.Exists(extraPath)) return;
        try
        {
            var text = File.ReadAllText(extraPath);
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(text);
            }
            catch (Exception ex)
            {
                throw new ResourceRequestException(ResourceRequestErrorKind.Parse, url,
                    $"特供版本文件不是合法 JSON：{ex.Message}", null, ex);
            }

            var extra = parsed as JsonArray ?? parsed?["versions"] as JsonArray;
            if (extra is null) return;

            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in versions)
                if (node is JsonObject item && item["id"].AsString() is { Length: > 0 } id)
                    existing.Add(id);

            var added = 0;
            foreach (var node in extra.ToList())
            {
                if (node is not JsonObject item) continue;
                var id = item["id"].AsString();
                if (string.IsNullOrEmpty(id) || !existing.Add(id)) continue;
                versions.Add(item.DeepClone());
                added++;
            }
            if (added > 0)
                DownloadLog.Info($"已合并 {added} 个特供版本（{extraPath}）");
        }
        catch (ResourceRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 特供文件只是锦上添花，读不到不应阻断版本列表
            DownloadLog.Warn(ex, $"读取特供版本文件失败，已忽略：{extraPath}");
        }
    }
}
