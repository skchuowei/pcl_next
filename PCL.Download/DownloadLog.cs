namespace PCL.Download;

/// <summary>
/// 极简日志门面（平移自 VB 的 Logger）。默认写入控制台，宿主可经 Sink 重定向到自己的日志系统。
/// <para>
/// 命名为 <c>DownloadLog</c> 而非 <c>Logger</c>：VB 主工程以 <c>Imports PCL.Download</c> 引入本程序集，
/// 若此处也叫 <c>Logger</c>，会抢占 VB 侧的 <c>MeloongCore.Logger</c>——<c>NetInterop.vb</c> 里未限定的
/// <c>Logger.Info</c> 会静默改写到本类（写进 Console 而非 Log1.txt），导致引擎日志在用户日志里消失。
/// 改名从源头消除歧义，VB 侧无需逐个加命名空间限定。
/// </para>
/// </summary>
public static class DownloadLog
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
