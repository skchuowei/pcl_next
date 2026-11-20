using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
            if (servicePort is < 1 or > 65535)
                throw new ArgumentException("邀请码里没有端口，请用 --port 指定房主服务端口");

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
            // 房主侧服务端口 → 本地回环端口；连接 127.0.0.1:localPort 即经 overlay 到房主
            args.Add("--port-forward");
            args.Add($"tcp://127.0.0.1:{localPort}/{options.OverlayIp}:{servicePort}");
            args.Add("--port-forward");
            args.Add($"udp://127.0.0.1:{localPort}/{options.OverlayIp}:{servicePort}");
            if (!string.IsNullOrWhiteSpace(options.Direct))
            {
                string direct = options.Direct.Trim();
                if (!direct.Contains("://")) direct = "tcp://" + direct;
                args.Add("-p");
                args.Add(direct);
            }
            AddPeers(args, options.Peers, Array.Empty<string>());

            status?.Invoke($"正在启动联机模块（加入 {info.FullNetworkName}，转发本地 {localPort} → 房主 {servicePort}）…");
            PclLinkEasyTier.LinkProcess process = PclLinkEasyTier.Launch(workDir, args, "join");
            LinkState state = new()
            {
                Mode = "join",
                InviteCode = PclLinkCode.Build(info.Name, info.Secret, info.Prefix, info.SecureMode),
                NetworkName = info.FullNetworkName,
                NetworkSecret = info.FullSecret,
                OverlayIp = options.OverlayIp,
                ServicePort = servicePort,
                LocalJoinPort = localPort,
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
                var peers = await PclLinkEasyTier.QueryAsync(workDir, rpcPort, new[] { "peer", "list" }, ct)
                    .ConfigureAwait(false);
                return PclLinkEasyTier.HasRoomHost(peers, "pclcli-host-", options.OverlayIp);
            }, options.WaitSeconds, ct).ConfigureAwait(false);
            if (!connected)
                throw new TimeoutException($"联机模块 {options.WaitSeconds}s 内没有连上房主，" +
                                           $"请查看日志 {process.LogPath}（公共节点不可达时可试 --direct）");
            return state;
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
