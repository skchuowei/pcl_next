namespace PCL.Download;

/// <summary>
/// 下载文件的状态（与 VB 侧 NetState 对齐）。
/// </summary>
public enum DownloadFileState
{
    /// <summary>尚未进行已存在检查。</summary>
    WaitingToCheck = -1,
    /// <summary>尚未开始。</summary>
    WaitingToDownload = 0,
    /// <summary>正在连接，尚未获取文件大小。</summary>
    Connecting = 1,
    /// <summary>已连接，正在尝试接收首个数据包。</summary>
    Reading = 2,
    /// <summary>正在下载。</summary>
    Downloading = 3,
    /// <summary>正在合并文件。</summary>
    Merging = 4,
    /// <summary>已完成。</summary>
    Finished = 5,
    /// <summary>已失败或中断。</summary>
    Canceled = 6,
}

/// <summary>
/// 下载源。
/// </summary>
public sealed class DownloadSource
{
    public DownloadSource(int id, string url)
    {
        Id = id;
        Url = url;
    }

    public int Id { get; }
    public string Url { get; }
    /// <summary>连续失败计数；一旦有成功的数据包即清零。驱动退避与超时放宽。</summary>
    public int FailCount;
    /// <summary>
    /// 计入「判死」的失败次数：限速类失败（429/503/镜像 403）不计入。
    /// 与 <see cref="FailCount"/> 分开，是为了既能对限速保持指数退避（防请求风暴），
    /// 又不会因为「被限速几次」就把唯一可用的源禁用掉。
    /// </summary>
    public int HardFailCount;
    /// <summary>
    /// 连续「零进展」失败次数：分块失败时一个字节都没收到。
    /// 曾经送达过数据的源不会命中「从未送达」的死源规则，靠这个计数快速降级
    /// （官方源能建连却不发数据时，原实现要攒满 MaxConcurrentChunks+2 次 ≈ 十几分钟才禁用）。
    /// </summary>
    public int ZeroProgressFails;
    /// <summary>首包对冲胜出次数（用于后续优先选用更快的源）。</summary>
    public int HedgeWins;
    /// <summary>首包对冲落败次数。</summary>
    public int HedgeLosses;
    /// <summary>该源累计成功接收的字节数；始终为 0 表示从未送达过任何数据（用于死源快速禁用）。</summary>
    public long DeliveredBytes;
    /// <summary>
    /// 该源最近一次尝试使用的连接/读取超时（毫秒）。
    /// <para/>用途：文件级退避的基准应与「源有多不健康」成正比。
    /// 快判超时（从未送达字节的源，通常只有几百毫秒）本身就意味着每次失败已经耗掉整个超时，
    /// 天然是低频的，再叠加固定 1500ms 起的指数退避就是过度惩罚：
    /// 死源要在少数几次内被剔除，而不是每个死源各自享受 ×8 的退避（实测 16s → 107s）。
    /// 慢源的超时是秒级，基准自然更大，退避力度也就更强。
    /// </summary>
    public int LastAttemptTimeoutMilliseconds;
    /// <summary>
    /// 连续收到 403 的次数（仅 BMCLAPI 这类「403 = 限流」的镜像）。限流可以容忍几次，
    /// 但一直 403 等价于死源：不设上限会让任务永久重试、既不换源也不失败。
    /// </summary>
    public int RateLimit403Count;
    /// <summary>该源是否已被禁用。</summary>
    public bool IsFailed;
    /// <summary>
    /// 该源不支持 Range（返回 200 全量或 416）。此时不能禁用它——它往往仍是唯一能用的镜像，
    /// 只能改为「单线程整文件下载」；此前直接禁用会导致：BMCLAPI 不支持 Range → 被禁用 →
    /// 只剩被墙的官方 maven → 整单 download_failed（真机 NeoForge installer 下载失败）。
    /// </summary>
    public bool RangeUnsupported;
    /// <summary>导致该源被禁用的异常。</summary>
    public Exception? Error;

    /// <summary>若该下载源正在进行强制单线程下载，标记这个唯一的分块。</summary>
    internal object? SingleChunk;

    /// <summary>
    /// 该源最近观测到的有效吞吐（EWMA，Byte/s）；负数表示尚无样本。
    /// <para/>只用于「多源并行」时给分块挑源：慢源排到最后，快源优先分到更多分块。
    /// 不参与判死（判死仍走失败计数），也没有任何 UI 展示用途。
    /// </summary>
    internal double SpeedEwma = -1;

    /// <summary>已用于速度采样的字节累计：样本太小时不采信，避免一次短分块把源判成「慢源」。</summary>
    internal long SpeedSampleBytes;

    /// <summary>保护 <see cref="SpeedEwma"/> / <see cref="SpeedSampleBytes"/> 的读写（多分块并行收尾时会并发更新）。</summary>
    internal readonly object SpeedLock = new();

    public override string ToString() => Url;
}

/// <summary>
/// 文件校验规则（与 MeloongCore FileChecker 语义对齐）。
/// </summary>
public sealed class FileCheckRule
{
    /// <summary>文件的准确大小。不检查则为 -1。</summary>
    public long ActualSize { get; init; } = -1;

    /// <summary>文件的最小大小。不检查则为 -1。</summary>
    public long MinSize { get; init; } = -1;

    /// <summary>文件的 MD5/SHA1/SHA256/SHA512 十六进制串，按长度自动判断。不检查则为 null。</summary>
    public string? Hash { get; init; }

    /// <summary>是否要求为 JSON 文件。</summary>
    public bool IsJson { get; init; }

    /// <summary>
    /// 校验文件。成功返回 null，失败返回错误描述（不以句号结尾）。不抛异常。
    /// </summary>
    public string? Validate(string localPath)
    {
        try
        {
            var info = new FileInfo(localPath);
            if (!info.Exists) return "文件不存在：" + localPath;
            long size = info.Length;
            if (ActualSize >= 0 && ActualSize != size)
                return $"文件大小应为 {ActualSize} B，实际为 {size} B";
            if (MinSize >= 0 && MinSize > size)
                return $"文件大小应至少为 {MinSize} B，实际为 {size} B";
            var hash = Hash?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(hash))
            {
                string actual = HashHelper.ComputeFileHash(localPath, hash.Length);
                if (actual != hash)
                    return $"文件 {HashHelper.NameOf(hash.Length)} 应为 {hash}，实际为 {actual}";
            }
            if (IsJson)
            {
                string content = File.ReadAllText(localPath).Trim('\r', '\n', ' ', '\t');
                if (!(content.StartsWith('{') && content.EndsWith('}')) &&
                    !(content.StartsWith('[') && content.EndsWith(']')))
                    return "返回结果并非 JSON 格式";
            }
            return null;
        }
        catch (Exception ex)
        {
            return "文件校验出错：" + ex.Message;
        }
    }
}

/// <summary>
/// 单个文件的下载请求。
/// </summary>
public sealed class DownloadFileRequest
{
    /// <summary>按优先级排列的下载源地址列表。</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>包含文件名的本地保存路径。</summary>
    public required string LocalPath { get; init; }

    /// <summary>校验规则；为 null 时跳过本地复用与校验。</summary>
    public FileCheckRule? Check { get; init; }

    /// <summary>是否模拟浏览器的 UserAgent 与 Referer。</summary>
    public bool SimulateBrowserHeaders { get; init; }

    /// <summary>
    /// 按 URL 提供自定义请求头（User-Agent / Referer / 鉴权键等）。
    /// 设置后拥有最高优先级，覆盖 SimulateBrowserHeaders 的默认行为。
    /// </summary>
    public Func<string, IEnumerable<KeyValuePair<string, string>>>? HeaderProvider { get; init; }
}

/// <summary>
/// 跨下载引擎实例共享的并发分块预算（对应 VB 的 NetTaskThreadCount 全局上限）。
/// <para/>VB 侧为每个文件单独调用一次引擎时，各引擎自带的 MaxConcurrentChunks 只能约束单文件；
/// 通过共享同一预算实例，可把「全局同时进行的下载分块数」压回 NetTaskThreadLimit，
/// 避免大量小文件同时发起连接触发服务器限速。
/// </summary>
public sealed class ConcurrencyBudget
{
    private int _active;
    private int _limit;

    public ConcurrencyBudget(int limit) => _limit = Math.Max(1, limit);

    /// <summary>全局并发上限。可在运行期按设置刷新。</summary>
    public int Limit
    {
        get => Volatile.Read(ref _limit);
        set => Volatile.Write(ref _limit, Math.Max(1, value));
    }

    /// <summary>当前占用的分块数。</summary>
    public int Active => Volatile.Read(ref _active);

    /// <summary>尝试占用一个并发名额；预算耗尽时立即返回 false。</summary>
    public bool TryAcquire()
    {
        while (true)
        {
            var current = Volatile.Read(ref _active);
            if (current >= Volatile.Read(ref _limit)) return false;
            if (Interlocked.CompareExchange(ref _active, current + 1, current) == current) return true;
        }
    }

    /// <summary>归还一个并发名额。</summary>
    public void Release() => Interlocked.Decrement(ref _active);
}

/// <summary>
/// 下载引擎选项（与 VB 侧 NetManager 的设置项对齐）。
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>全局最大并发下载数。对应 VB 的 NetTaskThreadLimit（ToolDownloadThread + 1）。</summary>
    public int MaxConcurrentChunks { get; init; } = 33;

    /// <summary>
    /// 跨引擎实例共享的并发预算；为 null 时仅受 <see cref="MaxConcurrentChunks"/> 约束。
    /// VB 侧为每个文件各起一个引擎时必须传入同一实例，否则全局并发不受控。
    /// </summary>
    public ConcurrencyBudget? SharedBudget { get; init; }

    /// <summary>
    /// 速度下限（Byte/s）：当前速度低于该值时为进行中的文件追加下载分块。默认 256K/s（与 VB 对齐）。
    /// <para/>补充说明（2026-11-22 实测）：调度首轮 <c>CurrentSpeed</c> 为 0，必然低于该值，
    /// 因此单文件一开跑就会连续追加数个分块，随后速度超过阈值才收敛；配合
    /// <see cref="MaxChunksPerHostUnrestricted"/> 放宽后，单文件 28 MB 实测 1.06 → 3.05 MB/s。
    /// 若把阈值抬到 1 MB/s 反而更慢（多分块压满主机名额后互相排队），故保持 256K。
    /// </summary>
    public long SpeedLimitLow { get; init; } = 256 * 1024;

    /// <summary>速度上限（Byte/s）；小于等于 0 表示不限速。</summary>
    public long SpeedLimitHigh { get; init; } = -1;

    /// <summary>文件分割下限（Byte）：小于该大小的文件不分割。默认 1MB。</summary>
    public long MinSplitSizeBytes { get; init; } = 1024 * 1024;

    /// <summary>分块追加的最小剩余量（Byte）。默认 256KB。</summary>
    public long PieceLimitBytes { get; init; } = 256 * 1024;

    /// <summary>是否启用本地相同文件复用（跨版本复用）。</summary>
    public bool EnableLocalReuse { get; init; } = true;

    /// <summary>
    /// 本地复用的检索根目录（如 .minecraft 目录列表）。
    /// 需要目标路径含 assets/libraries/versions 等类型目录标记才生效。
    /// </summary>
    public IReadOnlyList<string> LocalReuseFolders { get; init; } = [];

    /// <summary>
    /// 不允许分割多线程的地址关键字（与 VB 的内置列表一致）。
    /// </summary>
    public IReadOnlyList<string> NoSplitHostKeywords { get; init; } =
        ["pcl2-server", "meloong.com", "bmclapi", "github.com", "optifine.net", "momot.rs"];

    /// <summary>
    /// 首包对冲延迟（毫秒）：文件首块开始下载后，若主下载源在该时间内仍未返回任何数据，
    /// 立即同时向另一个可用源发起同一区间请求。谁先吐出首包谁继续下载，另一个连接立刻断开。
    /// <para/>真机场景：maven.fabricmc.net 能建立连接却始终不发数据，原实现要干等 30 秒超时才换源；
    /// 对冲后约 1 秒即可切到 BMCLAPI。仅作用于文件首块，整体请求量不放大。
    /// 0 表示关闭对冲（退回纯顺序换源）。
    /// </summary>
    public int HedgeDelayMilliseconds { get; init; } = 500;

    /// <summary>测速/调度轮询间隔。</summary>
    public int TickMilliseconds { get; init; } = 50;

    /// <summary>
    /// 需要限制「发起新连接」频率的主机关键字（防止请求过密触发服务器限速）。
    /// 与 VB 对齐默认仅限 BMCLAPI；空列表表示不限频。
    /// </summary>
    public IReadOnlyList<string> HostStartThrottleKeywords { get; init; } = ["bmclapi"];

    /// <summary>
    /// 受限主机两次发起连接间的最小间隔（毫秒），非阻塞式（未到间隔则本调度轮跳过）。
    /// 0 或负数表示不限频。默认 50ms ≈ 20 次/秒，与 VB 的 Sleep(100) × 双调度线程相当。
    /// </summary>
    public int HostStartMinIntervalMilliseconds { get; init; } = 50;

    /// <summary>
    /// 慢速下载源判定阈值（Byte/s）：分块持续传输超过 <see cref="SlowSourceRotateAfterMilliseconds"/>
    /// 且平均速度低于该值时，保留断点自动换其他下载源续传。小于等于 0 关闭该特性。
    /// </summary>
    public long SlowSourceRotateSpeedBytesPerSecond { get; init; } = 64 * 1024;

    /// <summary>慢速下载源判定所需的最短持续传输时间（毫秒）。默认 5000。</summary>
    public int SlowSourceRotateAfterMilliseconds { get; init; } = 5000;

    /// <summary>
    /// 「受限主机」允许同时进行的分块数上限。BMCLAPI 等镜像会按并发/频率限速，
    /// 单文件被切成十几个分块同时压过去会立刻回 429（随后全线失败 → 0 B/s 卡死）。
    /// 只对 <see cref="HostConcurrencyLimitedKeywords"/> 命中的主机生效；默认 4，0 或负数表示不限制。
    /// </summary>
    public int MaxChunksPerHost { get; init; } = 4;

    /// <summary>
    /// 受限主机关键字：URL 命中者按 <see cref="MaxChunksPerHost"/> 限并发，其余主机按
    /// <see cref="MaxChunksPerHostUnrestricted"/>。
    /// <para/>默认只锁 BMCLAPI 与自建服务：它们按请求频率/并发限速，压太狠会 429。
    /// 官方 CDN（cdn.modrinth.com / forgecdn / objects.githubusercontent.com）实测对并发不敏感，
    /// 归入「非受限」以吃满带宽。
    /// </summary>
    public IReadOnlyList<string> HostConcurrencyLimitedKeywords { get; init; } =
        ["bmclapi", "pcl2-server", "meloong.com"];

    /// <summary>
    /// 非受限主机（官方 CDN / 第三方镜像）同一主机允许同时进行的分块数上限。
    /// <para/>默认 16：实测国内到 cdn.modrinth.com 的 HTTP/1.1 多连接吞吐随并发线性增长
    /// （4 连接 1.5 MB/s、8 连接 3.6 MB/s、16 连接 6.9 MB/s），此前全局锁 4 条连接是整合包下载
    /// 长期只有 400~500 KB/s 的直接原因。真正的全局上限仍由 <see cref="SharedBudget"/> 与
    /// <see cref="MaxConcurrentChunks"/> 控制。
    /// </summary>
    public int MaxChunksPerHostUnrestricted { get; init; } = 16;

    /// <summary>
    /// 单个文件允许同时进行的分块数上限，防止一个大文件把主机并发名额全部吃光，
    /// 把其余小文件挤成串行。默认 12：留 4 个名额给其它文件，同时单文件也能吃到
    /// 接近 <see cref="MaxChunksPerHostUnrestricted"/> 的吞吐。0 或负数表示只受
    /// <see cref="MaxConcurrentChunks"/> 约束。
    /// </summary>
    public int MaxChunksPerFile { get; init; } = 12;

    /// <summary>
    /// 多源并行：同一文件的不同分块可以分配给不同下载源同时下载。
    /// <para/>关闭后回到旧行为（首包竞速赢家独占追加分块）。默认开启。
    /// </para>
    /// </summary>
    public bool MultiSourceParallel { get; init; } = true;

    /// <summary>
    /// 多源并行时，同一文件同时参与的源数上限。0 或负数表示不限制（用全部可用源）。
    /// <para/>默认 4：再多会把连接摊得过散，单源分不到足够分块，反而更容易触发镜像限流。
    /// </para>
    /// </summary>
    public int MultiSourceMaxSources { get; init; } = 4;

    /// <summary>
    /// 慢源降级阈值（Byte/s）：源的实测吞吐低于该值且样本可信时，多源调度把它的优先级排到最后。
    /// <para/>默认 48 KB/s。只影响新分块的分配顺序，已有分块不会被强行中断（慢速换源另有独立机制）。
    /// 小于等于 0 关闭慢源降级。
    /// </para>
    /// </summary>
    public long MultiSourceSlowSourceBytesPerSecond { get; init; } = 48 * 1024;

    /// <summary>连接超时上限（毫秒）。单个连接建立超时 = min(max(平均首包时间,下限) × (1+源失败数), 该值)。</summary>
    public int ConnectTimeoutMilliseconds { get; init; } = 30000;
    /// <summary>连接超时下限（毫秒）。默认 15000 与 VB 对齐；单测中可调小以加速失败。</summary>
    public int MinConnectTimeoutMilliseconds { get; init; } = 15000;

    /// <summary>
    /// 「一个字节都没送达过」的源的单次超时（毫秒）。默认 6000。
    /// <para/>这类源基本是被墙 / 建连后永不发数据的死连接。旧实现按 <see cref="MinConnectTimeoutMilliseconds"/>
    /// ×(1+失败次数) 放大到 15~30 秒，收尾阶段只剩这类源时，每个文件都要白等半分钟，
    /// 表现为进度长时间 0 B/s（真机 NeoForge/Fabric 安装尾段卡住不动的主因）。
    /// </summary>
    public int NeverDeliveredConnectTimeoutMilliseconds { get; init; } = 6000;

    /// <summary>
    /// 读取停滞判定（毫秒）：连接已建立但超过该时长未收到任何数据即断开重试。
    /// 对应下载提速插件的 BlockTimeoutMs（balanced 10s / aggressive 8s / conservative 20s）。
    /// 此前复用连接超时（15~30s），真机上表现为死连接要挂半分钟才动，UI 长时间 0 B/s。
    /// </summary>
    public int ReadStallTimeoutMilliseconds { get; init; } = 10000;

    /// <summary>
    /// 分块失败后的基础重试冷却（毫秒），按失败次数指数退避（×1/×2/×4/×8 封顶）。
    /// 修复「失败分块立即重新拉起 → 每 20ms 一次请求打爆服务器触发 429 → 全线 0 B/s」的重试风暴
    /// （真机 NeoForge 安装卡 2% 的根因）。0 表示关闭退避（仅保留主机惩罚）。
    /// </summary>
    public int RetryBackoffMilliseconds { get; init; } = 1500;

    /// <summary>
    /// 收到 429/503（服务器限速）后的主机冷却时长（毫秒），期间不再对该主机发起任何新连接。
    /// 对应下载提速插件「限速时宁可慢不可打爆」的原则。
    /// </summary>
    public int RateLimitCooldownMilliseconds { get; init; } = 5000;

    /// <summary>
    /// 调试用：每个连接随机延迟，模拟不稳定网络。</summary>
    public int DebugMaxDelayMilliseconds { get; init; } = 0;

    /// <summary>
    /// 实验性：校验失败时**只补下缺失区间**，而不是整文件重下。默认关闭（与既有线上行为一致）。
    /// <para/>仅在失败原因能定位到具体区间时生效（分块覆盖出现缺口/重复块，且重叠导致
    /// 「有效字节之和」虚高到 FileSize 从而提前进入合并）。此时引擎会丢弃重复分块、保留已下好的
    /// <c>.tmp</c>，只把缺失区间登记为待下载块。哈希/大小不符这类无法定位的失败，仍走整文件重下。
    /// <para/>dev 通道由宿主层默认打开（见 VB 侧「调试与实验 → 校验失败时只重下缺失区间」）。
    /// </summary>
    public bool RedownloadGapsOnVerifyFailure { get; init; }

    /// <summary>日志回调（信息级）。</summary>
    public Action<string>? Log { get; init; }

    /// <summary>日志回调（警告级）。</summary>
    public Action<Exception, string>? LogWarn { get; init; }
}

/// <summary>
/// 全局下载进度快照。
/// </summary>
public sealed class DownloadProgress
{
    /// <summary>总进度（0~1）。</summary>
    public required double OverallProgress { get; init; }
    /// <summary>已下载字节数。</summary>
    public required long TotalBytesDone { get; init; }
    /// <summary>预计总字节数（未知大小的文件不计入；尚有未知时为 -1）。</summary>
    public long TotalBytesExpected { get; init; } = -1;
    /// <summary>全局估计速度（Byte/s）。</summary>
    public required long Speed { get; init; }
    /// <summary>活动中的下载数。</summary>
    public required int ActiveChunks { get; init; }
}

/// <summary>
/// 单个文件的下载结果。
/// </summary>
public sealed class DownloadFileReport
{
    public required string LocalPath { get; init; }
    /// <summary>最终状态。</summary>
    public required DownloadFileState State { get; init; }
    /// <summary>是否由本地文件直接复制完成（含目标文件已存在）。</summary>
    public bool IsLocalReuse { get; init; }
    /// <summary>下载的字节数（本地复用时为 0）。</summary>
    public long BytesDownloaded { get; init; }
}

/// <summary>
/// 一次多文件下载任务的汇总。
/// </summary>
public sealed class DownloadReport
{
    public required IReadOnlyList<DownloadFileReport> Files { get; init; }
    public required long TotalBytesDownloaded { get; init; }
    public required TimeSpan Elapsed { get; init; }
}
