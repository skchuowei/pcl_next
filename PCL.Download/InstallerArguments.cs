namespace PCL.Download;

/// <summary>
/// 安装器命令行里「目录参数」的引号包装。Forge / NeoForge 与 OptiFine 两条路径共用这一份，
/// 避免两处拼接再次漂移。
/// </summary>
/// <remarks>
/// Windows 的 <c>CommandLineToArgvW</c> 把引号内紧邻闭合引号的反斜杠当作转义符：
/// <c>"C:\mc\"</c> 里的 <c>\"</c> 会被解析成<strong>一个字面双引号</strong>（反斜杠被吞掉），
/// 于是 Java 收到的 gameDir 是 <c>C:\mc"</c>，<c>new File(...).exists()</c> 为 false，
/// Forge 安装器直接输出 <c>There is no minecraft installation at: ...</c>。
/// 而 <c>RequestTaskTempFolder</c> 有意返回以 <c>\</c> 结尾的目录，所以这不是边缘情况。
/// <para>
/// 结论：目录参数在包进引号前必须去掉尾部斜杠。盘符根（<c>C:\</c>）不能去尾，
/// 否则会退化成相对路径 <c>C:</c>；这种情形改为在引号内把该反斜杠写成 <c>\\</c>，
/// 由命令行解析器还原成一个字面反斜杠并正常闭合引号。
/// </para>
/// </remarks>
public static class InstallerArguments
{
    /// <summary>
    /// 把目录路径安全地包进一对双引号，保证末尾反斜杠不会把闭合引号转义掉。
    /// </summary>
    /// <param name="path">目录路径；允许为 null / 空（返回空引号串，保持旧行为）。</param>
    public static string QuoteDirectory(string? path)
    {
        var value = path ?? string.Empty;
        var trimmed = value.TrimEnd('\\', '/');

        // 全部分隔符（或空串）：原样保留并把反斜杠加倍，避免退化成不可解析的命令行片段
        if (trimmed.Length == 0)
            return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";

        // 盘符根 C:\ → 去尾会变成 C:（当前盘符上的当前目录），必须保留根分隔符
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return "\"" + trimmed + "\\\\\"";

        return "\"" + trimmed + "\"";
    }
}
