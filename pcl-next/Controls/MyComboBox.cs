using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public class MyComboBox : ComboBox, ISettingControl
    {
        public event TextChangedEventHandler TextChanged;

        public delegate void TextChangedEventHandler(object sender, TextChangedEventArgs e);

        // 基础
        public int Uuid = ModBase.GetUuid();
        private MyTextBox TextBox;
        public string HintText { get; set; } = "";

        public MyComboBox()
        {
            _Text = (string)SelectedItem;
            PreviewMouseLeftButtonDown += MyComboBox_PreviewMouseLeftButtonDown;
            PreviewMouseLeftButtonUp += MyComboBox_PreviewMouseLeftButtonUp;
            MouseLeave += MyComboBox_PreviewMouseLeftButtonUp;
            IsEnabledChanged += (_, __) => RefreshColor();
            MouseEnter += (_, __) => RefreshColor();
            MouseLeave += (_, __) => RefreshColor();
            PreviewMouseLeftButtonDown += (_, __) => RefreshColor();
            PreviewMouseLeftButtonUp += (_, __) => RefreshColor();
            GotKeyboardFocus += (_, __) => RefreshColor();
            DropDownOpened += MyComboBox_DropDownOpened;
            DropDownClosed += MyComboBox_DropDownClosed;
            TextChanged += MyComboBox_TextChanged;
            SelectionChanged += MyComboBox_SelectionChanged;
            SelectionChanged += (_, __) => SaveSetting();
        }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (!IsEditable)
                return;
            try
            {
                TextBox = (MyTextBox)Template.FindName("PART_EditableTextBox", this);
                TextBox.AddHandler(LostFocusEvent, new RoutedEventHandler((_, __) => RefreshColor()));
                TextBox.ChangedEventList.Add(new RoutedEventHandler((sender, e) => TextChanged?.Invoke(sender, (TextChangedEventArgs)e)));
                TextBox.Tag = Tag; // 有时需要用文本框的 Tag 来写入设置
                if (string.IsNullOrEmpty(Text))
                {
                    TextBox.Text = _Text;
                }
                else
                {
                    TextChanged?.Invoke(this, null);
                }
                if (HintText.Length > 0)
                    TextBox.HintText = HintText;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"初始化可编辑文本框失败（{Name ?? ""}）");
            }
        }
        private string _Text;
        public new string Text
        {
            get
            {
                if (IsEditable)
                {
                    if (TextBox is null)
                    {
                        return _Text ?? "";
                    }
                    else
                    {
                        return TextBox.Text ?? "";
                    }
                }
                else
                {
                    return (SelectedItem ?? "").ToString();
                }
            }
            set
            {
                if (IsEditable)
                {
                    if (TextBox == null)
                    {
                        _Text = value;
                    }
                    else
                    {
                        TextBox.Text = value;
                    }
                }
                else
                {
                    throw new NotSupportedException("该 ComboBox 不支持修改文本。");
                }
            }
        }

        // 鼠标按下接口
        private bool IsMouseDown = false;
        private void MyComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            IsMouseDown = true;
        }
        private void MyComboBox_PreviewMouseLeftButtonUp(object sender, EventArgs e)
        {
            IsMouseDown = false;
        }

        // 指向动画
        public void RefreshColor()
        {
            // 判断当前颜色
            string ForeColorName;
            string BackColorName;
            int Time;
            if (IsEnabled)
            {
                if (Conversions.ToBoolean(IsMouseDown || IsDropDownOpen || IsEditable && ((dynamic)Template.FindName("PART_EditableTextBox", this)).IsFocused))
                {
                    ForeColorName = "ColorBrush3";
                    BackColorName = "ColorBrush7";
                    Time = 10;
                }
                else if (IsMouseOver)
                {
                    ForeColorName = "ColorBrush4";
                    BackColorName = "ColorBrush7";
                    Time = 100;
                }
                else
                {
                    ForeColorName = "ColorBrushBg0";
                    BackColorName = "ColorBrushHalfWhite";
                    Time = 100;
                }
            }
            else
            {
                ForeColorName = "ColorBrushGray5";
                BackColorName = "ColorBrushGray6";
                Time = 200;
            }
            // 触发颜色动画
            if (IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
            {
                // 有动画
                ModAnimation.AniStart(new[] { ModAnimation.AaColor(this, ForegroundProperty, ForeColorName, Time), ModAnimation.AaColor(this, BackgroundProperty, BackColorName, Time) }, "MyComboBox Color " + Uuid);
            }
            else
            {
                // 无动画
                ModAnimation.AniStop("MyComboBox Color " + Uuid);
                SetResourceReference(ForegroundProperty, ForeColorName);
                SetResourceReference(BackgroundProperty, BackColorName);
            }
        }

        private double RealWidth; // 由于下拉框 Popup 宽度与 Width 一致，故不能为 NaN（Auto）
        private void MyComboBox_DropDownOpened(object sender, EventArgs e)
        {
            RealWidth = Width;
            Width = ActualWidth;
            // 将下拉菜单透明度与主窗口同步
            try
            {
                ((Grid)Template.FindName("PanPopup", this)).Opacity = ModMain.FrmMain.Opacity;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "设置下拉框透明度失败");
            }
        }
        private void MyComboBox_DropDownClosed(object sender, EventArgs e)
        {
            Width = RealWidth;
        }

        // 修复 WPF Bug：下拉框文本修改后，依然误认为还选择着此前的选项，导致再次点击该选项时内容不变
        private bool IsTextChanging = false;
        private void MyComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsTextChanging || !IsEditable)
                return;
            if (SelectedItem is null || (Text ?? "") == (SelectedItem.ToString() ?? ""))
                return;
            string RawText = Text;
            int RawSelectionStart = TextBox.SelectionStart;
            IsTextChanging = true;
            SelectedItem = null;
            Text = RawText;
            TextBox.SelectionStart = RawSelectionStart;
            IsTextChanging = false;
        }

        public ContentPresenter ContentPresenter
        {
            get
            {
                return (ContentPresenter)Template.FindName("PART_Content", this);
            }
        }

        private void MyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && ModAnimation.AniControlEnabled == 0)
                this.RaiseCustomEvent();
        }

        #region 设置

        private void RefreshSetting(string NewValue)
        {
            var TargetItem = Items.OfType<DependencyObject>().FirstOrDefault(i => ModBase.Val((object)SettingService.GetValue(i) ?? -1) == Conversions.ToDouble(NewValue));
            if (TargetItem is not null)
            {
                SelectedItem = TargetItem;
            }
            else if (Items.Count > Conversions.ToDouble(NewValue))
            {
                SelectedIndex = Conversions.ToInteger(NewValue);
            }
            else
            {
                Logger.Warn($"尝试显示设置 {SettingService.GetKey(this)}，但没有找到设置值 {NewValue} 的对应项");
                SelectedIndex = -1;
                SelectedItem = null;
            }
        }

        void ISettingControl.RefreshSetting(string NewValue) => RefreshSetting(NewValue);

        private string GetCurrentSetting()
        {
            if (SelectedItem is null)
                return null;
            if (SelectedItem is DependencyObject)
            {
                return Conversions.ToString((object)SettingService.GetValue((DependencyObject)SelectedItem) ?? SelectedIndex);
            }
            else
            {
                return SelectedIndex.ToString();
            }
        }

        string ISettingControl.GetCurrentSetting() => GetCurrentSetting();

        private void SaveSetting()
        {
            if (SelectedItem is null)
                return;
            SettingService.SaveSetting(this);
        }

        #endregion

    }
}