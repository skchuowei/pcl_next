using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    /// <summary>
/// 社区资源的项目信息。
/// </summary>
    public class ResourceProject
    {

        // 源信息

        /// <summary>
    /// 该项目信息的所属平台。
    /// </summary>
        public readonly ResourcePlatforms Platform;
        /// <summary>
    /// 工程中包含的文件种类。
    /// 若为 Modrinth 工程，可能为 Mod 或 数据包。
    /// </summary>
        public readonly ResourceTypes Types;
        /// <summary>
    /// 工程的短名。例如 technical-enchant。
    /// </summary>
        public readonly string Slug;
        /// <summary>
    /// CurseForge 工程的数字 ID。Modrinth 工程的乱码 ID。
    /// </summary>
        public readonly string Id;
        /// <summary>
    /// CurseForge 文件列表的数字 ID。Modrinth 工程的此项无效。
    /// </summary>
        public readonly List<int> CurseForgeFileIds;

        // 描述性信息

        /// <summary>
    /// 原始的英文名称。
    /// </summary>
        public readonly string RawName;
        /// <summary>
    /// 英文描述。
    /// </summary>
        public readonly string Description;
        /// <summary>
    /// 来源网站的工程页面网址。确保格式一定标准。
    /// CurseForge：https://www.curseforge.com/minecraft/mc-mods/jei
    /// Modrinth：https://modrinth.com/mod/technical-enchant
    /// </summary>
        public readonly string Website;
        /// <summary>
    /// 最后一次更新的时间。
    /// </summary>
        public readonly DateTime LastUpdate;
        /// <summary>
    /// 下载量计数。注意，该计数仅为一个来源，无法反应两边加起来的下载量！
    /// </summary>
        public readonly int DownloadCount;
        /// <summary>
    /// 所有支持的 Mod 加载器。可能为 <see cref="ModLoaders.None"/>。
    /// </summary>
        public readonly ModLoaders ModLoaders = ModLoaders.None;
        /// <summary>
    /// 描述性标签的内容。已转换为中文。
    /// </summary>
        public readonly List<string> Tags;
        /// <summary>
    /// Logo 图片的下载地址。
    /// 若为 Nothing 则没有，保证不为空字符串。
    /// </summary>
        public string LogoUrl = null;
        /// <summary>
    /// 支持的 Drop 编号，从高到低排序，不为 Nothing。
    /// 例如：261（26.1.x）、180（1.18.x）。
    /// </summary>
        public readonly List<int> Drops;
        /// <summary>
    /// Modrinth API 返回的原始版本列表。
    /// 仅用于 Modrinth 工程的二次筛选，不会被缓存，不会被预处理。
    /// 若非从 Modrinth 获取则为 Nothing。
    /// </summary>
        public readonly List<string> UnsafeGameVersions;

        // 数据库信息

        private bool LoadedDatabase = false;
        private WikiEntry _WikiEntry = null;
        /// <summary>
    /// 关联的数据库条目。若为 Nothing 则没有。
    /// </summary>
        private WikiEntry WikiEntry
        {
            get
            {
                if (!LoadedDatabase)
                {
                    LoadedDatabase = true;
                    if (Types.HasFlagF(ResourceTypes.Mod) || Types.HasFlagF(ResourceTypes.DataPack))
                    {
                        _WikiEntry = WikiEntry.All.Value.FirstOrDefault(c => (c.Slugs.GetOrDefault(Platform) ?? "") == (Slug ?? ""));
                    }
                }
                return _WikiEntry;
            }
            set
            {
                LoadedDatabase = true;
                _WikiEntry = value;
            }
        }
        /// <summary>
    /// MC 百科的页面 ID。若为 0 则没有。
    /// </summary>
        public int WikiId
        {
            get
            {
                return WikiEntry is null ? 0 : WikiEntry.Id;
            }
        }
        /// <summary>
    /// 翻译后的中文名。若数据库没有则等同于 RawName。
    /// </summary>
        public string TranslatedName
        {
            get
            {
                return (WikiEntry?.ChineseName) ?? RawName;
            }
        }

        // 实例化

        /// <summary>
    /// 从 C# 侧的 <see cref="PCL.Download.ResourceProject"/> 直接构造（Phase 4 首步：列表数据源改由 Provider 提供）。
    /// 逐字段映射而不经 JSON 中转，避免 UnsafeGameVersions 这类只在内存中使用的字段丢失。
    /// </summary>
        internal ResourceProject(Download.ResourceProject Source)
        {
            if (Source is null)
                throw new ArgumentNullException(nameof(Source));
            Platform = (ResourcePlatforms)(int)Source.Platform;
            Types = (ResourceTypes)(int)Source.Types;
            Slug = Source.Slug;
            Id = Source.Id;
            if (Source.CurseForgeFileIds is not null)
                CurseForgeFileIds = new List<int>(Source.CurseForgeFileIds);
            RawName = Source.RawName;
            Description = Source.Description;
            Website = Source.Website;
            LastUpdate = Source.LastUpdate;
            DownloadCount = Source.DownloadCount;
            ModLoaders = (ModLoaders)(int)Source.ModLoaders;
            Tags = new List<string>(Source.Tags);
            LogoUrl = Source.LogoUrl;
            Drops = new List<int>(Source.Drops);
            if (Source.UnsafeGameVersions is not null)
                UnsafeGameVersions = new List<string>(Source.UnsafeGameVersions);
        }

        /// <summary>
    /// 从工程 Json 中初始化实例。若出错会抛出异常。
    /// </summary>
        public ResourceProject(JObject Data)
        {
            if (Data.ContainsKey("Tags"))
            {
                #region 缓存 JSON
                Platform = (ResourcePlatforms)Data["Platform"].ToObject<int>();
                Types = (ResourceTypes)Data["Types"].ToObject<int>();
                Slug = (string)Data["Slug"];
                Id = (string)Data["Id"];
                if (Data.ContainsKey("CurseForgeFileIds"))
                    CurseForgeFileIds = ((JArray)Data["CurseForgeFileIds"]).Select(t => t.ToObject<int>()).ToList();
                RawName = (string)Data["RawName"];
                Description = (string)Data["Description"];
                Website = (string)Data["Website"];
                LastUpdate = (DateTime)Data["LastUpdate"];
                DownloadCount = (int)Data["DownloadCount"];
                if (Data.ContainsKey("ModLoaders"))
                    ModLoaders = (ModLoaders)Data["ModLoaders"].ToObject<int>();
                Tags = ((JArray)Data["Tags"]).Select(t => t.ToString()).ToList();
                if (Data.ContainsKey("LogoUrl"))
                    LogoUrl = (string)Data["LogoUrl"];
                if (Data.ContainsKey("Drops"))
                {
                    Drops = ((JArray)Data["Drops"]).Select(t => t.ToObject<int>()).ToList();
                }
                else
                {
                    Drops = new List<int>();
                }
            }
            #endregion
            else
            {
                Platform = Data.ContainsKey("summary") ? ResourcePlatforms.CurseForge : ResourcePlatforms.Modrinth;
                if (Platform == ResourcePlatforms.CurseForge)
                {
                    #region CurseForge
                    // 简单信息
                    Id = (string)Data["id"];
                    Slug = (string)Data["slug"];
                    RawName = (string)Data["name"];
                    Description = (string)Data["summary"];
                    Website = Data["links"]["websiteUrl"].ToString().TrimEnd('/');
                    LastUpdate = (DateTime)Data["dateReleased"]; // #1194
                    DownloadCount = (int)Data["downloadCount"];
                    if (Data["logo"].Count() > 0)
                    {
                        if (Data["logo"]["thumbnailUrl"] is null || (string)Data["logo"]["thumbnailUrl"] == "")
                        {
                            LogoUrl = (string)Data["logo"]["url"];
                        }
                        else
                        {
                            LogoUrl = (string)Data["logo"]["thumbnailUrl"];
                        }
                    }
                    if (string.IsNullOrEmpty(LogoUrl))
                        LogoUrl = null;
                    // Type
                    if (Website.Contains("/mc-mods/") || Website.Contains("/mod/"))
                    {
                        Types = ResourceTypes.Mod;
                    }
                    else if (Website.Contains("/modpacks/"))
                    {
                        Types = ResourceTypes.ModPack;
                    }
                    else if (Website.Contains("/resourcepacks/"))
                    {
                        Types = ResourceTypes.ResourcePack;
                    }
                    else if (Website.Contains("/texture-packs/"))
                    {
                        Types = ResourceTypes.ResourcePack;
                    }
                    else if (Website.Contains("/shaders/"))
                    {
                        Types = ResourceTypes.Shader;
                    }
                    else
                    {
                        Types = ResourceTypes.DataPack;
                    }
                    // FileIndexes / VanillaMajorVersions / ModLoaders
                    ModLoaders = ModLoaders.None;
                    var Files = new List<KeyValuePair<int, List<string>>>(); // FileId, GameVersions
                    foreach (var File in Data["latestFiles"] ?? new JArray())
                    {
                        var NewFile = ResourceVersion.FromPlatformJson((JObject)File, Types);
                        if (!NewFile.DownloadAvailable)
                            continue;
                        ModLoaders = ModLoaders | NewFile.ModLoaders;
                        var GameVersions = File["gameVersions"].ToObject<List<string>>();
                        if (!GameVersions.Any(v => McVersion.IsStandardFormat(v)))
                            continue;
                        Files.Add(new KeyValuePair<int, List<string>>((int)File["id"], GameVersions));
                    }
                    foreach (var File in Data["latestFilesIndexes"] ?? new JArray()) // 这俩玩意儿包含的文件不一样，见 #3599
                    {
                        if (!McVersion.IsStandardFormat((string)File["gameVersion"]))
                            continue;
                        Files.Add(new KeyValuePair<int, List<string>>((int)File["fileId"], new[] { File["gameVersion"].ToString() }.ToList()));
                    }
                    CurseForgeFileIds = Files.Select(f => f.Key).Distinct().ToList();
                    Drops = Files.SelectMany(f => f.Value).Select(v => McVersion.VersionToDrop(v)).Where(v => v != 209).Distinct().OrderByDescending(v => v).ToList();
                    // Tags
                    Tags = new List<string>();
                    foreach (var Category in (Data["categories"] ?? new JArray()).Select<JToken, int>(t => (int)t["id"]).Distinct().OrderByDescending(c => c)) // 镜像源 API 可能丢失此字段 (4267#issuecomment-2254590831)
                    {
                        switch (Category)
                        {
                            // Mod
                            case 406:
                                {
                                    Tags.Add("世界元素");
                                    break;
                                }
                            case 407:
                                {
                                    Tags.Add("生物群系");
                                    break;
                                }
                            case 410:
                                {
                                    Tags.Add("维度");
                                    break;
                                }
                            case 408:
                                {
                                    Tags.Add("矿物/资源");
                                    break;
                                }
                            case 409:
                                {
                                    Tags.Add("天然结构");
                                    break;
                                }
                            case 412:
                                {
                                    Tags.Add("科技");
                                    break;
                                }
                            case 415:
                                {
                                    Tags.Add("管道/物流");
                                    break;
                                }
                            case 4843:
                                {
                                    Tags.Add("自动化");
                                    break;
                                }
                            case 417:
                                {
                                    Tags.Add("能源");
                                    break;
                                }
                            case 4558:
                                {
                                    Tags.Add("红石");
                                    break;
                                }
                            case 436:
                                {
                                    Tags.Add("食物/烹饪");
                                    break;
                                }
                            case 416:
                                {
                                    Tags.Add("农业");
                                    break;
                                }
                            case 414:
                                {
                                    Tags.Add("运输");
                                    break;
                                }
                            case 420:
                                {
                                    Tags.Add("仓储");
                                    break;
                                }
                            case 419:
                                {
                                    Tags.Add("魔法");
                                    break;
                                }
                            case 422:
                                {
                                    Tags.Add("冒险");
                                    break;
                                }
                            case 424:
                                {
                                    Tags.Add("装饰");
                                    break;
                                }
                            case 411:
                                {
                                    Tags.Add("生物");
                                    break;
                                }
                            case 434:
                                {
                                    Tags.Add("装备");
                                    break;
                                }
                            case 6814:
                                {
                                    Tags.Add("性能优化");
                                    break;
                                }
                            case 9026:
                                {
                                    Tags.Add("创造模式");
                                    break;
                                }
                            case 423:
                                {
                                    Tags.Add("信息显示");
                                    break;
                                }
                            case 435:
                                {
                                    Tags.Add("服务器");
                                    break;
                                }
                            case 5191:
                                {
                                    Tags.Add("改良");
                                    break;
                                }
                            case 421:
                                {
                                    Tags.Add("支持库");
                                    break;
                                }
                            // 整合包
                            case 4484:
                                {
                                    Tags.Add("多人");
                                    break;
                                }
                            case 4479:
                                {
                                    Tags.Add("硬核");
                                    break;
                                }
                            case 4483:
                                {
                                    Tags.Add("战斗");
                                    break;
                                }
                            case 4478:
                                {
                                    Tags.Add("任务");
                                    break;
                                }
                            case 4472:
                                {
                                    Tags.Add("科技");
                                    break;
                                }
                            case 4473:
                                {
                                    Tags.Add("魔法");
                                    break;
                                }
                            case 4475:
                                {
                                    Tags.Add("冒险");
                                    break;
                                }
                            case 4476:
                                {
                                    Tags.Add("探索");
                                    break;
                                }
                            case 4477:
                                {
                                    Tags.Add("小游戏");
                                    break;
                                }
                            case 4474:
                                {
                                    Tags.Add("科幻");
                                    break;
                                }
                            case 4736:
                                {
                                    Tags.Add("空岛");
                                    break;
                                }
                            case 5128:
                                {
                                    Tags.Add("原版改良");
                                    break;
                                }
                            case 4487:
                                {
                                    Tags.Add("FTB");
                                    break;
                                }
                            case 4480:
                                {
                                    Tags.Add("基于地图");
                                    break;
                                }
                            case 4481:
                                {
                                    Tags.Add("轻量");
                                    break;
                                }
                            case 4482:
                                {
                                    Tags.Add("大型");
                                    break;
                                }
                            // 资源包
                            case 403:
                                {
                                    Tags.Add("原版风");
                                    break;
                                }
                            case 400:
                                {
                                    Tags.Add("写实风");
                                    break;
                                }
                            case 401:
                                {
                                    Tags.Add("现代风");
                                    break;
                                }
                            case 402:
                                {
                                    Tags.Add("中世纪");
                                    break;
                                }
                            case 399:
                                {
                                    Tags.Add("蒸汽朋克");
                                    break;
                                }
                            case 5244:
                                {
                                    Tags.Add("含字体");
                                    break;
                                }
                            case 404:
                                {
                                    Tags.Add("动态效果");
                                    break;
                                }
                            case 4465:
                                {
                                    Tags.Add("兼容 Mod");
                                    break;
                                }
                            case 393:
                                {
                                    Tags.Add("16x");
                                    break;
                                }
                            case 394:
                                {
                                    Tags.Add("32x");
                                    break;
                                }
                            case 395:
                                {
                                    Tags.Add("64x");
                                    break;
                                }
                            case 396:
                                {
                                    Tags.Add("128x");
                                    break;
                                }
                            case 397:
                                {
                                    Tags.Add("256x");
                                    break;
                                }
                            case 398:
                                {
                                    Tags.Add("超高清");
                                    break;
                                }
                            case 5193:
                                {
                                    Tags.Add("数据包"); // 有这个 Tag 的项会从资源包请求中被移除
                                    break;
                                }
                            // 光影包
                            case 6553:
                                {
                                    Tags.Add("写实风");
                                    break;
                                }
                            case 6554:
                                {
                                    Tags.Add("幻想风");
                                    break;
                                }
                            case 6555:
                                {
                                    Tags.Add("原版风");
                                    break;
                                }
                            // 数据包
                            case 6948:
                                {
                                    Tags.Add("冒险");
                                    break;
                                }
                            case 6949:
                                {
                                    Tags.Add("幻想");
                                    break;
                                }
                            case 6950:
                                {
                                    Tags.Add("支持库");
                                    break;
                                }
                            case 6952:
                                {
                                    Tags.Add("魔法");
                                    break;
                                }
                            case 6946:
                                {
                                    Tags.Add("Mod 相关");
                                    break;
                                }
                            case 6951:
                                {
                                    Tags.Add("科技");
                                    break;
                                }
                            case 6953:
                                {
                                    Tags.Add("实用");
                                    break;
                                }
                        }
                    }
                }
                #endregion
                else
                {
                    #region Modrinth
                    // 简单信息
                    Id = (string)(Data["project_id"] ?? Data["id"]); // 两个 API 会返回的 key 不一样
                    Slug = (string)Data["slug"];
                    RawName = (string)Data["title"];
                    Description = (string)Data["description"];
                    LastUpdate = (DateTime)(Data["date_modified"] ?? Data["updated"]);
                    DownloadCount = (int)Data["downloads"];
                    LogoUrl = (string)Data["icon_url"];
                    if (string.IsNullOrEmpty(LogoUrl))
                        LogoUrl = null;
                    Website = $"https://modrinth.com/{Data["project_type"]}/{Slug}";
                    // GameVersions
                    // 搜索结果的键为 versions，获取特定工程的键为 game_versions
                    UnsafeGameVersions = ((JArray)(Data["game_versions"] ?? Data["versions"]) ?? new JArray()).Select(v => v.ToString()).Distinct().ToList();
                    Drops = UnsafeGameVersions.Select(v => McVersion.VersionToDrop(v)).Where(v => v != 209).Distinct().OrderByDescending(v => v).ToList();
                    // Type
                    switch (Data["project_type"].ToString() ?? "")
                    {
                        case "modpack":
                            {
                                Types = ResourceTypes.ModPack;
                                break;
                            }
                        case "resourcepack":
                            {
                                Types = ResourceTypes.ResourcePack;
                                break;
                            }
                        case "shader":
                            {
                                Types = ResourceTypes.Shader;
                                break;
                            }

                        default:
                            {
                                Types = ResourceTypes.Mod; // Modrinth 将数据包标为 Mod，但 categories 字段里有 datapack
                                break;
                            }
                    }
                    // Tags & ModLoaders
                    Tags = new List<string>();
                    ModLoaders = ModLoaders.None;
                    foreach (var Category in ((Data["loaders"]?.Select(t => t.ToString())) ?? Array.Empty<string>()).Concat(Data["categories"].Select(t => t.ToString())))
                    {
                        foreach (var Loader in EnumUtils.GetAllFlags<ModLoaders>())
                        {
                            if ((Category ?? "") == (Loader.ToString().Lower() ?? ""))
                            {
                                ModLoaders = ModLoaders | Loader;
                                break;
                            }
                        }
                    }
                    foreach (var Category in Data["categories"].Select(t => t.ToString()))
                    {
                        switch (Category ?? "")
                        {
                            case "datapack":
                                {
                                    Types = ResourceTypes.DataPack;
                                    break;
                                }
                            // 共用
                            case "technology":
                                {
                                    Tags.Add("科技");
                                    break;
                                }
                            case "magic":
                                {
                                    Tags.Add("魔法");
                                    break;
                                }
                            case "adventure":
                                {
                                    Tags.Add("冒险");
                                    break;
                                }
                            case "utility":
                                {
                                    Tags.Add("实用");
                                    break;
                                }
                            case "optimization":
                                {
                                    Tags.Add("性能优化");
                                    break;
                                }
                            case "vanilla-like":
                                {
                                    Tags.Add("原版风");
                                    break;
                                }
                            case "realistic":
                                {
                                    Tags.Add("写实风");
                                    break;
                                }
                            // Mod/数据包
                            case "worldgen":
                                {
                                    Tags.Add("世界元素");
                                    break;
                                }
                            case "food":
                                {
                                    Tags.Add("食物/烹饪");
                                    break;
                                }
                            case "game-mechanics":
                                {
                                    Tags.Add("游戏机制");
                                    break;
                                }
                            case "transportation":
                                {
                                    Tags.Add("运输");
                                    break;
                                }
                            case "storage":
                                {
                                    Tags.Add("仓储");
                                    break;
                                }
                            case "decoration":
                                {
                                    if (Types != ResourceTypes.ResourcePack)
                                        Tags.Add("装饰");
                                    break;
                                }
                            case "mobs":
                                {
                                    if (Types != ResourceTypes.ResourcePack)
                                        Tags.Add("生物");
                                    break;
                                }
                            case "equipment":
                                {
                                    if (Types != ResourceTypes.ResourcePack)
                                        Tags.Add("装备");
                                    break;
                                }
                            case "social":
                                {
                                    Tags.Add("服务器");
                                    break;
                                }
                            case "library":
                                {
                                    Tags.Add("支持库");
                                    break;
                                }
                            // 整合包
                            case "multiplayer":
                                {
                                    Tags.Add("多人");
                                    break;
                                }
                            case "challenging":
                                {
                                    Tags.Add("硬核");
                                    break;
                                }
                            case "combat":
                                {
                                    Tags.Add("战斗");
                                    break;
                                }
                            case "quests":
                                {
                                    Tags.Add("任务");
                                    break;
                                }
                            case "kitchen-sink":
                                {
                                    Tags.Add("水槽包");
                                    break;
                                }
                            case "lightweight":
                                {
                                    Tags.Add("轻量");
                                    break;
                                }
                            // 资源包
                            case "simplistic":
                                {
                                    Tags.Add("简洁");
                                    break;
                                }
                            case var @case when @case == "combat":
                                {
                                    Tags.Add("战斗");
                                    break;
                                }
                            case "tweaks":
                                {
                                    Tags.Add("改良");
                                    break;
                                }

                            case "8x-":
                                {
                                    Tags.Add("极简");
                                    break;
                                }
                            case "16x":
                                {
                                    Tags.Add("16x");
                                    break;
                                }
                            case "32x":
                                {
                                    Tags.Add("32x");
                                    break;
                                }
                            case "48x":
                                {
                                    Tags.Add("48x");
                                    break;
                                }
                            case "64x":
                                {
                                    Tags.Add("64x");
                                    break;
                                }
                            case "128x":
                                {
                                    Tags.Add("128x");
                                    break;
                                }
                            case "256x":
                                {
                                    Tags.Add("256x");
                                    break;
                                }
                            case "512x+":
                                {
                                    Tags.Add("超高清");
                                    break;
                                }

                            case "audio":
                                {
                                    Tags.Add("含声音");
                                    break;
                                }
                            case "fonts":
                                {
                                    Tags.Add("含字体");
                                    break;
                                }
                            case "models":
                                {
                                    Tags.Add("含模型");
                                    break;
                                }
                            case "gui":
                                {
                                    Tags.Add("含 UI");
                                    break;
                                }
                            case "locale":
                                {
                                    Tags.Add("含语言");
                                    break;
                                }
                            case "core-shaders":
                                {
                                    Tags.Add("核心着色器");
                                    break;
                                }
                            case "modded":
                                {
                                    Tags.Add("兼容 Mod");
                                    break;
                                }
                            // 光影包
                            case "fantasy":
                                {
                                    Tags.Add("幻想风");
                                    break;
                                }
                            case "semi-realistic":
                                {
                                    Tags.Add("半写实风");
                                    break;
                                }
                            case "cartoon":
                                {
                                    Tags.Add("卡通风");
                                    break;
                                }
                            // 暂时不添加性能负荷 Tag
                            // Case "potato" : Tags.Add("极低")
                            // Case "low" : Tags.Add("低")
                            // Case "medium" : Tags.Add("中")
                            // Case "high" : Tags.Add("高")
                            case "colored-lighting":
                                {
                                    Tags.Add("彩色光照");
                                    break;
                                }
                            case "path-tracing":
                                {
                                    Tags.Add("路径追踪");
                                    break;
                                }
                            case "pbr":
                                {
                                    Tags.Add("PBR");
                                    break;
                                }
                            case "reflections":
                                {
                                    Tags.Add("反射");
                                    break;
                                }

                            case "iris":
                                {
                                    Tags.Add("Iris");
                                    break;
                                }
                            case "optifine":
                                {
                                    Tags.Add("OptiFine");
                                    break;
                                }
                            case "vanilla":
                                {
                                    Tags.Add("原版可用");
                                    break;
                                }
                        }
                    }
                    if (Types == ResourceTypes.DataPack && ModLoaders != ModLoaders.None)
                        Types = ResourceTypes.DataPack | ResourceTypes.Mod;
                    #endregion
                }
                if (!Tags.Any())
                    Tags.Add("其他");
                Tags.Sort();
            }
            // 保存缓存
            Cache[Id] = this;
        }
        /// <summary>
    /// 从工程 ID 联网获取对应工程的信息。
    /// </summary>
        public static ResourceProject FromProjectId(string ProjectId, ResourcePlatforms Platform)
        {
            if (!Platform.Flags().IsSingle())
                throw new ArgumentException($"必须指定单一平台，当前值为 {Platform}");
            if (Cache.ContainsKey(ProjectId)) // 存在本地快查缓存
            {
                return Cache[ProjectId];
            }
            // Phase 4：联网查询、平台判别与 C# 侧缓存统一由 PCL.Download 提供，这里只做投影与本地快查缓存
            var Result = new ResourceProject(Download.ResourceProject.FromProjectId(ProjectId, (Download.ResourcePlatforms)(int)Platform));
            Cache[Result.Id] = Result;
            return Result;
        }
        /// <summary>
    /// 从工程 ID 列表联网获取工程信息，结果不按原顺序排列。
    /// </summary>
        public static IEnumerable<ResourceProject> FromProjectIds(IEnumerable<string> ProjectIds, ResourcePlatforms Platform)
        {
            if (!Platform.Flags().IsSingle())
                throw new ArgumentException($"必须指定单一平台，当前值为 {Platform}");
            // Phase 4：批量查询、平台判别与 C# 侧缓存统一由 PCL.Download 提供，这里只做投影与本地快查缓存
            foreach (var Source in Download.ResourceProject.FromProjectIds(ProjectIds, (Download.ResourcePlatforms)(int)Platform))
            {
                ResourceProject Existing = null;
                if (Cache.TryGetValue(Source.Id, out Existing))
                {
                    yield return Existing;
                }
                else
                {
                    var Projected = new ResourceProject(Source);
                    Cache[Projected.Id] = Projected;
                    yield return Projected;
                }
            }
        }

        /// <summary>
    /// 将当前实例转为可用于保存缓存的 Json。
    /// </summary>
        public JObject ToJson()
        {
            var Json = new JObject();
            Json["Platform"] = (int)Platform;
            Json["Types"] = (int)Types;
            Json["Slug"] = Slug;
            Json["Id"] = Id;
            if (CurseForgeFileIds is not null)
                Json["CurseForgeFileIds"] = new JArray(CurseForgeFileIds);
            Json["RawName"] = RawName;
            Json["Description"] = Description;
            Json["Website"] = Website;
            Json["LastUpdate"] = LastUpdate;
            Json["DownloadCount"] = DownloadCount;
            if (ModLoaders != ModLoaders.None)
                Json["ModLoaders"] = (int)ModLoaders;
            Json["Tags"] = new JArray(Tags);
            if (LogoUrl is not null)
                Json["LogoUrl"] = LogoUrl;
            if (Drops.Any())
                Json["Drops"] = new JArray(Drops);
            Json["CacheTime"] = DateTime.Now; // 用于检查缓存时间
            return Json;
        }

        /// <summary>
    /// 将当前工程信息实例化为控件。
    /// </summary>
        public MyVirtualizingElement<MyResourceItem> ToResourceItem(bool ShowMcVersionDesc, bool ShowLoaderDesc)
        {
            // 获取版本描述
            string GameVersionDescription;
            if (Drops is null || !Drops.Any())
            {
                GameVersionDescription = "仅快照版本"; // #5412
            }
            else
            {
                var Segments = new List<string>();
                bool IsOld = false;
                for (int i = 0, loopTo = Drops.Count - 1; i <= loopTo; i++) // 版本号一定为降序
                {
                    // 获取当前连续的版本号段
                    int StartDrop = Drops[i];
                    int EndDrop = Drops[i];
                    if (StartDrop < 120) // 如果支持新版本，则不显示 1.11-
                    {
                        if (Segments.Any() && !IsOld)
                        {
                            break;
                        }
                        else
                        {
                            IsOld = true;
                        }
                    }
                    for (int ii = i + 1, loopTo1 = Drops.Count - 1; ii <= loopTo1; ii++)
                    {
                        if (ModDownload.AllDrops is null || ModDownload.AllDrops.IndexOf(Drops[ii]) != ModDownload.AllDrops.IndexOf(EndDrop) + 1)
                            break;
                        EndDrop = Drops[ii];
                        i = ii;
                    }
                    // 将版本号段转为描述文本
                    string StartName = McVersion.DropToVersion(StartDrop);
                    string EndName = McVersion.DropToVersion(EndDrop);
                    if (StartDrop == EndDrop)
                    {
                        Segments.Add(StartName);
                    }
                    else if (ModDownload.AllDrops is not null && StartDrop >= ModDownload.AllDrops.First())
                    {
                        if (EndDrop < 120)
                        {
                            Segments.Clear();
                            Segments.Add("全版本");
                            break;
                        }
                        else
                        {
                            Segments.Add(EndName + "+");
                        }
                    }
                    else if (EndDrop < 120)
                    {
                        Segments.Add(StartName + "-");
                        break;
                    }
                    else if (ModDownload.AllDrops is null || ModDownload.AllDrops.IndexOf(EndDrop) - ModDownload.AllDrops.IndexOf(StartDrop) == 1)
                    {
                        Segments.Add(StartName + ", " + EndName);
                    }
                    else
                    {
                        Segments.Add(StartName + "~" + EndName);
                    }
                }
                GameVersionDescription = Segments.Join(", ");
            }
            // 获取 Mod 加载器描述
            string ModLoaderDescriptionFull;
            string ModLoaderDescriptionPart;
            var ModLoadersForDesc = ModLoaders.Flags().ToList();
            switch (ModLoadersForDesc.Count)
            {
                case 0:
                    {
                        if (ModLoadersForDesc.IsSingle())
                        {
                            ModLoaderDescriptionFull = "仅 " + ModLoadersForDesc.Single().ToString();
                            ModLoaderDescriptionPart = ModLoadersForDesc.Single().ToString();
                        }
                        else
                        {
                            ModLoaderDescriptionFull = "未知";
                            ModLoaderDescriptionPart = "";
                        }

                        break;
                    }
                case 1:
                    {
                        ModLoaderDescriptionFull = "仅 " + ModLoadersForDesc.Single().ToString();
                        ModLoaderDescriptionPart = ModLoadersForDesc.Single().ToString();
                        break;
                    }

                default:
                    {
                        int NewestDrop = Drops.Any() ? Drops.First() : 9999;
                        if (ModLoadersForDesc.Contains(ModLoaders.Forge) && (NewestDrop < 140 || ModLoadersForDesc.Contains(ModLoaders.Fabric)) && (NewestDrop < 200 || ModLoadersForDesc.Contains(ModLoaders.NeoForge)))
                        {
                            ModLoaderDescriptionFull = "任意";
                            ModLoaderDescriptionPart = "";
                        }
                        else
                        {
                            ModLoaderDescriptionFull = ModLoadersForDesc.Join(" / ");
                            ModLoaderDescriptionPart = ModLoadersForDesc.Join(" / ");
                        }

                        break;
                    }
            }
            // 实例化 UI
            return new MyVirtualizingElement<MyResourceItem>(() =>
        {
            var NewItem = new MyResourceItem() { Tag = this };
            this.ApplyLogoToMyImage(NewItem.PathLogo);
            var Titles = GetControlTitle(true);
            NewItem.Title = Titles.Title;
            if (string.IsNullOrEmpty(Titles.SubTitle))
            {
                ((StackPanel)NewItem.LabTitleRaw.Parent).Children.Remove(NewItem.LabTitleRaw);
            }
            else
            {
                NewItem.SubTitle = Titles.SubTitle;
            }
            NewItem.Tags = Tags;
            NewItem.Description = Description.ReplaceLineEndings("");
            // 下边栏
            if (!ShowMcVersionDesc && !ShowLoaderDesc)
            {
                // 全部隐藏
                ((Grid)NewItem.PathVersion.Parent).Children.Remove(NewItem.PathVersion);
                ((Grid)NewItem.LabVersion.Parent).Children.Remove(NewItem.LabVersion);
                NewItem.ColumnVersion1.Width = new GridLength(0d);
                NewItem.ColumnVersion2.MaxWidth = 0d;
                NewItem.ColumnVersion3.Width = new GridLength(0d);
            }
            else if (ShowMcVersionDesc && ShowLoaderDesc)
            {
                // 全部显示
                NewItem.LabVersion.Text = (string.IsNullOrEmpty(ModLoaderDescriptionPart) ? "" : ModLoaderDescriptionPart + " ") + GameVersionDescription;
            }
            else if (ShowMcVersionDesc)
            {
                // 仅显示版本
                NewItem.LabVersion.Text = GameVersionDescription;
            }
            else
            {
                // 仅显示 Mod 加载器
                NewItem.LabVersion.Text = ModLoaderDescriptionFull;
            }
            NewItem.LabSource.Text = Platform.ToString();
            NewItem.LabTime.Text = StringUtils.FormatTimeSpan(LastUpdate - DateTime.Now, true);
            NewItem.LabDownload.Text = Conversions.ToString(DownloadCount > 100000000 ? global::System.Math.Round(DownloadCount / 100000000d, 2) + " 亿" : DownloadCount > 100000 ? global::System.Math.Floor(DownloadCount / 10000d) + " 万" : DownloadCount);
            return NewItem;
        })
            {
                Height = 64d
            };
        }
        public void ApplyLogoToMyImage(MyImage Img)
        {
            if (string.IsNullOrEmpty(LogoUrl))
            {
                Img.Source = ModBase.PathImage + "Icons/NoIcon.png";
            }
            else
            {
                Img.Source = LogoUrl;
                Img.FallbackSource = ModDownload.DlSourceModGet(LogoUrl);
            }
        }
        public (string Title, string SubTitle) GetControlTitle(bool HasModLoaderDescription)
        {
            // 检查下列代码时可以参考 #1567 的测试例
            string Title = RawName;
            List<string> SubtitleList;
            if ((TranslatedName ?? "") == (RawName ?? ""))
            {
                // 没有中文翻译
                // 将所有名称分段
                var NameLists = TranslatedName.Split(new[] { " | ", " - ", "(", ")", "[", "]", "{", "}" }, true).Select(s => s.Trim(@" /\".ToCharArray())).Where(w => !string.IsNullOrEmpty(w));
                if (NameLists.IsSingle())
                    return (Title, ""); // VB 原文 GoTo NoSubtitle：该标签位于后面 `If SubtitleList.Any` 的 Else 分支，
                                        // 跳过去只执行 Subtitle = "" 再返回，故等价于直接返回空副标题（此时 Title 仍是 RawName）。
                // 查找其中的缩写、Forge/Fabric 等版本标记
                SubtitleList = new List<string>();
                var NormalNameList = new List<string>();
                foreach (var Name in NameLists)
                {
                    string LowerName = Name.Lower();
                    if ((Name ?? "") == (Name.Upper() ?? "") && Name != "FPS" && Name != "HUD")
                    {
                        // 缩写
                        SubtitleList.Add(Name);
                    }
                    else if (new[] { "neoforge", "forge", "fabric", "quilt" }.Any(l => LowerName.Contains(l)) && !LowerName.Replace("neoforge", "").Replace("forge", "").Replace("fabric", "").Replace("quilt", "").RegexCheck("[a-z]+")) // 去掉关键词后没有其他字母
                    {
                        // Forge/Fabric 等版本标记
                        SubtitleList.Add(Name);
                    }
                    else
                    {
                        // 其他部分
                        NormalNameList.Add(Name);
                    }
                }
                // 根据分类后的结果处理
                if (!NormalNameList.Any() || !SubtitleList.Any())
                    return (Title, ""); // 同上：对应 VB 的 GoTo NoSubtitle（跳过后只剩 Subtitle = "" 并返回）
                // 同时包含 NormalName 和 Subtitle
                Title = NormalNameList.Join(" - ");
            }
            else
            {
                // 有中文翻译
                // 尝试将文本分为三段：Title (EnglishName) - Suffix
                // 检查时注意 Carpet（它没有中文译名，但有 Suffix）和 “汤姆存储 - 星的奇妙优化 (Tom's balabala)”
                Title = TranslatedName.Contains(" (") ? TranslatedName.BeforeFirst(" (") : TranslatedName.BeforeLast(" - ");
                string Suffix = "";
                if (TranslatedName.AfterLast(")").Contains(" - "))
                    Suffix = TranslatedName.AfterLast(")").AfterLast(" - ");
                string EnglishName = TranslatedName;
                if (!string.IsNullOrEmpty(Suffix))
                    EnglishName = EnglishName.Replace(" - " + Suffix, "");
                EnglishName = EnglishName.Replace(Title, "").Trim('(', ')', ' ');
                // 中段的额外信息截取
                SubtitleList = EnglishName.Split(new[] { " | ", " - ", "(", ")", "[", "]", "{", "}" }, true).Select(s => s.Trim(" /".ToCharArray())).Where(w => !string.IsNullOrEmpty(w)).ToList();
                if (SubtitleList.Count > 1 && !SubtitleList.Any(s => s.Lower().Contains("forge") || s.Lower().Contains("fabric") || s.Lower().Contains("quilt")) && !(SubtitleList.Count == 2 && (SubtitleList.Last().Upper() ?? "") == (SubtitleList.Last() ?? ""))) // 不是标注 XX 版
                                                                                                                                                                                                                                                                      // 不是缩写
                {
                    SubtitleList = new List<string>() { EnglishName }; // 使用原名
                }
                // 添加后缀
                if (!string.IsNullOrEmpty(Suffix))
                    SubtitleList.Add(Suffix);
            }
            SubtitleList = SubtitleList.Distinct().ToList();
            // 设置标题与描述
            string Subtitle = "";
            if (SubtitleList.Any())
            {
                foreach (var Ex_Iter in SubtitleList)
                {
                    var Ex = Ex_Iter;
                    bool IsModLoaderDescription = Ex.Lower().Contains("neoforge") || Ex.Lower().Contains("forge") || Ex.Lower().Contains("fabric") || Ex.Lower().Contains("quilt");
                    // 是否显示 ModLoader 信息
                    if (!HasModLoaderDescription && IsModLoaderDescription)
                        continue;
                    // 去除 “Forge/Fabric” 这一无意义提示
                    if (Ex.Length < 16 && Ex.Lower().Contains("fabric") && Ex.Lower().Contains("forge"))
                        continue;
                    // 将 “Forge” 等提示改为 “Forge 版”
                    if (IsModLoaderDescription && !Ex.Contains("版") && Ex.Lower().Replace("neoforge", "").Replace("forge", "").Replace("fabric", "").Replace("quilt", "").Length <= 3)
                    {
                        Ex = Ex.Replace("Edition", "").Replace("edition", "").Trim().Capitalize() + " 版";
                    }
                    // 将 “forge” 等词语的首字母大写
                    Ex = Ex.Replace("neoforge", "NeoForge").Replace("forge", "Forge").Replace("neo", "Neo").Replace("fabric", "Fabric").Replace("quilt", "Quilt");
                    Subtitle += "  |  " + Ex.Trim();
                }
            }
            else
            {
                // VB 原文此处是 NoSubtitle 标签（Subtitle = "" 后返回）；上方的两处 GoTo 已改成等价的 return，
                // 这里保留兜底赋值，保证无副标题时的返回值不变。
                Subtitle = "";
            }
            return (Title, Subtitle);
        }

        /// <summary>
    /// 工程 ID 对应的工程实例的缓存。不会保存到文件。
    /// </summary>
        protected internal static Dictionary<string, ResourceProject> Cache = new Dictionary<string, ResourceProject>();

        /// <summary>
    /// 检查是否与某个 Project 是相同的工程，只是在不同的网站。
    /// </summary>
        public bool IsLike(ResourceProject Project)
        {
            if ((Id ?? "") == (Project.Id ?? ""))
                return true; // 相同实例
                             // 提取字符串中的字母和数字
            string GetRaw(string Data)
            {
                var Result = new StringBuilder();
                foreach (char r in Data.Where(c => char.IsLetterOrDigit(c)))
                    Result.Append(r);
                return Result.ToString().Lower();
            };
            // 来自不同的网站
            if (Platform == Project.Platform)
                return false;
            // Mod 加载器一致
            if (ModLoaders != Project.ModLoaders)
                return false;
            // 若不为光影，则要求 MC 版本一致
            if (Types != ResourceTypes.Shader && Drops.Ordered().SequenceEqual(Project.Drops.Ordered()))
                return false;
            // 最近更新时间差距在一周以内
            if (Math.Abs((LastUpdate - Project.LastUpdate).TotalDays) > 7d)
                return false;
            // MCMOD 翻译名 / 原名 / 描述文本 / Slug 的英文部分相同
            if ((TranslatedName ?? "") == (Project.TranslatedName ?? "") || (RawName ?? "") == (Project.RawName ?? "") || (Description ?? "") == (Project.Description ?? "") || (GetRaw(Slug) ?? "") == (GetRaw(Project.Slug) ?? ""))
            {
                Logger.Info($"将 {RawName} ({Slug}) 与 {Project.RawName} ({Project.Slug}) 认定为相似工程");
                // 如果只有一个有 DatabaseEntry，设置给另外一个
                if (WikiEntry is null && Project.WikiEntry is not null)
                    WikiEntry = Project.WikiEntry;
                if (WikiEntry is not null && Project.WikiEntry is null)
                    Project.WikiEntry = WikiEntry;
                return true;
            }
            return false;
        }

        // 辅助函数
        public override string ToString()
        {
            return $"{Id} ({Slug}): {RawName}";
        }
        public override bool Equals(object obj)
        {
            ResourceProject project = obj as ResourceProject;
            return project is not null && (Id ?? "") == (project.Id ?? "");
        }
        public static bool operator ==(ResourceProject left, ResourceProject right)
        {
            return EqualityComparer<ResourceProject>.Default.Equals(left, right);
        }
        public static bool operator !=(ResourceProject left, ResourceProject right)
        {
            return !(left == right);
        }

    }
}