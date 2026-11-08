using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Download.Platform;

namespace PCL.Cli;

/// <summary>版本 JSON 中的一个可下载条目（client / assetIndex / logging client）。</summary>
internal sealed class VersionDownload
{
    public string Url { get; init; } = "";
    public string Sha1 { get; init; } = "";
    public long Size { get; init; } = -1;
    /// <summary>相对路径（libraries 才有；其余由调用方拼）。</summary>
    public string? Path { get; init; }
    /// <summary>标识（assetIndex.id / logging.file.id）。</summary>
    public string? Id { get; init; }
}

/// <summary>版本 JSON 中的一条依赖库。</summary>
internal sealed class VersionLibrary
{
    public string Name { get; init; } = "";
    public VersionDownload? Artifact { get; init; }
    /// <summary>该库的 rules 是否适用于当前运行环境（Windows）。</summary>
    public bool AppliesHere { get; init; }
}

/// <summary>
/// Mojang 版本 JSON 的最小解析器（《计划-内核与GUI拆分（CLI）》§4 的 <c>install</c> 命令）。
/// <para/>
/// **只解析下载段所需字段**：<c>downloads.client</c> / <c>libraries[].downloads.artifact</c> /
/// <c>assetIndex</c> / <c>logging.client.file</c>。启动参数、JVM 参数、mainClass 等一律不解析
/// （那是 Phase 6 之后内核域的事，本版不做）。
/// </para>
/// <para/>
/// **明确不做**：natives 分类器下载与解压、assets objects 全量下载（可用 <c>--objects</c> 开启）、
/// <c>inheritsFrom</c> 继承链合并。缺省行为偏向「不做」而不是「做一半」。
/// </para>
/// </summary>
internal sealed class VersionJson
{
    public required string Id { get; init; }
    public VersionDownload? Client { get; init; }
    public VersionDownload? AssetIndex { get; init; }
    public VersionDownload? LoggingClient { get; init; }
    public required IReadOnlyList<VersionLibrary> Libraries { get; init; }

    /// <summary>按 Mojang 规则判定当前平台名。实现统一在内核的 <see cref="PlatformInfo"/>。</summary>
    public static string CurrentOsName => PlatformInfo.OsName;

    /// <summary>当前进程架构在 Mojang rules 里的写法。实现统一在内核的 <see cref="PlatformInfo"/>。</summary>
    public static string CurrentOsArch => PlatformInfo.ArchName;

    public static VersionJson Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CliUsageException("版本 JSON 解析失败：" + ex.Message);
        }

        if (root is not JsonObject obj)
            throw new CliUsageException("版本 JSON 的根节点不是对象，无法识别");

        var id = obj["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
            throw new CliUsageException("版本 JSON 缺少 id 字段");

        var libraries = new List<VersionLibrary>();
        if (obj["libraries"] is JsonArray libraryArray)
        {
            foreach (var node in libraryArray)
            {
                if (node is not JsonObject library) continue;
                var artifact = library["downloads"]?["artifact"];
                libraries.Add(new VersionLibrary
                {
                    Name = library["name"]?.GetValue<string>() ?? "",
                    AppliesHere = EvaluatesHere(library["rules"] as JsonArray),
                    Artifact = artifact is null ? null : ReadDownload(artifact, withPath: true),
                });
            }
        }

        return new VersionJson
        {
            Id = id,
            Client = obj["downloads"]?["client"] is JsonObject client ? ReadDownload(client, false) : null,
            AssetIndex = obj["assetIndex"] is JsonObject assetIndex
                ? ReadDownload(assetIndex, false, idKey: "id")
                : null,
            LoggingClient = obj["logging"]?["client"]?["file"] is JsonObject logging
                ? ReadDownload(logging, false, idKey: "id")
                : null,
            Libraries = libraries,
        };
    }

    private static VersionDownload ReadDownload(JsonNode node, bool withPath, string? idKey = null)
        => new()
        {
            Url = node["url"]?.GetValue<string>() ?? "",
            Sha1 = node["sha1"]?.GetValue<string>() ?? "",
            Size = node["size"] is JsonValue size && size.TryGetValue<long>(out var sz) ? sz : -1,
            Path = withPath ? node["path"]?.GetValue<string>() : null,
            Id = idKey is null ? null : node[idKey]?.GetValue<string>(),
        };

    /// <summary>
    /// Mojang rules 求值：无 rules 视为生效；有 rules 时默认不生效，按顺序匹配、后者覆盖前者。
    /// 只处理 <c>os.name</c> / <c>os.arch</c>；带 <c>features</c> 的规则一律视为不匹配（CLI 不声明任何 feature）。
    /// </summary>
    private static bool EvaluatesHere(JsonArray? rules)
    {
        if (rules is null || rules.Count == 0) return true;

        var allowed = false;
        foreach (var node in rules)
        {
            if (node is not JsonObject rule) continue;
            if (!RuleMatches(rule)) continue;
            allowed = string.Equals(rule["action"]?.GetValue<string>(), "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    private static bool RuleMatches(JsonObject rule)
    {
        // 带 features 的规则（如 is_demo_user）在 CLI 场景下一律不满足
        if (rule["features"] is JsonObject features && features.Count > 0) return false;

        if (rule["os"] is not JsonObject os) return true;

        if (os["name"] is JsonValue nameNode)
        {
            var name = nameNode.GetValue<string>();
            if (!string.Equals(name, CurrentOsName, StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (os["arch"] is JsonValue archNode)
        {
            var arch = archNode.GetValue<string>();
            // Mojang 里 x86 的规则只对 32 位生效；x64/arm64 直接比较
            if (!string.Equals(arch, CurrentOsArch, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
