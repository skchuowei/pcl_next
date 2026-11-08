namespace PCL.Download;

/// <summary>
/// 资源站请求的源偏好（对齐 VB 设置项 <c>ToolDownloadMod</c> 的取值：0 镜像优先 / 1 自动 / 其他 官方优先）。
/// </summary>
public enum ModSourcePreference
{
    /// <summary>优先使用镜像源（VB 值 0）。</summary>
    PreferMirror = 0,
    /// <summary>官方源优先、镜像兜底（VB 值 1）。</summary>
    Automatic = 1,
    /// <summary>官方源优先，仅在前两次尝试失败后兜底镜像（VB 值 2 及其他）。</summary>
    PreferOfficial = 2,
}

/// <summary>
/// 一次资源站请求的候选源：具体地址与该候选单独的超时。
/// </summary>
/// <param name="Url">候选地址。</param>
/// <param name="TimeoutSeconds">该候选的超时（秒）；与 VB 的 <c>DlModRequest</c> 逐项对齐。</param>
/// <param name="IsMirror">是否为镜像源（用于日志与统计，不参与判定）。</param>
public readonly record struct ResourceSourceCandidate(string Url, int TimeoutSeconds, bool IsMirror);

/// <summary>
/// 资源站 API 的候选源解析（平移自 VB <c>ModDownload.DlModRequest</c> 内的源列表构造）。
/// <para/>
/// VB 原实现把「候选顺序 + 每个候选的超时」硬编码在 <c>DlModRequest</c> 里，四种分支：
/// 镜像优先（镜像 10s ×2 → 官方 30s）、自动（官方 → 镜像 10s → 官方 30s → 镜像 30s）、
/// 官方优先（官方 → 官方 30s → 镜像 30s）、以及无镜像映射时只给官方两个候选。
/// 本类逐字平移该逻辑，行为与 VB 等价。
/// </summary>
public static class ResourceSourceResolver
{
    /// <summary>Modrinth 官方接口需要在 20s 以上才能稳定返回（VB 注释原话）。</summary>
    private const int ModrinthTimeoutSeconds = 20;

    /// <summary>非 Modrinth 官方接口的首选超时（秒）。</summary>
    private const int DefaultTimeoutSeconds = 10;

    /// <summary>兜底候选统一使用的宽超时（秒）。</summary>
    private const int FallbackTimeoutSeconds = 30;

    /// <summary>
    /// 解析出一次请求应依次尝试的候选源。
    /// </summary>
    /// <param name="url">原始（官方）地址。</param>
    /// <param name="preference">源偏好。</param>
    public static IReadOnlyList<ResourceSourceCandidate> Resolve(string url, ModSourcePreference preference = ModSourcePreference.Automatic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var list = new List<ResourceSourceCandidate>(4);
        var officialTimeout = url.Contains("modrinth", StringComparison.OrdinalIgnoreCase)
            ? ModrinthTimeoutSeconds
            : DefaultTimeoutSeconds;
        var mirrorUrl = ModMirror.Get(url);

        // 没有对应的镜像映射时，只能反复尝试官方源（VB 的 Else 分支）
        if (string.Equals(mirrorUrl, url, StringComparison.Ordinal))
        {
            list.Add(new ResourceSourceCandidate(url, officialTimeout, false));
            list.Add(new ResourceSourceCandidate(url, FallbackTimeoutSeconds, false));
            return list;
        }

        switch (preference)
        {
            case ModSourcePreference.PreferMirror:
                // VB Case 0：镜像两次（第二次是纯重试）→ 官方兜底
                list.Add(new ResourceSourceCandidate(mirrorUrl, 10, true));
                list.Add(new ResourceSourceCandidate(mirrorUrl, 10, true));
                list.Add(new ResourceSourceCandidate(url, FallbackTimeoutSeconds, false));
                break;

            case ModSourcePreference.Automatic:
                // VB Case 1：官方 → 镜像 → 官方宽超时 → 镜像宽超时
                list.Add(new ResourceSourceCandidate(url, officialTimeout, false));
                list.Add(new ResourceSourceCandidate(mirrorUrl, 10, true));
                list.Add(new ResourceSourceCandidate(url, FallbackTimeoutSeconds, false));
                list.Add(new ResourceSourceCandidate(mirrorUrl, FallbackTimeoutSeconds, true));
                break;

            default:
                // VB Case Else：官方两次 → 镜像宽超时兜底
                list.Add(new ResourceSourceCandidate(url, officialTimeout, false));
                list.Add(new ResourceSourceCandidate(url, FallbackTimeoutSeconds, false));
                list.Add(new ResourceSourceCandidate(mirrorUrl, FallbackTimeoutSeconds, true));
                break;
        }

        return list;
    }
}
