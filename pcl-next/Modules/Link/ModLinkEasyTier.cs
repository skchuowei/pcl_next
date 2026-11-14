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
        /// </summary>
        /// <param name="onStatus">进度回调（文案，已包含百分比），会在后台线程调用。</param>
        public static async Task EnsureAsync(Action<string> onStatus, CancellationToken ct)
        {
            if (IsReady)
                return;
            Directory.CreateDirectory(RootDir);
            string arch = GetArchName();
            string zipUrlName = ReleaseName.Replace("{arch}", arch);
            string[] mirrors = new[]
            {
                $"{ReleaseBase}/{zipUrlName}",
                $"https://ghfast.top/{ReleaseBase}/{zipUrlName}",
                $"https://gh.llkk.cc/{ReleaseBase}/{zipUrlName}",
                $"https://ghproxy.net/{ReleaseBase}/{zipUrlName}",
                $"https://gh-proxy.com/{ReleaseBase}/{zipUrlName}"
            };
            string zipPath = Path.Combine(Path.GetTempPath(), "pclnext-easytier-" + Guid.NewGuid().ToString("N") + ".zip");
            Exception lastError = null;
            foreach (string url in mirrors)
            {
                try
                {
                    onStatus($"正在下载联机模块（EasyTier {Version}）…");
                    using CancellationTokenSource mirrorTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, mirrorTimeout.Token);
                    await DownloadAsync(url, zipPath, onStatus, linked.Token).ConfigureAwait(false);
                    if (new FileInfo(zipPath).Length < 1024 * 1024 * 5)
                        throw new Exception("下载的联机模块体积异常");
                    break;
                }
                catch (OperationCanceledException)
                {
                    SafeDelete(zipPath);
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    SafeDelete(zipPath);
                }
            }
            if (!File.Exists(zipPath))
                throw new Exception("联机模块下载失败（所有下载源都不可用）：" + lastError?.Message, lastError);

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
                SafeDelete(zipPath);
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { /* 忽略清理失败 */ }
            }
            if (!IsReady)
                throw new Exception("联机模块安装后仍不完整，请检查目录写入权限：" + RootDir);
            onStatus("联机模块已就绪");
        }

        private static async Task DownloadAsync(string url, string target, Action<string> onStatus, CancellationToken ct)
        {
            using HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("pcl-next-link/" + Version);
            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? -1;
            await using FileStream fileStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await using Stream netStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            byte[] buffer = new byte[81920];
            long read = 0;
            int lastReported = -10;
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                int count = await netStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (count <= 0)
                    break;
                await fileStream.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                read += count;
                if (total > 0)
                {
                    int pct = (int)(read * 100 / total);
                    if (pct >= lastReported + 10)
                    {
                        lastReported = pct;
                        double seconds = Math.Max(0.5, stopwatch.Elapsed.TotalSeconds);
                        double speed = read / 1024.0 / seconds;
                        onStatus($"正在下载联机模块 {pct}%（{read / 1048576.0:0.0} / {total / 1048576.0:0.0} MB，{speed:0} KB/s）");
                    }
                }
                else if (read > 0 && read % (1024 * 1024) < 81920)
                {
                    double seconds = Math.Max(0.5, stopwatch.Elapsed.TotalSeconds);
                    onStatus($"正在下载联机模块（已下载 {read / 1048576.0:0.0} MB，{read / 1024.0 / seconds:0} KB/s）");
                }
            }
            await fileStream.FlushAsync(ct).ConfigureAwait(false);
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
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
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

        private static async Task<JToken> RunCliAsync(int rpcPort, string[] command, CancellationToken ct)
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
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                string text = await readTask.ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text))
                    return null;
                return JToken.Parse(text);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null; // CLI 超时，视为暂时拿不到状态
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "查询联机状态失败");
                return null;
            }
        }

        #endregion

    }
}
