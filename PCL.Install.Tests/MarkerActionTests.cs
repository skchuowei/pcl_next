namespace PCL.Install.Tests;

using PCL.Download;

/// <summary>
/// 「添加 / 删除忽略标识」两个动作：文件名、内容、缺失资产与类型不符都按 VB 口径锁住。
/// </summary>
public sealed class MarkerActionTests
{
    [Fact]
    public async Task 添加忽略标识写入固定文件名与内容()
    {
        using var workspace = new TempWorkspace();
        var versionFolder = workspace.PathOf("versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);
        var assets = new InstallAssetBag().Set("VersionFolder", versionFolder);
        var context = InstallTestContext.Create(InstallStepKind.IgnoreMarker, assets);

        await new IgnoreMarkerAction().ExecuteAsync(context, CancellationToken.None);

        var markerPath = Path.Combine(versionFolder, ".pclignore");
        Assert.True(File.Exists(markerPath));
        Assert.Equal("用于临时地在 PCL Next 的版本列表中屏蔽此版本。", File.ReadAllText(markerPath));
        // 无 BOM：首字节是「用」的 UTF-8 首字节 0xE7；若带 BOM 会是 0xEF。
        Assert.Equal((byte)0xE7, File.ReadAllBytes(markerPath)[0]);
    }

    [Fact]
    public async Task 添加忽略标识会创建不存在的版本目录()
    {
        using var workspace = new TempWorkspace();
        var versionFolder = workspace.PathOf("versions", "1.20.1");
        var assets = new InstallAssetBag().Set("VersionFolder", versionFolder);
        var context = InstallTestContext.Create(InstallStepKind.IgnoreMarker, assets);

        await new IgnoreMarkerAction().ExecuteAsync(context, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(versionFolder, ".pclignore")));
    }

    [Fact]
    public async Task 删除忽略标识会移除已存在的文件()
    {
        using var workspace = new TempWorkspace();
        var versionFolder = workspace.PathOf("versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);
        var markerPath = Path.Combine(versionFolder, ".pclignore");
        File.WriteAllText(markerPath, IgnoreMarkerAction.Content);
        var assets = new InstallAssetBag().Set("VersionFolder", versionFolder);
        var context = InstallTestContext.Create(InstallStepKind.RemoveIgnoreMarker, assets);

        await new RemoveIgnoreMarkerAction().ExecuteAsync(context, CancellationToken.None);

        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public async Task 删除忽略标识对不存在的文件不报错()
    {
        using var workspace = new TempWorkspace();
        var versionFolder = workspace.PathOf("versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);
        var assets = new InstallAssetBag().Set("VersionFolder", versionFolder);
        var context = InstallTestContext.Create(InstallStepKind.RemoveIgnoreMarker, assets);

        await new RemoveIgnoreMarkerAction().ExecuteAsync(context, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(versionFolder, ".pclignore")));
    }

    [Fact]
    public async Task 缺少版本目录资产时抛出()
    {
        var assets = new InstallAssetBag();
        var context = InstallTestContext.Create(InstallStepKind.IgnoreMarker, assets);

        var ex = await Assert.ThrowsAsync<MissingInstallAssetException>(
            () => new IgnoreMarkerAction().ExecuteAsync(context, CancellationToken.None));

        Assert.Equal("VersionFolder", ex.AssetName);
    }

    [Fact]
    public async Task 资产类型不符时抛出()
    {
        var assets = new InstallAssetBag().Set("VersionFolder", 42);
        var context = InstallTestContext.Create(InstallStepKind.IgnoreMarker, assets);

        var ex = await Assert.ThrowsAsync<InstallAssetTypeException>(
            () => new IgnoreMarkerAction().ExecuteAsync(context, CancellationToken.None));

        Assert.Equal("VersionFolder", ex.AssetName);
        Assert.Equal(typeof(string), ex.ExpectedType);
        Assert.Equal(typeof(int), ex.ActualType);
    }

    [Fact]
    public async Task 已取消时不再写文件()
    {
        using var workspace = new TempWorkspace();
        var versionFolder = workspace.PathOf("versions", "1.20.1");
        var assets = new InstallAssetBag().Set("VersionFolder", versionFolder);
        var context = InstallTestContext.Create(InstallStepKind.IgnoreMarker, assets);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new IgnoreMarkerAction().ExecuteAsync(context, cts.Token));

        Assert.False(Directory.Exists(versionFolder));
    }
}
