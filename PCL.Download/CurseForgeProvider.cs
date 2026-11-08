using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// CurseForge 资源站适配器（dev.4 §2.1 A2）。
/// <para/>
/// CurseForge 全部接口都要求 <c>x-api-key</c>，因此本 Provider 的可用性完全由
/// <see cref="ResourceSigningOptions.CurseForgeApiKey"/> 决定：Key 只来自环境变量
/// <c>PCL_CURSEFORGE_API_KEY</c>，缺失时 <see cref="IsAvailable"/> 为 false，
/// 调用方应降级到 <see cref="ModrinthProvider"/> 而不是报错中断（dev.4 §2.1 A4）。
/// </para>
/// </summary>
public sealed class CurseForgeProvider : ResourceProviderBase
{
    /// <summary>Minecraft 在 CurseForge 的 gameId（指纹反查接口固定在路径里）。</summary>
    private const int MinecraftGameId = 432;

    private const string ModsBaseUrl = "https://api.curseforge.com/v1/mods";
    private const string FingerprintsUrl = "https://api.curseforge.com/v1/fingerprints/432";
    private const string FilesUrl = "https://api.curseforge.com/v1/mods/files";

    private readonly ResourceSigningOptions _signing;

    /// <summary>构造适配器。</summary>
    /// <param name="api">请求器；null 时使用 <see cref="ResourceApi.Default"/>。</param>
    /// <param name="signing">签名选项（决定 Key 是否可用）；null 时取 <see cref="ResourceSigningOptions.Resolve"/>。</param>
    public CurseForgeProvider(IResourceApi? api = null, ResourceSigningOptions? signing = null) : base(api)
        => _signing = ResourceSigningOptions.Resolve(signing);

    /// <inheritdoc />
    public override ResourcePlatforms Platform => ResourcePlatforms.CurseForge;

    /// <inheritdoc />
    public override bool IsAvailable => _signing.HasCurseForgeApiKey;

    /// <inheritdoc />
    public override string? UnavailableReason =>
        IsAvailable ? null : $"CurseForge 源未启用（未配置环境变量 {ResourceSigningOptions.CurseForgeKeyEnvironmentVariable}）";

    /// <inheritdoc />
    public override IReadOnlyList<ResourceFingerprintMatch> LookupFingerprints(
        IReadOnlyCollection<ResourceFingerprint> fingerprints)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(fingerprints);

        var hashes = fingerprints
            .Select(f => f.CurseForge)
            .Where(h => h is not null)
            .Select(h => h!.Value)
            .Distinct()
            .ToList();
        if (hashes.Count == 0) return [];

        var body = $"{{\"fingerprints\": [{string.Join(",", hashes)}]}}";
        var result = Api.Request(FingerprintsUrl, HttpMethod.Post, body, "application/json");
        var data = result["data"];
        var exactMatches = data?["exactMatches"].AsArrayOrEmpty() ?? [];
        if (exactMatches.Count == 0)
        {
            DownloadLog.Info("CurseForge 指纹反查未命中任何本地文件");
            return [];
        }

        var byFingerprint = fingerprints
            .Where(f => f.CurseForge is not null)
            .GroupBy(f => f.CurseForge!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var matches = new List<ResourceFingerprintMatch>(exactMatches.Count);
        foreach (var entry in exactMatches)
        {
            if (entry is null) continue;
            var projectId = entry["id"].AsString();
            if (entry["file"] is not JsonObject fileJson || string.IsNullOrWhiteSpace(projectId)) continue;

            var fingerprintValue = ReadFingerprint(fileJson);
            if (fingerprintValue is null || !byFingerprint.TryGetValue(fingerprintValue.Value, out var fingerprint))
                continue;

            // CurseForge 的文件对象不一定带 gameId（工程对象才有），而 FromPlatformJson 用它判别平台；
            // 缺失时补上，保证走 CurseForge 解析分支，而不是被误判为 Modrinth。响应本身带 gameId 时是空操作。
            if (!fileJson.ContainsKey("gameId")) fileJson["gameId"] = MinecraftGameId;

            matches.Add(new ResourceFingerprintMatch
            {
                Fingerprint = fingerprint,
                ProjectId = projectId,
                File = ResourceVersion.FromPlatformJson(fileJson, ResourceTypes.Mod),
                Loaders = fileJson["gameVersions"].AsArrayOrEmpty()
                    .Select(v => (v.AsString() ?? "").Trim().ToLowerInvariant())
                    .Where(v => VbCompat.GetAllFlags<ModLoaders>()
                        .Any(loader => string.Equals(loader.ToString(), v, StringComparison.OrdinalIgnoreCase)))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToList(),
            });
        }

        DownloadLog.Info($"CurseForge 指纹反查命中 {matches.Count} / {hashes.Count} 个本地文件");
        return matches;
    }

    /// <summary>
    /// 批量查询指定文件 ID 的详细信息（VB 的 <c>POST /v1/mods/files</c>）。
    /// </summary>
    /// <param name="fileIds">CurseForge 文件 ID。</param>
    public IReadOnlyList<ResourceVersion> GetFiles(IReadOnlyCollection<int> fileIds)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(fileIds);
        if (fileIds.Count == 0) return [];

        var body = $"{{\"fileIds\": [{string.Join(",", fileIds)}]}}";
        var result = Api.Request(FilesUrl, HttpMethod.Post, body, "application/json");
        var files = result["data"].AsArrayOrEmpty();
        var versions = new List<ResourceVersion>(files.Count);
        foreach (var file in files)
        {
            if (file is not JsonObject fileJson) continue;
            if (!fileJson.ContainsKey("gameId")) fileJson["gameId"] = MinecraftGameId;
            versions.Add(ResourceVersion.FromPlatformJson(fileJson, ResourceTypes.Mod));
        }
        DownloadLog.Info($"CurseForge 返回 {versions.Count} / {fileIds.Count} 个文件信息");
        return versions;
    }

    /// <summary>
    /// 批量查询工程信息（VB 的 <c>POST /v1/mods</c>），过滤掉 <c>isAvailable = false</c> 的工程。
    /// </summary>
    /// <param name="projectIds">工程 ID。</param>
    public IReadOnlyList<ResourceProject> GetProjectsRaw(IReadOnlyCollection<string> projectIds)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0) return [];

        var body = $"{{\"modIds\": [{string.Join(",", projectIds)}]}}";
        var result = Api.Request(ModsBaseUrl, HttpMethod.Post, body, "application/json");
        var projects = new List<ResourceProject>();
        foreach (var project in result["data"].AsArrayOrEmpty())
        {
            if (project is null) continue;
            if (project["isAvailable"] is JsonValue available && available.TryGetValue<bool>(out var isAvailable) && !isAvailable)
                continue;
            projects.Add(new ResourceProject(project));
        }
        return projects;
    }

    /// <summary>
    /// 批量查询工程信息，并保留 <c>latestFilesIndexes</c>（每个工程按「加载器 + MC 版本」给出的候选文件 ID）。
    /// <para/>
    /// 在线反查靠这份索引挑候选文件，才能像 VB 一样只请求少量文件就完成更新检测；
    /// 若改用 <see cref="ResourceVersion.FromProjectId"/> 会退化成「每工程拉全量版本列表」。
    /// </para>
    /// </summary>
    /// <param name="projectIds">工程 ID。</param>
    public IReadOnlyList<CurseForgeProjectBundle> GetProjectBundles(IReadOnlyCollection<string> projectIds)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0) return [];

        var body = $"{{\"modIds\": [{string.Join(",", projectIds)}]}}";
        var result = Api.Request(ModsBaseUrl, HttpMethod.Post, body, "application/json");
        var bundles = new List<CurseForgeProjectBundle>();
        foreach (var project in result["data"].AsArrayOrEmpty())
        {
            if (project is not JsonObject projectJson) continue;
            if (projectJson["isAvailable"] is JsonValue available && available.TryGetValue<bool>(out var isAvailable) && !isAvailable)
                continue;

            var indexes = new List<CurseForgeFileIndex>();
            foreach (var index in projectJson["latestFilesIndexes"].AsArrayOrEmpty())
            {
                if (index is null) continue;
                var fileId = index["fileId"].AsInt();
                if (fileId <= 0) continue;
                var loader = index["modLoader"] is null
                    ? ModLoaders.None
                    : FromCurseForgeModLoaderType(index["modLoader"].AsInt());
                indexes.Add(new CurseForgeFileIndex(fileId, loader, index["gameVersion"].AsString()));
            }

            bundles.Add(new CurseForgeProjectBundle
            {
                Project = new ResourceProject(projectJson),
                LatestFiles = indexes,
            });
        }
        return bundles;
    }

    /// <summary>CurseForge <c>ModLoaderType</c> → <see cref="ModLoaders"/>（与 PCLCS <c>Resource.FromCurseForgeModLoaderType</c> 一致）。</summary>
    public static ModLoaders FromCurseForgeModLoaderType(int modLoaderType) => modLoaderType switch
    {
        1 => ModLoaders.Forge,
        3 => ModLoaders.LiteLoader,
        4 => ModLoaders.Fabric,
        6 => ModLoaders.NeoForge,
        _ => ModLoaders.None,
    };

    /// <summary>读取文件对象里的 CurseForge 指纹（fileFingerprint），失败返回 null。</summary>
    internal static uint? ReadFingerprint(JsonObject fileJson)
    {
        if (fileJson["fileFingerprint"] is not JsonValue value) return null;
        if (value.TryGetValue<uint>(out var u)) return u;
        if (value.TryGetValue<long>(out var l) && l is >= 0 and <= uint.MaxValue) return (uint)l;
        return null;
    }
}

/// <summary>
/// CurseForge 工程响应里 <c>latestFilesIndexes</c> 的单条记录（未取文件详情，只含候选关系）。
/// </summary>
/// <param name="FileId">文件 ID。</param>
/// <param name="Loader">该文件声明支持的加载器。</param>
/// <param name="GameVersion">该文件声明支持的 MC 版本。</param>
public readonly record struct CurseForgeFileIndex(int FileId, ModLoaders Loader, string? GameVersion);

/// <summary>
/// 一个 CurseForge 工程及其候选文件索引。
/// </summary>
public sealed class CurseForgeProjectBundle
{
    /// <summary>工程信息。</summary>
    public ResourceProject Project { get; init; } = null!;

    /// <summary>候选文件索引（对应 <c>latestFilesIndexes</c>）。</summary>
    public IReadOnlyList<CurseForgeFileIndex> LatestFiles { get; init; } = [];
}
