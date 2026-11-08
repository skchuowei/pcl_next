using System.Globalization;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// System.Text.Json.Nodes 的宽容取值助手（对齐 Newtonsoft JToken 在 VB 中的隐式转换语义）。
/// 仅供本程序集内部使用。
/// </summary>
internal static class ResourceJson
{
    /// <summary>
    /// 取字符串。数字/布尔按 VB CStr 语义转换；null（缺失键或 JSON null）返回 null。
    /// </summary>
    public static string? AsString(this JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var s)) return s;
            if (value.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<double>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var b)) return b ? "True" : "False";
        }
        return node.ToJsonString();
    }

    /// <summary>
    /// 取整数。字符串可被解析时同样转换（Newtonsoft ToObject(Of Integer) 语义）；null 返回 0。
    /// </summary>
    public static int AsInt(this JsonNode? node)
    {
        if (node is null) return 0;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i)) return i;
            if (value.TryGetValue<long>(out var l)) return (int)l;
            if (value.TryGetValue<double>(out var d)) return (int)d;
            if (value.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        throw new InvalidCastException($"无法将 JSON 节点转换为整数：{node.ToJsonString()}");
    }

    /// <summary>
    /// 取日期。ISO 8601 字符串按 RoundtripKind 解析（Newtonsoft ToObject(Of Date) 的默认语义）；
    /// null（缺失键）返回 <see cref="DateTime.MinValue"/>（VB CDate(Nothing) 语义）。
    /// </summary>
    public static DateTime AsDate(this JsonNode? node)
    {
        if (node is null) return DateTime.MinValue;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<DateTime>(out var dt)) return dt;
            if (value.TryGetValue<string>(out var s))
                return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
        throw new InvalidCastException($"无法将 JSON 节点转换为日期：{node.ToJsonString()}");
    }

    /// <summary>取 JSON 数组；缺失或非数组返回空数组（对应 VB 的 If(x, New JArray) 惯用法）。</summary>
    public static JsonArray AsArrayOrEmpty(this JsonNode? node) => node as JsonArray ?? [];
}
