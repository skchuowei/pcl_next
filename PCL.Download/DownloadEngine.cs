using System.Collections.Concurrent;
using System.Diagnostics;

namespace PCL.Download;

/// <summary>
/// C# 多源多线程下载引擎（对应 VB 的 NetManagerClass + LoaderDownload 组合）。
/// </summary>
public sealed class DownloadEngine : IDownloadEngine
{
    public async Task<DownloadReport> DownloadAsync(
        IEnumerable<DownloadFileRequest> files,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DownloadOptions();
        var stopwatch = Stopwatch.StartNew();

        // ---------- 输入校验与去重 ----------
        var taskByPath = new Dictionary<string, FileTask>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in files)
        {
            if (request.Sources is not { Count: > 0 })
                throw new ArgumentException($"下载请求缺少下载源：{request.LocalPath}");
            var localPath = request.LocalPath.Replace("/", "\\");
            if (!localPath.Contains(":\\")) throw new ArgumentException($"输入的本地文件地址不正确：{localPath}");
            if (localPath.EndsWith("\\")) throw new ArgumentException($"请输入含文件名的完整文件路径：{localPath}");
            var key = localPath.ToLowerInvariant();
            if (taskByPath.ContainsKey(key)) continue; // 同目标文件只保留一个任务
            var normalized = new DownloadFileRequest
            {
                Sources = request.Sources,
                LocalPath = localPath,
                Check = request.Check,
                SimulateBrowserHeaders = request.SimulateBrowserHeaders,
            };
            var task = FileTask.Create(normalized, taskByPath.Count);
            foreach (var source in task.Sources)
            {
                if (!source.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !source.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    source.Error = new ArgumentException("输入的下载链接不正确：" + source.Url);
                    source.IsFailed = true;
                }
            }
            taskByPath[key] = task;
        }

        var tasks = taskByPath.Values.ToList();
        if (tasks.Count == 0)
            return new DownloadReport { Files = [], TotalBytesDownloaded = 0, Elapsed = stopwatch.Elapsed };
        foreach (var invalid in tasks.Where(t => !t.HasAvailableSource()).ToList())
            throw new ArgumentException($"输入的下载链接不正确！（{invalid.LocalName}）");

        var tempFolder = Path.Combine(Path.GetTempPath(), "pcl-download", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        var context = new EngineContext(options, tempFolder);
        context.SpeedGate.Enabled = options.SpeedLimitHigh > 0;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = tasks.Count;
        var remainingLock = new object();
        var failures = new Dictionary<string, IReadOnlyList<Exception>>();
        Exception? firstFailure = null;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFileFinished(FileTask task)
        {
            lock (remainingLock)
            {
                remaining--;
                if (remaining <= 0 && firstFailure is null) completion.TrySetResult(true);
            }
        }

        void OnFileFailed(FileTask task, IReadOnlyList<Exception> errors)
        {
            var shouldTrigger = false;
            lock (remainingLock)
            {
                remaining--;
                failures[task.Request.LocalPath] = errors;
                if (firstFailure is null)
                {
                    firstFailure = new Exception($"文件下载失败：{task.Request.LocalPath}");
                    shouldTrigger = true;
                }
                if (remaining <= 0) completion.TrySetResult(false);
            }
            if (shouldTrigger)
            {
                // 任一文件失败：立即中断全部文件（与 VB 的 OnFail 行为一致）
                linkedCts.Cancel();
                foreach (var other in tasks) other.Cancel(options);
                completion.TrySetResult(false);
            }
        }

        foreach (var task in tasks) task.BindCallbacks(OnFileFinished, OnFileFailed);

        // 用户取消：中断所有文件并使完成信号就绪
        using var userCancelRegistration = cancellationToken.Register(() =>
        {
            linkedCts.Cancel();
            foreach (var task in tasks) task.Cancel(options);
            completion.TrySetResult(false);
        });

        // ---------- 已存在文件查找与本地复用 ----------
        foreach (var task in tasks.Where(t => t.Check is null))
            task.SetState(DownloadFileState.WaitingToDownload);
        await ReuseExistingFilesAsync(tasks.Where(t => t.Check is not null).ToList(), options, cancellationToken)
            .ConfigureAwait(false);

        var scheduler = SchedulerLoopAsync(tasks, options, context, linkedCts.Token);
        var statLoop = StatLoopAsync(tasks, options, context, progress, linkedCts.Token);

        try
        {
            var succeeded = await completion.Task.ConfigureAwait(false);
            await linkedCts.CancelAsync().ConfigureAwait(false);
            // 统计线程按 tick 上报，小文件可能在最终进度上报前就完成，这里补发一次最终进度
            if (progress is not null)
            {
                var final = new DownloadProgress
                {
                    OverallProgress = succeeded ? 1.0 : 0.0,
                    TotalBytesDone = Interlocked.Read(ref context.TotalBytesDone),
                    Speed = 0,
                    ActiveChunks = 0,
                };
                if (succeeded) progress.Report(final);
            }
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (!succeeded)
            {
                var failure = firstFailure!;
                throw new DownloadFailedException(failures.Count > 0
                    ? failures
                    : new Dictionary<string, IReadOnlyList<Exception>>
                    {
                        [failure.Message] = [failure],
                    });
            }
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch { /* 缓存清理失败可忽略 */ }
        }

        var cancelledByUser = cancellationToken.IsCancellationRequested;
        if (cancelledByUser) throw new OperationCanceledException(cancellationToken);

        return new DownloadReport
        {
            Files = tasks.Select(t => new DownloadFileReport
            {
                LocalPath = t.Request.LocalPath,
                State = t.State,
                IsLocalReuse = t.IsLocalReuse,
                BytesDownloaded = Math.Max(0, Interlocked.Read(ref t.BytesDownloaded)),
            }).ToList(),
            TotalBytesDownloaded = Interlocked.Read(ref context.TotalBytesDone),
            Elapsed = stopwatch.Elapsed,
        };
    }

    // ===================== 调度循环 =====================

    private static async Task SchedulerLoopAsync(
        List<FileTask> tasks, DownloadOptions options, EngineContext context, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);

                var speed = Math.Max(Interlocked.Read(ref context.CurrentSpeed), Interlocked.Read(ref context.DynamicSpeedFloor));
                var needMoreThreads = speed < options.SpeedLimitLow;

                foreach (var task in tasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var state = task.State;
                    if (state >= DownloadFileState.Merging) continue;

                    var running = Interlocked.Read(ref context.ActiveChunks);
                    var isWaiting = state == DownloadFileState.WaitingToDownload;
                    if (!isWaiting && !needMoreThreads) continue;

                    if (!isWaiting)
                    {
                        // 进行中的文件：准备中的分块已多于下载中的分块时不再新增
                        int preparing = 0, downloading = 0;
                        lock (task.ChunksLock)
                        {
                            foreach (var c in task.Chunks)
                            {
                                if (c.Phase < ChunkPhase.Downloading) preparing++;
                                else if (c.Phase == ChunkPhase.Downloading) downloading++;
                            }
                        }
                        if (preparing > downloading) continue;
                    }

                    var chunk = task.TryBeginChunk(options, (int)running);
                    if (chunk is null) continue;
                    Interlocked.Increment(ref context.ActiveChunks);
                    if (chunk.Source.Url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase))
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false); // 减少 BMCLAPI 请求频率
                    _ = task.RunChunkAsync(chunk, options, context, cancellationToken)
                        .ContinueWith(
                            t => options.LogWarn?.Invoke(t.Exception!.InnerException ?? t.Exception, "下载分块出现未处理异常"),
                            TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            options.LogWarn?.Invoke(ex, "下载调度线程出错");
        }
    }

    private static async Task StatLoopAsync(
        List<FileTask> tasks, DownloadOptions options, EngineContext context,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var speedWindow = new List<double>();
        long lastDone = 0, lastTick = Environment.TickCount64;
        long nextTick = Environment.TickCount64;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(options.TickMilliseconds, cancellationToken).ConfigureAwait(false);

                // 限速令牌补充
                if (options.SpeedLimitHigh > 0)
                    context.SpeedGate.Refill(options.SpeedLimitHigh * options.TickMilliseconds / 1000);

                // 全局速度（近 30 个记录加权平均）
                var now = Environment.TickCount64;
                var delta = now - lastTick;
                if (delta > 0)
                {
                    var done = Interlocked.Read(ref context.TotalBytesDone);
                    var actual = Math.Max(0, (done - lastDone) / (delta / 1000.0));
                    lastDone = done;
                    lastTick += delta;
                    speedWindow.Insert(0, actual);
                    if (speedWindow.Count >= 31) speedWindow.RemoveAt(30);
                    double sum = 0, div = 0;
                    var weight = speedWindow.Count;
                    foreach (var record in speedWindow)
                    {
                        sum += record * weight;
                        div += weight;
                        weight--;
                    }
                    Interlocked.Exchange(ref context.CurrentSpeed, (long)(div > 0 ? sum / div : 0));
                    // 速度下限自适应提升
                    if (speedWindow.Count >= 10)
                    {
                        var limit = (long)(speedWindow.Take(10).Average() * 0.85);
                        if (limit > Interlocked.Read(ref context.DynamicSpeedFloor))
                            Interlocked.Exchange(ref context.DynamicSpeedFloor, limit);
                    }
                }

                if (progress is null) continue;
                double newProgress = 0, totalProgress = 0;
                long totalExpected = 0;
                var allSizesKnown = true;
                foreach (var task in tasks)
                {
                    if (task.IsLocalReuse)
                    {
                        newProgress += task.Progress * 0.2;
                        totalProgress += 0.2;
                    }
                    else
                    {
                        newProgress += task.Progress;
                        totalProgress += 1;
                    }
                    // 统计总大小；未知大小（IsUnknownSize）时不计入并整体标记为未知
                    if (task.FileSize >= 0) totalExpected += task.FileSize;
                    else if (!task.IsLocalReuse) allSizesKnown = false;
                }
                if (totalProgress > 0 && !double.IsNaN(totalProgress)) newProgress /= totalProgress;
                progress.Report(new DownloadProgress
                {
                    OverallProgress = newProgress,
                    TotalBytesDone = Interlocked.Read(ref context.TotalBytesDone),
                    TotalBytesExpected = allSizesKnown ? totalExpected : -1,
                    Speed = Interlocked.Read(ref context.CurrentSpeed),
                    ActiveChunks = (int)Interlocked.Read(ref context.ActiveChunks),
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            options.LogWarn?.Invoke(ex, "下载统计线程出错");
        }
    }

    // ===================== 已存在文件复用 =====================

    /// <summary>
    /// 对带有校验规则的文件查找本地相同文件（目标文件本身、复用目录中的同路径/同哈希文件），
    /// 命中则直接复制完成，否则转入待下载状态。与 VB 的 CheckExistingFiles 对齐。
    /// </summary>
    private static async Task ReuseExistingFilesAsync(
        List<FileTask> tasks, DownloadOptions options, CancellationToken cancellationToken)
    {
        if (tasks.Count == 0) return;
        var threadCount = Math.Clamp(tasks.Count / 40, 1, 8);
        var groups = tasks.Chunk((int)Math.Ceiling(tasks.Count / (double)threadCount));
        await Task.WhenAll(groups.Select(group => Task.Run(() =>
        {
            foreach (var task in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReuseSingleFile(task, options);
            }
        }, cancellationToken))).ConfigureAwait(false);
    }

    private static void ReuseSingleFile(FileTask task, DownloadOptions options)
    {
        var check = task.Check!;
        var localPath = task.Request.LocalPath;

        // 目标文件本身已有效
        if (check.Validate(localPath) is null)
        {
            task.IsLocalReuse = true;
            task.IsSkippedExisting = true;
            task.Finish();
            return;
        }
        if (!options.EnableLocalReuse || (check.Hash is null && check.ActualSize < 0))
        {
            task.SetState(DownloadFileState.WaitingToDownload);
            return;
        }

        var candidate = LocalFileReuse.FindReuseCandidate(localPath, check, options.LocalReuseFolders);
        if (candidate is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                File.Copy(candidate, localPath, overwrite: true);
                task.IsLocalReuse = true;
                task.Finish();
                return;
            }
            catch
            {
                try { File.Delete(localPath); } catch { }
            }
        }
        task.SetState(DownloadFileState.WaitingToDownload);
    }
}

/// <summary>
/// 跨版本文件复用：按路径类型标记（assets/libraries/versions/...）在复用目录中查找相同哈希的文件。
/// </summary>
public static class LocalFileReuse
{
    private static readonly string[] TypeMarkers =
    [
        "\\assets\\", "\\libraries\\", "\\versions\\", "\\mods\\", "\\coremods\\",
        "\\lib\\", "\\resourcepacks\\", "\\texturepacks\\", "\\shaderpacks\\",
    ];

    private static readonly ConcurrentDictionary<string, long> SizeCache = new(StringComparer.OrdinalIgnoreCase);

    public static string? FindReuseCandidate(string localPath, FileCheckRule check, IReadOnlyList<string> reuseFolders)
    {
        if (reuseFolders.Count == 0) return null;

        // 大致判断文件类别
        var typeHits = TypeMarkers
            .Select(marker => (Marker: marker, Index: localPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase)))
            .Where(hit => hit.Index >= 0)
            .ToList();
        string type;
        if (typeHits.Count == 0)
        {
            if (!string.Equals(Path.GetExtension(localPath), ".jar", StringComparison.OrdinalIgnoreCase)) return null;
            type = "\\versions\\";
        }
        else
        {
            type = typeHits.MaxBy(hit => hit.Index).Marker;
        }

        bool CandidateOk(string candidate)
        {
            var length = SizeCache.GetOrAdd(candidate, static path
                => File.Exists(path) ? new FileInfo(path).Length : -1);
            if (check.ActualSize >= 0 && check.ActualSize != length) return false;
            return check.Validate(candidate) is null;
        }

        var relativeAfter = localPath[(localPath.IndexOf(type, StringComparison.OrdinalIgnoreCase) + type.Length)..];

        switch (type)
        {
            case "\\assets\\":
            case "\\libraries\\":
            {
                foreach (var folder in reuseFolders)
                {
                    var candidate = Path.Combine(folder, type.TrimStart('\\'), relativeAfter.Replace('\\', Path.DirectorySeparatorChar));
                    if (CandidateOk(candidate)) return candidate;
                }
                return null;
            }
            case "\\versions\\":
            {
                var extension = Path.GetExtension(localPath);
                foreach (var folder in reuseFolders)
                {
                    var versionRoot = Path.Combine(folder, "versions");
                    if (!Directory.Exists(versionRoot)) continue;
                    foreach (var versionDir in Directory.EnumerateDirectories(versionRoot))
                    foreach (var candidate in Directory.EnumerateFiles(versionDir, "*" + extension))
                        if (CandidateOk(candidate)) return candidate;
                }
                return null;
            }
            default:
            {
                // 社区资源：必须同时指定大小与哈希，避免全盘误复制
                if (check.ActualSize < 0 || check.Hash is null) return null;
                foreach (var folder in reuseFolders)
                {
                    var dir = Path.Combine(folder, type.TrimStart('\\'));
                    if (!Directory.Exists(dir)) continue;
                    foreach (var candidate in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        if (CandidateOk(candidate)) return candidate;
                }
                return null;
            }
        }
    }
}

/// <summary>
/// 引擎运行期共享上下文。
/// </summary>
internal sealed class EngineContext
{
    public EngineContext(DownloadOptions options, string tempFolder)
    {
        Options = options;
        TempFolder = tempFolder;
        Client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.GZip,
            UseCookies = false,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan, // 超时由引擎自行控制（含分块级动态超时）
        };
    }

    public DownloadOptions Options { get; }
    public string TempFolder { get; }
    public HttpClient Client { get; }
    public SpeedGate SpeedGate { get; } = new();
    public long TotalBytesDone;
    public long CurrentSpeed;
    public long DynamicSpeedFloor;
    public long ActiveChunks;

    public void OnChunkExited() => Interlocked.Decrement(ref ActiveChunks);

    /// <summary>带超时的一次流读取：超时视为无数据。</summary>
    public async Task<int> ReadWithTimeout(Stream stream, byte[] buffer, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);
        try
        {
            return await stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("操作超时，无数据。");
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }
}

/// <summary>
/// 全局限速令牌桶（对应 VB 的 NetTaskSpeedLimitLeft/High）。
/// </summary>
internal sealed class SpeedGate
{
    private long _tokens;
    /// <summary>是否启用限速；未启用时获取与消耗均为空操作。</summary>
    public bool Enabled;

    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        if (!Enabled) return;
        while (Interlocked.Read(ref _tokens) <= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(16, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Consume(long bytes)
    {
        if (Enabled && bytes > 0) Interlocked.Add(ref _tokens, -bytes);
    }

    public void Refill(long bytes)
    {
        if (Enabled && bytes > 0) Interlocked.Add(ref _tokens, bytes);
    }
}
