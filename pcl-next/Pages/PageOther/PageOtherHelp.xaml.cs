using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Wpf.Extensions;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class PageOtherHelp : IRefreshable
    {
        public PageOtherHelp()
        {
            InitializeComponent();
            this.SearchBox.TextChanged += (p1, p2) => SearchRun();
            this.Loaded += PageOther_Loaded;
            this.Initialized += PageOther_Inited;
        }

        #region 初始化

        // 滚动条
        private void PageOther_Loaded(object sender, RoutedEventArgs e)
        {
            this.PanBack.ScrollToHome();
        }
        // 初始化加载器信息
        private void PageOther_Inited(object sender, EventArgs e)
        {
            this.PageLoaderInit(this.Load, this.PanLoad, this.PanBack, (FrameworkElement)null, ModMain.HelpLoader, this.HelpListLoad);
        }

        #endregion

        /// <summary>
    /// 将帮助列表对象实例化为主页 UI。
    /// </summary>
        private void HelpListLoad(ModLoader.LoaderTask<int, List<ModMain.HelpEntry>> Loader)
        {
            try
            {

                // 初始化
                this.PanList.Children.Clear();
                this.PanBack.ScrollToHome();
                var HelpItems = Loader.Output;
                // 获取全部分类
                var Types = new List<string>();
                foreach (ModMain.HelpEntry Item in HelpItems)
                {
                    if (ModBuildChannel.ChannelIsOfficial && !Item.ShowInPublic)
                        continue;
                    if (!ModBuildChannel.ChannelIsOfficial && !Item.ShowInSnapshot)
                        continue;
                    foreach (var Type in Item.Types)
                    {
                        if (!Types.Contains(Type))
                            Types.Add(Type);
                    }
                }
                // 将指南页面置顶
                if (Types.Contains("指南"))
                {
                    Types.Remove("指南");
                    Types.Insert(0, "指南");
                }
                // 转化为 UI
                foreach (string Type in Types)
                {
                    // 确认所属该分类的项目
                    var TypeItems = new List<ModMain.HelpEntry>();
                    foreach (var Item in HelpItems)
                    {
                        if (ModBuildChannel.ChannelIsOfficial && !Item.ShowInPublic)
                            continue;
                        if (!ModBuildChannel.ChannelIsOfficial && !Item.ShowInSnapshot)
                            continue;
                        if (Item.Types.Contains(Type))
                            TypeItems.Add(Item);
                    }
                    // 增加卡片
                    var NewCard = new MyCard() { Title = Type, Margin = new Thickness(0d, 0d, 0d, 15d) };
                    var NewStack = new StackPanel() { Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d), VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d) };
                    NewCard.Children.Add(NewStack);
                    NewCard.SwapControl = NewStack;
                    NewCard.SetContentBuilder((IEnumerable<ModMain.HelpEntry>)TypeItems, Entry => Entry.ToListItem());
                    if (Type == "指南")
                    {
                        NewCard.BuildContent();
                    }
                    else
                    {
                        NewCard.IsSwapped = true;
                    }
                    this.PanList.Children.Add(NewCard);
                }
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "加载帮助列表 UI 失败");
            }
        }

        /// <summary>
    /// 帮助项目的点击事件。
    /// </summary>
        public static void OnItemClick(ModMain.HelpEntry Entry)
        {
            try
            {
                if (Entry.IsEvent)
                {
                    CustomEvent.Raise(Entry.EventType, Entry.EventData);
                }
                else
                {
                    EnterHelpPage(Entry);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "处理帮助项目点击时发生意外错误");
            }
        }
        public static void EnterHelpPage(string Location)
        {
            ModBase.RunInThread(() =>
        {
            if (!(ModMain.HelpLoader.State == ModBase.LoadState.Finished))
                ModMain.HelpLoader.WaitForExit(ModBase.GetUuid());
            var Entry = new ModMain.HelpEntry(Location);
            ModBase.RunInUi(() =>
        {
                var FrmHelpDetail = new PageOtherHelpDetail();
                if (FrmHelpDetail.Init(Entry))
                {
                    ModMain.FrmMain.PageChange(new FormMain.PageStackData() { Page = FormMain.PageType.HelpDetail, Additional = new object[]{ Entry, FrmHelpDetail } });
                }
                else
                {
                    Logger.Warn("已取消进入帮助项目，这一般是由于 xaml 初始化失败，且用户在弹窗中手动放弃");
                }
            });
        });
        }
        public static void EnterHelpPage(ModMain.HelpEntry Entry)
        {
            ModBase.RunInThread(() =>
        {
            if (!(ModMain.HelpLoader.State == ModBase.LoadState.Finished))
                ModMain.HelpLoader.WaitForExit(ModBase.GetUuid());
            ModBase.RunInUi(() =>
        {
                var FrmHelpDetail = new PageOtherHelpDetail();
                if (FrmHelpDetail.Init(Entry))
                {
                    ModMain.FrmMain.PageChange(new FormMain.PageStackData() { Page = FormMain.PageType.HelpDetail, Additional = new object[]{ Entry, FrmHelpDetail } });
                }
                else
                {
                    Logger.Warn("已取消进入帮助项目，这一般是由于 xaml 初始化失败，且用户在弹窗中手动放弃");
                }
            });
        });
        }

        /// <summary>
    /// 搜索帮助。
    /// </summary>
        public void SearchRun()
        {
            if (string.IsNullOrWhiteSpace(this.SearchBox.Text))
            {
                // 隐藏
                ModAnimation.AniStart(new[] {
                 ModAnimation.AaOpacity(this.PanSearch, -this.PanSearch.Opacity, 100),
                                  ModAnimation.AaCode(() =>
                    {
                     this.PanSearch.Height = 0d;
                     this.PanSearch.Visibility = Visibility.Collapsed;
                     this.PanList.Visibility = Visibility.Visible;
                 }, After: true),
                 ModAnimation.AaOpacity(this.PanList, 1d - this.PanList.Opacity, 150, 30)
            }, "FrmOtherHelp Search Switch");
            }
            else
            {
                // 构造请求
                var QueryList = new List<ModBase.SearchEntry<ModMain.HelpEntry>>();
                foreach (ModMain.HelpEntry Entry in ModMain.HelpLoader.Output)
                {
                    if (!Entry.ShowInSearch || ModBuildChannel.ChannelIsOfficial && !Entry.ShowInPublic)
                        continue;
                    if (!Entry.ShowInSearch || !ModBuildChannel.ChannelIsOfficial && !Entry.ShowInSnapshot)
                        continue;
                    QueryList.Add(new ModBase.SearchEntry<ModMain.HelpEntry>()
                    {
                        Item = Entry,
                        SearchSource = new List<ModBase.SearchSource>() { new ModBase.SearchSource(Entry.Title, 1d), new ModBase.SearchSource(Entry.Desc, 0.5d), new ModBase.SearchSource(Entry.Search, 1.5d) }
                    });
                    // New SearchSource(If(Entry.IsEvent, If(Entry.EventData, ""), Entry.XamlContent), 0.2)
                }
                // 进行搜索，构造列表
                var SearchResult = ModBase.Search(QueryList, this.SearchBox.Text, MaxBlurCount: 5, MinBlurSimilarity: 0.08d);
                this.PanSearchList.Children.Clear();
                if (!SearchResult.Any())
                {
                    this.PanSearch.Title = "无搜索结果";
                    this.PanSearchList.Visibility = Visibility.Collapsed;
                }
                else
                {
                    this.PanSearch.Title = "搜索结果";
                    foreach (var Result in SearchResult)
                    {
                        var Item = Result.Item.ToListItem();
                        if (ModBase.ModeDebug)
                            Item.Info = (Result.AbsoluteRight ? "完全匹配，" : "") + "相似度：" + Math.Round(Result.Similarity, 3) + "，" + Item.Info;
                        this.PanSearchList.Children.Add(Item);
                    }
                    this.PanSearchList.Visibility = Visibility.Visible;
                }
                // 显示
                ModAnimation.AniStart(new[] {
                 ModAnimation.AaOpacity(this.PanList, -this.PanList.Opacity, 100),
                                  ModAnimation.AaCode(() =>
                    {
                     this.PanList.Visibility = Visibility.Collapsed;
                     this.PanSearch.Visibility = Visibility.Visible;
                     this.PanSearch.TriggerForceResize();
                 }, After: true),
                 ModAnimation.AaOpacity(this.PanSearch, 1d - this.PanSearch.Opacity, 150, 30)
            }, "FrmOtherHelp Search Switch");
            }
        }

        public void Refresh()
        {
            PageOtherLeft.RefreshHelp();
        }
    }
}