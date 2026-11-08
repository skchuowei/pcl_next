namespace PCL.Install;

using System.Diagnostics;
using PCL.Download;

/// <summary>
/// 执行器：按 P4-A 描述符的顺序与 Block 语义遍历节点树。
/// <para>
/// 语义（与装配层约定一致，可单测）：
/// <list type="number">
///   <item>Block = true 的节点：先等待所有已启动的非阻塞节点完成，再串行执行本节点；</item>
///   <item>Block = false 的节点：启动后不阻塞后续节点，由下一个 Block 节点或收尾统一等待；</item>
///   <item>复合体内部按同样规则递归；</item>
///   <item>执行前校验节点声明的宿主资产存在，缺失即抛出（不跳过、不降级）；</item>
///   <item>取消令牌在节点边界检查，取消后不再启动新节点。</item>
/// </list>
/// </para>
/// </summary>
public sealed class InstallRunner
{
    private readonly IReadOnlyList<InstallNode> _nodes;
    private readonly IInstallAssetSource _assets;
    private readonly IDownloadEngine _engine;
    private readonly DownloadOptions? _downloadOptions;
    private readonly DownloadStepAction _downloadAction = new();
    private readonly double _totalLeafWeight;

    public InstallRunner(
        IReadOnlyList<InstallNode> nodes,
        IInstallAssetSource assets,
        IDownloadEngine engine,
        DownloadOptions? downloadOptions = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(engine);
        _nodes = nodes;
        _assets = assets;
        _engine = engine;
        _downloadOptions = downloadOptions;

        double sum = 0;
        foreach (var node in nodes) sum += node.LeafWeightSum();
        _totalLeafWeight = sum;
    }

    public async Task<InstallRunReport> RunAsync(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var state = new RunState(_totalLeafWeight);
        var pending = new List<Task>();

        foreach (var node in _nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.Block)
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
                pending.Clear();
                await RunNode(node, state, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                pending.Add(RunNode(node, state, progress, cancellationToken));
            }
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
        stopwatch.Stop();

        return new InstallRunReport
        {
            CompletedSteps = state.CompletedSteps,
            OverallProgress = state.OverallProgress,
            TotalBytesDownloaded = state.BytesDownloaded,
            Elapsed = stopwatch.Elapsed,
        };
    }

    private async Task RunNode(
        InstallNode node, RunState state, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var assetName in node.Inputs)
        {
            if (!_assets.TryGet(assetName, out _))
                throw new MissingInstallAssetException(node.Name, assetName);
        }

        switch (node.LoaderKind)
        {
            case InstallLoaderKind.Task:
                await node.Action!
                    .ExecuteAsync(CreateContext(node, state, progress), cancellationToken)
                    .ConfigureAwait(false);
                break;

            case InstallLoaderKind.Download:
                await _downloadAction
                    .ExecuteAsync(CreateContext(node, state, progress), cancellationToken)
                    .ConfigureAwait(false);
                break;

            case InstallLoaderKind.Combo:
                await RunChildren(node, state, progress, cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(node), node.LoaderKind, "未知的加载器种类");
        }

        state.Complete(node);
        progress?.Report(new InstallProgress
        {
            StepName = node.Name,
            OverallProgress = state.OverallProgress,
            StepProgress = 1,
        });
    }

    private async Task RunChildren(
        InstallNode combo, RunState state, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var pending = new List<Task>();
        foreach (var child in combo.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.Block)
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
                pending.Clear();
                await RunNode(child, state, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                pending.Add(RunNode(child, state, progress, cancellationToken));
            }
        }
        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    private InstallStepContext CreateContext(
        InstallNode node, RunState state, IProgress<InstallProgress>? progress)
    {
        IProgress<InstallStepProgress>? stepProgress = null;
        if (progress is not null)
        {
            stepProgress = new InlineProgress<InstallStepProgress>(p => progress.Report(new InstallProgress
            {
                StepName = node.Name,
                OverallProgress = state.ProgressFor(node, p.Fraction),
                StepProgress = p.Fraction,
                BytesDownloaded = p.BytesDownloaded,
                BytesExpected = p.BytesExpected,
                Speed = p.Speed,
            }));
        }

        return new InstallStepContext
        {
            Node = node,
            Assets = _assets,
            Engine = _engine,
            DownloadOptions = _downloadOptions,
            Progress = stepProgress,
            AddDownloadedBytes = state.AddBytes,
        };
    }

    /// <summary>跨节点/并发共享的累计状态。</summary>
    private sealed class RunState
    {
        private readonly object _gate = new();
        private readonly double _totalWeight;
        private readonly List<string> _completed = [];
        private double _completedWeight;
        private long _bytes;

        public RunState(double totalWeight) => _totalWeight = totalWeight;

        public IReadOnlyList<string> CompletedSteps
        {
            get { lock (_gate) return _completed.ToArray(); }
        }

        public long BytesDownloaded
        {
            get { lock (_gate) return _bytes; }
        }

        public double OverallProgress
        {
            get { lock (_gate) return _totalWeight <= 0 ? 1 : Math.Clamp(_completedWeight / _totalWeight, 0, 1); }
        }

        public void Complete(InstallNode node)
        {
            lock (_gate)
            {
                _completed.Add(node.Name);
                if (node.LoaderKind is InstallLoaderKind.Task or InstallLoaderKind.Download)
                    _completedWeight += node.ProgressWeight;
            }
        }

        public void AddBytes(long bytes)
        {
            if (bytes <= 0) return;
            lock (_gate) _bytes += bytes;
        }

        /// <summary>把步骤内部进度折算成整次安装的总进度。</summary>
        public double ProgressFor(InstallNode node, double fraction)
        {
            lock (_gate)
            {
                if (_totalWeight <= 0) return 1;
                double clamped = Math.Clamp(fraction, 0, 1);
                double weight = node.LoaderKind is InstallLoaderKind.Task or InstallLoaderKind.Download
                    ? node.ProgressWeight
                    : node.LeafWeightSum();
                double value = (_completedWeight + weight * clamped) / _totalWeight;
                return Math.Clamp(value, 0, 1);
            }
        }
    }
}

/// <summary>同步转发的进度适配器：避免 <see cref="Progress{T}"/> 在无同步上下文时把回调丢到线程池，
/// 导致单测里进度事件晚于 <c>RunAsync</c> 返回。</summary>
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
