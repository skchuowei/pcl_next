using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{

    public partial class PageDownloadResourceDetail
    {
        private MyResourceItem ResourceItem = null;

        /// <summary>
    /// 当前页面应展示的内容类别。
    /// 若当前页面是在查看前置时进入，则为 Any；这也能用于指示当前是否在查看前置。
    /// </summary>
        private ResourceTypes TargetResourceType;
        private ResourceProject Project;
        private string TargetVersion;
        private ModLoaders TargetLoaders;

        public PageDownloadResourceDetail()
        {
            InitializeComponent();
            this.Load.StateChanged += (p1, p2, p3) => Load_State((object)p1, (MyLoading.MyLoadingState)p2, (MyLoading.MyLoadingState)p3);
            this.BtnIntroWeb.Click += (p1, p2) => BtnIntroWeb_Click((object)p1, (EventArgs)p2);
            this.BtnIntroWiki.Click += (p1, p2) => BtnIntroWiki_Click((object)p1, (EventArgs)p2);
            this.BtnIntroCopy.Click += (p1, p2) => BtnIntroCopy_Click((object)p1, (EventArgs)p2);
            this.HintAlternative.Click += (p1, p2) => HintAlternative_Click();
            ResourceFileLoader = new ModLoader.LoaderTask<int, List<ResourceVersion>>("Resource File", Task =>
            {
                LoadPageArguments();
                var Result = ResourceVersion.FromProjectId(Project.Id, Project.Platform);
                if (Task.IsCanceled)
                    return;
                Task.Output = Result;
            });
            this.Loaded += (_, __) => LoadPageArguments();
            this.Initialized += PageDownloadResourceDetail_Inited;
            this.PageEnter += Init;
        }
        public void LoadPageArguments()
        {
            Project = (ResourceProject)((dynamic)ModMain.FrmMain.PageCurrent.Additional)[0];
            TargetVersion = Conversions.ToString(((dynamic)ModMain.FrmMain.PageCurrent.Additional)[2]);
            TargetLoaders = (ModLoaders)Conversions.ToInteger(((dynamic)ModMain.FrmMain.PageCurrent.Additional)[3]);
            if (TargetLoaders != ModLoaders.None && !TargetLoaders.Flags().IsSingle())
                TargetLoaders = TargetLoaders.Flags().First(); // 强制取第一个
            TargetResourceType = (ResourceTypes)Conversions.ToInteger(((dynamic)ModMain.FrmMain.PageCurrent.Additional)[4]);
        }

        #region 加载器

        private ModLoader.LoaderTask<int, List<ResourceVersion>> ResourceFileLoader;

        // 初始化加载器信息
        private void PageDownloadResourceDetail_Inited(object sender, EventArgs e)
        {
            LoadPageArguments();
            this.PageLoaderInit(this.Load, this.PanLoad, this.PanMain, this.CardIntro, ResourceFileLoader, (_) => Load_OnFinish());
        }
        // 自动重试
        private void Load_State(object sender, MyLoading.MyLoadingState state, MyLoading.MyLoadingState oldState)
        {
            switch (ResourceFileLoader.State)
            {
                case ModBase.LoadState.Failed:
                    {
                        string ErrorMessage = "";
                        if (ResourceFileLoader.Error is not null)
                            ErrorMessage = ResourceFileLoader.Error.Message;
                        if (ErrorMessage.Contains("不是有效的 json 文件"))
                        {
                            Logger.Warn("下载的文件 json 列表损坏，已自动重试");
                            this.PageLoaderRestart();
                        }

                        break;
                    }
            }
        }
        // 结果 UI 化
        private class CardSorter : IComparer<string>
        {
            public string Topmost = "";
            public int Compare(string x, string y)
            {
                // 相同
                if ((x ?? "") == (y ?? ""))
                    return 0;
                // 置顶
                if ((x ?? "") == (Topmost ?? ""))
                    return -1;
                if ((y ?? "") == (Topmost ?? ""))
                    return 1;
                // 特殊版本
                bool IsXSpecial = !x.Contains(".");
                bool IsYSpecial = !y.Contains(".");
                if (IsXSpecial && IsYSpecial)
                    return x.CompareTo(y);
                if (IsXSpecial)
                    return 1;
                if (IsYSpecial)
                    return -1;
                // 比较版本号
                int VersionCodeSort = -McUtils.CompareVersion(x.Replace(x.BeforeFirst(" ") + " ", ""), y.Replace(y.BeforeFirst(" ") + " ", ""));
                if (VersionCodeSort != 0)
                    return VersionCodeSort;
                // 比较全部
                return -McUtils.CompareVersion(x, y);
            }
            public CardSorter(string Topmost = "")
            {
                this.Topmost = Topmost ?? "";
            }
        }

        private string VersionFilter;
        private bool GroupedDrop; // 是否按 Drop 筛选（1.21 / 1.20 / 1.19 / ...）而非小版本号（1.21.1 / 1.21 / 1.20.4 / ...）
        private bool GroupedOld; // 是否折叠远古版本为一个选项
                                 // 筛选类型相同的结果（Modrinth 会返回 Mod、服务端插件、数据包混合的列表）
        private List<ResourceVersion> GetResults()
        {
            var Results = ResourceFileLoader.Output;
            switch (TargetResourceType)
            {
                case ResourceTypes.Any:
                    {
                        Results = Results.Where(r => r.ResourceType != ResourceTypes.Plugin).ToList();
                        break;
                    }
                // 不筛选光影和资源包，否则原版光影会因为是资源包格式而被过滤（#6473）
                case ResourceTypes.Shader:
                case ResourceTypes.ResourcePack:
                    {
                        break;
                    }

                default:
                    {
                        Results = Results.Where(r => r.ResourceType == TargetResourceType).ToList();
                        break;
                    }
            }
            return Results;
        }
        private void Load_OnFinish()
        {
            var Results = GetResults();

            // 初始化筛选器
            List<string> Filters = null;
            void UpdateFilters() => Filters = Results.SelectMany(v => v.GameVersions).Select(v => GetGroupedVersionName(v, GroupedDrop, GroupedOld)).Distinct().OrderByDescending(s => s, Comparer<string>.Create((x, y) => McUtils.CompareVersion(x, y))).ToList();

            // 确定分组方式
            GroupedDrop = false;
            GroupedOld = false;
            UpdateFilters();
            if (Filters.Count < 9)
                goto GroupDone;
            GroupedDrop = true;
            GroupedOld = false;
            UpdateFilters();
            if (Filters.Count < 9)
                goto GroupDone;
            GroupedDrop = false;
            GroupedOld = true;
            UpdateFilters();
            if (Filters.Count < 9)
                goto GroupDone;
            GroupedDrop = true;
            GroupedOld = true;
            UpdateFilters();
        GroupDone:
            ;


            // UI 化筛选器
            this.PanFilter.Children.Clear();
            if (Filters.Count < 2)
            {
                this.CardFilter.Visibility = Visibility.Collapsed;
                VersionFilter = null;
            }
            else
            {
                this.CardFilter.Visibility = Visibility.Visible;
                Filters.Insert(0, "全部");
                // 转化为按钮
                foreach (string Version in Filters)
                {
                    var NewButton = new MyRadioButton() { Text = Version, Margin = new Thickness(2d, 0d, 2d, 0d), ColorType = MyRadioButton.ColorState.Highlight };
                    NewButton.LabText.Margin = new Thickness(-2, 0d, 10d, 0d);
                    NewButton.Check += (sender, raiseByMouse) =>
        {
            VersionFilter = ((dynamic)sender).Text == "全部" ? null : ((dynamic)sender).Text;
            UpdateFilterResult();
        };
                    this.PanFilter.Children.Add(NewButton);
                }
                // 自动选择
                MyRadioButton ToCheck = null;
                if (!string.IsNullOrEmpty(TargetVersion))
                {
                    var TargetFile = Results.FirstOrDefault(v => v.GameVersions.Contains(TargetVersion));
                    if (TargetFile is not null)
                    {
                        string TargetGroup = GetGroupedVersionName(TargetVersion, GroupedDrop, GroupedOld);
                        foreach (MyRadioButton Button in this.PanFilter.Children)
                        {
                            if ((Button.Text ?? "") != (TargetGroup ?? ""))
                                continue;
                            ToCheck = Button;
                            break;
                        }
                    }
                }
                if (ToCheck is null)
                    ToCheck = (MyRadioButton)this.PanFilter.Children[0];
                ToCheck.Checked = true;
            }

            // 更新筛选结果（文件列表 UI 化）
            UpdateFilterResult();
        }
        private void UpdateFilterResult()
        {
            var Results = GetResults();

            string TargetCardName = !string.IsNullOrEmpty(TargetVersion) || TargetLoaders != ModLoaders.None ? $"{(TargetLoaders != ModLoaders.None ? TargetLoaders.ToString() + " " : "")}{TargetVersion}（所选版本）" : "";
            // 归类到卡片下
            var Dict = new SortedDictionary<string, List<ResourceVersion>>(new CardSorter(TargetCardName));
            Dict.Add("其他", new List<ResourceVersion>());
            foreach (ResourceVersion Version in Results)
            {
                foreach (var GameVersion in Version.GameVersions)
                {
                    // 检查是否符合版本筛选器
                    if (VersionFilter is not null && (GetGroupedVersionName(GameVersion, GroupedDrop, GroupedOld) ?? "") != (VersionFilter ?? ""))
                        continue;
                    // 决定添加到哪个卡片
                    string VerName = GetGroupedVersionName(GameVersion, false, false);
                    // 遍历加入的加载器列表
                    var Loaders = new List<string>();
                    if (Project.ModLoaders.Flags().Count() > 1 && Version.ResourceType == ResourceTypes.Mod && McVersion.IsStandardFormat(VerName)) // 工程至少有两个加载器
                                                                                                                                                    // 是 Mod
                                                                                                                                                    // 不是 “快照版本” 之类的
                    {
                        Loaders.AddRange(Version.ModLoaders.Flags().Select(f => f.ToString() + " "));
                    }
                    if (!Loaders.Any())
                        Loaders.Add(""); // 保底加一个空的，确保它在一张卡片里
                                         // 实际添加
                    foreach (var Loader in Loaders)
                    {
                        string TargetCard = Loader + VerName;
                        if (!Dict.ContainsKey(TargetCard))
                            Dict.Add(TargetCard, new List<ResourceVersion>());
                        if (!Dict[TargetCard].Contains(Version))
                            Dict[TargetCard].Add(Version);
                    }
                }
            }
            // 添加筛选的版本的卡片
            if (!string.IsNullOrEmpty(TargetCardName) && (VersionFilter is null || GetGroupedVersionName(TargetVersion, GroupedDrop, GroupedOld).StartsWithF(VersionFilter)))
            {
                Dict.Add(TargetCardName, new List<ResourceVersion>());
                foreach (ResourceVersion Version in Results)
                {
                    if (Version.GameVersions.Contains(TargetVersion) && (TargetLoaders == ModLoaders.None || TargetLoaders.Flags().Intersect(Version.ModLoaders.Flags()).Any()))
                    {
                        // 检查是否符合版本筛选器
                        if (VersionFilter is not null && !Version.GameVersions.Any(v => (GetGroupedVersionName(v, GroupedDrop, GroupedOld) ?? "") == (VersionFilter ?? "")))
                            continue;
                        if (!Dict[TargetCardName].Contains(Version))
                            Dict[TargetCardName].Add(Version);
                    }
                }
            }
            // 转化为 UI
            try
            {
                this.PanResults.Children.Clear();
                foreach (KeyValuePair<string, List<ResourceVersion>> Pair in Dict)
                {
                    if (!Pair.Value.Any())
                        continue;
                    if ((Pair.Key ?? "") == (TargetCardName.Replace("（所选版本）", "") ?? ""))
                        continue;
                    // 增加卡片
                    var NewCard = new MyCard() { Title = Pair.Key, Margin = new Thickness(0d, 0d, 0d, 15d) };
                    var NewStack = new StackPanel() { Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d), VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d), Tag = Pair.Value };
                    NewCard.Children.Add(NewStack);
                    NewCard.SwapControl = NewStack;
                    NewCard.SetContentBuilder((_) => this.InitializeStackResourceVersions((dynamic)_));
                    this.PanResults.Children.Add(NewCard);
                    // 确定卡片是否默认展开
                    if ((Pair.Key ?? "") == (TargetCardName ?? "") || ModMain.FrmMain.PageCurrent.Additional is not null && ((List<string>)((dynamic)ModMain.FrmMain.PageCurrent.Additional)[1]).Contains(NewCard.Title)) // #2761
                    {
                        NewCard.BuildContent();
                    }
                    else
                    {
                        NewCard.IsSwapped = true;
                    }
                    // 增加提示
                    if (Pair.Key == "其他")
                    {
                        NewStack.Children.Add(new MyHint() { Text = "由于版本信息更新缓慢，可能无法识别刚更新的 MC 版本。几天后即可正常识别。", Theme = MyHint.Themes.Yellow, Margin = new Thickness(5d, 0d, 0d, 8d) });
                    }
                }
                // 如果只有一张卡片，展开第一张卡片
                if (this.PanResults.Children.Count == 1)
                {
                    ((MyCard)this.PanResults.Children[0]).IsSwapped = false;
                }
                // 替代提示
                if (Project.Types == ResourceTypes.ModOrDataPack && (TargetResourceType == ResourceTypes.Mod || TargetResourceType == ResourceTypes.DataPack))
                {
                    this.HintAlternative.Visibility = Visibility.Visible;
                    this.HintAlternative.Text = TargetResourceType == ResourceTypes.Mod ? "以下是该项目的 Mod 版本。点击这里查看其数据包版本。" : "以下是该项目的数据包版本。点击这里查看其 Mod 版本。";
                }
                else
                {
                    this.HintAlternative.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化工程下载列表出错");
            }
        }
        /// <summary>
    /// 根据卡片中的 StackPanel.Tag 将资源版本列表数据实例化为 UI。
    /// </summary>
        private void InitializeStackResourceVersions(StackPanel Stack)
        {
            var Versions = ((List<ResourceVersion>)Stack.Tag).SortByComparison((a, b) => a.ReleaseDate > b.ReleaseDate);
            bool HasDuplicates = Versions.DistinctBy(a => a.Display).Count() != Versions.Count;
            List<string> Dependences;
            // 获取并添加前置信息
            if (TargetResourceType == ResourceTypes.ModPack)
            {
                Dependences = new List<string>(); // 整合包的前置信息会显示其中包含的一大堆 Mod，不是真正意义的前置
            }
            else
            {
                Dependences = Versions.SelectMany(f => f.Dependencies).Distinct().Ordered().ToList();
            }
            if (Dependences.Any())
            {
                Dependences = Dependences.Where(d =>
        {
            if (!ResourceProject.Cache.ContainsKey(d))
                Logger.Warn($"未找到 ID {d} 的前置信息");
            return ResourceProject.Cache.ContainsKey(d);
        }).ToList();
                // 添加开头间隔
                Stack.Children.Add(new TextBlock() { Text = "前置资源", FontSize = 14d, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6d, 2d, 0d, 5d) });
                // 添加前置列表
                foreach (var Dep in Dependences)
                {
                    var Item = ResourceProject.Cache[Dep].ToResourceItem(false, false);
                    Stack.Children.Add(Item);
                }
                // 添加结尾间隔
                Stack.Children.Add(new TextBlock() { Text = "版本列表", FontSize = 14d, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6d, 12d, 0d, 5d) });
            }
            // 实例化每个元素
            foreach (var Version in Versions)
            {
                if (!ModDownloadPolicy.RestrictMcDownload && TargetResourceType == ResourceTypes.ModPack)
                {
                    Stack.Children.Add(Version.ToListItem((_, __) => this.Install_Click((dynamic)_, (dynamic)__), this.Save_Click, IsBadDisplay: HasDuplicates));
                }
                else
                {
                    Stack.Children.Add(Version.ToListItem(Save_Click, IsBadDisplay: HasDuplicates));
                }
            }
            Stack.Children.Add(new FrameworkElement() { Height = 18d });
            Stack.Tag = null;
        }
        private string GetGroupedVersionName(string Name, bool GroupedByDrop, bool FoldOld)
        {
            int Drop = McVersion.VersionToDrop(Name);
            if (Name is null)
            {
                return "其他";
            }
            else if (Name.Contains("w") || Drop == 209)
            {
                return "快照版";
            }
            else if (!McVersion.IsStandardFormat(Name) || FoldOld && Drop < 120)
            {
                return "远古版";
            }
            else if (GroupedByDrop)
            {
                return McVersion.DropToVersion(Drop);
            }
            else
            {
                return Name;
            }
        }

        #endregion

        private bool IsFirstInit = true;
        public void Init()
        {
            ModAnimation.AniControlEnabled += 1;
            Project = (ResourceProject)((dynamic)ModMain.FrmMain.PageCurrent.Additional)[0];
            this.PanBack.ScrollToHome();

            // 重启加载器
            if (IsFirstInit)
            {
                // 在 Me.Initialized 已经初始化了加载器，不再重复初始化
                IsFirstInit = false;
            }
            else
            {
                this.PageLoaderRestart(IsForceRestart: true);
            }

            // 放置当前工程
            if (ResourceItem is not null)
                this.PanIntro.Children.Remove(ResourceItem);
            ResourceItem = Project.ToResourceItem(true, true).Init();
            ResourceItem.CanInteraction = false;
            ResourceItem.Margin = new Thickness(-7, -7, 0d, 8d);
            this.PanIntro.Children.Insert(0, ResourceItem);

            // 决定按钮显示
            this.BtnIntroWeb.Text = $"转到 {Project.Platform}";
            this.BtnIntroWiki.Visibility = Project.WikiId == 0 ? Visibility.Collapsed : Visibility.Visible;

            ModAnimation.AniControlEnabled -= 1;
        }

        // 整合包安装
        public void Install_Click(MyListItem sender, EventArgs e)
        {
            try
            {

                // 获取基本信息
                ResourceVersion File = (ResourceVersion)sender.Tag;
                string LoaderName = $"{Project.Platform} 整合包下载：{Project.TranslatedName} ";

                // 获取版本名
                string PackName = Project.TranslatedName.Replace(".zip", "").Replace(".rar", "").Replace(".mrpack", "").Replace(@"\", "＼").Replace("/", "／").Replace("|", "｜").Replace(":", "：").Replace("<", "＜").Replace(">", "＞").Replace("*", "＊").Replace("?", "？").Replace("\"", "").Replace("： ", "：");
                var Validate = new ValidateFolderName(ModMinecraft.McFolderSelected + "versions");
                if (!string.IsNullOrEmpty(Validate.Validate(PackName)))
                    PackName = "";
                string InstanceName = ModMain.MyMsgBoxInput("输入版本名称", "", PackName, new Collection<ValidateType>() { Validate });
                if (string.IsNullOrEmpty(InstanceName))
                    return;

                // 构造步骤加载器
                var Loaders = new List<ModLoader.LoaderBase>();
                string Target = $@"{ModMinecraft.McFolderSelected}versions\{InstanceName}\原始整合包.{(Project.Platform == ResourcePlatforms.CurseForge ? "zip" : "mrpack")}";
                string LogoFileAddress = ResourceItem.PathLogo.ActualSource;
                Loaders.Add(new ModNet.LoaderDownload("下载整合包文件", new[] { File.ToNetFile(Target, ResourceVersion.DownloadReason.Standalone, File.GameVersions.FirstOrDefault(), File.ModLoaders) }) { ProgressWeight = 10d, Block = true });
                Loaders.Add(new ModLoader.LoaderTask<int, int>("准备安装整合包", (_) => ModModpack.ModpackInstall(Target, InstanceName, FileUtils.Exists(LogoFileAddress) ? LogoFileAddress : null)) { ProgressWeight = 0.1d });

                // 启动
                var Loader = new ModLoader.LoaderCombo<string>(LoaderName, Loaders)
                {
                    OnStateChanged = new Action<ModLoader.LoaderBase>(MyLoader =>
                            {
                                switch (MyLoader.State)
                                {
                                    case ModBase.LoadState.Failed:
                                        {
                                            ModMain.Hint(MyLoader.Name + "失败：" + MyLoader.Error.GetDisplay(false), ModMain.HintType.Red);
                                            break;
                                        }
                                    case ModBase.LoadState.Canceled:
                                        {
                                            ModMain.Hint(MyLoader.Name + "已取消！", ModMain.HintType.Blue);
                                            break;
                                        }
                                    case ModBase.LoadState.Loading:
                                        {
                                            return; // 不重新加载版本列表
                                        }
                                }
                                ModDownloadLib.McInstallFailedClearFolder(MyLoader);
                            })
                };
                Loader.Start(ModMinecraft.McFolderSelected + @"versions\" + InstanceName + @"\");
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.TaskManager);
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "下载资源整合包失败");
            }
        }
        // 资源下载；整合包另存为
        public static Dictionary<ResourceTypes, string> CachedFolder = new Dictionary<ResourceTypes, string>(); // 仅在本次缓存的下载文件夹
        public void Save_Click(object sender, EventArgs e)
        {
            ResourceVersion File = (ResourceVersion)(sender is MyListItem ? sender : ((dynamic)sender).Parent).Tag;
            ModBase.RunInNewThread(() =>
        {
            try
            {
                string Desc = null;
                switch (File.ResourceType)
                {
                    case ResourceTypes.ModPack:
                        {
                            Desc = "整合包";
                            break;
                        }
                    case ResourceTypes.Mod:
                        {
                            Desc = "Mod ";
                            break;
                        }
                    case ResourceTypes.ResourcePack:
                        {
                            Desc = "资源包";
                            break;
                        }
                    case ResourceTypes.Shader:
                        {
                            Desc = "光影包";
                            break;
                        }
                    case ResourceTypes.DataPack:
                        {
                            Desc = "数据包";
                            break;
                        }
                }
                // 确认默认保存位置
                string DefaultFolder = null;
                if (File.ResourceType != ResourceTypes.ModPack)
                {
                    string SubFolder = null;
                    switch (File.ResourceType)
                    {
                        case ResourceTypes.Mod:
                            {
                                SubFolder = @"mods\";
                                break;
                            }
                        case ResourceTypes.ResourcePack:
                            {
                                SubFolder = @"resourcepacks\";
                                break;
                            }
                        case ResourceTypes.Shader:
                            {
                                SubFolder = @"shaderpacks\";
                                break;
                            }
                        case ResourceTypes.DataPack:
                            {
                                SubFolder = ""; // 导航到版本根目录
                                break;
                            }
                    }
                    Func<ModMinecraft.McInstance, bool> IsInstanceSuitable = null;
                    // 获取资源所需的加载器
                    var AllowedLoaders = ModLoaders.None;
                    if (File.ModLoaders != ModLoaders.None)
                    {
                        AllowedLoaders = File.ModLoaders;
                    }
                    else if (Project.ModLoaders != ModLoaders.None)
                    {
                        AllowedLoaders = Project.ModLoaders;
                    }
                    Logger.Info($"{Desc}要求的加载器种类：{AllowedLoaders}");
                    // 判断某个版本是否符合资源要求
                    IsInstanceSuitable = new Func<ModMinecraft.McInstance, bool>((Instance) =>
        {
                        if (!Instance.IsLoaded)
                            Instance.Load();
            // 只对 Mod 和数据包进行版本检测
                        if (File.ResourceType == ResourceTypes.Mod || File.ResourceType == ResourceTypes.DataPack)
                        {
                            if (!File.GameVersions.Any(FileVersion => // 判断是否有任何一个版本匹配
        {
                                bool FileIsSnapshot = FileVersion.Contains("预览版") || !FileVersion.Contains(".");
                                bool InstanceIsSnapshot = Instance.Version.VanillaName.Contains("snapshot") || !Instance.Version.VanillaName.Contains(".");
                                if (FileIsSnapshot != InstanceIsSnapshot)
                                {
                // 只有一种是预览版
                                    return false;
                                }
                                else if (!FileIsSnapshot && !InstanceIsSnapshot)
                                {
                // 都不是预览版
                                    return (FileVersion ?? "") == (Instance.Version.VanillaName ?? "");
                                }
                                else
                                {
                // 都是预览版
                                    bool FileIsNewFormat = FileVersion.Contains(".") && ModBase.Val(FileVersion.BeforeFirst(".")) > 1d;
                                    bool InstanceIsNewFormat = Instance.Version.Drop > 250;
                                    if (FileIsNewFormat != InstanceIsNewFormat)
                                    {
                    // 只有一种是新格式快照
                                        return false;
                                    }
                                    else if (FileIsNewFormat && InstanceIsNewFormat)
                                    {
                    // 都是新格式快照
                                        return (FileVersion.BeforeFirst(" ") ?? "") == (Instance.Version.VanillaName.BeforeFirst("-") ?? "");
                                    }
                // 都是旧格式快照
                                    else if (FileVersion.Contains("w") && Instance.Version.VanillaName.Contains("w"))
                                    {
                    // 都是 w 快照
                                        return (FileVersion ?? "") == (Instance.Version.VanillaName ?? "");
                                    }
                                    else
                                    {
                    // 不全是 w 快照
                                        return true;
                                    } // 只能假定相同，无法判断
                                }
                            }))
                                return false;
                        }
            // 加载器
                        return AllowedLoaders == ModLoaders.None || AllowedLoaders.Flags().Intersect(Instance.Version.ModLoaders.Flags()).Any();
                    });
                    // 获取常规资源默认下载位置
                    if (CachedFolder.ContainsKey(File.ResourceType))
                    {
                        DefaultFolder = CachedFolder[File.ResourceType];
                        Logger.Info($"使用上次下载时的文件夹作为默认下载位置：{DefaultFolder}");
                    }
                    else if (ModMinecraft.McInstanceSelected is not null && IsInstanceSuitable(ModMinecraft.McInstanceSelected))
                    {
                        DefaultFolder = $"{ModMinecraft.McInstanceSelected.PathIndie}{SubFolder}";
                        DirectoryUtils.Create(DefaultFolder);
                        Logger.Info($"使用当前版本作为默认下载位置：{DefaultFolder}");
                    }
                    else
                    {
                        // 查找所有可能的版本
                        bool NeedLoad = ModMinecraft.McInstanceListLoader.State != ModBase.LoadState.Finished;
                        if (NeedLoad)
                        {
                            ModMain.Hint("正在查找适合的游戏版本……");
                            ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\", WaitForExit: true);
                        }
                        var SuitableInstanceList = ModMinecraft.McInstanceList.Values.SelectMany(l => l).Where(v => IsInstanceSuitable(v)).Select(v => DirectoryUtils.GetInfo($"{v.PathIndie}{SubFolder}"));
                        if (SuitableInstanceList.Any())
                        {
                            var SelectedInstance = SuitableInstanceList.OrderByDescending(Dir => Dir.Exists ? Dir.LastWriteTimeUtc : DateTime.MinValue).ThenByDescending(Dir => Dir.Exists ? Dir.GetFiles().Length : -1).First(); // 先按文件夹更改时间降序
                                                                                                                                                                                                                                  // 再按文件夹中的文件数量降序
                            DefaultFolder = PathUtils.RemoveExtendedPrefix(SelectedInstance.FullName);
                            DirectoryUtils.Create(DefaultFolder);
                            Logger.Info($"使用适合的游戏版本作为默认下载位置：{DefaultFolder}");
                        }
                        else
                        {
                            DefaultFolder = ModMinecraft.McFolderSelected;
                            if (NeedLoad)
                            {
                                ModMain.Hint($"当前 MC 文件夹中没有找到合适的版本！");
                            }
                            else
                            {
                                Logger.Info("由于当前版本不兼容，使用当前的 MC 文件夹作为默认下载位置");
                            }
                        }
                    }
                }
                // 获取基本信息
                string FileName;
                if ((Project.TranslatedName ?? "") == (Project.RawName ?? ""))
                {
                    FileName = File.FileName;
                }
                else
                {
                    string ChineseName = Project.TranslatedName.BeforeFirst(" (").BeforeFirst(" - ").Replace(@"\", "＼").Replace("/", "／").Replace("|", "｜").Replace(":", "：").Replace("<", "＜").Replace(">", "＞").Replace("*", "＊").Replace("?", "？").Replace("\"", "").Replace("： ", "：");
                    switch (Settings.Get<int>("ToolDownloadTranslateV2"))
                    {
                        case 0:
                            {
                                FileName = $"【{ChineseName}】{File.FileName}";
                                break;
                            }
                        case 1:
                            {
                                FileName = $"[{ChineseName}] {File.FileName}";
                                break;
                            }
                        case 2:
                            {
                                FileName = $"{ChineseName}-{File.FileName}";
                                break;
                            }
                        case 3:
                            {
                                FileName = $"{File.FileName}-{ChineseName}";
                                break;
                            }

                        default:
                            {
                                FileName = File.FileName;
                                break;
                            }
                    }
                }
                if (File.ResourceType == ResourceTypes.Mod)
                    FileName = FileName.Replace("~", "-"); // ~ 会导致 Mixin 加载失败
                ModBase.RunInUi(() =>
        {
            // 弹窗要求选择保存位置
                    string Target;
                    string Ext = File.ResourceType == ResourceTypes.Mod ? PathUtils.GetExtension(File.FileName) == "litemod" ? "litemod" : "jar" : PathUtils.GetExtension(File.FileName) == "mrpack" ? "mrpack" : "zip";
                    Target = Dialogs.SaveFile("选择保存位置", FileName, DefaultFolder, new[] { (Ext, Desc + "文件") });
                    if (Target is null)
                        return;
            // 构造步骤加载器
                    string LoaderName = Desc + "下载：" + PathUtils.GetFileNameWithoutExtension(Target) + " ";
                    if (!PathUtils.IsSame(PathUtils.RemoveLastPart(Target), DefaultFolder))
                        CachedFolder[File.ResourceType] = PathUtils.RemoveLastPart(Target);
                    var Loaders = new List<ModLoader.LoaderBase>();
                    Loaders.Add(new ModNet.LoaderDownload("下载文件", new[] { File.ToNetFile(Target, TargetResourceType == ResourceTypes.Any ? ResourceVersion.DownloadReason.Dependency : ResourceVersion.DownloadReason.Standalone) }) { ProgressWeight = 6d, Block = true });
            // 启动
                    var Loader = new ModLoader.LoaderCombo<int>(LoaderName, Loaders)
                    {
                        OnStateChanged = new Action<ModLoader.LoaderBase>(MyLoader =>
                                    {
                            ModDownloadLib.LoaderStateChangedHintOnly(MyLoader);
                            if (MyLoader.State == ModBase.LoadState.Finished && File.ResourceType == ResourceTypes.ModPack)
                                ModBase.OpenExplorer(Target);
                        })
                    };
                    Loader.Start(1);
                    ModLoader.LoaderTaskbarAdd(Loader);
                    ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                    ModMain.FrmMain.BtnExtraDownload.Ribble();
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "保存资源文件失败");
            }
        }, "Download Resource Detail Save");
        }

        private void BtnIntroWeb_Click(object sender, EventArgs e)
        {
            ModBase.OpenWebsite(Project.Website);
        }
        private void BtnIntroWiki_Click(object sender, EventArgs e)
        {
            ModBase.OpenWebsite("https://www.mcmod.cn/class/" + Project.WikiId + ".html");
        }
        private void BtnIntroCopy_Click(object sender, EventArgs e)
        {
            ModBase.ClipboardSet(ResourceItem.LabTitle.Text + ResourceItem.LabTitleRaw.Text);
        }

        // Mod / 数据包 互相跳转
        private void HintAlternative_Click()
        {
            TargetResourceType = TargetResourceType == ResourceTypes.Mod ? ResourceTypes.DataPack : ResourceTypes.Mod;
            ((dynamic)ModMain.FrmMain.PageCurrent.Additional)[4] = TargetResourceType; // 加载器会从这里重新拿数据
            this.PageLoaderRestart(IsForceRestart: true);
        }

    }
}