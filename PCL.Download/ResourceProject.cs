using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.Download;

/// <summary>
/// 社区资源的项目信息（平移自 VB ResourceProject.vb，行为与 VB 等价）。
/// UI 胶水方法（ToResourceItem / ApplyLogoToMyImage）不迁入本库，属 Phase 4 桥接层职责。
/// 与 VB 的差异：镜像源可能缺失的字段（如 logo、categories）按「字段缺失 = 该信息不可用」容错处理，
/// VB 在这些输入上会直接抛空引用异常；对真实 API 响应两者行为一致（容错加固属 P1，见调研文档 §5.4）。
/// </summary>
public class ResourceProject
{
    //源信息

    /// <summary>
    /// 该项目信息的所属平台。
    /// </summary>
    public ResourcePlatforms Platform { get; }
    /// <summary>
    /// 工程中包含的文件种类。
    /// 若为 Modrinth 工程，可能为 Mod 或 数据包。
    /// </summary>
    public ResourceTypes Types { get; }
    /// <summary>
    /// 工程的短名。例如 technical-enchant。
    /// </summary>
    public string Slug { get; }
    /// <summary>
    /// CurseForge 工程的数字 ID。Modrinth 工程的乱码 ID。
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// CurseForge 文件列表的数字 ID。Modrinth 工程的此项无效。
    /// </summary>
    public List<int>? CurseForgeFileIds { get; }

    //描述性信息

    /// <summary>
    /// 原始的英文名称。
    /// </summary>
    public string RawName { get; }
    /// <summary>
    /// 英文描述。
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// 来源网站的工程页面网址。确保格式一定标准。
    /// CurseForge：https://www.curseforge.com/minecraft/mc-mods/jei
    /// Modrinth：https://modrinth.com/mod/technical-enchant
    /// </summary>
    public string Website { get; }
    /// <summary>
    /// 最后一次更新的时间。
    /// </summary>
    public DateTime LastUpdate { get; }
    /// <summary>
    /// 下载量计数。注意，该计数仅为一个来源，无法反应两边加起来的下载量！
    /// </summary>
    public int DownloadCount { get; }
    /// <summary>
    /// 所有支持的 Mod 加载器。可能为 <see cref="ModLoaders.None"/>。
    /// </summary>
    public ModLoaders ModLoaders { get; }
    /// <summary>
    /// 描述性标签的内容。已转换为中文。
    /// </summary>
    public List<string> Tags { get; }
    /// <summary>
    /// Logo 图片的下载地址。
    /// 若为 null 则没有，保证不为空字符串。
    /// </summary>
    public string? LogoUrl { get; set; }
    /// <summary>
    /// 支持的 Drop 编号，从高到低排序，不为 null。
    /// 例如：261（26.1.x）、180（1.18.x）。
    /// </summary>
    public List<int> Drops { get; }
    /// <summary>
    /// Modrinth API 返回的原始版本列表。
    /// 仅用于 Modrinth 工程的二次筛选，不会被缓存，不会被预处理。
    /// 若非从 Modrinth 获取则为 null。
    /// </summary>
    public List<string>? UnsafeGameVersions { get; }

    //数据库信息

    private bool LoadedDatabase;
    private WikiInfo? _wikiEntry;

    /// <summary>
    /// 关联的数据库条目。若为 null 则没有。
    /// 懒加载：<see cref="ResourceConfig.WikiLookup"/> 以 (slug, platform) 查询（对应 VB 的 WikiEntry.All 按 Slugs 检索）。
    /// </summary>
    private WikiInfo? WikiEntry
    {
        get
        {
            if (!LoadedDatabase)
            {
                LoadedDatabase = true;
                if (Types.HasFlagF(ResourceTypes.Mod) || Types.HasFlagF(ResourceTypes.DataPack))
                    _wikiEntry = ResourceConfig.WikiLookup?.Invoke(Slug, Platform);
            }
            return _wikiEntry;
        }
        set
        {
            LoadedDatabase = true;
            _wikiEntry = value;
        }
    }

    /// <summary>
    /// MC 百科的页面 ID。若为 0 则没有。
    /// </summary>
    public int WikiId => WikiEntry?.Id ?? 0;

    /// <summary>
    /// 翻译后的中文名。若数据库没有则等同于 RawName。
    /// </summary>
    public string TranslatedName => WikiEntry?.ChineseName ?? RawName;

    //实例化

    /// <summary>
    /// 从工程 Json 中初始化实例。若出错会抛出异常。
    /// 同时接受三种输入：缓存 JSON（含 Tags 键，PascalCase）、CurseForge API（含 summary）、Modrinth API。
    /// </summary>
    public ResourceProject(JsonNode data)
    {
        if (data.AsObject().ContainsKey("Tags"))
        {
            //缓存 JSON
            Platform = (ResourcePlatforms)data["Platform"].AsInt();
            Types = (ResourceTypes)data["Types"].AsInt();
            Slug = data["Slug"].AsString() ?? "";
            Id = data["Id"].AsString() ?? "";
            if (data["CurseForgeFileIds"] is JsonArray fileIds) CurseForgeFileIds = fileIds.Select(t => t.AsInt()).ToList();
            RawName = data["RawName"].AsString() ?? "";
            Description = data["Description"].AsString() ?? "";
            Website = data["Website"].AsString() ?? "";
            LastUpdate = data["LastUpdate"].AsDate();
            DownloadCount = data["DownloadCount"].AsInt();
            if (data.AsObject().ContainsKey("ModLoaders")) ModLoaders = (ModLoaders)data["ModLoaders"].AsInt();
            Tags = data["Tags"].AsArrayOrEmpty().Select(t => t.AsString() ?? "").ToList();
            if (data.AsObject().ContainsKey("LogoUrl")) LogoUrl = data["LogoUrl"].AsString();
            Drops = data["Drops"] is JsonArray drops ? drops.Select(t => t.AsInt()).ToList() : [];
        }
        else
        {
            Platform = data.AsObject().ContainsKey("summary") ? ResourcePlatforms.CurseForge : ResourcePlatforms.Modrinth;
            if (Platform == ResourcePlatforms.CurseForge)
            {
                //CurseForge
                //简单信息
                Id = data["id"].AsString() ?? "";
                Slug = data["slug"].AsString() ?? "";
                RawName = data["name"].AsString() ?? "";
                Description = data["summary"].AsString() ?? "";
                Website = (data["links"]?["websiteUrl"].AsString() ?? "").TrimEnd('/');
                LastUpdate = data["dateReleased"].AsDate(); //#1194
                DownloadCount = data["downloadCount"].AsInt();
                if (data["logo"] is JsonObject logo && logo.Count > 0)
                {
                    LogoUrl = logo["thumbnailUrl"].AsString();
                    if (string.IsNullOrEmpty(LogoUrl)) LogoUrl = logo["url"].AsString();
                }
                if (LogoUrl == "") LogoUrl = null;
                //Type
                if (Website.Contains("/mc-mods/", StringComparison.Ordinal) || Website.Contains("/mod/", StringComparison.Ordinal))
                    Types = ResourceTypes.Mod;
                else if (Website.Contains("/modpacks/", StringComparison.Ordinal))
                    Types = ResourceTypes.ModPack;
                else if (Website.Contains("/resourcepacks/", StringComparison.Ordinal))
                    Types = ResourceTypes.ResourcePack;
                else if (Website.Contains("/texture-packs/", StringComparison.Ordinal))
                    Types = ResourceTypes.ResourcePack;
                else if (Website.Contains("/shaders/", StringComparison.Ordinal))
                    Types = ResourceTypes.Shader;
                else
                    Types = ResourceTypes.DataPack;
                //FileIndexes / VanillaMajorVersions / ModLoaders
                ModLoaders = ModLoaders.None;
                var files = new List<(int FileId, List<string> GameVersions)>(); //FileId, GameVersions
                foreach (var file in data["latestFiles"].AsArrayOrEmpty())
                {
                    var newFile = ResourceVersion.FromPlatformJson(file!, Types);
                    if (!newFile.DownloadAvailable) continue;
                    ModLoaders |= newFile.ModLoaders;
                    var gameVersions = file!["gameVersions"].AsArrayOrEmpty().Select(t => t.AsString() ?? "").ToList();
                    if (!gameVersions.Any(McVersionUtil.IsStandardFormat)) continue;
                    files.Add((file["id"].AsInt(), gameVersions));
                }
                foreach (var file in data["latestFilesIndexes"].AsArrayOrEmpty()) //这俩玩意儿包含的文件不一样，见 #3599
                {
                    if (!McVersionUtil.IsStandardFormat(file?["gameVersion"].AsString())) continue;
                    files.Add((file!["fileId"].AsInt(), [file["gameVersion"].AsString() ?? ""]));
                }
                CurseForgeFileIds = files.Select(f => f.FileId).Distinct().ToList();
                Drops = files.SelectMany(f => f.GameVersions)
                    .Select(McVersionUtil.VersionToDrop).Where(v => v != 209).Distinct().OrderByDescending(v => v).ToList();
                //Tags
                Tags = [];
                foreach (var category in data["categories"].AsArrayOrEmpty() //镜像源 API 可能丢失此字段 (4267#issuecomment-2254590831)
                    .Select(t => t?["id"].AsInt() ?? 0).Distinct().OrderByDescending(c => c))
                {
                    switch (category)
                    {
                        //Mod
                        case 406: Tags.Add("世界元素"); break;
                        case 407: Tags.Add("生物群系"); break;
                        case 410: Tags.Add("维度"); break;
                        case 408: Tags.Add("矿物/资源"); break;
                        case 409: Tags.Add("天然结构"); break;
                        case 412: Tags.Add("科技"); break;
                        case 415: Tags.Add("管道/物流"); break;
                        case 4843: Tags.Add("自动化"); break;
                        case 417: Tags.Add("能源"); break;
                        case 4558: Tags.Add("红石"); break;
                        case 436: Tags.Add("食物/烹饪"); break;
                        case 416: Tags.Add("农业"); break;
                        case 414: Tags.Add("运输"); break;
                        case 420: Tags.Add("仓储"); break;
                        case 419: Tags.Add("魔法"); break;
                        case 422: Tags.Add("冒险"); break;
                        case 424: Tags.Add("装饰"); break;
                        case 411: Tags.Add("生物"); break;
                        case 434: Tags.Add("装备"); break;
                        case 6814: Tags.Add("性能优化"); break;
                        case 9026: Tags.Add("创造模式"); break;
                        case 423: Tags.Add("信息显示"); break;
                        case 435: Tags.Add("服务器"); break;
                        case 5191: Tags.Add("改良"); break;
                        case 421: Tags.Add("支持库"); break;
                        //整合包
                        case 4484: Tags.Add("多人"); break;
                        case 4479: Tags.Add("硬核"); break;
                        case 4483: Tags.Add("战斗"); break;
                        case 4478: Tags.Add("任务"); break;
                        case 4472: Tags.Add("科技"); break;
                        case 4473: Tags.Add("魔法"); break;
                        case 4475: Tags.Add("冒险"); break;
                        case 4476: Tags.Add("探索"); break;
                        case 4477: Tags.Add("小游戏"); break;
                        case 4474: Tags.Add("科幻"); break;
                        case 4736: Tags.Add("空岛"); break;
                        case 5128: Tags.Add("原版改良"); break;
                        case 4487: Tags.Add("FTB"); break;
                        case 4480: Tags.Add("基于地图"); break;
                        case 4481: Tags.Add("轻量"); break;
                        case 4482: Tags.Add("大型"); break;
                        //资源包
                        case 403: Tags.Add("原版风"); break;
                        case 400: Tags.Add("写实风"); break;
                        case 401: Tags.Add("现代风"); break;
                        case 402: Tags.Add("中世纪"); break;
                        case 399: Tags.Add("蒸汽朋克"); break;
                        case 5244: Tags.Add("含字体"); break;
                        case 404: Tags.Add("动态效果"); break;
                        case 4465: Tags.Add("兼容 Mod"); break;
                        case 393: Tags.Add("16x"); break;
                        case 394: Tags.Add("32x"); break;
                        case 395: Tags.Add("64x"); break;
                        case 396: Tags.Add("128x"); break;
                        case 397: Tags.Add("256x"); break;
                        case 398: Tags.Add("超高清"); break;
                        case 5193: Tags.Add("数据包"); break; //有这个 Tag 的项会从资源包请求中被移除
                        //光影包
                        case 6553: Tags.Add("写实风"); break;
                        case 6554: Tags.Add("幻想风"); break;
                        case 6555: Tags.Add("原版风"); break;
                        //数据包
                        case 6948: Tags.Add("冒险"); break;
                        case 6949: Tags.Add("幻想"); break;
                        case 6950: Tags.Add("支持库"); break;
                        case 6952: Tags.Add("魔法"); break;
                        case 6946: Tags.Add("Mod 相关"); break;
                        case 6951: Tags.Add("科技"); break;
                        case 6953: Tags.Add("实用"); break;
                    }
                }
            }
            else
            {
                //Modrinth
                //简单信息
                Id = data["project_id"].AsString() ?? data["id"].AsString() ?? ""; //两个 API 会返回的 key 不一样
                Slug = data["slug"].AsString() ?? "";
                RawName = data["title"].AsString() ?? "";
                Description = data["description"].AsString() ?? "";
                LastUpdate = (data["date_modified"] ?? data["updated"]).AsDate();
                DownloadCount = data["downloads"].AsInt();
                LogoUrl = data["icon_url"].AsString();
                if (LogoUrl == "") LogoUrl = null;
                Website = $"https://modrinth.com/{data["project_type"].AsString()}/{Slug}";
                //GameVersions
                //搜索结果的键为 versions，获取特定工程的键为 game_versions
                UnsafeGameVersions = (data["game_versions"] ?? data["versions"]).AsArrayOrEmpty().Select(v => v.AsString() ?? "").Distinct().ToList();
                Drops = UnsafeGameVersions.Select(McVersionUtil.VersionToDrop).Where(v => v != 209).Distinct().OrderByDescending(v => v).ToList();
                //Type
                switch (data["project_type"].AsString())
                {
                    case "modpack": Types = ResourceTypes.ModPack; break;
                    case "resourcepack": Types = ResourceTypes.ResourcePack; break;
                    case "shader": Types = ResourceTypes.Shader; break;
                    default: Types = ResourceTypes.Mod; break; //Modrinth 将数据包标为 Mod，但 categories 字段里有 datapack
                }
                //Tags & ModLoaders
                Tags = [];
                ModLoaders = ModLoaders.None;
                foreach (var category in data["loaders"].AsArrayOrEmpty().Select(t => t.AsString() ?? "")
                    .Concat(data["categories"].AsArrayOrEmpty().Select(t => t.AsString() ?? "")))
                {
                    foreach (var loader in VbCompat.GetAllFlags<ModLoaders>())
                    {
                        if (category == loader.ToString().ToLowerInvariant())
                        {
                            ModLoaders |= loader;
                            break;
                        }
                    }
                }
                foreach (var category in data["categories"].AsArrayOrEmpty().Select(t => t.AsString() ?? ""))
                {
                    switch (category)
                    {
                        case "datapack": Types = ResourceTypes.DataPack; break;
                        //共用
                        case "technology": Tags.Add("科技"); break;
                        case "magic": Tags.Add("魔法"); break;
                        case "adventure": Tags.Add("冒险"); break;
                        case "utility": Tags.Add("实用"); break;
                        case "optimization": Tags.Add("性能优化"); break;
                        case "vanilla-like": Tags.Add("原版风"); break;
                        case "realistic": Tags.Add("写实风"); break;
                        //Mod/数据包
                        case "worldgen": Tags.Add("世界元素"); break;
                        case "food": Tags.Add("食物/烹饪"); break;
                        case "game-mechanics": Tags.Add("游戏机制"); break;
                        case "transportation": Tags.Add("运输"); break;
                        case "storage": Tags.Add("仓储"); break;
                        case "decoration": if (Types != ResourceTypes.ResourcePack) Tags.Add("装饰"); break;
                        case "mobs": if (Types != ResourceTypes.ResourcePack) Tags.Add("生物"); break;
                        case "equipment": if (Types != ResourceTypes.ResourcePack) Tags.Add("装备"); break;
                        case "social": Tags.Add("服务器"); break;
                        case "library": Tags.Add("支持库"); break;
                        //整合包
                        case "multiplayer": Tags.Add("多人"); break;
                        case "challenging": Tags.Add("硬核"); break;
                        case "combat": Tags.Add("战斗"); break;
                        case "quests": Tags.Add("任务"); break;
                        case "kitchen-sink": Tags.Add("水槽包"); break;
                        case "lightweight": Tags.Add("轻量"); break;
                        //资源包
                        case "simplistic": Tags.Add("简洁"); break;
                        //（VB 原代码中 "combat" 重复出现于资源包段，Select Case 只命中第一个，此处不重复）
                        case "tweaks": Tags.Add("改良"); break;
                        case "8x-": Tags.Add("极简"); break;
                        case "16x": Tags.Add("16x"); break;
                        case "32x": Tags.Add("32x"); break;
                        case "48x": Tags.Add("48x"); break;
                        case "64x": Tags.Add("64x"); break;
                        case "128x": Tags.Add("128x"); break;
                        case "256x": Tags.Add("256x"); break;
                        case "512x+": Tags.Add("超高清"); break;
                        case "audio": Tags.Add("含声音"); break;
                        case "fonts": Tags.Add("含字体"); break;
                        case "models": Tags.Add("含模型"); break;
                        case "gui": Tags.Add("含 UI"); break;
                        case "locale": Tags.Add("含语言"); break;
                        case "core-shaders": Tags.Add("核心着色器"); break;
                        case "modded": Tags.Add("兼容 Mod"); break;
                        //光影包
                        case "fantasy": Tags.Add("幻想风"); break;
                        case "semi-realistic": Tags.Add("半写实风"); break;
                        case "cartoon": Tags.Add("卡通风"); break;
                        //暂时不添加性能负荷 Tag
                        case "colored-lighting": Tags.Add("彩色光照"); break;
                        case "path-tracing": Tags.Add("路径追踪"); break;
                        case "pbr": Tags.Add("PBR"); break;
                        case "reflections": Tags.Add("反射"); break;
                        case "iris": Tags.Add("Iris"); break;
                        case "optifine": Tags.Add("OptiFine"); break;
                        case "vanilla": Tags.Add("原版可用"); break;
                    }
                }
                if (Types == ResourceTypes.DataPack && ModLoaders != ModLoaders.None) Types = ResourceTypes.DataPack | ResourceTypes.Mod;
            }
            if (Tags.Count == 0) Tags.Add("其他");
            Tags.Sort();
        }
        //保存缓存
        Cache[Id] = this;
    }

    /// <summary>
    /// 从工程 ID 联网获取对应工程的信息。
    /// </summary>
    /// <param name="projectId">工程 ID。</param>
    /// <param name="platform">平台，必须为单一平台。</param>
    /// <param name="api">资源站请求器；null 时使用 <see cref="ResourceApi.Default"/>。</param>
    public static ResourceProject FromProjectId(string projectId, ResourcePlatforms platform, IResourceApi? api = null)
    {
        if (platform.FlagsOf().Count() != 1) throw new ArgumentException($"必须指定单一平台，当前值为 {platform}");
        if (Cache.ContainsKey(projectId)) //存在缓存
        {
            return Cache[projectId];
        }
        var client = ResourceApi.Resolve(api);
        if (platform == ResourcePlatforms.CurseForge) //CurseForge
        {
            return new ResourceProject(client.Request("https://api.curseforge.com/v1/mods/" + projectId)!["data"]!);
        }
        else //Modrinth
        {
            return new ResourceProject(client.Request("https://api.modrinth.com/v2/project/" + projectId)!);
        }
    }

    /// <summary>
    /// 从工程 ID 列表联网获取工程信息，结果不按原顺序排列。
    /// </summary>
    /// <param name="projectIds">工程 ID 列表。</param>
    /// <param name="platform">平台，必须为单一平台。</param>
    /// <param name="api">资源站请求器；null 时使用 <see cref="ResourceApi.Default"/>。</param>
    public static IEnumerable<ResourceProject> FromProjectIds(IEnumerable<string> projectIds, ResourcePlatforms platform, IResourceApi? api = null)
    {
        if (platform.FlagsOf().Count() != 1) throw new ArgumentException($"必须指定单一平台，当前值为 {platform}");
        //获取缓存
        var missingProjectIds = new List<string>();
        foreach (var projectId in projectIds)
        {
            if (Cache.ContainsKey(projectId))
            {
                yield return Cache[projectId];
            }
            else
            {
                missingProjectIds.Add(projectId);
            }
        }
        if (missingProjectIds.Count == 0) yield break;
        //联网获取剩余的工程对象
        var client = ResourceApi.Resolve(api);
        JsonArray projects;
        if (platform.HasFlagF(ResourcePlatforms.CurseForge))
        {
            projects = client.Request("https://api.curseforge.com/v1/mods",
                HttpMethod.Post, "{\"modIds\": [" + string.Join(",", missingProjectIds) + "]}", "application/json")!["data"].AsArrayOrEmpty();
        }
        else
        {
            projects = client.Request($"https://api.modrinth.com/v2/projects?ids=[\"{string.Join("\",\"", missingProjectIds)}\"]").AsArrayOrEmpty();
        }
        foreach (var project in projects)
        {
            yield return new ResourceProject(project!);
        }
    }

    /// <summary>
    /// 将当前实例转为可用于保存缓存的 Json。
    /// </summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["Platform"] = (int)Platform,
            ["Types"] = (int)Types,
            ["Slug"] = Slug,
            ["Id"] = Id,
        };
        if (CurseForgeFileIds is not null) json["CurseForgeFileIds"] = new JsonArray(CurseForgeFileIds.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        json["RawName"] = RawName;
        json["Description"] = Description;
        json["Website"] = Website;
        json["LastUpdate"] = JsonValue.Create(LastUpdate);
        json["DownloadCount"] = DownloadCount;
        if (ModLoaders != ModLoaders.None) json["ModLoaders"] = (int)ModLoaders;
        json["Tags"] = new JsonArray(Tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        if (LogoUrl is not null) json["LogoUrl"] = LogoUrl;
        if (Drops.Count > 0) json["Drops"] = new JsonArray(Drops.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        json["CacheTime"] = JsonValue.Create(DateTime.Now); //用于检查缓存时间
        return json;
    }

    /// <summary>
    /// 将标题文本拆分为主标题与副标题（平移自 VB GetControlTitle，纯文本逻辑）。
    /// 检查下列代码时可以参考 #1567 的测试例。
    /// </summary>
    /// <param name="hasModLoaderDescription">是否允许副标题包含 Mod 加载器描述。</param>
    public (string Title, string SubTitle) GetControlTitle(bool hasModLoaderDescription)
    {
        string title = RawName;
        List<string>? subtitleList;
        bool noSubtitle = false;
        if (TranslatedName == RawName)
        {
            //没有中文翻译
            //将所有名称分段
            //（VB 原代码为 Split(seps, True)，True 会被转为非法枚举值 -1：net48 上等价于 None + 后续 Trim/过滤，
            //   net10 上会抛 ArgumentException。此处按 net48 的有效结果平移：仅 RemoveEmptyEntries，后接相同的 Trim/过滤。）
            var nameLists = TranslatedName.Split(TitleSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim(' ', '/', '\\')).Where(w => !string.IsNullOrEmpty(w)).ToList();
            if (nameLists.IsSingle())
            {
                noSubtitle = true;
                subtitleList = null;
            }
            else
            {
                //查找其中的缩写、Forge/Fabric 等版本标记
                subtitleList = [];
                var normalNameList = new List<string>();
                foreach (var name in nameLists)
                {
                    var lowerName = name.ToLowerInvariant();
                    if (name == name.ToUpperInvariant() && name != "FPS" && name != "HUD")
                    {
                        //缩写
                        subtitleList.Add(name);
                    }
                    else if (new[] { "neoforge", "forge", "fabric", "quilt" }.Any(l => lowerName.Contains(l, StringComparison.Ordinal)) &&
                             !Regex.IsMatch(lowerName.Replace("neoforge", "").Replace("forge", "").Replace("fabric", "").Replace("quilt", ""), "[a-z]+"))
                    {
                        //Forge/Fabric 等版本标记
                        subtitleList.Add(name);
                    }
                    else
                    {
                        //其他部分
                        normalNameList.Add(name);
                    }
                }
                //根据分类后的结果处理
                if (normalNameList.Count == 0 || subtitleList.Count == 0)
                {
                    noSubtitle = true;
                }
                else
                {
                    //同时包含 NormalName 和 Subtitle
                    title = string.Join(" - ", normalNameList);
                }
            }
        }
        else
        {
            //有中文翻译
            //尝试将文本分为三段：Title (EnglishName) - Suffix
            //检查时注意 Carpet（它没有中文译名，但有 Suffix）和 “汤姆存储 - 星的奇妙优化 (Tom's balabala)”
            title = TranslatedName.Contains(" (", StringComparison.Ordinal) ? TranslatedName.BeforeFirst(" (") : TranslatedName.BeforeLast(" - ");
            string suffix = "";
            if (TranslatedName.AfterLast(")").Contains(" - ", StringComparison.Ordinal)) suffix = TranslatedName.AfterLast(")").AfterLast(" - ");
            string englishName = TranslatedName;
            if (suffix != "") englishName = englishName.Replace(" - " + suffix, "");
            englishName = englishName.Replace(title, "").Trim('(', ')', ' ');
            //中段的额外信息截取（Split 语义同上：按 net48 有效结果平移）
            subtitleList = englishName.Split(TitleSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim(' ', '/')).Where(w => !string.IsNullOrEmpty(w)).ToList();
            if (subtitleList.Count > 1 &&
                !subtitleList.Any(s => s.ToLowerInvariant().Contains("forge", StringComparison.Ordinal) || s.ToLowerInvariant().Contains("fabric", StringComparison.Ordinal) || s.ToLowerInvariant().Contains("quilt", StringComparison.Ordinal)) && //不是标注 XX 版
                !(subtitleList.Count == 2 && subtitleList[^1].ToUpperInvariant() == subtitleList[^1])) //不是缩写
            {
                subtitleList = [englishName]; //使用原名
            }
            //添加后缀
            if (suffix != "") subtitleList.Add(suffix);
        }
        if (noSubtitle) return (title, "");
        //设置标题与描述
        string subtitle = "";
        foreach (var exOriginal in subtitleList!.Distinct().ToList())
        {
            string ex = exOriginal;
            var isModLoaderDescription =
                ex.ToLowerInvariant().Contains("neoforge", StringComparison.Ordinal) || ex.ToLowerInvariant().Contains("forge", StringComparison.Ordinal) ||
                ex.ToLowerInvariant().Contains("fabric", StringComparison.Ordinal) || ex.ToLowerInvariant().Contains("quilt", StringComparison.Ordinal);
            //是否显示 ModLoader 信息
            if (!hasModLoaderDescription && isModLoaderDescription) continue;
            //去除 “Forge/Fabric” 这一无意义提示
            if (ex.Length < 16 && ex.ToLowerInvariant().Contains("fabric", StringComparison.Ordinal) && ex.ToLowerInvariant().Contains("forge", StringComparison.Ordinal)) continue;
            //将 “Forge” 等提示改为 “Forge 版”
            if (isModLoaderDescription && !ex.Contains("版", StringComparison.Ordinal) &&
                ex.ToLowerInvariant().Replace("neoforge", "").Replace("forge", "").Replace("fabric", "").Replace("quilt", "").Length <= 3)
            {
                ex = ex.Replace("Edition", "").Replace("edition", "").Trim().Capitalize() + " 版";
            }
            //将 “forge” 等词语的首字母大写
            ex = ex.Replace("neoforge", "NeoForge").Replace("forge", "Forge").Replace("neo", "Neo").Replace("fabric", "Fabric").Replace("quilt", "Quilt");
            subtitle += "  |  " + ex.Trim();
        }
        return (title, subtitle);
    }

    private static readonly string[] TitleSeparators = [" | ", " - ", "(", ")", "[", "]", "{", "}"];

    /// <summary>
    /// 工程 ID 对应的工程实例的缓存。不会保存到文件。
    /// </summary>
    private static readonly Dictionary<string, ResourceProject> Cache = [];

    /// <summary>清空工程实例内存缓存（测试或需要强制刷新时使用）。</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// 检查是否与某个 Project 是相同的工程，只是在不同的网站。
    /// 注意：Drops 相等反而判为不相似是 VB 原代码的实现（与注释「要求 MC 版本一致」相反，
    /// 疑为上游条件取反笔误）；为保持行为等价此处原样平移，不在迁移中修正。
    /// </summary>
    public bool IsLike(ResourceProject project)
    {
        if (Id == project.Id) return true; //相同实例
        //提取字符串中的字母和数字
        static string GetRaw(string data)
        {
            var result = new StringBuilder();
            foreach (var r in data.Where(char.IsLetterOrDigit)) result.Append(r);
            return result.ToString().ToLowerInvariant();
        }
        //来自不同的网站
        if (Platform == project.Platform) return false;
        //Mod 加载器一致
        if (ModLoaders != project.ModLoaders) return false;
        //若不为光影，则要求 MC 版本一致（VB 原实现：版本列表相同反而返回 false，见类注释）
        if (Types != ResourceTypes.Shader && Drops.Ordered().SequenceEqual(project.Drops.Ordered())) return false;
        //最近更新时间差距在一周以内
        if (Math.Abs((LastUpdate - project.LastUpdate).TotalDays) > 7) return false;
        //MCMOD 翻译名 / 原名 / 描述文本 / Slug 的英文部分相同
        if (TranslatedName == project.TranslatedName ||
            RawName == project.RawName || Description == project.Description ||
            GetRaw(Slug) == GetRaw(project.Slug))
        {
            //如果只有一个有 DatabaseEntry，设置给另外一个
            if (WikiEntry is null && project.WikiEntry is not null) WikiEntry = project.WikiEntry;
            if (WikiEntry is not null && project.WikiEntry is null) project.WikiEntry = WikiEntry;
            return true;
        }
        return false;
    }

    //辅助函数
    /// <inheritdoc />
    public override string ToString() => $"{Id} ({Slug}): {RawName}";

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ResourceProject project && Id == project.Id;

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}
