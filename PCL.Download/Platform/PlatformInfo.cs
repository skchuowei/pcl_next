using System.Runtime.InteropServices;

namespace PCL.Download.Platform;

/// <summary>当前进程运行的操作系统种类。</summary>
public enum PlatformKind
{
    Windows,
    Linux,
    MacOS,
    Other,
}

/// <summary>
/// 平台探测的唯一入口（内核里不要再各自写 <see cref="RuntimeInformation.IsOSPlatform"/>）。
/// <para/>
/// 口径与原先散落在 <c>PCL.Cli/VersionJson</c> 的实现保持一致：<see cref="OsName"/> 用 Mojang
/// 版本 JSON <c>rules.os.name</c> 的写法（<c>windows</c> / <c>osx</c> / 其余一律 <c>linux</c>），
/// <see cref="ArchName"/> 用其 <c>os.arch</c> 的写法（<c>x86</c> / <c>arm64</c> / 其余 <c>x64</c>）。
/// 只是把实现搬到一处，**不改语义**（改动会让已有版本 JSON 的库筛选结果变化）。
/// </para>
/// <para/>
/// 全部属性在进程内只求值一次：平台与架构不可能在运行期改变。
/// </para>
public static class PlatformInfo
{
    /// <summary>当前平台种类。</summary>
    public static PlatformKind Kind { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PlatformKind.Windows
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PlatformKind.Linux
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? PlatformKind.MacOS
        : PlatformKind.Other;

    public static bool IsWindows => Kind == PlatformKind.Windows;

    public static bool IsLinux => Kind == PlatformKind.Linux;

    public static bool IsMacOS => Kind == PlatformKind.MacOS;

    /// <summary>Mojang 版本 JSON 里的平台名。</summary>
    public static string OsName => Kind switch
    {
        PlatformKind.Windows => "windows",
        PlatformKind.MacOS => "osx",
        // 与既有实现一致：Windows / macOS 之外的平台一律按 linux 归类
        _ => "linux",
    };

    /// <summary>Mojang 版本 JSON 里的架构名。</summary>
    public static string ArchName => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x64",
    };
}
