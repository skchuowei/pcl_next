using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    public partial class PageSelectRight
    {
        public PageSelectRight()
        {
            InitializeComponent();
            this.Load.Click += (p1, p2) => Load_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnEmptyDownload.Click += (p1, p2) => BtnDownload_Click((object)p1, (EventArgs)p2);
            this.BtnEmptyDownload.Loaded += (p1, p2) => BtnEmptyDownload_Loaded();
            this.Loaded += PageSelectRight_Loaded;
            // 注意：WPF 的 Initialized 在 InitializeComponent() 内部的 EndInit 阶段就已触发，
            // 所以「先 InitializeComponent 再 += Initialized」的写法永远收不到事件。VB 原版用
            // Handles Me.Initialized，是编译期在构造函数最前面挂上的，才会命中。
            // 这里改成显式调用：等价于 XAML 解析完成后的那一刻。
            // 曾经漏掉它 → PageLoaderInit 从未执行 → PanLoad 与 PanEmpty 同时可见（页面重叠）、
            // 「正在获取版本列表」卡片永不消失、版本列表一直是空白（2026-11-11 修复）。
            LoaderInit();
        }

        // 窗口基础
        private void PageSelectRight_Loaded(object sender, RoutedEventArgs e)
        {
            ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.RunOnUpdated, MaxDepth: 1, ExtraPath: @"versions\");
            this.PanBack.ScrollToHome();
        }
        private void LoaderInit()
        {
            this.PageLoaderInit(this.Load, this.PanLoad, this.PanAllBack, (FrameworkElement)null, ModMinecraft.McInstanceListLoader, this.McInstanceListUI, AutoRun: false);
        }
        private void Load_Click(object sender, MouseButtonEventArgs e)
        {
            if (ModMinecraft.McInstanceListLoader.State == ModBase.LoadState.Failed)
            {
                ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
            }
        }

        // 窗口属性
        /// <summary>
    /// 是否显示隐藏的 Minecraft 版本。
    /// </summary>
        public bool ShowHidden = false;

        #region 结果 UI 化

        private void McInstanceListUI(ModLoader.LoaderTask<string, int> Loader)
        {
            try
            {
                // 加载 UI
                this.PanMain.Children.Clear();

                foreach (KeyValuePair<ModMinecraft.McInstanceCardType, List<ModMinecraft.McInstance>> Card in ModMinecraft.McInstanceList.ToArray())
                {
                    // 确认是否为隐藏版本显示状态
                    if (Card.Key == ModMinecraft.McInstanceCardType.Hidden ^ ShowHidden)
                        continue;
                    var Instances = Card.Value.Where(Instance => !ModMinecraft.McInstanceLock.IsLocked(Instance)).ToList();
                    if (!Instances.Any())
                        continue;
                    #region 确认卡片名称
                    string CardName = "";
                    switch (Card.Key)
                    {
                        case ModMinecraft.McInstanceCardType.OriginalLike:
                            {
                                CardName = "常规版本";
                                break;
                            }
                        case ModMinecraft.McInstanceCardType.API:
                            {
                                bool IsForgeExists = false;
                                bool IsNeoForgeExists = false;
                                bool IsFabricExists = false;
                                bool IsLiteExists = false;
                                foreach (ModMinecraft.McInstance Instance in Instances)
                                {
                                    if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
                                        IsFabricExists = true;
                                    if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader))
                                        IsLiteExists = true;
                                    if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge))
                                        IsForgeExists = true;
                                    if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
                                        IsNeoForgeExists = true;
                                }
                                if ((IsLiteExists ? 1 : 0) + (IsForgeExists ? 1 : 0) + (IsFabricExists ? 1 : 0) + (IsNeoForgeExists ? 1 : 0) > 1)
                                {
                                    CardName = "可安装 Mod";
                                }
                                else if (IsForgeExists)
                                {
                                    CardName = "Forge 版本";
                                }
                                else if (IsNeoForgeExists)
                                {
                                    CardName = "NeoForge 版本";
                                }
                                else if (IsLiteExists)
                                {
                                    CardName = "LiteLoader 版本";
                                }
                                else
                                {
                                    CardName = "Fabric 版本";
                                }

                                break;
                            }
                        case ModMinecraft.McInstanceCardType.Error:
                            {
                                CardName = "错误的版本";
                                break;
                            }
                        case ModMinecraft.McInstanceCardType.Hidden:
                            {
                                CardName = "隐藏的版本";
                                break;
                            }
                        case ModMinecraft.McInstanceCardType.Rubbish:
                            {
                                CardName = "不常用版本";
                                break;
                            }
                        case ModMinecraft.McInstanceCardType.Star:
                            {
                                CardName = "收藏夹";
                                break;
                            }
                        case ModMinecraft.McInstanceCardType.Fool:
                            {
                                CardName = "愚人节版本";
                                break;
                            }

                        default:
                            {
                                throw new ArgumentException("未知的卡片种类（" + ((int)Card.Key).ToString() + "）");
                            }
                    }
                    #endregion
                    // 建立控件
                    string CardTitle = CardName + (CardName == "收藏夹" ? "" : " (" + Instances.Count + ")");
                    var NewCard = new MyCard() { Title = CardTitle, Margin = new Thickness(0d, 0d, 0d, 15d) };
                    var NewStack = new StackPanel() { Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d), VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d) };
                    NewCard.Children.Add(NewStack);
                    NewCard.SwapControl = NewStack;
                    NewCard.SetContentBuilder(Instances, Instance => Instance.ToListItem(McInstanceListContent));
                    this.PanMain.Children.Add(NewCard);
                    // 确定卡片是否展开
                    if (Card.Key == ModMinecraft.McInstanceCardType.Rubbish || Card.Key == ModMinecraft.McInstanceCardType.Error || Card.Key == ModMinecraft.McInstanceCardType.Fool)
                    {
                        NewCard.IsSwapped = true;
                    }
                    else
                    {
                        NewCard.BuildContent();
                    }
                }

                // 若只有一个卡片，则强制展开
                if (this.PanMain.Children.Count == 1 && ((MyCard)this.PanMain.Children[0]).IsSwapped)
                {
                    ((MyCard)this.PanMain.Children[0]).IsSwapped = false;
                }

                // 判断应该显示哪一个页面
                if (this.PanMain.Children.Count == 0)
                {
                    this.PanEmpty.Visibility = Visibility.Visible;
                    this.PanBack.Visibility = Visibility.Collapsed;
                    if (ShowHidden)
                    {
                        this.LabEmptyTitle.Text = "无隐藏版本";
                        this.LabEmptyContent.Text = "没有版本被隐藏，你可以在版本设置的版本分类选项中隐藏版本。" + "\r\n" + "再次按下 F11 即可退出隐藏版本查看模式。";
                        this.BtnEmptyDownload.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        this.LabEmptyTitle.Text = "无可用版本";
                        this.LabEmptyContent.Text = "未找到任何版本的游戏，请先下载任意版本的游戏。" + "\r\n" + "若有已存在的游戏，请在左边的列表中选择添加文件夹，选择 .minecraft 文件夹将其导入。";
                        this.BtnEmptyDownload.Visibility = ModDownloadPolicy.RestrictMcDownload || Settings.Get<bool>("UiHiddenPageDownload") && !PageSetupUI.HiddenForceShow ? Visibility.Collapsed : Visibility.Visible;
                    }
                }
                else
                {
                    this.PanBack.Visibility = Visibility.Visible;
                    this.PanEmpty.Visibility = Visibility.Collapsed;
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "将版本列表转换显示时失败");
            }
        }
        public static void McInstanceListContent(MyListItem sender, EventArgs e)
        {
            ModMinecraft.McInstance Instance = (ModMinecraft.McInstance)sender.Tag;
            // 注册点击事件
            sender.Click += (_, __) => PageSelectRight.Item_Click((dynamic)_, (dynamic)__);
            // 图标按钮
            var BtnStar = new MyIconButton();
            if (Instance.IsStar)
            {
                BtnStar.ToolTip = "取消收藏";
                BtnStar.LogoScale = 1.1d;
                BtnStar.Logo = ModBase.Logo.IconButtonLikeFill;
            }
            else
            {
                BtnStar.ToolTip = "收藏";
                BtnStar.LogoScale = 1.1d;
                BtnStar.Logo = ModBase.Logo.IconButtonLikeLine;
            }
            BtnStar.Click += (_, __) =>
        {
            ModBase.WriteIni(Instance.PathVersion + @"PCL\Setup.ini", "IsStar", Conversions.ToString(!Instance.IsStar));
            ModMinecraft.McInstanceListForceRefresh = true;
            ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
        };
            var BtnDel = new MyIconButton() { LogoScale = 1.1d, Logo = ModBase.Logo.IconButtonDelete };
            BtnDel.ToolTip = "删除";
            BtnDel.Click += (_, __) => DeleteInstance(sender, Instance);
            if (Instance.State != ModMinecraft.McInstance.McInstanceState.Error)
            {
                var BtnCont = new MyIconButton() { LogoScale = 1.1d, Logo = ModBase.Logo.IconButtonSetup };
                BtnCont.ToolTip = "设置";
                BtnCont.Click += (_, __) =>
        {
            PageInstanceLeft.Instance = Instance;
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.InstanceSetup, 0);
        };
                sender.MouseRightButtonUp += (_, __) =>
        {
            global::PCL.PageInstanceLeft.Instance = Instance;
            global::PCL.ModMain.FrmMain.PageChange((global::PCL.FormMain.PageStackData)global::PCL.FormMain.PageType.InstanceSetup, 0);
        };
                sender.Buttons = new[] { BtnStar, BtnDel, BtnCont };
            }
            else
            {
                var BtnCont = new MyIconButton() { LogoScale = 1.15d, Logo = ModBase.Logo.IconButtonOpen };
                BtnCont.ToolTip = "打开文件夹";
                BtnCont.Click += (_, __) => PageInstanceOverall.OpenInstanceFolder(Instance);
                sender.MouseRightButtonUp += (_, __) => global::PCL.PageInstanceOverall.OpenInstanceFolder(Instance);
                sender.Buttons = new[] { BtnStar, BtnDel, BtnCont };
            }
        }

        #endregion

        #region 页面事件

        // 点击选项
        public static void Item_Click(MyListItem sender, EventArgs e)
        {
            ModMinecraft.McInstance Instance = (ModMinecraft.McInstance)sender.Tag;
            if (ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该版本正在被修改，请稍后再选择！", ModMain.HintType.Red);
                return;
            }
            if (new ModMinecraft.McInstance(Instance.PathVersion).Check())
            {
                // 正常版本
                ModMinecraft.McInstanceSelected = Instance;
                ModMain.FrmMain.PageBack();
            }
            else
            {
                // 错误版本
                PageInstanceOverall.OpenInstanceFolder(Instance);
            }
        }

        private void BtnDownload_Click(object sender, EventArgs e)
        {
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall);
        }

        // 修改此代码时，同时修改 PageInstanceOverall 中的代码
        public static void DeleteInstance(MyListItem Item, ModMinecraft.McInstance Instance)
        {
            try
            {
                if (ModMinecraft.McInstanceLock.IsLocked(Instance))
                {
                    ModMain.Hint("该版本正在被修改，请稍后再删除！", ModMain.HintType.Red);
                    return;
                }
                bool IsShiftPressed = My.Computer.Keyboard.ShiftKeyDown;
                bool IsHintIndie = Instance.State != ModMinecraft.McInstance.McInstanceState.Error && (Instance.PathIndie ?? "") != (ModMinecraft.McFolderSelected ?? "");
                var SavesFolder = DirectoryUtils.GetInfo(Instance.PathIndie + @"saves\");
                var SaveEntries = IsHintIndie && SavesFolder.Exists ? SavesFolder.EnumerateDirectories().OrderByDescending(Save => Save.LastWriteTime).Select(Save => $"{Save.Name}（上次修改：{StringUtils.FormatTimeSpan(Save.LastWriteTime - DateTime.Now, false)}）").ToList() : new List<string>();
                switch (ModMain.MyMsgBox($"你确定要{(IsShiftPressed ? "永久" : "")}删除版本 {Instance.Name} 吗？" + (IsHintIndie ? "\r\n" + "该版本对应的存档、资源包、Mod 等文件也将被一并删除！" : "") + (SaveEntries.Any() ? "\r\n" + "\r\n" + "这会删除以下存档：" + "\r\n" + "· " + SaveEntries.Join("\r\n" + "· ") : ""), "版本删除确认", Button2: "取消", IsWarn: true))
                {
                    case 1:
                        {
                            Instance.ResetSettingsCache();
                            DirectoryUtils.Delete(Instance.PathVersion, !IsShiftPressed);
                            ModMain.Hint("版本 " + Instance.Name + " 已删除！", ModMain.HintType.Green);
                            break;
                        }
                    case 2:
                        {
                            return;
                        }
                }
                // 从 UI 中移除
                if (Instance.DisplayType == ModMinecraft.McInstanceCardType.Hidden || !Instance.IsStar)
                {
                    // 仅出现在当前卡片
                    StackPanel Parent = (StackPanel)Item.Parent;
                    if (Parent.Children.Count > 2) // 当前的项目与一个占位符
                    {
                        // 删除后还有剩
                        MyCard Card = (MyCard)Parent.Parent;
                        Card.Title = Card.Title.Replace((Parent.Children.Count - 1).ToString(), (Parent.Children.Count - 2).ToString()); // 有一个占位符
                        Parent.Children.Remove(Item);
                        if (ModMinecraft.McInstanceSelected is not null && PathUtils.IsSame(Instance.PathVersion, ModMinecraft.McInstanceSelected.PathVersion))
                        {
                            // 删除当前版本就更改选择
                            ModMinecraft.McInstanceSelected = (ModMinecraft.McInstance)MyVirtualizingElement.TryInit((FrameworkElement)Parent.Children[0]).Tag;
                        }
                        ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.UpdateOnly, MaxDepth: 1, ExtraPath: @"versions\");
                    }
                    else
                    {
                        // 删除后没剩了
                        ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                    }
                }
                else
                {
                    // 同时出现在当前卡片与收藏夹
                    ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.Warn(ex, $"删除版本 {Instance.Name} 被主动取消");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"删除版本 {Instance.Name} 失败", LogBehavior.Alert);
            }
        }

        public void BtnEmptyDownload_Loaded()
        {
            var NewVisibility = Settings.Get<bool>("UiHiddenPageDownload") && !PageSetupUI.HiddenForceShow || ShowHidden ? Visibility.Collapsed : Visibility.Visible;
            if (this.BtnEmptyDownload.Visibility != NewVisibility)
            {
                this.BtnEmptyDownload.Visibility = NewVisibility;
                this.PanLoad.TriggerForceResize();
            }
        }

        #endregion

    }
}