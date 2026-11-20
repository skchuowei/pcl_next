using System.Globalization;
using System.Text.Json;
using PCL.Core.Link;

namespace PCL.Cli.Commands;

/// <summary>
/// 联机命令（CLI 无 GUI 联机通道）：建房 / 加入 / 状态 / 停止 / 探测 / 回显 / 安装 / 自检。
/// <para>
/// 设计目标：让「云端 Linux（无 GUI）+ 本地 Windows」能纯命令行跑通 EasyTier overlay 与端口转发，
/// 不依赖启动器界面、不启动游戏。会话状态落在 <c>&lt;workdir&gt;\link\session.json</c>，
/// 每次命令都是独立进程，核心进程由启动脚本留在后台。
/// </para>
/// </summary>
internal static class LinkCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static string Usage => """
        联机（EasyTier overlay，无 GUI）：
          pclcli link install                        安装联机模块（按平台下载 EasyTier 发行包）
          pclcli link create [--port 25565]          建房：固定 overlay IP，输出邀请码
                  [--name 5位] [--secret 5位] [--prefix 8位hex] [--secure]
                  [--overlay-ip 10.114.114.114] [--protocol tcp|udp]
                  [--peer <url>]... [--direct <ip:port>]... [--wait 20]
          pclcli link join <邀请码> [--port 25565]   加入：连房主并把房主端口转发到本地
                  [--local-port 25565] [--overlay-ip 10.114.114.114]
                  [--direct <ip:port>] [--peer <url>]... [--wait 60]
          pclcli link status                         查看当前会话、节点状态与 peer 列表
          pclcli link stop                           停止当前会话（含残留核心进程）
          pclcli link probe --target <ip:port>       对目标打一次 TCP 回显探测
                  [--payload <文本>] [--timeout 3000]
          pclcli link echo --port <端口> [--once]    起一个 TCP 回显服务（替代 MC 服务端做链路验证）
          pclcli link selftest                       离线自检（邀请码/状态文件/端口分配）

        说明：
          · 只有 --no-tun 模式，房主必须用固定 overlay IP（默认 10.114.114.114），
            加入者用 --local-port 连 127.0.0.1 即经 overlay 转到房主 --port。
          · 公共节点不可达时用 --direct 指向对方公网 IP:监听端口（需对方端口可达）。
          · 服务器没开游戏也能验证：房主跑 link echo，加入者跑 link probe。
        """;

    public static async Task<int> RunAsync(CliSession session)
    {
        var args = session.Args;
        string sub = args.Positionals.Count > 0 ? args.Positionals[0].ToLowerInvariant() : "";
        var rest = args.Positionals.Count > 1 ? args.Positionals[1] : null;

        try
        {
            return sub switch
            {
                "install" => await InstallAsync(session).ConfigureAwait(false),
                "create" => await CreateAsync(session).ConfigureAwait(false),
                "join" => rest is null ? throw new CliUsageException("join 需要邀请码：link join <邀请码>")
                    : await JoinAsync(session, rest).ConfigureAwait(false),
                "status" => await StatusAsync(session).ConfigureAwait(false),
                "stop" => Stop(session),
                "probe" => await ProbeAsync(session).ConfigureAwait(false),
                "echo" => await EchoAsync(session).ConfigureAwait(false),
                "selftest" => SelfTest(session),
                _ => throw new CliUsageException("未知 link 子命令，可选 install / create / join / status / stop / probe / echo / selftest")
            };
        }
        catch (CliUsageException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            session.Output.Error("link_timeout", ex.Message);
            session.Output.Result(false, ("code", "link_timeout"));
            return (int)CliExitCode.LinkFailed;
        }
        catch (InvalidOperationException ex)
        {
            session.Output.Error("link_busy", ex.Message);
            session.Output.Result(false, ("code", "link_busy"));
            return (int)CliExitCode.LinkFailed;
        }
        catch (ArgumentException ex)
        {
            session.Output.Error("bad_argument", ex.Message);
            session.Output.Result(false, ("code", "bad_argument"));
            return (int)CliExitCode.Argument;
        }
    }

    // ── install ─────────────────────────────────────────────────────────────

    private static async Task<int> InstallAsync(CliSession session)
    {
        session.Output.Say($"EasyTier {PclLinkEasyTier.Version}，发行包 {PclLinkEasyTier.PackageName()}");
        session.Output.Say($"安装目录：{PclLinkEasyTier.RootDir(session.WorkDir)}");
        try
        {
            await PclLinkEasyTier.EnsureAsync(session.WorkDir,
                message => session.Output.Say(message, alwaysLog: false), session.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Output.Error("link_install_failed", ex.Message);
            session.Output.Result(false, ("code", "link_install_failed"));
            return (int)CliExitCode.Network;
        }

        session.Output.Event("link", ("mode", "install"),
            ("ready", true),
            ("core", PclLinkEasyTier.CorePath(session.WorkDir)),
            ("cli", PclLinkEasyTier.CliPath(session.WorkDir)));
        session.Output.Result(true, ("ready", true));
        return (int)CliExitCode.Ok;
    }

    // ── create / join ───────────────────────────────────────────────────────

    private static async Task<int> CreateAsync(CliSession session)
    {
        var args = session.Args;
        var options = new PclLinkSession.CreateOptions
        {
            Name = NormalizeCodePart(args.Get("name"), PclLinkCode.NameLength, "网名"),
            Secret = NormalizeCodePart(args.Get("secret"), PclLinkCode.SecretLength, "密钥"),
            Prefix = NormalizeCodePart(args.Get("prefix"), PclLinkCode.PrefixLength, "前缀", hex: true),
            OverlayIp = args.Get("overlay-ip") ?? "10.114.114.114",
            ServicePort = args.GetInt("port") ?? 25565,
            SecureMode = args.GetBool("secure"),
            Protocol = NormalizeProtocol(args.Get("protocol")),
            Peers = args.GetAll("peer"),
            DirectPeers = args.GetAll("direct"),
            WaitSeconds = args.GetInt("wait") ?? 20
        };

        var state = await PclLinkSession.CreateAsync(session.WorkDir, options,
            message => session.Output.Say(message, alwaysLog: false), session.Token).ConfigureAwait(false);

        session.Output.Event("link",
            ("mode", "host"),
            ("inviteCode", state.InviteCode),
            ("network", state.NetworkName),
            ("overlayIp", state.OverlayIp),
            ("servicePort", state.ServicePort),
            ("listenPort", state.ListenPort),
            ("rpcPort", state.RpcPort),
            ("pid", state.Pid),
            ("log", state.LogPath));
        session.Output.Say($"建房成功，邀请码：{state.InviteCode}");
        session.Output.Say($"房主 overlay 地址：{state.OverlayIp}:{state.ServicePort}（加入者 --port {state.ServicePort}）");
        session.Output.Say($"EasyTier 日志：{state.LogPath}");
        session.Output.Result(true,
            ("inviteCode", state.InviteCode),
            ("overlayIp", state.OverlayIp),
            ("servicePort", state.ServicePort));
        return (int)CliExitCode.Ok;
    }

    private static async Task<int> JoinAsync(CliSession session, string code)
    {
        var args = session.Args;
        var options = new PclLinkSession.JoinOptions
        {
            Code = code,
            ServicePort = args.GetInt("port"),
            LocalPort = args.GetInt("local-port"),
            OverlayIp = args.Get("overlay-ip") ?? "10.114.114.114",
            Direct = args.Get("direct"),
            Protocol = NormalizeProtocol(args.Get("protocol")),
            Peers = args.GetAll("peer"),
            WaitSeconds = args.GetInt("wait") ?? 60
        };

        var state = await PclLinkSession.JoinAsync(session.WorkDir, options,
            message => session.Output.Say(message, alwaysLog: false), session.Token).ConfigureAwait(false);

        session.Output.Event("link",
            ("mode", "join"),
            ("network", state.NetworkName),
            ("overlayIp", state.OverlayIp),
            ("servicePort", state.ServicePort),
            ("localJoinPort", state.LocalJoinPort),
            ("listenPort", state.ListenPort),
            ("rpcPort", state.RpcPort),
            ("pid", state.Pid),
            ("log", state.LogPath));
        session.Output.Say($"已加入 {state.NetworkName}");
        session.Output.Say($"本地转发：127.0.0.1:{state.LocalJoinPort} → {state.OverlayIp}:{state.ServicePort}");
        session.Output.Say($"验证：pclcli link probe --target 127.0.0.1:{state.LocalJoinPort}");
        session.Output.Result(true,
            ("network", state.NetworkName),
            ("localJoinPort", state.LocalJoinPort));
        return (int)CliExitCode.Ok;
    }

    // ── status / stop ───────────────────────────────────────────────────────

    private static async Task<int> StatusAsync(CliSession session)
    {
        var state = PclLinkSession.Load(session.WorkDir);
        if (state is null)
        {
            session.Output.Error("no_session", "当前没有联机会话（先执行 link create 或 link join）");
            session.Output.Result(false, ("code", "no_session"));
            return (int)CliExitCode.LinkFailed;
        }

        bool alive = PclLinkEasyTier.IsAlive(state.Pid);
        bool nodeAlive = alive &&
                         await PclLinkEasyTier.IsNodeAliveAsync(session.WorkDir, state.RpcPort, session.Token)
                             .ConfigureAwait(false);
        var peers = nodeAlive ? await PclLinkSession.PeersAsync(session.WorkDir, state, session.Token)
            .ConfigureAwait(false) : null;
        var rawPeers = nodeAlive
            ? await PclLinkEasyTier.QueryAsync(session.WorkDir, state.RpcPort, new[] { "peer", "list" }, session.Token)
                .ConfigureAwait(false)
            : null;
        bool hostPresent = rawPeers is not null &&
                           PclLinkEasyTier.HasRoomHost(rawPeers, "pclcli-host-", state.OverlayIp);

        session.Output.Event("link",
            ("mode", state.Mode),
            ("inviteCode", state.InviteCode),
            ("network", state.NetworkName),
            ("overlayIp", state.OverlayIp),
            ("servicePort", state.ServicePort),
            ("localJoinPort", state.LocalJoinPort == 0 ? null : state.LocalJoinPort),
            ("pid", state.Pid),
            ("processAlive", alive),
            ("nodeAlive", nodeAlive),
            ("remotePeers", peers?.Remote),
            ("hostPresent", hostPresent),
            ("startedAt", state.StartedAt));

        if (peers is not null)
        {
            foreach (var peer in peers.Value.Peers)
            {
                session.Output.Item(
                    ("ip", peer.Ip),
                    ("hostname", peer.Hostname),
                    ("cost", peer.Cost),
                    ("latencyMs", peer.LatencyMs < 0 ? null : peer.LatencyMs));
            }
        }

        session.Output.Say(alive
            ? $"会话运行中：{state.Mode} / {state.NetworkName}，远端节点 {peers?.Remote.ToString() ?? "?"}"
            : $"会话进程已退出（PID {state.Pid}），可执行 link stop 清理状态后重建");
        session.Output.Result(alive && nodeAlive,
            ("mode", state.Mode),
            ("processAlive", alive),
            ("nodeAlive", nodeAlive),
            ("remotePeers", peers?.Remote));
        return alive && nodeAlive ? (int)CliExitCode.Ok : (int)CliExitCode.LinkFailed;
    }

    private static int Stop(CliSession session)
    {
        var state = PclLinkSession.Load(session.WorkDir);
        bool stopped = PclLinkSession.Stop(session.WorkDir, out state);
        if (state is null)
        {
            session.Output.Error("no_session", "当前没有联机会话");
            session.Output.Result(false, ("code", "no_session"));
            return (int)CliExitCode.LinkFailed;
        }

        session.Output.Say(stopped ? $"已停止联机模块（PID {state.Pid}）" : $"会话进程 {state.Pid} 已不存在，只清理了状态文件");
        session.Output.Result(true, ("stopped", stopped), ("pid", state.Pid));
        return (int)CliExitCode.Ok;
    }

    // ── probe / echo ────────────────────────────────────────────────────────

    private static async Task<int> ProbeAsync(CliSession session)
    {
        string target = session.Args.Require("target", "目标 ip:port");
        int colon = target.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(target[(colon + 1)..], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int port))
            throw new CliUsageException($"--target 需要 ip:port 形式，实际为「{target}」");

        string host = target[..colon];
        string payload = session.Args.Get("payload") ?? "pclcli-link-probe";
        int timeout = session.Args.GetInt("timeout") ?? 3000;

        try
        {
            var result = await PclLinkSession.ProbeAsync(host, port, payload, timeout, session.Token)
                .ConfigureAwait(false);
            session.Output.Event("link",
                ("mode", "probe"),
                ("remote", result.Remote),
                ("ok", result.Ok),
                ("connectMs", result.ConnectMs),
                ("roundTripMs", result.RoundTripMs),
                ("sent", result.Sent),
                ("received", result.Received));
            session.Output.Say(result.Ok
                ? $"探测成功：{result.Remote} 回显一致（连接 {result.ConnectMs} ms，往返 {result.RoundTripMs} ms）"
                : $"探测失败：回显不一致（收到「{result.Received}」）");
            session.Output.Result(result.Ok, ("remote", result.Remote), ("roundTripMs", result.RoundTripMs));
            return result.Ok ? (int)CliExitCode.Ok : (int)CliExitCode.LinkFailed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Output.Error("probe_failed", $"连不上 {target}：{ex.Message}");
            session.Output.Result(false, ("code", "probe_failed"), ("remote", target));
            return (int)CliExitCode.LinkFailed;
        }
    }

    private static async Task<int> EchoAsync(CliSession session)
    {
        int port = session.Args.GetInt("port") ?? throw new CliUsageException("echo 需要 --port <端口>");
        if (port is < 1 or > 65535)
            throw new CliUsageException("--port 需在 1~65535 之间");
        bool once = session.Args.GetBool("once");

        await PclLinkSession.RunEchoAsync(port, once,
            message => session.Output.Say(message, alwaysLog: false), session.Token).ConfigureAwait(false);
        session.Output.Result(true, ("port", port), ("once", once));
        return (int)CliExitCode.Ok;
    }

    // ── selftest ────────────────────────────────────────────────────────────

    private static int SelfTest(CliSession session)
    {
        int passed = 0;
        int failed = 0;
        void Check(string name, bool ok, string? detail = null)
        {
            if (ok)
            {
                passed++;
                session.Output.Say($"[通过] {name}");
            }
            else
            {
                failed++;
                session.Output.Say($"[失败] {name}" + (detail is null ? "" : $"：{detail}"));
            }
        }

        try
        {
            var identity = PclLinkCode.NewIdentity();
            string code = PclLinkCode.Build(identity.Name, identity.Secret, identity.Prefix);
            var info = PclLinkCode.Parse(code);
            Check("邀请码往返", info.FullNetworkName == "pclnext-" + identity.Name &&
                              info.FullSecret == identity.Secret + identity.Prefix, code);

            var vector = PclLinkCode.Parse("K7M3Q-XB4T9-3fa8c1d0");
            Check("固定向量", vector.FullSecret == "XB4T93fa8c1d0" && vector.Prefix == "3fa8c1d0");

            var legacy = PclLinkCode.Parse("ABCDEFGHJ-XB4T9-63DD");
            Check("旧格式兼容", legacy.IsLegacy && legacy.Port == 25565, $"port={legacy.Port}");

            Check("错误码拒绝", !PclLinkCode.TryParse("BAD-CODE", out _));
            Check("安全模式尾段", PclLinkCode.Parse(code + "-S").SecureMode);

            string tempDir = Path.Combine(Path.GetTempPath(), "pclcli-link-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var state = new PclLinkSession.LinkState
                {
                    Mode = "host",
                    InviteCode = code,
                    NetworkName = info.FullNetworkName,
                    NetworkSecret = info.FullSecret,
                    OverlayIp = "10.114.114.114",
                    ServicePort = 25565,
                    RpcPort = 30001,
                    Pid = 1234,
                    StartedAt = "2026-11-20 20:00:00"
                };
                PclLinkSession.Save(tempDir, state);
                var loaded = PclLinkSession.Load(tempDir);
                Check("状态文件往返", loaded is { Mode: "host", ServicePort: 25565, RpcPort: 30001 });

                int p1 = PclLinkEasyTier.FindFreePort(30000);
                int p2 = PclLinkEasyTier.FindFreePort(30000);
                Check("端口分配不重复", p1 != p2, $"{p1} / {p2}");

                session.Output.Event("link", ("mode", "selftest"),
                    ("passed", passed), ("failed", failed), ("workdir", tempDir));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
            }
        }
        catch (Exception ex)
        {
            failed++;
            session.Output.Error("selftest_failed", ex.Message);
        }

        session.Output.Say($"自检完成：通过 {passed}，失败 {failed}");
        session.Output.Result(failed == 0, ("passed", passed), ("failed", failed));
        return failed == 0 ? (int)CliExitCode.Ok : (int)CliExitCode.Internal;
    }

    // ── 参数校验 ────────────────────────────────────────────────────────────

    private static string? NormalizeCodePart(string? value, int length, string label, bool hex = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = value.Trim().ToUpperInvariant();
        bool ok = text.Length == length && text.All(c =>
            hex ? char.IsAsciiHexDigit(c) : "ABCDEFGHJKMNPQRSTUVWXYZ23456789".IndexOf(c) >= 0);
        if (!ok)
            throw new CliUsageException(
                $"起的{label}不合法：需 {length} 位" + (hex ? "十六进制" : "无歧义字母数字（去掉 0/O、1/I/L）"));
        return hex ? text.ToLowerInvariant() : text;
    }

    private static string NormalizeProtocol(string? value)
        => string.Equals(value, "udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
}
