using System.Runtime.InteropServices;
using PCL.Download.Platform;

namespace PCL.Download.Tests;

/// <summary>
/// 平台能力层（<see cref="PlatformInfo"/> / <see cref="CrossPlatformPath"/> / <see cref="PathComparison"/>）测试。
/// <para>
/// 这批断言必须**在 Windows 与 Linux 上同时成立**：期望值一律按当前平台推导，不写死分隔符与大小写语义。
/// 若哪天有人把 Windows 专属写法又带回内核，Linux 侧（build-linux job）会先红。
/// </para>
/// </summary>
public class PlatformCapabilityTests
{
    // ---- 平台探测 ----

    [Fact]
    public void PlatformInfo_与运行时一致()
    {
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), PlatformInfo.IsWindows);
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), PlatformInfo.IsLinux);
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX), PlatformInfo.IsMacOS);
    }

    [Fact]
    public void PlatformInfo_OsName与ArchName沿用Mojang写法()
    {
        Assert.Contains(PlatformInfo.OsName, new[] { "windows", "linux", "osx" });
        Assert.Contains(PlatformInfo.ArchName, new[] { "x86", "x64", "arm64" });

        if (PlatformInfo.IsWindows) Assert.Equal("windows", PlatformInfo.OsName);
        if (PlatformInfo.IsLinux) Assert.Equal("linux", PlatformInfo.OsName);
        if (PlatformInfo.IsMacOS) Assert.Equal("osx", PlatformInfo.OsName);
    }

    // ---- 路径拼接 ----

    [Fact]
    public void Join_用平台分隔符并忽略空段()
    {
        var expected = Path.Combine("a", "b");

        Assert.Equal(expected, CrossPlatformPath.Join("a", "", "b"));
        Assert.Equal(expected, CrossPlatformPath.Join("", "a", "b"));
        Assert.Equal(expected, CrossPlatformPath.Join("a" + Path.DirectorySeparatorChar, "b"));
        Assert.Equal(string.Empty, CrossPlatformPath.Join("", ""));
    }

    [Fact]
    public void Join_多段拼接不重复分隔符()
    {
        var expected = Path.Combine("root", "libraries", "optifine", "OptiFine", "1.12.2");

        Assert.Equal(expected,
            CrossPlatformPath.Join("root" + Path.DirectorySeparatorChar, "libraries", "optifine", "OptiFine",
                "1.12.2"));
    }

    [Fact]
    public void ToPortable_反斜杠归一且幂等()
    {
        Assert.Equal("a/b/c.jar", CrossPlatformPath.ToPortable(@"a\b\c.jar"));
        Assert.Equal("a/b/c.jar", CrossPlatformPath.ToPortable("a/b/c.jar"));

        var once = CrossPlatformPath.ToPortable(@"mods\sub\m.jar");
        Assert.Equal(once, CrossPlatformPath.ToPortable(once));
    }

    [Fact]
    public void ManagedModPath_清单键统一正斜杠小写()
    {
        Assert.Equal("mods/sub/m.jar", InstallManifestRules.ManagedModPath(@"sub\m.jar"));
        Assert.Equal("mods/sub/m.jar", InstallManifestRules.ManagedModPath("sub/m.jar"));
    }

    // ---- 路径比较 ----

    [Fact]
    public void PathComparison_口径与文件系统一致()
    {
        var expected = PlatformInfo.IsWindows || PlatformInfo.IsMacOS
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        Assert.Equal(expected, PathComparison.Comparer);
        Assert.Equal(expected.Equals("A.jar", "a.jar"), PathComparison.PathsEqual("A.jar", "a.jar"));
    }

    [Fact]
    public void PathsEqual_去掉尾部分隔符()
    {
        Assert.True(PathComparison.PathsEqual("mods/a.jar" + Path.DirectorySeparatorChar, "mods/a.jar"));
        Assert.True(PathComparison.PathsEqual(null, null));
        Assert.False(PathComparison.PathsEqual(null, "a"));
        Assert.False(PathComparison.PathsEqual("a", "b"));
    }

    [Fact]
    public void LocalResourceFile_同路径相等且哈希一致()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pcl-platform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "demo.jar");
            File.WriteAllText(path, "x");

            var a = new LocalResourceFile(path);
            var b = new LocalResourceFile(path);
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 清理失败不影响结论 */ }
        }
    }
}
