namespace PCL.Download.Platform;

/// <summary>
/// 跨平台的路径拼接与写法归一。
/// <para/>
/// 只做两件事：
/// <list type="bullet">
/// <item><see cref="Join"/> 用**平台分隔符**拼接，避免像 <c>mcFolder + "libraries\\optifine\\..."</c>
/// 那样把 Windows 分隔符写死——那种写法在 Linux 上会生成名字里含 <c>\</c> 的单个目录，不报错但落盘位置全错；</item>
/// <item><see cref="ToPortable"/> 把清单 / 归档用的相对路径统一成正斜杠写法。</item>
/// </list>
/// </para>
/// <para/>
/// 约定：<see cref="Join"/> 不转换各段**内部**的分隔符（字符串内容按字面处理），只保证拼接点用平台分隔符。
/// 调用方传进来的路径应当已经是本机写法（来自 <see cref="Path"/> API 或用户输入）。
/// </para>
public static class CrossPlatformPath
{
    /// <summary>按平台分隔符拼接若干路径段；空白段忽略。</summary>
    /// <remarks>
    /// 空段忽略是为了让调用方不必为「可选目录」写分支（例如临时目录为空时直接跳过）。
    /// 末段为空也不会留下尾部悬空分隔符——这与普通字符串拼接的差异正是本方法的用处。
    /// </remarks>
    public static string Join(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var result = string.Empty;
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment)) continue;
            result = result.Length == 0 ? segment : Path.Combine(result, segment);
        }

        return result;
    }

    /// <summary>把路径写法归一成可移植形式：<c>\</c> → <c>/</c>。重复调用结果不变。</summary>
    /// <remarks>
    /// 只用于清单键、归档内相对路径这类**跨机器复用**的字符串；
    /// 本机文件系统操作一律用 <see cref="Path"/>，不要拿归一转义后的字符串去访问文件
    /// （Linux 上正斜杠恰好可用，但把 Windows 路径转成 <c>/</c> 后再传回 Windows API 会重新混用）。
    /// </remarks>
    public static string ToPortable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Replace('\\', '/');
    }
}
