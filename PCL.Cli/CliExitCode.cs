namespace PCL.Cli;

/// <summary>
/// CLI 退出码（《计划-内核与GUI拆分（CLI）》§5.1）。**退出码即结论**，脚本与 CI 只认这个。
/// </summary>
internal enum CliExitCode
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>网络失败：连不上、超时、全部下载源不可用。</summary>
    Network = 1,

    /// <summary>校验失败：大小或哈希与预期不符。</summary>
    Verify = 2,

    /// <summary>参数错误：缺参数、值非法、文件不存在。</summary>
    Argument = 3,

    /// <summary>MC 下载被部署期策略禁用（合规红线，不提供绕过开关）。</summary>
    PolicyBlocked = 4,

    /// <summary>资源站不可用且无法降级。</summary>
    SourceUnavailable = 5,

    /// <summary>未预期的内部错误（含未捕获异常）。</summary>
    Internal = 6,

    /// <summary>用户中断（Ctrl+C）。</summary>
    Canceled = 130,
}

/// <summary>
/// 参数用法错误。抛出后由 <see cref="Program"/> 统一转成退出码 <see cref="CliExitCode.Argument"/>。
/// </summary>
internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message) { }
}
