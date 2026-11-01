using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace PCL.Download;

/// <summary>
/// 单个下载分块（对应 VB 的 NetThread）。
/// </summary>
internal sealed class Chunk
{
    public required long Start;
    public long Done;
    public required DownloadSource Source;
    public string? TempPath;

    public ChunkPhase Phase = ChunkPhase.Waiting;
    public long InitTime = Environment.TickCount64;
    public long LastReceiveTime = -1;

    /// <summary>最近一次失败的时刻（TickCount64）；0 表示从未失败。用于断点续传重试退避。</summary>
    public long FailTime;
    /// <summary>本分块累计失败次数，用于指数退避。</summary>
    public int FailCount;

    /// <summary>
    /// 已被「续传分块」接管：成功续传后置为 true，此后不再作为断点候选。
    /// <para/>修复的关键：原实现只新建续传分块、不认领旧分块，导致调度器每个周期都从同一个
    /// 断点再拉一个新分块（真机日志：起始点 1330552 每 61ms 出现一次），
    /// 同一区间被反复重新下载 —— 请求风暴打出 429，且重复字节让进度超过 100%（显示 NaN）。
    /// </summary>
    public bool Superseded;

    /// <summary>本分块发起连接时所属主机（用于按主机限制并发连接数）。</summary>
    public string? HostKey;

    public bool IsEnded => Phase is ChunkPhase.Finished or ChunkPhase.Canceled;

    public long End(FileTask file)
    {
        lock (file.ChunksLock)
        {
            long nextStart = long.MaxValue;
            foreach (var other in file.Chunks)
            {
                if (other.Start > Start && other.Start < nextStart) nextStart = other.Start;
            }
            if (nextStart != long.MaxValue) return nextStart - 1;
            if (file.IsUnknownSize) return 1_000_000_000_000L;
            return file.FileSize - 1;
        }
    }

    public long Undone(FileTask file) => End(file) - (Start + Done) + 1;
}

internal enum ChunkPhase
{
    Waiting,
    Connecting,
    Reading,
    Downloading,
    Finished,
    Canceled,
}

internal static class ChunkPhaseExtensions
{
    public static DownloadFileState ToFileState(this ChunkPhase phase) => phase switch
    {
        ChunkPhase.Waiting => DownloadFileState.WaitingToDownload,
        ChunkPhase.Connecting => DownloadFileState.Connecting,
        ChunkPhase.Reading => DownloadFileState.Reading,
        ChunkPhase.Downloading => DownloadFileState.Downloading,
        ChunkPhase.Finished => DownloadFileState.Finished,
        ChunkPhase.Canceled => DownloadFileState.Canceled,
        _ => DownloadFileState.WaitingToDownload,
    };
}

/// <summary>
/// 单个文件的下载任务（对应 VB 的 NetFile 状态机）。
/// </summary>
internal sealed class FileTask
{
    public const long PieceLimit = 256 * 1024;

    /// <summary>单次流读取缓冲大小。过小会因系统调用/任务切换开销限制吞吐（旧值 16KB）。</summary>
    private const int ReadBufferSize = 256 * 1024;

    /// <summary>从未送达过数据的源连续失败达到该次数即禁用（死源快速剔除）。</summary>
    private const int NeverDeliveredDisableFailCount = 5;

    public required DownloadFileRequest Request { get; init; }
    public required int Uuid { get; init; }

    public List<DownloadSource> Sources { get; } = [];
    private readonly Queue<DownloadSource> _onceSources = new();
    private bool _retried;
    private int _firstSourceIndex;

    /// <summary>
    /// 状态为单调递进（WaitingToCheck → … → Finished/Canceled），
    /// 并发写入的最坏情况是重复推进到同一状态，因此不做加锁，仅保留 ChunksLock。
    /// </summary>
    public readonly object ChunksLock = new();
    public volatile DownloadFileState State = DownloadFileState.WaitingToCheck;

    public long FileSize = -2;
    public bool IsUnknownSize;
    public long DownloadDone;

    public List<Chunk> Chunks { get; } = [];
    private MemoryStream? _cache;

    public bool IsNoSplit => IsUnknownSize || FileSize < 1024 * 1024;

    public bool IsLocalReuse;
    public bool IsSkippedExisting;

    private int _connectCount;
    private long _connectTimeTotal;
    private int _slowRotateCount;

    public string LocalName => Path.GetFileName(Request.LocalPath);
    public FileCheckRule? Check => Request.Check;

    public static FileTask Create(DownloadFileRequest request, int uuid)
    {
        var task = new FileTask { Request = request, Uuid = uuid };
        var urls = request.Sources.Distinct().ToArray();
        for (int i = 0; i < urls.Length; i++)
            task.Sources.Add(new DownloadSource(i, urls[i].ReplaceLineEndings("").Trim()));
        return task;
    }

    public long BytesDownloaded;

    private readonly List<Exception> _errors = [];

    // ===================== 状态与源管理 =====================

    public void SetState(DownloadFileState state)
    {
        if (state > State || State == DownloadFileState.WaitingToCheck) State = state;
    }

    public bool HasAvailableSource(bool allowOnce = true)
    {
        lock (ChunksLock)
        {
            if (Sources.Any(s => !s.IsFailed)) return true;
            if (allowOnce && _onceSources.Count > 0) return true;
        }
        return false;
    }

    private DownloadSource? GetSource(int id = 0)
    {
        lock (ChunksLock)
        {
            if (Sources.Any(s => !s.IsFailed))
            {
                if (id >= Sources.Count || id < 0) id = 0;
                var current = Sources[id];
                while (current.IsFailed)
                {
                    id = (id + 1) % Sources.Count;
                    current = Sources[id];
                }
                return current;
            }
            return _onceSources.Count > 0 ? _onceSources.Peek() : null;
        }
    }

    /// <summary>
    /// 为「追加分块」挑选下载源：在未禁用且允许分段的源中选活动分块数最少者（平局取 Id 小者）。
    /// 全部候选源都不允许分段（如仅有 BMCLAPI）时返回 null，维持不分割行为。
    /// </summary>
    private DownloadSource? GetSplitCapableSource(DownloadOptions options)
    {
        lock (ChunksLock)
        {
            var candidates = Sources
                .Where(s => !s.IsFailed &&
                            !options.NoSplitHostKeywords.Any(k => s.Url.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count == 0) return null;
            // 优先历史上赢过首包竞速、且少落败的源：对冲判定出谁快之后，后续分块不再回头用慢源
            return candidates
                .OrderByDescending(s => s.HedgeWins)
                .ThenBy(s => s.HedgeLosses)
                .ThenBy(s => Chunks.Count(c => ReferenceEquals(c.Source, s) && !c.IsEnded))
                .ThenBy(s => s.Id)
                .First();
        }
    }

    /// <summary>
    /// 慢速下载源判定：分块持续传输超过阈值时间但平均速度仍低于阈值，且存在其他可用源时，
    /// 建议保留断点换源续传（不记失败、不禁用源）。避免“还活着但极慢”的源把文件拖成龟速。
    /// </summary>
    private bool ShouldRotateSlowSource(Chunk chunk, DownloadOptions options, long firstDataTime)
    {
        if (options.SlowSourceRotateSpeedBytesPerSecond <= 0) return false;
        if (IsUnknownSize || FileSize <= 0) return false; // 仅支持已知大小的文件（Range 续传依赖确定的区间）
        if (chunk.Source.SingleChunk is not null) return false;
        if (Volatile.Read(ref _slowRotateCount) >= Math.Max(2, Sources.Count * 2)) return false; // 防止各源都慢时反复重连
        var elapsed = Environment.TickCount64 - firstDataTime;
        if (elapsed < options.SlowSourceRotateAfterMilliseconds) return false;
        var average = chunk.Done * 1000.0 / elapsed;
        if (average >= options.SlowSourceRotateSpeedBytesPerSecond) return false;
        lock (ChunksLock)
        {
            return Sources.Any(s => !s.IsFailed && !ReferenceEquals(s, chunk.Source));
        }
    }

    private int ConnectAverage => _connectCount == 0 ? -1 : (int)(_connectTimeTotal / _connectCount);

    public double Progress
    {
        get
        {
            switch (State)
            {
                case DownloadFileState.WaitingToCheck:
                    return 0;
                case DownloadFileState.WaitingToDownload:
                    return 0.01;
                case DownloadFileState.Connecting:
                    return 0.02;
                case DownloadFileState.Reading:
                    return 0.04;
                case DownloadFileState.Downloading:
                {
                    // 未知大小 / 大小尚未知时用 0.5 占位；并夹紧到 [0,1]，
                    // 否则 1 - original 为负时 Math.Pow 会算出 NaN（进度条显示 NaN%）
                    var original = IsUnknownSize || FileSize <= 0 ? 0.5 : DownloadDone / (double)FileSize;
                    if (double.IsNaN(original) || double.IsInfinity(original)) original = 0.5;
                    original = Math.Clamp(original, 0, 1);
                    original = 1 - Math.Pow(1 - original, 0.9);
                    return original * 0.93 + 0.05;
                }
                case DownloadFileState.Merging:
                    return 0.99;
                default:
                    return 1;
            }
        }
    }

    // ===================== 分块调度 =====================

    /// <summary>
    /// 是否存在失败中断、仍留有未下载区间需要断点续传的分块。
    /// 断点续传属恢复而非追加并发，调度时不受速度门限制。
    /// 处于失败退避冷却中的分块不计入，避免「失败即重拉」形成请求风暴（429 限速后全线 0 B/s）。
    /// </summary>
    public bool HasBrokenChunk(DownloadOptions options)
    {
        lock (ChunksLock)
        {
            foreach (var c in Chunks)
                if (IsBrokenReady(c, options)) return true;
        }
        return false;
    }

    /// <summary>
    /// 分块是否处于「失败中断、留有未下载区间、且已过重试冷却」的可续传状态。
    /// </summary>
    private bool IsBrokenReady(Chunk c, DownloadOptions options)
    {
        // 已被续传分块接管的旧分块不再重复认领（否则同一断点每个调度周期都会被重新拉一次）
        if (c.Superseded) return false;
        if (c.Phase != ChunkPhase.Canceled || c.Undone(this) <= 0) return false;
        if (c.FailTime > 0 && options.RetryBackoffMilliseconds > 0)
        {
            // 指数退避：×1 / ×2 / ×4 / ×8 封顶
            var backoff = Math.Min(8, 1 << Math.Min(Math.Max(c.FailCount - 1, 0), 3));
            if (Environment.TickCount64 - c.FailTime < (long)options.RetryBackoffMilliseconds * backoff) return false;
        }
        return true;
    }

    /// <summary>
    /// 尝试开启一个新的下载分块。返回 null 表示当前不应开启。
    /// 状态推进不加锁（见 State 字段说明），分块操作统一由 ChunksLock 保护。
    /// </summary>
    public Chunk? TryBeginChunk(DownloadOptions options, int runningChunks)
    {
        try
        {
            if (State >= DownloadFileState.Merging || State == DownloadFileState.WaitingToCheck) return null;
            if (State < DownloadFileState.Connecting) State = DownloadFileState.Connecting;

            long start;
            DownloadSource? source;
            var isSingleThreadOnce = false;
            var inheritedFailCount = 0;
            Chunk? claimedBroken = null;

            lock (ChunksLock)
            {
                if (runningChunks >= options.MaxConcurrentChunks || !HasAvailableSource()) return null;

                if (IsNoSplit && Chunks.Any(c => !c.IsEnded))
                {
                    return null;
                }
                if (!HasAvailableSource(allowOnce: false))
                {
                    // 只剩单线程源（或文件无需分割）
                    var onceSource = _onceSources.Count > 0 ? _onceSources.Peek() : null;
                    if (onceSource is not null)
                    {
                        if (onceSource.SingleChunk is Chunk alive && !alive.IsEnded) return null;
                        Reset(options);
                    }
                    isSingleThreadOnce = true;
                    start = 0;
                    source = onceSource ?? GetSource(0);
                    if (source is null) return null;
                }
                else
                {
                    // 首个分块
                    if (Chunks.Count == 0)
                    {
                        start = 0;
                        source = GetSource(_firstSourceIndex);
                        if (source is null) return null;
                        _firstSourceIndex = source.Id + 1;
                    }
                    else
                    {
                        // 接续失败点（仅认领已过退避冷却的分块，避免失败即重拉的请求风暴）
                        Chunk? broken = null;
                        foreach (var c in Chunks.OrderBy(c => c.Start))
                        {
                            if (IsBrokenReady(c, options))
                            {
                                broken = c;
                                break;
                            }
                        }
                        if (broken is not null)
                        {
                            start = broken.Start + broken.Done;
                            source = GetSource(broken.Source.Id + 1);
                            if (source is null) return null;
                            // 认领动作必须等分块真正创建后才生效：中间还有限频/主机并发等可能提前返回 null，
                            // 若先标记「已被认领」再返回，这一区间就永远没有分块负责（实测卡在 66% 不动）
                            claimedBroken = broken;
                            // 继承失败次数：同一点反复失败时指数退避要跨分块生效，
                            // 否则每次重拉都是新分块（FailCount 归零），退避永远停在 ×1。
                            // 上次尝试有实际进展时退一档，避免偶发抖动过度惩罚
                            inheritedFailCount = broken.Done > 0 ? Math.Max(0, broken.FailCount - 1) : broken.FailCount;
                        }
                        else
                        {
                            // 追加线程：切割最大碎片
                            // 选源改为「负载均衡 + 允许分段」：多源时把新分块摊到活动分块更少的源上，
                            // 并跳过禁止分段的主机（如 BMCLAPI），让其余源也可参与并行下载
                            source = GetSplitCapableSource(options);
                            if (source is null) return null;
                            Chunk? largest = null;
                            foreach (var c in Chunks)
                            {
                                if (largest is null || c.Undone(this) > largest.Undone(this)) largest = c;
                            }
                            if (largest is null || largest.Undone(this) < options.PieceLimitBytes) return null;
                            start = largest.End(this) - (long)(largest.Undone(this) * 0.4);
                        }
                    }
                }

                if (start < 0 || (start > FileSize && FileSize >= 0 && !IsUnknownSize)) return null;

                // 连接发起限频（非阻塞）：未到间隔则本轮放弃，下个调度周期再试
                if (!HostStartThrottle.TryReserve(source.Url, options)) return null;

                // 按主机限制并发分块数：单主机开太多连接会被镜像限速（429）
                var hostKey = HostConcurrency.KeyOf(source.Url);
                if (!HostConcurrency.TryEnter(hostKey, options.MaxChunksPerHost)) return null;

                var chunk = new Chunk
                {
                    Start = start,
                    Source = source,
                    HostKey = hostKey,
                    FailCount = inheritedFailCount,
                };
                if (isSingleThreadOnce)
                {
                    source.SingleChunk = chunk;
                }
                // 分块真正落地后才认领旧断点：中间任何提前返回都不会把区间变成无人负责的孤儿
                if (claimedBroken is not null)
                {
                    claimedBroken.Superseded = true;
                    chunk.FailCount = inheritedFailCount;
                }
                Chunks.Add(chunk);
                // 与 VB 对齐：首分块记 Info，续传/追加分块记日志留痕（此前完全静默，UI 卡死时无从排查）
                options.Log?.Invoke(start == 0
                    ? $"{LocalName}：开始下载，起始点 0，{source.Url}"
                    : $"{LocalName}：开始分块，起始点 {start}，{source.Url}");
                return chunk;
            }
        }
        catch (Exception ex)
        {
            Fail(new Exception($"尝试开始下载分块失败（{LocalName}）", ex));
            return null;
        }
    }

    // ===================== 首包对冲 =====================

    /// <summary>
    /// 一次「建立连接 → 校验响应 → 读到首包」的尝试。
    /// <para/>首包对冲时会有两个 attempt 并行，先完成者胜出并接管其连接，另一个立即释放。
    /// </summary>
    private sealed class ChunkAttempt
    {
        public required DownloadSource Source;
        public HttpRequestMessage? Request;
        public HttpResponseMessage? Response;
        public Stream? Stream;
        public CancellationTokenSource Cts = null!;
        public byte[] Buffer = new byte[ReadBufferSize];
        public int FirstCount;
        public long ContentLength;
        public long ElapsedMilliseconds;
        private int _disposed;
        private bool _detached;

        /// <summary>把连接所有权交给调用方（由 RunChunkAsync 的 finally 释放），之后 Dispose 不再动连接。</summary>
        public void Detach()
        {
            _detached = true;
            Request = null;
            Response = null;
            Stream = null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            // 胜出的连接已移交，不能取消（取消会中断主流程正在读的流）
            if (!_detached)
            {
                try { Cts.Cancel(); } catch { /* 已释放 */ }
            }
            try { Stream?.Dispose(); } catch { /* 忽略 */ }
            try { Response?.Dispose(); } catch { /* 忽略 */ }
            try { Request?.Dispose(); } catch { /* 忽略 */ }
            try { Cts.Dispose(); } catch { /* 忽略 */ }
        }
    }

    /// <summary>
    /// 建立连接、校验响应、读取首包。整个过程可被对冲：两个源同时跑，先拿到首包者胜。
    /// </summary>
    private async Task<ChunkAttempt> OpenAttemptAsync(
        Chunk chunk, DownloadSource source, DownloadOptions options, EngineContext ctx,
        int timeout, bool firstThread, CancellationToken cancellationToken)
    {
        var attempt = new ChunkAttempt { Source = source };
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.Cts = cts;
        var startedAt = Environment.TickCount64;
        try
        {
            cts.CancelAfter(timeout);

            attempt.Request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            ApplyBrowserHeaders(attempt.Request, Request, source.Url);
            if (!firstThread) attempt.Request.Headers.Range = new RangeHeaderValue(chunk.Start, null);

            attempt.Response = await ctx.Client.SendAsync(
                    attempt.Request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                // 与读超时同理：建连挂死时代理/网络层不会及时响应令牌取消，
                // 用 WaitAsync 保证到点即返回，随后 attempt.Dispose() 取消并关闭连接
                .WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, timeout)), cancellationToken)
                .ConfigureAwait(false);
            if (!attempt.Response.IsSuccessStatusCode)
                throw new HttpStatusCodeException(
                    $"错误码 {(int)attempt.Response.StatusCode}，{source.Url}", (int)attempt.Response.StatusCode);
            if (State == DownloadFileState.Canceled) throw new OperationCanceledException(cancellationToken);

            attempt.ContentLength = attempt.Response.Content.Headers.ContentLength ?? -1;
            ValidateAttemptResponse(attempt, chunk, source, firstThread, options);

            attempt.Stream = await attempt.Response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            // 首包读取挂上 attempt 自己的令牌：竞速落败时 Dispose 里的 Cancel 能立刻断开连接，
            // 否则落败的源要挂满一个停滞超时周期（10s）才释放，白白占着连接与并发名额
            attempt.FirstCount = await ctx.ReadWithTimeout(
                attempt.Stream, attempt.Buffer, options.ReadStallTimeoutMilliseconds, cts.Token).ConfigureAwait(false);
            attempt.ElapsedMilliseconds = Environment.TickCount64 - startedAt;
            return attempt;
        }
        catch
        {
            attempt.Dispose();
            throw;
        }
    }

    private int _sizeLogged;

    /// <summary>响应校验（文件大小、Range 支持、大小一致性）。与顺序下载路径的语义完全一致。</summary>
    private void ValidateAttemptResponse(
        ChunkAttempt attempt, Chunk chunk, DownloadSource source, bool firstThread, DownloadOptions options)
    {
        var contentLength = attempt.ContentLength;
        bool rangeNotSupported = false;
        if (contentLength < 0)
        {
            if (FileSize > 1)
            {
                if (chunk.Start == 0)
                    options.Log?.Invoke($"{LocalName}：文件大小未知，但已从其他下载源获取，不作处理");
                else
                    rangeNotSupported = true;
            }
            else
            {
                FileSize = -1;
                IsUnknownSize = true;
                if (Interlocked.Exchange(ref _sizeLogged, 1) == 0)
                    options.Log?.Invoke($"{LocalName}：文件大小未知");
            }
        }
        else if (firstThread)
        {
            var check = Check;
            if (check is not null)
            {
                if (check.MinSize > 0 && contentLength < check.MinSize)
                    throw new Exception($"文件大小不足，获取结果为 {contentLength}，要求至少为 {check.MinSize}。");
                if (check.ActualSize > 0 && contentLength != check.ActualSize)
                    throw new Exception($"文件大小不一致，获取结果为 {contentLength}，要求必须为 {check.ActualSize}。");
            }
            FileSize = contentLength;
            IsUnknownSize = false;
            // 对冲时两个 attempt 会同时走到这里，日志只打一次
            if (Interlocked.Exchange(ref _sizeLogged, 1) == 0)
                options.Log?.Invoke($"{LocalName}：文件大小 {contentLength}");
            if (contentLength > 50L * 1024 * 1024) CheckDiskSpace(contentLength, options);
        }
        else if (FileSize < 0)
        {
            throw new Exception("非首分块运行时，尚未获取文件大小");
        }
        else if (chunk.Start > 0 && contentLength >= FileSize)
        {
            rangeNotSupported = true;
        }
        else if (contentLength != FileSize - chunk.Start)
        {
            throw new RangeNotSupportedException(
                $"获取到的分段大小不一致：Range 起始于 {chunk.Start}，预期 {FileSize - chunk.Start}，返回 {contentLength}，总大小 {FileSize}");
        }
        if (rangeNotSupported)
        {
            lock (ChunksLock)
            {
                if (source.SingleChunk == chunk) rangeNotSupported = false; // 单线程源不因 Range 被禁用
            }
            if (rangeNotSupported)
                throw new RangeNotSupportedException(
                    $"该下载源不支持分段下载：Range 起始于 {chunk.Start}，预期 ContentLength 为 {FileSize - chunk.Start}，返回 ContentLength 为 {contentLength}，总文件大小 {FileSize}");
        }
    }

    /// <summary>是否对本次分块启用首包对冲：仅文件首个分块、至少两个可用源、且未关闭对冲。</summary>
    private bool ShouldHedge(DownloadOptions options, bool firstThread)
    {
        if (options.HedgeDelayMilliseconds <= 0) return false;
        if (!firstThread) return false;
        lock (ChunksLock) return Sources.Count(s => !s.IsFailed) >= 2;
    }

    /// <summary>挑选对冲的第二个源：优先历史上赢过竞速、少输的源。</summary>
    private DownloadSource? PickHedgeCandidate(DownloadSource primary)
    {
        lock (ChunksLock)
        {
            return Sources
                .Where(s => !s.IsFailed && !ReferenceEquals(s, primary))
                .OrderByDescending(s => s.HedgeWins)
                .ThenBy(s => s.HedgeLosses)
                .ThenBy(s => s.Id)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// 首包对冲：主源先跑，超过 <see cref="DownloadOptions.HedgeDelayMilliseconds"/> 仍无数据时，
    /// 立即向另一个可用源发起同一区间请求，谁先吐出首包谁继续下载，另一个连接立刻断开。
    /// <para/>典型收益：官方源能建连却不发数据时，从「干等 30 秒超时再换源」缩短到约 1 秒。
    /// </summary>
    private async Task<ChunkAttempt> OpenWithHedgeAsync(
        Chunk chunk, DownloadSource primary, DownloadOptions options, EngineContext ctx,
        int timeout, bool firstThread, CancellationToken cancellationToken)
    {
        var primaryTask = OpenAttemptAsync(chunk, primary, options, ctx, timeout, firstThread, cancellationToken);
        var hedgeDelay = options.HedgeDelayMilliseconds;

        var finished = await Task.WhenAny(primaryTask, Task.Delay(hedgeDelay, cancellationToken)).ConfigureAwait(false);
        if (finished == primaryTask)
        {
            var attempt = await primaryTask.ConfigureAwait(false); // 失败则交给上层既有失败流程处理
            Interlocked.Increment(ref attempt.Source.HedgeWins);
            return attempt;
        }

        var secondary = PickHedgeCandidate(primary);
        if (secondary is null) return await primaryTask.ConfigureAwait(false);

        options.Log?.Invoke(
            $"{LocalName}：下载源 {primary.Id} 在 {hedgeDelay}ms 内无数据，同时向下载源 {secondary.Id} 发起请求竞速");
        var secondaryTask = OpenAttemptAsync(chunk, secondary, options, ctx, timeout, firstThread, cancellationToken);

        var pending = new List<Task<ChunkAttempt>> { primaryTask, secondaryTask };
        Exception? lastError = null;
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(done);
            try
            {
                var winner = await done.ConfigureAwait(false);
                Interlocked.Increment(ref winner.Source.HedgeWins);
                foreach (var loser in pending)
                {
                    var loserSource = ReferenceEquals(loser, primaryTask) ? primary : secondary;
                    Interlocked.Increment(ref loserSource.HedgeLosses);
                    _ = DiscardAttemptAsync(loser);
                }
                options.Log?.Invoke(
                    $"{LocalName}：竞速胜出下载源 {winner.Source.Id}（首包 {winner.ElapsedMilliseconds}ms），已断开较慢的下载源");
                return winner;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex; // 先完成的是失败：继续等另一个源
            }
        }
        throw lastError ?? new TimeoutException("所有候选下载源均未能返回数据。");
    }

    /// <summary>落败尝试的后台清理：等它自己结束（多半是取消）再释放，异常一律吞掉。</summary>
    private static async Task DiscardAttemptAsync(Task<ChunkAttempt> task)
    {
        try
        {
            var attempt = await task.ConfigureAwait(false);
            attempt.Dispose();
        }
        catch
        {
            // 落败尝试自身的失败无需上报
        }
    }

    // ===================== 下载主体 =====================

    public async Task RunChunkAsync(
        Chunk chunk, DownloadOptions options, EngineContext ctx, CancellationToken cancellationToken)
    {
        var source = chunk.Source;
        var firstThread = chunk.Start == 0 && FileSize == -2;

        var connectAverage = ConnectAverage;
        var timeout = Math.Min(Math.Max(connectAverage < 0 ? options.MinConnectTimeoutMilliseconds : connectAverage, options.MinConnectTimeoutMilliseconds) * (1 + source.FailCount),
            options.ConnectTimeoutMilliseconds);
        chunk.Phase = ChunkPhase.Connecting;

        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        Stream? responseStream = null;
        FileStream? fileStream = null;
        long contentLength = 0;
        int readCount;

        try
        {
            if (source.IsFailed && source.SingleChunk != chunk) return;

            // ---------- 建连 + 响应校验 + 首包 ----------
            // 首块可对冲：主源迟迟不给数据时立即并行请求另一个源，谁先吐首包谁继续，
            // 另一个连接当场断开（官方源能建连却不发数据时，从 30 秒超时缩短到约 1 秒换源）
            var attempt = ShouldHedge(options, firstThread)
                ? await OpenWithHedgeAsync(chunk, source, options, ctx, timeout, firstThread, cancellationToken).ConfigureAwait(false)
                : await OpenAttemptAsync(chunk, source, options, ctx, timeout, firstThread, cancellationToken).ConfigureAwait(false);

            source = attempt.Source;
            chunk.Source = source;
            contentLength = attempt.ContentLength;
            request = attempt.Request;
            response = attempt.Response;
            responseStream = attempt.Stream!;
            var buffer = attempt.Buffer;
            readCount = attempt.FirstCount;
            attempt.Detach();   // 连接所有权移交本方法的 finally
            attempt.Dispose();  // 此时仅释放 CTS

            if (State == DownloadFileState.Canceled) return;

            chunk.Phase = ChunkPhase.Reading;
            SetState(DownloadFileState.Reading);

            // ---------- 创建缓存 ----------
            var useMemory = IsNoSplit;
            if (useMemory)
            {
                chunk.TempPath = null;
                // 续传/换源时已有缓存必须保留（含已下载数据），否则断点数据会丢失导致合并后哈希不一致
                if (_cache is null) _cache = new MemoryStream();
            }
            else
            {
                chunk.TempPath = Path.Combine(ctx.TempFolder, $"{Uuid}_{chunk.Start:x16}_{Random.Shared.Next(0, 999999)}.tmp");
                fileStream = new FileStream(chunk.TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64);
            }

            // ---------- 读取循环 ----------
            // 首包已由 attempt 读好（buffer / readCount），此处直接进入写入循环
            var firstDataTime = -1L;
            var stallTimeout = options.ReadStallTimeoutMilliseconds;
            while ((IsUnknownSize || chunk.Undone(this) > 0) && readCount > 0 && !cancellationToken.IsCancellationRequested)
            {
                if (State >= DownloadFileState.Merging) break;
                if (source.IsFailed && source.SingleChunk != chunk) break;

                await ctx.SpeedGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
                var real = (int)(IsUnknownSize ? readCount : Math.Min((long)readCount, chunk.Undone(this)));
                ctx.SpeedGate.Consume(real);

                var delta = chunk.LastReceiveTime < 0 ? 0 : Environment.TickCount64 - chunk.LastReceiveTime;

                if (chunk.Done == 0)
                {
                    chunk.Phase = ChunkPhase.Downloading;
                    SetState(DownloadFileState.Downloading);
                    firstDataTime = Environment.TickCount64;
                    Interlocked.Increment(ref _connectCount);
                    Interlocked.Add(ref _connectTimeTotal, Environment.TickCount64 - chunk.InitTime);
                }
                source.FailCount = 0;
                Interlocked.Add(ref source.DeliveredBytes, real);
                Interlocked.Add(ref ctx.TotalBytesDone, real);
                Interlocked.Add(ref DownloadDone, real);
                Interlocked.Add(ref BytesDownloaded, real);
                chunk.Done += real;
                if (useMemory) _cache!.Write(buffer, 0, real);
                else await fileStream!.WriteAsync(buffer.AsMemory(0, real), cancellationToken).ConfigureAwait(false);

                if (!IsUnknownSize && chunk.Undone(this) == 0) break;

                // 速度过慢检测（停滞断开）
                if (chunk.LastReceiveTime > 0 && delta > 5000 && delta > real && source.SingleChunk == null)
                    throw new TimeoutException($"由于速度过慢断开链接，下载 {real} B，消耗 {delta} ms。");
                chunk.LastReceiveTime = Environment.TickCount64;

                // 速度过慢检测（换源续传）：连接还活着但平均速度长期过低时，保留断点换其他源继续
                if (firstDataTime > 0 && ShouldRotateSlowSource(chunk, options, firstDataTime))
                {
                    var average = chunk.Done * 1000.0 / Math.Max(1, Environment.TickCount64 - firstDataTime);
                    Interlocked.Increment(ref _slowRotateCount);
                    options.Log?.Invoke($"{LocalName}：下载源速度过慢（约 {average / 1024:0.#} KB/s），保留断点换源续传");
                    break; // 正常结束判定会因 Undone > 0 将本分块标记为 Canceled，由调度器从断点续传
                }

                readCount = await ctx.ReadWithTimeout(responseStream, buffer, stallTimeout, cancellationToken).ConfigureAwait(false);
            }

            // ---------- 结束判定 ----------
            bool undoneLeft;
            lock (ChunksLock) undoneLeft = !IsUnknownSize && chunk.Undone(this) > 0;
            if (State == DownloadFileState.Canceled ||
                (source.IsFailed && source.SingleChunk != chunk) || undoneLeft)
            {
                chunk.Phase = ChunkPhase.Canceled;
                options.Log?.Invoke($"{LocalName}：分块中断");
            }
            else if (readCount == 0 && !IsUnknownSize && chunk.Undone(this) > 0)
            {
                throw new Exception(
                    $"返回的 ContentLength 过多：ContentLength 为 {contentLength}，但获取到的总数据量仅为 {chunk.Done}（全文件总数据量 {DownloadDone}）");
            }
            else
            {
                chunk.Phase = ChunkPhase.Finished;
            }
        }
        catch (Exception ex)
        {
            if (ex is HttpStatusCodeException { StatusCode: 416 })
                ex = new RangeNotSupportedException(ex.Message);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            // 连接/响应超时产生的 OCE（主令牌未取消）：转义为明确的超时异常，避免误导性的"操作已取消"
            if (ex is OperationCanceledException)
                ex = new TimeoutException($"连接或响应超时（上限 {timeout}ms）：{source.Url}", ex);
            // 与 VB 对齐（ModNet.vb NetThread 的 Catch）：每次分块失败都必须留痕。
            // 此前普通失败（非禁用路径）完全静默，UI 表现为无报错的"卡死"，日志无从排查
            options.Log?.Invoke(ex is TimeoutException
                ? $"{LocalName}：超时（{timeout}ms），{ex.Message}"
                : $"{LocalName}：出错，{ex.Message}");
            OnChunkFailure(chunk, ex, isMergeFailure: false, options, ctx);
        }
        finally
        {
            request?.Dispose();
            response?.Dispose();
            responseStream?.Dispose();
            fileStream?.Dispose();
            ctx.OnChunkExited();
            // 归还全局并发名额（调度器在启动分块前已占用）
            options.SharedBudget?.Release();
            // 归还主机并发名额（在 TryBeginChunk 中占用）
            HostConcurrency.Exit(chunk.HostKey);
            // 合并判定
            var shouldMerge = ((FileSize >= 0 && DownloadDone >= FileSize) || (FileSize == -1 && DownloadDone > 0)) &&
                              State < DownloadFileState.Merging && chunk.Phase != ChunkPhase.Canceled;
            if (shouldMerge) await MergeAsync(chunk, options, ctx, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ApplyBrowserHeaders(HttpRequestMessage request, DownloadFileRequest fileRequest, string url)
    {
        if (fileRequest.HeaderProvider is not null)
        {
            foreach (var (key, value) in fileRequest.HeaderProvider(url))
                request.Headers.TryAddWithoutValidation(key, value);
            return; // 自定义请求头拥有最高优先级，直接接管
        }
        if (!fileRequest.SimulateBrowserHeaders) return;
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        request.Headers.Referrer = new Uri("https://www.minecraft.net/");
    }

    private void CheckDiskSpace(long contentLength, DownloadOptions options)
    {
        var targetDrive = Path.GetPathRoot(Path.GetFullPath(Request.LocalPath));
        if (string.IsNullOrEmpty(targetDrive)) return;
        var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
            string.Equals(d.Name, targetDrive, StringComparison.OrdinalIgnoreCase));
        if (drive is null) return;
        if (drive.TotalFreeSpace < contentLength + 5L * 1024 * 1024)
            throw new Exception(
                $"{drive.Name} 盘空间不足，无法进行下载。需要至少 {contentLength + 5L * 1024 * 1024} 空间，但当前仅剩余 {drive.TotalFreeSpace}。");
    }

    // ===================== 失败处理 =====================

    private void OnChunkFailure(Chunk chunk, Exception ex, bool isMergeFailure, DownloadOptions options, EngineContext ctx)
    {
        Interlocked.Increment(ref chunk.Source.FailCount);
        // 记录失败时刻与次数（必须先于 Phase 变更，确保调度线程看到 Canceled 时退避时间已可见）：
        // 断点续传按指数退避重试，避免「失败即重拉」的请求风暴
        chunk.FailTime = Environment.TickCount64;
        chunk.FailCount++;
        chunk.Phase = ChunkPhase.Canceled;
        chunk.Source.Error = ex;

        var message = ex.Message;
        // 状态码判定必须走类型化检查：异常文本是「错误码 429，url」不含括号，
        // 此前 message.Contains("(429)") 形态的匹配永远为假，限速/禁用规则全是死代码
        // （真机日志实锤：429 风暴期间源始终未被禁用、未被限速）。
        var statusCode = (ex as HttpStatusCodeException)?.StatusCode
                         ?? (ex.InnerException as HttpStatusCodeException)?.StatusCode;
        // 主机惩罚：失败后对该主机冷却一段时间再发起新连接。
        // 429/503 是限速信号，冷却更久（真机 NeoForge 安装：分块失败后每 20ms 重拉 →
        // 数十并发 Range 请求把 BMCLAPI 打出 429 → 全线 0 B/s 卡死）。
        var isRateLimited = statusCode is 429 or 503 || message.Contains("(429)") || message.Contains("(503)");
        HostStartThrottle.Penalize(chunk.Source.Url,
            isRateLimited ? options.RateLimitCooldownMilliseconds : options.RetryBackoffMilliseconds);

        // 零进展计数：本次分块一个字节都没收到 → 该源在空转（建连成功但不发数据、或被墙）。
        // 一旦有进展就清零。429/503 是限速而非死源，不计入（走冷却重试）。
        if (chunk.Done > 0) chunk.Source.ZeroProgressFails = 0;
        else if (!isRateLimited) Interlocked.Increment(ref chunk.Source.ZeroProgressFails);

        // 是否还有别的可用源可切：只有存在备选时才敢快速判定死源，否则会把自己逼到无源可用
        bool hasOtherSource;
        lock (ChunksLock) hasOtherSource = Sources.Any(s => !ReferenceEquals(s, chunk.Source) && !s.IsFailed);

        var isRangeNotSupported = ex is RangeNotSupportedException || statusCode == 416 || message.Contains("(416)");
        var isDiskFull = message.Contains("空间不足");
        var shouldDisableSource = isMergeFailure || isRangeNotSupported ||
            statusCode is 502 or 404 || message.Contains("(502)") || message.Contains("(404)") ||
            message.Contains("未能解析") || message.Contains("无返回数据") || isDiskFull ||
            ((statusCode is 403 or 429 || message.Contains("(403)") || message.Contains("(429)")) &&
                !chunk.Source.Url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase)) ||
            // 死源快判：连续 2 次零进展且还有别的源可用 → 立刻换源。
            // 原实现只有「从未送达过字节」才算死源，源只要曾吐过几个字节就要攒满
            // MaxConcurrentChunks+2 次（30 秒 × 35~67 ≈ 十几到几十分钟）才禁用，
            // 这正是真机上「卡在 2% 好久突然就好了」的时间来源。
            (chunk.Source.ZeroProgressFails >= 2 && hasOtherSource) ||
            // 从未送达任何字节且已连续失败多次：按死源处理（连接挂起的源往往永远无法送达），
            // 避免按 MaxConcurrentChunks+2 的宽容阈值拖上十余分钟（真机 NeoForge 安装卡 2% 复现）
            (chunk.Source.FailCount >= NeverDeliveredDisableFailCount && Interlocked.Read(ref chunk.Source.DeliveredBytes) == 0) ||
            (chunk.Source.FailCount >= Math.Clamp(options.MaxConcurrentChunks, 5, 30) && DownloadDone < 1) ||
            chunk.Source.FailCount > options.MaxConcurrentChunks + 2;

        if (shouldDisableSource)
        {
            bool thisThreadDisables;
            lock (ChunksLock)
            {
                thisThreadDisables = !chunk.Source.IsFailed || chunk.Source.SingleChunk == chunk;
                if (thisThreadDisables) chunk.Source.IsFailed = true;
            }
            if (thisThreadDisables)
            {
                options.Log?.Invoke($"{LocalName}：下载源被禁用（{chunk.Source.Id}，Range 问题：{isRangeNotSupported}）：{chunk.Source.Url}");
                options.LogWarn?.Invoke(ex, $"下载源 {chunk.Source.Id} 已被禁用");
                lock (ChunksLock)
                {
                    var onceList = _onceSources.ToList();
                    onceList.Remove(chunk.Source);
                    _onceSources.Clear();
                    foreach (var s in onceList) _onceSources.Enqueue(s);
                    if (chunk.Source.SingleChunk == chunk) chunk.Source.SingleChunk = null;
                }

                bool hasMultiSource, hasOnce, inOnceMode;
                lock (ChunksLock)
                {
                    hasMultiSource = Sources.Any(s => !s.IsFailed);
                    hasOnce = _onceSources.Count > 0;
                    inOnceMode = !hasMultiSource && hasOnce;
                }

                if (isDiskFull)
                {
                    Fail(ex);
                }
                else if (hasMultiSource && !isMergeFailure)
                {
                    // 还有可用源，继续（可见性：静默重试也要留痕，否则 UI 上表现为"卡死"）
                    options.LogWarn?.Invoke(ex,
                        $"{LocalName}：分块失败，自动换源重试（第 {chunk.Source.FailCount} 次，源 {chunk.Source.Id}）");
                }
                else if (inOnceMode && !isMergeFailure)
                {
                    // 单线程模式：换下一个源重来
                    Reset(options);
                    State = DownloadFileState.WaitingToDownload;
                }
                else if (!_retried)
                {
                    // 首次整体失败：把所有源转入单线程逐个重试
                    if (!isRangeNotSupported) options.LogWarn?.Invoke(ex, $"{LocalName}：文件下载失败，正在自动重试……");
                    _retried = true;
                    lock (ChunksLock)
                    {
                        _onceSources.Clear();
                        foreach (var source in Sources)
                        {
                            source.SingleChunk = null;
                            _onceSources.Enqueue(source);
                        }
                    }
                    Reset(options);
                    State = DownloadFileState.WaitingToDownload;
                }
                else if (hasMultiSource && isMergeFailure)
                {
                    Reset(options);
                    State = DownloadFileState.WaitingToDownload;
                }
                else
                {
                    options.Log?.Invoke($"{LocalName}：已无可用下载源，下载失败");
                    Exception? example = null;
                    lock (ChunksLock)
                    {
                        foreach (var source in Sources)
                        {
                            if (source.Error is not null)
                            {
                                example = source.Error;
                                options.LogWarn?.Invoke(source.Error, $"下载源 {source.Url} 禁用原因");
                            }
                        }
                    }
                    Fail(example ?? ex);
                }
            }
        }

        if (FileSize == -2) Reset(options);
    }

    /// <summary>最近一次「有活动分块或已完成」的时刻，用于兜底重启的静默时长统计。</summary>
    public long IdleSince;

    /// <summary>
    /// 兜底重启：没有任何活动分块、却仍有未下载区间时（区间被孤儿化），清空重下。
    /// 只作为最后防线，正常情况下不应触发（真机曾因此永久卡在 66%）。
    /// </summary>
    public bool ForceRestart(DownloadOptions options)
    {
        lock (ChunksLock)
        {
            if (Chunks.Any(c => !c.IsEnded)) return false;
        }
        options.Log?.Invoke($"{LocalName}：长时间无进展且仍有未下载数据，重新开始下载");
        Reset(options);
        State = DownloadFileState.WaitingToDownload;
        return true;
    }

    private void Reset(DownloadOptions options)
    {
        FileSize = -2;
        IsUnknownSize = false;
        _cache?.Dispose();
        _cache = null;
        lock (ChunksLock)
        {
            foreach (var chunk in Chunks.Where(c => c.TempPath is not null))
            {
                try { if (chunk.TempPath is not null) File.Delete(chunk.TempPath); } catch { }
            }
            Chunks.Clear();
        }
        Interlocked.Add(ref BytesDownloaded, -DownloadDone);
        DownloadDone = 0;
    }

    // ===================== 合并与收尾 =====================

    private async Task MergeAsync(Chunk trigger, DownloadOptions options, EngineContext ctx, CancellationToken cancellationToken)
    {
        if (State < DownloadFileState.Merging) State = DownloadFileState.Merging;
        else return;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Request.LocalPath)!);
                if (File.Exists(Request.LocalPath)) File.Delete(Request.LocalPath);

                lock (ChunksLock)
                {
                    if (IsNoSplit)
                    {
                        options.Log?.Invoke($"{LocalName}：下载结束，从内存输出文件");
                        using var output = new FileStream(Request.LocalPath, FileMode.Create, FileAccess.Write);
                        _cache!.Position = 0;
                        _cache.CopyTo(output);
                    }
                    else
                    {
                        var ordered = Chunks.OrderBy(c => c.Start).ToList();
                        var single = ordered.Count == 1 ? ordered[0] : null;
                        if (single?.TempPath is not null)
                        {
                            options.Log?.Invoke($"{LocalName}：下载结束，仅有一个分块，无需合并");
                            // 同卷优先 Move，免去一次全量拷贝（大文件收益明显）；跨卷回退为复制
                            try
                            {
                                File.Move(single.TempPath, Request.LocalPath, overwrite: true);
                            }
                            catch
                            {
                                File.Copy(single.TempPath, Request.LocalPath, overwrite: true);
                                File.Delete(single.TempPath);
                            }
                            single.TempPath = null;
                        }
                        else
                        {
                            options.Log?.Invoke($"{LocalName}：下载结束，开始合并 {ordered.Count} 个分块");
                            using var output = new FileStream(Request.LocalPath, FileMode.Create, FileAccess.Write);
                            foreach (var chunk in ordered)
                            {
                                if (chunk.Done == 0 || chunk.TempPath is null) continue;
                                using var temp = new FileStream(chunk.TempPath, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 64, FileOptions.SequentialScan);
                                var remaining = chunk.Done;
                                var buffer = new byte[1024 * 256];
                                while (remaining > 0)
                                {
                                    var read = temp.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                                    if (read <= 0) throw new IOException($"分块文件意外结束：{chunk.TempPath}");
                                    output.Write(buffer, 0, read);
                                    remaining -= read;
                                }
                            }
                        }
                    }
                }

                // 校验
                var checkError = Check?.Validate(Request.LocalPath);
                if (checkError is null && !IsUnknownSize && Check is not null)
                {
                    if (Check.ActualSize == -1)
                    {
                        checkError = new FileCheckRule { ActualSize = FileSize }.Validate(Request.LocalPath);
                    }
                    else if (Check.ActualSize != FileSize)
                    {
                        checkError = $"文件大小不一致：任务校验要求为 {Check.ActualSize}，请求结果为 {FileSize}";
                    }
                }
                if (checkError is not null)
                {
                    lock (ChunksLock)
                    {
                        foreach (var chunk in Chunks)
                            options.Log?.Invoke($"- 分块 {chunk.Start}~，已下载 {chunk.Done}，状态 {chunk.Phase}");
                    }
                    throw new Exception(checkError);
                }

                // 清理分块临时文件
                if (!IsNoSplit)
                {
                    lock (ChunksLock)
                    {
                        foreach (var chunk in Chunks)
                        {
                            if (chunk.TempPath is not null)
                            {
                                try { File.Delete(chunk.TempPath); } catch { }
                                chunk.TempPath = null;
                            }
                        }
                    }
                }
                Finish();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (State > DownloadFileState.Merging) return;
                options.LogWarn?.Invoke(ex, $"合并文件出错，第 {attempt} 次尝试（{LocalName}）");
                if (attempt >= 3)
                {
                    try { File.Delete(Request.LocalPath); } catch { }
                    OnChunkFailure(trigger, ex, isMergeFailure: true, options, ctx);
                    return;
                }
                await Task.Delay(500 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Fail(Exception? ex)
    {
        if (State >= DownloadFileState.Finished) return;
        if (ex is not null) _errors.Add(ex);
        State = DownloadFileState.Canceled;
        var errors = _errors.Concat(Sources.Where(s => s.Error is not null).Select(s => s.Error!)).ToList();
        FileFailed.Invoke(this, errors);
    }

    public void Finish()
    {
        if (State >= DownloadFileState.Finished) return;
        State = DownloadFileState.Finished;
        FileFinished.Invoke(this);
    }

    public void Cancel(DownloadOptions options)
    {
        if (State >= DownloadFileState.Finished) return;
        State = DownloadFileState.Canceled;
        lock (ChunksLock)
        {
            foreach (var chunk in Chunks) chunk.Phase = ChunkPhase.Canceled;
        }
    }

    public IReadOnlyList<Exception> CollectErrors() =>
        _errors.Concat(Sources.Where(s => s.Error is not null).Select(s => s.Error!)).ToList();

    public event Action<FileTask> FileFinished = _ => { };
    public event Action<FileTask, IReadOnlyList<Exception>> FileFailed = (_, _) => { };

    public void BindCallbacks(Action<FileTask> onFinished, Action<FileTask, IReadOnlyList<Exception>> onFailed)
    {
        FileFinished += onFinished;
        FileFailed += onFailed;
    }
}

/// <summary>带状态码的 HTTP 异常（便于引擎判断 4xx/5xx）。</summary>
public sealed class HttpStatusCodeException : HttpRequestException
{
    public HttpStatusCodeException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public new int StatusCode { get; }
}
