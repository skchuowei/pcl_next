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
using PCLCS;

namespace PCL
{
    public partial class PageDownloadInstall
    {
        public PageDownloadInstall()
        {
            InitializeComponent();
            this.BtnBack.Click += (p1, p2) => ExitSelectPage();
            this.TextSelectName.TextChanged += (p1, p2) => TextSelectName_TextChanged((object)p1, (TextChangedEventArgs)p2);
            MyTextBox.ValidateChanged += (p1, p2) => TextSelectName_ValidateChanged((object)p1, (EventArgs)p2);
            this.CardOptiFine.PreviewSwap += (p1, p2) => CardOptiFine_PreviewSwap((object)p1, (ModBase.RouteEventArgs)p2);
            this.LoadOptiFine.StateChanged += (p1, p2, p3) => OptiFine_Loaded();
            this.BtnOptiFineClear.Click += (p1, p2) => OptiFine_Clear((object)p1, (EventArgs)p2);
            this.CardLiteLoader.PreviewSwap += (p1, p2) => CardLiteLoader_PreviewSwap((object)p1, (ModBase.RouteEventArgs)p2);
            this.LoadLiteLoader.StateChanged += (p1, p2, p3) => LiteLoader_Loaded();
            this.BtnLiteLoaderClear.Click += (p1, p2) => LiteLoader_Clear((object)p1, (EventArgs)p2);
            this.CardForge.PreviewSwap += (p1, p2) => CardForge_PreviewSwap((object)p1, (ModBase.RouteEventArgs)p2);
            this.LoadForge.StateChanged += (p1, p2, p3) => Forge_Loaded();
            this.BtnForgeClear.Click += (p1, p2) => Forge_Clear((object)p1, (EventArgs)p2);
            this.CardNeoForge.PreviewSwap += (p1, p2) => CardNeoForge_PreviewSwap((object)p1, (ModBase.RouteEventArgs)p2);
            this.LoadNeoForge.StateChanged += (p1, p2, p3) => NeoForge_Loaded();
            this.BtnNeoForgeClear.Click += (p1, p2) => NeoForge_Clear((object)p1, (EventArgs)p2);
            this.CardFabric.PreviewSwap += (p1, p2) => CardFabric_PreviewSwap((object)p1, (ModBase.RouteEventArgs)p2);
            this.LoadFabric.StateChanged += (p1, p2, p3) => Fabric_Loaded();
            this.BtnFabricClear.Click += (p1, p2) => Fabric_Clear((object)p1, (EventArgs)p2);
            this.CardFabricApi.PreviewSwap += (p1, p2) => CardFabricApi_PreviewSwap((object)p1, (ModBase.RouteEventArgs)p2);
            this.LoadFabricApi.StateChanged += (p1, p2, p3) => FabricApi_Loaded();
            this.BtnFabricApiClear.Click += (p1, p2) => FabricApi_Clear((object)p1, (EventArgs)p2);
            this.CardOptiFabric.PreviewSwap += (p1, p2) => CardOptiFabric_PreviewSwap((object)p1, (ModBase.RouteEventArgs)p2);
            // VB 原文是 Handles CardOptiFine.Swap, CardForge.Swap, ... → ReloadSelected()，
            // 迁移后只保留了 PreviewSwap（用于拦截错误），Swap（展开后真正重算预览）全部丢失，
            // 表现为展开组件卡片后右侧/底部的已选组件预览不刷新（2026-11-11 补）。
            this.CardOptiFine.Swap += (p1, p2) => ReloadSelected();
            this.CardLiteLoader.Swap += (p1, p2) => ReloadSelected();
            this.CardForge.Swap += (p1, p2) => ReloadSelected();
            this.CardNeoForge.Swap += (p1, p2) => ReloadSelected();
            this.CardFabric.Swap += (p1, p2) => ReloadSelected();
            this.CardFabricApi.Swap += (p1, p2) => ReloadSelected();
            this.CardOptiFabric.Swap += (p1, p2) => ReloadSelected();
            this.LoadOptiFabric.StateChanged += (p1, p2, p3) => OptiFabric_Loaded();
            this.BtnOptiFabricClear.Click += (p1, p2) => OptiFabric_Clear((object)p1, (EventArgs)p2);
            this.TextSelectName.KeyDown += (p1, p2) => TextSelectName_KeyDown((object)p1, (KeyEventArgs)p2);
            this.BtnStart.Click += (p1, p2) => BtnStart_Click();
            this.Loaded += (_, __) => Init();
            // Initialized 在 InitializeComponent() 内部就已触发，事后订阅收不到，LoaderInit 从不执行：
            // 下载页的加载环没有绑定加载器 → 进度永远停在 0%，直到整批加载结束才一次跳到完成（2026-11-11 修复）。
            // 同时这对派生类 PageDownloadModify（override LoaderInit）也生效。
            LoaderInit();
        }

        public void ControlInit()
        {
            this.DisabledPageAnimControls.Add(this.BtnStart);
            this.LoadOptiFine.State = ModDownload.DlOptiFineListLoader;
            this.LoadLiteLoader.State = ModDownload.DlLiteLoaderListLoader;
            this.LoadFabric.State = ModDownload.DlFabricListLoader;
            this.LoadFabricApi.State = ModDownload.DlFabricApiLoader;
            this.LoadNeoForge.State = ModDownload.DlNeoForgeListLoader;
            this.LoadOptiFabric.State = ModDownload.DlOptiFabricLoader;

            ModDownloadLib.McDownloadForgeRecommendedRefresh();
        }

        public virtual void LoaderInit()
        {
            ControlInit();
            if (ModDownloadPolicy.RestrictMcDownload)
            {
                this.PageLoaderInit(this.LoadMinecraft, this.PanLoad, this.PanAllBack, (FrameworkElement)null, new ModLoader.LoaderTask<string, ModDownload.DlClientListResult>("关闭下载", (_) => { throw new Exception("为遵守相关法律法规，PCL Next 不再提供 MC 下载功能，非常抱歉 qwq……"); }), (_) => LoadMinecraft_OnFinish());
            }
            else
            {
                this.PageLoaderInit(this.LoadMinecraft, this.PanLoad, this.PanAllBack, (FrameworkElement)null, ModDownload.DlClientListLoader, (_) => LoadMinecraft_OnFinish());
            }
        }

        private bool IsLoad = false;
        public virtual void Init()
        {
            if (IsLoad)
            {
                IsSelectNameEdited = false;
                ExitSelectPage();
                if (ModDownload.DlClientListLoader.State == ModBase.LoadState.Finished)
                    TrySelectWaitingVersion();
            }
            IsLoad = true;
            this.PanBack.ScrollToHome();
            ModDownload.DlOptiFineListLoader.Start();
            ModDownload.DlLiteLoaderListLoader.Start();
            ModDownload.DlFabricListLoader.Start();
            ModDownload.DlNeoForgeListLoader.Start();

            // 重载预览
            this.TextSelectName.ValidateRules = new System.Collections.ObjectModel.Collection<ValidateType>() { new ValidateFolderName(ModMinecraft.McFolderSelected + "versions") };
            this.TextSelectName.Validate();
            ReloadSelected();
        }

        #region 页面切换

        // 页面切换动画
        public bool IsInSelectPage = false;

        /// <summary>
    /// 选择页面 UI 初始化。
    /// </summary>
        public void PrepareSelectPage()
        {
            IsInSelectPage = true;
            this.PanInner.Margin = new Thickness(25d, 10d, 25d, 40d);
            this.PanSelect.Visibility = Visibility.Visible;
            this.PanSelect.IsHitTestVisible = true;
            this.PanMinecraft.IsHitTestVisible = false;
            this.PanBack.IsHitTestVisible = false;
            this.PanBack.ScrollToHome();

            this.DisabledPageAnimControls.Remove(this.BtnStart);
            this.BtnStart.Show = true;
            foreach (var Card in new[] { this.CardOptiFine, this.CardLiteLoader, this.CardForge, this.CardNeoForge, this.CardFabric, this.CardFabricApi, this.CardOptiFabric })
                Card.IsSwapped = true;

            // 如果在选择页面按了刷新键，选择页的东西可能会由于动画被隐藏，但不会由于加载结束而再次显示，因此这里需要手动恢复
            foreach (var Control in this.GetAllAnimControls(this.PanSelect))
            {
                Control.Opacity = 1d;
                if (Control.RenderTransform is null || Control.RenderTransform is TranslateTransform)
                {
                    Control.RenderTransform = new TranslateTransform();
                }
            }
        }

        private void EnterSelectPage()
        {
            if (IsInSelectPage)
                return;
            AutoSelectedFabricApi = false;
            AutoSelectedOptiFabric = false;
            PrepareSelectPage();

            // 启动 Forge 加载
            if (McVersion.IsStandardFormat(VanillaName))
            {
                var ForgeLoader = new ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>("DlForgeVersion " + VanillaName, ModDownload.DlForgeVersionMain);
                this.LoadForge.State = ForgeLoader;
                ForgeLoader.Start(VanillaName);
            }

            // 启动 Fabric API、OptiFabric 加载
            ModDownload.DlFabricApiLoader.Start();
            ModDownload.DlOptiFabricLoader.Start();

            ModAnimation.AniStart(new[] {
            ModAnimation.AaOpacity(this.PanMinecraft, -this.PanMinecraft.Opacity, 70, 10),
            ModAnimation.AaTranslateX(this.PanMinecraft, (double)-50 - ((TranslateTransform)this.PanMinecraft.RenderTransform).X, 90, 10),
                        ModAnimation.AaCode(() =>
                {
                this.PanBack.ScrollToHome();
                ReloadComponentLists();
                this.PanMinecraft.Visibility = Visibility.Collapsed;
            }, After: true),
            ModAnimation.AaOpacity(this.PanSelect, 1d - this.PanSelect.Opacity, 70, 100),
            ModAnimation.AaTranslateX(this.PanSelect, -((TranslateTransform)this.PanSelect.RenderTransform).X, 160, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.ExtraStrong)),
            ModAnimation.AaCode(() => this.PanBack.IsHitTestVisible = true, After: true)
        }, "FrmDownloadInstall SelectPageSwitch", true);
        }

        public void ReloadComponentLists()
        {
            this.TextSelectName.Validate();
            OptiFine_Loaded();
            LiteLoader_Loaded();
            Forge_Loaded();
            NeoForge_Loaded();
            Fabric_Loaded();
            FabricApi_Loaded();
            OptiFabric_Loaded();
            ReloadSelected();
        }

        public void ExitSelectPage()
        {
            if (!IsInSelectPage)
                return;
            IsInSelectPage = false;

            this.PanInner.Margin = new Thickness(25d, 10d, 25d, 25d);

            this.DisabledPageAnimControls.Add(this.BtnStart);
            this.BtnStart.Show = false;
            ClearSelected(); // 清除已选择项
            this.PanMinecraft.Visibility = Visibility.Visible;
            this.PanSelect.IsHitTestVisible = false;
            this.PanMinecraft.IsHitTestVisible = true;
            this.PanBack.IsHitTestVisible = false;
            this.PanBack.ScrollToHome();

            ModAnimation.AniStart(new[] {
            ModAnimation.AaOpacity(this.PanSelect, -this.PanSelect.Opacity, 70, 10),
            ModAnimation.AaTranslateX(this.PanSelect, 50d - ((TranslateTransform)this.PanSelect.RenderTransform).X, 90, 10),
            ModAnimation.AaCode(() => this.PanBack.ScrollToHome(), After: true),
            ModAnimation.AaOpacity(this.PanMinecraft, 1d - this.PanMinecraft.Opacity, 70, 100),
            ModAnimation.AaTranslateX(this.PanMinecraft, -((TranslateTransform)this.PanMinecraft.RenderTransform).X, 160, 100, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.ExtraStrong)),
                        ModAnimation.AaCode(() =>
                {
                this.PanSelect.Visibility = Visibility.Collapsed;
                this.PanBack.IsHitTestVisible = true;
            }, After: true)
        }, "FrmDownloadInstall SelectPageSwitch");
        }
        public virtual void MinecraftSelected(MyListItem sender, MouseButtonEventArgs e)
        {
            VanillaName = sender.Title;
            VanillaData = (JObject)sender.Tag;
            VanillaIcon = sender.Logo;
            this.LabMinecraftVersion.Text = VanillaName;
            EnterSelectPage();
        }

        #endregion

        #region 选择

        // Minecraft
        public string VanillaName;
        public JObject VanillaData;
        public string VanillaIcon;
        private int VanillaDrop
        {
            get
            {
                return McVersion.VersionToDrop(VanillaName);
            }
        }

        // 附加组件
        public ModDownload.DlOptiFineListEntry SelectedOptiFine = null;
        public ModDownload.DlLiteLoaderListEntry SelectedLiteLoader = null;
        public ModDownload.DlForgeVersionEntry SelectedForge = null;
        public ModDownload.DlNeoForgeListEntry SelectedNeoForge = null;
        public string SelectedFabric = null;
        public ResourceVersion SelectedFabricApi = null;
        public ResourceVersion SelectedOptiFabric = null;
        private bool _ReloadSelected_Ongoing = false; // #3742 中，LoadOptiFineGetError 会初始化 LoadOptiFine，触发事件 LoadOptiFine.StateChanged，导致再次调用 SelectReload

        /// <summary>
    /// 重载已选择的项目的显示。
    /// </summary>
        public virtual void ReloadSelected()
        {
            if (VanillaName is null || _ReloadSelected_Ongoing)
                return;
            _ReloadSelected_Ongoing = true;
            // 主预览
            SelectNameUpdate();
            this.ImgLogo.Source = GetSelectLogo();
            this.ImgMinecraft.Source = VanillaIcon;
            // OptiFine
            string OptiFineError = LoadOptiFineGetError();
            this.CardOptiFine.MainSwap.Visibility = OptiFineError is null ? Visibility.Visible : Visibility.Collapsed;
            if (OptiFineError is not null)
                this.CardOptiFine.IsSwapped = true; // 例如在同时展开卡片时选择了不兼容项则强制折叠
            this.SetPanelVisibility(this.PanOptiFineInfo, this.CardOptiFine.IsSwapped);
            if (SelectedOptiFine is null)
            {
                this.BtnOptiFineClear.Visibility = Visibility.Collapsed;
                this.ImgOptiFine.Visibility = Visibility.Collapsed;
                this.LabOptiFine.Text = OptiFineError ?? "可以添加";
                this.LabOptiFine.Foreground = ModSecret.ColorGray4;
            }
            else
            {
                this.BtnOptiFineClear.Visibility = Visibility.Visible;
                this.ImgOptiFine.Visibility = Visibility.Visible;
                this.LabOptiFine.Text = SelectedOptiFine.DisplayName.Replace(VanillaName + " ", "");
                this.LabOptiFine.Foreground = ModSecret.ColorGray1;
            }
            // LiteLoader
            if (VanillaDrop >= 130)
            {
                this.CardLiteLoader.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.CardLiteLoader.Visibility = Visibility.Visible;
                string LiteLoaderError = LoadLiteLoaderGetError();
                this.CardLiteLoader.MainSwap.Visibility = LiteLoaderError is null ? Visibility.Visible : Visibility.Collapsed;
                if (LiteLoaderError is not null)
                    this.CardLiteLoader.IsSwapped = true; // 例如在同时展开卡片时选择了不兼容项则强制折叠
                this.SetPanelVisibility(this.PanLiteLoaderInfo, this.CardLiteLoader.IsSwapped);
                if (SelectedLiteLoader is null)
                {
                    this.BtnLiteLoaderClear.Visibility = Visibility.Collapsed;
                    this.ImgLiteLoader.Visibility = Visibility.Collapsed;
                    this.LabLiteLoader.Text = LiteLoaderError ?? "可以添加";
                    this.LabLiteLoader.Foreground = ModSecret.ColorGray4;
                }
                else
                {
                    this.BtnLiteLoaderClear.Visibility = Visibility.Visible;
                    this.ImgLiteLoader.Visibility = Visibility.Visible;
                    this.LabLiteLoader.Text = SelectedLiteLoader.Inherit;
                    this.LabLiteLoader.Foreground = ModSecret.ColorGray1;
                }
            }
            // Forge
            if (!McVersion.IsStandardFormat(VanillaName))
            {
                this.CardForge.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.CardForge.Visibility = Visibility.Visible;
                string ForgeError = LoadForgeGetError();
                this.CardForge.MainSwap.Visibility = ForgeError is null ? Visibility.Visible : Visibility.Collapsed;
                if (ForgeError is not null)
                    this.CardForge.IsSwapped = true;
                this.SetPanelVisibility(this.PanForgeInfo, this.CardForge.IsSwapped);
                if (SelectedForge is null)
                {
                    this.BtnForgeClear.Visibility = Visibility.Collapsed;
                    this.ImgForge.Visibility = Visibility.Collapsed;
                    this.LabForge.Text = ForgeError ?? "可以添加";
                    this.LabForge.Foreground = ModSecret.ColorGray4;
                }
                else
                {
                    this.BtnForgeClear.Visibility = Visibility.Visible;
                    this.ImgForge.Visibility = Visibility.Visible;
                    this.LabForge.Text = SelectedForge.VersionName;
                    this.LabForge.Foreground = ModSecret.ColorGray1;
                }
            }
            // NeoForge
            DateTime vanillaReleaseTime = default;
            var vanillaRt = VanillaData["releaseTime"];
            if (vanillaRt is not null && vanillaRt.Type != JTokenType.Null)
            {
                try { vanillaReleaseTime = vanillaRt.ToObject<DateTime>(); } catch { vanillaReleaseTime = default; }
            }
            if (vanillaReleaseTime < new DateTime(2023, 6, 11)) // 匹配 1.20.1+ 与一些愚人节版本
            {
                this.CardNeoForge.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.CardNeoForge.Visibility = Visibility.Visible;
                string NeoForgeError = LoadNeoForgeGetError();
                this.CardNeoForge.MainSwap.Visibility = NeoForgeError is null ? Visibility.Visible : Visibility.Collapsed;
                if (NeoForgeError is not null)
                    this.CardNeoForge.IsSwapped = true;
                this.SetPanelVisibility(this.PanNeoForgeInfo, this.CardNeoForge.IsSwapped);
                if (SelectedNeoForge is null)
                {
                    this.BtnNeoForgeClear.Visibility = Visibility.Collapsed;
                    this.ImgNeoForge.Visibility = Visibility.Collapsed;
                    this.LabNeoForge.Text = NeoForgeError ?? "可以添加";
                    this.LabNeoForge.Foreground = ModSecret.ColorGray4;
                }
                else
                {
                    this.BtnNeoForgeClear.Visibility = Visibility.Visible;
                    this.ImgNeoForge.Visibility = Visibility.Visible;
                    this.LabNeoForge.Text = SelectedNeoForge.VersionName;
                    this.LabNeoForge.Foreground = ModSecret.ColorGray1;
                }
            }
            // Fabric
            if (VanillaDrop <= 130)
            {
                this.CardFabric.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.CardFabric.Visibility = Visibility.Visible;
                string FabricError = LoadFabricGetError();
                this.CardFabric.MainSwap.Visibility = FabricError is null ? Visibility.Visible : Visibility.Collapsed;
                if (FabricError is not null)
                    this.CardFabric.IsSwapped = true;
                this.SetPanelVisibility(this.PanFabricInfo, this.CardFabric.IsSwapped);
                if (SelectedFabric is null)
                {
                    this.BtnFabricClear.Visibility = Visibility.Collapsed;
                    this.ImgFabric.Visibility = Visibility.Collapsed;
                    this.LabFabric.Text = FabricError ?? "可以添加";
                    this.LabFabric.Foreground = ModSecret.ColorGray4;
                }
                else
                {
                    this.BtnFabricClear.Visibility = Visibility.Visible;
                    this.ImgFabric.Visibility = Visibility.Visible;
                    this.LabFabric.Text = SelectedFabric.Replace("+build", "");
                    this.LabFabric.Foreground = ModSecret.ColorGray1;
                }
            }
            // FabricApi
            if (SelectedFabric is null)
            {
                this.CardFabricApi.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.CardFabricApi.Visibility = Visibility.Visible;
                string FabricApiError = LoadFabricApiGetError();
                this.CardFabricApi.MainSwap.Visibility = FabricApiError is null ? Visibility.Visible : Visibility.Collapsed;
                if (FabricApiError is not null || SelectedFabric is null)
                    this.CardFabricApi.IsSwapped = true;
                this.SetPanelVisibility(this.PanFabricApiInfo, this.CardFabricApi.IsSwapped);
                if (SelectedFabricApi is null)
                {
                    this.BtnFabricApiClear.Visibility = Visibility.Collapsed;
                    this.ImgFabricApi.Visibility = Visibility.Collapsed;
                    this.LabFabricApi.Text = FabricApiError ?? "可以添加";
                    this.LabFabricApi.Foreground = ModSecret.ColorGray4;
                }
                else
                {
                    this.BtnFabricApiClear.Visibility = Visibility.Visible;
                    this.ImgFabricApi.Visibility = Visibility.Visible;
                    var apiDisplay = SelectedFabricApi.Display ?? "";
                    var apiParts = apiDisplay.Split("]");
                    this.LabFabricApi.Text = (apiParts.Length > 1 ? apiParts[1] : apiDisplay).Replace("Fabric API ", "").Replace(" build ", ".").Split("+").First().Trim();
                    this.LabFabricApi.Foreground = ModSecret.ColorGray1;
                }
            }
            // OptiFabric
            if (SelectedFabric is null || SelectedOptiFine is null)
            {
                this.CardOptiFabric.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.CardOptiFabric.Visibility = Visibility.Visible;
                string OptiFabricError = LoadOptiFabricGetError();
                this.CardOptiFabric.MainSwap.Visibility = OptiFabricError is null ? Visibility.Visible : Visibility.Collapsed;
                if (OptiFabricError is not null || SelectedFabric is null)
                    this.CardOptiFabric.IsSwapped = true;
                this.SetPanelVisibility(this.PanOptiFabricInfo, this.CardOptiFabric.IsSwapped);
                if (SelectedOptiFabric is null)
                {
                    this.BtnOptiFabricClear.Visibility = Visibility.Collapsed;
                    this.ImgOptiFabric.Visibility = Visibility.Collapsed;
                    this.LabOptiFabric.Text = OptiFabricError ?? "可以添加";
                    this.LabOptiFabric.Foreground = ModSecret.ColorGray4;
                }
                else
                {
                    this.BtnOptiFabricClear.Visibility = Visibility.Visible;
                    this.ImgOptiFabric.Visibility = Visibility.Visible;
                    this.LabOptiFabric.Text = SelectedOptiFabric.Display.Lower().Replace("optifabric-", "").Replace(".jar", "").Trim().TrimStart('v');
                    this.LabOptiFabric.Foreground = ModSecret.ColorGray1;
                }
            }
            // 主警告
            if (SelectedFabric is not null && SelectedFabricApi is null)
            {
                this.HintFabricAPI.Visibility = Visibility.Visible;
            }
            else
            {
                this.HintFabricAPI.Visibility = Visibility.Collapsed;
            }
            if (SelectedFabric is not null && SelectedOptiFine is not null && SelectedOptiFabric is null)
            {
                if (VanillaDrop >= 140 && VanillaDrop <= 150)
                {
                    this.HintOptiFabric.Visibility = Visibility.Collapsed;
                    this.HintOptiFabricOld.Visibility = Visibility.Visible;
                }
                else
                {
                    this.HintOptiFabric.Visibility = Visibility.Visible;
                    this.HintOptiFabricOld.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                this.HintOptiFabric.Visibility = Visibility.Collapsed;
                this.HintOptiFabricOld.Visibility = Visibility.Collapsed;
            }
            if (VanillaDrop >= 160 && SelectedOptiFine is not null && (SelectedForge is not null || SelectedFabric is not null))
            {
                this.HintModOptiFine.Visibility = Visibility.Visible;
            }
            else
            {
                this.HintModOptiFine.Visibility = Visibility.Collapsed;
            }
            // 结束
            _ReloadSelected_Ongoing = false;
        }
        /// <summary>
    /// 清空已选择的项目。
    /// </summary>
        public void ClearSelected()
        {
            VanillaName = null;
            VanillaData = null;
            VanillaIcon = null;
            SelectedOptiFine = null;
            SelectedLiteLoader = null;
            SelectedForge = null;
            SelectedNeoForge = null;
            SelectedFabric = null;
            SelectedFabricApi = null;
            SelectedOptiFabric = null;
            foreach (var ClearButton in new[] { this.BtnOptiFineClear, this.BtnLiteLoaderClear, this.BtnForgeClear, this.BtnNeoForgeClear, this.BtnFabricClear, this.BtnFabricApiClear, this.BtnOptiFabricClear })
                ClearButton.Visibility = Visibility.Collapsed;
        }
        // 信息栏动画
        private void SetPanelVisibility(Grid Panel, bool IsVisible)
        {
            if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(Panel.Tag, IsVisible.ToString(), false)))
                return;
            Panel.Tag = IsVisible.ToString();
            if (IsVisible)
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaTranslateY(Panel, -((TranslateTransform)Panel.RenderTransform).Y, 150, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaOpacity(Panel, 1d - Panel.Opacity, 60) }, "PageDownloadInstall Visibility " + Panel.Name);
            }
            else
            {
                ModAnimation.AniStart(new[] { ModAnimation.AaTranslateY(Panel, 6d - ((TranslateTransform)Panel.RenderTransform).Y, 60), ModAnimation.AaOpacity(Panel, -Panel.Opacity, 60) }, "PageDownloadInstall Visibility " + Panel.Name);
            }
        }

        /// <summary>
    /// 获取版本图标。
    /// </summary>
        public virtual string GetSelectLogo()
        {
            if (SelectedFabric is not null)
            {
                return "pack://application:,,,/images/Blocks/Fabric.png";
            }
            else if (SelectedForge is not null)
            {
                return "pack://application:,,,/images/Blocks/Anvil.png";
            }
            else if (SelectedNeoForge is not null)
            {
                return "pack://application:,,,/images/Blocks/NeoForge.png";
            }
            else if (SelectedLiteLoader is not null)
            {
                return "pack://application:,,,/images/Blocks/Egg.png";
            }
            else if (SelectedOptiFine is not null)
            {
                return "pack://application:,,,/images/Blocks/GrassPath.png";
            }
            else
            {
                return VanillaIcon;
            }
        }

        // 版本名处理
        /// <summary>
    /// 获取默认版本名。
    /// </summary>
        public string GetSelectName()
        {
            string Name = VanillaName;
            if (SelectedFabric is not null)
                Name += "-Fabric " + SelectedFabric.BeforeFirst("+");
            if (SelectedForge is not null)
                Name += "-Forge_" + SelectedForge.VersionName;
            if (SelectedNeoForge is not null)
                Name += "-NeoForge_" + SelectedNeoForge.VersionName.BeforeFirst("+");
            if (SelectedLiteLoader is not null)
                Name += "-LiteLoader";
            if (SelectedOptiFine is not null)
                Name += "-OptiFine_" + SelectedOptiFine.DisplayName.Replace(VanillaName + " ", "").Replace(" ", "_");
            return Name;
        }
        public bool IsSelectNameEdited = false;
        private void SelectNameUpdate()
        {
            if (IsSelectNameEdited)
                return;
            this.TextSelectName.Text = GetSelectName();
            IsSelectNameEdited = false;
        }
        private void TextSelectName_TextChanged(object sender, TextChangedEventArgs e)
        {
            IsSelectNameEdited = true;
        }
        private void TextSelectName_ValidateChanged(object sender, EventArgs e)
        {
            this.BtnStart.IsEnabled = this.TextSelectName.IsValidated;
            this.HintSelectName.Text = this.TextSelectName.ValidateResult;
            this.HintSelectName.Visibility = this.TextSelectName.IsValidated ? Visibility.Collapsed : Visibility.Visible;
        }

        #endregion

        #region 加载器
        public JArray MinecraftVersions;
        private void LoadMinecraft_OnFinish()
        {
            MinecraftVersions = (JArray)ModDownload.DlClientListLoader.Output.Value["versions"].DeepClone();
            ShowMinecraftVersionList();
            TrySelectWaitingVersion(); // 自动选择版本
        }

        // 结果数据化
        public virtual void ShowMinecraftVersionList()
        {
            ExitSelectPage(); // 返回
            try
            {
                var Dict = new Dictionary<string, List<JObject>>() { { "正式版", new List<JObject>() }, { "预览版", new List<JObject>() }, { "远古版", new List<JObject>() }, { "愚人节版", new List<JObject>() } };
                foreach (JObject Version in MinecraftVersions)
                {
                    // 确定分类
                    string Type = (string)Version["type"];
                    string versionId = Version["id"]?.ToString() ?? "";
                    switch (Type ?? "")
                    {
                        case "release":
                            {
                                Type = "正式版";
                                break;
                            }
                        case "snapshot":
                            {
                                Type = "预览版";
                                // Mojang 误分类
                                if (versionId.StartsWithF("1.") && !versionId.Lower().Contains("combat") && !versionId.Lower().Contains("rc") && !versionId.Lower().Contains("experimental") && !versionId.Lower().Contains("pre"))
                                {
                                    Type = "正式版";
                                    Version["type"] = "release";
                                }
                                // 愚人节版本
                                switch (versionId.Lower() ?? "")
                                {
                                    case "20w14infinite":
                                    case "20w14∞":
                                        {
                                            Type = "愚人节版";
                                            Version["id"] = "20w14∞";
                                            Version["type"] = "special";
                                            Version.Add("lore", ModMinecraft.GetMcFoolName(versionId));
                                            break;
                                        }
                                    case "3d shareware v1.34":
                                    case "1.rv-pre1":
                                    case "15w14a":
                                    case "2.0":
                                    case "22w13oneblockatatime":
                                    case "23w13a_or_b":
                                    case "24w14potato":
                                    case "25w14craftmine":
                                    case "26w14a":
                                        {
                                            Type = "愚人节版";
                                            Version["type"] = "special";
                                            Version.Add("lore", ModMinecraft.GetMcFoolName(versionId)); // 4/1 自动视作愚人节版
                                            break;
                                        }

                                    default:
                                        {
                                            DateTime releaseDate = default;
                                            var rtToken = Version["releaseTime"];
                                            if (rtToken is not null && rtToken.Type != JTokenType.Null)
                                            {
                                                try { releaseDate = rtToken.Value<DateTime>().ToUniversalTime().AddHours(2d); } catch { releaseDate = default; }
                                            }
                                            if (releaseDate.Month == 4 && releaseDate.Day == 1)
                                            {
                                                Type = "愚人节版";
                                                Version["type"] = "special";
                                            }

                                            break;
                                        }
                                }

                                break;
                            }
                        case "special":
                            {
                                // 已被处理的愚人节版
                                Type = "愚人节版";
                                break;
                            }

                        default:
                            {
                                Type = "远古版";
                                break;
                            }
                    }
                    // 加入辞典
                    Dict[Type].Add(Version);
                }
                // 排序
                foreach (var Pair in Dict.ToList())
                    Dict[Pair.Key] = Pair.Value.OrderByDescending(j =>
                    {
                        var jrt = j["releaseTime"];
                        if (jrt is not null && jrt.Type != JTokenType.Null)
                        {
                            try { return jrt.Value<DateTime>(); } catch { }
                        }
                        return default;
                    }).ToList();
                // 清空当前
                this.PanMinecraft.Children.Clear();
                // 添加最新版本
                var CardInfo = new MyCard() { Title = "最新版本", Margin = new Thickness(0d, 15d, 0d, 15d) };
                var PinnedVersions = new List<JObject>();
                if (Dict["正式版"].Count > 0)
                {
                    JObject Release = (JObject)Dict["正式版"][0].DeepClone();
                    DateTime releaseDate = default;
                    var releaseRt = Release["releaseTime"];
                    if (releaseRt is not null && releaseRt.Type != JTokenType.Null)
                    {
                        try { releaseDate = releaseRt.Value<DateTime>(); } catch { releaseDate = default; }
                    }
                    Release["lore"] = "最新正式版，发布于 " + releaseDate.ToString("yyyy'/'MM'/'dd HH':'mm");
                    PinnedVersions.Add(Release);
                    if (Dict["预览版"].Count > 0)
                    {
                        DateTime previewDate = default;
                        var previewRt = Dict["预览版"][0]["releaseTime"];
                        if (previewRt is not null && previewRt.Type != JTokenType.Null)
                        {
                            try { previewDate = previewRt.Value<DateTime>(); } catch { previewDate = default; }
                        }
                        if (releaseDate < previewDate)
                        {
                            JObject Snapshot = (JObject)Dict["预览版"][0].DeepClone();
                            Snapshot["lore"] = "最新预览版，发布于 " + previewDate.ToString("yyyy'/'MM'/'dd HH':'mm");
                            PinnedVersions.Add(Snapshot);
                        }
                    }
                }
                var PanInfo = new StackPanel() { Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d), VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d) };
                CardInfo.Children.Add(PanInfo);
                CardInfo.SwapControl = PanInfo;
                CardInfo.SetContentBuilder(PinnedVersions, Version => ModDownloadLib.McDownloadListItem(Version, (sender, e) => MinecraftSelected((MyListItem)sender, e), false)); // 使用 Lambda 转发点击事件，避免 #535
                CardInfo.BuildContent();
                this.PanMinecraft.Children.Insert(0, CardInfo);
                // 添加其他版本
                foreach (KeyValuePair<string, List<JObject>> Pair in Dict)
                {
                    if (!Pair.Value.Any())
                        continue;
                    // 增加卡片
                    var NewCard = new MyCard() { Title = Pair.Key, Margin = new Thickness(0d, 0d, 0d, 15d) };
                    var NewStack = new StackPanel() { Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d), VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d) };
                    NewCard.Children.Add(NewStack);
                    NewCard.SwapControl = NewStack;
                    NewCard.SetContentBuilder(Pair.Value, Version => ModDownloadLib.McDownloadListItem(Version, (sender, e) => MinecraftSelected((MyListItem)sender, e), false));
                    NewCard.IsSwapped = true;
                    this.PanMinecraft.Children.Add(NewCard);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化安装版本列表出错");
            }
        }
        private void TrySelectWaitingVersion()
        {
            if (VersionWaitingSelect is null)
                return;
            foreach (JObject Version in MinecraftVersions)
            {
                if ((Version["id"]?.ToString() ?? "") != (VersionWaitingSelect ?? ""))
                    continue;
                Logger.Info($"自动选择 MC 版本：{VersionWaitingSelect}");
                MinecraftSelected(ModDownloadLib.McDownloadListItem(Version, null, false).Init(), null);
                VersionWaitingSelect = null;
                return;
            }
        }

        /// <summary>
    /// 当 MC 版本列表加载完时，立即自动选择的版本。用于外部调用。
    /// </summary>
        public static string VersionWaitingSelect = null;

        #endregion

        #region OptiFine 列表

        /// <summary>
    /// 获取 OptiFine 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
        public string LoadOptiFineGetError()
        {
            if (SelectedNeoForge is not null)
                return "与 NeoForge 不兼容";
            // 检查 Forge 1.13 - 1.14.3：全部不兼容
            if (SelectedForge is not null && McUtils.CompareVersionGE(VanillaName, "1.13") && McUtils.CompareVersionGE("1.14.3", VanillaName))
            {
                return "与 Forge 不兼容";
            }
            // 检查 Fabric 1.20.5+：全部不兼容
            if (SelectedFabric is not null && McUtils.CompareVersion(VanillaName, "1.20.4") > 0)
            {
                return "与 Fabric 不兼容";
            }
            // 检查 Loader
            if (this.GetLoaderError(this.LoadOptiFine) is not null)
                return this.GetLoaderError(this.LoadOptiFine);
            // 检查 Forge 版本
            bool HasAny = false;
            bool HasRequiredVersion = false;
            foreach (ModDownload.DlOptiFineListEntry OptiFineVersion in ModDownload.DlOptiFineListLoader.Output.Value)
            {
                if (!OptiFineVersion.DisplayName.StartsWithF(VanillaName + " "))
                    continue; // 不是同一个大版本
                HasAny = true;
                if (SelectedForge is null)
                    return null; // 未选择 Forge
                if (IsOptiFineSuitForForge(OptiFineVersion, SelectedForge))
                    return null; // 该版本可用
                if (OptiFineVersion.RequiredForgeVersion is not null)
                    HasRequiredVersion = true;
            }
            if (!HasAny)
            {
                return "无";
            }
            else if (HasRequiredVersion)
            {
                return "仅兼容特定版本的 Forge";
            }
            else
            {
                return "与 Forge 不兼容";
            }
        }

        // 检查某个 OptiFine 是否与某个 Forge 兼容
        public static bool IsOptiFineSuitForForge(ModDownload.DlOptiFineListEntry OptiFine, ModDownload.DlForgeVersionEntry Forge)
        {
            if ((Forge.Inherit ?? "") != (OptiFine.Inherit ?? ""))
                return false; // 不是同一个大版本
            if (OptiFine.RequiredForgeVersion is null)
                return false; // 不兼容 Forge
            if (string.IsNullOrWhiteSpace(OptiFine.RequiredForgeVersion))
                return true; // #4183
            if (OptiFine.RequiredForgeVersion.Contains(".")) // XX.X.XXX
            {
                return McUtils.CompareVersion(Forge.Version.ToString(), OptiFine.RequiredForgeVersion) == 0;
            }
            else // XXXX
            {
                return Forge.Version.Revision == Conversions.ToDouble(OptiFine.RequiredForgeVersion);
            }
        }

        // 限制展开
        private void CardOptiFine_PreviewSwap(object sender, ModBase.RouteEventArgs e)
        {
            if (LoadOptiFineGetError() is not null)
                e.Handled = true;
        }

        /// <summary>
    /// 尝试重新可视化 OptiFine 版本列表。
    /// </summary>
        private void OptiFine_Loaded()
        {
            try
            {
                if (ModDownload.DlOptiFineListLoader.State != ModBase.LoadState.Finished)
                    return;

                // 获取版本列表
                var Versions = new List<ModDownload.DlOptiFineListEntry>();
                foreach (ModDownload.DlOptiFineListEntry Version in ModDownload.DlOptiFineListLoader.Output.Value)
                {
                    if (SelectedForge is not null && !IsOptiFineSuitForForge(Version, SelectedForge))
                        continue;
                    if (Version.DisplayName.StartsWithF(VanillaName + " "))
                        Versions.Add(Version);
                }
                if (!Versions.Any())
                    return;
                // 排序
                Versions = Versions.SortByComparison((Left, Right) =>
        {
            if (!Left.IsPreview && Right.IsPreview)
                return true;
            if (Left.IsPreview && !Right.IsPreview)
                return false;
            return McUtils.CompareVersionGE(Left.DisplayName, Right.DisplayName);
        });
                // 可视化
                this.PanOptiFine.Children.Clear();
                foreach (var Version in Versions)
                    this.PanOptiFine.Children.Add(ModDownloadLib.OptiFineDownloadListItem(Version, (_, __) => this.OptiFine_Selected((dynamic)_, (dynamic)__), false));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化 OptiFine 安装版本列表出错");
            }
        }

        // 选择与清除
        private void OptiFine_Selected(MyListItem sender, EventArgs e)
        {
            SelectedOptiFine = (ModDownload.DlOptiFineListEntry)sender.Tag;
            if (SelectedForge is not null && !IsOptiFineSuitForForge(SelectedOptiFine, SelectedForge))
                SelectedForge = null;
            OptiFabric_Loaded();
            Forge_Loaded();
            NeoForge_Loaded();
            this.CardOptiFine.IsSwapped = true;
            ReloadSelected();
        }
        private void OptiFine_Clear(object sender, EventArgs e)
        {
            SelectedOptiFine = null;
            SelectedOptiFabric = null;
            this.BtnOptiFabricClear.Visibility = Visibility.Collapsed;
            AutoSelectedOptiFabric = false;
            this.CardOptiFine.IsSwapped = true;
            Forge_Loaded();
            NeoForge_Loaded();
            ReloadSelected();
        }

        #endregion

        #region LiteLoader 列表

        /// <summary>
    /// 获取 LiteLoader 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
        public string LoadLiteLoaderGetError()
        {
            // 检查 Loader
            if (this.GetLoaderError(this.LoadLiteLoader) is not null)
                return this.GetLoaderError(this.LoadLiteLoader);
            // 检查版本
            return ModDownload.DlLiteLoaderListLoader.Output.Value.Any(v => (v.Inherit ?? "") == (VanillaName ?? "")) ? null : "无";
        }

        // 限制展开
        private void CardLiteLoader_PreviewSwap(object sender, ModBase.RouteEventArgs e)
        {
            if (LoadLiteLoaderGetError() is not null)
                e.Handled = true;
        }

        /// <summary>
    /// 尝试重新可视化 LiteLoader 版本列表。
    /// </summary>
        private void LiteLoader_Loaded()
        {
            try
            {
                if (ModDownload.DlLiteLoaderListLoader.State != ModBase.LoadState.Finished)
                    return;
                // 获取版本列表
                var Versions = new List<ModDownload.DlLiteLoaderListEntry>();
                foreach (ModDownload.DlLiteLoaderListEntry Version in ModDownload.DlLiteLoaderListLoader.Output.Value)
                {
                    if ((Version.Inherit ?? "") == (VanillaName ?? ""))
                        Versions.Add(Version);
                }
                if (!Versions.Any())
                    return;
                // 可视化
                this.PanLiteLoader.Children.Clear();
                foreach (var Version in Versions)
                    this.PanLiteLoader.Children.Add(ModDownloadLib.LiteLoaderDownloadListItem(Version, (_, __) => this.LiteLoader_Selected((dynamic)_, (dynamic)__), false));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化 LiteLoader 安装版本列表出错");
            }
        }

        // 选择与清除
        private void LiteLoader_Selected(MyListItem sender, EventArgs e)
        {
            SelectedLiteLoader = (ModDownload.DlLiteLoaderListEntry)sender.Tag;
            this.CardLiteLoader.IsSwapped = true;
            ReloadSelected();
        }
        private void LiteLoader_Clear(object sender, EventArgs e)
        {
            SelectedLiteLoader = null;
            this.CardLiteLoader.IsSwapped = true;
            ReloadSelected();
        }

        #endregion

        #region Forge 列表

        /// <summary>
    /// 获取 Forge 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
        public string LoadForgeGetError()
        {
            if (McUtils.CompareVersionGE("1.5.1", VanillaName) && McUtils.CompareVersionGE(VanillaName, "1.1"))
                return "无";
            // 检查 Loader
            if (this.GetLoaderError(this.LoadForge) is not null)
                return this.GetLoaderError(this.LoadForge);
            ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>> Loader = (ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>)this.LoadForge.State;
            if ((VanillaName ?? "") != (Loader.Input ?? ""))
                return "获取中……";
            // 检查版本
            foreach (var Version in Loader.Output)
            {
                if (Version.Category == "universal" || Version.Category == "client")
                    continue; // 跳过无法自动安装的版本
                if (SelectedNeoForge is not null)
                    return "与 NeoForge 不兼容";
                if (SelectedFabric is not null)
                    return "与 Fabric 不兼容";
                if (SelectedOptiFine is not null && McUtils.CompareVersionGE(VanillaName, "1.13") && McUtils.CompareVersionGE("1.14.3", VanillaName))
                {
                    return "与 OptiFine 不兼容"; // 1.13 ~ 1.14.3 OptiFine 检查
                }
                if (SelectedOptiFine is not null && !IsOptiFineSuitForForge(SelectedOptiFine, Version))
                    continue;
                return null;
            }
            return "与 OptiFine 不兼容";
        }

        // 限制展开
        private void CardForge_PreviewSwap(object sender, ModBase.RouteEventArgs e)
        {
            if (LoadForgeGetError() is not null)
                e.Handled = true;
        }

        /// <summary>
    /// 尝试重新可视化 Forge 版本列表。
    /// </summary>
        private void Forge_Loaded()
        {
            try
            {
                if (!this.LoadForge.State.IsLoader)
                    return;
                ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>> Loader = (ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>)this.LoadForge.State;
                if ((VanillaName ?? "") != (Loader.Input ?? ""))
                    return;
                if (Loader.State != ModBase.LoadState.Finished)
                    return;
                // 获取要显示的版本
                var Versions = Loader.Output.ToList(); // 复制数组，以免 Output 在实例化后变空
                if (!Loader.Output.Any())
                    return;
                this.PanForge.Children.Clear();
                Versions = Versions.Where(v =>
        {
            if (v.Category == "universal" || v.Category == "client")
                return false; // 跳过无法自动安装的版本
            if (SelectedOptiFine is not null && !IsOptiFineSuitForForge(SelectedOptiFine, v))
                return false;
            return true;
        }).OrderByDescending(v => v).ToList();
                ModDownloadLib.ForgeDownloadListItemPreload(this.PanForge, Versions, (_, __) => this.Forge_Selected((dynamic)_, (dynamic)__), false);
                foreach (var Version in Versions)
                    this.PanForge.Children.Add(ModDownloadLib.ForgeDownloadListItem(Version, (_, __) => this.Forge_Selected((dynamic)_, (dynamic)__), false));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化 Forge 安装版本列表出错");
            }
        }

        // 选择与清除
        private void Forge_Selected(MyListItem sender, EventArgs e)
        {
            SelectedForge = (ModDownload.DlForgeVersionEntry)sender.Tag;
            this.CardForge.IsSwapped = true;
            if (SelectedOptiFine is not null && !IsOptiFineSuitForForge(SelectedOptiFine, SelectedForge))
                SelectedOptiFine = null;
            OptiFine_Loaded();
            ReloadSelected();
        }
        private void Forge_Clear(object sender, EventArgs e)
        {
            SelectedForge = null;
            this.CardForge.IsSwapped = true;
            OptiFine_Loaded();
            ReloadSelected();
        }

        #endregion

        #region NeoForge 列表

        /// <summary>
    /// 获取 NeoForge 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
        public string LoadNeoForgeGetError()
        {
            if (SelectedOptiFine is not null)
                return "与 OptiFine 不兼容";
            if (SelectedForge is not null)
                return "与 Forge 不兼容";
            if (SelectedFabric is not null)
                return "与 Fabric 不兼容";
            // 检查 Loader
            if (this.GetLoaderError(this.LoadNeoForge) is not null)
                return this.GetLoaderError(this.LoadNeoForge);
            // 检查版本
            return ModDownload.DlNeoForgeListLoader.Output.Value.Any(v => (v.Inherit ?? "") == (VanillaName ?? "")) ? null : "无";
        }

        // 限制展开
        private void CardNeoForge_PreviewSwap(object sender, ModBase.RouteEventArgs e)
        {
            if (LoadNeoForgeGetError() is not null)
                e.Handled = true;
        }

        /// <summary>
    /// 尝试重新可视化 NeoForge 版本列表。
    /// </summary>
        private void NeoForge_Loaded()
        {
            try
            {
                // 获取版本列表
                if (ModDownload.DlNeoForgeListLoader.State != ModBase.LoadState.Finished)
                    return;
                var Versions = ModDownload.DlNeoForgeListLoader.Output.Value.Where(v => (v.Inherit ?? "") == (VanillaName ?? "")).ToList();
                if (!Versions.Any())
                    return;
                // 可视化
                this.PanNeoForge.Children.Clear();
                ModDownloadLib.NeoForgeDownloadListItemPreload(this.PanNeoForge, Versions, (_, __) => this.NeoForge_Selected((dynamic)_, (dynamic)__), false);
                foreach (var Version in Versions)
                    this.PanNeoForge.Children.Add(ModDownloadLib.NeoForgeDownloadListItem(Version, (_, __) => this.NeoForge_Selected((dynamic)_, (dynamic)__), false));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化 NeoForge 安装版本列表出错");
            }
        }

        // 选择与清除
        private void NeoForge_Selected(MyListItem sender, EventArgs e)
        {
            SelectedNeoForge = (ModDownload.DlNeoForgeListEntry)sender.Tag;
            this.CardNeoForge.IsSwapped = true;
            OptiFine_Loaded();
            ReloadSelected();
        }
        private void NeoForge_Clear(object sender, EventArgs e)
        {
            SelectedNeoForge = null;
            this.CardNeoForge.IsSwapped = true;
            OptiFine_Loaded();
            ReloadSelected();
        }

        #endregion

        #region Fabric 列表

        /// <summary>
    /// 获取 Fabric 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
        public string LoadFabricGetError()
        {
            // 检查 OptiFine 1.20.5+：没有 OptiFabric 故全部不兼容
            if (SelectedOptiFine is not null && McUtils.CompareVersionGE(VanillaName, "1.20.5"))
                return "与 OptiFine 不兼容";
            // 检查 Loader
            if (this.GetLoaderError(this.LoadFabric) is not null)
                return this.GetLoaderError(this.LoadFabric);
            // 检查版本
            var gameNode = ModDownload.DlFabricListLoader.Output.Value["game"];
            if (gameNode is null)
                return "无";
            foreach (JObject Version in gameNode)
            {
                if ((Version["version"]?.ToString() ?? "") == (VanillaName.Replace("∞", "infinite").Replace("Combat Test 7c", "1.16_combat-3") ?? ""))
                {
                    if (SelectedForge is not null)
                        return "与 Forge 不兼容";
                    if (SelectedNeoForge is not null)
                        return "与 NeoForge 不兼容";
                    return null;
                }
            }
            return "无";
        }

        // 限制展开
        private void CardFabric_PreviewSwap(object sender, ModBase.RouteEventArgs e)
        {
            if (LoadFabricGetError() is not null)
                e.Handled = true;
        }

        /// <summary>
    /// 尝试重新可视化 Fabric 版本列表。
    /// </summary>
        private void Fabric_Loaded()
        {
            try
            {
                if (ModDownload.DlFabricListLoader.State != ModBase.LoadState.Finished)
                    return;
                // 获取版本列表
                JArray Versions = ModDownload.DlFabricListLoader.Output.Value["loader"] as JArray;
                if (Versions is null || !Versions.Any())
                    return;
                // 可视化
                this.PanFabric.Children.Clear();
                this.CardFabric.SwapControl = this.PanFabric;
                this.CardFabric.SetContentBuilder(Versions.OfType<JObject>(), Version => ModDownloadLib.FabricDownloadListItem(Version, (_, __) => this.Fabric_Selected((dynamic)_, (dynamic)__)));
                if (!this.CardFabric.IsSwapped)
                    this.CardFabric.BuildContent();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化 Fabric 安装版本列表出错");
            }
        }

        // 选择与清除
        public void Fabric_Selected(MyListItem sender, EventArgs e)
        {
            var fabricTag = sender.Tag as JObject;
            SelectedFabric = fabricTag?["version"]?.ToString();
            FabricApi_Loaded();
            OptiFabric_Loaded();
            this.CardFabric.IsSwapped = true;
            ReloadSelected();
        }
        private void Fabric_Clear(object sender, EventArgs e)
        {
            SelectedFabric = null;
            SelectedFabricApi = null;
            this.BtnFabricApiClear.Visibility = Visibility.Collapsed;
            AutoSelectedFabricApi = false;
            SelectedOptiFabric = null;
            this.BtnOptiFabricClear.Visibility = Visibility.Collapsed;
            AutoSelectedOptiFabric = false;
            this.CardFabric.IsSwapped = true;
            ReloadSelected();
        }

        #endregion

        #region Fabric API 列表

        /// <summary>
    /// 判断某 Fabric API 是否适配指定的原版版本。
    /// </summary>
        public static bool IsFabricApiCompatible(ResourceVersion FabricApi, string MinecraftVersion)
        {
            string FabricApiName = FabricApi.Display;
            try
            {
                if (FabricApiName is null || MinecraftVersion is null)
                    return false;
                FabricApiName = FabricApiName.Lower();
                string TargetName = MinecraftVersion.Replace("∞", "infinite").Replace("Combat Test 7c", "1.16_combat-3").Lower();
                if (FabricApiName.StartsWithF("[" + TargetName + "]"))
                    return true;
                if (!FabricApiName.Contains("/") || !FabricApiName.Contains("]"))
                    return false;
                // 直接的判断（例如 1.18.1/22w03a）
                foreach (string Part in FabricApiName.BeforeFirst("]").TrimStart('[').Split("/"))
                {
                    if ((Part ?? "") == (TargetName ?? ""))
                        return true;
                }
                // 将版本名分割语素（例如 1.16.4/5）
                var Lefts = FabricApiName.BeforeFirst("]").RegexSearch("[a-z/]+|[0-9/]+").ToList();
                var Rights = TargetName.BeforeFirst("]").RegexSearch("[a-z/]+|[0-9/]+").ToList();
                // 对每段进行判断
                int i = 0;
                while (true)
                {
                    // 两边均缺失，感觉是一个东西
                    if (Lefts.Count - 1 < i && Rights.Count - 1 < i)
                        return true;
                    // 确定两边是否一致
                    string LeftValue = Lefts.Count - 1 < i ? "-1" : Lefts[i];
                    string RightValue = Rights.Count - 1 < i ? "-1" : Rights[i];
                    if (!LeftValue.Contains("/"))
                    {
                        if ((LeftValue ?? "") != (RightValue ?? ""))
                            return false;
                    }
                    // 左边存在斜杠
                    else if (!LeftValue.Contains(RightValue))
                        return false;
                    i += 1;
                }
                // 上方 while (true) 仅在 return 处退出，转换后追加的 return true 永不执行（CS0162），故删除
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"判断 Fabric API 版本适配性出错（{FabricApiName}, {MinecraftVersion}）");
                return false;
            }
        }

        /// <summary>
    /// 获取 FabricApi 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
        public string LoadFabricApiGetError()
        {
            // 检查 Loader
            if (this.GetLoaderError(this.LoadFabricApi) is not null)
                return this.GetLoaderError(this.LoadFabricApi);
            if (ModDownload.DlFabricApiLoader.Output is null)
                return SelectedFabric is null ? "需要安装 Fabric" : "获取中……";
            // 检查版本
            if (ModDownload.DlFabricApiLoader.Output.Any(f => IsFabricApiCompatible(f, VanillaName)))
            {
                return SelectedFabric is null ? "需要安装 Fabric" : null;
            }
            else
            {
                return "无";
            }
        }

        // 限制展开
        private void CardFabricApi_PreviewSwap(object sender, ModBase.RouteEventArgs e)
        {
            if (LoadFabricApiGetError() is not null)
                e.Handled = true;
        }

        public bool AutoSelectedFabricApi = false;
        /// <summary>
    /// 尝试重新可视化 FabricApi 版本列表。
    /// </summary>
        private void FabricApi_Loaded()
        {
            try
            {
                if (ModDownload.DlFabricApiLoader.State != ModBase.LoadState.Finished)
                    return;
                if (VanillaName is null || SelectedFabric is null)
                    return;
                // 获取版本列表
                var Versions = new List<ResourceVersion>();
                foreach (var Version in ModDownload.DlFabricApiLoader.Output)
                {
                    if (IsFabricApiCompatible(Version, VanillaName))
                    {
                        if (!Version.Display.StartsWithF("["))
                        {
                            Logger.Warn($"已特判修改 Fabric API 显示名：{Version.Display}");
                            Version.Display = "[" + VanillaName + "] " + Version.Display;
                        }
                        Versions.Add(Version);
                    }
                }
                if (!Versions.Any())
                    return;
                Versions = Versions.OrderByDescending(v => v.ReleaseDate).ToList();
                // 可视化
                this.PanFabricApi.Children.Clear();
                foreach (var Version in Versions)
                    this.PanFabricApi.Children.Add(ModDownloadLib.FabricApiDownloadListItem(Version, (_, __) => this.FabricApi_Selected((dynamic)_, (dynamic)__)));
                // 自动选择 Fabric API
                if (!AutoSelectedFabricApi)
                {
                    AutoSelectedFabricApi = true;
                    if (MyVirtualizingElement.TryInit((FrameworkElement)this.PanFabricApi.Children[0]) is not MyListItem Item)
                        return;
                    Logger.Info($"已自动选择 Fabric API：{Item.Title}");
                    FabricApi_Selected(Item, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化 Fabric API 安装版本列表出错");
            }
        }

        // 选择与清除
        private void FabricApi_Selected(MyListItem sender, EventArgs e)
        {
            SelectedFabricApi = (ResourceVersion)sender.Tag;
            this.CardFabricApi.IsSwapped = true;
            ReloadSelected();
        }
        private void FabricApi_Clear(object sender, EventArgs e)
        {
            SelectedFabricApi = null;
            this.CardFabricApi.IsSwapped = true;
            ReloadSelected();
        }

        #endregion

        #region OptiFabric 列表

        /// <summary>
    /// 判断某 OptiFabric 是否适配指定的原版版本。
    /// </summary>
        public static bool IsOptiFabricCompatible(ResourceVersion ModFile, string MinecraftVersion)
        {
            try
            {
                if (MinecraftVersion is null)
                    return false;
                return ModFile.GameVersions.Contains(MinecraftVersion);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"判断 OptiFabric 版本适配性出错（{MinecraftVersion}）");
                return false;
            }
        }

        public bool AutoSelectedOptiFabric = false;
        /// <summary>
    /// 获取 OptiFabric 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
        public string LoadOptiFabricGetError()
        {
            if (VanillaDrop >= 140 && VanillaDrop <= 150)
                return "不兼容老版本 Fabric，请手动下载 OptiFabric Origins";
            // 检查 Loader
            if (this.GetLoaderError(this.LoadOptiFabric) is not null)
                return this.GetLoaderError(this.LoadOptiFabric);
            // 检查版本
            if (ModDownload.DlOptiFabricLoader.Output is null)
            {
                if (SelectedFabric is null && SelectedOptiFine is null)
                    return "需要安装 OptiFine 与 Fabric";
                if (SelectedFabric is null)
                    return "需要安装 Fabric";
                if (SelectedOptiFine is null)
                    return "需要安装 OptiFine";
                return "获取中……";
            }
            // 未配置 API Key 时列表被主动跳过（空列表），应明示原因而非显示「无」或反复报 403。
            if (!ResourceSourceGate.CurseForgeAvailable)
                return "需要配置 CurseForge API Key";
            foreach (var Version in ModDownload.DlOptiFabricLoader.Output)
            {
                if (!IsOptiFabricCompatible(Version, VanillaName))
                    continue; // 2135#
                if (SelectedFabric is null && SelectedOptiFine is null)
                    return "需要安装 OptiFine 与 Fabric";
                if (SelectedFabric is null)
                    return "需要安装 Fabric";
                if (SelectedOptiFine is null)
                    return "需要安装 OptiFine";
                return null; // 通过检查
            }
            return "无";
        }

        // 限制展开
        private void CardOptiFabric_PreviewSwap(object sender, ModBase.RouteEventArgs e)
        {
            if (LoadOptiFabricGetError() is not null)
                e.Handled = true;
        }

        /// <summary>
    /// 尝试重新可视化 OptiFabric 版本列表。
    /// </summary>
        private void OptiFabric_Loaded()
        {
            try
            {
                if (ModDownload.DlOptiFabricLoader.State != ModBase.LoadState.Finished)
                    return;
                if (VanillaName is null || SelectedFabric is null || SelectedOptiFine is null)
                    return;
                // 获取版本列表
                var Versions = new List<ResourceVersion>();
                foreach (var Version in ModDownload.DlOptiFabricLoader.Output)
                {
                    if (IsOptiFabricCompatible(Version, VanillaName))
                        Versions.Add(Version);
                }
                if (!Versions.Any())
                    return;
                // 排序
                Versions = Versions.OrderByDescending(v => v.ReleaseDate).ToList();
                // 可视化
                this.PanOptiFabric.Children.Clear();
                foreach (var Version in Versions)
                    this.PanOptiFabric.Children.Add(ModDownloadLib.OptiFabricDownloadListItem(Version, (_, __) => this.OptiFabric_Selected((dynamic)_, (dynamic)__)));
                // 自动选择 OptiFabric
                if (AutoSelectedOptiFabric || VanillaDrop >= 140 && VanillaDrop <= 150)
                    return; // 1.14~15 不自动选择
                AutoSelectedOptiFabric = true;
                if (MyVirtualizingElement.TryInit((FrameworkElement)this.PanOptiFabric.Children[0]) is not MyListItem Item)
                    return;
                Logger.Info($"已自动选择 OptiFabric：{Item.Title}");
                OptiFabric_Selected(Item, null);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可视化 OptiFabric 安装版本列表出错");
            }
        }

        // 选择与清除
        private void OptiFabric_Selected(MyListItem sender, EventArgs e)
        {
            SelectedOptiFabric = (ResourceVersion)sender.Tag;
            this.CardOptiFabric.IsSwapped = true;
            ReloadSelected();
        }
        private void OptiFabric_Clear(object sender, EventArgs e)
        {
            SelectedOptiFabric = null;
            this.CardOptiFabric.IsSwapped = true;
            ReloadSelected();
        }

        #endregion

        #region 安装

        /// <summary>
    /// 获取当前选择的组件信息。
    /// </summary>
        public McComponents GetSelectedComponents()
        {
            return new McComponents(minecraftVersion: VanillaName, optiFineEntry: SelectedOptiFine is null ? null : JObject.FromObject(SelectedOptiFine), forgeVersion: SelectedForge?.VersionName, neoForgeVersion: SelectedNeoForge?.VersionName, fabricVersion: SelectedFabric, fabricApiVersion: (SelectedFabricApi?.Version) ?? (SelectedFabricApi?.Id) ?? (SelectedFabricApi?.Display), optiFabricVersion: (SelectedOptiFabric?.Version) ?? (SelectedOptiFabric?.Id) ?? (SelectedOptiFabric?.Display), liteLoaderVersion: SelectedLiteLoader?.JsonToken?["version"]?.ToString() ?? "");
        }

        private void TextSelectName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && IsInSelectPage && this.BtnStart.IsEnabled)
                BtnStart_Click();
        }
        private void BtnStart_Click()
        {
            // 提交安装申请
            string InstanceName = this.TextSelectName.Text;
            var Request = new ModDownloadLib.McInstallRequest()
            {
                NewInstanceName = InstanceName,
                InstanceFolder = $@"{ModMinecraft.McFolderSelected}versions\{InstanceName}\",
                Components = GetSelectedComponents(),
                MinecraftJson = VanillaData?["url"]?.ToString() ?? "",
                ForgeEntry = SelectedForge,
                NeoForgeEntry = SelectedNeoForge,
                FabricApi = SelectedFabricApi,
                OptiFabric = SelectedOptiFabric,
                LiteLoaderEntry = SelectedLiteLoader
            };
            StartInstall(Request);
        }

        public virtual void StartInstall(ModDownloadLib.McInstallRequest Request)
        {
            // 确认版本隔离
            if ((SelectedForge is not null || SelectedNeoForge is not null || SelectedFabric is not null) && (Settings.Get<int>("LaunchArgumentIndieV2") == 0 || Settings.Get<int>("LaunchArgumentIndieV2") == 2))
            {
                if (ModMain.MyMsgBox("你尚未开启版本隔离，多个 MC 版本会共用同一个 Mod 文件夹。" + Constants.vbCrLf + "因此，游戏可能会因为读取到与当前版本不符的 Mod 而崩溃。" + Constants.vbCrLf + "推荐先在 设置 → 启动选项 → 默认版本隔离 中开启版本隔离！", "版本隔离提示", "取消下载", "继续") == 1)
                {
                    return;
                }
            }
            if (!ModDownloadLib.McInstallStart(Request))
                return;
            // 返回，这样在再次进入安装页面时这个版本就会显示文件夹已重复
            ExitSelectPage();
        }

        #endregion

        private string GetLoaderError(MyLoading Loader)
        {
            if (Loader is null)
                return "获取中……";
            if (!Loader.State.IsLoader)
                return "获取中……";
            switch (Loader.State.LoadingState)
            {
                case MyLoading.MyLoadingState.Run:
                    {
                        return "获取中……";
                    }
                case MyLoading.MyLoadingState.Error:
                    {
                        string Message = ((ModLoader.LoaderBase)Loader.State).Error.Message;
                        return Message == "无" ? "无" : "获取失败：" + Message;
                    }
                case MyLoading.MyLoadingState.Unloaded:
                    {
                        return "未知错误，状态为 Unloaded";
                    }

                default:
                    {
                        return null;
                    }
            }
        }

    }
}