using System;
using System.Windows;
using System.Windows.Input;
using MeloongCore.Extensions;

namespace PCL
{
    public partial class PageOtherAbout
    {

        private new bool IsLoaded = false;

        public PageOtherAbout()
        {
            InitializeComponent();
            this.BtnDonateInput.Click += (p1, p2) => BtnDonateCodeInput_Click();
            // VB 的 Handles BtnDonateCopy.Click 迁移时被丢弃 → 关于页「复制识别码」点了没反应（2026-11-11 补）。
            this.BtnDonateCopy.Click += (p1, p2) => CopyIdentify();
            this.BtnDonateOutput.Click += (p1, p2) => BtnDonateOutput_Click((object)p1, (EventArgs)p2);
            this.BtnAboutUpdate.Click += (p1, p2) => BtnAboutUpdate_Click((object)p1, (MouseButtonEventArgs)p2);
            this.Loaded += PageOtherAbout_Loaded;
        }
        private void PageOtherAbout_Loaded(object sender, RoutedEventArgs e)
        {

            // 重复加载部分
            this.PanBack.ScrollToHome();

            // 非重复加载部分
            if (IsLoaded)
                return;
            IsLoaded = true;

            this.ItemAboutPcl.Info = this.ItemAboutPcl.Info.Replace("%VERSION%", ModBase.VersionDisplay).Replace("%VERSIONCODE%", ModBase.VersionCode.ToString()).Replace("%BRANCH%", ((int)ModBase.BuildType).ToString());
            // 构建版本：用于判断这份 exe 是否包含某次提交（规则见《分支与发布规范》§5.2.1）
            string buildInfo = $"{ModBuildInfo.BuildSmall} · #{ModBuildInfo.BuildCommit} · 第 {ModBuildInfo.BuildCommitCount} 个提交";
            if (ModBuildInfo.BuildIsDirty)
                buildInfo += " · 含未提交改动";
            this.ItemAboutBuild.Info = buildInfo;
            // 开发版不展示上游 PCL 的赞助入口——赞助属于上游作者，与本项目无关
            if (ModBuildChannel.ChannelIsInternal)
            {
                this.BtnDonateDonate.Visibility = Visibility.Collapsed;
                this.BtnDonateOutput.Visibility = Visibility.Visible;
            }

        }

        public static void CopyIdentify()
        {
            ModBase.ClipboardSet(ModBase.Identify);
        }
        private void BtnDonateCodeInput_Click()
        {
            ModSecret.InputPotatoCode(false);
        }
        private void BtnDonateOutput_Click(object sender, EventArgs e)
        {
            ModSecret.GeneratePotatoCode();
        }
        private void BtnAboutUpdate_Click(object sender, MouseButtonEventArgs e)
        {
            ModSecret.UpdateCheckByButton();
        }

    }
}