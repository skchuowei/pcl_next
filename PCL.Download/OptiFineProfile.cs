using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.Download;

/// <summary>
/// 旧式（方式 B）OptiFine 版本 json 的构造参数。
/// </summary>
/// <param name="InstanceName">新实例名，用作 json 的 <c>id</c> 与文件名。</param>
/// <param name="Inherit">所继承的 MC 版本。</param>
/// <param name="FileName">OptiFine 安装器文件名，用于推导库名。</param>
/// <param name="ReleaseTime">
/// 列表里的发布时间，形如 <c>2023/06/12 12:00</c>。为空时改用原版实例的发布时间。
/// </param>
/// <param name="InheritReleaseTime">原版实例的发布时间（<c>yyyy-MM-dd</c>），仅在 <paramref name="ReleaseTime"/> 为空时使用。</param>
/// <param name="IsOldJson">原版实例是否为旧版 json 格式（用 <c>minecraftArguments</c> 而不是 <c>arguments</c>）。</param>
/// <param name="MinecraftArguments">原版实例的 <c>minecraftArguments</c>，仅在 <paramref name="IsOldJson"/> 为真时使用。</param>
public sealed record OptiFineLegacyProfile(
    string InstanceName,
    string Inherit,
    string FileName,
    string ReleaseTime,
    string InheritReleaseTime,
    bool IsOldJson,
    string? MinecraftArguments);

/// <summary>
/// OptiFine 版本的「布局」：下载源、安装器落盘位置、旧式版本 json。
/// <para>
/// 这些片段原本在 <c>ModDownloadLib</c> 里各写两份——<c>McDownloadOptiFineLoader</c>（安装）与
/// <c>McDownloadOptiFineSaveLoader</c>（另存为）。两份的 BMCLAPI 地址拼接逐字相同，改其中一份
/// 就会让「装」与「下」打到不同地址。集中到这里后只有一份。
/// </para>
/// </summary>
public static class OptiFineProfile
{
    /// <summary>镜像源根地址。</summary>
    public const string MirrorBase = "https://bmclapi2.bangbang93.com/optifine/";

    /// <summary>官方下载页前缀。页面里带一段登记接口，真正的下载地址要从返回的 HTML 里抠。</summary>
    public const string OfficialPageBase = "https://optifine.net/adloadx?f=";

    /// <summary>官方下载地址前缀，与 <see cref="FindOfficialDownloadUrl"/> 抠出的相对路径拼接。</summary>
    public const string OfficialBase = "https://optifine.net/";

    /// <summary>
    /// 镜像源的下载地址。
    /// </summary>
    /// <param name="inherit">MC 版本。</param>
    /// <param name="isPreview">是否为预览版。</param>
    /// <param name="displayName">列表里的显示名，如 <c>1.20.1 HD U I6</c>、<c>1.8.9 HD U M5 preview</c>。</param>
    /// <remarks>
    /// 预览版与正式版的镜像路径形状不同：正式版是 <c>HD_U/&lt;文件名&gt;</c>，
    /// 预览版是 <c>HD_U_&lt;版本&gt;/preview</c>（显示名里的空格在这里变成斜杠）。
    /// </remarks>
    public static string BuildMirrorUrl(string inherit, bool isPreview, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inherit);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        // 镜像侧 1.8 / 1.9 的目录名带 .0（#4281），官方侧不带。
        var folder = inherit is "1.8" or "1.9" ? inherit + ".0" : inherit;
        var name = displayName.Replace(inherit + " ", "", StringComparison.Ordinal);

        return isPreview
            ? MirrorBase + folder + "/HD_U_" + name.Replace(" ", "/", StringComparison.Ordinal)
            : MirrorBase + folder + "/HD_U/" + name;
    }

    /// <summary>官方下载页地址。</summary>
    public static string BuildOfficialPageUrl(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return OfficialPageBase + fileName;
    }

    /// <summary>
    /// 从官方下载页的 HTML 里抠出真正的下载地址。
    /// </summary>
    /// <returns>完整 URL；页面里不含下载链接时返回 null。</returns>
    /// <remarks>
    /// 官方页面会先返回一个带 <c>downloadx?f=...</c> 的相对链接，直接拼 <c>optifine.net/adloadx</c>
    /// 下到的是 HTML 而不是 jar。抠不到时返回 null 而不是照样拼一个地址——那样会下回一个几百字节的
    /// 错误页，只在后续「最小体积」检查里才失败，日志看不出原因。
    /// </remarks>
    public static string? FindOfficialDownloadUrl(string? pageData)
    {
        if (string.IsNullOrEmpty(pageData)) return null;
        var match = Regex.Match(pageData, "downloadx\\?f=[^\"']+");
        return match.Success ? OfficialBase + match.Value : null;
    }

    /// <summary>
    /// 安装器落到哪里。
    /// </summary>
    /// <param name="isNewerVersion">是否为新版 OptiFine（1.14+）。</param>
    /// <param name="tempFolder">任务临时目录，需以分隔符结尾。</param>
    /// <param name="mcFolder">.minecraft 目录，需以分隔符结尾。</param>
    /// <param name="fileName">安装器文件名。</param>
    /// <remarks>
    /// 新版走临时目录（装完即删）；旧版要留在 <c>libraries\optifine\OptiFine</c> 下，
    /// 因为旧版启动时直接把那个 jar 当库加载。
    /// </remarks>
    public static string BuildInstallerTarget(bool isNewerVersion, string tempFolder, string mcFolder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (isNewerVersion)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tempFolder);
            return tempFolder + "OptiFine.jar";
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mcFolder);
        // 目录名去掉扩展名与前缀，文件名把「OptiFine_」换成「OptiFine-」并保留 preview 标记。
        var folder = StripExtension(fileName)
            .Replace("OptiFine_", "", StringComparison.Ordinal)
            .Replace("preview_", "", StringComparison.Ordinal);
        var name = fileName
            .Replace("OptiFine_", "OptiFine-", StringComparison.Ordinal)
            .Replace("preview_", "", StringComparison.Ordinal);
        return mcFolder + "libraries\\optifine\\OptiFine\\" + folder + "\\" + name;
    }

    /// <summary>
    /// 构造旧式（方式 B）OptiFine 的版本 json。
    /// </summary>
    /// <remarks>
    /// 两处看起来像笔误但必须照搬的历史细节：
    /// <list type="bullet">
    /// <item>时间戳的时分秒写死为 <c>T23:33:33+08:00</c>，列表里只给到分钟。</item>
    /// <item>新版 json 的 <c>minimumLauncherVersion</c> 是字符串 <c>"21"</c>，旧版是数字 <c>18</c>。
    /// 同步成同一种类型会让老版本被启动器判为不兼容。</item>
    /// </list>
    /// </remarks>
    public static string BuildLegacyProfileJson(OptiFineLegacyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.InstanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Inherit);

        var stamp = (profile.ReleaseTime.Length == 0
            ? profile.InheritReleaseTime
            : profile.ReleaseTime.Replace("/", "-", StringComparison.Ordinal)) + "T23:33:33+08:00";

        var json = new JsonObject
        {
            ["id"] = profile.InstanceName,
            ["inheritsFrom"] = profile.Inherit,
            ["time"] = stamp,
            ["releaseTime"] = stamp,
            ["type"] = "release",
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = "optifine:OptiFine:" + OptiFineVersionToken(profile.FileName) },
                new JsonObject { ["name"] = "net.minecraft:launchwrapper:1.12" },
            },
            ["mainClass"] = "net.minecraft.launchwrapper.Launch",
        };

        if (profile.IsOldJson)
        {
            json["minimumLauncherVersion"] = 18;
            json["minecraftArguments"] =
                (profile.MinecraftArguments ?? "") + "  --tweakClass optifine.OptiFineTweaker";
        }
        else
        {
            json["minimumLauncherVersion"] = "21";
            json["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray("--tweakClass", "optifine.OptiFineTweaker"),
            };
        }

        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>从安装器文件名推导库版本号（<c>OptiFine_1.20.1_HD_U_I6.jar</c> → <c>1.20.1_HD_U_I6</c>）。</summary>
    public static string OptiFineVersionToken(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return StripExtension(fileName)
            .Replace("OptiFine_", "", StringComparison.Ordinal)
            .Replace("preview_", "", StringComparison.Ordinal);
    }

    private static string StripExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot <= 0 ? fileName : fileName[..dot];
    }
}
