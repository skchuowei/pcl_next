using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{

    public static partial class ModMain
    {

        #region 弹出提示

        /// <summary>
    /// 提示信息的种类。
    /// 该枚举在自定义事件中使用，是公开 API 的一部分。
    /// </summary>
        public enum HintType
        {
            Blue,
            Green,
            Red
        }
        private struct HintMessage
        {
            public string Text;
            public HintType Type;
            public bool Log;
        }

        /// <summary>
    /// 等待弹出的提示列表。以 {String, HintType, Log As Boolean} 形式存储为数组。
    /// </summary>
        private static ConcurrentQueue<HintMessage> HintWaiting = new ConcurrentQueue<HintMessage>();
        /// <summary>
    /// 在窗口左下角弹出提示文本。
    /// </summary>
        public static void Hint(string Text, HintType Type = HintType.Blue, bool Log = true)
        {
            HintWaiting.Enqueue(new HintMessage() { Text = Text ?? "", Type = Type, Log = Log });
        }

        private static void HintTick()
        {
            try
            {
                // Tag 存储了：{ 是否可以重用, Uuid }
                HintMessage CurrentHint = default;
                while (HintWaiting.TryDequeue(out CurrentHint))
                {
                    // '清除空提示
                    // If IsNothing(HintWaiting(0)) OrElse IsNothing(HintWaiting(0)(0)) Then
                    // HintWaiting.RemoveAt(0)
                    // Continue Do
                    // End If
                    // 去回车
                    CurrentHint.Text = CurrentHint.Text.ReplaceLineEndings(" ", mergeMultiple: true);
                    // 超量提示直接忽略
                    if (FrmMain.PanHint.Children.Count >= 20)
                        goto EndHint;
                    // 检查是否有重复提示
                    Border DoubleStack = null;
                    foreach (Border stack in FrmMain.PanHint.Children)
                    {
                        if (Conversions.ToBoolean(((dynamic)stack.Tag)[0] && (((TextBlock)stack.Child).Text ?? "") == (CurrentHint.Text ?? "")))
                            DoubleStack = stack;
                    }
                    // 获取渐变颜色
                    ModBase.MyColor TargetColor0, TargetColor1;
                    double Percent = 0.3d;
                    switch (CurrentHint.Type)
                    {
                        case HintType.Blue:
                            {
                                TargetColor0 = new ModBase.MyColor(215d, 37d, 155d, 252d);
                                TargetColor1 = new ModBase.MyColor(215d, 10d, 142d, 252d);
                                break;
                            }
                        case HintType.Green:
                            {
                                TargetColor0 = new ModBase.MyColor(215d, 33d, 177d, 33d);
                                TargetColor1 = new ModBase.MyColor(215d, 29d, 160d, 29d); // HintType.Red
                                break;
                            }

                        default:
                            {
                                TargetColor0 = new ModBase.MyColor(215d, 255d, 53d, 11d);
                                TargetColor1 = new ModBase.MyColor(215d, 255d, 43d, 0d);
                                break;
                            }
                    }
                    if (!(DoubleStack == null))
                    {
                        // 有重复提示，且该提示的进入动画已播放
                        if (!ModAnimation.AniIsRun(Conversions.ToString(Operators.ConcatenateObject("Hint Show ", ((dynamic)DoubleStack.Tag)[1]))))
                        {
                            ModAnimation.AniStop(Conversions.ToString(Operators.ConcatenateObject("Hint Hide ", ((dynamic)DoubleStack.Tag)[1])));
                            double Delay = (800 + CurrentHint.Text.Length.Clamp(5, 23) * 180) * ModAnimation.AniSpeed;
                            ModAnimation.AniStart(new[] {
                            ModAnimation.AaX(DoubleStack, -12 - DoubleStack.Margin.Left, 50, Ease: new ModAnimation.AniEaseOutFluent()),
                            ModAnimation.AaX(DoubleStack, -8, 50, 50, new ModAnimation.AniEaseInFluent()),
                            ModAnimation.AaX(DoubleStack, 8d, 50, 100, new ModAnimation.AniEaseOutFluent()),
                            ModAnimation.AaX(DoubleStack, -8, 50, 150, new ModAnimation.AniEaseInFluent()),
                            ModAnimation.AaDouble(i =>
                                {
                                         // 对应 ModMain.vb:79「Percent += i」：i 是 Object（动画回调传参），VB 的 += 走晚绑定加法后再折回 Double。
                                         Percent = Conversions.ToDouble(Operators.AddObject(Percent, i));
                                         LinearGradientBrush Gradient = (LinearGradientBrush)DoubleStack.Background;
                                         Gradient.GradientStops[0].Color = TargetColor0 * Percent + new ModBase.MyColor(255d, 255d, 255d) * (1d - Percent);
                                         Gradient.GradientStops[1].Color = TargetColor1 * Percent + new ModBase.MyColor(255d, 255d, 255d) * (1d - Percent);
                                     }, 0.7d, 250),
                            ModAnimation.AaX(DoubleStack, -50, 200, (int)Math.Round(Delay), new ModAnimation.AniEaseInFluent()),
                            ModAnimation.AaOpacity(DoubleStack, -1, 150, (int)Math.Round(Delay)),
                            ModAnimation.AaCode(() => ((dynamic)DoubleStack.Tag)[0] = false, (int)Math.Round(Delay)),
                            ModAnimation.AaHeight(DoubleStack, -26, 100, Ease: new ModAnimation.AniEaseOutFluent(), After: true),
                            ModAnimation.AaCode(() => FrmMain.PanHint.Children.Remove(DoubleStack), After: true)
                      }, Conversions.ToString(Operators.ConcatenateObject("Hint Hide ", ((dynamic)DoubleStack.Tag)[1])));
                        }
                    }
                    else
                    {
                        // 准备控件
                        var NewHintControl = new Border() { Tag = new object[]{ true, ModBase.GetUuid() }, Margin = new Thickness(-70, 0d, 20d, 0d), Opacity = 0d, Height = 0d, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(0d, 6d, 6d, 0d) };
                        NewHintControl.Background = new LinearGradientBrush(new GradientStopCollection(new List<GradientStop>() { new GradientStop(TargetColor0 * Percent + new ModBase.MyColor(255d, 255d, 255d) * (1d - Percent), 0d), new GradientStop(TargetColor1 * Percent + new ModBase.MyColor(255d, 255d, 255d) * (1d - Percent), 1d) }), 90d);
                        NewHintControl.Child = new TextBlock() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13d, Text = CurrentHint.Text, Foreground = new ModBase.MyColor(255d, 255d, 255d), Margin = new Thickness(33d, 5d, 8d, 5d) };
                        // AddHandler NewHintControl.MouseLeftButtonDown, AddressOf HideAllHint
                        FrmMain.PanHint.Children.Add(NewHintControl);
                        // 控件动画
                        var Animations = new List<ModAnimation.AniData>();
                        if (FrmMain.PanHint.Children.Count > 1)
                        {
                            // 已有提示
                            Animations.Add(ModAnimation.AaHeight(NewHintControl, 26d, 150, Ease: new ModAnimation.AniEaseOutFluent()));
                        }
                        else
                        {
                            // 是唯一提示
                            NewHintControl.Height = 26d;
                        }
                        // 开始动画
                        Animations.AddRange(new[] {
                        ModAnimation.AaX(NewHintControl, 30d, 400, Ease: new ModAnimation.AniEaseOutElastic(ModAnimation.AniEasePower.Weak)),
                        ModAnimation.AaX(NewHintControl, 20d, 200, Ease: new ModAnimation.AniEaseOutFluent()),
                        ModAnimation.AaOpacity(NewHintControl, 1d, 100),
                        ModAnimation.AaDouble(i =>
                            {
                                     // 对应 ModMain.vb:115「Percent += i」：同上，object 与 double 相加要经 AddObject 晚绑定。
                                     Percent = Conversions.ToDouble(Operators.AddObject(Percent, i));
                                     LinearGradientBrush Gradient = (LinearGradientBrush)NewHintControl.Background;
                                     Gradient.GradientStops[0].Color = TargetColor0 * Percent + new ModBase.MyColor(255d, 255d, 255d) * (1d - Percent);
                                     Gradient.GradientStops[1].Color = TargetColor1 * Percent + new ModBase.MyColor(255d, 255d, 255d) * (1d - Percent);
                                 }, 0.7d, 250, 100)
                    });
                        ModAnimation.AniStart(Animations, Conversions.ToString(Operators.ConcatenateObject("Hint Show ", ((dynamic)NewHintControl.Tag)[1])));
                        // 结束动画
                        double Delay = (800 + CurrentHint.Text.Length.Clamp(5, 23) * 180) * ModAnimation.AniSpeed;
                        ModAnimation.AniStart(new[] { ModAnimation.AaX(NewHintControl, -50, 200, (int)Math.Round(Delay), new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(NewHintControl, -1, 150, (int)Math.Round(Delay)), ModAnimation.AaCode(() => ((dynamic)NewHintControl.Tag)[0] = false, (int)Math.Round(Delay)), ModAnimation.AaHeight(NewHintControl, -26, 100, Ease: new ModAnimation.AniEaseOutFluent(), After: true), ModAnimation.AaCode(() => FrmMain.PanHint.Children.Remove(NewHintControl), After: true) }, Conversions.ToString(Operators.ConcatenateObject("Hint Hide ", ((dynamic)NewHintControl.Tag)[1])));
                    }
                // 结束处理
                EndHint:
                    ;

                    if (CurrentHint.Log)
                        Logger.Info($"弹出提示：{CurrentHint.Text}");
                }
            }
            catch (Exception ex)
            {
                Logger.Info(ex, "显示弹出提示失败");
            }
        }
        private static void HideAllHint()
        {
            foreach (Border Control in FrmMain.PanHint.Children)
            {
                Control.IsHitTestVisible = false;
                ModAnimation.AniStart(new[] { ModAnimation.AaX(Control, -50, 200, Ease: new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(Control, -1, 150, Ease: new ModAnimation.AniEaseInFluent()), ModAnimation.AaCode(() => ((dynamic)Control.Tag)[0] = false), ModAnimation.AaHeight(Control, -26, 100, Ease: new ModAnimation.AniEaseOutFluent(), After: true), ModAnimation.AaCode(() => FrmMain.PanHint.Children.Remove(Control), After: true) }, Conversions.ToString(Operators.ConcatenateObject("Hint Hide ", ((dynamic)Control.Tag)[1])));
            }
        }

        #endregion

        #region 弹窗

        /// <summary>
    /// 存储弹窗信息的转换器。
    /// </summary>
        public class MyMsgBoxConverter
        {
            public MyMsgBoxType Type;
            public string Title;
            public string Text;
            /// <summary>
        /// 输入模式：文本框的文本。
        /// 选择模式：需要放进去的 IEnumberable(Of IMyRadio)。
        /// 登录模式：登录步骤 1 中返回的 JSON。
        /// </summary>
            public object Content;
            /// <summary>
        /// 输入模式：输入验证规则。
        /// </summary>
            public Collection<ValidateType> ValidateRules;
            /// <summary>
        /// 输入模式：提示文本。
        /// </summary>
            public string HintText = "";
            /// <summary>
        /// 有多个按钮时，是否给第一个按钮加高亮。
        /// </summary>
            public bool HighLight;
            public string Button1 = "确定";
            public string Button2 = "";
            public string Button3 = "";
            /// <summary>
        /// 点击第一个按钮将执行该方法，不关闭弹窗。
        /// </summary>
            public Action Button1Action = null;
            /// <summary>
        /// 点击第二个按钮将执行该方法，不关闭弹窗。
        /// </summary>
            public Action Button2Action = null;
            /// <summary>
        /// 点击第三个按钮将执行该方法，不关闭弹窗。
        /// </summary>
            public Action Button3Action = null;
            public bool IsWarn = false;
            public bool ForceWait = false;
            public DispatcherFrame WaitFrame = new DispatcherFrame(true);
            /// <summary>
        /// 弹窗是否已经关闭。
        /// </summary>
            public bool IsExited = false;
            /// <summary>
        /// 输入模式：输入的文本。若点击了 非 第一个按钮，则为 Nothing。
        /// 选择模式：点击的按钮编号，从 1 开始。
        /// 登录模式：字符串数组 {AccessToken, RefreshToken} 或一个 Exception。
        /// </summary>
            public object Result;
        }
        public enum MyMsgBoxType
        {
            Text,
            Select,
            Input,
            Login
        }

        /// <summary>
    /// 显示弹窗，返回点击按钮的编号（从 1 开始）。
    /// </summary>
    /// <param name="Title">弹窗的标题。</param>
    /// <param name="Caption">弹窗的内容。</param>
    /// <param name="Button1">显示的第一个按钮，默认为“确定”。</param>
    /// <param name="Button2">显示的第二个按钮，默认为空。</param>
    /// <param name="Button3">显示的第三个按钮，默认为空。</param>
    /// <param name="Button1Action">点击第一个按钮将执行该方法，不关闭弹窗。</param>
    /// <param name="Button2Action">点击第二个按钮将执行该方法，不关闭弹窗。</param>
    /// <param name="Button3Action">点击第三个按钮将执行该方法，不关闭弹窗。</param>
    /// <param name="IsWarn">是否为警告弹窗，若为 True，弹窗配色和背景会变为红色。</param>
        public static int MyMsgBox(string Caption, string Title = "提示", string Button1 = "确定", string Button2 = "", string Button3 = "", bool IsWarn = false, bool HighLight = true, bool ForceWait = false, Action Button1Action = null, Action Button2Action = null, Action Button3Action = null)
        {
            // 将弹窗列入队列
            var Converter = new MyMsgBoxConverter() { Type = MyMsgBoxType.Text, Button1 = Button1, Button2 = Button2, Button3 = Button3, Text = Caption, IsWarn = IsWarn, Title = Title, HighLight = HighLight, ForceWait = true, Button1Action = Button1Action, Button2Action = Button2Action, Button3Action = Button3Action };
            if (Button2.Length > 0 || ForceWait)
            {
                // 若有多个按钮则开始等待
                if (FrmMain is null || FrmMain.PanMsg is null && ModBase.RunInUi())
                {
                    // 主窗体尚未加载，用老土的弹窗来替代
                    if (Button2.Length > 0)
                    {
                        var RawResult = Interaction.MsgBox(Caption, (MsgBoxStyle)((int)(Button3.Length > 0 ? MsgBoxStyle.YesNoCancel : MsgBoxStyle.YesNo) + (int)(IsWarn ? MsgBoxStyle.Critical : MsgBoxStyle.Question)), Title);
                        switch (RawResult)
                        {
                            case MsgBoxResult.Yes:
                                {
                                    Converter.Result = 1;
                                    break;
                                }
                            case MsgBoxResult.No:
                                {
                                    Converter.Result = 2;
                                    break;
                                }
                            case MsgBoxResult.Cancel:
                                {
                                    Converter.Result = 3;
                                    break;
                                }
                        }
                    }
                    else
                    {
                        Interaction.MsgBox(Caption, (MsgBoxStyle)((int)MsgBoxStyle.OkOnly + (int)(IsWarn ? MsgBoxStyle.Critical : MsgBoxStyle.Question)), Title);
                        Converter.Result = 1;
                    }
                    Logger.Warn($"主窗体加载完成前出现意料外的等待弹窗：{Button1},{Button2},{Button3}");
                }
                else
                {
                    WaitingMyMsgBox.Enqueue(Converter);
                    try
                    {
                        FrmMain.DragStop();
                        if (ModBase.RunInUi())
                            MyMsgBoxTick();
                        ComponentDispatcher.PushModal();
                        Dispatcher.PushFrame(Converter.WaitFrame);
                    }
                    finally
                    {
                        ComponentDispatcher.PopModal();
                    }
                }
                Logger.Info($"普通弹框返回：{Converter.Result ?? "null"}");
                return Conversions.ToInteger(Converter.Result);
            }
            else
            {
                // 不进行等待，直接返回
                WaitingMyMsgBox.Enqueue(Converter);
                return 1;
            }
        }
        /// <summary>
    /// 显示输入框并返回输入的文本。若点击第二个按钮，则返回 Nothing。
    /// </summary>
    /// <param name="Title">弹窗的标题。</param>
    /// <param name="ValidateRules">文本框的输入检测。</param>
    /// <param name="Text">弹窗的介绍文本。</param>
    /// <param name="DefaultInput">文本框的默认内容。</param>
    /// <param name="HintText">文本框的提示内容。</param>
    /// <param name="Button1">显示的第一个按钮，默认为“确定”。</param>
    /// <param name="Button2">显示的第二个按钮，默认为“取消”。</param>
    /// <param name="IsWarn">是否为警告弹窗，若为 True，弹窗配色和背景会变为红色。</param>
        public static string MyMsgBoxInput(string Title, string Text = null, string DefaultInput = null, Collection<ValidateType> ValidateRules = null, string HintText = "", string Button1 = "确定", string Button2 = "取消", bool IsWarn = false)
        {
            // 将弹窗列入队列
            var Converter = new MyMsgBoxConverter() { Text = Text ?? "", HintText = HintText ?? "", Type = MyMsgBoxType.Input, ValidateRules = ValidateRules ?? new Collection<ValidateType>(), Button1 = Button1, Button2 = Button2, Content = DefaultInput, IsWarn = IsWarn, Title = Title };
            WaitingMyMsgBox.Enqueue(Converter);
            // 虽然我也不知道这是啥但是能用就成了 :)
            try
            {
                if (FrmMain is not null)
                    FrmMain.DragStop();
                if (ModBase.RunInUi())
                    MyMsgBoxTick();
                ComponentDispatcher.PushModal();
                Dispatcher.PushFrame(Converter.WaitFrame);
            }
            finally
            {
                ComponentDispatcher.PopModal();
            }
            Logger.Info($"输入弹框返回：{Converter.Result ?? "null"}");
            return Conversions.ToString(Converter.Result);
        }
        /// <summary>
    /// 显示选择框并返回选择的第几项（从 0 开始）。若点击第二个按钮，则返回 Nothing。
    /// </summary>
    /// <param name="Title">弹窗的标题。</param>
    /// <param name="Button1">显示的第一个按钮，默认为 “确定”。</param>
    /// <param name="Button2">显示的第二个按钮，默认为空。</param>
    /// <param name="IsWarn">是否为警告弹窗，若为 True，弹窗配色和背景会变为红色。</param>
        public static int? MyMsgBoxSelect(IEnumerable<IMyRadio> Selections, string Title = "提示", string Button1 = "确定", string Button2 = "", bool IsWarn = false)
        {
            // 将弹窗列入队列
            var Converter = new MyMsgBoxConverter() { Type = MyMsgBoxType.Select, Button1 = Button1, Button2 = Button2, Content = Selections, IsWarn = IsWarn, Title = Title };
            WaitingMyMsgBox.Enqueue(Converter);
            // 虽然我也不知道这是啥但是能用就成了 :)
            try
            {
                if (FrmMain is not null)
                    FrmMain.DragStop();
                if (ModBase.RunInUi())
                    MyMsgBoxTick();
                ComponentDispatcher.PushModal();
                Dispatcher.PushFrame(Converter.WaitFrame);
            }
            finally
            {
                ComponentDispatcher.PopModal();
            }
            Logger.Info($"选择弹框返回：{Converter.Result ?? "null"}");
            return (int?)Converter.Result;
        }

        /// <summary>
    /// 等待显示的弹窗。
    /// </summary>
        public static ConcurrentQueue<MyMsgBoxConverter> WaitingMyMsgBox = new ConcurrentQueue<MyMsgBoxConverter>();
        public static void MyMsgBoxTick()
        {
            try
            {
                if (FrmMain?.PanMsg is null || FrmMain.WindowState == WindowState.Minimized)
                    return;
                MyMsgBoxConverter TargetMsgBox = null;
                if (FrmMain.PanMsg.Children.Count > 0)
                {
                    // 弹窗中
                    FrmMain.PanMsg.Visibility = Visibility.Visible;
                }
                else if (WaitingMyMsgBox.TryDequeue(out TargetMsgBox))
                {
                    // 没有弹窗，显示一个等待的弹窗
                    FrmMain.PanMsg.Visibility = Visibility.Visible;
                    switch (TargetMsgBox.Type)
                    {
                        case MyMsgBoxType.Input:
                            {
                                FrmMain.PanMsg.Children.Add(new MyMsgInput(TargetMsgBox));
                                break;
                            }
                        case MyMsgBoxType.Select:
                            {
                                FrmMain.PanMsg.Children.Add(new MyMsgSelect(TargetMsgBox));
                                break;
                            }
                        case MyMsgBoxType.Text:
                            {
                                FrmMain.PanMsg.Children.Add(new MyMsgText(TargetMsgBox));
                                break;
                            }
                        case MyMsgBoxType.Login:
                            {
                                FrmMain.PanMsg.Children.Add(new MyMsgLogin(TargetMsgBox));
                                break;
                            }
                    }
                }
                // 没有弹窗，没有等待的弹窗
                else if (FrmMain.PanMsg.Visibility != Visibility.Collapsed)
                    FrmMain.PanMsg.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "处理等待中的弹窗失败");
            }
        }

        #endregion

        #region 页面声明
        // 在最后进行页面声明，避免颜色尚未加载完毕

        // 窗体声明
        public static FormMain FrmMain;
        public static SplashScreen FrmStart;

        // 页面声明（出于单元测试考虑，初始化页面已转入 FormMain 中）
        public static PageLaunchLeft FrmLaunchLeft;
        public static PageLaunchRight FrmLaunchRight;
        public static PageSelectLeft FrmSelectLeft;
        public static PageSelectRight FrmSelectRight;
        public static PageSpeedLeft FrmSpeedLeft;
        public static PageSpeedRight FrmSpeedRight;

        // 联机页面声明
        public static PageLinkMain FrmLinkMain;

        // 下载页面声明
        public static PageDownloadLeft FrmDownloadLeft;
        public static PageDownloadInstall FrmDownloadInstall;
        public static PageDownloadMod FrmDownloadMod;
        public static PageDownloadPack FrmDownloadPack;
        public static PageDownloadDataPack FrmDownloadDataPack;
        public static PageDownloadShader FrmDownloadShader;
        public static PageDownloadResourcePack FrmDownloadResourcePack;

        // 设置页面声明
        public static PageSetupLeft FrmSetupLeft;
        public static PageSetupLaunch FrmSetupLaunch;
        public static PageSetupUI FrmSetupUI;
        public static PageSetupSystem FrmSetupSystem;
        public static PageSetupLink FrmSetupLink;

        // 其他页面声明
        public static PageOtherLeft FrmOtherLeft;
        public static PageOtherHelp FrmOtherHelp;
        public static PageOtherAbout FrmOtherAbout;
        public static PageOtherTest FrmOtherTest;

        // 登录页面声明
        public static PageLoginLegacy FrmLoginLegacy;
        public static PageLoginNide FrmLoginNide;
        public static PageLoginNideSkin FrmLoginNideSkin;
        public static PageLoginAuth FrmLoginAuth;
        public static PageLoginAuthSkin FrmLoginAuthSkin;
        public static PageLoginMs FrmLoginMs;
        public static PageLoginMsSkin FrmLoginMsSkin;

        // 版本设置页面声明
        public static PageInstanceLeft FrmInstanceLeft;
        public static PageInstanceOverall FrmInstanceOverall;
        public static PageInstanceMod FrmInstanceMod;
        public static PageInstanceModDisabled FrmInstanceModDisabled;
        public static PageInstanceSetup FrmInstanceSetup;
        public static PageInstanceExport FrmInstanceExport;
        public static PageDownloadModify FrmDownloadModify;

        // 资源信息分页声明
        public static PageDownloadResourceDetail FrmDownloadResourceDetail;

        #endregion

        #region 帮助

        public class HelpEntry
        {
            /// <summary>
        /// 原始信息路径。用于刷新。
        /// </summary>
            public string RawPath;

            // 基础

            /// <summary>
        /// 显示标题。
        /// </summary>
            public string Title;
            /// <summary>
        /// 显示描述。
        /// </summary>
            public string Desc;
            /// <summary>
        /// 检索关键字。
        /// </summary>
            public string Search;
            /// <summary>
        /// 用于分类的标签列表。
        /// </summary>
            public List<string> Types;

            // 显示（可选）

            /// <summary>
        /// 帮助项的自定义图标。可能为 Nothing。
        /// </summary>
            public string Logo = null;
            /// <summary>
        /// 是否显示在搜索结果。默认为 True。
        /// </summary>
            public bool ShowInSearch = true;
            /// <summary>
        /// 是否在公开版的 PCL 中显示（这会影响主页与搜索）。默认为 True。
        /// </summary>
            public bool ShowInPublic = true;
            /// <summary>
        /// 是否在快照版的 PCL 中显示（这会影响主页与搜索）。默认为 True。
        /// </summary>
            public bool ShowInSnapshot = true;

            // 动作

            /// <summary>
        /// 是否为 “执行事件”。
        /// </summary>
            public bool IsEvent;
            public CustomEvent.EventType EventType;
            public string EventData;
            /// <summary>
        /// 若非执行事件，其对应的 .xaml 本地文件内容。
        /// </summary>
            public string XamlContent;

            // 转换

            /// <summary>
        /// 从文件初始化 HelpEntry 对象，失败会抛出异常。
        /// </summary>
            public HelpEntry(string FilePath)
            {
                RawPath = FilePath;
                if (!FileUtils.Exists(FilePath))
                    throw new FileNotFoundException("未找到帮助文件：" + FilePath, FilePath);
                JObject JsonData = (JObject)ArgumentReplace(FileUtils.ReadAsString(FilePath), StringUtils.XmlEscape).DeserializeJson();
                // 加载常规信息
                if (JsonData["Title"] is not null)
                {
                    Title = (string)JsonData["Title"];
                }
                else
                {
                    throw new ArgumentException("未找到 Title 项");
                }
                Desc = (string)(JsonData["Description"] ?? "");
                Search = (string)(JsonData["Keywords"] ?? "");
                Logo = (string)JsonData["Logo"]; // 为保持 Nothing，不要加 If
                ShowInSearch = (bool)(JsonData["ShowInSearch"] ?? ShowInSearch);
                ShowInPublic = (bool)(JsonData["ShowInPublic"] ?? ShowInPublic);
                ShowInSnapshot = (bool)(JsonData["ShowInSnapshot"] ?? ShowInSnapshot);
                Types = new List<string>();
                foreach (var NameOfType in JsonData["Types"] ?? new JArray())
                    Types.Add((string)NameOfType);
                // 加载事件信息
                if ((bool)(JsonData["IsEvent"] ?? false))
                {
                    EventType = JsonData["EventType"].ToObject<CustomEvent.EventType>();
                    EventData = (string)(JsonData["EventData"] ?? "");
                    IsEvent = true;
                }
                else
                {
                    string XamlAddress = Path.ChangeExtension(FilePath, ".xaml");
                    if (FileUtils.Exists(XamlAddress))
                    {
                        XamlContent = FileUtils.ReadAsString(XamlAddress);
                        IsEvent = false;
                    }
                    else
                    {
                        throw new FileNotFoundException("未找到帮助条目 .json 对应的 .xaml 文件（" + XamlAddress + "）");
                    }
                }
            }
            /// <summary>
        /// 获取该 HelpEntry 对应的 MyListItem。
        /// </summary>
            public MyListItem ToListItem()
            {
                return SetToListItem(new MyListItem());
            }
            /// <summary>
        /// 将属性设置入一个现有的 ListItem。
        /// </summary>
            public MyListItem SetToListItem(MyListItem Item)
            {
                string Logo;
                if (IsEvent)
                {
                    if (EventType == CustomEvent.EventType.弹出窗口)
                    {
                        Logo = ModBase.PathImage + "Blocks/GrassPath.png";
                    }
                    else
                    {
                        Logo = ModBase.PathImage + "Blocks/CommandBlock.png";
                    }
                }
                else
                {
                    Logo = ModBase.PathImage + "Blocks/Grass.png";
                }
                // 设置属性
                Item.SnapsToDevicePixels = true;
                Item.Title = Title;
                Item.Info = Desc;
                Item.Logo = this.Logo ?? Logo;
                Item.Height = 42d;
                Item.Type = MyListItem.CheckType.Clickable;
                Item.Tag = this;
                CustomEventService.SetEventType(Item, CustomEvent.EventType.None); // 清空自定义事件属性，它们会被下面的点击事件处理
                CustomEventService.SetEventData(Item, (string)null);
                // 项目的点击事件
                Item.Click += (sender, e) => PageOtherHelp.OnItemClick((HelpEntry)((dynamic)sender).Tag);
                return Item;
            }

        }

        public static ModLoader.LoaderTask<int, List<HelpEntry>> HelpLoader = new ModLoader.LoaderTask<int, List<HelpEntry>>("Help Page", HelpLoad, null, ThreadPriority.BelowNormal);
        private readonly static object HelpLoadLock = new object();
        /// <summary>
    /// 初始化帮助列表对象。
    /// </summary>
        private static void HelpLoad(ModLoader.LoaderTask<int, List<HelpEntry>> Loader)
        {
            lock (HelpLoadLock) // 避免重复解压文件导致出错
            {
                try
                {

                    // 解压内置文件
                    HelpTryExtract();

                    // 遍历文件
                    var FileList = new List<string>();
                    try
                    {
                        var IgnoreList = new List<string>();
                        // 读取自定义文件
                        if (DirectoryUtils.Exists(Paths.Base + @"PCL\Help\"))
                        {
                            foreach (var File in DirectoryUtils.EnumerateFiles(Paths.Base + @"PCL\Help\", true))
                            {
                                switch (PathUtils.GetExtension(File) ?? "")
                                {
                                    case "helpignore":
                                        {
                                            // 加载忽略列表
                                            Logger.Info($"发现 .helpignore 文件：{File}");
                                            foreach (var Line in FileUtils.ReadAsLines(File, true))
                                            {
                                                string RealString = Line.BeforeFirst("#").Trim();
                                                if (string.IsNullOrWhiteSpace(RealString))
                                                    continue;
                                                IgnoreList.Add(RealString);
                                                Logger.Trace($"> {RealString}");
                                            }

                                            break;
                                        }
                                    case "json":
                                        {
                                            FileList.Add(File);
                                            break;
                                        }
                                }
                            }
                        }
                        Logger.Info($"已扫描 PCL 文件夹下的帮助文件，目前总计 {FileList.Count} 条");
                        // 读取自带文件
                        foreach (var File in DirectoryUtils.EnumerateFiles(ModBase.PathTemp + "Help", true))
                        {
                            // 跳过非 json 文件与以 . 开头的文件夹
                            if (PathUtils.RemoveLastPart(File).Substring((ModBase.PathTemp + "Help").Length).Contains(@"\.") || PathUtils.GetExtension(File) != "json")
                                continue;
                            // 检查忽略列表
                            string RealPath = File.Substring((ModBase.PathTemp + @"Help\").Length);
                            foreach (var Ignore in IgnoreList)
                            {
                                if (RealPath.RegexCheck(Ignore))
                                {
                                    Logger.Trace($"已忽略 {RealPath}：{Ignore}");
                                    goto NextFile;
                                }
                            }
                            FileList.Add(File);
                        NextFile:
                            ;

                        }
                        Logger.Info($"已扫描缓存文件夹下的帮助文件，目前总计 {FileList.Count} 条");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "检查帮助文件夹失败", LogBehavior.Alert);
                    }
                    if (Loader.IsCanceled)
                        return;

                    // 将文件实例化
                    var Dict = new List<HelpEntry>();
                    foreach (string FilePath in FileList)
                    {
                        try
                        {
                            var Entry = new HelpEntry(FilePath);
                            Dict.Add(Entry);
                            Logger.Trace($"已加载的帮助条目：{Entry.Title} ← {FilePath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"初始化帮助条目失败（{FilePath}）", LogBehavior.Alert);
                        }
                    }

                    // 回设
                    if (!Dict.Any())
                        throw new Exception("未找到可用的帮助；若不需要帮助页面，可以在 设置 → 个性化 → 功能隐藏 中将其隐藏");
                    if (Loader.IsCanceled)
                        return;
                    Loader.Output = Dict;
                }

                catch (Exception ex)
                {
                    Logger.Warn(ex, "帮助列表初始化失败");
                    throw;
                }
            }
        }
        /// <summary>
    /// 尝试解压内置帮助文件。
    /// </summary>
        public static void HelpTryExtract()
        {
            if (Settings.Get<int>("SystemHelpVersion") != ModBase.VersionCode || !FileUtils.Exists(ModBase.PathTemp + @"Help\启动器\备份设置.xaml"))
            {
                DirectoryUtils.Delete(ModBase.PathTemp + "Help");
                ModBase.ExtractResources(ModBase.PathTemp + @"Cache\Help.zip", "Help");
                FileUtils.ExtractToDirectory(ModBase.PathTemp + @"Cache\Help.zip", ModBase.PathTemp + "Help");
                Settings.Set("SystemHelpVersion", ModBase.VersionCode);
                Logger.Info($"已解压内置帮助文件，目前状态：{FileUtils.Exists(ModBase.PathTemp + @"Help\启动器\备份设置.xaml")}");
            }
        }

        #endregion

        #region 愚人节

        private static bool? _IsAprilEnabled = default;
        public static bool IsAprilEnabled
        {
            get
            {
                if (_IsAprilEnabled is null)
                {
                    _IsAprilEnabled = DateTime.Now.Month == 4 && DateTime.Now.Day == 1 && Settings.Get<int>("AprilYear") != DateTime.Now.Year; // 成功后同年内则不再触发
                }
                return _IsAprilEnabled.Value;
            }
        }
        public static bool IsAprilGiveup = false;
        private static int AprilDifficultiedDistance = 0;
        private static int _TimerFool_IdieCount = 0;
        private static Point _TimerFool_MousePosLast = new Point(0d, 0d);
        // 回到边界
        private static Vector _TimerFool_Speed = new Vector(0d, 0d);
        // 放弃提示
        private static double _TimerFool_GiveUpDistance = -1500;
        private static void TimerFool()
        {
            try
            {
                if (FrmLaunchLeft is null || FrmLaunchLeft.AprilPosTrans is null || FrmMain.lastMouseArg is null)
                    return;
                if (IsAprilGiveup || FrmMain.PageCurrent != (FormMain.PageStackData)FormMain.PageType.Launch || ModAnimation.AniControlEnabled != 0 || !FrmLaunchLeft.BtnLaunch.IsLoaded)
                    return;

                // 计算是否空闲
                var MousePos = FrmMain.lastMouseArg.GetPosition(FrmMain);
                if (MousePos == _TimerFool_MousePosLast)
                {
                    _TimerFool_IdieCount += 1;
                }
                else
                {
                    _TimerFool_MousePosLast = MousePos;
                    _TimerFool_IdieCount = 0;
                }
                // 计算躲避移动
                Vector Direction;
                double Distance;
                double ButtonWidth = FrmLaunchLeft.BtnLaunch.ActualWidth / 2d;
                double ButtonHeight = FrmLaunchLeft.BtnLaunch.ActualHeight / 2d;
                Vector Vec = (Vector)(FrmMain.lastMouseArg.GetPosition(FrmLaunchLeft.BtnLaunch) - new Vector(ButtonWidth, ButtonHeight));
                var Dir = new Vector(Vec.X, Vec.Y);
                Dir.Normalize();
                Direction = -Dir;
                Distance = new Vector(Math.Max(0d, Math.Abs(Vec.X) - ButtonWidth), Math.Max(0d, Math.Abs(Vec.Y) - ButtonHeight)).Length;
                double BreathScale = Math.Sin(Timer150Count / 37.5d * Math.PI);
                double Difficulty = 1d / (1d + Math.Pow(AprilDifficultiedDistance / 6000d, 2d)) + 0.25d; // 难度，初始为 1.25x，6000 距离为 0.75x，12000 为 0.5x，24000 为 0.256x，最低 0.25x
                var Acc = Math.Max(0d, -0.65d - Math.Log((Distance / Difficulty + 0.4d) / 200d)) * Direction * Difficulty; // 加速度
                                                                                                                           // FrmMain.Title = CInt(AprilTotalDistance) & " - " & Difficulty
                                                                                                                           // 5s 不动时回到起始点
                if (_TimerFool_IdieCount >= 64 * 5)
                {
                    Vector SafeDist = (Vector)(FrmMain.lastMouseArg.GetPosition(FrmMain.PanMain) - new Vector(ButtonWidth, FrmMain.PanMain.ActualHeight - ButtonHeight * 3d));
                    var Back = new Vector(FrmLaunchLeft.AprilPosTrans.X, FrmLaunchLeft.AprilPosTrans.Y);
                    if (SafeDist.Length > 250d && Back.Length > 0.4d)
                    {
                        Acc -= Back * 0.0005d;
                        Back.Normalize();
                        Acc -= Back * 0.15d;
                    }
                }
                var Relative = FrmLaunchLeft.BtnLaunch.TranslatePoint(new Point(0d, 0d), FrmMain.PanForm);
                if (Relative.X < -ButtonWidth * 2d)
                {
                    FrmLaunchLeft.AprilPosTrans.X += FrmMain.PanForm.ActualWidth + ButtonWidth * 2d; // 离开左边界
                    _TimerFool_Speed.X -= 80d;
                    if (Relative.Y < 0d)
                    {
                        FrmLaunchLeft.AprilPosTrans.Y += ButtonHeight * 2.5d;
                    }
                    else if (Relative.Y > FrmMain.PanForm.ActualHeight - ButtonHeight * 2d)
                    {
                        FrmLaunchLeft.AprilPosTrans.Y -= ButtonHeight * 2.5d;
                    }
                }
                else if (Relative.X > FrmMain.PanForm.ActualWidth)
                {
                    FrmLaunchLeft.AprilPosTrans.X -= FrmMain.PanForm.ActualWidth + ButtonWidth * 2d; // 离开右边界
                    _TimerFool_Speed.X += 80d;
                    if (Relative.Y < 0d)
                    {
                        FrmLaunchLeft.AprilPosTrans.Y += ButtonHeight * 2.5d;
                    }
                    else if (Relative.Y > FrmMain.PanForm.ActualHeight - ButtonHeight * 2d)
                    {
                        FrmLaunchLeft.AprilPosTrans.Y -= ButtonHeight * 2.5d;
                    }
                }
                else if (Relative.Y < -ButtonHeight * 2d)
                {
                    FrmLaunchLeft.AprilPosTrans.Y += FrmMain.PanForm.ActualHeight + ButtonHeight * 2d; // 离开上边界
                    _TimerFool_Speed.Y -= 25d;
                    if (Relative.X < 0d)
                    {
                        FrmLaunchLeft.AprilPosTrans.X += ButtonWidth * 2d;
                    }
                    else if (Relative.X > FrmMain.PanForm.ActualWidth - ButtonWidth * 2d)
                    {
                        FrmLaunchLeft.AprilPosTrans.X -= ButtonWidth * 2d;
                    }
                }
                else if (Relative.Y > FrmMain.PanForm.ActualHeight)
                {
                    FrmLaunchLeft.AprilPosTrans.Y -= FrmMain.PanForm.ActualHeight + ButtonHeight * 2d; // 离开下边界
                    _TimerFool_Speed.Y += 25d;
                    if (Relative.X < 0d)
                    {
                        FrmLaunchLeft.AprilPosTrans.X += ButtonWidth * 2d;
                    }
                    else if (Relative.X > FrmMain.PanForm.ActualWidth - ButtonWidth * 2d)
                    {
                        FrmLaunchLeft.AprilPosTrans.X -= ButtonWidth * 2d;
                    }
                }
                // 移动
                _TimerFool_Speed = _TimerFool_Speed * 0.8d + Acc;
                double SpeedValue = Math.Min(60d, _TimerFool_Speed.Length);
                if (SpeedValue < 0.01d)
                    return;
                _TimerFool_Speed.Normalize();
                _TimerFool_Speed *= SpeedValue;
                AprilDifficultiedDistance = (int)Math.Round(AprilDifficultiedDistance + SpeedValue);
                FrmLaunchLeft.AprilPosTrans.X += _TimerFool_Speed.X;
                FrmLaunchLeft.AprilPosTrans.Y += _TimerFool_Speed.Y;
                // 大小改变
                FrmLaunchLeft.AprilScaleTrans.ScaleX = (1d - (Math.Abs(Direction.X) - Math.Abs(Direction.Y)) * (SpeedValue / 160d)).Clamp(0.2d, 1.8d);
                FrmLaunchLeft.AprilScaleTrans.ScaleY = (1d - (Math.Abs(Direction.Y) - Math.Abs(Direction.X)) * (SpeedValue / 100d)).Clamp(0.2d, 1.8d);
                _TimerFool_GiveUpDistance += SpeedValue;
                if (_TimerFool_GiveUpDistance > 2500d)
                {
                    _TimerFool_GiveUpDistance = 0d;
                    switch (ModBase.RandomInteger(0, 3))
                    {
                        case 0:
                            {
                                Hint("放弃吧！只需要点一下右下角的小白旗……", HintType.Red);
                                break;
                            }
                        case 1:
                            {
                                Hint("看到右下角的那面小白旗了吗？", HintType.Red);
                                break;
                            }
                        case 2:
                            {
                                Hint("这里建议点一下右下角的小白旗投降呢.jpg", HintType.Red);
                                break;
                            }
                        case 3:
                            {
                                Hint("右下角的小白旗永远等着你……", HintType.Red);
                                break;
                            }
                    }
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "愚人节移动出错");
            }
        }

        #endregion

        #region 系统

        /// <summary>
    /// 把某个 PCL 窗口拖到最前面。
    /// </summary>
        public static void ShowWindowToTop(nint Handle)
        {
            try
            {
                PostMessage(Handle, 400 * 16 + 2, 0L, 0L);
                SetForegroundWindow(Handle); // 不在这里放不行，神秘 WinAPI，建议别动
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "设置窗口置顶失败", LogBehavior.Toast);
            }
        }
        [DllImport("user32", EntryPoint = "FindWindowA")]
        public static extern nint FindWindow(string ClassName, string WindowName);
        [DllImport("user32")]
        public static extern int SetForegroundWindow(nint hWnd);
        [DllImport("user32", EntryPoint = "PostMessageA")]
        private static extern bool PostMessage(nint hWnd, uint msg, long wParam, long lParam);

        /// <summary>
    /// 将特定程序设置为使用高性能显卡启动。
    /// 如果失败，则抛出异常。
    /// </summary>
        public static void SetGPUPreference(string Executeable)
        {
            const string REG_KEY = @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences";
            const string REG_VALUE = "GpuPreference=2;";
            // 查看现有设置
            if ((RegistryUtils.Read(REG_KEY, Executeable).result?.ToString() ?? "") == REG_VALUE)
            {
                Logger.Info($"无需调整显卡设置：{Executeable}");
                return;
            }
            // 写入新设置
            RegistryUtils.Write(REG_KEY, Executeable, REG_VALUE);
            Logger.Info($"已调整显卡设置：{Executeable}");
        }
        private static string _ArgumentReplace_Replacer(string s, Func<string, string> EscapeHandler = null)
        {
            if (s is null)
                return "";
            if (EscapeHandler is null)
                return s;
            if (s.Contains(@":\"))
            {
                bool IsFolder = s.EndsWithF(@"\") || s.EndsWithF("/");
                s = PathUtils.ToShortPath(s);
                if (IsFolder)
                    s = PathUtils.AddSlashSuffix(s);
            }
            return EscapeHandler(s);
        }

        /// <summary>
    /// 对替换标记进行处理。会对替换内容使用 EscapeHandler 进行转义。
    /// </summary>
        public static string ArgumentReplace(string Text, Func<string, string> EscapeHandler = null, bool ReplaceTime = true)
        {
            // 预处理（注意，文件夹必须以 \ 结尾）
            if (Text is null || !Text.Contains("{"))
                return Text;
            // 基础
            Text = Text.Replace("{pcl_version}", _ArgumentReplace_Replacer(ModBase.VersionBaseName, EscapeHandler));
            Text = Text.Replace("{pcl_version_code}", _ArgumentReplace_Replacer(ModBase.VersionCode.ToString(), EscapeHandler));
            Text = Text.Replace("{pcl_version_branch}", _ArgumentReplace_Replacer(ModBase.BuildTypeDisplay, EscapeHandler));
            Text = Text.Replace("{pcl_build_type}", _ArgumentReplace_Replacer(ModBase.BuildType.ToString(), EscapeHandler));
            Text = Text.Replace("{pcl_branch}", _ArgumentReplace_Replacer(ModSecret.VersionBranchMain, EscapeHandler));
            Text = Text.Replace("{identify}", _ArgumentReplace_Replacer(ModBase.Identify, EscapeHandler));
            Text = Text.Replace("{path}", _ArgumentReplace_Replacer(Paths.Base, EscapeHandler));
            Text = Text.Replace("{path_with_name}", _ArgumentReplace_Replacer(ModBase.PathExe, EscapeHandler));
            Text = Text.Replace("{path_temp}", _ArgumentReplace_Replacer(ModBase.PathTemp, EscapeHandler));
            Text = Text.Replace("{pcl_md5}", () => _ArgumentReplace_Replacer(CryptographyUtils.ComputeFileHash(ModBase.PathExe, CryptographyUtils.HashMethod.Md5), EscapeHandler));
            Text = Text.Replace("{pcl_sha1}", () => _ArgumentReplace_Replacer(CryptographyUtils.ComputeFileHash(ModBase.PathExe, CryptographyUtils.HashMethod.Sha1), EscapeHandler));
            // 时间
            if (ReplaceTime) // 在窗口标题中，时间会被后续动态替换，所以此时不应该替换
            {
                Text = Text.Replace("{date}", _ArgumentReplace_Replacer(DateTime.Now.ToString("yyyy'/'M'/'d"), EscapeHandler));
                Text = Text.Replace("{time}", _ArgumentReplace_Replacer(DateTime.Now.ToString("HH':'mm':'ss"), EscapeHandler));
            }
            // Minecraft
            Text = Text.Replace("{java}", _ArgumentReplace_Replacer(ModLaunch.McLaunchJavaSelected?.Folder, EscapeHandler));
            Text = Text.Replace("{minecraft}", _ArgumentReplace_Replacer(ModMinecraft.McFolderSelected, EscapeHandler));
            if (ModMinecraft.McInstanceSelected?.IsLoaded == true)
            {
                Text = Text.Replace("{version_path}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.PathVersion, EscapeHandler));
                Text = Text.Replace("{verpath}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.PathVersion, EscapeHandler));
                Text = Text.Replace("{version_indie}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.PathIndie, EscapeHandler));
                Text = Text.Replace("{verindie}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.PathIndie, EscapeHandler));
                Text = Text.Replace("{name}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.Name, EscapeHandler));
                if (new[] { "unknown", "old", "pending" }.Contains(ModMinecraft.McInstanceSelected.Version.VanillaName.Lower()))
                {
                    Text = Text.Replace("{version}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.Name, EscapeHandler));
                }
                else
                {
                    Text = Text.Replace("{version}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.Version.VanillaName, EscapeHandler));
                }
                Text = Text.Replace("{forge}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Forge) ? ModMinecraft.McInstanceSelected.Version.ModLoaderVersions[ModLoaders.Forge] : "", EscapeHandler));
                Text = Text.Replace("{fabric}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) ? ModMinecraft.McInstanceSelected.Version.ModLoaderVersions[ModLoaders.Fabric] : "", EscapeHandler));
                Text = Text.Replace("{neoforge}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) ? ModMinecraft.McInstanceSelected.Version.ModLoaderVersions[ModLoaders.NeoForge] : "", EscapeHandler));
                Text = Text.Replace("{liteloader}", _ArgumentReplace_Replacer(ModMinecraft.McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) ? "true" : "", EscapeHandler));
            }
            else
            {
                Text = Text.Replace("{version_path}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{verpath}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{version_indie}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{verindie}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{name}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{version}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{forge}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{fabric}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{neoforge}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{liteloader}", _ArgumentReplace_Replacer(null, EscapeHandler));
            }
            // 登录信息
            if (ModLaunch.McLoginLoader.State == ModBase.LoadState.Finished)
            {
                Text = Text.Replace("{user}", _ArgumentReplace_Replacer(ModLaunch.McLoginLoader.Output.Name, EscapeHandler));
                Text = Text.Replace("{uuid}", _ArgumentReplace_Replacer(ModLaunch.McLoginLoader.Output.Uuid?.Lower(), EscapeHandler));
                switch (ModLaunch.McLoginLoader.Input.Type)
                {
                    case ModLaunch.McLoginType.Legacy:
                        {
                            Text = Text.Replace("{login}", _ArgumentReplace_Replacer("离线", EscapeHandler));
                            break;
                        }
                    case ModLaunch.McLoginType.Ms:
                        {
                            Text = Text.Replace("{login}", _ArgumentReplace_Replacer("正版", EscapeHandler));
                            break;
                        }
                    case ModLaunch.McLoginType.Nide:
                        {
                            Text = Text.Replace("{login}", _ArgumentReplace_Replacer("统一通行证", EscapeHandler));
                            break;
                        }
                    case ModLaunch.McLoginType.Auth:
                        {
                            Text = Text.Replace("{login}", _ArgumentReplace_Replacer("Authlib-Injector", EscapeHandler));
                            break;
                        }
                }
            }
            else
            {
                Text = Text.Replace("{user}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{uuid}", _ArgumentReplace_Replacer(null, EscapeHandler));
                Text = Text.Replace("{login}", _ArgumentReplace_Replacer(null, EscapeHandler));
            }
            // 高级
            Text = Text.RegexReplace(@"\{hint\}", (_) => _ArgumentReplace_Replacer(PageOtherTest.GetRandomHint(), EscapeHandler));
            Text = Text.RegexReplace(@"\{cave\}", (_) => _ArgumentReplace_Replacer(PageOtherTest.GetRandomCave(), EscapeHandler));
            Text = Text.RegexReplace(@"\{setup:([a-zA-Z0-9]+)\}", m => _ArgumentReplace_Replacer(Conversions.ToString(Settings.GetSafe(m.Groups[1].Value, ModMinecraft.McInstanceSelected)), EscapeHandler));
            Text = Text.RegexReplace(@"\{varible:([^:\}]+)(?::([^\}]+))?\}", m => _ArgumentReplace_Replacer(Conversions.ToString(RegistryUtils.TryRead($@"HKEY_CURRENT_USER\Software\{ModSecret.RegFolder}", $"CustomEvent{m.Groups[1].Value}", m.Groups[2].Value)), EscapeHandler));
            Text = Text.RegexReplace(@"\{variable:([^:\}]+)(?::([^\}]+))?\}", m => _ArgumentReplace_Replacer(Conversions.ToString(RegistryUtils.TryRead($@"HKEY_CURRENT_USER\Software\{ModSecret.RegFolder}", $"CustomEvent{m.Groups[1].Value}", m.Groups[2].Value)), EscapeHandler));
            return Text;
        }

        #endregion

        #region 任务缓存

        private static bool IsTaskTempCleared = false;
        private static bool IsTaskTempClearing = false;

        /// <summary>
    /// 尝试清理任务缓存文件夹。
    /// 在整次运行中只会实际清理一次。
    /// </summary>
        public static void TryClearTaskTemp()
        {
            if (!IsTaskTempCleared)
            {
                IsTaskTempCleared = true;
                IsTaskTempClearing = true;
                try
                {
                    Logger.Info("开始清理任务缓存文件夹");
                    DirectoryUtils.Delete($@"{ModBase.OsDrive}ProgramData\PCL\TaskTemp\");
                    DirectoryUtils.Delete($@"{ModBase.PathTemp}TaskTemp\");
                    Logger.Info("已清理任务缓存文件夹");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "清理任务缓存文件夹失败");
                }
                finally
                {
                    IsTaskTempClearing = false;
                }
            }
            else if (IsTaskTempClearing)
            {
                // 等待另一个清理步骤完成
                while (IsTaskTempClearing)
                    Thread.Sleep(1);
            }
        }

        /// <summary>
    /// 申请一个可用于任务缓存的临时文件夹，以 \ 结尾。这些文件夹无需进行后续清理。
    /// 若所有缓存位置均没有权限，会抛出异常。
    /// </summary>
    /// <param name="RequireNonSpace">是否要求路径不包含空格。</param>
        public static string RequestTaskTempFolder(bool RequireNonSpace = false)
        {
            TryClearTaskTemp();
            string ResultFolder;
            do
            {
                try
                {
                    ResultFolder = $@"{ModBase.PathTemp}TaskTemp\{ModBase.GetUuid()}-{ModBase.RandomInteger(0, 1000000)}\";
                    if (RequireNonSpace && ResultFolder.Contains(" "))
                        break; // 带空格
                    DirectoryUtils.Create(ResultFolder);
                    ModBase.CheckPermissionWithException(ResultFolder);
                    return ResultFolder;
                }
                catch
                {
                }
            }
            while (false);
            // 使用备用路径
            ResultFolder = $@"{ModBase.OsDrive}ProgramData\PCL\TaskTemp\{ModBase.GetUuid()}-{ModBase.RandomInteger(0, 1000000)}\";
            DirectoryUtils.Create(ResultFolder);
            ModBase.CheckPermission(ResultFolder);
            return ResultFolder;
        }

        #endregion

        #region 反馈与遥测

        // 反馈
        public static void Feedback(bool ShowMsgbox = true, bool ForceOpenLog = false)
        {
            ;
            // [VB Port] On Error Resume Next -> ignore exceptions in the rest of this method
            try
            {
            FeedbackInfo();
            if (ForceOpenLog || ShowMsgbox && MyMsgBox("若你在汇报一个 Bug，请点击 打开文件夹 按钮，并上传 Log(1~5).txt 中包含错误信息的文件。" + "\r\n" + "游戏崩溃一般与启动器无关，请不要因为游戏崩溃而提交反馈。", "反馈提交提醒", "打开文件夹", "不需要") == 1)
            {
                ModBase.OpenExplorer(Paths.Base + @"PCL\Log1.txt");
            }
            ModBase.OpenWebsite("https://github.com/Meloong-Git/PCL/issues/");
            } // [VB Port] end try
            // 对应 ModMain.vb:964-971 的「On Error Resume Next」：反馈流程只是辅助操作，其失败不影响用户继续使用。
            catch (System.ComponentModel.Win32Exception)
            {
                // OpenExplorer / OpenWebsite 经 Process.Start 打开日志文件夹或浏览器，系统无关联程序或被策略拦截时抛 Win32Exception：忽略。
            }
            catch (System.IO.IOException)
            {
                // 打开日志文件夹时文件系统异常（如路径失效）：反馈调用本身已完成，忽略。
            }
            catch (UnauthorizedAccessException)
            {
                // 上述两处权限不足：同理忽略。
            }
        }
        /// <summary>
    /// 在日志中输出系统诊断信息。
    /// </summary>
        public static void FeedbackInfo()
        {
            ;
            // [VB Port] On Error Resume Next -> ignore exceptions in the rest of this method
            try
            {
            Logger.Warn($"诊断信息：{"\r\n"}" + "操作系统：" + My.Computer.Info.OSFullName + "\r\n" + "剩余内存：" + Conversion.Int(My.Computer.Info.AvailablePhysicalMemory / 1024d / 1024d) + " M / " + Conversion.Int(My.Computer.Info.TotalPhysicalMemory / 1024d / 1024d) + " M" + "\r\n" + "DPI：" + ModBase.DPI + "（" + Math.Round(ModBase.DPI / 96d, 2) * 100d + "%）" + "\r\n" + "MC 文件夹：" + (ModMinecraft.McFolderSelected ?? "Nothing") + "\r\n" + "文件位置：" + Paths.Base);
            } // [VB Port] end try
            // 对应 ModMain.vb:975-983 的「On Error Resume Next」：诊断信息只供排查使用，任何失败都不应影响主流程。
            catch (System.ComponentModel.Win32Exception)
            {
                // My.Computer.Info 读取系统信息（WMI / 注册表）失败时抛 Win32Exception：放弃本次诊断输出即可。
            }
            catch (System.IO.IOException)
            {
                // Logger.Warn 写日志文件失败（磁盘已满 / 文件被占用）：忽略。
            }
            catch (UnauthorizedAccessException)
            {
                // 日志目录无写权限：忽略。
            }
        }

        #endregion

        public static object DragControl = null;
        private static int Timer4Count = 0;
        private static int Timer150Count = 0;
        private static void TimerMain()
        {
            try
            {
                #region 每 50ms 执行一次的代码
                HintTick();
                MyMsgBoxTick();
                FrmMain.DragTick();
                ModLoader.LoaderTaskbarProgressRefresh();
                if (ModSecret.ThemeDontClick == 2)
                    ModSecret.ThemeRefresh();
            }
            #endregion
            catch (Exception ex)
            {
                Logger.Error(ex, "短程主时钟执行异常", LogBehavior.AlertThenCrash);
            }
            Timer4Count += 1;
            if (Timer4Count == 4)
            {
                Timer4Count = 0;
                try
                {
                    #region 每 250ms 执行一次的代码
                    if (ModSecret.ThemeNow == 12)
                        ModSecret.ThemeRefresh();
                }
                #endregion
                catch (Exception ex)
                {
                    Logger.Warn(ex, "中程主时钟执行异常");
                }
            }
            Timer150Count += 1;
            if (Timer150Count == 150)
            {
                Timer150Count = 0;
                try
                {
                    #region 每 7.5s 执行一次的代码
                    if (FrmMain.BtnExtraApril_ShowCheck() && AprilDifficultiedDistance != 0)
                        FrmMain.BtnExtraApril.Ribble();
                    // 以未知原因窗口被丢到一边去的修复（Top、Left = -25600），还有 #745
                    ModBase.RunInUi(() => { if (!FrmMain.Hidden) { if (FrmMain.Top < (double)-9000) FrmMain.Top = 100d; if (FrmMain.Left < (double)-9000) FrmMain.Left = 100d; } }); // 窗口拉至最大时 Left = -18.8
                }
                #endregion
                catch (Exception ex)
                {
                    Logger.Error(ex, "长程主时钟执行异常", LogBehavior.AlertThenCrash);
                }
            }
        }
        public static void TimerMainStart()
        {
            ModBase.RunInNewThread(() => { try { while (true) { ModBase.RunInUiWait(TimerMain); Thread.Sleep((int)Math.Round(50d * 0.98d)); } } catch (Exception ex) { Logger.Error(ex, "程序主时钟出错"); } }, "Timer Main");
            if (!IsAprilEnabled)
                return;
            ModBase.RunInNewThread(() => { try { int LastTime = My.Computer.Clock.TickCount; while (true) { if (LastTime != My.Computer.Clock.TickCount) { LastTime = My.Computer.Clock.TickCount; ModBase.RunInUiWait(TimerFool); } Thread.Sleep(1); } } catch (Exception ex) { Logger.Error(ex, "愚人节主时钟出错"); } }, "Timer Main Fool");
        }

    }
}