using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public partial class PageSetupBackdrop
    {

        // 非重复加载部分
        private bool _PageSetupBackdrop_Loaded_Reloaded = false;

        public PageSetupBackdrop()
        {
            InitializeComponent();
            this.BtnBackgroundOpen.Click += (p1, p2) => BtnUIBgOpen_Click((object)p1, (EventArgs)p2);
            this.BtnBackgroundRefresh.Click += (p1, p2) => BtnBackgroundRefresh_Click((object)p1, (EventArgs)p2);
            this.BtnBackgroundClear.Click += (p1, p2) => BtnBackgroundClear_Click((object)p1, (EventArgs)p2);
            this.BtnMusicOpen.Click += (p1, p2) => BtnMusicOpen_Click((object)p1, (EventArgs)p2);
            this.BtnMusicRefresh.Click += (p1, p2) => BtnMusicRefresh_Click((object)p1, (EventArgs)p2);
            this.BtnMusicClear.Click += (p1, p2) => BtnMusicClear_Click((object)p1, (EventArgs)p2);
            this.CheckMusicStart.Change += (p1, p2) => CheckMusicStart_Change();
            this.CheckMusicStop.Change += (p1, p2) => CheckMusicStop_Change();
            this.Loaded += PageSetupBackdrop_Loaded;
        }

        private void PageSetupBackdrop_Loaded(object sender, RoutedEventArgs e)
        {
            this.PanBack.ScrollToHome();
            ModAnimation.AniControlEnabled += 1;
            Refresh();
            ModAnimation.AniControlEnabled -= 1;
            if (_PageSetupBackdrop_Loaded_Reloaded)
                return;
            _PageSetupBackdrop_Loaded_Reloaded = true;
            SliderLoad();
        }

        public void Refresh()
        {
            try
            {
                SettingService.RefreshSettings(this);
                // 背景图片（按检测结果同步卡片与控件可见性）
                BackgroundRefresh(false, false);
                // 背景音乐
                MusicRefreshUI();
            }
            catch (NullReferenceException ex)
            {
                Logger.Error(ex, "背景与音乐设置项存在异常，已被自动重置", LogBehavior.Alert);
                Reset();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "重载背景与音乐设置时出错");
            }
        }

        public void Reset()
        {
            try
            {
                SettingService.ResetSettings(this);
                Logger.Info("已初始化背景与音乐设置！");
                ModMain.Hint("已初始化背景与音乐设置", ModMain.HintType.Green, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化背景与音乐设置失败", LogBehavior.Alert);
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
                this.PanBackgroundStyle.Visibility = Visibility.Visible;
                this.PanBackgroundSuit.Visibility = Visibility.Visible;
                this.BtnBackgroundClear.Visibility = Visibility.Visible;
                this.CardBackground.Title = "背景图片（" + Count + " 张）";
            }
            else
            {
                this.PanBackgroundOpacity.Visibility = Visibility.Collapsed;
                this.PanBackgroundBlur.Visibility = Visibility.Collapsed;
                this.PanBackgroundStyle.Visibility = Visibility.Collapsed;
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
                    if (!(ModMain.FrmSetupBackdrop == null))
                        ModMain.FrmSetupBackdrop.BackgroundRefreshUI(false, 0);
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
                    if (!(ModMain.FrmSetupBackdrop == null))
                        ModMain.FrmSetupBackdrop.BackgroundRefreshUI(true, Pic.Count);
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "刷新背景图片时出现未知错误");
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
            if (this.PanMusicVolume is null || this.PanMusicDetail is null || this.BtnMusicClear is null || this.CardMusic is null)
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

        // 滑动条
        private void SliderLoad()
        {
            this.SliderMusicVolume.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(Math.Ceiling((dynamic)(Operators.MultiplyObject(v, 0.1d))), "%"));
            this.SliderBackgroundOpacity.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(Math.Round((dynamic)(Operators.MultiplyObject(v, 0.1d))), "%"));
            this.SliderBackgroundBlur.GetHintText = new Func<object, object>(v => Operators.ConcatenateObject(v, " 像素"));
        }

    }
}
