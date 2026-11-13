using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL
{

    // 颜色细调控件：A/R/G/B 滑块 + 十六进制输入 + 实时预览 + 重置。
    // 通过 SettingService.Key 关联到开发者配色设置项（留空 = 跟随主题）。
    // BaseBrushKey 指定「留空时取样」的主题画刷，便于从当前配色出发微调。
    public partial class MyColorEdit : Grid, ISettingControl
    {

        public static readonly DependencyProperty BaseBrushKeyProperty = DependencyProperty.Register("BaseBrushKey", typeof(string), typeof(MyColorEdit), new PropertyMetadata(null));
        public string BaseBrushKey
        {
            get => (string)GetValue(BaseBrushKeyProperty);
            set => SetValue(BaseBrushKeyProperty, value);
        }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(MyColorEdit), new PropertyMetadata(null, (o, e) =>
        {
            var Ctl = (MyColorEdit)o;
            if (Ctl.LabTitle is not null)
                Ctl.LabTitle.Text = e.NewValue?.ToString() ?? "";
        }));
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        private bool _IsRefreshing = false;

        public MyColorEdit()
        {
            InitializeComponent();
            SliderA.Change += Slider_Change;
            SliderR.Change += Slider_Change;
            SliderG.Change += Slider_Change;
            SliderB.Change += Slider_Change;
            HexBox.ValidatedTextChanged += HexBox_TextChanged;
            BtnReset.Click += (_, __) => ResetColor();
        }

        private void Slider_Change(object sender, bool user)
        {
            if (_IsRefreshing || !user)
                return;
            ApplyFromSliders();
        }

        private void HexBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_IsRefreshing)
                return;
            string Hex = HexBox.Text.Trim();
            if (string.IsNullOrEmpty(Hex))
            {
                ResetColor();
                return;
            }
            ModBase.MyColor Col;
            try
            {
                Col = new ModBase.MyColor(Hex);
            }
            catch (Exception)
            {
                return; // 非法十六进制，忽略本次输入
            }
            _IsRefreshing = true;
            SliderA.Value = (int)Math.Round(Col.A);
            SliderR.Value = (int)Math.Round(Col.R);
            SliderG.Value = (int)Math.Round(Col.G);
            SliderB.Value = (int)Math.Round(Col.B);
            Preview.Background = new SolidColorBrush((Color)Col);
            _IsRefreshing = false;
            SettingService.SaveSetting(this);
        }

        private void ApplyFromSliders()
        {
            var Col = new ModBase.MyColor(SliderA.Value, SliderR.Value, SliderG.Value, SliderB.Value);
            string Hex = "#" + ToHex(Col.A) + ToHex(Col.R) + ToHex(Col.G) + ToHex(Col.B);
            _IsRefreshing = true;
            HexBox.Text = Hex;
            Preview.Background = new SolidColorBrush((Color)Col);
            _IsRefreshing = false;
            SettingService.SaveSetting(this);
        }

        private static string ToHex(double V) => ModBase.MyColor.ClampToByte(V).ToString("X2");

        private void ResetColor()
        {
            string Key = SettingService.GetKey(this);
            if (Key is not null)
                Settings.Reset(Key);
            RefreshSetting(Settings.Get<string>(Key));
        }

        #region ISettingControl

        public void RefreshSetting(string NewValue)
        {
            _IsRefreshing = true;
            ModBase.MyColor Col;
            if (string.IsNullOrEmpty(NewValue))
            {
                // 跟随主题：以 BaseBrushKey 当前画刷为基准显示，但不写入覆盖
                if (!string.IsNullOrEmpty(BaseBrushKey) && Application.Current.Resources[BaseBrushKey] is SolidColorBrush BaseBrush)
                    Col = (ModBase.MyColor)BaseBrush;
                else
                    Col = new ModBase.MyColor(255d, 0d, 0d, 0d);
                HexBox.Text = "";
            }
            else
            {
                try
                {
                    Col = new ModBase.MyColor(NewValue);
                }
                catch (Exception)
                {
                    Col = new ModBase.MyColor(255d, 0d, 0d, 0d);
                }
                HexBox.Text = NewValue;
            }

            SliderA.Value = (int)Math.Round(Col.A);
            SliderR.Value = (int)Math.Round(Col.R);
            SliderG.Value = (int)Math.Round(Col.G);
            SliderB.Value = (int)Math.Round(Col.B);
            Preview.Background = new SolidColorBrush((Color)Col);
            _IsRefreshing = false;
        }

        public string GetCurrentSetting() => HexBox.Text.Trim();

        #endregion

    }
}
