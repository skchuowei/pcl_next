using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL
{
    public partial class PageLoginNide
    {

        public PageLoginNide()
        {
            InitializeComponent();
            this.ComboName.TextChanged += (p1, p2) => ComboName_TextChanged((object)p1, (TextChangedEventArgs)p2);
            this.TextPass.PasswordChanged += (p1, p2) => TextPass_PasswordChanged((object)p1, (RoutedEventArgs)p2);
            this.ComboName.SelectionChanged += (p1, p2) => ComboName_SelectionChanged((MyComboBox)p1, (SelectionChangedEventArgs)p2);
            this.CheckRemember.Change += (p1, p2) => CheckBoxChange((MyCheckBox)p1, (object)p2);
            this.ComboName.TextChanged += (p1, p2) => ComboName_TextChanged((object)p1, (TextChangedEventArgs)p2);
            this.BtnLink.Click += (p1, p2) => Btn_Click((object)p1, (EventArgs)p2);
        }
        private bool IsFirstLoad = true;
        /// <summary>
    /// 刷新页面显示的所有信息。
    /// </summary>
        public void Reload(bool KeepInput)
        {
            // 记住密码
            this.CheckRemember.Checked = Settings.Get<bool>("LoginRemember");
            if (KeepInput && !IsFirstLoad) // 避免第一次就以 KeepInput 的方式加载，导致文本框里没东西
            {
                // 保留输入，只刷新下拉框列表
                string Input = this.ComboName.Text;
                this.ComboName.ItemsSource = string.IsNullOrEmpty(Settings.Get<string>("LoginNideEmail")) ? null : Settings.Get<string>("LoginNideEmail").ToString().Split("¨");
                this.ComboName.Text = Input;
            }
            // 不保留输入，刷新列表后自动选择第一项
            else if (string.IsNullOrEmpty(Settings.Get<string>("LoginNideEmail")))
            {
                this.ComboName.ItemsSource = (IEnumerable)null;
            }
            else
            {
                this.ComboName.ItemsSource = Settings.Get<string>("LoginNideEmail").Split("¨");
                this.ComboName.Text = Settings.Get<string>("LoginNideEmail").BeforeFirst("¨");
                if (Settings.Get<bool>("LoginRemember"))
                    this.TextPass.Password = Settings.Get<string>("LoginNidePass").BeforeFirst("¨").Trim();
            }
            IsFirstLoad = false;
        }
        /// <summary>
    /// 获取当前页面的登录信息。
    /// </summary>
        public static ModLaunch.McLoginServer GetLoginData()
        {
            string Server = ModMinecraft.McInstanceSelected == null ? Settings.Get<string>("CacheNideServer") : Settings.Get<string>("VersionServerNide", Instance: ModMinecraft.McInstanceSelected);
            if (ModMain.FrmLoginNide is null)
            {
                return new ModLaunch.McLoginServer(ModLaunch.McLoginType.Nide) { Token = "Nide", UserName = "", Password = "", Description = "统一通行证", Type = ModLaunch.McLoginType.Nide, BaseUrl = "https://auth.mc-user.com:233/" + Server + "/authserver" };
            }
            else
            {
                return new ModLaunch.McLoginServer(ModLaunch.McLoginType.Nide) { Token = "Nide", UserName = ModMain.FrmLoginNide.ComboName.Text.Replace("¨", "").Trim(), Password = ModMain.FrmLoginNide.TextPass.Password.Replace("¨", "").Trim(), Description = "统一通行证", Type = ModLaunch.McLoginType.Nide, BaseUrl = "https://auth.mc-user.com:233/" + Server + "/authserver" };
            }
        }
        /// <summary>
    /// 当前页面的登录信息是否有效。
    /// </summary>
        public static string IsVaild(ModLaunch.McLoginServer LoginData)
        {
            if (string.IsNullOrEmpty(LoginData.UserName))
                return "账号不能为空！";
            if (string.IsNullOrEmpty(LoginData.Password))
                return "密码不能为空！";
            return "";
        }
        public string IsVaild()
        {
            return IsVaild(GetLoginData());
        }

        // 保存输入信息
        private void ComboName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(((dynamic)sender).Text, "", false)))
                this.TextPass.Password = "";
            if (ModAnimation.AniControlEnabled == 0)
                Settings.Set("CacheNideAccess", "");  // 迫使其不进行 Validate
        }
        private void TextPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ModAnimation.AniControlEnabled == 0)
                Settings.Set("CacheNideAccess", "");
        }
        private void ComboName_SelectionChanged(MyComboBox sender, SelectionChangedEventArgs e)
        {
            if (sender.SelectedIndex == -1 || !Settings.Get<bool>("LoginRemember"))
            {
                this.TextPass.Password = "";
            }
            else
            {
                this.TextPass.Password = Settings.Get<string>("LoginNidePass").Split("¨")[sender.SelectedIndex].Trim();
            }
        }
        private void CheckBoxChange(MyCheckBox sender, object e)
        {
            if (ModAnimation.AniControlEnabled == 0)
                Settings.Set(Conversions.ToString(sender.Tag), sender.Checked);
        }

        // 链接处理
        private void ComboName_TextChanged()
        {
            this.BtnLink.Content = string.IsNullOrEmpty(this.ComboName.Text) ? "注册账号" : "找回密码";
        }
        private void Btn_Click(object sender, EventArgs e)
        {
            if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(this.BtnLink.Content, "注册账号", false)))
            {
                ModBase.OpenWebsite("https://login.mc-user.com:233/" + Settings.Get<string>("VersionServerNide", Instance: ModMinecraft.McInstanceSelected) + "/register");
            }
            else
            {
                ModBase.OpenWebsite("https://login.mc-user.com:233/account/login");
            }
        }

    }
}