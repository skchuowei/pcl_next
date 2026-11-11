using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeloongCore.Extensions;

namespace PCL
{

    public partial class PageLoginLegacy
    {

        public PageLoginLegacy()
        {
            this.InitializeComponent();
            this.ComboName.PreviewKeyDown += (p1, p2) => ComboName_PreviewKeyDown((object)p1, (System.Windows.Input.KeyEventArgs)p2);
            this.ComboName.TextChanged += (p1, p2) => ComboLegacy_TextChanged((object)p1, (TextChangedEventArgs)p2);
            this.Skin.Click += (p1, p2) => Skin_Click();
            this.Skin.Loader = PageLaunchLeft.SkinLegacy;
            this.Loaded += PageLoginLegacy_Loaded;
        }
        private void PageLoginLegacy_Loaded(object sender, RoutedEventArgs e)
        {
            this.Skin.Loader.Start();
        }

        public bool IsReloaded = false;
        /// <summary>
    /// 刷新页面显示的所有信息。
    /// </summary>
        public void Reload(bool KeepInput)
        {
            if (KeepInput && IsReloaded) // 避免第一次就以 KeepInput 的方式加载，导致文本框里没东西
            {
                // 保留输入，只刷新下拉框列表
                string Input = this.ComboName.Text.Trim();
                this.ComboName.ItemsSource = string.IsNullOrEmpty(Settings.Get<string>("LoginLegacyName")) ? null : Settings.Get<string>("LoginLegacyName").Split("¨");
                this.ComboName.Text = Input;
            }
            // 不保留输入，刷新列表后自动选择第一项
            else if (string.IsNullOrEmpty(Settings.Get<string>("LoginLegacyName")))
            {
                this.ComboName.ItemsSource = (IEnumerable)null;
            }
            else
            {
                this.ComboName.ItemsSource = Settings.Get<string>("LoginLegacyName").ToString().Split("¨");
                this.ComboName.Text = Settings.Get<string>("LoginLegacyName").ToString().BeforeFirst("¨").Trim();
            }
            IsReloaded = true;
        }
        /// <summary>
    /// 获取当前页面的登录信息。
    /// </summary>
        public static ModLaunch.McLoginData GetLoginData()
        {
            string UserName = ModMain.FrmLoginLegacy is null ? "" : ModMain.FrmLoginLegacy.ComboName.Text.Replace("¨", "").Trim();
            return new ModLaunch.McLoginLegacy() { UserName = UserName, SkinType = Settings.Get<int>("LaunchSkinType"), SkinName = Settings.Get<string>("LaunchSkinID") };
        }
        /// <summary>
    /// 当前页面的登录信息是否有效。
    /// </summary>
        public static string IsVaild(ModLaunch.McLoginLegacy LoginData)
        {
            if (string.IsNullOrEmpty(LoginData.UserName.Trim()))
                return "玩家名不能为空！";
            if (LoginData.UserName.Contains("\""))
                return "玩家名不能包含英文引号！";
            if (ModMinecraft.McInstanceSelected is not null && ModMinecraft.McInstanceSelected.Version.Valid && ModMinecraft.McInstanceSelected.Version.Vanilla >= new Version(20, 0, 3) && LoginData.UserName.Trim().Length > 16)
            {
                return "自 1.20.3 起，玩家名至多只能包含 16 个字符！";
            }
            return "";
        }
        public string IsVaild()
        {
            return IsVaild((ModLaunch.McLoginLegacy)GetLoginData());
        }

        private void ComboName_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (this.ComboName.Template.FindName("PART_EditableTextBox", this.ComboName) is not MyTextBox EditableTextBox)
                    return;
                int Index = EditableTextBox.CaretIndex;
                if (Index == this.ComboName.Text.Length || Index == 0)
                    e.Handled = true;
            }
        }
        private void ComboLegacy_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Settings.Get<int>("LaunchSkinType") == 0)
                PageLaunchLeft.SkinLegacy.Start(IsForceRestart: true);
            this.HintChinese.Visibility = this.ComboName.Text.RegexCheck("^[0-9A-Za-z_]*$") ? Visibility.Collapsed : Visibility.Visible;
            ModBase.RunInUi(() => this.PanMain.InvalidateMeasure(), true); // 由于 WPF 的 Bug，它不一定会自动更新 HintChinese 的大小（#6627）
        }
        private void Skin_Click()
        {
            if ((Settings.Get<bool>("UiHiddenPageSetup") || Settings.Get<bool>("UiHiddenSetupLaunch")) && !PageSetupUI.HiddenForceShow)
            {
                ModMain.Hint("启动设置已被禁用！", ModMain.HintType.Red);
            }
            else
            {
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.Setup, FormMain.PageSubType.SetupLaunch);
            } // 切换到皮肤设置页面
        }

    }
}