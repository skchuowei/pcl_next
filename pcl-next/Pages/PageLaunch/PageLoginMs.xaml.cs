using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class PageLoginMs
    {

        public PageLoginMs()
        {
            InitializeComponent();
            this.ComboAccounts.SelectionChanged += (p1, p2) => ComboAccounts_SelectionChanged((object)p1, (SelectionChangedEventArgs)p2);
            this.BtnLogin.Click += (p1, p2) => BtnLogin_Click((object)p1, (EventArgs)p2);
        }

        /// <summary>
    /// 刷新页面显示的所有信息。
    /// </summary>
        public void Reload(bool KeepInput)
        {
            int IndexBefore = this.ComboAccounts.SelectedIndex;
            // 刷新下拉框列表
            this.ComboAccounts.Items.Clear();
            this.ComboAccounts.Items.Add(new MyComboBoxItem() { Content = "添加新账号" });
            try
            {
                JObject MsJson = (JObject)Settings.Get<string>("LoginMsJson").DeserializeJson();
                foreach (KeyValuePair<string, JToken> Account in MsJson)
                {
                    MyListItem Item = (MyListItem)((DataTemplate)this.FindResource("ComboBoxItemTemplateWithDelete")).LoadContent();
                    Item.Tag = Account.Value.ToString();
                    Item.Title = Account.Key;
                    Item.Buttons.ElementAtOrDefault(0).Tag = Account.Key;
                    this.ComboAccounts.Items.Add(Item);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"微软登录信息出错，登录信息已被重置", LogBehavior.Toast);
                Settings.Set("LoginMsJson", "{}");
            }
            // 如果不保留输入，刷新列表后自动选择第一项
            this.ComboAccounts.SelectedIndex = KeepInput ? Math.Max(0, IndexBefore) : 0;
        }
        private void ComboAccounts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModAnimation.AniControlEnabled != 0 || this.ComboAccounts.SelectedItem is null || this.ComboAccounts.ContentPresenter is null)
                return;
            if (this.ComboAccounts.SelectedItem is MyListItem)
            {
                this.ComboAccounts.ContentPresenter.Content = ((MyListItem)this.ComboAccounts.SelectedItem).Title;
            }
            else if (this.ComboAccounts.SelectedItem is MyComboBoxItem)
            {
                this.ComboAccounts.ContentPresenter.Content = ((MyComboBoxItem)this.ComboAccounts.SelectedItem).Content;
            }
        }

        /// <summary>
    /// 获取当前页面的登录信息。
    /// </summary>
        public static ModLaunch.McLoginMs GetLoginData()
        {
            if (ModMain.FrmLoginMs is null)
                return new ModLaunch.McLoginMs() { OAuthRefreshToken = Settings.Get<string>("CacheMsV2OAuthRefresh"), UserName = Settings.Get<string>("CacheMsV2Name") };
            ModLaunch.McLoginMs Result = null;
            ModBase.RunInUiWait(() => { if (ModMain.FrmLoginMs.ComboAccounts.SelectedIndex == 0) { Result = new ModLaunch.McLoginMs(); } else { MyListItem Item = (MyListItem)ModMain.FrmLoginMs.ComboAccounts.SelectedItem; Result = new ModLaunch.McLoginMs() { OAuthRefreshToken = Conversions.ToString(Item.Tag), UserName = Item.Title }; } });
            return Result;
        }
        /// <summary>
    /// 当前页面的登录信息是否有效。
    /// </summary>
        public static string IsVaild(ModLaunch.McLoginMs LoginData)
        {
            if (string.IsNullOrEmpty(LoginData.OAuthRefreshToken))
            {
                return "请在登录账号后再启动游戏！";
            }
            else
            {
                return "";
            }
        }
        public string IsVaild()
        {
            return IsVaild(GetLoginData());
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            this.ComboAccounts.IsEnabled = false;
            this.BtnLogin.IsEnabled = false;
            this.BtnLogin.Text = "0%";
            ModBase.RunInNewThread(() =>
        {
            try
            {
                ModLaunch.McLoginMsLoader.Start(GetLoginData(), IsForceRestart: true);
                while (ModLaunch.McLoginMsLoader.State == ModBase.LoadState.Loading)
                {
                    ModBase.RunInUi(() => this.BtnLogin.Text = Math.Round(ModLaunch.McLoginMsLoader.Progress * 100d) + "%");
                    Thread.Sleep(50);
                }
                if (ModLaunch.McLoginMsLoader.State == ModBase.LoadState.Finished)
                {
                    ModBase.RunInUi(() => ModMain.FrmLaunchLeft.RefreshPage(false, true));
                }
                else if (ModLaunch.McLoginMsLoader.State == ModBase.LoadState.Canceled)
                {
                    throw new OperationCanceledException();
                }
                else if (ModLaunch.McLoginMsLoader.Error is null)
                {
                    throw new Exception("未知错误！");
                }
                else
                {
                    throw new Exception(ModLaunch.McLoginMsLoader.Error.Message, ModLaunch.McLoginMsLoader.Error);
                }
            }
            catch (Exception ex)
            {
                if (ex.IsCanceled())
                {
                }
                else if (ex.Message.StartsWithF("$"))
                {
                    ModMain.Hint(ex.Message.TrimStart('$'), ModMain.HintType.Red);
                }
                else if (ex is System.Security.Authentication.AuthenticationException && ex.Message.Contains("SSL/TLS"))
                {
                    Logger.Error(ex, $"正版登录验证失败，请考虑在 [设置 → 其他] 中关闭 [在正版登录时验证 SSL 证书]，然后再试。{Constants.vbCrLf}{Constants.vbCrLf}原始错误信息：", LogBehavior.Alert);
                }
                else
                {
                    Logger.Error(ex, "正版登录尝试失败", LogBehavior.Alert);
                }
            }
            finally
            {
                ModBase.RunInUi(() =>
        {
                    this.ComboAccounts.IsEnabled = true;
                    this.BtnLogin.IsEnabled = true;
                    this.BtnLogin.Text = "登录";
                });
            }
        }, "Ms Login");
        }

    }
}