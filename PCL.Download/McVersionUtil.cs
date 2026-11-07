using System.Globalization;
using System.Text.RegularExpressions;

namespace PCL.Download;

/// <summary>
/// Minecraft 版本号解析与比较工具（平移自 PCLCS 的 McVersion 静态方法与 McUtils.CompareVersion）。
/// PCL.Download 不引用 PCLCS（net10.0 无法引用 net10.0-windows），故在此保留一份语义等价的实现；
/// 两份实现均由同一组对拍测试锁定行为，后续如做统一须同步修改。
/// </summary>
public static class McVersionUtil
{
    /// <summary>
    /// 判断版本字符串是否符合 Minecraft 原版格式，例如 1.x、26.x。
    /// </summary>
    public static bool IsStandardFormat(string? version)
    {
        if (version is null) return false;
        if (Regex.IsMatch(version, @"^1\.\d")) return true;
        string? head = VbCompat.RegexSeek(version, @"^[2-9]\d\.\d+");
        return double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= 26;
    }

    /// <summary>
    /// 将版本名转换为 Drop 序数；无法转换时返回 209。
    /// 例如 26.3.2 为 263，1.21.5 为 210。
    /// </summary>
    public static int VersionToDrop(string? vanillaName)
    {
        if (vanillaName is null) return 209;
        Version version = NameToVersion(vanillaName);
        return version.Major >= 1000 ? 209 : version.Major * 10 + version.Minor;
    }

    /// <summary>
    /// 将 Drop 序数转换为版本字符串。
    /// 旧快照版（209）无对应版本字符串，返回 "1.20"。
    /// </summary>
    public static string DropToVersion(int drop)
    {
        if (drop >= 250) return $"{drop / 10}.{drop % 10}";
        if (drop != 209) return $"1.{drop / 10}";
        return "1.20";
    }

    /// <summary>
    /// 将版本名转换为可比较的三段式原版版本号。
    /// 对老版本格式，例如 1.20.3，会被转换为 20.0.3。
    /// 对愚人节版本，返回其基于的正式版版本号。
    /// 若没有版本号，例如旧快照，则为 9999.0.0。
    /// </summary>
    public static Version NameToVersion(string? name)
    {
        if (name is null) return new Version(9999, 0, 0);
        name = name.ToLowerInvariant().Replace("_unobfuscated", "").Replace(" unobfuscated", "");
        // 愚人节版本
        if (name.StartsWith("2.0", StringComparison.Ordinal)) return NameToVersion("1.5.1");
        if (name == "15w14a") return NameToVersion("1.8.3");
        if (name.Contains(".rv-pre", StringComparison.OrdinalIgnoreCase)) return NameToVersion("1.9.2");
        if (name.Contains("shareware", StringComparison.OrdinalIgnoreCase)) return NameToVersion("1.13.2");
        if (name.StartsWith("20w14", StringComparison.Ordinal) && name != "20w14a") return NameToVersion("1.15.2");
        if (name.Contains("oneblockatatime", StringComparison.OrdinalIgnoreCase)) return NameToVersion("1.18.2");
        // 与 PCLCS 一致保留 name != "20w13a" 的原始条件（该条件恒真，属上游遗留）
        if (name.Contains("23w13a", StringComparison.OrdinalIgnoreCase) && name != "20w13a") return NameToVersion("1.19.4");
        if (name == "24w14potato") return NameToVersion("1.20.4");
        if (name == "25w14craftmine") return NameToVersion("1.21.4");
        if (name == "26w14a") return NameToVersion("26.1.1");
        // 其余版本
        string[] segments = name.Split([' ', '_', '-', '.'], StringSplitOptions.None);
        if (name.StartsWith("1.", StringComparison.Ordinal))
            return new Version(ParseSegment(segments, 1), 0, ParseSegment(segments, 2));
        if (Regex.IsMatch(name, @"^[2-9][0-9]\."))
            return new Version(ParseSegment(segments, 0), ParseSegment(segments, 1), ParseSegment(segments, 2));
        return new Version(9999, 0, 0);

        static int ParseSegment(string[] segments, int index)
        {
            if (index >= segments.Length) return 0;
            // 只读取该段开头连续的数字，忽略其后的预览版标记。
            return int.TryParse(VbCompat.RegexSeek(segments[index], @"^\d+"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) ? value : 0;
        }
    }

    /// <summary>
    /// 比较两个版本名；左侧较新返回正数，相同返回 0，右侧较新返回负数。
    /// 无法比较两个预发布版的大小。
    /// 支持的格式：未知版本, 26.1-snapshot-1，1.13.2, 1.7.10-pre4, 1.8_pre, 1.14 Pre-Release 2, 1.14.4 C6
    /// 与 PCLCS McUtils.CompareVersion 行为一致（含「未知版本视为最新」的原始语义）。
    /// </summary>
    public static int CompareVersion(string left, string right)
    {
        if (left == right) return 0;
        if (left == "未知版本" || right == "未知版本") return left == "未知版本" ? 1 : -1;
        left = left.ToLowerInvariant();
        right = right.ToLowerInvariant();
        var leftParts = left.Replace("快照", "snapshot").Replace("预览版", "pre");
        var rightParts = right.Replace("快照", "snapshot").Replace("预览版", "pre");
        var leftTokens = VbCompat.RegexSearch(leftParts, @"[a-z]+|[0-9]+");
        var rightTokens = VbCompat.RegexSearch(rightParts, @"[a-z]+|[0-9]+");
        for (int i = 0; ; i++)
        {
            if (i >= leftTokens.Count && i >= rightTokens.Count)
                return string.Compare(left, right, StringComparison.Ordinal); // 两边均缺失，感觉是一个东西
            string leftValue = i < leftTokens.Count ? leftTokens[i] : "0";
            string rightValue = i < rightTokens.Count ? rightTokens[i] : "0";
            if (leftValue == rightValue) continue;
            int leftNumber = leftValue switch
            {
                "rc" => -1,
                "pre" => -2,
                "snapshot" => -3,
                "experimental" => -4,
                _ => int.TryParse(leftValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int leftResult) ? leftResult : 0
            };
            int rightNumber = rightValue switch
            {
                "rc" => -1,
                "pre" => -2,
                "snapshot" => -3,
                "experimental" => -4,
                _ => int.TryParse(rightValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rightResult) ? rightResult : 0
            };
            if (leftNumber == 0 && rightNumber == 0) return string.Compare(leftValue, rightValue, StringComparison.Ordinal);
            if (leftNumber != rightNumber) return leftNumber.CompareTo(rightNumber);
        }
    }

    /// <summary>
    /// 比较两个版本名；左侧不旧于右侧返回 true。
    /// </summary>
    public static bool CompareVersionGE(string left, string right) => CompareVersion(left, right) >= 0;
}
