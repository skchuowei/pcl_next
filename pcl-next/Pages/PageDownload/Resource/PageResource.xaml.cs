using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using MeloongCore;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{

    [ContentProperty("SearchTags")]
    public partial class PageResource
    {

        #region 属性

        /// <summary>
    /// 用于 XAML 快速设置的 Tag 下拉框列表。
    /// </summary>
        public ItemCollection SearchTags
        {
            get
            {
                return this.ComboSearchTag.Items;
            }
        }

        /// <summary>
    /// 英文前后不含空格的可读资源类型名，例如 "Mod"、"整合包"。
    /// </summary>
        public string TypeName
        {
            get
            {
                return _TypeName;
            }
            set
            {
                if ((_TypeName ?? "") == (value ?? ""))
                    return;
                _TypeName = value;
                Loader.Name = $"社区资源获取：{value}";
            }
        }
        private string _TypeName = "";

        /// <summary>
    /// 英文前后含一个空格的可读资源类型名，例如 " Mod "、"整合包"。
    /// </summary>
        public string TypeNameSpaced
        {
            get
            {
                return _TypeNameSpaced;
            }
            set
            {
                if ((_TypeNameSpaced ?? "") == (value ?? ""))
                    return;
                _TypeNameSpaced = value;
                this.PanAlways.Title = $"搜索{value}";
                this.Load.Text = $"正在获取{value}列表";
            }
        }
        private string _TypeNameSpaced = "";

        /// <summary>
    /// 该页面对应的资源类型。
    /// </summary>
        public ResourceTypes PageType
        {
            get
            {
                return _Type;
            }
            set
            {
                if (_Type == value)
                    return;
                _Type = value;
                this.BtnSearchInstallModPack.Visibility = value == ResourceTypes.ModPack && !ModDownloadPolicy.RestrictMcDownload ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private ResourceTypes _Type = (ResourceTypes)(-1);

        #endregion

        #region 加载

        /// <summary>
    /// 在切换到页面时，应自动将筛选项设置为与该目标 MC 版本和加载器相同。
    /// </summary>
        public static ModMinecraft.McInstance TargetInstance = null;
        /// <summary>
    /// 在切换到该页面时自动设置的搜索框内的内容。
    /// </summary>
        public static string TargetName = null;
        /// <summary>
        /// 切换到本页时自动预设的游戏版本筛选（由下载页「基于某版本搜索」入口写入）。
        /// </summary>
        public static string TargetVersion = null;

        // 在点击 MyResourceItem 时会获取 Loader 的输入，以使资源详情页面可以应用相同的筛选项
        // 数据源已改由 C# 侧 Provider 提供（Phase 4 首步）；接口与原先的 ResourceSearcher.GetLoader 一致
        public ModLoader.LoaderTask<ResourceSearcher.SearchRequest, int> Loader;
        private bool _PageResource_Loaded_IsFirstLoaded = false;

        public PageResource()
        {
            InitializeComponent();
            this.Load.StateChanged += (p1, p2, p3) => Load_State((object)p1, (MyLoading.MyLoadingState)p2, (MyLoading.MyLoadingState)p3);
            this.BtnPageFirst.Click += (p1, p2) => BtnPageFirst_Click((object)p1, (RoutedEventArgs)p2);
            this.BtnPageLeft.Click += (p1, p2) => BtnPageLeft_Click((object)p1, (RoutedEventArgs)p2);
            this.BtnPageRight.Click += (p1, p2) => BtnPageRight_Click((object)p1, (RoutedEventArgs)p2);
            this.BtnSearchRun.Click += (p1, p2) => StartNewSearch();
            this.TextSearchName.KeyDown += (p1, p2) => EnterTrigger((object)p1, (KeyEventArgs)p2);
            this.TextSearchVersion.KeyDown += (p1, p2) => EnterTrigger((object)p1, (KeyEventArgs)p2);
            this.BtnSearchReset.Click += (p1, p2) => ResetFilter();
            this.TextSearchVersion.TextChanged += (p1, p2) => TextSearchVersion_TextChanged();
            this.TextSearchVersion.DropDownClosed += (p1, p2) => UpdateSearchLoaderVisibility();
            this.BtnSearchInstallModPack.Click += (p1, p2) => BtnSearchInstallModPack_Click((object)p1, (EventArgs)p2);
            Loader = ResourceProviderSearch.GetLoader(LoaderInput);
            this.Loaded += PageResource_Loaded;
        }

        private void PageResource_Loaded(object sender, EventArgs e)
        {
            // 不知道从 Initialized 改成 Loaded 会不会有问题，但用 Initialized 会导致初始的筛选器修改被覆盖回默认值
            if (TargetInstance is not null)
            {
                // 设置目标
                ResetFilter(); // 重置筛选器
                this.TextSearchVersion.Text = TargetInstance.Version.VanillaName;
                MyComboBoxItem GetTargetItemByName(string Name)
                {
                    foreach (MyComboBoxItem Item in this.ComboSearchLoader.Items)
                    {
                        if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(Item.Content, Name, false)))
                            return Item;
                    }
                    return (MyComboBoxItem)this.ComboSearchLoader.Items[0];
                };
                if (TargetInstance.Version.ModLoaders.HasFlagF(ModLoaders.Forge))
                {
                    this.ComboSearchLoader.SelectedItem = GetTargetItemByName("Forge");
                }
                else if (TargetInstance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
                {
                    this.ComboSearchLoader.SelectedItem = GetTargetItemByName("Fabric");
                }
                else if (TargetInstance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
                {
                    this.ComboSearchLoader.SelectedItem = GetTargetItemByName("NeoForge");
                }
                TargetInstance = null;
                if (TargetName is not null)
                {
                    this.TextSearchName.Text = TargetName;
                    TargetName = null;
                }
                // 如果已经完成请求，则重新开始
                if (_PageResource_Loaded_IsFirstLoaded)
                    StartNewSearch();
                this.ScrollToHome();
            }
            if (TargetVersion is not null)
            {
                ResetFilter();
                this.TextSearchVersion.Text = TargetVersion;
                TargetVersion = null;
                if (_PageResource_Loaded_IsFirstLoaded)
                    StartNewSearch();
                this.ScrollToHome();
            }
            // 加载器初始化
            if (_PageResource_Loaded_IsFirstLoaded)
                    return;
            _PageResource_Loaded_IsFirstLoaded = true;
            ApplySourceAvailability();
            ((MyPageRight)this.Parent).PageLoaderInit(this.Load, this.PanLoad, this.PanContent, this.PanAlways, Loader, (_) => Load_OnFinish(), this.LoaderInput);
            // 将最高 Drop 加入筛选
            if (ModDownload.AllDrops is not null && ModDownload.AllDrops.First() > 250)
            {
                string HighestVersion = McVersion.DropToVersion(ModDownload.AllDrops.First());
                if ((((MyComboBoxItem)this.TextSearchVersion.Items[1]).Content.ToString() ?? "") != (HighestVersion ?? "")) // 0 是全部
                {
                    this.TextSearchVersion.Items.Insert(1, new MyComboBoxItem() { Content = HighestVersion });
                }
            }
        }
        private ResourceSearcher.SearchRequest LoaderInput()
        {
            var Request = new ResourceSearcher.SearchRequest(PageType, Storage, (Page + 1) * PAGE_SIZE);
            string GameVersion = this.TextSearchVersion.Text == "全部 (也可自行输入)" ? (string)null : this.TextSearchVersion.Text.Contains(".") || this.TextSearchVersion.Text.Contains("w") ? this.TextSearchVersion.Text : (string)null;
            string Notice = null;
            Request.SearchText = this.TextSearchName.Text;
            Request.GameVersion = GameVersion;
            Request.Tag = Conversions.ToString(((dynamic)this.ComboSearchTag.SelectedItem).Tag);
            Request.ModLoaders = (ModLoaders)Math.Round(PageType == ResourceTypes.Mod ? ModBase.Val(((dynamic)this.ComboSearchLoader.SelectedItem).Tag) : (double)ModLoaders.None);
            // 未配置 CurseForge Key 时裁掉该源，避免向 CurseForge 发必然 403 的请求
            Request.Sources = ResourceSourceGate.ResolveSources((ResourcePlatforms)Math.Round(ModBase.Val(((dynamic)this.ComboSearchSource.SelectedItem).Tag)), ref Notice);
            return Request;
        }

        /// <summary>
    /// 未配置 CurseForge API Key 时，在页面上明示降级：显示提示条并禁用源选择器中的 CurseForge 项。
    /// 源可用性在进程内不会变化，首次加载时处理一次即可。
    /// </summary>
        private void ApplySourceAvailability()
        {
            try
            {
                if (ResourceSourceGate.CurseForgeAvailable)
                    return;
                this.HintSource.Text = ResourceSourceGate.DegradationNotice + "\r\n" + "如需启用，请设置环境变量 PCL_CURSEFORGE_API_KEY 后重启启动器。";
                this.HintSource.Visibility = Visibility.Visible;
                bool CurseForgeSelected = false;
                foreach (MyComboBoxItem Item in this.ComboSearchSource.Items)
                {
                    if (Item.Content?.ToString() == "CurseForge")
                    {
                        // 必须在改文案前判断选中态：Content 一旦改掉，后续再也匹配不上
                        CurseForgeSelected = object.ReferenceEquals(this.ComboSearchSource.SelectedItem, Item);
                        Item.Content = "CurseForge（未启用）";
                        Item.IsEnabled = false;
                    }
                }
                // 原本选中的是 CurseForge，回退到「全部」（实际只会走 Modrinth）
                if (CurseForgeSelected)
                    this.ComboSearchSource.SelectedIndex = 0;
                Logger.Info("未检测到 CurseForge API Key，资源搜索已降级为仅 Modrinth 源");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "应用资源源可用性提示失败");
            }
        }

        #endregion

        public ResourceSearcher.SearchResult Storage = new ResourceSearcher.SearchResult();
        /// <summary>
    /// 每页展示的结果数量。
    /// </summary>
        public const int PAGE_SIZE = 40;
        public int Page = 0;

        // 结果 UI 化
        private void Load_OnFinish()
        {
            try
            {
                Logger.Info($"开始可视化{TypeNameSpaced}列表，已储藏 {Storage.Results.Count} 个结果，当前在第 {Page + 1} 页");
                // 列表项
                this.PanProjects.Children.Clear();
                if (Storage.Results.Count == 0)
                {
                    this.CardPages.Visibility = Visibility.Collapsed;
                    this.HintError.Visibility = Storage.ErrorMessage is null ? Visibility.Collapsed : Visibility.Visible;
                    if (Storage.ErrorMessage is not null)
                        this.HintError.Text = Storage.ErrorMessage;
                    this.ScrollToTop();
                    return;
                }
                int Index = Math.Min(Page * PAGE_SIZE, Storage.Results.Count - 1);
                foreach (var Result in Storage.Results.GetRange(Index, Math.Min(Storage.Results.Count - Index, PAGE_SIZE)))
                    this.PanProjects.Children.Add(Result.ToResourceItem(ShowMcVersionDesc: Loader.Input.GameVersion is null, ShowLoaderDesc: Loader.Input.ModLoaders == ModLoaders.None && (PageType == ResourceTypes.Mod || PageType == ResourceTypes.ModPack)));
                // 页码
                this.CardPages.Visibility = Storage.Results.Count > 40 || Storage.CurseForgeOffset < Storage.CurseForgeTotal || Storage.ModrinthOffset < Storage.ModrinthTotal ? Visibility.Visible : Visibility.Collapsed;
                this.LabPage.Text = (Page + 1).ToString();
                this.BtnPageFirst.IsEnabled = Page > 1;
                this.BtnPageFirst.Opacity = Page > 1 ? 1d : 0.2d;
                this.BtnPageLeft.IsEnabled = Page > 0;
                this.BtnPageLeft.Opacity = Page > 0 ? 1d : 0.2d;
                bool IsRightEnabled = Storage.Results.Count > PAGE_SIZE * (Page + 1) || Storage.CurseForgeOffset < Storage.CurseForgeTotal || Storage.ModrinthOffset < Storage.ModrinthTotal; // 由于 WPF 的未知 bug，读取到的 IsEnabled 可能是错误的值（#3319）
                this.BtnPageRight.IsEnabled = IsRightEnabled;
                this.BtnPageRight.Opacity = IsRightEnabled ? 1d : 0.2d;
                // 错误信息
                if (Storage.ErrorMessage is null)
                {
                    this.HintError.Visibility = Visibility.Collapsed;
                }
                else
                {
                    this.HintError.Visibility = Visibility.Visible;
                    this.HintError.Text = Storage.ErrorMessage;
                }
                // 强制返回顶部
                this.ScrollToTop();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"可视化{TypeNameSpaced}列表出错");
            }
        }

        // 自动重试
        private void Load_State(object sender, MyLoading.MyLoadingState state, MyLoading.MyLoadingState oldState)
        {
            switch (Loader.State)
            {
                case ModBase.LoadState.Failed:
                    {
                        string ErrorMessage = "";
                        if (Loader.Error is not null)
                            ErrorMessage = Loader.Error.Message;
                        if (ErrorMessage.Contains("不是有效的 json 文件"))
                        {
                            Logger.Warn($"下载的{TypeNameSpaced}列表 json 文件损坏，已自动重试");
                            ((MyPageRight)this.Parent).PageLoaderRestart();
                        }

                        break;
                    }
            }
        }

        // 切换页码
        private void BtnPageFirst_Click(object sender, RoutedEventArgs e)
        {
            ChangePage(0);
        }
        private void BtnPageLeft_Click(object sender, RoutedEventArgs e)
        {
            ChangePage(Page - 1);
        }
        private void BtnPageRight_Click(object sender, RoutedEventArgs e)
        {
            ChangePage(Page + 1);
        }
        private void ChangePage(int NewPage)
        {
            this.CardPages.IsEnabled = false;
            Page = NewPage;
            ModMain.FrmMain.BackToTop();
            Logger.Info($"{TypeName}：切换到第 {Page + 1} 页");
            ModBase.RunInThread(() =>
        {
            Thread.Sleep(100); // 等待向上滚的动画结束
            ModBase.RunInUi(() => this.CardPages.IsEnabled = true);
            Loader.Start();
        });
        }

        #region 搜索

        // 搜索按钮
        private void StartNewSearch()
        {
            Page = 0;
            var argInput = LoaderInput();
            if (Loader.ShouldStart(ref argInput))
                Storage = new ResourceSearcher.SearchResult(); // 避免连续搜索两次导致 ProjectStorage 引用丢失（#1311）
            Loader.Start();
        }
        private void EnterTrigger(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                StartNewSearch();
        }

        // 重置按钮
        private void ResetFilter()
        {
            this.TextSearchName.Text = "";
            this.TextSearchVersion.Text = "全部 (也可自行输入)";
            this.TextSearchVersion.SelectedIndex = 0;
            this.ComboSearchSource.SelectedIndex = 0;
            this.ComboSearchTag.SelectedIndex = 0;
            this.ComboSearchLoader.SelectedIndex = 0;
            Loader.LastFinishedTime = 0L; // 要求强制重新开始
        }

        // 版本选择
        // #3067：当下拉菜单展开时，程序会被 WPF 挂起，因而无法更新 Grid 布局，所以必须延迟到下拉菜单收起后才能更新
        private void TextSearchVersion_TextChanged()
        {
            if (!this.TextSearchVersion.IsDropDownOpen)
                UpdateSearchLoaderVisibility();
        }
        private void UpdateSearchLoaderVisibility()
        {
            if (PageType == ResourceTypes.Mod && (this.TextSearchVersion.Text.Contains(".") || this.TextSearchVersion.Text.Contains("w")))
            {
                this.ComboSearchLoader.Visibility = Visibility.Visible;
                Grid.SetColumnSpan(this.TextSearchVersion, 1);
            }
            else
            {
                this.ComboSearchLoader.Visibility = Visibility.Collapsed;
                Grid.SetColumnSpan(this.TextSearchVersion, 2);
                this.ComboSearchLoader.SelectedIndex = 0;
            }
        }

        #endregion

        // 安装已有整合包按钮
        private void BtnSearchInstallModPack_Click(object sender, EventArgs e)
        {
            ModModpack.ModpackInstall();
        }

    }
}