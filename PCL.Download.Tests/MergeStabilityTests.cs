using System.Diagnostics;
using System.Security.Cryptography;

namespace PCL.Download.Tests;

/// <summary>
/// 合并阶段的稳定性回归：分块仍在写自己的 .tmp 时，任何一次提前合并都会撞
/// 「文件被另一个进程占用」，随后被当成下载源故障 → 禁用健康源 → 整个文件失败。
/// 真机表现：Fabric 安装卡在 99.8%、0 B/s 约两分钟后报 download_failed。
/// <para/>这批用例不追求必定命中竞态（时序相关），而是用「慢速源 + 高并发分块 + 多轮」
/// 把窗口拉到足够宽，一旦合并前置条件被改回「字节够了就合并」就会稳定失败。
/// </summary>
public class MergeStabilityTests
{
    private static readonly string TempRoot =
        Path.Combine(Path.GetTempPath(), "pcl-merge-tests", Guid.NewGuid().ToString("N"));

    private static string TempPath(params string[] relative) => Path.Combine([TempRoot, .. relative]);

    private static byte[] RandomBytes(int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static FileCheckRule Sha1Rule(byte[] data) =>
        new() { Hash = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant(), ActualSize = data.Length };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task 慢速源多分块_合并不撞文件占用(int round)
    {
        // 2MB：刚好越过 1MB 的分割阈值，会被切成多个分块
        var data = RandomBytes(2 * 1024 * 1024 + 37, seed: 700 + round);
        using var server = new FakeFileServer { Data = data, DelayMsPerChunk = 1 };
        var target = TempPath($"slow-{round}.bin");
        var engine = new DownloadEngine();
        var options = new DownloadOptions
        {
            MaxConcurrentChunks = 16,
            // 本地回环极快，抬高质量下限以强制追加分块，放大「合并与写入重叠」的窗口
            SpeedLimitLow = 1L << 30,
            TickMilliseconds = 20,
        };

        var watch = Stopwatch.StartNew();
        var report = await engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [server.Url],
                LocalPath = target,
                Check = Sha1Rule(data),
            },
        ], options);
        watch.Stop();

        Assert.Equal(DownloadFileState.Finished, report.Files.Single().State);
        Assert.True(File.Exists(target), "合并后目标文件不存在");

        // 内容必须逐字节一致：合并顺序错、少读一段都会在这里暴露
        var actual = await File.ReadAllBytesAsync(target);
        Assert.Equal(data.Length, actual.Length);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(actual)), Convert.ToHexString(SHA1.HashData(data)));

        // 合并失败 → 整文件重下 → 反复卡在 99% 的旧行为会轻易超过这个上限
        Assert.True(watch.ElapsedMilliseconds < 30000, $"下载耗时 {watch.ElapsedMilliseconds}ms，疑似反复重启");
    }

    /// <summary>
    /// 合并失败不得牵连下载源：本地 I/O 问题被当成网络故障时，引擎会禁用健康源、
    /// 丢弃已下载数据并整文件重下，最终报「已无可用下载源」。
    /// </summary>
    [Fact]
    public async Task 合并阶段不因本地IO失败禁用下载源()
    {
        var data = RandomBytes(1536 * 1024, seed: 99);
        using var server = new FakeFileServer { Data = data };
        var target = TempPath("keep-source.bin");
        var engine = new DownloadEngine();

        var report = await engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [server.Url],
                LocalPath = target,
                Check = Sha1Rule(data),
            },
        ], new DownloadOptions { MaxConcurrentChunks = 8, SpeedLimitLow = 1L << 30 });

        Assert.Equal(DownloadFileState.Finished, report.Files.Single().State);
        // 正常路径下只该有请求，不该出现任何「禁用源」的重试风暴
        Assert.True(server.Requests.Count > 0);
    }

    /// <summary>
    /// Reset（不支持 Range 的源触发整文件单线程重下）时，仍在读的旧分块必须被作废。
    /// <para/>旧行为：Reset 只清空分块列表，旧分块继续往 DownloadDone 累加字节（临时文件已删），
    /// 于是字节数虚高到 FileSize → 触发一次「合并 0 个分块」的空文件落地 → 校验失败 → 整文件重下。
    /// 真机表现：Forge 安装最后 1.1MB 的 mixin jar 空转约 40 秒（进度停在 99.6%）。
    /// </summary>
    [Fact]
    public async Task Reset期间的在读分块_不得触发零分块合并()
    {
        var data = RandomBytes(4 * 1024 * 1024, seed: 4242);
        // 慢源：持续在传（Reset 发生时它一定还活着）
        using var slow = new FakeFileServer { Data = data, DelayMsPerChunk = 200 };
        // 无视 Range 的源：被追加分块选中后立刻触发「不支持分段 → 整文件重下」
        using var noRange = new FakeFileServer { Data = data, SupportRange = false };
        var target = TempPath("reset-live.bin");
        var engine = new DownloadEngine();
        var logs = new List<string>();

        var download = engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [slow.Url, noRange.Url],
                LocalPath = target,
                Check = Sha1Rule(data),
            },
        ], new DownloadOptions
        {
            MaxConcurrentChunks = 4,
            SpeedLimitLow = 1L << 30,
            HedgeDelayMilliseconds = 0,
            TickMilliseconds = 20,
            Log = s => { lock (logs) logs.Add(s); },
        });

        var winner = await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(90)));
        Assert.True(ReferenceEquals(winner, download), "引擎疑似冻结：Reset 后未完成下载");
        var report = await download;

        Assert.Equal(Convert.ToHexString(SHA1.HashData(data)),
            Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(target))));
        // 关键指标：Reset 本身会带来**一次合理的重下**——不支持 Range 的源被选中后，
        // 旧分块作废、整文件单线程重来，此前已传的字节自然要再传一遍，
        // 所以总量在 2 倍以内是正常的（具体倍数取决于 Reset 发生时慢源已传了多少）。
        //
        // 要防的是另一条链路：Reset 期间在读分块没被作废 → DownloadDone 虚高到 FileSize
        // → 触发「合并 0 个分块」的空文件落地 → 校验失败 → **再**整文件重下，
        // 那才是 3 倍以上（真机 Forge 安装：mixin jar 空转 40 秒）。
        // 因此阈值取 3 倍：2 倍 = 一次正常重下，3 倍 = 出现了「空落地 + 校验失败」的额外一轮。
        Assert.True(report.TotalBytesDownloaded <= data.Length * 3,
            $"重复下载了 {report.TotalBytesDownloaded / 1048576.0:0.0} MiB（文件仅 {data.Length / 1048576.0:0.0} MiB），疑似出现空文件落地后的额外重下");

        string[] dump;
        lock (logs) dump = [.. logs];
        Assert.DoesNotContain(dump, l => l.Contains("合并 0 个分块"));
    }
}
