using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Cli;

/// <summary>
/// CLI 输出器（计划 §5.2）：
/// <list type="bullet">
/// <item><b>stdout</b>：只输出 JSON Lines，一行一个事件，末行必须是 <c>result</c>；</item>
/// <item><b>stderr</b>：人读进度（速率、源切换、分片数），供人和 CI 日志直接看；</item>
/// <item><b>日志文件</b>：<c>--workdir</c> 下的 <c>cli-&lt;cmd&gt;-&lt;ts&gt;.log</c>，B1 的分片/续传/校验证据以它为准。</item>
/// </list>
/// </summary>
internal sealed class CliOutput : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly object _gate = new();
    private readonly StreamWriter? _log;
    private readonly bool _quiet;

    /// <summary>stderr 上一行是否是不带换行的进度行（需要在写整行前先清掉）。</summary>
    private bool _pendingProgressLine;

    public CliOutput(string command, string? logPath, bool quiet)
    {
        Command = command;
        _quiet = quiet;
        LogPath = logPath;

        if (logPath is null) return;
        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _log = new StreamWriter(
            new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>当前命令名，写入每条事件的 <c>cmd</c> 字段。</summary>
    public string Command { get; }

    /// <summary>日志文件路径；未启用时为 null。</summary>
    public string? LogPath { get; }

    // ── stdout：JSON Lines ───────────────────────────────────────────────────

    /// <summary>向 stdout 写一条结构化事件。</summary>
    public void Event(string ev, params (string Key, object? Value)[] fields)
    {
        var node = new JsonObject
        {
            ["ev"] = ev,
            ["cmd"] = Command,
        };
        foreach (var (key, value) in fields)
        {
            if (value is null) continue;
            node[key] = JsonSerializer.SerializeToNode(value, JsonOptions);
        }
        WriteStdout(node.ToJsonString(JsonOptions));
    }

    /// <summary>写 <c>notice</c> 事件（降级提示等不影响结论的信息）。</summary>
    public void Notice(string message) => Event("notice", ("message", message));

    /// <summary>写 <c>error</c> 事件。</summary>
    public void Error(string code, string message, params (string Key, object? Value)[] extra)
    {
        var fields = new List<(string, object?)> { ("code", code), ("message", message) };
        fields.AddRange(extra);
        Event("error", fields.ToArray());
    }

    /// <summary>写最终 <c>result</c> 事件；退出码由调用方决定，但两者必须一致。</summary>
    public void Result(bool ok, params (string Key, object? Value)[] fields)
    {
        var list = new List<(string, object?)> { ("ok", ok) };
        list.AddRange(fields);
        Event("result", list.ToArray());
    }

    /// <summary>批量条目事件（资源搜索 / 版本枚举用）。</summary>
    public void Item(params (string Key, object? Value)[] fields) => Event("item", fields);

    // ── stderr：人读 ─────────────────────────────────────────────────────────

    /// <summary>写一整行人读信息（stderr）。</summary>
    public void Say(string message, bool alwaysLog = true)
    {
        if (alwaysLog) WriteLog("INFO", message);
        if (_quiet) return;
        lock (_gate)
        {
            ClearProgressLineLocked();
            Console.Error.WriteLine(message);
        }
    }

    /// <summary>写一行覆盖式进度（stderr，不带换行）。</summary>
    public void ProgressLine(string message)
    {
        if (_quiet) return;
        lock (_gate)
        {
            Console.Error.Write('\r');
            Console.Error.Write(message.PadRight(LastProgressWidth));
            LastProgressWidth = Math.Max(LastProgressWidth, message.Length);
            _pendingProgressLine = true;
        }
    }

    /// <summary>正常换行收尾进度行，避免后续输出被覆盖。</summary>
    public void EndProgressLine()
    {
        if (_quiet) return;
        lock (_gate)
        {
            if (!_pendingProgressLine) return;
            Console.Error.WriteLine();
            _pendingProgressLine = false;
            LastProgressWidth = 0;
        }
    }

    private int LastProgressWidth { get; set; }

    private void ClearProgressLineLocked()
    {
        if (!_pendingProgressLine) return;
        Console.Error.Write('\r');
        Console.Error.Write(new string(' ', LastProgressWidth));
        Console.Error.Write('\r');
        _pendingProgressLine = false;
        LastProgressWidth = 0;
    }

    // ── 日志文件 ─────────────────────────────────────────────────────────────

    /// <summary>引擎 Info 日志落点（挂到 <c>DownloadOptions.Log</c>）。</summary>
    public void EngineInfo(string message)
    {
        WriteLog("ENG ", message);

        // 引擎日志里出现分片/换源/续传等关键行时，同步给 stderr，便于人直接看到
        if (!_quiet) ProgressLine("[engine] " + message);
    }

    /// <summary>引擎 Warn 日志落点（挂到 <c>DownloadOptions.LogWarn</c>）。</summary>
    public void EngineWarn(string message, Exception? ex = null)
    {
        WriteLog("WARN", ex is null ? message : $"{message}｜{ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>直接落一行日志（不写 stderr）。</summary>
    public void WriteLog(string level, string message)
    {
        if (_log is null) return;
        lock (_gate)
            _log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
    }

    /// <summary>内核 Info 日志的落点（挂到 <c>DownloadLog.InfoSink</c>，只落盘不外溢 stdout）。</summary>
    public void WriteLogInfo(string message) => WriteLog("INFO", message);

    /// <summary>内核 Warn 日志的落点（挂到 <c>DownloadLog.WarnSink</c>）。</summary>
    public void WriteLogWarn(string message) => WriteLog("WARN", message);

    private void WriteStdout(string line)
    {
        lock (_gate)
        {
            // stdout 必须是干净的 JSON Lines。若 stderr 的进度行正在原地刷新，
            // 先换行把它「钉住」，避免两个流在同一终端行互相覆盖。
            if (_pendingProgressLine)
            {
                Console.Error.WriteLine();
                _pendingProgressLine = false;
                LastProgressWidth = 0;
            }

            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    public void Dispose()
    {
        EndProgressLine();
        lock (_gate)
        {
            _log?.Flush();
            _log?.Dispose();
        }
    }
}
