using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 参与合并的一份版本 profile，对应一个加载器目录。
/// </summary>
/// <param name="Name">显示名，仅用于日志与报错，例如 <c>Minecraft</c> / <c>Forge</c>。</param>
/// <param name="Folder">该 profile 所在目录，json 文件名取目录末段（<c>versions/1.20.1-Forge/1.20.1-Forge.json</c>）。</param>
public sealed record VersionMergeSource(string Name, string Folder);

/// <summary>
/// 已读入内存的 profile。
/// </summary>
/// <param name="Name">显示名。</param>
/// <param name="JsonText">json 原文，会在合并前统一解析。</param>
public sealed record VersionProfile(string Name, string JsonText);

/// <summary>
/// 版本合并：把原版 json 与若干加载器（OptiFine / Forge / NeoForge / LiteLoader / Fabric）的
/// install profile 合成一份完整版本 json。
/// <para>
/// 平移自 VB 的 <c>ModDownloadLib.MergeJson</c>。合并规则刻意与 Newtonsoft 的
/// <c>JObject.Merge</c> 默认设置保持一致（见 <see cref="MergeInto"/>），因为换用
/// System.Text.Json 后这套语义不会自动继承：
/// </para>
/// <list type="bullet">
/// <item>对象递归合并，同名对象继续往下钻；</item>
/// <item>数组<b>拼接</b>（目标在前、来源追加），而不是覆盖——<c>libraries</c>/<c>arguments</c> 依赖这条；</item>
/// <item>来源值为 <c>null</c> 时<b>忽略</b>，不把目标已有的键清空；</item>
/// <item>其余情况（标量、类型不同）直接替换。</item>
/// </list>
/// <para>
/// 合并顺序即加载器叠加顺序：原版在前，越靠后的加载器越优先（后合并者覆盖同名标量）。
/// </para>
/// </summary>
public static class VersionMerger
{
    /// <summary>
    /// 合并多份 profile，返回合并后的 json 对象。<paramref name="versionName"/> 会写入 <c>id</c>。
    /// </summary>
    /// <param name="profiles">首项必须是原版 profile，其余为加载器，按叠加顺序排列。</param>
    /// <param name="versionName">目标版本名，作为合并结果的 <c>id</c>。</param>
    public static JsonObject Merge(IReadOnlyList<VersionProfile> profiles, string versionName)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionName);
        if (profiles.Count == 0) throw new ArgumentException("没有可合并的 profile。", nameof(profiles));

        var parsed = new List<(string Name, JsonObject Json)>(profiles.Count);
        foreach (var profile in profiles)
        {
            if (!TryParseObject(profile.JsonText, out var json))
                throw new InvalidDataException($"{profile.Name} json 有误，前段内容：{Head(profile.JsonText)}");
            parsed.Add((profile.Name, json));
        }

        // 先收齐 minecraftArguments，避免合并过程中被后来的加载器覆盖掉原版参数。
        // 缺项记作一个空格（对齐 VB 的 If(x?.ToString(), " ")）。
        var allArguments = string.Join(" ",
            parsed.Select(p => p.Json["minecraftArguments"].AsString() ?? " "));

        var merged = parsed[0].Json;
        for (var i = 1; i < parsed.Count; i++)
        {
            // releaseTime / time 表示「这份 profile 什么时候生成的」，合并后若被加载器的时间覆盖，
            // 版本列表里就会显示成加载器的发布时间，故先剔掉。
            parsed[i].Json.Remove("releaseTime");
            parsed[i].Json.Remove("time");
            MergeInto(merged, parsed[i].Json);
        }

        ApplyMinecraftArguments(merged, allArguments);

        merged.Remove("_comment_");
        merged.Remove("inheritsFrom");
        // jar 指向「原版 jar 用哪个版本」，合并成独立版本后自己有 jar，留着会让启动器去别处找。
        merged.Remove("jar");
        merged["id"] = versionName;
        return merged;
    }

    /// <summary>合并并序列化为 json 文本（缩进，便于人工比对与 diff）。</summary>
    public static string MergeToText(IReadOnlyList<VersionProfile> profiles, string versionName)
        => Merge(profiles, versionName).ToJsonString(WriteOptions);

    /// <summary>
    /// 从磁盘读取各 profile 并合并，结果写回 <paramref name="versionFolder"/> 下的
    /// <c>{版本名}.json</c>。返回合并后的文本。
    /// </summary>
    public static string MergeToDirectory(IReadOnlyList<VersionMergeSource> sources, string versionFolder)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionFolder);
        if (sources.Count == 0) throw new ArgumentException("没有可合并的 profile。", nameof(sources));

        DownloadLog.Info($"开始进行版本合并，输出：{versionFolder}，"
            + string.Join("，", sources.Select(source => $"{source.Name}：{source.Folder}")));

        var profiles = new List<VersionProfile>(sources.Count);
        foreach (var source in sources)
        {
            var profilePath = Path.Combine(source.Folder, LastPart(source.Folder) + ".json");
            var profileText = File.Exists(profilePath) ? File.ReadAllText(profilePath) : "";
            // 加载器安装器偶尔会留下空文件或 HTML 错误页，早点报出来比在后面崩掉好定位。
            if (!profileText.StartsWith('{'))
                throw new InvalidDataException(
                    $"{source.Name} json 有误，地址：{profilePath}，前段内容：{Head(profileText)}");
            profiles.Add(new VersionProfile(source.Name, profileText));
        }

        var versionName = LastPart(versionFolder);
        var text = MergeToText(profiles, versionName);
        Directory.CreateDirectory(versionFolder);
        WriteWithRetry(Path.Combine(versionFolder, versionName + ".json"), text);
        DownloadLog.Info($"版本合并 {versionName} 完成");
        return text;
    }

    /// <summary>
    /// 写文件。版本 json 可能被杀毒软件或索引服务短暂占用，重试几次再放弃
    /// （对齐 VB <c>FileUtils.Write</c> 的重试语义；不重试会让「偶发占用」变成安装失败）。
    /// </summary>
    private static void WriteWithRetry(string path, string text, int attempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, text);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static bool TryParseObject(string? text, out JsonObject json)
    {
        json = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            if (JsonNode.Parse(text, nodeOptions: null, DocumentOptions) is not JsonObject obj) return false;
            json = obj;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 深合并 <paramref name="source"/> 到 <paramref name="target"/>，语义见类型注释。
    /// 赋给目标的值一律深克隆，避免同一个节点挂在两棵树上（STJ 的节点只允许一个父级）。
    /// </summary>
    internal static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (key, sourceValue) in source.ToList())
        {
            if (sourceValue is null) continue; // MergeNullValueHandling.Ignore
            if (!target.TryGetPropertyValue(key, out var targetValue) || targetValue is null)
            {
                target[key] = sourceValue.DeepClone();
                continue;
            }
            if (targetValue is JsonObject targetObject && sourceValue is JsonObject sourceObject)
            {
                MergeInto(targetObject, sourceObject);
            }
            else if (targetValue is JsonArray targetArray && sourceValue is JsonArray sourceArray)
            {
                foreach (var item in sourceArray.ToList()) targetArray.Add(item?.DeepClone());
            }
            else
            {
                target[key] = sourceValue.DeepClone();
            }
        }
    }

    /// <summary>
    /// 把各 profile 的 <c>minecraftArguments</c> 拼回一条「去重、且参数值不与 flag 断开」的字符串。
    /// <para>
    /// 旧版启动器只认这条扁平字符串，加载器会往里塞 <c>--tweakClass xxx</c> 这类成对参数。
    /// 简单按空格切开再 join 会把 <c>--tweakClass</c> 和它的值分散到两处（值在去重时可能与别的
    /// flag 撞车），所以这里把「紧跟 flag 后面的第一个非 flag 词」吸附到 flag 上，
    /// 再做整体去重。多个值的情况（<c>-flag a b</c>）刻意只吸附第一个，与 VB 实现一致。
    /// </para>
    /// </summary>
    private static void ApplyMinecraftArguments(JsonObject merged, string allArguments)
    {
        var parts = new List<string>();
        foreach (var argument in allArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()))
        {
            if (!argument.StartsWith('-') && parts.Count > 0)
            {
                var last = parts[^1];
                if (last.StartsWith('-') && !last.Contains(' '))
                {
                    parts[^1] = last + " " + argument;
                    continue;
                }
            }
            parts.Add(argument);
        }
        if (parts.Count > 0) merged["minecraftArguments"] = string.Join(" ", parts.Distinct());
    }

    /// <summary>取路径末段（去尾部斜杠后取最后一个分隔符之后的部分）。</summary>
    private static string LastPart(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    private static string Head(string? text)
        => text is null ? "" : text[..Math.Min(text.Length, 1000)];
}
