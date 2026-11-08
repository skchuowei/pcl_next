using System.Globalization;

namespace PCL.Download.Tests;

/// <summary>
/// McVersionUtil（PCLCS McVersion/McUtils 的平移）对拍测试。
/// </summary>
public class McVersionUtilTests
{
    [Theory]
    [InlineData("1.18.2", true)]
    [InlineData("1.20", true)]
    [InlineData("1.7.10", true)]
    [InlineData("1.20-snapshot", true)]
    [InlineData("26.1.5", true)]
    [InlineData("26.1", true)]
    [InlineData("26.1-snapshot", true)]
    [InlineData("26.0", true)]
    [InlineData("26", false)]
    [InlineData("25.2", false)]
    [InlineData("20.1", false)]
    [InlineData("9.9", false)]
    [InlineData("23w13a", false)]
    [InlineData("b1.7.3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsStandardFormat_判定原版版本格式(string? version, bool expected)
        => Assert.Equal(expected, McVersionUtil.IsStandardFormat(version));

    [Theory]
    [InlineData("1.21.5", 210)] //1.21.5 为 210（文档示例）
    [InlineData("26.3.2", 263)] //26.3.2 为 263（文档示例）
    [InlineData("1.20-pre1", 200)]
    [InlineData("1.12.2", 120)]
    [InlineData("23w13a", 190)] //愚人节快照 → 映射到 1.19.4 → 190
    [InlineData("24w14a", 209)] //旧快照无版本号
    [InlineData(null, 209)]
    [InlineData("unknown", 209)]
    public void VersionToDrop_版本名转Drop序数(string? name, int expected)
        => Assert.Equal(expected, McVersionUtil.VersionToDrop(name));

    [Theory]
    [InlineData(263, "26.3")]
    [InlineData(250, "25.0")]
    [InlineData(210, "1.21")]
    [InlineData(249, "1.24")]
    [InlineData(209, "1.20")] //旧快照返回 1.20
    public void DropToVersion_Drop序数转版本名(int drop, string expected)
        => Assert.Equal(expected, McVersionUtil.DropToVersion(drop));

    [Theory]
    [InlineData("1.20.3", 20, 0, 3)] //老格式：次版本移到主版本
    [InlineData("1.12.2", 12, 0, 2)]
    [InlineData("26.1.1", 26, 1, 1)]
    [InlineData("2.0", 5, 0, 1)] //愚人节版本 → 1.5.1
    [InlineData("15w14a", 8, 0, 3)] //愚人节版本 → 1.8.3
    [InlineData("24w14potato", 20, 0, 4)]
    [InlineData("25w14craftmine", 21, 0, 4)]
    [InlineData("26w14a", 26, 1, 1)]
    [InlineData("unknown", 9999, 0, 0)]
    public void NameToVersion_愚人节与格式转换(string name, int major, int minor, int build)
    {
        var version = McVersionUtil.NameToVersion(name);
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("1.20.2", "1.20.1", true)] //左新
    [InlineData("1.20.1", "1.20.2", false)]
    [InlineData("1.20", "1.20", true)] //相同 → GE
    [InlineData("26.1", "1.20.1", true)] //26.x 新于 1.x
    [InlineData("1.20", "1.20-pre1", true)] //正式版新于预览版
    [InlineData("1.20-rc1", "1.20-pre1", true)] //rc 新于 pre
    [InlineData("1.20-snapshot", "1.20-pre1", false)] //snapshot 旧于 pre
    [InlineData("26.1 预览版", "26.1", false)] //预览版旧于正式版
    [InlineData("未知版本", "1.20", true)] //原始语义：未知版本视为最新
    [InlineData("1.20", "未知版本", false)]
    public void CompareVersionGE_版本比较(string left, string right, bool expected)
        => Assert.Equal(expected, McVersionUtil.CompareVersionGE(left, right));

    [Fact]
    public void CompareVersion_两预览版无法比较大小时_按序数比较()
    {
        //两个 token 均无法解析为数字且不属 rc/pre/snapshot/experimental 时按序数比较（对拍 PCLCS 原实现）
        Assert.True(McVersionUtil.CompareVersion("1.20 abc", "1.20 abc") == 0);
        Assert.True(McVersionUtil.CompareVersion("1.20 abc", "1.20 abd") < 0);
    }

    [Fact]
    public void VersionToDrop与DropToVersion_常用区间往返()
    {
        foreach (var name in new[] { "1.16.5", "1.18.2", "1.20.1", "1.21.4", "26.1.5" })
        {
            int drop = McVersionUtil.VersionToDrop(name);
            string roundtrip = McVersionUtil.DropToVersion(drop);
            Assert.Equal(drop, McVersionUtil.VersionToDrop(roundtrip));
        }
    }

    [Fact]
    public void NameToVersion_快照前缀数字解析()
    {
        //段首数字解析：忽略其后的预览版标记
        Assert.Equal(new Version(20, 0, 0), McVersionUtil.NameToVersion("1.20_pre"));
        //23w13a 是愚人节快照，被映射到 1.19.4；其余旧快照无版本号
        Assert.Equal(new Version(19, 0, 4), McVersionUtil.NameToVersion("23w13a"));
        Assert.Equal(new Version(9999, 0, 0), McVersionUtil.NameToVersion("24w14a"));
    }
}
