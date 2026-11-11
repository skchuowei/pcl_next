using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public partial class MyResourceItem
    {

        #region 基础属性
        public int Uuid = ModBase.GetUuid();

        // 标题
        public string Title
        {
            get
            {
                return this.LabTitle.Text;
            }
            set
            {
                if ((this.LabTitle.Text ?? "") == (value ?? ""))
                    return;
                this.LabTitle.Text = value;
            }
        }

        // 副标题
        public string SubTitle
        {
            get
            {
                return (this.LabTitleRaw?.Text) ?? "";
            }
            set
            {
                if ((this.LabTitleRaw.Text ?? "") == (value ?? ""))
                    return;
                this.LabTitleRaw.Text = value;
                this.LabTitleRaw.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // 描述
        public string Description
        {
            get
            {
                return this.LabInfo.Text;
            }
            set
            {
                if ((this.LabInfo.Text ?? "") == (value ?? ""))
                    return;
                this.LabInfo.Text = value;
            }
        }

        public MyResourceItem()
        {
            InitializeComponent();
            this.LabInfo.MouseEnter += (p1, p2) => LabInfo_MouseEnter((object)p1, (MouseEventArgs)p2);
            this.PreviewMouseLeftButtonUp += Button_MouseUp;
            Click += MyResourceItem_Click;
            this.PreviewMouseLeftButtonDown += Button_MouseDown;
            this.MouseLeave += Button_MouseLeave;
            this.PreviewMouseLeftButtonUp += Button_MouseLeave;
            this.MouseEnter += RefreshColor;
            this.MouseLeave += RefreshColor;
            this.MouseLeftButtonDown += RefreshColor;
            this.MouseLeftButtonUp += RefreshColor;
        }
        // 指向时扩展描述
        private void LabInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            this.ToolTipInfo.Content = this.LabInfo.Text;
            this.LabInfo.ToolTip = ModBase.IsTextTrimmed(this.LabInfo) ? this.ToolTipInfo : (ToolTip)null;
        }

        // Tag
        public List<string> Tags
        {
            set
            {
                this.PanTags.Children.Clear();
                this.PanTags.Visibility = value.Any() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var TagText in value)
                {
                    var BorderTag = new Border()
                    {
                        Background = new ModBase.MyColor("#11000000"),
                        Padding = new Thickness(3d, 1d, 3d, 1d),
                        CornerRadius = new CornerRadius(3d),
                        Margin = new Thickness(0d, 0d, 3d, 0d),
                        SnapsToDevicePixels = true,
                        UseLayoutRounding = false
                    };
                    var LabTag = new TextBlock() { Text = TagText, Foreground = new ModBase.MyColor("#868686"), FontSize = 11d };
                    BorderTag.Child = LabTag;
                    this.PanTags.Children.Add(BorderTag);
                }
            }
        }

        #endregion

        #region 点击

        // 触发点击事件
        public event ClickEventHandler Click;

        public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e);
        private void Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsMouseDown)
            {
                Click?.Invoke(sender, e);
                if (e.Handled)
                    return;
                Logger.Info($"按下资源工程列表项：{this.LabTitle.Text}");
            }
        }
        // 原文 Handles Me.Click 的处理函数签名是 (sender As MyResourceItem, e As EventArgs)，
        // VB 靠宽松委托转换挂上 ClickEventHandler(object, MouseButtonEventArgs)；C# 必须显式匹配委托签名，
        // sender 在方法体内按 MyResourceItem 使用，故就地转换
        private void MyResourceItem_Click(object sender, MouseButtonEventArgs e)
        {
            // 记录当前展开的卡片标题（#2712）
            var Titles = new List<string>();
            if (ModMain.FrmMain.PageCurrent.Page == FormMain.PageType.ResourceDetail)
            {
                foreach (MyCard Card in ModMain.FrmDownloadResourceDetail.PanResults.Children)
                {
                    if (!string.IsNullOrEmpty(Card.Title) && !Card.IsSwapped)
                        Titles.Add(Card.Title);
                }
                Logger.Info($"记录当前已展开的卡片：{string.Join("、", Titles)}");
            }
            // 打开详情页
            var TargetType = default(ResourceTypes);
            string TargetInstance = null;
            var TargetLoader = ModLoaders.None;
            if (ModMain.FrmMain.PageCurrent.Page == FormMain.PageType.Download)
            {
                // 从下载页进入
                switch (ModMain.FrmMain.PageCurrentSub)
                {
                    case FormMain.PageSubType.DownloadMod:
                        {
                            TargetType = ResourceTypes.Mod;
                            var modPage = ModMain.FrmDownloadMod;
                            if (modPage is not null)
                            {
                                TargetInstance = modPage.Content?.Loader?.Input?.GameVersion;
                                TargetLoader = modPage.Content?.Loader?.Input?.ModLoaders ?? ModLoaders.None;
                            }
                            break;
                        }
                    case FormMain.PageSubType.DownloadPack:
                        {
                            TargetType = ResourceTypes.ModPack;
                            var packPage = ModMain.FrmDownloadPack;
                            if (packPage is not null)
                                TargetInstance = packPage.Content?.Loader?.Input?.GameVersion;
                            break;
                        }
                    case FormMain.PageSubType.DownloadDataPack:
                        {
                            TargetType = ResourceTypes.DataPack;
                            var dataPackPage = ModMain.FrmDownloadDataPack;
                            if (dataPackPage is not null)
                                TargetInstance = dataPackPage.Content?.Loader?.Input?.GameVersion;
                            break;
                        }
                    case FormMain.PageSubType.DownloadResourcePack:
                        {
                            TargetType = ResourceTypes.ResourcePack;
                            var resourcePackPage = ModMain.FrmDownloadResourcePack;
                            if (resourcePackPage is not null)
                                TargetInstance = resourcePackPage.Content?.Loader?.Input?.GameVersion;
                            break;
                        }
                    case FormMain.PageSubType.DownloadShader:
                        {
                            TargetType = ResourceTypes.Shader;
                            var shaderPage = ModMain.FrmDownloadShader;
                            if (shaderPage is not null)
                                TargetInstance = shaderPage.Content?.Loader?.Input?.GameVersion;
                            break;
                        }
                }
            }
            else
            {
                // 从详情页进入（查看前置）
                TargetType = ResourceTypes.Any; // 允许任意类别
                TargetInstance = Conversions.ToString(((dynamic)ModMain.FrmMain.PageCurrent.Additional)[2]);
                TargetLoader = (ModLoaders)Conversions.ToInteger(((dynamic)ModMain.FrmMain.PageCurrent.Additional)[3]);
            }
            ModMain.FrmMain.PageChange(new FormMain.PageStackData()
            {
                Page = FormMain.PageType.ResourceDetail,
                Additional = new[] { ((MyResourceItem)sender).Tag, Titles, TargetInstance, TargetLoader, TargetType }
            });
        }

        // 鼠标点击判定
        private bool IsMouseDown = false;
        private void Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (this.IsMouseOver && CanInteraction)
                IsMouseDown = true;
        }
        private void Button_MouseLeave(object sender, object e)
        {
            IsMouseDown = false;
        }

        #endregion

        #region 后加载指向背景

        private Border _RectBack = null;
        public Border RectBack
        {
            get
            {
                if (_RectBack is null)
                {
                    var Rect = new Border()
                    {
                        Name = "RectBack",
                        CornerRadius = new CornerRadius(3d),
                        RenderTransform = new ScaleTransform(0.8d, 0.8d),
                        RenderTransformOrigin = new Point(0.5d, 0.5d),
                        BorderThickness = new Thickness(ModBase.GetWPFSize(1d)),
                        SnapsToDevicePixels = true,
                        IsHitTestVisible = false,
                        Opacity = 0d
                    };
                    Rect.SetResourceReference(Border.BackgroundProperty, "ColorBrush7");
                    Rect.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6");
                    Grid.SetColumnSpan(Rect, 999);
                    Grid.SetRowSpan(Rect, 999);
                    this.Children.Insert(0, Rect);
                    _RectBack = Rect;
                    // <!--<Border x:Name = "RectBack" CornerRadius="3" RenderTransformOrigin="0.5,0.5" SnapsToDevicePixels="True" 
                    // IsHitTestVisible = "False" Opacity="0" BorderThickness="1" 
                    // Grid.ColumnSpan = "4" Background="{DynamicResource ColorBrush7}" BorderBrush="{DynamicResource ColorBrush6}"/>-->
                }
                return _RectBack;
            }
        }

        #endregion

        private string StateLast;
        /// <summary>
    /// 是否允许交互。目前仅用于 PageDownloadResourceDetail 的顶部栏展示：若关闭碰撞检测，则无法展开 Tooltip。
    /// </summary>
        public bool CanInteraction { get; set; } = true;
        public void RefreshColor(object sender, EventArgs e)
        {
            if (!CanInteraction)
                return;
            // 判断当前颜色
            string StateNew;
            int Time;
            if (this.IsMouseOver)
            {
                if (IsMouseDown)
                {
                    StateNew = "MouseDown";
                    Time = 120;
                }
                else
                {
                    StateNew = "MouseOver";
                    Time = 120;
                }
            }
            else
            {
                StateNew = "Idle";
                Time = 180;
            }
            if ((StateLast ?? "") == (StateNew ?? ""))
                return;
            StateLast = StateNew;
            // 触发颜色动画
            if (this.IsLoaded && ModAnimation.AniControlEnabled == 0) // 防止默认属性变更触发动画
            {
                // 有动画
                var Ani = new List<ModAnimation.AniData>();
                if (this.IsMouseOver)
                {
                    Ani.AddRange(new[] { ModAnimation.AaColor(RectBack, Border.BackgroundProperty, IsMouseDown ? "ColorBrush6" : "ColorBrushBg1", Time), ModAnimation.AaOpacity(RectBack, 1d - RectBack.Opacity, Time, Ease: new ModAnimation.AniEaseOutFluent()) });
                    if (IsMouseDown)
                    {
                        Ani.Add(ModAnimation.AaScaleTransform(RectBack, 0.996d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, (int)Math.Round(Time * 1.2d), Ease: new ModAnimation.AniEaseOutFluent()));
                    }
                    else
                    {
                        Ani.Add(ModAnimation.AaScaleTransform(RectBack, 1d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, (int)Math.Round(Time * 1.2d), Ease: new ModAnimation.AniEaseOutFluent()));
                    }
                }
                else
                {
                    Ani.AddRange(new[] { ModAnimation.AaOpacity(RectBack, -RectBack.Opacity, Time), ModAnimation.AaColor(RectBack, Border.BackgroundProperty, IsMouseDown ? "ColorBrush6" : "ColorBrush7", Time), ModAnimation.AaScaleTransform(RectBack, 0.996d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, Time, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaScaleTransform(RectBack, -0.196d, 1, After: true) });
                }
                ModAnimation.AniStart(Ani, "MyResourceItem Color " + Uuid);
            }
            else
            {
                // 无动画
                ModAnimation.AniStop("MyResourceItem Color " + Uuid);
                if (_RectBack is not null)
                    RectBack.Opacity = 0d;
            }
        }

    }
}