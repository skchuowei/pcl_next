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
    /// <summary>连续失败计数；一旦有成功的数据包即清零。</summary>
    public int FailCount;
    /// <summary>该源是否已被禁用。</summary>
    public bool IsFailed;
    /// <summary>导致该源被禁用的异常。</summary>
    public Exception? Error;

    /// <summary>若该下载源正在进行强制单线程下载，标记这个唯一的分块。</summary>
    internal object? SingleChunk;

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
}

/// <summary>
/// 下载引擎选项（与 VB 侧 NetManager 的设置项对齐）。
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>全局最大并发下载数。对应 VB 的 NetTaskThreadLimit（ToolDownloadThread + 1）。</summary>
    public int MaxConcurrentChunks { get; init; } = 33;

    /// <summary>速度下限（Byte/s）：低于该值时为进行中的文件追加下载分块。默认 256K/s。</summary>
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

    /// <summary>测速/调度轮询间隔。</summary>
    public int TickMilliseconds { get; init; } = 100;

    /// <summary>连接超时上限（毫秒）。单个连接建立超时 = min(max(平均首包时间,15s) × (1+源失败数), 该值)。</summary>
    public int ConnectTimeoutMilliseconds { get; init; } = 30000;

    /// <summary>调试用：每个连接随机延迟，模拟不稳定网络。</summary>
    public int DebugMaxDelayMilliseconds { get; init; } = 0;

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
