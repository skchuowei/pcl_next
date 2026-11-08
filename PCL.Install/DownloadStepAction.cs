namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 把 P4-A 的 <see cref="NetFileDescriptor"/> 翻译成引擎的 <see cref="DownloadFileRequest"/>。
/// <para>
/// 本类只做字段搬运与校验，不拼 URL、不拼路径——地址与落盘位置一律由宿主经
/// <see cref="INetFileSource"/> 提供。校验口径（13 类）由 P4-A 的
/// <see cref="InstallDownloads"/> 各工厂决定，这里不得补写任何「顺手加上」的规则
/// （尤其 NeoForge 不带 Hash、VanillaJar 缺 size 回退 -1）。
/// </para>
/// </summary>
public static class NetFileRequestFactory
{
    public static DownloadFileRequest Create(NetFileDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.MaxSize is not null)
        {
            // 内核 FileCheckRule 只有 ActualSize / MinSize / Hash / IsJson，没有上限口径。
            // 与其静默丢弃约束，不如立即失败：13 类工厂当前都不设 MaxSize，出现即说明口径没对齐。
            throw new InvalidOperationException(
                $"下载描述符「{descriptor.LocalPath}」设置了 MaxSize = {descriptor.MaxSize} B，"
                + "但内核 FileCheckRule 没有体积上限校验口径，无法无损失地映射。");
        }

        if (descriptor.Urls.Count == 0)
            throw new InvalidOperationException($"下载描述符「{descriptor.LocalPath}」没有任何候选地址。");

        if (string.IsNullOrWhiteSpace(descriptor.LocalPath))
            throw new InvalidOperationException("下载描述符的落盘路径为空。");

        return new DownloadFileRequest
        {
            Sources = descriptor.Urls,
            LocalPath = descriptor.LocalPath,
            SimulateBrowserHeaders = descriptor.SimulateBrowserHeaders,
            Check = new FileCheckRule
            {
                ActualSize = descriptor.ActualSize ?? -1,
                MinSize = descriptor.MinSize ?? -1,
                Hash = descriptor.Hash,
                IsJson = descriptor.IsJson,
            },
        };
    }
}

/// <summary>
/// 下载节点的默认执行实现：把节点清单交给 <see cref="IDownloadEngine"/>，
/// 进度与已下载字节回吐给执行器。断点、多源 failover、限速、取消由引擎负责。
/// </summary>
public sealed class DownloadStepAction : IInstallStepAction
{
    public async Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Files.Count == 0)
        {
            // 装配阶段已校验过一次；这里再兜一层，防止宿主手工改节点后静默跳过。
            throw new MissingNetFileSourceException(context.Name);
        }

        var requests = new List<DownloadFileRequest>(context.Files.Count);
        foreach (var file in context.Files) requests.Add(NetFileRequestFactory.Create(file));

        IProgress<DownloadProgress>? engineProgress = null;
        if (context.Progress is not null)
        {
            var sink = context.Progress;
            engineProgress = new InlineProgress<DownloadProgress>(p => sink.Report(new InstallStepProgress
            {
                Fraction = p.OverallProgress,
                BytesDownloaded = p.TotalBytesDone,
                BytesExpected = p.TotalBytesExpected,
                Speed = p.Speed,
            }));
        }

        var report = await context.Engine
            .DownloadAsync(requests, context.DownloadOptions, engineProgress, cancellationToken)
            .ConfigureAwait(false);

        context.AddDownloadedBytes?.Invoke(report.TotalBytesDownloaded);
        context.Progress?.Report(new InstallStepProgress
        {
            Fraction = 1,
            BytesDownloaded = report.TotalBytesDownloaded,
            BytesExpected = report.TotalBytesDownloaded,
        });
    }
}
