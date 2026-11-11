using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Shapes;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    [ContentProperty("Inlines")]
    public partial class MyRadioBox : IMyRadio, ISettingControl
    {

        // 基础

        public int Uuid = ModBase.GetUuid();
        public event PreviewCheckEventHandler PreviewCheck;

        public delegate void PreviewCheckEventHandler(object sender, ModBase.RouteEventArgs e);
        public event PreviewChangeEventHandler PreviewChange;

        public delegate void PreviewChangeEventHandler(object sender, ModBase.RouteEventArgs e);
        public event IMyRadio.CheckEventHandler Check;
        public event IMyRadio.ChangedEventHandler Changed;

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
        public static readonly DependencyProperty CheckedProperty = DependencyProperty.Register("Checked", typeof(bool), typeof(MyRadioBox), new PropertyMetadata(false, (d, e) => { if (!((dynamic)d).IsLoaded) ((dynamic)d).SyncUI(); }));

        public MyRadioBox()
        {
            InitializeComponent();
            this.MouseLeftButtonUp += (_, __) => Radiobox_MouseUp();
            this.MouseLeftButtonDown += (_, __) => Radiobox_MouseDown();
            this.MouseLeave += (_, __) => Radiobox_MouseLeave();
            this.IsEnabledChanged += (_, __) => Radiobox_IsEnabledChanged();
            this.MouseEnter += (_, __) => Radiobox_MouseEnterAnimation();
            this.MouseLeave += (_, __) => Radiobox_MouseLeaveAnimation();
            Check += (_, __) => SaveSetting();
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
                // Preview 事件
                if (value && user)
                {
                    var e = new ModBase.RouteEventArgs(user);
                    PreviewCheck?.Invoke(this, e);
                    if (e.Handled)
                    {
                        Radiobox_MouseLeave();
                        return;
                    }
                }

                // 自定义属性基础
                bool IsChanged = false;
                if (this.IsLoaded && !(value == Checked))
                    PreviewChange?.Invoke(this, new ModBase.RouteEventArgs(user));
                if (value != Checked)
                {
                    this.SetValue(CheckedProperty, value);
                    IsChanged = true;
                }

                // 保证最多只有一个单选框选中
                if (this.Parent is null)
                    return;
                var RadioboxList = new List<MyRadioBox>();
                int CheckedCount = 0;
                // 原文为 For Each Control In CType(Parent, Object).Children（VB 后期绑定取 Parent.Children），
                // codeconv 误把 Parent 本身强转成 IEnumerable；此处保留后期绑定，只把结果当集合遍历
                foreach (var Control in (IEnumerable)((dynamic)this.Parent).Children) // 收集控件列表与选中个数
                {
                    if (Control is MyRadioBox)
                    {
                        RadioboxList.Add((MyRadioBox)Control);
                        if (Conversions.ToBoolean(((dynamic)Control).Checked))
                            CheckedCount += 1;
                    }
                }
                if (CheckedCount > 1)
                {
                    if (Checked)
                    {
                        // 如果本控件选中，则取消其他所有控件的选中
                        foreach (MyRadioBox Control in RadioboxList)
                        {
                            if (Control.Checked && !Control.Equals(this))
                                Control.Checked = false;
                        }
                    }
                    else
                    {
                        // 如果本控件未选中，则只保留第一个选中的控件
                        bool FirstChecked = false;
                        foreach (MyRadioBox Control in RadioboxList)
                        {
                            if (Control.Checked)
                            {
                                if (FirstChecked)
                                {
                                    Control.Checked = false; // 修改 Checked 会自动触发 Change 事件，所以不用额外触发
                                }
                                else
                                {
                                    FirstChecked = true;
                                }
                            }
                        }
                    }
                }

                if (IsChanged)
                {
                    // 触发事件
                    if (Checked)
                        Check?.Invoke(this, new ModBase.RouteEventArgs(user));
                    Changed?.Invoke(this, new ModBase.RouteEventArgs(user));
                    this.RaiseCustomEvent();
                    // 更改动画
                    SyncUI();
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "单选框勾选改变错误", LogBehavior.Toast);
            }
        }
        private void SyncUI()
        {
            if (ModAnimation.AniControlEnabled == 0 && this.IsLoaded) // 防止默认属性变更触发动画
            {
                if (Checked)
                {
                    // 由无变有
                    if (this.ShapeDot.Opacity < 0.01d)
                        this.ShapeDot.Opacity = 1d;
                    ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeBorder, 10d - this.ShapeBorder.Width, AnimationTimeOfCheck, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak), Absolute: true), ModAnimation.AaScale(this.ShapeBorder, 8d, AnimationTimeOfCheck * 2, (int)Math.Round(AnimationTimeOfCheck * 0.6d), new ModAnimation.AniEaseOutBack(), Absolute: true) }, "MyRadioBox Border " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeDot, 9d - this.ShapeDot.Width, (int)Math.Round(AnimationTimeOfCheck * 2.6d), Ease: new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak), Absolute: true), ModAnimation.AaOpacity(this.ShapeDot, 1d - this.ShapeDot.Opacity, (int)Math.Round(AnimationTimeOfCheck * 0.5d), (int)Math.Round(AnimationTimeOfCheck * 0.6d)) }, "MyRadioBox Dot " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeBorder, Shape.StrokeProperty, this.IsMouseOver ? "ColorBrush3" : this.IsEnabled ? "ColorBrush2" : "ColorBrushGray5", AnimationTimeOfCheck) }, "MyRadioBox BorderColor " + Uuid);
                }
                else
                {
                    // 由有变无
                    ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeBorder, 18d - this.ShapeBorder.Width, AnimationTimeOfCheck, Ease: new ModAnimation.AniEaseOutFluent(), Absolute: true) }, "MyRadioBox Border " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaScale(this.ShapeDot, -this.ShapeDot.Width, AnimationTimeOfCheck, Ease: new ModAnimation.AniEaseInFluent(), Absolute: true), ModAnimation.AaOpacity(this.ShapeDot, -this.ShapeDot.Opacity, (int)Math.Round(AnimationTimeOfCheck * 0.5d), (int)Math.Round(AnimationTimeOfCheck * 0.2d)) }, "MyRadioBox Dot " + Uuid);
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeBorder, Shape.StrokeProperty, this.IsMouseOver ? "ColorBrush3" : this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5", AnimationTimeOfCheck) }, "MyRadioBox BorderColor " + Uuid);
                }
            }
            else
            {
                // 不使用动画
                ModAnimation.AniStop("MyRadioBox Border " + Uuid);
                ModAnimation.AniStop("MyRadioBox Dot " + Uuid);
                ModAnimation.AniStop("MyRadioBox BorderColor " + Uuid);
                if (Checked)
                {
                    this.ShapeDot.Width = 9d;
                    this.ShapeDot.Height = 9d;
                    this.ShapeDot.Opacity = 1d;
                    this.ShapeDot.Margin = new Thickness(5.5d, 0d, 0d, 0d);
                    this.ShapeBorder.SetResourceReference(Shape.StrokeProperty, this.IsEnabled ? "ColorBrush2" : "ColorBrushGray5");
                }
                else
                {
                    this.ShapeDot.Width = 0d;
                    this.ShapeDot.Height = 0d;
                    this.ShapeDot.Opacity = 0d;
                    this.ShapeDot.Margin = new Thickness(10d, 0d, 0d, 0d);
                    this.ShapeBorder.SetResourceReference(Shape.StrokeProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5");
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
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyRadioBox), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (sender is not null) ((MyRadioBox)sender).LabText.Text = Conversions.ToString(e.NewValue); })));

        // 点击事件

        private bool MouseDowned = false;
        // 上游遗留字段：仅初始化赋值，值从未被读取（VB 原文同样如此），保留以维持原逻辑，故豁免 CS0414
#pragma warning disable CS0414
        private bool AllowMouseDown = true;
#pragma warning restore CS0414
        private void Radiobox_MouseUp()
        {
            if (!MouseDowned)
                return;
            Logger.Info($"按下单选框：{Text}");
            SetChecked(true, true);
            MouseDowned = false;
            ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Shape.FillProperty, "ColorBrushHalfWhite", 100), "MyRadioBox Background " + Uuid);
        }
        private void Radiobox_MouseDown()
        {
            MouseDowned = true;
            this.Focus();
            ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Shape.FillProperty, "ColorBrushBg1", 100), "MyRadioBox Background " + Uuid);
            if (!Checked)
            {
                ModAnimation.AniStart(ModAnimation.AaScale(this.ShapeBorder, 16.5d - this.ShapeBorder.Width, 1000, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong), Absolute: true), "MyRadioBox Border " + Uuid);
            }
        }
        private void Radiobox_MouseLeave()
        {
            if (!MouseDowned)
                return;
            MouseDowned = false;
            ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Shape.FillProperty, "ColorBrushHalfWhite", 100), "MyRadioBox Background " + Uuid);
            if (!Checked)
            {
                ModAnimation.AniStart(ModAnimation.AaScale(this.ShapeBorder, 18d - this.ShapeBorder.Width, 400, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong), Absolute: true), "MyRadioBox Border " + Uuid);
            }
        }

        // 指向动画

        private const int AnimationTimeOfMouseIn = 100; // 鼠标指向动画长度
        private const int AnimationTimeOfMouseOut = 200; // 鼠标指向动画长度
        private const int AnimationTimeOfCheck = 150; // 勾选状态变更动画长度
        private void Radiobox_IsEnabledChanged()
        {
            if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
            {
                // 有动画
                if (this.IsEnabled)
                {
                    // 可用
                    Radiobox_MouseLeaveAnimation();
                }
                else
                {
                    // 不可用
                    ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Shape.StrokeProperty, ModSecret.ColorGray5 - this.ShapeBorder.Stroke, AnimationTimeOfMouseOut), "MyRadioBox BorderColor " + Uuid);
                    ModAnimation.AniStart(ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, ModSecret.ColorGray5 - this.LabText.Foreground, AnimationTimeOfMouseOut), "MyRadioBox TextColor " + Uuid);
                }
            }
            else
            {
                // 无动画
                ModAnimation.AniStop("MyRadioBox BorderColor " + Uuid);
                ModAnimation.AniStop("MyRadioBox TextColor " + Uuid);
                this.LabText.SetResourceReference(TextBlock.ForegroundProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5");
                this.ShapeBorder.SetResourceReference(Shape.StrokeProperty, this.IsEnabled ? Checked ? "ColorBrush2" : "ColorBrush1" : "ColorBrushGray5");
            }
        }
        private void Radiobox_MouseEnterAnimation()
        {
            ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Shape.StrokeProperty, "ColorBrush3", AnimationTimeOfMouseIn), "MyRadioBox BorderColor " + Uuid);
            ModAnimation.AniStart(ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseIn), "MyRadioBox TextColor " + Uuid);
        }
        private void Radiobox_MouseLeaveAnimation()
        {
            if (!this.IsEnabled)
                return; // MouseLeave 比 IsEnabledChanged 后执行，所以如果自定义事件修改了 IsEnabled，将导致显示错误
            if (this.IsLoaded && ModAnimation.AniControlEnabled == 0)
            {
                ModAnimation.AniStart(ModAnimation.AaColor(this.ShapeBorder, Shape.StrokeProperty, this.IsEnabled ? Checked ? "ColorBrush2" : "ColorBrush1" : "ColorBrushGray5", AnimationTimeOfMouseOut), "MyRadioBox BorderColor " + Uuid);
                ModAnimation.AniStart(ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5", AnimationTimeOfMouseOut), "MyRadioBox TextColor " + Uuid);
            }
            else
            {
                ModAnimation.AniStop("MyRadioBox BorderColor " + Uuid);
                ModAnimation.AniStop("MyRadioBox TextColor " + Uuid);
                this.ShapeBorder.SetResourceReference(Shape.StrokeProperty, this.IsEnabled ? Checked ? "ColorBrush2" : "ColorBrush1" : "ColorBrushGray5");
                this.LabText.SetResourceReference(TextBlock.ForegroundProperty, this.IsEnabled ? "ColorBrush1" : "ColorBrushGray5");
            }
        }

        #region 设置

        private void RefreshSetting(string NewValue)
        {
            Checked = (NewValue ?? "") == (SettingService.GetValue(this) ?? "");
        }

        void ISettingControl.RefreshSetting(string NewValue) => RefreshSetting(NewValue);

        private string GetCurrentSetting()
        {
            return Checked ? SettingService.GetValue(this) : null;
        }

        string ISettingControl.GetCurrentSetting() => GetCurrentSetting();

        private void SaveSetting()
        {
            SettingService.SaveSetting(this);
        }

        #endregion

    }
}