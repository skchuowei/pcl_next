using System.Diagnostics;
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
    public async Task AllSourcesHang_TimesOutRotatesSourcesAndFailsVisibly()
    {
        // 复现真机 NeoForge 卡死：所有源接受连接但永不响应。
        // 引擎应在连接超时后换源重试、留下日志，并在源全部达到失败上限后明确失败——而不是无声卡在 Connecting。
        using var server1 = new FakeFileServer { Data = [1, 2, 3], HangConnections = true };
        using var server2 = new FakeFileServer { Data = [1, 2, 3], HangConnections = true };
        var target = TempPath("hang.bin");
        var engine = new DownloadEngine();
        var warnings = new List<string>();
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            MinConnectTimeoutMilliseconds = 200, // 加速：连接 200ms 即超时
            ConnectTimeoutMilliseconds = 200,
            LogWarn = (_, msg) => lockWarnAdd(warnings, msg),
        };

        await Assert.ThrowsAnyAsync<Exception>(() => engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [server1.Url, server2.Url],
                LocalPath = target,
                Check = new FileCheckRule { Hash = "deadbeef" }, // 给 Check 以免触发本地复用歧义
            },
        ], options));

        // 必须有可见的重试/禁用日志（修复前：全程静默）
        Assert.Contains(warnings, w => w.Contains("自动换源重试") || w.Contains("已被禁用"));
        // 必须确实对两个源都发起过连接（调度器在超时后轮转了源）
        Assert.True(server1.AcceptCount + server2.AcceptCount >= 2,
            $"连接次数不足：{server1.AcceptCount}+{server2.AcceptCount}");
    }

    private static readonly object WarnLock = new();
    private static void lockWarnAdd(List<string> list, string msg)
    {
        lock (WarnLock) list.Add(msg);
    }

    [Fact]
    public async Task BurstThenStall_ResumesFromBreakpointAndCompletes()
    {
        // 复现真机 NeoForge 安装卡 16%、0 B/s：文件先高速下载一段（动态速度下限被抬高），
        // 随后所有连接停滞。修复前调度器用 max(当前速度, 动态下限) 与静态下限比较，
        // 速度掉零后恒判"速度足够"，永远不再补分块，文件冻结在当前进度且不报错。
        // 修复后：当前速度低于动态下限即从断点续传，最终完成下载。
        var data = RandomBytes(2 * 1024 * 1024);
        using var server = new FakeFileServer
        {
            Data = data,
            StallAfterBytes = 1024 * 1024, // 每条连接发 1MB 后停滞，制造"先快后停"
            DelayMsPerChunk = 50,          // 64KB/50ms ≈ 1.28MB/s 的稳定突发，保证动态下限被抬高
        };
        var target = TempPath("burst-then-stall.bin");
        var engine = new DownloadEngine();
        var logs = new List<string>();
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            MinConnectTimeoutMilliseconds = 500,
            ConnectTimeoutMilliseconds = 500,
            TickMilliseconds = 20,
            SpeedLimitLow = 1024,            // 极低静态下限，突出动态下限的判定差异
            NoSplitHostKeywords = ["127.0.0.1"], // 禁止切分，走可预测的断点续传路径
            Log = s => { lock (logs) logs.Add(s); },
        };

        var download = engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], options);

        var winner = await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(ReferenceEquals(winner, download),
            "引擎疑似冻结：突发下载停滞后再未从断点补分块（调度器速度门语义回归）");
        await download;
        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        // 停滞后必须发起过 Range 续传请求
        Assert.Contains(server.Requests, r => r.Contains("Range:", StringComparison.OrdinalIgnoreCase));
        // 分块失败必须留痕（修复前：非禁用路径全静默，卡死现场无从排查）
        Assert.Contains(logs, l => l.Contains("超时") || l.Contains("出错"));
    }

    [Fact]
    public async Task NeverDeliveredDeadSources_AreDisabledQuickly_FailVisibly()
    {
        // 真机 NeoForge 卡 2% 场景：源接受连接但永不响应（maven.neoforged.net 当晚即如此）。
        // 从未送达任何字节的源在连续少数几次失败后即应禁用，
        // 而不是按 MaxConcurrentChunks+2（本例 34）的宽容阈值拖上十余分钟。
        using var server1 = new FakeFileServer { Data = [1, 2, 3], HangConnections = true };
        using var server2 = new FakeFileServer { Data = [1, 2, 3], HangConnections = true };
        var target = TempPath("dead-source-fast-fail.bin");
        var engine = new DownloadEngine();
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 32, // 模拟生产环境（NetTaskThreadLimit）：旧阈值需 34+ 次连续失败才禁用
            MinConnectTimeoutMilliseconds = 150,
            ConnectTimeoutMilliseconds = 150,
        };

        await Assert.ThrowsAnyAsync<Exception>(() => engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [server1.Url, server2.Url],
                LocalPath = target,
                Check = new FileCheckRule { Hash = "deadbeef" },
            },
        ], options));

        // 死源应被快速剔除：两源合计连接次数不应超过「少数几次禁用 + 单线程兜底重试」的量级
        var total = server1.AcceptCount + server2.AcceptCount;
        Assert.True(total <= 24, $"死源未被及时禁用，连接次数过多：{total}");
    }

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

    #region 慢源换源与并发预算

    [Fact]
    public async Task SlowSource_IsRotatedToFastSource()
    {
        // 复现真机“任务管理显示几十 KB/s”场景：首选源连接正常但持续极慢，
        // 引擎应保留断点自动换第二源续传，而不是抱着慢源爬完全程。
        var data = RandomBytes(300 * 1024);
        using var slowServer = new FakeFileServer { Data = data, DelayMsPerChunk = 1500 }; // ≈ 42KB/s
        using var fastServer = new FakeFileServer { Data = data };
        var target = TempPath("slow-rotate.bin");
        var engine = new DownloadEngine();
        var logs = new List<string>();
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            SpeedLimitLow = 1L << 30,
            SlowSourceRotateAfterMilliseconds = 1000,
            SlowSourceRotateSpeedBytesPerSecond = 128 * 1024,
            Log = s => { lock (logs) logs.Add(s); },
        };

        var download = engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [slowServer.Url, fastServer.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], options);

        var winner = await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.True(ReferenceEquals(winner, download), "引擎疑似冻结：慢源未触发换源续传");
        await download;
        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        Assert.Contains(logs, l => l.Contains("换源"));
        Assert.True(fastServer.Requests.Count > 0, "第二下载源未被使用");
    }

    [Fact]
    public async Task SmallFile_StallResume_PreservesMemoryCache()
    {
        // 小文件走内存缓存（IsNoSplit）：连接中途停滞后续传时必须保留已下载数据，
        // 否则断点数据丢失 → 合并后哈希不一致 → 整文件反复重下直至失败。
        var data = RandomBytes(200 * 1024);
        using var server = new FakeFileServer { Data = data, StallAfterBytes = 50 * 1024 };
        var target = TempPath("small-stall-resume.bin");
        var engine = new DownloadEngine();
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            MinConnectTimeoutMilliseconds = 500,
            ConnectTimeoutMilliseconds = 500,
            ReadStallTimeoutMilliseconds = 500,
            RetryBackoffMilliseconds = 200,
            TickMilliseconds = 20,
        };

        var download = engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], options);

        var winner = await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.True(ReferenceEquals(winner, download), "引擎疑似冻结：小文件停滞后续传未完成");
        await download;
        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        // 必须发生过断点续传（多于一次请求）
        Assert.True(server.Requests.Count >= 2, $"未发生断点续传：请求数 {server.Requests.Count}");
    }

    [Fact]
    public async Task SharedBudget_CapsTotalConcurrency()
    {
        // 多文件、多引擎实例共享同一并发预算时，总并发不得超出预算。
        var data = RandomBytes(256 * 1024);
        using var server = new FakeFileServer { Data = data, DelayMsPerChunk = 200 };
        var budget = new ConcurrencyBudget(1);
        var peakActive = 0;
        var peakLock = new object();
        using var stopWatch = new CancellationTokenSource();
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 8,
            SharedBudget = budget,
            TickMilliseconds = 20,
        };

        // 两个文件并行下载，但预算仅允许 1 个分块
        var watch = Task.Run(async () =>
        {
            while (!stopWatch.IsCancellationRequested)
            {
                lock (peakLock) peakActive = Math.Max(peakActive, budget.Active);
                try { await Task.Delay(10, stopWatch.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        });

        var engine = new DownloadEngine();
        await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [server.Url], LocalPath = TempPath("budget-a.bin"), Check = Sha1Rule(data) },
            new DownloadFileRequest { Sources = [server.Url], LocalPath = TempPath("budget-b.bin"), Check = Sha1Rule(data) },
        ], options);

        lock (peakLock) peakActive = Math.Max(peakActive, budget.Active);
        stopWatch.Cancel();
        await watch;
        Assert.True(peakActive <= 1, $"并发预算未生效，峰值并发 {peakActive}");
        Assert.Equal(0, budget.Active);
    }

    #endregion

    #region 重试风暴回归（真机 NeoForge 安装卡 2%、0 B/s 根因）

    [Fact]
    public async Task InstantFailures_AreBackedOff_NoRequestStorm()
    {
        // 复现真机故障链：源持续瞬间失败（429）且不被禁用（bmclapi 豁免禁用）时，
        // 失败分块不得以调度器频率（20ms/tick）反复重拉形成请求风暴。
        using var server = new FakeFileServer
        {
            Data = RandomBytes(4096),
            FailStatus = 429,
            UrlPath = "/bmclapi/file.bin", // 命中 bmclapi 关键字：429 不禁用源，暴露风暴路径
        };
        using var cts = new CancellationTokenSource(3000);
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 8,
            MinConnectTimeoutMilliseconds = 2000,
            ReadStallTimeoutMilliseconds = 2000,
            RetryBackoffMilliseconds = 100,
            RateLimitCooldownMilliseconds = 100,
        };

        var engine = new DownloadEngine();
        try
        {
            await engine.DownloadAsync(
            [
                new DownloadFileRequest { Sources = [server.Url], LocalPath = TempPath("storm.bin") },
            ], options, cancellationToken: cts.Token);
        }
        catch
        {
            // 失败或取消均可接受，关注的是请求节奏
        }

        Assert.True(server.Requests.Count >= 2, $"应发生过重试，实际请求数 {server.Requests.Count}");
        Assert.True(server.Requests.Count <= 20, $"疑似请求风暴：3 秒内 {server.Requests.Count} 次请求");
        for (var i = 1; i < server.RequestTimes.Count; i++)
        {
            var gap = server.RequestTimes[i] - server.RequestTimes[i - 1];
            Assert.True(gap >= 60, $"请求间隔过密（{gap}ms），存在重试风暴");
        }
    }

    [Fact]
    public async Task RateLimit429_TriggersHostCooldown()
    {
        // 收到 429（限速）后，必须冷却 RateLimitCooldownMilliseconds 再重试，不得连续轰炸。
        using var server = new FakeFileServer
        {
            Data = RandomBytes(4096),
            FailStatus = 429,
            UrlPath = "/bmclapi/file.bin",
        };
        using var cts = new CancellationTokenSource(2500);
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 8,
            MinConnectTimeoutMilliseconds = 2000,
            RetryBackoffMilliseconds = 50,
            RateLimitCooldownMilliseconds = 1200,
        };

        var engine = new DownloadEngine();
        try
        {
            await engine.DownloadAsync(
            [
                new DownloadFileRequest { Sources = [server.Url], LocalPath = TempPath("cooldown.bin") },
            ], options, cancellationToken: cts.Token);
        }
        catch
        {
            // 失败或取消均可接受
        }

        Assert.True(server.Requests.Count is >= 2 and <= 4,
            $"429 冷却未生效：2.5 秒内 {server.Requests.Count} 次请求");
        if (server.RequestTimes.Count >= 2)
        {
            var firstGap = server.RequestTimes[1] - server.RequestTimes[0];
            Assert.True(firstGap >= 1000, $"429 后未按冷却等待（首两次请求间隔 {firstGap}ms）");
        }
    }

    #region 首包对冲与死源快判

    [Fact]
    public async Task FirstByteHedge_SlowPrimaryLosesToFastMirror()
    {
        // 主源能建连却迟迟不给首包（真机 maven.fabricmc.net 的典型表现），镜像即时响应。
        // 期望：对冲延迟后并行请求镜像并胜出，总耗时远小于主源的 5 秒首包延迟。
        var data = RandomBytes(512 * 1024);
        using var slow = new FakeFileServer { Data = data, FirstByteDelayMs = 5000 };
        using var fast = new FakeFileServer { Data = data };
        var target = TempPath("hedge.bin");

        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            HedgeDelayMilliseconds = 300,
            MinConnectTimeoutMilliseconds = 8000,
            ConnectTimeoutMilliseconds = 8000,
            TickMilliseconds = 20,
        };

        var watch = Stopwatch.StartNew();
        var engine = new DownloadEngine();
        await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [slow.Url, fast.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], options);
        watch.Stop();

        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        Assert.True(fast.Requests.Count >= 1, "镜像源未被并发请求");
        Assert.True(watch.ElapsedMilliseconds < 4000,
            $"首包对冲未生效，耗时 {watch.ElapsedMilliseconds}ms（慢源首包需 5000ms）");
    }

    [Fact]
    public async Task DeadSource_DisabledAfterTwoZeroProgressFails()
    {
        // 主源连上就挂起（零进展），备源正常；关闭对冲以单独验证死源快判。
        // 原实现要攒满 MaxConcurrentChunks+2 次才禁用，真机上表现为「卡十几分钟忽然好了」。
        var data = RandomBytes(64 * 1024);
        using var dead = new FakeFileServer { Data = data, HangConnections = true };
        using var good = new FakeFileServer { Data = data };
        var target = TempPath("dead-source.bin");

        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            HedgeDelayMilliseconds = 0,
            MinConnectTimeoutMilliseconds = 300,
            ConnectTimeoutMilliseconds = 300,
            ReadStallTimeoutMilliseconds = 300,
            RetryBackoffMilliseconds = 50,
            TickMilliseconds = 20,
        };

        var watch = Stopwatch.StartNew();
        var engine = new DownloadEngine();
        await engine.DownloadAsync(
        [
            new DownloadFileRequest { Sources = [dead.Url, good.Url], LocalPath = target, Check = Sha1Rule(data) },
        ], options);
        watch.Stop();

        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        Assert.True(dead.AcceptCount <= 3, $"零进展死源未被快速禁用，被连接了 {dead.AcceptCount} 次");
        Assert.True(watch.ElapsedMilliseconds < 5000, $"死源判定过慢，耗时 {watch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 限速（429）后恢复的镜像：断点续传必须能接着下完，且进度不得溢出。
    /// <para/>回归两个真机缺陷：
    /// ① 旧分块未「认领」→ 同一断点每个调度周期重开一个分块（请求风暴）；
    /// ② 认领标记提前于限频判定 → 区间变孤儿，文件永久卡住（实测卡 66%）；
    /// ③ 字节重复累加 → 进度 &gt; 100%，界面显示 NaN%。
    /// </summary>
    [Fact]
    public async Task RateLimitedResume_CompletesWithoutProgressOverflow()
    {
        var data = RandomBytes(512 * 1024);
        // 前 3 个请求返回 429，之后恢复正常；路径含 bmclapi 以免被「429 禁用非镜像源」规则直接干掉
        using var server = new FakeFileServer
        {
            Data = data,
            UrlPath = "/bmclapi/file.bin",
            FailStatus = 429,
            FailFirstRequests = 3,
        };
        var target = TempPath("ratelimited.bin");
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            MaxChunksPerHost = 1,           // 逼出「串行续传」路径
            SpeedLimitLow = 1L << 30,       // 强制追加分块
            RetryBackoffMilliseconds = 100,
            RateLimitCooldownMilliseconds = 100,
            ReadStallTimeoutMilliseconds = 2000,
            MinConnectTimeoutMilliseconds = 1000,
            ConnectTimeoutMilliseconds = 1000,
            TickMilliseconds = 20,
        };

        var samples = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(p => { lock (samples) samples.Add(p); });

        var engine = new DownloadEngine();
        var report = await engine.DownloadAsync(
            [new DownloadFileRequest { Sources = [server.Url], LocalPath = target, Check = Sha1Rule(data) }],
            options, progress);

        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        Assert.True(report.Files[0].BytesDownloaded > 0, "引擎未报告实际下载字节");

        lock (samples)
        {
            foreach (var sample in samples)
            {
                Assert.False(double.IsNaN(sample.OverallProgress), "进度出现 NaN（字节重复累加导致超过 100%）");
                Assert.InRange(sample.OverallProgress, 0, 1);
                if (sample.TotalBytesExpected >= 0)
                    Assert.True(sample.TotalBytesDone <= sample.TotalBytesExpected,
                        $"已下载字节超过文件大小：{sample.TotalBytesDone} > {sample.TotalBytesExpected}");
            }
        }
    }

    #endregion

    #endregion
}
