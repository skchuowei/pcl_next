using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// Modrinth 资源站适配器（dev.4 §2.1 A2）。
/// <para/>
/// 逻辑全部复用 dev.3 已平移的 <see cref="ResourceProject"/> / <see cref="ResourceVersion"/> /
/// <see cref="ResourceSearcher"/>，本类只负责「按平台收敛入口 + 指纹反查」。
/// Modrinth 不需要 API Key，因此在 CurseForge 不可用时由它承接降级。
/// </para>
/// </summary>
public sealed class ModrinthProvider : ResourceProviderBase
{
    private const string VersionFilesUrl = "https://api.modrinth.com/v2/version_files";
    private const string VersionFilesUpdateUrl = "https://api.modrinth.com/v2/version_files/update_individual";

    /// <summary>构造适配器。</summary>
    /// <param name="api">请求器；null 时使用 <see cref="ResourceApi.Default"/>。</param>
    public ModrinthProvider(IResourceApi? api = null) : base(api) { }

    /// <inheritdoc />
    public override ResourcePlatforms Platform => ResourcePlatforms.Modrinth;

    /// <inheritdoc />
    public override IReadOnlyList<ResourceFingerprintMatch> LookupFingerprints(
        IReadOnlyCollection<ResourceFingerprint> fingerprints)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(fingerprints);

        var hashes = fingerprints
            .Select(f => f.ModrinthSha1)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hashes.Count == 0) return [];

        var body = $"{{\"hashes\": [\"{string.Join("\",\"", hashes)}\"], \"algorithm\": \"sha1\"}}";
        var result = Api.Request(VersionFilesUrl, HttpMethod.Post, body, "application/json");
        if (result is not JsonObject mapping) return [];

        var found = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapping)
            if (pair.Value is not null) found[pair.Key] = pair.Value;

        var matches = new List<ResourceFingerprintMatch>();
        foreach (var fingerprint in fingerprints)
        {
            if (string.IsNullOrWhiteSpace(fingerprint.ModrinthSha1)) continue;
            if (!found.TryGetValue(fingerprint.ModrinthSha1, out var versionJson)) continue;

            // VB 的防御：接口返回的 sha1 必须与本地一致，否则视为未命中
            var fileSha1 = FirstFileSha1(versionJson);
            if (!string.IsNullOrEmpty(fileSha1) &&
                !string.Equals(fileSha1, fingerprint.ModrinthSha1, StringComparison.OrdinalIgnoreCase))
                continue;

            var projectId = versionJson["project_id"].AsString();
            if (string.IsNullOrWhiteSpace(projectId)) continue;

            matches.Add(new ResourceFingerprintMatch
            {
                Fingerprint = fingerprint,
                ProjectId = projectId,
                File = ResourceVersion.FromPlatformJson(versionJson, ResourceTypes.Mod),
                Loaders = ReadLoaders(versionJson),
            });
        }

        DownloadLog.Info($"Modrinth 指纹反查命中 {matches.Count} / {hashes.Count} 个本地文件");
        return matches;
    }

    /// <summary>
    /// 查询各文件在指定加载器与 MC 版本下的最新版本（VB 的
    /// <c>version_files/update_individual</c>），返回「输入指纹 SHA1 → 最新版本」。
    /// </summary>
    /// <param name="entries">待查文件：指纹 + 该文件在平台上声明的加载器列表。</param>
    /// <param name="gameVersion">目标 MC 版本（原版版本名）。</param>
    public IReadOnlyDictionary<string, ModrinthUpdate> LookupUpdates(
        IEnumerable<(string Sha1, IReadOnlyList<string> Loaders)> entries, string gameVersion)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);

        var list = entries.Where(e => !string.IsNullOrWhiteSpace(e.Sha1)).ToList();
        if (list.Count == 0) return new Dictionary<string, ModrinthUpdate>();

        var payload = string.Join(",", list.Select(e =>
            $"{{\"hash\": \"{e.Sha1}\", \"loaders\": [\"{string.Join("\",\"", e.Loaders)}\"], \"game_versions\": [\"{gameVersion}\"]}}"));
        var body = $"{{\"algorithm\": \"sha1\", \"hashes\": [{payload}]}}";
        var result = Api.Request(VersionFilesUpdateUrl, HttpMethod.Post, body, "application/json");
        if (result is not JsonObject mapping) return new Dictionary<string, ModrinthUpdate>();

        var updates = new Dictionary<string, ModrinthUpdate>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapping)
        {
            if (pair.Value is null) continue;
            updates[pair.Key] = new ModrinthUpdate
            {
                File = ResourceVersion.FromPlatformJson(pair.Value, ResourceTypes.Mod),
                Loaders = ReadLoaders(pair.Value),
                ProjectId = pair.Value["project_id"].AsString() ?? "",
            };
        }
        DownloadLog.Info($"Modrinth 更新检查返回 {updates.Count} 个文件的新版本信息");
        return updates;
    }

    /// <summary>读取 Modrinth 版本对象中的加载器列表（小写、去重、升序，与 VB 一致）。</summary>
    internal static List<string> ReadLoaders(JsonNode versionJson) => versionJson["loaders"]
        .AsArrayOrEmpty()
        .Select(l => (l.AsString() ?? "").Trim().ToLowerInvariant())
        .Where(s => s.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(s => s, StringComparer.Ordinal)
        .ToList();

    private static string? FirstFileSha1(JsonNode versionJson)
    {
        if (versionJson["files"] is JsonArray { Count: > 0 } files && files[0] is JsonObject first)
            return first["hashes"]?["sha1"].AsString();
        return null;
    }
}

/// <summary>
/// Modrinth 更新检查的结果项：新版本文件、该版本声明的加载器、所属工程 ID。
/// <para/>加载器列表单独带出，是因为 VB 的判定要求「更新版本与本地的加载器列表完全一致」。
/// </summary>
public sealed class ModrinthUpdate
{
    /// <summary>新版本文件。</summary>
    public ResourceVersion File { get; init; } = new();

    /// <summary>该版本声明的加载器（小写、去重、升序）。</summary>
    public IReadOnlyList<string> Loaders { get; init; } = [];

    /// <summary>所属工程 ID。</summary>
    public string ProjectId { get; init; } = "";
}
