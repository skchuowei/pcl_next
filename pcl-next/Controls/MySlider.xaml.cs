using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{

    public partial class MySlider : ISettingControl
    {

        // 基础

        public int Uuid = ModBase.GetUuid();
        public event ChangeEventHandler Change;

        public delegate void ChangeEventHandler(object sender, bool user);
        public event PreviewChangeEventHandler PreviewChange;

        public delegate void PreviewChangeEventHandler(object sender, ModBase.RouteEventArgs e);

        // 自定义属性

        private int _MaxValue = 100;
        public int MaxValue
        {
            get
            {
                return _MaxValue;
            }
            set
            {
                if (value == _MaxValue)
                    return;
                _MaxValue = value;
                RefreshWidth(null, null);
            }
        }
        private bool ChangeByKey = false;
        private int _Value = 0;
        public int Value
        {
            get
            {
                return _Value;
            }
            set
            {
                try
                {

                    value = value.Clamp(0, MaxValue);
                    if (_Value == value)
                        return;

                    // 触发 Preview 事件，修改新值
                    int OldValue = _Value;
                    _Value = value;
                    if (ModAnimation.AniControlEnabled == 0)
                    {
                        var e = new ModBase.RouteEventArgs(false);
                        PreviewChange?.Invoke(this, e);
                        if (e.Handled)
                        {
                            _Value = OldValue;
                            DragStop();
                            return;
                        }
                    }

                    if (this.IsLoaded && ModAnimation.AniControlEnabled == 0)
                    {
                        if (this.ActualWidth < this.ShapeDot.Width)
                            return;
                        double NewWidth = _Value / (double)MaxValue * (this.ActualWidth - this.ShapeDot.Width);
                        double DeltaProcess = Math.Abs(this.LineFore.Width / (this.ActualWidth - this.ShapeDot.Width) - _Value / (double)MaxValue);
                        double Time = (1d - Math.Pow(1d - DeltaProcess, 3d)) * 300d + (ChangeByKey ? 100 : 0);
                        ModAnimation.AniStart(new[] { ModAnimation.AaWidth(this.LineFore, Math.Max(0d, NewWidth + (NewWidth < 0.5d ? 0d : 0.5d)) - this.LineFore.Width, (int)Math.Round(Time), Ease: Time > 50d ? new global::PCL.ModAnimation.AniEaseOutFluent() : new global::PCL.ModAnimation.AniEaseLinear()), ModAnimation.AaWidth(this.LineBack, Math.Max(0d, this.ActualWidth - this.ShapeDot.Width - NewWidth + (this.ActualWidth - this.ShapeDot.Width - NewWidth < 0.5d ? 0d : 0.5d)) - this.LineBack.Width, (int)Math.Round(Time), Ease: Time > 50d ? new global::PCL.ModAnimation.AniEaseOutFluent() : new global::PCL.ModAnimation.AniEaseLinear()), ModAnimation.AaX(this.ShapeDot, NewWidth - this.ShapeDot.Margin.Left, (int)Math.Round(Time), Ease: Time > 50d ? new global::PCL.ModAnimation.AniEaseOutFluent() : new global::PCL.ModAnimation.AniEaseLinear()) }, "MySlider Progress " + Uuid);
                    }
                    else
                    {
                        RefreshWidth(null, null);
                    }
                    if (ModAnimation.AniControlEnabled == 0)
                        Change?.Invoke(this, false);
                }

                catch (Exception ex)
                {
                    Logger.Error(ex, "滑动条进度改变出错", LogBehavior.Toast);
                }
            }
        }

        public MySlider()
        {
            InitializeComponent();
            this.SizeChanged += RefreshWidth;
            this.MouseLeftButtonDown += DragStart;
            this.IsEnabledChanged += (_, __) => RefreshColor();
            this.MouseEnter += (_, __) => RefreshColor();
            this.MouseLeave += (_, __) => RefreshColor();
            this.MouseEnter += (_, __) => MySlider_MouseEnter();
            this.KeyDown += MySlider_KeyDown;
            Change += (_, __) => SaveSetting();
        }
        private void RefreshWidth(object sender, SizeChangedEventArgs e)
        {
            if (!(e == null))
                this.PanMain.Width = e.NewSize.Width;
            ModAnimation.AniStop("MySlider Progress " + Uuid);
            double NewWidth = _Value / (double)MaxValue * (this.ActualWidth - this.ShapeDot.Width);
            this.LineFore.Width = Math.Max(0d, NewWidth + (NewWidth < 0.5d ? 0d : 0.5d));
            this.LineBack.Width = Math.Max(0d, this.ActualWidth - this.ShapeDot.Width - NewWidth + (this.ActualWidth - this.ShapeDot.Width - NewWidth < 0.5d ? 0d : 0.5d));
            this.ShapeDot.Margin = new Thickness(NewWidth, this.ShapeDot.Margin.Top, this.ShapeDot.Margin.Right, this.ShapeDot.Margin.Bottom);
        }

        // 拖动

        public Delegate GetHintText;
        private void DragStart(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // 防止 ScrollViewer 失焦问题
            ModMain.DragControl = this;
            RefreshColor();
            ModMain.FrmMain.DragDoing();
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.ShapeDot, 1.3d - ((ScaleTransform)this.ShapeDot.RenderTransform).ScaleX, 40, Ease: new ModAnimation.AniEaseOutFluent()) }, "MySlider Scale " + Uuid);
            RefreshPopup();
            ModAnimation.AniStop("MySlider KeyPopup " + Uuid);
        }
        public void DragDoing()
        {
            double Percent = ((Mouse.GetPosition(PanMain).X - ShapeDot.Width / 2d) / (ActualWidth - ShapeDot.Width)).Clamp(0d, 1d);
            int NewValue = (int)Math.Round(Percent * MaxValue);
            if (!(NewValue == Value))
            {
                Value = NewValue;
            }
            RefreshPopup();
        }
        public void DragStop()
        {
            RefreshColor();
            ModAnimation.AniStart(new[] { ModAnimation.AaScaleTransform(this.ShapeDot, 1d - ((ScaleTransform)this.ShapeDot.RenderTransform).ScaleX, 200, Ease: new ModAnimation.AniEaseOutFluent()) }, "MySlider Scale " + Uuid);
            this.Popup.IsOpen = false;
        }
        public void RefreshPopup()
        {
            if (GetHintText is null)
                return;
            this.Popup.IsOpen = true;
            this.TextHint.Text = Conversions.ToString(GetHintText.DynamicInvoke(Value));
            var typeface = new Typeface(this.TextHint.FontFamily, this.TextHint.FontStyle, this.TextHint.FontWeight, this.TextHint.FontStretch);
            var formattedText = new FormattedText(this.TextHint.Text, Thread.CurrentThread.CurrentCulture, this.TextHint.FlowDirection, typeface, this.TextHint.FontSize, this.TextHint.Foreground, (double)ModBase.DPI);
            this.TextHint.Width = formattedText.Width; // 使用手动测量的宽度修复 #1057
        }

        // 指向动画

        private void RefreshColor()
        {
            try
            {

                // 判断当前颜色
                string ForegroundName;
                string DotFillName;
                int AnimationTime;
                if (this.IsEnabled)
                {
                    if (!(ModMain.DragControl == null) && ModMain.DragControl.Equals(this))
                    {
                        ForegroundName = "ColorBrush3";
                        DotFillName = "ColorBrush3";
                        AnimationTime = 40;
                    }
                    else if (this.IsMouseOver)
                    {
                        ForegroundName = "ColorBrush3";
                        DotFillName = "ColorBrush3";
                        AnimationTime = 40;
                    }
                    else
                    {
                        ForegroundName = "ColorBrushBg0";
                        DotFillName = "ColorBrushBg0";
                        AnimationTime = 100;
                    }
                }
                else
                {
                    ForegroundName = "ColorBrushGray5";
                    DotFillName = "ColorBrushGray5";
                    AnimationTime = 200;
                }
                // 触发颜色动画
                if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
                {
                    // 有动画
                    ModAnimation.AniStart(new[] { ModAnimation.AaColor(this, Border.BorderBrushProperty, ForegroundName, AnimationTime), ModAnimation.AaColor(this.ShapeDot, Shape.FillProperty, DotFillName, AnimationTime) }, "MySlider Color " + Uuid);
                }
                else
                {
                    // 无动画
                    ModAnimation.AniStop("MySlider Color " + Uuid);
                    this.SetResourceReference(Border.BorderBrushProperty, ForegroundName);
                    this.ShapeDot.SetResourceReference(Shape.FillProperty, DotFillName);
                }
            }

            catch (Exception ex)
            {
                Logger.Warn(ex, "滑动条颜色改变出错");
            }
        }

        // 按键改变

        public uint ValueByKey { get; set; } = 1U;
        private void MySlider_MouseEnter()
        {
            this.Focus(); // 确保按键能改变值
        }
        private void MySlider_KeyDown(object sender, KeyEventArgs e)
        {
            // 拒绝一边拖动一边用按键改变
            if (ReferenceEquals(this, ModMain.DragControl))
                return;
            // 改变值
            if (e.Key == Key.Left)
            {
                ChangeByKey = true;
                Value = (int)(Value - ValueByKey);
                ChangeByKey = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                ChangeByKey = true;
                Value = (int)(Value + ValueByKey);
                ChangeByKey = false;
                e.Handled = true;
            }
            else
            {
                return;
            }
            // 更新 Popup
            if (GetHintText is not null)
            {
                RefreshPopup();
                ModAnimation.AniStop("MySlider KeyPopup " + Uuid);
                ModAnimation.AniStart(ModAnimation.AaCode(() => this.Popup.IsOpen = false, (int)Math.Round(700d * ModAnimation.AniSpeed)), "MySlider KeyPopup " + Uuid);
            }
        }

        #region 设置

        private void RefreshSetting(string NewValue)
        {
            Value = (int)Math.Round(ModBase.Val(NewValue));
        }

        void ISettingControl.RefreshSetting(string NewValue) => RefreshSetting(NewValue);

        private string GetCurrentSetting()
        {
            return Value.ToString();
        }

        string ISettingControl.GetCurrentSetting() => GetCurrentSetting();

        private void SaveSetting()
        {
            SettingService.SaveSetting(this);
        }

        #endregion

    }
}