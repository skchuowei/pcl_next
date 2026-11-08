using System.Text;
using PCL.Cli.Commands;

namespace PCL.Cli;

/// <summary>
/// CLI 入口：参数分发 + 统一异常到退出码的映射（《计划-内核与GUI拆分（CLI）》§5.1）。
/// <para/>
/// 不做 WPF 进程内模式、不做交互式 TUI、不做自动更新（计划 §4「明确不做」）。
/// </para>
/// </summary>
internal static class Program
{
    internal const string ToolName = "pclcli";
    internal const string ToolVersion = "0.1.0";

    private static async Task<int> Main(string[] argv)
    {
        TryUseUtf8Console();

        string? command = null;
        CliArguments? args = null;
        try
        {
            command = argv.Length > 0 && !argv[0].StartsWith('-') ? argv[0].ToLowerInvariant() : "";
            args = CliArguments.Parse(argv.Length > 0 && !argv[0].StartsWith('-') ? argv[1..] : argv);

            if (args.Has("help") || args.Has("h") || command == "help")
            {
                Console.Out.Write(HelpText);
                return (int)CliExitCode.Ok;
            }

            if (args.Has("version") || args.Has("v") || command == "version")
            {
                Console.Out.WriteLine($"{ToolName} {ToolVersion}（内核前端，PCL.Download 通道）");
                return (int)CliExitCode.Ok;
            }

            if (command is null or "")
            {
                Console.Error.Write(HelpText);
                return (int)CliExitCode.Argument;
            }

            if (command is not ("download" or "install" or "loader" or "resource" or "selftest"))
                throw new CliUsageException($"未知命令「{command}」，可选 download / install / loader / resource / selftest");

            using var session = CliSession.Create(command, args);
            return command switch
            {
                "download" => await DownloadCommand.RunAsync(session).ConfigureAwait(false),
                "install" => await InstallCommand.RunAsync(session).ConfigureAwait(false),
                "loader" => await InstallLoaderCommand.RunAsync(session).ConfigureAwait(false),
                "resource" => await ResourceCommand.RunAsync(session).ConfigureAwait(false),
                "selftest" => await SelfTestCommand.RunAsync(session).ConfigureAwait(false),
                _ => throw new CliUsageException("未知命令：" + command),
            };
        }
        catch (CliUsageException ex)
        {
            return Fail(command ?? "", "bad_argument", ex.Message, CliExitCode.Argument);
        }
        catch (OperationCanceledException)
        {
            return Fail(command ?? "", "canceled", "操作被中断", CliExitCode.Canceled);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return Fail(command ?? "", "internal_error", ex.Message, CliExitCode.Internal);
        }
    }

    /// <summary>统一失败出口：stdout 一条 error + 一条 result，stderr 一行人读。</summary>
    private static int Fail(string command, string code, string message, CliExitCode exitCode)
    {
        Console.Error.WriteLine($"错误：{message}");
        using var output = new CliOutput(command, null, quiet: false);
        output.Error(code, message);
        output.Result(false, ("code", code));
        return (int)exitCode;
    }

    private static void TryUseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // 某些重定向场景不允许改编码，保持默认即可
        }
        catch (PlatformNotSupportedException)
        {
            // 同上
        }
    }

    private static string HelpText => $$"""
        {{ToolName}} {{ToolVersion}} —— pcl-next 的 CLI 内核通道（无 GUI、无窗口）

        用法：
          {{ToolName}} <命令> [参数...]

        命令：
          download    下载文件（支持多源、分片、续传、校验）
          install     按版本 JSON 拉取 client / libraries / assets 的下载段
          loader      拼装原版 + Fabric/Forge/NeoForge 完整实例（下载依赖并合并版本 JSON）
          resource    资源站联网查询（search / versions / fingerprint）
          selftest    自检套件（offline / b1 / b2 / b5 / all，默认 offline）
          help        显示本帮助
          version     显示版本

        {{DownloadCommand.Usage.Replace("pclcli", ToolName)}}

        {{InstallCommand.Usage.Replace("pclcli", ToolName)}}

        {{InstallLoaderCommand.Usage.Replace("pclcli", ToolName)}}

        {{ResourceCommand.Usage.Replace("pclcli", ToolName)}}

        {{SelfTestCommand.Usage.Replace("pclcli", ToolName)}}

        全局参数（所有命令通用）：
          --workdir <目录>      工作目录与日志落点（默认 %TEMP%\pclcli）
          --log <文件>          指定日志文件（默认 <workdir>\cli-<cmd>-<时间>.log）
          --no-log              不写日志文件
          --quiet, -q           抑制 stderr 人读输出（stdout 的 JSON 不受影响）
          --policy-file <ini>   显式指定 DownloadPolicy.ini
          --mirror <模式>       auto（默认）/ official / mirror
          --concurrency <n>     下载并发上限（默认 33）
          --speed-limit <B/s>   限速，<=0 表示不限（默认 -1）

        退出码：
          0 成功   1 网络失败   2 校验失败   3 参数错误
          4 策略禁用   5 资源站不可用   6 内部错误   130 中断

        输出约定：
          stdout 只输出 JSON Lines（末行必为 result 事件）；stderr 为人读进度；
          日志落在 --workdir 下，B1 的分片/续传/校验证据以日志为准。
        """;
}
