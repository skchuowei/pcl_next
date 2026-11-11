using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    [ContentProperty("Inlines")]
    public partial class MyRadioButton
    {

        // 基础

        public int Uuid = ModBase.GetUuid();
        public event CheckEventHandler Check;

        public delegate void CheckEventHandler(object sender, bool raiseByMouse);

        // 自定义属性

        public string Logo
        {
            get
            {
                return this.ShapeLogo.Data.ToString();
            }
            set
            {
                this.ShapeLogo.Data = (Geometry)new GeometryConverter().ConvertFromString(value);
            }
        }
        private double _LogoScale = 1d;
        public double LogoScale
        {
            get
            {
                return _LogoScale;
            }
            set
            {
                _LogoScale = value;
                if (!(this.ShapeLogo == null))
                    this.ShapeLogo.RenderTransform = new ScaleTransform() { ScaleX = LogoScale, ScaleY = LogoScale };
            }
        }

        private bool _Checked = false; // 是否选中
        public bool Checked
        {
            get
            {
                return _Checked;
            }
            set
            {
                SetChecked(value, false, true);
            }
        }

        public MyRadioButton()
        {
            this.MouseLeftButtonUp += (_, __) => Radiobox_MouseUp();
            this.MouseLeftButtonDown += (_, __) => Radiobox_MouseDown();
            this.MouseLeave += (_, __) => Radiobox_MouseLeave();
            this.MouseEnter += RefreshColor;
            this.MouseLeave += RefreshColor;
            this.Loaded += RefreshColor;
        }
        /// <summary>
    /// 手动设置 Checked 属性。
    /// </summary>
    /// <param name="value">新的 Checked 属性。</param>
    /// <param name="raiseByMouse">是否由用户引发。</param>
    /// <param name="anime">是否执行动画。</param>
        public void SetChecked(bool value, bool raiseByMouse, bool anime)
        {
            try
            {

                // 自定义属性基础

                bool IsChanged = false;
                if (_Checked != value)
                {
                    _Checked = value;
                    IsChanged = true;
                }

                // 保证只有一个单选框选中

                if (this.Parent is null)
                    return;
                var RadioboxList = new List<MyRadioButton>();
                int CheckedCount = 0;
                // 收集控件列表与选中个数
                foreach (var Control in ((dynamic)(IEnumerable)((object)this.Parent)).Children)
                {
                    if (Control is MyRadioButton)
                    {
                        RadioboxList.Add((MyRadioButton)Control);
                        if (Conversions.ToBoolean(((dynamic)Control).Checked))
                            CheckedCount += 1;
                    }
                }
                // 判断选中情况
                switch (CheckedCount)
                {
                    case 0:
                        {
                            // 没有任何单选框被选中，选择第一个
                            RadioboxList[0].Checked = true;
                            break;
                        }
                    case var @case when @case > 1:
                        {
                            // 选中项目多于 1 个
                            if (Checked)
                            {
                                // 如果本控件选中，则取消其他所有控件的选中
                                foreach (MyRadioButton Control in RadioboxList)
                                {
                                    if (Control.Checked && !Control.Equals(this))
                                        Control.Checked = false;
                                }
                            }
                            else
                            {
                                // 如果本控件未选中，则只保留第一个选中的控件
                                bool FirstChecked = false;
                                foreach (MyRadioButton Control in RadioboxList)
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

                            break;
                        }
                }

                // 更改动画

                if (!IsChanged)
                    return;
                RefreshColor(null, anime);

                // 触发事件
                if (Checked)
                    Check?.Invoke(this, raiseByMouse);
                this.RaiseCustomEvent();
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "单选按钮勾选改变错误", LogBehavior.Toast);
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
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyRadioButton), new PropertyMetadata(new PropertyChangedCallback((sender, e) => { if (sender is not null) ((MyRadioButton)sender).LabText.Text = Conversions.ToString(e.NewValue); })));
        public enum ColorState
        {
            White,
            Highlight
        }
        private ColorState _ColorType = ColorState.White;
        public ColorState ColorType
        {
            get
            {
                return _ColorType;
            }
            set
            {
                _ColorType = value;
                RefreshColor();
            }
        } // 颜色类别

        // 点击事件

        public event PreviewClickEventHandler PreviewClick;

        public delegate void PreviewClickEventHandler(object sender, ModBase.RouteEventArgs e);
        private bool IsMouseDown = false;
        private void Radiobox_MouseUp()
        {
            if (Checked)
                return;
            if (!IsMouseDown)
                return;
            Logger.Info($"按下单选按钮：{Text}");
            IsMouseDown = false;
            var e = new ModBase.RouteEventArgs(true);
            PreviewClick?.Invoke(this, e);
            if (e.Handled)
                return;
            SetChecked(true, true, true);
        }
        private void Radiobox_MouseDown()
        {
            if (Checked)
                return;
            IsMouseDown = true;
            RefreshColor();
        }
        private void Radiobox_MouseLeave()
        {
            IsMouseDown = false;
        }

        // 动画

        private const int AnimationTimeOfMouseIn = 90; // 鼠标指向动画长度
        private const int AnimationTimeOfMouseOut = 150; // 鼠标移出动画长度
        private const int AnimationTimeOfCheck = 120; // 勾选状态变更动画长度
        private void RefreshColor(object obj = null, object e = null)
        {
            try
            {
                if (this.IsLoaded && ModAnimation.AniControlEnabled == 0 && !false.Equals(e)) // 防止默认属性变更触发动画，若强制不执行动画，则 e 为 False
                {

                    switch (ColorType)
                    {
                        case ColorState.White:
                            {
                                if (Checked)
                                {
                                    // 勾选
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrush3", AnimationTimeOfCheck), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfCheck) }, "MyRadioButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, new ModBase.MyColor(255d, 255d, 255d) - this.Background, AnimationTimeOfCheck), "MyRadioButton Color " + Uuid);
                                }
                                else if (IsMouseDown)
                                {
                                    // 按下
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, new ModBase.MyColor(120d, ModSecret.Color8) - this.Background, 60), "MyRadioButton Color " + Uuid);
                                }
                                else if (this.IsMouseOver)
                                {
                                    // 指向
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, new ModBase.MyColor(255d, 255d, 255d) - this.ShapeLogo.Fill, AnimationTimeOfMouseIn), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, new ModBase.MyColor(255d, 255d, 255d) - this.LabText.Foreground, AnimationTimeOfMouseIn) }, "MyRadioButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, new ModBase.MyColor(50d, ModSecret.Color8) - this.Background, AnimationTimeOfMouseIn), "MyRadioButton Color " + Uuid);
                                }
                                else
                                {
                                    // 正常
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, new ModBase.MyColor(255d, 255d, 255d) - this.ShapeLogo.Fill, AnimationTimeOfMouseOut), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, new ModBase.MyColor(255d, 255d, 255d) - this.LabText.Foreground, AnimationTimeOfMouseOut) }, "MyRadioButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, ModSecret.ColorSemiTransparent - this.Background, AnimationTimeOfMouseOut), "MyRadioButton Color " + Uuid);
                                }

                                break;
                            }
                        case ColorState.Highlight:
                            {
                                if (Checked)
                                {
                                    // 勾选
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, new ModBase.MyColor(255d, 255d, 255d) - this.ShapeLogo.Fill, AnimationTimeOfCheck), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, new ModBase.MyColor(255d, 255d, 255d) - this.LabText.Foreground, AnimationTimeOfCheck) }, "MyRadioButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, "ColorBrush3", AnimationTimeOfCheck), "MyRadioButton Color " + Uuid);
                                }
                                else if (IsMouseDown)
                                {
                                    // 按下
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, "ColorBrush6", AnimationTimeOfMouseIn), "MyRadioButton Color " + Uuid);
                                }
                                else if (this.IsMouseOver)
                                {
                                    // 指向
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrush3", AnimationTimeOfMouseIn), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseIn) }, "MyRadioButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, "ColorBrush7", AnimationTimeOfMouseIn), "MyRadioButton Color " + Uuid);
                                }
                                else
                                {
                                    // 正常
                                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this.ShapeLogo, System.Windows.Shapes.Shape.FillProperty, "ColorBrush3", AnimationTimeOfMouseOut), ModAnimation.AaColor(this.LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseOut) }, "MyRadioButton Checked " + Uuid);
                                    ModAnimation.AniStart(ModAnimation.AaColor(this, Border.BackgroundProperty, ModSecret.ColorSemiTransparent - this.Background, AnimationTimeOfMouseOut), "MyRadioButton Color " + Uuid);
                                }

                                break;
                            }
                    }
                }

                else
                {

                    // 不使用动画
                    ModAnimation.AniStop("MyRadioButton Checked " + Uuid);
                    ModAnimation.AniStop("MyRadioButton Color " + Uuid);
                    switch (ColorType)
                    {
                        case ColorState.White:
                            {
                                if (Checked)
                                {
                                    this.Background = new ModBase.MyColor(255d, 255d, 255d);
                                    this.ShapeLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ColorBrush3");
                                    this.LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3");
                                }
                                else
                                {
                                    this.Background = ModSecret.ColorSemiTransparent;
                                    this.ShapeLogo.Fill = new ModBase.MyColor(255d, 255d, 255d);
                                    this.LabText.Foreground = new ModBase.MyColor(255d, 255d, 255d);
                                }

                                break;
                            }
                        case ColorState.Highlight:
                            {
                                if (Checked)
                                {
                                    this.SetResourceReference(Border.BackgroundProperty, "ColorBrush3");
                                    this.ShapeLogo.Fill = new ModBase.MyColor(255d, 255d, 255d);
                                    this.LabText.Foreground = new ModBase.MyColor(255d, 255d, 255d);
                                }
                                else
                                {
                                    this.Background = ModSecret.ColorSemiTransparent;
                                    this.ShapeLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ColorBrush3");
                                    this.LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3");
                                }

                                break;
                            }
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "刷新单选按钮颜色出错");
            }
        }

    }
}