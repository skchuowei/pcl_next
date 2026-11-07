namespace PCL.Download;

/// <summary>
/// OptiFine 版本列表的一条记录（平移自 VB <c>DlOptiFineListEntry</c>）。
/// </summary>
public sealed class OptiFineListEntry
{
    /// <summary>显示名称，已去除 HD_U 字样，如「1.12.2 C8」。</summary>
    public required string DisplayName { get; init; }

    /// <summary>原始文件名称，如「preview_OptiFine_1.11_HD_U_E1_pre.jar」。</summary>
    public required string FileName { get; init; }

    /// <summary>对应的版本名称，如「1.13.2-OptiFine_HD_U_E6」。</summary>
    public required string InstanceName { get; init; }

    /// <summary>是否为测试版。</summary>
    public required bool IsPreview { get; init; }

    /// <summary>对应的 Minecraft 版本，如「1.12.2」（已去掉末尾的 <c>.0</c>）。</summary>
    public required string Inherit { get; init; }

    /// <summary>发布时间，格式为「yyyy/MM/dd」；BMCLAPI 源无此数据（空串）。</summary>
    public required string ReleaseTime { get; init; }

    /// <summary>
    /// 需要的最低 Forge 版本。空串为无限制，null 为不兼容，「28.1.56」表示版本号，「1161」表示版本号的最后一位。
    /// </summary>
    public required string? RequiredForgeVersion { get; init; }
}

/// <summary>
/// Forge 版本列表的一条记录（平移自 VB <c>DlForgeVersionEntry</c>）。
/// </summary>
public sealed class ForgeVersionEntry
{
    /// <summary>标准化后的版本号，仅用于比较与排序，格式 Major.Minor.Build.Revision（最后一位固定为 0）。</summary>
    public required Version Version { get; init; }

    /// <summary>可读的非格式化版本名，如「50.1.9」「14.22.1.2478」。</summary>
    public required string VersionName { get; init; }

    /// <summary>对应的 Minecraft 版本，如「1.12.2」。</summary>
    public required string Inherit { get; init; }

    /// <summary>用于下载的文件版本名，可能在 <see cref="VersionName"/> 基础上带分支，如「1.7.10-1614」。</summary>
    public required string FileVersion { get; init; }

    /// <summary>版本分支（已施加 VB 构造函数里的两处特例修正）；null 表示无分支。</summary>
    public string? Branch { get; init; }

    /// <summary>发布时间，格式为「yyyy/MM/dd HH:mm」。</summary>
    public required string ReleaseTime { get; init; }

    /// <summary>文件的 MD5 或 SHA1（镜像的老版本是 MD5，新版本是 SHA1；官方源总是 MD5）；缺失为 null。</summary>
    public required string? Hash { get; init; }

    /// <summary>是否为推荐版本。</summary>
    public required bool IsRecommended { get; init; }

    /// <summary>安装类型：installer / client / universal / unknown。</summary>
    public required string Category { get; init; }

    /// <summary>文件扩展名，不以小数点开头。</summary>
    public string FileExtension => Category == "installer" ? "jar" : "zip";

    /// <summary>
    /// 构造一条记录，并施加 VB 构造函数里的两处版本特例（「司马版本」与 1.7.10 后期构建的分支修正）。
    /// </summary>
    /// <param name="versionName">版本名，如「50.1.9」。</param>
    /// <param name="branch">分支；null 表示无分支。</param>
    /// <param name="inherit">对应的 Minecraft 版本。</param>
    /// <param name="category">安装类型：installer / client / universal / unknown。</param>
    /// <param name="hash">文件哈希；null 表示源未提供。</param>
    /// <param name="isRecommended">是否为推荐版本。</param>
    /// <param name="releaseTime">发布时间，格式「yyyy/MM/dd HH:mm」。</param>
    public static ForgeVersionEntry Create(string versionName, string? branch, string inherit,
        string category = "unknown", string? hash = null, bool isRecommended = false, string releaseTime = "")
    {
        // 司马版本的特殊处理（原注释如此）
        if (versionName is "11.15.1.2318" or "11.15.1.1902" or "11.15.1.1890") branch = "1.8.9";
        if (branch is null && inherit == "1.7.10" && FourthSegment(versionName) >= 1300) branch = "1.7.10";

        return new ForgeVersionEntry
        {
            Version = new Version(versionName),
            VersionName = versionName,
            Inherit = inherit,
            FileVersion = branch is null ? versionName : $"{versionName}-{branch}",
            Branch = branch,
            ReleaseTime = releaseTime,
            Hash = hash,
            IsRecommended = isRecommended,
            Category = category,
        };
    }

    /// <summary>
    /// 取版本号的第四段；不足四段或无法解析时返回 0（VB 原实现直接索引，少于四段会抛异常）。
    /// </summary>
    private static int FourthSegment(string versionName)
    {
        var parts = versionName.Split('.');
        return parts.Length >= 4 && int.TryParse(parts[3], out var value) ? value : 0;
    }
}

/// <summary>
/// NeoForge 版本列表的一条记录（平移自 VB <c>DlNeoForgeListEntry</c>）。
/// </summary>
public sealed class NeoForgeListEntry
{
    /// <summary>API 使用的原始版本字符串，如「20.4.30-beta」「1.20.1-47.1.99」（Legacy）。</summary>
    public required string ApiName { get; init; }

    /// <summary>标准化后的版本号，仅用于排序；NeoForge 项不包含 alpha/beta 编号。</summary>
    public required Version Version { get; init; }

    /// <summary>可读的非格式化版本名，如「20.4.30-beta」「47.1.99」（Legacy）。</summary>
    public required string VersionName { get; init; }

    /// <summary>对应的 Minecraft 版本，如「1.20.4」「1.20.1」「26.1-snapshot-1」。</summary>
    public required string Inherit { get; init; }

    /// <summary>是否是 Beta 版（含 alpha）。</summary>
    public required bool IsBeta { get; init; }

    /// <summary>文件在官网的基础地址，不包含后缀。</summary>
    public string UrlBase
    {
        get
        {
            var packageName = Inherit == "1.20.1" ? "forge" : "neoforge";
            return $"https://maven.neoforged.net/releases/net/neoforged/{packageName}/{ApiName}/{packageName}-{ApiName}";
        }
    }
}

/// <summary>
/// LiteLoader 版本列表的一条记录（平移自 VB <c>DlLiteLoaderListEntry</c>）。
/// </summary>
public sealed class LiteLoaderListEntry
{
    /// <summary>实际的文件名，如「liteloader-installer-1.12-00-SNAPSHOT.jar」。</summary>
    public required string FileName { get; init; }

    /// <summary>是否为测试版。</summary>
    public required bool IsPreview { get; init; }

    /// <summary>对应的 Minecraft 版本，如「1.12.2」。</summary>
    public required string Inherit { get; init; }

    /// <summary>是否为 1.7 及更早的远古版。</summary>
    public required bool IsLegacy { get; init; }

    /// <summary>发布时间，格式为「yyyy/MM/dd HH:mm」。</summary>
    public required string ReleaseTime { get; init; }

    /// <summary>文件的 MD5。</summary>
    public required string? Md5 { get; init; }

    /// <summary>
    /// 该版本的原始 JSON（对应 VB 的 <c>JsonToken</c>）。
    /// 安装编排要从中读取 <c>tweakClass</c> / <c>libraries</c> / <c>version</c>，不能只留解析后的字段。
    /// </summary>
    public required string RawJson { get; init; }
}

/// <summary>
/// 加载器列表的获取结果。
/// </summary>
public sealed class LoaderListResult<T>
{
    /// <summary>实际命中的源名称，例如「BMCLAPI」「Forge 官方源」。</summary>
    public required string SourceName { get; init; }

    /// <summary>命中源是否为官方实时数据。</summary>
    public required bool IsOfficial { get; init; }

    /// <summary>解析后的条目（Fabric 列表为空，见 <see cref="RawJson"/>）。</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>原样透传的 JSON 文本（仅 Fabric 列表使用，UI 直接读取 game / loader / installer 三个数组）。</summary>
    public string? RawJson { get; init; }
}

/// <summary>
/// 各类加载器列表的地址与展示名（平移自 VB <c>ModDownload</c> 各区域里的字面量）。
/// </summary>
public static class LoaderListSources
{
    #region OptiFine

    /// <summary>OptiFine 官方下载页（HTML）。</summary>
    public const string OptiFineOfficialUrl = "https://optifine.net/downloads";

    /// <summary>OptiFine 的 BMCLAPI 版本列表（JSON）。</summary>
    public const string OptiFineMirrorUrl = "https://bmclapi2.bangbang93.com/optifine/versionList";

    /// <summary>OptiFine 条目数下限：低于该值视为「拿到了错误页面」（VB 原值）。</summary>
    public const int OptiFineMinimumCount = 10;

    #endregion

    #region Forge 的 MC 版本列表

    /// <summary>Forge 官方按 MC 版本分页的索引页（取 1.2.4，因其包含全部版本链接）。</summary>
    public const string ForgeGameListOfficialUrl =
        "https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html";

    /// <summary>Forge 的 BMCLAPI MC 版本列表（JSON 字符串数组）。</summary>
    public const string ForgeGameListMirrorUrl = "https://bmclapi2.bangbang93.com/forge/minecraft";

    /// <summary>Forge MC 版本数下限（VB 原值）。</summary>
    public const int ForgeGameListMinimumCount = 10;

    #endregion

    #region Forge 版本列表

    /// <summary>Forge 官方某个 MC 版本下的版本列表页。</summary>
    /// <param name="mcVersion">MC 版本名；连字符会换成下划线以兼容 1.7.10-pre4（#4057）。</param>
    public static string ForgeVersionsOfficialUrl(string mcVersion)
        => $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_{mcVersion.Replace('-', '_')}.html";

    /// <summary>Forge 的 BMCLAPI 某个 MC 版本下的版本列表。</summary>
    /// <param name="mcVersion">MC 版本名；连字符会换成下划线以兼容 1.7.10-pre4（#4057）。</param>
    public static string ForgeVersionsMirrorUrl(string mcVersion)
        => $"https://bmclapi2.bangbang93.com/forge/minecraft/{mcVersion.Replace('-', '_')}";

    #endregion

    #region NeoForge

    /// <summary>NeoForge 官方 Maven 版本列表（新版）。</summary>
    public const string NeoForgeOfficialLatestUrl =
        "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";

    /// <summary>NeoForge 官方 Maven 版本列表（1.20.1 的 Legacy 包）。</summary>
    public const string NeoForgeOfficialLegacyUrl =
        "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge";

    /// <summary>NeoForge 镜像 Maven 版本列表（新版）。</summary>
    public const string NeoForgeMirrorLatestUrl =
        "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge";

    /// <summary>NeoForge 镜像 Maven 版本列表（Legacy 包）。</summary>
    public const string NeoForgeMirrorLegacyUrl =
        "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge";

    /// <summary>响应长度下限：低于该值视为「拿到了错误页面」（VB 原值）。</summary>
    public const int NeoForgeMinimumLength = 100;

    #endregion

    #region LiteLoader / Fabric

    /// <summary>LiteLoader 官方版本清单。</summary>
    public const string LiteLoaderOfficialUrl = "https://dl.liteloader.com/versions/versions.json";

    /// <summary>LiteLoader 的 BMCLAPI 版本清单。</summary>
    public const string LiteLoaderMirrorUrl = "https://bmclapi2.bangbang93.com/maven/com/mumfrey/liteloader/versions.json";

    /// <summary>Fabric 官方元数据。</summary>
    public const string FabricOfficialUrl = "https://meta.fabricmc.net/v2/versions";

    /// <summary>Fabric 的 BMCLAPI 元数据。</summary>
    public const string FabricMirrorUrl = "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions";

    #endregion
}
