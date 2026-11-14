using System;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic;

namespace PCL
{
    public class PclLogger : FileLogger
    {

        /// <inheritdoc/>
        public override string Format(string Text, LogLevel Level, string FilePath, Exception Ex)
        {
            Text = base.Format(Text, Level, FilePath, Ex);
            Text = ModMinecraft.FilterUserName(ModMinecraft.FilterAccessToken(Text, '*'), '*');
            return Text;
        }
        private bool _HandleBehavior_FirstTrigger = true;

        /// <inheritdoc/>
        public override void HandleBehavior(string RawMessage, string FormattedMessage, LogBehavior Behavior, Exception Ex)
        {
            if (ModBase.IsProgramEnding)
                return;
            base.HandleBehavior(RawMessage, FormattedMessage, Behavior, Ex);
            string BriefText = Ex is null ? RawMessage : (RawMessage is null ? "" : $"{RawMessage}：") + Ex.GetDisplay(false);
            BriefText = ModMinecraft.FilterUserName(ModMinecraft.FilterAccessToken(BriefText, '*'), '*');
            string DetailText = Ex is null ? RawMessage : (RawMessage is null ? "" : $"{RawMessage}：") + Ex.GetDisplay(true);
            DetailText = ModMinecraft.FilterUserName(ModMinecraft.FilterAccessToken(DetailText, '*'), '*');
            switch (Behavior)
            {
                case LogBehavior.None:
                    {
                        break;
                    }
                // 啥也不干
                case LogBehavior.ToastIfDebug:
                    {
                        if (ModBase.BuildType == ModBase.BuildTypes.Debug || ModBase.ModeDebug)
                            ModMain.Hint("[调试模式] " + BriefText, ModMain.HintType.Blue, false);
                        break;
                    }
                case LogBehavior.Toast:
                    {
                        ModMain.Hint(BriefText, ModMain.HintType.Red, false);
                        break;
                    }
                case LogBehavior.Alert:
                    {
                        ModMain.MyMsgBox(DetailText, "错误", IsWarn: true);
                        break;
                    }
                case LogBehavior.AlertThenFeedback:
                    {
                        if (PageSetupNotify.IsLauncherNewest() == true)
                        {
                            if (ModMain.MyMsgBox(DetailText + Constants.vbCrLf + Constants.vbCrLf + "是否反馈此问题？如果不反馈，这个问题可能永远无法得到解决！", "错误", "反馈", "取消", IsWarn: true) == 1)
                                ModMain.Feedback(false, true);
                        }
                        else
                        {
                            ModMain.MyMsgBox(DetailText + Constants.vbCrLf + Constants.vbCrLf + "将 PCL Next 更新至最新版或许可以解决这个问题……", "错误", IsWarn: true);
                        }

                        break;
                    }
                case LogBehavior.AlertThenCrash:
                    {
                        if (_HandleBehavior_FirstTrigger)
                        {
                            // 首次触发
                            _HandleBehavior_FirstTrigger = false;
                            if (PageSetupNotify.IsLauncherNewest() == true)
                            {
                                if (Interaction.MsgBox(DetailText + Constants.vbCrLf + Constants.vbCrLf + "是否反馈此问题？如果不反馈，这个问题可能永远无法得到解决！", (MsgBoxStyle)((int)MsgBoxStyle.Critical + (int)MsgBoxStyle.YesNo), "错误") == MsgBoxResult.Yes)
                                    ModMain.Feedback(false, true);
                            }
                            else
                            {
                                Interaction.MsgBox(DetailText + Constants.vbCrLf + Constants.vbCrLf + "将 PCL Next 更新至最新版或许可以解决这个问题……", MsgBoxStyle.Critical, "错误");
                            }
                        }
                        else
                        {
                            // 多次触发，直接使程序崩溃（这通常代表着在其他线程循环触发严重异常）
                            Thread.Sleep(2000);
                        }
                        FormMain.EndProgramForce(ModBase.ProcessReturnValues.Exception);
                        break;
                    }
            }
        }

    }
}