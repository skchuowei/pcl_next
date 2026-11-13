using System;
using System.Windows;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class PageOtherHelpDetail : IRefreshable
    {
        public ModMain.HelpEntry Entry;

        public PageOtherHelpDetail()
        {
            InitializeComponent();
            this.Loaded += PageOtherHelpDetail_Loaded;
        }

        public void Refresh()
        {
            if (Entry is null)
            {
                Logger.Warn("刷新帮助详情时尚未初始化帮助条目");
                return;
            }
            Init(new ModMain.HelpEntry(Entry.RawPath));
        }

        private void PageOtherHelpDetail_Loaded(object sender, RoutedEventArgs e)
        {
            this.PanBack.ScrollToTop();
        }

        /// <summary>
    /// 根据特定帮助项初始化页面 UI，返回是否成功加载。
    /// </summary>
        public bool Init(ModMain.HelpEntry Entry)
        {
            if (Entry is null)
            {
                Logger.Warn("无法初始化空的帮助条目");
                return false;
            }
            string Content = Entry.XamlContent ?? "";
            if (string.IsNullOrEmpty(Content))
                throw new Exception("帮助 xaml 文件为空");
            try
            {
                // 修改时应同时修改 PageLaunchRight.LoadContent
                Content = ModMain.ArgumentReplace(Content, StringUtils.XmlEscape);
                while (Content.Contains("xmlns"))
                    Content = Content.RegexReplace("xmlns[^\"']*(\"|')[^\"']*(\"|')", "").Replace("xmlns", ""); // 禁止声明命名空间
                Content = "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=pcl-next\" xmlns:core=\"clr-namespace:MeloongCore;assembly=MeloongCore\" xmlns:corewpf=\"clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf\">" + Content + "</StackPanel>";
                var ContentElement = (UIElement)ModBase.GetObjectFromXML(Content);
                this.PanCustom.Children.Clear();
                this.PanCustom.Children.Add(ContentElement);
                this.Entry = Entry;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Info($"自定义信息内容：{"\r\n"}{Content}");
                Logger.Error(ex, "加载帮助 XAML 文件失败", LogBehavior.Alert);
                return false;
            }
        }

    }
}