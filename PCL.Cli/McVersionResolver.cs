using System.Text.Json.Nodes;
using PCL.Download;

namespace PCL.Cli;

/// <summary>
/// 版本 JSON 的获取与解析（<c>install</c> 与 <c>selftest</c> 共用）。
/// <para/>
/// 所有网络取数一律走 <see cref="IDownloadEngine"/>（计划 §5.3），本类不持有 HttpClient。
/// </para>
/// </summary>
internal static class McVersionResolver
{
    public const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    /// <summary>用引擎把 JSON 拉下来，并要求内容确为合法 JSON。</summary>
    public static Task DownloadJsonAsync(
        CliSession session, IDownloadEngine engine, IReadOnlyList<string> sources, string localPath, string label)
    {
        EnsureDirectory(localPath);
        session.Output.WriteLog("INFO", $"fetch {label}: {string.Join(" / ", sources)}");
        return engine.DownloadAsync(
            [
                new DownloadFileRequest
                {
                    Sources = sources,
                    LocalPath = localPath,
                    Check = new FileCheckRule { IsJson = true },
                    SimulateBrowserHeaders = true,
                },
            ],
            session.CreateOptions(),
            null,
            session.Token);
    }

    /// <summary>
    /// 取得版本 JSON 文本：支持本地路径、直链 URL，或按版本名从官方清单里查 URL。
    /// </summary>
    public static async Task<string> ResolveTextAsync(
        CliSession session, IDownloadEngine engine, string? versionJson, string? versionId)
    {
        if (versionJson is null && versionId is null)
            throw new CliUsageException("请用 --version-json 或 --version-id 指定要安装的版本");

        if (versionJson is not null)
        {
            if (versionJson.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(session.WorkDir, "version.json");
                await DownloadJsonAsync(session, engine, McSource.ForMeta(versionJson, session.Mirror), target, "version.json")
                    .ConfigureAwait(false);
                return await File.ReadAllTextAsync(target, session.Token).ConfigureAwait(false);
            }

            if (!File.Exists(versionJson)) throw new CliUsageException($"版本 JSON 不存在：{versionJson}");
            return await File.ReadAllTextAsync(versionJson, session.Token).ConfigureAwait(false);
        }

        // --version-id：先取清单，再按 id 找 url
        var manifestPath = Path.Combine(session.WorkDir, "version_manifest_v2.json");
        await DownloadJsonAsync(session, engine, McSource.ForMeta(VersionManifestUrl, session.Mirror), manifestPath, "manifest")
            .ConfigureAwait(false);

        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, session.Token).ConfigureAwait(false));
        var wanted = versionId!;
        if (wanted is "latest" or "latest-release")
        {
            wanted = manifest?["latest"]?["release"]?.GetValue<string>()
                     ?? throw new CliUsageException("版本清单里没有 latest.release");
            session.Output.Say($"latest → {wanted}");
        }

        var versions = manifest?["versions"] as JsonArray
                       ?? throw new CliUsageException("版本清单格式异常：缺少 versions 数组");
        var entry = versions.FirstOrDefault(v => string.Equals(v?["id"]?.GetValue<string>(), wanted, StringComparison.Ordinal));
        var url = entry?["url"]?.GetValue<string>()
                  ?? throw new CliUsageException(
                      $"版本清单中找不到 {wanted}（可用 --version-id latest 取最新正式版）");

        var jsonPath = Path.Combine(session.WorkDir, $"version-{wanted}.json");
        await DownloadJsonAsync(session, engine, McSource.ForMeta(url, session.Mirror), jsonPath, $"version-{wanted}.json")
            .ConfigureAwait(false);
        return await File.ReadAllTextAsync(jsonPath, session.Token).ConfigureAwait(false);
    }

    /// <summary>取得并解析版本 JSON。</summary>
    public static async Task<VersionJson> ResolveAsync(
        CliSession session, IDownloadEngine engine, string? versionJson, string? versionId)
        => VersionJson.Parse(await ResolveTextAsync(session, engine, versionJson, versionId).ConfigureAwait(false));

    /// <summary>确保目标文件的父目录存在。</summary>
    public static void EnsureDirectory(string localPath)
    {
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <summary>按 assets 索引展开全部对象（<c>--objects</c>）。索引文件此时已在本地；缺失时先单独拉取。</summary>
    public static async Task<List<DownloadFileRequest>> BuildObjectRequestsAsync(
        CliSession session, IDownloadEngine engine, DownloadFileRequest indexRequest, string instanceDir)
    {
        if (!File.Exists(indexRequest.LocalPath))
        {
            await engine.DownloadAsync([indexRequest], session.CreateOptions(), null, session.Token).ConfigureAwait(false);
        }

        var result = new List<DownloadFileRequest>();
        var root = JsonNode.Parse(File.ReadAllText(indexRequest.LocalPath));
        if (root?["objects"] is not JsonObject objects) return result;

        var objectsDir = Path.Combine(instanceDir, "assets", "objects");
        foreach (var (_, node) in objects)
        {
            var hash = node?["hash"]?.GetValue<string>();
            if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;
            var size = node?["size"] is JsonValue sizeNode && sizeNode.TryGetValue<long>(out var parsed) ? parsed : -1;

            var url = $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";
            result.Add(new DownloadFileRequest
            {
                Sources = McSource.ForAsset(url, session.Mirror),
                LocalPath = Path.Combine(objectsDir, hash[..2], hash),
                Check = new FileCheckRule { Hash = hash, ActualSize = size },
            });
        }

        session.Output.Say($"assets 索引含 {result.Count} 个对象");
        return result;
    }
}
