using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    public partial class MyLocalModItem
    {

        #region 基础属性
        public int Uuid = ModBase.GetUuid();

        // 标题
        private string _Title;
        public string Title
        {
            get
            {
                return _Title;
            }
            set
            {
                string RawValue = value;
                this.LabTitle.TextDecorations = Entry.IsEnabled ? null : TextDecorations.Strikethrough;
                if ((this.LabTitle.Text ?? "") == (value ?? ""))
                    return;
                this.LabTitle.Text = value;
                _Title = RawValue;
            }
        }

        // 副标题
        public string SubTitle
        {
            get
            {
                return (this.LabSubtitle?.Text) ?? "";
            }
            set
            {
                if ((this.LabSubtitle.Text ?? "") == (value ?? ""))
                    return;
                this.LabSubtitle.Text = value;
                this.LabSubtitle.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
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
                        Background = new ModBase.MyColor("#0A000000"),
                        Padding = new Thickness(3d, 1d, 3d, 1d),
                        CornerRadius = new CornerRadius(3d),
                        Margin = new Thickness(0d, 0d, 3d, 0d),
                        SnapsToDevicePixels = true,
                        UseLayoutRounding = false
                    };
                    var LabTag = new TextBlock() { Text = TagText, Foreground = new ModBase.MyColor("#B0B0B0"), FontSize = 11d };
                    BorderTag.Child = LabTag;
                    this.PanTags.Children.Add(BorderTag);
                }
            }
        }

        // 相关联的 Mod
        public LocalResourceFile Entry
        {
            get
            {
                return (LocalResourceFile)this.Tag;
            }
            set
            {
                this.Tag = value;
            }
        }

        #endregion

        #region 点击与勾选

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
                Logger.Info($"按下本地 Mod 列表项：{this.LabTitle.Text}");
            }
        }

        // 鼠标点击判定
        private bool IsMouseDown = false;
        private void Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!this.IsMouseDirectlyOver)
                return;
            IsMouseDown = true;
            if (ButtonStack is not null)
                ButtonStack.IsHitTestVisible = false;
        }
        private void Button_MouseLeave(object sender, object e)
        {
            IsMouseDown = false;
            if (ButtonStack is not null)
                ButtonStack.IsHitTestVisible = true;
        }

        // 滑动选中
        private static int SwipeStart;
        private static int SwipeEnd;
        private static bool _Swiping = false;
        private static bool Swiping
        {
            get
            {
                return _Swiping;
            }
            set
            {
                _Swiping = value;
                ModMain.FrmInstanceMod.CardSelect.IsHitTestVisible = !value;
            }
        }
        private static bool SwipToState; // 被滑动到的目标应将 Checked 改为此值
        private void Button_MouseSwipeStart(object sender, object e)
        {
            if (this.Parent is null)
                return; // Mod 可能已被删除（#3824）
                        // 开始滑动
            int Index = ((StackPanel)this.Parent).Children.IndexOf(this);
            SwipeStart = Index;
            SwipeEnd = Index;
            Swiping = true;
            SwipToState = !Checked;
        }
        private void Button_MouseSwipe(object sender, object e)
        {
            if (this.Parent is null)
                return; // Mod 可能已被删除（#3824）
                        // 结束滑动
            if (Mouse.LeftButton != MouseButtonState.Pressed || !(Mouse.DirectlyOver is MyLocalModItem)) // #5771
            {
                Swiping = false;
                return;
            }
            // 计算滑动范围
            var Elements = ((StackPanel)this.Parent).Children;
            int Index = Elements.IndexOf(this);
            SwipeStart = Math.Min(SwipeStart, Index).Clamp(0, Elements.Count - 1);
            SwipeEnd = Math.Max(SwipeEnd, Index).Clamp(0, Elements.Count - 1);
            // 勾选所有范围中的项
            if (SwipeStart == SwipeEnd)
                return;
            for (int i = SwipeStart, loopTo = SwipeEnd; i <= loopTo; i++)
            {
                MyLocalModItem Item = (MyLocalModItem)Elements[i];
                Item.InitLate(Item, (EventArgs)e);
                Item.Checked = SwipToState;
            }
        }

        // 勾选状态
        public event CheckEventHandler Check;

        public delegate void CheckEventHandler(object sender, ModBase.RouteEventArgs e);
        public event ChangedEventHandler Changed;

        public delegate void ChangedEventHandler(object sender, ModBase.RouteEventArgs e);
        private bool _Checked = false;
        public bool Checked
        {
            get
            {
                return _Checked;
            }
            set
            {
                try
                {
                    // 触发属性值修改
                    bool RawValue = _Checked;
                    if (value == _Checked)
                        return;
                    _Checked = value;
                    var ChangedEventArgs = new ModBase.RouteEventArgs(false);
                    if (this.IsInitialized)
                    {
                        Changed?.Invoke(this, ChangedEventArgs);
                        if (ChangedEventArgs.Handled)
                        {
                            _Checked = RawValue;
                            return;
                        }
                    }
                    if (value)
                    {
                        var CheckEventArgs = new ModBase.RouteEventArgs(false);
                        Check?.Invoke(this, CheckEventArgs);
                        if (CheckEventArgs.Handled)
                            return;
                    }
                    // 更改动画
                    if (this.IsVisibleInForm())
                    {
                        var Anim = new List<ModAnimation.AniData>();
                        if (Checked)
                        {
                            // 由无变有
                            double Delta = 32d - RectCheck.ActualHeight;
                            Anim.Add(ModAnimation.AaHeight(RectCheck, Delta * 0.4d, 200, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
                            Anim.Add(ModAnimation.AaHeight(RectCheck, Delta * 0.6d, 300, Ease: new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)));
                            Anim.Add(ModAnimation.AaOpacity(RectCheck, 1d - RectCheck.Opacity, 30));
                            RectCheck.VerticalAlignment = VerticalAlignment.Center;
                            RectCheck.Margin = new Thickness(-3, 0d, 0d, 0d);
                            Anim.Add(ModAnimation.AaColor(this.LabTitle, TextBlock.ForegroundProperty, Entry.IsEnabled ? "ColorBrush2" : "ColorBrush5", 200));
                        }
                        else
                        {
                            // 由有变无
                            Anim.Add(ModAnimation.AaHeight(RectCheck, -RectCheck.ActualHeight, 120, Ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)));
                            Anim.Add(ModAnimation.AaOpacity(RectCheck, -RectCheck.Opacity, 70, 40));
                            RectCheck.VerticalAlignment = VerticalAlignment.Center;
                            Anim.Add(ModAnimation.AaColor(this.LabTitle, TextBlock.ForegroundProperty, this.LabTitle.TextDecorations is null ? "ColorBrush1" : "ColorBrushGray4", 120));
                        }
                        ModAnimation.AniStart(Anim, "MyLocalModItem Checked " + Uuid);
                    }
                    else
                    {
                        // 不在窗口上时直接设置
                        RectCheck.VerticalAlignment = VerticalAlignment.Center;
                        RectCheck.Margin = new Thickness(-3, 0d, 0d, 0d);
                        if (Checked)
                        {
                            RectCheck.Height = 32d;
                            RectCheck.Opacity = 1d;
                            this.LabTitle.SetResourceReference(TextBlock.ForegroundProperty, Entry.IsEnabled ? "ColorBrush2" : "ColorBrush5");
                        }
                        else
                        {
                            RectCheck.Height = 0d;
                            RectCheck.Opacity = 0d;
                            this.LabTitle.SetResourceReference(TextBlock.ForegroundProperty, Entry.IsEnabled ? "ColorBrush1" : "ColorBrushGray4");
                        }
                        ModAnimation.AniStop("MyLocalModItem Checked " + Uuid);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "设置 Checked 失败");
                }
            }
        }


        #endregion

        #region 后加载内容

        // 右下角状态指示图标
        private Image ImgState;

        // 指向背景
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

        // 按钮
        public Action<MyLocalModItem, EventArgs> ButtonHandler;
        public FrameworkElement ButtonStack;
        private IEnumerable<MyIconButton> _Buttons;
        public IEnumerable<MyIconButton> Buttons
        {
            get
            {
                return _Buttons;
            }
            set
            {
                _Buttons = value;
                // 移除原 Stack
                if (ButtonStack is not null)
                {
                    this.Children.Remove(ButtonStack);
                    ButtonStack = null;
                }
                if (!value.Any())
                    return;
                // 添加新 Stack
                ButtonStack = new StackPanel()
                {
                    Opacity = 0d,
                    Margin = new Thickness(0d, 0d, 5d, 0d),
                    SnapsToDevicePixels = false,
                    Orientation = (System.Windows.Controls.Orientation)System.Windows.Forms.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    UseLayoutRounding = false
                };
                Grid.SetColumnSpan(ButtonStack, 10);
                Grid.SetRowSpan(ButtonStack, 10);
                // 构造按钮
                foreach (MyIconButton Btn in value)
                {
                    if (Btn.Height.Equals(double.NaN))
                        Btn.Height = 25d;
                    if (Btn.Width.Equals(double.NaN))
                        Btn.Width = 25d;
                    ((StackPanel)ButtonStack).Children.Add(Btn);
                }
                this.Children.Add(ButtonStack);
            }
        }

        // 勾选条
        private Border _RectCheck;
        public Border RectCheck
        {
            get
            {
                if (_RectCheck is null)
                {
                    _RectCheck = new Border()
                    {
                        Width = 5d,
                        Height = Checked ? double.NaN : 0d,
                        CornerRadius = new CornerRadius(2d, 2d, 2d, 2d),
                        VerticalAlignment = Checked ? VerticalAlignment.Stretch : VerticalAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                        UseLayoutRounding = false,
                        SnapsToDevicePixels = false,
                        Margin = Checked ? new Thickness(-3, 6d, 0d, 6d) : new Thickness(-3, 0d, 0d, 0d)
                    };
                    _RectCheck.SetResourceReference(Border.BackgroundProperty, "ColorBrush3");
                    Grid.SetRowSpan(_RectCheck, 10);
                    this.Children.Add(_RectCheck);
                }
                return _RectCheck;
            }
        }

        #endregion

        private string GetUpdateCompareDescription()
        {
            string CurrentName = (Entry.ProjectVersion.Version ?? Entry.ProjectVersion.Display).Replace(".jar", "");
            string NewestName = (Entry.UpdateFile.Version ?? Entry.UpdateFile.Display).Replace(".jar", "");
            // 简化名称对比
            var CurrentSegs = CurrentName.Split("-_+ ".ToCharArray()).ToList();
            var NewestSegs = NewestName.Split("-_+ ".ToCharArray()).ToList();
            bool Shortened = false;
            foreach (var Seg in CurrentSegs.ToList())
            {
                if (!NewestSegs.Contains(Seg))
                    continue;
                CurrentSegs.Remove(Seg);
                NewestSegs.Remove(Seg);
                Shortened = true;
            }
            if (Shortened && CurrentSegs.Any() && NewestSegs.Any())
            {
                CurrentName = CurrentSegs.Join("-");
                NewestName = NewestSegs.Join("-");
                Entry._Version = CurrentName; // 使用网络信息作为显示的版本号
            }
            return $"当前版本：{CurrentName}（{StringUtils.FormatTimeSpan(Entry.ProjectVersion.ReleaseDate - DateTime.Now, false)}）{Constants.vbCrLf}最新版本：{NewestName}（{StringUtils.FormatTimeSpan(Entry.UpdateFile.ReleaseDate - DateTime.Now, false)}）";
        }

        // 懒加载
        public MyLocalModItem()
        {
            this.InitializeComponent();
            this.BtnUpdate.PreviewMouseRightButtonUp += (p1, p2) => BtnUpdate_PreviewMouseRightButtonUp((object)p1, (MouseButtonEventArgs)p2);
            this.BtnUpdate.Click += (p1, p2) => BtnUpdate_Click((object)p1, (EventArgs)p2);
            this.PanTitle.SizeChanged += (p1, p2) => PanTitle_SizeChanged();
            LazyLoadBehavior.OnFirstEnterScrollViewerViewport(this, this.FirstEnterScrollViewerViewport);
            this.PreviewMouseLeftButtonUp += Button_MouseUp;
            this.PreviewMouseLeftButtonDown += Button_MouseDown;
            this.MouseLeave += Button_MouseLeave;
            this.PreviewMouseLeftButtonUp += Button_MouseLeave;
            this.MouseLeftButtonDown += Button_MouseSwipeStart;
            this.MouseEnter += Button_MouseSwipe;
            this.MouseLeave += Button_MouseSwipe;
            this.MouseLeftButtonUp += Button_MouseSwipe;
            this.MouseEnter += RefreshColor;
            this.MouseLeave += RefreshColor;
            this.MouseLeftButtonDown += RefreshColor;
            this.MouseLeftButtonUp += RefreshColor;
            Changed += RefreshColor;
        }
        private bool HasEnteredViewport = false;
        private void FirstEnterScrollViewerViewport()
        {
            HasEnteredViewport = true;
            Refresh();
        }
        public void Refresh()
        {
            if (!HasEnteredViewport)
                return; // 避免在未显示时因为资源更新而加载内容
                        // 更新
            if (Entry.HasUpdate)
            {
                this.BtnUpdate.Visibility = Visibility.Visible;
                this.BtnUpdate.ToolTip = $"{GetUpdateCompareDescription()}{Constants.vbCrLf}点击以更新，右键查看更新日志。";
            }
            else
            {
                this.BtnUpdate.Visibility = Visibility.Collapsed;
            }
            // 标题与描述
            string DescFileName = PathUtils.GetFileNameWithoutExtension(Entry.EnabledName);
            string NewDescription;
            if (Settings.Get<int>("ToolModLocalNameStyle") == 1)
            {
                // 标题显示文件名，详情显示译名
                // 标题
                Title = DescFileName;
                SubTitle = "";
                // 描述
                if (Entry.Project is null)
                {
                    NewDescription = Entry.Display;
                }
                else
                {
                    var Titles = Entry.Project.GetControlTitle(false);
                    NewDescription = Titles.Title + Titles.SubTitle;
                }
                NewDescription = NewDescription.Replace("  |  ", " / ");
                if (Entry.Version is not null)
                    NewDescription += $" ({Entry.Version})";
            }
            else
            {
                // 标题显示译名，详情显示文件名
                // 标题
                if (Entry.Project is null)
                {
                    Title = Entry.Display;
                    SubTitle = Entry.Version is null ? "" : "  |  " + Entry.Version;
                }
                else
                {
                    var Titles = Entry.Project.GetControlTitle(false);
                    Title = Titles.Title;
                    SubTitle = Titles.SubTitle + (Entry.Version is null ? "" : "  |  " + Entry.Version);
                }
                // 描述
                NewDescription = DescFileName;
            }
            if (Entry.Project is not null)
            {
                NewDescription += ": " + Entry.Project.Description.ReplaceLineEndings("");
            }
            else if (Entry.Description is not null)
            {
                NewDescription += ": " + Entry.Description.ReplaceLineEndings("");
            }
            Description = NewDescription;
            if (Checked)
            {
                this.LabTitle.SetResourceReference(TextBlock.ForegroundProperty, Entry.IsEnabled ? "ColorBrush2" : "ColorBrush5");
            }
            else
            {
                this.LabTitle.SetResourceReference(TextBlock.ForegroundProperty, Entry.IsEnabled ? "ColorBrush1" : "ColorBrushGray4");
            }
            // 主 Logo
            if (Entry.Project is not null)
            {
                Entry.Project.ApplyLogoToMyImage(this.PathLogo);
            }
            else
            {
                this.PathLogo.Source = ModBase.PathImage + "Icons/NoIcon.png";
            }
            // 图标右下角的 Logo
            if (Entry.IsEnabled)
            {
                if (ImgState is not null)
                {
                    this.Children.Remove(ImgState);
                    ImgState = null;
                }
            }
            else
            {
                if (ImgState is null)
                {
                    ImgState = new Image()
                    {
                        Width = 20d,
                        Height = 20d,
                        Margin = new Thickness(0d, 0d, -5, -3),
                        IsHitTestVisible = false,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };
                    RenderOptions.SetBitmapScalingMode(ImgState, BitmapScalingMode.HighQuality);
                    Grid.SetColumn(ImgState, 1);
                    Grid.SetRow(ImgState, 1);
                    Grid.SetRowSpan(ImgState, 2);
                    this.Children.Add(ImgState);
                    // <Image x:Name="ImgState" RenderOptions.BitmapScalingMode="HighQuality" Width="16" Height="16" Margin="0,0,-3,-1"
                    // Grid.Column="1" Grid.Row="1" Grid.RowSpan="2" IsHitTestVisible="False"
                    // HorizontalAlignment="Right" VerticalAlignment="Bottom"
                    // Source="/Images/Icons/Unavailable.png" />
                }
                ImgState.Source = new MyBitmap(ModBase.PathImage + $"Icons/Disabled.png");
            }
            // 标签
            if (Entry.Project is not null)
                Tags = Entry.Project.Tags;
        }

        public void RefreshColor(object sender, EventArgs e)
        {
            InitLate(sender, e);
            // 触发颜色动画
            int Time = this.IsMouseOver ? 120 : 180;
            var Ani = new List<ModAnimation.AniData>();
            // ButtonStack
            if (ButtonStack is not null)
            {
                if (this.IsMouseOver)
                {
                    Ani.Add(ModAnimation.AaOpacity(ButtonStack, 1d - ButtonStack.Opacity, (int)Math.Round(Time * 0.7d), (int)Math.Round(Time * 0.3d)));
                    Ani.Add(ModAnimation.AaDouble(i => this.ColumnPaddingRight.Width = new GridLength(Conversions.ToDouble(Math.Max(0, (dynamic)(Operators.AddObject(this.ColumnPaddingRight.Width.Value, i))))), (double)(5 + Buttons.Count() * 25) - this.ColumnPaddingRight.Width.Value, (int)Math.Round(Time * 0.3d), (int)Math.Round(Time * 0.7d)));
                }
                else
                {
                    Ani.Add(ModAnimation.AaOpacity(ButtonStack, -ButtonStack.Opacity, (int)Math.Round(Time * 0.4d)));
                    Ani.Add(ModAnimation.AaDouble(i => this.ColumnPaddingRight.Width = new GridLength(Conversions.ToDouble(Math.Max(0, (dynamic)(Operators.AddObject(this.ColumnPaddingRight.Width.Value, i))))), 4d - this.ColumnPaddingRight.Width.Value, (int)Math.Round(Time * 0.4d)));
                }
            }
            // RectBack
            if (this.IsMouseOver || Checked)
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
                Ani.AddRange(new[] { ModAnimation.AaOpacity(RectBack, -RectBack.Opacity, Time), ModAnimation.AaScaleTransform(RectBack, 0.996d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, Time, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaScaleTransform(RectBack, -0.196d, 1, After: true) });
            }
            ModAnimation.AniStart(Ani, "LocalModItem Color " + Uuid);
        }

        // 触发虚拟化内容
        private void InitLate(object sender, EventArgs e)
        {
            if (ButtonHandler is not null)
            {
                ButtonHandler((MyLocalModItem)sender, e);
                ButtonHandler = null;
            }
        }

        // 显示更新日志
        private void BtnUpdate_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowUpdateLog();
        }
        private void ShowUpdateLog()
        {
            string CurseForgeUrl = Entry.ChangelogUrls.FirstOrDefault(x => x.Contains("curseforge.com"));
            string ModrinthUrl = Entry.ChangelogUrls.FirstOrDefault(x => x.Contains("modrinth.com"));
            if (CurseForgeUrl is null || ModrinthUrl is null)
            {
                ModBase.OpenWebsite(Entry.ChangelogUrls.First());
            }
            else
            {
                switch (ModMain.MyMsgBox("要在哪个网站上查看更新日志？", "查看更新日志", "Modrinth", "CurseForge", "取消"))
                {
                    case 1:
                        {
                            ModBase.OpenWebsite(ModrinthUrl);
                            break;
                        }
                    case 2:
                        {
                            ModBase.OpenWebsite(CurseForgeUrl);
                            break;
                        }
                }
            }
        }

        // 触发更新
        private void BtnUpdate_Click(object sender, EventArgs e)
        {
            switch (ModMain.MyMsgBox($"是否要更新 {Entry.Display}？{Constants.vbCrLf}{Constants.vbCrLf}{GetUpdateCompareDescription()}", "Mod 更新确认", "更新", "查看更新日志", "取消"))
            {
                case 1: // 更新
                    {
                        ModMain.FrmInstanceMod.UpdateMods(new[] { Entry });
                        break;
                    }
                case 2: // 查看更新日志
                    {
                        ShowUpdateLog();
                        break;
                    }
                case 3: // 取消
                    {
                        break;
                    }
            }
        }

        // 自适应（#4465）
        private void PanTitle_SizeChanged()
        {
            // 0：全部舒展：Auto - Auto - (Auto) - 1*
            // 1：压缩 Subtitle：Auto - 1* - (Auto) - 0
            // 2：继续压缩 Title：1* - 0 - (Auto) - 0
            int CurrentCompressLevel = this.ColumnExtend.Width.IsStar ? 0 : this.ColumnTitle.Width.IsStar ? 2 : 1; // Subtitle 可能是 Collapsed
            var NewCompressLevel = default(int);
            switch (CurrentCompressLevel)
            {
                case 0:
                    {
                        if (this.ColumnExtend.ActualWidth < 0.5d)
                        {
                            NewCompressLevel = this.LabSubtitle.Visibility == Visibility.Collapsed ? 2 : 1;
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }
                case 1:
                    {
                        if (this.ColumnSubtitle.ActualWidth < 0.5d)
                        {
                            NewCompressLevel = 2;
                        }
                        else if (!this.LabSubtitle.IsTextTrimmed())
                        {
                            NewCompressLevel = 0;
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }
                case 2:
                    {
                        if (!this.LabTitle.IsTextTrimmed())
                        {
                            NewCompressLevel = this.LabSubtitle.Visibility == Visibility.Collapsed ? 0 : 1;
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }
            }
            switch (NewCompressLevel)
            {
                case 0:
                    {
                        // 全部舒展：Auto - Auto - (Auto) - 1*
                        this.ColumnTitle.Width = GridLength.Auto;
                        this.ColumnSubtitle.Width = GridLength.Auto;
                        this.ColumnExtend.Width = new GridLength(1d, GridUnitType.Star);
                        break;
                    }
                case 1:
                    {
                        // 压缩 Subtitle：Auto - 1* - (Auto) - 0
                        this.ColumnTitle.Width = GridLength.Auto;
                        this.ColumnSubtitle.Width = new GridLength(1d, GridUnitType.Star);
                        this.ColumnExtend.Width = new GridLength(0d, GridUnitType.Pixel);
                        break;
                    }
                case 2:
                    {
                        // 继续压缩 Title：1* - 0 - (Auto) - 0
                        this.ColumnTitle.Width = new GridLength(1d, GridUnitType.Star);
                        this.ColumnSubtitle.Width = new GridLength(0d, GridUnitType.Pixel);
                        this.ColumnExtend.Width = new GridLength(0d, GridUnitType.Pixel);
                        break;
                    }
            }
        }

    }
}