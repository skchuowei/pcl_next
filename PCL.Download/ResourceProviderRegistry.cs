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

    /// <summary>用给定 Provider 列表构造注册表（同平台后者覆盖前者）。</summary>
    public ResourceProviderRegistry(IEnumerable<IResourceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers) _providers[provider.Platform] = provider;
    }

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
        ]);
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
}
