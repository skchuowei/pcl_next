using System;
using System.Windows;
using MeloongCore;

namespace PCL
{
    public partial class PageSetupUIAdv
    {

        public PageSetupUIAdv()
        {
            InitializeComponent();
            this.BtnResetAll.Click += (_, __) => Reset();
            this.Loaded += PageSetupUIAdv_Loaded;
        }

        private void PageSetupUIAdv_Loaded(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        /// <summary>
        /// 刷新本页所有开发者设置控件。颜色/动画的实际生效由各自设置的 OnChanged
        /// （ThemeRefresh 重刷基线后叠加覆盖）与 ModAnimation 的实时读取完成，这里只同步 UI 显示。
        /// </summary>
        public void Refresh()
        {
            try
            {
                SettingService.RefreshSettings(this);
            }
            catch (NullReferenceException ex)
            {
                Logger.Error(ex, "开发测试设置项存在异常，已被自动重置", LogBehavior.Alert);
                Reset();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重载开发测试设置时出错");
            }
        }

        /// <summary>
        /// 恢复默认：清空所有开发者覆盖，回到主题 / 动画基线。
        /// 每次 Settings.Reset 都会触发对应设置的 OnChanged（ThemeRefresh），即时还原正式配色。
        /// </summary>
        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化开发测试设置！");
                ModMain.Hint("已恢复默认配色与动画基线", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化开发测试设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

    }
}
