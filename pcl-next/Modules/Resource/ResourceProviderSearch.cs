using System;
using System.Linq;
using MeloongCore;
using MeloongCore.Wpf.Extensions;
using DlSearchRequest = PCL.Download.ResourceProviderSearchRequest;
// 资源搜索的 C# 数据源加载器（dev.4 §2.1 C：Phase 4 首步）。
// 与 ResourceSearcher.GetLoader 的接口完全一致，页面只需替换 Loader 的构造处，
// 内部把搜索交给 C# 侧 ProviderRegistry：源收敛（CF 无 Key 时降级）→ 融合搜索 → 结果回填。
using DlSearchResult = PCL.Download.ResourceSearcher.SearchResult;

namespace PCL
{

    public static class ResourceProviderSearch
    {

        /// <summary>每次至少向 C# 侧要一页，避免目标数量已满足时传 0 导致 Provider 内部按 1 处理。</summary>
        private const int DEFAULT_PAGE_SIZE = 40;

        /// <summary>
    /// 构造加载器。签名与 <see cref="ResourceSearcher.GetLoader"/> 一致，便于页面侧就地替换。
    /// </summary>
        public static ModLoader.LoaderTask<ResourceSearcher.SearchRequest, int> GetLoader(Func<ResourceSearcher.SearchRequest> InputDelegate)
        {
            return new ModLoader.LoaderTask<ResourceSearcher.SearchRequest, int>("社区资源获取", Run, InputDelegate) { ReloadTimeout = 60 * 1000 };
        }

        private static void Run(ModLoader.LoaderTask<ResourceSearcher.SearchRequest, int> Task)
        {
            var Request = Task.Input;
            var Storage = Request.Storage; // 避免其他线程对 Request.Storage 重新进行了赋值

            // ---------- 前置检查（与 VB 版一致） ----------
            if (Storage.Results.Count >= Request.TargetResultCount)
            {
                Logger.Info($"已有 {Storage.Results.Count} 个结果，多于所需的 {Request.TargetResultCount} 个结果，结束处理");
                return;
            }
            if (!Request.CanContinue)
            {
                if (!Storage.Results.Any())
                    throw new Exception("没有符合条件的结果");
                Logger.Info($"已有 {Storage.Results.Count} 个结果，少于所需的 {Request.TargetResultCount} 个结果，但无法继续获取，结束处理");
                return;
            }

            // ---------- 准备 C# 侧状态 ----------
            // C# 侧的 Storage 承载翻页偏移、总量与跨页去重；与 VB 的 Storage 同生共死
            var CSharpStorage = Storage.CSharpStorage;
            if (CSharpStorage is null)
            {
                CSharpStorage = new DlSearchResult();
                Storage.CSharpStorage = CSharpStorage;
            }
            // 本次还需要多少个结果；C# 侧不足时会自行递归翻页补齐
            int PageSize = Math.Max(DEFAULT_PAGE_SIZE, Request.TargetResultCount - CSharpStorage.Results.Count);

            var CsRequest = new DlSearchRequest()
            {
                Type = (Download.ResourceTypes)(int)Request.Type,
                SearchText = Request.SearchText,
                Tag = Request.Tag ?? "",
                ModLoaders = (Download.ModLoaders)(int)Request.ModLoaders,
                GameVersion = Request.GameVersion,
                PageSize = PageSize,
                Storage = CSharpStorage
            };

            // ---------- 搜索 ----------
            string Notice = null;
            var Page = ResourceSourceGate.Registry.Search(CsRequest, (Download.ResourcePlatforms)(int)Request.Sources, out Notice);
            if (!string.IsNullOrEmpty(Notice))
                Logger.Info($"资源搜索已降级：{Notice}");

            // ---------- 回填（全量重建，保证顺序与去重结果与 C# 侧完全一致） ----------
            Storage.Results.Clear();
            foreach (var Project in CSharpStorage.Results)
                Storage.Results.Add(new ResourceProject(Project));
            Storage.CurseForgeOffset = CSharpStorage.CurseForgeOffset;
            Storage.CurseForgeTotal = CSharpStorage.CurseForgeTotal;
            Storage.ModrinthOffset = CSharpStorage.ModrinthOffset;
            Storage.ModrinthTotal = CSharpStorage.ModrinthTotal;
            Storage.ErrorMessage = CSharpStorage.ErrorMessage;
            Logger.Info($"资源搜索完成：本页新增 {Page.Items.Count} 个，累计 {Storage.Results.Count} 个" + $"（CurseForge {Storage.CurseForgeOffset}/{Storage.CurseForgeTotal}，Modrinth {Storage.ModrinthOffset}/{Storage.ModrinthTotal}）");
        }

    }
}