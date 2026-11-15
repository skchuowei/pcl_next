using System;
using MeloongCore;
using MeloongCore.Extensions;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCL
{

    /// <summary>
    /// EasyTier 运行环境的自管理：下载 → 校验 → 解压 → 启动/停止 → 状态查询。
    /// <para>
    /// 本类为 pcl-next 自研实现，不依赖上游 <c>ModSecret.ServerConfig</c>、也不走旧的
    /// 「联机模块」加载器链路（旧链路在开源构建里拿不到配置、下载进度也容易卡死）。
    /// 网络层只用 EasyTier 公共节点（public.easytier.top / public2.easytier.cn），无自建服务。
    /// </para>
    /// </summary>
    public static class ModLinkEasyTier
    {

        #region 常量与路径

        /// <summary>内置 EasyTier 版本（与发行包文件名一致）。</summary>
        public const string Version = "2.6.4";

        private const string ReleaseBase = "https://github.com/EasyTier/EasyTier/releases/download/v" + Version;
        private const string ReleaseName = "easytier-windows-{arch}-v" + Version + ".zip";

        /// <summary>单候选下载超时：发行包几十 MB，给 5 分钟（总时长仍由调用方的 CancellationToken 控制）。</summary>
        private const int LinkDownloadTimeoutMs = 5 * 60 * 1000;
        /// <summary>发行包体积上限：实测约 40–60 MB，留 200 MB 余量，防止被镜像塞入异常大文件。</summary>
        private const int LinkMaxBytes = 200 * 1024 * 1024;

        /// <summary>公共节点（社区免费共享，无 SLA，仅作发现与中转）。</summary>
        public static readonly string[] PublicPeers =
        {
            "tcp://public.easytier.top:11010",
            "tcp://public2.easytier.cn:54321",
            "udp://public.easytier.top:11010"
        };

        private static string RootDir => Paths.AppDataThenName + @"Link\EasyTier\";
        private static string CorePath => RootDir + "easytier-core.exe";
        private static string CliPath => RootDir + "easytier-cli.exe";

        /// <summary>当前架构的发行包完整地址。自检与诊断直接取这里，避免第二份 URL 常量。</summary>
        internal static string PackageUrl => $"{ReleaseBase}/{ReleaseName.Replace("{arch}", GetArchName())}";

        /// <summary>联机模块是否已经可用（两个可执行文件都存在）。</summary>
        public static bool IsReady => File.Exists(CorePath) && File.Exists(CliPath);

        #endregion

        #region 安装

        private static string GetArchName()
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.X86 => "i686",
                Architecture.Arm64 => "arm64",
                _ => "x86_64"
            };
        }

        /// <summary>
        /// 确保 EasyTier 已安装。已安装则立即返回；否则下载并解压到 <see cref="RootDir"/>。
        /// 下载统一走 <see cref="ModGitHub.GetFileAsync"/>：镜像候选、限流判定、大小上限都由请求层负责，
        /// 本类不再自带一份镜像列表（两份列表迟早会不一致）。
        /// </summary>
        /// <param name="onStatus">进度回调（文案，已包含百分比），会在后台线程调用。</param>
        public static async Task EnsureAsync(Action<string> onStatus, CancellationToken ct)
        {
            if (IsReady)
                return;
            Directory.CreateDirectory(RootDir);
            string arch = GetArchName();
            string zipName = ReleaseName.Replace("{arch}", arch);
            string zipUrl = $"{ReleaseBase}/{zipName}";
            string zipPath = Path.Combine(Path.GetTempPath(), "pclnext-easytier-" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                // 下载单独包一层：只有这一段失败才该说「下载源不可用」，
                // 解压 / 复制失败要保留原始异常，别误报成网络问题
                try
                {
                    onStatus($"正在下载联机模块（EasyTier {Version}）…");
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    int lastPct = -10;
                    await ModGitHub.GetFileAsync(zipUrl, zipPath,
                        (received, total) =>
                        {
                            double seconds = Math.Max(0.5, stopwatch.Elapsed.TotalSeconds);
                            double speed = received / 1024.0 / seconds;
                            if (total > 0)
                            {
                                int pct = (int)(received * 100 / total);
                                if (pct >= lastPct + 10)
                                {
                                    lastPct = pct;
                                    onStatus($"正在下载联机模块 {pct}%（{received / 1048576.0:0.0} / {total / 1048576.0:0.0} MB，{speed:0} KB/s）");
                                }
                            }
                            else
                            {
                                onStatus($"正在下载联机模块（已下载 {received / 1048576.0:0.0} MB，{speed:0} KB/s）");
                            }
                        },
                        timeoutMs: LinkDownloadTimeoutMs, maxBytes: LinkMaxBytes, ct: ct).ConfigureAwait(false);

                    if (new FileInfo(zipPath).Length < 1024 * 1024 * 5)
                        throw new Exception("下载的联机模块体积异常");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new Exception("联机模块下载失败（所有下载源都不可用）：" + ex.Message, ex);
                }

                onStatus("正在解压联机模块…");
                string extractDir = Path.Combine(Path.GetTempPath(), "pclnext-easytier-" + Guid.NewGuid().ToString("N"));
                try
                {
                    ZipFile.ExtractToDirectory(zipPath, extractDir, true);
                    string[] cores = Directory.GetFiles(extractDir, "easytier-core.exe", SearchOption.AllDirectories);
                    if (cores.Length == 0)
                        throw new Exception("联机模块压缩包里没有 easytier-core.exe，下载可能损坏");
                    string sourceDir = Path.GetDirectoryName(cores[0]);
                    foreach (string file in Directory.GetFiles(sourceDir))
                        File.Copy(file, Path.Combine(RootDir, Path.GetFileName(file)), true);
                }
                finally
                {
                    try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { /* 忽略清理失败 */ }
                }

                if (!IsReady)
                    throw new Exception("联机模块安装后仍不完整，请检查目录写入权限：" + RootDir);
                onStatus("联机模块已就绪");
            }
            finally
            {
                SafeDelete(zipPath);
            }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略 */ }
        }

        #endregion

        #region 进程

        private static Process _process;

        /// <summary>联机模块进程是否在运行。</summary>
        public static bool IsRunning
        {
            get
            {
                try { return _process is not null && !_process.HasExited; }
                catch { return false; }
            }
        }

        /// <summary>启动联机模块（EasyTier 核心进程）。</summary>
        public static void Launch(IEnumerable<string> arguments)
        {
            Stop();
            if (!IsReady)
                throw new Exception("联机模块尚未安装完成");
            ProcessStartInfo info = new ProcessStartInfo(CorePath)
            {
                WorkingDirectory = RootDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string arg in arguments)
                info.ArgumentList.Add(arg);
            Process process = new Process { StartInfo = info, EnableRaisingEvents = true };
            // 联机设置 → 是否把联机模块的普通输出写进启动器日志（错误行始终记录）
            bool logOutput = Settings.Get<bool>("LinkEnableCliOutput");
            process.OutputDataReceived += (_, e) =>
            {
                if (logOutput && !string.IsNullOrWhiteSpace(e.Data))
                    Logger.Info("[联机] " + e.Data.Trim());
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Logger.Warn("[联机] " + e.Data.Trim());
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            Logger.Info($"已启动联机模块，PID {process.Id}，参数：{string.Join(" ", arguments)}");
        }

        /// <summary>停止联机模块。</summary>
        public static void Stop()
        {
            Process process = _process;
            _process = null;
            if (process is null)
                return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "停止联机模块失败");
            }
            Logger.Info("联机模块已停止");
        }

        #endregion

        #region 状态查询

        /// <summary>获取一个本地空闲 TCP 端口。</summary>
        public static int FindFreePort(int start = 30000)
        {
            for (int port = Math.Max(1024, start); port < 65000; port++)
            {
                try
                {
                    using System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch
                {
                    // 端口被占用，继续尝试下一个
                }
            }
            throw new Exception("找不到空闲端口");
        }

        /// <summary>
        /// 查询本机实例是否已经在跑（easytier-cli node 成功返回即为真）。
        /// </summary>
        public static async Task<bool> IsNodeAliveAsync(int rpcPort, CancellationToken ct)
        {
            JToken output = await RunCliAsync(rpcPort, new[] { "node" }, ct).ConfigureAwait(false);
            return output is not null;
        }

        /// <summary>
        /// 查询已连接的远端 peer 数量（不含本机自身节点；拿不到时返回 -1）。
        /// </summary>
        public static async Task<int> GetPeerCountAsync(int rpcPort, CancellationToken ct)
        {
            JToken output = await RunCliAsync(rpcPort, new[] { "peer", "list" }, ct).ConfigureAwait(false);
            if (output is null)
                return -1;
            JArray list = null;
            if (output is JArray array)
                list = array;
            else if (output is JObject obj)
            {
                foreach (string key in new[] { "peers", "items", "data" })
                {
                    if (obj[key] is JArray inner)
                    {
                        list = inner;
                        break;
                    }
                }
            }
            if (list is null)
                return -1;
            int remote = 0;
            foreach (JToken item in list)
            {
                string cost = item["cost"]?.ToString() ?? "";
                if (!cost.Equals("Local", StringComparison.OrdinalIgnoreCase))
                    remote++;
            }
            return remote;
        }

        /// <summary>
        /// 读取 peer 列表里的延迟表（`IP → 毫秒`），用于房间卡片展示成员延迟。
        /// 拿不到延迟的节点不会出现在表里（调用方按「未知」处理）。
        /// </summary>
        public static async Task<Dictionary<string, int>> GetPeerLatencyAsync(int rpcPort, CancellationToken ct)
        {
            JToken output = await RunCliAsync(rpcPort, new[] { "peer", "list" }, ct).ConfigureAwait(false);
            JArray list = ExtractPeerArray(output);
            Dictionary<string, int> table = new Dictionary<string, int>();
            if (list is null)
                return table;
            foreach (JToken item in list)
            {
                string ip = item["ipv4"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(ip))
                    continue;
                int ping = ParseLatency(item);
                if (ping >= 0)
                    table[ip] = ping;
            }
            return table;
        }

        /// <summary>从 peer 条目里取延迟（不同版本字段名略有差异，按优先级尝试）。</summary>
        private static int ParseLatency(JToken item)
        {
            foreach (string key in new[] { "lat_ms", "latency_ms", "latency", "lat_ms_avg" })
            {
                JToken token = item[key];
                if (token is null)
                    continue;
                try
                {
                    return (int)Math.Round(token.ToObject<double>());
                }
                catch
                {
                    // 字段格式不认识，继续试下一个
                }
            }
            return -1;
        }

        /// <summary>
        /// 在已运行的实例上追加一条端口转发（联机模块启动后才知道目标端口时用）。
        /// </summary>
        /// <param name="rpcPort">本机 EasyTier 的 RPC 端口。</param>
        /// <param name="protocol">tcp / udp。</param>
        /// <param name="localPort">本机监听端口。</param>
        /// <param name="remoteIp">对端 overlay 地址。</param>
        /// <param name="remotePort">对端端口。</param>
        public static async Task AddPortForwardAsync(int rpcPort, string protocol, int localPort, string remoteIp, int remotePort, CancellationToken ct)
        {
            string text = await RunCliTextAsync(rpcPort, new[]
            {
                "port-forward", "add", protocol,
                $"127.0.0.1:{localPort}", $"{remoteIp}:{remotePort}"
            }, ct).ConfigureAwait(false);
            if (text is null)
                throw new Exception($"向联机模块追加 {protocol} 端口转发失败（{localPort} → {remoteIp}:{remotePort}）");
            Logger.Info($"联机：已追加端口转发 {protocol} 127.0.0.1:{localPort} → {remoteIp}:{remotePort}");
        }

        /// <summary>
        /// 在已运行的实例上追加一条「所有网卡」的端口转发（对方大厅的 UDP 广播需要）。
        /// </summary>
        public static async Task AddPortForwardAnyAsync(int rpcPort, string protocol, int localPort, string remoteIp, int remotePort, CancellationToken ct)
        {
            string text = await RunCliTextAsync(rpcPort, new[]
            {
                "port-forward", "add", protocol,
                $"[::]:{localPort}", $"{remoteIp}:{remotePort}"
            }, ct).ConfigureAwait(false);
            if (text is null)
                throw new Exception($"向联机模块追加 {protocol} 端口转发失败（[::]:{localPort} → {remoteIp}:{remotePort}）");
        }

        /// <summary>
        /// 在 peer 列表里找房主（主机名以给定前缀开头，后缀就是它的 Scaffolding 端口），
        /// 返回对端 overlay 地址和端口。pcl-next 自研大厅用 <c>pclnext-mc-server-</c>，
        /// PCL2-CE 大厅用 <c>scaffolding-mc-server-</c>，两边互不误认。
        /// </summary>
        /// <param name="rpcPort">本机 EasyTier 的 RPC 端口。</param>
        /// <param name="hostnamePrefix">要匹配的主机名前缀；为空时默认按 PCL2-CE 的前缀找。</param>
        /// <param name="ct">取消令牌。</param>
        public static async Task<(string Ip, int ScaffoldPort)?> FindScaffoldHostAsync(int rpcPort, CancellationToken ct, string hostnamePrefix = null)
        {
            string prefix = string.IsNullOrEmpty(hostnamePrefix) ? ModLinkScaffold.HostnamePrefix : hostnamePrefix;
            JToken output = await RunCliAsync(rpcPort, new[] { "peer", "list" }, ct).ConfigureAwait(false);
            JArray list = ExtractPeerArray(output);
            if (list is null)
                return null;
            foreach (JToken item in list)
            {
                string hostname = item["hostname"]?.ToString() ?? "";
                if (!hostname.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                string ip = item["ipv4"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(ip))
                    continue;
                string portText = hostname.Substring(prefix.Length);
                if (!int.TryParse(portText, out int scaffoldPort) || scaffoldPort <= 0 || scaffoldPort > 65535)
                    continue;
                // 太早连上的节点可能是旧连接残留，ping 都没通就先不算
                string cost = item["cost"]?.ToString() ?? "";
                if (!cost.Equals("Local", StringComparison.OrdinalIgnoreCase) && cost.Length == 0)
                    continue;
                Logger.Info($"联机：找到房主 {ip}:{scaffoldPort}（主机名 {hostname}）");
                return (ip, scaffoldPort);
            }
            return null;
        }

        private static JArray ExtractPeerArray(JToken output)
        {
            if (output is null)
                return null;
            if (output is JArray array)
                return array;
            if (output is JObject obj)
            {
                foreach (string key in new[] { "peers", "items", "data" })
                {
                    if (obj[key] is JArray inner)
                        return inner;
                }
            }
            return null;
        }

        private static async Task<JToken> RunCliAsync(int rpcPort, string[] command, CancellationToken ct)
        {
            string text = await RunCliTextAsync(rpcPort, command, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                return JToken.Parse(text);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "联机模块返回的内容不是有效 JSON");
                return null;
            }
        }

        /// <summary>执行一次 easytier-cli 并返回原始输出；失败返回 null。</summary>
        private static async Task<string> RunCliTextAsync(int rpcPort, string[] command, CancellationToken ct)
        {
            if (!File.Exists(CliPath))
                return null;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(CliPath)
                {
                    WorkingDirectory = RootDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                foreach (string arg in new[] { "-p", $"127.0.0.1:{rpcPort}", "-o", "json" })
                    info.ArgumentList.Add(arg);
                foreach (string arg in command)
                    info.ArgumentList.Add(arg);
                using Process process = new Process { StartInfo = info };
                process.Start();
                using CancellationTokenSource timeout = new CancellationTokenSource(5000);
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                Task<string> readTask = process.StandardOutput.ReadToEndAsync(linked.Token);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(linked.Token);
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                string text = await readTask.ConfigureAwait(false);
                string errorText = await errorTask.ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(errorText))
                    Logger.Warn("联机模块命令输出：" + errorText.Trim());
                return text;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null; // CLI 超时，视为暂时拿不到状态
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "执行联机模块命令失败");
                return null;
            }
        }

        #endregion

    }
}
