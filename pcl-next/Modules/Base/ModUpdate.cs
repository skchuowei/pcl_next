using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MeloongCore;
using Newtonsoft.Json.Linq;

namespace PCL
{
    /// <summary>
    /// 启动器自动更新（2026-11-20 上线）。
    ///
    /// 从 GitHub Releases API 拉取本仓库的版本列表，解析 tag 得到渠道与 VersionCode，
    /// 按用户选择的「更新渠道」与「陆续退出测试版」策略挑出目标版本，下载对应架构的
    /// 单文件 exe，生成一个等待主进程退出后覆盖并重启的批处理脚本，最后退出本进程。
    ///
    /// 设计要点：
    /// 1. tag 解析完全复刻 scripts/Set-VersionInfo.ps1 的号段表，客户端与 CI 同源，
    ///    避免"服务端算 5060、客户端算成别的"导致误判版本。
    /// 2. VersionCode 本身就按渠道阶梯递增（dev +0 / slush +10 / alpha +20 / beta +60 /
    ///    rc +90，每版 +1），所以"只升不降"和"晋级到最高渠道"都可直接用数值比较实现，
    ///    无需另建渠道优先级表。
    /// 3. 渠道是编译期常量，进程一旦被替换，重启后的 exe 天然就是新渠道——所以晋级只要
    ///    选中目标 release 即可，生效渠道在下载完成时写回设置，防止重复提示。
    /// 4. 全流程只读 GitHub 公开 API，失败一律记日志并静默返回，绝不阻塞启动。
    /// </summary>
    public static class ModUpdate
    {
        private const string Repo = "skchuowei/pcl_next";
        private const string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases?per_page=50";
        private const string ReleasesUrl = "https://github.com/" + Repo + "/releases";

        /// <summary>一次 Release 的解析结果。</summary>
        public class ReleaseInfo
        {
            public string Tag;
            public string VersionName;
            public int VersionCode;
            public ModBuildChannel.Channels Channel;
            public string X64Asset;   // browser_download_url
            public string Arm64Asset;
            public string Body;       // Release 说明正文（更新日志用）
        }

        /// <summary>更新检查结果，供 UI 展示与后续下载。</summary>
        public class CheckResult
        {
            public bool HasUpdate;
            public ReleaseInfo Target;
            public bool IsPromotion;     // 是否为渠道晋级（而非同渠道小版本升级）
            public string Message;       // 面向用户的说明
        }

        #region tag 解析（与 Set-VersionInfo.ps1 同源）

        /// <summary>渠道 → 号段偏移，严格对照 Set-VersionInfo.ps1 的 $Offset。</summary>
        private static int OffsetOf(ModBuildChannel.Channels Channel)
        {
            switch (Channel)
            {
                case ModBuildChannel.Channels.Dev: return 0;
                case ModBuildChannel.Channels.Slush: return 10;
                case ModBuildChannel.Channels.Alpha: return 20;
                case ModBuildChannel.Channels.Beta: return 60;
                case ModBuildChannel.Channels.Rc: return 90;
                case ModBuildChannel.Channels.Snapshot: return 95;
                default: return 0;
            }
        }

        private static readonly Regex TagRegex = new Regex(
            @"^(?:v)?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\.(?<build>\d+)(?:-(?<chan>dev|slush|alpha|beta|rc|sp|snapshot)\.?(?<seq>\d+)?)?(?:-(?<platform>Windows|Linux|Android))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 解析一个 tag。返回 null 表示不是有效的 Windows 主阶梯版本 tag
        /// （Linux/Android 平台 tag、非版本号 tag 一律忽略）。
        /// </summary>
        public static ReleaseInfo ParseTag(string Tag)
        {
            if (string.IsNullOrEmpty(Tag))
                return null;
            var m = TagRegex.Match(Tag.Trim());
            if (!m.Success)
                return null;
            // 平台后缀：只接受 Windows 与"无后缀"（旧 tag）。Linux / Android 是独立发布通道，忽略。
            string Platform = m.Groups["platform"].Value;
            if (Platform.Equals("Linux", StringComparison.OrdinalIgnoreCase)
                || Platform.Equals("Android", StringComparison.OrdinalIgnoreCase))
                return null;

            int Minor = int.Parse(m.Groups["minor"].Value);
            int Seq = m.Groups["seq"].Success ? int.Parse(m.Groups["seq"].Value) : 1;
            string ChanStr = m.Groups["chan"].Value.ToLowerInvariant();
            ModBuildChannel.Channels Channel;
            switch (ChanStr)
            {
                case "dev": Channel = ModBuildChannel.Channels.Dev; break;
                case "slush": Channel = ModBuildChannel.Channels.Slush; break;
                case "alpha": Channel = ModBuildChannel.Channels.Alpha; break;
                case "beta": Channel = ModBuildChannel.Channels.Beta; break;
                case "sp":
                case "snapshot": Channel = ModBuildChannel.Channels.Snapshot; break;
                default: Channel = ModBuildChannel.Channels.Rc; break; // 无渠道段 = 正式版
            }

            int Base = ModBuildChannel.ChannelVersionCodeBase + 100 * (Minor - 100);
            int VersionCode = Base + OffsetOf(Channel) + (Seq - 1);
            string VersionName = $"{m.Groups["major"].Value}.{m.Groups["minor"].Value}.{m.Groups["patch"].Value}.{m.Groups["build"].Value}";

            return new ReleaseInfo
            {
                Tag = Tag.Trim(),
                VersionName = VersionName,
                VersionCode = VersionCode,
                Channel = Channel
            };
        }

        #endregion

        #region 拉取与筛选

        /// <summary>从 GitHub API 拉取并解析全部可用版本，按 VersionCode 降序。</summary>
        public static List<ReleaseInfo> FetchReleases()
        {
            string Json = ModNet.NetRequestByClientRetry(
                ApiUrl,
                Headers: new[,]
                {
                    { "User-Agent", "PCL-Next-Updater" },
                    { "Accept", "application/vnd.github+json" },
                    { "X-GitHub-Api-Version", "2022-11-28" }
                },
                RequireJson: true);
            var Array = JArray.Parse(Json);
            var Result = new List<ReleaseInfo>();
            foreach (var Item in Array)
            {
                if (Item is not JObject Obj)
                    continue;
                bool Draft = (bool?)Obj["draft"] ?? false;
                if (Draft)
                    continue; // 草稿不对外，不能作为更新目标
                string Tag = (string)Obj["tag_name"];
                var Info = ParseTag(Tag);
                if (Info == null)
                    continue;

                // 挑选与当前架构匹配的附件。命名规则见 release.yml 的 pack 步骤：
                //   x64   → PCL-Next-<版本>-<代号>-<构建号>.exe
                //   arm64 → PCL-Next-<版本>-<代号>-<构建号>-arm64.exe
                var Assets = Obj["assets"] as JArray;
                if (Assets != null)
                {
                    foreach (var A in Assets)
                    {
                        string Name = (string)A["name"] ?? "";
                        string Url = (string)A["browser_download_url"] ?? "";
                        if (!Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(Url))
                            continue;
                        bool IsArm = Name.EndsWith("-arm64.exe", StringComparison.OrdinalIgnoreCase);
                        if (IsArm)
                            Info.Arm64Asset = Url;
                        else
                            Info.X64Asset = Url;
                    }
                }
                Info.Body = (string)Obj["body"] ?? "";
                Result.Add(Info);
            }
            return Result.OrderByDescending(r => r.VersionCode).ToList();
        }

        /// <summary>当前进程对应的架构所需的附件地址。</summary>
        private static string AssetFor(ReleaseInfo Info)
        {
            bool IsArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            return IsArm ? Info.Arm64Asset : Info.X64Asset;
        }

        /// <summary>该 release 是否提供了当前架构的附件（双 RID 缺一不可发布，这里再兜一层）。</summary>
        private static bool HasAsset(ReleaseInfo Info)
        {
            return !string.IsNullOrEmpty(AssetFor(Info));
        }

        #endregion

        #region 渠道策略

        /// <summary>把设置里保存的渠道序号转成枚举；-1 或越界表示"跟随当前编译渠道"。</summary>
        private static ModBuildChannel.Channels ResolveTargetChannel()
        {
            int Saved = Settings.Get<int>("SystemUpdateChannel"); // 默认 -1 = 跟随当前编译渠道
            if (Saved >= 0 && Saved <= (int)ModBuildChannel.Channels.Snapshot)
                return (ModBuildChannel.Channels)Saved;
            return ModBuildChannel.Channel;
        }

        /// <summary>
        /// 核心策略：从版本列表中挑出应当升级到的目标。
        ///
        /// - 只升不降：只考虑 VersionCode > 当前 的版本（挡住跨渠道切换后的"降级"）。
        /// - 渠道过滤：只考虑 Channel >= 用户选择的渠道（不接受更低渠道的更新）。
        /// - 陆续退出测试版（Graduate）：在上述候选里取渠道最高的一版，一次跳到最高；
        ///   不开启则只取所选渠道内的最新版。
        /// </summary>
        public static CheckResult Check(List<ReleaseInfo> Releases = null)
        {
            var Result = new CheckResult();
            try
            {
                if (Releases == null)
                    Releases = FetchReleases();

                var TargetChannel = ResolveTargetChannel();
                bool Graduate = Settings.Get<bool>("SystemUpdateGraduate");
                int CurrentCode = ModBase.VersionCode;

                // 候选：版本更高 + 渠道不低于所选渠道 + 有当前架构附件
                var Candidates = Releases
                    .Where(r => r.VersionCode > CurrentCode
                                && (int)r.Channel >= (int)TargetChannel
                                && HasAsset(r))
                    .ToList();
                if (Candidates.Count == 0)
                {
                    Result.Message = "当前已是所选渠道的最新版本";
                    return Result;
                }

                ReleaseInfo Pick;
                if (Graduate)
                {
                    // 一次跳到最高：先按渠道取最高，再在该渠道内取 VersionCode 最大。
                    int HighestChannel = Candidates.Max(r => (int)r.Channel);
                    Pick = Candidates
                        .Where(r => (int)r.Channel == HighestChannel)
                        .OrderByDescending(r => r.VersionCode)
                        .First();
                }
                else
                {
                    // 不晋级：只看所选渠道内的最新版。
                    var InChannel = Candidates.Where(r => r.Channel == TargetChannel).ToList();
                    if (InChannel.Count == 0)
                    {
                        Result.Message = $"所选渠道暂无新版本（存在更高渠道的更新，开启「陆续退出测试版」即可升级）";
                        return Result;
                    }
                    Pick = InChannel.OrderByDescending(r => r.VersionCode).First();
                }

                Result.HasUpdate = true;
                Result.Target = Pick;
                Result.IsPromotion = Pick.Channel != ModBuildChannel.Channel;
                Result.Message = Result.IsPromotion
                    ? $"发现新版本：{Pick.VersionName}（{ModChannelName(Pick.Channel)}，由当前 {ModChannelName(ModBuildChannel.Channel)} 晋级）"
                    : $"发现新版本：{Pick.VersionName}（{ModChannelName(Pick.Channel)}）";
                Logger.Info($"自动更新检查命中：{Pick.Tag}（VersionCode {Pick.VersionCode}），晋级={Result.IsPromotion}");
            }
            catch (Exception ex)
            {
                Result.Message = "检查更新失败：" + ex.Message;
                Logger.Warn(ex, "检查更新失败");
            }
            return Result;
        }

        private static string ModChannelName(ModBuildChannel.Channels C)
        {
            switch (C)
            {
                case ModBuildChannel.Channels.Dev: return "开发版 dev";
                case ModBuildChannel.Channels.Slush: return "收敛版 slush";
                case ModBuildChannel.Channels.Alpha: return "内测版 alpha";
                case ModBuildChannel.Channels.Beta: return "公测版 beta";
                case ModBuildChannel.Channels.Rc: return "正式版 rc";
                case ModBuildChannel.Channels.Snapshot: return "快照版 sp";
                default: return C.ToString();
            }
        }

        #endregion

        #region 下载与自替换

        /// <summary>更新用的临时目录（PathTemp 下独立子目录，跨重启保留待装文件）。</summary>
        private static string UpdateDir => PathUtils.AddSlashSuffix(ModBase.PathTemp + "Update");

        /// <summary>
        /// 下载目标版本到临时目录。返回下载后的 exe 完整路径。
        /// </summary>
        public static string Download(ReleaseInfo Info)
        {
            DirectoryUtils.Create(UpdateDir);
            string Url = AssetFor(Info);
            if (string.IsNullOrEmpty(Url))
                throw new InvalidOperationException("该版本没有匹配当前架构的附件");
            string LocalPath = UpdateDir + $"pcl-next-{Info.VersionCode}.exe";
            Logger.Info($"下载更新：{Url} → {LocalPath}");
            ModNet.NetDownloadByClient(Url, LocalPath, SimulateBrowserHeaders: true);
            return LocalPath;
        }

        /// <summary>
        /// 生成自替换脚本并启动它，然后由调用方退出本进程。
        ///
        /// 单文件 exe 无法在运行中覆盖自己，标准做法是让一个独立脚本：
        /// 等待本进程退出 → 用新 exe 覆盖旧 exe → 重新启动。
        /// 用 tasklist 按 PID 轮询等待，比固定 sleep 更可靠。
        /// </summary>
        public static void ApplyAndRestart(string NewExePath)
        {
            string CurrentExe = Environment.ProcessPath;
            int Pid = Environment.ProcessId;
            string ScriptPath = UpdateDir + "apply.cmd";
            // cmd 里 % 需要转义为 %%；路径统一用短路径避免空格/中文导致的解析问题。
            string NewShort = PathUtils.ToShortPath(NewExePath);
            string CurShort = PathUtils.ToShortPath(CurrentExe);
            string Script = new StringBuilder()
                .AppendLine("@echo off")
                .AppendLine("title PCL Next Updater")
                .AppendLine($":wait")
                .AppendLine($"tasklist /FI \"PID eq {Pid}\" 2>NUL | find /I \"{Pid}\" >NUL")
                .AppendLine("if errorlevel 1 goto replace")
                .AppendLine("timeout /T 1 /NOBREAK >NUL")
                .AppendLine("goto wait")
                .AppendLine(":replace")
                .AppendLine("timeout /T 1 /NOBREAK >NUL")
                .AppendLine($"copy /Y \"{NewShort}\" \"{CurShort}\" >NUL")
                .AppendLine("if errorlevel 1 goto failed")
                .AppendLine($"start \"\" \"{CurShort}\"")
                .AppendLine($"del \"%~f0\"")
                .AppendLine("exit /B 0")
                .AppendLine(":failed")
                .AppendLine($"echo 更新失败：无法覆盖 {CurShort} >> \"{UpdateDir}update-error.log\"")
                .AppendLine("pause")
                .AppendLine("exit /B 1")
                .ToString();
            FileUtils.Write(ScriptPath, Script);
            Logger.Info($"启动更新脚本：{ScriptPath}（新版本 {NewExePath}）");
            ModBase.StartProcess(new System.Diagnostics.ProcessStartInfo(ScriptPath)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = UpdateDir
            });
        }

        /// <summary>
        /// 把本次晋级后的生效渠道写回设置，避免重启后重复提示同一晋级。
        /// 「陆续退出测试版」开关保持开启，让它持续把用户从 dev 逐级推到 RC；
        /// 只有到达 RC（渠道阶梯顶端）才关闭——此后没有更高渠道，留着也没意义。
        /// </summary>
        public static void MarkInstalled(ReleaseInfo Info)
        {
            Settings.Set("SystemUpdateChannel", (int)Info.Channel);
            Settings.Set("SystemUpdateLastTag", Info.Tag);
            if (Info.Channel == ModBuildChannel.Channels.Rc || Info.Channel == ModBuildChannel.Channels.Snapshot)
                Settings.Set("SystemUpdateGraduate", false);
        }

        #endregion

        #region 交互流程

        /// <summary>
        /// 完整的"检查 → 确认 → 下载 → 确认重启"流程，供启动后台与手动按钮共用。
        /// 整个过程在工作线程执行；所有弹窗经 RunInUiWait 切到 UI 线程，避免跨线程异常。
        /// </summary>
        /// <param name="Silent">静默模式（启动时用）：无更新、失败都不弹窗，只有发现更新才提示。</param>
        public static void RunInteractiveCheck(bool Silent)
        {
            ModBase.RunInNewThread(() =>
            {
                // 用户主动关闭了更新检查
                if (Settings.Get<int>("SystemSystemUpdate") == 3)
                    return;

                var Result = Check();
                Settings.Set("SystemUpdateLastCheck", DateTime.Now.Ticks);

                if (!Result.HasUpdate)
                {
                    if (!Silent)
                        ModBase.RunInUiWait(() => ModMain.MyMsgBox(Result.Message, "检查更新"));
                    return;
                }

                var Target = Result.Target;
                string Body = string.IsNullOrEmpty(Target.Body) ? "" : "\r\n\r\n" + Target.Body;
                string Tip = Result.Message + Body + "\r\n\r\n" + "是否现在下载并安装？";
                int Choose = ModBase.RunInUiWait(() => ModMain.MyMsgBox(
                    Tip,
                    "发现新版本",
                    "立即更新", "稍后", "打开 Release 页",
                    Button3Action: () => ModBase.OpenWebsite(ReleasesUrl)));
                if (Choose != 1)
                    return;

                // 下载（后台线程里阻塞执行，期间用 Hint 告知用户）
                string NewExe;
                try
                {
                    ModBase.RunInUiWait(() => ModMain.Hint("正在下载 " + Target.VersionName + "，请稍候……"));
                    NewExe = Download(Target);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "下载更新失败");
                    ModBase.RunInUiWait(() => ModMain.MyMsgBox("下载更新失败：" + ex.Message, "更新失败", IsWarn: true));
                    return;
                }

                // 下载完成，确认重启（单文件无法热替换自身，必须重启进程）
                int Restart = ModBase.RunInUiWait(() => ModMain.MyMsgBox(
                    Target.VersionName + " 已下载完成。\r\n\r\n"
                    + "安装需要重启 PCL Next，请确保当前没有正在进行的下载或游戏启动任务。\r\n"
                    + "点击【立即重启】后将自动完成安装。",
                    "安装更新", "立即重启", "稍后安装"));
                if (Restart != 1)
                    return;

                MarkInstalled(Target);
                ApplyAndRestart(NewExe);
                FormMain.EndProgramForce(ModBase.ProcessReturnValues.TaskDone);
            }, "AutoUpdate Check");
        }

        #endregion
    }
}
