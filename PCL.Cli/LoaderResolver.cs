using System.Text.Json.Nodes;
using PCL.Download;

namespace PCL.Cli;

/// <summary>
/// 加载器 profile 解析：Fabric（meta JSON）、Forge / NeoForge（installer 内嵌 profile）。
/// 所有网络取数走 <see cref="IDownloadEngine"/>，不持有 HttpClient。
/// </summary>
internal static class LoaderResolver
{
    /// <summary>解析 Fabric profile；<paramref name="loaderVersion"/> 为空时取该 MC 首个稳定 loader。</summary>
    public static async Task<LoaderProfile> ResolveFabricAsync(
        CliSession session, IDownloadEngine engine, string mcVersion, string? loaderVersion)
    {
        var metaDir = Path.Combine(session.WorkDir, "loader-meta");
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            loaderVersion = await ResolveStableLoaderAsync(session, engine, mcVersion).ConfigureAwait(false);
            session.Output.Say($"Fabric loader 未指定，选用稳定版 {loaderVersion}");
        }

        var profilePath = Path.Combine(metaDir, $"fabric-{mcVersion}-{loaderVersion}.json");
        var metaPath = $"/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
        await McVersionResolver.DownloadJsonAsync(
            session, engine, McSource.ForFabricMeta(metaPath, session.Mirror), profilePath, "fabric-profile")
            .ConfigureAwait(false);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(profilePath, session.Token).ConfigureAwait(false))
            ?? throw new CliUsageException("Fabric profile JSON 解析失败");
        return ParseFabric(root);
    }

    /// <summary>拉 Fabric loader 列表，返回首个 stable 版本号。</summary>
    private static async Task<string> ResolveStableLoaderAsync(CliSession session, IDownloadEngine engine, string mcVersion)
    {
        var listPath = Path.Combine(session.WorkDir, "loader-meta", $"fabric-loaders-{mcVersion}.json");
        var metaPath = $"/v2/versions/loader/{mcVersion}";
        await McVersionResolver.DownloadJsonAsync(
            session, engine, McSource.ForFabricMeta(metaPath, session.Mirror), listPath, "fabric-loader-list")
            .ConfigureAwait(false);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(listPath, session.Token).ConfigureAwait(false))
            ?? throw new CliUsageException("Fabric loader 列表解析失败");
        if (root is not JsonArray array || array.Count == 0)
            throw new CliUsageException($"Fabric 没有 {mcVersion} 可用的 loader");

        foreach (var entry in array)
        {
            if (entry?["loader"]?["stable"]?.GetValue<bool>() == true)
                return entry["loader"]!["version"]!.GetValue<string>();
        }
        return array[0]!["loader"]!["version"]!.GetValue<string>();
    }

    /// <summary>解析 Forge profile：下载 installer jar，解压读内嵌 version.json / install_profile.json。</summary>
    public static Task<LoaderProfile> ResolveForgeAsync(
        CliSession session, IDownloadEngine engine, string mcVersion, string? loaderVersion, string instanceDir)
        => ForgeNeoResolver.ResolveAsync(session, engine, mcVersion, loaderVersion, instanceDir, "forge");

    /// <summary>解析 NeoForge profile：下载 installer jar，解压读内嵌 profile。</summary>
    public static Task<LoaderProfile> ResolveNeoAsync(
        CliSession session, IDownloadEngine engine, string mcVersion, string? loaderVersion, string instanceDir)
        => ForgeNeoResolver.ResolveAsync(session, engine, mcVersion, loaderVersion, instanceDir, "neoforge");

    private static LoaderProfile ParseFabric(JsonNode root)    {
        var obj = root.AsObject();
        var id = obj["id"]?.GetValue<string>() ?? throw new CliUsageException("Fabric profile 缺少 id");
        var mainClass = obj["mainClass"]?.GetValue<string>() ?? throw new CliUsageException("Fabric profile 缺少 mainClass");
        var inherits = obj["inheritsFrom"]?.GetValue<string>() ?? throw new CliUsageException("Fabric profile 缺少 inheritsFrom");

        var libraries = new List<LoaderLibrary>();
        if (obj["libraries"] is JsonArray libs)
        {
            foreach (var node in libs)
            {
                if (node is not JsonObject lib) continue;
                var name = lib["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var rootUrl = lib["url"]?.GetValue<string>() ?? "https://maven.fabricmc.net/";
                var sha1 = lib["sha1"]?.GetValue<string>();
                var size = lib["size"] is JsonValue s && s.TryGetValue<long>(out var sz) ? sz : -1;
                libraries.Add(new LoaderLibrary(name, rootUrl, sha1, size, null));
            }
        }

        return new LoaderProfile
        {
            Id = id,
            MainClass = mainClass,
            InheritsFrom = inherits,
            Libraries = libraries,
            Arguments = obj["arguments"]?.DeepClone(),
            LoaderKind = "fabric",
        };
    }
}
