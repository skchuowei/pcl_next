namespace PCL.Download;

/// <summary>
/// 根据搜索请求获取一系列的工程列表（平移自 VB Modules/Resource/ResourceSearcher.vb）。
/// <para/>本类只实现"语言替换"：行为与 VB 逐一等价；不负责 Loader 状态机包装（由宿主 Phase 4 桥接层经
/// <see cref="SearchRequest.Storage"/> 驱动）。网络请求经 <see cref="IResourceApi"/> 注入，MC 百科与繁简转换经
/// <see cref="ResourceConfig"/> 注入，保持 PCL.Download 不反向引用 net10.0-windows 的 PCLCS。
/// </summary>
public static class ResourceSearcher
{
    private const int ResultPageSize = 40;
    private const string MsgNoChineseResult = "无搜索结果，请尝试搜索其英文名称";

    //输入

    /// <summary>
    /// 资源搜索请求（平移自 VB SearchRequest）。
    /// </summary>
    public sealed class SearchRequest
    {
        /// <summary>结果存储器，会从中获取需加载的页面等。加载后也应输出到此处。</summary>
        public SearchResult Storage { get; init; }
        /// <summary>应当尽量达成的结果数量。</summary>
        public int TargetResultCount { get; init; }
        /// <summary>筛选资源种类。</summary>
        public ResourceTypes Type { get; init; }
        /// <summary>筛选资源标签。空字符串代表不限制。格式例如 "406/worldgen"，分别是 CurseForge 和 Modrinth 的 ID。</summary>
        public string Tag { get; init; } = "";
        /// <summary>筛选 Mod 加载器类别。None 代表不筛选。对于 Modrinth 请求，支持多个枚举值。</summary>
        public ModLoaders ModLoaders { get; init; } = ModLoaders.None;
        /// <summary>筛选 MC 版本。</summary>
        public string? GameVersion { get; init; }
        /// <summary>搜索的初始内容。</summary>
        public string? SearchText { get; init; }
        /// <summary>允许的来源。</summary>
        public ResourcePlatforms Sources { get; init; } = ResourcePlatforms.Any;

        public SearchRequest(ResourceTypes type, SearchResult storage, int targetResultCount)
        {
            Type = type;
            Storage = storage;
            TargetResultCount = targetResultCount;
        }

        /// <summary>
        /// 根据加载位置记录，是否还可以继续获取内容。
        /// </summary>
        public bool CanContinue
        {
            get
            {
                if (Tag.StartsWith("/") || !Sources.HasFlagF(ResourcePlatforms.CurseForge)) Storage.CurseForgeTotal = 0;
                if (Tag.EndsWith("/") || !Sources.HasFlagF(ResourcePlatforms.Modrinth)) Storage.ModrinthTotal = 0;
                if (Storage.CurseForgeTotal == -1 || Storage.ModrinthTotal == -1) return true;
                return Storage.CurseForgeOffset < Storage.CurseForgeTotal || Storage.ModrinthOffset < Storage.ModrinthTotal;
            }
        }

        /// <summary>获取对应的 CurseForge API 请求链接（含 VB 对 Storage.CurseForgeTotal 的副作用）。</summary>
        public string GetCurseForgeAddress(string searchText, bool ignoreModLoaderFilter)
        {
            if (Tag.StartsWith("/")) Storage.CurseForgeTotal = 0;
            var address = $"https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize={ResultPageSize}";
            switch (Type)
            {
                case ResourceTypes.Mod: address += "&classId=6"; break;
                case ResourceTypes.ModPack: address += "&classId=4471"; break;
                case ResourceTypes.DataPack: address += "&classId=6945"; break;
                case ResourceTypes.Shader: address += "&classId=6552"; break;
                case ResourceTypes.ResourcePack: address += "&classId=12"; break;
            }
            if (Tag != "") address += "&categoryId=" + Tag.BeforeFirst("/");
            if (ModLoaders != ModLoaders.None && !ignoreModLoaderFilter)
                address += "&modLoaderType=" + ToCurseForgeModLoaderType(ModLoaders.FlagsOf().First());
            if (!string.IsNullOrEmpty(GameVersion)) address += "&gameVersion=" + GameVersion;
            if (!string.IsNullOrEmpty(searchText)) address += "&searchFilter=" + UrlEscape(searchText);
            if (Storage.CurseForgeOffset > 0) address += "&index=" + Storage.CurseForgeOffset;
            return address;
        }

        /// <summary>获取对应的 Modrinth API 请求链接（含 VB 对 Storage.ModrinthTotal 的副作用）。</summary>
        public string GetModrinthAddress(string searchText, bool ignoreModLoaderFilter)
        {
            if (Tag.EndsWith("/")) Storage.ModrinthTotal = 0;
            var address = $"https://api.modrinth.com/v2/search?limit={ResultPageSize}&index=relevance";
            if (!string.IsNullOrEmpty(searchText)) address += "&query=" + UrlEscape(searchText);
            if (Storage.ModrinthOffset > 0) address += "&offset=" + Storage.ModrinthOffset;
            //facets=[["categories:'game-mechanics'"],["categories:forge","categories:fabric"],["versions:1.19.3"],["project_type:mod"]]
            var facets = new List<string>();
            facets.Add($"[\"project_type:{Type.ToString().ToLowerInvariant()}\"]");
            if (!string.IsNullOrEmpty(Tag)) facets.Add($"[\"categories:'{Tag.AfterLast("/")}'\"]");
            if (ModLoaders != ModLoaders.None && !ignoreModLoaderFilter)
                facets.Add("[" + string.Join(",", ModLoaders.FlagsOf().Select(f => $"\"categories:{f.ToString().ToLowerInvariant()}\"")) + "]");
            if (!string.IsNullOrEmpty(GameVersion)) facets.Add($"[\"versions:'{GameVersion}'\"]");
            address += "&facets=[" + string.Join(",", facets) + "]";
            return address;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not SearchRequest request) return false;
            return Type == request.Type && TargetResultCount == request.TargetResultCount &&
                   Tag == request.Tag && ModLoaders == request.ModLoaders && Sources == request.Sources &&
                   GameVersion == request.GameVersion && SearchText == request.SearchText;
        }

        public override int GetHashCode()
            => (Type, Tag, ModLoaders, GameVersion, SearchText, Sources).GetHashCode();
    }

    /// <summary>
    /// 资源搜索结果存储器（平移自 VB SearchResult）。
    /// </summary>
    public sealed class SearchResult
    {
        //加载位置记录
        public int CurseForgeOffset;
        public int CurseForgeTotal = -1;
        public int ModrinthOffset;
        public int ModrinthTotal = -1;

        //结果列表
        /// <summary>可供展示的所有工程的列表。</summary>
        public List<ResourceProject> Results { get; init; } = [];
        /// <summary>当前的错误信息。如果没有则为 null。</summary>
        public string? ErrorMessage { get; set; }
    }

    //执行

    /// <summary>
    /// 根据搜索请求获取工程列表，结果写入 <see cref="SearchRequest.Storage"/>。
    /// 与 VB 的 Run 行为等价：前置检查 →（中文搜索反查）→ 多平台并行拉取 → IsLike 去重 → 评分排序。
    /// </summary>
    /// <exception cref="Exception">无结果或某个平台失败时抛出（与 VB 一致）。</exception>
    public static void Search(SearchRequest request, IResourceApi api, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        var storage = request.Storage; //避免其他线程对 Request.Storage 重新进行了赋值

        //#Region "前置检查"
        if (storage.Results.Count >= request.TargetResultCount)
        {
            Logger.Info($"已有 {storage.Results.Count} 个结果，多于所需的 {request.TargetResultCount} 个结果，结束处理");
            return;
        }
        else if (!request.CanContinue)
        {
            if (storage.Results.Count == 0)
                throw new Exception("没有符合条件的结果");
            Logger.Info($"已有 {storage.Results.Count} 个结果，少于所需的 {request.TargetResultCount} 个结果，但无法继续获取，结束处理");
            return;
        }

        //#Region "中文搜索"
        var rawSearchText = (request.SearchText ?? "").Trim().ToLowerInvariant();
        Logger.Info($"工程列表搜索原始文本：{rawSearchText}");
        rawSearchText = ToSimplified(rawSearchText); //繁体转简体

        var isChineseSearch = rawSearchText.RegexHasCjk() && !string.IsNullOrEmpty(rawSearchText) &&
            (request.Type == ResourceTypes.Mod || request.Type == ResourceTypes.DataPack); //目前仅对 Mod 和数据包进行中文搜索
        string? curseForgeAltSearchText = null, modrinthAltSearchText = null;
        var modrinthSlugs = new List<string>(); //从中文转为英文的替代搜索内容
        if (isChineseSearch)
        {
            //帮助方法：从搜索项提取可能的英文单词
            Func<FuzzySearch.SearchEntry<WikiEntry>, ResourcePlatforms, IEnumerable<string>> extractWords = (result, source) =>
            {
                var candidates = new List<string>();
                if (result.Item.Slugs.TryGetValue(source, out var slug))
                    candidates.Add(slug.Replace("-", " ").Replace("/", " "));
                if (result.Item.ChineseName is not null)
                    candidates.Add(result.Item.ChineseName
                        .AfterLast(" (").TrimEnd(") ".ToCharArray()).BeforeFirst(" - ")
                        .Replace("-", " ").Replace("/", " ").Replace(":", " ").Replace("(", " ").Replace(")", ""));
                //分词、清洗、去重
                candidates = candidates
                    .SelectMany(c => c.Split(' '))
                    .Select(w => w.TrimStart("{[(".ToCharArray()).TrimEnd("}])".ToCharArray()).ToLowerInvariant())
                    .Where(w =>
                    {
                        if (w.Length <= 1) return false; //单字或空白
                        if (new[] { "the", "of", "mod", "and", "forge", "fabric", "for", "quilt", "neoforge" }.Contains(w)) return false; //常见词
                        if (double.TryParse(w, out var num) && num > 0) return false; //数字
                        if (!w.IsAsciiOnly()) return false; //非纯英文
                        return true;
                    }).Distinct().ToList();
                //如果一个词可以由其他词拼成，则去掉这个词（例如将 ender io enderio 的 enderio 剔除，只保留 ender io）
                bool CanForm(string s) => candidates.Contains(s) || candidates.Any(c => s.StartsWith(c) && CanForm(s[c.Length..]));
                candidates = candidates.Where(w => !candidates.Any(c => c.Length < w.Length && w.StartsWith(c) && CanForm(w[c.Length..]))).ToList();
                return candidates;
            };
            IEnumerable<FuzzySearch.SearchEntry<WikiEntry>> GetSearchEntries(ResourcePlatforms source)
            {
                if (ResourceConfig.WikiEntries is null) yield break;
                foreach (var entry in ResourceConfig.WikiEntries)
                {
                    if (!entry.Slugs.ContainsKey(source)) continue;
                    if (entry.ChineseName is not null)
                    {
                        yield return new FuzzySearch.SearchEntry<WikiEntry>
                        {
                            Item = entry,
                            SearchSource = new List<FuzzySearch.SearchSource>
                            {
                                new(ToSimplified(entry.ChineseName.BeforeFirst(" (")).Split('/'), 1), //部分 Mod 有别名
                                new(ToSimplified(entry.ChineseName.AfterFirst(" (")) + entry.Slugs[source], 0.5),
                            },
                        };
                    }
                    else
                    {
                        yield return new FuzzySearch.SearchEntry<WikiEntry>
                        {
                            Item = entry,
                            SearchSource = new List<FuzzySearch.SearchSource>
                            {
                                new(entry.Slugs[source], 0.5),
                            },
                        };
                    }
                }
            }
            //CurseForge
            if (request.Sources.HasFlagF(ResourcePlatforms.CurseForge))
            {
                var curseForgeSearchEntries = GetSearchEntries(ResourcePlatforms.CurseForge).ToList();
                var curseForgeSearchResults = FuzzySearch.Search(curseForgeSearchEntries, rawSearchText, 100, 0.25);
                if (curseForgeSearchResults.Count > 0)
                {
                    //选取目标（CurseForge 要求每个词都必须匹配上，所以只能选择一个 Mod 进行搜索）
                    var curseForgeCandidates = curseForgeSearchResults.Any(s => s.AbsoluteRight)
                        ? curseForgeSearchResults.Where(s => s.AbsoluteRight).ToList() //优先使用所有完全匹配的
                        : curseForgeSearchResults.MaxByAll(s => s.Similarity); //其次使用所有相似度最高的
                    var curseForgeTarget = curseForgeCandidates.MaxBy(s => s.Item.Popularity); //然后从中选择最受欢迎的那一个
                    //后处理
                    curseForgeAltSearchText = string.Join(" ", extractWords(curseForgeTarget, ResourcePlatforms.CurseForge));
                    Logger.Warn($"中文搜索关键词（CurseForge）：{curseForgeAltSearchText}");
                }
            }
            //Modrinth
            if (request.Sources.HasFlagF(ResourcePlatforms.Modrinth))
            {
                var modrinthSearchEntries = GetSearchEntries(ResourcePlatforms.Modrinth).ToList();
                var modrinthSearchResults = FuzzySearch.Search(modrinthSearchEntries, rawSearchText, 100, 0.25);
                if (modrinthSearchResults.Count > 0)
                {
                    //分词
                    var wordWeights = new Dictionary<string, double>(); //各个单词及其出现的权重
                    foreach (var result in modrinthSearchResults)
                    {
                        foreach (var word in extractWords(result, ResourcePlatforms.Modrinth))
                        {
                            if (!wordWeights.ContainsKey(word)) wordWeights.Add(word, 0);
                            var similarity = result.SearchSource.Any(s => s.Aliases.Contains(rawSearchText)) ? 1000 : result.Similarity; //完全匹配为 1000
                            wordWeights[word] += similarity * result.Item.Popularity; //权重 += 相似度 * 受欢迎程度
                        }
                    }
                    modrinthAltSearchText = wordWeights.MaxBy(w => w.Value).Key;
                    Logger.Warn($"中文搜索关键词（Modrinth）：{modrinthAltSearchText}");
                    //直接请求工程
                    modrinthSlugs = modrinthSearchResults.Take(100).Select(r => r.Item.Slugs[ResourcePlatforms.Modrinth]).ToList();
                }
            }
            //结束
            if (string.IsNullOrEmpty(curseForgeAltSearchText) && string.IsNullOrEmpty(modrinthAltSearchText) && modrinthSlugs.Count == 0)
                throw new Exception(MsgNoChineseResult);
        }
        progress?.Report(0.05);

        var realResults = new List<ResourceProject>();
        var errors = new List<(Exception Ex, ResourcePlatforms Source)>();

        //#Region "从 CurseForge 和 Modrinth 获取结果列表"
        //在 1.14-，部分老 Mod 没有设置支持的加载器，因此添加 Forge 筛选就会出现遗漏
        //所以，在发起请求时不筛选加载器，然后在返回的结果中自行筛除不是 Forge 的 Mod
        var ignoreModLoaderFilter = request.ModLoaders == ModLoaders.Forge && McVersionUtil.VersionToDrop(request.GameVersion) < 140;
        var searchResults = new List<ResourceProject>();
        var lockObj = new object();

        var tasks = new List<Task>();
        //CurseForge 搜索
        if (request.Sources.HasFlagF(ResourcePlatforms.CurseForge) &&
            !(storage.CurseForgeTotal > -1 && storage.CurseForgeTotal <= storage.CurseForgeOffset) && //剩余的未显示的搜索结果不足
            (!isChineseSearch || (isChineseSearch && !string.IsNullOrEmpty(curseForgeAltSearchText)))) //如果是中文搜索，就只在有对应搜索关键词的时候才继续
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var curseForgeUrl = request.GetCurseForgeAddress(curseForgeAltSearchText ?? rawSearchText, ignoreModLoaderFilter);
                    Logger.Info($"开始 CurseForge 搜索：{curseForgeUrl}");
                    var requestResult = api.Request(curseForgeUrl);
                    var projectList = new List<ResourceProject>();
                    foreach (var jsonEntry in requestResult["data"].AsArrayOrEmpty())
                    {
                        var project = new ResourceProject(jsonEntry!);
                        if (request.Type == ResourceTypes.ResourcePack && project.Tags.Contains("数据包")) continue; //CurseForge 将一些数据包分类成了资源包
                        if (request.Type != ResourceTypes.Any && !project.Types.HasFlagF(request.Type)) continue; //过滤分区不匹配的搜索结果（#8265）
                        projectList.Add(project);
                    }
                    lock (lockObj) { foreach (var p in projectList) searchResults.Add(p); }
                    storage.CurseForgeOffset += requestResult["data"].AsArrayOrEmpty().Count();
                    storage.CurseForgeTotal = requestResult["pagination"]["totalCount"].AsInt();
                    Logger.Info($"从 CurseForge 搜索到了 {projectList.Count} 个工程（总计已获取 {storage.CurseForgeOffset} 个，共 {storage.CurseForgeTotal} 个）");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "CurseForge 搜索失败");
                    storage.CurseForgeTotal = -1;
                    lock (lockObj) errors.Add((ex, ResourcePlatforms.CurseForge));
                }
            }));
        }
        //Modrinth 搜索
        if (request.Sources.HasFlagF(ResourcePlatforms.Modrinth) &&
            !(storage.ModrinthTotal > -1 && storage.ModrinthTotal <= storage.ModrinthOffset) && //剩余的未显示的搜索结果不足
            (!isChineseSearch || (isChineseSearch && !string.IsNullOrEmpty(modrinthAltSearchText)))) //如果是中文搜索，就只在有对应搜索关键词的时候才继续
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var modrinthUrl = request.GetModrinthAddress(modrinthAltSearchText ?? rawSearchText, ignoreModLoaderFilter);
                    Logger.Info($"开始 Modrinth 搜索：{modrinthUrl}");
                    var requestResult = api.Request(modrinthUrl);
                    var projectList = new List<ResourceProject>();
                    foreach (var jsonEntry in requestResult["hits"].AsArrayOrEmpty())
                        projectList.Add(new ResourceProject(jsonEntry!));
                    lock (lockObj) { foreach (var p in projectList) searchResults.Add(p); }
                    storage.ModrinthOffset += requestResult["hits"].AsArrayOrEmpty().Count();
                    storage.ModrinthTotal = requestResult["total_hits"].AsInt();
                    Logger.Info($"从 Modrinth 搜索到了 {projectList.Count} 个工程（总计已获取 {storage.ModrinthOffset} 个，共 {storage.ModrinthTotal} 个）");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Modrinth 搜索失败");
                    storage.ModrinthTotal = -1;
                    lock (lockObj) errors.Add((ex, ResourcePlatforms.Modrinth));
                }
            }));
        }
        //Modrinth 直接获取工程
        if (request.Sources.HasFlagF(ResourcePlatforms.Modrinth) &&
            !(storage.ModrinthTotal > -1 && storage.ModrinthTotal <= storage.ModrinthOffset) && //剩余的未显示的搜索结果不足
            modrinthSlugs.Count > 0) //有直接获取的 Slug
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var modrinthUrl = $"https://api.modrinth.com/v2/projects?ids=[\"{string.Join("\",\"", modrinthSlugs)}\"]";
                    Logger.Info($"开始 Modrinth 直接获取：{modrinthUrl}");
                    var projectList = new List<ResourceProject>();
                    foreach (var jsonEntry in api.Request(modrinthUrl).AsArrayOrEmpty())
                    {
                        var project = new ResourceProject(jsonEntry!);
                        //应用筛选
                        if (request.Type != ResourceTypes.Any && !project.Types.HasFlagF(request.Type)) continue;
                        if (!string.IsNullOrEmpty(request.Tag) &&
                            !jsonEntry!["categories"].AsArrayOrEmpty().Any(c => c.AsString() == request.Tag.AfterLast("/"))) continue; //Project.Tags 已经转换成中文了，只能从 json 判
                        if (request.ModLoaders != ModLoaders.None && !ignoreModLoaderFilter &&
                            !project.ModLoaders.FlagsOf().Intersect(request.ModLoaders.FlagsOf()).Any()) continue;
                        if (!string.IsNullOrEmpty(request.GameVersion) &&
                            !(project.UnsafeGameVersions ?? []).Any(d => d == request.GameVersion)) continue;
                        projectList.Add(project);
                    }
                    if (projectList.Count > 0 && request.ModLoaders != ModLoaders.None && !ignoreModLoaderFilter &&
                        !string.IsNullOrEmpty(request.GameVersion))
                    {
                        var projectIds = string.Join("\",\"project_id:", projectList.Select(p => p.Id));
                        var loaders = string.Join("\",\"categories:", request.ModLoaders.FlagsOf().Select(f => f.ToString().ToLowerInvariant()));
                        var filterUrl = $"https://api.modrinth.com/v2/search?limit={projectList.Count}&facets=" +
                            $"[[\"project_id:{projectIds}\"],[\"categories:{loaders}\"],[\"versions:{request.GameVersion}\"]]";
                        var matchedIds = api.Request(filterUrl)["hits"].AsArrayOrEmpty().Select(h => h!["project_id"].AsString() ?? "").ToList();
                        projectList = projectList.Where(p => matchedIds.Contains(p.Id)).ToList();
                    }
                    lock (lockObj) { foreach (var p in projectList) searchResults.Add(p); }
                    Logger.Info($"从 Modrinth 直接获取到了 {projectList.Count} 个工程");
                    modrinthSlugs.Clear(); //防止重试/加载下一页时重复获取
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Modrinth 直接获取失败");
                    lock (lockObj) errors.Add((ex, ResourcePlatforms.Modrinth));
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        ct.ThrowIfCancellationRequested();
        var rawResults = new List<ResourceProject>(searchResults);

        //仅保留兼容 Forge 的 Mod，或老版本中没有标注任何 Mod Loader 的 Mod
        if (ignoreModLoaderFilter)
            rawResults = rawResults.Where(p => p.ModLoaders == ModLoaders.None || p.ModLoaders.HasFlagF(ModLoaders.Forge)).ToList();

        //确保存在结果
        storage.ErrorMessage = null;
        if (rawResults.Count == 0)
        {
            if (errors.Count > 0)
            {
                throw errors[0].Ex;
            }
            else
            {
                if (isChineseSearch && !(request.Type == ResourceTypes.Mod || request.Type == ResourceTypes.DataPack))
                    throw new Exception(MsgNoChineseResult);
                else if (request.Sources == ResourcePlatforms.CurseForge && request.Tag.StartsWith("/"))
                    throw new Exception("CurseForge 不兼容所选的类型");
                else if (request.Sources == ResourcePlatforms.Modrinth && request.Tag.EndsWith("/"))
                    throw new Exception("Modrinth 不兼容所选的类型");
                else
                    throw new Exception("没有搜索结果");
            }
        }
        else if (errors.Count > 0)
        {
            //有结果但是有错误
            if (errors.Any(e => e.Source == ResourcePlatforms.CurseForge))
                storage.ErrorMessage = "无法连接到 CurseForge，所以目前仅显示了来自 Modrinth 的内容，搜索结果可能不全。\n请稍后再试，或使用 VPN 改善网络环境。";
            else
                storage.ErrorMessage = "无法连接到 Modrinth，所以目前仅显示了来自 CurseForge 的内容，搜索结果可能不全。\n请稍后再试，或使用 VPN 改善网络环境。";
        }

        //#Region "提取非重复项，存储于 RealResults"
        //将 CurseForge 排在 Modrinth 的前面，避免加载结束顺序不同导致排名不同
        rawResults = rawResults.Where(x => x.Platform == ResourcePlatforms.CurseForge)
            .Concat(rawResults.Where(x => x.Platform != ResourcePlatforms.CurseForge)).ToList();
        //去重
        rawResults = rawResults.Distinct((a, b) => a.IsLike(b)).ToList();
        //已有内容去重
        rawResults = rawResults.Where(r => !realResults.Any(b => r.IsLike(b)) &&
                                          !storage.Results.Any(b => r.IsLike(b))).ToList();
        //加入列表
        realResults.AddRange(rawResults);
        Logger.Info($"去重、筛选后累计新增结果 {realResults.Count} 个（目前已有结果 {storage.Results.Count} 个）");

        //#Region "检查结果数量，如果不足且可继续，会继续加载下一页"
        if (realResults.Count + storage.Results.Count < request.TargetResultCount)
        {
            Logger.Info($"总结果数需求最少 {request.TargetResultCount} 个，仅获得了 {realResults.Count + storage.Results.Count} 个");
            if (request.CanContinue && errors.Count == 0) //如果有某个源失败则不再重试，这时候重试可能导致无限循环
            {
                Logger.Info("将继续尝试加载下一页");
                Search(request, api, ct, progress); //递归加载下一页
                return;
            }
            else
            {
                Logger.Info("无法继续加载，将强制结束");
            }
        }

        //#Region "将结果排序并添加"
        var scores = new Dictionary<ResourceProject, double>(); //排序分
        double GetDownloadCountMult(ResourceProject project) => (project.Platform, request.Type) switch
        {
            (_, ResourceTypes.Mod) or (_, ResourceTypes.ModPack) => project.Platform == ResourcePlatforms.CurseForge ? 1 : 5,
            (_, ResourceTypes.DataPack) => project.Platform == ResourcePlatforms.CurseForge ? 10 : 1,
            (_, ResourceTypes.ResourcePack) or (_, ResourceTypes.Shader) => project.Platform == ResourcePlatforms.CurseForge ? 1 : 4,
            _ => 1,
        };
        if (string.IsNullOrEmpty(rawSearchText))
        {
            //如果没有搜索文本，按下载量将结果排序
            foreach (var result in realResults)
                scores[result] = result.DownloadCount * GetDownloadCountMult(result);
        }
        else
        {
            //如果有搜索文本，按关联度将结果排序
            //排序分 = 搜索相对相似度 (1) + 下载量权重 (对数，10 亿时为 1) + 有中文名 (0.2)
            var searchEntries = new List<FuzzySearch.SearchEntry<ResourceProject>>();
            foreach (var result in realResults)
            {
                scores[result] = (result.WikiId > 0 ? 0.2 : 0) +
                           Math.Log10(Math.Max(result.DownloadCount, 1) * GetDownloadCountMult(result)) / 9;
                searchEntries.Add(new FuzzySearch.SearchEntry<ResourceProject>
                {
                    Item = result,
                    SearchSource = new List<FuzzySearch.SearchSource>
                    {
                        new((isChineseSearch ? result.TranslatedName : result.RawName).Split('/'), 1),
                        new(result.Description, 0.05),
                    },
                });
            }
            var searchResult = FuzzySearch.Search(searchEntries, rawSearchText, 10000, -1);
            foreach (var oneResult in searchResult)
            {
                scores[oneResult.Item] +=
                    (oneResult.AbsoluteRight ? 10 : oneResult.Similarity) /
                    (searchResult[0].AbsoluteRight ? 10 : searchResult[0].Similarity); //最高 1 分的相似度分
            }
        }
        //根据排序分得出结果并添加
        ct.ThrowIfCancellationRequested();
        storage.Results.AddRange(scores.OrderByDescending(s => s.Value).Select(r => r.Key));
    }

    //辅助

    /// <summary>将单一 Mod Loader 枚举值映射为 CurseForge 的 ModLoaderType（平移自 PCLCS.Resource.ToCurseForgeModLoaderType）。</summary>
    private static int ToCurseForgeModLoaderType(ModLoaders loaders) => loaders switch
    {
        ModLoaders.Forge => 1,
        ModLoaders.LiteLoader => 3,
        ModLoaders.Fabric => 4,
        ModLoaders.NeoForge => 6,
        _ => 0,
    };

    private static string UrlEscape(string text) => Uri.EscapeDataString(text);

    private static string ToSimplified(string text) => ResourceConfig.ToSimplifiedChinese?.Invoke(text) ?? text;
}

/// <summary>
/// 与 VB 字符串扩展对齐的小工具（本文件内使用）。
/// </summary>
internal static class ResourceSearcherStringExt
{
    /// <summary>是否包含 CJK 统一表意文字（\u4e00-\u9fbb）。对应 VB 的 RegexCheck("[\u4e00-\u9fbb]")。</summary>
    public static bool RegexHasCjk(this string value)
    {
        foreach (var c in value)
            if (c is >= '一' and <= '鿿') return true;
        return false;
    }
}
