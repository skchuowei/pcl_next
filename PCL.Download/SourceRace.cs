using System.Diagnostics;

namespace PCL.Download;

/// <summary>
/// 一个候选源的取数动作。
/// </summary>
/// <param name="SourceName">展示名称，例如「BMCLAPI」「Forge 官方源」。</param>
/// <param name="Url">请求地址，仅用于异常定位与日志。</param>
/// <param name="IsOfficial">命中后是否为官方实时数据。</param>
/// <param name="StartAtSeconds">相对本次获取开始的启动时刻（秒）。0 表示立即启动。</param>
/// <param name="Fetch">取数动作，返回已解析的结果；失败时抛异常。</param>
public sealed record SourceRaceCandidate<T>(string SourceName, string Url, bool IsOfficial, int StartAtSeconds,
    Func<T> Fetch);

/// <summary>
/// 竞速结果。
/// </summary>
/// <param name="Value">胜出候选返回的结果。</param>
/// <param name="SourceName">胜出候选的名称。</param>
/// <param name="IsOfficial">胜出候选是否为官方实时数据。</param>
/// <param name="ElapsedMilliseconds">从开始竞速到胜出所花的时间。</param>
public sealed record SourceRaceResult<T>(T Value, string SourceName, bool IsOfficial, long ElapsedMilliseconds);

/// <summary>
/// 候选源竞速（平移自 VB <c>ModDownload.DlSourceLoader</c> 的 <c>WaitCycle</c> 语义）。
/// <para>
/// 规则与 VB 完全一致：
/// ① 第一个候选立即启动，其余候选在各自的 <see cref="SourceRaceCandidate{T}.StartAtSeconds"/> 到点启动；
/// ② 先成功者胜出，同时完成时取排在前面的候选；
/// ③ 已启动的候选**全部失败**时立即启动下一个候选，不白等到排期时刻
/// （对应 VB 的 <c>BeforeLoadersAllFailed</c> 加速）；
/// ④ 单个候选有兜底超时，避免慢连接永久占住轮询。
/// </para>
/// <para>本类只做调度，不关心候选取的是 HTML、JSON 还是别的东西。</para>
/// </summary>
public static class SourceRace
{
    /// <summary>单源的兜底超时（秒）。候选切换由排期控制，该值只用于保证任务最终会结束。</summary>
    public const int SingleSourceTimeoutSeconds = 45;

    /// <summary>轮询间隔（毫秒）。与 VB 的 <c>Thread.Sleep(10)</c> 差异：这里 50ms，切换粒度足够。</summary>
    private const int PollMilliseconds = 50;

    /// <summary>
    /// 按排期竞速取数。
    /// </summary>
    /// <param name="candidates">候选源，按排期先后排列。</param>
    /// <param name="label">日志前缀，例如「版本列表」「OptiFine 版本列表」。</param>
    /// <param name="isCancelled">取消检查；返回 true 时放弃并返回 null（对齐 VB 取消时「静默返回、不设输出」）。</param>
    /// <returns>胜出的结果；被取消时返回 null。</returns>
    /// <exception cref="ResourceRequestException">所有候选都失败时抛出，内部异常为聚合异常。</exception>
    public static SourceRaceResult<T>? Run<T>(IReadOnlyList<SourceRaceCandidate<T>> candidates, string label,
        Func<bool>? isCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (candidates.Count == 0) throw new ArgumentException("至少需要一个候选源", nameof(candidates));

        var clock = Stopwatch.StartNew();
        var pending = new List<(SourceRaceCandidate<T> Source, Task<T> Task)>(candidates.Count);
        var errors = new List<Exception>(candidates.Count);
        var next = 0;
        var started = 0;
        var deadline = TimeSpan.FromSeconds(candidates[^1].StartAtSeconds + SingleSourceTimeoutSeconds);

        while (true)
        {
            if (isCancelled is not null && isCancelled())
            {
                DownloadLog.Info($"{label}获取已取消（已启动 {started} 个候选源）");
                return null;
            }

            // 到点启动候选：第一个立即启动，其余在切换时刻启动
            while (next < candidates.Count && TimeSpan.FromSeconds(candidates[next].StartAtSeconds) <= clock.Elapsed)
                Start(candidates[next++]);

            // 已启动的全部失败且还有未启动的：立即启动下一个（VB 的 BeforeLoadersAllFailed 加速）
            if (next < candidates.Count && pending.Count == 0)
            {
                DownloadLog.Info($"{label}已启动的候选源全部失败，立即启动下一个：{candidates[next].SourceName}");
                Start(candidates[next++]);
            }

            // 先完成者优先；同一轮有多个完成时按候选顺序取靠前的那个
            for (var i = 0; i < pending.Count; i++)
            {
                var (source, task) = pending[i];
                if (!task.IsCompleted) continue;
                pending.RemoveAt(i);
                if (task.IsCompletedSuccessfully)
                {
                    DownloadLog.Info($"{label}获取成功：{source.SourceName}，耗时 {clock.ElapsedMilliseconds}ms");
                    return new SourceRaceResult<T>(task.Result, source.SourceName, source.IsOfficial,
                        clock.ElapsedMilliseconds);
                }

                var error = task.Exception?.GetBaseException() ?? new Exception($"{label}候选源获取失败");
                errors.Add(error);
                DownloadLog.Warn(error, $"{label}候选源失败（{source.SourceName}）");
                i--;
            }

            if (next >= candidates.Count && pending.Count == 0) break;

            if (clock.Elapsed >= deadline)
            {
                DownloadLog.Warn($"{label}获取超过兜底时限 {deadline.TotalSeconds:0}s，放弃剩余候选");
                break;
            }

            Thread.Sleep(PollMilliseconds);
        }

        var detail = errors.Count == 0
            ? "没有源在时限内返回结果"
            : string.Join("；", errors.Select(e => e.Message));
        var kind = errors.Count == 0 ? ResourceRequestErrorKind.Timeout : ResourceRequestErrorKind.Network;
        throw new ResourceRequestException(kind, candidates[0].Url,
            $"{label}的所有候选源均获取失败（尝试 {errors.Count} 个源）：{detail}", null,
            new AggregateException(errors));

        void Start(SourceRaceCandidate<T> source)
        {
            if (started > 0)
                DownloadLog.Info($"{label}切换候选源：{source.SourceName}（{clock.ElapsedMilliseconds}ms）");
            started++;
            pending.Add((source, Task.Run(source.Fetch)));
        }
    }
}
