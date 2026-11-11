using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{

    [ContentProperty("Inlines")]
    public partial class MyCheckBox : ISettingControl
    {

        // 基础

        public int Uuid = ModBase.GetUuid();
        /// <summary>
    /// 复选框勾选状态改变。
    /// </summary>
    /// <param name="user">是否为用户手动改变的勾选状态。</param>
        public event ChangeEventHandler Change;

        public delegate void ChangeEventHandler(object sender, bool user);
        public event PreviewChangeEventHandler PreviewChange;

        public delegate void PreviewChangeEventHandler(object sender, ModBase.RouteEventArgs e);

        // 自定义属性
        public bool Checked
        {
            get
            {
                return Conversions.ToBoolean(this.GetValue(CheckedProperty));
            }
            set
            {
                SetChecked(value, false);
            }
        }
        // 在使用 XAML 设置 Checked 属性时，不会触发 Checked_Set 方法，所以需要在这里手动触发 UI 改变
        public static readonly DependencyProperty CheckedProperty = DependencyProperty.Register("Checked", typeof(bool), typeof(MyCheckBox), new PropertyMetadata(false, (d, e) => { if (!((dynamic)d).IsLoaded) ((dynamic)d).SyncUI(); }));

        private const int AnimationTimeOfCheck = 150; // 勾选状态变更动画长度

        public MyCheckBox()
        {
            InitializeComponent();
            this.MouseLeftButtonUp += (_, __) => Checkbox_MouseUp();
            this.MouseLeftButtonDown += (_, __) => Checkbox_MouseDown();
            this.MouseLeave += (_, __) => Checkbox_MouseLeave();
            this.IsEnabledChanged += (_, __) => Checkbox_IsEnabledChanged();
            this.MouseEnter += (_, __) => Checkbox_MouseEnterAnimation();
            this.MouseLeave += (_, __) => Checkbox_MouseLeaveAnimation();
            Change += (_, __) => SaveSetting();
        }
        /// <summary>
    /// 手动设置 Checked 属性。
    /// </summary>
    /// <param name="value">新的 Checked 属性。</param>
    /// <param name="user">是否由用户引发。</param>
        public void SetChecked(bool value, bool user)
        {
            try
            {
                if (value == Checked)
                    return;

                // Preview 事件
                if (value && user)
                {
                    var e = new ModBase.RouteEventArgs(user);
                    PreviewChange?.Invoke(this, e);
                    if (e.Handled)
                    {
                        MouseDowned = true;
                        Checkbox_MouseLeave();
                        MouseDowned = false;
                        return;
                    }
                }

                this.SetValue(CheckedProperty, value);
                if (this.IsLoaded)
                    Change?.Invoke(this, user);

                // 更改动画
                SyncUI();
                this.RaiseCustomEvent();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "设置 Checked 失败");
            }
        }
        private void SyncUI()
        {
            if (ModAnimation.AniControlEnabled == 0 && this.IsLoaded) // 防止默认属性变更触发动画
            {
                AllowMouseDown = false;
                if (Checked)
                {
                    // 由无变有
                    ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeBorder, 12d - this.ShapeBorder.Width, AnimationTimeOfCheck, Ease: new ModAnimation.AniEaseOutFluent(), Absolute: true), ModAnimation.AaScaleTransform(this.ShapeCheck, 1d - ((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleX, AnimationTimeOfCheck * 2, (int)Math.Round(AnimationTimeOfCheck * 0.7d), new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)), ModAnimation.AaScale(this.ShapeBorder, 6d, AnimationTimeOfCheck * 2, (int)Math.Round(AnimationTimeOfCheck * 0.7d), new ModAnimation.AniEaseOutBack(), Absolute: true) }, "MyCheckBox Scale " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeBorder, Border.BorderBrushProperty, this.IsEnabled ? this.IsMouseOver ? "ColorBrush3" : "ColorBrush2" : "ColorBrushGray5", AnimationTimeOfCheck) }, "MyCheckBox BorderColor " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaCode(() => AllowMouseDown = true, AnimationTimeOfCheck * 2) }, "MyCheckBox AllowMouseDown " + Uuid);
                }
                else
                {
                    // 由有变无
                    ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeBorder, 12d - this.ShapeBorder.Width, AnimationTimeOfCheck, Ease: new ModAnimation.AniEaseOutFluent(), Absolute: true), ModAnimation.AaScaleTransform(this.ShapeCheck, -((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleX, (int)Math.Round(AnimationTimeOfCheck * 0.9d), Ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaScale(this.ShapeBorder, 6d, AnimationTimeOfCheck * 2, (int)Math.Round(AnimationTimeOfCheck * 0.7d), new ModAnimation.AniEaseOutBack(), Absolute: true) }, "MyCheckBox Scale " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeBorder, Border.BorderBrushProperty, this.IsEnabled ? this.IsMouseOver ? "ColorBrush3" : "ColorBrush1" : "ColorBrushGray5", AnimationTimeOfCheck) }, "MyCheckBox BorderColor " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaCode(() => AllowMouseDown = true, AnimationTimeOfCheck * 2) }, "MyCheckBox AllowMouseDown " + Uuid);
                }
            }
            else
            {
                // 不使用动画
                ModAnimation.AniStop("MyCheckBox Scale " + Uuid);
                ModAnimation.AniStop("MyCheckBox BorderColor " + Uuid);
                ModAnimation.AniStop("MyCheckBox AllowMouseDown " + Uuid);
                if (Checked)
                {
                    ((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleX = 1d;
                    ((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleY = 1d;
                    this.ShapeBorder.SetResourceReference(Border.BorderBrushProperty, this.IsEnabled ? "ColorBrush2" : "ColorBrushGray5");
                }
                else
                {
                    ((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleX = 0d;
                    ((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleY = 0d;
                    this.ShapeBorder.SetResourceReference(Border.BorderBrushProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5");
                }
            }
        }

        public InlineCollection Inlines
        {
            get
            {
                return this.LabText.Inlines;
            }
        }
        public string Text
        {
            get
            {
                return Conversions.ToString(this.GetValue(TextProperty));
            }
            set
            {
                this.SetValue(TextProperty, value);
            }
        } // 内容
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyCheckBox), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (!(sender == null)) ((MyCheckBox)sender).LabText.Text = Conversions.ToString(e.NewValue); })));

        // 点击事件

        private bool MouseDowned = false;
        private bool AllowMouseDown = true;
        private void Checkbox_MouseUp()
        {
            if (!MouseDowned)
                return;
            Logger.Info($"按下复选框（{!Checked}）：{Text}");
            MouseDowned = false;
            SetChecked(!Checked, true);
            ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Border.BackgroundProperty, "ColorBrushHalfWhite", 100), "MyCheckBox Background " + Uuid);
        }
        private void Checkbox_MouseDown()
        {
            if (!AllowMouseDown)
                return;
            MouseDowned = true;
            this.Focus();
            ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Border.BackgroundProperty, "ColorBrushBg1", 100), "MyCheckBox Background " + Uuid);
            if (Checked)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeBorder, 16.5d - this.ShapeBorder.Width, 1000, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong), Absolute: true), ModAnimation.AaScaleTransform(this.ShapeCheck, 0.9d - ((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleX, 1000, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)) }, "MyCheckBox Scale " + Uuid);
            }
            else
            {
                ModAnimation.AniStart(ModAnimation.AaScale(this.ShapeBorder, 16.5d - this.ShapeBorder.Width, 1000, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong), Absolute: true), "MyCheckBox Scale " + Uuid);
            }
        }
        private void Checkbox_MouseLeave()
        {
            if (!MouseDowned)
                return;
            MouseDowned = false;
            ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Border.BackgroundProperty, "ColorBrushHalfWhite", 100), "MyCheckBox Background " + Uuid);
            if (Checked)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeBorder, 18d - this.ShapeBorder.Width, 400, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong), Absolute: true), ModAnimation.AaScaleTransform(this.ShapeCheck, 1d - ((ScaleTransform)this.ShapeCheck.RenderTransform).ScaleX, 500, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)) }, "MyCheckBox Scale " + Uuid);
            }
            else
            {
                ModAnimation.AniStart(ModAnimation.AaScale(this.ShapeBorder, 18d - this.ShapeBorder.Width, 400, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong), Absolute: true), "MyCheckBox Scale " + Uuid);
            }
        }

        // 指向动画

        private const int AnimationTimeOfMouseIn = 100;
        private const int AnimationTimeOfMouseOut = 200;
        private void Checkbox_IsEnabledChanged()
        {
            if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
            {
                // 有动画
                if (this.IsEnabled)
                {
                    // 可用
                    Checkbox_MouseLeaveAnimation();
                }
                else
                {
                    // 不可用
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeBorder, Border.BorderBrushProperty, ModSecret.ColorGray5 - this.ShapeBorder.BorderBrush, AnimationTimeOfMouseOut) }, "MyCheckBox BorderColor " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, ModSecret.ColorGray5 - this.LabText.Foreground, AnimationTimeOfMouseOut) }, "MyCheckBox TextColor " + Uuid);
                }
            }
            else
            {
                // 无动画
                ModAnimation.AniStop("MyCheckBox TextColor " + Uuid);
                ModAnimation.AniStop("MyCheckBox BorderColor " + Uuid);
                this.LabText.SetResourceReference(TextBlock.ForegroundProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5");
                this.ShapeBorder.SetResourceReference(Border.BorderBrushProperty, this.IsEnabled ? Checked ? "ColorBrush2" : "ColorBrush1" : "ColorBrushGray5");
            }
        }
        private void Checkbox_MouseEnterAnimation()
        {
            ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseIn) }, "MyCheckBox TextColor " + Uuid);
            ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeBorder, Border.BorderBrushProperty, "ColorBrush3", AnimationTimeOfMouseIn) }, "MyCheckBox BorderColor " + Uuid);
        }
        private void Checkbox_MouseLeaveAnimation()
        {
            if (!this.IsEnabled)
                return; // MouseLeave 比 IsEnabledChanged 后执行，所以如果自定义事件修改了 IsEnabled，将导致显示错误
            ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5", AnimationTimeOfMouseOut) }, "MyCheckBox TextColor " + Uuid);
            ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeBorder, Border.BorderBrushProperty, this.IsEnabled ? Checked ? "ColorBrush2" : "ColorBrush1" : "ColorBrushGray5", AnimationTimeOfMouseOut) }, "MyCheckBox BorderColor " + Uuid);
        }

        #region 设置

        private void RefreshSetting(string NewValue)
        {
            Checked = bool.Parse(NewValue);
        }

        void ISettingControl.RefreshSetting(string NewValue) => RefreshSetting(NewValue);

        private string GetCurrentSetting()
        {
            return Checked.ToString();
        }

        string ISettingControl.GetCurrentSetting() => GetCurrentSetting();

        private void SaveSetting()
        {
            SettingService.SaveSetting(this);
        }

        #endregion

    }
}