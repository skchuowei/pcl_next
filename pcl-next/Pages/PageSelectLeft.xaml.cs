using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class PageSelectLeft : IRefreshable
    {
        public PageSelectLeft()
        {
            InitializeComponent();
            this.Loaded += PageSelectLeft_Loaded;
            // Initialized 在 InitializeComponent() 内部（EndInit）就已触发，事后订阅收不到，
            // 于是 McFolderListLoader.PreviewFinish 从未挂上 —— 点击左侧文件夹后列表不重建、
            // 勾选状态不刷新、右侧版本列表也跟着不动（2026-11-11 修复）。
            PageSelectLeft_Initialized(this, EventArgs.Empty);
        }

        private void PageSelectLeft_Initialized(object sender, EventArgs e)
        {
            ModMinecraft.McFolderListLoader.PreviewFinish += (_) => { if (ModMain.FrmSelectLeft is not null) ModBase.RunInUiWait(McFolderListUI); };
        }
        private bool IsFirstLoad = true;
        private void PageSelectLeft_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsFirstLoad)
                McFolderListUI(); // 若已经执行完成，触发首次加载
            IsFirstLoad = false;
        }
        private void McFolderListUI()
        {
            try
            {

                // 确认数据有变化
                if (McFolderListLast is not null && McFolderListLast.SequenceEqual(ModMinecraft.McFolderList))
                {
                    bool IsEqual = true;
                    for (int i = 0, loopTo = McFolderListLast.Count - 1; i <= loopTo; i++)
                    {
                        if (!McFolderListLast[i].Equals(ModMinecraft.McFolderList[i]))
                        {
                            IsEqual = false;
                            break;
                        }
                    }
                    if (IsEqual)
                        return;
                }
                McFolderListLast = ModMinecraft.McFolderList;

                // 创建 UI
                ModMain.FrmSelectLeft.PanList.Children.Clear();

                // 文件夹列表
                ModMain.FrmSelectLeft.PanList.Children.Add(new TextBlock() { Text = "文件夹列表", Margin = new Thickness(13d, 18d, 5d, 4d), Opacity = 0.6d, FontSize = 12d });
                foreach (ModMinecraft.McFolder Folder in ModMinecraft.McFolderList.ToArray())
                {
                    // 添加控件
                    // 菜单项改为显式构造：迁移时用 XElement 手拼 XAML，每个分支都漏掉了末尾的项
                    // （Vanilla / RenamedVanilla 漏了 Delete，Custom 漏了 Remove 和 Delete），
                    // 于是 ContMenu.FindName("Delete") 返回 null，AddHandler 抛 NullReferenceException，
                    // 被外层 catch 吞掉后整个文件夹列表一片空白（2026-11-11 修复）。
                    MyMenuItem ItemRemove = null, ItemRename, ItemOpen, ItemRefresh, ItemDelete;
                    ContextMenu ContMenu = new ContextMenu();
                    switch (Folder.Type)
                    {
                        case ModMinecraft.McFolder.Types.Vanilla:
                            {
                                ItemRename = NewFolderMenuItem("重命名", LogoRename, 2d, 0d);
                                ItemOpen = NewFolderMenuItem("打开", LogoOpen);
                                ItemRefresh = NewFolderMenuItem("刷新", LogoRefresh);
                                ItemDelete = NewFolderMenuItem("删除", LogoDelete, 0d, 2d);
                                ContMenu.Items.Add(ItemRename);
                                ContMenu.Items.Add(ItemOpen);
                                ContMenu.Items.Add(ItemRefresh);
                                ContMenu.Items.Add(ItemDelete);
                                break;
                            }
                        case ModMinecraft.McFolder.Types.RenamedVanilla:
                            {
                                ItemRemove = NewFolderMenuItem("复原名称", LogoRename, 2d, 0d);
                                ItemRename = NewFolderMenuItem("重命名", LogoRename);
                                ItemOpen = NewFolderMenuItem("打开", LogoOpen);
                                ItemRefresh = NewFolderMenuItem("刷新", LogoRefresh);
                                ItemDelete = NewFolderMenuItem("删除", LogoDelete, 0d, 2d);
                                ContMenu.Items.Add(ItemRemove);
                                ContMenu.Items.Add(ItemRename);
                                ContMenu.Items.Add(ItemOpen);
                                ContMenu.Items.Add(ItemRefresh);
                                ContMenu.Items.Add(ItemDelete);
                                break;
                            }
                        case ModMinecraft.McFolder.Types.Custom:
                            {
                                ItemRename = NewFolderMenuItem("重命名", LogoRename, 2d, 0d);
                                ItemOpen = NewFolderMenuItem("打开", LogoOpen);
                                ItemRefresh = NewFolderMenuItem("刷新", LogoRefresh);
                                ItemRemove = NewFolderMenuItem("移出列表", LogoRemove);
                                ItemDelete = NewFolderMenuItem("删除", LogoDelete, 0d, 2d);
                                ContMenu.Items.Add(ItemRename);
                                ContMenu.Items.Add(ItemOpen);
                                ContMenu.Items.Add(ItemRefresh);
                                ContMenu.Items.Add(ItemRemove);
                                ContMenu.Items.Add(ItemDelete);
                                break;
                            }
                        default:
                            {
                                ItemRename = NewFolderMenuItem("重命名", LogoRename, 2d, 0d);
                                ItemOpen = NewFolderMenuItem("打开", LogoOpen);
                                ItemRefresh = NewFolderMenuItem("刷新", LogoRefresh);
                                ItemDelete = NewFolderMenuItem("删除", LogoDelete, 0d, 2d);
                                ContMenu.Items.Add(ItemRename);
                                ContMenu.Items.Add(ItemOpen);
                                ContMenu.Items.Add(ItemRefresh);
                                ContMenu.Items.Add(ItemDelete);
                                break;
                            }
                    }
                    if ((Folder.Type == ModMinecraft.McFolder.Types.Vanilla || Folder.Type == ModMinecraft.McFolder.Types.RenamedVanilla) && PathUtils.IsSame(Folder.Location, Paths.Base + @".minecraft\") && ModMinecraft.McFolderList.IsSingle())
                        ItemDelete.Header = "清空";
                    // 注册事件
                    if (ItemRemove is not null)
                        ItemRemove.Click += Remove_Click;
                    ItemOpen.Click += Open_Click;
                    ItemDelete.Click += Delete_Click;
                    ItemRename.Click += Rename_Click;
                    ItemRefresh.Click += Refresh_Click;
                    // 构建框架与图表按钮
                    var NewItem = new MyListItem() { IsScaleAnimationEnabled = false, Type = MyListItem.CheckType.RadioBox, MinPaddingRight = 30, Title = Folder.Name, Info = Folder.Location, Height = 40d, ContextMenu = ContMenu, Tag = Folder };
                    NewItem.Changed += (_, __) => ModMain.FrmSelectLeft.Folder_Change((dynamic)_, (dynamic)__);
                    var NewIconButton = new MyIconButton() { Logo = ModBase.Logo.IconButtonSetup, LogoScale = 1.1d };
                    NewIconButton.Click += (sender, e) =>
                        {
                            ContMenu.PlacementTarget = NewItem;
                            ContMenu.IsOpen = true;
                        };
                    NewItem.Buttons = new[] { NewIconButton };
                    ModMain.FrmSelectLeft.PanList.Children.Add(NewItem);
                    Logger.Info($"有效的 Minecraft 文件夹：{Folder.Name} > {Folder.Location}");
                }

                // 标题文本
                ModMain.FrmSelectLeft.PanList.Children.Add(new TextBlock() { Text = "添加或导入", Margin = new Thickness(13d, 18d, 5d, 4d), Opacity = 0.6d, FontSize = 12d });

                // 确认创建按钮状态
                if (!DirectoryUtils.Exists(Paths.Base + @".minecraft\"))
                {
                    var ItemCreate = new MyListItem()
                    {
                        IsScaleAnimationEnabled = false,
                        Type = MyListItem.CheckType.Clickable,
                        Title = "新建 .minecraft 文件夹",
                        Height = 34d,
                        ToolTip = "在 PCL Next 当前所在文件夹下创建新的 .minecraft 文件夹",
                        LogoScale = 0.9d,
                        Logo = "M103.331925 384.978025l25.805736 0L129.137661 161.847132c0-18.313088 14.905478-33.718963 33.718963-33.718963l0.969071 0 253.006318 0c10.82044 0 20.218484 4.797259 26.500561 12.257162l117.579929 126.753869 297.819966 0c18.297738 0 33.736359 15.179724 33.736359 33.977859l0 0.952698 0 82.909292 25.547863 0c18.538215 0 34.187637 15.179724 34.187637 33.977859 0 2.163269-0.469698 3.617387-0.469698 5.539156l-54.437843 432.971086c-1.210571 10.382465-7.007601 19.056008-14.968923 24.352641-6.249331 5.765307-14.680351 9.624195-23.595394 9.624195l-0.969071 0-694.906773 0c-9.155521 0-17.344017-3.858888-23.626094-9.155521-8.67252-5.765307-14.453177-14.939247-15.389502-25.758664L69.597613 423.040922c-2.165316-18.313088 10.868535-35.414581 29.665647-38.062897L103.331925 384.978025 103.331925 384.978025zM196.576609 384.978025 196.576609 384.978025l627.938546 0 0-49.625234L546.461371 335.352791l0 0c-9.400091 0-18.329461-4.117784-25.048489-11.110035L402.363486 196.067514 196.576609 196.067514 196.576609 384.978025 196.576609 384.978025zM879.469767 452.916347 879.469767 452.916347l-20.267603 0-0.469698 0-0.969071 0-694.906773 0-0.984421 0-20.218484 0 45.781696 366.728382 646.218888 0L879.469767 452.916347 879.469767 452.916347z"
                    };
                    ModMain.FrmSelectLeft.PanList.Children.Add(ItemCreate);
                    ItemCreate.Click += (_, __) => ModMain.FrmSelectLeft.Create_Click();
                }

                // 添加按钮
                var ItemAdd = new MyListItem()
                {
                    IsScaleAnimationEnabled = false,
                    Type = MyListItem.CheckType.Clickable,
                    Title = "添加已有文件夹",
                    Height = 34d,
                    ToolTip = "将一个已有的 Minecraft 文件夹添加到列表",
                    Logo = "M512.277 954.412c-118.89 0-230.659-46.078-314.73-129.73S67.12 629.666 67.12 511.222s46.327-229.744 130.398-313.427 195.82-129.73 314.73-129.73 230.659 46.078 314.72 129.73S957.397 392.81 957.397 511.183 911.078 740.96 826.97 824.642s-195.8 129.77-314.692 129.77z m0-822.784c-101.972 0-197.809 39.494-269.865 111.222s-111.7 166.997-111.7 268.373 39.653 196.695 111.67 268.335S410.246 890.78 512.248 890.78s197.809-39.484 269.865-111.222 111.7-166.998 111.67-268.374c-0.03-101.375-39.654-196.665-111.67-268.303S614.22 131.628 512.277 131.628z m222.585 347.8H544.073V288.64c-0.76-17.561-15.613-31.18-33.173-30.419-16.495 0.714-29.704 13.924-30.419 30.419v190.787H289.703c-17.56 0.761-31.179 15.614-30.419 33.174 0.715 16.494 13.924 29.703 30.42 30.418H480.48v190.788c0.761 17.56 15.614 31.179 33.174 30.419 16.494-0.715 29.703-13.925 30.418-30.42V543.02h190.788c17.56 0.762 32.413-12.857 33.173-30.418 0.762-17.561-12.858-32.414-30.419-33.174a31.683 31.683 0 0 0-2.753 0z"
                };
                ModMain.FrmSelectLeft.PanList.Children.Add(ItemAdd);
                ItemAdd.Click += (_, __) => ModMain.FrmSelectLeft.Add_Click();

                // 安装按钮
                var ItemInstall = new MyListItem()
                {
                    IsScaleAnimationEnabled = false,
                    Type = MyListItem.CheckType.Clickable,
                    Title = "导入整合包",
                    Height = 34d,
                    ToolTip = "在当前选择的 Minecraft 文件夹下安装整合包",
                    Logo = "M512 40.96C249.344 40.96 35.84 252.416 35.84 512s213.504 471.04 476.16 471.04c103.424 0 202.752-33.28 286.72-96.256l1.536-1.536c5.12-5.632 7.68-12.8 7.68-19.968 0-16.896-13.824-30.208-30.72-30.208-7.68 0-15.36 2.56-20.992 7.68h-0.512c-71.68 52.224-155.648 79.36-243.712 79.36-227.328 0-412.16-182.784-412.16-407.552 0-224.768 184.832-407.552 412.16-407.552s412.16 182.784 412.16 407.552c0 68.608-15.872 132.608-46.592 190.464-0.512 1.024-1.024 2.048-1.024 3.072-0.512 2.048-1.536 4.608-1.536 8.192 0 16.896 13.824 30.208 30.72 30.208 12.288 0 23.04-7.168 28.16-18.432 35.84-68.608 53.76-141.312 53.76-216.064 0.512-259.584-212.992-471.04-475.648-471.04z M812.032 483.328c-31.744-20.992-71.68 1.536-78.848 6.144-1.024 0.512-104.448 61.44-128 74.752-8.192 4.608-22.528-0.512-27.136-4.096-31.232-36.352-54.272-70.656-68.608-102.4-13.312-29.184 0.512-41.472 3.072-43.52 7.168-4.608 114.688-68.608 143.36-83.456 24.064-12.288 40.96-25.088 46.08-45.056 3.072-13.312 0-27.136-9.216-39.936-22.016-31.744-172.544-84.992-311.296-3.584-157.184 91.648-152.064 242.688-150.528 292.352v9.216c0 18.944-12.8 37.376-14.848 40.448l-20.992 21.504c-6.144 6.144-9.216 13.824-9.216 22.528 0 8.704 3.584 16.384 9.728 22.528 12.8 12.288 32.768 11.776 45.056-0.512l22.528-23.552 0.512-0.512c3.072-3.584 30.208-38.4 30.208-81.92l-0.512-11.264c-1.536-44.544-5.632-162.816 119.296-235.52 88.064-51.2 173.056-32.256 208.896-19.968-36.864 19.456-143.36 83.456-144.896 84.48-22.016 14.336-55.808 58.88-26.112 122.88 17.408 37.376 43.52 76.8 80.896 120.32 14.336 17.408 62.976 37.376 103.424 15.36 24.576-13.312 125.44-73.216 130.048-75.776 2.048-1.024 4.608-2.56 7.68-3.584 0 2.56-0.512 6.144-1.024 10.752-5.632 35.84-35.328 155.136-191.488 181.76-49.664 8.704-89.6 3.584-121.856-0.512h-0.512c-37.888-4.608-73.216-9.216-101.888 14.336-31.232 26.112-40.96 34.304-35.84 54.272 3.584 14.336 16.384 24.064 30.72 24.064 2.56 0 5.12-0.512 7.68-1.024 6.656-1.536 12.8-5.632 16.896-10.752 2.048-2.048 7.68-6.656 20.992-18.432 6.656-5.632 25.088-3.584 52.736 0 34.816 4.608 81.92 10.24 141.312 0.512 157.184-26.624 228.864-138.752 243.2-234.496 7.68-38.912 0-64.512-21.504-78.336z"
                };
                if (!ModDownloadPolicy.RestrictMcDownload)
                    ModMain.FrmSelectLeft.PanList.Children.Add(ItemInstall);
                ItemInstall.Click += (_, __) => ModModpack.ModpackInstall();

                // 边距
                ModMain.FrmSelectLeft.PanList.Children.Add(new FrameworkElement() { Height = 10d, IsHitTestVisible = false });

                // 确认勾选状态
                for (int i = 0, loopTo1 = ModMinecraft.McFolderList.Count - 1; i <= loopTo1; i++)
                {
                    if (PathUtils.IsSame(ModMinecraft.McFolderList[i].Location, ModMinecraft.McFolderSelected))
                    {
                        ((MyListItem)ModMain.FrmSelectLeft.PanList.Children[i + 1]).Checked = true; // 去掉第一个标题
                        return;
                    }
                }
                if (!ModMinecraft.McFolderList.Any())
                {
                    throw new ArgumentNullException("没有可用的 Minecraft 文件夹");
                }
                else
                {
                    ModMinecraft.McFolderSelected = ModMinecraft.McFolderList.First().Location;
                    ((MyListItem)ModMain.FrmSelectLeft.PanList.Children[1]).Checked = true;
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "构建 Minecraft 文件夹列表 UI 出错");
            }
            finally
            {
                ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.RunOnUpdated, MaxDepth: 1, ExtraPath: @"versions\"); // 刷新版本列表
            }
        }
        // ── 文件夹右键菜单 ────────────────────────────────────────────────────────
        // 图标字符串取自 VB 原版 PageSelectLeft.xaml.vb 的 XAML 菜单定义，避免手抄出错。
        private const string LogoRename = "F1 M 53.2929,21.2929L 54.7071,22.7071C 56.4645,24.4645 56.4645,27.3137 54.7071,29.0711L 52.2323,31.5459L 44.4541,23.7677L 46.9289,21.2929C 48.6863,19.5355 51.5355,19.5355 53.2929,21.2929 Z M 31.7262,52.052L 23.948,44.2738L 43.0399,25.182L 50.818,32.9601L 31.7262,52.052 Z M 23.2409,47.1023L 28.8977,52.7591L 21.0463,54.9537L 23.2409,47.1023 Z";
        private const string LogoOpen = "F1 M 19,50L 28,34L 63,34L 54,50L 19,50 Z M 19,28.0001L 35,28C 36,25 37.4999,24.0001 37.4999,24.0001L 48.75,24C 49.3023,24 50,24.6977 50,25.25L 50,28L 54,28.0001L 54,32L 27,32L 19,46.4L 19,28.0001 Z";
        private const string LogoRefresh = "F1 M 38,20.5833C 42.9908,20.5833 47.4912,22.6825 50.6667,26.046L 50.6667,17.4167L 55.4166,22.1667L 55.4167,34.8333L 42.75,34.8333L 38,30.0833L 46.8512,30.0833C 44.6768,27.6539 41.517,26.125 38,26.125C 31.9785,26.125 27.0037,30.6068 26.2296,36.4167L 20.6543,36.4167C 21.4543,27.5397 28.9148,20.5833 38,20.5833 Z M 38,49.875C 44.0215,49.875 48.9963,45.3932 49.7703,39.5833L 55.3457,39.5833C 54.5457,48.4603 47.0852,55.4167 38,55.4167C 33.0092,55.4167 28.5088,53.3175 25.3333,49.954L 25.3333,58.5833L 20.5833,53.8333L 20.5833,41.1667L 33.25,41.1667L 38,45.9167L 29.1487,45.9167C 31.3231,48.3461 34.483,49.875 38,49.875 Z";
        private const string LogoDelete = "F1 M 26.9166,22.1667L 37.9999,33.25L 49.0832,22.1668L 53.8332,26.9168L 42.7499,38L 53.8332,49.0834L 49.0833,53.8334L 37.9999,42.75L 26.9166,53.8334L 22.1666,49.0833L 33.25,38L 22.1667,26.9167L 26.9166,22.1667 Z ";
        private const string LogoRemove = "F1 M 23.3428,25.205L 23.3805,25.4461C 23.9229,27.177 30.261,29.0992 38,29.0992C 45.7386,29.0992 52.0765,27.1771 52.6194,25.4463L 52.6571,25.205C 52.6571,23.3616 46.0949,21.3109 38,21.3109C 29.9051,21.3109 23.3428,23.3616 23.3428,25.205 Z M 23.3428,53.0204L 19.1571,26.2111C 19.0534,25.8817 19,25.5459 19,25.205C 19,20.9036 27.5066,17.4167 38,17.4167C 48.4934,17.4167 57,20.9036 57,25.205C 57,25.5459 56.9466,25.8818 56.8429,26.2112L 52.6571,53.0204L 52.5974,53.0204C 51.9241,56.1393 45.6457,58.5833 38,58.5833C 30.3543,58.5833 24.076,56.1393 23.4026,53.0204L 23.3428,53.0204 Z M 51.8228,30.5485C 48.3585,32.0537 43.4469,32.9933 38,32.9933C 32.5531,32.9933 27.6415,32.0537 24.1771,30.5484L 27.5988,52.464L 27.6857,52.464C 27.6857,53.3857 32.3036,54.6892 38,54.6892C 43.6964,54.6892 48.3143,53.3857 48.3143,52.464L 48.4011,52.464L 51.8228,30.5485 Z ";

        private static MyMenuItem NewFolderMenuItem(string Header, string Logo, double PaddingTop = 0d, double PaddingBottom = 0d)
        {
            return new MyMenuItem { Header = Header, Icon = Logo, Padding = new Thickness(0d, PaddingTop, 0d, PaddingBottom) };
        }

        private List<ModMinecraft.McFolder> McFolderListLast;

        // 添加文件夹
        public void Add_Click()
        {
            string NewFolder = "";
            // 下载或版本修改期间禁止更改游戏文件夹
            if (ModMinecraft.McInstanceLock.IsAnyLocked || ModNet.HasDownloadingTask())
            {
                ModMain.Hint("请在其他任务完成后，再添加游戏文件夹！", ModMain.HintType.Red);
                return;
            }
            try
            {
                // 获取输入
                NewFolder = Dialogs.SelectFolder("选择 Minecraft 文件夹", false).FirstOrDefault();
                if (NewFolder is null)
                    return;
                if (NewFolder.Contains("!") || NewFolder.Contains(";"))
                {
                    ModMain.Hint("Minecraft 文件夹路径中不能含有感叹号或分号！", ModMain.HintType.Red);
                    return;
                }
                // 要求输入显示名称
                string DefaultName = PathUtils.GetLastPart(NewFolder);
                if (DefaultName == ".minecraft")
                {
                    string ParentFolder = PathUtils.RemoveLastPart(NewFolder);
                    DefaultName = ParentFolder.EndsWithF(":") ? "" : PathUtils.GetLastPart(ParentFolder);
                }
                if (DefaultName.Length > 40)
                    DefaultName = DefaultName.Substring(0, 39);
                string NewName = ModMain.MyMsgBoxInput("输入显示名称", "输入该文件夹在左边栏列表中显示的名称。", DefaultName, new System.Collections.ObjectModel.Collection<ValidateType>() { new ValidateNullOrWhiteSpace(), new ValidateLength(1, 30), new ValidateExcept(new[] { ">", "|" }) });
                if (string.IsNullOrWhiteSpace(NewName))
                    return;
                // 添加文件夹
                AddFolder(NewFolder, NewName, true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"添加文件夹失败（{NewFolder}）");
            }
        }
        /// <summary>
    /// 将指定文件夹添加到 Minecraft 文件夹列表，并选中它。
    /// </summary>
        public static void AddFolder(string FolderPath, string DisplayName, bool ShowHint)
        {
            // 检查文件夹权限
            // 检查实际的 Minecraft 文件夹位置（没有问题，或是在子文件夹中）
            // 判断是否已经添加过，若添加过则直接修改自定义名
            // 如果没有添加过，则添加进去
            // 保存
            // 切换选择并更新列表
            // 提示
            // 检查是否为根目录整合包，自动关闭版本隔离
            // 1. 根目录中存在数个 Mod
            // 2. 版本数较少，可能为整合包
            // 3. 能够找到可安装 Mod 的版本
            // 4. 该版本的隔离文件夹下不存在 mods
            // 满足以上全部条件则视为根目录整合包
            ModBase.RunInThread(() => { try { FolderPath = PathUtils.AddSlashSuffix(FolderPath); if (!ModBase.CheckPermission(FolderPath)) { if (ShowHint) { ModMain.Hint("添加文件夹失败：PCL Next 没有访问该文件夹的权限！", ModMain.HintType.Red); return; } else { throw new Exception("PCL Next 没有访问文件夹的权限：" + FolderPath); } } if (!ModBase.CheckPermission(FolderPath + @"versions\")) { foreach (var Folder in DirectoryUtils.EnumerateDirectories(FolderPath, true)) { if (ModBase.CheckPermission(Folder + @"\versions\")) { FolderPath = Folder + @"\"; break; } } } var Folders = new List<string>(Settings.Get<string>("LaunchFolders").Split("|")); bool IsAdded = false; bool IsReplace = false; for (int i = 0, loopTo = Folders.Count - 1; i <= loopTo; i++) { string Folder = Folders[i]; if (string.IsNullOrEmpty(Folder)) continue; var folderParts = Folder.Split(">"); if (folderParts.Length < 2) continue; if (PathUtils.IsSame(folderParts[1], FolderPath)) { IsAdded = true; if ((folderParts[0] ?? "") == (DisplayName ?? "")) { if (ShowHint) ModMain.Hint("此文件夹已在列表中！", ModMain.HintType.Blue); return; } else { Folders[i] = DisplayName + ">" + FolderPath; IsReplace = true; if (ShowHint) ModMain.Hint("文件夹名称已更新为 " + DisplayName + " ！", ModMain.HintType.Green); } break; } } if (!IsAdded) Folders.Add(DisplayName + ">" + FolderPath); Settings.Set("LaunchFolders", Folders.Join("|")); ModMinecraft.McFolderSelected = FolderPath; ModMinecraft.McFolderListLoader.Start(IsForceRestart: true); if (IsReplace) return; if (ShowHint) ModMain.Hint("文件夹 " + DisplayName + " 已添加！", ModMain.HintType.Green); var ModFolder = DirectoryUtils.GetInfo(FolderPath + @"mods\"); if (!(ModFolder.Exists && ModFolder.EnumerateFiles().Count() >= 3)) return; var VersionFolderInfo = DirectoryUtils.GetInfo(FolderPath + @"versions\"); if (!(VersionFolderInfo.Exists && VersionFolderInfo.EnumerateDirectories().Count() <= 3)) return; foreach (var VersionFolder in VersionFolderInfo.EnumerateDirectories()) { var Instance = new ModMinecraft.McInstance(VersionFolder.FullName); Instance.Load(); if (!Instance.Modable) continue; var ModIndieFolder = DirectoryUtils.GetInfo(Instance.PathVersion + @"mods\"); if (ModIndieFolder.Exists && ModIndieFolder.EnumerateFiles().Any()) return; Settings.Set("VersionArgumentIndie", 2, Instance: Instance); Settings.Set("VersionArgumentIndieV2", false, Instance: Instance); Logger.Warn($"已自动关闭单版本隔离：{Instance.Name}"); } } catch (Exception ex) { Logger.Error(ex, "向文件夹列表中添加新文件夹失败"); } });
        }

        // 创建文件夹
        public void Create_Click()
        {
            // 下载或版本修改期间禁止更改游戏文件夹
            if (ModMinecraft.McInstanceLock.IsAnyLocked || ModNet.HasDownloadingTask())
            {
                ModMain.Hint("请在其他任务完成后，再创建游戏文件夹！", ModMain.HintType.Red);
                return;
            }
            CreateMcFolderInCurrentPath();
            ModMain.Hint("新建 .minecraft 文件夹成功！", ModMain.HintType.Green);
        }
        public static void CreateMcFolderInCurrentPath()
        {
            ModMinecraft.McFolderSelected = Paths.Base + @".minecraft\";
            if (!DirectoryUtils.Exists(ModMinecraft.McFolderSelected))
            {
                DirectoryUtils.Create(ModMinecraft.McFolderSelected + @"versions\");
                ModMinecraft.McFolderLauncherProfilesJsonCreate(ModMinecraft.McFolderSelected);
            }
            AddFolder(ModMinecraft.McFolderSelected, PathUtils.GetLastPart(Paths.Base), false);
        }

        // 右键菜单
        public void Remove_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                ModMinecraft.McFolder Target = (ModMinecraft.McFolder)((MyListItem)((System.Windows.Controls.Primitives.Popup)((ContextMenu)((dynamic)sender).Parent).Parent).PlacementTarget).Tag;
                // 若为 “移除”，则提醒是否删除 PCL 的配置文件
                if (Target.Type == ModMinecraft.McFolder.Types.Custom)
                {
                    if (ModMinecraft.McInstanceLock.IsAnyLocked || ModNet.HasDownloadingTask())
                    {
                        ModMain.Hint("请在其他任务完成后，再移除游戏文件夹！", ModMain.HintType.Red);
                        return;
                    }
                    switch (ModMain.MyMsgBox("是否需要清理 PCL Next 在该文件夹中的配置文件？" + "\r\n" + "这包括各个版本的独立设置（如自定义图标、第三方登录配置）等，对游戏本身没有影响。", "配置文件清理", "删除", "保留", "取消"))
                    {
                        case 1:
                            {
                                // 删除配置文件
                                FileUtils.Delete(Target.Location + "PCL.ini");
                                foreach (var Instance in DirectoryUtils.EnumerateDirectories(Target.Location + @"versions\"))
                                    DirectoryUtils.Delete(Path.Combine(Instance, "PCL"), true);
                                break;
                            }
                        case 2:
                            {
                                break;
                            }
                        // 不删除
                        case 3:
                            {
                                // 取消
                                return;
                            }
                    }
                }
                string DeletedName = RemoveFolderFromSetup(Target);
                ModMain.Hint(Target.Type == ModMinecraft.McFolder.Types.Custom ? "文件夹 " + DeletedName + " 已从列表中移除！" : "文件夹名称已复原！", ModMain.HintType.Green);
                ModMinecraft.McFolderListLoader.Start(IsForceRestart: true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "从列表中移除游戏文件夹失败");
            }
        }
        public void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (ModMinecraft.McInstanceLock.IsAnyLocked || ModNet.HasDownloadingTask())
            {
                ModMain.Hint("请在其他任务完成后，再删除游戏文件夹！", ModMain.HintType.Red);
                return;
            }
            ModMinecraft.McFolder Target = (ModMinecraft.McFolder)((MyListItem)((System.Windows.Controls.Primitives.Popup)((ContextMenu)((dynamic)sender).Parent).Parent).PlacementTarget).Tag;
            string DeleteText = (Target.Type == ModMinecraft.McFolder.Types.Vanilla || Target.Type == ModMinecraft.McFolder.Types.RenamedVanilla) && (Target.Location ?? "") == (Paths.Base + @".minecraft\" ?? "") && ModMinecraft.McFolderList.IsSingle() ? "清空" : "删除";
            if (ModMain.MyMsgBox("你确定要" + DeleteText + "这个文件夹吗？" + "\r\n" + "目标文件夹：" + Target.Location + "\r\n" + "\r\n" + "该文件夹中的游戏存档、游戏版本，以及 MC 之外的其他文件，都会永久丢失，不可恢复！", "警告", "取消", "确认", "取消") != 2)
                return;
            if (ModMain.MyMsgBoxInput("删除确认", "该文件夹中的游戏存档、游戏版本，以及 MC 之外的其他文件，都会永久丢失，不可恢复！" + "\r\n" + "目标文件夹：" + Target.Location + "\r\n" + "\r\n" + "如果确实要删除，请在下面输入【Potato】这个单词以继续。", ValidateRules: new System.Collections.ObjectModel.Collection<ValidateType>() { new ValidateSame("Potato", $"请输入 {ModBase.vbLQ}Potato{ModBase.vbRQ}", IgnoreCase: true) }, Button1: "永久删除文件夹", Button2: "取消", IsWarn: true) is null)
                return;
            RemoveFolderFromSetup(Target);
            // 删除文件夹
            // 刷新列表
            ModBase.RunInNewThread(() => { try { ModMain.Hint("正在" + DeleteText + "文件夹 " + Target.Name + "！", ModMain.HintType.Blue); DirectoryUtils.Delete(Target.Location); if (DeleteText == "清空") DirectoryUtils.Create(Target.Location); ModMain.Hint("已" + DeleteText + "文件夹 " + Target.Name + "！", ModMain.HintType.Green); } catch (Exception ex) { Logger.Error(ex, $"{DeleteText}文件夹 {Target.Name} 失败", LogBehavior.Toast); } finally { ModMinecraft.McFolderListLoader.Start(IsForceRestart: true); } }, "Folder Delete " + ModBase.GetUuid(), ThreadPriority.BelowNormal);
        }
        /// <summary>
    /// 从设置的文件夹列表中删除指定的文件夹。
    /// 返回被删除的文件夹的显示名称。如果没有，返回 Nothing。
    /// </summary>
        private static string RemoveFolderFromSetup(ModMinecraft.McFolder Target)
        {
            string DeletedName = null;
            var Folders = Settings.Get<string>("LaunchFolders").Split('|', true).ToList();
            foreach (var Folder in Folders.ToList())
            {
                if (!Folder.Contains(">") || !PathUtils.IsSame(Folder.AfterFirst(">"), Target.Location))
                    continue;
                DeletedName = Folder.BeforeFirst(">");
                Folders.Remove(Folder);
                break;
            }
            Settings.Set("LaunchFolders", Folders.Join("|"));
            return DeletedName;
        }

        public void Open_Click(object sender, RoutedEventArgs e)
        {
            ModBase.OpenExplorer(((MyListItem)((System.Windows.Controls.Primitives.Popup)((ContextMenu)((dynamic)sender).Parent).Parent).PlacementTarget).Info);
        }
        public void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ModMinecraft.McFolder Data = (ModMinecraft.McFolder)((MyListItem)((System.Windows.Controls.Primitives.Popup)((ContextMenu)((dynamic)sender).Parent).Parent).PlacementTarget).Tag;
            RefreshCurrent(Data.Location);
        }
        public void RefreshCurrent()
        {
            RefreshCurrent(ModMinecraft.McFolderSelected);
        }

        void IRefreshable.Refresh() => RefreshCurrent();
        public static void RefreshCurrent(string Folder)
        {
            ModBase.WriteIni(Folder + "PCL.ini", "InstanceCache", ""); // 删除缓存以强制要求下一次加载时更新列表
            if (PathUtils.IsSame(Folder, ModMinecraft.McFolderSelected))
                ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
        }
        public void Rename_Click(object sender, RoutedEventArgs e)
        {
            ModMinecraft.McFolder Folder = (ModMinecraft.McFolder)((MyListItem)((System.Windows.Controls.Primitives.Popup)((ContextMenu)((dynamic)sender).Parent).Parent).PlacementTarget).Tag;
            try
            {
                // 获取输入
                string NewName = ModMain.MyMsgBoxInput("输入新名称", "", Folder.Name, new System.Collections.ObjectModel.Collection<ValidateType>() { new ValidateNullOrWhiteSpace(), new ValidateLength(1, 30), new ValidateExcept(new[] { ">", "|" }) });
                if (string.IsNullOrWhiteSpace(NewName))
                    return;
                // 修改自定义名
                var Folders = new List<string>(Settings.Get<string>("LaunchFolders").Split("|"));
                bool IsAdded = false;
                for (int i = 0, loopTo = Folders.Count - 1; i <= loopTo; i++)
                {
                    string FolderCurrent = Folders[i];
                    if (string.IsNullOrEmpty(FolderCurrent))
                        continue;
                    var folderCurrentParts = FolderCurrent.Split(">");
                    if (folderCurrentParts.Length < 2)
                        continue;
                    if (PathUtils.IsSame(folderCurrentParts[1], Folder.Location))
                    {
                        IsAdded = true;
                        if ((folderCurrentParts[0] ?? "") == (NewName ?? ""))
                        {
                            // 名称未修改
                            return;
                        }
                        else
                        {
                            Folders[i] = NewName + ">" + Folder.Location;
                        }
                        break;
                    }
                }
                // 如果没有添加过，则添加进去（因为修改了默认项的名称）
                if (!IsAdded)
                    Folders.Add(NewName + ">" + Folder.Location);
                ModMain.Hint("文件夹名称已更新为 " + NewName + " ！", ModMain.HintType.Green);
                // 保存
                Settings.Set("LaunchFolders", Folders.Join("|"));
                ModMinecraft.McFolderListLoader.Start(IsForceRestart: true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重命名文件夹失败");
            }
        }

        // 点击选项
        public void Folder_Change(MyListItem sender, ModBase.RouteEventArgs e)
        {
            if (!e.RaiseByMouse || !sender.Checked)
                return;
            // 下载或版本修改期间禁止更改游戏文件夹
            if (ModMinecraft.McInstanceLock.IsAnyLocked || ModNet.HasDownloadingTask(true))
            {
                ModMain.Hint("请在其他任务完成后，再切换游戏文件夹！", ModMain.HintType.Red);
                e.Handled = true;
                return;
            }
            // 更换
            ModMinecraft.McFolderSelected = ((ModMinecraft.McFolder)sender.Tag).Location;
            ModMinecraft.McFolderListLoader.Start(IsForceRestart: true);
            ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.RunOnUpdated, MaxDepth: 1, ExtraPath: @"versions\"); // 刷新版本列表
        }

    }
}