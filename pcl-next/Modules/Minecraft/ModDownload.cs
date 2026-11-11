using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public static class ModDownload
    {

        #region DlClient* | Minecraft 客户端

        /// <summary>
    /// 返回某 Minecraft 版本对应的原版主 Jar 文件的下载信息，要求对应依赖版本已存在。
    /// 失败则抛出异常，不需要下载则返回 Nothing。
    /// </summary>
        public static ModNet.NetFile DlClientJarGet(ModMinecraft.McInstance Instance, bool ReturnNothingOnFileUseable)
        {
            // 获取底层继承版本
            try
            {
                while (!string.IsNullOrEmpty(Instance.InheritName))
                    Instance = new ModMinecraft.McInstance(Instance.InheritName);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "获取底层继承版本失败");
            }
            // 检查 Json 是否标准
            if (Instance.JsonObject["downloads"] is null || Instance.JsonObject["downloads"]["client"] is null || Instance.JsonObject["downloads"]["client"]["url"] is null)
            {
                throw new Exception("底层版本 " + Instance.Name + " 中无 jar 文件下载信息");
            }
            // 检查文件
            var Checker = new FileChecker() { MinSize = 1024L, ActualSize = (long)(Instance.JsonObject["downloads"]["client"]["size"] ?? -1), Hash = (string)Instance.JsonObject["downloads"]["client"]["sha1"] };
            if (ReturnNothingOnFileUseable && Checker.Check(Instance.PathVersion + Instance.Name + ".jar") is null)
                return null; // 通过校验
                             // 返回下载信息
            string JarUrl = (string)Instance.JsonObject["downloads"]["client"]["url"];
            return new ModNet.NetFile(DlSourceLauncherOrMetaGet(JarUrl), Instance.PathVersion + Instance.Name + ".jar", Checker);
        }

        /// <summary>
    /// 返回某 Minecraft 版本对应的原版主 AssetIndex 文件的下载信息，要求对应依赖版本已存在。
    /// 若未找到，则会返回 Legacy 资源文件或 Nothing。
    /// </summary>
        public static ModNet.NetFile DlClientAssetIndexGet(ModMinecraft.McInstance Instance)
        {
            // 获取底层继承版本
            while (!string.IsNullOrEmpty(Instance.InheritName))
                Instance = new ModMinecraft.McInstance(Instance.InheritName);
            // 获取信息
            var IndexInfo = ModMinecraft.McAssetsGetIndex(Instance, true, true);
            string IndexAddress = ModMinecraft.McFolderSelected + @"assets\indexes\" + IndexInfo["id"].ToString() + ".json";
            Logger.Info($"版本 {Instance.Name} 对应的资源文件索引为 {IndexInfo["id"]}");
            string IndexUrl = (string)(IndexInfo["url"] ?? "");
            if (string.IsNullOrEmpty(IndexUrl))
            {
                return null;
            }
            else
            {
                return new ModNet.NetFile(DlSourceLauncherOrMetaGet(IndexUrl), IndexAddress, new FileChecker() { IsJson = true });
            }
        }

        /// <summary>
    /// 构造补全某 Minecraft 版本的所有文件的加载器列表。失败会抛出异常。
    /// </summary>
        public static List<ModLoader.LoaderBase> DlClientFix(ModMinecraft.McInstance Instance, bool CheckAssetsHash, bool DownloadAssetIndexInBackground)
        {
            var Loaders = new List<ModLoader.LoaderBase>();

            #region 下载支持库文件
            var LoadersLib = new List<ModLoader.LoaderBase>() { new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("分析缺失支持库文件", (Task) => Task.Output = ModMinecraft.McLibNetFilesFromInstance(Instance)) { ProgressWeight = 1d }, new ModNet.LoaderDownload("下载支持库文件", new List<ModNet.NetFile>()) { ProgressWeight = 15d } };
            // 构造加载器
            Loaders.Add(new ModLoader.LoaderCombo<string>("下载支持库文件（主加载器）", LoadersLib) { Block = false, Show = false, ProgressWeight = 16d });
            #endregion

            #region 下载资源文件
            if (Conversions.ToBoolean(ModMinecraft.ShouldIgnoreFileCheck(Instance)))
            {
                Logger.Warn("设置要求跳过所有 Assets 检查，文件可能不是最新的", LogBehavior.None);
            }
            else
            {
                var LoadersAssets = new List<ModLoader.LoaderBase>();
                // 获取资源文件索引地址
                string TempAddressMain = null;
                string RealAddressMain = null;
                // 允许后台更新索引
                // 强制要求重新下载
                LoadersAssets.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("分析资源文件索引地址", (Task) => { try { var IndexFile = DlClientAssetIndexGet(Instance); if (IndexFile is null) { Task.Output = new List<ModNet.NetFile>(); Logger.Warn($"未找到版本 {Instance.Name} 的合适的资源索引下载地址，游戏 assets 可能缺失"); return; } string IndexBackgroundFile = ModBase.PathTemp + @"Cache\" + IndexFile.LocalName + "_background"; if (FileUtils.Exists(IndexBackgroundFile) && FileUtils.GetInfo(IndexBackgroundFile).LastWriteTime.Date > FileUtils.GetInfo(IndexFile.LocalPath).LastWriteTime.Date) { Logger.Info($"发现后台更新的资源索引缓存文件：{IndexBackgroundFile}"); FileUtils.Move(IndexBackgroundFile, IndexFile.LocalPath); } if (DownloadAssetIndexInBackground) { if (IndexFile.Check.Check(IndexFile.LocalPath) is null) { Task.Output = new List<ModNet.NetFile>(); } else { Task.Output = new List<ModNet.NetFile>() { IndexFile }; } } else { RealAddressMain = IndexFile.LocalPath; TempAddressMain = ModBase.PathTemp + @"Cache\" + IndexFile.LocalName; IndexFile.LocalPath = TempAddressMain; Task.Output = new List<ModNet.NetFile>() { IndexFile }; } } catch (Exception ex) { throw new Exception("分析资源文件索引地址失败", ex); } }) { ProgressWeight = 0.5d, Show = false });
                // 下载资源文件索引
                LoadersAssets.Add(new ModNet.LoaderDownload("下载资源文件索引", new List<ModNet.NetFile>()) { ProgressWeight = 2d });
                // 获取资源文件地址
                LoadersAssets.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("获取资源文件列表", Task =>
        {
            if (TempAddressMain is not null)
                FileUtils.Move(TempAddressMain, RealAddressMain);
            ModLoader.LoaderBase argProgressFeed = Task;
            Task.Output = ModMinecraft.McAssetsFixList(Instance, CheckAssetsHash, ref argProgressFeed);
            Task = (ModLoader.LoaderTask<string, List<ModNet.NetFile>>)argProgressFeed;
        })
                {
                    ProgressWeight = 0.01d
                });
                // 下载资源文件
                LoadersAssets.Add(new ModNet.LoaderDownload("下载资源文件", new List<ModNet.NetFile>()) { ProgressWeight = 25d });
                // 构造加载器
                Loaders.Add(new ModLoader.LoaderCombo<string>("下载资源文件（主加载器）", LoadersAssets) { Block = false, Show = false, ProgressWeight = 27d });

                // 后台独立更新索引
                if (DownloadAssetIndexInBackground)
                {
                    string RealAddressBg = null;
                    string TempAddressBg = null;
                    var LoadersAssetsUpdate = new List<ModLoader.LoaderBase>();
                    LoadersAssetsUpdate.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("后台分析资源文件索引地址", (Task) =>
        {
            var IndexFile = DlClientAssetIndexGet(Instance);
            if (IndexFile is null)
            {
                Logger.Warn($"未找到版本 {Instance.Name} 的合适的资源索引下载地址，游戏 assets 可能缺失");
                Task.Cancel();
                throw new OperationCanceledException();
            }
            RealAddressBg = IndexFile.LocalPath;
            TempAddressBg = ModBase.PathTemp + @"Cache\" + IndexFile.LocalName + "_background";
            IndexFile.LocalPath = TempAddressBg;
            Task.Output = new List<ModNet.NetFile>() { IndexFile };
            // 检查是否需要更新：每天只更新一次
            var LastUpdate = FileUtils.GetInfo(RealAddressBg).LastWriteTime.Date;
            if (FileUtils.Exists(RealAddressBg) && Math.Abs((LastUpdate - DateTime.Now.Date).TotalDays) < 1d)
            {
                Logger.Info($"无需更新资源文件索引，上次更新于 {LastUpdate}");
                Task.Cancel();
            }
        }));
                    LoadersAssetsUpdate.Add(new ModNet.LoaderDownload("后台下载资源文件索引", new List<ModNet.NetFile>()));
                    var Updater = new ModLoader.LoaderCombo<string>("后台更新资源文件索引", LoadersAssetsUpdate);
                    Logger.Info("开始后台检查资源文件索引");
                    Updater.Start();
                }
            }
            #endregion

            return Loaders;
        }

        #endregion

        #region DlClientList | Minecraft 客户端 版本列表

        /// <summary>
    /// 所有正式版的 Minecraft Drop 序数。
    /// 若从未完成过获取，返回 Nothing；否则必定存在元素，且从高到低排列。
    /// </summary>
        public static List<int> AllDrops
        {
            get
            {
                if (_AllDrops is null)
                {
                    _AllDrops = Settings.Get<string>("CacheDrops").ToString().Split(",", true).Select(d => (int)Math.Round(ModBase.Val(d))).ToList();
                }
                return _AllDrops.Any() ? _AllDrops : null; // 不要将 _AllDrops 再设为 Nothing，以防止反复获取设置尝试初始化
            }
            set
            {
                _AllDrops = value;
                Settings.Set("CacheDrops", value.Join(','));
            }
        }
        private static List<int> _AllDrops = null;

        // 主加载器
        public struct DlClientListResult
        {
            /// <summary>
        /// 数据来源名称，如“Mojang”，“BMCLAPI”。
        /// </summary>
            public string SourceName;
            /// <summary>
        /// 是否为官方的实时数据。
        /// </summary>
            public bool IsOfficial;
            /// <summary>
        /// 获取到的 Json 数据。
        /// </summary>
            public JObject Value;
        }
        /// <summary>
    /// Minecraft 客户端 版本列表，主加载器。
    /// 若要求镜像源必须包含某个版本，则将该版本 ID 作为输入（#5195）。
    /// </summary>
        public static ModLoader.LoaderTask<string, DlClientListResult> DlClientListLoader = new ModLoader.LoaderTask<string, DlClientListResult>("DlClientList Main", DlClientListMain);
        private static void DlClientListMain(ModLoader.LoaderTask<string, DlClientListResult> Loader)
        {
            // Phase 4：版本清单的获取、解析、源收敛与 Drop 提取已迁至 C#（PCL.Download.McVersionManifestClient）。
            // 本方法只把结果套回 VB 的 LoaderTask 契约，UI 侧读取方式保持不变。
            Download.McVersionSourceMode Mode = (Download.McVersionSourceMode)Settings.Get<int>("ToolDownloadVersion");
            var Manifest = new Download.McVersionManifestClient().Fetch(Mode, Loader.Input, ExtraVersionsPath);
            ApplyClientList(Loader, Manifest);
        }

        /// <summary>
    /// PCL 特供版本缓存文件（存在时由 C# 侧合并进清单）；不存在时返回 Nothing。
    /// </summary>
        private static string ExtraVersionsPath
        {
            get
            {
                string FilePath = ModBase.PathTemp + @"Cache\download.json";
                return FileUtils.Exists(FilePath) ? FilePath : null;
            }
        }

        /// <summary>
    /// 把 C# 侧的版本清单结果写回 VB 的加载器契约，并处理官方源耗时探测与 MC 更新提示。
    /// </summary>
        private static void ApplyClientList(ModLoader.LoaderTask<string, DlClientListResult> Loader, Download.McVersionManifest Manifest)
        {
            Loader.Output = new DlClientListResult()
            {
                IsOfficial = Manifest.IsOfficial,
                SourceName = Manifest.SourceName,
                Value = JObject.Parse(Manifest.RawJson)
            };
            AllDrops = Manifest.Drops.ToList();
            if (!Manifest.IsOfficial)
                return;
            // 确定官方源是否可用：只在尚未判定时探测一次（与 VB 原行为一致）
            if (!DlPreferMojang)
            {
                DlPreferMojang = Manifest.ElapsedMilliseconds < 4000L;
                Logger.Info($"Mojang 官方源加载耗时：{Manifest.ElapsedMilliseconds}ms，{(DlPreferMojang ? "可优先使用官方源" : "不优先使用官方源")}");
            }
            CheckClientUpdateHint(Manifest.LatestSnapshot, Manifest.LatestRelease, Loader.Output.Value);
        }

        /// <summary>是否已经弹出过 MC 更新提示（对应原实现里的 Static IsHinted）。</summary>
        private static bool IsClientUpdateHinted = false;

        /// <summary>
    /// MC 更新提示：快照版与正式版各自比对上次记录的版本，只在首次命中时提示一次。
    /// </summary>
        private static void CheckClientUpdateHint(string SnapshotVersion, string ReleaseVersion, JObject Json)
        {
            if (Settings.Get<bool>("ToolUpdateSnapshot") && !string.IsNullOrEmpty(Settings.Get<string>("ToolUpdateSnapshotLast")) && (Settings.Get<string>("ToolUpdateSnapshotLast") ?? "") != (SnapshotVersion ?? "") && !IsClientUpdateHinted)
            {
                IsClientUpdateHinted = true;
                ModMinecraft.McDownloadClientUpdateHint(SnapshotVersion, Json);
            }
            Settings.Set("ToolUpdateSnapshotLast", SnapshotVersion ?? "Nothing");
            if (Settings.Get<bool>("ToolUpdateRelease") && !string.IsNullOrEmpty(Settings.Get<string>("ToolUpdateReleaseLast")) && (Settings.Get<string>("ToolUpdateReleaseLast") ?? "") != (ReleaseVersion ?? "") && !IsClientUpdateHinted)
            {
                IsClientUpdateHinted = true;
                ModMinecraft.McDownloadClientUpdateHint(ReleaseVersion, Json);
            }
            Settings.Set("ToolUpdateReleaseLast", ReleaseVersion ?? "Nothing");
        }

        // 各个下载源的分加载器
        /// <summary>
    /// Minecraft 客户端 版本列表，Mojang 官方源加载器。
    /// </summary>
        public static ModLoader.LoaderTask<string, DlClientListResult> DlClientListMojangLoader = new ModLoader.LoaderTask<string, DlClientListResult>("DlClientList Mojang", DlClientListMojangMain);
        private static void DlClientListMojangMain(ModLoader.LoaderTask<string, DlClientListResult> Loader)
        {
            // 只取官方源；不校验输入版本（与 VB 原行为一致：官方源不做 #5195 的镜像缺失检查）
            var Manifest = new Download.McVersionManifestClient().Fetch(new Download.McVersionSource[] { Download.McVersionSources.Mojang() });
            ApplyClientList(Loader, Manifest);
        }
        /// <summary>
    /// Minecraft 客户端 版本列表，BMCLAPI 源加载器。
    /// </summary>
        public static ModLoader.LoaderTask<string, DlClientListResult> DlClientListBmclapiLoader = new ModLoader.LoaderTask<string, DlClientListResult>("DlClientList Bmclapi", DlClientListBmclapiMain);
        private static void DlClientListBmclapiMain(ModLoader.LoaderTask<string, DlClientListResult> Loader)
        {
            // 镜像源额外合并 PCL 特供版本；目标版本校验（#5195）由 C# 侧对本源执行
            var Manifest = new Download.McVersionManifestClient().Fetch(new Download.McVersionSource[] { Download.McVersionSources.Bmclapi() }, Loader.Input, ExtraVersionsPath);
            ApplyClientList(Loader, Manifest);
        }

        /// <summary>
    /// 获取某个版本的 Json 下载地址，若失败则返回 Nothing。必须在工作线程执行。
    /// </summary>
        public static object DlClientListGet(string Id)
        {
            try
            {
                // 确认版本格式标准
                Id = Id.Replace("_", "-"); // 1.7.10_pre4 在版本列表中显示为 1.7.10-pre4
                if (Id != "1.0" && Id.EndsWithF(".0"))
                    Id = Strings.Left(Id, Id.Length - 2); // OptiFine 1.8 的下载会触发此问题，显示版本为 1.8.0
                                                          // 获取 Minecraft 版本列表
                switch (DlClientListLoader.State)
                {
                    case ModBase.LoadState.Finished:
                        {
                            // 从当前的结果获取目标版本…
                            foreach (JObject Version in DlClientListLoader.Output.Value["versions"])
                            {
                                if ((string)Version["id"] == Id)
                                    return Version["url"].ToString();
                            }
                            // …如果没有，则重新尝试获取（在版本刚更新时可能出现这种情况，#5195）
                            DlClientListLoader.WaitForExit(Id, IsForceRestart: true);
                            break;
                        }
                    case ModBase.LoadState.Loading:
                        {
                            DlClientListLoader.WaitForExit(Id);
                            break;
                        }
                    case ModBase.LoadState.Failed:
                    case ModBase.LoadState.Canceled:
                    case ModBase.LoadState.Waiting:
                        {
                            DlClientListLoader.WaitForExit(Id, IsForceRestart: true);
                            break;
                        }
                }
                // 重新查找版本
                foreach (JObject Version in DlClientListLoader.Output.Value["versions"])
                {
                    if ((string)Version["id"] == Id)
                        return Version["url"].ToString();
                }
                Logger.Warn($"未发现版本 {Id} 的 json 下载地址，版本列表返回为：{Constants.vbCrLf}{DlClientListLoader.Output.Value}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"获取版本 {Id} 的 json 下载地址失败");
                return null;
            }
        }

        #endregion

        #region 加载器列表 | 取数与解析已迁至 C#

        /// <summary>
    /// 加载器列表获取器。取数、候选源排期与解析都在 PCL.Download，本模块只做投影与状态管理。
    /// </summary>
    /// 用模块级单例，是因为 C# 侧的实例内缓存对应原来各子加载器的「已加载结果复用」。
        private readonly static Download.LoaderListClient DlLoaderListClient = new Download.LoaderListClient();

        /// <summary>
    /// 当前设置的版本列表源偏好（对应 VB 设置项 ToolDownloadVersion）。
    /// </summary>
        private static Download.McVersionSourceMode DlLoaderSourceMode()
        {
            return (Download.McVersionSourceMode)Settings.Get<int>("ToolDownloadVersion");
        }

        #endregion

        #region DlOptiFineList | OptiFine 版本列表

        public struct DlOptiFineListResult
        {
            /// <summary>
        /// 数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
            public string SourceName;
            /// <summary>
        /// 是否为官方的实时数据。
        /// </summary>
            public bool IsOfficial;
            /// <summary>
        /// 获取到的数据。
        /// </summary>
            public List<DlOptiFineListEntry> Value;
        }

        public class DlOptiFineListEntry
        {
            /// <summary>
        /// 显示名称，已去除 HD_U 字样，如“1.12.2 C8”。
        /// </summary>
            public string DisplayName;
            /// <summary>
        /// 原始文件名称，如“preview_OptiFine_1.11_HD_U_E1_pre.jar”。
        /// </summary>
            public string FileName;
            /// <summary>
        /// 对应的版本名称，如“1.13.2-OptiFine_HD_U_E6”。
        /// </summary>
            public string InstanceName;
            /// <summary>
        /// 是否为测试版。
        /// </summary>
            public bool IsPreview;
            /// <summary>
        /// 对应的 Minecraft 版本，如“1.12.2”。
        /// </summary>
            public string Inherit
            {
                get
                {
                    return _inherit;
                }
                set
                {
                    if (value.EndsWithF(".0"))
                        value = Strings.Left(value, value.Length - 2);
                    _inherit = value;
                }
            }
            private string _inherit;
            /// <summary>
        /// 发布时间，格式为“yyyy/mm/dd”。OptiFine 源无此数据。
        /// </summary>
            public string ReleaseTime;
            /// <summary>
        /// 需要的最低 Forge 版本。空字符串为无限制，Nothing 为不兼容，“28.1.56” 表示版本号，“1161” 表示版本号的最后一位。
        /// </summary>
            public string RequiredForgeVersion;
        }

        /// <summary>
    /// OptiFine 版本列表，主加载器。
    /// </summary>
        public static ModLoader.LoaderTask<int, DlOptiFineListResult> DlOptiFineListLoader = new ModLoader.LoaderTask<int, DlOptiFineListResult>("DlOptiFineList Main", DlOptiFineListMain);
        private static void DlOptiFineListMain(ModLoader.LoaderTask<int, DlOptiFineListResult> Loader)
        {
            var Result = DlLoaderListClient.FetchOptiFine(DlLoaderSourceMode(), Loader.IsForceRestarting, () => Loader.IsCanceled);
            if (Result is null)
                return; // 已取消
            var Versions = new List<DlOptiFineListEntry>(Result.Items.Count);
            foreach (var Item in Result.Items)
                Versions.Add(new DlOptiFineListEntry()
                {
                    DisplayName = Item.DisplayName,
                    FileName = Item.FileName,
                    InstanceName = Item.InstanceName,
                    IsPreview = Item.IsPreview,
                    Inherit = Item.Inherit,
                    ReleaseTime = Item.ReleaseTime,
                    RequiredForgeVersion = Item.RequiredForgeVersion
                });
            Loader.Output = new DlOptiFineListResult() { IsOfficial = Result.IsOfficial, SourceName = Result.SourceName, Value = Versions };
        }

        /// <summary>
    /// OptiFine 版本列表，BMCLAPI 单源加载器。
    /// </summary>
    /// <remarks>
    /// 官方页会删掉旧预览版，实例重构与整合包安装需要一份「全量且稳定」的清单，故固定用镜像源，
    /// 不受版本列表源设置影响。
    /// </remarks>
        public static ModLoader.LoaderTask<int, DlOptiFineListResult> DlOptiFineListBmclapiLoader = new ModLoader.LoaderTask<int, DlOptiFineListResult>("DlOptiFineList Bmclapi", DlOptiFineListBmclapiMain);
        private static void DlOptiFineListBmclapiMain(ModLoader.LoaderTask<int, DlOptiFineListResult> Loader)
        {
            var Result = DlLoaderListClient.FetchOptiFineMirror(Loader.IsForceRestarting, () => Loader.IsCanceled);
            if (Result is null)
                return; // 已取消
            var Versions = new List<DlOptiFineListEntry>(Result.Items.Count);
            foreach (var Item in Result.Items)
                Versions.Add(new DlOptiFineListEntry()
                {
                    DisplayName = Item.DisplayName,
                    FileName = Item.FileName,
                    InstanceName = Item.InstanceName,
                    IsPreview = Item.IsPreview,
                    Inherit = Item.Inherit,
                    ReleaseTime = Item.ReleaseTime,
                    RequiredForgeVersion = Item.RequiredForgeVersion
                });
            Loader.Output = new DlOptiFineListResult() { IsOfficial = Result.IsOfficial, SourceName = Result.SourceName, Value = Versions };
        }
        #endregion

        #region DlForgeList | Forge Minecraft 版本列表

        public struct DlForgeListResult
        {
            /// <summary>
        /// 数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
            public string SourceName;
            /// <summary>
        /// 是否为官方的实时数据。
        /// </summary>
            public bool IsOfficial;
            /// <summary>
        /// 获取到的数据。
        /// </summary>
            public List<string> Value;
        }

        /// <summary>
    /// Forge 版本列表，主加载器。
    /// </summary>
        public static ModLoader.LoaderTask<int, DlForgeListResult> DlForgeListLoader = new ModLoader.LoaderTask<int, DlForgeListResult>("DlForgeList Main", DlForgeListMain);
        private static void DlForgeListMain(ModLoader.LoaderTask<int, DlForgeListResult> Loader)
        {
            var Result = DlLoaderListClient.FetchForgeGameVersions(DlLoaderSourceMode(), Loader.IsForceRestarting, () => Loader.IsCanceled);
            if (Result is null)
                return; // 已取消
            Loader.Output = new DlForgeListResult() { IsOfficial = Result.IsOfficial, SourceName = Result.SourceName, Value = Result.Items.ToList() };
        }
        #endregion

        #region DlForgeVersion | Forge 版本列表

        public abstract class DlForgelikeEntry : IComparable<DlForgelikeEntry>
        {

            public bool IsNeoForge;
            /// <summary>
        /// 加载器名称。Forge 或 NeoForge。
        /// </summary>
            public abstract string LoaderName { get; }
            /// <summary>
        /// 文件扩展名。不以小数点开头。
        /// </summary>
            public abstract string FileExtension { get; }
            /// <summary>
        /// 标准化后的版本号，仅可用于比较与排序，且 NeoForge 项不包含 alpha/beta 编号！
        /// 格式：Major.Minor.Build.Revision
        /// Forge：如 “50.1.9.0”（最后一位固定为 0）、“14.22.1.2478”（Legacy）。
        /// NeoForge：如 “20.4.30.0”（最后一位固定为 0）、“19.47.1.99”（Legacy：第一位固定为 19）。
        /// </summary>
            public Version Version;
            /// <summary>
        /// 可读的非格式化版本名。
        /// Forge：如 “50.1.9”、“14.22.1.2478”（Legacy）。
        /// NeoForge：如 “20.4.30-beta”、“47.1.99”（Legacy）。
        /// </summary>
            public string VersionName;
            /// <summary>
        /// 对应的 Minecraft 版本，如 1.12.2、26.1-snapshot-1。
        /// </summary>
            public string Inherit;

            public int CompareTo(DlForgelikeEntry other)
            {
                if (Version != other.Version)
                {
                    return Version.CompareTo(other.Version);
                }
                else
                {
                    return McUtils.CompareVersion(VersionName, other.VersionName);
                }
            }
        }

        public class DlForgeVersionEntry : DlForgelikeEntry
        {

            /// <summary>
        /// 发布时间，格式为“yyyy/MM/dd HH:mm”。
        /// </summary>
            public string ReleaseTime;
            /// <summary>
        /// 文件的 MD5 或 SHA1（BMCLAPI 的老版本是 MD5，新版本是 SHA1；官方源总是 MD5）。
        /// </summary>
            public string Hash = null;
            /// <summary>
        /// 是否为推荐版本。
        /// </summary>
            public bool IsRecommended;
            /// <summary>
        /// 安装类型。有 installer、client、universal 三种。
        /// </summary>
            public string Category;
            /// <summary>
        /// 用于下载的文件版本名。可能在 Version 的基础上添加了分支。
        /// </summary>
            public string FileVersion;

            /// <summary>
        /// 加载器名称。Forge 或 NeoForge。
        /// </summary>
            public override string LoaderName
            {
                get
                {
                    return "Forge";
                }
            }
            /// <summary>
        /// 文件扩展名。不以小数点开头。
        /// </summary>
            public override string FileExtension
            {
                get
                {
                    return Category == "installer" ? "jar" : "zip";
                }
            }

            public DlForgeVersionEntry(string Version, string Branch, string Inherit)
            {
                // 司马版本的特殊处理
                if (Version == "11.15.1.2318" || Version == "11.15.1.1902" || Version == "11.15.1.1890")
                    Branch = "1.8.9";
                if (Branch is null && Inherit == "1.7.10" && Conversions.ToDouble(Version.Split(".")[3]) >= 1300d)
                    Branch = "1.7.10";
                // 为 DlForgelikeEntry 提供所有信息
                IsNeoForge = false;
                VersionName = Version;
                this.Version = new Version(Version);
                this.Inherit = Inherit;
                FileVersion = Version + (Branch is null ? "" : "-" + Branch);
            }

        }

        /// <summary>
    /// Forge 版本列表，主加载器。
    /// </summary>
        public static void DlForgeVersionMain(ModLoader.LoaderTask<string, List<DlForgeVersionEntry>> Loader)
        {
            string RecommendedPath = ModBase.PathTemp + @"Cache\ForgeRecommendedList.json";
            var Result = DlLoaderListClient.FetchForgeVersions(Loader.Input, RecommendedPath, DlLoaderSourceMode(), Loader.IsForceRestarting, () => Loader.IsCanceled);
            if (Result is null)
                return; // 已取消
            var Versions = new List<DlForgeVersionEntry>(Result.Items.Count);
            foreach (var Item in Result.Items)
                // 分支与「司马版本」等特例已由 C# 侧施加，这里复用 VB 构造函数保证字段与旧实现一致
                Versions.Add(new DlForgeVersionEntry(Item.VersionName, Item.Branch, Item.Inherit)
                {
                    Category = Item.Category,
                    IsRecommended = Item.IsRecommended,
                    Hash = Item.Hash,
                    ReleaseTime = Item.ReleaseTime
                });
            Loader.Output = Versions;
        }
        #endregion

        #region DlNeoForgeList | NeoForge 版本列表

        public struct DlNeoForgeListResult
        {
            /// <summary>
        /// 数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
            public string SourceName;
            /// <summary>
        /// 是否为官方的实时数据。
        /// </summary>
            public bool IsOfficial;
            /// <summary>
        /// 所有版本的列表。已经按从新到老排序。
        /// </summary>
            public List<DlNeoForgeListEntry> Value;
        }

        public class DlNeoForgeListEntry : DlForgelikeEntry
        {

            /// <summary>
        /// 是否是 Beta 版。
        /// </summary>
            public bool IsBeta;
            /// <summary>
        /// API 使用的原始版本字符串，如 “20.4.30-beta”、“1.20.1-47.1.99”（Legacy）。
        /// </summary>
            public string ApiName;
            /// <summary>
        /// 文件在官网的基础地址，不包含后缀。
        /// </summary>
            public string UrlBase
            {
                get
                {
                    string PackageName = Inherit == "1.20.1" ? "forge" : "neoforge";
                    return $"https://maven.neoforged.net/releases/net/neoforged/{PackageName}/{ApiName}/{PackageName}-{ApiName}";
                }
            }

            /// <summary>
        /// 加载器名称。Forge 或 NeoForge。
        /// </summary>
            public override string LoaderName
            {
                get
                {
                    return "NeoForge";
                }
            }
            /// <summary>
        /// 文件扩展名。不以小数点开头。
        /// </summary>
            public override string FileExtension
            {
                get
                {
                    return "jar";
                }
            }

            public DlNeoForgeListEntry(string ApiName)
            {
                IsNeoForge = true;
                this.ApiName = ApiName;
                IsBeta = ApiName.ContainsIgnoreCase("beta") || ApiName.ContainsIgnoreCase("alpha");
                if (ApiName.Contains("1.20.1")) // 1.20.1-47.1.99
                {
                    VersionName = ApiName.Replace("1.20.1-", "");
                    Version = new Version("19." + VersionName);
                    Inherit = "1.20.1";
                }
                else if (ApiName.StartsWithF("0.")) // 0.25w14craftmine.3-beta
                {
                    VersionName = ApiName;
                    string[] Segments = ApiName.BeforeFirst("-").Split('.');
                    Version = new Version(0, 0, Conversions.ToInteger(Segments.Last()));
                    Inherit = Segments[1];
                }
                else // 20.4.30-beta；26.1.0.0-alpha.1+snapshot-1
                {
                    VersionName = ApiName;
                    Version = new Version(ApiName.BeforeFirst("-"));
                    if (Version.Major >= 24)
                    {
                        Inherit = $"{Version.Major}.{Version.Minor}.{Version.Build}";
                    }
                    else
                    {
                        Inherit = $"1.{Version.Major}.{Version.Minor}";
                    }
                    if (Inherit.EndsWithF(".0"))
                        Inherit = Strings.Left(Inherit, Inherit.Length - 2);
                    if (VersionName.Contains("+"))
                        Inherit += "-" + VersionName.AfterFirst("+");
                }
            }
        }

        /// <summary>
    /// NeoForge 版本列表，主加载器。
    /// </summary>
        public static ModLoader.LoaderTask<int, DlNeoForgeListResult> DlNeoForgeListLoader = new ModLoader.LoaderTask<int, DlNeoForgeListResult>("DlNeoForgeList Main", DlNeoForgeListMain);
        private static void DlNeoForgeListMain(ModLoader.LoaderTask<int, DlNeoForgeListResult> Loader)
        {
            var Result = DlLoaderListClient.FetchNeoForge(DlLoaderSourceMode(), Loader.IsForceRestarting, () => Loader.IsCanceled);
            if (Result is null)
                return; // 已取消
            var Versions = new List<DlNeoForgeListEntry>(Result.Items.Count);
            foreach (var Item in Result.Items)
                // 版本号归一化由 VB 构造函数完成（规则与 C# 侧一致），这里只传原始 API 版本名
                Versions.Add(new DlNeoForgeListEntry(Item.ApiName));
            Loader.Output = new DlNeoForgeListResult() { IsOfficial = Result.IsOfficial, SourceName = Result.SourceName, Value = Versions };
        }
        #endregion

        #region DlLiteLoaderList | LiteLoader 版本列表

        public struct DlLiteLoaderListResult
        {
            /// <summary>
        /// 数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
            public string SourceName;
            /// <summary>
        /// 是否为官方的实时数据。
        /// </summary>
            public bool IsOfficial;
            /// <summary>
        /// 获取到的数据。
        /// </summary>
            public List<DlLiteLoaderListEntry> Value;
            /// <summary>
        /// 官方源的失败原因。若没有则为 Nothing。
        /// </summary>
            public Exception OfficialError;
        }

        public class DlLiteLoaderListEntry
        {
            /// <summary>
        /// 实际的文件名，如“liteloader-installer-1.12-00-SNAPSHOT.jar”。
        /// </summary>
            public string FileName;
            /// <summary>
        /// 是否为测试版。
        /// </summary>
            public bool IsPreview;
            /// <summary>
        /// 对应的 Minecraft 版本，如“1.12.2”。
        /// </summary>
            public string Inherit;
            /// <summary>
        /// 是否为 1.7 及更早的远古版。
        /// </summary>
            public bool IsLegacy;
            /// <summary>
        /// 发布时间，格式为“yyyy/mm/dd HH:mm”。
        /// </summary>
            public string ReleaseTime;
            /// <summary>
        /// 文件的 MD5。
        /// </summary>
            public string MD5;
            /// <summary>
        /// 对应的 Json 项。
        /// </summary>
            public JToken JsonToken;
        }

        /// <summary>
    /// LiteLoader 版本列表，主加载器。
    /// </summary>
        public static ModLoader.LoaderTask<int, DlLiteLoaderListResult> DlLiteLoaderListLoader = new ModLoader.LoaderTask<int, DlLiteLoaderListResult>("DlLiteLoaderList Main", DlLiteLoaderListMain);
        private static void DlLiteLoaderListMain(ModLoader.LoaderTask<int, DlLiteLoaderListResult> Loader)
        {
            var Result = DlLoaderListClient.FetchLiteLoader(DlLoaderSourceMode(), Loader.IsForceRestarting, () => Loader.IsCanceled);
            if (Result is null)
                return; // 已取消
            var Versions = new List<DlLiteLoaderListEntry>(Result.Items.Count);
            foreach (var Item in Result.Items)
                Versions.Add(new DlLiteLoaderListEntry()
                {
                    FileName = Item.FileName,
                    IsPreview = Item.IsPreview,
                    Inherit = Item.Inherit,
                    IsLegacy = Item.IsLegacy,
                    ReleaseTime = Item.ReleaseTime,
                    MD5 = Item.Md5,
                    JsonToken = JToken.Parse(Item.RawJson) // 安装编排要读 tweakClass / libraries / version
                });
            Loader.Output = new DlLiteLoaderListResult() { IsOfficial = Result.IsOfficial, SourceName = Result.SourceName, Value = Versions };
        }
        #endregion

        #region DlFabricList | Fabric 列表

        public struct DlFabricListResult
        {
            /// <summary>
        /// 数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
            public string SourceName;
            /// <summary>
        /// 是否为官方的实时数据。
        /// </summary>
            public bool IsOfficial;
            /// <summary>
        /// 获取到的数据。
        /// </summary>
            public JObject Value;
        }

        /// <summary>
    /// Fabric 列表，主加载器。
    /// </summary>
        public static ModLoader.LoaderTask<int, DlFabricListResult> DlFabricListLoader = new ModLoader.LoaderTask<int, DlFabricListResult>("DlFabricList Main", DlFabricListMain);
        private static void DlFabricListMain(ModLoader.LoaderTask<int, DlFabricListResult> Loader)
        {
            var Result = DlLoaderListClient.FetchFabric(DlLoaderSourceMode(), Loader.IsForceRestarting, () => Loader.IsCanceled);
            if (Result is null)
                return; // 已取消
                        // Fabric 元数据按原样 JSON 透传：UI 直接读取 game / loader / installer 三个数组
            Loader.Output = new DlFabricListResult() { IsOfficial = Result.IsOfficial, SourceName = Result.SourceName, Value = (JObject)Result.RawJson.DeserializeJson() };
        }

        /// <summary>
    /// Fabric API 列表，官方源。
    /// </summary>
        public static ModLoader.LoaderTask<int, List<ResourceVersion>> DlFabricApiLoader = new ModLoader.LoaderTask<int, List<ResourceVersion>>("Fabric API List Loader", (Task) => Task.Output = ResourceVersion.FromProjectId("fabric-api", ResourcePlatforms.Modrinth, LoadDependencies: false));

        /// <summary>
    /// OptiFabric 列表，官方源。
    /// </summary>
        public static ModLoader.LoaderTask<int, List<ResourceVersion>> DlOptiFabricLoader = new ModLoader.LoaderTask<int, List<ResourceVersion>>("OptiFabric List Loader", (Task) =>
    {
        // OptiFabric 只存在于 CurseForge（Modrinth 上无此项目，实测 404）。
        // 未配置 PCL_CURSEFORGE_API_KEY 时直连官方源必然 403，按 ResourceSourceGate 的降级口径
        // 直接跳过请求、返回空列表，由 LoadOptiFabricGetError 给出明确提示，避免反复报错刷屏。
        if (!ResourceSourceGate.CurseForgeAvailable)
        {
            Task.Output = new List<ResourceVersion>();
            Logger.Info("未配置 CurseForge API Key，跳过 OptiFabric 列表加载（OptiFabric 仅存在于 CurseForge）");
            return;
        }
        Task.Output = ResourceVersion.FromProjectId("322385", ResourcePlatforms.CurseForge, LoadDependencies: false);
    });
        #endregion

        #region DlMod | Mod 镜像源请求

        /// <summary>
    /// 对可能涉及 Mod 镜像源的请求进行处理，返回 JToken。
    /// 调用 NetRequest，会进行重试。
    /// </summary>
        public static JToken DlModRequest(string Url, HttpMethod Method = null, string Content = null, string ContentType = null)
        {
            var Urls = new List<(string Url, int Timeout)>();
            string McimUrl = DlSourceModGet(Url);
            if ((McimUrl ?? "") != (Url ?? ""))
            {
                switch (Settings.Get<int>("ToolDownloadMod"))
                {
                    case 0:
                        {
                            Urls.Add((McimUrl, 10));
                            Urls.Add((McimUrl, 10));
                            Urls.Add((Url, 30));
                            break;
                        }
                    case 1:
                        {
                            Urls.Add((Url, Url.Contains("modrinth") ? 20 : 10)); // 至少 20s，要不然 Modrinth 返回不过来
                            Urls.Add((McimUrl, 10));
                            Urls.Add((Url, 30));
                            Urls.Add((McimUrl, 30));
                            break;
                        }

                    default:
                        {
                            Urls.Add((Url, Url.Contains("modrinth") ? 20 : 10));
                            Urls.Add((Url, 30));
                            Urls.Add((McimUrl, 30));
                            break;
                        }
                }
            }
            else
            {
                Urls.Add((Url, Url.Contains("modrinth") ? 20 : 10));
                Urls.Add((Url, 30));
            }
            var Errors = new List<Exception>();
            foreach (var Source in Urls)
            {
                try
                {
                    return ModNet.NetRequestByClient(Source.Url, Method, Content, ContentType, Timeout: Source.Timeout * 1000, Encoding: Encoding.UTF8, RequireJson: true).DeserializeJson();
                }
                catch (Exception ex)
                {
                    Errors.Add(ex);
                }
            }
            throw new AggregateException(Errors);
        }

        #endregion

        #region DlSource | 镜像下载源

        private static bool DlPreferMojang = false;
        /// <summary>
    /// 下载文件（而非获取版本列表）的时候，是否优先使用官方源。
    /// </summary>
        public static bool DlSourcePreferMojang
        {
            get
            {
                return Settings.Get<int>("ToolDownloadSource") == 2 || Settings.Get<int>("ToolDownloadSource") == 1 && DlPreferMojang;
            }
        }
        /// <summary>
    /// 下载文件（而非获取版本列表）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    /// </summary>
        public static IEnumerable<string> DlSourceOrder(IEnumerable<string> OfficialUrls, IEnumerable<string> MirrorUrls)
        {
            return DlSourcePreferMojang ? OfficialUrls.Union(MirrorUrls) : MirrorUrls.Union(OfficialUrls);
        }
        /// <summary>
    /// 获取版本列表（而非下载文件）的时候，是否优先使用官方源。
    /// </summary>
        public static bool DlVersionListPreferMojang
        {
            get
            {
                return Settings.Get<int>("ToolDownloadVersion") == 2 || Settings.Get<int>("ToolDownloadVersion") == 1 && DlPreferMojang;
            }
        }
        /// <summary>
    /// 获取版本列表（而非下载文件）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    /// </summary>
        public static IEnumerable<string> DlVersionListOrder(IEnumerable<string> OfficialUrls, IEnumerable<string> MirrorUrls)
        {
            return DlVersionListPreferMojang ? OfficialUrls.Union(MirrorUrls) : MirrorUrls.Union(OfficialUrls);
        }


        /// <summary>
    /// 下载 Assets 文件。
    /// </summary>
        public static IEnumerable<string> DlSourceAssetsGet(string Original)
        {
            Original = Original.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net");
            return DlSourceOrder(new[] { Original }, new[] { Original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/assets").Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/assets").Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets") });
        }
        /// <summary>
    /// 下载 Libraries 文件。
    /// </summary>
        public static IEnumerable<string> DlSourceLibraryGet(string Original)
        {
            if (IsThirdPartyMaven(Original))
            {
                // 第三方 maven（Forge / Fabric / NeoForge / Sponge）的官方域名在受限网络下基本不可达
                // （真机实测 maven.neoforged.net 21s 无响应，而 BMCLAPI 镜像 1.3s 即有响应），
                // 因此镜像优先、官方直链仅作兜底。
                return DlSourceOrder(new[] { Original }, new[] { MirrorMavenUrl(Original, "/maven"), MirrorMavenUrl(Original, "/libraries") });
            }
            else
            {
                return DlSourceOrder(new[] { Original }, new[] { Original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven").Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven").Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven"), Original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries").Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries").Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries") });
            }
        }

        /// <summary>
    /// 该地址是否来自第三方 maven 仓库根（Forge / Fabric / NeoForge / Sponge）。
    /// </summary>
        private static bool IsThirdPartyMaven(string Original)
        {
            return new[] { "https://maven.fabricmc.net", "https://maven.minecraftforge.net", "https://maven.neoforged.net", "https://repo.spongepowered.org/repository/maven-public" }.Any(k => Original.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
    /// 把第三方 maven 直链换算成 BMCLAPI 镜像地址。
    /// 仓库前缀段（releases/、snapshots/、repository/maven-public/）必须剥掉——
    /// 镜像的 /maven 根直接对应仓库内容，不剥会拼出 /maven/releases/net/neoforged/... 这类稳定 404 的地址。
    /// </summary>
        private static string MirrorMavenUrl(string Original, string MirrorPrefix)
        {
            string Rel = new Uri(Original).AbsolutePath.TrimStart('/');
            foreach (string Prefix in RepositoryPrefixes)
            {
                if (Rel.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    Rel = Rel.Substring(Prefix.Length);
                    break;
                }
            }
            return "https://bmclapi2.bangbang93.com" + MirrorPrefix + "/" + Rel;
        }

        /// <summary>maven 仓库里「仓库根之下、制品路径之上」的固定前缀段。</summary>
        private readonly static string[] RepositoryPrefixes = new[] { "releases/", "snapshots/", "repository/maven-public/" };
        /// <summary>
    /// 下载 Launcher 或 Meta 文件。
    /// 不应使用它来获取版本列表（因为它只使用文件下载源设置来决定源顺序）。
    /// </summary>
        public static IEnumerable<string> DlSourceLauncherOrMetaGet(string Original)
        {
            if (Original is null)
                throw new Exception("无对应的 json 下载地址");
            return DlSourceOrder(new[] { Original }, new[] { Original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com").Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com").Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com").Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com") });
        }

        // Mod 下载源
        public static string DlSourceModGet(string Original)
        {
            return Original.Replace("api.modrinth.com", "mod.mcimirror.top/modrinth").Replace("staging-api.modrinth.com", "mod.mcimirror.top/modrinth").Replace("cdn.modrinth.com", "mod.mcimirror.top").Replace("api.curseforge.com", "mod.mcimirror.top/curseforge").Replace("edge.forgecdn.net", "mod.mcimirror.top").Replace("mediafilez.forgecdn.net", "mod.mcimirror.top").Replace("media.forgecdn.net", "mod.mcimirror.top");
        }

        #endregion

    }
}