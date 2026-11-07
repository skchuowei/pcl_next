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
            // 路径校验：Windows 要求盘符+反斜杠（VB 原版行为），其他平台要求绝对路径。
            // 此前硬编码 Windows 格式导致 Linux/macOS 上所有路径被拒，引擎无法在非 Windows 环境测试。
            var localPath = request.LocalPath;
            if (OperatingSystem.IsWindows())
            {
                localPath = localPath.Replace("/", "\\");
                if (!localPath.Contains(":\\")) throw new ArgumentException($"输入的本地文件地址不正确：{localPath}");
            }
            else
            {
                localPath = localPath.Replace("\\", "/");
                if (!Path.IsPathRooted(localPath)) throw new ArgumentException($"输入的本地文件地址不正确：{localPath}");
            }
            if (localPath.EndsWith(Path.DirectorySeparatorChar)) throw new ArgumentException($"请输入含文件名的完整文件路径：{localPath}");
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
                    TotalBytesDone = CurrentDone(tasks),
                    // 必须带上总大小：留默认 -1 会让上层把它当成「大小未知」，
                    // 镜像逻辑与进度计算都会走未知分支（曾导致收尾瞬间显示 NaN%）
                    TotalBytesExpected = tasks.Where(t => t.FileSize >= 0).Sum(t => t.FileSize),
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
            var firstPass = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                // 首轮立即调度（不空等一个周期），后续每 20ms 一轮
                if (!firstPass) await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                firstPass = false;

                // 与 VB 对齐（ModNet.vb：If Speed >= NetTaskSpeedLimitLow Then Continue While）：
                // 用「当前速度」对比「动态抬升后的下限」（初始为 SpeedLimitLow，随历史速度水涨船高），
                // 当前速度掉到该下限以下即为进行中的文件补分块。
                // 修复前误写为 max(当前速度, 动态下限) < 静态下限：文件先快后停时动态下限已被抬高，
                // max 恒取高值导致永远不再补分块，文件冻结在当前进度（真机 NeoForge 安装卡 16%、0 B/s）。
                var currentSpeed = Interlocked.Read(ref context.CurrentSpeed);
                var speedFloor = Math.Max(Interlocked.Read(ref context.DynamicSpeedFloor), options.SpeedLimitLow);
                var needMoreThreads = currentSpeed < speedFloor;

                foreach (var task in tasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var state = task.State;
                    if (state >= DownloadFileState.Merging) continue;

                    var running = Interlocked.Read(ref context.ActiveChunks);
                    var isWaiting = state == DownloadFileState.WaitingToDownload;

                    // 兜底看门狗：没有任何活动分块、也还有未下载数据时，静默超过 10 秒就清空重下。
                    // 正常流程不会走到这里；它只用于兜住「区间无人负责」这类逻辑漏洞，避免永久卡死。
                    var doneNow = Interlocked.Read(ref task.DownloadDone);
                    if (running == 0 && state < DownloadFileState.Merging && task.FileSize != -2 &&
                        task.FileSize >= 0 && doneNow < task.FileSize)
                    {
                        if (task.IdleSince == 0) task.IdleSince = Environment.TickCount64;
                        else if (Environment.TickCount64 - task.IdleSince > 10000)
                        {
                            task.IdleSince = 0;
                            if (task.ForceRestart(options)) continue;
                        }
                    }
                    else
                    {
                        task.IdleSince = 0;
                    }
                    // 断点续传是恢复被中断的下载，不受速度门限制（速度门只约束追加并发分块）
                    var isRecovering = task.HasBrokenChunk(options);
                    if (!isWaiting && !needMoreThreads && !isRecovering) continue;

                    if (!isWaiting && !isRecovering)
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

                    // 全局并发预算：跨引擎实例共享名额，占不到则下一轮再试
                    var budget = options.SharedBudget;
                    if (budget is not null && !budget.TryAcquire()) continue;

                    var chunk = task.TryBeginChunk(options, (int)running);
                    if (chunk is null)
                    {
                        budget?.Release();
                        continue;
                    }
                    Interlocked.Increment(ref context.ActiveChunks);
                    // 连接发起限频由 HostStartThrottle 在 TryBeginChunk 内非阻塞完成（原 Task.Delay(100) 会拖慢整个调度循环）
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

    /// <summary>所有任务当前已下载量之和（重置/重下会正确回退，不会像累计计数那样虚高）。</summary>
    private static long CurrentDone(List<FileTask> tasks)
    {
        long done = 0;
        foreach (var task in tasks) done += Interlocked.Read(ref task.DownloadDone);
        return done;
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
                    // 进度必须由各任务「当前已下载量」求和得出，不能用累计传输量：
                    // 失败重启/Range 重下会让累计量超过文件大小（真机实测 3.7MB 的文件累计到 7.9MB），
                    // 进度随之 >100% → 前端显示 NaN。
                    var done = CurrentDone(tasks);
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
                    OverallProgress = Math.Clamp(double.IsNaN(newProgress) ? 0 : newProgress, 0, 1),
                    TotalBytesDone = CurrentDone(tasks),
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

        // TypeMarkers 用反斜杠书写，跨平台统一成反斜杠后再匹配
        localPath = localPath.Replace('/', '\\');

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
                    var candidate = Path.Combine(folder, type.Trim('\\'), relativeAfter.Replace('\\', Path.DirectorySeparatorChar));
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
                    var dir = Path.Combine(folder, type.Trim('\\'));
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
    /// <summary>
    /// 跨引擎实例共享的 HTTP 客户端（连接池复用）。
    /// <para/>PCL 的一次安装会产生数百个小文件（支持库 / Assets），此前每个文件各建一个 HttpClient，
    /// 每个文件都要重新 TCP + TLS 握手且无法复用 Keep-Alive 连接，小文件场景吞吐被握手开销严重拖累。
    /// </summary>
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private static HttpClient CreateSharedClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.GZip,
            UseCookies = false,
            AllowAutoRedirect = true,
            // 允许并行分块的多连接，真正的并发数由 MaxConcurrentChunks / SharedBudget 控制
            MaxConnectionsPerServer = 256,
            // 安装阶段的连续小文件复用空闲连接，免去重复握手（下载提速插件同款：5min）
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            // HTTP/2 单流初始窗口默认仅 64KB，高带宽延迟积下吞吐被流控锁死
            // （下载提速插件 NetworkServiceTuningMixin 同款调优：64KB → 1MB）
            InitialHttp2StreamWindowSize = 1024 * 1024,
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan, // 超时由引擎自行控制（含分块级动态超时）
        };
        // 全局默认浏览器 UA：兼容按 UA 拦截 .NET 默认客户端（dotnet/10.0）的镜像/WAF。
        // 注意：另有部分 WAF 按 TLS ClientHello 指纹（JA3）识别 .NET 栈，此类 403 与 UA 无关、
        // .NET 内无法绕开（curl 同链路 200、.NET 改 UA/HTTP 版本/Referer 仍 403 实锤）。
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        return client;
    }

    public EngineContext(DownloadOptions options, string tempFolder)
    {
        Options = options;
        TempFolder = tempFolder;
    }

    public DownloadOptions Options { get; }
    public string TempFolder { get; }
    public HttpClient Client => SharedClient;
    public SpeedGate SpeedGate { get; } = new();
    public long TotalBytesDone;
    public long CurrentSpeed;
    public long DynamicSpeedFloor;
    public long ActiveChunks;

    public void OnChunkExited() => Interlocked.Decrement(ref ActiveChunks);

    /// <summary>带超时的一次流读取：超时视为无数据。</summary>
    /// <remarks>
    /// 不能只给 ReadAsync 传一个 CancelAfter 的令牌：连接经代理/中间设备挂死时，
    /// 挂起的 socket 读取不会立刻响应取消（实测 10s 的停滞超时 58s 后才浮现，期间进度完全冻结，
    /// 表现为「卡在某个百分比不动」）。改用 WaitAsync 强制在超时时刻返回，
    /// 把善后交给调用方 Dispose 连接（那会真正关掉 socket）。
    /// </remarks>
    public async Task<int> ReadWithTimeout(Stream stream, byte[] buffer, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        var readTask = stream.ReadAsync(buffer, cancellationToken).AsTask();
        try
        {
            return await readTask.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMilliseconds)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // 被放弃的读取稍后会以异常结束，后台吞掉，避免未观察异常
            _ = readTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException("操作超时，无数据。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("操作超时，无数据。");
        }
    }
}

/// <summary>
/// 按主机限制「同时在跑的分块数」。
/// <para/>BMCLAPI 这类镜像会按请求频率/并发限速：一个 3.7MB 文件被切成 14 个分块同时压过去，
/// 立刻回 429，随后所有分块一起失败 → 表现为 0 B/s 卡死（真机 NeoForge 安装实测）。
/// 限并发后仍是多连接并行，但不会触发限速，实际吞吐反而更高。
/// </summary>
internal static class HostConcurrency
{
    private static readonly ConcurrentDictionary<string, int> Counts = new(StringComparer.OrdinalIgnoreCase);

    public static string? KeyOf(string url)
    {
        try { return new Uri(url).Host; } catch { return null; }
    }

    public static bool TryEnter(string? host, int limit)
    {
        if (string.IsNullOrEmpty(host) || limit <= 0) return true;
        var current = Counts.AddOrUpdate(host, 1, static (_, v) => v + 1);
        if (current > limit)
        {
            Counts.AddOrUpdate(host, 0, static (_, v) => Math.Max(0, v - 1));
            return false;
        }
        return true;
    }

    public static void Exit(string? host)
    {
        if (string.IsNullOrEmpty(host)) return;
        Counts.AddOrUpdate(host, 0, static (_, v) => Math.Max(0, v - 1));
    }
}

/// <summary>
/// 按主机限制「发起新连接」的频率（对应 VB 对 BMCLAPI 的 Thread.Sleep(100)）。
/// <para/>非阻塞实现：未到间隔时直接放弃本轮调度，避免拖慢整个调度循环；
/// 跨引擎实例共享同一主机的时间表，防止大量小文件同时爆发请求触发服务器限速。
/// 另支持「失败惩罚」：连接失败后对该主机冷却一段时间（对所有主机生效，不限关键字），
/// 防止失败即重拉把服务器打出 429 后陷入请求风暴。
/// </summary>
internal static class HostStartThrottle
{
    private sealed class Gate
    {
        public long NextAllowed;
    }

    private static readonly ConcurrentDictionary<string, Gate> Gates = new(StringComparer.OrdinalIgnoreCase);

    private static Gate? GetGate(string url)
    {
        try { return Gates.GetOrAdd(new Uri(url).Host, static _ => new Gate()); }
        catch { return null; }
    }

    /// <summary>
    /// 失败惩罚：冷却该主机一段时间（与限频取更晚者），期间所有新连接一律拒绝。
    /// </summary>
    public static void Penalize(string url, long cooldownMilliseconds)
    {
        if (cooldownMilliseconds <= 0) return;
        var gate = GetGate(url);
        if (gate is null) return;
        lock (gate)
        {
            var until = Environment.TickCount64 + cooldownMilliseconds;
            if (until > gate.NextAllowed) gate.NextAllowed = until;
        }
    }

    public static bool TryReserve(string url, DownloadOptions options)
    {
        var gate = GetGate(url);
        if (gate is null) return true;
        lock (gate)
        {
            var now = Environment.TickCount64;
            // 惩罚 / 限频共用时间表：未到允许时刻一律拒绝（惩罚对所有主机生效）
            if (now < gate.NextAllowed) return false;
            if (options.HostStartMinIntervalMilliseconds > 0 && options.HostStartThrottleKeywords.Count > 0 &&
                options.HostStartThrottleKeywords.Any(k => url.Contains(k, StringComparison.OrdinalIgnoreCase)))
                gate.NextAllowed = now + options.HostStartMinIntervalMilliseconds;
            return true;
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
