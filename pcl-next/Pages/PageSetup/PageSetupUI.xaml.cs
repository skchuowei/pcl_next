using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public partial class PageSetupUI
    {

        // 非重复加载部分
        private bool _PageSetupUI_Loaded_Reloaded = false;

        public PageSetupUI()
        {
            InitializeComponent();
            this.BtnLogoChange.Click += (p1, p2) => BtnLogoChange_Click((object)p1, (EventArgs)p2);
            this.RadioLogoType3.PreviewCheck += (p1, p2) => RadioLogoType3_Check((object)p1, (ModBase.RouteEventArgs)p2);
            this.BtnLogoDelete.Click += (p1, p2) => BtnLogoDelete_Click((object)p1, (EventArgs)p2);
            this.LabLauncherTheme5Unlock.MouseLeftButtonUp += (p1, p2) => LabLauncherTheme5Unlock_MouseLeftButtonUp((object)p1, (MouseButtonEventArgs)p2);
            this.LabLauncherTheme11Click.MouseLeftButtonUp += (p1, p2) => LabLauncherTheme11Click_MouseLeftButtonUp();
            this.RadioLauncherTheme11.MouseRightButtonUp += (p1, p2) => LabLauncherTheme11Click_MouseLeftButtonUp();
            this.LabLauncherTheme8Copy.MouseRightButtonUp += (p1, p2) => LabLauncherTheme8Copy_MouseRightButtonUp();
            this.RadioLauncherTheme8.MouseRightButtonUp += (p1, p2) => LabLauncherTheme8Copy_MouseRightButtonUp();
            this.LabLauncherTheme9Copy.MouseRightButtonUp += (p1, p2) => LabLauncherTheme9Copy_MouseRightButtonUp();
            this.RadioLauncherTheme9.MouseRightButtonUp += (p1, p2) => LabLauncherTheme9Copy_MouseRightButtonUp();
            this.RadioLauncherTheme14.Changed += (p1, p2) => RadioLauncherTheme14_Change((object)p1, (ModBase.RouteEventArgs)p2);
            this.SliderLauncherHue.Change += (p1, p2) => HSL_Change();
            this.SliderLauncherLight.Change += (p1, p2) => HSL_Change();
            this.SliderLauncherSat.Change += (p1, p2) => HSL_Change();
            this.SliderLauncherDelta.Change += (p1, p2) => HSL_Change();
            this.Loaded += PageSetupUI_Loaded;
        }

        private void PageSetupUI_Loaded(object sender, RoutedEventArgs e)
        {
            // 重复加载部分
            this.PanBack.ScrollToHome();
            ModSecret.ThemeCheckAll(true);
            if (ModSecret.ThemeDontClick != 0)
            {
                string NewText = null;
                switch (ModSecret.ThemeDontClick)
                {
                    case 1:
                        {
                            NewText = "眼瞎白";
                            break;
                        }
                    case 2:
                        {
                            NewText = "真·滑稽彩";
                            break;
                        }
                }
                foreach (var Control in this.PanLauncherTheme.Children)
                {
                    if (Control is MyRadioBox && ((MyRadioBox)Control).IsEnabled)
                        ((MyRadioBox)Control).Text = NewText;
                }
            }

            ModAnimation.AniControlEnabled += 1;
            Refresh(); // #4826
            ModAnimation.AniControlEnabled -= 1;
            if (_PageSetupUI_Loaded_Reloaded)
                return;
            _PageSetupUI_Loaded_Reloaded = true;

            SliderLoad();

            if (ModBuildChannel.ChannelIsOfficial)
                this.PanLauncherHide.Visibility = Visibility.Visible;

            // 设置解锁
            if (!this.RadioLauncherTheme8.IsEnabled)
                this.LabLauncherTheme8Copy.ToolTip = $"累积赞助达到 ¥23.33 后，在爱发电私信发送【土豆 {ModBase.Identify}】以解锁。" + Constants.vbCrLf + "右键打开赞助页面，如果觉得 PCL Next 做得还不错就支持一下吧 =w=！";
            this.RadioLauncherTheme8.ToolTip = $"累积赞助达到 ¥23.33 后，在爱发电私信发送【土豆 {ModBase.Identify}】以解锁";
            if (!this.RadioLauncherTheme9.IsEnabled)
                this.LabLauncherTheme9Copy.ToolTip = "· 反馈一个 Bug，在标记为 [完成] 后回复识别码要求解锁（右键打开反馈页面）" + Constants.vbCrLf + "· 提交一个 Pull Request 或主页预设，在标记为 [完成] 后回复识别码要求解锁";
            this.RadioLauncherTheme9.ToolTip = "· 反馈一个 Bug，在标记为 [完成] 后回复识别码要求解锁" + Constants.vbCrLf + "· 提交一个 Pull Request 或主页预设，在标记为 [完成] 后回复识别码要求解锁";
            // 极客蓝的处理在 ThemeCheck 中

        }
        public void Refresh()
        {
            try
            {
                SettingService.RefreshSettings(this);

                // 标题栏
                this.CheckLogoLeft.Visibility = this.RadioLogoType0.Checked ? Visibility.Visible : Visibility.Collapsed;
                this.PanLogoText.Visibility = this.RadioLogoType2.Checked ? Visibility.Visible : Visibility.Collapsed;
                this.PanLogoChange.Visibility = this.RadioLogoType3.Checked ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (NullReferenceException ex)
            {
                Logger.Error(ex, "外观设置项存在异常，已被自动重置", LogBehavior.Alert);
                Reset();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重载外观设置时出错");
            }
        }
        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化外观设置！");
                ModMain.Hint("已初始化外观设置", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化外观设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

        // 顶部栏
        private void BtnLogoChange_Click(object sender, EventArgs e)
        {
            string FileName = Dialogs.SelectFile("选择图片", false, filter: new[] { (new[] { "png", "jpg", "jpeg", "gif", "webp" }, "常用图片文件") }).FirstOrDefault();
            if (string.IsNullOrEmpty(FileName))
                return;
            string TargetPath = Paths.Base + @"PCL\Logo.png";
            try
            {
                // 复制文件
                FileUtils.Copy(FileName, TargetPath);
                // 设置当前显示
                ModMain.FrmMain.ImageTitleLogo.Source = (string)null; // 防止因为 Source 属性前后的值相同而不更新 (#5628)
                ModMain.FrmMain.ImageTitleLogo.Source = TargetPath;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("参数无效"))
                {
                    Logger.Error($"改变标题栏图片失败，该图片文件可能并非标准格式。{Constants.vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{Constants.vbCrLf}文件：{TargetPath}", LogBehavior.Alert);
                }
                else
                {
                    Logger.Error(ex, "设置标题栏图片失败", LogBehavior.Alert);
                }
                ModMain.FrmMain.ImageTitleLogo.Source = (string)null;
            }
        }
        private void RadioLogoType3_Check(object sender, ModBase.RouteEventArgs e)
        {
            if (!(ModAnimation.AniControlEnabled == 0 && e.RaiseByMouse))
                return;
            Refresh:
            ;

            // 已有图片则不再选择
            string TargetPath = Paths.Base + @"PCL\Logo.png";
            if (FileUtils.Exists(TargetPath))
            {
                try
                {
                    ModMain.FrmMain.ImageTitleLogo.Source = (string)null; // 防止因为 Source 属性前后的值相同而不更新 (#5628)
                    ModMain.FrmMain.ImageTitleLogo.Source = TargetPath;
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("参数无效"))
                    {
                        Logger.Error($"改变标题栏图片失败，该图片文件可能并非标准格式。{Constants.vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{Constants.vbCrLf}文件：{TargetPath}", LogBehavior.Alert);
                    }
                    else
                    {
                        Logger.Error(ex, "调整标题栏图片失败", LogBehavior.Alert);
                    }
                    ModMain.FrmMain.ImageTitleLogo.Source = (string)null;
                    e.Handled = true;
                    try
                    {
                        FileUtils.Delete(TargetPath);
                    }
                    catch (Exception exx)
                    {
                        Logger.Error(exx, "清理错误的标题栏图片失败", LogBehavior.Alert);
                    }
                }
                return;
            }
            // 没有图片则要求选择
            string FileName = Dialogs.SelectFile("选择图片", false, filter: new[] { (new[] { "png", "jpeg", "jpg", "gif", "webp" }, "常用图片文件") }).FirstOrDefault();
            if (string.IsNullOrEmpty(FileName))
            {
                ModMain.FrmMain.ImageTitleLogo.Source = (string)null;
                e.Handled = true;
            }
            else
            {
                try
                {
                    FileUtils.Copy(FileName, TargetPath);
                    goto Refresh;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "复制标题栏图片失败", LogBehavior.Alert);
                }
            }
        }
        private void BtnLogoDelete_Click(object sender, EventArgs e)
        {
            try
            {
                FileUtils.Delete(Paths.Base + @"PCL\Logo.png");
                this.RadioLogoType1.SetChecked(true, true);
                ModMain.Hint("标题栏图片已清空！", ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "清空标题栏图片失败", LogBehavior.Alert);
            }
        }

        // 主题
        private void LabLauncherTheme5Unlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            this.RadioLauncherTheme5Gray.Opacity -= 0.7d;
            this.RadioLauncherTheme5.Opacity += 0.7d;
            ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.RadioLauncherTheme5Gray, 1d, (int)Math.Round(1000d * ModAnimation.AniSpeed), (int)Math.Round(500d * ModAnimation.AniSpeed), new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(this.RadioLauncherTheme5, (double)-1, (int)Math.Round(1000d * ModAnimation.AniSpeed), (int)Math.Round(500d * ModAnimation.AniSpeed), new ModAnimation.AniEaseInFluent()) }, "ThemeUnlock");
            if (this.RadioLauncherTheme5Gray.Opacity < 0.02d)
            {
                ModSecret.ThemeUnlock(5, UnlockHint: "隐藏主题 玄素黑 已解锁！");
                ModAnimation.AniStop("ThemeUnlock");
                this.RadioLauncherTheme5.Checked = true;
            }
        }
        private void LabLauncherTheme11Click_MouseLeftButtonUp()
        {
            if (this.LabLauncherTheme11Click.Visibility == Visibility.Collapsed || (this.LabLauncherTheme11Click.ToolTip ?? "").ToString().Contains("点击"))
            {
                if (ModMain.MyMsgBox("1. 不爬取或攻击相关服务或网站，不盗取相关账号，没有谜题可以或需要以此来解决。" + Constants.vbCrLf + "2. 不得篡改或损毁相关公开信息，请尽量让它们保持原状。" + Constants.vbCrLf + "3. 在你感到迷茫的时候，看看回声洞可能会给你带来惊喜。" + Constants.vbCrLf + Constants.vbCrLf + "若违规，可能会被从任意相关群中踢出！", "解密游戏的基本规则", "我知道了", "恕我拒绝") == 1)
                {
                    ModMain.MyMsgBox("你需要用自己的智慧来找到下一步的线索……" + Constants.vbCrLf + @"初始线索：gnp.dorC61\60\20\0202\moc.x1xa.2s\\:sp" + "T".Lower() + "th", "解密游戏"); // 防止触发病毒检测规则
                }
            }
        }
        private void LabLauncherTheme8Copy_MouseRightButtonUp()
        {
            ModBase.OpenWebsite("https://meloong.com/afd/a/LTCat");
        }
        private void LabLauncherTheme9Copy_MouseRightButtonUp()
        {
            PageOtherLeft.TryFeedback();
        }

        // 主题自定义
        private void RadioLauncherTheme14_Change(object sender, ModBase.RouteEventArgs e)
        {
            if (this.RadioLauncherTheme14.Checked)
            {
                if (this.LabLauncherHue.Visibility == Visibility.Visible)
                    return;
                this.LabLauncherHue.Visibility = Visibility.Visible;
                this.SliderLauncherHue.Visibility = Visibility.Visible;
                this.LabLauncherSat.Visibility = Visibility.Visible;
                this.SliderLauncherSat.Visibility = Visibility.Visible;
                this.LabLauncherDelta.Visibility = Visibility.Visible;
                this.SliderLauncherDelta.Visibility = Visibility.Visible;
                this.LabLauncherLight.Visibility = Visibility.Visible;
                this.SliderLauncherLight.Visibility = Visibility.Visible;
            }
            else
            {
                if (this.LabLauncherHue.Visibility == Visibility.Collapsed)
                    return;
                this.LabLauncherHue.Visibility = Visibility.Collapsed;
                this.SliderLauncherHue.Visibility = Visibility.Collapsed;
                this.LabLauncherSat.Visibility = Visibility.Collapsed;
                this.SliderLauncherSat.Visibility = Visibility.Collapsed;
                this.LabLauncherDelta.Visibility = Visibility.Collapsed;
                this.SliderLauncherDelta.Visibility = Visibility.Collapsed;
                this.LabLauncherLight.Visibility = Visibility.Collapsed;
                this.SliderLauncherLight.Visibility = Visibility.Collapsed;
            }
            this.CardLauncher.TriggerForceResize();
        }
        private void HSL_Change()
        {
            if (ModAnimation.AniControlEnabled != 0 || this.SliderLauncherSat is null || !this.SliderLauncherSat.IsLoaded)
                return;
            ModSecret.ThemeRefresh();
        }

        // 滑动条
        private void SliderLoad()
        {
            this.SliderLauncherTransparent.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(Math.Round(Conversions.ToDouble(Operators.AddObject(40, Operators.MultiplyObject(v, 0.1d)))), "%"));
            this.SliderLauncherHue.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(v, "°"));
            this.SliderLauncherSat.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(v, "%"));
            this.SliderLauncherDelta.GetHintText = new Func<int, string>((Value) => { if (Value > 90) { return "+" + (Value - 90); } else if (Value == 90) { return 0.ToString(); } else { return (Value - 90).ToString(); } });
            this.SliderLauncherLight.GetHintText = new Func<int, string>((Value) => { if (Value > 20) { return "+" + (Value - 20); } else if (Value == 20) { return 0.ToString(); } else { return (Value - 20).ToString(); } });
        }

    }
}
