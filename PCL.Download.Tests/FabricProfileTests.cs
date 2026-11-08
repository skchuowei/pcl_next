namespace PCL.Download.Tests;

/// <summary>
/// FabricProfile（实例名与 profile 地址）测试。
/// <para>
/// 这里只有一个真正的坑：<c>∞</c> 只在请求地址里换成 <c>infinite</c>，实例名必须保留原样。
/// 两边写反都不报错，只是实例列表里再也找不到这个版本。
/// </para>
/// </summary>
public class FabricProfileTests
{
    [Fact]
    public void BuildInstanceName_按固定格式拼接()
    {
        Assert.Equal("fabric-loader-0.15.0-1.20.1", FabricProfile.BuildInstanceName("0.15.0", "1.20.1"));
    }

    [Fact]
    public void BuildInstanceName_保留无穷符号()
    {
        // 版本文件夹名要能原样显示「1.20.1∞」
        Assert.Equal("fabric-loader-0.15.0-1.20.1∞", FabricProfile.BuildInstanceName("0.15.0", "1.20.1∞"));
    }

    [Fact]
    public void BuildProfileUrls_镜像在前官方在后()
    {
        var urls = FabricProfile.BuildProfileUrls("0.15.0", "1.20.1");

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.20.1/0.15.0/profile/json", urls[0]);
        Assert.Equal("https://meta.fabricmc.net/v2/versions/loader/1.20.1/0.15.0/profile/json", urls[1]);
    }

    [Fact]
    public void BuildProfileUrls_请求地址里把无穷符号换成infinite()
    {
        var urls = FabricProfile.BuildProfileUrls("0.15.0", "1.20.1∞");

        Assert.All(urls, url => Assert.Contains("/loader/1.20.1infinite/", url));
        Assert.All(urls, url => Assert.DoesNotContain("∞", url));
    }
}
