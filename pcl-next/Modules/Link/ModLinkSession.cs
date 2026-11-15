using System;
using MeloongCore;
using MeloongCore.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCL
{

    /// <summary>
    /// 联机会话（pcl-next 自研）：负责建房 / 加入 / 退出，以及对外暴露状态。
    /// <para>
    /// 传输层只用 EasyTier 公共节点：房主与加入者各自起一个 EasyTier 进程，通过框架内的
    /// 端口白名单 + 端口转发把房主本机的 MC 服务器端口映射到加入者的本地端口。
    /// 没有任何中心服务器，也不需要账号身份。
    /// </para>
    /// </summary>
    public static class ModLinkSession
    {

        #region 常量与状态

        /// <summary>当前角色。</summary>
        public enum Role
        {
            None,
            Host,
            Joiner
        }

        /// <summary>当前阶段。</summary>
        public enum Stage
        {
            Idle,
            Preparing,
            Connecting,
            Ready,
            Failed
        }

        // 房主在 overlay 里的固定地址不再写死：由 LobbyTarget.OverlayIp 提供
        // （pcl-next 大厅 10.114.114.114 / PCL2-CE 10.114.51.41 / 陶瓦 10.144.144.1）。

        /// <summary>联机是否处于活动状态（准备中、连接中或已就绪）。</summary>
        public static bool IsActive =>
            CurrentRole != Role.None &&
            (CurrentStage == Stage.Preparing || CurrentStage == Stage.Connecting || CurrentStage == Stage.Ready);

        /// <summary>当前角色。</summary>
        public static Role CurrentRole { get; private set; } = Role.None;

        /// <summary>当前阶段。</summary>
        public static Stage CurrentStage { get; private set; } = Stage.Idle;

        /// <summary>状态文案（直接显示在界面上）。</summary>
        public static string StatusText { get; private set; } = "未连接";

        /// <summary>邀请码（房主就绪后有效）。</summary>
        public static string InviteCode { get; private set; } = "";

        /// <summary>房主的 MC 服务器端口。</summary>
        public static int HostServerPort { get; private set; }

        /// <summary>加入者本地可用的连接端口（加入就绪后有效）。</summary>
        public static int LocalJoinPort { get; private set; }

        /// <summary>最近一次错误信息。</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>房主的「同网络直连地址」（IP:端口），公共节点不可用时可用它直连。</summary>
        public static string HostDirectAddress { get; private set; } = "";

        /// <summary>状态变化通知（已切回 UI 线程）。</summary>
        public static event Action Changed;

        private static CancellationTokenSource _cts;
        private static int _rpcPort;
        private static string _networkName;
        private static string _networkSecret;
        private static string _directPeer = "";
        /// <summary>当前使用的大厅参数（建房时按互通设置生成，加入时按邀请码推导）。</summary>
        private static LobbyTarget _target;

        #endregion

        #region 对外操作

        /// <summary>创建房间（本机作为房主）。</summary>
        public static Task CreateAsync(ushort serverPort)
        {
            return RunAsync(true, serverPort, null);
        }

        /// <summary>加入房间。</summary>
        /// <param name="code">房主给出的邀请码。</param>
        /// <param name="directPeer">可选：房主的直连地址（IP:端口），公共节点不可达时使用。</param>
        public static Task JoinAsync(string code, string directPeer = "")
        {
            _directPeer = (directPeer ?? "").Trim();
            return RunAsync(false, 0, code);
        }

        /// <summary>退出房间 / 取消正在进行的联机操作。</summary>
        public static void Leave()
        {
            try { _cts?.Cancel(); } catch { /* 忽略 */ }
            _cts = null;
            ModLinkEasyTier.Stop();
            CurrentRole = Role.None;
            CurrentStage = Stage.Idle;
            InviteCode = "";
            LocalJoinPort = 0;
            HostServerPort = 0;
            HostDirectAddress = "";
            _directPeer = "";
            _networkName = null;
            _networkSecret = null;
            _target = null;
            StatusText = "未连接";
            RaiseChanged();
        }

        #endregion

        #region 内部流程

        private static async Task RunAsync(bool asHost, ushort serverPort, string code)
        {
            if (IsActive)
                Leave();
            CancellationTokenSource cts = new CancellationTokenSource();
            _cts = cts;
            CancellationToken ct = cts.Token;
            CurrentRole = asHost ? Role.Host : Role.Joiner;
            CurrentStage = Stage.Preparing;
            LastError = "";
            InviteCode = "";
            LocalJoinPort = 0;
            SetStatus(asHost ? "正在准备创建房间…" : "正在准备加入房间…");
            try
            {
                await ModLinkEasyTier.EnsureAsync(SetStatus, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                    return;
                if (asHost)
                    await StartHostAsync(serverPort, ct).ConfigureAwait(false);
                else
                    await StartJoinerAsync(code, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Leave();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "联机启动失败");
                ModLinkEasyTier.Stop();
                CurrentStage = Stage.Failed;
                LastError = SimplifyError(ex);
                SetStatus("联机失败：" + LastError);
            }
        }

        private static async Task StartHostAsync(ushort serverPort, CancellationToken ct)
        {
            HostServerPort = serverPort;
            // 按「互通目标」生成邀请码与大厅参数（仅 pcl-next / PCL2-CE / 陶瓦）
            (_target, InviteCode) = ModLinkInterop.BuildHost(ModLinkInterop.GetHostKind(), serverPort);
            _networkName = _target.FullNetworkName;
            _networkSecret = _target.NetworkSecret;
            Logger.Info($"联机大厅：{_target.KindName}，网络名 {_target.FullNetworkName}，邀请码 {_target.OriginalCode}");
            _rpcPort = ModLinkEasyTier.FindFreePort();
            int listenPort = ModLinkEasyTier.FindFreePort();
            List<string> args = BuildBaseArguments(_target, _rpcPort, listenPort);
            args.Add("-i");
            args.Add(_target.OverlayIp);
            args.Add("--hostname");
            args.Add(GetHostname("Server"));
            args.Add("--tcp-whitelist");
            args.Add(serverPort.ToString());
            args.Add("--udp-whitelist");
            args.Add(serverPort.ToString());
            SetStatus("正在启动联机模块…");
            ModLinkEasyTier.Launch(args);
            SetStatus("正在等待联机网络就绪…");
            bool alive = await WaitAsync(() => ModLinkEasyTier.IsNodeAliveAsync(_rpcPort, ct), 30, ct).ConfigureAwait(false);
            if (!alive)
                throw new Exception("联机模块启动超时，可能是端口被占用或本机网络受限");
            HostDirectAddress = GetLanAddress() + ":" + listenPort;
            CurrentStage = Stage.Ready;
            SetStatus($"房间已创建（{_target.KindName}，服务器端口 {serverPort}）。把邀请码发给好友即可加入。");
            // 公共节点不可用时给一句提示，避免误以为好友一定能连进来
            await Task.Delay(6000, ct).ConfigureAwait(false);
            int peers = await ModLinkEasyTier.GetPeerCountAsync(_rpcPort, ct).ConfigureAwait(false);
            if (CurrentStage == Stage.Ready && peers == 0)
                SetStatus($"房间已创建（{_target.KindName}，服务器端口 {serverPort}）。当前尚未连上任何公共节点，若好友加不进来，请改用直连地址 {HostDirectAddress}。");
        }

        private static async Task StartJoinerAsync(string code, CancellationToken ct)
        {
            // 码内可能不带端口（PCL2-CE 的 U/ 码），此时借用「直连地址」里填的端口
            _target = ModLinkInterop.Parse(code, GetDirectPort());
            if (_target.Port == 0)
                throw new Exception("这张邀请码不含服务器端口，请在「直连地址」里填 房主IP:端口 后再加入");
            _networkName = _target.FullNetworkName;
            _networkSecret = _target.NetworkSecret;
            HostServerPort = _target.Port;
            Logger.Info($"联机大厅：{_target.KindName}，网络名 {_target.FullNetworkName}，房主端口 {_target.Port}");
            _rpcPort = ModLinkEasyTier.FindFreePort();
            int listenPort = ModLinkEasyTier.FindFreePort();
            LocalJoinPort = ModLinkEasyTier.FindFreePort();
            List<string> args = BuildBaseArguments(_target, _rpcPort, listenPort);
            args.Add("-d");
            args.Add("--hostname");
            args.Add(GetHostname("Client"));
            args.Add("--tcp-whitelist");
            args.Add("0");
            args.Add("--udp-whitelist");
            args.Add("0");
            args.Add("--port-forward");
            args.Add($"tcp://127.0.0.1:{LocalJoinPort}/{_target.OverlayIp}:{_target.Port}");
            args.Add("--port-forward");
            args.Add($"udp://127.0.0.1:{LocalJoinPort}/{_target.OverlayIp}:{_target.Port}");
            SetStatus($"正在启动联机模块并连接房主（{_target.KindName}）…");
            ModLinkEasyTier.Launch(args);
            bool connected = await WaitAsync(async () => await ModLinkEasyTier.GetPeerCountAsync(_rpcPort, ct).ConfigureAwait(false) >= 1, 45, ct).ConfigureAwait(false);
            if (!connected)
                throw new Exception("45 秒内没有连上房主，请确认邀请码是否最新，以及双方网络能否访问 EasyTier 公共节点");
            CurrentStage = Stage.Ready;
            SetStatus($"已加入房间（{_target.KindName}）。在游戏里连接 127.0.0.1:{LocalJoinPort} 即可进入房主的服务器。");
        }

        /// <summary>从「直连地址」（可带 tcp:// 前缀）里取端口，取不到返回 0。</summary>
        private static ushort GetDirectPort()
        {
            string direct = _directPeer ?? "";
            int scheme = direct.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
                direct = direct.Substring(scheme + 3);
            int colon = direct.LastIndexOf(':');
            if (colon <= 0)
                return 0;
            return ushort.TryParse(direct.Substring(colon + 1), out ushort port) ? port : (ushort)0;
        }

        private static List<string> BuildBaseArguments(LobbyTarget target, int rpcPort, int listenPort)
        {
            List<string> args = new List<string>
            {
                "--no-tun",
                "--network-name", target.FullNetworkName,
                "--network-secret", target.NetworkSecret,
                "--relay-network-whitelist", target.FullNetworkName,
                // 连接对等节点时优先使用的传输协议（联机设置 → 协议偏好）
                "--default-protocol", Settings.Get<int>("LinkProtocolPreference") >= 1 ? "udp" : "tcp",
                // 显式只监听 tcp/udp：直接给端口号会让 EasyTier 顺带占用 +1/+2/+3 端口，容易撞车
                "--listeners", "tcp://0.0.0.0:" + listenPort,
                "--listeners", "udp://0.0.0.0:" + listenPort,
                "--rpc-portal", "127.0.0.1:" + rpcPort,
                "--private-mode", "true"
            };
            // 加密与压缩必须与对等节点一致，否则握手直接失败：由大厅档案决定
            if (!string.IsNullOrEmpty(target.EncryptionAlgorithm))
            {
                args.Add("--encryption-algorithm");
                args.Add(target.EncryptionAlgorithm);
            }
            if (!string.IsNullOrEmpty(target.Compression))
            {
                args.Add("--compression");
                args.Add(target.Compression);
            }
            // 兼容其它启动器时补上对方固定的数据流代理开关
            if (target.CompatFlags)
            {
                args.Add("--enable-quic-proxy");
                args.Add("--enable-kcp-proxy");
                args.Add("--use-smoltcp");
                args.Add("--multi-thread");
            }
            if (Settings.Get<int>("LinkLatencyMode") == 1)
                args.Add("--latency-first");
            // 联机设置：允许系统打洞 / 启用 IPv6（不勾选时才加对应的禁用开关）
            if (!Settings.Get<bool>("LinkTryPunchSym"))
                args.Add("--disable-sys-hole-punching");
            if (!Settings.Get<bool>("LinkEnableIPv6"))
                args.Add("--disable-ipv6");
            string direct = _directPeer;
            if (!string.IsNullOrWhiteSpace(direct))
            {
                if (!direct.Contains("://"))
                    direct = "tcp://" + direct;
                args.Add("-p");
                args.Add(direct);
            }
            foreach (string peer in GetPeers())
            {
                args.Add("-p");
                args.Add(peer);
            }
            return args;
        }

        /// <summary>
        /// 联机节点显示名：优先用设置里的「显示名」，留空则按角色 + 本机标识生成。
        /// </summary>
        private static string GetHostname(string role)
        {
            string custom = (Settings.Get<string>("LinkUsername") ?? "").Trim();
            if (!string.IsNullOrEmpty(custom))
                return custom.Length > 63 ? custom.Substring(0, 63) : custom;
            return role + "-" + ModBase.Identify.GetStableHashCode().ToString("X").TrimStart('-');
        }

        private static string GetLanAddress()
        {
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        continue;
                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                            continue;
                        if (address.Address.ToString().StartsWith("127.") || address.Address.ToString().StartsWith("169.254."))
                            continue;
                        return address.Address.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "获取本机局域网地址失败");
            }
            return "127.0.0.1";
        }

        private static IEnumerable<string> GetPeers()
        {
            string custom = Settings.Get<string>("LinkCustomPeer");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                List<string> list = custom
                    .Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();
                if (list.Count > 0)
                    return list;
            }
            return ModLinkEasyTier.PublicPeers;
        }

        private static async Task<bool> WaitAsync(Func<Task<bool>> probe, int timeoutSeconds, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await probe().ConfigureAwait(false))
                        return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 单次探测失败不算整体失败，继续等
                }
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            return false;
        }

        private static string SimplifyError(Exception ex)
        {
            string message = ex.Message;
            if (message.Contains("下载"))
                return message + "（可稍后重试，或检查网络能否访问 GitHub）";
            return message;
        }

        private static void SetStatus(string text)
        {
            StatusText = text;
            if (CurrentStage == Stage.Preparing && text.Contains("联机模块"))
                CurrentStage = Stage.Preparing;
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler is null)
                return;
            try
            {
                ModBase.RunInUi(() => handler());
            }
            catch
            {
                // 界面尚未就绪时忽略
            }
        }

        #endregion

    }
}
