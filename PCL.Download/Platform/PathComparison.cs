namespace PCL.Download.Platform;

/// <summary>
/// 路径比较口径：按**当前文件系统语义**选择大小写敏感性。
/// <para/>
/// Windows（NTFS 默认）与 macOS（APFS 默认）不区分大小写，Linux 区分。
/// 此前内核一律用 <see cref="StringComparison.OrdinalIgnoreCase"/> 比较路径，
/// 在 Linux 上会把 <c>mods/A.jar</c> 与 <c>mods/a.jar</c> 判成同一个文件——
/// 这属于「不报错但结果错」，正是《计划-Linux长期分支》§3 点名的同类问题。
/// </para>
/// <para/>
/// macOS 保持忽略大小写是**有意保留既有行为**（当前不出 macOS 制品，改成区分会让行为无谓变化）。
/// </para>
public static class PathComparison
{
    /// <summary>供 <see cref="string.Equals(string, string, StringComparison)"/> 等重载使用的比较口径。</summary>
    public static StringComparison Comparison { get; } =
        IsCaseInsensitiveFileSystem ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>供字典 / 去重使用的比较器，与 <see cref="Comparison"/> 同源。</summary>
    public static StringComparer Comparer { get; } =
        IsCaseInsensitiveFileSystem ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsCaseInsensitiveFileSystem => PlatformInfo.IsWindows || PlatformInfo.IsMacOS;

    /// <summary>
    /// 判断两个路径是否指向同一位置：去掉尾部分隔符后按 <see cref="Comparison"/> 比较。
    /// </summary>
    /// <remarks>只比较字符串，不展开相对路径、不解析符号链接——调用方应传入绝对路径。</remarks>
    public static bool PathsEqual(string? a, string? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), Comparison);
    }
}
