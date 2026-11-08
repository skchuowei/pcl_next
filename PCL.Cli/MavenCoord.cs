namespace PCL.Cli;

/// <summary>
/// Maven 坐标解析（<c>group:artifact:version[:classifier]</c>）与 jar 路径推导。
/// 加载器（Fabric / Forge / NeoForge）的依赖库均以 Maven 坐标描述，
/// 需据此拼出 maven 仓库内的 jar 相对路径与文件名。
/// </summary>
internal sealed record MavenCoord(string Group, string Artifact, string Version, string? Classifier)
{
    /// <summary>按冒号拆分坐标；classifier 可选。格式非法时返回 null。</summary>
    public static MavenCoord? Parse(string? coordinate)
    {
        if (string.IsNullOrWhiteSpace(coordinate)) return null;
        var parts = coordinate.Split(':');
        if (parts.Length < 3) return null;
        var group = parts[0].Trim();
        var artifact = parts[1].Trim();
        var version = parts[2].Trim();
        var classifier = parts.Length >= 4 ? parts[3].Trim() : null;
        if (group.Length == 0 || artifact.Length == 0 || version.Length == 0) return null;
        return new MavenCoord(group, artifact, version, string.IsNullOrWhiteSpace(classifier) ? null : classifier);
    }

    /// <summary>jar 文件名：<c>artifact-version[-classifier].jar</c>。</summary>
    public string JarName => Classifier is null
        ? $"{Artifact}-{Version}.jar"
        : $"{Artifact}-{Version}-{Classifier}.jar";

    /// <summary>jar 在 maven 仓库内的相对路径（正斜杠分隔）。</summary>
    public string RelativePath =>
        $"{Group.Replace('.', '/')}/{Artifact}/{Version}/{JarName}";

    /// <summary>基于仓库根 URL 拼出完整下载地址（根以 / 结尾或不带尾斜杠均可）。</summary>
    public string ToUrl(string repositoryRoot)
    {
        var root = repositoryRoot.TrimEnd('/');
        return $"{root}/{RelativePath}";
    }
}
