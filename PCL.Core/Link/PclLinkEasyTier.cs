using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Link
{
    /// <summary>
    /// EasyTier 运行环境的自管理（跨平台版）：下载 → 解压 → 启动/停止 → 状态查询。
    /// <para>
    /// 与 GUI 侧 <c>ModLinkEasyTier</c> 同一套参数语义与同一版本号，但只依赖 BCL，
    /// 因此同一个 CLI 二进制可以在 Windows 与 Linux 上跑出同样的联机行为。
    /// </para>
    /// <para>
    /// 进程以「启动脚本 + 日志重定向」方式脱离 CLI 父进程：命令返回后联机模块继续在后台跑，
    /// 后续 <c>link status / link stop</c> 通过状态文件里的 PID 控制它。
    /// </para>
    /// </summary>
    public static class PclLinkEasyTier
    {
        /// <summary>内置 EasyTier 版本（与发行包文件名一致）。</summary>
        public const string Version = "2.6.4";

        private const string ReleaseBase = "https://github.com/EasyTier/EasyTier/releases/download/v" + Version;

        /// <summary>单候选下载超时：发行包几十 MB。</summary>
        private const int DownloadTimeoutMs = 5 * 60 * 1000;
        /// <summary>发行包体积上限，防止镜像塞入异常大文件。</summary>
        private const long MaxBytes = 200L * 1024 * 1024;

        /// <summary>
        /// 公共节点（社区免费共享，无 SLA，仅作发现与中转）。
        /// <para>
        /// 2026-11-20 实测：GUI 里内置的 <c>public.easytier.top</c> / <c>public2.easytier.cn</c> 已无 A 记录
        /// （权威 DNS 返回 NXDOMAIN），继续用它们只会每次空等 2 s。此处改指向社区节点监控页
        /// （info.qtet.cn/uptime/easytier）在册、且 Windows 与云端 Linux 双向 TCP 可达的节点。
        /// </para>
        /// </summary>
        public static readonly string[] PublicPeers =
        {
            "tcp://225284.xyz:11010",
            "tcp://easytier.weiai.org.cn:11010",
            "tcp://183.230.36.171:11010",
            "tcp://et.sumnight.top:11010"
        };

        /// <summary>
        /// GitHub 下载镜像前缀（空串表示直连）。顺序按 2026-11-20 实测速度排列：
        /// 云端 Linux 上 GitHub 直连只有 3 KB/s，ghfast 约 120 KB/s。
        /// </summary>
        public static readonly string[] DownloadPrefixes =
        {
            "https://ghfast.top/",
            "https://gh-proxy.com/",
            "",
            "https://ghproxy.net/"
        };

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>联机模块安装根目录（随 <c>--workdir</c> 走，不写用户 APPDATA）。</summary>
        public static string RootDir(string workDir) => Path.Combine(workDir, "link", "easytier");

        public static string CoreFileName => OperatingSystem.IsWindows() ? "easytier-core.exe" : "easytier-core";

        public static string CliFileName => OperatingSystem.IsWindows() ? "easytier-cli.exe" : "easytier-cli";

        public static string CorePath(string workDir) => Path.Combine(RootDir(workDir), CoreFileName);

        public static string CliPath(string workDir) => Path.Combine(RootDir(workDir), CliFileName);

        /// <summary>联机模块是否已经可用（两个可执行文件都存在）。</summary>
        public static bool IsReady(string workDir) => File.Exists(CorePath(workDir)) && File.Exists(CliPath(workDir));

        /// <summary>当前平台对应发行包名，例如 <c>easytier-linux-x86_64-v2.6.4.zip</c>。</summary>
        public static string PackageName()
        {
            string os = OperatingSystem.IsWindows() ? "windows"
                : OperatingSystem.IsLinux() ? "linux"
                : OperatingSystem.IsMacOS() ? "macos"
                : throw new PlatformNotSupportedException("联机模块暂不支持当前操作系统");
            return $"easytier-{os}-{ArchName()}-v{Version}.zip";
        }

        /// <summary>发行包完整地址（不含镜像前缀）。</summary>
        public static string PackageUrl() => $"{ReleaseBase}/{PackageName()}";

        private static string ArchName()
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => OperatingSystem.IsLinux() ? "aarch64" : "arm64",
                Architecture.Arm => "armv7hf",
                Architecture.X86 => "i686",
                _ => OperatingSystem.IsLinux() ? "x86_64" : "x86_64"
            };
        }

        /// <summary>确保 EasyTier 已安装。已安装则立即返回；否则下载并解压到 <see cref="RootDir"/>。</summary>
        public static async Task EnsureAsync(string workDir, Action<string>? status, CancellationToken ct)
        {
            if (IsReady(workDir)) return;
            Directory.CreateDirectory(RootDir(workDir));

            string zipPath = Path.Combine(Path.GetTempPath(), "pclcli-easytier-" + Guid.NewGuid().ToString("N") + ".zip");
            Exception? lastError = null;
            try
            {
                bool downloaded = false;
                foreach (string prefix in DownloadPrefixes)
                {
                    ct.ThrowIfCancellationRequested();
                    string url = prefix + PackageUrl();
                    try
                    {
                        status?.Invoke($"正在下载联机模块（EasyTier {Version}）：{(prefix.Length == 0 ? "GitHub 直连" : prefix)}");
                        await DownloadAsync(url, zipPath, status, ct).ConfigureAwait(false);
                        if (new FileInfo(zipPath).Length < 5L * 1024 * 1024)
                            throw new Exception("下载的联机模块体积异常");
                        downloaded = true;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        SafeDelete(zipPath);
                        status?.Invoke($"下载源不可用（{ex.Message}），切换下一个镜像…");
                    }
                }

                if (!downloaded)
                    throw new Exception("联机模块下载失败（所有下载源都不可用）：" + lastError?.Message, lastError);

                status?.Invoke("正在解压联机模块…");
                string extractDir = Path.Combine(Path.GetTempPath(), "pclcli-easytier-" + Guid.NewGuid().ToString("N"));
                try
                {
                    ZipFile.ExtractToDirectory(zipPath, extractDir, true);
                    string[] cores = Directory.GetFiles(extractDir, CoreFileName, SearchOption.AllDirectories);
                    if (cores.Length == 0)
                        throw new Exception($"联机模块压缩包里没有 {CoreFileName}，下载可能损坏");
                    string sourceDir = Path.GetDirectoryName(cores[0])!;
                    foreach (string file in Directory.GetFiles(sourceDir))
                    {
                        string target = Path.Combine(RootDir(workDir), Path.GetFileName(file));
                        File.Copy(file, target, true);
                        if (!OperatingSystem.IsWindows()
                            && Path.GetFileName(file) is "easytier-core" or "easytier-cli")
                        {
                            File.SetUnixFileMode(target,
                                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                        }
                    }
                }
                finally
                {
                    try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { /* 忽略清理失败 */ }
                }

                if (!IsReady(workDir))
                    throw new Exception("联机模块安装后仍不完整，请检查目录写入权限：" + RootDir(workDir));
                status?.Invoke("联机模块已就绪");
            }
            finally
            {
                SafeDelete(zipPath);
            }
        }

        private static async Task DownloadAsync(string url, string target, Action<string>? status, CancellationToken ct)
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(DownloadTimeoutMs);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using HttpResponseMessage response = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? -1;
            if (total > MaxBytes)
                throw new Exception($"发行包体积异常（{total} 字节），拒绝下载");

            await using Stream source = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            await using FileStream file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920];
            long received = 0;
            int lastPct = -10;
            Stopwatch watch = Stopwatch.StartNew();
            while (true)
            {
                int read = await source.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                if (read <= 0) break;
                await file.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                received += read;
                if (received > MaxBytes)
                    throw new Exception("发行包下载超出体积上限，已中止");
                // 慢源熔断：20 秒还不到 512 KB 就换下一个镜像，别把 5 分钟超时耗死在 3 KB/s 的直连上
                if (watch.Elapsed.TotalSeconds > 20 && received < 512 * 1024)
                    throw new Exception($"下载速度过慢（20 秒仅 {received / 1024.0:0} KB），换下一个下载源");
                if (status is null) continue;
                double speed = received / 1024.0 / Math.Max(0.5, watch.Elapsed.TotalSeconds);
                if (total > 0)
                {
                    int pct = (int)(received * 100 / total);
                    if (pct >= lastPct + 10)
                    {
                        lastPct = pct;
                        status($"正在下载联机模块 {pct}%（{received / 1048576.0:0.0}/{total / 1048576.0:0.0} MB，{speed:0} KB/s）");
                    }
                }
                else
                {
                    status($"正在下载联机模块（已下载 {received / 1048576.0:0.0} MB，{speed:0} KB/s）");
                }
            }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略 */ }
        }

        /// <summary>启动后返回的进程信息。</summary>
        public sealed class LinkProcess
        {
            public int Pid { get; init; }
            public string LogPath { get; init; } = "";
            public string LauncherPath { get; init; } = "";
        }

        /// <summary>
        /// 以「启动脚本 + 日志重定向」方式启动 EasyTier 核心，CLI 退出后它继续在后台运行。
        /// </summary>
        /// <param name="workDir">CLI 工作目录。</param>
        /// <param name="arguments">EasyTier 参数。</param>
        /// <param name="tag">启动脚本与日志名后缀（host / join）。</param>
        public static LinkProcess Launch(string workDir, IReadOnlyList<string> arguments, string tag)
        {
            if (!IsReady(workDir))
                throw new Exception("联机模块尚未安装完成，先执行 link install");

            Directory.CreateDirectory(RootDir(workDir));
            string runDir = Path.Combine(workDir, "link");
            Directory.CreateDirectory(runDir);
            string logPath = Path.Combine(runDir, $"easytier-{tag}.log");
            string scriptPath = Path.Combine(runDir,
                OperatingSystem.IsWindows() ? $"run-{tag}.cmd" : $"run-{tag}.sh");

            StringBuilder script = new StringBuilder();
            if (OperatingSystem.IsWindows())
            {
                script.AppendLine("@echo off");
                script.Append('"').Append(CorePath(workDir)).Append('"');
                foreach (string arg in arguments)
                    script.Append(' ').Append('"').Append(arg.Replace("\"", "")).Append('"');
                script.Append(" >> ").Append('"').Append(logPath).Append('"').AppendLine(" 2>&1");
            }
            else
            {
                script.AppendLine("#!/bin/sh");
                script.Append("exec ").Append('"').Append(CorePath(workDir)).Append('"');
                foreach (string arg in arguments)
                    script.Append(' ').Append('"').Append(arg.Replace("\"", "")).Append('"');
                script.Append(" >> ").Append('"').Append(logPath).Append('"').AppendLine(" 2>&1");
            }

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

            using Process process = new Process { StartInfo = info };
            process.Start();
            return new LinkProcess { Pid = process.Id, LogPath = logPath, LauncherPath = scriptPath };
        }

        /// <summary>终止联机模块（含脚本派生的子进程）。</summary>
        public static bool Stop(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using Process process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                return true;
            }
            catch (ArgumentException)
            {
                return false; // 进程已不存在
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>PID 是否仍存活。</summary>
        public static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using Process process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>下一次 <see cref="FindFreePort"/> 从哪里开始找（避免连续多次调用都返回同一个端口）。</summary>
        private static int _nextPortCandidate = 30000;

        /// <summary>分配一个本机空闲端口（TCP 与 UDP 都要能绑）。</summary>
        public static int FindFreePort(int start = 30000, ISet<int>? reserved = null)
        {
            int candidate = Math.Max(Math.Max(1024, start), _nextPortCandidate);
            for (int attempt = 0; attempt < 20000; attempt++)
            {
                if (candidate >= 65000) candidate = 10240;
                if (reserved is null || !reserved.Contains(candidate))
                {
                    if (IsPortFree(candidate))
                    {
                        reserved?.Add(candidate);
                        _nextPortCandidate = candidate + 1;
                        return candidate;
                    }
                }
                candidate++;
            }
            throw new Exception("找不到空闲的 TCP/UDP 端口");
        }

        /// <summary>指定端口当前是否可绑（TCP + UDP + 回环）。</summary>
        public static bool IsPortUsable(int port) => IsPortFree(port);

        private static bool IsPortFree(int port)
        {
            try
            {
                TcpListener listener = new TcpListener(System.Net.IPAddress.Any, port);
                listener.Start();
                listener.Stop();
            }
            catch
            {
                return false;
            }
            try
            {
                using UdpClient udp = new UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
            }
            catch
            {
                return false;
            }
            try
            {
                TcpListener loopback = new TcpListener(System.Net.IPAddress.Loopback, port);
                loopback.Start();
                loopback.Stop();
            }
            catch
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 通过 <c>easytier-cli -o json</c> 查询本机实例；<paramref name="rpcPort"/> 是核心的 rpc-portal 端口。
        /// 查询失败（未启动 / 端口不通 / 输出非 JSON）返回 null。
        /// </summary>
        public static async Task<JsonNode?> QueryAsync(string workDir, int rpcPort, string[] command,
            CancellationToken ct, int timeoutMs = 5000)
        {
            string cli = CliPath(workDir);
            if (!File.Exists(cli)) return null;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(cli)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                info.ArgumentList.Add("-p");
                info.ArgumentList.Add($"127.0.0.1:{rpcPort}");
                info.ArgumentList.Add("-o");
                info.ArgumentList.Add("json");
                foreach (string arg in command) info.ArgumentList.Add(arg);

                using Process process = new Process { StartInfo = info };
                process.Start();
                using CancellationTokenSource timeout = new CancellationTokenSource(timeoutMs);
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(linked.Token);
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                string text = await stdout.ConfigureAwait(false);
                await stderr.ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return JsonNode.Parse(text);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 本机实例是否已经在跑（<c>node</c> 查询成功即为真）。
        /// </summary>
        public static async Task<bool> IsNodeAliveAsync(string workDir, int rpcPort, CancellationToken ct)
            => await QueryAsync(workDir, rpcPort, new[] { "node" }, ct).ConfigureAwait(false) is not null;

        /// <summary>解析 peer 列表并统计远端节点数（cost 为 Local 的是自己）。</summary>
        public static int CountRemotePeers(JsonNode? output)
        {
            JsonArray? list = ExtractPeerArray(output);
            if (list is null) return -1;
            int remote = 0;
            foreach (JsonNode? item in list)
            {
                if (item is null) continue;
                string cost = item["cost"]?.ToString() ?? "";
                if (!cost.Equals("Local", StringComparison.OrdinalIgnoreCase)) remote++;
            }
            return remote;
        }

        /// <summary>
        /// 只统计「同一房间」的成员，排除公共共享节点。
        /// <para>
        /// 公共节点也是 peer 条目（hostname 形如 <c>PublicServer_xxx</c>），
        /// 把它们算进来会让「加入成功」的判断在根本没连上房主时就成立。
        /// </para>
        /// </summary>
        public static int CountRoomPeers(JsonNode? output)
        {
            JsonArray? list = ExtractPeerArray(output);
            if (list is null) return -1;
            int count = 0;
            foreach (JsonNode? item in list)
            {
                if (item is null || IsInfrastructurePeer(item)) continue;
                string cost = item["cost"]?.ToString() ?? "";
                if (!cost.Equals("Local", StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        /// <summary>peer 列表里是否已出现房主（主机名带 <paramref name="hostnamePrefix"/>，或 overlay IP 匹配）。</summary>
        public static bool HasRoomHost(JsonNode? output, string hostnamePrefix = "pclcli-host-",
            string? overlayIp = null)
        {
            JsonArray? list = ExtractPeerArray(output);
            if (list is null) return false;
            foreach (JsonNode? item in list)
            {
                if (item is null || IsInfrastructurePeer(item)) continue;
                string hostname = item["hostname"]?.ToString() ?? "";
                string ip = item["ipv4"]?.ToString() ?? "";
                if (hostname.StartsWith(hostnamePrefix, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(overlayIp) && ip == overlayIp) return true;
            }
            return false;
        }

        private static bool IsInfrastructurePeer(JsonNode item)
        {
            string hostname = item["hostname"]?.ToString() ?? "";
            return hostname.StartsWith("PublicServer", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把房间成员压成 <c>{ip, hostname, cost, latencyMs}</c> 形式，便于 CLI 直接输出（不含公共节点）。</summary>
        public static List<(string Ip, string Hostname, string Cost, int LatencyMs)> DescribeRoomPeers(JsonNode? output)
        {
            List<(string, string, string, int)> result = new();
            JsonArray? list = ExtractPeerArray(output);
            if (list is null) return result;
            foreach (JsonNode? item in list)
            {
                if (item is null || IsInfrastructurePeer(item)) continue;
                string ip = item["ipv4"]?.ToString() ?? "";
                string hostname = item["hostname"]?.ToString() ?? "";
                string cost = item["cost"]?.ToString() ?? "";
                int latency = -1;
                foreach (string key in new[] { "lat_ms", "latency_ms", "latency", "lat_ms_avg" })
                {
                    if (item[key] is null) continue;
                    try
                    {
                        latency = (int)Math.Round(double.Parse(item[key]!.ToString(), System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    }
                    catch { /* 字段格式不认识，继续 */ }
                }
                result.Add((ip, hostname, cost, latency));
            }
            return result;
        }

        private static JsonArray? ExtractPeerArray(JsonNode? output)
        {
            if (output is null) return null;
            if (output is JsonArray array) return array;
            if (output is JsonObject obj)
            {
                foreach (string key in new[] { "peers", "items", "data" })
                {
                    if (obj[key] is JsonArray inner) return inner;
                }
            }
            return null;
        }
    }
}
