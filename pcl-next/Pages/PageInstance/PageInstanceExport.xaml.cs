using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public class ExportOption
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Rules { get; set; }
        /// <summary>
    /// 如果 Rules 为空，则根据 ShowRules 的内容判断是否应该显示这个复选框。
    /// 如果 ShowRules 也为空，则始终显示。
    /// </summary>
        public string ShowRules { get; set; }
        public bool DefaultChecked { get; set; }
        public bool RequireModLoader { get; set; } = false;
        public bool RequireOptiFine { get; set; } = false;
        public bool RequireModLoaderOrOptiFine { get; set; } = false;
    }

    public partial class PageInstanceExport : IRefreshable
    {

        private string CurrentInstance = "";

        public PageInstanceExport()
        {
            InitializeComponent();
            this.CardOptions.MouseLeftButtonDown += (p1, p2) => CardOptions_MouseLeftButtonDown();
            this.BtnAdvancedExport.Click += (p1, p2) => ExportConfig();
            this.BtnAdvancedImport.Click += (p1, p2) => ImportConfig();
            this.BtnExport.Click += (p1, p2) => StartExport();
            this.TextExportName.GotFocus += (p1, p2) => TextExportName_GotFocus();
            this.CheckAdvancedInclude.Change += (p1, p2) => CheckAdvancedInclude_Change((object)p1, (bool)p2);
            this.CheckAdvancedModrinth.Change += (p1, p2) => CheckAdvancedModrinth_Change((object)p1, (bool)p2);
            this.CheckOptionsPcl.Change += (p1, p2) => CheckAdvancedModrinth_Change((object)p1, (bool)p2);
            this.Loaded += (_, __) => PageInstanceExport_Loaded();
            this.Loaded += (_, __) => RefreshJavaInfo();
            // Initialized 在 InitializeComponent() 内部就已触发，事后订阅收不到（2026-11-11 修复）。
            PageInstanceExport_Initialized(this, EventArgs.Empty);
        }
        private void PageInstanceExport_Initialized(object sender, EventArgs e)
        {
            PageInstanceLeft.CurrentJavaWorker.Stopped += () => ModBase.RunInUi(RefreshJavaInfo);
        }
        private void PageInstanceExport_Loaded()
        {
            ModAnimation.AniControlEnabled += 1;
            if (!PathUtils.IsSame(CurrentInstance, PageInstanceLeft.Instance.PathVersion))
                RefreshAll(); // 切换到了另一个版本，重置页面
            CustomEventService.SetEventData(this.BtnAdvancedHelp, ModBuildChannel.ChannelIsOfficial ? "指南/整合包制作 - Public.json" : "指南/整合包制作 - Snapshot.json");
            ModAnimation.AniControlEnabled -= 1;
        }
        public void RefreshAll()
        {
            Logger.Info("刷新导出页面");
            this.HintOptiFine.Visibility = PageInstanceLeft.Instance.Version.HasOptiFine && !PageInstanceLeft.Instance.Version.OptiFineAsMod ? Visibility.Visible : Visibility.Collapsed;
            CurrentInstance = PageInstanceLeft.Instance.PathVersion;
            this.TextExportName.Text = "";
            this.TextExportName.HintText = PageInstanceLeft.Instance.Name;
            this.TextExportVersion.Text = "";
            this.TextExportVersion.HintText = "1.0.0";
            this.CheckAdvancedInclude.Checked = false;
            this.CheckAdvancedModrinth.Checked = false;
            this.GetExportOption(this.CheckOptionsBasic).Description = PageInstanceLeft.Instance.VersionDisplayName();
            ResetConfigOverrides();
            ReloadAllSubOptions();
            RefreshAllOptionsUI();
            RefreshJavaInfo();
            this.PanBack.ScrollToHome();
        }

        void IRefreshable.Refresh() => RefreshAll();

        #region 子选项
        private string[] SubOptionBlackList = new[] { "Quark Programmer Art.zip", "+ EuphoriaPatches_", "PCL2 Skin.zip" };

        /// <summary>
    /// 动态生成子文件夹下的选项，例如资源包、存档等。
    /// </summary>
        private void ReloadAllSubOptions()
        {
            this.ReloadSubOptions(this.PanOptionsResourcePacks, true, true, "resourcepacks", "texturepacks");
            this.ReloadSubOptions(this.PanOptionsSaves, false, true, "saves");
            this.ReloadSubOptions(this.PanOptionsShaderPacks, true, true, "shaderpacks");
        }

        private void ReloadSubOptions(StackPanel Panel, bool AcceptCompressedFile, bool AcceptFolder, params string[] Folders)
        {
            Panel.Children.Clear();
            foreach (var Folder in Folders)
            {
                var TargetFolder = DirectoryUtils.GetInfo(PageInstanceLeft.Instance.PathIndie + Folder);
                if (!TargetFolder.Exists)
                    continue;
                // 查找文件夹下的对应项
                if (AcceptCompressedFile)
                {
                    foreach (var File in TargetFolder.EnumerateFiles("*.zip").Concat(TargetFolder.EnumerateFiles("*.rar")))
                    {
                        if (SubOptionBlackList.Any(b => File.Name.Contains(b)))
                            continue;
                        Panel.Children.Add(new MyCheckBox() { Tag = new ExportOption() { Title = File.Name, DefaultChecked = true, Rules = StringUtils.LikePatternEscape($"{Folder}/{File.Name}") } });
                    }
                }
                if (AcceptFolder)
                {
                    foreach (var SubFolder in TargetFolder.EnumerateDirectories().OrderByDescending(f => f.LastWriteTime))
                    {
                        if (SubOptionBlackList.Any(b => SubFolder.Name.Contains(b)))
                            continue;
                        if (!SubFolder.EnumerateFileSystemInfos().Any())
                            continue;
                        var NewCheckBox = new MyCheckBox() { Tag = new ExportOption() { Title = SubFolder.Name, DefaultChecked = true, Rules = StringUtils.LikePatternEscape($"{Folder}/{SubFolder.Name}/") } };
                        if (AcceptCompressedFile)
                            GetExportOption(NewCheckBox).Description = "文件夹";
                        if (object.ReferenceEquals(Panel, this.PanOptionsSaves))
                            GetExportOption(NewCheckBox).Description = SubFolder.LastWriteTime.ToString("yyyy'/'MM'/'dd HH':'mm");
                        Panel.Children.Add(NewCheckBox);
                    }
                }
            }
        }

        #endregion

        #region 选项

        /// <summary>
    /// 重新确认是否应该显示每个选项，并将 ExportOption 同步到 UI。
    /// </summary>
        private void RefreshAllOptionsUI()
        {
            // 预先归纳所有至多二级的文件/文件夹
            var AllEntries = new List<string>();
            bool IsValidDirectory(DirectoryInfo Folder) { try { return Folder.Exists && Folder.EnumerateFileSystemInfos().Any(i => !SubOptionBlackList.Any(b => i.Name.Contains(b))); } catch { return false; } }; // 检查文件夹不为空
                                                                                                                                                                                                                   // 一般是由于无法访问，或是一个指向已不存在的文件夹的链接（例如使用 mklink 创造的 resource 文件夹链接）
            var PathInfo = DirectoryUtils.GetInfo(PageInstanceLeft.Instance.PathIndie);
            AllEntries.AddRange(PathInfo.EnumerateFiles().Select(f => f.Name));
            foreach (var SubFolder in PathInfo.EnumerateDirectories().Where(IsValidDirectory))
            {
                AllEntries.Add($@"{SubFolder.Name}\");
                AllEntries.AddRange(SubFolder.EnumerateFiles().Select(f => $@"{SubFolder.Name}\{f.Name}"));
                AllEntries.AddRange(SubFolder.EnumerateDirectories().Where(IsValidDirectory).Select(d => $@"{SubFolder.Name}\{d.Name}\"));
            }
            Logger.Info($"共发现 {AllEntries.Count} 个可行的二级文件/文件夹");
            // 确认选项是否应该被显示
            bool IsVisible(ExportOption TargetOption)
            {
                // 检查需要 OptiFine（例如光影）或 Mod 加载器（例如 mods/）
                if (TargetOption.RequireOptiFine && !PageInstanceLeft.Instance.Version.HasOptiFine)
                    return false;
                if (TargetOption.RequireModLoader && !PageInstanceLeft.Instance.Modable)
                    return false;
                if (TargetOption.RequireModLoaderOrOptiFine && !PageInstanceLeft.Instance.Version.HasOptiFine && !PageInstanceLeft.Instance.Modable)
                    return false;
                // 粗略检查是否可能有符合规则的文件/文件夹
                return StandardizeLines((TargetOption.Rules ?? TargetOption.ShowRules).Split('|'), true).Any((Rule) =>
        {
            if (Rule.StartsWithF("!"))
                return false; // 只看正向规则
                              // 检查前两级
            try
            {
                if (AllEntries.Any(Entry => LikeOperator.LikeString(Entry.Lower(), Rule.Lower(), CompareMethod.Binary)))
                    return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"错误的规则：{Rule}", LogBehavior.Toast);
                return false;
            }
            // 粗略检查所有级
            Rule = Rule.Trim("*?".ToCharArray());
            if (Rule.Split('\\', true).Count() >= 3)
            {
                if (Rule.EndsWithF(@"\"))
                {
                    return IsValidDirectory(DirectoryUtils.GetInfo(PageInstanceLeft.Instance.PathIndie + Rule)); // 文件夹有效
                }
                else
                {
                    return FileUtils.Exists(PageInstanceLeft.Instance.PathIndie + Rule);
                } // 文件有效
            }
            else
            {
                return false;
            }
        });
            };
            // 逐个检查选项
            foreach (var CheckBox in GetAllOptions(true))
            {
                var TargetOption = GetExportOption(CheckBox);
                // 可见性、默认勾选
                if (string.IsNullOrEmpty(TargetOption.Rules) && string.IsNullOrEmpty(TargetOption.ShowRules))
                {
                    CheckBox.Visibility = Visibility.Visible;
                    CheckBox.Checked = TargetOption.DefaultChecked;
                }
                else
                {
                    bool Pass = IsVisible(TargetOption);
                    CheckBox.Visibility = Pass ? Visibility.Visible : Visibility.Collapsed;
                    CheckBox.Checked = TargetOption.DefaultChecked && Pass;
                }
                // 名称与简介
                CheckBox.Inlines.Clear();
                if (CheckBox.Visibility != Visibility.Visible)
                    continue;
                CheckBox.Inlines.Add(new Run(TargetOption.Title));
                if (!string.IsNullOrEmpty(TargetOption.Description))
                {
                    CheckBox.Inlines.Add(new Run("   " + TargetOption.Description) { Foreground = ModSecret.ColorGray5 });
                }
            }
        }

        /// <summary>
    /// 对文本行进行标准化处理，以便使用 Like 进行匹配。
    /// </summary>
        private IEnumerable<string> StandardizeLines(IEnumerable<string> Raw, bool AddSuffixStarToFolderPath)
        {
            foreach (var IgnoreLine_Iter in Raw)
            {
                var IgnoreLine = IgnoreLine_Iter;
                IgnoreLine = IgnoreLine.Trim();
                if (string.IsNullOrEmpty(IgnoreLine) || IgnoreLine.StartsWithF("#") || IgnoreLine.StartsWithF("="))
                    continue;
                IgnoreLine = IgnoreLine.Replace("/", @"\");
                yield return IgnoreLine + (IgnoreLine.EndsWithF(@"\") && AddSuffixStarToFolderPath ? "*" : "");
            }
        }

        /// <summary>
    /// 获取所有可作为选项的 CheckBox。
    /// </summary>
        private IEnumerable<MyCheckBox> GetAllOptions(bool IncludeHidden)
        {
            foreach (var Element in this.PanOptions.Children)
            {
                if (!IncludeHidden && Conversions.ToBoolean(Operators.ConditionalCompareObjectNotEqual(((dynamic)Element).Visibility, Visibility.Visible, false)))
                    continue;
                if (Element is MyCheckBox)
                {
                    yield return (MyCheckBox)Element;
                }
                else if (Element is StackPanel)
                {
                    foreach (var SubElement in ((StackPanel)Element).Children)
                    {
                        if (!IncludeHidden && Conversions.ToBoolean(Operators.ConditionalCompareObjectNotEqual(((dynamic)SubElement).Visibility, Visibility.Visible, false)))
                            continue;
                        if (SubElement is MyCheckBox)
                            yield return (MyCheckBox)SubElement;
                    }
                }
            }
        }

        /// <summary>
    /// 获取该 CheckBox 对应的 ExportOption。
    /// </summary>
        private ExportOption GetExportOption(MyCheckBox CheckBox)
        {
            return (ExportOption)CheckBox.Tag;
        }

        #endregion

        #region 配置文件
        private const string Sperator = "==============================================================";

        // ================ 导出内容段 ================

        /// <summary>
    /// 从配置文件中读取的规则。
    /// 如果不为 Nothing，则会覆写当前勾选的规则并禁用对应 UI。
    /// </summary>
        private List<string> RulesOverrides
        {
            get
            {
                return _RulesOverrides;
            }
            set
            {
                _RulesOverrides = value;
                if (value is null)
                {
                    this.BtnOverrideCancel.Visibility = Visibility.Collapsed;
                    this.PanOptions.Visibility = Visibility.Visible;
                    this.CardOptions.Inlines.Clear();
                    this.CardOptions.Inlines.Add(new Run("导出内容列表") { FontWeight = FontWeights.Bold });
                }
                else
                {
                    this.BtnOverrideCancel.Visibility = Visibility.Visible;
                    this.PanOptions.Visibility = Visibility.Collapsed;
                    this.CardOptions.Inlines.Clear();
                    this.CardOptions.Inlines.Add(new Run("导出内容列表:    ") { FontWeight = FontWeights.Bold });
                    this.CardOptions.Inlines.Add(new Run("从配置文件中读取") { FontWeight = FontWeights.Normal });
                }
            }
        }
        private List<string> _RulesOverrides = null;
        /// <summary>
    /// 获取当前实际生效的所有规则。
    /// </summary>
        private IEnumerable<string> GetAllRules()
        {
            if (RulesOverrides is not null)
            {
                // 返回覆盖的列表
                foreach (var Rule in RulesOverrides)
                    yield return Rule;
            }
            else
            {
                // 从当前勾选的所有选项中获取所有规则行
                yield return "";
                yield return "# 修改下方的规则以控制需要导出的内容。";
                yield return "# 以 ! 开头以反选。可以使用 *、?、[] 通配符。靠后的行覆盖靠前的。";
                yield return "";
                foreach (var CheckBox in GetAllOptions(false))
                {
                    if (!CheckBox.Checked)
                        continue;
                    var TargetOption = GetExportOption(CheckBox);
                    var Rules = (TargetOption.Rules?.Split('|').ToList()) ?? new List<string>();

                    // 动态 Rules
                    if (object.ReferenceEquals(CheckBox, this.CheckOptionsShaderPackSettings)) // 光影包设置：只导出勾选的光影包的设置
                    {
                        foreach (var ShaderPackCheckBox in PanOptionsShaderPacks.Children.OfType<MyCheckBox>())
                        {
                            if (!ShaderPackCheckBox.Checked)
                                continue;
                            var ShaderPackOption = this.GetExportOption(ShaderPackCheckBox);
                            string SettingsFile = $"shaderpacks/{ShaderPackOption.Title}.txt";
                            if (!FileUtils.Exists(Path.Combine(PageInstanceLeft.Instance.PathIndie, SettingsFile)))
                                continue;
                            Rules.Add(StringUtils.LikePatternEscape(SettingsFile));
                        }
                    }

                    if (!Rules.Any())
                        continue;
                    yield return $"# {TargetOption.Title}";
                    foreach (var Rule in Rules)
                        yield return Rule;
                    yield return "";
                }
                yield return "# 排除的文件";
                yield return "!*.log";
                yield return "!*.dat_old";
                yield return "!*.BakaCoreInfo";
                yield return "!hmclversion.cfg";
                yield return "!log4j2.xml";
                yield return "";
            }
        }

        // ================ 追加内容段 ================

        private List<string> ExtraFiles = null;
        /// <summary>
    /// 获取当前实际生效的追加内容。
    /// </summary>
        private IEnumerable<string> GetExtraFileLines()
        {
            if (ExtraFiles is not null)
            {
                // 返回覆盖的列表
                foreach (var File in ExtraFiles)
                    yield return File;
            }
            else
            {
                // 从当前勾选的所有选项中获取所有规则行
                yield return "";
                yield return "# 如果想将额外的文件自动放到压缩包根目录中，可以将它们的路径写在下方。";
                yield return @"# 必须是完整路径。每行中，若以 \ 结尾则代表是文件夹，不以 \ 结尾则代表是文件。";
                yield return "";
            }
        }

        // ================ 重置 ================

        /// <summary>
    /// 重置配置文件所带来的影响。
    /// </summary>
        private void ResetConfigOverrides()
        {
            RulesOverrides = null;
            ConfigPackPath = null;
            ExtraFiles = null;
            this.PanBack.ScrollToHome();
        }
        private void CardOptions_MouseLeftButtonDown()
        {
            if (RulesOverrides is null)
                return;
            ResetConfigOverrides();
        }

        // ================ 保存 / 读取 ================

        // 保存配置文件
        private void ExportConfig()
        {
            try
            {
                string ConfigPath = Dialogs.SaveFile("选择文件位置", "export_config.txt", Settings.Get<string>("CacheExportConfig"), new[] { ("txt", "整合包导出配置") });
                if (ConfigPath is null)
                    return;
                Settings.Set("CacheExportConfig", ConfigPath);
                var ConfigLines = new List<string>();
                // ini 段
                ConfigLines.Add("Name:" + this.TextExportName.Text);
                ConfigLines.Add("Version:" + this.TextExportVersion.Text);
                ConfigLines.Add("");
                ConfigLines.Add($"# 当版本设置中选择了 {ModBase.vbLQ}使用版本文件夹中的 Java{ModBase.vbRQ} 时，是否打包版本文件夹中的 Java。");
                ConfigLines.Add("IncludeJava:" + this.CheckOptionsJava.Checked);
                ConfigLines.Add("");
                ConfigLines.Add("# 是否打包正式版 PCL Next，以便没有启动器的玩家安装整合包。");
                ConfigLines.Add("IncludeLauncher:" + this.CheckOptionsPcl.Checked);
                ConfigLines.Add("");
                ConfigLines.Add("# 是否打包 PCL Next 个性化内容，例如功能隐藏设置、主页、背景音乐和图片等。");
                ConfigLines.Add("IncludeLauncherCustom:" + this.CheckOptionsPclCustom.Checked);
                ConfigLines.Add("");
                ConfigLines.Add("# 是否将 Mod、资源包、光影包的文件直接放入整合包中，这样在导入时就无需联网下载它们。");
                ConfigLines.Add("# 建议仅在无法稳定连接 CurseForge 或 Modrinth 时才考虑启用。");
                ConfigLines.Add("# 二次分发可能违反使用协议，请尽量不要公开发布包含资源文件的整合包！");
                ConfigLines.Add("DontCheckHostedAssets:" + this.CheckAdvancedInclude.Checked);
                ConfigLines.Add("");
                ConfigLines.Add("# 若资源未在 Modrinth 上托管，则将其原始文件直接放入整合包中。");
                ConfigLines.Add("# 如果你想将整合包上传到 Modrinth，这能帮助你分辨哪些文件未在 Modrinth 上托管。");
                ConfigLines.Add("# 此选项与 DontCheckHostedAssets 冲突。");
                ConfigLines.Add("ModrinthUploadMode:" + this.CheckAdvancedModrinth.Checked);
                ConfigLines.Add("");
                ConfigLines.Add("# 导出的文件的存放位置。");
                ConfigLines.Add("# 若设置了此项，在导出时会直接将文件放到此路径，不会弹窗要求选择。");
                ConfigLines.Add("# 若 IncludeLauncher 为 True，应以 .zip 结尾；若为 False，应以 .mrpack 结尾。");
                ConfigLines.Add("PackPath:" + (ConfigPackPath ?? ""));
                ConfigLines.Add("");
                // 导出内容段
                ConfigLines.Add(Sperator);
                ConfigLines.AddRange(GetAllRules());
                // 追加内容段
                ConfigLines.Add(Sperator);
                ConfigLines.AddRange(GetExtraFileLines());
                // 结束
                FileUtils.Write(ConfigPath, ConfigLines.Join(Constants.vbCrLf));
                ModMain.Hint("已保存配置文件：" + ConfigPath, ModMain.HintType.Green);
                ModBase.OpenExplorer(ConfigPath);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "保存配置失败", LogBehavior.Alert);
            }
        }
        // 读取配置文件
        private void ImportConfig()
        {
            try
            {
                string ConfigPath = Dialogs.SelectFile("选择配置文件", false, Settings.Get<string>("CacheExportConfig"), new[] { (new[] { "txt" }, "整合包导出配置") }).FirstOrDefault();
                if (string.IsNullOrEmpty(ConfigPath))
                    return;
                Settings.Set("CacheExportConfig", ConfigPath);
                string[] Segments = FileUtils.ReadAsString(ConfigPath).Split(Sperator);
                // ini 段
                var Ini = new Dictionary<string, string>();
                foreach (var Line_Iter in Segments[0].SplitLines(true))
                {
                    var Line = Line_Iter;
                    Line = Line.Trim();
                    if (string.IsNullOrEmpty(Line) || Line.StartsWithF("#") || Line.StartsWithF("="))
                        continue;
                    int Index = Line.IndexOfF(":");
                    if (Index > 0)
                        Ini[Line.Substring(0, Index)] = Line.Substring(Index + 1);
                }
                this.TextExportName.Text = Ini.GetOrDefault("Name", "");
                this.TextExportVersion.Text = Ini.GetOrDefault("Version", "");
                this.CheckOptionsJava.Checked = Conversions.ToBoolean(Ini.GetOrDefault("IncludeJava", Conversions.ToString(true)));
                this.CheckOptionsPcl.Checked = Conversions.ToBoolean(Ini.GetOrDefault("IncludeLauncher", Conversions.ToString(true)));
                this.CheckOptionsPclCustom.Checked = Conversions.ToBoolean(Ini.GetOrDefault("IncludeLauncherCustom", Conversions.ToString(true)));
                this.CheckAdvancedInclude.Checked = Conversions.ToBoolean(Ini.GetOrDefault("DontCheckHostedAssets", Conversions.ToString(false)));
                if (!this.CheckAdvancedInclude.Checked)
                    this.CheckAdvancedModrinth.Checked = Conversions.ToBoolean(Ini.GetOrDefault("ModrinthUploadMode", Conversions.ToString(false))); // #7979，加个特判
                ConfigPackPath = Ini.GetOrDefault("PackPath", null);
                // 导出内容段
                RulesOverrides = Segments[1].SplitLines(true).ToList();
                // 追加内容段
                if (Segments.Length > 2)
                {
                    ExtraFiles = Segments[2].SplitLines(true).ToList();
                }
                else
                {
                    ExtraFiles = null;
                }
                // 结束
                ModMain.Hint("已读取配置文件：" + ConfigPath, ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "读取配置失败", LogBehavior.Alert);
            }
        }

        #endregion

        #region 导出

        /// <summary>
    /// 配置文件中指定的导出位置。
    /// </summary>
        private string ConfigPackPath = null;

        /// <summary>
    /// 开始导出。
    /// </summary>
        private void StartExport()
        {
            string PackName = string.IsNullOrEmpty(this.TextExportName.Text) ? this.TextExportName.HintText : this.TextExportName.Text;
            string PackVersion = string.IsNullOrEmpty(this.TextExportVersion.Text) ? "1.0.0" : this.TextExportVersion.Text;

            // 重复任务检查
            string LoaderName = $"导出整合包：" + PackName;
            foreach (var OngoingLoader in ModLoader.LoaderTaskbar)
            {
                if ((OngoingLoader.Name ?? "") != (LoaderName ?? ""))
                    continue;
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.TaskManager);
                return;
            }

            // 确认导出位置
            string PackPath = null;
            if (!string.IsNullOrWhiteSpace(ConfigPackPath) && !ConfigPackPath.EndsWithF(@"\") && !ConfigPackPath.EndsWithF("/"))
            {
                try
                {
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(ConfigPackPath));
                    PackPath = ConfigPackPath;
                    Logger.Info($"使用配置文件中指定的导出路径：{ConfigPackPath}");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"无法使用配置文件中指定的导出路径（{ConfigPackPath}）");
                    if (ModMain.MyMsgBox($"指定的路径：{ConfigPackPath}{Constants.vbCrLf}{Constants.vbCrLf}{ex.GetDisplay(true)}", "无法使用配置文件中指定的导出路径", "确定", "取消") == 2)
                        return;
                }
            }
            if (PackPath is null)
            {
                var Extensions = new List<(string Extension, string Display)>();
                if (!this.CheckOptionsPcl.Checked && this.CheckAdvancedModrinth.Checked)
                    Extensions.Add(("mrpack", "Modrinth 整合包文件"));
                Extensions.Add(("zip", "压缩文件"));
                if (!this.CheckOptionsPcl.Checked && !this.CheckAdvancedModrinth.Checked)
                    Extensions.Add(("mrpack", "Modrinth 整合包文件"));
                PackPath = Dialogs.SaveFile("选择导出位置", PackName + (string.IsNullOrEmpty(this.TextExportVersion.Text) ? "" : " " + this.TextExportVersion.Text), filter: Extensions);
                Logger.Info($"手动指定的导出路径：{PackPath}");
            }
            if (PackPath is null)
                return;

            // 缓存所需参数
            string CacheFolder = ModMain.RequestTaskTempFolder();
            string OverridesFolder = CacheFolder + @"modpack\overrides\";
            var TargetInstance = PageInstanceLeft.Instance;
            bool IncludeJava = RulesOverrides is null && this.CheckOptionsJava.Visibility == Visibility.Visible && this.CheckOptionsJava.Checked || RulesOverrides is not null && this.CheckOptionsJava.Checked && Settings.Get<int>("VersionArgumentJavaV2", TargetInstance) == 2;
            string PathIndie = TargetInstance.PathIndie;
            bool CheckHostedAssets = !this.CheckAdvancedInclude.Checked;
            bool ModrinthUploadMode = this.CheckAdvancedModrinth.Checked;
            bool IncludePCL = this.CheckOptionsPcl.Checked;
            bool IncludePCLCustom = IncludePCL && this.CheckOptionsPclCustom.Checked;
            var AllRules = StandardizeLines(GetAllRules(), true).ToList();
            var AllExtraFiles = StandardizeLines(GetExtraFileLines(), false).ToList();
            Logger.Info($"准备导出整合包，共有 {AllRules.Count} 条规则，{AllExtraFiles.Count} 条追加内容行");

            // 构造步骤加载器
            var Loaders = new List<ModLoader.LoaderBase>();

            #region 复制文件

            Loaders.Add(new ModLoader.LoaderTask<int, List<LocalResourceFile>>("复制导出内容", (Loader) =>
        {
            Loader.Output = new List<LocalResourceFile>();
            // 复制版本文件
            int Progress = 0;
            // VB 不要求局部委托变量先赋值即可在自身 lambda 内递归调用，C# 需显式初始化，
            // 保持原文的「先声明、随后立即赋值」顺序，赋初值仅为满足 C# 明确赋值规则
            Action<DirectoryInfo> SearchFolder = null;
            SearchFolder = new Action<DirectoryInfo>((Folder) =>
        {
            // 文件夹：进一步搜索
                foreach (var SubFolder in Folder.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                // 跳过部分又没用文件又多的文件夹，加快搜索
                    if (new[] { "assets", "versions", "libraries" }.Contains(SubFolder.Name) && PathUtils.IsSame(PathIndie, Folder.FullName))
                        continue;
                    if (new[] { "structureCacheV1", ".fabric", ".git", "avatar-cache", "cosmetic-cache" }.Contains(SubFolder.Name))
                        continue;
                    SearchFolder(SubFolder);
                }
            // 文件：检查规则并复制
                foreach (var Entry in Folder.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    string RelativePath = Entry.FullName.AfterFirst(PathIndie);
                // 检查规则
                    bool ShouldKeep = false;
                    foreach (var Rule in AllRules)
                    {
                        bool Revert = Rule.StartsWithF("!");
                        // VB 的 TrimStart("!") 因 Option Strict Off 被当成单个字符，这里改为 char 重载，语义一致
                        if (LikeOperator.LikeString(RelativePath.Lower(), Rule.TrimStart('!').Lower(), CompareMethod.Binary))
                            ShouldKeep = !Revert;
                    }
                    if (!ShouldKeep)
                        continue;
                    string TargetPath = OverridesFolder + RelativePath;
                    FileUtils.Copy(Entry.FullName, TargetPath);
                // 若为压缩包，考虑联网获取路径
                    if (CheckHostedAssets && new[] { ".zip", ".rar", ".jar", ".disabled", ".old" }.Contains(Entry.Extension.Lower()) && new[] { "mods", "packs", "openloader", "resource" }.Any(s => RelativePath.Contains(s)))
                    {
                        var ModFile = new LocalResourceFile(TargetPath);
                        string Unused = ModFile.ModrinthHash; // 提前计算 Hash
                        Unused = ModFile.CurseForgeHash.ToString();
                        Loader.Output.Add(ModFile);
                    }
                // 更新进度（进度并不准确，主要突出一个我还没似）
                    Progress += 1;
                    if (Progress == 25)
                    {
                        Loader.Progress += (0.84d - Loader.Progress) * 0.012d;
                        Progress = 0;
                    }
                }
            });
            SearchFolder(DirectoryUtils.GetInfo(PathIndie));
            Logger.Info($"复制 overrides 文件完成，有 {Loader.Output.Count} 个文件需要联网检查");
            Loader.Progress = 0.85d;
            // 复制 Java
            if (IncludeJava)
            {
                var JavaToExport = ModJava.SelectOrDownloadJava(TargetInstance, false, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider(0.85d, 0.93d));
                if (JavaToExport is null)
                    throw new FileNotFoundException("版本文件夹中没有找到可导出的 Java。");
                string JavaFolder = PathUtils.RemoveLastPart(JavaToExport.Folder);
                string DestFolder = Path.Combine(OverridesFolder, JavaFolder.AfterFirst(TargetInstance.PathVersion, true));
                DirectoryUtils.Copy(JavaFolder, DestFolder);
                Logger.Info($"已复制版本文件夹中的 Java：{JavaFolder} → {DestFolder}");
            }
            Loader.Progress = 0.95d;
            // 复制追加内容到根目录
            string BaseFolder = IncludePCL ? CacheFolder : CacheFolder + @"modpack\";
            foreach (var Line in AllExtraFiles)
            {
                if (Line.EndsWithF(@"\") || Line.EndsWithF("/"))
                {
                    if (DirectoryUtils.Exists(Line))
                    {
                        DirectoryUtils.Copy(Line, BaseFolder + PathUtils.GetLastPart(Line) + @"\");
                    }
                    else
                    {
                        ModMain.Hint($"未找到配置文件中指定的文件夹：{Line}", ModMain.HintType.Red);
                    }
                }
                else if (FileUtils.Exists(Line))
                {
                    FileUtils.Copy(Line, BaseFolder + PathUtils.GetLastPart(Line));
                }
                else
                {
                    ModMain.Hint($"未找到配置文件中指定的单个文件：{Line}", ModMain.HintType.Red);
                }
            }
            Loader.Progress = 0.97d;
            // 复制 PCL 版本设置
            if (DirectoryUtils.Exists(TargetInstance.PathVersion + @"PCL\"))
                DirectoryUtils.Copy(TargetInstance.PathVersion + @"PCL\", OverridesFolder + @"PCL\");
            ModBase.WriteIni(OverridesFolder + @"PCL\Setup.ini", "IsStar", Conversions.ToString(false));
            // 复制 PCL 本体（正式版）
            if (ModBuildChannel.ChannelIsOfficial && IncludePCL)
                FileUtils.Copy(ModBase.PathExe, CacheFolder + "Plain Craft Launcher.exe");
            // 复制 PCL 个性化内容
            if (IncludePCLCustom)
            {
                if (DirectoryUtils.Exists(Paths.Base + @"PCL\Pictures\"))
                    DirectoryUtils.Copy(Paths.Base + @"PCL\Pictures\", CacheFolder + @"PCL\Pictures\");
                if (DirectoryUtils.Exists(Paths.Base + @"PCL\Musics\"))
                    DirectoryUtils.Copy(Paths.Base + @"PCL\Musics\", CacheFolder + @"PCL\Musics\");
                if (DirectoryUtils.Exists(Paths.Base + @"PCL\Help\"))
                    DirectoryUtils.Copy(Paths.Base + @"PCL\Help\", CacheFolder + @"PCL\Help\");
                if (FileUtils.Exists(Paths.Base + @"PCL\Custom.xaml"))
                    FileUtils.Copy(Paths.Base + @"PCL\Custom.xaml", CacheFolder + @"PCL\Custom.xaml");
                if (FileUtils.Exists(Paths.Base + @"PCL\Setup.ini"))
                    FileUtils.Copy(Paths.Base + @"PCL\Setup.ini", CacheFolder + @"PCL\Setup.ini");
                if (FileUtils.Exists(Paths.Base + @"PCL\hints.txt"))
                    FileUtils.Copy(Paths.Base + @"PCL\hints.txt", CacheFolder + @"PCL\hints.txt");
                if (FileUtils.Exists(Paths.Base + @"PCL\Logo.png"))
                    FileUtils.Copy(Paths.Base + @"PCL\Logo.png", CacheFolder + @"PCL\Logo.png");
            }
        })
            {
                ProgressWeight = 5d
            });

            #endregion

            #region 联网检查

            Loaders.Add(new ModLoader.LoaderTask<List<LocalResourceFile>, Dictionary<LocalResourceFile, List<string>>>("联网获取文件信息", (Loader) =>
        {
            Loader.Output = new Dictionary<LocalResourceFile, List<string>>();
            if (!CheckHostedAssets)
            {
                Logger.Info($"要求跳过联网获取步骤");
                return;
            }
            if (!Loader.Input.Any())
            {
                Logger.Info($"没有需要联网检查的文件，跳过联网获取步骤");
                return;
            }

            // 分平台获取下载地址
            int EndedThreadCount = 0;
            var FailedExceptions = new List<Exception>();

            // 从 Modrinth 获取信息
            // 查找对应的文件
            // 写入下载地址
            ModBase.RunInNewThread(() => { try { var ModrinthHashes = Loader.Input.Select(m => m.ModrinthHash); JObject ModrinthRaw = (JObject)ModDownload.DlModRequest("https://api.modrinth.com/v2/version_files", HttpMethod.Post, $"{{\"hashes\": [\"{ModrinthHashes.Join("\",\"")}\"], \"algorithm\": \"sha1\"}}", "application/json"); foreach (var ModFile in Loader.Input) { if (!ModrinthRaw.ContainsKey(ModFile.ModrinthHash)) continue; if ((string)(ModrinthRaw[ModFile.ModrinthHash]?["files"]?[0]["hashes"]?["sha1"]) != ModFile.ModrinthHash) continue; Loader.Output.AddIntoValueCollection(ModFile, (string)ModrinthRaw[ModFile.ModrinthHash]["files"][0]["url"]); } Logger.Info($"从 Modrinth 获取到 {ModrinthRaw.Count} 个本地资源项的对应信息"); } catch (Exception ex) { Logger.Warn(ex, "从 Modrinth 获取本地 Mod 信息失败"); FailedExceptions.Add(ex); } finally { EndedThreadCount += 1; Loader.Progress += 0.45d; } }, "Modrinth - " + LoaderName);

            // 从 CurseForge 获取信息
            // 查找对应的文件
            // 写入下载地址
            ModBase.RunInNewThread(() => { try { if (ModrinthUploadMode) return; var CurseForgeHashes = Loader.Input.Select(m => m.CurseForgeHash); JContainer CurseForgeRaw = (JContainer)ModDownload.DlModRequest("https://api.curseforge.com/v1/fingerprints/432", HttpMethod.Post, $"{{\"fingerprints\": [{CurseForgeHashes.Join(",")}]}}", "application/json")["data"]["exactMatches"]; foreach (JObject ResultJson in CurseForgeRaw) { if (!ResultJson.ContainsKey("file")) continue; JObject File = (JObject)ResultJson["file"]; if (string.IsNullOrEmpty((string)File["downloadUrl"])) continue; var ModFile = Loader.Input.FirstOrDefault(m => m.CurseForgeHash == File["fileFingerprint"].ToObject<uint>()); if (ModFile is null) continue; foreach (var Address in ResourceVersion.ParseCurseForgeDownloadUrls(File["downloadUrl"].ToString())) Loader.Output.AddIntoValueCollection(ModFile, Address); } Logger.Info($"从 CurseForge 获取到 {CurseForgeRaw.Count} 个本地资源项的对应信息"); } catch (Exception ex) { Logger.Warn(ex, "从 CurseForge 获取本地 Mod 信息失败"); FailedExceptions.Add(ex); } finally { EndedThreadCount += 1; Loader.Progress += 0.45d; } }, "CurseForge - " + LoaderName); // Modrinth 上传模式下，不从 CurseForge 获取信息

            // 等待线程结束
            while (EndedThreadCount != 2)
            {
                if (Loader.IsCanceled)
                    return;
                Thread.Sleep(10);
            }

            // 若失败，确认是否继续
            if (FailedExceptions.Count == 1)
            {
                if (ModMain.MyMsgBox("联网获取部分文件信息失败，是否继续导出？" + Constants.vbCrLf + Constants.vbCrLf + "若继续，无法获取信息的文件将被直接打包。" + Constants.vbCrLf + "由于二次分发可能违反使用协议，请尽量不要公开发布导出的整合包！", "部分文件信息获取失败", "继续", "取消") == 2)
                    throw FailedExceptions.First();
            }
            else if (FailedExceptions.Count > 1)
            {
                if (ModMain.MyMsgBox("联网获取文件信息失败，是否继续导出？" + Constants.vbCrLf + Constants.vbCrLf + "若继续，所有文件都将被直接打包。" + Constants.vbCrLf + "由于二次分发可能违反使用协议，请尽量不要公开发布导出的整合包！", "文件信息获取失败", "继续", "取消") == 2)
                    throw FailedExceptions.First();
            }
        })
            {
                Show = CheckHostedAssets,
                ProgressWeight = CheckHostedAssets ? 2d : 0.01d
            });

            #endregion

            #region 生成压缩包

            Loaders.Add(new ModLoader.LoaderTask<Dictionary<LocalResourceFile, List<string>>, int>("生成压缩包", (Loader) =>
        {
            // 整理文件列表
            var Files = new JArray();
            foreach (var Pair in Loader.Input)
            {
                var ModFile = Pair.Key;
                Files.Add(new JObject() { { "path", PathUtils.RemoveExtendedPrefix(ModFile.File.FullName).AfterFirst(OverridesFolder).Replace(@"\", "/") }, { "hashes", new JObject() { { "sha1", ModFile.ModrinthHash }, { "sha512", CryptographyUtils.ComputeFileHash(ModFile.File.FullName, CryptographyUtils.HashMethod.Sha512) } } }, { "downloads", new JArray(Pair.Value.OrderBy(u => u.Contains("modrinth.com"))) }, { "fileSize", ModFile.File.Length } }); // 不优先选择 Modrinth
                ModFile.File.Delete();
            }
            Loader.Progress = 0.2d;
            // 导出最终 JSON 文件
            var Dependencies = new JObject() { { "minecraft", TargetInstance.Version.VanillaName } };
            if (TargetInstance.Version.ModLoaders.HasFlagF(ModLoaders.Forge))
                Dependencies.Add("forge", TargetInstance.Version.ModLoaderVersions[ModLoaders.Forge]);
            if (TargetInstance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
                Dependencies.Add("fabric-loader", TargetInstance.Version.ModLoaderVersions[ModLoaders.Fabric]);
            if (TargetInstance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
                Dependencies.Add("neoforge", TargetInstance.Version.ModLoaderVersions[ModLoaders.NeoForge]);
            var ResultJson = new JObject() { { "game", "minecraft" }, { "formatVersion", 1 }, { "versionId", PackVersion }, { "name", PackName }, { "summary", TargetInstance.Info }, { "files", Files }, { "dependencies", Dependencies } };
            FileUtils.Write(CacheFolder + @"modpack\modrinth.index.json", ResultJson.ToString(Formatting.Indented));
            // 打包
            if (IncludePCL)
            {
                // 首次压缩整合包
                FileUtils.CreateZipFromDirectory(CacheFolder + "modpack.mrpack", CacheFolder + @"modpack\");
                Loader.Progress = 0.5d;
                DirectoryUtils.Delete(CacheFolder + @"modpack\");
                Loader.Progress = 0.6d;
                // 二次压缩整合包
                FileUtils.CreateZipFromDirectory(PackPath, CacheFolder);
                Loader.Progress = 0.9d;
            }
            else
            {
                // 直接压缩整合包
                FileUtils.CreateZipFromDirectory(PackPath, CacheFolder + @"modpack\");
                Loader.Progress = 0.8d;
            }
            ModBase.OpenExplorer(PackPath);
        })
            {
                ProgressWeight = 6d
            });

            #endregion

            // 启动
            var MainLoader = new ModLoader.LoaderCombo<string>(LoaderName, Loaders) { OnStateChanged = ModDownloadLib.LoaderStateChangedHintOnly };
            MainLoader.Start();
            ModLoader.LoaderTaskbarAdd(MainLoader);
            ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
            ModMain.FrmMain.BtnExtraDownload.Ribble();
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.TaskManager);
        }

        #endregion

        private void RefreshJavaInfo()
        {
            int JavaType = Settings.Get<int>("VersionArgumentJavaV2", PageInstanceLeft.Instance);
            var JavaWorker = PageInstanceLeft.CurrentJavaWorker;
            // 导出选项
            var JavaToExport = JavaType == 2 && JavaWorker.LastSucceeded && !JavaWorker.Running ? JavaWorker.LastResult : null;
            if (JavaToExport is null)
            {
                this.CheckOptionsJava.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.CheckOptionsJava.Visibility = Visibility.Visible;
                this.CheckOptionsJava.Inlines.Clear();
                this.CheckOptionsJava.Inlines.Add(new Run(this.GetExportOption(this.CheckOptionsJava).Title));
                this.CheckOptionsJava.Inlines.Add(new Run("   " + $"Java {JavaToExport.Version.Major} ({JavaToExport.Version})：{PathUtils.AddSlashSuffix(PathUtils.RemoveLastPart(JavaToExport.Folder.Replace(PathUtils.RemoveLastPart(PathUtils.RemoveLastPart(PageInstanceLeft.Instance.PathVersion)), ""))).TrimStart('\\')}") { Foreground = ModSecret.ColorGray5 });
            }
            // 警告条
            if (JavaType == 3)
            {
                this.BtnExport.IsEnabled = true;
                this.HintJava.Visibility = Visibility.Visible;
                this.HintJava.Theme = MyHint.Themes.Yellow;
                this.HintJava.Text = $"该版本设置要求 {ModBase.vbLQ}使用指定的 Java{ModBase.vbRQ}，这在导出后很可能会失效。建议修改为 {ModBase.vbLQ}使用版本文件夹中的 Java{ModBase.vbRQ}。";
            }
            else if (JavaType == 2 && JavaWorker.HasSucceeded && JavaWorker.LastResult is null)
            {
                this.BtnExport.IsEnabled = false;
                this.HintJava.Visibility = Visibility.Visible;
                this.HintJava.Theme = MyHint.Themes.Red;
                this.HintJava.Text = $"该版本设置要求 {ModBase.vbLQ}使用版本文件夹中的 Java{ModBase.vbRQ}，但版本文件夹中没能找到任何 Java。";
            }
            else
            {
                this.BtnExport.IsEnabled = true;
                this.HintJava.Visibility = Visibility.Collapsed;
            }
        }

        // 自动填写整合包名称
        private void TextExportName_GotFocus()
        {
            if (string.IsNullOrEmpty(this.TextExportName.Text))
            {
                this.TextExportName.Text = this.TextExportName.HintText;
                this.TextExportName.SelectionStart = this.TextExportName.Text.Length;
            }
        }

        private void CheckAdvancedInclude_Change(object sender, bool user)
        {
            // 勾选打包资源文件时，禁止开启仅 Modrinth 资源模式
            if (this.CheckAdvancedInclude.Checked)
                this.CheckAdvancedModrinth.Checked = false;
            this.CheckAdvancedModrinth.IsEnabled = !this.CheckAdvancedInclude.Checked;
        }
        private void CheckAdvancedModrinth_Change(object sender, bool user)
        {
            this.HintModrinthFormat.Visibility = (this.CheckAdvancedModrinth.Checked && this.CheckOptionsPcl.Checked).ToVisibility();
        }

    }
}