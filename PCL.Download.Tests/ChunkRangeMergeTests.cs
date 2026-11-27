using System.Security.Cryptography;

namespace PCL.Download.Tests;

/// <summary>
/// 分块「区间账本」与「按偏移合并」的确定性回归。
/// <para/>真机缺陷（Modrinth 整合包 Zombie Invade 100 Days 2.3.mrpack，28.6 MB）：
/// 落地文件 30,423,278 B，期望 29,975,184 B，多出的 448,094 B 恰好等于两个尾段分块之和；
/// 体积校验不过 → 整文件重下 → 慢网下约 5 分钟无进展 → 用户手动取消。
/// <para/>成因：分块区间由 <c>End()</c> 取「下一个更大 Start − 1」在查询时反推，尾段被切给新分块后
/// 旧分块仍持有多写出来的字节，合并又按 Start 顺序把 Done 个字节盲拼 → 同一段写了两遍。
/// 本组用例钉住两条：① 区间裁剪后落地文件必须**恰好**等于 FileSize；② 真区间重叠/缺口必须被挡住。
/// </summary>
public class ChunkRangeMergeTests
{
    private static readonly string TempRoot =
        Path.Combine(Path.GetTempPath(), "pcl-range-merge", Guid.NewGuid().ToString("N"));

    private static string TempPath(string name) => Path.Combine(TempRoot, name);

    /// <summary>内容按「文件内偏移」编码：写错位置、重复写、漏写都能逐字节查出来。</summary>
    private static byte ContentByte(long offset) => (byte)(offset % 251);

    private static FileTask NewTask(string target, long fileSize)
    {
        var task = FileTask.Create(
            new DownloadFileRequest { Sources = ["http://127.0.0.1:1/x"], LocalPath = target }, uuid: 1);
        task.FileSize = fileSize;
        task.IsUnknownSize = false;
        return task;
    }

    /// <summary>
    /// 造一个分块：.tmp 里是从 <paramref name="chunk"/>.Start 开始连续写入的 <paramref name="done"/> 字节。
    /// </summary>
    private static Chunk MakeChunk(FileTask task, long start, long rangeEnd, long done)
    {
        var chunk = new Chunk { Start = start, Source = task.Sources[0], Done = done };
        chunk.SetRangeEnd(rangeEnd);
        Directory.CreateDirectory(TempRoot);
        chunk.TempPath = TempPath($"chunk-{start:x}-{Guid.NewGuid():N}.tmp");
        using (var fs = new FileStream(chunk.TempPath, FileMode.Create, FileAccess.Write))
        {
            for (long i = 0; i < done; i++) fs.WriteByte(ContentByte(start + i));
        }
        return chunk;
    }

    private static byte[] MergeToBytes(FileTask task, params Chunk[] chunks)
    {
        var target = TempPath($"merged-{Guid.NewGuid():N}.bin");
        using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
        {
            output.SetLength(task.FileSize);
            task.MergeByOffset(output, chunks);
        }
        return File.ReadAllBytes(target);
    }

    /// <summary>
    /// 三段严格分区：按偏移写，落地文件必须恰好是 FileSize，且每个字节都落在自己的区间里。
    /// </summary>
    [Fact]
    public void 按偏移合并_三段分区_落地体积与内容严格一致()
    {
        const long size = 300_000;
        var task = NewTask(TempPath("partition.bin"), size);

        var a = MakeChunk(task, 0, 99_999, 100_000);
        var b = MakeChunk(task, 100_000, 199_999, 100_000);
        var c = MakeChunk(task, 200_000, 299_999, 100_000);
        task.Chunks.AddRange([a, b, c]);

        Assert.True(task.IsDataComplete, "三段合计应已覆盖整个文件");

        var merged = MergeToBytes(task, a, b, c);
        Assert.Equal(size, merged.Length);
        for (long i = 0; i < size; i++) Assert.Equal(ContentByte(i), merged[i]);
    }

    /// <summary>
    /// 复现真机分块表：旧分块 Done 已越过自己被缩短后的区间（尾段多写了一遍）。
    /// 正确做法是按区间裁剪后再按偏移写 —— 落地文件必须**恰好** FileSize，而不是把多写的字节也拼进去。
    /// 旧实现这里会得到 130,000 B（= 70,000 + 60,000），正是 30,423,278 vs 29,975,184 的等比缩小版。
    /// </summary>
    [Fact]
    public void 尾段已多写的旧分块_按区间裁剪后不得撑大文件()
    {
        const long size = 100_000;
        var task = NewTask(TempPath("clip.bin"), size);

        // 0~39,999 归它，但它实际写了 70,000 字节（0~69,999）：多出的 40,000~69,999 已切给下一块
        var first = MakeChunk(task, 0, 39_999, 70_000);
        // 40,000~99,999 归第二块，且已写满
        var second = MakeChunk(task, 40_000, 99_999, 60_000);

        Assert.Equal(40_000, first.EffectiveDone(task));

        var merged = MergeToBytes(task, first, second);
        Assert.Equal(size, merged.Length);
        for (long i = 0; i < size; i++) Assert.Equal(ContentByte(i), merged[i]);
    }

    /// <summary>
    /// 真区间重叠（两个分块的账本区间本身交叉）：必须在落地前判失败并给出具体区间，
    /// 绝不能写出一个比 FileSize 长的文件再去撞体积校验。
    /// </summary>
    [Fact]
    public void 区间账本重叠_合并直接判失败并报出区间()
    {
        const long size = 100_000;
        var task = NewTask(TempPath("overlap.bin"), size);

        var a = MakeChunk(task, 0, 59_999, 60_000);
        var b = MakeChunk(task, 40_000, 99_999, 60_000);

        var ex = Assert.ThrowsAny<MergeVerifyException>(() => MergeToBytes(task, a, b));
        Assert.Contains("重复分块", ex.Message);
    }

    /// <summary>
    /// 区间缺口：合并必须报出缺失区间，而不是静默落地一个短文件。
    /// </summary>
    [Fact]
    public void 区间缺口_合并直接判失败并报出缺口()
    {
        const long size = 100_000;
        var task = NewTask(TempPath("gap.bin"), size);

        var a = MakeChunk(task, 0, 39_999, 40_000);
        var b = MakeChunk(task, 70_000, 99_999, 30_000);

        var ex = Assert.ThrowsAny<MergeVerifyException>(() => MergeToBytes(task, a, b));
        Assert.Contains("缺口", ex.Message);
    }

    /// <summary>
    /// 端到端：小 PieceLimit 强制反复切割分块（旧实现在这里会拼出重复字节），
    /// 结果文件必须与源数据逐字节一致、且长度恰好等于 FileSize。
    /// </summary>
    [Fact]
    public async Task 反复切割分块_端到端落地文件不得比源文件长()
    {
        var data = new byte[4 * 1024 * 1024 + 71];
        new Random(20261122).NextBytes(data);
        using var first = new FakeFileServer { Data = data, DelayMsPerChunk = 8 };
        using var second = new FakeFileServer { Data = data, DelayMsPerChunk = 8 };
        var target = TempPath("end-to-end.bin");

        await new DownloadEngine().DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [first.Url, second.Url],
                LocalPath = target,
                Check = new FileCheckRule
                {
                    Hash = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant(),
                    ActualSize = data.Length,
                },
            },
        ], new DownloadOptions
        {
            // 本地回环极快：把分割门槛压到 128KB，逼出「边下边切」的高频切割路径
            SpeedLimitLow = 1L << 30,
            MinSplitSizeBytes = 1024 * 1024,
            PieceLimitBytes = 128 * 1024,
            MaxChunksPerFile = 8,
            MaxChunksPerHost = 4,
            MaxChunksPerHostUnrestricted = 4,
            HedgeDelayMilliseconds = 0,
            TickMilliseconds = 20,
        });

        var merged = await File.ReadAllBytesAsync(target);
        Assert.Equal(data.Length, merged.Length);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(data)), Convert.ToHexString(SHA1.HashData(merged)));
    }

    /// <summary>
    /// 开关打开时，覆盖失败的恢复只能「丢重复块 + 登记缺口」，且进度必须回退到真实覆盖量：
    /// 否则进度虚高会让引擎以为数据已齐、再进一次合并，又撞同一处缺口。
    /// </summary>
    [Fact]
    public void 覆盖失败_开关打开_只丢重复块并登记缺口()
    {
        const long size = 100_000;
        var task = NewTask(TempPath("salvage.bin"), size);

        // 0~59,999 与 40,000~99,999 重叠 20,000 B：有效字节之和 120,000 ≥ FileSize，
        // 于是「字节够了」提前进合并，但 60,000~99,999 这段其实没有独立负责者
        var kept = MakeChunk(task, 0, 59_999, 60_000);
        var duplicate = MakeChunk(task, 40_000, 99_999, 60_000);
        task.Chunks.AddRange([kept, duplicate]);
        task.DownloadDone = 120_000; // 实际投递过的字节（含重复）

        Assert.True(task.IsDataComplete, "重叠造成的虚高应仍能触发合并（这正是要处理的场景）");

        var options = new DownloadOptions { RedownloadGapsOnVerifyFailure = true };
        var ex = new MergeCoverageException("缺口 60000~99999", [(60_000L, 99_999L)]);

        Assert.True(task.TrySalvageCoverage(options, ex, out var retryMerge));
        Assert.False(retryMerge, "还有缺口，不能直接重试合并");

        Assert.False(task.IsDataComplete, "缺口补齐前不得再次进合并");
        Assert.Equal(DownloadFileState.WaitingToDownload, task.State);
        Assert.Equal(60_000, task.DownloadDone); // 回退到真实唯一覆盖量

        Assert.Equal(0, kept.Undone(task));                 // 已下好的分块保留
        Assert.Equal(0, duplicate.EffectiveDone(task));     // 重复块被清空
        var gap = task.Chunks.Single(c => c.Start == 60_000);
        Assert.Equal(99_999, gap.End(task));
        Assert.Equal(40_000, gap.Undone(task));             // 待补 60,000~99,999
        Assert.Equal(ChunkPhase.Canceled, gap.Phase);       // 调度器按「断点续传」认领
    }

    /// <summary>
    /// 开关关闭（线上默认）时不得改动任何状态：仍走整文件重下。
    /// </summary>
    [Fact]
    public void 覆盖失败_开关关闭_不做任何抢救()
    {
        const long size = 100_000;
        var task = NewTask(TempPath("salvage-off.bin"), size);
        var kept = MakeChunk(task, 0, 59_999, 60_000);
        var duplicate = MakeChunk(task, 40_000, 99_999, 60_000);
        task.Chunks.AddRange([kept, duplicate]);
        task.DownloadDone = 120_000;

        var ex = new MergeCoverageException("缺口", [(60_000L, 99_999L)]);
        Assert.False(task.TrySalvageCoverage(new DownloadOptions(), ex, out _));

        Assert.Equal(DownloadFileState.WaitingToCheck, task.State);
        Assert.Equal(120_000, task.DownloadDone);
        Assert.Equal(60_000, duplicate.EffectiveDone(task));
        Assert.DoesNotContain(task.Chunks, c => c.Start == 60_000);
    }

    /// <summary>
    /// 只有重复块、没有缺口：剔除重复块后覆盖已齐，应直接重试合并，不重下任何字节。
    /// </summary>
    [Fact]
    public void 仅重复块无缺口_剔除后直接重试合并()
    {
        const long size = 100_000;
        var task = NewTask(TempPath("salvage-dup.bin"), size);
        var full = MakeChunk(task, 0, 99_999, 100_000);
        var duplicate = MakeChunk(task, 50_000, 99_999, 50_000);
        task.Chunks.AddRange([full, duplicate]);
        task.DownloadDone = 150_000;

        var ex = new MergeCoverageException("重复 50000~99999", []);
        Assert.True(task.TrySalvageCoverage(
            new DownloadOptions { RedownloadGapsOnVerifyFailure = true }, ex, out var retryMerge));

        Assert.True(retryMerge);
        Assert.Equal(100_000, task.DownloadDone);
        Assert.True(task.IsDataComplete, "重复块剔干净后覆盖已齐");
        Assert.Equal(0, duplicate.EffectiveDone(task));
        Assert.Equal(100_000, full.EffectiveDone(task));
    }

    /// <summary>
    /// 缺口反复出现说明账本不可信：超过上限后必须放弃抢救，由调用方退回整文件重下，不能无限打转。
    /// </summary>
    [Fact]
    public void 缺口反复出现_超过上限后不再抢救()
    {
        const long size = 100_000;
        var task = NewTask(TempPath("salvage-cap.bin"), size);
        task.Chunks.AddRange([
            MakeChunk(task, 0, 59_999, 60_000),
            MakeChunk(task, 40_000, 99_999, 60_000),
        ]);
        task.DownloadDone = 120_000;

        var options = new DownloadOptions { RedownloadGapsOnVerifyFailure = true };
        var ex = new MergeCoverageException("缺口", [(60_000L, 99_999L)]);
        for (var i = 0; i < 3; i++) Assert.True(task.TrySalvageCoverage(options, ex, out _));
        Assert.False(task.TrySalvageCoverage(options, ex, out _));
    }
}
