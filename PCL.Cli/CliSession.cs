using PCL.Download;

namespace PCL.Cli;

/// <summary>
/// 一次 CLI 调用的公共上下文：输出器、策略、工作目录、下载选项与取消令牌。
/// 所有命令共用同一份，避免各命令各写一套参数与日志。
/// </summary>
internal sealed class CliSession : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private CliSession(CliArguments args, CliOutput output, CliPolicy policy, string workDir,
        McMirrorPreference mirror, int maxConcurrency, long speedLimit)
    {
        Args = args;
        Output = output;
        Policy = policy;
        WorkDir = workDir;
        Mirror = mirror;
        MaxConcurrency = maxConcurrency;
        SpeedLimit = speedLimit;

        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public CliArguments Args { get; }
    public CliOutput Output { get; }
    public CliPolicy Policy { get; }
    public string WorkDir { get; }
    public McMirrorPreference Mirror { get; }
    public int MaxConcurrency { get; }
    public long SpeedLimit { get; }
    public CancellationToken Token => _cts.Token;

    /// <summary>创建工作会话：解析全局参数、建目录、开日志、读策略。</summary>
    public static CliSession Create(string command, CliArguments args)
    {
        var workDir = args.Get("workdir") ?? Path.Combine(Path.GetTempPath(), "pclcli");
        workDir = Path.GetFullPath(workDir);
        Directory.CreateDirectory(workDir);

        var logPath = args.Get("log");
        if (logPath is null && !args.GetBool("no-log"))
            logPath = Path.Combine(workDir, $"cli-{command}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var output = new CliOutput(command, logPath is null ? null : Path.GetFullPath(logPath),
            args.GetBool("quiet") || args.GetBool("q"));

        // 内核库（ResourceSearcher / LocalResourceScanner 等）通过静态 DownloadLog 打日志，
        // 默认写 Console.Out——那会污染 stdout 的 JSON Lines。此处一律改道到日志文件。
        DownloadLog.InfoSink = output.WriteLogInfo;
        DownloadLog.WarnSink = output.WriteLogWarn;
        var policy = CliPolicy.Resolve(args.Get("policy-file"));
        var mirror = McSource.ParsePreference(args.Get("mirror"));

        var concurrency = args.GetInt("concurrency") ?? 33;
        if (concurrency < 1 || concurrency > 256)
            throw new CliUsageException("--concurrency 需在 1~256 之间");

        var speedLimit = args.GetLong("speed-limit") ?? -1;

        return new CliSession(args, output, policy, workDir, mirror, concurrency, speedLimit);
    }

    /// <summary>把策略命中的来源打印到日志首行与 stdout（计划 §5.4：便于事后核对）。</summary>
    public void AnnouncePolicy()
    {
        Output.Event("policy",
            ("allowMcDownload", Policy.AllowMcDownload),
            ("source", Policy.Source));
        Output.Say($"MC 下载功能：{(Policy.AllowMcDownload ? "已启用" : "已禁用")}（来源：{Policy.Source}）");
    }

    /// <summary>MC 下载被禁用时的统一拒绝（退出码 4，不提供绕过开关）。</summary>
    public int RefuseByPolicy()
    {
        const string message = "MC 下载已被部署期策略禁用（DownloadPolicy.ini 或 PCL_ALLOW_MC_DOWNLOAD）";
        Output.Error("policy_blocked", message, ("source", Policy.Source));
        Output.Result(false, ("code", "policy_blocked"), ("source", Policy.Source));
        return (int)CliExitCode.PolicyBlocked;
    }

    /// <summary>构造下载引擎选项，日志接到本会话的输出器。</summary>
    public DownloadOptions CreateOptions() => new()
    {
        MaxConcurrentChunks = MaxConcurrency,
        SpeedLimitHigh = SpeedLimit,
        Log = Output.EngineInfo,
        LogWarn = (ex, message) => Output.EngineWarn(message, ex),
    };

    /// <summary>构造进度回调（节流到 ~5 次/秒，同时写 stdout 的 progress 事件与 stderr 的进度行）。</summary>
    public IProgress<DownloadProgress> CreateProgress(string label) => new ThrottledProgress(Output, label);

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        if (_cts.IsCancellationRequested)
        {
            // 第二次 Ctrl+C 直接走默认处理，避免程序卡在无法中断的等待里
            return;
        }
        Output.Say("收到中断信号，正在停止……");
        _cts.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Console.CancelKeyPress -= OnCancelKeyPress;
        _cts.Dispose();
        Output.Dispose();
    }

    /// <summary>节流进度上报：避免 stdout/stderr 被高频事件刷屏（引擎 tick 为 100ms）。</summary>
    private sealed class ThrottledProgress : IProgress<DownloadProgress>
    {
        private readonly CliOutput _output;
        private readonly string _label;
        private readonly object _gate = new();
        private DateTime _last = DateTime.MinValue;

        public ThrottledProgress(CliOutput output, string label)
        {
            _output = output;
            _label = label;
        }

        public void Report(DownloadProgress value)
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if ((now - _last).TotalMilliseconds < 200) return;
                _last = now;
            }

            _output.Event("progress",
                ("label", _label),
                ("percent", Math.Round(value.OverallProgress * 100, 1)),
                ("done", value.TotalBytesDone),
                ("total", value.TotalBytesExpected),
                ("speed", value.Speed),
                ("chunks", value.ActiveChunks));

            var total = value.TotalBytesExpected > 0
                ? FormatSize(value.TotalBytesExpected)
                : "?";
            _output.ProgressLine(
                $"[{_label}] {value.OverallProgress * 100,5:F1}%  {FormatSize(value.TotalBytesDone)}/{total}  " +
                $"{FormatSize(value.Speed)}/s  分块 {value.ActiveChunks}");
        }
    }

    /// <summary>人类可读的字节数。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "?";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:F0}{units[unit]}" : $"{value:F1}{units[unit]}";
    }

    /// <summary>把 <c>--sha1/--sha256/--md5/--sha512/--size/--min-size</c> 组装成校验规则。</summary>
    public static FileCheckRule? BuildCheck(CliArguments args)
    {
        var hash = args.Get("sha512") ?? args.Get("sha256") ?? args.Get("sha1") ?? args.Get("md5");
        if (hash is not null && hash.Length is not (32 or 40 or 64 or 128))
            throw new CliUsageException("哈希长度必须为 32/40/64/128（MD5/SHA1/SHA256/SHA512 的十六进制串）");

        var size = args.GetLong("size");
        var minSize = args.GetLong("min-size");
        if (hash is null && size is null && minSize is null) return null;
        if (hash is null && size is not null && size < 0)
            throw new CliUsageException("--size 不能为负数");

        return new FileCheckRule
        {
            Hash = hash,
            ActualSize = size ?? -1,
            MinSize = minSize ?? -1,
        };
    }
}
