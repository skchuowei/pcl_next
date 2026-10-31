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
                    var original = IsUnknownSize ? 0.5 : DownloadDone / Math.Max(FileSize, 1);
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
                        // 接续失败点
                        Chunk? broken = null;
                        foreach (var c in Chunks.OrderBy(c => c.Start))
                        {
                            if (c.Phase == ChunkPhase.Canceled && c.Undone(this) > 0)
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
                        }
                        else
                        {
                            // 追加线程：切割最大碎片
                            source = GetSource();
                            if (source is null) return null;
                            if (options.NoSplitHostKeywords.Any(k => source.Url.Contains(k, StringComparison.OrdinalIgnoreCase)))
                                return null;
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

                var chunk = new Chunk
                {
                    Start = start,
                    Source = source,
                };
                if (isSingleThreadOnce)
                {
                    source.SingleChunk = chunk;
                }
                Chunks.Add(chunk);
                return chunk;
            }
        }
        catch (Exception ex)
        {
            Fail(new Exception($"尝试开始下载分块失败（{LocalName}）", ex));
            return null;
        }
    }

    // ===================== 下载主体 =====================

    public async Task RunChunkAsync(
        Chunk chunk, DownloadOptions options, EngineContext ctx, CancellationToken cancellationToken)
    {
        var source = chunk.Source;
        var firstThread = chunk.Start == 0 && FileSize == -2;

        var connectAverage = ConnectAverage;
        var timeout = Math.Min(Math.Max(connectAverage < 0 ? 15000 : connectAverage, 15000) * (1 + source.FailCount),
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

            request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            ApplyBrowserHeaders(request, Request, chunk.Source.Url);
            var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(timeout);
            if (!firstThread) request.Headers.Range = new RangeHeaderValue(chunk.Start, null);

            response = await ctx.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectCts.Token)
                .WaitAsync(connectCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpStatusCodeException(
                    $"错误码 {(int)response.StatusCode}，{source.Url}", (int)response.StatusCode);
            if (State == DownloadFileState.Canceled) return;

            contentLength = response.Content.Headers.ContentLength ?? -1;

            // ---------- 文件大小与 Range 支持校验 ----------
            bool rangeNotSupported = false;
            if (contentLength < 0)
            {
                if (FileSize > 1)
                {
                    if (chunk.Start == 0)
                    {
                        options.Log?.Invoke($"{LocalName}：文件大小未知，但已从其他下载源获取，不作处理");
                    }
                    else
                    {
                        rangeNotSupported = true;
                    }
                }
                else
                {
                    FileSize = -1;
                    IsUnknownSize = true;
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

            chunk.Phase = ChunkPhase.Reading;
            SetState(DownloadFileState.Reading);

            // ---------- 创建缓存 ----------
            var useMemory = IsNoSplit;
            if (useMemory)
            {
                chunk.TempPath = null;
                _cache = new MemoryStream();
            }
            else
            {
                chunk.TempPath = Path.Combine(ctx.TempFolder, $"{Uuid}_{chunk.Start:x16}_{Random.Shared.Next(0, 999999)}.tmp");
                fileStream = new FileStream(chunk.TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64);
            }

            // ---------- 读取循环 ----------
            responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[16384];
            readCount = await ctx.ReadWithTimeout(responseStream, buffer, timeout, cancellationToken).ConfigureAwait(false);
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
                    Interlocked.Increment(ref _connectCount);
                    Interlocked.Add(ref _connectTimeTotal, Environment.TickCount64 - chunk.InitTime);
                }
                source.FailCount = 0;
                Interlocked.Add(ref ctx.TotalBytesDone, real);
                Interlocked.Add(ref DownloadDone, real);
                Interlocked.Add(ref BytesDownloaded, real);
                chunk.Done += real;
                if (useMemory) _cache!.Write(buffer, 0, real);
                else await fileStream!.WriteAsync(buffer.AsMemory(0, real), cancellationToken).ConfigureAwait(false);

                if (!IsUnknownSize && chunk.Undone(this) == 0) break;

                // 速度过慢检测
                if (chunk.LastReceiveTime > 0 && delta > 5000 && delta > real && source.SingleChunk == null)
                    throw new TimeoutException($"由于速度过慢断开链接，下载 {real} B，消耗 {delta} ms。");
                chunk.LastReceiveTime = Environment.TickCount64;

                readCount = await ctx.ReadWithTimeout(responseStream, buffer, timeout, cancellationToken).ConfigureAwait(false);
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
            OnChunkFailure(chunk, ex, isMergeFailure: false, options, ctx);
        }
        finally
        {
            request?.Dispose();
            response?.Dispose();
            responseStream?.Dispose();
            fileStream?.Dispose();
            ctx.OnChunkExited();
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
        chunk.Phase = ChunkPhase.Canceled;
        chunk.Source.Error = ex;

        var message = ex.Message;
        var isRangeNotSupported = ex is RangeNotSupportedException || message.Contains("(416)");
        var isDiskFull = message.Contains("空间不足");
        var shouldDisableSource = isMergeFailure || isRangeNotSupported ||
            message.Contains("(502)") || message.Contains("(404)") ||
            message.Contains("未能解析") || message.Contains("无返回数据") || isDiskFull ||
            ((message.Contains("(403)") || message.Contains("(429)")) && !chunk.Source.Url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase)) ||
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
                    // 还有可用源，继续
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
                            File.Copy(single.TempPath, Request.LocalPath, overwrite: true);
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
