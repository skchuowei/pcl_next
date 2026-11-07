using System.Text.Json.Nodes;
using PCL.Download;

namespace PCL.Cli.Commands;

/// <summary>
/// <c>pclcli install</c>：把版本 JSON 描述的**下载段**落到实例目录（dev.4 B2 门槛）。
/// <para/>
/// 范围内：client jar / libraries / assetIndex / logging 配置（可选 <c>--objects</c> 全量资源对象）。
/// 范围外（本版明确不做，见计划 §4「明确不做」）：安装编排、版本隔离、natives 解压、<c>inheritsFrom</c> 合并。
/// 这些仍在 VB 侧，Phase 6 之后再下沉。
/// </para>
/// </summary>
internal static class InstallCommand
{
    public const string Usage = """
        pclcli install --dir <实例目录> (--version-json <路径|URL> | --version-id <id|latest>)
                       [--only client,libraries,assets,logging] [--objects]
                       [--mirror auto|official|mirror]

        --version-id latest 表示取官方清单里的最新正式版；其余取值按版本名精确匹配。
        --objects 会额外下载 assets 索引里的全部对象（数百 MB），默认关闭。
        """;

    public static async Task<int> RunAsync(CliSession session)
    {
        var args = session.Args;
        var instanceDir = Path.GetFullPath(args.Require("dir", "实例目录"));

        session.AnnouncePolicy();
        if (!session.Policy.AllowMcDownload) return session.RefuseByPolicy();

        var only = ParseOnly(args.Get("only"));

        using var engine = new EngineHolder();
        try
        {
            var version = await McVersionResolver
                .ResolveAsync(session, engine.Engine, args.Get("version-json"), args.Get("version-id"))
                .ConfigureAwait(false);
            session.Output.Say($"版本 {version.Id}：{version.Libraries.Count} 条依赖库");

            var requests = BuildRequests(session, version, instanceDir, only, args.GetBool("objects"));
            if (requests.Count == 0)
            {
                session.Output.Error("nothing_to_do", "按 --only 过滤后没有需要下载的文件");
                session.Output.Result(false, ("code", "nothing_to_do"));
                return (int)CliExitCode.Argument;
            }

            // assets 对象要先读索引，索引本身也必须先落盘
            var indexRequest = requests.FirstOrDefault(r => r.LocalPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (args.GetBool("objects") && indexRequest is not null)
            {
                var extra = await McVersionResolver.BuildObjectRequestsAsync(session, engine.Engine, indexRequest, instanceDir).ConfigureAwait(false);
                requests.AddRange(extra);
            }

            session.Output.Say($"待下载 {requests.Count} 个文件 → {instanceDir}");

            var progress = session.CreateProgress("install");
            var report = await engine.Engine
                .DownloadAsync(requests, session.CreateOptions(), progress, session.Token)
                .ConfigureAwait(false);
            session.Output.EndProgressLine();

            var failures = 0;
            foreach (var request in requests)
            {
                if (request.Check is null) continue;
                var error = request.Check.Validate(request.LocalPath);
                if (error is null) continue;
                failures++;
                session.Output.Error("verify_failed", request.LocalPath, ("error", error));
            }

            session.Output.Say(
                $"完成：{report.Files.Count} 个文件，{CliSession.FormatSize(report.TotalBytesDownloaded)}，" +
                $"耗时 {report.Elapsed.TotalSeconds:F1}s");

            if (failures > 0)
            {
                session.Output.Result(false,
                    ("code", "verify_failed"), ("version", version.Id), ("files", report.Files.Count),
                    ("verifyFailures", failures));
                return (int)CliExitCode.Verify;
            }

            session.Output.Result(true,
                ("version", version.Id),
                ("files", report.Files.Count),
                ("bytes", report.TotalBytesDownloaded),
                ("localReuse", report.Files.Count(f => f.IsLocalReuse)),
                ("seconds", Math.Round(report.Elapsed.TotalSeconds, 2)));
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

    /// <summary>要下载的段落。</summary>
    private sealed class OnlySet
    {
        public bool Client = true;
        public bool Libraries = true;
        public bool Assets = true;
        public bool Logging = true;
    }

    private static OnlySet ParseOnly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new OnlySet();

        var set = new OnlySet { Client = false, Libraries = false, Assets = false, Logging = false };
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "client" or "jar": set.Client = true; break;
                case "libraries" or "libs": set.Libraries = true; break;
                case "assets" or "index": set.Assets = true; break;
                case "logging" or "log": set.Logging = true; break;
                default: throw new CliUsageException($"--only 不支持「{part}」，可选 client / libraries / assets / logging");
            }
        }

        if (!set.Client && !set.Libraries && !set.Assets && !set.Logging)
            throw new CliUsageException("--only 至少需要包含一段");

        return set;
    }

    private static List<DownloadFileRequest> BuildRequests(
        CliSession session, VersionJson version, string instanceDir, OnlySet only, bool withObjects)
    {
        var requests = new List<DownloadFileRequest>();

        if (only.Client && version.Client is { Url.Length: > 0 } client)
        {
            requests.Add(new DownloadFileRequest
            {
                Sources = McSource.ForMeta(client.Url, session.Mirror),
                LocalPath = Path.Combine(instanceDir, "versions", version.Id, version.Id + ".jar"),
                Check = ToRule(client),
                SimulateBrowserHeaders = true,
            });
        }

        if (only.Libraries)
        {
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
        }

        if (only.Assets && version.AssetIndex is { Url.Length: > 0 } index)
        {
            requests.Add(new DownloadFileRequest
            {
                Sources = McSource.ForMeta(index.Url, session.Mirror),
                LocalPath = Path.Combine(instanceDir, "assets", "indexes", (index.Id ?? version.Id) + ".json"),
                Check = ToRule(index),
                SimulateBrowserHeaders = true,
            });
        }

        if (only.Logging && version.LoggingClient is { Url.Length: > 0 } logging)
        {
            requests.Add(new DownloadFileRequest
            {
                Sources = McSource.ForMeta(logging.Url, session.Mirror),
                LocalPath = Path.Combine(instanceDir, "assets", "log_configs", logging.Id ?? "client-log.xml"),
                Check = ToRule(logging),
                SimulateBrowserHeaders = true,
            });
        }

        foreach (var request in requests) McVersionResolver.EnsureDirectory(request.LocalPath);
        return requests;
    }

    /// <summary>把版本 JSON 里的下载条目转成引擎校验规则（有 sha1 就必定校验）。</summary>
    private static FileCheckRule ToRule(VersionDownload download) => new()
    {
        Hash = string.IsNullOrWhiteSpace(download.Sha1) ? null : download.Sha1,
        ActualSize = download.Size,
    };

    /// <summary>把 <see cref="DownloadEngine"/> 的释放交给 using（引擎当前无资源，留扩展位）。</summary>
    private sealed class EngineHolder : IDisposable
    {
        public IDownloadEngine Engine { get; } = new DownloadEngine();
        public void Dispose() { }
    }
}
