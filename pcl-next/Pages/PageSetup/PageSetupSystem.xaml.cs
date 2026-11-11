using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class PageSetupSystem
    {

        // 非重复加载部分
        private bool _PageSetupSystem_Loaded_IsLoaded = false;

        public PageSetupSystem()
        {
            InitializeComponent();
            this.SliderDownloadThread.PreviewChange += (p1, p2) => SliderDownloadThread_PreviewChange((object)p1, (ModBase.RouteEventArgs)p2);
            this.BtnSystemIdentify.Click += (p1, p2) => BtnSystemIdentify_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnSystemUnlock.Click += (p1, p2) => BtnSystemUnlock_Click((object)p1, (MouseButtonEventArgs)p2);
            this.CheckDebugMode.Change += (p1, p2) => CheckDebugMode_Change();
            this.ComboSystemActivity.SelectionChanged += (p1, p2) => ComboSystemActivity_SelectionChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.ComboSystemUpdate.SelectionChanged += (p1, p2) => ComboSystemUpdate_SelectionChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.BtnSystemUpdate.Click += (p1, p2) => BtnSystemUpdate_Click((object)p1, (EventArgs)p2);
            this.BtnSystemSettingExp.Click += (p1, p2) => BtnSystemSettingExp_Click((object)p1, (MouseButtonEventArgs)p2);
            this.BtnSystemSettingImp.Click += (p1, p2) => BtnSystemSettingImp_Click((object)p1, (MouseButtonEventArgs)p2);
            this.Loaded += PageSetupSystem_Loaded;
        }


        private void PageSetupSystem_Loaded(object sender, RoutedEventArgs e)
        {

            // 重复加载部分
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
            if (_PageSetupSystem_Loaded_IsLoaded)
                return;
            _PageSetupSystem_Loaded_IsLoaded = true;

            ModAnimation.AniControlEnabled += 1;
            Reload();
            SliderLoad();
            ModAnimation.AniControlEnabled -= 1;

        }
        public void Reload()
        {
            SettingService.RefreshSettings(this);
        }
        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化启动器页设置");
                ModMain.Hint("已初始化启动器页设置！", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化启动器页设置失败", LogBehavior.Alert);
            }
            Reload();
        }

        // 滑动条
        private void SliderLoad()
        {
            this.SliderDownloadThread.GetHintText = new Func<object, object>(v => Operators.AddObject(v, 1));
            this.SliderDownloadSpeed.GetHintText = new Func<object, object>(v => { switch (v) { case var @case when Operators.ConditionalCompareObjectLessEqual(@case, 14, false): { return Operators.ConcatenateObject(Operators.MultiplyObject(Operators.AddObject(v, 1), 0.1d), " M/s"); } case var case1 when Operators.ConditionalCompareObjectLessEqual(case1, 31, false): { return Operators.ConcatenateObject(Operators.MultiplyObject(Operators.SubtractObject(v, 11), 0.5d), " M/s"); } case var case2 when Operators.ConditionalCompareObjectLessEqual(case2, 41, false): { return Operators.ConcatenateObject(Operators.SubtractObject(v, 21), " M/s"); } default: { return "无限制"; } } });
            this.SliderDebugAnim.GetHintText = new Func<object, object>(v => Conversions.ToBoolean(Operators.ConditionalCompareObjectGreater(v, 29, false)) ? "关闭" : Operators.ConcatenateObject(Operators.AddObject(Operators.DivideObject(v, 10), 0.1d), "x"));
        }
        private void SliderDownloadThread_PreviewChange(object sender, ModBase.RouteEventArgs e)
        {
            if (this.SliderDownloadThread.Value < 100)
                return;
            if (!Settings.Get<bool>("HintDownloadThread"))
            {
                Settings.Set("HintDownloadThread", true);
                ModMain.MyMsgBox("如果设置过多的下载线程，可能会导致下载时出现非常严重的卡顿。" + Constants.vbCrLf + "一般设置 64 线程即可满足大多数下载需求，除非你知道你在干什么，否则不建议设置更多的线程数！", "警告", "我知道了", IsWarn: true);
            }
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

        // 调试模式
        private void CheckDebugMode_Change()
        {
            if (ModAnimation.AniControlEnabled == 0)
                ModMain.Hint("部分调试信息将在刷新或启动器重启后切换显示！", Log: false);
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