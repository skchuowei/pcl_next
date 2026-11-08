namespace PCL.Download;

/// <summary>
/// 合并安装里出现的下载文件类别。
/// <para>
/// 计划口径是「九类」（原版 json / 支持库 / 资源索引 / OptiFine / Forge / NeoForge /
/// LiteLoader / Fabric API / OptiFabric）；按 VB 里实际的 <c>NetFile</c> 构造点展开后，
/// 原版三类再细分为 json / jar / library / 资源索引 / 资源文件，另补上另存为场景的
/// Fabric 安装器，加上 dev.6 既有的 Fabric 主文件，共 13 类。
/// 具体构造点与校验口径见 <see cref="InstallDownloads"/> 各工厂的注释。
/// </para>
/// </summary>
public enum NetFileDescriptorKind
{
    /// <summary>原版版本 json（<c>ModDownloadLib.vb:23</c>）。</summary>
    VanillaJson,

    /// <summary>原版客户端 jar（<c>ModDownload.vb:27</c>）。</summary>
    VanillaJar,

    /// <summary>原版支持库 / 合并安装补全的支持库（<c>ModMinecraft.vb:1894</c> 等）。</summary>
    VanillaLibrary,

    /// <summary>资源索引 json（<c>ModDownload.vb:47</c>）。</summary>
    AssetIndex,

    /// <summary>资源对象文件（<c>ModMinecraft.vb:2055 / 2075</c>）。</summary>
    AssetObject,

    /// <summary>OptiFine 安装器主文件（<c>ModDownloadLib.vb:505 / 615</c>）。</summary>
    OptiFineInstaller,

    /// <summary>Forge 安装器主文件（<c>ModDownloadLib.vb:854 / 1037</c>）。</summary>
    ForgeInstaller,

    /// <summary>NeoForge 安装器主文件（<c>ModDownloadLib.vb:849 / 1031</c>）。</summary>
    NeoForgeInstaller,

    /// <summary>LiteLoader 安装器主文件（<c>ModDownloadLib.vb:723</c>，另存为路径）。</summary>
    LiteLoaderInstaller,

    /// <summary>Fabric 安装器主文件（<c>ModDownloadLib.vb:1480</c>，另存为路径）。</summary>
    FabricInstaller,

    /// <summary>Fabric profile json（dev.6 既有，<c>ModDownloadLib.vb:1507</c>）。</summary>
    FabricMain,

    /// <summary>Fabric API（<c>ResourceVersion.ToNetFile</c>）。</summary>
    FabricApi,

    /// <summary>OptiFabric（<c>ResourceVersion.ToNetFile</c>）。</summary>
    OptiFabric,
}

/// <summary>
/// 一个待下载文件的纯数据描述：类别、地址、落盘路径与校验口径。
/// <para>
/// 这是「桥接层第二轮」的产出物（见 <c>docs/plans/计划-Phase4收尾与Phase6下线.md</c> §4.1，
/// 原《计划-Phase4主体》§4.9①）。内核只负责把「下载什么、下到哪、怎么校验」算成这份与执行无关的
/// 描述符，VB 侧据此构造 <c>NetFile</c> 与对应的 <c>LoaderDownload</c> / <c>LoaderTask</c>。
/// 不引用任何 VB 类型，保证内核自包含、可单测；<c>NetFile</c> 构造仍留在 VB
/// （口径 A：不动 <c>LoaderBase</c> 签名，不搬含宿主资产的 lambda）。
/// </para>
/// </summary>
public sealed class NetFileDescriptor
{
    /// <summary>下载文件类别。默认取既有的 Fabric 主文件，保持 dev.6 调用方兼容。</summary>
    public NetFileDescriptorKind Kind { get; }

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

    /// <summary>
    /// 是否模拟浏览器请求头。资源详情页的 <c>ToNetFile</c> 传 <c>SimulateBrowserHeaders:=True</c>，
    /// 其余 <c>NetFile</c> 构造用默认值 False。
    /// </summary>
    public bool SimulateBrowserHeaders { get; }

    public NetFileDescriptor(
        IReadOnlyList<string> urls,
        string localPath,
        bool isJson = false,
        long? minSize = null,
        long? maxSize = null,
        string? hash = null,
        long? actualSize = null,
        NetFileDescriptorKind kind = NetFileDescriptorKind.FabricMain,
        bool simulateBrowserHeaders = false)
    {
        Urls = urls;
        LocalPath = localPath;
        IsJson = isJson;
        MinSize = minSize;
        MaxSize = maxSize;
        Hash = hash;
        ActualSize = actualSize;
        Kind = kind;
        SimulateBrowserHeaders = simulateBrowserHeaders;
    }
}

/// <summary>
/// 合并安装里各个加载器「下载文件清单」的纯计算层。输入基元 / 描述符，输出 <see cref="NetFileDescriptor"/>。
/// <para>
/// P4-A 把 dev.6 仅有的一项（Fabric 主文件）扩到覆盖全部类别。<b>地址与路径仍由 VB 侧传入</b>——
/// 它们来自资源对象、镜像解析与版本 JSON 解析，内核不重复实现这些宿主逻辑；
/// 内核负责的是「按类别决定校验口径、字段来源与候选源顺序」，即把 VB 里散落的
/// <c>New FileChecker With {…}</c> 收编成可单测的单一真源。
/// 逐类对照见 <c>PCL.Download.Tests/InstallTaskDescriptorTests.cs</c>。
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
        return new NetFileDescriptor(urls, localPath, isJson: true, kind: NetFileDescriptorKind.FabricMain);
    }

    /// <summary>
    /// 原版版本 json：<c>ModDownloadLib.vb:23</c> 的
    /// <c>New FileChecker With {.IsJson = True}</c>。
    /// </summary>
    public static NetFileDescriptor VanillaJson(IReadOnlyList<string> urls, string localPath)
        => new(urls, localPath, isJson: true, kind: NetFileDescriptorKind.VanillaJson);

    /// <summary>
    /// 原版客户端 jar：<c>ModDownload.vb:23 / 27</c> 的
    /// <c>{.MinSize = 1024, .ActualSize = size, .Hash = sha1}</c>。
    /// <para><paramref name="size"/> 为 <c>null</c> 时对齐 VB 的 <c>If(…, -1)</c> 回退。</para>
    /// </summary>
    public static NetFileDescriptor VanillaJar(
        IReadOnlyList<string> urls, string localPath, string? sha1, long? size)
        => new(urls, localPath, minSize: 1024, hash: sha1, actualSize: size ?? -1,
            kind: NetFileDescriptorKind.VanillaJar);

    /// <summary>
    /// 支持库文件：<c>McLibNetFilesFromTokens</c> / <c>McLibNetFilesFromInstance</c> 产出，
    /// 校验口径为 <c>{.ActualSize, .Hash}</c>（<c>ModMinecraft.vb:1894</c>）。
    /// </summary>
    public static NetFileDescriptor VanillaLibrary(
        IReadOnlyList<string> urls, string localPath, string? hash = null, long? actualSize = null)
        => new(urls, localPath, hash: hash, actualSize: actualSize,
            kind: NetFileDescriptorKind.VanillaLibrary);

    /// <summary>
    /// 资源索引 json：<c>ModDownload.vb:47</c> 的 <c>{.IsJson = True}</c>。
    /// </summary>
    public static NetFileDescriptor AssetIndex(IReadOnlyList<string> urls, string localPath)
        => new(urls, localPath, isJson: true, kind: NetFileDescriptorKind.AssetIndex);

    /// <summary>
    /// 资源对象文件：<c>ModMinecraft.vb:2055 / 2075</c> 的
    /// <c>{.ActualSize = If(Size = 0, -1, Size), .Hash = Hash}</c>。
    /// </summary>
    public static NetFileDescriptor AssetObject(
        IReadOnlyList<string> urls, string localPath, string hash, long size)
        => new(urls, localPath, actualSize: size == 0 ? -1 : size, hash: hash,
            kind: NetFileDescriptorKind.AssetObject);

    /// <summary>
    /// OptiFine 安装器主文件。<c>ModDownloadLib.vb:505</c>（安装路径）用 300 KB 下限，
    /// <c>:615</c>（另存为 / 作为 Mod）用 64 KB 下限——两者都用于识别「下到了错误页」。
    /// </summary>
    public static NetFileDescriptor OptiFineInstaller(
        IReadOnlyList<string> urls, string localPath, bool asMod)
        => new(urls, localPath, minSize: asMod ? 64 * 1024 : 300 * 1024,
            kind: NetFileDescriptorKind.OptiFineInstaller);

    /// <summary>
    /// Forge 安装器主文件：<c>ModDownloadLib.vb:854 / 1037</c> 的
    /// <c>{.MinSize = Installer.MinSize, .Hash = Installer.Hash}</c>。
    /// </summary>
    public static NetFileDescriptor ForgeInstaller(ForgelikeInstallerFile installer, string localPath)
        => new(installer.Urls, localPath, minSize: installer.MinSize, hash: installer.Hash,
            kind: NetFileDescriptorKind.ForgeInstaller);

    /// <summary>
    /// NeoForge 安装器主文件：<c>ModDownloadLib.vb:849 / 1031</c> 只给
    /// <c>{.MinSize = Installer.MinSize}</c>，<b>不带 Hash</b>（与 Forge 不同，不得顺手补上）。
    /// </summary>
    public static NetFileDescriptor NeoForgeInstaller(ForgelikeInstallerFile installer, string localPath)
        => new(installer.Urls, localPath, minSize: installer.MinSize,
            kind: NetFileDescriptorKind.NeoForgeInstaller);

    /// <summary>
    /// LiteLoader 安装器（另存为路径，<c>ModDownloadLib.vb:723</c>）：<c>{.MinSize = 1024 * 1024}</c>。
    /// 合并安装里的 LiteLoader 只生成版本 json，不产生 <c>NetFile</c>。
    /// </summary>
    public static NetFileDescriptor LiteLoaderInstaller(IReadOnlyList<string> urls, string localPath)
        => new(urls, localPath, minSize: 1024 * 1024,
            kind: NetFileDescriptorKind.LiteLoaderInstaller);

    /// <summary>
    /// Fabric 安装器（另存为路径，<c>ModDownloadLib.vb:1480</c>）：<c>{.MinSize = 1024 * 64}</c>。
    /// BMCLAPI 不支持该安装器，候选地址只有一个官方源。
    /// </summary>
    public static NetFileDescriptor FabricInstaller(IReadOnlyList<string> urls, string localPath)
        => new(urls, localPath, minSize: 1024 * 64,
            kind: NetFileDescriptorKind.FabricInstaller);

    /// <summary>
    /// Fabric API / OptiFabric 依赖 Mod：对齐 <c>ResourceVersion.ToNetFile</c>（<c>ResourceVersion.vb:383</c>）
    /// 的 <c>{.Hash = Hash, .ActualSize = Size}</c> 与 <c>SimulateBrowserHeaders:=True</c>。
    /// </summary>
    public static NetFileDescriptor ResourceDependency(
        NetFileDescriptorKind kind, IReadOnlyList<string> urls, string localPath,
        string? hash, long? size)
    {
        if (kind is not (NetFileDescriptorKind.FabricApi or NetFileDescriptorKind.OptiFabric))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "仅支持 FabricApi / OptiFabric 两类依赖 Mod");
        return new NetFileDescriptor(urls, localPath, hash: hash, actualSize: size,
            kind: kind, simulateBrowserHeaders: true);
    }
}
