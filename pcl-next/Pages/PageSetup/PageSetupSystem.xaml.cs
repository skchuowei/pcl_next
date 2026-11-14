using System;
using System.Windows;
using MeloongCore;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public partial class PageSetupSystem
    {

        // 非重复加载部分
        private bool _PageSetupSystem_Loaded_IsLoaded = false;

        public PageSetupSystem()
        {
            InitializeComponent();
            this.CheckDebugMode.Change += (p1, p2) => CheckDebugMode_Change();
            this.Loaded += PageSetupSystem_Loaded;
        }


        private void PageSetupSystem_Loaded(object sender, RoutedEventArgs e)
        {

            // 重复加载部分
            this.PanBack.ScrollToHome();

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
        public void Refresh()
        {
            Reload();
        }
        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化调试设置");
                ModMain.Hint("已初始化调试设置！", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化调试设置失败", LogBehavior.Alert);
            }
            Reload();
        }

        // 滑动条
        private void SliderLoad()
        {
            this.SliderDebugAnim.GetHintText = new Func<object, object>(v => Conversions.ToBoolean(Operators.ConditionalCompareObjectGreater(v, 29, false)) ? "关闭" : Operators.ConcatenateObject(Operators.AddObject(Operators.DivideObject(v, 10), 0.1d), "x"));
        }

        // 调试模式
        private void CheckDebugMode_Change()
        {
            if (ModAnimation.AniControlEnabled == 0)
                ModMain.Hint("部分调试信息将在刷新或启动器重启后切换显示！", Log: false);
        }

    }
}
