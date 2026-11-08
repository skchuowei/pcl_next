using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 社区资源的单个可下载版本（平移自 VB ResourceVersion.vb，行为与 VB 等价）。
/// UI 胶水方法（ToListItem / ToNetFile）不迁入本库，属 Phase 4 桥接层职责。
/// </summary>
public class ResourceVersion
{
    //基础字段

    /// <summary>
    /// 用于唯一性鉴别该版本的 ID。CurseForge 中为 123456 的大整数，Modrinth 中为英文乱码的 Version 字段。
    /// </summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// 该版本所对应的单一资源类别。
    /// </summary>
    public ResourceTypes ResourceType { get; set; }

    //描述性字段

    /// <summary>
    /// 版本描述名。
    /// 并非文件名或版本号，是由上传者完全自定义的字段。
    /// </summary>
    public string Display { get; set; } = "";
    /// <summary>
    /// 发布时间。
    /// </summary>
    public DateTime ReleaseDate { get; set; }
    /// <summary>
    /// 下载量计数。
    /// 注意，仅为一个平台的计数，无法反映多平台加起来的下载量，且 CurseForge 可能错误地返回 0。
    /// </summary>
    public int DownloadCount { get; set; }
    /// <summary>
    /// 版本号。
    /// 不一定是标准格式。CurseForge 默认为 null。
    /// </summary>
    public string? Version { get; set; }
    /// <summary>
    /// 所有支持的 Mod 加载器列表。可能为 <see cref="ModLoaders.None"/>。
    /// </summary>
    public ModLoaders ModLoaders { get; set; } = ModLoaders.None;
    /// <summary>
    /// 支持的游戏版本列表。类型包括："26.1.5"，"26.1"，"26.1 预览版"，"1.18.5"，"1.18"，"1.18 预览版"，"21w15a"，"未知版本"。
    /// </summary>
    public List<string>? GameVersions { get; set; }

    /// <summary>
    /// 发布状态。
    /// </summary>
    public ReleaseTypes ReleaseType { get; set; }
    /// <summary>
    /// 发布状态的友好描述。
    /// 例如："正式版"，"Beta 版"。
    /// </summary>
    public string ReleaseTypeDisplay => ReleaseType switch
    {
        ReleaseTypes.Release => "正式版",
        ReleaseTypes.Beta => ResourceConfig.DebugMode ? "Beta 版" : "测试版",
        _ => ResourceConfig.DebugMode ? "Alpha 版" : "早期测试版",
    };

    /// <summary>发布状态（数值与 CurseForge FileReleaseType 一致）。</summary>
    public enum ReleaseTypes
    {
        //https://docs.curseforge.com/rest-api/#tocS_FileReleaseType
        Release = 1,
        Beta = 2,
        Alpha = 3,
    }

    //下载信息

    /// <summary>
    /// 下载信息是否可用。
    /// </summary>
    public bool DownloadAvailable => FileName is not null && DownloadUrls is not null;
    /// <summary>
    /// 文件名。
    /// </summary>
    public string? FileName { get; set; }
    /// <summary>
    /// 对应文件的所有可能的下载 URL。
    /// </summary>
    public List<string>? DownloadUrls { get; set; }
    /// <summary>
    /// 对应文件的 SHA1 或 MD5。固定为小写。
    /// </summary>
    public string? Hash { get; set; }
    /// <summary>
    /// 文件大小。不可用时为 -1。
    /// </summary>
    public int Size { get; set; } = -1;
    /// <summary>
    /// 作为该版本依赖项的项目 ID。
    /// 它们可能没有加载，在加载后会添加到 Dependencies 中（主要是因为 Modrinth 返回的是字符串 ID 而非 Slug，导致 Project.Id 查询不到）。
    /// </summary>
    private List<string> RawDependencies { get; set; } = [];

    /// <summary>
    /// 依赖项的原始项目 ID（未解析成项目对象之前的值）。
    /// <para>
    /// 仅供宿主做「版本对象 → 缓存 JSON」的序列化（VB 侧 <c>ResourceVersion.ToCacheJson</c> 需要它）；
    /// 业务代码请读 <see cref="Dependencies"/>，那里是已解析的结果。
    /// </para>
    /// </summary>
    public List<string> DependencyIds => RawDependencies;
    /// <summary>
    /// 作为该版本依赖项的项目 ID。
    /// </summary>
    public List<string> Dependencies { get; set; } = [];

    //初始化

    /// <summary>
    /// 从平台 API 返回的 JSON 中初始化实例。若出错会抛出异常。
    /// 与 VB 的差异：镜像源可能缺失的字段（如 logo、categories、hashes）在此按「字段缺失 = 该信息不可用」容错处理，
    /// VB 在这些输入上会直接抛空引用异常；对真实 API 响应两者行为一致。
    /// </summary>
    public static ResourceVersion FromPlatformJson(JsonNode data, ResourceTypes expectedResourceType)
    {
        var result = new ResourceVersion { ResourceType = expectedResourceType };
        if (data.AsObject().ContainsKey("gameId"))
        {
            //CurseForge
            //简单信息
            result.Id = data["id"].AsString() ?? "";
            result.Display = (data["displayName"].AsString() ?? "").Replace("\t", "").Trim(' ');
            result.Version = null;
            result.ReleaseDate = data["fileDate"].AsDate();
            result.ReleaseType = (ReleaseTypes)data["releaseType"].AsInt();
            result.DownloadCount = data["downloadCount"].AsInt();
            result.FileName = data["fileName"].AsString();
            result.Size = data["fileLength"].AsInt();
            if (result.Size == 0) result.Size = -1; //CurseForge 可能错误地返回 0 尺寸（#9104）
            var hashes = data["hashes"].AsArrayOrEmpty();
            result.Hash = hashes.FirstOrDefault(h => h?["algo"].AsInt() == 1)?["value"].AsString()?.ToLowerInvariant();
            if (result.Hash is null)
                result.Hash = hashes.FirstOrDefault(h => h?["algo"].AsInt() == 2)?["value"].AsString()?.ToLowerInvariant();
            //DownloadAddress
            var url = data["downloadUrl"].AsString() ?? "";
            if (url == "")
                url = $"https://edge.forgecdn.net/files/{int.Parse(result.Id[..4], System.Globalization.CultureInfo.InvariantCulture)}/{int.Parse(result.Id[4..], System.Globalization.CultureInfo.InvariantCulture)}/{result.FileName}";
            result.DownloadUrls = ParseCurseForgeDownloadUrls(url.Replace(result.FileName!, Uri.EscapeDataString(result.FileName!))); //对脑残 CurseForge 的下载地址进行多种修正
            result.DownloadUrls.Add(ModMirror.Get(url)); //添加镜像源；注意 MCIM 源不支持 URL 编码后的文件名，必须传入 URL 编码前的文件名
            result.DownloadUrls = result.DownloadUrls.Distinct().ToList(); //最终去重
            //Dependencies
            if (data["dependencies"] is JsonArray dependencies)
            {
                result.RawDependencies = dependencies
                    .Where(d => d?["relationType"].AsInt() == 3 && //种类为依赖
                                d?["modId"].AsInt() != 306612 && d?["modId"].AsInt() != 634179) //排除 Fabric API 和 Quilt API
                    .Select(d => d?["modId"].AsString() ?? "")
                    .ToList();
            }
            //GameVersions
            var rawVersions = data["gameVersions"].AsArrayOrEmpty().Select(t => (t.AsString() ?? "").Trim().ToLowerInvariant()).ToList();
            result.GameVersions = rawVersions
                .Where(McVersionUtil.IsStandardFormat)
                .Select(v => v.Replace("-snapshot", " 预览版"))
                .Distinct().ToList();
            if (result.GameVersions.IsSingle())
            {
                //无需处理
            }
            else if (result.GameVersions.Count > 1)
            {
                result.GameVersions = VbCompat.SortByComparison(result.GameVersions, McVersionUtil.CompareVersionGE);
                if (result.ResourceType == ResourceTypes.ModPack) result.GameVersions = [result.GameVersions[0]]; //整合包理应只 “支持” 一个版本
            }
            else
            {
                result.GameVersions = ["未知版本"];
            }
            //ModLoaders
            result.ModLoaders = ModLoaders.None;
            foreach (var loader in VbCompat.GetAllFlags<ModLoaders>())
                if (rawVersions.Contains(loader.ToString().ToLowerInvariant())) result.ModLoaders |= loader;
        }
        else
        {
            //Modrinth
            //简单信息
            // id 是「版本」的唯一 ID；project_id 是「项目」ID，同一项目下所有版本都相同。
            // 早期实现让 project_id 优先，会使 FromProjectId 的 DistinctBy(Id) 把整份版本列表压成 1 条
            // （实测症状：Fabric API 列表加载成功但 UI 显示「无」）。与 VB 原实现 .Id = Data("id") 对齐。
            result.Id = data["id"].AsString() ?? data["project_id"].AsString() ?? "";
            result.Display = (data["name"].AsString() ?? "").Replace("\t", "").Trim(' ');
            result.Version = data["version_number"].AsString();
            result.ReleaseDate = data["date_published"].AsDate();
            result.ReleaseType = data["version_type"].AsString() == "release" ? ReleaseTypes.Release
                : data["version_type"].AsString() == "beta" ? ReleaseTypes.Beta : ReleaseTypes.Alpha;
            result.DownloadCount = data["downloads"].AsInt();
            var files = data["files"].AsArrayOrEmpty();
            if (files.Count > 0) //可能为空
            {
                var file = files[0]!;
                result.FileName = file["filename"].AsString();
                var fileUrl = file["url"].AsString() ?? "";
                result.DownloadUrls = new List<string> { fileUrl, ModMirror.Get(fileUrl) }.Distinct().ToList(); //同时添加了镜像源
                result.Size = file["size"].AsInt();
                result.Hash = file["hashes"]?["sha1"].AsString()?.ToLowerInvariant();
            }
            //类别与 Loaders
            //结果可能混杂着 Mod、数据包和服务端插件
            var rawLoaders = data["loaders"].AsArrayOrEmpty().Select(v => v.AsString() ?? "").ToList();
            result.ModLoaders = ModLoaders.None;
            foreach (var loader in VbCompat.GetAllFlags<ModLoaders>())
                if (rawLoaders.Contains(loader.ToString().ToLowerInvariant())) result.ModLoaders |= loader;
            if (result.ResourceType.HasFlagF(ResourceTypes.Mod) || result.ResourceType.HasFlagF(ResourceTypes.DataPack))
            {
                if (rawLoaders.Intersect(["bukkit", "folia", "paper", "purpur", "spigot"]).Any()) result.ResourceType = ResourceTypes.Plugin; //Veinminer Enchantment 同时支持服务端与 Fabric
                if (rawLoaders.Contains("datapack")) result.ResourceType = ResourceTypes.DataPack;
                if (result.ModLoaders.FlagsOf().Any()) result.ResourceType = ResourceTypes.Mod;
            }
            else
            {
                //使用传入的类别，不作修改（#8377）
            }
            //Dependencies
            if (data["dependencies"] is JsonArray dependencies)
            {
                result.RawDependencies = dependencies
                    .Where(d => d?["dependency_type"].AsString() == "required" && //种类为依赖
                                d?["project_id"].AsString() != "P7dR8mSH" && d?["project_id"].AsString() != "qvIfYCYJ" && //排除 Fabric API 和 Quilt API
                                (d?["project_id"].AsString() ?? "").Length > 0) //有时候真的会空……
                    .Select(d => d?["project_id"].AsString() ?? "")
                    .ToList();
            }
            //GameVersions
            var rawVersions = data["game_versions"].AsArrayOrEmpty().Select(t => (t.AsString() ?? "").Trim().ToLowerInvariant()).ToList();
            result.GameVersions = rawVersions
                .Where(v => v.Contains('.'))
                .Select(v => v.Contains('-') ? v.BeforeFirst("-") + " 预览版" : v.StartsWith("b1.", StringComparison.Ordinal) ? "远古版本" : v)
                .Distinct().ToList();
            if (result.GameVersions.IsSingle())
            {
                //无需处理
            }
            else if (result.GameVersions.Count > 1)
            {
                result.GameVersions = VbCompat.SortByComparison(result.GameVersions, McVersionUtil.CompareVersionGE);
                if (result.ResourceType == ResourceTypes.ModPack) result.GameVersions = [result.GameVersions[0]]; //整合包理应只 “支持” 一个版本
            }
            else if (rawVersions.Any(v => System.Text.RegularExpressions.Regex.IsMatch(v, "[0-9]{2}w[0-9]{2}[a-z]")))
            {
                result.GameVersions = rawVersions.Where(v => System.Text.RegularExpressions.Regex.IsMatch(v, "[0-9]{2}w[0-9]{2}[a-z]")).ToList();
            }
            else
            {
                result.GameVersions = ["未知版本"];
            }
        }
        return result;
    }

    /// <summary>
    /// 从项目 ID 获取其下的全部版本列表，并获取其前置项目信息。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="platform">平台，必须为单一平台。</param>
    /// <param name="loadDependencies">是否加载前置项目信息。</param>
    /// <param name="api">资源站请求器；null 时使用 <see cref="ResourceApi.Default"/>。</param>
    public static List<ResourceVersion> FromProjectId(string projectId, ResourcePlatforms platform,
        bool loadDependencies = true, IResourceApi? api = null)
    {
        if (platform.FlagsOf().Count() != 1) throw new ArgumentException($"必须指定单一平台，当前值为 {platform}");
        var client = ResourceApi.Resolve(api);
        var targetProject = ResourceProject.FromProjectId(projectId, platform, api);
        //获取项目对象的文件列表
        if (!ProjectFilesCache.ContainsKey(projectId))
        {
            DownloadLog.Info($"开始获取项目版本列表：{projectId}");
            JsonArray resultJsonArray;
            if (platform == ResourcePlatforms.CurseForge)
            {
                resultJsonArray = [];
                for (int retryCount = 0; retryCount <= 2; retryCount++)
                {
                    var resultJson = client.Request($"https://api.curseforge.com/v1/mods/{projectId}/files?pageSize={10000 + retryCount}"); //每次重试多请求一个文件，以避免触发 CDN 缓存
                    //HMCL 一次性请求了 10000 个文件，虽然不知道会不会出问题但先这样吧……（#5522）
                    var pagination = resultJson["pagination"];
                    int resultCount = pagination is null ? 0 : pagination["resultCount"].AsInt();
                    int totalCount = pagination is null ? 0 : pagination["totalCount"].AsInt();
                    if (resultCount == totalCount)
                    {
                        resultJsonArray = resultJson["data"].AsArrayOrEmpty();
                        break;
                    }
                    if (retryCount < 2)
                    {
                        //CurseForge 返回的资源版本列表存在缺失，即将重试（#6224）
                        DownloadLog.Warn($"CurseForge 返回的资源版本列表存在缺失（{resultCount}/{totalCount}），"
                            + $"即将进行第 {retryCount + 1} 次重试");
                    }
                    else
                    {
                        DownloadLog.Warn($"CurseForge 返回的资源版本列表存在缺失（{resultCount}/{totalCount}），已重试 2 次");
                        throw new Exception("CurseForge 返回的资源版本列表存在缺失");
                    }
                }
            }
            else //Modrinth
            {
                resultJsonArray = client.Request($"https://api.modrinth.com/v2/project/{projectId}/version?include_changelog=false").AsArrayOrEmpty();
            }
            ProjectFilesCache[projectId] = resultJsonArray
                .Select(a => FromPlatformJson(a!, targetProject.Types))
                .Where(a => a.DownloadAvailable)
                .DistinctBy(a => a.Id).ToList(); //CurseForge 可能会重复返回相同项（#1330）
        }
        var result = ProjectFilesCache[projectId];
        if (!loadDependencies) return result;
        //加载前置信息
        var dependencies = result.SelectMany(f => f.RawDependencies).Distinct().ToList();
        if (dependencies.Count > 0)
        {
            foreach (var dependencyProject in ResourceProject.FromProjectIds(dependencies, targetProject.Platform, api))
            {
                foreach (var file in result)
                {
                    if (file.RawDependencies.Contains(dependencyProject.Id) && dependencyProject.Id != projectId && !file.Dependencies.Contains(dependencyProject.Id))
                        file.Dependencies.Add(dependencyProject.Id);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 项目 ID 对应的版本信息的缓存。不会保存到文件。
    /// </summary>
    private static readonly Dictionary<string, List<ResourceVersion>> ProjectFilesCache = [];

    /// <summary>清空版本列表内存缓存（测试或需要强制刷新时使用）。</summary>
    public static void ClearCache() => ProjectFilesCache.Clear();

    //序列化

    /// <summary>
    /// 从缓存 JSON 读取字段数据。
    /// </summary>
    public static ResourceVersion FromCacheJson(JsonNode data)
    {
        var result = new ResourceVersion
        {
            ResourceType = (ResourceTypes)data["ResourceType"].AsInt(),
            Id = data["Id"].AsString() ?? "",
            Display = data["DisplayName"].AsString() ?? "",
            ReleaseDate = data["ReleaseDate"].AsDate(),
            DownloadCount = data["DownloadCount"].AsInt(),
            ReleaseType = (ReleaseTypes)data["ReleaseType"].AsInt(),
        };
        if (data.AsObject().ContainsKey("Version")) result.Version = data["Version"].AsString();
        if (data.AsObject().ContainsKey("FileName")) result.FileName = data["FileName"].AsString();
        if (data["DownloadUrls"] is JsonArray downloadUrls) result.DownloadUrls = downloadUrls.Select(u => u.AsString() ?? "").ToList();
        if (data.AsObject().ContainsKey("ModLoaders")) result.ModLoaders = (ModLoaders)data["ModLoaders"].AsInt();
        if (data.AsObject().ContainsKey("Size")) result.Size = data["Size"].AsInt();
        if (data.AsObject().ContainsKey("Hash")) result.Hash = data["Hash"].AsString();
        if (data["GameVersions"] is JsonArray gameVersions) result.GameVersions = gameVersions.Select(v => v.AsString() ?? "").ToList();
        if (data["RawDependencies"] is JsonArray rawDependencies) result.RawDependencies = rawDependencies.Select(v => v.AsString() ?? "").ToList();
        if (data["Dependencies"] is JsonArray dependencies) result.Dependencies = dependencies.Select(v => v.AsString() ?? "").ToList();
        return result;
    }

    /// <summary>
    /// 转为缓存 JSON。
    /// </summary>
    public JsonObject ToCacheJson()
    {
        var result = new JsonObject
        {
            ["ResourceType"] = (int)ResourceType,
            ["Id"] = Id,
        };
        if (Version is not null) result["Version"] = Version;
        result["DisplayName"] = Display;
        result["ReleaseDate"] = JsonValue.Create(ReleaseDate);
        result["DownloadCount"] = DownloadCount;
        result["ModLoaders"] = (int)ModLoaders;
        result["GameVersions"] = new JsonArray((GameVersions ?? []).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        result["ReleaseType"] = (int)ReleaseType;
        if (FileName is not null) result["FileName"] = FileName;
        if (DownloadUrls is not null) result["DownloadUrls"] = new JsonArray(DownloadUrls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
        if (Hash is not null) result["Hash"] = Hash;
        if (Size >= 0) result["Size"] = Size;
        result["RawDependencies"] = new JsonArray(RawDependencies.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        result["Dependencies"] = new JsonArray(Dependencies.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        return result;
    }

    //杂项

    /// <summary>
    /// 添加 Modrinth 追踪参数。
    /// 按 VB 原样平移的两个怪癖（行为等价，不在迁移中修正）：
    /// 1. 非 cdn.modrinth.com 的 URL 会额外产出一份不带参数的副本（VB 的 If 后无 Else）；
    /// 2. 无任何追踪参数时仍会附加 "?" 或 "&amp;"（VB 未判断空参数串）。
    /// </summary>
    public static IEnumerable<string> ParseModrinthTrackArguments(IEnumerable<string> urls,
        DownloadReason? trackDownloadReason = null, string? trackGameVersion = null, ModLoaders trackLoader = ModLoaders.None)
    {
        var arguments = new Dictionary<string, string>();
        if (trackDownloadReason is not null) arguments.Add("mr_download_reason", trackDownloadReason.ToString()!.ToLowerInvariant());
        if (trackGameVersion is not null) arguments.Add("mr_game_version", trackGameVersion);
        if (trackLoader.FlagsOf().Any()) arguments.Add("mr_loader", trackLoader.FlagsOf().First().ToString().ToLowerInvariant());
        var argument = string.Join("&", arguments.Select(a => $"{a.Key}={a.Value}"));
        foreach (var url in urls)
        {
            if (!url.ContainsIgnoreCase("cdn.modrinth.com/")) yield return url;
            yield return $"{url}{(url.Contains('?') ? "&" : "?")}{argument}";
        }
    }

    /// <summary>下载原因（Modrinth 追踪参数用）。</summary>
    public enum DownloadReason
    {
        Standalone,
        Dependency,
        ModPack,
        Update,
    }

    /// <summary>
    /// 重新整理 CurseForge 的下载地址。
    /// </summary>
    public static List<string> ParseCurseForgeDownloadUrls(string url)
    {
        return new List<string>
        {
            url.Replace("-service.overwolf.wtf", ".forgecdn.net").Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."),
            url.Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."),
            url.Replace("-service.overwolf.wtf", ".forgecdn.net"),
            url.Replace("://media.", "://edge."),
            url,
        }.Distinct().ToList();
    }

    /// <inheritdoc />
    public override string ToString() => $"{Id}: {FileName}";
}
