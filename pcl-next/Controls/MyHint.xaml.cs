using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    [ContentProperty("Inlines")]
    public partial class MyHint
    {
        public int Uuid = ModBase.GetUuid();

        // 边框
        public bool HasBorder
        {
            get
            {
                return this.BorderThickness.Top > 0d;
            }
            set
            {
                if (value)
                {
                    this.BorderThickness = new Thickness(3d, ModBase.GetWPFSize(1d), ModBase.GetWPFSize(1d), ModBase.GetWPFSize(1d));
                }
                else
                {
                    this.BorderThickness = new Thickness(3d, 0d, 0d, 0d);
                }
            }
        }

        // 配色
        public enum Themes
        {
            Blue = 0,
            Red = 1,
            Yellow = 2
        }
        public Themes Theme
        {
            get
            {
                return _ColorType;
            }
            set
            {
                _ColorType = value;
                UpdateUI();
            }
        }
        private Themes _ColorType = Themes.Red;

        public MyHint()
        {
            InitializeComponent();
            this.BtnClose.Click += (p1, p2) => BtnClose_Click((object)p1, (EventArgs)p2);
            this.Loaded += (_, __) => UpdateUI();
            this.Loaded += MyHint_Loaded;
            this.MouseLeftButtonUp += MyHint_MouseUp;
            this.MouseLeftButtonDown += MyHint_MouseDown;
            this.MouseLeave += (_, __) => MyHint_MouseLeave();
        }
        private void UpdateUI()
        {
            switch (Theme)
            {
                case Themes.Blue:
                    {
                        this.Background = new ModBase.MyColor("#d9ecff");
                        this.BorderBrush = new ModBase.MyColor("#1172D4");
                        this.LabText.Foreground = new ModBase.MyColor("#0F64B8");
                        this.BtnClose.Foreground = new ModBase.MyColor("#0F64B8");
                        break;
                    }
                case Themes.Red:
                    {
                        this.Background = new ModBase.MyColor("#ffdddf");
                        this.BorderBrush = new ModBase.MyColor("#d82929");
                        this.LabText.Foreground = new ModBase.MyColor("#bf0b0b");
                        this.BtnClose.Foreground = new ModBase.MyColor("#bf0b0b");
                        break;
                    }
                case Themes.Yellow:
                    {
                        this.Background = new ModBase.MyColor("#ffebd7");
                        this.BorderBrush = new ModBase.MyColor("#f57a00");
                        this.LabText.Foreground = new ModBase.MyColor("#d86c00");
                        this.BtnClose.Foreground = new ModBase.MyColor("#d86c00");
                        break;
                    }
            }
        }
        [Obsolete("IsWarn 已过时。请换用 Theme 属性。")]
        public bool IsWarn
        {
            get
            {
                return Theme == Themes.Red;
            }
            set
            {
                Theme = value ? Themes.Red : Themes.Blue;
            }
        }
        public static readonly DependencyProperty IsWarnProperty = DependencyProperty.Register("IsWarn", typeof(bool), typeof(MyHint), new PropertyMetadata(true, (d, e) => ((dynamic)d).Theme = Conversions.ToBoolean(e.NewValue) ? Themes.Red : Themes.Blue));

        // 文本
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
        }
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MyHint), new PropertyMetadata("", (d, e) => ((dynamic)d).LabText.Text = Conversions.ToString(e.NewValue)));

        // 关闭按钮
        public bool CanClose
        {
            get
            {
                return this.BtnClose.Visibility == Visibility.Visible;
            }
            set
            {
                this.BtnClose.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        public string RelativeSetup { get; set; } = "";
        private void MyHint_Loaded(object sender, RoutedEventArgs e)
        {
            if (CanClose && Settings.Get<bool>(RelativeSetup))
                this.Visibility = Visibility.Collapsed;
        }
        private void BtnClose_Click(object sender, EventArgs e)
        {
            Settings.SetSafe(RelativeSetup, true);
            ModAnimation.AniDispose(this, false);
        }

        // 触发点击事件
        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e);
        private bool IsMouseDown = false;
        private void MyHint_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsMouseDown)
                return;
            IsMouseDown = false;
            Logger.Info($"按下提示条{(string.IsNullOrEmpty(this.Name) ? "" : "：" + this.Name)}");
            Click?.Invoke(sender, e);
            if (e.Handled)
                return;
            e.Handled = true;
            this.RaiseCustomEvent(); // 自定义事件
        }
        private void MyHint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            IsMouseDown = true;
        }
        private void MyHint_MouseLeave()
        {
            IsMouseDown = false;
        }

    }

    public static partial class ModAnimation
    {
        public static void AniDispose(MyHint Control, bool RemoveFromChildren, ParameterizedThreadStart CallBack = null)
        {
            if (!Control.IsHitTestVisible)
                return;
            Control.IsHitTestVisible = false;
            AniStart(new[] {
            AaScaleTransform(Control, -0.08d, 200, Ease: new AniEaseInFluent()),
            AaOpacity(Control, -1, 200, Ease: new AniEaseOutFluent()),
            ModAnimation.AaHeight(Control, -Control.ActualHeight, 150, 100, new AniEaseOutFluent()),
                        ModAnimation.AaCode(() =>
                {
                if (RemoveFromChildren)
                    {
                    ((dynamic)((object)Control.Parent)).Children.Remove(Control);
                    }
                else
                    {
                    Control.Visibility = Visibility.Collapsed;
                }
                if (CallBack is not null)
                        CallBack(Control);
            }, After: true)
        }, "MyCard Dispose " + Control.Uuid);
        }
    }
}