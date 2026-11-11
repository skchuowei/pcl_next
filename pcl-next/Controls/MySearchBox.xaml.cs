using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PCL
{
    public partial class MySearchBox : MyCard
    {

        public event TextChangedEventHandler TextChanged;

        public delegate void TextChangedEventHandler(object sender, EventArgs e);

        public MySearchBox()
        {
            Loaded += MySearchBox_Loaded;
            KeyUp += MySearchBox_KeyUp;
        }
        private void MySearchBox_Loaded(object sender, RoutedEventArgs e)
        {
            this.TextBox.Focus();
        }

        // 属性
        public string HintText
        {
            get
            {
                return this.TextBox.HintText;
            }
            set
            {
                this.TextBox.HintText = value;
            }
        }
        public string Text
        {
            get
            {
                return this.TextBox.Text;
            }
            set
            {
                this.TextBox.Text = value;
            }
        }

        private void Text_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(this.TextBox.Text))
            {
                ModAnimation.AniStart(ModAnimation.AaOpacity(this.BtnClear, -this.BtnClear.Opacity, 90), "MySearchBox ClearBtn " + Uuid);
                this.BtnClear.IsHitTestVisible = false;
            }
            else
            {
                ModAnimation.AniStart(ModAnimation.AaOpacity(this.BtnClear, 1d - this.BtnClear.Opacity, 90), "MySearchBox ClearBtn " + Uuid);
                this.BtnClear.IsHitTestVisible = true;
            }
            TextChanged?.Invoke(sender, e);
        }
        private void BtnClear_Click(object sender, EventArgs e)
        {
            this.TextBox.Text = "";
            this.TextBox.Focus();
        }

        private void MySearchBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                this.RaiseCustomEvent();
        }
    }
}