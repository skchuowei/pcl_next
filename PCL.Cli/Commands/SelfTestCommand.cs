using System.Runtime.InteropServices;
using PCL.Download;
using PCL.Download.Platform;

namespace PCL.Cli.Commands;

/// <summary>
/// <c>pclcli selftest</c>：把 dev.4 的人工验收门槛变成一条命令（《计划-内核与GUI拆分（CLI）》§1）。
/// <para/>
/// 套件：
/// <list type="bullet">
/// <item><c>offline</c>：不联网。哈希、校验规则、策略、版本 JSON 解析、资源注册表、架构护栏自检。</item>
/// <item><c>b1</c>：真实网络。清单 → 最新正式版 → client jar（≥20MB，必然走到分片/续传/校验）。</item>
/// <item><c>b2</c>：真实网络。按版本 JSON 下载 client + assets 索引到临时实例目录。</item>
/// <item><c>b5</c>：真实网络。中英文各搜一次 + 对 b1 产物做指纹反查。</item>
/// </list>
/// </para>
/// <para/>
/// 套件内部**直接调用真实命令**（不是另写一套逻辑），因此 selftest 通过即证明 CLI 表面可用。
/// </para>
/// </summary>
internal static class SelfTestCommand
{
    public const string Usage = """
        pclcli selftest [--suite offline|b1|b2|b5|all] [--workdir <临时目录>]

        默认 offline（不联网，CI 可跑）。all = offline,b1,b2,b5。
        """;

    public static async Task<int> RunAsync(CliSession session)
    {
        var suites = ParseSuites(session.Args.Get("suite"));
        session.Output.Say($"自检套件：{string.Join(", ", suites)}（工作目录 {session.WorkDir}）");

        foreach (var suite in suites)
        {
            session.Token.ThrowIfCancellationRequested();
            session.Output.Event("suite", ("name", suite), ("phase", "start"));
            session.Output.Say($"──── 套件 {suite} ────");

            var code = suite switch
            {
                "offline" => RunOffline(session),
                "b1" => await RunB1Async(session).ConfigureAwait(false),
                "b2" => await RunChildAsync(session, "install",
                [
                    "--dir", Path.Combine(session.WorkDir, "instance"),
                    "--version-id", "latest",
                    "--only", "client,assets",
                ]).ConfigureAwait(false),
                "b5" => await RunB5Async(session).ConfigureAwait(false),
                _ => (int)CliExitCode.Argument,
            };

            session.Output.Event("suite", ("name", suite), ("phase", "end"), ("code", code));
            if (code == (int)CliExitCode.Ok) continue;

            session.Output.Result(false, ("code", "suite_failed"), ("suite", suite), ("suiteCode", code));
            return code;
        }

        session.Output.Result(true, ("suites", suites));
        return (int)CliExitCode.Ok;
    }

    private static List<string> ParseSuites(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ["offline"];

        var result = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "all" or "full": result.AddRange(["offline", "b1", "b2", "b5"]); break;
                case "offline" or "o": result.Add("offline"); break;
                case "b1": result.Add("b1"); break;
                case "b2": result.Add("b2"); break;
                case "b5": result.Add("b5"); break;
                default: throw new CliUsageException($"--suite 不支持「{part}」，可选 offline / b1 / b2 / b5 / all");
            }
        }

        return result.Distinct().ToList();
    }

    // ── offline：不联网的机械自检 ────────────────────────────────────────────

    private static int RunOffline(CliSession session)
    {
        var failures = 0;

        failures += Check(session, "arch", CheckArchitecture);
        failures += Check(session, "hash", CheckHash);
        failures += Check(session, "file-check-rule", CheckFileRule);
        failures += Check(session, "policy", CheckPolicy);
        failures += Check(session, "version-json", CheckVersionJson);
        failures += Check(session, "resource-registry", CheckResourceRegistry);
        failures += Check(session, "platform", CheckPlatform);

        if (failures > 0)
        {
            session.Output.Error("offline_failed", $"{failures} 项自检未通过");
            return (int)CliExitCode.Internal;
        }

        session.Output.Say("离线段全部通过");
        return (int)CliExitCode.Ok;
    }

    /// <summary>跑一项检查，输出 check 事件；返回 0 通过 / 1 失败。</summary>
    private static int Check(CliSession session, string name, Func<string?> probe)
    {
        var error = probe();
        session.Output.Event("check", ("name", name), ("ok", error is null), ("error", error));
        if (error is null)
        {
            session.Output.WriteLog("INFO", $"check ok: {name}");
            session.Output.Say($"  ✓ {name}");
            return 0;
        }

        session.Output.WriteLog("WARN", $"check failed: {name}｜{error}");
        session.Output.Say($"  ✗ {name}：{error}");
        return 1;
    }

    /// <summary>架构护栏的运行期版本：进程里不得出现任何 WPF / GUI 宿主程序集。</summary>
    private static string? CheckArchitecture()
    {
        string[] forbidden =
        [
            "PresentationFramework", "PresentationCore", "WindowsBase",
            "System.Windows.Forms", "PCLCS", "MeloongCore", "MeloongCore.Wpf", "pcl-next",
        ];

        var own = typeof(DownloadEngine).Assembly;
        var referenced = own.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => forbidden.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (referenced.Count > 0)
            return $"PCL.Download 引用了 GUI 程序集：{string.Join(", ", referenced)}";

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? "")
            .Where(n => forbidden.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return loaded.Count > 0 ? $"进程已加载 GUI 程序集：{string.Join(", ", loaded)}" : null;
    }

    private static string? CheckHash()
    {
        var path = Path.Combine(Path.GetTempPath(), "pclcli-selftest-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllText(path, "hello");
            var sha1 = HashHelper.ComputeFileHash(path, 40);
            if (sha1 != "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d") return $"SHA1 计算结果不符：{sha1}";

            var md5 = HashHelper.ComputeFileHash(path, 32);
            return md5 == "5d41402abc4b2a76b9719d911017c592" ? null : $"MD5 计算结果不符：{md5}";
        }
        finally
        {
            try { File.Delete(path); } catch { /* 自检产物清理失败不影响结论 */ }
        }
    }

    private static string? CheckFileRule()
    {
        var path = Path.Combine(Path.GetTempPath(), "pclcli-selftest-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllText(path, "hello");

            var good = new FileCheckRule
            {
                Hash = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d",
                ActualSize = 5,
            };
            if (good.Validate(path) is { } error) return "正确哈希应通过校验，实际报：" + error;

            var bad = new FileCheckRule { Hash = new string('0', 40) };
            if (bad.Validate(path) is null) return "错误哈希应校验失败，实际却通过了";

            var missing = new FileCheckRule { ActualSize = 999 };
            return missing.Validate(path) is null ? "错误大小应校验失败，实际却通过了" : null;
        }
        finally
        {
            try { File.Delete(path); } catch { /* 同上 */ }
        }
    }

    private static string? CheckPolicy()
    {
        var policy = CliPolicy.Resolve();
        if (string.IsNullOrWhiteSpace(policy.Source)) return "策略来源为空，无法核对";
        return policy.AllowMcDownload ? null : "当前策略禁用了 MC 下载（b1/b2 套件将不可用）";
    }

    private static string? CheckVersionJson()
    {
        // 三条库分别覆盖：无 rules / 仅 Windows 生效 / 非 Windows 生效
        const string json = """
        {
          "id": "1.20.1-selftest",
          "downloads": { "client": { "url": "https://example.invalid/client.jar", "sha1": "0000000000000000000000000000000000000000", "size": 1024 } },
          "assetIndex": { "id": "5", "url": "https://example.invalid/index.json", "sha1": "", "size": 0 },
          "logging": { "client": { "file": { "id": "client-1.12.xml", "url": "https://example.invalid/log.xml", "sha1": "", "size": 0 } } },
          "libraries": [
            { "name": "a:no-rules:1", "downloads": { "artifact": { "path": "a/no-rules/1/no-rules-1.jar", "url": "https://example.invalid/a.jar", "sha1": "", "size": 0 } } },
            { "name": "b:windows-only:1", "rules": [ { "action": "allow", "os": { "name": "windows" } } ], "downloads": { "artifact": { "path": "b/windows/1/w.jar", "url": "https://example.invalid/b.jar", "sha1": "", "size": 0 } } },
            { "name": "c:osx-only:1", "rules": [ { "action": "allow", "os": { "name": "osx" } } ], "downloads": { "artifact": { "path": "c/osx/1/c.jar", "url": "https://example.invalid/c.jar", "sha1": "", "size": 0 } } }
          ]
        }
        """;

        var version = VersionJson.Parse(json);
        if (version.Id != "1.20.1-selftest") return "id 解析错误";
        if (version.Client is null || version.Client.Size != 1024) return "downloads.client 解析错误";
        if (version.AssetIndex?.Id != "5") return "assetIndex 解析错误";
        if (version.LoggingClient?.Id != "client-1.12.xml") return "logging.client.file 解析错误";
        if (version.Libraries.Count != 3) return $"libraries 数量应为 3，实际 {version.Libraries.Count}";

        var windowsOnly = version.Libraries[1];
        var osxOnly = version.Libraries[2];
        if (VersionJson.CurrentOsName == "windows" && !windowsOnly.AppliesHere) return "Windows 专属库未判定为生效";
        if (VersionJson.CurrentOsName == "windows" && osxOnly.AppliesHere) return "osx 专属库被误判为生效";
        return null;
    }

    private static string? CheckResourceRegistry()
    {
        var registry = ResourceProviderRegistry.CreateDefault();
        var resolved = registry.ResolveSources(ResourcePlatforms.Any, out var notice);
        if (resolved == 0) return "注册表未解析出任何可用资源站";
        // notice 非空是正常降级（未配置 CurseForge Key），不视为失败
        _ = notice;
        return null;
    }

    /// <summary>
    /// 平台能力层自检（<see cref="PlatformInfo"/> / <see cref="CrossPlatformPath"/> / <see cref="PathComparison"/>）。
    /// <para/>
    /// 纯离线、不写文件。Linux 上跑这条才算真正验证「内核没有 Windows 假设」——
    /// <c>build-linux</c> job 的 <c>selftest --suite offline</c> 就是它的门禁。
    /// </para>
    /// </summary>
    private static string? CheckPlatform()
    {
        // 1) 探测结论必须与运行时一致（映射写反时这里立刻暴露）
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !PlatformInfo.IsWindows)
            return "运行在 Windows 上，PlatformInfo 却判为非 Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !PlatformInfo.IsLinux)
            return "运行在 Linux 上，PlatformInfo 却判为非 Linux";

        // 2) 拼接点必须用平台分隔符；空段忽略、尾分隔符不重复
        var sep = Path.DirectorySeparatorChar;
        var expected = Path.Combine("a", "b");
        if (CrossPlatformPath.Join("a", "", "b") != expected) return "Join 未忽略空段";
        if (CrossPlatformPath.Join("a" + sep, "b") != expected) return "Join 未正确处理尾分隔符";
        if (sep != '/' && CrossPlatformPath.Join("a", "b").Contains('/')) return "Join 用错了分隔符";
        if (CrossPlatformPath.Join("", "") != "") return "全空段应返回空串";

        // 3) 可移植写法：反斜杠归一并幂等
        if (CrossPlatformPath.ToPortable(@"a\b\c.jar") != "a/b/c.jar") return "ToPortable 未把反斜杠归一";
        if (CrossPlatformPath.ToPortable("a/b/c.jar") != "a/b/c.jar") return "ToPortable 改动了正斜杠路径";

        // 4) 比较口径必须与文件系统一致（Linux 区分大小写）
        var ignoreCaseExpected = PlatformInfo.IsWindows || PlatformInfo.IsMacOS;
        var ignoreCaseActual = PathComparison.Comparison == StringComparison.OrdinalIgnoreCase;
        if (ignoreCaseActual != ignoreCaseExpected)
            return $"路径比较口径 {PathComparison.Comparison} 与平台 {PlatformInfo.Kind} 不匹配";
        if (!PathComparison.PathsEqual("mods/a.jar", "mods/a.jar" + sep)) return "PathsEqual 未去掉尾分隔符";

        return null;
    }

    // ── b1 / b5：真实网络 ───────────────────────────────────────────────────

    private static async Task<int> RunB1Async(CliSession session)
    {
        if (!session.Policy.AllowMcDownload) return session.RefuseByPolicy();

        using var engineHolder = new EngineHolder();
        var version = await McVersionResolver
            .ResolveAsync(session, engineHolder.Engine, null, "latest")
            .ConfigureAwait(false);

        if (version.Client is not { Url.Length: > 0 } client)
        {
            session.Output.Error("b1_failed", "最新正式版的版本 JSON 里没有 downloads.client");
            return (int)CliExitCode.Network;
        }

        session.Output.Say($"B1 目标：{version.Id} client jar，{CliSession.FormatSize(client.Size)}");

        return await RunChildAsync(session, "download",
        [
            "--url", client.Url,
            "--to", Path.Combine(session.WorkDir, "client.jar"),
            "--sha1", client.Sha1,
            "--size", client.Size.ToString(),
            "--browser",
        ]).ConfigureAwait(false);
    }

    private static async Task<int> RunB5Async(CliSession session)
    {
        var code = await RunChildAsync(session, "resource", ["search", "--query", "sodium", "--source", "auto"])
            .ConfigureAwait(false);
        if (code != (int)CliExitCode.Ok) return code;

        code = await RunChildAsync(session, "resource", ["search", "--query", "钠", "--source", "auto"])
            .ConfigureAwait(false);
        if (code != (int)CliExitCode.Ok) return code;

        var jar = Path.Combine(session.WorkDir, "client.jar");
        if (!File.Exists(jar))
        {
            session.Output.Notice("未找到 b1 产出的 client.jar，跳过指纹反查（先跑 --suite b1 可覆盖该项）");
            return (int)CliExitCode.Ok;
        }

        return await RunChildAsync(session, "resource", ["fingerprint", "--file", jar, "--source", "auto"])
            .ConfigureAwait(false);
    }

    /// <summary>以全新的子会话跑一条真实命令（子会话有独立日志，失败码原样上抛）。</summary>
    private static async Task<int> RunChildAsync(CliSession parent, string command, string[] argv)
    {
        var full = new List<string>(argv)
        {
            "--workdir", parent.WorkDir,
        };
        if (parent.Args.GetBool("quiet")) full.Add("--quiet");

        using var child = CliSession.Create(command, CliArguments.Parse(full));
        return command switch
        {
            "download" => await DownloadCommand.RunAsync(child).ConfigureAwait(false),
            "install" => await InstallCommand.RunAsync(child).ConfigureAwait(false),
            "resource" => await ResourceCommand.RunAsync(child).ConfigureAwait(false),
            _ => throw new CliUsageException("未支持的子命令：" + command),
        };
    }

    private sealed class EngineHolder : IDisposable
    {
        public IDownloadEngine Engine { get; } = new DownloadEngine();
        public void Dispose() { }
    }
}
