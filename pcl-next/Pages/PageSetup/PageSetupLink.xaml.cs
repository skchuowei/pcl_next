using System;
using System.Windows;
using MeloongCore;

namespace PCL
{
    public partial class PageSetupLink
    {

        // 非重复加载部分
        private bool _PageSetupLink_Loaded_IsLoaded = false;

        public PageSetupLink()
        {
            InitializeComponent();
            this.Loaded += PageSetupLink_Loaded;
        }

        private void PageSetupLink_Loaded(object sender, RoutedEventArgs e)
        {

            // 重复加载部分
            this.PanBack.ScrollToHome();
            if (_PageSetupLink_Loaded_IsLoaded)
                return;
            _PageSetupLink_Loaded_IsLoaded = true;

            ModAnimation.AniControlEnabled += 1;
            Reload();
            ModAnimation.AniControlEnabled -= 1;

        }
        public void Reload()
        {
            SettingService.RefreshSettings(this);
        }

        // 初始化
        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化联机页设置");
                ModMain.Hint("已初始化联机页设置！", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化联机页设置失败", LogBehavior.Alert);
            }
            Reload();
        }

    }
}