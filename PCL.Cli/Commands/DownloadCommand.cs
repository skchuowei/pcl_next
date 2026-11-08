using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Download;

namespace PCL.Cli.Commands;

/// <summary>
/// <c>pclcli download</c>：单/多文件下载（dev.4 B1 门槛的自动验收通道）。
/// <para/>
/// 硬性约束（计划 §5.3）：所有下载走 <see cref="IDownloadEngine.DownloadAsync"/>，
/// **不得**在本命令内自写 HttpClient 循环——B1 的意义就是验证引擎，绕过引擎等于没验。
/// </para>
/// </summary>
internal static class DownloadCommand
{
    public const string Usage = """
        pclcli download --to <本地路径> --url <源地址> [--url <源地址2> ...]
                        [--sha1|--sha256|--md5|--sha512 <哈希>] [--size <字节>] [--min-size <字节>]
                        [--browser] [--replace]
        pclcli download --file <请求.json>

        请求.json 格式：
          { "files": [ { "to": "D:\\a\\b.jar", "sources": ["https://..."],
                         "sha1": "…", "size": 123, "browserHeaders": false } ] }
        """;

    public static async Task<int> RunAsync(CliSession session)
    {
        var args = session.Args;
        var requests = BuildRequests(args);

        session.AnnouncePolicy();
        session.Output.Say($"共 {requests.Count} 个文件，工作目录 {session.WorkDir}");

        var engine = new DownloadEngine();
        var progress = session.CreateProgress("download");

        try
        {
            var report = await engine.DownloadAsync(requests, session.CreateOptions(), progress, session.Token)
                .ConfigureAwait(false);
            session.Output.EndProgressLine();

            var verifyFailures = VerifyAll(requests, session.Output);

            session.Output.Say(
                $"完成：{report.Files.Count} 个文件，{CliSession.FormatSize(report.TotalBytesDownloaded)}，" +
                $"耗时 {report.Elapsed.TotalSeconds:F1}s");

            if (verifyFailures > 0)
            {
                session.Output.Result(false,
                    ("code", "verify_failed"),
                    ("files", report.Files.Count),
                    ("verifyFailures", verifyFailures),
                    ("bytes", report.TotalBytesDownloaded),
                    ("seconds", Math.Round(report.Elapsed.TotalSeconds, 2)));
                return (int)CliExitCode.Verify;
            }

            session.Output.Result(true,
                ("files", report.Files.Count),
                ("bytes", report.TotalBytesDownloaded),
                ("localReuse", report.Files.Count(f => f.IsLocalReuse)),
                ("seconds", Math.Round(report.Elapsed.TotalSeconds, 2)));
            return (int)CliExitCode.Ok;
        }
        catch (OperationCanceledException)
        {
            session.Output.EndProgressLine();
            session.Output.Error("canceled", "下载被中断");
            session.Output.Result(false, ("code", "canceled"));
            return (int)CliExitCode.Canceled;
        }
        catch (DownloadFailedException ex)
        {
            session.Output.EndProgressLine();
            foreach (var (path, errors) in ex.ErrorsByFile)
                session.Output.Error("download_failed", path, ("errors", errors.Select(e => e.Message).ToArray()));

            // 文件已落盘但校验不过 → 退出码 2；否则按网络失败处理
            var verifyFailures = VerifyAll(requests, session.Output);
            var code = verifyFailures > 0 ? CliExitCode.Verify : CliExitCode.Network;
            session.Output.Result(false, ("code", code == CliExitCode.Verify ? "verify_failed" : "download_failed"));
            return (int)code;
        }
        catch (ArgumentException ex)
        {
            session.Output.EndProgressLine();
            session.Output.Error("bad_argument", ex.Message);
            session.Output.Result(false, ("code", "bad_argument"));
            return (int)CliExitCode.Argument;
        }
    }

    /// <summary>下载完成后重新跑一遍校验，作为 B1 的「verify 被走到」证据。</summary>
    private static int VerifyAll(IReadOnlyList<DownloadFileRequest> requests, CliOutput output)
    {
        var failures = 0;
        foreach (var request in requests)
        {
            var rule = request.Check;
            if (rule is null) continue;
            var error = rule.Validate(request.LocalPath);
            if (error is null)
            {
                output.Event("verify", ("path", request.LocalPath), ("ok", true));
                output.WriteLog("INFO", $"verify ok: {request.LocalPath}");
            }
            else
            {
                failures++;
                output.Event("verify", ("path", request.LocalPath), ("ok", false), ("error", error));
                output.WriteLog("WARN", $"verify failed: {request.LocalPath}｜{error}");
                output.Say("校验未通过：" + error);
            }
        }
        return failures;
    }

    private static IReadOnlyList<DownloadFileRequest> BuildRequests(CliArguments args)
    {
        var file = args.Get("file");
        return file is not null ? LoadFromFile(file, args) : FromInline(args);
    }

    private static IReadOnlyList<DownloadFileRequest> FromInline(CliArguments args)
    {
        var urls = args.GetAll("url").ToList();
        if (urls.Count == 0)
            throw new CliUsageException("缺少下载源：请用 --url 指定，或用 --file 指定请求文件");

        var to = args.Require("to", "本地保存路径（含文件名）");
        var check = CliSession.BuildCheck(args);

        return
        [
            new DownloadFileRequest
            {
                Sources = urls,
                LocalPath = to,
                Check = check,
                SimulateBrowserHeaders = args.GetBool("browser"),
            },
        ];
    }

    private static IReadOnlyList<DownloadFileRequest> LoadFromFile(string path, CliArguments args)
    {
        if (!File.Exists(path)) throw new CliUsageException($"请求文件不存在：{path}");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new CliUsageException("请求文件不是合法 JSON：" + ex.Message);
        }

        var array = root switch
        {
            JsonArray direct => direct,
            JsonObject obj when obj["files"] is JsonArray files => files,
            _ => throw new CliUsageException("请求文件应为数组，或含 files 数组的对象"),
        };

        var result = new List<DownloadFileRequest>();
        foreach (var node in array)
        {
            if (node is not JsonObject item) continue;

            var to = item["to"]?.GetValue<string>()
                     ?? throw new CliUsageException("请求文件的每一项都必须有 to 字段");
            var sources = (item["sources"] as JsonArray)?.Select(s => s?.GetValue<string>())
                              .Where(s => !string.IsNullOrWhiteSpace(s))
                              .Select(s => s!)
                              .ToList()
                          ?? throw new CliUsageException($"请求文件项 {to} 缺少 sources 数组");

            var hash = (item["sha512"] ?? item["sha256"] ?? item["sha1"] ?? item["md5"])?.GetValue<string>();
            var size = item["size"] is JsonValue sizeNode && sizeNode.TryGetValue<long>(out var parsedSize)
                ? parsedSize
                : -1;

            result.Add(new DownloadFileRequest
            {
                Sources = sources,
                LocalPath = to,
                Check = hash is null && size < 0 ? null : new FileCheckRule { Hash = hash, ActualSize = size },
                SimulateBrowserHeaders = item["browserHeaders"]?.GetValue<bool>() ?? args.GetBool("browser"),
            });
        }

        if (result.Count == 0) throw new CliUsageException("请求文件里没有任何有效条目");
        return result;
    }
}
