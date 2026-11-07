using System.Text.RegularExpressions;

namespace PCL.Download;

/// <summary>
/// 社区资源的类型。
/// 数值与 PCLCS.Constants.ResourceTypes 保持一致，桥接层按数值互转（迁移计划 §3.3 DTO 化方案）。
/// </summary>
[Flags]
public enum ResourceTypes
{
    /// <summary>Mod。</summary>
    Mod = 1,
    /// <summary>整合包。</summary>
    ModPack = 2,
    /// <summary>资源包。</summary>
    ResourcePack = 4,
    /// <summary>光影包。</summary>
    Shader = 8,
    /// <summary>数据包。</summary>
    DataPack = 16,
    /// <summary>服务端插件。</summary>
    Plugin = 32,
    /// <summary>地图。</summary>
    Map = 64,
    /// <summary>同时包含数据包以及 Mod。</summary>
    ModOrDataPack = Mod | DataPack,
    /// <summary>允许任意种类，或种类未知。</summary>
    Any = Mod | ModPack | ResourcePack | Shader | DataPack | Plugin | Map,
}

/// <summary>
/// 社区资源的来源平台。数值与 PCLCS.Constants.ResourcePlatforms 保持一致。
/// </summary>
[Flags]
public enum ResourcePlatforms
{
    CurseForge = 1,
    Modrinth = 2,
    Any = CurseForge | Modrinth,
}

/// <summary>
/// Mod 加载器的类型。数值与 PCLCS.Constants.ModLoaders 保持一致。
/// </summary>
[Flags]
public enum ModLoaders
{
    None = 0,
    Forge = 1,
    LiteLoader = 2,
    Fabric = 4,
    NeoForge = 16,
    All = Forge | LiteLoader | Fabric | NeoForge,
}

/// <summary>
/// MC 百科数据库条目的精简信息（供 <see cref="ResourceProject"/> 的翻译名查询使用）。
/// </summary>
public sealed class WikiInfo
{
    /// <summary>MC 百科的页面 ID。若为 0 则没有。</summary>
    public int Id { get; init; }

    /// <summary>翻译后的中文名。可能为 null。</summary>
    public string? ChineseName { get; init; }
}

/// <summary>
/// 资源模型层的宿主注入点（对应 VB 侧的全局状态：ModeDebug、WikiEntry.All）。
/// </summary>
public static class ResourceConfig
{
    /// <summary>
    /// 是否为调试模式。影响 <see cref="ResourceVersion.ReleaseTypeDisplay"/> 的文案（对应 VB 全局 ModeDebug）。
    /// </summary>
    public static bool DebugMode;

    /// <summary>
    /// MC 百科数据库查询钩子：输入 (slug, platform)，返回百科条目，无则返回 null。
    /// 未注入时视为无百科数据（TranslatedName 等同 RawName）。对应 VB 的 WikiEntry.All 按 Slug 检索。
    /// </summary>
    public static Func<string, ResourcePlatforms, WikiInfo?>? WikiLookup;

    /// <summary>
    /// MC 百科全量条目（用于中文搜索反查英文名）。由宿主注入（对应 VB 的 WikiEntry.All.Value）。
    /// 未注入时视为无百科数据，中文搜索分支将退化为直接以原文搜索。
    /// </summary>
    public static IEnumerable<WikiEntry>? WikiEntries;

    /// <summary>
    /// 繁体→简体中文转换钩子（对应 VB 的 Strings.StrConv(s, VbStrConv.SimplifiedChinese)）。
    /// 未注入时退化为恒等（不转换），由宿主在 net10 上经 Microsoft.VisualBasic 提供真实实现。
    /// </summary>
    public static Func<string, string>? ToSimplifiedChinese;
}

/// <summary>
/// VB / MeloongCore 语义的等价小工具（平移用，仅供本程序集内部使用）。
/// </summary>
internal static class VbCompat
{
    /// <summary>VB 的 IsSingle：集合恰有 1 个元素。</summary>
    public static bool IsSingle<T>(this IReadOnlyCollection<T>? source) => source is { Count: 1 };

    /// <summary>
    /// 对于具有 <see cref="FlagsAttribute"/> 的枚举，返回其所有单一 Flag 值（跳过 0、非 2 的幂、组合值、别名）。
    /// 与 MeloongCore EnumUtils.GetAllFlags 语义一致；按枚举数值升序（Enum.GetValues 顺序）。
    /// </summary>
    public static List<TEnum> GetAllFlags<TEnum>() where TEnum : struct, Enum
    {
        var flags = new List<TEnum>();
        foreach (var element in Enum.GetValues<TEnum>())
        {
            long number = Convert.ToInt64(element);
            if (number == 0 || (number & (number - 1)) != 0) continue;
            if (!flags.Contains(element)) flags.Add(element);
        }
        return flags;
    }

    /// <summary>将 Flags 枚举值解包为它包含的单一 Flag 成员集合。与 MeloongCore 的 Flags() 扩展语义一致。</summary>
    public static IEnumerable<TEnum> FlagsOf<TEnum>(this TEnum value) where TEnum : struct, Enum
        => GetAllFlags<TEnum>().Where(element => value.HasFlag(element));

    /// <summary>非零标志位的 <see cref="Enum.HasFlag"/>。与 MeloongCore 的 HasFlagF 语义一致。</summary>
    public static bool HasFlagF<TEnum>(this TEnum value, TEnum flag) where TEnum : struct, Enum
        => value.HasFlag(flag) && !EqualityComparer<TEnum>.Default.Equals(flag, default);

    /// <summary>
    /// 按既定规则进行选择排序（与 ModBase.SortByComparison 相同的算法）。
    /// <paramref name="sortRule"/> 返回 true 表示第一个对象应排在前面（a &gt; b）。
    /// </summary>
    public static List<T> SortByComparison<T>(IEnumerable<T> source, Func<T, T, bool> sortRule)
    {
        var list = source.ToList();
        var result = new List<T>(list.Count);
        while (list.Count > 0)
        {
            var highest = list[0];
            for (int i = 1; i < list.Count; i++)
                if (sortRule(list[i], highest)) highest = list[i];
            list.Remove(highest);
            result.Add(highest);
        }
        return result;
    }

    /// <summary>获取在子字符串第一次出现之前的部分，未找到则返回原串（MeloongCore BeforeFirst）。</summary>
    public static string BeforeFirst(this string str, string text)
    {
        int pos = string.IsNullOrEmpty(text) ? -1 : str.IndexOf(text, StringComparison.Ordinal);
        return pos < 0 ? str : str[..pos];
    }

    /// <summary>获取在子字符串最后一次出现之前的部分，未找到则返回原串（MeloongCore BeforeLast）。</summary>
    public static string BeforeLast(this string str, string text)
    {
        int pos = string.IsNullOrEmpty(text) ? -1 : str.LastIndexOf(text, StringComparison.Ordinal);
        return pos < 0 ? str : str[..pos];
    }

    /// <summary>获取在子字符串最后一次出现之后的部分，未找到则返回原串（MeloongCore AfterLast）。</summary>
    public static string AfterLast(this string str, string text)
    {
        int pos = string.IsNullOrEmpty(text) ? -1 : str.LastIndexOf(text, StringComparison.Ordinal);
        return pos < 0 ? str : str[(pos + text.Length)..];
    }

    /// <summary>获取在子字符串第一次出现之后的部分，未找到则返回原串（MeloongCore AfterFirst）。</summary>
    public static string AfterFirst(this string str, string text)
    {
        int pos = string.IsNullOrEmpty(text) ? -1 : str.IndexOf(text, StringComparison.Ordinal);
        return pos < 0 ? str : str[(pos + text.Length)..];
    }

    /// <summary>忽略大小写的 Contains（MeloongCore ContainsIgnoreCase）。</summary>
    public static bool ContainsIgnoreCase(this string value, string subString)
        => value.IndexOf(subString, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>获取字符串中的第一个正则匹配项，无匹配返回 null（MeloongCore RegexSeek）。</summary>
    public static string? RegexSeek(string input, string pattern)
    {
        var match = Regex.Match(input, pattern);
        return match.Success ? match.Value : null;
    }

    /// <summary>获取字符串中的全部正则匹配项（MeloongCore RegexSearch）。</summary>
    public static List<string> RegexSearch(string input, string pattern)
    {
        var result = new List<string>();
        foreach (Match match in Regex.Matches(input, pattern)) result.Add(match.Value);
        return result;
    }

    /// <summary>是否全部为 ASCII 字符（VB 的 IsAsciiOnly）。</summary>
    public static bool IsAsciiOnly(this string value)
    {
        foreach (var c in value)
            if (c > 127) return false;
        return true;
    }

    /// <summary>升序排序（MeloongCore Ordered）。</summary>
    public static IEnumerable<T> Ordered<T>(this IEnumerable<T> source) where T : IComparable<T>
        => source.OrderBy(x => x);

    /// <summary>
    /// 按自定义两两相等性去重（对应 VB 的 Distinct(comparison)）。保留每个等价类中的首个出现，保持顺序。
    /// <paramref name="equals"/> 返回 true 表示两项视为相同。
    /// </summary>
    public static IEnumerable<T> Distinct<T>(this IEnumerable<T> source, Func<T, T, bool> equals)
    {
        var kept = new List<T>();
        foreach (var item in source)
        {
            if (!kept.Any(k => equals(k, item))) kept.Add(item);
        }
        return kept;
    }

    /// <summary>将第一个字符转换为大写，其余字符转换为小写（MeloongCore Capitalize）。</summary>
    public static string Capitalize(this string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        return $"{char.ToUpperInvariant(word[0])}{word[1..].ToLowerInvariant()}";
    }

    /// <summary>
    /// VB 的 <c>Val</c>：解析字符串前导数字部分（可选符号 + 数字 + 至多一个小数点），
    /// 其余字符（含首个非数字字符起）一律截断。无有效数字则返回 0。
    /// 用于 <see cref="LocalResourceFile"/> 的 Display/Version 判定（纯数字串不当作名称）。
    /// </summary>
    public static double Val(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        int i = 0;
        while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
        if (i >= value.Length) return 0;
        bool neg = false;
        if (value[i] == '-') { neg = true; i++; }
        else if (value[i] == '+') { i++; }
        int j = i;
        bool hasDot = false;
        while (j < value.Length)
        {
            char c = value[j];
            if (c == '.')
            {
                if (hasDot) break;
                hasDot = true;
                j++;
            }
            else if (char.IsDigit(c)) j++;
            else break;
        }
        if (j == i) return 0;
        double.TryParse(value.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result);
        return neg ? -result : result;
    }
}
