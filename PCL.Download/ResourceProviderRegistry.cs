namespace PCL.Download;

/// <summary>
/// 资源站 Provider 注册表（dev.4 §2.1 A1/A4）：按平台取 Provider，并在某站不可用时做**降级**而不是报错。
/// <para/>
/// 典型场景：未配置 <c>PCL_CURSEFORGE_API_KEY</c> 时 CurseForge 不可用，
/// <see cref="ResolveSources"/> 会把请求里的 CurseForge 位裁掉并给出可直接展示的文案，
/// 调用方据此提示「CurseForge 源未启用」，CurseForge 相关请求自动改由 Modrinth 承接。
/// </para>
/// </summary>
public sealed class ResourceProviderRegistry
{
    /// <summary>CurseForge 不可用时的标准提示文案（UI 可直接展示）。</summary>
    public const string CurseForgeDisabledNotice =
        "CurseForge 源未启用（未配置环境变量 PCL_CURSEFORGE_API_KEY），已自动改用 Modrinth 源";

    private readonly Dictionary<ResourcePlatforms, IResourceProvider> _providers = [];
    private readonly IResourceApi? _api;

    /// <summary>用给定 Provider 列表构造注册表（同平台后者覆盖前者）。</summary>
    /// <param name="providers">Provider 列表。</param>
    /// <param name="api">融合搜索使用的请求器；null 时取全局默认。</param>
    public ResourceProviderRegistry(IEnumerable<IResourceProvider> providers, IResourceApi? api = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _api = api;
        foreach (var provider in providers) _providers[provider.Platform] = provider;
    }

    /// <summary>融合搜索使用的请求器：构造时注入优先，其次全局默认。</summary>
    private IResourceApi Api => ResourceApi.Resolve(_api);

    /// <summary>
    /// 按默认配置构造：Modrinth + CurseForge（Key 取自环境变量）。
    /// </summary>
    /// <param name="api">请求器；null 时使用 <see cref="ResourceApi.Default"/>。</param>
    /// <param name="signing">签名选项；null 时取 <see cref="ResourceSigningOptions.Resolve"/>。</param>
    public static ResourceProviderRegistry CreateDefault(IResourceApi? api = null, ResourceSigningOptions? signing = null)
    {
        var options = ResourceSigningOptions.Resolve(signing);
        return new ResourceProviderRegistry(
        [
            new ModrinthProvider(api),
            new CurseForgeProvider(api, options),
        ], api);
    }

    /// <summary>取指定平台的 Provider（未注册或非单一平台时返回 null）。</summary>
    public IResourceProvider? Get(ResourcePlatforms platform)
    {
        if (platform.FlagsOf().Count() != 1) return null;
        return _providers.TryGetValue(platform, out var provider) ? provider : null;
    }

    /// <summary>当前可用的 Provider。</summary>
    public IReadOnlyList<IResourceProvider> Available =>
        _providers.Values.Where(p => p.IsAvailable).ToList();

    /// <summary>
    /// 把「期望的源」收敛为「实际可用的源」。
    /// </summary>
    /// <param name="requested">调用方期望的源（可为组合值）。</param>
    /// <param name="notice">因降级而产生的提示文案；无降级时为 null。</param>
    public ResourcePlatforms ResolveSources(ResourcePlatforms requested, out string? notice)
    {
        notice = null;
        if (requested.FlagsOf().Count() == 0) return requested;

        var resolved = ResourcePlatforms.Any & requested;
        var notices = new List<string>();
        foreach (var platform in requested.FlagsOf())
        {
            var provider = Get(platform);
            if (provider is null)
            {
                // 未注册的平台（例如只装配了单站）：静默裁掉，不当作降级提示
                resolved &= ~platform;
                continue;
            }
            if (provider.IsAvailable) continue;
            resolved &= ~platform;
            notices.Add(provider.UnavailableReason ?? $"{platform} 源不可用");
        }

        if (resolved == 0)
        {
            // 期望的源全部不可用：退到任一可用源，仍不可用则保持原请求（由调用方决定抛错）
            var fallback = Available.FirstOrDefault();
            if (fallback is not null)
            {
                resolved = fallback.Platform;
                notices.Add($"已自动改用 {fallback.Platform} 源");
            }
        }

        if (notices.Count == 0) return resolved;

        notice = notices.Count == 1 && notices[0].StartsWith("CurseForge 源未启用", StringComparison.Ordinal)
            ? CurseForgeDisabledNotice
            : string.Join("；", notices);
        return resolved;
    }

    /// <summary>当前是否存在需要提示用户的降级（CurseForge 未启用时为 true）。</summary>
    public bool HasDegradation => Get(ResourcePlatforms.CurseForge) is { IsAvailable: false };

    /// <summary>
    /// 融合搜索（dev.4 §2.1 C）：按注册表的可用性收敛源后推进一页，返回本次新增的工程。
    /// <para/>
    /// 双站融合、<c>IsLike</c> 跨站去重与评分排序仍由 <see cref="ResourceSearcher"/> 完成——
    /// 这里只做「源收敛 + 入口收敛」，不复制融合逻辑。
    /// </para>
    /// </summary>
    /// <param name="request">搜索请求；翻页时把上次返回的 <see cref="ResourceSearchPage.Storage"/> 传回。</param>
    /// <param name="notice">因降级产生的提示文案；无降级时为 null。</param>
    /// <exception cref="ResourceProviderUnavailableException">所有源都不可用时抛出。</exception>
    public ResourceSearchPage Search(ResourceProviderSearchRequest request, out string? notice)
        => Search(request, ResourcePlatforms.Any, out notice);

    /// <inheritdoc cref="Search(ResourceProviderSearchRequest, out string?)"/>
    /// <param name="request">搜索请求。</param>
    /// <param name="requestedSources">调用方期望的源（用户在下拉框选的），会先经 <see cref="ResolveSources"/> 收敛。</param>
    /// <param name="notice">因降级产生的提示文案；无降级时为 null。</param>
    public ResourceSearchPage Search(ResourceProviderSearchRequest request, ResourcePlatforms requestedSources, out string? notice)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sources = ResolveSources(requestedSources, out notice);
        if (sources == 0)
            throw new ResourceProviderUnavailableException(ResourcePlatforms.Any, "没有可用的资源源");

        var storage = request.Storage ?? new ResourceSearcher.SearchResult();
        var before = storage.Results.Count;
        var target = before + Math.Max(1, request.PageSize);
        ResourceSearcher.Search(new ResourceSearcher.SearchRequest(request.Type, storage, target)
        {
            Tag = request.Tag,
            ModLoaders = request.ModLoaders,
            GameVersion = request.GameVersion,
            SearchText = request.SearchText,
            Sources = sources,
        }, Api);

        return new ResourceSearchPage
        {
            Items = storage.Results.Skip(before).ToList(),
            //融合场景下两站各自推进，聚合值只用于「是否还有下一页」的判断
            Offset = storage.CurseForgeOffset + storage.ModrinthOffset,
            Total = storage.CurseForgeTotal < 0 || storage.ModrinthTotal < 0
                ? -1
                : storage.CurseForgeTotal + storage.ModrinthTotal,
            Storage = storage,
        };
    }
}
