using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// LiteLoader 版本 json 的构造。
/// <para>
/// 原实现把整份 json 用字符串拼接写在 <c>ModDownloadLib.McDownloadLiteLoaderLoader</c> 里，
/// 其中夹着一条隐式规则：<c>libraries</c> 取列表条目自带的那一份，再追加 <c>com.mumfrey:liteloader</c>。
/// 少了追加的那条，游戏会在启动时找不到 tweaker；追加顺序颠倒则会被后面的同名库覆盖。
/// </para>
/// <para>
/// 拼接改为 <see cref="JsonObject"/> 后字段顺序与转义由库负责，语义与原来一致。
/// </para>
/// </summary>
public static class LiteLoaderProfile
{
    /// <summary>LiteLoader 库的 maven 仓库地址。缺了它启动器无法解析 <c>com.mumfrey:liteloader</c>。</summary>
    public const string MavenUrl = "https://dl.liteloader.com/versions/";

    /// <summary>列表条目里发布时间的格式。</summary>
    public const string ReleaseTimeFormat = "yyyy/MM/dd HH:mm";

    /// <summary>版本文件夹与 json 名的后缀。</summary>
    public const string InstanceSuffix = "-LiteLoader";

    /// <summary>
    /// 由列表条目构造 LiteLoader 的版本 json 文本。
    /// </summary>
    /// <param name="loaderJson">
    /// 列表条目里的 <c>latest</c> 节点 JSON 原文，需要含 <c>tweakClass</c>、<c>version</c>、<c>libraries</c>。
    /// </param>
    /// <param name="inherit">对应的 MC 版本，如 <c>1.12.2</c>。</param>
    /// <param name="releaseTime">发布时间，格式见 <see cref="ReleaseTimeFormat"/>。</param>
    /// <exception cref="InvalidOperationException">条目缺少必需字段，或发布时间格式不符。</exception>
    public static string BuildVersionJson(string loaderJson, string inherit, string releaseTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(inherit);

        var node = JsonNode.Parse(loaderJson) as JsonObject
                   ?? throw new InvalidOperationException("LiteLoader 条目不是 JSON 对象");

        var versionName = inherit + InstanceSuffix;
        // 时间只精确到分钟，秒位按 0 处理；这两个字段直接影响启动器的版本排序，不能留空。
        var time = ParseReleaseTime(releaseTime, inherit);

        // libraries 必须是数组：单值或缺失都说明条目结构变了，此时生成出来的版本一定启动不了。
        var libraries = node["libraries"] as JsonArray
                        ?? throw new InvalidOperationException($"LiteLoader {inherit} 条目缺少 libraries 数组");
        // 深拷贝：不能就地改动调用方传入的文档
        libraries = (JsonArray)libraries.DeepClone();
        libraries.Add(new JsonObject
        {
            ["name"] = "com.mumfrey:liteloader:" + RequireString(node, "version", inherit),
            ["url"] = MavenUrl,
        });

        var json = new JsonObject
        {
            ["id"] = versionName,
            ["time"] = time,
            ["releaseTime"] = time,
            ["type"] = "release",
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray("--tweakClass", RequireString(node, "tweakClass", inherit)),
            },
            ["libraries"] = libraries,
            ["mainClass"] = "net.minecraft.launchwrapper.Launch",
            ["minimumLauncherVersion"] = 18,
            ["inheritsFrom"] = inherit,
            // jar 指向原版的客户端 jar：LiteLoader 只是 tweaker，不带自己的 jar。
            ["jar"] = inherit,
        };

        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ParseReleaseTime(string releaseTime, string inherit)
    {
        if (!DateTime.TryParseExact(releaseTime, ReleaseTimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            throw new InvalidOperationException(
                $"LiteLoader {inherit} 的发布时间格式不符（期望 {ReleaseTimeFormat}）：{releaseTime}");
        return parsed.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);
    }

    private static string RequireString(JsonObject node, string name, string inherit)
    {
        var value = node[name]?.AsString();
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"LiteLoader {inherit} 条目缺少 {name}");
        return value;
    }
}
