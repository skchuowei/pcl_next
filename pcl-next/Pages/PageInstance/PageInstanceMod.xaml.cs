using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
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
    public partial class PageInstanceMod : IRefreshable
    {

        #region 初始化

        private bool IsLoad = false;

        public PageInstanceMod()
        {
            InitializeComponent();
            this.Load.Click += (p1, p2) => Load_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnManageOpen.Click += (p1, p2) => BtnManageOpen_Click((object)p1, (EventArgs)p2);
            this.BtnHintOpen.Click += (p1, p2) => BtnManageOpen_Click((object)p1, (EventArgs)p2);
            this.BtnManageSelectAll.Click += (p1, p2) => BtnManageSelectAll_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnManageInstall.Click += (p1, p2) => BtnManageInstall_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnHintInstall.Click += (p1, p2) => BtnManageInstall_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnManageDownload.Click += (p1, p2) => BtnManageDownload_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnHintDownload.Click += (p1, p2) => BtnManageDownload_Click((object)p1, (MouseButtonEventArgs)p2);
            this.Load.StateChanged += (p1, p2, p3) => UnselectedAllWithAnimation();
            this.SearchBox.PreviewKeyDown += (p1, p2) => SearchBox_PreviewKeyDown((object)p1, (KeyEventArgs)p2);
            this.BtnFilterAll.Check += (p1, p2) => ChangeFilter((MyRadioButton)p1, (bool)p2);
            this.BtnFilterCanUpdate.Check += (p1, p2) => ChangeFilter((MyRadioButton)p1, (bool)p2);
            this.BtnFilterDisabled.Check += (p1, p2) => ChangeFilter((MyRadioButton)p1, (bool)p2);
            this.BtnFilterEnabled.Check += (p1, p2) => ChangeFilter((MyRadioButton)p1, (bool)p2);
            this.BtnSelectEnable.Click += (p1, p2) => BtnSelectED_Click((MyIconTextButton)p1, (ModBase.RouteEventArgs)p2);
            this.BtnSelectDisable.Click += (p1, p2) => BtnSelectED_Click((MyIconTextButton)p1, (ModBase.RouteEventArgs)p2);
            this.BtnSelectUpdate.Click += (p1, p2) => BtnSelectUpdate_Click();
            this.BtnSelectDelete.Click += (p1, p2) => BtnSelectDelete_Click();
            this.BtnSelectCancel.Click += (p1, p2) => BtnSelectCancel_Click();
            this.SearchBox.TextChanged += (p1, p2) => SearchRun();
            this.Loaded += (_, __) => PageOther_Loaded();
            this.Initialized += (_, __) => LoaderInit();
            this.PageExit += UnselectedAllWithAnimation;
        }
        public void PageOther_Loaded()
        {

            if (ModMain.FrmMain.PageLast.Page != FormMain.PageType.ResourceDetail)
                this.PanBack.ScrollToHome();
            ModAnimation.AniControlEnabled += 1;
            SelectedMods.Clear();
            ReloadModList();
            ChangeAllSelected(false);
            ModAnimation.AniControlEnabled -= 1;

            // 非重复加载部分
            if (IsLoad)
                return;
            IsLoad = true;

            global::PCL.ModMain.FrmMain.KeyDown += this.FrmMain_KeyDown;
            // 调整按钮边距（这玩意儿没法从 XAML 改）
            foreach (MyRadioButton Btn in this.PanFilter.Children)
                Btn.LabText.Margin = new Thickness(-2, 0d, 8d, 0d);

        }
        /// <summary>
    /// 刷新 Mod 列表。
    /// </summary>
        public void ReloadModList(bool ForceReload = false)
        {
            if (!LoaderRun(ForceReload ? ModLoader.LoaderFolderRunType.ForceRun : ModLoader.LoaderFolderRunType.RunOnUpdated))
                return;
            Logger.Info("已刷新 Mod 列表");
            Filter = FilterType.All;
            this.PanBack.ScrollToHome();
            this.SearchBox.Text = "";
        }
        // 强制刷新
        private void RefreshSelf()
        {
            Refresh();
        }

        void IRefreshable.Refresh() => RefreshSelf();
        public static void Refresh()
        {
            try
            {
                ResourceProject.Cache.Clear();
                ResourceVersion.ClearCaches();
                FileUtils.Delete(ModBase.PathTemp + @"Cache\LocalMod.json");
                Logger.Info("由于点击刷新按钮，清理本地 Mod 信息缓存");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "强制刷新时清理本地 Mod 信息缓存失败");
            }
            if (ModMain.FrmInstanceMod is not null)
                ModMain.FrmInstanceMod.ReloadModList(true); // 无需 Else，还没加载刷个鬼的新
            ModMain.FrmInstanceLeft.ItemMod.Checked = true;
            ModMain.Hint("正在刷新……", Log: false);
        }

        private void LoaderInit()
        {
            this.PageLoaderInit(this.Load, this.PanLoad, this.PanAllBack, (FrameworkElement)null, LocalResourceLoaders.LocalResourceLoader, (_) => LoadUIFromLoaderOutput(), AutoRun: false);
        }
        private void Load_Click(object sender, MouseButtonEventArgs e)
        {
            if (LocalResourceLoaders.LocalResourceLoader.State == ModBase.LoadState.Failed)
                LoaderRun(ModLoader.LoaderFolderRunType.ForceRun);
        }
        public bool LoaderRun(ModLoader.LoaderFolderRunType Type)
        {
            return ModLoader.LoaderFolderRun(LocalResourceLoaders.LocalResourceLoader, PageInstanceLeft.Instance.PathIndie + @"mods\", Type);
        }

        #endregion

        #region UI 化

        /// <summary>
    /// 已加载的 Mod UI 缓存，不确保按显示顺序排列。Key 为 Mod 的 RawFileName。
    /// </summary>
        public ConcurrentDictionary<string, MyLocalModItem> ModItems = new ConcurrentDictionary<string, MyLocalModItem>();
        /// <summary>
    /// 将加载器结果的 Mod 列表加载为 UI。
    /// </summary>
        private void LoadUIFromLoaderOutput()
        {
            try
            {
                // 判断应该显示哪一个页面
                if (LocalResourceLoaders.LocalResourceLoader.Output.Any())
                {
                    this.PanBack.Visibility = Visibility.Visible;
                    this.PanEmpty.Visibility = Visibility.Collapsed;
                }
                else
                {
                    this.PanEmpty.Visibility = Visibility.Visible;
                    this.PanBack.Visibility = Visibility.Collapsed;
                    return;
                }
                // 修改缓存
                ModItems.Clear();
                foreach (LocalResourceFile ModEntity in LocalResourceLoaders.LocalResourceLoader.Output)
                    ModItems[ModEntity.EnabledName] = McModListItem(ModEntity);
                // 显示结果
                Filter = FilterType.All;
                this.SearchBox.Text = ""; // 这会触发结果刷新，所以需要在 ModItems 更新之后，详见 #3124 的视频
                RefreshUI();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "加载 Mod 列表 UI 失败");
            }
        }
        private MyLocalModItem McModListItem(LocalResourceFile Entry)
        {
            ModAnimation.AniControlEnabled += 1;
            var NewItem = new MyLocalModItem()
            {
                SnapsToDevicePixels = true,
                Entry = Entry,
                ButtonHandler = McModContent,
                Checked = SelectedMods.Contains(Entry.EnabledName)
            };
            Entry.OnUpdate += (_) => ModBase.RunInUi(NewItem.Refresh);
            ModAnimation.AniControlEnabled -= 1;
            return NewItem;
        }
        private void McModContent(MyLocalModItem sender, EventArgs e)
        {
            // 点击事件
            sender.Changed += (_, __) => this.CheckChanged((dynamic)_, (dynamic)__);
            sender.Click += (ss, ee) => ((dynamic)ss).Checked = !((dynamic)ss).Checked;
            // 图标按钮
            var BtnOpen = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonOpen, Tag = sender };
            BtnOpen.ToolTip = "打开文件位置";
            BtnOpen.Click += (_, __) => this.Open_Click((dynamic)_, (dynamic)__);
            var BtnCont = new MyIconButton() { LogoScale = 1d, Logo = ModBase.Logo.IconButtonInfo, Tag = sender };
            BtnCont.ToolTip = "详情";
            BtnCont.Click += Info_Click;
            sender.MouseRightButtonUp += this.Info_Click;
            var BtnDelete = new MyIconButton() { LogoScale = 1d, Logo = ModBase.Logo.IconButtonDelete, Tag = sender };
            BtnDelete.ToolTip = "删除";
            BtnDelete.Click += (_, __) => this.Delete_Click((dynamic)_, (dynamic)__);
            var BtnED = new MyIconButton() { LogoScale = 1d, Logo = sender.Entry.IsEnabled ? ModBase.Logo.IconButtonStop : ModBase.Logo.IconButtonCheck, Tag = sender };
            BtnED.ToolTip = sender.Entry.IsEnabled ? "禁用" : "启用";
            BtnED.Click += (_, __) => this.ED_Click((dynamic)_, (dynamic)__);
            sender.Buttons = new[] { BtnCont, BtnOpen, BtnED, BtnDelete };
        }

        /// <summary>
    /// 刷新整个 UI。
    /// </summary>
        public void RefreshUI()
        {
            if (this.PanList is null)
                return;
            var ShowingMods = (IsSearching ? SearchResult : LocalResourceLoaders.LocalResourceLoader.Output ?? new List<LocalResourceFile>()).Where(m => CanPassFilter(m)).ToList();
            // 重新列出列表
            ModAnimation.AniControlEnabled += 1;
            if (ShowingMods.Any())
            {
                this.PanList.Visibility = Visibility.Visible;
                this.PanList.Children.Clear();
                foreach (var TargetMod in ShowingMods)
                {
                    MyLocalModItem Item = null;
                    if (!ModItems.TryGetValue(TargetMod.EnabledName, out Item))
                        continue;
                    Item.Checked = SelectedMods.Contains(TargetMod.EnabledName); // 更新选中状态
                    this.PanList.Children.Add(Item);
                }
            }
            else
            {
                this.PanList.Visibility = Visibility.Collapsed;
            }
            ModAnimation.AniControlEnabled -= 1;
            SelectedMods = SelectedMods.Where(m => ShowingMods.Any(s => (s.EnabledName ?? "") == (m ?? ""))).ToList(); // 取消选中已经不显示的 Mod
            RefreshBars();
        }

        /// <summary>
    /// 刷新顶栏和底栏显示。
    /// </summary>
        public void RefreshBars()
        {

            // -----------------
            // 顶部栏
            // -----------------

            // 计数
            int AnyCount = 0;
            int EnabledCount = 0;
            int UpdateCount = 0;
            foreach (var ModItem in IsSearching ? SearchResult : LocalResourceLoaders.LocalResourceLoader.Output ?? new List<LocalResourceFile>())
            {
                AnyCount += 1;
                if (ModItem.HasUpdate)
                    UpdateCount += 1;
                if (ModItem.IsEnabled)
                    EnabledCount += 1;
            }
            int DisabledCount = AnyCount - EnabledCount;
            // 显示
            this.BtnFilterAll.Text = (IsSearching ? "搜索结果" : "全部") + $" ({AnyCount})";
            this.BtnFilterCanUpdate.Text = $"可更新 ({UpdateCount})";
            this.BtnFilterCanUpdate.Visibility = Filter == FilterType.CanUpdate || UpdateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            this.BtnFilterEnabled.Text = $"启用 ({EnabledCount})";
            this.BtnFilterEnabled.Visibility = Filter == FilterType.Enabled || EnabledCount > 0 && EnabledCount < AnyCount ? Visibility.Visible : Visibility.Collapsed;
            this.BtnFilterDisabled.Text = $"禁用 ({DisabledCount})";
            this.BtnFilterDisabled.Visibility = Filter == FilterType.Disabled || DisabledCount > 0 ? Visibility.Visible : Visibility.Collapsed;

            // -----------------
            // 底部栏
            // -----------------

            // 计数
            int NewCount = SelectedMods.Count;
            bool Selected = NewCount > 0;
            if (Selected)
                this.LabSelect.Text = $"已选择 {NewCount} 个文件"; // 取消所有选择时不更新数字
                                                             // 按钮可用性
            if (Selected)
            {
                bool HasUpdate = false;
                bool HasEnabled = false;
                bool HasDisabled = false;
                foreach (var ModEntity in LocalResourceLoaders.LocalResourceLoader.Output)
                {
                    if (!SelectedMods.Contains(ModEntity.EnabledName))
                        continue;
                    if (ModEntity.HasUpdate)
                        HasUpdate = true;
                    if (ModEntity.IsEnabled)
                        HasEnabled = true;
                    if (!ModEntity.IsEnabled)
                        HasDisabled = true;
                }
                this.BtnSelectDisable.IsEnabled = HasEnabled;
                this.BtnSelectEnable.IsEnabled = HasDisabled;
                this.BtnSelectUpdate.IsEnabled = HasUpdate;
            }
            // 更新显示状态
            if (ModAnimation.AniControlEnabled == 0)
            {
                this.PanListBack.Margin = new Thickness(0d, 0d, 0d, Selected ? 95 : 15);
                if (Selected)
                {
                    // 仅在数量增加时播放出现/跳跃动画
                    if (BottomBarShownCount >= NewCount)
                    {
                        BottomBarShownCount = NewCount;
                        return;
                    }
                    else
                    {
                        BottomBarShownCount = NewCount;
                    }
                    // 出现/跳跃动画
                    this.CardSelect.Visibility = Visibility.Visible;
                    ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.CardSelect, 1d - this.CardSelect.Opacity, 60), ModAnimation.AaTranslateY(this.CardSelect, (double)-27 - this.TransSelect.Y, 120, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaTranslateY(this.CardSelect, 3d, 150, 120, Ease: new ModAnimation.AniEaseInoutFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaTranslateY(this.CardSelect, (double)-1, 90, 270, Ease: new ModAnimation.AniEaseInoutFluent(ModAnimation.AniEasePower.Weak)) }, "Mod Sidebar");
                }
                else
                {
                    // 不重复播放隐藏动画
                    if (BottomBarShownCount == 0)
                        return;
                    BottomBarShownCount = 0;
                    // 隐藏动画
                    ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.CardSelect, -this.CardSelect.Opacity, 90), ModAnimation.AaTranslateY(this.CardSelect, (double)-10 - this.TransSelect.Y, 90, Ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaCode(() => this.CardSelect.Visibility = Visibility.Collapsed, After: true) }, "Mod Sidebar");
                }
            }
            else
            {
                ModAnimation.AniStop("Mod Sidebar");
                BottomBarShownCount = NewCount;
                if (Selected)
                {
                    this.CardSelect.Visibility = Visibility.Visible;
                    this.CardSelect.Opacity = 1d;
                    this.TransSelect.Y = (double)-25;
                }
                else
                {
                    this.CardSelect.Visibility = Visibility.Collapsed;
                    this.CardSelect.Opacity = 0d;
                    this.TransSelect.Y = (double)-10;
                }
            }
        }
        private int BottomBarShownCount = 0;

        #endregion

        #region 管理

        /// <summary>
    /// 打开 Mods 文件夹。
    /// </summary>
        private void BtnManageOpen_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryUtils.Create(PageInstanceLeft.Instance.PathIndie + @"mods\");
                ModBase.OpenExplorer(PageInstanceLeft.Instance.PathIndie + @"mods\");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "打开 Mods 文件夹失败", LogBehavior.Alert);
            }
        }

        /// <summary>
    /// 全选。
    /// </summary>
        private void BtnManageSelectAll_Click(object sender, MouseButtonEventArgs e)
        {
            ChangeAllSelected(SelectedMods.Count < this.PanList.Children.Count);
        }

        /// <summary>
    /// 安装 Mod。
    /// </summary>
        private void BtnManageInstall_Click(object sender, MouseButtonEventArgs e)
        {
            var FileList = Dialogs.SelectFile("选择要安装的 Mod", true, filter: new[] { (new[] { "jar", "litemod", "disabled", "old" }, "Mod 文件") });
            if (!FileList.Any())
                return;
            InstallMods(FileList);
        }
        /// <summary>
    /// 尝试安装 Mod。
    /// 返回输入的文件是否为一个 Mod 文件，仅用于判断拖拽行为。
    /// </summary>
        public static bool InstallMods(IEnumerable<string> FilePathList)
        {
            string Extension = PathUtils.GetExtension(FilePathList.First());
            // 检查文件扩展名
            if (!new[] { "jar", "litemod", "disabled", "old" }.Any(t => (t ?? "") == (Extension ?? "")))
                return false;
            Logger.Info("文件为 jar/litemod 格式，尝试作为 Mod 安装");
            // 检查回收站：回收站中的文件有错误的文件名
            if (FilePathList.First().Contains(@":\$RECYCLE.BIN\"))
            {
                ModMain.Hint("请先将文件从回收站还原，再尝试安装！", ModMain.HintType.Red);
                return true;
            }
            // 获取并检查目标版本
            var Instance = ModMinecraft.McInstanceSelected;
            if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSetup)
                Instance = PageInstanceLeft.Instance;
            // VB 原文从「未处于 Mod 管理页面」分支用 GoTo Install 跳进 else 分支体内的标签，
            // C# 不允许跨块跳转，改为标记位 DoInstall + 顺序 if，三个分支的取舍与原文一致
            bool DoInstall = false;
            if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSelect || Instance is null || !Instance.Modable)
            {
                // 正在选择版本，或当前版本不能安装 Mod
                ModMain.Hint("若要安装 Mod，请先选择一个可以安装 Mod 的版本！");
            }
            else if (!(ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSetup && ModMain.FrmMain.PageCurrentSub == FormMain.PageSubType.InstanceMod))
            {
                // 未处于 Mod 管理页面
                if (ModMain.MyMsgBox($"是否要将这{(FilePathList.IsSingle() ? "个" : "些")}文件作为 Mod 安装到 {Instance.Name}？", "Mod 安装确认", "确定", "取消") == 1)
                    DoInstall = true; // 等价于原文的 GoTo Install
            }
            else
            {
                // 处于 Mod 管理页面
                DoInstall = true;
            }
            if (DoInstall)
            {
                try
                {
                    foreach (var ModFile in FilePathList)
                    {
                        string NewFileName = new LocalResourceFile(ModFile).EnabledName;
                        if (!NewFileName.Contains("."))
                            NewFileName += ".jar"; // #4227
                        NewFileName = Instance.PathIndie + @"mods\" + NewFileName;
                        if (PathUtils.IsSame(ModFile, NewFileName))
                            continue; // 防止安装相同文件导致文件被删除
                        FileUtils.Delete(NewFileName, true);
                        FileUtils.Copy(ModFile, NewFileName);
                    }
                    if (FilePathList.IsSingle())
                    {
                        ModMain.Hint($"已安装 {new LocalResourceFile(FilePathList.First()).EnabledName}！", ModMain.HintType.Green);
                    }
                    else
                    {
                        ModMain.Hint($"已安装 {FilePathList.Count()} 个 Mod！", ModMain.HintType.Green);
                    }
                    // 刷新列表
                    if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSetup && ModMain.FrmMain.PageCurrentSub == FormMain.PageSubType.InstanceMod)
                    {
                        ModLoader.LoaderFolderRun(LocalResourceLoaders.LocalResourceLoader, Instance.PathIndie + @"mods\", ModLoader.LoaderFolderRunType.ForceRun);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "复制 Mod 文件失败", LogBehavior.Alert);
                }
            }
            return true;
        }

        /// <summary>
    /// 下载 Mod。
    /// </summary>
        private void BtnManageDownload_Click(object sender, MouseButtonEventArgs e)
        {
            // 预设置当前版本和搜索框内容
            PageResource.TargetInstance = PageInstanceLeft.Instance;
            PageResource.TargetName = this.SearchBox.Text;
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Download, FormMain.PageSubType.DownloadMod);
        }

        #endregion

        #region 选择

        // 选择的 Mod 的路径（不含 .disabled 和 .old）
        public List<string> SelectedMods = new List<string>();

        // 单项切换选择状态
        public void CheckChanged(MyLocalModItem sender, ModBase.RouteEventArgs e)
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            // 更新选择了的内容
            string SelectedKey = sender.Entry.EnabledName;
            if (sender.Checked)
            {
                if (!SelectedMods.Contains(SelectedKey))
                    SelectedMods.Add(SelectedKey);
            }
            else
            {
                SelectedMods.Remove(SelectedKey);
            }
            RefreshBars();
        }

        // 切换所有项的选择状态
        private void ChangeAllSelected(bool Value)
        {
            ModAnimation.AniControlEnabled += 1;
            SelectedMods.Clear();
            foreach (MyLocalModItem Item in ModItems.Values)
            {
                // #4992，Mod 从过滤器看可能不应在列表中，但因为刚切换状态所以依然保留在列表中，所以应该从列表 UI 判断，而非从过滤器判断
                bool ShouldSelected = Value && this.PanList.Children.Contains(Item);
                Item.Checked = ShouldSelected;
                if (ShouldSelected)
                    SelectedMods.Add(Item.Entry.EnabledName);
            }
            ModAnimation.AniControlEnabled -= 1;
            RefreshBars();
        }
        private void UnselectedAllWithAnimation()
        {
            int CacheAniControlEnabled = ModAnimation.AniControlEnabled;
            ModAnimation.AniControlEnabled = 0;
            ChangeAllSelected(false);
            ModAnimation.AniControlEnabled += CacheAniControlEnabled;
        }
        private void FrmMain_KeyDown(object sender, KeyEventArgs e) // 监听自己的事件的话进入页面后不点击右侧控件就没办法监听到事件 (#4311)
        {
            if (!ReferenceEquals(ModMain.FrmMain.PageRight, this))
                return;
            if (My.Computer.Keyboard.CtrlKeyDown && e.Key == Key.A)
                ChangeAllSelected(true);
        }
        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl + A 会被搜索框捕获，导致无法全选，所以在按下 Ctrl + A 时转移焦点以便捕获
            if (SearchBox.Text.Any())
                return;
            if (My.Computer.Keyboard.CtrlKeyDown && e.Key == Key.A)
                this.PanBack.Focus();
        }

        #endregion

        #region 筛选

        private FilterType _Filter = FilterType.All;
        public FilterType Filter
        {
            get
            {
                return _Filter;
            }
            set
            {
                if (_Filter == value)
                    return;
                _Filter = value;
                switch (value)
                {
                    case FilterType.All:
                        {
                            this.BtnFilterAll.Checked = true;
                            break;
                        }
                    case FilterType.Enabled:
                        {
                            this.BtnFilterEnabled.Checked = true;
                            break;
                        }
                    case FilterType.Disabled:
                        {
                            this.BtnFilterDisabled.Checked = true;
                            break;
                        }

                    default:
                        {
                            this.BtnFilterCanUpdate.Checked = true;
                            break;
                        }
                }
                RefreshUI();
            }
        }
        public enum FilterType : int
        {
            All = 0,
            Enabled = 1,
            Disabled = 2,
            CanUpdate = 3
        }

        /// <summary>
    /// 检查该 Mod 项是否符合当前筛选的类别。
    /// </summary>
        private bool CanPassFilter(LocalResourceFile CheckingMod)
        {
            switch (Filter)
            {
                case FilterType.All:
                    {
                        return true;
                    }
                case FilterType.Enabled:
                    {
                        return CheckingMod.IsEnabled;
                    }
                case FilterType.Disabled:
                    {
                        return !CheckingMod.IsEnabled;
                    }
                case FilterType.CanUpdate:
                    {
                        return CheckingMod.HasUpdate;
                    }

                default:
                    {
                        return false;
                    }
            }
        }

        // 点击筛选项触发的改变
        private void ChangeFilter(MyRadioButton sender, bool raiseByMouse)
        {
            Filter = (FilterType)Conversions.ToInteger(sender.Tag);
            RefreshUI();
        }

        #endregion

        #region 下边栏

        // 启用 / 禁用
        private void BtnSelectED_Click(MyIconTextButton sender, ModBase.RouteEventArgs e)
        {
            EDMods(LocalResourceLoaders.LocalResourceLoader.Output.Where(m => SelectedMods.Contains(m.EnabledName)), !sender.Equals(this.BtnSelectDisable));
            ChangeAllSelected(false);
        }
        private void EDMods(IEnumerable<LocalResourceFile> ModList, bool IsEnable)
        {
            bool IsSuccessful = true;
            foreach (var ModE in ModList.ToList())
            {
                var ModEntity = ModE; // 仅用于去除迭代变量无法修改的限制
                string NewPath = null;
                if (ModEntity.IsEnabled && !IsEnable)
                {
                    // 禁用
                    NewPath = PathUtils.RemoveExtendedPrefix(ModEntity.File.FullName) + (FileUtils.Exists(ModEntity.File.FullName + ".old") ? ".old" : ".disabled");
                }
                else if (!ModEntity.IsEnabled && IsEnable)
                {
                    // 启用
                    NewPath = ModEntity.EnabledFullName;
                }
                else
                {
                    continue;
                }
                // 重命名
                try
                {
                    if (FileUtils.Exists(NewPath))
                    {
                        if (FileUtils.Exists(ModEntity.File.FullName))
                        {
                            // 同时存在两个名称的 Mod
                            if ((CryptographyUtils.ComputeFileHash(ModEntity.File.FullName) ?? "") != (CryptographyUtils.ComputeFileHash(NewPath) ?? ""))
                            {
                                ModMain.MyMsgBox($"目前同时存在启用和禁用的两个 Mod 文件：{Constants.vbCrLf} - {PathUtils.GetLastPart(NewPath)}{Constants.vbCrLf} - {ModEntity.File.Name}{Constants.vbCrLf}{Constants.vbCrLf}注意，这两个文件的内容并不相同。{Constants.vbCrLf}在手动删除或重命名其中一个文件后，才能继续操作。", "存在文件冲突");
                                continue;
                            }
                        }
                        else
                        {
                            // 已经重命名过了
                            Logger.Warn("Mod 的状态已被切换");
                            continue;
                        }
                    }
                    FileUtils.Move(ModEntity.File.FullName, NewPath);
                }
                catch (FileNotFoundException ex)
                {
                    Logger.Error(ex, $"未找到需要重命名的 Mod（{ModEntity.File.FullName ?? "null"}）");
                    ReloadModList(true);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"重命名 Mod 失败（{ModEntity.File.FullName ?? "null"}）");
                    IsSuccessful = false;
                }
                // 更改 Loader 中的列表
                var NewModEntity = new LocalResourceFile(NewPath);
                NewModEntity.FromJson(ModEntity.ToJson());
                if (LocalResourceLoaders.LocalResourceLoader.Output.Contains(ModEntity))
                {
                    int IndexOfLoader = LocalResourceLoaders.LocalResourceLoader.Output.IndexOf(ModEntity);
                    LocalResourceLoaders.LocalResourceLoader.Output.RemoveAt(IndexOfLoader);
                    LocalResourceLoaders.LocalResourceLoader.Output.Insert(IndexOfLoader, NewModEntity);
                }
                if (SearchResult is not null && SearchResult.Contains(ModEntity)) // #4862
                {
                    int IndexOfResult = SearchResult.IndexOf(ModEntity);
                    SearchResult.Remove(ModEntity);
                    SearchResult.Insert(IndexOfResult, NewModEntity);
                }
                // 更改 UI 中的列表
                var NewItem = McModListItem(NewModEntity);
                ModItems[ModEntity.EnabledName] = NewItem;
                int IndexOfUi = this.PanList.Children.IndexOf(PanList.Children.OfType<MyLocalModItem>().FirstOrDefault(i => object.ReferenceEquals(i.Entry, ModEntity)));
                if (IndexOfUi == -1)
                    continue; // 因为未知原因 Mod 的状态已经切换完了
                this.PanList.Children.RemoveAt(IndexOfUi);
                this.PanList.Children.Insert(IndexOfUi, NewItem);
                NewItem.UpdateLayout(); // #7861
            }
            if (IsSuccessful)
            {
                RefreshBars();
            }
            else
            {
                ModMain.Hint("由于文件被占用，Mod 的状态切换失败，请尝试关闭正在运行的游戏后再试！", ModMain.HintType.Red);
                ReloadModList(true);
            }
            LoaderRun(ModLoader.LoaderFolderRunType.UpdateOnly);
        }

        // 更新
        private void BtnSelectUpdate_Click()
        {
            var UpdateList = LocalResourceLoaders.LocalResourceLoader.Output.Where(m => SelectedMods.Contains(m.EnabledName) && m.HasUpdate).ToList();
            if (!UpdateList.Any())
                return;
            UpdateMods(UpdateList);
            ChangeAllSelected(false);
        }
        /// <summary>
    /// 记录正在进行 Mod 更新的 mods 文件夹路径。
    /// </summary>
        public static List<string> UpdatingInstanceModFolders = new List<string>();
        public void UpdateMods(IEnumerable<LocalResourceFile> ModList)
        {
            // 更新前警告
            if (!Settings.Get<bool>("HintUpdateMod") || ModList.Count() >= 15)
            {
                if (ModMain.MyMsgBox($"新版本 Mod 可能不兼容旧存档或者其他 Mod，这可能导致游戏崩溃，甚至永久损坏存档！{Constants.vbCrLf}如果你在游玩整合包，请千万不要自行更新 Mod！{Constants.vbCrLf}{Constants.vbCrLf}在更新前，请先备份存档，并检查 Mod 的更新日志。{Constants.vbCrLf}如果更新后出现问题，你也可以在回收站找回更新前的 Mod。", "Mod 更新警告", "我已了解风险，继续更新", "取消", IsWarn: true) == 1)
                {
                    Settings.Set("HintUpdateMod", true);
                }
                else
                {
                    return;
                }
            }
            try
            {
                // 构造下载信息
                ModList = ModList.ToList(); // 防止刷新影响迭代器
                var FileList = new List<ModNet.NetFile>();
                var FileCopyList = new Dictionary<string, string>();
                foreach (LocalResourceFile Entry in ModList)
                {
                    var File = Entry.UpdateFile;
                    if (!File.DownloadAvailable)
                        continue;
                    // 确认更新后的文件名
                    string CurrentReplaceName = PathUtils.GetFileNameWithoutExtension(Entry.ProjectVersion.FileName);
                    string NewestReplaceName = PathUtils.GetFileNameWithoutExtension(Entry.UpdateFile.FileName);
                    var CurrentSegs = CurrentReplaceName.Split('-').ToList();
                    var NewestSegs = NewestReplaceName.Split('-').ToList();
                    bool Shortened = false;
                    while (true) // 移除前导相同部分（不能移除所有相同项，这会导致例如 1.2-forge-2 和 1.3-forge-3 中间的 forge 被去掉，导致尝试替换 1.2-2）
                    {
                        if (!CurrentSegs.Any() || !NewestSegs.Any())
                            break;
                        if ((CurrentSegs.First() ?? "") != (NewestSegs.First() ?? ""))
                            break;
                        CurrentSegs.RemoveAt(0);
                        NewestSegs.RemoveAt(0);
                        Shortened = true;
                    }
                    while (true) // 移除后导相同部分
                    {
                        if (!CurrentSegs.Any() || !NewestSegs.Any())
                            break;
                        if ((CurrentSegs.Last() ?? "") != (NewestSegs.Last() ?? ""))
                            break;
                        CurrentSegs.RemoveAt(CurrentSegs.Count - 1);
                        NewestSegs.RemoveAt(NewestSegs.Count - 1);
                        Shortened = true;
                    }
                    if (Shortened && CurrentSegs.Any() && NewestSegs.Any())
                    {
                        CurrentReplaceName = CurrentSegs.Join("-");
                        NewestReplaceName = NewestSegs.Join("-");
                    }
                    // 添加到下载列表
                    string TempAddress = ModBase.PathTemp + @"mods\" + Entry.File.Name.Replace(CurrentReplaceName, NewestReplaceName); // 需要下载到 mods 文件夹，以便重复文件检查时识别
                    string RealAddress = Path.Combine(PathUtils.RemoveLastPart(Entry.File.FullName), Entry.File.Name.Replace(CurrentReplaceName, NewestReplaceName));
                    FileList.Add(File.ToNetFile(TempAddress, ResourceVersion.DownloadReason.Update, PageInstanceLeft.Instance.Version.VanillaName, PageInstanceLeft.Instance.Version.ModLoaders));
                    FileCopyList[TempAddress] = RealAddress;
                }
                // 构造加载器
                var InstallLoaders = new List<ModLoader.LoaderBase>();
                var FinishedFileNames = new List<string>();
                InstallLoaders.Add(new ModNet.LoaderDownload("下载新版 Mod 文件", FileList) { ProgressWeight = ModList.Count() * 1.5d }); // 每个 Mod 需要 1.5s
                InstallLoaders.Add(new ModLoader.LoaderTask<int, int>("替换旧版 Mod 文件", (_) => { try { foreach (LocalResourceFile Entry in ModList) { if (FileUtils.Exists(Entry.File.FullName)) { FileUtils.Delete(Entry.File.FullName, true); } else { Logger.Warn($"未找到更新前的 Mod 文件，跳过对它的删除：{Entry.File.FullName}"); } } foreach (KeyValuePair<string, string> Entry in FileCopyList) { if (DirectoryUtils.Exists(PathUtils.RemoveLastPart(Entry.Value))) { FileUtils.Move(Entry.Key, Entry.Value); FinishedFileNames.Add(PathUtils.GetLastPart(Entry.Value)); } else { Logger.Warn($"更新后的目标文件夹已被删除：{Entry.Value}"); } } } catch (OperationCanceledException ex) { Logger.Warn(ex, "替换旧版 Mod 文件时被主动取消"); } }));
                // 结束处理
                var Loader = new ModLoader.LoaderCombo<IEnumerable<LocalResourceFile>>("Mod 更新：" + PageInstanceLeft.Instance.Name, InstallLoaders);
                string PathMods = PageInstanceLeft.Instance.PathIndie + @"mods\";
                Loader.OnStateChanged = new Action<ModLoader.LoaderBase>((_) =>
        {
            // 结果提示
            switch (Loader.State)
            {
                case ModBase.LoadState.Finished:
                    {
                        switch (FinishedFileNames.Count)
                        {
                            case 0: // 一般是由于 Mod 文件被占用，然后玩家主动取消
                                {
                                    Logger.Info($"没有 Mod 被成功更新");
                                    break;
                                }
                            case 1:
                                {
                                    ModMain.Hint($"已成功更新 {FinishedFileNames.Single()}！", ModMain.HintType.Green);
                                    break;
                                }

                            default:
                                {
                                    ModMain.Hint($"已成功更新 {FinishedFileNames.Count} 个 Mod！", ModMain.HintType.Green);
                                    break;
                                }
                        }

                        break;
                    }
                case ModBase.LoadState.Failed:
                    {
                        ModMain.Hint("Mod 更新失败：" + Loader.Error.GetDisplay(false), ModMain.HintType.Red);
                        break;
                    }
                case ModBase.LoadState.Canceled:
                    {
                        ModMain.Hint("Mod 更新已中止！", ModMain.HintType.Blue);
                        break;
                    }

                default:
                    {
                        return;
                    }
            }
            Logger.Info($"已从正在进行 Mod 更新的文件夹列表移除：{PathMods}");
            UpdatingInstanceModFolders.Remove(PathMods);
            // 清理缓存
            ModBase.RunInNewThread(() => { try { foreach (var TempFile in FileCopyList.Keys) FileUtils.Delete(TempFile); } catch (Exception ex) { Logger.Warn(ex, "清理 Mod 更新缓存失败"); } }, "Clean Mod Update Cache", ThreadPriority.BelowNormal);
        });
                // 启动加载器
                Logger.Info($"开始更新 {ModList.Count()} 个 Mod：{Enumerable.Select(ModList, m => m.File.Name).Join("，")}");
                UpdatingInstanceModFolders.Add(PathMods);
                Loader.Start();
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
                ReloadModList(true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "初始化 Mod 更新失败");
            }
        }

        // 删除
        private void BtnSelectDelete_Click()
        {
            DeleteMods(LocalResourceLoaders.LocalResourceLoader.Output.Where(m => SelectedMods.Contains(m.EnabledName)));
            ChangeAllSelected(false);
        }
        private void DeleteMods(IEnumerable<LocalResourceFile> ModsToDelete)
        {
            try
            {
                bool IsSuccessful = true;
                bool IsShiftPressed = My.Computer.Keyboard.ShiftKeyDown;
                // 确认需要删除的文件
                var ModList = ModsToDelete.SelectMany((Target) => { if (Target.IsEnabled) { return new[] { Target.File.FullName, Target.File.FullName + (FileUtils.Exists(Target.File.FullName + ".old") ? ".old" : ".disabled") }; } else { return new[] { Target.File.FullName, Target.EnabledFullName }; } }).Distinct().Where(m => FileUtils.Exists(m)).Select(m => new LocalResourceFile(m)).ToList();
                // 实际删除文件
                foreach (var ModEntity in ModList)
                {
                    // 删除
                    try
                    {
                        FileUtils.Delete(ModEntity.File.FullName, !IsShiftPressed);
                    }
                    catch (OperationCanceledException ex)
                    {
                        Logger.Warn(ex, "删除 Mod 被主动取消");
                        ReloadModList(true);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"删除 Mod 失败（{ModEntity.File.FullName}）", LogBehavior.Alert);
                        IsSuccessful = false;
                    }
                    // 取消选中
                    SelectedMods.Remove(ModEntity.EnabledName);
                    // 更改 Loader 和 UI 中的列表
                    LocalResourceLoaders.LocalResourceLoader.Output.Remove(ModEntity);
                    SearchResult?.Remove(ModEntity);
                    ModItems.Remove(ModEntity.EnabledName);
                    int IndexOfUi = this.PanList.Children.IndexOf(PanList.Children.OfType<MyLocalModItem>().FirstOrDefault(i => i.Entry.Equals(ModEntity)));
                    if (IndexOfUi >= 0)
                        this.PanList.Children.RemoveAt(IndexOfUi);
                }
                RefreshBars();
                if (!IsSuccessful)
                {
                    ModMain.Hint("由于文件被占用，Mod 删除失败，请尝试关闭正在运行的游戏后再试！", ModMain.HintType.Red);
                    ReloadModList(true);
                }
                else if (this.PanList.Children.Count == 0)
                {
                    ReloadModList(true); // 删除了全部文件
                }
                else
                {
                    RefreshBars();
                }
                // 显示结果提示
                if (!IsSuccessful)
                    return;
                if (ModList.IsSingle())
                {
                    ModMain.Hint($"已删除 {ModList.Single().File.Name}！", ModMain.HintType.Green);
                }
                else
                {
                    ModMain.Hint($"已删除 {ModList.Count} 个文件！", ModMain.HintType.Green);
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.Warn(ex, "删除 Mod 被主动取消");
                ReloadModList(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "删除 Mod 出现未知错误");
                ReloadModList(true);
            }
            LoaderRun(ModLoader.LoaderFolderRunType.UpdateOnly);
        }

        // 取消选择
        private void BtnSelectCancel_Click()
        {
            ChangeAllSelected(false);
        }

        #endregion

        #region 单个 Mod 项

        // 详情
        public void Info_Click(object sender, EventArgs e)
        {
            try
            {

                var ModEntry = ((MyLocalModItem)(sender is MyIconButton ? ((dynamic)sender).Tag : sender)).Entry;
                // 加载失败信息
                if (ModEntry.Project is not null)
                {
                    // 跳转到 Mod 下载页面
                    ModMain.FrmMain.PageChange(new FormMain.PageStackData()
                    {
                        Page = FormMain.PageType.ResourceDetail,
                        Additional = new object[]{ ModEntry.Project, new List<string>(), PageInstanceLeft.Instance.Version.VanillaName, PageInstanceLeft.Instance.Version.ModLoaders, ResourceTypes.Mod }
                    });
                }
                else
                {
                    // 获取信息
                    var ContentLines = new List<string>();
                    if (ModEntry.Description is not null)
                        ContentLines.Add(ModEntry.Description + Constants.vbCrLf);
                    ContentLines.Add("文件：" + ModEntry.File.Name + "（" + StringUtils.FormatByteSize(ModEntry.File.Length) + "）");
                    if (ModEntry.Version is not null)
                        ContentLines.Add("版本：" + ModEntry.Version);
                    var DebugInfo = new List<string>();
                    if (DebugInfo.Any())
                    {
                        ContentLines.Add("");
                        ContentLines.AddRange(DebugInfo);
                    }
                    // 获取用于搜索的 Mod 名称
                    string ModOriginalName = ModEntry.Display.Replace(" ", "+");
                    string ModSearchName = ModOriginalName.Substring(0, 1);
                    for (int i = 1, loopTo = ModOriginalName.Count() - 1; i <= loopTo; i++)
                    {
                        bool IsLastLower = ModOriginalName[i - 1].ToString().Lower().Equals(ModOriginalName[i - 1].ToString());
                        bool IsCurrentLower = ModOriginalName[i].ToString().Lower().Equals(ModOriginalName[i].ToString());
                        if (IsLastLower && !IsCurrentLower)
                        {
                            // 上一个字母为小写，这一个字母为大写
                            ModSearchName += "+";
                        }
                        ModSearchName += Conversions.ToString(ModOriginalName[i]);
                    }
                    ModSearchName = ModSearchName.Replace("++", "+").Replace("pti+Fine", "ptiFine");
                    // 百科搜索链接
                    if (ModMain.MyMsgBox(ContentLines.Join(Constants.vbCrLf), ModEntry.Display, "百科搜索", "返回") == 1)
                    {
                        ModBase.OpenWebsite("https://www.mcmod.cn/s?key=" + ModSearchName + "&site=all&filter=0");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "获取 Mod 详情失败");
            }
        }
        // 打开文件所在的位置
        public void Open_Click(MyIconButton sender, EventArgs e)
        {
            try
            {
                MyLocalModItem ListItem = (MyLocalModItem)sender.Tag;
                ModBase.OpenExplorer(ListItem.Entry.File.FullName);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "打开 Mod 文件位置失败");
            }
        }
        // 删除
        public void Delete_Click(MyIconButton sender, EventArgs e)
        {
            MyLocalModItem ListItem = (MyLocalModItem)sender.Tag;
            DeleteMods(new[] { ListItem.Entry });
        }
        // 启用 / 禁用
        public void ED_Click(MyIconButton sender, EventArgs e)
        {
            MyLocalModItem ListItem = (MyLocalModItem)sender.Tag;
            EDMods(new[] { ListItem.Entry }, !ListItem.Entry.IsEnabled);
        }

        #endregion

        #region 搜索

        public bool IsSearching
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.SearchBox.Text);
            }
        }
        private List<LocalResourceFile> SearchResult;
        public void SearchRun()
        {
            if (IsSearching)
            {
                // 构造请求
                var QueryList = new List<ModBase.SearchEntry<LocalResourceFile>>();
                foreach (LocalResourceFile Entry in LocalResourceLoaders.LocalResourceLoader.Output)
                {
                    var SearchSources = new List<ModBase.SearchSource>();
                    SearchSources.Add(new ModBase.SearchSource(Entry.Display, 1d));
                    SearchSources.Add(new ModBase.SearchSource(Entry.File.Name, 1d));
                    if (Entry.Version is not null)
                    {
                        SearchSources.Add(new ModBase.SearchSource(Entry.Version, 0.2d));
                    }
                    if (Entry.Description is not null && !string.IsNullOrEmpty(Entry.Description))
                    {
                        SearchSources.Add(new ModBase.SearchSource(Entry.Description, 0.4d));
                    }
                    if (Entry.Project is not null)
                    {
                        if ((Entry.Project.RawName ?? "") != (Entry.Display ?? ""))
                            SearchSources.Add(new ModBase.SearchSource(Entry.Project.RawName, 1d));
                        if ((Entry.Project.TranslatedName ?? "") != (Entry.Project.RawName ?? ""))
                            SearchSources.Add(new ModBase.SearchSource(Entry.Project.TranslatedName, 1d));
                        if ((Entry.Project.Description ?? "") != (Entry.Description ?? ""))
                            SearchSources.Add(new ModBase.SearchSource(Entry.Project.Description, 0.4d));
                        SearchSources.Add(new ModBase.SearchSource(string.Join("", Entry.Project.Tags), 0.2d));
                    }
                    QueryList.Add(new ModBase.SearchEntry<LocalResourceFile>() { Item = Entry, SearchSource = SearchSources });
                }
                // 进行搜索
                SearchResult = ModBase.Search(QueryList, SearchBox.Text, MaxBlurCount: 6, MinBlurSimilarity: 0.35d).Select(r => r.Item).ToList();
            }
            RefreshUI();
        }

        #endregion

    }
}