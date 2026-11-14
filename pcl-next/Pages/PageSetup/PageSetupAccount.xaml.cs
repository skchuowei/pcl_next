using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Extensions;

namespace PCL
{
    public partial class PageSetupAccount
    {

        // 复用启动页同一套图标，避免设置页再手写一套风格不同的图形
        private const string IcoCopy = "M364 0h-273C40 0 0 40 0 91v273C0 414 40 455 91 455h273C414 455 455 414 455 364V91C455 40 414 0 364 0zM341 341H113V113h227v227zM933 0h-273C609 0 568 40 568 91v273c0 50 40 91 91 91h273C983 455 1024 414 1024 364V91c0-50-40-91-90-91zM910 341h-227V113h227v227z";
        private const string IcoDelete = "M979 1024H45c-24.9 0-45-20.1-45-45s20.1-45 45-45h934c24.9 0 45 20.1 45 45s-20.1 45-45 45zM135 887C60.6 887 0 826.4 0 752V334c0-74.4 60.6-135 135-135s135 60.6 135 135v418c0 74.4-60.6 135-135 135z m0-598c-24.8 0-45 20.2-45 45v418c0 24.8 20.2 45 45 45s45-20.2 45-45V334c0-24.8-20.2-45-45-45zM512 887c-74.4 0-135-60.6-135-135V532c0-74.4 60.6-135 135-135s135 60.6 135 135v220c0 74.4-60.6 135-135 135z m0-400c-24.8 0-45 20.2-45 45v220c0 24.8 20.2 45 45 45s45-20.2 45-45V532c0-24.8-20.2-45-45-45zM889 887c-74.4 0-135-60.6-135-135V135C754 60.6 814.6 0 889 0s135 60.6 135 135v617c0 74.4-60.6 135-135 135z m0-797c-24.8 0-45 20.2-45 45v617c0 24.8 20.2 45 45 45s45-20.2 45-45V135c0-24.8-20.2-45-45-45z";

        private bool IsSkinHooked = false;

        public PageSetupAccount()
        {
            InitializeComponent();
            this.Loaded += PageSetupAccount_Loaded;
        }

        private void PageSetupAccount_Loaded(object sender, RoutedEventArgs e)
        {
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
                // 没有账号时也要保留「添加账号」入口，否则用户只能回启动页操作
                PanAccounts.Children.Add(CreateAddAccountButton());
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
                    LabEmpty.Text = "尚未添加任何账号，点击上方「添加账号」开始。";
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

        private FrameworkElement CreateAddAccountButton()
        {
            var Btn = new MyButton()
            {
                Text = "添加账号",
                Height = 28d,
                Width = 100d,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
                ColorType = MyButton.ColorState.Highlight,
                Padding = new Thickness(12, 3, 12, 3)
            };
            Btn.Click += (_, __) => AddAccount();
            return Btn;
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
            var BtnEdit = new MyIconButton() { Logo = ModBase.Logo.IconButtonEdit, ToolTip = "编辑账号", Height = 25, Width = 25 };
            BtnEdit.Click += (_, __) => EditAccount(Entry);
            var BtnCopy = new MyIconButton() { Logo = IcoCopy, ToolTip = "复制账号名称", Height = 25, Width = 25 };
            BtnCopy.Click += (_, __) => ModAccountList.CopyName(Entry);
            var BtnDelete = new MyIconButton() { Logo = IcoDelete, ToolTip = "删除账号", Theme = MyIconButton.Themes.Red, Height = 25, Width = 25 };
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

        private void EditAccount(AccountEntry Entry)
        {
            try
            {
                var Options = new List<IMyRadio>();
                switch (Entry.Type)
                {
                    case ModLaunch.McLoginType.Legacy:
                        Options.Add(new MyListItem() { Title = "修改玩家名", Height = 36d });
                        Options.Add(new MyListItem() { Title = "更换离线皮肤", Height = 36d });
                        break;
                    case ModLaunch.McLoginType.Auth:
                        Options.Add(new MyListItem() { Title = "修改密码", Height = 36d });
                        Options.Add(new MyListItem() { Title = "修改验证服务器", Height = 36d });
                        break;
                    case ModLaunch.McLoginType.Nide:
                        Options.Add(new MyListItem() { Title = "修改密码", Height = 36d });
                        break;
                    default:
                        ModMain.Hint("微软账号的名称与皮肤由微软账户管理，请在网页端修改", ModMain.HintType.Blue);
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
                            OpenSkinSettings();
                        break;
                    case ModLaunch.McLoginType.Auth:
                        if (Choice.Value == 0)
                            ChangePassword(Entry);
                        else
                            ChangeAuthServer();
                        break;
                    case ModLaunch.McLoginType.Nide:
                        ChangePassword(Entry);
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

        private void OpenSkinSettings()
        {
            try
            {
                ModMain.FrmMain?.PageChange(new FormMain.PageStackData() { Page = FormMain.PageType.Setup }, FormMain.PageSubType.SetupLaunch);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "打开启动设置更换皮肤失败");
            }
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
