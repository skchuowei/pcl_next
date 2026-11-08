namespace PCL.Install.Tests;

using PCL.Download;

public sealed class DownloadMappingTests
{
    private static readonly IReadOnlyList<string> Url = ["https://example.test/file"];

    [Fact]
    public void 原版jar缺体积时回退负一并保留校验口径()
    {
        var descriptor = InstallDownloads.VanillaJar(Url, "/tmp/client.jar", "sha1-value", null);
        var request = NetFileRequestFactory.Create(descriptor);

        Assert.Equal("/tmp/client.jar", request.LocalPath);
        Assert.Equal(Url, request.Sources);
        Assert.Equal(1024L, Convert.ToInt64(request.Check!.MinSize));
        Assert.Equal(-1L, Convert.ToInt64(request.Check.ActualSize));
        Assert.Equal("sha1-value", request.Check.Hash);
        Assert.False(request.Check.IsJson);
        Assert.False(request.SimulateBrowserHeaders);
    }

    [Fact]
    public void 原版json保留IsJson标记()
    {
        var descriptor = InstallDownloads.VanillaJson(Url, "/tmp/1.20.1.json");
        var request = NetFileRequestFactory.Create(descriptor);

        Assert.True(request.Check!.IsJson);
        Assert.Equal(-1L, Convert.ToInt64(request.Check.ActualSize));
    }

    [Fact]
    public void 资源对象体积为零时回退负一()
    {
        var descriptor = InstallDownloads.AssetObject(Url, "/tmp/asset", "hash-value", 0);
        var request = NetFileRequestFactory.Create(descriptor);

        Assert.Equal(-1L, Convert.ToInt64(request.Check!.ActualSize));
        Assert.Equal("hash-value", request.Check.Hash);
    }

    [Fact]
    public void 依赖Mod保留浏览器请求头模拟()
    {
        var descriptor = InstallDownloads.ResourceDependency(
            NetFileDescriptorKind.OptiFabric, Url, "/tmp/mod.jar", "hash-value", 8192);
        var request = NetFileRequestFactory.Create(descriptor);

        Assert.True(request.SimulateBrowserHeaders);
        Assert.Equal(8192L, Convert.ToInt64(request.Check!.ActualSize));
        Assert.Equal("hash-value", request.Check.Hash);
    }

    [Fact]
    public void 体积上限无法映射时立即抛出()
    {
        var descriptor = new NetFileDescriptor(Url, "/tmp/file", maxSize: 2048);

        Assert.Throws<InvalidOperationException>(() => NetFileRequestFactory.Create(descriptor));
    }

    [Fact]
    public void 没有候选地址时立即抛出()
    {
        var descriptor = new NetFileDescriptor([], "/tmp/file");

        Assert.Throws<InvalidOperationException>(() => NetFileRequestFactory.Create(descriptor));
    }

    [Fact]
    public void 落盘路径为空时立即抛出()
    {
        var descriptor = new NetFileDescriptor(Url, "   ");

        Assert.Throws<InvalidOperationException>(() => NetFileRequestFactory.Create(descriptor));
    }
}
