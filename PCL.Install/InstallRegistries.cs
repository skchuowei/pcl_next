namespace PCL.Install;

using PCL.Download;

/// <summary>
/// 节点执行实现的注册表。
/// <para>
/// 解析优先级：顶层步骤按 <see cref="InstallStepKind"/> 注册 → 按名字注册 → <see cref="Fallback"/>。
/// 三档都没有则装配失败（<see cref="MissingInstallActionException"/>），不静默跳过。
/// 子加载器的 <see cref="InstallTaskDescriptor.Kind"/> 为 <c>null</c>，只能按名字或 Fallback 解析。
/// </para>
/// </summary>
public sealed class InstallActionRegistry
{
    private readonly Dictionary<InstallStepKind, IInstallStepAction> _byKind = [];
    private readonly Dictionary<string, IInstallStepAction> _byName = new(StringComparer.Ordinal);

    /// <summary>兜底实现：仅建议测试与宿主「所有 Task 走同一动作」的场景使用。</summary>
    public IInstallStepAction? Fallback { get; set; }

    public InstallActionRegistry Register(InstallStepKind kind, IInstallStepAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _byKind[kind] = action;
        return this;
    }

    public InstallActionRegistry Register(string name, IInstallStepAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        _byName[name] = action;
        return this;
    }

    public IInstallStepAction? Resolve(InstallTaskDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind is { } kind && _byKind.TryGetValue(kind, out var byKind)) return byKind;
        if (_byName.TryGetValue(descriptor.Name, out var byName)) return byName;
        return Fallback;
    }
}

/// <summary>
/// 复合体子步骤提供者的注册表，键即 <see cref="InstallTaskDescriptor.SubStepsSource"/> 字符串
/// （P4-A 登记的原 VB 函数名）。
/// </summary>
public sealed class SubStepProviderRegistry
{
    private readonly Dictionary<string, ISubStepProvider> _providers = new(StringComparer.Ordinal);

    public SubStepProviderRegistry Register(string source, ISubStepProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(provider);
        _providers[source] = provider;
        return this;
    }

    public bool TryGet(string source, out ISubStepProvider provider)
        => _providers.TryGetValue(source, out provider!);
}

/// <summary>
/// <see cref="IInstallAssetSource"/> 的字典实现。键名与描述符的 Inputs / Outputs 逐字一致。
/// </summary>
public sealed class InstallAssetBag : IInstallAssetSource
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>写入资产（值可为 <c>null</c>，表示「存在但为空」）。</summary>
    public InstallAssetBag Set(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _values[name] = value;
        return this;
    }

    public bool Contains(string name) => _values.ContainsKey(name);

    public bool TryGet(string name, out object? value) => _values.TryGetValue(name, out value);

    /// <summary>只读快照，便于宿主把一个 bag 交给多个会话。</summary>
    public IReadOnlyDictionary<string, object?> Snapshot() => _values;
}
