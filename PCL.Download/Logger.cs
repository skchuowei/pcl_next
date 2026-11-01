namespace PCL.Download;

/// <summary>
/// 极简日志门面（平移自 VB 的 Logger）。默认写入控制台，宿主可经 Sink 重定向到自己的日志系统。
/// </summary>
public static class Logger
{
    /// <summary>Info 级别日志的输出目标。为 null 时写入 Console.Out。</summary>
    public static Action<string>? InfoSink;
    /// <summary>Warn 级别日志的输出目标。为 null 时写入 Console.Error。</summary>
    public static Action<string>? WarnSink;

    public static void Info(string message)
        => (InfoSink ?? Console.Out.WriteLine)(message);

    public static void Warn(string message)
        => (WarnSink ?? Console.Error.WriteLine)(message);

    public static void Warn(Exception ex, string message)
        => Warn($"{message}：{ex}");
}
