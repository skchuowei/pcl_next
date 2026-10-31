using System.Security.Cryptography;

namespace PCL.Download.Tests;

public class DownloadEngineTests
{
    private static readonly string TempRoot =
        Path.Combine(Path.GetTempPath(), "pcl-download-tests", Guid.NewGuid().ToString("N"));

    private static string TempPath(params string[] relative) => Path.Combine([TempRoot, .. relative]);

    private static byte[] RandomBytes(int size, int seed = 42)
    {
        var data = new byte[size];
        var random = new Random(seed);
        random.NextBytes(data);
        return data;
    }

    private static FileCheckRule Sha1Rule(byte[] data) => new() { Hash = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant() };

    private static DownloadOptions EngineOptions() => new()
    {
        MaxConcurrentChunks = 8,
        // 本地回环速度极快，调高“速度下限”以强制引擎追加分块，覆盖多线程路径
        SpeedLimitLow = 1L << 30,
    };

    #region 基础下载

    [Fact]
    public async Task SmallFile_DownloadsAndPassesHash()
    {
        using var server = new FakeFileServer { Data = RandomBytes(4096) };
        var target = TempPath("small.bin");
        var engine = new DownloadEngine();

        var report = await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(server.Data) },
        ], EngineOptions());

        Assert.True(File.Exists(target));
        Assert.Equal(server.Data, await File.ReadAllBytesAsync(target));
        Assert.Equal(DownloadFileState.Finished, report.Files.Single().State);
        Assert.False(report.Files.Single().IsLocalReuse);
        Assert.Equal(4096, report.TotalBytesDownloaded);
    }

    [Fact]
    public async Task LargeFile_MultiChunk_ReassemblesCorrectly()
    {
        var data = RandomBytes(5 * 1024 * 1024);
        using var server = new FakeFileServer { Data = data, DelayMsPerChunk = 3 }; // 放慢以触发追加分块
        var target = TempPath("large.bin");
        var engine = new DownloadEngine();

        await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], EngineOptions());

        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        // 分片请求应发生过（服务器人为放慢，保证引擎有机会追加分块）
        Assert.Contains(server.Requests, r => r.Contains("Range:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MultipleSources_SecondUsedWhenFirstRefuses()
    {
        var data = RandomBytes(200 * 1024);
        using var deadServer = new FakeFileServer { RefuseConnections = true };
        using var goodServer = new FakeFileServer { Data = data };
        var target = TempPath("failover.bin");
        var engine = new DownloadEngine();

        await engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [deadServer.Url, goodServer.Url],
                LocalPath = target,
                Check = Sha1Rule(data),
            },
        ], EngineOptions());

        Assert.Equal(data, await File.ReadAllBytesAsync(target));
    }

    #endregion

    #region Range 支持与降级

    [Fact]
    public async Task RangeNotSupported_FallsBackToSingleThreadAndSucceeds()
    {
        var data = RandomBytes(3 * 1024 * 1024);
        using var server = new FakeFileServer { Data = data, SupportRange = false };
        var target = TempPath("norange.bin");
        var engine = new DownloadEngine();

        await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], EngineOptions());

        Assert.Equal(data, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task CorruptSingleChunkSource_FailsWithHashMismatch()
    {
        var data = RandomBytes(100 * 1024);
        using var server = new FakeFileServer { Data = data, Mutate = bytes => bytes.Select(b => (byte)(b ^ 0xFF)).ToArray() };
        var target = TempPath("corrupt.bin");
        var engine = new DownloadEngine();

        await Assert.ThrowsAsync<DownloadFailedException>(() => engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], EngineOptions()));
    }

    #endregion

    #region 本地复用

    [Fact]
    public async Task ExistingLocalFile_IsReusedWithoutNetwork()
    {
        var data = RandomBytes(64 * 1024, seed: 7);
        var target = TempPath("libraries", "com", "example", "lib.dat"); // 目标本身不存在，必须从复用目录找
        var server = new FakeFileServer(); // 不填充 Data，也不启动真正下载
        using var _ = server;

        var reuseFolder = TempPath("minecraft");
        var sourceCopy = Path.Combine(reuseFolder, "libraries", "com", "example", "lib.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceCopy)!);
        await File.WriteAllBytesAsync(sourceCopy, data);

        var engine = new DownloadEngine();
        var report = await engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [server.Url],
                LocalPath = target,
                Check = Sha1Rule(data),
            },
        ], new DownloadOptions
        {
            EnableLocalReuse = true,
            LocalReuseFolders = [reuseFolder],
        });

        Assert.True(report.Files.Single().IsLocalReuse);
        Assert.Equal(0, report.TotalBytesDownloaded);
        Assert.Empty(server.Requests);
        Assert.Equal(data, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task CorruptedExistingFile_IsRedownloaded()
    {
        var data = RandomBytes(64 * 1024, seed: 9);
        using var server = new FakeFileServer { Data = data };
        var target = TempPath("libraries", "com", "example", "needs-redownload.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, data.Select(b => (byte)(b ^ 0xFF)).ToArray()); // 已有但损坏

        var engine = new DownloadEngine();
        var report = await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], EngineOptions());

        Assert.False(report.Files.Single().IsLocalReuse);
        Assert.Equal(data, await File.ReadAllBytesAsync(target));
    }

    #endregion

    #region 取消与进度

    [Fact]
    public async Task SlowDownload_CanBeCancelled()
    {
        var data = RandomBytes(2 * 1024 * 1024);
        using var server = new FakeFileServer { Data = data, DelayMsPerChunk = 50 };
        var target = TempPath("slow.bin");
        var engine = new DownloadEngine();
        using var cts = new CancellationTokenSource(400);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], EngineOptions(), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Progress_IsReportedMonotonically()
    {
        var data = RandomBytes(1024 * 1024);
        using var server = new FakeFileServer { Data = data, DelayMsPerChunk = 5 };
        var target = TempPath("progress.bin");
        var engine = new DownloadEngine();
        var progresses = new List<double>();
        var progressLock = new object();

        await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], new DownloadOptions { TickMilliseconds = 30 },
            new Progress<DownloadProgress>(p =>
            {
                lock (progressLock) progresses.Add(p.OverallProgress);
            }));
        await Task.Delay(120); // Progress<T> 异步投递，等待最终进度回调送达

        lock (progressLock)
        {
            Assert.NotEmpty(progresses);
            Assert.True(progresses.Last() >= 0.98, $"最终进度不足：{progresses.Last()}");
            for (var i = 1; i < progresses.Count; i++)
                Assert.True(progresses[i] >= progresses[i - 1] - 1e-9, $"进度出现回退：{progresses[i - 1]} → {progresses[i]}");
        }
    }

    #endregion

    #region 输入校验

    [Fact]
    public async Task InvalidSourceUrl_ThrowsArgumentException()
    {
        var engine = new DownloadEngine();
        await Assert.ThrowsAsync<ArgumentException>(() => engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = ["ftp://not-http/file"], LocalPath = TempPath("bad.bin") },
        ]));
    }

    [Fact]
    public async Task DuplicateLocalPath_IsDownloadedOnce()
    {
        var data = RandomBytes(8 * 1024);
        using var server = new FakeFileServer { Data = data };
        var target = TempPath("dup.bin");
        var engine = new DownloadEngine();

        var report = await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], EngineOptions());

        Assert.Single(report.Files);
        Assert.Equal(data, await File.ReadAllBytesAsync(target));
    }

    #endregion
}
