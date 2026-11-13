using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MeloongCore;
using Microsoft.VisualBasic;

namespace PCL
{
    public partial class PageSetupAccount
    {

        // 复用工作区现有几何路径，避免手写无效 SVG
        private const string IcoCopy = "M364 0h-273C40 0 0 40 0 91v273C0 414 40 455 91 455h273C414 455 455 414 455 364V91C455 40 414 0 364 0zM341 341H113V113h227v227zM933 0h-273C609 0 568 40 568 91v273c0 50 40 91 91 91h273C983 455 1024 414 1024 364V91c0-50-40-91-90-91zM910 341h-227V113h227v227z";
        private const string IcoSwitch = "M530 0c287 0 521 229 521 511s-233 511-521 511c-233 0-436-151-500-368a63 63 0 0 1 44-79 65 65 0 0 1 80 43c48 162 200 276 375 276 215 0 390-171 390-383s-174-383-390-383c-103 0-199 39-270 106l21-5a63 63 0 0 1 33 123l-157 42a65 65 0 0 1-90-42l-49-183a65 65 0 1 1 126-33l6 26A524 524 0 0 1 530 0z";
        private const string IcoDelete = "M979 1024H45c-24.9 0-45-20.1-45-45s20.1-45 45-45h934c24.9 0 45 20.1 45 45s-20.1 45-45 45zM135 887C60.6 887 0 826.4 0 752V334c0-74.4 60.6-135 135-135s135 60.6 135 135v418c0 74.4-60.6 135-135 135z m0-598c-24.8 0-45 20.2-45 45v418c0 24.8 20.2 45 45 45s45-20.2 45-45V334c0-24.8-20.2-45-45-45zM512 887c-74.4 0-135-60.6-135-135V532c0-74.4 60.6-135 135-135s135 60.6 135 135v220c0 74.4-60.6 135-135 135z m0-400c-24.8 0-45 20.2-45 45v220c0 24.8 20.2 45 45 45s45-20.2 45-45V532c0-24.8-20.2-45-45-45zM889 887c-74.4 0-135-60.6-135-135V135C754 60.6 814.6 0 889 0s135 60.6 135 135v617c0 74.4-60.6 135-135 135z m0-797c-24.8 0-45 20.2-45 45v617c0 24.8 20.2 45 45 45s45-20.2 45-45V135c0-24.8-20.2-45-45-45z";

        public PageSetupAccount()
        {
            InitializeComponent();
            this.Loaded += PageSetupAccount_Loaded;
        }

        private void PageSetupAccount_Loaded(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        /// <summary>刷新账号列表。每次进入页面或增删/切换后调用。</summary>
        public void Refresh()
        {
            try
            {
                PanAccounts.Children.Clear();
                var Accounts = ModAccountList.GetAll();
                var Current = ModAccountList.GetCurrent();
                if (Accounts.Count == 0)
                {
                    LabEmpty.Visibility = Visibility.Visible;
                    return;
                }
                LabEmpty.Visibility = Visibility.Collapsed;
                foreach (var Entry in Accounts)
                {
                    var Item = new MyListItem();
                    Item.Height = 42;
                    Item.Title = Entry.Name;
                    Item.Info = Entry.InfoText;
                    Item.Type = MyListItem.CheckType.RadioBox;
                    Item.Tag = Entry;
                    Item.IsScaleAnimationEnabled = false;
                    if (Current != null && Current.Type == Entry.Type && (Current.Name ?? "") == Entry.Name)
                        Item.Checked = true;
                    // 悬停操作：复制名称 / 设为当前 / 删除
                    var BtnCopy = new MyIconButton() { ToolTip = "复制账号名称", Logo = IcoCopy, Height = 25, Width = 25 };
                    BtnCopy.Click += (s, ev) => ModAccountList.CopyName(Entry);
                    var BtnSwitch = new MyIconButton() { ToolTip = "切换为当前账号", Logo = IcoSwitch, Height = 25, Width = 25 };
                    BtnSwitch.Click += (s, ev) => { ModAccountList.SwitchTo(Entry); Refresh(); };
                    var BtnDelete = new MyIconButton() { ToolTip = "删除账号", Logo = IcoDelete, Height = 25, Width = 25 };
                    BtnDelete.Click += (s, ev) => DeleteAccount(Entry);
                    Item.Buttons = new List<MyIconButton>() { BtnCopy, BtnSwitch, BtnDelete };
                    PanAccounts.Children.Add(Item);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新我的账号列表失败");
            }
        }

        private void DeleteAccount(AccountEntry Entry)
        {
            if (ModMain.MyMsgBox("确定要删除账号「" + Entry.Name + "」吗？此操作不可撤销，仅移除本地登录记录。", "删除账号", Button2: "取消", IsWarn: true) == 1)
            {
                ModAccountList.Delete(Entry);
                Refresh();
            }
        }

        /// <summary>由左侧导航的「刷新账号列表」按钮触发，重新读取并重建账号列表。</summary>
        public void Reset()
        {
            Refresh();
        }
    }
}
