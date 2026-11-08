namespace PCL.Download;

/// <summary>
/// 一个待下载文件的纯数据描述：地址、落盘路径与校验口径。
/// <para>
/// 这是「桥接层第二轮」的产出物（见 <c>docs/plans/计划-Phase4主体-下载数据源C化.md</c> §4.9①）。
/// 内核只负责把「下载什么、下到哪、怎么校验」算成这份与执行无关的描述符，
/// VB 侧据此构造 <c>NetFile</c> 与对应的 <c>LoaderDownload</c> / <c>LoaderTask</c>。
/// 不引用任何 VB 类型，保证内核自包含、可单测；<c>NetFile</c> 构造仍留在 VB
/// （不动 <c>LoaderBase</c> 签名，不搬含宿主资产的 lambda）。
/// </para>
/// </summary>
public sealed class NetFileDescriptor
{
    /// <summary>按优先级排列的候选下载地址（镜像在前、官方在后）。</summary>
    public IReadOnlyList<string> Urls { get; }

    /// <summary>落盘绝对路径（已带尾斜杠的文件夹前缀会被正确拼出文件名）。</summary>
    public string LocalPath { get; }

    /// <summary>下载后是否按 JSON 校验。</summary>
    public bool IsJson { get; }

    /// <summary>可接受的最小文件体积；小于该值基本可断定下到的是错误页。null 表示不限。</summary>
    public long? MinSize { get; }

    /// <summary>可接受的最大文件体积；null 表示不限。</summary>
    public long? MaxSize { get; }

    /// <summary>文件校验值（MD5 或 SHA1）；null 表示不校验。</summary>
    public string? Hash { get; }

    /// <summary>期望文件体积；用于精确校验。null 表示不校验。</summary>
    public long? ActualSize { get; }

    public NetFileDescriptor(
        IReadOnlyList<string> urls,
        string localPath,
        bool isJson = false,
        long? minSize = null,
        long? maxSize = null,
        string? hash = null,
        long? actualSize = null)
    {
        Urls = urls;
        LocalPath = localPath;
        IsJson = isJson;
        MinSize = minSize;
        MaxSize = maxSize;
        Hash = hash;
        ActualSize = actualSize;
    }
}

/// <summary>
/// 合并安装里各个加载器「下载文件清单」的纯计算层。输入基元，输出 <see cref="NetFileDescriptor"/>。
/// <para>
/// 只收编那些「地址与落盘路径本就由 C# 真源算出」的下载项——目前是 Fabric 主文件
/// （<see cref="FabricProfile"/>）。Forge / NeoForge 安装器虽也经由 C# <c>ForgelikeInstaller</c>
/// 算地址，但其 URL 拼接逻辑已在该类内，且调用点嵌套在 250 行复合加载器里，本轮不动；
/// OptiFine（非 Mod）/ LiteLoader / 原版 / Fabric API / OptiFabric 的地址与路径由 VB 资源对象、
/// 镜像解析与版本 JSON 解析持有，需 <c>LoaderBase</c> DTO 化大工程才能搬进内核。
/// </para>
/// </summary>
public static class InstallDownloads
{
    /// <summary>
    /// Fabric 主文件（profile/json）的下载描述符。
    /// <para>
    /// 路径口径逐字对齐 VB <c>McDownloadFabricLoader</c>：
    /// <c>McFolder &amp; "versions\" &amp; Id &amp; "\" &amp; Id &amp; ".json"</c>，
    /// 其中 <c>Id = FabricProfile.BuildInstanceName(FabricVersion, MinecraftName)</c>。
    /// 地址由 <see cref="FabricProfile.BuildProfileUrls"/> 产出（镜像在前、官方在后，
    /// MC 版本里的 <c>∞</c> 在该处替换为 <c>infinite</c>）。
    /// </para>
    /// </summary>
    public static NetFileDescriptor FabricMainFile(string fabricVersion, string minecraft, string mcFolder)
    {
        var id = FabricProfile.BuildInstanceName(fabricVersion, minecraft);
        var versionFolder = mcFolder + "versions\\" + id + "\\";
        var localPath = versionFolder + id + ".json";
        var urls = FabricProfile.BuildProfileUrls(fabricVersion, minecraft);
        return new NetFileDescriptor(urls, localPath, isJson: true);
    }
}
