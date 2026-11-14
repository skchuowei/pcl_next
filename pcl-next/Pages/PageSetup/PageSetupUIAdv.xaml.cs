using System;
using System.Windows;
using MeloongCore;

namespace PCL
{
    public partial class PageSetupUIAdv
    {

        private bool _IsRefreshing = false;

        private MyColorEdit[] ColorControls => new[] { ColorAccent, ColorBg, ColorText, ColorBtnDefault, ColorBtnHover, ColorBtnPressed, ColorBtnDisabled, ColorPopup };

        public PageSetupUIAdv()
        {
            InitializeComponent();
            this.BtnApply.Click += (_, __) => Apply();
            this.BtnCancel.Click += (_, __) => Cancel();
            this.BtnResetAll.Click += (_, __) => Reset();
            this.Loaded += (_, __) => Refresh();

            // 取消实时预览：控件只改显示值，任何改动都只标记「未应用」，不写设置。
            foreach (var Ctl in ColorControls)
                Ctl.ValueChanged += (_, __) => MarkPending();
            this.SliderAnimSpeed.Change += (_, __) => { MarkPending(); UpdateLabels(); };
            this.SliderAnimFps.Change += (_, __) => { MarkPending(); UpdateLabels(); };
            this.SliderPopupFrames.Change += (_, __) => { MarkPending(); UpdateLabels(); };
            this.RadioEase0.Check += (_, __) => MarkPending();
            this.RadioEase1.Check += (_, __) => MarkPending();
            this.RadioEase2.Check += (_, __) => MarkPending();
            this.RadioEase3.Check += (_, __) => MarkPending();
        }

        /// <summary>
        /// 从设置载入全部显示值。颜色项由 SettingService 刷新（带 Key），
        /// 动画项无 Key、由本页手动读写，避免控件自身即时保存。
        /// </summary>
        public void Refresh()
        {
            _IsRefreshing = true;
            try
            {
                SettingService.RefreshSettings(this);
                this.SliderAnimSpeed.Value = Settings.Get<int>("UiDevAnimSpeed");
                this.SliderAnimFps.Value = Settings.Get<int>("UiDevAnimFps");
                this.SliderPopupFrames.Value = Settings.Get<int>("UiDevAnimPopupHideFrames");
                int Ease = Settings.Get<int>("UiDevAnimEase");
                this.RadioEase0.Checked = Ease == 0;
                this.RadioEase1.Checked = Ease == 1;
                this.RadioEase2.Checked = Ease == 2;
                this.RadioEase3.Checked = Ease == 3;
                UpdateLabels();
                this.LabPending.Text = "";
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
            finally
            {
                _IsRefreshing = false;
            }
        }

        /// <summary>把页面上所有当前显示值一次性写入设置并生效。</summary>
        private void Apply()
        {
            try
            {
                _IsRefreshing = true;
                foreach (var Ctl in ColorControls)
                    SettingService.SaveSetting(Ctl);
                Settings.Set("UiDevAnimSpeed", this.SliderAnimSpeed.Value);
                Settings.Set("UiDevAnimEase", GetEase());
                Settings.Set("UiDevAnimFps", ClampFps(this.SliderAnimFps.Value));
                Settings.Set("UiDevAnimPopupHideFrames", this.SliderPopupFrames.Value);
                _IsRefreshing = false;
                // 统一重刷主题基线并叠加覆盖，配色/按钮/标题栏/背景一次到位
                ModSecret.ThemeRefresh();
                ModMain.Hint("已应用 UI 调试设置", ModMain.HintType.Green, false);
                Refresh();
            }
            catch (Exception ex)
            {
                _IsRefreshing = false;
                Logger.Error(ex, "应用 UI 调试设置失败", LogBehavior.Alert);
            }
        }

        /// <summary>丢弃未应用的改动，回到已保存的值。</summary>
        private void Cancel()
        {
            Refresh();
            ModMain.Hint("已取消未应用的改动", ModMain.HintType.Green, false);
        }

        /// <summary>恢复默认：清空所有开发者覆盖，回到主题 / 动画基线（立即生效）。</summary>
        public void Reset()
        {
            try
            {
                _IsRefreshing = true;
                SettingService.ResetSettings(this);
                Settings.Reset("UiDevAnimSpeed");
                Settings.Reset("UiDevAnimEase");
                Settings.Reset("UiDevAnimFps");
                Settings.Reset("UiDevAnimPopupHideFrames");
                _IsRefreshing = false;
                ModSecret.ThemeRefresh();
                Logger.Info("已初始化开发测试设置！");
                ModMain.Hint("已恢复默认配色与动画基线", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                _IsRefreshing = false;
                Logger.Error(ex, "初始化开发测试设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

        private int GetEase()
        {
            if (this.RadioEase1.Checked) return 1;
            if (this.RadioEase2.Checked) return 2;
            if (this.RadioEase3.Checked) return 3;
            return 0;
        }

        private static int ClampFps(int Value) => Math.Max(15, Math.Min(240, Value));

        /// <summary>所有数值都显示具体数字，便于精确复现。</summary>
        private void UpdateLabels()
        {
            int Speed = this.SliderAnimSpeed.Value;
            int Fps = ClampFps(this.SliderAnimFps.Value);
            int Frames = this.SliderPopupFrames.Value;
            this.LabSpeed.Text = $"速度：{Speed}（{Speed / 10d:0.0}×）";
            this.LabFps.Text = $"渲染帧率：{Fps} FPS（帧间隔 {Math.Round(1000d / Fps)} ms）";
            this.LabPopupFrames.Text = $"消失保帧：{Frames} 帧（≈ {Math.Round(Frames * 1000d / Fps)} ms）";
        }

        private void MarkPending()
        {
            if (_IsRefreshing)
                return;
            this.LabPending.Text = "● 有未应用的改动，点击「应用」后生效";
        }

    }
}
