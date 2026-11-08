using System.Text.Json.Nodes;

namespace PCL.Cli;

/// <summary>加载器依赖库（Maven 坐标 + 仓库根 + 校验）。</summary>
internal sealed record LoaderLibrary(string Name, string RepositoryRoot, string? Sha1, long Size, string? SourceUrl)
{
    public MavenCoord? Coord => MavenCoord.Parse(Name);

    /// <summary>是否为安装期 processor 产物（如 Forge <c>-client.jar</c>），下载层不在 maven 上拉取。</summary>
    public bool IsProcessorArtifact { get; init; }
}

/// <summary>
/// 解析后的加载器 profile：与原版版本 JSON 合并所需的最小字段集。
/// </summary>
internal sealed class LoaderProfile
{
    public required string Id { get; init; }
    public required string MainClass { get; init; }
    public required string InheritsFrom { get; init; }
    public required IReadOnlyList<LoaderLibrary> Libraries { get; init; }
    /// <summary>原样保留的启动参数（game / jvm），合并时并入最终版本 JSON。</summary>
    public JsonNode? Arguments { get; init; }
    /// <summary>加载器类型标识：fabric / forge / neoforge。</summary>
    public required string LoaderKind { get; init; }
}
