using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL
{

    // 颜色细调控件：A/R/G/B 滑块 + 十六进制输入 + 可视化调色板 + RGB 数值 + 重置。
    // 2026-11-13：按用户裁决取消实时预览——本控件只改「显示值」，不写设置；
    // 由所在页面点「应用」统一保存。ValueChanged 供页面标记未应用的改动。
    // 通过 SettingService.Key 关联开发者配色设置项（留空 = 跟随主题）。
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

        /// <summary>显示值被用户改动（尚未应用）时触发。</summary>
        public event EventHandler ValueChanged;

        private bool _IsRefreshing = false;
        private ModBase.MyColor _PaletteColor;

        public MyColorEdit()
        {
            InitializeComponent();
            SliderA.Change += Slider_Change;
            SliderR.Change += Slider_Change;
            SliderG.Change += Slider_Change;
            SliderB.Change += Slider_Change;
            HexBox.ValidatedTextChanged += HexBox_TextChanged;
            BtnReset.Click += (_, __) => ResetColor();
            BtnPalette.Click += (_, __) => TogglePalette();
            BtnPaletteClose.Click += (_, __) => PanPalette.Visibility = Visibility.Collapsed;
            BtnPaletteUse.Click += (_, __) => UsePaletteColor();
            SliderH.Change += Hsl_Change;
            SliderS.Change += Hsl_Change;
            SliderL.Change += Hsl_Change;
            BuildSwatches();
        }

        #region 主显示值

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
            try
            {
                SetColor(new ModBase.MyColor(Hex));
            }
            catch (Exception)
            {
                // 非法十六进制，忽略本次输入
            }
        }

        private void ApplyFromSliders()
        {
            SetColor(new ModBase.MyColor(SliderA.Value, SliderR.Value, SliderG.Value, SliderB.Value));
        }

        /// <summary>把颜色写入本控件的显示值，并通知页面「有未应用改动」。不写设置。</summary>
        private void SetColor(ModBase.MyColor Col)
        {
            string Hex = "#" + ToHex(Col.A) + ToHex(Col.R) + ToHex(Col.G) + ToHex(Col.B);
            _IsRefreshing = true;
            SliderA.Value = ClampByte(Col.A);
            SliderR.Value = ClampByte(Col.R);
            SliderG.Value = ClampByte(Col.G);
            SliderB.Value = ClampByte(Col.B);
            HexBox.Text = Hex;
            Preview.Background = new SolidColorBrush((Color)Col);
            LabRgb.Text = RgbText(Col) + "  ·  " + Hex;
            _IsRefreshing = false;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string ToHex(double V) => ModBase.MyColor.ClampToByte(V).ToString("X2");
        private static int ClampByte(double V) => (int)Math.Round((double)ModBase.MyColor.ClampToByte(V));
        private static string RgbText(ModBase.MyColor C) =>
            $"RGB({ClampByte(C.R)}, {ClampByte(C.G)}, {ClampByte(C.B)})  ·  A {ClampByte(C.A)}";

        /// <summary>重置本项为「跟随主题」：只清显示值，等页面点应用才写回设置。</summary>
        private void ResetColor()
        {
            RefreshSetting("");
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region 颜色调色板

        /// <summary>12 列色相 × 6 行明度的可视化色板。</summary>
        private void BuildSwatches()
        {
            for (int Row = 0; Row < 6; Row++)
            {
                double L = 15d + Row * 13d;
                for (int Col = 0; Col < 12; Col++)
                {
                    double H = Col * 30d;
                    var C = new ModBase.MyColor();
                    C.FromHSL(H, 85d, L);
                    var Swatch = new Border()
                    {
                        Width = 20,
                        Height = 18,
                        Margin = new Thickness(1),
                        Background = new SolidColorBrush((Color)C),
                        Cursor = Cursors.Hand,
                        ToolTip = $"H {H:0} / S 85 / L {L:0}"
                    };
                    double CapturedH = H, CapturedL = L;
                    Swatch.MouseLeftButtonUp += (_, __) => SelectSwatch(CapturedH, CapturedL);
                    PanSwatches.Children.Add(Swatch);
                }
            }
        }

        private void SelectSwatch(double H, double L)
        {
            _IsRefreshing = true;
            SliderH.Value = (int)Math.Round(H);
            SliderS.Value = 85;
            SliderL.Value = (int)Math.Round(L);
            _IsRefreshing = false;
            ApplyFromHsl();
        }

        private void Hsl_Change(object sender, bool user)
        {
            if (_IsRefreshing || !user)
                return;
            ApplyFromHsl();
        }

        private void ApplyFromHsl()
        {
            var C = new ModBase.MyColor();
            C.FromHSL(SliderH.Value, SliderS.Value, SliderL.Value);
            C.A = SliderA.Value;
            _PaletteColor = C;
            PalettePreview.Background = new SolidColorBrush((Color)C);
            LabPaletteRgb.Text = RgbText(C);
        }

        private void TogglePalette()
        {
            if (PanPalette.Visibility == Visibility.Visible)
            {
                PanPalette.Visibility = Visibility.Collapsed;
                return;
            }
            PanPalette.Visibility = Visibility.Visible;
            // 以本项当前颜色反算 H/S/L，方便从现有配色出发细调
            double[] Hsl = RgbToHsl(new ModBase.MyColor(SliderA.Value, SliderR.Value, SliderG.Value, SliderB.Value));
            _IsRefreshing = true;
            SliderH.Value = (int)Math.Round(Hsl[0]);
            SliderS.Value = (int)Math.Round(Hsl[1]);
            SliderL.Value = (int)Math.Round(Hsl[2]);
            _IsRefreshing = false;
            ApplyFromHsl();
        }

        private void UsePaletteColor()
        {
            SetColor(_PaletteColor);
            PanPalette.Visibility = Visibility.Collapsed;
        }

        /// <summary>RGB → HSL，返回 [H(0-360), S(0-100), L(0-100)]。</summary>
        private static double[] RgbToHsl(ModBase.MyColor C)
        {
            double R = ClampByte(C.R) / 255d, G = ClampByte(C.G) / 255d, B = ClampByte(C.B) / 255d;
            double Max = Math.Max(R, Math.Max(G, B)), Min = Math.Min(R, Math.Min(G, B));
            double L = (Max + Min) / 2d;
            double H = 0d, S = 0d;
            double D = Max - Min;
            if (D > 1e-6d)
            {
                S = D / (1d - Math.Abs(2d * L - 1d));
                if (Max == R)
                    H = 60d * ((G - B) / D % 6d);
                else if (Max == G)
                    H = 60d * ((B - R) / D + 2d);
                else
                    H = 60d * ((R - G) / D + 4d);
                if (H < 0d)
                    H += 360d;
            }
            return new[] { H, S * 100d, L * 100d };
        }

        #endregion

        #region ISettingControl

        public void RefreshSetting(string NewValue)
        {
            _IsRefreshing = true;
            ModBase.MyColor Col;
            string Tail;
            if (string.IsNullOrEmpty(NewValue))
            {
                // 跟随主题：以 BaseBrushKey 当前画刷为基准显示，但不写入覆盖
                if (!string.IsNullOrEmpty(BaseBrushKey) && Application.Current.Resources[BaseBrushKey] is SolidColorBrush BaseBrush)
                    Col = (ModBase.MyColor)BaseBrush;
                else
                    Col = new ModBase.MyColor(255d, 0d, 0d, 0d);
                HexBox.Text = "";
                Tail = "  ·  跟随主题";
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
                Tail = "  ·  " + NewValue;
            }

            SliderA.Value = ClampByte(Col.A);
            SliderR.Value = ClampByte(Col.R);
            SliderG.Value = ClampByte(Col.G);
            SliderB.Value = ClampByte(Col.B);
            Preview.Background = new SolidColorBrush((Color)Col);
            LabRgb.Text = RgbText(Col) + Tail;
            _IsRefreshing = false;
        }

        public string GetCurrentSetting() => HexBox.Text.Trim();

        #endregion

    }
}
