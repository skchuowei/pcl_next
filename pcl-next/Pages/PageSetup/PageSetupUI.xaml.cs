using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class PageSetupUI
    {

        // 非重复加载部分
        private bool _PageSetupUI_Loaded_Reloaded = false;

        public PageSetupUI()
        {
            InitializeComponent();
            this.BtnBackgroundOpen.Click += (p1, p2) => BtnUIBgOpen_Click((object)p1, (EventArgs)p2);
            this.BtnBackgroundRefresh.Click += (p1, p2) => BtnBackgroundRefresh_Click((object)p1, (EventArgs)p2);
            this.BtnBackgroundClear.Click += (p1, p2) => BtnBackgroundClear_Click((object)p1, (EventArgs)p2);
            this.BtnLogoChange.Click += (p1, p2) => BtnLogoChange_Click((object)p1, (EventArgs)p2);
            this.RadioLogoType3.PreviewCheck += (p1, p2) => RadioLogoType3_Check((object)p1, (ModBase.RouteEventArgs)p2);
            this.BtnLogoDelete.Click += (p1, p2) => BtnLogoDelete_Click((object)p1, (EventArgs)p2);
            this.BtnMusicOpen.Click += (p1, p2) => BtnMusicOpen_Click((object)p1, (EventArgs)p2);
            this.BtnMusicRefresh.Click += (p1, p2) => BtnMusicRefresh_Click((object)p1, (EventArgs)p2);
            this.BtnMusicClear.Click += (p1, p2) => BtnMusicClear_Click((object)p1, (EventArgs)p2);
            this.CheckMusicStart.Change += (p1, p2) => CheckMusicStart_Change();
            this.CheckMusicStop.Change += (p1, p2) => CheckMusicStop_Change();
            this.BtnCustomFile.Click += (p1, p2) => BtnCustomFile_Click((object)p1, (EventArgs)p2);
            this.BtnCustomRefresh.Click += (p1, p2) => BtnCustomRefresh_Click();
            this.BtnCustomTutorial.Click += (p1, p2) => BtnCustomTutorial_Click((object)p1, (EventArgs)p2);
            this.BtnCustomFolder.Click += (p1, p2) => BtnCustomFolder_Click((object)p1, (MouseButtonEventArgs)p2);
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
            this.CheckHiddenPageSetup.Change += (p1, p2) => HiddenSetupMain();
            this.CheckHiddenSetupLaunch.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenSetupSystem.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenSetupLink.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenSetupUI.Change += (p1, p2) => HiddenSetupSub();
            this.CheckHiddenPageOther.Change += (p1, p2) => HiddenOtherMain();
            this.CheckHiddenOtherHelp.Change += (p1, p2) => HiddenOtherSub((object)p1, (bool)p2);
            this.CheckHiddenOtherAbout.Change += (p1, p2) => HiddenOtherSub((object)p1, (bool)p2);
            this.CheckHiddenOtherTest.Change += (p1, p2) => HiddenOtherSub((object)p1, (bool)p2);
            this.CheckHiddenOtherFeedback.Change += (p1, p2) => HiddenOtherNet((object)p1, (bool)p2);
            this.CheckHiddenOtherVote.Change += (p1, p2) => HiddenOtherNet((object)p1, (bool)p2);
            this.CheckHiddenFunctionHidden.Change += (p1, p2) => HiddenHint((object)p1, (bool)p2);
            this.CheckHiddenPageSetup.Change += (p1, p2) => HiddenHint((object)p1, (bool)p2);
            this.CheckHiddenSetupUI.Change += (p1, p2) => HiddenHint((object)p1, (bool)p2);
            this.Loaded += PageSetupUI_Loaded;
            this.Loaded += (_, __) => HiddenRefresh();
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
                BackgroundRefresh(false, false);

                // 标题栏
                this.CheckLogoLeft.Visibility = this.RadioLogoType0.Checked ? Visibility.Visible : Visibility.Collapsed;
                this.PanLogoText.Visibility = this.RadioLogoType2.Checked ? Visibility.Visible : Visibility.Collapsed;
                this.PanLogoChange.Visibility = this.RadioLogoType3.Checked ? Visibility.Visible : Visibility.Collapsed;

                // 背景音乐
                MusicRefreshUI();

                // 主页
                OnMainPageTypeChanged();
            }
            catch (NullReferenceException ex)
            {
                Logger.Error(ex, "个性化设置项存在异常，已被自动重置", LogBehavior.Alert);
                Reset();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重载个性化设置时出错");
            }
        }
        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化个性化设置！");
                ModMain.Hint("已初始化个性化设置", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化个性化设置失败", LogBehavior.Alert);
            }
            Refresh();
        }

        // 背景图片
        private void BtnUIBgOpen_Click(object sender, EventArgs e)
        {
            ModBase.OpenExplorer(Paths.Base + @"PCL\Pictures\");
        }
        private void BtnBackgroundRefresh_Click(object sender, EventArgs e)
        {
            BackgroundRefresh(true, true);
        }
        public void BackgroundRefreshUI(bool Show, int Count)
        {
            if (this.PanBackgroundOpacity == null)
                return;
            if (Show)
            {
                this.PanBackgroundOpacity.Visibility = Visibility.Visible;
                this.PanBackgroundBlur.Visibility = Visibility.Visible;
                this.PanBackgroundSuit.Visibility = Visibility.Visible;
                this.BtnBackgroundClear.Visibility = Visibility.Visible;
                this.CardBackground.Title = "背景图片（" + Count + " 张）";
            }
            else
            {
                this.PanBackgroundOpacity.Visibility = Visibility.Collapsed;
                this.PanBackgroundBlur.Visibility = Visibility.Collapsed;
                this.PanBackgroundSuit.Visibility = Visibility.Collapsed;
                this.BtnBackgroundClear.Visibility = Visibility.Collapsed;
                this.CardBackground.Title = "背景图片";
            }
            this.CardBackground.TriggerForceResize();
        }
        private void BtnBackgroundClear_Click(object sender, EventArgs e)
        {
            if (ModMain.MyMsgBox("即将删除背景图片文件夹中的所有文件。" + Constants.vbCrLf + "此操作不可撤销，是否确定？", "警告", Button2: "取消", IsWarn: true) == 1)
            {
                DirectoryUtils.Delete(Paths.Base + @"PCL\Pictures");
                BackgroundRefresh(false, true);
                ModMain.Hint("背景图片已清空！", ModMain.HintType.Green);
            }
        }
        /// <summary>
    /// 刷新背景图片及设置页 UI。
    /// </summary>
    /// <param name="IsHint">是否显示刷新提示。</param>
    /// <param name="Refresh">是否刷新图片显示。</param>
        public static void BackgroundRefresh(bool IsHint, bool Refresh)
        {
            try
            {

                // 获取可用的图片文件
                DirectoryUtils.Create(Paths.Base + @"PCL\Pictures\");
                var Pic = new List<string>();
                foreach (var File in DirectoryUtils.EnumerateFiles(Paths.Base + @"PCL\Pictures\", true))
                {
                    string Extension = PathUtils.GetExtension(File);
                    if (Extension != "ini" && Extension != "db")
                        Pic.Add(File); // 文件夹可能会被加入 .ini 和 thumbs.db
                }
                // 加载
                if (!Pic.Any())
                {
                    if (Refresh)
                    {
                        if (ModMain.FrmMain.ImgBack.Visibility == Visibility.Collapsed)
                        {
                            if (IsHint)
                                ModMain.Hint("未检测到可用背景图片！", ModMain.HintType.Red);
                        }
                        else
                        {
                            ModMain.FrmMain.ImgBack.Visibility = Visibility.Collapsed;
                            if (IsHint)
                                ModMain.Hint("背景图片已清除！", ModMain.HintType.Green);
                        }
                    }
                    if (!(ModMain.FrmSetupUI == null))
                        ModMain.FrmSetupUI.BackgroundRefreshUI(false, 0);
                }
                else
                {
                    if (Refresh)
                    {
                        string Address = ModBase.RandomOne(Pic);
                        try
                        {
                            Logger.Info($"加载背景图片：{Address}");
                            ModMain.FrmMain.ImgBack.Background = new MyBitmap(Address);
                            ModMain.FrmMain.ImgBack.Visibility = Visibility.Visible;
                            ModMain.FrmMain.UpdateBackgroundAndTitleBar();
                            if (IsHint)
                                ModMain.Hint("背景图片已刷新：" + PathUtils.GetLastPart(Address), ModMain.HintType.Green, false);
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message.Contains("参数无效"))
                            {
                                Logger.Error($"刷新背景图片失败，该图片文件可能并非标准格式。{Constants.vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{Constants.vbCrLf}文件：{Address}", LogBehavior.Alert);
                            }
                            else
                            {
                                Logger.Error(ex, $"刷新背景图片失败（{Address}）", LogBehavior.Alert);
                            }
                        }
                    }
                    if (!(ModMain.FrmSetupUI == null))
                        ModMain.FrmSetupUI.BackgroundRefreshUI(true, Pic.Count);
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "刷新背景图片时出现未知错误");
            }
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

        // 背景音乐
        private void BtnMusicOpen_Click(object sender, EventArgs e)
        {
            ModBase.OpenExplorer(Paths.Base + @"PCL\Musics\");
        }
        private void BtnMusicRefresh_Click(object sender, EventArgs e)
        {
            ModMusic.MusicRefreshPlay(true);
        }
        public void MusicRefreshUI()
        {
            if (this.PanBackgroundOpacity is null)
                return;
            if (ModMusic.MusicAllList.Any())
            {
                this.PanMusicVolume.Visibility = Visibility.Visible;
                this.PanMusicDetail.Visibility = Visibility.Visible;
                this.BtnMusicClear.Visibility = Visibility.Visible;
                this.CardMusic.Title = "背景音乐（" + DirectoryUtils.EnumerateFiles(Paths.Base + @"PCL\Musics\", true).Count(f => !new[] { "ini", "jpg", "txt", "cfg", "lrc", "db", "png" }.Contains(PathUtils.GetExtension(f))) + " 首）";
            }
            else
            {
                this.PanMusicVolume.Visibility = Visibility.Collapsed;
                this.PanMusicDetail.Visibility = Visibility.Collapsed;
                this.BtnMusicClear.Visibility = Visibility.Collapsed;
                this.CardMusic.Title = "背景音乐";
            }
            this.CardMusic.TriggerForceResize();
        }
        private void BtnMusicClear_Click(object sender, EventArgs e)
        {
            if (ModMain.MyMsgBox("即将删除背景音乐文件夹中的所有文件。" + Constants.vbCrLf + "此操作不可撤销，是否确定？", "警告", Button2: "取消", IsWarn: true) == 1)
            {
                ModBase.RunInThread(() =>
        {
            ModMain.Hint("正在删除背景音乐……");
            // 停止播放音乐
            ModMusic.MusicNAudio = null;
            ModMusic.MusicWaitingList = new List<string>();
            ModMusic.MusicAllList = new List<string>();
            Thread.Sleep(200);
            // 删除文件
            try
            {
                DirectoryUtils.Delete(Paths.Base + @"PCL\Musics");
                ModMain.Hint("背景音乐已删除！", ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "删除背景音乐失败", LogBehavior.Alert);
            }
            try
            {
                DirectoryUtils.Create(Paths.Base + @"PCL\Musics\");
                ModBase.RunInUi(() => ModMusic.MusicRefreshPlay(false));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重建背景音乐文件夹失败", LogBehavior.Alert);
            }
        });
            }
        }
        private void CheckMusicStart_Change()
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (this.CheckMusicStart.Checked)
                this.CheckMusicStop.Checked = false;
        }
        private void CheckMusicStop_Change()
        {
            if (ModAnimation.AniControlEnabled != 0)
                return;
            if (this.CheckMusicStop.Checked)
                this.CheckMusicStart.Checked = false;
        }

        // 主页
        private void BtnCustomFile_Click(object sender, EventArgs e)
        {
            try
            {
                if (FileUtils.Exists(Paths.Base + @"PCL\Custom.xaml"))
                {
                    if (ModMain.MyMsgBox("当前已存在布局文件，继续生成教学文件将会覆盖现有布局文件！", "覆盖确认", "继续", "取消", IsWarn: true) == 2)
                        return;
                }
                ModBase.ExtractResources(Paths.Base + @"PCL\Custom.xaml", "Custom");
                ModMain.Hint("教学文件已生成！", ModMain.HintType.Green);
                ModBase.OpenExplorer(Paths.Base + @"PCL\Custom.xaml");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "生成教学文件失败");
            }
        }
        private void BtnCustomRefresh_Click()
        {
            ModMain.FrmLaunchRight.ForceRefresh();
            ModMain.Hint("已刷新主页！", ModMain.HintType.Green);
        }
        private void BtnCustomTutorial_Click(object sender, EventArgs e)
        {
            ModMain.MyMsgBox("1. 点击 生成教学文件 按钮，这会在 PCL 文件夹下生成 Custom.xaml 布局文件。" + Constants.vbCrLf + "2. 使用记事本等工具打开这个文件并进行修改，修改完记得保存。" + Constants.vbCrLf + "3. 点击 刷新主页 按钮，查看主页现在长啥样了。" + Constants.vbCrLf + Constants.vbCrLf + "你可以在生成教学文件后直接刷新主页，对照着进行修改，更有助于理解。" + Constants.vbCrLf + "直接将主页文件拖进 PCL Next 窗口也可以快捷加载。", "主页自定义教程");
        }
        public static void OnMainPageTypeChanged()
        {
            if (ModMain.FrmSetupUI is null)
                return;
            switch (Settings.Get<int>("UiCustomType"))
            {
                case 0: // 无
                    {
                        ModMain.FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.HintCustom.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.HintCustomWarn.Visibility = Visibility.Collapsed;
                        break;
                    }
                case 1: // 本地
                    {
                        ModMain.FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.PanCustomLocal.Visibility = Visibility.Visible;
                        ModMain.FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.HintCustom.Visibility = Visibility.Visible;
                        ModMain.FrmSetupUI.HintCustomWarn.Visibility = Settings.Get<bool>("HintCustomWarn") ? Visibility.Collapsed : Visibility.Visible;
                        ModMain.FrmSetupUI.HintCustom.Text = $"从 PCL 文件夹下的 Custom.xaml 读取主页内容。{Constants.vbCrLf}你可以手动编辑该文件，向主页添加文本、图片、常用网站、快捷启动等功能。";
                        CustomEventService.SetEventType(ModMain.FrmSetupUI.HintCustom, CustomEvent.EventType.None);
                        break;
                    }
                case 2: // 联网
                    {
                        ModMain.FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.PanCustomNet.Visibility = Visibility.Visible;
                        ModMain.FrmSetupUI.HintCustom.Visibility = Visibility.Visible;
                        ModMain.FrmSetupUI.HintCustomWarn.Visibility = Settings.Get<bool>("HintCustomWarn") ? Visibility.Collapsed : Visibility.Visible;
                        ModMain.FrmSetupUI.HintCustom.Text = $"从指定网址联网获取主页内容。服主也可以用于动态更新服务器公告。{Constants.vbCrLf}如果你制作了稳定运行的联网主页，可以点击这条提示投稿，若合格即可加入预设！";
                        CustomEventService.SetEventType(ModMain.FrmSetupUI.HintCustom, CustomEvent.EventType.打开网页);
                        CustomEventService.SetEventData(ModMain.FrmSetupUI.HintCustom, "https://github.com/Meloong-Git/PCL/discussions/2528");
                        break;
                    }
                case 3: // 预设
                    {
                        ModMain.FrmSetupUI.PanCustomPreset.Visibility = Visibility.Visible;
                        ModMain.FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.HintCustom.Visibility = Visibility.Collapsed;
                        ModMain.FrmSetupUI.HintCustomWarn.Visibility = Visibility.Collapsed;
                        break;
                    }
            }
            ModMain.FrmSetupUI.CardCustom.TriggerForceResize();
        }
        private void BtnCustomFolder_Click(object sender, MouseButtonEventArgs e)
        {
            string CustomPath = Paths.Base + @"PCL\Custom.xaml";
            if (FileUtils.Exists(CustomPath))
            {
                ModBase.OpenExplorer(CustomPath);
            }
            else
            {
                ModBase.OpenExplorer(Paths.Base + @"PCL\");
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

        #region 功能隐藏

        private static bool _HiddenForceShow = false;
        /// <summary>
    /// 是否强制显示被禁用的功能。
    /// </summary>
        public static bool HiddenForceShow
        {
            get
            {
                return _HiddenForceShow;
            }
            set
            {
                _HiddenForceShow = value;
                HiddenRefresh();
            }
        }

        /// <summary>
    /// 更新功能隐藏带来的显示变化。
    /// </summary>
        public static void HiddenRefresh()
        {
            if (ModMain.FrmMain.PanTitleSelect is null || !ModMain.FrmMain.PanTitleSelect.IsLoaded)
                return;
            try
            {
                // 顶部栏
                if (!HiddenForceShow && Settings.Get<bool>("UiHiddenPageDownload") && Settings.Get<bool>("UiHiddenPageLink") && Settings.Get<bool>("UiHiddenPageSetup") && Settings.Get<bool>("UiHiddenPageOther"))
                {
                    // 顶部栏已被全部隐藏
                    ModMain.FrmMain.PanTitleSelect.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // 顶部栏未被全部隐藏
                    ModMain.FrmMain.PanTitleSelect.Visibility = Visibility.Visible;
                    ModMain.FrmMain.BtnTitleSelect1.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenPageDownload") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmMain.BtnTitleSelect2.Visibility = Visibility.Collapsed; // If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageLink"), Visibility.Collapsed, Visibility.Visible)
                    ModMain.FrmMain.BtnTitleSelect3.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenPageSetup") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmMain.BtnTitleSelect4.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenPageOther") ? Visibility.Collapsed : Visibility.Visible;
                }
                // 功能
                ModMain.FrmLaunchLeft.RefreshButtonsUI();
                if (ModMain.FrmSetupUI is not null)
                {
                    ModMain.FrmSetupUI.CardSwitch.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenFunctionHidden") ? Visibility.Collapsed : Visibility.Visible;
                }
                // 版本设置子页面
                if (ModMain.FrmInstanceLeft is not null)
                {
                    ModMain.FrmInstanceLeft.ItemModify.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenFunctionInstanceModify") ? Visibility.Collapsed : Visibility.Visible;
                }
                // 设置子页面
                if (ModMain.FrmSetupLeft is not null)
                {
                    ModMain.FrmSetupLeft.ItemLaunch.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenSetupLaunch") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmSetupLeft.ItemUI.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenSetupUi") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmSetupLeft.ItemLink.Visibility = Visibility.Collapsed; // If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLink"), Visibility.Collapsed, Visibility.Visible)
                    ModMain.FrmSetupLeft.ItemSystem.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenSetupSystem") ? Visibility.Collapsed : Visibility.Visible;
                    // 隐藏左边选择卡
                    int AvaliableCount = 0;
                    if (!Settings.Get<bool>("UiHiddenSetupLaunch"))
                        AvaliableCount += 1;
                    if (!Settings.Get<bool>("UiHiddenSetupUi"))
                        AvaliableCount += 1;
                    // If Not Settings.Get(Of Boolean)("UiHiddenSetupLink") Then AvaliableCount += 1
                    if (!Settings.Get<bool>("UiHiddenSetupSystem"))
                        AvaliableCount += 1;
                    ModMain.FrmSetupLeft.PanItem.Visibility = AvaliableCount < 2 && !HiddenForceShow ? Visibility.Collapsed : Visibility.Visible;
                }
                // 更多子页面
                int OtherAvaliableCount = 0;
                if (!Settings.Get<bool>("UiHiddenOtherHelp"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherAbout"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherTest"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherFeedback"))
                    OtherAvaliableCount += 1;
                if (!Settings.Get<bool>("UiHiddenOtherVote"))
                    OtherAvaliableCount += 1;
                if (ModMain.FrmOtherLeft is not null)
                {
                    ModMain.FrmOtherLeft.ItemHelp.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherHelp") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemFeedback.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherFeedback") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemVote.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherVote") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemAbout.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherAbout") ? Visibility.Collapsed : Visibility.Visible;
                    ModMain.FrmOtherLeft.ItemTest.Visibility = !HiddenForceShow && Settings.Get<bool>("UiHiddenOtherTest") ? Visibility.Collapsed : Visibility.Visible;
                    // 隐藏左边选择卡
                    ModMain.FrmOtherLeft.PanItem.Visibility = OtherAvaliableCount < 2 && !HiddenForceShow ? Visibility.Collapsed : Visibility.Visible;
                }
                if (OtherAvaliableCount == 1 && !HiddenForceShow)
                {
                    if (!Settings.Get<bool>("UiHiddenOtherHelp"))
                    {
                        ModMain.FrmMain.BtnTitleSelect4.Text = "帮助";
                    }
                    else if (!Settings.Get<bool>("UiHiddenOtherAbout"))
                    {
                        ModMain.FrmMain.BtnTitleSelect4.Text = "关于";
                    }
                    else
                    {
                        ModMain.FrmMain.BtnTitleSelect4.Text = "百宝箱";
                    }
                }
                else
                {
                    ModMain.FrmMain.BtnTitleSelect4.Text = "更多";
                }
                // 各个页面的入口
                if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSelect)
                    ModMain.FrmSelectRight.BtnEmptyDownload_Loaded();
                if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.Launch)
                    ModMain.FrmLaunchLeft.RefreshButtonsUI();
                if (ModMain.FrmMain.PageCurrent == (FormMain.PageStackData)FormMain.PageType.InstanceSetup && ModMain.FrmInstanceModDisabled is not null)
                    ModMain.FrmInstanceModDisabled.BtnDownload_Loaded();
                // 备注
                if (ModMain.FrmSetupUI is not null)
                    ModMain.FrmSetupUI.CardSwitch.Title = HiddenForceShow ? "功能隐藏（已暂时关闭，按 F12 以重新启用）" : "功能隐藏";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "刷新功能隐藏项目失败");
            }
        }

        // UI 协同改变
        private void HiddenSetupMain()
        {
            // 设置主页面
            if (this.CheckHiddenPageSetup.Checked)
            {
                // 开启
                this.CheckHiddenSetupLaunch.Checked = true;
                this.CheckHiddenSetupSystem.Checked = true;
                this.CheckHiddenSetupLink.Checked = true;
                this.CheckHiddenSetupUI.Checked = true;
            }
            // 关闭
            else if (Settings.Get<bool>("UiHiddenSetupLaunch") && Settings.Get<bool>("UiHiddenSetupUi") && Settings.Get<bool>("UiHiddenSetupSystem") && Settings.Get<bool>("UiHiddenSetupLink"))
            {
                this.CheckHiddenSetupLaunch.Checked = false;
                this.CheckHiddenSetupSystem.Checked = false;
                this.CheckHiddenSetupLink.Checked = false;
                this.CheckHiddenSetupUI.Checked = false;
            }
        }
        private void HiddenSetupSub()
        {
            // 设置子页面
            if (Settings.Get<bool>("UiHiddenSetupLaunch") && Settings.Get<bool>("UiHiddenSetupUi") && Settings.Get<bool>("UiHiddenSetupSystem") && Settings.Get<bool>("UiHiddenSetupLink"))
            {
                // 已被全部隐藏
                this.CheckHiddenPageSetup.Checked = true;
            }
            else
            {
                // 未被全部隐藏
                this.CheckHiddenPageSetup.Checked = false;
            }
        }
        private void HiddenOtherMain()
        {
            // 更多主页面
            if (this.CheckHiddenPageOther.Checked)
            {
                // 开启
                this.CheckHiddenOtherAbout.Checked = true;
                this.CheckHiddenOtherTest.Checked = true;
                this.CheckHiddenOtherFeedback.Checked = true;
                this.CheckHiddenOtherVote.Checked = true;
                this.CheckHiddenOtherHelp.Checked = true;
            }
            // 关闭
            else if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest") && Settings.Get<bool>("UiHiddenOtherVote") && Settings.Get<bool>("UiHiddenOtherFeedback"))
            {
                this.CheckHiddenOtherAbout.Checked = false;
                this.CheckHiddenOtherTest.Checked = false;
                this.CheckHiddenOtherFeedback.Checked = false;
                this.CheckHiddenOtherVote.Checked = false;
                this.CheckHiddenOtherHelp.Checked = false;
            }
        }
        private void HiddenOtherSub(object sender, bool user)
        {
            // 更多子页面（有具体内容的）
            if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest"))
            {
                // 已被全部隐藏
                this.CheckHiddenPageOther.Checked = true;
            }
            else
            {
                // 未被全部隐藏
                this.CheckHiddenPageOther.Checked = false;
            }
            // 修改无具体内容的项
            if (!user)
                return;
            if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest"))
            {
                this.CheckHiddenOtherFeedback.Checked = true;
                this.CheckHiddenOtherVote.Checked = true;
            }
        }
        private void HiddenOtherNet(object sender, bool user)
        {
            // 更多子页面（无具体内容的）
            if (!user)
                return;
            if (Settings.Get<bool>("UiHiddenOtherHelp") && Settings.Get<bool>("UiHiddenOtherAbout") && Settings.Get<bool>("UiHiddenOtherTest") && (!Settings.Get<bool>("UiHiddenOtherFeedback") || !Settings.Get<bool>("UiHiddenOtherVote")))
            {
                this.CheckHiddenOtherAbout.Checked = false;
                this.CheckHiddenOtherTest.Checked = false;
                this.CheckHiddenOtherHelp.Checked = false;
            }
        }

        // 警告提示
        private void HiddenHint(object sender, bool user)
        {
            if (Conversions.ToBoolean(ModAnimation.AniControlEnabled == 0 && ((dynamic)sender).Checked))
                ModMain.Hint("按 F12 即可暂时关闭功能隐藏设置。千万别忘了，要不然设置就改不回来了……");
        }

        #endregion

        // 滑动条
        private void SliderLoad()
        {
            this.SliderMusicVolume.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(Math.Ceiling((dynamic)(Operators.MultiplyObject(v, 0.1d))), "%"));
            // 对应 PageSetupUI.xaml.vb:599「Math.Round(40 + v * 0.1) & "%"」：VB 里 Math.Round 晚绑定到 Double 重载，
            // C# 侧 AddObject 返回 object，会误绑到 decimal 重载，故先 ToDouble 还原 VB 行为。
            this.SliderLauncherTransparent.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(Math.Round(Conversions.ToDouble(Operators.AddObject(40, Operators.MultiplyObject(v, 0.1d)))), "%"));
            this.SliderLauncherHue.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(v, "°"));
            this.SliderLauncherSat.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(v, "%"));
            this.SliderLauncherDelta.GetHintText = new Func<int, string>((Value) => { if (Value > 90) { return "+" + (Value - 90); } else if (Value == 90) { return 0.ToString(); } else { return (Value - 90).ToString(); } });
            this.SliderLauncherLight.GetHintText = new Func<int, string>((Value) => { if (Value > 20) { return "+" + (Value - 20); } else if (Value == 20) { return 0.ToString(); } else { return (Value - 20).ToString(); } });
            this.SliderBackgroundOpacity.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(Math.Round((dynamic)(Operators.MultiplyObject(v, 0.1d))), "%"));
            this.SliderBackgroundBlur.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(v, " 像素"));
        }

    }
}