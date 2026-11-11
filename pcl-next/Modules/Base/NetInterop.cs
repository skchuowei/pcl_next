using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using MeloongCore.Extensions;
using PCL.Download;

namespace PCL
{

    /// <summary>
/// VB 下载编排层与 C# 下载引擎（PCL.Download）的桥接。
/// <para/>接管策略：保留 VB 侧的 NetFile/LoaderDownload/NetManager 编排与 UI 统计，
/// 仅把"字节传输"部分（原 NetThread 的 HTTP 分片下载）交给 C# 引擎：
/// NetFile.TryBeginThread 在文件的首线程时机 CAS 抢占后调用 <see cref="RunCSharpDownload"/>，
/// 引擎内部自行完成多源 failover、Range 分片、合并与校验，
/// 完成后回写 VB 侧状态（DownloadDone / FileSize / State / Finish / Fail）。
/// </summary>
    public static class NetInterop
    {

        /// <summary>
    /// 是否启用 C# 下载引擎执行文件传输。False 时回退到 VB NetThread 实现。
    /// </summary>
        public static bool IsCSharpDownloadEnabled = true;

        /// <summary>
    /// 跨文件共享的全局并发下载预算（对应 VB 路径的 NetTaskThreadCount 上限）。
    /// C# 引擎按文件各起一个实例，若不共享预算，所有文件会同时发起下载形成连接风暴，
    /// 极易触发下载源限速（表现为总速度骤降至几十 KB/s）。
    /// </summary>
        private readonly static ConcurrencyBudget GlobalBudget = new ConcurrencyBudget(32);

        /// <summary>
    /// 用 C# 引擎下载单个文件（由 NetFile.TryBeginThread 抢占标记后调用）。
    /// </summary>
        public static void RunCSharpDownload(ModNet.NetFile File)
        {
            try
            {
                // ---------- 构造请求 ----------
                var Sources = new List<string>();
                foreach (var Source in File.Sources)
                    Sources.Add(Source.Url); // URL 已在 NetFile 构造时经 SecretCdnSign 签名
                FileCheckRule Check = null;
                if (File.Check is not null)
                {
                    Check = new FileCheckRule()
                    {
                        ActualSize = File.Check.ActualSize,
                        MinSize = File.Check.MinSize,
                        Hash = File.Check.Hash,
                        IsJson = File.Check.IsJson
                    };
                }
                // 按 URL 生成请求头：完全复用 SecretHeadersSign 的既有规则
                // （User-Agent / pcl2 Referer / CurseForge Key），并捕获文件的浏览器模拟开关
                bool Simulate = File.SimulateBrowserHeaders;
                Func<string, IEnumerable<KeyValuePair<string, string>>> HeaderProvider = (Url) =>
        {
            var Req = new HttpRequestMessage(HttpMethod.Get, Url);
            ModSecret.SecretHeadersSign(Url, ref Req, Simulate);
            return Req.Headers.SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v))).ToList();
        };
                var Request = new DownloadFileRequest()
                {
                    Sources = Sources,
                    LocalPath = File.LocalPath,
                    Check = Check,
                    SimulateBrowserHeaders = File.SimulateBrowserHeaders,
                    HeaderProvider = HeaderProvider
                };

                // ---------- 构造选项 ----------
                // 刷新全局并发上限（用户设置 ToolDownloadThread），所有文件的引擎实例共享同一预算
                GlobalBudget.Limit = ModNet.NetTaskThreadLimit;
                var Options = new DownloadOptions()
                {
                    MaxConcurrentChunks = ModNet.NetTaskThreadLimit,
                    SharedBudget = GlobalBudget,
                    SpeedLimitHigh = ModNet.NetTaskSpeedLimitHigh,
                    Log = new Action<string>(m => Logger.Info(m)),
                    LogWarn = new Action<Exception, string>((ex, m) => Logger.Warn(ex, m))
                };

                // ---------- 状态镜像 ----------
                var Cts = new CancellationTokenSource();
                File.CSharpCts = Cts;
                lock (File.LockState)
                {
                    if (File.State < ModNet.NetState.Connecting)
                        File.State = ModNet.NetState.Connecting;
                }
                long LastDone = 0L;
                bool SizeMirrored = false;
                // 用户已通过 VB 侧取消/失败（State 被置为 Canceled）或已完成 → 中断引擎
                // 镜像文件大小（供 File.Progress 计算百分比）
                // 镜像字节计数
                // 用 Exchange 原子取旧值：进度回调可能并发执行，原先「读 LastDone → 再赋值」会让
                // 同一段字节被重复累加（速度虚高、进度甚至超过 100%）
                // 必须同时处理负增量：引擎因失败重启而重置进度时，镜像值要跟着回退，
                // 否则只增不减的累计值会超过文件大小（实测 3.7MB 的文件累加到 7.9MB）→ 进度 >100% → NaN
                // 镜像状态：有活动分块或已有数据 → Downloading
                // 注意：C# 引擎接管时状态只会被设到 Connecting(1)，而原来的门槛要求 State >= Reading(2)，
                // 条件恒不成立 → 进度长期钉在 Connecting 的 2%，直到 Finish() 直接跳到 100%。
                // 这就是真机上「一直卡 2%，过一会突然下载完成」的根因。
                var ProgressCallback = new Progress<DownloadProgress>(p => { try { if (File.State >= ModNet.NetState.Finished) { Cts.Cancel(); return; } if (!SizeMirrored && p.TotalBytesExpected >= 0L) { Interlocked.Exchange(ref File.FileSize, p.TotalBytesExpected); SizeMirrored = true; } long Previous = Interlocked.Exchange(ref LastDone, p.TotalBytesDone); long Delta = p.TotalBytesDone - Previous; if (Delta != 0L) { Interlocked.Add(ref File.DownloadDone, Delta); Interlocked.Add(ref ModNet.NetManager.DownloadDone, Delta); if (File.DownloadDone < 0L) Interlocked.Exchange(ref File.DownloadDone, 0L); if (ModNet.NetManager.DownloadDone < 0L) Interlocked.Exchange(ref ModNet.NetManager.DownloadDone, 0L); } if (p.ActiveChunks > 0 || p.TotalBytesDone > 0L) { lock (File.LockState) { if (File.State < ModNet.NetState.Merging) { File.State = ModNet.NetState.Downloading; } } } } catch (Exception ex) { Logger.Warn(ex, "C# 下载引擎进度镜像出错"); } });

                // ---------- 执行 ----------
                var Engine = new DownloadEngine();
                // 引擎的本地复用结果也计入完成
                // VB 侧主动取消/失败导致的中断，状态已由 VB 管理
                // 把错误分发到各下载源，供 LoaderDownload.OnFileFail 汇总展示
                Task.Run(async () => { try { var Report = await Engine.DownloadAsync(new[] { Request }, Options, ProgressCallback, Cts.Token); var Reused = Report.Files.FirstOrDefault(); if (Reused is not null && Reused.IsLocalReuse && File.FileSize < 0L) { Interlocked.Exchange(ref File.FileSize, -1); } long BytesLog = Reused is not null ? Reused.BytesDownloaded : 0L; File.Finish(); Logger.Info($"{File.LocalName}：C# 引擎下载完成（{StringUtils.FormatByteSize(BytesLog)}）"); } catch (OperationCanceledException) when (File.State >= ModNet.NetState.Finished) { } catch (Exception ex) { IReadOnlyList<Exception> SourceErrors = new[] { ex }; DownloadFailedException Failed = ex as DownloadFailedException; if (Failed is not null) { foreach (var kvp in Failed.ErrorsByFile) { SourceErrors = kvp.Value; break; } } for (int i = 0, loopTo = File.Sources.Count - 1; i <= loopTo; i++) { if (i < SourceErrors.Count) { File.Sources[i].Ex = SourceErrors[i]; File.Sources[i].IsFailed = true; } } File.Fail(ex); } }); // 复用场景引擎未下载，大小留给 VB 校验逻辑
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                // 单文件任务
            }
            catch (Exception ex)
            {
                // 接管失败：撤销抢占标记并回退 VB 实现
                Interlocked.Exchange(ref File.CSharpDownloadStarted, 0);
                Logger.Warn(ex, $"C# 下载引擎启动失败，回退 VB 实现（{File.LocalName}）");
            }
        }

    }
}