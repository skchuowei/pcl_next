using System;
using PCL.Download;
using Xunit;

namespace PCL.Download.Tests;

public class InstallDownloadPlanTests
{
    [Fact]
    public void FabricMainFile_路径口径对齐VB()
    {
        // 对齐 VB McDownloadFabricLoader：McFolder & "versions\" & Id & "\" & Id & ".json"
        const string mcFolder = @"C:\MC\";
        var desc = InstallDownloads.FabricMainFile("0.15.0", "1.20.1", mcFolder);
        var id = "fabric-loader-0.15.0-1.20.1";

        Assert.StartsWith(mcFolder, desc.LocalPath, StringComparison.Ordinal);
        Assert.Contains(@"versions\" + id + @"\", desc.LocalPath, StringComparison.Ordinal);
        Assert.EndsWith(id + ".json", desc.LocalPath, StringComparison.Ordinal);
        Assert.Equal(mcFolder + @"versions\" + id + @"\" + id + ".json", desc.LocalPath);
    }

    [Fact]
    public void FabricMainFile_镜像地址在前官方在后()
    {
        var desc = InstallDownloads.FabricMainFile("0.15.0", "1.20.1", @"C:\MC\");
        Assert.Equal(2, desc.Urls.Count);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/fabric-meta/", desc.Urls[0], StringComparison.Ordinal);
        Assert.StartsWith("https://meta.fabricmc.net/", desc.Urls[1], StringComparison.Ordinal);
        Assert.All(desc.Urls, u => Assert.Contains("/profile/json", u, StringComparison.Ordinal));
    }

    [Fact]
    public void FabricMainFile_标记Json校验()
    {
        var desc = InstallDownloads.FabricMainFile("0.15.0", "1.20.1", @"C:\MC\");
        Assert.True(desc.IsJson);
    }

    [Fact]
    public void FabricMainFile_版本含无穷符号_地址替换而实例名保留()
    {
        // 实例名保留 ∞（对齐实例列表），请求地址里 ∞ 换成 infinite
        var desc = InstallDownloads.FabricMainFile("0.16.0", "1.20.1∞", @"C:\MC\");
        Assert.Contains("1.20.1∞", desc.LocalPath, StringComparison.Ordinal);
        Assert.DoesNotContain("∞", desc.Urls[0], StringComparison.Ordinal);
        Assert.Contains("infinite", desc.Urls[0], StringComparison.Ordinal);
        Assert.Contains("infinite", desc.Urls[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FabricMainFile_空参数抛异常()
    {
        Assert.Throws<ArgumentException>(() => InstallDownloads.FabricMainFile("", "1.20.1", @"C:\MC\"));
        Assert.Throws<ArgumentException>(() => InstallDownloads.FabricMainFile("0.15.0", "", @"C:\MC\"));
    }
}
