using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    /// <summary>
/// 版本修改页：完整还原已安装组件后，复用安装页的选择交互。
/// </summary>
    public class PageDownloadModify : PageDownloadInstall
    {
        private MyTextBox _subscribedTextSelectName;

        internal new MyIconButton BtnMinecraftRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnMinecraftRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnMinecraftRestore != null)
                {
                    base.BtnMinecraftRestore.Click -= RestoreComponent;
                }

                base.BtnMinecraftRestore = value;
                if (base.BtnMinecraftRestore != null)
                {
                    base.BtnMinecraftRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyIconButton BtnOptiFineRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnOptiFineRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnOptiFineRestore != null)
                {
                    base.BtnOptiFineRestore.Click -= RestoreComponent;
                }

                base.BtnOptiFineRestore = value;
                if (base.BtnOptiFineRestore != null)
                {
                    base.BtnOptiFineRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyIconButton BtnForgeRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnForgeRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnForgeRestore != null)
                {
                    base.BtnForgeRestore.Click -= RestoreComponent;
                }

                base.BtnForgeRestore = value;
                if (base.BtnForgeRestore != null)
                {
                    base.BtnForgeRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyIconButton BtnNeoForgeRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnNeoForgeRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnNeoForgeRestore != null)
                {
                    base.BtnNeoForgeRestore.Click -= RestoreComponent;
                }

                base.BtnNeoForgeRestore = value;
                if (base.BtnNeoForgeRestore != null)
                {
                    base.BtnNeoForgeRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyIconButton BtnFabricRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnFabricRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnFabricRestore != null)
                {
                    base.BtnFabricRestore.Click -= RestoreComponent;
                }

                base.BtnFabricRestore = value;
                if (base.BtnFabricRestore != null)
                {
                    base.BtnFabricRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyIconButton BtnFabricApiRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnFabricApiRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnFabricApiRestore != null)
                {
                    base.BtnFabricApiRestore.Click -= RestoreComponent;
                }

                base.BtnFabricApiRestore = value;
                if (base.BtnFabricApiRestore != null)
                {
                    base.BtnFabricApiRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyIconButton BtnOptiFabricRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnOptiFabricRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnOptiFabricRestore != null)
                {
                    base.BtnOptiFabricRestore.Click -= RestoreComponent;
                }

                base.BtnOptiFabricRestore = value;
                if (base.BtnOptiFabricRestore != null)
                {
                    base.BtnOptiFabricRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyIconButton BtnLiteLoaderRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnLiteLoaderRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnLiteLoaderRestore != null)
                {
                    base.BtnLiteLoaderRestore.Click -= RestoreComponent;
                }

                base.BtnLiteLoaderRestore = value;
                if (base.BtnLiteLoaderRestore != null)
                {
                    base.BtnLiteLoaderRestore.Click += RestoreComponent;
                }
            }
        }

        internal new MyCard CardMinecraft
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.CardMinecraft;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.CardMinecraft != null)
                {
                    base.CardMinecraft.MouseLeftButtonUp -= CardMinecraft_MouseLeftButtonUp;
                }

                base.CardMinecraft = value;
                if (base.CardMinecraft != null)
                {
                    base.CardMinecraft.MouseLeftButtonUp += CardMinecraft_MouseLeftButtonUp;
                }
            }
        }

        internal new MyTextBox TextSelectName
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.TextSelectName;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (_subscribedTextSelectName is not null)
                {
                    _subscribedTextSelectName.TextChanged -= OnNameTextChanged;
                    MyTextBox.ValidateChanged -= OnValidateChanged;
                    _subscribedTextSelectName.TextChanged -= OnOriginalNameTextChanged;
                }

                base.TextSelectName = value;
                _subscribedTextSelectName = base.TextSelectName;

                if (_subscribedTextSelectName is not null)
                {
                    _subscribedTextSelectName.TextChanged += OnNameTextChanged;
                    MyTextBox.ValidateChanged += OnValidateChanged;
                    _subscribedTextSelectName.TextChanged += OnOriginalNameTextChanged;
                }
            }
        }

        internal new MyIconButton BtnOriginalNameRestore
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return base.BtnOriginalNameRestore;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                if (base.BtnOriginalNameRestore != null)
                {
                    base.BtnOriginalNameRestore.Click -= BtnOriginalNameRestore_Click;
                }

                base.BtnOriginalNameRestore = value;
                if (base.BtnOriginalNameRestore != null)
                {
                    base.BtnOriginalNameRestore.Click += BtnOriginalNameRestore_Click;
                }
            }
        }

        private ModLoader.LoaderTask<ModMinecraft.McInstance, object> InitLoader;
        private ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>> ForgeVersionLoader;
        private McComponents InitialTarget; // 进入时的组件快照
                                            /// <summary>
    /// 原版的版本号是否可靠（不是通过 json 模糊分析）。
    /// </summary>
        private bool IsVanillaVersionReliable;
        private string OriginalName;
        private string CustomLogo;
        /// <summary>
    /// 开始修改 MC 版本时，记录当前选择的组件，以在修改 MC 版本后尝试自动选中相同组件。
    /// </summary>
        private readonly Dictionary<MyLoading, string> PendingComponents = new Dictionary<MyLoading, string>();
        private bool IsRestoringComponents;

        public PageDownloadModify()
        {
            this.Unloaded += (_, __) => PageUnloaded();
        }

        public override void LoaderInit() // 父级 Handles Me.Initialized
        {
            ControlInit();
            InitLoader = new ModLoader.LoaderTask<ModMinecraft.McInstance, object>("初始化版本修改", LoadInstance);
            this.PageLoaderInit(this.LoadMinecraft, this.PanLoad, this.PanAllBack, (FrameworkElement)null, InitLoader, this.LoadInstanceFinished, () => PageInstanceLeft.Instance);
        }
        private bool _Init_IsLoad = false;
        public override void Init() // 父级 Handles Me.Loaded
        {
            this.PanBack.ScrollToHome();
            if (_Init_IsLoad)
                this.PageLoaderRestart();
            _Init_IsLoad = true;
        }
        private void PageUnloaded()
        {
            // InitLoader 在 LoaderInit() 里才创建；若页面尚未初始化（或初始化被跳过）就被卸载，
            // 这里会是 null。VB 原版在构造函数里创建，所以不会空引用；迁移后创建位置改变，
            // 必须判空，否则切换页面时必抛 NullReferenceException（2026-11-11 修复）。
            InitLoader?.Cancel();
        }
        private JObject _LoadInstance_OptiFineHashes = (JObject)FileUtils.ReadAsJson(@"Minecraft\OptiFineHashes.json", @type: typeof(Resource)); // JSON 内容：{"SHA1": "原始文件名", ...}

        /// <summary>
    /// 读取或初始化版本的安装清单。
    /// </summary>
        private void LoadInstance(ModLoader.LoaderTask<ModMinecraft.McInstance, object> Task)
        {
            if (Task.Input is null)
                throw new InvalidOperationException("缺少待修改的 Minecraft 实例。");
            if (Task.Input.Config is null || string.IsNullOrWhiteSpace(Task.Input.PathIndie) || string.IsNullOrWhiteSpace(Task.Input.PathVersion))
                throw new InvalidOperationException("待修改的 Minecraft 实例信息不完整。");

            // 旧状态清理
            ModBase.RunInUiWait(() =>
        {
            ModAnimation.AniStop("FrmDownloadInstall SelectPageSwitch");
            IsSelectNameEdited = false;
            ClearSelected();
            PendingComponents.Clear();
            InitialTarget = null;
            IsInSelectPage = false;
            this.PanAllBack.IsHitTestVisible = false;
        });
            Task.Input.Config.DiscardCache();

            // 获取 Components
            var Manifest = Configs.McInstallManifest.Get(Task.Input.Config);
            if (Manifest is not null && Manifest.SchemaVersion > Versions.McInstallManifestVersion)
            {
                Logger.Warn($"安装清单由更新版本的 PCL Next 创建（清单版本：{Manifest.SchemaVersion}，当前版本：{Versions.McInstallManifestVersion}）");
                throw new Exception($"更新 PCL Next 后才能修改它！点击【设置 → 其他 → 检查更新】以更新。");
            }
            if (Manifest is not null)
            {
                var LatestManifest = Manifest.RefreshModComponents(Task.Input.PathIndie);
                if (!ReferenceEquals(LatestManifest, Manifest))
                {
                    Manifest = LatestManifest;
                    Configs.McInstallManifest.Set(Manifest, Task.Input.Config);
                    Task.Input.Config.Save();
                }
            }
            McComponents Components;
            string OptiFineVersion = null;
            if (Manifest is not null)
            {
                // 有清单：从清单读取
                Components = Manifest.Components;
                if (Components is null)
                    throw new Exception("安装清单缺少组件信息，无法还原已安装组件。");
                IsVanillaVersionReliable = true;
            }
            else
            {
                // 无清单：通过版本 JSON 获取
                var VersionInfo = new ModMinecraft.McInstance(Task.Input.PathVersion).Version;
                if (VersionInfo.VanillaName == "Unknown")
                    throw new Exception("无法确定 Minecraft 版本，无法修改");
                var UnknownLoaders = VersionInfo.ModLoaderVersions.Where(Loader => string.IsNullOrWhiteSpace(Loader.Value) || Loader.Value == "未知版本").Select(Loader => Loader.Key.ToString()).ToList();
                if (UnknownLoaders.Any())
                    throw new Exception($"无法确定 {UnknownLoaders.Join("、")} 的版本，无法修改");
                Components = new McComponents(VersionInfo.VanillaName, VersionInfo.ModLoaderVersions);
                OptiFineVersion = VersionInfo.OptiFine;
                IsVanillaVersionReliable = VersionInfo.Reliable;
            }
            string MinecraftVersion = Components.MinecraftVersion;
            if (string.IsNullOrWhiteSpace(MinecraftVersion))
                throw new Exception("无法确定当前 Minecraft 版本，请检查版本文件");
            // 等待子列表加载器完成获取
            var LoaderList = new Dictionary<string, ModLoader.LoaderBase>() { { "Minecraft", ModDownload.DlClientListLoader }, { "OptiFine", ModDownload.DlOptiFineListLoader }, { "LiteLoader", ModDownload.DlLiteLoaderListLoader }, { "NeoForge", ModDownload.DlNeoForgeListLoader }, { "Fabric", ModDownload.DlFabricListLoader }, { "Fabric API", ModDownload.DlFabricApiLoader }, { "OptiFabric", ModDownload.DlOptiFabricLoader } };
            ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>> ForgeLoader = null;
            if (McVersion.IsStandardFormat(MinecraftVersion))
            {
                ForgeLoader = new ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>("DlForgeVersion " + MinecraftVersion, ModDownload.DlForgeVersionMain);
                LoaderList.Add("Forge", ForgeLoader);
            }
            foreach (var Loader in LoaderList.Values)
                Loader.Start(ReferenceEquals(Loader, ForgeLoader) ? MinecraftVersion : null, Task.IsForceRestarting);
            do
            {
                if (Task.IsCanceled)
                    return;
                foreach (var Pair in LoaderList)
                {
                    var Loader = Pair.Value;
                    if (Loader.State == ModBase.LoadState.Failed)
                    {
                        if (ReferenceEquals(Loader, ForgeLoader) && Loader.Error.Message == "无")
                            continue; // Forge 无版本时不报错
                        throw new Exception($"获取 {Pair.Key} 版本列表失败，请稍后再试，或使用 VPN 改善网络环境", Loader.Error);
                    }
                    else if (Loader.State == ModBase.LoadState.Canceled)
                    {
                        throw new Exception($"获取 {Pair.Key} 版本列表被中断，请稍后再试，或使用 VPN 改善网络环境");
                    }
                }
                if (LoaderList.Values.All(Loader => Loader.State == ModBase.LoadState.Finished || Loader.State == ModBase.LoadState.Failed))
                    break;
                Task.Progress = 0.7d * LoaderList.Values.Average(Loader => Loader.Progress);
                Thread.Sleep(10);
            }
            while (true);
            if (Task.IsCanceled)
                return;
            Task.Progress = 0.7d;
            // Minecraft
            MinecraftVersions = (JArray)ModDownload.DlClientListLoader.Output.Value["versions"].DeepClone();
            VanillaData = MinecraftVersions.OfType<JObject>().FirstOrDefault(v => (v["id"]?.ToString().Replace("∞", "infinite") ?? "") == (MinecraftVersion.Replace("∞", "infinite") ?? ""));
            if (VanillaData is null)
                throw new Exception($"Minecraft 列表中没有已安装的 {MinecraftVersion}");
            VanillaData["id"] = MinecraftVersion;
            // OptiFine
            if (Manifest is not null)
            {
                SelectedOptiFine = Components.OptiFineEntry?.ToObject<ModDownload.DlOptiFineListEntry>();
                if (SelectedOptiFine is not null && !ModDownload.DlOptiFineListLoader.Output.Value.Any(v => (v.FileName ?? "") == (SelectedOptiFine.FileName ?? "")))
                {
                    // 如果 OptiFine 是 pre 版，可能只在 BMCLAPI 下有，官方源删了。如果是这种缺失数据的情况，换用 BMCLAPI 源作为输出
                    if (ModDownload.DlOptiFineListBmclapiLoader.State != ModBase.LoadState.Finished)
                        ModDownload.DlOptiFineListBmclapiLoader.WaitForExit();
                    ModDownload.DlOptiFineListLoader.Output = ModDownload.DlOptiFineListBmclapiLoader.Output;
                }
            }
            else if (OptiFineVersion is not null && OptiFineVersion != "未知版本")
            {
                SelectedOptiFine = ModDownload.DlOptiFineListLoader.Output.Value.FirstOrDefault(v => (v.Inherit ?? "") == (MinecraftVersion ?? "") && (v.InstanceName.AfterLast("-OptiFine_").Replace("HD_U_", "") ?? "") == (OptiFineVersion.Replace("HD_U_", "") ?? ""));
                if (SelectedOptiFine is null)
                    throw new Exception($"OptiFine 列表中没有已安装的 {OptiFineVersion}");
            }
            // Forge
            if (Components.ModLoaderVersions.ContainsKey(ModLoaders.Forge))
            {
                SelectedForge = ForgeLoader?.Output?.FirstOrDefault(v => (v.Inherit ?? "") == (MinecraftVersion ?? "") && (v.VersionName ?? "") == (Components.ModLoaderVersions[ModLoaders.Forge] ?? "") && v.Category != "universal" && v.Category != "client" && (SelectedOptiFine is null || IsOptiFineSuitForForge(SelectedOptiFine, v)));
                if (SelectedForge is null)
                    throw new Exception($"Forge 列表中没有已安装的 {Components.ModLoaderVersions[ModLoaders.Forge]}");
            }
            // NeoForge
            if (Components.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge))
            {
                SelectedNeoForge = ModDownload.DlNeoForgeListLoader.Output.Value.FirstOrDefault(v => (v.Inherit ?? "") == (MinecraftVersion ?? "") && (v.VersionName ?? "") == (Components.ModLoaderVersions[ModLoaders.NeoForge] ?? ""));
                if (SelectedNeoForge is null)
                    throw new Exception($"NeoForge 列表中没有已安装的 {Components.ModLoaderVersions[ModLoaders.NeoForge]}");
            }
            // Fabric
            if (Components.ModLoaderVersions.ContainsKey(ModLoaders.Fabric))
            {
                var fabricLoader = ModDownload.DlFabricListLoader.Output.Value["loader"];
                var fabricGame = ModDownload.DlFabricListLoader.Output.Value["game"];
                if (fabricLoader is null || fabricGame is null)
                    throw new Exception($"Fabric 列表中没有已安装的 {Components.ModLoaderVersions[ModLoaders.Fabric]}");
                var FabricEntry = fabricLoader.OfType<JObject>().FirstOrDefault(v => (v["version"]?.ToString() ?? "") == (Components.ModLoaderVersions[ModLoaders.Fabric] ?? ""), v => (v["version"]?.ToString().Replace("+build", "") ?? "") == (Components.ModLoaderVersions[ModLoaders.Fabric] ?? ""));
                if (FabricEntry is null || !fabricGame.OfType<JObject>().Any(v => (v["version"]?.ToString() ?? "") == (MinecraftVersion.Replace("∞", "infinite").Replace("Combat Test 7c", "1.16_combat-3") ?? "")))
                {
                    throw new Exception($"Fabric 列表中没有已安装的 {Components.ModLoaderVersions[ModLoaders.Fabric]}");
                }
                SelectedFabric = FabricEntry["version"]?.ToString();
            }
            // LiteLoader
            if (Components.ModLoaderVersions.ContainsKey(ModLoaders.LiteLoader))
            {
                SelectedLiteLoader = ModDownload.DlLiteLoaderListLoader.Output.Value.FirstOrDefault(v => (v.Inherit ?? "") == (MinecraftVersion ?? "") && (v.JsonToken?["version"]?.ToString() ?? "") == (Components.ModLoaderVersions[ModLoaders.LiteLoader] ?? ""));
                if (SelectedLiteLoader is null)
                    throw new Exception($"LiteLoader 列表中没有已安装的 {Components.ModLoaderVersions[ModLoaders.LiteLoader]}");
            }
            // Fabric API
            if (Components.FabricApiVersion is not null)
            {
                SelectedFabricApi = ModDownload.DlFabricApiLoader.Output.FirstOrDefault(v => ((v.Version ?? "") == (Components.FabricApiVersion ?? "") || (v.Id ?? "") == (Components.FabricApiVersion ?? "") || (v.Display ?? "") == (Components.FabricApiVersion ?? "")) && IsFabricApiCompatible(v, MinecraftVersion));
                if (SelectedFabricApi is null)
                    throw new Exception($"Fabric API 列表中没有已安装的 {Components.FabricApiVersion}");
            }
            // OptiFabric
            if (Components.OptiFabricVersion is not null)
            {
                SelectedOptiFabric = ModDownload.DlOptiFabricLoader.Output.FirstOrDefault(v => ((v.Version ?? "") == (Components.OptiFabricVersion ?? "") || (v.Id ?? "") == (Components.OptiFabricVersion ?? "") || (v.Display ?? "") == (Components.OptiFabricVersion ?? "")) && IsOptiFabricCompatible(v, MinecraftVersion));
                if (SelectedOptiFabric is null)
                    throw new Exception($"OptiFabric 列表中没有已安装的 {Components.OptiFabricVersion}");
            }

            // 扫描尚未识别的 Mod 组件
            var ManagedFiles = new List<McInstallManifest.ManagedFile>();
            if (Manifest is not null)
                ManagedFiles.AddRange(Manifest.ManagedFiles);

            #region 从 mods 文件夹中获取以 Mod 形式安装的组件信息

            // 获取 mods 文件夹下的 jar 文件与 SHA1
            var Mods = new Lazy<List<(FileInfo File, string Sha1)>>(() =>
    {
        string ModsFolder = Task.Input.PathIndie + @"mods\";
        var Files = DirectoryUtils.EnumerateFiles(ModsFolder, searchPattern: "*.jar").ToList();
        if (SelectedLiteLoader is not null) // LiteLoader 的 OptiFine 安装目录
        {
            Files.AddRange(DirectoryUtils.EnumerateFiles(Path.Combine(ModsFolder, MinecraftVersion), searchPattern: "*.jar"));
        }
        var Entries = new List<(FileInfo File, string Sha1)>();
        if (Files.Count == 0)
            return Entries;
        foreach (var FilePath in Files)
        {
            if (Task.IsCanceled)
                return Entries;
            var File = FileUtils.GetInfo(FilePath);
            Entries.Add((File, new LocalResourceFile(File).ModrinthHash));
            Task.Progress = 0.7d + 0.28d * Entries.Count / Files.Count;
        }
        return Entries;
    });
            // Fabric API
            if (SelectedFabric is not null && SelectedFabricApi is null)
            {
                var FabricApiList = ModDownload.DlFabricApiLoader.Output.Where(v => IsFabricApiCompatible(v, MinecraftVersion)).ToList();
                var Matches = new List<(FileInfo File, string Sha1, ResourceVersion Version)>();
                foreach (var Entry in Mods.Value)
                {
                    var Match = FabricApiList.FirstOrDefault(v => (v.Hash ?? "") == (Entry.Sha1 ?? ""));
                    if (Match is null)
                        continue;
                    Matches.Add((Entry.File, Entry.Sha1, Match));
                }
                var SelectedFile = SelectMod("Fabric API", Matches);
                if (SelectedFile is not null)
                {
                    SelectedFabricApi = SelectedFile.Value.Version;
                    ManagedFiles.Add(new McInstallManifest.ManagedFile("mods/" + SelectedFile.Value.File.Name.Lower(), SelectedFile.Value.Sha1, SelectedFile.Value.File.LastWriteTimeUtc, "FabricApi"));
                }
            }
            if (Task.IsCanceled)
                return;
            // OptiFabric
            if (SelectedFabric is not null && SelectedOptiFabric is null)
            {
                var OptiFabricList = ModDownload.DlOptiFabricLoader.Output.Where(v => IsOptiFabricCompatible(v, MinecraftVersion)).ToList();
                bool HasMd5 = OptiFabricList.Any(v => (v.Hash ?? "").Length == 32); // CurseForge 的 OptiFabric 信息可能提供 SHA1 或 MD5
                var Matches = new List<(FileInfo File, string Sha1, ResourceVersion Version)>();
                foreach (var Entry in Mods.Value)
                {
                    var Match = OptiFabricList.FirstOrDefault(v => (v.Hash ?? "") == (Entry.Sha1 ?? ""));
                    if (Match is null && HasMd5)
                    {
                        string Md5 = CryptographyUtils.ComputeFileHash(Entry.File.FullName, CryptographyUtils.HashMethod.Md5);
                        Match = OptiFabricList.FirstOrDefault(v => (v.Hash ?? "") == (Md5 ?? ""));
                    }
                    if (Match is null)
                        continue;
                    Matches.Add((Entry.File, Entry.Sha1, Match));
                }
                var SelectedFile = SelectMod("OptiFabric", Matches);
                if (SelectedFile is not null)
                {
                    SelectedOptiFabric = SelectedFile.Value.Version;
                    ManagedFiles.Add(new McInstallManifest.ManagedFile("mods/" + SelectedFile.Value.File.Name.Lower(), SelectedFile.Value.Sha1, SelectedFile.Value.File.LastWriteTimeUtc, "OptiFabric"));
                }
            }
            if (Task.IsCanceled)
                return;
            // OptiFine
            var OptiFineFiles = new List<(FileInfo File, string Sha1, string OriginalName)>();
            if (SelectedOptiFine is null && (SelectedForge is not null || SelectedLiteLoader is not null || SelectedFabric is not null)) // OptiFine 只在有 Mod 加载器时才看在不在 mods 文件夹中
            {
                OptiFineFiles = Mods.Value.Select(Entry => (Entry.File, Entry.Sha1, _LoadInstance_OptiFineHashes[Entry.Sha1]?.ToString())).Where(Entry => ((dynamic)Entry).OriginalName is not null).ToList(); // 用本地文件的 SHA1 匹配下载时的原始文件名，再根据原始文件名取得下载源信息
                var Matches = new List<(FileInfo File, string Sha1, ModDownload.DlOptiFineListEntry Version)>();
                foreach (var Entry in OptiFineFiles) // 遍历 hash 匹配到了的本地文件
                {
                    var Match = ModDownload.DlOptiFineListLoader.Output.Value.FirstOrDefault(v => (v.Inherit ?? "") == (MinecraftVersion ?? "") && (v.FileName ?? "") == (Entry.OriginalName ?? "") && (SelectedForge is null || IsOptiFineSuitForForge(v, SelectedForge)));
                    if (Match is null)
                    {
                        ModMain.Hint($"无法从 OptiFine 列表还原 {Entry.File.Name} 对应的 {Entry.OriginalName}，将跳过该文件。");
                        continue;
                    }
                    Matches.Add((Entry.File, Entry.Sha1, Match));
                }
                var SelectedFile = SelectMod("OptiFine", Matches);
                if (SelectedFile is not null)
                {
                    SelectedOptiFine = SelectedFile.Value.Version;
                    string InstanceFolder = Path.GetFullPath(PathUtils.RemoveExtendedPrefix(Task.Input.PathIndie));
                    string SelectedPath = Path.GetFullPath(PathUtils.RemoveExtendedPrefix(SelectedFile.Value.File.FullName));
                    if (!PathUtils.IsParentOf(InstanceFolder, SelectedPath) || PathUtils.IsSame(InstanceFolder, SelectedPath))
                        throw new InvalidDataException($"OptiFine 文件不在当前实例目录中：{SelectedPath}");
                    string RelativePath = Path.GetRelativePath(InstanceFolder, SelectedPath).Replace(@"\", "/").Lower();
                    ManagedFiles.Add(new McInstallManifest.ManagedFile(RelativePath, SelectedFile.Value.Sha1, SelectedFile.Value.File.LastWriteTimeUtc, "OptiFine"));
                }
            }
            if (Task.IsCanceled)
                return;

            #endregion

            // 保存 Manifest
            if (IsVanillaVersionReliable)
            {
                Components = new McComponents(MinecraftVersion, Components.ModLoaderVersions, SelectedOptiFine is null ? null : JObject.FromObject(SelectedOptiFine), (SelectedFabricApi?.Version) ?? (SelectedFabricApi?.Id) ?? (SelectedFabricApi?.Display), (SelectedOptiFabric?.Version) ?? (SelectedOptiFabric?.Id) ?? (SelectedOptiFabric?.Display));
                if (Manifest is null || !Components.IsEquivalentTo(Manifest.Components) || !ManagedFiles.SequenceEqual(Manifest.ManagedFiles))
                {
                    Configs.McInstallManifest.Set(new McInstallManifest(Versions.McInstallManifestVersion, Components, DateTime.UtcNow, ManagedFiles), Task.Input.Config);
                    Task.Input.Config.Save();
                    Logger.Info($"已为 {Task.Input.Name} 更新安装清单");
                }
            }
            if (Task.IsCanceled)
                return;
            ForgeVersionLoader = ForgeLoader;
        }
        private (FileInfo File, string Sha1, T Version)? SelectMod<T>(string ComponentName, List<(FileInfo File, string Sha1, T Version)> Matches)
        {
            if (Matches.Count == 0)
                return default;
            if (Matches.Count == 1)
                return Matches[0];
            int? Choice = default;
            ModBase.RunInUiWait(() => Choice = ModMain.MyMsgBoxSelect(Matches.Select(Entry => (IMyRadio)new MyRadioBox() { Text = Entry.File.Name }), $"选择当前使用的 {ComponentName}"));
            if (!Choice.HasValue)
                throw new Exception($"发现了多个 {ComponentName}");
            return Matches[Choice.Value];
        }

        /// <summary>
    /// 初始化修改页
    /// </summary>
        private void LoadInstanceFinished(ModLoader.LoaderTask<ModMinecraft.McInstance, object> Task)
        {
            if (Task.Input is null || VanillaData is null)
            {
                Logger.Error(new InvalidOperationException("缺少实例或 Minecraft 版本数据，无法初始化修改页。"), "初始化修改页失败");
                return;
            }
            // 还原原版信息
            OriginalName = Task.Input.Name;
            CustomLogo = null;
            if (Conversions.ToBoolean(ModBase.ReadIni(Task.Input.PathVersion + @"PCL\Setup.ini", "LogoCustom", Conversions.ToString(false))))
            {
                CustomLogo = Task.Input.Logo.EndsWithF(@"PCL\Logo.png") ? Task.Input.PathVersion + @"PCL\Logo.png" : Task.Input.Logo;
            }
            var ItemElement = ModDownloadLib.McDownloadListItem(VanillaData, null, false);
            var Item = ItemElement?.Init();
            if (Item is null)
            {
                Logger.Error(new InvalidOperationException("无法初始化要显示的 Minecraft 版本列表项。"), "初始化修改页失败");
                return;
            }
            VanillaName = Item.Title;
            VanillaIcon = Item.Logo;
            this.LabMinecraftVersion.Text = VanillaName;
            TextSelectName.Text = OriginalName;
            IsSelectNameEdited = !string.Equals(OriginalName, GetSelectName(), StringComparison.Ordinal);
            TextSelectName.ValidateRules = new System.Collections.ObjectModel.Collection<ValidateType>() { new ValidateFolderName(ModMinecraft.McFolderSelected + "versions", IgnoreList: new List<string>() { OriginalName }) };
            // 修改页 UI
            CardMinecraft.Visibility = ModDownloadPolicy.RestrictMcDownload ? Visibility.Collapsed : Visibility.Visible;
            this.BtnBack.Visibility = Visibility.Collapsed;
            this.ImgLogo.Margin = new Thickness(15d, 15d, 0d, 0d);
            TextSelectName.Margin = new Thickness(56d, 16d, 16d, 0d);
            this.LabSelectName.Text = $"原名 :   {OriginalName}";
            UpdateOriginalNameVisibility();
            this.BtnStart.Text = "开始修改";
            this.BtnStart.Logo = ModMain.FrmInstanceLeft.ItemModify.Logo;
            AutoSelectedFabricApi = SelectedFabric is not null;
            AutoSelectedOptiFabric = SelectedFabric is not null && SelectedOptiFine is not null;
            if (ForgeVersionLoader is not null)
                this.LoadForge.State = ForgeVersionLoader;
            PrepareSelectPage();
            ReloadComponentLists();
            // 进入动画
            this.PanMinecraft.Visibility = Visibility.Collapsed;
            this.PanMinecraft.Opacity = 0d;
            this.PanMinecraft.RenderTransform = new TranslateTransform(-50, 0d);
            this.PanSelect.Opacity = 1d;
            this.PanSelect.RenderTransform = new TranslateTransform();
            this.PanBack.IsHitTestVisible = true;
            this.PanAllBack.IsHitTestVisible = true;
            InitialTarget = GetSelectedComponents();
            // 设置原始数据
            BtnMinecraftRestore.Tag = VanillaData;
            BtnOptiFineRestore.Tag = SelectedOptiFine;
            BtnForgeRestore.Tag = SelectedForge;
            BtnNeoForgeRestore.Tag = SelectedNeoForge;
            BtnFabricRestore.Tag = SelectedFabric;
            BtnFabricApiRestore.Tag = SelectedFabricApi;
            BtnOptiFabricRestore.Tag = SelectedOptiFabric;
            BtnLiteLoaderRestore.Tag = SelectedLiteLoader;
            this.ImgMinecraftOld.Source = VanillaIcon;
            this.LabMinecraftOld.Text = this.LabMinecraftVersion.Text;
            this.LabOptiFineOld.Text = SelectedOptiFine is null ? "无" : this.LabOptiFine.Text;
            this.LabForgeOld.Text = SelectedForge is null ? "无" : this.LabForge.Text;
            this.LabNeoForgeOld.Text = SelectedNeoForge is null ? "无" : this.LabNeoForge.Text;
            this.LabFabricOld.Text = SelectedFabric is null ? "无" : this.LabFabric.Text;
            this.LabLiteLoaderOld.Text = SelectedLiteLoader is null ? "无" : this.LabLiteLoader.Text;
            this.LabFabricApiOld.Text = SelectedFabricApi is null ? "无" : this.LabFabricApi.Text;
            this.LabOptiFabricOld.Text = SelectedOptiFabric is null ? "无" : this.LabOptiFabric.Text;
            RefreshComparisons();
        }

        public override string GetSelectLogo()
        {
            return CustomLogo ?? base.GetSelectLogo();
        }

        public override void ShowMinecraftVersionList()
        {
            if (IsInSelectPage)
            {
                // 把当前已选择的组件记录到 PendingComponents
                var Components = GetSelectedComponents();
                foreach (var Component in new[] { (this.LoadOptiFine, Components.OptiFineVersion), (this.LoadFabricApi, Components.FabricApiVersion), (this.LoadOptiFabric, Components.OptiFabricVersion), (this.LoadForge, SelectedForge?.VersionName), (this.LoadNeoForge, SelectedNeoForge?.VersionName), (this.LoadFabric, SelectedFabric), (this.LoadLiteLoader, SelectedLiteLoader?.JsonToken?["version"]?.ToString() ?? "") })
                {
                    // 保留尚未加载完成的组件选择
                    if (((dynamic)Component).Version is not null)
                        PendingComponents[((dynamic)Component).Loader] = ((dynamic)Component).Version;
                }
            }
            base.ShowMinecraftVersionList();
        }

        public override void ReloadSelected()
        {
            if (IsRestoringComponents)
                return;
            if (IsInSelectPage && VanillaName is not null && PendingComponents.Any())
            {
                IsRestoringComponents = true;
                try
                {
                    bool HasProcessed = false;
                    // 优先恢复 Mod 加载器，再匹配兼容的附加组件
                    foreach (var LoadingComponent in new[] { this.LoadFabric, this.LoadForge, this.LoadNeoForge, this.LoadLiteLoader, this.LoadOptiFine, this.LoadFabricApi, this.LoadOptiFabric })
                    {
                        string Version = null;
                        if (!PendingComponents.TryGetValue(LoadingComponent, out Version))
                            continue;
                        if (!LoadingComponent.State.IsLoader)
                            continue;
                        ModLoader.LoaderBase Loader = (ModLoader.LoaderBase)LoadingComponent.State;
                        // 检查前置组件
                        if (object.ReferenceEquals(LoadingComponent, this.LoadOptiFine))
                        {
                            if (new[] { LoadFabric, LoadForge, LoadNeoForge, LoadLiteLoader }.Any(Component => PendingComponents.ContainsKey(Component)))
                                continue;
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadFabricApi))
                        {
                            if (PendingComponents.ContainsKey(this.LoadFabric))
                                continue;
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadForge))
                        {
                            if (!McVersion.IsStandardFormat(VanillaName))
                            {
                                PendingComponents.Remove(LoadingComponent);
                                continue;
                            }
                            if ((((ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>)Loader).Input ?? "") != (VanillaName ?? ""))
                                continue;
                            if (Loader.State == ModBase.LoadState.Failed && Loader.Error.Message == "无")
                            {
                                PendingComponents.Remove(LoadingComponent);
                                continue;
                            }
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadOptiFabric))
                        {
                            if (PendingComponents.ContainsKey(this.LoadFabric) || PendingComponents.ContainsKey(this.LoadOptiFine))
                                continue;
                        }
                        if (Loader.State != ModBase.LoadState.Finished)
                            continue;
                        // 移除已处理的组件
                        PendingComponents.Remove(LoadingComponent);
                        HasProcessed = true;
                        // 匹配目标 MC 版本下兼容的组件
                        if (object.ReferenceEquals(LoadingComponent, this.LoadFabric))
                        {
                            if (SelectedFabric is null && LoadFabricGetError() is null)
                            {
                                var fabricLoader = ModDownload.DlFabricListLoader.Output.Value["loader"];
                                if (fabricLoader is null)
                                    return;
                                var Versions = fabricLoader.OfType<JObject>();
                                var FabricEntry = Versions.FirstOrDefault(v => (v["version"]?.ToString() ?? "") == (Version ?? "")) ?? Versions.FirstOrDefault();
                                SelectedFabric = FabricEntry?["version"]?.ToString();
                            }
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadOptiFine))
                        {
                            if (SelectedOptiFine is null && LoadOptiFineGetError() is null)
                            {
                                var Versions = ModDownload.DlOptiFineListLoader.Output.Value.Where(v => (v.Inherit ?? "") == (VanillaName ?? "") && (SelectedForge is null || IsOptiFineSuitForForge(v, SelectedForge)));
                                SelectedOptiFine = Versions.FirstOrDefault(v => (v.InstanceName.AfterLast("-OptiFine_") ?? "") == (Version ?? "")) ?? Versions.OrderBy(v => v.IsPreview).ThenByDescending(v => v.DisplayName, Comparer<string>.Create(McUtils.CompareVersion)).FirstOrDefault();
                            }
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadForge))
                        {
                            if (SelectedForge is null && LoadForgeGetError() is null)
                            {
                                // 原文为 LoaderTask(Of String, List(Of DlForgeVersionEntry))，即 ModLoader.LoaderTask<string, List<DlForgeVersionEntry>>
                                var Versions = ((ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>)Loader).Output.Where(v => (v.Inherit ?? "") == (VanillaName ?? "") && v.Category != "universal" && v.Category != "client" && (SelectedOptiFine is null || PageDownloadInstall.IsOptiFineSuitForForge(SelectedOptiFine, v)));
                                SelectedForge = Versions.FirstOrDefault(v => (v.VersionName ?? "") == (Version ?? "")) ?? Versions.OrderByDescending(v => v).FirstOrDefault();
                            }
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadNeoForge))
                        {
                            if (SelectedNeoForge is null && LoadNeoForgeGetError() is null)
                            {
                                var Versions = ModDownload.DlNeoForgeListLoader.Output.Value.Where(v => (v.Inherit ?? "") == (VanillaName ?? ""));
                                SelectedNeoForge = Versions.FirstOrDefault(v => (v.VersionName ?? "") == (Version ?? "")) ?? Versions.FirstOrDefault();
                            }
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadLiteLoader))
                        {
                            if (SelectedLiteLoader is null && LoadLiteLoaderGetError() is null)
                            {
                                var Versions = ModDownload.DlLiteLoaderListLoader.Output.Value.Where(v => (v.Inherit ?? "") == (VanillaName ?? ""));
                                SelectedLiteLoader = Versions.FirstOrDefault(v => (v.JsonToken?["version"]?.ToString() ?? "") == (Version ?? "")) ?? Versions.FirstOrDefault();
                            }
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadFabricApi))
                        {
                            if (SelectedFabricApi is null && LoadFabricApiGetError() is null)
                            {
                                var Versions = ModDownload.DlFabricApiLoader.Output.Where(v => IsFabricApiCompatible(v, VanillaName));
                                SelectedFabricApi = Versions.FirstOrDefault(v => (v.Version ?? "") == (Version ?? "") || (v.Id ?? "") == (Version ?? "") || (v.Display ?? "") == (Version ?? "")) ?? Versions.OrderByDescending(v => v.ReleaseDate).FirstOrDefault();
                            }
                            if (SelectedFabricApi is not null)
                                AutoSelectedFabricApi = true;
                        }
                        else if (object.ReferenceEquals(LoadingComponent, this.LoadOptiFabric))
                        {
                            if (SelectedOptiFabric is null && LoadOptiFabricGetError() is null)
                            {
                                var Versions = ModDownload.DlOptiFabricLoader.Output.Where(v => IsOptiFabricCompatible(v, VanillaName));
                                SelectedOptiFabric = Versions.FirstOrDefault(v => (v.Version ?? "") == (Version ?? "") || (v.Id ?? "") == (Version ?? "") || (v.Display ?? "") == (Version ?? "")) ?? Versions.OrderByDescending(v => v.ReleaseDate).FirstOrDefault();
                            }
                            if (SelectedOptiFabric is not null)
                                AutoSelectedOptiFabric = true;
                        }
                    }
                    // 更新组件列表
                    if (HasProcessed)
                        ReloadComponentLists();
                }
                finally
                {
                    IsRestoringComponents = false;
                }
            }
            // 刷新当前选择与原始版本的对比
            base.ReloadSelected();
            if (InitialTarget is not null)
                RefreshComparisons();
        }

        private void RefreshComparisons()
        {
            var Current = GetSelectedComponents();
            this.ImgMinecraftNew.Source = VanillaIcon;
            // Minecraft
            bool IsMinecraftUnchanged = (InitialTarget.MinecraftVersion ?? "") == (Current.MinecraftVersion ?? "");
            this.ShowComparison(this.PanMinecraftCompare, this.PanMinecraftInfo, InitialTarget.MinecraftVersion, Current.MinecraftVersion, this.LabMinecraftVersion.Text, BtnMinecraftRestore, true);
            // OptiFine
            ModDownload.DlOptiFineListEntry OriginalOptiFine = BtnOptiFineRestore.Tag as ModDownload.DlOptiFineListEntry;
            this.ShowComparison(this.PanOptiFineCompare, this.PanOptiFineInfo, InitialTarget.OptiFineVersion, Current.OptiFineVersion, this.LabOptiFine.Text, BtnOptiFineRestore, InitialTarget.OptiFineEntry is null || IsMinecraftUnchanged && this.CardOptiFine.MainSwap.Visibility == Visibility.Visible && OriginalOptiFine is not null && (SelectedForge is null || IsOptiFineSuitForForge(OriginalOptiFine, SelectedForge)), this.BtnOptiFineClear);
            // Fabric API
            this.ShowComparison(this.PanFabricApiCompare, this.PanFabricApiInfo, InitialTarget.FabricApiVersion, Current.FabricApiVersion, this.LabFabricApi.Text, BtnFabricApiRestore, InitialTarget.FabricApiVersion is null || IsMinecraftUnchanged && SelectedFabric is not null && this.CardFabricApi.MainSwap.Visibility == Visibility.Visible, this.BtnFabricApiClear);
            // OptiFabric
            this.ShowComparison(this.PanOptiFabricCompare, this.PanOptiFabricInfo, InitialTarget.OptiFabricVersion, Current.OptiFabricVersion, this.LabOptiFabric.Text, BtnOptiFabricRestore, InitialTarget.OptiFabricVersion is null || IsMinecraftUnchanged && SelectedFabric is not null && SelectedOptiFine is not null && this.CardOptiFabric.MainSwap.Visibility == Visibility.Visible, this.BtnOptiFabricClear);
            // Mod Loader
            foreach (var Loader in new[] { (ModLoaders.Forge, this.CardForge, this.PanForgeCompare, this.PanForgeInfo, this.LabForge, BtnForgeRestore, this.BtnForgeClear), (ModLoaders.NeoForge, this.CardNeoForge, this.PanNeoForgeCompare, this.PanNeoForgeInfo, this.LabNeoForge, BtnNeoForgeRestore, this.BtnNeoForgeClear), (ModLoaders.Fabric, this.CardFabric, this.PanFabricCompare, this.PanFabricInfo, this.LabFabric, BtnFabricRestore, this.BtnFabricClear), (ModLoaders.LiteLoader, this.CardLiteLoader, this.PanLiteLoaderCompare, this.PanLiteLoaderInfo, this.LabLiteLoader, BtnLiteLoaderRestore, this.BtnLiteLoaderClear) })
            {
                string OldVersion = null;
                string NewVersion = null;
                InitialTarget.ModLoaderVersions.TryGetValue(((dynamic)Loader).Kind, out OldVersion);
                Current.ModLoaderVersions.TryGetValue(((dynamic)Loader).Kind, out NewVersion);
                bool CanRestore = OldVersion is null || IsMinecraftUnchanged && ((dynamic)Loader).Card.MainSwap.Visibility == Visibility.Visible && ((dynamic)Loader).RestoreButton.Tag is not null;
                if (((dynamic)Loader).Kind == ModLoaders.Forge && OldVersion is not null && CanRestore)
                {
                    // 原文是 DirectCast(Loader.RestoreButton.Tag, DlForgeVersionEntry)：先取元组的 RestoreButton.Tag，再转成 DlForgeVersionEntry
                    CanRestore = SelectedOptiFine is null || IsOptiFineSuitForForge(SelectedOptiFine, (ModDownload.DlForgeVersionEntry)((dynamic)Loader).RestoreButton.Tag);
                }
                if (((dynamic)Loader).Kind == ModLoaders.Fabric && OldVersion is not null)
                {
                    string TargetVersion = VanillaName.Replace("∞", "infinite").Replace("Combat Test 7c", "1.16_combat-3");
                    var fabricLoader = ModDownload.DlFabricListLoader.Output.Value["loader"];
                    var fabricGame = ModDownload.DlFabricListLoader.Output.Value["game"];
                    CanRestore = this.CardFabric.Visibility == Visibility.Visible && this.CardFabric.MainSwap.Visibility == Visibility.Visible && (fabricLoader?.OfType<JObject>().Any(v => (v["version"]?.ToString() ?? "") == (OldVersion ?? "")) ?? false) && (fabricGame?.OfType<JObject>().Any(v => (v["version"]?.ToString() ?? "") == (TargetVersion ?? "")) ?? false);
                }
                this.ShowComparison(((dynamic)Loader).ComparePanel, ((dynamic)Loader).CurrentPanel, OldVersion, NewVersion, ((dynamic)Loader).Label.Text, ((dynamic)Loader).RestoreButton, CanRestore, ((dynamic)Loader).ClearButton);
            }
            UpdateStartButton();
        }

        private void ShowComparison(Grid ComparePanel, FrameworkElement CurrentPanel, string OldVersion, string NewVersion, string NewText, MyIconButton RestoreButton, bool CanRestore, FrameworkElement ClearButton = null)
        {
            // 获取不可安装的原因
            string UnavailableReason = null;
            if (NewVersion is null)
            {
                if (ReferenceEquals(RestoreButton, BtnOptiFineRestore))
                {
                    UnavailableReason = LoadOptiFineGetError();
                }
                else if (ReferenceEquals(RestoreButton, BtnForgeRestore))
                {
                    UnavailableReason = McVersion.IsStandardFormat(VanillaName) ? LoadForgeGetError() : "无";
                }
                else if (ReferenceEquals(RestoreButton, BtnNeoForgeRestore))
                {
                    UnavailableReason = this.CardNeoForge.Visibility == Visibility.Collapsed ? "无" : LoadNeoForgeGetError();
                }
                else if (ReferenceEquals(RestoreButton, BtnFabricRestore))
                {
                    UnavailableReason = this.CardFabric.Visibility == Visibility.Collapsed ? "无" : LoadFabricGetError();
                }
                else if (ReferenceEquals(RestoreButton, BtnLiteLoaderRestore))
                {
                    UnavailableReason = this.CardLiteLoader.Visibility == Visibility.Collapsed ? "无" : LoadLiteLoaderGetError();
                }
                else if (ReferenceEquals(RestoreButton, BtnFabricApiRestore))
                {
                    UnavailableReason = LoadFabricApiGetError();
                }
                else if (ReferenceEquals(RestoreButton, BtnOptiFabricRestore))
                {
                    UnavailableReason = LoadOptiFabricGetError();
                }
                NewText = UnavailableReason ?? "可以添加";
            }
            // 修改对比
            bool IsPending = NewVersion is null && (NewText == "获取中……" || NewText.StartsWithF("获取失败："));
            bool IsChanged = (OldVersion ?? "") != (NewVersion ?? "") && !IsPending;
            CanRestore = IsChanged && OldVersion is not null && CanRestore;
            ComparePanel.Visibility = IsChanged ? Visibility.Visible : Visibility.Collapsed;
            CurrentPanel.Visibility = IsChanged ? Visibility.Collapsed : Visibility.Visible;
            // 还原与取消安装按钮
            RestoreButton.Visibility = CanRestore ? Visibility.Visible : Visibility.Collapsed;
            if (ClearButton is not null)
                ClearButton.ToolTip = OldVersion is null ? "取消安装" : "移除";
            bool HasClearButton = ClearButton is not null && ClearButton.Visibility == Visibility.Visible;
            RestoreButton.Margin = new Thickness(0d, 5d, CanRestore && HasClearButton ? 60 : 32, 0d);
            ComparePanel.Margin = new Thickness(ComparePanel.Margin.Left, ComparePanel.Margin.Top, CanRestore && HasClearButton ? 110 : 80, ComparePanel.Margin.Bottom);
            if (!IsChanged)
                return;
            // 显示修改前已安装组件的对比卡片
            if (OldVersion is not null)
            {
                MyCard Card = (MyCard)ComparePanel.Parent;
                if (Card.Visibility == Visibility.Collapsed)
                {
                    Card.IsSwapped = true;
                    if (Card.MainSwap is not null)
                        Card.MainSwap.Visibility = Visibility.Collapsed;
                    Card.Visibility = Visibility.Visible;
                }
            }
            DockPanel OldSide = (DockPanel)ComparePanel.Children[0];
            DockPanel NewSide = (DockPanel)ComparePanel.Children[2];
            OldSide.Children[0].Visibility = OldVersion is null ? Visibility.Collapsed : Visibility.Visible;
            ((TextBlock)OldSide.Children[1]).Margin = OldVersion is null ? new Thickness(0d) : new Thickness(7d, 0d, 0d, 0d);
            NewSide.Children[0].Visibility = NewVersion is null ? Visibility.Collapsed : Visibility.Visible;
            TextBlock NewLabel = (TextBlock)NewSide.Children[1];
            NewLabel.Margin = NewVersion is null ? new Thickness(0d) : new Thickness(7d, 0d, 0d, 0d);
            NewLabel.Text = NewVersion is null ? "移除" : NewText;
            if (NewVersion is null && UnavailableReason is not null)
            {
                NewLabel.Inlines.Add(new Run($"（{(UnavailableReason == "无" ? "无可用版本" : UnavailableReason)}）") { Foreground = ModSecret.ColorGray4 });
            }
        }

        private void RestoreComponent(object sender, EventArgs e)
        {
            MyIconButton Button = (MyIconButton)sender;
            // 恢复初始选择
            if (ReferenceEquals(Button, BtnMinecraftRestore))
            {
                IsRestoringComponents = true;
                try
                {
                    PendingComponents.Clear();
                    if (Button.Tag is not JObject RestoreVersion)
                        throw new InvalidOperationException("缺少要恢复的 Minecraft 版本数据。");
                    var RestoreItem = ModDownloadLib.McDownloadListItem(RestoreVersion, null, false);
                    if (RestoreItem is null || RestoreItem.Init() is not MyListItem InitializedItem)
                        throw new InvalidOperationException("无法初始化要恢复的 Minecraft 版本列表项。");
                    MinecraftSelected(InitializedItem, null);
                    if (ForgeVersionLoader is not null)
                        this.LoadForge.State = ForgeVersionLoader;
                    // 恢复初始的组件
                    SelectedOptiFine = BtnOptiFineRestore.Tag as ModDownload.DlOptiFineListEntry;
                    SelectedForge = BtnForgeRestore.Tag as ModDownload.DlForgeVersionEntry;
                    SelectedNeoForge = BtnNeoForgeRestore.Tag as ModDownload.DlNeoForgeListEntry;
                    SelectedFabric = BtnFabricRestore.Tag as string;
                    SelectedLiteLoader = BtnLiteLoaderRestore.Tag as ModDownload.DlLiteLoaderListEntry;
                    SelectedFabricApi = BtnFabricApiRestore.Tag as ResourceVersion;
                    SelectedOptiFabric = BtnOptiFabricRestore.Tag as ResourceVersion;
                    AutoSelectedFabricApi = SelectedFabric is not null;
                    AutoSelectedOptiFabric = SelectedFabric is not null && SelectedOptiFine is not null;
                }
                finally
                {
                    IsRestoringComponents = false;
                }
                ReloadComponentLists();
                return;
            }
            else if (ReferenceEquals(Button, BtnOptiFineRestore))
            {
                SelectedOptiFine = Button.Tag as ModDownload.DlOptiFineListEntry;
                AutoSelectedOptiFabric = SelectedOptiFine is not null && SelectedFabric is not null;
                if (SelectedOptiFine is null)
                    SelectedOptiFabric = null;
            }
            else if (ReferenceEquals(Button, BtnForgeRestore))
            {
                SelectedForge = Button.Tag as ModDownload.DlForgeVersionEntry;
            }
            else if (ReferenceEquals(Button, BtnNeoForgeRestore))
            {
                SelectedNeoForge = Button.Tag as ModDownload.DlNeoForgeListEntry;
            }
            else if (ReferenceEquals(Button, BtnFabricRestore))
            {
                SelectedFabric = Button.Tag as string;
                AutoSelectedFabricApi = SelectedFabric is not null;
                AutoSelectedOptiFabric = SelectedFabric is not null && SelectedOptiFine is not null;
                if (SelectedFabric is null)
                {
                    SelectedFabricApi = null;
                    SelectedOptiFabric = null;
                }
            }
            else if (ReferenceEquals(Button, BtnFabricApiRestore))
            {
                SelectedFabricApi = Button.Tag as ResourceVersion;
                AutoSelectedFabricApi = true;
            }
            else if (ReferenceEquals(Button, BtnOptiFabricRestore))
            {
                SelectedOptiFabric = Button.Tag as ResourceVersion;
                AutoSelectedOptiFabric = true;
            }
            else if (ReferenceEquals(Button, BtnLiteLoaderRestore))
            {
                SelectedLiteLoader = Button.Tag as ModDownload.DlLiteLoaderListEntry;
            }
            if (Button.Parent is MyCard ParentCard)
                ParentCard.IsSwapped = true;
            // 刷新组件
            ReloadComponentLists();
        }

        private void CardMinecraft_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ShowMinecraftVersionList();
            e.Handled = true;
        }

        private void OnNameTextChanged(object sender, EventArgs e) => UpdateStartButton();
        private void OnValidateChanged(object sender, EventArgs e) => UpdateStartButton();
        private void OnOriginalNameTextChanged(object sender, EventArgs e) => UpdateOriginalNameVisibility();

        private void UpdateStartButton()
        {
            this.BtnStart.IsEnabled = TextSelectName.IsValidated && InitialTarget is not null && !PendingComponents.Any() && (!IsVanillaVersionReliable || !InitialTarget.IsEquivalentTo(GetSelectedComponents()) || !string.Equals(TextSelectName.Text, OriginalName, StringComparison.Ordinal));
        }

        private void UpdateOriginalNameVisibility()
        {
            bool IsNameChanged = !string.Equals(TextSelectName.Text, OriginalName, StringComparison.Ordinal);
            var NewVisibility = IsNameChanged ? Visibility.Visible : Visibility.Collapsed;
            if (this.LabSelectName.Visibility == NewVisibility)
                return;
            this.LabSelectName.Visibility = NewVisibility;
            BtnOriginalNameRestore.Visibility = NewVisibility;
            int RightMargin = IsNameChanged ? 40 : 16;
            if (IsInSelectPage && ModAnimation.AniControlEnabled == 0)
            {
                ModAnimation.AniStart(ModAnimation.AaDouble(Delta => TextSelectName.Margin = new Thickness(56d, 16d, Conversions.ToDouble(Operators.AddObject(TextSelectName.Margin.Right, Delta)), 0d), RightMargin - TextSelectName.Margin.Right, 150, 0, new ModAnimation.AniEaseOutFluent(), After: false), "DownloadModify Name Margin");
            }
            else
            {
                ModAnimation.AniStop("DownloadModify Name Margin");
                TextSelectName.Margin = new Thickness(56d, 16d, RightMargin, 0d);
            }
        }

        private void BtnOriginalNameRestore_Click(object sender, EventArgs e)
        {
            TextSelectName.Text = OriginalName;
            IsSelectNameEdited = !string.Equals(OriginalName, GetSelectName(), StringComparison.Ordinal);
        }

        public override void StartInstall(ModDownloadLib.McInstallRequest Request)
        {
            UpdateStartButton();
            if (!this.BtnStart.IsEnabled)
                return;
            var TargetInstance = InitLoader.Input;
            var Target = Request.Components;
            string InstanceName = Request.NewInstanceName;
            bool IsNameChanged = !string.Equals(InstanceName, TargetInstance.Name, StringComparison.Ordinal);
            Request.NewInstanceName = IsNameChanged ? InstanceName : null;
            Request.InstanceFolder = TargetInstance.PathVersion;
            // 警告
            var Warnings = new List<string>();
            if ((SelectedForge is not null || SelectedNeoForge is not null || SelectedFabric is not null) && !PathUtils.IsSame(TargetInstance.PathIndie, TargetInstance.PathVersion))
            {
                Warnings.Add("该版本尚未开启版本隔离，可能与别的 MC 版本共用同一个 Mod 和存档文件夹。" + Constants.vbCrLf + "修改版本后，已有的 Mod、存档等需要手动迁移到新的游戏目录。");
            }
            bool IsMinecraftVersionChanged = (InitialTarget.MinecraftVersion ?? "") != (Target.MinecraftVersion ?? "");
            if (!IsVanillaVersionReliable && !IsMinecraftVersionChanged)
            {
                Warnings.Add($"PCL Next 未能精准识别该版本，请确认它是不是 Minecraft {InitialTarget.MinecraftVersion}，如果不正确，请取消并选择正确的版本！");
            }
            if (!Configs.HintModifyInstanceWarning.Get())
            {
                Warnings.Add("如果该版本安装了 PCL Next 不兼容的加载器，或经过第三方修改，这些改动将会丢失！");
            }
            bool IsModLoaderChanged = false;
            foreach (var Loader in InitialTarget.ModLoaderVersions.Keys.Union(Target.ModLoaderVersions.Keys))
            {
                string CurrentVersion = null;
                string TargetVersion = null;
                bool HasCurrent = InitialTarget.ModLoaderVersions.TryGetValue(Loader, out CurrentVersion);
                bool HasTarget = Target.ModLoaderVersions.TryGetValue(Loader, out TargetVersion);
                if (HasCurrent != HasTarget || (CurrentVersion ?? "") != (TargetVersion ?? ""))
                {
                    IsModLoaderChanged = true;
                    break;
                }
            }
            if ((IsModLoaderChanged || IsMinecraftVersionChanged) && InitialTarget.ModLoaderVersions.Any())
            {
                Warnings.Add("现有的 Mod、资源包等在修改后可能会不兼容！");
            }
            if (Warnings.Any())
            {
                if (ModMain.MyMsgBox("请注意：" + Constants.vbCrLf + "• " + Warnings.Join(Constants.vbCrLf + "• "), "修改确认", "开始修改", "取消", IsWarn: true) != 1)
                    return;
            }
            if (!Configs.HintModifyInstanceWarning.Get())
                Configs.HintModifyInstanceWarning.Set(true);
            // 开始修改
            if (IsVanillaVersionReliable && InitialTarget.IsEquivalentTo(Target))
            {
                // 仅重命名
                try
                {
                    string OldPath = TargetInstance.PathVersion;
                    var RenamedInstance = TargetInstance.Rename(InstanceName);
                    if (PageInstanceLeft.Instance is not null && PathUtils.IsSame(PageInstanceLeft.Instance.PathVersion, OldPath))
                        PageInstanceLeft.Instance = RenamedInstance;
                    if (ModMinecraft.McInstanceSelected is not null && PathUtils.IsSame(ModMinecraft.McInstanceSelected.PathVersion, OldPath))
                        ModMinecraft.McInstanceSelected = RenamedInstance;
                    PageInstanceLeft.ReloadCurrentJava();
                    ModMain.Hint("重命名成功！", ModMain.HintType.Green);
                    ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                    ModMain.FrmMain.PageBack();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "重命名版本失败", LogBehavior.Alert);
                }
            }
            // 实际修改
            else if (ModDownloadLib.McModificationStart(Request, TargetInstance))
                ModMain.FrmMain.PageBack();
        }
    }
}