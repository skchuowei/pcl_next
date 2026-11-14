using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public partial class PageSetupNotify
    {

        private bool _PageSetupNotify_Loaded_IsLoaded = false;

        public PageSetupNotify()
        {
            InitializeComponent();
            this.BtnSystemIdentify.Click += (p1, p2) => BtnSystemIdentify_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnSystemUnlock.Click += (p1, p2) => BtnSystemUnlock_Click((object)p1, (MouseButtonEventArgs)p2);
            this.ComboSystemActivity.SelectionChanged += (p1, p2) => ComboSystemActivity_SelectionChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.ComboSystemUpdate.SelectionChanged += (p1, p2) => ComboSystemUpdate_SelectionChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.BtnSystemUpdate.Click += (p1, p2) => BtnSystemUpdate_Click((object)p1, (EventArgs)p2);
            this.BtnSystemSettingExp.Click += (p1, p2) => BtnSystemSettingExp_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnSystemSettingImp.Click += (p1, p2) => BtnSystemSettingImp_Click((object)p1, (MouseButtonEventArgs)p2);
            this.Loaded += PageSetupNotify_Loaded;
        }

        private void PageSetupNotify_Loaded(object sender, RoutedEventArgs e)
        {
            this.PanBack.ScrollToHome();

            if (ModBuildChannel.ChannelIsOfficial)
            {
                this.PanDonate.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.PanDonate.Visibility = Visibility.Visible;
                this.ItemSystemUpdateDownload.Content = "在有新版本时自动下载（更新非正式版前需要先输入土豆码）";
            }
            if (_PageSetupNotify_Loaded_IsLoaded)
                return;
            _PageSetupNotify_Loaded_IsLoaded = true;

            ModAnimation.AniControlEnabled += 1;
            Refresh();
            ModAnimation.AniControlEnabled -= 1;
        }

        public void Refresh()
        {
            try
            {
                SettingService.RefreshSettings(this);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重载通知与更新设置时出错");
            }
        }

        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化通知与更新设置");
                ModMain.Hint("已初始化通知与更新设置！", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化通知与更新设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

        // 识别码/土豆码替代入口
        private void BtnSystemIdentify_Click(object sender, MouseButtonEventArgs e)
        {
            PageOtherAbout.CopyIdentify();
        }
        private void BtnSystemUnlock_Click(object sender, MouseButtonEventArgs e)
        {
            ModSecret.InputPotatoCode(false);
        }

        // 自动更新
        private void ComboSystemActivity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (this.ComboSystemActivity.SelectedIndex != 2)
                return;
            if (ModMain.MyMsgBox("若选择此项，即使在将来出现严重问题时，你也无法获取相关通知。" + Constants.vbCrLf + "例如，如果发现某个版本游戏存在严重 Bug，你可能就会因为无法得到通知而导致无法预知的后果。" + Constants.vbCrLf + Constants.vbCrLf + "一般选择 仅在有重要通知时显示公告 就可以让你尽量不受打扰了。" + Constants.vbCrLf + "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn: true) == 2)
            {
                if (e.RemovedItems.Count > 0)
                    this.ComboSystemActivity.SelectedItem = e.RemovedItems[0];
            }
        }
        private void ComboSystemUpdate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (this.ComboSystemUpdate.SelectedIndex != 3)
                return;
            if (ModMain.MyMsgBox("若选择此项，即使在启动器将来出现严重问题时，你也无法获取更新并获得修复。" + Constants.vbCrLf + "例如，如果官方修改了登录方式，从而导致现有启动器无法登录，你可能就会因为无法更新而无法开始游戏。" + Constants.vbCrLf + Constants.vbCrLf + "一般选择 仅在有重大漏洞更新时显示提示 就可以让你尽量不受打扰了。" + Constants.vbCrLf + "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn: true) == 2)
            {
                if (e.RemovedItems.Count > 0)
                    this.ComboSystemUpdate.SelectedItem = e.RemovedItems[0];
            }
        }
        private void BtnSystemUpdate_Click(object sender, EventArgs e)
        {
            ModSecret.UpdateCheckByButton();
        }
        /// <summary>
        /// 启动器是否已经是最新版？
        /// 若返回 Nothing，则代表无更新缓存文件或出错。
        /// </summary>
        public static bool? IsLauncherNewest()
        {
            try
            {
                // 确认服务器公告是否正常
                string ServerContent = FileUtils.TryReadAsString(ModBase.PathTemp + @"Cache\Notice.cfg") ?? "";
                if (ServerContent.Split("|").Count() < 3)
                    return default;
                // 确认是否为最新
                return Conversions.ToDouble(ServerContent.Split("|")[ModBuildChannel.ChannelIsOfficial ? 2 : 1]) <= ModBase.VersionCode;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "确认启动器更新失败");
                return default;
            }
        }

        #region 导出 / 导入设置

        private void BtnSystemSettingExp_Click(object sender, MouseButtonEventArgs e)
        {
            ModMain.Hint("该功能尚在开发中！");
        }
        private void BtnSystemSettingImp_Click(object sender, MouseButtonEventArgs e)
        {
            ModMain.Hint("该功能尚在开发中！");
        }

        #endregion

    }
}
