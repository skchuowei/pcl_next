using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Download;

namespace PCL.Cli.Commands;

/// <summary>
/// <c>pclcli loader</c>：把原版 + 指定加载器（Fabric / Forge / NeoForge）下载拼装成一个完整可启动实例。
/// <para/>
/// 编排顺序：解析原版版本 JSON → 下载原版 client / libraries / assetIndex / logging →
/// 解析加载器 profile → 下载加载器依赖库 → 合并出最终版本 JSON。
/// 所有文件下载走 <see cref="IDownloadEngine"/>，不在命令内自写 HttpClient 循环。
/// </summary>
internal static class InstallLoaderCommand
{
    public const string Usage = """
        pclcli loader --loader <fabric|forge|neoforge> --mc <原版版本>
                      [--loader-version <版本号>] --dir <实例目录>
                      [--mirror auto|official|mirror]

        示例：
          pclcli loader --loader fabric --mc 1.21.4 --dir ./instances/my-fabric
          pclcli loader --loader forge  --mc 1.21    --dir ./instances/my-forge
        """;

    public static async Task<int> RunAsync(CliSession session)
    {
        var args = session.Args;
        var loaderKind = args.Require("loader", "加载器类型 fabric / forge / neoforge").ToLowerInvariant();
        var mcVersion = args.Require("mc", "原版版本号");
        var instanceDir = Path.GetFullPath(args.Require("dir", "实例目录"));
        var loaderVersion = args.Get("loader-version");

        session.AnnouncePolicy();
        if (!session.Policy.AllowMcDownload) return session.RefuseByPolicy();

        using var engine = new EngineHolder();
        try
        {
            // 1. 解析原版版本 JSON（完整文本供合并，VersionJson 供下载段展开）
            var vanillaText = await McVersionResolver
                .ResolveTextAsync(session, engine.Engine, null, mcVersion)
                .ConfigureAwait(false);
            var vanilla = VersionJson.Parse(vanillaText);
            session.Output.Say($"原版 {vanilla.Id}：{vanilla.Libraries.Count} 条依赖库");

            // 2. 解析加载器 profile
            LoaderProfile profile = loaderKind switch
            {
                "fabric" => await LoaderResolver.ResolveFabricAsync(session, engine.Engine, mcVersion, loaderVersion).ConfigureAwait(false),
                "forge" => await LoaderResolver.ResolveForgeAsync(session, engine.Engine, mcVersion, loaderVersion, instanceDir).ConfigureAwait(false),
                "neoforge" => await LoaderResolver.ResolveNeoAsync(session, engine.Engine, mcVersion, loaderVersion, instanceDir).ConfigureAwait(false),
                _ => throw new CliUsageException($"不支持的加载器类型「{loaderKind}」，可选 fabric / forge / neoforge"),
            };
            session.Output.Say($"加载器 {profile.LoaderKind} {profile.Id}：{profile.Libraries.Count} 条依赖库，mainClass={profile.MainClass}");
            var processorSkipped = profile.Libraries.Count(l => l.IsProcessorArtifact);
            if (processorSkipped > 0)
                session.Output.Say($"注意：{processorSkipped} 条为安装期 processor 产物（二进制补丁/重命名），下载层不拉取，需 Java 安装器在运行环境补齐后实例方可启动");

            // 3. 拼装下载请求：原版段 + 加载器段
            var requests = BuildVanillaRequests(session, vanilla, instanceDir);
            var loaderRequests = BuildLoaderRequests(session, profile, instanceDir);
            requests.AddRange(loaderRequests);
            foreach (var request in requests) McVersionResolver.EnsureDirectory(request.LocalPath);

            session.Output.Say($"待下载 {requests.Count} 个文件 → {instanceDir}");

            var progress = session.CreateProgress("loader");
            var report = await engine.Engine
                .DownloadAsync(requests, session.CreateOptions(), progress, session.Token)
                .ConfigureAwait(false);
            session.Output.EndProgressLine();

            // 4. 下载完成后逐文件校验
            var failures = 0;
            foreach (var request in requests)
            {
                if (request.Check is null) continue;
                var error = request.Check.Validate(request.LocalPath);
                if (error is null) continue;
                failures++;
                session.Output.Error("verify_failed", request.LocalPath, ("error", error));
            }

            // 5. 合并最终版本 JSON
            var merged = Merge(vanillaText, profile);
            var versionDir = Path.Combine(instanceDir, "versions", profile.Id);
            Directory.CreateDirectory(versionDir);
            var versionJsonPath = Path.Combine(versionDir, profile.Id + ".json");
            await File.WriteAllTextAsync(versionJsonPath, merged, session.Token).ConfigureAwait(false);

            var avgSpeed = report.Elapsed.TotalSeconds > 0
                ? report.TotalBytesDownloaded / report.Elapsed.TotalSeconds
                : 0;
            session.Output.Say(
                $"完成：{report.Files.Count} 个文件，{CliSession.FormatSize(report.TotalBytesDownloaded)}，" +
                $"耗时 {report.Elapsed.TotalSeconds:F1}s，平均 {CliSession.FormatSize((long)avgSpeed)}/s");

            if (failures > 0)
            {
                session.Output.Result(false,
                    ("code", "verify_failed"), ("version", profile.Id), ("files", report.Files.Count),
                    ("verifyFailures", failures), ("bytes", report.TotalBytesDownloaded),
                    ("seconds", Math.Round(report.Elapsed.TotalSeconds, 2)));
                return (int)CliExitCode.Verify;
            }

            session.Output.Result(true,
                ("loader", profile.LoaderKind),
                ("version", profile.Id),
                ("files", report.Files.Count),
                ("bytes", report.TotalBytesDownloaded),
                ("avgSpeed", Math.Round(avgSpeed, 1)),
                ("localReuse", report.Files.Count(f => f.IsLocalReuse)),
                ("processorSkipped", processorSkipped),
                ("needsProcessor", processorSkipped > 0),
                ("seconds", Math.Round(report.Elapsed.TotalSeconds, 2)),
                ("versionJson", versionJsonPath));
            return (int)CliExitCode.Ok;
        }
        catch (OperationCanceledException)
        {
            session.Output.EndProgressLine();
            session.Output.Error("canceled", "安装下载被中断");
            session.Output.Result(false, ("code", "canceled"));
            return (int)CliExitCode.Canceled;
        }
        catch (DownloadFailedException ex)
        {
            session.Output.EndProgressLine();
            foreach (var (path, errors) in ex.ErrorsByFile)
                session.Output.Error("download_failed", path, ("errors", errors.Select(e => e.Message).ToArray()));
            session.Output.Result(false, ("code", "download_failed"));
            return (int)CliExitCode.Network;
        }
        catch (CliUsageException ex)
        {
            session.Output.EndProgressLine();
            session.Output.Error("bad_argument", ex.Message);
            session.Output.Result(false, ("code", "bad_argument"));
            return (int)CliExitCode.Argument;
        }
        catch (ArgumentException ex)
        {
            session.Output.EndProgressLine();
            session.Output.Error("bad_argument", ex.Message);
            session.Output.Result(false, ("code", "bad_argument"));
            return (int)CliExitCode.Argument;
        }
    }

    /// <summary>原版下载请求：client / libraries / assetIndex / logging。</summary>
    private static List<DownloadFileRequest> BuildVanillaRequests(
        CliSession session, VersionJson version, string instanceDir)
    {
        var requests = new List<DownloadFileRequest>();

        if (version.Client is { Url.Length: > 0 } client)
        {
            requests.Add(new DownloadFileRequest
            {
                Sources = McSource.ForMeta(client.Url, session.Mirror),
                LocalPath = Path.Combine(instanceDir, "versions", version.Id, version.Id + ".jar"),
                Check = ToRule(client),
                SimulateBrowserHeaders = true,
            });
        }

        foreach (var library in version.Libraries)
        {
            if (!library.AppliesHere) continue;
            if (library.Artifact is not { } artifact) continue;
            if (string.IsNullOrEmpty(artifact.Url) || string.IsNullOrEmpty(artifact.Path)) continue;

            requests.Add(new DownloadFileRequest
            {
                Sources = McSource.ForLibrary(artifact.Url, session.Mirror),
                LocalPath = Path.Combine(instanceDir, "libraries", artifact.Path.Replace('/', Path.DirectorySeparatorChar)),
                Check = ToRule(artifact),
                SimulateBrowserHeaders = true,
            });
        }

        if (version.AssetIndex is { Url.Length: > 0 } index)
        {
            requests.Add(new DownloadFileRequest
            {
                Sources = McSource.ForMeta(index.Url, session.Mirror),
                LocalPath = Path.Combine(instanceDir, "assets", "indexes", (index.Id ?? version.Id) + ".json"),
                Check = ToRule(index),
                SimulateBrowserHeaders = true,
            });
        }

        if (version.LoggingClient is { Url.Length: > 0 } logging)
        {
            requests.Add(new DownloadFileRequest
            {
                Sources = McSource.ForMeta(logging.Url, session.Mirror),
                LocalPath = Path.Combine(instanceDir, "assets", "log_configs", logging.Id ?? "client-log.xml"),
                Check = ToRule(logging),
                SimulateBrowserHeaders = true,
            });
        }

        return requests;
    }

    /// <summary>加载器依赖库下载请求（Maven 坐标 → maven 仓库 URL → libraries 本地目录）。processor 产物跳过。</summary>
    private static List<DownloadFileRequest> BuildLoaderRequests(
        CliSession session, LoaderProfile profile, string instanceDir)
    {
        var requests = new List<DownloadFileRequest>();
        foreach (var library in profile.Libraries)
        {
            if (library.IsProcessorArtifact)
            {
                session.Output.WriteLog("WARN", $"跳过 processor 产物（下载层不拉取）：{library.Name}");
                continue;
            }
            var coord = library.Coord;
            if (coord is null)
            {
                session.Output.WriteLog("WARN", $"跳过无法解析的 Maven 坐标：{library.Name}");
                continue;
            }

            var relativePath = coord.RelativePath;
            var sources = McSource.ForLoaderLibrary(coord, library.RepositoryRoot, library.SourceUrl, session.Mirror);
            requests.Add(new DownloadFileRequest
            {
                Sources = sources,
                LocalPath = Path.Combine(instanceDir, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar)),
                Check = new FileCheckRule
                {
                    Hash = string.IsNullOrWhiteSpace(library.Sha1) ? null : library.Sha1,
                    ActualSize = library.Size,
                },
                SimulateBrowserHeaders = true,
            });
        }
        return requests;
    }

    /// <summary>
    /// 以原版版本 JSON 为基底，合并加载器：覆写 id / mainClass，追加加载器依赖库（按 name 去重），合并启动参数。
    /// </summary>
    private static string Merge(string vanillaText, LoaderProfile profile)
    {
        var vanilla = JsonNode.Parse(vanillaText)?.AsObject()
            ?? throw new CliUsageException("原版版本 JSON 不是合法对象");

        vanilla["id"] = profile.Id;
        vanilla["mainClass"] = profile.MainClass;
        vanilla["inheritsFrom"] = profile.InheritsFrom;

        // 加载器库并入 libraries（按坐标名去重，已存在则保留原版条目）
        var libraries = vanilla["libraries"] as JsonArray ?? new JsonArray();
        var existing = new HashSet<string>(
            libraries.Select(n => n?["name"]?.GetValue<string>() ?? ""),
            StringComparer.Ordinal);

        foreach (var library in profile.Libraries)
        {
            var coord = library.Coord;
            if (coord is null || !existing.Add(library.Name)) continue;
            var artifactUrl = string.IsNullOrWhiteSpace(library.SourceUrl) ? coord.ToUrl(library.RepositoryRoot) : library.SourceUrl;
            libraries.Add(new JsonObject
            {
                ["name"] = library.Name,
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = new JsonObject
                    {
                        ["path"] = coord.RelativePath,
                        ["url"] = artifactUrl,
                        ["sha1"] = library.Sha1,
                        ["size"] = library.Size,
                    },
                },
            });
        }
        vanilla["libraries"] = libraries;

        // 合并启动参数：加载器的 game / jvm 追加到原版对应数组
        if (profile.Arguments is JsonObject loaderArgs)
        {
            var vanillaArgs = vanilla["arguments"] as JsonObject ?? new JsonObject();
            foreach (var key in new[] { "game", "jvm" })
            {
                if (loaderArgs[key] is not JsonArray loaderArray) continue;
                var vanillaArray = vanillaArgs[key] as JsonArray ?? new JsonArray();
                var existingArgs = new HashSet<string>(
                    vanillaArray.Select(n => n?.ToJsonString() ?? ""), StringComparer.Ordinal);
                foreach (var node in loaderArray)
                {
                    if (node is not null && existingArgs.Add(node.ToJsonString())) vanillaArray.Add(node.DeepClone());
                }
                vanillaArgs[key] = vanillaArray;
            }
            vanilla["arguments"] = vanillaArgs;
        }

        return vanilla.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static FileCheckRule ToRule(VersionDownload download) => new()
    {
        Hash = string.IsNullOrWhiteSpace(download.Sha1) ? null : download.Sha1,
        ActualSize = download.Size,
    };

    private sealed class EngineHolder : IDisposable
    {
        public IDownloadEngine Engine { get; } = new DownloadEngine();
        public void Dispose() { }
    }
}
