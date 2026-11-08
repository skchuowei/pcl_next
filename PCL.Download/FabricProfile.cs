namespace PCL.Download;

/// <summary>
/// Fabric 安装用到的两个地址：实例名与 <c>profile/json</c> 的候选源。
/// </summary>
/// <remarks>
/// 只有两行拼接，但顺序是踩过坑的：实例名里必须保留原始 MC 版本串（含 <c>∞</c>），
/// 而请求地址里要把 <c>∞</c> 换成 <c>infinite</c>。写反了不会报错——只是版本文件夹名变成
/// 「fabric-loader-0.15.0-1.20.1∞」之后再也对不上实例列表，或者请求打到 404。
/// </remarks>
public static class FabricProfile
{
    /// <summary>实例名前缀。</summary>
    public const string InstancePrefix = "fabric-loader-";

    /// <summary>Fabric 官方 meta 根地址。</summary>
    public const string OfficialBase = "https://meta.fabricmc.net/v2/versions/loader/";

    /// <summary>Fabric 的 BMCLAPI 镜像。</summary>
    public const string MirrorBase = "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/";

    /// <summary>
    /// 实例名（同时是版本文件夹名与 json 文件名）。
    /// </summary>
    /// <remarks>
    /// 保留原始 MC 版本串，不做 <c>∞ → infinite</c> 替换——替换只用于请求地址。
    /// </remarks>
    public static string BuildInstanceName(string fabricVersion, string minecraft)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fabricVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraft);
        return InstancePrefix + fabricVersion + "-" + minecraft;
    }

    /// <summary>
    /// 拉取安装 profile 的候选地址：镜像在前、官方在后。
    /// </summary>
    /// <param name="fabricVersion">Fabric Loader 版本，如 <c>0.15.0</c>。</param>
    /// <param name="minecraft">MC 版本；<c>∞</c> 会在这里被替换为 <c>infinite</c>。</param>
    public static IReadOnlyList<string> BuildProfileUrls(string fabricVersion, string minecraft)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fabricVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraft);

        var game = minecraft.Replace("∞", "infinite", StringComparison.Ordinal);
        var tail = game + "/" + fabricVersion + "/profile/json";
        return [MirrorBase + tail, OfficialBase + tail];
    }
}
