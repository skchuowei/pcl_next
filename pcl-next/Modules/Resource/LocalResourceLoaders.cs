using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{

    /// <summary>
/// 实例 Mod 列表的加载与联网反查。
/// <para/>
/// Phase 4：扫描、去重、排序与联网反查的**取数与判定**已全部迁至 C#（<c>PCL.Download.LocalResourceScanner</c> /
/// <c>LocalResourceOnlineLoader</c>），这里只保留 LoaderTask 外壳、等待更新、UI 文案、缓存落盘与 UI 刷新。
/// </para>
/// </summary>
    public static class LocalResourceLoaders
    {

        // 加载 Mod 列表
        public static ModLoader.LoaderTask<string, List<LocalResourceFile>> LocalResourceLoader = new ModLoader.LoaderTask<string, List<LocalResourceFile>>("LocalResourceLoader", LocalResourceFileLoad);
        private static void LocalResourceFileLoad(ModLoader.LoaderTask<string, List<LocalResourceFile>> Loader)
        {
            try
            {
                ModBase.RunInUiWait(() => { if (ModMain.FrmInstanceMod is not null) ModMain.FrmInstanceMod.Load.ShowProgress = false; });

                // 等待 Mod 更新完成
                if (PageInstanceMod.UpdatingInstanceModFolders.Contains(Loader.Input))
                {
                    Logger.Info($"等待 Mod 更新完成后才能继续加载 Mod 列表：{Loader.Input}");
                    try
                    {
                        ModBase.RunInUiWait(() => { if (ModMain.FrmInstanceMod is not null) ModMain.FrmInstanceMod.Load.Text = "正在更新 Mod"; });
                        while (PageInstanceMod.UpdatingInstanceModFolders.Contains(Loader.Input))
                        {
                            if (Loader.IsCanceled)
                                return;
                            Thread.Sleep(100);
                        }
                    }
                    finally
                    {
                        ModBase.RunInUiWait(() => { if (ModMain.FrmInstanceMod is not null) ModMain.FrmInstanceMod.Load.Text = "正在加载 Mod 列表"; });
                    }
                    ModMain.FrmInstanceMod.LoaderRun(ModLoader.LoaderFolderRunType.UpdateOnly);
                }

                // 扫描 Mod 文件夹：递归枚举、Forge 1.13- 的版本号子目录特例、同名启用/禁用去重、
                // 按文件名排序，全部在 C# 侧（LocalResourceScanner）
                var Instance = PageInstanceLeft.Instance;
                string LegacySubfolder = null;
                if (Instance is not null && Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) && Instance.Version.Vanilla.Major < 13)
                {
                    LegacySubfolder = $"1.{Instance.Version.Vanilla.Major}.{Instance.Version.Vanilla.Build}";
                }
                var Scanned = Download.LocalResourceScanner.Scan(Loader.Input, new Download.LocalResourceScanner.Options() { IsLegacyForgeSubfolder = new Func<string, bool>((Name) => (Name ?? "") == (LegacySubfolder ?? "")) }); // 为 Nothing 时恒为 False，等价于「子目录一律不收录」
                if (Loader.IsCanceled)
                    return;

                // 投影回 VB 侧的条目：文件信息、OnUpdate 事件与跨运行的指纹缓存（Cache\ModHash.ini）都还在这边
                var ModList = new List<LocalResourceFile>(Scanned.Count);
                foreach (var Entry in Scanned)
                    ModList.Add(new LocalResourceFile(Entry.File.FullName));

                // 获取本地文件缓存
                string CachePath = ModBase.PathTemp + @"Cache\LocalMod.json";
                var Cache = new JObject();
                try
                {
                    string CacheContent = FileUtils.TryReadAsString(CachePath);
                    if (!string.IsNullOrWhiteSpace(CacheContent))
                    {
                        Cache = (JObject)CacheContent.DeserializeJson();
                        if (!Cache.ContainsKey("version") || Cache["version"].ToObject<int>() != Versions.LocalModCacheVersion)
                        {
                            Logger.Warn($"本地 Mod 信息缓存版本已过期，将弃用这些缓存信息");
                            Cache = new JObject();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "读取本地 Mod 信息缓存失败，已重置");
                    Cache = new JObject();
                }
                Cache["version"] = Versions.LocalModCacheVersion;

                // 回设
                if (Loader.IsCanceled)
                    return;
                Loader.Output = ModList;

                // 开始联网加载
                // TODO: 添加信息获取中提示
                LocalResourceOnlineLoader.Start((ModList, Cache, PageInstanceLeft.Instance.Version), IsForceRestart: true);
            }

            catch (Exception ex)
            {
                Logger.Warn(ex, "Mod 列表加载失败");
                throw;
            }
        }

        // 联网加载 Mod 详情
        public static ModLoader.LoaderTask<(List<LocalResourceFile> ModList, JObject Cache, McVersion Target), int> LocalResourceOnlineLoader = new ModLoader.LoaderTask<(List<LocalResourceFile> ModList, JObject Cache, McVersion Target), int>("LocalResourceOnlineLoader", LocalResourceOnlineLoad);
        private static void LocalResourceOnlineLoad(ModLoader.LoaderTask<(List<LocalResourceFile> ModList, JObject Cache, McVersion Target), int> Loader)
        {
            var Target = Loader.Input.Target;
            var Context = new Download.OnlineLoadContext()
            {
                GameVersion = Target.VanillaName,
                Loaders = (Download.ModLoaders)(int)Target.ModLoaders
            };

            // 投影到 C# 侧，并把已经算好的指纹一并带过去：
            // VB 的指纹有跨运行缓存，不带的话内核每次启动都要把全部 Mod 重读一遍算哈希
            var CoreList = new List<Download.LocalResourceFile>(Loader.Input.ModList.Count);
            var CoreToLocal = new Dictionary<Download.LocalResourceFile, LocalResourceFile>();
            foreach (var Entry in Loader.Input.ModList)
            {
                if (Loader.IsCanceled)
                    return;
                var Core = new Download.LocalResourceFile(Entry.File.FullName);
                Core.PrimeHashes(Entry.ModrinthHash, Entry.CurseForgeHash);
                CoreList.Add(Core);
                CoreToLocal[Core] = Entry;
            }

            // 缓存 JSON 转一次格式：Newtonsoft ↔ System.Text.Json；落盘仍由这边负责
            JsonObject CoreCache = (JsonObject)JsonNode.Parse(Loader.Input.Cache.ToString());
            var Client = new Download.LocalResourceOnlineLoader();
            var Report = Client.Load(CoreList, CoreCache, Context, () => Loader.IsCanceled);
            if (Report is null)
                return; // 已取消

            // 把结果投影回 VB 条目：Project / UpdateFile 的 setter 会触发 OnUpdate，UI 才能跟着刷新
            foreach (var Core in CoreList)
            {
                var Entry = CoreToLocal[Core];
                if (Core.Project is not null)
                    Entry.Project = new ResourceProject(Core.Project);
                if (Core.ProjectVersion is not null)
                    Entry.ProjectVersion = new ResourceVersion(Core.ProjectVersion);
                if (Core.UpdateFile is not null)
                    Entry.UpdateFile = new ResourceVersion(Core.UpdateFile);
                Entry.ChangelogUrls = Core.ChangelogUrls;
                Entry.OnlineDataLoaded = Core.OnlineDataLoaded;
            }

            // 保存缓存（有失败时内核不给条目标记 OnlineDataLoaded，下次启动会重试，与旧实现一致）
            if (Loader.IsCanceled)
                return;
            if (Report.Resolved > 0)
            {
                var Format = ModBase.ModeDebug ? new JsonSerializerOptions() { WriteIndented = true } : null;
                FileUtils.Write(ModBase.PathTemp + @"Cache\LocalMod.json", CoreCache.ToJsonString(Format));
            }

            // 刷新边栏
            if (Loader.IsCanceled)
                return;
            if ((ModMain.FrmInstanceMod?.Filter) is { } arg1 && arg1 == PageInstanceMod.FilterType.CanUpdate)
            {
                ModBase.RunInUi(() => ModMain.FrmInstanceMod?.RefreshUI()); // 同步 “可更新” 列表 (#4677)
            }
            else
            {
                ModBase.RunInUi(() => ModMain.FrmInstanceMod?.RefreshBars());
            }
        }

    }
}