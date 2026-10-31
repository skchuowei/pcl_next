namespace PCL.Download;

/// <summary>
/// 该下载源不支持分段下载（返回内容与 Range 请求不匹配，或返回 416）。
/// </summary>
public sealed class RangeNotSupportedException : HttpRequestException
{
    public RangeNotSupportedException(string message) : base(message) { }
}

/// <summary>
/// 多文件下载整体失败，聚合了各文件、各下载源的错误。
/// </summary>
public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(IReadOnlyDictionary<string, IReadOnlyList<Exception>> errorsByFile)
        : base(BuildMessage(errorsByFile))
    {
        ErrorsByFile = errorsByFile;
    }

    /// <summary>本地路径 → 该文件各下载源的错误。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Exception>> ErrorsByFile { get; }

    private static string BuildMessage(IReadOnlyDictionary<string, IReadOnlyList<Exception>> errorsByFile)
    {
        var lines = new List<string> { "文件下载失败：" };
        foreach (var (path, errors) in errorsByFile.Take(5))
        {
            lines.Add("文件：" + path);
            foreach (var error in errors.Take(6))
                lines.Add("  - " + error.Message);
        }
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// C# 下载引擎的对外契约（对应 VB 侧 LoaderDownload + NetManager 的组合能力）。
/// </summary>
public interface IDownloadEngine
{
    /// <summary>
    /// 下载一批文件。任一文件失败则抛出 <see cref="DownloadFailedException"/>；全部成功则返回汇总。
    /// 本地复用、断点分片、多源 failover、限速与取消均由实现负责。
    /// </summary>
    Task<DownloadReport> DownloadAsync(
        IEnumerable<DownloadFileRequest> files,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
