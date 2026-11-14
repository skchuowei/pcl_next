using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Documents;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public static class ModMinecraft
    {

        #region 文件夹

        /// <summary>
    /// 当前的 Minecraft 文件夹路径，以 \ 结尾。
    /// </summary>
        public static string McFolderSelected
        {
            get
            {
                if (_McFolderSelected is null)
                    SetMcFolder(Settings.Get<string>("LaunchFolderSelect")); // 不能使用 McFolderSelected 触发 Set，这会被视为为 Get 设置返回值
                return _McFolderSelected;
            }
            set
            {
                SetMcFolder(value);
            }
        }
        private static void SetMcFolder(string Value) // 等同 McFolderSelected.Set
        {
            if (PathUtils.IsSame(_McFolderSelected, Value.Replace("$", Paths.Base)))
                return;
            _McFolderSelected = Value.Replace("$", Paths.Base);
            _McFolderSelected = PathUtils.AddSlashSuffix(PathUtils.ForCompare(_McFolderSelected));
            Settings.Set("LaunchFolderSelect", Value.Replace(Paths.Base, "$"));
            Logger.Info($"当前选择的 Minecraft 文件夹：{_McFolderSelected}");
        }
        private static string _McFolderSelected;
        /// <summary>
    /// 当前的 Minecraft 文件夹列表。
    /// </summary>
        public static List<McFolder> McFolderList = new List<McFolder>();

        public class McFolder
        {
            public string Name;
            /// <summary>
        /// 文件夹路径。
        /// 以 \ 结尾，例如 "D:\Game\MC\.minecraft\"。
        /// </summary>
            public string Location;
            public Types Type;
            public enum Types
            {
                Vanilla,
                RenamedVanilla,
                Custom
            }

            public override bool Equals(object obj)
            {
                if (!(obj is McFolder))
                    return false;
                McFolder Folder = (McFolder)obj;
                return (Name ?? "") == (Folder.Name ?? "") && PathUtils.IsSame(Location, Folder.Location) && Type == Folder.Type;
            }
            public override string ToString()
            {
                return Location;
            }
        }

        /// <summary>
    /// 加载 Minecraft 文件夹列表。
    /// </summary>
        public static ModLoader.LoaderTask<int, int> McFolderListLoader = new ModLoader.LoaderTask<int, int>("Minecraft Folder List", (_) => McFolderListLoadSub(), Priority: ThreadPriority.AboveNormal);
        private static void McFolderListLoadSub()
        {
            try
            {
                // 初始化
                var CacheMcFolderList = new List<McFolder>();

                #region 读取默认（Original）文件夹，即当前、官启文件夹，可能没有结果

                // 扫描当前文件夹
                try
                {
                    if (DirectoryUtils.Exists(Paths.Base + @"versions\"))
                        CacheMcFolderList.Add(new McFolder() { Name = "当前文件夹", Location = Paths.Base, Type = McFolder.Types.Vanilla });
                    foreach (DirectoryInfo Folder in DirectoryUtils.GetInfo(Paths.Base).GetDirectories())
                    {
                        if (DirectoryUtils.Exists(Folder.FullName + @"versions\") || Folder.Name == ".minecraft")
                        {
                            CacheMcFolderList.Add(new McFolder() { Name = "当前文件夹", Location = PathUtils.AddSlashSuffix(PathUtils.ForCompare(Folder.FullName)), Type = McFolder.Types.Vanilla });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "扫描 PCL Next 所在文件夹中是否有 MC 文件夹失败");
                }

                // 扫描官启文件夹
                string MojangPath = Paths.AppData + @".minecraft\";
                if ((!CacheMcFolderList.Any() || !PathUtils.IsSame(MojangPath, CacheMcFolderList[0].Location)) && DirectoryUtils.Exists(MojangPath + @"versions\")) // 当前文件夹不是官启文件夹
                                                                                                                                                                    // 具有权限且存在 versions 文件夹
                {
                    CacheMcFolderList.Add(new McFolder() { Name = "官方启动器文件夹", Location = MojangPath, Type = McFolder.Types.Vanilla });
                }

                #endregion

                #region 读取自定义（Custom）文件夹，可能没有结果

                // 格式：TMZ 12>C://xxx/xx/|Test>D://xxx/xx/|名称>路径
                foreach (string Folder in Settings.Get<string>("LaunchFolders").ToString().Split('|', true))
                {
                    if (string.IsNullOrEmpty(Folder))
                        continue;
                    if (!Folder.Contains(">") || !Folder.EndsWithF(@"\"))
                    {
                        ModMain.Hint("无效的 Minecraft 文件夹：" + Folder, ModMain.HintType.Red);
                        continue;
                    }
                    string FolderName = Folder.BeforeFirst(">");
                    string FolderPath = Folder.AfterFirst(">");
                    try
                    {
                        ModBase.CheckPermissionWithException(FolderPath);
                        // 若已有该文件夹，则直接重命名；没有则添加
                        bool Renamed = false;
                        foreach (McFolder OriginalFolder in CacheMcFolderList)
                        {
                            if (PathUtils.IsSame(OriginalFolder.Location, FolderPath))
                            {
                                OriginalFolder.Name = FolderName;
                                OriginalFolder.Type = McFolder.Types.RenamedVanilla;
                                Renamed = true;
                            }
                        }
                        if (!Renamed)
                            CacheMcFolderList.Add(new McFolder() { Name = FolderName, Location = PathUtils.AddSlashSuffix(PathUtils.ForCompare(FolderPath)), Type = McFolder.Types.Custom });
                    }
                    catch (Exception ex)
                    {
                        ModMain.MyMsgBox("失效的 Minecraft 文件夹：" + Constants.vbCrLf + FolderPath + Constants.vbCrLf + Constants.vbCrLf + ex.GetDisplay(false), "Minecraft 文件夹失效", IsWarn: true);
                        Logger.Warn(ex, $"无法访问 Minecraft 文件夹 {FolderPath}");
                    }
                }

                // 将自定义文件夹情况同步到设置
                var NewSetup = new List<string>();
                foreach (McFolder Folder in CacheMcFolderList)
                {
                    if (!(Folder.Type == McFolder.Types.Vanilla))
                        NewSetup.Add(Folder.Name + ">" + Folder.Location);
                }
                if (!NewSetup.Any())
                    NewSetup.Add(""); // 防止 0 元素 Join 返回 Nothing
                Settings.Set("LaunchFolders", NewSetup.Join('|'));

                #endregion

                // 若没有可用文件夹，则创建 .minecraft
                if (!CacheMcFolderList.Any())
                {
                    DirectoryUtils.Create(Paths.Base + @".minecraft\versions\");
                    CacheMcFolderList.Add(new McFolder() { Name = "当前文件夹", Location = Paths.Base + @".minecraft\", Type = McFolder.Types.Vanilla });
                }

                foreach (McFolder Folder in CacheMcFolderList)
                    McFolderLauncherProfilesJsonCreate(Folder.Location);
                if (Settings.Get<bool>("SystemDebugDelay"))
                    Thread.Sleep(ModBase.RandomInteger(200, 2000));

                // 回设
                McFolderList = CacheMcFolderList;
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "加载 Minecraft 文件夹列表失败");
            }
        }

        /// <summary>
    /// 为 Minecraft 文件夹创建 launcher_profiles.json 文件。
    /// </summary>
        public static void McFolderLauncherProfilesJsonCreate(string Folder)
        {
            try
            {
                if (FileUtils.Exists(Folder + "launcher_profiles.json"))
                    return;
                string ResultJson = @"{
    ""profiles"":  {
        ""PCL"": {
            ""icon"": ""Grass"",
            ""name"": ""PCL"",
            ""lastVersionId"": ""latest-release"",
            ""type"": ""latest-release"",
            ""lastUsed"": """ + DateTime.Now.ToString("yyyy'-'MM'-'dd") + "T" + DateTime.Now.ToString("HH':'mm':'ss") + @".0000Z""
        }
    },
    ""selectedProfile"": ""PCL"",
    ""clientToken"": ""23323323323323323323323323323333""
}";
                FileUtils.Write(Folder + "launcher_profiles.json", ResultJson, encoding: Encoding.GetEncoding("GB18030"));
                Logger.Info($"已创建 launcher_profiles.json：{Folder}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"创建 launcher_profiles.json 失败（{Folder}）");
            }
        }

        #endregion

        #region 版本处理

        private static McInstance _McInstanceSelected;
        private static object _McInstanceSelected__McInstanceSelectedLast = 0; // 为 0 以保证与 Nothing 不相同，使得 UI 显示可以正常初始化
                                                                               /// <summary>
    /// 当前的 Minecraft 版本。
    /// </summary>
        public static McInstance McInstanceSelected
        {
            get
            {
                return _McInstanceSelected;
            }
            set
            {
                if (ReferenceEquals(_McInstanceSelected__McInstanceSelectedLast, value))
                    return;
                _McInstanceSelected = value; // 由于有可能是 Nothing，导致无法初始化，才得这样弄一圈
                _McInstanceSelected__McInstanceSelectedLast = value;
                // 更新 MC 文件夹所选取的 Minecraft 版本
                Logger.Info($"当前选择的 Minecraft 版本：{(value?.PathVersion) ?? "无"}");
                ModBase.WriteIni(McFolderSelected + "PCL.ini", "Version", (value?.Name) ?? "");
                if (value is null)
                    return;
                // 重置缓存的下载文件夹
                PageDownloadResourceDetail.CachedFolder.Clear();
                // 统一通行证重判
                if (ModAnimation.AniControlEnabled == 0 && (Settings.Get<string>("VersionServerNide", Instance: value) ?? "") != (Settings.Get<string>("CacheNideServer") ?? "") && Settings.Get<int>("VersionServerLogin", Instance: value) == 3)
                {
                    Settings.Set("CacheNideAccess", "");
                    Logger.Info("服务器改变，要求重新登录统一通行证");
                }
                if (Settings.Get<int>("VersionServerLogin", Instance: value) == 3)
                {
                    Settings.Set("CacheNideServer", Settings.Get<string>("VersionServerNide", Instance: value));
                }
                // Authlib-Injector 重判
                if (ModAnimation.AniControlEnabled == 0 && (Settings.Get<string>("VersionServerAuthServer", Instance: value) ?? "") != (Settings.Get<string>("CacheAuthServerServer") ?? "") && Settings.Get<int>("VersionServerLogin", Instance: value) == 4)
                {
                    Settings.Set("CacheAuthAccess", "");
                    Logger.Info("服务器改变，要求重新登录 Authlib-Injector");
                }
                if (Settings.Get<int>("VersionServerLogin", Instance: value) == 4)
                {
                    Settings.Set("CacheAuthServerServer", Settings.Get<string>("VersionServerAuthServer", Instance: value));
                    Settings.Set("CacheAuthServerName", Settings.Get<string>("VersionServerAuthName", Instance: value));
                    Settings.Set("CacheAuthServerRegister", Settings.Get<string>("VersionServerAuthRegister", Instance: value));
                }
            }
        }
        private static bool _JsonVersion_JsonVersionInited = false;

        public class McInstance
        {

            public enum McInstanceState
            {
                Error,
                Original,
                Snapshot,
                Fool,
                OptiFine,
                Old,
                Forge,
                NeoForge,
                LiteLoader,
                Fabric
            }

            /// <summary>
        /// 版本文件夹路径，以 \ 结尾。
        /// </summary>
            public string PathVersion { get; private set; }
            /// <summary>
        /// 应用版本隔离后，该版本所对应的 Minecraft 根文件夹，以 \ 结尾。
        /// </summary>
            public string PathIndie
            {
                get
                {
                    if (!Settings.HasSaved("VersionArgumentIndieV2", Instance: this))
                    {
                        if (!IsLoaded)
                            Load();
                        // 决定该版本是否应该被隔离
                        bool ShouldBeIndie()
                        {
                            // 从老的版本独立设置中迁移：-1 未决定，0 使用全局设置，1 手动开启，2 手动关闭
                            if (Settings.HasSaved("VersionArgumentIndie", Instance: this) && Settings.Get<int>("VersionArgumentIndie", Instance: this) > 0)
                            {
                                Logger.Info($"版本隔离初始化（{Name}）：从老的版本独立设置中迁移");
                                return Settings.Get<int>("VersionArgumentIndie", Instance: this) == 1;
                            }
                            // 若版本文件夹下包含 mods 或 saves 文件夹，则自动开启版本隔离
                            var ModFolder = DirectoryUtils.GetInfo(PathVersion + @"mods\");
                            var SaveFolder = DirectoryUtils.GetInfo(PathVersion + @"saves\");
                            if (ModFolder.Exists && ModFolder.EnumerateFiles().Any() || SaveFolder.Exists && SaveFolder.EnumerateDirectories().Any())
                            {
                                Logger.Info($"版本隔离初始化（{Name}）：版本文件夹下存在 mods 或 saves 文件夹，自动开启");
                                return true;
                            }
                            // 根据全局的默认设置决定是否隔离
                            bool IsRelease = State != McInstanceState.Fool && State != McInstanceState.Old && State != McInstanceState.Snapshot;
                            Logger.Info($"版本隔离初始化（{Name}）：从全局默认设置中（{Settings.Get<int>("LaunchArgumentIndieV2")}）判断，State {State}，IsRelease {IsRelease}，Modable {Modable}");
                            switch (Settings.Get<int>("LaunchArgumentIndieV2"))
                            {
                                case 0: // 关闭
                                    {
                                        return false;
                                    }
                                case 1: // 仅隔离可安装 Mod 的版本
                                    {
                                        return Modable;
                                    }
                                case 2: // 仅隔离非正式版
                                    {
                                        return !IsRelease;
                                    }
                                case 3: // 隔离非正式版与可安装 Mod 的版本
                                    {
                                        return !IsRelease || Modable; // 隔离所有版本
                                    }

                                default:
                                    {
                                        return true;
                                    }
                            }
                        };
                        Settings.Set("VersionArgumentIndieV2", ShouldBeIndie(), Instance: this);
                    }
                    return Settings.Get<bool>("VersionArgumentIndieV2", Instance: this) ? PathVersion : McFolderSelected;
                }
            }

            /// <summary>
        /// 该版本的版本文件夹名称。
        /// </summary>
            public string Name
            {
                get
                {
                    if (_Name is null && !string.IsNullOrEmpty(PathVersion))
                        _Name = PathUtils.GetLastPart(PathVersion);
                    return _Name;
                }
            }
            private string _Name = null;

            /// <summary>
        /// 显示的描述文本。
        /// </summary>
            public string Info = "该版本未被加载，请向作者反馈此问题";

            /// <summary>
        /// 该版本的列表检查原始结果，不受自定义影响。
        /// </summary>
            public McInstanceState State = McInstanceState.Error;
            /// <summary>
        /// 显示的版本图标。
        /// </summary>
            public string Logo;
            /// <summary>
        /// 是否为收藏的版本。
        /// </summary>
            public bool IsStar = false;
            /// <summary>
        /// 强制版本分类，0 为未启用，1 为隐藏，2 及以上为其他普通分类。
        /// </summary>
            public McInstanceCardType DisplayType = McInstanceCardType.Auto;
            /// <summary>
        /// 该版本是否可以安装 Mod。
        /// </summary>
            public bool Modable
            {
                get
                {
                    if (!IsLoaded)
                        Load();
                    return Version.ModLoaders.Flags().Any() || DisplayType == McInstanceCardType.API; // #223
                }
            }
            /// <summary>
        /// 版本信息。
        /// </summary>
            public McVersion Version
            {
                get
                {
                    if (_Version is not null)
                        return _Version;
                    try
                    {
                        var Result = new McVersion(Name, JsonObject, JsonVersion);
                        _Version = Result;
                        if (Result.VanillaName == "Unknown")
                            Info = "PCL Next 无法识别该版本的 MC 版本号";
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "识别 Minecraft 版本时出错");
                        _Version = new McVersion("Unknown");
                        Info = "无法识别：" + ex.Message;
                    }
                    return _Version;
                }
                set
                {
                    _Version = value;
                }
            }
            private McVersion _Version = null;

            /// <summary>
        /// 版本的发布时间。
        /// </summary>
            public DateTime ReleaseTime
            {
                get
                {
                    return Version.ReleaseTime;
                }
            }

            /// <summary>
        /// 获取该版本的 JSON 路径。
        /// </summary>
            public string GetJsonPath()
            {
                string JsonPath = PathVersion + Name + ".json";
                if (!FileUtils.Exists(JsonPath))
                {
                    // 尝试寻找 JSON 文件
                    JsonPath = null;
                    foreach (var JsonCandidatePath in DirectoryUtils.EnumerateFiles(PathVersion, searchPattern: "*.json"))
                    {
                        try
                        {
                            JObject JsonCandidate = (JObject)FileUtils.ReadAsJson(JsonCandidatePath);
                            if (!JsonCandidate.ContainsKey("mainClass"))
                                continue;
                            if (!JsonCandidate.ContainsKey("type"))
                                continue;
                            if (!JsonCandidate.ContainsKey("id"))
                                continue;
                        }
                        catch
                        {
                            continue;
                        }
                        JsonPath = JsonCandidatePath;
                        break;
                    }
                    if (JsonPath is null)
                    {
                        throw new Exception($"未找到版本 JSON 文件：{PathVersion}{Name}.json");
                    }
                    else
                    {
                        Logger.Warn($"未找到同名版本 JSON，自动换用 {JsonPath}");
                    }
                }
                return JsonPath;
            }
            /// <summary>
        /// 该版本的 Json 文本。会抛出异常。
        /// </summary>
            public string JsonText
            {
                get
                {
                    // 快速检查 JSON 是否以 { 开头、} 结尾；忽略空白字符
                    bool FastJsonCheck(string Json)
                    {
                        string TrimedJson = Json.Trim();
                        return TrimedJson.StartsWithF("{") && TrimedJson.EndsWithF("}");
                    };
                    if (_JsonText is null)
                    {
                        string JsonPath = GetJsonPath();
                        _JsonText = FileUtils.TryReadAsString(JsonPath) ?? "";
                        // 如果 ReadFile 失败会返回空字符串；这可能是由于文件被临时占用，故延时后重试
                        if (!FastJsonCheck(_JsonText))
                        {
                            if (ModBase.RunInUi())
                            {
                                Logger.Warn("版本 JSON 文件为空或有误，由于代码在主线程运行，将不再进行重试");
                                _JsonText.DeserializeJson(); // 触发异常
                            }
                            else
                            {
                                Logger.Warn($"版本 JSON 文件为空或有误，将在 2s 后重试读取（{JsonPath}）");
                                Thread.Sleep(2000);
                                _JsonText = FileUtils.ReadAsString(JsonPath);
                                if (!FastJsonCheck(_JsonText))
                                    _JsonText.DeserializeJson();
                            } // 触发异常
                        }
                    }
                    return _JsonText;
                }
                set
                {
                    _JsonText = value;
                }
            }
            private string _JsonText = null;
            /// <summary>
        /// 该版本的 Json 对象。
        /// 若 Json 存在问题，在获取该属性时即会抛出异常。
        /// </summary>
            public JObject JsonObject
            {
                get
                {
                    if (_JsonObject is null)
                    {
                        string Text = JsonText; // 触发 JsonText 的 Get 事件
                        try
                        {
                            _JsonObject = (JObject)Text.DeserializeJson();
                            // 转换 HMCL 关键项
                            if (_JsonObject.ContainsKey("patches") && !_JsonObject.ContainsKey("time"))
                            {
                                IsHmclFormatJson = true;
                                // 合并 Json
                                // Dim HasOptiFine As Boolean = False, HasForge As Boolean = False
                                JObject CurrentObject = null;
                                var SubjsonList = new List<JObject>();
                                foreach (JObject Subjson in _JsonObject["patches"])
                                    SubjsonList.Add(Subjson);
                                SubjsonList = SubjsonList.SortByComparison((Left, Right) => ModBase.Val((Left["priority"] ?? "0").ToString()) < ModBase.Val((Right["priority"] ?? "0").ToString()));
                                foreach (JObject Subjson in SubjsonList)
                                {
                                    string Id = (string)Subjson["id"];
                                    if (Id is not null)
                                    {
                                        // 合并 Json
                                        Logger.Info($"合并 HMCL 分支项：{Id}");
                                        if (CurrentObject is not null)
                                        {
                                            CurrentObject.Merge(Subjson);
                                        }
                                        else
                                        {
                                            CurrentObject = Subjson;
                                        }
                                    }
                                    else
                                    {
                                        Logger.Info("存在为空的 HMCL 分支项");
                                    }
                                }
                                _JsonObject = CurrentObject;
                                // 修改附加项
                                _JsonObject["id"] = Name;
                                if (_JsonObject.ContainsKey("inheritsFrom"))
                                    _JsonObject.Remove("inheritsFrom");
                            }
                            // 与继承版本合并
                            object InstanceNameInheritFrom = null;
                            do
                            {
                                try
                                {
                                    InstanceNameInheritFrom = _JsonObject["inheritsFrom"] is null ? "" : _JsonObject["inheritsFrom"].ToString();
                                    if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(InstanceNameInheritFrom, Name, false)))
                                    {
                                        Logger.Warn($"自引用的继承版本：{Name}");
                                        InstanceNameInheritFrom = "";
                                        break;
                                    }

                                Recheck:
                                    ;

                                    if (Conversions.ToBoolean(Operators.ConditionalCompareObjectNotEqual(InstanceNameInheritFrom, "", false)))
                                    {
                                        var InheritInstance = new McInstance(Conversions.ToString(InstanceNameInheritFrom));
                                        // 继续循环
                                        if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(InheritInstance.InheritName, InstanceNameInheritFrom, false)))
                                            throw new Exception(Conversions.ToString(Operators.ConcatenateObject("版本依赖项出现嵌套：", InstanceNameInheritFrom)));
                                        InstanceNameInheritFrom = InheritInstance.InheritName;
                                        // 合并 Libraries 项：子版本放在前面，父版本放在后面（5978#：如果多个 jar 包中含有相同的类，Java 8 和之前的版本按照 -cp 指定的顺序选择第一个）
                                        JArray CurrentLib = (JArray)_JsonObject["libraries"].DeepClone();
                                        foreach (var LibToken in InheritInstance.JsonObject["libraries"])
                                            CurrentLib.Add(LibToken);
                                        // 合并其他项：子版本优先于父版本
                                        InheritInstance.JsonObject.Merge(_JsonObject);
                                        _JsonObject = InheritInstance.JsonObject;
                                        _JsonObject["libraries"] = CurrentLib;
                                        goto Recheck;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warn(ex, $"合并版本依赖项 JSON 失败（{InstanceNameInheritFrom ?? "null"}）");
                                }
                            }
                            while (false);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("初始化版本 JSON 时失败（" + (Name ?? "null") + "）", ex);
                        }
                    }
                    return _JsonObject;
                }
                set
                {
                    _JsonObject = value;
                }
            }
            private JObject _JsonObject = null;
            /// <summary>
        /// 是否为旧版 Json 格式。
        /// </summary>
            public bool IsOldJson
            {
                get
                {
                    return JsonObject["minecraftArguments"] is not null && (string)JsonObject["minecraftArguments"] != "";
                }
            }
            /// <summary>
        /// Json 是否为 HMCL 格式。
        /// </summary>
            public bool IsHmclFormatJson { get; set; } = false;

            /// <summary>
        /// 版本 jar 中的 version.json 文件对象。
        /// 若没有则返回 Nothing。
        /// </summary>
            public JObject JsonVersion
            {
                get
                {
                    if (!_JsonVersion_JsonVersionInited)
                    {
                        _JsonVersion_JsonVersionInited = true;
                        do
                        {
                            try
                            {
                                if (!FileUtils.Exists($"{PathVersion}{Name}.jar"))
                                    break;
                                using (var JarArchive = FileUtils.OpenZip(PathVersion + Name + ".jar"))
                                {
                                    var VersionJson = JarArchive.GetEntry("version.json");
                                    if (VersionJson is null)
                                        break;
                                    using (var VersionJsonStream = new StreamReader(VersionJson.Open()))
                                    {
                                        _JsonVersion = (JObject)VersionJsonStream.ReadToEnd().DeserializeJson();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn(ex, $"从版本 jar 中读取 version.json 失败（{PathVersion}{Name}.jar）");
                            }
                        }
                        while (false);
                    }
                    return _JsonVersion;
                }
            }
            private JObject _JsonVersion = null;

            /// <summary>
        /// 该版本的依赖版本。若无依赖版本则为空字符串。
        /// </summary>
            public string InheritName
            {
                get
                {
                    if (_InheritName is null)
                    {
                        _InheritName = (JsonObject["inheritsFrom"] ?? "").ToString();
                        // 由于过老的 LiteLoader 中没有 Inherits（例如 1.5.2），需要手动判断以获取真实继承版本
                        // 此外，由于这里的加载早于版本种类判断，所以需要手动判断是否为 LiteLoader
                        // 如果版本提供了不同的 Jar，代表所需的 Jar 可能已被更改，则跳过 Inherit 替换
                        if (JsonText.Contains("liteloader") && (Version.VanillaName ?? "") != (Name ?? "") && !JsonText.Contains("logging"))
                        {
                            if (((JsonObject["jar"] ?? Version.VanillaName).ToString() ?? "") == (Version.VanillaName ?? ""))
                                _InheritName = Version.VanillaName;
                        }
                        // HMCL 版本无 Json
                        if (IsHmclFormatJson)
                            _InheritName = "";
                    }
                    return _InheritName;
                }
            }
            private string _InheritName = null;

            public JsonConfigProvider Config;
            public void ResetSettingsCache()
            {
                ModBase.IniClearCache(PathIndie + "options.txt");
                ModBase.IniClearCache(PathVersion + @"PCL\Setup.ini");
                Config.DiscardCache();
                foreach (var Entry in Settings.Entries.Values)
                {
                    if (Entry.Source == Settings.Sources.Instance)
                        Entry.ValueCache.Remove(PathVersion);
                }
            }

            /// <summary>
        /// 从版本名，或版本文件夹的完整路径初始化。
        /// </summary>
            public McInstance(string NameOrFullPath)
            {
                NameOrFullPath = PathUtils.RemoveExtendedPrefix(NameOrFullPath);
                PathVersion = PathUtils.AddSlashSuffix((NameOrFullPath.Contains(":") ? "" : McFolderSelected + @"versions\") + NameOrFullPath);
                Config = new JsonConfigProvider(Path.Combine(PathVersion, @"PCL\config.json"));
            }

            /// <summary>
        /// 检查 Minecraft 版本，若检查通过 State 则为 Original 且返回 True。
        /// </summary>
            public bool Check()
            {

                // 检查文件夹
                if (!DirectoryUtils.Exists(PathVersion))
                {
                    State = McInstanceState.Error;
                    Info = "未找到版本 " + Name;
                    return false;
                }
                // 确认 Json 可用性
                try
                {
                    if (JsonObject["mainClass"] is null)
                        throw new Exception("版本 JSON 中没有 mainClass 项！"); // 这里也会触发 JsonObject 的 Get 事件
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"版本 JSON 可用性检查失败（{PathVersion}）");
                    JsonText = "";
                    JsonObject = null;
                    Info = ex.Message;
                    State = McInstanceState.Error;
                    return false;
                }
                // 检查版本号获取
                try
                {
                    if (string.IsNullOrEmpty(Version.VanillaName))
                        throw new Exception("无法获取版本号，结果为空");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"版本号获取失败（{Name}）");
                    State = McInstanceState.Error;
                    Info = "版本号获取失败：" + ex.GetDisplay(false);
                    return false;
                }
                // 检查依赖版本
                try
                {
                    if (!string.IsNullOrEmpty(InheritName))
                    {
                        if (!FileUtils.Exists($@"{PathUtils.RemoveLastPart(PathVersion)}\{InheritName}\{InheritName}.json"))
                        {
                            State = McInstanceState.Error;
                            Info = "需要安装 " + InheritName + " 作为前置版本";
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"依赖版本检查出错（{Name}）");
                    State = McInstanceState.Error;
                    Info = "未知错误：" + ex.GetDisplay(false);
                    return false;
                }

                State = McInstanceState.Original;
                return true;
            }
            /// <summary>
        /// 加载 Minecraft 版本的详细信息。不使用其缓存，且会更新缓存。
        /// </summary>
            public McInstance Load()
            {
                try
                {
                    // 检查版本，若出错则跳过数据确定阶段
                    if (!Check())
                        goto ExitDataLoad;
                    #region 确定版本分类
                    switch (Version.VanillaName ?? "") // 在获取 Version.Original 对象时会完成它的加载
                    {
                        case "Unknown":
                            {
                                State = McInstanceState.Error;
                                break;
                            }
                        case "Old":
                            {
                                State = McInstanceState.Old;
                                break;
                            }

                        default:
                            {
                                // 愚人节与快照版本
                                if (Version.IsFool)
                                {
                                    State = McInstanceState.Fool;
                                }
                                else if (Version.IsSnapshot)
                                {
                                    State = McInstanceState.Snapshot;
                                }
                                // OptiFine
                                if (Version.HasOptiFine)
                                {
                                    State = McInstanceState.OptiFine;
                                }
                                // LiteLoader
                                if (Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader))
                                {
                                    State = McInstanceState.LiteLoader;
                                }
                                // Fabric、Forge、NeoForge
                                if (Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
                                {
                                    State = McInstanceState.Fabric;
                                }
                                else if (Version.ModLoaders.HasFlagF(ModLoaders.Forge))
                                {
                                    State = McInstanceState.Forge;
                                }
                                else if (Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
                                {
                                    State = McInstanceState.NeoForge;
                                }

                                break;
                            }
                    }
                #endregion
                ExitDataLoad:
                    ;

                    // 确定版本图标
                    Logo = ModBase.ReadIni(PathVersion + @"PCL\Setup.ini", "Logo", "");
                    if (string.IsNullOrEmpty(Logo) || !Conversions.ToBoolean(ModBase.ReadIni(PathVersion + @"PCL\Setup.ini", "LogoCustom", Conversions.ToString(false))))
                    {
                        switch (State)
                        {
                            case McInstanceState.Original:
                                {
                                    Logo = ModBase.PathImage + "Blocks/Grass.png";
                                    break;
                                }
                            case McInstanceState.Snapshot:
                                {
                                    Logo = ModBase.PathImage + "Blocks/CommandBlock.png";
                                    break;
                                }
                            case McInstanceState.Old:
                                {
                                    Logo = ModBase.PathImage + "Blocks/CobbleStone.png";
                                    break;
                                }
                            case McInstanceState.Forge:
                                {
                                    Logo = ModBase.PathImage + "Blocks/Anvil.png";
                                    break;
                                }
                            case McInstanceState.NeoForge:
                                {
                                    Logo = ModBase.PathImage + "Blocks/NeoForge.png";
                                    break;
                                }
                            case McInstanceState.Fabric:
                                {
                                    Logo = ModBase.PathImage + "Blocks/Fabric.png";
                                    break;
                                }
                            case McInstanceState.OptiFine:
                                {
                                    Logo = ModBase.PathImage + "Blocks/GrassPath.png";
                                    break;
                                }
                            case McInstanceState.LiteLoader:
                                {
                                    Logo = ModBase.PathImage + "Blocks/Egg.png";
                                    break;
                                }
                            case McInstanceState.Fool:
                                {
                                    Logo = ModBase.PathImage + "Blocks/GoldBlock.png";
                                    break;
                                }

                            default:
                                {
                                    Logo = ModBase.PathImage + "Blocks/RedstoneBlock.png";
                                    break;
                                }
                        }
                    }
                    // 确定版本描述
                    RefreshInfo();
                    // 确定版本收藏状态
                    IsStar = Conversions.ToBoolean(ModBase.ReadIni(PathVersion + @"PCL\Setup.ini", "IsStar", Conversions.ToString(false)));
                    // 确定版本显示种类
                    DisplayType = (McInstanceCardType)Conversions.ToInteger(ModBase.ReadIni(PathVersion + @"PCL\Setup.ini", "DisplayType", ((int)McInstanceCardType.Auto).ToString()));
                    // 写入缓存
                    if (DirectoryUtils.Exists(PathVersion))
                    {
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "State", ((int)State).ToString());
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "Info", Info);
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "Logo", Logo);
                    }
                    if (State != McInstanceState.Error && Version.Reliable)
                    {
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "ReleaseTime", ReleaseTime.ToString("yyyy'-'MM'-'dd HH':'mm"));
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "VersionFabric", Version.ModLoaders.HasFlagF(ModLoaders.Fabric) ? Version.ModLoaderVersions[ModLoaders.Fabric] : null);
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "VersionOptiFine", Version.OptiFineAsMod ? null : Version.OptiFine);
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "VersionLiteLoader", Conversions.ToString(Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)));
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "VersionForge", Version.ModLoaders.HasFlagF(ModLoaders.Forge) ? Version.ModLoaderVersions[ModLoaders.Forge] : null);
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "VersionNeoForge", Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) ? Version.ModLoaderVersions[ModLoaders.NeoForge] : null);
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "VersionVanillaName", Version.VanillaName);
                        ModBase.WriteIni(PathVersion + @"PCL\Setup.ini", "VersionVanilla", Version.Vanilla.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Info = ex.GetDisplay(false);
                    Logo = ModBase.PathImage + "Blocks/RedstoneBlock.png";
                    State = McInstanceState.Error;
                    Logger.Warn(ex, $"加载版本失败（{Name}）");
                }
                finally
                {
                    IsLoaded = true;
                }
                return this;
            }
            public bool IsLoaded = false;
            /// <summary>
        /// 根据当前组件信息刷新版本描述。
        /// </summary>
            public void RefreshInfo()
            {
                string CustomInfo = ModBase.ReadIni(PathVersion + @"PCL\Setup.ini", "CustomInfo");
                if (!string.IsNullOrEmpty(CustomInfo))
                {
                    Info = CustomInfo;
                }
                else
                {
                    Info = VersionDisplayName();
                    if (State == McInstanceState.Fool)
                    {
                        Info = GetMcFoolName(Version.VanillaName);
                    }
                    else if (State != McInstanceState.Error)
                    {
                        if (Settings.Get<int>("VersionServerLogin", Instance: this) == 3)
                            Info += ", 统一通行证验证";
                        if (Settings.Get<int>("VersionServerLogin", Instance: this) == 4)
                            Info += ", Authlib 验证";
                    }
                }
            }
            /// <summary>
        /// 获取对版本信息的简短描述。
        /// 例如 “快照 16w01a”、“原版 1.12.2, Forge 1.2.3”、“愚人节版本 2.0”。
        /// </summary>
            public string VersionDisplayName()
            {
                // Mod Loader 信息
                string ModLoaderInfo = "";
                if (Version.ModLoaders.HasFlagF(ModLoaders.Forge))
                    ModLoaderInfo += ", Forge" + (Version.ModLoaderVersions[ModLoaders.Forge] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.Forge]);
                if (Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
                    ModLoaderInfo += ", NeoForge" + (Version.ModLoaderVersions[ModLoaders.NeoForge] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.NeoForge]);
                if (Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
                    ModLoaderInfo += ", Fabric" + (Version.ModLoaderVersions[ModLoaders.Fabric] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.Fabric]);
                if (Version.HasOptiFine)
                    ModLoaderInfo += ", OptiFine" + (Version.OptiFine == "未知版本" ? "" : " " + Version.OptiFine.Replace("-", " ").Replace("_", " "));
                if (Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader))
                    ModLoaderInfo += ", LiteLoader";
                // 基础信息
                string Info;
                switch (State)
                {
                    case McInstanceState.Snapshot:
                    case McInstanceState.Original:
                    case McInstanceState.Forge:
                    case McInstanceState.NeoForge:
                    case McInstanceState.Fabric:
                    case McInstanceState.OptiFine:
                    case McInstanceState.LiteLoader:
                        {
                            if (Version.VanillaName.ContainsIgnoreCase("pre"))
                            {
                                Info = "预发布版 " + Version.VanillaName;
                            }
                            else if (Version.VanillaName.ContainsIgnoreCase("rc"))
                            {
                                Info = "发布候选 " + Version.VanillaName;
                            }
                            else if (Version.VanillaName.ContainsIgnoreCase("experimental"))
                            {
                                Info = "实验性快照" + Version.VanillaName;
                            }
                            else if (Version.VanillaName == "pending")
                            {
                                Info = "实验性快照";
                            }
                            else if (Version.IsSnapshot)
                            {
                                Info = Version.Reliable ? "快照版 " + Version.VanillaName.Replace("-snapshot", "") : "快照版";
                            }
                            else
                            {
                                Info = Version.Reliable ? "正式版 " + Version.VanillaName : "正式版";
                            }

                            break;
                        }
                    case McInstanceState.Old:
                        {
                            Info = "远古版本";
                            break;
                        }
                    case McInstanceState.Fool:
                        {
                            Info = "愚人节版本 " + Version.VanillaName;
                            break;
                        }
                    case McInstanceState.Error:
                        {
                            return this.Info; // 已有错误信息
                        }

                    default:
                        {
                            return "发生了未知错误，请向作者反馈此问题";
                        }
                }
                return (Info + ModLoaderInfo).Replace("_", "-");
            }

            public MyVirtualizingElement<MyListItem> ToListItem(Action<MyListItem, EventArgs> ContentHandler = null)
            {
                return new MyVirtualizingElement<MyListItem>(() =>
        {
            var NewItem = new MyListItem()
            {
                Info = Info,
                Height = 42d,
                Tag = this,
                SnapsToDevicePixels = true,
                Type = MyListItem.CheckType.Clickable,
                ContentHandler = ContentHandler
            };
            // 标题
            NewItem.Inlines.Clear();
            NewItem.Inlines.Add(new Run(Name));
            if (!string.IsNullOrEmpty(ModBase.ReadIni(PathVersion + @"PCL\Setup.ini", "CustomInfo"))) // 如果版本设置了自定义描述，在标题后面以淡灰色显示其版本号
            {
                NewItem.Inlines.Add(new Run("  |  " + VersionDisplayName()) { Foreground = new ModBase.MyColor(215d, 215d, 215d), FontSize = 12d });
            }
            // Logo
            try
            {
                if (Logo.EndsWithF(@"PCL\Logo.png"))
                {
                    NewItem.Logo = PathVersion + @"PCL\Logo.png"; // 修复老版本中，存储的自定义 Logo 使用完整路径，导致移动后无法加载的 Bug
                }
                else
                {
                    NewItem.Logo = Logo;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "加载版本图标失败", LogBehavior.Toast);
                NewItem.Logo = "pack://application:,,,/images/Blocks/RedstoneBlock.png";
            }
            return NewItem;
        })
                {
                    Height = 42d
                };
            }

            /// <summary>
        /// 重命名 MC 版本。
        /// </summary>
            public McInstance Rename(string NewName)
            {
                string OldName = Name;
                string OldPath = PathVersion;
                string NewPath = PathUtils.AddSlashSuffix(Path.Combine(PathUtils.RemoveLastPart(OldPath), NewName));
                // 重新读取版本 JSON 信息，避免 JsonObject 中已被合并的项被重新存储
                JObject JsonObject;
                string OldJsonPath = GetJsonPath();
                try
                {
                    JsonObject = (JObject)FileUtils.ReadAsJson(OldJsonPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "在重命名读取 JSON 时失败");
                    JsonObject = this.JsonObject;
                }
                // 清理缓存
                Config.Save();
                ResetSettingsCache();
                // 重命名主文件夹
                DirectoryUtils.Move(OldPath, NewPath);
                // 重命名 jar 文件与 natives 文件夹
                // 不能进行遍历重命名，否则在版本名很短的时候容易误伤其他文件（#6443）
                if (DirectoryUtils.Exists($"{NewPath}{OldName}-natives"))
                    DirectoryUtils.Move($"{NewPath}{OldName}-natives", $"{NewPath}{NewName}-natives");
                if (FileUtils.Exists($"{NewPath}{OldName}.jar"))
                    FileUtils.Move($"{NewPath}{OldName}.jar", $"{NewPath}{NewName}.jar");
                // 替换版本设置文件中的路径
                if (FileUtils.Exists(NewPath + @"PCL\Setup.ini"))
                {
                    FileUtils.Write(NewPath + @"PCL\Setup.ini", FileUtils.ReadAsString(NewPath + @"PCL\Setup.ini").Replace(OldPath, NewPath));
                }
                // 更改已选中的版本
                if ((ModBase.ReadIni(McFolderSelected + "PCL.ini", "Version") ?? "") == (OldName ?? ""))
                    ModBase.WriteIni(McFolderSelected + "PCL.ini", "Version", NewName);
                // 更新版本 Json
                try
                {
                    JsonObject["id"] = NewName;
                    FileUtils.Delete(NewPath + PathUtils.GetLastPart(OldJsonPath));
                    FileUtils.Write(NewPath + NewName + ".json", JsonObject.ToString());
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "重命名版本 json 失败");
                }
                return new McInstance(NewPath).Load();
            }

            // 运算符支持
            public override bool Equals(object obj)
            {
                McInstance version = obj as McInstance;
                return version is not null && PathUtils.IsSame(PathVersion, version.PathVersion);
            }
            public static bool operator ==(McInstance a, McInstance b)
            {
                if (a is null && b is null)
                    return true;
                if (a is null || b is null)
                    return false;
                return PathUtils.IsSame(a.PathVersion, b.PathVersion);
            }
            public static bool operator !=(McInstance a, McInstance b)
            {
                return !(a == b);
            }

        }

        /// <summary>
    /// MC 版本独占锁。
    /// </summary>
        public sealed class McInstanceLock
        {
            private static readonly ConcurrentDictionary<string, IWorker> LockedPaths = new ConcurrentDictionary<string, IWorker>(StringComparer.OrdinalIgnoreCase);
            internal static bool IsAnyLocked
            {
                get
                {
                    return !LockedPaths.IsEmpty;
                }
            }
            /// <summary>
        /// 在 <paramref name="Worker"/> 结束前，锁定指定的 MC 版本。当 <paramref name="Worker"/> 结束时，会自动将锁定释放。
        /// <para/> 仅在该版本已被锁定时返回 false，此时不会改变锁定状态。
        /// </summary>
            public static bool TryAcquire(IWorker Worker, string InstancePath)
            {
                InstancePath = PathUtils.ForCompare(InstancePath);
                if (!LockedPaths.TryAdd(InstancePath, Worker))
                    return false;
                Worker.Stopped += () =>
                    {
                        IWorker argvalue = null;
                        LockedPaths.TryRemove(InstancePath, out argvalue);
                    };
                return true;
            }
            /// <summary>
        /// 检查 MC 版本是否正被锁定。
        /// </summary>
            public static bool IsLocked(McInstance Instance)
            {
                if (Instance is null)
                    return false;
                return LockedPaths.ContainsKey(PathUtils.ForCompare(Instance.PathVersion));
            }
        }

        /// <summary>
    /// 根据版本名获取对应的愚人节版本描述。非愚人节版本会返回空字符串。
    /// </summary>
        public static string GetMcFoolName(string Name)
        {
            Name = Name.Lower();
            if (Name.StartsWithF("2.0"))
            {
                return "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！";
            }
            else if (Name == "15w14a")
            {
                return "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。";
            }
            else if (Name == "1.rv-pre1")
            {
                return "2016 | 是时候将现代科技带入 Minecraft 了！";
            }
            else if (Name == "3d shareware v1.34")
            {
                return "2019 | 我们从地下室的废墟里找到了这个开发于 1994 年的杰作！";
            }
            else if (Name.StartsWithF("20w14inf") || Name == "20w14∞")
            {
                return "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！";
            }
            else if (Name == "22w13oneblockatatime")
            {
                return "2022 | 一次一个方块更新！迎接全新的挖掘、合成与骑乘玩法吧！";
            }
            else if (Name == "23w13a_or_b")
            {
                return "2023 | 研究表明：玩家喜欢作出选择——越多越好！";
            }
            else if (Name == "24w14potato")
            {
                return "2024 | 毒马铃薯一直都被大家忽视和低估，于是我们超级加强了它！";
            }
            else if (Name == "25w14craftmine")
            {
                return "2025 | 你可以合成任何东西——包括合成你的世界！";
            }
            else if (Name == "26w14a")
            {
                return "2026 | 为什么需要物品栏？让方块们跟着你走吧！";
            }
            else
            {
                return "";
            }
        }

        /// <summary>
    /// 当前按卡片分类的所有版本列表。
    /// </summary>
        public static Dictionary<McInstanceCardType, List<McInstance>> McInstanceList = new Dictionary<McInstanceCardType, List<McInstance>>();

        #endregion

        #region 版本列表加载

        /// <summary>
    /// 是否要求本次加载强制刷新版本列表。
    /// </summary>
        public static bool McInstanceListForceRefresh = false;
        /// <summary>
    /// 是否为本次打开 PCL 后第一次加载版本列表。
    /// 这会清理所有 .pclignore 文件，而非跳过这些对应版本。
    /// </summary>
        private static bool IsFirstMcInstanceListLoad = true;

        /// <summary>
    /// 加载 Minecraft 文件夹的版本列表。
    /// </summary>
        public static ModLoader.LoaderTask<string, int> McInstanceListLoader = new ModLoader.LoaderTask<string, int>("Minecraft Instance List", InitMcInstanceList) { ReloadTimeout = 1 };
        private static void InitMcInstanceList(ModLoader.LoaderTask<string, int> Loader)
        {
            // 开始加载
            string PathMc = Loader.Input;
            try
            {
                // 初始化
                McInstanceList = new Dictionary<McInstanceCardType, List<McInstance>>();

                // 检测缓存是否需要更新
                var FolderList = new List<string>();
                if (DirectoryUtils.Exists(PathMc + "versions")) // 不要使用 CheckPermission，会导致写入时间改变，从而使得文件夹被强制刷新
                {
                    try
                    {
                        foreach (DirectoryInfo Folder in DirectoryUtils.GetInfo(PathMc + "versions").GetDirectories())
                            FolderList.Add(Folder.Name);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("无法读取版本文件夹，可能是由于没有权限（" + PathMc + "versions）", ex);
                    }
                }
                // 不可用
                if (!FolderList.Any())
                {
                    ModBase.WriteIni(PathMc + "PCL.ini", "InstanceCache", ""); // 清空缓存
                    goto OnLoaded;
                }
                // 有可用版本
                int FolderListCheck = (int)Math.Round((Versions.McInstanceCacheVersion + "#" + FolderList.Join('#')).GetStableHashCode() % (decimal)(int.MaxValue - 1)); // 根据文件夹名列表生成辨识码
                // VB 原文用 `GoTo Reload` 跳进下面的 Else 块内部，C# 不允许跨作用域 goto，
                // 改为标记位：缓存不可用与「文件夹列表不符」走同一段重载逻辑，行为等价。
                bool NeedReloadInstances = false;
                if (!McInstanceListForceRefresh && ModBase.Val(ModBase.ReadIni(PathMc + "PCL.ini", "InstanceCache")) == FolderListCheck)
                {
                    // 可以使用缓存
                    var Result = InitMcInstanceListWithCache(PathMc);
                    if (Result is null)
                    {
                        NeedReloadInstances = true;
                    }
                    else
                    {
                        McInstanceList = Result;
                    }
                }
                else
                {
                    // 文件夹列表不符
                    NeedReloadInstances = true;
                }
                if (NeedReloadInstances)
                {
                    McInstanceListForceRefresh = false;
                    Logger.Info("文件夹列表变更，重载所有版本");
                    ModBase.WriteIni(PathMc + "PCL.ini", "InstanceCache", FolderListCheck.ToString());
                    McInstanceList = InitMcInstanceListWithoutCache(PathMc);
                }
                IsFirstMcInstanceListLoad = false;

            // 改变当前选择的版本
            OnLoaded:
                ;

                if (Loader.IsCanceled)
                    return;
                if (McInstanceList.Any(v => v.Key != McInstanceCardType.Error))
                {
                    // 尝试读取已储存的选择
                    string SavedSelection = ModBase.ReadIni(PathMc + "PCL.ini", "Version");
                    if (!string.IsNullOrEmpty(SavedSelection))
                    {
                        foreach (KeyValuePair<McInstanceCardType, List<McInstance>> Card in McInstanceList)
                        {
                            foreach (McInstance Instance in Card.Value)
                            {
                                if ((Instance.Name ?? "") == (SavedSelection ?? "") && !(Instance.State == McInstance.McInstanceState.Error))
                                {
                                    // 使用已储存的选择
                                    McInstanceSelected = Instance;
                                    Logger.Info($"选择该文件夹储存的 Minecraft 版本：{McInstanceSelected.PathVersion}");
                                    return;
                                }
                            }
                        }
                    }
                    if (!(McInstanceList.First().Value[0].State == McInstance.McInstanceState.Error))
                    {
                        // 自动选择第一项
                        McInstanceSelected = McInstanceList.First().Value[0];
                        Logger.Info($"自动选择 Minecraft 版本：{McInstanceSelected.PathVersion}");
                    }
                }
                else
                {
                    McInstanceSelected = null;
                    Logger.Info("未找到可用 Minecraft 版本");
                }
                if (Settings.Get<bool>("SystemDebugDelay"))
                    Thread.Sleep(ModBase.RandomInteger(200, 3000));
            }
            catch (Exception ex)
            {
                if (Loader.IsCanceled || ex.IsCanceled())
                    return; // #5617
                ModBase.WriteIni(PathMc + "PCL.ini", "InstanceCache", ""); // 要求下次重新加载
                Logger.Error(ex, "加载 .minecraft 版本列表失败");
            }
        }

        // 获取版本列表
        private static Dictionary<McInstanceCardType, List<McInstance>> InitMcInstanceListWithCache(string Folder)
        {
            var Results = new Dictionary<McInstanceCardType, List<McInstance>>();
            try
            {
                int CardCount = Conversions.ToInteger(ModBase.ReadIni(Folder + "PCL.ini", "CardCount", (-1).ToString()));
                if (CardCount == -1)
                    return null;
                for (int i = 0, loopTo = CardCount - 1; i <= loopTo; i++)
                {
                    McInstanceCardType CardType = (McInstanceCardType)Conversions.ToInteger(ModBase.ReadIni(Folder + "PCL.ini", "CardKey" + (i + 1), ":"));
                    var InstanceList = new List<McInstance>();

                    // 循环读取版本
                    foreach (string VersionName in ModBase.ReadIni(Folder + "PCL.ini", "CardValue" + (i + 1), ":").Split(":"))
                    {
                        if (string.IsNullOrEmpty(VersionName))
                            continue;
                        string FolderVersions = $@"{Folder}versions\{VersionName}\";
                        if (FileUtils.Exists(FolderVersions + ".pclignore"))
                        {
                            if (IsFirstMcInstanceListLoad)
                            {
                                Logger.Info($"清理残留的忽略项目：{FolderVersions}"); // #2781
                                FileUtils.Delete(FolderVersions + ".pclignore");
                            }
                            else
                            {
                                Logger.Info($"跳过要求忽略的项目：{FolderVersions}");
                                continue;
                            }
                        }
                        try
                        {

                            // 读取单个版本
                            var Instance = new McInstance(FolderVersions);
                            InstanceList.Add(Instance);
                            Instance.Info = ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "CustomInfo", "");
                            if (string.IsNullOrEmpty(Instance.Info))
                                Instance.Info = ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "Info", Instance.Info);
                            Instance.Logo = ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "Logo", Instance.Logo);
                            Instance.State = (McInstance.McInstanceState)Conversions.ToInteger(ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "State", ((int)Instance.State).ToString()));
                            Instance.IsStar = Conversions.ToBoolean(ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "IsStar", Conversions.ToString(false)));
                            Instance.DisplayType = (McInstanceCardType)Conversions.ToInteger(ModBase.ReadIni(Folder + @"PCL\Setup.ini", "DisplayType", ((int)McInstanceCardType.Auto).ToString()));
                            var Manifest = Instance.State == McInstance.McInstanceState.Error ? null : Configs.McInstallManifest.Get(Instance.Config);
                            // 检查 manifest 是否有过期文件，并进行更新
                            if (Manifest is not null && Manifest.SchemaVersion <= Versions.McInstallManifestVersion && Manifest.ManagedFiles.Any(File => File.Component is not null))
                            {
                                var LatestManifest = Manifest.RefreshModComponents(Instance.PathIndie);
                                if (!ReferenceEquals(LatestManifest, Manifest))
                                {
                                    Manifest = LatestManifest;
                                    Configs.McInstallManifest.Set(Manifest, Instance.Config);
                                    Instance.Config.Save();
                                }
                            }
                            string CachedVanillaName = ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "VersionVanillaName", "Unknown");
                            if (Instance.State != McInstance.McInstanceState.Error && (Manifest is not null || CachedVanillaName != "Unknown"))
                            {
                                McVersion Version;
                                if (Manifest is not null)
                                {
                                    // 从 manifest 中读取版本信息
                                    Version = new McVersion(Manifest.Components) { OptiFineAsMod = Manifest.ManagedFiles.Any(File => File.Component == "OptiFine") };
                                }
                                else
                                {
                                    // 从 Setup.ini 中读取版本信息
                                    string ReadCachedInfo(string Key)
                                    {
                                        string Value = ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", Key, null);
                                        return string.IsNullOrEmpty(Value) ? null : Value; // 对老版本 Setup.ini 的空值兼容
                                    };
                                    Version = new McVersion(CachedVanillaName) { OptiFine = ReadCachedInfo("VersionOptiFine") };
                                    foreach (var Loader in new[] { ModLoaders.Fabric, ModLoaders.Forge, ModLoaders.NeoForge })
                                    {
                                        string CachedVersion = ReadCachedInfo($"Version{Loader}");
                                        if (CachedVersion is not null)
                                            Version.ModLoaderVersions[Loader] = CachedVersion;
                                    }
                                    if (Conversions.ToBoolean(ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "VersionLiteLoader", Conversions.ToString(false))))
                                        Version.ModLoaderVersions[ModLoaders.LiteLoader] = "未知版本";
                                }
                                Version.ReleaseTime = Conversions.ToDate(ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "ReleaseTime", Conversions.ToString(new DateTime(1970, 1, 1, 15, 0, 0))));
                                Version.IsFool = Version.IsFool || Instance.State == McInstance.McInstanceState.Fool;
                                Version.IsSnapshot = Version.IsSnapshot || Instance.State == McInstance.McInstanceState.Snapshot || Instance.Name.ContainsIgnoreCase("combat");
                                Instance.Version = Version;
                                if (Manifest is not null)
                                    Instance.RefreshInfo();
                            }
                            else
                            {
                                Instance.Load();
                            }

                            // 重新检查错误版本
                            if (Instance.State == McInstance.McInstanceState.Error)
                            {
                                // 重新尝试获取版本信息
                                string OldDesc = Instance.Info;
                                Instance.State = McInstance.McInstanceState.Original;
                                Instance.Check();
                                // 校验错误原因是否改变
                                string CustomInfo = ModBase.ReadIni(Instance.PathVersion + @"PCL\Setup.ini", "CustomInfo");
                                if (Instance.State == McInstance.McInstanceState.Original || string.IsNullOrEmpty(CustomInfo) && !((OldDesc ?? "") == (Instance.Info ?? "")))
                                {
                                    Logger.Info($"版本 {Instance.Name} 的错误状态已变更，新的状态为：{Instance.Info}");
                                    return null;
                                }
                            }

                            // 校验未加载的版本
                            if (string.IsNullOrEmpty(Instance.Logo))
                            {
                                Logger.Info($"版本 {Instance.Name} 未被加载");
                                return null;
                            }
                        }

                        catch (Exception ex)
                        {
                            Logger.Warn(ex, $"读取版本加载缓存失败（{VersionName}）");
                            return null;
                        }
                    }

                    if (InstanceList.Any())
                        Results.Add(CardType, InstanceList);
                }
                return Results;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取版本缓存失败");
                return null;
            }
        }
        private static Dictionary<McInstanceCardType, List<McInstance>> InitMcInstanceListWithoutCache(string Folder)
        {
            var InstanceList = new List<McInstance>();

            #region 循环加载每个版本的信息
            foreach (DirectoryInfo VersionFolderInfo in DirectoryUtils.GetInfo(Folder + "versions").GetDirectories())
            {
                if (!VersionFolderInfo.Exists || !VersionFolderInfo.EnumerateFiles().Any())
                {
                    Logger.Info($"跳过空文件夹：{VersionFolderInfo.FullName}");
                    continue;
                }
                if ((VersionFolderInfo.Name == "cache" || VersionFolderInfo.Name == "BLClient" || VersionFolderInfo.Name == "PCL") && !FileUtils.Exists(VersionFolderInfo.FullName + @"\" + VersionFolderInfo.Name + ".json"))
                {
                    Logger.Info($"跳过可能不是版本文件夹的项目：{VersionFolderInfo.FullName}");
                    continue;
                }
                string VersionFolder = VersionFolderInfo.FullName + @"\";
                if (FileUtils.Exists(VersionFolder + ".pclignore"))
                {
                    if (IsFirstMcInstanceListLoad)
                    {
                        Logger.Info($"清理残留的忽略项目：{VersionFolder}"); // #2781
                        FileUtils.Delete(VersionFolder + ".pclignore");
                    }
                    else
                    {
                        Logger.Info($"跳过要求忽略的项目：{VersionFolder}");
                        continue;
                    }
                }
                var Instance = new McInstance(VersionFolder);
                InstanceList.Add(Instance);
                Instance.Load();
            }
            #endregion

            var Results = new Dictionary<McInstanceCardType, List<McInstance>>();

            #region 将版本分类到各个卡片
            try
            {

                // 未经过自定义的版本列表
                var InstanceListOriginal = new Dictionary<McInstanceCardType, List<McInstance>>();

                // 单独列出收藏的版本
                var InstanceStar = new List<McInstance>();
                foreach (McInstance Instance in InstanceList.ToList())
                {
                    if (!Instance.IsStar)
                        continue;
                    if (Instance.DisplayType == McInstanceCardType.Hidden)
                        continue;
                    InstanceStar.Add(Instance);
                    InstanceList.Remove(Instance);
                }
                if (InstanceStar.Any())
                    InstanceListOriginal.Add(McInstanceCardType.Star, InstanceStar);

                // 预先筛选出愚人节和错误的版本
                McInstanceFilter(ref InstanceList, ref InstanceListOriginal, new[] { McInstance.McInstanceState.Error }, McInstanceCardType.Error);
                McInstanceFilter(ref InstanceList, ref InstanceListOriginal, new[] { McInstance.McInstanceState.Fool }, McInstanceCardType.Fool);

                // 筛选 API 版本
                McInstanceFilter(ref InstanceList, ref InstanceListOriginal, new[] { McInstance.McInstanceState.Forge, McInstance.McInstanceState.NeoForge, McInstance.McInstanceState.LiteLoader, McInstance.McInstanceState.Fabric }, McInstanceCardType.API);

                // 将老版本预先分类入不常用，只剩余原版、快照、OptiFine
                var InstanceUseful = new List<McInstance>();
                var InstanceRubbish = new List<McInstance>();
                McInstanceFilter(ref InstanceList, new[] { McInstance.McInstanceState.Old }, ref InstanceRubbish);

                // 确认最新版本，若为快照则加入常用列表
                var LargestInstance = InstanceList.Where(v => v.State == McInstance.McInstanceState.Original || v.State == McInstance.McInstanceState.Snapshot).MaxBy(v => v.ReleaseTime);
                if (LargestInstance is not null && LargestInstance.State == McInstance.McInstanceState.Snapshot)
                {
                    InstanceUseful.Add(LargestInstance);
                    InstanceList.Remove(LargestInstance);
                }

                // 将剩余的快照全部拖进不常用列表
                McInstanceFilter(ref InstanceList, new[] { McInstance.McInstanceState.Snapshot }, ref InstanceRubbish);

                // 获取每个 Drop 下最新的原版与 OptiFine
                var NewerInstance = new Dictionary<string, McInstance>();
                var ExistDrops = new List<int>();
                foreach (McInstance Instance in InstanceList)
                {
                    if (!Instance.Version.Valid)
                        continue;
                    if (!ExistDrops.Contains(Instance.Version.Drop))
                        ExistDrops.Add(Instance.Version.Drop);
                    string Key = Instance.Version.Drop + "-" + ((int)Instance.State).ToString();
                    if (!NewerInstance.ContainsKey(Key))
                    {
                        NewerInstance.Add(Key, Instance);
                        continue;
                    }
                    if (Instance.State == McInstance.McInstanceState.OptiFine)
                    {
                        if (Instance.Version.OptiFineCode > NewerInstance[Key].Version.OptiFineCode)
                            NewerInstance[Key] = Instance; // OptiFine 根据版本号判断
                    }
                    else if (Instance.ReleaseTime > NewerInstance[Key].ReleaseTime)
                        NewerInstance[Key] = Instance; // 原版根据发布时间判断
                }

                // 将每个 Drop 下的最常规版本加入
                foreach (int Drop in ExistDrops)
                {
                    if (NewerInstance.ContainsKey(Drop + "-" + ((int)McInstance.McInstanceState.OptiFine).ToString()) && NewerInstance.ContainsKey(Drop + "-" + ((int)McInstance.McInstanceState.Original).ToString()))
                    {
                        // 同时存在 OptiFine 与原版
                        var VanillaInstance = NewerInstance[Drop + "-" + ((int)McInstance.McInstanceState.Original).ToString()];
                        var OptiFineInstance = NewerInstance[Drop + "-" + ((int)McInstance.McInstanceState.OptiFine).ToString()];
                        if (VanillaInstance.Version.Drop > OptiFineInstance.Version.Drop)
                        {
                            // 仅在原版比 OptiFine 更新时才加入原版
                            InstanceUseful.Add(VanillaInstance);
                            InstanceList.Remove(VanillaInstance);
                        }
                        InstanceUseful.Add(OptiFineInstance);
                        InstanceList.Remove(OptiFineInstance);
                    }
                    else if (NewerInstance.ContainsKey(Drop + "-" + ((int)McInstance.McInstanceState.OptiFine).ToString()))
                    {
                        // 没有原版，直接加入 OptiFine
                        InstanceUseful.Add(NewerInstance[Drop + "-" + ((int)McInstance.McInstanceState.OptiFine).ToString()]);
                        InstanceList.Remove(NewerInstance[Drop + "-" + ((int)McInstance.McInstanceState.OptiFine).ToString()]);
                    }
                    else if (NewerInstance.ContainsKey(Drop + "-" + ((int)McInstance.McInstanceState.Original).ToString()))
                    {
                        // 没有 OptiFine，直接加入原版
                        InstanceUseful.Add(NewerInstance[Drop + "-" + ((int)McInstance.McInstanceState.Original).ToString()]);
                        InstanceList.Remove(NewerInstance[Drop + "-" + ((int)McInstance.McInstanceState.Original).ToString()]);
                    }
                }

                // 将剩余的东西添加进去
                InstanceRubbish.AddRange(InstanceList);
                if (InstanceUseful.Any())
                    InstanceListOriginal.Add(McInstanceCardType.OriginalLike, InstanceUseful);
                if (InstanceRubbish.Any())
                    InstanceListOriginal.Add(McInstanceCardType.Rubbish, InstanceRubbish);

                // 按照自定义版本分类重新添加
                foreach (var InstancePair in InstanceListOriginal)
                {
                    foreach (McInstance Instance in InstancePair.Value)
                    {
                        var RealType = Instance.DisplayType == 0 || InstancePair.Key == McInstanceCardType.Star ? InstancePair.Key : Instance.DisplayType;
                        if (!Results.ContainsKey(RealType))
                            Results.Add(RealType, new List<McInstance>());
                        Results[RealType].Add(Instance);
                    }
                }
            }

            catch (Exception ex)
            {
                Results.Clear();
                Logger.Error(ex, "分类版本列表失败");
            }
            #endregion
            #region 对卡片与版本进行排序

            // 卡片排序
            var SortedInstanceList = new Dictionary<McInstanceCardType, List<McInstance>>();
            // VB 原文：For Each SortRule As String In {McInstanceCardType.Star, ...}
            // VB 允许把枚举隐式转成字符串（且按 VB 规则转成「数字字符串」，故原代码又用 Conversions.ToInteger 转回枚举）。
            // C# 不允许枚举赋给 string（CS0030），这里直接按枚举迭代并用枚举做键，往返结果与 VB 完全一致。
            foreach (McInstanceCardType SortRule in new[] { McInstanceCardType.Star, McInstanceCardType.API, McInstanceCardType.OriginalLike, McInstanceCardType.Rubbish, McInstanceCardType.Fool, McInstanceCardType.Error, McInstanceCardType.Hidden })
            {
                if (Results.ContainsKey(SortRule))
                    SortedInstanceList.Add(SortRule, Results[SortRule]);
            }
            Results = SortedInstanceList;

            // 版本排序
            foreach (var CardType in new[] { McInstanceCardType.Star, McInstanceCardType.API, McInstanceCardType.OriginalLike, McInstanceCardType.Rubbish, McInstanceCardType.Fool })
            {
                if (!Results.ContainsKey(CardType))
                    continue;
                int GetComponentCode(McInstance Instance)
                {
                    if (Instance.Version.ForgelikeCode > 0)
                        return Instance.Version.ForgelikeCode;
                    if (Instance.Version.HasOptiFine)
                        return Instance.Version.OptiFineCode;
                    return 0;
                };
                Results[CardType] = Results[CardType].SortByComparison((Left, Right) =>
        {
            // 发布时间
            if ((Left.ReleaseTime.Year >= 2000 || Right.ReleaseTime.Year >= 2000) && Left.ReleaseTime != Right.ReleaseTime)
                return Left.ReleaseTime > Right.ReleaseTime;
            // 附加组件种类
            if (Left.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) != Right.Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
                return Left.Version.ModLoaders.HasFlagF(ModLoaders.Fabric);
            if (Left.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) != Right.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
                return Left.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge);
            if (Left.Version.ModLoaders.HasFlagF(ModLoaders.Forge) != Right.Version.ModLoaders.HasFlagF(ModLoaders.Forge))
                return Left.Version.ModLoaders.HasFlagF(ModLoaders.Forge);
            if (Left.Version.HasOptiFine != Right.Version.HasOptiFine)
                return Left.Version.HasOptiFine;
            if (Left.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) != Right.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader))
                return Left.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader);
            // 附加组件版本
            if (GetComponentCode(Left) != GetComponentCode(Right))
                return GetComponentCode(Left) > GetComponentCode(Right);
            // 名称
            return Operators.CompareString(Left.Name, Right.Name, false) > 0;
        });
            }

            #endregion
            #region 保存卡片缓存
            ModBase.WriteIni(Folder + "PCL.ini", "CardCount", Results.Count.ToString());
            for (int i = 0, loopTo = Results.Count - 1; i <= loopTo; i++)
            {
                ModBase.WriteIni(Folder + "PCL.ini", "CardKey" + (i + 1), ((int)Results.Keys.ElementAtOrDefault(i)).ToString());
                string Value = "";
                foreach (McInstance Instance in Results.Values.ElementAtOrDefault(i))
                    Value += Instance.Name + ":";
                ModBase.WriteIni(Folder + "PCL.ini", "CardValue" + (i + 1), Value);
            }
            #endregion

            return Results;
        }
        /// <summary>
    /// 筛选特定种类的版本，并直接添加为卡片。
    /// </summary>
    /// <param name="InstanceList">用于筛选的列表。</param>
    /// <param name="Formula">需要筛选出的版本类型。-2 代表隐藏的版本。</param>
    /// <param name="CardType">卡片的名称。</param>
        private static void McInstanceFilter(ref List<McInstance> InstanceList, ref Dictionary<McInstanceCardType, List<McInstance>> Target, McInstance.McInstanceState[] Formula, McInstanceCardType CardType)
        {
            var KeepList = InstanceList.Where(v => Formula.Contains(v.State)).ToList();
            // 加入版本列表，并从剩余中删除
            if (KeepList.Any())
            {
                Target.Add(CardType, KeepList);
                InstanceList = InstanceList.Except(KeepList).ToList();
            }
        }
        /// <summary>
    /// 筛选特定种类的版本，并增加入一个已有列表中。
    /// </summary>
    /// <param name="InstanceList">用于筛选的列表。</param>
    /// <param name="Formula">需要筛选出的版本类型。-2 代表隐藏的版本。</param>
    /// <param name="KeepList">传入需要增加入的列表。</param>
        private static void McInstanceFilter(ref List<McInstance> InstanceList, McInstance.McInstanceState[] Formula, ref List<McInstance> KeepList)
        {
            KeepList.AddRange(InstanceList.Where(v => Formula.Contains(v.State)));
            // 加入版本列表，并从剩余中删除
            if (KeepList.Any())
            {
                InstanceList = InstanceList.Except(KeepList).ToList();
            }
        }
        public enum McInstanceCardType
        {
            Star = -1,
            Auto = 0, // 仅用于强制版本分类的自动
            Hidden = 1,
            API = 2,
            OriginalLike = 3,
            Rubbish = 4,
            Fool = 5,
            Error = 6
        }

        #endregion

        #region 皮肤

        public struct McSkinInfo
        {
            public bool IsSlim;
            public string LocalFile;
            public bool IsVaild;
        }
        /// <summary>
    /// 要求玩家选择一个皮肤文件，并进行相关校验。
    /// </summary>
        public static McSkinInfo McSkinSelect()
        {
            string FileName = Dialogs.SelectFile("选择皮肤文件", false, filter: new[] { (new[] { "png", "jpg", "jpeg", "webp" }, "皮肤文件") }).FirstOrDefault();

            // 验证有效性
            if (string.IsNullOrEmpty(FileName))
                return new McSkinInfo() { IsVaild = false };
            try
            {
                var Image = new MyBitmap(FileName);
                if (Image.Pic.Width != 64 || !(Image.Pic.Height == 32 || Image.Pic.Height == 64))
                {
                    ModMain.Hint("皮肤图片大小应为 64x32 像素或 64x64 像素！", ModMain.HintType.Red);
                    return new McSkinInfo() { IsVaild = false };
                }
                var FileInfo = FileUtils.GetInfo(FileName);
                if (FileInfo.Length > 24 * 1024)
                {
                    ModMain.Hint("皮肤文件大小需小于 24 KB，而所选文件大小为 " + Math.Round(FileInfo.Length / 1024d, 2) + " KB", ModMain.HintType.Red);
                    return new McSkinInfo() { IsVaild = false };
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "皮肤文件存在错误", LogBehavior.Toast);
                return new McSkinInfo() { IsVaild = false };
            }

            // 获取皮肤种类
            int IsSlim = ModMain.MyMsgBox("此皮肤为 Steve 模型（粗手臂）还是 Alex 模型（细手臂）？", "选择皮肤种类", "Steve 模型", "Alex 模型", "我不知道", HighLight: false);
            if (IsSlim == 3)
            {
                ModMain.Hint("请在皮肤下载页面确认皮肤种类后再使用此皮肤！");
                return new McSkinInfo() { IsVaild = false };
            }

            return new McSkinInfo() { IsVaild = true, IsSlim = IsSlim == 2, LocalFile = FileName };
        }

        /// <summary>
        /// 当前生效的 Authlib-Injector 验证服务器地址。
        /// 与社区版一致，未配置时回退到 LittleSkin，避免账号已保存但服务器为空时
        /// 拼出 "/authserver" 这种非法相对地址（登录与皮肤请求都会因此直接失败）。
        /// </summary>
        public static string McAuthServerGet()
        {
            string Server = "";
            try
            {
                Server = McInstanceSelected is null
                    ? Settings.Get<string>("CacheAuthServerServer")
                    : Settings.Get<string>("VersionServerAuthServer", Instance: McInstanceSelected);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取第三方验证服务器失败，回退到缓存设置");
            }
            if (string.IsNullOrEmpty(Server))
            {
                try
                {
                    Server = Settings.Get<string>("CacheAuthServerServer");
                }
                catch
                {
                    Server = "";
                }
            }
            if (string.IsNullOrWhiteSpace(Server))
                Server = "https://littleskin.cn/api/yggdrasil";
            return Server.Trim().TrimEnd('/');
        }

        /// <summary>
    /// 获取 Uuid 对应的皮肤文件地址，失败将抛出异常。
    /// </summary>
        public static string McSkinGetAddress(string UUID, string Type)
        {
            if (string.IsNullOrEmpty(UUID))
                throw new Exception("UUID 为空。");
            if (UUID.StartsWithF("00000") && Type != "Auth")
                throw new Exception("离线 UUID 无正版皮肤文件：" + UUID);
            // 尝试读取缓存
            string CacheSkinAddress = ModBase.ReadIni(ModBase.PathTemp + @"Cache\Skin\Index" + Type + ".ini", UUID);
            if (!string.IsNullOrEmpty(CacheSkinAddress))
                return CacheSkinAddress;
            // 获取皮肤地址
            string Url;
            switch (Type ?? "")
            {
                case "Mojang":
                case "Ms":
                    {
                        Url = "https://sessionserver.mojang.com/session/minecraft/profile/";
                        break;
                    }
                case "Nide":
                    {
                        Url = "https://auth.mc-user.com:233/" + (McInstanceSelected is null ? Settings.Get<string>("CacheNideServer") : Settings.Get<string>("VersionServerNide", Instance: McInstanceSelected)) + "/sessionserver/session/minecraft/profile/";
                        break;
                    }
                case "Auth":
                    {
                        Url = McAuthServerGet() + "/sessionserver/session/minecraft/profile/";
                        break;
                    }

                default:
                    {
                        throw new ArgumentException("皮肤地址种类无效：" + (Type ?? "null"));
                    }
            }
            string SkinString = ModNet.NetRequestByClientRetry(Url + UUID, RequireJson: true);
            if (string.IsNullOrEmpty(SkinString))
                throw new Exception("皮肤返回值为空，可能是未设置自定义皮肤的用户");
            // 处理皮肤地址
            // VB 原文 Dim SkinValue As String 未赋初值，默认即为 Nothing；C# 有「明确赋值」检查（CS0165），
            // 故显式初始化为 null，取值语义与 VB 保持一致（正常运行路径一定会先被 Try 内赋值）。
            string SkinValue = null;
            do
            {
                try
                {
                    bool exitTry = false;
                    foreach (var SkinProperty in SkinString.DeserializeJson()["properties"])
                    {
                        if ((string)SkinProperty["name"] == "textures")
                        {
                            SkinValue = (string)SkinProperty["value"];
                            exitTry = true;
                            break;
                        }
                    }

                    if (exitTry)
                    {
                        break;
                    }
                    throw new Exception("未从皮肤返回值中找到符合条件的 Property");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"无法完成解析的皮肤返回值，可能是未设置自定义皮肤的用户：{SkinString}");
                    throw new Exception("皮肤返回值中不包含皮肤数据项，可能是未设置自定义皮肤的用户", ex);
                }
            }
            while (false);
            SkinString = Encoding.UTF8.GetString(Convert.FromBase64String(SkinValue));
            JObject SkinJson = (JObject)SkinString.Lower().DeserializeJson();
            if (SkinJson["textures"] is null || SkinJson["textures"]["skin"] is null || SkinJson["textures"]["skin"]["url"] is null)
            {
                throw new Exception("用户未设置自定义皮肤");
            }
            else
            {
                string SkinUrl = SkinJson["textures"]["skin"]["url"].ToString();
                SkinValue = SkinUrl.Contains("minecraft.net/") ? SkinUrl.Replace("http://", "https://") : SkinUrl;
            }
            // 保存缓存
            ModBase.WriteIni(ModBase.PathTemp + @"Cache\Skin\Index" + Type + ".ini", UUID, SkinValue);
            Logger.Info($"UUID {UUID} 对应的皮肤文件为 {SkinValue}");
            return SkinValue;
        }

        private readonly static object McSkinDownloadLock = new object();
        /// <summary>
    /// 从 Url 下载皮肤，可能会使用缓存。返回本地文件路径，失败将抛出异常。
    /// </summary>
        public static string McSkinDownload(string Address)
        {
            string SkinName = PathUtils.GetLastPart(Address);
            string FileAddress = ModBase.PathTemp + @"Cache\Skin\" + Address.GetStableHashCode() + ".png";
            lock (McSkinDownloadLock)
            {
                if (!FileUtils.Exists(FileAddress))
                {
                    ModNet.NetDownloadByClient(Address, FileAddress + ".PCLDownloading");
                    FileUtils.Move(FileAddress + ".PCLDownloading", FileAddress);
                    Logger.Info($"皮肤下载成功：{FileAddress}");
                }
                return FileAddress;
            }
        }

        /// <summary>
    /// 获取 Uuid 对应的皮肤，返回“Steve”或“Alex”。
    /// </summary>
        public static string McSkinSex(string Uuid)
        {
            if (!(Uuid.Length == 32))
                return "Steve";
            int a = int.Parse(Conversions.ToString(Uuid[7]), System.Globalization.NumberStyles.AllowHexSpecifier);
            int b = int.Parse(Conversions.ToString(Uuid[15]), System.Globalization.NumberStyles.AllowHexSpecifier);
            int c = int.Parse(Conversions.ToString(Uuid[23]), System.Globalization.NumberStyles.AllowHexSpecifier);
            int d = int.Parse(Conversions.ToString(Uuid[31]), System.Globalization.NumberStyles.AllowHexSpecifier);
            return Conversions.ToBoolean((a ^ b ^ c ^ d) % 2) ? "Alex" : "Steve";
            // Math.floorMod(uuid.hashCode(), 18)

            // Public Function hashCode(ByVal str As String) As Integer
            // Dim hash As Integer = 0
            // Dim n As Integer = str.Length
            // If n = 0 Then
            // Return hash
            // End If
            // For i As Integer = 0 To n - 1
            // hash = hash + Asc(str(i)) * (1 << (n - i - 1))
            // Next
            // Return hash
            // End Function
        }

        #endregion

        #region 支持库文件（Library）

        public class McLibEntry
        {
            /// <summary>
        /// 文件的完整本地路径。
        /// </summary>
            public string LocalPath;
            /// <summary>
        /// 文件大小。若无有效数据即为 0。
        /// </summary>
            public long Size = 0L;
            /// <summary>
        /// 是否为 Natives 文件。
        /// </summary>
            public bool IsNatives = false;
            /// <summary>
        /// 文件的 SHA1。
        /// </summary>
            public string SHA1 = null;
            /// <summary>
        /// 由 Json 提供的 URL，若没有则为 Nothing。
        /// </summary>
            public string Url
            {
                get
                {
                    return _Url;
                }
                set
                {
                    // 孤儿 Forge 作者喜欢把没有 URL 的写个空字符串
                    _Url = string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
            private string _Url;
            /// <summary>
        /// 原 Json 中 Name 项除去版本号部分的较前部分。可能为 Nothing。
        /// </summary>
            public string Name
            {
                get
                {
                    if (OriginalName is null)
                        return null;
                    var Splited = new List<string>(OriginalName.Split(":"));
                    Splited.RemoveAt(2); // Java 的此格式下版本号固定为第三段，第四段可能包含架构、分包等其他信息
                    return Splited.Join(':');
                }
            }
            /// <summary>
        /// 原 Json 中的 Name 项。
        /// </summary>
            public string OriginalName;

            public override string ToString()
            {
                return (IsNatives ? "[Native] " : "") + StringUtils.FormatByteSize(Size) + " | " + LocalPath;
            }
        }

        /// <summary>
    /// 检查是否符合 Json 中的 Rules。
    /// </summary>
    /// <param name="RuleToken">Json 中的 "rules" 项目。</param>
        public static bool McJsonRuleCheck(JToken RuleToken)
        {
            if (RuleToken is null)
                return true;

            // 初始化
            bool Required = false;
            foreach (JToken Rule in RuleToken)
            {

                // 单条条件验证
                bool IsRightRule = true; // 是否为正确的规则
                if (Rule["os"] is not null) // 操作系统
                {
                    if (Rule["os"]["name"] is not null) // 操作系统名称
                    {
                        string OsName = Rule["os"]["name"].ToString();
                        if (OsName == "unknown")
                        {
                        }
                        else if (OsName == "windows")
                        {
                            if (Rule["os"]["version"] is not null) // 操作系统版本
                            {
                                IsRightRule = IsRightRule && OSVersion.RegexCheck(Rule["os"]["version"].ToString());
                            }
                        }
                        else
                        {
                            IsRightRule = false;
                        }
                    }
                    if (Rule["os"]["arch"] is not null) // 操作系统架构
                    {
                        IsRightRule = IsRightRule && Rule["os"]["arch"].ToString() != "x86" && !Rule["os"]["arch"].Contains("arm");
                    }
                }
                if (!(Rule["features"] == null)) // 标签
                {
                    IsRightRule = IsRightRule && Rule["features"]["is_demo_user"] == null; // 反选是否为 Demo 用户
                    // VB 原文把声明成 JProperty 参数的 Lambda 直接传给 Children.Any（VB 允许放宽委托转换）；
                    // C# 的显式 Lambda 参数类型必须与 Func(Of JToken, Boolean) 一致（否则 CS1929），故改为先判断 Token 是否为 JProperty。
                    if (((JObject)Rule["features"]).Children().Any(Token => Token is JProperty Property && Property.Name.Contains("quick_play")))
                    {
                        IsRightRule = false; // 不开 Quick Play，让玩家自己加去
                    }
                }

                // 反选确认
                if (Rule["action"].ToString() == "allow")
                {
                    if (IsRightRule)
                        Required = true; // allow
                }
                else if (IsRightRule)
                    Required = false; // disallow

            }
            return Required;
        }
        private static string OSVersion = My.Computer.Info.OSVersion;

        /// <summary>
    /// 递归获取 Minecraft 某一版本的完整支持库列表。
    /// </summary>
        public static List<McLibEntry> McLibListGet(McInstance Instance, bool IncludeMainJar)
        {

            // 获取当前支持库列表
            Logger.Info($"获取支持库列表：{Instance.Name}");
            var Result = McLibListGetWithJson(Instance.JsonObject);

            // 需要添加原版 Jar
            if (IncludeMainJar)
            {
                McInstance RealInstance;
                string RequiredJar = Instance.JsonObject["jar"]?.ToString();
                if (Instance.IsHmclFormatJson || RequiredJar is null)
                {
                    // HMCL 项直接使用自身的 Jar
                    // 根据 Inherit 获取最深层版本
                    var OriginalInstance = Instance;
                    // 1.17+ 的 Forge 不寻找 Inherit
                    if (!((Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) || Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) && Instance.Version.Vanilla.Major >= 17))
                    {
                        while (!string.IsNullOrEmpty(OriginalInstance.InheritName))
                        {
                            if ((OriginalInstance.InheritName ?? "") == (OriginalInstance.Name ?? ""))
                                break;
                            OriginalInstance = new McInstance(McFolderSelected + @"versions\" + OriginalInstance.InheritName + @"\");
                        }
                    }
                    // 需要新建对象，否则后面的 Check 会导致 McInstanceCurrent 的 State 变回 Original
                    // 复现：启动一个 Snapshot 版本
                    RealInstance = new McInstance(OriginalInstance.PathVersion);
                }
                else
                {
                    // Json 已提供 Jar 字段，使用该字段的信息
                    RealInstance = new McInstance(RequiredJar);
                }
                string ClientUrl;
                string ClientSHA1;
                // 判断需求的版本是否存在
                // 不能调用 RealVersion.Check()，可能会莫名其妙地触发 CheckPermission 正被另一进程使用，导致误判前置不存在
                if (!FileUtils.Exists(RealInstance.PathVersion + RealInstance.Name + ".json"))
                {
                    RealInstance = Instance;
                    Logger.Warn($"可能缺少前置版本 {RealInstance.Name}，找不到对应的 json 文件");
                }
                // 获取详细下载信息
                if (RealInstance.JsonObject["downloads"] is not null && RealInstance.JsonObject["downloads"]["client"] is not null)
                {
                    ClientUrl = (string)RealInstance.JsonObject["downloads"]["client"]["url"];
                    ClientSHA1 = (string)RealInstance.JsonObject["downloads"]["client"]["sha1"];
                }
                else
                {
                    ClientUrl = null;
                    ClientSHA1 = null;
                }
                // 把所需的原版 Jar 添加进去
                Result.Add(new McLibEntry() { LocalPath = RealInstance.PathVersion + RealInstance.Name + ".jar", Size = 0L, IsNatives = false, Url = ClientUrl, SHA1 = ClientSHA1 });
            }

            return Result;
        }
        /// <summary>
    /// 获取 Minecraft 某一版本忽视继承的支持库列表，即结果中没有继承项。
    /// </summary>
        public static List<McLibEntry> McLibListGetWithJson(JObject JsonObject, bool KeepSameNameDifferentVersionResult = false, string CustomMcFolder = null)
        {
            CustomMcFolder = CustomMcFolder ?? McFolderSelected;

            // 转换为 LibToken
            var BasicArray = new List<McLibEntry>();
            foreach (JObject Library in ((JArray)JsonObject["libraries"]).Children())
            {

                // 清理 null 项（BakaXL 会把没有的项序列化为 null，但会被 Newtonsoft 转换为 JValue，导致 Is Nothing = false；这导致了 #409）
                for (int i = Library.Properties().Count() - 1; i >= 0; i -= 1)
                {
                    if (Library.Properties().ElementAtOrDefault(i).Value.Type == JTokenType.Null)
                        Library.Remove(Library.Properties().ElementAtOrDefault(i).Name);
                }

                // 检查是否需要（Rules）
                if (!McJsonRuleCheck(Library["rules"]))
                    continue;

                // 获取根节点下的 url
                string RootUrl = (string)Library["url"];
                if (RootUrl is not null)
                {
                    RootUrl += McLibGet((string)Library["name"], false, true, CustomMcFolder).Replace(@"\", "/");
                }

                // 根据是否本地化处理（Natives）
                if (Library["natives"] is null) // 没有 Natives
                {
                    string LocalPath;
                    LocalPath = McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder);
                    try
                    {
                        if (Library["downloads"] is not null && Library["downloads"]["artifact"] is not null)
                        {
                            McLibEntry @init = new McLibEntry();
                            BasicArray.Add((@init.OriginalName = (string)Library["name"], @init.Url = (string)(Library["downloads"]["artifact"]["url"] ?? RootUrl), @init.LocalPath = Library["downloads"]["artifact"]["path"] is null ? McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder) : CustomMcFolder + @"libraries\" + Library["downloads"]["artifact"]["path"].ToString().Replace("/", @"\"), @init.Size = (long)Math.Round(ModBase.Val(Library["downloads"]["artifact"]["size"].ToString())), @init.IsNatives = false, @init.SHA1 = Library["downloads"]["artifact"]["sha1"]?.ToString(), @init).@init);
                        }
                        else
                        {
                            BasicArray.Add(new McLibEntry() { OriginalName = (string)Library["name"], Url = RootUrl, LocalPath = LocalPath, Size = 0L, IsNatives = false, SHA1 = null });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"处理实际支持库列表失败（无 Natives，{Library["name"] ?? "Nothing"}）");
                        BasicArray.Add(new McLibEntry() { OriginalName = (string)Library["name"], Url = RootUrl, LocalPath = LocalPath, Size = 0L, IsNatives = false, SHA1 = null });
                    }
                }
                else if (Library["natives"]["windows"] is not null) // 有 Windows Natives
                {
                    string NativeName = (string)Library["natives"]["windows"];
                    try
                    {
                        if (Library["downloads"] is not null && Library["downloads"]["classifiers"] is not null && Library["downloads"]["classifiers"][NativeName] is not null)
                        {
                            var NativeData = Library["downloads"]["classifiers"][NativeName];
                            BasicArray.Add(new McLibEntry()
                            {
                                OriginalName = (string)Library["name"],
                                Url = (string)(NativeData["url"] ?? RootUrl),
                                LocalPath = NativeData["path"] is null ? McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder).Replace(".jar", "-" + NativeName + ".jar").Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32") : $@"{CustomMcFolder}libraries\{NativeData["path"].ToString().Replace("/", @"\")}",
                                Size = (long)Math.Round(ModBase.Val(NativeData["size"].ToString())),
                                IsNatives = true,
                                SHA1 = NativeData["sha1"].ToString()
                            });
                        }
                        else
                        {
                            BasicArray.Add(new McLibEntry()
                            {
                                OriginalName = (string)Library["name"],
                                Url = RootUrl,
                                Size = 0L,
                                IsNatives = true,
                                SHA1 = null,
                                LocalPath = McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder).Replace(".jar", $"-{NativeName}.jar").Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32")
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"处理实际支持库列表失败（有 Natives，{Library["name"] ?? "Nothing"}）");
                        BasicArray.Add(new McLibEntry()
                        {
                            OriginalName = (string)Library["name"],
                            Url = RootUrl,
                            Size = 0L,
                            IsNatives = true,
                            SHA1 = null,
                            LocalPath = McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder).Replace(".jar", $"-{NativeName}.jar").Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32")
                        });
                    }
                }

            }

            // 去重
            var ResultArray = new Dictionary<string, McLibEntry>();
            // 测试例：
            // D:\Minecraft\test\libraries\net\neoforged\mergetool\2.0.0\mergetool-2.0.0-api.jar
            // D:\Minecraft\test\libraries\org\apache\commons\commons-collections4\4.2\commons-collections4-4.2.jar
            // D:\Minecraft\test\libraries\com\google\guava\guava\31.1-jre\guava-31.1-jre.jar
            string GetVersion(McLibEntry Token) => PathUtils.GetLastPart(PathUtils.RemoveLastPart(Token.LocalPath));
            for (int i = 0, loopTo = BasicArray.Count - 1; i <= loopTo; i++)
            {
                string Key = BasicArray[i].Name + BasicArray[i].IsNatives.ToString();
                if (ResultArray.ContainsKey(Key))
                {
                    string BasicArrayVersion = GetVersion(BasicArray[i]);
                    string ResultArrayVersion = GetVersion(ResultArray[Key]);
                    if ((BasicArrayVersion ?? "") != (ResultArrayVersion ?? "") && KeepSameNameDifferentVersionResult)
                    {
                        Logger.Info($"发现疑似重复的支持库：{BasicArray[i]} ({BasicArrayVersion}) 与 {ResultArray[Key]} ({ResultArrayVersion})");
                        ResultArray.Add(Key + ModBase.GetUuid(), BasicArray[i]);
                    }
                    else
                    {
                        Logger.Info($"发现重复的支持库：{BasicArray[i]} ({BasicArrayVersion}) 与 {ResultArray[Key]} ({ResultArrayVersion})，已忽略其中之一");
                        if (McUtils.CompareVersionGE(BasicArrayVersion, ResultArrayVersion))
                        {
                            ResultArray[Key] = BasicArray[i];
                        }
                    }
                }
                else
                {
                    ResultArray.Add(Key, BasicArray[i]);
                }
            }
            return ResultArray.Values.ToList();
        }

        /// <summary>
    /// 获取版本所需支持库文件的 NetFile。
    /// </summary>
        public static List<ModNet.NetFile> McLibNetFilesFromInstance(McInstance Instance)
        {
            if (!Instance.IsLoaded)
                Instance.Load();
            var Result = new List<ModNet.NetFile>();

            // 更新此方法时需要同步更新 Forge 新版自动安装方法！

            // 主 Jar 文件
            try
            {
                var MainJar = ModDownload.DlClientJarGet(Instance, true);
                if (MainJar is not null)
                    Result.Add(MainJar);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "版本缺失主 jar 文件所必须的信息");
            }

            // Library 文件
            Result.AddRange(McLibNetFilesFromTokens(McLibListGet(Instance, false)));

            // 统一通行证文件
            if (Settings.Get<int>("VersionServerLogin", Instance: Instance) == 3)
            {
                string TargetFile = ModBase.PathPure.Value + "nide8auth.jar";
                try
                {
                    // 测试链接：https://auth.mc-user.com:233/00000000000000000000000000000000/
                    Logger.Info("开始获取统一通行证下载信息");
                    JObject DownloadInfo = (JObject)ModNet.NetRequestByClientRetry("https://auth.mc-user.com:233/" + Settings.Get<string>("VersionServerNide", Instance: Instance), RequireJson: true).DeserializeJson();
                    Result.Add(new ModNet.NetFile(Urls: new[] { "https://login.mc-user.com:233/index/jar" }, LocalPath: TargetFile, Checker: new FileChecker() { Hash = DownloadInfo["jarHash"].ToString() }));
                }
                catch (Exception ex)
                {
                    if (FileUtils.Exists(TargetFile))
                    {
                        Logger.Warn(ex, "获取统一通行证下载信息失败");
                    }
                    else
                    {
                        throw new Exception("获取统一通行证下载信息失败", ex);
                    }
                }
            }

            // Authlib-Injector 文件
            if (Settings.Get<int>("VersionServerLogin", Instance: Instance) == 4)
            {
                string TargetFile = ModBase.PathPure.Value + "authlib-injector.jar";
                try
                {
                    Logger.Info("开始获取 Authlib-Injector 下载信息");
                    JObject DownloadInfo = (JObject)ModNet.NetRequestByClientRetry("https://authlib-injector.yushi.moe/artifact/latest.json", BackupUrl: "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json", RequireJson: true).DeserializeJson();
                    string DownloadAddress = DownloadInfo["download_url"].ToString().Replace("bmclapi2.bangbang93.com/mirrors/authlib-injector", "authlib-injector.yushi.moe");
                    Result.Add(new ModNet.NetFile(new[] { DownloadAddress, DownloadAddress.Replace("authlib-injector.yushi.moe", "bmclapi2.bangbang93.com/mirrors/authlib-injector") }, TargetFile, new FileChecker() { Hash = DownloadInfo["checksums"]["sha256"].ToString() }));
                }
                catch (Exception ex)
                {
                    if (FileUtils.Exists(TargetFile))
                    {
                        Logger.Warn(ex, "获取 Authlib-Injector 下载信息失败");
                    }
                    else
                    {
                        throw new Exception("获取 Authlib-Injector 下载信息失败", ex);
                    }
                }
            }

            // 跳过校验
            if (Conversions.ToBoolean(ShouldIgnoreFileCheck(Instance)))
            {
                Logger.Warn("设置要求尽量忽略文件检查，这可能会保留有误的文件");
                Result = Result.Where(f => { if (FileUtils.Exists(f.LocalPath)) { Logger.Warn($"跳过下载的支持库文件：{f.LocalPath}"); return false; } else { return true; } }).ToList();
            }

            return Result;
        }
        /// <summary>
    /// 将 McLibToken 列表转换为 NetFile。
    /// </summary>
        public static List<ModNet.NetFile> McLibNetFilesFromTokens(List<McLibEntry> Libs, string CustomMcFolder = null)
        {
            CustomMcFolder = CustomMcFolder ?? McFolderSelected;
            var Result = new List<ModNet.NetFile>();
            // 获取
            foreach (McLibEntry Token in Libs)
            {
                // 检查器
                FileChecker Checker;
                if (Token.Name.ContainsIgnoreCase("labymod"))
                {
                    Checker = new FileChecker(); // 不检查 LabyMod 的文件，它们提供的文件校验信息是错的（#3225）
                }
                else
                {
                    Checker = new FileChecker() { ActualSize = Token.Size == 0L ? -1 : Token.Size, Hash = Token.SHA1 };
                }
                // URL
                var Urls = new List<string>();
                if (Token.Url is null && Token.Name == "net.minecraftforge:forge:universal") // 特判修复 Forge 部分 universal 文件缺失 URL（#5455）
                {
                    Token.Url = "https://maven.minecraftforge.net" + Token.LocalPath.Replace(CustomMcFolder + "libraries", "").Replace(@"\", "/");
                }
                if (Token.Url is not null)
                {
                    // 获取 URL 的真实地址
                    Urls.Add(Token.Url);
                    if (Token.Url.Contains("launcher.mojang.com/v1/objects") || Token.Url.Contains("client.txt") || Token.Url.Contains(".tsrg"))
                    {
                        Urls.AddRange(ModDownload.DlSourceLauncherOrMetaGet(Token.Url)); // Mappings（#4425）
                    }
                    if (Token.Url.Contains("maven"))
                    {
                        string BmclapiUrl = Token.Url.Replace(Token.Url.Substring(0, Token.Url.IndexOfF("maven")), "https://bmclapi2.bangbang93.com/").Replace("maven.fabricmc.net", "maven").Replace("maven.minecraftforge.net", "maven").Replace("maven.neoforged.net/releases", "maven");
                        if (ModDownload.DlSourcePreferMojang)
                        {
                            Urls.Add(BmclapiUrl); // 官方源优先
                        }
                        else
                        {
                            Urls.Insert(0, BmclapiUrl);
                        } // 镜像源优先
                    }
                }
                if (Token.LocalPath.Contains("transformer-discovery-service"))
                {
                    // Transformer 文件释放
                    if (!FileUtils.Exists(Token.LocalPath))
                        ModBase.ExtractResources(Token.LocalPath, "Transformer");
                    Logger.Warn("已自动释放 Transformer Discovery Service");
                    continue;
                }
                else if (Token.LocalPath.Contains(@"optifine\OptiFine"))
                {
                    // OptiFine 主 Jar
                    string OptiFineBase = Token.LocalPath.Replace(CustomMcFolder + @"libraries\optifine\OptiFine\", "").Split("_")[0] + "/" + PathUtils.GetLastPart(Token.LocalPath).Replace("-", "_");
                    OptiFineBase = "/maven/com/optifine/" + OptiFineBase;
                    if (OptiFineBase.Contains("_pre"))
                        OptiFineBase = OptiFineBase.Replace("com/optifine/", "com/optifine/preview_");
                    Urls.Add("https://bmclapi2.bangbang93.com" + OptiFineBase);
                }
                else if (Urls.Count <= 2)
                {
                    // 普通文件
                    Urls.AddRange(ModDownload.DlSourceLibraryGet("https://libraries.minecraft.net" + Token.LocalPath.Replace(CustomMcFolder + "libraries", "").Replace(@"\", "/")));
                }
                Result.Add(new ModNet.NetFile(Urls.Distinct(), Token.LocalPath, Checker));
            }
            // 去重并返回
            return Result.DistinctBy(a => a.LocalPath).ToList();
        }
        /// <summary>
    /// 获取对应的支持库文件地址。
    /// </summary>
    /// <param name="Original">原始地址，如 com.mumfrey:liteloader:1.12.2-SNAPSHOT。</param>
    /// <param name="WithHead">是否包含 Lib 文件夹头部，若不包含，则会类似以 com\xxx\ 开头。</param>
        public static string McLibGet(string Original, bool WithHead = true, bool IgnoreLiteLoader = false, string CustomMcFolder = null)
        {
            CustomMcFolder = CustomMcFolder ?? McFolderSelected;
            // 有时候原始的有四段，例如 net.minecraftforge:forge:1.21.4-54.0.34:client
            // 文件夹名应该忽略最后一段（client），但文件名需要保留
            // 参见 #7306（以及 #5376 也是因为这里没有处理好导致的）
            string[] Splited = Original.Split(":");
            string Result = (WithHead ? CustomMcFolder + @"libraries\" : "") + $@"{Splited[0].Replace(".", @"\")}\{Splited[1]}\{Splited[2]}\{Splited.Skip(1).Join("-")}.jar";
            return Result;
        }

        /// <summary>
    /// 检查设置，是否应当忽略文件检查？
    /// </summary>
        public static object ShouldIgnoreFileCheck(McInstance Instance)
        {
            return Settings.Get<bool>("VersionAdvanceAssetsV2", Instance: Instance) || Settings.Get<int>("VersionAdvanceAssets", Instance: Instance) == 2;
        }

        #endregion

        #region 资源文件（Assets）

        // 获取索引
        /// <summary>
    /// 获取某版本资源文件索引的对应 Json 项，详见版本 Json 中的 assetIndex 项。失败会抛出异常。
    /// </summary>
        public static JToken McAssetsGetIndex(McInstance Instance, bool ReturnLegacyOnError = false, bool CheckURLEmpty = false)
        {
            string AssetsName;
            try
            {
                while (true)
                {
                    var Index = Instance.JsonObject["assetIndex"];
                    if (Index is not null && Index["id"] is not null)
                        return Index;
                    if (Instance.JsonObject["assets"] is not null)
                        AssetsName = Instance.JsonObject["assets"].ToString();
                    // 该层版本 json 可能没有 assetIndex（Index 为 null），直接取 Index["url"]
                    // 会抛 NRE 并被下面的空 catch 吞掉，从而错误回退到 legacy 内置索引。
                    if (CheckURLEmpty && Index is not null && Index["url"] is not null)
                        return Index;
                    // 下一个版本
                    if (string.IsNullOrEmpty(Instance.InheritName))
                        break;
                    Instance = new McInstance(McFolderSelected + @"versions\" + Instance.InheritName);
                }
            }
            catch (Exception ex)
            {
                // 原实现是空 catch：任何异常（含上面的 NRE）都被静默吞掉，
                // 直接落到 legacy 回退，日志里看不出为什么资源索引换了内容。
                Logger.Warn(ex, "获取资源文件索引信息失败");
            }
            // 无法获取到下载地址
            if (ReturnLegacyOnError)
            {
                // 返回 assets 文件名会由于没有下载地址导致全局失败
                // If AssetsName IsNot Nothing AndAlso AssetsName <> "legacy" Then
                // Log("[Minecraft] 无法获取资源文件索引下载地址，使用 assets 项提供的资源文件名：" & AssetsName)
                // Return ("{""id"": """ & AssetsName & """}").DeserializeJson()
                // Else
                Logger.Info("无法获取资源文件索引下载地址，使用默认的 legacy 下载地址");
                return @"{
                ""id"": ""legacy"",
                ""sha1"": ""c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729"",
                ""size"": 134284,
                ""url"": ""https://launchermeta.mojang.com/mc-staging/assets/legacy/c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729/legacy.json"",
                ""totalSize"": 111220701
            }".DeserializeJson();
            }
            // End If
            else
            {
                throw new Exception("该版本不存在资源文件索引信息");
            }
        }
        /// <summary>
    /// 获取某版本资源文件索引名，优先使用 assetIndex，其次使用 assets。失败会返回 legacy。
    /// </summary>
        public static string McAssetsGetIndexName(McInstance Instance)
        {
            try
            {
                while (true)
                {
                    if (Instance.JsonObject["assetIndex"] is not null && Instance.JsonObject["assetIndex"]["id"] is not null)
                    {
                        return Instance.JsonObject["assetIndex"]["id"].ToString();
                    }
                    if (Instance.JsonObject["assets"] is not null)
                    {
                        return Instance.JsonObject["assets"].ToString();
                    }
                    if (string.IsNullOrEmpty(Instance.InheritName))
                        break;
                    Instance = new McInstance(McFolderSelected + @"versions\" + Instance.InheritName);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "获取资源文件索引名失败");
            }
            return "legacy";
        }

        // 获取列表
        private struct McAssetsToken
        {
            /// <summary>
        /// 文件的完整本地路径。
        /// </summary>
            public string LocalPath;
            /// <summary>
        /// Json 中书写的源路径。例如 minecraft/sounds/mob/stray/death2.ogg 。
        /// </summary>
            public string SourcePath;
            /// <summary>
        /// 文件大小。若无有效数据即为 0。
        /// </summary>
            public long Size;
            /// <summary>
        /// 文件的 Hash 校验码。
        /// </summary>
            public string Hash;

            public override string ToString()
            {
                return StringUtils.FormatByteSize(Size) + " | " + LocalPath;
            }
        }
        /// <summary>
    /// 获取 Minecraft 的资源文件列表。失败会抛出异常。
    /// </summary>
        private static List<McAssetsToken> McAssetsListGet(McInstance Instance)
        {
            string IndexName = McAssetsGetIndexName(Instance);
            try
            {

                // 初始化
                if (!FileUtils.Exists($@"{McFolderSelected}assets\indexes\{IndexName}.json"))
                    throw new FileNotFoundException("未找到 Asset Index", McFolderSelected + @"assets\indexes\" + IndexName + ".json");
                var Result = new List<McAssetsToken>();
                JObject Json = (JObject)FileUtils.ReadAsJson($@"{McFolderSelected}assets\indexes\{IndexName}.json");

                // 读取列表
                foreach (JProperty File in Json["objects"].Children())
                {
                    string LocalPath;
                    if (Json["map_to_resources"] is not null && Json["map_to_resources"].ToObject<bool>())
                    {
                        // Remap
                        LocalPath = $@"{Instance.PathIndie}resources\{File.Name}";
                    }
                    else if (Json["virtual"] is not null && Json["virtual"].ToObject<bool>())
                    {
                        // Virtual
                        LocalPath = $@"{McFolderSelected}assets\virtual\legacy\{File.Name}";
                    }
                    else
                    {
                        // 正常
                        LocalPath = $@"{McFolderSelected}assets\objects\{Strings.Left(File.Value["hash"].ToString(), 2)}\{File.Value["hash"]}";
                    }
                    Result.Add(new McAssetsToken()
                    {
                        LocalPath = LocalPath.Replace("/", @"\"),
                        SourcePath = File.Name,
                        Hash = File.Value["hash"].ToString(),
                        Size = Conversions.ToLong(File.Value["size"].ToString())
                    });
                }
                return Result;
            }

            catch (Exception ex)
            {
                Logger.Warn(ex, $"获取资源文件列表失败：{IndexName}");
                throw;
            }
        }

        /// <summary>
    /// 获取版本资源文件所对应的 NetFile。
    /// </summary>
        public static List<ModNet.NetFile> McAssetsFixList(McInstance Instance, bool CheckHash, [Optional] ref ModLoader.LoaderBase ProgressFeed)
        {
            // 如果需要检查 Hash，则留到下载时处理，以借助多线程加快检查速度
            if (CheckHash)
            {
                return McAssetsListGet(Instance).Select((Token) => new ModNet.NetFile(ModDownload.DlSourceAssetsGet($"https://resources.download.minecraft.net/{Strings.Left(Token.Hash, 2)}/{Token.Hash}"), LocalPath: Token.LocalPath, Checker: new FileChecker() { ActualSize = Token.Size == 0L ? -1 : Token.Size, Hash = Token.Hash })).ToList();
            }
            // 如果不检查 Hash，则立即处理
            var Result = new List<ModNet.NetFile>();
            List<McAssetsToken> AssetsList;
            try
            {
                AssetsList = McAssetsListGet(Instance);
                McAssetsToken Token;
                if (ProgressFeed is not null)
                    ProgressFeed.Progress = 0.04d;
                for (int i = 0, loopTo = AssetsList.Count - 1; i <= loopTo; i++)
                {
                    // 初始化
                    Token = AssetsList[i];
                    if (ProgressFeed is not null)
                        ProgressFeed.Progress = 0.05d + 0.94d * i / AssetsList.Count;
                    // 检查文件是否存在
                    var File = FileUtils.GetInfo(Token.LocalPath);
                    if (File.Exists && (Token.Size == 0L || Token.Size == File.Length))
                        continue;
                    // 文件不存在，添加下载
                    Result.Add(new ModNet.NetFile(ModDownload.DlSourceAssetsGet($"https://resources.download.minecraft.net/{Strings.Left(Token.Hash, 2)}/{Token.Hash}"), Token.LocalPath, new FileChecker() { ActualSize = Token.Size == 0L ? -1 : Token.Size, Hash = Token.Hash }));
                }
            }
            catch (Exception ex)
            {
                // 索引缺失/损坏时原实现吞掉异常并返回空列表，加载器会把「一个文件都没下」
                // 当成成功完成（表现为资源、语言包下载不完整，补全却提示成功）。
                // 与 CheckHash=true 分支的语义保持一致：让加载器明确失败，暴露真实原因。
                Logger.Warn(ex, "获取版本缺失的资源文件下载列表失败");
                throw new Exception("获取版本缺失的资源文件下载列表失败，资源文件可能不完整", ex);
            }
            if (ProgressFeed is not null)
                ProgressFeed.Progress = 0.99d;
            return Result;
        }

        #endregion

        /// <summary>
    /// 发送 Minecraft 更新提示。
    /// </summary>
        public static void McDownloadClientUpdateHint(string VersionName, JObject Json)
        {
            try
            {

                // 获取对应版本
                JToken Version = null;
                foreach (var Token in Json["versions"])
                {
                    if (Token["id"] is not null && (Token["id"].ToString() ?? "") == (VersionName ?? ""))
                    {
                        Version = Token;
                        break;
                    }
                }
                // 进行提示
                if (Version is null)
                    return;
                DateTime Time = (DateTime)Version["releaseTime"];
                string MsgBoxText = $"新版本：{VersionName}{Constants.vbCrLf}" + ((DateTime.Now - Time).TotalDays > 1d ? "更新时间：" + Time.ToString() : "更新于：" + StringUtils.FormatTimeSpan(Time - DateTime.Now, false));
                int MsgResult = ModMain.MyMsgBox(MsgBoxText, "Minecraft 更新提示", "确定", ModDownloadPolicy.RestrictMcDownload ? "" : "下载", (DateTime.Now - Time).TotalHours > 3d ? "更新日志" : "", Button3Action: () => ModDownloadLib.McUpdateLogShow(Version));
                // 弹窗结果
                if (MsgResult == 2)
                {
                    // 下载
                    ModBase.RunInUi(() =>
        {
            PageDownloadInstall.VersionWaitingSelect = VersionName;
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall);
        });
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, $"Minecraft 更新提示发送失败（{VersionName ?? "Nothing"}）");
            }
        }

        /// <summary>
    /// 打码字符串中的 AccessToken。
    /// </summary>
        public static string FilterAccessToken(string Raw, char FilterChar)
        {
            // 打码 "accessToken " 后的内容
            if (Raw.Contains("accessToken "))
            {
                foreach (var Token in Raw.RegexSearch("(?<=accessToken ([^ ]{5}))[^ ]+(?=[^ ]{5})"))
                    Raw = Raw.Replace(Token, new string(FilterChar, Token.Count()));
            }
            // 打码当前登录的结果
            string AccessToken = ModLaunch.McLoginLoader.Output.AccessToken;
            if (AccessToken is not null && AccessToken.Length >= 10 && Raw.ContainsIgnoreCase(AccessToken) && (ModLaunch.McLoginLoader.Output.Uuid ?? "") != (ModLaunch.McLoginLoader.Output.AccessToken ?? "")) // UUID 和 AccessToken 一样则不打码
            {
                Raw = Raw.Replace(AccessToken, Strings.Left(AccessToken, 5) + new string(FilterChar, AccessToken.Length - 10) + Strings.Right(AccessToken, 5));
            }
            return Raw;
        }
        /// <summary>
    /// 打码字符串中的 Windows 用户名。
    /// </summary>
        public static string FilterUserName(string Raw, char FilterChar)
        {
            if (Raw.Contains(@":\Users\"))
            {
                Raw = System.Text.RegularExpressions.Regex.Replace(Raw, @"(?<=:\\Users\\)[^\\]+", Token => new string(FilterChar, Token.Length));
            }
            if (Raw.Contains(":/Users/"))
            {
                Raw = System.Text.RegularExpressions.Regex.Replace(Raw, "(?<=:/Users/)[^/]+", Token => new string(FilterChar, Token.Length));
            }
            return Raw;
        }

    }
}