using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Link
{
    /// <summary>
    /// CLI 侧联机会话：建房 / 加入 / 状态 / 停止 / 探测 / 回显。
    /// <para>
    /// 每次 CLI 调用都是独立进程，因此会话状态落在 <c>&lt;workdir&gt;\link\session.json</c>，
    /// EasyTier 核心由启动脚本脱离父进程继续运行，后续命令按状态文件里的 PID 与 rpc-portal 端口查询。
    /// </para>
    /// <para>
    /// 与 GUI 的差别：不做大厅协议、不做 Scaffolding 握手、不启动游戏。
    /// 只验证「overlay 能建成 + 端口转发能通」，用 <see cref="ProbeAsync"/> 对回显服务打一次即可。
    /// </para>
    /// </summary>
    public static class PclLinkSession
    {
        /// <summary>会话状态文件（JSON，字段为 camelCase）。</summary>
        public static string StatePath(string workDir) => Path.Combine(workDir, "link", "session.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>落盘的会话状态。</summary>
        public sealed class LinkState
        {
            /// <summary>host / join。</summary>
            public string Mode { get; set; } = "";
            public string InviteCode { get; set; } = "";
            public string NetworkName { get; set; } = "";
            public string NetworkSecret { get; set; } = "";
            public string OverlayIp { get; set; } = "";
            /// <summary>房主侧被访问的服务端口（回显 / MC 服务端）。</summary>
            public int ServicePort { get; set; }
            /// <summary>加入侧本地转发端口（连 127.0.0.1:LocalJoinPort 就等于连房主 ServicePort）。</summary>
            public int LocalJoinPort { get; set; }
            /// <summary>房主大厅协议端口（从房主主机名后缀拿到；CLI 自建房间为 0）。</summary>
            public int ScaffoldPort { get; set; }
            /// <summary>认证网关端口（0 表示房主没启用网关，走旧直连转发）。</summary>
            public int GatePort { get; set; }
            /// <summary>大厅协议里展示的昵称。</summary>
            public string Nickname { get; set; } = "";
            /// <summary>网关隧道子进程 PID（0 表示没有走网关）。</summary>
            public int TunnelPid { get; set; }
            public int ListenPort { get; set; }
            public int RpcPort { get; set; }
            public int Pid { get; set; }
            public bool SecureMode { get; set; }
            public string? Direct { get; set; }
            public string? LogPath { get; set; }
            public string StartedAt { get; set; } = "";
        }

        public static LinkState? Load(string workDir)
        {
            string path = StatePath(workDir);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<LinkState>(File.ReadAllText(path), JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string workDir, LinkState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath(workDir))!);
            File.WriteAllText(StatePath(workDir), JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false));
        }

        public static void Clear(string workDir)
        {
            try { if (File.Exists(StatePath(workDir))) File.Delete(StatePath(workDir)); } catch { /* 忽略 */ }
        }

        /// <summary>建房参数。</summary>
        public sealed class CreateOptions
        {
            public string? Name { get; init; }
            public string? Secret { get; init; }
            public string? Prefix { get; init; }
            public string OverlayIp { get; init; } = "10.114.114.114";
            public int ServicePort { get; init; }
            public bool SecureMode { get; init; }
            public string Protocol { get; init; } = "tcp";
            public IReadOnlyList<string> Peers { get; init; } = Array.Empty<string>();
            public IReadOnlyList<string> DirectPeers { get; init; } = Array.Empty<string>();
            public int WaitSeconds { get; init; } = 20;
        }

        /// <summary>建房：启动 EasyTier 固定 overlay IP，返回可分享的邀请码。</summary>
        public static async Task<LinkState> CreateAsync(string workDir, CreateOptions options,
            Action<string>? status, CancellationToken ct)
        {
            if (options.ServicePort is < 1 or > 65535)
                throw new ArgumentException("--port 需在 1~65535 之间");

            await PclLinkEasyTier.EnsureAsync(workDir, status, ct).ConfigureAwait(false);

            LinkState? existing = Load(workDir);
            if (existing is not null && PclLinkEasyTier.IsAlive(existing.Pid))
                throw new InvalidOperationException("已有联机会话在运行（先执行 link stop）");

            (string name, string secret, string prefix) = PclLinkCode.NewIdentity();
            name = (options.Name ?? name).ToUpperInvariant();
            secret = (options.Secret ?? secret).ToUpperInvariant();
            prefix = (options.Prefix ?? prefix).ToLowerInvariant();
            string code = PclLinkCode.Build(name, secret, prefix, options.SecureMode);
            PclLinkCode.CodeInfo info = PclLinkCode.Parse(code);

            HashSet<int> reserved = new();
            int rpcPort = PclLinkEasyTier.FindFreePort(30000, reserved);
            int listenPort = PclLinkEasyTier.FindFreePort(30000, reserved);

            List<string> args = BaseArguments(info, rpcPort, listenPort, options.Protocol, options.SecureMode);
            args.Add("-i");
            args.Add(options.OverlayIp);
            args.Add("--hostname");
            args.Add($"pclcli-host-{options.ServicePort}");
            foreach (int port in new[] { options.ServicePort })
            {
                args.Add("--tcp-whitelist");
                args.Add(port.ToString());
                args.Add("--udp-whitelist");
                args.Add(port.ToString());
            }
            AddPeers(args, options.Peers, options.DirectPeers);

            status?.Invoke($"正在启动联机模块（房主，overlay {options.OverlayIp}:{options.ServicePort}）…");
            PclLinkEasyTier.LinkProcess process = PclLinkEasyTier.Launch(workDir, args, "host");
            LinkState state = new()
            {
                Mode = "host",
                InviteCode = code,
                NetworkName = info.FullNetworkName,
                NetworkSecret = info.FullSecret,
                OverlayIp = options.OverlayIp,
                ServicePort = options.ServicePort,
                ListenPort = listenPort,
                RpcPort = rpcPort,
                Pid = process.Pid,
                SecureMode = options.SecureMode,
                LogPath = process.LogPath,
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            bool alive = await WaitAsync(async () =>
                await PclLinkEasyTier.IsNodeAliveAsync(workDir, rpcPort, ct).ConfigureAwait(false),
                options.WaitSeconds, ct).ConfigureAwait(false);
            Save(workDir, state);
            if (!alive)
                throw new TimeoutException($"联机模块 {options.WaitSeconds}s 内没有响应（rpc-portal {rpcPort}），" +
                                           $"请查看日志 {process.LogPath}");
            return state;
        }

        /// <summary>加入参数。</summary>
        public sealed class JoinOptions
        {
            public required string Code { get; init; }
            /// <summary>房主侧服务端口；邀请码为旧格式时可从码里取。</summary>
            public int? ServicePort { get; init; }
            public int? LocalPort { get; init; }
            /// <summary>房主固定 overlay 地址（建房时用 -i 指定的那个）。</summary>
            public string OverlayIp { get; init; } = "10.114.114.114";
            public string? Direct { get; init; }
            public string Protocol { get; init; } = "tcp";
            public IReadOnlyList<string> Peers { get; init; } = Array.Empty<string>();
            public int WaitSeconds { get; init; } = 60;
        }

        /// <summary>加入：启动 EasyTier 成员节点，并把房主服务端口转发到本地。</summary>
        public static async Task<LinkState> JoinAsync(string workDir, JoinOptions options,
            Action<string>? status, CancellationToken ct)
        {
            await PclLinkEasyTier.EnsureAsync(workDir, status, ct).ConfigureAwait(false);

            LinkState? existing = Load(workDir);
            if (existing is not null && PclLinkEasyTier.IsAlive(existing.Pid))
                throw new InvalidOperationException("已有联机会话在运行（先执行 link stop）");

            PclLinkCode.CodeInfo info = PclLinkCode.Parse(PclLinkCode.ExtractCode(options.Code));
            int servicePort = options.ServicePort ?? (info.Port > 0 ? info.Port : 0);
            // 新码里没有端口：先不报错，等连上房主后用大厅协议问出来（房主没协议时再要求 --port）
            if (servicePort is > 65535)
                throw new ArgumentException("--port 超出范围");

            HashSet<int> reserved = new();
            int rpcPort = PclLinkEasyTier.FindFreePort(30000, reserved);
            int listenPort = PclLinkEasyTier.FindFreePort(30000, reserved);
            int localPort = options.LocalPort ?? 25565;
            if (!PclLinkEasyTier.IsPortUsable(localPort))
                throw new ArgumentException($"本地转发端口 {localPort} 已被占用，请换 --local-port");

            List<string> args = BaseArguments(info, rpcPort, listenPort, options.Protocol,
                info.SecureMode || false);
            args.Add("-d");
            args.Add("--hostname");
            args.Add("pclcli-client");
            args.Add("--tcp-whitelist");
            args.Add("0");
            args.Add("--udp-whitelist");
            args.Add("0");
            if (!string.IsNullOrWhiteSpace(options.Direct))
            {
                string direct = options.Direct.Trim();
                if (!direct.Contains("://")) direct = "tcp://" + direct;
                args.Add("-p");
                args.Add(direct);
            }
            AddPeers(args, options.Peers, Array.Empty<string>());

            status?.Invoke($"正在启动联机模块（加入 {info.FullNetworkName}）…");
            PclLinkEasyTier.LinkProcess process = PclLinkEasyTier.Launch(workDir, args, "join");
            (string nickname, string machineId) = GetOrCreateIdentity(workDir);
            LinkState state = new()
            {
                Mode = "join",
                InviteCode = PclLinkCode.Build(info.Name, info.Secret, info.Prefix, info.SecureMode),
                NetworkName = info.FullNetworkName,
                NetworkSecret = info.FullSecret,
                OverlayIp = options.OverlayIp,
                ServicePort = servicePort,
                LocalJoinPort = localPort,
                Nickname = nickname,
                ListenPort = listenPort,
                RpcPort = rpcPort,
                Pid = process.Pid,
                SecureMode = info.SecureMode,
                Direct = options.Direct,
                LogPath = process.LogPath,
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            Save(workDir, state);

            bool connected = await WaitAsync(async () =>
            {
                JsonNode? peers = await PclLinkEasyTier.QueryAsync(workDir, rpcPort, new[] { "peer", "list" }, ct)
                    .ConfigureAwait(false);
                return PclLinkEasyTier.HasRoomHost(peers, "pclcli-host-", options.OverlayIp) ||
                       PclLinkEasyTier.HasRoomHost(peers, "pclnext-mc-server-", options.OverlayIp);
            }, options.WaitSeconds, ct).ConfigureAwait(false);
            if (!connected)
                throw new TimeoutException($"联机模块 {options.WaitSeconds}s 内没有连上房主，" +
                                           $"请查看日志 {process.LogPath}（公共节点不可达时可试 --direct）");

            // 房主主机名里带大厅协议端口（pclnext-mc-server-<port>）；有它就能注册、问端口、拿网关凭据。
            JsonNode? peerList = await PclLinkEasyTier.QueryAsync(workDir, rpcPort, new[] { "peer", "list" }, ct)
                .ConfigureAwait(false);
            string hostHostname = FindHostHostname(peerList, options.OverlayIp);
            int scaffoldPort = ParseHostScaffoldPort(hostHostname);
            state.ScaffoldPort = scaffoldPort;
            if (scaffoldPort > 0)
            {
                int localScaffold = PclLinkEasyTier.FindFreePort(30000, reserved);
                await AddForwardAsync(workDir, rpcPort, "tcp", localScaffold, options.OverlayIp, scaffoldPort, ct)
                    .ConfigureAwait(false);
                using PclLinkScaffoldClient scaffold = await ConnectScaffoldAsync(
                    localScaffold, machineId, nickname, ct).ConfigureAwait(false);
                if (scaffold is not null)
                {
                    if (state.ServicePort <= 0)
                    {
                        status?.Invoke("向房主询问服务器端口…");
                        state.ServicePort = scaffold.GetServerPort();
                        if (state.ServicePort <= 0)
                            throw new InvalidOperationException("房主没有给出有效的服务器端口");
                    }
                    PclLinkScaffoldClient.GateInfo? gate = scaffold.GetGate();
                    if (gate is not null && gate.Port > 0 && !string.IsNullOrEmpty(gate.Ticket))
                    {
                        // CLI 进程退出后隧道就没了：必须交给独立子进程常驻
                        state.TunnelPid = StartGateTunnel(workDir, localPort, options.OverlayIp, gate.Port,
                            info.FullSecret, gate.Ticket);
                        state.GatePort = gate.Port;
                        status?.Invoke($"准入网关已握手，隧道子进程 PID {state.TunnelPid}");
                    }
                }
            }

            if (state.GatePort <= 0)
            {
                if (state.ServicePort <= 0)
                    throw new ArgumentException("房主没有启用大厅协议，无法自动得知端口，请用 --port 指定房主服务端口");
                // 旧路径（房主没启用网关、或是 PCL2-CE / 陶瓦 房间）：直接把房主端口转到本地
                await AddForwardAsync(workDir, rpcPort, "tcp", localPort, options.OverlayIp, state.ServicePort, ct)
                    .ConfigureAwait(false);
                await AddForwardAsync(workDir, rpcPort, "udp", localPort, options.OverlayIp, state.ServicePort, ct)
                    .ConfigureAwait(false);
            }
            Save(workDir, state);
            return state;
        }

        /// <summary>
        /// 起一个常驻的网关隧道子进程：本机 <paramref name="localPort"/> 经 HMAC 握手转到房主网关端口。
        /// 凭据写入受限权限的配置文件，避免出现在进程命令行里。
        /// </summary>
        private static int StartGateTunnel(string workDir, int localPort, string hostIp, int gatePort,
            string secret, string ticket)
        {
            string runDir = Path.Combine(workDir, "link");
            Directory.CreateDirectory(runDir);
            string configPath = Path.Combine(runDir, "gate-tunnel.json");
            string logPath = Path.Combine(runDir, "gate-tunnel.log");
            string json = JsonSerializer.Serialize(new
            {
                localPort,
                hostIp,
                gatePort,
                secret,
                ticket
            }, JsonOptions);
            File.WriteAllText(configPath, json, new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(configPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // 某些文件系统不支持，忽略
                }
            }

            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("拿不到 CLI 可执行文件路径");
            string scriptPath = Path.Combine(runDir, OperatingSystem.IsWindows() ? "run-gate-tunnel.cmd" : "run-gate-tunnel.sh");
            StringBuilder script = new();
            if (OperatingSystem.IsWindows())
            {
                script.AppendLine("@echo off");
                script.Append('"').Append(exe).Append('"');
            }
            else
            {
                script.AppendLine("#!/bin/sh");
                script.Append("exec \"").Append(exe).Append('"');
            }
            foreach (string arg in new[]
                     {
                         "link", "tunnel", "--config", configPath, "--log", logPath
                     })
                script.Append(' ').Append('"').Append(arg).Append('"');
            script.Append(" >> ").Append('"').Append(logPath).Append('"').AppendLine(" 2>&1");
            File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            ProcessStartInfo info = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd.exe")
                : new ProcessStartInfo("/bin/sh");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            if (OperatingSystem.IsWindows())
            {
                info.ArgumentList.Add("/c");
                info.ArgumentList.Add(scriptPath);
            }
            else
            {
                info.ArgumentList.Add(scriptPath);
            }
            using Process process = new() { StartInfo = info };
            process.Start();
            return process.Id;
        }

        /// <summary>向本机 easytier-cli 动态加一条端口转发。</summary>
        private static async Task AddForwardAsync(string workDir, int rpcPort, string protocol,
            int localPort, string overlayIp, int targetPort, CancellationToken ct)
        {
            await PclLinkEasyTier.QueryAsync(workDir, rpcPort, new[]
            {
                "port-forward", "add", protocol, $"127.0.0.1:{localPort}", $"{overlayIp}:{targetPort}"
            }, ct).ConfigureAwait(false);
        }

        /// <summary>等房主的协议端口被转发起来并完成注册；返回已注册的客户端，失败返回 null。</summary>
        private static async Task<PclLinkScaffoldClient?> ConnectScaffoldAsync(int localPort, string machineId,
            string nickname, CancellationToken ct)
        {
            DateTime deadline = DateTime.Now.AddSeconds(20);
            Exception? last = null;
            while (DateTime.Now < deadline)
            {
                PclLinkScaffoldClient? client = null;
                try
                {
                    client = new PclLinkScaffoldClient("127.0.0.1", localPort, machineId, nickname,
                        "pclcli, EasyTier " + PclLinkEasyTier.Version);
                    client.Connect();
                    client.Register();
                    return client;
                }
                catch (Exception ex)
                {
                    last = ex;
                    client?.Dispose();
                    await Task.Delay(700, ct).ConfigureAwait(false);
                }
            }
            if (last is not null)
                throw new InvalidOperationException("连不上房主的大厅协议端口：" + last.Message, last);
            return null;
        }

        /// <summary>在 peer 列表里找房主的 hostname（优先匹配固定 overlay IP，其次匹配已知前缀）。</summary>
        private static string FindHostHostname(JsonNode? output, string overlayIp)
        {
            string fallback = "";
            foreach ((string ip, string hostname, _, _) in PclLinkEasyTier.DescribeRoomPeers(output))
            {
                if (!hostname.StartsWith("pclnext-mc-server-", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ip == overlayIp) return hostname;
                fallback = hostname;
            }
            return fallback;
        }

        /// <summary>从 <c>pclnext-mc-server-&lt;port&gt;</c> 里取大厅协议端口；不是这种主机名返回 0。</summary>
        private static int ParseHostScaffoldPort(string hostname)
        {
            const string prefix = "pclnext-mc-server-";
            if (!hostname.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return 0;
            int port = int.TryParse(hostname[prefix.Length..], out int value) ? value : 0;
            return port is > 0 and <= 65535 ? port : 0;
        }

        /// <summary>本机在大厅里的昵称与机器码：落盘复用，避免每次加入都换身份。</summary>
        private static (string Nickname, string MachineId) GetOrCreateIdentity(string workDir)
        {
            string file = Path.Combine(workDir, "link", "identity.json");
            try
            {
                if (File.Exists(file))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                    string name = doc.RootElement.TryGetProperty("nickname", out JsonElement n) ? (n.GetString() ?? "") : "";
                    string id = doc.RootElement.TryGetProperty("machineId", out JsonElement m) ? (m.GetString() ?? "") : "";
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                        return (name, id);
                }
            }
            catch
            {
                // 文件坏了就重新生成
            }
            string[] alphabet = { "ABCDEFGHJKMNPQRSTUVWXYZ23456789" };
            string tag = new string(Enumerable.Range(0, 4)
                .Select(_ => alphabet[0][RandomNumberGenerator.GetInt32(alphabet[0].Length)]).ToArray());
            string machineId = string.Join('-', Enumerable.Range(0, 4)
                .Select(_ => new string(Enumerable.Range(0, 4)
                    .Select(__ => "0123456789ABCDEFGHJKMNPQRSTUVWXYZ"[RandomNumberGenerator.GetInt32(32)]).ToArray())));
            (string, string) identity = ("pclcli-" + tag, machineId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(new { nickname = identity.Item1, machineId = identity.Item2 }),
                    new UTF8Encoding(false));
            }
            catch
            {
                // 落盘失败也能用
            }
            return identity;
        }

        private static void AddPeers(List<string> args, IReadOnlyList<string> peers, IReadOnlyList<string> direct)
        {
            IEnumerable<string> all = peers.Count > 0 ? peers : PclLinkEasyTier.PublicPeers;
            foreach (string peer in all)
            {
                if (string.IsNullOrWhiteSpace(peer)) continue;
                args.Add("-p");
                args.Add(peer.Trim());
            }
            foreach (string peer in direct)
            {
                if (string.IsNullOrWhiteSpace(peer)) continue;
                string value = peer.Trim();
                if (!value.Contains("://")) value = "tcp://" + value;
                args.Add("-p");
                args.Add(value);
            }
        }

        private static List<string> BaseArguments(PclLinkCode.CodeInfo info, int rpcPort, int listenPort,
            string protocol, bool secureMode)
        {
            List<string> args = new()
            {
                "--no-tun",
                "--network-name", info.FullNetworkName,
                "--network-secret", info.FullSecret,
                "--relay-network-whitelist", info.FullNetworkName,
                "--default-protocol", string.Equals(protocol, "udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp",
                "--listeners", "tcp://0.0.0.0:" + listenPort,
                "--listeners", "udp://0.0.0.0:" + listenPort,
                "--rpc-portal", "127.0.0.1:" + rpcPort,
                "--private-mode", "true"
            };
            if (secureMode)
            {
                args.Add("--secure-mode");
                args.Add("true");
                // 加密算法必须与对等节点一致，否则握手会被对方直接断开（表现为 conn closed during wait handshake）
                args.Add("--encryption-algorithm");
                args.Add("aes-gcm");
            }
            return args;
        }

        /// <summary>轮询直到条件成立或超时。</summary>
        private static async Task<bool> WaitAsync(Func<Task<bool>> condition, int seconds, CancellationToken ct)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await condition().ConfigureAwait(false)) return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 查询失败按「还没好」处理，继续等
                }
                await Task.Delay(800, ct).ConfigureAwait(false);
            }
            return false;
        }

        /// <summary>停止当前会话（有状态文件时按 PID 杀进程，再删状态）。</summary>
        public static bool Stop(string workDir, out LinkState? state)
        {
            state = Load(workDir);
            bool stopped = state is not null && PclLinkEasyTier.Stop(state.Pid);
            if (state is not null && state.TunnelPid > 0)
                PclLinkEasyTier.Stop(state.TunnelPid);
            if (state is not null)
            {
                foreach (string file in new[] { "gate-tunnel.json", "gate-tunnel.log" })
                {
                    try { File.Delete(Path.Combine(workDir, "link", file)); } catch { /* 忽略 */ }
                }
            }
            Clear(workDir);
            return stopped;
        }

        /// <summary>读取 peer 列表（未连上或查询失败返回 null）。</summary>
        public static async Task<(int Remote, List<(string Ip, string Hostname, string Cost, int LatencyMs)> Peers)?>
            PeersAsync(string workDir, LinkState state, CancellationToken ct)
        {
            var output = await PclLinkEasyTier.QueryAsync(workDir, state.RpcPort, new[] { "peer", "list" }, ct)
                .ConfigureAwait(false);
            if (output is null) return null;
            return (PclLinkEasyTier.CountRoomPeers(output), PclLinkEasyTier.DescribeRoomPeers(output));
        }

        /// <summary>
        /// TCP 探测：连上目标后发送 payload，读回等长字节并计算往返耗时。
        /// 用于验证「overlay + 端口转发」是否真的通（不需要启动游戏）。
        /// </summary>
        public static async Task<ProbeResult> ProbeAsync(string host, int port, string payload, int timeoutMs,
            CancellationToken ct)
        {
            Stopwatch watch = Stopwatch.StartNew();
            using TcpClient client = new TcpClient();
            using CancellationTokenSource timeout = new CancellationTokenSource(timeoutMs);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            long connectMs = watch.ElapsedMilliseconds;
            NetworkStream stream = client.GetStream();
            byte[] outBytes = Encoding.UTF8.GetBytes(payload);
            await stream.WriteAsync(outBytes, linked.Token).ConfigureAwait(false);
            byte[] buffer = new byte[Math.Max(16, outBytes.Length)];
            int total = 0;
            while (total < outBytes.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, outBytes.Length - total), linked.Token)
                    .ConfigureAwait(false);
                if (read <= 0) break;
                total += read;
            }
            string echo = Encoding.UTF8.GetString(buffer, 0, total);
            return new ProbeResult
            {
                Ok = total == outBytes.Length && echo == payload,
                ConnectMs = connectMs,
                RoundTripMs = watch.ElapsedMilliseconds,
                Sent = payload,
                Received = echo,
                Remote = $"{host}:{port}"
            };
        }

        public sealed class ProbeResult
        {
            public bool Ok { get; init; }
            public long ConnectMs { get; init; }
            public long RoundTripMs { get; init; }
            public string Sent { get; init; } = "";
            public string Received { get; init; } = "";
            public string Remote { get; init; } = "";
        }

        /// <summary>
        /// 极简 TCP 回显服务：把收到的字节原样发回。
        /// 用于在没有 MC 服务端时验证联机链路（云端 AI 不能开 GUI，也不需要开游戏）。
        /// </summary>
        public static async Task RunEchoAsync(int port, bool once, Action<string>? status, CancellationToken ct)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            status?.Invoke($"回显服务已监听 0.0.0.0:{port}（{(once ? "处理一次连接后退出" : "Ctrl+C 结束")}）");
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    status?.Invoke($"收到连接：{client.Client.RemoteEndPoint}");
                    NetworkStream stream = client.GetStream();
                    byte[] buffer = new byte[65536];
                    while (!ct.IsCancellationRequested)
                    {
                        int read;
                        try
                        {
                            read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                        }
                        catch (IOException)
                        {
                            break;
                        }
                        if (read <= 0) break;
                        byte[] echo = buffer[..read];
                        await stream.WriteAsync(echo, ct).ConfigureAwait(false);
                        status?.Invoke($"回显 {read} 字节");
                    }
                    if (once) break;
                }
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
