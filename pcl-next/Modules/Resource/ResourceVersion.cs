using System;
using System.Collections.Generic;
using System.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    /// <summary>
/// 社区资源的单个可下载版本。
/// </summary>
    public class ResourceVersion
    {

        // 基础字段

        /// <summary>
    /// 用于唯一性鉴别该版本的 ID。CurseForge 中为 123456 的大整数，Modrinth 中为英文乱码的 Version 字段。
    /// </summary>
        public string Id;
        /// <summary>
    /// 该版本所对应的单一资源类别。
    /// </summary>
        public ResourceTypes ResourceType;

        // 描述性字段

        /// <summary>
    /// 版本描述名。
    /// 并非文件名或版本号，是由上传者完全自定义的字段。
    /// </summary>
        public string Display;
        /// <summary>
    /// 发布时间。
    /// </summary>
        public DateTime ReleaseDate;
        /// <summary>
    /// 下载量计数。
    /// 注意，仅为一个平台的计数，无法反映多平台加起来的下载量，且 CurseForge 可能错误地返回 0。
    /// </summary>
        public int DownloadCount;
        /// <summary>
    /// 版本号。
    /// 不一定是标准格式。CurseForge 默认为 Nothing。
    /// </summary>
        public string Version;
        /// <summary>
    /// 所有支持的 Mod 加载器列表。可能为 <see cref="ModLoaders.None"/>。
    /// </summary>
        public ModLoaders ModLoaders = ModLoaders.None;
        /// <summary>
    /// 支持的游戏版本列表。类型包括："26.1.5"，"26.1"，"26.1 预览版"，"1.18.5"，"1.18"，"1.18 预览版"，"21w15a"，"未知版本"。
    /// </summary>
        public List<string> GameVersions;

        /// <summary>
    /// 发布状态。
    /// </summary>
        public ReleaseTypes ReleaseType;
        /// <summary>
    /// 发布状态的友好描述。
    /// 例如："正式版"，"Beta 版"。
    /// </summary>
        public string ReleaseTypeDisplay
        {
            get
            {
                switch (ReleaseType)
                {
                    case ReleaseTypes.Release:
                        {
                            return "正式版";
                        }
                    case ReleaseTypes.Beta:
                        {
                            return ModBase.ModeDebug ? "Beta 版" : "测试版";
                        }

                    default:
                        {
                            return ModBase.ModeDebug ? "Alpha 版" : "早期测试版";
                        }
                }
            }
        }
        public enum ReleaseTypes
        {
            // https://docs.curseforge.com/rest-api/#tocS_FileReleaseType
            Release = 1,
            Beta = 2,
            Alpha = 3
        }

        // 下载信息

        /// <summary>
    /// 下载信息是否可用。
    /// </summary>
        public bool DownloadAvailable
        {
            get
            {
                return FileName is not null && DownloadUrls is not null;
            }
        }
        /// <summary>
    /// 文件名。
    /// </summary>
        public string FileName = null;
        /// <summary>
    /// 对应文件的所有可能的下载 URL。
    /// </summary>
        public List<string> DownloadUrls;
        /// <summary>
    /// 对应文件的 SHA1 或 MD5。固定为小写。
    /// </summary>
        public string Hash = null;
        /// <summary>
    /// 文件大小。不可用时为 -1。
    /// </summary>
        public int Size = -1;
        /// <summary>
    /// 作为该版本依赖项的项目 ID。
    /// 它们可能没有加载，在加载后会添加到 Dependencies 中（主要是因为 Modrinth 返回的是字符串 ID 而非 Slug，导致 Project.Id 查询不到）。
    /// </summary>
        private List<string> RawDependencies = new List<string>();
        /// <summary>
    /// 作为该版本依赖项的项目 ID。
    /// </summary>
        public List<string> Dependencies = new List<string>();

        // 初始化

        /// <summary>
    /// 从平台 API 返回的 JSON 中初始化实例。若出错会抛出异常。
    /// </summary>
        public static ResourceVersion FromPlatformJson(JObject Data, ResourceTypes ExpectedResourceType)
        {
            var Result = new ResourceVersion();
            Result.ResourceType = ExpectedResourceType;
            if (Data.ContainsKey("gameId"))
            {
                #region CurseForge
                // 简单信息
                Result.Id = (string)Data["id"];
                Result.Display = Data["displayName"].ToString().Replace("	", "").Trim(' ');
                Result.Version = null;
                Result.ReleaseDate = (DateTime)Data["fileDate"];
                Result.ReleaseType = (ReleaseTypes)Data["releaseType"].ToObject<int>();
                Result.DownloadCount = (int)Data["downloadCount"];
                Result.FileName = (string)Data["fileName"];
                Result.Size = (int)Data["fileLength"];
                if (Result.Size == 0)
                    Result.Size = -1; // CurseForge 可能错误地返回 0 尺寸（#9104）
                Result.Hash = ((JArray)Data["hashes"]).ToList().FirstOrDefault(s => s["algo"].ToObject<int>() == 1)?["value"]?.ToString().Lower();
                if (Result.Hash is null)
                    Result.Hash = ((JArray)Data["hashes"]).ToList().FirstOrDefault(s => s["algo"].ToObject<int>() == 2)?["value"]?.ToString().Lower();
                // DownloadAddress
                string Url = Data["downloadUrl"].ToString();
                if (string.IsNullOrEmpty(Url))
                    Url = $"https://edge.forgecdn.net/files/{Conversions.ToInteger(Result.Id.ToString().Substring(0, 4))}/{Conversions.ToInteger(Result.Id.ToString().Substring(4))}/{Result.FileName}";
                Result.DownloadUrls = ParseCurseForgeDownloadUrls(Url.Replace(Result.FileName, StringUtils.UrlEscape(Result.FileName))); // 对脑残 CurseForge 的下载地址进行多种修正
                Result.DownloadUrls.Add(ModDownload.DlSourceModGet(Url)); // 添加镜像源；注意 MCIM 源不支持 URL 编码后的文件名，必须传入 URL 编码前的文件名
                Result.DownloadUrls = Result.DownloadUrls.Distinct().ToList(); // 最终去重
                // Dependencies
                if (Data.ContainsKey("dependencies"))
                {
                    Result.RawDependencies = Data["dependencies"].Where(d => d["relationType"].ToObject<int>() == 3 && d["modId"].ToObject<int>() != 306612 && d["modId"].ToObject<int>() != 634179).Select(d => d["modId"].ToString()).ToList(); // 种类为依赖
                                                                                                                                                                                                                                                  // 排除 Fabric API 和 Quilt API
                }
                // GameVersions
                var RawVersions = Data["gameVersions"].Select(t => t.ToString().Trim().Lower()).ToList();
                Result.GameVersions = RawVersions.Where(v => McVersion.IsStandardFormat(v)).Select(v => v.Replace("-snapshot", " 预览版")).Distinct().ToList();
                if (Result.GameVersions.IsSingle())
                {
                    Result.GameVersions = Result.GameVersions.ToList();
                }
                else if (Result.GameVersions.Count > 1)
                {
                    Result.GameVersions = Result.GameVersions.SortByComparison(McUtils.CompareVersionGE).ToList();
                    if (Result.ResourceType == ResourceTypes.ModPack)
                        Result.GameVersions = new[] { Result.GameVersions[0] }.ToList(); // 整合包理应只 “支持” 一个版本
                }
                else
                {
                    Result.GameVersions = new List<string>() { "未知版本" };
                }
                // ModLoaders
                Result.ModLoaders = ModLoaders.None;
                foreach (var Loader in EnumUtils.GetAllFlags<ModLoaders>())
                {
                    if (RawVersions.Contains(Loader.ToString().Lower()))
                        Result.ModLoaders = Result.ModLoaders | Loader;
                }
            }
            #endregion
            else
            {
                #region Modrinth
                // 简单信息
                Result.Id = (string)Data["id"];
                Result.Display = Data["name"].ToString().Replace("	", "").Trim(' ');
                Result.Version = (string)Data["version_number"];
                Result.ReleaseDate = (DateTime)Data["date_published"];
                Result.ReleaseType = Data["version_type"].ToString() == "release" ? ReleaseTypes.Release : Data["version_type"].ToString() == "beta" ? ReleaseTypes.Beta : ReleaseTypes.Alpha;
                Result.DownloadCount = (int)Data["downloads"];
                if (((JArray)Data["files"]).Any()) // 可能为空
                {
                    var File = Data["files"][0];
                    Result.FileName = (string)File["filename"];
                    Result.DownloadUrls = new List<string>() { (string)File["url"], ModDownload.DlSourceModGet((string)File["url"]) }.Distinct().ToList(); // 同时添加了镜像源
                    Result.Size = (int)File["size"];
                    Result.Hash = File["hashes"]["sha1"]?.ToString().Lower();
                }
                // 类别与 Loaders
                // 结果可能混杂着 Mod、数据包和服务端插件
                var RawLoaders = Data["loaders"].Select(v => v.ToString()).ToList();
                Result.ModLoaders = ModLoaders.None;
                foreach (ModLoaders Loader in EnumUtils.GetAllFlags<ModLoaders>())
                {
                    if (RawLoaders.Contains(Loader.ToString().Lower()))
                        Result.ModLoaders = Result.ModLoaders | Loader;
                }
                if (Result.ResourceType.HasFlagF(ResourceTypes.Mod) || Result.ResourceType.HasFlagF(ResourceTypes.DataPack))
                {
                    if (RawLoaders.Intersect(new[] { "bukkit", "folia", "paper", "purpur", "spigot" }).Any())
                        Result.ResourceType = ResourceTypes.Plugin; // Veinminer Enchantment 同时支持服务端与 Fabric
                    if (RawLoaders.Contains("datapack"))
                        Result.ResourceType = ResourceTypes.DataPack;
                    if (Result.ModLoaders.Flags().Any())
                        Result.ResourceType = ResourceTypes.Mod;
                }
                else
                {
                    // 使用传入的类别，不作修改（#8377）
                }
                // Dependencies
                if (Data.ContainsKey("dependencies"))
                {
                    Result.RawDependencies = Data["dependencies"].Where(d => (string)d["dependency_type"] == "required" && (string)d["project_id"] != "P7dR8mSH" && (string)d["project_id"] != "qvIfYCYJ" && d["project_id"].ToString().Length > 0).Select(d => d["project_id"].ToString()).ToList(); // 种类为依赖
                                                                                                                                                                                                                                                                                                      // 排除 Fabric API 和 Quilt API
                                                                                                                                                                                                                                                                                                      // 有时候真的会空……
                }
                // GameVersions
                var RawVersions = Data["game_versions"].Select(t => t.ToString().Trim().Lower()).ToList();
                Result.GameVersions = RawVersions.Where(v => v.Contains(".")).Select(v => v.Contains("-") ? v.BeforeFirst("-") + " 预览版" : v.StartsWithF("b1.") ? "远古版本" : v).Distinct().ToList();
                if (Result.GameVersions.IsSingle())
                {
                }
                // 无需处理
                else if (Result.GameVersions.Count > 1)
                {
                    Result.GameVersions = Result.GameVersions.SortByComparison(McUtils.CompareVersionGE).ToList();
                    if (Result.ResourceType == ResourceTypes.ModPack)
                        Result.GameVersions = new List<string>() { Result.GameVersions[0] }; // 整合包理应只 “支持” 一个版本
                }
                else if (RawVersions.Any(v => v.RegexCheck("[0-9]{2}w[0-9]{2}[a-z]")))
                {
                    Result.GameVersions = RawVersions.Where(v => v.RegexCheck("[0-9]{2}w[0-9]{2}[a-z]")).ToList();
                }
                else
                {
                    Result.GameVersions = new List<string>() { "未知版本" };
                }
                #endregion
            }
            return Result;
        }
        /// <summary>
    /// 从项目 ID 获取其下的全部版本列表，并获取其前置项目信息。
    /// </summary>
        public static List<ResourceVersion> FromProjectId(string ProjectId, ResourcePlatforms Platform, bool LoadDependencies = true)
        {
            // Phase 4：枚举、CurseForge 缺失重试、DistinctBy 去重与前置解析均已由 PCL.Download 提供，
            // 本方法只把结果投影成 VB 模型，保证 UI 侧类型与字段不变。
            if (!Platform.Flags().IsSingle())
                throw new ArgumentException($"必须指定单一平台，当前值为 {Platform}");
            var Source = Download.ResourceVersion.FromProjectId(ProjectId, (Download.ResourcePlatforms)(int)Platform, LoadDependencies);
            var Result = new List<ResourceVersion>(Source.Count);
            foreach (var Item in Source)
                Result.Add(new ResourceVersion(Item));
            return Result;
        }

        /// <summary>
    /// 默认构造。定义了带参构造后 VB 不再自动生成，而 JSON 解析路径（FromPlatformJson / FromCacheJson）仍需要它。
    /// </summary>
        public ResourceVersion()
        {
        }

        /// <summary>
    /// 从 C# 侧的版本模型投影（Phase 4）。逐字段构造，不走 JSON 中转，避免丢字段。
    /// </summary>
        internal ResourceVersion(Download.ResourceVersion Source)
        {
            Id = Source.Id;
            ResourceType = (ResourceTypes)(int)Source.ResourceType;
            Display = Source.Display;
            ReleaseDate = Source.ReleaseDate;
            DownloadCount = Source.DownloadCount;
            Version = Source.Version;
            // 字段名与类型名同为 ModLoaders，必须限定命名空间，否则 CType 会把第二个参数解析成字段
            ModLoaders = (ModLoaders)(int)Source.ModLoaders;
            GameVersions = Source.GameVersions;
            ReleaseType = (ReleaseTypes)(int)Source.ReleaseType;
            FileName = Source.FileName;
            DownloadUrls = Source.DownloadUrls;
            Hash = Source.Hash;
            Size = Source.Size;
            Dependencies = Source.Dependencies;
            RawDependencies = Source.DependencyIds;
        }

        /// <summary>
    /// 清空版本列表缓存。C# 侧与 VB 侧各有一份，必须同时清掉，
    /// 否则「刷新」拿到的仍是 C# 侧的旧列表。
    /// </summary>
        public static void ClearCaches()
        {
            ProjectFilesCache.Clear();
            Download.ResourceVersion.ClearCache();
            // 版本枚举内部会查 C# 的工程缓存（取项目的资源类型），工程缓存不一起清就会拿到旧类型
            Download.ResourceProject.ClearCache();
        }
        /// <summary>
    /// 项目 ID 对应的版本信息的缓存。不会保存到文件。
    /// </summary>
        protected internal static Dictionary<string, List<ResourceVersion>> ProjectFilesCache = new Dictionary<string, List<ResourceVersion>>();

        // 序列化

        /// <summary>
    /// 从缓存 JSON 读取字段数据。
    /// </summary>
        public static ResourceVersion FromCacheJson(JObject Data)
        {
            var Result = new ResourceVersion();
            Result.ResourceType = Data["ResourceType"].ToObject<ResourceTypes>();
            Result.Id = Data["Id"].ToString();
            Result.Display = Data["DisplayName"].ToString();
            if (Data.ContainsKey("Version"))
                Result.Version = Data["Version"].ToString();
            Result.ReleaseDate = Data["ReleaseDate"].ToObject<DateTime>();
            Result.DownloadCount = Data["DownloadCount"].ToObject<int>();
            Result.ReleaseType = Data["ReleaseType"].ToObject<ReleaseTypes>();
            if (Data.ContainsKey("FileName"))
                Result.FileName = Data["FileName"].ToString();
            if (Data.ContainsKey("DownloadUrls"))
                Result.DownloadUrls = Data["DownloadUrls"].ToObject<List<string>>();
            if (Data.ContainsKey("ModLoaders"))
                Result.ModLoaders = Data["ModLoaders"].ToObject<ModLoaders>();
            if (Data.ContainsKey("Size"))
                Result.Size = Data["Size"].ToObject<int>();
            if (Data.ContainsKey("Hash"))
                Result.Hash = Data["Hash"].ToString();
            if (Data.ContainsKey("GameVersions"))
                Result.GameVersions = Data["GameVersions"].ToObject<List<string>>();
            if (Data.ContainsKey("RawDependencies"))
                Result.RawDependencies = Data["RawDependencies"].ToObject<List<string>>();
            if (Data.ContainsKey("Dependencies"))
                Result.Dependencies = Data["Dependencies"].ToObject<List<string>>();
            return Result;
        }
        /// <summary>
    /// 转为缓存 JSON。
    /// </summary>
        public JObject ToCacheJson()
        {
            var Result = new JObject();
            Result.Add("ResourceType", (int)ResourceType);
            Result.Add("Id", Id);
            if (Version is not null)
                Result.Add("Version", Version);
            Result.Add("DisplayName", Display);
            Result.Add("ReleaseDate", ReleaseDate);
            Result.Add("DownloadCount", DownloadCount);
            Result.Add("ModLoaders", (int)ModLoaders);
            Result.Add("GameVersions", new JArray(GameVersions));
            Result.Add("ReleaseType", (int)ReleaseType);
            if (FileName is not null)
                Result.Add("FileName", FileName);
            if (DownloadUrls is not null)
                Result.Add("DownloadUrls", new JArray(DownloadUrls));
            if (Hash is not null)
                Result.Add("Hash", Hash);
            if (Size >= 0)
                Result.Add("Size", Size);
            Result.Add("RawDependencies", new JArray(RawDependencies));
            Result.Add("Dependencies", new JArray(Dependencies));
            return Result;
        }

        // 杂项

        /// <summary>
    /// 转为列表项。
    /// </summary>
        public MyVirtualizingElement<MyListItem> ToListItem(MyListItem.ClickEventHandler ClickHandler, MyIconButton.ClickEventHandler SaveAsButtonHandler = null, bool IsBadDisplay = false)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            var Result = new MyListItem();

            // 基础属性
            Result.SnapsToDevicePixels = true;
            Result.Height = 42d;
            Result.Type = MyListItem.CheckType.Clickable;
            Result.Tag = this;
            Result.Title = (IsBadDisplay ? FileName : Display).BeforeLastOfAny(new[] { ".zip", ".jar", ".mrpack", ".litemod" }, true);
            Result.Logo = $"{ModBase.PathImage}ReleaseTypes/{ReleaseType}.png";
            Result.Click += ClickHandler;
            // 描述
            IEnumerable<string> GetInfoLines()
{
                if (ModLoaders != ModLoaders.None)
                    yield return ModLoaders.Flags().Join('、');
                if ((Result.Title ?? "") != (PathUtils.GetFileNameWithoutExtension(FileName) ?? ""))
                    yield return PathUtils.GetFileNameWithoutExtension(FileName);
                if (Dependencies.Any())
                    yield return $"{Dependencies.Count} 项前置";
                if (GameVersions.All(v => !v.Contains(".") || (new[] { "w", "snapshot", "rc", "pre", "experimental", "-" }).Any(s => v.ContainsIgnoreCase(s))))
                    yield return $"游戏版本 {GameVersions.Join('、')}";
                if (DownloadCount > 0)
                    yield return $"下载 {(DownloadCount > 100000 ? Math.Round(DownloadCount / 10000d) + " 万" : DownloadCount.ToString() + " ")}次"; // CurseForge 的下载次数经常错误地返回 0
                yield return $"更新于 {StringUtils.FormatTimeSpan(ReleaseDate - DateTime.Now, false)}";
                if (ReleaseType != ReleaseTypes.Release)
                    yield return ReleaseTypeDisplay;
            }
            Result.Info = GetInfoLines().Join('，');

            // 另存为按钮
            if (SaveAsButtonHandler is not null)
            {
                var BtnSave = new MyIconButton() { Logo = ModBase.Logo.IconButtonSave, ToolTip = "另存为" };
                BtnSave.Click += SaveAsButtonHandler;
                Result.Buttons = new[] { BtnSave };

            }
            return Result;
        })
            {
                Height = 42d
            };
        }
        /// <summary>
    /// 获取下载信息。
    /// </summary>
    /// <param name="LocalFileOrFolder">目标本地文件夹，或完整的文件路径。会自动判断类型。</param>
        public ModNet.NetFile ToNetFile(string LocalFileOrFolder, DownloadReason? TrackDownloadReason = default, string TrackGameVersion = null, ModLoaders TrackLoader = ModLoaders.None)
        {
            return new ModNet.NetFile(ParseModrinthTrackArguments(DownloadUrls, TrackDownloadReason, TrackGameVersion, TrackLoader), LocalFileOrFolder + (LocalFileOrFolder.EndsWithF(@"\") ? FileName : ""), new FileChecker() { Hash = Hash, ActualSize = Size }, SimulateBrowserHeaders: true);
        }
        /// <summary>
    /// 添加 Modrinth 追踪参数。
    /// </summary>
        public static IEnumerable<string> ParseModrinthTrackArguments(List<string> Urls, DownloadReason? TrackDownloadReason = default, string TrackGameVersion = null, ModLoaders TrackLoader = ModLoaders.None)
        {
            var Arguments = new Dictionary<string, string>();
            if (TrackDownloadReason is not null)
                Arguments.Add("mr_download_reason", TrackDownloadReason.ToString().Lower());
            if (TrackGameVersion is not null)
                Arguments.Add("mr_game_version", TrackGameVersion);
            if (TrackLoader.Flags().Any())
                Arguments.Add("mr_loader", TrackLoader.Flags().First().ToString().Lower());
            var Argument = Enumerable.Select((IEnumerable<KeyValuePair<string, string>>)Arguments, a => $"{a.Key}={a.Value}").Join("&");
            foreach (var Url in Urls)
            {
                if (!Url.ContainsIgnoreCase("cdn.modrinth.com/"))
                    yield return Url;
                yield return $"{Url}{(Url.Contains("?") ? "&" : "?")}{Argument}";
            }
        }
        public enum DownloadReason
        {
            Standalone,
            Dependency,
            ModPack,
            Update
        }
        /// <summary>
    /// 重新整理 CurseForge 的下载地址。
    /// </summary>
        public static List<string> ParseCurseForgeDownloadUrls(string Url)
        {
            return new[] { Url.Replace("-service.overwolf.wtf", ".forgecdn.net").Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."), Url.Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."), Url.Replace("-service.overwolf.wtf", ".forgecdn.net"), Url.Replace("://media.", "://edge."), Url }.Distinct().ToList();
        }
        public override string ToString()
        {
            return $"{Id}: {FileName}";
        }

    }
}