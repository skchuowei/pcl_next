using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NAudio.Wave;

namespace PCL
{

    public static class ModMusic
    {

        #region 播放列表

        /// <summary>
    /// 接下来要播放的音乐文件路径。未初始化时为 Nothing。
    /// </summary>
        public static List<string> MusicWaitingList = null;
        /// <summary>
    /// 全部音乐文件路径。未初始化时为 Nothing。
    /// </summary>
        public static List<string> MusicAllList = null;
        /// <summary>
    /// 初始化音乐播放列表。
    /// </summary>
    /// <param name="ForceReload">强制全部重新载入列表。</param>
    /// <param name="PreventFirst">在重载列表时避免让某项成为第一项。</param>
        private static void MusicListInit(bool ForceReload, string PreventFirst = null)
        {
            if (ForceReload)
                MusicAllList = null;
            try
            {
                // 初始化全部可用音乐列表
                if (MusicAllList is null)
                {
                    MusicAllList = new List<string>();
                    DirectoryUtils.Create(Paths.Base + @"PCL\Musics\");
                    foreach (var File in DirectoryUtils.EnumerateFiles(Paths.Base + @"PCL\Musics\", true))
                    {
                        // 文件夹可能会被加入 .ini 文件夹配置文件、一些乱七八糟的 .jpg 文件啥的
                        if (new[] { "ini", "jpg", "txt", "cfg", "lrc", "db", "png" }.Contains(PathUtils.GetExtension(File)))
                            continue;
                        MusicAllList.Add(File);
                    }
                }
                // 打乱顺序播放
                MusicWaitingList = Settings.Get<bool>("UiMusicRandom") ? new List<string>(MusicAllList).Shuffle().ToList() : new List<string>(MusicAllList);
                if (PreventFirst is not null && (MusicWaitingList.FirstOrDefault() ?? "") == (PreventFirst ?? ""))
                {
                    // 若需要避免成为第一项的为第一项，则将它放在最后
                    MusicWaitingList.RemoveAt(0);
                    MusicWaitingList.Add(PreventFirst);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化音乐列表失败");
            }
        }
        /// <summary>
    /// 获取下一首播放的音乐路径并将其从列表中移除。
    /// 如果没有，可能会返回 Nothing。
    /// </summary>
        private static string DequeueNextMusicAddress()
        {
            string Result;
            // 初始化，确保存在音乐
            if (MusicAllList is null || !MusicAllList.Any() || !MusicWaitingList.Any())
                MusicListInit(false);
            // 出列下一个音乐，如果出列结束则生成新列表
            if (MusicWaitingList.Any())
            {
                Result = MusicWaitingList[0];
                MusicWaitingList.RemoveAt(0);
            }
            else
            {
                Result = null;
            }
            if (!MusicWaitingList.Any())
                MusicListInit(false, Result);
            return Result;
        }

        #endregion

        #region UI 控制

        /// <summary>
    /// 刷新背景音乐按钮 UI 与设置页 UI。
    /// </summary>
        private static void MusicRefreshUI()
        {

            // 无背景音乐
            // 有背景音乐

            ModBase.RunInUi(() => { try { if (!MusicAllList.Any()) { ModMain.FrmMain.BtnExtraMusic.Show = false; } else { ModMain.FrmMain.BtnExtraMusic.Show = true; string ToolTipText; if (MusicState == MusicStates.Pause) { ModMain.FrmMain.BtnExtraMusic.Logo = ModBase.Logo.IconPlay; ModMain.FrmMain.BtnExtraMusic.LogoScale = 0.8d; ToolTipText = "已暂停：" + PathUtils.GetFileNameWithoutExtension(MusicCurrent); if (MusicAllList.Count > 1) { ToolTipText += Constants.vbCrLf + "左键恢复播放，右键播放下一曲。"; } else { ToolTipText += Constants.vbCrLf + "左键恢复播放，右键重新从头播放。"; } } else { ModMain.FrmMain.BtnExtraMusic.Logo = ModBase.Logo.IconMusic; ModMain.FrmMain.BtnExtraMusic.LogoScale = 1d; ToolTipText = "正在播放：" + PathUtils.GetFileNameWithoutExtension(MusicCurrent); if (MusicAllList.Count > 1) { ToolTipText += Constants.vbCrLf + "左键暂停，右键播放下一曲。"; } else { ToolTipText += Constants.vbCrLf + "左键暂停，右键重新从头播放。"; } } ModMain.FrmMain.BtnExtraMusic.ToolTip = ToolTipText; } if (ModMain.FrmSetupBackdrop is not null) ModMain.FrmSetupBackdrop.MusicRefreshUI(); } catch (Exception ex) { Logger.Error(ex, "刷新背景音乐 UI 失败"); } });
        }

        /// <summary>
    /// 让音乐在暂停、播放间切换，并显示提示文本。
    /// </summary>
        public static void MusicControlPause()
        {
            if (MusicNAudio is null)
            {
                ModMain.Hint("音乐播放尚未开始！", ModMain.HintType.Red);
            }
            else
            {
                switch (MusicState)
                {
                    case MusicStates.Pause:
                        {
                            MusicResume();
                            break;
                        }
                    case MusicStates.Play:
                        {
                            MusicPause();
                            break;
                        }

                    default:
                        {
                            Logger.Warn("音乐目前为停止状态，已强制尝试开始播放");
                            MusicRefreshPlay(false);
                            break;
                        }
                }
            }
        }

        /// <summary>
    /// 播放下一曲，并显示提示文本。
    /// </summary>
        public static void MusicControlNext()
        {
            if (MusicAllList.IsSingle())
            {
                MusicStartPlay(MusicCurrent);
                ModMain.Hint("重新播放：" + PathUtils.GetLastPart(MusicCurrent), ModMain.HintType.Green);
            }
            else
            {
                string Address = DequeueNextMusicAddress();
                if (Address is null)
                {
                    ModMain.Hint("没有可以播放的音乐！", ModMain.HintType.Red);
                }
                else
                {
                    MusicStartPlay(Address);
                    ModMain.Hint("正在播放：" + PathUtils.GetLastPart(Address), ModMain.HintType.Green);
                }
            }
            MusicRefreshUI();
        }

        #endregion

        #region 主状态控制

        /// <summary>
    /// 获取当前的音乐播放状态。
    /// </summary>
        public static MusicStates MusicState
        {
            get
            {
                if (MusicNAudio is null)
                    return MusicStates.Stop;
                switch (((dynamic)MusicNAudio).PlaybackState)
                {
                    case var @case when Operators.ConditionalCompareObjectEqual(@case, 0, false): // PlaybackState.Stopped
                        {
                            return MusicStates.Stop;
                        }
                    case var case1 when Operators.ConditionalCompareObjectEqual(case1, 2, false): // PlaybackState.Paused
                        {
                            return MusicStates.Pause;
                        }

                    default:
                        {
                            return MusicStates.Play;
                        }
                }
            }
        }
        public enum MusicStates
        {
            Stop,
            Play,
            Pause
        }

        // 重载与开始

        /// <summary>
    /// 重载播放列表并尝试开始播放背景音乐。
    /// </summary>
    /// <param name="ShowHint">是否显示刷新提示。</param>
        public static void MusicRefreshPlay(bool ShowHint, bool IsFirstLoad = false)
        {
            try
            {

                MusicListInit(true);
                if (!MusicAllList.Any())
                {
                    if (MusicNAudio is null)
                    {
                        if (ShowHint)
                            ModMain.Hint("未检测到可用的背景音乐！", ModMain.HintType.Red);
                    }
                    else
                    {
                        MusicNAudio = null;
                        if (ShowHint)
                            ModMain.Hint("背景音乐已清除！", ModMain.HintType.Green);
                    }
                }
                else
                {
                    string Address = DequeueNextMusicAddress();
                    if (Address is null)
                    {
                        if (ShowHint)
                            ModMain.Hint("没有可以播放的音乐！", ModMain.HintType.Red);
                    }
                    else
                    {
                        try
                        {
                            MusicStartPlay(Address, IsFirstLoad);
                            if (ShowHint)
                                ModMain.Hint("背景音乐已刷新：" + PathUtils.GetLastPart(Address), ModMain.HintType.Green, false);
                        }
                        catch
                        {
                        }
                    }
                }
                MusicRefreshUI();
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "刷新背景音乐播放失败");
            }
        }
        /// <summary>
    /// 开始播放音乐。
    /// </summary>
        private static void MusicStartPlay(string Address, bool IsFirstLoad = false)
        {
            if (Address is null)
                return;
            Logger.Info($"播放开始：{Address}");
            MusicCurrent = Address;
            ModBase.RunInNewThread(() => MusicLoop(IsFirstLoad), "Music", ThreadPriority.BelowNormal);
        }

        // 播放与暂停

        /// <summary>
    /// 暂停音乐播放，返回是否成功切换了状态。
    /// </summary>
        public static bool MusicPause()
        {
            if (MusicState == MusicStates.Play)
            {
                ModBase.RunInThread(() =>
        {
            Logger.Info("已暂停播放");
            MusicNAudio?.Pause();
            MusicRefreshUI();
        });
                return true;
            }
            else
            {
                Logger.Info($"无需暂停播放，当前状态为 {MusicState}");
                return false;
            }
        }
        /// <summary>
    /// 继续音乐播放，返回是否成功切换了状态。
    /// </summary>
        public static bool MusicResume()
        {
            if (MusicState == MusicStates.Play || !MusicAllList.Any())
            {
                Logger.Info($"无需继续播放，当前状态为 {MusicState}");
                return false;
            }
            else
            {
                ModBase.RunInThread(() =>
        {
            Logger.Info("已恢复播放");
            try
            {
                MusicNAudio?.Play(); // https://github.com/Meloong-Git/PCL/pull/5415#issuecomment-2751135223
            }
            catch
            {
                MusicNAudio?.Stop();
                MusicNAudio?.Play();
            }
            MusicRefreshUI();
        });
                return true;
            }
        }

        #endregion

        /// <summary>
    /// 当前正在播放的 WaveOutEvent。
    /// 如果为它赋了类别，则会没有必要地加载 NAudio.dll。
    /// </summary>
        public static dynamic MusicNAudio = null;
        /// <summary>
    /// 当前播放的音乐地址。
    /// </summary>
        private static string MusicCurrent = "";

        /// <summary>
    /// 在 MusicUuid 不变的前提下，持续播放某地址的音乐，且在播放结束后随机播放下一曲。
    /// </summary>
        private static void MusicLoop(bool IsFirstLoad = false)
        {
            WaveOut CurrentWave = null;
            WaveStream Reader = null;
            try
            {
                // 开始播放（NAudio 3.x 起 WaveOutEvent 更名为 WaveOut，CS0618 的官方迁移建议）
                CurrentWave = new WaveOut();
                MusicNAudio = CurrentWave;
                CurrentWave.DeviceNumber = -1;
                try
                {
                    Reader = new AudioFileReader(MusicCurrent);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "使用 AudioFileReader 加载音频文件失败，换用 MediaFoundationReader 重试");
                    Reader = new MediaFoundationReader(MusicCurrent);
                }
                CurrentWave.Init(Reader);
                CurrentWave.Play();
                // 第一次打开的暂停
                if (IsFirstLoad && !Settings.Get<bool>("UiMusicAuto"))
                    CurrentWave.Pause();
                MusicRefreshUI();
                // 停止条件：播放完毕或变化
                int PreviousVolume = 0;
                while (CurrentWave.Equals(MusicNAudio) && !(CurrentWave.PlaybackState == PlaybackState.Stopped))
                {
                    if (Settings.Get<int>("UiMusicVolume") != PreviousVolume)
                    {
                        // 更新音量
                        PreviousVolume = Settings.Get<int>("UiMusicVolume");
                        // 原 VB 在此用 #Disable Warning BC40008 屏蔽「已过时」提示（NAudio 的 Volume）。
                        // C# 侧该访问是 dynamic 调用，编译器不产生对应警告，故无需等价 #pragma。
                        CurrentWave.Volume = (float)(PreviousVolume / 1000d);
                    }
                    // 更新进度条
                    double Percent = Reader.CurrentTime.TotalMilliseconds / Reader.TotalTime.TotalMilliseconds;
                    ModBase.RunInUi(() => ModMain.FrmMain.BtnExtraMusic.Progress = Percent);
                    Thread.Sleep(100);
                }
                // 当前音乐已播放结束，继续下一曲
                if (CurrentWave.PlaybackState == PlaybackState.Stopped && MusicAllList.Any())
                    MusicStartPlay(DequeueNextMusicAddress());
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"播放音乐出现内部错误（{MusicCurrent}）");
                if (ex is NAudio.MmException && ex.Message.Contains("AlreadyAllocated"))
                {
                    ModMain.Hint("你的音频设备正被其他程序占用。请在关闭占用的程序后重启 PCL Next，才能恢复音乐播放功能！", ModMain.HintType.Red);
                    Thread.Sleep(1000000000);
                }
                if (ex is NAudio.MmException && (ex.Message.Contains("NoDriver") || ex.Message.Contains("BadDeviceId")))
                {
                    ModMain.Hint("由于音频设备变更，音乐播放功能在重启 PCL Next 后才能恢复！", ModMain.HintType.Red);
                    Thread.Sleep(1000000000);
                }
                if (!new[] { "wav", "mp3", "flac" }.Contains(PathUtils.GetExtension(MusicCurrent)) || ex.Message.Contains("0xC00D36C4")) // #5096：不支持给定的 URL 的字节流类型。 (异常来自 HRESULT:0xC00D36C4)
                {
                    ModMain.Hint("播放音乐失败（" + PathUtils.GetLastPart(MusicCurrent) + "）：PCL Next 可能不支持此音乐格式，请将格式转换为 .wav、.mp3 或 .flac 后再试", ModMain.HintType.Red);
                }
                else
                {
                    Logger.Error(ex, $"播放音乐失败（{PathUtils.GetLastPart(MusicCurrent)}）", LogBehavior.Toast);
                }
                // 将播放错误的音乐从列表中移除
                MusicAllList.Remove(MusicCurrent);
                MusicWaitingList.Remove(MusicCurrent);
                MusicRefreshUI();
                // 等待 2 秒后继续播放
                Thread.Sleep(2000);
                if (ex is FileNotFoundException)
                {
                    MusicRefreshPlay(true, IsFirstLoad);
                }
                else
                {
                    MusicStartPlay(DequeueNextMusicAddress(), IsFirstLoad);
                }
            }
            finally
            {
                if (CurrentWave is not null)
                    CurrentWave.Dispose();
                if (Reader is not null)
                    Reader.Dispose();
                MusicRefreshUI();
            }
        }

    }
}