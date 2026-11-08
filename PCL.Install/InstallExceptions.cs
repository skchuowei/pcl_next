namespace PCL.Install;

/// <summary>
/// 装配失败的基类。装配层对「说不清怎么执行」的节点一律抛出，绝不静默跳过——
/// 静默跳过会让安装「看起来成功、实际缺件」，比直接失败更难排查。
/// </summary>
public class InstallAssemblyException : InvalidOperationException
{
    public InstallAssemblyException(string message) : base(message) { }
}

/// <summary>Task 节点没有对应的执行实现。</summary>
public sealed class MissingInstallActionException : InstallAssemblyException
{
    public string StepName { get; }

    public MissingInstallActionException(string stepName)
        : base($"装配失败：步骤「{stepName}」是 Task 节点，但没有注册任何执行实现（IInstallStepAction）。")
    {
        StepName = stepName;
    }
}

/// <summary>Download 节点没有拿到下载文件清单。</summary>
public sealed class MissingNetFileSourceException : InstallAssemblyException
{
    public string StepName { get; }

    public MissingNetFileSourceException(string stepName)
        : base($"装配失败：下载步骤「{stepName}」没有提供任何下载文件描述符（INetFileSource 返回空）。")
    {
        StepName = stepName;
    }
}

/// <summary>复合体既没有内联子步骤，也没有注册子步骤提供者。</summary>
public sealed class MissingSubStepSourceException : InstallAssemblyException
{
    public string StepName { get; }

    public string? SubStepsSource { get; }

    public MissingSubStepSourceException(string stepName, string? source, string reason)
        : base(source is null
            ? $"装配失败：复合步骤「{stepName}」既无内联子步骤，也未登记 SubStepsSource。{reason}"
            : $"装配失败：复合步骤「{stepName}」的子步骤来源「{source}」没有注册 C# 提供者（ISubStepProvider）。{reason}")
    {
        StepName = stepName;
        SubStepsSource = source;
    }
}

/// <summary>宿主给的文件类别与描述符声明的下载类别不一致。</summary>
public sealed class NetFileKindMismatchException : InstallAssemblyException
{
    public string StepName { get; }

    public NetFileKindMismatchException(string stepName, string expected, string actual, string localPath)
        : base($"装配失败：步骤「{stepName}」声明下载类别 [{expected}]，"
               + $"但宿主给的文件「{localPath}」类别为 {actual}。")
    {
        StepName = stepName;
    }
}

/// <summary>执行时发现节点声明需要的宿主资产不存在。</summary>
public sealed class MissingInstallAssetException : InstallAssemblyException
{
    public string StepName { get; }

    public string AssetName { get; }

    public MissingInstallAssetException(string stepName, string assetName)
        : base($"执行失败：步骤「{stepName}」需要宿主资产「{assetName}」，但 IInstallAssetSource 中不存在该键。")
    {
        StepName = stepName;
        AssetName = assetName;
    }
}
