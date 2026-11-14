using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Wpf.Extensions;

namespace PCL
{
    /// <summary>
    /// 不可关闭的模态进度弹窗：显示当前阶段文案与百分比进度，任务结束后由调用方主动关闭。
    /// 用于「还原实例」这类一旦被用户操作打断就会损坏现场的长任务。
    /// </summary>
    public partial class MyMsgProgress
    {
        private readonly int Uuid = ModBase.GetUuid();
        private bool IsFinished;
        private bool IsLoadedUi;
        private double PendingPercent;
        private string PendingStage = "准备中……";

        public MyMsgProgress(string Title, string Stage, string Note = null)
        {
            InitializeComponent();
            this.LabTitle.Text = string.IsNullOrEmpty(Title) ? "正在还原实例" : Title;
            if (!string.IsNullOrEmpty(Stage))
            {
                this.PendingStage = Stage;
                this.LabStage.Text = Stage;
            }
            if (!string.IsNullOrEmpty(Note))
                this.LabNote.Text = Note;
            this.ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);
            this.BarBack.SizeChanged += (_, __) => ApplyPercent(PendingPercent);
            base.Loaded += Load;
        }

        private void Load(object sender, EventArgs e)
        {
            try
            {
                IsLoadedUi = true;
                ApplyPercent(PendingPercent);
                if (PendingStage is not null)
                    this.LabStage.Text = PendingStage;
                this.Opacity = 0d;
                ModAnimation.AniStart(new[]
                {
                    ModAnimation.AaOpacity(this, 1d, 120, 60),
                    ModAnimation.AaDouble(Frame => this.TransformPos.Y = this.TransformPos.Y + Convert.ToDouble(Frame), 0d - this.TransformPos.Y, 300, 60, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaDouble(Frame => this.TransformRotate.Angle = this.TransformRotate.Angle + Convert.ToDouble(Frame), -this.TransformRotate.Angle, 300, 60, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak))
                }, "MyMsgProgress " + Uuid);
                Logger.Info($"进度弹窗：{this.LabTitle.Text}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "进度弹窗加载失败", LogBehavior.Toast);
            }
        }

        /// <summary>由后台线程调用，更新阶段文案与进度（0~1）。</summary>
        public void Report(double Percent, string Stage)
        {
            if (IsFinished)
                return;
            if (Stage is not null)
                PendingStage = Stage;
            if (Percent >= 0d)
                PendingPercent = Math.Min(1d, Math.Max(PendingPercent, Percent));
            ModBase.RunInUi(() =>
            {
                if (IsFinished)
                    return;
                ApplyPercent(PendingPercent);
                if (PendingStage is not null)
                    this.LabStage.Text = PendingStage;
            });
        }

        private void ApplyPercent(double Percent)
        {
            Percent = Math.Min(1d, Math.Max(0d, Percent));
            double Total = this.BarBack.ActualWidth;
            if (Total > 0d)
            {
                this.BarFill.Width = Math.Max(2d, Total * Percent);
            }
            this.LabPercent.Text = ((int)Math.Round(Percent * 100d)) + "%";
        }

        /// <summary>关闭弹窗并释放模态锁。重复调用安全。</summary>
        public void Finish()
        {
            if (IsFinished)
                return;
            IsFinished = true;
            ModBase.RunInUi(() =>
            {
                try
                {
                    System.Windows.Interop.ComponentDispatcher.PopModal();
                }
                catch (Exception)
                {
                    // 模态栈已为空时忽略，不影响关闭
                }
                ModAnimation.AniStart(new[]
                {
                    ModAnimation.AaOpacity(this, -this.Opacity, (int)Math.Round(ModAnimation.AniPopupHideTime * 0.4d), 20),
                    ModAnimation.AaCode(() => { if (this.Parent is not null) ((Grid)this.Parent).Children.Remove(this); }, After: true)
                }, "MyMsgProgressClose " + Uuid);
                Logger.Info("进度弹窗已关闭");
            });
        }
    }
}
