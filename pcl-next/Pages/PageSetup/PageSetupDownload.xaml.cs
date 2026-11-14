using System;
using System.Windows;
using MeloongCore;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public partial class PageSetupDownload
    {

        private bool _PageSetupDownload_Loaded_IsLoaded = false;

        public PageSetupDownload()
        {
            InitializeComponent();
            this.SliderDownloadThread.PreviewChange += (p1, p2) => SliderDownloadThread_PreviewChange((object)p1, (ModBase.RouteEventArgs)p2);
            this.Loaded += PageSetupDownload_Loaded;
        }

        private void PageSetupDownload_Loaded(object sender, RoutedEventArgs e)
        {
            this.PanBack.ScrollToHome();
            if (_PageSetupDownload_Loaded_IsLoaded)
                return;
            _PageSetupDownload_Loaded_IsLoaded = true;

            ModAnimation.AniControlEnabled += 1;
            Refresh();
            SliderLoad();
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
                Logger.Error(ex, "重载下载设置时出错");
            }
        }

        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化下载设置");
                ModMain.Hint("已初始化下载设置！", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化下载设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

        // 滑动条
        private void SliderLoad()
        {
            this.SliderDownloadThread.GetHintText = new Func<object, object>(v => Operators.AddObject(v, 1));
            this.SliderDownloadSpeed.GetHintText = new Func<object, object>(v => { switch (v) { case var @case when Operators.ConditionalCompareObjectLessEqual(@case, 14, false): { return Operators.ConcatenateObject(Operators.MultiplyObject(Operators.AddObject(v, 1), 0.1d), " M/s"); } case var case1 when Operators.ConditionalCompareObjectLessEqual(case1, 31, false): { return Operators.ConcatenateObject(Operators.MultiplyObject(Operators.SubtractObject(v, 11), 0.5d), " M/s"); } case var case2 when Operators.ConditionalCompareObjectLessEqual(case2, 41, false): { return Operators.ConcatenateObject(Operators.SubtractObject(v, 21), " M/s"); } default: { return "无限制"; } } });
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

    }
}
