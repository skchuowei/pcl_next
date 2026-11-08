namespace PCL.Install.Tests;

using PCL.Download;

/// <summary>任何键都「存在」的资产源，用于只关心结构的用例。</summary>
internal sealed class AllPresentAssets : IInstallAssetSource
{
    public bool TryGet(string name, out object? value)
    {
        value = null;
        return true;
    }
}

/// <summary>按键名精确给出下载清单的假实现；默认按描述符声明的下载类别合成一份。</summary>
internal sealed class FakeFileSource : INetFileSource
{
    private readonly Dictionary<string, IReadOnlyList<NetFileDescriptor>?> _byName = new(StringComparer.Ordinal);

    /// <summary>为 <c>null</c> 时一律返回 null，用于验证「缺下载清单必须抛出」。</summary>
    public bool ReturnNull { get; set; }

    /// <summary>为 <c>true</c> 时无视声明类别，一律给 Fabric 主文件，用于验证类别校验。</summary>
    public bool ForceFabricMain { get; set; }

    public FakeFileSource Set(string stepName, params NetFileDescriptor[] files)
    {
        _byName[stepName] = files;
        return this;
    }

    public IReadOnlyList<NetFileDescriptor>? Get(InstallTaskDescriptor descriptor, IInstallAssetSource assets)
    {
        if (_byName.TryGetValue(descriptor.Name, out var explicitFiles)) return explicitFiles;
        if (ReturnNull) return null;
        if (ForceFabricMain)
            return [new NetFileDescriptor(["https://example.test/forge"], "/tmp/fabric-main.json", kind: NetFileDescriptorKind.FabricMain)];

        if (descriptor.DownloadKinds.Count == 0)
            return [new NetFileDescriptor(["https://example.test/unknown"], "/tmp/unknown", kind: NetFileDescriptorKind.FabricMain)];

        var list = new List<NetFileDescriptor>(descriptor.DownloadKinds.Count);
        foreach (var kind in descriptor.DownloadKinds)
            list.Add(new NetFileDescriptor([$"https://example.test/{kind}"], $"/tmp/{kind}", kind: kind));
        return list;
    }
}

/// <summary>记录开始/结束事件的假动作，可注入延迟用于观察 Block 语义。</summary>
internal sealed class RecordingAction : IInstallStepAction
{
    private readonly List<string> _events;
    private readonly object _gate;
    private readonly TimeSpan _delay;

    public RecordingAction(List<string> events, TimeSpan? delay = null, object? gate = null)
    {
        _events = events;
        _delay = delay ?? TimeSpan.Zero;
        _gate = gate ?? new object();
    }

    public async Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken)
    {
        Add("start:" + context.Name);
        if (_delay > TimeSpan.Zero) await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        Add("end:" + context.Name);
    }

    private void Add(string value)
    {
        lock (_gate) _events.Add(value);
    }
}

/// <summary>记录请求的假下载引擎。</summary>
internal sealed class FakeEngine : IDownloadEngine
{
    private readonly object _gate = new();
    private readonly List<DownloadFileRequest> _requests = [];

    public long BytesPerCall { get; set; } = 100;

    public int CallCount { get; private set; }

    public IReadOnlyList<DownloadFileRequest> Requests
    {
        get { lock (_gate) return _requests.ToArray(); }
    }

    public Task<DownloadReport> DownloadAsync(
        IEnumerable<DownloadFileRequest> files,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            CallCount++;
            _requests.AddRange(files);
        }

        progress?.Report(new DownloadProgress
        {
            OverallProgress = 0.5,
            TotalBytesDone = BytesPerCall,
            Speed = 0,
            ActiveChunks = 1,
        });

        return Task.FromResult(new DownloadReport
        {
            Files = [],
            TotalBytesDownloaded = BytesPerCall,
            Elapsed = TimeSpan.Zero,
        });
    }
}
