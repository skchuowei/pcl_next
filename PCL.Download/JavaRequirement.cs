using System.Text.Json;

namespace PCL.Download;

/// <summary>
/// 从原版版本 json 里读出 Mojang 声明的 Java 主版本要求（<c>javaVersion.majorVersion</c>）。
/// </summary>
/// <remarks>
/// 安装 Forge / NeoForge 时，注入器进程用的是「按 JavaList 顺序取第一个 ≥8 的 Java」，
/// 在 26.3（原版 json 写 majorVersion = 25）的机器上会抓到列表靠前的 Java 21，
/// 目录问题修好后 processor 阶段仍会失败。这里只做纯解析，范围构造仍由宿主负责：
/// <c>ValueRange</c> 属于 MeloongCore，无 GUI 内核不引用它。
/// <para>
/// 同时兼容旧字段 <c>java_version</c>（部分整合包 / 老 json 用这个写法），以及把版本号
/// 写成字符串的情况；任何异常都返回 null，由调用方回退到旧的 <c>≥8</c> 口径。
/// </para>
/// </remarks>
public static class JavaRequirement
{
    /// <summary>解析成功返回主版本号（如 25），字段缺失 / 格式不对 / json 损坏返回 null。</summary>
    public static int? ReadRequiredJavaMajor(string? versionJson)
    {
        if (string.IsNullOrWhiteSpace(versionJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(versionJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("javaVersion", out var javaVersion) &&
                javaVersion.ValueKind == JsonValueKind.Object &&
                javaVersion.TryGetProperty("majorVersion", out var majorVersion) &&
                TryReadPositiveInt(majorVersion, out var major))
                return major;

            if (root.TryGetProperty("java_version", out var legacyVersion) &&
                TryReadPositiveInt(legacyVersion, out var legacyMajor))
                return legacyMajor;

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadPositiveInt(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return value > 0;
        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out value))
            return value > 0;
        return false;
    }
}
