namespace PCL.Download;

/// <summary>
/// 参与指纹反查的本地文件指纹：Modrinth 侧为 SHA1，CurseForge 侧为去空白 MurmurHash2 的十进制值。
/// 两个字段都可为 null（只查单站时）。
/// </summary>
public readonly record struct ResourceFingerprint(string? ModrinthSha1, uint? CurseForge);

/// <summary>
/// 指纹反查的结果：平台返回的工程 ID、该指纹对应的文件版本，以及归一化后的加载器列表。
/// </summary>
public sealed class ResourceFingerprintMatch
{
    /// <summary>被反查的指纹（与请求中的一致）。</summary>
    public ResourceFingerprint Fingerprint { get; init; }

    /// <summary>平台侧工程 ID。</summary>
    public string ProjectId { get; init; } = "";

    /// <summary>该指纹对应的文件（<see cref="ResourceVersion"/>）。</summary>
    public ResourceVersion File { get; init; } = new();

    /// <summary>
    /// 归一化的小写加载器名（如 <c>fabric</c> / <c>neoforge</c>）。
    /// Modrinth 直接用接口返回的 loaders；CurseForge 由 <see cref="ResourceVersion.ModLoaders"/> 反推。
    /// </summary>
    public IReadOnlyList<string> Loaders { get; init; } = [];
}

/// <summary>
/// 一次 Provider 搜索请求。翻页状态由调用方持有（<see cref="Storage"/>），Provider 只负责推进一页。
/// </summary>
public sealed class ResourceProviderSearchRequest
{
    /// <summary>资源种类。</summary>
    public ResourceTypes Type { get; init; } = ResourceTypes.Any;

    /// <summary>搜索文本；null 或空表示按下载量列出。</summary>
    public string? SearchText { get; init; }

    /// <summary>标签筛选（格式 <c>CF标签/MR标签</c>，空表示不限）。</summary>
    public string Tag { get; init; } = "";

    /// <summary>Mod 加载器筛选；None 表示不限。</summary>
    public ModLoaders ModLoaders { get; init; } = ModLoaders.None;

    /// <summary>MC 版本筛选。</summary>
    public string? GameVersion { get; init; }

    /// <summary>本次期望新增的结果数量。</summary>
    public int PageSize { get; init; } = 40;

    /// <summary>
    /// 翻页状态（复用 <see cref="ResourceSearcher.SearchResult"/>）。为空时新建，调用方可从
    /// <see cref="ResourceSearchPage.Storage"/> 取回以便继续翻页。
    /// </summary>
    public ResourceSearcher.SearchResult? Storage { get; init; }
}

/// <summary>
/// 一页搜索结果。<see cref="Items"/> 只含本次新增项，全量列表在 <see cref="Storage"/> 中。
/// </summary>
public sealed class ResourceSearchPage
{
    /// <summary>本次新增的工程。</summary>
    public IReadOnlyList<ResourceProject> Items { get; init; } = [];

    /// <summary>当前的偏移量（该平台已获取的结果数）。</summary>
    public int Offset { get; init; }

    /// <summary>该平台的结果总数；-1 表示未知（通常是请求失败或首次未拉到总数）。</summary>
    public int Total { get; init; } = -1;

    /// <summary>是否还有下一页。</summary>
    public bool HasMore => Total < 0 || Offset < Total;

    /// <summary>本次请求使用的翻页状态，继续翻页时传回 <see cref="ResourceProviderSearchRequest.Storage"/>。</summary>
    public ResourceSearcher.SearchResult? Storage { get; init; }
}

/// <summary>
/// 资源站不可用（例如 CurseForge 未配置 API Key）。调用方应据此降级而不是中断。
/// </summary>
public sealed class ResourceProviderUnavailableException : Exception
{
    public ResourceProviderUnavailableException(ResourcePlatforms platform, string reason)
        : base(reason) => Platform = platform;

    /// <summary>不可用的平台。</summary>
    public ResourcePlatforms Platform { get; }
}

/// <summary>
/// 资源站统一抽象（dev.4 §2.1 A1）：工程查询、批量工程查询、版本枚举、按指纹反查、搜索分页。
/// <para/>
/// 双站融合（IsLike 去重、评分排序）仍由 <see cref="ResourceSearcher"/> 负责——Provider 只提供
/// 「单个平台的原始能力」，融合逻辑平移而非重写，dev.3 的行为保持不变。
/// </para>
/// </summary>
public interface IResourceProvider
{
    /// <summary>该 Provider 对应的平台，必须为单一平台。</summary>
    ResourcePlatforms Platform { get; }

    /// <summary>当前是否可用（例如 CurseForge 需要 API Key）。</summary>
    bool IsAvailable { get; }

    /// <summary>不可用原因（可直接展示给用户）；可用时为 null。</summary>
    string? UnavailableReason { get; }

    /// <summary>按工程 ID 查询单个工程。</summary>
    ResourceProject GetProject(string projectId);

    /// <summary>按工程 ID 批量查询工程（结果顺序不保证）。</summary>
    IReadOnlyList<ResourceProject> GetProjects(IReadOnlyCollection<string> projectIds);

    /// <summary>枚举工程的全部版本文件。</summary>
    /// <param name="projectId">工程 ID。</param>
    /// <param name="loadDependencies">是否同时加载前置工程信息。</param>
    IReadOnlyList<ResourceVersion> GetVersions(string projectId, bool loadDependencies = false);

    /// <summary>按本地文件指纹反查工程与版本（仅返回命中的指纹）。</summary>
    IReadOnlyList<ResourceFingerprintMatch> LookupFingerprints(IReadOnlyCollection<ResourceFingerprint> fingerprints);

    /// <summary>推进一页搜索。</summary>
    ResourceSearchPage Search(ResourceProviderSearchRequest request);
}

/// <summary>
/// Provider 的公共实现：请求器解析、可用性检查与搜索分页复用 <see cref="ResourceSearcher"/>。
/// </summary>
public abstract class ResourceProviderBase : IResourceProvider
{
    private readonly IResourceApi? _api;

    protected ResourceProviderBase(IResourceApi? api = null) => _api = api;

    /// <summary>实际使用的请求器：构造时注入优先，其次全局默认。</summary>
    protected IResourceApi Api => _api ?? ResourceApi.Resolve(null);

    /// <inheritdoc />
    public abstract ResourcePlatforms Platform { get; }

    /// <inheritdoc />
    public virtual bool IsAvailable => true;

    /// <inheritdoc />
    public virtual string? UnavailableReason => null;

    /// <inheritdoc />
    public ResourceProject GetProject(string projectId)
    {
        EnsureAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        return ResourceProject.FromProjectId(projectId, Platform, Api);
    }

    /// <inheritdoc />
    public IReadOnlyList<ResourceProject> GetProjects(IReadOnlyCollection<string> projectIds)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0) return [];
        return ResourceProject.FromProjectIds(projectIds, Platform, Api).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ResourceVersion> GetVersions(string projectId, bool loadDependencies = false)
    {
        EnsureAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        return ResourceVersion.FromProjectId(projectId, Platform, loadDependencies, Api);
    }

    /// <inheritdoc />
    public abstract IReadOnlyList<ResourceFingerprintMatch> LookupFingerprints(IReadOnlyCollection<ResourceFingerprint> fingerprints);

    /// <inheritdoc />
    public ResourceSearchPage Search(ResourceProviderSearchRequest request)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(request);

        var storage = request.Storage ?? new ResourceSearcher.SearchResult();
        var before = storage.Results.Count;
        var target = before + Math.Max(1, request.PageSize);
        var searchRequest = new ResourceSearcher.SearchRequest(request.Type, storage, target)
        {
            Tag = request.Tag,
            ModLoaders = request.ModLoaders,
            GameVersion = request.GameVersion,
            SearchText = request.SearchText,
            Sources = Platform,
        };
        ResourceSearcher.Search(searchRequest, Api);

        var isCurseForge = Platform == ResourcePlatforms.CurseForge;
        return new ResourceSearchPage
        {
            Items = storage.Results.Skip(before).ToList(),
            Offset = isCurseForge ? storage.CurseForgeOffset : storage.ModrinthOffset,
            Total = isCurseForge ? storage.CurseForgeTotal : storage.ModrinthTotal,
            Storage = storage,
        };
    }

    /// <summary>不可用时抛出 <see cref="ResourceProviderUnavailableException"/>，避免调用方在降级路径上继续发请求。</summary>
    protected void EnsureAvailable()
    {
        if (!IsAvailable)
            throw new ResourceProviderUnavailableException(Platform,
                UnavailableReason ?? $"{Platform} 源当前不可用");
    }
}
