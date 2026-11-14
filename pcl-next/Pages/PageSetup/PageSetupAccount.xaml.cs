using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Extensions;

namespace PCL
{
    public partial class PageSetupAccount
    {

        // 账号行图标统一取自用户提供的图标集（D:\bing\MC\pcl_x\icons），
        // 该套为 24×24 stroke 线框图标，这里已预转换为填充轮廓路径（MyIconButton 只吃填充几何）
        private const string IcoEdit = "F1M4,21L4,19 8,19 8,20 7.292893409729004,19.292892456054688 17.292892456054688,9.292893409729004 18,10 17.292892456054688,10.707106590270996 13.292893409729004,6.707106590270996 14,6 14.707106590270996,6.707106590270996 4.707106590270996,16.707107543945312 4,16 5,16 5,20 4,20 5,20 4,21 3,21 3,20 3,15.585786819458008 3.292893171310425,15.292893409729004 14,4.58578634262085 14.707106590270996,5.292893409729004 19.414213180541992,10 18.707107543945312,10.707106590270996 8.414213180541992,21 8,21 4,21z M13.292893409729004,6.707106590270996L14.707106590270996,5.292893409729004 18.707107543945312,9.292893409729004 17.292892456054688,10.707106590270996 13.292893409729004,6.707106590270996z";
        private const string IcoCopy = "F1M8,9L8,7 21,7 22,7 22,21 22,22 8,22 7,22 7,8 7,7 8,7 9,8 8,8 9,8 9,21 8,21 8,20 21,20 21,21 20,21 20,8 21,8 21,9 8,9z M17,8L15,8 15,4 16,4 16,5 4,5 4,4 5,4 5,17 4,17 4,16 8,16 8,18 3,18 3,17 3,3 4,3 17,3 17,4 17,8z";
        private const string IcoTrash = "F1M4,7L4,5 20,5 20,7 4,7z M5,6L7,6 7,20 6,20 6,19 18,19 18,20 17,20 17,6 19,6 19,21 18,21 5,21 5,20 5,6z M10,6L8,6 8,4 8,3 15,3 16,3 16,6 14,6 14,4 15,4 15,5 9,5 9,4 10,4 10,6z M9,10L11,10 11,17 9,17 9,10z M13,10L15,10 15,17 13,17 13,10z";

        // 第三方 / 正版账号相关站点（取值参考 PCL2-CE：PageLoginAuth 注册地址、PageLoginMs 官网与购买页）
        private const string UrlLittleSkinRegister = "https://littleskin.cn/auth/register";
        private const string UrlMsAccount = "https://account.microsoft.com/";

        private bool IsSkinHooked = false;

        public PageSetupAccount()
        {
            InitializeComponent();
            this.Loaded += PageSetupAccount_Loaded;
        }

        private void PageSetupAccount_Loaded(object sender, RoutedEventArgs e)
        {
            this.PanBack.ScrollToHome();
            // 皮肤下载完成后要刷新列表头像。订阅放在这里并只挂一次，避免页面重复 Loaded 时叠加事件。
            if (!IsSkinHooked)
            {
                IsSkinHooked = true;
                foreach (var Loader in PageLaunchLeft.SkinLoaders)
                {
                    Loader.OnStateChangedUi += (_, NewState, __) =>
                    {
                        if (NewState == ModBase.LoadState.Finished && this.IsLoaded)
                            Refresh();
                    };
                }
            }
            Refresh();
        }

        /// <summary>刷新账号列表。每次进入页面或增删/切换后调用。</summary>
        public void Refresh()
        {
            try
            {
                PanAccounts.Children.Clear();
                PanAccounts.Children.Add(CreateToolbar());
                var Accounts = ModAccountList.GetAll();
                var Current = ModAccountList.GetCurrent();
                if (Current != null && string.IsNullOrEmpty(Current.Name))
                    Current = null;
                if (Current is null && Accounts.Count > 0)
                    Current = Accounts[0];
                // 当前账号可能是第三方，先启动对应皮肤加载器，头像才有机会变成真实皮肤
                PageLaunchLeft.EnsureAccountSkin(Current);
                if (Accounts.Count == 0)
                {
                    LabEmpty.Text = "尚未添加任何账号，点击上方「新建账号」开始。";
                    LabEmpty.Visibility = Visibility.Visible;
                    return;
                }
                LabEmpty.Visibility = Visibility.Collapsed;
                foreach (var Entry in Accounts)
                {
                    bool IsCurrent = Current != null && Current.Type == Entry.Type && (Current.Name ?? "") == (Entry.Name ?? "");
                    PanAccounts.Children.Add(CreateAccountItem(Entry, Current, IsCurrent));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新我的账号列表失败");
            }
        }

        /// <summary>列表顶部工具栏：只放一个文字按钮「添加账号」，不占用图标语义。</summary>
        private FrameworkElement CreateToolbar()
        {
            var Bar = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var BtnAdd = new MyButton()
            {
                Text = "添加账号",
                Height = 28d,
                Padding = new Thickness(14, 3, 14, 3),
                ColorType = MyButton.ColorState.Highlight
            };
            BtnAdd.Click += (_, __) => AddAccount();
            Bar.Children.Add(BtnAdd);
            return Bar;
        }

        /// <summary>
        /// 账号行与启动页选择态保持同一套视觉：头像 + 名称 + 类型，当前账号浅蓝圆角底、
        /// 蓝色加粗标题，悬停出现编辑 / 复制 / 删除三个 25px 线框按钮。
        /// </summary>
        private FrameworkElement CreateAccountItem(AccountEntry Entry, AccountEntry Current, bool IsCurrent)
        {
            var Item = new MyListItem()
            {
                Title = Entry.Name,
                Info = Entry.InfoText,
                Height = 42d,
                Margin = new Thickness(4, 0, 4, 0),
                LogoScale = 0.92d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Entry
            };
            string Head = PageLaunchLeft.GetAccountHead(Entry, Current, 64);
            if (!string.IsNullOrEmpty(Head) && FileUtils.Exists(Head))
                Item.Logo = Head;
            if (IsCurrent)
            {
                // MyListItem_Loaded 会按 Checked 重置前景色，必须在加载完成后覆盖
                Item.Loaded += (_, __) =>
                {
                    try
                    {
                        Item.SetResourceReference(MyListItem.ForegroundProperty, "ColorBrush2");
                        if (Item.FindName("LabTitle") is TextBlock TitleText)
                            TitleText.FontWeight = FontWeights.Bold;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "设置账号选中样式失败");
                    }
                };
            }
            var BtnEdit = new MyIconButton() { Logo = IcoEdit, ToolTip = "编辑账号", Height = 25, Width = 25 };
            BtnEdit.Click += (_, __) => EditAccount(Entry);
            var BtnCopy = new MyIconButton() { Logo = IcoCopy, ToolTip = "复制账号名称", Height = 25, Width = 25 };
            BtnCopy.Click += (_, __) => ModAccountList.CopyName(Entry);
            var BtnDelete = new MyIconButton() { Logo = IcoTrash, ToolTip = "删除账号", Theme = MyIconButton.Themes.Red, Height = 25, Width = 25 };
            BtnDelete.Click += (_, __) => DeleteAccount(Entry);
            // 按钮不挂在 MyListItem.Buttons 上：MyListItem 的预览鼠标事件会把图标点击吞成整行点击，
            // 这里把按钮作为 Grid 的第二层覆盖在列表项右侧，悬停淡入、移出淡出，点击不会被行拦住。
            var BtnStack = new StackPanel()
            {
                Orientation = Orientation.Horizontal
            };
            BtnStack.Children.Add(BtnEdit);
            BtnStack.Children.Add(BtnCopy);
            BtnStack.Children.Add(BtnDelete);
            // 用带透明背景的 Border 承接整块按钮区域的命中测试：
            // StackPanel 自身没有背景，空隙会把鼠标漏给下面的账号行。
            var BtnHost = new Border()
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(2, 2, 2, 2),
                Opacity = 0d,
                IsHitTestVisible = false,
                Child = BtnStack
            };
            Panel.SetZIndex(BtnHost, 20);
            var RowHost = new Grid();
            RowHost.Children.Add(Item);
            RowHost.Children.Add(BtnHost);
            RowHost.MouseEnter += (_, __) =>
            {
                BtnHost.IsHitTestVisible = true;
                BtnHost.Opacity = 1d;
            };
            RowHost.MouseLeave += (_, __) =>
            {
                BtnHost.Opacity = 0d;
                BtnHost.IsHitTestVisible = false;
            };
            RowHost.Loaded += (_, __) =>
            {
                if (RowHost.IsMouseOver)
                {
                    BtnHost.IsHitTestVisible = true;
                    BtnHost.Opacity = 1d;
                }
            };
            Item.Click += (_, __) =>
            {
                if (IsCurrent)
                {
                    ModMain.Hint("已经是当前账号", ModMain.HintType.Blue);
                    return;
                }
                ModAccountList.SwitchTo(Entry);
                Refresh();
                ModMain.Hint("已切换账号：" + Entry.Name, ModMain.HintType.Green);
            };
            return new Border()
            {
                CornerRadius = new CornerRadius(6),
                Background = IsCurrent ? (Brush)FindResource("ColorBrush7") : Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 4),
                Child = RowHost
            };
        }

        private void AddAccount()
        {
            try
            {
                ModMain.FrmMain?.PageChange(new FormMain.PageStackData() { Page = FormMain.PageType.Launch });
                Dispatcher.BeginInvoke(new Action(() => ModMain.FrmLaunchLeft?.OpenNewAccountDialog()));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "从我的账号页打开新建档案失败");
            }
        }

        #region 更改皮肤

        /// <summary>
        /// 「编辑账号」菜单里的「更改皮肤」：离线账号直接换本地皮肤；
        /// 第三方与正版没有本地可改的皮肤，给出各自平台的入口。
        /// </summary>
        private void ChangeSkinFor(AccountEntry Entry)
        {
            try
            {
                switch (Entry.Type)
                {
                    case ModLaunch.McLoginType.Legacy:
                        ChangeOfflineSkin();
                        break;
                    case ModLaunch.McLoginType.Ms:
                        ModMain.Hint("正版账号的皮肤由微软账号管理，已打开 Microsoft 账号页", ModMain.HintType.Blue);
                        ModBase.OpenWebsite(UrlMsAccount);
                        break;
                    default:
                        {
                            string Register = Settings.Get<string>("CacheAuthServerRegister") ?? "";
                            ModMain.Hint("第三方账号的皮肤由皮肤站管理，本地无法更改", ModMain.HintType.Blue);
                            if (!string.IsNullOrWhiteSpace(Register))
                                ModBase.OpenWebsite(Register);
                            else
                                ModBase.OpenWebsite(UrlLittleSkinRegister);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "更改皮肤失败");
            }
        }

        /// <summary>当前账号必须是离线账号，皮肤才会在游戏里生效。</summary>
        private AccountEntry GetCurrentOffline()
        {
            var Current = ModAccountList.GetCurrent();
            if (Current is null || Current.Type != ModLaunch.McLoginType.Legacy || string.IsNullOrEmpty(Current.Name))
            {
                ModMain.Hint("离线皮肤只对离线登录有效，请先在启动页选择或新建一个离线账号", ModMain.HintType.Blue);
                return null;
            }
            return Current;
        }

        private void ChangeOfflineSkin()
        {
            if (GetCurrentOffline() is null)
                return;
            var SkinInfo = ModMinecraft.McSkinSelect();
            if (!SkinInfo.IsVaild)
                return;
            // 先切到「自定义」模式，再写入 CustomSkin.png，二者顺序不能反，否则预览会先读旧图
            Settings.Set("LaunchSkinType", 4);
            if (ChangeSkin(SkinInfo))
                ModMain.Hint("离线皮肤已更新", ModMain.HintType.Green);
        }

        // 返回是否成功改变
        private bool ChangeSkin(ModMinecraft.McSkinInfo SkinInfo)
        {
            try
            {
                // 复制文件
                FileUtils.Copy(SkinInfo.LocalFile, Paths.AppDataThenName + "CustomSkin.png");
                // 将单层皮肤扩展到双层
                var Bitmap = new MyBitmap(Paths.AppDataThenName + "CustomSkin.png");
                if (Bitmap.Pic.Width == 64 && Bitmap.Pic.Height == 32)
                {
                    System.Drawing.Image Img = Bitmap;
                    var NewBitmap = new System.Drawing.Bitmap(64, 64);
                    using (var g = System.Drawing.Graphics.FromImage(NewBitmap))
                    {
                        g.DrawImageUnscaled(Img, new System.Drawing.Point(0, 0));
                    }
                    FileUtils.Delete(Paths.AppDataThenName + "CustomSkin.png");
                    NewBitmap.Save(Paths.AppDataThenName + "CustomSkin.png");
                }
                // 更新设置
                Settings.Set("LaunchSkinSlim", SkinInfo.IsSlim);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "改变离线皮肤失败", LogBehavior.Alert);
                return false;
            }
            finally
            {
                // 设置当前显示
                PageLaunchLeft.SkinLegacy.Start(IsForceRestart: true);
            }
        }

        /// <summary>皮肤类型变化后刷新一次离线皮肤预览（控件已随卡片移除，只保留加载器动作）。</summary>
        public static void UpdateSkinType()
        {
            PageLaunchLeft.SkinLegacy.Start();
            ModMain.FrmSetupAccount?.Refresh();
        }

        #endregion

        private void EditAccount(AccountEntry Entry)
        {
            try
            {
                var Options = new List<IMyRadio>();
                switch (Entry.Type)
                {
                    case ModLaunch.McLoginType.Legacy:
                        Options.Add(new MyListItem() { Title = "修改玩家名", Height = 36d });
                        Options.Add(new MyListItem() { Title = "更改皮肤", Height = 36d, Info = "选择一张图片作为当前离线账号的皮肤" });
                        break;
                    case ModLaunch.McLoginType.Auth:
                        Options.Add(new MyListItem() { Title = "更改皮肤", Height = 36d, Info = "皮肤由皮肤站管理，将打开对应网站" });
                        Options.Add(new MyListItem() { Title = "修改密码", Height = 36d });
                        Options.Add(new MyListItem() { Title = "修改验证服务器", Height = 36d });
                        break;
                    case ModLaunch.McLoginType.Nide:
                        Options.Add(new MyListItem() { Title = "更改皮肤", Height = 36d, Info = "皮肤由皮肤站管理，将打开对应网站" });
                        Options.Add(new MyListItem() { Title = "修改密码", Height = 36d });
                        break;
                    case ModLaunch.McLoginType.Ms:
                        Options.Add(new MyListItem() { Title = "更改皮肤", Height = 36d, Info = "皮肤由微软账号管理，将打开 Microsoft 账号页" });
                        break;
                    default:
                        ModMain.Hint("该账号类型暂不支持编辑", ModMain.HintType.Blue);
                        return;
                }
                int? Choice = ModMain.MyMsgBoxSelect(Options, "编辑账号", "继续", "取消");
                if (Choice is null)
                    return;
                switch (Entry.Type)
                {
                    case ModLaunch.McLoginType.Legacy:
                        if (Choice.Value == 0)
                            RenameLegacy(Entry);
                        else
                            ChangeSkinFor(Entry);
                        break;
                    case ModLaunch.McLoginType.Auth:
                        if (Choice.Value == 0)
                            ChangeSkinFor(Entry);
                        else if (Choice.Value == 1)
                            ChangePassword(Entry);
                        else
                            ChangeAuthServer();
                        break;
                    case ModLaunch.McLoginType.Nide:
                        if (Choice.Value == 0)
                            ChangeSkinFor(Entry);
                        else
                            ChangePassword(Entry);
                        break;
                    case ModLaunch.McLoginType.Ms:
                        ChangeSkinFor(Entry);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "编辑账号失败");
            }
        }

        private void RenameLegacy(AccountEntry Entry)
        {
            string NewName = ModMain.MyMsgBoxInput("修改玩家名", "请输入新的离线玩家名。", Entry.Name, HintText: "玩家名");
            if (NewName is null)
                return;
            string Error = ModAccountList.Rename(Entry, NewName);
            if (string.IsNullOrEmpty(Error))
            {
                ModMain.Hint("玩家名已修改", ModMain.HintType.Green);
                Refresh();
            }
            else
            {
                ModMain.Hint(Error, ModMain.HintType.Red);
            }
        }

        private void ChangePassword(AccountEntry Entry)
        {
            string Password = ModMain.MyMsgBoxInput("修改密码", "请输入新的密码。", HintText: "密码");
            if (Password is null)
                return;
            string Error = ModAccountList.SetPassword(Entry, Password);
            if (string.IsNullOrEmpty(Error))
            {
                ModMain.Hint("密码已修改，下次启动会使用新密码登录", ModMain.HintType.Green);
                Refresh();
            }
            else
            {
                ModMain.Hint(Error, ModMain.HintType.Red);
            }
        }

        private void ChangeAuthServer()
        {
            if (!PageLaunchLeft.PromptAuthServer())
                return;
            ModMain.Hint("验证服务器已保存，启动时会重新登录并刷新皮肤", ModMain.HintType.Green);
            Refresh();
        }

        private void DeleteAccount(AccountEntry Entry)
        {
            if (ModMain.MyMsgBox("确定要删除账号「" + Entry.Name + "」吗？此操作不可撤销，仅移除本地登录记录。", "删除账号", Button1: "删除", Button2: "取消", IsWarn: true) == 1)
            {
                ModAccountList.Delete(Entry);
                Refresh();
                ModMain.Hint("已删除账号：" + Entry.Name, ModMain.HintType.Green);
            }
        }

        /// <summary>由左侧导航的「刷新账号列表」按钮触发，重新读取并重建账号列表。</summary>
        public void Reset()
        {
            Refresh();
        }
    }
}
